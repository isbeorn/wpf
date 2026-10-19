// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MS.Internal.Documents
{
    internal sealed partial class PortableDocumentLayout
    {
        private readonly List<Child> _children = new List<Child>();

        private double FormatChild(BlockUIContainer container, double x, double y, double width, PortableDocumentLayout previous)
        {
            if (container.Child == null) return y;
            Child old = null;
            if (previous != null)
                foreach (Child candidate in previous._children)
                    if (ReferenceEquals(candidate.Island.Root, container.Child)) { old = candidate; break; }
            UIElementIsland island = old?.Island;
            if (island == null)
            {
                if (VisualTreeHelper.GetParent(container.Child) != null)
                    throw new InvalidOperationException("Embedded document child already belongs to another visual host.");
                island = new UIElementIsland(container.Child);
            }
            var child = new Child(container, island, old, old == null);
            _children.Add(child);
            _navigationLines.Add(new NavigationLine(child));
            container.Child.Measure(new Size(width, double.PositiveInfinity));
            child.Bounds = new Rect(x, y, width, container.Child.DesiredSize.Height);
            return child.Bounds.Bottom;
        }

        internal void ArrangeChildren(PortableDocumentLayout previous)
        {
            try
            {
                foreach (Child child in _children)
                    child.Island.DoLayout(new Size(child.Bounds.Width, double.PositiveInfinity), false, true);
                _document.StructuralCache.DetectInvalidOperation();
                foreach (Child child in _children)
                {
                    if (child.Previous != null)
                    {
                        previous.Visual.Children.Remove(child.Island);
                        child.Island.DesiredSizeChanged -= previous.OnChildDesiredSizeChanged;
                        child.Previous.OwnsIsland = false;
                    }
                    child.OwnsIsland = true;
                    child.Transferred = true;
                    child.Island.Offset = (Vector)child.Bounds.TopLeft;
                    Visual.Children.Add(child.Island);
                    child.Island.DesiredSizeChanged += OnChildDesiredSizeChanged;
                }
            }
            catch (Exception failure)
            {
                foreach (Child child in _children)
                {
                    if (!child.Transferred) continue;
                    Visual.Children.Remove(child.Island);
                    child.Island.DesiredSizeChanged -= OnChildDesiredSizeChanged;
                    if (child.Previous != null)
                    {
                        child.Island.Offset = (Vector)child.Previous.Bounds.TopLeft;
                        previous.Visual.Children.Add(child.Island);
                        child.Island.DesiredSizeChanged += previous.OnChildDesiredSizeChanged;
                        child.Previous.OwnsIsland = true;
                        child.OwnsIsland = false;
                    }
                    child.Transferred = false;
                }
                RestorePreviousChildLayout(failure);
                throw;
            }
            foreach (Child child in _children) child.Previous = null;
        }

        internal void RestorePreviousChildLayout(Exception failure)
        {
            List<Exception> errors = null;
            foreach (Child child in _children)
            {
                if (child.Previous == null) continue;
                try
                {
                    child.Island.DoLayout(new Size(child.Previous.Bounds.Width, double.PositiveInfinity), false, true);
                }
                catch (Exception rollbackFailure)
                {
                    errors ??= new List<Exception> { failure };
                    errors.Add(rollbackFailure);
                }
            }
            if (errors != null) throw new AggregateException("Document child layout failed and its previous layout could not be fully restored.", errors);
        }

        private Child FindChild(ITextPointer position)
        {
            foreach (Child child in _children)
                if (position.CompareTo(child.Container.ContentStart) >= 0 && position.CompareTo(child.Container.ContentEnd) <= 0)
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
            foreach (Child child in _children)
                if (child.OwnsIsland)
                {
                    child.Island.DesiredSizeChanged -= OnChildDesiredSizeChanged;
                    child.Island.Dispose();
                }
            _children.Clear();
        }

        private sealed class Child
        {
            internal readonly BlockUIContainer Container;
            internal readonly UIElementIsland Island;
            internal Child Previous;
            internal bool OwnsIsland;
            internal bool Transferred;
            internal Rect Bounds;
            internal Child(BlockUIContainer container, UIElementIsland island, Child previous, bool owns)
            { Container = container; Island = island; Previous = previous; OwnsIsland = owns; }
        }
    }
}