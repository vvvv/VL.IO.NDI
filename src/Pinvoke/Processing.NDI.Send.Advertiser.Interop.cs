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
        public struct send_advertiser_create_t
        {
            // The URL address of the NDI Discovery Server to connect to. If NULL, then the default NDI discovery
            // server will be used. If there is no discovery server available, then the sender advertiser will not
            // be able to be instantiated and the create function will return NULL. The format of this field is
            // expected to be the hostname or IP address, optionally followed by a colon and a port number. If the
            // port number is not specified, then port 5959 will be used. For example,
            //     127.0.0.1:5959
            //       or
            //     127.0.0.1
            //       or
            //     hostname:5959
            // This field can also specify multiple addresses separated by commas for redundancy support.
            public IntPtr p_url_address;
        }

        // Create a new sender Listener instance. This will return NULL if it fails.
        public static IntPtr send_advertiser_create(ref send_advertiser_create_t p_create_settings)
        {
            return IntPtr.Size == 8 ? UnsafeNativeMethods.send_advertiser_create_64(ref p_create_settings) : 
                                      UnsafeNativeMethods.send_advertiser_create_32(ref p_create_settings);
        }

        // This will destroy an existing sender Listener instance.
        public static bool send_advertiser_add_sender(IntPtr p_instance, IntPtr p_sender, bool allow_monitoring)
        {
            return IntPtr.Size == 8 ? UnsafeNativeMethods.send_advertiser_add_sender_64(p_instance, p_sender, allow_monitoring) : 
                                      UnsafeNativeMethods.send_advertiser_add_sender_32(p_instance, p_sender, allow_monitoring);
        }

        // This will wait up till timeout_in_ms seconds to check for new senders to be added or removed
        public static void send_advertiser_del_sender(IntPtr p_instance_send_advertiser, IntPtr p_instance_send)
        {
            if (IntPtr.Size == 8)
                UnsafeNativeMethods.send_advertiser_del_sender_64(p_instance_send_advertiser, p_instance_send);
            else
                UnsafeNativeMethods.send_advertiser_del_sender_32(p_instance_send_advertiser, p_instance_send);
        }

        // Get the updated list of senders
        public static void send_advertiser_destroy(IntPtr p_instance_send_advertiser)
        {
            if (IntPtr.Size == 8)
                UnsafeNativeMethods.send_advertiser_destroy_64(p_instance_send_advertiser);
            else
                UnsafeNativeMethods.send_advertiser_destroy_32(p_instance_send_advertiser);
        }

        [SuppressUnmanagedCodeSecurity]
		internal static partial class UnsafeNativeMethods
		{
            // Create sender Listener
            [DllImport("Processing.NDI.Lib.x64.dll", EntryPoint = "NDIlib_send_advertiser_create", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr send_advertiser_create_64(ref send_advertiser_create_t p_create_settings);
            [DllImport("Processing.NDI.Lib.x86.dll", EntryPoint = "NDIlib_send_advertiser_create", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr send_advertiser_create_32(ref send_advertiser_create_t p_create_settings);

            // Destroy sender Listener
            [DllImport("Processing.NDI.Lib.x64.dll", EntryPoint = "NDIlib_send_advertiser_destroy", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern void send_advertiser_destroy_64(IntPtr p_instance_send_advertiser);
            [DllImport("Processing.NDI.Lib.x86.dll", EntryPoint = "NDIlib_send_advertiser_destroy", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern void send_advertiser_destroy_32(IntPtr p_instance_send_advertiser);

            // Check if sender Listener is Connected
            [DllImport("Processing.NDI.Lib.x64.dll", EntryPoint = "NDIlib_send_advertiser_add_sender", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern bool send_advertiser_add_sender_64(IntPtr p_instance, IntPtr p_sender, bool allow_monitoring);
            [DllImport("Processing.NDI.Lib.x86.dll", EntryPoint = "NDIlib_send_advertiser_add_sender", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern bool send_advertiser_add_sender_32(IntPtr p_instance, IntPtr p_sender, bool allow_monitoring);

            // Get senders 
            [DllImport("Processing.NDI.Lib.x64.dll", EntryPoint = "NDIlib_send_advertiser_del_sender", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern void send_advertiser_del_sender_64(IntPtr p_instance_send_advertiser, IntPtr p_instance_send);
            [DllImport("Processing.NDI.Lib.x86.dll", EntryPoint = "NDIlib_send_advertiser_del_sender", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern void send_advertiser_del_sender_32(IntPtr p_instance_send_advertiser, IntPtr p_instance_send);

        } // UnsafeNativeMethods

    } // class NDIlib

} // namespace NewTek

