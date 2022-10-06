using Stride.Graphics;
using Stride.Rendering;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Text;
using VL.Lib.Basics.Resources;

namespace VL.IO.NDI
{
    public class ImageDownloader : RendererBase
    {
        private readonly Queue<Texture> textureDownloads = new Queue<Texture>();
        private readonly Stack<Texture> texturePool = new Stack<Texture>();
        private readonly Stack<Image> imagePool = new Stack<Image>();
        private readonly SerialDisposable imageProviderSubscription = new SerialDisposable();
        private TextureDescription textureDescription;

        public Texture Texture { get; set; }

        public IResourceProvider<Image> ImageProvider { get; private set; }

        /// <inheritdoc />
        protected override void DrawCore(RenderDrawContext context)
        {
            // Request copy
            {
                var texture = Texture;
                if (texture is null)
                    return;

                if (texture.Description != textureDescription)
                {
                    textureDescription = texture.Description;
                    DisposeResources();
                }

                var stagingTexture = RentStagingTexture(texture);
                context.CommandList.Copy(texture, stagingTexture);
                textureDownloads.Enqueue(stagingTexture);
            }

            // Try to download
            {
                var texture = textureDownloads.Peek();
                var image = RentImage(texture);
                var pixelBuffer = image.PixelBuffer[0, 0];
                var dataPointer = new DataPointer(pixelBuffer.DataPointer, pixelBuffer.BufferStride);
                if (texture.GetData(context.CommandList, texture, dataPointer, 0, 0, doNotWait: true))
                {
                    ReturnTexture(textureDownloads.Dequeue());

                    ImageProvider = ResourceProvider.Return(image, i => ReturnImage(i))
                        .SharePooled(delayDisposalInMilliseconds: 0, resetResource: null);

                    // Subscribe to our own provider to ensure the image is returned if no one else is using it
                    imageProviderSubscription.Disposable = ImageProvider.GetHandle();
                }
                else
                {
                    ReturnImage(image);
                }
            }
        }

        private void DisposeResources()
        {
            DisposeTextureDownloads();
            DisposePool(texturePool);
            DisposePool(imagePool);
        }

        private Texture RentStagingTexture(Texture texture)
        {
            if (texturePool.Count > 0)
                return texturePool.Pop();
            return texture.ToStaging();
        }

        private Image RentImage(Texture texture)
        {
            if (imagePool.Count > 0)
                return imagePool.Pop();
            return Image.New(texture.Description);
        }

        private void ReturnTexture(Texture texture)
        {
            if (texture.Description == textureDescription)
                texturePool.Push(texture);
            else
                texture.Dispose();
        }

        private void ReturnImage(Image image)
        {
            ImageDescription imageDescription = textureDescription;
            if (image.Description == imageDescription)
                imagePool.Push(image);
            else
                image.Dispose();
        }

        private void DisposeTextureDownloads()
        {
            while (textureDownloads.Count > 0)
                textureDownloads.Dequeue().Dispose();
        }

        private static void DisposePool<T>(Stack<T> pool) where T : IDisposable
        {
            while (pool.Count > 0)
                pool.Pop().Dispose();
        }

        protected override void Destroy()
        {
            DisposeResources();
            base.Destroy();
        }
    }
}
