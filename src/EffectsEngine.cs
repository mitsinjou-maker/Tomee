using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Tomee
{
    /// <summary>
    /// Dessine les effets autour du curseur (projecteur, halo, clics, traînée, localisation)
    /// ainsi que les bulles de touches et notifications, dans une fenêtre transparente par écran.
    /// </summary>
    internal sealed class EffectsEngine : IDisposable
    {
        const int ToastDuration = 1800, FindDuration = 900;
        // Afficher/masquer une fenêtre plein écran fait réagir la barre des tâches : on ne la masque qu'après un long moment vide.
        const int HideAfter = 3000;
        // Windows masque le pointeur à chaque frappe : on attend un état stable avant de masquer/réafficher le halo.
        const int CursorStateDelay = 300;

        sealed class Surface
        {
            public LayeredWindow Win;
            public Rectangle Bounds, WorkArea;
            public float Scale;
            public List<Rectangle> Prev = new List<Rectangle>();
            public long LastContent;
        }

        struct TrailPoint { public float X, Y; public long T; }
        sealed class Ripple { public PointF P; public long T; public Color C; }

        readonly Settings S;
        readonly KeyDisplay keys;
        readonly List<Surface> surfaces = new List<Surface>();
        readonly List<TrailPoint> trail = new List<TrailPoint>();
        readonly List<Ripple> ripples = new List<Ripple>();
        readonly Bitmap measureBmp = new Bitmap(1, 1);
        readonly Graphics measure;

        Point cursor, drawnCursor = new Point(int.MinValue, int.MinValue);
        bool cursorShown = true, rawCursorShown = true;
        long rawCursorSince;
        int held;                       // 1 gauche, 2 droit, 4 milieu
        long now, findStart = -100000, toastTime = -100000;
        string toast;
        bool forceFull, lastSpot, poked, lastAnimated, suspended;
        Surface cur;
        Font bubbleFont; float bubbleFontSize;

        public bool Paused;
        public bool SuppressClicks;

        public EffectsEngine(Settings s, KeyDisplay k)
        {
            S = s;
            keys = k;
            measure = Graphics.FromImage(measureBmp);
            measure.TextRenderingHint = TextRenderingHint.AntiAlias;
            Rebuild();
        }

        public Point Cursor { get { return cursor; } }
        public Rectangle CursorMonitor { get { return cur != null ? cur.Bounds : Screen.PrimaryScreen.Bounds; } }
        public float CursorScale { get { return cur != null ? cur.Scale : 1f; } }

        public void Rebuild()
        {
            foreach (var sf in surfaces) sf.Win.Dispose();
            surfaces.Clear();
            foreach (var scr in Screen.AllScreens)
            {
                var b = scr.Bounds;
                surfaces.Add(new Surface
                {
                    Bounds = b,
                    WorkArea = scr.WorkingArea,
                    Win = new LayeredWindow(b, true),
                    Scale = Dpi.ScaleAt(new Point(b.X + b.Width / 2, b.Y + b.Height / 2)),
                });
            }
            cur = surfaces.Count > 0 ? surfaces[0] : null;
            forceFull = true;
        }

        // ---------- évènements ----------

        public void OnMouseMove(Point p, long t)
        {
            if (!S.TrailEnabled || Paused) return;
            if (trail.Count > 0)
            {
                var last = trail[trail.Count - 1];
                if (Math.Abs(last.X - p.X) + Math.Abs(last.Y - p.Y) < 2) return;
            }
            trail.Add(new TrailPoint { X = p.X, Y = p.Y, T = t });
        }

        public void OnButton(int button, bool down, Point p, long t)
        {
            if (down) held |= button; else held &= ~button;
            poked = true;
            if (!down || Paused || SuppressClicks || !S.ClicksEnabled) return;
            var c = button == 2 ? S.RightClickColor : button == 4 ? S.MiddleClickColor : S.LeftClickColor;
            ripples.Add(new Ripple { P = p, T = t, C = c });
        }

        public void Find() { findStart = now; poked = true; }

        public void Toast(string text)
        {
            if (!S.ShowToasts) return;
            toast = text;
            toastTime = now;
            poked = true;
        }

        public void Poke() { poked = true; }
        public void ForceFull() { forceFull = true; }

        /// <summary>Masque immédiatement les fenêtres (ex. capture d'écran) ; elles réapparaissent au prochain ForceFull.</summary>
        public void HideNow(bool hide)
        {
            suspended = hide;
            if (hide) foreach (var sf in surfaces) if (sf.Win.Visible) sf.Win.Hide();
            forceFull = true;
        }

        public void BringToTop()
        {
            foreach (var sf in surfaces) if (sf.Win.Visible) sf.Win.BringToTop();
        }

        // ---------- boucle de rendu ----------

        public void Tick(long t)
        {
            now = t;
            Native.POINT np;
            if (Native.GetCursorPos(out np)) cursor = new Point(np.X, np.Y);
            var ci = new Native.CURSORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.CURSORINFO)) };
            bool shown = !Native.GetCursorInfo(ref ci) || (ci.flags & 1) != 0;
            if (shown != rawCursorShown) { rawCursorShown = shown; rawCursorSince = now; }
            if (rawCursorShown != cursorShown && now - rawCursorSince >= CursorStateDelay) { cursorShown = rawCursorShown; poked = true; }
            bool waitingHide = false;
            foreach (var sf in surfaces) if (sf.Win.Visible) { waitingHide = true; break; }

            foreach (var sf in surfaces)
                if (sf.Bounds.Contains(cursor)) { cur = sf; break; }

            if (!S.TrailEnabled || Paused) trail.Clear();
            int trailLen = Math.Max(50, S.TrailLength);
            trail.RemoveAll(p => now - p.T > trailLen);
            int rippleLen = Math.Max(100, S.RippleDuration);
            ripples.RemoveAll(r => now - r.T > rippleLen);
            if (Paused || !S.ClicksEnabled) ripples.Clear();

            bool active = !Paused && !suspended;
            bool spot = active && S.SpotlightEnabled;
            bool keyOn = active && S.KeysEnabled && keys.Text != null && now - keys.Time < S.KeysDuration;
            bool toastOn = !suspended && toast != null && now - toastTime < ToastDuration;
            bool findOn = active && now - findStart < FindDuration;
            bool full = forceFull || spot != lastSpot;
            bool moved = cursor != drawnCursor;
            bool animated = ripples.Count > 0 || trail.Count > 0 || keyOn || toastOn || findOn;

            if (!full && !moved && !animated && !poked && !lastAnimated)
            {
                if (waitingHide) HideIdleSurfaces();
                return;
            }
            forceFull = false;
            poked = false;
            lastAnimated = animated;
            lastSpot = spot;
            drawnCursor = cursor;

            float sc = cur != null ? cur.Scale : 1f;
            var frame = new Frame
            {
                Active = active, Spot = spot, Scale = sc,
                ShowRing = active && S.RingEnabled && (cursorShown || !S.RingHideWithCursor),
                KeyText = keyOn ? keys.DisplayText : null,
                KeyAge = now - keys.Time,
                ToastText = toastOn ? toast : null,
                ToastAge = now - toastTime,
                FindAge = findOn ? now - findStart : -1,
            };

            // zones occupées par le contenu de cette image (coordonnées écran)
            var content = new List<Rectangle>();
            if (active)
            {
                var r = Rectangle.Empty;
                if (spot) r = Gfx.Union(r, Gfx.Circle(cursor, (S.SpotlightRadius + Math.Max(1, S.SpotlightFeather)) * sc + 2));
                if (frame.ShowRing || (held != 0 && S.ClicksEnabled))
                    r = Gfx.Union(r, Gfx.Circle(cursor, (Math.Max(S.RingRadius, 16) + S.RingThickness) * sc + 3));
                foreach (var rp in ripples) r = Gfx.Union(r, Gfx.Circle(rp.P, S.RippleRadius * sc + 4 * sc + 2));
                if (trail.Count > 0 && cursorShown)
                {
                    float minX = cursor.X, minY = cursor.Y, maxX = cursor.X, maxY = cursor.Y;
                    foreach (var p in trail) { minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); }
                    int pad = (int)(S.TrailWidth * sc) + 3;
                    r = Gfx.Union(r, Rectangle.FromLTRB((int)minX - pad, (int)minY - pad, (int)maxX + pad, (int)maxY + pad));
                }
                if (frame.FindAge >= 0) r = Gfx.Union(r, Gfx.Circle(cursor, 260 * sc + 6));
                if (!r.IsEmpty) content.Add(r);
            }
            if (frame.KeyText != null) { frame.KeyRect = BubbleRect(frame.KeyText, S.KeysFontSize * sc, S.KeysPosition, sc); content.Add(Inflate(frame.KeyRect)); }
            if (frame.ToastText != null) { frame.ToastRect = BubbleRect(frame.ToastText, 17 * sc, 4, sc); content.Add(Inflate(frame.ToastRect)); }

            foreach (var sf in surfaces) RenderSurface(sf, frame, content, full);
        }

        static Rectangle Inflate(RectangleF r)
        {
            var x = Rectangle.Round(r);
            x.Inflate(3, 3);
            return x;
        }

        sealed class Frame
        {
            public bool Active, Spot, ShowRing;
            public float Scale;
            public string KeyText, ToastText;
            public long KeyAge, ToastAge, FindAge;
            public RectangleF KeyRect, ToastRect;
        }

        void RenderSurface(Surface sf, Frame f, List<Rectangle> content, bool full)
        {
            var local = new List<Rectangle>();
            foreach (var r in content)
            {
                var x = Rectangle.Intersect(r, sf.Bounds);
                if (x.IsEmpty) continue;
                x.Offset(-sf.Bounds.X, -sf.Bounds.Y);
                local.Add(x);
            }
            bool hasContent = f.Spot || local.Count > 0;

            var dirty = new List<Rectangle>();
            if (full) dirty.Add(sf.Win.LocalBounds);
            else { dirty.AddRange(sf.Prev); dirty.AddRange(local); }
            sf.Prev = local;

            // une fenêtre masquée peut garder d'anciens pixels : un rendu complet la nettoie quand même
            if (suspended || dirty.Count == 0 || (!hasContent && !sf.Win.Visible && !full)) return;

            dirty = Merge(dirty);
            foreach (var d in dirty) Paint(sf, d, f);
            foreach (var d in dirty) sf.Win.Present(d);

            if (hasContent)
            {
                sf.LastContent = now;
                if (!sf.Win.Visible) sf.Win.Show(false);
            }
        }

        /// <summary>
        /// Masque les fenêtres restées vides (et déjà effacées) depuis HideAfter ms. Celle de l'écran du curseur
        /// reste affichée tant qu'un effet est actif, pour ne pas faire réagir la barre des tâches à chaque frappe ou clic.
        /// </summary>
        void HideIdleSurfaces()
        {
            bool effectsOn = !Paused && (S.RingEnabled || S.ClicksEnabled || S.TrailEnabled || S.KeysEnabled);
            foreach (var sf in surfaces)
            {
                if (!sf.Win.Visible || sf.Prev.Count > 0 || lastSpot || now - sf.LastContent <= HideAfter) continue;
                if (effectsOn && sf == cur) continue;
                sf.Win.Hide();
            }
        }

        static List<Rectangle> Merge(List<Rectangle> input)
        {
            var list = new List<Rectangle>();
            foreach (var r in input) if (!r.IsEmpty) list.Add(r);
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < list.Count && !merged; i++)
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var a = list[i];
                        a.Inflate(16, 16);
                        if (!a.IntersectsWith(list[j])) continue;
                        list[i] = Rectangle.Union(list[i], list[j]);
                        list.RemoveAt(j);
                        merged = true;
                        break;
                    }
            }
            return list;
        }

        void Paint(Surface sf, Rectangle d, Frame f)
        {
            var g = sf.Win.G;
            g.ResetTransform();
            g.SetClip(d);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.SmoothingMode = SmoothingMode.None;
            var veil = Color.FromArgb(Math.Min(255, S.SpotlightDim * 255 / 100), 0, 0, 0);
            using (var b = new SolidBrush(f.Spot ? veil : Gfx.Clear)) g.FillRectangle(b, d);

            g.TranslateTransform(-sf.Bounds.X, -sf.Bounds.Y);
            if (f.Active)
            {
                if (f.Spot) DrawSpotlightHole(g, veil, f.Scale);
                g.CompositingMode = CompositingMode.SourceOver;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (cursorShown) DrawTrail(g, f.Scale);
                DrawRipples(g, f.Scale);
                if (f.FindAge >= 0) DrawFind(g, f.Scale, f.FindAge);
                DrawRing(g, f);
            }
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (f.KeyText != null) DrawBubble(g, f.KeyText, f.KeyRect, S.KeysFontSize * f.Scale, Fade(f.KeyAge, S.KeysDuration), f.Scale);
            if (f.ToastText != null) DrawBubble(g, f.ToastText, f.ToastRect, 17 * f.Scale, Fade(f.ToastAge, ToastDuration), f.Scale);
            g.ResetTransform();
            g.ResetClip();
        }

        static float Fade(long age, int duration)
        {
            float a = Math.Min(1f, age / 90f);
            long remain = duration - age;
            if (remain < 250) a = Math.Min(a, remain / 250f);
            return Math.Max(0f, a);
        }

        void DrawSpotlightHole(Graphics g, Color veil, float sc)
        {
            float r = S.SpotlightRadius * sc;
            float feather = Math.Max(1f, S.SpotlightFeather * sc);
            float R = r + feather;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cursor.X - R, cursor.Y - R, R * 2, R * 2);
                using (var brush = new PathGradientBrush(path))
                {
                    brush.CenterPoint = cursor;
                    brush.InterpolationColors = new ColorBlend(3)
                    {
                        Colors = new[] { veil, Gfx.Clear, Gfx.Clear },
                        Positions = new[] { 0f, Math.Min(0.999f, feather / R), 1f },
                    };
                    g.FillPath(brush, path);
                }
            }
        }

        void DrawRing(Graphics g, Frame f)
        {
            float sc = f.Scale;
            bool pressed = held != 0 && S.ClicksEnabled && !SuppressClicks;
            Color pressColor = (held & 1) != 0 ? S.LeftClickColor : (held & 2) != 0 ? S.RightClickColor : S.MiddleClickColor;

            if (!f.ShowRing)
            {
                if (!pressed) return;
                float rr = 12 * sc;
                using (var b = new SolidBrush(Color.FromArgb(140, pressColor)))
                    g.FillEllipse(b, cursor.X - rr, cursor.Y - rr, rr * 2, rr * 2);
                return;
            }

            float r = S.RingRadius * sc * (pressed ? 0.85f : 1f);
            var color = pressed ? pressColor : S.RingColor;
            if (S.RingFillOpacity > 0 || pressed)
                using (var b = new SolidBrush(Settings.WithOpacity(color, pressed ? Math.Max(35, S.RingFillOpacity) : S.RingFillOpacity)))
                    g.FillEllipse(b, cursor.X - r, cursor.Y - r, r * 2, r * 2);
            using (var p = new Pen(Settings.WithOpacity(color, S.RingOpacity), Math.Max(1f, S.RingThickness * sc)))
                g.DrawEllipse(p, cursor.X - r, cursor.Y - r, r * 2, r * 2);
        }

        void DrawRipples(Graphics g, float sc)
        {
            float dur = Math.Max(100, S.RippleDuration);
            foreach (var rp in ripples)
            {
                float t = Math.Min(1f, (now - rp.T) / dur);
                float e = 1 - (1 - t) * (1 - t) * (1 - t);
                float r = (6 + (S.RippleRadius - 6) * e) * sc;
                float a = 1 - t;
                using (var b = new SolidBrush(Color.FromArgb((int)(70 * a), rp.C)))
                    g.FillEllipse(b, rp.P.X - r, rp.P.Y - r, r * 2, r * 2);
                using (var p = new Pen(Color.FromArgb((int)(255 * a), rp.C), Math.Max(1f, 3.5f * sc * (1 - 0.5f * t))))
                    g.DrawEllipse(p, rp.P.X - r, rp.P.Y - r, r * 2, r * 2);
            }
        }

        void DrawTrail(Graphics g, float sc)
        {
            if (trail.Count == 0) return;
            float len = Math.Max(50, S.TrailLength);
            int baseAlpha = Math.Min(255, S.TrailOpacity * 255 / 100);
            float px = trail[0].X, py = trail[0].Y;
            for (int i = 1; i <= trail.Count; i++)
            {
                float x, y; long t;
                if (i < trail.Count) { x = trail[i].X; y = trail[i].Y; t = trail[i].T; }
                else { x = cursor.X; y = cursor.Y; t = now; }
                float k = 1 - (now - t) / len;
                if (k > 0)
                {
                    using (var p = new Pen(Color.FromArgb((int)(baseAlpha * k), S.TrailColor), Math.Max(1f, S.TrailWidth * sc * k)))
                    {
                        p.StartCap = p.EndCap = LineCap.Round;
                        g.DrawLine(p, px, py, x, y);
                    }
                }
                px = x; py = y;
            }
        }

        void DrawFind(Graphics g, float sc, long age)
        {
            for (int k = 0; k < 3; k++)
            {
                float t = (age - k * 130) / 600f;
                if (t < 0 || t > 1) continue;
                float e = 1 - (1 - t) * (1 - t);
                float r = (260 - (260 - Math.Max(18, S.RingRadius)) * e) * sc;
                using (var p = new Pen(Color.FromArgb((int)(230 * (1 - t * 0.6f)), S.RingColor), 4 * sc))
                    g.DrawEllipse(p, cursor.X - r, cursor.Y - r, r * 2, r * 2);
            }
        }

        // ---------- bulles de texte ----------

        Font FontFor(float px)
        {
            if (bubbleFont == null || bubbleFontSize != px)
            {
                if (bubbleFont != null) bubbleFont.Dispose();
                bubbleFont = new Font("Segoe UI Semibold", Math.Max(8f, px), GraphicsUnit.Pixel);
                bubbleFontSize = px;
            }
            return bubbleFont;
        }

        /// <param name="position">0 bas-centre, 1 bas-gauche, 2 bas-droite, 3 haut-centre, 4 notification (haut-centre)</param>
        RectangleF BubbleRect(string text, float px, int position, float sc)
        {
            var size = measure.MeasureString(text, FontFor(px));
            float padX = px * 0.6f, padY = px * 0.3f;
            float w = size.Width + padX * 2, h = size.Height + padY * 2;
            var area = cur != null ? cur.WorkArea : Screen.PrimaryScreen.WorkingArea;
            float margin = 60 * sc;
            float x = area.X + (area.Width - w) / 2, y = area.Bottom - h - margin;
            switch (position)
            {
                case 1: x = area.X + margin; break;
                case 2: x = area.Right - w - margin; break;
                case 3: y = area.Y + margin; break;
                case 4: y = area.Y + 24 * sc + (S.KeysPosition == 3 && S.KeysEnabled ? S.KeysFontSize * 2.4f * sc : 0); break;
            }
            return new RectangleF(x, y, w, h);
        }

        void DrawBubble(Graphics g, string text, RectangleF r, float px, float alpha, float sc)
        {
            if (alpha <= 0) return;
            using (var path = Gfx.RoundRect(r, Math.Min(r.Height / 2, 14 * sc)))
            {
                using (var b = new SolidBrush(Color.FromArgb((int)(220 * alpha), 22, 24, 30))) g.FillPath(b, path);
                using (var p = new Pen(Color.FromArgb((int)(60 * alpha), 255, 255, 255), Math.Max(1f, sc))) g.DrawPath(p, path);
            }
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            using (var b = new SolidBrush(Color.FromArgb((int)(255 * alpha), 255, 255, 255)))
                g.DrawString(text, FontFor(px), b, r.X + px * 0.6f, r.Y + px * 0.3f);
        }

        public void Dispose()
        {
            foreach (var sf in surfaces) sf.Win.Dispose();
            surfaces.Clear();
            measure.Dispose();
            measureBmp.Dispose();
            if (bubbleFont != null) bubbleFont.Dispose();
        }
    }
}
