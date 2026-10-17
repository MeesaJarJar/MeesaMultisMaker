using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    public partial class Form1
    {
        // Draw bold, outlined label text
        private void DrawOutlinedText(Graphics g, string text, PointF location, Color fillColor, float pointSize)
        {
            using (var font = new Font(this.Font.FontFamily, pointSize, FontStyle.Bold, GraphicsUnit.Point))
            using (var path = new GraphicsPath())
            {
                float emSizePx = g.DpiY / 72f * font.SizeInPoints;
                path.AddString(text, font.FontFamily, (int)FontStyle.Bold, emSizePx, location, StringFormat.GenericDefault);
                using (var pen = new Pen(Color.Black, 3f) { LineJoin = LineJoin.Round })
                { g.DrawPath(pen, path); }
                using (var brush = new SolidBrush(fillColor))
                { g.FillPath(brush, path); }
            }
        }

        private void DesignPictureBox_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;

            int minZFilter = minZTrackBar?.Value ?? -127;
            int maxZFilter = maxZTrackBar?.Value ?? 127;

            // Draw grid
            using (Pen gridPen = new Pen(HolographicTheme.GridLine))
            {
                for (int y = 0; y < gridHeight; y++)
                    for (int x = 0; x < gridWidth; x++)
                    {
                        Point isoPos = GridToIso(x, y);
                        int hw = (int)Math.Round((TILE_WIDTH / 2f) * zoom);
                        int hh = (int)Math.Round((TILE_HEIGHT / 2f) * zoom);
                        Point[] diamond = new Point[4]
                        {
 new Point(isoPos.X, isoPos.Y - hh),
 new Point(isoPos.X + hw, isoPos.Y),
 new Point(isoPos.X, isoPos.Y + hh),
 new Point(isoPos.X - hw, isoPos.Y)
                        };
                        g.DrawPolygon(gridPen, diamond);
                    }
            }

            // Highlight slice tiles
            if (isSliceMode && sliceTiles.Count > 0)
            {
                using (var fill = new SolidBrush(Color.FromArgb(80, HolographicTheme.CyanAccent)))
                using (var pen = new Pen(HolographicTheme.CyanAccent, 2))
                {
                    foreach (var tile in sliceTiles)
                    {
                        var isoPos = GridToIso(tile.X, tile.Y);
                        int hw = (int)Math.Round((TILE_WIDTH / 2f) * zoom);
                        int hh = (int)Math.Round((TILE_HEIGHT / 2f) * zoom);
                        Point[] diamond = new Point[4]
                        {
 new Point(isoPos.X, isoPos.Y - hh),
 new Point(isoPos.X + hw, isoPos.Y),
 new Point(isoPos.X, isoPos.Y + hh),
 new Point(isoPos.X - hw, isoPos.Y)
                        };
                        g.FillPolygon(fill, diamond);
                        g.DrawPolygon(pen, diamond);
                    }
                }
            }

            // Draw objects (UO painter's order - see GetPaintOrder: floors below
            // walls/roof at the same tile always draw first)
            foreach (var obj in GetPaintOrder())
            {
                if (obj.Hidden) continue;
                if (!IsImageUsable(obj.Image)) continue;

                if (obj.Z < minZFilter || obj.Z > maxZFilter) continue;

                float objScale = obj.Scale * zoom;
                float zOffset = obj.Z * Z_PIXEL * zoom;

                float isoX = (obj.GridX - obj.GridY) * (TILE_WIDTH / 2f);
                float isoY = (obj.GridX + obj.GridY) * (TILE_HEIGHT / 2f);
                float screenX = canvasOffset.X + isoX * zoom + obj.PixelOffsetX * zoom;
                float screenY = canvasOffset.Y + isoY * zoom + obj.PixelOffsetY * zoom;

                float w, h;
                try { w = obj.Image.Width * objScale; h = obj.Image.Height * objScale; }
                catch { continue; }
                float tileHalfHeight = (TILE_HEIGHT / 2f) * zoom;

                float drawX = screenX - w / 2f;
                float drawY = screenY - h + tileHalfHeight - zOffset;

                try
                {
                    // Check if object has skew applied
                    if (obj.HasSkew)
                    {
                        // Draw with perspective/skew transform
                        DrawSkewedImage(g, obj, drawX, drawY, w, h);
                    }
                    else if (obj.Rotation != 0f || obj.FlipHorizontal || obj.FlipVertical)
                    {
                        // Apply rotation/flip transformations
                        var savedState = g.Save();
                        float centerX = drawX + w / 2f;
                        float centerY = drawY + h / 2f;

                        var matrix = new Matrix();
                        matrix.Translate(centerX, centerY);
                        if (obj.Rotation != 0f) matrix.Rotate(obj.Rotation);
                        if (obj.FlipHorizontal || obj.FlipVertical)
                            matrix.Scale(obj.FlipHorizontal ? -1 : 1, obj.FlipVertical ? -1 : 1);
                        matrix.Translate(-centerX, -centerY);
                        g.Transform = matrix;

                        g.DrawImage(obj.Image, new RectangleF(drawX, drawY, w, h));
                        g.Restore(savedState);
                        matrix.Dispose();
                    }
                    else
                    {
                        g.DrawImage(obj.Image, new RectangleF(drawX, drawY, w, h));
                    }
                }
                catch { continue; }

                bool isSel = selectedObjects.Contains(obj);
                if (isSel)
                {
                    bool isLocked = lockedObjects.Contains(obj);
                    Color selectColor = isLocked ? HolographicTheme.ButtonDanger : HolographicTheme.SelectionCyan;

                    // Draw selection rectangle (or skew quadrilateral)
                    if (obj.HasSkew || (isSkewMode && obj == selectedObject))
                    {
                        // Draw skew outline
                        var corners = GetSkewCorners(obj, drawX, drawY, w, h);
                        using (Pen selectPen = new Pen(selectColor, 2))
                        {
                            g.DrawLine(selectPen, corners[0], corners[1]);
                            g.DrawLine(selectPen, corners[1], corners[2]);
                            g.DrawLine(selectPen, corners[2], corners[3]);
                            g.DrawLine(selectPen, corners[3], corners[0]);
                        }
                    }
                    else
                    {
                        using (Pen selectPen = new Pen(selectColor, 2))
                        { g.DrawRectangle(selectPen, drawX, drawY, w, h); }

                        using (Pen glowPen = new Pen(Color.FromArgb(60, selectColor), 4))
                        { g.DrawRectangle(glowPen, drawX - 1, drawY - 1, w + 2, h + 2); }
                    }

                    // Draw skew handles when in skew mode
                    if (isSkewMode && obj == selectedObject)
                    {
                        DrawSkewHandles(g, obj, drawX, drawY, w, h);
                    }

                    float basePt = this.Font.SizeInPoints;
                    float factor = 1f + (1f - Math.Min(1f, zoom)) * 0.6f; if (factor > 1.6f) factor = 1.6f;
                    float pt = basePt * factor;
                    string zTxt = "Z:" + obj.Z;
                    string lTxt = "L:" + obj.Layer;

                    string transformTxt = "";
                    if (obj.Scale != 1.0f) transformTxt += $"S:{obj.Scale:F1} ";
                    if (obj.Rotation != 0f) transformTxt += $"R:{obj.Rotation:F0}deg ";
                    if (obj.FlipHorizontal) transformTxt += "FH ";
                    if (obj.FlipVertical) transformTxt += "FV ";
                    if (obj.HasSkew) transformTxt += "Skew ";

                    using (var measureFont = new Font(this.Font.FontFamily, pt, FontStyle.Bold, GraphicsUnit.Point))
                    {
                        var lSize = e.Graphics.MeasureString(lTxt, measureFont);
                        DrawOutlinedText(g, zTxt, new PointF(drawX + 2, drawY + 2), selectColor, pt);
                        DrawOutlinedText(g, lTxt, new PointF(drawX + w - lSize.Width - 2, drawY + 2), selectColor, pt);

                        if (!string.IsNullOrEmpty(transformTxt))
                        {
                            DrawOutlinedText(g, transformTxt.Trim(), new PointF(drawX + 2, drawY + h - pt - 4), Color.Yellow, pt * 0.85f);
                        }
                    }
                }
            } // End foreach

            // Show Overlap layer: tint every screen pixel where a piece drawn
            // LATER (painter order) covered opaque pixels of a piece drawn
            // EARLIER. Cached bitmap anchored at the build-time canvas offset;
            // panning just translates it (see region Show Overlap Layer).
            if (showOverlapLayer)
            {
                try
                {
                    EnsureOverlapOverlayValid(minZFilter, maxZFilter);
                    if (overlapBitmap != null && overlapPixelCount > 0)
                    {
                        g.DrawImage(overlapBitmap,
                            canvasOffset.X - overlapBuildOffsetX,
                            canvasOffset.Y - overlapBuildOffsetY);
                    }
                }
                catch { }
            }

            // Draw marquee
            if (isMarquee)
            {
                var fill = marqueeDeselect ? HolographicTheme.MarqueeDeselect : HolographicTheme.MarqueeSelect;
                var borderColor = marqueeDeselect ? Color.FromArgb(255, 100, 100) : HolographicTheme.CyanAccent;
                using (var brush = new SolidBrush(fill)) g.FillRectangle(brush, marqueeRect);
                using (var pen = new Pen(borderColor, 1)) g.DrawRectangle(pen, marqueeRect);
            }

            // Draw skew mode indicator
            if (isSkewMode)
            {
                using (var font = new Font("Consolas", 10, FontStyle.Bold))
                using (var brush = new SolidBrush(Color.FromArgb(200, Color.Yellow)))
                {
                    g.DrawString("SKEW MODE (Press Esc to exit, Enter to apply)", font, brush, 10, 10);
                }
            }

            // Draw rotate mode indicator and handle
            if (isRotateMode && selectedObject != null)
            {
                using (var font = new Font("Consolas", 10, FontStyle.Bold))
                using (var brush = new SolidBrush(Color.FromArgb(200, Color.Orange)))
                {
                    g.DrawString($"ROTATE MODE - {selectedObject.Rotation:F1}deg (Drag handle to rotate, Esc to exit)", font, brush, 10, 10);
                }

                // Draw rotation handle
                float drawX, drawY, w, h;
                GetObjectDrawPosition(selectedObject, out drawX, out drawY, out w, out h);
                DrawRotationHandle(g, selectedObject, drawX, drawY, w, h);
            }
        }

        #region Skew/Distort Methods

        /// <summary>
        /// Get the 4 corners of a skewed object in screen coordinates
        /// </summary>
        private PointF[] GetSkewCorners(PlacedObject obj, float drawX, float drawY, float w, float h)
        {
            return new PointF[]
            {
 new PointF(drawX + obj.SkewTopLeft.X * zoom, drawY + obj.SkewTopLeft.Y * zoom),                           // Top-Left
 new PointF(drawX + w + obj.SkewTopRight.X * zoom, drawY + obj.SkewTopRight.Y * zoom),                     // Top-Right
 new PointF(drawX + w + obj.SkewBottomRight.X * zoom, drawY + h + obj.SkewBottomRight.Y * zoom),           // Bottom-Right
 new PointF(drawX + obj.SkewBottomLeft.X * zoom, drawY + h + obj.SkewBottomLeft.Y * zoom)                  // Bottom-Left
            };
        }

        /// <summary>
        /// Draw the skew/distort handles at each corner
        /// </summary>
        private void DrawSkewHandles(Graphics g, PlacedObject obj, float drawX, float drawY, float w, float h)
        {
            var corners = GetSkewCorners(obj, drawX, drawY, w, h);
            int handleSize = SKEW_HANDLE_SIZE;

            for (int i = 0; i < 4; i++)
            {
                var corner = corners[i];
                var handleRect = new RectangleF(corner.X - handleSize / 2f, corner.Y - handleSize / 2f, handleSize, handleSize);

                // Different color for the corner being dragged
                Color handleColor = (skewDraggingCorner == i) ? Color.Yellow : Color.Magenta;
                Color borderColor = Color.White;

                using (var brush = new SolidBrush(handleColor))
                using (var pen = new Pen(borderColor, 1.5f))
                {
                    g.FillEllipse(brush, handleRect);
                    g.DrawEllipse(pen, handleRect);
                }
            }
        }

        /// <summary>
        /// Draw an image with skew/perspective distortion using the 4 corner points
        /// </summary>
        private void DrawSkewedImage(Graphics g, PlacedObject obj, float drawX, float drawY, float w, float h)
        {
            var corners = GetSkewCorners(obj, drawX, drawY, w, h);

            // For perspective transform, we need to draw the image mapped to 4 arbitrary points
            // GDI+ doesn't support true perspective, so we use DrawImage with parallelogram (3 points)
            // For better results, we'll approximate by drawing in strips

            try
            {
                // Use parallelogram approximation (top-left, top-right, bottom-left)
                PointF[] destPoints = new PointF[]
                {
 corners[0], // Top-left
 corners[1], // Top-right
 corners[3]  // Bottom-left
                };

                g.InterpolationMode = InterpolationMode.Bilinear;
                g.DrawImage(obj.Image, destPoints);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
            }
            catch
            {
                // Fallback to normal drawing if skew fails
                g.DrawImage(obj.Image, new RectangleF(drawX, drawY, w, h));
            }
        }

        /// <summary>
        /// Hit test for skew handles - returns corner index (0-3) or -1 if no hit
        /// </summary>
        private int HitTestSkewHandle(PlacedObject obj, float drawX, float drawY, float w, float h, Point mousePos)
        {
            var corners = GetSkewCorners(obj, drawX, drawY, w, h);

            for (int i = 0; i < 4; i++)
            {
                float dx = mousePos.X - corners[i].X;
                float dy = mousePos.Y - corners[i].Y;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                if (dist <= SKEW_HANDLE_HIT_RADIUS)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// True when the image can be safely measured/drawn. A disposed GDI+
        /// Bitmap throws ArgumentException ("Parameter is not valid") on
        /// Width/Height, so null-checks alone are not enough.
        /// </summary>
        private static bool IsImageUsable(System.Drawing.Image img)
        {
            if (img == null) return false;
            try { var w = img.Width; var h = img.Height; return w > 0 && h > 0; }
            catch { return false; }
        }

        /// <summary>
        /// Get the screen position for drawing the selected object
        /// </summary>
        private void GetObjectDrawPosition(PlacedObject obj, out float drawX, out float drawY, out float w, out float h)
        {
            float objScale = obj.Scale * zoom;
            float zOffset = obj.Z * Z_PIXEL * zoom;

            float isoX = (obj.GridX - obj.GridY) * (TILE_WIDTH / 2f);
            float isoY = (obj.GridX + obj.GridY) * (TILE_HEIGHT / 2f);
            float screenX = canvasOffset.X + isoX * zoom + obj.PixelOffsetX * zoom;
            float screenY = canvasOffset.Y + isoY * zoom + obj.PixelOffsetY * zoom;

            try { w = obj.Image.Width * objScale; h = obj.Image.Height * objScale; }
            catch { w = 0; h = 0; }
            float tileHalfHeight = (TILE_HEIGHT / 2f) * zoom;

            drawX = screenX - w / 2f;
            drawY = screenY - h + tileHalfHeight - zOffset;
        }

        /// <summary>
        /// Toggle skew mode for the selected object
        /// </summary>
        private void ToggleSkewMode()
        {
            if (selectedObject == null || lockedObjects.Contains(selectedObject))
            {
                isSkewMode = false;
                return;
            }

            isSkewMode = !isSkewMode;
            skewDraggingCorner = -1;

            if (isSkewMode)
            {
                outputTextBox.AppendText("Skew mode enabled - drag corners to distort. Press Esc to cancel, Enter to apply.\r\n");
            }
            else
            {
                outputTextBox.AppendText("Skew mode disabled.\r\n");
            }

            designPictureBox.Invalidate();
        }

        /// <summary>
        /// Reset skew on selected objects
        /// </summary>
        private void ResetSelectedObjectsSkew()
        {
            var targets = GetSelectedObjectsForTransform();
            if (targets.Count == 0) return;

            PushUndo();
            foreach (var obj in targets)
            {
                obj.ResetSkew();
            }
            designPictureBox.Invalidate();
            outputTextBox.AppendText("Skew reset.\r\n");
        }

        #endregion

        #region Free Rotation Mode Methods

        /// <summary>
        /// Toggle free rotation mode for the selected object
        /// </summary>
        private void ToggleRotateMode()
        {
            if (selectedObject == null || lockedObjects.Contains(selectedObject))
            {
                isRotateMode = false;
                return;
            }

            // Exit skew mode if entering rotate mode
            if (!isRotateMode && isSkewMode)
            {
                isSkewMode = false;
            }

            isRotateMode = !isRotateMode;
            isRotateDragging = false;

            if (isRotateMode)
            {
                outputTextBox.AppendText("Rotate mode enabled - drag the handle to rotate freely. Press Esc to exit.\r\n");
            }
            else
            {
                outputTextBox.AppendText("Rotate mode disabled.\r\n");
            }

            designPictureBox.Invalidate();
        }

        /// <summary>
        /// Draw the rotation handle (circular handle on a line from center)
        /// </summary>
        private void DrawRotationHandle(Graphics g, PlacedObject obj, float drawX, float drawY, float w, float h)
        {
            // Calculate center of object
            float centerX = drawX + w / 2f;
            float centerY = drawY + h / 2f;

            // Calculate handle position based on current rotation
            float handleDistance = ROTATE_HANDLE_DISTANCE * zoom;
            float angleRad = (float)((obj.Rotation - 90) * Math.PI / 180.0); // -90 to put handle at top when rotation is 0
            float handleX = centerX + (float)Math.Cos(angleRad) * handleDistance;
            float handleY = centerY + (float)Math.Sin(angleRad) * handleDistance;

            // Draw line from center to handle
            using (var linePen = new Pen(Color.Orange, 2f))
            {
                linePen.DashStyle = DashStyle.Dash;
                g.DrawLine(linePen, centerX, centerY, handleX, handleY);
            }

            // Draw center point
            float centerSize = 6;
            using (var centerBrush = new SolidBrush(Color.Orange))
            {
                g.FillEllipse(centerBrush, centerX - centerSize / 2, centerY - centerSize / 2, centerSize, centerSize);
            }

            // Draw rotation handle
            float handleSize = ROTATE_HANDLE_SIZE;
            var handleRect = new RectangleF(handleX - handleSize / 2f, handleY - handleSize / 2f, handleSize, handleSize);

            Color handleColor = isRotateDragging ? Color.Yellow : Color.Orange;
            using (var brush = new SolidBrush(handleColor))
            using (var pen = new Pen(Color.White, 2f))
            {
                g.FillEllipse(brush, handleRect);
                g.DrawEllipse(pen, handleRect);
            }

            // Draw rotation arc indicator
            using (var arcPen = new Pen(Color.FromArgb(100, Color.Orange), 1.5f))
            {
                float arcRadius = handleDistance * 0.7f;
                g.DrawArc(arcPen, centerX - arcRadius, centerY - arcRadius, arcRadius * 2, arcRadius * 2, -90, obj.Rotation);
            }
        }

        /// <summary>
        /// Get the position of the rotation handle
        /// </summary>
        private PointF GetRotationHandlePosition(PlacedObject obj, float drawX, float drawY, float w, float h)
        {
            float centerX = drawX + w / 2f;
            float centerY = drawY + h / 2f;

            float handleDistance = ROTATE_HANDLE_DISTANCE * zoom;
            float angleRad = (float)((obj.Rotation - 90) * Math.PI / 180.0);
            float handleX = centerX + (float)Math.Cos(angleRad) * handleDistance;
            float handleY = centerY + (float)Math.Sin(angleRad) * handleDistance;

            return new PointF(handleX, handleY);
        }

        /// <summary>
        /// Get the center position of an object
        /// </summary>
        private PointF GetObjectCenter(PlacedObject obj, float drawX, float drawY, float w, float h)
        {
            return new PointF(drawX + w / 2f, drawY + h / 2f);
        }

        /// <summary>
        /// Hit test for rotation handle
        /// </summary>
        private bool HitTestRotationHandle(PlacedObject obj, float drawX, float drawY, float w, float h, Point mousePos)
        {
            var handlePos = GetRotationHandlePosition(obj, drawX, drawY, w, h);

            float dx = mousePos.X - handlePos.X;
            float dy = mousePos.Y - handlePos.Y;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);

            return dist <= ROTATE_HANDLE_HIT_RADIUS;
        }

        /// <summary>
        /// Calculate angle from center to mouse position
        /// </summary>
        private float CalculateAngleFromCenter(PointF center, Point mousePos)
        {
            float dx = mousePos.X - center.X;
            float dy = mousePos.Y - center.Y;

            // Calculate angle in degrees, with 0 at top and increasing clockwise
            float angleRad = (float)Math.Atan2(dy, dx);
            float angleDeg = (float)(angleRad * 180.0 / Math.PI) + 90; // +90 to make 0 at top

            // Normalize to 0-360
            while (angleDeg < 0) angleDeg += 360;
            while (angleDeg >= 360) angleDeg -= 360;

            return angleDeg;
        }

        #endregion

        #region Transformation Methods

        private void ScaleSelectedObjects(float delta)
        {
            var targets = GetSelectedObjectsForTransform();
            if (targets.Count == 0) return;

            PushUndo();
            foreach (var obj in targets)
            {
                obj.Scale = Math.Max(SCALE_MIN, Math.Min(SCALE_MAX, obj.Scale + delta));
            }
            designPictureBox.Invalidate();
            UpdateTransformStatus();
        }

        private void SetSelectedObjectsScale(float scale)
        {
            var targets = GetSelectedObjectsForTransform();
            if (targets.Count == 0) return;

            PushUndo();
            foreach (var obj in targets)
            {
                obj.Scale = Math.Max(SCALE_MIN, Math.Min(SCALE_MAX, scale));
            }
            designPictureBox.Invalidate();
            UpdateTransformStatus();
        }

        private void RotateSelectedObjects(float degrees)
        {
            var targets = GetSelectedObjectsForTransform();
            if (targets.Count == 0) return;

            PushUndo();
            foreach (var obj in targets)
            {
                obj.Rotation = (obj.Rotation + degrees) % 360f;
                if (obj.Rotation < 0) obj.Rotation += 360f;
            }
            designPictureBox.Invalidate();
            UpdateTransformStatus();
        }

        private void SetSelectedObjectsRotation(float degrees)
        {
            var targets = GetSelectedObjectsForTransform();
            if (targets.Count == 0) return;

            PushUndo();
            foreach (var obj in targets)
            {
                obj.Rotation = degrees % 360f;
                if (obj.Rotation < 0) obj.Rotation += 360f;
            }
            designPictureBox.Invalidate();
            UpdateTransformStatus();
        }

        private void FlipSelectedObjects(bool horizontal, bool vertical)
        {
            var targets = GetSelectedObjectsForTransform();
            if (targets.Count == 0) return;

            PushUndo();
            foreach (var obj in targets)
            {
                if (horizontal) obj.FlipHorizontal = !obj.FlipHorizontal;
                if (vertical) obj.FlipVertical = !obj.FlipVertical;
            }
            designPictureBox.Invalidate();
            UpdateTransformStatus();
        }

        private void MoveSelectedObjectsPixels(int dx, int dy)
        {
            var targets = GetSelectedObjectsForTransform();
            if (targets.Count == 0) return;

            PushUndo();
            foreach (var obj in targets)
            {
                obj.PixelOffsetX += dx;
                obj.PixelOffsetY += dy;
            }
            RefreshOffsetControls();
            designPictureBox.Invalidate();
        }

        /// <summary>
        /// Toolbar OffX/OffY boxes: absolute set (type the offset, every
        /// selected object takes it). Undoable per change.
        /// </summary>
        private void OffsetNumeric_Changed(object sender, EventArgs e)
        {
            if (_offsetSync) return;
            try
            {
                var targets = GetSelectedObjectsForTransform();
                if (targets.Count == 0 || offsetXNum == null || offsetYNum == null) return;
                int nx = (int)offsetXNum.Value;
                int ny = (int)offsetYNum.Value;
                bool changed = false;
                foreach (var obj in targets)
                {
                    if (obj.PixelOffsetX != nx || obj.PixelOffsetY != ny) { changed = true; break; }
                }
                if (!changed) return; // no-op: leave undo history alone
                PushUndo();
                foreach (var obj in targets)
                {
                    obj.PixelOffsetX = nx;
                    obj.PixelOffsetY = ny;
                }
                designPictureBox.Invalidate();
            }
            catch { }
        }

        /// <summary>Zero artwork offsets on the selection (scale/rotation kept).</summary>
        private void ResetSelectedObjectsOffset()
        {
            var targets = GetSelectedObjectsForTransform();
            if (targets.Count == 0) return;

            PushUndo();
            foreach (var obj in targets)
            {
                obj.PixelOffsetX = 0;
                obj.PixelOffsetY = 0;
            }
            RefreshOffsetControls();
            designPictureBox.Invalidate();
        }

        /// <summary>
        /// Mirror the primary selection's offsets into the toolbar boxes
        /// (0,0 when nothing selected). Guarded: never fights user typing.
        /// Called on a timer plus after every programmatic offset change.
        /// </summary>
        private void RefreshOffsetControls()
        {
            try
            {
                if (offsetXNum == null || offsetYNum == null) return;
                PlacedObject primary = null;
                try
                {
                    if (selectedObject != null) primary = selectedObject;
                    else if (selectedObjects.Count > 0)
                    {
                        foreach (var o in selectedObjects) { primary = o; break; }
                    }
                }
                catch { }
                int ox = primary != null ? primary.PixelOffsetX : 0;
                int oy = primary != null ? primary.PixelOffsetY : 0;
                bool has = primary != null;
                _offsetSync = true;
                try
                {
                    if (offsetXNum.Value != ox) offsetXNum.Value = Math.Max(offsetXNum.Minimum, Math.Min(offsetXNum.Maximum, ox));
                    if (offsetYNum.Value != oy) offsetYNum.Value = Math.Max(offsetYNum.Minimum, Math.Min(offsetYNum.Maximum, oy));
                    offsetXNum.Enabled = has;
                    offsetYNum.Enabled = has;
                }
                finally { _offsetSync = false; }
            }
            catch { try { _offsetSync = false; } catch { } }
        }

        private void ResetSelectedObjectsTransform()
        {
            var targets = GetSelectedObjectsForTransform();
            if (targets.Count == 0) return;

            PushUndo();
            foreach (var obj in targets)
            {
                obj.Scale = 1.0f;
                obj.Rotation = 0f;
                obj.FlipHorizontal = false;
                obj.FlipVertical = false;
                obj.PixelOffsetX = 0;
                obj.PixelOffsetY = 0;
                obj.ResetSkew();
            }
            RefreshOffsetControls();
            designPictureBox.Invalidate();
            UpdateTransformStatus();
        }

        private List<PlacedObject> GetSelectedObjectsForTransform()
        {
            var result = new List<PlacedObject>();

            if (selectedObjects.Count > 0)
            {
                result.AddRange(selectedObjects.Where(o => !lockedObjects.Contains(o) && !o.Hidden));
            }
            else if (selectedObject != null && !lockedObjects.Contains(selectedObject) && !selectedObject.Hidden)
            {
                result.Add(selectedObject);
            }

            return result;
        }

        private void UpdateTransformStatus()
        {
            if (selectedObject != null)
            {
                string info = $"Scale: {selectedObject.Scale:F2}x | Rotation: {selectedObject.Rotation:F0}deg | ";
                info += $"Flip: {(selectedObject.FlipHorizontal ? "H" : "-")}{(selectedObject.FlipVertical ? "V" : "-")} | ";
                info += $"Offset: ({selectedObject.PixelOffsetX}, {selectedObject.PixelOffsetY})";
                if (selectedObject.HasSkew) info += " | Skewed";
                outputTextBox.AppendText($"Transform: {info}\r\n");
            }
        }

        #endregion

        // --- Slice tool ---
        private void ToggleSliceMode()
        {
            if (isSliceMode)
            {
                if (sliceTarget != null && sliceTiles.Count > 0)
                {
                    ApplySliceSelection();
                }
                ExitSliceMode();
                return;
            }

            if (selectedObject == null)
            {
                outputTextBox.AppendText("Select an object to slice first.\r\n");
                return;
            }

            sliceTarget = selectedObject;
            sliceTiles.Clear();
            isSkewMode = false;
            isRotateMode = false;
            isSliceMode = true;
            designPictureBox.Cursor = Cursors.Cross;
            outputTextBox.AppendText("Slice mode: click tiles to include. Right-click or click Slice Tool again to finish.\r\n");
            designPictureBox.Invalidate();
        }

        private void ExitSliceMode()
        {
            isSliceMode = false;
            sliceTiles.Clear();
            sliceTarget = null;
            designPictureBox.Cursor = Cursors.Default;
            designPictureBox.Invalidate();
        }

        private void ApplySliceSelection()
        {
            if (sliceTarget == null || !IsImageUsable(sliceTarget.Image) || sliceTiles.Count == 0)
            {
                outputTextBox.AppendText("Nothing to slice.\r\n");
                return;
            }

            PushUndo();

            // Calculate where the target is drawn on screen
            float drawX, drawY, w, h;
            GetObjectDrawPosition(sliceTarget, out drawX, out drawY, out w, out h);

            // Prepare transforms
            float invScale = 1f / (sliceTarget.Scale * zoom);

            int created = 0;
            foreach (var tile in sliceTiles.OrderBy(t => t.X + t.Y).ThenBy(t => t.X))
            {
                var iso = GridToIso(tile.X, tile.Y);
                float halfW = (TILE_WIDTH / 2f) * zoom;
                float halfH = (TILE_HEIGHT / 2f) * zoom;

                // Screen-space slice rectangle: full width of diamond, from image top to tile bottom
                float x1 = iso.X - halfW;
                float x2 = iso.X + halfW;
                float yTop = drawY;
                float yBottom = iso.Y + halfH;
                var screenRect = new RectangleF(x1, yTop, x2 - x1, yBottom - yTop);

                // Transform rect into image space
                var rectPoints = new PointF[]
                {
             new PointF(screenRect.Left, screenRect.Top),
             new PointF(screenRect.Right, screenRect.Top),
             new PointF(screenRect.Right, screenRect.Bottom),
             new PointF(screenRect.Left, screenRect.Bottom)
                };
                using (var m = new Matrix())
                {
                    m.Translate(-drawX, -drawY);
                    m.Scale(invScale, invScale);
                    m.TransformPoints(rectPoints);
                }

                float minX = rectPoints.Min(p => p.X);
                float maxX = rectPoints.Max(p => p.X);
                float minY = rectPoints.Min(p => p.Y);
                float maxY = rectPoints.Max(p => p.Y);
                var clipRect = Rectangle.Round(RectangleF.FromLTRB(minX, minY, maxX, maxY));
                var imgRect = new Rectangle(0, 0, sliceTarget.Image.Width, sliceTarget.Image.Height);
                clipRect.Intersect(imgRect);
                if (clipRect.Width <= 0 || clipRect.Height <= 0)
                    continue;

                using (var sliceBmp = new Bitmap(clipRect.Width, clipRect.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(sliceBmp))
                    {
                        g.Clear(Color.Transparent);

                        using (var path = new GraphicsPath())
                        {
                            // Build clip parallelogram in slice bitmap space
                            var localPts = rectPoints.Select(p => new PointF(p.X - clipRect.X, p.Y - clipRect.Y)).ToArray();
                            path.AddPolygon(localPts);
                            g.SetClip(path);
                        }

                        g.DrawImage(sliceTarget.Image, new Rectangle(-clipRect.X, -clipRect.Y, sliceTarget.Image.Width, sliceTarget.Image.Height));
                    }

                    // Pixel offsets to keep slice aligned to its selected tile
                    float scaleVal = sliceTarget.Scale;
                    float sliceW = sliceBmp.Width * scaleVal * zoom;
                    float sliceH = sliceBmp.Height * scaleVal * zoom;

                    // Desired draw position on screen for this slice (match original region)
                    float desiredDrawX = drawX + clipRect.X * scaleVal * zoom;
                    float desiredDrawY = drawY + clipRect.Y * scaleVal * zoom;

                    // Compute screen targets to solve back to pixel offsets
                    float screenXTarget = desiredDrawX + sliceW / 2f;
                    float screenYTarget = desiredDrawY + sliceH - (TILE_HEIGHT / 2f) * zoom + sliceTarget.Z * Z_PIXEL * zoom;

                    // Solve pixel offsets relative to this slice's grid position
                    float isoXSlice = (tile.X - tile.Y) * (TILE_WIDTH / 2f) * zoom;
                    float isoYSlice = (tile.X + tile.Y) * (TILE_HEIGHT / 2f) * zoom;

                    int newOffsetX = (int)Math.Round((screenXTarget - canvasOffset.X - isoXSlice) / zoom);
                    int newOffsetY = (int)Math.Round((screenYTarget - canvasOffset.Y - isoYSlice) / zoom);

                    var newObj = new PlacedObject
                    {
                        Image = new Bitmap(sliceBmp),
                        GraphicId = (sliceTarget.GraphicId ?? "slice") + $"_slice_{created + 1}",
                        GridX = tile.X,
                        GridY = tile.Y,
                        Z = sliceTarget.Z,
                        Flags = sliceTarget.Flags,
                        IsoPosition = GridToIso(tile.X, tile.Y),
                        Layer = placedObjects.Count,
                        Hidden = false,
                        Scale = sliceTarget.Scale,
                        Rotation = sliceTarget.Rotation,
                        FlipHorizontal = sliceTarget.FlipHorizontal,
                        FlipVertical = sliceTarget.FlipVertical,
                        PixelOffsetX = newOffsetX,
                        PixelOffsetY = newOffsetY
                    };

                    placedObjects.Add(newObj);
                    selectedObjects.Clear();
                    selectedObjects.Add(newObj);
                    selectedObject = newObj;
                    created++;
                }
            }

            RebuildLockLists();
            designPictureBox.Invalidate();
            outputTextBox.AppendText(created > 0
                ? $"Slice created from {created} tile(s). Original left unchanged.\r\n"
                : "Slice selection produced no output.\r\n");
        }

        private void DesignPictureBox_MouseWheel(object sender, MouseEventArgs e)
        {
            float oldZoom = zoom;
            if (e.Delta > 0) zoom *= 1.1f; else zoom /= 1.1f;
            zoom = Math.Max(0.2f, Math.Min(4.0f, zoom));
            if (Math.Abs(zoom - oldZoom) > 0.0001f)
            {
                var before = new PointF((e.X - canvasOffset.X) / oldZoom, (e.Y - canvasOffset.Y) / oldZoom);
                canvasOffset.X = e.X - (int)Math.Round(before.X * zoom);
                canvasOffset.Y = e.Y - (int)Math.Round(before.Y * zoom);
            }
            designPictureBox.Invalidate();
        }

        /// <summary>
        /// UO painter's order shared by live canvas, hit-testing, and the AI
        /// composite: back-to-front by tile (GridX+GridY), then low-to-high Z.
        /// Same tile + same Z ties break by type (floors before walls/roof),
        /// then TileData height (flatter first), then manual Layer. Matches the
        /// canvas PNG export (RenderCanvasToImage).
        /// </summary>
        private IEnumerable<PlacedObject> GetPaintOrder()
        {
            return placedObjects.OrderBy(o => o, Comparer<PlacedObject>.Create(ComparePaintOrder));
        }

        private int ComparePaintOrder(PlacedObject a, PlacedObject b)
        {
            int c = (a.GridX + a.GridY).CompareTo(b.GridX + b.GridY);
            if (c != 0) return c;
            c = a.Z.CompareTo(b.Z);
            if (c != 0) return c;
            GetPaintTags(a.GraphicId, out bool aFloor, out int aH);
            GetPaintTags(b.GraphicId, out bool bFloor, out int bH);
            c = (aFloor ? 0 : 1).CompareTo(bFloor ? 0 : 1);
            if (c != 0) return c;
            c = aH.CompareTo(bH);
            if (c != 0) return c;
            return a.Layer.CompareTo(b.Layer);
        }

        // Cached TileData sort tags per art ID (flags/height are immutable per ID).
        private readonly Dictionary<string, Tuple<bool, int>> paintTagCache =
            new Dictionary<string, Tuple<bool, int>>(StringComparer.OrdinalIgnoreCase);

        private void GetPaintTags(string graphicId, out bool isFloor, out int height)
        {
            isFloor = false;
            height = 0;
            if (string.IsNullOrWhiteSpace(graphicId)) return;
            Tuple<bool, int> cached;
            if (paintTagCache.TryGetValue(graphicId.Trim(), out cached))
            {
                isFloor = cached.Item1;
                height = cached.Item2;
                return;
            }
            try
            {
                int gidVal;
                if (TryParseGraphicId(graphicId, out gidVal) && gidVal >= 0 && gidVal <= 0xFFFF
                    && tileDataReader != null && tileDataReader.IsLoaded)
                {
                    var td = tileDataReader.GetItemTile((ushort)gidVal);
                    if (td != null)
                    {
                        var f = td.Flags;
                        height = td.Height;
                        // Floor = walkable surface or bridge (mirrors MultiRules.ClassifyRole).
                        isFloor = f.HasFlag(Mul.TileFlag.Bridge)
                            || (f.HasFlag(Mul.TileFlag.Surface) && !f.HasFlag(Mul.TileFlag.Impassable))
                            || f.HasFlag(Mul.TileFlag.Surface);
                    }
                }
            }
            catch { }
            paintTagCache[graphicId.Trim()] = Tuple.Create(isFloor, height);
        }

        #region Show Overlap Layer

        // ---------------------------------------------------------------------------
        // "Show Overlap" toggle (toolbar button on the Z-filter row).
        //
        // When on, the canvas tints every screen pixel where a piece drawn
        // LATER (painter's order, GetPaintOrder) covers opaque pixels of a
        // piece drawn EARLIER. With iso front walls painting over back walls
        // this shows exactly the regions the "AI Regen as One" composite
        // cannot know about.
        //
        // Approximations (acceptable for a visual aid): skewed/rotated art is
        // sampled from its axis-aligned draw rect mapped proportionally,
        // matching the un-transformed DrawImage path; horizontal/vertical
        // flips are handled exactly; alpha >= 128 counts as opaque.
        //
        // Caching/perf: the overlay is built once into a client-sized ARGB
        // bitmap anchored to the canvasOffset at build time. Panning reuses
        // the cached bitmap via a plain translation; a rebuild happens when
        // the (scene fingerprint, zoom, Z filter bounds, client size)
        // signature changes, or when a pan would expose more than half a
        // screen of uncached area. The scene fingerprint is a cheap O(objects)
        // fold computed while the layer is on, so adds/removes/moves/Hidden
        // flips/transform changes/art swaps (new Image instance, which every
        // edit and AI-regen path produces - in-place bitmap repaints are NOT
        // detected) invalidate the cache without wiring dirty calls into
        // every editing path. Rebuilds are throttled while objects are
        // dragged; the toggle being OFF skips all of this work entirely.
        // ---------------------------------------------------------------------------

        private bool showOverlapLayer;
        private Button showOverlapButton;

        private Bitmap overlapBitmap;      // client-sized ARGB; transparent except tinted pixels
        private int overlapBuildOffsetX;   // canvasOffset.X when the cache was built
        private int overlapBuildOffsetY;   // canvasOffset.Y when the cache was built
        private int overlapPixelCount;

        // Signature of the last build; any mismatch forces a rebuild.
        private long overlapLastSceneStamp = long.MinValue;
        private float overlapLastZoom = float.NaN;
        private int overlapLastMinZ = int.MinValue;
        private int overlapLastMaxZ = int.MinValue;
        private int overlapLastClientW = -1;
        private int overlapLastClientH = -1;

        // Scratch buffers reused between rebuilds to keep GC pressure low.
        private int[] overlapScratch;      // owner pass; reused as the ARGB color buffer
        private byte[] overlapHitScratch;  // 1 = pixel covered by a later-drawn piece
        private byte[] overlapRgbaScratch; // extracted 32bpp image bytes (grows as needed)

        // Tint: semi transparent magenta fill plus a brighter 1px edge pass.
        private static readonly Color OverlapTintCore = Color.FromArgb(110, 255, 0, 200);
        private static readonly Color OverlapTintEdge = Color.FromArgb(220, 255, 0, 200);

        private const int OverlapDragRebuildThrottleMs = 80;
        private DateTime overlapLastBuildTime = DateTime.MinValue;

        /// <summary>
        /// Toolbar "Show Overlap" toggle. Magenta pixels on the canvas show
        /// where art drawn later covers art drawn earlier (painter order).
        /// </summary>
        private void ToggleShowOverlap()
        {
            showOverlapLayer = !showOverlapLayer;
            if (!showOverlapLayer) FreeOverlapOverlay();
            if (showOverlapButton != null)
            {
                showOverlapButton.BackColor = showOverlapLayer
                    ? HolographicTheme.ButtonAccent
                    : HolographicTheme.ButtonBackground;
                showOverlapButton.FlatAppearance.BorderColor = showOverlapLayer
                    ? HolographicTheme.CyanAccent
                    : HolographicTheme.BorderCyan;
            }
            outputTextBox.AppendText(showOverlapLayer
                ? "Overlap view ON - magenta pixels show where art covers other art\r\n"
                : "Overlap view OFF\r\n");
            designPictureBox.Invalidate();
        }

        /// <summary>Drops the cached overlay (and scratch buffers).</summary>
        private void FreeOverlapOverlay()
        {
            try { if (overlapBitmap != null) overlapBitmap.Dispose(); } catch { }
            overlapBitmap = null;
            overlapPixelCount = 0;
            overlapScratch = null;
            overlapHitScratch = null;
            overlapRgbaScratch = null;
        }

        /// <summary>
        /// Cheap dirty detector: one O(objects) arithmetic fold over all
        /// placed objects, called each paint while the layer is on. Catches
        /// count changes, grid/Z/pixel-offset/Layer moves, Hidden flips,
        /// transform changes and art-image swaps (by instance identity).
        /// </summary>
        private long ComputeOverlapSceneStamp()
        {
            long h = 1125899906842679L; // odd prime-ish seed
            foreach (var o in placedObjects)
            {
                unchecked
                {
                    h = h * 31 + (uint)o.GridX;
                    h = h * 31 + (uint)o.GridY;
                    h = h * 31 + (uint)o.Z;
                    h = h * 31 + (uint)o.PixelOffsetX;
                    h = h * 31 + (uint)o.PixelOffsetY;
                    h = h * 31 + (uint)o.Layer;
                    h = h * 31 + (uint)((o.Hidden ? 1 : 0) | (o.FlipHorizontal ? 2 : 0)
                        | (o.FlipVertical ? 4 : 0) | (o.HasSkew ? 8 : 0));
                    h = h * 31 + (uint)(int)(o.Scale * 100f);
                    h = h * 31 + (uint)(int)(o.Rotation * 4f);
                    h = h * 31 + (uint)(o.Image == null ? 0 : RuntimeHelpers.GetHashCode(o.Image));
                    if (o.HasSkew)
                    {
                        int sk = ((int)o.SkewTopLeft.X + (int)o.SkewTopLeft.Y)
                               ^ ((int)o.SkewTopRight.X + (int)o.SkewTopRight.Y)
                               ^ ((int)o.SkewBottomRight.X + (int)o.SkewBottomRight.Y)
                               ^ ((int)o.SkewBottomLeft.X + (int)o.SkewBottomLeft.Y);
                        h = h * 31 + (uint)sk;
                    }
                }
            }
            return h;
        }

        /// <summary>
        /// Rebuilds the cached overlay when the current view/scene signature
        /// differs from the last build. Called from DesignPictureBox_Paint.
        /// </summary>
        private void EnsureOverlapOverlayValid(int minZFilter, int maxZFilter)
        {
            int cw, ch;
            try { cw = designPictureBox.ClientSize.Width; ch = designPictureBox.ClientSize.Height; }
            catch { return; }
            if (cw <= 0 || ch <= 0) return;

            // Keep the previous frame's overlay mid-drag instead of rebuilding
            // for every mouse move; a slightly stale tint during an active
            // drag is fine.
            if (isDragging && (DateTime.UtcNow - overlapLastBuildTime).TotalMilliseconds < OverlapDragRebuildThrottleMs)
                return;

            long stamp = ComputeOverlapSceneStamp();
            bool valid = overlapBitmap != null
                && cw == overlapLastClientW && ch == overlapLastClientH
                && zoom == overlapLastZoom
                && minZFilter == overlapLastMinZ && maxZFilter == overlapLastMaxZ
                && stamp == overlapLastSceneStamp
                && Math.Abs(canvasOffset.X - overlapBuildOffsetX) <= cw / 2
                && Math.Abs(canvasOffset.Y - overlapBuildOffsetY) <= ch / 2;
            if (valid) return;

            OverlapRebuild(minZFilter, maxZFilter, cw, ch, stamp);
            overlapLastBuildTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Rasterizes the overlay: walks GetPaintOrder() with the same screen
        /// math as DesignPictureBox_Paint, maps each opaque source pixel to
        /// its axis-aligned screen rect (nearest sampling, proportional for
        /// skew/rotation), and marks pixels whose owner cell already held a
        /// DIFFERENT (earlier-drawn) object.
        /// </summary>
        private void OverlapRebuild(int minZFilter, int maxZFilter, int cw, int ch, long stamp)
        {
            int total = cw * ch;
            var owners = overlapScratch;
            if (owners == null || owners.Length < total) { owners = new int[total]; overlapScratch = owners; }
            var hit = overlapHitScratch;
            if (hit == null || hit.Length < total) { hit = new byte[total]; overlapHitScratch = hit; }
            for (int i = 0; i < total; i++) { owners[i] = -1; hit[i] = 0; }

            int paintIndex = 0; // stable id per object, even if the object gets skipped
            foreach (var obj in GetPaintOrder())
            {
                int owner = paintIndex++;

                if (obj.Hidden) continue;
                if (!IsImageUsable(obj.Image)) continue;
                if (obj.Z < minZFilter || obj.Z > maxZFilter) continue;

                var srcBmp = obj.Image as Bitmap;
                if (srcBmp == null) continue;

                // Same draw math as the live object loop above so the tint
                // lands exactly on the drawn artwork.
                float objScale = obj.Scale * zoom;
                float zOffset = obj.Z * Z_PIXEL * zoom;
                float isoX = (obj.GridX - obj.GridY) * (TILE_WIDTH / 2f);
                float isoY = (obj.GridX + obj.GridY) * (TILE_HEIGHT / 2f);
                float screenX = canvasOffset.X + isoX * zoom + obj.PixelOffsetX * zoom;
                float screenY = canvasOffset.Y + isoY * zoom + obj.PixelOffsetY * zoom;
                float w, h;
                try { w = obj.Image.Width * objScale; h = obj.Image.Height * objScale; }
                catch { continue; }
                float tileHalfHeight = (TILE_HEIGHT / 2f) * zoom;
                float drawX = screenX - w / 2f;
                float drawY = screenY - h + tileHalfHeight - zOffset;

                if (drawX >= cw || drawY >= ch || drawX + w <= 0f || drawY + h <= 0f) continue;

                int imgW, imgH;
                try { imgW = srcBmp.Width; imgH = srcBmp.Height; }
                catch { continue; }
                if (imgW <= 0 || imgH <= 0 || objScale <= 0f) continue;

                BitmapData data = null;
                try
                {
                    data = srcBmp.LockBits(new Rectangle(0, 0, imgW, imgH),
                        ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    int stride = data.Stride;
                    int bytes = stride * imgH;
                    var rgba = overlapRgbaScratch;
                    if (rgba == null || rgba.Length < bytes) { rgba = new byte[bytes]; overlapRgbaScratch = rgba; }
                    Marshal.Copy(data.Scan0, rgba, 0, bytes);
                    srcBmp.UnlockBits(data);
                    data = null;

                    bool flipH = obj.FlipHorizontal, flipV = obj.FlipVertical;

                    if (objScale >= 1f)
                    {
                        // Upscale/1:1: walk image pixels, expand each into its screen span.
                        for (int iy = 0; iy < imgH; iy++)
                        {
                            int sRow = flipV ? (imgH - 1 - iy) : iy;
                            int rowBase = sRow * stride;
                            float fy0 = drawY + iy * objScale;
                            int sy0 = (int)Math.Ceiling(fy0);
                            int sy1 = (int)Math.Ceiling(fy0 + objScale);
                            if (sy1 <= 0 || sy0 >= ch) continue;
                            if (sy0 < 0) sy0 = 0;
                            if (sy1 > ch) sy1 = ch;
                            for (int ix = 0; ix < imgW; ix++)
                            {
                                if (rgba[rowBase + ix * 4 + 3] < 128) continue;
                                int sCol = flipH ? (imgW - 1 - ix) : ix;
                                float fx0 = drawX + sCol * objScale;
                                int sx0 = (int)Math.Ceiling(fx0);
                                int sx1 = (int)Math.Ceiling(fx0 + objScale);
                                if (sx1 <= 0 || sx0 >= cw) continue;
                                if (sx0 < 0) sx0 = 0;
                                if (sx1 > cw) sx1 = cw;
                                for (int sy = sy0; sy < sy1; sy++)
                                {
                                    int cell = sy * cw + sx0;
                                    for (int sx = sx0; sx < sx1; sx++, cell++)
                                    {
                                        int prev = owners[cell];
                                        if (prev != -1 && prev != owner) hit[cell] = 1;
                                        else owners[cell] = owner;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // Downscale: walk screen pixels, nearest-sample the source.
                        int x0 = Math.Max(0, (int)Math.Ceiling(drawX));
                        int x1 = Math.Min(cw, (int)Math.Ceiling(drawX + imgW * objScale));
                        int y0 = Math.Max(0, (int)Math.Ceiling(drawY));
                        int y1 = Math.Min(ch, (int)Math.Ceiling(drawY + imgH * objScale));
                        for (int sy = y0; sy < y1; sy++)
                        {
                            int iy = (int)(((sy + 0.5f) - drawY) / objScale);
                            if (iy < 0) iy = 0; else if (iy >= imgH) iy = imgH - 1;
                            int sRow = flipV ? (imgH - 1 - iy) : iy;
                            int rowBase = sRow * stride;
                            int cell = sy * cw + x0;
                            for (int sx = x0; sx < x1; sx++, cell++)
                            {
                                int ix = (int)(((sx + 0.5f) - drawX) / objScale);
                                if (ix < 0) ix = 0; else if (ix >= imgW) ix = imgW - 1;
                                int sCol = flipH ? (imgW - 1 - ix) : ix;
                                if (rgba[rowBase + sCol * 4 + 3] < 128) continue;
                                int prev = owners[cell];
                                if (prev != -1 && prev != owner) hit[cell] = 1;
                                else owners[cell] = owner;
                            }
                        }
                    }
                }
                catch
                {
                    // Disposed/mangled image: skip this object, keep the overlay buildable.
                    if (data != null) { try { srcBmp.UnlockBits(data); } catch { } }
                    continue;
                }
            }

            // Paint pass: magenta core fill, brighter edge where a tinted
            // pixel touches an untinted neighbor (readability on busy art).
            int coreArgb = OverlapTintCore.ToArgb();
            int edgeArgb = OverlapTintEdge.ToArgb();
            int count = 0;
            for (int i = 0; i < total; i++)
            {
                if (hit[i] == 0) { owners[i] = 0; continue; }
                bool edge =
                    (i >= cw && hit[i - cw] == 0) ||
                    (i + cw < total && hit[i + cw] == 0) ||
                    (i % cw != 0 && hit[i - 1] == 0) ||
                    (i % cw != cw - 1 && hit[i + 1] == 0);
                owners[i] = edge ? edgeArgb : coreArgb;
                count++;
            }

            var bmp = new Bitmap(cw, ch, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var bd = bmp.LockBits(new Rectangle(0, 0, cw, ch),
                    ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try { Marshal.Copy(owners, 0, bd.Scan0, total); }
                finally { bmp.UnlockBits(bd); }
            }
            catch
            {
                bmp.Dispose();
                overlapBitmap = null;
                overlapPixelCount = 0;
                // Commit the failed signature so a broken view cannot cause an
                // endless rebuild-retry loop on every paint.
                OverlapCommitSignature(minZFilter, maxZFilter, cw, ch, stamp);
                return;
            }

            var oldBmp = overlapBitmap;
            overlapBitmap = bmp;
            overlapPixelCount = count;
            OverlapCommitSignature(minZFilter, maxZFilter, cw, ch, stamp);
            if (oldBmp != null && !ReferenceEquals(oldBmp, bmp))
            {
                try { oldBmp.Dispose(); } catch { }
            }
        }

        private void OverlapCommitSignature(int minZFilter, int maxZFilter, int cw, int ch, long stamp)
        {
            overlapLastSceneStamp = stamp;
            overlapLastZoom = zoom;
            overlapLastMinZ = minZFilter;
            overlapLastMaxZ = maxZFilter;
            overlapLastClientW = cw;
            overlapLastClientH = ch;
            overlapBuildOffsetX = canvasOffset.X;
            overlapBuildOffsetY = canvasOffset.Y;
        }

        #endregion

        private PlacedObject HitTestPlacedObject(Point location, bool includeLocked)
        {
            int minZFilter = minZTrackBar?.Value ?? -127;
            int maxZFilter = maxZTrackBar?.Value ?? 127;

            // Reverse paint order: first hit is the visually-top object.
            foreach (var obj in GetPaintOrder().Reverse())
            {
                if (obj.Hidden) continue;
                if (!IsImageUsable(obj.Image)) continue;
                if (!includeLocked && lockedObjects.Contains(obj)) continue;
                if (obj.Z < minZFilter || obj.Z > maxZFilter) continue;

                int w, h;
                try { w = (int)Math.Round(obj.Image.Width * zoom); h = (int)Math.Round(obj.Image.Height * zoom); }
                catch { continue; }
                Point iso = GridToIso(obj.GridX, obj.GridY);
                int drawX = iso.X - w / 2;
                int drawY = iso.Y - h + (int)Math.Round((TILE_HEIGHT / 2f) * zoom);

                if (new Rectangle(drawX, drawY, w, h).Contains(location))
                    return obj;
            }

            return null;
        }

        private void EnsureEditedImageContextMenu()
        {
            if (editedImageContextMenu != null)
                return;

            editedImageContextMenu = new ContextMenuStrip();
            saveToMulContextMenuItem = new ToolStripMenuItem("Save to MUL");
            saveToMulContextMenuItem.Click += (s, e) => SaveSelectedEditedObjectsToMul();
            editedImageContextMenu.Items.Add(saveToMulContextMenuItem);
        }

        private void DesignPictureBox_MouseDown(object sender, MouseEventArgs e)
        {
            try
            {
            if (e.Button == MouseButtons.Middle)
            { isPanning = true; panDragStart = e.Location; panOffsetStart = canvasOffset; designPictureBox.Capture = true; return; }

            if (isSliceMode)
            {
                if (e.Button == MouseButtons.Left)
                {
                    var sliceTile = IsoToGrid(e.X, e.Y);
                    if (sliceTile.X >= 0 && sliceTile.X < gridWidth && sliceTile.Y >= 0 && sliceTile.Y < gridHeight)
                    {
                        if (sliceTiles.Contains(sliceTile)) sliceTiles.Remove(sliceTile); else sliceTiles.Add(sliceTile);
                        designPictureBox.Invalidate();
                    }
                    return;
                }
                if (e.Button == MouseButtons.Right)
                {
                    if (sliceTiles.Count > 0) ApplySliceSelection();
                    ExitSliceMode();
                    return;
                }
            }

            // Check for rotation handle interaction first
            if (isRotateMode && selectedObject != null && e.Button == MouseButtons.Left)
            {
                float drawX, drawY, w, h;
                GetObjectDrawPosition(selectedObject, out drawX, out drawY, out w, out h);

                if (HitTestRotationHandle(selectedObject, drawX, drawY, w, h, e.Location))
                {
                    PushUndo();
                    isRotateDragging = true;
                    rotateDragStart = new PointF(e.X, e.Y);
                    rotateStartAngle = selectedObject.Rotation;
                    designPictureBox.Capture = true;
                    return;
                }
            }

            // Check for skew handle interaction
            if (isSkewMode && selectedObject != null && e.Button == MouseButtons.Left)
            {
                float drawX, drawY, w, h;
                GetObjectDrawPosition(selectedObject, out drawX, out drawY, out w, out h);

                int cornerHit = HitTestSkewHandle(selectedObject, drawX, drawY, w, h, e.Location);
                if (cornerHit >= 0)
                {
                    PushUndo();
                    skewDraggingCorner = cornerHit;
                    skewDragStart = new PointF(e.X, e.Y);

                    // Store the original corner offset
                    switch (cornerHit)
                    {
                        case SKEW_CORNER_TOP_LEFT: skewCornerStartOffset = selectedObject.SkewTopLeft; break;
                        case SKEW_CORNER_TOP_RIGHT: skewCornerStartOffset = selectedObject.SkewTopRight; break;
                        case SKEW_CORNER_BOTTOM_RIGHT: skewCornerStartOffset = selectedObject.SkewBottomRight; break;
                        case SKEW_CORNER_BOTTOM_LEFT: skewCornerStartOffset = selectedObject.SkewBottomLeft; break;
                    }

                    designPictureBox.Capture = true;
                    return;
                }
            }

            if (e.Button == MouseButtons.Right)
            {
                var hitObject = HitTestPlacedObject(e.Location, includeLocked: true);
                if (hitObject == null)
                    return;

                selectedObject = hitObject;
                selectedObjects.Clear();
                selectedObjects.Add(hitObject);
                isSkewMode = false;
                isRotateMode = false;
                SyncListBoxSelections();
                designPictureBox.Invalidate();

                if (!hitObject.IsEdited)
                    return;

                if (!TryParseGraphicId(hitObject.GraphicId, out _))
                {
                    outputTextBox.AppendText($"Cannot save to MUL: '{hitObject.GraphicId}' is not a valid art ID.\r\n");
                    return;
                }

                EnsureEditedImageContextMenu();
                saveToMulContextMenuItem.Enabled = true;
                saveToMulContextMenuItem.Text = "Save to MUL";
                editedImageContextMenu.Show(designPictureBox, e.Location);
                return;
            }

            if ((ModifierKeys & Keys.Alt) == Keys.Alt)
            { marqueeStart = e.Location; marqueeRect = new Rectangle(e.Location, Size.Empty); marqueeDeselect = true; isMarquee = true; designPictureBox.Capture = true; designPictureBox.Invalidate(); return; }

            bool isCtrl = (ModifierKeys & Keys.Control) == Keys.Control;

            int minZFilter = minZTrackBar?.Value ?? -127;
            int maxZFilter = maxZTrackBar?.Value ?? 127;

            var tile = IsoToGrid(e.X, e.Y);
            var onTile = GetPaintOrder().Where(o => !o.Hidden && IsImageUsable(o.Image) && o.GridX == tile.X && o.GridY == tile.Y && o.Z >= minZFilter && o.Z <= maxZFilter).ToList();
            if (onTile.Count > 1 && !isCtrl)
            { var idx = onTile.IndexOf(selectedObject); var next = (idx >= 0 && idx < onTile.Count - 1) ? onTile[idx + 1] : onTile[0]; selectedObjects.Clear(); selectedObject = next; selectedObjects.Add(next); isSkewMode = false; isRotateMode = false; SyncListBoxSelections(); designPictureBox.Invalidate(); return; }

            selectedObject = null; bool hit = false;
            // Reverse paint order: first hit is the visually-top object.
            foreach (var obj in GetPaintOrder().Reverse())
            {
                if (obj.Hidden) continue;
                if (!IsImageUsable(obj.Image)) continue;
                if (lockedObjects.Contains(obj)) continue;
                if (obj.Z < minZFilter || obj.Z > maxZFilter) continue;

                int w, h;
                try { w = (int)Math.Round(obj.Image.Width * zoom); h = (int)Math.Round(obj.Image.Height * zoom); }
                catch { continue; }
                Point iso = GridToIso(obj.GridX, obj.GridY);
                int drawX = iso.X - w / 2;
                int drawY = iso.Y - h + (int)Math.Round((TILE_HEIGHT / 2f) * zoom);
                if (new Rectangle(drawX, drawY, w, h).Contains(e.Location))
                {
                    if (isCtrl)
                    {
                        if (selectedObjects.Contains(obj))
                        {
                            selectedObjects.Remove(obj);
                            selectedObject = selectedObjects.FirstOrDefault();
                        }
                        else
                        {
                            selectedObjects.Add(obj);
                            selectedObject = obj;
                        }
                        isSkewMode = false;
                        isRotateMode = false;
                        SyncListBoxSelections();
                        designPictureBox.Invalidate();
                        return;
                    }
                    else
                    {
                        PushUndo();
                        selectedObject = obj;
                        if (!selectedObjects.Contains(obj)) { selectedObjects.Clear(); selectedObjects.Add(obj); isSkewMode = false; isRotateMode = false; }
                        SyncListBoxSelections();
                        isDragging = true; dragStartPoint = e.Location;
                        dragStartGridByObject.Clear();
                        foreach (var s in selectedObjects) dragStartGridByObject[s] = new Point(s.GridX, s.GridY);
                        anchorStartGrid = new Point(obj.GridX, obj.GridY);
                        hit = true; break;
                    }
                }
            }

            if (!hit && isCtrl)
            {
                marqueeStart = e.Location;
                marqueeRect = new Rectangle(e.Location, Size.Empty);
                marqueeDeselect = false;
                isMarquee = true;
                designPictureBox.Capture = true;
                designPictureBox.Invalidate();
                return;
            }

            if (!hit && !isCtrl) { selectedObjects.Clear(); selectedObject = null; isSkewMode = false; isRotateMode = false; SyncListBoxSelections(); }
            designPictureBox.Invalidate();
            }
            catch (Exception ex) { try { outputTextBox?.AppendText("Canvas click failed: " + ex.Message + "\r\n"); } catch { } try { designPictureBox?.Invalidate(); } catch { } }
        }

        private void DesignPictureBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (isPanning)
            { canvasOffset = new Point(panOffsetStart.X + (e.X - panDragStart.X), panOffsetStart.Y + (e.Y - panDragStart.Y)); designPictureBox.Invalidate(); return; }

            if (isSliceMode)
            {
                return;
            }

            // Handle rotation dragging
            if (isRotateDragging && selectedObject != null)
            {
                float drawX, drawY, w, h;
                GetObjectDrawPosition(selectedObject, out drawX, out drawY, out w, out h);
                var center = GetObjectCenter(selectedObject, drawX, drawY, w, h);

                // Calculate new angle based on mouse position relative to center
                float newAngle = CalculateAngleFromCenter(center, e.Location);

                // Snap to 15-degree increments if Shift is held
                if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                {
                    newAngle = (float)Math.Round(newAngle / 15f) * 15f;
                }

                // Snap to 90-degree increments if Ctrl is held
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    newAngle = (float)Math.Round(newAngle / 90f) * 90f;
                }

                selectedObject.Rotation = newAngle;
                designPictureBox.Invalidate();
                return;
            }

            // Handle skew corner dragging
            if (skewDraggingCorner >= 0 && selectedObject != null)
            {
                float dx = (e.X - skewDragStart.X) / zoom;
                float dy = (e.Y - skewDragStart.Y) / zoom;

                PointF newOffset = new PointF(skewCornerStartOffset.X + dx, skewCornerStartOffset.Y + dy);

                switch (skewDraggingCorner)
                {
                    case SKEW_CORNER_TOP_LEFT: selectedObject.SkewTopLeft = newOffset; break;
                    case SKEW_CORNER_TOP_RIGHT: selectedObject.SkewTopRight = newOffset; break;
                    case SKEW_CORNER_BOTTOM_RIGHT: selectedObject.SkewBottomRight = newOffset; break;
                    case SKEW_CORNER_BOTTOM_LEFT: selectedObject.SkewBottomLeft = newOffset; break;
                }

                designPictureBox.Invalidate();
                return;
            }

            if (isMarquee)
            { int x = Math.Min(marqueeStart.X, e.X); int y = Math.Min(marqueeStart.Y, e.Y); int mw = Math.Abs(e.X - marqueeStart.X); int mh = Math.Abs(e.Y - marqueeStart.Y); marqueeRect = new Rectangle(x, y, mw, mh); designPictureBox.Invalidate(); return; }
            if (!isDragging || selectedObject == null) return;
            Point curGrid = IsoToGrid(e.X, e.Y);
            int ddx = curGrid.X - anchorStartGrid.X; int ddy = curGrid.Y - anchorStartGrid.Y;
            foreach (var s in selectedObjects)
            {
                int gx = Math.Max(0, Math.Min(gridWidth - 1, dragStartGridByObject[s].X + ddx));
                int gy = Math.Max(0, Math.Min(gridHeight - 1, dragStartGridByObject[s].Y + ddy));
                s.GridX = gx; s.GridY = gy; s.IsoPosition = GridToIso(gx, gy);
            }
            designPictureBox.Invalidate();
        }

        private void DesignPictureBox_MouseUp(object sender, MouseEventArgs e)
        {
            // In slice mode, mouse up does nothing (selection handled on click)
            if (isSliceMode)
            {
                return;
            }

            // Handle rotation drag end
            if (isRotateDragging)
            {
                isRotateDragging = false;
                designPictureBox.Capture = false;
                UpdateTransformStatus();
                designPictureBox.Invalidate();
                return;
            }

            // Handle skew corner drag end
            if (skewDraggingCorner >= 0)
            {
                skewDraggingCorner = -1;
                designPictureBox.Capture = false;
                designPictureBox.Invalidate();
                return;
            }

            if (isPanning) { isPanning = false; designPictureBox.Capture = false; return; }
            if (isMarquee)
            {
                var rect = marqueeRect;

                int minZFilter = minZTrackBar?.Value ?? -127;
                int maxZFilter = maxZTrackBar?.Value ?? 127;

                if (marqueeDeselect)
                {
                    foreach (var obj in placedObjects.ToList())
                    {
                        if (obj.Hidden) continue;
                        if (!IsImageUsable(obj.Image)) continue;
                        if (obj.Z < minZFilter || obj.Z > maxZFilter) continue;

                        int w, h;
                        try { w = (int)Math.Round(obj.Image.Width * zoom); h = (int)Math.Round(obj.Image.Height * zoom); }
                        catch { continue; }
                        Point iso = GridToIso(obj.GridX, obj.GridY);
                        int drawX = iso.X - w / 2;
                        int drawY = iso.Y - h + (int)Math.Round((TILE_HEIGHT / 2f) * zoom);
                        var bounds = new Rectangle(drawX, drawY, w, h);
                        if (bounds.IntersectsWith(rect)) selectedObjects.Remove(obj);
                    }
                }
                else
                {
                    selectedObjects.Clear();
                    foreach (var obj in placedObjects)
                    {
                        if (obj.Hidden) continue;
                        if (!IsImageUsable(obj.Image)) continue;
                        if (obj.Z < minZFilter || obj.Z > maxZFilter) continue;

                        int w, h;
                        try { w = (int)Math.Round(obj.Image.Width * zoom); h = (int)Math.Round(obj.Image.Height * zoom); }
                        catch { continue; }
                        Point iso = GridToIso(obj.GridX, obj.GridY);
                        int drawX = iso.X - w / 2;
                        int drawY = iso.Y - h + (int)Math.Round((TILE_HEIGHT / 2f) * zoom);
                        var bounds = new Rectangle(drawX, drawY, w, h);
                        if (bounds.IntersectsWith(rect) && !lockedObjects.Contains(obj)) selectedObjects.Add(obj);
                    }
                }
                selectedObject = selectedObjects.FirstOrDefault();
                isSkewMode = false;
                isRotateMode = false;
                SyncListBoxSelections();
                isMarquee = false; marqueeDeselect = false; marqueeRect = Rectangle.Empty; designPictureBox.Capture = false;
                designPictureBox.Invalidate();
                return;
            }
            if (!isDragging || selectedObject == null) { isDragging = false; return; }
            isDragging = false; dragStartGridByObject.Clear();
            designPictureBox.Invalidate();
        }
    }
}
