using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;

internal static partial class Program
{
    private static string[] EditorRequiredChecks => RequiredChecks.Take(7).Concat(new[] {
        "editable-insertion-selection-UndoRedo-readonly", "caret-raster-show-hide-blink",
        "cursor-hide-move-focus-unload-restore" }).ToArray();
    private static int? blinkFixture;

    private static void ConfigureEditorProfile()
    {
        string? input = Environment.GetEnvironmentVariable("AVALONEDIT_BLINK_FIXTURE");
        if (input == null || Stock) return;
        int value = int.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        Require(value is -1 or 0 or 80, "Only declared sentinel or short positive fixture intervals allowed.");
        var core = typeof(Visual).Assembly;
        var settings = core.GetType("MS.Internal.Portability.Settings.PortableSystemSettings", true)!;
        var profile = settings.GetMethod("CreateInitialLightProfile", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object?[] { false, null, 1d, value, false, 4d, 4d, null, null, 400, 4d, 4d });
        core.GetType("MS.Internal.Portability.Settings.PortableSystemSettingsProvider", true)!
            .GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { profile });
        blinkFixture = value;
        observations.Add(new { stage = "explicit-component-startup-profile", value, observedDesktopPreference = false });
    }

    private static void RunEditorControls(IDisposable? native)
    {
        var editor = new TextEditor { Text = "alpha beta", FontSize = 18,
            FontFamily = new FontFamily(new Uri(AppContext.BaseDirectory), "./Fonts/#DejaVu Sans"),
            Background = Brushes.White, Foreground = Brushes.Black, Margin = new Thickness(12) };
        editor.TextArea.Caret.CaretBrush = Brushes.Magenta;
        var neighbor = new Button { Content = "Neighbor", Height = 30 };
        var panel = new DockPanel(); DockPanel.SetDock(neighbor, Dock.Bottom);
        panel.Children.Add(neighbor); panel.Children.Add(editor);
        var window = new Window { Title = "Owned genuine AvalonEdit controls", Width = 440, Height = 240,
            ShowInTaskbar = false, Content = panel };
        try
        {
            window.Show(); SettleLayout(window);
            Require(editor.TextArea.Focus(), "Genuine editable TextArea did not focus."); Settle();
            Check("editable-insertion-selection-UndoRedo-readonly", () =>
            {
                editor.Select(0, 5); Input("Omega"); Require(editor.Text == "Omega beta", "Selected insertion failed.");
                Require(ApplicationCommands.Undo.CanExecute(null, editor.TextArea), "Actual Undo command disabled.");
                ApplicationCommands.Undo.Execute(null, editor.TextArea); Require(editor.Text == "alpha beta", "Undo failed.");
                Require(ApplicationCommands.Redo.CanExecute(null, editor.TextArea), "Actual Redo command disabled.");
                ApplicationCommands.Redo.Execute(null, editor.TextArea); Require(editor.Text == "Omega beta", "Redo failed.");
                editor.TextArea.Selection = Selection.Create(editor.TextArea, 8, 2);
                Require(editor.SelectedText == "ega be", "Reverse selection failed.");
                editor.TextArea.Selection = Selection.Create(editor.TextArea, 2, 8);
                Require(editor.SelectedText == "ega be", "Forward selection failed.");
                editor.IsReadOnly = true; Input("BLOCKED");
                if (ApplicationCommands.Delete.CanExecute(null, editor.TextArea)) ApplicationCommands.Delete.Execute(null, editor.TextArea);
                Require(editor.Text == "Omega beta", "Readonly insert/Delete mutated genuine editor.");
                editor.IsReadOnly = false; editor.Select(editor.Text.Length, 0); Input("!");
                Require(editor.Text == "Omega beta!", "Reenabled editable input failed.");
                editor.Select(0, 0); observations.Add(new { stage = "editable-pairs", text = editor.Text,
                    input = "routed TextInput and genuine commands", physicalKeyboardQualified = false });
            });
            Check("caret-raster-show-hide-blink", () =>
            {
                editor.TextArea.Caret.Hide(); SettleLayout(window); Require(CaretPixels() == 0, "Hidden caret left magenta ink.");
                editor.TextArea.Caret.Show(); SettleLayout(window);
                Rect rect = editor.TextArea.Caret.CalculateCaretRectangle();
                Require(!rect.IsEmpty && rect.Width > 0 && rect.Height > 0, "Public caret rectangle is empty.");
                Require(CaretPixels() > 0, "Shown caret has no magenta ink at public rectangle.");
                var actualCaretLayer = typeof(Caret).GetField("caretAdorner", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor.TextArea.Caret);
                var actualCaretTimer = actualCaretLayer?.GetType().GetField("caretBlinkTimer", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(actualCaretLayer) as DispatcherTimer;
                Require(actualCaretTimer != null, "Actual AvalonEdit caret DispatcherTimer is unavailable.");
                if (blinkFixture == 80)
                    Require(actualCaretTimer!.Interval == TimeSpan.FromMilliseconds(80) && actualCaretTimer.IsEnabled, "Explicit 80ms profile did not set the effective caret timer interval.");
                if (blinkFixture is 0 or -1)
                    Require(!actualCaretTimer!.IsEnabled, "Disabled caret profile started the actual timer.");
                observations.Add(new { stage = "effective-caret-timer", blinkFixture, effectiveIntervalMilliseconds = actualCaretTimer!.Interval.TotalMilliseconds, isEnabled = actualCaretTimer.IsEnabled });
                var samples = new List<bool> { true };
                if (blinkFixture is 0 or -1)
                {
                    for (int i = 0; i < 5; i++) { Delay(100); samples.Add(CaretPixels() > 0); }
                    Require(samples.All(x => x), "Disabled blink sentinel did not keep caret visible.");
                }
                else
                {
                    var timer = Stopwatch.StartNew(); bool off = false, onAgain = false;
                    while (timer.ElapsedMilliseconds < 2500 && !onAgain)
                    {
                        Delay(25); bool on = CaretPixels() > 0; samples.Add(on);
                        if (!on) off = true; else if (off) onAgain = true;
                    }
                    Require(off && onAgain, "Positive blink did not produce actual on/off/on rasters.");
                }
                editor.TextArea.Caret.Hide(); Settle(); Require(CaretPixels() == 0, "Second Hide did not remove caret.");
                editor.TextArea.Caret.Show(); Settle(); Require(CaretPixels() > 0, "Second Show did not restore caret.");
                Require(neighbor.Focus(), "Caret neighbor did not focus."); Settle(); Require(CaretPixels() == 0, "Focus loss left caret ink.");
                Require(editor.TextArea.Focus(), "Caret focus did not return."); Settle(); Require(CaretPixels() > 0, "Focus return did not restore caret.");
                observations.Add(new { stage = "actual-caret-raster", blinkFixture, rect, samples,
                    source = PresentationSource.FromVisual(editor)?.GetType().FullName, exactTimestampAsserted = false });
            });
            Check("cursor-hide-move-focus-unload-restore", () =>
            {
                if (Stock) { observations.Add(new { stage = "stock-cursor-policy", branch = "original WinForms", privateDesktopOnly = true }); return; }
                var x11 = (X11Fixture)native!;
                QueryCursorEventHandler child = (_, args) => { args.Cursor = Cursors.Hand; args.Handled = true; };
                editor.TextArea.TextView.AddHandler(Mouse.QueryCursorEvent, child);
                try
                {
                    Move(30); Require(editor.TextArea.IsMouseOver && x11.CursorImage().Visible, "Native pointer did not enter measured editor.");
                    HideAndQuery();
                    Move(35); Require(x11.CursorImage().Visible && Query() == Cursors.Hand, "Movement did not restore handled child cursor.");
                    HideAndQuery(); Require(neighbor.Focus(), "Cursor focus neighbor failed."); Settle();
                    Require(x11.CursorImage().Visible && Query() == Cursors.Hand, "Focus loss did not restore cursor query/native image.");
                    Require(editor.TextArea.Focus(), "Cursor editor focus return failed."); Settle(); HideAndQuery();
                    panel.Children.Remove(editor); SettleLayout(window);
                    Require(Query() == Cursors.Hand && x11.CursorImage().Visible, "Unload retained hidden cursor ownership.");
                    panel.Children.Add(editor); SettleLayout(window); Move(40);
                    Require(Query() == Cursors.Hand && x11.CursorImage().Visible, "Reload did not restore normal child query.");
                    observations.Add(new { stage = "cursor-pairs", handledChildPreservedWhenVisible = true,
                        hideScope = "WPF editor queries", nativeCursorImagesObserved = true, globalOverrideChanged = false });
                }
                finally { editor.TextArea.TextView.RemoveHandler(Mouse.QueryCursorEvent, child); }

                void Move(int x) { Point p = editor.TextArea.TextView.PointToScreen(new Point(x, 12)); x11.MovePointer((int)p.X, (int)p.Y); Delay(40); }
                Cursor? Query() { var args = new QueryCursorEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.QueryCursorEvent, Cursor = Cursors.Arrow }; editor.TextArea.TextView.RaiseEvent(args); return args.Cursor; }
                void HideAndQuery() { editor.Select(0, 0); Input("x"); Settle(); Require(Query() == Cursors.None && !x11.CursorImage().Visible, "Typing did not hide handled child query and native cursor."); }
            });
        }
        finally { window.Close(); Settle(); }
        Require(!window.IsVisible && !editor.IsKeyboardFocusWithin, "Editor window close retained active focus.");

        void Input(string text) { editor.TextArea.RaiseEvent(new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice,
            new TextComposition(InputManager.Current, editor.TextArea, text)) { RoutedEvent = TextCompositionManager.TextInputEvent }); Settle(); }
        int CaretPixels()
        {
            int width=(int)Math.Ceiling(window.ActualWidth),height=(int)Math.Ceiling(window.ActualHeight);
            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); bitmap.Render(window);
            var bytes=new byte[width*height*4];bitmap.CopyPixels(bytes,width*4,0);
            var caret=editor.TextArea.Caret.CalculateCaretRectangle();
            Point origin=editor.TextArea.TextView.TransformToAncestor(window).Transform(caret.TopLeft-editor.TextArea.TextView.ScrollOffset);
            int count=0;
            for(int y=Math.Max(0,(int)Math.Floor(origin.Y)-2);y<Math.Min(height,(int)Math.Ceiling(origin.Y+caret.Height)+2);y++)
                for(int x=Math.Max(0,(int)Math.Floor(origin.X)-2);x<Math.Min(width,(int)Math.Ceiling(origin.X+caret.Width)+2);x++)
                {int i=(y*width+x)*4;if(bytes[i]>220&&bytes[i+1]<30&&bytes[i+2]>220&&bytes[i+3]>220)count++;}
            return count;
        }
    }

    private static void Delay(int milliseconds)
    {
        var frame=new DispatcherFrame();var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(milliseconds)};
        timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
    }
}
