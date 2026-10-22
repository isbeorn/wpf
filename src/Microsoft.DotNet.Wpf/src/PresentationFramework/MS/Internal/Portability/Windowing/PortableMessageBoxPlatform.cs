// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Threading;
using System.Windows;

namespace MS.Internal.Portability.Windowing
{
    internal interface IPortableMessageBoxExecutor
    {
        MessageBoxResult Execute(Func<MessageBoxResult> showDialog);
    }

    internal static class PortableMessageBoxPlatform
    {
        private static IPortableMessageBoxExecutor s_executor;

        internal static void RegisterExecutor(IPortableMessageBoxExecutor executor)
        {
            ArgumentNullException.ThrowIfNull(executor);
            var previous = Interlocked.CompareExchange(ref s_executor, executor, null);
            if (previous != null && !ReferenceEquals(previous, executor))
                throw new InvalidOperationException("A portable MessageBox executor is already registered.");
        }

        internal static MessageBoxResult Show(Func<MessageBoxResult> showDialog)
        {
            if (PortableWindowPlatform.HasFactory) return showDialog();
            var executor = Volatile.Read(ref s_executor) ?? throw new PlatformNotSupportedException(
                "A portable native host must register MessageBox execution before an unhosted thread shows a dialog.");
            return executor.Execute(showDialog);
        }
    }
}
#endif
