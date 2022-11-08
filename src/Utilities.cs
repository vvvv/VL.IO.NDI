using ExCSS;
using Microsoft.Toolkit.HighPerformance;
using NewTek;
using System;
using System.Buffers;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using VL.Lib.Basics.Imaging;

// Utility functions outside of the NDILib SDK itself,
// but useful for working with NDI from managed languages.

namespace VL.IO.NDI
{
    static partial class Utils
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

        public static unsafe ref T NULL<T>()
            where T : struct
        {
            return ref Unsafe.AsRef<T>(IntPtr.Zero.ToPointer());
        }

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

        public static unsafe IMemoryOwner<float> GetPlanarBuffer(ref NDIlib.audio_frame_v2_t audioFrame)
        {
            var length = audioFrame.channel_stride_in_bytes / sizeof(float) * audioFrame.no_channels;
            return new UnmanagedMemoryManager<float>((float*)audioFrame.p_data.ToPointer(), length, isOwner: false);
        }

        public static unsafe NDIlib.audio_frame_v2_t ToV2(ref NDIlib.audio_frame_v3_t audioFrame)
        {
            return new NDIlib.audio_frame_v2_t()
            {
                channel_stride_in_bytes = audioFrame.channel_stride_in_bytes,
                no_channels = audioFrame.no_channels,
                no_samples = audioFrame.no_samples,
                p_data = audioFrame.p_data,
                p_metadata = audioFrame.p_metadata,
                sample_rate = audioFrame.sample_rate,
                timecode = audioFrame.timecode,
                timestamp = audioFrame.timestamp
            };
        }

        public static unsafe IMemoryOwner<float> GetInterleavedBuffer(ref NDIlib.audio_frame_v2_t audioFrame)
        {
            var bufferOwner = MemoryPool<float>.Shared.Rent(audioFrame.no_samples * audioFrame.no_channels);

            using var handle = bufferOwner.Memory.Pin();
            var interleavedFrame = new NDIlib.audio_frame_interleaved_32f_t()
            {
                no_channels = audioFrame.no_samples,
                no_samples = audioFrame.no_samples,
                sample_rate = audioFrame.sample_rate,
                timecode = audioFrame.timecode,
                p_data = new IntPtr(handle.Pointer)
            };
            NDIlib.util_audio_to_interleaved_32f_v2(ref audioFrame, ref interleavedFrame);

            return bufferOwner;
        }
    }
}
