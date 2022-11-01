using System;
using System.Linq;
using System.Threading;

using NewTek;
using VL.Lib.Collections;
using System.Threading.Tasks;
using System.Reactive.Subjects;
using VL.Lib.Basics.Resources;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using VL.Core;

namespace VL.IO.NDI
{
    public static class Finder
    {
        public static IObservable<Spread<Source>> GetSources(bool showLocalSources = false, string[] groups = null, string[] extraIps = null)
        {
            return Observable.Create<Spread<Source>>(async (observer, ct) =>
            {
                while (!ct.IsCancellationRequested)
                {
                    // Create and destroy new finder on each iteration - prevents vvvv.exe from lingering around
                    using (var handle = CreateNativeInstanceProvider(showLocalSources, groups, extraIps).GetHandle())
                    {
                        // Wait up to 500ms for sources to change
                        if (NDIlib.find_wait_for_sources(handle.Resource, 500))
                        {
                            uint numSources = 0;
                            var sourcesPtr = NDIlib.find_get_current_sources(handle.Resource, ref numSources);
                            var sources = GetSources(sourcesPtr, (int)numSources);
                            observer.OnNext(sources);
                        }
                    }

                    await Task.Delay(500);
                }
            }).SubscribeOn(Scheduler.Default);

            static unsafe Spread<Source> GetSources(IntPtr sourcesPtr, int numSources)
            {
                var sources = new ReadOnlySpan<NDIlib.source_t>(sourcesPtr.ToPointer(), numSources);
                var builder = Spread.CreateBuilder<Source>(sources.Length);
                foreach (var s in sources)
                    builder.Add(new Source(s));
                return builder.ToSpread();
            }

        }

        private static unsafe IResourceProvider<IntPtr> CreateNativeInstanceProvider(bool showLocalSources = false, string[] groups = null, string[] extraIps = null)
        {
            // make a flat list of groups if needed
            var flatGroups = groups != null ? string.Join(",", groups) : null;

            // This is also optional.
            // The list of additional IP addresses that exist that we should query for 
            // sources on. For instance, if you want to find the sources on a remote machine
            // that is not on your local sub-net then you can put a comma seperated list of 
            // those IP addresses here and those sources will be available locally even though
            // they are not mDNS discoverable. An example might be "12.0.0.8,13.0.12.8".
            // When none is specified (IntPtr.Zero) the registry is used.
            // Create a UTF-8 buffer from our string
            // Must use Marshal.FreeHGlobal() after use!
            // IntPtr extraIpsPtr = NDI.Common.StringToUtf8("12.0.0.8,13.0.12.8")
            // make a flat list of ip addresses as comma separated strings
            var flatIps = extraIps != null ? string.Join(",", extraIps) : null;

            fixed (byte* groupsNamePtr = UTF.StringToUtf8(flatGroups))
            fixed (byte* extraIpsPtr = UTF.StringToUtf8(flatIps))
            {
                // how we want our find to operate
                NDIlib.find_create_t findDesc = new NDIlib.find_create_t()
                {
                    p_groups = new IntPtr(groupsNamePtr),
                    show_local_sources = showLocalSources,
                    p_extra_ips = new IntPtr(extraIpsPtr)

                };

                // create our find instance
                var instance = NDIlib.find_create_v2(ref findDesc);
                if (instance == IntPtr.Zero)
                    throw new InvalidOperationException("Failed to create NDI-find");
                return ResourceProvider.Return(instance, i => NDIlib.find_destroy(i));
            }
        }
    }
}
