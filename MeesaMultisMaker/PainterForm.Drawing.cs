using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using MeesaMultisMaker.Utils;

namespace MeesaMultisMaker
{
    public partial class PainterForm
    {
        private void CreateCanvas()
        {
            int size = 512;
            switch (sizeCombo.SelectedIndex)
            {
                case 1: size = 768; break;
                case 2: size = 1024; break;
                case 3: size = 1536; break;
            }

            // dispose old layers safely
            foreach (var l in layers)
            {
                l.Image?.Dispose(); 
                l.Mask?.Dispose();
                l.SourceImage?.Dispose();
            }
            layers.Clear();

            // create initial canvas and size the picture box first so new layers use correct dimensions
            canvasBmp?.Dispose();
            canvasBmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);

            canvasBox.Width = (int)(size * zoom);
            canvasBox.Height = (int)(size * zoom);
            
            // Safely dispose and recreate canvas image
            var oldImage = canvasBox.Image;
            canvasBox.Image = new Bitmap(size, size);
            oldImage?.Dispose();

            // create initial layer
            AddLayer("Layer 0");

            RebuildLayersUI();
            UpdateCanvas();
            CenterCanvas();
        }

        private void UpdateCanvas()
        {
            if (!ImageHelper.IsValidImage(canvasBox.Image)) return;

            try
            {
                // Composite layers bottom->top into canvasBox.Image
                using (var g = Graphics.FromImage(canvasBox.Image))
                {
                    g.Clear(Color.Transparent);
                    g.CompositingMode = CompositingMode.SourceOver;
                    foreach (var l in layers)
                    {
                        if (!l.Visible) continue;
                        if (!ImageHelper.IsValidImage(l.Image)) continue;

                        using (var layerComposite = new Bitmap(l.Image.Width, l.Image.Height, PixelFormat.Format32bppArgb))
                        using (var lg = Graphics.FromImage(layerComposite))
                        {
                            lg.CompositingMode = CompositingMode.SourceOver;
                            lg.DrawImage(l.Image, 0, 0, l.Image.Width, l.Image.Height);

                            if (l.SourceImage != null)
                            {
                                int drawW = Math.Max(1, (int)Math.Round(l.SourceImage.Width * l.SourceScale));
                                int drawH = Math.Max(1, (int)Math.Round(l.SourceImage.Height * l.SourceScale));
                                var dest = new Rectangle(
                                    (int)Math.Round(l.SourceOffset.X),
                                    (int)Math.Round(l.SourceOffset.Y),
                                    drawW,
                                    drawH);

                                lg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                lg.DrawImage(l.SourceImage, dest, 0, 0, l.SourceImage.Width, l.SourceImage.Height, GraphicsUnit.Pixel);
                            }

                            if (l.Opacity >= 0.999f)
                            {
                                g.DrawImage(layerComposite, 0, 0, layerComposite.Width, layerComposite.Height);
                            }
                            else
                            {
                                var cm = new ColorMatrix(); cm.Matrix33 = l.Opacity;
                                var ia = new ImageAttributes(); ia.SetColorMatrix(cm);
                                g.DrawImage(layerComposite, new Rectangle(0, 0, layerComposite.Width, layerComposite.Height), 0, 0, layerComposite.Width, layerComposite.Height, GraphicsUnit.Pixel, ia);
                            }
                        }
                    }

                    if (drawMask && activeLayerIndex >= 0 && activeLayerIndex < layers.Count)
                    {
                        using (var redBrush = new SolidBrush(Color.FromArgb(100, 255, 0, 0)))
                        {
                            var combined = GetCombinedMask();
                            if (combined != null)
                            {
                                for (int y = 0; y < combined.Height; y += 2)
                                {
                                    for (int x = 0; x < combined.Width; x += 2)
                                    {
                                        var px = combined.GetPixel(x, y);
                                        if (px.R > 128)
                                        {
                                            g.FillRectangle(redBrush, x, y, 2, 2);
                                        }
                                    }
                                }
                                combined.Dispose();
                            }
                        }
                    }
                }
                
                // resize control to match zoomed image so scrollbars work
                if (ImageHelper.IsValidImage(canvasBox.Image))
                {
                    canvasBox.Width = (int)(canvasBox.Image.Width * zoom);
                    canvasBox.Height = (int)(canvasBox.Image.Height * zoom);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateCanvas error: {ex.Message}");
            }
            
            canvasBox.Invalidate();
        }

        private void CanvasBox_Paint(object sender, PaintEventArgs e)
        {
            // draw the base image scaled to zoom
            try
            {
                if (ImageHelper.IsValidImage(canvasBox.Image))
                {
                    e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                    e.Graphics.DrawImage(canvasBox.Image, new Rectangle(0, 0, canvasBox.Width, canvasBox.Height));
                }
            }
            catch { }

            if (drawingShape && currentTool != ToolMode.Brush && currentTool != ToolMode.Pencil && currentTool != ToolMode.Eraser && currentTool != ToolMode.Fill)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                var color = drawMask ? Color.FromArgb(200, 255, 255, 255) : currentColor;

                switch (currentTool)
                {
                    case ToolMode.Line:
                        using (var pen = new Pen(color, Math.Max(1, brushSize * zoom)))
                        {
                            g.DrawLine(pen, new Point((int)(shapeStart.X * zoom), (int)(shapeStart.Y * zoom)), new Point((int)(lastPt.X * zoom), (int)(lastPt.Y * zoom)));
                        }
                        break;
                    case ToolMode.Rectangle:
                        var rect = GetRectangle(shapeStart, lastPt);
                        var rectZoom = new Rectangle((int)(rect.X * zoom), (int)(rect.Y * zoom), (int)(rect.Width * zoom), (int)(rect.Height * zoom));
                        if (fillShapeCheck.Checked)
                        {
                            using (var brush = new SolidBrush(color))
                                g.FillRectangle(brush, rectZoom);
                        }
                        else
                        {
                            using (var pen = new Pen(color, Math.Max(1, brushSize * zoom)))
                                g.DrawRectangle(pen, rectZoom);
                        }
                        break;
                    case ToolMode.Circle:
                        var circRect = GetRectangle(shapeStart, lastPt);
                        var circRectZoom = new Rectangle((int)(circRect.X * zoom), (int)(circRect.Y * zoom), (int)(circRect.Width * zoom), (int)(circRect.Height * zoom));
                        if (fillShapeCheck.Checked)
                        {
                            using (var brush = new SolidBrush(color))
                                g.FillEllipse(brush, circRectZoom);
                        }
                        else
                        {
                            using (var pen = new Pen(color, Math.Max(1, brushSize * zoom)))
                                g.DrawEllipse(pen, circRectZoom);
                        }
                        break;
                }
            }

            if (hasSelection || isSelecting)
            {
                var r = selectionRect;
                if (r.Width > 0 && r.Height > 0)
                {
                    var rz = new Rectangle(
                        (int)Math.Round(r.X * zoom),
                        (int)Math.Round(r.Y * zoom),
                        (int)Math.Round(r.Width * zoom),
                        (int)Math.Round(r.Height * zoom));

                    using (var pen = new Pen(Color.FromArgb(240, 255, 220, 60), 1f))
                    {
                        pen.DashStyle = DashStyle.Dash;
                        e.Graphics.DrawRectangle(pen, rz);
                    }
                }
            }

            // Draw isometric diamond grid overlay (not part of image data)
            DrawIsometricGrid(e.Graphics);

            // Draw brush preview circle when hovering over canvas (on top of grid)
            DrawBrushPreview(e.Graphics);
        }

        private void DrawIsometricGrid(Graphics g)
        {
            try
            {
                g.SmoothingMode = SmoothingMode.None;
                const int TILE_WIDTH = 44;
                const int TILE_HEIGHT = 44;
                int hw = TILE_WIDTH / 2;
                int hh = TILE_HEIGHT / 2;

                // teal/blue grid similar to main form, semi-transparent
                using (var gridPen = new Pen(Color.FromArgb(40, 0, 122, 122)))
                {
                    // center of canvas in local coordinates (use unzoomed center scaled)
                    float centerX = (canvasBox.Image.Width / 2f) * zoom;
                    float centerY = (canvasBox.Image.Height / 2f) * zoom;

                    // choose range large enough to cover canvas
                    int range = (int)((canvasBox.Width + canvasBox.Height) / Math.Min(TILE_WIDTH, TILE_HEIGHT)) + 8;

                    for (int y = -range; y <= range; y++)
                    {
                        for (int x = -range; x <= range; x++)
                        {
                            float isoX = (x - y) * (TILE_WIDTH / 2f);
                            float isoY = (x + y) * (TILE_HEIGHT / 2f);

                            float sx = centerX + isoX * zoom;
                            float sy = centerY + isoY * zoom;

                            // Only draw if inside canvas bounds (with margin)
                            if (sx < -hw || sx > canvasBox.Width + hw || sy < -hh || sy > canvasBox.Height + hh) continue;

                            Point[] diamond = new Point[4]
                            {
                                new Point((int)Math.Round(sx), (int)Math.Round(sy - hh)),
                                new Point((int)Math.Round(sx + hw), (int)Math.Round(sy)),
                                new Point((int)Math.Round(sx), (int)Math.Round(sy + hh)),
                                new Point((int)Math.Round(sx - hw), (int)Math.Round(sy))
                            };

                            g.DrawPolygon(gridPen, diamond);
                        }
                    }
                }
            }
            catch { }
        }

        private void DrawBrushPreview(Graphics g)
        {
            try
            {
                if (mouseOverCanvas && (currentTool == ToolMode.Brush || currentTool == ToolMode.Pencil || currentTool == ToolMode.Eraser))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    // Determine preview color
                    Color previewColor;
                    if (drawMask)
                        previewColor = Color.FromArgb(160, Color.White);
                    else if (currentTool == ToolMode.Eraser)
                        previewColor = Color.FromArgb(160, Color.Black);
                    else
                        previewColor = Color.FromArgb(160, currentColor);

                    int radius = Math.Max(1, (int)(brushSize * zoom));
                    var rect = new Rectangle((int)(mousePos.X * zoom) - radius / 2, (int)(mousePos.Y * zoom) - radius / 2, radius, radius);

                    using (var fill = new SolidBrush(Color.FromArgb(48, previewColor)))
                    using (var pen = new Pen(previewColor, 2))
                    {
                        g.FillEllipse(fill, rect);
                        g.DrawEllipse(pen, rect);
                    }
                }
            }
            catch { }
        }

        private Rectangle GetRectangle(Point p1, Point p2)
        {
            return new Rectangle(
                Math.Min(p1.X, p2.X),
                Math.Min(p1.Y, p2.Y),
                Math.Abs(p2.X - p1.X),
                Math.Abs(p2.Y - p1.Y)
            );
        }

        private void CanvasBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count) return;
            if (layers[activeLayerIndex].Locked || !layers[activeLayerIndex].Visible) return;

            if (currentTool == ToolMode.Select)
            {
                isSelecting = true;
                selectionStart = new Point((int)(e.Location.X / zoom), (int)(e.Location.Y / zoom));
                selectionRect = new Rectangle(selectionStart, Size.Empty);
                hasSelection = false;
                canvasBox.Invalidate();
                return;
            }

            if (currentTool == ToolMode.Move)
            {
                SaveLayerUndo(activeLayerIndex);
                movingLayer = true;
                moveStart = new Point((int)(e.Location.X / zoom), (int)(e.Location.Y / zoom));
                moveStartSourceOffset = layers[activeLayerIndex].SourceOffset;
                moveStartImage?.Dispose();
                moveStartMask?.Dispose();

                if (layers[activeLayerIndex].SourceImage == null)
                {
                    moveStartImage = new Bitmap(layers[activeLayerIndex].Image);
                    moveStartMask = new Bitmap(layers[activeLayerIndex].Mask);
                }
                return;
            }

            hasSelection = false;
            selectionRect = Rectangle.Empty;

            SaveLayerUndo(activeLayerIndex);

            // convert to image coordinates
            lastPt = new Point((int)(e.Location.X / zoom), (int)(e.Location.Y / zoom));
            mousePos = new Point((int)(e.Location.X / zoom), (int)(e.Location.Y / zoom));
            mouseOverCanvas = true;

            shapeStart = new Point((int)(e.Location.X / zoom), (int)(e.Location.Y / zoom));

            if (currentTool == ToolMode.Fill)
            {
                FloodFill(mousePos);
                UpdateCanvas();
                return;
            }

            if (currentTool == ToolMode.Line || currentTool == ToolMode.Rectangle || currentTool == ToolMode.Circle)
            {
                drawingShape = true;
                canvasBox.Invalidate();
                return;
            }

            drawing = true;
            DrawAt(mousePos);
        }

        private void CanvasBox_MouseMove(object sender, MouseEventArgs e)
        {
            // Always update mouse position for preview
            mousePos = new Point((int)(e.Location.X / zoom), (int)(e.Location.Y / zoom));
            mouseOverCanvas = true;
            canvasBox.Invalidate();

            if (isSelecting && currentTool == ToolMode.Select)
            {
                int x = Math.Min(selectionStart.X, mousePos.X);
                int y = Math.Min(selectionStart.Y, mousePos.Y);
                int w = Math.Abs(mousePos.X - selectionStart.X);
                int h = Math.Abs(mousePos.Y - selectionStart.Y);
                selectionRect = new Rectangle(x, y, w, h);
                canvasBox.Invalidate();
                return;
            }

            if (movingLayer && currentTool == ToolMode.Move)
            {
                if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count)
                    return;

                int dx = mousePos.X - moveStart.X;
                int dy = mousePos.Y - moveStart.Y;

                var layer = layers[activeLayerIndex];
                if (layer.SourceImage != null)
                {
                    layer.SourceOffset = new PointF(moveStartSourceOffset.X + dx, moveStartSourceOffset.Y + dy);
                    UpdateCanvas();
                    return;
                }

                if (moveStartImage == null || moveStartMask == null)
                    return;

                using (var gImage = Graphics.FromImage(layer.Image))
                using (var gMask = Graphics.FromImage(layer.Mask))
                {
                    gImage.Clear(Color.Transparent);
                    gImage.InterpolationMode = InterpolationMode.NearestNeighbor;
                    gImage.DrawImageUnscaled(moveStartImage, dx, dy);

                    gMask.Clear(Color.Black);
                    gMask.InterpolationMode = InterpolationMode.NearestNeighbor;
                    gMask.DrawImageUnscaled(moveStartMask, dx, dy);
                }

                UpdateCanvas();
                return;
            }

            if (drawingShape)
            {
                lastPt = new Point((int)(e.Location.X / zoom), (int)(e.Location.Y / zoom));
                canvasBox.Invalidate();
                return;
            }

            if (!drawing) return;
            DrawLine(lastPt, new Point((int)(e.Location.X / zoom), (int)(e.Location.Y / zoom)));
            lastPt = new Point((int)(e.Location.X / zoom), (int)(e.Location.Y / zoom));
        }

        private void CanvasBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            if (isSelecting && currentTool == ToolMode.Select)
            {
                isSelecting = false;
                hasSelection = selectionRect.Width > 0 && selectionRect.Height > 0;
                canvasBox.Invalidate();
                return;
            }

            if (movingLayer)
            {
                movingLayer = false;
                moveStartImage?.Dispose();
                moveStartMask?.Dispose();
                moveStartImage = null;
                moveStartMask = null;
                UpdateCanvas();
                return;
            }

            if (drawingShape)
            {
                drawingShape = false;

                var bmp = drawMask ? layers[activeLayerIndex].Mask : layers[activeLayerIndex].Image;
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var color = drawMask ? Color.White : currentColor;

                    switch (currentTool)
                    {
                        case ToolMode.Line:
                            using (var pen = new Pen(color, brushSize))
                            {
                                g.DrawLine(pen, shapeStart, new Point((int)(e.Location.X / zoom), (int)(e.Location.Y / zoom)));
                            }
                            break;
                        case ToolMode.Rectangle:
                            var rect = GetRectangle(shapeStart, new Point((int)(e.Location.X / zoom), (int)(e.Location.Y / zoom)));
                            if (fillShapeCheck.Checked)
                            {
                                using (var brush = new SolidBrush(color))
                                    g.FillRectangle(brush, rect);
                            }
                            else
                            {
                                using (var pen = new Pen(color, brushSize))
                                    g.DrawRectangle(pen, rect);
                            }
                            break;
                        case ToolMode.Circle:
                            var circRect = GetRectangle(shapeStart, new Point((int)(e.Location.X / zoom), (int)(e.Location.Y / zoom)));
                            if (fillShapeCheck.Checked)
                            {
                                using (var brush = new SolidBrush(color))
                                    g.FillEllipse(brush, circRect);
                            }
                            else
                            {
                                using (var pen = new Pen(color, brushSize))
                                    g.DrawEllipse(pen, circRect);
                            }
                            break;
                    }
                }
                UpdateCanvas();
            }

            drawing = false;
        }

        private void DrawAt(Point p)
        {
            if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count) return;
            if (layers[activeLayerIndex].Locked || !layers[activeLayerIndex].Visible) return;
            var bmp = drawMask ? layers[activeLayerIndex].Mask : layers[activeLayerIndex].Image;
            var size = currentTool == ToolMode.Pencil ? 1 : brushSize;

            using (var g = Graphics.FromImage(bmp))
            {
                if (drawMask)
                {
                    // Mask painting: white to add mask, black to erase mask
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var maskColor = (currentTool == ToolMode.Eraser) ? Color.Black : Color.White;
                    using (var brush = new SolidBrush(maskColor))
                    {
                        g.FillEllipse(brush, p.X - size / 2, p.Y - size / 2, size, size);
                    }
                }
                else
                {
                    if (currentTool == ToolMode.Eraser)
                    {
                        // Erase to transparent using SourceCopy compositing
                        g.CompositingMode = CompositingMode.SourceCopy;
                        using (var brush = new SolidBrush(Color.FromArgb(0, 0, 0, 0)))
                        {
                            g.FillEllipse(brush, p.X - size / 2, p.Y - size / 2, size, size);
                        }
                        g.CompositingMode = CompositingMode.SourceOver;
                    }
                    else
                    {
                        g.SmoothingMode = currentTool == ToolMode.Pencil ? SmoothingMode.None : SmoothingMode.AntiAlias;
                        using (var brush = new SolidBrush(currentColor))
                        {
                            g.FillEllipse(brush, p.X - size / 2, p.Y - size / 2, size, size);
                        }
                    }
                }
            }
            UpdateCanvas();
        }

        private void DrawLine(Point a, Point b)
        {
            if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count) return;
            if (layers[activeLayerIndex].Locked || !layers[activeLayerIndex].Visible) return;
            var bmp = drawMask ? layers[activeLayerIndex].Mask : layers[activeLayerIndex].Image;
            var size = currentTool == ToolMode.Pencil ? 1 : brushSize;

            using (var g = Graphics.FromImage(bmp))
            {
                if (drawMask)
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var maskColor = (currentTool == ToolMode.Eraser) ? Color.Black : Color.White;
                    using (var pen = new Pen(maskColor, size) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawLine(pen, a, b);
                    }
                }
                else
                {
                    if (currentTool == ToolMode.Eraser)
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        using (var pen = new Pen(Color.FromArgb(0, 0, 0, 0), size) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            g.DrawLine(pen, a, b);
                        }
                        g.CompositingMode = CompositingMode.SourceOver;
                    }
                    else
                    {
                        g.SmoothingMode = currentTool == ToolMode.Pencil ? SmoothingMode.None : SmoothingMode.AntiAlias;
                        using (var pen = new Pen(currentColor, size) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            g.DrawLine(pen, a, b);
                        }
                    }
                }
            }
            UpdateCanvas();
        }

        private void FloodFill(Point start)
        {
            if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count) return;
            if (layers[activeLayerIndex].Locked || !layers[activeLayerIndex].Visible) return;
            var bmp = drawMask ? layers[activeLayerIndex].Mask : layers[activeLayerIndex].Image;
            var fillColor = drawMask ? Color.White : currentColor;
            var targetColor = bmp.GetPixel(start.X, start.Y);

            if (targetColor.ToArgb() == fillColor.ToArgb()) return;

            var pixels = new Stack<Point>();
            pixels.Push(start);

            while (pixels.Count > 0)
            {
                var p = pixels.Pop();
                if (p.X < 0 || p.X >= bmp.Width || p.Y < 0 || p.Y >= bmp.Height) continue;

                var current = bmp.GetPixel(p.X, p.Y);
                if (current.ToArgb() != targetColor.ToArgb()) continue;

                bmp.SetPixel(p.X, p.Y, fillColor);

                pixels.Push(new Point(p.X + 1, p.Y));
                pixels.Push(new Point(p.X - 1, p.Y));
                pixels.Push(new Point(p.X, p.Y + 1));
                pixels.Push(new Point(p.X, p.Y - 1));
            }
        }

        private void ClearBtn_Click(object sender, EventArgs e)
        {
            SaveLayerUndo(activeLayerIndex);
            layers[activeLayerIndex].SourceImage?.Dispose();
            layers[activeLayerIndex].SourceImage = null;
            layers[activeLayerIndex].SourceOffset = PointF.Empty;
            layers[activeLayerIndex].SourceScale = 1.0f;

            if (drawMask)
            {
                using (var g = Graphics.FromImage(layers[activeLayerIndex].Mask)) g.Clear(Color.Black);
            }
            else
            {
                // Clear canvas to fully transparent
                using (var g = Graphics.FromImage(layers[activeLayerIndex].Image))
                {
                    g.Clear(Color.Transparent);
                }
            }
            UpdateCanvas();
        }

        private void SaveBtn_Click(object sender, EventArgs e)
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "PNG Image|*.png|JPEG Image|*.jpg|Bitmap|*.bmp";
                sfd.FileName = "painting.png";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    var format = ImageFormat.Png;
                    if (sfd.FileName.EndsWith(".jpg")) format = ImageFormat.Jpeg;
                    else if (sfd.FileName.EndsWith(".bmp")) format = ImageFormat.Bmp;

                    var layer = layers[activeLayerIndex];
                    if (layer.SourceImage != null)
                    {
                        // Save-only combine: draw SourceImage over Image without mutating the layer.
                        using (var combined = new Bitmap(layer.Image.Width, layer.Image.Height, PixelFormat.Format32bppArgb))
                        using (var g = Graphics.FromImage(combined))
                        {
                            g.DrawImage(layer.Image, 0, 0, layer.Image.Width, layer.Image.Height);
                            int drawW = Math.Max(1, (int)Math.Round(layer.SourceImage.Width * layer.SourceScale));
                            int drawH = Math.Max(1, (int)Math.Round(layer.SourceImage.Height * layer.SourceScale));
                            var dest = new Rectangle(
                                (int)Math.Round(layer.SourceOffset.X),
                                (int)Math.Round(layer.SourceOffset.Y),
                                drawW, drawH);
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(layer.SourceImage, dest, 0, 0, layer.SourceImage.Width, layer.SourceImage.Height, GraphicsUnit.Pixel);
                            combined.Save(sfd.FileName, format);
                        }
                    }
                    else
                    {
                        layer.Image.Save(sfd.FileName, format);
                    }
                }
            }
        }

        private void LoadBtn_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    var loaded = new Bitmap(ofd.FileName);
                    layers[activeLayerIndex].SourceImage?.Dispose();
                    layers[activeLayerIndex].SourceImage = null;
                    layers[activeLayerIndex].SourceOffset = PointF.Empty;
                    layers[activeLayerIndex].SourceScale = 1.0f;
                    using (var g = Graphics.FromImage(layers[activeLayerIndex].Image))
                    {
                        g.Clear(backgroundColor);
                        g.DrawImage(loaded, 0, 0, layers[activeLayerIndex].Image.Width, layers[activeLayerIndex].Image.Height);
                    }
                    loaded.Dispose();
                    UpdateCanvas();
                }
            }
        }

        private void CopySelectionToClipboard(bool cut)
        {
            if (!hasSelection)
                return;
            if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count)
                return;

            var layer = layers[activeLayerIndex];
            if (layer.Locked)
                return;

            RasterizeLayerSource(layer);

            var bounds = new Rectangle(0, 0, layer.Image.Width, layer.Image.Height);
            var sel = Rectangle.Intersect(selectionRect, bounds);
            if (sel.Width <= 0 || sel.Height <= 0)
                return;

            var copied = new Bitmap(sel.Width, sel.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(copied))
            {
                g.DrawImage(layer.Image, new Rectangle(0, 0, sel.Width, sel.Height), sel, GraphicsUnit.Pixel);
            }

            Clipboard.SetImage(copied);

            if (cut)
            {
                SaveLayerUndo(activeLayerIndex);

                using (var g = Graphics.FromImage(layer.Image))
                using (var brush = new SolidBrush(Color.FromArgb(0, 0, 0, 0)))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.FillRectangle(brush, sel);
                    g.CompositingMode = CompositingMode.SourceOver;
                }

                using (var g = Graphics.FromImage(layer.Mask))
                {
                    g.FillRectangle(Brushes.Black, sel);
                }

                UpdateCanvas();
            }

            copied.Dispose();
        }

        private void RemoveBackgroundFromActiveLayer()
        {
            if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count)
                return;

            var layer = layers[activeLayerIndex];
            if (layer.Locked || !layer.Visible)
                return;

            Bitmap target = layer.SourceImage ?? layer.Image;
            if (target == null)
                return;

            SaveLayerUndo(activeLayerIndex);

            int w = target.Width;
            int h = target.Height;
            if (w == 0 || h == 0)
                return;

            // Sample corners for a representative background tone.
            Color c1 = target.GetPixel(0, 0);
            Color c2 = target.GetPixel(w - 1, 0);
            Color c3 = target.GetPixel(0, h - 1);
            Color c4 = target.GetPixel(w - 1, h - 1);
            Color bg = Color.FromArgb(
                (c1.R + c2.R + c3.R + c4.R) / 4,
                (c1.G + c2.G + c3.G + c4.G) / 4,
                (c1.B + c2.B + c3.B + c4.B) / 4);

            const int tolerance = 30;
            bool[,] visited = new bool[w, h];
            var q = new Queue<Point>();

            void EnqueueIfMatch(int x, int y)
            {
                if (x < 0 || x >= w || y < 0 || y >= h) return;
                if (visited[x, y]) return;

                Color px = target.GetPixel(x, y);
                int dist = Math.Abs(px.R - bg.R) + Math.Abs(px.G - bg.G) + Math.Abs(px.B - bg.B);
                if (dist > tolerance) return;

                visited[x, y] = true;
                q.Enqueue(new Point(x, y));
            }

            for (int x = 0; x < w; x++)
            {
                EnqueueIfMatch(x, 0);
                EnqueueIfMatch(x, h - 1);
            }
            for (int y = 0; y < h; y++)
            {
                EnqueueIfMatch(0, y);
                EnqueueIfMatch(w - 1, y);
            }

            while (q.Count > 0)
            {
                var p = q.Dequeue();
                Color c = target.GetPixel(p.X, p.Y);
                target.SetPixel(p.X, p.Y, Color.FromArgb(0, c.R, c.G, c.B));

                EnqueueIfMatch(p.X + 1, p.Y);
                EnqueueIfMatch(p.X - 1, p.Y);
                EnqueueIfMatch(p.X, p.Y + 1);
                EnqueueIfMatch(p.X, p.Y - 1);
            }

            UpdateCanvas();
        }

        private void RasterizeLayerSource(Layer layer)
        {
            if (layer?.SourceImage == null)
                return;

            using (var g = Graphics.FromImage(layer.Image))
            {
                int drawW = Math.Max(1, (int)Math.Round(layer.SourceImage.Width * layer.SourceScale));
                int drawH = Math.Max(1, (int)Math.Round(layer.SourceImage.Height * layer.SourceScale));
                var dest = new Rectangle(
                    (int)Math.Round(layer.SourceOffset.X),
                    (int)Math.Round(layer.SourceOffset.Y),
                    drawW,
                    drawH);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(layer.SourceImage, dest, 0, 0, layer.SourceImage.Width, layer.SourceImage.Height, GraphicsUnit.Pixel);
            }

            layer.SourceImage.Dispose();
            layer.SourceImage = null;
            layer.SourceOffset = PointF.Empty;
            layer.SourceScale = 1.0f;
        }

        private void ApplyEffectsToLayer(bool preview, int layerIndex = -1)
        {
            if (layerIndex < 0) layerIndex = activeLayerIndex;
            if (layerIndex < 0 || layerIndex >= layers.Count) return;

            var layer = layers[layerIndex];
            RasterizeLayerSource(layer);

            // gather effect parameters from the editing panel
            float brightness = imageEditPanel?.Brightness ?? 0f;
            float contrast = imageEditPanel?.Contrast ?? 0f;
            float hue = imageEditPanel?.Hue ?? 0f;
            float saturation = imageEditPanel?.Saturation ?? 0f;
            int pixelSize = imageEditPanel?.PixelSize ?? 1;
            bool pixelize = imageEditPanel?.PixelizeEnabled ?? false;
            int paletteColors = imageEditPanel?.PaletteColors ?? 0;

            if (preview)
            {
                // backup original if not already
                if (layer.PreEditImage == null)
                    layer.PreEditImage = new Bitmap(layer.Image);

                // apply effects to a copy of the original backup
                using (var src = new Bitmap(layer.PreEditImage))
                {
                    var processed = Utils.ImageEffects.ApplyEffects(src, brightness, contrast, hue, saturation, pixelSize, pixelize, paletteColors);
                    // replace current layer image with processed
                    layer.Image.Dispose();
                    layer.Image = processed;
                }
            }
            else
            {
                // If we had a preview backup, current layer.Image already holds the preview result; commit by discarding backup
                if (layer.PreEditImage != null)
                {
                    layer.PreEditImage.Dispose();
                    layer.PreEditImage = null;
                }
                else
                {
                    // no preview existed; apply effects directly to current image
                    using (var src = new Bitmap(layer.Image))
                    {
                        var processed = Utils.ImageEffects.ApplyEffects(src, brightness, contrast, hue, saturation, pixelSize, pixelize, paletteColors);
                        layer.Image.Dispose();
                        layer.Image = processed;
                    }
                }
            }

            UpdateCanvas();
        }
    }
}
