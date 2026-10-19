using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using NINA.Astrometry;
using NINA.Core.Utility;

internal static class Program {
    private static readonly Dictionary<string, double> values = new();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Value(string name, double number) { Require(double.IsFinite(number), "Nonfinite " + name); values.Add(name, number); }
    private static void PV(string name, RectangularPV pv) {
        Value(name + ".px", pv.Position.X); Value(name + ".py", pv.Position.Y); Value(name + ".pz", pv.Position.Z);
        Value(name + ".vx", pv.Velocity.X); Value(name + ".vy", pv.Velocity.Y); Value(name + ".vz", pv.Velocity.Z);
    }
    private static void Main(string[] args) {
        string report = args[0];
        try {
            var ephemeris = Path.Combine(AppContext.BaseDirectory, "External", "JPLEPH");
            Require(new FileInfo(ephemeris).Length == 13966960, "Wrong JPLEPH length");
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ephemeris))) == "E7AE604FB0BB2C31BEB2D1DC919459D5CC688DDCCBBE20A7E528FF6D3F1BD257", "Wrong JPLEPH");
            CoreUtil.APPLICATIONTEMPPATH = Path.Combine(Path.GetDirectoryName(report)!, "state");
            Directory.CreateDirectory(CoreUtil.APPLICATIONTEMPPATH);
            double d1 = 0, d2 = 0, t1 = 0, t2 = 0, tt1 = 0, tt2 = 0;
            Value("utc.status", SOFA.Dtf2d("UTC", 2026, 7, 28, 20, 15, 0, ref d1, ref d2));
            Value("utc.d1", d1); Value("utc.d2", d2);
            Value("utc.invalid-month", SOFA.Dtf2d("UTC", 2026, 13, 28, 20, 15, 0, ref t1, ref t2));
            Value("tai.status", SOFA.UtcTai(d1, d2, ref t1, ref t2));
            Value("tai.d1", t1); Value("tai.d2", t2);
            Value("tt.status", SOFA.TaiTt(t1, t2, ref tt1, ref tt2));
            Value("tt.d1", tt1); Value("tt.d2", tt2);
            double dat = 0; Value("dat.status", SOFA.Dat(2026, 7, 28, .5, ref dat)); Value("dat.seconds", dat);
            Value("anp.negative", SOFA.Anp(-.7)); Value("anp.positive", SOFA.Anp(8));
            Value("origins", SOFA.Eo06a(tt1, tt2)); Value("separation", SOFA.Seps(.4, -.3, 5.1, .9));
            double ri = 0, di = 0, eo = 0, rc = 0, dc = 0;
            SOFA.CelestialToIntermediate(1.2, -.4, 0, 0, 0, 0, tt1, tt2, ref ri, ref di, ref eo);
            Value("cirs.ra", ri); Value("cirs.dec", di); Value("cirs.eo", eo);
            SOFA.IntermediateToCelestial(ri, di, tt1, tt2, ref rc, ref dc, ref eo);
            Value("icrs.ra", rc); Value("icrs.dec", dc);
            var time = new DateTime(2026, 7, 28, 20, 15, 0, DateTimeKind.Utc);
            var celestial = new Coordinates(Angle.ByHours(22.2), Angle.ByDegree(49.8), Epoch.J2000, time);
            var apparent = celestial.Transform(Epoch.JNOW, time);
            var back = apparent.Transform(Epoch.J2000);
            Value("cached.ra", apparent.RA); Value("cached.dec", apparent.Dec); Value("cached.back.ra", back.RA); Value("cached.back.dec", back.Dec);
            double aob = 0, zob = 0, hob = 0, dob = 0, rob = 0;
            Value("observed.status", SOFA.CelestialToTopocentric(1.2, -.4, 0, 0, 0, 0, d1, d2, 0, .2, .8, 650, 0, 0, 0, 10, .5, .55, ref aob, ref zob, ref hob, ref dob, ref rob, ref eo));
            Value("observed.az", aob); Value("observed.zd", zob); Value("observed.ha", hob); Value("observed.dec", dob); Value("observed.ra", rob);
            Value("observed.reverse-status", SOFA.TopocentricToCelestial("A", aob, zob, d1, d2, 0, .2, .8, 650, 0, 0, 0, 10, .5, .55, ref rc, ref dc));
            Value("observed.reverse-ra", rc); Value("observed.reverse-dec", dc);
            double refa = 0, refb = 0; SOFA.RefractionConstants(1010, 10, .5, .55, ref refa, ref refb);
            Value("refraction.a", refa); Value("refraction.b", refb); Value("obliquity", SOFA.MeanEcclipticObliquity(d1, d2).Radians);
            Value("novas.jd", NOVAS.JulianDate(2026, 7, 28, 20.25));
            foreach (var body in new[] { NOVAS.Body.Sun, NOVAS.Body.Moon, NOVAS.Body.Mars }) {
                var coordinates = NOVAS.PlanetApparentCoordinates(tt1 + tt2, body);
                Value(body + ".ra", coordinates.RA); Value(body + ".dec", coordinates.Dec);
                PV(body + ".barycentric", NOVAS.BodyPositionAndVelocity(tt1 + tt2, body, NOVAS.SolarSystemOrigin.Barycenter));
                PV(body + ".heliocentric", NOVAS.BodyPositionAndVelocity(tt1 + tt2, body, NOVAS.SolarSystemOrigin.SolarCenterOfMass));
            }
            PV("observer", NOVAS.GetPositionOnEarthSurface(tt1 + tt2, 69.184, Angle.ByDegree(47), Angle.ByDegree(11), 650));
            double gst = 0; Value("sidereal.status", NOVAS.SiderealTime(d1, d2, 69.184, NOVAS.GstType.GreenwichApparentSiderealTime, NOVAS.Method.EquinoxBased, NOVAS.Accuracy.Full, ref gst)); Value("sidereal.hours", gst);
            var surface = new NOVAS.OnSurface { Latitude = 47, Longitude = 11, Height = 650, Temperature = 10, Pressure = 1010 };
            Value("novas.refraction", NOVAS.Refract(ref surface, (NOVAS.RefractionOption)2, 65));
            var observer = new NOVAS.Observer { Where = 1, OnSurf = surface };
            var moon = new NOVAS.CelestialObject { Type = 0, Number = 11, Name = "Moon", Star = new NOVAS.CatalogueEntry { StarName = "DUMMY", Catalog = "xxx" } };
            var position = new NOVAS.SkyPosition { RHat = new double[3] };
            Value("place.status", NOVAS.Place(tt1 + tt2, moon, observer, 69.184, NOVAS.CoordinateSystem.EquinoxOfDate, NOVAS.Accuracy.Full, ref position));
            Value("place.ra", position.RA); Value("place.dec", position.Dec); Value("place.distance", position.Dis); Value("place.rv", position.RV);
            var earth = moon; earth.Number = 3; earth.Name = "Earth";
            Require(NOVAS.Place(tt1 + tt2, earth, observer, 69.184, NOVAS.CoordinateSystem.EquinoxOfDate, NOVAS.Accuracy.Full, ref position) != 0, "Invalid Earth place succeeded");
            Require(double.IsNaN(position.RA) && position.RHat.All(double.IsNaN), "Invalid place did not invalidate coordinates");
            var makeEntry = typeof(NOVAS).GetMethod("NOVAS_make_cat_entry", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (int number in new[] { int.MinValue, -1, 0, int.MaxValue }) {
                object?[] parameters = { "ABI-STAR", "HIP", (long)number, 12.25, -44.5, 1.25, -2.5, .125, -34.75, null };
                Require((short)makeEntry.Invoke(null, parameters)! == 0, "Catalogue construction failed");
                var entry = (NOVAS.CatalogueEntry)parameters[9]!;
                Require(entry.StarNumber == number && entry.RA == 12.25 && entry.Dec == -44.5 && entry.ProMoRA == 1.25 && entry.ProMoDec == -2.5 && entry.Parallax == .125 && entry.RadialVelocity == -34.75, "Catalogue width or adjacent numeric field corruption");
                if (OperatingSystem.IsLinux()) Require(entry.StarName == "ABI-STAR" && entry.Catalog == "HIP", "Catalogue ANSI string marshalling failed");
            }
            System.Threading.Tasks.Parallel.For(0, 24, index => {
                var actual = NOVAS.PlanetApparentCoordinates(tt1 + tt2, index % 2 == 0 ? NOVAS.Body.Moon : NOVAS.Body.Mars);
                var expected = index % 2 == 0 ? "Moon" : "Mars";
                Require(Math.Abs(actual.RA - values[expected + ".ra"]) < 1e-10 && Math.Abs(actual.Dec - values[expected + ".dec"]) < 1e-10, "Concurrent NOVAS cache corruption");
            });
            var layout = new Dictionary<string, int>();
            foreach (var type in new[] { typeof(NOVAS.CatalogueEntry), typeof(NOVAS.CelestialObject), typeof(NOVAS.OnSurface), typeof(NOVAS.InSpace), typeof(NOVAS.Observer), typeof(NOVAS.SkyPosition), typeof(SOFA).GetNestedType("Astrom", BindingFlags.NonPublic)! }) {
                layout[type.Name] = Marshal.SizeOf(type);
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) layout[type.Name + "." + field.Name] = checked((int)Marshal.OffsetOf(type, field.Name));
            }
            if (OperatingSystem.IsLinux()) {
                double x = 0, y = 0; short year = 0, month = 0, day = 0;
                foreach (Action call in new Action[] { () => SOFA.Ae2hd(0, 0, 0, ref x, ref y), () => SOFA.Hd2ae(0, 0, 0, ref x, ref y), () => NOVAS.CalDate(d1, ref year, ref month, ref day, ref x) }) {
                    try { call(); throw new InvalidOperationException("Undefined-return compatibility call accepted"); } catch (PlatformNotSupportedException) { }
                }
            }
            Require(NOVAS.Shutdown() == 0, "Ephemeris close failed");
            string[] nativeMaps = OperatingSystem.IsLinux() ? File.ReadAllLines("/proc/self/maps").Where(line => line.Contains("libNOVAS31.so") || line.Contains("libSOFA_2023_10_11.so")).ToArray() : Array.Empty<string>();
            if (OperatingSystem.IsLinux()) Require(nativeMaps.Any(line => line.Contains("libNOVAS31.so")) && nativeMaps.Any(line => line.Contains("libSOFA_2023_10_11.so")), "Genuine native library maps absent");
            File.WriteAllText(report, JsonSerializer.Serialize(new { status = "pass", values, layout, nativeMaps, ephemerisSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ephemeris))), assemblies = new[] { typeof(SOFA).Assembly.Location, typeof(CoreUtil).Assembly.Location } }, new JsonSerializerOptions { WriteIndented = true }));
        } catch (Exception error) { File.WriteAllText(report, JsonSerializer.Serialize(new { status = "fail", error = error.ToString(), values }, new JsonSerializerOptions { WriteIndented = true })); Environment.ExitCode = 1; }
    }
}
