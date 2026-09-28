using System.Drawing.Drawing2D;

namespace MayaXBattery;

internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    static readonly Color Background = Color.FromArgb(28, 36, 40);
    static readonly Color Hover = Color.FromArgb(45, 75, 72);
    static readonly Color Ink = Color.FromArgb(237, 245, 242);
    static readonly Color Muted = Color.FromArgb(156, 178, 178);
    static readonly Color Accent = Color.FromArgb(143, 226, 201);
    static readonly Color Separator = Color.FromArgb(64, 78, 80);

    internal static void Configure(ToolStripDropDownMenu menu)
    {
        menu.Renderer = new TrayMenuRenderer();
        menu.BackColor = Background;
        menu.ForeColor = Ink;
        menu.Font = new Font("Segoe UI", 10);
        menu.Padding = new Padding(5, 6, 5, 6);
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = false;
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
    }

    static void SizeItems(ToolStripDropDownMenu menu)
    {
        using var graphics = menu.CreateGraphics();
        int width = menu.Items.OfType<ToolStripMenuItem>()
            .Select(item => TextRenderer.MeasureText(item.Text, menu.Font).Width
                + Scale(graphics, item.Tag is UiLanguage ? 65 : 41))
            .DefaultIfEmpty(0).Max();
        foreach (ToolStripItem item in menu.Items)
        {
            var preferred = item.GetPreferredSize(Size.Empty);
            item.AutoSize = false;
            item.Size = new Size(width, preferred.Height);
        }
        menu.MinimumSize = new Size(width + menu.Padding.Horizontal, 0);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Separator);
        var bounds = e.ToolStrip.ClientRectangle;
        e.Graphics.DrawRectangle(pen, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        int inset = Scale(e.Graphics, 4);
        int vertical = Scale(e.Graphics, 1);
        int left = inset;
        int right = VisibleRight(e.Item) - inset;
        if (right <= left) return;
        var bounds = new Rectangle(left, vertical, right - left, e.Item.Height - vertical * 2);
        int radius = Scale(e.Graphics, 8);
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
        bool languageItem = e.Item.Tag is UiLanguage;
        int left = Scale(e.Graphics, languageItem ? 33 : 16);
        int right = Scale(e.Graphics, languageItem ? 22
            : e.Item is ToolStripMenuItem { HasDropDownItems: true } ? 25 : 16);
        var rectangle = new Rectangle(left, 0, Math.Max(0, e.Item.Width - left - right), e.Item.Height);
        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, rectangle,
            e.Item.Enabled ? Ink : Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if (e.Item.Tag is UiLanguage language)
        {
            DrawFlag(e.Graphics, language, new Rectangle(Scale(e.Graphics, 8),
                (e.Item.Height - Scale(e.Graphics, 12)) / 2, Scale(e.Graphics, 20), Scale(e.Graphics, 12)));
            if (e.Item is ToolStripMenuItem { Checked: true })
            {
                using var pen = new Pen(Accent, Math.Max(1, Scale(e.Graphics, 2)));
                int x = VisibleRight(e.Item) - Scale(e.Graphics, 14);
                int y = e.Item.Height / 2;
                e.Graphics.DrawLines(pen, [new Point(x - Scale(e.Graphics, 5), y),
                    new Point(x - Scale(e.Graphics, 2), y + Scale(e.Graphics, 3)),
                    new Point(x + Scale(e.Graphics, 3), y - Scale(e.Graphics, 4))]);
            }
        }
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        if (e.Direction != ArrowDirection.Right) { base.OnRenderArrow(e); return; }
        int x = e.ArrowRectangle.Left + e.ArrowRectangle.Width / 2;
        int y = e.ArrowRectangle.Top + e.ArrowRectangle.Height / 2;
        int size = Scale(e.Graphics, 3);
        using var brush = new SolidBrush(e.Item.Enabled ? Ink : Muted);
        e.Graphics.FillPolygon(brush, [new Point(x - size / 2, y - size),
            new Point(x + size / 2 + 1, y), new Point(x - size / 2, y + size)]);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Separator);
        int inset = Scale(e.Graphics, 16);
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

    static int Scale(Graphics graphics, int logical) =>
        Math.Max(1, (int)Math.Round(logical * graphics.DpiX / 96f));

    static int VisibleRight(ToolStripItem item) => Math.Min(item.Width,
        item.Owner.ClientSize.Width - item.Owner.Padding.Right - item.Bounds.Left);
}
