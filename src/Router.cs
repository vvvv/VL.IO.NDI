using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NewTek;

namespace VL.IO.NDI
{
    public class Router : IDisposable, INotifyPropertyChanged
    {
        private string[] _groups;
        private IntPtr _routingInstancePtr;
        private Source _selectedSource;
        private string _routingName = "Routing";

        [Category("NewTek NDI"),
        Description("The NDI source to route elsewhere. An empty new Source() or a Source with no Name will disconnect.")]
        public Source SelectedSource
        {
            get { return _selectedSource; }
            set
            {
                if (value.Name != _selectedSource.Name)
                {
                    _selectedSource = value;
                    
                    UpdateRouting();

                    NotifyPropertyChanged("FromSource");
                }
            }
        }

        [Category("NewTek NDI"),
        Description("The name that will be given to the routed source. If empty it will default to 'Routing'.")]
        public String RoutingName
        {
            get { return String.IsNullOrWhiteSpace(_routingName) ? "Routing" : _routingName; }
            set
            {
                if (value != _routingName)
                {
                    _routingName = String.IsNullOrWhiteSpace(value) ? "Routing" : value;

                    // start over if the routing name changes
                    CreateRouting();

                    NotifyPropertyChanged("RoutingName");
                }
            }
        }

        // Constructor
        public Router(String routingName="Routing", String[] groups = null)
        {
            _groups = groups;
            _routingName = routingName;

            if (!NDIlib.initialize())
            {
                if (!NDIlib.is_supported_CPU())
                    throw new InvalidOperationException("CPU incompatible with NDI.");
                else
                    throw new InvalidOperationException("Unable to initialize NDI.");
            }

            CreateRouting();
        }

        // Route to nowhere (black)
        public void Clear()
        {
            if(_routingInstancePtr != IntPtr.Zero)
                NDIlib.routing_clear(_routingInstancePtr);
        }

        // This will reenable routing if previous cleared.
        // Should not be needed otherwise since FromSource changes will automatically update.
        public unsafe void UpdateRouting()
        {
            // never started before?
            if (_routingInstancePtr == IntPtr.Zero)
            {
                CreateRouting();
                return;
            }

            // Sanity
            if (_selectedSource == null || _selectedSource.IsNone)
            {
                Clear();
                return;
            }

            // a source_t to describe the source to connect to.
            fixed (byte* selectedSourceNamePtr = Utils.StringToUtf8(_selectedSource.Name))
            {
                NDIlib.source_t source_t = new NDIlib.source_t()
                {
                    p_ndi_name = new IntPtr(selectedSourceNamePtr)
                };

                if (!NDIlib.routing_change(_routingInstancePtr, ref source_t))
                    throw new InvalidOperationException("Failed to change routing.");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged(String info)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(info));
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~Router() 
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Clear();
                }

                if (_routingInstancePtr != IntPtr.Zero)
                {
                    NDIlib.routing_destroy(_routingInstancePtr);
                    _routingInstancePtr = IntPtr.Zero;
                }

                NDIlib.destroy();

                _disposed = true;
            }
        }

        private bool _disposed = false;

        private unsafe void CreateRouting()
        {
            if (_routingInstancePtr != IntPtr.Zero)
            {
                NDIlib.routing_destroy(_routingInstancePtr);
                _routingInstancePtr = IntPtr.Zero;
            }

            // Sanity check
            if (_selectedSource == null || String.IsNullOrEmpty(_selectedSource.Name))
                return;

            var flatGroups = _groups != null ? string.Join(",", _groups) : null;

            // .Net interop doesn't handle UTF-8 strings, so do it manually
            fixed (byte* sourceNamePtr = Utils.StringToUtf8(_routingName))
            fixed (byte* groupsNamePtr = Utils.StringToUtf8(flatGroups))
            {
                // Create an NDI routing description
                NDIlib.routing_create_t createDesc = new NDIlib.routing_create_t()
                {
                    p_ndi_name = new IntPtr(sourceNamePtr),
                    p_groups = new IntPtr(groupsNamePtr)
                };

                // create the NDI routing instance
                _routingInstancePtr = NDIlib.routing_create(ref createDesc);

                // did it succeed?
                if (_routingInstancePtr == IntPtr.Zero)
                    throw new InvalidOperationException("Failed to create routing instance.");
            }

            // update in case we have enough info to start routing
            UpdateRouting();
        }        
    }
}
