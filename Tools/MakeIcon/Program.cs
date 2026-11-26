using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace MakeIcon;

/// <summary>
/// Generates WinCare Pro's Option 3 application icon at every size Windows asks for.
/// </summary>
/// <remarks>
/// The icon is committed to the repo rather than generated at build time, so
/// the build stays reproducible and needs no image tooling on a fresh machine.
/// This tool exists to regenerate it when the mark changes.
///
/// Option 3 mark: a white security shield with an EvenOdd lightning-bolt cutout
/// centred on a #0D9488 -> #0F766E -> #134E4A rounded squircle with a subtle
/// inner rim highlight. The geometry matches the vector Path in MainWindow.xaml.
/// </remarks>
internal static class Program
{
    private static readonly Color BrandBright = Color.FromArgb(0x0D, 0x94, 0x88);
    private static readonly Color BrandPrimary = Color.FromArgb(0x0F, 0x76, 0x6E);
    private static readonly Color BrandDeep = Color.FromArgb(0x13, 0x4E, 0x4A);

    [STAThread]
    private static int Main(string[] args)
    {
        var outPath = args.Length > 0 ? args[0] : "WinCarePro.ico";
        var fullOutPath = Path.GetFullPath(outPath);
        var outDir = Path.GetDirectoryName(fullOutPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outDir);

        string? assetsDir = null;

        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--assets" && i + 1 < args.Length) assetsDir = args[++i];
        }

        // The sizes Windows actually requests. 256 is the PNG-compressed
        // large icon; below 64 it is BMP.
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };

        var images = new List<Bitmap>();
        foreach (var size in sizes)
        {
            var bmp = RenderMark(size, 0f);
            images.Add(bmp);

            // Write per-size PNGs beside the .ico so the mark can be reviewed
            // at each size without opening a multi-resolution icon viewer.
            using (var buffer = new MemoryStream())
            {
                bmp.Save(buffer, ImageFormat.Png);
                File.WriteAllBytes(Path.Combine(outDir, $"icon-{size}.png"), buffer.ToArray());
            }
        }

        try
        {
            WriteIco(fullOutPath, images);
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
    /// Draws the Option 3 mark: a rounded teal gradient squircle with subtle
    /// inner rim and a white shield carrying an EvenOdd bolt cutout.
    /// </summary>
    private static Bitmap RenderMark(int size, float inset)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        var plate = new RectangleF(
            size * inset, size * inset,
            size * (1f - 2f * inset), size * (1f - 2f * inset));

        DrawPlate(g, plate);
        return bmp;
    }

    /// <summary>
    /// Fills a 3-stop rounded gradient squircle, strokes a subtle inner rim,
    /// and draws the white shield with EvenOdd bolt cutout centred inside it.
    /// </summary>
    private static void DrawPlate(Graphics g, RectangleF plate)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var radius = plate.Width * 0.225f;

        using (var path = SquirclePath(plate, radius))
        using (var brush = CreateTealGradient(plate))
        {
            g.FillPath(brush, path);
        }

        // Subtle inner rim highlight so the squircle reads crisply against both
        // light and dark Windows taskbars and shells.
        var rimThickness = Math.Max(0.8f, plate.Width * 0.018f);
        var rimInset = rimThickness * 0.65f;
        var rimRect = RectangleF.Inflate(plate, -rimInset, -rimInset);
        var rimRadius = Math.Max(1f, radius - rimInset);
        if (rimRect.Width > 4f && rimRect.Height > 4f)
        {
            using var rimPath = SquirclePath(rimRect, rimRadius);
            using var rimPen = new Pen(Color.FromArgb(46, 255, 255, 255), rimThickness);
            g.DrawPath(rimPen, rimPath);
        }

        using var shield = BuildShieldWithBoltCutout(plate);
        using var fill = new SolidBrush(Color.White);
        g.FillPath(fill, shield);
    }

    private static LinearGradientBrush CreateTealGradient(RectangleF rect)
    {
        var brush = new LinearGradientBrush(
            new PointF(rect.Left, rect.Top),
            new PointF(rect.Right, rect.Bottom),
            BrandBright,
            BrandDeep)
        {
            WrapMode = WrapMode.TileFlipXY,
            InterpolationColors = new ColorBlend
            {
                Colors = new[] { BrandBright, BrandPrimary, BrandDeep },
                Positions = new[] { 0f, 0.5f, 1f }
            }
        };
        return brush;
    }

    /// <summary>
    /// Builds the Option 3 shield outer silhouette plus internal lightning-bolt
    /// cutout on a 24x24 design grid using FillMode.Alternate (EvenOdd).
    /// Matches MainWindow.xaml's sidebar Path data verbatim:
    /// F0 M 12,3.2 L 19.2,6 L 19.2,11.4 C 19.2,16 16.1,19.6 12,21 C 7.9,19.6 4.8,16 4.8,11.4 L 4.8,6 Z
    ///    M 13.4,6.2 L 8.6,12.6 L 11.5,12.6 L 10.6,17.8 L 15.4,11.4 L 12.5,11.4 Z
    /// </summary>
    private static GraphicsPath BuildShieldWithBoltCutout(RectangleF plate)
    {
        PointF P(float x, float y) => new(
            plate.X + (x / 24f) * plate.Width,
            plate.Y + (y / 24f) * plate.Height);

        var path = new GraphicsPath(FillMode.Alternate);

        // Outer shield figure
        path.StartFigure();
        path.AddLine(P(12.0f, 3.2f), P(19.2f, 6.0f));
        path.AddLine(P(19.2f, 6.0f), P(19.2f, 11.4f));
        path.AddBezier(
            P(19.2f, 11.4f),
            P(19.2f, 16.0f),
            P(16.1f, 19.6f),
            P(12.0f, 21.0f));
        path.AddBezier(
            P(12.0f, 21.0f),
            P(7.9f, 19.6f),
            P(4.8f, 16.0f),
            P(4.8f, 11.4f));
        path.AddLine(P(4.8f, 11.4f), P(4.8f, 6.0f));
        path.CloseFigure();

        // Inner bolt cutout figure (EvenOdd)
        path.StartFigure();
        path.AddPolygon(new[]
        {
            P(13.4f, 6.2f),
            P(8.6f, 12.6f),
            P(11.5f, 12.6f),
            P(10.6f, 17.8f),
            P(15.4f, 11.4f),
            P(12.5f, 11.4f),
        });

        return path;
    }

    /// <summary>
    /// Emits the artwork shapes the Microsoft Store requires inside the MSIX package.
    /// </summary>
    private static void WriteStoreAssets(string dir)
    {
        Directory.CreateDirectory(dir);

        void Square(string name, int size) =>
            Save(RenderMark(size, 0f), Path.Combine(dir, name));

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
    /// 310x150 transparent banner: Option 3 mark on the left, wordmark beside it.
    /// </summary>
    private static Bitmap RenderWideLogo()
    {
        const int w = 310, h = 150;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Transparent);

        var mark = 76f;
        var plate = new RectangleF(18, (h - mark) / 2f, mark, mark);
        DrawPlate(g, plate);

        using var font = new Font("Segoe UI Semibold", 30f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.FromArgb(0x0F, 0x17, 0x2A));

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
    /// 620x300 launch image with the Option 3 teal gradient and inverted mark.
    /// </summary>
    private static Bitmap RenderSplash()
    {
        const int w = 620, h = 300;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Transparent);

        using (var bg = CreateTealGradient(new RectangleF(0, 0, w, h)))
        {
            g.FillRectangle(bg, 0, 0, w, h);
        }

        var mark = 104f;
        var plate = new RectangleF((w - mark) / 2f, (h - mark) / 2f - 26, mark, mark);

        // Inverted white squircle carrying the Option 3 shield-with-bolt-cutout in primary teal.
        using (var path = SquirclePath(plate, mark * 0.225f))
        using (var fill = new SolidBrush(Color.White))
        {
            g.FillPath(fill, path);
        }

        using (var shield = BuildShieldWithBoltCutout(plate))
        using (var fill = new SolidBrush(BrandPrimary))
        {
            g.FillPath(fill, shield);
        }

        using var font = new Font("Segoe UI Semibold", 30f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var text = new SolidBrush(Color.White);

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

    /// <summary>
    /// Smooth cubic-Bezier squircle (continuous-curvature rounded square).
    /// </summary>
    private static GraphicsPath SquirclePath(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var k = Math.Min(radius * 1.15f, Math.Min(r.Width, r.Height) * 0.48f);
        var c = k * 0.55228475f;

        path.StartFigure();
        path.AddLine(r.Left + k, r.Top, r.Right - k, r.Top);
        path.AddBezier(
            r.Right - k, r.Top,
            r.Right - k + c, r.Top,
            r.Right, r.Top + k - c,
            r.Right, r.Top + k);
        path.AddLine(r.Right, r.Top + k, r.Right, r.Bottom - k);
        path.AddBezier(
            r.Right, r.Bottom - k,
            r.Right, r.Bottom - k + c,
            r.Right - k + c, r.Bottom,
            r.Right - k, r.Bottom);
        path.AddLine(r.Right - k, r.Bottom, r.Left + k, r.Bottom);
        path.AddBezier(
            r.Left + k, r.Bottom,
            r.Left + k - c, r.Bottom,
            r.Left, r.Bottom - k + c,
            r.Left, r.Bottom - k);
        path.AddLine(r.Left, r.Bottom - k, r.Left, r.Top + k);
        path.AddBezier(
            r.Left, r.Top + k,
            r.Left, r.Top + k - c,
            r.Left + k - c, r.Top,
            r.Left + k, r.Top);
        path.CloseFigure();
        return path;
    }

    private static void WriteIco(string path, List<Bitmap> images)
    {
        var payloads = new List<byte[]>(images.Count);

        foreach (var img in images)
        {
            using var buffer = new MemoryStream();

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

    private static byte[] ToBmpBytes(Bitmap img)
    {
        using var buffer = new MemoryStream();
        using var w = new BinaryWriter(buffer);

        var w32 = img.Width;
        var h32 = img.Height;

        w.Write(40);                       // biSize
        w.Write(w32);                      // biWidth
        w.Write(h32 * 2);                  // biHeight
        w.Write((ushort)1);                // biPlanes
        w.Write((ushort)32);               // biBitCount
        w.Write(0);                        // biCompression = BI_RGB
        w.Write(w32 * h32 * 4);           // biSizeImage
        w.Write(2835); w.Write(2835);      // ppm
        w.Write(0); w.Write(0);            // colours used / important

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

        var maskStride = ((w32 + 31) / 32) * 4;
        var mask = new byte[maskStride * h32];
        w.Write(mask);

        w.Flush();
        return buffer.ToArray();
    }
}
