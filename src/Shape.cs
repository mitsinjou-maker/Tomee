using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Tomee
{
    internal enum Tool { Pen, Marker, Line, Arrow, Rect, Ellipse, Text }

    /// <summary>Annotation dessinée en mode dessin (coordonnées locales à l'écran annoté).</summary>
    internal sealed class Shape
    {
        static readonly Bitmap measureBmp = new Bitmap(1, 1);
        static readonly Graphics measure = Graphics.FromImage(measureBmp);

        public Tool Tool;
        public Color Color;
        public float Width;
        public readonly List<PointF> Points = new List<PointF>();
        /// <summary>Pression du stylet (0–1) pour chaque point ; vide pour un tracé à la souris (épaisseur constante).</summary>
        public readonly List<float> Pressures = new List<float>();
        public bool Smooth = true;
        public PointF A, B;
        public string Text = "";
        public float FontSize;

        float StrokeWidth { get { return Tool == Tool.Marker ? Math.Max(12f, Width * 4f) : Width; } }

        /// <summary>La pression ne s'applique qu'au stylo (le surligneur, translucide, garde une épaisseur constante).</summary>
        bool HasPressure { get { return Tool == Tool.Pen && Pressures.Count == Points.Count && Points.Count > 0; } }

        // pression 0 → 30 % de l'épaisseur, 0,5 → 100 %, 1 → 170 %
        static float PressureFactor(float p) { return 0.3f + 1.4f * p; }
        float MaxStrokeWidth { get { return HasPressure ? StrokeWidth * 1.7f : StrokeWidth; } }
        float HeadLength { get { return Math.Max(14f, Width * 4.5f); } }

        public Font MakeFont() { return new Font("Segoe UI Semibold", FontSize, GraphicsUnit.Pixel); }

        public SizeF TextSize
        {
            get
            {
                using (var f = MakeFont())
                    return measure.MeasureString(Text.Length == 0 ? "M" : Text + "|", f);
            }
        }

        public Rectangle Bounds()
        {
            RectangleF r;
            if (Tool == Tool.Text)
            {
                var s = TextSize;
                r = new RectangleF(A.X, A.Y, s.Width, s.Height);
            }
            else if (Tool == Tool.Pen || Tool == Tool.Marker)
            {
                if (Points.Count == 0) return Rectangle.Empty;
                float x0 = Points[0].X, y0 = Points[0].Y, x1 = x0, y1 = y0;
                foreach (var p in Points) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
                r = RectangleF.FromLTRB(x0, y0, x1, y1);
            }
            else r = RectangleF.FromLTRB(Math.Min(A.X, B.X), Math.Min(A.Y, B.Y), Math.Max(A.X, B.X), Math.Max(A.Y, B.Y));

            float pad = MaxStrokeWidth + 4 + (Tool == Tool.Arrow ? HeadLength : 0);
            r.Inflate(pad, pad);
            return Rectangle.Round(r);
        }

        /// <summary>
        /// Zone à redessiner après l'ajout du dernier point d'un trait : avec le lissage,
        /// le nouveau point modifie aussi la courbe des deux segments précédents.
        /// </summary>
        public Rectangle TailBounds()
        {
            int n = Points.Count;
            if (n == 0) return Rectangle.Empty;
            float x0 = Points[n - 1].X, y0 = Points[n - 1].Y, x1 = x0, y1 = y0;
            for (int i = Math.Max(0, n - 4); i < n; i++)
            {
                x0 = Math.Min(x0, Points[i].X); y0 = Math.Min(y0, Points[i].Y);
                x1 = Math.Max(x1, Points[i].X); y1 = Math.Max(y1, Points[i].Y);
            }
            var r = RectangleF.FromLTRB(x0, y0, x1, y1);
            // le lissage peut légèrement dépasser l'enveloppe des points de contrôle
            float pad = MaxStrokeWidth + 4 + Math.Max(r.Width, r.Height) * 0.15f;
            r.Inflate(pad, pad);
            return Rectangle.Round(r);
        }

        public bool IsEmpty
        {
            get
            {
                if (Tool == Tool.Text) return Text.Trim().Length == 0;
                if (Tool == Tool.Pen || Tool == Tool.Marker) return Points.Count == 0;
                return Math.Abs(A.X - B.X) < 2 && Math.Abs(A.Y - B.Y) < 2;
            }
        }

        public void Draw(Graphics g, bool caret = false)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var color = Tool == Tool.Marker ? Color.FromArgb(110, Color) : Color;
            switch (Tool)
            {
                case Tool.Pen:
                case Tool.Marker:
                    DrawStroke(g, color);
                    break;
                case Tool.Line:
                    using (var p = MakePen(color)) g.DrawLine(p, A, B);
                    break;
                case Tool.Arrow:
                    DrawArrow(g, color);
                    break;
                case Tool.Rect:
                    using (var p = MakePen(color))
                    {
                        p.LineJoin = LineJoin.Round;
                        g.DrawRectangle(p, Math.Min(A.X, B.X), Math.Min(A.Y, B.Y), Math.Abs(A.X - B.X), Math.Abs(A.Y - B.Y));
                    }
                    break;
                case Tool.Ellipse:
                    using (var p = MakePen(color))
                        g.DrawEllipse(p, Math.Min(A.X, B.X), Math.Min(A.Y, B.Y), Math.Abs(A.X - B.X), Math.Abs(A.Y - B.Y));
                    break;
                case Tool.Text:
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;
                    using (var f = MakeFont())
                    using (var b = new SolidBrush(color))
                        g.DrawString(caret ? Text + "|" : Text, f, b, A);
                    break;
            }
        }

        void DrawStroke(Graphics g, Color color)
        {
            if (Points.Count == 1)
            {
                float r = StrokeWidth * (HasPressure ? PressureFactor(Pressures[0]) : 1f) / 2;
                using (var b = new SolidBrush(color)) g.FillEllipse(b, Points[0].X - r, Points[0].Y - r, r * 2, r * 2);
                return;
            }

            PointF[] pts;
            float[] press;
            Interpolate(out pts, out press);

            if (!HasPressure)
            {
                // un seul tracé : indispensable pour le surligneur translucide (pas de recouvrements plus foncés)
                using (var p = MakePen(color)) g.DrawLines(p, pts);
                return;
            }

            // épaisseur variable : segment par segment, en ne traitant que ceux qui touchent la zone redessinée
            var clip = g.ClipBounds;
            float maxW = MaxStrokeWidth;
            using (var p = MakePen(color))
            {
                for (int i = 1; i < pts.Length; i++)
                {
                    PointF a = pts[i - 1], b = pts[i];
                    var seg = RectangleF.FromLTRB(Math.Min(a.X, b.X) - maxW, Math.Min(a.Y, b.Y) - maxW, Math.Max(a.X, b.X) + maxW, Math.Max(a.Y, b.Y) + maxW);
                    if (!seg.IntersectsWith(clip)) continue;
                    p.Width = Math.Max(1f, StrokeWidth * PressureFactor((press[i - 1] + press[i]) / 2));
                    g.DrawLine(p, a, b);
                }
            }
        }

        /// <summary>
        /// Lissage Catmull-Rom : la courbe passe par tous les points saisis, en ajoutant
        /// des points intermédiaires (et des pressions interpolées) entre eux.
        /// </summary>
        void Interpolate(out PointF[] pts, out float[] press)
        {
            int n = Points.Count;
            bool withPressure = HasPressure;
            if (!Smooth || n < 3)
            {
                pts = Points.ToArray();
                press = withPressure ? Pressures.ToArray() : null;
                return;
            }
            var outPts = new List<PointF>(n * 4);
            var outPress = withPressure ? new List<float>(n * 4) : null;
            outPts.Add(Points[0]);
            if (withPressure) outPress.Add(Pressures[0]);
            for (int i = 0; i < n - 1; i++)
            {
                PointF p0 = Points[Math.Max(0, i - 1)], p1 = Points[i], p2 = Points[i + 1], p3 = Points[Math.Min(n - 1, i + 2)];
                float dx = p2.X - p1.X, dy = p2.Y - p1.Y;
                int steps = Math.Max(1, Math.Min(10, (int)(Math.Sqrt(dx * dx + dy * dy) / 3)));
                for (int s = 1; s <= steps; s++)
                {
                    float t = (float)s / steps, t2 = t * t, t3 = t2 * t;
                    outPts.Add(new PointF(
                        0.5f * (2 * p1.X + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3),
                        0.5f * (2 * p1.Y + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3)));
                    if (withPressure) outPress.Add(Pressures[i] + (Pressures[i + 1] - Pressures[i]) * t);
                }
            }
            pts = outPts.ToArray();
            press = withPressure ? outPress.ToArray() : null;
        }

        Pen MakePen(Color c)
        {
            var p = new Pen(c, StrokeWidth);
            p.StartCap = p.EndCap = LineCap.Round;
            p.LineJoin = LineJoin.Round;
            return p;
        }

        void DrawArrow(Graphics g, Color c)
        {
            float dx = B.X - A.X, dy = B.Y - A.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return;
            float ux = dx / len, uy = dy / len;
            float head = Math.Min(HeadLength, len * 0.6f);
            float half = head * 0.55f;
            var baseP = new PointF(B.X - ux * head, B.Y - uy * head);
            using (var p = MakePen(c)) g.DrawLine(p, A, new PointF(baseP.X + ux * 2, baseP.Y + uy * 2));
            var tri = new[]
            {
                B,
                new PointF(baseP.X - uy * half, baseP.Y + ux * half),
                new PointF(baseP.X + uy * half, baseP.Y - ux * half),
            };
            using (var b = new SolidBrush(c)) g.FillPolygon(b, tri);
        }
    }
}
