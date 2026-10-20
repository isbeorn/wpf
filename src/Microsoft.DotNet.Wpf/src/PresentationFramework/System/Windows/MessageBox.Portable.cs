// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MS.Internal.Portability.Windowing;

namespace System.Windows
{
    public sealed partial class MessageBox
    {
        private static MessageBoxResult ShowPortable(Window owner, string text, string caption, MessageBoxButton buttons,
            MessageBoxImage icon, MessageBoxResult defaultResult, MessageBoxOptions options)
        {
            ArgumentNullException.ThrowIfNull(owner, "window");
            owner.VerifyContextAndObjectState();
            ValidateArguments(buttons, icon, defaultResult, options);
            return ShowPortableValidated(owner.IsSourceWindowNull ? null : owner, text, caption, buttons, icon, defaultResult, options);
        }

        private static MessageBoxResult ShowPortableValidated(Window owner, string text, string caption, MessageBoxButton buttons,
            MessageBoxImage icon, MessageBoxResult defaultResult, MessageBoxOptions options)
        {
            if ((options & (MessageBoxOptions.ServiceNotification | MessageBoxOptions.DefaultDesktopOnly)) != 0)
            {
                if (owner != null) throw new ArgumentException(SR.CantShowMBServiceWithOwner);
                throw new PlatformNotSupportedException("Portable MessageBox does not support Windows service or default-desktop presentation.");
            }
            return PortableMessageBoxPlatform.Show(() => ShowManaged(owner ?? FindActiveOwner(), text, caption, buttons, icon, defaultResult, options));
        }

        private static Window FindActiveOwner()
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            foreach (PresentationSource source in PresentationSource.CurrentSources)
                if (source.Dispatcher == dispatcher && !source.IsDisposed && source.RootVisual is Window window && window.IsVisible && window.IsActive)
                    return window;
            return null;
        }

        private static MessageBoxResult ShowManaged(Window owner, string text, string caption, MessageBoxButton buttons,
            MessageBoxImage icon, MessageBoxResult defaultResult, MessageBoxOptions options)
        {
            var dialog = new Window(inRbw: false, portableFrameworkOwned: true);
            MessageBoxResult selected = MessageBoxResult.None;
            bool finishing = false;
            bool failed = false;
            try
            {
                dialog.Title = caption ?? SR.PortableMessageBoxDefaultCaption;
                dialog.Owner = owner;
                dialog.ShowInTaskbar = owner == null;
                dialog.ResizeMode = ResizeMode.NoResize;
                dialog.SizeToContent = SizeToContent.WidthAndHeight;
                dialog.MaxWidth = 600;
                dialog.MaxHeight = 500;
                dialog.FontFamily = SystemFonts.MessageFontFamily;
                dialog.FontSize = SystemFonts.MessageFontSize;
                dialog.FontStyle = SystemFonts.MessageFontStyle;
                dialog.FontWeight = SystemFonts.MessageFontWeight;
                MessageBoxResult[] results = Results(buttons);
                var content = new Grid { Margin = new Thickness(16), MinWidth = 280 };
                dialog.Content = content;
                content.RowDefinitions.Add(new RowDefinition());
                content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var body = new DockPanel();
                content.Children.Add(body);
                if (icon != MessageBoxImage.None)
                {
                    var symbol = new TextBlock();
                    DockPanel.SetDock(symbol, Dock.Left); body.Children.Add(symbol);
                    symbol.Text = (int)icon switch { 16 => "\u00d7", 32 => "?", 48 => "!", _ => "i" };
                    symbol.FontSize = 28;
                    symbol.FontWeight = FontWeights.Bold;
                    symbol.Foreground = (int)icon == 16 ? Brushes.Firebrick : Brushes.RoyalBlue;
                    symbol.Margin = new Thickness(0, 0, 16, 0);
                    symbol.IsHitTestVisible = false;
                }
                var scroll = new ScrollViewer { MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                body.Children.Add(scroll);
                var message = new TextBlock();
                scroll.Content = message;
                message.Text = text ?? string.Empty;
                message.TextWrapping = TextWrapping.Wrap;
                message.MaxWidth = 480;
                message.FlowDirection = (options & MessageBoxOptions.RtlReading) != 0 ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                message.TextAlignment = (options & MessageBoxOptions.RightAlign) != 0 ? TextAlignment.Right : TextAlignment.Left;
                var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
                Grid.SetRow(actions, 1); content.Children.Add(actions);
                int defaultIndex = DefaultResultToButtonNumber(defaultResult, buttons) >> 8;
                Button defaultButton = null;
                for (int index = 0; index < results.Length; index++)
                {
                    MessageBoxResult result = results[index];
                    var button = new Button();
                    actions.Children.Add(button);
                    button.Content = Label(result);
                    button.MinWidth = 80;
                    button.MinHeight = 28;
                    button.Margin = new Thickness(4, 0, 0, 0);
                    button.Padding = new Thickness(10, 2, 10, 2);
                    button.IsDefault = index == defaultIndex;
                    button.IsCancel = result == MessageBoxResult.Cancel || buttons == MessageBoxButton.OK;
                    button.Click += (_, _) => { selected = result; dialog.DialogResult = true; };
                    if (button.IsDefault) defaultButton = button;
                }
                dialog.Loaded += (_, _) => defaultButton?.Focus();
                dialog.Closing += (_, args) =>
                {
                    if (finishing || selected != MessageBoxResult.None || !dialog.IsVisible) return;
                    if (buttons == MessageBoxButton.OK) selected = MessageBoxResult.OK;
                    else if (Array.IndexOf(results, MessageBoxResult.Cancel) >= 0) selected = MessageBoxResult.Cancel;
                    else args.Cancel = true;
                };
                dialog.PortableShowMessageBox();
                return selected == MessageBoxResult.None ? MessageBoxResult.No : selected;
            }
            catch { failed = true; throw; }
            finally
            {
                finishing = true;
                try { dialog.Close(); }
                catch when (failed) { }
            }
        }

        private static MessageBoxResult[] Results(MessageBoxButton buttons) => buttons switch
        {
            MessageBoxButton.OK => new[] { MessageBoxResult.OK },
            MessageBoxButton.OKCancel => new[] { MessageBoxResult.OK, MessageBoxResult.Cancel },
            MessageBoxButton.AbortRetryIgnore => new[] { MessageBoxResult.Abort, MessageBoxResult.Retry, MessageBoxResult.Ignore },
            MessageBoxButton.YesNoCancel => new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel },
            MessageBoxButton.YesNo => new[] { MessageBoxResult.Yes, MessageBoxResult.No },
            MessageBoxButton.RetryCancel => new[] { MessageBoxResult.Retry, MessageBoxResult.Cancel },
            MessageBoxButton.CancelTryContinue => new[] { MessageBoxResult.Cancel, MessageBoxResult.TryAgain, MessageBoxResult.Continue },
            _ => throw new InvalidEnumArgumentException(nameof(buttons), (int)buttons, typeof(MessageBoxButton))
        };

        private static string Label(MessageBoxResult result) => result switch
        {
            MessageBoxResult.OK => SR.PortableMessageBoxOK, MessageBoxResult.Cancel => SR.PortableMessageBoxCancel,
            MessageBoxResult.Abort => SR.PortableMessageBoxAbort, MessageBoxResult.Retry => SR.PortableMessageBoxRetry,
            MessageBoxResult.Ignore => SR.PortableMessageBoxIgnore, MessageBoxResult.Yes => SR.PortableMessageBoxYes,
            MessageBoxResult.No => SR.PortableMessageBoxNo, MessageBoxResult.TryAgain => SR.PortableMessageBoxTryAgain,
            MessageBoxResult.Continue => SR.PortableMessageBoxContinue,
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }
}
#endif
