using NewTek;
using System;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;

// Utility functions outside of the NDILib SDK itself,
// but useful for working with NDI from managed languages.

namespace VL.IO.NDI
{
    [SuppressUnmanagedCodeSecurity]
    public static partial class UTF
    {
        // This REQUIRES you to use Marshal.FreeHGlobal() on the returned pointer!
        public static ReadOnlySpan<byte> StringToUtf8(string managedString)
        {
            if (managedString is null)
                return default;

            int len = Encoding.UTF8.GetByteCount(managedString);

            byte[] buffer = new byte[len + 1];

            var count = Encoding.UTF8.GetBytes(managedString, 0, managedString.Length, buffer, 0);
            return buffer.AsSpan(0, count);
        }

        // Length is optional, but recommended
        // This is all potentially dangerous
        public static unsafe string Utf8ToString(IntPtr nativeUtf8, int? length = null)
        {
            if (nativeUtf8 == IntPtr.Zero)
                return string.Empty;

            int len = 0;

            if (length.HasValue)
            {
                len = length.Value;
            }
            else
            {
                // try to find the terminator
                byte* ptr = (byte*)nativeUtf8.ToPointer();
                while (*(ptr++) != 0)
                {
                    ++len;
                }
            }

            return Encoding.UTF8.GetString((byte*)nativeUtf8.ToPointer(), len);
        }

    } // class NDILib

    internal static class NativeUtils
    {
        public static unsafe ref T NULL<T>()
            where T : struct
        {
            return ref Unsafe.AsRef<T>(IntPtr.Zero.ToPointer());
        }
    }
} // namespace NewTek.NDI
