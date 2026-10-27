using System;
using System.Collections.Generic;
using System.Text;

namespace Tomee
{
    /// <summary>Transforme les frappes clavier en texte à afficher (« Ctrl + C », « Alt + Tab ×3 »...).</summary>
    internal sealed class KeyDisplay
    {
        const int RepeatWindow = 1500;

        readonly Settings S;
        bool lctrl, rctrl, lshift, rshift, lalt, ralt, lwin, rwin;
        bool typing;
        string lastCombo;
        int repeat;

        public string Text { get; private set; }
        public long Time { get; private set; }

        public KeyDisplay(Settings s) { S = s; Time = -100000; }

        bool Ctrl { get { return lctrl || rctrl; } }
        bool Shift { get { return lshift || rshift; } }
        bool Alt { get { return lalt || ralt; } }
        bool Win { get { return lwin || rwin; } }
        bool AltGr { get { return ralt && lctrl; } }

        public void Reset()
        {
            lctrl = rctrl = lshift = rshift = lalt = ralt = lwin = rwin = false;
            Text = null;
        }

        bool UpdateModifier(int vk, bool down)
        {
            switch (vk)
            {
                case 0xA2: lctrl = down; return true;
                case 0xA3: rctrl = down; return true;
                case 0xA0: lshift = down; return true;
                case 0xA1: rshift = down; return true;
                case 0xA4: lalt = down; return true;
                case 0xA5: ralt = down; return true;
                case 0x5B: lwin = down; return true;
                case 0x5C: rwin = down; return true;
                case 0x10: case 0x11: case 0x12: return true;
            }
            return false;
        }

        /// <summary>Retourne true si le texte affiché a changé.</summary>
        public bool OnKey(int vk, int scan, bool down, long now)
        {
            if (UpdateModifier(vk, down) || !down) return false;

            bool altGr = AltGr;
            bool ctrl = Ctrl && !altGr, alt = Alt && !altGr, win = Win;
            bool combo = ctrl || alt || win;
            bool shortcutsOnly = S.KeysShortcutsOnly;
            bool special = IsSpecial(vk, !shortcutsOnly);

            if (!combo && !special)
            {
                if (shortcutsOnly) return false;
                string ch = TypedChar(vk, scan, Shift, altGr);
                if (ch == null) return false;
                if (typing && now - Time < RepeatWindow) Text = Tail(Text + ch);
                else Text = ch;
                typing = true;
                lastCombo = null;
                Time = now;
                return true;
            }

            if (!combo && vk == 0x08 && typing && now - Time < RepeatWindow && Text.Length > 1)
            {
                Text = Text.Substring(0, Text.Length - 1);
                Time = now;
                return true;
            }

            string name = KeyName(vk);
            if (name == null) return false;
            Show(Modifiers(ctrl, alt, Shift, win) + name, now);
            return true;
        }

        /// <summary>Affiche « Ctrl + Clic gauche » quand un modificateur est maintenu.</summary>
        public bool OnMouseDown(string button, long now)
        {
            bool altGr = AltGr;
            bool ctrl = Ctrl && !altGr, alt = Alt && !altGr;
            if (!(ctrl || alt || Shift || Win)) return false;
            Show(Modifiers(ctrl, alt, Shift, Win) + button, now);
            return true;
        }

        void Show(string label, long now)
        {
            if (label == lastCombo && now - Time < RepeatWindow) repeat++;
            else { repeat = 1; lastCombo = label; }
            Text = repeat > 1 ? label + "  ×" + repeat : label;
            typing = false;
            Time = now;
        }

        public string DisplayText
        {
            get
            {
                if (Text == null) return null;
                if (typing && Text.Trim().Length == 0) return "Espace";
                return Text;
            }
        }

        static string Tail(string s) { return s.Length > 40 ? s.Substring(s.Length - 40) : s; }

        static string Modifiers(bool ctrl, bool alt, bool shift, bool win)
        {
            var sb = new StringBuilder();
            if (win) sb.Append("Win + ");
            if (ctrl) sb.Append("Ctrl + ");
            if (alt) sb.Append("Alt + ");
            if (shift) sb.Append("Maj + ");
            return sb.ToString();
        }

        static bool IsSpecial(int vk, bool extended)
        {
            if (vk == 0x1B || vk == 0x0D || vk == 0x09 || vk == 0x2E || vk == 0x2C) return true;   // Échap, Entrée, Tab, Suppr, Impr
            if (vk >= 0x70 && vk <= 0x87) return true;                                         // F1-F24
            if (vk >= 0xAD && vk <= 0xB3) return true;                                         // multimédia
            if (!extended) return false;
            return vk == 0x08 || (vk >= 0x21 && vk <= 0x28) || vk == 0x2D;                     // ←, flèches, Début/Fin, Inser
        }

        static readonly Dictionary<int, string> Names = new Dictionary<int, string>
        {
            { 0x08, "Retour arrière" }, { 0x09, "Tab" }, { 0x0D, "Entrée" }, { 0x13, "Pause" },
            { 0x14, "Verr. Maj" }, { 0x1B, "Échap" }, { 0x20, "Espace" }, { 0x21, "Pg. préc." },
            { 0x22, "Pg. suiv." }, { 0x23, "Fin" }, { 0x24, "Début" }, { 0x25, "←" }, { 0x26, "↑" },
            { 0x27, "→" }, { 0x28, "↓" }, { 0x2C, "Impr. écran" }, { 0x2D, "Inser" }, { 0x2E, "Suppr" },
            { 0x5D, "Menu" }, { 0x6A, "Num *" }, { 0x6B, "Num +" }, { 0x6D, "Num -" }, { 0x6E, "Num ," },
            { 0x6F, "Num /" }, { 0xAD, "Muet" }, { 0xAE, "Volume -" }, { 0xAF, "Volume +" },
            { 0xB0, "Piste suivante" }, { 0xB1, "Piste précédente" }, { 0xB2, "Stop" }, { 0xB3, "Lecture/Pause" },
        };

        static string KeyName(int vk)
        {
            string n;
            if (Names.TryGetValue(vk, out n)) return n;
            if ((vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A)) return ((char)vk).ToString();
            if (vk >= 0x60 && vk <= 0x69) return "Num " + (vk - 0x60);
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
            uint c = Native.MapVirtualKey((uint)vk, 2) & 0x7FFF;
            if (c > 32) return char.ToUpperInvariant((char)c).ToString();
            return null;
        }

        static string TypedChar(int vk, int scan, bool shift, bool altGr)
        {
            if (vk == 0x20) return " ";
            var state = new byte[256];
            if (shift) state[0x10] = 0x80;
            if (altGr) { state[0x11] = 0x80; state[0x12] = 0x80; }
            if ((Native.GetKeyState(0x14) & 1) != 0) state[0x14] = 1;
            var hkl = Native.GetKeyboardLayout(Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero));
            var sb = new StringBuilder(8);
            // flag 4 : ne pas modifier l'état du clavier (préserve les touches mortes de l'application active)
            int n = Native.ToUnicodeEx((uint)vk, (uint)scan, state, sb, sb.Capacity, 4, hkl);
            if (n <= 0) return null;
            var s = sb.ToString(0, n);
            return s.Length > 0 && s[0] >= 32 ? s : null;
        }
    }
}
