using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MayaXBattery;

internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    static readonly Color Background = Color.FromArgb(24, 31, 35);
    static readonly Color Hover = Color.FromArgb(38, 58, 58);
    static readonly Color Ink = Color.FromArgb(241, 246, 245);
    static readonly Color Muted = Color.FromArgb(147, 164, 168);
    static readonly Color Accent = Color.FromArgb(143, 226, 201);
    static readonly Color Warning = Color.FromArgb(255, 209, 117);
    static readonly Color Critical = Color.FromArgb(255, 151, 141);
    static readonly Color Separator = Color.FromArgb(46, 58, 62);
    static readonly Color Track = Color.FromArgb(52, 66, 70);

    // Logical pixels. Every label starts in the same column, after the icon or flag.
    const int IconLeft = 12, IconWidth = 22, TextLeft = 42, StatusTextLeft = 56, StatusHeight = 54;

    static readonly Font TextFont = new("Segoe UI", 10);
    static readonly Font StrongFont = new("Segoe UI Semibold", 10);
    static readonly Font SmallFont = new("Segoe UI", 9);
    static readonly Font IconFont = CreateIconFont();

    internal static void Configure(ToolStripDropDownMenu menu, ToolStripMenuItem defaultItem = null)
    {
        menu.Renderer = new TrayMenuRenderer();
        menu.BackColor = Background;
        menu.ForeColor = Ink;
        menu.Font = TextFont;
        menu.Padding = new Padding(5, 6, 5, 6);
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = false;
        // The default item is what a click on the tray icon does.
        if (defaultItem != null) defaultItem.Font = StrongFont;
        foreach (ToolStripItem item in menu.Items)
        {
            if (item is not ToolStripMenuItem entry) continue;
            entry.Padding = entry.Tag is UiLanguage ? new Padding(0, 3, 0, 3)
                : new Padding(0, 6, 0, 6);
            if (entry.HasDropDownItems && entry.DropDown is ToolStripDropDownMenu child)
                Configure(child);
        }
        SizeItems(menu);
        menu.Opening += (_, _) => SizeItems(menu);
        menu.HandleCreated += (_, _) => RoundCorners(menu.Handle);
    }

    static void SizeItems(ToolStripDropDownMenu menu)
    {
        using var graphics = menu.CreateGraphics();
        int width = menu.Items.OfType<ToolStripMenuItem>()
            .Select(item => MeasureItem(graphics, item)).DefaultIfEmpty(0).Max();
        foreach (ToolStripItem item in menu.Items)
        {
            int height = item.Tag is MenuStatus ? Scale(graphics, StatusHeight)
                : item.GetPreferredSize(Size.Empty).Height;
            item.AutoSize = false;
            item.Size = new Size(width, height);
        }
        menu.MinimumSize = new Size(width + menu.Padding.Horizontal, 0);
    }

    static int MeasureItem(Graphics graphics, ToolStripMenuItem item)
    {
        if (item.Tag is MenuStatus status)
            return Math.Max(TextRenderer.MeasureText(status.Title, StrongFont).Width,
                TextRenderer.MeasureText(status.Summary, SmallFont).Width) + Scale(graphics, StatusTextLeft + 20);
        return TextRenderer.MeasureText(item.Text, item.Font).Width
            + Scale(graphics, TextLeft + RightInset(item) + 9);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // Windows 11 draws the rounded border itself; see RoundCorners.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        using var pen = new Pen(Separator);
        var bounds = e.ToolStrip.ClientRectangle;
        e.Graphics.DrawRectangle(pen, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        int inset = Scale(e.Graphics, 3);
        int vertical = Scale(e.Graphics, 1);
        int right = VisibleRight(e.Item) - inset;
        if (right <= inset) return;
        var bounds = new Rectangle(inset, vertical, right - inset, e.Item.Height - vertical * 2);
        int radius = Scale(e.Graphics, 10);
        using var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, radius, radius, 180, 90);
        path.AddArc(bounds.Right - radius, bounds.Top, radius, radius, 270, 90);
        path.AddArc(bounds.Right - radius, bounds.Bottom - radius, radius, radius, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - radius, radius, radius, 90, 90);
        path.CloseFigure();
        using var brush = new SolidBrush(Hover);
        var old = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
        e.Graphics.SmoothingMode = old;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var g = e.Graphics;
        if (e.Item.Tag is MenuStatus status) { DrawStatus(g, status, e.Item); return; }

        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        int left = Scale(g, TextLeft);
        int right = Scale(g, RightInset(e.Item));
        var rectangle = new Rectangle(left, 0, Math.Max(0, e.Item.Width - left - right), e.Item.Height);
        TextRenderer.DrawText(g, e.Text, e.TextFont, rectangle, e.Item.Enabled ? Ink : Muted, flags);

        if (e.Item.Tag is UiLanguage language)
            DrawFlag(g, language, new Rectangle(Scale(g, IconLeft + 1),
                (e.Item.Height - Scale(g, 12)) / 2, Scale(g, 20), Scale(g, 12)));
        else if (e.Item.Tag is MenuItemStyle { Glyph: { } glyph })
            TextRenderer.DrawText(g, glyph, IconFont,
                new Rectangle(Scale(g, IconLeft), 0, Scale(g, IconWidth), e.Item.Height),
                !e.Item.Enabled ? Muted : e.Item.Selected ? Accent : Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

        if (IsCheckable(e.Item) && e.Item is ToolStripMenuItem { Checked: true })
            DrawCheck(g, VisibleRight(e.Item) - Scale(g, 16), e.Item.Height / 2);
    }

    // The header is information, not an unavailable action, so it keeps full contrast.
    static void DrawStatus(Graphics g, MenuStatus status, ToolStripItem item)
    {
        var tint = status.State switch
        {
            ChargeState.Low => Warning,
            ChargeState.Critical => Critical,
            ChargeState.Unknown => Muted,
            _ => Accent
        };
        int size = Scale(g, 28);
        float stroke = Math.Max(2f, Scale(g, 7) / 2f);
        var ring = new RectangleF(Scale(g, 15), (item.Height - size) / 2f, size, size);
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var track = new Pen(Track, stroke))
            g.DrawEllipse(track, ring);
        if (status.Percent is int percent && percent > 0)
        {
            using var pen = new Pen(tint, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(pen, ring, -90, Math.Clamp(percent, 1, 100) * 3.6f);
        }
        g.SmoothingMode = old;

        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        int left = Scale(g, StatusTextLeft);
        int width = Math.Max(0, item.Width - left - Scale(g, 12));
        bool twoLines = !string.IsNullOrEmpty(status.Summary);
        int titleHeight = StrongFont.Height, summaryHeight = twoLines ? SmallFont.Height : 0;
        int top = (item.Height - titleHeight - summaryHeight) / 2;
        TextRenderer.DrawText(g, status.Title, StrongFont, new Rectangle(left, top, width, titleHeight), Ink, flags);
        if (twoLines)
            TextRenderer.DrawText(g, status.Summary, SmallFont,
                new Rectangle(left, top + titleHeight, width, summaryHeight),
                status.State is ChargeState.Low or ChargeState.Critical ? tint : Muted, flags);
    }

    static void DrawCheck(Graphics graphics, int x, int y)
    {
        using var pen = new Pen(Accent, Math.Max(1, Scale(graphics, 2)))
            { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var old = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.DrawLines(pen, [new Point(x - Scale(graphics, 5), y),
            new Point(x - Scale(graphics, 2), y + Scale(graphics, 3)),
            new Point(x + Scale(graphics, 3), y - Scale(graphics, 4))]);
        graphics.SmoothingMode = old;
    }

    static bool IsCheckable(ToolStripItem item) =>
        item.Tag is UiLanguage or MenuItemStyle { Checkable: true };

    static int RightInset(ToolStripItem item) => IsCheckable(item) ? 30
        : item is ToolStripMenuItem { HasDropDownItems: true } ? 25 : 18;

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        if (e.Direction != ArrowDirection.Right) { base.OnRenderArrow(e); return; }
        int x = e.ArrowRectangle.Left + e.ArrowRectangle.Width / 2;
        int y = e.ArrowRectangle.Top + e.ArrowRectangle.Height / 2;
        int size = Scale(e.Graphics, 4);
        using var pen = new Pen(e.Item.Enabled ? Muted : Track, Math.Max(1f, Scale(e.Graphics, 3) / 2f))
            { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var old = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen, [new Point(x - size / 2, y - size),
            new Point(x + size / 2, y), new Point(x - size / 2, y + size)]);
        e.Graphics.SmoothingMode = old;
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Separator);
        int inset = Scale(e.Graphics, 12);
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, inset, y, e.Item.Width - inset, y);
    }

    internal static void DrawFlag(Graphics graphics, UiLanguage language, Rectangle bounds)
    {
        var state = graphics.Save();
        graphics.SetClip(bounds);
        if (language == UiLanguage.Russian)
        {
            using var white = new SolidBrush(Color.White);
            using var blue = new SolidBrush(Color.FromArgb(49, 86, 160));
            using var red = new SolidBrush(Color.FromArgb(216, 60, 72));
            int third = bounds.Height / 3;
            graphics.FillRectangle(white, bounds);
            graphics.FillRectangle(blue, bounds.Left, bounds.Top + third, bounds.Width, third);
            graphics.FillRectangle(red, bounds.Left, bounds.Top + third * 2, bounds.Width, bounds.Height - third * 2);
        }
        else
        {
            using var blue = new SolidBrush(Color.FromArgb(23, 58, 117));
            using var white = new Pen(Color.White, Math.Max(2, bounds.Height / 3f));
            using var red = new Pen(Color.FromArgb(216, 66, 74), Math.Max(1, bounds.Height / 6f));
            graphics.FillRectangle(blue, bounds);
            graphics.DrawLine(white, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            graphics.DrawLine(white, bounds.Left, bounds.Bottom, bounds.Right, bounds.Top);
            graphics.DrawLine(red, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            graphics.DrawLine(red, bounds.Left, bounds.Bottom, bounds.Right, bounds.Top);
            using var whiteFill = new SolidBrush(Color.White);
            using var redFill = new SolidBrush(Color.FromArgb(216, 66, 74));
            graphics.FillRectangle(whiteFill, bounds.Left, bounds.Top + bounds.Height / 3,
                bounds.Width, bounds.Height / 3 + 1);
            graphics.FillRectangle(whiteFill, bounds.Left + bounds.Width / 3, bounds.Top,
                bounds.Width / 3 + 1, bounds.Height);
            graphics.FillRectangle(redFill, bounds.Left, bounds.Top + bounds.Height * 5 / 12,
                bounds.Width, Math.Max(1, bounds.Height / 6));
            graphics.FillRectangle(redFill, bounds.Left + bounds.Width * 5 / 12, bounds.Top,
                Math.Max(1, bounds.Width / 6), bounds.Height);
        }
        graphics.Restore(state);
    }

    // Segoe Fluent Icons ships with Windows 11; Windows 10 has the same code points in MDL2 Assets.
    static Font CreateIconFont()
    {
        var fluent = new Font("Segoe Fluent Icons", 11);
        if (fluent.Name == "Segoe Fluent Icons") return fluent;
        fluent.Dispose();
        return new Font("Segoe MDL2 Assets", 11);
    }

    // Windows 11 rounds the popup and draws its border; older versions ignore both attributes.
    static void RoundCorners(IntPtr handle)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        int round = 2;      // DWMWCP_ROUND
        _ = DwmSetWindowAttribute(handle, 33, ref round, sizeof(int));
        int border = Separator.R | Separator.G << 8 | Separator.B << 16;
        _ = DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    static int Scale(Graphics graphics, int logical) =>
        Math.Max(1, (int)Math.Round(logical * graphics.DpiX / 96f));

    static int VisibleRight(ToolStripItem item) => Math.Min(item.Width,
        item.Owner.ClientSize.Width - item.Owner.Padding.Right - item.Bounds.Left);
}
