using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Heightmap visualization modes for the map editor.
    /// </summary>
    public enum HeightmapMode
    {
        Off,
        Grayscale,
        Heatmap,
        Contour
    }

    /// <summary>
    /// Terrain editing tool types modelled after CentrED#.
    /// </summary>
    public enum HeightTool
    {
        None,
        Raise,
        Lower,
        Flatten,
        Smooth,
        Ramp
    }

    partial class MapViewerForm
    {
        // ================================================================
        //  Heightmap state
        // ================================================================
        internal HeightmapMode heightmapMode = HeightmapMode.Off;
        internal HeightTool activeHeightTool = HeightTool.None;
        internal int heightToolStep = 1;       // Z units per click for Raise/Lower
        internal int heightToolRadius = 1;     // Brush radius in tiles (0 = single tile)
        internal sbyte flattenTargetZ = 0;     // Target Z for Flatten tool

        // Ramp tool: uses the first and last selected tiles as endpoints
        private bool rampStartSet = false;
        private int rampStartX, rampStartY;
        private sbyte rampStartZ;

        // UI controls
        private ComboBox heightmapModeCombo;
        private ComboBox heightToolCombo;
        private NumericUpDown heightStepNumeric;
        private NumericUpDown heightRadiusNumeric;
        private NumericUpDown flattenZNumeric;
        private Panel heightmapPanel;
        private Label heightToolInfoLabel;

        // ================================================================
        //  UI setup — called from Designer.cs
        // ================================================================

        /// <summary>
        /// Build the Heightmap panel and add it into the right-side settings area.
        /// Call this from InitializeComponent after CreateComfyUISettingsPanel.
        /// </summary>
        internal void InitializeHeightmapPanel()
        {
            heightmapPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 250,
                Padding = new Padding(8),
                AutoScroll = false
            };
            HolographicTheme.ApplyToPanel(heightmapPanel, true);

            int y = 6;
            int ctrlW = 280;

            // ---------- Title ----------
            var title = new Label
            {
                Text = "Heightmap / Terrain",
                Location = new Point(8, y),
                Width = ctrlW,
                Height = 22,
                Font = new Font(this.Font.FontFamily, 10, FontStyle.Bold)
            };
            HolographicTheme.ApplyToLabel(title);
            heightmapPanel.Controls.Add(title);
            y += 26;

            // ---------- Visualisation mode ----------
            var vizLabel = new Label { Text = "View:", Location = new Point(8, y + 3), Width = 40, Height = 18 };
            HolographicTheme.ApplyToLabel(vizLabel);
            heightmapPanel.Controls.Add(vizLabel);

            heightmapModeCombo = new ComboBox
            {
                Location = new Point(50, y),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            heightmapModeCombo.Items.AddRange(new object[] { "Normal", "Grayscale", "Heatmap", "Contour" });
            heightmapModeCombo.SelectedIndex = 0;
            heightmapModeCombo.SelectedIndexChanged += (s, e) =>
            {
                heightmapMode = (HeightmapMode)heightmapModeCombo.SelectedIndex;
                GenerateMapImage();
            };
            HolographicTheme.ApplyToComboBox(heightmapModeCombo);
            heightmapPanel.Controls.Add(heightmapModeCombo);
            y += 28;

            // ---------- Tool selector ----------
            var toolLabel = new Label { Text = "Tool:", Location = new Point(8, y + 3), Width = 40, Height = 18 };
            HolographicTheme.ApplyToLabel(toolLabel);
            heightmapPanel.Controls.Add(toolLabel);

            heightToolCombo = new ComboBox
            {
                Location = new Point(50, y),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            heightToolCombo.Items.AddRange(new object[] { "None", "Raise", "Lower", "Flatten", "Smooth", "Ramp" });
            heightToolCombo.SelectedIndex = 0;
            heightToolCombo.SelectedIndexChanged += (s, e) =>
            {
                activeHeightTool = (HeightTool)heightToolCombo.SelectedIndex;
                rampStartSet = false;
                UpdateHeightToolInfo();
            };
            HolographicTheme.ApplyToComboBox(heightToolCombo);
            heightmapPanel.Controls.Add(heightToolCombo);
            y += 28;

            // ---------- Step size ----------
            var stepLabel = new Label { Text = "Step:", Location = new Point(8, y + 3), Width = 40, Height = 18 };
            HolographicTheme.ApplyToLabel(stepLabel);
            heightmapPanel.Controls.Add(stepLabel);

            heightStepNumeric = new NumericUpDown
            {
                Location = new Point(50, y),
                Width = 60,
                Minimum = 1,
                Maximum = 50,
                Value = 1
            };
            heightStepNumeric.ValueChanged += (s, e) => { heightToolStep = (int)heightStepNumeric.Value; };
            HolographicTheme.ApplyToNumericUpDown(heightStepNumeric);
            heightmapPanel.Controls.Add(heightStepNumeric);

            // ---------- Brush radius ----------
            var radLabel = new Label { Text = "Radius:", Location = new Point(120, y + 3), Width = 50, Height = 18 };
            HolographicTheme.ApplyToLabel(radLabel);
            heightmapPanel.Controls.Add(radLabel);

            heightRadiusNumeric = new NumericUpDown
            {
                Location = new Point(175, y),
                Width = 55,
                Minimum = 0,
                Maximum = 20,
                Value = 1
            };
            heightRadiusNumeric.ValueChanged += (s, e) => { heightToolRadius = (int)heightRadiusNumeric.Value; };
            HolographicTheme.ApplyToNumericUpDown(heightRadiusNumeric);
            heightmapPanel.Controls.Add(heightRadiusNumeric);
            y += 28;

            // ---------- Flatten target Z ----------
            var flatLabel = new Label { Text = "Flat Z:", Location = new Point(8, y + 3), Width = 45, Height = 18 };
            HolographicTheme.ApplyToLabel(flatLabel);
            heightmapPanel.Controls.Add(flatLabel);

            flattenZNumeric = new NumericUpDown
            {
                Location = new Point(55, y),
                Width = 60,
                Minimum = -128,
                Maximum = 127,
                Value = 0
            };
            flattenZNumeric.ValueChanged += (s, e) => { flattenTargetZ = (sbyte)flattenZNumeric.Value; };
            HolographicTheme.ApplyToNumericUpDown(flattenZNumeric);
            heightmapPanel.Controls.Add(flattenZNumeric);

            // ---------- Apply button ----------
            var applyButton = new Button
            {
                Text = "Apply to Selection",
                Location = new Point(130, y - 2),
                Width = 110,
                Height = 24
            };
            applyButton.Click += (s, e) => ApplyHeightToolToSelection();
            HolographicTheme.ApplyToButton(applyButton, ButtonStyle.Accent);
            heightmapPanel.Controls.Add(applyButton);
            y += 30;

            // ---------- Info label ----------
            heightToolInfoLabel = new Label
            {
                Text = "Select tiles, then Apply or left-click with a tool.",
                Location = new Point(8, y),
                Width = ctrlW,
                Height = 36,
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8)
            };
            HolographicTheme.ApplyToLabel(heightToolInfoLabel);
            heightmapPanel.Controls.Add(heightToolInfoLabel);

            // Add to the right-side settings panel so it is always visible
            comfySettingsPanel.Controls.Add(heightmapPanel);
        }

        // ================================================================
        //  Heightmap colour helpers
        // ================================================================

        /// <summary>
        /// Map a Z value (-128..127) to a greyscale colour.
        /// </summary>
        internal static Color HeightToGrayscale(sbyte z)
        {
            int v = (z + 128) * 255 / 255;   // 0..255
            v = Math.Max(0, Math.Min(255, v));
            return Color.FromArgb(255, v, v, v);
        }

        /// <summary>
        /// Map a Z value to a heatmap gradient (blue → cyan → green → yellow → red).
        /// </summary>
        internal static Color HeightToHeatmap(sbyte z)
        {
            float t = (z + 128f) / 255f;     // 0..1
            t = Math.Max(0f, Math.Min(1f, t));

            int r, g, b;
            if (t < 0.25f)
            {
                float s = t / 0.25f;
                r = 0; g = (int)(s * 255); b = 255;
            }
            else if (t < 0.5f)
            {
                float s = (t - 0.25f) / 0.25f;
                r = 0; g = 255; b = (int)((1f - s) * 255);
            }
            else if (t < 0.75f)
            {
                float s = (t - 0.5f) / 0.25f;
                r = (int)(s * 255); g = 255; b = 0;
            }
            else
            {
                float s = (t - 0.75f) / 0.25f;
                r = 255; g = (int)((1f - s) * 255); b = 0;
            }
            return Color.FromArgb(255, r, g, b);
        }

        /// <summary>
        /// Whether a contour line should be drawn between two Z values.
        /// Returns true when they cross a contour interval boundary.
        /// </summary>
        internal static bool IsContourBoundary(sbyte z1, sbyte z2, int interval = 5)
        {
            return (z1 / interval) != (z2 / interval);
        }

        /// <summary>
        /// Returns the heightmap colour for a tile based on the active mode.
        /// Returns null when heightmap mode is Off (use normal rendering).
        /// </summary>
        internal Color? GetHeightmapColor(sbyte z)
        {
            switch (heightmapMode)
            {
                case HeightmapMode.Grayscale:
                    return HeightToGrayscale(z);
                case HeightmapMode.Heatmap:
                    return HeightToHeatmap(z);
                default:
                    return null;
            }
        }

        // ================================================================
        //  Height tools — core operations
        // ================================================================

        /// <summary>
        /// Apply the currently selected height tool to the current tile selection.
        /// </summary>
        internal void ApplyHeightToolToSelection()
        {
            if (currentMap == null) return;
            if (selectedTiles.Count == 0)
            {
                if (statusLabel != null)
                    statusLabel.Text = "No land tiles selected — select tiles first.";
                return;
            }

            switch (activeHeightTool)
            {
                case HeightTool.Raise:
                    ApplyRaiseLower(heightToolStep);
                    break;
                case HeightTool.Lower:
                    ApplyRaiseLower(-heightToolStep);
                    break;
                case HeightTool.Flatten:
                    ApplyFlatten(flattenTargetZ);
                    break;
                case HeightTool.Smooth:
                    ApplySmooth();
                    break;
                case HeightTool.Ramp:
                    ApplyRamp();
                    break;
                default:
                    if (statusLabel != null)
                        statusLabel.Text = "No height tool selected.";
                    break;
            }
        }

        /// <summary>
        /// Raise or lower all selected tiles by <paramref name="delta"/> Z units.
        /// </summary>
        private void ApplyRaiseLower(int delta)
        {
            if (selectedTiles.Count == 0 || currentMap == null) return;

            var action = new MapAction { Description = $"{(delta > 0 ? "Raise" : "Lower")} {selectedTiles.Count} tile(s) by {Math.Abs(delta)}" };

            foreach (var t in selectedTiles)
            {
                if (t.X < 0 || t.X >= currentMap.Width || t.Y < 0 || t.Y >= currentMap.Height) continue;
                var tile = currentMap.Tiles[t.X, t.Y];
                if (tile == null) continue;

                var lk = (t.X, t.Y);
                if (action.LandChanges.ContainsKey(lk)) continue;

                sbyte newZ = (sbyte)Math.Max(-128, Math.Min(127, tile.Z + delta));
                action.LandChanges[lk] = new LandTileChange
                {
                    OldTileId = tile.TileId, OldZ = tile.Z,
                    NewTileId = tile.TileId, NewZ = newZ
                };
                tile.Z = newZ;
                t.Z = newZ;
            }

            FinishHeightEdit(action);
        }

        /// <summary>
        /// Set all selected tiles to exactly <paramref name="targetZ"/>.
        /// </summary>
        private void ApplyFlatten(sbyte targetZ)
        {
            if (selectedTiles.Count == 0 || currentMap == null) return;

            var action = new MapAction { Description = $"Flatten {selectedTiles.Count} tile(s) to Z={targetZ}" };

            foreach (var t in selectedTiles)
            {
                if (t.X < 0 || t.X >= currentMap.Width || t.Y < 0 || t.Y >= currentMap.Height) continue;
                var tile = currentMap.Tiles[t.X, t.Y];
                if (tile == null) continue;

                var lk = (t.X, t.Y);
                if (action.LandChanges.ContainsKey(lk)) continue;

                action.LandChanges[lk] = new LandTileChange
                {
                    OldTileId = tile.TileId, OldZ = tile.Z,
                    NewTileId = tile.TileId, NewZ = targetZ
                };
                tile.Z = targetZ;
                t.Z = targetZ;
            }

            FinishHeightEdit(action);
        }

        /// <summary>
        /// Smooth each selected tile's Z toward the average of its neighbours.
        /// </summary>
        private void ApplySmooth()
        {
            if (selectedTiles.Count == 0 || currentMap == null) return;

            var action = new MapAction { Description = $"Smooth {selectedTiles.Count} tile(s)" };

            // Pre-compute new Z values so neighbours don't shift mid-pass
            var newZValues = new Dictionary<(int x, int y), sbyte>();

            foreach (var t in selectedTiles)
            {
                if (t.X < 0 || t.X >= currentMap.Width || t.Y < 0 || t.Y >= currentMap.Height) continue;

                int sum = 0;
                int count = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = t.X + dx;
                        int ny = t.Y + dy;
                        if (nx >= 0 && nx < currentMap.Width && ny >= 0 && ny < currentMap.Height)
                        {
                            var nb = currentMap.Tiles[nx, ny];
                            if (nb != null) { sum += nb.Z; count++; }
                        }
                    }
                }
                if (count > 0)
                {
                    sbyte avg = (sbyte)Math.Max(-128, Math.Min(127, sum / count));
                    newZValues[(t.X, t.Y)] = avg;
                }
            }

            foreach (var t in selectedTiles)
            {
                var lk = (t.X, t.Y);
                if (!newZValues.TryGetValue(lk, out sbyte newZ)) continue;

                var tile = currentMap.Tiles[t.X, t.Y];
                if (tile == null) continue;
                if (action.LandChanges.ContainsKey(lk)) continue;

                action.LandChanges[lk] = new LandTileChange
                {
                    OldTileId = tile.TileId, OldZ = tile.Z,
                    NewTileId = tile.TileId, NewZ = newZ
                };
                tile.Z = newZ;
                t.Z = newZ;
            }

            FinishHeightEdit(action);
        }

        /// <summary>
        /// Create a linear ramp across the selected tiles from lowest to highest
        /// sorted by (X + Y).  The first and last tiles' current Z values define
        /// the range and all tiles between are linearly interpolated.
        /// </summary>
        private void ApplyRamp()
        {
            if (selectedTiles.Count < 2 || currentMap == null)
            {
                if (statusLabel != null)
                    statusLabel.Text = "Ramp needs at least 2 selected tiles.";
                return;
            }

            // Sort tiles by map depth (x + y) so the ramp follows isometric order
            var sorted = selectedTiles.OrderBy(t => t.X + t.Y).ToList();

            sbyte startZ = sorted.First().Z;
            sbyte endZ = sorted.Last().Z;
            int total = sorted.Count;

            var action = new MapAction { Description = $"Ramp {total} tile(s) from Z={startZ} to Z={endZ}" };

            for (int i = 0; i < total; i++)
            {
                var t = sorted[i];
                if (t.X < 0 || t.X >= currentMap.Width || t.Y < 0 || t.Y >= currentMap.Height) continue;

                var tile = currentMap.Tiles[t.X, t.Y];
                if (tile == null) continue;

                float frac = (total > 1) ? (float)i / (total - 1) : 0f;
                sbyte newZ = (sbyte)Math.Max(-128, Math.Min(127, Math.Round(startZ + (endZ - startZ) * frac)));

                var lk = (t.X, t.Y);
                if (!action.LandChanges.ContainsKey(lk))
                {
                    action.LandChanges[lk] = new LandTileChange
                    {
                        OldTileId = tile.TileId, OldZ = tile.Z,
                        NewTileId = tile.TileId, NewZ = newZ
                    };
                }
                tile.Z = newZ;
                t.Z = newZ;
            }

            FinishHeightEdit(action);
        }

        /// <summary>
        /// Shared finish logic: record undo, refresh map, update UI.
        /// </summary>
        private void FinishHeightEdit(MapAction action)
        {
            if (action.LandChanges.Count == 0) return;

            undoRedoManager.RecordAction(action);
            hasUnsavedChanges = true;
            InvalidateMinimaps();
            GenerateMapImage();
            UpdateSaveButtonState();
            UpdateUndoRedoButtons();

            if (statusLabel != null)
                statusLabel.Text = $"{action.Description} (unsaved)";
        }

        /// <summary>
        /// Apply the height tool at a single map location (for click-to-paint).
        /// Respects the brush radius.
        /// </summary>
        internal void ApplyHeightToolAtLocation(int mapX, int mapY)
        {
            if (currentMap == null) return;
            if (activeHeightTool == HeightTool.None || activeHeightTool == HeightTool.Ramp) return;

            int radius = heightToolRadius;
            var action = new MapAction { Description = $"{activeHeightTool} at ({mapX},{mapY}) r={radius}" };

            // Pre-read for Smooth
            Dictionary<(int, int), sbyte> smoothTargets = null;
            if (activeHeightTool == HeightTool.Smooth)
            {
                smoothTargets = new Dictionary<(int, int), sbyte>();
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (dx * dx + dy * dy > radius * radius) continue;
                        int tx = mapX + dx, ty = mapY + dy;
                        if (tx < 0 || tx >= currentMap.Width || ty < 0 || ty >= currentMap.Height) continue;

                        int sum = 0, cnt = 0;
                        for (int ny = -1; ny <= 1; ny++)
                            for (int nx = -1; nx <= 1; nx++)
                            {
                                int ax = tx + nx, ay = ty + ny;
                                if (ax >= 0 && ax < currentMap.Width && ay >= 0 && ay < currentMap.Height)
                                {
                                    var nb = currentMap.Tiles[ax, ay];
                                    if (nb != null) { sum += nb.Z; cnt++; }
                                }
                            }
                        if (cnt > 0)
                            smoothTargets[(tx, ty)] = (sbyte)Math.Max(-128, Math.Min(127, sum / cnt));
                    }
                }
            }

            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius) continue;
                    int tx = mapX + dx, ty = mapY + dy;
                    if (tx < 0 || tx >= currentMap.Width || ty < 0 || ty >= currentMap.Height) continue;

                    var tile = currentMap.Tiles[tx, ty];
                    if (tile == null) continue;

                    sbyte newZ = tile.Z;
                    switch (activeHeightTool)
                    {
                        case HeightTool.Raise:
                            newZ = (sbyte)Math.Min(127, tile.Z + heightToolStep);
                            break;
                        case HeightTool.Lower:
                            newZ = (sbyte)Math.Max(-128, tile.Z - heightToolStep);
                            break;
                        case HeightTool.Flatten:
                            newZ = flattenTargetZ;
                            break;
                        case HeightTool.Smooth:
                            if (smoothTargets != null && smoothTargets.TryGetValue((tx, ty), out sbyte sv))
                                newZ = sv;
                            break;
                    }

                    if (newZ == tile.Z) continue;

                    var lk = (tx, ty);
                    if (!action.LandChanges.ContainsKey(lk))
                    {
                        action.LandChanges[lk] = new LandTileChange
                        {
                            OldTileId = tile.TileId, OldZ = tile.Z,
                            NewTileId = tile.TileId, NewZ = newZ
                        };
                    }
                    tile.Z = newZ;
                }
            }

            FinishHeightEdit(action);
        }

        // ================================================================
        //  UI helpers
        // ================================================================

        private void UpdateHeightToolInfo()
        {
            if (heightToolInfoLabel == null) return;
            switch (activeHeightTool)
            {
                case HeightTool.None:
                    heightToolInfoLabel.Text = "Select a tool to edit terrain height.";
                    break;
                case HeightTool.Raise:
                    heightToolInfoLabel.Text = "Raises selected tiles by Step.\nLeft-click map to paint, or select + Apply.";
                    break;
                case HeightTool.Lower:
                    heightToolInfoLabel.Text = "Lowers selected tiles by Step.\nLeft-click map to paint, or select + Apply.";
                    break;
                case HeightTool.Flatten:
                    heightToolInfoLabel.Text = "Sets all selected tiles to Flat Z.\nLeft-click map to paint, or select + Apply.";
                    break;
                case HeightTool.Smooth:
                    heightToolInfoLabel.Text = "Averages each tile's Z with its neighbors.\nLeft-click map to paint, or select + Apply.";
                    break;
                case HeightTool.Ramp:
                    heightToolInfoLabel.Text = "Select 2+ tiles, then Apply.\nInterpolates Z linearly across the selection.";
                    break;
            }
        }

        /// <summary>
        /// Draw a horizontal colour-bar legend at the bottom-right of the map
        /// view so the user can read Z values from the heightmap colours.
        /// </summary>
        internal void DrawHeightmapLegend(Graphics g, int viewW, int viewH)
        {
            const int BAR_W = 256;
            const int BAR_H = 14;
            const int MARGIN = 12;
            int x0 = viewW - BAR_W - MARGIN;
            int y0 = viewH - BAR_H - MARGIN - 18; // room for labels below

            // Background
            using (var bg = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                g.FillRectangle(bg, x0 - 4, y0 - 4, BAR_W + 8, BAR_H + 26);

            // Gradient bar
            for (int i = 0; i < BAR_W; i++)
            {
                sbyte z = (sbyte)(-128 + i);
                Color c = heightmapMode == HeightmapMode.Grayscale
                    ? HeightToGrayscale(z)
                    : HeightToHeatmap(z);
                using (var pen = new Pen(c))
                    g.DrawLine(pen, x0 + i, y0, x0 + i, y0 + BAR_H);
            }

            // Border
            using (var pen = new Pen(Color.White))
                g.DrawRectangle(pen, x0, y0, BAR_W - 1, BAR_H);

            // Labels
            using (var font = new Font("Arial", 8))
            using (var brush = new SolidBrush(Color.White))
            {
                g.DrawString("-128", font, brush, x0, y0 + BAR_H + 2);
                g.DrawString("0", font, brush, x0 + BAR_W / 2 - 4, y0 + BAR_H + 2);
                string maxLabel = "127";
                var sz = g.MeasureString(maxLabel, font);
                g.DrawString(maxLabel, font, brush, x0 + BAR_W - sz.Width, y0 + BAR_H + 2);
            }
        }
    }
}
