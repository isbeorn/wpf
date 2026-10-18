using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Portability.MarkupIntegration.Fixture;
using Portability.SystemSettings.GenericFixture;
using System.Xaml;
using System.Xaml.Permissions;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--primary-screen-unavailable" }, StringComparer.Ordinal))
            return PrimaryScreenChecks.RunUnavailable();
        if (args.SequenceEqual(new[] { "--primary-screen-constructor" }, StringComparer.Ordinal))
            return PrimaryScreenChecks.RunConstructor();
        if (args.SequenceEqual(new[] { "--classic-dictionary-diagnostic" }, StringComparer.Ordinal))
            return RunClassicDictionaryDiagnostic();
        if (args.SequenceEqual(new[] { "--default-theme-layout" }, StringComparer.Ordinal))
            return RunDefaultThemeLayoutDiagnostic();
        var checks = new List<object>();
        string[] windowsProcessModules = [];
        var failed = false;
        void Check(string name, Func<string> body)
        {
            try { checks.Add(new { name, status = "pass", detail = body() }); }
            catch (Exception exception) { failed = true; checks.Add(new { name, status = "fail", detail = exception.ToString() }); }
        }
        bool always = args.Contains("--always-show-cues", StringComparer.Ordinal);
        bool pointer = args.Contains("--pointer-fixture", StringComparer.Ordinal);
        bool highContrast = args.Contains("--high-contrast-fixture", StringComparer.Ordinal);
        bool screenFixture = args.Contains("--primary-screen-fixture", StringComparer.Ordinal);
        bool metricFixture = args.Contains("--metric-fixture", StringComparer.Ordinal);
        bool expectClassicTheme = !args.Contains("--expect-generic-theme", StringComparer.Ordinal);
        if (args.Any(argument => argument is not "--always-show-cues" and not "--pointer-fixture" and not "--high-contrast-fixture" and not "--metric-fixture" and not "--primary-screen-fixture" and not "--expect-classic-theme" and not "--expect-generic-theme")
            || args.Contains("--expect-classic-theme", StringComparer.Ordinal) && !expectClassicTheme)
            Check("arguments", () => throw new ArgumentException("Use startup fixture flags and at most one of --expect-classic-theme or --expect-generic-theme. Theme expectation flags only assert policy."));
        else
        {
            Check("startup-profile-registration", () =>
            {
                object profile = CreateProfile(always, pointer ? CreatePointer() : null, scrollBarMetrics: metricFixture ? SystemMetricChecks.CreateFixture() : null, primaryScreenMetrics: screenFixture ? PrimaryScreenChecks.CreateFixture() : null);
                if (highContrast)
                    profile = profile.GetType().GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single().Invoke(
                        ["test-high-contrast-v1", GetProperty(profile, "Colors"), always, true, pointer ? CreatePointer() : null, 2.0, -1, true, 2.5, 7.5, GetProperty(profile, "ScrollBarMetrics"), screenFixture ? GetProperty(profile, "PrimaryScreenMetrics") : null]);
                if (always || pointer || highContrast || metricFixture || screenFixture) Register(profile);
                Require((string)GetProperty(CurrentProfile(), "ProfileName") == (highContrast ? "test-high-contrast-v1" : "portable-light-v1"), "Profile identity mismatch.");
                return "Named startup palette registered before consumption; fixture preferences do not claim desktop theme parity.";
            });
            Check("all-system-colors-and-frozen-brushes", () => VerifyPalette(highContrast));
            Check("brush-cache-invalidation", VerifyInvalidation);
            Check("keyboard-cues-and-existing-show-hide-logic", () => VerifyKeyboardCues(always));
            Check("complete-immutable-palette", VerifyImmutablePalette);
            Check("pointer-settings-or-named-diagnostic", () => VerifyPointer(pointer));
            Check("caret-settings-and-boundaries", () => VerifyCaret(highContrast));
            Check("drag-distance-profile", () => VerifyDragDistance(highContrast));
            Check("scrollbar-metrics-and-resource-keys", () => SystemMetricChecks.Verify(metricFixture));
            foreach (string role in SystemFontChecks.Roles)
                Check("system-font-" + role, () => SystemFontChecks.VerifyRole(role));
            Check("actual-classic-tooltip-font-and-layout", SystemFontChecks.VerifyToolTip);
            Check("fixed-classic-no-shadow-resource", SystemFontChecks.VerifyDropShadow);
            Check("primary-screen-metrics-and-closed-combobox", () => PrimaryScreenChecks.Verify(screenFixture));
            Check("fixed-classic-combobox-animation-resources", PrimaryScreenChecks.VerifyAnimation);
            Check("fixed-classic-repeatbutton-policy", PrimaryScreenChecks.VerifyRepeatPolicy);
            Check("late-profile-replacement-rejected", VerifyLateRegistration);
            Check("dynamic-system-color-resource", VerifyDynamicSystemResource);
            Check("actual-generic-style-dictionary", () => VerifyGenericStyle(expectClassicTheme));
            Check("xaml-access-factories-and-nonpublic-construction", VerifyXamlAccess);
            Check("unsupported-native-resource-notifications", VerifyUnsupportedNotifications);
            Check("dynamic-local-resource-replacement-removal", VerifyLocalResources);
            Check("dynamic-application-resource-replacement-removal", VerifyApplicationResources);
            Check("actual-control-construction", ConstructControl);
            Check("actual-window-construction", ConstructWindow);
            Check("actual-compiled-baml-construction", ConstructBaml);
            Check("portable-settings-avoid-windows-contract-loads", () =>
            {
                var loaded = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => assembly.GetName().Name is "DirectWriteForwarder" or "System.Private.Windows.Core").ToArray();
                Require(loaded.Length == 0, "Portable settings loaded a Windows native contract: " + string.Join(", ", loaded.Select(assembly => assembly.FullName)));
                using var process = Process.GetCurrentProcess();
                var modules = process.Modules.Cast<ProcessModule>().Select(module => module.ModuleName).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                if (OperatingSystem.IsWindows()) windowsProcessModules = modules;
                bool uxThemeLoaded = modules.Contains("uxtheme.dll", StringComparer.OrdinalIgnoreCase);
                Require(OperatingSystem.IsWindows() || !uxThemeLoaded, "Portable Linux resource lookup loaded Windows UxTheme.");
                return $"No DirectWrite or private WinForms contract loaded. UxTheme module observed={uxThemeLoaded}; strict native module absence is asserted on Linux, while Windows may load it outside the portable resource policy.";
            });
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            probe = "system-settings-integration",
            os = RuntimeInformation.OSDescription,
            scope = "Actual portable settings and construction; native theme parity, live profile updates and native Alt delivery are not claimed.",
            alwaysShowCues = always,
            explicitPointerFixture = pointer,
            explicitHighContrastFixture = highContrast,
            explicitMetricFixture = metricFixture,
            explicitPrimaryScreenFixture = screenFixture,
            expectedTheme = expectClassicTheme ? "Classic" : "generic",
            windowsProcessModules,
            checks
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failed ? 1 : 0;
    }

    private static string VerifyPalette(bool highContrast)
    {
        var properties = typeof(SystemColors).GetProperties(BindingFlags.Public | BindingFlags.Static).Where(property => property.PropertyType == typeof(Color)).ToArray();
        Require(properties.Length == 37, "Expected 30 base colors and seven accent colors.");
        foreach (var property in properties)
        {
            var color = (Color)property.GetValue(null)!;
            Require(color.A == 255, property.Name + " is not opaque.");
            var brushName = property.Name.StartsWith("AccentColor", StringComparison.Ordinal) ? property.Name + "Brush" : property.Name[..^5] + "Brush";
            var brushProperty = typeof(SystemColors).GetProperty(brushName)!;
            var brush = (SolidColorBrush)brushProperty.GetValue(null)!;
            bool accent = property.Name.StartsWith("AccentColor", StringComparison.Ordinal);
            Require(brush.Color == (highContrast && accent ? SystemColors.HighlightTextColor : color) && brush.IsFrozen && ReferenceEquals(brush, brushProperty.GetValue(null)), brushName + " color/cache/freeze mismatch.");
        }
        Require(SystemColors.ControlTextColor == Color.FromRgb(31, 35, 40) && SystemColors.WindowColor == Colors.White, "Initial light palette changed.");
        Require(SystemParameters.HighContrast == highContrast, "High contrast preference mismatch.");
        Require(ReferenceEquals(SystemColors.InactiveSelectionHighlightBrush, highContrast ? SystemColors.HighlightBrush : SystemColors.ControlBrush), "Inactive selection role mismatch.");
        Require(ReferenceEquals(SystemColors.InactiveSelectionHighlightTextBrush, highContrast ? SystemColors.HighlightTextBrush : SystemColors.ControlTextBrush), "Inactive selection text role mismatch.");
        return $"All 37 explicit colors and frozen cached brushes preserve existing HighContrast={highContrast} aliases.";
    }

    private static string VerifyInvalidation()
    {
        var before = SystemColors.ControlTextBrush;
        var accentBefore = SystemColors.AccentColorBrush;
        var key = SystemColors.ControlTextBrushKey;
        Require((bool)typeof(SystemColors).GetMethod("InvalidateCache", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!, "Populated caches did not report invalidation.");
        var after = SystemColors.ControlTextBrush;
        Require(!ReferenceEquals(before, after) && after.IsFrozen && after.Color == before.Color, "Base brush did not regenerate.");
        Require(!ReferenceEquals(accentBefore, SystemColors.AccentColorBrush) && SystemColors.AccentColorBrush.IsFrozen, "Accent brush did not regenerate.");
        Require(ReferenceEquals(key, SystemColors.ControlTextBrushKey), "Resource key identity changed.");
        return "Existing invalidation recreated base/accent frozen brushes and preserved resource keys.";
    }

    private static string VerifyKeyboardCues(bool always)
    {
        Require(SystemParameters.KeyboardCues == always, "Keyboard preference mismatch.");
        var navigation = typeof(SystemColors).Assembly.GetType("System.Windows.Input.KeyboardNavigation", throwOnError: true)!;
        var property = (DependencyProperty)navigation.GetField("ShowKeyboardCuesProperty", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var element = new DependencyObject();
        element.SetValue(property, false);
        Require((bool)element.GetValue(property) == always, "Hidden cues did not honor the accessibility preference.");
        element.SetValue(property, true);
        Require((bool)element.GetValue(property), "Show-cues flag did not show cues.");
        element.SetValue(property, false);
        Require((bool)element.GetValue(property) == always, "Show/hide symmetry failed.");
        Require((bool)navigation.GetProperty("AlwaysShowFocusVisual", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)! == always, "Startup focus preference mismatch.");
        return $"KeyboardCues={always}; verified existing KeyboardNavigation show/hide coercion and startup focus preference.";
    }

    private static string VerifyImmutablePalette()
    {
        var source = CreateProfile(false);
        var colors = (IDictionary)GetProperty(source, "Colors");
        var roleType = colors.Keys.Cast<object>().First().GetType();
        var mutable = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(roleType, typeof(uint)))!;
        foreach (DictionaryEntry entry in colors) mutable.Add(entry.Key, entry.Value);
        var constructor = source.GetType().GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        var copied = constructor.Invoke(["copied-profile", mutable, false, false, null, 1.0, 500, false, 4.0, 4.0, null, null]);
        var role = Enum.Parse(roleType, "ControlText");
        var original = mutable[role];
        mutable[role] = 0xFF000000u;
        Require(Equals(source.GetType().GetMethod("GetColor", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(copied, [role]), original), "Snapshot retained the caller's mutable palette.");
        mutable.Remove(role);
        try { _ = constructor.Invoke(["incomplete-profile", mutable, false, false, null, 1.0, 500, false, 4.0, 4.0, null, null]); }
        catch (TargetInvocationException exception) when (exception.InnerException is ArgumentException inner && inner.Message.Contains("ControlText", StringComparison.Ordinal))
        { return "Snapshot copied the palette and rejected a missing color with a named role diagnostic."; }
        throw new InvalidOperationException("Incomplete palette was accepted.");
    }

    private static string VerifyPointer(bool supplied)
    {
        try
        {
            var pointer = GetProperty(CurrentProfile(), "PointerSettings");
            Require(supplied, "Missing native pointer preferences acquired implicit defaults.");
            Require((int)GetProperty(pointer, "DoubleClickWidth") == 4 && (int)GetProperty(pointer, "DoubleClickHeight") == 6 && (int)GetProperty(pointer, "DoubleClickTimeMilliseconds") == 500, "Explicit pointer preferences changed.");
            return "Explicit test inputs retained: 4 by 6 physical client pixels and 500 ms; these are not inferred OS defaults.";
        }
        catch (TargetInvocationException exception) when (!supplied && exception.InnerException is PlatformNotSupportedException inner && inner.Message.Contains((string)GetProperty(CurrentProfile(), "ProfileName"), StringComparison.Ordinal))
        { return inner.Message; }
    }

    private static string VerifyCaret(bool alternate)
    {
        Require(SystemParameters.CaretWidth == (alternate ? 2.0 : 1.0), "Caret width did not use the startup DIP preference.");
        Require((int)GetProperty(CurrentProfile(), "CaretBlinkTimeMilliseconds") == (alternate ? -1 : 500), "Caret blink startup preference changed.");
        Require((bool)typeof(SystemParameters).GetProperty("MouseVanish", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)! == alternate, "Mouse vanish startup preference changed.");
        foreach (int value in new[] { -1, 0, 1, int.MaxValue / 2 })
            Require((int)GetProperty(CreateProfile(false, blink: value), "CaretBlinkTimeMilliseconds") == value, "Valid caret blink boundary rejected.");
        foreach (int value in new[] { -2, int.MaxValue / 2 + 1 })
            ExpectArgumentRange(() => CreateProfile(false, blink: value), "caretBlinkTimeMilliseconds");
        foreach (double value in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            ExpectArgumentRange(() => CreateProfile(false, width: value), "caretWidthDip");
        Require((double)GetProperty(CreateProfile(false, width: 0.25), "CaretWidthDip") == 0.25, "Fractional positive DIP width was rounded.");
        return "Explicit caret and mouse vanish preferences retained in both startup directions; disabled sentinels, interval overflow and invalid DIP widths checked.";
    }

    private static void ExpectArgumentRange(Func<object> action, string parameter)
    {
        try { _ = action(); }
        catch (TargetInvocationException exception) when (exception.InnerException is ArgumentOutOfRangeException inner && inner.ParamName == parameter) { return; }
        throw new InvalidOperationException("Expected argument range rejection: " + parameter);
    }

    private static string VerifyDragDistance(bool alternate)
    {
        Require(SystemParameters.MinimumHorizontalDragDistance == (alternate ? 2.5 : 4.0), "Horizontal drag preference changed or was rounded.");
        Require(SystemParameters.MinimumVerticalDragDistance == (alternate ? 7.5 : 4.0), "Vertical drag preference changed or was mapped to the horizontal axis.");
        foreach (double value in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            ExpectArgumentRange(() => CreateProfile(false, horizontalDrag: value), "minimumHorizontalDragDistanceDip");
            ExpectArgumentRange(() => CreateProfile(false, verticalDrag: value), "minimumVerticalDragDistanceDip");
        }
        foreach (double value in new[] { double.Epsilon, 0.5, double.MaxValue })
        {
            var profile = CreateProfile(false, horizontalDrag: value, verticalDrag: value);
            Require((double)GetProperty(profile, "MinimumHorizontalDragDistanceDip") == value &&
                (double)GetProperty(profile, "MinimumVerticalDragDistanceDip") == value, "Valid finite drag preference changed.");
        }
        return "Both DIP axes retained independent startup values; nonpositive/nonfinite values rejected and positive finite limits retained. These are explicit product preferences, not detected desktop settings.";
    }

    private static string VerifyLateRegistration()
    {
        try { Register(CreateProfile(true)); }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException inner && inner.Message.Contains((string)GetProperty(CurrentProfile(), "ProfileName"), StringComparison.Ordinal))
        { return inner.Message; }
        throw new InvalidOperationException("Late profile replacement was accepted.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string VerifyGenericStyle(bool expectClassic)
    {
        Require((string)GetProperty(CurrentProfile(), "ThemeName") == (expectClassic ? "Classic" : "generic")
            && (string)GetProperty(CurrentProfile(), "ThemedResourceName") == (expectClassic ? "themes/classic" : "themes/generic"), "Portable resource policy changed.");
        var control = new GenericControl();
        control.BeginInit();
        control.EndInit();
        Require(control.Background is SolidColorBrush brush && brush.Color == Color.FromRgb(0, 128, 128) && control.Width == 71, "Actual generic BAML style did not apply through DefaultStyleKey metadata.");
        return "Real themes/generic.baml supplied the custom control's metadata-selected style under the explicitly expected " + (expectClassic ? "Classic" : "generic") + " theme profile.";
    }

    private static string VerifyUnsupportedNotifications()
    {
        var resources = typeof(SystemColors).Assembly.GetType("System.Windows.SystemResources", throwOnError: true)!;
        ExpectUnsupported(() => resources.GetProperty("Hwnd", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null), "notification window");
        ExpectUnsupported(() => resources.GetMethod("GetDpiAwarenessCompatibleNotificationWindow", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [new HandleRef(null, IntPtr.Zero)]), "notification windows");
        var theme = typeof(SystemColors).Assembly.GetType("MS.Win32.UxThemeWrapper", throwOnError: true)!;
        Require(!(bool)theme.GetProperty("IsActive", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!, "Portable resources claim an active Windows native theme.");
        ExpectUnsupported(() => theme.GetProperty("ThemeColor", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null), "Windows theme color");
        ExpectUnsupported(() => theme.GetMethod("OnThemeChanged", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null), "immutable");
        return "HWND notification, native theme color and live theme change requests report explicit unsupported boundaries.";
    }

    [UnconditionalSuppressMessage("Interoperability", "CA1416", Justification = "The exact pinned official managed XAML access implementation is inspected and exercised here on Linux; other Windows-only operations are not qualified.")]
    private static string VerifyXamlAccess()
    {
        var assembly = typeof(Program).Assembly;
        var cases = new[]
        {
            (level: XamlAccessLevel.AssemblyAccessTo(assembly), type: typeof(InternalAccessModel)),
            (level: XamlAccessLevel.AssemblyAccessTo(assembly.GetName()), type: typeof(InternalAccessModel)),
            (level: XamlAccessLevel.PrivateAccessTo(typeof(PrivateAccessModel)), type: typeof(PrivateAccessModel)),
            (level: XamlAccessLevel.PrivateAccessTo(typeof(PrivateAccessModel).AssemblyQualifiedName!), type: typeof(PrivateAccessModel))
        };
        var schema = new XamlSchemaContext();
        foreach (var item in cases)
        {
            Require(item.level.AssemblyAccessToAssemblyName.FullName == assembly.FullName, "Access factory lost its assembly identity.");
            Require(item.level.PrivateAccessToTypeName == (item.type == typeof(PrivateAccessModel) ? item.type.FullName : null), "Private type scope changed.");
            var type = schema.GetXamlType(item.type);
            using var writer = new XamlObjectWriter(schema, new XamlObjectWriterSettings { AccessLevel = item.level });
            writer.WriteStartObject(type);
            writer.WriteStartMember(type.GetMember("Value"));
            writer.WriteValue("scoped XAML");
            writer.WriteEndMember();
            writer.WriteEndObject();
            Require(item.type.IsInstanceOfType(writer.Result) && (string)item.type.GetProperty("Value")!.GetValue(writer.Result)! == "scoped XAML", "Real object writer failed non-public construction.");
        }
        var extensions = typeof(XamlAccessLevel).Assembly;
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(extensions.Location)));
        Require(extensions.GetName().FullName == "System.Windows.Extensions, Version=10.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51", "Forwarded type identity changed.");
        Require(hash == "54163AEF0B78412550C50FFE0826E53B6F39DD3CC718D1EB9D16B73C27FD75F9", "Qualified unmodified implementation hash changed.");
        Require(AssemblyLoadContext.GetLoadContext(extensions) == AssemblyLoadContext.Default, "Access types require a custom load context.");
        return $"All four factories constructed real internal/private XAML objects in Default ALC; implementation={extensions.Location}; SHA256={hash}. Windows-only media/certificate operations remain unqualified.";
    }

    private sealed class PrivateAccessModel
    {
        public string Value { get; set; } = "";
    }

    private static void ExpectUnsupported(Func<object?> action, string diagnostic)
    {
        try { _ = action(); }
        catch (TargetInvocationException exception) when (exception.InnerException is PlatformNotSupportedException inner && inner.Message.Contains(diagnostic, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Expected named unsupported diagnostic: " + diagnostic);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string VerifyDynamicSystemResource()
    {
        var control = new Control();
        control.SetResourceReference(Control.BackgroundProperty, SystemColors.WindowBrushKey);
        Require(ReferenceEquals(control.Background, SystemColors.WindowBrush), "Dynamic system resource did not resolve the cached frozen window brush.");
        return "Actual DynamicResource resolved SystemColors.WindowBrushKey without a hidden Win32 listener.";
    }

    private static string VerifyLocalResources()
    {
        var control = new Control();
        control.SetResourceReference(Control.BackgroundProperty, SystemColors.WindowBrushKey);
        control.Resources[SystemColors.WindowBrushKey] = Brushes.Red;
        Require(ReferenceEquals(control.Background, Brushes.Red), "Local add did not replace the dynamic system resource.");
        control.Resources[SystemColors.WindowBrushKey] = Brushes.Blue;
        Require(ReferenceEquals(control.Background, Brushes.Blue), "Local replacement did not invalidate the expression.");
        control.Resources.Remove(SystemColors.WindowBrushKey);
        Require(ReferenceEquals(control.Background, SystemColors.WindowBrush), "Local removal did not restore the system resource.");
        return "Local dictionary add, replace and remove preserved DynamicResource fallback to the system brush.";
    }

    private static string VerifyApplicationResources()
    {
        var application = new Application();
        var control = new Window();
        control.SetResourceReference(Control.BackgroundProperty, SystemColors.WindowBrushKey);
        application.Resources[SystemColors.WindowBrushKey] = Brushes.Red;
        Require(ReferenceEquals(control.Background, Brushes.Red), "Application add did not replace the dynamic system resource.");
        application.Resources = new ResourceDictionary { [SystemColors.WindowBrushKey] = Brushes.Blue };
        Require(ReferenceEquals(control.Background, Brushes.Blue), "Application dictionary replacement did not invalidate the expression.");
        application.Resources.Remove(SystemColors.WindowBrushKey);
        Require(ReferenceEquals(control.Background, SystemColors.WindowBrush), "Application removal did not restore the system resource.");
        return "Actual Application dictionary add, replacement and removal preserved DynamicResource fallback.";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ConstructControl()
    {
        var control = new Control();
        Require(control.Foreground is SolidColorBrush brush && brush.Color == SystemColors.ControlTextColor && brush.IsFrozen, "Control did not inherit the system foreground.");
        return "Constructed actual Control with its system foreground and font metadata.";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ConstructWindow()
    {
        var window = new Window { Title = "Portable settings", Width = 320, Height = 240 };
        Require(window.Title == "Portable settings" && window.Width == 320 && window.Height == 240, "Window construction properties changed.");
        return "Constructed actual portable Window without a native peer.";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ConstructBaml()
    {
        var view = new ProbeView();
        Require(view.ConnectedButton is not null && view.ConnectedCard is not null, "Compiled fields did not connect.");
        view.ConnectedButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(view.ActionCount == 1, "BAML event hookup failed.");
        return "Constructed the existing compiled UserControl fixture and invoked its generated button handler.";
    }

    private static Type CoreType(string name) => typeof(SolidColorBrush).Assembly.GetType("MS.Internal.Portability.Settings." + name, throwOnError: true)!;
    private static object GetProperty(object instance, string name) => instance.GetType().GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static object CreatePointer() => Activator.CreateInstance(CoreType("PortablePointerSettings"), BindingFlags.NonPublic | BindingFlags.Instance, null, [4, 6, 500], null)!;
    private static object CreateProfile(bool cues, object? pointer = null, double width = 1.0, int blink = 500, double horizontalDrag = 4.0, double verticalDrag = 4.0, object? scrollBarMetrics = null, object? primaryScreenMetrics = null) => CoreType("PortableSystemSettings").GetMethod("CreateInitialLightProfile", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [cues, pointer, width, blink, false, horizontalDrag, verticalDrag, scrollBarMetrics, primaryScreenMetrics])!;
    private static object CurrentProfile() => CoreType("PortableSystemSettingsProvider").GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    private static void Register(object profile) => CoreType("PortableSystemSettingsProvider").GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [profile]);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

internal sealed class InternalAccessModel
{
    public string Value { get; set; } = "";
}
