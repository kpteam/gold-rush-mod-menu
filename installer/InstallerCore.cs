using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace GoldRushInstaller
{
    public record GithubRelease(string TagName, string HtmlUrl, List<GithubAsset> Assets);
    public record GithubAsset(string Name, string BrowserDownloadUrl, long Size);

    public class InstallerCore
    {
        private const string RepoOwner         = "kpteam";
        private const string RepoName          = "gold-rush-mod-menu";
        private const string ApiBase           = "https://api.github.com";
        private const string VersionFile       = @"BepInEx\plugins\GoldRushModMenu.version";
        private const string PluginDll         = @"BepInEx\plugins\GoldRushModMenu.dll";
        private const string PluginConfig      = @"BepInEx\config\com.goldrushmod.modmenu.cfg";
        private const string GameExe           = "GoldMiningSimulator.exe";

        /// <summary>Current installer version. Bump this with each new installer release.</summary>
        public const string InstallerVersion   = "1.1.0";

        private static readonly HttpClient Http = new()
        {
            DefaultRequestHeaders =
            {
                UserAgent = { new ProductInfoHeaderValue("GoldRushInstaller", "1.0") },
                Accept    = { new MediaTypeWithQualityHeaderValue("application/vnd.github+json") }
            }
        };

        // ── Steam / game detection ────────────────────────────────────────────
        public static string? DetectGamePath()
        {
            // Try Steam registry keys
            var regKeys = new[]
            {
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
                @"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam"
            };

            var candidates = new List<string>();

            foreach (var key in regKeys)
            {
                var steamPath = Registry.GetValue(key, "SteamPath", null) as string;
                if (!string.IsNullOrEmpty(steamPath))
                {
                    candidates.Add(Path.Combine(steamPath, "steamapps", "common", "Gold Rush The Game"));
                    // Parse libraryfolders.vdf for extra library locations
                    var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(vdf))
                        foreach (var lib in ParseLibraryFolders(vdf))
                            candidates.Add(Path.Combine(lib, "steamapps", "common", "Gold Rush The Game"));
                }
            }

            // Common fallback paths
            candidates.AddRange(new[]
            {
                @"C:\Program Files (x86)\Steam\steamapps\common\Gold Rush The Game",
                @"C:\Program Files\Steam\steamapps\common\Gold Rush The Game",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                             @"Steam\steamapps\common\Gold Rush The Game")
            });

            foreach (var path in candidates)
                if (IsValidGameFolder(path))
                    return path;

            return null;
        }

        private static List<string> ParseLibraryFolders(string vdfPath)
        {
            var libs = new List<string>();
            try
            {
                foreach (var line in File.ReadAllLines(vdfPath))
                {
                    var t = line.Trim();
                    if (t.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = t.Split('"');
                        if (parts.Length >= 4)
                            libs.Add(parts[3].Replace(@"\\", @"\"));
                    }
                }
            }
            catch { /* ignore parse errors */ }
            return libs;
        }

        public static bool IsValidGameFolder(string path) =>
            !string.IsNullOrWhiteSpace(path) &&
            Directory.Exists(path) &&
            File.Exists(Path.Combine(path, GameExe));

        // ── Installed version ─────────────────────────────────────────────────
        public static string? GetInstalledVersion(string gamePath)
        {
            var vf = Path.Combine(gamePath, VersionFile);
            if (File.Exists(vf))
                return File.ReadAllText(vf).Trim();
            // Fallback: DLL exists but no version file = legacy install
            if (File.Exists(Path.Combine(gamePath, PluginDll)))
                return "legacy";
            return null;
        }

        // ── GitHub releases ───────────────────────────────────────────────────
        /// <summary>
        /// Returns the latest release that contains a ZIP asset (the mod release).
        /// Walks all releases so installer-only releases are skipped automatically.
        /// </summary>
        public static async Task<GithubRelease?> GetLatestRelease()
        {
            // List up to 10 releases and find the first one with a ZIP asset
            var url  = $"{ApiBase}/repos/{RepoOwner}/{RepoName}/releases?per_page=10";
            var json = await Http.GetStringAsync(url);
            using var doc  = JsonDocument.Parse(json);

            foreach (var rel in doc.RootElement.EnumerateArray())
            {
                var release = ParseRelease(rel);
                if (release.Assets.Exists(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                    return release;
            }

            // Fallback: return the very latest even if no ZIP (shows a cleaner error later)
            var fallback = $"{ApiBase}/repos/{RepoOwner}/{RepoName}/releases/latest";
            var fb       = await Http.GetStringAsync(fallback);
            using var fbDoc = JsonDocument.Parse(fb);
            return ParseRelease(fbDoc.RootElement);
        }

        /// <summary>
        /// Returns the latest release that contains an installer EXE asset.
        /// Used by self-update to avoid picking up mod releases.
        /// </summary>
        public static async Task<GithubRelease?> GetLatestInstallerRelease()
        {
            var url  = $"{ApiBase}/repos/{RepoOwner}/{RepoName}/releases?per_page=10";
            var json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);

            foreach (var rel in doc.RootElement.EnumerateArray())
            {
                var release = ParseRelease(rel);
                if (release.Assets.Exists(a =>
                        a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        a.Name.Contains("Installer", StringComparison.OrdinalIgnoreCase)))
                    return release;
            }
            return null;
        }

        private static GithubRelease ParseRelease(JsonElement root)
        {
            var tag    = root.GetProperty("tag_name").GetString() ?? "";
            var html   = root.GetProperty("html_url").GetString()  ?? "";
            var assets = new List<GithubAsset>();
            if (root.TryGetProperty("assets", out var arr))
                foreach (var a in arr.EnumerateArray())
                {
                    var name  = a.GetProperty("name").GetString()                    ?? "";
                    var dlUrl = a.GetProperty("browser_download_url").GetString()    ?? "";
                    var size  = a.GetProperty("size").GetInt64();
                    assets.Add(new GithubAsset(name, dlUrl, size));
                }
            return new GithubRelease(tag, html, assets);
        }

        // ── Installer self-update ─────────────────────────────────────────────
        /// <summary>
        /// Fetches the raw installer/version.txt from the repo to see if a newer
        /// installer is available without touching the mod release channel.
        /// Returns null if the version file doesn't exist yet.
        /// </summary>
        public static async Task<string?> GetInstallerRemoteVersion()
        {
            const string url = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/master/installer/version.txt";
            try
            {
                var text = await Http.GetStringAsync(url);
                return text.Trim().TrimStart('v');
            }
            catch { return null; }
        }

        /// <summary>Returns true if <paramref name="remote"/> is newer than <paramref name="local"/>.</summary>
        public static bool IsNewer(string local, string remote)
        {
            if (Version.TryParse(local, out var l) && Version.TryParse(remote, out var r))
                return r > l;
            return string.Compare(remote, local, StringComparison.OrdinalIgnoreCase) > 0;
        }

        /// <summary>
        /// Downloads the latest installer exe from the GitHub release assets,
        /// writes it next to the current exe with a ".new" suffix, then launches
        /// a small cmd script that waits for the current process to exit, replaces
        /// the exe, and restarts it.
        /// </summary>
        public static async Task SelfUpdate(
            GithubRelease release,
            IProgress<(int Percent, string Message)> progress,
            CancellationToken ct)
        {
            var asset = release.Assets.Find(a =>
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("Installer", StringComparison.OrdinalIgnoreCase))
                ?? release.Assets.Find(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                ?? throw new Exception("No installer EXE asset found in the GitHub release.");

            progress.Report((5, $"Downloading installer update: {asset.Name}…"));

            var currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                             ?? System.AppContext.BaseDirectory + "GoldRushModMenuInstaller.exe";

            var newExe = currentExe + ".new";
            var oldExe = currentExe + ".old";

            using (var resp = await Http.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? asset.Size;
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                await using var file   = File.Create(newExe);
                var buf  = new byte[81920];
                long read = 0;
                int n;
                while ((n = await stream.ReadAsync(buf, ct)) > 0)
                {
                    await file.WriteAsync(buf.AsMemory(0, n), ct);
                    read += n;
                    int pct = total > 0 ? (int)(read * 85 / total) + 5 : 50;
                    progress.Report((pct, $"Downloading… {read / 1024:N0} / {total / 1024:N0} KB"));
                }
            }

            progress.Report((95, "Preparing update script…"));

            // Build a cmd script: wait for this PID to exit → move files → restart
            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            var script = $@"@echo off
:wait
tasklist /FI ""PID eq {pid}"" 2>NUL | find ""{pid}"" >NUL
if not errorlevel 1 (
    timeout /t 1 /nobreak >NUL
    goto :wait
)
move /Y ""{newExe}"" ""{currentExe}""
if exist ""{oldExe}"" del /F /Q ""{oldExe}""
start """" ""{currentExe}""
";
            var scriptPath = Path.Combine(Path.GetTempPath(), "grmod_update.cmd");
            File.WriteAllText(scriptPath, script);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
            {
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            });

            progress.Report((100, "Update downloaded. Restarting installer…"));
        }

        // ── Remove mod only (leaves BepInEx intact) ───────────────────────────
        /// <summary>
        /// Removes only the mod plugin DLL, version file, and mod config file.
        /// BepInEx core files, winhttp.dll, doorstop_config.ini, and all other
        /// mods/plugins are left completely untouched.
        /// </summary>
        public static void RemoveModOnly(string gamePath, IProgress<(int, string)> progress)
        {
            progress.Report((10, "Removing mod DLL…"));
            Delete(Path.Combine(gamePath, PluginDll));

            progress.Report((50, "Removing version tracking file…"));
            Delete(Path.Combine(gamePath, VersionFile));

            progress.Report((80, "Removing mod config…"));
            Delete(Path.Combine(gamePath, PluginConfig));

            progress.Report((100, "Mod removed. BepInEx and all other files are intact."));
        }

        // ── Install / update ──────────────────────────────────────────────────
        public static async Task Install(
            string gamePath,
            GithubRelease release,
            IProgress<(int Percent, string Message)> progress,
            CancellationToken ct)
        {
            // Find ZIP asset
            var asset = release.Assets.Find(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        ?? throw new Exception("No ZIP asset found in the release. Please check the GitHub repo.");

            progress.Report((5, $"Downloading {asset.Name} ({asset.Size / 1024:N0} KB)…"));

            // Download to temp file
            var tmp = Path.Combine(Path.GetTempPath(), asset.Name);
            using (var resp = await Http.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? asset.Size;
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                await using var file   = File.Create(tmp);
                var buf = new byte[81920];
                long read = 0;
                int  n;
                while ((n = await stream.ReadAsync(buf, ct)) > 0)
                {
                    await file.WriteAsync(buf.AsMemory(0, n), ct);
                    read += n;
                    int pct = total > 0 ? (int)(read * 70 / total) + 5 : 40;
                    progress.Report((pct, $"Downloading… {read / 1024:N0} / {total / 1024:N0} KB"));
                }
            }

            progress.Report((76, "Extracting…"));
            ct.ThrowIfCancellationRequested();

            using var zip = ZipFile.OpenRead(tmp);
            int total2 = zip.Entries.Count, done = 0;
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(entry.Name)) { done++; continue; } // directory entry

                var dest = Path.Combine(gamePath, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
                done++;
                int pct = 76 + done * 20 / Math.Max(total2, 1);
                progress.Report((pct, $"Installing {entry.Name}"));
            }

            // Write version file so we can detect updates next time
            var verDir = Path.Combine(gamePath, "BepInEx", "plugins");
            Directory.CreateDirectory(verDir);
            File.WriteAllText(Path.Combine(gamePath, VersionFile), release.TagName.TrimStart('v'));

            try { File.Delete(tmp); } catch { /* best-effort cleanup */ }

            progress.Report((100, $"Done! Installed {release.TagName}. Launch the game and press F10."));
        }

        // ── Full uninstall (removes BepInEx hook + mod) ───────────────────────
        public static void Uninstall(string gamePath, IProgress<(int, string)> progress)
        {
            progress.Report((10, "Removing mod DLL…"));
            Delete(Path.Combine(gamePath, PluginDll));
            Delete(Path.Combine(gamePath, VersionFile));
            Delete(Path.Combine(gamePath, PluginConfig));

            progress.Report((60, "Removing BepInEx hook (winhttp.dll / doorstop)…"));
            Delete(Path.Combine(gamePath, "winhttp.dll"));
            Delete(Path.Combine(gamePath, "doorstop_config.ini"));

            progress.Report((100, "Full uninstall complete. Game is back to vanilla."));
        }

        private static void Delete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* ignore locked files */ }
        }
    }
}