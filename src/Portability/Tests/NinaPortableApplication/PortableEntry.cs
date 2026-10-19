using System;
using System.Runtime.CompilerServices;
using Wpf.Portable.NativeHost;

namespace NINA {
    internal static class PortableEntry {
        [STAThread]
        private static void Main() {
            if (OperatingSystem.IsLinux()) {
                InitializeNativeHost();
            }

            App.Main();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InitializeNativeHost() {
            NativeWindowHost.InitializeForCurrentThread();
        }
    }
}
