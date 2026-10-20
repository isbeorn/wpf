// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using MS.Internal.Documents;

namespace MS.Internal.PtsHost
{
    internal sealed partial class FlowDocumentPage
    {
        private PortableDocumentLayout _portableLayout;
        private PortableDocumentLayout _pendingLayout;
        private Thickness _committedMargin;
        private Size _committedSize = Size.Empty;
        private Rect _committedContentBox = Rect.Empty;
        internal PortableDocumentLayout PortableLayout => _portableLayout;
        internal bool IsPortableLayoutCurrent => _pendingLayout == null && _portableLayout != null && _portableLayout.IsCurrent;

        internal void ReleasePortableLayout()
        {
            var pending = _pendingLayout;
            var current = _portableLayout;
            _pendingLayout = _portableLayout = null;
            var errors = new List<Exception>();
            try { if (PageVisual != null) PageVisual.Children.Clear(); }
            catch (Exception error) { errors.Add(error); }
            try { pending?.Dispose(); }
            catch (Exception error) { errors.Add(error); }
            try { current?.Dispose(); }
            catch (Exception error) { errors.Add(error); }
            _textView?.Invalidate();
            if (errors.Count != 0) throw new AggregateException("Document page resources could not be fully released.", errors);
        }

        private void RestorePortableGeometry()
        {
            _pageMargin = _committedMargin;
            SetSize(_committedSize);
            SetContentBox(_committedContentBox);
        }

        private void FormatPortable(Size pageSize, Thickness margin)
        {
            _pendingLayout?.Dispose();
            _pendingLayout = null;
            using (_structuralCache.SetDocumentFormatContext(this))
            {
                PortableDocumentLayout next = null;
                try
                {
                    next = PortableDocumentLayout.Format(_structuralCache.FormattingOwner, pageSize.Width, margin, _portableLayout);
                    _structuralCache.DetectInvalidOperation();
                    _pageMargin = margin;
                    _pendingLayout = next;
                    _structuralCache.ClearUpdateInfo(false);
                    SetSize(next.Size);
                    SetContentBox(new Rect(margin.Left, margin.Top,
                        Math.Max(0, next.Size.Width - margin.Left - margin.Right),
                        Math.Max(0, next.Size.Height - margin.Top - margin.Bottom)));
                }
                catch (Exception failure)
                {
                    RestorePortableGeometry();
                    _pendingLayout = null;
                    var errors = new List<Exception> { failure };
                    next?.RollbackChildren(_portableLayout, errors);
                    try { next?.Dispose(); }
                    catch (Exception cleanup) { errors.Add(cleanup); }
                    if (errors.Count > 1) throw new AggregateException("Document formatting failed and rollback could not be fully completed.", errors);
                    throw;
                }
            }
        }

        private void ArrangePortable(Size partitionSize)
        {
            if (_pendingLayout == null) { _partitionSize = partitionSize; return; }
            using (_structuralCache.SetDocumentArrangeContext(this))
            {
                var next = _pendingLayout;
                var old = _portableLayout;
                PageVisual page = PageVisual;
                bool hadPageVisual = page != null;
                Visual oldVisual = page?.Child;
                PageVisual.PortableBackground background = page == null ? default : page.CapturePortableBackground();
                Size oldPartition = _partitionSize;
                bool oldNeedsUpdate = _visualNeedsUpdate;
                Exception failure = null;
                bool stale = false;
                try
                {
                    _structuralCache.DetectInvalidOperation();
                    next.ArrangeChildren(old);
                    _structuralCache.DetectInvalidOperation();
                    if (!next.IsCurrent) stale = true;
                    else
                    {
                        if (page == null) SetVisual(page = new PageVisual(this));
                        _visualNeedsUpdate = false;
                        page.Children.Clear();
                        page.Child = next.Visual;
                        _partitionSize = partitionSize;
                        page.DrawBackground((Brush)_structuralCache.PropertyOwner.GetValue(FlowDocument.BackgroundProperty), new Rect(partitionSize));
                        _structuralCache.DetectInvalidOperation();
                        if (!next.IsCurrent) stale = true;
                        else
                        {
                            next.PublishInlineCaches();
                            _portableLayout = next;
                            _pendingLayout = null;
                            _committedMargin = _pageMargin;
                            _committedSize = Size;
                            _committedContentBox = ContentBox;
                            _textView?.Invalidate();
                        }
                    }
                }
                catch (Exception error) { failure = error; }
                if (failure != null || stale)
                {
                    var errors = new List<Exception>();
                    if (failure != null) errors.Add(failure);
                    _portableLayout = old;
                    _pendingLayout = null;
                    if (page != null)
                        try { page.Children.Clear(); }
                        catch (Exception error) { errors.Add(error); }
                    next.RollbackChildren(old, errors);
                    if (page != null)
                    {
                        try
                        {
                            if (oldVisual != null) page.Child = oldVisual;
                            page.RestorePortableBackground(background);
                        }
                        catch (Exception error) { errors.Add(error); }
                    }
                    _partitionSize = oldPartition;
                    _visualNeedsUpdate = oldNeedsUpdate;
                    if (!hadPageVisual) SetVisual(null);
                    RestorePortableGeometry();
                    try { next.Dispose(); }
                    catch (Exception error) { errors.Add(error); }
                    _structuralCache.InvalidateFormatCache(true);
                    if (errors.Count > 1 || (failure == null && errors.Count != 0))
                        throw new AggregateException("Document page publication failed and rollback could not be fully completed.", errors);
                    if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                    return;
                }

                // Publication is complete. Cleanup and Updated failures cannot undo an observed page.
                var cleanupErrors = new List<Exception>();
                try { next.CompleteCommit(); }
                catch (Exception error) { cleanupErrors.Add(error); }
                try { old?.Dispose(); }
                catch (Exception error) { cleanupErrors.Add(error); }
                if (cleanupErrors.Count != 0) throw new AggregateException("Document committed but previous page resources could not be fully released.", cleanupErrors);
            }
        }
    }
}
