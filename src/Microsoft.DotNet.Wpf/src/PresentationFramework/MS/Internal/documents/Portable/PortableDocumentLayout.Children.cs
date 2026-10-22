// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

namespace MS.Internal.Documents
{
    internal sealed partial class PortableDocumentLayout
    {
        private readonly List<Child> _children = new List<Child>();
        private uint _generation;

        private Child AcquireChild(TextElement container, UIElement element, PortableDocumentLayout previous)
        {
            Child old = null;
            if (previous != null)
                foreach (Child candidate in previous._children)
                    if (ReferenceEquals(candidate.Container, container) && ReferenceEquals(candidate.Element, element)) { old = candidate; break; }
            var inline = container as InlineUIContainer;
            UIElementIsland cached = inline?.PortableCachedIsland;
            UIElementIsland island = inline == null ? old?.Island : ReferenceEquals(cached?.Root, element) ? cached : null;
            bool created = false;
            if (island == null && element != null)
            {
                if (VisualTreeHelper.GetParent(element) != null)
                    throw new InvalidOperationException("Embedded document child already belongs to another visual host.");
                try { island = new UIElementIsland(element); }
                catch (Exception failure)
                {
                    var acquired = VisualTreeHelper.GetParent(element) as UIElementIsland;
                    try { acquired?.Dispose(); }
                    catch (Exception cleanup) { throw new AggregateException("Document island acquisition failed and could not be fully released.", failure, cleanup); }
                    throw;
                }
                created = true;
            }
            var child = new Child(container, element, island, old, cached, created);
            _children.Add(child);
            return child;
        }

        private double FormatChild(BlockUIContainer container, double x, double y, double width, PortableDocumentLayout previous)
        {
            if (container.Child == null) return y;
            Child child = AcquireChild(container, container.Child, previous);
            _navigationLines.Add(new NavigationLine(child));
            MeasureChild(child, width);
            child.Bounds = new Rect(x, y, width, child.MeasuredSize.Height);
            return child.Bounds.Bottom;
        }

        private Child InlineChild(InlineUIContainer container, double width, PortableDocumentLayout previous)
        {
            foreach (Child child in _children)
                if (ReferenceEquals(child.Container, container)) return child;
            Child result = AcquireChild(container, container.Child, previous);
            if (result.Element != null) MeasureChild(result, width);
            return result;
        }

        private static void MeasureChild(Child child, double width)
        {
            child.MeasureWidth = width;
            child.Element.Measure(new Size(width, double.PositiveInfinity));
            child.MeasuredSize = child.Element.DesiredSize;
        }

        internal bool IsCurrent
        {
            get
            {
                if (_document.TextContainer.LayoutGeneration != _generation || _document.StructuralCache.ForceReformat || _document.StructuralCache.DtrList != null)
                    return false;
                foreach (Child child in _children)
                {
                    UIElement current = child.Container is InlineUIContainer inline ? inline.Child : ((BlockUIContainer)child.Container).Child;
                    if (!ReferenceEquals(current, child.Element) || !ReferenceEquals(child.Island?.Root, child.Element)) return false;
                    if (child.Container is InlineUIContainer owner && !ReferenceEquals(owner.PortableCachedIsland, child.CachePublished ? child.Island : child.Cached)) return false;
                    if (child.Element != null && (!child.Element.IsMeasureValid || !child.Element.IsArrangeValid || child.Element.DesiredSize != child.MeasuredSize)) return false;
                }
                return true;
            }
        }

        internal void ArrangeChildren(PortableDocumentLayout previous)
        {
            foreach (Child child in _children)
                if (child.Island != null)
                    child.Island.DoLayout(new Size(child.MeasureWidth, double.PositiveInfinity), child.IsInline, true);
            _document.StructuralCache.DetectInvalidOperation();
            foreach (Child child in _children)
            {
                if (child.Island == null) continue;
                if (child.Previous != null)
                {
                    child.Island.DesiredSizeChanged -= previous.OnChildDesiredSizeChanged;
                    previous.Visual.Children.Remove(child.Island);
                }
                else if (VisualTreeHelper.GetParent(child.Island) != null)
                    throw new InvalidOperationException("Embedded document island already belongs to another page.");
                child.Island.Offset = (Vector)child.Bounds.TopLeft;
                Visual.Children.Add(child.Island);
                child.Island.DesiredSizeChanged += OnChildDesiredSizeChanged;
            }
        }

        internal void PublishInlineCaches()
        {
            // No callbacks occur between identity validation and these cache assignments.
            foreach (Child child in _children)
                if (child.Container is InlineUIContainer inline)
                {
                    if (!inline.PortableExchangeIsland(child.Cached, child.Island))
                        throw new InvalidOperationException("Inline island cache changed during document publication.");
                    child.CachePublished = true;
                }
        }

        internal void CompleteCommit()
        {
            _previous = null;
            foreach (Child child in _children)
            {
                if (child.Previous != null) child.Previous.OwnsIsland = false;
                child.OwnsIsland = !child.IsInline;
                child.Previous = null;
            }
            List<Exception> errors = null;
            foreach (Child child in _children)
                if (child.IsInline && child.Cached != null && !ReferenceEquals(child.Cached, child.Island))
                    try { child.Cached.Dispose(); }
                    catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
            if (errors != null) throw new AggregateException("Document committed but obsolete inline islands could not be fully released.", errors);
        }

        internal void RollbackChildren(PortableDocumentLayout previous, List<Exception> errors)
        {
            foreach (Child child in _children)
            {
                if (child.CachePublished && child.Container is InlineUIContainer inline)
                {
                    if (!inline.PortableExchangeIsland(child.Island, child.Cached))
                        errors.Add(new InvalidOperationException("Inline island cache changed before document rollback."));
                    child.CachePublished = false;
                }
                if (child.Island == null) continue;
                try
                {
                    child.Island.DesiredSizeChanged -= OnChildDesiredSizeChanged;
                    if (ReferenceEquals(VisualTreeHelper.GetParent(child.Island), Visual)) Visual.Children.Remove(child.Island);
                    if (child.Previous != null)
                    {
                        child.Island.Offset = (Vector)child.Previous.Bounds.TopLeft;
                        if (VisualTreeHelper.GetParent(child.Island) == null) previous.Visual.Children.Add(child.Island);
                        child.Island.DesiredSizeChanged -= previous.OnChildDesiredSizeChanged;
                        child.Island.DesiredSizeChanged += previous.OnChildDesiredSizeChanged;
                    }
                }
                catch (Exception error) { errors.Add(error); }
            }
            RestorePreviousChildLayout(errors);
        }

        private void RestorePreviousChildLayout(List<Exception> errors)
        {
            foreach (Child child in _children)
            {
                if (child.Previous == null || child.Island == null) continue;
                try { child.Island.DoLayout(new Size(child.Previous.MeasureWidth, double.PositiveInfinity), child.IsInline, true); }
                catch (Exception error) { errors.Add(error); }
            }
        }

        private Child FindChild(ITextPointer position)
        {
            foreach (Child child in _children)
                if (!child.IsInline && position.CompareTo(child.Container.ContentStart) >= 0 && position.CompareTo(child.Container.ContentEnd) <= 0)
                    return child;
            return null;
        }

        private void OnChildDesiredSizeChanged(object sender, DesiredSizeChangedEventArgs args)
        {
            if (!_document.StructuralCache.IsFormattingInProgress)
                _document.OnChildDesiredSizeChanged(args.Child);
        }

        private void DisposeChildren()
        {
            var errors = new List<Exception>();
            foreach (Child child in _children)
            {
                if (child.Island == null) continue;
                child.Island.DesiredSizeChanged -= OnChildDesiredSizeChanged;
                try { if (ReferenceEquals(VisualTreeHelper.GetParent(child.Island), Visual)) Visual.Children.Remove(child.Island); }
                catch (Exception error) { errors.Add(error); }
                try { if (child.OwnsIsland) child.Island.Dispose(); }
                catch (Exception error) { errors.Add(error); }
            }
            _children.Clear();
            if (errors.Count != 0) throw new AggregateException("Document child resources could not be fully released.", errors);
        }

        private sealed class Child
        {
            internal readonly TextElement Container;
            internal readonly UIElement Element;
            internal readonly UIElementIsland Island, Cached;
            internal Child Previous;
            internal bool OwnsIsland, CachePublished;
            internal double MeasureWidth;
            internal Size MeasuredSize;
            internal Rect Bounds;
            internal bool IsInline => Container is InlineUIContainer;
            internal Child(TextElement container, UIElement element, UIElementIsland island, Child previous, UIElementIsland cached, bool created)
            { Container = container; Element = element; Island = island; Previous = previous; Cached = cached; OwnsIsland = created; }
        }

        private sealed class EmbeddedRun : TextEmbeddedObject
        {
            private readonly Child _child;
            private readonly TextRunProperties _properties;
            private double _baseline;
            internal EmbeddedRun(Child child, TextRunProperties properties) { _child = child; _properties = properties; }
            public override CharacterBufferReference CharacterBufferReference => new CharacterBufferReference(string.Empty, 0);
            public override int Length => 1;
            public override TextRunProperties Properties => _properties;
            public override LineBreakCondition BreakBefore => LineBreakCondition.BreakDesired;
            public override LineBreakCondition BreakAfter => LineBreakCondition.BreakDesired;
            public override bool HasFixedSize => true;
            public override TextEmbeddedObjectMetrics Format(double remainingParagraphWidth)
            {
                double baseline = (double)_child.Element.GetValue(TextBlock.BaselineOffsetProperty);
                _baseline = double.IsNaN(baseline) ? _child.MeasuredSize.Height : baseline;
                return new TextEmbeddedObjectMetrics(_child.MeasuredSize.Width, _child.MeasuredSize.Height, _baseline);
            }
            public override Rect ComputeBoundingBox(bool rightToLeft, bool sideways) => new Rect(0, -_baseline, _child.MeasuredSize.Width, _child.MeasuredSize.Height);
            public override void Draw(DrawingContext context, Point origin, bool rightToLeft, bool sideways)
            {
                _child.Bounds = new Rect(origin.X - (rightToLeft ? _child.MeasuredSize.Width : 0), origin.Y - _baseline,
                    _child.MeasuredSize.Width, _child.MeasuredSize.Height);
            }
        }
    }
}