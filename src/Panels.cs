using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StepLoop
{
    // Log in basso: righe con ora, icona e tipo, filtri semplici. Le righe più recenti stanno in alto.
    public sealed class LogView : Control
    {
        struct Entry
        {
            public DateTime Time;
            public LogKind Kind;
            public string Text;
        }

        const int MaxEntries = 1500;
        readonly List<Entry> all = new List<Entry>();
        int filter;               // 0 tutto, 1 azioni, 2 controlli colore, 3 avvisi
        int offset;               // righe saltate dall'alto (0 = ultime)
        int hotChip = -1;
        bool emptyShown = true;

        public event EventHandler Cleared;

        public LogView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public int Count { get { return all.Count; } }

        public int Filter
        {
            get { return filter; }
            set { filter = value; offset = 0; Invalidate(); }
        }

        public void Add(DateTime time, LogKind kind, string text)
        {
            Entry en = new Entry();
            en.Time = time; en.Kind = kind; en.Text = text;
            all.Add(en);
            if (all.Count > MaxEntries) all.RemoveRange(0, all.Count - MaxEntries);
            if (offset > 0) offset++;      // non far scorrere la vista se l'utente sta leggendo righe più vecchie
            emptyShown = false;
            Invalidate();
        }

        public void Clear()
        {
            all.Clear();
            offset = 0;
            Invalidate();
            EventHandler h = Cleared;
            if (h != null) h(this, EventArgs.Empty);
        }

        // righe visibili con il filtro attuale (più recente per prima)
        List<Entry> Filtered()
        {
            List<Entry> list = new List<Entry>();
            for (int i = all.Count - 1; i >= 0; i--)
            {
                Entry en = all[i];
                bool ok;
                switch (filter)
                {
                    case 1: ok = en.Kind == LogKind.Action || en.Kind == LogKind.Loop; break;
                    case 2: ok = en.Kind == LogKind.Color; break;
                    case 3: ok = en.Kind == LogKind.Warn || en.Kind == LogKind.Error; break;
                    default: ok = true; break;
                }
                if (ok) list.Add(en);
            }
            return list;
        }

        public int VisibleCount { get { return Filtered().Count; } }

        public string[] VisibleLines(int max)
        {
            List<Entry> f = Filtered();
            List<string> res = new List<string>();
            for (int i = 0; i < f.Count && i < max; i++) res.Add(f[i].Time.ToString("HH:mm:ss.fff") + " " + KindName(f[i].Kind) + " " + f[i].Text);
            return res.ToArray();
        }

        int HeaderH { get { return Ui.S(32); } }
        int LineH { get { return Ui.S(19); } }

        static string KindName(LogKind k)
        {
            switch (k)
            {
                case LogKind.Action: return L.T("AZIONE", "ACTION");
                case LogKind.Loop: return L.T("GIRO", "ROUND");
                case LogKind.Color: return L.T("COLORE", "COLOR");
                case LogKind.Warn: return L.T("AVVISO", "WARN");
                case LogKind.Error: return L.T("ERRORE", "ERROR");
                default: return "INFO";
            }
        }

        static Color KindColor(LogKind k)
        {
            switch (k)
            {
                case LogKind.Action: return Pal.Ok;
                case LogKind.Loop: return Pal.Info;
                case LogKind.Color: return Pal.Warn;
                case LogKind.Warn: return Pal.Warn;
                case LogKind.Error: return Pal.Danger;
                default: return Pal.Faint;
            }
        }

        static void KindIcon(Graphics g, LogKind k, Rectangle r, Color c)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, u = r.Width / 12f;
            using (Pen p = new Pen(c, Math.Max(1.2f, 1.3f * u)))
            using (SolidBrush b = new SolidBrush(c))
            {
                switch (k)
                {
                    case LogKind.Action:
                        g.FillPolygon(b, new PointF[] { new PointF(cx - 3 * u, cy - 4 * u), new PointF(cx + 4 * u, cy), new PointF(cx - 3 * u, cy + 4 * u) });
                        break;
                    case LogKind.Loop:
                        g.DrawArc(p, cx - 4 * u, cy - 4 * u, 8 * u, 8 * u, 40, 280);
                        g.FillPolygon(b, new PointF[] { new PointF(cx + 4.8f * u, cy - 3.6f * u), new PointF(cx + 1.2f * u, cy - 4 * u), new PointF(cx + 4 * u, cy - 0.2f * u) });
                        break;
                    case LogKind.Color:
                        g.FillRectangle(b, cx - 3.5f * u, cy - 3.5f * u, 7 * u, 7 * u);
                        break;
                    case LogKind.Warn:
                        g.DrawPolygon(p, new PointF[] { new PointF(cx, cy - 4.5f * u), new PointF(cx + 4.6f * u, cy + 3.8f * u), new PointF(cx - 4.6f * u, cy + 3.8f * u) });
                        break;
                    case LogKind.Error:
                        g.DrawLine(p, cx - 3.5f * u, cy - 3.5f * u, cx + 3.5f * u, cy + 3.5f * u);
                        g.DrawLine(p, cx - 3.5f * u, cy + 3.5f * u, cx + 3.5f * u, cy - 3.5f * u);
                        break;
                    default:
                        g.DrawEllipse(p, cx - 3.5f * u, cy - 3.5f * u, 7 * u, 7 * u);
                        break;
                }
            }
            g.SmoothingMode = old;
        }

        string ChipText(int i)
        {
            switch (i)
            {
                case 1: return L.T("Azioni", "Actions");
                case 2: return L.T("Controlli colore", "Color checks");
                case 3: return L.T("Avvisi", "Warnings");
                default: return L.T("Tutto", "All");
            }
        }

        Rectangle ChipRect(Graphics g, int i)
        {
            int x = Ui.S(58);
            for (int k = 0; k < i; k++) x += Ui.Measure(g, ChipText(k), Ui.F(8.5f)) + Ui.S(20 + 6);
            return new Rectangle(x, Ui.S(6), Ui.Measure(g, ChipText(i), Ui.F(8.5f)) + Ui.S(20), Ui.S(20));
        }

        Rectangle ClearRect(Graphics g)
        {
            string t = L.T("Svuota", "Clear");
            int w = Ui.Measure(g, t, Ui.F(8.5f)) + Ui.S(20);
            return new Rectangle(Width - w - Ui.S(10), Ui.S(6), w, Ui.S(20));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = -1;
            using (Graphics g = CreateGraphics())
            {
                for (int i = 0; i < 4; i++) if (ChipRect(g, i).Contains(e.Location)) { h = i; break; }
                if (h < 0 && ClearRect(g).Contains(e.Location)) h = 9;
            }
            if (h != hotChip) { hotChip = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hotChip = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            using (Graphics g = CreateGraphics())
            {
                for (int i = 0; i < 4; i++) if (ChipRect(g, i).Contains(e.Location)) { Filter = i; return; }
                if (ClearRect(g).Contains(e.Location)) { Clear(); return; }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int total = Filtered().Count;
            int visible = Math.Max(1, (Height - HeaderH) / LineH);
            offset = Math.Max(0, Math.Min(Math.Max(0, total - visible), offset - Math.Sign(e.Delta) * 3));
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Pal.LogBg);
            Ui.Text(g, "LOG", Ui.F(7.5f, true), new Rectangle(Ui.S(12), Ui.S(4), Ui.S(44), Ui.S(24)), Pal.Muted, Ui.Left);
            for (int i = 0; i < 4; i++)
            {
                Rectangle r = ChipRect(g, i);
                bool on = i == filter;
                if (on) Ui.FillRound(g, r, r.Height / 2, Pal.Raised);
                else if (i == hotChip) Ui.FillRound(g, r, r.Height / 2, Pal.Panel);
                Ui.Text(g, ChipText(i), Ui.F(8.5f), r, on ? Pal.Text : Pal.Muted, Ui.Center);
            }
            Rectangle cr = ClearRect(g);
            if (hotChip == 9) Ui.FillRound(g, cr, cr.Height / 2, Pal.Panel);
            Ui.Text(g, L.T("Svuota", "Clear"), Ui.F(8.5f), cr, Pal.Muted, Ui.Center);
            using (Pen p = new Pen(Pal.LogSep)) g.DrawLine(p, 0, HeaderH, Width, HeaderH);

            List<Entry> f = Filtered();
            if (f.Count == 0)
            {
                string msg = all.Count == 0
                    ? L.T("Nessuna esecuzione. Il log compare qui quando premi Avvia.", "Nothing has run yet. The log appears here when you press Start.")
                    : L.T("Nessuna riga con questo filtro.", "No lines match this filter.");
                Ui.Text(g, msg, Ui.F(9f), new Rectangle(Ui.S(12), HeaderH + Ui.S(6), Width - Ui.S(24), LineH), Pal.Faint, Ui.Left);
                return;
            }
            int visible = Math.Max(1, (Height - HeaderH - Ui.S(4)) / LineH);
            Font mono = Ui.M(9f);
            Font tagFont = Ui.M(8.5f, true);
            int y = HeaderH + Ui.S(4);
            for (int i = offset; i < f.Count && i < offset + visible + 1; i++)
            {
                Entry en = f[i];
                Color kc = KindColor(en.Kind);
                Ui.Text(g, en.Time.ToString("HH:mm:ss.fff"), mono, new Rectangle(Ui.S(12), y, Ui.S(92), LineH), Pal.Faint, Ui.Left);
                KindIcon(g, en.Kind, new Rectangle(Ui.S(108), y + (LineH - Ui.S(12)) / 2, Ui.S(12), Ui.S(12)), kc);
                Ui.Text(g, KindName(en.Kind), tagFont, new Rectangle(Ui.S(126), y, Ui.S(66), LineH), kc, Ui.Left);
                Ui.Text(g, en.Text, mono, new Rectangle(Ui.S(196), y, Width - Ui.S(208), LineH), Pal.Text2, Ui.Left);
                y += LineH;
            }
        }
    }

    // Schede delle statistiche (ripetizione, tempo, durata ciclo, step attivo).
    public sealed class StatCard : Control
    {
        public string Caption = "";
        public string Value = "";
        public string Sub = "";
        public double Progress = -1;
        public bool Highlight;
        public bool ValueIsText;     // valore lungo (nome step): font più piccolo

        public StatCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void Set(string caption, string value, string sub, double progress)
        {
            if (Caption == caption && Value == value && Sub == sub && Progress == progress) return;
            Caption = caption; Value = value; Sub = sub; Progress = progress;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Pal.Bg);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Ui.FillRound(g, r, Ui.S(8), Pal.Panel);
            Ui.DrawRound(g, r, Ui.S(8), Highlight ? Pal.Focus : Pal.Border, 1f);
            int px = Ui.S(14);
            Ui.Text(g, (Caption ?? "").ToUpperInvariant(), Ui.F(7.5f, true), new Rectangle(px, Ui.S(8), Width - px * 2, Ui.S(14)),
                Highlight ? Pal.Info : Pal.Muted, Ui.Left);
            Font vf = ValueIsText ? Ui.M(12f, true) : Ui.M(15f, true);
            Ui.Text(g, Value, vf, new Rectangle(px, Ui.S(24), Width - px * 2, Ui.S(28)), Pal.TextStrong, Ui.Left);
            if (Progress >= 0)
            {
                Rectangle tr = new Rectangle(px, Height - Ui.S(14), Width - px * 2, Ui.S(3));
                using (SolidBrush b = new SolidBrush(Pal.Border)) g.FillRectangle(b, tr);
                using (SolidBrush b = new SolidBrush(Pal.Dot(Sem.Flow))) g.FillRectangle(b, tr.X, tr.Y, (int)(tr.Width * Math.Min(1.0, Progress)), tr.Height);
            }
            else if (!string.IsNullOrEmpty(Sub))
            {
                Ui.Text(g, Sub, Ui.F(8.25f), new Rectangle(px, Height - Ui.S(22), Width - px * 2, Ui.S(16)), Pal.Muted, Ui.Left);
            }
        }
    }

    // Pannello destro durante l'esecuzione: controllo colore, blocco corrente, prossimi step.
    public sealed class LivePanel : Control
    {
        MacroRunner runner;
        List<MacroStep>[] lists = new List<MacroStep>[] { new List<MacroStep>(), new List<MacroStep>() };

        public LivePanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void SetContext(MacroRunner r, List<MacroStep> game, List<MacroStep> menu)
        {
            runner = r;
            lists[0] = game ?? new List<MacroStep>();
            lists[1] = menu ?? new List<MacroStep>();
            Invalidate();
        }

        static Color Soft(Color c, double a) { return Pal.Over(Pal.Panel, c, a); }

        // intestazione del blocco più interno che contiene il passo i (esclusa la riga stessa), oppure -1
        static int Enclosing(List<MacroStep> steps, int i)
        {
            for (int k = i - 1; k >= 0; k--)
                if (steps[k].IsBlockHeader && steps[k].Match > i) return k;
            return -1;
        }

        // prossimi passi eseguibili dopo quello attivo (salta chiusure ed etichette; a fine ciclo riparte dal corpo)
        List<int> NextSteps(List<MacroStep> steps, int idx, int max)
        {
            List<int> res = new List<int>();
            if (idx < 0 || idx >= steps.Count) return res;
            if (Flow.Prepare(steps) != null) return res;
            int i = idx + 1;
            int guard = 0;
            while (res.Count < max && i < steps.Count && guard++ < 400)
            {
                MacroStep s = steps[i];
                // le altre alternative di una scelta (o il ramo "altrimenti") non verranno eseguite: si salta alla fine del blocco
                int enc = Enclosing(steps, i);
                if (enc >= 0 && enc < idx && steps[enc].Match > idx)
                {
                    MacroStep eh = steps[enc];
                    bool noOptions = eh.OptionMarks == null || eh.OptionMarks.Count == 0;
                    if (s.IsBranchMarker || (eh.Type == StepType.ChoiceStart && noOptions))
                    {
                        i = eh.Match;
                        continue;
                    }
                }
                if (s.Type == StepType.LoopEnd)
                {
                    int h = -1;
                    for (int k = i - 1; k >= 0; k--) if (steps[k].Type == StepType.LoopStart && steps[k].Match == i) { h = k; break; }
                    if (h >= 0 && h < idx)
                    {
                        // fine del ciclo che contiene il passo attivo: segue un nuovo giro (non si ripetono i passi)
                        res.Add(-1);
                        break;
                    }
                    i++;
                    continue;
                }
                if (s.IsBlockFooter || s.IsBranchMarker || s.Type == StepType.Label || s.IsBlockHeader || s.Type == StepType.SetVar)
                {
                    i++;
                    continue;
                }
                res.Add(i);
                i++;
            }
            return res;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Pal.Panel);
            if (runner == null) return;

            int list, idx, planned;
            long el;
            string label;
            runner.GetCurrent(out list, out idx, out el, out planned, out label);
            List<MacroStep> steps = lists[Math.Max(0, Math.Min(1, list < 0 ? 0 : list))];

            int px = Ui.S(14);
            int w = Width - px * 2;
            int y = 0;

            // intestazione: blocco in cui ci si trova (o il passo stesso)
            int headerIdx = -1;
            if (idx >= 0 && idx < steps.Count && Flow.Prepare(steps) == null)
            {
                for (int k = idx; k >= 0; k--)
                {
                    MacroStep h = steps[k];
                    if (h.IsBlockHeader && h.Match >= idx) { headerIdx = k; if (h.Type == StepType.LoopStart) break; }
                }
            }
            MacroStep titleStep = headerIdx >= 0 ? steps[headerIdx] : (idx >= 0 && idx < steps.Count ? steps[idx] : null);
            Rectangle top = new Rectangle(0, 0, Width, Ui.S(40));
            using (Pen p = new Pen(Pal.Border)) g.DrawLine(p, 0, top.Bottom, Width, top.Bottom);
            if (titleStep != null)
            {
                Sem sem = StepStyle.SemOf(titleStep.Type);
                string bt = StepStyle.Badge(titleStep.Type);
                Font bf = Ui.M(7.5f, true);
                int bw = Math.Max(Ui.S(46), Ui.Measure(g, bt, bf) + Ui.S(16));
                Rectangle br = new Rectangle(px, (top.Height - Ui.S(18)) / 2, bw, Ui.S(18));
                Ui.FillRound(g, br, Ui.S(4), Pal.BadgeBg(sem, Pal.Panel));
                Ui.Text(g, bt, bf, br, Pal.BadgeFg(sem), Ui.Center);
                Ui.Text(g, StepStyle.Title(titleStep), Ui.F(9.5f, true), new Rectangle(br.Right + Ui.S(10), 0, Width - br.Right - Ui.S(24), top.Height), Pal.TextStrong, Ui.Left);
            }
            else
            {
                string t = runner.InCountdown ? L.T("Partenza in corso…", "Starting…") : L.T("In attesa del primo passo…", "Waiting for the first step…");
                Ui.Text(g, t, Ui.F(9.5f, true), new Rectangle(px, 0, w, top.Height), Pal.Muted, Ui.Left);
            }
            y = top.Bottom + Ui.S(14);

            // CONTROLLO COLORE
            ColorCheck cc = runner.LastColor;
            if (cc.Valid)
            {
                Ui.Text(g, L.T("CONTROLLO COLORE · (", "COLOR CHECK · (") + cc.X + ", " + cc.Y + ")", Ui.F(7.5f, true), new Rectangle(px, y, w, Ui.S(16)), Pal.Muted, Ui.Left);
                y += Ui.S(22);
                int half = w / 2;
                Swatch(g, new Point(px, y), cc.Expected, L.T("Atteso", "Expected"));
                Swatch(g, new Point(px + half, y), cc.Read, L.T("Letto ora", "Read now"));
                y += Ui.S(46);
                Color chipBase = cc.Match ? Pal.Dot(Sem.Input) : Pal.Dot(Sem.Cond);
                Rectangle chip = new Rectangle(px, y, w, Ui.S(30));
                Ui.FillRound(g, chip, Ui.S(6), Soft(chipBase, 0.14));
                string ctxt = cc.Match
                    ? L.T("Corrisponde · Δ " + cc.Delta + " ≤ ±" + cc.Tolerance, "Matches · Δ " + cc.Delta + " ≤ ±" + cc.Tolerance)
                    : L.T("Non corrisponde · Δ " + cc.Delta + " > ±" + cc.Tolerance, "No match · Δ " + cc.Delta + " > ±" + cc.Tolerance);
                if (!cc.Match)
                {
                    List<MacroStep> cl = lists[Math.Max(0, Math.Min(1, cc.List))];
                    StepType ct = cc.Index >= 0 && cc.Index < cl.Count ? cl[cc.Index].Type : StepType.Wait;
                    if (ct == StepType.LoopStart) ctxt += L.T(" · il ciclo continua", " · the loop goes on");
                    else if (ct == StepType.WaitPixel || ct == StepType.WaitPixelGone) ctxt += L.T(" · continua ad attendere", " · still waiting");
                }
                Ui.Text(g, ctxt, Ui.F(9f), new Rectangle(chip.X + Ui.S(10), chip.Y, chip.Width - Ui.S(14), chip.Height), cc.Match ? Pal.Ok : Pal.Warn, Ui.Left);
                y += Ui.S(30) + Ui.S(18);
            }

            // BLOCCO CORRENTE
            if (headerIdx >= 0 && steps[headerIdx].Type == StepType.LoopStart)
            {
                LoopInfo li = runner.GetLoop(list, headerIdx);
                MacroStep hs = steps[headerIdx];
                Ui.Text(g, L.T("BLOCCO CORRENTE", "CURRENT BLOCK"), Ui.F(7.5f, true), new Rectangle(px, y, w, Ui.S(16)), Pal.Muted, Ui.Left);
                y += Ui.S(24);
                string iter = Math.Max(1, li.Iteration) + " · max " + (li.MaxIterations > 0 ? li.MaxIterations.ToString() : "∞");
                string tim = Ui.Hms(li.ElapsedMs) + " · max " + (li.MaxTimeMs > 0 ? Ui.Hms(li.MaxTimeMs) : "∞");
                string cur = Ui.Secs(li.IterElapsedMs) + (li.LastIterMs > 0 ? " / " + Ui.Secs(li.LastIterMs) : "");
                Row(g, px, w, ref y, L.T("Giro", "Round"), iter);
                Row(g, px, w, ref y, L.T("Tempo nel blocco", "Time in block"), tim);
                Row(g, px, w, ref y, L.T("Giro in corso", "Current round"), cur);
                y += Ui.S(10);
            }

            // SCELTA A CASO: alternativa estratta dalla scelta che contiene il passo attivo
            if (idx >= 0 && idx < steps.Count && Flow.Prepare(steps) == null)
            {
                int ch = -1;
                for (int k = idx; k >= 0; k--)
                    if (steps[k].Type == StepType.ChoiceStart && steps[k].Match >= idx) { ch = k; break; }
                if (ch >= 0)
                {
                    int pick = runner.GetCond(list, ch);
                    int total = Flow.ChoiceRanges(steps, ch).Count;
                    Ui.Text(g, L.T("SCELTA A CASO", "RANDOM PICK"), Ui.F(7.5f, true), new Rectangle(px, y, w, Ui.S(16)), Pal.Muted, Ui.Left);
                    y += Ui.S(24);
                    Row(g, px, w, ref y, L.T("Alternativa estratta", "Alternative drawn"), pick >= 0 ? (pick + 1) + " / " + total : "…");
                    y += Ui.S(10);
                }
            }

            // VARIABILI
            Dictionary<string, int> vars = runner.Variables;
            if (vars.Count > 0)
            {
                Ui.Text(g, L.T("VARIABILI", "VARIABLES"), Ui.F(7.5f, true), new Rectangle(px, y, w, Ui.S(16)), Pal.Muted, Ui.Left);
                y += Ui.S(24);
                List<string> names = new List<string>(vars.Keys);
                names.Sort(StringComparer.OrdinalIgnoreCase);
                int shown = 0;
                foreach (string n in names)
                {
                    if (shown++ >= 6) break;
                    Row(g, px, w, ref y, n, vars[n].ToString());
                }
                if (names.Count > 6)
                {
                    Ui.Text(g, "+ " + (names.Count - 6) + L.T(" altre", " more"), Ui.F(9f), new Rectangle(px, y, w, Ui.S(22)), Pal.Faint, Ui.Left);
                    y += Ui.S(24);
                }
                y += Ui.S(10);
            }

            // PROSSIMI STEP
            List<int> next = NextSteps(steps, idx, 3);
            if (next.Count > 0)
            {
                Ui.Text(g, L.T("PROSSIMI STEP", "NEXT STEPS"), Ui.F(7.5f, true), new Rectangle(px, y, w, Ui.S(16)), Pal.Muted, Ui.Left);
                y += Ui.S(24);
                foreach (int k in next)
                {
                    if (k < 0)
                    {
                        Ui.Text(g, "↻ " + L.T("nuovo giro del ciclo", "next round of the loop"), Ui.F(9f), new Rectangle(px, y, w, Ui.S(22)), Pal.Muted, Ui.Left);
                        y += Ui.S(26);
                        continue;
                    }
                    MacroStep s = steps[k];
                    string right = s.HasDuration ? (s.RandDur ? StepStyle.FmtRange(s.DurMin, s.DurMax) : StepStyle.Fmt(s.DurationMs)) : "";
                    Ui.Text(g, StepStyle.Title(s), Ui.F(9.5f), new Rectangle(px, y, w - Ui.S(90), Ui.S(22)), Pal.Text2, Ui.Left);
                    Ui.Text(g, right, Ui.M(9f), new Rectangle(px + w - Ui.S(90), y, Ui.S(90), Ui.S(22)), Pal.Muted, Ui.Right);
                    y += Ui.S(26);
                }
            }

            string note = L.T("Modifica bloccata durante l'esecuzione. Premi Stop per tornare all'editor.",
                              "Editing is locked while running. Press Stop to go back to the editor.");
            Rectangle nr = new Rectangle(px, Height - Ui.S(54), w, Ui.S(44));
            TextRenderer.DrawText(g, note, Ui.F(8.75f), nr, Pal.Muted, TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }

        void Swatch(Graphics g, Point p, Color c, string caption)
        {
            Rectangle r = new Rectangle(p.X, p.Y, Ui.S(36), Ui.S(36));
            Ui.FillRound(g, r, Ui.S(6), c.IsEmpty ? Pal.Raised : c);
            Ui.DrawRound(g, r, Ui.S(6), Pal.Over(Pal.Panel, Color.White, 0.25), 1f);
            Ui.Text(g, caption, Ui.F(8.25f), new Rectangle(r.Right + Ui.S(10), p.Y, Ui.S(90), Ui.S(17)), Pal.Muted, Ui.Left);
            Ui.Text(g, c.IsEmpty ? "?" : "#" + ScreenPixel.ToHex(c), Ui.M(9f), new Rectangle(r.Right + Ui.S(10), p.Y + Ui.S(18), Ui.S(90), Ui.S(18)), Pal.Text, Ui.Left);
        }

        void Row(Graphics g, int px, int w, ref int y, string label, string value)
        {
            Ui.Text(g, label, Ui.F(9.5f), new Rectangle(px, y, w / 2, Ui.S(22)), Pal.Muted, Ui.Left);
            Ui.Text(g, value, Ui.M(9.5f), new Rectangle(px + w / 2, y, w / 2, Ui.S(22)), Pal.Text, Ui.Right);
            y += Ui.S(26);
        }
    }
}
