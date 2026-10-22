// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
#nullable enable
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace MS.Internal.Portability.Clipboard
{
    internal interface IPortableClipboard
    {
        Task<string?> GetTextAsync();
        Task SetTextAsync(string text, object original);
        Task ClearAsync();
        Task FlushAsync();
        Task<bool> IsCurrentAsync(object original);
    }

    internal static class PortableClipboard
    {
        [ThreadStatic] private static IPortableClipboard? s_backend;
        [ThreadStatic] private static Dispatcher? s_dispatcher;

        internal static void Register(IPortableClipboard backend)
        {
            ArgumentNullException.ThrowIfNull(backend);
            if (s_backend != null) throw new InvalidOperationException("A portable clipboard backend is already registered on this thread.");
            s_dispatcher = Dispatcher.CurrentDispatcher;
            s_backend = backend;
            s_dispatcher.ShutdownFinished += (_, _) => s_backend = null;
        }

        private static IPortableClipboard Backend
        {
            get
            {
                if (s_dispatcher != null)
                {
                    s_dispatcher.VerifyAccess();
                    if (s_dispatcher.HasShutdownStarted) throw new InvalidOperationException("The clipboard dispatcher is shutting down.");
                    if (s_dispatcher._disableProcessingCount != 0) throw new InvalidOperationException("Clipboard operations require dispatcher processing.");
                }
                return s_backend ?? throw new PlatformNotSupportedException("A native clipboard backend must be registered on the current dispatcher thread.");
            }
        }

        internal static void Clear()
        {
            Wait(Backend.ClearAsync());
        }

        internal static void Flush() => Wait(Backend.FlushAsync());

        internal static IDataObject? GetDataObject()
        {
            string? text = Wait(Backend.GetTextAsync());
            return text == null ? null : new DataObject(DataFormats.UnicodeText, text);
        }

        internal static bool IsCurrent(IDataObject data) => Wait(Backend.IsCurrentAsync(data));

        internal static void SetDataObject(object data)
        {
            _ = Backend;
            IDataObject dataObject = data as IDataObject ?? new DataObject(data);
            string? text = null;
            foreach (string format in dataObject.GetFormats(false))
            {
                if (!IsTextFormat(format)) throw new PlatformNotSupportedException("Portable clipboard export supports Unicode text only. Unsupported format: " + format);
                if (dataObject.GetData(format, false) is not string value) throw new NotSupportedException("Clipboard text formats require string data.");
                if (text != null && text != value) throw new NotSupportedException("Conflicting clipboard text aliases cannot be exported as one native Unicode value.");
                text = value;
            }
            if (text == null) throw new PlatformNotSupportedException("The data object does not contain supported clipboard text.");
            Wait(Backend.SetTextAsync(text, data));
        }

        private static bool IsTextFormat(string format) =>
            string.Equals(format, DataFormats.UnicodeText, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, DataFormats.Text, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, DataFormats.StringFormat, StringComparison.OrdinalIgnoreCase);

        private static void Wait(Task task)
        {
            if (!task.IsCompleted)
            {
                var frame = new DispatcherFrame();
                _ = task.ContinueWith(_ => frame.Continue = false, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
                if (!task.IsCompleted) throw new OperationCanceledException("The clipboard wait ended before the native operation completed.");
            }
            task.GetAwaiter().GetResult();
        }

        private static T Wait<T>(Task<T> task)
        {
            Wait((Task)task);
            return task.GetAwaiter().GetResult();
        }
    }
}
#endif
