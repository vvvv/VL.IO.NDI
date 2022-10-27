//using NAudio.Wave;
using NewTek;
using System;

using VL.Lib.Basics.Imaging;
using VL.Lib.Basics.Resources;
using System.Reactive.Disposables;

namespace VL.IO.NDI
{
    /// <summary>
    /// If you do not use this control, you can remove this file
    /// and remove the dependency on naudio.
    /// Alternatively you can also remove any naudio related entries
    /// and use it for video only, but don't forget that you will still need
    /// to free any audio frames received.
    /// </summary>
    public unsafe class SynchronizedReceiver : IDisposable
    {
        private readonly SerialDisposable imageSubscription = new SerialDisposable();

        // our unmanaged NDI sync instance
        private IResourceProvider<IntPtr> _syncInstanceProvider;
        private IResourceHandle<IntPtr> _syncInstanceHandle;

        /// <summary>
        /// The name of this receiver channel. Required or else an invalid argument exception will be thrown.
        /// </summary>
        public string ReceiverName { get; set; }

        /// <summary>
        /// The NDI source to connect to. An empty new Source() or a Source with no Name will disconnect.
        /// </summary>
        public Source ConnectedSource { get; set; }

        public SynchronizedReceiver()
        {
        }

        public IResourceProvider<IImage> Update()
        {
            if (_syncInstanceHandle is null)
                return null;

            var syncInstance = _syncInstanceHandle.Resource;
            if (syncInstance == default)
                return null;

            var videoFrame = new NDIlib.video_frame_v2_t();
            NDIlib.framesync_capture_video(syncInstance, ref videoFrame, NDIlib.frame_format_type_e.frame_format_type_interleaved);
            if (videoFrame.p_data != default)
            {
                var image = new VideoFrame(videoFrame);
                var imageProvider = _syncInstanceProvider.Bind(s => ResourceProvider.Return(image, i =>
                {
                    image.Dispose();

                    // Free video frame
                    NDIlib.framesync_free_video(s, ref videoFrame);
                })).ShareInParallel();

                imageSubscription.Disposable = imageProvider.GetHandle();

                return imageProvider;
            }

            return null;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~SynchronizedReceiver()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;

                if (disposing)
                {
                    Disconnect();

                    imageSubscription.Dispose();
                }
            }
        }

        private bool _disposed = false;

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

            _syncInstanceProvider = NativeFactory.CreateReceiver(source.Name, ReceiverName, colorFormat, bandwidth, allowVideoFields)
                .CreateSync()
                .ShareInParallel();
            _syncInstanceHandle = _syncInstanceProvider.GetHandle();
        }

        public void Disconnect()
        {
            // Release image
            imageSubscription.Disposable = null;

            // Destroy the sync
            _syncInstanceHandle?.Dispose();
            _syncInstanceHandle = null;
        }
    }
}
