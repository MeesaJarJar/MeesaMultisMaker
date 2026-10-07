using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using MeesaMultisMaker.Utils;

namespace MeesaMultisMaker
{
    public partial class GumpEditorForm
    {
        private int GetHandleAtPoint(int screenX, int screenY, PlacedGump gump)
        {
            if (gump == null || !ImageHelper.IsValidImage(gump.Image))
                return -1;

            const int handleSize = 10;
            float dx = gump.X * zoom;
            float dy = gump.Y * zoom;
            float dw = gump.Image.Width * zoom * gump.ScaleX;
            float dh = gump.Image.Height * zoom * gump.ScaleY;

            PointF[] handles = new PointF[]
            {
                new PointF(dx, dy),
                new PointF(dx + dw/2, dy),
                new PointF(dx + dw, dy),
                new PointF(dx + dw, dy + dh/2),
                new PointF(dx + dw, dy + dh),
                new PointF(dx + dw/2, dy + dh),
                new PointF(dx, dy + dh),
                new PointF(dx, dy + dh/2),
            };

            for (int i = 0; i < handles.Length; i++)
            {
                if (Math.Abs(screenX - handles[i].X) <= handleSize && Math.Abs(screenY - handles[i].Y) <= handleSize)
                    return i;
            }
            return -1;
        }

        private bool IsRotationHandle(int screenX, int screenY, PlacedGump gump)
        {
            if (gump == null || !ImageHelper.IsValidImage(gump.Image))
                return false;

            const int handleSize = 10;
            float dx = gump.X * zoom;
            float dy = gump.Y * zoom;
            float dw = gump.Image.Width * zoom * gump.ScaleX;
            float rotHandleX = dx + dw / 2;
            float rotHandleY = dy - 25;

            return Math.Abs(screenX - rotHandleX) <= handleSize && Math.Abs(screenY - rotHandleY) <= handleSize;
        }

        private void HandleResize(int cx, int cy)
        {
            if (selectedGump == null || !ImageHelper.IsValidImage(selectedGump.Image)) return;

            float centerX = selectedGump.X + startWidth / 2f;
            float centerY = selectedGump.Y + startHeight / 2f;

            // Calculate scale based on handle being dragged
            float newScaleX = startScaleX;
            float newScaleY = startScaleY;

            int deltaX = cx - transformStart.X;
            int deltaY = cy - transformStart.Y;

            int imgWidth = selectedGump.Image.Width;
            int imgHeight = selectedGump.Image.Height;

            if (imgWidth == 0 || imgHeight == 0) return;

            switch (resizeHandle)
            {
                case 0: // Top-left (scale both, anchor bottom-right)
                    newScaleX = startScaleX - (float)deltaX / imgWidth;
                    newScaleY = startScaleY - (float)deltaY / imgHeight;
                    break;
                case 1: // Top-center (scale Y only)
                    newScaleY = startScaleY - (float)deltaY / imgHeight;
                    break;
                case 2: // Top-right (scale both)
                    newScaleX = startScaleX + (float)deltaX / imgWidth;
                    newScaleY = startScaleY - (float)deltaY / imgHeight;
                    break;
                case 3: // Right-center (scale X only)
                    newScaleX = startScaleX + (float)deltaX / imgWidth;
                    break;
                case 4: // Bottom-right (scale both)
                    newScaleX = startScaleX + (float)deltaX / imgWidth;
                    newScaleY = startScaleY + (float)deltaY / imgHeight;
                    break;
                case 5: // Bottom-center (scale Y only)
                    newScaleY = startScaleY + (float)deltaY / imgHeight;
                    break;
                case 6: // Bottom-left (scale both)
                    newScaleX = startScaleX - (float)deltaX / imgWidth;
                    newScaleY = startScaleY + (float)deltaY / imgHeight;
                    break;
                case 7: // Left-center (scale X only)
                    newScaleX = startScaleX - (float)deltaX / imgWidth;
                    break;
            }

            // Maintain aspect ratio if Shift is held
            if (Control.ModifierKeys.HasFlag(Keys.Shift))
            {
                float avgScale = (newScaleX + newScaleY) / 2;
                newScaleX = newScaleY = avgScale;
            }

            // Clamp to reasonable values
            newScaleX = Math.Max(0.1f, Math.Min(10f, newScaleX));
            newScaleY = Math.Max(0.1f, Math.Min(10f, newScaleY));

            selectedGump.ScaleX = newScaleX;
            selectedGump.ScaleY = newScaleY;

            SetStatus($"Scale: {newScaleX:F2} x {newScaleY:F2}");
        }

        private void HandleRotate(int screenX, int screenY)
        {
            if (selectedGump == null || !ImageHelper.IsValidImage(selectedGump.Image)) return;

            float centerX = (selectedGump.X + selectedGump.Width / 2f) * zoom;
            float centerY = (selectedGump.Y + selectedGump.Height / 2f) * zoom;

            // Calculate angle from center to mouse
            float dx = screenX - centerX;
            float dy = screenY - centerY;
            float angle = (float)(Math.Atan2(dy, dx) * 180 / Math.PI) + 90;

            // Calculate initial angle
            float startDx = transformStart.X * zoom - centerX;
            float startDy = transformStart.Y * zoom - centerY;
            float startAngle = (float)(Math.Atan2(startDy, startDx) * 180 / Math.PI) + 90;

            float deltaAngle = angle - startAngle;
            float newRotation = startRotation + deltaAngle;

            // Snap to 15-degree increments if Shift is held
            if (Control.ModifierKeys.HasFlag(Keys.Shift))
            {
                newRotation = (float)Math.Round(newRotation / 15) * 15;
            }

            selectedGump.Rotation = newRotation % 360;
            SetStatus($"Rotation: {selectedGump.Rotation:F1}°");
        }

        // Transform methods
        private void RotateSelected(float degrees)
        {
            if (selectedGumps.Count == 0) return;
            PushUndo();
            foreach (var gump in selectedGumps)
            {
                gump.Rotation = (gump.Rotation + degrees) % 360;
            }
            canvasBox.Invalidate();
            SetStatus($"Rotated {selectedGumps.Count} gump(s) by {degrees}°");
        }

        private void ScaleSelected(float factor)
        {
            if (selectedGumps.Count == 0) return;
            PushUndo();
            foreach (var gump in selectedGumps)
            {
                gump.ScaleX *= factor;
                gump.ScaleY *= factor;
            }
            canvasBox.Invalidate();
            SetStatus($"Scaled {selectedGumps.Count} gump(s) by {factor:P0}");
        }

        private void ResetSelectedTransform()
        {
            if (selectedGumps.Count == 0) return;
            PushUndo();
            foreach (var gump in selectedGumps)
            {
                gump.Rotation = 0;
                gump.ScaleX = 1.0f;
                gump.ScaleY = 1.0f;
                gump.SkewX = 0;
                gump.SkewY = 0;
            }
            canvasBox.Invalidate();
            SetStatus($"Reset transform on {selectedGumps.Count} gump(s)");
        }

        /// <summary>
        /// Flip selected gumps horizontally by negating the ScaleX
        /// </summary>
        private void FlipSelectedHorizontal()
        {
            if (selectedGumps.Count == 0) return;
            PushUndo();
            foreach (var gump in selectedGumps)
            {
                gump.ScaleX = -gump.ScaleX;
            }
            canvasBox.Invalidate();
            SetStatus($"Flipped {selectedGumps.Count} gump(s) horizontally");
        }

        /// <summary>
        /// Flip selected gumps vertically by negating the ScaleY
        /// </summary>
        private void FlipSelectedVertical()
        {
            if (selectedGumps.Count == 0) return;
            PushUndo();
            foreach (var gump in selectedGumps)
            {
                gump.ScaleY = -gump.ScaleY;
            }
            canvasBox.Invalidate();
            SetStatus($"Flipped {selectedGumps.Count} gump(s) vertically");
        }

        private void ShowSliceDialog()
        {
            if (selectedGump == null || !ImageHelper.IsValidImage(selectedGump.Image))
            {
                SetStatus("Select a gump to slice.");
                return;
            }

            using (var dialog = new Form())
            {
                dialog.Text = "Slice Gump";
                dialog.Width = 300;
                dialog.Height = 200;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                HolographicTheme.ApplyToForm(dialog);

                var colsLabel = new Label { Text = "Columns:", Left = 20, Top = 20, Width = 80 };
                HolographicTheme.ApplyToLabel(colsLabel);
                dialog.Controls.Add(colsLabel);

                var colsUpDown = new NumericUpDown { Left = 110, Top = 18, Width = 60, Minimum = 1, Maximum = 20, Value = 2 };
                HolographicTheme.ApplyToNumericUpDown(colsUpDown);
                dialog.Controls.Add(colsUpDown);

                var rowsLabel = new Label { Text = "Rows:", Left = 20, Top = 55, Width = 80 };
                HolographicTheme.ApplyToLabel(rowsLabel);
                dialog.Controls.Add(rowsLabel);

                var rowsUpDown = new NumericUpDown { Left = 110, Top = 53, Width = 60, Minimum = 1, Maximum = 20, Value = 2 };
                HolographicTheme.ApplyToNumericUpDown(rowsUpDown);
                dialog.Controls.Add(rowsUpDown);

                var infoLabel = new Label { Text = $"Image: {selectedGump.Width}x{selectedGump.Height}", Left = 20, Top = 90, Width = 200 };
                HolographicTheme.ApplyToLabel(infoLabel);
                dialog.Controls.Add(infoLabel);

                var okBtn = CreateThemedButton("Slice", 60, 120, 80, ButtonStyle.Success);
                okBtn.Click += (s, e) => { dialog.DialogResult = DialogResult.OK; dialog.Close(); };
                dialog.Controls.Add(okBtn);

                var cancelBtn = CreateThemedButton("Cancel", 150, 120, 80, ButtonStyle.Default);
                cancelBtn.Click += (s, e) => dialog.Close();
                dialog.Controls.Add(cancelBtn);

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    SliceSelectedGump((int)colsUpDown.Value, (int)rowsUpDown.Value);
                }
            }
        }

        private void SliceSelectedGump(int cols, int rows)
        {
            if (selectedGump == null || !ImageHelper.IsValidImage(selectedGump.Image)) return;
            if (cols < 1 || rows < 1) return;

            PushUndo();

            var layer = layers[selectedGump.LayerIndex];
            var sourceImage = selectedGump.Image;
            int sliceWidth = sourceImage.Width / cols;
            int sliceHeight = sourceImage.Height / rows;
            int baseX = selectedGump.X;
            int baseY = selectedGump.Y;

            layer.Gumps.Remove(selectedGump);
            placedGumps.Remove(selectedGump);

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    var slice = new Bitmap(sliceWidth, sliceHeight, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(slice))
                    {
                        g.DrawImage(sourceImage,
                            new Rectangle(0, 0, sliceWidth, sliceHeight),
                            new Rectangle(col * sliceWidth, row * sliceHeight, sliceWidth, sliceHeight),
                            GraphicsUnit.Pixel);
                    }

                    var gump = new PlacedGump
                    {
                        GumpId = -1,
                        Image = slice,
                        X = baseX + (int)(col * sliceWidth * selectedGump.ScaleX),
                        Y = baseY + (int)(row * sliceHeight * selectedGump.ScaleY),
                        LayerIndex = selectedGump.LayerIndex,
                        ScaleX = selectedGump.ScaleX,
                        ScaleY = selectedGump.ScaleY
                    };

                    layer.Gumps.Add(gump);
                    placedGumps.Add(gump);
                }
            }

            sourceImage.Dispose();
            selectedGump = null;
            selectedGumps.Clear();

            canvasBox.Invalidate();
            SetStatus($"Sliced gump into {cols}x{rows} = {cols * rows} pieces");
        }

        /// <summary>
        /// Show dialog to adjust skew values for selected gump
        /// </summary>
        private void ShowSkewDialog()
        {
            if (selectedGump == null || !ImageHelper.IsValidImage(selectedGump.Image))
            {
                SetStatus("Select a gump to adjust skew.");
                return;
            }

            using (var dialog = new Form())
            {
                dialog.Text = "Adjust Skew";
                dialog.Width = 320;
                dialog.Height = 220;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                HolographicTheme.ApplyToForm(dialog);

                // Store original values for preview/cancel
                float originalSkewX = selectedGump.SkewX;
                float originalSkewY = selectedGump.SkewY;

                var skewXLabel = new Label { Text = "Skew X:", Left = 20, Top = 20, Width = 80 };
                HolographicTheme.ApplyToLabel(skewXLabel);
                dialog.Controls.Add(skewXLabel);

                var skewXTrackBar = new TrackBar
                {
                    Left = 100,
                    Top = 15,
                    Width = 180,
                    Minimum = -100,
                    Maximum = 100,
                    Value = (int)(selectedGump.SkewX * 100),
                    TickFrequency = 25
                };
                dialog.Controls.Add(skewXTrackBar);

                var skewXValueLabel = new Label 
                { 
                    Text = $"{selectedGump.SkewX:F2}", 
                    Left = 20, 
                    Top = 50, 
                    Width = 80,
                    ForeColor = HolographicTheme.CyanAccent
                };
                HolographicTheme.ApplyToLabel(skewXValueLabel);
                dialog.Controls.Add(skewXValueLabel);

                var skewYLabel = new Label { Text = "Skew Y:", Left = 20, Top = 80, Width = 80 };
                HolographicTheme.ApplyToLabel(skewYLabel);
                dialog.Controls.Add(skewYLabel);

                var skewYTrackBar = new TrackBar
                {
                    Left = 100,
                    Top = 75,
                    Width = 180,
                    Minimum = -100,
                    Maximum = 100,
                    Value = (int)(selectedGump.SkewY * 100),
                    TickFrequency = 25
                };
                dialog.Controls.Add(skewYTrackBar);

                var skewYValueLabel = new Label 
                { 
                    Text = $"{selectedGump.SkewY:F2}", 
                    Left = 20, 
                    Top = 110, 
                    Width = 80,
                    ForeColor = HolographicTheme.CyanAccent
                };
                HolographicTheme.ApplyToLabel(skewYValueLabel);
                dialog.Controls.Add(skewYValueLabel);

                // Live preview when trackbar changes
                skewXTrackBar.Scroll += (s, e) =>
                {
                    selectedGump.SkewX = skewXTrackBar.Value / 100f;
                    skewXValueLabel.Text = $"{selectedGump.SkewX:F2}";
                    canvasBox.Invalidate();
                };

                skewYTrackBar.Scroll += (s, e) =>
                {
                    selectedGump.SkewY = skewYTrackBar.Value / 100f;
                    skewYValueLabel.Text = $"{selectedGump.SkewY:F2}";
                    canvasBox.Invalidate();
                };

                var resetBtn = CreateThemedButton("Reset", 20, 145, 70, ButtonStyle.Warning);
                resetBtn.Click += (s, e) =>
                {
                    skewXTrackBar.Value = 0;
                    skewYTrackBar.Value = 0;
                    selectedGump.SkewX = 0;
                    selectedGump.SkewY = 0;
                    skewXValueLabel.Text = "0.00";
                    skewYValueLabel.Text = "0.00";
                    canvasBox.Invalidate();
                };
                dialog.Controls.Add(resetBtn);

                var okBtn = CreateThemedButton("OK", 130, 145, 70, ButtonStyle.Success);
                okBtn.Click += (s, e) =>
                {
                    PushUndo();
                    dialog.DialogResult = DialogResult.OK;
                    dialog.Close();
                };
                dialog.Controls.Add(okBtn);

                var cancelBtn = CreateThemedButton("Cancel", 210, 145, 70, ButtonStyle.Default);
                cancelBtn.Click += (s, e) =>
                {
                    // Restore original values
                    selectedGump.SkewX = originalSkewX;
                    selectedGump.SkewY = originalSkewY;
                    canvasBox.Invalidate();
                    dialog.Close();
                };
                dialog.Controls.Add(cancelBtn);

                dialog.ShowDialog(this);
            }

            SetStatus($"Skew adjusted: X={selectedGump.SkewX:F2}, Y={selectedGump.SkewY:F2}");
        }
    }
}
