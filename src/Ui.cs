using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StepLoop
{
    // ---------- tema e palette ----------

    public static class NativeUi
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string subApp, string subIdList);

        public static void DarkTitleBar(IntPtr hwnd, bool dark)
        {
            try
            {
                int v = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref v, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref v, 4);
            }
            catch (Exception) { }
        }

        public static void DarkScroll(IntPtr hwnd, bool dark)
        {
            try { SetWindowTheme(hwnd, dark ? "DarkMode_Explorer" : null, null); }
            catch (Exception) { }
        }
    }

    // Flag del tema corrente (letto da AppSettings e da tutti i controlli).
    public static class Theme
    {
        public static bool Dark = true;
    }

    // Colori semantici dell'interfaccia. Il tema scuro segue il mockup; il chiaro è una variante morbida (niente bianco acceso).
    public static class Pal
    {
        static Color C(int dark, int light)
        {
            int v = Theme.Dark ? dark : light;
            return Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
        }

        public static Color Bg { get { return C(0x121418, 0xDFE2E8); } }
        public static Color Bar { get { return C(0x171A20, 0xE9EBF0); } }
        public static Color Panel { get { return C(0x1A1D23, 0xEEF0F4); } }
        public static Color Border { get { return C(0x262B36, 0xC9CED8); } }
        public static Color Raised { get { return C(0x212734, 0xE3E6EC); } }
        public static Color RaisedHover { get { return C(0x2A3140, 0xD6DBE4); } }
        public static Color RaisedBorder { get { return C(0x2F3645, 0xB9C0CC); } }
        public static Color Field { get { return C(0x12151B, 0xF7F8FA); } }
        public static Color FieldBorder { get { return C(0x2A2F3A, 0xB9C0CC); } }
        public static Color Focus { get { return C(0x4F8FE6, 0x2F6FD0); } }
        public static Color Text { get { return C(0xE4E7EC, 0x1D2129); } }
        public static Color TextStrong { get { return C(0xE8EBF0, 0x12151B); } }
        public static Color Text2 { get { return C(0xC3C9D3, 0x3A414D); } }
        public static Color Muted { get { return C(0x8B93A1, 0x5F6775); } }
        public static Color Faint { get { return C(0x7A8394, 0x6C7482); } }
        public static Color Pending { get { return C(0x6B7384, 0x8A92A0); } }
        public static Color HeaderRow { get { return C(0x171D29, 0xE2E7F0); } }
        public static Color RowHover { get { return C(0x20252F, 0xE4E8EF); } }
        public static Color SelRow { get { return C(0x1F2C44, 0xCFDDF5); } }
        public static Color RunRing { get { return C(0x2B4163, 0x9FB8E0); } }
        public static Color Guide { get { return C(0x2A303C, 0xC3C9D4); } }
        public static Color LogBg { get { return C(0x0E1013, 0xE8EAEE); } }
        public static Color LogSep { get { return C(0x1C2028, 0xD2D6DE); } }
        public static Color ProgressTrack { get { return C(0x2A3550, 0xBCC8DC); } }

        public static Color Start { get { return Color.FromArgb(0x2F, 0x8F, 0x5B); } }
        public static Color StartHover { get { return Color.FromArgb(0x38, 0xA0, 0x68); } }
        public static Color Stop { get { return Color.FromArgb(0xB8, 0x3F, 0x36); } }
        public static Color StopHover { get { return Color.FromArgb(0xC9, 0x4B, 0x41); } }
        public static Color Primary { get { return Color.FromArgb(0x2F, 0x6F, 0xD0); } }

        public static Color Ok { get { return C(0x62D49D, 0x1F7A44); } }
        public static Color Warn { get { return C(0xF3B25E, 0x9A5E00); } }
        public static Color Danger { get { return C(0xF08A80, 0xB3382E); } }
        public static Color Info { get { return C(0x8DBFFF, 0x1F5BBF); } }

        // colore pieno (pallino) e testo del badge per ogni famiglia
        public static Color Dot(Sem s)
        {
            switch (s)
            {
                case Sem.Input: return C(0x4CC38A, 0x2E9E5B);
                case Sem.Flow: return C(0x5AA2FF, 0x2F6FD0);
                case Sem.Cond: return C(0xF0A03C, 0xC47A12);
                case Sem.Wait: return C(0xA98BF0, 0x7A55D6);
                default: return C(0xEF6A5E, 0xD0453B);
            }
        }

        public static Color BadgeFg(Sem s)
        {
            switch (s)
            {
                case Sem.Input: return C(0x62D49D, 0x1F7A44);
                case Sem.Flow: return C(0x8DBFFF, 0x1F5BBF);
                case Sem.Cond: return C(0xF3B25E, 0x9A5E00);
                case Sem.Wait: return C(0xC2AAF6, 0x5E3FB8);
                default: return C(0xF08A80, 0xB3382E);
            }
        }

        public static Color BadgeBg(Sem s, Color under)
        {
            double a = s == Sem.Wait ? 0.18 : (s == Sem.Input ? 0.15 : 0.16);
            return Over(under, Dot(s), Theme.Dark ? a : a + 0.06);
        }

        // sovrappone "top" con trasparenza alpha a "under"
        public static Color Over(Color under, Color top, double alpha)
        {
            alpha = Math.Max(0, Math.Min(1, alpha));
            return Color.FromArgb((int)(under.R + (top.R - under.R) * alpha), (int)(under.G + (top.G - under.G) * alpha), (int)(under.B + (top.B - under.B) * alpha));
        }
    }

    // ---------- utilità di disegno ----------

    public static class Ui
    {
        public static float Scale = 1f;
        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        public static void InitScale()
        {
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Scale = Math.Max(1f, g.DpiX / 96f); }
            catch (Exception) { Scale = 1f; }
        }

        public static int S(int px) { return (int)Math.Round(px * Scale); }

        public static Font F(float pt, bool bold)
        {
            string key = "U" + pt + (bold ? "b" : "");
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                f = new Font("Segoe UI", pt, bold ? FontStyle.Bold : FontStyle.Regular);
                fonts[key] = f;
            }
            return f;
        }

        public static Font F(float pt) { return F(pt, false); }

        public static Font M(float pt, bool bold)
        {
            string key = "M" + pt + (bold ? "b" : "");
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                f = new Font("Consolas", pt, bold ? FontStyle.Bold : FontStyle.Regular);
                fonts[key] = f;
            }
            return f;
        }

        public static Font M(float pt) { return M(pt, false); }

        // Colore dietro un controllo: risale i contenitori saltando quelli trasparenti.
        public static Color UnderColor(Control c)
        {
            Control p = c.Parent;
            while (p != null)
            {
                Card card = p as Card;
                if (card != null) return card.Page ? Pal.Bg : Pal.Panel;
                Surface sf = p as Surface;
                if (sf != null) return sf.BackColor;
                if (p is Form) return Pal.Bg;
                if (p.BackColor.A == 255 && p.BackColor != SystemColors.Control) return p.BackColor;
                p = p.Parent;
            }
            return Pal.Bg;
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            int d = Math.Max(2, radius * 2);
            d = Math.Min(d, Math.Min(r.Width, r.Height));
            GraphicsPath p = new GraphicsPath();
            if (d <= 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color fill)
        {
            using (GraphicsPath gp = Round(r, radius))
            using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, gp);
        }

        public static void DrawRound(Graphics g, Rectangle r, int radius, Color line, float width)
        {
            using (GraphicsPath gp = Round(r, radius))
            using (Pen p = new Pen(line, width)) g.DrawPath(p, gp);
        }

        public const TextFormatFlags Left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
            | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        public const TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
            | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        public const TextFormatFlags Right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
            | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

        public static void Text(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags flags)
        {
            if (string.IsNullOrEmpty(s)) return;
            TextRenderer.DrawText(g, s, f, r, c, flags);
        }

        public static int Measure(Graphics g, string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return TextRenderer.MeasureText(g, s, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
        }

        public static string Mmss(long ms)
        {
            long s = ms / 1000;
            long h = s / 3600, m = (s / 60) % 60, sec = s % 60;
            return h > 0 ? string.Format("{0:00}:{1:00}:{2:00}", h, m, sec) : string.Format("{0:00}:{1:00}", m, sec);
        }

        public static string Hms(long ms)
        {
            long s = ms / 1000;
            return string.Format("{0:00}:{1:00}:{2:00}", s / 3600, (s / 60) % 60, s % 60);
        }

        // Secondi con un decimale: "3.2 s" (o ms sotto il secondo).
        public static string Secs(long ms)
        {
            if (ms < 1000) return ms + " ms";
            return (ms / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s";
        }
    }

    // ---------- icone disegnate a mano ----------

    public enum Glyph
    {
        None, Copy, Trash, Up, Down, Play, Stop, Pause, Rec, Save, Open, Bulb, Theme, Plus, Crosshair, Chevron, Check, Eye, Close
    }

    public static class Icons
    {
        // Disegna l'icona dentro il quadrato r, con il colore dato.
        public static void Draw(Graphics g, Glyph glyph, Rectangle r, Color c)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(r.Width, r.Height);
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
            float u = s / 16f;                 // unità: l'icona vive in una griglia 16x16
            float lw = Math.Max(1.3f, 1.5f * u);
            using (Pen pen = new Pen(c, lw))
            using (SolidBrush br = new SolidBrush(c))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                switch (glyph)
                {
                    case Glyph.Copy:
                        g.DrawRectangle(pen, cx - 6 * u, cy - 3 * u, 8 * u, 9 * u);
                        g.DrawLines(pen, new PointF[] { new PointF(cx - 2 * u, cy - 6 * u), new PointF(cx + 6 * u, cy - 6 * u), new PointF(cx + 6 * u, cy + 3 * u) });
                        break;
                    case Glyph.Trash:
                        g.DrawLine(pen, cx - 6 * u, cy - 4 * u, cx + 6 * u, cy - 4 * u);
                        g.DrawLine(pen, cx - 2 * u, cy - 4 * u, cx - 2 * u, cy - 6.5f * u);
                        g.DrawLine(pen, cx + 2 * u, cy - 4 * u, cx + 2 * u, cy - 6.5f * u);
                        g.DrawLine(pen, cx - 2 * u, cy - 6.5f * u, cx + 2 * u, cy - 6.5f * u);
                        g.DrawLines(pen, new PointF[] { new PointF(cx - 4.5f * u, cy - 4 * u), new PointF(cx - 3.8f * u, cy + 6 * u), new PointF(cx + 3.8f * u, cy + 6 * u), new PointF(cx + 4.5f * u, cy - 4 * u) });
                        g.DrawLine(pen, cx - 1.2f * u, cy - 1.5f * u, cx - 1.2f * u, cy + 3.5f * u);
                        g.DrawLine(pen, cx + 1.2f * u, cy - 1.5f * u, cx + 1.2f * u, cy + 3.5f * u);
                        break;
                    case Glyph.Up:
                        g.FillPolygon(br, new PointF[] { new PointF(cx, cy - 4 * u), new PointF(cx + 5 * u, cy + 3 * u), new PointF(cx - 5 * u, cy + 3 * u) });
                        break;
                    case Glyph.Down:
                        g.FillPolygon(br, new PointF[] { new PointF(cx, cy + 4 * u), new PointF(cx + 5 * u, cy - 3 * u), new PointF(cx - 5 * u, cy - 3 * u) });
                        break;
                    case Glyph.Play:
                        g.FillPolygon(br, new PointF[] { new PointF(cx - 4 * u, cy - 5.5f * u), new PointF(cx + 5.5f * u, cy), new PointF(cx - 4 * u, cy + 5.5f * u) });
                        break;
                    case Glyph.Stop:
                        g.FillRectangle(br, cx - 4.5f * u, cy - 4.5f * u, 9 * u, 9 * u);
                        break;
                    case Glyph.Pause:
                        g.FillRectangle(br, cx - 4.5f * u, cy - 5 * u, 3 * u, 10 * u);
                        g.FillRectangle(br, cx + 1.5f * u, cy - 5 * u, 3 * u, 10 * u);
                        break;
                    case Glyph.Rec:
                        g.FillEllipse(br, cx - 4.5f * u, cy - 4.5f * u, 9 * u, 9 * u);
                        break;
                    case Glyph.Save:
                        g.DrawRectangle(pen, cx - 6 * u, cy - 6 * u, 12 * u, 12 * u);
                        g.DrawRectangle(pen, cx - 3 * u, cy - 6 * u, 6 * u, 4 * u);
                        g.DrawRectangle(pen, cx - 3.5f * u, cy + 1 * u, 7 * u, 5 * u);
                        break;
                    case Glyph.Open:
                        g.DrawLines(pen, new PointF[] { new PointF(cx - 7 * u, cy + 5 * u), new PointF(cx - 7 * u, cy - 5 * u), new PointF(cx - 2 * u, cy - 5 * u), new PointF(cx, cy - 3 * u), new PointF(cx + 6 * u, cy - 3 * u), new PointF(cx + 6 * u, cy + 5 * u), new PointF(cx - 7 * u, cy + 5 * u) });
                        break;
                    case Glyph.Bulb:
                        {
                            float rad = 4.6f * u;
                            g.DrawEllipse(pen, cx - rad, cy - 3.2f * u - rad, rad * 2, rad * 2);
                            g.DrawLine(pen, cx - 2.2f * u, cy + 3.6f * u, cx + 2.2f * u, cy + 3.6f * u);
                            g.DrawLine(pen, cx - 1.6f * u, cy + 6 * u, cx + 1.6f * u, cy + 6 * u);
                            break;
                        }
                    case Glyph.Theme:
                        {
                            float rad = 5.5f * u;
                            RectangleF cr = new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2);
                            g.FillPie(br, Rectangle.Round(cr), 90, 180);
                            g.DrawEllipse(pen, cr);
                            break;
                        }
                    case Glyph.Plus:
                        g.DrawLine(pen, cx - 5 * u, cy, cx + 5 * u, cy);
                        g.DrawLine(pen, cx, cy - 5 * u, cx, cy + 5 * u);
                        break;
                    case Glyph.Crosshair:
                        g.DrawEllipse(pen, cx - 4.5f * u, cy - 4.5f * u, 9 * u, 9 * u);
                        g.DrawLine(pen, cx - 7 * u, cy, cx - 2.5f * u, cy);
                        g.DrawLine(pen, cx + 2.5f * u, cy, cx + 7 * u, cy);
                        g.DrawLine(pen, cx, cy - 7 * u, cx, cy - 2.5f * u);
                        g.DrawLine(pen, cx, cy + 2.5f * u, cx, cy + 7 * u);
                        break;
                    case Glyph.Chevron:
                        g.DrawLines(pen, new PointF[] { new PointF(cx - 2 * u, cy - 4 * u), new PointF(cx + 2.5f * u, cy), new PointF(cx - 2 * u, cy + 4 * u) });
                        break;
                    case Glyph.Check:
                        g.DrawLines(pen, new PointF[] { new PointF(cx - 5 * u, cy), new PointF(cx - 1.5f * u, cy + 4 * u), new PointF(cx + 5.5f * u, cy - 4.5f * u) });
                        break;
                    case Glyph.Eye:
                        g.DrawEllipse(pen, cx - 7 * u, cy - 4 * u, 14 * u, 8 * u);
                        g.FillEllipse(br, cx - 2 * u, cy - 2 * u, 4 * u, 4 * u);
                        break;
                    case Glyph.Close:
                        g.DrawLine(pen, cx - 4 * u, cy - 4 * u, cx + 4 * u, cy + 4 * u);
                        g.DrawLine(pen, cx - 4 * u, cy + 4 * u, cx + 4 * u, cy - 4 * u);
                        break;
                }
            }
            g.SmoothingMode = old;
        }
    }

    // Controlli che hanno colori propri o campi nativi da aggiornare quando cambia il tema.
    public interface IThemed
    {
        void ApplyTheme();
    }

    public static class ThemeApplier
    {
        public static void Apply(Control root)
        {
            IThemed t = root as IThemed;
            if (t != null) t.ApplyTheme();
            root.Invalidate();
            foreach (Control c in root.Controls) Apply(c);
        }
    }

    // ---------- controlli di base ----------

    // Pannello con bordo sottile e angoli arrotondati.
    public class Card : Panel, IThemed
    {
        public int Radius = 8;
        public bool Accent;          // bordo evidenziato
        public bool Page;            // sfondo come la pagina (senza riempimento di card)

        public Card()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void ApplyTheme() { BackColor = Page ? Pal.Bg : Pal.Panel; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Pal.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Ui.FillRound(g, r, Ui.S(Radius), Page ? Pal.Bg : Pal.Panel);
            Ui.DrawRound(g, r, Ui.S(Radius), Accent ? Pal.Focus : Pal.Border, 1f);
        }
    }

    // Testo semplice con un "ruolo" (colore/font) che segue il tema da solo.
    public class UiLabel : Control
    {
        public enum Role { Normal, Muted, Section, Strong, Small, Mono, MonoMuted }
        public Role Style = Role.Normal;
        public ContentAlignment Align = ContentAlignment.MiddleLeft;
        public Color Override = Color.Empty;

        public UiLabel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Height = Ui.S(20);
        }

        public Font RoleFont
        {
            get
            {
                switch (Style)
                {
                    case Role.Section: return Ui.F(7.5f, true);
                    case Role.Strong: return Ui.F(9.5f, true);
                    case Role.Small: return Ui.F(8.25f);
                    case Role.Mono: return Ui.M(9.5f);
                    case Role.MonoMuted: return Ui.M(9f);
                    default: return Ui.F(9f);
                }
            }
        }

        Color RoleColor
        {
            get
            {
                if (!Override.IsEmpty) return Override;
                if (!Enabled) return Pal.Faint;
                switch (Style)
                {
                    case Role.Muted: case Role.Small: case Role.Section: case Role.MonoMuted: return Pal.Muted;
                    case Role.Strong: return Pal.TextStrong;
                    default: return Pal.Text;
                }
            }
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            string t = Style == Role.Section ? (Text ?? "").ToUpperInvariant() : Text;
            TextFormatFlags f = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            if (Align == ContentAlignment.MiddleRight) f |= TextFormatFlags.Right | TextFormatFlags.VerticalCenter;
            else if (Align == ContentAlignment.MiddleCenter) f |= TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            else if (Align == ContentAlignment.TopLeft) f |= TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak;
            else f |= TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, t, RoleFont, ClientRectangle, RoleColor, f);
        }
    }

    public class HLine : Control
    {
        public HLine()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Height = 1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Pal.Border);
        }
    }

    public class FlatButton : Control
    {
        public enum Look { Normal, Success, Danger, DangerSoft, Primary, Ghost }

        public Look Style = Look.Normal;
        public Glyph Icon = Glyph.None;
        public Color IconColor = Color.Empty;
        public Color TextColor = Color.Empty;
        public bool BoldText;
        public float FontPt = 9.5f;
        public int Radius = 6;
        bool hover;
        bool down;

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Height = Ui.S(32);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Focus(); Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Enter || keyData == Keys.Space || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color under = Ui.UnderColor(this);
            g.Clear(under);

            Color fill, border, fore;
            bool en = Enabled;
            switch (Style)
            {
                case Look.Success:
                    fill = en ? (hover ? Pal.StartHover : Pal.Start) : Pal.Raised; border = fill; fore = Color.White; break;
                case Look.Danger:
                    fill = en ? (hover ? Pal.StopHover : Pal.Stop) : Pal.Raised; border = fill; fore = Color.White; break;
                case Look.DangerSoft:
                    fill = Pal.Over(under, Pal.Dot(Sem.Exit), hover ? 0.2 : 0.12);
                    border = Pal.Over(under, Pal.Dot(Sem.Exit), 0.38);
                    fore = Pal.Danger; break;
                case Look.Primary:
                    fill = Pal.Over(under, Pal.Primary, hover ? 0.28 : 0.18); border = Pal.Primary; fore = Pal.Info; break;
                case Look.Ghost:
                    fill = hover && en ? Pal.Raised : under; border = fill; fore = Pal.Muted; break;
                default:
                    fill = en ? (hover ? Pal.RaisedHover : Pal.Raised) : Pal.Panel; border = Pal.RaisedBorder; fore = Pal.Text; break;
            }
            if (!en) { fore = Pal.Faint; if (Style != Look.Normal) { fill = Pal.Panel; border = Pal.RaisedBorder; } }
            if (down && en) fill = Pal.Over(fill, Color.Black, 0.18);
            if (!TextColor.IsEmpty && en) fore = TextColor;

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Ui.FillRound(g, r, Ui.S(Radius), fill);
            if (border != fill || Style == Look.Normal || Style == Look.DangerSoft || Style == Look.Primary)
                Ui.DrawRound(g, r, Ui.S(Radius), border, 1f);
            if (Focused && ShowFocusCues)
                Ui.DrawRound(g, new Rectangle(1, 1, Width - 3, Height - 3), Ui.S(Radius), Pal.Focus, 2f);

            Font f = Ui.F(FontPt, BoldText);
            int iconSize = Ui.S(16);
            Color ic = IconColor.IsEmpty ? fore : IconColor;
            string text = Text ?? "";
            if (Icon != Glyph.None && text.Length == 0)
            {
                Icons.Draw(g, Icon, new Rectangle((Width - iconSize) / 2, (Height - iconSize) / 2, iconSize, iconSize), en ? ic : Pal.Faint);
            }
            else if (Icon != Glyph.None)
            {
                int tw = Ui.Measure(g, text, f);
                int total = iconSize + Ui.S(6) + tw;
                int x = Math.Max(Ui.S(6), (Width - total) / 2);
                Icons.Draw(g, Icon, new Rectangle(x, (Height - iconSize) / 2, iconSize, iconSize), en ? ic : Pal.Faint);
                Ui.Text(g, text, f, new Rectangle(x + iconSize + Ui.S(6), 0, Math.Max(10, Width - x - iconSize - Ui.S(10)), Height), fore, Ui.Left);
            }
            else
            {
                Ui.Text(g, text, f, new Rectangle(Ui.S(4), 0, Width - Ui.S(8), Height), fore, Ui.Center);
            }
        }
    }

    // Campo di testo a tema: bordo sottile, anello di focus, testo nativo senza bordo all'interno.
    public class TextField : Control, IThemed
    {
        public readonly TextBox Box;
        public bool Mono;
        bool hover;

        public event EventHandler TextChangedLive;

        public TextField() : this(new TextBox()) { }

        public TextField(TextBox box)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Box = box;
            Box.BorderStyle = BorderStyle.None;
            Box.Font = Ui.F(9.5f);
            Controls.Add(Box);
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            Box.TextChanged += delegate
            {
                EventHandler h = TextChangedLive;
                if (h != null) h(this, EventArgs.Empty);
                OnTextChanged(EventArgs.Empty);
            };
            Height = Ui.S(30);
            ApplyTheme();
        }

        public override string Text
        {
            get { return Box.Text; }
            set { if (Box.Text != value) Box.Text = value ?? ""; }
        }

        public bool ReadOnly
        {
            get { return Box.ReadOnly; }
            set { Box.ReadOnly = value; Invalidate(); }
        }

        public Color TextColorOverride = Color.Empty;

        public void ApplyTheme()
        {
            Box.BackColor = Pal.Field;
            Box.ForeColor = TextColorOverride.IsEmpty ? Pal.Text : TextColorOverride;
            Box.Font = Mono ? Ui.M(9.5f) : Ui.F(9.5f);
            LayoutBox();
            Invalidate();
        }

        void LayoutBox()
        {
            int h = Box.PreferredHeight;
            int pad = Ui.S(8);
            Box.SetBounds(pad, Math.Max(1, (Height - h) / 2), Math.Max(10, Width - pad * 2), h);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutBox(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); LayoutBox(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Box.Focus(); base.OnMouseDown(e); }
        protected override void OnEnabledChanged(EventArgs e) { Box.Enabled = Enabled; Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color under = Ui.UnderColor(this);
            g.Clear(under);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Ui.FillRound(g, r, Ui.S(6), Enabled ? Pal.Field : Pal.Panel);
            if (Box.Focused) Ui.DrawRound(g, r, Ui.S(6), Pal.Focus, 2f);
            else Ui.DrawRound(g, r, Ui.S(6), hover ? Pal.RaisedBorder : Pal.FieldBorder, 1f);
        }
    }

    // Campo numerico a tema: modifica "dal vivo", frecce su/giù, rotella quando ha il focus.
    public class NumBox : Control, IThemed
    {
        readonly TextBox box = new TextBox();
        int value;
        int min;
        int max = 100000;
        int step = 1;
        bool hover;
        bool settingText;

        public event EventHandler ValueChanged;

        public NumBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            box.BorderStyle = BorderStyle.None;
            box.Font = Ui.M(9.5f);
            box.Text = "0";
            Controls.Add(box);
            box.GotFocus += delegate { Invalidate(); };
            box.LostFocus += delegate { Normalize(); Invalidate(); };
            box.TextChanged += delegate { if (!settingText) LiveCommit(); };
            box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Up) { Nudge(e.Shift ? 10 : 1); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down) { Nudge(e.Shift ? -10 : -1); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter) { Normalize(); box.SelectAll(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            box.MouseWheel += delegate(object s, MouseEventArgs e) { Nudge(e.Delta > 0 ? 1 : -1); };
            Height = Ui.S(30);
            ApplyTheme();
        }

        public int Value
        {
            get { return value; }
            set
            {
                int v = Math.Max(min, Math.Min(max, value));
                if (v == this.value && box.Text == v.ToString(CultureInfo.InvariantCulture)) return;
                bool changed = v != this.value;
                this.value = v;
                SetText();
                if (changed) RaiseChanged();
            }
        }

        public int Minimum { get { return min; } set { min = value; if (this.value < min) Value = min; } }
        public int Maximum { get { return max; } set { max = value; if (this.value > max) Value = max; } }
        public int Step { get { return step; } set { step = Math.Max(1, value); } }
        public bool ReadOnly { get { return box.ReadOnly; } set { box.ReadOnly = value; Invalidate(); } }

        void SetText()
        {
            settingText = true;
            try { box.Text = value.ToString(CultureInfo.InvariantCulture); }
            finally { settingText = false; }
            Invalidate();
        }

        void RaiseChanged()
        {
            EventHandler h = ValueChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        // Mentre si digita: se il testo è un numero valido nell'intervallo, si applica subito.
        void LiveCommit()
        {
            int v;
            if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return;
            if (v < min || v > max || v == value) return;
            value = v;
            RaiseChanged();
        }

        // All'uscita dal campo: testo non valido o fuori intervallo = si riporta al valore corrente / al limite.
        void Normalize()
        {
            int v;
            if (int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
            {
                v = Math.Max(min, Math.Min(max, v));
                bool changed = v != value;
                value = v;
                SetText();
                if (changed) RaiseChanged();
            }
            else SetText();
        }

        void Nudge(int dir)
        {
            if (ReadOnly || !Enabled) return;
            Normalize();
            Value = value + dir * step;
            box.SelectionStart = box.TextLength;
        }

        public void ApplyTheme()
        {
            box.BackColor = Pal.Field;
            box.ForeColor = Pal.Text;
            LayoutBox();
            Invalidate();
        }

        int SpinW { get { return Ui.S(15); } }

        void LayoutBox()
        {
            int h = box.PreferredHeight;
            int pad = Ui.S(8);
            box.SetBounds(pad, Math.Max(1, (Height - h) / 2), Math.Max(10, Width - pad - SpinW - Ui.S(2)), h);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutBox(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { box.Enabled = Enabled; Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.X >= Width - SpinW - 1 && !ReadOnly && Enabled)
            {
                box.Focus();
                Nudge(e.Y < Height / 2 ? 1 : -1);
            }
            else box.Focus();
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (box.Focused) Nudge(e.Delta > 0 ? 1 : -1);
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color under = Ui.UnderColor(this);
            g.Clear(under);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Ui.FillRound(g, r, Ui.S(6), Enabled ? Pal.Field : Pal.Panel);
            if (box.Focused) Ui.DrawRound(g, r, Ui.S(6), Pal.Focus, 2f);
            else Ui.DrawRound(g, r, Ui.S(6), hover ? Pal.RaisedBorder : Pal.FieldBorder, 1f);
            if (!ReadOnly && Enabled && (hover || box.Focused))
            {
                int x = Width - SpinW - 2;
                Color c = Pal.Muted;
                using (SolidBrush b = new SolidBrush(c))
                {
                    float cx = x + SpinW / 2f;
                    float u = Ui.S(1);
                    g.FillPolygon(b, new PointF[] { new PointF(cx, Height / 2f - 7 * u / 1.2f), new PointF(cx + 3.2f * u, Height / 2f - 2.2f * u), new PointF(cx - 3.2f * u, Height / 2f - 2.2f * u) });
                    g.FillPolygon(b, new PointF[] { new PointF(cx, Height / 2f + 7 * u / 1.2f), new PointF(cx + 3.2f * u, Height / 2f + 2.2f * u), new PointF(cx - 3.2f * u, Height / 2f + 2.2f * u) });
                }
            }
        }
    }

    // Casella di controllo a tema.
    public class CheckField : Control
    {
        bool isChecked;
        bool hover;
        public event EventHandler CheckedChanged;

        public CheckField()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            TabStop = true;
            Cursor = Cursors.Hand;
            Height = Ui.S(24);
        }

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                Invalidate();
                EventHandler h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnClick(EventArgs e)
        {
            if (Enabled) { Focus(); Checked = !Checked; }
            base.OnClick(e);
        }

        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Space || base.IsInputKey(keyData); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Enabled && e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int b = Ui.S(16);
            Rectangle box = new Rectangle(0, (Height - b) / 2, b, b);
            Color fill = isChecked ? (Enabled ? Pal.Focus : Pal.Faint) : Pal.Field;
            Ui.FillRound(g, box, Ui.S(4), fill);
            Ui.DrawRound(g, box, Ui.S(4), isChecked ? fill : (hover && Enabled ? Pal.Muted : Pal.FieldBorder), 1.2f);
            if (isChecked) Icons.Draw(g, Glyph.Check, box, Color.White);
            if (Focused && ShowFocusCues) Ui.DrawRound(g, new Rectangle(-1, box.Y - 1, b + 2, b + 2), Ui.S(5), Pal.Focus, 1.5f);
            Ui.Text(g, Text, Ui.F(9.5f), new Rectangle(b + Ui.S(8), 0, Width - b - Ui.S(8), Height), Enabled ? Pal.Text2 : Pal.Faint, Ui.Left);
        }
    }

    // Interruttore (es. "segue lo step attivo").
    public class ToggleSwitch : Control
    {
        bool isChecked;
        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Size = new Size(Ui.S(32), Ui.S(18));
        }

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                Invalidate();
                EventHandler h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Ui.FillRound(g, r, Height / 2, isChecked ? Color.FromArgb(0x2F, 0x6F, 0xD0) : Pal.RaisedBorder);
            int d = Height - Ui.S(6);
            int x = isChecked ? Width - d - Ui.S(3) : Ui.S(3);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(0xE8, 0xEB, 0xF0))) g.FillEllipse(b, x, Ui.S(3), d, d);
        }
    }

    // Due o più segmenti affiancati (es. Sincrono | Random).
    public class Segmented : Control
    {
        public string[] Items = new string[0];
        int selected;
        public event EventHandler SelectedIndexChanged;

        public Segmented()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Height = Ui.S(30);
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                Invalidate();
                EventHandler h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public int PreferredWidth(Graphics g)
        {
            int w = 0;
            foreach (string s in Items) w += Ui.Measure(g, s, Ui.F(9f, true)) + Ui.S(26);
            return w;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Items.Length == 0 || !Enabled) return;
            int seg = Math.Min(Items.Length - 1, Math.Max(0, e.X * Items.Length / Math.Max(1, Width)));
            SelectedIndex = seg;
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color under = Ui.UnderColor(this);
            g.Clear(under);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Ui.FillRound(g, r, Ui.S(6), Pal.Field);
            Ui.DrawRound(g, r, Ui.S(6), Pal.FieldBorder, 1f);
            if (Items.Length == 0) return;
            int sw = Width / Items.Length;
            for (int i = 0; i < Items.Length; i++)
            {
                Rectangle seg = new Rectangle(i * sw + 2, 2, sw - 4 + (i == Items.Length - 1 ? Width - sw * Items.Length : 0), Height - 5);
                bool on = i == selected;
                if (on) Ui.FillRound(g, seg, Ui.S(4), Enabled ? Pal.Primary : Pal.Faint);
                Ui.Text(g, Items[i], Ui.F(9f, on), seg, on ? Color.White : (Enabled ? Pal.Muted : Pal.Faint), Ui.Center);
            }
        }
    }

    // ---------- elenco a discesa a tema ----------

    sealed class PopupRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Pal.Raised)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen p = new Pen(Pal.RaisedBorder))
                e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
    }

    sealed class PopupList : Control
    {
        public List<string> Items;
        public int Selected;
        int hot = -1;
        public Action<int> Picked;

        public PopupList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand;
        }

        public int RowH { get { return Ui.S(28); } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = e.Y / RowH;
            if (h != hot) { hot = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            int i = e.Y / RowH;
            if (i >= 0 && i < Items.Count && Picked != null) Picked(i);
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Pal.Raised);
            for (int i = 0; i < Items.Count; i++)
            {
                Rectangle r = new Rectangle(Ui.S(2), i * RowH + Ui.S(2), Width - Ui.S(4), RowH - Ui.S(2));
                if (i == hot) Ui.FillRound(g, r, Ui.S(4), Pal.Focus);
                else if (i == Selected) Ui.FillRound(g, r, Ui.S(4), Pal.RaisedHover);
                Ui.Text(g, Items[i], Ui.F(9.5f), new Rectangle(r.X + Ui.S(8), r.Y, r.Width - Ui.S(12), r.Height),
                    i == hot ? Color.White : Pal.Text, Ui.Left);
            }
        }
    }

    public class DarkCombo : Control
    {
        public readonly List<string> Items = new List<string>();
        int selected = -1;
        bool hover;
        ToolStripDropDown drop;
        public event EventHandler SelectedIndexChanged;
        public bool Mono;

        public DarkCombo()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Height = Ui.S(30);
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                int v = Math.Max(-1, Math.Min(Items.Count - 1, value));
                if (v == selected) return;
                selected = v;
                Invalidate();
                EventHandler h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public string SelectedText { get { return selected >= 0 && selected < Items.Count ? Items[selected] : ""; } }

        public void SetItems(IEnumerable<string> items)
        {
            Items.Clear();
            Items.AddRange(items);
            if (selected >= Items.Count) selected = Items.Count - 1;
            Invalidate();
        }

        public int IndexOfText(string text)
        {
            return Items.IndexOf(text);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Space || keyData == Keys.Enter || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!Enabled) return;
            if (e.KeyCode == Keys.Down) { SelectedIndex = selected + 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { SelectedIndex = selected - 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { ShowList(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Enabled) { Focus(); ShowList(); }
            base.OnMouseDown(e);
        }

        public void ShowList()
        {
            if (Items.Count == 0) return;
            if (drop != null) { drop.Close(); drop = null; return; }
            PopupList list = new PopupList();
            list.Items = Items;
            list.Selected = selected;
            list.Width = Math.Max(Width, Ui.S(120));
            list.Height = Items.Count * list.RowH + Ui.S(4);
            ToolStripDropDown dd = new ToolStripDropDown();
            dd.Renderer = new PopupRenderer();
            dd.Padding = Padding.Empty;
            dd.Margin = Padding.Empty;
            dd.AutoSize = true;
            ToolStripControlHost host = new ToolStripControlHost(list);
            host.Margin = Padding.Empty;
            host.Padding = Padding.Empty;
            host.AutoSize = false;
            host.Size = list.Size;
            dd.Items.Add(host);
            drop = dd;
            list.Picked = delegate(int i)
            {
                dd.Close();
                SelectedIndex = i;
            };
            dd.Closed += delegate { if (drop == dd) drop = null; Invalidate(); };
            dd.Show(this, new Point(0, Height + 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color under = Ui.UnderColor(this);
            g.Clear(under);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Ui.FillRound(g, r, Ui.S(6), Enabled ? Pal.Field : Pal.Panel);
            if (Focused && ShowFocusCues) Ui.DrawRound(g, r, Ui.S(6), Pal.Focus, 2f);
            else Ui.DrawRound(g, r, Ui.S(6), hover && Enabled ? Pal.RaisedBorder : Pal.FieldBorder, 1f);
            int pad = Ui.S(10);
            Ui.Text(g, SelectedText, Mono ? Ui.M(9.5f) : Ui.F(9.5f), new Rectangle(pad, 0, Width - pad - Ui.S(26), Height),
                Enabled ? Pal.Text : Pal.Faint, Ui.Left);
            // freccia
            float cx = Width - Ui.S(14), cy = Height / 2f;
            float u = Ui.S(1);
            using (Pen p = new Pen(Pal.Muted, Math.Max(1.4f, 1.5f * u)))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLines(p, new PointF[] { new PointF(cx - 3.5f * u, cy - 1.5f * u), new PointF(cx, cy + 2 * u), new PointF(cx + 3.5f * u, cy - 1.5f * u) });
            }
        }
    }

    // Linguette delle schede ("Sequenza" / "Riavvio partita") nella barra superiore.
    public class TabStrip : Control
    {
        public List<string> Tabs = new List<string>();
        int selected;
        int hot = -1;
        public event EventHandler SelectedIndexChanged;

        public TabStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Height = Ui.S(34);
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                Invalidate();
                EventHandler h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        Rectangle TabRect(Graphics g, int i)
        {
            int x = 0;
            for (int k = 0; k < i; k++) x += Ui.Measure(g, Tabs[k], Ui.F(9.5f, true)) + Ui.S(28 + 4);
            return new Rectangle(x, 1, Ui.Measure(g, Tabs[i], Ui.F(9.5f, true)) + Ui.S(28), Height - 2);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = -1;
            using (Graphics g = CreateGraphics())
                for (int i = 0; i < Tabs.Count; i++) if (TabRect(g, i).Contains(e.Location)) { h = i; break; }
            if (h != hot) { hot = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            using (Graphics g = CreateGraphics())
                for (int i = 0; i < Tabs.Count; i++)
                    if (TabRect(g, i).Contains(e.Location)) { SelectedIndex = i; break; }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Pal.Bar);
            for (int i = 0; i < Tabs.Count; i++)
            {
                Rectangle r = TabRect(g, i);
                bool on = i == selected;
                if (on)
                {
                    Ui.FillRound(g, r, Ui.S(6), Pal.Raised);
                    Ui.DrawRound(g, r, Ui.S(6), Pal.RaisedBorder, 1f);
                }
                else if (i == hot) Ui.FillRound(g, r, Ui.S(6), Pal.Panel);
                Ui.Text(g, Tabs[i], Ui.F(9.5f, on), r, on ? Pal.Text : Pal.Muted, Ui.Center);
            }
        }
    }

    // Pillola di stato in alto a destra (Fermo / In esecuzione / In pausa).
    public class StatusPill : Control
    {
        public enum State { Stopped, Running, Paused }
        State state = State.Stopped;

        public StatusPill()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Ui.S(28);
        }

        public State Mode { get { return state; } set { state = value; Invalidate(); } }

        public string Caption
        {
            get
            {
                switch (state)
                {
                    case State.Running: return L.T("In esecuzione", "Running");
                    case State.Paused: return L.T("In pausa", "Paused");
                    default: return L.T("Fermo", "Stopped");
                }
            }
        }

        public int PreferredWidth()
        {
            using (Graphics g = CreateGraphics()) return Ui.Measure(g, Caption, Ui.F(9f, true)) + Ui.S(46);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Pal.Bar);
            Color dot, fill, fore;
            switch (state)
            {
                case State.Running:
                    dot = Pal.Dot(Sem.Input); fore = Pal.Ok; fill = Pal.Over(Pal.Bar, Pal.Dot(Sem.Input), 0.16);
                    break;
                case State.Paused:
                    dot = Pal.Dot(Sem.Cond); fore = Pal.Warn; fill = Pal.Over(Pal.Bar, Pal.Dot(Sem.Cond), 0.16);
                    break;
                default:
                    dot = Pal.Muted; fore = Pal.Muted; fill = Pal.Raised;
                    break;
            }
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Ui.FillRound(g, r, Height / 2, fill);
            double pulse = state == State.Running ? 0.55 + 0.45 * Math.Cos(Environment.TickCount % 1200 / 1200.0 * 2 * Math.PI) : 1.0;
            Color dc = Pal.Over(fill, dot, Math.Max(0.2, Math.Min(1, pulse)));
            int d = Ui.S(8);
            using (SolidBrush b = new SolidBrush(dc)) g.FillEllipse(b, Ui.S(12), (Height - d) / 2, d, d);
            Ui.Text(g, Caption, Ui.F(9f, true), new Rectangle(Ui.S(26), 0, Width - Ui.S(30), Height), fore, Ui.Left);
        }
    }
}
