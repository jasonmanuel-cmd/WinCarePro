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

        // The sizes Windows actually requests. 256 is the PNG-compressed
        // large icon; below that it is BMP.
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };

        var images = new List<Bitmap>();
        foreach (var size in sizes)
        {
            var bmp = Render(size);
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

        return 0;
    }

    private static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        // Rounded square plate. The corner radius is a fraction of the size so
        // it looks the same at 16px and at 256px.
        var radius = size * 0.22f;
        var plate = new RectangleF(0, 0, size, size);

        using (var path = RoundedRect(plate, radius))
        {
            using var brush = new LinearGradientBrush(
                new RectangleF(0, 0, size, size), Brand, BrandDeep, 55f);
            g.FillPath(brush, path);
        }

        // Bolt, drawn as a filled polygon so it scales without blurring.
        using (var bolt = new GraphicsPath())
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
            bolt.AddPolygon(Scale(pts, size));
            using var fill = new SolidBrush(Color.White);
            g.FillPath(fill, bolt);
        }

        return bmp;
    }

    private static PointF[] Scale(PointF[] points, int size) =>
        points.Select(p => new PointF(p.X * size, p.Y * size)).ToArray();

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
