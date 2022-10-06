using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Text;
using VL.Lib.Basics.Resources;

namespace VL.IO.NDI
{
    internal static class PoolUtils
    {
        public static TPool Subscribe<TPool>(this IResourceProvider<TPool> poolProvider, SerialDisposable subscription)
        {
            var handle = poolProvider.GetHandle();
            subscription.Disposable = handle;
            return handle.Resource;
        }
    }
}
