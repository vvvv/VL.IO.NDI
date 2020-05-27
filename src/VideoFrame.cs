using System;
using System.Runtime.InteropServices;
using System.Xml.Linq;

using SharpDX.Direct3D11;

using VL.Core;
using VL.Lib.Basics.Resources;
using VL.Lib.Basics.Imaging;

using NewTek;

namespace VL.IO.NDI
{
    public class VideoFrame : IDisposable
    {
        // the simple constructor only deals with BGRA. For other color formats you'll need to handle it manually.
        // Defaults to progressive but can be changed.
        public VideoFrame( int width, int height, float aspectRatio, int frameRateNumerator, int frameRateDenominator,
                            NDIlib.frame_format_type_e format = NDIlib.frame_format_type_e.frame_format_type_progressive)
        {
            // we have to know to free it later
            _memoryOwned = true;

            int stride = (width * 32 /*BGRA bpp*/ + 7) / 8;
            int bufferSize = height * stride;

            // allocate some memory for a video buffer
            IntPtr videoBufferPtr = Marshal.AllocHGlobal(bufferSize);

            _ndiVideoFrame = new NDIlib.video_frame_v2_t()
            {
                xres = width,
                yres = height,
                FourCC = NDIlib.FourCC_type_e.FourCC_type_BGRA,
                frame_rate_N = frameRateNumerator,
                frame_rate_D = frameRateDenominator,
                picture_aspect_ratio = aspectRatio,
                frame_format_type = format,
                timecode = NDIlib.send_timecode_synthesize,
                p_data = videoBufferPtr,
                line_stride_in_bytes = stride,
                p_metadata = IntPtr.Zero,
                timestamp = 0
            };
        }

        /// <summary>
        /// Create a VideoFrame from a <see cref="SharpDX.Direct3D11.Texture2D"/>
        /// </summary>
        /// <param name="texture"></param>
        /// <param name="aspectRatio"></param>
        /// <param name="fourCC"></param>
        /// <param name="frameRateNumerator"></param>
        /// <param name="frameRateDenominator"></param>
        /// <param name="format"></param>
        /// <param name="nodeContext"></param>
        public VideoFrame(Texture2D texture, NDIlib.FourCC_type_e fourCC,
            int frameRateNumerator, int frameRateDenominator, NDIlib.frame_format_type_e format, NodeContext nodeContext)

        {
            var provider = nodeContext.Factory.CreateService<IResourceProvider<Device>>(nodeContext);
            using var deviceHandle = provider.GetHandle();
            var device = deviceHandle.Resource;

            int width = texture.Description.Width;
            int height = texture.Description.Height;
            int stride = width * SharpDX.DXGI.FormatHelper.SizeOfInBytes(texture.Description.Format);
            int bufferSize = height * stride;

            IntPtr videoBufferPtr = Marshal.AllocHGlobal(bufferSize);

            texture.CopyToPointer(device, videoBufferPtr, bufferSize);

            _ndiVideoFrame = new NDIlib.video_frame_v2_t()
            {
                xres = width,
                yres = height,
                FourCC = texture.Description.Format.ToFourCC(),
                frame_rate_N = frameRateNumerator,
                frame_rate_D = frameRateDenominator,
                picture_aspect_ratio = (float)width / height,
                frame_format_type = format,
                timecode = NDIlib.send_timecode_synthesize,
                p_data = videoBufferPtr,
                line_stride_in_bytes = stride,
                p_metadata = IntPtr.Zero,
                timestamp = 0
            };
        }

        public VideoFrame(IImage image, bool clone, float aspectRatio, NDIlib.FourCC_type_e fourCC,
            int frameRateNumerator, int frameRateDenominator, NDIlib.frame_format_type_e format)
        {
            var ar = aspectRatio;
            if (ar <= 0.0)
                ar = (float) image.Info.Width / image.Info.Height;

            int bufferSize = image.Info.ImageSize;

            IntPtr videoBufferPtr;

            if (clone) { 
                // we have to know to free it later
                _memoryOwned = true;

                // allocate some memory for a video buffer
                videoBufferPtr = Marshal.AllocHGlobal(bufferSize);

                using(var handle = image.GetData().Bytes.Pin())
                {
                    unsafe
                    {
                        System.Buffer.MemoryCopy((void*)handle.Pointer, (void*)videoBufferPtr.ToPointer(), bufferSize, bufferSize);
                    }
                }
            }
            else
            {
                _pinnedBytes = true;
                unsafe
                {
                    _handle = image.GetData().Bytes.Pin(); //unpin when frame gets Disposed
                    videoBufferPtr = (IntPtr) _handle.Pointer; 
                }
            }

            _ndiVideoFrame = new NDIlib.video_frame_v2_t()
            {
                xres = image.Info.Width,
                yres = image.Info.Height,
                FourCC = fourCC,
                frame_rate_N = frameRateNumerator,
                frame_rate_D = frameRateDenominator,
                picture_aspect_ratio = ar,
                frame_format_type = format,
                timecode = NDIlib.send_timecode_synthesize,
                p_data = videoBufferPtr,
                line_stride_in_bytes = image.Info.ScanSize,
                p_metadata = IntPtr.Zero,
                timestamp = 0
            };
        }

        public VideoFrame(IntPtr bufferPtr, int width, int height, int stride, NDIlib.FourCC_type_e fourCC,
                            float aspectRatio, int frameRateNumerator, int frameRateDenominator, NDIlib.frame_format_type_e format)
        {
            _ndiVideoFrame = new NDIlib.video_frame_v2_t()
            {
                xres = width,
                yres = height,
                FourCC = fourCC,
                frame_rate_N = frameRateNumerator,
                frame_rate_D = frameRateDenominator,
                picture_aspect_ratio = aspectRatio,
                frame_format_type = format,
                timecode = NDIlib.send_timecode_synthesize,
                p_data = bufferPtr,
                line_stride_in_bytes = stride,
                p_metadata = IntPtr.Zero,
                timestamp = 0
            };
        }

        public int Width
        {
            get
            {
                return _ndiVideoFrame.xres;
            }
        }

        public int Height
        {
            get
            {
                return _ndiVideoFrame.yres;
            }
        }

        public int Stride
        {
            get
            {
                return _ndiVideoFrame.line_stride_in_bytes;
            }
        }

        public IntPtr BufferPtr
        {
            get
            {
                return _ndiVideoFrame.p_data;
            }
        }

        public Int64 TimeStamp
        {
            get
            {
                return _ndiVideoFrame.timestamp;
            }
            set
            {
                _ndiVideoFrame.timestamp = value;
            }
        }

        public XElement MetaData
        {
            get
            {
                if(_ndiVideoFrame.p_metadata == IntPtr.Zero)
                    return null;

                String mdString = UTF.Utf8ToString(_ndiVideoFrame.p_metadata);
                if (String.IsNullOrEmpty(mdString))
                    return null;

                return XElement.Parse(mdString);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~VideoFrame() 
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing) 
            {
                if (_memoryOwned)
                {
                    Marshal.FreeHGlobal(_ndiVideoFrame.p_data);
                    _ndiVideoFrame.p_data = IntPtr.Zero;
                }

                if (_pinnedBytes)
                {
                    _handle.Dispose();
                    _pinnedBytes = false;
                }

                NDIlib.destroy();
            }
        }

        internal NDIlib.video_frame_v2_t _ndiVideoFrame;
        bool _memoryOwned = false;

        bool _pinnedBytes = false;
        System.Buffers.MemoryHandle _handle;
    }    
}
