using System;

namespace VL.IO.NDI
{
    public abstract class NativeObject : IDisposable
    {
        private bool isDisposed;

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                isDisposed = true;
                Destroy(disposing);
            }
        }

        ~NativeObject()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected abstract void Destroy(bool disposing);
    }
}
