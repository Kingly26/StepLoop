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

[assembly: System.Reflection.AssemblyTitle("StepLoop")]
[assembly: System.Reflection.AssemblyVersion("6.1.0.0")]

namespace StepLoop
{
    // Lingua dell'interfaccia: ogni testo è scritto in italiano e in inglese nel punto in cui viene usato.
    public static class L
    {
        public static bool En;

        public static string T(string it, string en) { return En ? en : it; }

        // Il profilo salva sempre il nome italiano del pulsante (Sinistro/Destro/Centrale): qui si traduce solo per mostrarlo.
        public static string Button(string canonical)
        {
            switch ((canonical ?? "").ToLowerInvariant())
            {
                case "destro": return T("destro", "right");
                case "centrale": return T("centrale", "middle");
                default: return T("sinistro", "left");
            }
        }
    }

    // Cartella dei dati dell'app (%APPDATA%\StepLoop). Alla prima apertura copia lo stato salvato e le preferenze
    // dalla vecchia cartella R6DroneMacro, senza toccarla.
    public static class AppPaths
    {
        static bool migrated;

        public static string DataDir
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dir = Path.Combine(root, "StepLoop");
                if (!migrated)
                {
                    migrated = true;
                    try
                    {
                        string old = Path.Combine(root, "R6DroneMacro");
                        if (Directory.Exists(old))
                        {
                            Directory.CreateDirectory(dir);
                            foreach (string name in new string[] { "last.json", "settings.txt" })
                            {
                                string from = Path.Combine(old, name), to = Path.Combine(dir, name);
                                if (File.Exists(from) && !File.Exists(to)) File.Copy(from, to);
                            }
                        }
                    }
                    catch (Exception) { }
                }
                return dir;
            }
        }
    }

    // Preferenze dell'app (lingua e tema), separate dai profili.
    public static class AppSettings
    {
        public static string PathOverride;

        static string FilePath
        {
            get
            {
                if (PathOverride != null) return PathOverride;
                return Path.Combine(AppPaths.DataDir, "settings.txt");
            }
        }

        public static void Load()
        {
            bool en = !string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "it", StringComparison.OrdinalIgnoreCase);
            bool dark = true;
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                        string v = line.Substring(eq + 1).Trim().ToLowerInvariant();
                        if (k == "lang") en = v == "en";
                        else if (k == "theme") dark = v == "dark";
                    }
                }
            }
            catch (Exception) { }
            L.En = en;
            Theme.Dark = dark;
        }

        public static void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllLines(FilePath, new string[] { "lang=" + (L.En ? "en" : "it"), "theme=" + (Theme.Dark ? "dark" : "light") });
            }
            catch (Exception) { }
        }
    }

    public enum StepType
    {
        Keys = 0, Click = 1, MouseMove = 2, Wait = 3, ClickAt = 4, WaitPixel = 5, WaitPixelGone = 6,
        LoopStart = 7, LoopEnd = 8, IfStart = 9, Else = 10, IfEnd = 11, Break = 12, StopMacro = 13, MoveTo = 14,
        RandomKey = 15, Label = 16, Goto = 17,
        ChoiceStart = 18, Option = 19, ChoiceEnd = 20, SetVar = 21, Combo = 22
    }

    public enum RunMode { Sync = 0, Random = 1 }

    // Famiglie semantiche dei passi (determinano il colore nell'interfaccia).
    public enum Sem { Input, Flow, Cond, Wait, Exit }

    // Valore casuale in [min, max]; con probabilità longPct% prende invece un valore "lungo" tra max (escluso) e longMax.
    public static class Rand
    {
        public static int Pick(Random rng, int min, int max, int longPct, int longMax, out bool isLong)
        {
            isLong = false;
            min = Math.Max(0, min);
            max = Math.Max(min, max);
            if (longPct > 0 && longMax > max && rng.Next(100) < longPct)
            {
                isLong = true;
                return rng.Next(max + 1, longMax + 1);
            }
            return min == max ? min : rng.Next(min, max + 1);
        }
    }

    // Insieme di tasti "a caso": "W, A, S, D", intervalli "A-Z" / "1-9", combinazioni "W+D".
    public static class KeyPool
    {
        const int MaxEntries = 256;

        public static bool TryParse(string text, out List<List<Keys>> combos, out string badToken)
        {
            combos = new List<List<Keys>>();
            badToken = null;
            if (string.IsNullOrWhiteSpace(text)) { badToken = ""; return false; }
            string[] tokens = text.Split(new char[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in tokens)
            {
                string t = raw.Trim();
                if (t.Length == 0) continue;
                if (t.Length == 3 && t[1] == '-' && ((char.IsLetter(t[0]) && char.IsLetter(t[2])) || (char.IsDigit(t[0]) && char.IsDigit(t[2]))))
                {
                    char a = char.ToUpperInvariant(t[0]), b = char.ToUpperInvariant(t[2]);
                    if (a > b) { badToken = t; return false; }
                    for (char c = a; c <= b; c++)
                    {
                        Keys k;
                        if (!KeyParser.TryParseKey(c.ToString(), out k)) { badToken = t; return false; }
                        List<Keys> one = new List<Keys>();
                        one.Add(k);
                        combos.Add(one);
                    }
                }
                else
                {
                    List<Keys> combo;
                    if (!KeyParser.TryParseCombo(t, out combo)) { badToken = t; return false; }
                    combos.Add(combo);
                }
                if (combos.Count > MaxEntries) { badToken = t; return false; }
            }
            return combos.Count > 0;
        }

        public static string ComboName(List<Keys> keys)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Keys k in keys)
            {
                if (sb.Length > 0) sb.Append('+');
                sb.Append(KeyParser.DisplayName(k));
            }
            return sb.ToString();
        }
    }

    // Una delle quattro azioni di un passo "Azioni insieme".
    [DataContract]
    public class ComboPart
    {
        public const int Keys = 0, Click = 1, Mouse = 2, Aim = 3;

        [DataMember] public bool Enabled;
        [DataMember] public int Chance = 100;            // probabilità (%) che questa parte partecipi
        [DataMember(EmitDefaultValue = false)] public string Input;   // tasti / insieme di tasti, oppure pulsante del mouse
        [DataMember] public int MoveX;                   // solo mouse
        [DataMember] public int MoveY;
        [DataMember] public bool RandMove = true;        // quantità del movimento a caso entro ± i valori
        [DataMember] public int DurMin = 1000;           // durata casuale: da...
        [DataMember] public int DurMax = 3000;           // ...a
        [DataMember] public int LongPct = 10;            // ogni tanto (%) una durata più lunga...
        [DataMember] public int LongMax = 10000;         // ...fino a questo valore

        public ComboPart Clone() { return (ComboPart)MemberwiseClone(); }

        public static ComboPart[] Defaults()
        {
            ComboPart[] p = new ComboPart[4];
            for (int i = 0; i < 4; i++) { p[i] = new ComboPart(); p[i].Enabled = true; }
            p[Keys].Input = "W, A, S, D";
            p[Click].Input = "Sinistro";
            p[Mouse].MoveX = 300; p[Mouse].MoveY = 100;
            p[Aim].Input = "Destro";
            return p;
        }

        public void Sanitize()
        {
            Chance = Math.Min(100, Math.Max(0, Chance));
            DurMin = Math.Max(0, DurMin);
            DurMax = Math.Max(DurMin, DurMax);
            LongPct = Math.Min(100, Math.Max(0, LongPct));
            LongMax = Math.Max(0, LongMax);
        }
    }

    // Una condizione in più di un "Se", di un "Ripeti fino a" o di un "Attendi colore": si combina con la condizione principale
    // del passo (che resta nei campi del passo, per la compatibilità con i profili salvati) in OR (basta una) o in AND (tutte).
    [DataContract]
    public class CondClause
    {
        public const int Color = 0, Variable = 1, Chance = 2;
        public const int MaxClauses = 4;            // principale + 3 aggiuntive

        [DataMember] public int Kind;                // 0 colore a schermo, 1 variabile, 2 probabilità
        [DataMember(EmitDefaultValue = false)] public string Input = "FFFFFF";   // colore RRGGBB
        [DataMember] public int MoveX;
        [DataMember] public int MoveY;
        [DataMember] public int Width = 1;
        [DataMember] public int Height = 1;
        [DataMember] public int Tolerance = 24;
        [DataMember] public bool Negate;             // colore: "NON è presente"
        [DataMember(EmitDefaultValue = false)] public string VarName;
        [DataMember] public int CmpOp;               // 0 ==, 1 !=, 2 <, 3 <=, 4 >, 5 >=
        [DataMember] public int CmpValue;
        [DataMember] public int ChancePct = 50;

        public CondClause Clone() { return (CondClause)MemberwiseClone(); }

        public void Sanitize()
        {
            Kind = Math.Min(2, Math.Max(0, Kind));
            if (Input == null) Input = "";
            Width = Math.Max(1, Width);
            Height = Math.Max(1, Height);
            Tolerance = Math.Min(255, Math.Max(0, Tolerance));
            CmpOp = Math.Min(5, Math.Max(0, CmpOp));
            ChancePct = Math.Min(100, Math.Max(0, ChancePct));
            if (VarName != null) VarName = VarName.Trim();
        }
    }

    [DataContract]
    public class MacroStep
    {
        [DataMember] public StepType Type;
        [DataMember] public string Name = "";
        [DataMember] public string Input = "";
        [DataMember] public int MoveX;
        [DataMember] public int MoveY;
        [DataMember] public int Width = 1;
        [DataMember] public int Height = 1;
        [DataMember] public int DurationMs = 100;
        [DataMember] public int PauseMs = 100;
        [DataMember] public int JitterMs;
        [DataMember] public int Weight = 1;
        [DataMember] public int Tolerance = 12;
        [DataMember] public int Count;
        [DataMember] public bool Negate;
        [DataMember] public bool UseUntil;
        [DataMember] public bool CheckFirst;

        // Durata casuale: intervallo [DurMin, DurMax]; con DurLongPct % di probabilità prende invece un valore fino a DurLongMax.
        [DataMember] public bool RandDur;
        [DataMember] public int DurMin;
        [DataMember] public int DurMax;
        [DataMember] public int DurLongPct;
        [DataMember] public int DurLongMax;
        // Pausa dopo il passo, casuale allo stesso modo.
        [DataMember] public bool RandPause;
        [DataMember] public int PauseMin;
        [DataMember] public int PauseMax;
        [DataMember] public int PauseLongPct;
        [DataMember] public int PauseLongMax;
        // Raggio (px) di variazione casuale della posizione per "Click su" e "Vai a".
        [DataMember] public int PosJitter;
        // "Muovi mouse": quantità scelta a caso entro ± i valori anche in modalità Sincrona.
        [DataMember] public bool RandMove;
        // Blocco Ripeti/Se ridotto a una riga nella lista (solo aspetto, non cambia l'esecuzione).
        [DataMember] public bool Collapsed;

        // "Se": 0 = colore a schermo, 1 = variabile, 2 = probabilità (ChancePct % delle volte).
        [DataMember] public int CondKind;
        [DataMember(EmitDefaultValue = false)] public string VarName;
        [DataMember] public int CmpOp;          // 0 ==, 1 !=, 2 <, 3 <=, 4 >, 5 >=
        [DataMember] public int CmpValue;       // valore di confronto (Se) o valore da usare (Variabile)
        [DataMember] public int VarOp;          // Variabile: 0 imposta, 1 somma, 2 sottrai, 3 a caso tra CmpValue e VarValue2
        [DataMember] public int VarValue2;
        [DataMember] public int ChancePct = 50;
        // "Azioni insieme": le 4 parti (0 tasti, 1 click, 2 mouse, 3 mira) e quante farne partire a caso (0 = tutte).
        [DataMember(EmitDefaultValue = false)] public ComboPart[] Parts;
        [DataMember] public int PickCount;
        // "Scegli a caso": peso della prima alternativa (le altre sono pesate dalle Opzioni); 0 = 1.
        [DataMember(EmitDefaultValue = false)] public int AltWeight;
        // Condizioni aggiuntive (Se / Ripeti fino a / Attendi colore) e come si combinano con quella principale: 0 = OR, 1 = AND.
        [DataMember(EmitDefaultValue = false)] public CondClause[] Extra;
        [DataMember(EmitDefaultValue = false)] public int CondJoin;

        // Calcolati da Flow.Prepare, non salvati nel profilo.
        public int Match = -1;
        public int ElseMatch = -1;
        public List<int> OptionMarks;          // "Scegli a caso": indici dei passi "Opzione" del blocco

        public MacroStep Clone()
        {
            MacroStep c = (MacroStep)MemberwiseClone();
            if (Parts != null)
            {
                c.Parts = new ComboPart[Parts.Length];
                for (int i = 0; i < Parts.Length; i++) c.Parts[i] = Parts[i] == null ? null : Parts[i].Clone();
            }
            if (Extra != null)
            {
                c.Extra = new CondClause[Extra.Length];
                for (int i = 0; i < Extra.Length; i++) c.Extra[i] = Extra[i] == null ? null : Extra[i].Clone();
            }
            c.OptionMarks = null;
            return c;
        }

        // ---- condizioni: la principale (campi del passo) + le aggiuntive (Extra) ----

        // Passi che hanno una condizione sul colore / variabile / caso.
        public bool HasCondition
        {
            get { return Type == StepType.IfStart || Type == StepType.LoopStart || Type == StepType.WaitPixel || Type == StepType.WaitPixelGone; }
        }

        // La condizione è davvero usata: un Ripeti la usa solo se ha "esci quando…".
        public bool ConditionActive
        {
            get { return Type == StepType.IfStart || Type == StepType.WaitPixel || Type == StepType.WaitPixelGone || (Type == StepType.LoopStart && UseUntil); }
        }

        public int ExtraCount
        {
            get
            {
                int n = 0;
                if (Extra != null) foreach (CondClause c in Extra) if (c != null) n++;
                return n;
            }
        }

        // La condizione principale del passo come clausola (Se: del tipo scelto; Ripeti / Attendi: sempre colore).
        public CondClause PrimaryClause()
        {
            CondClause c = new CondClause();
            c.Kind = Type == StepType.IfStart ? Math.Min(2, Math.Max(0, CondKind)) : CondClause.Color;
            c.Input = Input ?? "";
            c.MoveX = MoveX; c.MoveY = MoveY; c.Width = Width; c.Height = Height; c.Tolerance = Tolerance;
            c.Negate = (Type == StepType.IfStart || Type == StepType.LoopStart) && Negate;
            c.VarName = VarName; c.CmpOp = CmpOp; c.CmpValue = CmpValue; c.ChancePct = ChancePct;
            return c;
        }

        // Scrive una clausola nei campi della condizione principale (solo i campi pertinenti al tipo di passo).
        public void ApplyPrimary(CondClause c)
        {
            if (Type == StepType.IfStart)
            {
                CondKind = c.Kind;
                if (c.Kind == CondClause.Variable) { VarName = c.VarName; CmpOp = c.CmpOp; CmpValue = c.CmpValue; Negate = false; return; }
                if (c.Kind == CondClause.Chance) { ChancePct = c.ChancePct; Negate = false; return; }
            }
            Input = c.Input; MoveX = c.MoveX; MoveY = c.MoveY; Width = c.Width; Height = c.Height; Tolerance = c.Tolerance;
            if (Type == StepType.IfStart || Type == StepType.LoopStart) Negate = c.Negate;
        }

        // Tutte le clausole della condizione, la principale per prima.
        public List<CondClause> AllClauses()
        {
            List<CondClause> l = new List<CondClause>();
            l.Add(PrimaryClause());
            if (Extra != null) foreach (CondClause c in Extra) if (c != null) l.Add(c);
            return l;
        }

        public static MacroStep Make(StepType type)
        {
            MacroStep s = new MacroStep();
            s.Type = type;
            s.DurationMs = 0;
            s.PauseMs = 0;
            return s;
        }

        public static MacroStep MakeKeys(string keys, int dur, int pause, int jit, int weight)
        {
            MacroStep s = new MacroStep();
            s.Type = StepType.Keys; s.Input = keys;
            s.DurationMs = dur; s.PauseMs = pause; s.JitterMs = jit; s.Weight = weight;
            return s;
        }

        // Un tasto (o combinazione) scelto a caso dall'insieme, premuto per una durata casuale.
        public static MacroStep MakeRandomKey(string pool, int minMs, int maxMs, int longPct, int longMaxMs, int pause)
        {
            MacroStep s = new MacroStep();
            s.Type = StepType.RandomKey; s.Input = pool;
            s.RandDur = true; s.DurMin = minMs; s.DurMax = maxMs; s.DurLongPct = longPct; s.DurLongMax = longMaxMs;
            s.DurationMs = minMs; s.PauseMs = pause;
            return s;
        }

        public static MacroStep MakeMove(int dx, int dy, int dur, int pause, int jit, int weight)
        {
            MacroStep s = new MacroStep();
            s.Type = StepType.MouseMove; s.MoveX = dx; s.MoveY = dy;
            s.DurationMs = dur; s.PauseMs = pause; s.JitterMs = jit; s.Weight = weight;
            return s;
        }

        public static MacroStep MakeWait(int ms)
        {
            MacroStep s = new MacroStep();
            s.Type = StepType.Wait; s.DurationMs = ms; s.PauseMs = 0;
            return s;
        }

        public static MacroStep MakeClick(string button, int holdMs, int pause)
        {
            MacroStep s = new MacroStep();
            s.Type = StepType.Click; s.Input = button;
            s.DurationMs = holdMs; s.PauseMs = pause;
            return s;
        }

        public static MacroStep MakeClickAt(string button, int x, int y, int holdMs, int pause)
        {
            MacroStep s = new MacroStep();
            s.Type = StepType.ClickAt; s.Input = button; s.MoveX = x; s.MoveY = y;
            s.DurationMs = holdMs; s.PauseMs = pause;
            return s;
        }

        public static MacroStep MakeMoveTo(int x, int y, int pause)
        {
            MacroStep s = new MacroStep();
            s.Type = StepType.MoveTo; s.MoveX = x; s.MoveY = y;
            s.DurationMs = 0; s.PauseMs = pause;
            return s;
        }

        public static MacroStep MakeWaitPixel(bool appear, int x, int y, int w, int h, string hex, int tol, int timeoutMs)
        {
            MacroStep s = new MacroStep();
            s.Type = appear ? StepType.WaitPixel : StepType.WaitPixelGone;
            s.MoveX = x; s.MoveY = y; s.Width = w; s.Height = h; s.Input = hex; s.Tolerance = tol;
            s.DurationMs = timeoutMs; s.PauseMs = 0;
            return s;
        }

        public static MacroStep MakeWaitPixel(bool appear, int x, int y, string hex, int tol, int timeoutMs)
        {
            return MakeWaitPixel(appear, x, y, 1, 1, hex, tol, timeoutMs);
        }

        public static MacroStep MakeLoop(int count)
        {
            MacroStep s = Make(StepType.LoopStart);
            s.Count = count;
            return s;
        }

        // Ripeti ... finché il colore è presente (o assente se negate): controlla l'uscita alla fine di ogni giro.
        public static MacroStep MakeLoopUntil(int x, int y, int w, int h, string hex, int tol, bool exitWhenAbsent, int maxLoops, int maxMs)
        {
            MacroStep s = Make(StepType.LoopStart);
            s.UseUntil = true;
            s.MoveX = x; s.MoveY = y; s.Width = w; s.Height = h; s.Input = hex; s.Tolerance = tol;
            s.Negate = exitWhenAbsent;
            s.Count = maxLoops;
            s.DurationMs = maxMs;
            return s;
        }

        public static MacroStep MakeIf(int x, int y, int w, int h, string hex, int tol, bool negate)
        {
            MacroStep s = Make(StepType.IfStart);
            s.MoveX = x; s.MoveY = y; s.Width = w; s.Height = h; s.Input = hex; s.Tolerance = tol; s.Negate = negate;
            return s;
        }

        // "Scegli a caso": esegue UNA sola alternativa (un'opzione, o un passo/blocco se non ci sono opzioni).
        public static MacroStep MakeChoice()
        {
            return Make(StepType.ChoiceStart);
        }

        public static MacroStep MakeOption(int weight)
        {
            MacroStep s = Make(StepType.Option);
            s.Weight = Math.Max(1, weight);
            return s;
        }

        // Variabile: op 0 = imposta a v1, 1 = somma v1, 2 = sottrai v1, 3 = numero a caso tra v1 e v2.
        public static MacroStep MakeSetVar(string name, int op, int v1, int v2)
        {
            MacroStep s = Make(StepType.SetVar);
            s.VarName = name; s.VarOp = op; s.CmpValue = v1; s.VarValue2 = v2;
            return s;
        }

        // Se <variabile> <op> <valore>: op 0 ==, 1 !=, 2 <, 3 <=, 4 >, 5 >=.
        public static MacroStep MakeIfVar(string name, int op, int value)
        {
            MacroStep s = Make(StepType.IfStart);
            s.CondKind = 1; s.VarName = name; s.CmpOp = op; s.CmpValue = value;
            return s;
        }

        // Se "a caso, pct% delle volte".
        public static MacroStep MakeIfChance(int pct)
        {
            MacroStep s = Make(StepType.IfStart);
            s.CondKind = 2; s.ChancePct = pct;
            return s;
        }

        // "Azioni insieme": tasti, click, movimento del mouse e mira nello stesso momento, ognuno con durata casuale.
        // Di default: 1-3 s con il 10% delle volte fino a 10 s.
        public static MacroStep MakeCombo()
        {
            MacroStep s = Make(StepType.Combo);
            s.PauseMs = 100;
            s.Parts = ComboPart.Defaults();
            return s;
        }

        // Punto di arrivo di un Goto: il nome dell'etichetta è il Nome del passo.
        public static MacroStep MakeLabel(string name)
        {
            MacroStep s = Make(StepType.Label);
            s.Name = name;
            return s;
        }

        // Salta all'etichetta indicata (anche fuori dal ciclo / dal Se in cui si trova).
        public static MacroStep MakeGoto(string label)
        {
            MacroStep s = Make(StepType.Goto);
            s.Input = label;
            return s;
        }

        public MacroStep Named(string name) { Name = name; return this; }

        public static string TypeName(StepType t)
        {
            switch (t)
            {
                case StepType.Keys: return L.T("Tasti", "Keys");
                case StepType.RandomKey: return L.T("Tasto casuale", "Random key");
                case StepType.Click: return L.T("Click mouse", "Mouse click");
                case StepType.MouseMove: return L.T("Muovi mouse", "Move mouse");
                case StepType.ClickAt: return L.T("Click in un punto", "Click at a point");
                case StepType.WaitPixel: return L.T("Aspetta colore", "Wait for color");
                case StepType.WaitPixelGone: return L.T("Aspetta sparizione", "Wait for color to go");
                case StepType.LoopStart: return L.T("Ripeti", "Repeat");
                case StepType.LoopEnd: return L.T("Fine ripeti", "End repeat");
                case StepType.IfStart: return L.T("Se colore", "If color");
                case StepType.Else: return L.T("Altrimenti", "Else");
                case StepType.IfEnd: return L.T("Fine se", "End if");
                case StepType.Break: return L.T("Esci dal ciclo", "Exit loop");
                case StepType.StopMacro: return L.T("Ferma la macro", "Stop the macro");
                case StepType.MoveTo: return L.T("Sposta cursore", "Move cursor");
                case StepType.Label: return L.T("Etichetta", "Label");
                case StepType.Goto: return "Goto";
                case StepType.ChoiceStart: return L.T("Scegli a caso", "Pick at random");
                case StepType.Option: return L.T("Opzione", "Option");
                case StepType.ChoiceEnd: return L.T("Fine scelta", "End pick");
                case StepType.SetVar: return L.T("Variabile", "Variable");
                case StepType.Combo: return L.T("Azioni insieme", "Do together");
                default: return L.T("Pausa", "Wait");
            }
        }

        public bool HasDuration
        {
            get
            {
                switch (Type)
                {
                    case StepType.LoopStart: case StepType.LoopEnd: case StepType.IfStart: case StepType.Else:
                    case StepType.IfEnd: case StepType.Break: case StepType.StopMacro: case StepType.MoveTo:
                    case StepType.Label: case StepType.Goto:
                    case StepType.ChoiceStart: case StepType.Option: case StepType.ChoiceEnd: case StepType.SetVar: case StepType.Combo:
                        return false;
                }
                return true;
            }
        }

        // La durata di questo passo può essere casuale (intervallo + valori lunghi occasionali).
        public bool SupportsRandDur
        {
            get
            {
                switch (Type)
                {
                    case StepType.Keys: case StepType.RandomKey: case StepType.Click: case StepType.ClickAt:
                    case StepType.MouseMove: case StepType.Wait:
                        return true;
                }
                return false;
            }
        }

        // Il passo ha una "pausa dopo" (e quindi può averla casuale).
        public bool HasPause
        {
            get { return SupportsRandDur || Type == StepType.MoveTo || Type == StepType.Combo; }
        }

        // Blocchi con un'intestazione e una chiusura (Ripeti, Se, Scegli a caso).
        public bool IsBlockHeader
        {
            get { return Type == StepType.LoopStart || Type == StepType.IfStart || Type == StepType.ChoiceStart; }
        }

        // Chiusura di un blocco (Fine ripeti / Fine se / Fine scelta) e marcatori di ramo (Altrimenti / Opzione).
        public bool IsBlockFooter
        {
            get { return Type == StepType.LoopEnd || Type == StepType.IfEnd || Type == StepType.ChoiceEnd; }
        }

        public bool IsBranchMarker
        {
            get { return Type == StepType.Else || Type == StepType.Option; }
        }

        // Condizione del "Se" valutata sul colore a schermo (le altre usano variabili o il caso).
        public bool UsesColor
        {
            get
            {
                if (Type == StepType.IfStart) return CondKind == 0;
                if (Type == StepType.LoopStart) return UseUntil;
                return Type == StepType.WaitPixel || Type == StepType.WaitPixelGone;
            }
        }

        public void Sanitize()
        {
            if (Input == null) Input = "";
            if (Name == null) Name = "";
            DurationMs = Math.Max(0, DurationMs);
            PauseMs = Math.Max(0, PauseMs);
            JitterMs = Math.Max(0, JitterMs);
            Weight = Math.Max(1, Weight);
            Tolerance = Math.Min(255, Math.Max(0, Tolerance));
            Width = Math.Max(1, Width);
            Height = Math.Max(1, Height);
            Count = Math.Max(0, Count);
            DurMin = Math.Max(0, DurMin);
            DurMax = Math.Max(DurMin, DurMax);
            DurLongPct = Math.Min(100, Math.Max(0, DurLongPct));
            DurLongMax = Math.Max(0, DurLongMax);
            PauseMin = Math.Max(0, PauseMin);
            PauseMax = Math.Max(PauseMin, PauseMax);
            PauseLongPct = Math.Min(100, Math.Max(0, PauseLongPct));
            PauseLongMax = Math.Max(0, PauseLongMax);
            PosJitter = Math.Min(1000, Math.Max(0, PosJitter));
            CondKind = Math.Min(2, Math.Max(0, CondKind));
            CmpOp = Math.Min(5, Math.Max(0, CmpOp));
            VarOp = Math.Min(3, Math.Max(0, VarOp));
            ChancePct = Math.Min(100, Math.Max(0, ChancePct));
            PickCount = Math.Max(0, PickCount);
            AltWeight = Math.Max(0, AltWeight);
            if (VarName != null) VarName = VarName.Trim();
            CondJoin = CondJoin == 1 ? 1 : 0;
            if (Extra != null)
            {
                // niente voci vuote e al massimo 3 condizioni aggiuntive (4 in tutto con la principale)
                List<CondClause> keep = new List<CondClause>();
                foreach (CondClause c in Extra)
                {
                    if (c == null || keep.Count >= CondClause.MaxClauses - 1) continue;
                    c.Sanitize();
                    keep.Add(c);
                }
                Extra = keep.Count == 0 ? null : keep.ToArray();
            }
            if (!Enum.IsDefined(typeof(StepType), Type)) Type = StepType.Wait;
            if (Type == StepType.Combo)
            {
                ComboPart[] defaults = ComboPart.Defaults();
                ComboPart[] fixedParts = new ComboPart[4];
                for (int i = 0; i < 4; i++)
                {
                    fixedParts[i] = Parts != null && i < Parts.Length && Parts[i] != null ? Parts[i] : defaults[i];
                    fixedParts[i].Sanitize();
                }
                Parts = fixedParts;
            }
        }
    }

    // Aspetto dei passi: colori per famiglia, etichette e descrizioni.
    public static class StepStyle
    {
        public static readonly Color KeysColor = Color.FromArgb(0x5B, 0x6B, 0x82);
        public static readonly Color MouseColor = Color.FromArgb(0x2E, 0x9E, 0x5B);
        public static readonly Color WaitColor = Color.FromArgb(0x84, 0x5B, 0xD0);
        public static readonly Color LoopColor = Color.FromArgb(0x2F, 0x6F, 0xDE);
        public static readonly Color CondColor = Color.FromArgb(0xE0, 0x8A, 0x1E);
        public static readonly Color ExitColor = Color.FromArgb(0xD0, 0x45, 0x45);

        public static Color Of(StepType t)
        {
            switch (t)
            {
                case StepType.Keys: case StepType.RandomKey: return KeysColor;
                case StepType.Click: case StepType.ClickAt: case StepType.MoveTo: case StepType.MouseMove: return MouseColor;
                case StepType.Wait: case StepType.WaitPixel: case StepType.WaitPixelGone: return WaitColor;
                case StepType.LoopStart: case StepType.LoopEnd: return LoopColor;
                case StepType.IfStart: case StepType.Else: case StepType.IfEnd: return CondColor;
                default: return ExitColor;
            }
        }

        public static Color KindColor(int kind) { return kind == 0 ? LoopColor : CondColor; }

        // Famiglia semantica del passo (usata dalla nuova interfaccia per scegliere i colori).
        public static Sem SemOf(StepType t)
        {
            switch (t)
            {
                case StepType.Keys: case StepType.RandomKey: case StepType.Click: case StepType.ClickAt:
                case StepType.MoveTo: case StepType.MouseMove: case StepType.Combo:
                    return Sem.Input;
                case StepType.LoopStart: case StepType.LoopEnd: case StepType.Label: case StepType.Goto: case StepType.SetVar:
                    return Sem.Flow;
                case StepType.IfStart: case StepType.Else: case StepType.IfEnd:
                case StepType.ChoiceStart: case StepType.Option: case StepType.ChoiceEnd:
                    return Sem.Cond;
                case StepType.Wait: case StepType.WaitPixel: case StepType.WaitPixelGone:
                    return Sem.Wait;
                default:
                    return Sem.Exit;
            }
        }

        // Mescola il colore c con lo sfondo bg: amount = quanto c resta (1 = puro).
        public static Color Blend(Color c, double amount, Color bg)
        {
            int r = (int)(c.R * amount + bg.R * (1 - amount));
            int g = (int)(c.G * amount + bg.G * (1 - amount));
            int b = (int)(c.B * amount + bg.B * (1 - amount));
            return Color.FromArgb(Math.Min(255, Math.Max(0, r)), Math.Min(255, Math.Max(0, g)), Math.Min(255, Math.Max(0, b)));
        }

        public static Color Blend(Color c, double amount) { return Blend(c, amount, Color.White); }

        public static string Badge(StepType t)
        {
            switch (t)
            {
                case StepType.Keys: return L.T("TASTI", "KEYS");
                case StepType.RandomKey: return L.T("CASUALE", "RANDOM");
                case StepType.Click: return "CLICK";
                case StepType.ClickAt: return L.T("CLICK SU", "CLICK AT");
                case StepType.MoveTo: return L.T("VAI A", "MOVE TO");
                case StepType.MouseMove: return "MOUSE";
                case StepType.Wait: return L.T("PAUSA", "WAIT");
                case StepType.WaitPixel: return L.T("ATTENDI", "WAIT FOR");
                case StepType.WaitPixelGone: return L.T("ATTENDI", "WAIT FOR");
                case StepType.LoopStart: return L.T("RIPETI", "REPEAT");
                case StepType.LoopEnd: return L.T("FINE RIPETI", "END REPEAT");
                case StepType.IfStart: return L.T("SE", "IF");
                case StepType.Else: return L.T("ALTRIMENTI", "ELSE");
                case StepType.IfEnd: return L.T("FINE SE", "END IF");
                case StepType.Break: return L.T("ESCI", "EXIT");
                case StepType.Label: return L.T("ETICHETTA", "LABEL");
                case StepType.Goto: return "GOTO";
                case StepType.ChoiceStart: return L.T("SCEGLI", "PICK");
                case StepType.Option: return L.T("OPZIONE", "OPTION");
                case StepType.ChoiceEnd: return L.T("FINE SCELTA", "END PICK");
                case StepType.SetVar: return L.T("VARIABILE", "VARIABLE");
                case StepType.Combo: return L.T("INSIEME", "TOGETHER");
                default: return "STOP";
            }
        }

        public static string Hint(StepType t)
        {
            switch (t)
            {
                case StepType.Keys:
                    return L.T("Tiene premuti uno o più tasti per il tempo indicato (fisso o casuale).",
                               "Holds one or more keys down for the given time (fixed or random).");
                case StepType.RandomKey:
                    return L.T("Preme un tasto a caso tra quelli dell'insieme, per una durata casuale.",
                               "Presses a random key from your set, for a random duration.");
                case StepType.Click:
                    return L.T("Clicca dove si trova il cursore in questo momento.", "Clicks where the cursor is right now.");
                case StepType.ClickAt:
                    return L.T("Sposta il cursore nel punto e clicca.", "Moves the cursor to the point and clicks.");
                case StepType.MoveTo:
                    return L.T("Porta il cursore nel punto senza cliccare (fa comparire l'hover del pulsante).",
                               "Moves the cursor to the point without clicking (shows the button hover).");
                case StepType.MouseMove:
                    return L.T("Muove il mouse di una quantità relativa (per guardarsi intorno).",
                               "Moves the mouse by a relative amount (to look around).");
                case StepType.Wait:
                    return L.T("Aspetta il tempo indicato (fisso o casuale).", "Waits for the given time (fixed or random).");
                case StepType.WaitPixel:
                    return L.T("Aspetta che l'area dello schermo assuma il colore indicato (o che si verifichi una delle condizioni aggiuntive, in OR).", "Waits until the screen area takes the given color (or until one of the extra conditions is true, as OR).");
                case StepType.WaitPixelGone:
                    return L.T("Aspetta che l'area smetta di avere il colore indicato (con più condizioni: finché nessuna è vera, in OR).", "Waits until the area stops having the given color (with several conditions: until none is true, as OR).");
                case StepType.LoopStart:
                    return L.T("Ripete i passi che contiene. Alla fine di ogni giro controlla se può uscire (anche con più colori in OR); se no, ricomincia.",
                               "Repeats the steps it contains. At the end of each round it checks whether it can exit (also with several colors as OR); if not, it starts again.");
                case StepType.LoopEnd:
                    return L.T("Fine del ciclo: si torna al primo passo del Ripeti, a meno che la condizione di uscita sia soddisfatta.",
                               "End of the loop: goes back to the first step of the Repeat, unless the exit condition is met.");
                case StepType.IfStart:
                    return L.T("Esegue i passi seguenti solo se la condizione è vera (un colore, una variabile o il caso). Puoi aggiungere altre condizioni in OR o in AND.",
                               "Runs the following steps only if the condition is true (a color, a variable or chance). You can add more conditions combined with OR or AND.");
                case StepType.Else:
                    return L.T("Passi eseguiti quando la condizione del 'Se' è falsa.", "Steps run when the 'If' condition is false.");
                case StepType.IfEnd:
                    return L.T("Fine del blocco 'Se'.", "End of the 'If' block.");
                case StepType.Break:
                    return L.T("Esce subito dal ciclo più interno.", "Leaves the innermost loop right away.");
                case StepType.Label:
                    return L.T("Punto di arrivo con un nome: un Goto può saltare qui da qualunque punto dello stesso blocco o di un blocco interno.",
                               "A named landing point: a Goto can jump here from anywhere in the same block or from a block nested inside it.");
                case StepType.Goto:
                    return L.T("Salta direttamente a un'etichetta, anche uscendo da un Ripeti o da un Se. Funziona anche dentro cicli e condizioni.",
                               "Jumps straight to a label, even leaving a Repeat or an If. Works inside loops and conditions too.");
                case StepType.ChoiceStart:
                    return L.T("Esegue UNA sola alternativa scelta a caso (in base al peso). Con «Opzione» separi alternative fatte di più passi; senza opzioni ogni passo o blocco dentro è un'alternativa.",
                               "Runs ONE alternative picked at random (by weight). Use «Option» to separate alternatives made of several steps; without options each step or block inside is an alternative.");
                case StepType.Option:
                    return L.T("Inizia una nuova alternativa del blocco «Scegli a caso». Il peso decide quanto spesso viene scelta.",
                               "Starts a new alternative of the «Pick at random» block. The weight decides how often it is picked.");
                case StepType.ChoiceEnd:
                    return L.T("Fine del blocco «Scegli a caso».", "End of the «Pick at random» block.");
                case StepType.SetVar:
                    return L.T("Imposta, somma, sottrae o sceglie a caso il valore di una variabile. Poi usala in un «Se» per creare comportamenti diversi.",
                               "Sets, adds, subtracts or randomizes the value of a variable. Then use it in an «If» to create different behaviors.");
                case StepType.Combo:
                    return L.T("Fa partire insieme tasti, click, movimento del mouse e mira, ognuno con la propria durata casuale (di default 1-3 s, ogni tanto fino a 10 s).",
                               "Starts keys, clicks, mouse movement and aim together, each with its own random duration (by default 1-3 s, sometimes up to 10 s).");
                default:
                    return L.T("Ferma tutta la macro.", "Stops the whole macro.");
            }
        }

        public static string Fmt(int ms)
        {
            if (ms < 1000) return ms + " ms";
            double s = ms / 1000.0;
            if (s < 60) return s.ToString(s == Math.Floor(s) ? "0" : "0.#", CultureInfo.InvariantCulture) + " s";
            int total = (int)Math.Round(s);
            int m = total / 60, r = total % 60;
            return r == 0 ? m + " min" : m + " min " + r + " s";
        }

        public static string FmtRange(int min, int max)
        {
            return min >= max ? Fmt(min) : Fmt(min) + " - " + Fmt(max);
        }

        // "1 s - 10 s (15%: fino a 30 s)"
        public static string RandInfo(int min, int max, int longPct, int longMax)
        {
            string t = FmtRange(min, Math.Max(min, max));
            if (longPct > 0 && longMax > max)
                t += "  (" + longPct + "%: " + L.T("fino a ", "up to ") + Fmt(longMax) + ")";
            return t;
        }

        static string Area(MacroStep s)
        {
            return s.Width > 1 || s.Height > 1
                ? L.T("area (", "area (") + s.MoveX + ", " + s.MoveY + ") " + s.Width + "x" + s.Height
                : L.T("punto (", "point (") + s.MoveX + ", " + s.MoveY + ")";
        }

        static string Shorten(string text, int max)
        {
            text = (text ?? "").Trim();
            return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
        }

        public static string Title(MacroStep s)
        {
            if (!string.IsNullOrEmpty(s.Name)) return s.Name;
            switch (s.Type)
            {
                case StepType.Keys: return L.T("Premi ", "Press ") + s.Input;
                case StepType.RandomKey: return L.T("Tasto casuale: ", "Random key: ") + Shorten(s.Input, 26);
                case StepType.Click: return L.En ? L.Button(s.Input) + " click" : "Click " + L.Button(s.Input);
                case StepType.ClickAt: return L.T("Click in (", "Click at (") + s.MoveX + ", " + s.MoveY + ")";
                case StepType.MoveTo: return L.T("Vai a (", "Move to (") + s.MoveX + ", " + s.MoveY + ")";
                case StepType.MouseMove: return L.T("Muovi il mouse", "Move the mouse");
                case StepType.Wait: return L.T("Pausa ", "Wait ") + (s.RandDur ? FmtRange(s.DurMin, s.DurMax) : Fmt(s.DurationMs));
                case StepType.WaitPixel: return L.T("Attendi che compaia il colore", "Wait for the color to appear");
                case StepType.WaitPixelGone: return L.T("Attendi che il colore sparisca", "Wait for the color to disappear");
                case StepType.LoopStart:
                    if (s.UseUntil) return L.T("Ripeti fino a quando", "Repeat until");
                    if (s.Count > 0) return L.T("Ripeti " + s.Count + " volte", "Repeat " + s.Count + " times");
                    return L.T("Ripeti", "Repeat");
                case StepType.LoopEnd: return L.T("Fine ripeti", "End repeat");
                case StepType.IfStart:
                    if (s.CondKind == 1) return L.T("Se ", "If ") + VarCondText(s);
                    if (s.CondKind == 2) return L.T("Se a caso ", "If random ") + (s.Negate ? (100 - s.ChancePct) : s.ChancePct) + "%";
                    return L.T("Se colore", "If color");
                case StepType.Else: return L.T("Altrimenti", "Else");
                case StepType.IfEnd: return L.T("Fine se", "End if");
                case StepType.Break: return L.T("Esci dal ciclo", "Exit loop");
                case StepType.Label: return L.T("Etichetta", "Label");
                case StepType.Goto: return L.T("Salta a «", "Jump to «") + s.Input + "»";
                case StepType.ChoiceStart: return L.T("Scegli a caso", "Pick at random");
                case StepType.Option: return L.T("Opzione", "Option");
                case StepType.ChoiceEnd: return L.T("Fine scelta", "End pick");
                case StepType.SetVar: return VarOpText(s);
                case StepType.Combo: return L.T("Azioni insieme", "Do together");
                default: return L.T("Ferma la macro", "Stop the macro");
            }
        }

        static readonly string[] CmpSymbols = { "=", "≠", "<", "≤", ">", "≥" };

        public static string CmpSymbol(int op) { return CmpSymbols[Math.Min(5, Math.Max(0, op))]; }

        // "contatore > 3"
        public static string VarCondText(MacroStep s)
        {
            string n = string.IsNullOrEmpty(s.VarName) ? "?" : s.VarName;
            return n + " " + CmpSymbol(s.CmpOp) + " " + s.CmpValue;
        }

        // "  ·  +2 condizioni (OR)": le condizioni aggiuntive di un Se / Ripeti fino a / Attendi colore
        public static string CondSuffix(MacroStep s)
        {
            int n = s.ExtraCount;
            if (n == 0 || !s.ConditionActive) return "";
            return "  ·  +" + n + L.T(n == 1 ? " condizione" : " condizioni", n == 1 ? " condition" : " conditions")
                + " (" + (s.CondJoin == 1 ? "AND" : "OR") + ")";
        }

        // Versione corta per le righe della lista: "  ·  +2 OR"
        public static string CondShort(MacroStep s)
        {
            int n = s.ExtraCount;
            if (n == 0 || !s.ConditionActive) return "";
            return "  ·  +" + n + " " + (s.CondJoin == 1 ? "AND" : "OR");
        }

        // Testo breve di una clausola, per i log: "#1FA84A presente @ (10, 20)", "giri ≥ 5", "30%"
        public static string ClauseText(CondClause c)
        {
            if (c.Kind == CondClause.Variable)
                return (string.IsNullOrEmpty(c.VarName) ? "?" : c.VarName) + " " + CmpSymbol(c.CmpOp) + " " + c.CmpValue;
            if (c.Kind == CondClause.Chance)
                return L.T("a caso ", "random ") + c.ChancePct + "%";
            return "#" + (c.Input ?? "").ToUpperInvariant() + (c.Negate ? L.T(" NON presente", " NOT present") : L.T(" presente", " present"))
                + " @ (" + c.MoveX + ", " + c.MoveY + ")";
        }

        // "x = 5", "x + 1", "x − 2", "x = a caso 1..6"
        public static string VarOpText(MacroStep s)
        {
            string n = string.IsNullOrEmpty(s.VarName) ? "?" : s.VarName;
            switch (s.VarOp)
            {
                case 1: return n + " + " + s.CmpValue;
                case 2: return n + " − " + s.CmpValue;
                case 3: return n + " = " + L.T("a caso ", "random ") + Math.Min(s.CmpValue, s.VarValue2) + ".." + Math.Max(s.CmpValue, s.VarValue2);
                default: return n + " = " + s.CmpValue;
            }
        }

        // "tasti · click · mouse · mira"
        public static string ComboSummary(MacroStep s)
        {
            if (s.Parts == null) return "";
            List<string> names = new List<string>();
            string[] it = { "tasti", "click", "mouse", "mira" }, en = { "keys", "click", "mouse", "aim" };
            for (int i = 0; i < s.Parts.Length && i < 4; i++)
                if (s.Parts[i] != null && s.Parts[i].Enabled) names.Add(L.T(it[i], en[i]) + (s.Parts[i].Chance < 100 ? " " + s.Parts[i].Chance + "%" : ""));
            if (names.Count == 0) return L.T("nessuna azione attiva", "no active action");
            string txt = string.Join(" + ", names.ToArray());
            if (s.PickCount > 0) txt = L.T("a caso " + s.PickCount + " tra: ", "random " + s.PickCount + " of: ") + txt;
            return txt;
        }

        // Descrizione; swatch = colore da mostrare accanto al testo, se presente.
        public static string Details(MacroStep s, MacroStep header, out Color swatch)
        {
            swatch = Color.Empty;
            Color c;
            string d = "";
            switch (s.Type)
            {
                case StepType.Keys:
                    d = s.RandDur
                        ? L.T("tieni premuto ", "hold ") + RandInfo(s.DurMin, s.DurMax, s.DurLongPct, s.DurLongMax)
                        : L.T("tieni premuto per ", "hold for ") + Fmt(s.DurationMs) + (s.JitterMs > 0 ? " (± " + Fmt(s.JitterMs) + ")" : "");
                    break;
                case StepType.RandomKey:
                    d = L.T("uno tra: ", "one of: ") + Shorten(s.Input, 40) + (s.RandDur ? "  -  " + RandInfo(s.DurMin, s.DurMax, s.DurLongPct, s.DurLongMax) : "");
                    break;
                case StepType.Click:
                    d = L.T("pulsante ", "button ") + L.Button(s.Input) + ", " + (s.RandDur ? RandInfo(s.DurMin, s.DurMax, s.DurLongPct, s.DurLongMax) : Fmt(s.DurationMs));
                    break;
                case StepType.ClickAt:
                    d = L.T("pulsante ", "button ") + L.Button(s.Input) + L.T(" in (", " at (") + s.MoveX + ", " + s.MoveY + ")"
                        + (s.PosJitter > 0 ? " ± " + s.PosJitter + " px" : "");
                    break;
                case StepType.MoveTo:
                    d = L.T("porta il cursore su (", "move the cursor to (") + s.MoveX + ", " + s.MoveY + ")" + (s.PosJitter > 0 ? " ± " + s.PosJitter + " px" : "");
                    break;
                case StepType.MouseMove:
                    d = (s.RandMove ? L.T("casuale entro ± (", "random within ± (") : L.T("di (", "by (")) + s.MoveX + ", " + s.MoveY + L.T(") px in ", ") px in ")
                        + (s.RandDur ? RandInfo(s.DurMin, s.DurMax, s.DurLongPct, s.DurLongMax) : Fmt(s.DurationMs));
                    break;
                case StepType.Wait:
                    d = s.RandDur ? RandInfo(s.DurMin, s.DurMax, s.DurLongPct, s.DurLongMax) : (string.IsNullOrEmpty(s.Name) ? "" : Fmt(s.DurationMs));
                    break;
                case StepType.WaitPixel:
                    if (ScreenPixel.TryParseHex(s.Input, out c)) swatch = c;
                    return "#" + s.Input + L.T(" su ", " on ") + Area(s) + CondSuffix(s) + "  -  max " + Fmt(s.DurationMs);
                case StepType.WaitPixelGone:
                    if (ScreenPixel.TryParseHex(s.Input, out c)) swatch = c;
                    return "#" + s.Input + L.T(" deve sparire da ", " must disappear from ") + Area(s) + CondSuffix(s) + "  -  max " + Fmt(s.DurationMs);
                case StepType.IfStart:
                    if (s.CondKind == 1) return (s.Negate ? L.T("NON ", "NOT ") : "") + VarCondText(s) + CondSuffix(s);
                    if (s.CondKind == 2) return L.T("a caso, ", "random, ") + s.ChancePct + L.T("% delle volte", "% of the time") + (s.Negate ? L.T(" (invertito)", " (inverted)") : "") + CondSuffix(s);
                    if (ScreenPixel.TryParseHex(s.Input, out c)) swatch = c;
                    return "#" + s.Input + (s.Negate ? L.T(" NON è presente su ", " is NOT present on ") : L.T(" è presente su ", " is present on ")) + Area(s) + CondSuffix(s);
                case StepType.ChoiceStart:
                    return L.T("una sola alternativa, a caso", "only one alternative, at random");
                case StepType.Option:
                    return L.T("peso ", "weight ") + s.Weight;
                case StepType.SetVar:
                    return L.T("variabile", "variable");
                case StepType.Combo:
                    d = ComboSummary(s);
                    break;
                case StepType.LoopStart:
                    {
                        if (!s.UseUntil && s.Count > 0 && s.DurationMs == 0) return L.T(s.Count + " volte", s.Count + " times");
                        List<string> parts = new List<string>();
                        if (s.UseUntil)
                        {
                            if (ScreenPixel.TryParseHex(s.Input, out c)) swatch = c;
                            parts.Add(L.T("esci quando #", "exit when #") + s.Input
                                + (s.Negate ? L.T(" NON è presente su ", " is NOT present on ") : L.T(" è presente su ", " is present on ")) + Area(s)
                                + CondSuffix(s) + (s.CheckFirst ? L.T(" (controlla anche all'inizio)", " (also checked at the start)") : ""));
                        }
                        if (s.Count > 0) parts.Add("max " + s.Count + L.T(" giri", " rounds"));
                        if (s.DurationMs > 0) parts.Add("max " + Fmt(s.DurationMs));
                        if (parts.Count == 0) return L.T("senza limite: esci con 'Esci dal ciclo' o con Stop", "no limit: leave with 'Exit loop' or with Stop");
                        return string.Join("  -  ", parts.ToArray());
                    }
                case StepType.LoopEnd:
                    if (header != null && header.UseUntil) return L.T("se non si può uscire, ricomincia dal primo passo", "if it can't exit, starts again from the first step");
                    return L.T("torna al primo passo del ciclo", "goes back to the first step of the loop");
                case StepType.Break:
                    return L.T("interrompe il ciclo più interno", "ends the innermost loop");
                case StepType.StopMacro:
                    return L.T("ferma tutto", "stops everything");
                case StepType.Label:
                    return L.T("punto di arrivo dei Goto", "target point for Goto steps");
                case StepType.Goto:
                    return L.T("salta direttamente all'etichetta '" + s.Input + "'", "jumps straight to the label '" + s.Input + "'");
                default:
                    return "";
            }
            if (s.RandPause && s.HasPause)
                d += (d.Length > 0 ? "  -  " : "") + L.T("pausa ", "pause ") + RandInfo(s.PauseMin, s.PauseMax, s.PauseLongPct, s.PauseLongMax);
            return d;
        }

        // Descrizione completa e leggibile di un ciclo, usata nei log.
        public static string LoopLog(MacroStep s)
        {
            if (!s.UseUntil && s.DurationMs == 0)
                return s.Count > 0
                    ? L.T("Ripeti " + s.Count + " volte", "Repeat " + s.Count + " times")
                    : L.T("Ripeti all'infinito (esci con 'Esci dal ciclo' o con Stop)", "Repeat forever (leave with 'Exit loop' or with Stop)");
            Color dummy;
            return L.T("Ripeti: ", "Repeat: ") + Details(s, null, out dummy);
        }
    }

    public static class Flow
    {
        // Calcola gli indici di chiusura dei blocchi (Ripeti / Se) e verifica che siano ben formati.
        public static string Prepare(List<MacroStep> steps)
        {
            foreach (MacroStep s in steps) { s.Match = -1; s.ElseMatch = -1; s.OptionMarks = null; }
            List<int> stack = new List<int>();
            // "Frame" = tratto di passi eseguito in un colpo solo: lista principale, corpo di un ciclo, ramo "allora" o "altrimenti".
            // Un Goto può raggiungere solo etichette del proprio frame o di un frame che lo contiene.
            List<int> frameParent = new List<int>();
            frameParent.Add(-1);
            List<int> frameStack = new List<int>();
            frameStack.Add(0);
            int[] frameOf = new int[steps.Count];
            for (int i = 0; i < steps.Count; i++)
            {
                MacroStep s = steps[i];
                frameOf[i] = frameStack[frameStack.Count - 1];
                switch (s.Type)
                {
                    case StepType.LoopStart:
                    case StepType.IfStart:
                    case StepType.ChoiceStart:
                        stack.Add(i);
                        frameParent.Add(frameStack[frameStack.Count - 1]);
                        frameStack.Add(frameParent.Count - 1);
                        break;
                    case StepType.Option:
                        {
                            if (stack.Count == 0 || steps[stack[stack.Count - 1]].Type != StepType.ChoiceStart)
                                return L.T("Passo " + (i + 1) + ": 'Opzione' deve stare dentro un 'Scegli a caso'.", "Step " + (i + 1) + ": 'Option' must be inside a 'Pick at random'.");
                            MacroStep top = steps[stack[stack.Count - 1]];
                            if (top.OptionMarks == null) top.OptionMarks = new List<int>();
                            top.OptionMarks.Add(i);
                            if (frameStack.Count > 1)
                            {
                                int prevFrame = frameStack[frameStack.Count - 1];
                                frameStack.RemoveAt(frameStack.Count - 1);
                                frameOf[i] = frameStack[frameStack.Count - 1];
                                frameParent.Add(frameParent[prevFrame]);
                                frameStack.Add(frameParent.Count - 1);
                            }
                            break;
                        }
                    case StepType.ChoiceEnd:
                        {
                            if (stack.Count == 0 || steps[stack[stack.Count - 1]].Type != StepType.ChoiceStart)
                                return L.T("Passo " + (i + 1) + ": 'Fine scelta' senza uno 'Scegli a caso' corrispondente.", "Step " + (i + 1) + ": 'End pick' without a matching 'Pick at random'.");
                            steps[stack[stack.Count - 1]].Match = i;
                            stack.RemoveAt(stack.Count - 1);
                            if (frameStack.Count > 1) frameStack.RemoveAt(frameStack.Count - 1);
                            frameOf[i] = frameStack[frameStack.Count - 1];
                            break;
                        }
                    case StepType.Else:
                        {
                            if (stack.Count == 0 || steps[stack[stack.Count - 1]].Type != StepType.IfStart)
                                return L.T("Passo " + (i + 1) + ": 'Altrimenti' deve stare dentro un 'Se'.", "Step " + (i + 1) + ": 'Else' must be inside an 'If'.");
                            MacroStep top = steps[stack[stack.Count - 1]];
                            if (top.ElseMatch >= 0)
                                return L.T("Passo " + (i + 1) + ": c'è già un 'Altrimenti' in questo 'Se'.", "Step " + (i + 1) + ": this 'If' already has an 'Else'.");
                            top.ElseMatch = i;
                            if (frameStack.Count > 1)
                            {
                                int thenFrame = frameStack[frameStack.Count - 1];
                                frameStack.RemoveAt(frameStack.Count - 1);
                                frameOf[i] = frameStack[frameStack.Count - 1];
                                frameParent.Add(frameParent[thenFrame]);
                                frameStack.Add(frameParent.Count - 1);
                            }
                            break;
                        }
                    case StepType.LoopEnd:
                        {
                            if (stack.Count == 0 || steps[stack[stack.Count - 1]].Type != StepType.LoopStart)
                                return L.T("Passo " + (i + 1) + ": 'Fine ripeti' senza un 'Ripeti' corrispondente.", "Step " + (i + 1) + ": 'End repeat' without a matching 'Repeat'.");
                            steps[stack[stack.Count - 1]].Match = i;
                            stack.RemoveAt(stack.Count - 1);
                            if (frameStack.Count > 1) frameStack.RemoveAt(frameStack.Count - 1);
                            frameOf[i] = frameStack[frameStack.Count - 1];
                            break;
                        }
                    case StepType.IfEnd:
                        {
                            if (stack.Count == 0 || steps[stack[stack.Count - 1]].Type != StepType.IfStart)
                                return L.T("Passo " + (i + 1) + ": 'Fine se' senza un 'Se' corrispondente.", "Step " + (i + 1) + ": 'End if' without a matching 'If'.");
                            steps[stack[stack.Count - 1]].Match = i;
                            stack.RemoveAt(stack.Count - 1);
                            if (frameStack.Count > 1) frameStack.RemoveAt(frameStack.Count - 1);
                            frameOf[i] = frameStack[frameStack.Count - 1];
                            break;
                        }
                    case StepType.Break:
                        {
                            bool inLoop = false;
                            foreach (int k in stack) if (steps[k].Type == StepType.LoopStart) inLoop = true;
                            if (!inLoop)
                                return L.T("Passo " + (i + 1) + ": 'Esci dal ciclo' deve stare dentro un 'Ripeti'.", "Step " + (i + 1) + ": 'Exit loop' must be inside a 'Repeat'.");
                            break;
                        }
                }
            }
            if (stack.Count > 0)
            {
                MacroStep open = steps[stack[stack.Count - 1]];
                int n = stack[stack.Count - 1] + 1;
                bool loop = open.Type == StepType.LoopStart;
                bool pick = open.Type == StepType.ChoiceStart;
                return L.T("Passo " + n + ": manca la chiusura ('" + (loop ? "Fine ripeti" : (pick ? "Fine scelta" : "Fine se")) + "').",
                           "Step " + n + ": the closing step is missing ('" + (loop ? "End repeat" : (pick ? "End pick" : "End if")) + "').");
            }

            // Etichette e Goto
            Dictionary<string, int> labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].Type != StepType.Label) continue;
                string nm = (steps[i].Name ?? "").Trim();
                if (nm.Length == 0)
                    return L.T("Passo " + (i + 1) + ": l'etichetta non ha un nome.", "Step " + (i + 1) + ": the label has no name.");
                int prev;
                if (labels.TryGetValue(nm, out prev))
                    return L.T("Passo " + (i + 1) + ": etichetta '" + nm + "' duplicata (già al passo " + (prev + 1) + ").",
                               "Step " + (i + 1) + ": duplicate label '" + nm + "' (already at step " + (prev + 1) + ").");
                labels[nm] = i;
            }
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].Type != StepType.Goto) continue;
                string target = (steps[i].Input ?? "").Trim();
                int li;
                if (!labels.TryGetValue(target, out li))
                    return L.T("Passo " + (i + 1) + ": il Goto punta all'etichetta '" + target + "' che non esiste.",
                               "Step " + (i + 1) + ": the Goto points to the label '" + target + "' which doesn't exist.");
                bool reachable = false;
                for (int f = frameOf[i]; f >= 0; f = frameParent[f])
                    if (f == frameOf[li]) { reachable = true; break; }
                if (!reachable)
                    return L.T("Passo " + (i + 1) + ": il Goto non può saltare dentro un altro blocco (etichetta '" + target + "' al passo " + (li + 1) + ").",
                               "Step " + (i + 1) + ": the Goto can't jump into another block (label '" + target + "' at step " + (li + 1) + ").");
                steps[i].Match = li;
            }
            return null;
        }

        // Elementi di primo livello: un passo semplice oppure un intero blocco Ripeti/Se.
        public static List<int[]> TopLevelItems(List<MacroStep> steps)
        {
            List<int[]> items = new List<int[]>();
            int i = 0;
            while (i < steps.Count)
            {
                MacroStep s = steps[i];
                if (s.IsBlockHeader && s.Match > i)
                {
                    items.Add(new int[] { i, s.Match });
                    i = s.Match + 1;
                }
                else
                {
                    if (s.Type != StepType.LoopEnd && s.Type != StepType.IfEnd && s.Type != StepType.Else
                        && s.Type != StepType.ChoiceEnd && s.Type != StepType.Option)
                        items.Add(new int[] { i, i });
                    i++;
                }
            }
            return items;
        }

        // Alternative di un blocco "Scegli a caso" (richiede Flow.Prepare già eseguito): {da, a (escluso), peso}.
        // Con i passi "Opzione" sono i tratti tra un'opzione e l'altra; senza, ogni passo/blocco diretto è un'alternativa.
        public static List<int[]> ChoiceRanges(List<MacroStep> steps, int header)
        {
            List<int[]> r = new List<int[]>();
            if (header < 0 || header >= steps.Count) return r;
            MacroStep s = steps[header];
            int end = s.Match;
            if (end <= header) return r;
            if (s.OptionMarks != null && s.OptionMarks.Count > 0)
            {
                int from = header + 1;
                int w = Math.Max(1, s.AltWeight);
                foreach (int m in s.OptionMarks)
                {
                    r.Add(new int[] { from, m, w });
                    from = m + 1;
                    w = Math.Max(1, steps[m].Weight);
                }
                r.Add(new int[] { from, end, w });
            }
            else
            {
                int i = header + 1;
                while (i < end)
                {
                    MacroStep c = steps[i];
                    int e = i;
                    if (c.IsBlockHeader && c.Match > i) e = c.Match;
                    r.Add(new int[] { i, e + 1, Math.Max(1, c.Weight) });
                    i = e + 1;
                }
            }
            return r;
        }
    }

    [DataContract]
    public class Profile
    {
        [DataMember] public RunMode Mode;
        [DataMember] public int StartDelaySec = 3;
        [DataMember] public int Cycles;
        [DataMember] public string Hotkey = "F8";
        [DataMember] public string PauseHotkey = "F7";
        [DataMember] public string WindowTitle = "";
        [DataMember] public List<MacroStep> Steps = new List<MacroStep>();

        [DataMember] public bool AutoRestart;
        [DataMember] public bool StartWithMenu;
        [DataMember] public int GameSeconds = 180;
        [DataMember] public bool EndPixelEnabled;
        [DataMember] public int EndX;
        [DataMember] public int EndY;
        [DataMember] public int EndW = 1;
        [DataMember] public int EndH = 1;
        [DataMember] public string EndColor = "000000";
        [DataMember] public int EndTolerance = 12;
        // Altri colori che segnano la fine della partita (stessa area e tolleranza), separati da virgola: basta uno qualsiasi (OR).
        [DataMember(EmitDefaultValue = false)] public string EndExtra;
        [DataMember] public List<MacroStep> MenuSteps = new List<MacroStep>();

        public Profile Clone()
        {
            Profile p = (Profile)MemberwiseClone();
            p.Steps = new List<MacroStep>();
            foreach (MacroStep s in Steps) p.Steps.Add(s.Clone());
            p.MenuSteps = new List<MacroStep>();
            foreach (MacroStep s in MenuSteps) p.MenuSteps.Add(s.Clone());
            return p;
        }

        public static Profile Default()
        {
            Profile p = new Profile();
            p.Mode = RunMode.Sync;
            MacroStep fwd = MacroStep.MakeKeys("W", 1500, 100, 300, 5).Named(L.T("Avanti", "Forward"));
            fwd.RandDur = true; fwd.DurMin = 1000; fwd.DurMax = 2500; fwd.DurLongPct = 15; fwd.DurLongMax = 6000;
            p.Steps.Add(fwd);
            p.Steps.Add(MacroStep.MakeMove(250, 0, 350, 150, 100, 2).Named(L.T("Guarda a destra", "Look right")));
            p.Steps.Add(MacroStep.MakeKeys("W+D", 700, 100, 200, 2).Named(L.T("Avanti a destra", "Forward right")));
            p.Steps.Add(MacroStep.MakeMove(-250, 0, 350, 150, 100, 2).Named(L.T("Guarda a sinistra", "Look left")));
            p.Steps.Add(MacroStep.MakeKeys("W+A", 700, 100, 200, 2).Named(L.T("Avanti a sinistra", "Forward left")));
            p.Steps.Add(MacroStep.MakeKeys("Space", 80, 400, 150, 1).Named(L.T("Salto", "Jump")));
            return p;
        }

        static void CleanList(List<MacroStep> list)
        {
            list.RemoveAll(delegate(MacroStep s) { return s == null; });
            foreach (MacroStep s in list) s.Sanitize();
        }

        public static Profile Load(string path)
        {
            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(Profile));
            Profile p;
            using (FileStream fs = File.OpenRead(path)) p = (Profile)ser.ReadObject(fs);
            if (p == null) throw new InvalidDataException(L.T("File vuoto.", "Empty file."));
            if (p.Steps == null) p.Steps = new List<MacroStep>();
            if (p.MenuSteps == null) p.MenuSteps = new List<MacroStep>();
            CleanList(p.Steps);
            CleanList(p.MenuSteps);
            if (string.IsNullOrEmpty(p.Hotkey)) p.Hotkey = "F8";
            if (string.IsNullOrEmpty(p.PauseHotkey)) p.PauseHotkey = "F7";
            if (p.WindowTitle == null) p.WindowTitle = "";
            Color c;
            if (!ScreenPixel.TryParseHex(p.EndColor, out c)) p.EndColor = "000000";
            p.EndExtra = ScreenPixel.NormalizeColorList(p.EndExtra);
            p.StartDelaySec = Math.Min(60, Math.Max(0, p.StartDelaySec));
            p.Cycles = Math.Max(0, p.Cycles);
            p.GameSeconds = Math.Max(0, p.GameSeconds);
            p.EndTolerance = Math.Min(255, Math.Max(0, p.EndTolerance));
            p.EndW = Math.Max(1, p.EndW);
            p.EndH = Math.Max(1, p.EndH);
            if (!Enum.IsDefined(typeof(RunMode), p.Mode)) p.Mode = RunMode.Sync;
            return p;
        }

        public void Save(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(Profile));
            using (FileStream fs = File.Create(path))
            using (XmlDictionaryWriter w = JsonReaderWriterFactory.CreateJsonWriter(fs, Encoding.UTF8, false, true))
            {
                ser.WriteObject(w, this);
                w.Flush();
            }
        }
    }

    // Stima della durata di una passata della sequenza (solo per la visualizzazione).
    // I cicli senza numero di giri contano un solo giro; un "Se" conta il ramo più lungo; le attese a colore non contano.
    public static class Estimator
    {
        public static long CycleMs(List<MacroStep> steps)
        {
            if (steps == null || steps.Count == 0) return 0;
            if (Flow.Prepare(steps) != null) return 0;
            return Range(steps, 0, steps.Count);
        }

        // Durata di un solo giro del corpo di un ciclo (richiede Flow.Prepare già eseguito).
        public static long BodyMs(List<MacroStep> steps, int header)
        {
            if (header < 0 || header >= steps.Count || steps[header].Match <= header) return 0;
            return Range(steps, header + 1, steps[header].Match);
        }

        static long Range(List<MacroStep> steps, int from, int to)
        {
            long total = 0;
            int i = from;
            while (i < to)
            {
                MacroStep s = steps[i];
                if (s.Type == StepType.LoopStart && s.Match > i)
                {
                    long body = Range(steps, i + 1, s.Match);
                    long n = s.Count > 0 ? s.Count : 1;
                    long t = body * n;
                    if (s.Count == 0 && s.DurationMs > 0) t = Math.Min(t, s.DurationMs);
                    total += t;
                    i = s.Match + 1;
                }
                else if (s.Type == StepType.ChoiceStart && s.Match > i)
                {
                    // media pesata delle alternative
                    List<int[]> alts = Flow.ChoiceRanges(steps, i);
                    double sum = 0, wsum = 0;
                    foreach (int[] a in alts) { sum += Range(steps, a[0], a[1]) * (double)a[2]; wsum += a[2]; }
                    total += wsum > 0 ? (long)(sum / wsum) : 0;
                    i = s.Match + 1;
                }
                else if (s.Type == StepType.IfStart && s.Match > i)
                {
                    int e = s.ElseMatch;
                    long a = Range(steps, i + 1, e >= 0 ? e : s.Match);
                    long b = e >= 0 ? Range(steps, e + 1, s.Match) : 0;
                    total += Math.Max(a, b);
                    i = s.Match + 1;
                }
                else
                {
                    total += StepMs(s);
                    i++;
                }
            }
            return total;
        }

        static long Avg(int min, int max, int longPct, int longMax)
        {
            long v = (min + Math.Max(min, max)) / 2;
            if (longPct > 0 && longMax > max) v += (long)((longMax - max) / 2.0 * longPct / 100.0);
            return v;
        }

        public static long StepMs(MacroStep s)
        {
            long dur = 0;
            switch (s.Type)
            {
                case StepType.Keys: case StepType.RandomKey: case StepType.MouseMove: case StepType.Wait:
                    dur = s.RandDur ? Avg(s.DurMin, s.DurMax, s.DurLongPct, s.DurLongMax) : s.DurationMs;
                    break;
                case StepType.Click:
                    dur = Math.Max(20, s.RandDur ? Avg(s.DurMin, s.DurMax, s.DurLongPct, s.DurLongMax) : s.DurationMs);
                    break;
                case StepType.ClickAt:
                    dur = 80 + Math.Max(40, s.RandDur ? Avg(s.DurMin, s.DurMax, s.DurLongPct, s.DurLongMax) : s.DurationMs);
                    break;
                case StepType.MoveTo:
                    dur = 60;
                    break;
                case StepType.Combo:
                    // le parti partono insieme: conta la più lunga (durata media)
                    if (s.Parts != null)
                        foreach (ComboPart p in s.Parts)
                            if (p != null && p.Enabled) dur = Math.Max(dur, Avg(p.DurMin, p.DurMax, p.LongPct, p.LongMax));
                    break;
            }
            long pause = 0;
            if (s.HasPause) pause = s.RandPause ? Avg(s.PauseMin, s.PauseMax, s.PauseLongPct, s.PauseLongMax) : s.PauseMs;
            return dur + Math.Max(1, pause);
        }
    }

}
