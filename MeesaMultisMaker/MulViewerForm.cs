using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using MeesaMultisMaker.Mul;

namespace MeesaMultisMaker
{
    /// <summary>
    /// MUL Viewer form for viewing and generating multi structures.
    /// Split across: MulViewerForm.cs, Fields.cs, UI.cs, Preview.cs, Generation.cs
    /// </summary>
    public partial class MulViewerForm : Form
    {
        public MulViewerForm()
        {
            InitializeComponent();
        }

        private void LoadEntries()
        {
            try
            {
                entries = MultiReader.Load(pathBox.Text);
                currentMulFolder = pathBox.Text;
                artCache.Clear();

                // Ensure TileData is loaded so tile classification uses real
                // flags (Wall, Door, Roof, Surface, Impassable, etc.) instead
                // of Z-heuristic guessing.
                EnsureTileDataLoaded(pathBox.Text);

                RefreshMultiList();
                partsView.Items.Clear();
                exportBox.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Mul Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                statusLabel.Text = ex.Message;
            }
        }

        /// <summary>
        /// Loads TileData from the MUL folder if not already available.
        /// TileData provides item flags (Wall, Door, Roof, Surface, Impassable, etc.)
        /// that are essential for accurate tile role classification.
        /// Without it, everything falls back to Z-based heuristics.
        /// </summary>
        private void EnsureTileDataLoaded(string mulFolder)
        {
            if (string.IsNullOrEmpty(mulFolder))
                return;

            // Already loaded?
            if (Generation.TileDataLookup.GetItemData(1) != null)
                return;

            try
            {
                var reader = new TileDataReader();
                reader.Load(mulFolder);
                if (reader.IsLoaded)
                {
                    Generation.TileDataLookup.SetReader(reader);
                    LogMessage($"TileData loaded from {mulFolder}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"TileData load failed: {ex.Message} — classification will use Z-heuristics only");
            }
        }

        private void RefreshMultiList()
        {
            if (entries == null) return;
            bool hideEmpty = hideEmptyCheck?.Checked ?? false;
            int totalCount = entries.Count, shownCount = 0;
            
            multiList.BeginUpdate();
            multiList.Items.Clear();
            foreach (var e in entries)
            {
                if (hideEmpty && (e.Components == null || e.Components.Count == 0)) continue;
                int w = 0, h = 0;
                if (e.Components != null && e.Components.Count > 0)
                {
                    w = e.Components.Max(c => c.X) - e.Components.Min(c => c.X) + 1;
                    h = e.Components.Max(c => c.Y) - e.Components.Min(c => c.Y) + 1;
                }
                multiList.Items.Add(new ListBoxEntryItem(e, w, h));
                shownCount++;
            }
            multiList.EndUpdate();
            statusLabel.Text = hideEmpty ? $"Showing {shownCount} of {totalCount} entries" : $"Loaded {totalCount} entries";
        }

        private void MultiList_SelectedIndexChanged(object sender, EventArgs e)
        {
            partsView.Items.Clear();
            exportBox.Clear();
            showingGenerated = false;
            generatedComponents = null;
            panOffsetX = panOffsetY = 0f;

            if (multiList.SelectedItems.Count != 1) { previewBox?.Invalidate(); return; }
            var selectedItem = multiList.SelectedItem as ListBoxEntryItem;
            if (selectedItem == null) { previewBox?.Invalidate(); return; }

            // Detect floor levels for the selected multi
            var comps = selectedItem.Entry.Components;
            var floorLevels = Generation.MultiRules.DetectFloorLevels(comps.Select(c => (int)c.Z));

            foreach (var c in comps)
            {
                var role = Generation.MultiRules.ClassifyRole(c.TileId, c.Z, c.Flags, floorLevels);
                int floorLevel = Generation.MultiRules.GetFloorLevel(c.Z);
                partsView.Items.Add(new ListViewItem(new[]
                {
                    $"0x{c.TileId:X4}",
                    c.X.ToString(),
                    c.Y.ToString(),
                    c.Z.ToString(),
                    c.Flags.ToString(),
                    role.ToString(),
                    floorLevel.ToString()
                }));
            }

            RefreshExport();
            previewBox?.Invalidate();
        }

        private void RefreshExport()
        {
            if (multiList.SelectedItems.Count != 1) { exportBox.Clear(); return; }
            var selectedItem = multiList.SelectedItem as ListBoxEntryItem;
            if (selectedItem == null) { exportBox.Clear(); return; }
            
            var entry = selectedItem.Entry;
            IEnumerable<MultiComponent> comps = entry.Components;
            if (normalizeCheck.Checked && comps.Any())
            {
                int minX = comps.Min(c => c.X), minY = comps.Min(c => c.Y);
                comps = comps.Select(c => new MultiComponent { TileId = c.TileId, X = (short)(c.X - minX), Y = (short)(c.Y - minY), Z = c.Z, Flags = c.Flags, Unk1 = c.Unk1 })
                             .OrderBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z).ThenBy(c => c.TileId);
            }
            var sb = new StringBuilder();
            bool hs = entry.EntrySize == 16;
            foreach (var c in comps)
                sb.AppendLine(hs ? $"0x{c.TileId:X4}\t{c.X}\t{c.Y}\t{c.Z}\t{c.Flags}\t{c.Unk1}" : $"0x{c.TileId:X4}\t{c.X}\t{c.Y}\t{c.Z}\t{c.Flags}");
            exportBox.Text = sb.ToString();
        }

        private void OpenLogFile()
        {
            string logPath = Path.Combine(Path.GetTempPath(), "MeesaMulGen", "log.txt");
            if (!File.Exists(logPath)) { MessageBox.Show(this, "No log found yet.", "Log", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            try { Process.Start(new ProcessStartInfo { FileName = logPath, UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "View Log Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void LogMessage(string message)
        {
            try
            {
                if (statusLabel != null) statusLabel.Text = message;
                Debug.WriteLine("[MulViewer] " + message);
                string dir = Path.Combine(Path.GetTempPath(), "MeesaMulGen");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "log.txt"), DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch { }
        }

        private static bool SafeCopyToClipboard(string text, Action<string> logger = null)
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try { if (string.IsNullOrEmpty(text)) return false; Clipboard.SetText(text); return true; }
                catch { System.Threading.Thread.Sleep(120); }
            }
            return false;
        }

        private void SendToCanvasBtn_Click(object sender, EventArgs e) => SendToCanvas(true);
        private void AddToCanvasBtn_Click(object sender, EventArgs e) => SendToCanvas(false);

        private void SendToCanvas(bool replaceCanvas)
        {
            Form1 mainForm = Application.OpenForms.OfType<Form1>().FirstOrDefault();
            if (mainForm == null) { MessageBox.Show(this, "Main form not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            
            try
            {
                IEnumerable<MultiComponent> comps;
                if (showingGenerated && generatedComponents?.Count > 0)
                    comps = generatedComponents;
                else
                {
                    if (multiList.SelectedItems.Count != 1) { MessageBox.Show(this, "Select a multi entry first.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                    var selectedItem = multiList.SelectedItem as ListBoxEntryItem;
                    if (selectedItem?.Entry?.Components == null || selectedItem.Entry.Components.Count == 0) { MessageBox.Show(this, "No components.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    comps = selectedItem.Entry.Components;
                    if (normalizeCheck.Checked && comps.Any())
                    {
                        int minX = comps.Min(c => c.X), minY = comps.Min(c => c.Y);
                        comps = comps.Select(c => new MultiComponent { TileId = c.TileId, X = (short)(c.X - minX), Y = (short)(c.Y - minY), Z = c.Z, Flags = c.Flags, Unk1 = c.Unk1 });
                    }
                }

                var sb = new StringBuilder();
                foreach (var c in comps) sb.AppendLine($"0x{c.TileId:X4}\t{c.X}\t{c.Y}\t{c.Z}\t{c.Flags}");
                if (replaceCanvas) mainForm.ImportMultiText(sb.ToString()); else mainForm.AddMultiText(sb.ToString());
                statusLabel.Text = $"{(replaceCanvas ? "Sent" : "Added")} {comps.Count()} parts to canvas";
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        /// <summary>
        /// Batch-add selected multis as training data for the Form1 generation pipeline.
        /// This bridges MulViewer's multi.mul data directly to the WFC building generator.
        /// </summary>
        private void TrainFromSelectedMultis_Click(object sender, EventArgs e)
        {
            if (multiList.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Select one or more multis to use as training data.", "Train", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Form1 mainForm = Application.OpenForms.OfType<Form1>().FirstOrDefault();
            if (mainForm == null)
            {
                MessageBox.Show(this, "Main form not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            int count = 0;
            foreach (var item in multiList.SelectedItems)
            {
                var entryItem = item as ListBoxEntryItem;
                if (entryItem?.Entry?.Components == null || entryItem.Entry.Components.Count == 0)
                    continue;

                var comps = entryItem.Entry.Components;
                if (normalizeCheck.Checked && comps.Any())
                {
                    int minX = comps.Min(c => c.X), minY = comps.Min(c => c.Y);
                    comps = comps.Select(c => new MultiComponent
                    {
                        TileId = c.TileId,
                        X = (short)(c.X - minX),
                        Y = (short)(c.Y - minY),
                        Z = c.Z,
                        Flags = c.Flags,
                        Unk1 = c.Unk1
                    }).ToList();
                }

                var sb = new StringBuilder();
                foreach (var c in comps)
                    sb.AppendLine($"0x{c.TileId:X4}\t{c.X}\t{c.Y}\t{c.Z}\t{c.Flags}");

                mainForm.AddMultiTraining(sb.ToString());
                count++;
            }

            statusLabel.Text = $"Added {count} multi(s) as training data";
            MessageBox.Show(this,
                $"Added {count} multi structure(s) as training examples.\n\n" +
                "To generate a building variant:\n" +
                "1. Go to the main form\n" +
                "2. Click 'Generate Variant'\n" +
                "3. Adjust settings and click Generate",
                "Training Data Added",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// Validate the selected multi against UO house/multi structural rules.
        /// Shows floor levels, separation gaps, foundation status, wall coverage,
        /// and structural support diagnostics.
        /// </summary>
        private void ValidateBtn_Click(object sender, EventArgs e)
        {
            List<MultiComponent> comps = null;
            string title;

            if (showingGenerated && generatedComponents != null && generatedComponents.Count > 0)
            {
                comps = generatedComponents;
                title = "Generated Structure";
            }
            else
            {
                if (multiList.SelectedItems.Count != 1)
                {
                    MessageBox.Show(this, "Select a multi entry first.", "Validate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var selectedItem = multiList.SelectedItem as ListBoxEntryItem;
                if (selectedItem?.Entry?.Components == null || selectedItem.Entry.Components.Count == 0)
                {
                    MessageBox.Show(this, "No components to validate.", "Validate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                comps = selectedItem.Entry.Components;
                title = $"Multi #{selectedItem.Entry.Index}";
            }

            var result = Generation.MultiValidator.ValidateComponents(comps);

            // Detect floor levels for the summary
            var floorLevels = Generation.MultiRules.DetectFloorLevels(comps.Select(c => (int)c.Z));

            var sb = new StringBuilder();
            sb.AppendLine($"Validation: {title}");
            sb.AppendLine($"Components: {comps.Count}");
            sb.AppendLine($"Status: {(result.IsValid ? "VALID" : "INVALID")}");
            sb.AppendLine();

            // Floor level summary
            sb.AppendLine("═══ Floor Levels ═══");
            for (int i = 0; i < floorLevels.Count; i++)
            {
                int floorZ = floorLevels[i];
                int tileCount = comps.Count(c => Generation.MultiRules.AssignToFloor(c.Z, floorLevels) == floorZ);
                string sep = i > 0 ? $" (gap={floorLevels[i] - floorLevels[i - 1]})" : "";
                sb.AppendLine($"  Floor {i}: Z={floorZ} — {tileCount} tiles{sep}");
            }
            sb.AppendLine();

            // Tile role breakdown
            sb.AppendLine("═══ Tile Roles ═══");
            var roleCounts = new Dictionary<string, int>();
            foreach (var c in comps)
            {
                var role = Generation.MultiRules.ClassifyRole(c.TileId, c.Z, c.Flags, floorLevels);
                string roleName = role.ToString();
                if (!roleCounts.ContainsKey(roleName))
                    roleCounts[roleName] = 0;
                roleCounts[roleName]++;
            }
            foreach (var kv in roleCounts)
                sb.AppendLine($"  {kv.Key}: {kv.Value}");
            sb.AppendLine();

            // TileData flag sample — show a few unique tiles per role so the
            // user can verify classification is based on the right flags.
            sb.AppendLine("═══ Classification Samples ═══");
            var sampled = new HashSet<ushort>();
            var samplesPerRole = new Dictionary<string, int>();
            foreach (var c in comps)
            {
                if (sampled.Contains(c.TileId)) continue;
                sampled.Add(c.TileId);

                var role = Generation.MultiRules.ClassifyRole(c.TileId, c.Z, c.Flags, floorLevels);
                string roleName = role.ToString();
                if (!samplesPerRole.ContainsKey(roleName))
                    samplesPerRole[roleName] = 0;
                if (samplesPerRole[roleName] >= 3) continue; // max 3 samples per role
                samplesPerRole[roleName]++;

                string flagStr = "";
                string name = "";
                int height = 0;
                var td = Generation.TileDataLookup.GetItemData(c.TileId);
                if (td != null)
                {
                    flagStr = td.GetFlagsString();
                    name = td.Name ?? "";
                    height = td.Height;
                }
                sb.AppendLine($"  0x{c.TileId:X4} → {roleName}  h={height}  \"{name}\"");
                sb.AppendLine($"         flags: {flagStr}");
            }
            sb.AppendLine();

            // Rules reference
            sb.AppendLine("═══ UO Multi Rules ═══");
            sb.AppendLine($"  Min floor separation: {Generation.MultiRules.MinFloorSeparation}Z");
            sb.AppendLine($"  Foundation Z: {Generation.MultiRules.FoundationZ}");
            sb.AppendLine($"  Step Z: {Generation.MultiRules.StepZ}");
            sb.AppendLine($"  Pixels per Z: {Generation.MultiRules.PixelsPerZ}");
            sb.AppendLine($"  Max roof overhang: {Generation.MultiRules.MaxRoofOverhang} tile(s)");
            sb.AppendLine();

            if (result.Errors.Count > 0)
            {
                sb.AppendLine("═══ Errors ═══");
                foreach (var err in result.Errors)
                    sb.AppendLine($"  ERROR {err}");
                sb.AppendLine();
            }

            if (result.Warnings.Count > 0)
            {
                sb.AppendLine("═══ Warnings ═══");
                foreach (var warn in result.Warnings)
                    sb.AppendLine($"  ! {warn}");
                sb.AppendLine();
            }

            if (result.Errors.Count == 0 && result.Warnings.Count == 0)
            {
                sb.AppendLine("No issues found. Structure conforms to UO multi rules.");
            }

            var diagForm = new Form
            {
                Text = $"Multi Validation — {title}",
                Width = 620,
                Height = 480,
                StartPosition = FormStartPosition.CenterParent
            };
            HolographicTheme.ApplyToForm(diagForm);

            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Font = new Font("Consolas", 9.5f),
                Text = sb.ToString()
            };
            HolographicTheme.ApplyToTextBox(textBox);

            diagForm.Controls.Add(textBox);
            diagForm.ShowDialog(this);
        }
    }
}
