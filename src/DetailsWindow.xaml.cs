using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

namespace MayaXBattery;

public partial class DetailsWindow : Window
{
    /// <summary>One visual theme per charge state: text tint, ring gradient and glow.</summary>
    sealed record Tone(SolidColorBrush Solid, System.Windows.Media.Brush Ring, System.Windows.Media.Brush Glow,
        SolidColorBrush Pill, WpfColor Color);

    static readonly Tone Mint = CreateTone("#8FE2C9", "#62C9E8");
    static readonly Tone Amber = CreateTone("#FFD175", "#FFA45C");
    static readonly Tone Coral = CreateTone("#FF978D", "#FF5F6D");
    static readonly Tone Grey = CreateTone("#93A4A8", "#93A4A8");
    static readonly SolidColorBrush Ink = Solid(Parse("#F1F6F5"));

    // Ring geometry in the 200 x 200 hero canvas: the centre line of the 12 px track.
    const double RingCenter = 100, RingRadius = 78;

    UiStrings _text;
    Reading _reading;
    DateTimeOffset? _checked;
    double? _hours, _rate;
    bool _hasReading, _busy;
    int _pollSeconds;
    string _estimateNote;
    bool _exporting;
    string _diagnosticResult;

    internal event EventHandler RefreshRequested;
    internal event EventHandler DiagnosticsRequested;
    internal event EventHandler<UiLanguage> LanguageChanged;

    internal DetailsWindow(UiLanguage language = UiLanguage.English)
    {
        InitializeComponent();
        VersionText.Text = "v" + AppInfo.Version;
        ApplyTone(Grey, percent: 0);
        SetLanguage(language);
    }

    internal void SetLanguage(UiLanguage language)
    {
        _text = new UiStrings(language);
        BatteryHeading.Text = _text.BatteryHeading;
        RateHeading.Text = _text.RateHeading;
        RemainingHeading.Text = _text.RemainingHeading;
        AutomationProperties.SetName(EnglishButton, "English");
        AutomationProperties.SetName(RussianButton, "Русский");
        AutomationProperties.SetName(RefreshButton, _text.RefreshAutomation);
        AutomationProperties.SetName(DiagnosticsButton, _text.ExportDiagnostics);
        UpdateDiagnosticState();
        RefreshButton.ToolTip = _text.RefreshHint;
        EnglishButton.Tag = language == UiLanguage.English ? "Selected" : null;
        RussianButton.Tag = language == UiLanguage.Russian ? "Selected" : null;
        if (_hasReading) RenderReading();
        else
        {
            Title = _text.WindowTitle("LAMZU");
            StateText.Text = _text.WaitingForData;
            NoteText.Text = _text.EstimateAfterChange;
            UpdatedText.Text = _text.FirstPollPending;
            RefreshLabel.Text = _text.Refresh;
        }
    }

    internal void UpdateReading(Reading r, DateTimeOffset? time, double? hours, bool busy,
        int pollSeconds = 60, double? rate = null, string estimateNote = null,
        bool exporting = false, string diagnosticResult = null)
    {
        _exporting = exporting;
        _diagnosticResult = diagnosticResult;
        _reading = r;
        _checked = time;
        _hours = hours;
        _busy = busy;
        _pollSeconds = pollSeconds;
        _rate = rate;
        _estimateNote = estimateNote;
        _hasReading = true;
        RenderReading();
        UpdateDiagnosticState();
    }

    void RenderReading()
    {
        var r = _reading;
        string device = r.Ok ? TrayApp.ShortName(r.Device) : "LAMZU";
        Title = _text.WindowTitle(device);
        DeviceText.Text = device;
        PercentValue.Text = r.Ok ? r.Percent.ToString(_text.Culture) : "?";
        PercentSign.Text = r.Ok ? "%" : "";
        AutomationProperties.SetName(PercentText,
            r.Ok ? _text.ChargeAutomation(r.Percent) : _text.UnknownCharge);
        var tone = r.State switch
        {
            ChargeState.Unknown => Grey,
            ChargeState.Low => Amber,
            ChargeState.Critical => Coral,
            _ => Mint
        };
        ApplyTone(tone, r.Ok ? r.Percent : 0);
        // The ring carries the colour; the number is tinted only when it needs attention.
        PercentText.Foreground = r.State is ChargeState.Normal or ChargeState.Charging ? Ink : tone.Solid;
        HeroGlyph.Visibility = r.State == ChargeState.Charging ? Visibility.Visible : Visibility.Hidden;
        StateText.Text = !r.Ok ? _text.DeviceError(r.Error) : r.Charging ? _text.Charging
            : r.Percent <= Reading.LowPercent ? _text.LowCharge : _text.OnBattery;
        RateText.Text = r.Ok && !r.Charging && _rate is > 0 ? _text.Rate(_rate.Value) : "···";
        HoursText.Text = r.Ok && !r.Charging && _hours is >= 0 ? _text.Remaining(_hours.Value) : "···";
        NoteText.Text = !r.Ok ? _text.DiagnosticHelp
            : r.Charging ? _text.EstimateAfterDischarge
            : !string.IsNullOrWhiteSpace(_estimateNote) ? _estimateNote
            : _hours.HasValue ? _text.ApproximateEstimate
            : _text.EstimateAfterChange;
        UpdatedText.Text = r.Error == ReadError.DiagnosticOnly ? _text.DiagnosticMode : _checked.HasValue
            ? _text.CheckedAt(_checked.Value, _pollSeconds)
            : _text.FirstPollPending;
        RefreshButton.IsEnabled = CanRefresh;
        RefreshLabel.Text = _busy ? _text.Checking : _text.Refresh;
    }

    void ApplyTone(Tone tone, int percent)
    {
        ArcPath.Stroke = tone.Ring;
        ArcPath.Data = RingArc(percent);
        ArcGlow.Color = tone.Color;
        Glow.Fill = tone.Glow;
        Glow.Visibility = percent > 0 ? Visibility.Visible : Visibility.Hidden;
        StatePill.Background = tone.Pill;
        StateDot.Fill = tone.Solid;
        StateText.Foreground = tone.Solid;
    }

    /// <summary>Clockwise arc from twelve o'clock covering the given percentage of the ring.</summary>
    static Geometry RingArc(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (percent == 0) return Geometry.Empty;
        var centre = new WpfPoint(RingCenter, RingCenter);
        if (percent == 100) return new EllipseGeometry(centre, RingRadius, RingRadius);
        double angle = percent * 3.6 * Math.PI / 180;
        var end = new WpfPoint(RingCenter + RingRadius * Math.Sin(angle), RingCenter - RingRadius * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new WpfPoint(RingCenter, RingCenter - RingRadius), IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new System.Windows.Size(RingRadius, RingRadius), 0,
            percent > 50, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    void OnEnglishClick(object sender, RoutedEventArgs e) => ChooseLanguage(UiLanguage.English);
    void OnRussianClick(object sender, RoutedEventArgs e) => ChooseLanguage(UiLanguage.Russian);

    void ChooseLanguage(UiLanguage language)
    {
        if (_text.Language == language) return;
        SetLanguage(language);
        LanguageChanged?.Invoke(this, language);
    }

    void OnRefreshClick(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    void OnDiagnosticsClick(object sender, RoutedEventArgs e) =>
        DiagnosticsRequested?.Invoke(this, EventArgs.Empty);

    // The passive diagnostic instance never polls, and an export holds the device.
    bool CanRefresh => !_busy && !_exporting && _reading.Error != ReadError.DiagnosticOnly;

    void UpdateDiagnosticState()
    {
        DiagnosticsButton.IsEnabled = !_exporting;
        DiagnosticsLabel.Text = _exporting ? _text.CollectingDiagnostics : _text.ExportDiagnostics;
        DiagnosticsNote.Text = _exporting ? _text.DiagnosticWaiting : _diagnosticResult ?? _text.DiagnosticPrivacy;
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        else if (e.Key == Key.F5)
        {
            if (CanRefresh) RefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        else return;
        e.Handled = true;
    }

    void OnSourceInitialized(object sender, EventArgs e)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) return;
        var handle = new WindowInteropHelper(this).Handle;
        SetDwm(handle, 20, 1);              // dark title bar
        // Windows 11: blend the title bar into the window. Older versions ignore these.
        SetDwm(handle, 35, 0x0026221A);     // caption colour #1A2226 as 0x00BBGGRR
        SetDwm(handle, 34, 0x003A362A);     // border colour #2A363A
    }

    static void SetDwm(IntPtr handle, int attribute, int value) =>
        _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));

    static WpfColor Parse(string color) => (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(color);

    static SolidColorBrush Solid(WpfColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    static Tone CreateTone(string from, string to)
    {
        var start = Parse(from);
        var ring = new LinearGradientBrush(start, Parse(to), new WpfPoint(0, 0), new WpfPoint(1, 1));
        ring.Freeze();
        var glow = new RadialGradientBrush();
        glow.GradientStops.Add(new GradientStop(WpfColor.FromArgb(0, start.R, start.G, start.B), 0.35));
        glow.GradientStops.Add(new GradientStop(WpfColor.FromArgb(0x2E, start.R, start.G, start.B), 0.62));
        glow.GradientStops.Add(new GradientStop(WpfColor.FromArgb(0, start.R, start.G, start.B), 1));
        glow.Freeze();
        return new Tone(Solid(start), ring, glow, Solid(WpfColor.FromArgb(0x24, start.R, start.G, start.B)), start);
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
