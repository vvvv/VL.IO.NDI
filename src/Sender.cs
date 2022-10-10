using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NewTek;
using VL.Lib.Basics.Imaging;
using VL.Lib.Basics.Resources;

//namespace NewTek.NDI
namespace VL.IO.NDI

{
    public class Sender : IDisposable
    {
        private IntPtr _sendInstancePtr = IntPtr.Zero;
        private NDIlib.tally_t _ndiTally = new NDIlib.tally_t();
        private readonly Task _sendTask;
        private BlockingCollection<IResourceHandle<IImage>> _videoFrames = new BlockingCollection<IResourceHandle<IImage>>(boundedCapacity: 1);

        public unsafe Sender(String sourceName, bool clockVideo=true, bool clockAudio=false, String[] groups = null, String failoverName=null)
        {
            if (String.IsNullOrEmpty(sourceName))
            {
                throw new ArgumentException("sourceName can not be null or empty.", sourceName);
            }

            if (!NDIlib.initialize())
            {
                if(!NDIlib.is_supported_CPU())
                    throw new InvalidOperationException("CPU incompatible with NDI.");
                else
                    throw new InvalidOperationException("Unable to initialize NDI.");
            }

            // .Net interop doesn't handle UTF-8 strings, so do it manually
            // These must be freed later
            IntPtr sourceNamePtr = UTF.StringToUtf8(sourceName);

            IntPtr groupsNamePtr = IntPtr.Zero;

            // make a flat list of groups if needed
            if(groups != null)
            {
                StringBuilder flatGroups = new StringBuilder();
                foreach (String group in groups)
                {
                    flatGroups.Append(group);
                    if (group != groups.Last())
                    {
                        flatGroups.Append(',');
                    }
                }

                groupsNamePtr = UTF.StringToUtf8(flatGroups.ToString());
            }            

            // Create an NDI source description
            NDIlib.send_create_t createDesc = new NDIlib.send_create_t()
            {
                p_ndi_name = sourceNamePtr,
                p_groups = groupsNamePtr,
                clock_video = clockVideo,
                clock_audio = clockAudio
            };

            // create the NDI send instance
            _sendInstancePtr = NDIlib.send_create(ref createDesc);

            // free the strings we allocated
            Marshal.FreeHGlobal(sourceNamePtr);
            Marshal.FreeHGlobal(groupsNamePtr);

            // did it succeed?
            if (_sendInstancePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create send instance.");
            }

            if (!String.IsNullOrEmpty(failoverName))
            {
                // .Net interop doesn't handle UTF-8 strings, so do it manually
                // These must be freed later
                IntPtr failoverNamePtr = UTF.StringToUtf8(failoverName);

                NDIlib.source_t failoverDesc = new NDIlib.source_t()
                {
                    p_ndi_name = failoverNamePtr,
                    p_url_address = IntPtr.Zero
                };

                NDIlib.send_set_failover(_sendInstancePtr, ref failoverDesc);

                // free the strings we allocated
                Marshal.FreeHGlobal(failoverNamePtr);
            }

            _sendTask = Task.Run(() =>
            {
                var videoFrameSubscription = new SerialDisposable();
                try
                {
                    foreach (var handle in _videoFrames.GetConsumingEnumerable())
                    {
                        var image = handle.Resource;
                        if (image is null)
                            continue;

                        var info = image.Info;
                        var imageData = image.GetData();
                        var memoryHanlde = imageData.Bytes.Pin();
                        var ndiVideoFrame = new NDIlib.video_frame_v2_t()
                        {
                            xres = info.Width,
                            yres = info.Height,
                            FourCC = ToFourCC(info.Format),
                            frame_rate_N = 0,
                            frame_rate_D = 0,
                            picture_aspect_ratio = (float)info.Width / info.Height,
                            frame_format_type = NDIlib.frame_format_type_e.frame_format_type_progressive,
                            timecode = NDIlib.send_timecode_synthesize,
                            p_data = new IntPtr(memoryHanlde.Pointer),
                            line_stride_in_bytes = imageData.ScanSize,
                            p_metadata = IntPtr.Zero,
                            timestamp = 0
                        };

                        NDIlib.send_send_video_async_v2(_sendInstancePtr, ref ndiVideoFrame);

                        // Release the previous frame and hold on to this one
                        videoFrameSubscription.Disposable = Disposable.Create(() =>
                        {
                            memoryHanlde.Dispose();
                            imageData.Dispose();
                            handle.Dispose();
                        });
                    }
                }
                finally
                {
                    NDIlib.send_send_video_async_v2(_sendInstancePtr, ref Unsafe.AsRef<NDIlib.video_frame_v2_t>(IntPtr.Zero.ToPointer()));
                    videoFrameSubscription.Dispose();
                }

                static NDIlib.FourCC_type_e ToFourCC(PixelFormat format)
                {
                    switch (format)
                    {
                        case PixelFormat.R8G8B8X8:
                            return NDIlib.FourCC_type_e.FourCC_type_RGBX;
                        case PixelFormat.R8G8B8A8:
                            return NDIlib.FourCC_type_e.FourCC_type_RGBA;
                        case PixelFormat.B8G8R8X8:
                            return NDIlib.FourCC_type_e.FourCC_type_BGRX;
                        case PixelFormat.B8G8R8A8:
                            return NDIlib.FourCC_type_e.FourCC_type_BGRA;
                        default:
                            throw new UnsupportedPixelFormatException(format);
                    }
                }
            });
        }

        // The current tally state
        public NDIlib.tally_t Tally
        {
            get
            {
                if (_sendInstancePtr == IntPtr.Zero)
                    return _ndiTally;

                NDIlib.send_get_tally(_sendInstancePtr, ref _ndiTally, 0);

                return _ndiTally;
            }
        }

        // Determine the current tally sate. If you specify a timeout then it will wait until it has changed, otherwise it will simply poll it
        // and return the current tally immediately. The return value is whether anything has actually change (true) or whether it timed out (false)
        public bool GetTally(ref NDIlib.tally_t tally, int timeout)
        {
            if (_sendInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.send_get_tally(_sendInstancePtr, ref tally, (uint)timeout);
        }
        
        // The number of current connections
        public int Connections
        {
            get
            {
                if (_sendInstancePtr == IntPtr.Zero)
                    return 0;

                return NDIlib.send_get_no_connections(_sendInstancePtr, 0);
            }
        }

        // Get the current number of receivers connected to this source. This can be used to avoid even rendering when nothing is connected to the video source.
        // which can significantly improve the efficiency if you want to make a lot of sources available on the network. If you specify a timeout that is not
        // 0 then it will wait until there are connections for this amount of time.
        public int GetConnections(int waitMs)
        {
            if (_sendInstancePtr == IntPtr.Zero)
                return 0;

            return NDIlib.send_get_no_connections(_sendInstancePtr, (uint)waitMs);
        }

        public void Send(VideoFrame videoFrame)
        {
            Send(ref videoFrame._ndiVideoFrame);
        }

        public void Send(ref NDIlib.video_frame_v2_t videoFrame)
        {
            if (_sendInstancePtr == IntPtr.Zero)
                return;

            NDIlib.send_send_video_v2(_sendInstancePtr, ref videoFrame);
        }

        public unsafe void SendAsync(IResourceProvider<IImage> videoFrame)
        {
            if (_sendInstancePtr == IntPtr.Zero || videoFrame is null)
                return;

            var handle = videoFrame.GetHandle();
            _videoFrames.Add(handle);
        }

        public void Send(AudioFrame audioFrame)
        {
            Send(ref audioFrame._ndiAudioFrame);
        }

        public void Send(ref NDIlib.audio_frame_v2_t audioFrame)
        {
            if (_sendInstancePtr == IntPtr.Zero)
                return;

            NDIlib.send_send_audio_v2(_sendInstancePtr, ref audioFrame);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~Sender() 
        {
            Dispose(false);
        }

        protected virtual unsafe void Dispose(bool disposing)
        {
            if (disposing) 
            {
                if (_sendInstancePtr != IntPtr.Zero)
                {
                    _videoFrames.CompleteAdding();
                    if (_sendTask != null)
                        _sendTask.Wait();

                    NDIlib.send_destroy(_sendInstancePtr);
                    _sendInstancePtr = IntPtr.Zero;
                }

                NDIlib.destroy();
            }
        }
    }
}
