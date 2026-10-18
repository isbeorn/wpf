// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System.Collections;
using System.Globalization;
using System.Windows.Input;

namespace MS.Internal.Portability.Input
{
    internal sealed class PortableInputLanguageSource : IInputLanguageSource
    {
        private readonly CultureInfo _language = CultureInfo.CurrentCulture;
        public void Initialize() { }
        public void Uninitialize() { }
        public CultureInfo CurrentInputLanguage
        {
            get => _language;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                if (!Equals(value, _language))
                    throw new PlatformNotSupportedException("The portable input host does not support switching keyboard layouts.");
            }
        }
        public IEnumerable InputLanguageList => new[] { _language };
    }
}
#endif
