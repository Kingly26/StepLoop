using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StepLoop
{
    // Lista dei passi disegnata a mano: blocchi comprimibili con guide di annidamento, gruppi di passi uguali,
    // evidenza e progresso dello step in esecuzione, drag & drop per riordinare e per aggiungere dalla palette.
    public sealed class StepListControl : Control
    {
        public const string DragFormat = "StepLoopStepType";

        enum RowKind { Step, Header, Footer, Else, Group }

        sealed class Row
        {
            public RowKind Kind;
            public int Index;          // passo (Group: primo membro)
            public int EndIndex;       // Header: chiusura; Group: ultimo membro; altrimenti = Index
            public int Depth;
            public int Inner;          // Header: passi contenuti; Group: membri
            public bool Collapsed;
            public bool Collapsible;
            public int Y;
            public int H;
        }

        List<MacroStep> steps = new List<MacroStep>();
        int listId;
        readonly List<Row> rows = new List<Row>();
        int[] matchOf = new int[0];          // per ogni passo: chiusura (header) o intestazione (chiusure); -1 se nessuna
        int scroll;
        int contentH;
        int selected = -1;
        int hoverRow = -1;
        readonly HashSet<int> expanded = new HashSet<int>();
        MacroRunner runner;
        int activeIndex = -1;
        int lastEnsured = -2;
        readonly ToolTip tip = new ToolTip();
        string tipText;

        public bool Follow = true;
        public bool ReadOnly;

        // trascinamento
        bool dragCandidate;
        bool dragging;
        Point dragStart;
        int dragFrom, dragTo;
        int dropIndex = -1;
        int dropY = -1;

        // barra di scorrimento
        bool sbDrag;
        int sbGrab;

        public event EventHandler SelectionChanged;
        public event Action<StepType, int> PaletteDrop;
        public event Action<int, int, int> Reorder;
        public event Action DeleteRequested;
        public event Action DuplicateRequested;
        public event Action<int> MoveRequested;

        public StepListControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.Opaque, true);
            TabStop = true;
            AllowDrop = true;
            tip.InitialDelay = 500;
            tip.ReshowDelay = 200;
            tip.AutoPopDelay = 12000;
        }

        // ---------- dati ----------

        public List<MacroStep> Steps { get { return steps; } }

        public void SetSource(List<MacroStep> list, int id)
        {
            steps = list ?? new List<MacroStep>();
            listId = id;
            selected = -1;
            scroll = 0;
            expanded.Clear();
            Rebuild();
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                int v = value >= 0 && value < steps.Count ? value : -1;
                if (v >= 0) Reveal(v);
                if (v == selected) { if (v >= 0) EnsureVisibleStep(v); return; }
                selected = v;
                Rebuild();
                if (v >= 0) EnsureVisibleStep(v);
                EventHandler h = SelectionChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        // Dopo un'operazione sul modello (aggiunta, spostamento...) si ricostruiscono le righe.
        public void Reload(int select)
        {
            selected = select >= 0 && select < steps.Count ? select : -1;
            if (selected >= 0) Reveal(selected);
            Rebuild();
            if (selected >= 0) EnsureVisibleStep(selected);
            EventHandler h = SelectionChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        public void SetLive(MacroRunner r)
        {
            runner = r;
            activeIndex = -1;
            lastEnsured = -2;
            Rebuild();
        }

        public bool Live { get { return runner != null; } }

        // ---------- ricostruzione delle righe ----------

        bool IsHeader(MacroStep s) { return s.IsBlockHeader; }

        // Raggruppabili: tasti con stessa durata/pausa, senza nome e senza durata casuale.
        bool Groupable(MacroStep s)
        {
            return s.Type == StepType.Keys && string.IsNullOrEmpty(s.Name) && !s.RandDur && !s.RandPause;
        }

        bool SameGroup(MacroStep a, MacroStep b)
        {
            return Groupable(a) && Groupable(b) && a.DurationMs == b.DurationMs && a.PauseMs == b.PauseMs;
        }

        void ComputeMatches()
        {
            int n = steps.Count;
            matchOf = new int[n];
            for (int i = 0; i < n; i++) matchOf[i] = -1;
            List<int> st = new List<int>();
            for (int i = 0; i < n; i++)
            {
                MacroStep s = steps[i];
                if (IsHeader(s)) st.Add(i);
                else if (s.IsBlockFooter)
                {
                    int want = s.Type == StepType.LoopEnd ? (int)StepType.LoopStart : (s.Type == StepType.IfEnd ? (int)StepType.IfStart : (int)StepType.ChoiceStart);
                    if (st.Count > 0 && (int)steps[st[st.Count - 1]].Type == want)
                    {
                        int h = st[st.Count - 1];
                        st.RemoveAt(st.Count - 1);
                        matchOf[h] = i;
                        matchOf[i] = h;
                    }
                }
            }
        }

        bool LiveFollow { get { return runner != null && Follow; } }

        bool CollapsedEff(int header, int end)
        {
            if (LiveFollow && activeIndex >= 0) return !(activeIndex >= header && activeIndex <= end);
            return steps[header].Collapsed;
        }

        bool GroupExpandedEff(int first, int last)
        {
            if (LiveFollow && activeIndex >= 0) return activeIndex >= first && activeIndex <= last;
            if (selected > first && selected <= last) return true;
            return expanded.Contains(first);
        }

        public void Rebuild()
        {
            rows.Clear();
            ComputeMatches();
            Emit(0, steps.Count, 0);
            int y = Ui.S(6);
            foreach (Row r in rows)
            {
                switch (r.Kind)
                {
                    case RowKind.Header: r.H = Ui.S(34); break;
                    case RowKind.Footer: case RowKind.Else: r.H = Ui.S(22); break;
                    default: r.H = Ui.S(30); break;
                }
                r.Y = y;
                y += r.H;
            }
            contentH = y + Ui.S(6);
            ClampScroll();
            Invalidate();
        }

        void Emit(int from, int to, int depth)
        {
            int i = from;
            while (i < to)
            {
                MacroStep s = steps[i];
                if (IsHeader(s) && matchOf[i] > i && matchOf[i] < steps.Count)
                {
                    int m = matchOf[i];
                    bool col = CollapsedEff(i, m);
                    Row h = new Row();
                    h.Kind = RowKind.Header; h.Index = i; h.EndIndex = m; h.Depth = depth; h.Inner = m - i - 1;
                    h.Collapsed = col; h.Collapsible = true;
                    rows.Add(h);
                    if (!col)
                    {
                        Emit(i + 1, m, depth + 1);
                        Row f = new Row();
                        f.Kind = RowKind.Footer; f.Index = m; f.EndIndex = m; f.Depth = depth;
                        rows.Add(f);
                    }
                    i = m + 1;
                    continue;
                }
                if (s.IsBranchMarker)
                {
                    Row e = new Row();
                    e.Kind = RowKind.Else; e.Index = i; e.EndIndex = i; e.Depth = Math.Max(0, depth - 1);
                    rows.Add(e);
                    i++;
                    continue;
                }
                if (s.IsBlockFooter)
                {
                    Row f = new Row();
                    f.Kind = RowKind.Footer; f.Index = i; f.EndIndex = i; f.Depth = Math.Max(0, depth - 1);
                    rows.Add(f);
                    i++;
                    continue;
                }
                if (Groupable(s))
                {
                    int j = i;
                    while (j + 1 < to && SameGroup(steps[j + 1], s)) j++;
                    if (j - i + 1 >= 3)
                    {
                        bool open = GroupExpandedEff(i, j);
                        Row g = new Row();
                        g.Kind = RowKind.Group; g.Index = i; g.EndIndex = j; g.Depth = depth; g.Inner = j - i + 1;
                        g.Collapsed = !open; g.Collapsible = true;
                        rows.Add(g);
                        if (open)
                        {
                            for (int k = i; k <= j; k++)
                            {
                                Row st = new Row();
                                st.Kind = RowKind.Step; st.Index = k; st.EndIndex = k; st.Depth = depth + 1;
                                rows.Add(st);
                            }
                        }
                        i = j + 1;
                        continue;
                    }
                }
                Row r = new Row();
                r.Kind = RowKind.Step; r.Index = i; r.EndIndex = i; r.Depth = depth;
                rows.Add(r);
                i++;
            }
        }

        // Apre i blocchi e i gruppi che nascondono il passo indicato.
        void Reveal(int idx)
        {
            ComputeMatches();
            bool changed = false;
            for (int h = 0; h < idx; h++)
            {
                if (IsHeader(steps[h]) && matchOf[h] > idx && steps[h].Collapsed) { steps[h].Collapsed = false; changed = true; }
            }
            // gruppi: si ricostruisce e si controlla se il passo è visibile
            if (!VisibleRow(idx, true))
            {
                int run = idx;
                while (run > 0 && SameGroup(steps[run - 1], steps[idx])) run--;
                if (expanded.Add(run)) changed = true;
            }
            if (changed) Rebuild();
        }

        bool VisibleRow(int idx, bool rebuildFirst)
        {
            if (rebuildFirst) Rebuild();
            return RowOfStep(idx) >= 0;
        }

        int RowOfStep(int idx)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                if (r.Kind == RowKind.Group) { if (idx >= r.Index && idx <= r.EndIndex) { /* membro nascosto: la riga gruppo lo rappresenta */ if (r.Collapsed) return i; } }
                else if (r.Index == idx) return i;
            }
            return -1;
        }

        // ---------- aggiornamento live ----------

        // Chiamato dal timer dell'interfaccia: aggiorna lo step attivo, riapre i blocchi giusti e fa scorrere la lista.
        public void LiveTick()
        {
            if (runner == null) return;
            int l, idx, planned;
            long el;
            string label;
            runner.GetCurrent(out l, out idx, out el, out planned, out label);
            int act = (l == listId) ? idx : -1;
            if (act != activeIndex)
            {
                activeIndex = act;
                Rebuild();
            }
            if (Follow && act >= 0 && act != lastEnsured)
            {
                lastEnsured = act;
                int r = RowOfStep(act);
                if (r >= 0)
                {
                    EnsureRowVisible(Math.Min(rows.Count - 1, r + 2));
                    EnsureRowVisible(r);
                }
            }
            Invalidate();
        }

        // ---------- scorrimento ----------

        int ViewH { get { return Height; } }

        void ClampScroll()
        {
            int max = Math.Max(0, contentH - ViewH);
            if (scroll > max) scroll = max;
            if (scroll < 0) scroll = 0;
        }

        public int ScrollOffset { get { return scroll; } }

        public void EnsureVisibleStep(int idx)
        {
            int r = RowOfStep(idx);
            if (r >= 0) EnsureRowVisible(r);
        }

        void EnsureRowVisible(int r)
        {
            if (r < 0 || r >= rows.Count) return;
            Row row = rows[r];
            if (row.Y < scroll) scroll = row.Y - Ui.S(6);
            else if (row.Y + row.H > scroll + ViewH) scroll = row.Y + row.H - ViewH + Ui.S(6);
            ClampScroll();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ClampScroll();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll -= Math.Sign(e.Delta) * Ui.S(60);
            ClampScroll();
            Invalidate();
            base.OnMouseWheel(e);
        }

        Rectangle ThumbRect()
        {
            if (contentH <= ViewH) return Rectangle.Empty;
            int track = ViewH - Ui.S(8);
            int th = Math.Max(Ui.S(28), (int)((long)track * ViewH / contentH));
            int range = Math.Max(1, contentH - ViewH);
            int ty = Ui.S(4) + (int)((long)(track - th) * scroll / range);
            return new Rectangle(Width - Ui.S(9), ty, Ui.S(6), th);
        }

        // ---------- esposizione per i test ----------

        public int VisibleRowCount { get { return rows.Count; } }
        public int RowStepIndex(int row) { return rows[row].Index; }
        public int RowDepth(int row) { return rows[row].Depth; }
        public string RowKindName(int row) { return rows[row].Kind.ToString(); }
        public bool RowCollapsed(int row) { return rows[row].Collapsed; }
        public int RowHeight(int row) { return rows[row].H; }
        public int RowInner(int row) { return rows[row].Inner; }
        public int RowOf(int stepIndex) { return RowOfStep(stepIndex); }
        public int ContentHeight { get { return contentH; } }

        public Rectangle RowRect(int row)
        {
            Row r = rows[row];
            return new Rectangle(0, r.Y - scroll, Width - Ui.S(12), r.H);
        }

        // Alterna comprimi/espandi del blocco (o del gruppo) alla riga indicata.
        public void ToggleRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= rows.Count) return;
            Row r = rows[rowIndex];
            if (!r.Collapsible) return;
            if (r.Kind == RowKind.Header)
            {
                steps[r.Index].Collapsed = !steps[r.Index].Collapsed;
                if (steps[r.Index].Collapsed && selected > r.Index && selected <= r.EndIndex) { selected = r.Index; RaiseSelection(); }
            }
            else if (r.Kind == RowKind.Group)
            {
                if (r.Collapsed) expanded.Add(r.Index);
                else
                {
                    expanded.Remove(r.Index);
                    if (selected >= r.Index && selected <= r.EndIndex) { selected = r.Index; RaiseSelection(); }
                }
            }
            Rebuild();
        }

        void RaiseSelection()
        {
            EventHandler h = SelectionChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        // ---------- hit test ----------

        int RowAt(int clientY)
        {
            int y = clientY + scroll;
            for (int i = 0; i < rows.Count; i++)
                if (y >= rows[i].Y && y < rows[i].Y + rows[i].H) return i;
            return -1;
        }

        int ChevronRight(Row r) { return Ui.S(16) + r.Depth * Ui.S(22) + Ui.S(18); }

        // Indice (nella lista dei passi) davanti al quale si inserisce, per un puntatore a questa altezza.
        int InsertIndexAt(int clientY, out int lineY)
        {
            int y = clientY + scroll;
            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                if (y < r.Y + r.H / 2)
                {
                    lineY = r.Y - scroll;
                    return r.Index;
                }
            }
            lineY = contentH - Ui.S(6) - scroll;
            return steps.Count;
        }

        // ---------- mouse e tastiera ----------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button == MouseButtons.Left)
            {
                Rectangle th = ThumbRect();
                if (!th.IsEmpty && e.X >= Width - Ui.S(14))
                {
                    if (th.Contains(e.Location)) { sbDrag = true; sbGrab = e.Y - th.Y; }
                    else
                    {
                        scroll += e.Y < th.Y ? -ViewH : ViewH;
                        ClampScroll();
                        Invalidate();
                    }
                    base.OnMouseDown(e);
                    return;
                }
                int ri = RowAt(e.Y);
                if (ri >= 0)
                {
                    Row r = rows[ri];
                    if (r.Collapsible && e.X < ChevronRight(r)) { ToggleRow(ri); base.OnMouseDown(e); return; }
                    SelectedIndex = r.Index;
                    if (!ReadOnly && (r.Kind == RowKind.Step || r.Kind == RowKind.Header || r.Kind == RowKind.Group))
                    {
                        dragCandidate = true;
                        dragStart = e.Location;
                        dragFrom = r.Index;
                        dragTo = r.Kind == RowKind.Header || r.Kind == RowKind.Group ? r.EndIndex : r.Index;
                    }
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (sbDrag)
            {
                int track = ViewH - Ui.S(8);
                Rectangle th = ThumbRect();
                int range = Math.Max(1, track - th.Height);
                int pos = e.Y - sbGrab - Ui.S(4);
                scroll = (int)((long)Math.Max(0, Math.Min(range, pos)) * Math.Max(1, contentH - ViewH) / range);
                ClampScroll();
                Invalidate();
                return;
            }
            if (dragCandidate && !dragging && (Math.Abs(e.X - dragStart.X) > Ui.S(5) || Math.Abs(e.Y - dragStart.Y) > Ui.S(5)))
            {
                dragging = true;
                Capture = true;
                Cursor = Cursors.SizeAll;
            }
            if (dragging)
            {
                if (e.Y < Ui.S(24)) { scroll -= Ui.S(14); ClampScroll(); }
                else if (e.Y > Height - Ui.S(24)) { scroll += Ui.S(14); ClampScroll(); }
                int ly;
                dropIndex = InsertIndexAt(e.Y, out ly);
                dropY = ly;
                Invalidate();
                return;
            }
            int hr = RowAt(e.Y);
            if (hr != hoverRow)
            {
                hoverRow = hr;
                Invalidate();
                string t = null;
                if (hr >= 0 && rows[hr].Kind == RowKind.Header) t = StepStyle.Hint(steps[rows[hr].Index].Type);
                else if (hr >= 0 && rows[hr].Kind == RowKind.Group) t = L.T("Passi uguali consecutivi: clicca la freccia per aprirli.", "Identical consecutive steps: click the arrow to open them.");
                if (t != tipText)
                {
                    tipText = t;
                    tip.SetToolTip(this, t);
                }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            sbDrag = false;
            if (dragging)
            {
                dragging = false;
                Capture = false;
                Cursor = Cursors.Default;
                int ly;
                int ins = InsertIndexAt(e.Y, out ly);
                dropIndex = -1;
                Invalidate();
                Action<int, int, int> h = Reorder;
                if (h != null && !(ins >= dragFrom && ins <= dragTo + 1)) h(dragFrom, dragTo, ins);
            }
            dragCandidate = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hoverRow != -1) { hoverRow = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int ri = RowAt(e.Y);
            if (ri >= 0 && rows[ri].Collapsible) ToggleRow(ri);
            base.OnMouseDoubleClick(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right: case Keys.Delete:
                case Keys.Home: case Keys.End: case Keys.Enter: case Keys.PageUp: case Keys.PageDown:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int cur = selected >= 0 ? RowOfStep(selected) : -1;
            if (e.Alt && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))
            {
                Action<int> h = MoveRequested;
                if (h != null && !ReadOnly) h(e.KeyCode == Keys.Up ? -1 : 1);
                e.Handled = true;
                return;
            }
            switch (e.KeyCode)
            {
                case Keys.Up: if (cur > 0) SelectedIndex = rows[cur - 1].Index; else if (cur < 0 && rows.Count > 0) SelectedIndex = rows[0].Index; e.Handled = true; break;
                case Keys.Down: if (cur >= 0 && cur + 1 < rows.Count) SelectedIndex = rows[cur + 1].Index; else if (cur < 0 && rows.Count > 0) SelectedIndex = rows[0].Index; e.Handled = true; break;
                case Keys.Home: if (rows.Count > 0) SelectedIndex = rows[0].Index; e.Handled = true; break;
                case Keys.End: if (rows.Count > 0) SelectedIndex = rows[rows.Count - 1].Index; e.Handled = true; break;
                case Keys.PageUp: if (cur >= 0) SelectedIndex = rows[Math.Max(0, cur - 8)].Index; e.Handled = true; break;
                case Keys.PageDown: if (cur >= 0) SelectedIndex = rows[Math.Min(rows.Count - 1, cur + 8)].Index; e.Handled = true; break;
                case Keys.Left:
                    if (cur >= 0 && rows[cur].Collapsible && !rows[cur].Collapsed) ToggleRow(cur);
                    else if (cur >= 0) SelectParentOf(selected);
                    e.Handled = true;
                    break;
                case Keys.Right:
                    if (cur >= 0 && rows[cur].Collapsible && rows[cur].Collapsed) ToggleRow(cur);
                    e.Handled = true;
                    break;
                case Keys.Enter:
                    if (cur >= 0 && rows[cur].Collapsible) ToggleRow(cur);
                    e.Handled = true;
                    break;
                case Keys.Delete:
                    { Action h = DeleteRequested; if (h != null && !ReadOnly) h(); e.Handled = true; break; }
                case Keys.D:
                    if (e.Control) { Action h = DuplicateRequested; if (h != null && !ReadOnly) h(); e.Handled = true; }
                    break;
            }
            base.OnKeyDown(e);
        }

        void SelectParentOf(int idx)
        {
            int best = -1;
            for (int h = 0; h < idx; h++)
                if (IsHeader(steps[h]) && matchOf.Length > h && matchOf[h] > idx) best = h;
            if (best >= 0) SelectedIndex = best;
        }

        // ---------- drag & drop dalla palette ----------

        protected override void OnDragEnter(DragEventArgs e)
        {
            e.Effect = !ReadOnly && e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            if (ReadOnly || !e.Data.GetDataPresent(DragFormat)) { e.Effect = DragDropEffects.None; return; }
            e.Effect = DragDropEffects.Copy;
            Point p = PointToClient(new Point(e.X, e.Y));
            if (p.Y < Ui.S(24)) { scroll -= Ui.S(14); ClampScroll(); }
            else if (p.Y > Height - Ui.S(24)) { scroll += Ui.S(14); ClampScroll(); }
            int ly;
            dropIndex = InsertIndexAt(p.Y, out ly);
            dropY = ly;
            Invalidate();
        }

        protected override void OnDragLeave(EventArgs e)
        {
            dropIndex = -1;
            Invalidate();
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            dropIndex = -1;
            Invalidate();
            if (ReadOnly || !e.Data.GetDataPresent(DragFormat)) return;
            int t = (int)e.Data.GetData(DragFormat);
            Point p = PointToClient(new Point(e.X, e.Y));
            int ly;
            int ins = InsertIndexAt(p.Y, out ly);
            Action<StepType, int> h = PaletteDrop;
            if (h != null) h((StepType)t, ins);
        }

        // ---------- testo delle righe ----------

        static string Coord(MacroStep s) { return "(" + s.MoveX + ", " + s.MoveY + ")"; }

        string TitleOf(Row r)
        {
            if (r.Kind == RowKind.Group)
                return L.T("Sequenza di " + r.Inner + " tasti", "Sequence of " + r.Inner + " keys");
            return StepStyle.Title(steps[r.Index]);
        }

        string BadgeOf(Row r)
        {
            if (r.Kind == RowKind.Group) return StepStyle.Badge(StepType.Keys);
            return StepStyle.Badge(steps[r.Index].Type);
        }

        // Dettaglio compatto accanto al nome (lo stato live ha la precedenza).
        string DetailOf(Row r, out Color swatch)
        {
            swatch = Color.Empty;
            Color c;
            if (r.Kind == RowKind.Group)
            {
                List<string> keys = new List<string>();
                for (int k = r.Index; k <= r.EndIndex && keys.Count < 9; k++) keys.Add(steps[k].Input);
                string joined = string.Join(" · ", keys.ToArray()) + (r.Inner > keys.Count ? " …" : "");
                return joined + "  " + L.T("tenuto ", "hold ") + StepStyle.Fmt(steps[r.Index].DurationMs);
            }
            MacroStep s = steps[r.Index];
            string d = "";
            switch (s.Type)
            {
                case StepType.Keys:
                    d = s.RandDur ? L.T("tenuto ", "hold ") + StepStyle.RandInfo(s.DurMin, s.DurMax, s.DurLongPct, s.DurLongMax) : L.T("tenuto", "hold");
                    break;
                case StepType.RandomKey:
                    d = L.T("uno tra: ", "one of: ") + s.Input;
                    break;
                case StepType.Click:
                    d = L.T("pulsante ", "button ") + L.Button(s.Input);
                    break;
                case StepType.ClickAt:
                    d = Coord(s) + " · " + L.T("pulsante ", "button ") + L.Button(s.Input) + (s.PosJitter > 0 ? " ± " + s.PosJitter + " px" : "");
                    break;
                case StepType.MoveTo:
                    d = L.T("porta il cursore", "moves the cursor") + (s.PosJitter > 0 ? " ± " + s.PosJitter + " px" : "");
                    break;
                case StepType.MouseMove:
                    d = (s.RandMove ? L.T("casuale entro ± ", "random within ± ") : L.T("di ", "by ")) + Coord(s) + " px";
                    break;
                case StepType.Wait:
                    d = "";
                    break;
                case StepType.WaitPixel:
                case StepType.WaitPixelGone:
                    if (ScreenPixel.TryParseHex(s.Input, out c)) swatch = c;
                    d = (s.Type == StepType.WaitPixel ? L.T("compare ", "appears ") : L.T("sparisce ", "disappears ")) + Coord(s) + StepStyle.CondShort(s) + " · max " + StepStyle.Fmt(s.DurationMs);
                    break;
                case StepType.IfStart:
                    if (s.CondKind == 1)
                    {
                        d = (s.Negate ? L.T("non vale: ", "not: ") : "") + StepStyle.VarCondText(s) + StepStyle.CondShort(s);
                    }
                    else if (s.CondKind == 2)
                    {
                        d = (s.Negate ? L.T("nel ", "in the other ") + (100 - s.ChancePct) + L.T("% dei casi", "% of cases") : L.T("nel ", "in ") + s.ChancePct + L.T("% dei casi", "% of cases")) + StepStyle.CondShort(s);
                    }
                    else
                    {
                        if (ScreenPixel.TryParseHex(s.Input, out c)) swatch = c;
                        d = (s.Negate ? L.T("assente ", "absent ") : L.T("presente ", "present ")) + Coord(s) + StepStyle.CondShort(s);
                    }
                    break;
                case StepType.ChoiceStart:
                    {
                        int alts = matchOf.Length > r.Index ? ChoiceAlternatives(r.Index) : 0;
                        d = alts > 0 ? L.T("una tra " + alts + " alternative", "one of " + alts + " alternatives") : L.T("nessuna alternativa", "no alternatives");
                        if (r.Collapsed) d += " · " + r.Inner + " step";
                        if (s.AltWeight > 1 && alts > 1) d += " · " + L.T("la 1ª pesa ", "1st weighs ") + s.AltWeight;
                        break;
                    }
                case StepType.Option:
                    d = "";
                    break;
                case StepType.SetVar:
                    switch (s.VarOp)
                    {
                        case 1: d = L.T("aggiunge ", "adds ") + s.CmpValue; break;
                        case 2: d = L.T("toglie ", "subtracts ") + s.CmpValue; break;
                        case 3: d = L.T("valore a caso tra ", "random value between ") + Math.Min(s.CmpValue, s.VarValue2) + L.T(" e ", " and ") + Math.Max(s.CmpValue, s.VarValue2); break;
                        default: d = L.T("imposta a ", "sets to ") + s.CmpValue; break;
                    }
                    break;
                case StepType.Combo:
                    d = StepStyle.ComboSummary(s);
                    break;
                case StepType.LoopStart:
                    {
                        List<string> parts = new List<string>();
                        if (r.Collapsed) parts.Add(r.Inner + " " + L.T(r.Inner == 1 ? "step" : "step", "step"));
                        if (s.UseUntil)
                        {
                            if (ScreenPixel.TryParseHex(s.Input, out c)) swatch = c;
                            int nc = s.ExtraCount + 1;
                            if (nc > 1)
                                parts.Add(s.CondJoin == 1 ? L.T("esce con tutte e " + nc + " le condizioni", "exits when all " + nc + " conditions hold")
                                                          : L.T("esce con una tra " + nc + " condizioni", "exits on any of " + nc + " conditions"));
                            else
                                parts.Add((s.Negate ? L.T("esce se assente ", "exits if absent ") : L.T("esce se presente ", "exits if present ")) + Coord(s));
                        }
                        if (s.Count > 0) parts.Add("max " + s.Count + L.T(" giri", " rounds"));
                        if (s.DurationMs > 0) parts.Add("max " + StepStyle.Fmt(s.DurationMs));
                        if (parts.Count == 0 || (parts.Count == 1 && r.Collapsed && !s.UseUntil && s.Count == 0 && s.DurationMs == 0 && false)) parts.Add(L.T("senza limite", "no limit"));
                        d = string.Join(" · ", parts.ToArray());
                        break;
                    }
                case StepType.Goto:
                    d = "";
                    break;
                case StepType.Label:
                    d = L.T("punto di arrivo dei Goto", "target of Goto steps");
                    break;
                case StepType.Break:
                    d = L.T("esce dal ciclo", "leaves the loop");
                    break;
                case StepType.StopMacro:
                    d = L.T("ferma tutto", "stops everything");
                    break;
                case StepType.Reset:
                    d = L.T("riparte dal primo passo", "starts again from the first step")
                        + (s.ResetVars ? L.T(" · azzera le variabili", " · clears the variables") : "");
                    break;
            }
            if (s.RandPause && s.HasPause)
                d += (d.Length > 0 ? " · " : "") + L.T("pausa ", "pause ") + StepStyle.FmtRange(s.PauseMin, s.PauseMax);
            return d;
        }

        // Durata mostrata a destra quando non si esegue.
        string RightOf(Row r)
        {
            if (r.Kind == RowKind.Group)
            {
                long total = 0;
                for (int k = r.Index; k <= r.EndIndex; k++) total += steps[k].DurationMs + steps[k].PauseMs;
                return StepStyle.Fmt((int)Math.Min(int.MaxValue, total));
            }
            MacroStep s = steps[r.Index];
            switch (s.Type)
            {
                case StepType.Keys: case StepType.RandomKey: case StepType.Click: case StepType.ClickAt:
                case StepType.MouseMove: case StepType.Wait:
                    return s.RandDur ? StepStyle.FmtRange(s.DurMin, s.DurMax) : StepStyle.Fmt(s.DurationMs);
                case StepType.WaitPixel: case StepType.WaitPixelGone:
                    return "max " + StepStyle.Fmt(s.DurationMs);
                case StepType.Combo:
                    {
                        // dura quanto la parte più lunga: da (max dei minimi) a (max dei massimi)
                        int lo = 0, hi = 0;
                        if (s.Parts != null)
                            foreach (ComboPart p in s.Parts)
                            {
                                if (p == null || !p.Enabled) continue;
                                lo = Math.Max(lo, Math.Min(p.DurMin, p.DurMax));
                                hi = Math.Max(hi, Math.Max(p.DurMax, p.LongPct > 0 ? p.LongMax : 0));
                            }
                        return hi <= 0 ? "" : StepStyle.FmtRange(lo, Math.Max(lo, hi));
                    }
                case StepType.LoopStart:
                    {
                        if (matchOf.Length <= r.Index || matchOf[r.Index] < 0) return "";
                        long body = EstimateBody(r.Index);
                        return body > 0 ? StepStyle.Fmt((int)Math.Min(int.MaxValue, body)) + " / " + L.T("giro", "round") : "";
                    }
            }
            return "";
        }

        // Intestazione "Scegli a caso" che contiene il passo (usa la struttura locale), oppure -1.
        int ChoiceHeaderOf(int idx)
        {
            if (matchOf == null) return -1;
            for (int h = Math.Min(idx, steps.Count) - 1; h >= 0; h--)
            {
                if (steps[h].IsBlockHeader && h < matchOf.Length && matchOf[h] > idx)
                    return steps[h].Type == StepType.ChoiceStart ? h : -1;
            }
            return -1;
        }

        // Numero di alternative di una "Scegli a caso" (separate da Opzione, altrimenti un passo/blocco ciascuna).
        int ChoiceAlternatives(int header)
        {
            if (header < 0 || header >= matchOf.Length || matchOf[header] <= header) return 0;
            int end = matchOf[header], options = 0, direct = 0;
            for (int j = header + 1; j < end; j++)
            {
                MacroStep c = steps[j];
                if (c.Type == StepType.Option) options++;
                else direct++;
                if (c.IsBlockHeader && matchOf[j] > j && matchOf[j] < end) j = matchOf[j];
            }
            return options > 0 ? options + 1 : direct;
        }

        // "Oppure · alternativa 2 · peso 3"
        string OptionLabel(int idx)
        {
            int h = ChoiceHeaderOf(idx);
            int n = 2;
            if (h >= 0)
            {
                for (int j = h + 1; j < idx; j++)
                {
                    if (steps[j].Type == StepType.Option) n++;
                    if (steps[j].IsBlockHeader && matchOf[j] > j && matchOf[j] < idx) j = matchOf[j];
                }
            }
            int w = Math.Max(1, steps[idx].Weight);
            return L.T("Oppure · alternativa ", "Or · alternative ") + n + (w > 1 ? L.T(" · peso ", " · weight ") + w : "");
        }

        long EstimateBody(int header)
        {
            if (Flow.Prepare(steps) != null) return 0;
            return Estimator.BodyMs(steps, header);
        }

        // ---------- disegno ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Pal.Panel);

            // stato dello step attivo (una sola lettura per fotogramma)
            int curList = -1, curIdx = -1, planned = 0;
            long curEl = 0;
            string curLabel = null;
            if (runner != null) runner.GetCurrent(out curList, out curIdx, out curEl, out planned, out curLabel);

            int viewTop = 0, viewBottom = Height;
            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                int y = r.Y - scroll;
                if (y + r.H < viewTop) continue;
                if (y > viewBottom) break;
                PaintRow(g, r, i, y, curList == listId ? curIdx : -1, planned, curEl, curLabel);
            }

            if (rows.Count == 0)
            {
                string msg = L.T("Nessun passo. Trascina un blocco dalla palette o fai doppio clic su un blocco per aggiungerlo.",
                                 "No steps yet. Drag a block from the palette or double-click a block to add it.");
                Ui.Text(g, msg, Ui.F(9.5f), new Rectangle(Ui.S(20), Ui.S(20), Width - Ui.S(40), Ui.S(40)), Pal.Muted,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            }

            if (dropIndex >= 0 && dropY >= -Ui.S(4))
            {
                int x0 = Ui.S(10);
                using (Pen p = new Pen(Pal.Focus, 2f)) g.DrawLine(p, x0, dropY, Width - Ui.S(14), dropY);
                using (SolidBrush b = new SolidBrush(Pal.Focus)) g.FillEllipse(b, x0 - Ui.S(3), dropY - Ui.S(3), Ui.S(7), Ui.S(7));
            }

            Rectangle th = ThumbRect();
            if (!th.IsEmpty)
            {
                Ui.FillRound(g, th, Ui.S(3), sbDrag ? Pal.Muted : Pal.RaisedBorder);
            }
        }

        void PaintRow(Graphics g, Row r, int rowIndex, int y, int activeStep, int planned, long curEl, string curLabel)
        {
            float sc = Ui.Scale;
            int w = Width - Ui.S(12);
            Rectangle rc = new Rectangle(0, y, w, r.H);
            int pl = Ui.S(16) + r.Depth * Ui.S(22);

            // guide verticali dei livelli che contengono la riga
            for (int lvl = 0; lvl < r.Depth; lvl++)
            {
                int gx = Ui.S(16) + lvl * Ui.S(22) + Ui.S(6);
                using (Pen p = new Pen(Pal.Guide)) g.DrawLine(p, gx, rc.Top, gx, rc.Bottom);
            }

            if (r.Kind == RowKind.Footer || r.Kind == RowKind.Else)
            {
                bool isElse = r.Kind == RowKind.Else;
                MacroStep ms = steps[r.Index];
                string label = isElse ? (ms.Type == StepType.Option ? OptionLabel(r.Index) : L.T("Altrimenti", "Else"))
                    : (ms.Type == StepType.LoopEnd ? L.T("Fine ripeti", "End repeat") : (ms.Type == StepType.ChoiceEnd ? L.T("Fine scelta", "End pick") : L.T("Fine se", "End if")));
                if (selected == r.Index) Ui.FillRound(g, new Rectangle(rc.X + Ui.S(4), rc.Y, rc.Width - Ui.S(8), rc.Height), Ui.S(4), Pal.SelRow);
                Ui.Text(g, label, Ui.F(8.25f, isElse), new Rectangle(pl + Ui.S(22), rc.Y, rc.Width - pl, rc.Height),
                    isElse ? Pal.Warn : Pal.Faint, Ui.Left);
                return;
            }

            MacroStep s = steps[r.Index];
            bool header = r.Kind == RowKind.Header;
            bool group = r.Kind == RowKind.Group;
            Sem sem = StepStyle.SemOf(r.Kind == RowKind.Group ? StepType.Keys : s.Type);
            bool live = runner != null;

            StepRunState st = StepRunState.Pending;
            LoopInfo li = new LoopInfo();
            int cond = -1;
            bool containsActive = false;
            if (live)
            {
                if (group)
                {
                    bool allDone = true, any = false;
                    for (int k = r.Index; k <= r.EndIndex; k++)
                    {
                        StepRunState ks = runner.GetState(listId, k);
                        if (ks != StepRunState.Done) allDone = false;
                        if (ks != StepRunState.Pending) any = true;
                    }
                    st = allDone ? StepRunState.Done : (any ? StepRunState.Active : StepRunState.Pending);
                    containsActive = activeStep >= r.Index && activeStep <= r.EndIndex;
                }
                else
                {
                    st = runner.GetState(listId, r.Index);
                    if (header)
                    {
                        li = runner.GetLoop(listId, r.Index);
                        cond = runner.GetCond(listId, r.Index);
                        containsActive = activeStep >= r.Index && activeStep <= r.EndIndex;
                    }
                }
            }
            bool isActiveStep = live && !header && !group && activeStep == r.Index && st == StepRunState.Active;
            bool runContainer = live && (header || group) && containsActive;
            bool done = live && st == StepRunState.Done && !runContainer;

            // sfondo
            Color bg = header ? Pal.HeaderRow : Pal.Panel;
            bool isSel = selected >= r.Index && selected <= r.EndIndex && (r.Kind != RowKind.Group || r.Collapsed) && !(header && selected != r.Index);
            if (isSel || (!live && selected == r.Index)) bg = Pal.SelRow;
            if (isActiveStep || (group && containsActive && r.Collapsed)) bg = Pal.SelRow;
            else if (hoverRow == rowIndex && !isSel && !live) bg = Pal.RowHover;
            if (bg != Pal.Panel)
            {
                using (SolidBrush b = new SolidBrush(bg)) g.FillRectangle(b, rc);
            }
            if (header)
            {
                // barra laterale colorata (3 px): solo qui il colore pieno dei blocchi
                using (SolidBrush b = new SolidBrush(Pal.Dot(sem)))
                    g.FillRectangle(b, pl - Ui.S(14), rc.Y + Ui.S(3), Ui.S(3), rc.Height - Ui.S(6));
            }
            if (isSel || isActiveStep)
            {
                using (Pen p = new Pen(Pal.Focus, 1f)) g.DrawRectangle(p, rc.X, rc.Y, rc.Width - 1, rc.Height - 1);
            }
            else if (runContainer)
            {
                using (Pen p = new Pen(Pal.RunRing, 1f)) g.DrawRectangle(p, rc.X, rc.Y, rc.Width - 1, rc.Height - 1);
            }
            if (isActiveStep)
            {
                // alone che pulsa attorno allo step in esecuzione
                double pulse = 0.5 - 0.5 * Math.Cos(2 * Math.PI * (Environment.TickCount % 1100) / 1100.0);
                using (Pen p = new Pen(Color.FromArgb((int)(40 + 90 * pulse), Pal.Focus), 3f * sc))
                    g.DrawRectangle(p, rc.X + 1, rc.Y + 1, rc.Width - 3, rc.Height - 3);
            }

            Color under = bg;
            int midY = rc.Y + rc.Height / 2;
            int x = pl;

            // freccia comprimi/espandi
            if (r.Collapsible)
            {
                int cs = Ui.S(5);
                using (SolidBrush b = new SolidBrush(Pal.Muted))
                {
                    if (r.Collapsed)
                        g.FillPolygon(b, new Point[] { new Point(x + Ui.S(2), midY - cs), new Point(x + Ui.S(2) + cs, midY), new Point(x + Ui.S(2), midY + cs) });
                    else
                        g.FillPolygon(b, new Point[] { new Point(x, midY - cs / 2), new Point(x + cs * 2, midY - cs / 2), new Point(x + cs, midY + cs / 2 + 1) });
                }
            }
            x += Ui.S(12) + Ui.S(10);

            // icona di stato (solo durante l'esecuzione)
            if (live)
            {
                int ix = x + Ui.S(7), iy = midY;
                if (st == StepRunState.Done && !runContainer)
                    Icons.Draw(g, Glyph.Check, new Rectangle(ix - Ui.S(7), iy - Ui.S(7), Ui.S(14), Ui.S(14)), Pal.Dot(Sem.Input));
                else if (isActiveStep || runContainer || (group && st == StepRunState.Active))
                {
                    double pulse = 0.55 + 0.45 * Math.Cos(2 * Math.PI * (Environment.TickCount % 1100) / 1100.0);
                    Color dc = Pal.Over(under, Pal.Dot(Sem.Flow), Math.Max(0.25, pulse));
                    using (SolidBrush b = new SolidBrush(dc)) g.FillEllipse(b, ix - Ui.S(4), iy - Ui.S(4), Ui.S(8), Ui.S(8));
                }
                else
                {
                    using (Pen p = new Pen(Pal.Pending, 1.4f)) g.DrawEllipse(p, ix - Ui.S(3), iy - Ui.S(3), Ui.S(6), Ui.S(6));
                }
                x += Ui.S(14) + Ui.S(10);
            }

            // badge
            string badge = BadgeOf(r);
            Font bf = Ui.M(7.5f, true);
            int bw = Math.Max(Ui.S(46), Ui.Measure(g, badge, bf) + Ui.S(16));
            Rectangle br = new Rectangle(x, midY - Ui.S(9), bw, Ui.S(18));
            Ui.FillRound(g, br, Ui.S(4), Pal.BadgeBg(sem, under));
            Ui.Text(g, badge, bf, br, Pal.BadgeFg(sem), Ui.Center);
            x += bw + Ui.S(10);

            // zona destra: testo di durata / avanzamento
            int right = rc.Right - Ui.S(16);
            string rightText;
            Color rightCol = Pal.Muted;
            double pct = -1;
            string detailOverride = null;
            Color swatch;
            string detail = DetailOf(r, out swatch);

            if (live)
            {
                if (isActiveStep)
                {
                    rightCol = Pal.Info;
                    if (s.Type == StepType.Wait)
                    {
                        rightText = StepStyle.Fmt(planned) + " → " + Ui.Secs(Math.Max(0, planned - curEl));
                    }
                    else if (s.Type == StepType.WaitPixel || s.Type == StepType.WaitPixelGone)
                    {
                        rightText = Ui.Secs(curEl) + " / " + Ui.Secs(planned);
                    }
                    else if (planned > 0)
                    {
                        rightText = planned < 10000 ? Math.Min(curEl, planned) + " / " + planned + " ms" : Ui.Secs(curEl) + " / " + Ui.Secs(planned);
                    }
                    else rightText = "";
                    if (planned > 0) pct = Math.Min(1.0, curEl / (double)planned);
                    if (!string.IsNullOrEmpty(curLabel) && (s.Type == StepType.RandomKey || s.Type == StepType.MouseMove || s.Type == StepType.Combo))
                        detailOverride = "→ " + curLabel;
                }
                else if (header && (runContainer || st == StepRunState.Active))
                {
                    rightCol = Pal.Info;
                    rightText = Ui.Hms(li.ElapsedMs);
                    if (s.Type == StepType.LoopStart)
                        detailOverride = L.T("giro ", "round ") + Math.Max(1, li.Iteration) + (li.MaxIterations > 0 ? "/" + li.MaxIterations : "");
                    else if (s.Type == StepType.IfStart)
                        detailOverride = cond < 0 ? L.T("valuto…", "evaluating…") : (cond == 1 ? L.T("vero", "true") : L.T("falso", "false"));
                    else if (s.Type == StepType.ChoiceStart)
                        detailOverride = cond < 0 ? L.T("scelgo…", "picking…") : L.T("alternativa ", "alternative ") + (cond + 1) + "/" + ChoiceAlternatives(r.Index);
                }
                else if (header && st == StepRunState.Done)
                {
                    rightText = Ui.Secs(li.ElapsedMs);
                    if (s.Type == StepType.LoopStart)
                        detailOverride = li.Iteration > 0 ? L.T("uscito al giro ", "left at round ") + li.Iteration
                            : L.T("saltato", "skipped");
                    else if (s.Type == StepType.IfStart)
                        detailOverride = cond == 1 ? L.T("vero", "true") : L.T("falso · saltato", "false · skipped");
                    else if (s.Type == StepType.ChoiceStart)
                        detailOverride = cond < 0 ? L.T("saltato", "skipped") : L.T("era l'alternativa ", "was alternative ") + (cond + 1) + "/" + ChoiceAlternatives(r.Index);
                }
                else rightText = RightOf(r);
            }
            else rightText = RightOf(r);

            Font mono = Ui.M(9f);
            int rw = string.IsNullOrEmpty(rightText) ? 0 : Ui.Measure(g, rightText, mono);
            if (rw > 0)
            {
                Ui.Text(g, rightText, mono, new Rectangle(right - rw - 2, rc.Y, rw + 4, rc.Height), rightCol, Ui.Right);
                right -= rw + Ui.S(14);
            }

            // nome, campione di colore e dettaglio
            string title = TitleOf(r);
            Font tf = Ui.F(9.5f, true);
            Color nameCol = done ? Pal.Text2 : Pal.TextStrong;
            int avail = Math.Max(Ui.S(40), right - x);
            int tw = Math.Min(Ui.Measure(g, title, tf) + 2, (int)(avail * 0.62));
            Ui.Text(g, title, tf, new Rectangle(x, rc.Y, tw, rc.Height), nameCol, Ui.Left);
            x += tw + Ui.S(10);
            if (!swatch.IsEmpty && x + Ui.S(14) < right)
            {
                Rectangle sr = new Rectangle(x, midY - Ui.S(6), Ui.S(12), Ui.S(12));
                Ui.FillRound(g, sr, Ui.S(3), swatch);
                Ui.DrawRound(g, sr, Ui.S(3), Pal.Over(under, Color.White, 0.25), 1f);
                x += Ui.S(12) + Ui.S(8);
            }
            string dtxt = detailOverride ?? detail;
            if (!string.IsNullOrEmpty(dtxt) && right - x > Ui.S(20))
            {
                Color dcol = detailOverride != null ? (done ? Pal.Muted : Pal.Text2) : Pal.Muted;
                Ui.Text(g, dtxt, Ui.F(9f), new Rectangle(x, rc.Y, right - x, rc.Height), dcol, Ui.Left);
            }

            // barra di avanzamento sotto la riga attiva
            if (pct >= 0)
            {
                Rectangle tr = new Rectangle(pl, rc.Bottom - Ui.S(3), rc.Right - Ui.S(16) - pl, Ui.S(2));
                using (SolidBrush b = new SolidBrush(Pal.ProgressTrack)) g.FillRectangle(b, tr);
                using (SolidBrush b = new SolidBrush(Pal.Dot(Sem.Flow)))
                    g.FillRectangle(b, tr.X, tr.Y, (int)(tr.Width * pct), tr.Height);
            }
        }
    }
}
