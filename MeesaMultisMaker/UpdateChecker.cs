using System;
using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Checks for application updates from GitHub releases
    /// </summary>
    public static class UpdateChecker
    {
        private const string GITHUB_OWNER = "MeesaJarJar";
        private const string GITHUB_REPO = "MeesaMultisMaker";
        private const string GITHUB_API_URL = "https://api.github.com/repos/{0}/{1}/releases/latest";
        private const string GITHUB_RELEASES_URL = "https://github.com/{0}/{1}/releases/latest";

        private static readonly HttpClient _httpClient = new HttpClient();

        static UpdateChecker()
        {
            // GitHub API requires a User-Agent header
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MeesaMultisMaker-UpdateChecker");
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        /// <summary>
        /// Gets the current application version
        /// </summary>
        public static Version CurrentVersion
        {
            get
            {
                try
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    var version = assembly.GetName().Version;
                    return version;
                }
                catch
                {
                    return new Version(1, 0, 0, 0);
                }
            }
        }

        /// <summary>
        /// Gets the current version as a display string
        /// </summary>
        public static string CurrentVersionString => $"v{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

        /// <summary>
        /// Check for updates asynchronously
        /// </summary>
        /// <returns>UpdateInfo if an update is available, null otherwise</returns>
        public static async Task<UpdateInfo> CheckForUpdatesAsync()
        {
            try
            {
                string apiUrl = string.Format(GITHUB_API_URL, GITHUB_OWNER, GITHUB_REPO);
                
                var response = await _httpClient.GetStringAsync(apiUrl);
                
                // Parse the JSON response manually (avoiding dependency on JSON library)
                var tagName = ExtractJsonValue(response, "tag_name");
                var releaseName = ExtractJsonValue(response, "name");
                var releaseBody = ExtractJsonValue(response, "body");
                var htmlUrl = ExtractJsonValue(response, "html_url");
                var publishedAt = ExtractJsonValue(response, "published_at");

                if (string.IsNullOrEmpty(tagName))
                    return null;

                // Parse version from tag (e.g., "v1.2.3" or "1.2.3")
                var latestVersion = ParseVersion(tagName);
                if (latestVersion == null)
                    return null;

                // Compare versions
                bool updateAvailable = latestVersion > CurrentVersion;

                return new UpdateInfo
                {
                    LatestVersion = latestVersion,
                    TagName = tagName,
                    ReleaseName = releaseName ?? tagName,
                    ReleaseNotes = UnescapeJson(releaseBody ?? ""),
                    ReleaseUrl = htmlUrl ?? string.Format(GITHUB_RELEASES_URL, GITHUB_OWNER, GITHUB_REPO),
                    PublishedAt = DateTime.TryParse(publishedAt, out var dt) ? dt : DateTime.MinValue,
                    UpdateAvailable = updateAvailable
                };
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"Update check failed (network error): {ex.Message}");
                return null;
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("Update check timed out");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update check failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Check for updates and show a dialog if an update is available
        /// </summary>
        /// <param name="owner">Parent form for the dialog</param>
        /// <param name="silent">If true, only show dialog when update is available</param>
        public static async Task CheckAndPromptAsync(IWin32Window owner, bool silent = true)
        {
            var updateInfo = await CheckForUpdatesAsync();

            if (updateInfo == null)
            {
                if (!silent)
                {
                    MessageBox.Show(owner,
                        "Unable to check for updates.\n\nPlease check your internet connection or try again later.",
                        "Update Check Failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                return;
            }

            if (updateInfo.UpdateAvailable)
            {
                ShowUpdateDialog(owner, updateInfo);
            }
            else if (!silent)
            {
                MessageBox.Show(owner,
                    $"You are running the latest version!\n\nCurrent version: {CurrentVersionString}",
                    "No Updates Available",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        /// <summary>
        /// Show the update available dialog
        /// </summary>
        private static void ShowUpdateDialog(IWin32Window owner, UpdateInfo info)
        {
            using (var dialog = new Form())
            {
                dialog.Text = "Update Available";
                dialog.Width = 500;
                dialog.Height = 350;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;

                HolographicTheme.ApplyToForm(dialog);

                int y = 15;

                // Title
                var titleLabel = new Label
                {
                    Text = "A new version is available!",
                    Location = new Point(15, y),
                    Size = new Size(460, 25),
                    Font = new Font("Segoe UI", 12, FontStyle.Bold)
                };
                HolographicTheme.ApplyToLabel(titleLabel);
                titleLabel.ForeColor = HolographicTheme.CyanAccent;
                dialog.Controls.Add(titleLabel);
                y += 35;

                // Version info
                var versionLabel = new Label
                {
                    Text = $"Current version: {CurrentVersionString}\nLatest version: v{info.LatestVersion.Major}.{info.LatestVersion.Minor}.{info.LatestVersion.Build}",
                    Location = new Point(15, y),
                    Size = new Size(460, 40)
                };
                HolographicTheme.ApplyToLabel(versionLabel);
                dialog.Controls.Add(versionLabel);
                y += 50;

                // Release notes label
                var notesLabel = new Label
                {
                    Text = "Release Notes:",
                    Location = new Point(15, y),
                    Size = new Size(460, 20),
                    Font = new Font("Segoe UI", 9, FontStyle.Bold)
                };
                HolographicTheme.ApplyToLabel(notesLabel);
                dialog.Controls.Add(notesLabel);
                y += 25;

                // Release notes textbox
                var notesTextBox = new TextBox
                {
                    Text = string.IsNullOrEmpty(info.ReleaseNotes) ? "No release notes available." : info.ReleaseNotes,
                    Location = new Point(15, y),
                    Size = new Size(455, 120),
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical
                };
                HolographicTheme.ApplyToTextBox(notesTextBox);
                dialog.Controls.Add(notesTextBox);
                y += 130;

                // Buttons panel
                var buttonPanel = new Panel
                {
                    Location = new Point(0, y),
                    Size = new Size(500, 50),
                    Dock = DockStyle.Bottom
                };
                HolographicTheme.ApplyToPanel(buttonPanel, true);

                var downloadButton = new Button
                {
                    Text = "Download Update",
                    Size = new Size(130, 30),
                    Location = new Point(15, 10)
                };
                HolographicTheme.ApplyToButton(downloadButton, ButtonStyle.Success);
                downloadButton.Click += (s, e) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = info.ReleaseUrl,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to open browser: {ex.Message}", "Error", 
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    dialog.Close();
                };
                buttonPanel.Controls.Add(downloadButton);

                var remindButton = new Button
                {
                    Text = "Remind Me Later",
                    Size = new Size(120, 30),
                    Location = new Point(250, 10)
                };
                HolographicTheme.ApplyToButton(remindButton);
                remindButton.Click += (s, e) => dialog.Close();
                buttonPanel.Controls.Add(remindButton);

                var skipButton = new Button
                {
                    Text = "Skip Version",
                    Size = new Size(100, 30),
                    Location = new Point(380, 10)
                };
                HolographicTheme.ApplyToButton(skipButton, ButtonStyle.Warning);
                skipButton.Click += (s, e) =>
                {
                    // Save skipped version to config
                    AppConfig.Instance.SkippedVersion = info.TagName;
                    AppConfig.Instance.Save();
                    dialog.Close();
                };
                buttonPanel.Controls.Add(skipButton);

                dialog.Controls.Add(buttonPanel);

                dialog.ShowDialog(owner);
            }
        }

        /// <summary>
        /// Extract a value from JSON string (simple parser without dependencies)
        /// </summary>
        private static string ExtractJsonValue(string json, string key)
        {
            try
            {
                // Match "key": "value" (value may contain escaped quotes \" and escapes \\)
                var pattern = $"\"{key}\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"";
                var match = Regex.Match(json, pattern);
                if (match.Success)
                    return match.Groups[1].Value;

                // Try without quotes for non-string values
                pattern = $"\"{key}\"\\s*:\\s*([^,}}\\s]+)";
                match = Regex.Match(json, pattern);
                if (match.Success)
                    return match.Groups[1].Value.Trim('"');

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Unescape JSON string (handle \n, \r, \t, etc.)
        /// </summary>
        private static string UnescapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            return value
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }

        /// <summary>
        /// Parse a version string (e.g., "v1.2.3" or "1.2.3")
        /// </summary>
        private static Version ParseVersion(string versionString)
        {
            try
            {
                // Remove leading 'v' if present
                var cleaned = versionString.TrimStart('v', 'V').Trim();
                
                // Try to parse as Version
                if (Version.TryParse(cleaned, out var version))
                    return version;

                // Try to parse with fewer parts
                var parts = cleaned.Split('.');
                if (parts.Length >= 2)
                {
                    int major = int.Parse(parts[0]);
                    int minor = int.Parse(parts[1]);
                    int build = parts.Length > 2 ? int.Parse(parts[2]) : 0;
                    int revision = parts.Length > 3 ? int.Parse(parts[3]) : 0;
                    return new Version(major, minor, build, revision);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Contains information about an available update
    /// </summary>
    public class UpdateInfo
    {
        public Version LatestVersion { get; set; }
        public string TagName { get; set; }
        public string ReleaseName { get; set; }
        public string ReleaseNotes { get; set; }
        public string ReleaseUrl { get; set; }
        public DateTime PublishedAt { get; set; }
        public bool UpdateAvailable { get; set; }
    }
}
