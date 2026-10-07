using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    public partial class PainterForm
    {
        private void AddLayer(string name = null)
        {
            int size = canvasBox.Width > 0 ? canvasBox.Width : (sizeCombo.SelectedIndex == 0 ? 512 : 1024);
            var layer = new Layer
            {
                Image = new Bitmap(canvasBox.Width > 0 ? canvasBox.Width : size, canvasBox.Height > 0 ? canvasBox.Height : size, PixelFormat.Format32bppArgb),
                Mask = new Bitmap(canvasBox.Width > 0 ? canvasBox.Width : size, canvasBox.Height > 0 ? canvasBox.Height : size, PixelFormat.Format32bppArgb),
                Name = name ?? $"Layer {layers.Count}",
                Locked = false
            };
            using (var g = Graphics.FromImage(layer.Image)) g.Clear(Color.Transparent);
            using (var g = Graphics.FromImage(layer.Mask)) g.Clear(Color.Black);
            layers.Add(layer);
            layerUndoImage.Add(new Stack<Bitmap>());
            layerUndoMask.Add(new Stack<Bitmap>());
            activeLayerIndex = layers.Count - 1;
            RebuildLayersUI();
            UpdateCanvas();
        }

        private void DeleteLayer(int index)
        {
            if (index < 0 || index >= layers.Count) return;
            if (layers.Count == 1) return; // keep at least one
            var layer = layers[index];
            layer.Image.Dispose(); layer.Mask.Dispose(); layer.SourceImage?.Dispose();
            layers.RemoveAt(index);
            layerUndoImage.RemoveAt(index);
            layerUndoMask.RemoveAt(index);
            activeLayerIndex = Math.Max(0, Math.Min(activeLayerIndex, layers.Count - 1));
            RebuildLayersUI();
            UpdateCanvas();
        }

        private void ToggleLockActiveLayer()
        {
            if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count) return;
            layers[activeLayerIndex].Locked = !layers[activeLayerIndex].Locked;
            RebuildLayersUI();
        }

        private void MoveLayer(int index, int delta)
        {
            if (index < 0 || index >= layers.Count) return;
            int newIndex = index + delta;
            if (newIndex < 0 || newIndex >= layers.Count) return;
            var tmp = layers[newIndex]; layers[newIndex] = layers[index]; layers[index] = tmp;
            var tut = layerUndoImage[newIndex]; layerUndoImage[newIndex] = layerUndoImage[index]; layerUndoImage[index] = tut;
            var tmu = layerUndoMask[newIndex]; layerUndoMask[newIndex] = layerUndoMask[index]; layerUndoMask[index] = tmu;
            activeLayerIndex = newIndex;
            RebuildLayersUI();
            UpdateCanvas();
        }

        private void SelectLayer(int index)
        {
            if (index < 0 || index >= layers.Count) return;
            activeLayerIndex = index;
            RebuildLayersUI();
        }

        private void RebuildLayersUI()
        {
            suppressLayerListEvents = true;
            layersListBox.Items.Clear();
            for (int i = 0; i < layers.Count; i++)
            {
                var l = layers[i];
                string txt = (i == activeLayerIndex ? "> " : "  ") + l.Name + (l.Locked ? " (locked)" : "") + (l.Visible ? "" : " (hidden)") + $" [{(int)(l.Opacity*100)}%]";
                layersListBox.Items.Add(txt);
            }
            if (activeLayerIndex >= 0 && activeLayerIndex < layersListBox.Items.Count)
                layersListBox.SelectedIndex = activeLayerIndex;
            suppressLayerListEvents = false;
        }

        private void LayersListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (suppressLayerListEvents) return;
            if (layersListBox.SelectedIndex >= 0) SelectLayer(layersListBox.SelectedIndex);
        }

        private void PasteFromClipboard()
        {
            if (!Clipboard.ContainsImage()) return;

            using (var img = Clipboard.GetImage())
            {
                if (img == null) return;

                // Paste as a new layer at original size/aspect ratio (no auto-fit scaling)
                AddLayer($"Paste {layers.Count}");
                if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count) return;

                var layer = layers[activeLayerIndex];
                int x = (layer.Image.Width - img.Width) / 2;
                int y = (layer.Image.Height - img.Height) / 2;

                layer.SourceImage?.Dispose();
                layer.SourceImage = new Bitmap(img);
                layer.SourceOffset = new PointF(x, y);
                layer.SourceScale = 1.0f;

                SetActiveTool(ToolMode.Move);
                UpdateCanvas();
            }
        }

        private void MoveActiveLayerContent(int dx, int dy)
        {
            if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count) return;
            var layer = layers[activeLayerIndex];
            if (layer.Locked || !layer.Visible) return;

            if (layer.SourceImage != null)
            {
                layer.SourceOffset = new PointF(layer.SourceOffset.X + dx, layer.SourceOffset.Y + dy);
                UpdateCanvas();
                return;
            }

            SaveLayerUndo(activeLayerIndex);

            using (var srcImage = new Bitmap(layer.Image))
            using (var srcMask = new Bitmap(layer.Mask))
            using (var gImage = Graphics.FromImage(layer.Image))
            using (var gMask = Graphics.FromImage(layer.Mask))
            {
                gImage.Clear(Color.Transparent);
                gImage.InterpolationMode = InterpolationMode.NearestNeighbor;
                gImage.DrawImageUnscaled(srcImage, dx, dy);

                gMask.Clear(Color.Black);
                gMask.InterpolationMode = InterpolationMode.NearestNeighbor;
                gMask.DrawImageUnscaled(srcMask, dx, dy);
            }

            UpdateCanvas();
        }

        private void ScaleActiveLayerContent(float scaleFactor)
        {
            if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count) return;
            var layer = layers[activeLayerIndex];
            if (layer.Locked || !layer.Visible) return;
            if (scaleFactor <= 0f) return;

            Rectangle bounds = GetLayerOpaqueBounds(layer.Image);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            SaveLayerUndo(activeLayerIndex);

            int newW = Math.Max(1, (int)Math.Round(bounds.Width * scaleFactor));
            int newH = Math.Max(1, (int)Math.Round(bounds.Height * scaleFactor));

            int centerX = bounds.X + bounds.Width / 2;
            int centerY = bounds.Y + bounds.Height / 2;
            var destRect = new Rectangle(centerX - newW / 2, centerY - newH / 2, newW, newH);

            using (var srcImage = new Bitmap(layer.Image))
            using (var srcMask = new Bitmap(layer.Mask))
            using (var gImage = Graphics.FromImage(layer.Image))
            using (var gMask = Graphics.FromImage(layer.Mask))
            {
                gImage.Clear(Color.Transparent);
                gImage.InterpolationMode = InterpolationMode.HighQualityBicubic;
                gImage.DrawImage(srcImage, destRect, bounds, GraphicsUnit.Pixel);

                gMask.Clear(Color.Black);
                gMask.InterpolationMode = InterpolationMode.NearestNeighbor;
                gMask.DrawImage(srcMask, destRect, bounds, GraphicsUnit.Pixel);
            }

            UpdateCanvas();
        }

        private Rectangle GetLayerOpaqueBounds(Bitmap bmp)
        {
            int minX = bmp.Width;
            int minY = bmp.Height;
            int maxX = -1;
            int maxY = -1;

            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    if (bmp.GetPixel(x, y).A == 0) continue;

                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY)
                return Rectangle.Empty;

            return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }

        private Bitmap GetCombinedMask()
        {
            if (layers.Count == 0) return new Bitmap(1,1);
            int w = layers[0].Mask.Width; int h = layers[0].Mask.Height;
            var combined = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(combined)) g.Clear(Color.Black);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                foreach (var l in layers)
                {
                    var px = l.Mask.GetPixel(x, y);
                    if (px.R > 128) { combined.SetPixel(x, y, Color.White); break; }
                }
            }
            return combined;
        }

        private void RenameLayer(int index)
        {
            if (index < 0 || index >= layers.Count) return;
            string name = RenameLayerDialog.Show("Layer name:", "Rename Layer", layers[index].Name);
            if (!string.IsNullOrEmpty(name)) { layers[index].Name = name; RebuildLayersUI(); }
        }

        private void ToggleVisibility(int index)
        {
            if (index < 0 || index >= layers.Count) return;
            layers[index].Visible = !layers[index].Visible;
            RebuildLayersUI();
            UpdateCanvas();
        }

        private Bitmap GetCombinedImage()
        {
            if (layers.Count == 0) return new Bitmap(1,1);
            int w = layers[0].Image.Width, h = layers[0].Image.Height;
            var combined = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(combined))
            {
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceOver;
                foreach (var l in layers)
                {
                    if (!l.Visible) continue;

                    using (var layerComposite = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                    using (var lg = Graphics.FromImage(layerComposite))
                    {
                        lg.CompositingMode = CompositingMode.SourceOver;
                        lg.DrawImage(l.Image, 0, 0);

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
                            g.DrawImage(layerComposite, 0, 0);
                        else
                        {
                            var cm = new ColorMatrix(); cm.Matrix33 = l.Opacity;
                            var ia = new ImageAttributes(); ia.SetColorMatrix(cm);
                            g.DrawImage(layerComposite, new Rectangle(0,0,w,h), 0,0,w,h, GraphicsUnit.Pixel, ia);
                        }
                    }
                }
            }
            return combined;
        }

        // Undo/Redo
        private void SaveLayerUndo(int index)
        {
            if (index < 0 || index >= layers.Count) return;
            if (layerUndoImage[index] == null) layerUndoImage[index] = new Stack<Bitmap>();
            if (layerUndoMask[index] == null) layerUndoMask[index] = new Stack<Bitmap>();
            // push copies
            layerUndoImage[index].Push(new Bitmap(layers[index].Image));
            layerUndoMask[index].Push(new Bitmap(layers[index].Mask));
            // cap depth: evict OLDEST, keep newest UNDO_DEPTH entries
            CapUndoStack(layerUndoImage[index]);
            CapUndoStack(layerUndoMask[index]);
        }

        private void CapUndoStack(Stack<Bitmap> stack)
        {
            if (stack.Count <= UNDO_DEPTH) return;
            var newestFirst = stack.ToArray(); // top-first = newest first
            stack.Clear();
            for (int i = Math.Min(UNDO_DEPTH, newestFirst.Length) - 1; i >= 0; i--)
                stack.Push(newestFirst[i]);
            for (int i = UNDO_DEPTH; i < newestFirst.Length; i++)
                newestFirst[i].Dispose();
        }

        private void UndoLayer()
        {
            if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count) return;
            if (layerUndoImage[activeLayerIndex] == null || layerUndoImage[activeLayerIndex].Count == 0) return;
            if (layerUndoMask[activeLayerIndex] == null || layerUndoMask[activeLayerIndex].Count == 0) return;
            var uImg = layerUndoImage[activeLayerIndex].Pop();
            var uMask = layerUndoMask[activeLayerIndex].Pop();
            layers[activeLayerIndex].Image.Dispose(); layers[activeLayerIndex].Mask.Dispose();
            layers[activeLayerIndex].Image = uImg;
            layers[activeLayerIndex].Mask = uMask;
            UpdateCanvas();
        }

        private void UndoBtn_Click(object sender, EventArgs e)
        {
            UndoLayer();
        }
    }

    public static class RenameLayerDialog
    {
        public static string Show(string text, string caption, string defaultName)
        {
            Form prompt = new Form()
            {
                Width = 400,
                Height = 200,
                Text = caption,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            Label textLabel = new Label() { Left = 50, Top = 20, Text = text, AutoSize = true };
            TextBox inputBox = new TextBox() { Left = 50, Top = 50, Width = 300, Text = defaultName };
            Button confirmation = new Button() { Text = "OK", Left = 250, Width = 100, Top = 100, DialogResult = DialogResult.OK };
            confirmation.Click += (sender, e) => { prompt.Close(); };
            prompt.Controls.Add(confirmation);
            prompt.Controls.Add(inputBox);
            prompt.Controls.Add(textLabel);
            prompt.AcceptButton = confirmation;

            prompt.ShowDialog();
            return inputBox.Text;
        }
    }
}
