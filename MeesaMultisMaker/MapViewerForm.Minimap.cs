using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    partial class MapViewerForm
    {
        private PictureBox minimapOverlay;
        private Bitmap minimapOverlayImage;
        private bool minimapOverlayDragging;
        private const int MINIMAP_SIZE = 400;

        private void InitializeMinimapOverlay()
        {
            minimapOverlay = new PictureBox
            {
                Width = MINIMAP_SIZE,
                Height = MINIMAP_SIZE,
                SizeMode = PictureBoxSizeMode.Normal,
                BackColor = Color.FromArgb(200, 0, 0, 0),
                Cursor = Cursors.Cross,
                Visible = false
            };

            minimapOverlay.Paint += MinimapOverlay_Paint;
            minimapOverlay.MouseDown += MinimapOverlay_MouseDown;
            minimapOverlay.MouseMove += MinimapOverlay_MouseMove;
            minimapOverlay.MouseUp += MinimapOverlay_MouseUp;

            mapPictureBox.Controls.Add(minimapOverlay);
            PositionMinimapOverlay();
        }

        private void PositionMinimapOverlay()
        {
            if (minimapOverlay == null) return;
            minimapOverlay.Left = 8;
            // Position below the coordinate overlay text drawn at the top of the map view
            minimapOverlay.Top = 135;
            minimapOverlay.BringToFront();
        }

        /// <summary>
        /// Generates the flat radar-color minimap image for the overlay.
        /// Called after a map is loaded or changed.
        /// </summary>
        private void GenerateMinimapOverlay()
        {
            if (currentMap == null)
            {
                minimapOverlay.Visible = false;
                return;
            }

            int mapW = currentMap.Width;
            int mapH = currentMap.Height;

            // Determine scale to fit within MINIMAP_SIZE
            int scale = Math.Max(1, Math.Max(
                (mapW + MINIMAP_SIZE - 1) / MINIMAP_SIZE,
                (mapH + MINIMAP_SIZE - 1) / MINIMAP_SIZE));

            int imgW = (mapW + scale - 1) / scale;
            int imgH = (mapH + scale - 1) / scale;

            var bmp = new Bitmap(imgW, imgH, PixelFormat.Format32bppArgb);
            var bmpData = bmp.LockBits(
                new Rectangle(0, 0, imgW, imgH),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            int stride = bmpData.Stride;
            byte[] pixels = new byte[stride * imgH];

            for (int my = 0; my < imgH; my++)
            {
                for (int mx = 0; mx < imgW; mx++)
                {
                    int mapX = mx * scale;
                    int mapY = my * scale;

                    if (mapX >= mapW || mapY >= mapH)
                        continue;

                    var tile = currentMap.Tiles[mapX, mapY];
                    if (tile == null) continue;

                    Color c = GetRadarColor(tile.TileId);

                    int idx = my * stride + mx * 4;
                    pixels[idx + 0] = c.B;
                    pixels[idx + 1] = c.G;
                    pixels[idx + 2] = c.R;
                    pixels[idx + 3] = 255;
                }
            }

            Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
            bmp.UnlockBits(bmpData);

            var old = minimapOverlayImage;
            minimapOverlayImage = bmp;
            old?.Dispose();

            minimapOverlay.Width = imgW + 4;  // 2px border each side
            minimapOverlay.Height = imgH + 4;
            minimapOverlay.Visible = true;
            PositionMinimapOverlay();
            minimapOverlay.Invalidate();
        }

        /// <summary>
        /// Gets a radar-style color for a land tile ID.
        /// Uses the same palette as the existing MinimapGenerator fallback,
        /// extended with additional terrain ranges.
        /// </summary>
        private static Color GetRadarColor(ushort tileId)
        {
            // Water
            if (tileId >= 0x00A8 && tileId <= 0x00AB) return Color.FromArgb(30, 70, 160);
            if (tileId >= 0x0136 && tileId <= 0x01AF) return Color.FromArgb(30, 70, 160);

            // Deep water
            if (tileId >= 0x0198 && tileId <= 0x01A7) return Color.FromArgb(20, 50, 120);

            // Grass
            if (tileId <= 0x0003) return Color.FromArgb(40, 120, 40);
            if (tileId >= 0x0004 && tileId <= 0x0015) return Color.FromArgb(34, 139, 34);
            if (tileId >= 0x009C && tileId <= 0x00A7) return Color.FromArgb(60, 100, 40);

            // Jungle / forest
            if (tileId >= 0x0230 && tileId <= 0x023F) return Color.FromArgb(30, 100, 30);
            if (tileId >= 0x0578 && tileId <= 0x05FB) return Color.FromArgb(34, 110, 34);

            // Dirt / farmland
            if (tileId >= 0x0016 && tileId <= 0x002B) return Color.FromArgb(120, 85, 45);

            // Sand
            if (tileId >= 0x002C && tileId <= 0x004F) return Color.FromArgb(200, 180, 140);

            // Rock / mountain
            if (tileId >= 0x0050 && tileId <= 0x009B) return Color.FromArgb(90, 90, 90);
            if (tileId >= 0x00AC && tileId <= 0x00D7) return Color.FromArgb(64, 64, 64);
            if (tileId >= 0x00DC && tileId <= 0x010B) return Color.FromArgb(80, 80, 80);

            // Snow
            if (tileId >= 0x010C && tileId <= 0x011F) return Color.FromArgb(230, 230, 240);
            if (tileId >= 0x0120 && tileId <= 0x0135) return Color.FromArgb(210, 215, 225);

            // Lava
            if (tileId >= 0x01F4 && tileId <= 0x0212) return Color.FromArgb(200, 50, 10);

            // Swamp
            if (tileId >= 0x00D8 && tileId <= 0x00DB) return Color.FromArgb(60, 80, 40);

            // Void / cave
            if (tileId == 0x0002) return Color.FromArgb(20, 20, 20);

            return Color.FromArgb(100, 100, 100);
        }

        private void MinimapOverlay_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.FromArgb(200, 0, 0, 0));

            if (minimapOverlayImage == null || currentMap == null) return;

            // Draw the minimap image with a 2px border offset
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(minimapOverlayImage, 2, 2, minimapOverlayImage.Width, minimapOverlayImage.Height);

            // Draw viewport rectangle
            int mapW = currentMap.Width;
            int mapH = currentMap.Height;
            float scaleX = (float)minimapOverlayImage.Width / mapW;
            float scaleY = (float)minimapOverlayImage.Height / mapH;

            // Calculate visible map tile range from the main viewport
            int viewW = Math.Max(1, mapPictureBox.ClientSize.Width);
            int viewH = Math.Max(1, mapPictureBox.ClientSize.Height);
            float zoom = zoomFactor / 44f;
            float halfTileW = 22f * zoom;
            float halfTileH = 22f * zoom;

            // Rough estimate of visible tile radius
            int tilesAcrossHalf = (int)(viewW / Math.Max(0.1f, halfTileW * 2)) + 2;
            int tilesDownHalf = (int)(viewH / Math.Max(0.1f, halfTileH * 2)) + 2;

            float rectX = (cameraX - tilesAcrossHalf) * scaleX + 2;
            float rectY = (cameraY - tilesDownHalf) * scaleY + 2;
            float rectW = tilesAcrossHalf * 2 * scaleX;
            float rectH = tilesDownHalf * 2 * scaleY;

            // Clamp
            rectX = Math.Max(2, rectX);
            rectY = Math.Max(2, rectY);

            // Draw viewport box
            using (var pen = new Pen(Color.FromArgb(200, 0, 255, 255), 1.5f))
            {
                g.DrawRectangle(pen, rectX, rectY, rectW, rectH);
            }
            using (var brush = new SolidBrush(Color.FromArgb(30, 0, 255, 255)))
            {
                g.FillRectangle(brush, rectX, rectY, rectW, rectH);
            }

            // Draw camera crosshair
            float cx = cameraX * scaleX + 2;
            float cy = cameraY * scaleY + 2;
            using (var pen = new Pen(Color.FromArgb(200, 255, 255, 0), 1f))
            {
                g.DrawLine(pen, cx - 4, cy, cx + 4, cy);
                g.DrawLine(pen, cx, cy - 4, cx, cy + 4);
            }

            // Border
            using (var pen = new Pen(Color.FromArgb(150, 0, 200, 200), 1f))
            {
                g.DrawRectangle(pen, 0, 0, minimapOverlay.Width - 1, minimapOverlay.Height - 1);
            }
        }

        private void MinimapOverlay_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                minimapOverlayDragging = true;
                NavigateMinimapClick(e.X, e.Y);
            }
        }

        private void MinimapOverlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (minimapOverlayDragging)
            {
                NavigateMinimapClick(e.X, e.Y);
            }
        }

        private void MinimapOverlay_MouseUp(object sender, MouseEventArgs e)
        {
            minimapOverlayDragging = false;
        }

        private void NavigateMinimapClick(int pixelX, int pixelY)
        {
            if (minimapOverlayImage == null || currentMap == null) return;

            // Account for 2px border
            float imgX = pixelX - 2;
            float imgY = pixelY - 2;

            int mapW = currentMap.Width;
            int mapH = currentMap.Height;

            int targetX = (int)(imgX / minimapOverlayImage.Width * mapW);
            int targetY = (int)(imgY / minimapOverlayImage.Height * mapH);

            // Clamp to map bounds
            cameraX = Math.Max(0, Math.Min(mapW - 1, targetX));
            cameraY = Math.Max(0, Math.Min(mapH - 1, targetY));

            GenerateMapImage();
            minimapOverlay.Invalidate();
        }

        /// <summary>
        /// Refreshes the minimap viewport indicator (call after camera moves).
        /// </summary>
        private void RefreshMinimapViewport()
        {
            if (minimapOverlay != null && minimapOverlay.Visible)
                minimapOverlay.Invalidate();
        }
    }
}
