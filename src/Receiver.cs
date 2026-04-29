using CommunityToolkit.HighPerformance;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using VL.Core;
using VL.Core.Import;
using VL.Lib.Basics.Audio;
using VL.Lib.Basics.Resources;
using VL.Lib.Basics.Video;
using VL.Model;
using static NewTek.NDIlib;

namespace VL.IO.NDI;

[ProcessNode(Name = "NDIReceiver")]
public class Receiver : IDisposable
{
    private readonly ILogger logger;

    private Source? source;
    private string? receiverName;
    private recv_color_format_e colorFormat;
    private recv_bandwidth_e bandwidth;
    private bool allowVideoFields;

    private NdiSession? session;

    private static bool needsEnabledWorkaround;
    private bool wasActive;

    static Receiver()
    {
        var assembly = typeof(IVLObject).Assembly;
        needsEnabledWorkaround = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .None(a => a.Key == "BugFix.VideoSourceToImage.FrameReleaseIssue");
    }

    public Receiver(NodeContext nodeContext) 
    {
        logger = nodeContext.GetLogger();
    }

    public void Update(Source source, 
        [Pin(Visibility = PinVisibility.Optional)] [DefaultValue("vvvv")] string receiverName,
        [Pin(Visibility = PinVisibility.Optional)] bool onProgram,
        [Pin(Visibility = PinVisibility.Optional)] bool onPreview,
        [DefaultValue(recv_bandwidth_e.recv_bandwidth_highest)] recv_bandwidth_e bandwidth,
        [Pin(Visibility = PinVisibility.Optional)] [DefaultValue(recv_color_format_e.recv_color_format_BGRX_BGRA)] recv_color_format_e colorFormat,
        [Pin(Visibility = PinVisibility.Optional)] bool allowVideoFields,
        [DefaultValue(true)] bool enabled,
        out IVideoSource? videoSource,
        out IAudioSource? audioSource,
        out IObservable<string> metadataFrames)
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

            session?.Dispose();
            session = null;

            if (!source.IsNone)
            {
                session = new NdiSession(source.ToString(), receiverName, colorFormat, bandwidth, allowVideoFields);
                wasActive = true;
            }
        }

        session?.UpdateTally(onProgram, onPreview);

        videoSource = session ?? (needsEnabledWorkaround && wasActive ? (IVideoSource)EmptyVideoSource.Instance : null);
        audioSource = session;
        metadataFrames = session?.MetadataFrames ?? Observable.Empty<string>();
    }

    public void Dispose()
    {
        session?.Dispose();
        session = null;
    }

    sealed class NdiSession : IVideoSource, IAudioSource, IDisposable, IRefCounted
    {
        private readonly IntPtr recv;
        private readonly IntPtr sync;
        private readonly IObservable<string> metadataFrames;
        private int refCount = 1;
        private bool onProgram;
        private bool onPreview;

        public unsafe NdiSession(string sourceName, string receiverName, recv_color_format_e colorFormat, recv_bandwidth_e bandwidth, bool allowVideoFields)
        {
            fixed (byte* sourceNamePtr = Utils.StringToUtf8(sourceName))
            fixed (byte* receiverNamePtr = Utils.StringToUtf8(receiverName))
            {
                // a source_t to describe the source to connect to.
                source_t source_t = new source_t()
                {
                    p_ndi_name = new IntPtr(sourceNamePtr)
                };

                // make a description of the receiver we want
                recv_create_v3_t recvDescription = new recv_create_v3_t()
                {
                    // the source we selected
                    source_to_connect_to = source_t,

                    // we want BGRA frames for this example
                    color_format = colorFormat,

                    // we want full quality - for small previews or limited bandwidth, choose lowest
                    bandwidth = bandwidth,

                    // let NDIlib deinterlace for us if needed
                    allow_video_fields = allowVideoFields,

                    // The name of the NDI receiver to create. This is a NULL terminated UTF8 string and should be
                    // the name of receive channel that you have. This is in many ways symettric with the name of
                    // senders, so this might be "Channel 1" on your system.
                    p_ndi_recv_name = new IntPtr(receiverNamePtr)
                };

                recv = recv_create_v3(ref recvDescription);
                sync = framesync_create(recv);
            }

            metadataFrames = Observable.Create<string>(PushMetadataFrames).Publish().RefCount();
        }

        public void Dispose() => Release();

        public bool Enter() => Interlocked.Increment(ref refCount) > 1;

        public void Release()
        {
            if (Interlocked.Decrement(ref refCount) == 0)
            {
                recv_destroy(recv);
                framesync_destroy(sync);
            }
        }

        public void UpdateTally(bool onProgram, bool onPreview)
        {
            using var l = Lock.Enter(this);
            if (!l.Aquired) 
                return;

            if (onProgram != this.onProgram || onPreview != this.onPreview)
            {
                this.onProgram = onProgram;
                this.onPreview = onPreview;

                // set up a state descriptor
                tally_t tallyState = new tally_t()
                {
                    on_program = onProgram,
                    on_preview = onPreview
                };

                // set it on the receiver instance
                recv_set_tally(recv, ref tallyState);
            }
        }

        IResourceProvider<VideoFrame>? IVideoSource.GrabVideoFrame()
        {
            using var l = Lock.Enter(this);
            if (!l.Aquired) 
                return null;

            var nativeVideoFrame = new video_frame_v2_t();
            framesync_capture_video(sync, ref nativeVideoFrame, frame_format_type_e.frame_format_type_interleaved);

            if (nativeVideoFrame.p_data == default)
            {
                framesync_free_video(sync, ref nativeVideoFrame);
                return null;
            }

            var (memoryOwner, videoFrame) = Utils.CreateVideoFrame(ref nativeVideoFrame);
            return ResourceProvider.Return(videoFrame, (sync, memoryOwner, nativeVideoFrame, l.GetLock()), static x =>
            {
                var (sync, memoryOwner, nativeVideoFrame, frameLock) = x;
                // Free allocated memory
                memoryOwner.Dispose();
                // Free video frame
                framesync_free_video(sync, ref nativeVideoFrame);
                frameLock.Dispose();
            });
        }

        IResourceProvider<AudioFrame>? IAudioSource.GrabAudioFrame(int sampleCount, Optional<int> sampleRate, Optional<int> channelCount, Optional<bool> interleaved)
        {
            using var l = Lock.Enter(this);
            if (!l.Aquired) 
                return null;

            var nativeAudioFrame = new audio_frame_v2_t();
            framesync_capture_audio(sync, ref nativeAudioFrame, sampleRate.Value, channelCount.Value, sampleCount);

            if (nativeAudioFrame.p_data == default)
            {
                framesync_free_audio(sync, ref nativeAudioFrame);
                return null;
            }

            var (bufferOwner, audioFrame) = Utils.CreateAudioFrame(ref nativeAudioFrame, interleaved.Value);
            return ResourceProvider.Return(audioFrame, (sync, bufferOwner, nativeAudioFrame, l.GetLock()), static x =>
            {
                var (sync, bufferOwner, nativeAudioFrame, frameLock) = x;
                // Free allocated memory
                bufferOwner.Dispose();
                // Free audio frame
                framesync_free_audio(sync, ref nativeAudioFrame);
                // Release the handle to the session
                frameLock.Dispose();
            });
        }

        public IObservable<string> MetadataFrames => metadataFrames;

        private async Task PushMetadataFrames(IObserver<string> observer, CancellationToken token)
        {
            using var l = Lock.Enter(this);
            if (!l.Aquired) 
                return;

            await Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    var metadata = ReceiveMetadata(timeout: 10);
                    if (metadata is not null)
                        observer.OnNext(metadata);

                    await Task.Yield();
                }
            }, token);
        }

        private string? ReceiveMetadata(uint timeout)
        {
            metadata_frame_t nativeMetadataFrame = new metadata_frame_t();

            switch (recv_capture_v2(recv, ref Utils.NULL<video_frame_v2_t>(), ref Utils.NULL<audio_frame_v2_t>(), ref nativeMetadataFrame, timeout))
            {
                // Video data
                case frame_type_e.frame_type_metadata:

                    // UTF-8 strings must be converted for use - length includes the terminating zero
                    var metadata = Utils.Utf8ToString(nativeMetadataFrame.p_data, nativeMetadataFrame.length - 1);

                    // free frames that were received
                    recv_free_metadata(recv, ref nativeMetadataFrame);

                    return metadata;
            }

            return null;
        }
    }
}

// Needed as workaround for vvvv < 7.2 to ensure video frame gets released when the receiver is disabled
sealed class EmptyVideoSource : IVideoSource
{
    public static readonly EmptyVideoSource Instance = new EmptyVideoSource();

    private static readonly VideoFrame blackVideoFrame = new VideoFrame<BgraPixel>((new BgraPixel[1]).AsMemory().AsMemory2D(1, 1));

    public IResourceProvider<VideoFrame>? GrabVideoFrame() => ResourceProvider.Return(blackVideoFrame);
}
