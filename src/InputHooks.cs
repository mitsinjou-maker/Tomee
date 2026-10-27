using System;
using System.Drawing;

namespace Tomee
{
    /// <summary>Hooks bas niveau souris et clavier (globaux, exécutés sur le thread de l'interface).</summary>
    internal sealed unsafe class InputHooks : IDisposable
    {
        /// <summary>Retourne true pour bloquer l'évènement.</summary>
        /// <param name="fromPen">Vrai si l'évènement est généré par un stylet ou le tactile (et non une vraie souris).</param>
        public delegate bool MouseHandler(int msg, Point pt, int wheelDelta, bool fromPen);
        public delegate void KeyHandler(int vk, int scan, bool down);

        public MouseHandler OnMouse;
        public KeyHandler OnKey;

        readonly Native.HookProc mouseProc, keyProc;
        IntPtr mouseHook, keyHook;

        public InputHooks()
        {
            mouseProc = MouseProc;
            keyProc = KeyProc;
        }

        static IntPtr Module { get { return Native.GetModuleHandle(null); } }

        public void StartMouse()
        {
            if (mouseHook == IntPtr.Zero)
                mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, Module, 0);
        }

        public void SetKeyboard(bool enabled)
        {
            if (enabled && keyHook == IntPtr.Zero)
                keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyProc, Module, 0);
            else if (!enabled && keyHook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(keyHook);
                keyHook = IntPtr.Zero;
            }
        }

        IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && OnMouse != null)
            {
                var info = (Native.MSLLHOOKSTRUCT*)lParam;
                int msg = wParam.ToInt32();
                int delta = (short)((info->mouseData >> 16) & 0xFFFF);
                uint extra = (uint)(info->dwExtraInfo.ToInt64() & 0xFFFFFFFF);
                bool fromPen = (extra & Native.SIGNATURE_MASK) == Native.MI_WP_SIGNATURE;
                try
                {
                    if (OnMouse(msg, new Point(info->pt.X, info->pt.Y), delta, fromPen)) return new IntPtr(1);
                }
                catch (Exception ex) { Program.Log(ex); }
            }
            return Native.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        IntPtr KeyProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && OnKey != null)
            {
                var info = (Native.KBDLLHOOKSTRUCT*)lParam;
                int msg = wParam.ToInt32();
                bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
                try { OnKey((int)info->vkCode, (int)info->scanCode, down); }
                catch (Exception ex) { Program.Log(ex); }
            }
            return Native.CallNextHookEx(keyHook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            SetKeyboard(false);
            if (mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
        }
    }
}
