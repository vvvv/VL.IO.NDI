using NewTek;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Reactive.Subjects;

using VL.Audio;
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
        #region private properties
        private IObservable<IResourceProvider<VideoFrame>> _imageStream;
        private IObservable<string> _metadataStream;

        private AudioOut audioOutSignal = new AudioOut();


        // our unmanaged NDI receiver instance
        private IResourceProvider<IntPtr> _recvInstanceProvider;
        private IResourceHandle<IntPtr> _recvInstanceHandle;
        private IResourceProvider<IntPtr> _syncInstanceProvider;

        private IntPtr _recvInstancePtr => _recvInstanceHandle != null ? _recvInstanceHandle.Resource : default;

        // should we send audio to Windows or not?
        private bool _audioEnabled = false;

        // should we send video to Windows or not?
        private bool _videoEnabled = true;

        //// the current audio volume
        //private float _volume = 1.0f;

        private bool _isPtz = false;
        private bool _canRecord = false;
        private String _webControlUrl = String.Empty;
        private String _receiverName = String.Empty;

        private Source _connectedSource;
        #endregion

        #region public properties
        /// <summary>
        /// The name of this receiver channel. Required or else an invalid argument exception will be thrown.
        /// </summary>
        public String ReceiverName
        {
            get { return _receiverName; }
            set { _receiverName = value; }
        }

        /// <summary>
        /// The NDI source to connect to. An empty new Source() or a Source with no Name will disconnect.
        /// </summary>
        public Source ConnectedSource
        {
            get { return _connectedSource; }
            set { _connectedSource = value; }
        }


        /// <summary>
        /// If true (default) received audio will be sent to the default Windows audio playback device.
        /// </summary>
        public bool IsAudioEnabled
        {
            get { return _audioEnabled; }
            set { _audioEnabled = value; }
        }

        /// <summary>
        /// If true (default) received video will be sent to the screen.
        /// </summary>
        public bool IsVideoEnabled
        {
            get { return _videoEnabled; }
            set { _videoEnabled = value; }
        }

        //[Category("NewTek NDI"),
        //Description("Set or get the current audio volume. Range is 0.0 to 1.0")]
        //public float Volume
        //{
        //    get { return _volume; }
        //    set
        //    {
        //        if (value != _volume)
        //        {
        //            _volume = Math.Max(0.0f, Math.Min(1.0f, value));

        //            if (_wasapiOut != null)
        //                _wasapiOut.Volume = _volume;

        //            NotifyPropertyChanged("Volume");
        //        }
        //    }
        //}

        /// <summary>
        /// Does the current source support PTZ functionality?
        /// </summary>
        public bool IsPtz
        {
            get { return _isPtz; }
            set { _isPtz = value; }
        }

        /// <summary>
        /// Does the current source support record functionality?
        /// </summary>
        public bool IsRecordingSupported
        {
            get { return _canRecord; }
            set { _canRecord = value; }
        }

        /// <summary>
        /// The web control URL for the current device, as a String, or an Empty String if not supported.
        /// </summary>
        public String WebControlUrl
        {
            get { return _webControlUrl; }
            set { _webControlUrl = value; }
        }

        /// <summary>
        /// Received Images
        /// </summary>
        public IObservable<IResourceProvider<VideoFrame>> ImageStream => _imageStream ?? Observable.Empty<IResourceProvider<VideoFrame>>();

        internal IResourceProvider<IntPtr> SyncInstanceProvider => _syncInstanceProvider;

        /// <summary>
        /// Received Metadata
        /// </summary>
        public IObservable<string> Metadata => _metadataStream ?? Observable.Empty<string>();
        #endregion  

        /// <summary>
        /// 
        /// </summary>
        public AudioSignal AudioOutput => audioOutSignal;

        public Receiver()
        {

        }

        #region PTZ Methods
        public bool SetPtzZoom(double value)
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_zoom(_recvInstancePtr, (float)value);
        }

        public bool SetPtzZoomSpeed(double value)
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_zoom_speed(_recvInstancePtr, (float)value);
        }

        public bool SetPtzPanTilt(double pan, double tilt)
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_pan_tilt(_recvInstancePtr, (float)pan, (float)tilt);
        }

        public bool SetPtzPanTiltSpeed(double panSpeed, double tiltSpeed)
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_pan_tilt_speed(_recvInstancePtr, (float)panSpeed, (float)tiltSpeed);
        }

        public bool PtzStorePreset(int index)
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero || index < 0 || index > 99)
                return false;

            return NDIlib.recv_ptz_store_preset(_recvInstancePtr, index);
        }

        public bool PtzRecallPreset(int index, double speed)
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero || index < 0 || index > 99)
                return false;

            return NDIlib.recv_ptz_recall_preset(_recvInstancePtr, index, (float)speed);
        }

        public bool PtzAutoFocus()
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_auto_focus(_recvInstancePtr);
        }

        public bool SetPtzFocusSpeed(double speed)
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_focus_speed(_recvInstancePtr, (float)speed);
        }

        public bool PtzWhiteBalanceAuto()
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_white_balance_auto(_recvInstancePtr);
        }

        public bool PtzWhiteBalanceIndoor()
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_white_balance_indoor(_recvInstancePtr);
        }

        public bool PtzWhiteBalanceOutdoor()
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_white_balance_outdoor(_recvInstancePtr);
        }

        public bool PtzWhiteBalanceOneShot()
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_white_balance_oneshot(_recvInstancePtr);
        }

        public bool PtzWhiteBalanceManual(double red, double blue)
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_white_balance_manual(_recvInstancePtr, (float)red, (float)blue);
        }

        public bool PtzExposureAuto()
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_exposure_auto(_recvInstancePtr);
        }

        public bool PtzExposureManual(double level)
        {
            if (!_isPtz || _recvInstancePtr == IntPtr.Zero)
                return false;

            return NDIlib.recv_ptz_exposure_manual(_recvInstancePtr, (float)level);
        }

        #endregion PTZ Methods

        /// <summary>
        /// when the ConnectedSource changes, connect to it.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void OnConnectedSourceChanged(object sender, EventArgs e)
        {
            Receiver s = sender as Receiver;
            if (s == null)
                return;

            s.Connect(s.ConnectedSource);
        }

        /// <summary>
        /// connect to an NDI source in our Dictionary by name
        /// </summary>
        /// <param name="source"></param>
        /// <param name="colorFormat"></param>
        /// <param name="bandwidth"></param>
        /// <param name="allowVideoFields"></param>
        public void Connect(Source source, 
            NDIlib.recv_color_format_e colorFormat = NDIlib.recv_color_format_e.recv_color_format_BGRX_BGRA,
            NDIlib.recv_bandwidth_e bandwidth = NDIlib.recv_bandwidth_e.recv_bandwidth_highest, 
            bool allowVideoFields = false)
        {
            //if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            //    return;

            if (String.IsNullOrEmpty(ReceiverName))
                throw new ArgumentException("sourceName can not be null or empty.", ReceiverName);

            // just in case we're already connected
            Disconnect();

            // Sanity
            if (source == null || String.IsNullOrEmpty(source.Name))
                return;

            // create a new instance connected to this source
            _recvInstanceProvider = NativeFactory.CreateReceiver(source.Name, ReceiverName, colorFormat, bandwidth, allowVideoFields)
                .ShareInParallel();
            _recvInstanceHandle = _recvInstanceProvider.GetHandle();

            // did it work?
            System.Diagnostics.Debug.Assert(_recvInstancePtr != IntPtr.Zero, "Failed to create NDI receive instance.");

            if (_recvInstancePtr != IntPtr.Zero)
            {
                // We are now going to mark this source as being on program output for tally purposes (but not on preview)
                SetTallyIndicators(true, false);

                //// start up a thread to receive on
                //_receiveThread = new Thread(ReceiveThreadProc) { IsBackground = true, Name = "NdiExampleReceiveThread" };
                //_receiveThread.Start();

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
                            NDIlib.video_frame_v2_t nativeVideoFrame = new NDIlib.video_frame_v2_t();

                            switch (NDIlib.recv_capture_v2(recvInstancePtr, ref nativeVideoFrame, ref NativeUtils.NULL<NDIlib.audio_frame_v2_t>(), ref NativeUtils.NULL<NDIlib.metadata_frame_t>(), 30))
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
                                    var videoFrame = new VideoFrame(image, UTF.Utf8ToString(nativeVideoFrame.p_metadata));
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
                            NDIlib.metadata_frame_t nativeMetadataFrame = new NDIlib.metadata_frame_t();

                            switch (NDIlib.recv_capture_v2(recvInstancePtr, ref NativeUtils.NULL<NDIlib.video_frame_v2_t>(), ref NativeUtils.NULL<NDIlib.audio_frame_v2_t>(), ref nativeMetadataFrame, 30))
                            {
                                // Video data
                                case NDIlib.frame_type_e.frame_type_metadata:

                                    // UTF-8 strings must be converted for use - length includes the terminating zero
                                    var metadata = UTF.Utf8ToString(nativeMetadataFrame.p_data, nativeMetadataFrame.length - 1);

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

            // set function status to defaults
            IsPtz = false;
            IsRecordingSupported = false;
            WebControlUrl = String.Empty;
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

        // the receive thread runs though this loop until told to exit
        void ReceiveThreadProc()
        {
            using var videoFrameSubscription = new SerialDisposable();
            while (/*!_exitThread && */_recvInstancePtr != IntPtr.Zero)
            {
                // The descriptors
                NDIlib.video_frame_v2_t nativeVideoFrame = new NDIlib.video_frame_v2_t();
                NDIlib.audio_frame_v2_t audioFrame = new NDIlib.audio_frame_v2_t();
                NDIlib.metadata_frame_t metadataFrame = new NDIlib.metadata_frame_t();

                switch (NDIlib.recv_capture_v2(_recvInstancePtr, ref nativeVideoFrame, ref audioFrame, ref metadataFrame, 1000))
                {
                    // No data
                    case NDIlib.frame_type_e.frame_type_none:
                        // No data received
                        break;

                    // frame settings - check for extended functionality
                    case NDIlib.frame_type_e.frame_type_status_change:
                        // check for PTZ
                        IsPtz = NDIlib.recv_ptz_is_supported(_recvInstancePtr);

                        // Check for recording
                        IsRecordingSupported = NDIlib.recv_recording_is_supported(_recvInstancePtr);

                        // Check for a web control URL
                        // We must free this string ptr if we get one.
                        IntPtr webUrlPtr = NDIlib.recv_get_web_control(_recvInstancePtr);
                        if (webUrlPtr == IntPtr.Zero)
                        {
                            WebControlUrl = String.Empty;
                        }
                        else
                        {
                            // convert to managed String
                            WebControlUrl = UTF.Utf8ToString(webUrlPtr);

                            // Don't forget to free the string ptr
                            NDIlib.recv_free_string(_recvInstancePtr, webUrlPtr);
                        }

                        break;

                    // audio is beyond the scope of this example
                    case NDIlib.frame_type_e.frame_type_audio:
                      
                        // if no audio or disabled, nothing to do
                        if (!_audioEnabled || audioFrame.p_data == IntPtr.Zero || audioFrame.no_samples == 0)
                        {
                            // alreays free received frames
                            NDIlib.recv_free_audio_v2(_recvInstancePtr, ref audioFrame);

                            break;
                        }

                        // we're working in bytes, so take the size of a 32 bit sample (float) into account
                        int sizeInBytes = (int)audioFrame.no_samples * (int)audioFrame.no_channels * sizeof(float);

                        // NAudio is expecting interleaved audio and NDI uses planar.
                        // create an interleaved frame and convert from the one we received
                        NDIlib.audio_frame_interleaved_32f_t interleavedFrame = new NDIlib.audio_frame_interleaved_32f_t()
                        {
                            sample_rate = audioFrame.sample_rate,
                            no_channels = audioFrame.no_channels,
                            no_samples = audioFrame.no_samples,
                            timecode = audioFrame.timecode
                        };

                        // we need a managed byte array to add to buffered provider
                        byte[] audBuffer = new byte[sizeInBytes];

                        // pin the byte[] and get a GC handle to it
                        // doing it this way saves an expensive Marshal.Alloc/Marshal.Copy/Marshal.Free later
                        // the data will only be moved once, during the fast interleave step that is required anyway
                        GCHandle handle = GCHandle.Alloc(audBuffer, GCHandleType.Pinned);

                        // access it by an IntPtr and use it for our interleaved audio buffer
                        interleavedFrame.p_data = handle.AddrOfPinnedObject();

                        // Convert from float planar to float interleaved audio
                        // There is a matching version of this that converts to interleaved 16 bit audio frames if you need 16 bit

                        NDIlib.util_audio_to_interleaved_32f_v2(ref audioFrame, ref interleavedFrame);

                        // release the pin on the byte[]
                        // never try to access p_data after the byte[] has been unpinned!
                        // that IntPtr will no longer be valid.
                        handle.Free();

                        int channelStride = audioFrame.channel_stride_in_bytes;

                        var floatBuffer = ConvertByteArrayToFloat(audBuffer, channelStride);

                        float[] outBuffer = new float[512];

                        Buffer.BlockCopy(floatBuffer, 0, outBuffer, 0, 512);

                        audioOutSignal.Read(outBuffer, 0, 512);

                        // free the frame that was received
                        NDIlib.recv_free_audio_v2(_recvInstancePtr, ref audioFrame);

                        break;

                }

            }
        }

        static float[] ConvertByteArrayToFloat(byte[] bytes, int stopAt)
        {
            if (bytes.Length % 4 != 0) throw new ArgumentException();

            float[] floats = new float[bytes.Length / 4];
            for (int i = 0; i < floats.Length; i++)
            {
                if (i >= stopAt)
                    break;
                else
                    floats[i] = BitConverter.ToSingle(bytes, i * 4);
            }

            return floats;
        }

        protected override void Destroy(bool disposing)
        {
            Disconnect();
        }
    }

    
}
