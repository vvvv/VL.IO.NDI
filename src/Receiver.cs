using NewTek;
using System;
using System.Threading;

using VL.Lib.Basics.Resources;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Linq;
using VL.Lib.Basics.Video;
using VL.Lib.Basics.Audio;
using CommunityToolkit.HighPerformance;
using System.Buffers;

namespace VL.IO.NDI
{
    public sealed class Receiver : NativeObject
    {
        private VideoStream _videoStream;
        private AudioStream _audioStream;
        private IObservable<string> _metadataStream;

        // our unmanaged NDI receiver instance
        private IResourceProvider<IntPtr> _recvInstanceProvider;
        private IResourceHandle<IntPtr> _recvInstanceHandle;
        private IResourceProvider<IntPtr> _syncInstanceProvider;

        private IntPtr _recvInstancePtr => _recvInstanceHandle != null ? _recvInstanceHandle.Resource : default;

        internal IResourceProvider<IntPtr> SyncInstanceProvider => _syncInstanceProvider;

        /// <summary>
        /// Received Images
        /// </summary>
        public VideoStream VideoStream => _videoStream;

        public AudioStream AudioStream => _audioStream;

        /// <summary>
        /// Received Metadata
        /// </summary>
        public IObservable<string> MetadataStream => _metadataStream ?? Observable.Empty<string>();

        /// <summary>
        /// Whether or not streaming is enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Connect to an NDI source.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="receiverName"></param>
        /// <param name="colorFormat"></param>
        /// <param name="bandwidth"></param>
        /// <param name="allowVideoFields"></param>
        public void Connect(Source source, string receiverName,
            NDIlib.recv_color_format_e colorFormat = NDIlib.recv_color_format_e.recv_color_format_BGRX_BGRA,
            NDIlib.recv_bandwidth_e bandwidth = NDIlib.recv_bandwidth_e.recv_bandwidth_highest, 
            bool allowVideoFields = false)
        {
            if (string.IsNullOrEmpty(receiverName))
                throw new ArgumentException($"{nameof(receiverName)} can not be null or empty.", nameof(receiverName));

            // just in case we're already connected
            Disconnect();

            // Sanity
            if (source == null || source.IsNone)
                return;

            // create a new instance connected to this source
            _recvInstanceProvider = NativeFactory.CreateReceiver(source.Name, receiverName, colorFormat, bandwidth, allowVideoFields)
                .ShareInParallel();
            _recvInstanceHandle = _recvInstanceProvider.GetHandle();

            // did it work?
            System.Diagnostics.Debug.Assert(_recvInstancePtr != IntPtr.Zero, "Failed to create NDI receive instance.");

            if (_recvInstancePtr != IntPtr.Zero)
            {
                // We are now going to mark this source as being on program output for tally purposes (but not on preview)
                SetTallyIndicators(true, false);

                _videoStream = new VideoStream(Observable.Create<IResourceProvider<VideoFrame>>(PushVideoFrames).Publish().RefCount());
                _audioStream = new AudioStream(Observable.Create<IResourceProvider<AudioFrame>>(PushAudioFrames).Publish().RefCount());
                _metadataStream = Observable.Create<string>(PushMetadataFrames).Publish().RefCount();

                _syncInstanceProvider = _recvInstanceProvider.CreateSync().ShareInParallel();

                async Task PushVideoFrames(IObserver<IResourceProvider<VideoFrame>> observer, CancellationToken token)
                {
                    var recvInstanceProvider = _recvInstanceProvider;
                    using var nativeReceiverHandle = recvInstanceProvider.GetHandle();
                    var recvInstancePtr = nativeReceiverHandle.Resource;

                    await Task.Run(async () =>
                    {
                        while (!token.IsCancellationRequested)
                        {
                            if (!Enabled)
                            {
                                await Task.Delay(500);
                                continue;
                            }

                            NDIlib.video_frame_v2_t nativeVideoFrame = new NDIlib.video_frame_v2_t();

                            switch (NDIlib.recv_capture_v2(recvInstancePtr, ref nativeVideoFrame, ref Utils.NULL<NDIlib.audio_frame_v2_t>(), ref Utils.NULL<NDIlib.metadata_frame_t>(), 30))
                            {
                                // Video data
                                case NDIlib.frame_type_e.frame_type_video:

                                    // if not enabled, just discard
                                    // this can also occasionally happen when changing sources
                                    if (nativeVideoFrame.p_data == IntPtr.Zero)
                                    {
                                        // alreays free received frames
                                        NDIlib.recv_free_video_v2(recvInstancePtr, ref nativeVideoFrame);

                                        break;
                                    }

                                    var (memoryOwner, videoFrame) = Utils.CreateVideoFrame(ref nativeVideoFrame);

                                    var recvInstanceHandle = recvInstanceProvider.GetHandle();
                                    var videoFrameProvider = ResourceProvider.Return(videoFrame, (memoryOwner, recvInstanceHandle, nativeVideoFrame),
                                        disposeAction: static x =>
                                        {
                                            var (memoryOwner, recvInstanceHandle, nativeVideoFrame) = x;
                                            memoryOwner.Dispose();
                                            NDIlib.recv_free_video_v2(recvInstanceHandle.Resource, ref nativeVideoFrame);
                                            recvInstanceHandle.Dispose();
                                        });

                                    using (videoFrameProvider.GetHandle())
                                    {
                                        // Tell consumers about it
                                        observer.OnNext(videoFrameProvider);
                                    }

                                    break;

                                default:
                                    await Task.Yield();
                                    break;
                            }
                        }
                    }, token);
                }

                async Task PushAudioFrames(IObserver<IResourceProvider<AudioFrame>> observer, CancellationToken token)
                {
                    var recvInstanceProvider = _recvInstanceProvider;
                    using var nativeReceiverHandle = recvInstanceProvider.GetHandle();
                    var recvInstancePtr = nativeReceiverHandle.Resource;

                    await Task.Run(async () =>
                    {
                        while (!token.IsCancellationRequested)
                        {
                            if (!Enabled)
                            {
                                await Task.Delay(500);
                                continue;
                            }

                            NDIlib.audio_frame_v2_t nativeAudioFrame = new NDIlib.audio_frame_v2_t();

                            switch (NDIlib.recv_capture_v2(recvInstancePtr, ref Utils.NULL<NDIlib.video_frame_v2_t>(), ref nativeAudioFrame, ref Utils.NULL<NDIlib.metadata_frame_t>(), 30))
                            {
                                case NDIlib.frame_type_e.frame_type_audio:

                                    if (nativeAudioFrame.p_data == IntPtr.Zero)
                                    {
                                        // alreays free received frames
                                        NDIlib.recv_free_audio_v2(recvInstancePtr, ref nativeAudioFrame);
                                        break;
                                    }

                                    var (bufferOwner, audioFrame) = Utils.CreateAudioFrame(ref nativeAudioFrame, isInterleaved: false);

                                    var audioFrameProvider = ResourceProvider.Return(audioFrame, (recvInstanceProvider.GetHandle(), bufferOwner, nativeAudioFrame), static x =>
                                    {
                                        var (recvInstanceHandle, bufferOwner, nativeAudioFrame) = x;
                                        // Free allocated memory
                                        bufferOwner.Dispose();
                                        // Free audio frame
                                        NDIlib.framesync_free_audio(recvInstanceHandle.Resource, ref nativeAudioFrame);
                                        // Release the sync instance
                                        recvInstanceHandle.Dispose();
                                    });

                                    using (audioFrameProvider.GetHandle())
                                    {
                                        // Tell consumers about it
                                        observer.OnNext(audioFrameProvider);
                                    }

                                    break;

                                default:
                                    await Task.Yield();
                                    break;
                            }
                        }
                    }, token);
                }

                async Task PushMetadataFrames(IObserver<string> observer, CancellationToken token)
                {
                    var recvInstanceProvider = _recvInstanceProvider;
                    using var nativeReceiverHandle = recvInstanceProvider.GetHandle();
                    var recvInstancePtr = nativeReceiverHandle.Resource;

                    await Task.Run(async () =>
                    {
                        while (!token.IsCancellationRequested)
                        {
                            if (!Enabled)
                            {
                                await Task.Delay(500);
                                continue;
                            }

                            NDIlib.metadata_frame_t nativeMetadataFrame = new NDIlib.metadata_frame_t();

                            switch (NDIlib.recv_capture_v2(recvInstancePtr, ref Utils.NULL<NDIlib.video_frame_v2_t>(), ref Utils.NULL<NDIlib.audio_frame_v2_t>(), ref nativeMetadataFrame, 30))
                            {
                                // Video data
                                case NDIlib.frame_type_e.frame_type_metadata:

                                    // UTF-8 strings must be converted for use - length includes the terminating zero
                                    var metadata = Utils.Utf8ToString(nativeMetadataFrame.p_data, nativeMetadataFrame.length - 1);

                                    // free frames that were received
                                    NDIlib.recv_free_metadata(_recvInstancePtr, ref nativeMetadataFrame);

                                    // Tell consumers about it
                                    observer.OnNext(metadata);

                                    break;

                                default:
                                    await Task.Yield();
                                    break;
                            }
                        }
                    }, token);
                }
            }
        }

        public void Disconnect()
        {
            // in case we're connected, reset the tally indicators
            SetTallyIndicators(false, false);

            // Destroy the receiver
            _recvInstanceHandle?.Dispose();
            _recvInstanceHandle = null;

            _syncInstanceProvider = null;

            // set it to a safe value
            _recvInstanceProvider = null;
            _videoStream = null;
            _audioStream = null;
            _metadataStream = null;
        }

        public void SetTallyIndicators(bool onProgram, bool onPreview)
        {
            // we need to have a receive instance
            if (_recvInstancePtr != IntPtr.Zero)
            {
                // set up a state descriptor
                NDIlib.tally_t tallyState = new NDIlib.tally_t()
                {
                    on_program = onProgram,
                    on_preview = onPreview
                };

                // set it on the receiver instance
                NDIlib.recv_set_tally(_recvInstancePtr, ref tallyState);
            }
        }

        protected override void Destroy(bool disposing)
        {
            Disconnect();
        }
    }
}
