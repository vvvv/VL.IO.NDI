using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;
using NewTek;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using VL.Core;
using VL.Lib.Basics.Video;

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

        public static(IDisposable memoryOwner, VideoFrame videoFrame) CreateVideoFrame(ref NDIlib.video_frame_v2_t nativeVideoFrame)
        {
            switch (nativeVideoFrame.FourCC)
            {
                case NDIlib.FourCC_type_e.FourCC_type_RGBA: return CreateVideoFrame<RgbaPixel>(ref nativeVideoFrame);
                case NDIlib.FourCC_type_e.FourCC_type_RGBX: return CreateVideoFrame<RgbxPixel>(ref nativeVideoFrame);
                case NDIlib.FourCC_type_e.FourCC_type_BGRA: return CreateVideoFrame<BgraPixel>(ref nativeVideoFrame);
                case NDIlib.FourCC_type_e.FourCC_type_BGRX: return CreateVideoFrame<BgrxPixel>(ref nativeVideoFrame);
                default:
                    throw new Exception("Unsupported pixel format");
            }
        }

        public static(IMemoryOwner<T> memoryOwner, VideoFrame<T> videoFrame) CreateVideoFrame<T>(ref NDIlib.video_frame_v2_t nativeVideoFrame)
            where T : unmanaged, IPixel
        {
            var lengthInBytes = nativeVideoFrame.line_stride_in_bytes * nativeVideoFrame.yres;
            var memoryOwner = new UnmanagedMemoryManager<T>(nativeVideoFrame.p_data, lengthInBytes);

            var videoFrame = new VideoFrame<T>(
                memoryOwner.Memory.AsMemory2D(nativeVideoFrame.yres, nativeVideoFrame.xres),
                Utf8ToString(nativeVideoFrame.p_metadata),
                (nativeVideoFrame.frame_rate_N, nativeVideoFrame.frame_rate_D));

            return (memoryOwner, videoFrame);
        }

        public static unsafe IMemoryOwner<float> GetPlanarBuffer(ref NDIlib.audio_frame_v2_t audioFrame)
        {
            var lengthInBytes = audioFrame.channel_stride_in_bytes * audioFrame.no_channels;
            return new UnmanagedMemoryManager<float>(audioFrame.p_data, lengthInBytes);
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
            var bufferOwner = MemoryOwner<float>.Allocate(audioFrame.no_samples * audioFrame.no_channels);

            fixed (float* pointer = bufferOwner.Span)
            {
                var interleavedFrame = new NDIlib.audio_frame_interleaved_32f_t()
                {
                    no_channels = audioFrame.no_channels,
                    no_samples = audioFrame.no_samples,
                    sample_rate = audioFrame.sample_rate,
                    timecode = audioFrame.timecode,
                    p_data = new IntPtr(pointer)
                };
                NDIlib.util_audio_to_interleaved_32f_v2(ref audioFrame, ref interleavedFrame);
            }

            return bufferOwner;
        }
    }
}
