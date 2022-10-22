using System;

using NewTek;
using VL.Lib.Basics.Imaging;

namespace VL.IO.NDI
{
    /// <summary>
    /// 
    /// </summary>
    public class VideoFrame : IImage, IDisposable
    {
        private readonly IntPtrImage _image;

        internal NDIlib.video_frame_v2_t _ndiVideoFrame;

        internal VideoFrame(NDIlib.video_frame_v2_t videoFrame)
        {
            _ndiVideoFrame = videoFrame;
            _image = new IntPtrImage(
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

        public string MetaData
        {
            get
            {
                if (_ndiVideoFrame.p_metadata == IntPtr.Zero)
                    return null;

                return UTF.Utf8ToString(_ndiVideoFrame.p_metadata);
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
                _image.Dispose();
            }
        }

        ImageInfo IImage.Info => _image.Info;

        bool IImage.IsVolatile => _image.IsVolatile;

        IImageData IImage.GetData() => _image.GetData();
    }
}
