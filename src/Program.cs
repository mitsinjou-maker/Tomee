using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;


namespace Tomee
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Coordonnées en pixels physiques sur chaque écran (indispensable pour suivre le curseur avec précision).
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException)
            {
                try { Native.SetProcessDpiAwareness(2); } catch (Exception) { }
            }

            bool created;
            using (var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, AppInfo.ShowSettingsEvent, out created))
            using (var exitRequest = new EventWaitHandle(false, EventResetMode.AutoReset, AppInfo.ExitEvent))
            {
                if (!created)
                {
                    // déjà lancé : on demande à l'instance existante d'ouvrir ses paramètres
                    Native.AllowSetForegroundWindow(-1);
                    showSettings.Set();
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);
                Application.Run(new TrayApp(showSettings, exitRequest));
            }
        }

        public static void Log(Exception ex)
        {
            if (ex == null) return;
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                File.AppendAllText(Path.Combine(Settings.Folder, "erreurs.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }
    }
}
