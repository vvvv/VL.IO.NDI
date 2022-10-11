using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Text;
using VL.Core;
using VL.Lib.Basics.Imaging;
using VL.Lib.Basics.Resources;

namespace VL.IO.NDI
{
    public static class SkiaUtils
    {
        public static unsafe IResourceProvider<SKImage> ToSkia(this IResourceProvider<IImage> imageProvider)
        {
            return imageProvider.BindNew(image =>
            {
                var info = image.Info;
                var skInfo = info.ToSkImageInfo();
                var imageData = image.GetData();
                var handle = imageData.Bytes.Pin();
                var pixmap = new SKPixmap(skInfo, new IntPtr(handle.Pointer), imageData.ScanSize);
                return SKImage.FromPixels(pixmap, (a, b) =>
                {
                    handle.Dispose();
                    imageData.Dispose();
                });
            });
        }

        static SKImageInfo ToSkImageInfo(this ImageInfo info)
        {
            var colorType = info.Format.ToSkColorType();
            return new SKImageInfo(info.Width, info.Height, colorType, info.IsPremultipliedAlpha ? SKAlphaType.Premul : SKAlphaType.Unpremul);
        }

        static SKColorType ToSkColorType(this PixelFormat format)
        {
            switch (format)
            {
                case PixelFormat.R8:
                    return SKColorType.Gray8;
                case PixelFormat.R8G8B8X8:
                    return SKColorType.Rgb888x;
                case PixelFormat.R8G8B8A8:
                    return SKColorType.Rgba8888;
                case PixelFormat.B8G8R8A8:
                    return SKColorType.Bgra8888;
                case PixelFormat.R16G16B16A16F:
                    return SKColorType.RgbaF16;
                case PixelFormat.R32G32B32A32F:
                    return SKColorType.RgbaF32;
            }
            throw new UnsupportedPixelFormatException(format);
        }
    }
}
