using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Windows;
using NINA.Astrometry;
using NINA.Astrometry.RiseAndSet;
using NINA.Core.Utility;
using NINA.Profile;

internal static class NighttimeProgram {
    private static readonly Dictionary<string, double> values = new();
    private static string phase = "owned-inputs";
    private static string defaultConnection = "";
    private static string defaultDataSource = "";
    private static string DatabaseHash(string path) {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(input));
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Value(string key, double number) { Require(double.IsFinite(number), "Nonfinite genuine calculation: " + key); values.Add(key, number); }
    private static void Event(string key, RiseAndSetEvent result) {
        Require(result.Rise.HasValue && result.Set.HasValue, "Genuine event has missing rise/set: " + key);
        Value(key + ".rise", (result.Rise!.Value.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds);
        Value(key + ".set", (result.Set!.Value.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds);
    }
    [STAThread]
    private static void Main(string[] args) {
        string report = Path.GetFullPath(args[0]);
        string state = Path.GetFullPath(args[1]);
        string sourceDatabase = Path.GetFullPath(args[2]);
        ProfileService? profiles = null;
        try {
            Require(!Directory.Exists(state), "Use a fresh owned calculation state.");
            Require(Path.GetFileName(state) == "NINA", "Owned state must retain the original NINA application subdirectory.");
            Directory.CreateDirectory(state);
            string database = Path.Combine(state, "NINA.sqlite");
            File.Copy(sourceDatabase, database);
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(database))) == "4DD072B013F015C3E4460B0E6AFF29538DE2774331293726F81E8DF14E2DDAAC", "Wrong genuine stock-initialized catalog");
            CoreUtil.APPLICATIONTEMPPATH = state;
            ProfileService.PROFILEFOLDER = Path.Combine(state, "Profiles");
            Environment.SetEnvironmentVariable("localappdata", Path.GetDirectoryName(state));
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            phase = "explicit-genuine-database-control";
            var explicitDatabase = new DatabaseInteraction($"Data Source={database};Pooling=False;");
            using (var context = explicitDatabase.GetContext()) {
                context.Database.Initialize(false);
                Require(context.Database.SqlQuery<int>("PRAGMA user_version").Single() == 16, "Original migration version differs");
                Require(context.Database.SqlQuery<long>("SELECT COUNT(*) FROM earthrotationparameters").Single() == 23085, "Original Earth-rotation rows differ");
                Require(context.Database.SqlQuery<long>("SELECT COUNT(*) FROM dsodetail").Single() == 16857, "Original DSO catalog differs");
            }
            var dates = new[] { new DateTime(2024, 10, 1, 20, 15, 0, DateTimeKind.Utc), new DateTime(2024, 10, 2, 20, 15, 0, DateTimeKind.Utc) };
            foreach (var date in dates) Value(date.ToString("yyyyMMdd") + ".explicit-ut1", explicitDatabase.GetUT1_UTC(date, CancellationToken.None).GetAwaiter().GetResult());
            phase = "real-profile-selection";
            var application = new Application();
            profiles = new ProfileService();
            Require(profiles.TryLoad(""), "Original ProfileService.TryLoad failed");
            profiles.ChangeLatitude(47); profiles.ChangeLongitude(11); profiles.ChangeElevation(650);
            Require(profiles.ActiveProfile.GetType() == typeof(Profile), "Actual original profile required");
            defaultConnection = (string)typeof(DatabaseInteraction).GetField("connectionString", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(new DatabaseInteraction())!;
            using (var context = new DatabaseInteraction().GetContext()) {
                context.Database.Connection.Open();
                using var command = context.Database.Connection.CreateCommand();
                command.CommandText = "PRAGMA database_list";
                using var reader = command.ExecuteReader();
                while (reader.Read()) if (reader.GetString(1) == "main") defaultDataSource = reader.GetString(2);
            }
            phase = "original-nighttime-calculator";
            var calculator = new NighttimeCalculator(profiles);
            foreach (var date in dates) {
                string key = date.ToString("yyyyMMdd");
                var night = calculator.Calculate(date);
                night.Ticker.Stop();
                Require(ReferenceEquals(night, calculator.Calculate(date)), "Original night cache did not preserve the result");
                Value(key + ".illumination", night.Illumination ?? double.NaN);
                Value(key + ".phase", (int)night.MoonPhase);
                Event(key + ".astronomical", night.TwilightRiseAndSet);
                Event(key + ".nautical", night.NauticalTwilightRiseAndSet);
                Event(key + ".civil", night.CivilTwilightRiseAndSet);
                Event(key + ".sun", night.SunRiseAndSet);
                Event(key + ".moon", night.MoonRiseAndSet);
                Value(key + ".ut1", new DatabaseInteraction().GetUT1_UTC(date, CancellationToken.None).GetAwaiter().GetResult());
                Value(key + ".delta-t", AstroUtil.DeltaT(date));
                var observer = new ObserverInfo { Latitude = 47, Longitude = 11, Elevation = 650 };
                Value(key + ".sun-altitude", AstroUtil.GetSunAltitude(date, observer));
                Value(key + ".moon-altitude", AstroUtil.GetMoonAltitude(date, observer));
                Require(night.NightDuration.Count > 0 && night.TwilightDuration.Count > 0 && night.CivilTwilightDuration.Count > 0 && night.NauticalTwilightDuration.Count > 0, "Original nighttime plot series empty");
            }
            phase = "stale-original-row-contract";
            var stale = new DateTime(2026, 7, 28, 20, 15, 0, DateTimeKind.Utc);
            Value("2026.explicit-ut1-held", explicitDatabase.GetUT1_UTC(stale, CancellationToken.None).GetAwaiter().GetResult());
            Value("2026.default-ut1-held", new DatabaseInteraction().GetUT1_UTC(stale, CancellationToken.None).GetAwaiter().GetResult());
            Require(Math.Abs(values["2026.default-ut1-held"] - .0117914) < 1e-10, "Original nearest-EOP holding contract differs");
            phase = "actual-database-path-containment";
            Require(string.Equals(Path.GetFullPath(defaultDataSource), database, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "The actual default SQLite path is outside the owned CoreUtil application directory: " + defaultDataSource);
            Require(NOVAS.Shutdown() == 0, "Original NOVAS shutdown failed");
            profiles.Release(); profiles = null;
            File.WriteAllText(report, JsonSerializer.Serialize(new { status = "pass", phase, defaultConnection, defaultDataSource, values, profileType = typeof(Profile).FullName, calculatorType = typeof(NighttimeCalculator).FullName, databaseSha256After = DatabaseHash(database), sourceDatabase, state, staleEop = "Original bundled data ends 2025-03-15. The 2026 sample deliberately observes the original logged nearest-UT1-TAI holding behavior and is not a current IERS accuracy claim.", scope = "Minimal System.Windows.Application resource owner only. No NINA.App, OnStartup, window, view, VM, external network or physical equipment." }, new JsonSerializerOptions { WriteIndented = true }));
            GC.KeepAlive(application);
        } catch (Exception error) {
            File.WriteAllText(report, JsonSerializer.Serialize(new { status = "fail", phase, defaultConnection, defaultDataSource, values, error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            Environment.ExitCode = 1;
        } finally { profiles?.Release(); }
    }
}
