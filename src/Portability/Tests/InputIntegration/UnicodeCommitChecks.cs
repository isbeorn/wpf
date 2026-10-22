using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MS.Internal.Portability.Input;
using MS.Internal.Portability.Windowing;

internal static class UnicodeCommitChecks
{
    internal static void Run(ControlsHost host,PortablePresentationSource source,TextBox box,AdornerDecorator root,TextModel model,string fontPath)
    {
        box.FontFamily=new FontFamily(new Uri(Path.GetFullPath(fontPath)),"DejaVu Sans" );
        var events=new List<string>();
        box.AddHandler(TextCompositionManager.TextInputEvent,new TextCompositionEventHandler((_,args) => events.Add(args.Text)),true);
        void Commit(string text) { host.Report(new PortableTextInputReport(source,InputMode.Foreground,1,text)); Drain(); }
        void Exact(string expected) => Require(box.Text == expected && model.Value == expected,"Actual editor/source mismatch: " + box.Text + " / " + model.Value);

        foreach(string text in new[] {"abc","\U0001F600","ab\U0001F600\u03a9e\u0301"})
        {
            box.SelectAll(); events.Clear(); Commit(text); Exact(text);
            Require(events.Count == 1 && events[0] == text,"Committed string was split or changed in routed TextInput.");
        }
        foreach(string boundary in new[] {"\uD800\uDC00","\uDBFF\uDFFF"})
            Require(new PortableTextInputReport(source,InputMode.Foreground,1,boundary).Text == boundary,"Valid supplementary scalar boundary changed in the internal report.");
        Console.WriteLine("PASS complete routed strings, supplementary scalar report boundaries and bound editor values");

        box.SelectAll(); Commit("ABCDE"); ResetUndo(box);
        box.Select(1,2); events.Clear(); Commit("\U0001F600e\u0301"); Exact("A\U0001F600e\u0301DE");
        Require(events.SequenceEqual(new[] {"\U0001F600e\u0301"}),"Selection replacement did not preserve one exact routed payload.");
        box.Undo(); Drain(); Exact("ABCDE");
        Require(!box.CanUndo && box.SelectionStart == 1 && box.SelectionLength == 2,"One undo failed to restore the replaced range as one operation.");
        box.Redo(); Drain(); Exact("A\U0001F600e\u0301DE");
        Require(!box.CanRedo && box.SelectionLength == 0 && box.CaretIndex == 5,"Redo did not restore the complete UTF-16 commit/caret.");
        Console.WriteLine("PASS selected replacement, one undo/redo and UTF-16 selection/caret");

        string before=box.Text; events.Clear(); ResetUndo(box);
        TextCompositionEventHandler cancel=(_,args) => { if(args.Text == "blocked\U0001F600") args.Handled=true; };
        box.PreviewTextInput += cancel;
        try { Commit("blocked\U0001F600"); }
        finally { box.PreviewTextInput -= cancel; }
        Exact(before); Require(events.Count == 0 && !box.CanUndo,"Preview Handled did not cancel editor mutation and the bubble route.");
        Commit(""); Exact(before); Require(events.Count == 0 && !box.CanUndo,"Empty commit produced input or undo.");
        foreach(string invalid in new[] {"\ud800","\udc00","ok\ud800x","ok\udc00","\ud800\ud800","\udc00\ud800"})
        {
            bool rejected=false;
            try { Commit(invalid); } catch(ArgumentException) { rejected=true; }
            Require(rejected,"Malformed UTF-16 was accepted."); Exact(before);
            Require(events.Count == 0 && !box.CanUndo,"Malformed commit partially edited or routed text.");
        }
        bool nullRejected=false;
        try { _=new PortableTextInputReport(source,InputMode.Foreground,1,null!); } catch(ArgumentNullException) { nullRejected=true; }
        Require(nullRejected,"Null committed text was accepted.");
        foreach(var legacy in new[] {(true,'a'),(false,'\ud800'),(false,'\udc00')})
        {
            bool rejected=false;
            try { host.Report(new RawTextInputReport(source,InputMode.Foreground,1,legacy.Item1,false,false,legacy.Item2)); }
            catch(PlatformNotSupportedException exception) { rejected=exception.Message == "Portable text input currently supports committed BMP characters without dead keys."; }
            Require(rejected,"Legacy character/dead-key diagnostic changed."); Exact(before);
        }
        Console.WriteLine("PASS Handled cancellation, empty/null/malformed no partial mutation and legacy diagnostics");

        box.IsReadOnly=true; Commit("readonly\U0001F600"); Exact(before); box.IsReadOnly=false;
        box.IsEnabled=false; Commit("disabled\U0001F600"); Exact(before); box.IsEnabled=true;
        Require(ReferenceEquals(Keyboard.Focus(box),box),"The editor could not regain keyboard focus.");
        Keyboard.ClearFocus(); Commit("unfocused\U0001F600"); Exact(before);
        Require(ReferenceEquals(Keyboard.Focus(box),box),"The editor could not reacquire keyboard focus.");
        using(var otherSource=new PortablePresentationSource(_ => { }))
        {
            var otherHost=new ControlsHost {Source=otherSource}; otherSource.AttachHost(otherHost);
            bool foreignRejected=false;
            try { ((PortableInputProvider)source.GetInputProvider(typeof(KeyboardDevice))).ReportInput(new PortableTextInputReport(otherSource,InputMode.Foreground,1,"foreign")); }
            catch(ArgumentException) {foreignRejected=true;}
            Require(foreignRejected,"A foreign-source complete text report was accepted.");
            var otherBox=new TextBox {Width=200,Height=40,Template=box.Template};
            var otherModel=new TextModel();
            otherBox.SetBinding(TextBox.TextProperty,new Binding(nameof(TextModel.Value)) {Source=otherModel,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
            var otherRoot=new AdornerDecorator {Child=otherBox}; otherSource.RootVisual=otherRoot;
            otherRoot.Measure(new Size(200,40)); otherRoot.Arrange(new Rect(0,0,200,40)); otherRoot.UpdateLayout();
            Require(ReferenceEquals(Keyboard.Focus(otherBox),otherBox),"The second actual source did not acquire focus.");
            Commit("inactive\U0001F600"); Exact(before);
            Require(otherBox.Text == "","Inactive-source text leaked into the focused editor.");
            otherHost.Report(new PortableTextInputReport(otherSource,InputMode.Foreground,1,"second\U0001F600")); Drain();
            Require(otherBox.Text == "second\U0001F600" && otherModel.Value == otherBox.Text,"Focused source did not commit complete text.");
        }
        Require(ReferenceEquals(Keyboard.Focus(box),box),"Source disposal prevented refocusing the surviving editor.");
        box.SelectAll(); events.Clear(); Commit("surviving\U0001F600"); Exact("surviving\U0001F600");
        Require(events.Count == 1,"Focus reacquisition duplicated the commit.");
        var provider=(PortableInputProvider)source.GetInputProvider(typeof(KeyboardDevice));
        source.Dispose(); bool disposed=false;
        try { provider.ReportInput(new PortableTextInputReport(source,InputMode.Foreground,1,"closed")); } catch(ObjectDisposedException) {disposed=true;}
        Require(disposed && Keyboard.FocusedElement is null && PresentationSource.FromVisual(box) is null,"Disposed source accepted committed text or retained focus.");
        Exact("surviving\U0001F600");
        Console.WriteLine("PASS readonly/disabled/unfocused isolation, source handoff/reacquisition and disposal");
    }

    private static void ResetUndo(TextBox box) { box.IsUndoEnabled=false; box.IsUndoEnabled=true; }
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(() => { }));
    private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
}
