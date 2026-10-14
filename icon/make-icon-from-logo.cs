using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

// Creates StepLoop.ico (16-256 px) from a logo image (PNG) on a uniform dark background.
// Usage:  make-icon-from-logo.exe <logo.png> <output-folder>
// The logo is centered in a rounded square (Windows 11 style); at small sizes it takes up more of the tile.
static class MakeIconFromLogo
{
    static Color bg;
    static Rectangle logoBox;
    static Bitmap src;

    static void Analyze()
    {
        int w = src.Width, h = src.Height;
        BitmapData d = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] px = new byte[d.Stride * h];
        Marshal.Copy(d.Scan0, px, 0, px.Length);
        src.UnlockBits(d);

        // background color: average of the four corners (24x24 px)
        long r = 0, g = 0, b = 0, n = 0;
        int[][] corners = { new int[] { 0, 0 }, new int[] { w - 24, 0 }, new int[] { 0, h - 24 }, new int[] { w - 24, h - 24 } };
        foreach (int[] c in corners)
            for (int y = c[1]; y < c[1] + 24; y++)
                for (int x = c[0]; x < c[0] + 24; x++)
                {
                    int o = y * d.Stride + x * 4;
                    b += px[o]; g += px[o + 1]; r += px[o + 2]; n++;
                }
        bg = Color.FromArgb((int)(r / n), (int)(g / n), (int)(b / n));

        // logo bounding box: pixels that clearly differ from the background
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = y * d.Stride + x * 4;
                int dist = Math.Abs(px[o + 2] - bg.R) + Math.Abs(px[o + 1] - bg.G) + Math.Abs(px[o] - bg.B);
                if (dist > 90)
                {
                    if (x < x0) x0 = x; if (x > x1) x1 = x;
                    if (y < y0) y0 = y; if (y > y1) y1 = y;
                }
            }
        logoBox = new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    static Bitmap Render(int s)
    {
        // share of the tile used by the logo: larger when the icon is small
        double occ = s <= 24 ? 0.90 : (s <= 48 ? 0.84 : 0.78);
        double side = Math.Max(logoBox.Width, logoBox.Height) / occ;
        double cx = logoBox.X + logoBox.Width / 2.0, cy = logoBox.Y + logoBox.Height / 2.0;
        RectangleF from = new RectangleF((float)(cx - side / 2), (float)(cy - side / 2), (float)side, (float)side);

        using (Bitmap tile = new Bitmap(s, s, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(tile))
            {
                g.Clear(bg);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                // part of the source that falls inside the image (the rest stays background)
                RectangleF inter = RectangleF.Intersect(from, new RectangleF(0, 0, src.Width, src.Height));
                float k = s / from.Width;
                RectangleF dst = new RectangleF((inter.X - from.X) * k, (inter.Y - from.Y) * k, inter.Width * k, inter.Height * k);
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(src, new Rectangle((int)Math.Round(dst.X), (int)Math.Round(dst.Y), (int)Math.Round(dst.Width), (int)Math.Round(dst.Height)),
                        inter.X, inter.Y, inter.Width, inter.Height, GraphicsUnit.Pixel, ia);
                }
            }

            // rounded-corner mask with soft edges
            Bitmap result = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float m = Math.Max(0.5f, s * 0.015f);
                RectangleF r = new RectangleF(m, m, s - 2 * m, s - 2 * m);
                using (GraphicsPath p = Round(r, s * 0.22f))
                using (TextureBrush tb = new TextureBrush(tile))
                    g.FillPath(tb, p);
            }
            return result;
        }
    }

    static GraphicsPath Round(RectangleF r, float rad)
    {
        float d = rad * 2;
        GraphicsPath p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static byte[] Dib(Bitmap b)
    {
        int w = b.Width, h = b.Height;
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter bw = new BinaryWriter(ms))
        {
            bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write((short)1); bw.Write((short)32);
            bw.Write(0); bw.Write(w * h * 4); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            for (int y = h - 1; y >= 0; y--)
                for (int x = 0; x < w; x++)
                {
                    Color c = b.GetPixel(x, y);
                    bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
                }
            bw.Write(new byte[((w + 31) / 32) * 4 * h]);
            return ms.ToArray();
        }
    }

    static byte[] Png(Bitmap b)
    {
        using (MemoryStream ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    static void Main(string[] args)
    {
        string logo = args[0];
        string dir = args.Length > 1 ? args[1] : ".";
        Directory.CreateDirectory(dir);
        src = new Bitmap(logo);
        Analyze();
        Console.WriteLine("Background " + bg.R + "," + bg.G + "," + bg.B + "  logo " + logoBox);

        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
        List<byte[]> data = new List<byte[]>();
        foreach (int s in sizes)
            using (Bitmap b = Render(s)) data.Add(s >= 256 ? Png(b) : Dib(b));

        using (FileStream fs = new FileStream(Path.Combine(dir, "StepLoop.ico"), FileMode.Create))
        using (BinaryWriter bw = new BinaryWriter(fs))
        {
            bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                bw.Write((byte)(s >= 256 ? 0 : s)); bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)0); bw.Write((byte)0);
                bw.Write((short)1); bw.Write((short)32);
                bw.Write(data[i].Length); bw.Write(offset);
                offset += data[i].Length;
            }
            foreach (byte[] d in data) bw.Write(d);
        }

        // preview: large + small sizes on dark and light backgrounds (like the taskbar / Explorer)
        using (Bitmap prev = new Bitmap(700, 320))
        using (Graphics g = Graphics.FromImage(prev))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush dk = new SolidBrush(Color.FromArgb(0x20, 0x20, 0x20))) g.FillRectangle(dk, 0, 0, 350, 320);
            using (SolidBrush lt = new SolidBrush(Color.FromArgb(0xF3, 0xF3, 0xF3))) g.FillRectangle(lt, 350, 0, 350, 320);
            using (Bitmap big = Render(256)) g.DrawImage(big, 47, 32, 256, 256);
            int x = 372;
            foreach (int s in new int[] { 64, 48, 32, 24, 16 })
            {
                using (Bitmap b = Render(s)) g.DrawImage(b, x, 40 + (64 - s) / 2, s, s);
                x += s + 14;
            }
            using (Bitmap b2 = Render(128)) g.DrawImage(b2, 412, 150, 128, 128);
            prev.Save(Path.Combine(dir, "preview.png"), ImageFormat.Png);
        }
        Console.WriteLine("Created " + Path.Combine(dir, "StepLoop.ico"));
    }
}
