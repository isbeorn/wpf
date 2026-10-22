using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NINA.Core.Locale;
using NINA.Equipment.Equipment;
using NINA.Equipment.Equipment.MyGuider;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Image.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Model.Equipment.MyCamera.Simulator;
using NINA.WPF.Base.ViewModel.Equipment;

namespace NINA.Portability {
    internal sealed class SimulationDeviceChooser<T> : DeviceChooserVM<T> where T : IDevice {
        private readonly Func<IList<IDevice>> inventory;
        private readonly Func<(string Id, string Name)> selection;

        internal SimulationDeviceChooser(IProfileService profileService, IEquipmentProviders<T> providers,
                Func<IList<IDevice>> inventory, Func<(string Id, string Name)> selection) : base(profileService, providers) {
            this.inventory = inventory;
            this.selection = selection;
        }

        public override async Task GetEquipment() {
            await lockObj.WaitAsync();
            try {
                var selected = selection();
                DetermineSelectedDevice(inventory(), selected.Id, selected.Name);
            } finally {
                lockObj.Release();
            }
        }
    }

    internal static class SimulationInventory {
        internal static SimulationDeviceChooser<ICamera> Camera(IServiceProvider services) {
            var profile = services.GetRequiredService<IProfileService>();
            var camera = new SimulatorCamera(profile, services.GetRequiredService<ITelescopeMediator>(),
                services.GetRequiredService<IExposureDataFactory>(), services.GetRequiredService<IImageDataFactory>());
            return Create<ICamera>(services, () => new List<IDevice> { new DummyDevice(Loc.Instance["LblNoCamera"]), camera },
                () => (profile.ActiveProfile.CameraSettings.Id, profile.ActiveProfile.CameraSettings.LastDeviceName));
        }

        internal static SimulationDeviceChooser<IGuider> Guider(IServiceProvider services) {
            var profile = services.GetRequiredService<IProfileService>();
            return Create<IGuider>(services, () => new List<IDevice> { new DummyGuider(profile) },
                () => (profile.ActiveProfile.GuiderSettings.GuiderName, profile.ActiveProfile.GuiderSettings.LastDeviceName));
        }

        internal static SimulationDeviceChooser<IFilterWheel> FilterWheel(IServiceProvider services) {
            var profile = services.GetRequiredService<IProfileService>();
            return Create<IFilterWheel>(services, () => new List<IDevice> { new DummyDevice(Loc.Instance["LblNoFilterwheel"]) },
                () => (profile.ActiveProfile.FilterWheelSettings.Id, profile.ActiveProfile.FilterWheelSettings.LastDeviceName));
        }

        internal static SimulationDeviceChooser<IFocuser> Focuser(IServiceProvider services) {
            var profile = services.GetRequiredService<IProfileService>();
            return Create<IFocuser>(services, () => new List<IDevice> { new DummyDevice(Loc.Instance["LblNoFocuser"]) },
                () => (profile.ActiveProfile.FocuserSettings.Id, profile.ActiveProfile.FocuserSettings.LastDeviceName));
        }

        internal static SimulationDeviceChooser<IRotator> Rotator(IServiceProvider services) {
            var profile = services.GetRequiredService<IProfileService>();
            return Create<IRotator>(services, () => new List<IDevice> { new DummyDevice(Loc.Instance["LblNoRotator"]) },
                () => (profile.ActiveProfile.RotatorSettings.Id, profile.ActiveProfile.RotatorSettings.LastDeviceName));
        }

        internal static SimulationDeviceChooser<ITelescope> Telescope(IServiceProvider services) {
            var profile = services.GetRequiredService<IProfileService>();
            return Create<ITelescope>(services, () => new List<IDevice> { new DummyDevice(Loc.Instance["LblNoMount"]) },
                () => (profile.ActiveProfile.TelescopeSettings.Id, profile.ActiveProfile.TelescopeSettings.LastDeviceName));
        }

        internal static SimulationDeviceChooser<ISwitchHub> Switch(IServiceProvider services) {
            var profile = services.GetRequiredService<IProfileService>();
            return Create<ISwitchHub>(services, () => new List<IDevice> { new DummyDevice(Loc.Instance["LblSwitchNoSource"]) },
                () => (profile.ActiveProfile.SwitchSettings.Id, profile.ActiveProfile.SwitchSettings.LastDeviceName));
        }

        internal static SimulationDeviceChooser<IFlatDevice> FlatDevice(IServiceProvider services) {
            var profile = services.GetRequiredService<IProfileService>();
            return Create<IFlatDevice>(services, () => new List<IDevice> { new DummyDevice(Loc.Instance["LblFlatDeviceNoSource"]) },
                () => (profile.ActiveProfile.FlatDeviceSettings.Id, profile.ActiveProfile.FlatDeviceSettings.LastDeviceName));
        }

        internal static SimulationDeviceChooser<IWeatherData> WeatherData(IServiceProvider services) {
            var profile = services.GetRequiredService<IProfileService>();
            return Create<IWeatherData>(services, () => new List<IDevice> { new DummyDevice(Loc.Instance["LblWeatherNoSource"]) },
                () => (profile.ActiveProfile.WeatherDataSettings.Id, profile.ActiveProfile.WeatherDataSettings.LastDeviceName));
        }

        internal static SimulationDeviceChooser<IDome> Dome(IServiceProvider services) {
            var profile = services.GetRequiredService<IProfileService>();
            return Create<IDome>(services, () => new List<IDevice> { new DummyDevice(Loc.Instance["LblDomeNoSource"]) },
                () => (profile.ActiveProfile.DomeSettings.Id, profile.ActiveProfile.DomeSettings.LastDeviceName));
        }

        internal static SimulationDeviceChooser<ISafetyMonitor> SafetyMonitor(IServiceProvider services) {
            var profile = services.GetRequiredService<IProfileService>();
            return Create<ISafetyMonitor>(services, () => new List<IDevice> { new DummyDevice(Loc.Instance["LblSafetyMonitorNoSource"]) },
                () => (profile.ActiveProfile.SafetyMonitorSettings.Id, profile.ActiveProfile.SafetyMonitorSettings.LastDeviceName));
        }

        private static SimulationDeviceChooser<T> Create<T>(IServiceProvider services, Func<IList<IDevice>> inventory,
                Func<(string Id, string Name)> selection) where T : IDevice {
            return new SimulationDeviceChooser<T>(services.GetRequiredService<IProfileService>(),
                services.GetRequiredService<IEquipmentProviders<T>>(), inventory, selection);
        }
    }
}
