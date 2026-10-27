using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Tomee
{
    /// <summary>
    /// Installateur / désinstallateur de Tomee (installation par utilisateur, sans droits administrateur).
    ///   Tomee-Setup.exe               assistant d'installation
    ///   Tomee-Setup.exe /S            installation silencieuse ; options /desktop, /launch, /autostart
    ///   Desinstaller.exe /uninstall   désinstallation (/S pour silencieux)
    /// </summary>
    internal static class SetupProgram
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch (EntryPointNotFoundException) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool silent = false, uninstall = false, desktop = false, launch = false, autostart = Installer.AutostartEnabled;
            foreach (var a in args)
            {
                var x = a.ToLowerInvariant();
                if (x == "/s" || x == "/silent") silent = true;
                if (x == "/uninstall") uninstall = true;
                if (x == "/desktop") desktop = true;
                if (x == "/launch") launch = true;
                if (x == "/autostart") autostart = true;
            }

            try
            {
                if (uninstall) return Installer.Uninstall(silent) ? 0 : 1;
                if (silent)
                {
                    Installer.Install(Installer.DefaultFolder, desktop, autostart, launch, null);
                    return 0;
                }
                Application.Run(new SetupForm());
                return 0;
            }
            catch (Exception ex)
            {
                if (!silent) MessageBox.Show(ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
        }
    }

    internal static class Installer
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppInfo.Name;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string UninstallerName = "Desinstaller.exe";

        public static string DefaultFolder
        {
            get
            {
                var existing = InstalledFolder;
                if (existing != null) return existing;
                return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"), AppInfo.Name);
            }
        }

        /// <summary>Dossier d'une installation existante, ou null.</summary>
        public static string InstalledFolder
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                {
                    var v = k != null ? k.GetValue("InstallLocation") as string : null;
                    return string.IsNullOrEmpty(v) ? null : v;
                }
            }
        }

        public static bool AutostartEnabled
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(AppInfo.Name) != null;
            }
        }

        static string StartMenuShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppInfo.Name + ".lnk"); }
        }

        static string DesktopShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppInfo.Name + ".lnk"); }
        }

        public static void Install(string folder, bool desktop, bool autostart, bool launch, Action<string> progress)
        {
            Action<string> say = progress;
            if (say == null) say = delegate { };
            folder = Path.GetFullPath(folder);
            string exe = Path.Combine(folder, AppInfo.ExeName);

            say("Fermeture de " + AppInfo.Name + " s'il est en cours…");
            CloseRunningInstances();

            say("Copie des fichiers…");
            Directory.CreateDirectory(folder);
            long size;
            using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("Tomee.App.exe"))
            {
                if (payload == null) throw new InvalidOperationException("Installateur incomplet : application introuvable.");
                size = payload.Length;
                Retry(() =>
                {
                    payload.Position = 0;
                    using (var f = File.Create(exe)) payload.CopyTo(f);
                });
            }
            string uninstaller = Path.Combine(folder, UninstallerName);
            if (!string.Equals(Path.GetFullPath(Application.ExecutablePath), uninstaller, StringComparison.OrdinalIgnoreCase))
                Retry(() => File.Copy(Application.ExecutablePath, uninstaller, true));

            say("Création des raccourcis…");
            CreateShortcut(StartMenuShortcut, exe, folder);
            if (desktop) CreateShortcut(DesktopShortcut, exe, folder);
            else if (File.Exists(DesktopShortcut)) File.Delete(DesktopShortcut);

            say("Enregistrement dans Windows…");
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", AppInfo.Name);
                k.SetValue("DisplayVersion", AppInfo.Version);
                k.SetValue("Publisher", AppInfo.Name);
                k.SetValue("DisplayIcon", exe);
                k.SetValue("InstallLocation", folder);
                k.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /uninstall /S");
                k.SetValue("EstimatedSize", (int)((size + new FileInfo(uninstaller).Length) / 1024), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (autostart) k.SetValue(AppInfo.Name, "\"" + exe + "\"");
                else if (k.GetValue(AppInfo.Name) != null) k.DeleteValue(AppInfo.Name);
                if (k.GetValue("Projecteur") != null) k.DeleteValue("Projecteur"); // ancienne version
            }

            if (launch)
            {
                say("Lancement de " + AppInfo.Name + "…");
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = folder, UseShellExecute = true });
            }
            say(AppInfo.Name + " est installé.");
        }

        public static bool Uninstall(bool silent)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(Application.ExecutablePath));
            if (!silent && MessageBox.Show("Désinstaller " + AppInfo.Name + " ?\n\nVos réglages (%APPDATA%\\" + AppInfo.Name + ") seront conservés.",
                    AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return false;

            CloseRunningInstances();
            foreach (var lnk in new[] { StartMenuShortcut, DesktopShortcut })
                if (File.Exists(lnk)) File.Delete(lnk);
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                if (k != null && k.GetValue(AppInfo.Name) != null) k.DeleteValue(AppInfo.Name);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);

            string exe = Path.Combine(folder, AppInfo.ExeName);
            Retry(() => { if (File.Exists(exe)) File.Delete(exe); });

            // Le désinstallateur ne peut pas se supprimer lui-même : cmd s'en charge juste après sa fermeture.
            // Seuls nos fichiers sont supprimés, et le dossier uniquement s'il est vide.
            // Le chemin passe par une variable d'environnement : cmd ne réinterprète donc pas son contenu
            // (un dossier contenant « % » ou « & » ne peut pas injecter de commande).
            var psi = new ProcessStartInfo("cmd.exe",
                "/d /c ping -n 3 127.0.0.1 >nul & del /f /q \"%TOMEE_DIR%\\" + UninstallerName + "\" & rmdir \"%TOMEE_DIR%\"")
            {
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
            };
            psi.EnvironmentVariables["TOMEE_DIR"] = folder;
            Process.Start(psi);

            if (!silent) MessageBox.Show(AppInfo.Name + " a été désinstallé.", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        /// <summary>Ferme proprement Tomee (évènement nommé), puis de force si besoin. Ferme aussi l'ancienne version « Projecteur ».</summary>
        static void CloseRunningInstances()
        {
            EventWaitHandle exit;
            if (EventWaitHandle.TryOpenExisting(AppInfo.ExitEvent, out exit))
                using (exit) exit.Set();

            // Seuls les processus identifiés comme Tomee (ou l'ancienne version « Projecteur ») sont fermés,
            // jamais un autre programme qui porterait le même nom.
            var deadline = DateTime.Now.AddSeconds(5);
            foreach (var name in new[] { AppInfo.Name, "Projecteur" })
                foreach (var p in Process.GetProcessesByName(name))
                    using (p)
                    {
                        if (!AppInfo.IsTomeeProcess(p, name)) continue;
                        try
                        {
                            if (!p.WaitForExit((int)Math.Max(0, (deadline - DateTime.Now).TotalMilliseconds)))
                            {
                                p.Kill();
                                p.WaitForExit(2000);
                            }
                        }
                        catch (InvalidOperationException) { }
                        catch (System.ComponentModel.Win32Exception) { }
                    }
        }

        static void Retry(Action action)
        {
            for (int i = 0; ; i++)
            {
                try { action(); return; }
                catch (IOException) { if (i >= 10) throw; }
                catch (UnauthorizedAccessException) { if (i >= 10) throw; }
                Thread.Sleep(300);
            }
        }

        static void CreateShortcut(string path, string target, string workingDir)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            try
            {
                dynamic lnk = shell.CreateShortcut(path);
                lnk.TargetPath = target;
                lnk.WorkingDirectory = workingDir;
                lnk.IconLocation = target + ",0";
                lnk.Description = "Mise en valeur du curseur pour réunions, présentations et formations";
                lnk.Save();
                Marshal.FinalReleaseComObject(lnk);
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }
    }

    /// <summary>Assistant d'installation (une seule page).</summary>
    internal sealed class SetupForm : Form
    {
        readonly TextBox folder;
        readonly CheckBox desktop, autostart, launch;
        readonly System.Windows.Forms.Button install, cancel, browse;
        readonly Label status;
        bool done;

        public SetupForm()
        {
            float k;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
            bool upgrade = Installer.InstalledFolder != null;

            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9F);
            Text = "Installation de " + AppInfo.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (ArgumentException) { }

            var header = new Panel { Location = new Point(0, 0), Size = new Size(520, 92), BackColor = Color.FromArgb(31, 41, 55) };
            var logo = new PictureBox { Location = new Point(22, 22), Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom };
            try { logo.Image = new Icon(Icon, 48, 48).ToBitmap(); } catch (Exception) { }
            header.Controls.Add(logo);
            header.Controls.Add(new Label
            {
                Text = AppInfo.Name + " " + AppInfo.Version, AutoSize = true, Location = new Point(84, 20),
                Font = new Font("Segoe UI Semibold", 16F), ForeColor = Color.White,
            });
            header.Controls.Add(new Label
            {
                Text = "Mettez en valeur votre curseur en réunion, présentation ou formation.", AutoSize = true,
                Location = new Point(86, 56), ForeColor = Color.FromArgb(255, 210, 90),
            });
            Controls.Add(header);

            Controls.Add(new Label { Text = "Dossier d'installation", AutoSize = true, Location = new Point(22, 110) });
            folder = new TextBox { Location = new Point(24, 132), Size = new Size(380, 24), Text = Installer.DefaultFolder };
            browse = new System.Windows.Forms.Button { Text = "Parcourir…", Location = new Point(412, 130), Size = new Size(86, 27) };
            browse.Click += delegate { Browse(); };
            Controls.Add(folder);
            Controls.Add(browse);

            desktop = new CheckBox { Text = "Créer un raccourci sur le Bureau", AutoSize = true, Location = new Point(24, 172), Checked = true };
            autostart = new CheckBox { Text = "Lancer " + AppInfo.Name + " au démarrage de Windows", AutoSize = true, Location = new Point(24, 198), Checked = Installer.AutostartEnabled };
            launch = new CheckBox { Text = "Lancer " + AppInfo.Name + " après l'installation", AutoSize = true, Location = new Point(24, 224), Checked = true };
            Controls.Add(desktop);
            Controls.Add(autostart);
            Controls.Add(launch);

            status = new Label
            {
                AutoSize = false, Location = new Point(22, 258), Size = new Size(476, 36), ForeColor = Color.DimGray,
                Text = upgrade ? "Une version est déjà installée : elle sera mise à jour (vos réglages sont conservés)."
                               : "Installation pour votre compte uniquement, sans droits administrateur.",
            };
            Controls.Add(status);

            var footer = new Panel { Location = new Point(0, 300), Size = new Size(520, 54), BackColor = Color.FromArgb(243, 244, 246) };
            install = new System.Windows.Forms.Button { Text = upgrade ? "Mettre à jour" : "Installer", Location = new Point(316, 13), Size = new Size(96, 28) };
            cancel = new System.Windows.Forms.Button { Text = "Annuler", Location = new Point(420, 13), Size = new Size(86, 28) };
            install.Click += delegate { if (done) Close(); else DoInstall(); };
            cancel.Click += delegate { Close(); };
            footer.Controls.Add(install);
            footer.Controls.Add(cancel);
            Controls.Add(footer);
            AcceptButton = install;
            CancelButton = cancel;
            ActiveControl = install;
            ClientSize = new Size(520, 354);

            if (k > 1.01f)
            {
                foreach (Control c in Controls) c.Scale(new SizeF(k, k));
                ClientSize = new Size((int)(520 * k), (int)(354 * k));
            }
            ResumeLayout(false);
        }

        void Browse()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Choisissez le dossier où installer " + AppInfo.Name, SelectedPath = folder.Text })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var path = dlg.SelectedPath;
                if (!path.TrimEnd('\\').EndsWith(AppInfo.Name, StringComparison.OrdinalIgnoreCase)) path = Path.Combine(path, AppInfo.Name);
                folder.Text = path;
            }
        }

        void DoInstall()
        {
            foreach (Control c in new Control[] { folder, browse, desktop, autostart, launch, install, cancel }) c.Enabled = false;
            UseWaitCursor = true;
            try
            {
                Installer.Install(folder.Text.Trim(), desktop.Checked, autostart.Checked, launch.Checked, msg =>
                {
                    status.Text = msg;
                    status.Refresh();
                });
                done = true;
                status.ForeColor = Color.ForestGreen;
                status.Text = AppInfo.Name + " est installé. Retrouvez-le dans le menu Démarrer et la zone de notification.";
                install.Text = "Terminer";
                install.Enabled = true;
                install.Focus();
            }
            catch (Exception ex)
            {
                status.ForeColor = Color.Firebrick;
                status.Text = "Échec : " + ex.Message;
                foreach (Control c in new Control[] { folder, browse, desktop, autostart, launch, install, cancel }) c.Enabled = true;
            }
            finally { UseWaitCursor = false; }
        }
    }
}
