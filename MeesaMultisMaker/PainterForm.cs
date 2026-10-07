using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Painter form for creating and editing images with layers support.
    /// 
    /// This is a partial class split across multiple files:
    /// - PainterForm.cs (this file): Constructor, events, dispose
    /// - PainterForm.Fields.cs: Field declarations and nested types
    /// - PainterForm.UI.cs: InitializeComponent and UI setup
    /// - PainterForm.Drawing.cs: Drawing tools and paint operations
    /// - PainterForm.Layers.cs: Layer management and undo/redo
    /// </summary>
    public partial class PainterForm : Form
    {
        public PainterForm()
        {
            InitializeComponent();
        }

        private void PainterForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Z) { UndoLayer(); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.V) { PasteFromClipboard(); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.C) { CopySelectionToClipboard(cut: false); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.X) { CopySelectionToClipboard(cut: true); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.N) { AddLayer(); e.Handled = true; }
            // Zoom shortcuts: Ctrl + +, Ctrl + -, Ctrl + 0
            if (e.Control && (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add)) { ZoomIn(); e.Handled = true; }
            if (e.Control && (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract)) { ZoomOut(); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.D0) { zoom = 1.0f; UpdateCanvas(); CenterCanvas(); e.Handled = true; }

            // Move/scale active layer content shortcuts
            if (currentTool == ToolMode.Move)
            {
                int step = e.Shift ? 10 : 1;
                if (e.KeyCode == Keys.Left) { MoveActiveLayerContent(-step, 0); e.Handled = true; }
                if (e.KeyCode == Keys.Right) { MoveActiveLayerContent(step, 0); e.Handled = true; }
                if (e.KeyCode == Keys.Up) { MoveActiveLayerContent(0, -step); e.Handled = true; }
                if (e.KeyCode == Keys.Down) { MoveActiveLayerContent(0, step); e.Handled = true; }
                if (e.KeyCode == Keys.OemOpenBrackets) { ScaleActiveLayerContent(0.9f); e.Handled = true; }
                if (e.KeyCode == Keys.Oem6) { ScaleActiveLayerContent(1.1f); e.Handled = true; }
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // Ctrl + wheel for zoom
            if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
            {
                if (e.Delta > 0) ZoomIn(); else ZoomOut();
            }
            base.OnMouseWheel(e);
        }

        private (Bitmap processed, Bitmap maskCopy) PrepareOutputBitmaps()
        {
            if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count)
                return (new Bitmap(1, 1, PixelFormat.Format32bppArgb), new Bitmap(1, 1, PixelFormat.Format32bppArgb));

            var layer = layers[activeLayerIndex];

            // Compose visible image for this layer, including non-destructive pasted source.
            Bitmap effectiveLayer = new Bitmap(layer.Image.Width, layer.Image.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(effectiveLayer))
            {
                g.Clear(Color.Transparent);
                g.DrawImage(layer.Image, 0, 0, layer.Image.Width, layer.Image.Height);

                if (layer.SourceImage != null)
                {
                    int drawW = Math.Max(1, (int)Math.Round(layer.SourceImage.Width * layer.SourceScale));
                    int drawH = Math.Max(1, (int)Math.Round(layer.SourceImage.Height * layer.SourceScale));
                    var dest = new Rectangle(
                        (int)Math.Round(layer.SourceOffset.X),
                        (int)Math.Round(layer.SourceOffset.Y),
                        drawW,
                        drawH);

                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(layer.SourceImage, dest, 0, 0, layer.SourceImage.Width, layer.SourceImage.Height, GraphicsUnit.Pixel);
                }
            }

            Bitmap processed = new Bitmap(effectiveLayer.Width, effectiveLayer.Height, PixelFormat.Format32bppArgb);
            Bitmap maskCopy = new Bitmap(layer.Mask);

            var rect = new Rectangle(0, 0, effectiveLayer.Width, effectiveLayer.Height);
            var srcData = effectiveLayer.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var maskData = layer.Mask.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var dstData = processed.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try
            {
                int bytes = Math.Abs(srcData.Stride) * layers[activeLayerIndex].Image.Height;
                var srcBuf = new byte[bytes];
                var maskBuf = new byte[bytes];
                var dstBuf = new byte[bytes];

                Marshal.Copy(srcData.Scan0, srcBuf, 0, bytes);
                Marshal.Copy(maskData.Scan0, maskBuf, 0, bytes);

                for (int i = 0; i < bytes; i += 4)
                {
                    byte b = srcBuf[i + 0];
                    byte g = srcBuf[i + 1];
                    byte r = srcBuf[i + 2];
                    byte a = srcBuf[i + 3];

                    byte maskR = maskBuf[i + 2];

                    if (maskR > 128)
                    {
                        dstBuf[i + 0] = b;
                        dstBuf[i + 1] = g;
                        dstBuf[i + 2] = r;
                        dstBuf[i + 3] = 0;
                    }
                    else
                    {
                        dstBuf[i + 0] = b;
                        dstBuf[i + 1] = g;
                        dstBuf[i + 2] = r;
                        dstBuf[i + 3] = a;
                    }
                }

                Marshal.Copy(dstBuf, 0, dstData.Scan0, bytes);
            }
            finally
            {
                effectiveLayer.UnlockBits(srcData);
                layer.Mask.UnlockBits(maskData);
                processed.UnlockBits(dstData);
                effectiveLayer.Dispose();
            }

            return (processed, maskCopy);
        }

        private void SendBtn_Click(object sender, EventArgs e)
        {
            var (processed, maskCopy) = PrepareOutputBitmaps();
            ImageReady?.Invoke(processed, maskCopy);
        }

        private void SendGumpBtn_Click(object sender, EventArgs e)
        {
            var (processed, maskCopy) = PrepareOutputBitmaps();
            ImageReadyForGump?.Invoke(processed, maskCopy);
        }

        private void Send3DBtn_Click(object sender, EventArgs e)
        {
            var (processed, maskCopy) = PrepareOutputBitmaps();
            ImageReadyFor3D?.Invoke(processed, maskCopy);
        }

        private void SendMapUnderlayBtn_Click(object sender, EventArgs e)
        {
            var (processed, maskCopy) = PrepareOutputBitmaps();
            ImageReadyForMapEditor?.Invoke(processed, maskCopy, false);
        }

        private void SendMapOverlayBtn_Click(object sender, EventArgs e)
        {
            var (processed, maskCopy) = PrepareOutputBitmaps();
            ImageReadyForMapEditor?.Invoke(processed, maskCopy, true);
        }

        private void DisposeBitmaps(System.Collections.Generic.List<Layer> layerList)
        {
            foreach (var l in layerList)
            {
                l.Image.Dispose();
                l.Mask.Dispose();
                l.SourceImage?.Dispose();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeBitmaps(layers);
                canvasBmp?.Dispose();
                maskBmp?.Dispose();
                // dispose undo stacks contents
                foreach (var stack in layerUndoImage)
                {
                    if (stack == null) continue;
                    foreach (var bmp in stack) bmp.Dispose();
                }
                foreach (var stack in layerUndoMask)
                {
                    if (stack == null) continue;
                    foreach (var bmp in stack) bmp.Dispose();
                }
                canvasBox?.Image?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}