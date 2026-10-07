using MeesaMultisMaker.Mul;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Generates and caches a high-quality downsampled minimap by sampling actual tile colors.
    /// This provides much better visual quality than simple tile-type coloring when zoomed out.
    /// </summary>
    public class MinimapGenerator
    {
        private Bitmap _minimapCache;
        private int _cachedMapIndex = -1;
        private int _minimapScale;
        private int _minimapWidth;
        private int _minimapHeight;
        private volatile bool _isGenerating = false;
        private volatile bool _cancelRequested = false;

        /// <summary>
        /// The cached minimap bitmap (can be null if not generated yet)
        /// </summary>
        public Bitmap Minimap => _minimapCache;

        /// <summary>
        /// Scale factor: how many map tiles each minimap pixel represents
        /// </summary>
        public int Scale => _minimapScale;

        /// <summary>
        /// Whether the minimap has been generated
        /// </summary>
        public bool IsGenerated => _minimapCache != null && !_isGenerating;

        /// <summary>
        /// Whether the minimap is currently being generated
        /// </summary>
        public bool IsGenerating => _isGenerating;

        /// <summary>
        /// Generate a downsampled minimap from the map data by sampling actual tile colors.
        /// This synchronous overload calls the tile loader directly and should only be
        /// used from the UI thread.
        /// </summary>
        public void Generate(MapData map, int mapIndex, Func<ushort, Image> tileLoader, int targetMaxSize = 2048, Action<float> progress = null)
        {
            if (map == null) return;
            if (_isGenerating) return;

            // Check if we already have a valid cache
            if (_minimapCache != null && _cachedMapIndex == mapIndex)
                return;

            // Pre-sample all tile colors on the calling thread
            var tileColorCache = new System.Collections.Generic.Dictionary<ushort, Color>();
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    var tile = map.Tiles[x, y];
                    if (tile == null) continue;
                    if (tileColorCache.ContainsKey(tile.TileId)) continue;
                    tileColorCache[tile.TileId] = GetTileAverageColor(tile.TileId, tileLoader);
                }

            GenerateFromColorCache(map, mapIndex, tileColorCache, targetMaxSize, progress);
        }

        /// <summary>
        /// Generate the minimap using a pre-built tile color lookup.
        /// This method is thread-safe — it never accesses the art cache or GDI+ bitmaps.
        /// </summary>
        private void GenerateFromColorCache(
            MapData map, int mapIndex,
            System.Collections.Generic.Dictionary<ushort, Color> tileColorCache,
            int targetMaxSize, Action<float> progress)
        {
            if (map == null) return;
            if (_isGenerating) return;

            // Check if we already have a valid cache
            if (_minimapCache != null && _cachedMapIndex == mapIndex)
                return;

            _isGenerating = true;
            _cancelRequested = false;

            try
            {
                // Calculate scale to fit map within targetMaxSize
                int maxMapDim = Math.Max(map.Width, map.Height);
                _minimapScale = Math.Max(1, (maxMapDim + targetMaxSize - 1) / targetMaxSize);

                _minimapWidth = (map.Width + _minimapScale - 1) / _minimapScale;
                _minimapHeight = (map.Height + _minimapScale - 1) / _minimapScale;

                // Dispose old cache
                var oldCache = _minimapCache;
                _minimapCache = null;
                oldCache?.Dispose();

                var newBitmap = new Bitmap(_minimapWidth, _minimapHeight, PixelFormat.Format32bppArgb);

                // Lock bits for fast pixel access
                var bmpData = newBitmap.LockBits(
                    new Rectangle(0, 0, _minimapWidth, _minimapHeight),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);

                bool cancelled = false;
                try
                {
                    int stride = bmpData.Stride;
                    byte[] pixels = new byte[stride * _minimapHeight];

                    int totalPixels = _minimapWidth * _minimapHeight;
                    int processedPixels = 0;
                    int lastReportedPercent = -1;

                    // Process each minimap pixel
                    for (int my = 0; my < _minimapHeight; my++)
                    {
                        if (_cancelRequested) { cancelled = true; break; }

                        for (int mx = 0; mx < _minimapWidth; mx++)
                        {
                            // Calculate the map region this minimap pixel represents
                            int mapStartX = mx * _minimapScale;
                            int mapStartY = my * _minimapScale;
                            int mapEndX = Math.Min(mapStartX + _minimapScale, map.Width);
                            int mapEndY = Math.Min(mapStartY + _minimapScale, map.Height);

                            // Average the colors of all tiles in this region
                            long totalR = 0, totalG = 0, totalB = 0;
                            int sampleCount = 0;

                            for (int ty = mapStartY; ty < mapEndY; ty++)
                            {
                                for (int tx = mapStartX; tx < mapEndX; tx++)
                                {
                                    var tile = map.Tiles[tx, ty];
                                    if (tile == null) continue;

                                    Color tileColor;
                                    if (!tileColorCache.TryGetValue(tile.TileId, out tileColor))
                                        tileColor = GetFallbackTileColor(tile.TileId);

                                    totalR += tileColor.R;
                                    totalG += tileColor.G;
                                    totalB += tileColor.B;
                                    sampleCount++;
                                }
                            }

                            // Calculate average color for this minimap pixel
                            Color avgColor;
                            if (sampleCount > 0)
                            {
                                avgColor = Color.FromArgb(
                                    255,
                                    (int)(totalR / sampleCount),
                                    (int)(totalG / sampleCount),
                                    (int)(totalB / sampleCount));
                            }
                            else
                            {
                                avgColor = Color.Black;
                            }

                            // Write to pixel array (BGRA format)
                            int pixelIndex = my * stride + mx * 4;
                            pixels[pixelIndex + 0] = avgColor.B;
                            pixels[pixelIndex + 1] = avgColor.G;
                            pixels[pixelIndex + 2] = avgColor.R;
                            pixels[pixelIndex + 3] = 255;

                            processedPixels++;

                            // Report progress every 1%
                            int percent = (processedPixels * 100) / totalPixels;
                            if (percent > lastReportedPercent)
                            {
                                lastReportedPercent = percent;
                                try { progress?.Invoke(percent / 100f); } catch { }
                            }
                        }
                    }

                    if (!cancelled)
                    {
                        // Copy pixels to bitmap
                        Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
                    }
                }
                finally
                {
                    newBitmap.UnlockBits(bmpData);
                }

                if (cancelled)
                {
                    newBitmap.Dispose();
                }
                else
                {
                    _minimapCache = newBitmap;
                    _cachedMapIndex = mapIndex;
                    try { progress?.Invoke(1.0f); } catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MinimapGenerator error: {ex.Message}");
            }
            finally
            {
                _isGenerating = false;
            }
        }

        /// <summary>
        /// Generate the minimap asynchronously.
        /// Phase 1 runs on the calling (UI) thread to pre-sample tile colors safely.
        /// Phase 2 runs on a background thread to fill the minimap bitmap.
        /// </summary>
        public Task GenerateAsync(MapData map, int mapIndex, Func<ushort, Image> tileLoader, int targetMaxSize = 2048, Action<float> progress = null)
        {
            if (map == null) return Task.FromResult(0);
            if (_isGenerating) return Task.FromResult(0);
            if (_minimapCache != null && _cachedMapIndex == mapIndex)
                return Task.FromResult(0);

            // Phase 1 (UI thread): scan all unique tile IDs and pre-sample their
            // average colors.  This avoids accessing the non-thread-safe artCache
            // dictionary and shared GDI+ bitmaps from the background thread.
            var tileColorCache = new System.Collections.Generic.Dictionary<ushort, Color>();
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    var tile = map.Tiles[x, y];
                    if (tile == null) continue;
                    if (tileColorCache.ContainsKey(tile.TileId)) continue;
                    tileColorCache[tile.TileId] = GetTileAverageColor(tile.TileId, tileLoader);
                }
            }

            // Phase 2 (background thread): fill the minimap bitmap using only
            // the pre-computed color cache — no GDI+ or art-cache access needed.
            return Task.Run(() => GenerateFromColorCache(map, mapIndex, tileColorCache, targetMaxSize, progress));
        }

        /// <summary>
        /// Get the average color of a tile by sampling its image.
        /// Must be called from the UI thread only (accesses shared art cache and GDI+).
        /// </summary>
        private Color GetTileAverageColor(ushort tileId, Func<ushort, Image> tileLoader)
        {
            try
            {
                var img = tileLoader(tileId);
                if (img == null)
                    return GetFallbackTileColor(tileId);

                var bmp = img as Bitmap;
                if (bmp == null)
                    return GetFallbackTileColor(tileId);

                int w = bmp.Width;
                int h = bmp.Height;
                if (w <= 0 || h <= 0)
                    return GetFallbackTileColor(tileId);

                BitmapData data;
                try
                {
                    data = bmp.LockBits(
                        new Rectangle(0, 0, w, h),
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format32bppArgb);
                }
                catch
                {
                    return GetFallbackTileColor(tileId);
                }

                int stride = data.Stride;
                byte[] pixels = new byte[stride * h];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                bmp.UnlockBits(data);

                long totalR = 0, totalG = 0, totalB = 0;
                int sampleCount = 0;
                int step = Math.Max(1, Math.Min(w, h) / 6);

                for (int y = step; y < h - step; y += step)
                {
                    int rowOff = y * stride;
                    for (int x = step; x < w - step; x += step)
                    {
                        int px = rowOff + x * 4;
                        byte a = pixels[px + 3];
                        if (a < 128) continue;

                        totalB += pixels[px + 0];
                        totalG += pixels[px + 1];
                        totalR += pixels[px + 2];
                        sampleCount++;
                    }
                }

                if (sampleCount > 0)
                {
                    return Color.FromArgb(
                        255,
                        (int)(totalR / sampleCount),
                        (int)(totalG / sampleCount),
                        (int)(totalB / sampleCount));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetTileAverageColor error for tile {tileId}: {ex.Message}");
            }

            return GetFallbackTileColor(tileId);
        }

        /// <summary>
        /// Fallback color based on tile type when image not available
        /// </summary>
        private Color GetFallbackTileColor(ushort tileId)
        {
            if (tileId >= 0x00A8 && tileId <= 0x00AB) return Color.FromArgb(65, 105, 225);
            if (tileId >= 0x0136 && tileId <= 0x01AF) return Color.FromArgb(65, 105, 225);
            if (tileId >= 0x009C && tileId <= 0x00A7) return Color.FromArgb(85, 107, 47);
            if (tileId <= 0x0015) return Color.FromArgb(34, 139, 34);
            if (tileId >= 0x0016 && tileId <= 0x002B) return Color.FromArgb(139, 90, 43);
            if (tileId >= 0x002C && tileId <= 0x004F) return Color.FromArgb(238, 214, 175);
            if (tileId >= 0x0050 && tileId <= 0x009B) return Color.FromArgb(105, 105, 105);
            if (tileId >= 0x00AC && tileId <= 0x00D7) return Color.FromArgb(64, 64, 64);
            if (tileId >= 0x010C && tileId <= 0x011F) return Color.FromArgb(255, 250, 250);
            if (tileId >= 0x01F4 && tileId <= 0x0212) return Color.FromArgb(255, 69, 0);
            if (tileId >= 0x0230 && tileId <= 0x023F) return Color.FromArgb(50, 205, 50);
            if (tileId >= 0x0578 && tileId <= 0x05FB) return Color.FromArgb(34, 139, 34);
            return Color.FromArgb(128, 128, 128);
        }

        /// <summary>
        /// Convert minimap coordinates to map coordinates
        /// </summary>
        public void MinimapToMap(int minimapX, int minimapY, out int mapX, out int mapY)
        {
            mapX = minimapX * _minimapScale;
            mapY = minimapY * _minimapScale;
        }

        /// <summary>
        /// Convert map coordinates to minimap coordinates
        /// </summary>
        public void MapToMinimap(int mapX, int mapY, out int minimapX, out int minimapY)
        {
            minimapX = mapX / _minimapScale;
            minimapY = mapY / _minimapScale;
        }

        /// <summary>
        /// Invalidate the cache (call when map changes).
        /// Cancels any in-progress background generation and waits for it to stop.
        /// </summary>
        public void Invalidate()
        {
            _cancelRequested = true;

            // Spin-wait briefly for the background thread to notice the cancel flag
            int waits = 0;
            while (_isGenerating && waits < 100)
            {
                Thread.Sleep(10);
                waits++;
            }

            var old = _minimapCache;
            _minimapCache = null;
            _cachedMapIndex = -1;
            old?.Dispose();
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            Invalidate();
        }
    }
}
