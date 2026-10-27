using System;
using System.Drawing;

namespace Tomee
{
    /// <summary>Loupe plein écran (API Magnification de Windows) qui suit le curseur.</summary>
    internal sealed class ScreenZoom : IDisposable
    {
        bool initialized;
        float level = 1f, target = 1f, ox, oy;

        public bool Active { get; private set; }
        /// <summary>Quand true, la vue ne suit plus le curseur (utile pendant le dessin).</summary>
        public bool Frozen;

        public bool Toggle(int percent)
        {
            if (!initialized)
            {
                try { initialized = Native.MagInitialize(); }
                catch (DllNotFoundException) { initialized = false; }
                if (!initialized) return false;
            }
            Active = !Active;
            target = Active ? percent / 100f : 1f;
            return true;
        }

        public void SetPercent(int percent)
        {
            if (Active) target = percent / 100f;
        }

        public void Tick(Point cursor, Rectangle monitor, bool smooth)
        {
            if (!initialized || (!Active && level == 1f)) return;

            bool starting = level == 1f;
            level = smooth ? level + (target - level) * 0.22f : target;
            if (Math.Abs(target - level) < 0.004f) level = target;

            float vw = monitor.Width / level, vh = monitor.Height / level;
            float tx = Clamp(cursor.X - vw / 2, monitor.Left, monitor.Right - vw);
            float ty = Clamp(cursor.Y - vh / 2, monitor.Top, monitor.Bottom - vh);
            if (starting || !smooth) { ox = tx; oy = ty; }
            else if (!Frozen) { ox += (tx - ox) * 0.35f; oy += (ty - oy) * 0.35f; }
            ox = Clamp(ox, monitor.Left, monitor.Right - vw);
            oy = Clamp(oy, monitor.Top, monitor.Bottom - vh);

            if (!Active && level <= 1.002f) { level = 1f; ox = oy = 0; }
            Native.MagSetFullscreenTransform(level, (int)Math.Round(ox), (int)Math.Round(oy));
        }

        static float Clamp(float v, float min, float max) { return max < min ? min : Math.Max(min, Math.Min(max, v)); }

        public void Dispose()
        {
            if (!initialized) return;
            Native.MagSetFullscreenTransform(1f, 0, 0);
            Native.MagUninitialize();
            initialized = false;
        }
    }
}
