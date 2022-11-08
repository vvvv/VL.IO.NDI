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

namespace VL.IO.NDI
{
    /// <summary>
    /// If you do not use this control, you can remove this file
    /// and remove the dependency on naudio.
    /// Alternatively you can also remove any naudio related entries
    /// and use it for video only, but don't forget that you will still need
    /// to free any audio frames received.
    /// </summary>
    public class Receiver : NativeObject
    {
        private IObservable<IResourceProvider<VideoFrame>> _imageStream;
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
        public IObservable<IResourceProvider<VideoFrame>> ImageStream => _imageStream ?? Observable.Empty<IResourceProvider<VideoFrame>>();

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

                _imageStream = Observable.Create<IResourceProvider<VideoFrame>>(PushVideoFrames).PubRefCount();
                _metadataStream = Observable.Create<string>(PushMetadataFrames).PubRefCount();

                _syncInstanceProvider = _recvInstanceProvider.CreateSync().ShareInParallel();

                async Task PushVideoFrames(IObserver<IResourceProvider<VideoFrame>> observer, CancellationToken token)
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

                                    var image = nativeVideoFrame.ToImage();
                                    var videoFrame = new VideoFrame(image, Utils.Utf8ToString(nativeVideoFrame.p_metadata));
                                    var imageProvider = recvInstanceProvider.Bind(r => ResourceProvider.Return(videoFrame, i =>
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

        //// the receive thread runs though this loop until told to exit
        //void ReceiveThreadProc()
        //{
        //    using var videoFrameSubscription = new SerialDisposable();
        //    while (/*!_exitThread && */_recvInstancePtr != IntPtr.Zero)
        //    {
        //        // The descriptors
        //        NDIlib.video_frame_v2_t nativeVideoFrame = new NDIlib.video_frame_v2_t();
        //        NDIlib.audio_frame_v2_t audioFrame = new NDIlib.audio_frame_v2_t();
        //        NDIlib.metadata_frame_t metadataFrame = new NDIlib.metadata_frame_t();

        //        switch (NDIlib.recv_capture_v2(_recvInstancePtr, ref nativeVideoFrame, ref audioFrame, ref metadataFrame, 1000))
        //        {
        //            // No data
        //            case NDIlib.frame_type_e.frame_type_none:
        //                // No data received
        //                break;

        //            // frame settings - check for extended functionality
        //            case NDIlib.frame_type_e.frame_type_status_change:
        //                // check for PTZ
        //                IsPtz = NDIlib.recv_ptz_is_supported(_recvInstancePtr);

        //                // Check for recording
        //                IsRecordingSupported = NDIlib.recv_recording_is_supported(_recvInstancePtr);

        //                // Check for a web control URL
        //                // We must free this string ptr if we get one.
        //                IntPtr webUrlPtr = NDIlib.recv_get_web_control(_recvInstancePtr);
        //                if (webUrlPtr == IntPtr.Zero)
        //                {
        //                    WebControlUrl = String.Empty;
        //                }
        //                else
        //                {
        //                    // convert to managed String
        //                    WebControlUrl = UTF.Utf8ToString(webUrlPtr);

        //                    // Don't forget to free the string ptr
        //                    NDIlib.recv_free_string(_recvInstancePtr, webUrlPtr);
        //                }

        //                break;

        //            // audio is beyond the scope of this example
        //            case NDIlib.frame_type_e.frame_type_audio:
                      
        //                // if no audio or disabled, nothing to do
        //                if (!_audioEnabled || audioFrame.p_data == IntPtr.Zero || audioFrame.no_samples == 0)
        //                {
        //                    // alreays free received frames
        //                    NDIlib.recv_free_audio_v2(_recvInstancePtr, ref audioFrame);

        //                    break;
        //                }

        //                // we're working in bytes, so take the size of a 32 bit sample (float) into account
        //                int sizeInBytes = (int)audioFrame.no_samples * (int)audioFrame.no_channels * sizeof(float);

        //                // NAudio is expecting interleaved audio and NDI uses planar.
        //                // create an interleaved frame and convert from the one we received
        //                NDIlib.audio_frame_interleaved_32f_t interleavedFrame = new NDIlib.audio_frame_interleaved_32f_t()
        //                {
        //                    sample_rate = audioFrame.sample_rate,
        //                    no_channels = audioFrame.no_channels,
        //                    no_samples = audioFrame.no_samples,
        //                    timecode = audioFrame.timecode
        //                };

        //                // we need a managed byte array to add to buffered provider
        //                byte[] audBuffer = new byte[sizeInBytes];

        //                // pin the byte[] and get a GC handle to it
        //                // doing it this way saves an expensive Marshal.Alloc/Marshal.Copy/Marshal.Free later
        //                // the data will only be moved once, during the fast interleave step that is required anyway
        //                GCHandle handle = GCHandle.Alloc(audBuffer, GCHandleType.Pinned);

        //                // access it by an IntPtr and use it for our interleaved audio buffer
        //                interleavedFrame.p_data = handle.AddrOfPinnedObject();

        //                // Convert from float planar to float interleaved audio
        //                // There is a matching version of this that converts to interleaved 16 bit audio frames if you need 16 bit

        //                NDIlib.util_audio_to_interleaved_32f_v2(ref audioFrame, ref interleavedFrame);

        //                // release the pin on the byte[]
        //                // never try to access p_data after the byte[] has been unpinned!
        //                // that IntPtr will no longer be valid.
        //                handle.Free();

        //                int channelStride = audioFrame.channel_stride_in_bytes;

        //                var floatBuffer = ConvertByteArrayToFloat(audBuffer, channelStride);

        //                float[] outBuffer = new float[512];

        //                Buffer.BlockCopy(floatBuffer, 0, outBuffer, 0, 512);

        //                audioOutSignal.Read(outBuffer, 0, 512);

        //                // free the frame that was received
        //                NDIlib.recv_free_audio_v2(_recvInstancePtr, ref audioFrame);

        //                break;

        //        }

        //    }
        //}

        //static float[] ConvertByteArrayToFloat(byte[] bytes, int stopAt)
        //{
        //    if (bytes.Length % 4 != 0) throw new ArgumentException();

        //    float[] floats = new float[bytes.Length / 4];
        //    for (int i = 0; i < floats.Length; i++)
        //    {
        //        if (i >= stopAt)
        //            break;
        //        else
        //            floats[i] = BitConverter.ToSingle(bytes, i * 4);
        //    }

        //    return floats;
        //}

        protected override void Destroy(bool disposing)
        {
            Disconnect();
        }
    }

    
}
