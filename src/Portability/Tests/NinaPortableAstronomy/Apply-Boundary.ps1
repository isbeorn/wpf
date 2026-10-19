param([Parameter(Mandatory)][string] $Source)
$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false)
function Edit-Text([string] $Relative, [scriptblock] $Edit) {
    $path = Join-Path $Source $Relative
    $text = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
    $new = & $Edit $text
    [IO.File]::WriteAllText($path, $new, $utf8)
}
function Replace-Once([string] $Text, [string] $Before, [string] $After) {
    if ($Text.IndexOf($Before, [StringComparison]::Ordinal) -lt 0 -or $Text.IndexOf($Before, [StringComparison]::Ordinal) -ne $Text.LastIndexOf($Before, [StringComparison]::Ordinal)) { throw "Expected exactly one boundary: $Before" }
    return $Text.Replace($Before, $After)
}
Edit-Text 'NINA.Core/Utility/DllLoader.cs' {
    param($text)
    $text = Replace-Once $text '            lock (lockobj) {' @'
            if (OperatingSystem.IsLinux()) {
                if (!Environment.Is64BitProcess || RuntimeInformation.ProcessArchitecture != Architecture.X64) {
                    throw new PlatformNotSupportedException("The portable native deployment supports Linux x64.");
                }
                var name = Path.GetFileName(dllPath);
                var nativeName = name == "SOFA_2023_10_11.dll" ? "libSOFA_2023_10_11.so"
                    : name == "NOVAS31lib.dll" ? "libNOVAS31.so" : name;
                NativeLibrary.Load(Path.Combine(Path.GetDirectoryName(dllPath), nativeName));
                return;
            }
            lock (lockobj) {
'@
    Replace-Once $text "return FileVersionInfo.GetVersionInfo(path.Replace('/', '\\'));" "return FileVersionInfo.GetVersionInfo(OperatingSystem.IsWindows() ? path.Replace('/', '\\') : path);"
}
Edit-Text 'NINA.Astrometry/NOVAS.cs' {
    param($text)
    $text = Replace-Once $text '                return NOVAS_CalDate(jtd, ref year, ref month, ref day, ref hour);' @'
                if (OperatingSystem.IsLinux()) {
                    throw new PlatformNotSupportedException("NOVAS.CalDate exposes a return value for a void native function and is outside the bounded Linux profile.");
                }
                return NOVAS_CalDate(jtd, ref year, ref month, ref day, ref hour);
'@
    foreach ($name in @('make_cat_entry', 'make_object', 'app_planet', 'geo_posvel_native')) {
        $text = Replace-Once $text "private static extern short NOVAS_$name(" "private static extern short NOVAS_${name}_Windows("
    }
    $text = Replace-Once $text 'private static extern double NOVAS_Refract(' 'private static extern double NOVAS_Refract_Windows('
    $imports = @'
        [DllImport(DLLNAME, EntryPoint = "make_cat_entry", CallingConvention = CallingConvention.Cdecl)]
        private static extern short NOVAS_make_cat_entry_Linux(string name, string catalog, int number, double ra, double dec, double pmra, double pmdec, double parallax, double velocity, out CatalogueEntry star);
        [DllImport(DLLNAME, EntryPoint = "make_object", CallingConvention = CallingConvention.Cdecl)]
        private static extern short NOVAS_make_object_Linux(ObjectType type, short number, string name, ref CatalogueEntry star, out CelestialObject result);
        [DllImport(DLLNAME, EntryPoint = "app_planet", CallingConvention = CallingConvention.Cdecl)]
        private static extern short NOVAS_app_planet_Linux(double date, ref CelestialObject body, Accuracy accuracy, out double ra, out double dec, out double distance);
        [DllImport(DLLNAME, EntryPoint = "geo_posvel", CallingConvention = CallingConvention.Cdecl)]
        private static extern short NOVAS_geo_posvel_native_Linux(double date, double deltaT, Accuracy accuracy, ref Observer observer, [In, Out] double[] position, [In, Out] double[] velocity);
        [DllImport(DLLNAME, EntryPoint = "refract", CallingConvention = CallingConvention.Cdecl)]
        private static extern double NOVAS_Refract_Linux(ref OnSurface location, short option, double zenith);

        private static short NOVAS_make_cat_entry(string name, string catalog, long number, double ra, double dec, double pmra, double pmdec, double parallax, double velocity, out CatalogueEntry star) {
            return OperatingSystem.IsLinux()
                ? NOVAS_make_cat_entry_Linux(name, catalog, checked((int)number), ra, dec, pmra, pmdec, parallax, velocity, out star)
                : NOVAS_make_cat_entry_Windows(name, catalog, number, ra, dec, pmra, pmdec, parallax, velocity, out star);
        }
        private static short NOVAS_make_object(ObjectType type, short number, string name, CatalogueEntry star, out CelestialObject result) {
            return OperatingSystem.IsLinux() ? NOVAS_make_object_Linux(type, number, name, ref star, out result) : NOVAS_make_object_Windows(type, number, name, star, out result);
        }
        private static short NOVAS_app_planet(double date, CelestialObject body, Accuracy accuracy, out double ra, out double dec, out double distance) {
            return OperatingSystem.IsLinux() ? NOVAS_app_planet_Linux(date, ref body, accuracy, out ra, out dec, out distance) : NOVAS_app_planet_Windows(date, body, accuracy, out ra, out dec, out distance);
        }
        private static short NOVAS_geo_posvel_native(double date, double deltaT, Accuracy accuracy, Observer observer, double[] position, double[] velocity) {
            return OperatingSystem.IsLinux() ? NOVAS_geo_posvel_native_Linux(date, deltaT, accuracy, ref observer, position, velocity) : NOVAS_geo_posvel_native_Windows(date, deltaT, accuracy, observer, position, velocity);
        }
        private static double NOVAS_Refract(ref OnSurface location, RefractionOption option, double zenith) {
            return OperatingSystem.IsLinux() ? NOVAS_Refract_Linux(ref location, checked((short)option), zenith) : NOVAS_Refract_Windows(ref location, option, zenith);
        }

'@
    Replace-Once $text '        #region "NOVAS Structs"' ($imports + '        #region "NOVAS Structs"')
}
Edit-Text 'NINA.Astrometry/SOFA.cs' {
    param($text)
    foreach ($name in @('Dtf2d', 'Utctai', 'Taitt', 'Atco13', 'Atoc13')) {
        $pattern = '        (\[DllImport\(DLLNAME, EntryPoint = "[^"\n]+", CallingConvention = CallingConvention.Cdecl\)\]\n)        private static extern short SOFA_' + $name + '\(([^;]+)\);'
        $match = [regex]::Match($text, $pattern)
        if (!$match.Success) { throw "Missing SOFA import $name" }
        $args = $match.Groups[2].Value
        $forward = ($args -split ', ' | ForEach-Object { if ($_ -match '^(ref|out) ') { ($_.Split(' ')[0] + ' ' + ($_.Split(' ')[-1])) } else { $_.Split(' ')[-1] } }) -join ', '
        $replacement = $match.Value.Replace("SOFA_$name(", "SOFA_${name}_Windows(") + "`n        " + $match.Groups[1].Value + "        private static extern int SOFA_${name}_Linux($args);`n        private static short SOFA_$name($args) => OperatingSystem.IsLinux() ? checked((short)SOFA_${name}_Linux($forward)) : SOFA_${name}_Windows($forward);"
        $text = Replace-Once $text $match.Value $replacement
    }
    foreach ($call in @('return SOFA_Ae2hd(azimuth, altitude, latitude, ref hourAngle, ref declination);', 'return SOFA_Hd2ae(hourAngle, declination, latitude, ref azimuth, ref altitude);')) {
        $text = Replace-Once $text $call ('if (OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This compatibility method exposes a return value for a void SOFA function and is outside the bounded Linux profile.");' + "`n            " + $call)
    }
    $text
}
Copy-Item (Join-Path $PSScriptRoot 'PortableNativeImports.cs') (Join-Path $Source 'NINA.Astrometry/PortableNativeImports.cs')
Edit-Text 'NINA.Astrometry/DatabaseInteraction.cs' {
    param($text)
    Replace-Once $text '            : this(string.Format(@"Data Source={0};", Environment.ExpandEnvironmentVariables(@"%localappdata%\NINA\NINA.sqlite"))) {' @'
            : this(OperatingSystem.IsLinux()
                ? $"Data Source={Path.Combine(CoreUtil.APPLICATIONTEMPPATH, "NINA.sqlite")};"
                : string.Format(@"Data Source={0};", Environment.ExpandEnvironmentVariables(@"%localappdata%\NINA\NINA.sqlite"))) {
'@
}
Edit-Text 'NOVAS31/NOVAS31/novas.h' {
    param($text)
    $text = Replace-Once $text '#define _CRT_SECURE_NO_DEPRECATE' "#define _CRT_SECURE_NO_DEPRECATE`n#include <stdint.h>"
    $text = Replace-Once $text '      long int starnumber;' '      int32_t starnumber;'
    Replace-Once $text '                        long int star_num, double ra, double dec,' '                        int32_t star_num, double ra, double dec,'
}
Edit-Text 'NOVAS31/NOVAS31/novas.c' {
    param($text)
    Replace-Once $text '                          long int star_num, double ra, double dec,' '                          int32_t star_num, double ra, double dec,'
}
Edit-Text 'NINA.Core/Utility/DocumentationIndex.cs' {
    param($text)
    $pattern = 'new UriBuilder\(Path\.Combine\(([^()\r\n]+)\)\)'
    if ([regex]::Matches($text, $pattern).Count -ne 13) { throw 'Expected all thirteen original DocumentationIndex URI getters.' }
    [regex]::Replace($text, $pattern, 'new UriBuilder(new Uri(Path.Combine($1), UriKind.Absolute))')
}
