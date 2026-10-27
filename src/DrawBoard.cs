using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Tomee
{
    internal enum BoardBackground { None, White, Black }

    /// <summary>
    /// Mode dessin : fenêtre plein écran (sur l'écran du curseur) qui capture la souris et le clavier
    /// pour annoter par-dessus le bureau, ou sur un tableau blanc / noir.
    /// </summary>
    internal sealed class DrawBoard : LayeredWindow
    {
        public static readonly Color[] Palette =
        {
            Color.FromArgb(235, 40, 40), Color.FromArgb(40, 180, 70), Color.FromArgb(40, 110, 240), Color.FromArgb(255, 210, 0),
            Color.FromArgb(255, 130, 0), Color.FromArgb(240, 60, 160), Color.FromArgb(20, 20, 20), Color.FromArgb(250, 250, 250),
        };
        static readonly string[] PaletteNames = { "Rouge", "Vert", "Bleu", "Jaune", "Orange", "Rose", "Noir", "Blanc" };
        static readonly string[] ToolTips =
        {
            "Stylo (P)", "Surligneur (M)", "Ligne (L)  — Maj : angles de 45°", "Flèche (A)  — Maj : angles de 45°",
            "Rectangle (R)  — Maj : carré", "Ellipse (E)  — Maj : cercle", "Texte (T)  — cliquez puis tapez, Entrée pour valider",
        };

        enum Kind { Separator, Tool, Color, Thinner, WidthShow, Thicker, Undo, Redo, Clear, White, Black, Save, Close }

        sealed class Button
        {
            public Kind Kind;
            public int Index;
            public Rectangle R;
            public string Tip;
        }

        readonly Settings S;
        readonly float sc;
        readonly Bitmap committed;
        readonly Graphics cg;
        readonly List<Shape> shapes = new List<Shape>();
        readonly Stack<List<Shape>> undo = new Stack<List<Shape>>(), redo = new Stack<List<Shape>>();
        readonly List<Button> buttons = new List<Button>();
        readonly IntPtr cursorCross, cursorArrow, cursorBeam, cursorHand;

        Shape current, text;
        Tool tool = Tool.Pen;
        Color color;
        int width;
        BoardBackground background;
        Rectangle bar, barArea, msgRect;
        int hover = -1;
        bool barVisible = true, caretOn = true;
        string message;
        long messageTime, lastBlink;
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        long Now { get { return clock.ElapsedMilliseconds; } }

        public event Action CloseRequested;
        /// <summary>Argument : true pour enregistrer un fichier, false pour copier seulement.</summary>
        public event Action<bool> CaptureRequested;

        public DrawBoard(Rectangle monitor, float scale, Settings s, BoardBackground bg) : base(monitor, false)
        {
            S = s;
            sc = scale;
            background = bg;
            color = s.DrawColor;
            width = Math.Max(1, Math.Min(40, s.DrawWidth));
            cursorCross = Native.LoadCursor(IntPtr.Zero, Native.IDC_CROSS);
            cursorArrow = Native.LoadCursor(IntPtr.Zero, Native.IDC_ARROW);
            cursorBeam = Native.LoadCursor(IntPtr.Zero, Native.IDC_IBEAM);
            cursorHand = Native.LoadCursor(IntPtr.Zero, Native.IDC_HAND);

            committed = new Bitmap(monitor.Width, monitor.Height, PixelFormat.Format32bppPArgb);
            cg = Graphics.FromImage(committed);
            LayoutBar();
            RebuildCommitted();
            Render(LocalBounds);
            Show(true);
            Native.SetForegroundWindow(Handle);
            ShowMessage("Mode dessin — Échap pour quitter, Tab pour masquer la barre");
        }

        public Color CurrentColor { get { return color; } }
        public int CurrentWidth { get { return width; } }
        public BoardBackground Background { get { return background; } }

        // ---------- barre d'outils ----------

        void LayoutBar()
        {
            int b = (int)(34 * sc), gap = (int)(3 * sc), pad = (int)(7 * sc), sep = (int)(11 * sc);
            Action<Kind, int, string> add = (k, i, tip) => buttons.Add(new Button { Kind = k, Index = i, Tip = tip });
            for (int i = 0; i < ToolTips.Length; i++) add(Kind.Tool, i, ToolTips[i]);
            add(Kind.Separator, 0, null);
            for (int i = 0; i < Palette.Length; i++) add(Kind.Color, i, PaletteNames[i] + " (" + (i + 1) + ")");
            add(Kind.Separator, 0, null);
            add(Kind.Thinner, 0, "Plus fin (−, molette)");
            add(Kind.WidthShow, 0, "Épaisseur actuelle");
            add(Kind.Thicker, 0, "Plus épais (+, molette)");
            add(Kind.Separator, 0, null);
            add(Kind.Undo, 0, "Annuler (Ctrl+Z)");
            add(Kind.Redo, 0, "Rétablir (Ctrl+Y)");
            add(Kind.Clear, 0, "Tout effacer (Suppr)");
            add(Kind.Separator, 0, null);
            add(Kind.White, 0, "Tableau blanc (W)");
            add(Kind.Black, 0, "Tableau noir (K)");
            add(Kind.Save, 0, "Capture : enregistrer dans Images + copier (Ctrl+S)");
            add(Kind.Separator, 0, null);
            add(Kind.Close, 0, "Quitter le dessin (Échap)");

            int x = pad;
            foreach (var bt in buttons)
            {
                if (bt.Kind == Kind.Separator) { bt.R = new Rectangle(x, pad, sep - gap, b); x += sep; continue; }
                bt.R = new Rectangle(x, pad, b, b);
                x += b + gap;
            }
            int w = x - gap + pad, h = b + pad * 2;
            int ox = (Bounds.Width - w) / 2, oy = (int)(14 * sc);
            foreach (var bt in buttons) bt.R.Offset(ox, oy);
            bar = new Rectangle(ox, oy, w, h);
            barArea = Rectangle.FromLTRB(bar.Left - (int)(160 * sc), bar.Top - 2, bar.Right + (int)(160 * sc), bar.Bottom + (int)(46 * sc));
        }

        int HitButton(Point p)
        {
            if (!barVisible || !bar.Contains(p)) return -1;
            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].Kind != Kind.Separator && buttons[i].R.Contains(p)) return i;
            return -1;
        }

        bool IsActive(Button b)
        {
            switch (b.Kind)
            {
                case Kind.Tool: return (int)tool == b.Index;
                case Kind.Color: return color.ToArgb() == Palette[b.Index].ToArgb();
                case Kind.White: return background == BoardBackground.White;
                case Kind.Black: return background == BoardBackground.Black;
            }
            return false;
        }

        void Press(Button b)
        {
            switch (b.Kind)
            {
                case Kind.Tool: SetTool((Tool)b.Index); break;
                case Kind.Color: SetColor(Palette[b.Index]); break;
                case Kind.Thinner: SetWidth(width - 1); break;
                case Kind.Thicker: SetWidth(width + 1); break;
                case Kind.Undo: Undo(); break;
                case Kind.Redo: Redo(); break;
                case Kind.Clear: ClearAll(); break;
                case Kind.White: SetBackground(background == BoardBackground.White ? BoardBackground.None : BoardBackground.White); break;
                case Kind.Black: SetBackground(background == BoardBackground.Black ? BoardBackground.None : BoardBackground.Black); break;
                case Kind.Save: if (CaptureRequested != null) CaptureRequested(true); break;
                case Kind.Close: if (CloseRequested != null) CloseRequested(); break;
            }
        }

        void DrawBar(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var path = Gfx.RoundRect(bar, 12 * sc))
            {
                using (var b = new SolidBrush(Color.FromArgb(242, 30, 32, 38))) g.FillPath(b, path);
                using (var p = new Pen(Color.FromArgb(60, 255, 255, 255), Math.Max(1f, sc))) g.DrawPath(p, path);
            }
            string iconFont = Gfx.IconFont;
            using (var glyphFont = iconFont != null ? new Font(iconFont, 15 * sc, GraphicsUnit.Pixel) : null)
            using (var textFont = new Font("Segoe UI", 15 * sc, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var small = new Font("Segoe UI", 9 * sc, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fg = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
            using (var fgPen = new Pen(Color.FromArgb(235, 255, 255, 255), 2 * sc))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                for (int i = 0; i < buttons.Count; i++)
                {
                    var bt = buttons[i];
                    var r = bt.R;
                    if (bt.Kind == Kind.Separator)
                    {
                        using (var p = new Pen(Color.FromArgb(50, 255, 255, 255), Math.Max(1f, sc)))
                            g.DrawLine(p, r.X + r.Width / 2f, r.Y + r.Height * 0.2f, r.X + r.Width / 2f, r.Y + r.Height * 0.8f);
                        continue;
                    }
                    bool active = IsActive(bt), hov = i == hover && bt.Kind != Kind.WidthShow;
                    if (active || hov)
                        using (var path = Gfx.RoundRect(r, 7 * sc))
                        using (var b = new SolidBrush(active ? Color.FromArgb(70, 255, 196, 0) : Color.FromArgb(28, 255, 255, 255)))
                            g.FillPath(b, path);

                    RectangleF ir = r;
                    ir.Inflate(-r.Width * 0.27f, -r.Height * 0.27f);
                    switch (bt.Kind)
                    {
                        case Kind.Tool: DrawToolIcon(g, (Tool)bt.Index, ir, fg, fgPen, glyphFont, textFont, sf); break;
                        case Kind.Color:
                        {
                            var cr = RectangleF.Inflate(r, -r.Width * 0.24f, -r.Height * 0.24f);
                            using (var b = new SolidBrush(Palette[bt.Index])) g.FillEllipse(b, cr);
                            using (var p = new Pen(Color.FromArgb(active ? 255 : 90, 255, 255, 255), (active ? 2.5f : 1f) * sc))
                                g.DrawEllipse(p, cr);
                            break;
                        }
                        case Kind.WidthShow:
                        {
                            float d = Math.Max(3 * sc, Math.Min(r.Width * 0.62f, width * sc));
                            using (var b = new SolidBrush(color))
                                g.FillEllipse(b, r.X + (r.Width - d) / 2, r.Y + (r.Height - d) / 2 - 3 * sc, d, d);
                            g.DrawString(width.ToString(), small, fg, new RectangleF(r.X, r.Bottom - 12 * sc, r.Width, 12 * sc), sf);
                            break;
                        }
                        case Kind.Thinner: Glyph(g, "", "−", r, fg, glyphFont, textFont, sf); break;
                        case Kind.Thicker: Glyph(g, "", "+", r, fg, glyphFont, textFont, sf); break;
                        case Kind.Undo: Glyph(g, "", "↶", r, fg, glyphFont, textFont, sf); break;
                        case Kind.Redo: Glyph(g, "", "↷", r, fg, glyphFont, textFont, sf); break;
                        case Kind.Clear: Glyph(g, "", "✕", r, fg, glyphFont, textFont, sf); break;
                        case Kind.Save: Glyph(g, "", "⎙", r, fg, glyphFont, textFont, sf); break;
                        case Kind.Close: Glyph(g, "", "✕", r, fg, glyphFont, textFont, sf); break;
                        case Kind.White:
                        case Kind.Black:
                        {
                            var br = RectangleF.Inflate(r, -r.Width * 0.25f, -r.Height * 0.3f);
                            using (var path = Gfx.RoundRect(br, 3 * sc))
                            {
                                using (var b = new SolidBrush(bt.Kind == Kind.White ? Color.White : Color.FromArgb(25, 27, 31))) g.FillPath(b, path);
                                using (var p = new Pen(Color.FromArgb(200, 255, 255, 255), 1.4f * sc)) g.DrawPath(p, path);
                            }
                            break;
                        }
                    }
                }

                if (hover >= 0 && buttons[hover].Tip != null)
                {
                    var tip = buttons[hover].Tip;
                    using (var tf = new Font("Segoe UI", 13 * sc, GraphicsUnit.Pixel))
                    {
                        var size = g.MeasureString(tip, tf);
                        var hr = buttons[hover].R;
                        var tr = new RectangleF(hr.X + hr.Width / 2f - size.Width / 2 - 8 * sc, bar.Bottom + 6 * sc, size.Width + 16 * sc, size.Height + 8 * sc);
                        using (var path = Gfx.RoundRect(tr, 6 * sc))
                        using (var b = new SolidBrush(Color.FromArgb(235, 20, 21, 25)))
                            g.FillPath(b, path);
                        g.DrawString(tip, tf, fg, tr, sf);
                    }
                }
            }
        }

        static void Glyph(Graphics g, string glyph, string fallback, RectangleF r, Brush fg, Font glyphFont, Font textFont, StringFormat sf)
        {
            g.DrawString(glyphFont != null ? glyph : fallback, glyphFont ?? textFont, fg, r, sf);
        }

        void DrawToolIcon(Graphics g, Tool t, RectangleF r, Brush fg, Pen pen, Font glyphFont, Font textFont, StringFormat sf)
        {
            var full = RectangleF.Inflate(r, r.Width * 0.37f, r.Height * 0.37f);
            switch (t)
            {
                case Tool.Pen: Glyph(g, "", "✎", full, fg, glyphFont, textFont, sf); break;
                case Tool.Marker:
                    if (glyphFont != null) Glyph(g, "", "", full, fg, glyphFont, textFont, sf);
                    else using (var p = new Pen(Color.FromArgb(160, 255, 210, 0), r.Height * 0.4f)) g.DrawLine(p, r.Left, r.Bottom - r.Height * 0.3f, r.Right, r.Top + r.Height * 0.3f);
                    break;
                case Tool.Line: g.DrawLine(pen, r.Left, r.Bottom, r.Right, r.Top); break;
                case Tool.Arrow:
                {
                    g.DrawLine(pen, r.Left, r.Bottom, r.Right - 1, r.Top + 1);
                    float h = r.Width * 0.45f;
                    g.DrawLines(pen, new[] { new PointF(r.Right - h, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Top + h) });
                    break;
                }
                case Tool.Rect: g.DrawRectangle(pen, r.X, r.Y + r.Height * 0.1f, r.Width, r.Height * 0.8f); break;
                case Tool.Ellipse: g.DrawEllipse(pen, r.X - 1, r.Y + r.Height * 0.05f, r.Width + 2, r.Height * 0.9f); break;
                case Tool.Text: g.DrawString("T", textFont, fg, full, sf); break;
            }
        }

        // ---------- rendu ----------

        Color BackgroundColor
        {
            get
            {
                switch (background)
                {
                    case BoardBackground.White: return Color.FromArgb(252, 252, 250);
                    case BoardBackground.Black: return Color.FromArgb(28, 31, 35);
                    // alpha 1 : invisible mais capte la souris
                    default: return Color.FromArgb(1, 0, 0, 0);
                }
            }
        }

        void RebuildCommitted()
        {
            cg.CompositingMode = CompositingMode.SourceCopy;
            cg.Clear(BackgroundColor);
            cg.CompositingMode = CompositingMode.SourceOver;
            foreach (var s in shapes) s.Draw(cg);
        }

        void Render(Rectangle dirty)
        {
            dirty.Intersect(LocalBounds);
            if (dirty.IsEmpty) return;
            var g = G;
            g.SetClip(dirty);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(committed, dirty, dirty, GraphicsUnit.Pixel);
            g.CompositingMode = CompositingMode.SourceOver;
            if (current != null) current.Draw(g);
            if (text != null)
            {
                text.Draw(g, caretOn);
                var tb = text.Bounds();
                using (var p = new Pen(Color.FromArgb(150, 128, 128, 128), 1) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(p, tb.X, tb.Y, tb.Width - 1, tb.Height - 1);
            }
            if (barVisible && dirty.IntersectsWith(barArea)) DrawBar(g);
            if (message != null && dirty.IntersectsWith(msgRect)) DrawMessage(g);
            g.ResetClip();
            Present(dirty);
        }

        public void ShowMessage(string msg)
        {
            var old = msgRect;
            message = msg;
            messageTime = Now;
            using (var f = new Font("Segoe UI", 15 * sc, GraphicsUnit.Pixel))
            {
                var size = G.MeasureString(msg, f);
                msgRect = new Rectangle((int)((Bounds.Width - size.Width) / 2 - 14 * sc), (int)(Bounds.Height - size.Height - 70 * sc),
                                        (int)(size.Width + 28 * sc), (int)(size.Height + 14 * sc));
            }
            Render(Gfx.Union(old, msgRect));
        }

        void DrawMessage(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var path = Gfx.RoundRect(msgRect, msgRect.Height / 2f))
            using (var b = new SolidBrush(Color.FromArgb(230, 22, 24, 30)))
                g.FillPath(b, path);
            using (var f = new Font("Segoe UI", 15 * sc, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(message, f, Brushes.White, msgRect, sf);
        }

        public void Tick()
        {
            long now = Now;
            if (message != null && now - messageTime > 2600)
            {
                message = null;
                Render(msgRect);
            }
            if (text != null && now - lastBlink > 530)
            {
                lastBlink = Now;
                caretOn = !caretOn;
                Render(text.Bounds());
            }
        }

        /// <summary>Dessine les annotations (avec le fond éventuel) sur une capture de l'écran.</summary>
        public void ComposeOnto(Graphics g)
        {
            CommitText();
            g.DrawImage(committed, 0, 0);
        }

        // ---------- actions ----------

        void SetTool(Tool t)
        {
            CommitText();
            tool = t;
            Render(barArea);
        }

        void SetColor(Color c)
        {
            color = c;
            if (text != null) { text.Color = c; Render(text.Bounds()); }
            Render(barArea);
        }

        void SetWidth(int w)
        {
            width = Math.Max(1, Math.Min(40, w));
            if (text != null)
            {
                var before = text.Bounds();
                text.FontSize = FontSizeFor(width);
                Render(Gfx.Union(before, text.Bounds()));
            }
            Render(barArea);
        }

        float FontSizeFor(int w) { return (16 + w * 3) * sc; }

        void SetBackground(BoardBackground bg)
        {
            background = bg;
            RebuildCommitted();
            Render(LocalBounds);
        }

        void PushUndo()
        {
            undo.Push(new List<Shape>(shapes));
            redo.Clear();
        }

        void Undo()
        {
            if (text != null) { var r = text.Bounds(); text = null; Render(r); return; }
            if (undo.Count == 0) return;
            redo.Push(new List<Shape>(shapes));
            shapes.Clear();
            shapes.AddRange(undo.Pop());
            RebuildCommitted();
            Render(LocalBounds);
        }

        void Redo()
        {
            if (redo.Count == 0) return;
            undo.Push(new List<Shape>(shapes));
            shapes.Clear();
            shapes.AddRange(redo.Pop());
            RebuildCommitted();
            Render(LocalBounds);
        }

        void ClearAll()
        {
            text = null;
            if (shapes.Count > 0) { PushUndo(); shapes.Clear(); }
            RebuildCommitted();
            Render(LocalBounds);
        }

        void Commit(Shape s)
        {
            PushUndo();
            shapes.Add(s);
            cg.CompositingMode = CompositingMode.SourceOver;
            s.Draw(cg);
        }

        void CommitText()
        {
            if (text == null) return;
            var t = text;
            text = null;
            if (!t.IsEmpty) Commit(t);
            Render(t.Bounds());
        }

        // ---------- souris / clavier ----------

        static Point PointFrom(IntPtr lParam)
        {
            long l = lParam.ToInt64();
            return new Point((short)(l & 0xFFFF), (short)((l >> 16) & 0xFFFF));
        }

        static bool ShiftDown { get { return Native.GetKeyState(0x10) < 0; } }
        static bool CtrlDown { get { return Native.GetKeyState(0x11) < 0; } }

        void OnMouseDown(Point p)
        {
            if (barVisible && bar.Contains(p))
            {
                int i = HitButton(p);
                if (i >= 0) Press(buttons[i]);
                return;
            }
            if (tool == Tool.Text)
            {
                CommitText();
                text = new Shape { Tool = Tool.Text, Color = color, FontSize = FontSizeFor(width), A = p };
                caretOn = true;
                lastBlink = Now;
                Render(text.Bounds());
                return;
            }
            current = new Shape { Tool = tool, Color = color, Width = width * sc, A = p, B = p };
            if (tool == Tool.Pen || tool == Tool.Marker) current.Points.Add(p);
            Native.SetCapture(Handle);
            Render(current.Bounds());
        }

        void OnMouseMove(Point p)
        {
            int h = current == null ? HitButton(p) : -1;
            if (h != hover) { hover = h; Render(barArea); }
            if (current == null) return;

            if (current.Tool == Tool.Pen || current.Tool == Tool.Marker)
            {
                var last = current.Points[current.Points.Count - 1];
                if (Math.Abs(last.X - p.X) + Math.Abs(last.Y - p.Y) < 2) return;
                current.Points.Add(p);
                Render(current.SegmentBounds(last, p));
                return;
            }
            var before = current.Bounds();
            current.B = ShiftDown ? Constrain(current.Tool, current.A, p) : (PointF)p;
            Render(Gfx.Union(before, current.Bounds()));
        }

        void OnMouseUp()
        {
            if (current == null) return;
            Native.ReleaseCapture();
            var s = current;
            current = null;
            if (!s.IsEmpty) Commit(s);
            Render(s.Bounds());
        }

        static PointF Constrain(Tool t, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            if (t == Tool.Rect || t == Tool.Ellipse)
            {
                float d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                return new PointF(a.X + Math.Sign(dx) * d, a.Y + Math.Sign(dy) * d);
            }
            double len = Math.Sqrt(dx * dx + dy * dy);
            double ang = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
            return new PointF(a.X + (float)(Math.Cos(ang) * len), a.Y + (float)(Math.Sin(ang) * len));
        }

        void OnKeyDown(int vk)
        {
            if (text != null) return; // la saisie de texte passe par WM_CHAR
            if (CtrlDown)
            {
                switch (vk)
                {
                    case 'Z': Undo(); break;
                    case 'Y': Redo(); break;
                    case 'S': if (CaptureRequested != null) CaptureRequested(true); break;
                    case 'C': if (CaptureRequested != null) CaptureRequested(false); break;
                }
                return;
            }
            if (vk >= '1' && vk <= '8') { SetColor(Palette[vk - '1']); return; }
            if (vk >= 0x61 && vk <= 0x68) { SetColor(Palette[vk - 0x61]); return; }
            switch (vk)
            {
                case 0x1B: if (CloseRequested != null) CloseRequested(); break;
                case 'P': SetTool(Tool.Pen); break;
                case 'M': SetTool(Tool.Marker); break;
                case 'L': SetTool(Tool.Line); break;
                case 'A': SetTool(Tool.Arrow); break;
                case 'R': SetTool(Tool.Rect); break;
                case 'E': SetTool(Tool.Ellipse); break;
                case 'T': SetTool(Tool.Text); break;
                case 'W': SetBackground(background == BoardBackground.White ? BoardBackground.None : BoardBackground.White); break;
                case 'K':
                case 'B': SetBackground(background == BoardBackground.Black ? BoardBackground.None : BoardBackground.Black); break;
                case 0x2E:
                case 0x08: ClearAll(); break;
                case 0x6B:
                case 0xBB: SetWidth(width + 1); break;
                case 0x6D:
                case 0xBD: SetWidth(width - 1); break;
                case 0x09: barVisible = !barVisible; hover = -1; Render(barArea); break;
            }
        }

        void OnChar(char c)
        {
            if (text == null) return;
            var before = text.Bounds();
            switch (c)
            {
                case '\x1b': text = null; Render(before); return;
                case '\r':
                    if (ShiftDown) text.Text += "\n";
                    else { CommitText(); return; }
                    break;
                case '\b':
                    if (text.Text.Length > 0) text.Text = text.Text.Substring(0, text.Text.Length - 1);
                    break;
                default:
                    if (c < 32) return;
                    text.Text += c;
                    break;
            }
            caretOn = true;
            lastBlink = Now;
            Render(Gfx.Union(before, text.Bounds()));
        }

        protected override void WndProc(ref Message m)
        {
            try
            {
                switch (m.Msg)
                {
                    case Native.WM_LBUTTONDOWN: OnMouseDown(PointFrom(m.LParam)); return;
                    case Native.WM_MOUSEMOVE: OnMouseMove(PointFrom(m.LParam)); return;
                    case Native.WM_LBUTTONUP: OnMouseUp(); return;
                    case Native.WM_MOUSEWHEEL:
                        SetWidth(width + ((short)((m.WParam.ToInt64() >> 16) & 0xFFFF) > 0 ? 1 : -1));
                        return;
                    case Native.WM_GETDLGCODE:
                        // sinon la boucle WinForms (IsDialogMessage) avale Échap, Tab et Entrée
                        m.Result = new IntPtr(Native.DLGC_WANTALLKEYS);
                        return;
                    case Native.WM_KEYDOWN: OnKeyDown(m.WParam.ToInt32()); return;
                    case Native.WM_CHAR: OnChar((char)m.WParam.ToInt32()); return;
                    case Native.WM_SETCURSOR:
                        if ((m.LParam.ToInt64() & 0xFFFF) == 1) // HTCLIENT
                        {
                            Native.POINT pt;
                            Native.GetCursorPos(out pt);
                            var local = new Point(pt.X - Bounds.X, pt.Y - Bounds.Y);
                            Native.SetCursor(HitButton(local) >= 0 ? cursorHand
                                : barVisible && bar.Contains(local) ? cursorArrow
                                : tool == Tool.Text ? cursorBeam : cursorCross);
                            m.Result = new IntPtr(1);
                            return;
                        }
                        break;
                }
            }
            catch (Exception ex) { Program.Log(ex); }
            base.WndProc(ref m);
        }

        public override void Dispose()
        {
            Native.ReleaseCapture();
            cg.Dispose();
            committed.Dispose();
            base.Dispose();
        }
    }
}
