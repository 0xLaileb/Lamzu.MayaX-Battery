using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Globalization;

namespace MayaXBattery;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--diagnostics", StringComparer.Ordinal))
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Use --diagnostics without other arguments.");
                return 2;
            }

            return DiagnosticCommand.Run(DiagnosticCommand.ExecutableDirectory(),
                Diagnostics.Capture, Console.Out, Console.Error);
        }

        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length >= 2 && args[0] == "--ui-preview")
        {
            string state = "normal";
            double scale = 2;
            bool narrow = false;
            var (language, options) = PreviewOptions(args.Skip(2).ToArray());
            foreach (var option in options)
            {
                if (option == "narrow") narrow = true;
                else if (double.TryParse(option, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                    && parsed is >= 1 and <= 2) scale = parsed;
                else state = option;
            }
            WriteUiPreview(args[1], state, scale, narrow, language);
            return 0;
        }

        if (args.Length >= 2 && args[0] == "--menu-preview")
        {
            var (language, options) = PreviewOptions(args.Skip(2).ToArray());
            if (options.Any(option => option != "submenu"))
                throw new ArgumentException("Unknown menu preview option.");
            WriteMenuPreview(args[1], language, options.Contains("submenu"));
            return 0;
        }

        if (args.Length >= 2 && args[0] == "--preview")
        {
            var (language, options) = PreviewOptions(args.Skip(2).ToArray());
            if (options.Length != 0) throw new ArgumentException("Unknown icon preview option.");
            Preview.Write(args[1], language);
            return 0;
        }

        bool diagnosticsOnly = args.Contains("--diagnostics-only");
        UiLanguage? diagnosticLanguage = null;
        if (diagnosticsOnly)
        {
            var (language, options) = PreviewOptions(args.Where(a => a != "--diagnostics-only").ToArray());
            if (options.Length != 0) throw new ArgumentException("Use --diagnostics-only [--language en|ru].");
            diagnosticLanguage = args.Contains("--language") ? language : null;
        }
        bool showWindow = diagnosticsOnly || !args.Contains("--tray") || args.Contains("--show");
        var instanceSuffix = diagnosticsOnly ? "_Diagnostics" : "";
        using var showRequested = new EventWaitHandle(false, EventResetMode.AutoReset,
            @"Local\MayaX-Battery_Show" + instanceSuffix);
        using var single = new Mutex(true, @"Local\MayaX-Battery_Tray" + instanceSuffix, out bool first);
        if (!first)
        {
            if (showWindow) showRequested.Set();
            return 0;
        }

        if (!diagnosticsOnly) AppStorage.MigrateLegacyFiles();
        System.Windows.Forms.Application.Run(new TrayApp(showWindow,
            showRequested, diagnosticsOnly, diagnosticLanguage));
        return 0;
    }

    static (UiLanguage Language, string[] Options) PreviewOptions(string[] options)
    {
        UiLanguage language = UiLanguage.English;
        var remaining = new List<string>();
        for (int index = 0; index < options.Length; index++)
        {
            if (options[index] != "--language") { remaining.Add(options[index]); continue; }
            if (++index >= options.Length || LanguageSettings.Parse(options[index]) is not UiLanguage selected)
                throw new ArgumentException("Use --language en or --language ru.");
            language = selected;
        }
        return (language, remaining.ToArray());
    }

    static void WriteUiPreview(string path, string state, double scale, bool narrow, UiLanguage language)
    {
        var window = new DetailsWindow(language) { ShowInTaskbar = false, Width = narrow ? 360 : 400 };
        var now = new DateTimeOffset(2026, 9, 28, 14, 30, 0, TimeSpan.FromHours(5));
        string observed = new UiStrings(language).HistoryNote(10, 18, true);
        string learning = new UiStrings(language).HistoryNote(0, 0, false);
        switch (state)
        {
            case "normal":
                window.UpdateReading(new Reading(78, false, "LAMZU Maya X", null), now, 43, false,
                    rate: 1.8, estimateNote: observed);
                break;
            case "learning":
                window.UpdateReading(new Reading(78, false, "LAMZU Maya X", null), now, null, false,
                    estimateNote: learning);
                break;
            case "charging":
                window.UpdateReading(new Reading(78, true, "LAMZU Maya X", null), now, null, false);
                break;
            case "low":
                window.UpdateReading(new Reading(12, false, "LAMZU Maya X", null), now, 7, false,
                    rate: 1.8, estimateNote: observed);
                break;
            case "critical":
                window.UpdateReading(new Reading(7, false, "LAMZU Maya X", null), now, 4, false,
                    rate: 1.8, estimateNote: observed);
                break;
            case "full":
                window.UpdateReading(new Reading(100, false, "LAMZU Maya X", null), now, null, false,
                    estimateNote: learning);
                break;
            case "busy":
                window.UpdateReading(new Reading(78, false, "LAMZU Maya X", null), now, 43, true,
                    rate: 1.8, estimateNote: observed);
                break;
            case "error":
                window.UpdateReading(Reading.Fail("мышь не отвечает"), now, null, false);
                break;
            default:
                throw new ArgumentException($"Unknown UI preview state: {state}");
        }

        window.Show();
        window.UpdateLayout();
        var content = (System.Windows.FrameworkElement)window.Content;
        double clientWidth = content.ActualWidth + content.Margin.Left + content.Margin.Right;
        double clientHeight = content.ActualHeight + content.Margin.Top + content.Margin.Bottom;
        int width = (int)Math.Ceiling(clientWidth * scale);
        int height = (int)Math.Ceiling(clientHeight * scale);
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(23, 29, 33)), null,
                new System.Windows.Rect(0, 0, clientWidth, clientHeight));
            dc.DrawRectangle(new VisualBrush(content), null,
                new System.Windows.Rect(content.Margin.Left, content.Margin.Top,
                    content.ActualWidth, content.ActualHeight));
        }
        var target = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        target.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using (var output = File.Create(path)) encoder.Save(output);
        window.Close();
    }

    static void WriteMenuPreview(string path, UiLanguage language, bool showSubmenu)
    {
        var text = new UiStrings(language);
        using var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem(text.OpenApp));
        menu.Items.Add(new ToolStripMenuItem("LAMZU Maya X: 78%") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(text.RefreshNow));
        menu.Items.Add(new ToolStripMenuItem(text.ExportDiagnostics));
        var languageMenu = new ToolStripMenuItem(text.LanguageMenu);
        languageMenu.DropDownItems.Add(new ToolStripMenuItem("EN")
            { Tag = UiLanguage.English, Checked = language == UiLanguage.English });
        languageMenu.DropDownItems.Add(new ToolStripMenuItem("RU")
            { Tag = UiLanguage.Russian, Checked = language == UiLanguage.Russian });
        menu.Items.Add(languageMenu);
        menu.Items.Add(new ToolStripMenuItem(text.Exit));
        TrayMenuRenderer.Configure(menu);
        menu.Show(new Point(60, 60));
        if (showSubmenu) languageMenu.Select();
        else menu.Items[3].Select();
        if (showSubmenu) languageMenu.ShowDropDown();
        System.Windows.Forms.Application.DoEvents();
        int childWidth = showSubmenu ? languageMenu.DropDown.Width : 0;
        using var image = new Bitmap(menu.Width + childWidth, Math.Max(menu.Height,
            showSubmenu ? languageMenu.Bounds.Top + languageMenu.DropDown.Height : 0));
        using (var main = new Bitmap(menu.Width, menu.Height))
        using (var graphics = Graphics.FromImage(image))
        {
            menu.DrawToBitmap(main, new Rectangle(Point.Empty, main.Size));
            graphics.DrawImageUnscaled(main, Point.Empty);
            if (showSubmenu)
            {
                using var child = new Bitmap(languageMenu.DropDown.Width, languageMenu.DropDown.Height);
                languageMenu.DropDown.DrawToBitmap(child, new Rectangle(Point.Empty, child.Size));
                graphics.DrawImageUnscaled(child, menu.Width - 1, languageMenu.Bounds.Top);
            }
        }
        image.Save(path);
        menu.Close();
    }
}


