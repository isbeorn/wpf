// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using MS.Internal.Documents;
using MS.Win32;

namespace MS.Internal.Portability.Input
{
    internal sealed class PortableTextEditorComposition : IPortableTextCompositionSink
    {
        private readonly TextEditor _editor;
        private readonly TextBox _box;
        private PortableTextCompositionSession _session;
        private long _generation;
        private uint _operation;
        private bool _active;
        private bool _editing;
        private bool _canceling;
        private bool _cancelRequested;
        private bool _committing;
        private bool _completedByEditor;
        private bool _hasEdited;
        private bool _publishing;
        private bool _visible;
        private string _candidateText;
        private int _candidateCaret;
        private string _originalDocument;
        private ITextPointer _start;
        private ITextPointer _end;
        private string _originalText;
        private int _originalAnchor;
        private int _originalMoving;
        private string _expectedText;
        private int _expectedAnchor;
        private int _expectedMoving;
        private TextParentUndoUnit _undoUnit;
        private CompositionAdorner _adorner;
        private ITextView _adornedView;
        private DispatcherOperation _adornmentUpdate;
        private BindingExpressionBase _binding;

        internal PortableTextEditorComposition(TextEditor editor)
        {
            _editor = editor;
            _box = (TextBox)editor.UiScope;
        }

        public bool CanStart => !_editing && !_canceling && !_publishing && UndoManager.GetUndoManager(_box)?.OpenedUnit == null;
        internal bool IsComposing => _active;
        internal TextEditor Editor => _editor;

        public bool Begin(PortableTextCompositionSession session, long generation, string text, int caret, bool visible)
        {
            if (!_box.IsEnabled || _box.IsReadOnly || !CanStart) return false;
            _session = session;
            _generation = generation;
            unchecked { _operation++; }
            _active = true;
            _completedByEditor = false;
            _committing = false;
            _cancelRequested = false;
            _hasEdited = false;
            _visible = visible;
            _candidateText = string.Empty;
            _candidateCaret = 0;
            _originalDocument = ReadDocument();
            _undoUnit = null;
            _start = _editor.Selection.Start.CreatePointer(LogicalDirection.Backward);
            _end = _editor.Selection.End.CreatePointer(LogicalDirection.Forward);
            _originalText = new TextRange(_start, _end, true).Text;
            _originalAnchor = _editor.Selection.AnchorPosition.Offset;
            _originalMoving = _editor.Selection.MovingPosition.Offset;
            _binding = BindingOperations.GetBindingExpressionBase(_box, TextBox.TextProperty);
            RememberDocument();
            return RaisePreedit(text, caret, true, visible);
        }

        public bool Update(string text, int caret, bool? visible)
        {
            if (_editing || _canceling || _committing || !CheckOwned()) return false;
            return RaisePreedit(text, caret, false, visible);
        }

        public bool SetVisibility(bool visible)
        {
            if (_editing || _canceling || _committing || !CheckOwned()) return false;
            if (_visible == visible) return true;
            uint operation = _operation;
            if (!visible)
            {
                _visible = false;
                RemoveAdorner();
                if (operation != _operation || !CheckOwned()) return false;
                if (!RestoreOriginal()) { Finish(operation); return false; }
                if (_cancelRequested) { _cancelRequested = false; Cancel(); }
            }
            else
            {
                _visible = true;
                var composition = CreateComposition();
                composition.SetCompositionPositions(_start, _end, _candidateText);
                Apply(composition, _candidateCaret);
            }
            if (operation != _operation || !CheckOwned()) return false;
            if (_visible) UpdateAdorner();
            return _active && operation == _operation;
        }

        private bool RaisePreedit(string text, int caret, bool start, bool? visible)
        {
            uint operation = _operation;
            var composition = CreateComposition();
            composition.SetCompositionPositions(_start, _end, text);
            bool handled;
            if (start) handled = TextCompositionManager.StartComposition(composition);
            else
            {
                composition.Stage = TextCompositionStage.Started;
                handled = TextCompositionManager.UpdateComposition(composition);
            }
            if (operation != _operation || !_active) return false;
            if (handled || composition.PendingComplete || !CheckOwned())
            {
                if (_active) Cancel();
                return false;
            }
            _candidateText = text;
            _candidateCaret = caret;
            bool show = visible ?? _visible;
            if (!show) return SetVisibility(false);
            _visible = true;
            Apply(composition, caret);
            if (operation != _operation || !CheckOwned()) return false;
            UpdateAdorner();
            return _active && operation == _operation;
        }

        public bool Commit(string text)
        {
            if (_editing) { _cancelRequested = true; return false; }
            if (_canceling || _committing || !CheckOwned()) return false;
            uint operation = _operation;
            var composition = CreateComposition();
            composition.Stage = TextCompositionStage.Started;
            composition.SetResultPositions(_start, _end, text);
            _committing = true;
            _completedByEditor = false;
            try
            {
                TextCompositionManager.CompleteComposition(composition);
                if (operation != _operation || !_active) return false;
                if (!_completedByEditor || composition.PendingComplete || !OwnsDocument())
                {
                    if (OwnsDocument()) Cancel(); else Finish(operation);
                    return false;
                }
                Finish(operation);
                return true;
            }
            finally { if (operation == _operation) _committing = false; }
        }

        internal void UpdateCompositionText(FrameworkTextComposition composition)
        {
            if (_canceling) return;
            if (!_committing || composition.PendingComplete || !ReferenceEquals(composition.Owner, this) || !CheckOwned()) return;
            uint operation = _operation;
            Apply(composition, composition.Text.Length);
            if (operation == _operation && _active) _completedByEditor = true;
        }

        public void Cancel()
        {
            if (!_active || _canceling) return;
            if (_editing) { _cancelRequested = true; return; }
            uint operation = _operation;
            if (!OwnsDocument() || (_committing && _completedByEditor)) { Finish(operation); return; }
            _canceling = true;
            try
            {
                if (!RestoreOriginal() || operation != _operation || !_active) return;
                var composition = CreateComposition();
                composition.Stage = TextCompositionStage.Started;
                composition.SetResultPositions(_editor.Selection.Start, _editor.Selection.End, string.Empty);
                TextCompositionManager.CompleteComposition(composition);
            }
            finally
            {
                _canceling = false;
                Finish(operation);
            }
        }

        internal void Detach()
        {
            Cancel();
            PortableTextCompositionSession.RegisterEditor(_box, null);
        }

        private void Apply(FrameworkTextComposition composition, int caret)
        {
            uint operation = _operation;
            UndoManager manager = UndoManager.GetUndoManager(_box);
            TextParentUndoUnit unit = null;
            if (manager != null && manager.IsEnabled && manager.OpenedUnit == null)
            {
                if (_undoUnit != null && ReferenceEquals(manager.LastUnit, _undoUnit) && !_undoUnit.Locked)
                    manager.Reopen(_undoUnit);
                else { _undoUnit = new TextParentUndoUnit(_editor.Selection); manager.Open(_undoUnit); }
                unit = _undoUnit;
            }
            UndoCloseAction close = UndoCloseAction.Rollback;
            _editing = true;
            try
            {
                _editor.Selection.BeginChange();
                try
                {
                    bool result = composition._ResultStart != null;
                    var range = new TextRange(result ? composition._ResultStart : composition._CompositionStart,
                        result ? composition._ResultEnd : composition._CompositionEnd, true);
                    string text = result ? _editor._FilterText(composition.Text, range) : composition.CompositionText;
                    _editor.SetText(range, text, InputLanguageManager.Current.CurrentInputLanguage);
                    _hasEdited = true;
                    _start = range.Start.CreatePointer(LogicalDirection.Backward);
                    _end = range.End.CreatePointer(LogicalDirection.Forward);
                    ITextPointer position = _start.CreatePointer(Math.Min(caret, text.Length), LogicalDirection.Forward);
                    _editor.Selection.Select(position, position);
                    RememberDocument();
                }
                finally { _editor.Selection.EndChange(); }
                close = UndoCloseAction.Commit;
            }
            finally
            {
                if (unit != null && ReferenceEquals(manager.OpenedUnit, unit))
                {
                    if (close == UndoCloseAction.Commit)
                        unit.RecordRedoSelectionState(_editor.Selection.AnchorPosition, _editor.Selection.MovingPosition);
                    manager.Close(unit, close);
                }
                _editing = false;
            }
            if (operation != _operation) return;
            if (_cancelRequested) { _cancelRequested = false; Cancel(); }
        }

        private bool RestoreOriginal()
        {
            _editing = true;
            try
            {
                _editor.Selection.BeginChangeNoUndo();
                try
                {
                    UndoManager manager = UndoManager.GetUndoManager(_box);
                    if (_undoUnit != null && manager != null && manager.IsEnabled && manager.OpenedUnit == null &&
                        ReferenceEquals(manager.LastUnit, _undoUnit) && !_undoUnit.Locked)
                    {
                        manager.Reopen(_undoUnit);
                        manager.Close(_undoUnit, UndoCloseAction.Rollback);
                    }
                    else if (_hasEdited) RestoreWithoutUndo();
                    _undoUnit = null;
                    _hasEdited = false;
                }
                finally { _editor.Selection.EndChange(); }
                if (_originalDocument != ReadDocument() || _originalAnchor != _editor.Selection.AnchorPosition.Offset ||
                    _originalMoving != _editor.Selection.MovingPosition.Offset) return false;
                _start = _editor.TextContainer.CreatePointerAtOffset(Math.Min(_originalAnchor, _originalMoving), LogicalDirection.Backward);
                _end = _editor.TextContainer.CreatePointerAtOffset(Math.Max(_originalAnchor, _originalMoving), LogicalDirection.Forward);
                RememberDocument();
                return true;
            }
            finally { _editing = false; }
        }

        private void RestoreWithoutUndo()
        {
            _editor.TextContainer.BeginChangeNoUndo();
            try
            {
                var range = new TextRange(_start, _end, true);
                _editor.SetText(range, _originalText, InputLanguageManager.Current.CurrentInputLanguage);
                _editor.Selection.Select(_editor.TextContainer.CreatePointerAtOffset(_originalAnchor, LogicalDirection.Backward),
                    _editor.TextContainer.CreatePointerAtOffset(_originalMoving, LogicalDirection.Forward));
            }
            finally { _editor.TextContainer.EndChange(); }
        }

        private FrameworkTextComposition CreateComposition() => new FrameworkTextComposition(InputManager.Current, _box, this);
        private string ReadDocument() => new TextRange(_editor.TextContainer.Start, _editor.TextContainer.End, true).Text;
        private void RememberDocument()
        {
            _expectedText = ReadDocument();
            _expectedAnchor = _editor.Selection.AnchorPosition.Offset;
            _expectedMoving = _editor.Selection.MovingPosition.Offset;
        }
        private bool OwnsDocument() =>
            ReferenceEquals(_binding, BindingOperations.GetBindingExpressionBase(_box, TextBox.TextProperty)) &&
            _expectedText == ReadDocument() &&
            _expectedAnchor == _editor.Selection.AnchorPosition.Offset && _expectedMoving == _editor.Selection.MovingPosition.Offset;
        private bool CheckOwned()
        {
            if (!_active) return false;
            if (_session.IsCurrent(_generation, this) && _box.IsEnabled && !_box.IsReadOnly && OwnsDocument()) return true;
            if (OwnsDocument()) Cancel(); else Finish(_operation);
            return false;
        }
        private void Finish(uint operation)
        {
            if (!_active || operation != _operation) return;
            _active = false;
            _undoUnit = null;
            _visible = false;
            _candidateText = null;
            _originalDocument = null;
            _session.Release(_generation, this);
            BindingExpressionBase binding = _binding;
            _binding = null;
            RemoveAdorner();
            if (operation != _operation || _active) return;
            if (binding != null && ReferenceEquals(binding, BindingOperations.GetBindingExpressionBase(_box, TextBox.TextProperty)) &&
                !Helper.IsComposing(_box) && binding.IsUpdateDeferredForComposition)
            {
                binding.IsUpdateDeferredForComposition = false;
                _publishing = true;
                try { binding.Dirty(); }
                finally { _publishing = false; }
            }
        }
        private void RemoveAdorner()
        {
            _adornmentUpdate?.Abort();
            _adornmentUpdate = null;
            if (_adornedView != null) _adornedView.Updated -= OnTextViewUpdated;
            _adornedView = null;
            CompositionAdorner adorner = _adorner;
            _adorner = null;
            adorner?.Uninitialize();
        }

        private void UpdateAdorner()
        {
            uint operation = _operation;
            RemoveAdorner();
            if (!_active || !_visible || operation != _operation) return;
            if (_editor.TextView == null || _start.CompareTo(_end) == 0) return;
            var attribute = new UnsafeNativeMethods.TF_DISPLAYATTRIBUTE { lsStyle = UnsafeNativeMethods.TF_DA_LINESTYLE.TF_LS_SOLID };
            _adorner = new CompositionAdorner(_editor.TextView);
            _adorner.AddAttributeRange(_start, _end, new TextServicesDisplayAttribute(attribute));
            _adorner.Initialize(_editor.TextView);
            _adornedView = _editor.TextView;
            _adornedView.Updated += OnTextViewUpdated;
            _adorner.InvalidateAdorner();
            OnTextViewUpdated(_adornedView, EventArgs.Empty);
        }

        private void OnTextViewUpdated(object sender, EventArgs args)
        {
            if (!_active || !_visible || _editing || _canceling || _adornmentUpdate != null) return;
            uint operation = _operation;
            CompositionAdorner adorner = _adorner;
            _adornmentUpdate = _box.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _adornmentUpdate = null;
                if (!_active || operation != _operation || !ReferenceEquals(adorner, _adorner)) return;
                if (OwnsDocument()) adorner?.InvalidateAdorner();
                else Finish(operation);
            }));
        }
    }
}
#endif
