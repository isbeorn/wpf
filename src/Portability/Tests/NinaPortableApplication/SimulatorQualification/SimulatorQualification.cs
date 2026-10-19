using System;
using System.IO;
using NINA.Core.Utility;
using NINA.Profile;

namespace NINA.Portability {
    internal static class SimulatorQualification {
        internal static bool Enabled {
            get {
#if NINA_SIMULATOR_QUALIFICATION
                return true;
#else
                return false;
#endif
            }
        }

        internal static string StateDirectory => Path.Combine(AppContext.BaseDirectory, "QualificationState");

        internal static void InitializeState() {
            if (!Enabled) return;
            Directory.CreateDirectory(StateDirectory);
            CoreUtil.APPLICATIONTEMPPATH = StateDirectory;
            ProfileService.PROFILEFOLDER = Path.Combine(StateDirectory, "Profiles");
        }
    }
}

#if NINA_SIMULATOR_QUALIFICATION
namespace NINA {
    public partial class App {
        public App() {
            Portability.SimulatorQualification.InitializeState();
        }
    }
}
#endif
