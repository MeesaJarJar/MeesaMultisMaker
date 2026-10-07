using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using MeesaMultisMaker.Mul;

namespace MeesaMultisMaker
{
    public partial class MulViewerForm
    {
        private void PreviewBox_MouseWheel(object sender, MouseEventArgs e)
        {
            if (e.Delta > 0) AdjustZoom(ZOOM_STEP);
            else if (e.Delta < 0) AdjustZoom(-ZOOM_STEP);
        }

        private void PreviewBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isPanning = true;
                panStartPoint = e.Location;
                panStartOffsetX = panOffsetX;
                panStartOffsetY = panOffsetY;
                previewBox.Cursor = Cursors.SizeAll;
            }
        }

        private void PreviewBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (isPanning)
            {
                panOffsetX = panStartOffsetX + (e.X - panStartPoint.X);
                panOffsetY = panStartOffsetY + (e.Y - panStartPoint.Y);
                previewBox.Invalidate();
            }
        }

        private void PreviewBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && isPanning)
            {
                isPanning = false;
                previewBox.Cursor = Cursors.Hand;
            }
        }

        private void PreviewBox_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.FromArgb(30, 30, 35));
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            List<MultiComponent> comps = null;
            
            if (showingGenerated && generatedComponents != null && generatedComponents.Count > 0)
            {
                comps = generatedComponents;
            }
            else
            {
                if (multiList.SelectedItems.Count != 1) return;
                var selectedItem = multiList.SelectedItem as ListBoxEntryItem;
                if (selectedItem == null) return; 
                comps = selectedItem.Entry.Components;
            }
            
            if (comps == null || comps.Count == 0) return;

            int minX = comps.Min(c => c.X), minY = comps.Min(c => c.Y);
            int maxX = comps.Max(c => c.X), maxY = comps.Max(c => c.Y);
            int minZ = comps.Min(c => c.Z), maxZ = comps.Max(c => c.Z);
            int w = Math.Max(1, maxX - minX + 1), h = Math.Max(1, maxY - minY + 1);

            const int TILE_WIDTH = Generation.MultiRules.TileWidth;
            const int TILE_HEIGHT = Generation.MultiRules.TileHeight;
            const float Z_SCALE = Generation.MultiRules.PixelsPerZ;

            float isoWidth = (w + h) * (TILE_WIDTH / 2f);
            float isoHeight = (w + h) * (TILE_HEIGHT / 2f) + (maxZ - minZ) * Z_SCALE + 100;
            float scaleX = (previewBox.ClientSize.Width - 20) / isoWidth;
            float scaleY = (previewBox.ClientSize.Height - 40) / isoHeight;
            float baseScale = Math.Max(0.1f, Math.Min(2f, Math.Min(scaleX, scaleY)));
            float scale = baseScale * zoomLevel;

            float centerX = previewBox.ClientSize.Width / 2f + panOffsetX;
            float centerY = previewBox.ClientSize.Height / 2f + 20 + panOffsetY;

            // UO draw order: back-to-front (GX+GY), then left-to-right (GX), then bottom-Z to top-Z
            var sorted = comps
                .Select(c => new { Comp = c, GX = c.X - minX, GY = c.Y - minY })
                .OrderBy(c => c.GX + c.GY).ThenBy(c => c.GX).ThenBy(c => c.Comp.Z).ToList();

            bool showArt = showArtCheck?.Checked ?? true;

            foreach (var item in sorted)
            {
                var c = item.Comp;
                float isoX = (item.GX - item.GY) * (TILE_WIDTH / 2f) * scale;
                float isoY = (item.GX + item.GY) * (TILE_HEIGHT / 2f) * scale;
                float zOffset = (c.Z - minZ) * Z_SCALE * scale;
                float screenX = centerX + isoX + panOffsetX;
                float screenY = centerY + isoY - zOffset + panOffsetY;

                if (showArt)
                {
                    var img = LoadArtImage(c.TileId);
                    if (img != null)
                    {
                        float imgW = img.Width * scale, imgH = img.Height * scale;
                        g.DrawImage(img, screenX - imgW / 2f, screenY - imgH + (TILE_HEIGHT / 2f) * scale, imgW, imgH);
                    }
                    else DrawColoredDiamond(g, screenX, screenY, TILE_WIDTH * scale, TILE_HEIGHT * scale, c.TileId, c.Z);
                }
                else DrawColoredDiamond(g, screenX, screenY, TILE_WIDTH * scale, TILE_HEIGHT * scale, c.TileId, c.Z);
            }

            DrawPreviewCaption(g, w, h, comps.Count, minZ, maxZ);
        }

        private void DrawPreviewCaption(Graphics g, int w, int h, int partsCount, int minZ, int maxZ)
        {
            int floors = Generation.MultiRules.GetFloorLevel(maxZ) -
                         Generation.MultiRules.GetFloorLevel(minZ) + 1;

            string caption;
            using (var font = new Font(FontFamily.GenericSansSerif, 9f))
            using (var textBrush = new SolidBrush(Color.White))
            using (var bgBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
            {
                string floorInfo = floors > 1 ? $" | {floors} floors" : "";
                if (showingGenerated && generatedComponents != null && generatedComponents.Count > 0)
                    caption = $"{w}x{h} cells | {partsCount} parts | Z: {minZ}-{maxZ}{floorInfo} | Zoom: {(int)(zoomLevel * 100)}% | GENERATED";
                else
                {
                    var selectedItem = multiList.SelectedItem as ListBoxEntryItem;
                    caption = $"{w}x{h} cells | {partsCount} parts | Z: {minZ}-{maxZ}{floorInfo} | Zoom: {(int)(zoomLevel * 100)}% | Entry #{selectedItem?.Entry?.Index ?? -1}";
                }
                var sz = g.MeasureString(caption, font);
                g.FillRectangle(bgBrush, 4, 4, sz.Width + 8, sz.Height + 4);
                g.DrawString(caption, font, textBrush, 8, 6);
            }
        }

        private void DrawColoredDiamond(Graphics g, float screenX, float screenY, float tileW, float tileH, ushort tileId, short z)
        {
            unchecked
            {
                int seed = (int)(tileId * 2654435761u) ^ (z << 16);
                var color = Color.FromArgb(255, (seed >> 16) & 0xFF, (seed >> 8) & 0xFF, seed & 0xFF);
                PointF[] diamond = new PointF[]
                {
                    new PointF(screenX, screenY - tileH / 2f),
                    new PointF(screenX + tileW / 2f, screenY),
                    new PointF(screenX, screenY + tileH / 2f),
                    new PointF(screenX - tileW / 2f, screenY)
                };
                using (var br = new SolidBrush(Color.FromArgb(200, color)))
                using (var pen = new Pen(Color.FromArgb(200, 240, 240, 240), 1f))
                {
                    g.FillPolygon(br, diamond);
                    g.DrawPolygon(pen, diamond);
                }
            }
        }

        private Image LoadArtImage(int tileId)
        {
            if (artCache.ContainsKey(tileId)) return artCache[tileId];

            string mulFolder = AppConfig.Instance.FindMulFolder();
            if (string.IsNullOrEmpty(mulFolder)) mulFolder = currentMulFolder;
            if (string.IsNullOrEmpty(mulFolder)) return null;

            try
            {
                Bitmap img = null;
                var format = AppConfig.Instance.GetArtFileFormat(mulFolder);
                
                if (format == ArtFileFormat.MulFiles)
                    img = StaticArtReader.LoadStaticArt(mulFolder, (ushort)tileId);
                else if (format == ArtFileFormat.UopOnly)
                    img = UopArtReader.LoadStaticArt(mulFolder, (ushort)tileId);
                else if (format == ArtFileFormat.TecmoExpanded)
                    img = TecmoArtReader.LoadStaticArt(mulFolder, tileId);
                
                artCache[tileId] = img;
                return img;
            }
            catch
            {
                artCache[tileId] = null;
                return null;
            }
        }

        /// <summary>
        /// Overload for backward compatibility with ushort item IDs
        /// </summary>
        private Image LoadArtImage(ushort tileId)
        {
            return LoadArtImage((int)tileId);
        }
    }
}
