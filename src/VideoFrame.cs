using System;

using VL.Lib.Basics.Imaging;

namespace VL.IO.NDI
{
    public class VideoFrame : IImage
    {
        public VideoFrame(IImage image, string metadata)
        {
            Image = image;
            Metadata = metadata;
        }

        public IImage Image { get; }

        public string Metadata { get; }

        ImageInfo IImage.Info => Image.Info;

        bool IImage.IsVolatile => Image.IsVolatile;

        IImageData IImage.GetData()=> Image.GetData();
    }
}
