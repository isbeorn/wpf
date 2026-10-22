// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Internal.Portability.Windowing
{
    internal interface IPortableFileDialogHost
    {
        Task<PortableFileDialogResult> ShowFileDialogAsync(PortableFileDialogOptions options, CancellationToken cancellationToken);
    }

    internal sealed record PortableFileDialogFilter(string Name, IReadOnlyList<string> Patterns);

    internal sealed record PortableFileDialogOptions(bool Save, string Title, string FileName, string Directory,
        IReadOnlyList<PortableFileDialogFilter> Filters, int FilterIndex, bool AllowMultiple);

    internal sealed record PortableFileDialogResult(bool Accepted, IReadOnlyList<string> Paths, int? FilterIndex);
}
#endif
