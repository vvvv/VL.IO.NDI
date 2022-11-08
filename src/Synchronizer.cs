using NewTek;

using System;
using VL.Lib.Basics.Resources;
using System.Reactive.Disposables;

namespace VL.IO.NDI
{
    public unsafe class Synchronizer : NativeObject
    {
        private readonly SerialDisposable videoFrameSubscription = new SerialDisposable();
        private readonly SerialDisposable audioFrameSubscription = new SerialDisposable();

        // our unmanaged NDI sync instance
        private IResourceProvider<IntPtr> _syncInstanceProvider;
        private IResourceHandle<IntPtr> _syncInstanceHandle;

        private IntPtr GetSyncInstance()
        {
            return _syncInstanceHandle?.Resource ?? default;
        }

        public Receiver Receiver
        {
            set
            {
                var syncInstance = value?.SyncInstanceProvider;
                if (syncInstance != _syncInstanceProvider)
                {
                    _syncInstanceProvider = syncInstance;
                    _syncInstanceHandle?.Dispose();
                    _syncInstanceHandle = syncInstance?.GetHandle();
                }
            }
        }

        public IResourceProvider<VideoFrame> ReceiveVideoFrame()
        {
            var syncInstance = GetSyncInstance();
            if (syncInstance == default)
                return null;

            var nativeVideoFrame = new NDIlib.video_frame_v2_t();
            NDIlib.framesync_capture_video(syncInstance, ref nativeVideoFrame, NDIlib.frame_format_type_e.frame_format_type_interleaved);

            if (nativeVideoFrame.p_data == default)
                return null;

            var image = nativeVideoFrame.ToImage();
            var videoFrame = new VideoFrame(image, Utils.Utf8ToString(nativeVideoFrame.p_metadata));
            var frameProvider = _syncInstanceProvider.Bind(s => ResourceProvider.Return(videoFrame, i =>
            {
                image.Dispose();

                // Free video frame
                NDIlib.framesync_free_video(s, ref nativeVideoFrame);
            })).ShareInParallel();

            videoFrameSubscription.Disposable = frameProvider.GetHandle();

            return frameProvider;
        }

        public IResourceProvider<AudioFrame> ReceiveAudioFrame(int sampleRate = 44000, int channelCount = 2, int sampleCount = 1024)
        {
            var syncInstance = GetSyncInstance();
            if (syncInstance == default)
                return null;

            var nativeAudioFrame = new NDIlib.audio_frame_v2_t();
            NDIlib.framesync_capture_audio(syncInstance, ref nativeAudioFrame, sampleRate, channelCount, sampleCount);

            if (nativeAudioFrame.p_data == default)
                return null;

            var bufferOwner = Utils.GetPlanarBuffer(ref nativeAudioFrame); // Utils.GetInterleavedBuffer(ref nativeAudioFrame);
            var size = nativeAudioFrame.no_samples * nativeAudioFrame.no_channels;
            var audioFrame = new AudioFrame(bufferOwner.Memory/*.Slice(0, size)*/, nativeAudioFrame.no_samples, nativeAudioFrame.no_channels, nativeAudioFrame.sample_rate, Utils.Utf8ToString(nativeAudioFrame.p_metadata));
            var frameProvider = _syncInstanceProvider.Bind(s => ResourceProvider.Return(audioFrame, i =>
            {
                // Free allocated memory
                bufferOwner.Dispose();
                // Free audio frame
                NDIlib.framesync_free_audio(s, ref nativeAudioFrame);
            })).ShareInParallel();

            audioFrameSubscription.Disposable = frameProvider.GetHandle();

            return frameProvider;
        }

        protected override void Destroy(bool disposing)
        {
            videoFrameSubscription.Dispose();
            audioFrameSubscription.Dispose();
            _syncInstanceHandle?.Dispose();
            _syncInstanceHandle = null;
        }
    }
}
