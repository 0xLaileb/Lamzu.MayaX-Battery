using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace MayaXBattery;

// Apple-inspired status colours, with darker variants for a light Windows taskbar.
internal readonly record struct Palette(Color Fore, Color LightFore)
{
    internal static readonly Palette Charging = new(Color.FromArgb(48, 209, 88), Color.FromArgb(24, 114, 49));
    internal static readonly Palette Normal = new(Color.FromArgb(245, 245, 247), Color.FromArgb(28, 28, 30));
    internal static readonly Palette Low = new(Color.FromArgb(255, 214, 10), Color.FromArgb(140, 103, 0));
    internal static readonly Palette Critical = new(Color.FromArgb(255, 69, 58), Color.FromArgb(196, 38, 32));
    internal static readonly Palette Unknown = new(Color.FromArgb(174, 174, 178), Color.FromArgb(99, 99, 102));
}
internal static class IconFactory
{
    // Every size the shell may ask for, across DPI settings. Each is drawn natively, so nothing
    // is ever rescaled — rescaling was what made the icon look soft next to real app icons.
    static readonly int[] Sizes = { 16, 20, 24, 28, 32, 40, 48, 64 };

    internal static Icon Build(string text, Palette palette)
    {
        var frames = new List<Bitmap>();
        try
        {
            bool light = IsLightTaskbar();
            foreach (var s in Sizes) frames.Add(Render(text, palette, s, light));
            using var ms = new MemoryStream();
            WriteIco(ms, frames);
            ms.Position = 0;
            using var loaded = new Icon(ms, SystemInformation.SmallIconSize);
            return (Icon)loaded.Clone();
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    static bool IsLightTaskbar()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
        }
        catch (System.Security.SecurityException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    static FontFamily CreateNumeralFamily()
    {
        try { return new FontFamily("Bahnschrift SemiBold"); }
        catch (ArgumentException) { return new FontFamily("Segoe UI"); }
    }

    /// <summary>Only antialiased numeral outlines; the rest of the icon stays transparent.</summary>
    internal static Bitmap Render(string text, Palette palette, int size, bool light = false)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var family = CreateNumeralFamily();
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        using var path = new GraphicsPath();
        path.AddString(text, family, (int)FontStyle.Regular, 100, PointF.Empty, format);
        var bounds = path.GetBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0) return bmp;

        // Preserve the font proportions. Shorter numerals leave breathing room in the tray;
        // 100 scales down uniformly instead of being squeezed horizontally.
        float inset = Math.Max(1f, size / 16f);
        float scale = Math.Min(size * 0.64f / bounds.Height, (size - 2 * inset) / bounds.Width);
        float x = (size - bounds.Width * scale) / 2f;
        float y = (size - bounds.Height * scale) / 2f;
        using var transform = new Matrix(scale, 0, 0, scale,
            x - bounds.X * scale, y - bounds.Y * scale);        path.Transform(transform);
        using var brush = new SolidBrush(light ? palette.LightFore : palette.Fore);
        g.FillPath(brush, path);
        return bmp;
    }
    /// <summary>Writes a classic multi-frame .ico (BITMAPINFOHEADER + 32bpp XOR + 1bpp AND mask).</summary>
    static void WriteIco(Stream stream, List<Bitmap> frames)
    {
        var payloads = frames.Select(EncodeDib).ToList();

        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write((ushort)0);                  // reserved
        w.Write((ushort)1);                  // type: icon
        w.Write((ushort)frames.Count);

        int offset = 6 + 16 * frames.Count;
        for (int i = 0; i < frames.Count; i++)
        {
            var f = frames[i];
            w.Write((byte)(f.Width >= 256 ? 0 : f.Width));
            w.Write((byte)(f.Height >= 256 ? 0 : f.Height));
            w.Write((byte)0);                // palette entries
            w.Write((byte)0);                // reserved
            w.Write((ushort)1);              // planes
            w.Write((ushort)32);             // bits per pixel
            w.Write(payloads[i].Length);
            w.Write(offset);
            offset += payloads[i].Length;
        }
        foreach (var p in payloads) w.Write(p);
        w.Flush();
    }

    static byte[] EncodeDib(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int maskStride = ((w + 31) / 32) * 4;      // 1bpp rows padded to 4 bytes
        int xorSize = w * h * 4;
        int andSize = maskStride * h;

        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new byte[Math.Abs(data.Stride) * h];
        try
        {
            for (int y = 0; y < h; y++)
                System.Runtime.InteropServices.Marshal.Copy(
                    data.Scan0 + y * data.Stride, pixels, y * Math.Abs(data.Stride), Math.Abs(data.Stride));
        }
        finally { bmp.UnlockBits(data); }

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(40);                 // biSize
        bw.Write(w);                  // biWidth
        bw.Write(h * 2);              // biHeight — XOR and AND stacked
        bw.Write((ushort)1);          // biPlanes
        bw.Write((ushort)32);         // biBitCount
        bw.Write(0);                  // BI_RGB
        bw.Write(xorSize + andSize);  // biSizeImage
        bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

        // XOR bitmap, bottom-up; GDI+ 32bppArgb is already BGRA in memory
        int stride = Math.Abs(data.Stride);
        for (int y = h - 1; y >= 0; y--)
            bw.Write(pixels, y * stride, w * 4);

        // AND mask: alpha carries transparency, so leave it fully opaque
        bw.Write(new byte[andSize]);

        bw.Flush();
        return ms.ToArray();
    }
}




