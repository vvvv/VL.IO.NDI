// NOTE : The following MIT license applies to this file ONLY and not to the SDK as a whole. Please review the SDK documentation 
// for the description of the full license terms, which are also provided in the file "NDI License Agreement.pdf" within the SDK or 
// online at http://ndi.link/ndisdk_license. Your use of any part of this SDK is acknowledgment that you agree to the SDK license 
// terms. The full NDI SDK may be downloaded at http://ndi.video/
//
//*************************************************************************************************************************************
// 
// Copyright (C) 2023-2026 Vizrt NDI AB. All rights reserved.
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation 
// files(the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, 
// merge, publish, distribute, sublicense, and / or sell copies of the Software, and to permit persons to whom the Software is 
// furnished to do so, subject to the following conditions :
//
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF 
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE 
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION 
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Runtime.InteropServices;
using System.Security;

namespace NewTek
{
    [SuppressUnmanagedCodeSecurity]
    public static partial class NDIlib
    {
        /// <summary>
        /// Create a new Sender Listener instance. This will return NULL if it fails.
        /// </summary>
        /// <param name="createSettings">The creation settings.</param>
        /// <returns></returns>
        public static IntPtr SenderListenerCreate(ref NDIlib_send_listener_create_t createSettings)
        {
            return IntPtr.Size == 8 ? UnsafeNativeMethods.send_listener_create_64(ref createSettings)
                                    : UnsafeNativeMethods.send_listener_create_32(ref createSettings);
        }

        /// <summary>
        /// This will destroy an existing Sender Listener instance.
        /// </summary>
        /// <param name="instance">The instance.</param>
        public static void SenderListenerDestroy(IntPtr instance)
        {
            if (IntPtr.Size == 8)
            {
                UnsafeNativeMethods.send_listener_destroy_64(instance);
            }
            else
            {
                UnsafeNativeMethods.send_listener_destroy_32(instance);
            }
        }

        /// <summary>
        /// This will wait up till timeout_in_ms seconds to check for new senders to be added or removed
        /// </summary>
        /// <param name="instance">The instance.</param>
        /// <param name="timeoutInMs">The timeout in ms.</param>
        /// <returns></returns>
        public static bool SenderListenerWaitForSources(IntPtr instance, uint timeoutInMs)
        {
            return IntPtr.Size == 8 ? UnsafeNativeMethods.send_listener_wait_for_senders_64(instance, timeoutInMs)
                                    : UnsafeNativeMethods.send_listener_wait_for_senders_32(instance, timeoutInMs);

        }

        /// <summary>
        /// Senders the listener is connected.
        /// </summary>
        /// <param name="instance">The instance.</param>
        /// <returns></returns>
        public static bool SenderListenerIsConnected(IntPtr instance)
        {
            return IntPtr.Size == 8 ? UnsafeNativeMethods.send_listener_is_connected_64(instance) :
                                      UnsafeNativeMethods.send_listener_is_connected_32(instance);
        }

        /// <summary>
        /// Senders the listener get server URL.
        /// </summary>
        /// <param name="instance">The instance.</param>
        /// <returns></returns>
        public static IntPtr SenderListenerGetServerUrl(IntPtr instance)
        {
            return IntPtr.Size == 8 ? UnsafeNativeMethods.send_listener_get_server_url_64(instance) :
                                      UnsafeNativeMethods.send_listener_get_server_url_32(instance);
        }

        /// <summary>
        /// Senders the listener get current sources.
        /// </summary>
        /// <param name="instance">The instance.</param>
        /// <param name="noSources">The no sources.</param>
        /// <returns></returns>
        public static IntPtr SenderListenerGetCurrentSources(IntPtr instance, ref uint noSources)
        {
            return IntPtr.Size == 8 ? UnsafeNativeMethods.send_listener_get_senders_64(instance, ref noSources)
                                    : UnsafeNativeMethods.send_listener_get_senders_32(instance, ref noSources);
        }

        [SuppressUnmanagedCodeSecurity]
        internal static partial class UnsafeNativeMethods
		{
            // Senders Monitoring

            // Create listener sender
            // Create an instance of the sender listener. This will return NULL if it fails to create the listener.
            [DllImport("Processing.NDI.Lib.x64.dll", EntryPoint = "NDIlib_send_listener_create", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr send_listener_create_64(ref NDIlib_send_listener_create_t p_create_settings);
            [DllImport("Processing.NDI.Lib.x86.dll", EntryPoint = "NDIlib_send_listener_create", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr send_listener_create_32(ref NDIlib_send_listener_create_t p_create_settings);

            // Destroy an instance of the sender listener.
            [DllImport("Processing.NDI.Lib.x64.dll", EntryPoint = "NDIlib_send_listener_destroy", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern void send_listener_destroy_64(IntPtr p_instance);
            [DllImport("Processing.NDI.Lib.x64.dll", EntryPoint = "NDIlib_send_listener_destroy", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern void send_listener_destroy_32(IntPtr p_instance);

            // Sender Listener Is Connected?
            // Returns true if the sender listener is actively connected to the configured NDI Discovery Server.
            [DllImport("Processing.NDI.Lib.x64.dll", EntryPoint = "NDIlib_send_listener_is_connected", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern bool send_listener_is_connected_64(IntPtr p_instance);
            [DllImport("Processing.NDI.Lib.x86.dll", EntryPoint = "NDIlib_send_listener_is_connected", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern bool send_listener_is_connected_32(IntPtr p_instance);

            // Get Server URL Sender Listener

            // Retrieve the URL address of the NDI Discovery Server that the sender listener is connected to. This can
            // return NULL if the instance pointer is invalid.
            [DllImport("Processing.NDI.Lib.x64.dll", EntryPoint = "NDIlib_send_listener_get_server_url", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr send_listener_get_server_url_64(IntPtr p_instance);
            [DllImport("Processing.NDI.Lib.x86.dll", EntryPoint = "NDIlib_send_listener_get_server_url", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr send_listener_get_server_url_32(IntPtr p_instance);

            // Get Senders

            // Retrieves the current list of advertised senders. The memory for the returned structure is only valid
            // until the next call or when destroy is called. For a given NDIlib_send_listener_instance_t, do not call
            // NDIlib_send_listener_get_senders asynchronously.
            [DllImport("Processing.NDI.Lib.x64.dll", EntryPoint = "NDIlib_send_listener_get_senders", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr send_listener_get_senders_64(IntPtr p_instance, ref uint p_num_senders);
            [DllImport("Processing.NDI.Lib.x86.dll", EntryPoint = "NDIlib_send_listener_get_senders", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr send_listener_get_senders_32(IntPtr p_instance, ref uint p_num_senders);

            // Wait for senders

            // This will allow you to wait until the number of online sender has changed.
            [DllImport("Processing.NDI.Lib.x64.dll", EntryPoint = "NDIlib_send_listener_wait_for_senders", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern bool send_listener_wait_for_senders_64(IntPtr p_instance, uint timeout_in_ms);
            [DllImport("Processing.NDI.Lib.x86.dll", EntryPoint = "NDIlib_send_listener_wait_for_senders", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern bool send_listener_wait_for_senders_32(IntPtr p_instance, uint timeout_in_ms);

        } // UnsafeNativeMethods
    } // class NDIlibAdvanced
} 

