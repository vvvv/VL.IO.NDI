using SkiaSharp;
using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reflection.Metadata;
using VL.Core;
using VL.Lib.Basics.Imaging;
using VL.Lib.Basics.Resources;

namespace VL.IO.NDI
{
    public sealed class ImageStreamToSKImage : IDisposable
    {
        private readonly SerialDisposable imageStreamSubscription = new SerialDisposable();
        private readonly SerialDisposable latestSubscription = new SerialDisposable();
        private readonly SerialDisposable currentSubscription = new SerialDisposable();

        private IObservable<IResourceProvider<IImage>> imageStream;
        private IResourceProvider<SKImage> current, latest;

        public unsafe IObservable<IResourceProvider<IImage>> ImageStream 
        {
            get => imageStream;
            set
            {
                if (value != imageStream)
                {
                    imageStream = value;

                    imageStreamSubscription.Disposable = value?
                        .Do(provider =>
                        {
                            var skImageProvider = SkiaUtils.ToSKImage(provider).ShareInParallel();
                            var handle = skImageProvider.GetHandle(); // Upload the texture

                            // Exchange provider
                            lock (this)
                            {
                                latest = skImageProvider;
                                latestSubscription.Disposable = handle;
                            }
                        })
                        .Finally(() =>
                        {
                            lock (this)
                            {
                                latest = null;
                                latestSubscription.Disposable = null;
                            }
                        })
                        .Subscribe();
                }
            }
        }

        public IResourceProvider<SKImage> Provider
        {
            get
            {
                lock (this)
                {
                    var latest = this.latest;
                    if (latest != current)
                    {
                        current = latest;
                        currentSubscription.Disposable = current?.GetHandle();
                    }
                    return current ?? ResourceProvider.Default<SKImage>.GetInstance(default);
                }
            }
        }

        public void Dispose()
        {
            imageStreamSubscription.Dispose();
            latestSubscription.Dispose();
            currentSubscription.Dispose();
        }
    }
}
