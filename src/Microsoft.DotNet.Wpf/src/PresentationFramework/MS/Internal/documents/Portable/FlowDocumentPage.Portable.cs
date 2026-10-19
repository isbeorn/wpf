// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows;
using MS.Internal.Documents;

namespace MS.Internal.PtsHost
{
    internal sealed partial class FlowDocumentPage
    {
        private PortableDocumentLayout _portableLayout;
        private PortableDocumentLayout _pendingLayout;
        internal PortableDocumentLayout PortableLayout => _portableLayout;

        internal void ReleasePortableLayout()
        {
            if (PageVisual != null) PageVisual.Children.Clear();
            _pendingLayout?.Dispose();
            _portableLayout?.Dispose();
            _pendingLayout = _portableLayout = null;
            _textView?.Invalidate();
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
                    try { next?.RestorePreviousChildLayout(failure); }
                    finally { next?.Dispose(); }
                    throw;
                }
            }
        }

        private void ArrangePortable(Size partitionSize)
        {
            _partitionSize = partitionSize;
            if (_pendingLayout != null)
            {
                using (_structuralCache.SetDocumentArrangeContext(this))
                {
                    _structuralCache.DetectInvalidOperation();
                    var old = _portableLayout;
                    try { _pendingLayout.ArrangeChildren(old); }
                    catch
                    {
                        _pendingLayout.Dispose();
                        _pendingLayout = null;
                        throw;
                    }
                    _structuralCache.DetectInvalidOperation();
                    _portableLayout = _pendingLayout;
                    _pendingLayout = null;
                    _textView?.Invalidate();
                    _visualNeedsUpdate = true;
                    UpdateVisual();
                    old?.Dispose();
                    _structuralCache.ClearUpdateInfo(false);
                }
            }
            ValidateTextView();
        }
    }
}
