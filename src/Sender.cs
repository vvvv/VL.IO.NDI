using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.HighPerformance.Buffers;
using NewTek;
using VL.Lib.Basics.Audio;
using VL.Lib.Basics.Imaging;
using VL.Lib.Basics.Resources;
using VL.Lib.Basics.Video;

namespace VL.IO.NDI
{
    public class Sender : NativeObject
    {
        private readonly Task _sendTask;
        private readonly BlockingCollection<(TaskCompletionSource<Unit> tcs, IResourceHandle<VideoFrame> handle)> _videoFrames = new BlockingCollection<(TaskCompletionSource<Unit> tcs, IResourceHandle<VideoFrame> handle)>(boundedCapacity: 1);
        private readonly IntPtr _sendInstancePtr;

        private readonly SerialDisposable _videoStreamSubscription = new SerialDisposable();
        private readonly SerialDisposable _audioStreamSubscription = new SerialDisposable();

        VideoStream _videoStream;
        AudioStream _audioStream;

        private NDIlib.tally_t _ndiTally = new NDIlib.tally_t();

        public Sender(string sourceName, bool clockVideo = true, bool clockAudio = false, String[] groups = null, Source failsafe = null)
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

            unsafe
            {
                var flatGroups = groups != null ? string.Join(",", groups) : null;
                fixed (byte* sourceNamePtr = Utils.StringToUtf8(sourceName))
                fixed (byte* groupsNamePtr = Utils.StringToUtf8(flatGroups))
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
                        throw new InvalidOperationException("Failed to create send instance. Make sure the source name is unique.");
                    }
                }

                if (failsafe != null && !failsafe.IsNone)
                {
                    // .Net interop doesn't handle UTF-8 strings, so do it manually
                    // These must be freed later
                    fixed (byte* failoverNamePtr = Utils.StringToUtf8(failsafe.Name))
                    {
                        NDIlib.source_t failoverDesc = new NDIlib.source_t()
                        {
                            p_ndi_name = new IntPtr(failoverNamePtr),
                            p_url_address = IntPtr.Zero
                        };

                        NDIlib.send_set_failover(_sendInstancePtr, ref failoverDesc);
                    }
                }
            }

            _sendTask = Task.Run(async () =>
            {
                using var videoFrameSubscription = new SerialDisposable();
                try
                {
                    foreach (var (tcs, handle) in _videoFrames.GetConsumingEnumerable())
                    {
                        try
                        {
                            var videoFrame = handle.Resource;

                            MemoryOwner<byte> memoryOwner = default;
                            if (!videoFrame.TryGetMemory(out var memory))
                            {
                                memoryOwner = MemoryOwner<byte>.Allocate(videoFrame.LengthInBytes);
                                memory = memoryOwner.Memory;
                                await videoFrame.CopyToAsync(memoryOwner.Memory);
                            }

                            var memoryHandle = memory.Pin();
                            var metadataHandle = GCHandle.Alloc(Utils.StringToUtf8(videoFrame.Metadata), GCHandleType.Pinned);

                            var ndiVideoFrame = ToNativeVideoFrame(videoFrame, memoryHandle, metadataHandle.AddrOfPinnedObject());
                            NDIlib.send_send_video_async_v2(_sendInstancePtr, ref ndiVideoFrame);

                            // Release the previous frame and hold on to this one
                            videoFrameSubscription.Disposable = Disposable.Create(() =>
                            {
                                metadataHandle.Free();
                                memoryHandle.Dispose();
                                memoryOwner?.Dispose();
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

                    NDIlib.send_send_video_async_v2(_sendInstancePtr, ref Utils.NULL<NDIlib.video_frame_v2_t>());
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

        public bool OnPreview => Tally.on_preview;

        public bool OnProgram => Tally.on_program;

        // The number of current connections
        public int Connections
        {
            get
            {
                return NDIlib.send_get_no_connections(_sendInstancePtr, 0) / 2;
            }
        }

        // Get the current number of receivers connected to this source. This can be used to avoid even rendering when nothing is connected to the video source.
        // which can significantly improve the efficiency if you want to make a lot of sources available on the network. If you specify a timeout that is not
        // 0 then it will wait until there are connections for this amount of time.
        public int GetConnections(int waitMs)
        {
            return NDIlib.send_get_no_connections(_sendInstancePtr, (uint)waitMs);
        }

        public VideoStream VideoStream
        {
            set
            {
                if (value != _videoStream)
                {
                    _videoStream = value;
                    _videoStreamSubscription.Disposable = value?.Frames
                        .SelectMany(f => SendAsync(f))
                        .Subscribe();
                }
            }
        }

        public AudioStream AudioStream
        {
            set
            {
                if (value != _audioStream)
                {
                    _audioStream = value;
                    _audioStreamSubscription.Disposable = value?.Frames
                        .Subscribe(f =>
                        {
                            using (var handle = f.GetHandle())
                                Send(handle.Resource);
                        });
                }
            }
        }

        public unsafe void Send(VideoFrame videoFrame)
        {
            if (!Enabled)
                return;

            if (!videoFrame.TryGetMemory(out var memory))
                return;

            fixed (byte* dataP = memory.Span)
            fixed (byte* metadataP = Utils.StringToUtf8(videoFrame.Metadata))
            {
                var nativeVideoFrame = ToNativeVideoFrame(videoFrame, new IntPtr(dataP), new IntPtr(metadataP));
                NDIlib.send_send_video_v2(_sendInstancePtr, ref nativeVideoFrame);
            }
        }

        public Task<Unit> SendAsync(IResourceProvider<VideoFrame> videoFrame)
        {
            if (!Enabled)
                return Task.FromResult(Unit.Default);

            var handle = videoFrame?.GetHandle();
            if (handle?.Resource is null)
                return Task.FromResult(Unit.Default);

            var tcs = new TaskCompletionSource<Unit>();
            _videoFrames.Add((tcs, handle));
            return tcs.Task;
        }

        public unsafe void Send(AudioFrame audioFrame)
        {
            if (!Enabled)
                return;

            if (audioFrame.IsPlanar)
            {
                var buffer = audioFrame.Data.Span;
                fixed (float* bufferPointer = buffer)
                fixed (byte* metadataPointer = Utils.StringToUtf8(audioFrame.Metadata))
                {
                    var nativeAudioFrame = new NDIlib.audio_frame_v2_t()
                    {
                        channel_stride_in_bytes = buffer.Width * sizeof(float),
                        no_channels = audioFrame.ChannelCount,
                        no_samples = audioFrame.SampleCount,
                        p_data = new IntPtr(bufferPointer),
                        p_metadata = new IntPtr(metadataPointer),
                        sample_rate = audioFrame.SampleRate
                    };
                    NDIlib.send_send_audio_v2(_sendInstancePtr, ref nativeAudioFrame);
                }
            }
        }

        protected override void Destroy(bool disposing)
        {
            try
            {
                _videoStreamSubscription.Dispose();
                _audioStreamSubscription.Dispose();
                _videoFrames.CompleteAdding();
                _sendTask?.Wait();
            }
            finally
            {
                NDIlib.send_destroy(_sendInstancePtr);
                NDIlib.destroy();
            }
        }

        private static unsafe NDIlib.video_frame_v2_t ToNativeVideoFrame(VideoFrame videoFrame, MemoryHandle handle, IntPtr metadata)
        {
            return ToNativeVideoFrame(videoFrame, new IntPtr(handle.Pointer), metadata);
        }

        private static unsafe NDIlib.video_frame_v2_t ToNativeVideoFrame(VideoFrame videoFrame, IntPtr data, IntPtr metadata)
        {
            return new NDIlib.video_frame_v2_t()
            {
                xres = videoFrame.Width,
                yres = videoFrame.Height,
                FourCC = ToFourCC(videoFrame.PixelFormat),
                frame_rate_N = videoFrame.FrameRate.N,
                frame_rate_D = videoFrame.FrameRate.D,
                picture_aspect_ratio = (float)videoFrame.Width / videoFrame.Height,
                frame_format_type = NDIlib.frame_format_type_e.frame_format_type_progressive,
                timecode = NDIlib.send_timecode_synthesize,
                p_data = data,
                line_stride_in_bytes = videoFrame.PixelSizeInBytes * videoFrame.Width,
                p_metadata = metadata,
                timestamp = 0
            };

            static NDIlib.FourCC_type_e ToFourCC(PixelFormat pixelFormat)
            {
                switch (pixelFormat)
                {
                    case PixelFormat.R8G8B8X8: return NDIlib.FourCC_type_e.FourCC_type_RGBX;
                    case PixelFormat.R8G8B8A8: return NDIlib.FourCC_type_e.FourCC_type_RGBA;
                    case PixelFormat.B8G8R8X8: return NDIlib.FourCC_type_e.FourCC_type_BGRX;
                    case PixelFormat.B8G8R8A8: return NDIlib.FourCC_type_e.FourCC_type_BGRA;
                    default:
                        throw new UnsupportedPixelFormatException(pixelFormat);
                }
            }
        }
    }
}
