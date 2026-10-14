using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace StepLoop
{
    // Pillola con un breve esito (es. "Ultimo test: corrisponde").
    public sealed class Chip : Control
    {
        public enum Tone { Neutral, Ok, Warn }
        Tone tone = Tone.Neutral;

        public Chip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = Ui.S(30);
        }

        public Tone Mood { get { return tone; } set { tone = value; Invalidate(); } }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color baseCol = tone == Tone.Ok ? Pal.Dot(Sem.Input) : (tone == Tone.Warn ? Pal.Dot(Sem.Cond) : Pal.Muted);
            Color fore = tone == Tone.Ok ? Pal.Ok : (tone == Tone.Warn ? Pal.Warn : Pal.Muted);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Ui.FillRound(g, r, Ui.S(6), Pal.Over(Pal.Panel, baseCol, tone == Tone.Neutral ? 0.1 : 0.14));
            Ui.Text(g, Text, Ui.F(9f), new Rectangle(Ui.S(10), 0, Width - Ui.S(14), Height), fore, Ui.Left);
        }
    }

    // Parte "ispettore" e operazioni di modifica della sequenza.
    public partial class MainForm
    {

        // campi dell'ispettore
        TextField tfName, tfKeys, tfPool, tfColor;
        CaptureBox capKeys;
        DarkCombo cbButton, cbCond, cbPreset, cbLabels;
        NumBox nudX, nudY, nudW, nudH, nudTol, nudCount, nudLoopSec, nudDur, nudPause, nudJit, nudWeight, nudPosJit;
        NumBox nudDurMin, nudDurMax, nudDurPct, nudDurLong, nudPauseMin, nudPauseMax, nudPausePct, nudPauseLong;
        CheckField chkUntil, chkCheckFirst, chkRandDur, chkRandPause, chkRandMove;
        SwatchBox swColor;
        AreaPreview areaPreview;
        Chip chipTest;
        FieldBox fbName, fbKeys, fbPool, fbPreset, fbGoto, fbButton, fbCond, fbColor, fbPreview, fbDur, fbDurRange, fbDurLong;
        FieldBox fbPause, fbPauseRange, fbPauseLong, fbJit, fbPosJit, fbWeight, fbX, fbY, fbW, fbH, fbCount, fbLoopSec;
        RowPanel rowLimits, rowArea, rowPickBtns;
        readonly List<Control> stack = new List<Control>();
        Rectangle previewRect;
        UiLabel lblPlus;

        // variabili, probabilità, scelta casuale e "Insieme"
        TextField tfVarName;
        DarkCombo cbCondKind, cbCmp, cbVarOp;
        NumBox nudIfVal, nudSetVal, nudSetVal2, nudChance, nudAltWeight, nudPickCount, nudPartChance;
        CheckField chkPartOn;
        Segmented segPart;
        FieldBox fbCondKind, fbVarName, fbCmp, fbChance, fbVarOp, fbSetVal, fbSetVal2, fbAltWeight, fbPickCount, fbPart, fbPartChance;
        RowPanel rowSetVals;
        ComboPart[] editParts;          // copia di lavoro delle 4 parti dell'"Insieme" selezionato
        int partShown = -1;             // parte i cui valori sono ora nei campi

        string[] CondKindItems()
        {
            return new string[] { L.T("Colore a schermo", "Screen color"), L.T("Variabile", "Variable"), L.T("Probabilità", "Chance") };
        }

        string[] CmpItems() { return new string[] { "=", "≠", "<", "≤", ">", "≥" }; }

        string[] VarOpItems()
        {
            return new string[] { L.T("Imposta a", "Set to"), L.T("Somma", "Add"), L.T("Sottrai", "Subtract"), L.T("Numero a caso tra", "Random number between") };
        }

        string[] PartItems() { return new string[] { L.T("Tasti", "Keys"), "Click", "Mouse", L.T("Mira", "Aim") }; }

        void BuildInspector()
        {
            inspCard = new Card();
            inspCard.Dock = DockStyle.Fill;
            inspCard.Padding = new Padding(S(1));
            splitInner.Panel2.Controls.Add(inspCard);

            // ordine di docking: prima il corpo (Fill), poi piede (Bottom) e intestazione (Top)
            inspBody = new Surface(Surface.Kind.Panel);
            inspBody.Dock = DockStyle.Fill;
            inspBody.AutoScroll = true;
            inspCard.Controls.Add(inspBody);

            livePanel = new LivePanel();
            livePanel.Dock = DockStyle.Fill;
            livePanel.Visible = false;
            inspCard.Controls.Add(livePanel);

            inspFooter = new Surface(Surface.Kind.Panel);
            inspFooter.Dock = DockStyle.Bottom;
            inspFooter.Height = S(58);
            inspFooter.TopLine = true;
            inspCard.Controls.Add(inspFooter);

            inspHeader = new InspHeader();
            inspHeader.Dock = DockStyle.Top;
            inspCard.Controls.Add(inspHeader);

            btnDuplicate = new FlatButton();
            btnDuplicate.Icon = Glyph.Copy;
            Loc(btnDuplicate, "Duplica", "Duplicate");
            btnDuplicate.Click += delegate { DuplicateStep(); };
            inspFooter.Controls.Add(btnDuplicate);
            Tip(btnDuplicate, "Duplica il passo (o l'intero blocco). Ctrl+D.", "Duplicates the step (or the whole block). Ctrl+D.");

            btnRemove = new FlatButton();
            btnRemove.Style = FlatButton.Look.DangerSoft;
            btnRemove.Icon = Glyph.Trash;
            Loc(btnRemove, "Elimina", "Delete");
            btnRemove.Click += delegate { RemoveStep(); };
            inspFooter.Controls.Add(btnRemove);
            Tip(btnRemove, "Elimina il passo (o l'intero blocco). Tasto Canc.", "Deletes the step (or the whole block). Del key.");

            btnUp = new FlatButton();
            btnUp.Icon = Glyph.Up;
            btnUp.Click += delegate { MoveStep(-1); };
            inspFooter.Controls.Add(btnUp);
            Tip(btnUp, "Sposta in su il passo (o l'intero blocco). Alt+↑.", "Moves the step (or the whole block) up. Alt+↑.");

            btnDown = new FlatButton();
            btnDown.Icon = Glyph.Down;
            btnDown.Click += delegate { MoveStep(1); };
            inspFooter.Controls.Add(btnDown);
            Tip(btnDown, "Sposta in giù il passo (o l'intero blocco). Alt+↓.", "Moves the step (or the whole block) down. Alt+↓.");

            inspFooter.Resize += delegate { LayoutInspFooter(); };

            // ---- campi ----
            tfName = new TextField();
            fbName = MakeField(tfName, "Nome", "Name", false);
            Tip(tfName, "Nome del passo (opzionale): compare nella lista e nel log. Per un'etichetta è il nome da usare nei Goto.",
                        "Step name (optional): shown in the list and in the log. For a label it is the name to use in Goto steps.");

            capKeys = new CaptureBox();
            tfKeys = new TextField(capKeys);
            tfKeys.Mono = true;
            fbKeys = MakeField(tfKeys, "Tasti", "Keys", false);
            Tip(tfKeys, "Clicca qui e premi i tasti (anche insieme, es. W+D). Backspace cancella.",
                        "Click here and press the keys (even together, e.g. W+D). Backspace clears.");

            tfPool = new TextField();
            tfPool.Mono = true;
            tfPool.Text = "W, A, S, D";
            fbPool = MakeField(tfPool, "Insieme di tasti", "Key set", false);
            Tip(tfPool, "Elenco separato da virgole: tasti (W, Space, F5), intervalli (A-Z, 1-9) o combinazioni (W+D). Ad ogni esecuzione ne viene scelto uno a caso.",
                        "Comma-separated list: keys (W, Space, F5), ranges (A-Z, 1-9) or combos (W+D). One is picked at random each time.");

            cbPreset = new DarkCombo();
            fbPreset = MakeField(cbPreset, "Preset", "Preset", false);

            cbLabels = new DarkCombo();
            fbGoto = MakeField(cbLabels, "Salta all'etichetta", "Jump to label", false);
            Tip(cbLabels, "Etichette presenti in questa scheda. Aggiungi un blocco «Etichetta» per crearne una.",
                          "Labels in this tab. Add a «Label» block to create one.");

            cbButton = new DarkCombo();
            cbButton.SetItems(ButtonItems());
            cbButton.SelectedIndex = 0;
            fbButton = MakeField(cbButton, "Pulsante", "Button", false);

            nudCount = MakeNum(0, 100000, 0, true);
            fbCount = MakeField(nudCount, "Max giri", "Max rounds", false);
            Tip(nudCount, "Numero massimo di giri. 0 = nessun limite.", "Maximum number of rounds. 0 = no limit.");
            nudLoopSec = MakeNum(0, 86400, 0, true);
            fbLoopSec = MakeField(nudLoopSec, "Tempo max (s)", "Max time (s)", false);
            Tip(nudLoopSec, "Dopo questo tempo il ciclo si interrompe comunque. 0 = nessun limite.", "After this time the loop stops anyway. 0 = no limit.");
            rowLimits = new RowPanel();
            rowLimits.Controls.Add(fbCount);
            rowLimits.Controls.Add(fbLoopSec);
            rowLimits.Height = S(50);

            chkUntil = new CheckField();
            Loc(chkUntil, "Esci quando un colore è a schermo", "Exit when a color is on screen");
            Tip(chkUntil, "Alla fine di ogni giro controlla il colore: se la condizione è soddisfatta esce, altrimenti ricomincia dal primo passo.",
                          "At the end of each round it checks the color: if the condition is met it exits, otherwise it starts again from the first step.");

            cbCond = new DarkCombo();
            cbCond.SetItems(new string[] { L.T("il colore è presente", "the color is present"), L.T("il colore NON è presente", "the color is NOT present") });
            cbCond.SelectedIndex = 0;
            fbCond = MakeField(cbCond, "Condizione", "Condition", false);

            nudX = MakeNum(-20000, 20000, 0, true);
            nudY = MakeNum(-20000, 20000, 0, true);
            nudW = MakeNum(1, 5000, 1, true);
            nudH = MakeNum(1, 5000, 1, true);
            fbX = MakeField(nudX, "X", "X", false);
            fbY = MakeField(nudY, "Y", "Y", false);
            fbW = MakeField(nudW, "Larg.", "Width", false);
            fbH = MakeField(nudH, "Alt.", "Height", false);
            rowArea = new RowPanel();
            rowArea.Gap = 8;
            rowArea.Controls.Add(fbX);
            rowArea.Controls.Add(fbY);
            rowArea.Controls.Add(fbW);
            rowArea.Controls.Add(fbH);
            rowArea.Height = S(50);
            Tip(nudW, "Larghezza dell'area (1 = singolo punto).", "Width of the area (1 = single point).");
            Tip(nudH, "Altezza dell'area (1 = singolo punto).", "Height of the area (1 = single point).");

            // colore: anteprima grande + esadecimale + tolleranza
            swColor = new SwatchBox();
            tfColor = new TextField();
            tfColor.Mono = true;
            tfColor.Text = "000000";
            tfColor.TextChanged += delegate { UpdateColorSwatch(); };
            lblPlus = new UiLabel();
            lblPlus.Text = "±";
            lblPlus.Style = UiLabel.Role.Muted;
            lblPlus.Align = ContentAlignment.MiddleCenter;
            nudTol = MakeNum(0, 255, 12, true);
            RowPanel colorRow = new RowPanel();
            colorRow.Weights = new float[] { 0.1f, 1.4f, 0.12f, 0.6f };
            colorRow.Gap = 8;
            colorRow.Controls.Add(swColor);
            colorRow.Controls.Add(tfColor);
            colorRow.Controls.Add(lblPlus);
            colorRow.Controls.Add(nudTol);
            colorRow.Height = S(44);
            colorRow.FixedWidths = new int[] { 44, 0, 14, 0 };
            fbColor = MakeField(colorRow, "Colore · tolleranza", "Color · tolerance", false);
            fbColor.Height = fbColor.NaturalHeight;
            Tip(tfColor, "Colore RRGGBB esadecimale (es. 1FA84A): per un'area è il colore medio.",
                         "Hexadecimal RRGGBB color (e.g. 1FA84A): for an area it is the average color.");
            Tip(nudTol, "Differenza massima ammessa per canale (0-255). Più alta = più permissivo.",
                        "Maximum allowed difference per channel (0-255). Higher = more permissive.");

            // seleziona / testa
            btnPick = new FlatButton();
            btnPick.Icon = Glyph.Crosshair;
            Loc(btnPick, "Seleziona dallo schermo…", "Pick from the screen…");
            btnPick.Click += delegate { PickForEditor(); };
            Tip(btnPick, "Porta il gioco nello stato giusto (anche il mouse sul pulsante) e premi F9: lo schermo si congela e trascini per scegliere l'area (un clic = un punto).",
                         "Bring the game to the right state (mouse on the button too) and press F9: the screen freezes and you drag to choose the area (a click = a point).");
            btnTest = new FlatButton();
            btnTest.Style = FlatButton.Look.Primary;
            btnTest.BoldText = true;
            Loc(btnTest, "Testa ora", "Test now");
            btnTest.Click += delegate { TestCondition(); };
            Tip(btnTest, "Legge ora il colore dell'area e dice se la condizione è vera. La finestra si riduce a icona per un istante.",
                         "Reads the area's color right now and tells whether the condition is true. The window is minimized for a moment.");
            rowPickBtns = new RowPanel();
            rowPickBtns.Gap = 8;
            rowPickBtns.Weights = new float[] { 1f, 0.5f };
            rowPickBtns.Controls.Add(btnPick);
            rowPickBtns.Controls.Add(btnTest);
            rowPickBtns.Height = S(34);
            btnPick.Height = S(34);
            btnTest.Height = S(34);

            chipTest = new Chip();
            chipTest.Text = L.T("Nessun test eseguito.", "No test run yet.");

            areaPreview = new AreaPreview();
            areaPreview.Height = S(76);
            fbPreview = MakeField(areaPreview, "Anteprima dell'area", "Area preview", false);
            fbPreview.Height = fbPreview.NaturalHeight;

            chkCheckFirst = new CheckField();
            Loc(chkCheckFirst, "Controlla anche prima del 1° giro", "Also check before the 1st round");

            nudDur = MakeNum(0, 3600000, 100, true);
            fbDur = MakeField(nudDur, "Durata (ms)", "Duration (ms)", false);

            BuildRandomBlock("Durata casuale (intervallo)", "Random duration (range)", out chkRandDur, out nudDurMin, out nudDurMax, out nudDurPct, out nudDurLong,
                out fbDurRange, out fbDurLong);
            Tip(chkRandDur, "Invece di una durata fissa, ad ogni esecuzione sceglie un valore a caso nell'intervallo. Funziona anche in modalità Sincrona.",
                            "Instead of a fixed duration, each time it picks a random value within the range. Works in Synchronous mode too.");

            nudPause = MakeNum(0, 3600000, 100, true);
            fbPause = MakeField(nudPause, "Pausa dopo (ms)", "Pause after (ms)", false);
            BuildRandomBlock("Pausa casuale (intervallo)", "Random pause (range)", out chkRandPause, out nudPauseMin, out nudPauseMax, out nudPausePct, out nudPauseLong,
                out fbPauseRange, out fbPauseLong);

            nudJit = MakeNum(0, 3600000, 0, true);
            fbJit = MakeField(nudJit, "Variazione ± (ms)", "Variation ± (ms)", false);
            Tip(nudJit, "Solo in modalità Random: durata e pausa variano casualmente di ± questo valore.",
                        "Random mode only: duration and pause vary randomly by ± this value.");
            nudPosJit = MakeNum(0, 1000, 0, true);
            fbPosJit = MakeField(nudPosJit, "Posizione ± (px)", "Position ± (px)", false);
            Tip(nudPosJit, "Il punto viene spostato a caso di al massimo questi pixel in ogni direzione (il click non cade sempre sullo stesso pixel).",
                           "The point is shifted at random by at most this many pixels in each direction (the click doesn't always land on the same pixel).");
            chkRandMove = new CheckField();
            Loc(chkRandMove, "Quantità casuale (entro ± i valori)", "Random amount (within ± the values)");
            Tip(chkRandMove, "Ad ogni esecuzione il movimento è un valore a caso tra - e + quello indicato, su ciascun asse (anche in modalità Sincrona).",
                             "Each time the movement is a random value between - and + the given one, on each axis (also in Synchronous mode).");
            nudWeight = MakeNum(1, 100, 1, true);
            fbWeight = MakeField(nudWeight, "Peso (random)", "Weight (random)", false);
            Tip(nudWeight, "Solo in modalità Random: più è alto, più spesso il passo viene scelto.",
                           "Random mode only: the higher it is, the more often the step is picked.");

            // ---- "Se" su variabile / probabilità, "Variabile" ----
            cbCondKind = new DarkCombo();
            cbCondKind.SetItems(CondKindItems());
            cbCondKind.SelectedIndex = 0;
            fbCondKind = MakeField(cbCondKind, "Il Se controlla", "The If checks", false);
            Tip(cbCondKind, "Colore a schermo, il valore di una variabile oppure il caso (una probabilità).",
                            "A screen color, the value of a variable, or chance (a probability).");

            tfVarName = new TextField();
            tfVarName.Mono = true;
            tfVarName.Text = "contatore";
            fbVarName = MakeField(tfVarName, "Nome variabile", "Variable name", false);
            Tip(tfVarName, "Le variabili sono numeri interi che esistono durante l'esecuzione (partono da 0 a ogni avvio). Il nome non distingue maiuscole e minuscole.",
                           "Variables are whole numbers that exist while running (they start at 0 on every start). The name is not case-sensitive.");

            cbCmp = new DarkCombo();
            cbCmp.SetItems(CmpItems());
            cbCmp.SelectedIndex = 0;
            nudIfVal = MakeNum(-1000000, 1000000, 0, true);
            RowPanel cmpRow = new RowPanel();
            cmpRow.Gap = 8;
            cmpRow.Weights = new float[] { 0.55f, 1f };
            cmpRow.Controls.Add(cbCmp);
            cmpRow.Controls.Add(nudIfVal);
            cmpRow.Height = S(30);
            fbCmp = MakeField(cmpRow, "Vero quando la variabile è… il valore", "True when the variable is… the value", false);
            fbCmp.Height = fbCmp.NaturalHeight;

            nudChance = MakeNum(0, 100, 50, true);
            fbChance = MakeField(nudChance, "Probabilità di vero (%)", "Chance of true (%)", false);
            Tip(nudChance, "Ad ogni esecuzione il Se è vero con questa probabilità (50 = una volta su due).",
                           "Each time the If is true with this probability (50 = one time in two).");

            cbVarOp = new DarkCombo();
            cbVarOp.SetItems(VarOpItems());
            cbVarOp.SelectedIndex = 0;
            fbVarOp = MakeField(cbVarOp, "Operazione", "Operation", false);
            nudSetVal = MakeNum(-1000000, 1000000, 0, true);
            nudSetVal2 = MakeNum(-1000000, 1000000, 0, true);
            fbSetVal = MakeField(nudSetVal, "Valore", "Value", false);
            fbSetVal2 = MakeField(nudSetVal2, "e", "and", false);
            rowSetVals = new RowPanel();
            rowSetVals.Gap = 8;
            rowSetVals.Controls.Add(fbSetVal);
            rowSetVals.Controls.Add(fbSetVal2);
            rowSetVals.Height = fbSetVal.NaturalHeight;

            // ---- "Scegli a caso" ----
            nudAltWeight = MakeNum(1, 100, 1, true);
            fbAltWeight = MakeField(nudAltWeight, "Peso della 1ª alternativa", "Weight of the 1st alternative", false);
            Tip(nudAltWeight, "Più è alto, più spesso viene scelta la prima alternativa (i passi prima della prima 'Opzione'). Le altre hanno il peso della loro Opzione.",
                              "The higher it is, the more often the first alternative is picked (the steps before the first 'Option'). Others use their Option's weight.");

            // ---- "Insieme" ----
            nudPickCount = MakeNum(0, 4, 0, true);
            fbPickCount = MakeField(nudPickCount, "Quante azioni insieme (0 = tutte)", "How many actions together (0 = all)", false);
            Tip(nudPickCount, "0 = partono tutte le azioni attive. Con un numero, ad ogni esecuzione ne parte solo quel numero, scelte a caso.",
                              "0 = all the active actions start. With a number, each time only that many start, picked at random.");

            segPart = new Segmented();
            segPart.Items = PartItems();
            fbPart = MakeField(segPart, "Azione da configurare", "Action to configure", false);
            segPart.SelectedIndexChanged += delegate { OnPartSwitched(); };

            chkPartOn = new CheckField();
            Loc(chkPartOn, "Usa questa azione", "Use this action");
            Tip(chkPartOn, "Se disattivata, l'azione non parte mai.", "If off, this action never starts.");
            nudPartChance = MakeNum(0, 100, 100, true);
            fbPartChance = MakeField(nudPartChance, "Probabilità che parta (%)", "Chance it starts (%)", false);
            Tip(nudPartChance, "Ad ogni esecuzione l'azione parte solo con questa probabilità (100 = sempre).",
                               "Each time the action only starts with this probability (100 = always).");

            stack.AddRange(new Control[]
            {
                fbName, fbCondKind, fbVarName, fbCmp, fbChance, fbVarOp, rowSetVals, fbAltWeight,
                fbPart, chkPartOn, fbPartChance,
                fbKeys, fbPool, fbPreset, fbGoto, fbButton, rowLimits, chkUntil, fbCond, rowArea, chkRandMove, fbColor, rowPickBtns, chipTest,
                fbPreview, chkCheckFirst, fbDur, chkRandDur, fbDurRange, fbDurLong, fbPause, chkRandPause, fbPauseRange, fbPauseLong,
                fbPickCount, fbJit, fbPosJit, fbWeight
            });
            foreach (Control c in stack) inspBody.Controls.Add(c);

            // intervalli coerenti: minimo <= massimo <= valore lungo
            nudDurMin.ValueChanged += delegate { FixRange(nudDurMin, nudDurMax, nudDurLong, true); };
            nudDurMax.ValueChanged += delegate { FixRange(nudDurMin, nudDurMax, nudDurLong, false); };
            nudPauseMin.ValueChanged += delegate { FixRange(nudPauseMin, nudPauseMax, nudPauseLong, true); };
            nudPauseMax.ValueChanged += delegate { FixRange(nudPauseMin, nudPauseMax, nudPauseLong, false); };

            chkRandDur.CheckedChanged += delegate
            {
                if (!rebuilding && chkRandDur.Checked) SeedRange(nudDurMin, nudDurMax, nudDurPct, nudDurLong, nudDur.Value, 15);
                UpdateEditorVisibility();
                OnEditorChanged();
            };
            chkRandPause.CheckedChanged += delegate
            {
                if (!rebuilding && chkRandPause.Checked) SeedRange(nudPauseMin, nudPauseMax, nudPausePct, nudPauseLong, nudPause.Value, 0);
                UpdateEditorVisibility();
                OnEditorChanged();
            };

            cbPreset.SelectedIndexChanged += delegate
            {
                if (presetBusy || cbPreset.SelectedIndex <= 0) return;
                string pool = PresetPools[cbPreset.SelectedIndex - 1];
                presetBusy = true;
                try { cbPreset.SelectedIndex = 0; }
                finally { presetBusy = false; }
                tfPool.Text = pool;
            };
            tfPool.TextChanged += delegate { ValidatePool(); };

            // modifica "dal vivo": ogni cambio ai campi si applica subito al passo selezionato
            tfName.TextChanged += delegate { OnEditorChanged(); };
            tfKeys.TextChanged += delegate { OnEditorChanged(); };
            tfPool.TextChanged += delegate { OnEditorChanged(); };
            tfColor.TextChanged += delegate { OnEditorChanged(); };
            cbButton.SelectedIndexChanged += delegate { OnEditorChanged(); };
            cbCond.SelectedIndexChanged += delegate { OnEditorChanged(); };
            cbLabels.SelectedIndexChanged += delegate { OnEditorChanged(); };
            chkUntil.CheckedChanged += delegate { UpdateEditorVisibility(); OnEditorChanged(); };
            chkCheckFirst.CheckedChanged += delegate { OnEditorChanged(); };
            chkRandMove.CheckedChanged += delegate { OnEditorChanged(); };
            tfVarName.TextChanged += delegate { OnEditorChanged(); };
            cbCmp.SelectedIndexChanged += delegate { OnEditorChanged(); };
            cbVarOp.SelectedIndexChanged += delegate { UpdateEditorVisibility(); OnEditorChanged(); };
            chkPartOn.CheckedChanged += delegate { OnEditorChanged(); };
            cbCondKind.SelectedIndexChanged += delegate
            {
                if (!rebuilding && cbCondKind.SelectedIndex == 1 && tfVarName.Text.Trim().Length == 0) tfVarName.Text = DefaultVarName();
                UpdateEditorVisibility();
                OnEditorChanged();
            };
            foreach (NumBox n in new NumBox[] { nudX, nudY, nudW, nudH, nudTol, nudCount, nudLoopSec, nudDur, nudPause, nudJit, nudWeight, nudPosJit,
                nudDurMin, nudDurMax, nudDurPct, nudDurLong, nudPauseMin, nudPauseMax, nudPausePct, nudPauseLong,
                nudIfVal, nudSetVal, nudSetVal2, nudChance, nudAltWeight, nudPickCount, nudPartChance })
                n.ValueChanged += delegate { OnEditorChanged(); };

            inspBody.Resize += delegate { LayoutStack(); };
        }

        // casella + intervallo da/a + "ogni tanto" (probabilità e massimo assoluto)
        void BuildRandomBlock(string chkIt, string chkEn, out CheckField chk, out NumBox min, out NumBox max, out NumBox pct, out NumBox lng,
            out FieldBox rangeBox, out FieldBox longBox)
        {
            chk = new CheckField();
            Loc(chk, chkIt, chkEn);
            min = MakeNum(0, 3600000, 0, true);
            max = MakeNum(0, 3600000, 0, true);
            RowPanel range = new RowPanel();
            range.Gap = 8;
            range.Controls.Add(min);
            range.Controls.Add(max);
            range.Height = S(30);
            rangeBox = MakeField(range, "Intervallo: da – a (ms)", "Range: from – to (ms)", false);
            rangeBox.Height = rangeBox.NaturalHeight;

            pct = MakeNum(0, 100, 0, true);
            lng = MakeNum(0, 3600000, 0, true);
            RowPanel lp = new RowPanel();
            lp.Gap = 8;
            lp.Weights = new float[] { 0.45f, 1f };
            lp.Controls.Add(pct);
            lp.Controls.Add(lng);
            lp.Height = S(30);
            longBox = MakeField(lp, "Ogni tanto: % delle volte, fino a (ms)", "Sometimes: % of the time, up to (ms)", false);
            longBox.Height = longBox.NaturalHeight;

            Tip(pct, "Probabilità (0-100) che questa volta il valore esca dall'intervallo e sia più lungo, fino al massimo indicato. 0 = mai.",
                     "Probability (0-100) that this time the value goes beyond the range and is longer, up to the given maximum. 0 = never.");
            Tip(lng, "Valore massimo assoluto: ogni tanto (con la probabilità a sinistra) il valore è scelto tra il massimo dell'intervallo e questo.",
                     "Absolute maximum: now and then (with the probability on the left) the value is picked between the range maximum and this.");
        }

        void LayoutInspFooter()
        {
            int w = inspFooter.Width - S(28);
            int h = S(36), y = (inspFooter.Height - h) / 2 + 1;
            int small = S(42), gap = S(8);
            int rest = w - small * 2 - gap * 3;
            int half = rest / 2;
            btnDuplicate.SetBounds(S(14), y, half, h);
            btnRemove.SetBounds(S(14) + half + gap, y, rest - half, h);
            btnUp.SetBounds(S(14) + rest + gap * 2, y, small, h);
            btnDown.SetBounds(S(14) + rest + gap * 3 + small, y, small, h);
        }

        void LayoutStack()
        {
            if (inspBody == null) return;
            inspBody.AutoScrollPosition = new Point(0, 0);
            int x = S(14), y = S(14);
            int w = Math.Max(S(120), inspBody.ClientSize.Width - S(28));
            inspBody.SuspendLayout();
            foreach (Control c in stack)
            {
                if (!c.Visible) continue;
                c.SetBounds(x, y, w, c.Height);
                y += c.Height + S(12);
            }
            inspBody.AutoScrollMinSize = new Size(0, y + S(4));
            inspBody.ResumeLayout(false);
        }

        void UpdateColorSwatch()
        {
            Color c;
            swColor.Value = ScreenPixel.TryParseHex(tfColor.Text, out c) ? c : Pal.Raised;
        }

        void ValidatePool()
        {
            List<List<Keys>> pool;
            string bad;
            bool ok = KeyPool.TryParse(tfPool.Text, out pool, out bad);
            tfPool.TextColorOverride = ok ? Color.Empty : Color.FromArgb(225, 70, 70);
            tfPool.ApplyTheme();
        }

        void FixRange(NumBox min, NumBox max, NumBox lng, bool minChanged)
        {
            bool old = rebuilding;
            rebuilding = true;
            try
            {
                if (min.Value > max.Value)
                {
                    if (minChanged) max.Value = min.Value;
                    else min.Value = max.Value;
                }
                if (lng.Value != 0 && lng.Value < max.Value) lng.Value = max.Value;
            }
            finally { rebuilding = old; }
        }

        // Valori iniziali sensati quando si attiva la casualità: parte dalla durata fissa già impostata.
        void SeedRange(NumBox min, NumBox max, NumBox pct, NumBox lng, int fixedMs, int pctDefault)
        {
            if (max.Value > min.Value) return;
            bool old = rebuilding;
            rebuilding = true;
            try
            {
                int d = Math.Max(10, fixedMs);
                min.Value = d;
                max.Value = d * 2;
                pct.Value = pctDefault;
                lng.Value = d * 4;
            }
            finally { rebuilding = old; }
        }

        // Etichette presenti nella scheda attiva (per il menu del Goto).
        List<string> LabelNames()
        {
            List<string> names = new List<string>();
            foreach (MacroStep s in ActiveSteps)
            {
                if (s.Type != StepType.Label) continue;
                string n = (s.Name ?? "").Trim();
                if (n.Length > 0 && !names.Contains(n)) names.Add(n);
            }
            return names;
        }

        void RefreshLabelItems(string current)
        {
            bool old = rebuilding;
            rebuilding = true;
            try
            {
                List<string> names = LabelNames();
                if (!string.IsNullOrEmpty(current) && !names.Contains(current)) names.Add(current);
                cbLabels.SetItems(names);
                cbLabels.SelectedIndex = string.IsNullOrEmpty(current) ? -1 : names.IndexOf(current);
            }
            finally { rebuilding = old; }
        }

        // ---------- variabili e "Insieme" ----------

        // Variabili già usate da un passo "Variabile" in una delle due schede.
        List<string> VarNames()
        {
            List<string> names = new List<string>();
            foreach (List<MacroStep> src in new List<MacroStep>[] { profile.Steps, profile.MenuSteps })
            {
                if (src == null) continue;
                foreach (MacroStep s in src)
                {
                    string n = (s.VarName ?? "").Trim();
                    if (s.Type == StepType.SetVar && n.Length > 0 && !names.Contains(n)) names.Add(n);
                }
            }
            return names;
        }

        string DefaultVarName()
        {
            List<string> names = VarNames();
            return names.Count > 0 ? names[0] : L.T("contatore", "counter");
        }

        // Se il passo è figlio diretto di una "Scegli a caso" senza Opzioni (ogni figlio è un'alternativa) ne restituisce l'indice.
        int ChoiceParentOf(int idx)
        {
            List<MacroStep> steps = ActiveSteps;
            if (idx <= 0 || idx >= steps.Count) return -1;
            if (Flow.Prepare(steps) != null) return -1;       // aggiorna Match / OptionMarks dopo una modifica
            for (int h = idx - 1; h >= 0; h--)
            {
                MacroStep c = steps[h];
                if (!c.IsBlockHeader || c.Match <= idx) continue;
                bool noOptions = c.OptionMarks == null || c.OptionMarks.Count == 0;
                return c.Type == StepType.ChoiceStart && noOptions ? h : -1;
            }
            return -1;
        }

        // campi -> parte i della copia di lavoro
        void StorePartFields(int i)
        {
            if (editParts == null || i < 0 || i >= editParts.Length) return;
            ComboPart p = editParts[i];
            p.Enabled = chkPartOn.Checked;
            p.Chance = nudPartChance.Value;
            if (i == ComboPart.Keys) p.Input = tfPool.Text.Trim();
            else if (i == ComboPart.Click || i == ComboPart.Aim) p.Input = ButtonCanon[Math.Max(0, cbButton.SelectedIndex)];
            else
            {
                p.MoveX = nudX.Value;
                p.MoveY = nudY.Value;
                p.RandMove = chkRandMove.Checked;
            }
            p.DurMin = nudDurMin.Value;
            p.DurMax = nudDurMax.Value;
            p.LongPct = nudDurPct.Value;
            p.LongMax = nudDurLong.Value;
        }

        // parte i della copia di lavoro -> campi (senza scatenare la modifica del passo)
        void LoadPartFields(int i)
        {
            if (editParts == null || i < 0 || i >= editParts.Length) return;
            ComboPart p = editParts[i];
            bool old = rebuilding;
            rebuilding = true;
            try
            {
                chkPartOn.Checked = p.Enabled;
                nudPartChance.Value = p.Chance;
                if (i == ComboPart.Keys) tfPool.Text = p.Input;
                else if (i == ComboPart.Click || i == ComboPart.Aim)
                {
                    int bi = Array.IndexOf(ButtonCanon, p.Input);
                    cbButton.SelectedIndex = bi >= 0 ? bi : (i == ComboPart.Aim ? 1 : 0);
                }
                else
                {
                    nudX.Value = p.MoveX;
                    nudY.Value = p.MoveY;
                    chkRandMove.Checked = p.RandMove;
                }
                nudDurMin.Value = p.DurMin;
                nudDurMax.Value = p.DurMax;
                nudDurPct.Value = p.LongPct;
                nudDurLong.Value = p.LongMax;
            }
            finally { rebuilding = old; }
            if (i == ComboPart.Keys) ValidatePool();
        }

        void OnPartSwitched()
        {
            if (rebuilding || editType != StepType.Combo) return;
            StorePartFields(partShown);
            partShown = segPart.SelectedIndex;
            LoadPartFields(partShown);
            UpdateEditorVisibility();
        }

        // ---------- visibilità dei campi ----------

        static void Vis(Control c, bool v) { if (c.Visible != v) c.Visible = v; }

        void UpdateEditorVisibility()
        {
            bool none = editIndex < 0;
            StepType t = editType;
            int ifKind = (!none && t == StepType.IfStart) ? cbCondKind.SelectedIndex : 0;
            bool until = !none && t == StepType.LoopStart && chkUntil.Checked;
            bool pixel = !none && (t == StepType.WaitPixel || t == StepType.WaitPixelGone || (t == StepType.IfStart && ifKind == 0) || until);
            bool absolute = pixel || (!none && (t == StepType.ClickAt || t == StepType.MoveTo));
            bool combo = !none && t == StepType.Combo;
            int part = combo ? segPart.SelectedIndex : -1;
            bool timed = !none && (t == StepType.Keys || t == StepType.RandomKey || t == StepType.Click || t == StepType.MouseMove || t == StepType.Wait
                || t == StepType.ClickAt || t == StepType.WaitPixel || t == StepType.WaitPixelGone);
            bool randDurOk = !none && (t == StepType.Keys || t == StepType.RandomKey || t == StepType.Click || t == StepType.ClickAt
                || t == StepType.MouseMove || t == StepType.Wait);
            bool hasPause = randDurOk || (!none && t == StepType.MoveTo) || combo;
            bool durRandom = (randDurOk && chkRandDur.Checked) || combo;
            bool pauseRandom = hasPause && chkRandPause.Checked;
            bool marker = none || t == StepType.LoopEnd || t == StepType.Else || t == StepType.IfEnd || t == StepType.Break || t == StepType.StopMacro
                || t == StepType.Option || t == StepType.ChoiceEnd;
            bool random = segMode.SelectedIndex == 1;
            bool choiceChild = !none && !marker && ChoiceParentOf(editIndex) >= 0;
            bool mouseFields = (!none && t == StepType.MouseMove) || (combo && part == ComboPart.Mouse);

            inspBody.SuspendLayout();
            Vis(fbName, !none && t != StepType.LoopEnd && t != StepType.IfEnd && t != StepType.Else && t != StepType.Option && t != StepType.ChoiceEnd);
            Vis(fbCondKind, !none && t == StepType.IfStart);
            Vis(fbVarName, !none && (t == StepType.SetVar || (t == StepType.IfStart && ifKind == 1)));
            Vis(fbCmp, !none && t == StepType.IfStart && ifKind == 1);
            Vis(fbChance, !none && t == StepType.IfStart && ifKind == 2);
            Vis(fbVarOp, !none && t == StepType.SetVar);
            Vis(rowSetVals, !none && t == StepType.SetVar);
            Vis(fbSetVal2, !none && t == StepType.SetVar && cbVarOp.SelectedIndex == 3);
            Vis(fbAltWeight, !none && t == StepType.ChoiceStart);
            Vis(fbPickCount, combo);
            Vis(fbPart, combo);
            Vis(chkPartOn, combo);
            Vis(fbPartChance, combo);
            Vis(fbKeys, !none && t == StepType.Keys);
            Vis(fbPool, (!none && t == StepType.RandomKey) || (combo && part == ComboPart.Keys));
            Vis(fbPreset, (!none && t == StepType.RandomKey) || (combo && part == ComboPart.Keys));
            Vis(fbGoto, !none && t == StepType.Goto);
            Vis(fbButton, (!none && (t == StepType.Click || t == StepType.ClickAt)) || (combo && (part == ComboPart.Click || part == ComboPart.Aim)));
            Vis(rowLimits, !none && t == StepType.LoopStart);
            Vis(chkUntil, !none && t == StepType.LoopStart);
            Vis(fbCond, !none && ((t == StepType.IfStart && ifKind == 0) || until));
            Vis(rowArea, absolute || mouseFields);
            Vis(fbW, pixel);
            Vis(fbH, pixel);
            Vis(fbColor, pixel);
            Vis(rowPickBtns, absolute);
            Vis(btnTest, pixel);
            Vis(chipTest, pixel);
            Vis(fbPreview, pixel);
            Vis(chkCheckFirst, until);
            Vis(fbDur, timed && !durRandom);
            Vis(chkRandDur, randDurOk);
            Vis(fbDurRange, durRandom);
            Vis(fbDurLong, durRandom);
            Vis(fbPause, hasPause && !pauseRandom);
            Vis(chkRandPause, hasPause);
            Vis(fbPauseRange, pauseRandom);
            Vis(fbPauseLong, pauseRandom);
            Vis(fbJit, random && randDurOk && !(durRandom && pauseRandom));
            Vis(fbPosJit, !none && (t == StepType.ClickAt || t == StepType.MoveTo));
            Vis(chkRandMove, mouseFields);
            Vis(fbWeight, (!none && t == StepType.Option) || choiceChild || (random && !marker));

            // etichette dei campi che cambiano con il tipo
            if (mouseFields) fbX.Caption = L.T("Spostamento X", "Movement X");
            else fbX.Caption = "X";
            if (mouseFields) fbY.Caption = L.T("Spostamento Y", "Movement Y");
            else fbY.Caption = "Y";
            fbSetVal.Caption = cbVarOp.SelectedIndex == 3 ? L.T("Da", "From") : L.T("Valore", "Value");
            fbSetVal2.Caption = L.T("A", "To");
            fbWeight.Caption = (!none && t == StepType.Option) ? L.T("Peso dell'alternativa", "Alternative weight")
                : (choiceChild ? L.T("Peso nella scelta", "Weight in the pick") : L.T("Peso (random)", "Weight (random)"));
            fbVarName.Caption = L.T("Nome variabile", "Variable name");
            if (combo)
            {
                fbButton.Caption = part == ComboPart.Aim ? L.T("Pulsante di mira (tenuto premuto)", "Aim button (held down)")
                    : L.T("Pulsante del click", "Click button");
                fbPool.Caption = L.T("Tasti: uno di questi, a caso", "Keys: one of these, at random");
                fbDurRange.Caption = L.T("Durata casuale: da – a (ms)", "Random duration: from – to (ms)");
            }
            else
            {
                fbButton.Caption = L.T("Pulsante", "Button");
                fbPool.Caption = L.T("Insieme di tasti", "Key set");
                fbDurRange.Caption = L.T("Intervallo: da – a (ms)", "Range: from – to (ms)");
            }
            fbW.Caption = L.T("Larg.", "Width");
            fbH.Caption = L.T("Alt.", "Height");
            fbDur.Caption = (!none && (t == StepType.WaitPixel || t == StepType.WaitPixelGone)) ? L.T("Timeout (ms)", "Timeout (ms)")
                : (!none && (t == StepType.ClickAt || t == StepType.Click) ? L.T("Pressione (ms)", "Hold (ms)")
                : (!none && t == StepType.Wait ? L.T("Attesa (ms)", "Wait (ms)") : L.T("Durata (ms)", "Duration (ms)")));
            fbName.Caption = (!none && t == StepType.Label) ? L.T("Nome dell'etichetta", "Label name") : L.T("Nome", "Name");
            inspBody.ResumeLayout(false);

            if (none)
                inspHeader.Set("", L.T("Nessun passo selezionato", "No step selected"), Sem.Input, true);
            else
                inspHeader.Set(StepStyle.Badge(t), InspTitle(), StepStyle.SemOf(t), false);

            LayoutStack();
            RefreshHeader();
        }

        string InspTitle()
        {
            List<MacroStep> steps = ActiveSteps;
            if (editIndex >= 0 && editIndex < steps.Count) return StepStyle.Title(steps[editIndex]);
            return "";
        }

        // ---------- modifica dal vivo ----------

        bool ApplyFields(MacroStep s)
        {
            s.Name = tfName.Text.Trim();
            switch (s.Type)
            {
                case StepType.Keys:
                    {
                        List<Keys> keys;
                        if (!KeyParser.TryParseCombo(tfKeys.Text, out keys)) return false;
                        s.Input = tfKeys.Text;
                        break;
                    }
                case StepType.RandomKey:
                    {
                        List<List<Keys>> pool;
                        string bad;
                        if (!KeyPool.TryParse(tfPool.Text, out pool, out bad)) return false;
                        s.Input = tfPool.Text.Trim();
                        break;
                    }
                case StepType.Goto:
                    s.Input = cbLabels.SelectedText;
                    break;
                case StepType.Click:
                    s.Input = ButtonCanon[Math.Max(0, cbButton.SelectedIndex)];
                    break;
                case StepType.ClickAt:
                    s.Input = ButtonCanon[Math.Max(0, cbButton.SelectedIndex)];
                    s.MoveX = nudX.Value;
                    s.MoveY = nudY.Value;
                    break;
                case StepType.MoveTo:
                case StepType.MouseMove:
                    s.MoveX = nudX.Value;
                    s.MoveY = nudY.Value;
                    break;
                case StepType.WaitPixel:
                case StepType.WaitPixelGone:
                case StepType.IfStart:
                    {
                        if (s.Type == StepType.IfStart)
                        {
                            s.CondKind = Math.Max(0, cbCondKind.SelectedIndex);
                            if (s.CondKind == 1)
                            {
                                s.VarName = tfVarName.Text.Trim();
                                s.CmpOp = Math.Max(0, cbCmp.SelectedIndex);
                                s.CmpValue = nudIfVal.Value;
                                s.Negate = false;
                                break;
                            }
                            if (s.CondKind == 2)
                            {
                                s.ChancePct = nudChance.Value;
                                s.Negate = false;
                                break;
                            }
                        }
                        Color c;
                        if (!ScreenPixel.TryParseHex(tfColor.Text, out c)) return false;
                        s.Input = ScreenPixel.ToHex(c);
                        s.MoveX = nudX.Value;
                        s.MoveY = nudY.Value;
                        s.Width = nudW.Value;
                        s.Height = nudH.Value;
                        s.Tolerance = nudTol.Value;
                        s.Negate = s.Type == StepType.IfStart && cbCond.SelectedIndex == 1;
                        break;
                    }
                case StepType.SetVar:
                    s.VarName = tfVarName.Text.Trim();
                    s.VarOp = Math.Max(0, cbVarOp.SelectedIndex);
                    s.CmpValue = nudSetVal.Value;
                    s.VarValue2 = nudSetVal2.Value;
                    break;
                case StepType.ChoiceStart:
                    s.AltWeight = nudAltWeight.Value;
                    break;
                case StepType.Combo:
                    {
                        if (editParts == null) return false;
                        if (partShown >= 0)
                        {
                            // il campo-pool deve essere valido per la parte "tasti" attualmente mostrata
                            if (partShown == ComboPart.Keys)
                            {
                                List<List<Keys>> pool;
                                string bad;
                                if (!KeyPool.TryParse(tfPool.Text, out pool, out bad)) return false;
                            }
                            StorePartFields(partShown);
                        }
                        ComboPart[] np = new ComboPart[editParts.Length];
                        for (int i = 0; i < np.Length; i++) np[i] = editParts[i].Clone();
                        s.Parts = np;
                        s.PickCount = nudPickCount.Value;
                        break;
                    }
                case StepType.LoopStart:
                    s.Count = nudCount.Value;
                    s.DurationMs = nudLoopSec.Value * 1000;
                    s.UseUntil = chkUntil.Checked;
                    s.CheckFirst = chkUntil.Checked && chkCheckFirst.Checked;
                    if (s.UseUntil)
                    {
                        Color u;
                        if (!ScreenPixel.TryParseHex(tfColor.Text, out u)) return false;
                        s.Input = ScreenPixel.ToHex(u);
                        s.MoveX = nudX.Value;
                        s.MoveY = nudY.Value;
                        s.Width = nudW.Value;
                        s.Height = nudH.Value;
                        s.Tolerance = nudTol.Value;
                        s.Negate = cbCond.SelectedIndex == 1;
                    }
                    break;
            }
            if (s.HasDuration)
            {
                s.DurationMs = nudDur.Value;
                s.PauseMs = nudPause.Value;
                s.JitterMs = nudJit.Value;
            }
            else if (s.Type == StepType.MoveTo || s.Type == StepType.Combo)
            {
                s.PauseMs = nudPause.Value;
            }
            if (s.SupportsRandDur)
            {
                s.RandDur = chkRandDur.Checked;
                s.DurMin = nudDurMin.Value;
                s.DurMax = nudDurMax.Value;
                s.DurLongPct = nudDurPct.Value;
                s.DurLongMax = nudDurLong.Value;
            }
            if (s.HasPause)
            {
                s.RandPause = chkRandPause.Checked;
                s.PauseMin = nudPauseMin.Value;
                s.PauseMax = nudPauseMax.Value;
                s.PauseLongPct = nudPausePct.Value;
                s.PauseLongMax = nudPauseLong.Value;
            }
            if (s.Type == StepType.ClickAt || s.Type == StepType.MoveTo) s.PosJitter = nudPosJit.Value;
            if (s.Type == StepType.MouseMove) s.RandMove = chkRandMove.Checked;
            s.Weight = nudWeight.Value;
            return true;
        }

        void OnEditorChanged()
        {
            if (rebuilding || editIndex < 0) return;
            List<MacroStep> steps = ActiveSteps;
            if (editIndex >= steps.Count) return;
            MacroStep n = steps[editIndex].Clone();
            if (!ApplyFields(n)) return;
            steps[editIndex] = n;
            list.Rebuild();
            inspHeader.Set(StepStyle.Badge(n.Type), StepStyle.Title(n), StepStyle.SemOf(n.Type), false);
            RefreshHeader();
            if (n.Type == StepType.Label) { /* gli altri Goto vedono il nuovo nome al prossimo cambio di selezione */ }
        }

        void OnListSelectionChanged()
        {
            if (MenuTab) menuSel = list.SelectedIndex; else gameSel = list.SelectedIndex;
            LoadEditorFromSelection();
        }

        void LoadEditorFromSelection()
        {
            int idx = list.SelectedIndex;
            List<MacroStep> steps = ActiveSteps;
            if (idx < 0 || idx >= steps.Count)
            {
                editIndex = -1;
                UpdateEditorVisibility();
                return;
            }
            MacroStep s = steps[idx];

            rebuilding = true;
            try
            {
                editIndex = idx;
                editType = s.Type;
                tfName.Text = s.Name;
                tfKeys.Text = s.Type == StepType.Keys ? s.Input : "";
                if (s.Type == StepType.RandomKey) tfPool.Text = s.Input;
                if (s.Type == StepType.Goto) RefreshLabelItems(s.Input);
                if (s.Type == StepType.Click || s.Type == StepType.ClickAt)
                {
                    int bi = Array.IndexOf(ButtonCanon, s.Input);
                    cbButton.SelectedIndex = bi >= 0 ? bi : 0;
                }
                bool colorStep = s.Type == StepType.WaitPixel || s.Type == StepType.WaitPixelGone || (s.Type == StepType.IfStart && s.CondKind == 0)
                    || (s.Type == StepType.LoopStart && s.UseUntil);
                if (colorStep) tfColor.Text = s.Input;
                else if (s.Type == StepType.IfStart)
                {
                    Color tc;
                    tfColor.Text = ScreenPixel.TryParseHex(s.Input, out tc) ? s.Input : "FFFFFF";
                }
                nudX.Value = s.MoveX;
                nudY.Value = s.MoveY;
                nudW.Value = s.Width;
                nudH.Value = s.Height;
                nudTol.Value = s.Tolerance;
                nudCount.Value = s.Count;
                nudLoopSec.Value = s.DurationMs / 1000;
                chkUntil.Checked = s.UseUntil;
                chkCheckFirst.Checked = s.CheckFirst;
                cbCond.SelectedIndex = s.Negate ? 1 : 0;
                if (s.HasDuration) nudDur.Value = s.DurationMs;
                nudPause.Value = s.PauseMs;
                nudJit.Value = s.JitterMs;
                nudWeight.Value = s.Weight;
                chkRandDur.Checked = s.RandDur;
                nudDurMin.Value = s.DurMin;
                nudDurMax.Value = s.DurMax;
                nudDurPct.Value = s.DurLongPct;
                nudDurLong.Value = s.DurLongMax;
                chkRandPause.Checked = s.RandPause;
                nudPauseMin.Value = s.PauseMin;
                nudPauseMax.Value = s.PauseMax;
                nudPausePct.Value = s.PauseLongPct;
                nudPauseLong.Value = s.PauseLongMax;
                nudPosJit.Value = s.PosJitter;
                chkRandMove.Checked = s.RandMove;

                // variabili, probabilità, scelta casuale
                cbCondKind.SelectedIndex = s.Type == StepType.IfStart ? Math.Min(2, Math.Max(0, s.CondKind)) : 0;
                if (s.Type == StepType.SetVar || (s.Type == StepType.IfStart && s.CondKind == 1))
                    tfVarName.Text = string.IsNullOrEmpty(s.VarName) ? DefaultVarName() : s.VarName;
                cbCmp.SelectedIndex = Math.Min(5, Math.Max(0, s.CmpOp));
                nudIfVal.Value = s.CmpValue;
                nudChance.Value = s.ChancePct;
                cbVarOp.SelectedIndex = Math.Min(3, Math.Max(0, s.VarOp));
                nudSetVal.Value = s.CmpValue;
                nudSetVal2.Value = s.VarValue2;
                nudAltWeight.Value = Math.Max(1, s.AltWeight);
                nudPickCount.Value = s.PickCount;

                // "Insieme": copia di lavoro delle 4 parti, si mostra quella scelta nel selettore
                if (s.Type == StepType.Combo)
                {
                    ComboPart[] src = s.Parts ?? ComboPart.Defaults();
                    ComboPart[] defs = ComboPart.Defaults();
                    editParts = new ComboPart[4];
                    for (int i = 0; i < 4; i++) editParts[i] = (i < src.Length && src[i] != null ? src[i] : defs[i]).Clone();
                    partShown = Math.Max(0, Math.Min(3, segPart.SelectedIndex));
                    LoadPartFields(partShown);
                }
                else
                {
                    editParts = null;
                    partShown = -1;
                }
            }
            finally { rebuilding = false; }
            UpdateColorSwatch();
            ValidatePool();
            UpdateEditorVisibility();
            UpdatePreviewForSelection();
        }

        // ---------- scheda attiva e intestazione della lista ----------

        void OnTabChanged()
        {
            // la selezione viene ricordata per scheda
            list.SetSource(ActiveSteps, MenuTab ? 1 : 0);
            menuOpts.Visible = MenuTab && !isRunning;
            RefreshHeader();
            int sel = MenuTab ? menuSel : gameSel;
            if (sel >= 0 && sel < ActiveSteps.Count) list.SelectedIndex = sel;
            else { editIndex = -1; UpdateEditorVisibility(); }
        }

        int CountSteps(List<MacroStep> steps)
        {
            int n = 0;
            foreach (MacroStep s in steps)
                if (!s.IsBlockFooter && !s.IsBranchMarker) n++;
            return n;
        }

        void RefreshHeader()
        {
            if (listHeader == null || list == null) return;
            string cap = MenuTab ? L.T("Sequenza · riavvio partita (menu)", "Sequence · match restart (menu)")
                                 : L.T("Sequenza · principale", "Sequence · main");
            List<MacroStep> steps = ActiveSteps;
            lastStructureError = Flow.Prepare(steps);
            if (isRunning) { listHeader.Set(cap, "", Color.Empty); return; }
            if (lastStructureError != null)
            {
                listHeader.Set(cap, lastStructureError, Pal.Warn);
                return;
            }
            long est = Estimator.CycleMs(steps);
            string right = CountSteps(steps) + " step · " + L.T("ciclo stimato ", "estimated cycle ") + Ui.Mmss(est);
            listHeader.Set(cap, right, Color.Empty);
        }

        // ---------- aggiunta e organizzazione dei passi ----------

        MacroStep NewStep(StepType t)
        {
            switch (t)
            {
                case StepType.Keys: return MacroStep.MakeKeys("W", 500, 100, 0, 1);
                case StepType.RandomKey: return MacroStep.MakeRandomKey("W, A, S, D", 400, 1500, 10, 4000, 100);
                case StepType.Click: return MacroStep.MakeClick("Sinistro", 60, 150);
                case StepType.ClickAt: return MacroStep.MakeClickAt("Sinistro", 0, 0, 60, 150);
                case StepType.MoveTo: return MacroStep.MakeMoveTo(0, 0, 0);
                case StepType.MouseMove: return MacroStep.MakeMove(200, 0, 300, 100, 0, 1);
                case StepType.Wait: return MacroStep.MakeWait(1000);
                case StepType.WaitPixel: return MacroStep.MakeWaitPixel(true, 0, 0, 1, 1, "FFFFFF", 24, 30000);
                case StepType.LoopStart: return MacroStep.MakeLoop(0);
                case StepType.IfStart: return MacroStep.MakeIf(0, 0, 1, 1, "FFFFFF", 24, false);
                case StepType.Label: return MacroStep.MakeLabel(NextLabelName());
                case StepType.SetVar: return MacroStep.MakeSetVar(DefaultVarName(), 1, 1, 0);
                case StepType.Combo: return MacroStep.MakeCombo();
                case StepType.Goto:
                    {
                        List<string> names = LabelNames();
                        return MacroStep.MakeGoto(names.Count > 0 ? names[0] : "");
                    }
                default: return MacroStep.Make(t);
            }
        }

        string NextLabelName()
        {
            List<string> names = LabelNames();
            for (int i = 1; i < 1000; i++)
            {
                string n = L.T("punto", "point") + i;
                if (!names.Contains(n)) return n;
            }
            return "label";
        }

        // Inserisce un blocco dalla palette: dopo il passo selezionato (dentro il blocco se l'intestazione è selezionata).
        void AddNew(StepType t)
        {
            if (isRunning) return;
            int sel = list.SelectedIndex;
            AddNewAt(t, sel >= 0 ? sel + 1 : ActiveSteps.Count, sel);
        }

        void AddNewAt(StepType t, int at)
        {
            AddNewAt(t, at, at - 1);
        }

        // at = indice di inserimento; anchor = passo "di riferimento" per capire in quale Se inserire un Altrimenti.
        void AddNewAt(StepType t, int at, int anchor)
        {
            if (isRunning) return;
            List<MacroStep> steps = ActiveSteps;
            at = Math.Max(0, Math.Min(steps.Count, at));

            if (t == StepType.Else)
            {
                string err = Flow.Prepare(steps);
                int ifIdx = -1;
                if (err == null && anchor >= 0 && anchor < steps.Count)
                {
                    for (int j = anchor; j >= 0; j--)
                    {
                        MacroStep h = steps[j];
                        if (h.Type == StepType.IfStart && h.Match >= anchor) { ifIdx = j; break; }
                    }
                }
                if (ifIdx < 0)
                {
                    Warn(L.T("Seleziona un passo dentro un blocco 'Se' (o il 'Se' stesso) e poi aggiungi 'Altrimenti'.",
                             "Select a step inside an 'If' block (or the 'If' itself) and then add 'Else'."));
                    return;
                }
                if (steps[ifIdx].ElseMatch >= 0)
                {
                    Warn(L.T("Questo 'Se' ha già un 'Altrimenti'.", "This 'If' already has an 'Else'."));
                    return;
                }
                at = steps[ifIdx].Match;
            }

            if (t == StepType.Option)
            {
                // un'Opzione ha senso solo dentro una "Scegli a caso": subito dopo il passo selezionato se ne è figlio diretto,
                // altrimenti in fondo alla scelta che lo contiene
                string err = Flow.Prepare(steps);
                int chIdx = -1;
                bool nested = false;
                if (err == null && anchor >= 0 && anchor < steps.Count)
                {
                    for (int j = anchor; j >= 0; j--)
                    {
                        MacroStep h = steps[j];
                        if (!h.IsBlockHeader || h.Match < anchor) continue;
                        if (h.Type == StepType.ChoiceStart) { chIdx = j; break; }
                        nested = true;
                    }
                }
                if (chIdx < 0)
                {
                    Warn(L.T("Seleziona un passo dentro una 'Scegli a caso' (o la 'Scegli a caso' stessa) e poi aggiungi 'Opzione'.",
                             "Select a step inside a 'Pick random' block (or the block itself) and then add 'Option'."));
                    return;
                }
                if (nested || chIdx == anchor || at <= chIdx || at > steps[chIdx].Match) at = steps[chIdx].Match;
            }

            List<MacroStep> ins = new List<MacroStep>();
            ins.Add(NewStep(t));
            if (t == StepType.LoopStart) ins.Add(MacroStep.Make(StepType.LoopEnd));
            else if (t == StepType.IfStart) ins.Add(MacroStep.Make(StepType.IfEnd));
            else if (t == StepType.ChoiceStart) ins.Add(MacroStep.Make(StepType.ChoiceEnd));
            steps.InsertRange(at, ins);
            list.Reload(at);

            if (t == StepType.ClickAt || t == StepType.MoveTo || t == StepType.WaitPixel || t == StepType.IfStart)
                PickForEditor();
        }

        void DuplicateStep()
        {
            if (isRunning) return;
            int at = list.SelectedIndex;
            if (at < 0) return;
            List<MacroStep> steps = ActiveSteps;
            MacroStep s = steps[at];
            int end = at;
            if (s.IsBlockHeader)
            {
                Flow.Prepare(steps);
                if (s.Match > at) end = s.Match;
            }
            else if (s.IsBlockFooter || s.IsBranchMarker)
            {
                Warn(L.T("Seleziona l'intestazione del blocco per duplicarlo.", "Select the block header to duplicate it."));
                return;
            }
            List<MacroStep> copy = new List<MacroStep>();
            for (int i = at; i <= end; i++) copy.Add(steps[i].Clone());
            // un'etichetta duplicata ha bisogno di un nome nuovo
            foreach (MacroStep c in copy)
                if (c.Type == StepType.Label) c.Name = NextLabelNameAvoiding(copy, steps);
            steps.InsertRange(end + 1, copy);
            list.Reload(end + 1);
        }

        string NextLabelNameAvoiding(List<MacroStep> pending, List<MacroStep> existing)
        {
            List<string> used = new List<string>();
            foreach (MacroStep s in existing) if (s.Type == StepType.Label) used.Add((s.Name ?? "").Trim());
            foreach (MacroStep s in pending) if (s.Type == StepType.Label && !string.IsNullOrEmpty(s.Name)) used.Add(s.Name.Trim());
            for (int i = 1; i < 1000; i++)
            {
                string n = L.T("punto", "point") + i;
                if (!used.Contains(n)) return n;
            }
            return "label";
        }

        void RemoveStep()
        {
            if (isRunning) return;
            int at = list.SelectedIndex;
            if (at < 0) return;
            List<MacroStep> steps = ActiveSteps;
            MacroStep s = steps[at];
            if (s.IsBlockFooter)
            {
                MessageBox.Show(this, L.T("Per eliminare un blocco seleziona la sua intestazione (Ripeti / Se / Scegli a caso).",
                                          "To delete a block select its header (Repeat / If / Pick random)."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (s.IsBlockHeader)
            {
                Flow.Prepare(steps);
                int end = s.Match > at ? s.Match : at;
                int inner = end - at - 1;
                if (inner > 0)
                {
                    DialogResult dr = MessageBox.Show(this, L.T("Eliminare il blocco con i " + inner + " passi che contiene?",
                                                                "Delete the block with the " + inner + " steps it contains?"),
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (dr != DialogResult.Yes) return;
                }
                steps.RemoveRange(at, end - at + 1);
            }
            else
            {
                steps.RemoveAt(at);
            }
            list.Reload(Math.Min(at, steps.Count - 1));
        }

        void MoveStep(int delta)
        {
            if (isRunning) return;
            int at = list.SelectedIndex;
            if (at < 0) return;
            List<MacroStep> steps = ActiveSteps;
            if (Flow.Prepare(steps) != null)
            {
                Warn(L.T("La struttura dei blocchi non è valida: sistemala prima di spostare i passi.",
                         "The block structure is not valid: fix it before moving steps."));
                return;
            }
            MacroStep s = steps[at];
            if (s.IsBlockFooter || s.IsBranchMarker) return;
            int start = at;
            int end = (s.IsBlockHeader) ? s.Match : at;
            int count = end - start + 1;

            int newStart;
            if (delta < 0)
            {
                if (start == 0) return;
                int p = start - 1;
                MacroStep prev = steps[p];
                if (prev.IsBlockHeader || prev.IsBranchMarker) return;
                if (prev.IsBlockFooter)
                {
                    for (int j = 0; j < p; j++)
                        if (steps[j].IsBlockHeader && steps[j].Match == p) { p = j; break; }
                }
                newStart = p;
            }
            else
            {
                if (end + 1 >= steps.Count) return;
                MacroStep next = steps[end + 1];
                int nEnd = end + 1;
                if (next.IsBlockFooter || next.IsBranchMarker) return;
                if (next.IsBlockHeader) nEnd = next.Match;
                newStart = nEnd - count + 1;
            }

            List<MacroStep> backup = new List<MacroStep>(steps);
            List<MacroStep> block = steps.GetRange(start, count);
            steps.RemoveRange(start, count);
            steps.InsertRange(newStart, block);
            if (Flow.Prepare(steps) != null)
            {
                // un Goto/Esci spostato non può restare dov'è: si annulla
                steps.Clear();
                steps.AddRange(backup);
                Flow.Prepare(steps);
                Warn(L.T("Spostamento annullato: renderebbe non valida la sequenza (un Goto o un Esci perderebbe il suo bersaglio).",
                         "Move cancelled: it would make the sequence invalid (a Goto or an Exit would lose its target)."));
                return;
            }
            list.Reload(newStart);
        }

        // Trascinamento nella lista: sposta [from..to] davanti all'indice "ins" (indici prima dello spostamento).
        void ReorderSteps(int from, int to, int ins)
        {
            if (isRunning) return;
            List<MacroStep> steps = ActiveSteps;
            if (from < 0 || to >= steps.Count || ins < 0 || ins > steps.Count) return;
            if (ins >= from && ins <= to + 1) return;
            List<MacroStep> backup = new List<MacroStep>(steps);
            int count = to - from + 1;
            List<MacroStep> block = steps.GetRange(from, count);
            steps.RemoveRange(from, count);
            int at = ins > to ? ins - count : ins;
            steps.InsertRange(at, block);
            if (Flow.Prepare(steps) != null)
            {
                steps.Clear();
                steps.AddRange(backup);
                Flow.Prepare(steps);
                Warn(L.T("Spostamento annullato: renderebbe non valida la sequenza (un Goto o un Esci perderebbe il suo bersaglio).",
                         "Move cancelled: it would make the sequence invalid (a Goto or an Exit would lose its target)."));
                list.Reload(from);
                return;
            }
            list.Reload(at);
        }

        // Elenca i passi con posizione (0, 0): quasi sempre sono passi appena aggiunti e mai configurati.
        string UnsetPositions(List<MacroStep> steps, string listName)
        {
            List<string> found = new List<string>();
            for (int i = 0; i < steps.Count; i++)
            {
                MacroStep s = steps[i];
                bool positional = s.Type == StepType.ClickAt || s.Type == StepType.MoveTo || s.Type == StepType.WaitPixel
                    || s.Type == StepType.WaitPixelGone || (s.Type == StepType.IfStart && s.CondKind == 0) || (s.Type == StepType.LoopStart && s.UseUntil);
                if (positional && s.MoveX == 0 && s.MoveY == 0) found.Add(listName + " #" + (i + 1) + " (" + StepStyle.Title(s) + ")");
            }
            return string.Join(", ", found.ToArray());
        }

        // ---------- test della condizione e anteprima ----------

        void UpdatePreviewForSelection()
        {
            // l'anteprima resta valida solo se l'area è quella catturata l'ultima volta
            if (previewRect == Rectangle.Empty) return;
            Rectangle now = new Rectangle(nudX.Value, nudY.Value, nudW.Value, nudH.Value);
            if (now != previewRect) { areaPreview.Set(null, ""); previewRect = Rectangle.Empty; }
        }

        void ShowAreaPreview(int x, int y, int w, int h)
        {
            try
            {
                int cw = Math.Max(1, Math.Min(w, 480)), ch = Math.Max(1, Math.Min(h, 270));
                Bitmap bmp = ScreenPixel.CaptureArea(x, y, cw, ch);
                if (bmp != null)
                {
                    areaPreview.Set(bmp, w + " × " + h + " @ " + x + ", " + y);
                    previewRect = new Rectangle(x, y, w, h);
                }
            }
            catch (Exception) { }
        }

        // Legge ora il colore dell'area e dice se la condizione del passo selezionato è vera.
        void TestCondition()
        {
            if (editIndex < 0 || isRunning) return;
            Color target;
            if (!ScreenPixel.TryParseHex(tfColor.Text, out target))
            {
                chipTest.Mood = Chip.Tone.Warn;
                chipTest.Text = L.T("Colore non valido.", "Invalid color.");
                return;
            }
            int x = nudX.Value, y = nudY.Value, w = nudW.Value, h = nudH.Value, tol = nudTol.Value;
            bool negate = cbCond.SelectedIndex == 1;
            FormWindowState prev = WindowState;
            if (testMinimizes && prev != FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Minimized;
                Application.DoEvents();
                Thread.Sleep(550);
            }
            Color read;
            int delta;
            bool present = ScreenPixel.Check(x, y, w, h, target, tol, out read, out delta);
            ShowAreaPreview(x, y, w, h);
            if (testMinimizes && prev != FormWindowState.Minimized)
            {
                WindowState = prev;
                Activate();
            }
            bool result = negate ? !present : present;
            string rtxt = read.IsEmpty ? "?" : "#" + ScreenPixel.ToHex(read);
            if (present)
                chipTest.Text = L.T("Ultimo test: corrisponde · letto " + rtxt + " · Δ " + delta, "Last test: matches · read " + rtxt + " · Δ " + delta);
            else
                chipTest.Text = L.T("Ultimo test: non corrisponde · letto " + rtxt + " · Δ " + delta + " > ±" + tol,
                                    "Last test: no match · read " + rtxt + " · Δ " + delta + " > ±" + tol);
            if (editType == StepType.IfStart || editType == StepType.LoopStart)
                chipTest.Text += "  →  " + (result ? L.T("condizione VERA", "condition TRUE") : L.T("condizione falsa", "condition false"));
            chipTest.Mood = present ? Chip.Tone.Ok : Chip.Tone.Warn;
        }
    }
}
