using System;
using System.Collections.Generic;

namespace StepLoop
{
    // Risoluzioni comuni per i menu a tendina della conversione.
    public static class ResolutionPresets
    {
        public sealed class Preset
        {
            public int W, H;
            public string Label;
        }

        public static readonly Preset[] All = new Preset[]
        {
            P(3840, 2160, "4K UHD · 16:9"),
            P(2560, 1440, "QHD 1440p · 16:9"),
            P(1920, 1080, "Full HD 1080p · 16:9"),
            P(1600, 900, "HD+ 900p · 16:9"),
            P(1366, 768, "HD 768p · 16:9"),
            P(1280, 720, "HD 720p · 16:9"),
            P(5120, 1440, "Super ultrawide · 32:9"),
            P(3440, 1440, "Ultrawide · 21:9"),
            P(2560, 1080, "Ultrawide · 21:9"),
            P(3840, 1600, "Ultrawide · 24:10"),
            P(2560, 1600, "WQXGA · 16:10"),
            P(1920, 1200, "WUXGA · 16:10"),
            P(1680, 1050, "WSXGA+ · 16:10"),
            P(1440, 900, "WXGA+ · 16:10"),
            P(1280, 1024, "SXGA · 5:4"),
            P(1024, 768, "XGA · 4:3")
        };

        static Preset P(int w, int h, string label)
        {
            Preset p = new Preset();
            p.W = w; p.H = h; p.Label = label;
            return p;
        }

        // Testo per il menu a tendina: "1920 × 1080 (Full HD 1080p · 16:9)"
        public static string Text(Preset p) { return p.W + " × " + p.H + "  (" + p.Label + ")"; }

        // Indice della risoluzione nell'elenco, oppure -1 se non è tra quelle comuni.
        public static int IndexOf(int w, int h)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].W == w && All[i].H == h) return i;
            return -1;
        }
    }

    // Converte le coordinate di un profilo da una risoluzione dello schermo a un'altra.
    // Cosa viene scalato: le posizioni (clic, "vai a", aree dei colori di Se / Ripeti fino a / Attendi e delle loro condizioni
    // aggiuntive), la dimensione delle aree, il raggio della variazione casuale dei clic e l'area del colore di fine partita.
    // Cosa NON cambia: durate, tolleranze, colori e i movimenti RELATIVI del mouse (quanto gira la visuale non dipende
    // dalla risoluzione dello schermo ma dalla sensibilità del gioco).
    public static class ResolutionScaler
    {
        public sealed class Result
        {
            public int Points;      // posizioni di clic / "vai a"
            public int Areas;       // aree di colore (condizioni, attese, uscite dai cicli)
            public int EndArea;     // 1 se c'è l'area del colore di fine partita
            public int Total { get { return Points + Areas + EndArea; } }
        }

        static int Pos(int v, double f, int shift)
        {
            return (int)Math.Round(v * f, MidpointRounding.AwayFromZero) + shift;
        }

        static int Size(int v, double f)
        {
            return Math.Max(1, (int)Math.Round(v * f, MidpointRounding.AwayFromZero));
        }

        // Scala il profilo in place (o, con dryRun, conta soltanto cosa verrebbe cambiato).
        // fx, fy: rapporto tra le larghezze e tra le altezze (nuova / vecchia); shiftX, shiftY: spostamento dopo la scala
        // (per un gioco in finestra che non parte da (0, 0)).
        public static Result Apply(Profile p, double fx, double fy, int shiftX, int shiftY, bool dryRun)
        {
            Result r = new Result();
            if (p == null || fx <= 0 || fy <= 0 || double.IsNaN(fx) || double.IsNaN(fy) || double.IsInfinity(fx) || double.IsInfinity(fy)) return r;
            ApplyList(p.Steps, fx, fy, shiftX, shiftY, dryRun, r);
            ApplyList(p.MenuSteps, fx, fy, shiftX, shiftY, dryRun, r);
            if (p.EndPixelEnabled) r.EndArea = 1;
            if (!dryRun)
            {
                p.EndX = Pos(p.EndX, fx, shiftX);
                p.EndY = Pos(p.EndY, fy, shiftY);
                p.EndW = Size(p.EndW, fx);
                p.EndH = Size(p.EndH, fy);
            }
            return r;
        }

        static void ApplyList(List<MacroStep> steps, double fx, double fy, int sx, int sy, bool dry, Result r)
        {
            if (steps == null) return;
            double fj = (fx + fy) / 2.0;
            foreach (MacroStep s in steps)
            {
                if (s == null) continue;
                switch (s.Type)
                {
                    case StepType.ClickAt:
                    case StepType.MoveTo:
                        r.Points++;
                        if (!dry)
                        {
                            s.MoveX = Pos(s.MoveX, fx, sx);
                            s.MoveY = Pos(s.MoveY, fy, sy);
                            s.PosJitter = s.PosJitter <= 0 ? 0 : Math.Max(1, (int)Math.Round(s.PosJitter * fj, MidpointRounding.AwayFromZero));
                        }
                        break;
                    case StepType.WaitPixel:
                    case StepType.WaitPixelGone:
                    case StepType.IfStart:
                    case StepType.LoopStart:
                        // i valori vengono scalati comunque (anche se la condizione oggi non usa il colore): se la si riattiva sono già giusti
                        if (s.ConditionActive && (s.Type != StepType.IfStart || s.CondKind == 0)) r.Areas++;
                        if (!dry)
                        {
                            s.MoveX = Pos(s.MoveX, fx, sx);
                            s.MoveY = Pos(s.MoveY, fy, sy);
                            s.Width = Size(s.Width, fx);
                            s.Height = Size(s.Height, fy);
                        }
                        if (s.Extra != null)
                            foreach (CondClause c in s.Extra)
                            {
                                if (c == null) continue;
                                if (s.ConditionActive && c.Kind == CondClause.Color) r.Areas++;
                                if (!dry)
                                {
                                    c.MoveX = Pos(c.MoveX, fx, sx);
                                    c.MoveY = Pos(c.MoveY, fy, sy);
                                    c.Width = Size(c.Width, fx);
                                    c.Height = Size(c.Height, fy);
                                }
                            }
                        break;
                }
            }
        }
    }
}
