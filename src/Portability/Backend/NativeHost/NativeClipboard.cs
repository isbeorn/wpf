extern alias WpfFork;

using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using WpfFork::MS.Internal.Portability.Clipboard;

namespace Wpf.Portable.NativeHost;

internal sealed class NativeClipboard(IClipboard clipboard) : IPortableClipboard
{
    private DataTransfer? _current;
    private object? _original;

    public Task<string?> GetTextAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        return clipboard.TryGetValueAsync(DataFormat.Text);
    }

    public Task SetTextAsync(string text, object original)
    {
        Dispatcher.UIThread.VerifyAccess();
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText(text));
        _current = transfer;
        _original = original;
        return clipboard.SetDataAsync(transfer);
    }

    public Task ClearAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        _current = null;
        _original = null;
        return clipboard.ClearAsync();
    }

    public Task FlushAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        return clipboard.FlushAsync();
    }

    public async Task<bool> IsCurrentAsync(object original)
    {
        Dispatcher.UIThread.VerifyAccess();
        return ReferenceEquals(_original, original) && _current != null && ReferenceEquals(_current, await clipboard.TryGetInProcessDataAsync().ConfigureAwait(true));
    }
}
