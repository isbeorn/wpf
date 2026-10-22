using System.Windows.Input;
using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.Equipment.Equipment;
using NINA.Equipment.Equipment.MyGuider.PHD2.PhdEvents;
using NINA.Profile;

public sealed class GraphSimulation : BaseINPC
{
    private readonly ProfileService profile;
    public GuideStepsHistory GuideStepsHistory { get; }
    public ICommand ClearGraphCommand { get; }

    public GraphSimulation(ProfileService profile)
    {
        this.profile = profile;
        var settings = profile.ActiveProfile.GuiderSettings;
        settings.PHD2HistorySize = 50;
        settings.PHD2GuiderScale = GuiderScaleEnum.PIXELS;
        settings.MaxY = 4;
        settings.PHD2GuideChartShowStarMass = true;
        settings.PHD2GuideChartShowSNR = true;
        settings.GuideChartShowCorrections = true;
        GuideStepsHistory = new GuideStepsHistory(HistorySize, GuiderScale, GuiderMaxY);
        ClearGraphCommand = new RelayCommand(_ => GuideStepsHistory.Clear());
        GuideStepsHistory.Clear();
        Add(1, -0.5, -0.25, 1000, 10, 100, "East", 80, "North");
        Add(2, 0.25, 0.5, 1200, 15, 60, "West", 120, "South");
        Add(3, -1.25, -1, 900, 12, 140, "West", 40, "North");
    }

    public int HistorySize
    {
        get => profile.ActiveProfile.GuiderSettings.PHD2HistorySize;
        set { profile.ActiveProfile.GuiderSettings.PHD2HistorySize = value; GuideStepsHistory.HistorySize = value; RaisePropertyChanged(); }
    }
    public GuiderScaleEnum GuiderScale
    {
        get => profile.ActiveProfile.GuiderSettings.PHD2GuiderScale;
        set { profile.ActiveProfile.GuiderSettings.PHD2GuiderScale = value; GuideStepsHistory.Scale = value; RaisePropertyChanged(); }
    }
    public double GuiderMaxY
    {
        get => profile.ActiveProfile.GuiderSettings.MaxY;
        set { profile.ActiveProfile.GuiderSettings.MaxY = value; GuideStepsHistory.MaxY = value; RaisePropertyChanged(); }
    }

    public void AppendFourth() => Add(4, -0.75, 0.25, 1100, 18, 90, "East", 70, "South");

    private void Add(int frame, double raSetter, double dec, double mass, double snr, int raDuration, string raDirection, int decDuration, string decDirection)
    {
        GuideStepsHistory.AddGuideStep(new PhdEventGuideStep
        {
            Frame = frame, RADistanceRaw = raSetter, DECDistanceRaw = dec,
            StarMass = mass, SNR = snr, RADuration = raDuration, RADirection = raDirection,
            DECDuration = decDuration, DECDirection = decDirection
        });
    }
}
