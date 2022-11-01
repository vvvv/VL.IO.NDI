//using NAudio.Wave;
using NewTek;
using System;

using VL.Lib.Basics.Imaging;
using VL.Lib.Basics.Resources;
using System.Reactive.Disposables;
using System.Collections.Generic;

namespace VL.IO.NDI
{
    public unsafe class Synchronizer : NativeObject
    {
        private readonly SerialDisposable imageSubscription = new SerialDisposable();

        // our unmanaged NDI sync instance
        private IResourceProvider<IntPtr> _syncInstanceProvider;
        private IResourceHandle<IntPtr> _syncInstanceHandle;

        private IntPtr GetSyncInstance()
        {
            var syncInstance = Receiver.SyncInstanceProvider;
            if (syncInstance != _syncInstanceProvider)
            {
                _syncInstanceProvider = syncInstance;
                _syncInstanceHandle?.Dispose();
                _syncInstanceHandle = syncInstance?.GetHandle();
            }
            return _syncInstanceHandle?.Resource ?? default;
        }

        public Receiver Receiver { get; set; }

        public IResourceProvider<VideoFrame> PullVideoFrame()
        {
            var syncInstance = GetSyncInstance();
            if (syncInstance == default)
                return ResourceProvider.Default<VideoFrame>.GetInstance(default);

            var nativeVideoFrame = new NDIlib.video_frame_v2_t();
            NDIlib.framesync_capture_video(syncInstance, ref nativeVideoFrame, NDIlib.frame_format_type_e.frame_format_type_interleaved);

            if (nativeVideoFrame.p_data == default)
                return ResourceProvider.Default<VideoFrame>.GetInstance(default);

            var image = nativeVideoFrame.ToImage();
            var videoFrame = new VideoFrame(image, UTF.Utf8ToString(nativeVideoFrame.p_metadata));
            var imageProvider = _syncInstanceProvider.Bind(s => ResourceProvider.Return(videoFrame, i =>
            {
                image.Dispose();

                // Free video frame
                NDIlib.framesync_free_video(s, ref nativeVideoFrame);
            })).ShareInParallel();

            imageSubscription.Disposable = imageProvider.GetHandle();

            return imageProvider;
        }

        public IResourceProvider<IImage> PullImage()
        {
            return PullVideoFrame().Bind(v => v?.Image);
        }

        public IResourceProvider<AudioFrame> PullAudioFrame()
        {
            throw new NotImplementedException();
        }

        protected override void Destroy(bool disposing)
        {
            imageSubscription.Dispose();
            _syncInstanceHandle?.Dispose();
            _syncInstanceHandle = null;
        }
    }
}
