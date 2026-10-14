using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace StepLoop
{
    // Profilo, esecuzione, selezione area, registrazione, hotkey e ciclo di vita.
    public partial class MainForm
    {
        // ---------- profilo ----------

        Profile CollectProfile()
        {
            Profile p = new Profile();
            p.Mode = segMode.SelectedIndex == 1 ? RunMode.Random : RunMode.Sync;
            p.StartDelaySec = nudDelay.Value;
            p.Cycles = nudCycles.Value;
            p.Hotkey = cbHotkey.SelectedText;
            p.PauseHotkey = cbPause.SelectedText;
            p.WindowTitle = tfWindow.Text.Trim();
            p.Steps = profile.Steps;
            p.MenuSteps = profile.MenuSteps;
            p.AutoRestart = chkAuto.Checked;
            p.StartWithMenu = chkMenuFirst.Checked;
            p.GameSeconds = nudGameSec.Value;
            p.EndPixelEnabled = chkEndPixel.Checked;
            p.EndX = nudEndX.Value;
            p.EndY = nudEndY.Value;
            p.EndW = nudEndW.Value;
            p.EndH = nudEndH.Value;
            Color c;
            p.EndColor = ScreenPixel.TryParseHex(tfEndColor.Text, out c) ? ScreenPixel.ToHex(c) : "000000";
            p.EndTolerance = nudEndTol.Value;
            return p;
        }

        void ApplyProfile(Profile p)
        {
            profile = p;
            editIndex = -1;
            gameSel = menuSel = -1;
            rebuilding = true;
            try
            {
                segMode.SelectedIndex = p.Mode == RunMode.Random ? 1 : 0;
                nudDelay.Value = p.StartDelaySec;
                nudCycles.Value = p.Cycles;
                keyFix = true;
                try
                {
                    int hk = cbHotkey.IndexOfText(p.Hotkey);
                    cbHotkey.SelectedIndex = hk >= 0 ? hk : cbHotkey.IndexOfText("F8");
                    int pk = cbPause.IndexOfText(p.PauseHotkey);
                    cbPause.SelectedIndex = pk >= 0 ? pk : cbPause.IndexOfText("F7");
                }
                finally { keyFix = false; }
                tfWindow.Text = p.WindowTitle;
                chkAuto.Checked = p.AutoRestart;
                chkMenuFirst.Checked = p.StartWithMenu;
                nudGameSec.Value = p.GameSeconds;
                chkEndPixel.Checked = p.EndPixelEnabled;
                nudEndX.Value = p.EndX;
                nudEndY.Value = p.EndY;
                nudEndW.Value = p.EndW;
                nudEndH.Value = p.EndH;
                tfEndColor.Text = p.EndColor;
                nudEndTol.Value = p.EndTolerance;
            }
            finally { rebuilding = false; }
            EnsureDistinctKeys(true);
            UpdateEndSwatch();
            list.SetSource(ActiveSteps, MenuTab ? 1 : 0);
            list.SelectedIndex = ActiveSteps.Count > 0 ? 0 : -1;
            if (ActiveSteps.Count == 0) { editIndex = -1; UpdateEditorVisibility(); }
            UpdateRunTexts();
        }

        void LoadLastProfile()
        {
            string path = Path.Combine(DataDir, "last.json");
            Profile p = null;
            try { if (File.Exists(path)) p = Profile.Load(path); }
            catch (Exception) { p = null; }
            ApplyProfile(p ?? Profile.Default());
        }

        void SaveProfileDialog()
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                Directory.CreateDirectory(ProfilesDir);
                dlg.InitialDirectory = ProfilesDir;
                dlg.Filter = L.T("Profilo macro (*.json)|*.json", "Macro profile (*.json)|*.json");
                dlg.FileName = "profilo.json";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    CollectProfile().Save(dlg.FileName);
                    LogLine(L.T("Profilo salvato: ", "Profile saved: ") + Path.GetFileName(dlg.FileName));
                }
                catch (Exception ex) { Warn(L.T("Salvataggio non riuscito: ", "Saving failed: ") + ex.Message); }
            }
        }

        void LoadProfileDialog()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                Directory.CreateDirectory(ProfilesDir);
                dlg.InitialDirectory = ProfilesDir;
                dlg.Filter = L.T("Profilo macro (*.json)|*.json", "Macro profile (*.json)|*.json");
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ApplyProfile(Profile.Load(dlg.FileName));
                    LogLine(L.T("Profilo caricato: ", "Profile loaded: ") + Path.GetFileName(dlg.FileName));
                }
                catch (Exception ex) { Warn(L.T("Caricamento non riuscito: ", "Loading failed: ") + ex.Message); }
            }
        }

        // ---------- log ----------

        void Warn(string message)
        {
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void Ui_(Action a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(a); }
            catch (InvalidOperationException) { }
        }

        void LogLine(string text) { Enqueue(LogKind.Info, text); }

        // Chiamato anche dal thread della macro: solo una coda protetta, mai un BeginInvoke per riga.
        void Enqueue(LogKind kind, string text)
        {
            LogItem it = new LogItem();
            it.Time = DateTime.Now;
            it.Kind = kind;
            it.Text = text;
            lock (logQueue)
            {
                if (logQueue.Count >= 400)
                {
                    logQueue.Dequeue();
                    logDropped++;
                }
                logQueue.Enqueue(it);
            }
        }

        // ---------- esecuzione ----------

        void SetRunning(bool running)
        {
            isRunning = running;
            if (!running) runPaused = false;
            settingsCard.Visible = !running;
            statRep.Visible = statTime.Visible = statCycle.Visible = statStep.Visible = running;
            infoHost.Height = running ? S(98) : S(76);
            palette.Locked = running;
            list.ReadOnly = running;
            menuOpts.Visible = MenuTab && !running;
            inspBody.Visible = !running;
            inspFooter.Visible = !running;
            inspHeader.Visible = !running;
            livePanel.Visible = running;
            lblFollow.Visible = running;
            swFollow.Visible = running;
            btnPause.Enabled = running;
            btnRec.Enabled = !running;
            btnSave.Enabled = !running;
            btnLoad.Enabled = !running;
            btnNewProfile.Enabled = !running;
            RegisterPauseHotkey(running);
            if (running)
            {
                runStartedAt = DateTime.Now;
                list.Follow = swFollow.Checked;
                list.SelectedIndex = -1;
                list.SetLive(runner);
                livePanel.SetContext(runner, profile.Steps, profile.MenuSteps);
            }
            else
            {
                list.SetLive(null);
                livePanel.SetContext(null, profile.Steps, profile.MenuSteps);
            }
            UpdateRunTexts();
            RefreshHeader();
            LayoutListHeader();
            if (!running) { list.Rebuild(); if (list.SelectedIndex < 0) { editIndex = -1; UpdateEditorVisibility(); } }
        }

        void UpdateRunTexts()
        {
            if (btnStart == null) return;
            string startKey = cbHotkey.SelectedText;
            string pauseKey = cbPause.SelectedText;
            if (isRunning)
            {
                btnStart.Style = FlatButton.Look.Danger;
                btnStart.Icon = Glyph.Stop;
                btnStart.Text = L.T("STOP (", "STOP (") + startKey + ")";
            }
            else
            {
                btnStart.Style = FlatButton.Look.Success;
                btnStart.Icon = Glyph.Play;
                btnStart.Text = L.T("AVVIA (", "START (") + startKey + ")";
            }
            if (runPaused)
            {
                btnPause.Style = FlatButton.Look.Success;
                btnPause.Icon = Glyph.Play;
                btnPause.Text = L.T("Riprendi (", "Resume (") + pauseKey + ")";
            }
            else
            {
                btnPause.Style = FlatButton.Look.Normal;
                btnPause.Icon = Glyph.Pause;
                btnPause.Text = L.T("Pausa (", "Pause (") + pauseKey + ")";
            }
            StatusPill.State st = !isRunning ? StatusPill.State.Stopped : (runPaused ? StatusPill.State.Paused : StatusPill.State.Running);
            pill.Mode = st;
            if (!isRunning)
            {
                lblStatusTitle.Text = L.T("Fermo", "Stopped");
                lblStatusSub.Text = L.T(startKey + " ferma sempre tutto, anche nel gioco.", startKey + " always stops everything, even in the game.");
            }
            else if (runPaused)
            {
                lblStatusTitle.Text = L.T("In pausa", "Paused");
                lblStatusSub.Text = L.T(pauseKey + " riprende · " + startKey + " ferma tutto.", pauseKey + " resumes · " + startKey + " stops everything.");
            }
            else
            {
                lblStatusTitle.Text = L.T("In esecuzione", "Running");
                lblStatusSub.Text = L.T(pauseKey + " pausa · " + startKey + " ferma sempre tutto, anche nel gioco.",
                                        pauseKey + " pause · " + startKey + " always stops everything, even in the game.");
            }
            LayoutTopBar();
        }

        bool Busy { get { return recorder != null || toast != null; } }

        void Toggle()
        {
            if (Busy) return;
            if (runner != null && runner.IsRunning) { runner.Stop(); return; }
            StartRun();
        }

        // Pausa / ripresa: la macro resta dov'è (ciclo, giro, passo) e riparte da lì.
        void TogglePause()
        {
            MacroRunner r = runner;
            if (r == null || !r.IsRunning) return;
            r.TogglePause();
        }

        void OnRunnerPauseChanged()
        {
            MacroRunner r = runner;
            runPaused = r != null && r.IsRunning && r.IsPaused;
            UpdateRunTexts();
            list.Invalidate();
        }

        // I tasti Start/Stop e Pausa devono essere diversi: se coincidono, l'altro viene spostato.
        void EnsureDistinctKeys(bool startChanged)
        {
            if (keyFix || cbHotkey == null || cbPause == null) return;
            if (cbHotkey.SelectedText != cbPause.SelectedText) return;
            keyFix = true;
            try
            {
                DarkCombo other = startChanged ? cbPause : cbHotkey;
                string avoid = startChanged ? cbHotkey.SelectedText : cbPause.SelectedText;
                foreach (string cand in new string[] { "F7", "F8", "F6", "F10", "F11", "F12", "F9" })
                {
                    if (cand != avoid)
                    {
                        other.SelectedIndex = other.IndexOfText(cand);
                        break;
                    }
                }
            }
            finally { keyFix = false; }
        }

        void StartRun()
        {
            Profile p = CollectProfile();
            string err = Flow.Prepare(p.Steps);
            if (err != null) { Warn(L.T("Scheda Sequenza: ", "Sequence tab: ") + err); return; }
            err = Flow.Prepare(p.MenuSteps);
            if (err != null) { Warn(L.T("Scheda Riavvio partita: ", "Match restart tab: ") + err); return; }
            if (p.AutoRestart)
            {
                if (p.GameSeconds <= 0 && !p.EndPixelEnabled)
                {
                    Warn(L.T("Con il riavvio automatico imposta la durata della partita (secondi) e/o il colore di fine partita.",
                             "With automatic restart, set the match duration (seconds) and/or the end-of-match color."));
                    return;
                }
                if (p.MenuSteps.Count == 0)
                {
                    Warn(L.T("Nella scheda 'Riavvio partita (menu)' aggiungi i passi per far ripartire una partita (click su Gioca, attese, ecc.). Puoi usare REC.",
                             "In the 'Match restart (menu)' tab add the steps to start a new match (click Play, waits, etc.). You can use REC."));
                    return;
                }
            }
            else if (p.Steps.Count == 0)
            {
                Warn(L.T("Aggiungi almeno un'azione alla sequenza.", "Add at least one action to the sequence."));
                return;
            }

            string unset = UnsetPositions(p.Steps, L.T("Sequenza", "Sequence"));
            string unsetMenu = UnsetPositions(p.MenuSteps, "Menu");
            if (unsetMenu.Length > 0) unset = unset.Length > 0 ? unset + ", " + unsetMenu : unsetMenu;
            if (unset.Length > 0 && !chkDry.Checked)
            {
                DialogResult dr = MessageBox.Show(this,
                    L.T("Questi passi hanno posizione (0, 0), probabilmente non ancora impostata:\r\n\r\n" + unset
                        + "\r\n\r\nAvviare comunque? (con la Simulazione non c'è rischio)",
                        "These steps have position (0, 0), probably not set yet:\r\n\r\n" + unset
                        + "\r\n\r\nStart anyway? (there is no risk with Simulation)"),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (dr != DialogResult.Yes) return;
            }

            runner = new MacroRunner(p, chkDry.Checked, null, null);
            runner.TypedLog = Enqueue;
            runner.Finished += delegate { Ui_(delegate { SetRunning(false); }); };
            runner.PauseChanged += delegate { Ui_(OnRunnerPauseChanged); };
            SetRunning(true);
            runner.Start();
        }

        // ---------- timer dell'interfaccia ----------

        // Arresto d'emergenza: un thread dedicato legge direttamente lo stato del tasto, quindi funziona anche
        // se l'interfaccia fosse occupata o il messaggio WM_HOTKEY restasse in coda.
        void WatchdogLoop()
        {
            bool wasDown = false;
            while (!closing)
            {
                Thread.Sleep(15);
                int vk = stopVk;
                if (vk == 0) { wasDown = false; continue; }
                bool down = (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
                if (down && !wasDown)
                {
                    MacroRunner r = runner;
                    if (r != null && r.IsRunning)
                    {
                        r.Stop();
                        stopStamp = Environment.TickCount;
                    }
                }
                wasDown = down;
            }
        }

        void UiTick()
        {
            tickCount++;
            // log: al massimo 80 righe per giro
            List<LogItem> batch = null;
            int dropped;
            lock (logQueue)
            {
                int n = Math.Min(logQueue.Count, 80);
                if (n > 0) batch = new List<LogItem>(n);
                for (int i = 0; i < n; i++) batch.Add(logQueue.Dequeue());
                dropped = logDropped;
                logDropped = 0;
            }
            if (dropped > 0)
                logView.Add(DateTime.Now, LogKind.Info, L.T("(… " + dropped + " righe di log omesse: la macro è molto veloce)",
                                                          "(… " + dropped + " log lines omitted: the macro is very fast)"));
            if (batch != null)
                foreach (LogItem it in batch) logView.Add(it.Time, it.Kind, it.Text);

            MacroRunner r = runner;
            if (isRunning && r != null)
            {
                // cambia scheda se l'esecuzione passa dalla sequenza al menu e viceversa
                int l, idx, planned;
                long el;
                string label;
                r.GetCurrent(out l, out idx, out el, out planned, out label);
                if (l >= 0 && tabs.SelectedIndex != l) tabs.SelectedIndex = l;
                list.LiveTick();
                pill.Invalidate();
                if (tickCount % 3 == 0) UpdateStats(r, l, idx);
                livePanel.Invalidate();
            }
        }

        // schede in alto durante l'esecuzione
        void UpdateStats(MacroRunner r, int curList, int curIdx)
        {
            int rep = Math.Max(1, r.Repetition);
            int target = r.RepetitionTarget;
            if (r.InCountdown) rep = 0;
            statRep.Set(L.T("Ripetizione", "Repetition"), rep + " / " + (target > 0 ? target.ToString() : "∞"), "",
                target > 0 ? Math.Min(1.0, rep / (double)target) : -1);

            long el = r.ElapsedMs;
            statTime.Set(L.T("Tempo trascorso", "Elapsed time"), Ui.Hms(el),
                L.T("avvio alle ", "started at ") + runStartedAt.ToString("HH:mm:ss"), -1);

            List<MacroStep> steps = curList == 1 ? profile.MenuSteps : profile.Steps;
            long est = Estimator.CycleMs(profile.Steps);
            long last = r.LastCycleMs;
            statCycle.Set(L.T("Durata ciclo", "Cycle length"), Ui.Mmss(est > 0 ? est : last),
                L.T("stimata", "estimated") + (last > 0 ? " · " + L.T("ultimo giro ", "last round ") + Ui.Mmss(last) : ""), -1);

            string title = "—";
            string sub = r.InCountdown ? L.T("partenza in corso…", "starting…") : "";
            if (curIdx >= 0 && curIdx < steps.Count)
            {
                MacroStep s = steps[curIdx];
                title = StepStyle.Title(s);
                int pos, count;
                string where = PositionIn(steps, curIdx, out pos, out count);
                sub = pos + L.T(" di ", " of ") + count + " · " + where;
            }
            statStep.Set(L.T("Step attivo", "Active step"), title, sub, -1);
        }

        // posizione dello step tra gli elementi del blocco che lo contiene (un blocco annidato conta come uno)
        string PositionIn(List<MacroStep> steps, int idx, out int pos, out int count)
        {
            pos = 1; count = 1;
            if (Flow.Prepare(steps) != null) return "";
            int from = 0, to = steps.Count;
            string name = L.T("Sequenza principale", "Main sequence");
            for (int h = idx - 1; h >= 0; h--)
            {
                MacroStep s = steps[h];
                if ((s.IsBlockHeader) && s.Match > idx)
                {
                    from = h + 1; to = s.Match;
                    name = StepStyle.Title(s);
                    break;
                }
            }
            int n = 0, i = from, found = 0;
            while (i < to)
            {
                MacroStep s = steps[i];
                int endIdx = i;
                if ((s.IsBlockHeader) && s.Match > i) endIdx = s.Match;
                else if (s.IsBlockFooter || s.IsBranchMarker) { i++; continue; }
                n++;
                if (idx >= i && idx <= endIdx) found = n;
                i = endIdx + 1;
            }
            pos = Math.Max(1, found);
            count = Math.Max(1, n);
            return name;
        }

        // ---------- selezione area dallo schermo ----------

        Keys CaptureKey()
        {
            Keys stop, pause;
            Enum.TryParse<Keys>(cbHotkey.SelectedText, out stop);
            Enum.TryParse<Keys>(cbPause.SelectedText, out pause);
            foreach (Keys k in new Keys[] { Keys.F9, Keys.F10, Keys.F11, Keys.F12, Keys.F6, Keys.F7, Keys.F8 })
                if (k != stop && k != pause) return k;
            return Keys.F9;
        }

        void BeginAreaCapture(Action<Rectangle, Color> apply)
        {
            if (Busy || (runner != null && runner.IsRunning)) return;
            Keys key = CaptureKey();
            if (!Native.RegisterHotKey(Handle, CaptureHotkeyId, 0, (uint)key))
            {
                Warn(L.T("Il tasto " + key + " è già usato da un altro programma: cambia il tasto Start/Stop e riprova.",
                         "The " + key + " key is already used by another program: change the Start/Stop key and try again."));
                return;
            }
            captureApply = apply;
            toast = new ToastBar(
                L.T("Porta il gioco nello stato giusto (anche il mouse sul pulsante) e premi " + key + " per congelare lo schermo.\r\n"
                    + "Poi trascina per selezionare l'area (un clic = un singolo punto, Esc annulla).",
                    "Bring the game to the right state (mouse on the button too) and press " + key + " to freeze the screen.\r\n"
                    + "Then drag to select the area (a click = a single point, Esc cancels)."),
                L.T("Annulla", "Cancel"));
            toast.ButtonClicked += delegate { EndCapture(); };
            toast.Show();
            WindowState = FormWindowState.Minimized;
        }

        void EndCapture()
        {
            Native.UnregisterHotKey(Handle, CaptureHotkeyId);
            if (toast != null) { toast.Close(); toast = null; }
            captureApply = null;
            WindowState = FormWindowState.Normal;
            Activate();
        }

        void OnCaptureKey()
        {
            if (toast == null) return;
            Native.UnregisterHotKey(Handle, CaptureHotkeyId);
            toast.Close();
            toast = null;
            Action<Rectangle, Color> apply = captureApply;
            captureApply = null;

            Point origin;
            Bitmap shot = ScreenPixel.CaptureVirtualScreen(out origin);
            Rectangle rect;
            Color avg;
            bool ok;
            Bitmap thumb = null;
            try
            {
                ok = AreaPickerForm.Pick(shot, origin, out rect, out avg);
                if (ok)
                {
                    // miniatura dell'area scelta, presa dall'istantanea congelata
                    Rectangle src = new Rectangle(rect.X - origin.X, rect.Y - origin.Y, Math.Min(rect.Width, 480), Math.Min(rect.Height, 270));
                    src.Intersect(new Rectangle(0, 0, shot.Width, shot.Height));
                    if (src.Width > 0 && src.Height > 0) thumb = shot.Clone(src, shot.PixelFormat);
                }
            }
            finally { shot.Dispose(); }

            WindowState = FormWindowState.Normal;
            Activate();
            if (ok && apply != null)
            {
                apply(rect, avg);
                if (thumb != null)
                {
                    areaPreview.Set(thumb, rect.Width + " × " + rect.Height + " @ " + rect.X + ", " + rect.Y);
                    previewRect = rect;
                }
                LogLine(L.T("Selezionato (", "Selected (") + rect.X + ", " + rect.Y + ") " + rect.Width + "x" + rect.Height
                    + L.T("  colore medio #", "  average color #") + ScreenPixel.ToHex(avg));
            }
            else if (thumb != null) thumb.Dispose();
        }

        void PickForEditor()
        {
            if (editIndex < 0) return;
            StepType t = editType;
            BeginAreaCapture(delegate(Rectangle r, Color c)
            {
                rebuilding = true;
                try
                {
                    if (t == StepType.ClickAt || t == StepType.MoveTo)
                    {
                        nudX.Value = r.X + r.Width / 2;
                        nudY.Value = r.Y + r.Height / 2;
                    }
                    else
                    {
                        nudX.Value = r.X;
                        nudY.Value = r.Y;
                        nudW.Value = r.Width;
                        nudH.Value = r.Height;
                        tfColor.Text = ScreenPixel.ToHex(c);
                    }
                }
                finally { rebuilding = false; }
                OnEditorChanged();
            });
        }

        // ---------- registrazione ----------

        void StartRecording()
        {
            if (Busy || (runner != null && runner.IsRunning)) return;
            Keys stopKey;
            Enum.TryParse<Keys>(cbHotkey.SelectedText, out stopKey);
            Keys waitKey = CaptureKey();

            bool wait, pauses, keys;
            using (RecOptionsDialog dlg = new RecOptionsDialog(stopKey.ToString(), waitKey.ToString()))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                wait = dlg.ChkWait.Checked;
                pauses = dlg.ChkPauses.Checked;
                keys = dlg.ChkKeys.Checked;
            }

            recorder = new Recorder();
            recorder.WaitBeforeClick = wait;
            recorder.RecordPauses = pauses;
            recorder.RecordKeys = keys;
            recorder.StopKey = stopKey;
            recorder.WaitKey = waitKey;
            recorder.IgnoreArea = delegate(Point p)
            {
                return recBar != null && recBar.IsHandleCreated && ScreenPixel.WindowRect(recBar.Handle).Contains(p);
            };
            recorder.Changed += RecordingChanged;
            recorder.StopRequested += delegate { Ui_(FinishRecording); };

            recBar = new ToastBar(RecText(0), L.T("Fine registrazione", "Stop recording"));
            recBar.ButtonClicked += FinishRecording;
            recBar.Show();
            if (!recorder.Start())
            {
                recorder = null;
                recBar.Close();
                recBar = null;
                Warn(L.T("Impossibile avviare la registrazione (hook di input non disponibili).",
                         "Unable to start recording (input hooks not available)."));
                return;
            }
            WindowState = FormWindowState.Minimized;
        }

        string RecText(int count)
        {
            return L.T("● REC   " + count + " passi registrati\r\n" + recorder.WaitKey + " = attesa colore sul cursore   -   " + recorder.StopKey + " = fine registrazione",
                       "● REC   " + count + " steps recorded\r\n" + recorder.WaitKey + " = wait for the color under the cursor   -   " + recorder.StopKey + " = stop recording");
        }

        void RecordingChanged()
        {
            if (recBar != null && recorder != null) recBar.SetText(RecText(recorder.Steps.Count));
        }

        void FinishRecording()
        {
            if (recorder == null) return;
            Recorder r = recorder;
            recorder = null;
            r.Dispose();
            if (recBar != null) { recBar.Close(); recBar = null; }
            WindowState = FormWindowState.Normal;
            Activate();

            if (r.Steps.Count == 0)
            {
                LogLine(L.T("Registrazione terminata: nessun passo registrato.", "Recording finished: no steps recorded."));
                return;
            }
            List<MacroStep> steps = ActiveSteps;
            int sel = list.SelectedIndex;
            int at = sel >= 0 ? sel + 1 : steps.Count;
            steps.InsertRange(at, r.Steps);
            list.Reload(at);
            LogLine(L.T("Registrazione terminata: " + r.Steps.Count + " passi aggiunti alla scheda '" + (MenuTab ? "Riavvio partita" : "Sequenza") + "'.",
                        "Recording finished: " + r.Steps.Count + " steps added to the '" + (MenuTab ? "Match restart" : "Sequence") + "' tab."));
        }

        // ---------- hotkey e ciclo di vita ----------

        void RegisterHotkey()
        {
            Keys k;
            if (!Enum.TryParse<Keys>(cbHotkey.SelectedText, out k)) return;
            stopVk = (int)k;
            if (!IsHandleCreated || !hotkeysReady) return;
            Native.UnregisterHotKey(Handle, HotkeyId);
            bool ok = Native.RegisterHotKey(Handle, HotkeyId, 0, (uint)k);
            if (!ok)
                LogLine(L.T("Impossibile registrare " + cbHotkey.SelectedText + " (già usato da un altro programma?). Scegli un altro tasto.",
                            "Unable to register " + cbHotkey.SelectedText + " (already used by another program?). Choose another key."));
            if (isRunning) RegisterPauseHotkey(true);
        }

        // Il tasto Pausa è registrato solo mentre la macro gira, così non toglie il tasto al gioco quando sei fermo.
        void RegisterPauseHotkey(bool on)
        {
            if (!IsHandleCreated || !hotkeysReady) return;
            Native.UnregisterHotKey(Handle, PauseHotkeyId);
            if (!on) return;
            Keys k;
            if (!Enum.TryParse<Keys>(cbPause.SelectedText, out k)) return;
            if (!Native.RegisterHotKey(Handle, PauseHotkeyId, 0, (uint)k))
                LogLine(L.T("Impossibile registrare il tasto Pausa " + cbPause.SelectedText + " (già usato da un altro programma?).",
                            "Unable to register the Pause key " + cbPause.SelectedText + " (already used by another program?)."));
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HotkeyId)
                {
                    // Se il watchdog ha appena fermato la macro, questo stesso tasto non deve riavviarla.
                    if (Environment.TickCount - stopStamp < 700) return;
                    Toggle();
                    return;
                }
                if (id == PauseHotkeyId) { TogglePause(); return; }
                if (id == CaptureHotkeyId) { OnCaptureKey(); return; }
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing = true;
            if (uiTimer != null) uiTimer.Stop();
            if (recorder != null) { recorder.Dispose(); recorder = null; }
            if (runner != null) { runner.Stop(); runner.Wait(1500); }
            if (IsHandleCreated)
            {
                Native.UnregisterHotKey(Handle, HotkeyId);
                Native.UnregisterHotKey(Handle, CaptureHotkeyId);
                Native.UnregisterHotKey(Handle, PauseHotkeyId);
            }
            try { CollectProfile().Save(Path.Combine(DataDir, "last.json")); }
            catch (Exception) { }
            base.OnFormClosing(e);
        }
    }
}
