using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace MakeIcon;

/// <summary>
/// Generates WinCare Pro's application icon at every size Windows asks for.
///
/// </summary>
/// <remarks>
/// The icon is committed to the repo rather than generated at build time, so
/// the build stays reproducible and needs no image tooling on a fresh machine.
/// This tool exists to regenerate it when the mark changes.
///
/// The design is deliberately the simplest thing that reads at 16x16: a
/// lightning bolt in the brand teal. Windows renders the exe icon at 16, 24,
/// 32, 48, 64 and 256 pixels across Explorer, the taskbar, the Start Menu and
/// Alt-Tab, and anything with fine detail turns to mush at the small end.
/// A single high-contrast glyph is the only shape that survives all of them.
/// </remarks>
internal static class Program
{
    private static readonly Color Brand = Color.FromArgb(0x0D, 0x94, 0x88);
    private static readonly Color BrandDeep = Color.FromArgb(0x0F, 0x76, 0x6E);

    [STAThread]
    private static int Main(string[] args)
    {
        var outPath = args.Length > 0 ? args[0] : "WinCarePro.ico";
        string? assetsDir = null;

        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--assets" && i + 1 < args.Length) assetsDir = args[++i];
        }

        // The sizes Windows actually requests. 256 is the PNG-compressed
        // large icon; below that it is BMP.
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };

        var images = new List<Bitmap>();
        foreach (var size in sizes)
        {
            var bmp = RenderMark(size, 0f);
            images.Add(bmp);

            // Each frame is stored as a PNG inside the .ico for sizes >= 64.
            // That is what Windows itself writes, and it keeps the file small
            // while staying lossless. The PNGs are also written beside the .ico
            // so the mark can be reviewed at each size without opening a viewer
            // that understands multi-resolution icons.
            using (var buffer = new MemoryStream())
            {
                bmp.Save(buffer, ImageFormat.Png);
                File.WriteAllBytes($"icon-{size}.png", buffer.ToArray());
            }
        }

        try
        {
            WriteIco(outPath, images);
            Console.WriteLine($"Wrote {outPath} with {sizes.Length} frames");
        }
        finally
        {
            foreach (var b in images) b.Dispose();
        }

        if (assetsDir != null)
        {
            WriteStoreAssets(assetsDir);
        }

        return 0;
    }

    /// <summary>
    /// Draws the mark: a rounded teal plate with a white bolt.
    /// </summary>
    /// <param name="size">Canvas edge in pixels. Always square.</param>
    /// <param name="inset">
    /// Fraction of the canvas to leave empty on every side. 0 fills the canvas
    /// edge to edge, which is what taskbar and Alt-Tab want. The Store's
    /// "Targeted" logos want the mark inset instead, because Windows applies
    /// its own mask and shadow around it and a full-bleed plate gets clipped.
    /// </param>
    private static Bitmap RenderMark(int size, float inset)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        var plate = new RectangleF(
            size * inset, size * inset,
            size * (1f - 2f * inset), size * (1f - 2f * inset));

        DrawPlate(g, plate);
        return bmp;
    }

    /// <summary>
    /// Fills a rounded gradient plate and draws the bolt centred inside it.
    /// </summary>
    private static void DrawPlate(Graphics g, RectangleF plate)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Rounded square plate. The corner radius is a fraction of the size so
        // it looks the same at 16px and at 256px.
        var radius = plate.Width * 0.22f;

        using (var path = RoundedRect(plate, radius))
        {
            using var brush = new LinearGradientBrush(plate, Brand, BrandDeep, 55f);
            g.FillPath(brush, path);
        }

        DrawBolt(g, plate);
    }

    /// <summary>
    /// The bolt polygon, drawn as a filled path so it scales without blurring.
    /// </summary>
    private static void DrawBolt(Graphics g, RectangleF plate)
    {
        var pts = new[]
        {
            new PointF(0.56f, 0.13f),
            new PointF(0.30f, 0.55f),
            new PointF(0.47f, 0.55f),
            new PointF(0.42f, 0.88f),
            new PointF(0.72f, 0.44f),
            new PointF(0.54f, 0.44f),
        };

        var scaled = pts
            .Select(p => new PointF(
                plate.X + p.X * plate.Width,
                plate.Y + p.Y * plate.Height))
            .ToArray();

        using var bolt = new GraphicsPath();
        bolt.AddPolygon(scaled);
        using var fill = new SolidBrush(Color.White);
        g.FillPath(fill, bolt);
    }

    /// <summary>
    /// Emits the artwork shapes the Microsoft Store requires inside the MSIX
    /// package.
    /// </summary>
    /// <remarks>
    /// Store validates asset names and dimensions at submission time, so these
    /// are generated rather than hand-drawn: getting one dimension wrong is a
    /// rejected submission, and there is no useful error message for it.
    /// </remarks>
    private static void WriteStoreAssets(string dir)
    {
        Directory.CreateDirectory(dir);

        // Full-bleed squares. Windows does not mask these, so the plate runs to
        // the canvas edge exactly as the taskbar icon does.
        void Square(string name, int size) =>
            Save(RenderMark(size, 0f), Path.Combine(dir, name));

        // "Targeted" squares. Windows draws its own rounded mask and drop shadow
        // around these, so the mark is inset and the outside is transparent.
        // Full bleed here produces a plate with a visible second border.
        void Targeted(string name, int size) =>
            Save(RenderMark(size, 0.14f), Path.Combine(dir, name));

        Square("Square44x44Logo.png", 44);
        Square("Square71x71Logo.png", 71);
        Square("Square150x150Logo.png", 150);
        Square("StoreLogo.png", 50);
        Square("LockScreenLogo.png", 24);

        Targeted("Square44x44LogoTargeted.png", 44);
        Targeted("Square71x71LogoTargeted.png", 71);
        Targeted("Square150x150LogoTargeted.png", 150);

        Save(RenderWideLogo(), Path.Combine(dir, "WideLogo.png"));
        Save(RenderSplash(), Path.Combine(dir, "SplashScreen.png"));

        Console.WriteLine($"Wrote {Directory.GetFiles(dir).Length} Store asset(s) to {dir}");
    }

    /// <summary>
    /// 310x150 transparent banner: mark on the left, wordmark beside it.
    /// </summary>
    private static Bitmap RenderWideLogo()
    {
        const int w = 310, h = 150;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Transparent);

        var mark = 76f;
        var plate = new RectangleF(18, (h - mark) / 2f, mark, mark);
        DrawPlate(g, plate);

        using var font = new Font("Segoe UI Semibold", 30f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.FromArgb(0x0F, 0x17, 0x2A));

        // Centred in the space to the right of the mark, not at a fixed offset.
        // A fixed offset pushed the wordmark off the canvas once the measured
        // string was wider than the remaining room.
        using var centred = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString("WinCare Pro", font, brush,
            new RectangleF(18 + mark + 8, 0, w - (18 + mark + 8) - 8, h), centred);

        return bmp;
    }

    /// <summary>
    /// 620x300 launch image.
    /// </summary>
    /// <remarks>
    /// Opaque brand fill rather than transparency. The splash is composited by
    /// the shell on backgrounds this app cannot know in advance, and a
    /// transparent one either vanishes or turns into dark-grey-on-dark on a
    /// dark-theme machine.
    /// </remarks>
    private static Bitmap RenderSplash()
    {
        const int w = 620, h = 300;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Transparent);

        using (var bg = new LinearGradientBrush(
                   new RectangleF(0, 0, w, h), Brand, BrandDeep, 60f))
        {
            g.FillRectangle(bg, 0, 0, w, h);
        }

        var mark = 104f;
        var plate = new RectangleF((w - mark) / 2f, (h - mark) / 2f - 26, mark, mark);

        // The plate is white on brand teal here, so invert it: a white plate
        // carrying a brand-coloured bolt, which reads at a glance on launch.
        using (var path = RoundedRect(plate, mark * 0.22f))
        using (var fill = new SolidBrush(Color.White))
        {
            g.FillPath(fill, path);
        }

        var pts = new[]
        {
            new PointF(0.56f, 0.13f), new PointF(0.30f, 0.55f),
            new PointF(0.47f, 0.55f), new PointF(0.42f, 0.88f),
            new PointF(0.72f, 0.44f), new PointF(0.54f, 0.44f),
        };
        var scaled = pts
            .Select(p => new PointF(
                plate.X + p.X * plate.Width, plate.Y + p.Y * plate.Height))
            .ToArray();
        using (var bolt = new GraphicsPath())
        using (var fill = new SolidBrush(Color.FromArgb(0x0F, 0x76, 0x6E)))
        {
            bolt.AddPolygon(scaled);
            g.FillPath(fill, bolt);
        }

        using var font = new Font("Segoe UI Semibold", 30f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var text = new SolidBrush(Color.White);

        // Centre across the full canvas width. Passing a PointF to DrawString
        // with GenericTypographic anchors the text at that point instead, which
        // put the wordmark flush against the left edge and clipped it.
        using var centred = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Near
        };
        g.DrawString("WinCare Pro", font, text,
            new RectangleF(0, plate.Bottom + 20, w, 44f), centred);

        return bmp;
    }

    private static void Save(Bitmap bmp, string path)
    {
        using (bmp) bmp.Save(path, ImageFormat.Png);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Writes a multi-image .ico by hand. GDI+ cannot save one.
    /// </summary>
    /// <remarks>
    /// Written in one pass, and deliberately so. An earlier version wrote the
    /// image data first and then seeked back to patch the directory offsets,
    /// which requires knowing every frame's encoded length in advance. That
    /// assumption was wrong: the .ico directory sits *before* the image data, so
    /// the first attempt emitted 16-byte frames for everything after the first
    /// and produced a file Windows could not read at all. Encoding every frame
    /// into memory first makes the two passes trivially correct.
    /// </remarks>
    private static void WriteIco(string path, List<Bitmap> images)
    {
        // Encode every frame up front so both the directory and the data can be
        // written left to right in a single pass.
        var payloads = new List<byte[]>(images.Count);

        foreach (var img in images)
        {
            using var buffer = new MemoryStream();

            // PNG for the larger frames, which is what Windows itself does and
            // stops the 256px frame from dominating the file size. BMP below
            // that, because some older shell surfaces will not decode PNG at
            // small sizes.
            if (img.Width >= 64)
            {
                img.Save(buffer, ImageFormat.Png);
            }
            else
            {
                var bmp = ToBmpBytes(img);
                buffer.Write(bmp);
            }

            payloads.Add(buffer.ToArray());
        }

        const int headerSize = 6;
        const int entrySize = 16;
        var dataStart = headerSize + (images.Count * entrySize);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);

        w.Write((ushort)0);        // reserved
        w.Write((ushort)1);        // type: icon
        w.Write((ushort)images.Count);

        var offset = dataStart;
        for (var i = 0; i < images.Count; i++)
        {
            var img = images[i];
            var data = payloads[i];

            // 0 means 256 in the .ico directory; anything else is the literal
            // pixel size. Sizes above 255 therefore cannot be expressed.
            w.Write(img.Width >= 256 ? (byte)0 : (byte)img.Width);
            w.Write(img.Height >= 256 ? (byte)0 : (byte)img.Height);
            w.Write((byte)0);      // palette entries
            w.Write((byte)0);      // reserved
            w.Write((ushort)1);    // colour planes
            w.Write((ushort)32);   // bits per pixel
            w.Write((uint)data.Length);
            w.Write((uint)offset);

            offset += data.Length;
        }

        foreach (var data in payloads) w.Write(data);
        w.Flush();
    }

    /// <summary>
    /// Encodes a bitmap as the DIB an .ico frame expects.
    /// </summary>
    private static byte[] ToBmpBytes(Bitmap img)
    {
        using var buffer = new MemoryStream();
        using var w = new BinaryWriter(buffer);

        var w32 = img.Width;
        var h32 = img.Height;

        // BITMAPINFOHEADER. biHeight is doubled to cover the XOR + AND masks.
        w.Write(40);                       // biSize
        w.Write(w32);                      // biWidth
        w.Write(h32 * 2);                  // biHeight
        w.Write((ushort)1);                // biPlanes
        w.Write((ushort)32);               // biBitCount
        w.Write(0);                        // biCompression = BI_RGB
        w.Write(w32 * h32 * 4);           // biSizeImage
        w.Write(2835); w.Write(2835);      // ppm
        w.Write(0); w.Write(0);            // colours used / important

        // Pixel data, bottom-up, BGRA.
        var rect = new Rectangle(0, 0, w32, h32);
        var data = img.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var rowBytes = w32 * 4;
        var row = new byte[rowBytes];

        for (var y = h32 - 1; y >= 0; y--)
        {
            System.Runtime.InteropServices.Marshal.Copy(
                data.Scan0 + (y * data.Stride), row, 0, rowBytes);
            w.Write(row);
        }
        img.UnlockBits(data);

        // AND mask: one bit per pixel, rows padded to 4 bytes. All zero means
        // fully opaque, which is correct for this design since the plate is
        // drawn as an anti-aliased path and already carries the alpha.
        var maskStride = ((w32 + 31) / 32) * 4;
        var mask = new byte[maskStride * h32];
        w.Write(mask);

        w.Flush();
        return buffer.ToArray();
    }
}
