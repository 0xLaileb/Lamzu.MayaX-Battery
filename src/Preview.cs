using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MayaXBattery;

/// <summary>
/// Renders the tray icons through the same code the app uses, at true pixel size and magnified,
/// on both taskbar themes. Development aid only: --preview &lt;file.png&gt;.
/// </summary>
internal static class Preview
{
    internal static void Write(string outPath, UiLanguage language)
    {
        var strings = new UiStrings(language);
        (string text, Palette pal, string label)[] states =
        {
            ("100", Palette.Charging, strings.PreviewCharging),
            ("94",  Palette.Normal,   "94%"),
            ("49",  Palette.Normal,   "49%"),
            ("18",  Palette.Low,      strings.PreviewLow),
            ("7",   Palette.Critical, strings.PreviewCritical),
            ("?",   Palette.Unknown,  strings.PreviewUnknown),
        };

        const int native = 16;      // real tray size at 100% DPI
        const int scale = 7;
        const int cell = native * scale;
        const int pad = 20;
        const int labelH = 24;
        const int trueRowH = 34;

        int w = pad + states.Length * (cell + pad);
        int h = labelH + cell + trueRowH + labelH + cell + trueRowH + pad;

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(243, 243, 245));

        int darkTop = labelH + cell + trueRowH;
        using (var dark = new SolidBrush(Color.FromArgb(28, 28, 30)))
            g.FillRectangle(dark, 0, darkTop, w, h - darkTop);

        using var labelFont = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
        using var onLight = new SolidBrush(Color.FromArgb(40, 40, 40));
        using var onDark = new SolidBrush(Color.FromArgb(230, 230, 230));
        var center = new StringFormat { Alignment = StringAlignment.Center };

        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        for (int i = 0; i < states.Length; i++)
        {
            var (text, pal, label) = states[i];
            int x = pad + i * (cell + pad);

            using var shot = IconFactory.Render(text, pal, native, light: true);
            using var darkShot = IconFactory.Render(text, pal, native);

            // magnified, then again at true 16px so the real thing is visible too
            g.DrawImage(shot, new Rectangle(x, labelH, cell, cell));
            g.DrawImage(shot, new Rectangle(x + cell / 2 - native / 2, labelH + cell + 8, native, native));

            g.DrawImage(darkShot, new Rectangle(x, darkTop + labelH, cell, cell));
            g.DrawImage(darkShot, new Rectangle(x + cell / 2 - native / 2, darkTop + labelH + cell + 8, native, native));

            g.DrawString(label, labelFont, onLight, new RectangleF(x - pad / 2f, 5, cell + pad, labelH), center);
            g.DrawString(label, labelFont, onDark, new RectangleF(x - pad / 2f, darkTop + 5, cell + pad, labelH), center);
        }

        bmp.Save(outPath, ImageFormat.Png);

        // Also emit the real multi-frame icon so the .ico writer itself can be inspected.
        using var icon = IconFactory.Build("87", Palette.Normal);
        using var fs = File.Create(Path.ChangeExtension(outPath, ".ico"));
        icon.Save(fs);
    }
}

