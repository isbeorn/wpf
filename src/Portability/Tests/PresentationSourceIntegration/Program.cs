using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MS.Internal.Portability.Windowing;

var results = new List<object>();
bool failed = false;
Run("first-public-root-dpi-keeps-default-baseline", () =>
{
    var root = new ContainerVisual();
    VisualTreeHelper.SetRootDpi(root, new DpiScale(2, 2));
    Require(VisualTreeHelper.GetDpi(root).Equals(new DpiScale(2, 2)), "First explicit root DPI was lost.");
    Require(VisualTreeHelper.GetDpi(new DrawingVisual()).Equals(new DpiScale(1, 1)), "First host DPI changed an independent default visual.");
    var child = new DrawingVisual();
    root.Children.Add(child);
    Require(VisualTreeHelper.GetDpi(child).Equals(new DpiScale(2, 2)), "A newly attached child did not inherit DPI.");
    VisualTreeHelper.SetRootDpi(root, new DpiScale(1, 1));
    Require(VisualTreeHelper.GetDpi(root).Equals(new DpiScale(1, 1)) && VisualTreeHelper.GetDpi(child).Equals(new DpiScale(1, 1)), "Restoring root DPI did not update descendants.");
});
Run("attach-render-metrics-expose-and-borrowed-disposal", () =>
{
    var host = new ContractHost();
    var layouts = new List<Size>();
    var source = new PortablePresentationSource(size => layouts.Add(size));
    Require(source.CompositionTarget == null && !Sources().Contains(source), "Unattached source was published.");
    Expect<InvalidOperationException>(() => source.RootVisual = Rectangle());
    source.AttachHost(host);
    Require(Sources().Contains(source), "Attached source is missing from CurrentSources.");
    var root = Rectangle();
    source.RootVisual = root;
    Require(ReferenceEquals(PresentationSource.FromVisual(root), source), "FromVisual did not identify its actual source.");
    Require(layouts.Count == 0 && host.Frames == 0, "Root assignment synchronously laid out or presented.");
    Drain();
    Require(layouts.SequenceEqual([new Size(16, 8)]), "Deferred layout did not receive logical client bounds.");
    Require(host.Width == 16 && host.Height == 8 && host.Red(2, 2), "1x surface did not present retained BGRA pixels.");
    Require(source.CompositionTarget!.TransformToDevice == Matrix.Identity, "1x target transform is wrong.");
    host.ChangeMetrics(new Size(16, 8), new DpiScale(2, 2));
    Drain();
    Require(host.Width == 32 && host.Height == 16 && host.Red(6, 6) && host.Transparent(10, 6), "2x surface did not scale logical content.");
    Require(VisualTreeHelper.GetDpi(root).Equals(new DpiScale(2, 2)), "Root DPI was not propagated.");
    Require(source.CompositionTarget.TransformToDevice.M11 == 2 && source.CompositionTarget.TransformFromDevice.M11 == 0.5, "DPI transforms do not invert.");
    host.ChangeMetrics(new Size(16, 8), new DpiScale(2, 1.5));
    Drain();
    Require(host.Width == 32 && host.Height == 12 && host.Red(6, 4) && host.Transparent(6, 7), "Asymmetric host DPI did not scale both axes.");
    Require(VisualTreeHelper.GetDpi(root).Equals(new DpiScale(2, 1.5)), "Asymmetric root DPI was not retained.");
    host.ChangeMetrics(new Size(16, 8), new DpiScale(1, 1));
    Drain();
    Require(host.Width == 16 && host.Height == 8 && VisualTreeHelper.GetDpi(root).Equals(new DpiScale(1, 1)), "Restoring host DPI did not restore logical pixels.");
    host.ChangeMetrics(new Size(8.25, 4.25), new DpiScale(2, 2));
    Drain();
    Require(host.Width == 17 && host.Height == 9, "Physical size did not round upward.");
    int frames = host.Frames;
    host.Expose();
    Drain();
    Require(host.Frames == frames + 1, "Expose did not present a new frame.");
    var compositionTarget = source.CompositionTarget;
    source.Dispose();
    source.Dispose();
    Require(source.IsDisposed && PresentationSource.FromVisual(root) == null && !Sources().Contains(source), "Disposal left a source or root published.");
    Require(host.Subscriptions == 0 && host.CloseCalls == 0, "Source disposal retained events or closed its borrowed host.");
    Expect<ObjectDisposedException>(() => _ = compositionTarget.TransformToDevice);
    Expect<ObjectDisposedException>(() => _ = compositionTarget.RootVisual);
    frames = host.Frames;
    host.Expose();
    host.ChangeMetrics(new Size(32, 16), new DpiScale(1, 1));
    Drain();
    Require(host.Frames == frames, "Disposed source presented again.");
});
Run("source-notifications-root-replacement-and-ownership", () =>
{
    using var first = new PortablePresentationSource(_ => { });
    using var second = new PortablePresentationSource(_ => { });
    first.AttachHost(new ContractHost());
    second.AttachHost(new ContractHost());
    var root = new UIElement();
    var changes = new List<SourceChangedEventArgs>();
    SourceChangedEventHandler changed = (sender, args) => changes.Add(args);
    PresentationSource.AddSourceChangedHandler(root, changed);
    first.RootVisual = root;
    Require(changes.Count == 1 && changes[0].OldSource == null && ReferenceEquals(changes[0].NewSource, first), "Actual SourceChanged attach was not raised.");
    Expect<ArgumentException>(() => second.RootVisual = root);
    Require(ReferenceEquals(first.RootVisual, root) && second.RootVisual == null, "Duplicate ownership changed the first source.");
    first.RootVisual = null!;
    Require(changes.Count == 2 && ReferenceEquals(changes[1].OldSource, first) && changes[1].NewSource == null, "Actual SourceChanged detach was not raised.");
    second.RootVisual = root;
    Require(ReferenceEquals(PresentationSource.FromVisual(root), second), "A detached root could not be reattached.");
    var replacement = Rectangle();
    second.CompositionTarget.RootVisual = replacement;
    Require(PresentationSource.FromVisual(root) == null, "Replaced root still resolves to a source.");
    Require(ReferenceEquals(second.RootVisual, replacement) && ReferenceEquals(PresentationSource.FromVisual(replacement), second), "CompositionTarget root assignment bypassed source ownership.");
    PresentationSource.RemoveSourceChangedHandler(root, changed);
    Drain();
});
Run("attach-validation-failure-and-retry", () =>
{
    var neverAttached = new PortablePresentationSource(_ => { });
    neverAttached.Dispose();
    Require(neverAttached.IsDisposed && !Sources().Contains(neverAttached), "Unattached disposal published a source.");
    Expect<ObjectDisposedException>(() => neverAttached.AttachHost(new ContractHost()));
    using var source = new PortablePresentationSource(_ => { });
    var invalid = new ContractHost { ClientSize = new Size(double.PositiveInfinity, 4) };
    Expect<ArgumentOutOfRangeException>(() => source.AttachHost(invalid));
    Require(!Sources().Contains(source) && invalid.Subscriptions == 0, "Failed metrics validation published a source.");
    var failedHost = new ContractHost { RejectExposeSubscription = true };
    Expect<InvalidOperationException>(() => source.AttachHost(failedHost));
    Require(!Sources().Contains(source) && source.CompositionTarget == null && failedHost.Subscriptions == 0, "Partial subscription failure leaked source state.");
    var host = new ContractHost();
    source.AttachHost(host);
    Expect<InvalidOperationException>(() => source.AttachHost(host));
    Expect<InvalidOperationException>(() => source.AttachHost(new ContractHost()));
    Require(host.Subscriptions == 4, "Duplicate attachment changed subscriptions.");
    using var foreignSource = new PortablePresentationSource(_ => { });
    var report = new RawMouseInputReport(InputMode.Foreground, 0, foreignSource, RawMouseActions.AbsoluteMove, 1, 1, 0, IntPtr.Zero);
    Expect<ArgumentException>(() => host.Report(report));
    source.RootVisual = Rectangle();
    Drain();
    host.ChangeMetrics(new Size(0, 0), new DpiScale(1, 1));
    Drain();
    int frames = host.Frames;
    host.Expose();
    Drain();
    Require(host.Frames == frames, "A zero-sized host presented an old surface.");
    host.ChangeMetrics(new Size(16, 8), new DpiScale(1, 1));
    Drain();
    Require(host.Frames == frames + 1, "A restored host did not recreate its surface.");
});
Run("one-typed-input-provider-per-source", () =>
{
    var host = new ContractHost();
    using var source = new PortablePresentationSource(_ => { });
    source.AttachHost(host);
    var manager = InputManager.Current;
    int previous = manager.InputProviders.Count;
    var keyboard = source.GetInputProvider(typeof(KeyboardDevice));
    var mouse = source.GetInputProvider(typeof(MouseDevice));
    Require(keyboard is IKeyboardInputProvider && mouse is IMouseInputProvider && ReferenceEquals(keyboard, mouse), "The source did not return one correctly typed provider.");
    Require(manager.InputProviders.Count == previous + 1, "Provider lookup registered more than one input site.");
    Require(!((IKeyboardInputProvider)keyboard).AcquireFocus(false) && !((IMouseInputProvider)mouse).CaptureMouse(), "A host without input capabilities reported focus or capture success.");
    source.Dispose();
    Require(manager.InputProviders.Count == previous, "Source disposal did not unregister its input provider.");
});
Run("host-close-source-dispose-and-reuse", () =>
{
    var host = new ContractHost();
    var first = new PortablePresentationSource(_ => { });
    first.AttachHost(host);
    var root = Rectangle();
    first.RootVisual = root;
    Drain();
    host.Close();
    Require(first.IsDisposed && host.Subscriptions == 0 && PresentationSource.FromVisual(root) == null, "Host close did not detach source state.");
    using var second = new PortablePresentationSource(_ => { });
    second.AttachHost(host);
    second.RootVisual = root;
    Drain();
    Require(host.Subscriptions == 4 && ReferenceEquals(PresentationSource.FromVisual(root), second), "Reusing a borrowed host accumulated subscriptions.");
});
Run("composition-target-disposal-detaches-source", () =>
{
    var host = new ContractHost();
    using var source = new PortablePresentationSource(_ => { });
    source.AttachHost(host);
    var root = Rectangle();
    source.CompositionTarget.RootVisual = root;
    Drain();
    Require(ReferenceEquals(PresentationSource.FromVisual(root), source) && host.Frames == 1, "Public target root assignment did not present through its source.");
    source.CompositionTarget.Dispose();
    Require(source.IsDisposed && PresentationSource.FromVisual(root) == null && host.Subscriptions == 0, "Public target disposal bypassed source cleanup.");
    Require(host.CloseCalls == 0, "Composition target disposal closed its borrowed host.");
});
Run("present-callback-can-dispose-source", () =>
{
    var host = new ContractHost();
    using var source = new PortablePresentationSource(_ => { });
    source.AttachHost(host);
    var root = Rectangle();
    source.RootVisual = root;
    host.OnPresent = source.Dispose;
    Drain();
    Require(host.Frames == 1 && source.IsDisposed && host.Subscriptions == 0, "Disposal from the borrowed pixel callback left rendering active.");
    Require(PresentationSource.FromVisual(root) == null && host.CloseCalls == 0, "Pixel callback disposal leaked ownership or closed the host.");
});
Run("present-root-replacement-notifies-only-current-frame", () =>
{
    var host = new ContractHost();
    using var source = new PortablePresentationSource(_ => { });
    source.AttachHost(host);
    var original = Rectangle();
    var replacement = Rectangle();
    source.RootVisual = original;
    int rendered = 0;
    source.ContentRendered += (sender, args) => rendered++;
    host.OnPresent = () =>
    {
        host.OnPresent = null;
        source.RootVisual = replacement;
    };
    Drain();
    Require(host.Frames == 2 && rendered == 1, "A replaced target notified the new root before its own frame was presented.");
    Require(PresentationSource.FromVisual(original) == null && ReferenceEquals(PresentationSource.FromVisual(replacement), source), "Replacing a root during pixel presentation broke source ownership.");
});
Run("dpi-callback-replacement-keeps-inner-root-and-frame", () =>
{
    var host = new ContractHost { DpiScale = new DpiScale(2, 2) };
    using var source = new PortablePresentationSource(_ => { });
    source.AttachHost(host);
    var replacement = Rectangle(Brushes.Blue);
    var initial = new DpiCallbackVisual();
    using (var drawing = initial.RenderOpen()) drawing.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 4, 4));
    initial.DpiChanged = () => source.RootVisual = replacement;
    source.RootVisual = initial;
    Require(ReferenceEquals(source.RootVisual, replacement), "The inner root was overwritten.");
    Require(PresentationSource.FromVisual(initial) == null && ReferenceEquals(PresentationSource.FromVisual(replacement), source), "The abandoned outer root was still published.");
    Drain();
    Require(host.Blue(2, 2), "The abandoned outer target replaced the inner root's frame.");
});
Run("dpi-callback-disposal-abandons-local-target", () =>
{
    var host = new ContractHost { DpiScale = new DpiScale(2, 2) };
    using var source = new PortablePresentationSource(_ => { });
    source.AttachHost(host);
    var initial = new DpiCallbackVisual { DpiChanged = source.Dispose };
    var brush = new SolidColorBrush(Colors.Red);
    using (var drawing = initial.RenderOpen()) drawing.DrawRectangle(brush, null, new Rect(0, 0, 4, 4));
    int targets = RegisteredRenderTargets();
    source.RootVisual = initial;
    Drain();
    Require(source.IsDisposed && source.RootVisual == null && PresentationSource.FromVisual(initial) == null, "Disposal during DPI change retained root ownership.");
    Require(host.Frames == 0 && host.Subscriptions == 0, "An abandoned target rendered after disposal during DPI change.");
    Require(RegisteredRenderTargets() == targets, "Disposal during DPI change leaked a registered raster target.");
    int renderPosts = 0;
    var media = MediaContext.From(Dispatcher.CurrentDispatcher);
    EventHandler? rendered = null;
    rendered = (sender, args) => { renderPosts++; media.Rendering -= rendered; };
    media.Rendering += rendered;
    brush.Color = Colors.Blue;
    Drain();
    media.Rendering -= rendered;
    Require(renderPosts == 0, "A target abandoned during disposal still posts render work for old resource changes.");
});
Run("dpi-replacement-detaches-previously-published-root", () =>
{
    var host = new ContractHost { DpiScale = new DpiScale(2, 2) };
    using var source = new PortablePresentationSource(_ => { });
    source.AttachHost(host);
    var previous = Rectangle(Brushes.Green);
    source.RootVisual = previous;
    Drain();
    var replacement = Rectangle(Brushes.Blue);
    var initial = new DpiCallbackVisual { DpiChanged = () => source.RootVisual = replacement };
    source.RootVisual = initial;
    Require(PresentationSource.FromVisual(previous) == null, "Reentry left the previously published root attached.");
    Require(PresentationSource.FromVisual(initial) == null && ReferenceEquals(PresentationSource.FromVisual(replacement), source), "Reentry published the wrong root.");
    Drain();
    Require(host.Blue(2, 2), "Reentry lost the replacement raster target.");
});
Run("dpi-disposal-clears-previous-root-visibility", () =>
{
    var host = new ContractHost { DpiScale = new DpiScale(2, 2) };
    using var source = new PortablePresentationSource(_ => { });
    source.AttachHost(host);
    var previous = new UIElement();
    source.RootVisual = previous;
    Require(previous.IsVisible, "The prior UIElement root was not connected.");
    var initial = new DpiCallbackVisual { DpiChanged = source.Dispose };
    source.RootVisual = initial;
    Require(!previous.IsVisible, "Disposal during reentry left the prior root's source visibility cache attached.");
});
Run("dpi-callback-throw-preserves-newer-root-publication", () =>
{
    var host = new ContractHost { DpiScale = new DpiScale(2, 2) };
    using var source = new PortablePresentationSource(_ => { });
    source.AttachHost(host);
    var previous = new UIElement();
    source.RootVisual = previous;
    var changes = new List<SourceChangedEventArgs>();
    SourceChangedEventHandler changed = (sender, args) => changes.Add(args);
    PresentationSource.AddSourceChangedHandler(previous, changed);
    var replacement = Rectangle(Brushes.Blue);
    var initial = new DpiCallbackVisual
    {
        DpiChanged = () =>
        {
            source.RootVisual = replacement;
            throw new InvalidOperationException("Fixture DPI callback failure.");
        }
    };
    Expect<InvalidOperationException>(() => source.RootVisual = initial);
    Require(ReferenceEquals(source.RootVisual, replacement) && ReferenceEquals(PresentationSource.FromVisual(replacement), source), "An outer rollback replaced the newer inner root.");
    Require(!previous.IsVisible && changes.Count == 1 && changes[0].NewSource == null, "The prior root did not receive its real source-detachment notification.");
    Require(PresentationSource.FromVisual(initial) == null && RegisteredRenderTargets() == 1, "A throwing DPI callback leaked its abandoned root or target.");
    PresentationSource.RemoveSourceChangedHandler(previous, changed);
    Drain();
    Require(host.Blue(2, 2), "The newer target stopped rendering after an outer callback failure.");
});
Run("dpi-reentry-preserves-root-transferred-to-another-source", () =>
{
    using var first = new PortablePresentationSource(_ => { });
    using var second = new PortablePresentationSource(_ => { });
    first.AttachHost(new ContractHost { DpiScale = new DpiScale(2, 2) });
    second.AttachHost(new ContractHost());
    var previous = new UIElement();
    first.RootVisual = previous;
    var replacement = Rectangle(Brushes.Blue);
    var initial = new DpiCallbackVisual
    {
        DpiChanged = () =>
        {
            second.RootVisual = previous;
            first.RootVisual = replacement;
        }
    };
    first.RootVisual = initial;
    Require(ReferenceEquals(PresentationSource.FromVisual(previous), second) && previous.IsVisible, "An abandoned transition cleared a root already transferred to another source.");
    Require(ReferenceEquals(PresentationSource.FromVisual(replacement), first) && RegisteredRenderTargets() == 2, "The surviving source roots or raster registrations are inconsistent.");
    Drain();
});
Run("dpi-callback-metrics-change-keeps-latest-surface", () =>
{
    var host = new ContractHost { DpiScale = new DpiScale(2, 2) };
    using var source = new PortablePresentationSource(_ => { });
    source.AttachHost(host);
    var initial = new DpiCallbackVisual
    {
        DpiChanged = () => host.ChangeMetrics(new Size(8, 4), new DpiScale(2, 2))
    };
    using (var drawing = initial.RenderOpen()) drawing.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 4, 4));
    source.RootVisual = initial;
    Drain();
    Require(host.Width == 16 && host.Height == 8 && host.Red(2, 2), "The outer root transition replaced newer host dimensions with a stale local surface.");
    Require(RegisteredRenderTargets() == 1, "Metrics reentry left more than one raster target registered.");
});
Run("dpi-callback-updates-existing-root-to-latest-asymmetric-metrics", () =>
{
    var host = new ContractHost { DpiScale = new DpiScale(2, 2) };
    var layouts = new List<Size>();
    using var source = new PortablePresentationSource(size => layouts.Add(size));
    source.AttachHost(host);
    var previous = Rectangle(Brushes.Green);
    source.RootVisual = previous;
    Drain();
    var initial = new DpiCallbackVisual();
    initial.DpiChanged = () =>
    {
        initial.DpiChanged = null;
        host.ChangeMetrics(new Size(7.25, 5.25), new DpiScale(1.5, 1.25));
    };
    using (var drawing = initial.RenderOpen()) drawing.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 4, 4));
    source.RootVisual = initial;
    Drain();
    Require(host.Width == 11 && host.Height == 7 && host.Red(2, 2) && host.Transparent(6, 2) && host.Transparent(2, 5), "A root transition lost the latest client size or either DPI axis.");
    Require(VisualTreeHelper.GetDpi(initial).Equals(new DpiScale(1.5, 1.25)) && source.CompositionTarget.TransformToDevice.M11 == 1.5 && source.CompositionTarget.TransformToDevice.M22 == 1.25, "The published root and target disagree with the latest raster DPI.");
    Require(layouts.Last() == new Size(7.25, 5.25) && PresentationSource.FromVisual(previous) == null && ReferenceEquals(PresentationSource.FromVisual(initial), source) && RegisteredRenderTargets() == 1, "The latest layout, publication or target ownership was lost during metrics reentry.");
});
Run("raster-allocation-failure-preserves-root-and-metrics", () =>
{
    var host = new ContractHost();
    using var source = new PortablePresentationSource(_ => { });
    source.AttachHost(host);
    var previous = Rectangle();
    source.RootVisual = previous;
    Drain();
    Expect<InvalidOperationException>(() => host.ChangeMetrics(new Size(int.MaxValue, int.MaxValue), new DpiScale(1, 1)));
    host.Expose();
    Drain();
    Require(host.Width == 16 && host.Height == 8 && host.Red(2, 2) && ReferenceEquals(source.RootVisual, previous) && ReferenceEquals(PresentationSource.FromVisual(previous), source), "Failed resizing changed the retained surface or source ownership.");
    Require(source.CompositionTarget.TransformToDevice == Matrix.Identity && VisualTreeHelper.GetDpi(previous).Equals(new DpiScale(1, 1)) && RegisteredRenderTargets() == 1, "Failed resizing changed DPI or registered another target.");
    host.ChangeMetrics(new Size(16, 8), new DpiScale(1, 1));
    source.RootVisual = null!;
    host.ChangeMetrics(new Size(int.MaxValue, int.MaxValue), new DpiScale(1, 1));
    var rejected = Rectangle(Brushes.Blue);
    Expect<InvalidOperationException>(() => source.RootVisual = rejected);
    Require(source.RootVisual == null && PresentationSource.FromVisual(rejected) == null && RegisteredRenderTargets() == 0, "Failed initial allocation published a root or leaked a raster target.");
    host.ChangeMetrics(new Size(16, 8), new DpiScale(1, 1));
    source.RootVisual = previous;
    Drain();
    Require(host.Red(2, 2) && ReferenceEquals(PresentationSource.FromVisual(previous), source), "A source could not recover from allocation failure.");
});
Run("caught-inner-validation-failure-preserves-outer-transition", () =>
{
    var host = new ContractHost { DpiScale = new DpiScale(2, 2) };
    using var source = new PortablePresentationSource(_ => { });
    source.AttachHost(host);
    var parent = new ContainerVisual();
    var rejected = Rectangle(Brushes.Blue);
    parent.Children.Add(rejected);
    var initial = new DpiCallbackVisual { DpiChanged = () => Expect<ArgumentException>(() => source.RootVisual = rejected) };
    using (var drawing = initial.RenderOpen()) drawing.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 4, 4));
    source.RootVisual = initial;
    Drain();
    Require(ReferenceEquals(source.RootVisual, initial) && ReferenceEquals(PresentationSource.FromVisual(initial), source) && host.Red(2, 2) && RegisteredRenderTargets() == 1, "A rejected nested assignment abandoned the valid outer transition.");
});
Run("caught-inner-callback-failure-preserves-outer-transition", () =>
{
    var host = new ContractHost { DpiScale = new DpiScale(2, 2) };
    using var source = new PortablePresentationSource(_ => { });
    source.AttachHost(host);
    var rejected = new DpiCallbackVisual { DpiChanged = () => throw new InvalidOperationException("Fixture nested callback failure.") };
    var initial = new DpiCallbackVisual { DpiChanged = () => Expect<InvalidOperationException>(() => source.RootVisual = rejected) };
    using (var drawing = initial.RenderOpen()) drawing.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 4, 4));
    source.RootVisual = initial;
    Drain();
    Require(ReferenceEquals(source.RootVisual, initial) && ReferenceEquals(PresentationSource.FromVisual(initial), source) && PresentationSource.FromVisual(rejected) == null && host.Red(2, 2) && RegisteredRenderTargets() == 1, "A rolled-back nested callback abandoned the valid outer transition.");
});
Run("caught-inner-allocation-failure-preserves-outer-transition", () =>
{
    var host = new ContractHost { ClientSize = new Size(int.MaxValue / 2d, int.MaxValue / 2d), DpiScale = new DpiScale(2, 2) };
    using var source = new PortablePresentationSource(_ => { });
    source.AttachHost(host);
    var rejected = Rectangle(Brushes.Blue);
    var initial = new DpiCallbackVisual
    {
        DpiChanged = () =>
        {
            Expect<InvalidOperationException>(() => source.RootVisual = rejected);
            host.ChangeMetrics(new Size(16, 8), new DpiScale(2, 2));
        }
    };
    using (var drawing = initial.RenderOpen()) drawing.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 4, 4));
    source.RootVisual = initial;
    Drain();
    Require(ReferenceEquals(source.RootVisual, initial) && ReferenceEquals(PresentationSource.FromVisual(initial), source) && PresentationSource.FromVisual(rejected) == null && host.Width == 32 && host.Height == 16 && host.Red(2, 2) && RegisteredRenderTargets() == 1, "A rolled-back nested allocation abandoned the recovered outer transition.");
});
Run("dpi-callback-transfer-then-throw-preserves-new-owner", () =>
{
    var host = new ContractHost { DpiScale = new DpiScale(2, 2) };
    using var first = new PortablePresentationSource(_ => { });
    using var second = new PortablePresentationSource(_ => { });
    first.AttachHost(host);
    second.AttachHost(new ContractHost());
    var previous = Rectangle();
    first.RootVisual = previous;
    Drain();
    var candidate = new DpiCallbackVisual
    {
        DpiChanged = () =>
        {
            second.RootVisual = previous;
            throw new InvalidOperationException("Fixture transfer callback failure.");
        }
    };
    Expect<InvalidOperationException>(() => first.RootVisual = candidate);
    Require(first.RootVisual == null && PresentationSource.FromVisual(candidate) == null && ReferenceEquals(PresentationSource.FromVisual(previous), second), "A failed transition reclaimed the root transferred to another source.");
    Require(RegisteredRenderTargets() == 1, "The failed source retained a raster for another source's root.");
    int frames = host.Frames;
    host.Expose();
    Drain();
    Require(host.Frames == frames, "The failed source presented the other source's root.");
});
foreach (string callback in new[] { "metrics", "exposed", "raw-input", "closed" })
Run($"captured-{callback}-callback-after-earlier-listener-disposes", () =>
{
    var host = new ContractHost();
    using var source = new PortablePresentationSource(_ => { });
    Action dispose = source.Dispose;
    Action<InputReport> disposeOnInput = _ => source.Dispose();
    if (callback == "metrics") host.MetricsChanged += dispose;
    if (callback == "exposed") host.Exposed += dispose;
    if (callback == "raw-input") host.RawInput += disposeOnInput;
    if (callback == "closed") host.Closed += dispose;
    source.AttachHost(host);
    var root = Rectangle();
    source.RootVisual = root;
    Drain();
    int frames = host.Frames;
    var manager = InputManager.Current;
    int providers = manager.InputProviders.Count;
    _ = source.GetInputProvider(typeof(MouseDevice));
    if (callback == "metrics") host.ChangeMetrics(new Size(24, 12), new DpiScale(2, 1.5));
    if (callback == "exposed") host.Expose();
    if (callback == "raw-input") host.Report(new RawMouseInputReport(InputMode.Foreground, 0, source, RawMouseActions.AbsoluteMove, 1, 1, 0, IntPtr.Zero));
    if (callback == "closed") host.Close();
    Drain();
    Require(source.IsDisposed && PresentationSource.FromVisual(root) == null && !Sources().Contains(source), "A captured native callback republished a disposed source.");
    Require(host.Frames == frames && RegisteredRenderTargets() == 0 && manager.InputProviders.Count == providers, "A stale native callback rendered or recreated source resources.");
    if (callback == "metrics") host.MetricsChanged -= dispose;
    if (callback == "exposed") host.Exposed -= dispose;
    if (callback == "raw-input") host.RawInput -= disposeOnInput;
    if (callback == "closed") host.Closed -= dispose;
    Require(host.Subscriptions == 0 && host.CloseCalls == (callback == "closed" ? 1 : 0), "Callback cleanup retained subscriptions or closed a borrowed host.");
    Expect<ObjectDisposedException>(() => source.RootVisual = root);
    Expect<ObjectDisposedException>(() => source.AttachHost(host));
});
Run("stale-callback-retains-thread-affinity", () =>
{
    var host = new ContractHost();
    using var source = new PortablePresentationSource(_ => { });
    source.AttachHost(host);
    Action captured = host.CaptureExposed();
    source.Dispose();
    captured();
    Exception? wrongThread = null;
    var thread = new Thread(() =>
    {
        try { captured(); }
        catch (Exception exception) { wrongThread = exception; }
    }) { IsBackground = true };
    thread.Start();
    Require(thread.Join(TimeSpan.FromSeconds(3)) && wrongThread is InvalidOperationException and not ObjectDisposedException, "A stale native callback bypassed dispatcher affinity.");
});
Run("active-raw-callback-retains-payload-validation", () =>
{
    var host = new ContractHost();
    using var source = new PortablePresentationSource(_ => { });
    using var other = new PortablePresentationSource(_ => { });
    source.AttachHost(host);
    int providers = InputManager.Current.InputProviders.Count;
    Expect<ArgumentNullException>(() => host.Report(null!));
    Expect<ArgumentException>(() => host.Report(new RawMouseInputReport(InputMode.Foreground, 0, other, RawMouseActions.AbsoluteMove, 1, 1, 0, IntPtr.Zero)));
    Require(!source.IsDisposed && InputManager.Current.InputProviders.Count == providers, "Rejected active callbacks changed source lifetime or registered input providers.");
});
Run("foreign-thread-access-and-shutdown", () =>
{
    var host = new ContractHost();
    var source = new PortablePresentationSource(_ => { });
    Exception? wrongThread = null;
    var thread = new Thread(() =>
    {
        try { source.AttachHost(host); }
        catch (Exception exception) { wrongThread = exception; }
    }) { IsBackground = true };
    thread.Start();
    Require(thread.Join(TimeSpan.FromSeconds(3)) && wrongThread is InvalidOperationException, "Foreign thread attached a source.");
    source.AttachHost(host);
    source.RootVisual = Rectangle();
    Dispatcher.CurrentDispatcher.InvokeShutdown();
    Require(source.IsDisposed && host.Subscriptions == 0 && host.CloseCalls == 0, "Dispatcher shutdown leaked or closed a borrowed host.");
    Expect<ObjectDisposedException>(() => source.AttachHost(host));
    using var late = new PortablePresentationSource(_ => { });
    Expect<InvalidOperationException>(() => late.AttachHost(new ContractHost()));
    Require(!Sources().Contains(late), "A late source was published after dispatcher shutdown.");
});
Console.WriteLine(JsonSerializer.Serialize(new { fixture = "managed-presentation-host-contract", core = typeof(Visual).Assembly.Location, results }, new JsonSerializerOptions { WriteIndented = true }));
return failed ? 1 : 0;

void Run(string name, Action test)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try { test(); }
        catch (Exception exception) { failure = exception; }
        finally
        {
            var dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
            if (dispatcher != null && !dispatcher.HasShutdownFinished) dispatcher.InvokeShutdown();
        }
    }) { IsBackground = true, Name = name };
    if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    if (!thread.Join(TimeSpan.FromSeconds(15))) failure = new TimeoutException("Presentation source case exceeded its watchdog.");
    failed |= failure != null;
    results.Add(new { name, passed = failure == null, detail = failure?.ToString() ?? "Actual source ownership and retained frame checks passed." });
}
static DrawingVisual Rectangle(Brush? brush = null)
{
    var root = new DrawingVisual();
    using (var drawing = root.RenderOpen()) drawing.DrawRectangle(brush ?? Brushes.Red, null, new Rect(0, 0, 4, 4));
    return root;
}
static HashSet<PresentationSource> Sources() => PresentationSource.CurrentSources.Cast<PresentationSource>().ToHashSet();
static int RegisteredRenderTargets()
{
    var media = MediaContext.From(Dispatcher.CurrentDispatcher);
    var targets = typeof(MediaContext).GetField("_portableRenderTargets", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(media)!;
    return (int)targets.GetType().GetProperty("Count")!.GetValue(targets)!;
}
static void Drain()
{
    var frame = new DispatcherFrame();
    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
    Dispatcher.PushFrame(frame);
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static void Expect<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
internal sealed class ContractHost : IPortablePresentationHost
{
    private Action? metricsChanged;
    private Action? exposed;
    private Action? closed;
    private Action<InputReport>? rawInput;
    internal bool RejectExposeSubscription { get; init; }
    public Size ClientSize { get; set; } = new Size(16, 8);
    public DpiScale DpiScale { get; set; } = new DpiScale(1, 1);
    public event Action MetricsChanged { add => metricsChanged += value; remove => metricsChanged -= value; }
    public event Action Exposed
    {
        add { if (RejectExposeSubscription) throw new InvalidOperationException("Fixture rejects expose subscription."); exposed += value; }
        remove => exposed -= value;
    }
    public event Action Closed { add => closed += value; remove => closed -= value; }
    public event Action<InputReport> RawInput { add => rawInput += value; remove => rawInput -= value; }
    internal int Subscriptions => (metricsChanged?.GetInvocationList().Length ?? 0) + (exposed?.GetInvocationList().Length ?? 0) + (closed?.GetInvocationList().Length ?? 0) + (rawInput?.GetInvocationList().Length ?? 0);
    internal int Frames { get; private set; }
    internal int CloseCalls { get; private set; }
    internal int Width { get; private set; }
    internal int Height { get; private set; }
    internal int Stride { get; private set; }
    internal byte[] Pixels { get; private set; } = [];
    internal Action? OnPresent { get; set; }
    public void Present(ReadOnlySpan<byte> pixels, int pixelWidth, int pixelHeight, int stride)
    {
        Width = pixelWidth;
        Height = pixelHeight;
        Stride = stride;
        Pixels = pixels.ToArray();
        Frames++;
        OnPresent?.Invoke();
    }
    internal bool Red(int x, int y) => Pixels[y * Stride + x * 4] == 0 && Pixels[y * Stride + x * 4 + 1] == 0 && Pixels[y * Stride + x * 4 + 2] == 255 && Pixels[y * Stride + x * 4 + 3] == 255;
    internal bool Transparent(int x, int y) => Pixels[y * Stride + x * 4 + 3] == 0;
    internal bool Blue(int x, int y) => Pixels[y * Stride + x * 4] == 255 && Pixels[y * Stride + x * 4 + 1] == 0 && Pixels[y * Stride + x * 4 + 2] == 0 && Pixels[y * Stride + x * 4 + 3] == 255;
    internal void ChangeMetrics(Size size, DpiScale dpi) { ClientSize = size; DpiScale = dpi; metricsChanged?.Invoke(); }
    internal void Expose() => exposed?.Invoke();
    internal Action CaptureExposed() => exposed!;
    internal void Report(InputReport report) => rawInput?.Invoke(report);
    internal void Close() { CloseCalls++; closed?.Invoke(); }
}
internal sealed class DpiCallbackVisual : DrawingVisual
{
    internal Action? DpiChanged { get; set; }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        DpiChanged?.Invoke();
    }
}
