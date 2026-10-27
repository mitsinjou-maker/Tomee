using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Tomee
{
    /// <summary>Fenêtre de paramètres. Les changements s'appliquent immédiatement (aperçu en direct).</summary>
    internal sealed class SettingsForm : Form
    {
        static readonly Color[] Presets =
        {
            Color.FromArgb(255, 196, 0), Color.FromArgb(255, 72, 72), Color.FromArgb(64, 140, 255),
            Color.FromArgb(70, 200, 110), Color.FromArgb(230, 70, 220), Color.FromArgb(255, 255, 255),
        };

        readonly Settings S;
        readonly List<Action> refreshers = new List<Action>();
        readonly TabControl tabs;
        readonly Timer poll = new Timer();
        readonly float k;
        bool refreshing;
        int seenRevision = -1;

        public SettingsForm(Settings s)
        {
            S = s;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
            SuspendLayout();
            // Mise en page écrite à 96 ppp puis mise à l'échelle explicitement (les polices en points suivent seules).
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9F);
            Text = AppInfo.Name + " — Paramètres";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(620, 540);
            Icon = AppIcons.Make(32, false);

            tabs = new TabControl { Location = new Point(10, 10), Size = new Size(600, 482) };
            Controls.Add(tabs);
            BuildPages();

            var reset = new System.Windows.Forms.Button { Text = "Réinitialiser", Location = new Point(10, 502), Size = new Size(110, 28) };
            reset.Click += delegate
            {
                if (MessageBox.Show(this, "Rétablir tous les réglages par défaut ?", AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    S.ResetToDefaults();
            };
            Controls.Add(reset);
            var close = new System.Windows.Forms.Button { Text = "Fermer", Location = new Point(510, 502), Size = new Size(100, 28) };
            close.Click += delegate { Close(); };
            Controls.Add(close);
            CancelButton = close;

            if (k > 1.01f)
            {
                foreach (Control c in Controls) c.Scale(new SizeF(k, k));
                ClientSize = new Size((int)(620 * k), (int)(540 * k));
            }
            ResumeLayout(false);

            poll.Interval = 250;
            poll.Tick += delegate { if (S.Revision != seenRevision) SyncControls(); };
            poll.Start();
            SyncControls();
        }

        void SyncControls()
        {
            seenRevision = S.Revision;
            refreshing = true;
            try { foreach (var r in refreshers) r(); }
            finally { refreshing = false; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            Native.SetForegroundWindow(Handle);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            poll.Stop();
            poll.Dispose();
            base.OnFormClosed(e);
        }

        void Changed() { if (!refreshing) S.Touch(); }

        // ---------- pages ----------

        void BuildPages()
        {
            var p = Page("Général");
            p.Header("Mettre en valeur votre curseur en réunion, présentation ou formation");
            p.Check("Mettre en pause tous les effets", () => S.Paused, v => S.Paused = v);
            p.Check("Lancer " + AppInfo.Name + " au démarrage de Windows", () => Autostart.IsEnabled, Autostart.Set);
            p.Check("Afficher une notification quand un effet est activé/désactivé", () => S.ShowToasts, v => S.ShowToasts = v);
            p.Gap();
            p.Header("Effets actifs");
            p.Check("Projecteur — assombrit l'écran autour du curseur   (Ctrl+Alt+S)", () => S.SpotlightEnabled, v => S.SpotlightEnabled = v);
            p.Check("Halo autour du curseur   (Ctrl+Alt+H)", () => S.RingEnabled, v => S.RingEnabled = v);
            p.Check("Effets de clic   (Ctrl+Alt+C)", () => S.ClicksEnabled, v => S.ClicksEnabled = v);
            p.Check("Traînée du curseur   (Ctrl+Alt+T)", () => S.TrailEnabled, v => S.TrailEnabled = v);
            p.Check("Afficher les touches et raccourcis   (Ctrl+Alt+K)", () => S.KeysEnabled, v => S.KeysEnabled = v);
            p.Gap();
            p.Note("Les effets apparaissent dans les partages d'écran (Teams, Zoom, Meet, OBS, enregistreurs) " +
                   "quand vous partagez l'écran entier. Double-cliquez sur l'icône de la zone de notification pour rouvrir cette fenêtre.", 3);

            p = Page("Projecteur");
            p.Check("Activer le projecteur", () => S.SpotlightEnabled, v => S.SpotlightEnabled = v);
            p.Slider("Rayon de la zone éclairée", 50, 600, " px", () => S.SpotlightRadius, v => S.SpotlightRadius = v);
            p.Slider("Assombrissement", 10, 95, " %", () => S.SpotlightDim, v => S.SpotlightDim = v);
            p.Slider("Douceur du bord", 0, 150, " px", () => S.SpotlightFeather, v => S.SpotlightFeather = v);
            p.Note("Idéal pour attirer l'attention sur une zone précise de l'écran pendant une démonstration.", 2);

            p = Page("Halo");
            p.Check("Activer le halo", () => S.RingEnabled, v => S.RingEnabled = v);
            p.ColorPick("Couleur", () => S.RingColor, v => S.RingColor = v);
            p.Slider("Opacité du contour", 10, 100, " %", () => S.RingOpacity, v => S.RingOpacity = v);
            p.Slider("Opacité du remplissage", 0, 80, " %", () => S.RingFillOpacity, v => S.RingFillOpacity = v);
            p.Slider("Rayon", 8, 90, " px", () => S.RingRadius, v => S.RingRadius = v);
            p.Slider("Épaisseur", 1, 16, " px", () => S.RingThickness, v => S.RingThickness = v);
            p.Check("Masquer le halo quand le curseur est masqué (diaporama, vidéo)", () => S.RingHideWithCursor, v => S.RingHideWithCursor = v);

            p = Page("Clics");
            p.Check("Activer les effets de clic", () => S.ClicksEnabled, v => S.ClicksEnabled = v);
            p.ColorPick("Clic gauche", () => S.LeftClickColor, v => S.LeftClickColor = v);
            p.ColorPick("Clic droit", () => S.RightClickColor, v => S.RightClickColor = v);
            p.ColorPick("Clic milieu", () => S.MiddleClickColor, v => S.MiddleClickColor = v);
            p.Slider("Taille de l'onde", 20, 160, " px", () => S.RippleRadius, v => S.RippleRadius = v);
            p.Slider("Durée de l'onde", 150, 1200, " ms", () => S.RippleDuration, v => S.RippleDuration = v);
            p.Check("Son de clic", () => S.ClickSound, v => S.ClickSound = v);
            p.Slider("Volume du son", 5, 100, " %", () => S.ClickVolume, v => S.ClickVolume = v);

            p = Page("Traînée");
            p.Check("Activer la traînée", () => S.TrailEnabled, v => S.TrailEnabled = v);
            p.ColorPick("Couleur", () => S.TrailColor, v => S.TrailColor = v);
            p.Slider("Opacité", 10, 100, " %", () => S.TrailOpacity, v => S.TrailOpacity = v);
            p.Slider("Longueur", 100, 1500, " ms", () => S.TrailLength, v => S.TrailLength = v);
            p.Slider("Épaisseur", 2, 30, " px", () => S.TrailWidth, v => S.TrailWidth = v);

            p = Page("Touches");
            p.Check("Afficher les touches pressées", () => S.KeysEnabled, v => S.KeysEnabled = v);
            p.Check("Raccourcis uniquement (Ctrl, Alt, Win, Échap, F1…) — recommandé", () => S.KeysShortcutsOnly, v => S.KeysShortcutsOnly = v);
            p.Check("Afficher aussi les clics combinés (ex. Ctrl + Clic gauche)", () => S.KeysShowMouse, v => S.KeysShowMouse = v);
            p.Combo("Position", new[] { "En bas au centre", "En bas à gauche", "En bas à droite", "En haut au centre" },
                    () => S.KeysPosition, v => S.KeysPosition = v);
            p.Slider("Taille du texte", 14, 72, " px", () => S.KeysFontSize, v => S.KeysFontSize = v);
            p.Slider("Durée d'affichage", 600, 5000, " ms", () => S.KeysDuration, v => S.KeysDuration = v);
            p.Note("Confidentialité : en mode « raccourcis uniquement », le texte que vous tapez (mots de passe…) n'est jamais affiché. " +
                   "Désactivez ce mode seulement pour montrer une saisie à l'écran.", 3);

            p = Page("Zoom et dessin");
            p.Header("Zoom (Ctrl+Alt+Z, puis Ctrl+Alt+molette ou Ctrl+Alt+↑/↓)");
            p.Slider("Niveau de zoom", 125, 800, " %", () => S.ZoomPercent, v => S.ZoomPercent = v, 25);
            p.Check("Zoom et suivi du curseur fluides", () => S.ZoomSmooth, v => S.ZoomSmooth = v);
            p.Gap();
            p.Header("Dessin à l'écran (Ctrl+Alt+D) et tableau blanc (Ctrl+Alt+W)");
            p.ColorPick("Couleur par défaut", () => S.DrawColor, v => S.DrawColor = v, DrawBoard.Palette);
            p.Slider("Épaisseur par défaut", 1, 40, " px", () => S.DrawWidth, v => S.DrawWidth = v);

            BuildShortcutsPage();
        }

        void BuildShortcutsPage()
        {
            var tab = new TabPage("Raccourcis") { UseVisualStyleBackColor = true };
            var list = new ListView
            {
                View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Location = new Point(10, 10), Size = new Size(570, 435), GridLines = false,
            };
            list.Columns.Add("Raccourci", (int)(190 * k));
            list.Columns.Add("Action", (int)(355 * k));
            var global = new ListViewGroup("Raccourcis globaux");
            var draw = new ListViewGroup("En mode dessin");
            list.Groups.Add(global);
            list.Groups.Add(draw);
            foreach (var h in Hotkeys.All)
            {
                var item = new ListViewItem(new[] { h.Combo, h.Label + (h.Failed ? "   ⚠ déjà utilisé par une autre application" : "") }, global);
                if (h.Failed) item.ForeColor = Color.Firebrick;
                list.Items.Add(item);
            }
            string[,] drawKeys =
            {
                { "P / M / L / A", "Stylo / surligneur / ligne / flèche" },
                { "R / E / T", "Rectangle / ellipse / texte" },
                { "Maj (en traçant)", "Carré, cercle ou angles de 45°" },
                { "1 … 8", "Couleurs (rouge, vert, bleu, jaune, orange, rose, noir, blanc)" },
                { "Molette ou + / −", "Épaisseur du trait / taille du texte" },
                { "Ctrl + Z / Ctrl + Y", "Annuler / rétablir" },
                { "Suppr", "Tout effacer" },
                { "W / K", "Tableau blanc / tableau noir" },
                { "Ctrl + S", "Capture enregistrée dans Images\\" + AppInfo.Name + " et copiée" },
                { "Ctrl + C", "Capture copiée dans le presse-papiers" },
                { "Tab", "Masquer / afficher la barre d'outils" },
                { "Échap", "Quitter le mode dessin" },
            };
            for (int i = 0; i < drawKeys.GetLength(0); i++)
                list.Items.Add(new ListViewItem(new[] { drawKeys[i, 0], drawKeys[i, 1] }, draw));
            tab.Controls.Add(list);
            tabs.TabPages.Add(tab);
        }

        PageBuilder Page(string title)
        {
            var tab = new TabPage(title) { UseVisualStyleBackColor = true, AutoScroll = true };
            tabs.TabPages.Add(tab);
            return new PageBuilder(this, tab);
        }

        sealed class PageBuilder
        {
            const int Left = 16, CtrlX = 250, CtrlW = 320;
            readonly SettingsForm f;
            readonly TabPage tab;
            int y = 14;

            public PageBuilder(SettingsForm form, TabPage page) { f = form; tab = page; }

            public void Gap() { y += 10; }

            public void Header(string text)
            {
                tab.Controls.Add(new Label
                {
                    Text = text, AutoSize = true, Location = new Point(Left - 2, y),
                    Font = new Font("Segoe UI Semibold", 9.5F), ForeColor = Color.FromArgb(30, 60, 110),
                });
                y += 28;
            }

            public void Note(string text, int lines)
            {
                tab.Controls.Add(new Label
                {
                    Text = text, AutoSize = false, Location = new Point(Left, y + 4),
                    Size = new Size(550, lines * 17 + 4), ForeColor = SystemColors.GrayText,
                });
                y += lines * 17 + 14;
            }

            public void Check(string text, Func<bool> get, Action<bool> set)
            {
                var cb = new CheckBox { Text = text, AutoSize = true, Location = new Point(Left, y) };
                cb.CheckedChanged += delegate { if (f.refreshing) return; set(cb.Checked); f.Changed(); };
                f.refreshers.Add(() => cb.Checked = get());
                tab.Controls.Add(cb);
                y += 28;
            }

            void Caption(string text)
            {
                tab.Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(Left, y + 6) });
            }

            public void Slider(string text, int min, int max, string unit, Func<int> get, Action<int> set, int step = 1)
            {
                Caption(text);
                var tb = new TrackBar
                {
                    Minimum = min / step, Maximum = max / step, TickStyle = TickStyle.None, AutoSize = false,
                    Location = new Point(CtrlX - 6, y), Size = new Size(CtrlW - 60, 30),
                    SmallChange = 1, LargeChange = Math.Max(1, (max - min) / step / 10),
                };
                var value = new Label { AutoSize = true, Location = new Point(CtrlX + CtrlW - 62, y + 6) };
                tb.ValueChanged += delegate
                {
                    value.Text = tb.Value * step + unit;
                    if (f.refreshing) return;
                    set(tb.Value * step);
                    f.Changed();
                };
                f.refreshers.Add(() =>
                {
                    tb.Value = Math.Max(tb.Minimum, Math.Min(tb.Maximum, get() / step));
                    value.Text = tb.Value * step + unit;
                });
                tab.Controls.Add(tb);
                tab.Controls.Add(value);
                y += 36;
            }

            public void ColorPick(string text, Func<Color> get, Action<Color> set, Color[] presets = null)
            {
                Caption(text);
                var main = new System.Windows.Forms.Button
                {
                    Location = new Point(CtrlX, y + 2), Size = new Size(54, 24), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                };
                main.FlatAppearance.BorderColor = Color.Gray;
                main.Click += delegate
                {
                    using (var dlg = new ColorDialog { Color = get(), FullOpen = true, AnyColor = true })
                    {
                        if (dlg.ShowDialog(f) != DialogResult.OK) return;
                        set(dlg.Color);
                        f.Changed();
                    }
                };
                f.refreshers.Add(() => main.BackColor = get());
                tab.Controls.Add(main);

                int x = CtrlX + 66;
                foreach (var c in presets ?? Presets)
                {
                    var color = c;
                    var sw = new System.Windows.Forms.Button
                    {
                        Location = new Point(x, y + 4), Size = new Size(20, 20), BackColor = color,
                        FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, TabStop = false,
                    };
                    sw.FlatAppearance.BorderColor = Color.Silver;
                    sw.Click += delegate { set(color); f.Changed(); };
                    tab.Controls.Add(sw);
                    x += 26;
                }
                y += 34;
            }

            public void Combo(string text, string[] items, Func<int> get, Action<int> set)
            {
                Caption(text);
                var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(CtrlX, y + 2), Width = 200 };
                cb.Items.AddRange(items);
                cb.SelectedIndexChanged += delegate { if (f.refreshing || cb.SelectedIndex < 0) return; set(cb.SelectedIndex); f.Changed(); };
                f.refreshers.Add(() => cb.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, get())));
                tab.Controls.Add(cb);
                y += 34;
            }
        }
    }
}
