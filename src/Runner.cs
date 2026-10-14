using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace StepLoop
{
    // Tipo di riga del log (usato dall'interfaccia per icona, colore e filtri).
    public enum LogKind { Info, Action, Loop, Color, Warn, Error }

    public enum StepRunState : byte { Pending = 0, Active = 1, Done = 2 }

    // Stato "live" di un blocco Ripeti, letto dall'interfaccia mentre la macro gira.
    public struct LoopInfo
    {
        public bool Started;
        public bool Finished;
        public int Iteration;        // giro corrente (1-based); a ciclo finito = giri svolti
        public int MaxIterations;    // 0 = nessun limite
        public int MaxTimeMs;        // 0 = nessun limite
        public long ElapsedMs;       // tempo attivo dall'inizio del ciclo (a ciclo finito: durata totale)
        public long IterElapsedMs;   // tempo del giro in corso
        public long LastIterMs;      // durata dell'ultimo giro completo
        public string ExitReason;    // a ciclo finito: perché è uscito
    }

    // Ultimo controllo di colore eseguito (Se, uscita di un Ripeti, Attendi).
    public struct ColorCheck
    {
        public bool Valid;
        public int List;
        public int Index;
        public int X, Y, W, H;
        public Color Expected;
        public Color Read;
        public int Delta;
        public int Tolerance;
        public bool Match;
        public int Sequence;         // aumenta a ogni controllo: l'interfaccia vede quando ne arriva uno nuovo
    }

    public class MacroRunner
    {
        // Pausa minima tra due giri di un ciclo: impedisce che un ciclo "vuoto" saturi CPU e interfaccia.
        const int MinLoopGapMs = 10;

        readonly Profile profile;
        readonly bool dryRun;
        readonly Action<string> log;
        readonly ManualResetEvent stopEvt = new ManualResetEvent(false);
        readonly ManualResetEvent pauseEvt = new ManualResetEvent(false);    // acceso mentre la macro è in pausa
        readonly ManualResetEvent resumeEvt = new ManualResetEvent(true);    // acceso mentre la macro gira
        readonly WaitHandle[] sleepHandles;
        readonly WaitHandle[] resumeHandles;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly object pauseLock = new object();
        readonly Random rng = new Random();
        readonly List<Keys> heldKeys = new List<Keys>();
        readonly List<string> heldButtons = new List<string>();
        readonly Dictionary<string, List<List<Keys>>> poolCache = new Dictionary<string, List<List<Keys>>>();
        long pausedTotal;
        long pauseStartedAt;
        volatile bool paused;
        Thread thread;
        bool focusWarned;
        bool breakFlag;
        bool phaseEnded;
        int gotoTarget = -1;
        Func<bool> phaseEndCheck;

        // telemetria letta dall'interfaccia
        readonly object statLock = new object();
        StepRunState[][] stState = new StepRunState[2][];
        LoopInfo[][] loops = new LoopInfo[2][];
        long[][] loopStart = new long[2][];
        long[][] loopIterStart = new long[2][];
        sbyte[][] condRes = new sbyte[2][];
        int curList = -1, curIndex = -1, curPlanned;
        long curStart;
        string curLabel;
        int repetition;
        long runStart = -1;
        long cycleStart;
        long lastCycleMs;
        int colorSeq;
        ColorCheck lastColor;
        volatile bool counting;

        public event Action Finished;
        public event Action<int, int> StepStarted;
        public event Action PauseChanged;

        // Se impostato, riceve le righe di log con il loro tipo (altrimenti si usa il callback di testo).
        public Action<LogKind, string> TypedLog;

        public MacroRunner(Profile profile, bool dryRun, Action<string> log, Action<int, int> onStep)
        {
            this.profile = profile.Clone();
            this.dryRun = dryRun;
            this.log = log;
            sleepHandles = new WaitHandle[] { stopEvt, pauseEvt };
            resumeHandles = new WaitHandle[] { stopEvt, resumeEvt };
            if (onStep != null) StepStarted += onStep;
            InitStats();
        }

        public bool IsRunning { get { Thread t = thread; return t != null && t.IsAlive; } }

        public bool IsPaused { get { return paused; } }

        public void Start()
        {
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Start();
        }

        public void Stop() { stopEvt.Set(); }

        // Mette in pausa dove si trova: tasti e pulsanti tenuti premuti vengono rilasciati e ripremuti alla ripresa.
        public bool Pause()
        {
            lock (pauseLock)
            {
                if (paused || Stopped || !IsRunning) return false;
                paused = true;
                pauseStartedAt = clock.ElapsedMilliseconds;
                resumeEvt.Reset();
                pauseEvt.Set();
            }
            Action a = PauseChanged;
            if (a != null) a();
            return true;
        }

        public bool Resume()
        {
            lock (pauseLock)
            {
                if (!paused) return false;
                pausedTotal += clock.ElapsedMilliseconds - pauseStartedAt;
                paused = false;
                pauseEvt.Reset();
                resumeEvt.Set();
            }
            Action a = PauseChanged;
            if (a != null) a();
            return true;
        }

        public void TogglePause()
        {
            if (paused) Resume(); else Pause();
        }

        public bool Wait(int ms)
        {
            Thread t = thread;
            return t == null || t.Join(ms);
        }

        bool Stopped { get { return stopEvt.WaitOne(0); } }

        // Tempo "attivo" in ms: si ferma mentre la macro è in pausa, così timeout e durate non vengono consumati.
        long Now
        {
            get
            {
                lock (pauseLock)
                {
                    long c = paused ? pauseStartedAt : clock.ElapsedMilliseconds;
                    return c - pausedTotal;
                }
            }
        }

        // ---------- telemetria (lettura dall'interfaccia) ----------

        void InitStats()
        {
            int[] counts = new int[] { profile.Steps.Count, profile.MenuSteps.Count };
            for (int l = 0; l < 2; l++)
            {
                stState[l] = new StepRunState[counts[l]];
                loops[l] = new LoopInfo[counts[l]];
                loopStart[l] = new long[counts[l]];
                loopIterStart[l] = new long[counts[l]];
                condRes[l] = new sbyte[counts[l]];
                for (int i = 0; i < counts[l]; i++) condRes[l][i] = -1;
            }
        }

        // Ripetizione corrente (1-based): passata della sequenza, o partita con il riavvio automatico.
        public int Repetition { get { lock (statLock) { return repetition; } } }

        public int RepetitionTarget { get { return profile.Cycles; } }

        public bool InCountdown { get { return counting; } }

        // Tempo trascorso dall'avvio effettivo (dopo il ritardo iniziale), senza le pause.
        public long ElapsedMs
        {
            get
            {
                long rs;
                lock (statLock) { rs = runStart; }
                return rs < 0 ? 0 : Math.Max(0, Now - rs);
            }
        }

        // Durata dell'ultima passata completa della sequenza (0 se non ancora completata).
        public long LastCycleMs { get { lock (statLock) { return lastCycleMs; } } }

        public long CycleElapsedMs
        {
            get
            {
                long cs;
                lock (statLock) { cs = cycleStart; }
                return Math.Max(0, Now - cs);
            }
        }

        public void GetCurrent(out int list, out int index, out long elapsedMs, out int plannedMs, out string label)
        {
            long start;
            lock (statLock)
            {
                list = curList; index = curIndex; plannedMs = curPlanned; label = curLabel; start = curStart;
            }
            elapsedMs = index < 0 ? 0 : Math.Max(0, Now - start);
        }

        public StepRunState GetState(int list, int idx)
        {
            lock (statLock)
            {
                if (list < 0 || list > 1 || idx < 0 || idx >= stState[list].Length) return StepRunState.Pending;
                return stState[list][idx];
            }
        }

        public LoopInfo GetLoop(int list, int idx)
        {
            long now = Now;
            lock (statLock)
            {
                if (list < 0 || list > 1 || idx < 0 || idx >= loops[list].Length) return new LoopInfo();
                LoopInfo li = loops[list][idx];
                if (li.Started && !li.Finished)
                {
                    li.ElapsedMs = Math.Max(0, now - loopStart[list][idx]);
                    li.IterElapsedMs = Math.Max(0, now - loopIterStart[list][idx]);
                }
                return li;
            }
        }

        // -1 = non ancora valutato, 0 = falso, 1 = vero
        public int GetCond(int list, int idx)
        {
            lock (statLock)
            {
                if (list < 0 || list > 1 || idx < 0 || idx >= condRes[list].Length) return -1;
                return condRes[list][idx];
            }
        }

        public ColorCheck LastColor { get { lock (statLock) { return lastColor; } } }

        void ResetList(int list)
        {
            lock (statLock)
            {
                for (int i = 0; i < stState[list].Length; i++)
                {
                    stState[list][i] = StepRunState.Pending;
                    loops[list][i] = new LoopInfo();
                    condRes[list][i] = -1;
                }
            }
        }

        void ResetRange(int list, int from, int to)
        {
            lock (statLock)
            {
                for (int i = Math.Max(0, from); i < to && i < stState[list].Length; i++)
                {
                    stState[list][i] = StepRunState.Pending;
                    loops[list][i] = new LoopInfo();
                    condRes[list][i] = -1;
                }
            }
        }

        void SetState(int list, int idx, StepRunState st)
        {
            lock (statLock)
            {
                if (list >= 0 && list < 2 && idx >= 0 && idx < stState[list].Length) stState[list][idx] = st;
            }
        }

        void BeginStep(int list, int idx, int plannedMs, string label)
        {
            long now = Now;
            lock (statLock)
            {
                curList = list; curIndex = idx; curStart = now; curPlanned = plannedMs; curLabel = label;
                if (list >= 0 && list < 2 && idx >= 0 && idx < stState[list].Length) stState[list][idx] = StepRunState.Active;
            }
        }

        void SetCond(int list, int idx, bool value)
        {
            SetCondValue(list, idx, value ? 1 : 0);
        }

        // Per il blocco "Scegli a caso" memorizza l'indice dell'alternativa scelta (0, 1, 2...).
        void SetCondValue(int list, int idx, int value)
        {
            lock (statLock)
            {
                if (list >= 0 && list < 2 && idx >= 0 && idx < condRes[list].Length) condRes[list][idx] = (sbyte)Math.Min(127, Math.Max(0, value));
            }
        }

        // ---------- variabili ----------

        readonly Dictionary<string, int> vars = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int GetVar(string name)
        {
            lock (statLock)
            {
                int v;
                return vars.TryGetValue(name ?? "", out v) ? v : 0;
            }
        }

        void SetVarValue(string name, int value)
        {
            lock (statLock) { vars[name] = value; }
        }

        // Copia dei valori correnti (per il pannello live).
        public Dictionary<string, int> Variables
        {
            get
            {
                lock (statLock) { return new Dictionary<string, int>(vars, StringComparer.OrdinalIgnoreCase); }
            }
        }

        void LoopBegin(int list, int idx, int maxIter, int maxMs)
        {
            long now = Now;
            lock (statLock)
            {
                if (list < 0 || list > 1 || idx < 0 || idx >= loops[list].Length) return;
                LoopInfo li = new LoopInfo();
                li.Started = true; li.MaxIterations = maxIter; li.MaxTimeMs = maxMs;
                loops[list][idx] = li;
                loopStart[list][idx] = now;
                loopIterStart[list][idx] = now;
            }
        }

        void LoopIter(int list, int idx, int end, int iteration)
        {
            long now = Now;
            lock (statLock)
            {
                if (list < 0 || list > 1 || idx < 0 || idx >= loops[list].Length) return;
                LoopInfo li = loops[list][idx];
                if (iteration > 1) li.LastIterMs = Math.Max(0, now - loopIterStart[list][idx]);
                li.Iteration = iteration;
                loops[list][idx] = li;
                loopIterStart[list][idx] = now;
                // il corpo riparte da capo: i suoi passi tornano "da fare"
                for (int i = idx + 1; i < end && i < stState[list].Length; i++)
                {
                    stState[list][i] = StepRunState.Pending;
                    loops[list][i] = new LoopInfo();
                    condRes[list][i] = -1;
                }
            }
        }

        void LoopFinish(int list, int idx, string reason)
        {
            long now = Now;
            lock (statLock)
            {
                if (list < 0 || list > 1 || idx < 0 || idx >= loops[list].Length) return;
                LoopInfo li = loops[list][idx];
                if (!li.Started) return;
                li.Finished = true;
                li.ElapsedMs = Math.Max(0, now - loopStart[list][idx]);
                li.IterElapsedMs = Math.Max(0, now - loopIterStart[list][idx]);
                li.ExitReason = reason;
                loops[list][idx] = li;
                stState[list][idx] = StepRunState.Done;
            }
        }

        void NoteColor(int list, int idx, MacroStep s, Color expected, Color read, int delta, bool match)
        {
            lock (statLock)
            {
                colorSeq++;
                lastColor.Valid = true;
                lastColor.List = list; lastColor.Index = idx;
                lastColor.X = s.MoveX; lastColor.Y = s.MoveY; lastColor.W = s.Width; lastColor.H = s.Height;
                lastColor.Expected = expected; lastColor.Read = read; lastColor.Delta = delta;
                lastColor.Tolerance = s.Tolerance; lastColor.Match = match; lastColor.Sequence = colorSeq;
            }
        }

        // ---------- log ----------

        void Log(string text) { Log(LogKind.Info, text); }

        void Log(LogKind kind, string text)
        {
            Action<LogKind, string> t = TypedLog;
            if (t != null) { t(kind, text); return; }
            if (log != null) log(text);
        }

        static string Hex(Color c) { return c.IsEmpty ? "?" : "#" + ScreenPixel.ToHex(c); }

        void LogColor(MacroStep s, Color expected, Color read, int delta, bool match)
        {
            Log(LogKind.Color, Tag(s) + Hex(expected) + " @ (" + s.MoveX + ", " + s.MoveY + ") " + L.T("→ letto ", "→ read ") + Hex(read)
                + " · Δ " + delta + " · " + (match ? L.T("sì", "yes") : L.T("no", "no")));
        }

        // Attesa interrompibile da Stop e sospesa dalla pausa (il tempo restante si conserva).
        void Sleep(int ms)
        {
            if (ms <= 0) return;
            long end = Now + ms;
            while (!Stopped)
            {
                if (paused) { WaitWhilePaused(); continue; }
                long left = end - Now;
                if (left <= 0) return;
                int idx = WaitHandle.WaitAny(sleepHandles, (int)Math.Min(left, 1000000000L));
                if (idx == 0) return;
            }
        }

        void WaitWhilePaused()
        {
            List<Keys> keys = new List<Keys>(heldKeys);
            List<string> buttons = new List<string>(heldButtons);
            if (!dryRun)
            {
                foreach (Keys k in keys) InputSender.Key(k, false);
                foreach (string b in buttons) InputSender.Button(b, false);
            }
            Log(LogKind.Info, L.T("In pausa: rilascio i tasti e aspetto.", "Paused: releasing the keys and waiting."));
            WaitHandle.WaitAny(resumeHandles);
            if (Stopped) return;

            // Se si riprende dal pulsante dell'app, il gioco non è in primo piano: i tasti si ripremono solo quando torna attivo.
            if (!dryRun && (keys.Count > 0 || buttons.Count > 0))
            {
                bool warned = false;
                while (!Stopped && !paused && !GameFocused())
                {
                    if (!warned)
                    {
                        Log(LogKind.Warn, L.T("Riprendo appena la finestra '" + profile.WindowTitle + "' torna in primo piano.",
                                "Resuming as soon as the '" + profile.WindowTitle + "' window is in the foreground again."));
                        warned = true;
                    }
                    stopEvt.WaitOne(150);
                }
                if (Stopped || paused) return;
                foreach (Keys k in keys) InputSender.Key(k, true);
                foreach (string b in buttons) InputSender.Button(b, true);
            }
            Log(LogKind.Info, L.T("Ripreso.", "Resumed."));
        }

        static string Tag(MacroStep s)
        {
            return string.IsNullOrEmpty(s.Name) ? "" : "[" + s.Name + "] ";
        }

        void Report(int listId, int index)
        {
            long now = Now;
            lock (statLock)
            {
                curList = listId; curIndex = index; curStart = now; curPlanned = 0; curLabel = null;
            }
            Action<int, int> h = StepStarted;
            if (h != null) h(listId, index);
        }

        void Loop()
        {
            Native.timeBeginPeriod(1);
            try
            {
                string err = Flow.Prepare(profile.Steps);
                if (err != null) { Log(LogKind.Error, L.T("Scheda Sequenza: ", "Sequence tab: ") + err); return; }
                err = Flow.Prepare(profile.MenuSteps);
                if (err != null) { Log(LogKind.Error, L.T("Scheda Riavvio partita: ", "Match restart tab: ") + err); return; }

                counting = true;
                for (int s = profile.StartDelaySec; s > 0 && !Stopped; s--)
                {
                    Log(LogKind.Info, L.T("Parto tra " + s + " s...", "Starting in " + s + " s..."));
                    Sleep(1000);
                }
                counting = false;
                lock (statLock) { runStart = Now; cycleStart = runStart; }
                if (!Stopped)
                {
                    string mode = profile.Mode == RunMode.Sync ? L.T("sincrona", "synchronous") : "random";
                    Log(LogKind.Info, (dryRun ? L.T("[simulazione] ", "[simulation] ") : "")
                        + L.T("Macro avviata - modalità " + mode + (profile.AutoRestart ? " con riavvio automatico delle partite" : ""),
                              "Macro started - " + mode + " mode" + (profile.AutoRestart ? " with automatic match restart" : "")));
                }

                if (profile.AutoRestart) RunAutoRestart();
                else RunPlain();
            }
            catch (Exception ex)
            {
                Log(LogKind.Error, L.T("Errore: ", "Error: ") + ex.Message);
            }
            finally
            {
                counting = false;
                ReleaseAll();
                Native.timeEndPeriod(1);
                lock (pauseLock)
                {
                    paused = false;
                    pauseEvt.Reset();
                    resumeEvt.Set();
                }
                Log(LogKind.Info, L.T("Macro ferma.", "Macro stopped."));
                Action f = Finished;
                if (f != null) f();
            }
        }

        // Un Goto che esce dall'elemento scelto (modalità Random / partita a tempo): riprende dall'etichetta fino a fine lista.
        void DrainGoto(List<MacroStep> steps, int listId, bool random)
        {
            int guard = 0;
            while (gotoTarget >= 0 && !Stopped && !phaseEnded && guard++ < 100000)
            {
                int t = gotoTarget;
                gotoTarget = -1;
                RunBlock(steps, listId, t, steps.Count, random);
            }
            gotoTarget = -1;
        }

        void RunPlain()
        {
            List<MacroStep> steps = profile.Steps;
            List<int[]> items = Flow.TopLevelItems(steps);
            int cycles = 0;
            while (!Stopped && items.Count > 0 && (profile.Cycles <= 0 || cycles < profile.Cycles))
            {
                lock (statLock) { repetition = cycles + 1; cycleStart = Now; }
                ResetList(0);
                if (profile.Mode == RunMode.Sync)
                {
                    RunBlock(steps, 0, 0, steps.Count, false);
                    DrainGoto(steps, 0, false);
                }
                else
                {
                    for (int i = 0; i < items.Count && !Stopped; i++)
                    {
                        int[] it = PickItem(steps, items);
                        RunBlock(steps, 0, it[0], it[1] + 1, true);
                        DrainGoto(steps, 0, true);
                        breakFlag = false;
                    }
                }
                breakFlag = false;
                cycles++;
                lock (statLock) { lastCycleMs = Math.Max(0, Now - cycleStart); }
                Sleep(MinLoopGapMs);
            }
        }

        void RunAutoRestart()
        {
            if (profile.GameSeconds <= 0 && !profile.EndPixelEnabled)
            {
                Log(LogKind.Error, L.T("Riavvio automatico: imposta la durata della partita o il colore di fine partita.",
                        "Automatic restart: set the match duration or the end-of-match color."));
                return;
            }
            Color endColor = Color.Empty;
            if (profile.EndPixelEnabled && !ScreenPixel.TryParseHex(profile.EndColor, out endColor))
            {
                Log(LogKind.Error, L.T("Colore di fine partita non valido.", "Invalid end-of-match color."));
                return;
            }

            int round = 0;
            while (!Stopped && (profile.Cycles <= 0 || round < profile.Cycles))
            {
                lock (statLock) { repetition = round + 1; }
                ResetList(0);
                ResetList(1);
                if (round > 0 || profile.StartWithMenu)
                {
                    Log(LogKind.Info, L.T("Riavvio partita: eseguo i passi del menu", "Restarting the match: running the menu steps"));
                    RunBlock(profile.MenuSteps, 1, 0, profile.MenuSteps.Count, false);
                    gotoTarget = -1;
                    breakFlag = false;
                    if (Stopped) break;
                }
                Log(LogKind.Info, L.T("Partita " + (round + 1) + (profile.Cycles > 0 ? "/" + profile.Cycles : "") + " in corso",
                        "Match " + (round + 1) + (profile.Cycles > 0 ? "/" + profile.Cycles : "") + " in progress"));
                PlayUntilEnd(endColor);
                round++;
            }
        }

        void PlayUntilEnd(Color endColor)
        {
            long t0 = Now;
            List<MacroStep> steps = profile.Steps;
            List<int[]> items = Flow.TopLevelItems(steps);
            phaseEnded = false;
            long lastCheck = t0 - 1000;
            lock (statLock) { cycleStart = t0; }
            phaseEndCheck = delegate
            {
                // Il controllo dell'area costa: al massimo 4 volte al secondo, anche con cicli velocissimi.
                long now = Now;
                if (now - lastCheck < 250) return false;
                lastCheck = now;
                if (profile.GameSeconds > 0 && now - t0 >= profile.GameSeconds * 1000L)
                {
                    Log(LogKind.Info, L.T("Fine partita: tempo scaduto", "End of match: time is up"));
                    return true;
                }
                if (profile.EndPixelEnabled)
                {
                    Color read;
                    int delta;
                    bool hit = ScreenPixel.Check(profile.EndX, profile.EndY, profile.EndW, profile.EndH, endColor, profile.EndTolerance, out read, out delta);
                    if (hit)
                    {
                        Log(LogKind.Info, L.T("Fine partita: colore di fine rilevato", "End of match: end color detected"));
                        return true;
                    }
                }
                return false;
            };
            try
            {
                int idx = 0;
                while (!Stopped && !phaseEnded)
                {
                    if (paused) WaitWhilePaused();
                    if (Stopped) break;
                    if (phaseEndCheck()) { phaseEnded = true; break; }
                    if (items.Count == 0) { Sleep(250); continue; }
                    int[] it;
                    if (profile.Mode == RunMode.Sync)
                    {
                        if (idx == 0)
                        {
                            lock (statLock) { cycleStart = Now; }
                            ResetList(0);
                        }
                        it = items[idx];
                        idx = (idx + 1) % items.Count;
                    }
                    else it = PickItem(steps, items);
                    RunBlock(steps, 0, it[0], it[1] + 1, profile.Mode == RunMode.Random);
                    DrainGoto(steps, 0, profile.Mode == RunMode.Random);
                    breakFlag = false;
                    if (profile.Mode == RunMode.Sync && idx == 0)
                        lock (statLock) { lastCycleMs = Math.Max(0, Now - cycleStart); }
                    Sleep(1);
                }
            }
            finally
            {
                phaseEndCheck = null;
                phaseEnded = false;
                gotoTarget = -1;
            }
        }

        int[] PickItem(List<MacroStep> steps, List<int[]> items)
        {
            int total = 0;
            foreach (int[] it in items) total += Math.Max(1, steps[it[0]].Weight);
            int r = rng.Next(total);
            foreach (int[] it in items)
            {
                r -= Math.Max(1, steps[it[0]].Weight);
                if (r < 0) return it;
            }
            return items[items.Count - 1];
        }

        bool ExitConditionMet(int listId, int pc, MacroStep s, Color color)
        {
            Color read;
            int delta;
            bool present = ScreenPixel.Check(s.MoveX, s.MoveY, s.Width, s.Height, color, s.Tolerance, out read, out delta);
            bool result = s.Negate ? !present : present;
            NoteColor(listId, pc, s, color, read, delta, present);
            LogColor(s, color, read, delta, present);
            return result;
        }

        void RunBlock(List<MacroStep> steps, int listId, int from, int to, bool random)
        {
            int pc = from;
            while (!Stopped && !phaseEnded && !breakFlag)
            {
                // Goto in arrivo: se l'etichetta è in questo tratto si riparte da lì, altrimenti si risale al tratto che la contiene.
                if (gotoTarget >= 0)
                {
                    if (gotoTarget >= from && gotoTarget < to) { pc = gotoTarget; gotoTarget = -1; }
                    else return;
                }
                if (pc >= to) break;
                if (paused) { WaitWhilePaused(); continue; }
                MacroStep s = steps[pc];
                switch (s.Type)
                {
                    case StepType.LoopStart:
                        {
                            int end = s.Match;
                            int max = Math.Max(0, s.Count);
                            Report(listId, pc);
                            SetState(listId, pc, StepRunState.Active);
                            LoopBegin(listId, pc, max, s.DurationMs);
                            Log(LogKind.Loop, Tag(s) + StepStyle.LoopLog(s));

                            Color untilColor = Color.Empty;
                            bool useUntil = s.UseUntil && ScreenPixel.TryParseHex(s.Input, out untilColor);
                            if (s.UseUntil && !useUntil)
                                Log(LogKind.Warn, Tag(s) + L.T("Colore di uscita non valido: il ciclo non ha una condizione di uscita.",
                                                 "Invalid exit color: the loop has no exit condition."));
                            long t0 = Now;
                            string reason = "";

                            bool skip = false;
                            if (useUntil && s.CheckFirst && ExitConditionMet(listId, pc, s, untilColor))
                            {
                                Log(LogKind.Loop, Tag(s) + L.T("Condizione di uscita già soddisfatta: salto il ciclo", "Exit condition already met: skipping the loop"));
                                reason = L.T("saltato", "skipped");
                                skip = true;
                            }

                            for (int i = 0; !skip && !Stopped && !phaseEnded; i++)
                            {
                                if (max > 0 && i >= max)
                                {
                                    Log(LogKind.Loop, Tag(s) + L.T("Raggiunti " + max + " giri: esco", "Reached " + max + " rounds: leaving"));
                                    reason = L.T("max giri", "max rounds");
                                    break;
                                }
                                LoopIter(listId, pc, end, i + 1);
                                Log(LogKind.Loop, Tag(s) + L.T("GIRO ", "ROUND ") + (i + 1) + (max > 0 ? "/" + max : ""));
                                RunBlock(steps, listId, pc + 1, end, random);
                                if (gotoTarget >= 0) { reason = "goto"; break; }
                                if (breakFlag) { breakFlag = false; reason = L.T("esci", "exit"); break; }
                                if (Stopped || phaseEnded) break;
                                if (useUntil && ExitConditionMet(listId, pc, s, untilColor))
                                {
                                    Log(LogKind.Loop, Tag(s) + L.T("Condizione di uscita soddisfatta dopo " + (i + 1) + " giri: esco",
                                                     "Exit condition met after " + (i + 1) + " rounds: leaving"));
                                    reason = L.T("colore", "color");
                                    break;
                                }
                                if (s.DurationMs > 0 && Now - t0 >= s.DurationMs)
                                {
                                    Log(LogKind.Loop, Tag(s) + L.T("Tempo massimo (" + StepStyle.Fmt(s.DurationMs) + ") raggiunto: esco",
                                                     "Maximum time (" + StepStyle.Fmt(s.DurationMs) + ") reached: leaving"));
                                    reason = L.T("tempo max", "max time");
                                    break;
                                }
                                Sleep(MinLoopGapMs);
                            }
                            LoopFinish(listId, pc, reason);
                            pc = end + 1;
                            break;
                        }
                    case StepType.IfStart:
                        {
                            Report(listId, pc);
                            SetState(listId, pc, StepRunState.Active);
                            bool cond = EvalCondition(listId, pc, s);
                            SetCond(listId, pc, cond);
                            int elseIdx = s.ElseMatch;
                            int end = s.Match;
                            if (cond) RunBlock(steps, listId, pc + 1, elseIdx >= 0 ? elseIdx : end, random);
                            else if (elseIdx >= 0) RunBlock(steps, listId, elseIdx + 1, end, random);
                            SetState(listId, pc, StepRunState.Done);
                            pc = end + 1;
                            break;
                        }
                    case StepType.ChoiceStart:
                        {
                            Report(listId, pc);
                            SetState(listId, pc, StepRunState.Active);
                            int end = s.Match;
                            List<int[]> alts = Flow.ChoiceRanges(steps, pc);
                            if (alts.Count > 0)
                            {
                                int total = 0;
                                foreach (int[] a in alts) total += Math.Max(1, a[2]);
                                int r = rng.Next(total), pick = alts.Count - 1;
                                for (int k = 0; k < alts.Count; k++)
                                {
                                    r -= Math.Max(1, alts[k][2]);
                                    if (r < 0) { pick = k; break; }
                                }
                                SetCondValue(listId, pc, pick);
                                Log(LogKind.Action, Tag(s) + L.T("Scelta a caso: alternativa ", "Random pick: alternative ") + (pick + 1) + "/" + alts.Count);
                                RunBlock(steps, listId, alts[pick][0], alts[pick][1], random);
                            }
                            SetState(listId, pc, StepRunState.Done);
                            pc = end + 1;
                            break;
                        }
                    case StepType.Option:
                    case StepType.ChoiceEnd:
                        pc++;
                        break;
                    case StepType.SetVar:
                        {
                            Report(listId, pc);
                            SetState(listId, pc, StepRunState.Active);
                            string vn = (s.VarName ?? "").Trim();
                            if (vn.Length == 0)
                            {
                                Log(LogKind.Warn, Tag(s) + L.T("Variabile senza nome: ignorata", "Variable without a name: ignored"));
                            }
                            else
                            {
                                int cur = GetVar(vn), nv;
                                switch (s.VarOp)
                                {
                                    case 1: nv = cur + s.CmpValue; break;
                                    case 2: nv = cur - s.CmpValue; break;
                                    case 3:
                                        {
                                            int lo = Math.Min(s.CmpValue, s.VarValue2), hi = Math.Max(s.CmpValue, s.VarValue2);
                                            nv = lo == hi ? lo : rng.Next(lo, hi + 1);
                                            break;
                                        }
                                    default: nv = s.CmpValue; break;
                                }
                                SetVarValue(vn, nv);
                                Log(LogKind.Action, Tag(s) + vn + " = " + nv + (cur != nv ? "  (" + cur + " → " + nv + ")" : ""));
                            }
                            SetState(listId, pc, StepRunState.Done);
                            pc++;
                            break;
                        }
                    case StepType.Label:
                        pc++;
                        break;
                    case StepType.Goto:
                        {
                            Report(listId, pc);
                            SetState(listId, pc, StepRunState.Active);
                            if (s.Match < 0)
                            {
                                Log(LogKind.Warn, Tag(s) + L.T("Goto senza etichetta valida: ignorato", "Goto without a valid label: ignored"));
                                pc++;
                                break;
                            }
                            Log(LogKind.Action, Tag(s) + L.T("Salto all'etichetta '", "Jumping to label '") + (steps[s.Match].Name ?? "") + "'");
                            SetState(listId, pc, StepRunState.Done);
                            gotoTarget = s.Match;
                            // un salto all'indietro nello stesso tratto non deve saturare la CPU
                            Sleep(MinLoopGapMs);
                            break;
                        }
                    case StepType.Break:
                        Report(listId, pc);
                        Log(LogKind.Action, Tag(s) + L.T("Esco dal ciclo", "Leaving the loop"));
                        breakFlag = true;
                        return;
                    case StepType.StopMacro:
                        Report(listId, pc);
                        Log(LogKind.Action, Tag(s) + L.T("Fermo la macro", "Stopping the macro"));
                        stopEvt.Set();
                        return;
                    case StepType.LoopEnd:
                    case StepType.Else:
                    case StepType.IfEnd:
                        pc++;
                        break;
                    default:
                        if (phaseEndCheck != null && phaseEndCheck()) { phaseEnded = true; return; }
                        Report(listId, pc);
                        Execute(s, random, listId, pc);
                        SetState(listId, pc, StepRunState.Done);
                        pc++;
                        break;
                }
            }
        }

        static bool Compare(int a, int op, int b)
        {
            switch (op)
            {
                case 1: return a != b;
                case 2: return a < b;
                case 3: return a <= b;
                case 4: return a > b;
                case 5: return a >= b;
                default: return a == b;
            }
        }

        bool EvalCondition(int listId, int pc, MacroStep s)
        {
            if (s.CondKind == 1)
            {
                string vn = (s.VarName ?? "").Trim();
                int v = GetVar(vn);
                bool hit = Compare(v, s.CmpOp, s.CmpValue);
                bool res = s.Negate ? !hit : hit;
                Log(LogKind.Action, Tag(s) + L.T("Se ", "If ") + vn + " (" + v + ") " + StepStyle.CmpSymbol(s.CmpOp) + " " + s.CmpValue
                    + ": " + (res ? L.T("vero", "true") : L.T("falso", "false")));
                return res;
            }
            if (s.CondKind == 2)
            {
                int roll = rng.Next(100);
                bool hit = roll < s.ChancePct;
                bool res = s.Negate ? !hit : hit;
                Log(LogKind.Action, Tag(s) + L.T("Se a caso ", "If random ") + s.ChancePct + "%: " + L.T("estratto ", "rolled ") + roll
                    + " → " + (res ? L.T("vero", "true") : L.T("falso", "false")));
                return res;
            }
            Color target;
            if (!ScreenPixel.TryParseHex(s.Input, out target))
            {
                Log(LogKind.Warn, Tag(s) + L.T("Colore della condizione non valido: '", "Invalid condition color: '") + s.Input + "'");
                return false;
            }
            Color read;
            int delta;
            bool present = ScreenPixel.Check(s.MoveX, s.MoveY, s.Width, s.Height, target, s.Tolerance, out read, out delta);
            bool result = s.Negate ? !present : present;
            NoteColor(listId, pc, s, target, read, delta, present);
            Log(LogKind.Color, Tag(s) + L.T("Se il colore ", "If the color ") + Hex(target)
                + (s.Negate ? L.T(" NON è presente", " is NOT present") : L.T(" è presente", " is present"))
                + " @ (" + s.MoveX + ", " + s.MoveY + ") " + L.T("→ letto ", "→ read ") + Hex(read) + " · Δ " + delta
                + ": " + (result ? L.T("vero", "true") : L.T("falso", "false")));
            return result;
        }

        int Jitter(int value, int jitter, int min)
        {
            if (jitter <= 0) return Math.Max(min, value);
            return Math.Max(min, value + rng.Next(-jitter, jitter + 1));
        }

        int RandSym(int amplitude)
        {
            int a = Math.Abs(amplitude);
            return a == 0 ? 0 : rng.Next(-a, a + 1);
        }

        bool GameFocused()
        {
            string need = profile.WindowTitle;
            if (dryRun || string.IsNullOrEmpty(need)) return true;
            StringBuilder sb = new StringBuilder(256);
            Native.GetWindowText(Native.GetForegroundWindow(), sb, 256);
            return sb.ToString().IndexOf(need, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void WaitForGameWindow()
        {
            if (GameFocused()) { focusWarned = false; return; }
            if (!focusWarned)
            {
                Log(LogKind.Warn, L.T("Finestra '" + profile.WindowTitle + "' non attiva: input sospesi finché non torna in primo piano.",
                        "Window '" + profile.WindowTitle + "' is not active: input suspended until it is back in the foreground."));
                focusWarned = true;
            }
            while (!Stopped && !GameFocused()) Sleep(500);
            if (!Stopped && focusWarned)
            {
                Log(LogKind.Info, L.T("Finestra attiva di nuovo: riprendo.", "Window active again: resuming."));
                focusWarned = false;
            }
        }

        List<List<Keys>> GetPool(string text)
        {
            List<List<Keys>> pool;
            if (poolCache.TryGetValue(text ?? "", out pool)) return pool;
            string bad;
            if (!KeyPool.TryParse(text, out pool, out bad)) pool = null;
            poolCache[text ?? ""] = pool;
            return pool;
        }

        static string LongTag(bool isLong)
        {
            return isLong ? L.T("  (pressione lunga)", "  (long press)") : "";
        }

        void Execute(MacroStep s, bool random, int listId, int pc)
        {
            WaitForGameWindow();
            if (Stopped) return;

            bool isWait = s.Type == StepType.WaitPixel || s.Type == StepType.WaitPixelGone;
            bool longDur = false, longPause = false;
            int dur;
            if (isWait) dur = s.DurationMs;
            else if (s.RandDur && s.SupportsRandDur) dur = Rand.Pick(rng, s.DurMin, s.DurMax, s.DurLongPct, s.DurLongMax, out longDur);
            else dur = random ? Jitter(s.DurationMs, s.JitterMs, 1) : s.DurationMs;
            int pause;
            if (s.RandPause && s.HasPause) pause = Rand.Pick(rng, s.PauseMin, s.PauseMax, s.PauseLongPct, s.PauseLongMax, out longPause);
            else pause = random ? Jitter(s.PauseMs, s.JitterMs, 0) : s.PauseMs;
            string tag = Tag(s);

            switch (s.Type)
            {
                case StepType.Combo:
                    RunCombo(s, listId, pc, tag);
                    break;
                case StepType.Keys:
                    {
                        List<Keys> keys;
                        if (!KeyParser.TryParseCombo(s.Input, out keys))
                        {
                            Log(LogKind.Warn, tag + L.T("Tasti non validi: '", "Invalid keys: '") + s.Input + "'");
                            break;
                        }
                        BeginStep(listId, pc, dur, s.Input);
                        Log(LogKind.Action, tag + L.T("Tasti ", "Keys ") + s.Input + L.T(" per ", " for ") + dur + " ms" + LongTag(longDur));
                        foreach (Keys k in keys) KeyDown(k);
                        Sleep(dur);
                        for (int i = keys.Count - 1; i >= 0; i--) KeyUp(keys[i]);
                        break;
                    }
                case StepType.RandomKey:
                    {
                        List<List<Keys>> pool = GetPool(s.Input);
                        if (pool == null || pool.Count == 0)
                        {
                            Log(LogKind.Warn, tag + L.T("Insieme di tasti non valido: '", "Invalid key set: '") + s.Input + "'");
                            break;
                        }
                        List<Keys> keys = pool[rng.Next(pool.Count)];
                        string name = KeyPool.ComboName(keys);
                        BeginStep(listId, pc, dur, name);
                        Log(LogKind.Action, tag + L.T("Tasto casuale ", "Random key ") + name + L.T(" per ", " for ") + dur + " ms" + LongTag(longDur));
                        foreach (Keys k in keys) KeyDown(k);
                        Sleep(dur);
                        for (int i = keys.Count - 1; i >= 0; i--) KeyUp(keys[i]);
                        break;
                    }
                case StepType.Click:
                    BeginStep(listId, pc, Math.Max(20, dur), null);
                    Log(LogKind.Action, tag + L.T("Click ", "Click ") + L.Button(s.Input) + L.T(" per ", " for ") + dur + " ms" + LongTag(longDur));
                    ButtonDown(s.Input);
                    Sleep(Math.Max(20, dur));
                    ButtonUp(s.Input);
                    break;
                case StepType.MouseMove:
                    {
                        int dx = (random || s.RandMove) ? RandSym(s.MoveX) : s.MoveX;
                        int dy = (random || s.RandMove) ? RandSym(s.MoveY) : s.MoveY;
                        BeginStep(listId, pc, dur, "(" + dx + ", " + dy + ")");
                        Log(LogKind.Action, tag + "Mouse (" + dx + ", " + dy + ") " + L.T("in ", "in ") + dur + " ms" + LongTag(longDur));
                        MoveSmooth(dx, dy, dur);
                        break;
                    }
                case StepType.MoveTo:
                    {
                        int px = s.MoveX + RandSym(s.PosJitter), py = s.MoveY + RandSym(s.PosJitter);
                        BeginStep(listId, pc, 0, "(" + px + ", " + py + ")");
                        Log(LogKind.Action, tag + L.T("Sposto il cursore su (", "Moving the cursor to (") + px + ", " + py + ")");
                        if (!dryRun) { InputSender.MoveAbs(px, py); Sleep(60); }
                        break;
                    }
                case StepType.ClickAt:
                    {
                        int px = s.MoveX + RandSym(s.PosJitter), py = s.MoveY + RandSym(s.PosJitter);
                        BeginStep(listId, pc, Math.Max(40, dur), "(" + px + ", " + py + ")");
                        Log(LogKind.Action, tag + L.T("Click ", "Click ") + L.Button(s.Input) + L.T(" in (", " at (") + px + ", " + py + ")");
                        if (!dryRun) { InputSender.MoveAbs(px, py); Sleep(80); }
                        ButtonDown(s.Input);
                        Sleep(Math.Max(40, dur));
                        ButtonUp(s.Input);
                        break;
                    }
                case StepType.WaitPixel:
                case StepType.WaitPixelGone:
                    BeginStep(listId, pc, s.DurationMs, null);
                    WaitForPixel(listId, pc, s, s.Type == StepType.WaitPixel);
                    break;
                default:
                    BeginStep(listId, pc, dur, null);
                    Log(LogKind.Action, tag + L.T("Pausa ", "Wait ") + dur + " ms" + LongTag(longDur));
                    Sleep(dur);
                    break;
            }
            if (pause > 0 && s.RandPause && s.HasPause && longPause)
                Log(LogKind.Action, tag + L.T("Pausa lunga dopo il passo: ", "Long pause after the step: ") + pause + " ms");
            Sleep(Math.Max(1, pause));
        }

        sealed class ComboRun
        {
            public int Kind;             // 0 tasti, 1 click, 2 mouse, 3 mira
            public int Dur;
            public bool IsLong;
            public List<Keys> Keys;
            public string Button;
            public int Dx, Dy, SentX, SentY;
            public string Label;
            public bool Done;
        }

        // "Azioni insieme": tasti, click, movimento del mouse e mira partono nello stesso istante e ognuno finisce alla propria
        // durata casuale; il passo termina quando hanno finito tutti.
        void RunCombo(MacroStep s, int listId, int pc, string tag)
        {
            if (s.Parts == null || s.Parts.Length == 0)
            {
                Log(LogKind.Warn, tag + L.T("Azioni insieme senza parti: ignorato", "Do-together step without parts: ignored"));
                return;
            }
            List<ComboRun> runs = new List<ComboRun>();
            for (int i = 0; i < s.Parts.Length && i < 4; i++)
            {
                ComboPart p = s.Parts[i];
                if (p == null || !p.Enabled) continue;
                if (p.Chance < 100 && rng.Next(100) >= p.Chance) continue;
                ComboRun r = new ComboRun();
                r.Kind = i;
                bool isLong;
                r.Dur = Rand.Pick(rng, p.DurMin, p.DurMax, p.LongPct, p.LongMax, out isLong);
                r.IsLong = isLong;
                if (i == ComboPart.Keys)
                {
                    List<List<Keys>> pool = GetPool(p.Input);
                    if (pool == null || pool.Count == 0)
                    {
                        Log(LogKind.Warn, tag + L.T("Insieme di tasti non valido: '", "Invalid key set: '") + p.Input + "'");
                        continue;
                    }
                    r.Keys = pool[rng.Next(pool.Count)];
                    r.Label = KeyPool.ComboName(r.Keys);
                }
                else if (i == ComboPart.Click || i == ComboPart.Aim)
                {
                    r.Button = string.IsNullOrEmpty(p.Input) ? (i == ComboPart.Aim ? "Destro" : "Sinistro") : p.Input;
                    r.Label = L.Button(r.Button);
                }
                else
                {
                    r.Dx = p.RandMove ? RandSym(p.MoveX) : p.MoveX;
                    r.Dy = p.RandMove ? RandSym(p.MoveY) : p.MoveY;
                    r.Label = "(" + r.Dx + ", " + r.Dy + ")";
                }
                runs.Add(r);
            }
            // "quante insieme": ne restano solo PickCount, scelte a caso
            if (s.PickCount > 0)
                while (runs.Count > s.PickCount) runs.RemoveAt(rng.Next(runs.Count));
            if (runs.Count == 0)
            {
                Log(LogKind.Info, tag + L.T("Azioni insieme: questa volta nessuna azione scelta", "Do together: no action picked this time"));
                return;
            }

            int planned = 0;
            StringBuilder desc = new StringBuilder();
            string[] names = { L.T("tasti ", "keys "), L.T("click ", "click "), "mouse ", L.T("mira ", "aim ") };
            foreach (ComboRun r in runs)
            {
                planned = Math.Max(planned, r.Dur);
                if (desc.Length > 0) desc.Append(" + ");
                desc.Append(names[r.Kind]).Append(r.Label).Append(" (").Append(StepStyle.Fmt(r.Dur)).Append(r.IsLong ? L.T(", lunga", ", long") : "").Append(")");
            }
            BeginStep(listId, pc, planned, desc.ToString());
            Log(LogKind.Action, tag + L.T("Insieme: ", "Together: ") + desc);

            long t0 = Now;
            foreach (ComboRun r in runs)
            {
                if (r.Kind == ComboPart.Keys) foreach (Keys k in r.Keys) KeyDown(k);
                else if (r.Kind == ComboPart.Click || r.Kind == ComboPart.Aim) ButtonDown(r.Button);
            }
            while (!Stopped)
            {
                if (paused) { WaitWhilePaused(); continue; }
                long el = Now - t0;
                bool pending = false;
                foreach (ComboRun r in runs)
                {
                    if (r.Done) continue;
                    if (r.Kind == ComboPart.Mouse)
                    {
                        double f = r.Dur <= 0 ? 1.0 : Math.Min(1.0, el / (double)r.Dur);
                        int tx = (int)Math.Round(r.Dx * f), ty = (int)Math.Round(r.Dy * f);
                        if ((tx != r.SentX || ty != r.SentY) && !dryRun) InputSender.Move(tx - r.SentX, ty - r.SentY);
                        r.SentX = tx; r.SentY = ty;
                        if (f >= 1.0) r.Done = true; else pending = true;
                    }
                    else if (el >= r.Dur)
                    {
                        if (r.Kind == ComboPart.Keys) { for (int i = r.Keys.Count - 1; i >= 0; i--) KeyUp(r.Keys[i]); }
                        else ButtonUp(r.Button);
                        r.Done = true;
                    }
                    else pending = true;
                }
                if (!pending) break;
                Sleep(4);
            }
        }

        void WaitForPixel(int listId, int pc, MacroStep s, bool wantMatch)
        {
            Color target;
            if (!ScreenPixel.TryParseHex(s.Input, out target))
            {
                Log(LogKind.Warn, Tag(s) + L.T("Colore non valido: '", "Invalid color: '") + s.Input + "'");
                return;
            }
            string where = (s.Width > 1 || s.Height > 1)
                ? L.T("area (", "area (") + s.MoveX + ", " + s.MoveY + ") " + s.Width + "x" + s.Height
                : L.T("punto (", "point (") + s.MoveX + ", " + s.MoveY + ")";
            Log(LogKind.Action, Tag(s) + L.T("Attendo che il colore #" + s.Input.ToUpperInvariant() + " su " + where + (wantMatch ? " compaia" : " sparisca")
                    + " (max " + StepStyle.Fmt(s.DurationMs) + ")",
                "Waiting for the color #" + s.Input.ToUpperInvariant() + " on " + where + (wantMatch ? " to appear" : " to disappear")
                    + " (max " + StepStyle.Fmt(s.DurationMs) + ")"));
            long t0 = Now;
            bool ok = false;
            Color read = Color.Empty;
            int delta = 255;
            bool lastPresent = false;
            while (!Stopped)
            {
                lastPresent = ScreenPixel.Check(s.MoveX, s.MoveY, s.Width, s.Height, target, s.Tolerance, out read, out delta);
                NoteColor(listId, pc, s, target, read, delta, lastPresent);
                if (lastPresent == wantMatch) { ok = true; break; }
                if (Now - t0 >= s.DurationMs) break;
                Sleep(200);
            }
            if (!Stopped)
            {
                LogColor(s, target, read, delta, lastPresent);
                Log(ok ? LogKind.Info : LogKind.Warn, Tag(s)
                    + (ok ? L.T("Condizione soddisfatta dopo " + (Now - t0) + " ms", "Condition met after " + (Now - t0) + " ms")
                          : L.T("Timeout: continuo comunque", "Timeout: continuing anyway")));
            }
        }

        void KeyDown(Keys k)
        {
            if (!dryRun) InputSender.Key(k, true);
            heldKeys.Add(k);
        }

        void KeyUp(Keys k)
        {
            if (!dryRun) InputSender.Key(k, false);
            heldKeys.Remove(k);
        }

        void ButtonDown(string b)
        {
            if (!dryRun) InputSender.Button(b, true);
            heldButtons.Add(b);
        }

        void ButtonUp(string b)
        {
            if (!dryRun) InputSender.Button(b, false);
            heldButtons.Remove(b);
        }

        void MoveSmooth(int dx, int dy, int durationMs)
        {
            if (dx == 0 && dy == 0) { Sleep(durationMs); return; }
            if (durationMs < 20)
            {
                if (!dryRun) InputSender.Move(dx, dy);
                return;
            }
            long t0 = Now;
            int sentX = 0, sentY = 0;
            while (!Stopped)
            {
                if (paused) { WaitWhilePaused(); continue; }
                double f = Math.Min(1.0, (Now - t0) / (double)durationMs);
                int tx = (int)Math.Round(dx * f);
                int ty = (int)Math.Round(dy * f);
                if ((tx != sentX || ty != sentY) && !dryRun) InputSender.Move(tx - sentX, ty - sentY);
                sentX = tx; sentY = ty;
                if (f >= 1.0) break;
                Sleep(4);
            }
        }

        void ReleaseAll()
        {
            foreach (Keys k in heldKeys.ToArray()) { if (!dryRun) InputSender.Key(k, false); }
            foreach (string b in heldButtons.ToArray()) { if (!dryRun) InputSender.Button(b, false); }
            heldKeys.Clear();
            heldButtons.Clear();
        }
    }
}
