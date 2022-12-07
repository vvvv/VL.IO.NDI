﻿using NewTek;
using CommunityToolkit.HighPerformance;
using System;
using VL.Lib.Basics.Resources;
using VL.Lib.Basics.Audio;
using System.Buffers;
using VL.Core;
using VL.Lib.Basics.Video;

namespace VL.IO.NDI
{
    public unsafe class Synchronizer : NativeObject, IAudioSource, IVideoSource
    {
        // our unmanaged NDI sync instance
        private IResourceProvider<IntPtr> _syncInstanceProvider;
        private IResourceHandle<IntPtr> _syncInstanceHandle;

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

        IResourceProvider<VideoFrame> IVideoSource.GrabVideoFrame()
        {
            var syncInstanceHandle = _syncInstanceProvider?.GetHandle();
            if (syncInstanceHandle is null)
                return null;

            var nativeVideoFrame = new NDIlib.video_frame_v2_t();
            NDIlib.framesync_capture_video(syncInstanceHandle.Resource, ref nativeVideoFrame, NDIlib.frame_format_type_e.frame_format_type_interleaved);

            if (nativeVideoFrame.p_data == default)
            {
                NDIlib.framesync_free_video(syncInstanceHandle.Resource, ref nativeVideoFrame);
                syncInstanceHandle.Dispose();
                return null;
            }

            var (memoryOwner, videoFrame) = Utils.CreateVideoFrame(ref nativeVideoFrame);

            return ResourceProvider.Return(videoFrame, (syncInstanceHandle, memoryOwner, nativeVideoFrame), static x =>
            {
                var (syncInstanceHandle, memoryOwner, nativeVideoFrame) = x;
                // Free allocated memory
                memoryOwner.Dispose();
                // Free video frame
                NDIlib.framesync_free_video(syncInstanceHandle.Resource, ref nativeVideoFrame);
                // Release the sync instance
                syncInstanceHandle.Dispose();
            });
        }

        IResourceProvider<AudioFrame> IAudioSource.GrabAudioFrame(int sampleCount, Optional<int> sampleRate, Optional<int> channelCount, Optional<bool> interleaved)
        {
            var syncInstanceHandle = _syncInstanceProvider?.GetHandle();
            if (syncInstanceHandle is null)
                return ResourceProvider.Return(AudioFrame.Empty);

            var nativeAudioFrame = new NDIlib.audio_frame_v2_t();
            NDIlib.framesync_capture_audio(syncInstanceHandle.Resource, ref nativeAudioFrame, sampleRate.Value, channelCount.Value, sampleCount);

            if (nativeAudioFrame.p_data == default)
            {
                NDIlib.framesync_free_audio(syncInstanceHandle.Resource, ref nativeAudioFrame);
                syncInstanceHandle.Dispose();
                return ResourceProvider.Return(AudioFrame.Empty);
            }

            IMemoryOwner<float> bufferOwner;
            AudioFrame audioFrame;

            if (interleaved.Value)
            {
                bufferOwner = Utils.GetInterleavedBuffer(ref nativeAudioFrame);

                audioFrame = new AudioFrame(
                    bufferOwner.Memory.AsMemory2D(nativeAudioFrame.no_samples, nativeAudioFrame.no_channels),
                    nativeAudioFrame.sample_rate,
                    IsInterleaved: true,
                    Metadata: Utils.Utf8ToString(nativeAudioFrame.p_metadata));
            }
            else
            {
                bufferOwner = Utils.GetPlanarBuffer(ref nativeAudioFrame);

                audioFrame = new AudioFrame(
                    bufferOwner.Memory.AsMemory2D(nativeAudioFrame.no_channels, nativeAudioFrame.no_samples),
                    nativeAudioFrame.sample_rate,
                    IsInterleaved: false,
                    Metadata: Utils.Utf8ToString(nativeAudioFrame.p_metadata));
            }

            return ResourceProvider.Return(audioFrame, (syncInstanceHandle, bufferOwner, nativeAudioFrame), static x =>
            {
                var (syncInstanceHandle, bufferOwner, nativeAudioFrame) = x;
                // Free allocated memory
                bufferOwner.Dispose();
                // Free audio frame
                NDIlib.framesync_free_audio(syncInstanceHandle.Resource, ref nativeAudioFrame);
                // Release the sync instance
                syncInstanceHandle.Dispose();
            });
        }

        protected override void Destroy(bool disposing)
        {
            _syncInstanceHandle?.Dispose();
            _syncInstanceHandle = null;
        }
    }
}
