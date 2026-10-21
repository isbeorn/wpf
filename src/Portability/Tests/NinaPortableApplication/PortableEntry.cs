using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Wpf.Portable.NativeHost;

namespace NINA {
    internal static class PortableEntry {
        [STAThread]
        private static void Main() {
            if (OperatingSystem.IsLinux()) {
                InitializeNativeHost();
                ConfigureDiagnostics();
            }

            App.Main();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InitializeNativeHost() {
            NativeWindowHost.InitializeForCurrentThread();
        }

        private static void ConfigureDiagnostics() {
            if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("WPF_NINA_PORTABLE_DIAGNOSTICS") != "1") {
                return;
            }

            PresentationTraceSources.Refresh();
            Trace.AutoFlush = true;
            const string listenerName = "NinaPortableDiagnostics";
            TraceSource binding = PresentationTraceSources.DataBindingSource;
            TraceListener listener = binding.Listeners[listenerName] ?? new ConsoleTraceListener(true) { Name = listenerName };
            foreach (TraceSource source in new[] { binding, PresentationTraceSources.ResourceDictionarySource, PresentationTraceSources.MarkupSource }) {
                source.Switch.Level = SourceLevels.Warning;
                if (source.Listeners[listenerName] == null) {
                    source.Listeners.Add(listener);
                }
            }
        }
    }
}
