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
        public PointF A, B;
        public string Text = "";
        public float FontSize;

        float StrokeWidth { get { return Tool == Tool.Marker ? Math.Max(12f, Width * 4f) : Width; } }
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

            float pad = StrokeWidth + 4 + (Tool == Tool.Arrow ? HeadLength : 0);
            r.Inflate(pad, pad);
            return Rectangle.Round(r);
        }

        /// <summary>Zone à redessiner quand on ajoute le segment a→b à un trait.</summary>
        public Rectangle SegmentBounds(PointF a, PointF b)
        {
            var r = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
            float pad = StrokeWidth + 4;
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
                    if (Points.Count == 1)
                    {
                        float r = StrokeWidth / 2;
                        using (var b = new SolidBrush(color)) g.FillEllipse(b, Points[0].X - r, Points[0].Y - r, r * 2, r * 2);
                    }
                    else using (var p = MakePen(color)) g.DrawLines(p, Points.ToArray());
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
