﻿using Stride.Graphics;
using System;
using System.Collections.Generic;
using VL.Core;
using VL.Lib.Basics.Imaging;
using VL.Lib.Basics.Resources;

using StridePixelFormat = Stride.Graphics.PixelFormat;
using VLPixelFormat = VL.Lib.Basics.Imaging.PixelFormat;

namespace VL.IO.NDI
{
    public static class StrideUtils
    {
        public static unsafe IResourceProvider<Texture> ToTexture(this IResourceProvider<IImage> imageProvider, GraphicsDevice graphicsDevice)
        {
            return imageProvider.BindNew(image =>
            {
                var info = image.Info;
                using var imageData = image.GetData();
                using var memoryHandle = imageData.Bytes.Pin();
                var description = TextureDescription.New2D(info.Width, info.Height, ToPixelFormat(info.Format), usage: GraphicsResourceUsage.Immutable);
                return Texture.New(graphicsDevice, description, new DataBox(new IntPtr(memoryHandle.Pointer), info.ScanSize, info.ImageSize));
            });
        }

        static StridePixelFormat ToPixelFormat(VLPixelFormat format)
        {
            switch (format)
            {
                case VLPixelFormat.R8: return StridePixelFormat.R8_UNorm;
                case VLPixelFormat.R16: return StridePixelFormat.R16_UNorm;
                case VLPixelFormat.R32F: return StridePixelFormat.R32_Float;
                case VLPixelFormat.R8G8B8A8: return StridePixelFormat.R8G8B8A8_UNorm_SRgb;
                case VLPixelFormat.B8G8R8X8: return StridePixelFormat.B8G8R8X8_UNorm_SRgb;
                case VLPixelFormat.B8G8R8A8: return StridePixelFormat.B8G8R8A8_UNorm_SRgb;
                case VLPixelFormat.R16G16B16A16F: return StridePixelFormat.R16G16B16A16_Float;
                case VLPixelFormat.R32G32F: return StridePixelFormat.R32G32_Float;
                case VLPixelFormat.R32G32B32A32F: return StridePixelFormat.R32G32B32A32_Float;
            }
            throw new UnsupportedPixelFormatException(format);
        }
    }
}
