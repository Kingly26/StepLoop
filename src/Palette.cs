using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StepLoop
{
    // Colonna sinistra: i blocchi, raggruppati per famiglia. Doppio clic o trascinamento nella lista per aggiungere.
    // Se la colonna è bassa scorre con la rotella.
    public sealed class PaletteView : Control
    {
        sealed class Item
        {
            public string It, En, HintIt, HintEn;
            public StepType Type;
            public bool Section;
            public int Top;
            public int H;
        }

        readonly List<Item> items = new List<Item>();
        int hot = -1;
        int pressed = -1;
        int scroll;
        int contentH;
        Point downPoint;
        readonly ToolTip tip = new ToolTip();
        string tipText;

        public bool Locked;
        public event Action<StepType> AddRequested;

        public PaletteView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardDoubleClick, true);
            TabStop = true;
            tip.InitialDelay = 450;

            Section("INPUT", "INPUT");
            Add("Tasti", "Keys", StepType.Keys, "Tiene premuti uno o più tasti per il tempo indicato.", "Holds one or more keys for the given time.");
            Add("Casuale", "Random key", StepType.RandomKey, "Un tasto a caso tra quelli che definisci (es. W, A, S, D o A-Z).", "A random key from the set you define (e.g. W, A, S, D or A-Z).");
            Add("Click", "Click", StepType.Click, "Clicca dove si trova il cursore.", "Clicks where the cursor is.");
            Add("Click su…", "Click at…", StepType.ClickAt, "Sposta il cursore in un punto e clicca.", "Moves the cursor to a point and clicks.");
            Add("Vai a…", "Move to…", StepType.MoveTo, "Porta il cursore in un punto senza cliccare (fa comparire l'hover).", "Moves the cursor to a point without clicking (shows the hover).");
            Add("Mouse", "Mouse", StepType.MouseMove, "Movimento relativo del mouse (per guardarsi intorno).", "Relative mouse movement (to look around).");
            Add("Insieme", "Together", StepType.Combo, "Più azioni nello stesso istante: tasti, click, mouse e mira, ognuna con la propria durata casuale.", "Several actions at the same moment: keys, click, mouse and aim, each with its own random duration.");
            Section("FLUSSO", "FLOW");
            Add("Ripeti", "Repeat", StepType.LoopStart, "Un ciclo: i passi dentro si ripetono, e puoi uscire quando compare un colore.", "A loop: the steps inside repeat, and you can leave when a color appears.");
            Add("Se…", "If…", StepType.IfStart, "Esegue i passi dentro solo se un colore è (o non è) presente.", "Runs the steps inside only if a color is (or is not) present.");
            Add("Altrimenti", "Else", StepType.Else, "Ramo 'altrimenti' del Se che contiene il passo selezionato.", "The 'else' branch of the If that contains the selected step.");
            Add("Scegli a caso", "Pick random", StepType.ChoiceStart, "Esegue UNA sola alternativa scelta a caso (con pesi). Le alternative sono i passi dentro, o i gruppi separati da 'Opzione'.", "Runs ONE alternative picked at random (with weights). Alternatives are the steps inside, or groups separated by 'Option'.");
            Add("Opzione", "Option", StepType.Option, "Separa le alternative di una 'Scegli a caso': ogni opzione può contenere più passi.", "Separates the alternatives of a 'Pick random': each option can hold several steps.");
            Add("Variabile…", "Variable…", StepType.SetVar, "Imposta, somma o estrae a caso un numero in una variabile, da usare nei 'Se'.", "Sets, adds or randomly draws a number into a variable, to use in 'If' steps.");
            Add("Goto…", "Goto…", StepType.Goto, "Salta direttamente a un'etichetta (anche fuori da un ciclo o da un Se).", "Jumps straight to a label (even out of a loop or an If).");
            Add("Etichetta", "Label", StepType.Label, "Punto di arrivo con un nome per i Goto.", "A named landing point for Goto steps.");
            Add("Esci", "Exit", StepType.Break, "Esce subito dal ciclo.", "Leaves the loop right away.");
            Add("Reset", "Reset", StepType.Reset, "Interrompe tutto e ricomincia dal primo passo della sequenza, da qualsiasi punto (anche dentro cicli e Se). Non ferma la macro.", "Aborts everything and starts again from the first step of the sequence, from anywhere (even inside loops and Ifs). It does not stop the macro.");
            Add("Stop", "Stop", StepType.StopMacro, "Ferma tutta la macro.", "Stops the whole macro.");
            Section("ATTESA", "WAIT");
            Add("Pausa", "Wait", StepType.Wait, "Attesa a tempo (anche casuale).", "Timed wait (optionally random).");
            Add("Attendi…", "Wait for…", StepType.WaitPixel, "Aspetta che un colore compaia in un'area dello schermo.", "Waits for a color to appear in a screen area.");
        }

        void Section(string it, string en)
        {
            Item i = new Item();
            i.It = it; i.En = en; i.Section = true;
            items.Add(i);
        }

        void Add(string it, string en, StepType t, string hit, string hen)
        {
            Item i = new Item();
            i.It = it; i.En = en; i.Type = t; i.HintIt = hit; i.HintEn = hen;
            items.Add(i);
        }

        int Pad { get { return Ui.S(8); } }

        // posizioni verticali (senza scorrimento) e altezza totale
        void ComputeLayout()
        {
            int y = Ui.S(8);
            for (int k = 0; k < items.Count; k++)
            {
                Item it = items[k];
                it.H = it.Section ? Ui.S(24) : Ui.S(27);
                if (it.Section && k > 0) y += Ui.S(6);
                it.Top = y;
                y += it.H;
            }
            contentH = y + Ui.S(8);
        }

        public int ContentHeight { get { ComputeLayout(); return contentH; } }

        void ClampScroll()
        {
            ComputeLayout();
            int max = Math.Max(0, contentH + Ui.S(46) - Height);
            if (scroll > max) scroll = max;
            if (scroll < 0) scroll = 0;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ClampScroll();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll -= Math.Sign(e.Delta) * Ui.S(54);
            ClampScroll();
            Invalidate();
            base.OnMouseWheel(e);
        }

        int HitTest(int y)
        {
            ComputeLayout();
            int yy = y + scroll;
            for (int k = 0; k < items.Count; k++)
            {
                Item it = items[k];
                if (yy >= it.Top && yy < it.Top + it.H) return it.Section ? -1 : k;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (pressed >= 0 && !Locked && e.Button == MouseButtons.Left
                && (Math.Abs(e.X - downPoint.X) > Ui.S(5) || Math.Abs(e.Y - downPoint.Y) > Ui.S(5)))
            {
                int k = pressed;
                pressed = -1;
                DataObject d = new DataObject();
                d.SetData(StepListControl.DragFormat, (int)items[k].Type);
                DoDragDrop(d, DragDropEffects.Copy);
                return;
            }
            int h = HitTest(e.Y);
            if (h != hot)
            {
                hot = h;
                Invalidate();
                string t = h >= 0 ? L.T(items[h].HintIt, items[h].HintEn) : null;
                if (t != tipText) { tipText = t; tip.SetToolTip(this, t); }
            }
            Cursor = h >= 0 && !Locked ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            pressed = HitTest(e.Y);
            downPoint = e.Location;
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { pressed = -1; base.OnMouseUp(e); }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int h = HitTest(e.Y);
            if (h >= 0 && !Locked)
            {
                Action<StepType> a = AddRequested;
                if (a != null) a(items[h].Type);
            }
            base.OnMouseDoubleClick(e);
        }

        // per i test: attiva la voce con il tipo indicato come un doppio clic
        public void Activate(StepType t)
        {
            if (Locked) return;
            Action<StepType> a = AddRequested;
            if (a != null) a(t);
        }

        public bool Contains(StepType t)
        {
            foreach (Item i in items) if (!i.Section && i.Type == t) return true;
            return false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Pal.Panel);
            ComputeLayout();
            ClampScroll();
            for (int k = 0; k < items.Count; k++)
            {
                Item it = items[k];
                int top = it.Top - scroll;
                if (top + it.H < 0 || top > Height) continue;
                if (it.Section)
                {
                    Ui.Text(g, L.T(it.It, it.En), Ui.F(7.5f, true), new Rectangle(Pad + Ui.S(8), top, Width - Pad * 2, it.H), Locked ? Pal.Faint : Pal.Muted, Ui.Left);
                    continue;
                }
                Rectangle r = new Rectangle(Pad, top, Width - Pad * 2, it.H);
                if (k == hot && !Locked) Ui.FillRound(g, r, Ui.S(6), Pal.Raised);
                Sem sem = StepStyle.SemOf(it.Type);
                int d = Ui.S(8);
                Color dot = Locked ? Pal.Over(Pal.Panel, Pal.Dot(sem), 0.5) : Pal.Dot(sem);
                using (SolidBrush b = new SolidBrush(dot)) g.FillEllipse(b, r.X + Ui.S(10), r.Y + (r.Height - d) / 2, d, d);
                Ui.Text(g, L.T(it.It, it.En), Ui.F(9.5f), new Rectangle(r.X + Ui.S(28), r.Y, r.Width - Ui.S(30), r.Height),
                    Locked ? Pal.Faint : Pal.Text2, Ui.Left);
            }
            // suggerimento in fondo (se c'è posto) oppure barra di scorrimento sottile
            int hintTop = contentH - scroll;
            if (hintTop + Ui.S(40) <= Height)
            {
                string hint = L.T("Doppio clic o trascina nella lista per aggiungere.", "Double-click or drag into the list to add.");
                Rectangle hr = new Rectangle(Pad + Ui.S(8), Math.Max(hintTop, Height - Ui.S(44)), Width - Pad * 2 - Ui.S(8), Ui.S(40));
                TextRenderer.DrawText(g, hint, Ui.F(8.25f), hr, Pal.Muted, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            }
            else
            {
                int track = Height - Ui.S(8);
                int total = contentH + Ui.S(46);
                int th = Math.Max(Ui.S(24), (int)((long)track * Height / total));
                int range = Math.Max(1, total - Height);
                int ty = Ui.S(4) + (int)((long)(track - th) * scroll / range);
                Ui.FillRound(g, new Rectangle(Width - Ui.S(8), ty, Ui.S(4), th), Ui.S(2), Pal.RaisedBorder);
            }
        }
    }
}
