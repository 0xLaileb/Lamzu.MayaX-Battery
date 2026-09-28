namespace MayaXBattery;

internal sealed class TrayApp : ApplicationContext
{
    readonly PollSchedule _schedule = new();
    readonly NotifyIcon _tray;
    readonly System.Windows.Forms.Timer _timer;
    readonly System.Windows.Forms.Timer _activationTimer;
    readonly EventWaitHandle _showRequested;
    readonly CompxSource _source = new();
    readonly RuntimeEstimate _estimate = new(AppStorage.HistoryPath);
    readonly ToolStripMenuItem _status;
    readonly ToolStripMenuItem _refresh;
    readonly ToolStripMenuItem _open;
    readonly ToolStripMenuItem _exit;
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
    bool _busy, _quitting;

    internal TrayApp(bool showWindow, EventWaitHandle showRequested)
    {
        _showRequested = showRequested;
        _language = LanguageSettings.Load(_settingsPath, System.Globalization.CultureInfo.CurrentUICulture);
        _text = new UiStrings(_language);
        _status = new ToolStripMenuItem(_text.PollingMouse) { Enabled = false };
        _refresh = new ToolStripMenuItem(_text.RefreshNow, null, async (_, _) => await Refresh());
        _open = new ToolStripMenuItem(_text.OpenApp, null, (_, _) => ShowDetails());
        _exit = new ToolStripMenuItem(_text.Exit, null, (_, _) => ExitThread());
        _english = new ToolStripMenuItem("EN", null, (_, _) => ChangeLanguage(UiLanguage.English)) { Tag = UiLanguage.English, ToolTipText = "English" };
        _russian = new ToolStripMenuItem("RU", null, (_, _) => ChangeLanguage(UiLanguage.Russian)) { Tag = UiLanguage.Russian, ToolTipText = "Русский" };
        _languageMenu = new ToolStripMenuItem(_text.LanguageMenu);
        _languageMenu.DropDownItems.AddRange([_english, _russian]);
        _menu = new ContextMenuStrip();
        _menu.Items.Add(_open);
        _menu.Items.Add(_status);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_refresh);
        _menu.Items.Add(_languageMenu);
        _menu.Items.Add(_exit);
        TrayMenuRenderer.Configure(_menu);
        UpdateLanguageChecks();
        _currentIcon = IconFactory.Build("?", Palette.Unknown);
        _tray = new NotifyIcon { Icon = _currentIcon, Text = _text.TrayPolling,
            Visible = true, ContextMenuStrip = _menu };
        _tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowDetails(); };
        // Start after the UI message loop has installed its synchronization context.
        _timer = new System.Windows.Forms.Timer { Interval = 1 };
        _timer.Tick += async (_, _) =>
        {
            if (showWindow) { showWindow = false; ShowDetails(); }
            await Refresh();
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
            _window.LanguageChanged += (_, language) => ChangeLanguage(language);
        }
        UpdateWindow();
        _window.Show();
        _window.WindowState = System.Windows.WindowState.Normal;
        _window.Activate();
    }

    async Task Refresh()
    {
        if (_busy || _quitting) return;
        _busy = true;
        _timer.Stop();
        _refresh.Enabled = false;
        UpdateWindow();
        try
        {
            Reading result;
            try { result = await Task.Run(_source.Read); }
            catch (Exception ex) { result = Reading.Fail($"read-error:{ex.GetType().Name}"); }
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
                _refresh.Enabled = true;
                _timer.Interval = _schedule.DelaySeconds * 1000;
                _timer.Start();
                UpdateWindow();
            }
        }
    }

    void UpdateWindow()
    {
        if (_window != null)
            _window.UpdateReading(_reading, _checked, _hours, _busy, _schedule.DelaySeconds,
                _estimate.Rate, _estimate.GetNote(_language));
    }

    void ChangeLanguage(UiLanguage language)
    {
        if (_language == language) return;
        _language = language;
        _text = new UiStrings(language);
        LanguageSettings.Save(_settingsPath, language);
        _open.Text = _text.OpenApp;
        _refresh.Text = _text.RefreshNow;
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
        var status = _reading.Ok
            ? _text.TrayStatus(ShortName(_reading.Device), _reading.Percent, _reading.Charging)
            : _checked.HasValue ? _text.StatusUnavailable : _text.PollingMouse;
        _tray.Text = status.Length <= 63 ? status : status[..60] + "…";
        _status.Text = status;
    }

    internal static string ShortName(string product)
    {
        if (string.IsNullOrWhiteSpace(product)) return "LAMZU Maya X";
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

