using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MeesaMultisMaker.Mul;

namespace MeesaMultisMaker
{
    partial class MapViewerForm
    {
        private void LoadMap(int mapIndex, string folderOverride = null)
        {
            try
            {
                string loadFolder = folderOverride ?? mulFolderPath;
                mapSourceFolder = loadFolder;

                statusLabel.Text = $"Loading map {mapIndex}...";
                Application.DoEvents();

                currentMap = MapReader.Load(loadFolder, mapIndex);
                
                // Load TileData for item properties
                if (!tileDataReader.IsLoaded)
                {
                    tileDataReader.Load(mulFolderPath);
                    // Wire up TileData for generation pipeline
                    Generation.TileDataLookup.SetReader(tileDataReader);
                }

                // Load hue color palettes
                if (!huesReader.IsLoaded)
                {
                    huesReader.Load(mulFolderPath);
                }
                
                // Invalidate minimap when loading a new map
                minimapGenerator.Invalidate();
                
                // Diagnostic: Check tile variety
                var tileIds = new System.Collections.Generic.HashSet<ushort>();
                
                for (int i = 0; i < currentMap.Width && tileIds.Count < 50; i += 10)
                {
                    for (int j = 0; j < currentMap.Height && tileIds.Count < 50; j += 10)
                    {
                        var tile = currentMap.Tiles[i, j];
                        if (tile != null)
                        {
                            tileIds.Add(tile.TileId);
                        }
                    }
                }
                
                // Clear overrides and dirty state for the fresh load
                staticOverrides.Clear();
                landTileOverrides.Clear();
                hasUnsavedChanges = false;
                undoRedoManager.Clear();
                cachedStaticsData = null; // invalidate statics cache for new map
                cachedArtFormat = null; // re-detect art format

                GenerateMapImage();
                GenerateMinimapOverlay();
                UpdateSaveButtonState();
                UpdateUndoRedoButtons();
                RefreshSavesList();
                LoadStaticPalette();
                lastLoadedFacetIndex = mapIndex;

                string sourceNote = (loadFolder != mulFolderPath) ? " [from save]" : "";
                statusLabel.Text = $"Loaded map {mapIndex}: {currentMap.Width}x{currentMap.Height} | {tileIds.Count} unique tile types{sourceNote}";

                System.Diagnostics.Debug.WriteLine($"Sample tile IDs: {string.Join(", ", tileIds.Take(20).Select(id => $"0x{id:X4}"))}");
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Error loading map: {ex.Message}";
                currentMap = null;
                mapPictureBox.Image = null;
            }
        }

        private void FacetComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (suppressFacetChange) return;

            if (hasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved map changes.\nSwitch facets anyway? (Changes will be lost)",
                    "Unsaved Changes",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (result != DialogResult.Yes)
                {
                    // Revert the combo box to the previously loaded facet
                    suppressFacetChange = true;
                    facetComboBox.SelectedIndex = lastLoadedFacetIndex;
                    suppressFacetChange = false;
                    return;
                }
            }
            LoadMap(facetComboBox.SelectedIndex);
        }

        private void LoadButton_Click(object sender, EventArgs e)
        {
            if (hasUnsavedChanges)
            {
                var confirm = MessageBox.Show(
                    "You have unsaved map changes.\nLoad a different folder anyway? (Changes will be lost)",
                    "Unsaved Changes",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes)
                    return;
            }

            using (var folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "Select folder containing map*.mul or map*LegacyMUL.uop files";
                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    mulFolderPath = folderDialog.SelectedPath;
                    mapSourceFolder = mulFolderPath;
                    PopulateCustomMaps(mulFolderPath);
                    LoadMap(facetComboBox.SelectedIndex);
                }
            }
        }

        // ================================================================
        //  New Map creation
        // ================================================================

        private void NewMapButton_Click(object sender, EventArgs e)
        {
            if (hasUnsavedChanges)
            {
                var confirm = MessageBox.Show(
                    "You have unsaved map changes.\nCreate a new map anyway? (Changes will be lost)",
                    "Unsaved Changes",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes)
                    return;
            }

            ShowNewMapDialog();
        }

        /// <summary>
        /// Shows a dialog for the user to configure and create a brand-new empty map.
        /// </summary>
        private void ShowNewMapDialog()
        {
            using (var dlg = new Form())
            {
                dlg.Text = "Create New Map";
                dlg.Width = 380;
                dlg.Height = 340;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                HolographicTheme.ApplyToForm(dlg);

                int y = 15;
                int lblW = 100;
                int ctrlX = 115;
                int ctrlW = 230;

                // --- Map Name ---
                var nameLabel = new Label { Text = "Map Name:", Left = 15, Top = y + 3, Width = lblW, Height = 20 };
                HolographicTheme.ApplyToLabel(nameLabel);
                dlg.Controls.Add(nameLabel);

                var nameTextBox = new TextBox { Text = "New Map", Left = ctrlX, Top = y, Width = ctrlW, Height = 22 };
                HolographicTheme.ApplyToTextBox(nameTextBox);
                dlg.Controls.Add(nameTextBox);
                y += 32;

                // --- Width ---
                var wLabel = new Label { Text = "Width (tiles):", Left = 15, Top = y + 3, Width = lblW, Height = 20 };
                HolographicTheme.ApplyToLabel(wLabel);
                dlg.Controls.Add(wLabel);

                var widthNumeric = new NumericUpDown
                {
                    Left = ctrlX, Top = y, Width = 100, Height = 22,
                    Minimum = 64, Maximum = 16384, Value = 512, Increment = 8
                };
                HolographicTheme.ApplyToNumericUpDown(widthNumeric);
                dlg.Controls.Add(widthNumeric);

                var wNote = new Label { Text = "(multiple of 8)", Left = 220, Top = y + 3, Width = 120, Height = 20, ForeColor = Color.Gray };
                HolographicTheme.ApplyToLabel(wNote);
                dlg.Controls.Add(wNote);
                y += 32;

                // --- Height ---
                var hLabel = new Label { Text = "Height (tiles):", Left = 15, Top = y + 3, Width = lblW, Height = 20 };
                HolographicTheme.ApplyToLabel(hLabel);
                dlg.Controls.Add(hLabel);

                var heightNumeric = new NumericUpDown
                {
                    Left = ctrlX, Top = y, Width = 100, Height = 22,
                    Minimum = 64, Maximum = 16384, Value = 512, Increment = 8
                };
                HolographicTheme.ApplyToNumericUpDown(heightNumeric);
                dlg.Controls.Add(heightNumeric);
                y += 32;

                // --- Default tile ---
                var tileLabel = new Label { Text = "Default Tile:", Left = 15, Top = y + 3, Width = lblW, Height = 20 };
                HolographicTheme.ApplyToLabel(tileLabel);
                dlg.Controls.Add(tileLabel);

                var tileCombo = new ComboBox
                {
                    Left = ctrlX, Top = y, Width = ctrlW, Height = 22,
                    DropDownStyle = ComboBoxStyle.DropDownList
                };
                tileCombo.Items.AddRange(new object[]
                {
                    "Water  (0x00A8)",
                    "Grass  (0x0003)",
                    "Dirt   (0x0031)",
                    "Sand   (0x0016)",
                    "Stone  (0x006A)",
                    "Snow   (0x011A)",
                    "Void   (0x0002)"
                });
                tileCombo.SelectedIndex = 0;
                HolographicTheme.ApplyToComboBox(tileCombo);
                dlg.Controls.Add(tileCombo);
                y += 32;

                // --- Default Z ---
                var zLabel = new Label { Text = "Default Z:", Left = 15, Top = y + 3, Width = lblW, Height = 20 };
                HolographicTheme.ApplyToLabel(zLabel);
                dlg.Controls.Add(zLabel);

                var zNumeric = new NumericUpDown
                {
                    Left = ctrlX, Top = y, Width = 100, Height = 22,
                    Minimum = -128, Maximum = 127, Value = 0
                };
                HolographicTheme.ApplyToNumericUpDown(zNumeric);
                dlg.Controls.Add(zNumeric);
                y += 40;

                // --- Info ---
                var infoLabel = new Label
                {
                    Text = "Creates an empty map filled with the chosen tile.\nDimensions are rounded down to the nearest multiple of 8.",
                    Left = 15, Top = y, Width = 340, Height = 36,
                    ForeColor = Color.Gray, Font = new Font("Segoe UI", 8)
                };
                HolographicTheme.ApplyToLabel(infoLabel);
                dlg.Controls.Add(infoLabel);
                y += 42;

                // --- Buttons ---
                var createBtn = new Button
                {
                    Text = "Create Map",
                    Left = 115, Top = y, Width = 110, Height = 28,
                    DialogResult = DialogResult.OK
                };
                HolographicTheme.ApplyToButton(createBtn, ButtonStyle.Success);
                dlg.Controls.Add(createBtn);

                var cancelBtn = new Button
                {
                    Text = "Cancel",
                    Left = 235, Top = y, Width = 80, Height = 28,
                    DialogResult = DialogResult.Cancel
                };
                HolographicTheme.ApplyToButton(cancelBtn);
                dlg.Controls.Add(cancelBtn);

                dlg.AcceptButton = createBtn;
                dlg.CancelButton = cancelBtn;

                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;

                // --- Parse inputs ---
                int mapW = ((int)widthNumeric.Value / 8) * 8;
                int mapH = ((int)heightNumeric.Value / 8) * 8;
                if (mapW < 8) mapW = 8;
                if (mapH < 8) mapH = 8;

                ushort[] defaultTileIds = { 0x00A8, 0x0003, 0x0031, 0x0016, 0x006A, 0x011A, 0x0002 };
                ushort tileId = defaultTileIds[tileCombo.SelectedIndex];
                sbyte defaultZ = (sbyte)zNumeric.Value;
                string mapName = nameTextBox.Text.Trim();
                if (string.IsNullOrEmpty(mapName)) mapName = "New Map";

                CreateEmptyMap(mapW, mapH, tileId, defaultZ, mapName);
            }
        }

        /// <summary>
        /// Creates a new empty map in memory and sets it as the current map.
        /// No files are written to disk until the user saves.
        /// </summary>
        private void CreateEmptyMap(int width, int height, ushort tileId, sbyte defaultZ, string name)
        {
            var map = new MapData
            {
                Width = width,
                Height = height,
                Tiles = new LandTile[width, height]
            };

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    map.Tiles[x, y] = new LandTile { TileId = tileId, Z = defaultZ };
                }
            }

            // Set as current map
            currentMap = map;

            // Clear all editing state
            staticOverrides.Clear();
            landTileOverrides.Clear();
            hasUnsavedChanges = true; // mark dirty — the new map has never been saved
            undoRedoManager.Clear();
            cachedStaticsData = null;
            cachedArtFormat = null;
            currentStatics = null;
            minimapGenerator.Invalidate();
            minimapGenerating = false;

            // Centre camera on the new map
            cameraX = width / 2;
            cameraY = height / 2;

            GenerateMapImage();
            GenerateMinimapOverlay();
            UpdateSaveButtonState();
            UpdateUndoRedoButtons();

            statusLabel.Text = $"Created new map \"{name}\": {width}x{height} (tile 0x{tileId:X4}, Z={defaultZ}) — unsaved";
        }

        /// <summary>
        /// Scans the MUL folder for map files beyond the standard 6 facets
        /// and adds them to the facet combo box.
        /// </summary>
        private void PopulateCustomMaps(string folder)
        {
            // Remove any previously added custom entries (keep the first 6 standard ones).
            while (facetComboBox.Items.Count > 6)
                facetComboBox.Items.RemoveAt(facetComboBox.Items.Count - 1);

            // Scan for map*.mul and map*LegacyMUL.uop with index >= 6.
            var customIndices = new System.Collections.Generic.SortedSet<int>();
            try
            {
                foreach (var file in Directory.GetFiles(folder))
                {
                    string name = Path.GetFileName(file);
                    int idx = -1;

                    // Match map<N>.mul
                    if (name.StartsWith("map", StringComparison.OrdinalIgnoreCase)
                        && name.EndsWith(".mul", StringComparison.OrdinalIgnoreCase))
                    {
                        string numPart = name.Substring(3, name.Length - 3 - 4); // strip "map" and ".mul"
                        int parsed;
                        if (int.TryParse(numPart, out parsed))
                            idx = parsed;
                    }
                    // Match map<N>LegacyMUL.uop
                    else if (name.StartsWith("map", StringComparison.OrdinalIgnoreCase)
                             && name.EndsWith("LegacyMUL.uop", StringComparison.OrdinalIgnoreCase))
                    {
                        string numPart = name.Substring(3, name.Length - 3 - 14); // strip "map" and "LegacyMUL.uop"
                        int parsed;
                        if (int.TryParse(numPart, out parsed))
                            idx = parsed;
                    }

                    if (idx >= 6)
                        customIndices.Add(idx);
                }
            }
            catch { }

            // Fill gaps: if highest custom index is 10, add entries 6 through 10
            // so combo box index == map index.
            if (customIndices.Count > 0)
            {
                int maxIdx = 5;
                foreach (int ci in customIndices)
                    if (ci > maxIdx) maxIdx = ci;

                for (int i = 6; i <= maxIdx; i++)
                {
                    if (customIndices.Contains(i))
                        facetComboBox.Items.Add($"Custom Map ({i})");
                    else
                        facetComboBox.Items.Add($"Map {i} (not found)");
                }
            }
        }

        private void DiagnoseButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(mulFolderPath))
            {
                MessageBox.Show("No MUL folder configured. Load a map first or set the path in Settings.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            int mapIndex = facetComboBox?.SelectedIndex ?? 0;
            string mapPath = Path.Combine(mulFolderPath, $"map{mapIndex}.mul");

            if (!File.Exists(mapPath))
            {
                MessageBox.Show($"File not found: {mapPath}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string diagnostics = MapDiagnostics.AnalyzeMapFile(mapPath);
            
            var diagForm = new Form
            {
                Text = "Map File Diagnostics",
                Width = 800,
                Height = 600,
                StartPosition = FormStartPosition.CenterParent
            };
            
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Font = new Font("Consolas", 9),
                Text = diagnostics
            };
            
            diagForm.Controls.Add(textBox);
            diagForm.ShowDialog(this);
        }

        internal Image LoadTileImage(int tileId)
        {
            if (artCache.ContainsKey(tileId)) return artCache[tileId];
            
            if (tileId < 0x4000 && !string.IsNullOrEmpty(mulFolderPath))
            {
                try
                {
                    Bitmap landArt = null;

                    // Check which format is available (cached to avoid File.Exists per tile)
                    if (!cachedArtFormat.HasValue)
                        cachedArtFormat = AppConfig.Instance.GetArtFileFormat(mulFolderPath);
                    var format = cachedArtFormat.Value;
                    
                    if (format == ArtFileFormat.MulFiles)
                    {
                        // OSI-style: use LandTileArtReader
                        landArt = LandTileArtReader.LoadLandTile(mulFolderPath, (ushort)tileId);
                    }
                    else if (format == ArtFileFormat.UopOnly)
                    {
                        // UOForever-style: use UopArtReader
                        landArt = UopArtReader.LoadLandTile(mulFolderPath, (ushort)tileId);
                    }
                    else if (format == ArtFileFormat.TecmoExpanded)
                    {
                        // Tecmo Expanded Art: use TecmoArtReader
                        landArt = TecmoArtReader.LoadLandTile(mulFolderPath, tileId);
                    }
                    
                    if (landArt != null)
                    {
                        artCache[tileId] = landArt;
                        return landArt;
                    }
                }
                catch { }
            }
            
            string graphicId = $"0x{tileId:X4}";
            string path;
            
            if (TryResolveIdToPath(graphicId, out path))
            {
                try
                {
                    using (var src = Image.FromFile(path))
                    {
                        var img = new Bitmap(src);
                        artCache[tileId] = img;
                        return img;
                    }
                }
                catch { }
            }
            
            return null;
        }

        /// <summary>
        /// Overload for backward compatibility with ushort tile IDs
        /// </summary>
        internal Image LoadTileImage(ushort tileId)
        {
            return LoadTileImage((int)tileId);
        }
        
        private void LoadArtFiles()
        {
            if (string.IsNullOrEmpty(artFolderPath) || !Directory.Exists(artFolderPath))
                return;

            try
            {
                var artFiles = Directory.GetFiles(artFolderPath, "*.png", SearchOption.AllDirectories);
                
                if (artFiles.Length == 0) return;

                foreach (var file in artFiles)
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    var upper = name.ToUpperInvariant();
                    if (!idToPath.ContainsKey(upper))
                        idToPath[upper] = file;

                    string hex4;
                    if (TryExtractHexId(name, out hex4))
                    {
                        if (!idToPath.ContainsKey(hex4))
                            idToPath[hex4] = file;
                        var pref = $"0x{hex4}";
                        if (!idToPath.ContainsKey(pref))
                            idToPath[pref] = file;
                    }
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text += $" | Art load error: {ex.Message}";
            }
        }

        private bool TryExtractHexId(string text, out string hex4)
        {
            hex4 = null;
            if (string.IsNullOrEmpty(text)) return false;

            var m = System.Text.RegularExpressions.Regex.Matches(text, "0[xX]([0-9A-Fa-f]{4,})|([0-9A-Fa-f]{4,})");
            if (m.Count == 0) return false;

            string digits = null;
            var last = m[m.Count - 1];
            if (last.Groups[1].Success)
                digits = last.Groups[1].Value;
            else if (last.Groups[2].Success)
                digits = last.Groups[2].Value;

            if (string.IsNullOrEmpty(digits) || digits.Length < 4) return false;
            hex4 = digits.Substring(digits.Length - 4).ToUpperInvariant();
            return true;
        }

        private bool TryResolveIdToPath(string id, out string path)
        {
            path = null;
            if (string.IsNullOrWhiteSpace(id)) return false;
            if (idToPath.TryGetValue(id, out path)) return true;

            var withPrefix = id.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? id : $"0x{id}";
            if (idToPath.TryGetValue(withPrefix, out path)) return true;

            var raw = id.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? id.Substring(2) : id;
            raw = raw.ToUpperInvariant();
            if (raw.Length == 3) raw = "0" + raw;
            if (idToPath.TryGetValue(raw, out path)) return true;

            return false;
        }

        /// <summary>
        /// Loads a texture from texmaps.mul for the given land tile ID.
        /// Returns null if no texture is available.
        /// </summary>
        internal Image LoadTexMap(ushort tileId)
        {
            Image cached;
            if (texMapCache.TryGetValue(tileId, out cached))
                return cached;

            if (string.IsNullOrEmpty(mulFolderPath))
                return null;

            if (tileDataReader == null || !tileDataReader.IsLoaded)
                return null;

            var landData = tileDataReader.GetLandTile(tileId);
            if (landData == null || landData.TextureId == 0)
                return null;

            var bmp = TexMapReader.LoadTexture(mulFolderPath, landData.TextureId);
            texMapCache[tileId] = bmp; // cache even if null to avoid retries
            return bmp;
        }

        /// <summary>
        /// Returns true if the land tile should not be drawn at all
        /// (void / "nodraw" tiles that the UO client skips).
        /// Only hard-coded void tile IDs are filtered.  The previous
        /// heuristic (no name + no texture = void) was too aggressive
        /// and incorrectly hid legitimate tiles that simply use the
        /// 44×44 land art instead of a texmap overlay.
        /// </summary>
        internal bool IsNoDraw(ushort tileId)
        {
            // Tile ID 2 is the standard void/cave tile.
            if (tileId == 0x0002)
                return true;

            return false;
        }

        /// <summary>
        /// Returns true if a static item should not be drawn
        /// (nodraw / internal items that the UO client skips).
        /// </summary>
        internal bool IsStaticNoDraw(ushort itemId)
        {
            // Item ID 0 is the standard nodraw static.
            if (itemId == 0x0000)
                return true;

            if (tileDataReader != null && tileDataReader.IsLoaded)
            {
                var itemData = tileDataReader.GetItemTile(itemId);
                if (itemData != null)
                {
                    // Statics flagged as Internal are hidden by the client.
                    if (itemData.Flags.HasFlag(TileFlag.Internal))
                        return true;

                    // A static with no name and zero height is typically nodraw.
                    if (string.IsNullOrEmpty(itemData.Name) && itemData.Height == 0
                        && itemData.Flags == TileFlag.None)
                        return true;
                }
            }

            return false;
        }

        private const int STATS_ROW_HEIGHT = 48;
        private const int STATS_IMG_SIZE = 44;

        /// <summary>
        /// Count how many times each static ItemId is placed on the current map
        /// and display the results in a sortable list with artwork previews.
        /// </summary>
        private void StaticStatsButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(mapSourceFolder) && string.IsNullOrEmpty(mulFolderPath))
            {
                MessageBox.Show("No map loaded. Load a map first.", "Tile Statistics",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string folder = mapSourceFolder ?? mulFolderPath;
            int mapIndex = facetComboBox?.SelectedIndex ?? 0;

            // Build the stats dialog with a Statics/Land Tiles toggle
            ShowTileStatsDialog(folder, mapIndex, "Statics");
        }

        /// <summary>
        /// Shows the tile statistics dialog with a toggle between Statics and Land Tiles.
        /// </summary>
        private void ShowTileStatsDialog(string folder, int mapIndex, string initialMode)
        {
            string artFolder = folder;

            // Local art cache shared across mode switches
            var statsArtCache = new Dictionary<ushort, Image>();

            // Build form
            var statsForm = new Form
            {
                Text = $"Tile Statistics — Map {mapIndex}",
                Width = 820,
                Height = 650,
                StartPosition = FormStartPosition.CenterParent
            };
            HolographicTheme.ApplyToForm(statsForm);

            // Mode toggle panel (top)
            var togglePanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                Padding = new Padding(8, 4, 8, 4)
            };
            HolographicTheme.ApplyToPanel(togglePanel);

            var radioStatics = new RadioButton
            {
                Text = "Statics",
                Left = 8,
                Top = 8,
                Width = 80,
                Height = 20,
                Checked = initialMode == "Statics",
                AutoSize = false
            };
            HolographicTheme.ApplyToRadioButton(radioStatics);

            var radioLandTiles = new RadioButton
            {
                Text = "Land Tiles",
                Left = 96,
                Top = 8,
                Width = 100,
                Height = 20,
                Checked = initialMode == "Land Tiles",
                AutoSize = false
            };
            HolographicTheme.ApplyToRadioButton(radioLandTiles);

            var radioPatterns2x2 = new RadioButton
            {
                Text = "Patterns (2x2)",
                Left = 204,
                Top = 8,
                Width = 110,
                Height = 20,
                Checked = initialMode == "Patterns2x2",
                AutoSize = false
            };
            HolographicTheme.ApplyToRadioButton(radioPatterns2x2);

            var radioPatterns3x3 = new RadioButton
            {
                Text = "Patterns (3x3)",
                Left = 322,
                Top = 8,
                Width = 110,
                Height = 20,
                Checked = initialMode == "Patterns" || initialMode == "Patterns3x3",
                AutoSize = false
            };
            HolographicTheme.ApplyToRadioButton(radioPatterns3x3);

            togglePanel.Controls.Add(radioStatics);
            togglePanel.Controls.Add(radioLandTiles);
            togglePanel.Controls.Add(radioPatterns2x2);
            togglePanel.Controls.Add(radioPatterns3x3);

            // Summary label
            var summaryLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0)
            };
            HolographicTheme.ApplyToLabel(summaryLabel);

            // Filter text box
            var filterTextBox = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 25,
                Text = "Filter by name, ID, or hex...",
                ForeColor = Color.Gray,
                Font = new Font("Consolas", 10)
            };
            HolographicTheme.ApplyToTextBox(filterTextBox);
            filterTextBox.Enter += (s, ev) =>
            {
                if (filterTextBox.Text == "Filter by name, ID, or hex...")
                {
                    filterTextBox.Text = "";
                    filterTextBox.ForeColor = HolographicTheme.TextPrimary;
                }
            };
            filterTextBox.Leave += (s, ev) =>
            {
                if (string.IsNullOrWhiteSpace(filterTextBox.Text))
                {
                    filterTextBox.Text = "Filter by name, ID, or hex...";
                    filterTextBox.ForeColor = Color.Gray;
                }
            };

            // Dummy ImageList to force row height
            var rowSpacer = new ImageList { ImageSize = new Size(1, STATS_ROW_HEIGHT) };

            // Owner-drawn ListView
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = true,
                OwnerDraw = true,
                Font = new Font("Consolas", 9.5f),
                BackColor = HolographicTheme.CanvasBackground,
                ForeColor = HolographicTheme.TextPrimary,
                SmallImageList = rowSpacer
            };

            // Set columns based on mode
            void SetColumnsForMode()
            {
                listView.Columns.Clear();
                bool is2x2 = radioPatterns2x2.Checked;
                bool is3x3 = radioPatterns3x3.Checked;
                if (is2x2 || is3x3)
                {
                    int kernelSize = is2x2 ? 2 : 3;
                    // Larger row height for pattern previews
                    listView.SmallImageList = new ImageList { ImageSize = new Size(1, 80) };
                    listView.Columns.Add("Preview", 140, HorizontalAlignment.Center);
                    listView.Columns.Add("Count", 90, HorizontalAlignment.Right);
                    listView.Columns.Add($"Pattern Hex IDs ({kernelSize}x{kernelSize})", 340, HorizontalAlignment.Left);
                    listView.Columns.Add("Center Tile", 160, HorizontalAlignment.Left);
                    listView.Columns.Add("% of Total", 90, HorizontalAlignment.Right);
                }
                else
                {
                    listView.SmallImageList = rowSpacer;
                    listView.Columns.Add("Preview", STATS_IMG_SIZE + 8, HorizontalAlignment.Center);
                    listView.Columns.Add("ID", 70, HorizontalAlignment.Left);
                    listView.Columns.Add("Hex", 70, HorizontalAlignment.Left);
                    listView.Columns.Add("Name", 260, HorizontalAlignment.Left);
                    listView.Columns.Add("Count", 100, HorizontalAlignment.Right);
                    listView.Columns.Add("% of Total", 90, HorizontalAlignment.Right);
                }
            }

            SetColumnsForMode();

            // Master list of all items (kept for filtering)
            var allItems = new List<ListViewItem>();

            // Pattern preview caches
            var pattern2x2PreviewCache = new Dictionary<MapReader.Pattern2x2, Image>();
            var pattern3x3PreviewCache = new Dictionary<MapReader.Pattern3x3, Image>();

            // Function to populate the list based on current mode
            void PopulateList()
            {
                SetColumnsForMode();

                bool showStatics = radioStatics.Checked;
                bool showLandTiles = radioLandTiles.Checked;
                bool showPatterns2x2 = radioPatterns2x2.Checked;
                bool showPatterns3x3 = radioPatterns3x3.Checked;
                bool showAnyPatterns = showPatterns2x2 || showPatterns3x3;
                statusLabel.Text = showStatics ? "Counting statics..." : showLandTiles ? "Counting land tiles..." : showPatterns2x2 ? "Scanning 2x2 patterns..." : "Scanning 3x3 patterns...";
                Application.DoEvents();

                int usedCount = 0, unusedCount = 0;
                long totalPlacements = 0;
                string modeLabel = "";
                var sorted = new List<KeyValuePair<ushort, int>>();
                var patternSorted = new List<KeyValuePair<MapReader.Pattern3x3, int>>();

                if (showStatics)
                {
                    var counts = StaticsReader.CountStaticFrequencies(folder, mapIndex);

                    // Gather ALL available static item IDs
                    var allStaticIds = new HashSet<int>();
                    
                    // Check for Tecmo format
                    var artFormat = AppConfig.Instance.GetArtFileFormat(artFolder);
                    
                    if (artFormat == ArtFileFormat.TecmoExpanded)
                    {
                        var tecmoIds = TecmoArtReader.GetValidStaticItemIds(artFolder);
                        foreach (var id in tecmoIds) allStaticIds.Add(id);
                    }
                    else
                    {
                        var mulIds = StaticArtReader.GetValidStaticItemIds(artFolder);
                        foreach (var id in mulIds) allStaticIds.Add(id);
                        if (UopArtReader.UopFileExists(artFolder))
                        {
                            var uopIds = UopArtReader.GetValidStaticItemIds(artFolder);
                            foreach (var id in uopIds) allStaticIds.Add(id);
                        }
                    }
                    
                    foreach (var id in allStaticIds)
                        if (id <= 0xFFFF && !counts.ContainsKey((ushort)id)) counts[(ushort)id] = 0;

                    usedCount = counts.Count(kv => kv.Value > 0);
                    unusedCount = counts.Count(kv => kv.Value == 0);
                    totalPlacements = 0;
                    foreach (var kv in counts) totalPlacements += kv.Value;
                    modeLabel = "Statics";

                    statsForm.Text = $"Tile Statistics — Map {mapIndex} — Statics ({usedCount} used, {unusedCount} unused, {counts.Count:N0} total)";
                    summaryLabel.Text = $"Total placements: {totalPlacements:N0}  |  Used: {usedCount:N0}  |  Unused: {unusedCount:N0}";

                    var usedItems = new List<KeyValuePair<ushort, int>>();
                    var unusedItems = new List<KeyValuePair<ushort, int>>();
                    foreach (var kv in counts)
                    {
                        if (kv.Value > 0) usedItems.Add(kv);
                        else unusedItems.Add(kv);
                    }
                    usedItems.Sort((a, b) => b.Value.CompareTo(a.Value));
                    unusedItems.Sort((a, b) => a.Key.CompareTo(b.Key));
                    sorted = new List<KeyValuePair<ushort, int>>(usedItems);
                    sorted.AddRange(unusedItems);
                }
                else if (showLandTiles)
                {
                    // Land tiles — count from the already-loaded map
                    if (currentMap == null)
                    {
                        statusLabel.Text = "No map loaded.";
                        return;
                    }
                    var counts = MapReader.CountLandTileFrequencies(currentMap);

                    // Gather ALL available land tile IDs
                    var allLandIds = new HashSet<ushort>();
                    var landMulIds = StaticArtReader.GetValidLandTileIds(artFolder);
                    foreach (var id in landMulIds) allLandIds.Add(id);
                    if (UopArtReader.UopFileExists(artFolder))
                    {
                        var landUopIds = UopArtReader.GetValidLandTileIds(artFolder);
                        foreach (var id in landUopIds) allLandIds.Add(id);
                    }
                    foreach (var id in allLandIds)
                        if (!counts.ContainsKey(id)) counts[id] = 0;

                    usedCount = counts.Count(kv => kv.Value > 0);
                    unusedCount = counts.Count(kv => kv.Value == 0);
                    totalPlacements = 0;
                    foreach (var kv in counts) totalPlacements += kv.Value;
                    modeLabel = "Land Tiles";

                    statsForm.Text = $"Tile Statistics — Map {mapIndex} — Land Tiles ({usedCount} used, {unusedCount} unused, {counts.Count:N0} total)";
                    summaryLabel.Text = $"Total tiles: {totalPlacements:N0}  |  Used: {usedCount:N0}  |  Unused: {unusedCount:N0}";

                    var usedItems = new List<KeyValuePair<ushort, int>>();
                    var unusedItems = new List<KeyValuePair<ushort, int>>();
                    foreach (var kv in counts)
                    {
                        if (kv.Value > 0) usedItems.Add(kv);
                        else unusedItems.Add(kv);
                    }
                    usedItems.Sort((a, b) => b.Value.CompareTo(a.Value));
                    unusedItems.Sort((a, b) => a.Key.CompareTo(b.Key));
                    sorted = new List<KeyValuePair<ushort, int>>(usedItems);
                    sorted.AddRange(unusedItems);
                }
                else if (showPatterns2x2)
                {
                    if (currentMap == null)
                    {
                        statusLabel.Text = "No map loaded.";
                        return;
                    }

                    statusLabel.Text = "Scanning 2x2 land tile patterns...";
                    Application.DoEvents();

                    var patternCounts = new Dictionary<MapReader.Pattern2x2, int>();
                    try
                    {
                        patternCounts = MapReader.CountLandTilePatterns2x2(currentMap);
                    }
                    catch (OutOfMemoryException)
                    {
                        statusLabel.Text = "Out of memory — map too large for full pattern scan.";
                        return;
                    }

                    totalPlacements = 0;
                    foreach (var kv in patternCounts) totalPlacements += kv.Value;
                    usedCount = patternCounts.Count;
                    modeLabel = "2x2 Patterns";

                    statsForm.Text = $"Tile Statistics — Map {mapIndex} — 2x2 Patterns ({patternCounts.Count:N0} unique, {totalPlacements:N0} total)";
                    summaryLabel.Text = $"Total 2x2 windows: {totalPlacements:N0}  |  Unique patterns: {patternCounts.Count:N0}";

                    var pattern2x2Sorted = new List<KeyValuePair<MapReader.Pattern2x2, int>>(patternCounts);
                    pattern2x2Sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
                    statusLabel.Text = $"Ready | {modeLabel}: {usedCount:N0} unique, {totalPlacements:N0} total windows";

                    allItems.Clear();
                    foreach (var kv in pattern2x2Sorted)
                    {
                        double pct = totalPlacements > 0 ? (kv.Value * 100.0 / totalPlacements) : 0;
                        var p = kv.Key;
                        string patternHex = $"{p.TL:X4},{p.TR:X4},{p.BL:X4},{p.BR:X4}";

                        // Resolve the top-left tile name
                        string centerName = "";
                        if (tileDataReader != null && tileDataReader.IsLoaded)
                        {
                            var td = tileDataReader.GetLandTile(p.TL);
                            if (td != null && !string.IsNullOrWhiteSpace(td.Name))
                                centerName = td.Name;
                        }
                        string nameStr = string.IsNullOrEmpty(centerName)
                            ? $"TL: 0x{p.TL:X4}"
                            : $"TL: {centerName}";

                        var item = new ListViewItem(new[]
                        {
                            "",
                            kv.Value.ToString("N0"),
                            patternHex,
                            nameStr,
                            pct.ToString("0.00") + "%"
                        });
                        item.Tag = kv.Key;
                        item.UseItemStyleForSubItems = false;
                        allItems.Add(item);
                    }
                }
                else if (showPatterns3x3)
                {
                    if (currentMap == null)
                    {
                        statusLabel.Text = "No map loaded.";
                        return;
                    }

                    statusLabel.Text = "Scanning 3x3 land tile patterns (this may take a moment)...";
                    Application.DoEvents();

                    var patternCounts = new Dictionary<MapReader.Pattern3x3, int>();
                    try
                    {
                        patternCounts = MapReader.CountLandTilePatterns3x3(currentMap);
                    }
                    catch (OutOfMemoryException)
                    {
                        statusLabel.Text = "Out of memory — map too large for full pattern scan.";
                        return;
                    }

                    totalPlacements = 0;
                    foreach (var kv in patternCounts) totalPlacements += kv.Value;
                    usedCount = patternCounts.Count;
                    modeLabel = "3x3 Patterns";

                    statsForm.Text = $"Tile Statistics — Map {mapIndex} — 3x3 Patterns ({patternCounts.Count:N0} unique, {totalPlacements:N0} total)";
                    summaryLabel.Text = $"Total 3x3 windows: {totalPlacements:N0}  |  Unique patterns: {patternCounts.Count:N0}";

                    var pattern3x3Sorted = new List<KeyValuePair<MapReader.Pattern3x3, int>>(patternCounts);
                    pattern3x3Sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
                    statusLabel.Text = $"Ready | {modeLabel}: {usedCount:N0} unique, {totalPlacements:N0} total windows";

                    allItems.Clear();
                    foreach (var kv in pattern3x3Sorted)
                    {
                        double pct = totalPlacements > 0 ? (kv.Value * 100.0 / totalPlacements) : 0;
                        var p = kv.Key;
                        string patternHex = $"{p.TL:X4},{p.TM:X4},{p.TR:X4},{p.ML:X4},{p.MM:X4},{p.MR:X4},{p.BL:X4},{p.BM:X4},{p.BR:X4}";

                        string centerName = "";
                        if (tileDataReader != null && tileDataReader.IsLoaded)
                        {
                            var td = tileDataReader.GetLandTile(p.MM);
                            if (td != null && !string.IsNullOrWhiteSpace(td.Name))
                                centerName = td.Name;
                        }
                        string nameStr = string.IsNullOrEmpty(centerName)
                            ? $"Center: 0x{p.MM:X4}"
                            : $"Center: {centerName}";

                        var item = new ListViewItem(new[]
                        {
                            "",
                            kv.Value.ToString("N0"),
                            patternHex,
                            nameStr,
                            pct.ToString("0.00") + "%"
                        });
                        item.Tag = kv.Key;
                        item.UseItemStyleForSubItems = false;
                        allItems.Add(item);
                    }
                }
                if (showStatics || showLandTiles)
                {
                    allItems.Clear();
                    foreach (var kv in sorted)
                    {
                        string name = "";
                        if (tileDataReader != null && tileDataReader.IsLoaded)
                        {
                            if (showStatics)
                            {
                                var td = tileDataReader.GetItemTile(kv.Key);
                                if (td != null && !string.IsNullOrWhiteSpace(td.Name))
                                    name = td.Name;
                            }
                            else
                            {
                                var td = tileDataReader.GetLandTile(kv.Key);
                                if (td != null && !string.IsNullOrWhiteSpace(td.Name))
                                    name = td.Name;
                            }
                        }

                        bool isUnused = kv.Value == 0;
                        double pct = totalPlacements > 0 ? (kv.Value * 100.0 / totalPlacements) : 0;
                        var item = new ListViewItem(new[]
                        {
                            "",                              // Art column
                            kv.Key.ToString(),               // ID
                            $"0x{kv.Key:X4}",                // Hex
                            isUnused ? $"(unused) {name}" : name,
                            isUnused ? "—" : kv.Value.ToString("N0"),
                            pct.ToString("0.00") + "%"
                        });
                        item.Tag = kv.Key;
                        item.UseItemStyleForSubItems = false;
                        allItems.Add(item);
                    }
                    statusLabel.Text = $"Ready | {modeLabel}: {usedCount:N0} used, {totalPlacements:N0} total placements";
                }

                // Apply current filter
                ApplyFilter(listView, allItems, filterTextBox.Text);
            }

            // Art loading helper
            Image LoadArtForId(int id, bool isStatics)
            {
                if (isStatics)
                    return LoadStaticImage(id);
                return LoadTileImage(id);
            }

            // Render a pattern preview by stitching tile images at native 44x44 size.
            // UO land tiles are 44x44 diamonds that overlap when adjacent.
            Image RenderPatternPreview2x2(MapReader.Pattern2x2 pattern)
            {
                if (pattern2x2PreviewCache.TryGetValue(pattern, out var cached))
                    return cached;

                // Render 2x2 pattern as a diamond using UO isometric projection.
                // ScreenX = (col - row) * tileHalf, ScreenY = (col + row) * tileHalf
                const int tileSize = 44;
                const int tileHalf = 22;
                const int cols = 2, rows = 2;

                // Calculate bounding box for isometric diamond arrangement
                // (0,0): X=0,Y=0  (1,0): X=22,Y=22  (0,1): X=-22,Y=22  (1,1): X=0,Y=44
                // Min X=-22, Max X=22, Min Y=0, Max Y=44
                // Canvas needs to fit 44px tiles at these positions
                const int offsetX = tileHalf; // +22 to make all X positive
                const int offsetY = 0;
                const int canvasW = (cols * tileSize); // 88
                const int canvasH = (rows * tileSize); // 88

                var bmp = new Bitmap(canvasW, canvasH);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(30, 30, 40));
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                    var ids = pattern.ToArray();
                    for (int row = 0; row < rows; row++)
                    {
                        for (int col = 0; col < cols; col++)
                        {
                            int idx = row * cols + col;
                            // Isometric projection
                            int drawX = ((col - row) * tileHalf) + offsetX + (canvasW / 2) - tileHalf;
                            int drawY = ((col + row) * tileHalf) + offsetY;
                            var tileImg = LoadTileImage(ids[idx]);
                            if (tileImg != null)
                                g.DrawImage(tileImg, drawX, drawY, tileSize, tileSize);
                            else
                            {
                                using (var brush = new SolidBrush(Color.FromArgb(50, 50, 60)))
                                {
                                    PointF[] diamond = new PointF[]
                                    {
                                        new PointF(drawX + tileHalf, drawY),
                                        new PointF(drawX + tileSize, drawY + tileHalf),
                                        new PointF(drawX + tileHalf, drawY + tileSize),
                                        new PointF(drawX, drawY + tileHalf)
                                    };
                                    g.FillPolygon(brush, diamond);
                                }
                                using (var pen = new Pen(Color.FromArgb(80, 80, 100)))
                                {
                                    PointF[] diamond = new PointF[]
                                    {
                                        new PointF(drawX + tileHalf, drawY),
                                        new PointF(drawX + tileSize, drawY + tileHalf),
                                        new PointF(drawX + tileHalf, drawY + tileSize),
                                        new PointF(drawX, drawY + tileHalf)
                                    };
                                    g.DrawPolygon(pen, diamond);
                                }
                            }
                        }
                    }
                }
                pattern2x2PreviewCache[pattern] = bmp;
                return bmp;
            }

            Image RenderPatternPreview3x3(MapReader.Pattern3x3 pattern)
            {
                if (pattern3x3PreviewCache.TryGetValue(pattern, out var cached))
                    return cached;

                // Render 3x3 pattern as a single diamond using UO isometric projection.
                // ScreenX = (col - row) * tileHalf, ScreenY = (col + row) * tileHalf
                // This arranges the 9 tiles into one large diamond shape.
                const int tileSize = 44;
                const int tileHalf = 22;
                const int cols = 3, rows = 3;

                // Isometric positions for 3x3:
                // (0,0): X=0,Y=0    (1,0): X=22,Y=22  (2,0): X=44,Y=44
                // (0,1): X=-22,Y=22 (1,1): X=0,Y=44   (2,1): X=22,Y=66
                // (0,2): X=-44,Y=44 (1,2): X=-22,Y=66 (2,2): X=0,Y=88
                // Min X=-44, Max X=44, Min Y=0, Max Y=88
                const int canvasW = 132; // 88 range + 44 tile width
                const int canvasH = 132; // 88 range + 44 tile height

                var bmp = new Bitmap(canvasW, canvasH);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(30, 30, 40));
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                    var ids = pattern.ToArray();
                    for (int row = 0; row < rows; row++)
                    {
                        for (int col = 0; col < cols; col++)
                        {
                            int idx = row * cols + col;
                            // Isometric projection, centered in canvas
                            int drawX = ((col - row) * tileHalf) + (canvasW / 2) - tileHalf;
                            int drawY = ((col + row) * tileHalf);
                            var tileImg = LoadTileImage(ids[idx]);
                            if (tileImg != null)
                                g.DrawImage(tileImg, drawX, drawY, tileSize, tileSize);
                            else
                            {
                                using (var brush = new SolidBrush(Color.FromArgb(50, 50, 60)))
                                {
                                    PointF[] diamond = new PointF[]
                                    {
                                        new PointF(drawX + tileHalf, drawY),
                                        new PointF(drawX + tileSize, drawY + tileHalf),
                                        new PointF(drawX + tileHalf, drawY + tileSize),
                                        new PointF(drawX, drawY + tileHalf)
                                    };
                                    g.FillPolygon(brush, diamond);
                                }
                                using (var pen = new Pen(Color.FromArgb(80, 80, 100)))
                                {
                                    PointF[] diamond = new PointF[]
                                    {
                                        new PointF(drawX + tileHalf, drawY),
                                        new PointF(drawX + tileSize, drawY + tileHalf),
                                        new PointF(drawX + tileHalf, drawY + tileSize),
                                        new PointF(drawX, drawY + tileHalf)
                                    };
                                    g.DrawPolygon(pen, diamond);
                                }
                            }
                        }
                    }
                }
                pattern3x3PreviewCache[pattern] = bmp;
                return bmp;
            }

            listView.BeginUpdate();
            PopulateList();
            listView.EndUpdate();

            const int MaxDisplayItems = 100000;

            // Wire up filter
            void ApplyFilter(ListView lv, List<ListViewItem> master, string filterText)
            {
                string ft = filterText?.Trim() ?? "";
                lv.BeginUpdate();
                lv.Items.Clear();
                if (ft == "Filter by name, ID, or hex..." || ft.Length == 0)
                {
                    int count = 0;
                    foreach (var item in master)
                    {
                        if (count >= MaxDisplayItems) break;
                        lv.Items.Add(item);
                        count++;
                    }
                }
                else
                {
                    string upper = ft.ToUpperInvariant();
                    int count = 0;
                    foreach (var item in master)
                    {
                        if (count >= MaxDisplayItems) break;
                        if (item.SubItems[3].Text.ToUpperInvariant().Contains(upper)
                            || item.SubItems[1].Text.Contains(upper)
                            || item.SubItems[2].Text.ToUpperInvariant().Contains(upper))
                        {
                            lv.Items.Add(item);
                            count++;
                        }
                    }
                }
                if (master.Count > MaxDisplayItems)
                    statusLabel.Text += $"  (showing first {lv.Items.Count:N0} of {master.Count:N0} — CSV export includes all)";
                lv.EndUpdate();
            }

            filterTextBox.TextChanged += (s, ev) => ApplyFilter(listView, allItems, filterTextBox.Text);

            // Export Pattern Hex IDs button (declared early for use in radio handlers)
            var exportPatternButton = new Button
            {
                Text = "Export Pattern Hex IDs",
                Left = 554,
                Top = 3,
                Width = 150,
                Height = 26,
                Visible = radioPatterns2x2.Checked || radioPatterns3x3.Checked
            };
            HolographicTheme.ApplyToButton(exportPatternButton, ButtonStyle.Accent);
            exportPatternButton.Click += (s, ev) =>
            {
                var itemsToExport = listView.SelectedItems.Count > 0
                    ? listView.SelectedItems.Cast<ListViewItem>().ToList()
                    : allItems;

                if (itemsToExport.Count == 0)
                {
                    MessageBox.Show("No patterns to export.", "Export",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                bool is2x2 = radioPatterns2x2.Checked;
                string kernelLabel = is2x2 ? "2x2" : "3x3";

                using (var sfd = new SaveFileDialog())
                {
                    sfd.Filter = "Text File|*.txt|CSV File|*.csv";
                    sfd.FileName = $"land_patterns_{kernelLabel}_map{mapIndex}.txt";
                    if (sfd.ShowDialog(statsForm) != DialogResult.OK)
                        return;

                    using (var sw = new StreamWriter(sfd.FileName))
                    {
                        foreach (var lvi in itemsToExport)
                        {
                            if (lvi.Tag is MapReader.Pattern2x2 p2)
                            {
                                string hexLine = $"{p2.TL:X4},{p2.TR:X4},{p2.BL:X4},{p2.BR:X4}";
                                sw.WriteLine(hexLine);
                            }
                            else if (lvi.Tag is MapReader.Pattern3x3 p3)
                            {
                                string hexLine = $"{p3.TL:X4},{p3.TM:X4},{p3.TR:X4},{p3.ML:X4},{p3.MM:X4},{p3.MR:X4},{p3.BL:X4},{p3.BM:X4},{p3.BR:X4}";
                                sw.WriteLine(hexLine);
                            }
                        }
                    }
                    MessageBox.Show($"Exported {itemsToExport.Count} {kernelLabel} pattern(s) to:\n{sfd.FileName}", "Export Pattern Hex IDs",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };

            // Mode toggle handlers
            radioStatics.CheckedChanged += (s, ev) =>
            {
                if (radioStatics.Checked)
                {
                    statsArtCache.Clear();
                    pattern2x2PreviewCache.Clear();
                    pattern3x3PreviewCache.Clear();
                    exportPatternButton.Visible = false;
                    listView.BeginUpdate();
                    PopulateList();
                    listView.EndUpdate();
                }
            };
            radioLandTiles.CheckedChanged += (s, ev) =>
            {
                if (radioLandTiles.Checked)
                {
                    statsArtCache.Clear();
                    pattern2x2PreviewCache.Clear();
                    pattern3x3PreviewCache.Clear();
                    exportPatternButton.Visible = false;
                    listView.BeginUpdate();
                    PopulateList();
                    listView.EndUpdate();
                }
            };
            radioPatterns2x2.CheckedChanged += (s, ev) =>
            {
                if (radioPatterns2x2.Checked)
                {
                    statsArtCache.Clear();
                    pattern2x2PreviewCache.Clear();
                    pattern3x3PreviewCache.Clear();
                    exportPatternButton.Visible = true;
                    exportPatternButton.Text = "Export Pattern Hex IDs";
                    listView.BeginUpdate();
                    PopulateList();
                    listView.EndUpdate();
                }
            };
            radioPatterns3x3.CheckedChanged += (s, ev) =>
            {
                if (radioPatterns3x3.Checked)
                {
                    statsArtCache.Clear();
                    pattern2x2PreviewCache.Clear();
                    pattern3x3PreviewCache.Clear();
                    exportPatternButton.Visible = true;
                    exportPatternButton.Text = "Export Pattern Hex IDs";
                    listView.BeginUpdate();
                    PopulateList();
                    listView.EndUpdate();
                }
            };

            // Owner-draw handlers
            listView.DrawColumnHeader += (s, ev) =>
            {
                using (var bg = new SolidBrush(HolographicTheme.PanelBackground))
                    ev.Graphics.FillRectangle(bg, ev.Bounds);
                using (var fg = new SolidBrush(HolographicTheme.TextPrimary))
                {
                    var sf = new StringFormat { LineAlignment = StringAlignment.Center };
                    if (ev.Header.TextAlign == HorizontalAlignment.Right)
                        sf.Alignment = StringAlignment.Far;
                    else if (ev.Header.TextAlign == HorizontalAlignment.Center)
                        sf.Alignment = StringAlignment.Center;
                    else
                        sf.Alignment = StringAlignment.Near;
                    ev.Graphics.DrawString(ev.Header.Text, listView.Font, fg, ev.Bounds, sf);
                }
            };

            listView.DrawItem += (s, ev) =>
            {
                Color rowBg = ev.Item.Selected ? HolographicTheme.ButtonAccent : HolographicTheme.CanvasBackground;
                using (var brush = new SolidBrush(rowBg))
                    ev.Graphics.FillRectangle(brush, ev.Bounds);
            };

            listView.DrawSubItem += (s, ev) =>
            {
                Color rowBg = ev.Item.Selected ? HolographicTheme.ButtonAccent : HolographicTheme.CanvasBackground;
                using (var bgBrush = new SolidBrush(rowBg))
                    ev.Graphics.FillRectangle(bgBrush, ev.Bounds);

                if (ev.ColumnIndex == 0)
                {
                    // Draw artwork thumbnail
                    if (ev.Item.Tag is ushort artId)
                    {
                        bool isStatics = radioStatics.Checked;
                        Image img;
                        if (!statsArtCache.TryGetValue(artId, out img))
                        {
                            img = LoadArtForId(artId, isStatics);
                            statsArtCache[artId] = img;
                        }
                        if (img != null)
                        {
                            int drawW = img.Width, drawH = img.Height;
                            if (drawW > STATS_IMG_SIZE || drawH > STATS_IMG_SIZE)
                            {
                                float scale = Math.Min((float)STATS_IMG_SIZE / drawW, (float)STATS_IMG_SIZE / drawH);
                                drawW = (int)(drawW * scale);
                                drawH = (int)(drawH * scale);
                            }
                            int imgX = ev.Bounds.Left + (ev.Bounds.Width - drawW) / 2;
                            int imgY = ev.Bounds.Top + (ev.Bounds.Height - drawH) / 2;
                            try { ev.Graphics.DrawImage(img, imgX, imgY, drawW, drawH); } catch { }
                        }
                    }
                    else if (ev.Item.Tag is MapReader.Pattern2x2 p2x2)
                    {
                        // Draw 2x2 pattern preview, scaled to fit row height
                        var img = RenderPatternPreview2x2(p2x2);
                        if (img != null)
                        {
                            int maxH = ev.Bounds.Height - 4;
                            int maxW = ev.Bounds.Width - 4;
                            float scale = Math.Min((float)maxW / img.Width, (float)maxH / img.Height);
                            int drawW = (int)(img.Width * scale);
                            int drawH = (int)(img.Height * scale);
                            int imgX = ev.Bounds.Left + (ev.Bounds.Width - drawW) / 2;
                            int imgY = ev.Bounds.Top + (ev.Bounds.Height - drawH) / 2;
                            try { ev.Graphics.DrawImage(img, imgX, imgY, drawW, drawH); } catch { }
                        }
                    }
                    else if (ev.Item.Tag is MapReader.Pattern3x3 p3x3)
                    {
                        // Draw 3x3 pattern preview, scaled to fit row height
                        var img = RenderPatternPreview3x3(p3x3);
                        if (img != null)
                        {
                            int maxH = ev.Bounds.Height - 4;
                            int maxW = ev.Bounds.Width - 4;
                            float scale = Math.Min((float)maxW / img.Width, (float)maxH / img.Height);
                            int drawW = (int)(img.Width * scale);
                            int drawH = (int)(img.Height * scale);
                            int imgX = ev.Bounds.Left + (ev.Bounds.Width - drawW) / 2;
                            int imgY = ev.Bounds.Top + (ev.Bounds.Height - drawH) / 2;
                            try { ev.Graphics.DrawImage(img, imgX, imgY, drawW, drawH); } catch { }
                        }
                    }
                }
                else
                {
                    var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
                    if (ev.Header.TextAlign == HorizontalAlignment.Right)
                        sf.Alignment = StringAlignment.Far;
                    else
                        sf.Alignment = StringAlignment.Near;

                    bool isUnused = ev.SubItem.Text == "—" || ev.SubItem.Text.StartsWith("(unused)");
                    Color textColor = isUnused ? Color.FromArgb(100, 100, 100) : HolographicTheme.TextPrimary;

                    var textRect = new Rectangle(ev.Bounds.Left + 4, ev.Bounds.Top, ev.Bounds.Width - 8, ev.Bounds.Height);
                    using (var textBrush = new SolidBrush(textColor))
                        ev.Graphics.DrawString(ev.SubItem.Text, listView.Font, textBrush, textRect, sf);
                }
            };

            // Column click sorting
            int lastSortColumn = -1;
            bool sortAscending = true;
            listView.ColumnClick += (s, ev) =>
            {
                if (ev.Column == 0) return;
                if (ev.Column == lastSortColumn)
                    sortAscending = !sortAscending;
                else
                {
                    lastSortColumn = ev.Column;
                    sortAscending = true;
                }
                listView.ListViewItemSorter = new StaticStatsComparer(ev.Column, sortAscending);
                listView.Sort();
            };

            // Bottom export panel
            var exportPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 34,
                Padding = new Padding(4, 2, 4, 2)
            };
            HolographicTheme.ApplyToPanel(exportPanel);

            var selLabel = new Label
            {
                Text = "0 selected",
                Left = 4,
                Top = 7,
                Width = 120,
                Height = 20,
                ForeColor = HolographicTheme.TextSecondary
            };
            HolographicTheme.ApplyToLabel(selLabel);
            exportPanel.Controls.Add(selLabel);

            listView.SelectedIndexChanged += (s, ev) =>
            {
                int sel = listView.SelectedItems.Count;
                selLabel.Text = sel > 0 ? $"{sel} selected" : "0 selected";
            };

            var exportAllCsvButton = new Button
            {
                Text = "Export All CSV",
                Left = 130,
                Top = 3,
                Width = 110,
                Height = 26
            };
            HolographicTheme.ApplyToButton(exportAllCsvButton, ButtonStyle.Success);
            exportAllCsvButton.Click += (s, ev) =>
            {
                ExportStatsCsv(statsForm, allItems, mapIndex, "all");
            };
            exportPanel.Controls.Add(exportAllCsvButton);

            var exportSelCsvButton = new Button
            {
                Text = "Export Selected CSV",
                Left = 248,
                Top = 3,
                Width = 140,
                Height = 26
            };
            HolographicTheme.ApplyToButton(exportSelCsvButton, ButtonStyle.Accent);
            exportSelCsvButton.Click += (s, ev) =>
            {
                if (listView.SelectedItems.Count == 0)
                {
                    MessageBox.Show("Select one or more rows first.", "Export",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                ExportStatsCsv(statsForm, listView.SelectedItems.Cast<ListViewItem>().ToList(), mapIndex, "selected");
            };
            exportPanel.Controls.Add(exportSelCsvButton);

            var exportPngButton = new Button
            {
                Text = "Export Selected PNGs",
                Left = 396,
                Top = 3,
                Width = 150,
                Height = 26
            };
            HolographicTheme.ApplyToButton(exportPngButton, ButtonStyle.Warning);
            exportPngButton.Click += (s, ev) =>
            {
                if (listView.SelectedItems.Count == 0)
                {
                    MessageBox.Show("Select one or more rows first.", "Export",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                ExportStatsPngs(statsForm, listView.SelectedItems.Cast<ListViewItem>().ToList());
            };
            exportPanel.Controls.Add(exportPngButton);

            // Add the pattern export button (declared earlier, just add to panel now)
            exportPanel.Controls.Add(exportPatternButton);

            // Add controls in dock order
            statsForm.Controls.Add(listView);
            statsForm.Controls.Add(exportPanel);
            statsForm.Controls.Add(filterTextBox);
            statsForm.Controls.Add(summaryLabel);
            statsForm.Controls.Add(togglePanel);
            statsForm.ShowDialog(this);
        }

        /// <summary>
        /// Export static stats rows to a CSV file.
        /// </summary>
        private void ExportStatsCsv(Form owner, List<ListViewItem> items, int mapIndex, string suffix)
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "CSV File|*.csv";
                sfd.FileName = $"static_stats_map{mapIndex}_{suffix}.csv";
                if (sfd.ShowDialog(owner) != DialogResult.OK)
                    return;

                using (var sw = new StreamWriter(sfd.FileName))
                {
                    bool isPatternView = items.Count > 0 && items[0].SubItems.Count == 5;
                    if (isPatternView)
                    {
                        sw.WriteLine("Count,PatternHexIds,CenterTile,Percent");
                        foreach (var lvi in items)
                        {
                            string hexIds = lvi.SubItems[2].Text.Replace("\"", "\"\"");
                            string center = lvi.SubItems[3].Text.Replace("\"", "\"\"");
                            sw.WriteLine($"{lvi.SubItems[1].Text},\"{hexIds}\",\"{center}\",{lvi.SubItems[4].Text}");
                        }
                    }
                    else
                    {
                        sw.WriteLine("ItemId,Hex,Name,Count,Percent");
                        foreach (var lvi in items)
                        {
                            string csvName = lvi.SubItems[3].Text.Replace("\"", "\"\"");
                            sw.WriteLine($"{lvi.SubItems[1].Text},\"{lvi.SubItems[2].Text}\",\"{csvName}\",{lvi.SubItems[4].Text.Replace(",", "")},{lvi.SubItems[5].Text}");
                        }
                    }
                }
                MessageBox.Show($"Exported {items.Count} rows to:\n{sfd.FileName}", "Export CSV",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>
        /// Export full-resolution RGBA static art PNGs for selected rows.
        /// </summary>
        private void ExportStatsPngs(Form owner, List<ListViewItem> items)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = $"Choose folder to export {items.Count} static art PNG(s)";
                if (fbd.ShowDialog(owner) != DialogResult.OK)
                    return;

                string outDir = fbd.SelectedPath;
                int exported = 0;
                int failed = 0;

                foreach (var lvi in items)
                {
                    if (!(lvi.Tag is ushort itemId))
                        continue;

                    var bmp = LoadStaticImage(itemId) as Bitmap;
                    if (bmp == null)
                    {
                        failed++;
                        continue;
                    }

                    string name = lvi.SubItems[3].Text.Trim();
                    string safeName = string.IsNullOrEmpty(name) ? "" : "_" + SanitizeFileName(name);
                    string fileName = $"0x{itemId:X4}{safeName}.png";
                    string fullPath = Path.Combine(outDir, fileName);

                    try
                    {
                        bmp.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
                        exported++;
                    }
                    catch
                    {
                        failed++;
                    }
                }

                string msg = $"Exported {exported} PNG(s) to:\n{outDir}";
                if (failed > 0)
                    msg += $"\n{failed} item(s) had no art or failed to save.";
                MessageBox.Show(msg, "Export PNGs", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (Array.IndexOf(invalid, c) < 0)
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Comparer for sorting the static stats ListView columns.
        /// </summary>
        private class StaticStatsComparer : System.Collections.IComparer
        {
            private readonly int column;
            private readonly bool ascending;

            public StaticStatsComparer(int column, bool ascending)
            {
                this.column = column;
                this.ascending = ascending;
            }

            public int Compare(object x, object y)
            {
                var a = (ListViewItem)x;
                var b = (ListViewItem)y;

                int result;
                // Numeric columns: 1 (ItemId or Count), 4 (Count or Percent for tiles, Percent for patterns)
                if (column == 1 || column == 4)
                {
                    string sa = a.SubItems[column].Text.Replace(",", "").Replace("%", "");
                    string sb = b.SubItems[column].Text.Replace(",", "").Replace("%", "");
                    double da = 0, db = 0;
                    double.TryParse(sa, out da);
                    double.TryParse(sb, out db);
                    result = da.CompareTo(db);
                }
                else if (column == 5)
                {
                    string sa = a.SubItems[column].Text.Replace("%", "");
                    string sb = b.SubItems[column].Text.Replace("%", "");
                    double da = 0, db = 0;
                    double.TryParse(sa, out da);
                    double.TryParse(sb, out db);
                    result = da.CompareTo(db);
                }
                else
                {
                    result = string.Compare(a.SubItems[column].Text, b.SubItems[column].Text, StringComparison.OrdinalIgnoreCase);
                }

                return ascending ? result : -result;
            }
        }
    }
}
