using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

internal static partial class Program
{
    private static int RunMenuAnimationSettings()
    {
        var checks = new List<object>();
        bool failed = false;
        CheckMenu("menu-show-delay-getter-resource", () => VerifyResource(
            () => SystemParameters.MenuShowDelay, SystemParameters.MenuShowDelayKey, 400, 25));
        CheckMenu("menu-popup-animation-getter-resource", () => VerifyResource(
            () => SystemParameters.MenuPopupAnimation, SystemParameters.MenuPopupAnimationKey,
            PopupAnimation.None, PopupAnimation.Fade));
        CheckMenu("menu-animation-getter-resource", () => VerifyResource(
            () => SystemParameters.MenuAnimation, SystemParameters.MenuAnimationKey, false, true));
        CheckMenu("menu-fade-getter-resource", () => VerifyResource(
            () => SystemParameters.MenuFade, SystemParameters.MenuFadeKey, false, true));
        CheckMenu("existing-combobox-tooltip-disabled-animation-policy", () =>
            Require(!SystemParameters.ComboBoxAnimation && !SystemParameters.ToolTipAnimation && !SystemParameters.ToolTipFade &&
                SystemParameters.ComboBoxPopupAnimation == PopupAnimation.None && SystemParameters.ToolTipPopupAnimation == PopupAnimation.None,
                "The existing portable ComboBox or tooltip animation policy changed."));
        Console.WriteLine(JsonSerializer.Serialize(new { probe = "portable-menu-animation-settings", checks },
            new JsonSerializerOptions { WriteIndented = true }));
        return failed ? 1 : 0;

        void CheckMenu(string name, Action action)
        {
            try { action(); checks.Add(new { name, status = "pass", detail = "Public getter and original resource policy observed." }); }
            catch (Exception error) { failed = true; checks.Add(new { name, status = "fail", detail = error.ToString() }); }
        }

        static void VerifyResource<T>(Func<T> read, ResourceKey key, T expected, T replacement)
        {
            Require(Equals(read(), expected), "The portable menu getter did not return its declared policy value.");
            var consumer = new Border();
            consumer.SetResourceReference(FrameworkElement.TagProperty, key);
            Require(Equals(consumer.Tag, expected), "The original menu resource did not resolve its declared policy value.");
            consumer.Resources[key] = replacement;
            Require(Equals(consumer.Tag, replacement) && Equals(read(), expected),
                "A local menu resource override failed or changed the independent getter.");
            consumer.Resources.Remove(key);
            Require(Equals(consumer.Tag, expected) && Equals(read(), expected),
                "Removing the local menu resource did not restore the declared policy value.");
        }
    }
}
