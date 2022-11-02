﻿using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
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
    public class Sender : NativeObject
    {
        private readonly Task _sendTask;
        private readonly BlockingCollection<(TaskCompletionSource<Unit> tcs, IResourceHandle<VideoFrame> handle)> _videoFrames = new BlockingCollection<(TaskCompletionSource<Unit> tcs, IResourceHandle<VideoFrame> handle)>(boundedCapacity: 1);
        private readonly IntPtr _sendInstancePtr;

        private NDIlib.tally_t _ndiTally = new NDIlib.tally_t();
        private IObservable<IResourceProvider<IImage>> _imageStream;

        public unsafe Sender(string sourceName, bool clockVideo=true, bool clockAudio=false, String[] groups = null, String failoverName=null)
        {
            if (string.IsNullOrEmpty(sourceName))
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
            var flatGroups = groups != null ? string.Join(",", groups) : null;
            fixed (byte* sourceNamePtr = UTF.StringToUtf8(sourceName))
            fixed (byte* groupsNamePtr = UTF.StringToUtf8(flatGroups))
            {
                // Create an NDI source description
                NDIlib.send_create_t createDesc = new NDIlib.send_create_t()
                {
                    p_ndi_name = new IntPtr(sourceNamePtr),
                    p_groups = new IntPtr(groupsNamePtr),
                    clock_video = clockVideo,
                    clock_audio = clockAudio
                };

                // create the NDI send instance
                _sendInstancePtr = NDIlib.send_create(ref createDesc);

                // did it succeed?
                if (_sendInstancePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create send instance.");
                }
            }

            if (!String.IsNullOrEmpty(failoverName))
            {
                // .Net interop doesn't handle UTF-8 strings, so do it manually
                // These must be freed later
                fixed (byte* failoverNamePtr = UTF.StringToUtf8(failoverName))
                {
                    NDIlib.source_t failoverDesc = new NDIlib.source_t()
                    {
                        p_ndi_name = new IntPtr(failoverNamePtr),
                        p_url_address = IntPtr.Zero
                    };

                    NDIlib.send_set_failover(_sendInstancePtr, ref failoverDesc);
                }
            }

            _sendTask = Task.Run(() =>
            {
                using var videoFrameSubscription = new SerialDisposable();
                try
                {
                    foreach (var (tcs, handle) in _videoFrames.GetConsumingEnumerable())
                    {
                        try
                        {
                            var videoFrame = handle.Resource;
                            var image = videoFrame.Image;
                            var info = image.Info;
                            var imageData = image.GetData();
                            var memoryHandle = imageData.Bytes.Pin();
                            var metadataHandle = GCHandle.Alloc(UTF.StringToUtf8(videoFrame.Metadata), GCHandleType.Pinned);

                            var ndiVideoFrame = ToNativeVideoFrame(info, imageData, new IntPtr(memoryHandle.Pointer), metadataHandle.AddrOfPinnedObject());
                            NDIlib.send_send_video_async_v2(_sendInstancePtr, ref ndiVideoFrame);

                            // Release the previous frame and hold on to this one
                            videoFrameSubscription.Disposable = Disposable.Create(() =>
                            {
                                metadataHandle.Free();
                                memoryHandle.Dispose();
                                imageData.Dispose();
                                handle.Dispose();
                            });

                            // Mark task as completed
                            tcs.SetResult(default);
                        }
                        catch (Exception e)
                        {
                            // Mark task as faulted
                            tcs.SetException(e);
                        }
                    }
                }
                finally
                {
                    // Ensures that in case of a crash no more frames will be added
                    _videoFrames.CompleteAdding();

                    NDIlib.send_send_video_async_v2(_sendInstancePtr, ref Unsafe.AsRef<NDIlib.video_frame_v2_t>(IntPtr.Zero.ToPointer()));
                }
            });
        }

        public bool Enabled { get; set; } = true;

        // The current tally state
        public NDIlib.tally_t Tally
        {
            get
            {
                NDIlib.send_get_tally(_sendInstancePtr, ref _ndiTally, 0);

                return _ndiTally;
            }
        }

        // Determine the current tally sate. If you specify a timeout then it will wait until it has changed, otherwise it will simply poll it
        // and return the current tally immediately. The return value is whether anything has actually change (true) or whether it timed out (false)
        public bool GetTally(ref NDIlib.tally_t tally, int timeout)
        {
            return NDIlib.send_get_tally(_sendInstancePtr, ref tally, (uint)timeout);
        }
        
        // The number of current connections
        public int Connections
        {
            get
            {
                return NDIlib.send_get_no_connections(_sendInstancePtr, 0);
            }
        }

        // Get the current number of receivers connected to this source. This can be used to avoid even rendering when nothing is connected to the video source.
        // which can significantly improve the efficiency if you want to make a lot of sources available on the network. If you specify a timeout that is not
        // 0 then it will wait until there are connections for this amount of time.
        public int GetConnections(int waitMs)
        {
            return NDIlib.send_get_no_connections(_sendInstancePtr, (uint)waitMs);
        }

        public unsafe void Send(VideoFrame videoFrame)
        {
            var image = videoFrame.Image;
            if (image is null)
                return;

            Send(image, videoFrame.Metadata);
        }

        public unsafe void Send(IImage image, string metadata = null)
        {
            var info = image.Info;
            var imageData = image.GetData();
            fixed (byte* dataP = imageData.Bytes.Span)
            fixed (byte* metadataP = UTF.StringToUtf8(metadata))
            {
                var nativeVideoFrame = ToNativeVideoFrame(info, imageData, new IntPtr(dataP), new IntPtr(metadataP));
                NDIlib.send_send_video_v2(_sendInstancePtr, ref nativeVideoFrame);
            }
        }

        public Task SendAsync(IResourceProvider<IImage> image, string metadata = null)
        {
            return SendAsync(image.Bind(i => new VideoFrame(i, metadata)));
        }

        public Task SendAsync(IResourceProvider<VideoFrame> videoFrame)
        {
            var handle = videoFrame?.GetHandle();
            if (handle?.Resource?.Image is null)
                return Task.CompletedTask;

            var tcs = new TaskCompletionSource<Unit>();
            _videoFrames.Add((tcs, handle));
            return tcs.Task;
        }

        public void Send(AudioFrame audioFrame)
        {
            Send(ref audioFrame._ndiAudioFrame);
        }

        public void Send(ref NDIlib.audio_frame_v2_t audioFrame)
        {
            NDIlib.send_send_audio_v2(_sendInstancePtr, ref audioFrame);
        }

        protected override void Destroy(bool disposing)
        {
            try
            {
                _videoFrames.CompleteAdding();
                _sendTask.Wait();
            }
            finally
            {
                NDIlib.send_destroy(_sendInstancePtr);
                NDIlib.destroy();
            }
        }

        private static unsafe NDIlib.video_frame_v2_t ToNativeVideoFrame(ImageInfo info, IImageData imageData, IntPtr data, IntPtr metadata)
        {
            return new NDIlib.video_frame_v2_t()
            {
                xres = info.Width,
                yres = info.Height,
                FourCC = ToFourCC(info.Format),
                frame_rate_N = 0,
                frame_rate_D = 0,
                picture_aspect_ratio = (float)info.Width / info.Height,
                frame_format_type = NDIlib.frame_format_type_e.frame_format_type_progressive,
                timecode = NDIlib.send_timecode_synthesize,
                p_data = data,
                line_stride_in_bytes = imageData.ScanSize,
                p_metadata = metadata,
                timestamp = 0
            };

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
        }
    }
}
