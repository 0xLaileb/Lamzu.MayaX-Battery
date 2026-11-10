namespace MayaXBattery;

/// <summary>How the renderer draws an ordinary menu item: an icon-font glyph and optional traits.</summary>
internal sealed record MenuItemStyle(string Glyph, bool Checkable = false);

/// <summary>Content of the status header: a small gauge, the device name and a summary line.</summary>
internal sealed record MenuStatus(string Title, string Summary, int? Percent, ChargeState State);

/// <summary>
/// The tray context menu. The running app and --menu-preview build it through the same code,
/// so the documentation screenshot cannot drift from the real menu.
/// </summary>
internal sealed class TrayMenu : IDisposable
{
    internal ContextMenuStrip Strip { get; } = new();
    internal ToolStripMenuItem Status { get; } = new() { Enabled = false };
    internal ToolStripMenuItem Open { get; } = new() { Tag = new MenuItemStyle("\uE8A7") };
    internal ToolStripMenuItem Refresh { get; } = new() { Tag = new MenuItemStyle("\uE72C") };
    internal ToolStripMenuItem Autostart { get; } = new() { Tag = new MenuItemStyle("\uE7B5", Checkable: true) };
    internal ToolStripMenuItem Language { get; } = new() { Tag = new MenuItemStyle("\uE774") };
    internal ToolStripMenuItem English { get; } = new("English") { Tag = UiLanguage.English };
    internal ToolStripMenuItem Russian { get; } = new("Русский") { Tag = UiLanguage.Russian };
    internal ToolStripMenuItem Diagnostics { get; } = new() { Tag = new MenuItemStyle("\uE9D9") };
    internal ToolStripMenuItem Exit { get; } = new() { Tag = new MenuItemStyle("\uE7E8") };

    internal TrayMenu(UiStrings text, bool showAutostart = true)
    {
        Language.DropDownItems.AddRange([English, Russian]);
        Strip.Items.AddRange([Status, new ToolStripSeparator(), Open, Refresh, new ToolStripSeparator()]);
        if (showAutostart) Strip.Items.Add(Autostart);
        Strip.Items.AddRange([Language, Diagnostics, new ToolStripSeparator(), Exit]);
        SetStatus(new MenuStatus("MayaX-Battery", "", null, ChargeState.Unknown));
        Apply(text);
        TrayMenuRenderer.Configure(Strip, defaultItem: Open);
    }

    internal void Apply(UiStrings text)
    {
        Open.Text = text.OpenApp;
        Refresh.Text = text.RefreshNow;
        Autostart.Text = text.StartWithWindows;
        Language.Text = text.LanguageMenu;
        Diagnostics.Text = text.ExportDiagnostics;
        Exit.Text = text.Exit;
        English.Checked = text.Language == UiLanguage.English;
        Russian.Checked = text.Language == UiLanguage.Russian;
    }

    internal void SetStatus(MenuStatus status)
    {
        Status.Tag = status;
        // The renderer draws both lines itself. The item text stays short because the menu derives
        // its own width from it; screen readers get the full line.
        Status.Text = status.Title;
        Status.AccessibleName = string.IsNullOrEmpty(status.Summary) ? status.Title
            : $"{status.Title}: {status.Summary}";
    }

    public void Dispose() => Strip.Dispose();
}