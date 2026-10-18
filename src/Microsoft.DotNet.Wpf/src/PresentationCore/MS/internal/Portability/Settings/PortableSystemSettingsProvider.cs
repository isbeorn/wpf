// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;

namespace MS.Internal.Portability.Settings
{
    internal static class PortableSystemSettingsProvider
    {
        private static readonly object s_sync = new object();
        private static PortableSystemSettings s_current = PortableSystemSettings.CreateInitialLightProfile();
        private static bool s_consumed;

        internal static PortableSystemSettings Current
        {
            get
            {
                lock (s_sync)
                {
                    s_consumed = true;
                    return s_current;
                }
            }
        }

        internal static void Register(PortableSystemSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            lock (s_sync)
            {
                if (s_consumed)
                    throw new InvalidOperationException($"Portable settings profile '{s_current.ProfileName}' has already been consumed. Register platform settings before constructing WPF controls or reading system settings.");
                s_current = settings;
            }
        }
    }
}
#endif
