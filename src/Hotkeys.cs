using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Tomee
{
    internal enum HotkeyId { Spotlight = 1, Ring, Clicks, Trail, Keys, Draw, Whiteboard, Zoom, ZoomIn, ZoomOut, Find, Pause }

    internal sealed class HotkeyDef
    {
        public HotkeyId Id;
        public Keys Key;
        public string Label;
        public bool Failed;

        public string Combo
        {
            get
            {
                string k = Key == Keys.Up ? "↑" : Key == Keys.Down ? "↓" : Key.ToString();
                return "Ctrl + Alt + " + k;
            }
        }
    }

    /// <summary>Raccourcis globaux (Ctrl+Alt+…) via RegisterHotKey sur une fenêtre message-only.</summary>
    internal sealed class Hotkeys : NativeWindow, IDisposable
    {
        public static readonly List<HotkeyDef> All = new List<HotkeyDef>
        {
            new HotkeyDef { Id = HotkeyId.Spotlight,  Key = Keys.S,    Label = "Projecteur (assombrir autour du curseur)" },
            new HotkeyDef { Id = HotkeyId.Ring,       Key = Keys.H,    Label = "Halo autour du curseur" },
            new HotkeyDef { Id = HotkeyId.Clicks,     Key = Keys.C,    Label = "Effets de clic" },
            new HotkeyDef { Id = HotkeyId.Trail,      Key = Keys.T,    Label = "Traînée du curseur" },
            new HotkeyDef { Id = HotkeyId.Keys,       Key = Keys.K,    Label = "Afficher les touches" },
            new HotkeyDef { Id = HotkeyId.Draw,       Key = Keys.D,    Label = "Dessiner sur l'écran" },
            new HotkeyDef { Id = HotkeyId.Whiteboard, Key = Keys.W,    Label = "Tableau blanc" },
            new HotkeyDef { Id = HotkeyId.Zoom,       Key = Keys.Z,    Label = "Zoom (loupe)" },
            new HotkeyDef { Id = HotkeyId.ZoomIn,     Key = Keys.Up,   Label = "Zoom avant (ou Ctrl+Alt+molette)" },
            new HotkeyDef { Id = HotkeyId.ZoomOut,    Key = Keys.Down, Label = "Zoom arrière" },
            new HotkeyDef { Id = HotkeyId.Find,       Key = Keys.F,    Label = "Localiser le curseur" },
            new HotkeyDef { Id = HotkeyId.Pause,      Key = Keys.X,    Label = "Pause / reprise de tous les effets" },
        };

        public event Action<HotkeyId> Pressed;

        public Hotkeys()
        {
            var cp = new CreateParams();
            cp.Parent = new IntPtr(-3); // HWND_MESSAGE
            CreateHandle(cp);
            foreach (var h in All)
                h.Failed = !Native.RegisterHotKey(Handle, (int)h.Id, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)h.Key);
        }

        public static string ComboFor(HotkeyId id)
        {
            foreach (var h in All) if (h.Id == id) return h.Combo;
            return "";
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && Pressed != null) Pressed((HotkeyId)m.WParam.ToInt32());
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            foreach (var h in All) Native.UnregisterHotKey(Handle, (int)h.Id);
            DestroyHandle();
        }
    }
}
