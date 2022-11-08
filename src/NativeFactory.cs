using NewTek;
using System;
using VL.Lib.Basics.Resources;

// Utility functions outside of the NDILib SDK itself,
// but useful for working with NDI from managed languages.

namespace VL.IO.NDI
{
    internal static unsafe class NativeFactory
    {
        public static IResourceProvider<IntPtr> CreateReceiver(string sourceName, string receiverName, NDIlib.recv_color_format_e colorFormat, NDIlib.recv_bandwidth_e bandwidth, bool allowVideoFields)
        {
            return ResourceProvider.New(() =>
            {
                fixed (byte* sourceNamePtr = Utils.StringToUtf8(sourceName))
                fixed (byte* receiverNamePtr = Utils.StringToUtf8(receiverName))
                {
                    // a source_t to describe the source to connect to.
                    NDIlib.source_t source_t = new NDIlib.source_t()
                    {
                        p_ndi_name = new IntPtr(sourceNamePtr)
                    };

                    // make a description of the receiver we want
                    NDIlib.recv_create_v3_t recvDescription = new NDIlib.recv_create_v3_t()
                    {
                        // the source we selected
                        source_to_connect_to = source_t,

                        // we want BGRA frames for this example
                        color_format = colorFormat,

                        // we want full quality - for small previews or limited bandwidth, choose lowest
                        bandwidth = bandwidth,

                        // let NDIlib deinterlace for us if needed
                        allow_video_fields = allowVideoFields,

                        // The name of the NDI receiver to create. This is a NULL terminated UTF8 string and should be
                        // the name of receive channel that you have. This is in many ways symettric with the name of
                        // senders, so this might be "Channel 1" on your system.
                        p_ndi_recv_name = new IntPtr(receiverNamePtr)
                    };

                    return NDIlib.recv_create_v3(ref recvDescription);
                }
            }, NDIlib.recv_destroy);
        }

        public static IResourceProvider<IntPtr> CreateSync(this IResourceProvider<IntPtr> receiver)
        {
            return receiver.Bind(r => ResourceProvider.New(() => NDIlib.framesync_create(r), NDIlib.framesync_destroy));
        }
    }
} // namespace NewTek.NDI
