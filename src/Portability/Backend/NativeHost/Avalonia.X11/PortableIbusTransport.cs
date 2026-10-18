using System;
using System.Threading.Tasks;
using Avalonia.FreeDesktop;
using Avalonia.FreeDesktop.DBusIme;
using Avalonia.FreeDesktop.DBusIme.IBus;
using Avalonia.Input.TextInput;

namespace Avalonia.X11;

internal enum PortableIbusPreeditKind { Update, Show, Hide }
internal readonly record struct PortableIbusPreedit(PortableIbusPreeditKind Kind, string Text, int Utf16Caret, bool Visible);
internal interface IPortableIbusTransport
{
    event Action<PortableIbusPreedit>? PreeditChanged;
    Task ChangeClientAsync(TextInputMethodClient? client);
    Task ResetAsync();
}
internal static class PortableIbusFactory
{
    internal static void ReplaceRegisteredIbus()
    {
        if (AvaloniaLocator.Current.GetService<IX11InputMethodFactory>() is DBusInputMethodFactory<IBusX11TextInputMethod> &&
            DBusHelper.DefaultConnection is { } connection)
            AvaloniaLocator.CurrentMutable.Bind<IX11InputMethodFactory>().ToConstant(
                new DBusInputMethodFactory<PortableIbusTextInputMethod>(_ => new PortableIbusTextInputMethod(connection)));
    }
}
