namespace MayaXBattery;

internal sealed class TrayApp : ApplicationContext
{
    readonly PollSchedule _schedule = new();
    readonly NotifyIcon _tray;
    readonly System.Windows.Forms.Timer _timer;
    readonly System.Windows.Forms.Timer _activationTimer;
    readonly EventWaitHandle _showRequested;
    readonly CompxSource _source = new();
    readonly RuntimeEstimate _estimate;
    readonly SemaphoreSlim _deviceGate = new(1, 1);
    readonly bool _diagnosticsOnly;
    readonly ToolStripMenuItem _status;
    readonly ToolStripMenuItem _refresh;
    readonly ToolStripMenuItem _open;
    readonly ToolStripMenuItem _exit;
    readonly ToolStripMenuItem _diagnostics;
    readonly ToolStripMenuItem _languageMenu;
    readonly ToolStripMenuItem _english;
    readonly ToolStripMenuItem _russian;
    readonly ContextMenuStrip _menu;
    readonly string _settingsPath = LanguageSettings.DefaultPath;
    UiLanguage _language;
    UiStrings _text;
    Icon _currentIcon;
    DetailsWindow _window;
    Reading _reading = Reading.Fail("Ожидание первого опроса");
    DateTimeOffset? _checked;
    double? _hours;
    bool _busy, _quitting, _exporting;
    string _diagnosticResult;

    internal TrayApp(bool showWindow, EventWaitHandle showRequested, bool diagnosticsOnly = false,
        UiLanguage? diagnosticLanguage = null)
    {
        _diagnosticsOnly = diagnosticsOnly;
        _estimate = new RuntimeEstimate(diagnosticsOnly ? null : AppStorage.HistoryPath);
        _showRequested = showRequested;
        _language = diagnosticsOnly ? diagnosticLanguage ?? LanguageSettings.DefaultFor(System.Globalization.CultureInfo.CurrentUICulture)
            : LanguageSettings.Load(_settingsPath, System.Globalization.CultureInfo.CurrentUICulture);
        if (diagnosticsOnly) _reading = Reading.Fail("diagnostic-only");
        _text = new UiStrings(_language);
        _status = new ToolStripMenuItem(diagnosticsOnly ? _text.DiagnosticMode : _text.PollingMouse) { Enabled = false };
        _refresh = new ToolStripMenuItem(_text.RefreshNow, null, async (_, _) => await Refresh());
        _open = new ToolStripMenuItem(_text.OpenApp, null, (_, _) => ShowDetails());
        _exit = new ToolStripMenuItem(_text.Exit, null, (_, _) => ExitThread());
        _diagnostics = new ToolStripMenuItem(_text.ExportDiagnostics, null, async (_, _) => await ExportDiagnostics());
        _english = new ToolStripMenuItem("EN", null, (_, _) => ChangeLanguage(UiLanguage.English)) { Tag = UiLanguage.English, ToolTipText = "English" };
        _russian = new ToolStripMenuItem("RU", null, (_, _) => ChangeLanguage(UiLanguage.Russian)) { Tag = UiLanguage.Russian, ToolTipText = "Русский" };
        _languageMenu = new ToolStripMenuItem(_text.LanguageMenu);
        _languageMenu.DropDownItems.AddRange([_english, _russian]);
        _menu = new ContextMenuStrip();
        _menu.Items.Add(_open);
        _menu.Items.Add(_status);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_refresh);
        _menu.Items.Add(_diagnostics);
        _menu.Items.Add(_languageMenu);
        _menu.Items.Add(_exit);
        TrayMenuRenderer.Configure(_menu);
        UpdateLanguageChecks();
        _currentIcon = IconFactory.Build("?", Palette.Unknown);
        _tray = new NotifyIcon { Icon = _currentIcon, Text = diagnosticsOnly ? _text.DiagnosticMode : _text.TrayPolling,
            Visible = true, ContextMenuStrip = _menu };
        _tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowDetails(); };
        // Start after the UI message loop has installed its synchronization context.
        _timer = new System.Windows.Forms.Timer { Interval = 1 };
        _timer.Tick += async (_, _) =>
        {
            if (showWindow) { showWindow = false; ShowDetails(); }
            if (_diagnosticsOnly) { _timer.Stop(); _refresh.Enabled = false; }
            else await Refresh();
        };
        _timer.Start();
        _activationTimer = new System.Windows.Forms.Timer { Interval = 150 };
        _activationTimer.Tick += (_, _) =>
        {
            if (_showRequested.WaitOne(0)) ShowDetails();
        };
        _activationTimer.Start();
    }

    void ShowDetails()
    {
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
        _refresh.Enabled = false;
        UpdateWindow();
        try
        {
            Reading result;
            await _deviceGate.WaitAsync();
            try { result = await Task.Run(_source.Read); }
            catch (Exception ex) { result = Reading.Fail($"read-error:{ex.GetType().Name}"); }
            finally { _deviceGate.Release(); }
            if (_quitting) return;
            _reading = result;
            _schedule.Observe(result.Ok);
            _checked = DateTimeOffset.Now;
            _hours = _estimate.Observe(result, _checked.Value);
            var palette = !result.Ok ? Palette.Unknown : result.Charging ? Palette.Charging
                : result.Percent > 20 ? Palette.Normal : result.Percent > 10 ? Palette.Low : Palette.Critical;
            var icon = IconFactory.Build(result.Ok ? result.Percent.ToString() : "?", palette);
            var old = _currentIcon;
            _tray.Icon = icon;
            _currentIcon = icon;
            old.Dispose();
            UpdateTrayStatus();
        }
        finally
        {
            _busy = false;
            if (!_quitting)
            {
                _refresh.Enabled = !_exporting;
                if (!_exporting)
                {
                    _timer.Interval = _schedule.DelaySeconds * 1000;
                    _timer.Start();
                }
                UpdateWindow();
            }
        }
    }

    void UpdateWindow()
    {
        if (_window != null)
        {
            _window.UpdateReading(_reading, _checked, _hours, _busy, _schedule.DelaySeconds,
                _estimate.Rate, _estimate.GetNote(_language));
            if (_exporting || _diagnosticsOnly) _window.RefreshButton.IsEnabled = false;
            _window.SetDiagnosticState(_exporting, _diagnosticResult);
        }
    }

    async Task ExportDiagnostics()
    {
        if (_exporting || _quitting) return;
        _exporting = true;
        _timer.Stop();
        _diagnostics.Enabled = false;
        _refresh.Enabled = false;
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
                _diagnostics.Enabled = true;
                _refresh.Enabled = !_busy && !_diagnosticsOnly;
                if (!_busy && !_diagnosticsOnly)
                {
                    _timer.Interval = _schedule.DelaySeconds * 1000;
                    _timer.Start();
                }
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
        _open.Text = _text.OpenApp;
        _refresh.Text = _text.RefreshNow;
        _diagnostics.Text = _text.ExportDiagnostics;
        _languageMenu.Text = _text.LanguageMenu;
        _exit.Text = _text.Exit;
        UpdateLanguageChecks();
        UpdateTrayStatus();
        _window?.SetLanguage(language);
        UpdateWindow();
    }

    void UpdateLanguageChecks()
    {
        _english.Checked = _language == UiLanguage.English;
        _russian.Checked = _language == UiLanguage.Russian;
    }

    void UpdateTrayStatus()
    {
        var status = _diagnosticsOnly ? _text.DiagnosticMode : _reading.Ok
            ? _text.TrayStatus(ShortName(_reading.Device), _reading.Percent, _reading.Charging)
            : _checked.HasValue ? _text.StatusUnavailable : _text.PollingMouse;
        _tray.Text = status.Length <= 63 ? status : status[..60] + "…";
        _status.Text = status;
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

