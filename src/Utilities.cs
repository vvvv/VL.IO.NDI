using NewTek;
using System;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using VL.Lib.Basics.Imaging;

// Utility functions outside of the NDILib SDK itself,
// but useful for working with NDI from managed languages.

namespace VL.IO.NDI
{
    [SuppressUnmanagedCodeSecurity]
    public static partial class UTF
    {
        public static byte[] StringToUtf8(string managedString)
        {
            if (managedString is null)
                return default;

            int len = Encoding.UTF8.GetByteCount(managedString);

            byte[] buffer = new byte[len + 1];

            Encoding.UTF8.GetBytes(managedString, 0, managedString.Length, buffer, 0);

            return buffer;
        }

        // Length is optional, but recommended
        // This is all potentially dangerous
        public static unsafe string Utf8ToString(IntPtr nativeUtf8, int? length = null)
        {
            if (nativeUtf8 == IntPtr.Zero)
                return string.Empty;

            int len = 0;

            if (length.HasValue)
            {
                len = length.Value;
            }
            else
            {
                // try to find the terminator
                byte* ptr = (byte*)nativeUtf8.ToPointer();
                while (*(ptr++) != 0)
                {
                    ++len;
                }
            }

            return Encoding.UTF8.GetString((byte*)nativeUtf8.ToPointer(), len);
        }

    } // class NDILib

    internal static class NativeUtils
    {
        public static unsafe ref T NULL<T>()
            where T : struct
        {
            return ref Unsafe.AsRef<T>(IntPtr.Zero.ToPointer());
        }
    }

    static class ImageUtils
    {
        public static IntPtrImage ToImage(this NDIlib.video_frame_v2_t videoFrame)
        {
            return new IntPtrImage(
                pointer: videoFrame.p_data,
                size: videoFrame.line_stride_in_bytes * videoFrame.yres,
                info: new ImageInfo(
                    videoFrame.xres,
                    videoFrame.yres,
                    ToPixelFormat(videoFrame.FourCC),
                    isPremultipliedAlpha: false,
                    scanSize: videoFrame.line_stride_in_bytes,
                    originalFormat: videoFrame.FourCC.ToString()));

            static PixelFormat ToPixelFormat(NDIlib.FourCC_type_e fourCC)
            {
                switch (fourCC)
                {
                    case NDIlib.FourCC_type_e.FourCC_type_BGRA:
                        return PixelFormat.B8G8R8A8;
                    case NDIlib.FourCC_type_e.FourCC_type_BGRX:
                        return PixelFormat.B8G8R8;
                    case NDIlib.FourCC_type_e.FourCC_type_RGBA:
                        return PixelFormat.R8G8B8A8;
                    case NDIlib.FourCC_type_e.FourCC_type_RGBX:
                        return PixelFormat.R8G8B8;
                    default:
                        return PixelFormat.Unknown;    // TODO: need to handle other video formats which are currently unsupported by IImage
                }
            }
        }
    }
} // namespace NewTek.NDI
