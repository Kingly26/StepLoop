using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;


namespace StepLoop
{
    public class Recorder : IDisposable
    {
        const int WH_KEYBOARD_LL = 13;
        const int WH_MOUSE_LL = 14;
        const int WM_KEYDOWN = 0x0100;
        const int WM_KEYUP = 0x0101;
        const int WM_SYSKEYDOWN = 0x0104;
        const int WM_SYSKEYUP = 0x0105;
        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_RBUTTONDOWN = 0x0204;
        const int WM_MBUTTONDOWN = 0x0207;
        const int Probe = 3;

        public bool WaitBeforeClick = true;
        public bool RecordPauses = true;
        public bool RecordKeys = false;
        public Keys StopKey = Keys.F8;
        public Keys WaitKey = Keys.F9;
        public Func<Point, bool> IgnoreArea;
        public readonly List<MacroStep> Steps = new List<MacroStep>();

        public event Action Changed;
        public event Action StopRequested;

        IntPtr mouseHook = IntPtr.Zero;
        IntPtr keyHook = IntPtr.Zero;
        Native.HookProc mouseProc;
        Native.HookProc keyProc;
        int lastTick;
        bool hasLast;
        readonly Dictionary<Keys, int> keyDownAt = new Dictionary<Keys, int>();

        public bool Start()
        {
            mouseProc = MouseCallback;
            keyProc = KeyCallback;
            IntPtr module = Native.GetModuleHandle(null);
            mouseHook = Native.SetWindowsHookEx(WH_MOUSE_LL, mouseProc, module, 0);
            keyHook = Native.SetWindowsHookEx(WH_KEYBOARD_LL, keyProc, module, 0);
            if (mouseHook == IntPtr.Zero || keyHook == IntPtr.Zero)
            {
                Dispose();
                return false;
            }
            return true;
        }

        public void Dispose()
        {
            if (mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
            if (keyHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(keyHook); keyHook = IntPtr.Zero; }
        }

        IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
                {
                    Native.MSLLHOOKSTRUCT h = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                    Point p = new Point(h.pt.X, h.pt.Y);
                    bool injected = (h.flags & 1) != 0;
                    if (!injected && (IgnoreArea == null || !IgnoreArea(p)))
                        FeedClick(msg == WM_LBUTTONDOWN ? "Sinistro" : (msg == WM_RBUTTONDOWN ? "Destro" : "Centrale"), p, Environment.TickCount);
                }
            }
            return Native.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        IntPtr KeyCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                Native.KBDLLHOOKSTRUCT k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                bool injected = (k.flags & 0x10) != 0;
                int msg = wParam.ToInt32();
                bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
                Keys key = (Keys)k.vkCode;
                if (!injected && (down || up))
                {
                    if (key == StopKey)
                    {
                        if (down) { Action a = StopRequested; if (a != null) a(); }
                        return (IntPtr)1;
                    }
                    if (key == WaitKey)
                    {
                        if (down) FeedWaitColor(ScreenPixel.CursorPosition(), Environment.TickCount);
                        return (IntPtr)1;
                    }
                    if (RecordKeys) FeedKey(key, down, Environment.TickCount);
                }
            }
            return Native.CallNextHookEx(keyHook, nCode, wParam, lParam);
        }

        void AddPause(int fromTick, int toTick)
        {
            if (!RecordPauses || !hasLast) return;
            int gap = toTick - fromTick;
            if (gap >= 200) Steps.Add(MacroStep.MakeWait(Math.Min(gap, 20000)).Named(L.T("Pausa", "Wait")));
        }

        void Notify()
        {
            Action a = Changed;
            if (a != null) a();
        }

        void ProbeRegion(Point p, out int rx, out int ry, out string hex)
        {
            rx = p.X - Probe;
            ry = p.Y - Probe;
            Color c;
            if (!ScreenPixel.AverageColor(rx, ry, Probe * 2 + 1, Probe * 2 + 1, out c)) c = Color.Black;
            hex = ScreenPixel.ToHex(c);
        }

        public void FeedClick(string button, Point p, int tick)
        {
            AddPause(lastTick, tick);
            int rx, ry; string hex;
            ProbeRegion(p, out rx, out ry, out hex);
            Steps.Add(MacroStep.MakeMoveTo(p.X, p.Y, 0).Named(L.T("Vai a (", "Go to (") + p.X + ", " + p.Y + ")"));
            if (WaitBeforeClick)
                Steps.Add(MacroStep.MakeWaitPixel(true, rx, ry, Probe * 2 + 1, Probe * 2 + 1, hex, 24, 30000).Named(L.T("Attendi il pulsante (", "Wait for the button (") + p.X + ", " + p.Y + ")"));
            Steps.Add(MacroStep.MakeClick(button, 60, 150).Named(L.En ? L.Button(button) + " click" : "Click " + L.Button(button)));
            lastTick = tick;
            hasLast = true;
            Notify();
        }

        public void FeedWaitColor(Point p, int tick)
        {
            AddPause(lastTick, tick);
            int rx, ry; string hex;
            ProbeRegion(p, out rx, out ry, out hex);
            Steps.Add(MacroStep.MakeWaitPixel(true, rx, ry, Probe * 2 + 1, Probe * 2 + 1, hex, 24, 30000).Named(L.T("Attendi colore (", "Wait for color (") + p.X + ", " + p.Y + ")"));
            lastTick = tick;
            hasLast = true;
            Notify();
        }

        public void FeedKey(Keys key, bool down, int tick)
        {
            if (down)
            {
                if (!keyDownAt.ContainsKey(key)) keyDownAt[key] = tick;
                return;
            }
            int t0;
            if (!keyDownAt.TryGetValue(key, out t0)) return;
            keyDownAt.Remove(key);
            AddPause(lastTick, t0);
            string name = KeyParser.DisplayName(key);
            Steps.Add(MacroStep.MakeKeys(name, Math.Max(40, tick - t0), 100, 0, 1).Named(L.T("Tasto ", "Key ") + name));
            lastTick = tick;
            hasLast = true;
            Notify();
        }
    }

    public class CaptureBox : TextBox
    {
        readonly List<Keys> chord = new List<Keys>();
        readonly HashSet<Keys> down = new HashSet<Keys>();

        public CaptureBox()
        {
            ShortcutsEnabled = false;
            ContextMenuStrip = new ContextMenuStrip();
        }

        protected override bool IsInputKey(Keys keyData) { return true; }
        protected override bool ProcessDialogKey(Keys keyData) { return false; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            Keys k = e.KeyCode;
            if ((k == Keys.Back || k == Keys.Delete) && down.Count == 0)
            {
                chord.Clear();
                Text = "";
                return;
            }
            if (down.Count == 0) chord.Clear();
            down.Add(k);
            if (!chord.Contains(k)) chord.Add(k);
            StringBuilder sb = new StringBuilder();
            foreach (Keys c in chord)
            {
                if (sb.Length > 0) sb.Append('+');
                sb.Append(KeyParser.DisplayName(c));
            }
            Text = sb.ToString();
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            e.Handled = true;
            down.Remove(e.KeyCode);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            down.Clear();
            base.OnLostFocus(e);
        }
    }

    // Barra piccola, sempre in primo piano e che non ruba il focus al gioco.
    public class ToastBar : Form
    {
        readonly Label lbl;
        readonly FlatButton btn;
        public event Action ButtonClicked;

        public ToastBar(string text, string buttonText)
        {
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ControlBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Font = new Font("Segoe UI", 9f);
            BackColor = Pal.Panel;

            lbl = new Label();
            lbl.ForeColor = Pal.Text;
            lbl.BackColor = Pal.Panel;
            lbl.AutoSize = true;
            lbl.MaximumSize = new Size(Ui.S(560), 0);
            lbl.Location = new Point(Ui.S(12), Ui.S(10));
            lbl.Text = text;
            Controls.Add(lbl);

            btn = new FlatButton();
            btn.Text = buttonText;
            btn.Size = new Size(Ui.S(120), Ui.S(30));
            btn.Click += delegate { Action a = ButtonClicked; if (a != null) a(); };
            Controls.Add(btn);

            Relayout();
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + 12);
        }

        void Relayout()
        {
            Size ls = lbl.PreferredSize;
            btn.Location = new Point(Ui.S(12), Ui.S(10) + ls.Height + Ui.S(10));
            ClientSize = new Size(Math.Max(ls.Width + Ui.S(24), btn.Width + Ui.S(24)), Ui.S(10) + ls.Height + Ui.S(10) + btn.Height + Ui.S(12));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeUi.DarkTitleBar(Handle, Theme.Dark);
        }

        public void SetText(string text)
        {
            lbl.Text = text;
            Relayout();
        }

        protected override bool ShowWithoutActivation { get { return true; } }
    }


    public class AreaPickerForm : Form
    {
        readonly Bitmap shot;
        readonly Point origin;
        Point start;
        Point current;
        bool dragging;
        bool hasMouse;

        public Rectangle Result;
        public Color Average = Color.Black;
        public bool Accepted;

        public AreaPickerForm(Bitmap shot, Point origin)
        {
            this.shot = shot;
            this.origin = origin;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Bounds = new Rectangle(origin, shot.Size);
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            KeyPreview = true;
        }

        Rectangle Selection()
        {
            int x = Math.Min(start.X, current.X);
            int y = Math.Min(start.Y, current.Y);
            return new Rectangle(x, y, Math.Abs(current.X - start.X) + 1, Math.Abs(current.Y - start.Y) + 1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.DrawImageUnscaled(shot, 0, 0);
            string text;
            Point at;
            if (dragging)
            {
                Rectangle r = Selection();
                using (Pen p = new Pen(Color.Magenta, 2f)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
                Color avg = ScreenPixel.AverageOf(shot, r);
                text = r.Width + " x " + r.Height + "   #" + ScreenPixel.ToHex(avg);
                at = new Point(r.X, Math.Max(0, r.Y - 26));
            }
            else
            {
                text = L.T("Trascina per selezionare un'area  -  un clic = un singolo punto  -  Esc annulla", "Drag to select an area  -  a click = a single point  -  Esc cancels");
                at = hasMouse ? new Point(current.X + 18, current.Y + 18) : new Point(20, 20);
            }
            using (Font f = new Font("Segoe UI", 10f, FontStyle.Bold))
            {
                SizeF sz = g.MeasureString(text, f);
                Rectangle box = new Rectangle(at.X, at.Y, (int)sz.Width + 12, (int)sz.Height + 6);
                g.FillRectangle(Brushes.Black, box);
                g.DrawString(text, f, Brushes.White, at.X + 6, at.Y + 3);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            start = current = e.Location;
            dragging = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            current = e.Location;
            hasMouse = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging || e.Button != MouseButtons.Left) return;
            current = e.Location;
            dragging = false;
            Rectangle r = Selection();
            Average = ScreenPixel.AverageOf(shot, r);
            Result = new Rectangle(r.X + origin.X, r.Y + origin.Y, r.Width, r.Height);
            Accepted = true;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Accepted = false; Close(); }
            base.OnKeyDown(e);
        }

        public static bool Pick(Bitmap shot, Point origin, out Rectangle rect, out Color average)
        {
            rect = Rectangle.Empty;
            average = Color.Black;
            using (new PhysicalCoords())
            using (AreaPickerForm f = new AreaPickerForm(shot, origin))
            {
                f.ShowDialog();
                if (!f.Accepted) return false;
                rect = f.Result;
                average = f.Average;
                return true;
            }
        }
    }

}
