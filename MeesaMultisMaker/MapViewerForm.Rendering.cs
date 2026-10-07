using MeesaMultisMaker.Mul;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;

namespace MeesaMultisMaker
{
    partial class MapViewerForm
    {
        /// <summary>
        /// Pixels per Z unit for isometric rendering.
        /// Sourced from <see cref="Generation.MultiRules.PixelsPerZ"/>.
        /// </summary>
        private const float Z_SCALE = Generation.MultiRules.PixelsPerZ;

        private void MapPictureBox_Resize(object sender, EventArgs e)
        {
            if (currentMap != null)
            {
                GenerateMapImage();
            }
        }

        private sbyte GetZAt(int x, int y)
        {
            if (currentMap == null) return 0;
            if (x < 0 || x >= currentMap.Width || y < 0 || y >= currentMap.Height) return 0;
            var tile = currentMap.Tiles[x, y];
            return tile?.Z ?? (sbyte)0;
        }

        private void GetTileCornerZValues(int mapX, int mapY, out sbyte zTop, out sbyte zRight, out sbyte zBottom, out sbyte zLeft)
        {
            zTop = GetZAt(mapX, mapY);
            zRight = GetZAt(mapX + 1, mapY);
            zBottom = GetZAt(mapX + 1, mapY + 1);
            zLeft = GetZAt(mapX, mapY + 1);
        }

        private void DrawDeformedLandTile(Graphics g, Image img, float screenX, float screenY,
            float halfTileW, float halfTileH, float zoom,
            sbyte zTop, sbyte zRight, sbyte zBottom, sbyte zLeft, ushort tileId)
        {
            float zOffsetTop = -zTop * Z_SCALE * zoom;
            float zOffsetRight = -zRight * Z_SCALE * zoom;
            float zOffsetBottom = -zBottom * Z_SCALE * zoom;
            float zOffsetLeft = -zLeft * Z_SCALE * zoom;

            PointF topCorner = new PointF(screenX, screenY - halfTileH + zOffsetTop);
            PointF rightCorner = new PointF(screenX + halfTileW, screenY + zOffsetRight);
            PointF bottomCorner = new PointF(screenX, screenY + halfTileH + zOffsetBottom);
            PointF leftCorner = new PointF(screenX - halfTileW, screenY + zOffsetLeft);

            if (img != null)
            {
                DrawTexturedQuad(g, img, topCorner, rightCorner, bottomCorner, leftCorner, tileId);
            }
            else
            {
                DrawSolidDeformedTile(g, topCorner, rightCorner, bottomCorner, leftCorner, tileId);
            }
        }

        private void DrawTexturedQuad(Graphics g, Image img, PointF top, PointF right, PointF bottom, PointF left, ushort tileId = 0)
        {
            int w = img.Width;
            int h = img.Height;

            PointF srcTop, srcRight, srcBottom, srcLeft;

            if (w == h && w != 44)
            {
                // Square texmap (64×64 or 128×128): map square corners
                // onto the isometric diamond for full-resolution sampling.
                srcTop = new PointF(0, 0);
                srcRight = new PointF(w - 1, 0);
                srcBottom = new PointF(w - 1, h - 1);
                srcLeft = new PointF(0, h - 1);
            }
            else
            {
                // Standard 44×44 diamond land art: use diamond midpoints.
                float hw = (w - 1) / 2f;
                float hh = (h - 1) / 2f;
                srcTop = new PointF(hw, 0);
                srcRight = new PointF(w - 1, hh);
                srcBottom = new PointF(hw, h - 1);
                srcLeft = new PointF(0, hh);
            }

            DrawTexturedTriangle(g, img, top, right, left, srcTop, srcRight, srcLeft, tileId);
            DrawTexturedTriangle(g, img, right, bottom, left, srcRight, srcBottom, srcLeft, tileId);
        }

        private static PointF[] InflateTriangle(PointF p0, PointF p1, PointF p2, float amount)
        {
            // Compute centroid and push each vertex outward by `amount` pixels.
            float cx = (p0.X + p1.X + p2.X) / 3f;
            float cy = (p0.Y + p1.Y + p2.Y) / 3f;
            return new PointF[]
            {
                Push(p0, cx, cy, amount),
                Push(p1, cx, cy, amount),
                Push(p2, cx, cy, amount)
            };
        }

        private static PointF Push(PointF pt, float cx, float cy, float amount)
        {
            float dx = pt.X - cx;
            float dy = pt.Y - cy;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001f) return pt;
            return new PointF(pt.X + dx / len * amount, pt.Y + dy / len * amount);
        }

        private void DrawTexturedTriangle(Graphics g, Image img, PointF dest0, PointF dest1, PointF dest2, PointF src0, PointF src1, PointF src2, ushort tileId = 0)
        {
            // Expand the triangle slightly so adjacent tiles overlap
            // and no black seam appears between them.
            var inflated = InflateTriangle(dest0, dest1, dest2, 0.5f);

            float sx0 = src0.X, sy0 = src0.Y, sx1 = src1.X, sy1 = src1.Y, sx2 = src2.X, sy2 = src2.Y;
            float dx0 = inflated[0].X, dy0 = inflated[0].Y, dx1 = inflated[1].X, dy1 = inflated[1].Y, dx2 = inflated[2].X, dy2 = inflated[2].Y;

            float denom = (sx0 - sx2) * (sy1 - sy2) - (sx1 - sx2) * (sy0 - sy2);
            if (Math.Abs(denom) < 0.0001f)
            {
                // Degenerate source triangle — fill with solid color.
                using (var fb = new SolidBrush(GetLandTileColorFromImage(img, tileId)))
                    g.FillPolygon(fb, inflated);
                return;
            }

            float m11 = ((dx0 - dx2) * (sy1 - sy2) - (dx1 - dx2) * (sy0 - sy2)) / denom;
            float m12 = ((dx1 - dx2) * (sx0 - sx2) - (dx0 - dx2) * (sx1 - sx2)) / denom;
            float m21 = ((dy0 - dy2) * (sy1 - sy2) - (dy1 - dy2) * (sy0 - sy2)) / denom;
            float m22 = ((dy1 - dy2) * (sx0 - sx2) - (dy0 - dy2) * (sx1 - sx2)) / denom;
            float m13 = dx2 - m11 * sx2 - m12 * sy2;
            float m23 = dy2 - m21 * sx2 - m22 * sy2;

            // Guard against NaN / Infinity from near-singular transforms.
            if (float.IsNaN(m11) || float.IsNaN(m12) || float.IsNaN(m21) || float.IsNaN(m22) ||
                float.IsNaN(m13) || float.IsNaN(m23) ||
                float.IsInfinity(m11) || float.IsInfinity(m12) || float.IsInfinity(m21) ||
                float.IsInfinity(m22) || float.IsInfinity(m13) || float.IsInfinity(m23))
            {
                using (var fb = new SolidBrush(GetLandTileColorFromImage(img, tileId)))
                    g.FillPolygon(fb, inflated);
                return;
            }

            // Use TextureBrush to fill the triangle directly.  Unlike the
            // old SetClip + g.Transform + DrawImage approach, TextureBrush
            // samples per-pixel inside the polygon and never allocates a
            // huge intermediate bitmap, so it handles extreme terrain
            // angles (steep cliffs, caves) without GDI+ exceptions.
            try
            {
                using (var transform = new Matrix(m11, m21, m12, m22, m13, m23))
                using (var texBrush = new TextureBrush(img, WrapMode.Clamp))
                {
                    texBrush.Transform = transform;
                    g.FillPolygon(texBrush, inflated);
                }
            }
            catch
            {
                // Last resort — fill with a color sampled from the texture.
                using (var fb = new SolidBrush(GetLandTileColorFromImage(img, tileId)))
                    g.FillPolygon(fb, inflated);
            }
        }

        private void DrawSolidDeformedTile(Graphics g, PointF top, PointF right, PointF bottom, PointF left, ushort tileId)
        {
            PointF[] points = new PointF[] { top, right, bottom, left };
            using (var brush = new SolidBrush(GetLandTileColor(tileId)))
            {
                g.FillPolygon(brush, points);
            }
        }

        // ================================================================
        //  Heightmap rendering helpers
        // ================================================================

        /// <summary>
        /// Draw a deformed isometric diamond filled with a solid heightmap colour.
        /// Z-offsets are applied to each corner exactly as in DrawDeformedLandTile.
        /// </summary>
        private void DrawHeightmapTile(Graphics g, float screenX, float screenY,
            float halfTileW, float halfTileH, float zoom,
            sbyte zTop, sbyte zRight, sbyte zBottom, sbyte zLeft, Color color)
        {
            float zT = -zTop * Z_SCALE * zoom;
            float zR = -zRight * Z_SCALE * zoom;
            float zB = -zBottom * Z_SCALE * zoom;
            float zL = -zLeft * Z_SCALE * zoom;

            PointF[] pts =
            {
                new PointF(screenX, screenY - halfTileH + zT),
                new PointF(screenX + halfTileW, screenY + zR),
                new PointF(screenX, screenY + halfTileH + zB),
                new PointF(screenX - halfTileW, screenY + zL)
            };

            using (var brush = new SolidBrush(color))
            {
                g.FillPolygon(brush, pts);
            }
        }

        /// <summary>
        /// Overlay contour lines on a tile's edges where the Z value crosses
        /// an elevation interval boundary (default 5).
        /// </summary>
        private void DrawContourEdges(Graphics g, float screenX, float screenY,
            float halfTileW, float halfTileH, float zoom,
            sbyte zTop, sbyte zRight, sbyte zBottom, sbyte zLeft)
        {
            const int INTERVAL = 5;

            float zT = -zTop * Z_SCALE * zoom;
            float zR = -zRight * Z_SCALE * zoom;
            float zB = -zBottom * Z_SCALE * zoom;
            float zL = -zLeft * Z_SCALE * zoom;

            PointF pTop = new PointF(screenX, screenY - halfTileH + zT);
            PointF pRight = new PointF(screenX + halfTileW, screenY + zR);
            PointF pBottom = new PointF(screenX, screenY + halfTileH + zB);
            PointF pLeft = new PointF(screenX - halfTileW, screenY + zL);

            using (var pen = new Pen(Color.FromArgb(200, 255, 255, 0), 1.5f))
            {
                if (IsContourBoundary(zTop, zRight, INTERVAL))
                    g.DrawLine(pen, pTop, pRight);
                if (IsContourBoundary(zRight, zBottom, INTERVAL))
                    g.DrawLine(pen, pRight, pBottom);
                if (IsContourBoundary(zBottom, zLeft, INTERVAL))
                    g.DrawLine(pen, pBottom, pLeft);
                if (IsContourBoundary(zLeft, zTop, INTERVAL))
                    g.DrawLine(pen, pLeft, pTop);
            }
        }

        /// <summary>
        /// Returns a representative color for a land tile by sampling the
        /// center pixel of its texture image.  Falls back to the hardcoded
        /// color table when no image is available.
        /// </summary>
        private Color GetLandTileColorFromImage(Image img, ushort tileId)
        {
            if (img != null)
            {
                try
                {
                    var bmp = img as Bitmap;
                    if (bmp != null)
                    {
                        Color c = bmp.GetPixel(bmp.Width / 2, bmp.Height / 2);
                        if (c.A > 0)
                            return Color.FromArgb(255, c.R, c.G, c.B);
                    }
                }
                catch { }
            }
            return GetLandTileColor(tileId);
        }

        /// <summary>
        /// Draws a static item image with hue recoloring and translucency applied.
        /// Hued bitmaps are cached to avoid re-computing per frame.
        /// </summary>
        private void DrawStaticImage(Graphics g, Image img, RectangleF destRect, ushort hue, bool isTranslucent, bool isPartialHue, ushort itemId = 0)
        {
            Image toDraw = img;

            if (hue > 0 && huesReader != null && huesReader.IsLoaded)
            {
                var hueKey = (itemId, hue, isPartialHue);
                if (!huedStaticCache.TryGetValue(hueKey, out var cachedHued))
                {
                    cachedHued = huesReader.ApplyHue(img, hue, isPartialHue);
                    if (cachedHued != null)
                        huedStaticCache[hueKey] = cachedHued;
                }
                if (cachedHued != null)
                    toDraw = cachedHued;
            }

            if (isTranslucent)
            {
                var cm = new ColorMatrix();
                cm.Matrix33 = 0.5f;
                using (var ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                    g.DrawImage(toDraw,
                        new Rectangle((int)destRect.X, (int)destRect.Y, (int)destRect.Width, (int)destRect.Height),
                        0, 0, toDraw.Width, toDraw.Height,
                        GraphicsUnit.Pixel, ia);
                }
            }
            else
            {
                g.DrawImage(toDraw, destRect);
            }
        }

        /// <summary>
        /// Ensure minimap is generated (starts async generation if needed)
        /// </summary>
        private void EnsureMinimapGenerated()
        {
            if (currentMap == null) return;
            if (minimapGenerator.IsGenerated) return;
            if (minimapGenerator.IsGenerating) return;
            if (minimapGenerating) return;

            minimapGenerating = true;
            int mapIndex = facetComboBox?.SelectedIndex ?? 0;

            // Start async generation
            minimapGenerator.GenerateAsync(currentMap, mapIndex, LoadTileImage, 2048, (progress) =>
            {
                // Update status on UI thread - use BeginInvoke to avoid deadlock
                try
                {
                    if (statusLabel != null && !statusLabel.IsDisposed && statusLabel.IsHandleCreated)
                    {
                        statusLabel.BeginInvoke(new Action(() =>
                        {
                            try { statusLabel.Text = $"Generating minimap... {(int)(progress * 100)}%"; }
                            catch { }
                        }));
                    }
                }
                catch { }
            }).ContinueWith(t =>
            {
                minimapGenerating = false;

                // Refresh the view on UI thread - use BeginInvoke to avoid deadlock
                try
                {
                    if (mapPictureBox != null && !mapPictureBox.IsDisposed && mapPictureBox.IsHandleCreated)
                    {
                        mapPictureBox.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (statusLabel != null && !statusLabel.IsDisposed)
                                    statusLabel.Text = "Minimap ready";
                                GenerateMapImage();
                            }
                            catch { }
                        }));
                    }
                }
                catch { }
            });
        }

        internal void GenerateMapImage()
        {
            if (currentMap == null) return;

            int viewW = Math.Max(1, mapPictureBox.ClientSize.Width);
            int viewH = Math.Max(1, mapPictureBox.ClientSize.Height);

            // Reuse the render buffer when dimensions haven't changed to avoid
            // a large GDI+ bitmap allocation on every frame.
            var bmp = renderBuffer;
            if (bmp == null || bmp.Width != viewW || bmp.Height != viewH)
            {
                // Detach the old buffer from the PictureBox before disposing it
                if (renderBuffer != null && ReferenceEquals(mapPictureBox.Image, renderBuffer))
                    mapPictureBox.Image = null;
                renderBuffer?.Dispose();
                bmp = new Bitmap(viewW, viewH, PixelFormat.Format32bppArgb);
                renderBuffer = bmp;
            }

            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);

                const int TILE_WIDTH = Generation.MultiRules.TileWidth;
                const int TILE_HEIGHT = Generation.MultiRules.TileHeight;

                float zoom = zoomFactor / 44f;
                float halfTileW = (TILE_WIDTH / 2f) * zoom;
                float halfTileH = (TILE_HEIGHT / 2f) * zoom;

                int tilesAcross = (int)(viewW / Math.Max(0.1f, halfTileW)) + 6;
                int tilesDown = (int)(viewH / Math.Max(0.1f, halfTileH)) + 6;

                // Use minimap when zoomed out far (less than ~8 pixels per tile)
                // Never switch to minimap when a heightmap visualisation is active —
                // the user needs to see the heightmap at every zoom level.
                bool useMinimapMode = zoom < 0.2f && heightmapMode == HeightmapMode.Off;

                // Limit maximum tiles for normal rendering.
                // Heightmap modes draw cheap solid-colour diamonds so we can
                // afford a higher cap for better detail when zoomed out.
                int maxTilesPerAxis = heightmapMode != HeightmapMode.Off ? 1000 : 500;
                int step = 1;
                if (!useMinimapMode && (tilesAcross > maxTilesPerAxis || tilesDown > maxTilesPerAxis))
                {
                    step = Math.Max(tilesAcross / maxTilesPerAxis, tilesDown / maxTilesPerAxis);
                    step = Math.Max(1, step);
                    tilesAcross = Math.Min(tilesAcross, maxTilesPerAxis);
                    tilesDown = Math.Min(tilesDown, maxTilesPerAxis);
                }

                int startX = cameraX - (tilesAcross * step) / 2;
                int startY = cameraY - (tilesDown * step) / 2;
                int endX = cameraX + (tilesAcross * step) / 2;
                int endY = cameraY + (tilesDown * step) / 2;

                float centerX = viewW / 2f;
                float centerY = viewH / 2f;

                // MINIMAP MODE: Use pre-generated high-quality minimap
                if (useMinimapMode && minimapGenerator.IsGenerated)
                {
                    var minimap = minimapGenerator.Minimap;
                    if (minimap == null) goto minimapDone;
                    int mmScale = minimapGenerator.Scale;

                    g.InterpolationMode = InterpolationMode.Bilinear;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    // The minimap is stored in flat map coordinates (X right, Y down)
                    // But the game renders in isometric view where:
                    //   isoX = (mapX - mapY) * halfTileW
                    //   isoY = (mapX + mapY) * halfTileH
                    // 
                    // We need to transform the minimap to isometric space
                    // Each minimap pixel represents mmScale map tiles

                    float screenPixelsPerMapTile = zoom * TILE_WIDTH;
                    float screenPixelsPerMmPixel = screenPixelsPerMapTile * mmScale;

                    // Calculate visible map range in isometric rendering
                    int visibleMapTilesX = (int)(viewW / screenPixelsPerMapTile) + 10;
                    int visibleMapTilesY = (int)(viewH / screenPixelsPerMapTile) + 10;

                    // Draw minimap pixels transformed to isometric coordinates
                    // Determine which minimap region we need to draw
                    int mmCamX = cameraX / mmScale;
                    int mmCamY = cameraY / mmScale;
                    int mmRadius = Math.Max(visibleMapTilesX, visibleMapTilesY) / mmScale + 5;

                    int mmStartX = Math.Max(0, mmCamX - mmRadius);
                    int mmStartY = Math.Max(0, mmCamY - mmRadius);
                    int mmEndX = Math.Min(minimap.Width, mmCamX + mmRadius);
                    int mmEndY = Math.Min(minimap.Height, mmCamY + mmRadius);

                    // Draw each minimap pixel as an isometric diamond
                    // Use LockBits for fast pixel access instead of GetPixel
                    // Lock the full bitmap to avoid sub-rectangle buffer overread
                    // issues with Marshal.Copy when the rect doesn't start at X=0.
                    if (mmStartX >= minimap.Width || mmStartY >= minimap.Height) goto minimapDone;
                    if (mmEndX <= 0 || mmEndY <= 0) goto minimapDone;

                    BitmapData mmData;
                    try
                    {
                        mmData = minimap.LockBits(
                            new Rectangle(0, 0, minimap.Width, minimap.Height),
                            System.Drawing.Imaging.ImageLockMode.ReadOnly,
                            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    }
                    catch { goto minimapDone; }
                    int mmStride = mmData.Stride;
                    byte[] mmPixels = new byte[mmStride * minimap.Height];
                    System.Runtime.InteropServices.Marshal.Copy(mmData.Scan0, mmPixels, 0, mmPixels.Length);
                    minimap.UnlockBits(mmData);

                    for (int mmY = mmStartY; mmY < mmEndY; mmY++)
                    {
                        int rowOffset = mmY * mmStride;
                        for (int mmX = mmStartX; mmX < mmEndX; mmX++)
                        {
                            int pxOffset = rowOffset + mmX * 4;
                            byte pB = mmPixels[pxOffset + 0];
                            byte pG = mmPixels[pxOffset + 1];
                            byte pR = mmPixels[pxOffset + 2];
                            byte pA = mmPixels[pxOffset + 3];
                            if (pA == 0) continue;

                            // Convert minimap coords back to map coords (center of this minimap pixel's region)
                            int mapX = mmX * mmScale + mmScale / 2;
                            int mapY = mmY * mmScale + mmScale / 2;

                            // Calculate isometric screen position (same formula as normal rendering)
                            int relX = mapX - cameraX;
                            int relY = mapY - cameraY;

                            float isoX = (relX - relY) * halfTileW;
                            float isoY = (relX + relY) * halfTileH;

                            float screenX = centerX + isoX;
                            float screenY = centerY + isoY;

                            // Skip if off screen
                            if (screenX < -50 || screenX > viewW + 50 || screenY < -50 || screenY > viewH + 50)
                                continue;

                            // Draw as a diamond (isometric tile shape) for proper alignment
                            float tileW = screenPixelsPerMmPixel;
                            float tileH = screenPixelsPerMmPixel;

                            PointF[] diamond = new PointF[]
                            {
                                new PointF(screenX, screenY - tileH / 2),  // Top
                                new PointF(screenX + tileW / 2, screenY),   // Right
                                new PointF(screenX, screenY + tileH / 2),  // Bottom
                                new PointF(screenX - tileW / 2, screenY)    // Left
                            };

                            using (var brush = new SolidBrush(Color.FromArgb(pA, pR, pG, pB)))
                            {
                                g.FillPolygon(brush, diamond);
                            }
                        }
                    }
                    minimapDone:;
                }
                // FALLBACK: Simple colored rectangles while minimap generates
                else if (useMinimapMode)
                {
                    // Start generating minimap in background
                    EnsureMinimapGenerated();

                    g.SmoothingMode = SmoothingMode.None;
                    g.PixelOffsetMode = PixelOffsetMode.None;

                    // Simple fallback rendering
                    int fallbackStep = Math.Max(1, Math.Max(tilesAcross, tilesDown) / 300);
                    int fallbackTilesAcross = Math.Min(300, tilesAcross / fallbackStep);
                    int fallbackTilesDown = Math.Min(300, tilesDown / fallbackStep);

                    for (int ty = 0; ty < fallbackTilesDown; ty++)
                    {
                        int mapY = startY + ty * fallbackStep;
                        if (mapY < 0 || mapY >= currentMap.Height) continue;

                        for (int tx = 0; tx < fallbackTilesAcross; tx++)
                        {
                            int mapX = startX + tx * fallbackStep;
                            if (mapX < 0 || mapX >= currentMap.Width) continue;

                            var tile = currentMap.Tiles[mapX, mapY];
                            if (tile == null) continue;

                            int relX = mapX - cameraX;
                            int relY = mapY - cameraY;

                            float isoX = (relX - relY) * halfTileW;
                            float isoY = (relX + relY) * halfTileH;

                            float screenX = centerX + isoX;
                            float screenY = centerY + isoY;

                            Color tileColor = GetLandTileColor(tile.TileId);
                            float pixelSize = Math.Max(1f, halfTileW * 2f * fallbackStep);

                            using (var brush = new SolidBrush(tileColor))
                            {
                                g.FillRectangle(brush, screenX - pixelSize / 2, screenY - pixelSize / 2, pixelSize, pixelSize);
                            }
                        }
                    }
                }
                // NORMAL MODE: Full detailed rendering
                else
                {
                    // Rendering quality settings.
                    // UO art is pixel art — NearestNeighbor keeps it crisp at all
                    // zoom levels.  Bilinear interpolation makes it look blurry.
                    // AntiAlias is still used for polygon edges (heightmap, contours,
                    // selection outlines) drawn via the Paint overlay.
                    if (isPanning && heightmapMode == HeightmapMode.Off)
                    {
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.CompositingMode = CompositingMode.SourceOver;
                        g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                        g.SmoothingMode = SmoothingMode.HighSpeed;
                    }
                    else
                    {
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.CompositingMode = CompositingMode.SourceOver;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                    }

                    // Draw generated underlay planes beneath map land/statics.
                    DrawUnderlayPlanes(g, viewW, viewH, zoom, halfTileW, halfTileH, centerX, centerY);

                    if (showStatics && !string.IsNullOrEmpty(mulFolderPath))
                    {
                        string loadFolder = mapSourceFolder ?? mulFolderPath;
                        int mapIndex = facetComboBox?.SelectedIndex ?? 0;

                        int reqStartX = Math.Max(0, startX);
                        int reqStartY = Math.Max(0, startY);
                        int reqEndX = Math.Min(currentMap.Width - 1, endX);
                        int reqEndY = Math.Min(currentMap.Height - 1, endY);

                        // Only reload statics from disk when the view moves outside
                        // the previously cached region.
                        if (cachedStaticsData == null
                            || reqStartX < cachedStaticsStartX
                            || reqStartY < cachedStaticsStartY
                            || reqEndX > cachedStaticsEndX
                            || reqEndY > cachedStaticsEndY)
                        {
                            // Load a wider area than needed so small pans don't re-trigger I/O
                            int margin = STATICS_CACHE_MARGIN;
                            int loadStartX = Math.Max(0, reqStartX - margin);
                            int loadStartY = Math.Max(0, reqStartY - margin);
                            int loadEndX = Math.Min(currentMap.Width - 1, reqEndX + margin);
                            int loadEndY = Math.Min(currentMap.Height - 1, reqEndY + margin);

                            cachedStaticsData = StaticsReader.LoadArea(loadFolder, mapIndex,
                                loadStartX, loadStartY, loadEndX, loadEndY);
                            cachedStaticsStartX = loadStartX;
                            cachedStaticsStartY = loadStartY;
                            cachedStaticsEndX = loadEndX;
                            cachedStaticsEndY = loadEndY;

                            // Re-apply in-memory static overrides (biome edits) on
                            // top of the freshly loaded disk data so cleared /
                            // replaced positions are honoured.
                            if (staticOverrides.Count > 0)
                            {
                                foreach (var kvp in staticOverrides)
                                {
                                    if (kvp.Value == null || kvp.Value.Count == 0)
                                        cachedStaticsData.StaticsByPosition.Remove(kvp.Key);
                                    else
                                        cachedStaticsData.StaticsByPosition[kvp.Key] = kvp.Value;
                                }
                            }
                        }

                        currentStatics = cachedStaticsData;
                    }

                    var landTiles = new List<(int x, int y, sbyte z, sbyte zTop, sbyte zRight, sbyte zBottom, sbyte zLeft, ushort id, Image img)>();
                    var staticItems = new List<(int x, int y, int z, ushort id, Image img, ushort hue, bool isTranslucent, bool isPartialHue)>();

                    for (int ty = 0; ty < tilesDown; ty++)
                    {
                        int mapY = startY + ty * step;
                        if (mapY < 0 || mapY >= currentMap.Height) continue;

                        for (int tx = 0; tx < tilesAcross; tx++)
                        {
                            int mapX = startX + tx * step;
                            if (mapX < 0 || mapX >= currentMap.Width) continue;

                            var tile = currentMap.Tiles[mapX, mapY];
                            if (tile == null) continue;

                            // Skip void / "nodraw" tiles
                            if (IsNoDraw(tile.TileId)) continue;

                            GetTileCornerZValues(mapX, mapY, out sbyte zTop, out sbyte zRight, out sbyte zBottom, out sbyte zLeft);

                            Image img = null;
                            if (heightmapMode == HeightmapMode.Off || heightmapMode == HeightmapMode.Contour)
                            {
                                // If this tile has AI-modified art, use that directly
                                // instead of the texmap so the replacement is visible.
                                if (modifiedArtCache.ContainsKey(tile.TileId))
                                {
                                    img = modifiedArtCache[tile.TileId];
                                }
                                else
                                {
                                    // Prefer the texmap texture for consistent coloring
                                    // across flat and deformed tiles.  Fall back to the
                                    // 44×44 land-tile art when no texmap is available.
                                    img = LoadTexMap(tile.TileId);
                                    if (img == null)
                                        img = LoadTileImage(tile.TileId);
                                }
                            }

                            landTiles.Add((mapX, mapY, tile.Z, zTop, zRight, zBottom, zLeft, tile.TileId, img));
                        }
                    }

                    if (showStatics && currentStatics != null && heightmapMode == HeightmapMode.Off)
                    {
                        for (int ty = 0; ty < tilesDown; ty++)
                        {
                            int mapY = startY + ty * step;
                            if (mapY < 0 || mapY >= currentMap.Height) continue;

                            for (int tx = 0; tx < tilesAcross; tx++)
                            {
                                int mapX = startX + tx * step;
                                if (mapX < 0 || mapX >= currentMap.Width) continue;

                                var statics = currentStatics.GetStaticsAt(mapX, mapY);
                                foreach (var staticTile in statics)
                                {
                                    if (!IsStaticInZRange(staticTile.Z)) continue;
                                    if (IsStaticNoDraw(staticTile.ItemId)) continue;
                                    var img = LoadStaticImage(staticTile.ItemId);
                                    if (img != null)
                                    {
                                        bool translucent = false;
                                        bool partialHue = false;
                                        if (tileDataReader != null && tileDataReader.IsLoaded)
                                        {
                                            var td = tileDataReader.GetItemTile(staticTile.ItemId);
                                            if (td != null)
                                            {
                                                translucent = td.Flags.HasFlag(TileFlag.Translucent);
                                                partialHue = td.Flags.HasFlag(TileFlag.PartialHue);
                                            }
                                        }
                                        staticItems.Add((mapX, mapY, staticTile.Z, staticTile.ItemId, img, staticTile.Hue, translucent, partialHue));
                                    }
                                }
                            }
                        }
                    }

                    // Merge land and statics into a single interleaved draw list.
                    // This ensures correct depth: a closer land tile is always drawn
                    // after a further-away wall, and a floor is drawn before a wall
                    // at the same position.
                    var drawItems = new List<ExportDrawItem>(landTiles.Count + staticItems.Count);
                    int orderCounter = 0;

                    for (int i = 0; i < landTiles.Count; i++)
                    {
                        var t = landTiles[i];
                        drawItems.Add(new ExportDrawItem { SortDepth = t.x + t.y, Z = t.z, IsStatic = false, Index = i, OriginalOrder = orderCounter++ });
                    }

                    for (int i = 0; i < staticItems.Count; i++)
                    {
                        var t = staticItems[i];
                        int topZ = t.z;
                        bool foliage = false;
                        if (tileDataReader != null && tileDataReader.IsLoaded)
                        {
                            var itemData = tileDataReader.GetItemTile(t.id);
                            if (itemData != null)
                            {
                                topZ = t.z + itemData.Height;
                                foliage = itemData.Flags.HasFlag(TileFlag.Foliage);
                            }
                        }
                        drawItems.Add(new ExportDrawItem { SortDepth = t.x + t.y, Z = topZ, IsStatic = true, IsFoliage = foliage, Index = i, OriginalOrder = orderCounter++ });
                    }

                    drawItems.Sort((a, b) =>
                    {
                        int cmp = a.SortDepth.CompareTo(b.SortDepth);
                        if (cmp != 0) return cmp;
                        cmp = a.IsStatic.CompareTo(b.IsStatic);
                        if (cmp != 0) return cmp;
                        cmp = a.IsFoliage.CompareTo(b.IsFoliage);
                        if (cmp != 0) return cmp;
                        cmp = a.Z.CompareTo(b.Z);
                        if (cmp != 0) return cmp;
                        return a.OriginalOrder.CompareTo(b.OriginalOrder);
                    });

                    // Generous off-screen margin so tall / wide items aren't clipped
                    const float CULL_MARGIN = 200f;

                    foreach (var di in drawItems)
                    {
                        if (di.IsStatic) continue;

                        var item = landTiles[di.Index];
                        int relX = item.x - cameraX;
                        int relY = item.y - cameraY;
                        float isoX = (relX - relY) * halfTileW;
                        float isoY = (relX + relY) * halfTileH;
                        float screenX = centerX + isoX;
                        float screenY = centerY + isoY;

                        // Screen-space cull
                        if (screenX < -CULL_MARGIN || screenX > viewW + CULL_MARGIN ||
                            screenY < -CULL_MARGIN || screenY > viewH + CULL_MARGIN)
                            continue;

                        if (heightmapMode == HeightmapMode.Grayscale || heightmapMode == HeightmapMode.Heatmap)
                        {
                            // Draw solid heightmap-coloured diamond
                            Color hc = heightmapMode == HeightmapMode.Grayscale
                                ? HeightToGrayscale(item.z)
                                : HeightToHeatmap(item.z);
                            DrawHeightmapTile(g, screenX, screenY, halfTileW, halfTileH, zoom,
                                item.zTop, item.zRight, item.zBottom, item.zLeft, hc);
                        }
                        else
                        {
                            // Normal or Contour: draw the textured/solid tile first
                            DrawDeformedLandTile(g, item.img, screenX, screenY, halfTileW, halfTileH, zoom,
                                item.zTop, item.zRight, item.zBottom, item.zLeft, item.id);

                            // Contour overlay: draw edges where Z crosses an interval
                            if (heightmapMode == HeightmapMode.Contour)
                            {
                                DrawContourEdges(g, screenX, screenY, halfTileW, halfTileH, zoom,
                                    item.zTop, item.zRight, item.zBottom, item.zLeft);
                            }
                        }
                    }

                    // Land overlays sit above terrain but below statics/players.
                    DrawLandOverlayPlanes(g, viewW, viewH, zoom, halfTileW, halfTileH, centerX, centerY);

                    foreach (var di in drawItems)
                    {
                        if (!di.IsStatic) continue;

                        var item = staticItems[di.Index];
                        int relX = item.x - cameraX;
                        int relY = item.y - cameraY;
                        float isoX = (relX - relY) * halfTileW;
                        float isoY = (relX + relY) * halfTileH;
                        float screenX = centerX + isoX;
                        float screenY = centerY + isoY;
                        float imgW = item.img.Width * zoom;
                        float imgH = item.img.Height * zoom;
                        float zOffset = item.z * Z_SCALE * zoom;
                        float drawX = screenX - imgW / 2f;
                        float drawY = screenY - imgH + halfTileH - zOffset;

                        // Screen-space cull
                        if (drawX + imgW < -CULL_MARGIN || drawX > viewW + CULL_MARGIN ||
                            drawY + imgH < -CULL_MARGIN || drawY > viewH + CULL_MARGIN)
                            continue;

                        try { DrawStaticImage(g, item.img, new RectangleF(drawX, drawY, imgW, imgH), item.hue, item.isTranslucent, item.isPartialHue, item.id); } catch { }
                    }
                }

                // Draw coordinate overlay
                using (var font = new Font("Arial", 10, FontStyle.Bold))
                using (var brush = new SolidBrush(HolographicTheme.TextPrimary))
                using (var bgBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                {
                    string coordText = $"Position: ({cameraX}, {cameraY}) | Zoom: {zoomFactor:F1}px";
                    if (useMinimapMode)
                    {
                        if (minimapGenerator.IsGenerated)
                            coordText += " | Minimap";
                        else if (minimapGenerating)
                            coordText += " | Generating minimap...";
                        else
                            coordText += " | Overview";
                    }
                    if (showStatics && currentStatics != null && !useMinimapMode) coordText += " | Statics: ON";
                    if (heightmapMode != HeightmapMode.Off)
                        coordText += $" | Height: {heightmapMode} | Z={GetZAt(cameraX, cameraY)}";
                    if (activeHeightTool != HeightTool.None)
                        coordText += $" | Tool: {activeHeightTool}";
                    if (selectedTiles.Count > 0) coordText += $" | Selected: {selectedTiles.Count} tile(s)";
                    var textSize = g.MeasureString(coordText, font);
                    g.FillRectangle(bgBrush, 10, 10, textSize.Width + 10, textSize.Height + 6);
                    g.DrawString(coordText, font, brush, 15, 13);
                }
            }

            // Detach the old image from the PictureBox (if it was a different bitmap,
            // e.g. from a resize, it will have been disposed when renderBuffer was
            // replaced above).  Then assign the current buffer.
            if (mapPictureBox.Image != null && !ReferenceEquals(mapPictureBox.Image, bmp))
            {
                var old = mapPictureBox.Image;
                mapPictureBox.Image = null;
                old.Dispose();
            }
            mapPictureBox.Image = bmp;

            // Update coordinate text boxes (with null checks for initialization)
            if (coordXTextBox != null)
                coordXTextBox.Text = cameraX.ToString();
            if (coordYTextBox != null)
                coordYTextBox.Text = cameraY.ToString();

            mapPictureBox.Invalidate();
            RefreshMinimapViewport();
        }

        private void MapPictureBox_Paint(object sender, System.Windows.Forms.PaintEventArgs e)
        {
            if (currentMap == null || mapPictureBox.Image == null) return;

            int viewW = mapPictureBox.Image.Width;
            int viewH = mapPictureBox.Image.Height;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float zoom = zoomFactor / 44f;
            float halfTileW = 22f * zoom;
            float halfTileH = 22f * zoom;
            float centerX = viewW / 2f;
            float centerY = viewH / 2f;

            // Heightmap legend
            if (heightmapMode == HeightmapMode.Grayscale || heightmapMode == HeightmapMode.Heatmap)
            {
                DrawHeightmapLegend(g, viewW, viewH);
            }

            if (isLassoSelecting && lassoPoints.Count > 1) DrawLassoPath(g);

            foreach (var t in contextTiles) DrawTileSelection(g, t, centerX, centerY, halfTileW, halfTileH, Color.FromArgb(255, 165, 0));
            foreach (var t in replaceTiles) DrawTileSelection(g, t, centerX, centerY, halfTileW, halfTileH, HolographicTheme.SelectionCyan);
            foreach (var s in selectedStatics) DrawStaticSelection(g, s, centerX, centerY, halfTileW, halfTileH, zoom, Color.FromArgb(255, 0, 255));

            if (selectionTargetType == SelectionTargetType.Statics && selectedStatics.Count > 0) DrawStaticInfoPanel(g, viewW, viewH);
            else DrawTileInfoPanel(g, viewW, viewH);
        }

        private void DrawLassoPath(Graphics g)
        {
            if (lassoPoints.Count < 2) return;
            if (lassoPoints.Count >= 3) using (var b = new SolidBrush(Color.FromArgb(40, 0, 255, 255))) g.FillPolygon(b, lassoPoints.ToArray());
            using (var p = new Pen(Color.FromArgb(100, 0, 200, 255), 4)) { p.LineJoin = LineJoin.Round; g.DrawLines(p, lassoPoints.ToArray()); g.DrawLine(p, lassoPoints[lassoPoints.Count - 1], lassoPoints[0]); }
            using (var p = new Pen(Color.FromArgb(255, 0, 255, 255), 2)) { p.LineJoin = LineJoin.Round; p.DashStyle = DashStyle.Dash; g.DrawLines(p, lassoPoints.ToArray()); g.DrawLine(p, lassoPoints[lassoPoints.Count - 1], lassoPoints[0]); }
            using (var b = new SolidBrush(Color.White)) foreach (var pt in lassoPoints) g.FillEllipse(b, pt.X - 3, pt.Y - 3, 6, 6);
            using (var f = new Font("Consolas", 10, FontStyle.Bold)) using (var tb = new SolidBrush(Color.White)) using (var bg = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
            { string t = "Release mouse to select | ESC to cancel"; var s = g.MeasureString(t, f); g.FillRectangle(bg, 8, 8, s.Width + 4, s.Height + 4); g.DrawString(t, f, tb, 10, 10); }
        }

        private void DrawStaticSelection(Graphics g, SelectedStatic s, float centerX, float centerY, float halfTileW, float halfTileH, float zoom, Color c)
        {
            int relX = s.X - cameraX, relY = s.Y - cameraY;
            float screenX = centerX + (relX - relY) * halfTileW, screenY = centerY + (relX + relY) * halfTileH;
            var img = LoadStaticImage(s.ItemId); if (img == null) return;
            float imgW = img.Width * zoom, imgH = img.Height * zoom, zOffset = s.Z * Z_SCALE * zoom;
            float drawX = screenX - imgW / 2f, drawY = screenY - imgH + halfTileH - zOffset;
            var r = new RectangleF(drawX - 2, drawY - 2, imgW + 4, imgH + 4);
            DrawTintedStaticImage(g, img, drawX, drawY, imgW, imgH, c);
            using (var b = new SolidBrush(Color.FromArgb(60, c))) g.FillRectangle(b, r.X - 4, r.Y - 4, r.Width + 8, r.Height + 8);
            using (var p = new Pen(Color.FromArgb(180, c), 4)) g.DrawRectangle(p, r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4);
            using (var p = new Pen(c, 2)) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
        }

        private void DrawTintedStaticImage(Graphics g, Image img, float x, float y, float w, float h, Color c)
        {
            float tR = c.R / 255f, tG = c.G / 255f, tB = c.B / 255f, blend = 0.35f, keep = 0.65f;
            var cm = new ColorMatrix(new float[][] {
                new float[] { keep + blend * tR * 0.5f, blend * tR * 0.2f, blend * tR * 0.2f, 0, 0 },
                new float[] { blend * tG * 0.2f, keep + blend * tG * 0.5f, blend * tG * 0.2f, 0, 0 },
                new float[] { blend * tB * 0.2f, blend * tB * 0.2f, keep + blend * tB * 0.5f, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 }, new float[] { blend * tR * 0.15f, blend * tG * 0.15f, blend * tB * 0.15f, 0, 1 }
            });
            using (var a = new ImageAttributes()) { a.SetColorMatrix(cm); g.DrawImage(img, new Rectangle((int)x, (int)y, (int)w, (int)h), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, a); }
        }

        private void DrawStaticInfoPanel(Graphics g, int viewW, int viewH)
        {
            if (selectedStatics.Count == 0) return;

            using (var f = new Font("Consolas", 9))
            using (var hf = new Font("Consolas", 10, FontStyle.Bold))
            using (var sf = new Font("Consolas", 8))  // Smaller font for flags
            using (var tb = new SolidBrush(HolographicTheme.TextPrimary))
            using (var hb = new SolidBrush(Color.Magenta))
            using (var lb = new SolidBrush(Color.FromArgb(180, 180, 180)))  // Light gray for labels
            using (var vb = new SolidBrush(Color.FromArgb(100, 255, 218)))  // Cyan for values
            using (var fb = new SolidBrush(Color.FromArgb(255, 200, 100)))  // Orange for flags
            using (var bg = new SolidBrush(Color.FromArgb(220, 20, 25, 30)))
            using (var bp = new Pen(Color.Magenta, 1))
            {
                var lines = new List<(string text, Font font, Brush brush)>();

                // Get preview image
                Image previewImage = null;
                int previewSize = 80;

                if (selectedStatics.Count == 1)
                {
                    var s = selectedStatics[0];
                    var itemData = tileDataReader.GetItemTile(s.ItemId);
                    previewImage = LoadStaticImage(s.ItemId);

                    // Header
                    string name = itemData?.Name ?? "Unknown";
                    if (!string.IsNullOrWhiteSpace(name))
                        lines.Add(($"Name: {name}", hf, hb));
                    else
                        lines.Add(("Selected Static", hf, hb));

                    lines.Add(("?????????????????????", f, lb));

                    // Basic info
                    lines.Add(($"Graphic:    0x{s.ItemId:X4} ({s.ItemId})", f, vb));
                    lines.Add(($"Position:   ({s.X}, {s.Y})", f, tb));
                    lines.Add(($"Altitude:   {s.Z}", f, tb));
                    lines.Add(($"Hue:        {s.Hue}", f, tb));

                    if (itemData != null)
                    {
                        lines.Add(("?????????????????????", f, lb));
                        lines.Add(($"Height:     {itemData.Height}", f, tb));
                        lines.Add(($"Weight:     {itemData.Weight}", f, tb));
                        lines.Add(($"Animation:  0x{itemData.Animation:X4}", f, tb));
                        lines.Add(($"Quality:    {itemData.Quality}", f, tb));
                        lines.Add(($"Quantity:   {itemData.Quantity}", f, tb));
                        lines.Add(($"StackOff:   {itemData.StackingOffset}", f, tb));

                        // Get pixel dimensions from the image
                        if (previewImage != null)
                        {
                            lines.Add(($"PixelSize:  {previewImage.Width} x {previewImage.Height}", f, tb));
                        }

                        // Flags
                        string flagsStr = itemData.GetFlagsString();
                        if (flagsStr != "None")
                        {
                            lines.Add(("?????????????????????", f, lb));
                            lines.Add(("Flags:", f, lb));

                            // Split flags into multiple lines if too long
                            var flagsList = flagsStr.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                            string currentLine = "";
                            foreach (var flag in flagsList)
                            {
                                if (currentLine.Length + flag.Length > 25)
                                {
                                    if (!string.IsNullOrEmpty(currentLine))
                                        lines.Add(($"  {currentLine}", sf, fb));
                                    currentLine = flag;
                                }
                                else
                                {
                                    currentLine = string.IsNullOrEmpty(currentLine) ? flag : currentLine + ", " + flag;
                                }
                            }
                            if (!string.IsNullOrEmpty(currentLine))
                                lines.Add(($"  {currentLine}", sf, fb));
                        }
                    }
                }
                else
                {
                    // Multiple selection summary
                    lines.Add(($"Selected: {selectedStatics.Count} items", hf, hb));
                    lines.Add(("?????????????????????", f, lb));

                    var uniqueIds = selectedStatics.Select(s => s.ItemId).Distinct().ToList();
                    var minZ = selectedStatics.Min(s => s.Z);
                    var maxZ = selectedStatics.Max(s => s.Z);

                    lines.Add(($"Unique IDs: {uniqueIds.Count}", f, tb));
                    lines.Add(($"Z Range:    {minZ} to {maxZ}", f, tb));

                    // Show first few item names
                    int shown = 0;
                    foreach (var id in uniqueIds.Take(3))
                    {
                        var itemData = tileDataReader.GetItemTile(id);
                        string name = itemData?.Name ?? "Unknown";
                        lines.Add(($"  0x{id:X4}: {name}", sf, vb));
                        shown++;
                    }
                    if (uniqueIds.Count > 3)
                        lines.Add(($"  ... +{uniqueIds.Count - 3} more", sf, lb));

                    previewImage = LoadStaticImage(selectedStatics[0].ItemId);
                }

                // Calculate panel size
                float maxWidth = 0;
                float totalHeight = 0;
                float lineHeight = f.GetHeight(g);
                float smallLineHeight = sf.GetHeight(g);
                float headerHeight = hf.GetHeight(g);

                foreach (var line in lines)
                {
                    var size = g.MeasureString(line.text, line.font);
                    maxWidth = Math.Max(maxWidth, size.Width);
                    totalHeight += (line.font == hf) ? headerHeight : (line.font == sf) ? smallLineHeight : lineHeight;
                }
                totalHeight += 10;

                // Add space for preview
                float previewAreaWidth = 0, previewAreaHeight = 0;
                if (previewImage != null)
                {
                    float scale = Math.Min((float)previewSize / previewImage.Width, (float)previewSize / previewImage.Height);
                    scale = Math.Min(scale, 2f);
                    previewAreaWidth = previewImage.Width * scale;
                    previewAreaHeight = previewImage.Height * scale;
                    maxWidth += previewAreaWidth + 25;
                    totalHeight = Math.Max(totalHeight, previewAreaHeight + 30);
                }

                maxWidth = Math.Max(maxWidth, 220);

                float panelX = 10;
                float panelY = viewH - totalHeight - 50;
                float panelW = maxWidth + 20;
                float panelH = totalHeight + 10;

                // Draw panel background
                g.FillRectangle(bg, panelX, panelY, panelW, panelH);
                g.DrawRectangle(bp, panelX, panelY, panelW, panelH);

                // Draw text
                float textAreaWidth = panelW - (previewImage != null ? previewAreaWidth + 25 : 0);
                float y = panelY + 8;
                foreach (var line in lines)
                {
                    float lh = (line.font == hf) ? headerHeight : (line.font == sf) ? smallLineHeight : lineHeight;
                    g.DrawString(line.text, line.font, line.brush, panelX + 10, y);
                    y += lh;
                }

                // Draw preview image
                if (previewImage != null)
                {
                    float previewX = panelX + panelW - previewAreaWidth - 15;
                    float previewY = panelY + (panelH - previewAreaHeight) / 2;

                    using (var previewBgBrush = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
                        g.FillRectangle(previewBgBrush, previewX - 5, previewY - 5, previewAreaWidth + 10, previewAreaHeight + 10);

                    using (var previewBorderPen = new Pen(Color.Magenta, 1))
                        g.DrawRectangle(previewBorderPen, previewX - 5, previewY - 5, previewAreaWidth + 10, previewAreaHeight + 10);

                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(previewImage, previewX, previewY, previewAreaWidth, previewAreaHeight);
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    g.PixelOffsetMode = PixelOffsetMode.Default;
                }
            }
        }

        private void DrawTileInfoPanel(Graphics g, int viewW, int viewH)
        {
            var all = replaceTiles.Concat(contextTiles).ToList(); if (all.Count == 0) return;
            using (var f = new Font("Consolas", 9)) using (var hf = new Font("Consolas", 10, FontStyle.Bold))
            using (var tb = new SolidBrush(HolographicTheme.TextPrimary)) using (var hb = new SolidBrush(HolographicTheme.SelectionCyan))
            using (var bg = new SolidBrush(Color.FromArgb(200, 20, 25, 30))) using (var bp = new Pen(HolographicTheme.SelectionCyan, 1))
            {
                var lines = new List<string> { $"Selected Tiles: {all.Count}", "---------------------" };
                if (all.Count == 1) { var t = all[0]; lines.Add($"Pos: ({t.X},{t.Y})"); lines.Add($"ID: 0x{t.TileId:X4}"); lines.Add($"Z: {t.Z}"); }
                else lines.Add($"{all.Count} tiles selected");
                float maxW = 0, lh = f.GetHeight(g), hh = hf.GetHeight(g);
                foreach (var l in lines) { var sz = g.MeasureString(l, l == lines[0] ? hf : f); maxW = Math.Max(maxW, sz.Width); }
                float th = hh + (lines.Count - 1) * lh + 10, px = 10, py = viewH - th - 50, pw = maxW + 20, ph = th + 10;
                g.FillRectangle(bg, px, py, pw, ph); g.DrawRectangle(bp, px, py, pw, ph);
                float y = py + 8; for (int i = 0; i < lines.Count; i++) { g.DrawString(lines[i], i == 0 ? hf : f, i == 0 ? hb : tb, px + 10, y); y += i == 0 ? hh : lh; }
            }
        }

        private void DrawTileSelection(Graphics g, SelectedTile t, float centerX, float centerY, float halfTileW, float halfTileH, Color c)
        {
            int relX = t.X - cameraX, relY = t.Y - cameraY;
            float screenX = centerX + (relX - relY) * halfTileW, screenY = centerY + (relX + relY) * halfTileH;
            float zoom = zoomFactor / 44f, tileW = 44 * zoom, tileH = 44 * zoom;
            var d = new Point[] { new Point((int)screenX, (int)(screenY - tileH / 2f)), new Point((int)(screenX + tileW / 2f), (int)screenY),
                new Point((int)screenX, (int)(screenY + tileH / 2f)), new Point((int)(screenX - tileW / 2f), (int)screenY) };
            using (var b = new SolidBrush(Color.FromArgb(60, c))) g.FillPolygon(b, d);
            using (var p = new Pen(c, 3)) g.DrawPolygon(p, d);
        }

        private Color GetLandTileColor(ushort id)
        {
            if (id >= 0x00A8 && id <= 0x00AB) return Color.FromArgb(65, 105, 225);
            if (id >= 0x0136 && id <= 0x01AF) return Color.FromArgb(65, 105, 225);
            if (id >= 0x009C && id <= 0x00A7) return Color.FromArgb(85, 107, 47);
            if (id <= 0x0015) return Color.FromArgb(34, 139, 34);
            if (id >= 0x0016 && id <= 0x002B) return Color.FromArgb(139, 90, 43);
            if (id >= 0x002C && id <= 0x004F) return Color.FromArgb(238, 214, 175);
            if (id >= 0x0050 && id <= 0x009B) return Color.FromArgb(105, 105, 105);
            if (id >= 0x00AC && id <= 0x00D7) return Color.FromArgb(64, 64, 64);
            if (id >= 0x010C && id <= 0x011F) return Color.FromArgb(255, 250, 250);
            if (id >= 0x01F4 && id <= 0x0212) return Color.FromArgb(255, 69, 0);
            if (id >= 0x0230 && id <= 0x023F) return Color.FromArgb(50, 205, 50);
            if (id >= 0x0578 && id <= 0x05FB) return Color.FromArgb(34, 139, 34);
            return Color.FromArgb(90, 80, 70);
        }
    }
}
