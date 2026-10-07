using System;
using System.Drawing;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    partial class MapViewerForm
    {
        // Track potential lasso vs single click for CTRL operations
        private bool isPotentialLasso = false;
        private Point potentialLassoStartPoint;
        private const int LASSO_DRAG_THRESHOLD = 5; // Pixels of movement before starting lasso

        // Biome paint drag state
        private bool isBiomePainting = false;

        // Height tool paint drag state
        private bool isHeightPainting = false;

        // Panning render throttle
        private Timer panRenderTimer;
        private bool panRenderPending = false;
        private const int PAN_RENDER_INTERVAL_MS = 30; // ~33 FPS cap during panning

        private void MapPictureBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                // Right-click = show context menu with selectable items, or pan
                // Also show when a palette item is selected (for placing statics)
                var items = GetSelectableItemsAt(e.X, e.Y);
                if (items.Count > 0 || selectedPaletteItemId > 0)
                {
                    ShowSelectionContextMenu(mapPictureBox.PointToScreen(e.Location), e.Location);
                }
                else
                {
                    // No items under cursor and no palette selection, start panning
                    isPanning = true;
                    panStartPoint = e.Location;
                    panStartX = cameraX;
                    panStartY = cameraY;
                    mapPictureBox.Cursor = Cursors.SizeAll;
                }
            }
            else if (e.Button == MouseButtons.Middle)
            {
                // Middle-click = always panning (for when right-click is used for context menu)
                isPanning = true;
                panStartPoint = e.Location;
                panStartX = cameraX;
                panStartY = cameraY;
                mapPictureBox.Cursor = Cursors.SizeAll;
            }
            else if (e.Button == MouseButtons.Left)
            {
                if (isBiomePaintMode && selectedBiomeBrush != null)
                {
                    // Biome paint mode: paint at click location
                    if (ScreenToMapCoords(e.X, e.Y, out int paintX, out int paintY))
                    {
                        isBiomePainting = true;
                        ApplyBiomeAtLocation(paintX, paintY);
                    }
                }
                else if (activeHeightTool != HeightTool.None && activeHeightTool != HeightTool.Ramp)
                {
                    // Height tool paint mode: apply tool at click location
                    if (ScreenToMapCoords(e.X, e.Y, out int htX, out int htY))
                    {
                        isHeightPainting = true;
                        ApplyHeightToolAtLocation(htX, htY);
                    }
                }
                else if (ModifierKeys.HasFlag(Keys.Control))
                {
                    // CTRL + Left-click: Could be single click to add, or drag to lasso
                    // We'll wait to see if user drags before starting lasso
                    isPotentialLasso = true;
                    potentialLassoStartPoint = e.Location;
                }
                else
                {
                    // Regular left-click = tile selection
                    HandleTileSelection(e);
                }
            }
        }

        private void MapPictureBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (isPanning)
            {
                int screenDX = panStartPoint.X - e.X;
                int screenDY = panStartPoint.Y - e.Y;

                const int TILE_WIDTH = 44;
                const int TILE_HEIGHT = 44;
                float zoom = zoomFactor / 44f;
                float halfTileW = (TILE_WIDTH / 2f) * zoom;
                float halfTileH = (TILE_HEIGHT / 2f) * zoom;

                float isoX = screenDX / halfTileW;
                float isoY = screenDY / halfTileH;

                int mapDX = (int)Math.Round((isoX + isoY) / 2f);
                int mapDY = (int)Math.Round((isoY - isoX) / 2f);

                cameraX = panStartX + mapDX;
                cameraY = panStartY + mapDY;

                cameraX = Math.Max(0, Math.Min(currentMap.Width - 1, cameraX));
                cameraY = Math.Max(0, Math.Min(currentMap.Height - 1, cameraY));

                // Throttle rendering during panning to avoid a full re-render
                // on every mouse-move event.
                if (panRenderTimer == null)
                {
                    panRenderTimer = new Timer { Interval = PAN_RENDER_INTERVAL_MS };
                    panRenderTimer.Tick += (s, a) =>
                    {
                        panRenderTimer.Stop();
                        if (panRenderPending)
                        {
                            panRenderPending = false;
                            GenerateMapImage();
                        }
                    };
                }

                if (!panRenderTimer.Enabled)
                {
                    // First movement — render immediately and start cooldown
                    panRenderPending = false;
                    panRenderTimer.Start();
                    GenerateMapImage();
                }
                else
                {
                    // Timer still running — just mark a render pending
                    panRenderPending = true;
                }
            }
            else if (isBiomePainting)
            {
                // Continue painting biome as user drags
                if (ScreenToMapCoords(e.X, e.Y, out int paintX, out int paintY))
                {
                    ApplyBiomeAtLocation(paintX, paintY);
                }
            }
            else if (isHeightPainting)
            {
                // Continue painting height tool as user drags
                if (ScreenToMapCoords(e.X, e.Y, out int htX, out int htY))
                {
                    ApplyHeightToolAtLocation(htX, htY);
                }
            }
            else if (isPotentialLasso)
            {
                // Check if we've moved enough to start a lasso selection
                double distance = Math.Sqrt(
                    Math.Pow(e.X - potentialLassoStartPoint.X, 2) +
                    Math.Pow(e.Y - potentialLassoStartPoint.Y, 2));

                if (distance >= LASSO_DRAG_THRESHOLD)
                {
                    // User is dragging - start lasso selection
                    isPotentialLasso = false;
                    StartLassoSelection(potentialLassoStartPoint);
                    AddLassoPoint(e.Location);
                }
            }
            else if (isLassoSelecting)
            {
                // Add point to lasso path while dragging
                AddLassoPoint(e.Location);
            }
        }

        private void MapPictureBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
            {
                isPanning = false;
                mapPictureBox.Cursor = (isBiomePaintMode || activeHeightTool != HeightTool.None) ? Cursors.Cross : Cursors.Hand;
                // Flush any pending pan render
                if (panRenderTimer != null)
                {
                    panRenderTimer.Stop();
                    if (panRenderPending)
                    {
                        panRenderPending = false;
                        GenerateMapImage();
                    }
                }
            }
            else if (e.Button == MouseButtons.Left)
            {
                if (isBiomePainting)
                {
                    isBiomePainting = false;
                    // Refresh the minimap overlay now that the paint stroke is done
                    GenerateMinimapOverlay();
                }
                else if (isHeightPainting)
                {
                    isHeightPainting = false;
                    // Refresh the minimap overlay after height painting
                    GenerateMinimapOverlay();
                }
                else if (isPotentialLasso)
                {
                    // User clicked without dragging enough - treat as CTRL+click to add/toggle
                    isPotentialLasso = false;
                    // Create a fake MouseEventArgs with the original click location
                    var clickEvent = new MouseEventArgs(e.Button, e.Clicks,
                        potentialLassoStartPoint.X, potentialLassoStartPoint.Y, e.Delta);
                    HandleTileSelection(clickEvent);
                }
                else if (isLassoSelecting)
                {
                    // Complete the lasso selection
                    CompleteLassoSelection();
                }
            }
        }

        private void MapPictureBox_MouseWheel(object sender, MouseEventArgs e)
        {
            // Smaller steps for finer control
            if (e.Delta > 0)
                ChangeZoom(25);  // Zoom in
            else
                ChangeZoom(-25); // Zoom out
        }

        /// <summary>
        /// Convert trackbar value (0-1000) to actual zoom factor using exponential curve.
        /// This gives fine control at low zoom (0.07-5) and reasonable steps at high zoom (50-128).
        /// </summary>
        private float TrackBarToZoom(int trackValue)
        {
            // Exponential mapping: zoom = minZoom * (maxZoom/minZoom)^(value/max)
            // This gives: value=0 -> 0.07, value=500 -> ~3.0, value=1000 -> 128
            const float minZoom = 0.07f;
            const float maxZoom = 128f;
            float t = trackValue / 1000f;
            return minZoom * (float)Math.Pow(maxZoom / minZoom, t);
        }

        /// <summary>
        /// Convert actual zoom factor to trackbar value (0-1000)
        /// </summary>
        private int ZoomToTrackBar(float zoom)
        {
            const float minZoom = 0.07f;
            const float maxZoom = 128f;
            // Inverse of exponential: t = log(zoom/minZoom) / log(maxZoom/minZoom)
            float t = (float)(Math.Log(zoom / minZoom) / Math.Log(maxZoom / minZoom));
            return (int)Math.Round(t * 1000);
        }

        private void ZoomTrackBar_ValueChanged(object sender, EventArgs e)
        {
            zoomFactor = TrackBarToZoom(zoomTrackBar.Value);
            zoomLabel.Text = $"Zoom: {zoomFactor:F1}px";
            GenerateMapImage();
        }

        private void ChangeZoom(int delta)
        {
            int newValue = zoomTrackBar.Value + delta;
            if (newValue < zoomTrackBar.Minimum) newValue = zoomTrackBar.Minimum;
            if (newValue > zoomTrackBar.Maximum) newValue = zoomTrackBar.Maximum;
            zoomTrackBar.Value = newValue;
        }

        private void GoToButton_Click(object sender, EventArgs e)
        {
            if (int.TryParse(coordXTextBox.Text, out int x) && int.TryParse(coordYTextBox.Text, out int y))
            {
                GoToLocation(x, y);
            }
        }

        internal void GoToLocation(int x, int y)
        {
            if (currentMap == null) return;

            cameraX = Math.Max(0, Math.Min(currentMap.Width - 1, x));
            cameraY = Math.Max(0, Math.Min(currentMap.Height - 1, y));

            GenerateMapImage();
        }
    }
}
