using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Tomee
{
    /// <summary>Application résidente : icône de notification, raccourcis globaux, boucle d'animation.</summary>
    internal sealed class TrayApp : ApplicationContext
    {
        readonly Settings S;
        readonly KeyDisplay keys;
        readonly InputHooks hooks = new InputHooks();
        readonly EffectsEngine fx;
        readonly ScreenZoom zoom = new ScreenZoom();
        readonly Hotkeys hotkeys = new Hotkeys();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Icon iconOn, iconPaused;
        readonly SettingsHost settingsWindow;

        DrawBoard board;
        int appliedRevision = -1;
        long saveDue = -1, lastTopmost;
        volatile bool displayChanged;
        bool closeBoardRequested;

        MenuItem miSpot, miRing, miClicks, miTrail, miKeys, miZoom, miPause;

        readonly EventWaitHandle showSettingsRequest, exitRequest;

        public TrayApp(EventWaitHandle showSettingsRequest, EventWaitHandle exitRequest)
        {
            this.showSettingsRequest = showSettingsRequest;
            this.exitRequest = exitRequest;
            S = Settings.Load();
            keys = new KeyDisplay(S);
            fx = new EffectsEngine(S, keys);
            settingsWindow = new SettingsHost(S);

            int iconSize = Math.Max(16, SystemInformation.SmallIconSize.Width);
            iconOn = AppIcons.Make(iconSize, false);
            iconPaused = AppIcons.Make(iconSize, true);
            tray.Icon = iconOn;
            tray.Text = AppInfo.Name;
            tray.ContextMenu = BuildMenu();
            tray.DoubleClick += delegate { settingsWindow.Show(); };
            tray.Visible = true;

            hotkeys.Pressed += OnHotkey;
            hooks.OnMouse = OnMouse;
            hooks.OnKey = OnKey;
            hooks.StartMouse();

            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

            timer.Interval = 15;
            timer.Tick += OnTick;
            timer.Start();

            if (S.FirstRun)
            {
                S.FirstRun = false;
                S.Save();
                tray.ShowBalloonTip(6000, AppInfo.Name + " est prêt",
                    "Ctrl+Alt+S : projecteur · Ctrl+Alt+D : dessiner · Ctrl+Alt+Z : zoom.\nDouble-cliquez sur l'icône pour les paramètres.",
                    ToolTipIcon.Info);
            }
        }

        // ---------- menu ----------

        ContextMenu BuildMenu()
        {
            var m = new ContextMenu();
            miSpot = Item(m, "Projecteur", HotkeyId.Spotlight);
            miRing = Item(m, "Halo autour du curseur", HotkeyId.Ring);
            miClicks = Item(m, "Effets de clic", HotkeyId.Clicks);
            miTrail = Item(m, "Traînée du curseur", HotkeyId.Trail);
            miKeys = Item(m, "Afficher les touches", HotkeyId.Keys);
            m.MenuItems.Add("-");
            Item(m, "Dessiner sur l'écran", HotkeyId.Draw);
            Item(m, "Tableau blanc", HotkeyId.Whiteboard);
            miZoom = Item(m, "Zoom", HotkeyId.Zoom);
            Item(m, "Localiser le curseur", HotkeyId.Find);
            m.MenuItems.Add("-");
            miPause = Item(m, "Pause", HotkeyId.Pause);
            var settings = new MenuItem("Paramètres…", delegate { settingsWindow.Show(); });
            settings.DefaultItem = true;
            m.MenuItems.Add(settings);
            m.MenuItems.Add(new MenuItem("Quitter", delegate { ExitThread(); }));
            m.Popup += delegate { UpdateMenu(); };
            return m;
        }

        MenuItem Item(ContextMenu m, string text, HotkeyId id)
        {
            var mi = new MenuItem(text + "\t" + Hotkeys.ComboFor(id), delegate { Run(id, false); });
            m.MenuItems.Add(mi);
            return mi;
        }

        void UpdateMenu()
        {
            miSpot.Checked = S.SpotlightEnabled;
            miRing.Checked = S.RingEnabled;
            miClicks.Checked = S.ClicksEnabled;
            miTrail.Checked = S.TrailEnabled;
            miKeys.Checked = S.KeysEnabled;
            miZoom.Checked = zoom.Active;
            miPause.Checked = S.Paused;
        }

        // ---------- actions ----------

        void OnHotkey(HotkeyId id) { Run(id, true); }

        void Run(HotkeyId id, bool fromKeyboard)
        {
            switch (id)
            {
                case HotkeyId.Spotlight: S.SpotlightEnabled = !S.SpotlightEnabled; Changed("Projecteur", S.SpotlightEnabled); break;
                case HotkeyId.Ring: S.RingEnabled = !S.RingEnabled; Changed("Halo", S.RingEnabled); break;
                case HotkeyId.Clicks: S.ClicksEnabled = !S.ClicksEnabled; Changed("Effets de clic", S.ClicksEnabled); break;
                case HotkeyId.Trail: S.TrailEnabled = !S.TrailEnabled; Changed("Traînée", S.TrailEnabled); break;
                case HotkeyId.Keys: S.KeysEnabled = !S.KeysEnabled; Changed("Affichage des touches", S.KeysEnabled); break;
                case HotkeyId.Pause: S.Paused = !S.Paused; fx.Toast(S.Paused ? "Effets en pause" : "Effets réactivés"); break;
                case HotkeyId.Find: fx.Find(); break;
                case HotkeyId.Draw: ToggleBoard(BoardBackground.None); break;
                case HotkeyId.Whiteboard: ToggleBoard(BoardBackground.White); break;
                case HotkeyId.Zoom:
                    if (!zoom.Toggle(S.ZoomPercent)) fx.Toast("Zoom indisponible sur ce système");
                    else fx.Toast(zoom.Active ? "Zoom " + S.ZoomPercent + " %" : "Zoom désactivé");
                    break;
                case HotkeyId.ZoomIn: StepZoom(25); break;
                case HotkeyId.ZoomOut: StepZoom(-25); break;
            }
        }

        void Changed(string what, bool on)
        {
            S.Touch();
            fx.Toast(what + (on ? " : activé" : " : désactivé"));
        }

        void StepZoom(int delta)
        {
            S.ZoomPercent = Math.Max(125, Math.Min(800, S.ZoomPercent + delta));
            S.Touch();
            if (!zoom.Active) zoom.Toggle(S.ZoomPercent);
            zoom.SetPercent(S.ZoomPercent);
            fx.Toast("Zoom " + S.ZoomPercent + " %");
        }

        void ToggleBoard(BoardBackground bg)
        {
            if (board != null)
            {
                if (bg != BoardBackground.None && board.Background != bg) return; // Ctrl+Alt+W pendant le dessin : rien
                CloseBoard();
                return;
            }
            var mon = fx.CursorMonitor;
            board = new DrawBoard(mon, fx.CursorScale, S, bg);
            board.CloseRequested += delegate { closeBoardRequested = true; };
            board.CaptureRequested += CaptureBoard;
            fx.SuppressClicks = true;
            zoom.Frozen = true;
            fx.BringToTop();
        }

        void CloseBoard()
        {
            if (board == null) return;
            S.DrawColor = board.CurrentColor;
            S.DrawWidth = board.CurrentWidth;
            S.Touch();
            board.Dispose();
            board = null;
            fx.SuppressClicks = false;
            zoom.Frozen = false;
        }

        void CaptureBoard(bool toFile)
        {
            var b = board;
            if (b == null) return;
            var bounds = b.Bounds;
            using (var shot = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
            {
                fx.HideNow(true);
                b.Hide();
                Native.DwmFlush();
                Native.DwmFlush();
                try
                {
                    using (var g = Graphics.FromImage(shot))
                    {
                        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                        b.ComposeOnto(g);
                    }
                }
                finally
                {
                    b.Show(true);
                    Native.SetForegroundWindow(b.Handle);
                    fx.HideNow(false);
                }

                try
                {
                    // la capture peut montrer des informations sensibles : pas de synchronisation vers le presse-papiers cloud
                    var data = new DataObject();
                    data.SetImage(shot);
                    data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
                    Clipboard.SetDataObject(data, true);
                    if (toFile)
                    {
                        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), AppInfo.Name);
                        Directory.CreateDirectory(dir);
                        var file = Path.Combine(dir, "Annotation_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png");
                        shot.Save(file, ImageFormat.Png);
                        b.ShowMessage("Capture enregistrée dans Images\\" + AppInfo.Name + " et copiée dans le presse-papiers");
                    }
                    else b.ShowMessage("Capture copiée dans le presse-papiers");
                }
                catch (Exception ex)
                {
                    Program.Log(ex);
                    b.ShowMessage("Impossible d'enregistrer la capture");
                }
            }
        }

        // ---------- entrées ----------

        bool OnMouse(int msg, Point pt, int delta)
        {
            long now = clock.ElapsedMilliseconds;
            switch (msg)
            {
                case Native.WM_MOUSEMOVE: fx.OnMouseMove(pt, now); return false;
                case Native.WM_MOUSEWHEEL:
                    if (zoom.Active && board == null && Native.IsDown(0x11) && Native.IsDown(0x12))
                    {
                        StepZoom(delta > 0 ? 25 : -25);
                        return true;
                    }
                    return false;
                case Native.WM_LBUTTONDOWN: ButtonDown(1, pt, now, "Clic gauche"); return false;
                case Native.WM_RBUTTONDOWN: ButtonDown(2, pt, now, "Clic droit"); return false;
                case Native.WM_MBUTTONDOWN: ButtonDown(4, pt, now, "Clic milieu"); return false;
                case Native.WM_LBUTTONUP: fx.OnButton(1, false, pt, now); return false;
                case Native.WM_RBUTTONUP: fx.OnButton(2, false, pt, now); return false;
                case Native.WM_MBUTTONUP: fx.OnButton(4, false, pt, now); return false;
            }
            return false;
        }

        void ButtonDown(int button, Point pt, long now, string name)
        {
            fx.OnButton(button, true, pt, now);
            if (S.Paused || board != null) return;
            if (S.ClicksEnabled && S.ClickSound) ClickSound.Play(button == 2, S.ClickVolume);
            if (S.KeysEnabled && S.KeysShowMouse && keys.OnMouseDown(name, now)) fx.Poke();
        }

        void OnKey(int vk, int scan, bool down)
        {
            if (keys.OnKey(vk, scan, down, clock.ElapsedMilliseconds)) fx.Poke();
        }

        void OnDisplayChanged(object sender, EventArgs e) { displayChanged = true; }

        // ---------- boucle ----------

        void OnTick(object sender, EventArgs e)
        {
            try
            {
                long now = clock.ElapsedMilliseconds;

                if (exitRequest.WaitOne(0)) { ExitThread(); return; }
                if (showSettingsRequest.WaitOne(0)) settingsWindow.Show();
                if (closeBoardRequested)
                {
                    closeBoardRequested = false;
                    CloseBoard();
                }
                if (displayChanged)
                {
                    displayChanged = false;
                    CloseBoard();
                    fx.Rebuild();
                }

                int rev = S.Revision;
                if (rev != appliedRevision)
                {
                    appliedRevision = rev;
                    ApplySettings();
                    saveDue = now + 800;
                }
                if (saveDue >= 0 && now >= saveDue)
                {
                    saveDue = -1;
                    S.Save();
                }

                fx.Paused = S.Paused;
                fx.Tick(now);
                zoom.Tick(fx.Cursor, fx.CursorMonitor, S.ZoomSmooth);
                if (board != null) board.Tick();

                if (now - lastTopmost > 1500)
                {
                    lastTopmost = now;
                    fx.BringToTop();
                }
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        void ApplySettings()
        {
            bool keyHook = S.KeysEnabled && !S.Paused;
            hooks.SetKeyboard(keyHook);
            if (!keyHook) keys.Reset();
            if (S.Paused && zoom.Active) zoom.Toggle(S.ZoomPercent);
            zoom.SetPercent(S.ZoomPercent);
            tray.Icon = S.Paused ? iconPaused : iconOn;
            tray.Text = S.Paused ? AppInfo.Name + " (en pause)" : AppInfo.Name;
            fx.ForceFull();
        }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            settingsWindow.Close();
            CloseBoard();
            S.Save();
            hooks.Dispose();
            hotkeys.Dispose();
            zoom.Dispose();
            fx.Dispose();
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }
    }

    /// <summary>
    /// Ouvre la fenêtre de paramètres sur son propre thread, en mode « DPI système »,
    /// pour que WinForms la mette à l'échelle correctement (le thread principal est « per-monitor »).
    /// </summary>
    internal sealed class SettingsHost
    {
        readonly Settings S;
        Thread thread;
        volatile SettingsForm form;

        public SettingsHost(Settings s) { S = s; }

        public void Show()
        {
            var f = form;
            if (thread != null && thread.IsAlive)
            {
                if (f != null)
                    try
                    {
                        f.BeginInvoke((Action)delegate
                        {
                            f.WindowState = FormWindowState.Normal;
                            f.Activate();
                            Native.SetForegroundWindow(f.Handle);
                        });
                    }
                    catch (InvalidOperationException) { }
                return;
            }
            thread = new Thread(() =>
            {
                try { Native.SetThreadDpiAwarenessContext(new IntPtr(-2)); } catch (EntryPointNotFoundException) { }
                try
                {
                    form = new SettingsForm(S);
                    Application.Run(form);
                }
                catch (Exception ex) { Program.Log(ex); }
                form = null;
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        public void Close()
        {
            var f = form;
            if (f != null)
                try { f.BeginInvoke((Action)f.Close); } catch (InvalidOperationException) { }
        }
    }
}
