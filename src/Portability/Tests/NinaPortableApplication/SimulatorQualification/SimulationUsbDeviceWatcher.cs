using System;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces;

namespace NINA.Portability {
    internal sealed class SimulationUsbDeviceWatcher : IUsbDeviceWatcher {
        private bool started;
        public event EventHandler<UsbDeviceEventArgs> DeviceInserted { add { } remove { } }
        public event EventHandler<UsbDeviceEventArgs> DeviceRemoved { add { } remove { } }

        public void Start() {
            if (started) return;
            started = true;
            Logger.Info("Simulator-only qualification: USB discovery is excluded; watcher lifecycle started.");
        }

        public void Stop() {
            if (!started) return;
            started = false;
            Logger.Info("Simulator-only qualification: watcher lifecycle stopped.");
        }

        public void Dispose() => Stop();
    }
}
