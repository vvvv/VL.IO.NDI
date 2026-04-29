using NewTek;
using System;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using VL.Core.Import;
using VL.Lib.Basics.Audio;
using VL.Lib.Basics.Resources;
using VL.Lib.Basics.Video;
using VL.Model;
using static NewTek.NDIlib;

namespace VL.IO.NDI
{
    [ProcessNode(Name = "NDIReceiver (Reactive Advanced)")]
    public sealed class Receiver_Reactive : IDisposable
    {
        private Source? source;
        private string? receiverName;
        private recv_color_format_e colorFormat;
        private recv_bandwidth_e bandwidth;
        private bool allowVideoFields;
        private bool onProgram, onPreview;

        private VideoStream? _videoStream;
        private AudioStream? _audioStream;
        private IObservable<string>? _metadataStream;

        // our unmanaged NDI receiver instance
        private IResourceHandle<IntPtr>? _recvInstanceHandle;

        private IntPtr _recvInstancePtr => _recvInstanceHandle != null ? _recvInstanceHandle.Resource : default;

        public void Update(Source source,
            [Pin(Visibility = PinVisibility.Optional)][DefaultValue("vvvv")] string receiverName,
            [Pin(Visibility = PinVisibility.Optional)] bool onProgram,
            [Pin(Visibility = PinVisibility.Optional)] bool onPreview,
            [DefaultValue(recv_bandwidth_e.recv_bandwidth_highest)] recv_bandwidth_e bandwidth,
            [Pin(Visibility = PinVisibility.Optional)][DefaultValue(recv_color_format_e.recv_color_format_BGRX_BGRA)] recv_color_format_e colorFormat,
            [Pin(Visibility = PinVisibility.Optional)] bool allowVideoFields,
            [DefaultValue(true)] bool enabled,
            out VideoStream? videoStream,
            out AudioStream? audioStream,
            out IObservable<string> metadataStream)
        {
            if (string.IsNullOrEmpty(receiverName))
                throw new ArgumentException($"{nameof(receiverName)} can not be null or empty.", nameof(receiverName));

            if (!enabled)
                source = Source.None;

            if (this.source != source || this.receiverName != receiverName || this.colorFormat != colorFormat || this.bandwidth != bandwidth || this.allowVideoFields != allowVideoFields)
            {
                this.source = source;
                this.receiverName = receiverName;
                this.colorFormat = colorFormat;
                this.bandwidth = bandwidth;
                this.allowVideoFields = allowVideoFields;

                Reconnect(source, receiverName, colorFormat, bandwidth, allowVideoFields);
            }

            if (this.onProgram != onProgram || this.onPreview != onPreview)
            {
                this.onProgram = onProgram;
                this.onPreview = onPreview;
                SetTallyIndicators(onProgram, onPreview);
            }

            videoStream = _videoStream;
            audioStream = _audioStream;
            metadataStream = _metadataStream ?? Observable.Empty<string>();
        }

        /// <summary>
        /// Connect to an NDI source.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="receiverName"></param>
        /// <param name="colorFormat"></param>
        /// <param name="bandwidth"></param>
        /// <param name="allowVideoFields"></param>
        private void Reconnect(Source source, string receiverName,
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
            var recvInstanceProvider = NativeFactory.CreateReceiver(source.Name, receiverName, colorFormat, bandwidth, allowVideoFields)
                .ShareInParallel();
            _recvInstanceHandle = recvInstanceProvider.GetHandle();

            _videoStream = new VideoStream(Observable.Create<IResourceProvider<VideoFrame>>(PushVideoFrames).Publish().RefCount());
            _audioStream = new AudioStream(Observable.Create<IResourceProvider<AudioFrame>>(PushAudioFrames).Publish().RefCount());
            _metadataStream = Observable.Create<string>(PushMetadataFrames).Publish().RefCount();

            async Task PushVideoFrames(IObserver<IResourceProvider<VideoFrame>> observer, CancellationToken token)
            {
                using var nativeReceiverHandle = recvInstanceProvider.GetHandle();
                var recvInstancePtr = nativeReceiverHandle.Resource;

                await Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
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
                using var nativeReceiverHandle = recvInstanceProvider.GetHandle();
                var recvInstancePtr = nativeReceiverHandle.Resource;

                await Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
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
                using var nativeReceiverHandle = recvInstanceProvider.GetHandle();
                var recvInstancePtr = nativeReceiverHandle.Resource;

                await Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
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

        private void Disconnect()
        {
            // in case we're connected, reset the tally indicators
            SetTallyIndicators(false, false);

            // Destroy the receiver
            _recvInstanceHandle?.Dispose();
            _recvInstanceHandle = null;

            // set it to a safe value
            _videoStream = null;
            _audioStream = null;
            _metadataStream = null;
        }

        private void SetTallyIndicators(bool onProgram, bool onPreview)
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

        public void Dispose()
        {
            Disconnect();
        }
    }
}
