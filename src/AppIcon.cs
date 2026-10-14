using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace StepLoop
{
    // Icona dell'app: è incorporata nell'exe come risorsa "StepLoop.ico" (vedi build.bat) e dà l'icona al file,
    // alla barra del titolo e alla barra delle applicazioni.
    public static class AppIcon
    {
        static Icon cached;
        static bool tried;

        public static Icon Get()
        {
            if (tried) return cached;
            tried = true;
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("StepLoop.ico"))
                {
                    if (s != null) cached = new Icon(s);
                }
            }
            catch (Exception) { cached = null; }
            return cached;
        }
    }
}
