using Stride.Graphics;
using System;
using System.Collections.Generic;
using VL.Lib.Basics.Resources;

namespace VL.IO.NDI
{
    sealed class ImagePool : IDisposable
    {
        public static IResourceProvider<ImagePool> Get(ImageDescription description)
        {
            return ResourceProvider.NewPooledPerApp(description, x => new ImagePool(x));
        }

        private readonly Stack<Image> images = new Stack<Image>();
        private readonly ImageDescription description;
        private bool isDisposed;

        public ImagePool(ImageDescription description)
        {
            this.description = description;
        }

        public void Dispose()
        {
            isDisposed = true;

            while (images.Count > 0)
                images.Pop().Dispose();
        }

        public Image Rent()
        {
            lock (images)
            {
                if (images.Count > 0)
                    return images.Pop();
            }

            return Image.New(description);
        }

        public void Return(Image image)
        {
            if (isDisposed || image.Description != description)
            {
                image.Dispose();
                return;
            }

            lock (images)
            {
                images.Push(image);
            }
        }
    }
}
