using NewTek;
using CommunityToolkit.HighPerformance;
using System;
using VL.Lib.Basics.Resources;
using System.Reactive.Disposables;
using VL.Lib.Basics.Audio;
using VL.Lib.Basics.Imaging;
using System.Buffers;
using VL.Core;

namespace VL.IO.NDI
{
    public unsafe class Synchronizer : NativeObject, IAudioSource<float>
    {
        private readonly SerialDisposable videoFrameSubscription = new SerialDisposable();

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

        public IResourceProvider<IImage> ReceiveVideoFrame()
        {
            var syncInstance = GetSyncInstance();
            if (syncInstance == default)
                return null;

            var nativeVideoFrame = new NDIlib.video_frame_v2_t();
            NDIlib.framesync_capture_video(syncInstance, ref nativeVideoFrame, NDIlib.frame_format_type_e.frame_format_type_interleaved);

            if (nativeVideoFrame.p_data == default)
                return null;

            var image = nativeVideoFrame.ToImage(Utils.Utf8ToString(nativeVideoFrame.p_metadata));
            var imageProvider = _syncInstanceProvider.Bind(s => ResourceProvider.Return(image, i =>
            {
                image.Dispose();

                // Free video frame
                NDIlib.framesync_free_video(s, ref nativeVideoFrame);
            })).ShareInParallel();

            videoFrameSubscription.Disposable = imageProvider.GetHandle();

            return imageProvider;
        }

        public IResourceHandle<AudioFrame<float>> GrabAudioFrame(int sampleCount, Optional<int> sampleRate, Optional<int> channelCount, Optional<bool> interleaved)
        {
            var syncInstance = GetSyncInstance();
            if (syncInstance == default)
                return ResourceProvider.NewHandle(AudioFrame<float>.Empty, () => { });

            var nativeAudioFrame = new NDIlib.audio_frame_v2_t();
            NDIlib.framesync_capture_audio(syncInstance, ref nativeAudioFrame, sampleRate.Value, channelCount.Value, sampleCount);

            if (nativeAudioFrame.p_data == default)
            {
                NDIlib.framesync_free_audio(syncInstance, ref nativeAudioFrame);
                return ResourceProvider.NewHandle(AudioFrame<float>.Empty, () => { });
            }

            IMemoryOwner<float> bufferOwner;
            AudioFrame<float> audioFrame;

            if (interleaved.Value)
            {
                bufferOwner = Utils.GetInterleavedBuffer(ref nativeAudioFrame);

                audioFrame = new AudioFrame<float>(
                    bufferOwner.Memory.AsMemory2D(nativeAudioFrame.no_samples, nativeAudioFrame.no_channels),
                    nativeAudioFrame.sample_rate,
                    IsInterleaved: true,
                    Metadata: Utils.Utf8ToString(nativeAudioFrame.p_metadata));
            }
            else
            {
                bufferOwner = Utils.GetPlanarBuffer(ref nativeAudioFrame);

                audioFrame = new AudioFrame<float>(
                    bufferOwner.Memory.AsMemory2D(nativeAudioFrame.no_channels, nativeAudioFrame.no_samples),
                    nativeAudioFrame.sample_rate,
                    IsInterleaved: false,
                    Metadata: Utils.Utf8ToString(nativeAudioFrame.p_metadata));
            }

            return ResourceProvider.NewHandle(audioFrame, (syncInstance, bufferOwner, nativeAudioFrame), x =>
            {
                var (s, bufferOwner, nativeAudioFrame) = x;
                // Free allocated memory
                bufferOwner.Dispose();
                // Free audio frame
                NDIlib.framesync_free_audio(s, ref nativeAudioFrame);
            });
        }

        protected override void Destroy(bool disposing)
        {
            videoFrameSubscription.Dispose();
            _syncInstanceHandle?.Dispose();
            _syncInstanceHandle = null;
        }
    }
}
