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
    public static class Native
    {
        public const uint INPUT_MOUSE = 0;
        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_SCANCODE = 0x0008;
        public const uint MOUSEEVENTF_MOVE = 0x0001;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
        public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        // L'unione deve contenere il membro più grande (MOUSEINPUT), altrimenti sizeof(INPUT) è sbagliato e SendInput fallisce.
        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")]
        public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")]
        public static extern uint GetPixel(IntPtr hdc, int x, int y);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("winmm.dll")]
        public static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")]
        public static extern uint timeEndPeriod(uint ms);
    }

    // Con più monitor a scalature diverse, le coordinate "virtualizzate" non coincidono con quelle di SendInput assoluto:
    // dentro questo scope il thread vede coordinate fisiche (pixel reali) per cursore, metriche schermo, GetPixel e CopyFromScreen.
    public sealed class PhysicalCoords : IDisposable
    {
        readonly IntPtr old;

        public PhysicalCoords()
        {
            try { old = Native.SetThreadDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { old = IntPtr.Zero; }
        }

        public void Dispose()
        {
            if (old == IntPtr.Zero) return;
            try { Native.SetThreadDpiAwarenessContext(old); }
            catch (EntryPointNotFoundException) { }
        }
    }

    public static class ScreenPixel
    {
        public static bool TryGet(int x, int y, out Color color)
        {
            using (new PhysicalCoords())
            {
                IntPtr dc = Native.GetDC(IntPtr.Zero);
                try
                {
                    uint c = Native.GetPixel(dc, x, y);
                    if (c == 0xFFFFFFFF) { color = Color.Empty; return false; }
                    color = Color.FromArgb((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF));
                    return true;
                }
                finally { Native.ReleaseDC(IntPtr.Zero, dc); }
            }
        }

        public static Point CursorPosition()
        {
            using (new PhysicalCoords()) { return Cursor.Position; }
        }

        public static Rectangle WindowRect(IntPtr hwnd)
        {
            using (new PhysicalCoords())
            {
                Native.RECT r;
                if (!Native.GetWindowRect(hwnd, out r)) return Rectangle.Empty;
                return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            }
        }

        // Colore medio di un'area (coordinate fisiche dello schermo). 1x1 = singolo pixel.
        public static bool AverageColor(int x, int y, int w, int h, out Color color)
        {
            w = Math.Max(1, w);
            h = Math.Max(1, h);
            if (w == 1 && h == 1) return TryGet(x, y, out color);
            color = Color.Empty;
            try
            {
                using (new PhysicalCoords())
                using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
                    color = AverageOf(bmp, new Rectangle(0, 0, w, h));
                    return true;
                }
            }
            catch (Exception) { return false; }
        }

        public static Color AverageOf(Bitmap bmp, Rectangle region)
        {
            region.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
            if (region.Width <= 0 || region.Height <= 0) return Color.Black;
            BitmapData d = bmp.LockBits(region, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stepX = Math.Max(1, region.Width / 200);
                int stepY = Math.Max(1, region.Height / 200);
                long sr = 0, sg = 0, sb = 0, n = 0;
                byte[] row = new byte[region.Width * 4];
                for (int yy = 0; yy < region.Height; yy += stepY)
                {
                    Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)yy * d.Stride), row, 0, row.Length);
                    for (int xx = 0; xx < region.Width; xx += stepX)
                    {
                        sb += row[xx * 4];
                        sg += row[xx * 4 + 1];
                        sr += row[xx * 4 + 2];
                        n++;
                    }
                }
                if (n == 0) return Color.Black;
                return Color.FromArgb((int)(sr / n), (int)(sg / n), (int)(sb / n));
            }
            finally { bmp.UnlockBits(d); }
        }

        public static bool MatchesArea(int x, int y, int w, int h, Color target, int tolerance)
        {
            Color c;
            if (!AverageColor(x, y, w, h, out c)) return false;
            return Math.Abs(c.R - target.R) <= tolerance && Math.Abs(c.G - target.G) <= tolerance && Math.Abs(c.B - target.B) <= tolerance;
        }

        // Come MatchesArea, ma restituisce anche il colore letto e lo scarto massimo per canale.
        public static bool Check(int x, int y, int w, int h, Color target, int tolerance, out Color read, out int delta)
        {
            delta = 255;
            if (!AverageColor(x, y, w, h, out read)) { read = Color.Empty; return false; }
            delta = Math.Max(Math.Abs(read.R - target.R), Math.Max(Math.Abs(read.G - target.G), Math.Abs(read.B - target.B)));
            return delta <= tolerance;
        }

        // Istantanea di un'area dello schermo (coordinate fisiche).
        public static Bitmap CaptureArea(int x, int y, int w, int h)
        {
            w = Math.Max(1, w);
            h = Math.Max(1, h);
            using (new PhysicalCoords())
            {
                Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                try
                {
                    using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
                    return bmp;
                }
                catch (Exception)
                {
                    bmp.Dispose();
                    return null;
                }
            }
        }

        public static Bitmap CaptureVirtualScreen(out Point origin)
        {
            using (new PhysicalCoords())
            {
                Rectangle vs = SystemInformation.VirtualScreen;
                Bitmap bmp = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(vs.Left, vs.Top, 0, 0, vs.Size);
                origin = vs.Location;
                return bmp;
            }
        }

        public static string ToHex(Color c)
        {
            return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        public static bool TryParseHex(string text, out Color color)
        {
            color = Color.Empty;
            text = (text ?? "").Trim().TrimStart('#');
            if (text.Length != 6) return false;
            int v;
            if (!int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return false;
            color = Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
            return true;
        }
    }

    public static class KeyParser
    {
        static readonly Dictionary<string, Keys> Aliases = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase)
        {
            { "shift", Keys.LShiftKey }, { "lshift", Keys.LShiftKey }, { "rshift", Keys.RShiftKey },
            { "ctrl", Keys.LControlKey }, { "control", Keys.LControlKey }, { "lctrl", Keys.LControlKey }, { "rctrl", Keys.RControlKey },
            { "alt", Keys.LMenu }, { "lalt", Keys.LMenu }, { "ralt", Keys.RMenu },
            { "space", Keys.Space }, { "spazio", Keys.Space },
            { "enter", Keys.Return }, { "invio", Keys.Return },
            { "esc", Keys.Escape }, { "backspace", Keys.Back },
            { "tab", Keys.Tab }, { "capslock", Keys.CapsLock }
        };

        public static bool TryParseKey(string token, out Keys key)
        {
            key = Keys.None;
            token = (token ?? "").Trim();
            if (token.Length == 0) return false;
            if (Aliases.TryGetValue(token, out key)) return true;
            if (token.Length == 1 && token[0] >= '0' && token[0] <= '9')
            {
                key = (Keys)((int)Keys.D0 + (token[0] - '0'));
                return true;
            }
            if (token.IndexOf(',') >= 0 || char.IsDigit(token[0])) return false;
            Keys parsed;
            if (!Enum.TryParse<Keys>(token, true, out parsed)) return false;
            int v = (int)parsed;
            if (v < 8 || v > 0xFE) return false;
            key = parsed;
            return true;
        }

        public static bool TryParseCombo(string text, out List<Keys> keys)
        {
            keys = new List<Keys>();
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (string part in text.Split('+'))
            {
                Keys k;
                if (!TryParseKey(part, out k)) return false;
                if (!keys.Contains(k)) keys.Add(k);
            }
            return keys.Count > 0;
        }

        public static string DisplayName(Keys k)
        {
            switch (k)
            {
                case Keys.ShiftKey: case Keys.LShiftKey: return "Shift";
                case Keys.RShiftKey: return "RShift";
                case Keys.ControlKey: case Keys.LControlKey: return "Ctrl";
                case Keys.RControlKey: return "RCtrl";
                case Keys.Menu: case Keys.LMenu: return "Alt";
                case Keys.RMenu: return "RAlt";
                case Keys.Return: return "Enter";
                case Keys.Escape: return "Esc";
            }
            if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
            return k.ToString();
        }
    }

    public static class InputSender
    {
        static readonly HashSet<Keys> Extended = new HashSet<Keys>
        {
            Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Insert, Keys.Delete, Keys.Home, Keys.End,
            Keys.PageUp, Keys.PageDown, Keys.RControlKey, Keys.RMenu, Keys.Divide, Keys.NumLock
        };

        // Gli FPS leggono i tasti dagli scancode: l'invio via virtual-key puro spesso viene ignorato.
        public static bool Key(Keys key, bool down)
        {
            ushort scan = (ushort)Native.MapVirtualKey((uint)key, 0);
            Native.INPUT[] inp = new Native.INPUT[1];
            inp[0].type = Native.INPUT_KEYBOARD;
            uint flags = 0;
            if (scan != 0)
            {
                inp[0].u.ki.wScan = scan;
                flags |= Native.KEYEVENTF_SCANCODE;
                if (Extended.Contains(key)) flags |= Native.KEYEVENTF_EXTENDEDKEY;
            }
            else
            {
                inp[0].u.ki.wVk = (ushort)key;
            }
            if (!down) flags |= Native.KEYEVENTF_KEYUP;
            inp[0].u.ki.dwFlags = flags;
            return Native.SendInput(1, inp, Marshal.SizeOf(typeof(Native.INPUT))) == 1;
        }

        public static bool Button(string button, bool down)
        {
            uint flag;
            switch ((button ?? "").ToLowerInvariant())
            {
                case "destro": flag = down ? Native.MOUSEEVENTF_RIGHTDOWN : Native.MOUSEEVENTF_RIGHTUP; break;
                case "centrale": flag = down ? Native.MOUSEEVENTF_MIDDLEDOWN : Native.MOUSEEVENTF_MIDDLEUP; break;
                default: flag = down ? Native.MOUSEEVENTF_LEFTDOWN : Native.MOUSEEVENTF_LEFTUP; break;
            }
            return SendMouse(0, 0, flag);
        }

        public static bool Move(int dx, int dy)
        {
            return SendMouse(dx, dy, Native.MOUSEEVENTF_MOVE);
        }

        public static void AbsToNormalized(int x, int y, out int nx, out int ny)
        {
            using (new PhysicalCoords())
            {
                Rectangle vs = SystemInformation.VirtualScreen;
                nx = (int)Math.Round((x - vs.Left) * 65535.0 / Math.Max(1, vs.Width - 1));
                ny = (int)Math.Round((y - vs.Top) * 65535.0 / Math.Max(1, vs.Height - 1));
                nx = Math.Min(65535, Math.Max(0, nx));
                ny = Math.Min(65535, Math.Max(0, ny));
            }
        }

        public static bool MoveAbs(int x, int y)
        {
            using (new PhysicalCoords())
            {
                int nx, ny;
                AbsToNormalized(x, y, out nx, out ny);
                return SendMouse(nx, ny, Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE | Native.MOUSEEVENTF_VIRTUALDESK);
            }
        }

        static bool SendMouse(int dx, int dy, uint flags)
        {
            Native.INPUT[] inp = new Native.INPUT[1];
            inp[0].type = Native.INPUT_MOUSE;
            inp[0].u.mi.dx = dx;
            inp[0].u.mi.dy = dy;
            inp[0].u.mi.dwFlags = flags;
            return Native.SendInput(1, inp, Marshal.SizeOf(typeof(Native.INPUT))) == 1;
        }
    }

}
