using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace StepLoop
{
    // Conversione della risoluzione: sceglie la risoluzione per cui è fatto il profilo e quella di destinazione
    // (da un menu a tendina oppure scrivendo larghezza e altezza); mostra subito cosa cambierà.
    public class ResolutionDialog : Form
    {
        // risultato
        public int FromW, FromH, ToW, ToH, ShiftX, ShiftY;

        readonly Profile profile;
        readonly Size screen;
        DarkCombo cbFrom, cbTo;
        NumBox nudFromW, nudFromH, nudToW, nudToH, nudShiftX, nudShiftY;
        FlatButton btnMyScreen, btnOk, btnCancel;
        UiLabel lblInfo, lblFromNote, lblSummary, lblWarn;
        bool busy;

        public ResolutionDialog(Profile p, Size screenSize)
        {
            profile = p;
            screen = screenSize;
            SuspendLayout();
            Text = L.T("Risoluzione dello schermo", "Screen resolution");
            Font = new Font("Segoe UI", 9f);
            BackColor = Pal.Bg;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Icon = AppIcon.Get();
            ClientSize = new Size(Ui.S(640), Ui.S(580));

            lblInfo = Label(L.T(
                "Converte tutte le posizioni del profilo (clic, aree dei colori, colore di fine partita) da una risoluzione dello schermo a un'altra, in proporzione. "
                + "Durate, tolleranze, colori e movimenti relativi del mouse non cambiano. Dopo la conversione controlla i punti con «Testa ora»; "
                + "il file su disco cambia solo quando salvi.",
                "Converts all the positions of the profile (clicks, color areas, end-of-match color) from one screen resolution to another, proportionally. "
                + "Durations, tolerances, colors and relative mouse movements do not change. After converting, check the points with «Test now»; "
                + "the file on disk only changes when you save."),
                20, 14, 600, 76, UiLabel.Role.Normal);
            lblInfo.Align = ContentAlignment.TopLeft;

            // ---- da ----
            Label(L.T("IL PROFILO È FATTO PER", "THE PROFILE WAS MADE FOR"), 20, 104, 600, 16, UiLabel.Role.Section);
            cbFrom = MakeCombo(20, 124);
            nudFromW = MakeNum(360, 124);
            Times(462, 124);
            nudFromH = MakeNum(482, 124);
            lblFromNote = Label("", 20, 160, 600, 36, UiLabel.Role.Small);
            lblFromNote.Align = ContentAlignment.TopLeft;

            // ---- a ----
            Label(L.T("CONVERTI A", "CONVERT TO"), 20, 206, 600, 16, UiLabel.Role.Section);
            cbTo = MakeCombo(20, 226);
            nudToW = MakeNum(360, 226);
            Times(462, 226);
            nudToH = MakeNum(482, 226);
            btnMyScreen = new FlatButton();
            btnMyScreen.Icon = Glyph.Screen;
            btnMyScreen.Radius = 6;
            btnMyScreen.Text = L.T("Usa il mio schermo", "Use my screen") + " (" + screen.Width + " × " + screen.Height + ")";
            btnMyScreen.SetBounds(Ui.S(20), Ui.S(266), Ui.S(330), Ui.S(32));
            btnMyScreen.Click += delegate { SetTo(screen.Width, screen.Height); };
            Controls.Add(btnMyScreen);

            // ---- spostamento ----
            Label(L.T("SPOSTA TUTTO DI (px) · solo se il gioco è in una finestra che non parte da (0, 0)",
                      "SHIFT EVERYTHING BY (px) · only if the game is in a window that does not start at (0, 0)"), 20, 314, 600, 16, UiLabel.Role.Section);
            Label("X", 20, 340, 18, 30, UiLabel.Role.Muted);
            nudShiftX = MakeNum(40, 340, 120);
            nudShiftX.Minimum = -20000; nudShiftX.Maximum = 20000; nudShiftX.Value = 0;
            Label("Y", 176, 340, 18, 30, UiLabel.Role.Muted);
            nudShiftY = MakeNum(196, 340, 120);
            nudShiftY.Minimum = -20000; nudShiftY.Maximum = 20000; nudShiftY.Value = 0;

            lblSummary = Label("", 20, 392, 600, 70, UiLabel.Role.Normal);
            lblSummary.Align = ContentAlignment.TopLeft;
            lblWarn = Label("", 20, 462, 600, 52, UiLabel.Role.Small);
            lblWarn.Align = ContentAlignment.TopLeft;
            lblWarn.Override = Pal.Warn;

            btnOk = new FlatButton();
            btnOk.Text = L.T("Converti", "Convert");
            btnOk.Style = FlatButton.Look.Primary;
            btnOk.BoldText = true;
            btnOk.SetBounds(Ui.S(640 - 20 - 100 - 10 - 150), Ui.S(580 - 20 - 36), Ui.S(150), Ui.S(36));
            btnOk.Click += delegate { Accept(); };
            Controls.Add(btnOk);

            btnCancel = new FlatButton();
            btnCancel.Text = L.T("Annulla", "Cancel");
            btnCancel.SetBounds(Ui.S(640 - 20 - 100), Ui.S(580 - 20 - 36), Ui.S(100), Ui.S(36));
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnCancel);

            // elenchi a tendina
            List<string> items = new List<string>();
            foreach (ResolutionPresets.Preset pr in ResolutionPresets.All) items.Add(ResolutionPresets.Text(pr));
            items.Add(L.T("Personalizzata (scrivi sotto)…", "Custom (type below)…"));
            cbFrom.SetItems(items);
            cbTo.SetItems(items);

            // valori iniziali: il profilo dice per quale schermo è fatto; altrimenti i profili di esempio (4K)
            bool known = profile != null && profile.RefW > 0 && profile.RefH > 0;
            SetFrom(known ? profile.RefW : 3840, known ? profile.RefH : 2160);
            lblFromNote.Text = known
                ? L.T("Risoluzione salvata nel profilo.", "Resolution saved in the profile.")
                : L.T("Il profilo non dice per quale schermo è fatto: ho preselezionato 3840 × 2160 (quello dei profili di esempio). Cambialo se il tuo è diverso.",
                      "The profile does not say which screen it was made for: I preselected 3840 × 2160 (the sample profiles' one). Change it if yours is different.");
            SetTo(screen.Width, screen.Height);

            cbFrom.SelectedIndexChanged += delegate { OnComboChanged(cbFrom, nudFromW, nudFromH); };
            cbTo.SelectedIndexChanged += delegate { OnComboChanged(cbTo, nudToW, nudToH); };
            nudFromW.ValueChanged += delegate { OnNumChanged(cbFrom, nudFromW, nudFromH); };
            nudFromH.ValueChanged += delegate { OnNumChanged(cbFrom, nudFromW, nudFromH); };
            nudToW.ValueChanged += delegate { OnNumChanged(cbTo, nudToW, nudToH); };
            nudToH.ValueChanged += delegate { OnNumChanged(cbTo, nudToW, nudToH); };
            nudShiftX.ValueChanged += delegate { RefreshSummary(); };
            nudShiftY.ValueChanged += delegate { RefreshSummary(); };

            AcceptButton = null;
            ResumeLayout(false);
            RefreshSummary();
        }

        UiLabel Label(string text, int x, int y, int w, int h, UiLabel.Role role)
        {
            UiLabel l = new UiLabel();
            l.Style = role;
            l.Text = text;
            l.SetBounds(Ui.S(x), Ui.S(y), Ui.S(w), Ui.S(h));
            Controls.Add(l);
            return l;
        }

        void Times(int x, int y)
        {
            UiLabel l = Label("×", x, y, 18, 30, UiLabel.Role.Muted);
            l.Align = ContentAlignment.MiddleCenter;
        }

        DarkCombo MakeCombo(int x, int y)
        {
            DarkCombo c = new DarkCombo();
            c.SetBounds(Ui.S(x), Ui.S(y), Ui.S(330), Ui.S(30));
            Controls.Add(c);
            return c;
        }

        NumBox MakeNum(int x, int y) { return MakeNum(x, y, 100); }

        NumBox MakeNum(int x, int y, int w)
        {
            NumBox n = new NumBox();
            n.Minimum = 200;
            n.Maximum = 20000;
            n.Value = 1920;
            n.SetBounds(Ui.S(x), Ui.S(y), Ui.S(w), Ui.S(30));
            Controls.Add(n);
            return n;
        }

        // ---- sincronizzazione menu a tendina <-> numeri ----

        void SetFrom(int w, int h) { Set(cbFrom, nudFromW, nudFromH, w, h); }
        void SetTo(int w, int h) { Set(cbTo, nudToW, nudToH, w, h); RefreshSummary(); }

        void Set(DarkCombo cb, NumBox nw, NumBox nh, int w, int h)
        {
            bool old = busy;
            busy = true;
            try
            {
                nw.Value = w;
                nh.Value = h;
                int i = ResolutionPresets.IndexOf(nw.Value, nh.Value);
                cb.SelectedIndex = i >= 0 ? i : ResolutionPresets.All.Length;
            }
            finally { busy = old; }
        }

        void OnComboChanged(DarkCombo cb, NumBox nw, NumBox nh)
        {
            if (busy) return;
            int i = cb.SelectedIndex;
            if (i >= 0 && i < ResolutionPresets.All.Length)
            {
                busy = true;
                try { nw.Value = ResolutionPresets.All[i].W; nh.Value = ResolutionPresets.All[i].H; }
                finally { busy = false; }
            }
            RefreshSummary();
        }

        void OnNumChanged(DarkCombo cb, NumBox nw, NumBox nh)
        {
            if (busy) return;
            busy = true;
            try
            {
                int i = ResolutionPresets.IndexOf(nw.Value, nh.Value);
                cb.SelectedIndex = i >= 0 ? i : ResolutionPresets.All.Length;
            }
            finally { busy = false; }
            RefreshSummary();
        }

        // ---- riepilogo in tempo reale ----

        static string Fmt(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        static string Ratio(int w, int h)
        {
            double r = (double)w / h;
            if (Math.Abs(r - 16.0 / 9) < 0.02) return "16:9";
            if (Math.Abs(r - 16.0 / 10) < 0.02) return "16:10";
            if (Math.Abs(r - 21.0 / 9) < 0.06) return "21:9";
            if (Math.Abs(r - 32.0 / 9) < 0.06) return "32:9";
            if (Math.Abs(r - 4.0 / 3) < 0.02) return "4:3";
            if (Math.Abs(r - 5.0 / 4) < 0.02) return "5:4";
            return r.ToString("0.00", CultureInfo.InvariantCulture) + ":1";
        }

        // primo punto o area con una posizione, per l'esempio
        static bool FirstPoint(Profile p, out int x, out int y)
        {
            x = 0; y = 0;
            if (p == null) return false;
            foreach (List<MacroStep> list in new List<MacroStep>[] { p.Steps, p.MenuSteps })
            {
                if (list == null) continue;
                foreach (MacroStep s in list)
                {
                    if (s == null) continue;
                    bool pos = s.Type == StepType.ClickAt || s.Type == StepType.MoveTo
                        || (s.ConditionActive && s.Type != StepType.IfStart) || (s.Type == StepType.IfStart && s.CondKind == 0);
                    if (pos && (s.MoveX != 0 || s.MoveY != 0)) { x = s.MoveX; y = s.MoveY; return true; }
                }
            }
            return false;
        }

        void RefreshSummary()
        {
            if (nudToH == null || lblSummary == null) return;
            int fw = nudFromW.Value, fh = nudFromH.Value, tw = nudToW.Value, th = nudToH.Value;
            double fx = (double)tw / fw, fy = (double)th / fh;
            int sx = nudShiftX.Value, sy = nudShiftY.Value;
            bool same = fw == tw && fh == th && sx == 0 && sy == 0;

            ResolutionScaler.Result r = ResolutionScaler.Apply(profile, fx, fy, sx, sy, true);
            string text;
            if (same)
            {
                text = L.T("Le due risoluzioni sono uguali: non c'è niente da convertire.", "The two resolutions are the same: there is nothing to convert.");
            }
            else
            {
                text = L.T("Fattore: larghezza ×", "Factor: width ×") + Fmt(fx) + L.T(" · altezza ×", " · height ×") + Fmt(fy)
                    + (sx != 0 || sy != 0 ? L.T(" · spostamento (", " · shift (") + sx + ", " + sy + ")" : "") + ".\r\n"
                    + L.T("Verranno convertiti: ", "Will be converted: ") + r.Points + L.T(" posizioni di clic, ", " click positions, ")
                    + r.Areas + L.T(" aree di colore", " color areas") + (r.EndArea > 0 ? L.T(" e l'area di fine partita", " and the end-of-match area") : "") + ".";
                int ex, ey;
                if (FirstPoint(profile, out ex, out ey))
                    text += "\r\n" + L.T("Esempio: (", "Example: (") + ex + ", " + ey + ") → ("
                        + ((int)Math.Round(ex * fx, MidpointRounding.AwayFromZero) + sx) + ", " + ((int)Math.Round(ey * fy, MidpointRounding.AwayFromZero) + sy) + ")";
            }
            lblSummary.Text = text;

            double ra = (double)fw / fh, rb = (double)tw / th;
            if (!same && Math.Abs(ra - rb) / ra > 0.01)
                lblWarn.Text = L.T("Attenzione: il formato è diverso (" + Ratio(fw, fh) + " → " + Ratio(tw, th) + "). Le posizioni vengono scalate in proporzione su ogni asse, "
                                   + "ma l'interfaccia del gioco potrebbe non coincidere (di solito l'HUD resta ancorato ai bordi): controlla ogni punto con «Testa ora».",
                                   "Warning: the aspect ratio is different (" + Ratio(fw, fh) + " → " + Ratio(tw, th) + "). Positions are scaled proportionally on each axis, "
                                   + "but the game interface may not match (the HUD is usually anchored to the edges): check every point with «Test now».");
            else lblWarn.Text = "";
            btnOk.Enabled = !same;
        }

        void Accept()
        {
            if (nudFromW.Value == nudToW.Value && nudFromH.Value == nudToH.Value && nudShiftX.Value == 0 && nudShiftY.Value == 0) return;
            FromW = nudFromW.Value; FromH = nudFromH.Value; ToW = nudToW.Value; ToH = nudToH.Value;
            ShiftX = nudShiftX.Value; ShiftY = nudShiftY.Value;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeUi.DarkTitleBar(Handle, Theme.Dark);
        }
    }
}
