using SharpDX;
using SharpDX.Direct3D11;
using SkiaSharp;
using Stride.Core.Mathematics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using VL.Core;
using VL.Lib.Basics.Imaging;
using VL.Lib.Basics.Resources;
using VL.Skia;
using VL.Skia.Egl;
using MapMode = SharpDX.Direct3D11.MapMode;

using EGLDisplay = System.IntPtr;
using EGLContext = System.IntPtr;
using EGLConfig = System.IntPtr;
using EGLSurface = System.IntPtr;

namespace VL.IO.NDI
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            var serviceRegistry = new ServiceRegistry();
            serviceRegistry.MakeCurrent();

            var form = new Form();
            var control = new SkiaGLControl()
            {
                Dock = DockStyle.Fill
            };
            form.Controls.Add(control);

            var renderContext = default(RenderContext);
            var eglContext = default(EglContext);
            Device device = default;
            Texture2D renderTarget = default;
            var textureDownloads = new Queue<Texture2D>();
            var imageSubscription = new SerialDisposable();
            var texturePoolSubscription = new SerialDisposable();
            var image = default(SKImage);

            control.OnRender += Control_OnRender;
            Application.Run(form);

            void Control_OnRender(CallerInfo obj)
            {
                if (image is null)
                {
                    renderContext = RenderContext.ForCurrentThread();
                    eglContext = renderContext.EglContext;

                    uint textureId = 0u;
                    NativeGles.glGenTextures(1, ref textureId);

                    image = SKImage.FromBitmap(SKBitmap.Decode(@"C:\Users\elias\Downloads\959295.jpg")).ToTextureImage(renderContext.SkiaContext);
                    if (eglContext.Dislpay.TryGetD3D11Device(out var devicePtr))
                        device = new SharpDX.Direct3D11.Device(devicePtr);
                }

                obj.Canvas.Clear(SKColors.Green);

                DownloadWithStagingTexture(image);
            }

            void DownloadWithStagingTexture(SKImage skImage)
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

                    using var eglSurface = CreateSurfaceFromClientBuffer(eglContext, renderTarget.NativePointer, NativeEgl.EGL_TEXTURE_RGBA, NativeEgl.EGL_TEXTURE_2D);

                    //using var rtv = new RenderTargetView(device, renderTarget);
                    //device.ImmediateContext.OutputMerger.SetRenderTargets(rtv);
                    // Make the surface current (becomes default FBO)
                    //renderContext.MakeCurrent(eglSurface);

                    // Setup a skia surface around the currently set render target
                    using var surface = CreateSkSurface(renderContext.SkiaContext, eglSurface, renderTarget);

                    // Render
                    var canvas = surface.Canvas;
                    canvas.Clear(SKColors.Red);
                    //canvas.Flush();
                    //canvas.DrawImage(skImage, 0f, 0f);

                    // Flush
                    surface.Flush();

                    // Ensures surface gets released
                    //eglContext.MakeCurrent(default);


                    var stagingTexture = texturePool.Rent();
                    device.ImmediateContext.CopyResource(renderTarget, stagingTexture);
                    textureDownloads.Enqueue(stagingTexture);
                }

                // Download recently staged
                unsafe
                {
                    var stagedTexture = textureDownloads.Peek();
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
                        //imageStream.OnNext(imageProvider);
                    }
                    else
                    {
                        //device.ImmediateContext.Flush();
                    }
                }

                SKSurface CreateSkSurface(GRContext context, EglSurface eglSurface, Texture2D texture)
                {
                    var colorType = SKColorType.Bgra8888;

                    uint textureId = 0u;
                    NativeGles.glGenTextures(1, ref textureId);
                    Debug.Assert(glGetError() == 0);
                    NativeGles.glBindTexture(NativeGles.GL_TEXTURE_2D, textureId);
                    NativeEgl.eglBindTexImage(eglContext.Dislpay, eglSurface, NativeEgl.EGL_BACK_BUFFER);

                    uint fbo = 0u;
                    NativeGles.glGenFramebuffers(1, ref fbo);
                    NativeGles.glBindFramebuffer(NativeGles.GL_FRAMEBUFFER, fbo);
                    glFramebufferTexture2D(NativeGles.GL_FRAMEBUFFER, NativeGles.GL_COLOR_ATTACHMENT0, NativeGles.GL_TEXTURE_2D, textureId, 0);

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

                D3D11TexturePool GetTexturePool(Device graphicsDevice, in Texture2DDescription description)
                {
                    return D3D11TexturePool.Get(graphicsDevice, in description)
                        .Subscribe(texturePoolSubscription);
                }
            }
        }

        [DllImport("libGLESv2.dll")]
        static extern void glFramebufferTexture2D(uint target, uint attachment, uint textarget, uint texture, int level);

        [DllImport("libGLESv2.dll")]
        static extern uint glGetError();

        [DllImport("libegl.dll")]
        static extern uint eglQueryAPI();

        static EglSurface CreateSurfaceFromClientBuffer(EglContext context, IntPtr buffer, int eglTextureFormat, int eglTextureTarget)
        {
            if (buffer == default)
                throw new ArgumentNullException(nameof(buffer));

            int[] surfaceAttributes = new[]
            {
                         NativeEgl.EGL_TEXTURE_FORMAT, eglTextureFormat, NativeEgl.EGL_TEXTURE_TARGET,
                         eglTextureTarget,   NativeEgl.EGL_NONE,         NativeEgl.EGL_NONE,
                    };

            var config = (IntPtr)(typeof(EglContext).GetField("config", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(context));
            var surface = NativeEgl.eglCreatePbufferFromClientBuffer(context.Dislpay, NativeEgl.EGL_D3D_TEXTURE_ANGLE, buffer, config, surfaceAttributes);
            if (surface == default)
                throw new Exception("Failed to create EGL surface");

            return new EglSurface(context.Dislpay, surface);
        }
    }
}
