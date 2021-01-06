//using NAudio.Wave;
using NewTek;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Reactive.Subjects;

using VL.Lib.Basics.Imaging;
using ImagingPixelFormat = VL.Lib.Basics.Imaging.PixelFormat;


namespace VL.IO.NDI
{
    /// <summary>
    /// If you do not use this control, you can remove this file
    /// and remove the dependency on naudio.
    /// Alternatively you can also remove any naudio related entries
    /// and use it for video only, but don't forget that you will still need
    /// to free any audio frames received.
    /// </summary>
    public class ReceiverImage : ReceiverBase
    {
        private readonly Subject<IImage> videoFrames = new Subject<IImage>();

        private IntPtr buffer0 = IntPtr.Zero;
        private IntPtr buffer1 = IntPtr.Zero;
        private int buffer01Size = 0;

        [DllImport("msvcrt.dll", EntryPoint = "memcpy", CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public unsafe static extern IntPtr memcpy(byte* dest, byte* src, int count);

        /// <summary>
        /// Received Images
        /// </summary>
        public IObservable<IImage> Frames => videoFrames;


        protected override void createVideoOutput(NDIlib.video_frame_v2_t videoFrame)
        {
            // get all our info so that we can free the frame
            int yres = (int)videoFrame.yres;
            int xres = (int)videoFrame.xres;

            // quick and dirty aspect ratio correction for non-square pixels - SD 4:3, 16:9, etc.
            double dpiX = 96.0 * (videoFrame.picture_aspect_ratio / ((double)xres / (double)yres));

            int stride = (int)videoFrame.line_stride_in_bytes;
            int bufferSize = yres * stride;


            if (bufferSize != buffer01Size)
            {
                buffer0 = Marshal.ReAllocCoTaskMem(buffer0, bufferSize);
                buffer1 = Marshal.ReAllocCoTaskMem(buffer1, bufferSize);
                buffer01Size = bufferSize;
            }


            // Copy data
            unsafe
            {
                byte* dst = (byte*)buffer0.ToPointer();
                byte* src = (byte*)videoFrame.p_data.ToPointer();

                for (int y = 0; y < yres; y++)
                {
                    memcpy(dst, src, stride);
                    dst += stride;
                    src += stride;
                }
            }

            // swap
            IntPtr temp = buffer0;
            buffer0 = buffer1;
            buffer1 = temp;

            ImagingPixelFormat pixFmt;
            switch (videoFrame.FourCC)
            {
                case NDIlib.FourCC_type_e.FourCC_type_BGRA:
                    pixFmt = PixelFormat.B8G8R8A8; break;
                case NDIlib.FourCC_type_e.FourCC_type_BGRX:
                    pixFmt = PixelFormat.B8G8R8; break;
                case NDIlib.FourCC_type_e.FourCC_type_RGBA:
                    pixFmt = PixelFormat.R8G8B8A8; break;
                case NDIlib.FourCC_type_e.FourCC_type_RGBX:
                    pixFmt = PixelFormat.R8G8B8; break;
                default:
                    pixFmt = PixelFormat.Unknown;    // TODO: need to handle other video formats which are currently unsupported by IImage
                    break;
            }

            var VideoFrameImage = buffer1.ToImage(bufferSize, xres, yres, pixFmt, videoFrame.FourCC.ToString());

            videoFrames.OnNext(VideoFrameImage);
        }

        public override void Dispose()
        {
            Marshal.FreeCoTaskMem(buffer0);
            Marshal.FreeCoTaskMem(buffer1);

            base.Dispose();
        }
    }
}
