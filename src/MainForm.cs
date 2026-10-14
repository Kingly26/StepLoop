using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace StepLoop
{
    // Finestra principale: barra superiore, impostazioni/statistiche, palette | lista | ispettore, controlli e log.
    public partial class MainForm : Form
    {
        const int HotkeyId = 0x6D52;
        const int CaptureHotkeyId = 0x6D53;
        const int PauseHotkeyId = 0x6D54;
        const int WM_HOTKEY = 0x0312;

        // Nomi canonici (salvati nel profilo) dei pulsanti del mouse e preset dell'insieme di tasti casuali.
        static readonly string[] ButtonCanon = { "Sinistro", "Destro", "Centrale" };
        static readonly string[] PresetPools = { "W, A, S, D", "W, A, S, D, Space", "Up, Down, Left, Right", "1-9", "A-Z" };

        Profile profile = Profile.Default();
        MacroRunner runner;
        Recorder recorder;
        ToastBar toast;
        ToastBar recBar;
        Action<Rectangle, Color> captureApply;

        struct LogItem
        {
            public DateTime Time;
            public LogKind Kind;
            public string Text;
        }

        // Stato scritto dai thread di lavoro e letto dal timer dell'interfaccia (mai un BeginInvoke per riga di log).
        readonly Queue<LogItem> logQueue = new Queue<LogItem>();
        int logDropped;
        volatile int stopVk;
        volatile bool closing;
        int stopStamp;
        bool isRunning;
        bool runPaused;
        bool hotkeysReady;
        bool keyFix;
        bool presetBusy;
        bool rebuilding;
        int editIndex = -1;
        StepType editType;
        int gameSel = -1, menuSel = -1;
        DateTime runStartedAt;
        Thread watchdog;
        System.Windows.Forms.Timer uiTimer;
        int tickCount;
        string lastStructureError;

        // Impostazione dei test: se false "Testa ora" legge subito senza ridurre a icona la finestra.
        bool testMinimizes = true;

        readonly List<Action> relocalize = new List<Action>();
        readonly ToolTip tip = new ToolTip();

        // layout
        SplitContainer splitMain, splitCols, splitInner;
        Surface topBar, infoHost, bottomBar, menuOpts, inspBody, inspFooter;
        Card settingsCard, paletteCard, listCard, inspCard;
        UiLabel lblTitle, lblStatusTitle, lblStatusSub;
        TabStrip tabs;
        FlatButton btnNewProfile;
        StatusPill pill;
        PaletteView palette;
        HeaderStrip listHeader;
        UiLabel lblFollow;
        ToggleSwitch swFollow;
        StepListControl list;
        InspHeader inspHeader;
        LivePanel livePanel;
        LogView logView;
        StatCard statRep, statTime, statCycle, statStep;
        FlatButton btnStart, btnPause, btnRec, btnSave, btnLoad, btnDuplicate, btnRemove, btnUp, btnDown;
        FlatButton btnHint, btnTheme, btnLang, btnPick, btnTest, btnEndPick;

        // impostazioni
        Segmented segMode;
        NumBox nudDelay, nudCycles;
        DarkCombo cbHotkey, cbPause;
        TextField tfWindow;
        CheckField chkDry;
        FieldBox fbMode, fbDelay, fbCycles, fbStart, fbPauseKey, fbWindow;

        // opzioni della scheda "Riavvio partita"
        CheckField chkAuto, chkMenuFirst, chkEndPixel;
        NumBox nudGameSec, nudEndX, nudEndY, nudEndW, nudEndH, nudEndTol;
        TextField tfEndColor;
        SwatchBox swEnd;
        UiLabel lblGameSec;

        static string DataDir
        {
            get { return AppPaths.DataDir; }
        }

        static string ProfilesDir
        {
            get
            {
                // "profiles" next to the exe; an existing legacy "profili" folder is still used if there is no "profiles"
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string current = Path.Combine(baseDir, "profiles");
                string legacy = Path.Combine(baseDir, "profili");
                return (!Directory.Exists(current) && Directory.Exists(legacy)) ? legacy : current;
            }
        }

        bool MenuTab { get { return tabs.SelectedIndex == 1; } }
        List<MacroStep> ActiveSteps { get { return MenuTab ? profile.MenuSteps : profile.Steps; } }

        static int S(int px) { return Ui.S(px); }

        public MainForm()
        {
            AppSettings.Load();
            Ui.InitScale();

            SuspendLayout();
            Text = "StepLoop";
            Icon = AppIcon.Get();
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Pal.Bg;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(1320), S(860));
            MinimumSize = new Size(S(1120), S(700));

            BuildLayout();
            ResumeLayout(true);
            PerformLayout();

            // Su schermi piccoli la finestra non deve uscire dall'area di lavoro.
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            if (Height > wa.Height - 10 || Width > wa.Width - 10)
            {
                MinimumSize = new Size(Math.Min(MinimumSize.Width, wa.Width - 10), Math.Min(MinimumSize.Height, wa.Height - 10));
                Size = new Size(Math.Min(Width, wa.Width - 10), Math.Min(Height, wa.Height - 10));
            }
            SetupSplitters();

            LoadLastProfile();
            ApplyLanguage();
            ApplyTheme();
            SetRunning(false);

            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 40;
            uiTimer.Tick += delegate { UiTick(); };
            uiTimer.Start();

            watchdog = new Thread(WatchdogLoop);
            watchdog.IsBackground = true;
            watchdog.Name = "stop-watchdog";
            watchdog.Start();
        }

        // ---------- localizzazione ----------

        void Loc(Control c, string it, string en)
        {
            Action a = delegate { c.Text = L.T(it, en); };
            relocalize.Add(a);
            a();
        }

        void Cap(FieldBox f, string it, string en)
        {
            Action a = delegate { f.Caption = L.T(it, en); };
            relocalize.Add(a);
            a();
        }

        void Tip(Control c, string it, string en)
        {
            Action a = delegate { tip.SetToolTip(c, L.T(it, en)); };
            relocalize.Add(a);
            a();
        }

        // ---------- costruzione dell'interfaccia ----------

        FlatButton MakeButton(string it, string en, int w, int h)
        {
            FlatButton b = new FlatButton();
            Loc(b, it, en);
            b.Size = new Size(S(w), S(h));
            return b;
        }

        NumBox MakeNum(int min, int max, int val, bool mono)
        {
            NumBox n = new NumBox();
            n.Minimum = min;
            n.Maximum = max;
            n.Value = val;
            return n;
        }

        FieldBox MakeField(Control c, string it, string en, bool section)
        {
            FieldBox f = new FieldBox(c, section);
            Cap(f, it, en);
            return f;
        }

        DarkCombo MakeCombo(params string[] items)
        {
            DarkCombo c = new DarkCombo();
            c.SetItems(items);
            return c;
        }

        void BuildLayout()
        {
            // corpo (alto) + log (basso)
            splitMain = new SplitContainer();
            splitMain.Dock = DockStyle.Fill;
            splitMain.Orientation = Orientation.Horizontal;
            splitMain.SplitterWidth = S(6);
            splitMain.BackColor = Pal.Bg;
            splitMain.Panel1.BackColor = Pal.Bg;
            splitMain.Panel2.BackColor = Pal.Bg;
            splitMain.FixedPanel = FixedPanel.Panel2;
            Controls.Add(splitMain);

            BuildTopBar();

            // --- pannello alto: corpo ---
            splitCols = new SplitContainer();
            splitCols.Dock = DockStyle.Fill;
            splitCols.Orientation = Orientation.Vertical;
            splitCols.SplitterWidth = S(10);
            splitCols.FixedPanel = FixedPanel.Panel1;
            splitCols.BackColor = Pal.Bg;
            splitCols.Panel1.BackColor = Pal.Bg;
            splitCols.Panel2.BackColor = Pal.Bg;
            splitMain.Panel1.Controls.Add(splitCols);

            BuildBottomBar();
            splitMain.Panel1.Controls.Add(bottomBar);

            BuildInfoStrip();
            splitMain.Panel1.Controls.Add(infoHost);

            splitInner = new SplitContainer();
            splitInner.Dock = DockStyle.Fill;
            splitInner.Orientation = Orientation.Vertical;
            splitInner.SplitterWidth = S(10);
            splitInner.FixedPanel = FixedPanel.Panel2;
            splitInner.BackColor = Pal.Bg;
            splitInner.Panel1.BackColor = Pal.Bg;
            splitInner.Panel2.BackColor = Pal.Bg;
            splitCols.Panel2.Controls.Add(splitInner);

            BuildPalette();
            BuildListCard();
            BuildInspector();

            // log
            logView = new LogView();
            logView.Dock = DockStyle.Fill;
            splitMain.Panel2.Padding = new Padding(S(12), 0, S(12), S(12));
            splitMain.Panel2.Controls.Add(logView);

            splitMain.Panel1.Padding = new Padding(S(12), 0, S(12), 0);
        }

        void BuildTopBar()
        {
            topBar = new Surface(Surface.Kind.Bar);
            topBar.Dock = DockStyle.Top;
            topBar.Height = S(52);
            topBar.BottomLine = true;
            Controls.Add(topBar);

            lblTitle = new UiLabel();
            lblTitle.Style = UiLabel.Role.Strong;
            lblTitle.Text = "StepLoop";
            lblTitle.Size = new Size(S(150), S(52));
            lblTitle.Location = new Point(S(16), 0);
            topBar.Controls.Add(lblTitle);

            tabs = new TabStrip();
            tabs.Location = new Point(S(172), S(9));
            tabs.Height = S(34);
            tabs.Width = S(420);
            tabs.SelectedIndexChanged += delegate { OnTabChanged(); };
            topBar.Controls.Add(tabs);

            btnNewProfile = new FlatButton();
            btnNewProfile.Style = FlatButton.Look.Ghost;
            btnNewProfile.Icon = Glyph.Plus;
            btnNewProfile.Size = new Size(S(32), S(32));
            btnNewProfile.Click += delegate { NewProfile(); };
            topBar.Controls.Add(btnNewProfile);
            Tip(btnNewProfile, "Nuovo profilo vuoto", "New empty profile");

            pill = new StatusPill();
            pill.Size = new Size(S(140), S(28));
            topBar.Controls.Add(pill);

            topBar.Resize += delegate { LayoutTopBar(); };
        }

        void LayoutTopBar()
        {
            pill.Width = pill.PreferredWidth();
            pill.Location = new Point(topBar.Width - pill.Width - S(16), (topBar.Height - pill.Height) / 2);
            using (Graphics g = tabs.CreateGraphics())
            {
                int w = 0;
                foreach (string t in tabs.Tabs) w += Ui.Measure(g, t, Ui.F(9.5f, true)) + S(28 + 4);
                tabs.Width = Math.Max(S(200), w);
            }
            btnNewProfile.Location = new Point(tabs.Right + S(4), (topBar.Height - btnNewProfile.Height) / 2);
        }

        void BuildInfoStrip()
        {
            infoHost = new Surface(Surface.Kind.Bg);
            infoHost.Dock = DockStyle.Top;
            infoHost.Height = S(76);

            // impostazioni compatte (modifica)
            settingsCard = new Card();
            settingsCard.Location = new Point(0, S(12));
            infoHost.Controls.Add(settingsCard);

            segMode = new Segmented();
            segMode.SelectedIndexChanged += delegate { UpdateEditorVisibility(); };
            fbMode = MakeField(segMode, "MODALITÀ", "MODE", true);
            nudDelay = MakeNum(0, 60, 3, true);
            fbDelay = MakeField(nudDelay, "RITARDO (S)", "DELAY (S)", true);
            nudCycles = MakeNum(0, 100000, 0, true);
            fbCycles = MakeField(nudCycles, "RIPETIZIONI (0 = ∞)", "REPEATS (0 = ∞)", true);
            cbHotkey = MakeCombo("F6", "F7", "F8", "F9", "F10", "F11", "F12");
            cbHotkey.Mono = true;
            cbHotkey.SelectedIndex = 2;
            fbStart = MakeField(cbHotkey, "START/STOP", "START/STOP", true);
            cbPause = MakeCombo("F6", "F7", "F8", "F9", "F10", "F11", "F12");
            cbPause.Mono = true;
            cbPause.SelectedIndex = 1;
            fbPauseKey = MakeField(cbPause, "PAUSA", "PAUSE", true);
            tfWindow = new TextField();
            tfWindow.Text = "";
            fbWindow = MakeField(tfWindow, "SOLO SE FINESTRA ATTIVA", "ONLY IF WINDOW ACTIVE", true);
            chkDry = new CheckField();
            Loc(chkDry, "Simulazione", "Simulation");
            foreach (Control c in new Control[] { fbMode, fbDelay, fbCycles, fbStart, fbPauseKey, fbWindow, chkDry }) settingsCard.Controls.Add(c);

            cbHotkey.SelectedIndexChanged += delegate
            {
                EnsureDistinctKeys(true);
                RegisterHotkey();
                UpdateRunTexts();
            };
            cbPause.SelectedIndexChanged += delegate
            {
                EnsureDistinctKeys(false);
                UpdateRunTexts();
            };

            Tip(segMode, "Sincrono: esegue la sequenza nell'ordine, con le durate esatte. Random: sceglie a caso i passi (in base al peso) e varia durate e pause.",
                         "Synchronous: runs the sequence in order with exact durations. Random: picks steps at random (by weight) and varies durations and pauses.");
            Tip(nudCycles, "Quante volte ripetere. Con il riavvio automatico è il numero di partite; altrimenti le passate della sequenza.",
                           "How many times to repeat. With automatic restart it is the number of matches; otherwise the passes of the sequence.");
            Tip(cbPause, "Mette in pausa la macro senza uscire, dove si trova: rilascia i tasti e li ripreme quando riprendi. Lo stesso tasto riprende.",
                         "Pauses the macro where it is without leaving: releases the keys and presses them again when you resume. The same key resumes.");
            Tip(chkDry, "Non invia nessun input: scrive nel log cosa farebbe. I controlli sui colori funzionano comunque.",
                        "Sends no input: writes to the log what it would do. Color checks still work.");
            Tip(tfWindow, "Consigliato: scrivi parte del titolo della finestra del gioco o del programma. Tasti e click partono solo se la finestra attiva lo contiene; se perde il focus gli input si sospendono. Vuoto = nessun controllo (inattivo in Simulazione).",
                          "Recommended: type part of the game or program window title. Keys and clicks are sent only if the active window contains it; if it loses focus, input is suspended. Empty = no check (inactive in Simulation).");

            // statistiche (esecuzione)
            statRep = new StatCard();
            statTime = new StatCard();
            statCycle = new StatCard();
            statStep = new StatCard();
            statStep.Highlight = true;
            statStep.ValueIsText = true;
            foreach (StatCard c in new StatCard[] { statRep, statTime, statCycle, statStep })
            {
                c.Visible = false;
                infoHost.Controls.Add(c);
            }

            infoHost.Resize += delegate { LayoutInfo(); };
            LayoutInfo();
        }

        void LayoutInfo()
        {
            int w = infoHost.Width;
            if (w <= 0) return;
            settingsCard.SetBounds(0, S(12), w, S(64));
            // posizioni nella card delle impostazioni
            int x = S(14);
            int y = S(8);
            int gap = S(14);
            int fixedW = S(188) + S(92) + S(122) + S(86) + S(86) + S(122);
            int winW = Math.Max(S(140), w - S(28) - fixedW - gap * 6);
            fbMode.SetBounds(x, y, S(188), fbMode.NaturalHeight); x += S(188) + gap;
            fbDelay.SetBounds(x, y, S(92), fbDelay.NaturalHeight); x += S(92) + gap;
            fbCycles.SetBounds(x, y, S(122), fbCycles.NaturalHeight); x += S(122) + gap;
            fbStart.SetBounds(x, y, S(86), fbStart.NaturalHeight); x += S(86) + gap;
            fbPauseKey.SetBounds(x, y, S(86), fbPauseKey.NaturalHeight); x += S(86) + gap;
            fbWindow.SetBounds(x, y, winW, fbWindow.NaturalHeight); x += winW + gap;
            chkDry.SetBounds(x, y + S(18) + S(3), S(122), S(24));

            // schede statistiche
            int cw = (w - S(12) * 3) / 4;
            StatCard[] cards = new StatCard[] { statRep, statTime, statCycle, statStep };
            for (int i = 0; i < 4; i++) cards[i].SetBounds(i * (cw + S(12)), S(12), cw, S(74));
        }

        void BuildBottomBar()
        {
            bottomBar = new Surface(Surface.Kind.Bg);
            bottomBar.Dock = DockStyle.Bottom;
            bottomBar.Height = S(74);

            btnStart = new FlatButton();
            btnStart.Style = FlatButton.Look.Success;
            btnStart.FontPt = 11.5f;
            btnStart.BoldText = true;
            btnStart.Icon = Glyph.Play;
            btnStart.Radius = 8;
            btnStart.Click += delegate { Toggle(); };
            bottomBar.Controls.Add(btnStart);

            btnPause = new FlatButton();
            btnPause.FontPt = 10.5f;
            btnPause.BoldText = false;
            btnPause.Icon = Glyph.Pause;
            btnPause.Radius = 8;
            btnPause.Click += delegate { TogglePause(); };
            bottomBar.Controls.Add(btnPause);
            Tip(btnPause, "Mette in pausa senza uscire: ferma la macro dove si trova (rilascia i tasti) per fare prove, poi riprende da lì.",
                          "Pauses without leaving: freezes the macro where it is (releases the keys) so you can test, then resumes from there.");

            btnRec = new FlatButton();
            btnRec.Text = "REC";
            btnRec.Icon = Glyph.Rec;
            btnRec.IconColor = Pal.Dot(Sem.Exit);
            btnRec.FontPt = 10.5f;
            btnRec.Radius = 8;
            btnRec.Click += delegate { StartRecording(); };
            bottomBar.Controls.Add(btnRec);
            Tip(btnRec, "Registra i tuoi clic nel gioco (posizione + colore) e li trasforma in passi della scheda attiva.",
                        "Records your clicks in the game (position + color) and turns them into steps of the active tab.");

            btnSave = new FlatButton();
            btnSave.Icon = Glyph.Save;
            btnSave.Radius = 8;
            Loc(btnSave, "Salva profilo…", "Save profile…");
            btnSave.Click += delegate { SaveProfileDialog(); };
            bottomBar.Controls.Add(btnSave);

            btnLoad = new FlatButton();
            btnLoad.Icon = Glyph.Open;
            btnLoad.Radius = 8;
            Loc(btnLoad, "Carica profilo…", "Load profile…");
            btnLoad.Click += delegate { LoadProfileDialog(); };
            bottomBar.Controls.Add(btnLoad);

            lblStatusTitle = new UiLabel();
            lblStatusTitle.Style = UiLabel.Role.Strong;
            bottomBar.Controls.Add(lblStatusTitle);
            lblStatusSub = new UiLabel();
            lblStatusSub.Style = UiLabel.Role.Muted;
            bottomBar.Controls.Add(lblStatusSub);

            btnHint = new FlatButton();
            btnHint.Icon = Glyph.Bulb;
            btnHint.IconColor = Pal.Warn;
            btnHint.Radius = 8;
            btnHint.Click += delegate { ShowHint(); };
            bottomBar.Controls.Add(btnHint);
            Tip(btnHint, "Suggerimenti, avvertenze sugli anti-cheat e disclaimer.", "Tips, anti-cheat warnings and disclaimer.");

            btnTheme = new FlatButton();
            btnTheme.Icon = Glyph.Theme;
            btnTheme.Radius = 8;
            btnTheme.Click += delegate { ToggleTheme(); };
            bottomBar.Controls.Add(btnTheme);
            Tip(btnTheme, "Passa dal tema scuro a quello chiaro.", "Switch between the dark and light theme.");

            btnLang = new FlatButton();
            btnLang.Radius = 8;
            btnLang.BoldText = true;
            btnLang.FontPt = 9f;
            btnLang.Click += delegate { ToggleLanguage(); };
            bottomBar.Controls.Add(btnLang);
            Tip(btnLang, "Cambia lingua: Italiano / English.", "Change language: Italiano / English.");

            bottomBar.Resize += delegate { LayoutBottom(); };
            LayoutBottom();
        }

        void LayoutBottom()
        {
            int y = S(10), h = S(52), x = 0, gap = S(10);
            btnStart.SetBounds(x, y, S(200), h); x += S(200) + gap;
            btnPause.SetBounds(x, y, S(160), h); x += S(160) + gap;
            btnRec.SetBounds(x, y, S(100), h); x += S(100) + gap;
            btnSave.SetBounds(x, y, S(150), h); x += S(150) + gap;
            btnLoad.SetBounds(x, y, S(150), h); x += S(150) + gap * 2;
            int right = bottomBar.Width;
            int ib = S(38);
            btnLang.SetBounds(right - ib, y + (h - ib) / 2, ib, ib); right -= ib + S(8);
            btnTheme.SetBounds(right - ib, y + (h - ib) / 2, ib, ib); right -= ib + S(8);
            btnHint.SetBounds(right - ib, y + (h - ib) / 2, ib, ib); right -= ib + S(14);
            int tw = Math.Max(S(120), right - x);
            lblStatusTitle.SetBounds(x, y + S(6), tw, S(22));
            lblStatusSub.SetBounds(x, y + S(28), tw, S(20));
        }

        void BuildPalette()
        {
            paletteCard = new Card();
            paletteCard.Dock = DockStyle.Fill;
            paletteCard.Padding = new Padding(S(1));
            splitCols.Panel1.Controls.Add(paletteCard);
            palette = new PaletteView();
            palette.Dock = DockStyle.Fill;
            palette.AddRequested += delegate(StepType t) { AddNew(t); };
            paletteCard.Controls.Add(palette);
            paletteCard.Resize += delegate { RoundRegion(palette, paletteCard); };
        }

        void RoundRegion(Control inner, Control card)
        {
            try
            {
                if (inner.Width <= 4 || inner.Height <= 4) return;
                using (GraphicsPath gp = Ui.Round(new Rectangle(0, 0, inner.Width, inner.Height), S(7)))
                    inner.Region = new Region(gp);
            }
            catch (Exception) { }
        }

        void BuildListCard()
        {
            listCard = new Card();
            listCard.Dock = DockStyle.Fill;
            listCard.Padding = new Padding(S(1));
            splitInner.Panel1.Controls.Add(listCard);

            list = new StepListControl();
            list.Dock = DockStyle.Fill;
            list.SelectionChanged += delegate { OnListSelectionChanged(); };
            list.PaletteDrop += delegate(StepType t, int ins) { AddNewAt(t, ins); };
            list.Reorder += delegate(int from, int to, int ins) { ReorderSteps(from, to, ins); };
            list.DeleteRequested += delegate { RemoveStep(); };
            list.DuplicateRequested += delegate { DuplicateStep(); };
            list.MoveRequested += delegate(int d) { MoveStep(d); };
            listCard.Controls.Add(list);

            menuOpts = new Surface(Surface.Kind.Panel);
            menuOpts.Dock = DockStyle.Top;
            menuOpts.Height = S(124);
            menuOpts.BottomLine = true;
            menuOpts.Visible = false;
            listCard.Controls.Add(menuOpts);
            BuildMenuOptions();

            listHeader = new HeaderStrip();
            listHeader.Dock = DockStyle.Top;
            listCard.Controls.Add(listHeader);

            lblFollow = new UiLabel();
            lblFollow.Style = UiLabel.Role.Muted;
            Loc(lblFollow, "segue lo step attivo", "follows the active step");
            lblFollow.Align = ContentAlignment.MiddleRight;
            lblFollow.Visible = false;
            listHeader.Controls.Add(lblFollow);
            swFollow = new ToggleSwitch();
            swFollow.Checked = true;
            swFollow.Visible = false;
            swFollow.CheckedChanged += delegate { list.Follow = swFollow.Checked; list.Rebuild(); };
            listHeader.Controls.Add(swFollow);
            listHeader.Resize += delegate { LayoutListHeader(); };
            listCard.Resize += delegate { RoundRegion(list, listCard); };
        }

        void LayoutListHeader()
        {
            swFollow.Location = new Point(listHeader.Width - S(14) - swFollow.Width, (listHeader.Height - swFollow.Height) / 2);
            using (Graphics g = lblFollow.CreateGraphics())
            {
                int tw = Ui.Measure(g, lblFollow.Text, Ui.F(9f)) + S(8);
                lblFollow.SetBounds(swFollow.Left - S(10) - tw, 0, tw, listHeader.Height);
            }
        }

        void BuildMenuOptions()
        {
            chkAuto = new CheckField();
            Loc(chkAuto, "Riavvio automatico delle partite (loop)", "Automatic match restart (loop)");
            chkAuto.SetBounds(S(14), S(8), S(330), S(24));
            menuOpts.Controls.Add(chkAuto);
            Tip(chkAuto, "Quando la partita finisce esegue i passi qui sotto (menu) e ne fa partire un'altra, per il numero di ripetizioni impostato (0 = infinito).",
                         "When the match ends it runs the steps below (menu) and starts another one, for the number of repeats set (0 = infinite).");

            chkMenuFirst = new CheckField();
            Loc(chkMenuFirst, "Inizia dal menu (sono nella lobby)", "Start from the menu (I'm in the lobby)");
            chkMenuFirst.SetBounds(S(350), S(8), S(300), S(24));
            menuOpts.Controls.Add(chkMenuFirst);
            Tip(chkMenuFirst, "Spuntalo se avvii la macro mentre sei già nella lobby: parte subito dai passi del menu. Se lo lasci vuoto la macro parte dalla sequenza principale e rilancia il menu alla fine.",
                              "Check it if you start the macro while already in the lobby: it starts straight from the menu steps. If left unchecked the macro starts from the main sequence and runs the menu at the end.");

            lblGameSec = new UiLabel();
            Loc(lblGameSec, "Fine partita dopo (s):", "Match ends after (s):");
            lblGameSec.SetBounds(S(14), S(38), S(150), S(30));
            menuOpts.Controls.Add(lblGameSec);
            nudGameSec = MakeNum(0, 86400, 180, true);
            nudGameSec.SetBounds(S(168), S(38), S(90), S(30));
            menuOpts.Controls.Add(nudGameSec);
            Tip(nudGameSec, "Dopo questi secondi di esecuzione la partita è considerata finita. 0 = usa solo il colore.",
                            "After these seconds of running the match is considered over. 0 = use only the color.");

            chkEndPixel = new CheckField();
            Loc(chkEndPixel, "oppure quando l'area a schermo ha il colore:", "or when the screen area has the color:");
            chkEndPixel.SetBounds(S(276), S(41), S(330), S(24));
            menuOpts.Controls.Add(chkEndPixel);
            Tip(chkEndPixel, "La partita è finita quando il colore medio dell'area è quello indicato (± tolleranza). Si aggiunge alla durata.",
                             "The match is over when the area's average color is the given one (± tolerance). It adds to the duration.");

            int y = S(80);
            nudEndX = MakeNum(-20000, 20000, 0, true); nudEndX.SetBounds(S(14), y, S(76), S(30)); menuOpts.Controls.Add(nudEndX);
            nudEndY = MakeNum(-20000, 20000, 0, true); nudEndY.SetBounds(S(96), y, S(76), S(30)); menuOpts.Controls.Add(nudEndY);
            nudEndW = MakeNum(1, 5000, 1, true); nudEndW.SetBounds(S(178), y, S(70), S(30)); menuOpts.Controls.Add(nudEndW);
            nudEndH = MakeNum(1, 5000, 1, true); nudEndH.SetBounds(S(254), y, S(70), S(30)); menuOpts.Controls.Add(nudEndH);
            tfEndColor = new TextField();
            tfEndColor.Mono = true;
            tfEndColor.Text = "000000";
            tfEndColor.SetBounds(S(334), y, S(90), S(30));
            menuOpts.Controls.Add(tfEndColor);
            swEnd = new SwatchBox();
            swEnd.SetBounds(S(430), y + S(3), S(24), S(24));
            swEnd.Radius = 5;
            menuOpts.Controls.Add(swEnd);
            nudEndTol = MakeNum(0, 255, 12, true);
            nudEndTol.SetBounds(S(462), y, S(70), S(30));
            menuOpts.Controls.Add(nudEndTol);
            tfEndColor.TextChanged += delegate { UpdateEndSwatch(); };

            btnEndPick = new FlatButton();
            Loc(btnEndPick, "Seleziona area…", "Pick area…");
            btnEndPick.Icon = Glyph.Crosshair;
            btnEndPick.SetBounds(S(540), y, S(170), S(30));
            btnEndPick.Click += delegate
            {
                BeginAreaCapture(delegate(Rectangle r, Color c)
                {
                    nudEndX.Value = r.X;
                    nudEndY.Value = r.Y;
                    nudEndW.Value = r.Width;
                    nudEndH.Value = r.Height;
                    tfEndColor.Text = ScreenPixel.ToHex(c);
                    chkEndPixel.Checked = true;
                });
            };
            menuOpts.Controls.Add(btnEndPick);
            Tip(btnEndPick, "Porta il gioco nello stato giusto e premi F9: lo schermo si congela e trascini per scegliere l'area.",
                            "Bring the game to the right state and press F9: the screen freezes and you drag to choose the area.");
        }

        void UpdateEndSwatch()
        {
            Color c;
            swEnd.Value = ScreenPixel.TryParseHex(tfEndColor.Text, out c) ? c : Pal.Raised;
        }

        // ---------- barre di divisione ----------

        void SetupSplitters()
        {
            try
            {
                splitMain.Panel2MinSize = S(70);
                splitMain.Panel1MinSize = S(360);
                splitMain.SplitterDistance = Math.Max(splitMain.Panel1MinSize, splitMain.Height - S(124) - splitMain.SplitterWidth);
            }
            catch (Exception) { }
            try
            {
                splitCols.Panel1MinSize = S(150);
                splitCols.Panel2MinSize = S(500);
                splitCols.SplitterDistance = Math.Min(S(190), Math.Max(S(150), splitCols.Width - S(500)));
            }
            catch (Exception) { }
            try
            {
                splitInner.Panel2MinSize = S(300);
                splitInner.Panel1MinSize = S(320);
                splitInner.SplitterDistance = Math.Max(S(320), splitInner.Width - S(370) - splitInner.SplitterWidth);
            }
            catch (Exception) { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SetupSplitters();
            hotkeysReady = true;
            RegisterHotkey();
        }

        // ---------- lingua e tema ----------

        string[] ButtonItems()
        {
            return new string[] { L.T("Sinistro", "Left"), L.T("Destro", "Right"), L.T("Centrale", "Middle") };
        }

        string[] PresetItems()
        {
            return new string[]
            {
                L.T("Scegli un preset…", "Choose a preset…"),
                L.T("Movimento (W, A, S, D)", "Movement (W, A, S, D)"),
                L.T("Movimento + salto", "Movement + jump"),
                L.T("Frecce direzionali", "Arrow keys"),
                L.T("Numeri 1-9", "Numbers 1-9"),
                L.T("Lettere A-Z", "Letters A-Z")
            };
        }

        void Refill(DarkCombo cb, string[] items)
        {
            int sel = cb.SelectedIndex;
            cb.SetItems(items);
            cb.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, sel));
        }

        void ApplyLanguage()
        {
            bool old = rebuilding;
            rebuilding = true;
            bool oldPreset = presetBusy;
            presetBusy = true;
            try
            {
                foreach (Action a in relocalize) a();
                Refill(cbButton, ButtonItems());
                Refill(cbCond, new string[] { L.T("il colore è presente", "the color is present"), L.T("il colore NON è presente", "the color is NOT present") });
                Refill(cbPreset, PresetItems());
                Refill(cbCondKind, CondKindItems());
                Refill(cbVarOp, VarOpItems());
                segPart.Items = PartItems();
                segPart.Invalidate();
                segMode.Items = new string[] { L.T("Sincrono", "Synchronous"), "Random" };
                segMode.Invalidate();
                tabs.Tabs.Clear();
                tabs.Tabs.Add(L.T("Sequenza", "Sequence"));
                tabs.Tabs.Add(L.T("Riavvio partita (menu)", "Match restart (menu)"));
                tabs.Invalidate();
            }
            finally
            {
                rebuilding = old;
                presetBusy = oldPreset;
            }
            btnLang.Text = L.En ? "EN" : "IT";
            LayoutTopBar();
            LayoutListHeader();
            UpdateEditorVisibility();
            UpdateRunTexts();
            RefreshHeader();
            list.Invalidate();
            palette.Invalidate();
            logView.Invalidate();
            livePanel.Invalidate();
        }

        void ToggleLanguage()
        {
            L.En = !L.En;
            AppSettings.Save();
            ApplyLanguage();
        }

        void ApplyTheme()
        {
            SuspendLayout();
            BackColor = Pal.Bg;
            foreach (SplitContainer sc in new SplitContainer[] { splitMain, splitCols, splitInner })
            {
                sc.BackColor = Pal.Bg;
                sc.Panel1.BackColor = Pal.Bg;
                sc.Panel2.BackColor = Pal.Bg;
            }
            ThemeApplier.Apply(this);
            UpdateEndSwatch();
            ValidatePool();
            btnRec.IconColor = Pal.Dot(Sem.Exit);
            btnHint.IconColor = Pal.Warn;
            UpdateRunTexts();
            ResumeLayout(true);
            if (IsHandleCreated) ApplyNativeTheme();
            Invalidate(true);
        }

        void ApplyNativeTheme()
        {
            NativeUi.DarkTitleBar(Handle, Theme.Dark);
            if (inspBody != null && inspBody.IsHandleCreated) inspBody.ApplyScrollTheme();
        }

        void ToggleTheme()
        {
            Theme.Dark = !Theme.Dark;
            AppSettings.Save();
            ApplyTheme();
        }

        void ShowHint()
        {
            using (HintDialog d = new HintDialog(cbHotkey.SelectedText, cbPause.SelectedText))
                d.ShowDialog(this);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyNativeTheme();
        }

        // Nuovo profilo vuoto (pulsante "+" nella barra superiore).
        void NewProfile()
        {
            if (isRunning) return;
            DialogResult dr = MessageBox.Show(this,
                L.T("Creare un profilo vuoto? Le modifiche non salvate andranno perse.", "Create an empty profile? Unsaved changes will be lost."),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dr != DialogResult.Yes) return;
            Profile p = new Profile();
            p.StartDelaySec = 3;
            ApplyProfile(p);
        }
    }
}
