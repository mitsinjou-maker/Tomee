[assembly: System.Reflection.AssemblyTitle(Tomee.AppInfo.Name)]
[assembly: System.Reflection.AssemblyProduct(Tomee.AppInfo.Name)]
[assembly: System.Reflection.AssemblyDescription("Mise en valeur du curseur pour réunions, présentations et formations")]
[assembly: System.Reflection.AssemblyVersion(Tomee.AppInfo.Version + ".0")]
[assembly: System.Reflection.AssemblyFileVersion(Tomee.AppInfo.Version + ".0")]

namespace Tomee
{
    /// <summary>Nom et version, partagés par l'application et l'installateur.</summary>
    internal static class AppInfo
    {
        public const string Name = "Tomee";
        public const string Version = "1.0.0";
        public const string ExeName = "Tomee.exe";

        /// <summary>Évènement nommé : demande à l'instance en cours d'ouvrir ses paramètres.</summary>
        public const string ShowSettingsEvent = @"Local\Tomee.ShowSettings";
        /// <summary>Évènement nommé : demande à l'instance en cours de se fermer (utilisé par l'installateur).</summary>
        public const string ExitEvent = @"Local\Tomee.Exit";
    }
}
