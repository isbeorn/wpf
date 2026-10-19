extern alias WpfFork;
using SkiaSharp;
using Wpf.Portable.NativeHost.Linux;
using WpfFork::MS.Internal.Portability;
using WpfDispatcher = WpfFork::System.Windows.Threading.Dispatcher;
using WpfPriority = WpfFork::System.Windows.Threading.DispatcherPriority;

namespace Wpf.Portable.NativeHost;

internal sealed class NativeSplashFactory(X11Connection connection, X11Screens screens) : IPortableSplashFactory
{
    public IPortableSplashWindow Create(Stream encodedImage, bool topMost, WpfDispatcher owner)
    {
        owner.VerifyAccess();
        connection.VerifyOwner();
        using var input = new SKManagedStream(encodedImage, disposeManagedStream: false);
        using SKCodec codec = SKCodec.Create(input) ?? throw new IOException("The native splash image cannot be decoded.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        try
        {
            if (bitmap.GetPixels() == IntPtr.Zero) throw new InvalidOperationException("The native splash bitmap could not be allocated.");
            SKCodecResult result = codec.GetPixels(info, bitmap.GetPixels());
            if (result != SKCodecResult.Success) throw new IOException("The native splash image cannot be decoded: " + result);
        }
        catch { bitmap.Dispose(); throw; }
        return new NativeSplashScreen(connection, screens, bitmap, owner, topMost);
    }
}

internal sealed class NativeSplashScreen : IPortableSplashWindow
{
    private readonly SKBitmap _bitmap;
    private readonly X11Connection _connection;
    private readonly X11Screens _screens;
    private readonly WpfDispatcher _owner;
    private X11Window? _window;
    private SKBitmap? _frame;
    private bool _disposed;
    private double _opacity = 1;

    internal NativeSplashScreen(X11Connection connection, X11Screens screens, SKBitmap bitmap, WpfDispatcher owner, bool topMost)
    {
        _connection = connection; _screens = screens; _bitmap = bitmap; _owner = owner;
        try
        {
            var position = ReadPosition();
            _frame = new SKBitmap(bitmap.Info);
            if (_frame.GetPixels() == IntPtr.Zero) throw new InvalidOperationException("The native splash frame could not be allocated.");
            _window = new X11Window(connection, new X11WindowOptions
            {
                Width = bitmap.Width, Height = bitmap.Height, X = position.X, Y = position.Y,
                Transparent = true, Splash = true
            });
            _window.SetDecorations(X11WindowDecorations.None);
            _window.SetMinMaxSize(bitmap.Width, bitmap.Height, bitmap.Width, bitmap.Height);
            _window.SetShowInTaskbar(false);
            _window.SetTopmost(topMost);
            _window.Closed += DisposeOnOwner;
            _screens.Changed += OnScreensChanged;
            _owner.ShutdownStarted += OnShutdown;
            _window.Show(activate: false);
            Paint();
        }
        catch { DisposeOnOwner(); throw; }
    }

    private (int X, int Y) ReadPosition()
    {
        var screen = _screens.ReadPrimary()
            ?? throw new PlatformNotSupportedException("The native splash requires an unambiguous observed primary screen.");
        return (checked(screen.Bounds.X + (screen.Bounds.Width - _bitmap.Width) / 2),
            checked(screen.Bounds.Y + (screen.Bounds.Height - _bitmap.Height) / 2));
    }

    private void OnScreensChanged()
    {
        if (_disposed) return;
        var position = ReadPosition();
        _window!.Move(position.X, position.Y);
    }

    public void SetOpacity(double opacity)
    {
        _owner.VerifyAccess();
        if (_disposed) return;
        _opacity = opacity;
        Paint();
    }

    private unsafe void Paint()
    {
        _owner.VerifyAccess();
        if (_disposed || _frame is null || _window is null) return;
        using (var canvas = new SKCanvas(_frame))
        using (var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(_opacity, 0, 1) * 255)) })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(_bitmap, 0, 0, paint);
            canvas.Flush();
        }
        _window.Present(new ReadOnlySpan<byte>((void*)_frame.GetPixels(), checked(_frame.RowBytes * _frame.Height)),
            _frame.Width, _frame.Height, _frame.RowBytes);
    }

    private void OnShutdown(object? sender, EventArgs args) => DisposeOnOwner();

    public void Dispose()
    {
        if (_owner.CheckAccess()) DisposeOnOwner();
        else if (!_owner.HasShutdownStarted)
            _owner.BeginInvoke(WpfPriority.Send, new Action(DisposeOnOwner));
    }

    private void DisposeOnOwner()
    {
        _owner.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        _owner.ShutdownStarted -= OnShutdown;
        _screens.Changed -= OnScreensChanged;
        try
        {
            if (_window is not null)
            {
                _window.Closed -= DisposeOnOwner;
                try { _window.Dispose(); }
                finally { _connection.Synchronize(); }
                _window = null;
            }
        }
        finally
        {
            _frame?.Dispose();
            _frame = null;
            _bitmap.Dispose();
        }
    }
}
