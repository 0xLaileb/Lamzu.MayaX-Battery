using Microsoft.Win32;

namespace MayaXBattery;

internal sealed class TrayApp : ApplicationContext
{
    readonly PollSchedule _schedule = new();
    readonly LowBatteryAlert _alert = new();
    readonly NotifyIcon _tray;
    readonly System.Windows.Forms.Timer _timer;
    readonly System.Windows.Forms.Timer _activationTimer;
    readonly EventWaitHandle _showRequested;
    readonly CompxSource _source = new();
    readonly RuntimeEstimate _estimate;
    readonly SemaphoreSlim _deviceGate = new(1, 1);
    readonly bool _diagnosticsOnly;
    readonly TrayMenu _menu;
    readonly string _settingsPath = LanguageSettings.DefaultPath;
    UiLanguage _language;
    UiStrings _text;
    Icon _currentIcon;
    DetailsWindow _window;
    Reading _reading = Reading.Fail(ReadError.Pending);
    DateTimeOffset? _checked;
    double? _hours;
    bool _busy, _quitting, _exporting;
    volatile bool _themeChanged;
    string _diagnosticResult;

    internal TrayApp(bool showWindow, EventWaitHandle showRequested, bool diagnosticsOnly = false,
        UiLanguage? diagnosticLanguage = null)
    {
        _diagnosticsOnly = diagnosticsOnly;
        _estimate = new RuntimeEstimate(diagnosticsOnly ? null : AppStorage.HistoryPath);
        _showRequested = showRequested;
        var culture = System.Globalization.CultureInfo.CurrentUICulture;
        _language = diagnosticsOnly ? diagnosticLanguage ?? LanguageSettings.DefaultFor(culture)
            : LanguageSettings.Load(_settingsPath, culture);
        if (diagnosticsOnly) _reading = Reading.Fail(ReadError.DiagnosticOnly);
        _text = new UiStrings(_language);
        // The passive diagnostic instance must not change how the ordinary app starts.
        _menu = new TrayMenu(_text, showAutostart: !diagnosticsOnly);
        _menu.Open.Click += (_, _) => ShowDetails();
        _menu.Refresh.Click += async (_, _) => await Refresh();
        _menu.Autostart.Click += (_, _) => ToggleAutostart();
        _menu.English.Click += (_, _) => ChangeLanguage(UiLanguage.English);
        _menu.Russian.Click += (_, _) => ChangeLanguage(UiLanguage.Russian);
        _menu.Diagnostics.Click += async (_, _) => await ExportDiagnostics();
        _menu.Exit.Click += (_, _) => ExitThread();
        // The entry can be changed outside the app, so read it whenever the menu opens.
        _menu.Strip.Opening += (_, _) => _menu.Autostart.Checked = !_diagnosticsOnly && Autostart.IsEnabled();
        _currentIcon = IconFactory.Build("?", Palette.Unknown);
        _tray = new NotifyIcon { Icon = _currentIcon, Visible = true, ContextMenuStrip = _menu.Strip };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowDetails(); };
        UpdateTrayStatus();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        // Start after the UI message loop has installed its synchronization context.
        _timer = new System.Windows.Forms.Timer { Interval = 1 };
        _timer.Tick += async (_, _) =>
        {
            if (showWindow) { showWindow = false; ShowDetails(); }
            if (_diagnosticsOnly) { _timer.Stop(); _menu.Refresh.Enabled = false; }
            else await Refresh();
        };
        _timer.Start();
        _activationTimer = new System.Windows.Forms.Timer { Interval = 150 };
        _activationTimer.Tick += (_, _) =>
        {
            if (_showRequested.WaitOne(0)) ShowDetails();
            if (_themeChanged) { _themeChanged = false; UpdateIcon(); }
        };
        _activationTimer.Start();
    }

    void ShowDetails()
    {
        if (_quitting) return;
        if (_window == null)
        {
            _window = new DetailsWindow(_language);
            System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(_window);
            _window.Closed += (_, _) => _window = null;
            _window.RefreshRequested += async (_, _) => await Refresh();
            _window.DiagnosticsRequested += async (_, _) => await ExportDiagnostics();
            _window.LanguageChanged += (_, language) => ChangeLanguage(language);
        }
        UpdateWindow();
        _window.Show();
        _window.WindowState = System.Windows.WindowState.Normal;
        _window.Activate();
    }

    async Task Refresh()
    {
        if (_busy || _quitting || _exporting || _diagnosticsOnly) return;
        _busy = true;
        _timer.Stop();
        _menu.Refresh.Enabled = false;
        UpdateWindow();
        try
        {
            Reading result;
            await _deviceGate.WaitAsync();
            try { result = await Task.Run(_source.Read); }
            catch (Exception ex) { result = Reading.Fail(ReadError.ExceptionPrefix + ex.GetType().Name); }
            finally { _deviceGate.Release(); }
            if (_quitting) return;
            _reading = result;
            _schedule.Observe(result.Ok);
            _checked = DateTimeOffset.Now;
            _hours = _estimate.Observe(result, _checked.Value);
            UpdateIcon();
            UpdateTrayStatus();
            if (_alert.Observe(result) is ChargeState level) ShowLowBatteryAlert(level);
        }
        finally
        {
            _busy = false;
            if (!_quitting)
            {
                _menu.Refresh.Enabled = !_exporting;
                if (!_exporting) RestartTimer();
                UpdateWindow();
            }
        }
    }

    void RestartTimer()
    {
        _timer.Interval = _schedule.DelaySeconds * 1000;
        _timer.Start();
    }

    void UpdateIcon()
    {
        var icon = IconFactory.Build(_reading.Ok ? _reading.Percent.ToString() : "?",
            Palette.For(_reading.State));
        var old = _currentIcon;
        _tray.Icon = icon;
        _currentIcon = icon;
        old.Dispose();
    }

    // The numerals have a light-taskbar variant. This event can arrive on another thread, so it
    // only raises a flag; the UI timer redraws the icon without waiting for the next poll.
    void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General) _themeChanged = true;
    }

    void ShowLowBatteryAlert(ChargeState level) => _tray.ShowBalloonTip(5000,
        _text.TrayStatus(ShortName(_reading.Device), _reading.Percent, false),
        level == ChargeState.Critical ? _text.CriticalBatteryAlert : _text.LowBatteryAlert,
        ToolTipIcon.Warning);

    void ToggleAutostart()
    {
        if (Autostart.Set(!Autostart.IsEnabled())) return;
        System.Windows.Forms.MessageBox.Show(_text.AutostartFailed, "MayaX-Battery",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    void UpdateWindow() => _window?.UpdateReading(_reading, _checked, _hours, _busy, _schedule.DelaySeconds,
        _estimate.Rate, _estimate.GetNote(_language), _exporting, _diagnosticResult);

    async Task ExportDiagnostics()
    {
        if (_exporting || _quitting) return;
        _exporting = true;
        _timer.Stop();
        _menu.Diagnostics.Enabled = false;
        _menu.Refresh.Enabled = false;
        _diagnosticResult = null;
        UpdateWindow();
        bool acquired = false;
        try
        {
            ShowDetails();
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = _text.DiagnosticSaveTitle,
                Filter = "ZIP (*.zip)|*.zip",
                DefaultExt = "zip", AddExtension = true, OverwritePrompt = true,
                FileName = $"MayaX-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
                RestoreDirectory = true
            };
            if (dialog.ShowDialog(_window) != true || _quitting) return;
            acquired = await _deviceGate.WaitAsync(TimeSpan.FromSeconds(10));
            if (_quitting) return;
            if (!acquired)
            {
                _diagnosticResult = _text.DiagnosticBusy;
                ShowDiagnosticMessage(_diagnosticResult);
                return;
            }
            await Task.Run(() => Diagnostics.Export(dialog.FileName, Diagnostics.Capture()));
            if (_quitting) return;
            _diagnosticResult = _text.DiagnosticSaved;
            ShowDiagnosticMessage($"{_text.DiagnosticSaved}\n\n{dialog.FileName}");
        }
        catch (Exception ex)
        {
            if (!_quitting)
            {
                _diagnosticResult = _text.DiagnosticFailed;
                ShowDiagnosticMessage($"{_text.DiagnosticFailed}\n\n{ex.GetType().Name}", warning: true);
            }
        }
        finally
        {
            if (acquired) _deviceGate.Release();
            _exporting = false;
            if (!_quitting)
            {
                _menu.Diagnostics.Enabled = true;
                _menu.Refresh.Enabled = !_busy && !_diagnosticsOnly;
                if (!_busy && !_diagnosticsOnly) RestartTimer();
                UpdateWindow();
            }
        }
    }

    void ShowDiagnosticMessage(string message, bool warning = false)
    {
        var icon = warning ? System.Windows.MessageBoxImage.Warning : System.Windows.MessageBoxImage.Information;
        if (_window != null)
            System.Windows.MessageBox.Show(_window, message, _text.DiagnosticSaveTitle, System.Windows.MessageBoxButton.OK, icon);
        else System.Windows.MessageBox.Show(message, _text.DiagnosticSaveTitle, System.Windows.MessageBoxButton.OK, icon);
    }

    void ChangeLanguage(UiLanguage language)
    {
        if (_language == language) return;
        _language = language;
        _text = new UiStrings(language);
        if (!_diagnosticsOnly) LanguageSettings.Save(_settingsPath, language);
        _diagnosticResult = null;
        _menu.Apply(_text);
        UpdateTrayStatus();
        _window?.SetLanguage(language);
        UpdateWindow();
    }

    void UpdateTrayStatus()
    {
        var status = _diagnosticsOnly ? _text.DiagnosticMode : _reading.Ok
            ? _text.TrayStatus(ShortName(_reading.Device), _reading.Percent, _reading.Charging, _hours)
            : _checked.HasValue ? _text.StatusUnavailable : _text.PollingMouse;
        // Keep the notification-area tooltip within the classic 63-character limit.
        _tray.Text = status.Length <= 63 ? status : status[..62] + "…";
        _menu.SetStatus(_reading.Ok
            ? new MenuStatus(ShortName(_reading.Device),
                _text.ChargeSummary(_reading.Percent, _reading.Charging, _hours), _reading.Percent, _reading.State)
            : new MenuStatus("MayaX-Battery", status, null, ChargeState.Unknown));
    }

    internal static string ShortName(string product)
    {
        if (string.IsNullOrWhiteSpace(product)) return "LAMZU";
        var words = product.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 1 && words[^1].ToLowerInvariant() is "dongle" or "receiver" or "8k" or "4k" or "2k" or "wireless")
            words.RemoveAt(words.Count - 1);
        return string.Join(' ', words);
    }

    protected override void ExitThreadCore()
    {
        _quitting = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _timer.Stop();
        _timer.Dispose();
        _activationTimer.Stop();
        _activationTimer.Dispose();
        _window?.Close();
        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
        _currentIcon.Dispose();
        base.ExitThreadCore();
    }
}
