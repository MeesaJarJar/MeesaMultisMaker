using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MeesaMultisMaker.Utils;

namespace MeesaMultisMaker
{
    public partial class GumpEditorForm
    {
        /// <summary>
        /// Handle drag enter on canvas - show copy cursor if valid gump data
        /// </summary>
        private void CanvasBox_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(int)) || e.Data.GetDataPresent(typeof(int[])))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private Bitmap GetBitmapFromClipboardPreserveAlpha()
        {
            try
            {
                IDataObject dataObject = Clipboard.GetDataObject();
                if (dataObject != null && dataObject.GetDataPresent("PNG"))
                {
                    var pngData = dataObject.GetData("PNG");
                    Stream stream = pngData as Stream;
                    if (stream == null && pngData is byte[] bytes)
                    {
                        stream = new MemoryStream(bytes);
                    }

                    if (stream != null)
                    {
                        using (stream)
                        using (var img = Image.FromStream(stream, true, true))
                        {
                            var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                            using (var g = Graphics.FromImage(bmp))
                            {
                                g.Clear(Color.Transparent);
                                g.CompositingMode = CompositingMode.SourceCopy;
                                g.DrawImage(img, 0, 0, img.Width, img.Height);
                            }
                            return bmp;
                        }
                    }
                }
            }
            catch
            {
            }

            if (Clipboard.ContainsImage())
            {
                var img = Clipboard.GetImage();
                if (img != null)
                {
                    var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.DrawImage(img, 0, 0, img.Width, img.Height);
                    }
                    return bmp;
                }
            }

            return null;
        }

        /// <summary>
        /// Handle drag over canvas - keep showing copy cursor
        /// </summary>
        private void CanvasBox_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(int)) || e.Data.GetDataPresent(typeof(int[])))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        /// <summary>
        /// Handle drop on canvas - add the gump(s) at the drop location
        /// </summary>
        private void CanvasBox_DragDrop(object sender, DragEventArgs e)
        {
            // Convert screen coordinates to canvas coordinates
            Point screenPoint = new Point(e.X, e.Y);
            Point canvasPoint = canvasBox.PointToClient(screenPoint);

            // Convert to canvas coordinates (accounting for zoom)
            int cx = (int)(canvasPoint.X / zoom);
            int cy = (int)(canvasPoint.Y / zoom);

            // Check for multiple gump IDs (int array)
            if (e.Data.GetDataPresent(typeof(int[])))
            {
                int[] gumpIds = (int[])e.Data.GetData(typeof(int[]));

                // Add all gumps, offset each one slightly so they're not all stacked
                int offsetX = 0;
                int offsetY = 0;
                const int OFFSET_STEP = 20; // Pixels between each dropped item

                foreach (int gumpId in gumpIds)
                {
                    AddGumpToCanvas(gumpId, cx + offsetX, cy + offsetY);
                    offsetX += OFFSET_STEP;
                    offsetY += OFFSET_STEP;
                }

                SetStatus($"Dropped {gumpIds.Length} GUMP(s) at ({cx}, {cy})");
            }
            // Check for single gump ID (int)
            else if (e.Data.GetDataPresent(typeof(int)))
            {
                int gumpId = (int)e.Data.GetData(typeof(int));

                // Add the gump at the drop location
                AddGumpToCanvas(gumpId, cx, cy);

                SetStatus($"Dropped GUMP 0x{gumpId:X4} at ({cx}, {cy})");
            }
        }

        private void AddGumpToCanvas(int gumpId, int x, int y)
        {
            if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count)
                return;

            var layer = layers[activeLayerIndex];
            if (layer.Locked)
            {
                SetStatus("Layer is locked!");
                return;
            }

            PushUndo();

            var bmp = GetGumpImage(gumpId);
            if (bmp == null)
            {
                SetStatus($"Failed to load GUMP 0x{gumpId:X4}");
                return;
            }

            var gump = new PlacedGump
            {
                GumpId = gumpId,
                Image = new Bitmap(bmp),
                X = x - bmp.Width / 2,
                Y = y - bmp.Height / 2,
                LayerIndex = activeLayerIndex
            };

            // Snap to grid
            if (GetSnapToGrid())
            {
                gump.X = (gump.X / gridSize) * gridSize;
                gump.Y = (gump.Y / gridSize) * gridSize;
            }

            layer.Gumps.Add(gump);
            placedGumps.Add(gump);
            selectedGump = gump;

            canvasBox.Invalidate();
            SetStatus($"Added GUMP 0x{gumpId:X4} at ({gump.X}, {gump.Y})");
        }

        public void AddCustomImage(Bitmap bmp)
        {
            if (bmp == null) return;
            if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count) return;

            var layer = layers[activeLayerIndex];
            if (layer.Locked)
            {
                SetStatus("Layer is locked!");
                return;
            }

            PushUndo();

            var imgCopy = new Bitmap(bmp);
            var gump = new PlacedGump
            {
                GumpId = -1,
                Image = imgCopy,
                X = canvasWidth / 2 - imgCopy.Width / 2,
                Y = canvasHeight / 2 - imgCopy.Height / 2,
                LayerIndex = activeLayerIndex
            };

            if (GetSnapToGrid())
            {
                gump.X = (gump.X / gridSize) * gridSize;
                gump.Y = (gump.Y / gridSize) * gridSize;
            }

            layer.Gumps.Add(gump);
            placedGumps.Add(gump);
            selectedGump = gump;

            canvasBox.Invalidate();
            SetStatus("Added image from Painter");
        }

        private bool GetSnapToGrid()
        {
            return _snapToGrid;
        }

        private void CreateCanvas()
        {
            canvasBox.Width = (int)(canvasWidth * zoom);
            canvasBox.Height = (int)(canvasHeight * zoom);
            CenterCanvas();
            canvasBox.Invalidate();
        }

        private void UpdateCanvasSize()
        {
            canvasBox.Width = (int)(canvasWidth * zoom);
            canvasBox.Height = (int)(canvasHeight * zoom);
            CenterCanvas();
            canvasBox.Invalidate();
        }

        private void CenterCanvas()
        {
            if (canvasPanel == null || canvasBox == null) return;

            int x = Math.Max(0, (canvasPanel.ClientSize.Width - canvasBox.Width) / 2);
            int y = Math.Max(0, (canvasPanel.ClientSize.Height - canvasBox.Height) / 2);
            canvasBox.Location = new Point(x + panOffset.X, y + panOffset.Y);
        }

        private void CanvasBox_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // Background
            g.Clear(Color.FromArgb(60, 60, 65));

            // Draw grid
            if (showGrid)
            {
                using (var gridPen = new Pen(Color.FromArgb(40, 100, 200, 200)))
                {
                    float scaledGrid = gridSize * zoom;
                    for (float x = 0; x < canvasBox.Width; x += scaledGrid)
                    {
                        g.DrawLine(gridPen, x, 0, x, canvasBox.Height);
                    }
                    for (float y = 0; y < canvasBox.Height; y += scaledGrid)
                    {
                        g.DrawLine(gridPen, 0, y, canvasBox.Width, y);
                    }
                }
            }

            // Draw placed gumps from all visible layers
            foreach (var layer in layers)
            {
                if (!layer.Visible) continue;

                foreach (var gump in layer.Gumps)
                {
                    if (!ImageHelper.IsValidImage(gump.Image)) continue;

                    float dx = gump.X * zoom;
                    float dy = gump.Y * zoom;
                    float dw = gump.Image.Width * zoom * gump.ScaleX;
                    float dh = gump.Image.Height * zoom * gump.ScaleY;

                    // Save current transform
                    var savedTransform = g.Transform;

                    // Apply transformations if needed
                    bool hasTransform = Math.Abs(gump.Rotation) > 0.01f || Math.Abs(gump.SkewX) > 0.01f || Math.Abs(gump.SkewY) > 0.01f;
                    if (hasTransform)
                    {
                        float centerX = dx + dw / 2;
                        float centerY = dy + dh / 2;

                        var matrix = new Matrix();
                        matrix.Translate(centerX, centerY);

                        // Apply rotation
                        if (Math.Abs(gump.Rotation) > 0.01f)
                        {
                            matrix.Rotate(gump.Rotation);
                        }

                        // Apply skew
                        if (Math.Abs(gump.SkewX) > 0.01f || Math.Abs(gump.SkewY) > 0.01f)
                        {
                            matrix.Shear(gump.SkewX, gump.SkewY);
                        }

                        matrix.Translate(-centerX, -centerY);
                        g.MultiplyTransform(matrix);
                        matrix.Dispose();
                    }

                    // Apply layer opacity
                    if (layer.Opacity < 1.0f)
                    {
                        var cm = new ColorMatrix { Matrix33 = layer.Opacity };
                        var ia = new ImageAttributes();
                        ia.SetColorMatrix(cm);
                        g.DrawImage(gump.Image,
                            new Rectangle((int)dx, (int)dy, (int)dw, (int)dh),
                            0, 0, gump.Image.Width, gump.Image.Height,
                            GraphicsUnit.Pixel, ia);
                    }
                    else
                    {
                        g.DrawImage(gump.Image, dx, dy, dw, dh);
                    }

                    // Restore transform before drawing selection
                    if (hasTransform)
                    {
                        g.Transform = savedTransform;
                    }

                    // Draw selection highlight and handles
                    bool isSelected = selectedGumps.Contains(gump);
                    if (isSelected)
                    {
                        DrawSelectionWithHandles(g, gump, gump == selectedGump);
                    }
                    else if (gump == hoveredGump)
                    {
                        using (var hoverPen = new Pen(Color.FromArgb(128, HolographicTheme.CyanAccent), 1))
                        {
                            g.DrawRectangle(hoverPen, dx, dy, dw, dh);
                        }
                    }
                }
            }

            // Draw canvas border
            using (var borderPen = new Pen(HolographicTheme.BorderCyan, 2))
            {
                g.DrawRectangle(borderPen, 0, 0, canvasBox.Width - 1, canvasBox.Height - 1);
            }

            // Draw selection rectangle if active
            if (isSelecting)
            {
                using (var selBrush = new SolidBrush(Color.FromArgb(80, HolographicTheme.CyanAccent)))
                {
                    g.FillRectangle(selBrush, selectionRect);
                }

                using (var selPen = new Pen(HolographicTheme.CyanAccent, 2))
                {
                    g.DrawRectangle(selPen, selectionRect);
                }
            }
        }

        private void DrawSelectionWithHandles(Graphics g, PlacedGump gump, bool isPrimary)
        {
            if (!ImageHelper.IsValidImage(gump.Image)) return;

            float dx = gump.X * zoom;
            float dy = gump.Y * zoom;
            float dw = gump.Image.Width * zoom * gump.ScaleX;
            float dh = gump.Image.Height * zoom * gump.ScaleY;

            // Draw selection border
            using (var selPen = new Pen(HolographicTheme.CyanAccent, isPrimary ? 2 : 1))
            {
                if (Math.Abs(gump.Rotation) > 0.01f || Math.Abs(gump.SkewX) > 0.01f || Math.Abs(gump.SkewY) > 0.01f)
                {
                    // Draw transformed outline
                    var corners = gump.GetTransformedCorners();
                    var zoomedCorners = corners.Select(p => new PointF(p.X * zoom, p.Y * zoom)).ToArray();
                    g.DrawPolygon(selPen, zoomedCorners);
                }
                else
                {
                    g.DrawRectangle(selPen, dx - 1, dy - 1, dw + 2, dh + 2);
                }
            }

            // Draw resize handles (only for primary selection)
            if (isPrimary)
            {
                const int handleSize = 8;
                using (var handleBrush = new SolidBrush(Color.White))
                using (var handlePen = new Pen(HolographicTheme.CyanAccent, 1))
                {
                    // 8 handles: corners and edge midpoints
                    PointF[] handles = new PointF[]
                    {
                        new PointF(dx, dy),                          // 0: Top-left
                        new PointF(dx + dw/2, dy),                   // 1: Top-center
                        new PointF(dx + dw, dy),                     // 2: Top-right
                        new PointF(dx + dw, dy + dh/2),              // 3: Right-center
                        new PointF(dx + dw, dy + dh),                // 4: Bottom-right
                        new PointF(dx + dw/2, dy + dh),              // 5: Bottom-center
                        new PointF(dx, dy + dh),                     // 6: Bottom-left
                        new PointF(dx, dy + dh/2),                   // 7: Left-center
                    };

                    foreach (var handle in handles)
                    {
                        var rect = new RectangleF(handle.X - handleSize / 2, handle.Y - handleSize / 2, handleSize, handleSize);
                        g.FillRectangle(handleBrush, rect);
                        g.DrawRectangle(handlePen, rect.X, rect.Y, rect.Width, rect.Height);
                    }

                    // Rotation handle (circle above top-center)
                    float rotHandleY = dy - 25;
                    g.DrawLine(handlePen, dx + dw / 2, dy, dx + dw / 2, rotHandleY);
                    g.FillEllipse(handleBrush, dx + dw / 2 - handleSize / 2, rotHandleY - handleSize / 2, handleSize, handleSize);
                    g.DrawEllipse(handlePen, dx + dw / 2 - handleSize / 2, rotHandleY - handleSize / 2, handleSize, handleSize);
                }

                // Draw rotation angle indicator
                if (Math.Abs(gump.Rotation) > 0.01f)
                {
                    using (var font = new Font("Consolas", 8))
                    using (var brush = new SolidBrush(HolographicTheme.CyanAccent))
                    {
                        g.DrawString($"{gump.Rotation:F1}�", font, brush, dx + dw + 5, dy);
                    }
                }
            }
        }

        private void CanvasBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                isPanning = true;
                lastPanPoint = e.Location;
                canvasBox.Cursor = Cursors.SizeAll;
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                // Convert to canvas coordinates
                int cx = (int)(e.X / zoom);
                int cy = (int)(e.Y / zoom);

                bool ctrlPressed = Control.ModifierKeys.HasFlag(Keys.Control);
                bool shiftPressed = Control.ModifierKeys.HasFlag(Keys.Shift);

                // Check for resize/rotate handles on selected gump first
                if (selectedGump != null && selectedGump.Image != null)
                {
                    int handle = GetHandleAtPoint(e.X, e.Y, selectedGump);
                    if (handle >= 0)
                    {
                        // Start resize operation
                        currentTransformMode = TransformMode.Resize;
                        resizeHandle = handle;
                        transformStart = new Point(cx, cy);
                        startScaleX = selectedGump.ScaleX;
                        startScaleY = selectedGump.ScaleY;
                        startWidth = selectedGump.Width;
                        startHeight = selectedGump.Height;
                        PushUndo();
                        return;
                    }

                    // Check for rotation handle
                    if (IsRotationHandle(e.X, e.Y, selectedGump))
                    {
                        currentTransformMode = TransformMode.Rotate;
                        transformStart = new Point(cx, cy);
                        startRotation = selectedGump.Rotation;
                        PushUndo();
                        return;
                    }
                }

                // Find clicked gump (reverse order for top-most first)
                PlacedGump clicked = null;
                for (int i = layers.Count - 1; i >= 0; i--)
                {
                    var layer = layers[i];
                    if (!layer.Visible || layer.Locked) continue;

                    for (int j = layer.Gumps.Count - 1; j >= 0; j--)
                    {
                        var gump = layer.Gumps[j];
                        if (gump.ContainsPoint(cx, cy))
                        {
                            clicked = gump;
                            break;
                        }
                    }
                    if (clicked != null) break;
                }

                if (clicked != null)
                {
                    if (ctrlPressed)
                    {
                        // Ctrl+Click: Toggle selection
                        if (selectedGumps.Contains(clicked))
                        {
                            selectedGumps.Remove(clicked);
                            if (selectedGump == clicked)
                                selectedGump = selectedGumps.Count > 0 ? selectedGumps[0] : null;
                        }
                        else
                        {
                            selectedGumps.Add(clicked);
                            selectedGump = clicked;
                        }
                    }
                    else if (shiftPressed && selectedGump != null)
                    {
                        // Shift+Click: Add to selection (range-like behavior)
                        if (!selectedGumps.Contains(clicked))
                        {
                            selectedGumps.Add(clicked);
                        }
                        selectedGump = clicked;
                    }
                    else
                    {
                        // Regular click: Single selection (unless already in multi-selection)
                        if (!selectedGumps.Contains(clicked))
                        {
                            selectedGumps.Clear();
                            selectedGumps.Add(clicked);
                        }
                        selectedGump = clicked;
                    }

                    // Start dragging all selected gumps
                    isDragging = true;
                    currentTransformMode = TransformMode.Move;
                    dragStart = new Point(cx, cy);
                    dragOffset = new Point(cx - clicked.X, cy - clicked.Y);

                    // Store starting positions for all selected gumps
                    dragSelection.Clear();
                    foreach (var gump in selectedGumps)
                    {
                        dragSelection.Add(new DragItem
                        {
                            Gump = gump,
                            StartX = gump.X,
                            StartY = gump.Y
                        });
                    }

                    PushUndo();
                }
                else
                {
                    // Clicked on empty space
                    if (!ctrlPressed)
                    {
                        // Clear selection and start selection rectangle
                        selectedGumps.Clear();
                        selectedGump = null;
                    }

                    // Start selection rectangle
                    isSelecting = true;
                    selectionStart = new Point(cx, cy);
                    selectionEnd = selectionStart;
                    selectionRect = Rectangle.Empty;
                }

                canvasBox.Invalidate();
            }
            else if (e.Button == MouseButtons.Right && selectedPaletteGump >= 0)
            {
                // Right-click to place selected gump
                int cx = (int)(e.X / zoom);
                int cy = (int)(e.Y / zoom);
                AddGumpToCanvas(selectedPaletteGump, cx, cy);
            }
        }

        private void CanvasBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (isPanning)
            {
                int dx = e.X - lastPanPoint.X;
                int dy = e.Y - lastPanPoint.Y;
                panOffset.X += dx;
                panOffset.Y += dy;
                lastPanPoint = e.Location;
                CenterCanvas();
                return;
            }

            int cx = (int)(e.X / zoom);
            int cy = (int)(e.Y / zoom);

            // Handle transform operations
            if (currentTransformMode == TransformMode.Resize && selectedGump != null)
            {
                HandleResize(cx, cy);
                canvasBox.Invalidate();
                return;
            }

            if (currentTransformMode == TransformMode.Rotate && selectedGump != null)
            {
                HandleRotate(e.X, e.Y);
                canvasBox.Invalidate();
                return;
            }

            if (isSelecting)
            {
                // Update selection rectangle
                selectionEnd = new Point(cx, cy);

                int x = Math.Min(selectionStart.X, selectionEnd.X);
                int y = Math.Min(selectionStart.Y, selectionEnd.Y);
                int w = Math.Abs(selectionEnd.X - selectionStart.X);
                int h = Math.Abs(selectionEnd.Y - selectionStart.Y);
                selectionRect = new Rectangle(x, y, w, h);

                canvasBox.Invalidate();
                SetStatus($"Selection: {w} x {h}");
            }
            else if (isDragging && selectedGumps.Count > 0)
            {
                // Calculate movement delta from drag start
                int deltaX = cx - dragStart.X;
                int deltaY = cy - dragStart.Y;

                // Move all selected gumps
                foreach (var item in dragSelection)
                {
                    int newX = item.StartX + deltaX;
                    int newY = item.StartY + deltaY;

                    if (GetSnapToGrid())
                    {
                        newX = (newX / gridSize) * gridSize;
                        newY = (newY / gridSize) * gridSize;
                    }

                    item.Gump.X = newX;
                    item.Gump.Y = newY;
                }

                canvasBox.Invalidate();
                if (selectedGumps.Count == 1)
                {
                    SetStatus($"Moving GUMP to ({selectedGumps[0].X}, {selectedGumps[0].Y})");
                }
                else
                {
                    SetStatus($"Moving {selectedGumps.Count} GUMPs");
                }
            }
            else
            {
                // Update cursor based on what's under mouse
                UpdateCursor(e.X, e.Y);

                // Update hovered gump
                PlacedGump hovered = null;
                for (int i = layers.Count - 1; i >= 0; i--)
                {
                    var layer = layers[i];
                    if (!layer.Visible) continue;

                    for (int j = layer.Gumps.Count - 1; j >= 0; j--)
                    {
                        var gump = layer.Gumps[j];
                        if (gump.ContainsPoint(cx, cy))
                        {
                            hovered = gump;
                            break;
                        }
                    }
                    if (hovered != null) break;
                }

                if (hovered != hoveredGump)
                {
                    hoveredGump = hovered;
                    canvasBox.Invalidate();
                }
            }
        }

        private void CanvasBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                isPanning = false;
                canvasBox.Cursor = Cursors.Default;
            }

            if (e.Button == MouseButtons.Left)
            {
                // End any transform operation
                if (currentTransformMode == TransformMode.Resize || currentTransformMode == TransformMode.Rotate)
                {
                    currentTransformMode = TransformMode.None;
                    resizeHandle = -1;
                    canvasBox.Invalidate();
                    return;
                }

                if (isSelecting)
                {
                    // Complete selection rectangle - select all gumps that intersect
                    bool ctrlPressed = Control.ModifierKeys.HasFlag(Keys.Control);

                    if (!ctrlPressed)
                    {
                        selectedGumps.Clear();
                    }

                    // Find all gumps that intersect with selection rectangle
                    foreach (var layer in layers)
                    {
                        if (!layer.Visible || layer.Locked) continue;

                        foreach (var gump in layer.Gumps)
                        {
                            if (gump.Image == null) continue;

                            // Get gump bounds
                            var gumpRect = gump.GetBounds();

                            // Check if gump intersects with selection rectangle
                            if (selectionRect.Width > 0 && selectionRect.Height > 0 &&
                                selectionRect.IntersectsWith(gumpRect))
                            {
                                if (!selectedGumps.Contains(gump))
                                {
                                    selectedGumps.Add(gump);
                                }
                            }
                        }
                    }

                    // Update selectedGump to first in list
                    selectedGump = selectedGumps.Count > 0 ? selectedGumps[0] : null;

                    isSelecting = false;
                    selectionRect = Rectangle.Empty;

                    if (selectedGumps.Count > 0)
                    {
                        SetStatus($"Selected {selectedGumps.Count} GUMP(s)");
                    }
                    else
                    {
                        SetStatus("No GUMPs selected");
                    }

                    canvasBox.Invalidate();
                }

                isDragging = false;
                currentTransformMode = TransformMode.None;
                dragSelection.Clear();
            }
        }

        private void CanvasBox_MouseWheel(object sender, MouseEventArgs e)
        {
            if (Control.ModifierKeys.HasFlag(Keys.Control))
            {
                if (e.Delta > 0)
                    ZoomIn();
                else
                    ZoomOut();
            }
        }

        private void ZoomIn()
        {
            zoom = Math.Min(8.0f, zoom * 1.25f);
            UpdateCanvasSize();
        }

        private void ZoomOut()
        {
            zoom = Math.Max(0.1f, zoom / 1.25f);
            UpdateCanvasSize();
        }

        private void UpdateCursor(int screenX, int screenY)
        {
            if (selectedGump != null && selectedGump.Image != null)
            {
                // Check rotation handle
                if (IsRotationHandle(screenX, screenY, selectedGump))
                {
                    canvasBox.Cursor = Cursors.Cross;
                    return;
                }

                // Check resize handles
                int handle = GetHandleAtPoint(screenX, screenY, selectedGump);
                switch (handle)
                {
                    case 0: case 4: canvasBox.Cursor = Cursors.SizeNWSE; return;
                    case 2: case 6: canvasBox.Cursor = Cursors.SizeNESW; return;
                    case 1: case 5: canvasBox.Cursor = Cursors.SizeNS; return;
                    case 3: case 7: canvasBox.Cursor = Cursors.SizeWE; return;
                }
            }

            canvasBox.Cursor = Cursors.Default;
        }

        // Export/Import
        private void ExportCanvas()
        {
            bool canExportPieces = placedGumps.Count > 1;
            if (canExportPieces)
            {
                var choice = MessageBox.Show(this,
                    "Export mode:\n\nYes = Export single composited canvas\nNo = Export each sprite as separate PNG files\nCancel = Abort",
                    "Export Options",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (choice == DialogResult.Cancel)
                    return;

                if (choice == DialogResult.No)
                {
                    ExportSpritePieces();
                    return;
                }
            }

            ExportCompositedCanvas();
        }

        private void ExportCompositedCanvas()
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "PNG Image|*.png|JPEG Image|*.jpg|Bitmap|*.bmp";
                sfd.FileName = "gump_export.png";

                if (sfd.ShowDialog() != DialogResult.OK)
                    return;

                var bmp = new Bitmap(canvasWidth, canvasHeight, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);

                    foreach (var layer in layers)
                    {
                        if (!layer.Visible) continue;

                        foreach (var gump in layer.Gumps)
                        {
                            if (gump.Image == null) continue;

                            float dw = gump.Image.Width * gump.ScaleX;
                            float dh = gump.Image.Height * gump.ScaleY;

                            if (layer.Opacity < 1.0f)
                            {
                                var cm = new ColorMatrix { Matrix33 = layer.Opacity };
                                var ia = new ImageAttributes();
                                ia.SetColorMatrix(cm);
                                g.DrawImage(gump.Image,
                                    new Rectangle(gump.X, gump.Y, (int)dw, (int)dh),
                                    0, 0, gump.Image.Width, gump.Image.Height,
                                    GraphicsUnit.Pixel, ia);
                            }
                            else
                            {
                                g.DrawImage(gump.Image, gump.X, gump.Y, dw, dh);
                            }
                        }
                    }
                }

                var format = ImageFormat.Png;
                if (sfd.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                    format = ImageFormat.Jpeg;
                else if (sfd.FileName.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                    format = ImageFormat.Bmp;

                bmp.Save(sfd.FileName, format);
                bmp.Dispose();

                SetStatus($"Exported to {Path.GetFileName(sfd.FileName)}");
            }
        }

        private void ExportSpritePieces()
        {
            var targets = selectedGumps != null && selectedGumps.Count > 0
                ? selectedGumps.Where(g => g?.Image != null).ToList()
                : placedGumps.Where(g => g?.Image != null).ToList();

            if (targets.Count == 0)
            {
                SetStatus("No sprites to export.");
                return;
            }

            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select folder to export sprite PNG files";
                if (fbd.ShowDialog() != DialogResult.OK)
                    return;

                int saved = 0;
                for (int i = 0; i < targets.Count; i++)
                {
                    var g = targets[i];
                    var layerName = (g.LayerIndex >= 0 && g.LayerIndex < layers.Count) ? layers[g.LayerIndex].Name : "Sprite";
                    string safeLayerName = string.Concat(layerName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
                    if (string.IsNullOrWhiteSpace(safeLayerName)) safeLayerName = "Sprite";

                    string filePath = Path.Combine(fbd.SelectedPath, $"{i:D3}_{safeLayerName}.png");
                    g.Image.Save(filePath, ImageFormat.Png);
                    saved++;
                }

                MessageBox.Show(this,
                    $"Exported {saved} sprite PNG(s) to:\n{fbd.SelectedPath}",
                    "Export Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                SetStatus($"Exported {saved} sprite PNG(s)");
            }
        }

        private void ImportImage()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif";

                if (ofd.ShowDialog() != DialogResult.OK)
                    return;

                try
                {
                    var img = new Bitmap(ofd.FileName);

                    if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count)
                        return;

                    var layer = layers[activeLayerIndex];
                    if (layer.Locked)
                    {
                        SetStatus("Layer is locked!");
                        img.Dispose();
                        return;
                    }

                    PushUndo();

                    var gump = new PlacedGump
                    {
                        GumpId = -1,
                        Image = img,
                        X = canvasWidth / 2 - img.Width / 2,
                        Y = canvasHeight / 2 - img.Height / 2,
                        LayerIndex = activeLayerIndex
                    };

                    layer.Gumps.Add(gump);
                    placedGumps.Add(gump);
                    selectedGump = gump;

                    canvasBox.Invalidate();
                    SetStatus($"Imported {Path.GetFileName(ofd.FileName)}");
                }
                catch (Exception ex)
                {
                    SetStatus($"Import failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Pastes an image from clipboard directly to canvas (Ctrl+V).
        /// </summary>
        private void PasteFromClipboard()
        {
            var img = GetBitmapFromClipboardPreserveAlpha();
            if (img == null)
            {
                SetStatus("No image in clipboard");
                return;
            }

            try
            {
                if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count)
                {
                    SetStatus("No active layer!");
                    img.Dispose();
                    return;
                }

                var layer = layers[activeLayerIndex];
                if (layer.Locked)
                {
                    SetStatus("Layer is locked!");
                    img.Dispose();
                    return;
                }

                PushUndo();

                // Paste at canvas center
                var gump = new PlacedGump
                {
                    GumpId = -1,
                    Image = img,
                    X = canvasWidth / 2 - img.Width / 2,
                    Y = canvasHeight / 2 - img.Height / 2,
                    LayerIndex = activeLayerIndex
                };

                layer.Gumps.Add(gump);
                placedGumps.Add(gump);
                selectedGump = gump;
                selectedGumps.Clear();
                selectedGumps.Add(gump);

                canvasBox.Invalidate();
                SetStatus($"Pasted image from clipboard ({img.Width}�{img.Height})");
            }
            catch (Exception ex)
            {
                SetStatus($"Paste failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Splits a sprite sheet from clipboard or file into separate layers.
        /// Automatically detects individual sprites and creates a layer for each.
        /// </summary>
        private void SplitSpriteSheet()
        {
            Bitmap sourceImage = null;

            // Try to get image from clipboard first
            sourceImage = GetBitmapFromClipboardPreserveAlpha();
            if (sourceImage == null)
            {
                // Ask user to select a file
                using (var ofd = new OpenFileDialog())
                {
                    ofd.Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif";
                    ofd.Title = "Select Sprite Sheet to Split";

                    if (ofd.ShowDialog() != DialogResult.OK)
                        return;

                    try
                    {
                        sourceImage = new Bitmap(ofd.FileName);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, $"Failed to load image:\n{ex.Message}", "Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
            }

            if (sourceImage == null)
            {
                MessageBox.Show(this, "No image found in clipboard or file.", "No Image",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // Show sprite sheet splitter dialog
                using (var dialog = new Dialogs.SpriteSheetSplitterDialog(sourceImage))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        sourceImage.Dispose();
                        return;
                    }

                    var detectedSprites = dialog.DetectedSprites;

                    if (detectedSprites == null || detectedSprites.Count == 0)
                    {
                        MessageBox.Show(this, "No sprites detected. Try adjusting the detection options.", "No Sprites",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        sourceImage.Dispose();
                        return;
                    }

                    // Push undo state before creating layers
                    PushUndo();

                    // Create a layer for each detected sprite
                    int successCount = 0;
                    foreach (var sprite in detectedSprites)
                    {
                        try
                        {
                            // Create new layer
                            string layerName = $"Sprite {sprite.Index + 1}";
                            var layer = new Layer { Name = layerName };
                            layers.Add(layer);

                            // Add sprite to the new layer
                            var gump = new PlacedGump
                            {
                                GumpId = -1,
                                Image = (Bitmap)sprite.Image.Clone(),
                                X = sprite.Bounds.X,
                                Y = sprite.Bounds.Y,
                                LayerIndex = layers.Count - 1
                            };

                            layer.Gumps.Add(gump);
                            placedGumps.Add(gump);
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            SetStatus($"Failed to add sprite {sprite.Index}: {ex.Message}");
                        }
                    }

                    // Set last created layer as active
                    if (successCount > 0)
                    {
                        activeLayerIndex = layers.Count - 1;
                        RefreshLayersList();
                        canvasBox.Invalidate();
                        SetStatus($"Split sprite sheet into {successCount} layers");

                        MessageBox.Show(this,
                            $"Successfully split sprite sheet into {successCount} sprites!\n\n" +
                            "Each sprite is now in a separate layer.\n" +
                            "You can move, scale, or edit them individually.",
                            "Sprite Sheet Split Complete",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show(this, "Failed to split sprite sheet.", "Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Sprite sheet splitting failed:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                sourceImage?.Dispose();
            }
        }
    }
}
