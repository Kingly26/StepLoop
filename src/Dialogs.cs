using System;
using System.Drawing;
using System.Windows.Forms;

namespace StepLoop
{
    // Opzioni della registrazione (REC): finestra scura con controlli a tema.
    public class RecOptionsDialog : Form
    {
        public CheckField ChkWait;
        public CheckField ChkPauses;
        public CheckField ChkKeys;
        public FlatButton BtnOk;
        public FlatButton BtnCancel;

        public RecOptionsDialog(string stopKey, string waitKey)
        {
            SuspendLayout();
            Text = L.T("Registrazione", "Recording");
            Font = new Font("Segoe UI", 9f);
            BackColor = Pal.Bg;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Icon = AppIcon.Get();
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(Ui.S(540), Ui.S(310));

            Label info = new Label();
            info.ForeColor = Pal.Text2;
            info.BackColor = Pal.Bg;
            info.Font = Ui.F(9.5f);
            info.Location = new Point(Ui.S(20), Ui.S(16));
            info.Size = new Size(Ui.S(500), Ui.S(100));
            info.Text = L.T(
                "Dopo OK questa finestra si riduce a icona: usa il gioco normalmente e ogni clic verrà registrato con posizione e colore.\r\n"
                + waitKey + " = aggiungi un'attesa del colore sotto il cursore (utile per i pulsanti che cambiano colore al passaggio del mouse).   "
                + stopKey + " = fine registrazione.",
                "After OK this window is minimized: use the game normally and every click is recorded with position and color.\r\n"
                + waitKey + " = add a wait for the color under the cursor (useful for buttons that change color on hover).   "
                + stopKey + " = stop recording.");
            Controls.Add(info);

            ChkWait = MakeCheck(L.T("Prima di ogni clic attendi il colore del pulsante (consigliato)", "Before each click wait for the button color (recommended)"), 130, true);
            ChkPauses = MakeCheck(L.T("Registra anche le pause tra un clic e l'altro", "Also record the pauses between clicks"), 164, true);
            ChkKeys = MakeCheck(L.T("Registra anche i tasti premuti", "Also record the keys pressed"), 198, false);

            BtnOk = new FlatButton();
            BtnOk.Text = L.T("Inizia a registrare", "Start recording");
            BtnOk.Style = FlatButton.Look.Primary;
            BtnOk.BoldText = true;
            BtnOk.Size = new Size(Ui.S(170), Ui.S(36));
            BtnOk.Location = new Point(Ui.S(540 - 20 - 100 - 10 - 170), Ui.S(310 - 20 - 36));
            BtnOk.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(BtnOk);

            BtnCancel = new FlatButton();
            BtnCancel.Text = L.T("Annulla", "Cancel");
            BtnCancel.Size = new Size(Ui.S(100), Ui.S(36));
            BtnCancel.Location = new Point(Ui.S(540 - 20 - 100), Ui.S(310 - 20 - 36));
            BtnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(BtnCancel);

            AcceptButton = null;
            ResumeLayout(false);
        }

        CheckField MakeCheck(string text, int y, bool on)
        {
            CheckField c = new CheckField();
            c.Text = text;
            c.Checked = on;
            c.Location = new Point(Ui.S(22), Ui.S(y));
            c.Size = new Size(Ui.S(496), Ui.S(26));
            Controls.Add(c);
            return c;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeUi.DarkTitleBar(Handle, Theme.Dark);
        }
    }

    // Finestra "Suggerimenti e avvertenze" (pulsante con la lampadina).
    public class HintDialog : Form
    {
        public RichTextBox Box;
        FlatButton ok;

        public HintDialog(string stopKey, string pauseKey)
        {
            SuspendLayout();
            Text = L.T("Suggerimenti e avvertenze", "Tips and warnings");
            Font = new Font("Segoe UI", 9f);
            BackColor = Pal.Bg;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Icon = AppIcon.Get();
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(Ui.S(620), Ui.S(640));

            Card card = new Card();
            card.Location = new Point(Ui.S(14), Ui.S(14));
            card.Size = new Size(Ui.S(592), Ui.S(564));
            Controls.Add(card);

            Box = new RichTextBox();
            Box.ReadOnly = true;
            Box.BorderStyle = BorderStyle.None;
            Box.ScrollBars = RichTextBoxScrollBars.Vertical;
            Box.BackColor = Pal.Panel;
            Box.ForeColor = Pal.Text;
            Box.Location = new Point(Ui.S(14), Ui.S(12));
            Box.Size = new Size(Ui.S(564), Ui.S(540));
            Box.TabStop = false;
            card.Controls.Add(Box);

            ok = new FlatButton();
            ok.Text = "OK";
            ok.Style = FlatButton.Look.Primary;
            ok.BoldText = true;
            ok.Size = new Size(Ui.S(100), Ui.S(36));
            ok.Location = new Point(Ui.S(620 - 14 - 100), Ui.S(640 - 14 - 36));
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(ok);

            AddHeading(L.T("Anti-cheat e rischi", "Anti-cheat and risks"));
            AddBody(L.T(
                "Gli anti-cheat possono rilevare gli input automatici (macro, tasti e movimenti del mouse generati da un programma). "
                + "L'uso di questa app può portare a sanzioni o al ban dell'account: la scelta e il rischio sono tuoi.",
                "Anti-cheat systems can detect automated input (macros, keys and mouse movements generated by a program). "
                + "Using this app may lead to penalties or an account ban: the choice and the risk are yours."));

            AddHeading(L.T("A cosa serve", "What it is for"));
            AddBody(L.T(
                "StepLoop è un'app di automazione generica: sequenze di tasti, click e movimenti del mouse con cicli, condizioni sui colori dello schermo, "
                + "pause e casualità. Nasce per Rainbow Six Siege (le modalità in cui si gira con il drone) ed è lì che è consigliato usarla, "
                + "in particolare quando ci sono gli eventi in cui l'uso di una macro è consentito: "
                + "sul GitHub dell'autore verranno pubblicati aggiornamenti con profili pronti per quegli eventi. "
                + "Funziona anche con altri giochi e programmi, nel rispetto dei loro regolamenti.",
                "StepLoop is a general-purpose automation app: sequences of keys, clicks and mouse movements with loops, screen-color conditions, "
                + "waits and randomness. It was made for Rainbow Six Siege (the drone modes) and that is where it is recommended, "
                + "especially during the events where using a macro is allowed: "
                + "updates with ready-made profiles for those events will be published on the author's GitHub. "
                + "It also works with other games and programs, as long as you respect their rules."));

            AddHeading(L.T("Sessioni lunghe (farming)", "Long sessions (farming)"));
            AddBody(L.T(
                "Per sessioni molto lunghe è fortemente consigliato impostare al minimo la risoluzione e la qualità grafica del gioco, "
                + "limitare gli FPS e ridurre tutto ciò che è riducibile (ombre, effetti, V-Sync...): meno carico sul PC, meno calore e meno consumi.",
                "For very long sessions it is strongly recommended to set the game's resolution and graphics quality to the minimum, "
                + "cap the FPS and reduce everything that can be reduced (shadows, effects, V-Sync...): less load on the PC, less heat and lower power use."));

            AddHeading(L.T("Consigli d'uso", "Usage tips"));
            AddBody(L.T(
                "- " + stopKey + " avvia e ferma sempre la macro, anche dentro il gioco. " + pauseKey + " la mette in pausa senza uscire: "
                + "rilascia i tasti e li ripreme quando riprendi.\r\n"
                + "- Le attese a colore funzionano con il gioco in finestra o a schermo intero senza bordi: in schermo intero esclusivo i pixel non si leggono.\r\n"
                + "- Se il gioco gira come amministratore, avvia anche questa app come amministratore.\r\n"
                + "- Prova i profili con \"Simulazione\" prima di usarli davvero.\r\n"
                + "- Per rendere la macro meno ripetitiva: \"Scegli a caso\" esegue una sola alternativa (con pesi), \"Insieme\" fa partire tasti, click, mouse e mira nello stesso istante "
                + "con durate casuali, e le variabili con i \"Se\" permettono di contare e decidere (es. fai una cosa ogni 5 giri).\r\n"
                + "- Ogni condizione (Se, Ripeti fino a, Attendi colore) può avere più controlli: aggiungi un altro colore con «+» e scegli se basta uno (OR) "
                + "o servono tutti (AND), per esempio per uscire da un ciclo quando compare un colore oppure un altro.",
                "- " + stopKey + " always starts and stops the macro, even inside the game. " + pauseKey + " pauses it without leaving: "
                + "it releases the keys and presses them again when you resume.\r\n"
                + "- Color waits work with the game windowed or borderless: in exclusive fullscreen the pixels cannot be read.\r\n"
                + "- If the game runs as administrator, start this app as administrator too.\r\n"
                + "- Try profiles with \"Simulation\" before using them for real.\r\n"
                + "- To make the macro less repetitive: \"Pick random\" runs a single alternative (with weights), \"Together\" starts keys, click, mouse and aim at the same moment "
                + "with random durations, and variables with \"If\" let you count and decide (e.g. do something every 5 rounds).\r\n"
                + "- Every condition (If, Repeat until, Wait for color) can have several checks: add another color with «+» and choose whether any one (OR) "
                + "or all of them (AND) must hold, e.g. to leave a loop when one color or another shows up."));

            AddHeading(L.T("Esclusione di responsabilità", "Disclaimer"));
            AddBody(L.T(
                "Questo software è fornito \"così com'è\", senza alcuna garanzia. L'autore non è responsabile dell'uso che ne fai: "
                + "né di ban, sanzioni, perdita di account o altri danni, né dell'uso dell'app nei giochi online a scopo di cheating "
                + "o per ottenere vantaggi sleali. Sei l'unico responsabile del rispetto dei termini di servizio del gioco e delle regole degli eventi.",
                "This software is provided \"as is\", without any warranty. The author is not responsible for how you use it: "
                + "not for bans, penalties, loss of accounts or other damage, nor for using the app in online games for cheating "
                + "or to gain an unfair advantage. You alone are responsible for complying with the game's terms of service and the event rules."));

            Box.SelectionStart = 0;
            Box.SelectionLength = 0;
            ResumeLayout(false);
        }

        void AddHeading(string text)
        {
            Box.SelectionStart = Box.TextLength;
            Box.SelectionFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            Box.SelectionColor = Pal.Info;
            Box.AppendText(text + "\r\n");
        }

        void AddBody(string text)
        {
            Box.SelectionStart = Box.TextLength;
            Box.SelectionFont = new Font("Segoe UI", 9.5f);
            Box.SelectionColor = Pal.Text;
            Box.AppendText(text + "\r\n\r\n");
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeUi.DarkTitleBar(Handle, Theme.Dark);
            NativeUi.DarkScroll(Box.Handle, Theme.Dark);
        }
    }
}
