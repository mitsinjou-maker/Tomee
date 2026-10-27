using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using Microsoft.Win32;

namespace Tomee
{
    /// <summary>
    /// Réglages persistants. Chaque champ public est sauvegardé dans %APPDATA%\Projecteur\settings.ini.
    /// Les propriétés (Revision, Paused) ne sont pas persistées.
    /// </summary>
    internal sealed class Settings
    {
        // Projecteur (assombrit l'écran autour du curseur)
        public bool SpotlightEnabled = false;
        public int SpotlightRadius = 160;
        public int SpotlightDim = 65;       // % d'opacité du voile
        public int SpotlightFeather = 30;

        // Halo
        public bool RingEnabled = true;
        public Color RingColor = Color.FromArgb(255, 196, 0);
        public int RingOpacity = 90;
        public int RingRadius = 26;
        public int RingThickness = 4;
        public int RingFillOpacity = 20;
        public bool RingHideWithCursor = true;

        // Clics
        public bool ClicksEnabled = true;
        public Color LeftClickColor = Color.FromArgb(255, 72, 72);
        public Color RightClickColor = Color.FromArgb(64, 140, 255);
        public Color MiddleClickColor = Color.FromArgb(70, 200, 110);
        public int RippleRadius = 60;
        public int RippleDuration = 450;
        public bool ClickSound = false;
        public int ClickVolume = 50;

        // Traînée
        public bool TrailEnabled = false;
        public Color TrailColor = Color.FromArgb(255, 140, 0);
        public int TrailOpacity = 80;
        public int TrailLength = 350;
        public int TrailWidth = 8;

        // Touches
        public bool KeysEnabled = false;
        public bool KeysShortcutsOnly = true;
        public bool KeysShowMouse = true;
        public int KeysFontSize = 28;
        public int KeysPosition = 0;        // 0 bas-centre, 1 bas-gauche, 2 bas-droite, 3 haut-centre
        public int KeysDuration = 1600;

        // Zoom
        public int ZoomPercent = 200;
        public bool ZoomSmooth = true;

        // Dessin
        public Color DrawColor = Color.FromArgb(235, 40, 40);
        public int DrawWidth = 4;

        // Général
        public bool ShowToasts = true;
        public bool FirstRun = true;

        int revision;
        volatile bool paused;

        /// <summary>Incrémenté à chaque modification (lu par le thread principal pour appliquer les changements).</summary>
        public int Revision { get { return Thread.VolatileRead(ref revision); } }
        public void Touch() { Interlocked.Increment(ref revision); }

        public bool Paused { get { return paused; } set { paused = value; Touch(); } }

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Name); }
        }

        static string FilePath { get { return Path.Combine(Folder, "settings.ini"); } }

        static IEnumerable<FieldInfo> Fields()
        {
            return typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance);
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                string path = FilePath;
                // reprise des réglages de l'ancienne version (nommée « Projecteur »)
                string legacy = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Projecteur"), "settings.ini");
                if (!File.Exists(path)) path = legacy;
                if (!File.Exists(path)) return s;
                foreach (var line in File.ReadAllLines(path))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var f = typeof(Settings).GetField(line.Substring(0, eq).Trim());
                    if (f == null) continue;
                    var v = line.Substring(eq + 1).Trim();
                    try
                    {
                        if (f.FieldType == typeof(bool)) f.SetValue(s, bool.Parse(v));
                        // bornes larges : un fichier modifié à la main ne peut pas provoquer de débordement ni de plantage
                        else if (f.FieldType == typeof(int)) f.SetValue(s, Math.Max(0, Math.Min(10000, int.Parse(v, CultureInfo.InvariantCulture))));
                        else if (f.FieldType == typeof(Color))
                            f.SetValue(s, Color.FromArgb(unchecked((int)0xFF000000 | Convert.ToInt32(v.TrimStart('#'), 16))));
                    }
                    catch { }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                var lines = new List<string>();
                foreach (var f in Fields())
                {
                    object v = f.GetValue(this);
                    string text;
                    if (v is Color) { var c = (Color)v; text = string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B); }
                    else text = Convert.ToString(v, CultureInfo.InvariantCulture);
                    lines.Add(f.Name + "=" + text);
                }
                Directory.CreateDirectory(Folder);
                File.WriteAllLines(FilePath, lines);
            }
            catch { }
        }

        public void ResetToDefaults()
        {
            var d = new Settings();
            d.FirstRun = FirstRun;
            foreach (var f in Fields()) f.SetValue(this, f.GetValue(d));
            Touch();
        }

        public static Color WithOpacity(Color c, int percent)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, percent * 255 / 100)), c.R, c.G, c.B);
        }
    }

    internal static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = AppInfo.Name;

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                        return k != null && k.GetValue(Name) != null;
                }
                catch { return false; }
            }
        }

        public static void Set(bool enabled)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (enabled) k.SetValue(Name, "\"" + System.Windows.Forms.Application.ExecutablePath + "\"");
                    else if (k.GetValue(Name) != null) k.DeleteValue(Name);
                }
            }
            catch { }
        }
    }

    internal static class Dpi
    {
        public static float ScaleAt(Point p)
        {
            try
            {
                var mon = Native.MonitorFromPoint(new Native.POINT { X = p.X, Y = p.Y }, 2);
                uint dx, dy;
                if (Native.GetDpiForMonitor(mon, 0, out dx, out dy) == 0 && dx > 0) return dx / 96f;
            }
            catch { }
            return 1f;
        }
    }
}
