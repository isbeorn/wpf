using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NINA.Astrometry {
    internal static class PortableNativeImports {
        [ModuleInitializer]
        internal static void Initialize() {
            if (OperatingSystem.IsLinux()) {
                NativeLibrary.SetDllImportResolver(typeof(SOFA).Assembly, Resolve);
            }
        }

        private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath) {
            string relative;
            switch (name) {
                case "SOFA_2023_10_11.dll": relative = Path.Combine("SOFA", "libSOFA_2023_10_11.so"); break;
                case "NOVAS31lib.dll": relative = Path.Combine("NOVAS", "libNOVAS31.so"); break;
                default: return IntPtr.Zero;
            }
            return NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "External", "x64", relative));
        }
    }
}
