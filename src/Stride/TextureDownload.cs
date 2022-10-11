using Stride.Graphics;
using Stride.Rendering;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Disposables;
using VL.Core;
using VL.Lib.Basics.Imaging;
using VL.Lib.Basics.Resources;
using StridePixelFormat = Stride.Graphics.PixelFormat;
using VLPixelFormat = VL.Lib.Basics.Imaging.PixelFormat;

namespace VL.IO.NDI
{
    public sealed class TextureDownload : RendererBase
    {
        private readonly Queue<Texture> textureDownloads = new Queue<Texture>();
        private readonly Stopwatch stopwatch = new Stopwatch();
        private readonly ServiceRegistry serviceRegistry;
        private readonly CompositeDisposable subscriptions;
        private readonly SerialDisposable texturePoolSubscription;
        private readonly SerialDisposable imagePoolSubscription;
        private readonly SerialDisposable imageSubscription;

        public TextureDownload()
        {
            serviceRegistry = ServiceRegistry.Current;
            subscriptions = new CompositeDisposable()
            {
                (texturePoolSubscription = new SerialDisposable()),
                (imagePoolSubscription = new SerialDisposable()),
                (imageSubscription = new SerialDisposable())
            };
        }

        public Texture Texture { get; set; }

        public IResourceProvider<IImage> ImageProvider { get; private set; }

        public bool DownloadAsync { get; set; } = true;

        public TimeSpan ElapsedTime { get; private set; }

        /// <inheritdoc />
        protected override void DrawCore(RenderDrawContext context)
        {
            var texture = Texture;
            if (texture is null)
            {
                texturePoolSubscription.Disposable = null;
                imagePoolSubscription.Disposable = null;
                imageSubscription.Disposable = null;
                return;
            }

            // Workaround: Re-install the service registry - should be done by vvvv itself in the render callback
            using var _ = serviceRegistry.MakeCurrentIfNone();

            stopwatch.Restart();

            var texturePool = GetTexturePool(context.GraphicsDevice, texture);

            {
                // Request copy
                var stagingTexture = texturePool.Rent();
                context.CommandList.Copy(Texture, stagingTexture);
                textureDownloads.Enqueue(stagingTexture);
            }

            if (!DownloadAsync)
            {
                // Drain the queue
                while (textureDownloads.Count > 1)
                    textureDownloads.Dequeue().Dispose();
            }

            {
                // Download recently staged
                var stagedTexture = textureDownloads.Peek();
                var imagePool = GetImagePool(stagedTexture);
                var image = imagePool.Rent();
                var pixelBuffer = image.PixelBuffer[0, 0];
                var dataPointer = new DataPointer(pixelBuffer.DataPointer, pixelBuffer.BufferStride);
                if (stagedTexture.GetData(context.CommandList, stagedTexture, dataPointer, 0, 0, doNotWait: DownloadAsync))
                {
                    // Dequeue and return to the pool
                    texturePool.Return(textureDownloads.Dequeue());

                    // Setup the new image resource
                    ImageProvider = ResourceProvider.Return(image, i => imagePool.Return(i))
                        .BindNew(i => i.DataPointer.ToImage(i.TotalSizeInBytes, i.Description.Width, i.Description.Height, ToPixelFormat(i.Description.Format), i.Description.Format.ToString()))
                        .SharePooled(delayDisposalInMilliseconds: 0, resetResource: null);

                    // Subscribe to our own provider to ensure the image is returned if no one else is using it
                    imageSubscription.Disposable = ImageProvider.GetHandle();

                    ElapsedTime = stopwatch.Elapsed;
                }
                else
                {
                    imagePool.Return(image);
                }
            }

            static VLPixelFormat ToPixelFormat(StridePixelFormat format)
            {
                switch (format)
                {
                    case StridePixelFormat.R8_UNorm: return VLPixelFormat.R8;
                    case StridePixelFormat.R16_UNorm: return VLPixelFormat.R16;
                    case StridePixelFormat.R32_Float: return VLPixelFormat.R32F;
                    case StridePixelFormat.R8G8B8A8_UNorm: return VLPixelFormat.R8G8B8A8;
                    case StridePixelFormat.R8G8B8A8_UNorm_SRgb: return VLPixelFormat.R8G8B8A8;
                    case StridePixelFormat.B8G8R8X8_UNorm: return VLPixelFormat.B8G8R8X8;
                    case StridePixelFormat.B8G8R8X8_UNorm_SRgb: return VLPixelFormat.B8G8R8X8;
                    case StridePixelFormat.B8G8R8A8_UNorm: return VLPixelFormat.B8G8R8A8;
                    case StridePixelFormat.B8G8R8A8_UNorm_SRgb: return VLPixelFormat.B8G8R8A8;
                    case StridePixelFormat.R16G16B16A16_Float: return VLPixelFormat.R16G16B16A16F;
                    case StridePixelFormat.R32G32_Float: return VLPixelFormat.R32G32F;
                    case StridePixelFormat.R32G32B32A32_Float: return VLPixelFormat.R32G32B32A32F;
                    default:
                        throw new Exception("Unsupported pixel format");
                }
            }
        }

        private TexturePool GetTexturePool(GraphicsDevice graphicsDevice, Texture texture)
        {
            return TexturePool.Get(graphicsDevice, texture.Description.ToStagingDescription())
                .Subscribe(texturePoolSubscription);
        }

        private ImagePool GetImagePool(Texture texture)
        {
            return ImagePool.Get(texture.Description)
                .Subscribe(imagePoolSubscription);
        }

        protected override void Destroy()
        {
            while (textureDownloads.Count > 0)
                textureDownloads.Dequeue().Dispose();

            subscriptions.Dispose();

            base.Destroy();
        }
    }
}
