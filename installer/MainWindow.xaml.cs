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
            Log("Gold Rush Mod Menu Installer v" + InstallerCore.InstallerVersion);
            InstallerVersionText.Text = InstallerCore.InstallerVersion;
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
            await CheckInstallerVersion();
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

        // ── Installer self-update ─────────────────────────────────────────────
        private async Task CheckInstallerVersion()
        {
            try
            {
                Log("Checking for installer update…");
                var remote = await InstallerCore.GetInstallerRemoteVersion();
                if (remote == null)
                {
                    LatestInstallerVersionText.Text = "N/A";
                    Log("Installer version info not available on repo yet.");
                    return;
                }
                LatestInstallerVersionText.Text = remote;
                if (InstallerCore.IsNewer(InstallerCore.InstallerVersion, remote))
                {
                    UpdateInstallerButton.IsEnabled = true;
                    LatestInstallerVersionText.Foreground = System.Windows.Media.Brushes.Orange;
                    Log($"Installer update available: {InstallerCore.InstallerVersion} → {remote}  Click 'Update' to self-update.");
                }
                else
                {
                    LatestInstallerVersionText.Foreground = System.Windows.Media.Brushes.LightGreen;
                    Log($"Installer is up to date ({InstallerCore.InstallerVersion}).");
                }
            }
            catch (Exception ex)
            {
                LatestInstallerVersionText.Text = "Error";
                Log($"Could not check installer version: {ex.Message}");
            }
        }

        private async void UpdateInstaller_Click(object sender, RoutedEventArgs e)
        {
            SetBusy(true);
            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.Value = 0;
            _cts = new CancellationTokenSource();

            GithubRelease? installerRelease = null;
            try
            {
                Log("Fetching installer release info…");
                installerRelease = await InstallerCore.GetLatestInstallerRelease();
                if (installerRelease == null)
                {
                    Log("No installer release found on GitHub.");
                    MessageBox.Show("Could not find an installer release on GitHub.\nPlease download manually from:\nhttps://github.com/kpteam/gold-rush-mod-menu/releases",
                                    "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log($"ERROR fetching installer release: {ex.Message}");
                SetBusy(false);
                ProgressBar.Visibility = Visibility.Collapsed;
                return;
            }

            var confirm = MessageBox.Show(
                "The installer will download an updated version of itself, close, and restart automatically.\n\nContinue?",
                "Update Installer", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                SetBusy(false);
                ProgressBar.Visibility = Visibility.Collapsed;
                return;
            }

            var progress = new Progress<(int Percent, string Message)>(p =>
            {
                ProgressBar.Value = p.Percent;
                Log(p.Message);
            });
            try
            {
                await InstallerCore.SelfUpdate(installerRelease, progress, _cts.Token);
                // Give the cmd script a moment to spawn, then exit
                await Task.Delay(800);
                System.Windows.Application.Current.Shutdown();
            }
            catch (OperationCanceledException)
            {
                Log("Installer update cancelled.");
            }
            catch (Exception ex)
            {
                Log($"ERROR updating installer: {ex.Message}");
                MessageBox.Show($"Self-update failed:\n{ex.Message}\n\nPlease download the latest installer manually from:\nhttps://github.com/kpteam/gold-rush-mod-menu/releases",
                                "Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
                ProgressBar.Visibility = Visibility.Collapsed;
            }
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

        // ── Remove mod only ────────────────────────────────────────────────────
        private void RemoveMod_Click(object sender, RoutedEventArgs e)
        {
            var gamePath = GamePathBox.Text.Trim();
            if (!InstallerCore.IsValidGameFolder(gamePath))
            {
                Log("ERROR: Please select a valid game folder first.");
                return;
            }

            var v = InstallerCore.GetInstalledVersion(gamePath);
            if (v == null)
            {
                Log("Mod is not installed — nothing to remove.");
                MessageBox.Show("The mod does not appear to be installed in this folder.",
                                "Not Installed", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                "This will remove ONLY the mod DLL, version file, and mod config file.\n\n" +
                "BepInEx, winhttp.dll, and all other files will be left completely intact.\n\n" +
                "You can reinstall the mod at any time using the Install / Update button.\n\nContinue?",
                "Remove Mod Only", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            SetBusy(true);
            ProgressBar.Visibility = Visibility.Visible;
            var progress = new Progress<(int, string)>(p => { ProgressBar.Value = p.Item1; Log(p.Item2); });
            try
            {
                InstallerCore.RemoveModOnly(gamePath, progress);
                RefreshInstalledVersion();
                MessageBox.Show(
                    "Mod removed successfully.\n\nBepInEx and all other files are untouched.\n" +
                    "Click 'Install / Update Mod' to reinstall at any time.",
                    "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"ERROR removing mod: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        // ── Full Uninstall ─────────────────────────────────────────────────────
        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            var gamePath = GamePathBox.Text.Trim();
            if (!InstallerCore.IsValidGameFolder(gamePath))
            {
                Log("ERROR: Please select a valid game folder first.");
                return;
            }

            var confirm = MessageBox.Show(
                "FULL UNINSTALL will remove the mod AND the BepInEx loader hook (winhttp.dll + doorstop_config.ini).\n\n" +
                "The game will be returned to a completely vanilla state.\n\n" +
                "Use 'Remove Mod Only' if you just want to disable the mod while keeping BepInEx.\n\nContinue?",
                "Confirm Full Uninstall", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            SetBusy(true);
            var progress = new Progress<(int, string)>(p => { ProgressBar.Value = p.Item1; Log(p.Item2); });
            ProgressBar.Visibility = Visibility.Visible;
            try
            {
                InstallerCore.Uninstall(gamePath, progress);
                RefreshInstalledVersion();
                MessageBox.Show("Full uninstall complete. Game is back to completely vanilla.",
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
            InstallButton.IsEnabled         = !busy;
            RemoveModButton.IsEnabled       = !busy;
            UninstallButton.IsEnabled       = !busy;
            UpdateInstallerButton.IsEnabled = !busy && (LatestInstallerVersionText.Text != "---" && LatestInstallerVersionText.Text != "Checking..." && LatestInstallerVersionText.Text != "N/A" && LatestInstallerVersionText.Text != "Error"
                && InstallerCore.IsNewer(InstallerCore.InstallerVersion, LatestInstallerVersionText.Text));
            BrowseButton.IsEnabled          = !busy;
            CheckUpdateButton.IsEnabled     = !busy;
        }

        private void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            LogText.Text += line;
            LogScroller.ScrollToEnd();
        }
    }
}