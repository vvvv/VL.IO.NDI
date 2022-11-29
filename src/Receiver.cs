using NewTek;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Reactive.Subjects;

using VL.Lib.Basics.Resources;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using VL.Lib.Reactive;
using VL.Lib.Basics.Imaging;
using System.Collections.Generic;
using System.Linq;

namespace VL.IO.NDI
{
    public sealed class Receiver : NativeObject
    {
        private IObservable<IResourceProvider<IImage>> _imageStream;
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
        public IObservable<IResourceProvider<IImage>> ImageStream => _imageStream ?? Observable.Empty<IResourceProvider<IImage>>();

        /// <summary>
        /// Received Metadata
        /// </summary>
        public IObservable<string> Metadata => _metadataStream ?? Observable.Empty<string>();

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

                _imageStream = Observable.Create<IResourceProvider<IImage>>(PushVideoFrames).Publish().RefCount();
                _metadataStream = Observable.Create<string>(PushMetadataFrames).Publish().RefCount();

                _syncInstanceProvider = _recvInstanceProvider.CreateSync().ShareInParallel();

                async Task PushVideoFrames(IObserver<IResourceProvider<IImage>> observer, CancellationToken token)
                {
                    using var videoFrameSubscription = new SerialDisposable();
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

                                    var image = nativeVideoFrame.ToImage(Utils.Utf8ToString(nativeVideoFrame.p_metadata));
                                    var imageProvider = recvInstanceProvider.Bind(r => ResourceProvider.Return(image, i =>
                                    {
                                        image.Dispose();
                                        NDIlib.recv_free_video_v2(r, ref nativeVideoFrame);
                                    })).ShareInParallel();

                                    // Release the previous frame and hold on to this one
                                    videoFrameSubscription.Disposable = imageProvider.GetHandle();

                                    // Tell consumers about it
                                    observer.OnNext(imageProvider);

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
            _imageStream = null;
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
