using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MayaXBattery;

public partial class DetailsWindow : Window
{
    static readonly SolidColorBrush Accent = Brush("#8FE2C9");
    static readonly SolidColorBrush Warning = Brush("#FFD175");
    static readonly SolidColorBrush Critical = Brush("#FF978D");
    static readonly SolidColorBrush Muted = Brush("#A4B3B6");
    static readonly SolidColorBrush SelectedLanguage = Brush("#8FE2C9");
    static readonly SolidColorBrush UnselectedLanguage = Brush("#344B48");

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
        SetLanguage(language);
    }

    internal void SetLanguage(UiLanguage language)
    {
        _text = new UiStrings(language);
        BatteryHeading.Text = _text.BatteryHeading;
        RateHeading.Text = _text.RateHeading;
        RemainingHeading.Text = _text.RemainingHeading;
        System.Windows.Automation.AutomationProperties.SetName(MouseIconView, _text.MouseIconAutomation);
        System.Windows.Automation.AutomationProperties.SetName(EnglishButton, "English");
        System.Windows.Automation.AutomationProperties.SetName(RussianButton, "Русский");
        System.Windows.Automation.AutomationProperties.SetName(RefreshButton, _text.RefreshAutomation);
        System.Windows.Automation.AutomationProperties.SetName(DiagnosticsButton, _text.ExportDiagnostics);
        UpdateDiagnosticState();
        EnglishButton.BorderBrush = language == UiLanguage.English ? SelectedLanguage : UnselectedLanguage;
        RussianButton.BorderBrush = language == UiLanguage.Russian ? SelectedLanguage : UnselectedLanguage;
        if (_hasReading) RenderReading();
        else
        {
            StateText.Text = _text.WaitingForData;
            NoteText.Text = _text.EstimateAfterChange;
            UpdatedText.Text = _text.FirstPollPending;
            RefreshButton.Content = _text.Refresh;
        }
    }

    internal void UpdateReading(Reading r, DateTimeOffset? time, double? hours, bool busy,
        int pollSeconds = 60, double? rate = null, string estimateNote = null)
    {
        _reading = r;
        _checked = time;
        _hours = hours;
        _busy = busy;
        _pollSeconds = pollSeconds;
        _rate = rate;
        _estimateNote = estimateNote;
        _hasReading = true;
        RenderReading();
    }

    void RenderReading()
    {
        var r = _reading;
        Title = $"{(r.Ok ? TrayApp.ShortName(r.Device) : "LAMZU")} · Battery";
        PercentText.Text = r.Ok ? $"{r.Percent}%" : "?";
        System.Windows.Automation.AutomationProperties.SetName(PercentText,
            r.Ok ? _text.ChargeAutomation(r.Percent) : _text.UnknownCharge);
        var tint = !r.Ok ? Muted : r.Charging ? Accent : r.Percent <= 10 ? Critical
            : r.Percent <= 20 ? Warning : Accent;
        PercentText.Foreground = tint;
        ChargeFill.Background = tint;
        int percent = Math.Clamp(r.Ok ? r.Percent : 0, 0, 100);
        ChargeColumn.Width = new GridLength(percent, GridUnitType.Star);
        RemainderColumn.Width = new GridLength(100 - percent, GridUnitType.Star);
        StateText.Text = !r.Ok ? _text.DeviceError(r.Error) : r.Charging ? _text.Charging
            : r.Percent <= 20 ? _text.LowCharge : _text.OnBattery;
        RateText.Text = r.Ok && !r.Charging && _rate is > 0 ? _text.Rate(_rate.Value) : "···";
        HoursText.Text = r.Ok && !r.Charging && _hours is >= 0
            ? _hours.Value < 1 ? _text.RemainingMinutes(_hours.Value * 60) : _text.RemainingHours(_hours.Value)
            : "···";
        NoteText.Text = !r.Ok ? _text.DiagnosticHelp
            : r.Charging ? _text.EstimateAfterDischarge
            : !string.IsNullOrWhiteSpace(_estimateNote) ? _estimateNote
            : _hours.HasValue ? _text.ApproximateEstimate
            : _text.EstimateAfterChange;
        UpdatedText.Text = r.Error == "diagnostic-only" ? _text.DiagnosticMode : _checked.HasValue
            ? _text.CheckedAt(_checked.Value, _pollSeconds)
            : _text.FirstPollPending;
        RefreshButton.IsEnabled = !_busy;
        RefreshButton.Content = _busy ? _text.Checking : _text.Refresh;
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

    internal void SetDiagnosticState(bool exporting, string result = null)
    {
        _exporting = exporting;
        _diagnosticResult = result;
        UpdateDiagnosticState();
    }

    void UpdateDiagnosticState()
    {
        DiagnosticsButton.IsEnabled = !_exporting;
        DiagnosticsButton.Content = _exporting ? _text.CollectingDiagnostics : _text.ExportDiagnostics;
        DiagnosticsNote.Text = _exporting ? _text.DiagnosticWaiting : _diagnosticResult ?? _text.DiagnosticPrivacy;
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Close();
        e.Handled = true;
    }

    void OnSourceInitialized(object sender, EventArgs e)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) return;
        int dark = 1;
        var handle = new WindowInteropHelper(this).Handle;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
