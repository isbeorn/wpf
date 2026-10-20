using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NINA.Astrometry;
using NINA.Astrometry.Interfaces;
using NINA.Core.Enum;
using NINA.WPF.Base.SkySurvey;

internal sealed class FixedSurveyFactory : ISkySurveyFactory, ISkySurvey
{
    private readonly BitmapSource image;
    private readonly List<object> observations;
    public int Requests { get; private set; }
    private SkySurveySource selected;

    public FixedSurveyFactory(List<object> observations)
    {
        this.observations = observations;
        byte[] pixels = File.ReadAllBytes(Path.Combine(Program.TestAssetDirectory, "survey.bgra"));
        image = BitmapSource.Create(800, 600, 96, 96, PixelFormats.Bgra32, null, pixels, 3200);
        image.Freeze();
    }

    public ISkySurvey Create(SkySurveySource source)
    {
        if (source is not (SkySurveySource.NASA or SkySurveySource.SKYATLAS))
            throw new InvalidOperationException("FIXTURE_PROVIDER_FORBIDDEN: " + source);
        selected = source;
        return this;
    }

    public Task<SkySurveyImage> GetImage(string name, Coordinates coordinates, double fieldOfView,
        int width, int height, CancellationToken ct, IProgress<int> progress)
    {
        ct.ThrowIfCancellationRequested();
        Requests++;
        observations.Add(new { stage = "fixed-survey-request", Requests, source = selected.ToString(), fieldOfView,
            requestedWidth = width, requestedHeight = height, actualWidth = 800, actualHeight = 600,
            coordinates.RADegrees, coordinates.Dec, thread = Environment.CurrentManagedThreadId, network = false });
        return Task.FromResult(new SkySurveyImage { Source = "owned-fixed-survey", Name = name, Image = image,
            Coordinates = coordinates, FoVHeight = fieldOfView, FoVWidth = fieldOfView * 800 / 600,
            Rotation = 0 });
    }
}

internal sealed class FixedNighttime : INighttimeCalculator
{
    private readonly NighttimeData data = new(new DateTime(2026, 7, 28), new DateTime(2026, 7, 28),
        default, null, null!, null!, null!, null!, null!);
    public int Calls;
    public event EventHandler? OnReferenceDayChanged { add { } remove { } }
    public FixedNighttime() => data.Ticker.Stop();
    public NighttimeData Calculate(DateTime? selectedDate = null) { Interlocked.Increment(ref Calls); return data; }
}

public class ForbiddenEquipment : DispatchProxy
{
    public static T Create<T>() where T : class => DispatchProxy.Create<T, ForbiddenEquipment>();
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        throw new InvalidOperationException("FIXTURE_EQUIPMENT_FORBIDDEN: " + targetMethod?.DeclaringType?.FullName + "." + targetMethod?.Name);
}
