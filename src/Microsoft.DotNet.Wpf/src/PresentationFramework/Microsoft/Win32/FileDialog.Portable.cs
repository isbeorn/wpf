// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
using MS.Internal.Portability.Windowing;

namespace Microsoft.Win32
{
    public abstract partial class FileDialog
    {
        private bool _portableShowing;

        internal bool? ShowPortableDialog(Window owner)
        {
            if (_portableShowing) throw new InvalidOperationException("This file dialog is already showing.");
            if (!Environment.UserInteractive) throw new InvalidOperationException(SR.CantShowModalOnNonInteractive);
            owner ??= FindPortableOwner();
            owner.VerifyAccess();
            PortableFileDialogOptions options = CreatePortableOptions();
            _portableShowing = true;
            IPortableWindowPeer peer = null;
            bool pushed = false, failed = false;
            using var cancellation = new CancellationTokenSource();
            EventHandler cancel = (_, _) => cancellation.Cancel();
            owner.Closed += cancel;
            owner.Dispatcher.ShutdownStarted += cancel;
            ExceptionDispatchInfo cleanupFailure = null;
            IInputElement previousFocus = Keyboard.FocusedElement;
            try
            {
                peer = owner.PortableBeginFileDialog();
                if (peer is not IPortableFileDialogHost host)
                    throw new PlatformNotSupportedException("The native owner has no file chooser portal capability.");
                pushed = true;
                ComponentDispatcher.PushModal();
                while (true)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    PortableFileDialogResult selection = AwaitPortableDialog(host.ShowFileDialogAsync(options, cancellation.Token), owner.Dispatcher);
                    if (!owner.PortableOwnsDialogPeer(peer) || !selection.Accepted) return false;
                    ValidatePortableSelection(selection, options.AllowMultiple);
                    string[] previousNames = MutableItemNames;
                    int previousFilter = _filterIndex;
                    bool accepted = false;
                    try
                    {
                        MutableItemNames = selection.Paths.ToArray();
                        if (selection.FilterIndex is int selectedFilter) _filterIndex = selectedFilter;
                        var args = new CancelEventArgs();
                        OnItemOk(args);
                        accepted = !args.Cancel;
                        if (accepted) return true;
                    }
                    finally
                    {
                        if (!accepted) { MutableItemNames = previousNames; _filterIndex = previousFilter; }
                    }
                    if (!owner.PortableOwnsDialogPeer(peer)) return false;
                    options = CreatePortableOptions();
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return false; }
            catch { failed = true; throw; }
            finally
            {
                try { cancellation.Cancel(); }
                catch (Exception error) { cleanupFailure = ExceptionDispatchInfo.Capture(error); }
                owner.Closed -= cancel;
                owner.Dispatcher.ShutdownStarted -= cancel;
                if (pushed)
                    try { ComponentDispatcher.PopModal(); }
                    catch (Exception error) { cleanupFailure = ExceptionDispatchInfo.Capture(error); }
                if (peer != null)
                    try
                    {
                        owner.PortableEndFileDialog(peer, true);
                        if (owner.PortableOwnsDialogPeer(peer) && peer.IsEnabled && previousFocus is DependencyObject element &&
                            Window.GetWindow(element) == owner && !ReferenceEquals(Keyboard.FocusedElement, previousFocus))
                            Keyboard.Focus(previousFocus);
                    }
                    catch (Exception error) { cleanupFailure ??= ExceptionDispatchInfo.Capture(error); }
                _portableShowing = false;
                if (!failed) cleanupFailure?.Throw();
            }
        }

        private static Window FindPortableOwner()
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            PresentationSource active = Keyboard.PrimaryDevice.ActiveSource;
            if (active != null && active.Dispatcher == dispatcher && !active.IsDisposed && active.RootVisual is Window focused && focused.IsVisible)
                return focused;
            foreach (PresentationSource source in PresentationSource.CurrentSources)
                if (source.Dispatcher == dispatcher && source.RootVisual is Window window && window.IsVisible && window.IsActive)
                    return window;
            Window main = Application.Current?.MainWindow;
            if (main != null && main.Dispatcher == dispatcher && main.IsVisible) return main;
            throw new PlatformNotSupportedException("A portable file dialog requires a visible native owner or active Window.");
        }

        private PortableFileDialogOptions CreatePortableOptions()
        {
            bool save = this is SaveFileDialog;
            if (!save && this is not OpenFileDialog)
                throw new PlatformNotSupportedException("Only native OpenFileDialog and SaveFileDialog are supported.");
            if (ClientGuid != null || CustomPlaces?.Count > 0 || !string.IsNullOrEmpty(RootDirectory))
                throw new PlatformNotSupportedException("Windows shell places, root navigation and client persistence are unavailable in the native file chooser.");
            if (this is OpenFileDialog open && (open.ShowReadOnly || open.ReadOnlyChecked || open.ForcePreviewPane))
                throw new PlatformNotSupportedException("Windows read-only and preview controls are unavailable in the native file chooser.");
            if (this is SaveFileDialog saving && (saving.CreatePrompt || !saving.OverwritePrompt))
                throw new PlatformNotSupportedException("The native save chooser owns overwrite confirmation; CreatePrompt and disabling OverwritePrompt are unavailable.");
            var filters = new List<PortableFileDialogFilter>();
            if (!string.IsNullOrEmpty(Filter))
            {
                string[] parts = Filter.Split('|');
                for (int index = 0; index < parts.Length; index += 2)
                    filters.Add(new PortableFileDialogFilter(parts[index], Array.AsReadOnly(parts[index + 1].Split(';').Select(pattern => pattern == "*.*" ? "*" : pattern).ToArray())));
                if (_filterIndex < 1 || _filterIndex > filters.Count) throw new InvalidOperationException(SR.FileDialogInvalidFilterIndex);
            }
            string directory = InitialDirectory;
            if (string.IsNullOrEmpty(directory)) directory = Path.GetDirectoryName(FileName);
            if (string.IsNullOrEmpty(directory)) directory = DefaultDirectory;
            return new PortableFileDialogOptions(save, Title, Path.GetFileName(FileName), directory,
                filters.AsReadOnly(), _filterIndex, this is OpenFileDialog selecting && selecting.Multiselect);
        }

        private static PortableFileDialogResult AwaitPortableDialog(Task<PortableFileDialogResult> task, Dispatcher dispatcher)
        {
            var frame = new DispatcherFrame();
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => frame.Continue = false)),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            if (!task.IsCompleted) Dispatcher.PushFrame(frame);
            if (!task.IsCompleted) throw new InvalidOperationException("The dispatcher stopped before the native file chooser completed.");
            return task.GetAwaiter().GetResult();
        }

        private void ValidatePortableSelection(PortableFileDialogResult selection, bool multiple)
        {
            if (selection.Paths.Count == 0 || (!multiple && selection.Paths.Count != 1) ||
                selection.Paths.Any(path => string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path)))
                throw new InvalidDataException("The native file chooser returned an invalid local selection.");
            if (selection.FilterIndex is int index && (index < 1 || (!string.IsNullOrEmpty(Filter) && index > Filter.Split('|').Length / 2)))
                throw new InvalidDataException("The native file chooser returned an invalid filter index.");
        }
    }
}
#endif
