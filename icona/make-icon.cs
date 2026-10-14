using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Genera StepLoop.ico (16-256 px) e un'anteprima PNG.  Uso:  make-icon.exe <cartella-di-uscita>
// Disegno: quadrato arrotondato blu con una freccia circolare (il ciclo) e un triangolo "play" al centro (il passo).
static class MakeIcon
{
    static Bitmap Render(int s)
    {
        Bitmap bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            float S = s;

            // sfondo: quadrato arrotondato con sfumatura blu
            float m = Math.Max(0.5f, S * 0.02f);
            RectangleF r = new RectangleF(m, m, S - 2 * m, S - 2 * m);
            float rad = S * 0.23f;
            using (GraphicsPath bg = Round(r, rad))
            using (LinearGradientBrush br = new LinearGradientBrush(r, Color.FromArgb(0x4C, 0x8D, 0xFF), Color.FromArgb(0x1E, 0x45, 0xB8), 90f))
            {
                g.FillPath(br, bg);
                // leggero riflesso in alto
                using (GraphicsPath hi = Round(new RectangleF(r.X, r.Y, r.Width, r.Height * 0.5f), rad))
                using (LinearGradientBrush hb = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, r.Height * 0.5f),
                    Color.FromArgb(46, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                {
                    Region old = g.Clip;
                    g.SetClip(bg);
                    g.FillPath(hb, hi);
                    g.Clip = old;
                }
            }

            // freccia circolare (il ciclo)
            float cx = S / 2f, cy = S / 2f;
            float rr = S * 0.30f;
            float pw = Math.Max(1.6f, S * 0.105f);
            float a0 = 28f, sweep = 292f;                       // gradi, in senso orario (y verso il basso)
            using (Pen p = new Pen(Color.White, pw))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Flat;
                g.DrawArc(p, cx - rr, cy - rr, rr * 2, rr * 2, a0, sweep - 8f);
            }
            // punta della freccia nel punto finale, lungo la tangente
            double ea = (a0 + sweep) * Math.PI / 180.0;
            PointF end = new PointF(cx + rr * (float)Math.Cos(ea), cy + rr * (float)Math.Sin(ea));
            PointF tan = new PointF(-(float)Math.Sin(ea), (float)Math.Cos(ea));         // direzione di marcia
            PointF nor = new PointF((float)Math.Cos(ea), (float)Math.Sin(ea));           // verso l'esterno
            float hl = Math.Max(3f, S * 0.20f), hw = Math.Max(2.6f, S * 0.17f);
            PointF apex = new PointF(end.X + tan.X * hl * 0.55f, end.Y + tan.Y * hl * 0.55f);
            PointF b1 = new PointF(end.X - tan.X * hl * 0.45f + nor.X * hw, end.Y - tan.Y * hl * 0.45f + nor.Y * hw);
            PointF b2 = new PointF(end.X - tan.X * hl * 0.45f - nor.X * hw, end.Y - tan.Y * hl * 0.45f - nor.Y * hw);
            using (SolidBrush wb = new SolidBrush(Color.White))
                g.FillPolygon(wb, new PointF[] { apex, b1, b2 });

            // triangolo "play" al centro
            float ts = S * 0.17f;
            PointF[] tri = new PointF[]
            {
                new PointF(cx - ts * 0.55f, cy - ts * 0.95f),
                new PointF(cx - ts * 0.55f, cy + ts * 0.95f),
                new PointF(cx + ts * 0.95f, cy)
            };
            using (SolidBrush gb = new SolidBrush(Color.FromArgb(0x7B, 0xF2, 0xA8)))
                g.FillPolygon(gb, tri);
        }
        return bmp;
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

    // Immagine ICO in formato DIB 32 bit (con canale alfa) per le dimensioni piccole.
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
            int maskRow = ((w + 31) / 32) * 4;
            bw.Write(new byte[maskRow * h]);
            return ms.ToArray();
        }
    }

    static byte[] Png(Bitmap b)
    {
        using (MemoryStream ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    static void Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(dir);
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
        List<byte[]> data = new List<byte[]>();
        foreach (int s in sizes)
        {
            using (Bitmap b = Render(s)) data.Add(s >= 256 ? Png(b) : Dib(b));
        }
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

        // anteprima: 256 grande + piccole su fondo scuro e chiaro
        using (Bitmap prev = new Bitmap(640, 300))
        using (Graphics g = Graphics.FromImage(prev))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush dk = new SolidBrush(Color.FromArgb(0x16, 0x19, 0x1F))) g.FillRectangle(dk, 0, 0, 320, 300);
            using (SolidBrush lt = new SolidBrush(Color.FromArgb(0xEE, 0xF0, 0xF4))) g.FillRectangle(lt, 320, 0, 320, 300);
            using (Bitmap big = Render(256)) g.DrawImage(big, 32, 22, 256, 256);
            int x = 340;
            foreach (int s in new int[] { 64, 48, 32, 24, 16 })
            {
                using (Bitmap b = Render(s)) g.DrawImage(b, x, 40 + (64 - s) / 2, s, s);
                x += s + 16;
            }
            using (Bitmap b2 = Render(128)) g.DrawImage(b2, 360, 130, 128, 128);
            prev.Save(Path.Combine(dir, "anteprima.png"), ImageFormat.Png);
        }
        Console.WriteLine("Creato " + Path.Combine(dir, "StepLoop.ico"));
    }
}
