using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace GoldRushInstaller
{
    public partial class MainWindow : Window
    {
        private GithubRelease? _latestRelease;
        private CancellationTokenSource? _cts;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Log("Gold Rush Mod Menu Installer v1.0");
            Log("Detecting game folder…");

            var found = InstallerCore.DetectGamePath();
            if (found != null)
            {
                GamePathBox.Text = found;
                Log($"Found game at: {found}");
                RefreshInstalledVersion();
            }
            else
            {
                Log("Game folder not found automatically. Please use Browse to locate it.");
                GamePathStatus.Text = "⚠ Game folder not detected — please browse manually.";
            }

            await CheckForUpdates();
        }

        // ── Game path ──────────────────────────────────────────────────────────
        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select your Gold Rush: The Game folder (contains GoldMiningSimulator.exe)",
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var path = dialog.SelectedPath;
                if (!InstallerCore.IsValidGameFolder(path))
                {
                    Log($"WARNING: GoldMiningSimulator.exe not found in {path}");
                    GamePathStatus.Text = "⚠ GoldMiningSimulator.exe not found — are you sure this is the right folder?";
                }
                else
                {
                    GamePathStatus.Text = "✓ Valid game folder";
                    Log($"Game folder set to: {path}");
                }
                GamePathBox.Text = path;
                RefreshInstalledVersion();
            }
        }

        private void RefreshInstalledVersion()
        {
            var path = GamePathBox.Text.Trim();
            if (string.IsNullOrEmpty(path)) return;
            var v = InstallerCore.GetInstalledVersion(path);
            InstalledVersionText.Text = v ?? "Not installed";
            GamePathStatus.Text = InstallerCore.IsValidGameFolder(path)
                ? (v != null ? $"✓ Mod installed ({v})" : "✓ Game found — mod not yet installed")
                : "⚠ GoldMiningSimulator.exe not found here";
        }

        // ── Version check ──────────────────────────────────────────────────────
        private async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await CheckForUpdates();

        private async Task CheckForUpdates()
        {
            CheckUpdateButton.IsEnabled = false;
            LatestVersionText.Text = "Checking…";
            Log("Checking GitHub for latest release…");
            try
            {
                _latestRelease = await InstallerCore.GetLatestRelease();
                LatestVersionText.Text = _latestRelease?.TagName ?? "Unknown";
                Log($"Latest release: {_latestRelease?.TagName}  →  {_latestRelease?.HtmlUrl}");

                var installed = InstallerCore.GetInstalledVersion(GamePathBox.Text.Trim());
                if (installed == null)
                    Log("Mod is not installed. Click Install / Update to install it.");
                else if (installed == "legacy")
                    Log("Legacy install detected (no version info). Click Install / Update to refresh.");
                else if (installed == _latestRelease?.TagName.TrimStart('v'))
                    Log($"You are up to date ({installed}).");
                else
                    Log($"Update available: {installed} → {_latestRelease?.TagName}. Click Install / Update!");
            }
            catch (Exception ex)
            {
                LatestVersionText.Text = "Error";
                Log($"ERROR checking for updates: {ex.Message}");
                Log("Check your internet connection or visit: https://github.com/kpteam/gold-rush-mod-menu/releases");
            }
            CheckUpdateButton.IsEnabled = true;
        }

        // ── Install ────────────────────────────────────────────────────────────
        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            var gamePath = GamePathBox.Text.Trim();
            if (!InstallerCore.IsValidGameFolder(gamePath))
            {
                Log("ERROR: Please select a valid game folder first.");
                MessageBox.Show("Please select your Gold Rush: The Game folder first.\n\nIt should contain GoldMiningSimulator.exe.",
                                "Invalid Game Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_latestRelease == null)
            {
                Log("No release info — checking GitHub first…");
                await CheckForUpdates();
                if (_latestRelease == null) return;
            }

            SetBusy(true);
            Log($"Starting install of {_latestRelease.TagName}…");
            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.Value = 0;
            _cts = new CancellationTokenSource();

            var progress = new Progress<(int Percent, string Message)>(p =>
            {
                ProgressBar.Value = p.Percent;
                Log(p.Message);
            });

            try
            {
                await InstallerCore.Install(gamePath, _latestRelease, progress, _cts.Token);
                RefreshInstalledVersion();
                MessageBox.Show($"Installed {_latestRelease.TagName} successfully!\n\nLaunch the game through Steam and press F10 to open the mod menu.",
                                "Install Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                Log("Install cancelled.");
            }
            catch (Exception ex)
            {
                Log($"ERROR during install: {ex.Message}");
                MessageBox.Show($"Install failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        // ── Uninstall ──────────────────────────────────────────────────────────
        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            var gamePath = GamePathBox.Text.Trim();
            if (!InstallerCore.IsValidGameFolder(gamePath))
            {
                Log("ERROR: Please select a valid game folder first.");
                return;
            }

            var confirm = MessageBox.Show(
                "This will remove the mod, BepInEx hook (winhttp.dll), and your config file.\n\nThe game will return to vanilla.\n\nContinue?",
                "Confirm Uninstall", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            SetBusy(true);
            var progress = new Progress<(int, string)>(p => { ProgressBar.Value = p.Item1; Log(p.Item2); });
            ProgressBar.Visibility = Visibility.Visible;
            try
            {
                InstallerCore.Uninstall(gamePath, progress);
                RefreshInstalledVersion();
                MessageBox.Show("Mod uninstalled successfully. Game is back to vanilla.",
                                "Uninstall Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"ERROR during uninstall: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        // ── Open folder ────────────────────────────────────────────────────────
        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var path = GamePathBox.Text.Trim();
            if (System.IO.Directory.Exists(path))
                Process.Start("explorer.exe", path);
            else
                Log("Game folder path is not set or does not exist.");
        }

        // ── Repo link ──────────────────────────────────────────────────────────
        private void RepoLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo("https://github.com/kpteam/gold-rush-mod-menu") { UseShellExecute = true }); }
            catch { /* ignore */ }
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private void SetBusy(bool busy)
        {
            InstallButton.IsEnabled   = !busy;
            UninstallButton.IsEnabled = !busy;
            BrowseButton.IsEnabled    = !busy;
            CheckUpdateButton.IsEnabled = !busy;
        }

        private void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            LogText.Text += line;
            LogScroller.ScrollToEnd();
        }
    }
}