using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Tomee
{
    internal static class Gfx
    {
        public static readonly Color Clear = Color.FromArgb(0, 0, 0, 0);

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Rectangle Circle(PointF c, float r)
        {
            int ri = (int)Math.Ceiling(r);
            return new Rectangle((int)Math.Floor(c.X) - ri, (int)Math.Floor(c.Y) - ri, ri * 2 + 2, ri * 2 + 2);
        }

        /// <summary>Union qui ignore les rectangles vides (Rectangle.Union inclurait l'origine).</summary>
        public static Rectangle Union(Rectangle a, Rectangle b)
        {
            if (a.IsEmpty) return b;
            if (b.IsEmpty) return a;
            return Rectangle.Union(a, b);
        }

        public static Color Fade(Color c, float factor)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, (int)(c.A * factor))), c.R, c.G, c.B);
        }

        static string iconFont;

        /// <summary>Police d'icônes de Windows (Segoe Fluent Icons sur Windows 11, MDL2 sur Windows 10), ou null.</summary>
        public static string IconFont
        {
            get
            {
                if (iconFont == null)
                {
                    iconFont = "";
                    foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
                    {
                        try { using (new FontFamily(name)) { iconFont = name; break; } }
                        catch (ArgumentException) { }
                    }
                }
                return iconFont.Length > 0 ? iconFont : null;
            }
        }
    }

    internal static class AppIcons
    {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        public static Bitmap Draw(int size, bool paused)
        {
            var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float s = size;
                var bg = paused ? Color.FromArgb(95, 99, 110) : Color.FromArgb(31, 41, 55);
                var fg = paused ? Color.FromArgb(200, 200, 205) : Color.FromArgb(255, 196, 0);
                using (var br = new SolidBrush(bg)) g.FillEllipse(br, 0.5f, 0.5f, s - 1, s - 1);
                using (var br = new SolidBrush(Color.FromArgb(70, fg))) g.FillEllipse(br, s * 0.2f, s * 0.2f, s * 0.6f, s * 0.6f);
                using (var p = new Pen(fg, Math.Max(1.6f, s * 0.1f))) g.DrawEllipse(p, s * 0.2f, s * 0.2f, s * 0.6f, s * 0.6f);
                using (var br = new SolidBrush(fg)) g.FillEllipse(br, s * 0.41f, s * 0.41f, s * 0.18f, s * 0.18f);
            }
            return b;
        }

        public static Icon Make(int size, bool paused)
        {
            using (var b = Draw(size, paused))
            {
                IntPtr h = b.GetHicon();
                using (var tmp = Icon.FromHandle(h))
                {
                    var copy = (Icon)tmp.Clone();
                    DestroyIcon(h);
                    return copy;
                }
            }
        }
    }
}
