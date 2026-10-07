using MeesaMultisMaker.JarJar;
using MeesaMultisMaker.Helpers;
using MeesaMultisMaker.Mul;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    partial class MapViewerForm
    {
        // Track pending art-image changes (AI replacement)
        private readonly Dictionary<int, Bitmap> pendingArtChanges = new Dictionary<int, Bitmap>();
        // Track pending underlay area payloads for JarJar push
        private readonly Dictionary<string, UnderlayUploadItem> pendingUnderlays = new Dictionary<string, UnderlayUploadItem>();
        // Track pending overlay area payloads for JarJar push
        private readonly Dictionary<string, OverlayUploadItem> pendingOverlays = new Dictionary<string, OverlayUploadItem>();
        // Track pending overlay area removals for JarJar delete
        private readonly Dictionary<string, OverlayDeleteItem> pendingOverlayDeletes = new Dictionary<string, OverlayDeleteItem>();

        private class UnderlayUploadItem
        {
            public int Facet { get; set; }
            public Rectangle Bounds { get; set; }
            public Bitmap Image { get; set; }
            public string FilePath { get; set; }
        }

        private class OverlayUploadItem
        {
            public int Facet { get; set; }
            public Rectangle Bounds { get; set; }
            public Bitmap Image { get; set; }
            public string FilePath { get; set; }
        }

        private class OverlayDeleteItem
        {
            public int Facet { get; set; }
            public Rectangle Bounds { get; set; }
        }

        private static string BuildOverlayAreaKey(int facet, Rectangle bounds)
        {
            return $"{facet}:{bounds.Left}:{bounds.Top}:{bounds.Width}:{bounds.Height}";
        }

        // Save-section UI controls (created in Designer)
        private Button saveMapButton;
        private Button loadSaveButton;
        private Button deployButton;
        private Label saveStatusLabel;
        private ListBox savesListBox;
        private Button deleteSaveButton;
        private Button _pushToJarJarButton;
        private CheckBox _autoPushJarJarCheckBox;

        // ================================================================
        //  Art-change tracking  (existing functionality, kept intact)
        // ================================================================

        internal void MarkArtModified(ushort itemId, Image newImage)
        {
            if (newImage == null) return;
            Bitmap bmp = newImage as Bitmap ?? new Bitmap(newImage);
            pendingArtChanges[itemId] = bmp;
            UpdatePendingChangesLabel();
        }

        internal void MarkOverlayModified(int facet, Rectangle bounds, Bitmap image, string filePath)
        {
            if (image == null) return;

            string key = BuildOverlayAreaKey(facet, bounds);
            pendingOverlays[key] = new OverlayUploadItem
            {
                Facet = facet,
                Bounds = bounds,
                Image = new Bitmap(image),
                FilePath = filePath
            };

            // Upload wins over delete when both target same area.
            pendingOverlayDeletes.Remove(key);

            UpdatePendingChangesLabel();
        }

        internal void MarkOverlayRemoved(int facet, Rectangle bounds)
        {
            string key = BuildOverlayAreaKey(facet, bounds);

            // If an overlay was added/edited locally but then removed before push,
            // don't upload it.
            pendingOverlays.Remove(key);

            pendingOverlayDeletes[key] = new OverlayDeleteItem
            {
                Facet = facet,
                Bounds = bounds
            };

            UpdatePendingChangesLabel();
        }

        internal void ClearPendingChanges()
        {
            pendingArtChanges.Clear();
            UpdatePendingChangesLabel();
        }

        internal void MarkUnderlayModified(int facet, Rectangle bounds, Bitmap image, string filePath)
        {
            if (image == null) return;

            string key = $"{facet}:{bounds.Left}:{bounds.Top}:{bounds.Width}:{bounds.Height}";
            pendingUnderlays[key] = new UnderlayUploadItem
            {
                Facet = facet,
                Bounds = bounds,
                Image = new Bitmap(image),
                FilePath = filePath
            };

            UpdatePendingChangesLabel();
        }

        private void UpdatePendingChangesLabel()
        {
            if (pendingChangesLabel == null) return;

            if (pendingArtChanges.Count == 0)
            {
                if (pendingUnderlays.Count == 0 && pendingOverlays.Count == 0 && pendingOverlayDeletes.Count == 0)
                    pendingChangesLabel.Text = "No pending art/underlay/overlay changes";
                else
                    pendingChangesLabel.Text =
                        $"{pendingUnderlays.Count} underlay + {pendingOverlays.Count} overlay up + {pendingOverlayDeletes.Count} overlay del pending";
                pendingChangesLabel.ForeColor = HolographicTheme.TextSecondary;
                if (saveToMulButton != null)
                    saveToMulButton.Enabled = false;
                if (_pushToJarJarButton != null)
                    _pushToJarJarButton.Enabled = pendingUnderlays.Count > 0 || pendingOverlays.Count > 0 || pendingOverlayDeletes.Count > 0;
            }
            else
            {
                if (pendingUnderlays.Count > 0 || pendingOverlays.Count > 0 || pendingOverlayDeletes.Count > 0)
                    pendingChangesLabel.Text = $"{pendingArtChanges.Count} art + {pendingUnderlays.Count} underlay + {pendingOverlays.Count} overlay up + {pendingOverlayDeletes.Count} overlay del";
                else
                    pendingChangesLabel.Text = $"{pendingArtChanges.Count} art item(s) modified";
                pendingChangesLabel.ForeColor = HolographicTheme.ButtonWarning;
                if (saveToMulButton != null)
                    saveToMulButton.Enabled = true;
                if (_pushToJarJarButton != null)
                    _pushToJarJarButton.Enabled = true;
            }
        }

        private string GetOverlayMetadataPath(string folder, int mapIndex)
        {
            return Path.Combine(folder, $"overlays_map{mapIndex}.txt");
        }

        private void SaveOverlayMetadata(string folder, int mapIndex, bool copyPngIntoSaveFolder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return;

            try
            {
                string metadataPath = GetOverlayMetadataPath(folder, mapIndex);
                string overlayImageFolder = Path.Combine(folder, "Overlays");
                if (copyPngIntoSaveFolder && !Directory.Exists(overlayImageFolder))
                    Directory.CreateDirectory(overlayImageFolder);

                var lines = new List<string>();
                lines.Add("# facet|left|top|width|height|imageFile");

                foreach (var p in overlayPlanes)
                {
                    if (p == null || p.Facet != mapIndex)
                        continue;

                    string imageFile = string.Empty;
                    if (!string.IsNullOrWhiteSpace(p.FilePath))
                    {
                        imageFile = Path.GetFileName(p.FilePath);

                        if (copyPngIntoSaveFolder && File.Exists(p.FilePath))
                        {
                            string dest = Path.Combine(overlayImageFolder, imageFile);
                            File.Copy(p.FilePath, dest, true);
                        }
                    }

                    lines.Add(string.Join("|", new[]
                    {
                        p.Facet.ToString(),
                        p.Bounds.Left.ToString(),
                        p.Bounds.Top.ToString(),
                        p.Bounds.Width.ToString(),
                        p.Bounds.Height.ToString(),
                        imageFile
                    }));
                }

                File.WriteAllLines(metadataPath, lines.ToArray());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveOverlayMetadata failed: {ex.Message}");
            }
        }

        private void LoadOverlayMetadata(string folder, int mapIndex)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return;

            string metadataPath = GetOverlayMetadataPath(folder, mapIndex);
            if (!File.Exists(metadataPath))
                return;

            try
            {
                for (int i = overlayPlanes.Count - 1; i >= 0; i--)
                {
                    if (overlayPlanes[i] != null && overlayPlanes[i].Facet == mapIndex)
                    {
                        overlayPlanes[i].Image?.Dispose();
                        overlayPlanes.RemoveAt(i);
                    }
                }

                var lines = File.ReadAllLines(metadataPath);
                string overlayImageFolder = Path.Combine(folder, "Overlays");

                foreach (var raw in lines)
                {
                    if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("#"))
                        continue;

                    string[] parts = raw.Split('|');
                    if (parts.Length < 6)
                        continue;

                    if (!int.TryParse(parts[0], out int facet)) continue;
                    if (!int.TryParse(parts[1], out int left)) continue;
                    if (!int.TryParse(parts[2], out int top)) continue;
                    if (!int.TryParse(parts[3], out int width)) continue;
                    if (!int.TryParse(parts[4], out int height)) continue;

                    string imageFile = parts[5] ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(imageFile)) continue;

                    string pngPath = Path.Combine(overlayImageFolder, imageFile);
                    if (!File.Exists(pngPath))
                        continue;

                    Bitmap image;
                    using (var temp = new Bitmap(pngPath))
                        image = new Bitmap(temp);

                    overlayPlanes.Add(new OverlayPlane
                    {
                        Facet = facet,
                        Bounds = new Rectangle(left, top, width, height),
                        Image = image,
                        FilePath = pngPath,
                        IsFromServer = false
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadOverlayMetadata failed: {ex.Message}");
            }
        }

        private void SaveChangesToMul()
        {
            if (pendingArtChanges.Count == 0)
            {
                MessageBox.Show("No pending art changes to save.", "Save Art",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrEmpty(mulFolderPath) || !Directory.Exists(mulFolderPath))
            {
                MessageBox.Show("MUL folder path is not set or does not exist.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var result = MessageBox.Show(
                $"This will save {pendingArtChanges.Count} modified art item(s) to:\n\n" +
                $"{Path.Combine(mulFolderPath, "art.mul")}\n\n" +
                "A backup will be created before making changes.\n\nContinue?",
                "Save Art to MUL",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes) return;

            try
            {
                statusLabel.Text = "Creating art backup...";
                Application.DoEvents();

                if (!StaticArtWriter.BackupArtFiles(mulFolderPath))
                {
                    var br = MessageBox.Show(
                        "Failed to create backup of art files.\nContinue anyway? (Not recommended)",
                        "Backup Failed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (br != DialogResult.Yes) return;
                }

                statusLabel.Text = $"Saving {pendingArtChanges.Count} art items...";
                Application.DoEvents();

                int savedCount = StaticArtWriter.SaveMultipleStaticArts(mulFolderPath, pendingArtChanges);

                if (savedCount == pendingArtChanges.Count)
                {
                    MessageBox.Show(
                        $"Successfully saved {savedCount} art item(s) to art.mul!\nBackups created with .bak extension.",
                        "Save Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    ClearPendingChanges();
                    statusLabel.Text = $"Saved {savedCount} art items to art.mul";
                }
                else if (savedCount > 0)
                {
                    MessageBox.Show(
                        $"Saved {savedCount} of {pendingArtChanges.Count} art items.\nSome may have failed — check debug output.",
                        "Partial Save", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    statusLabel.Text = $"Saved {savedCount}/{pendingArtChanges.Count} art items";
                }
                else
                {
                    MessageBox.Show(
                        "Failed to save art items. Check that files are not read-only.",
                        "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    statusLabel.Text = "Art save failed!";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving art:\n\n{ex.Message}",
                    "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                statusLabel.Text = $"Art save error: {ex.Message}";
            }
        }

        private void DiscardPendingChanges()
        {
            if (pendingArtChanges.Count == 0 && pendingUnderlays.Count == 0 && pendingOverlays.Count == 0 && pendingOverlayDeletes.Count == 0) return;

            var result = MessageBox.Show(
                $"Discard {pendingArtChanges.Count} pending art change(s)" +
                (pendingUnderlays.Count > 0 || pendingOverlays.Count > 0 || pendingOverlayDeletes.Count > 0
                    ? $" and {pendingUnderlays.Count} underlay + {pendingOverlays.Count} overlay upload + {pendingOverlayDeletes.Count} overlay delete area(s)?\n"
                    : "?\n") +
                "The art cache will be reloaded from original files.",
                "Discard Art Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;

            foreach (var itemId in pendingArtChanges.Keys)
            {
                staticArtCache.Remove(itemId);
                modifiedStaticArtCache.Remove(itemId);
            }

            pendingUnderlays.Clear();
            pendingOverlays.Clear();
            pendingOverlayDeletes.Clear();

            ClearPendingChanges();
            GenerateMapImage();
            statusLabel.Text = "Art changes discarded.";
        }

        /// <summary>
        /// Push all pending art changes to the MeesaJarJar.com server.
        /// </summary>
        private async Task PushToJarJar()
        {
            if (pendingArtChanges.Count == 0 && pendingUnderlays.Count == 0 && pendingOverlays.Count == 0 && pendingOverlayDeletes.Count == 0)
            {
                MessageBox.Show("No pending art/underlay/overlay changes to push.", "Push to MeesaJarJar",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var config = AppConfig.Instance;
            if (!config.JarJarPushEnabled)
            {
                MessageBox.Show("Push to MeesaJarJar is disabled in settings.",
                    "Push Disabled", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Push {pendingArtChanges.Count} modified art item(s)" +
                (pendingUnderlays.Count > 0 ? $" and {pendingUnderlays.Count} underlay area(s)" : "") +
                (pendingOverlays.Count > 0 ? $" and {pendingOverlays.Count} overlay area(s)" : "") +
                (pendingOverlayDeletes.Count > 0 ? $" and {pendingOverlayDeletes.Count} overlay deletion(s)" : "") +
                " to MeesaJarJar.com?\n\n" +
                $"Server: {config.JarJarApiUrl}\n\n" +
                "Connected game clients will see changes within ~30 seconds.",
                "Push to MeesaJarJar",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            JarJarClient client = null;
            try
            {
                client = new JarJarClient(config.JarJarApiUrl, config.JarJarAuthToken);

                statusLabel.Text = "Connecting to MeesaJarJar...";
                Application.DoEvents();

                bool connected = await client.TestConnectionAsync();
                if (!connected)
                {
                    MessageBox.Show(
                        $"Cannot connect to {config.JarJarApiUrl}\n\n" +
                        "Check your internet connection and the server URL in Settings.",
                        "Connection Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    statusLabel.Text = "JarJar connection failed";
                    return;
                }

                statusLabel.Text = "Downloading lookup table...";
                Application.DoEvents();

                int totalToPush = pendingArtChanges.Count + pendingUnderlays.Count + pendingOverlays.Count + pendingOverlayDeletes.Count;
                int completedOverall = 0;

                var results = new List<UploadResult>();

                if (pendingArtChanges.Count > 0)
                {
                    // Filter to standard MUL format items (<= 0xFFFF) for JarJar upload
                    var standardItems = new Dictionary<ushort, Bitmap>();
                    foreach (var kvp in pendingArtChanges)
                    {
                        if (kvp.Key <= 0xFFFF)
                            standardItems[(ushort)kvp.Key] = kvp.Value;
                    }
                    
                    if (standardItems.Count > 0)
                    {
                        var spriteResults = await client.UploadMultipleSpritesAsync(
                            standardItems,
                            (completed, total) =>
                            {
                                int done = completedOverall + completed;
                                if (this.InvokeRequired)
                                    this.Invoke(new Action(() =>
                                    {
                                        statusLabel.Text = $"Pushing {done}/{totalToPush}...";
                                        Application.DoEvents();
                                    }));
                                else
                                {
                                    statusLabel.Text = $"Pushing {done}/{totalToPush}...";
                                    Application.DoEvents();
                                }
                            });
                        results.AddRange(spriteResults);
                        completedOverall += standardItems.Count;
                    }
                }

                if (pendingUnderlays.Count > 0)
                {
                    var underlayItems = pendingUnderlays.Values.Select(u => new UnderlayUploadRequest
                    {
                        Facet = u.Facet,
                        Left = u.Bounds.Left,
                        Top = u.Bounds.Top,
                        Width = u.Bounds.Width,
                        Height = u.Bounds.Height,
                        Image = u.Image,
                        SourcePath = u.FilePath
                    }).ToList();

                    var underlayResults = await client.UploadMultipleUnderlaysAsync(
                        underlayItems,
                        onProgress: (completed, total) =>
                        {
                            int done = completedOverall + completed;
                            if (this.InvokeRequired)
                                this.Invoke(new Action(() =>
                                {
                                    statusLabel.Text = $"Pushing {done}/{totalToPush}...";
                                    Application.DoEvents();
                                }));
                            else
                            {
                                statusLabel.Text = $"Pushing {done}/{totalToPush}...";
                                Application.DoEvents();
                            }
                        });
                    results.AddRange(underlayResults);
                    completedOverall += pendingUnderlays.Count;
                }

                if (pendingOverlays.Count > 0)
                {
                    var overlayItems = pendingOverlays.Values.Select(o => new OverlayUploadRequest
                    {
                        Facet = o.Facet,
                        Left = o.Bounds.Left,
                        Top = o.Bounds.Top,
                        Width = o.Bounds.Width,
                        Height = o.Bounds.Height,
                        Image = o.Image,
                        SourcePath = o.FilePath
                    }).ToList();

                    var overlayResults = await client.UploadMultipleOverlaysAsync(
                        overlayItems,
                        onProgress: (completed, total) =>
                        {
                            int done = completedOverall + completed;
                            if (this.InvokeRequired)
                                this.Invoke(new Action(() =>
                                {
                                    statusLabel.Text = $"Pushing {done}/{totalToPush}...";
                                    Application.DoEvents();
                                }));
                            else
                            {
                                statusLabel.Text = $"Pushing {done}/{totalToPush}...";
                                Application.DoEvents();
                            }
                        });
                    results.AddRange(overlayResults);
                    completedOverall += pendingOverlays.Count;
                }

                if (pendingOverlayDeletes.Count > 0)
                {
                    var deleteItems = pendingOverlayDeletes.Values.Select(o => new OverlayDeleteRequest
                    {
                        Facet = o.Facet,
                        Left = o.Bounds.Left,
                        Top = o.Bounds.Top,
                        Width = o.Bounds.Width,
                        Height = o.Bounds.Height
                    }).ToList();

                    var deleteResults = await client.DeleteMultipleOverlaysAsync(
                        deleteItems,
                        onProgress: (completed, total) =>
                        {
                            int done = completedOverall + completed;
                            if (this.InvokeRequired)
                                this.Invoke(new Action(() =>
                                {
                                    statusLabel.Text = $"Pushing {done}/{totalToPush}...";
                                    Application.DoEvents();
                                }));
                            else
                            {
                                statusLabel.Text = $"Pushing {done}/{totalToPush}...";
                                Application.DoEvents();
                            }
                        });
                    results.AddRange(deleteResults);
                    completedOverall += pendingOverlayDeletes.Count;
                }

                int successCount = 0;
                int failCount = 0;
                var errors = new StringBuilder();

                foreach (var r in results)
                {
                    if (r.Success)
                    {
                        successCount++;
                        System.Diagnostics.Debug.WriteLine($"Pushed {r.GraphicId} to JarJar");
                    }
                    else
                    {
                        failCount++;
                        errors.AppendLine($"  {r.GraphicId}: {r.Message}");
                        System.Diagnostics.Debug.WriteLine($"FAILED {r.GraphicId}: {r.Message}");
                    }
                }

                if (failCount == 0)
                {
                    pendingUnderlays.Clear();
                    pendingOverlays.Clear();
                    pendingOverlayDeletes.Clear();
                    MessageBox.Show(
                        $"Successfully pushed {successCount} item(s) to MeesaJarJar!\n\n" +
                        "Game clients will auto-update within ~30 seconds.",
                        "Push Complete",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    statusLabel.Text = $"Pushed {successCount} item(s) to JarJar";
                }

                else if (successCount > 0)
                {
                    MessageBox.Show(
                        $"Pushed {successCount} of {results.Count} items.\n\n" +
                        $"Failed ({failCount}):\n{errors}",
                        "Partial Push",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    statusLabel.Text = $"Pushed {successCount}/{results.Count} to JarJar";
                }
                else
                {
                    MessageBox.Show(
                        $"Failed to push all items:\n\n{errors}",
                        "Push Failed",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    statusLabel.Text = "Push to JarJar failed!";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error pushing to MeesaJarJar:\n\n{ex.Message}",
                    "Push Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                statusLabel.Text = $"JarJar error: {ex.Message}";
            }
            finally
            {
                client?.Dispose();
            }
        }

        /// <summary>
        /// Pull underlay index + images for the current facet from the JarJar server
        /// and display them in the editor, replacing any already-loaded server underlays.
        /// </summary>
        private async Task PullUnderlaysFromJarJar()
        {
            var config = AppConfig.Instance;
            if (!config.JarJarPushEnabled)
            {
                MessageBox.Show("JarJar is disabled in settings.",
                    "Pull Disabled", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int facet = facetComboBox?.SelectedIndex ?? 0;

            JarJarClient client = null;
            try
            {
                client = new JarJarClient(config.JarJarApiUrl, config.JarJarAuthToken);

                statusLabel.Text = "Connecting to JarJar...";
                Application.DoEvents();

                bool connected = await client.TestConnectionAsync();
                if (!connected)
                {
                    MessageBox.Show(
                        $"Cannot connect to {config.JarJarApiUrl}\n\nCheck the server URL in Settings.",
                        "Connection Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    statusLabel.Text = "JarJar connection failed";
                    return;
                }

                statusLabel.Text = $"Fetching underlay index for facet {facet}...";
                Application.DoEvents();

                var entries = await client.GetUnderlayIndexAsync(facet);
                if (entries == null || entries.Count == 0)
                {
                    MessageBox.Show(
                        $"No underlays found on server for facet {facet}.",
                        "Pull Underlays", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    statusLabel.Text = "No server underlays for this facet";
                    return;
                }

                // Remove existing server-sourced underlays for this facet
                for (int i = underlayPlanes.Count - 1; i >= 0; i--)
                {
                    var p = underlayPlanes[i];
                    if (p != null && p.Facet == facet && p.IsFromServer)
                    {
                        p.Image?.Dispose();
                        underlayPlanes.RemoveAt(i);
                    }
                }

                int loaded = 0;
                int failed = 0;

                for (int idx = 0; idx < entries.Count; idx++)
                {
                    var entry = entries[idx];
                    statusLabel.Text = $"Downloading underlay {idx + 1}/{entries.Count}...";
                    Application.DoEvents();

                    try
                    {
                        Bitmap image = await client.DownloadUnderlayImageAsync(entry.ImageUrl);
                        if (image == null)
                        {
                            failed++;
                            continue;
                        }

                        // Cache to local Underlays folder so it survives reconnects
                        string underlayFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Underlays");
                        if (!Directory.Exists(underlayFolder))
                            Directory.CreateDirectory(underlayFolder);

                        string fileName = $"server_f{facet}_{entry.Left}_{entry.Top}_{entry.Width}_{entry.Height}.png";
                        string filePath = Path.Combine(underlayFolder, fileName);
                        image.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);

                        underlayPlanes.Add(new UnderlayPlane
                        {
                            Facet    = facet,
                            Bounds   = new Rectangle(entry.Left, entry.Top, entry.Width, entry.Height),
                            Image    = image,
                            GumpId   = -1,
                            FilePath = filePath,
                            IsFromServer = true
                        });

                        loaded++;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"PullUnderlays: failed entry {idx}: {ex.Message}");
                        failed++;
                    }
                }

                GenerateMapImage();

                statusLabel.Text = $"Loaded {loaded} server underlay(s) for facet {facet}";
                string summary = $"Loaded {loaded} underlay(s) from JarJar for facet {facet}.";
                if (failed > 0)
                    summary += $"\n{failed} failed to download.";

                MessageBox.Show(summary, "Pull Underlays", MessageBoxButtons.OK,
                    failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error pulling underlays from JarJar:\n\n{ex.Message}",
                    "Pull Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                statusLabel.Text = $"Pull error: {ex.Message}";
            }
            finally
            {
                client?.Dispose();
            }
        }

        /// <summary>
        /// Pull overlay index + images for the current facet from the JarJar server
        /// and display them in the editor, replacing any already-loaded server overlays.
        /// </summary>
        private async Task PullOverlaysFromJarJar()
        {
            var config = AppConfig.Instance;
            if (!config.JarJarPushEnabled)
            {
                MessageBox.Show("JarJar is disabled in settings.",
                    "Pull Disabled", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int facet = facetComboBox?.SelectedIndex ?? 0;

            JarJarClient client = null;
            try
            {
                client = new JarJarClient(config.JarJarApiUrl, config.JarJarAuthToken);

                statusLabel.Text = "Connecting to JarJar...";
                Application.DoEvents();

                bool connected = await client.TestConnectionAsync();
                if (!connected)
                {
                    MessageBox.Show(
                        $"Cannot connect to {config.JarJarApiUrl}\n\nCheck the server URL in Settings.",
                        "Connection Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    statusLabel.Text = "JarJar connection failed";
                    return;
                }

                statusLabel.Text = $"Fetching overlay index for facet {facet}...";
                Application.DoEvents();

                var entries = await client.GetOverlayIndexAsync(facet);
                if (entries == null || entries.Count == 0)
                {
                    MessageBox.Show(
                        $"No overlays found on server for facet {facet}.",
                        "Pull Overlays", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    statusLabel.Text = "No server overlays for this facet";
                    return;
                }

                for (int i = overlayPlanes.Count - 1; i >= 0; i--)
                {
                    var p = overlayPlanes[i];
                    if (p != null && p.Facet == facet && p.IsFromServer)
                    {
                        p.Image?.Dispose();
                        overlayPlanes.RemoveAt(i);
                    }
                }

                int loaded = 0;
                int failed = 0;

                for (int idx = 0; idx < entries.Count; idx++)
                {
                    var entry = entries[idx];
                    statusLabel.Text = $"Downloading overlay {idx + 1}/{entries.Count}...";
                    Application.DoEvents();

                    try
                    {
                        Bitmap image = await client.DownloadOverlayImageAsync(entry.ImageUrl);
                        if (image == null)
                        {
                            failed++;
                            continue;
                        }

                        string overlayFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Overlays");
                        if (!Directory.Exists(overlayFolder))
                            Directory.CreateDirectory(overlayFolder);

                        string fileName = $"server_f{facet}_{entry.Left}_{entry.Top}_{entry.Width}_{entry.Height}.png";
                        string filePath = Path.Combine(overlayFolder, fileName);
                        image.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);

                        overlayPlanes.Add(new OverlayPlane
                        {
                            Facet = facet,
                            Bounds = new Rectangle(entry.Left, entry.Top, entry.Width, entry.Height),
                            Image = image,
                            FilePath = filePath,
                            IsFromServer = true
                        });

                        loaded++;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"PullOverlays: failed entry {idx}: {ex.Message}");
                        failed++;
                    }
                }

                GenerateMapImage();

                statusLabel.Text = $"Loaded {loaded} server overlay(s) for facet {facet}";
                string summary = $"Loaded {loaded} overlay(s) from JarJar for facet {facet}.";
                if (failed > 0)
                    summary += $"\n{failed} failed to download.";

                MessageBox.Show(summary, "Pull Overlays", MessageBoxButtons.OK,
                    failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error pulling overlays from JarJar:\n\n{ex.Message}",
                    "Pull Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                statusLabel.Text = $"Pull error: {ex.Message}";
            }
            finally
            {
                client?.Dispose();
            }
        }

        private async Task PullMapLayersFromJarJar()
        {
            await PullUnderlaysFromJarJar();
            await PullOverlaysFromJarJar();
        }

        // ================================================================
        //  Full Map + Statics saving  (NEW)
        // ================================================================

        /// <summary>
        /// Update the save button enabled/disabled state based on dirty tracking.
        /// </summary>
        internal void UpdateSaveButtonState()
        {
            if (saveMapButton != null)
                saveMapButton.Enabled = hasUnsavedChanges && currentMap != null;

            if (deployButton != null)
            {
                // Deploy is available whenever a map is loaded and the UO folder is set.
                // It does not require hasUnsavedChanges because the user may want to
                // deploy a loaded save iteration or push an already-saved state.
                bool canDeploy = currentMap != null
                    && !string.IsNullOrEmpty(mulFolderPath)
                    && Directory.Exists(mulFolderPath);
                deployButton.Enabled = canDeploy;
            }

            if (saveStatusLabel != null)
            {
                if (hasUnsavedChanges)
                {
                    int overrideCount = staticOverrides.Count;
                    saveStatusLabel.Text = overrideCount > 0
                        ? $"Unsaved: map + {overrideCount} static position(s)"
                        : "Unsaved: map changes";
                    saveStatusLabel.ForeColor = HolographicTheme.ButtonWarning;
                }
                else
                {
                    saveStatusLabel.Text = "All changes saved";
                    saveStatusLabel.ForeColor = HolographicTheme.TextSecondary;
                }
            }
        }

        /// <summary>
        /// Save the current map and statics as a new iteration to the saves folder.
        /// </summary>
        private void SaveMapIteration()
        {
            if (currentMap == null)
            {
                MessageBox.Show("No map loaded.", "Save Map",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Allow saving even without a MUL folder (e.g. a new blank map).
            // The save manager will skip statics files if the source doesn't exist.
            string sourceFolder = mapSourceFolder ?? mulFolderPath ?? "";

            int mapIndex = facetComboBox?.SelectedIndex ?? 0;
            string facetName = facetComboBox?.SelectedItem?.ToString() ?? $"Map {mapIndex}";

            try
            {
                statusLabel.Text = "Saving map iteration...";
                Application.DoEvents();

                var result = mapSaveManager.SaveIteration(
                    sourceFolder, mapIndex, currentMap, staticOverrides,
                    $"Facet: {facetName}");

                if (result.Success)
                {
                    SaveUnderlayMetadata(result.SaveFolder, mapIndex, copyPngIntoSaveFolder: true);
                    SaveOverlayMetadata(result.SaveFolder, mapIndex, copyPngIntoSaveFolder: true);

                    hasUnsavedChanges = false;
                    UpdateSaveButtonState();
                    RefreshSavesList();

                    statusLabel.Text = $"Saved iteration: {result.SaveName}";

                    MessageBox.Show(
                        $"Map saved successfully!\n\n" +
                        $"Location:\n{result.SaveFolder}\n\n" +
                        $"Map file: {(result.MapFileSaved ? "Yes" : "No")}\n" +
                        $"Statics: {(result.StaticsFileSaved ? "Yes" : "No")}\n\n" +
                        "Use 'Load Save' to reload this version,\n" +
                        "or 'Deploy to UO' to copy it to your game folder.",
                        "Save Complete",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(
                        $"Failed to save map:\n\n{result.ErrorMessage}",
                        "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    statusLabel.Text = $"Save failed: {result.ErrorMessage}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving map:\n\n{ex.Message}",
                    "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                statusLabel.Text = $"Save error: {ex.Message}";
            }
        }

        /// <summary>
        /// Load a previously saved iteration back into the viewer.
        /// </summary>
        private void LoadSavedIteration()
        {
            if (savesListBox == null || savesListBox.SelectedItem == null)
            {
                // No list selection — open folder browser
                if (hasUnsavedChanges)
                {
                    var guard = MessageBox.Show(
                        "You have unsaved changes. Load a saved map anyway?\n(Current changes will be lost)",
                        "Unsaved Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (guard != DialogResult.Yes) return;
                }

                using (var fbd = new FolderBrowserDialog())
                {
                    fbd.Description = "Select a saved map folder (contains map*.mul files)";
                    fbd.SelectedPath = mapSaveManager.SavesRootFolder;

                    if (fbd.ShowDialog(this) != DialogResult.OK)
                        return;

                    LoadMapFromFolder(fbd.SelectedPath);
                }
                return;
            }

            var info = savesListBox.SelectedItem as MapSaveInfo;
            if (info == null) return;

            if (hasUnsavedChanges)
            {
                var confirm = MessageBox.Show(
                    "You have unsaved changes. Load a saved iteration anyway?\n(Current changes will be lost)",
                    "Unsaved Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;
            }

            LoadMapFromFolder(info.FolderPath, info.MapIndex);
        }

        /// <summary>
        /// Load map and statics from an arbitrary folder.
        /// Art, tiledata, and hues still come from the main MUL folder.
        /// </summary>
        private void LoadMapFromFolder(string folder, int mapIndexHint = -1)
        {
            // Auto-detect map index from files in the folder
            int mapIndex = mapIndexHint;
            if (mapIndex < 0)
            {
                for (int i = 0; i <= 10; i++)
                {
                    if (File.Exists(Path.Combine(folder, $"map{i}.mul")))
                    {
                        mapIndex = i;
                        break;
                    }
                }
            }

            if (mapIndex < 0)
            {
                MessageBox.Show("No map files found in the selected folder.",
                    "Load Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // Suppress the facet-change handler so it doesn't double-prompt
                suppressFacetChange = true;
                if (mapIndex < facetComboBox.Items.Count)
                    facetComboBox.SelectedIndex = mapIndex;
                suppressFacetChange = false;

                LoadMap(mapIndex, folder);
                LoadUnderlayMetadata(folder, mapIndex);
                LoadOverlayMetadata(folder, mapIndex);
                GenerateMapImage();

                statusLabel.Text = $"Loaded save: {Path.GetFileName(folder)} (map{mapIndex})";
            }
            catch (Exception ex)
            {
                suppressFacetChange = false;
                MessageBox.Show($"Error loading save:\n\n{ex.Message}",
                    "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Deploy the current map state to the UO game folder (with backups).
        /// </summary>
        private void DeployToGameFolder()
        {
            if (currentMap == null)
            {
                MessageBox.Show("No map loaded.", "Deploy",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(mulFolderPath) || !Directory.Exists(mulFolderPath))
            {
                MessageBox.Show("UO MUL folder is not set.", "Deploy",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            int mapIndex = facetComboBox?.SelectedIndex ?? 0;

            var confirm = MessageBox.Show(
                $"This will overwrite map files in your UO folder:\n\n" +
                $"{mulFolderPath}\n\n" +
                $"Files affected:\n" +
                $"  • map{mapIndex}.mul\n" +
                $"  • staidx{mapIndex}.mul\n" +
                $"  • statics{mapIndex}.mul\n\n" +
                "Backup copies (.bak) will be created first.\n\n" +
                "Continue?",
                "Deploy to UO Folder",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            try
            {
                statusLabel.Text = "Deploying to UO folder...";
                Application.DoEvents();

                var result = mapSaveManager.DeployToGameFolder(
                    mulFolderPath, mapIndex, currentMap, staticOverrides,
                    mapSourceFolder ?? mulFolderPath);

                if (result.Success)
                {
                    SaveUnderlayMetadata(mulFolderPath, mapIndex, copyPngIntoSaveFolder: false);
                    SaveOverlayMetadata(mulFolderPath, mapIndex, copyPngIntoSaveFolder: false);

                    hasUnsavedChanges = false;

                    // Point map source back to the UO folder and clear overrides
                    // since the changes are now baked into the deployed files.
                    mapSourceFolder = mulFolderPath;
                    staticOverrides.Clear();
                    landTileOverrides.Clear();

                    UpdateSaveButtonState();
                    statusLabel.Text = "Deployed to UO folder! Backups created with .bak extension.";

                    MessageBox.Show(
                        "Map files deployed to your UO folder.\n\n" +
                        "Backup files were created with .bak timestamp extensions.\n" +
                        "To revert, rename the .bak files back to their original names.",
                        "Deploy Complete",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(
                        $"Deploy failed:\n\n{result.ErrorMessage}",
                        "Deploy Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    statusLabel.Text = $"Deploy failed: {result.ErrorMessage}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error deploying:\n\n{ex.Message}",
                    "Deploy Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                statusLabel.Text = $"Deploy error: {ex.Message}";
            }
        }

        private string GetUnderlayMetadataPath(string folder, int mapIndex)
        {
            return Path.Combine(folder, $"underlays_map{mapIndex}.txt");
        }

        private void SaveUnderlayMetadata(string folder, int mapIndex, bool copyPngIntoSaveFolder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return;

            try
            {
                string metadataPath = GetUnderlayMetadataPath(folder, mapIndex);
                string underlayImageFolder = Path.Combine(folder, "Underlays");
                if (copyPngIntoSaveFolder && !Directory.Exists(underlayImageFolder))
                    Directory.CreateDirectory(underlayImageFolder);

                var lines = new List<string>();
                lines.Add("# facet|left|top|width|height|gumpId|imageFile");

                foreach (var p in underlayPlanes)
                {
                    if (p == null || p.Facet != mapIndex)
                        continue;

                    string imageFile = string.Empty;
                    if (!string.IsNullOrWhiteSpace(p.FilePath))
                    {
                        imageFile = Path.GetFileName(p.FilePath);

                        if (copyPngIntoSaveFolder && File.Exists(p.FilePath))
                        {
                            string dest = Path.Combine(underlayImageFolder, imageFile);
                            File.Copy(p.FilePath, dest, true);
                        }
                    }

                    lines.Add(string.Join("|", new[]
                    {
                        p.Facet.ToString(),
                        p.Bounds.Left.ToString(),
                        p.Bounds.Top.ToString(),
                        p.Bounds.Width.ToString(),
                        p.Bounds.Height.ToString(),
                        p.GumpId.ToString(),
                        imageFile
                    }));
                }

                File.WriteAllLines(metadataPath, lines.ToArray());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveUnderlayMetadata failed: {ex.Message}");
            }
        }

        private void LoadUnderlayMetadata(string folder, int mapIndex)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return;

            string metadataPath = GetUnderlayMetadataPath(folder, mapIndex);
            if (!File.Exists(metadataPath))
                return;

            try
            {
                for (int i = underlayPlanes.Count - 1; i >= 0; i--)
                {
                    if (underlayPlanes[i] != null && underlayPlanes[i].Facet == mapIndex)
                    {
                        underlayPlanes[i].Image?.Dispose();
                        underlayPlanes.RemoveAt(i);
                    }
                }

                var lines = File.ReadAllLines(metadataPath);
                string underlayImageFolder = Path.Combine(folder, "Underlays");

                foreach (var raw in lines)
                {
                    if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("#"))
                        continue;

                    string[] parts = raw.Split('|');
                    if (parts.Length < 7)
                        continue;

                    if (!int.TryParse(parts[0], out int facet)) continue;
                    if (!int.TryParse(parts[1], out int left)) continue;
                    if (!int.TryParse(parts[2], out int top)) continue;
                    if (!int.TryParse(parts[3], out int width)) continue;
                    if (!int.TryParse(parts[4], out int height)) continue;
                    if (!int.TryParse(parts[5], out int gumpId)) gumpId = -1;

                    string imageFile = parts[6] ?? string.Empty;

                    Bitmap image = null;
                    string resolvedPngPath = string.Empty;

                    if (!string.IsNullOrWhiteSpace(imageFile))
                    {
                        string pngPath = Path.Combine(underlayImageFolder, imageFile);
                        if (File.Exists(pngPath))
                        {
                            using (var temp = new Bitmap(pngPath))
                                image = new Bitmap(temp);
                            resolvedPngPath = pngPath;
                        }
                    }

                    if (image == null && gumpId >= 0)
                    {
                        image = GumpArtReader.LoadGumpArt(folder, gumpId);
                    }

                    if (image == null)
                        continue;

                    underlayPlanes.Add(new UnderlayPlane
                    {
                        Facet = facet,
                        Bounds = new Rectangle(left, top, width, height),
                        Image = image,
                        GumpId = gumpId,
                        FilePath = resolvedPngPath
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadUnderlayMetadata failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Refresh the saves list box with available iterations.
        /// </summary>
        private void RefreshSavesList()
        {
            if (savesListBox == null) return;

            int mapIndex = facetComboBox?.SelectedIndex ?? 0;
            var saves = mapSaveManager.GetSavedIterations(mapIndex);

            savesListBox.Items.Clear();
            foreach (var save in saves)
                savesListBox.Items.Add(save);

            if (deleteSaveButton != null)
                deleteSaveButton.Enabled = false;
        }

        /// <summary>
        /// Delete a saved iteration.
        /// </summary>
        private void DeleteSelectedSave()
        {
            if (savesListBox == null || savesListBox.SelectedItem == null) return;

            var info = savesListBox.SelectedItem as MapSaveInfo;
            if (info == null) return;

            var confirm = MessageBox.Show(
                $"Delete saved iteration?\n\n{info.Name}\n{info.FolderPath}",
                "Delete Save", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            if (mapSaveManager.DeleteIteration(info.FolderPath))
            {
                RefreshSavesList();
                statusLabel.Text = $"Deleted save: {info.Name}";
            }
        }

        /// <summary>
        /// Open the saves folder in Windows Explorer.
        /// </summary>
        private void OpenSavesFolder()
        {
            try
            {
                string folder = mapSaveManager.SavesRootFolder;
                if (Directory.Exists(folder))
                    System.Diagnostics.Process.Start("explorer.exe", folder);
                else
                    MessageBox.Show("Saves folder does not exist yet.\nSave a map first.",
                        "Open Saves Folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch { }
        }

        // ================================================================
        //  Form closing guard
        // ================================================================

        private void CheckUnsavedChangesOnClose(FormClosingEventArgs e)
        {
            bool hasChanges = hasUnsavedChanges || pendingArtChanges.Count > 0 || pendingUnderlays.Count > 0;
            if (!hasChanges) return;

            string details = "";
            if (hasUnsavedChanges) details += "• Unsaved map/statics changes\n";
            if (pendingArtChanges.Count > 0) details += $"• {pendingArtChanges.Count} unsaved art change(s)\n";
            if (pendingUnderlays.Count > 0) details += $"• {pendingUnderlays.Count} unsent underlay area upload(s)\n";

            var result = MessageBox.Show(
                $"You have unsaved changes:\n\n{details}\nSave before closing?",
                "Unsaved Changes",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                if (hasUnsavedChanges)
                    SaveMapIteration();
                if (pendingArtChanges.Count > 0)
                    SaveChangesToMul();
            }
            else if (result == DialogResult.Cancel)
            {
                e.Cancel = true;
            }
        }
    }
}
