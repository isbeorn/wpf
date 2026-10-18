extern alias WpfFork;

using System.Reflection;
using Avalonia.Input.Raw;
using WpfFork::System.Windows;
using WpfFork::System.Windows.Controls;
using WpfFork::System.Windows.Data;
using WpfFork::System.Windows.Input;
using WpfFork::System.Windows.Media;

internal static class NativeTextCommitChecks
{
    internal static void CompleteString(X11Fixture native, Action<Func<bool>> wait, Action drain, FontFamily font, string commit)
    {
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        var box = new TextBox { OverridesDefaultStyle=true, FontFamily=font, Width=300, Height=60,
            Template=new ControlTemplate(typeof(TextBox)) { VisualTree=host } };
        var model = new Model();
        box.SetBinding(TextBox.TextProperty,new Binding(nameof(Model.Value)) { Source=model,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged });
        var window = new Window { Title="Native complete text callback",Width=360,Height=120,Content=box,FontFamily=font };
        var texts = new List<string>();
        box.AddHandler(TextCompositionManager.TextInputEvent,new TextCompositionEventHandler((_,args) => texts.Add(args.Text)),true);
        try
        {
            window.Show(); var xid=native.FindWindow(window.Title);
            wait(() => native.IsMapped(xid) && box.ActualHeight == 60);
            native.Focus(xid); native.Click(xid,180,60); wait(() => box.IsKeyboardFocused);
            native.Type('a'); wait(() => box.Text == "a" && model.Value == "a" && Keyboard.IsKeyUp(Key.A));
            if(texts.Count != 1 || texts[0] != "a") throw new InvalidOperationException("Ordinary native BMP keyboard text was duplicated or lost.");
            box.SelectAll(); box.IsUndoEnabled=false; box.IsUndoEnabled=true; texts.Clear();
            object peer=typeof(Window).GetField("_portablePeer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            object implementation=peer.GetType().GetField("_window",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(peer)!;
            var keyboard=(Avalonia.Input.IKeyboardDevice)implementation.GetType().GetField("_keyboard",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(implementation)!;
            var root=(Avalonia.Input.IInputRoot)implementation.GetType().GetProperty("InputRoot")!.GetValue(implementation)!;
            void Deliver(string value)
            {
                var report=(RawTextInputEventArgs)Activator.CreateInstance(typeof(RawTextInputEventArgs),[keyboard,1UL,root,value])!;
                try { peer.GetType().GetMethod("OnInput",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(peer,[report]); }
                catch(TargetInvocationException exception) when(exception.InnerException is not null)
                { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); }
            }
            Deliver(commit);
            drain();
            if(box.Text != commit || model.Value != commit || texts.Count != 1 || texts[0] != commit)
                throw new InvalidOperationException($"Complete native callback was not one exact routed/editor commit: expected={commit}, actual={box.Text}, model={model.Value}, events={texts.Count}, payloads={string.Join("|",texts)}.");
            box.Undo(); drain();
            if(box.Text != "a" || model.Value != "a" || box.CanUndo) throw new InvalidOperationException("One undo did not restore the replaced selection and bound source.");
            box.Redo(); drain();
            if(box.Text != commit || model.Value != commit || box.CanRedo) throw new InvalidOperationException("One redo did not restore the whole native commit.");
            texts.Clear(); Deliver(""); drain();
            if(box.Text != commit || model.Value != commit || texts.Count != 0) throw new InvalidOperationException("An empty native commit changed editor state or routed text.");
            foreach(string invalid in new[] {"\ud800","prefix\udc00"})
            {
                bool rejected=false;
                try { Deliver(invalid); } catch(ArgumentException) {rejected=true;}
                drain();
                if(!rejected || box.Text != commit || model.Value != commit || texts.Count != 0)
                    throw new InvalidOperationException("Malformed native commit was not rejected before partial editing.");
            }
            TextCompositionEventHandler cancel=(_,args) => args.Handled=true;
            box.PreviewTextInput += cancel;
            try { Deliver("cancel\U0001F600"); drain(); }
            finally {box.PreviewTextInput -= cancel;}
            if(box.Text != commit || model.Value != commit || texts.Count != 0) throw new InvalidOperationException("Handled native commit reached the editor.");
            foreach(string control in new[] { "\r", "\t", "\u0001" })
            {
                int received=0;
                TextCompositionEventHandler cancelControl=(_,args) =>
                {
                    if(args.Text != "" || args.ControlText != control || args.SystemText != "")
                        throw new InvalidOperationException("A native single-control commit changed its composition route.");
                    received++; args.Handled=true;
                };
                box.PreviewTextInput += cancelControl;
                try { Deliver(control); drain(); }
                finally { box.PreviewTextInput -= cancelControl; }
                if(received != 1 || box.Text != commit || model.Value != commit || texts.Count != 0)
                    throw new InvalidOperationException("Handled native control text reached the editor or was routed more than once.");
            }
            window.IsEnabled=false; Deliver("disabled"); drain();
            if(box.Text != commit || model.Value != commit) throw new InvalidOperationException("Disabled native window accepted text.");
            window.IsEnabled=true; window.Hide(); Deliver("hidden"); drain();
            if(box.Text != commit || model.Value != commit) throw new InvalidOperationException("Hidden native window accepted text.");
            window.Close(); Deliver("closed"); drain();
            if(box.Text != commit || model.Value != commit || PresentationSource.FromVisual(box) != null)
                throw new InvalidOperationException("Closed native source accepted text or retained the editor.");
        }
        finally { window.Close(); }
    }

    internal static List<object> Observations { get; } = new();

    internal static void KeyCommands(X11Fixture native,Action<Func<bool>> wait,Action drain,FontFamily font)
    {
        foreach(bool multiline in new[] {false,true})
        {
            var part=new FrameworkElementFactory(typeof(ScrollViewer),"PART_ContentHost");
            var box=new TextBox {OverridesDefaultStyle=true,FontFamily=font,Width=300,Height=80,AcceptsReturn=multiline,AcceptsTab=false,
                Template=new ControlTemplate(typeof(TextBox)) {VisualTree=part}};
            var model=new Model();
            box.SetBinding(TextBox.TextProperty,new Binding(nameof(Model.Value)) {Source=model,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
            var window=new Window {Title="Native text key commands " + multiline,Width=360,Height=140,Content=box,FontFamily=font};
            var routed=new List<object>(); int enter=0,tab=0;
            box.PreviewKeyDown += (_,args) => {if(args.Key == Key.Return) enter++; if(args.Key == Key.Tab) tab++;};
            box.AddHandler(TextCompositionManager.TextInputEvent,new TextCompositionEventHandler((_,args) =>
                routed.Add(new {args.Text,args.ControlText,args.SystemText})),true);
            try
            {
                window.Show(); var xid=native.FindWindow(window.Title);
                wait(() => native.IsMapped(xid) && box.ActualHeight == 80);
                native.Focus(xid);native.Click(xid,180,70);wait(() => box.IsKeyboardFocused);
                native.Type('a');wait(() => box.Text == "a" && Keyboard.IsKeyUp(Key.A));
                routed.Clear();
                Press('\uff09');wait(() => tab == 1 && Keyboard.IsKeyUp(Key.Tab));drain();
                if(box.Text != "a" || model.Value != "a") throw new InvalidOperationException("Native Tab inserted text despite AcceptsTab=false.");
                Observations.Add(new {phase="native-tab",multiline,box.Text,model=model.Value,events=routed.ToArray()});
                routed.Clear();
                try {native.Key('\uffe3',true);native.Type('a');}
                finally {native.Key('\uffe3',false);}
                wait(() => box.SelectionStart == 0 && box.SelectionLength == box.Text.Length && Keyboard.IsKeyUp(Key.LeftCtrl));
                if(box.Text != "a" || model.Value != "a") throw new InvalidOperationException("Native Ctrl+A inserted its control character.");
                Observations.Add(new {phase="native-control-select-all",multiline,box.Text,box.SelectionStart,box.SelectionLength,events=routed.ToArray()});
                native.Type('z');wait(() => box.Text == "z" && model.Value == "z" && Keyboard.IsKeyUp(Key.Z));drain();
                if(box.Text != "z" || model.Value != "z") throw new InvalidOperationException("Native selection replacement after Ctrl+A duplicated text.");
                routed.Clear();
                Press('\uff0d');
                string expected=multiline?"z"+Environment.NewLine:"z";
                wait(() => enter == 1 && Keyboard.IsKeyUp(Key.Return) && box.Text == expected && model.Value == expected);
                drain();
                if(box.Text != expected || model.Value != expected) throw new InvalidOperationException("Native Return inserted more than once or ignored AcceptsReturn.");
                Observations.Add(new {phase="native-return",multiline,box.Text,model=model.Value,events=routed.ToArray()});
            }
            finally {native.Key('\uffe3',false);window.Close();}

            void Press(char key)
            {
                try {native.Key(key,true);}
                finally {native.Key(key,false);}
            }
        }
    }

    private sealed class Model { public string Value {get;set;}=""; }
}
