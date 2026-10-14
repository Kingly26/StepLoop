using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StepLoop
{
    // Contenitore con sfondo del tema e, se serve, una linea sul bordo.
    public sealed class Surface : Panel, IThemed
    {
        public enum Kind { Bg, Bar, Panel }

        public Kind Look;
        public bool BottomLine;
        public bool TopLine;

        public Surface(Kind look)
        {
            Look = look;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            ApplyTheme();
        }

        public void ApplyTheme()
        {
            BackColor = Look == Kind.Bg ? Pal.Bg : (Look == Kind.Bar ? Pal.Bar : Pal.Panel);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            using (Pen p = new Pen(Pal.Border))
            {
                if (BottomLine) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
                if (TopLine) g.DrawLine(p, 0, 0, Width, 0);
            }
        }

        // ---- scrollbar nativa a tema ----
        // Il tema scuro della scrollbar (SetWindowTheme) vale solo per la scrollbar già presente: quando compare dopo un
        // ridimensionamento torna chiara. Per questo il tema si riapplica ogni volta che la scrollbar compare o sparisce.
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        const int WM_NCCALCSIZE = 0x0083;
        const int GWL_STYLE = -16;
        const int WS_SCROLLBARS = 0x00200000 | 0x00100000;
        const uint SWP_FLAGS = 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020;   // NOSIZE | NOMOVE | NOZORDER | NOACTIVATE | FRAMECHANGED

        int scrollBits = -1;
        bool themeQueued;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            scrollBits = -1;
            if (AutoScroll) NativeUi.DarkScroll(Handle, Theme.Dark);
        }

        // Riapplica il tema alla scrollbar nativa (anche dopo un cambio di tema dell'app).
        public void ApplyScrollTheme()
        {
            if (!IsHandleCreated || IsDisposed) return;
            NativeUi.DarkScroll(Handle, Theme.Dark);
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_FLAGS);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_NCCALCSIZE && AutoScroll && IsHandleCreated)
            {
                int bits = GetWindowLong(Handle, GWL_STYLE) & WS_SCROLLBARS;
                if (bits != scrollBits)
                {
                    scrollBits = bits;
                    if (bits != 0 && !themeQueued)
                    {
                        // non dentro il calcolo del frame: si rimanda a subito dopo
                        themeQueued = true;
                        try
                        {
                            BeginInvoke(new Action(delegate
                            {
                                themeQueued = false;
                                ApplyScrollTheme();
                            }));
                        }
                        catch (Exception) { themeQueued = false; }
                    }
                }
            }
        }
    }

    // Etichetta + campo (etichetta sopra, come nel mockup).
    public sealed class FieldBox : Control
    {
        readonly UiLabel cap = new UiLabel();
        public readonly Control Field;

        public FieldBox(Control field, bool sectionStyle)
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.Transparent;
            Field = field;
            cap.Style = sectionStyle ? UiLabel.Role.Section : UiLabel.Role.Muted;
            cap.Height = Ui.S(16);
            Controls.Add(cap);
            Controls.Add(field);
            Height = Ui.S(18) + field.Height + Ui.S(2);
        }

        public string Caption
        {
            get { return cap.Text; }
            set { cap.Text = value; }
        }

        public int NaturalHeight { get { return Ui.S(18) + Field.Height + Ui.S(2); } }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            cap.SetBounds(0, 0, Width, Ui.S(16));
            Field.SetBounds(0, Ui.S(18), Width, Field.Height);
        }
    }

    // Distribuisce i figli in una riga, con larghezze proporzionali ai pesi.
    public sealed class RowPanel : Panel
    {
        public float[] Weights;
        public int[] FixedWidths;      // larghezza fissa in px (0 = usa il peso)
        public int Gap = 10;

        public RowPanel()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.Transparent;
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            List<int> vis = new List<int>();
            for (int i = 0; i < Controls.Count; i++) if (Controls[i].Visible) vis.Add(i);
            int n = vis.Count;
            if (n == 0) return;
            int gap = Ui.S(Gap);
            float total = 0;
            int fixedTotal = 0;
            foreach (int i in vis)
            {
                int fw = FixedWidths != null && i < FixedWidths.Length ? FixedWidths[i] : 0;
                if (fw > 0) fixedTotal += Ui.S(fw);
                else total += Weights != null && i < Weights.Length ? Weights[i] : 1f;
            }
            if (total <= 0) total = 1;
            float avail = Math.Max(1, Width - gap * (n - 1) - fixedTotal);
            float x = 0;
            foreach (int i in vis)
            {
                int fw = FixedWidths != null && i < FixedWidths.Length ? FixedWidths[i] : 0;
                int w;
                if (fw > 0) w = Ui.S(fw);
                else
                {
                    float wgt = Weights != null && i < Weights.Length ? Weights[i] : 1f;
                    w = (int)Math.Round(avail * wgt / total);
                }
                Controls[i].SetBounds((int)Math.Round(x), 0, w, Height);
                x += w + gap;
            }
        }
    }

    // Campione di colore (anteprima grande nell'ispettore).
    public sealed class SwatchBox : Control
    {
        Color color = Color.Black;
        public int Radius = 8;

        public SwatchBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public Color Value
        {
            get { return color; }
            set { color = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Ui.FillRound(g, r, Ui.S(Radius), color.IsEmpty ? Pal.Raised : color);
            Ui.DrawRound(g, r, Ui.S(Radius), Pal.Over(Pal.Panel, Color.White, 0.22), 1f);
        }
    }

    // Mini anteprima dell'area scelta (istantanea dello schermo ingrandita senza sfumature).
    public sealed class AreaPreview : Control
    {
        Bitmap image;
        string caption = "";

        public AreaPreview()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public void Set(Bitmap bmp, string text)
        {
            if (image != null) image.Dispose();
            image = bmp;
            caption = text ?? "";
            Invalidate();
        }

        public bool HasImage { get { return image != null; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Ui.FillRound(g, r, Ui.S(6), Pal.Field);
            if (image != null)
            {
                Rectangle inner = new Rectangle(Ui.S(4), Ui.S(4), Width - Ui.S(8), Height - Ui.S(8));
                float k = Math.Min(inner.Width / (float)image.Width, inner.Height / (float)image.Height);
                int w = Math.Max(1, (int)(image.Width * k)), h = Math.Max(1, (int)(image.Height * k));
                Rectangle dst = new Rectangle(inner.X + (inner.Width - w) / 2, inner.Y + (inner.Height - h) / 2, w, h);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(image, dst, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel);
                using (Pen p = new Pen(Pal.Over(Pal.Field, Color.White, 0.3))) g.DrawRectangle(p, dst.X, dst.Y, dst.Width - 1, dst.Height - 1);
            }
            else
            {
                Ui.Text(g, L.T("Nessuna anteprima: usa «Seleziona» o «Testa ora»", "No preview: use «Pick» or «Test now»"), Ui.F(8.25f),
                    new Rectangle(Ui.S(8), 0, Width - Ui.S(16), Height), Pal.Faint, Ui.Center);
            }
            Ui.DrawRound(g, r, Ui.S(6), Pal.FieldBorder, 1f);
            if (caption.Length > 0)
            {
                Rectangle cr = new Rectangle(Ui.S(6), Height - Ui.S(18), Width - Ui.S(12), Ui.S(14));
                Ui.Text(g, caption, Ui.M(8f), cr, Pal.Muted, Ui.Left);
            }
        }
    }

    // Intestazione dell'ispettore: badge del tipo + nome del passo.
    public sealed class InspHeader : Control
    {
        public string Badge = "";
        public string Title = "";
        public Sem Family = Sem.Input;
        public bool Marker;     // nessun passo selezionato

        public InspHeader()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Ui.S(40);
        }

        public void Set(string badge, string title, Sem family, bool none)
        {
            Badge = badge; Title = title; Family = family; Marker = none;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Pal.Panel);
            using (Pen p = new Pen(Pal.Border)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            int x = Ui.S(14);
            if (!Marker && Badge.Length > 0)
            {
                Font bf = Ui.M(7.5f, true);
                int bw = Math.Max(Ui.S(46), Ui.Measure(g, Badge, bf) + Ui.S(16));
                Rectangle br = new Rectangle(x, (Height - Ui.S(18)) / 2, bw, Ui.S(18));
                Ui.FillRound(g, br, Ui.S(4), Pal.BadgeBg(Family, Pal.Panel));
                Ui.Text(g, Badge, bf, br, Pal.BadgeFg(Family), Ui.Center);
                x = br.Right + Ui.S(10);
            }
            Ui.Text(g, Title, Ui.F(9.5f, true), new Rectangle(x, 0, Width - x - Ui.S(10), Height), Marker ? Pal.Muted : Pal.TextStrong, Ui.Left);
        }
    }

    // Barra del titolo di un pannello (testo a sinistra, testo a destra, linea sotto).
    public sealed class HeaderStrip : Control
    {
        public string LeftText = "";
        public string RightText = "";
        public Color RightColor = Color.Empty;

        public HeaderStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Ui.S(40);
        }

        public void Set(string left, string right, Color rightColor)
        {
            if (LeftText == left && RightText == right && RightColor == rightColor) return;
            LeftText = left; RightText = right; RightColor = rightColor;
            Invalidate();
        }

        public int RightReserve;      // spazio lasciato a destra a controlli figli (es. interruttore)

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Pal.Panel);
            using (Pen p = new Pen(Pal.Border)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            int px = Ui.S(14);
            int rw = Math.Min(Width / 2, Ui.Measure(g, RightText, Ui.F(9f)) + Ui.S(4));
            int rx = Width - px - RightReserve - rw;
            Ui.Text(g, (LeftText ?? "").ToUpperInvariant(), Ui.F(7.5f, true), new Rectangle(px, 0, Math.Max(10, rx - px - Ui.S(8)), Height), Pal.Muted, Ui.Left);
            if (!string.IsNullOrEmpty(RightText))
                Ui.Text(g, RightText, Ui.F(9f), new Rectangle(rx, 0, rw, Height), RightColor.IsEmpty ? Pal.Muted : RightColor, Ui.Right);
        }
    }
}
