using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using MeesaMultisMaker.MapGeneration;
using MeesaMultisMaker.Mul;

namespace MeesaMultisMaker
{
    partial class MapViewerForm
    {
        // ================================================================
        //  World Generation UI state
        // ================================================================
        private Panel worldGenPanel;
        private NumericUpDown genWidthNumeric;
        private NumericUpDown genHeightNumeric;
        private NumericUpDown genSeedNumeric;
        private TrackBar landMassTrackBar;
        private Label landMassLabel;
        private TrackBar mountainTrackBar;
        private Label mountainLabel;
        private TrackBar islandTrackBar;
        private Label islandLabel;
        private TrackBar roughnessTrackBar;
        private Label roughnessLabel;

        // Biome sliders
        private TrackBar grassTrackBar, forestTrackBar, desertTrackBar, jungleTrackBar;
        private TrackBar swampTrackBar, snowTrackBar, farmTrackBar, lavaTrackBar;

        private Button generateButton;
        private Button regenerateButton;
        private Button importHeightmapButton;
        private Label genStatusLabel;

        private WorldGenConfig currentGenConfig;

        // ================================================================
        //  Panel initialisation — called from Designer.cs
        // ================================================================

        internal void InitializeWorldGenPanel()
        {
            worldGenPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 520,
                Padding = new Padding(6),
                AutoScroll = true
            };
            HolographicTheme.ApplyToPanel(worldGenPanel, true);

            int y = 4;
            int ctrlW = 280;
            int sliderW = 145;

            // ── Title ────────────────────────────────────────────────
            var title = new Label
            {
                Text = "World Generator",
                Location = new Point(6, y),
                Width = ctrlW,
                Height = 22,
                Font = new Font(this.Font.FontFamily, 10, FontStyle.Bold)
            };
            HolographicTheme.ApplyToLabel(title);
            worldGenPanel.Controls.Add(title);
            y += 24;

            // ── Import Heightmap button ──────────────────────────────
            importHeightmapButton = new Button
            {
                Text = "Import Heightmap…",
                Location = new Point(6, y),
                Width = 130,
                Height = 24
            };
            importHeightmapButton.Click += ImportHeightmapButton_Click;
            HolographicTheme.ApplyToButton(importHeightmapButton);
            worldGenPanel.Controls.Add(importHeightmapButton);

            // ── Export Heightmap button ──────────────────────────────
            var exportHeightmapButton = new Button
            {
                Text = "Export Heightmap…",
                Location = new Point(142, y),
                Width = 130,
                Height = 24
            };
            exportHeightmapButton.Click += ExportHeightmapButton_Click;
            HolographicTheme.ApplyToButton(exportHeightmapButton, ButtonStyle.Warning);
            worldGenPanel.Controls.Add(exportHeightmapButton);
            y += 30;

            // ── Size ─────────────────────────────────────────────────
            AddLabelAt(worldGenPanel, "Width:", 6, y + 3, 40);
            genWidthNumeric = AddNumericAt(worldGenPanel, 48, y, 64, 64, 16384, 512, 8);
            AddLabelAt(worldGenPanel, "Height:", 120, y + 3, 45);
            genHeightNumeric = AddNumericAt(worldGenPanel, 168, y, 64, 64, 16384, 512, 8);
            y += 28;

            // ── Seed ─────────────────────────────────────────────────
            AddLabelAt(worldGenPanel, "Seed:", 6, y + 3, 40);
            genSeedNumeric = AddNumericAt(worldGenPanel, 48, y, 100, 0, 999999999, 0, 1);
            AddLabelAt(worldGenPanel, "(0 = random)", 155, y + 3, 90).ForeColor = Color.Gray;
            y += 28;

            // ── Terrain sliders ──────────────────────────────────────
            y = AddSliderRow(worldGenPanel, "Land %:", y, 0, 100, 40, out landMassTrackBar, out landMassLabel);
            y = AddSliderRow(worldGenPanel, "Mountains:", y, 0, 50, 10, out mountainTrackBar, out mountainLabel);
            y = AddSliderRow(worldGenPanel, "Islands:", y, 0, 100, 50, out islandTrackBar, out islandLabel);
            y = AddSliderRow(worldGenPanel, "Roughness:", y, 0, 100, 50, out roughnessTrackBar, out roughnessLabel);

            // ── Biome weight section ─────────────────────────────────
            var biomeTitle = new Label
            {
                Text = "— Biome Weights —",
                Location = new Point(6, y),
                Width = ctrlW,
                Height = 18,
                ForeColor = Color.FromArgb(0, 200, 200),
                Font = new Font(this.Font.FontFamily, 8, FontStyle.Bold)
            };
            HolographicTheme.ApplyToLabel(biomeTitle);
            worldGenPanel.Controls.Add(biomeTitle);
            y += 20;

            y = AddSliderRow(worldGenPanel, "Grass:", y, 0, 100, 30, out grassTrackBar, out _);
            y = AddSliderRow(worldGenPanel, "Forest:", y, 0, 100, 20, out forestTrackBar, out _);
            y = AddSliderRow(worldGenPanel, "Desert:", y, 0, 100, 10, out desertTrackBar, out _);
            y = AddSliderRow(worldGenPanel, "Jungle:", y, 0, 100, 8, out jungleTrackBar, out _);
            y = AddSliderRow(worldGenPanel, "Swamp:", y, 0, 100, 7, out swampTrackBar, out _);
            y = AddSliderRow(worldGenPanel, "Snow:", y, 0, 100, 8, out snowTrackBar, out _);
            y = AddSliderRow(worldGenPanel, "Farmland:", y, 0, 100, 5, out farmTrackBar, out _);
            y = AddSliderRow(worldGenPanel, "Lava:", y, 0, 100, 2, out lavaTrackBar, out _);

            // ── Buttons ──────────────────────────────────────────────
            generateButton = new Button
            {
                Text = "Generate World",
                Location = new Point(6, y),
                Width = 120,
                Height = 28
            };
            generateButton.Click += GenerateWorldButton_Click;
            HolographicTheme.ApplyToButton(generateButton, ButtonStyle.Accent);
            worldGenPanel.Controls.Add(generateButton);

            regenerateButton = new Button
            {
                Text = "Regenerate",
                Location = new Point(132, y),
                Width = 100,
                Height = 28
            };
            regenerateButton.Click += RegenerateWorldButton_Click;
            HolographicTheme.ApplyToButton(regenerateButton, ButtonStyle.Success);
            worldGenPanel.Controls.Add(regenerateButton);

            var learnRegenButton = new Button
            {
                Text = "Learn && Regenerate",
                Location = new Point(238, y),
                Width = 140,
                Height = 28
            };
            learnRegenButton.Click += LearnAndRegenerateButton_Click;
            HolographicTheme.ApplyToButton(learnRegenButton, ButtonStyle.Warning);
            worldGenPanel.Controls.Add(learnRegenButton);
            y += 34;

            // ── Status ───────────────────────────────────────────────
            genStatusLabel = new Label
            {
                Text = "Configure settings, then click Generate.",
                Location = new Point(6, y),
                Width = ctrlW,
                Height = 36,
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8)
            };
            HolographicTheme.ApplyToLabel(genStatusLabel);
            worldGenPanel.Controls.Add(genStatusLabel);

            comfySettingsPanel.Controls.Add(worldGenPanel);
        }

        // ================================================================
        //  Event handlers
        // ================================================================

        private void ImportHeightmapButton_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Select Heightmap Image";
                dlg.Filter = "Image files|*.png;*.bmp;*.jpg;*.jpeg;*.tif;*.tiff|All files|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                ShowImportHeightmapDialog(dlg.FileName);
            }
        }

        private void ShowImportHeightmapDialog(string imagePath)
        {
            using (var dlg = new Form())
            {
                dlg.Text = "Import Heightmap";
                dlg.Width = 380;
                dlg.Height = 320;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                HolographicTheme.ApplyToForm(dlg);

                int y = 15;

                // File info
                var fileLabel = new Label
                {
                    Text = $"File: {System.IO.Path.GetFileName(imagePath)}",
                    Left = 15, Top = y, Width = 340, Height = 20
                };
                HolographicTheme.ApplyToLabel(fileLabel);
                dlg.Controls.Add(fileLabel);
                y += 28;

                // Map size
                AddLabelAt(dlg, "Map Width:", 15, y + 3, 70);
                var impW = AddNumericAt(dlg, 90, y, 80, 64, 16384, 512, 8);
                AddLabelAt(dlg, "Height:", 180, y + 3, 50);
                var impH = AddNumericAt(dlg, 235, y, 80, 64, 16384, 512, 8);
                y += 30;

                // Z range
                AddLabelAt(dlg, "Z Min:", 15, y + 3, 45);
                var zMinN = AddNumericAt(dlg, 65, y, 60, -128, 127, -15, 1);
                AddLabelAt(dlg, "Z Max:", 140, y + 3, 45);
                var zMaxN = AddNumericAt(dlg, 190, y, 60, -128, 127, 80, 1);
                y += 30;

                // Water threshold
                AddLabelAt(dlg, "Water cutoff:", 15, y + 3, 85);
                var waterCut = AddNumericAt(dlg, 105, y, 60, 0, 255, 64, 1);
                AddLabelAt(dlg, "(brightness 0-255)", 175, y + 3, 140).ForeColor = Color.Gray;
                y += 30;

                // Water Z
                AddLabelAt(dlg, "Water Z:", 15, y + 3, 60);
                var waterZ = AddNumericAt(dlg, 80, y, 60, -128, 127, -5, 1);
                y += 32;

                // Auto-biomes checkbox
                var autoBiomeCheck = new CheckBox
                {
                    Text = "Auto-assign biomes (terrain tiles by elevation)",
                    Left = 15, Top = y, Width = 340, Height = 20,
                    Checked = true
                };
                HolographicTheme.ApplyToCheckBox(autoBiomeCheck);
                dlg.Controls.Add(autoBiomeCheck);
                y += 28;

                var info = new Label
                {
                    Text = "Black pixels → low Z / water.  White → high Z / land.\nBiomes use Perlin noise for moisture & temperature variation.",
                    Left = 15, Top = y, Width = 340, Height = 32,
                    ForeColor = Color.Gray, Font = new Font("Segoe UI", 8)
                };
                HolographicTheme.ApplyToLabel(info);
                dlg.Controls.Add(info);
                y += 38;

                var okBtn = new Button { Text = "Import", Left = 130, Top = y, Width = 90, Height = 28, DialogResult = DialogResult.OK };
                HolographicTheme.ApplyToButton(okBtn, ButtonStyle.Accent);
                dlg.Controls.Add(okBtn);

                var cancelBtn = new Button { Text = "Cancel", Left = 230, Top = y, Width = 80, Height = 28, DialogResult = DialogResult.Cancel };
                HolographicTheme.ApplyToButton(cancelBtn);
                dlg.Controls.Add(cancelBtn);

                dlg.AcceptButton = okBtn;
                dlg.CancelButton = cancelBtn;

                // Adjust dialog height to fit all controls
                dlg.Height = y + 75;

                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    statusLabel.Text = "Importing heightmap...";
                    Application.DoEvents();

                    var map = HeightmapImporter.Import(
                        imagePath,
                        (int)impW.Value, (int)impH.Value,
                        (sbyte)zMinN.Value, (sbyte)zMaxN.Value,
                        (int)waterCut.Value, (sbyte)waterZ.Value,
                        autoBiomeCheck.Checked);

                    ApplyGeneratedMap(map, "Imported heightmap");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Import failed:\n{ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void GenerateWorldButton_Click(object sender, EventArgs e)
        {
            RunWorldGeneration(false);
        }

        private void RegenerateWorldButton_Click(object sender, EventArgs e)
        {
            RunWorldGeneration(true);
        }

        private void LearnAndRegenerateButton_Click(object sender, EventArgs e)
        {
            RunLearnAndRegenerate();
        }

        private void RunLearnAndRegenerate()
        {
            if (currentMap == null)
            {
                MessageBox.Show("No map loaded to learn from.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (hasUnsavedChanges)
            {
                var confirm = MessageBox.Show(
                    "You have unsaved changes.\nLearn from current map and generate new world? (Changes will be lost)",
                    "Unsaved Changes",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;
            }

            generateButton.Enabled = false;
            regenerateButton.Enabled = false;

            try
            {
                genStatusLabel.Text = "Learning transition patterns from current map...";
                Application.DoEvents();

                var analyzer = new MapTransitionAnalyzer();
                analyzer.LearnFromMap(currentMap, prunePercentile: 0.10);

                genStatusLabel.Text = $"Learned {analyzer.PatternsAfterPruning:N0} patterns. Generating...";
                Application.DoEvents();

                var cfg = BuildConfigFromUI();
                // Use current map dimensions for best recreation
                cfg.Width = currentMap.Width;
                cfg.Height = currentMap.Height;

                var gen = new WorldGenerator(cfg, analyzer);
                var map = gen.Generate(p =>
                {
                    genStatusLabel.Text = $"{p.Phase} ({p.Percent:F0}%)";
                    Application.DoEvents();
                });

                ApplyGeneratedMap(map, $"Regenerated with learned patterns (seed {cfg.Seed})");
                currentGenConfig = cfg;

                MessageBox.Show(
                    $"Done!\n\n{analyzer.GetStats()}\n\nNew map uses NEW biome system but tile placement follows learned 3x3 patterns from the original map.",
                    "Learn & Regenerate Complete",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Learn & Regenerate failed:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                genStatusLabel.Text = "Learn & Regenerate failed.";
            }
            finally
            {
                generateButton.Enabled = true;
                regenerateButton.Enabled = true;
            }
        }

        private void RunWorldGeneration(bool randomSeed)
        {
            if (hasUnsavedChanges && currentMap != null)
            {
                var confirm = MessageBox.Show(
                    "You have unsaved changes.\nGenerate a new world? (Changes will be lost)",
                    "Unsaved Changes",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;
            }

            var cfg = BuildConfigFromUI();
            if (randomSeed)
                cfg.Seed = new Random().Next(1, 999999999);

            genSeedNumeric.Value = cfg.Seed;

            generateButton.Enabled = false;
            regenerateButton.Enabled = false;

            try
            {
                var gen = new WorldGenerator(cfg);
                var map = gen.Generate(p =>
                {
                    genStatusLabel.Text = $"{p.Phase} ({p.Percent:F0}%)";
                    Application.DoEvents();
                });

                ApplyGeneratedMap(map, $"Generated world (seed {cfg.Seed})");
                currentGenConfig = cfg;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Generation failed:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                genStatusLabel.Text = "Generation failed.";
            }
            finally
            {
                generateButton.Enabled = true;
                regenerateButton.Enabled = true;
            }
        }

        // ================================================================
        //  Helpers
        // ================================================================

        private WorldGenConfig BuildConfigFromUI()
        {
            return new WorldGenConfig
            {
                Width = (int)genWidthNumeric.Value,
                Height = (int)genHeightNumeric.Value,
                Seed = (int)genSeedNumeric.Value,
                LandMassPercent = landMassTrackBar.Value,
                MountainPercent = mountainTrackBar.Value,
                IslandScatter = islandTrackBar.Value / 100.0,
                TerrainRoughness = roughnessTrackBar.Value / 100.0,
                GrassWeight = grassTrackBar.Value,
                ForestWeight = forestTrackBar.Value,
                DesertWeight = desertTrackBar.Value,
                JungleWeight = jungleTrackBar.Value,
                SwampWeight = swampTrackBar.Value,
                SnowWeight = snowTrackBar.Value,
                FarmlandWeight = farmTrackBar.Value,
                LavaWeight = lavaTrackBar.Value
            };
        }

        /// <summary>
        /// Apply a generated (or imported) map as the current working map.
        /// </summary>
        private void ApplyGeneratedMap(MapData map, string description)
        {
            currentMap = map;

            staticOverrides.Clear();
            landTileOverrides.Clear();
            hasUnsavedChanges = true;
            undoRedoManager.Clear();
            cachedStaticsData = null;
            cachedArtFormat = null;
            currentStatics = null;
            minimapGenerator.Invalidate();
            minimapGenerating = false;

            cameraX = map.Width / 2;
            cameraY = map.Height / 2;

            GenerateMapImage();
            GenerateMinimapOverlay();
            UpdateSaveButtonState();
            UpdateUndoRedoButtons();

            statusLabel.Text = $"{description}: {map.Width}x{map.Height} — unsaved";
            if (genStatusLabel != null)
                genStatusLabel.Text = $"Done! {map.Width}x{map.Height}";
        }

        // ================================================================
        //  UI builder helpers
        // ================================================================

        private Label AddLabelAt(Control parent, string text, int left, int top, int width)
        {
            var lbl = new Label { Text = text, Left = left, Top = top, Width = width, Height = 18 };
            HolographicTheme.ApplyToLabel(lbl);
            parent.Controls.Add(lbl);
            return lbl;
        }

        private NumericUpDown AddNumericAt(Control parent, int left, int top, int width,
            int min, int max, int value, int increment)
        {
            var n = new NumericUpDown
            {
                Left = left, Top = top, Width = width, Height = 22,
                Minimum = min, Maximum = max, Value = value, Increment = increment
            };
            HolographicTheme.ApplyToNumericUpDown(n);
            parent.Controls.Add(n);
            return n;
        }

        /// <summary>
        /// Add a labelled TrackBar row and return the next Y position.
        /// </summary>
        private int AddSliderRow(Control parent, string label, int y,
            int min, int max, int value,
            out TrackBar trackBar, out Label valueLabel)
        {
            AddLabelAt(parent, label, 6, y + 3, 68);

            trackBar = new TrackBar
            {
                Left = 76, Top = y - 2, Width = 145, Height = 25,
                Minimum = min, Maximum = max, Value = value,
                TickFrequency = Math.Max(1, (max - min) / 10),
                AutoSize = false
            };
            HolographicTheme.ApplyToTrackBar(trackBar);
            parent.Controls.Add(trackBar);

            valueLabel = new Label
            {
                Text = value.ToString(),
                Left = 225, Top = y + 3, Width = 40, Height = 18
            };
            HolographicTheme.ApplyToLabel(valueLabel);
            parent.Controls.Add(valueLabel);

            // Capture for closure
            var vl = valueLabel;
            trackBar.ValueChanged += (s, e) => { vl.Text = ((TrackBar)s).Value.ToString(); };

            return y + 26;
        }

        // ================================================================
        //  Export Heightmap
        // ================================================================

        private void ExportHeightmapButton_Click(object sender, EventArgs e)
        {
            if (currentMap == null)
            {
                MessageBox.Show("No map loaded to export.", "Export Heightmap",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "Export Heightmap Image";
                dlg.Filter = "PNG Image|*.png|BMP Image|*.bmp|All files|*.*";
                dlg.FileName = "heightmap.png";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    statusLabel.Text = "Exporting heightmap...";
                    Application.DoEvents();

                    ExportHeightmapImage(dlg.FileName);

                    statusLabel.Text = $"Exported heightmap: {currentMap.Width}x{currentMap.Height}";
                    MessageBox.Show(
                        $"Heightmap exported successfully!\n\n" +
                        $"Size: {currentMap.Width} x {currentMap.Height}\n" +
                        $"File: {System.IO.Path.GetFileName(dlg.FileName)}\n\n" +
                        "Black = lowest Z (-128), White = highest Z (127)",
                        "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed:\n{ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>
        /// Export the current map's elevation data as a grayscale PNG.
        /// Each pixel represents one map tile. Brightness maps Z -128..127 to 0..255.
        /// Land tile overrides are included.
        /// </summary>
        private void ExportHeightmapImage(string outputPath)
        {
            int w = currentMap.Width;
            int h = currentMap.Height;

            var bmp = new Bitmap(w, h, PixelFormat.Format8bppIndexed);

            // Set up a grayscale palette
            var palette = bmp.Palette;
            for (int i = 0; i < 256; i++)
                palette.Entries[i] = Color.FromArgb(255, i, i, i);
            bmp.Palette = palette;

            // Lock bits for fast pixel writing
            var rect = new Rectangle(0, 0, w, h);
            var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);

            try
            {
                for (int y = 0; y < h; y++)
                {
                    var row = new byte[w];

                    for (int x = 0; x < w; x++)
                    {
                        sbyte z;
                        LandTile overrideTile;
                        if (landTileOverrides.TryGetValue((x, y), out overrideTile))
                            z = overrideTile.Z;
                        else
                            z = currentMap.Tiles[x, y].Z;

                        // Map -128..127 to 0..255
                        row[x] = (byte)(z + 128);
                    }

                    Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, w);
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            // Determine format from extension
            ImageFormat fmt = ImageFormat.Png;
            if (outputPath.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                fmt = ImageFormat.Bmp;

            bmp.Save(outputPath, fmt);
            bmp.Dispose();
        }
    }
}
