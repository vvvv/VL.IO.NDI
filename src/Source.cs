﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using NewTek;
using VL.Core.CompilerServices;
using VL.Lib.Collections;

namespace VL.IO.NDI
{
    [Serializable]
    public sealed class Source : DynamicEnumBase<Source, SourceDefinition>, IEquatable<Source>
    {
        private const string NONE = "NONE";

        public static readonly Source None = new Source(NONE);

        [CreateDefault]
        public static Source CreateDefault() => None;

        private string _computerName;
        private string _sourceName;
        private Lazy<Uri> _uri;

        // Construct from NDIlib.source_t
        internal Source(NDIlib.source_t source_t)
            : this(UTF.Utf8ToString(source_t.p_ndi_name))
        {

        }

        // Construct from strings
        public Source(string name) 
            : base(name)
        {
        }

        internal bool IsNone => string.IsNullOrEmpty(Name) || Name == NONE;

        public string Name => Value;

        public string ComputerName => _computerName ??= Name.Substring(0, Name.IndexOf(" ("));

        public string SourceName => _sourceName ??= Regex.Match(Name, @"(?<=\().+?(?=\))").Value;

        public Uri Uri
        {
            get 
            {
                return (_uri ??= new Lazy<Uri>(Compute)).Value;

                Uri Compute()
                {
                    var uriString = string.Format("ndi://{0}/{1}", ComputerName, System.Net.WebUtility.UrlEncode(SourceName));
                    if (Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
                        return uri;
                    return null;
                }
            }
        }

        public override string ToString()
        {
            return Name;
        }

        public override bool Equals(object obj)
        {
            if (obj is Source source)
                return Equals(source);
            return false;
        }

        public bool Equals(Source other)
        {
            if (other is null)
                return false;

            return Name == other.Name;
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }
    }

    public sealed class SourceDefinition : DynamicEnumDefinitionBase<SourceDefinition>
    {
        private readonly IObservable<Spread<Source>> observable;
        private readonly IDisposable subscription;

        private Spread<Source> mostRecentSources = Spread.Create(Source.None);

        public SourceDefinition()
        {
            observable = Finder.GetSources(showLocalSources: true).Publish().RefCount();
            subscription = observable.Subscribe(s => mostRecentSources = s.Add(Source.None));
        }

        ~SourceDefinition()
        {
            subscription.Dispose();
        }

        protected override IReadOnlyDictionary<string, object> GetEntries()
        {
            return mostRecentSources.ToDictionary(s => s.Name, s => default(object));
        }

        protected override IObservable<object> GetEntriesChangedObservable()
        {
            return observable;
        }
    }
}
