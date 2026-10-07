using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using MeesaMultisMaker.Mul;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Holds copied land tiles and statics with positions relative to the
    /// top-left corner of the original selection so they can be pasted
    /// at any map location.
    /// </summary>
    public class MapClipboardData
    {
        public int Width { get; set; }
        public int Height { get; set; }

        public List<ClipboardLandTile> LandTiles { get; set; } = new List<ClipboardLandTile>();
        public List<ClipboardStaticTile> Statics { get; set; } = new List<ClipboardStaticTile>();

        public bool IsEmpty => LandTiles.Count == 0 && Statics.Count == 0;
    }

    public class ClipboardLandTile
    {
        public int RelativeX { get; set; }
        public int RelativeY { get; set; }
        public ushort TileId { get; set; }
        public sbyte Z { get; set; }
    }

    public class ClipboardStaticTile
    {
        public int RelativeX { get; set; }
        public int RelativeY { get; set; }
        public ushort ItemId { get; set; }
        public sbyte Z { get; set; }
        public ushort Hue { get; set; }
    }

    /// <summary>
    /// Selection mode for tile editing
    /// </summary>
    public enum TileSelectionMode
    {
        Replace,    // Tiles to be replaced with AI-generated art (inpaint mask)
        Context     // Tiles to include as context reference (not modified)
    }

    /// <summary>
    /// What type of objects can be selected
    /// </summary>
    public enum SelectionTargetType
    {
        Land,       // Select land tiles only
        Statics     // Select static items only
    }

    /// <summary>
    /// Represents a selected map tile
    /// </summary>
    public class SelectedTile
    {
        public int X { get; set; }
        public int Y { get; set; }
        public ushort TileId { get; set; }
        public sbyte Z { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is SelectedTile other)
                return X == other.X && Y == other.Y;
            return false;
        }

        public override int GetHashCode()
        {
            return X.GetHashCode() ^ Y.GetHashCode();
        }
    }

    /// <summary>
    /// Represents a selected static item
    /// </summary>
    public class SelectedStatic
    {
        public int X { get; set; }
        public int Y { get; set; }
        public ushort ItemId { get; set; }
        public sbyte Z { get; set; }
        public ushort Hue { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is SelectedStatic other)
                return X == other.X && Y == other.Y && ItemId == other.ItemId && Z == other.Z;
            return false;
        }

        public override int GetHashCode()
        {
            return X.GetHashCode() ^ Y.GetHashCode() ^ ItemId.GetHashCode() ^ Z.GetHashCode();
        }
    }

    partial class MapViewerForm
    {
        // Dual selection system for land tiles
        private readonly List<SelectedTile> replaceTiles = new List<SelectedTile>();
        private readonly List<SelectedTile> contextTiles = new List<SelectedTile>();
        private TileSelectionMode currentSelectionMode = TileSelectionMode.Replace;

        // Static object selection
        private readonly List<SelectedStatic> selectedStatics = new List<SelectedStatic>();
        private SelectionTargetType selectionTargetType = SelectionTargetType.Land;

        // Static cycling state - for cycling through overlapping statics on repeated clicks
        private Point lastStaticClickLocation = Point.Empty;
        private int staticCycleIndex = 0;
        private List<(SelectedStatic staticItem, RectangleF bounds, int drawOrder)> lastStaticCandidates = new List<(SelectedStatic, RectangleF, int)>();

        // Legacy compatibility - returns current mode's selection
        private List<SelectedTile> selectedTiles
        {
            get { return currentSelectionMode == TileSelectionMode.Replace ? replaceTiles : contextTiles; }
        }

        /// <summary>
        /// Convert screen coordinates to map coordinates
        /// </summary>
        private bool ScreenToMapCoords(int screenX, int screenY, out int mapX, out int mapY)
        {
            mapX = 0;
            mapY = 0;

            if (currentMap == null) return false;
            if (mapPictureBox.Image == null) return false;

            // IMPORTANT: Use the IMAGE dimensions for coordinate calculations
            // This ensures mouse clicks align with where the tiles were actually rendered
            int viewW = mapPictureBox.Image.Width;
            int viewH = mapPictureBox.Image.Height;

            const int TILE_WIDTH = 44;
            const int TILE_HEIGHT = 44;

            float zoom = zoomFactor / 44f;
            float halfTileW = (TILE_WIDTH / 2f) * zoom;
            float halfTileH = (TILE_HEIGHT / 2f) * zoom;

            float centerX = viewW / 2f;
            float centerY = viewH / 2f;

            // Screen offset from center
            float screenOffsetX = screenX - centerX;
            float screenOffsetY = screenY - centerY;

            // Inverse isometric transformation
            // Forward: isoX = (relX - relY) * halfTileW, isoY = (relX + relY) * halfTileH
            // Inverse: relX = (isoX/halfTileW + isoY/halfTileH) / 2
            //          relY = (isoY/halfTileH - isoX/halfTileW) / 2
            float isoX = screenOffsetX / halfTileW;
            float isoY = screenOffsetY / halfTileH;

            float relX = (isoX + isoY) / 2f;
            float relY = (isoY - isoX) / 2f;

            mapX = (int)Math.Round(cameraX + relX);
            mapY = (int)Math.Round(cameraY + relY);

            return mapX >= 0 && mapX < currentMap.Width && mapY >= 0 && mapY < currentMap.Height;
        }

        /// <summary>
        /// Calculate the screen bounding rectangle for a static item
        /// </summary>
        private RectangleF GetStaticScreenBounds(int mapX, int mapY, sbyte z, ushort itemId)
        {
            if (mapPictureBox.Image == null) return RectangleF.Empty;

            int viewW = mapPictureBox.Image.Width;
            int viewH = mapPictureBox.Image.Height;

            const int TILE_WIDTH = 44;
            const int TILE_HEIGHT = 44;

            float zoom = zoomFactor / 44f;
            float halfTileW = (TILE_WIDTH / 2f) * zoom;
            float halfTileH = (TILE_HEIGHT / 2f) * zoom;

            float centerX = viewW / 2f;
            float centerY = viewH / 2f;

            int relX = mapX - cameraX;
            int relY = mapY - cameraY;

            float isoX = (relX - relY) * halfTileW;
            float isoY = (relX + relY) * halfTileH;

            float screenX = centerX + isoX;
            float screenY = centerY + isoY;

            // Get the static image to determine its size
            var img = LoadStaticImage(itemId);
            if (img == null) return RectangleF.Empty;

            float imgW = img.Width * zoom;
            float imgH = img.Height * zoom;

            // Calculate draw position (same as in GenerateMapImage and DrawStaticSelection)
            float zOffset = z * Z_SCALE * zoom;
            float drawX = screenX - imgW / 2f;
            float drawY = screenY - imgH + halfTileH - zOffset;

            return new RectangleF(drawX, drawY, imgW, imgH);
        }

        /// <summary>
        /// Handle tile/static selection based on current target type
        /// </summary>
        private void HandleTileSelection(MouseEventArgs e)
        {
            if (selectionTargetType == SelectionTargetType.Statics)
            {
                HandleStaticSelection(e);
            }
            else
            {
                HandleLandTileSelection(e);
            }
        }

        /// <summary>
        /// Handle land tile selection
        /// </summary>
        private void HandleLandTileSelection(MouseEventArgs e)
        {
            if (!ScreenToMapCoords(e.X, e.Y, out int mapX, out int mapY))
                return;

            var tile = currentMap.Tiles[mapX, mapY];
            if (tile == null) return;

            var selectedTile = new SelectedTile
            {
                X = mapX,
                Y = mapY,
                TileId = tile.TileId,
                Z = tile.Z
            };

            if (ModifierKeys.HasFlag(Keys.Control))
            {
                // Toggle selection
                int index = selectedTiles.FindIndex(t => t.X == mapX && t.Y == mapY);
                if (index >= 0)
                    selectedTiles.RemoveAt(index);
                else
                    selectedTiles.Add(selectedTile);
            }
            else if (ModifierKeys.HasFlag(Keys.Shift) && selectedTiles.Count > 0)
            {
                // Range select (add all tiles between last selected and this one)
                var lastSelected = selectedTiles.Last();
                int minX = Math.Min(lastSelected.X, mapX);
                int maxX = Math.Max(lastSelected.X, mapX);
                int minY = Math.Min(lastSelected.Y, mapY);
                int maxY = Math.Max(lastSelected.Y, mapY);

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        if (x >= 0 && x < currentMap.Width && y >= 0 && y < currentMap.Height)
                        {
                            var t = currentMap.Tiles[x, y];
                            if (t != null)
                            {
                                var st = new SelectedTile { X = x, Y = y, TileId = t.TileId, Z = t.Z };
                                if (!selectedTiles.Contains(st))
                                    selectedTiles.Add(st);
                            }
                        }
                    }
                }
            }
            else
            {
                // Single selection (replace)
                selectedTiles.Clear();
                selectedTiles.Add(selectedTile);
            }

            mapPictureBox.Invalidate();
            UpdateSelectionStatus();
            UpdateSelectionButtons();
        }

        /// <summary>
        /// Handle static item selection using proper visual hit-testing
        /// </summary>
        private void HandleStaticSelection(MouseEventArgs e)
        {
            if (currentStatics == null) return;
            if (mapPictureBox.Image == null) return;

            int viewW = mapPictureBox.Image.Width;
            int viewH = mapPictureBox.Image.Height;

            const int TILE_WIDTH = 44;
            const int TILE_HEIGHT = 44;

            float zoom = zoomFactor / 44f;
            float halfTileW = (TILE_WIDTH / 2f) * zoom;
            float halfTileH = (TILE_HEIGHT / 2f) * zoom;

            // Calculate the visible tile range (same as in GenerateMapImage)
            int tilesAcross = (int)(viewW / halfTileW) + 6;
            int tilesDown = (int)(viewH / halfTileH) + 6;

            int startX = cameraX - tilesAcross / 2;
            int startY = cameraY - tilesDown / 2;
            int endX = cameraX + tilesAcross / 2;
            int endY = cameraY + tilesDown / 2;

            // Collect all visible statics with their screen bounds
            // We need to check them in reverse draw order (front to back) to pick the topmost one
            var staticCandidates = new List<(SelectedStatic staticItem, RectangleF bounds, int drawOrder)>();

            for (int mapY = Math.Max(0, startY); mapY <= Math.Min(currentMap.Height - 1, endY); mapY++)
            {
                for (int mapX = Math.Max(0, startX); mapX <= Math.Min(currentMap.Width - 1, endX); mapX++)
                {
                    var statics = currentStatics.GetStaticsAt(mapX, mapY);
                    foreach (var s in statics)
                    {
                        // Apply Z filter
                        if (!IsStaticInZRange(s.Z))
                            continue;

                        var bounds = GetStaticScreenBounds(mapX, mapY, s.Z, s.ItemId);
                        if (bounds.IsEmpty) continue;

                        // Check if click is within this static's visual bounds
                        if (bounds.Contains(e.X, e.Y))
                        {
                            var selectedStatic = new SelectedStatic
                            {
                                X = mapX,
                                Y = mapY,
                                ItemId = s.ItemId,
                                Z = s.Z,
                                Hue = s.Hue
                            };

                            // Draw order: items drawn later (higher x+y, then higher z) appear on top
                            int drawOrder = (mapX + mapY) * 1000 + (s.Z + 128);
                            staticCandidates.Add((selectedStatic, bounds, drawOrder));
                        }
                    }
                }
            }

            if (staticCandidates.Count == 0)
            {
                // No static under click - optionally clear selection if no modifier
                if (!ModifierKeys.HasFlag(Keys.Control) && !ModifierKeys.HasFlag(Keys.Shift))
                {
                    selectedStatics.Clear();
                    mapPictureBox.Invalidate();
                    UpdateSelectionStatus();
                    UpdateSelectionButtons();
                }
                // Reset cycling state
                lastStaticClickLocation = Point.Empty;
                staticCycleIndex = 0;
                lastStaticCandidates.Clear();
                return;
            }

            // Sort by draw order descending to get the topmost (last drawn) static first
            staticCandidates.Sort((a, b) => b.drawOrder.CompareTo(a.drawOrder));

            // Check if this is a repeated click in approximately the same location (for cycling)
            bool isSameLocation = Math.Abs(e.X - lastStaticClickLocation.X) < 10 && 
                                  Math.Abs(e.Y - lastStaticClickLocation.Y) < 10;
            
            // Determine which static to select
            SelectedStatic clickedStatic;
            
            if (isSameLocation && staticCandidates.Count > 1 && !ModifierKeys.HasFlag(Keys.Control) && !ModifierKeys.HasFlag(Keys.Shift))
            {
                // Cycle to next static in the list
                staticCycleIndex = (staticCycleIndex + 1) % staticCandidates.Count;
                clickedStatic = staticCandidates[staticCycleIndex].staticItem;
                
                // Update status to show cycling info
                statusLabel.Text = $"Target: STATICS | Cycling: {staticCycleIndex + 1}/{staticCandidates.Count} at this location";
            }
            else
            {
                // New location or modifier key held - start fresh
                staticCycleIndex = 0;
                clickedStatic = staticCandidates[0].staticItem;
            }
            
            // Store state for potential cycling on next click
            lastStaticClickLocation = e.Location;
            lastStaticCandidates = staticCandidates;

            if (ModifierKeys.HasFlag(Keys.Control))
            {
                // Toggle selection
                int index = selectedStatics.FindIndex(s => s.Equals(clickedStatic));
                if (index >= 0)
                    selectedStatics.RemoveAt(index);
                else
                    selectedStatics.Add(clickedStatic);
            }
            else if (ModifierKeys.HasFlag(Keys.Shift) && selectedStatics.Count > 0)
            {
                // Range select - add all statics in the rectangular area between last selected and clicked
                var lastSelected = selectedStatics.Last();
                int minX = Math.Min(lastSelected.X, clickedStatic.X);
                int maxX = Math.Max(lastSelected.X, clickedStatic.X);
                int minY = Math.Min(lastSelected.Y, clickedStatic.Y);
                int maxY = Math.Max(lastSelected.Y, clickedStatic.Y);

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        var statics = currentStatics.GetStaticsAt(x, y);
                        foreach (var s in statics)
                        {
                            if (!IsStaticInZRange(s.Z))
                                continue;

                            var ss = new SelectedStatic 
                            { 
                                X = x, 
                                Y = y, 
                                ItemId = s.ItemId, 
                                Z = s.Z, 
                                Hue = s.Hue 
                            };
                            if (!selectedStatics.Contains(ss))
                                selectedStatics.Add(ss);
                        }
                    }
                }
            }
            else
            {
                // Single selection (replace)
                selectedStatics.Clear();
                selectedStatics.Add(clickedStatic);
            }

            mapPictureBox.Invalidate();
            UpdateSelectionStatus();
            UpdateSelectionButtons();
        }

        /// <summary>
        /// Clear tile selection
        /// </summary>
        public void ClearSelection()
        {
            replaceTiles.Clear();
            contextTiles.Clear();
            selectedStatics.Clear();
            mapPictureBox.Invalidate();
            UpdateSelectionStatus();
            UpdateSelectionButtons();
        }

        /// <summary>
        /// Clear selection for current mode only
        /// </summary>
        public void ClearCurrentModeSelection()
        {
            if (selectionTargetType == SelectionTargetType.Statics)
            {
                selectedStatics.Clear();
            }
            else
            {
                if (currentSelectionMode == TileSelectionMode.Replace)
                    replaceTiles.Clear();
                else
                    contextTiles.Clear();
            }
            mapPictureBox.Invalidate();
            UpdateSelectionStatus();
            UpdateSelectionButtons();
        }

        /// <summary>
        /// Switch selection mode (Replace/Context)
        /// </summary>
        public void SetSelectionMode(TileSelectionMode mode)
        {
            currentSelectionMode = mode;
            UpdateSelectionStatus();
        }

        /// <summary>
        /// Switch selection target type (Land/Statics)
        /// </summary>
        public void SetSelectionTargetType(SelectionTargetType targetType)
        {
            selectionTargetType = targetType;
            UpdateSelectionStatus();
        }

        private void UpdateSelectionStatus()
        {
            // Null check for form initialization
            if (statusLabel == null)
                return;

            string targetText = selectionTargetType == SelectionTargetType.Land ? "LAND" : "STATICS";
            
            if (selectionTargetType == SelectionTargetType.Statics)
            {
                if (selectedStatics.Count > 0)
                {
                    statusLabel.Text = $"Target: {targetText} | Selected: {selectedStatics.Count} static(s)";
                }
                else
                {
                    statusLabel.Text = $"Target: {targetText} | Click to select statics | Ctrl-click: Toggle | Shift-click: Range";
                }
            }
            else
            {
                int totalSelected = replaceTiles.Count + contextTiles.Count;
                if (totalSelected > 0)
                {
                    string modeText = currentSelectionMode == TileSelectionMode.Replace ? "REPLACE" : "CONTEXT";
                    statusLabel.Text = $"Target: {targetText} | Mode: {modeText} | Replace: {replaceTiles.Count} | Context: {contextTiles.Count}";
                }
                else
                {
                    string modeText = currentSelectionMode == TileSelectionMode.Replace ? "REPLACE" : "CONTEXT";
                    statusLabel.Text = $"Target: {targetText} | Mode: {modeText} | Left-click: Select | Ctrl-click: Toggle | Shift-click: Range";
                }
            }
        }

        private void UpdateSelectionButtons()
        {
            // Null checks for form initialization
            if (replaceTilesButton == null || restoreOriginalButton == null || clearSelectionButton == null)
                return;

            bool hasLandSelection = replaceTiles.Count > 0 || contextTiles.Count > 0;
            bool hasStaticSelection = selectedStatics.Count > 0;

            // "Replace Selected" button works for both land and statics
            if (selectionTargetType == SelectionTargetType.Statics)
            {
                replaceTilesButton.Enabled = hasStaticSelection;
                replaceTilesButton.Text = "Replace Statics";
            }
            else
            {
                replaceTilesButton.Enabled = replaceTiles.Count > 0;
                replaceTilesButton.Text = "Replace Selected";
            }

            restoreOriginalButton.Enabled = hasLandSelection || hasStaticSelection;
            clearSelectionButton.Enabled = hasLandSelection || hasStaticSelection;

            // Update biome buttons when selection changes
            UpdateBiomeButtons();
        }

        /// <summary>
        /// Handle tile/static replacement button click
        /// </summary>
        private async void ReplaceTilesButton_Click(object sender, EventArgs e)
        {
            if (selectionTargetType == SelectionTargetType.Statics)
            {
                if (selectedStatics.Count == 0)
                    return;
                await ReplaceSelectedStaticsWithAI();
            }
            else
            {
                if (selectedTiles.Count == 0)
                    return;
                await ReplaceSelectedTilesWithAI();
            }
        }

        /// <summary>
        /// Start lasso selection
        /// </summary>
        private void StartLassoSelection(Point startPoint)
        {
            isLassoSelecting = true;
            lassoPoints.Clear();
            lassoPoints.Add(startPoint);
            mapPictureBox.Cursor = Cursors.Cross;
        }

        /// <summary>
        /// Add point to lasso path
        /// </summary>
        private void AddLassoPoint(Point point)
        {
            if (!isLassoSelecting) return;
            
            // Only add point if it's far enough from the last one (reduces points, smoother line)
            if (lassoPoints.Count > 0)
            {
                var last = lassoPoints[lassoPoints.Count - 1];
                double dist = Math.Sqrt(Math.Pow(point.X - last.X, 2) + Math.Pow(point.Y - last.Y, 2));
                if (dist < 5) return; // Minimum 5 pixels between points
            }
            
            lassoPoints.Add(point);
            mapPictureBox.Invalidate(); // Redraw to show lasso path
        }

        /// <summary>
        /// Complete lasso selection and select all items inside the lasso
        /// </summary>
        private void CompleteLassoSelection()
        {
            if (!isLassoSelecting || lassoPoints.Count < 3)
            {
                CancelLassoSelection();
                return;
            }

            isLassoSelecting = false;
            mapPictureBox.Cursor = Cursors.Hand;

            // Create a GraphicsPath from the lasso points to test if points are inside
            using (var lassoPath = new GraphicsPath())
            {
                lassoPath.AddPolygon(lassoPoints.ToArray());
                
                if (selectionTargetType == SelectionTargetType.Statics)
                {
                    SelectStaticsInLasso(lassoPath);
                }
                else
                {
                    SelectLandTilesInLasso(lassoPath);
                }
            }

            lassoPoints.Clear();
            mapPictureBox.Invalidate();
            UpdateSelectionStatus();
            UpdateSelectionButtons();
        }

        /// <summary>
        /// Cancel lasso selection
        /// </summary>
        private void CancelLassoSelection()
        {
            isLassoSelecting = false;
            lassoPoints.Clear();
            mapPictureBox.Cursor = Cursors.Hand;
            mapPictureBox.Invalidate();
        }

        /// <summary>
        /// Select all land tiles that fall within the lasso region
        /// </summary>
        private void SelectLandTilesInLasso(GraphicsPath lassoPath)
        {
            if (currentMap == null || mapPictureBox.Image == null) return;

            int viewW = mapPictureBox.Image.Width;
            int viewH = mapPictureBox.Image.Height;

            const int TILE_WIDTH = 44;
            const int TILE_HEIGHT = 44;

            float zoom = zoomFactor / 44f;
            float halfTileW = (TILE_WIDTH / 2f) * zoom;
            float halfTileH = (TILE_HEIGHT / 2f) * zoom;

            float centerX = viewW / 2f;
            float centerY = viewH / 2f;

            // Calculate visible tile range
            int tilesAcross = (int)(viewW / halfTileW) + 6;
            int tilesDown = (int)(viewH / halfTileH) + 6;

            int startX = cameraX - tilesAcross / 2;
            int startY = cameraY - tilesDown / 2;
            int endX = cameraX + tilesAcross / 2;
            int endY = cameraY + tilesDown / 2;

            // Clear existing selection unless Shift is held
            if (!ModifierKeys.HasFlag(Keys.Shift))
            {
                selectedTiles.Clear();
            }

            // Check each visible tile
            for (int mapY = Math.Max(0, startY); mapY <= Math.Min(currentMap.Height - 1, endY); mapY++)
            {
                for (int mapX = Math.Max(0, startX); mapX <= Math.Min(currentMap.Width - 1, endX); mapX++)
                {
                    var tile = currentMap.Tiles[mapX, mapY];
                    if (tile == null) continue;

                    // Calculate tile center in screen coordinates
                    int relX = mapX - cameraX;
                    int relY = mapY - cameraY;

                    float isoX = (relX - relY) * halfTileW;
                    float isoY = (relX + relY) * halfTileH;

                    float screenX = centerX + isoX;
                    float screenY = centerY + isoY;

                    // Check if tile center is inside the lasso
                    if (lassoPath.IsVisible(screenX, screenY))
                    {
                        var selectedTile = new SelectedTile
                        {
                            X = mapX,
                            Y = mapY,
                            TileId = tile.TileId,
                            Z = tile.Z
                        };

                        if (!selectedTiles.Contains(selectedTile))
                        {
                            selectedTiles.Add(selectedTile);
                        }
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"Lasso selected {selectedTiles.Count} land tiles");
        }

        /// <summary>
        /// Select all statics that fall within the lasso region
        /// </summary>
        private void SelectStaticsInLasso(GraphicsPath lassoPath)
        {
            if (currentMap == null || currentStatics == null || mapPictureBox.Image == null) return;

            int viewW = mapPictureBox.Image.Width;
            int viewH = mapPictureBox.Image.Height;

            const int TILE_WIDTH = 44;
            const int TILE_HEIGHT = 44;

            float zoom = zoomFactor / 44f;
            float halfTileW = (TILE_WIDTH / 2f) * zoom;
            float halfTileH = (TILE_HEIGHT / 2f) * zoom;

            // Calculate visible tile range
            int tilesAcross = (int)(viewW / halfTileW) + 6;
            int tilesDown = (int)(viewH / halfTileH) + 6;

            int startX = cameraX - tilesAcross / 2;
            int startY = cameraY - tilesDown / 2;
            int endX = cameraX + tilesAcross / 2;
            int endY = cameraY + tilesDown / 2;

            // Clear existing selection unless Shift is held
            if (!ModifierKeys.HasFlag(Keys.Shift))
            {
                selectedStatics.Clear();
            }

            // Check each visible static
            for (int mapY = Math.Max(0, startY); mapY <= Math.Min(currentMap.Height - 1, endY); mapY++)
            {
                for (int mapX = Math.Max(0, startX); mapX <= Math.Min(currentMap.Width - 1, endX); mapX++)
                {
                    var statics = currentStatics.GetStaticsAt(mapX, mapY);
                    foreach (var s in statics)
                    {
                        // Apply Z filter
                        if (!IsStaticInZRange(s.Z))
                            continue;

                        // Get the screen bounds for this static
                        var bounds = GetStaticScreenBounds(mapX, mapY, s.Z, s.ItemId);
                        if (bounds.IsEmpty) continue;

                        // Check if the center of the static is inside the lasso
                        float staticCenterX = bounds.X + bounds.Width / 2;
                        float staticCenterY = bounds.Y + bounds.Height / 2;

                        if (lassoPath.IsVisible(staticCenterX, staticCenterY))
                        {
                            var selectedStatic = new SelectedStatic
                            {
                                X = mapX,
                                Y = mapY,
                                ItemId = s.ItemId,
                                Z = s.Z,
                                Hue = s.Hue
                            };

                            if (!selectedStatics.Contains(selectedStatic))
                            {
                                selectedStatics.Add(selectedStatic);
                            }
                        }
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"Lasso selected {selectedStatics.Count} statics");
        }

        /// <summary>
        /// Get all selectable items (statics and/or land tiles) at the given screen location
        /// </summary>
        private List<(string displayText, object item, bool isStatic)> GetSelectableItemsAt(int screenX, int screenY)
        {
            var items = new List<(string displayText, object item, bool isStatic)>();

            if (currentMap == null || mapPictureBox.Image == null) return items;

            int viewW = mapPictureBox.Image.Width;
            int viewH = mapPictureBox.Image.Height;

            const int TILE_WIDTH = 44;
            const int TILE_HEIGHT = 44;

            float zoom = zoomFactor / 44f;
            float halfTileW = (TILE_WIDTH / 2f) * zoom;
            float halfTileH = (TILE_HEIGHT / 2f) * zoom;

            // Calculate visible tile range
            int tilesAcross = (int)(viewW / halfTileW) + 6;
            int tilesDown = (int)(viewH / halfTileH) + 6;

            int startX = cameraX - tilesAcross / 2;
            int startY = cameraY - tilesDown / 2;
            int endX = cameraX + tilesAcross / 2;
            int endY = cameraY + tilesDown / 2;

            // Get the land tile at this location
            if (ScreenToMapCoords(screenX, screenY, out int mapX, out int mapY))
            {
                var tile = currentMap.Tiles[mapX, mapY];
                if (tile != null)
                {
                    var landTile = new SelectedTile
                    {
                        X = mapX,
                        Y = mapY,
                        TileId = tile.TileId,
                        Z = tile.Z
                    };
                    string landText = $"Land Tile 0x{tile.TileId:X4} at ({mapX}, {mapY}) Z:{tile.Z}";
                    items.Add((landText, landTile, false));
                }
            }

            // Get all statics at this location
            if (currentStatics != null)
            {
                var staticCandidates = new List<(SelectedStatic staticItem, int drawOrder)>();

                for (int y = Math.Max(0, startY); y <= Math.Min(currentMap.Height - 1, endY); y++)
                {
                    for (int x = Math.Max(0, startX); x <= Math.Min(currentMap.Width - 1, endX); x++)
                    {
                        var statics = currentStatics.GetStaticsAt(x, y);
                        foreach (var s in statics)
                        {
                            if (!IsStaticInZRange(s.Z))
                                continue;

                            var bounds = GetStaticScreenBounds(x, y, s.Z, s.ItemId);
                            if (bounds.IsEmpty) continue;

                            if (bounds.Contains(screenX, screenY))
                            {
                                var selectedStatic = new SelectedStatic
                                {
                                    X = x,
                                    Y = y,
                                    ItemId = s.ItemId,
                                    Z = s.Z,
                                    Hue = s.Hue
                                };

                                int drawOrder = (x + y) * 1000 + (s.Z + 128);
                                staticCandidates.Add((selectedStatic, drawOrder));
                            }
                        }
                    }
                }

                // Sort by draw order descending (topmost first)
                staticCandidates.Sort((a, b) => b.drawOrder.CompareTo(a.drawOrder));

                foreach (var candidate in staticCandidates)
                {
                    var s = candidate.staticItem;
                    // Get item name from tiledata if available
                    string itemName = "Static";
                    var itemData = tileDataReader.GetItemTile(s.ItemId);
                    if (itemData != null && !string.IsNullOrWhiteSpace(itemData.Name))
                        itemName = itemData.Name;

                    string staticText = $"[Static] {itemName} (0x{s.ItemId:X4}) at ({s.X}, {s.Y}) Z:{s.Z}";
                    items.Add((staticText, s, true));
                }
            }

            return items;
        }

        /// <summary>
        /// Show context menu with all selectable items at the given location
        /// </summary>
        private void ShowSelectionContextMenu(Point screenLocation, Point clickLocation)
        {
            var items = GetSelectableItemsAt(clickLocation.X, clickLocation.Y);

            var contextMenu = new ContextMenuStrip();
            contextMenu.BackColor = HolographicTheme.PanelBackground;
            contextMenu.ForeColor = HolographicTheme.TextPrimary;
            contextMenu.ShowImageMargin = false;

            // ── Place Static option (from palette) ──────────────
            int mapX, mapY;
            bool validPos = ScreenToMapCoords(clickLocation.X, clickLocation.Y, out mapX, out mapY);
            if (validPos && selectedPaletteItemId > 0)
            {
                // Gather all selected palette item IDs
                var selectedIds = new List<ushort>();
                if (paletteListView != null && paletteListView.SelectedIndices.Count > 1)
                {
                    foreach (int idx in paletteListView.SelectedIndices)
                    {
                        if (idx >= 0 && idx < filteredStaticIds.Count)
                            selectedIds.Add(filteredStaticIds[idx]);
                    }
                }
                else
                {
                    selectedIds.Add(selectedPaletteItemId);
                }

                if (selectedIds.Count == 1)
                {
                    string itemName = null;
                    if (tileDataReader != null && tileDataReader.IsLoaded)
                    {
                        var td = tileDataReader.GetItemTile(selectedIds[0]);
                        if (td != null && !string.IsNullOrWhiteSpace(td.Name))
                            itemName = td.Name;
                    }
                    string label = itemName != null
                        ? $"Place \"{itemName}\" (0x{selectedIds[0]:X4}) here"
                        : $"Place 0x{selectedIds[0]:X4} here";

                    var placeItem = new ToolStripMenuItem(label);
                    placeItem.ForeColor = Color.FromArgb(0, 220, 130);
                    placeItem.Font = new Font(contextMenu.Font, FontStyle.Bold);
                    placeItem.Tag = new Tuple<ushort, int, int>(selectedIds[0], mapX, mapY);
                    placeItem.Click += ContextMenu_PlaceStaticClick;
                    contextMenu.Items.Add(placeItem);
                }
                else
                {
                    var placeAllItem = new ToolStripMenuItem($"Place {selectedIds.Count} items here");
                    placeAllItem.ForeColor = Color.FromArgb(0, 220, 130);
                    placeAllItem.Font = new Font(contextMenu.Font, FontStyle.Bold);
                    placeAllItem.Tag = new Tuple<List<ushort>, int, int>(selectedIds, mapX, mapY);
                    placeAllItem.Click += ContextMenu_PlaceMultipleStaticsClick;
                    contextMenu.Items.Add(placeAllItem);
                }
            }

            if (items.Count == 0 && contextMenu.Items.Count == 0)
                return;

            if (items.Count > 0)
            {
                if (contextMenu.Items.Count > 0)
                    contextMenu.Items.Add(new ToolStripSeparator());

                // Add header
                var headerItem = new ToolStripLabel($"Items at this location ({items.Count}):");
                headerItem.ForeColor = HolographicTheme.SelectionCyan;
                headerItem.Font = new Font(headerItem.Font, FontStyle.Bold);
                contextMenu.Items.Add(headerItem);
                contextMenu.Items.Add(new ToolStripSeparator());

                // Add each item
                int index = 0;
                foreach (var item in items)
                {
                    var menuItem = new ToolStripMenuItem(item.displayText);
                    menuItem.Tag = item;
                    menuItem.ForeColor = item.isStatic ? Color.Magenta : HolographicTheme.TextPrimary;
                    menuItem.Click += ContextMenu_ItemClick;

                    if (index < 9)
                        menuItem.ShortcutKeyDisplayString = $"[{index + 1}]";

                    contextMenu.Items.Add(menuItem);
                    index++;
                }

                // "Select All Statics" option if multiple items
                if (items.Count > 1)
                {
                    contextMenu.Items.Add(new ToolStripSeparator());

                    var selectAllStaticsItem = new ToolStripMenuItem("Select All Statics Here");
                    selectAllStaticsItem.ForeColor = Color.Magenta;
                    selectAllStaticsItem.Tag = items.Where(i => i.isStatic).Select(i => i.item).ToList();
                    selectAllStaticsItem.Click += ContextMenu_SelectAllStaticsClick;
                    contextMenu.Items.Add(selectAllStaticsItem);
                }

                // Delete selected statics option
                if (selectedStatics.Count > 0)
                {
                    contextMenu.Items.Add(new ToolStripSeparator());
                    var deleteItem = new ToolStripMenuItem($"Delete {selectedStatics.Count} selected static(s)");
                    deleteItem.ForeColor = Color.OrangeRed;
                    deleteItem.Click += ContextMenu_DeleteSelectedStaticsClick;
                    contextMenu.Items.Add(deleteItem);
                }
            }

            contextMenu.Show(mapPictureBox, clickLocation);
        }

        private void ContextMenu_PlaceStaticClick(object sender, EventArgs e)
        {
            var menuItem = sender as ToolStripMenuItem;
            var data = menuItem?.Tag as Tuple<ushort, int, int>;
            if (data == null) return;

            sbyte z = GetZAt(data.Item2, data.Item3);
            AddStaticToMap(data.Item1, data.Item2, data.Item3, z, 0);
        }

        private void ContextMenu_PlaceMultipleStaticsClick(object sender, EventArgs e)
        {
            var menuItem = sender as ToolStripMenuItem;
            var data = menuItem?.Tag as Tuple<List<ushort>, int, int>;
            if (data == null) return;

            PlaceMultipleStaticsSpaced(data.Item1, data.Item2, data.Item3);
        }

        private void ContextMenu_DeleteSelectedStaticsClick(object sender, EventArgs e)
        {
            if (selectedStatics.Count == 0 || currentMap == null) return;

            foreach (var sel in selectedStatics)
            {
                var key = (sel.X, sel.Y);

                // Snapshot before
                bool hadBefore = staticOverrides.ContainsKey(key);
                List<StaticTile> oldList = hadBefore
                    ? MapAction.CloneStaticList(staticOverrides[key])
                    : null;

                if (!staticOverrides.ContainsKey(key))
                {
                    var existing = new List<StaticTile>();
                    if (currentStatics != null && currentStatics.StaticsByPosition.ContainsKey(key))
                    {
                        foreach (var s in currentStatics.StaticsByPosition[key])
                            existing.Add(new StaticTile
                            {
                                ItemId = s.ItemId, X = s.X, Y = s.Y,
                                Z = s.Z, Hue = s.Hue,
                                WorldX = s.WorldX, WorldY = s.WorldY
                            });
                    }
                    staticOverrides[key] = existing;
                }

                // Remove matching static
                staticOverrides[key].RemoveAll(s =>
                    s.ItemId == sel.ItemId && s.Z == sel.Z && s.Hue == sel.Hue);

                // Record undo
                var action = new MapAction { Description = $"Delete static 0x{sel.ItemId:X4} at ({sel.X},{sel.Y})" };
                action.StaticChanges[key] = new StaticOverrideChange
                {
                    HadOverrideBefore = hadBefore,
                    OldOverride = oldList,
                    NewOverride = MapAction.CloneStaticList(staticOverrides[key])
                };
                undoRedoManager.RecordAction(action);
            }

            int count = selectedStatics.Count;
            selectedStatics.Clear();
            hasUnsavedChanges = true;
            cachedStaticsData = null;
            GenerateMapImage();
            UpdateSaveButtonState();
            UpdateUndoRedoButtons();
            statusLabel.Text = $"Deleted {count} static(s)";
        }

        private void ContextMenu_ItemClick(object sender, EventArgs e)
        {
            var menuItem = sender as ToolStripMenuItem;
            if (menuItem?.Tag == null) return;

            var itemData = ((string displayText, object item, bool isStatic))menuItem.Tag;

            if (itemData.isStatic && itemData.item is SelectedStatic selectedStatic)
            {
                // Select the static
                if (!ModifierKeys.HasFlag(Keys.Control))
                {
                    selectedStatics.Clear();
                }
                if (!selectedStatics.Contains(selectedStatic))
                {
                    selectedStatics.Add(selectedStatic);
                }
                
                // Switch to statics mode
                if (selectionTargetType != SelectionTargetType.Statics)
                {
                    selectStaticsRadioButton.Checked = true;
                }
            }
            else if (!itemData.isStatic && itemData.item is SelectedTile selectedTile)
            {
                // Select the land tile
                if (!ModifierKeys.HasFlag(Keys.Control))
                {
                    selectedTiles.Clear();
                }
                if (!selectedTiles.Contains(selectedTile))
                {
                    selectedTiles.Add(selectedTile);
                }
                
                // Switch to land mode
                if (selectionTargetType != SelectionTargetType.Land)
                {
                    selectLandRadioButton.Checked = true;
                }
            }

            mapPictureBox.Invalidate();
            UpdateSelectionStatus();
            UpdateSelectionButtons();
        }

        private void ContextMenu_SelectAllStaticsClick(object sender, EventArgs e)
        {
            var menuItem = sender as ToolStripMenuItem;
            if (menuItem?.Tag == null) return;

            var staticsList = menuItem.Tag as List<object>;
            if (staticsList == null) return;

            if (!ModifierKeys.HasFlag(Keys.Control))
            {
                selectedStatics.Clear();
            }

            foreach (var item in staticsList)
            {
                if (item is SelectedStatic ss && !selectedStatics.Contains(ss))
                {
                    selectedStatics.Add(ss);
                }
            }

            // Switch to statics mode
            if (selectionTargetType != SelectionTargetType.Statics)
            {
                selectStaticsRadioButton.Checked = true;
            }

            mapPictureBox.Invalidate();
            UpdateSelectionStatus();
            UpdateSelectionButtons();
        }

        // ================================================================
        //  Copy / Paste
        // ================================================================

        /// <summary>
        /// Copy the current selection (land tiles and/or statics) into the
        /// internal clipboard. When in Land mode the selected land tiles and
        /// all statics within the selection bounds are copied.  When in
        /// Statics mode only the selected statics are copied.
        /// </summary>
        internal void CopySelection()
        {
            if (currentMap == null) return;

            var clip = new MapClipboardData();

            if (selectionTargetType == SelectionTargetType.Statics && selectedStatics.Count > 0)
            {
                // --- Statics-only copy ---
                int minX = selectedStatics.Min(s => s.X);
                int minY = selectedStatics.Min(s => s.Y);
                int maxX = selectedStatics.Max(s => s.X);
                int maxY = selectedStatics.Max(s => s.Y);

                clip.Width = maxX - minX + 1;
                clip.Height = maxY - minY + 1;

                foreach (var s in selectedStatics)
                {
                    clip.Statics.Add(new ClipboardStaticTile
                    {
                        RelativeX = s.X - minX,
                        RelativeY = s.Y - minY,
                        ItemId = s.ItemId,
                        Z = s.Z,
                        Hue = s.Hue
                    });
                }
            }
            else if (selectionTargetType == SelectionTargetType.Land && selectedTiles.Count > 0)
            {
                // --- Land copy (+ statics in the same bounds) ---
                int minX = selectedTiles.Min(t => t.X);
                int minY = selectedTiles.Min(t => t.Y);
                int maxX = selectedTiles.Max(t => t.X);
                int maxY = selectedTiles.Max(t => t.Y);

                clip.Width = maxX - minX + 1;
                clip.Height = maxY - minY + 1;

                // Copy each selected land tile
                foreach (var t in selectedTiles)
                {
                    clip.LandTiles.Add(new ClipboardLandTile
                    {
                        RelativeX = t.X - minX,
                        RelativeY = t.Y - minY,
                        TileId = t.TileId,
                        Z = t.Z
                    });
                }

                // Also copy statics within the selection bounds
                if (currentStatics != null)
                {
                    var selectedPositions = new HashSet<(int, int)>();
                    foreach (var t in selectedTiles)
                        selectedPositions.Add((t.X, t.Y));

                    foreach (var pos in selectedPositions)
                    {
                        var statics = currentStatics.GetStaticsAt(pos.Item1, pos.Item2);
                        // Also include any overrides at this position
                        List<StaticTile> overrides;
                        if (staticOverrides.TryGetValue(pos, out overrides))
                            statics = overrides;

                        foreach (var s in statics)
                        {
                            clip.Statics.Add(new ClipboardStaticTile
                            {
                                RelativeX = pos.Item1 - minX,
                                RelativeY = pos.Item2 - minY,
                                ItemId = s.ItemId,
                                Z = s.Z,
                                Hue = s.Hue
                            });
                        }
                    }
                }
            }
            else
            {
                if (statusLabel != null)
                    statusLabel.Text = "Nothing selected to copy.";
                return;
            }

            clipboard = clip;

            if (statusLabel != null)
                statusLabel.Text = $"Copied {clip.LandTiles.Count} land tile(s), {clip.Statics.Count} static(s) ({clip.Width}x{clip.Height})";
        }

        /// <summary>
        /// Paste the clipboard contents at the current camera centre.
        /// Land tiles overwrite the map directly; statics are added via
        /// the override system.  The operation is recorded for undo.
        /// </summary>
        internal void PasteAtCamera()
        {
            if (clipboard == null || clipboard.IsEmpty)
            {
                if (statusLabel != null)
                    statusLabel.Text = "Clipboard is empty — copy a selection first (Ctrl+C).";
                return;
            }
            if (currentMap == null) return;

            // Paste centred on the camera position
            int originX = cameraX - clipboard.Width / 2;
            int originY = cameraY - clipboard.Height / 2;

            var action = new MapAction { Description = $"Paste ({clipboard.LandTiles.Count} land, {clipboard.Statics.Count} statics)" };

            // Collect all affected positions for override snapshots
            var affectedPositions = new HashSet<(int, int)>();
            foreach (var lt in clipboard.LandTiles)
                affectedPositions.Add((originX + lt.RelativeX, originY + lt.RelativeY));
            foreach (var st in clipboard.Statics)
                affectedPositions.Add((originX + st.RelativeX, originY + st.RelativeY));

            foreach (var pos in affectedPositions)
                CaptureOverrideState(action, pos);

            // --- Paste land tiles ---
            int tilesApplied = 0;
            foreach (var lt in clipboard.LandTiles)
            {
                int worldX = originX + lt.RelativeX;
                int worldY = originY + lt.RelativeY;
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
                            OldTileId = tile.TileId,
                            OldZ = tile.Z,
                            NewTileId = lt.TileId,
                            NewZ = lt.Z
                        };
                    }

                    tile.TileId = lt.TileId;
                    tile.Z = lt.Z;
                    tilesApplied++;
                }
            }

            // --- Paste statics ---
            // Clear existing statics at each affected position, then add pasted ones
            var staticPositions = new HashSet<(int, int)>();
            foreach (var st in clipboard.Statics)
                staticPositions.Add((originX + st.RelativeX, originY + st.RelativeY));

            foreach (var pos in staticPositions)
            {
                if (pos.Item1 < 0 || pos.Item1 >= currentMap.Width || pos.Item2 < 0 || pos.Item2 >= currentMap.Height)
                    continue;
                staticOverrides[pos] = new List<StaticTile>();
            }

            int staticsApplied = 0;
            foreach (var st in clipboard.Statics)
            {
                int worldX = originX + st.RelativeX;
                int worldY = originY + st.RelativeY;
                if (worldX < 0 || worldX >= currentMap.Width || worldY < 0 || worldY >= currentMap.Height)
                    continue;

                var key = (worldX, worldY);
                if (!staticOverrides.ContainsKey(key))
                    staticOverrides[key] = new List<StaticTile>();

                staticOverrides[key].Add(new StaticTile
                {
                    ItemId = st.ItemId,
                    X = (byte)(worldX % 8),
                    Y = (byte)(worldY % 8),
                    Z = st.Z,
                    Hue = st.Hue,
                    WorldX = worldX,
                    WorldY = worldY
                });
                staticsApplied++;
            }

            // Finalize and record for undo
            FinalizeOverrideState(action);
            undoRedoManager.RecordAction(action);

            hasUnsavedChanges = true;
            InvalidateStaticsCache();
            InvalidateMinimaps();
            GenerateMapImage();
            UpdateSaveButtonState();
            UpdateUndoRedoButtons();

            if (statusLabel != null)
                statusLabel.Text = $"Pasted {tilesApplied} land tile(s), {staticsApplied} static(s) at ({originX}, {originY}) (unsaved)";
        }
    }
}
