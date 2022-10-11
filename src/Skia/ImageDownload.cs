using SharpDX;
using SharpDX.Direct3D11;
using SkiaSharp;
using Stride.Core.Mathematics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Runtime.Remoting.Contexts;
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

        private readonly Queue<Texture2D> textureDownloads = new Queue<Texture2D>();
        private readonly Subject<IResourceProvider<IImage>> imageStream = new Subject<IResourceProvider<IImage>>();
        private readonly SerialDisposable imageSubscription = new SerialDisposable();
        private readonly SerialDisposable texturePoolSubscription = new SerialDisposable();
        private readonly RenderContext renderContext;
        private readonly EglContext eglContext;
        private readonly Device device;
        private Texture2D renderTarget;
        private EglSurface eglSurface;

        public ImageDownload()
        {
            renderContext = RenderContext.ForCurrentThread();
            eglContext = renderContext.EglContext;
            if (eglContext.Dislpay.TryGetD3D11Device(out var devicePtr))
                device = new SharpDX.Direct3D11.Device(devicePtr);
        }

        public IObservable<IResourceProvider<IImage>> Update(SKImage image)
        {
            if (image is null)
                return emptyObservable;

            // Not working :( Output is black
            //if (device != null)
            //    DownloadWithStagingTexture(image);
            //else
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
            };

            var stagingDescription = description;
            stagingDescription.CpuAccessFlags = CpuAccessFlags.Read | CpuAccessFlags.Write;
            stagingDescription.BindFlags = BindFlags.None;
            stagingDescription.Usage = ResourceUsage.Staging;

            var texturePool = GetTexturePool(device, stagingDescription);

            // Render into a temp target and request copy of it to staging texture
            {
                if (renderTarget is null || renderTarget.Description.Width != description.Width || renderTarget.Description.Height != description.Height)
                {
                    //eglSurface?.Dispose();
                    renderTarget?.Dispose();

                    renderTarget = new Texture2D(device, description);
                    //eglSurface = eglContext.CreateSurfaceFromClientBuffer(renderTarget.NativePointer);
                }
                using var eglSurface = eglContext.CreateSurfaceFromClientBuffer(renderTarget.NativePointer);

                using var rtv = new RenderTargetView(device, renderTarget);
                device.ImmediateContext.OutputMerger.SetRenderTargets(rtv);
                // Make the surface current (becomes default FBO)
                renderContext.MakeCurrent(eglSurface);

                // Setup a skia surface around the currently set render target
                //using var surface = CreateSkSurface(renderContext.SkiaContext, renderTarget);

                //// Render
                //var canvas = surface.Canvas;
                //canvas.Clear(SKColors.Red);
                //canvas.Flush();
                //canvas.DrawImage(skImage, 0f, 0f);

                //static void SimpleStupidTestRendering()
                //{
                NativeGles.glClearColor(1, 1, 0, 1);
                NativeGles.glClear(NativeGles.GL_COLOR_BUFFER_BIT);
                NativeGles.glFlush();
                //}
                //int i = 0;

                // Flush
                //surface.Flush();

                // Ensures surface gets released
                eglContext.MakeCurrent(default);


                var stagingTexture = texturePool.Rent();

                ((IUnknown)stagingTexture).AddReference();
                Trace.TraceInformation($"Before: {((IUnknown)stagingTexture).Release()}");

                device.ImmediateContext.CopyResource(renderTarget, stagingTexture);
                textureDownloads.Enqueue(stagingTexture);

                ((IUnknown)stagingTexture).AddReference();
                Trace.TraceInformation($"After: {((IUnknown)stagingTexture).Release()}");
            }

            ((IUnknown)renderTarget).AddReference();
            Trace.TraceInformation(((IUnknown)renderTarget).Release().ToString());

            // Download recently staged
            unsafe {
                var stagedTexture = textureDownloads.Peek();

                ((IUnknown)stagedTexture).AddReference();
                Trace.TraceInformation($"Staged: {((IUnknown)stagedTexture).Release()}");

                ((IUnknown)device).AddReference();
                Trace.TraceInformation($"Device: {((IUnknown)device).Release()}");

                var data = device.ImmediateContext.MapSubresource(stagedTexture, 0, MapMode.Read, textureDownloads.Count <= 4 ? MapFlags.DoNotWait : MapFlags.None);
                if (!data.IsEmpty)
                {
                    // Dequeue
                    textureDownloads.Dequeue();

                    //device.ImmediateContext.UnmapSubresource(stagedTexture, 0);
                    //texturePool.Return(stagedTexture);

                    // Setup the new image resource
                    var image = data.DataPointer.ToImage(data.SlicePitch, description.Width, description.Height, PixelFormat.B8G8R8A8, description.Format.ToString());
                    var v = Unsafe.Read<ColorBGRA>(data.DataPointer.ToPointer());
                    var imageProvider = ResourceProvider.Return(image, i =>
                    {
                        i.Dispose();
                        device.ImmediateContext.UnmapSubresource(stagedTexture, 0);
                        texturePool.Return(stagedTexture);
                    }).ShareInParallel();

                    // Subscribe to our own provider to ensure the image is returned if no one else is using it
                    imageSubscription.Disposable = imageProvider.GetHandle();

                    // Push it downstream
                    imageStream.OnNext(imageProvider);
                }
                else
                {
                    //device.ImmediateContext.Flush();
                }
            }

            SKSurface CreateSkSurface(GRContext context, Texture2D texture)
            {
                var colorType = SKColorType.Bgra8888;
                NativeGles.glGetIntegerv(NativeGles.GL_FRAMEBUFFER_BINDING, out var framebuffer);
                NativeGles.glGetIntegerv(NativeGles.GL_STENCIL_BITS, out var stencil);
                NativeGles.glGetIntegerv(NativeGles.GL_SAMPLES, out var samples);
                var maxSamples = context.GetMaxSurfaceSampleCount(colorType);
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
                    context,
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
