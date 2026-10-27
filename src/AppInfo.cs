[assembly: System.Reflection.AssemblyTitle(Tomee.AppInfo.Name)]
[assembly: System.Reflection.AssemblyProduct(Tomee.AppInfo.Name)]
[assembly: System.Reflection.AssemblyDescription("Mise en valeur du curseur pour réunions, présentations et formations")]
[assembly: System.Reflection.AssemblyVersion(Tomee.AppInfo.Version + ".0")]
[assembly: System.Reflection.AssemblyFileVersion(Tomee.AppInfo.Version + ".0")]
// Les DLL natives (Magnification, dwmapi, winmm…) sont chargées uniquement depuis System32,
// jamais depuis le dossier de l'exécutable (protection contre le « DLL planting »).
[assembly: System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]

namespace Tomee
{
    /// <summary>Nom et version, partagés par l'application et l'installateur.</summary>
    internal static class AppInfo
    {
        public const string Name = "Tomee";
        public const string Version = "1.0.1";
        public const string ExeName = "Tomee.exe";

        /// <summary>Évènement nommé : demande à l'instance en cours d'ouvrir ses paramètres.</summary>
        public const string ShowSettingsEvent = @"Local\Tomee.ShowSettings";
        /// <summary>Évènement nommé : demande à l'instance en cours de se fermer (utilisé par l'installateur).</summary>
        public const string ExitEvent = @"Local\Tomee.Exit";

        /// <summary>Vrai si le processus est bien Tomee (et pas un autre programme portant le même nom).</summary>
        public static bool IsTomeeProcess(System.Diagnostics.Process p, string productName)
        {
            try
            {
                var info = p.MainModule.FileVersionInfo;
                return info.ProductName == productName && info.Comments != null && info.Comments.StartsWith("Mise en valeur du curseur");
            }
            catch (System.ComponentModel.Win32Exception) { return false; }
            catch (System.InvalidOperationException) { return false; }
        }
    }
}
