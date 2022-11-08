using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace VL.IO.NDI
{
    sealed unsafe class UnmanagedMemoryManager<T> : MemoryManager<T>
        where T : unmanaged
    {
        public readonly T* pointer;
        public readonly int length;
        public readonly bool isOwner;

        public UnmanagedMemoryManager(T* pointer, int length, bool isOwner)
        {
            this.pointer = pointer;
            this.length = length;
            this.isOwner = isOwner;
        }

        public override Span<T> GetSpan() => new Span<T>(pointer, length);

        public override unsafe MemoryHandle Pin(int elementIndex = 0)
        {
            if (elementIndex < 0 || elementIndex >= length)
                throw new ArgumentOutOfRangeException(nameof(elementIndex));
            return new MemoryHandle(pointer + elementIndex);
        }

        public override void Unpin() { }

        protected override void Dispose(bool disposing)
        {
            if (isOwner)
                Marshal.FreeHGlobal(new IntPtr(pointer));
        }
    }
}
