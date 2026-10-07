using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MeesaMultisMaker.Biome;
using MeesaMultisMaker.Mul;

namespace MeesaMultisMaker
{
    partial class MapViewerForm
    {
        // Biome brush state
        internal List<BiomeBrush> biomeBrushes = new List<BiomeBrush>();
        internal BiomeBrush selectedBiomeBrush = null;
        internal bool isBiomePaintMode = false;
        internal int biomeBrushRadius = 5;

        // Biome UI controls
        internal ListBox biomeListBox;
        internal Button sampleBiomeButton;
        internal Button applyBiomeButton;
        internal Button deleteBiomeButton;
        internal CheckBox biomePaintModeCheckBox;
        internal NumericUpDown biomeBrushRadiusNumeric;
        internal CheckBox biomeReplaceStaticsCheckBox;
        internal Label biomeInfoLabel;

        /// <summary>
        /// Initialize biome system — load saved brushes from disk
        /// </summary>
        internal void InitializeBiomeSystem()
        {
            biomeBrushes = BiomeBrushManager.LoadAll();
            RefreshBiomeList();
        }

        /// <summary>
        /// Refresh the biome listbox with current brushes
        /// </summary>
        private void RefreshBiomeList()
        {
            if (biomeListBox == null) return;

            biomeListBox.Items.Clear();
            foreach (var brush in biomeBrushes)
                biomeListBox.Items.Add(brush);

            UpdateBiomeButtons();
        }

        /// <summary>
        /// Update biome button enabled states
        /// </summary>
        internal void UpdateBiomeButtons()
        {
            bool hasBrushSelected = biomeListBox?.SelectedItem != null;
            bool hasMapSelection = replaceTiles.Count > 0;

            if (applyBiomeButton != null)
                applyBiomeButton.Enabled = hasBrushSelected && hasMapSelection;
            if (deleteBiomeButton != null)
                deleteBiomeButton.Enabled = hasBrushSelected;
            if (biomePaintModeCheckBox != null)
                biomePaintModeCheckBox.Enabled = hasBrushSelected;

            if (biomeInfoLabel != null && hasBrushSelected)
            {
                var brush = biomeListBox.SelectedItem as BiomeBrush;
                if (brush != null)
                    biomeInfoLabel.Text = $"{brush.Width}x{brush.Height} | {brush.LandTiles.Count} tiles | {brush.Statics.Count} statics";
            }
            else if (biomeInfoLabel != null)
            {
                biomeInfoLabel.Text = "No biome selected";
            }
        }

        /// <summary>
        /// Sample the currently selected tile area as a new biome brush.
        /// Captures BOTH land tiles AND statics within the selection bounds.
        /// </summary>
        private void SampleBiome()
        {
            if (replaceTiles.Count == 0)
            {
                MessageBox.Show(this,
                    "Select an area of tiles first using Replace mode.\n\n" +
                    "1. Set Mode to 'Replace'\n" +
                    "2. Shift+Click to select a rectangular area\n" +
                    "3. Or use Ctrl+Drag for lasso selection\n" +
                    "4. Then click 'Sample Biome'",
                    "Sample Biome", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string biomeName = PromptForBiomeName();
            if (string.IsNullOrWhiteSpace(biomeName))
                return;

            // Check for duplicate name
            if (biomeBrushes.Any(b => b.Name.Equals(biomeName, StringComparison.OrdinalIgnoreCase)))
            {
                var overwrite = MessageBox.Show(this,
                    $"A biome named '{biomeName}' already exists. Overwrite?",
                    "Biome Exists", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (overwrite != DialogResult.Yes)
                    return;
                biomeBrushes.RemoveAll(b => b.Name.Equals(biomeName, StringComparison.OrdinalIgnoreCase));
                BiomeBrushManager.Delete(biomeName);
            }

            // Calculate selection bounds
            int minX = replaceTiles.Min(t => t.X);
            int maxX = replaceTiles.Max(t => t.X);
            int minY = replaceTiles.Min(t => t.Y);
            int maxY = replaceTiles.Max(t => t.Y);
            int width = maxX - minX + 1;
            int height = maxY - minY + 1;

            var brush = new BiomeBrush
            {
                Name = biomeName,
                Width = width,
                Height = height,
                CreatedDate = DateTime.Now,
                SourceFacet = facetComboBox?.SelectedItem?.ToString() ?? "Unknown",
                SourceX = minX,
                SourceY = minY
            };

            // Sample land tiles
            foreach (var tile in replaceTiles)
            {
                brush.LandTiles.Add(new BiomeLandSample
                {
                    RelativeX = tile.X - minX,
                    RelativeY = tile.Y - minY,
                    TileId = tile.TileId,
                    Z = tile.Z
                });
            }

            // Sample statics in the selected area
            if (currentStatics != null)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        var statics = currentStatics.GetStaticsAt(x, y);
                        foreach (var s in statics)
                        {
                            brush.Statics.Add(new BiomeStaticSample
                            {
                                RelativeX = x - minX,
                                RelativeY = y - minY,
                                ItemId = s.ItemId,
                                Z = s.Z,
                                Hue = s.Hue
                            });
                        }
                    }
                }
            }

            BiomeBrushManager.Save(brush);
            biomeBrushes.Add(brush);
            RefreshBiomeList();

            // Select the new brush
            for (int i = 0; i < biomeListBox.Items.Count; i++)
            {
                if (((BiomeBrush)biomeListBox.Items[i]).Name == biomeName)
                {
                    biomeListBox.SelectedIndex = i;
                    break;
                }
            }

            statusLabel.Text = $"Sampled biome '{biomeName}': {brush.LandTiles.Count} tiles, {brush.Statics.Count} statics ({width}x{height})";
        }

        /// <summary>
        /// Apply the selected biome brush to the current Replace selection.
        /// Uses the WFC-inspired BiomeGenerator to produce new terrain.
        /// </summary>
        private void ApplyBiome()
        {
            if (selectedBiomeBrush == null)
            {
                MessageBox.Show(this, "Select a biome brush first.", "Apply Biome",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (replaceTiles.Count == 0)
            {
                MessageBox.Show(this,
                    "Select target tiles to fill with the biome.\nUse Replace mode to select tiles.",
                    "Apply Biome", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Calculate target bounds
            int minX = replaceTiles.Min(t => t.X);
            int maxX = replaceTiles.Max(t => t.X);
            int minY = replaceTiles.Min(t => t.Y);
            int maxY = replaceTiles.Max(t => t.Y);
            int width = maxX - minX + 1;
            int height = maxY - minY + 1;

            // Base Z from target center
            int centerX = (minX + maxX) / 2;
            int centerY = (minY + maxY) / 2;
            sbyte baseZ = GetZAt(centerX, centerY);

            // Build a HashSet for O(1) selection lookups
            var selectedPositions = new HashSet<(int, int)>();
            foreach (var t in replaceTiles)
                selectedPositions.Add((t.X, t.Y));

            // Generate
            var generator = new BiomeGenerator(selectedBiomeBrush);
            var result = generator.Generate(width, height, baseZ);

            var action = new MapAction { Description = $"Apply biome '{selectedBiomeBrush.Name}'" };

            // Snapshot override state BEFORE any modifications
            foreach (var pos in selectedPositions)
                CaptureOverrideState(action, pos);

            // Apply land tiles (record old→new in same pass)
            int tilesApplied = 0;
            foreach (var gen in result.LandTiles)
            {
                int worldX = minX + gen.RelativeX;
                int worldY = minY + gen.RelativeY;

                if (!selectedPositions.Contains((worldX, worldY)))
                    continue;
                if (worldX < 0 || worldX >= currentMap.Width || worldY < 0 || worldY >= currentMap.Height)
                    continue;

                var tile = currentMap.Tiles[worldX, worldY];
                if (tile != null)
                {
                    var lk = (worldX, worldY);
                    if (!action.LandChanges.ContainsKey(lk))
                    {
                        action.LandChanges[lk] = new LandTileChange
                        {
                            OldTileId = tile.TileId, OldZ = tile.Z,
                            NewTileId = gen.TileId, NewZ = gen.Z
                        };
                    }

                    tile.TileId = gen.TileId;
                    tile.Z = gen.Z;
                    tilesApplied++;
                }
            }

            // Apply statics
            int staticsApplied = 0;
            bool replaceExisting = biomeReplaceStaticsCheckBox?.Checked ?? true;

            if (replaceExisting)
            {
                foreach (var pos in selectedPositions)
                    staticOverrides[pos] = new List<StaticTile>();
            }

            foreach (var gen in result.Statics)
            {
                int worldX = minX + gen.RelativeX;
                int worldY = minY + gen.RelativeY;

                if (!selectedPositions.Contains((worldX, worldY)))
                    continue;

                var staticTile = new StaticTile
                {
                    ItemId = gen.ItemId,
                    X = (byte)(worldX % 8),
                    Y = (byte)(worldY % 8),
                    Z = gen.Z,
                    Hue = gen.Hue,
                    WorldX = worldX,
                    WorldY = worldY
                };

                var key = (worldX, worldY);
                if (!staticOverrides.ContainsKey(key))
                    staticOverrides[key] = new List<StaticTile>();
                staticOverrides[key].Add(staticTile);
                staticsApplied++;
            }

            // Finalize override snapshots and record
            FinalizeOverrideState(action);
            undoRedoManager.RecordAction(action);

            InvalidateStaticsCache();
            InvalidateMinimaps();
            GenerateMapImage();
            MarkMapDirty();
            UpdateUndoRedoButtons();
            statusLabel.Text = $"Applied biome '{selectedBiomeBrush.Name}': {tilesApplied} land tiles, {staticsApplied} statics placed (unsaved)";
        }

        /// <summary>
        /// Apply biome at a specific map location (for paint mode).
        /// Uses the brush radius to define a circular patch.
        /// </summary>
        internal void ApplyBiomeAtLocation(int mapX, int mapY)
        {
            if (selectedBiomeBrush == null || currentMap == null) return;

            int radius = biomeBrushRadius;
            sbyte baseZ = GetZAt(mapX, mapY);

            int size = radius * 2 + 1;
            var generator = new BiomeGenerator(selectedBiomeBrush);
            var result = generator.Generate(size, size, baseZ);

            var action = new MapAction { Description = "Paint biome" };

            // Snapshot old state for circular area
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius) continue;
                    CaptureOverrideState(action, (mapX + dx, mapY + dy));
                }
            }

            // Apply land tiles within circular brush
            foreach (var gen in result.LandTiles)
            {
                int dx = gen.RelativeX - radius;
                int dy = gen.RelativeY - radius;
                if (dx * dx + dy * dy > radius * radius)
                    continue;

                int worldX = mapX + dx;
                int worldY = mapY + dy;
                if (worldX < 0 || worldX >= currentMap.Width || worldY < 0 || worldY >= currentMap.Height)
                    continue;

                var tile = currentMap.Tiles[worldX, worldY];
                if (tile != null)
                {
                    var lk = (worldX, worldY);
                    if (!action.LandChanges.ContainsKey(lk))
                    {
                        action.LandChanges[lk] = new LandTileChange
                        {
                            OldTileId = tile.TileId, OldZ = tile.Z,
                            NewTileId = gen.TileId, NewZ = gen.Z
                        };
                    }

                    tile.TileId = gen.TileId;
                    tile.Z = gen.Z;
                }
            }

            // Apply statics via override system
            bool replaceExisting = biomeReplaceStaticsCheckBox?.Checked ?? true;

            if (replaceExisting)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (dx * dx + dy * dy > radius * radius) continue;
                        var pos = (mapX + dx, mapY + dy);
                        staticOverrides[pos] = new List<StaticTile>();
                    }
                }
            }

            foreach (var gen in result.Statics)
            {
                int dx = gen.RelativeX - radius;
                int dy = gen.RelativeY - radius;
                if (dx * dx + dy * dy > radius * radius)
                    continue;

                int worldX = mapX + dx;
                int worldY = mapY + dy;
                if (worldX < 0 || worldX >= currentMap.Width || worldY < 0 || worldY >= currentMap.Height)
                    continue;

                var staticTile = new StaticTile
                {
                    ItemId = gen.ItemId,
                    X = (byte)(worldX % 8),
                    Y = (byte)(worldY % 8),
                    Z = gen.Z,
                    Hue = gen.Hue,
                    WorldX = worldX,
                    WorldY = worldY
                };

                var key = (worldX, worldY);
                if (!staticOverrides.ContainsKey(key))
                    staticOverrides[key] = new List<StaticTile>();
                staticOverrides[key].Add(staticTile);
            }

            FinalizeOverrideState(action);
            undoRedoManager.RecordAction(action);

            hasUnsavedChanges = true;
            InvalidateStaticsCache();
            minimapGenerator.Invalidate();
            minimapGenerating = false;
            GenerateMapImage();
            UpdateUndoRedoButtons();
        }

        /// <summary>
        /// Show a dialog to get the biome name from the user
        /// </summary>
        private string PromptForBiomeName()
        {
            using (var dialog = new Form())
            {
                dialog.Text = "Add Biome Type";
                dialog.Width = 400;
                dialog.Height = 180;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                HolographicTheme.ApplyToForm(dialog);

                var label = new Label
                {
                    Text = "Enter a name for this biome brush:",
                    Left = 20,
                    Top = 20,
                    Width = 340,
                    Height = 25
                };
                HolographicTheme.ApplyToLabel(label);
                dialog.Controls.Add(label);

                var textBox = new TextBox
                {
                    Left = 20,
                    Top = 50,
                    Width = 340,
                    Height = 25
                };
                HolographicTheme.ApplyToTextBox(textBox);
                dialog.Controls.Add(textBox);

                var okButton = new Button
                {
                    Text = "Add Biome",
                    Left = 180,
                    Top = 90,
                    Width = 90,
                    Height = 30,
                    DialogResult = DialogResult.OK
                };
                HolographicTheme.ApplyToButton(okButton, ButtonStyle.Accent);
                dialog.Controls.Add(okButton);

                var cancelButton = new Button
                {
                    Text = "Cancel",
                    Left = 280,
                    Top = 90,
                    Width = 80,
                    Height = 30,
                    DialogResult = DialogResult.Cancel
                };
                HolographicTheme.ApplyToButton(cancelButton);
                dialog.Controls.Add(cancelButton);

                dialog.AcceptButton = okButton;
                dialog.CancelButton = cancelButton;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                    return textBox.Text.Trim();
            }
            return null;
        }

        /// <summary>
        /// Delete the currently selected biome brush
        /// </summary>
        private void DeleteSelectedBiome()
        {
            if (biomeListBox?.SelectedItem == null) return;

            var brush = biomeListBox.SelectedItem as BiomeBrush;
            if (brush == null) return;

            var result = MessageBox.Show(this,
                $"Delete biome brush '{brush.Name}'?",
                "Delete Biome", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;

            BiomeBrushManager.Delete(brush.Name);
            biomeBrushes.Remove(brush);
            selectedBiomeBrush = null;
            RefreshBiomeList();
            statusLabel.Text = $"Deleted biome brush '{brush.Name}'";
        }
    }
}
