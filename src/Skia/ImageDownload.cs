using SharpDX;
using SharpDX.Direct3D11;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Threading;
using VL.Lib.Basics.Imaging;
using VL.Lib.Basics.Resources;
using VL.Skia;
using VL.Skia.Egl;

namespace VL.IO.NDI
{
    public sealed class ImageDownload : IDisposable
    {
        private static readonly IRefCounter<SKImage> refCounter = RefCounting.GetRefCounter<SKImage>();
        private static readonly IObservable<IResourceProvider<IImage>> emptyObservable = Observable.Empty<IResourceProvider<IImage>>();

        // 2021.4.11 comes with crucial fix that
        private static readonly bool isFastDownloadSupported = VL.Core.VersionUtils.ParseLanguageVersion(VL.Core.VersionUtils.FullVersionString) >= new Version(2021, 4, 11);

        private readonly SynchronizationContext synchronizationContext = SynchronizationContext.Current;
        private readonly Queue<Texture2D> textureDownloads = new Queue<Texture2D>();
        private readonly Subject<IResourceProvider<IImage>> imageStream = new Subject<IResourceProvider<IImage>>();
        private readonly SerialDisposable imageSubscription = new SerialDisposable();
        private readonly SerialDisposable texturePoolSubscription = new SerialDisposable();
        private readonly RenderContext renderContext;

        // Nullable
        private readonly Device device;
        private Texture2D renderTarget;
        private EglSurface eglSurface;
        private SKSurface surface;

        public ImageDownload()
        {
            renderContext = RenderContext.ForCurrentThread();
            var eglContext = renderContext.EglContext;
            if (eglContext.Dislpay.TryGetD3D11Device(out var devicePtr))
                device = new Device(devicePtr);
        }

        public IObservable<IResourceProvider<IImage>> Update(SKImage image)
        {
            if (image is null)
                return emptyObservable;

            // Not working :( Output is black
            if (isFastDownloadSupported && device != null)
                DownloadWithStagingTexture(image);
            else
                DownloadWithRasterImage(image);

            return imageStream;
        }

        private void DownloadWithStagingTexture(SKImage skImage)
        {
            // Fast path
            // - Create render texture
            // - Make Skia surface out of it
            // - Draw image into surface
            // - Create staging texture
            // - Copy render texture into staging texture

            var description = new Texture2DDescription()
            {
                Width = skImage.Width,
                Height = skImage.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                CpuAccessFlags = CpuAccessFlags.None,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                OptionFlags = ResourceOptionFlags.Shared
            };

            var stagingDescription = description;
            stagingDescription.CpuAccessFlags = CpuAccessFlags.Read;
            stagingDescription.BindFlags = BindFlags.None;
            stagingDescription.Usage = ResourceUsage.Staging;
            stagingDescription.OptionFlags = ResourceOptionFlags.None;

            var texturePool = GetTexturePool(device, stagingDescription);

            // Render into a temp target and request copy of it to staging texture
            {
                if (renderTarget is null || renderTarget.Description.Width != description.Width || renderTarget.Description.Height != description.Height)
                {
                    surface?.Dispose();
                    eglSurface?.Dispose();
                    renderTarget?.Dispose();

                    renderTarget = new Texture2D(device, description);

                    //eglSurface = eglContext.CreateSurfaceFromClientBuffer(renderTarget.NativePointer);
                    using var sharedResource = renderTarget.QueryInterface<SharpDX.DXGI.Resource>();
                    eglSurface = renderContext.EglContext.CreateSurfaceFromSharedHandle(description.Width, description.Height, sharedResource.SharedHandle);

                    // Setup a skia surface around the currently set render target
                    surface = CreateSkSurface(renderContext, eglSurface, renderTarget);
                }

                // Render
                var canvas = surface.Canvas;
                canvas.DrawImage(skImage, 0f, 0f);

                var stagingTexture = texturePool.Rent();
                device.ImmediateContext.CopyResource(renderTarget, stagingTexture);
                textureDownloads.Enqueue(stagingTexture);
            }

            // Download recently staged
            {
                var stagedTexture = textureDownloads.Peek();
                var waitOnGPU = textureDownloads.Count >= 4;
                var data = device.ImmediateContext.MapSubresource(stagedTexture, 0, MapMode.Read, waitOnGPU ? MapFlags.None : MapFlags.DoNotWait);
                if (!data.IsEmpty)
                {
                    // Dequeue
                    textureDownloads.Dequeue();

                    // Setup the new image resource
                    var imageInfo = new ImageInfo(description.Width, description.Height, PixelFormat.B8G8R8A8, isPremultipliedAlpha: true, data.RowPitch, description.Format.ToString());
                    var image = new IntPtrImage(data.DataPointer, data.SlicePitch, imageInfo);
                    var imageProvider = ResourceProvider.Return(image, ReleaseImage).ShareInParallel();

                    // Subscribe to our own provider to ensure the image is returned if no one else is using it
                    imageSubscription.Disposable = imageProvider.GetHandle();

                    // Push it downstream
                    imageStream.OnNext(imageProvider);

                    void ReleaseImage(IntPtrImage i)
                    {
                        if (SynchronizationContext.Current != synchronizationContext)
                            synchronizationContext.Post(x => ReleaseImage((IntPtrImage)x), i);
                        else
                        {
                            i.Dispose();
                            device.ImmediateContext.UnmapSubresource(stagedTexture, 0);
                            texturePool.Return(stagedTexture);
                        }
                    }
                }
            }

            SKSurface CreateSkSurface(RenderContext context, EglSurface eglSurface, Texture2D texture)
            {
                var eglContext = context.EglContext;

                var colorType = SKColorType.Bgra8888;

                uint textureId = 0u;
                NativeGles.glGenTextures(1, ref textureId);
                NativeGles.glBindTexture(NativeGles.GL_TEXTURE_2D, textureId);
                var result = NativeEgl.eglBindTexImage(eglContext.Dislpay, eglSurface, NativeEgl.EGL_BACK_BUFFER);
                if (result == 0)
                    throw new Exception("Failed to bind surface");

                uint fbo = 0u;
                NativeGles.glGenFramebuffers(1, ref fbo);
                NativeGles.glBindFramebuffer(NativeGles.GL_FRAMEBUFFER, fbo);
                glFramebufferTexture2D(NativeGles.GL_FRAMEBUFFER, NativeGles.GL_COLOR_ATTACHMENT0, NativeGles.GL_TEXTURE_2D, textureId, 0);

                NativeGles.glGetIntegerv(NativeGles.GL_FRAMEBUFFER_BINDING, out var framebuffer);
                NativeGles.glGetIntegerv(NativeGles.GL_STENCIL_BITS, out var stencil);
                NativeGles.glGetIntegerv(NativeGles.GL_SAMPLES, out var samples);
                var maxSamples = context.SkiaContext.GetMaxSurfaceSampleCount(colorType);
                if (samples > maxSamples)
                    samples = maxSamples;

                var glInfo = new GRGlFramebufferInfo(
                    fboId: (uint)framebuffer,
                    format: colorType.ToGlSizedFormat());

                using var renderTarget = new GRBackendRenderTarget(
                    width: texture.Description.Width,
                    height: texture.Description.Height,
                    sampleCount: samples,
                    stencilBits: stencil,
                    glInfo: glInfo);

                return SKSurface.Create(
                    context.SkiaContext,
                    renderTarget,
                    GRSurfaceOrigin.TopLeft,
                    colorType,
                    colorspace: SKColorSpace.CreateSrgb());
            }

            D3D11TexturePool GetTexturePool(Device graphicsDevice, in Texture2DDescription description )
            {
                return D3D11TexturePool.Get(graphicsDevice, in description)
                    .Subscribe(texturePoolSubscription);
            }

            [DllImport("libGLESv2.dll")]
            static extern void glFramebufferTexture2D(uint target, uint attachment, uint textarget, uint texture, int level);
        }

        private void DownloadWithRasterImage(SKImage skImage)
        {
            // Slow path through ToRasterImage
            var provider = GetProvider(skImage);
            if (provider != null)
                imageStream.OnNext(provider.BindNew(img => img.ToImage()));

            imageSubscription.Disposable = provider?.GetHandle();
        }

        public void Dispose()
        {
            imageStream.Dispose();
            imageSubscription.Dispose();
            texturePoolSubscription.Dispose();

            while (textureDownloads.Count > 0)
                textureDownloads.Dequeue().Dispose();

            renderTarget?.Dispose();
            surface?.Dispose();
            eglSurface?.Dispose();
            device?.Dispose();

            renderContext.Dispose();
        }

        static IResourceProvider<SKImage> GetProvider(SKImage image)
        {
            var rasterImage = image?.ToRasterImage(ensurePixelData: true);
            if (rasterImage is null)
                return null;

            if (rasterImage != image)
                refCounter.Init(rasterImage);
            else
                refCounter.AddRef(rasterImage);

            return ResourceProvider.Return(rasterImage, refCounter.Release);
        }
    }
}
