using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using NINA.Core.Utility;

namespace NINA.Portability {
    internal static class PortableWindowPlacement {
        private sealed class Placement {
            public double Left { get; set; }
            public double Top { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public WindowState State { get; set; }
        }

        private static string FilePath => Path.Combine(SimulatorQualification.StateDirectory, "window-placement.json");

        internal static void Restore(Window window) {
            if (!File.Exists(FilePath)) return;
            Placement saved;
            try {
                saved = JsonSerializer.Deserialize<Placement>(File.ReadAllText(FilePath));
            } catch (JsonException error) {
                Logger.Error("The owned qualification window placement is invalid.", error);
                return;
            }
            if (saved == null || !double.IsFinite(saved.Left) || !double.IsFinite(saved.Top)
                    || !double.IsFinite(saved.Width) || !double.IsFinite(saved.Height)
                    || saved.Width <= 0 || saved.Height <= 0) return;
            var area = SystemParameters.WorkArea;
            window.Width = Math.Max(window.MinWidth, Math.Min(saved.Width, area.Width));
            window.Height = Math.Max(window.MinHeight, Math.Min(saved.Height, area.Height));
            window.Left = Math.Clamp(saved.Left, area.Left, Math.Max(area.Left, area.Right - window.Width));
            window.Top = Math.Clamp(saved.Top, area.Top, Math.Max(area.Top, area.Bottom - window.Height));
            window.WindowState = saved.State == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        }

        internal static void Save(Window window) {
            var bounds = window.WindowState == WindowState.Normal
                ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight) : window.RestoreBounds;
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;
            Directory.CreateDirectory(SimulatorQualification.StateDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Placement {
                Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height,
                State = window.WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal
            }));
        }
    }
}
