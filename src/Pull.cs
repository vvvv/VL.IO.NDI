using System;
using System.Collections.Generic;

namespace VL.IO.NDI
{
    public sealed class PullAndHold<T> : IDisposable
    {
        private IEnumerable<T> _source;
        private IEnumerator<T> _enumerator;
        private T _current;

        public T Update(IEnumerable<T> source, out bool onData)
        {
            if (source != _source)
            {
                _source = source;
                _enumerator?.Dispose();
                _enumerator = source.GetEnumerator();
            }

            if (_enumerator is null)
            {
                onData = false;
                return _current;
            }

            if (onData = _enumerator.MoveNext())
            {
                _current = _enumerator.Current;
            }
            else
            {
                _enumerator.Dispose();
                _enumerator = null;
            }
            
            return _current;
        }

        public void Dispose()
        {
            _enumerator.Dispose();
        }
    }
}
