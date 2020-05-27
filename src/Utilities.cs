using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Reactive.Linq;

using VL.Lib.Basics.Imaging;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using MapMode = SharpDX.Direct3D11.MapMode;

using VL.Core;
using VL.Lib.Basics.Imaging;
using VL.Lib.Basics.Resources;
using SharpDX;

using NewTek;

// Utility functions outside of the NDILib SDK itself,
// but useful for working with NDI from managed languages.

namespace VL.IO.NDI
{
    [SuppressUnmanagedCodeSecurity]
    public static partial class UTF
    {
        // This REQUIRES you to use Marshal.FreeHGlobal() on the returned pointer!
        public static IntPtr StringToUtf8(String managedString)
        {
            int len = Encoding.UTF8.GetByteCount(managedString);

            byte[] buffer = new byte[len + 1];

            Encoding.UTF8.GetBytes(managedString, 0, managedString.Length, buffer, 0);

            IntPtr nativeUtf8 = Marshal.AllocHGlobal(buffer.Length);

            Marshal.Copy(buffer, 0, nativeUtf8, buffer.Length);

            return nativeUtf8;
        }

        // this version will also return the length of the utf8 string
        // This REQUIRES you to use Marshal.FreeHGlobal() on the returned pointer!
        public static IntPtr StringToUtf8(String managedString, out int utf8Length )
        {
            utf8Length = Encoding.UTF8.GetByteCount(managedString);

            byte[] buffer = new byte[utf8Length + 1];

            Encoding.UTF8.GetBytes(managedString, 0, managedString.Length, buffer, 0);

            IntPtr nativeUtf8 = Marshal.AllocHGlobal(buffer.Length);

            Marshal.Copy(buffer, 0, nativeUtf8, buffer.Length);

            return nativeUtf8;
        }

        // Length is optional, but recommended
        // This is all potentially dangerous
        public static string Utf8ToString(IntPtr nativeUtf8, uint? length = null)
        {
            if (nativeUtf8 == IntPtr.Zero)
                return String.Empty;

            uint len = 0;

            if (length.HasValue)
            {
                len = length.Value;
            }
            else
            {
                // try to find the terminator
                while (Marshal.ReadByte(nativeUtf8, (int)len) != 0)
                {
                    ++len;
                }
            }

            byte[] buffer = new byte[len];

            Marshal.Copy(nativeUtf8, buffer, 0, buffer.Length);

            return Encoding.UTF8.GetString(buffer);
        }

    } // class NDILib


    public static class ImageDownload
    {
        /// <summary>
        /// copy contents into a given IntPtr
        /// </summary>
        /// <param name="texture"></param>
        /// <param name="device"></param>
        /// <param name="destination"></param>
        /// <param name="bufferSize"></param>
        public static void CopyToPointer(this Texture2D texture, Device device, IntPtr destination, int bufferSize)
        {
            var stagingTexture = default(Texture2D);
            var width = 0;
            var height = 0;
            var format = Format.Unknown;
            var pixelFormat = format.ToPixelFormat();


            var description = texture.Description;
            if (stagingTexture is null || description.Width != width || description.Height != height || description.Format != format)
            {
                width = description.Width;
                height = description.Height;
                format = description.Format;
                pixelFormat = format.ToPixelFormat();

                description.ArraySize = 1;
                description.Usage = ResourceUsage.Staging;
                description.BindFlags = BindFlags.None;
                description.CpuAccessFlags = CpuAccessFlags.Read;

                stagingTexture?.Dispose();
                stagingTexture = new Texture2D(device, description);
            }

            var deviceContext = device.ImmediateContext;
            deviceContext.CopyResource(texture, stagingTexture);
            //deviceContext.Flush();

            DataBox data = deviceContext.MapSubresource(stagingTexture, 0, MapMode.Read, MapFlags.None);

            //data.RowPitch; // The stride returned from Mapped Subresource. see https://gamedev.stackexchange.com/questions/22460/determine-the-stride-of-a-directx-texture2d-line 

            unsafe
            {
                System.Buffer.MemoryCopy(data.DataPointer.ToPointer(), destination.ToPointer(), bufferSize, bufferSize);
            }

            deviceContext.UnmapSubresource(stagingTexture, 0);
        }

        /// <summary>
        /// Get the Pointer to the Pixeldata from a Texture2D
        /// </summary>
        /// <param name="texture"></param>
        /// <param name="device"></param>
        /// <returns></returns>
        public static IntPtr ToPointer(this Texture2D texture, Device device)
        {
            var stagingTexture = default(Texture2D);
            var width = 0;
            var height = 0;
            var format = Format.Unknown;
            var pixelFormat = format.ToPixelFormat();


            var description = texture.Description;
            if (stagingTexture is null || description.Width != width || description.Height != height || description.Format != format)
            {
                width = description.Width;
                height = description.Height;
                format = description.Format;
                pixelFormat = format.ToPixelFormat();

                description.ArraySize = 1;
                description.Usage = ResourceUsage.Staging;
                description.BindFlags = BindFlags.None;
                description.CpuAccessFlags = CpuAccessFlags.Read;

                stagingTexture?.Dispose();
                stagingTexture = new Texture2D(device, description);
            }

            var deviceContext = device.ImmediateContext;
            deviceContext.CopyResource(texture, stagingTexture);
            //deviceContext.Flush();

            var data = deviceContext.MapSubresource(stagingTexture, 0, MapMode.Read, MapFlags.None);

            return data.DataPointer;
        }

        public static IObservable<IImage> Download(this IObservable<Texture2D> textures, NodeContext nodeContext)
        {
            var stagingTexture = default(Texture2D);
            var width = 0;
            var height = 0;
            var format = Format.Unknown;
            var pixelFormat = format.ToPixelFormat();

            var provider = nodeContext.Factory.CreateService<IResourceProvider<Device>>(nodeContext);
            return Using(provider, device =>
            {
                return Observable.Create<IImage>(observer =>
                {
                    return textures.Subscribe(texture =>
                    {
                        var description = texture.Description;
                        if (stagingTexture is null || description.Width != width || description.Height != height || description.Format != format)
                        {
                            width = description.Width;
                            height = description.Height;
                            format = description.Format;
                            pixelFormat = format.ToPixelFormat();

                            description.ArraySize = 1;
                            description.Usage = ResourceUsage.Staging;
                            description.BindFlags = BindFlags.None;
                            description.CpuAccessFlags = CpuAccessFlags.Read;

                            stagingTexture?.Dispose();
                            stagingTexture = new Texture2D(device, description);
                        }

                        var deviceContext = device.ImmediateContext;
                        deviceContext.CopyResource(texture, stagingTexture);
                        //deviceContext.Flush();

                        var data = deviceContext.MapSubresource(stagingTexture, 0, MapMode.Read, MapFlags.None);
                        try
                        {
                            var info = new ImageInfo(width, height, pixelFormat);
                            using var image = new IntPtrImage(data.DataPointer, data.RowPitch * height, info);
                            observer.OnNext(image);
                        }
                        finally
                        {
                            deviceContext.UnmapSubresource(stagingTexture, 0);
                        }
                    });
                });
            });
        }

        // Taken from VL.CoreLib
        static IObservable<T> Using<TResource, T>(this IResourceProvider<TResource> provider, Func<TResource, IObservable<T>> observableFactory)
            where TResource : class, IDisposable
        {
            if (provider is null)
                throw new ArgumentNullException(nameof(provider));

            if (observableFactory is null)
                throw new ArgumentNullException(nameof(observableFactory));

            return Observable.Using(
                resourceFactory: () => provider.GetHandle(),
                observableFactory: h => observableFactory(h.Resource));
        }

        static PixelFormat ToPixelFormat(this Format format)
        {
            var isSRgb = false;
            switch (format)
            {
                case Format.Unknown:
                    return PixelFormat.Unknown;
                case Format.R8_UNorm:
                    return PixelFormat.R8;
                case Format.R16_UNorm:
                    return PixelFormat.R16;
                case Format.R32_Float:
                    return PixelFormat.R32F;
                case Format.R8G8B8A8_UNorm:
                    return PixelFormat.R8G8B8A8;
                case Format.R8G8B8A8_UNorm_SRgb:
                    isSRgb = true;
                    return PixelFormat.R8G8B8A8;
                case Format.B8G8R8X8_UNorm:
                    return PixelFormat.B8G8R8X8;
                case Format.B8G8R8X8_UNorm_SRgb:
                    isSRgb = true;
                    return PixelFormat.B8G8R8X8;
                case Format.B8G8R8A8_UNorm:
                    return PixelFormat.B8G8R8A8;
                case Format.B8G8R8A8_UNorm_SRgb:
                    isSRgb = true;
                    return PixelFormat.B8G8R8A8;
                case Format.R32G32_Float:
                    return PixelFormat.R32G32F;
                case Format.R32G32B32A32_Float:
                    return PixelFormat.R32G32B32A32F;
                default:
                    throw new Exception("Unsupported texture format");
            }
        }

        public static NDIlib.FourCC_type_e ToFourCC(this Format format)
        {
            switch (format)
            {
                case Format.R8G8B8A8_UNorm:
                    return NDIlib.FourCC_type_e.FourCC_type_RGBA;
                case Format.R8G8B8A8_UNorm_SRgb:
                    return NDIlib.FourCC_type_e.FourCC_type_RGBA;
                case Format.B8G8R8X8_UNorm:
                    return NDIlib.FourCC_type_e.FourCC_type_BGRX;
                case Format.B8G8R8X8_UNorm_SRgb:
                    return NDIlib.FourCC_type_e.FourCC_type_BGRX;
                case Format.B8G8R8A8_UNorm:
                    return NDIlib.FourCC_type_e.FourCC_type_BGRA;
                case Format.B8G8R8A8_UNorm_SRgb:
                    return NDIlib.FourCC_type_e.FourCC_type_BGRA;
                case Format.R32G32_Float:
                case Format.R32G32B32A32_Float:
                case Format.R16_UNorm:
                case Format.R32_Float:
                case Format.Unknown:
                case Format.R8_UNorm:
                default:
                    throw new Exception("Unsupported texture format");
            }
        }
    }
} // namespace NewTek.NDI
