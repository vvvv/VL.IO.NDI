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
using System.Text;

namespace NewTek
{
	[SuppressUnmanagedCodeSecurity]
	public static partial class NDIlib
	{
        [StructLayoutAttribute(LayoutKind.Sequential)]
        public struct recv_listener_create_t
        {
            // The URL address of the NDI Discovery Server to connect to. If NULL, then the default NDI discovery
            // server will be used. If there is no discovery server available, then the receiver listener will not
            // be able to be instantiated and the create function will return NULL. The format of this field is
            // expected to be the hostname or IP address, optionally followed by a colon and a port number. If the
            // port number is not specified, then port 5959 will be used. For example,
            //     127.0.0.1:5959
            //       or
            //     127.0.0.1
            //       or
            //     hostname:5959
            // If this field is a comma-separated list, then only the first address will be used.
            public IntPtr p_url_address;

        }

        // This is a descriptor of a NDI Receiver available on the network.
        [StructLayoutAttribute(LayoutKind.Sequential)]
        public struct receiver_t
        {
            // The unique identifier for the receiver on the network.
            public IntPtr p_uuid;

            // The human-readable name of the receiver.
            public IntPtr p_name;

            // The unique identifier for the input group that the receiver belongs to.
            public IntPtr p_input_uuid;

            // The human-readable name of the input group that the receiver belongs to.
            public IntPtr p_input_name;

            // The known IP address of the receiver.
            public IntPtr p_address;

            // An array of streams that the receiver is set to receive. The last entry in this list will be
            // receiver_type_none. (Type - receiver_type_e)
            public IntPtr p_streams;

            // How many elements are in the p_streams array, excluding the receiver_type_none entry.
            public uint num_streams;

            // An array of commands that the receiver can process. The last entry in this list will be
            // NDIlib_receiver_command_none.(Type - NDIlib_receiver_command_e)
            public IntPtr p_commands;

            // How many elements are in the p_commands array, excluding the NDIlib_receiver_command_none entry.
            public uint num_commands;

            // Are we currently subscribed for receive events?
            public bool events_subscribed;
        }

        [StructLayoutAttribute(LayoutKind.Sequential)]
        public struct recv_listener_event
        {
            // The unique identifier for the receiver that triggered the event.
            public IntPtr p_uuid;

            // The name of the event that was triggered.
            public IntPtr p_name;

            // The value of the event that was triggered.
            public IntPtr p_value;
        }

        // Create a new Receiver Listener instance. This will return NULL if it fails.
        public static IntPtr recv_listener_create(ref recv_listener_create_t p_create_settings)
        {
            return UnsafeNativeMethods.recv_listener_create(ref p_create_settings);
        }

        // This will destroy an existing Receiver Listener instance.
        public static void recv_listener_destroy(IntPtr p_instance)
        {
            UnsafeNativeMethods.recv_listener_destroy(p_instance);
        }

        // This will wait up till timeout_in_ms seconds to check for new receivers to be added or removed
        public static bool recv_listener_is_connected(IntPtr p_instance)
        {
            return UnsafeNativeMethods.recv_listener_is_connected(p_instance);
        }

        // Get the updated list of receivers
        public static IntPtr recv_listener_get_receivers(IntPtr p_instance, ref uint p_no_receivers)
        {
            return UnsafeNativeMethods.recv_listener_get_receivers(p_instance, ref p_no_receivers);
        }

        // Wait for Receiver Listeners
        public static bool recv_listener_wait_for_receivers(IntPtr p_instance, uint timeout_in_ms)
        {
            return UnsafeNativeMethods.recv_listener_wait_for_receivers(p_instance, timeout_in_ms);
        }

        // Retrieve the URL address of the NDI Discovery Server that the receiver listener is connected to
        public static IntPtr recv_listener_get_server_url(IntPtr p_instance)
        {
            return UnsafeNativeMethods.recv_listener_get_server_url(p_instance);
        }

        [SuppressUnmanagedCodeSecurity]
		internal static partial class UnsafeNativeMethods
		{
            // Create Receiver Listener
            [DllImport("Processing.NDI.Lib.x64.dll", EntryPoint = "NDIlib_recv_listener_create", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr recv_listener_create(ref recv_listener_create_t p_create_settings);

            // Destroy Receiver Listener
            [DllImport("Processing.NDI.Lib.x64.dll", EntryPoint = "NDIlib_recv_listener_destroy", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern void recv_listener_destroy(IntPtr p_instance);

            // Check if Receiver Listener is Connected
            [DllImport("Processing.NDI.Lib.x64.dll", EntryPoint = "NDIlib_recv_listener_is_connected", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern bool recv_listener_is_connected(IntPtr p_instance);

            // Get Receivers 
            [DllImport("Processing.NDI.Lib.x64.dll", EntryPoint = "NDIlib_recv_listener_get_receivers", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr recv_listener_get_receivers(IntPtr p_instance, ref uint p_num_receivers);

            // Wait for Receivers
            [DllImport("Processing.NDI.Lib.x64.dll", EntryPoint = "NDIlib_recv_listener_wait_for_receivers", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern bool recv_listener_wait_for_receivers(IntPtr p_instance, uint timeout_in_ms);

            // Get Server URL
            [DllImport("Processing.NDI.Lib.x64.dll", EntryPoint = "NDIlib_recv_listener_get_server_url", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr recv_listener_get_server_url(IntPtr p_instance);
        } // UnsafeNativeMethods

	} // class NDIlib

} // namespace NewTek

