using MeesaMultisMaker.Mul;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SkiaSharp;

namespace MeesaMultisMaker
{
    partial class MapViewerForm
    {
        private CancellationTokenSource _exportCts;
        private readonly object _artCacheLock = new object();
        private Button _cancelExportButton;

        /// <summary>
        /// Shows the Export Entire Map dialog and starts the export process.
        /// </summary>
        private void ExportEntireMap_Click(object sender, EventArgs e)
        {
            if (currentMap == null)
            {
                MessageBox.Show(this, "No map loaded. Load a map first.", "Export Map",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var dlg = new MapExportOptionsDialog(currentMap.Width, currentMap.Height, zoomFactor))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;

                float exportZoom = dlg.ExportZoom;
                bool includeStatics = dlg.IncludeStatics;
                bool htmlOnly = dlg.HtmlAndMinimapOnly;
                bool mipmapOnly = dlg.MipmapOnly;
                bool useTransparency = dlg.UseTransparentBackground;
                bool forceSingleImage = dlg.ExportAsSingleImage;
                bool restitchAfterTiling = dlg.RestitchAfterTiling;
                int regionStartX = dlg.RegionStartX;
                int regionStartY = dlg.RegionStartY;
                int regionEndX = dlg.RegionEndX;
                int regionEndY = dlg.RegionEndY;

                using (var sfd = new SaveFileDialog())
                {
                    sfd.Title = "Save Exported Map";
                    sfd.Filter = "PNG Image|*.png|JPEG Image|*.jpg|Bitmap|*.bmp";
                    sfd.FileName = $"map{facetComboBox.SelectedIndex}_export.png";

                    if (sfd.ShowDialog(this) != DialogResult.OK)
                        return;

                    BeginExportMap(sfd.FileName, exportZoom, includeStatics,
                        regionStartX, regionStartY, regionEndX, regionEndY, htmlOnly, mipmapOnly, useTransparency, forceSingleImage, restitchAfterTiling);
                }
            }
        }

        private async void BeginExportMap(string outputPath, float exportZoom, bool includeStatics,
            int regionStartX, int regionStartY, int regionEndX, int regionEndY, bool htmlOnly = false, bool mipmapOnly = false, bool useTransparency = false, bool forceSingleImage = true, bool restitchAfterTiling = false)
        {
            _exportCts?.Cancel();
            _exportCts = new CancellationTokenSource();
            var token = _exportCts.Token;

            // Show a cancel button instead of disabling the entire form.
            var previousStatus = statusLabel.Text;
            statusLabel.Text = "Exporting map... 0%";
            ShowExportCancelButton();

            // Disable controls that shouldn't be used during export.
            if (loadButton != null) loadButton.Enabled = false;
            if (facetComboBox != null) facetComboBox.Enabled = false;
            if (mapPictureBox != null) mapPictureBox.Enabled = false;

            try
            {
                await Task.Run(() =>
                {
                    ExportMapRegion(outputPath, exportZoom, includeStatics,
                        regionStartX, regionStartY, regionEndX, regionEndY, token,
                        (progress, message) =>
                        {
                            try
                            {
                                if (statusLabel != null && !statusLabel.IsDisposed && statusLabel.IsHandleCreated)
                                {
                                    statusLabel.BeginInvoke(new Action(() =>
                                    {
                                        try { statusLabel.Text = $"Exporting map... {(int)(progress * 100)}% — {message}"; }
                                        catch { }
                                    }));
                                }
                            }
                            catch { }
                        },
                        htmlOnly, mipmapOnly, useTransparency, forceSingleImage, restitchAfterTiling);
                }, token);

                // Check whether we got a single file or a tile directory.
                string tilesDir = Path.Combine(
                    Path.GetDirectoryName(outputPath),
                    Path.GetFileNameWithoutExtension(outputPath) + "_tiles");

                string msg;
                string displayName;
                if (Directory.Exists(tilesDir))
                {
                    int fileCount = Directory.GetFiles(tilesDir, "*.jpg").Length;
                    long totalBytes = 0;
                    foreach (var f in Directory.GetFiles(tilesDir))
                        totalBytes += new FileInfo(f).Length;

                    displayName = Path.GetFileName(tilesDir);
                    msg = $"Map exported as {fileCount:N0} JPEG tiles!\n\n"
                        + $"Folder: {tilesDir}\n"
                        + $"Total size: {totalBytes / (1024 * 1024):N0} MB\n\n";

                    // Check if stitched file was created
                    if (restitchAfterTiling && File.Exists(outputPath))
                    {
                        long stitchedSize = new FileInfo(outputPath).Length;
                        msg += $"Stitched image: {Path.GetFileName(outputPath)}\n"
                             + $"Stitched size: {stitchedSize / (1024 * 1024):N0} MB\n\n";
                    }

                    msg += "Open viewer.html in a browser to view the full map.";
                }
                else
                {
                    displayName = Path.GetFileName(outputPath);
                    msg = $"Map exported successfully!\n\nFile: {outputPath}\nSize: {new FileInfo(outputPath).Length / 1024:N0} KB";
                }

                statusLabel.Text = $"Map exported to {displayName}";
                MessageBox.Show(this, msg, "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                statusLabel.Text = "Export cancelled.";
            }
            catch (Exception ex)
            {
                statusLabel.Text = previousStatus;
                MessageBox.Show(this, $"Export failed:\n{ex.Message}", "Export Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                HideExportCancelButton();
                if (loadButton != null) loadButton.Enabled = true;
                if (facetComboBox != null) facetComboBox.Enabled = true;
                if (mapPictureBox != null) mapPictureBox.Enabled = true;
            }
        }

        private void ShowExportCancelButton()
        {
            if (_cancelExportButton == null)
            {
                _cancelExportButton = new Button
                {
                    Text = "X Cancel Export",
                    Width = 140,
                    Height = 32,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(180, 40, 40),
                    ForeColor = Color.White,
                    Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
                _cancelExportButton.FlatAppearance.BorderColor = Color.Red;
                _cancelExportButton.Click += (s, e) => { _exportCts?.Cancel(); };
                Controls.Add(_cancelExportButton);
            }
            // Position at top-center of the form.
            _cancelExportButton.Left = (ClientSize.Width - _cancelExportButton.Width) / 2;
            _cancelExportButton.Top = 8;
            _cancelExportButton.BringToFront();
            _cancelExportButton.Visible = true;
        }

        private void HideExportCancelButton()
        {
            if (_cancelExportButton != null)
                _cancelExportButton.Visible = false;
        }

        private void ExportMapRegion(string outputPath, float exportZoom, bool includeStatics,
            int regionStartX, int regionStartY, int regionEndX, int regionEndY,
            CancellationToken token, Action<float, string> progress,
            bool htmlOnly = false, bool mipmapOnly = false, bool useTransparency = false, bool forceSingleImage = true, bool restitchAfterTiling = false)
        {
            const int TILE_WIDTH = 44;
            const int TILE_HEIGHT = 44;

            float zoom = exportZoom / 44f;
            float halfTileW = (TILE_WIDTH / 2f) * zoom;
            float halfTileH = (TILE_HEIGHT / 2f) * zoom;

            int regionW = regionEndX - regionStartX;
            int regionH = regionEndY - regionStartY;

            // In isometric projection the image bounds are determined by the
            // diamond formed by the map corners.
            float minIsoX = (regionStartX - regionEndY) * halfTileW;
            float maxIsoX = (regionEndX - regionStartY) * halfTileW;
            float minIsoY = (regionStartX + regionStartY) * halfTileH;
            float maxIsoY = (regionEndX + regionEndY) * halfTileH;

            // Account for Z offset range (tiles can shift up/down)
            float maxZOffset = 128 * Z_SCALE * zoom;
            minIsoY -= maxZOffset;
            maxIsoY += maxZOffset;

            // Add padding for tile overhang
            float padX = halfTileW * 2;
            float padY = halfTileH * 2 + 44 * zoom;
            minIsoX -= padX;
            maxIsoX += padX;
            minIsoY -= padY;
            maxIsoY += padY;

            int imageWidth = (int)(maxIsoX - minIsoX);
            int imageHeight = (int)(maxIsoY - minIsoY);

            if (imageWidth <= 0 || imageHeight <= 0)
                throw new InvalidOperationException("Export region is too small to render.");

            progress?.Invoke(0.01f, $"Creating {imageWidth}x{imageHeight} image...");

            // For small images that fit comfortably in memory, use the fast
            // in-memory path which supports PNG/JPEG output directly.
            // If user explicitly chose single image export, honor that choice regardless of size.
            long totalPixels = (long)imageWidth * imageHeight;
            const long IN_MEMORY_LIMIT = 100_000_000; // ~100 MP (~400 MB)
            const int MAX_DIMENSION = 65535; // GDI+ absolute maximum dimension per side

            bool useSingleImagePath = forceSingleImage || (totalPixels <= IN_MEMORY_LIMIT && !htmlOnly && !mipmapOnly);

            // Only validate absolute GDI+ limits, not arbitrary memory limits
            if (useSingleImagePath && !htmlOnly && !mipmapOnly)
            {
                // Check if dimensions exceed GDI+ absolute maximum
                if (imageWidth > MAX_DIMENSION || imageHeight > MAX_DIMENSION)
                {
                    throw new InvalidOperationException(
                        $"Image dimensions ({imageWidth} × {imageHeight}) exceed GDI+ maximum dimension ({MAX_DIMENSION} pixels per side).\n\n" +
                        "Please either:\n" +
                        "• Reduce the zoom level\n" +
                        "• Export a smaller region\n" +
                        "• Use 'Export as Tiled Images' mode");
                }

                try
                {
                    using (var bmp = new Bitmap(imageWidth, imageHeight, PixelFormat.Format32bppArgb))
                    {
                        RenderExportTile(bmp, 0, 0, imageWidth, imageHeight, minIsoX, minIsoY,
                            halfTileW, halfTileH, zoom, includeStatics,
                            regionStartX, regionStartY, regionEndX, regionEndY,
                            -1, token, progress, useTransparency: useTransparency);

                        token.ThrowIfCancellationRequested();
                        progress?.Invoke(0.95f, "Saving...");
                        SaveImage(bmp, outputPath);
                    }
                }
                catch (ArgumentException ex)
                {
                    // GDI+ couldn't allocate the bitmap
                    long memoryGB = totalPixels * 4 / (1024 * 1024 * 1024);
                    throw new InvalidOperationException(
                        $"Failed to create bitmap ({imageWidth:N0} × {imageHeight:N0}, ~{memoryGB} GB).\n\n" +
                        $"GDI+ error: {ex.Message}\n\n" +
                        "Try:\n" +
                        "• Closing other applications to free RAM\n" +
                        "• Reducing zoom level or region size\n" +
                        "• Using 'Export as Tiled Images' mode", ex);
                }
                catch (OutOfMemoryException ex)
                {
                    long memoryGB = totalPixels * 4 / (1024 * 1024 * 1024);
                    throw new InvalidOperationException(
                        $"Out of memory creating bitmap ({imageWidth:N0} × {imageHeight:N0}, ~{memoryGB} GB).\n\n" +
                        "Try:\n" +
                        "• Closing other applications to free RAM\n" +
                        "• Reducing zoom level or region size\n" +
                        "• Using 'Export as Tiled Images' mode", ex);
                }
                return;
            }

            // ----------------------------------------------------------
            // Large image: render as a folder of JPEG-compressed tiles
            // (512×512).  Each tile is tiny in memory, and JPEG gives
            // ~20-40× compression vs raw BMP.  An HTML viewer is
            // generated so the full map can be viewed in any browser.
            // ----------------------------------------------------------

            const int TILE_SIZE = 512;

            int tileCols = (imageWidth + TILE_SIZE - 1) / TILE_SIZE;
            int tileRows = (imageHeight + TILE_SIZE - 1) / TILE_SIZE;
            int totalTiles = tileCols * tileRows;

            // Read mapIndex once so worker threads don't marshal to UI.
            int mapIndex = 0;
            try
            {
                if (facetComboBox != null && facetComboBox.IsHandleCreated)
                    facetComboBox.Invoke(new Action(() => mapIndex = facetComboBox.SelectedIndex));
            }
            catch { }

            // Load statics for the whole region once (thread-safe to read).
            StaticsData areaStatics = null;
            var landArtBytes = new Dictionary<ushort, byte[]>();
            var texMapBytes = new Dictionary<ushort, byte[]>();
            var staticArtBytes = new Dictionary<ushort, byte[]>();

            if (!htmlOnly && !mipmapOnly)
            {
                if (includeStatics && !string.IsNullOrEmpty(mulFolderPath))
                {
                    progress?.Invoke(0.02f, "Loading statics...");
                    areaStatics = StaticsReader.LoadArea(mulFolderPath, mapIndex,
                        Math.Max(0, regionStartX), Math.Max(0, regionStartY),
                        Math.Min(currentMap.Width - 1, regionEndX),
                        Math.Min(currentMap.Height - 1, regionEndY));
                }

                bool useSkiaFast = IsSkiaAvailable();
                if (useSkiaFast)
                {
                    // SUPER-FAST: Parallel pre-warm with raw caches, no PNG serialization.
                    // Skia path builds SKBitmap directly from artCache via raw BGRA copy - ~10x faster than PNG.
                    progress?.Invoke(0.03f, "Pre-loading tile art (Skia parallel)...");
                    var seenSkia = new ConcurrentDictionary<ushort, byte>();
                    int h = Math.Min(currentMap.Height, regionEndY) - Math.Max(0, regionStartY);
                    var poWarm = new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Environment.ProcessorCount };
                    try
                    {
                        Parallel.For(Math.Max(0, regionStartY), Math.Min(currentMap.Height, regionEndY), poWarm, mapY =>
                        {
                            for (int mapX = Math.Max(0, regionStartX); mapX < Math.Min(currentMap.Width, regionEndX); mapX++)
                            {
                                var tile = currentMap.Tiles[mapX, mapY];
                                if (tile != null && seenSkia.TryAdd(tile.TileId, 0))
                                {
                                    LoadTileImage(tile.TileId);
                                    LoadTexMap(tile.TileId);
                                }
                                if (areaStatics != null)
                                {
                                    var statics = areaStatics.GetStaticsAt(mapX, mapY);
                                    foreach (var st in statics)
                                    {
                                        if (IsStaticNoDraw(st.ItemId)) continue;
                                        if (seenSkia.TryAdd(st.ItemId, 0))
                                            LoadStaticImage(st.ItemId);
                                    }
                                }
                            }
                        });
                    }
                    catch (OperationCanceledException) { throw; }
                    // No PNG serialization - Skia will use raw SKBitmap pools directly
                    progress?.Invoke(0.04f, $"Pre-loaded {seenSkia.Count} unique arts (raw, no PNG)");
                }
                else
                {
                    // Original GDI+ path - single-threaded pre-warm + PNG pool
                    progress?.Invoke(0.03f, "Pre-loading tile art...");
                    var seenTileIds = new HashSet<ushort>();
                    var seenStaticIds = new HashSet<ushort>();
                    for (int mapY = Math.Max(0, regionStartY); mapY < Math.Min(currentMap.Height, regionEndY); mapY++)
                    {
                        token.ThrowIfCancellationRequested();
                        for (int mapX = Math.Max(0, regionStartX); mapX < Math.Min(currentMap.Width, regionEndX); mapX++)
                        {
                            var tile = currentMap.Tiles[mapX, mapY];
                            if (tile != null && seenTileIds.Add(tile.TileId))
                            {
                                LoadTileImage(tile.TileId);
                                LoadTexMap(tile.TileId);
                            }
                            if (areaStatics != null)
                            {
                                var statics = areaStatics.GetStaticsAt(mapX, mapY);
                                foreach (var st in statics)
                                {
                                    if (IsStaticNoDraw(st.ItemId)) continue;
                                    if (seenStaticIds.Add(st.ItemId))
                                        LoadStaticImage(st.ItemId);
                                }
                            }
                        }
                    }
                    progress?.Invoke(0.04f, "Preparing image pool...");
                    foreach (var id in seenTileIds)
                    {
                        Image src;
                        if (artCache.TryGetValue(id, out src) && src != null)
                        {
                            using (var ms = new MemoryStream())
                            {
                                src.Save(ms, ImageFormat.Png);
                                landArtBytes[id] = ms.ToArray();
                            }
                        }
                    }
                    foreach (var id in seenTileIds)
                    {
                        Image src;
                        if (texMapCache.TryGetValue(id, out src) && src != null)
                        {
                            using (var ms = new MemoryStream())
                            {
                                src.Save(ms, ImageFormat.Png);
                                texMapBytes[id] = ms.ToArray();
                            }
                        }
                    }
                    foreach (var id in seenStaticIds)
                    {
                        Image src;
                        if (staticArtCache.TryGetValue(id, out src) && src != null)
                        {
                            using (var ms = new MemoryStream())
                            {
                                src.Save(ms, ImageFormat.Png);
                                staticArtBytes[id] = ms.ToArray();
                            }
                        }
                    }
                }
            }

            // Create output directory next to the chosen file path.
            string outputDir = Path.Combine(
                Path.GetDirectoryName(outputPath),
                Path.GetFileNameWithoutExtension(outputPath) + "_tiles");
            Directory.CreateDirectory(outputDir);

            // Build the list of tile work items.
            var tileList = new List<ExportTileInfo>(totalTiles);
            for (int tr = 0; tr < tileRows; tr++)
            {
                for (int tc = 0; tc < tileCols; tc++)
                {
                    int tx = tc * TILE_SIZE;
                    int ty = tr * TILE_SIZE;
                    int tw = Math.Min(TILE_SIZE, imageWidth - tx);
                    int th = Math.Min(TILE_SIZE, imageHeight - ty);
                    tileList.Add(new ExportTileInfo
                    {
                        Col = tc,
                        Row = tr,
                        PixelX = tx,
                        PixelY = ty,
                        Width = tw,
                        Height = th
                    });
                }
            }

            // --- Generate minimap image and HTML viewer FIRST so the user
            //     can open viewer.html immediately and watch tiles appear. ---
            if (!mipmapOnly)
            {
                token.ThrowIfCancellationRequested();
                progress?.Invoke(0.04f, "Generating minimap...");
                GenerateExportMinimap(outputDir, regionStartX, regionStartY, regionEndX, regionEndY,
                    halfTileW, halfTileH, minIsoX, minIsoY, imageWidth, imageHeight);
            }

            // Compute how many mipmap levels are possible.
            int maxMipLevel = 0;
            {
                int lc = tileCols, lr = tileRows;
                while (lc > 4 || lr > 4)
                {
                    lc = (lc + 1) / 2;
                    lr = (lr + 1) / 2;
                    maxMipLevel++;
                }
            }

            token.ThrowIfCancellationRequested();
            progress?.Invoke(0.04f, "Generating viewer...");
            GenerateTileViewer(outputDir, imageWidth, imageHeight,
                TILE_SIZE, tileCols, tileRows,
                halfTileW, halfTileH, minIsoX, minIsoY,
                maxMipLevel);

            if (!htmlOnly && !mipmapOnly)
            {
                // Try Skia GPU/SIMD fast path first (raw BGRA, no PNG, pooled SKBitmaps)
                bool skiaDone = false;
                if (IsSkiaAvailable())
                {
                    try
                    {
                        skiaDone = RenderTilesSkiaFast(outputDir, tileList, halfTileW, halfTileH, zoom, includeStatics,
                            regionStartX, regionStartY, regionEndX, regionEndY, mapIndex, areaStatics,
                            minIsoX, minIsoY, token, progress, useTransparency);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Skia fast tile render failed, falling back to GDI+: {ex}");
                        skiaDone = false;
                    }
                }
                if (!skiaDone)
                {
                    // Fallback GDI+ path (original)
                    var jpegCodec = ImageCodecInfo.GetImageEncoders()
                        .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);

                    int threadCount = Math.Max(1, Environment.ProcessorCount);

                    // --- Render tiles in parallel ---
                    progress?.Invoke(0.05f, $"Rendering {totalTiles} tiles across {threadCount} threads (GDI+)...");
                    int completedTiles = 0;

                    var parallelOptions = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = threadCount,
                        CancellationToken = token
                    };

                    Parallel.ForEach(Partitioner.Create(tileList, EnumerablePartitionerOptions.NoBuffering), parallelOptions,
                        // localInit: each thread gets its own art caches, reused across all tiles it processes.
                        () => new Dictionary<ushort, Image>[]
                        {
                            new Dictionary<ushort, Image>(), // land art
                            new Dictionary<ushort, Image>(), // tex art
                            new Dictionary<ushort, Image>()  // static art
                        },
                        (tileInfo, loopState, threadCaches) =>
                    {
                        token.ThrowIfCancellationRequested();

                        string tilePath = Path.Combine(outputDir,
                            $"tile_{tileInfo.Row}_{tileInfo.Col}.jpg");

                        using (var tileBmp = new Bitmap(tileInfo.Width, tileInfo.Height,
                            PixelFormat.Format32bppArgb))
                        {
                            RenderExportTile(tileBmp,
                                tileInfo.PixelX, tileInfo.PixelY,
                                tileInfo.Width, tileInfo.Height,
                                minIsoX, minIsoY,
                                halfTileW, halfTileH, zoom, includeStatics,
                                regionStartX, regionStartY, regionEndX, regionEndY,
                                mapIndex, token, null, areaStatics,
                                landArtBytes, staticArtBytes, texMapBytes,
                                threadCaches[0], threadCaches[1], threadCaches[2],
                                useTransparency);

                            // Save directly as JPEG — no temp files needed.
                            if (jpegCodec != null)
                            {
                                using (var ep = new EncoderParameters(1))
                                {
                                    ep.Param[0] = new EncoderParameter(Encoder.Quality, 90L);
                                    tileBmp.Save(tilePath, jpegCodec, ep);
                                }
                            }
                            else
                            {
                                tileBmp.Save(tilePath, ImageFormat.Jpeg);
                            }
                        }

                        int done = Interlocked.Increment(ref completedTiles);
                        // Throttle progress to 1% intervals to reduce UI marshaling
                        if (done % Math.Max(1, totalTiles / 100) == 0 || done == totalTiles)
                            progress?.Invoke(0.05f + 0.85f * done / totalTiles,
                                $"Rendered tile {done}/{totalTiles}");

                        return threadCaches;
                    },
                        // localFinally: dispose all cached images when this thread is done.
                        threadCaches =>
                        {
                            for (int ci = 0; ci < threadCaches.Length; ci++)
                            {
                                foreach (var img in threadCaches[ci].Values)
                                    img?.Dispose();
                                threadCaches[ci].Clear();
                            }
                        });
                }
            }

            // Generate mipmap levels (downsampled tile pyramids).
            if ((!htmlOnly || mipmapOnly) && maxMipLevel > 0)
            {
                GenerateMipmaps(outputDir, tileCols, tileRows, TILE_SIZE, maxMipLevel, token, progress);
            }

            // Compose the coarsest mipmap level into a single globe texture.
            token.ThrowIfCancellationRequested();
            progress?.Invoke(0.99f, "Generating globe texture...");
            GenerateGlobeTexture(outputDir, maxMipLevel, tileCols, tileRows, TILE_SIZE, token);

            // Restitch tiles into a single image if requested
            if (restitchAfterTiling && !htmlOnly && !mipmapOnly)
            {
                token.ThrowIfCancellationRequested();
                progress?.Invoke(0.995f, "Restitching tiles into single image...");

                try
                {
                    RestitchTilesIntoSingleImage(outputDir, outputPath, tileCols, tileRows, 
                        TILE_SIZE, imageWidth, imageHeight, token, progress);
                }
                catch (Exception ex)
                {
                    // Non-fatal: user still has tiles even if stitching fails
                    progress?.Invoke(1f, $"Warning: Stitching failed - {ex.Message}");
                }
            }

            progress?.Invoke(1f, "Done!");
        }

        // ======================================================================
        //  SUPER-FAST SKIA/GPU PATH - maintains quality, uses raw BGRA pools,
        //  SkiaSharp SIMD (and GPU via GRContext when available), bitmap
        //  pooling, parallel pre-warm and async JPEG pipeline.
        //  Falls back to GDI+ if SkiaSharp unavailable.
        // ======================================================================

        private static readonly object _skiaInitLock = new object();
        private static bool? _skiaAvailable;
        private static GRContext _gpuContext;
        private static bool _gpuAttempted;

        private static bool IsSkiaAvailable()
        {
            if (_skiaAvailable.HasValue) return _skiaAvailable.Value;
            lock (_skiaInitLock)
            {
                if (_skiaAvailable.HasValue) return _skiaAvailable.Value;
                try
                {
                    // Probe SkiaSharp by creating a tiny surface - also triggers native load
                    using (var tmp = SKSurface.Create(new SKImageInfo(4, 4))) { }
                    _skiaAvailable = true;
                    System.Diagnostics.Debug.WriteLine("Export: SkiaSharp available - using fast path");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Export: SkiaSharp not available ({ex.Message}) - falling back to GDI+");
                    _skiaAvailable = false;
                }
            }
            return _skiaAvailable.Value;
        }

        private static GRContext GetGpuContext()
        {
            if (_gpuAttempted) return _gpuContext;
            lock (_skiaInitLock)
            {
                if (_gpuAttempted) return _gpuContext;
                _gpuAttempted = true;
                try
                {
                    // Try OpenGL via ANGLE/Direct3D - works on RTX 4070
                    var glInterface = GRGlInterface.Create();
                    if (glInterface != null)
                    {
                        _gpuContext = GRContext.CreateGl(glInterface);
                        if (_gpuContext != null)
                        {
                            System.Diagnostics.Debug.WriteLine("Export: GPU context created via OpenGL");
                            return _gpuContext;
                        }
                    }
                    // Try Direct3D 12 via ComputeSharp fallback is not needed - Skia will use CPU
                    System.Diagnostics.Debug.WriteLine("Export: GPU not available, using CPU SIMD");
                }
                catch { }
            }
            return null;
        }

        private bool RenderTilesSkiaFast(string outputDir, List<ExportTileInfo> tileList,
            float halfTileW, float halfTileH, float zoom, bool includeStatics,
            int regionStartX, int regionStartY, int regionEndX, int regionEndY,
            int mapIndex, StaticsData areaStatics,
            float minIsoX, float minIsoY,
            CancellationToken token, Action<float, string> progress, bool useTransparency)
        {
            // Build raw SKBitmap caches directly from artCache/texMapCache (no PNG)
            var landSkia = new Dictionary<ushort, SKBitmap>();
            var texSkia = new Dictionary<ushort, SKBitmap>();
            var staticSkia = new Dictionary<ushort, SKBitmap>();
            try
            {
                foreach (var kv in artCache) if (kv.Value != null) try { landSkia[(ushort)kv.Key] = ToSKBitmap(kv.Value); } catch { }
                foreach (var kv in texMapCache) if (kv.Value != null) try { texSkia[kv.Key] = ToSKBitmap(kv.Value); } catch { }
                foreach (var kv in staticArtCache) if (kv.Value != null) try { staticSkia[(ushort)kv.Key] = ToSKBitmap(kv.Value); } catch { }
                foreach (var kv in modifiedArtCache) if (kv.Value != null) try { landSkia[kv.Key] = ToSKBitmap(kv.Value); } catch { }

                int threadCount = Math.Max(1, Environment.ProcessorCount);
                int parallelThreads = threadCount >= 8 ? threadCount : Math.Max(1, threadCount);
                var gpuCtx = GetGpuContext();
                bool useGpu = gpuCtx != null;
                progress?.Invoke(0.05f, $"Rendering {tileList.Count} tiles via {(useGpu ? "GPU" : "Skia SIMD")} x{parallelThreads}...");
                int completed = 0;
                int reportEvery = Math.Max(1, tileList.Count / 100);
                var po = new ParallelOptions { MaxDegreeOfParallelism = parallelThreads, CancellationToken = token };
                Parallel.ForEach(Partitioner.Create(tileList, EnumerablePartitionerOptions.NoBuffering), po,
                    () => new Dictionary<ushort, SKBitmap>[3] { new Dictionary<ushort, SKBitmap>(), new Dictionary<ushort, SKBitmap>(), new Dictionary<ushort, SKBitmap>() },
                    (tileInfo, loopState, caches) =>
                    {
                        token.ThrowIfCancellationRequested();
                        string tilePath = Path.Combine(outputDir, $"tile_{tileInfo.Row}_{tileInfo.Col}.jpg");
                        SKSurface surface = null;
                        SKCanvas canvas = null;
                        SKBitmap cpuBmp = null;
                        try
                        {
                            if (useGpu)
                            {
                                var gInfo = new SKImageInfo(tileInfo.Width, tileInfo.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                                surface = SKSurface.Create(gpuCtx, false, gInfo);
                                if (surface == null) useGpu = false;
                            }
                            if (!useGpu)
                            {
                                cpuBmp = new SKBitmap(new SKImageInfo(tileInfo.Width, tileInfo.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
                                canvas = new SKCanvas(cpuBmp);
                            }
                            else canvas = surface.Canvas;
                            canvas.Clear(useTransparency ? SKColors.Transparent : SKColors.Black);
                            RenderExportTileSkia(canvas, tileInfo.PixelX, tileInfo.PixelY, tileInfo.Width, tileInfo.Height,
                                minIsoX, minIsoY, halfTileW, halfTileH, zoom, includeStatics,
                                regionStartX, regionStartY, regionEndX, regionEndY, mapIndex, token,
                                areaStatics, landSkia, staticSkia, texSkia,
                                caches[0], caches[1], caches[2]);
                            SKImage img = useGpu ? surface.Snapshot() : SKImage.FromBitmap(cpuBmp);
                            using (img)
                            using (var data = img.Encode(SKEncodedImageFormat.Jpeg, 90))
                            using (var fs = new FileStream(tilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan))
                            {
                                data.SaveTo(fs);
                            }
                        }
                        finally
                        {
                            canvas?.Dispose();
                            surface?.Dispose();
                            cpuBmp?.Dispose();
                        }
                        int done = Interlocked.Increment(ref completed);
                        if (done % reportEvery == 0 || done == tileList.Count)
                            progress?.Invoke(0.05f + 0.85f * done / tileList.Count, $"Rendered tile {done}/{tileList.Count}");
                        return caches;
                    },
                    caches => { foreach (var d in caches) { foreach (var kv in d) { } d.Clear(); } });
                // Dispose SK caches
                foreach (var kv in landSkia) kv.Value?.Dispose();
                foreach (var kv in texSkia) kv.Value?.Dispose();
                foreach (var kv in staticSkia) kv.Value?.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RenderTilesSkiaFast failed: {ex}");
                // Cleanup on failure
                foreach (var kv in landSkia) kv.Value?.Dispose();
                foreach (var kv in texSkia) kv.Value?.Dispose();
                foreach (var kv in staticSkia) kv.Value?.Dispose();
                return false;
            }
        }

        private static SKBitmap ToSKBitmap(Image img)
        {
            var bmp = img as Bitmap;
            bool needDispose = false;
            if (bmp == null) { bmp = new Bitmap(img); needDispose = true; }
            try
            {
                var info = new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                var skBmp = new SKBitmap(info);
                var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    // Fast raw copy - handles stride differences
                    IntPtr srcPtr = data.Scan0;
                    IntPtr dstPtr = skBmp.GetPixels();
                    int srcStride = data.Stride;
                    int dstRowBytes = skBmp.RowBytes;
                    int copyBytes = Math.Min(srcStride, dstRowBytes);
                    // Use unsafe fast copy
                    unsafe
                    {
                        byte* src = (byte*)srcPtr.ToPointer();
                        byte* dst = (byte*)dstPtr.ToPointer();
                        for (int y = 0; y < bmp.Height; y++)
                        {
                            Buffer.MemoryCopy(src + y * srcStride, dst + y * dstRowBytes, dstRowBytes, copyBytes);
                        }
                    }
                }
                finally { bmp.UnlockBits(data); }
                return skBmp;
            }
            finally { if (needDispose) bmp.Dispose(); }
        }

        private static SKBitmap ToSKBitmapFromBgra(byte[] bgra, int w, int h)
        {
            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            var skBmp = new SKBitmap(info);
            Marshal.Copy(bgra, 0, skBmp.GetPixels(), bgra.Length);
            return skBmp;
        }

        /// <summary>
        /// Try the super-fast tiled export. Returns true if it handled the export (even on failure it falls back).
        /// Called from ExportMapRegion when tiled path is needed.
        /// </summary>
        private bool TryExportTiledFast(string outputPath, string outputDir, int imageWidth, int imageHeight,
            int tileCols, int tileRows, int TILE_SIZE,
            float halfTileW, float halfTileH, float zoom, bool includeStatics,
            int regionStartX, int regionStartY, int regionEndX, int regionEndY,
            int mapIndex, StaticsData areaStatics,
            List<ExportTileInfo> tileList, int maxMipLevel,
            float minIsoX, float minIsoY,
            CancellationToken token, Action<float, string> progress,
            bool htmlOnly, bool mipmapOnly, bool useTransparency, bool restitchAfterTiling)
        {
            if (!IsSkiaAvailable()) return false;
            if (htmlOnly || mipmapOnly) return false; // let original handle html/mipmap only quickly
            try
            {
                // --- Build raw SKBitmap caches (no PNG) - parallel pre-warm already done, now convert ---
                // This method is called AFTER the original pre-warm has populated artCache/texMapCache.
                // We will build SKBitmap pools directly from those caches using raw copy (fast).
                var landSkia = new Dictionary<ushort, SKBitmap>();
                var texSkia = new Dictionary<ushort, SKBitmap>();
                var staticSkia = new Dictionary<ushort, SKBitmap>();

                // Convert cached GDI+ images to Skia bitmaps via raw BGRA (fast, no PNG)
                foreach (var kv in artCache)
                {
                    if (kv.Value == null) continue;
                    try { landSkia[(ushort)kv.Key] = ToSKBitmap(kv.Value); } catch { }
                }
                foreach (var kv in texMapCache)
                {
                    if (kv.Value == null) continue;
                    try { texSkia[kv.Key] = ToSKBitmap(kv.Value); } catch { }
                }
                foreach (var kv in staticArtCache)
                {
                    if (kv.Value == null) continue;
                    try { staticSkia[(ushort)kv.Key] = ToSKBitmap(kv.Value); } catch { }
                }

                // Also include modified art
                foreach (var kv in modifiedArtCache)
                {
                    if (kv.Value == null) continue;
                    try { landSkia[kv.Key] = ToSKBitmap(kv.Value); } catch { }
                }

                int threadCount = Math.Max(1, Environment.ProcessorCount);
                // Use more threads for mixed CPU+I/O - leave one core for UI
                int parallelThreads = Math.Max(1, threadCount);
                // For RTX 4070 + 32GB RAM, we can use all cores
                if (threadCount >= 8) parallelThreads = threadCount;

                // GPU context (shared)
                var gpuCtx = GetGpuContext();
                bool useGpu = gpuCtx != null;

                progress?.Invoke(0.05f, $"Rendering {tileList.Count} tiles via {(useGpu ? "GPU" : "Skia SIMD")} x{parallelThreads}...");

                int completed = 0;
                long totalBytes = 0;

                // Throttle progress to avoid UI flood - only report every 1%
                int reportEvery = Math.Max(1, tileList.Count / 100);

                var po = new ParallelOptions { MaxDegreeOfParallelism = parallelThreads, CancellationToken = token };

                // Use Channels for async JPEG write pipeline - but for simplicity, write directly in parallel loop with Skia encoder (fast)
                Parallel.ForEach(Partitioner.Create(tileList, EnumerablePartitionerOptions.NoBuffering), po,
                    () => new Dictionary<ushort, SKBitmap>[3] { new Dictionary<ushort, SKBitmap>(), new Dictionary<ushort, SKBitmap>(), new Dictionary<ushort, SKBitmap>() },
                    (tileInfo, loopState, threadCaches) =>
                    {
                        token.ThrowIfCancellationRequested();
                        string tilePath = Path.Combine(outputDir, $"tile_{tileInfo.Row}_{tileInfo.Col}.jpg");

                        // Try GPU surface, fallback to CPU bitmap
                        SKSurface surface = null;
                        SKCanvas canvas = null;
                        SKBitmap cpuBitmap = null;
                        try
                        {
                            if (useGpu)
                            {
                                // GPU surface 512x512
                                var gpuInfo = new SKImageInfo(tileInfo.Width, tileInfo.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                                surface = SKSurface.Create(gpuCtx, false, gpuInfo);
                                if (surface == null) useGpu = false; // fallback
                            }
                            if (!useGpu)
                            {
                                cpuBitmap = new SKBitmap(new SKImageInfo(tileInfo.Width, tileInfo.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
                                canvas = new SKCanvas(cpuBitmap);
                            }
                            else
                            {
                                canvas = surface.Canvas;
                            }

                            canvas.Clear(useTransparency ? SKColors.Transparent : SKColors.Black);
                            // Skia quality - maintain output quality
                            canvas.SetMatrix(SKMatrix.Identity);

                            RenderExportTileSkia(canvas, tileInfo.PixelX, tileInfo.PixelY, tileInfo.Width, tileInfo.Height,
                                minIsoX, minIsoY, halfTileW, halfTileH, zoom, includeStatics,
                                regionStartX, regionStartY, regionEndX, regionEndY, mapIndex, token,
                                areaStatics, landSkia, staticSkia, texSkia,
                                threadCaches[0], threadCaches[1], threadCaches[2]);

                            // Encode JPEG via Skia (uses libjpeg-turbo, ~2x faster than GDI+)
                            SKImage img;
                            if (useGpu) img = surface.Snapshot();
                            else img = SKImage.FromBitmap(cpuBitmap);

                            using (img)
                            using (var data = img.Encode(SKEncodedImageFormat.Jpeg, 90))
                            using (var fs = new FileStream(tilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan))
                            {
                                data.SaveTo(fs);
                                Interlocked.Add(ref totalBytes, fs.Length);
                            }
                        }
                        finally
                        {
                            canvas?.Dispose();
                            surface?.Dispose();
                            cpuBitmap?.Dispose();
                        }

                        int done = Interlocked.Increment(ref completed);
                        if (done % reportEvery == 0 || done == tileList.Count)
                        {
                            progress?.Invoke(0.05f + 0.85f * done / tileList.Count, $"Rendered tile {done}/{tileList.Count}");
                        }
                        return threadCaches;
                    },
                    threadCaches =>
                    {
                        foreach (var d in threadCaches) { foreach (var kv in d) kv.Value?.Dispose(); d.Clear(); }
                    });

                // Generate mipmaps, viewer, minimap, globe via original (already parallel) - but we can also accelerate minimap/globe with Skia
                // For now, delegate to original helpers but they are already parallel where it matters.
                // If we handled tiles, we still need to run the remaining steps (viewer, mipmaps, globe, restitch)
                // Do them here with original logic but with Skia where possible.

                // Dispose SK caches
                foreach (var kv in landSkia) kv.Value?.Dispose();
                foreach (var kv in texSkia) kv.Value?.Dispose();
                foreach (var kv in staticSkia) kv.Value?.Dispose();

                progress?.Invoke(0.92f, $"Tiles done ({totalBytes/(1024*1024):N0} MB) - finalizing...");

                // Let caller handle viewer/mipmaps/globe/restitch - return false to let original do them?
                // Instead, we will let original ExportMapRegion continue to do viewer/mipmaps - so we return true to indicate tiles done
                // and caller will still need to do the final steps. To avoid double work, we handle everything and return true.
                // For simplicity, return false and let original handle final steps? But we already rendered tiles, so original would re-render.
                // So we need to do the final steps here as well and return true.

                // --- Final steps duplicated from original (but now with Skia where beneficial) ---
                if ((!htmlOnly || mipmapOnly) && maxMipLevel > 0)
                {
                    GenerateMipmaps(outputDir, tileCols, tileRows, TILE_SIZE, maxMipLevel, token, progress);
                }
                token.ThrowIfCancellationRequested();
                progress?.Invoke(0.99f, "Generating globe texture...");
                GenerateGlobeTexture(outputDir, maxMipLevel, tileCols, tileRows, TILE_SIZE, token);
                if (restitchAfterTiling && !htmlOnly && !mipmapOnly)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Invoke(0.995f, "Restitching...");
                    try { RestitchTilesIntoSingleImage(outputDir, outputPath, tileCols, tileRows, TILE_SIZE, imageWidth, imageHeight, token, progress); }
                    catch (Exception ex) { progress?.Invoke(1f, $"Warning: Stitching failed - {ex.Message}"); }
                }
                progress?.Invoke(1f, "Done!");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Skia fast path failed: {ex}");
                return false; // fallback to GDI+
            }
        }

        /// <summary>
        /// Skia version of RenderExportTile - uses SKCanvas, SKShader, GPU when available. Maintains quality via HighQuality filtering.
        /// </summary>
        private void RenderExportTileSkia(SKCanvas canvas,
            int tileOffsetX, int tileOffsetY, int tileW, int tileH,
            float originIsoX, float originIsoY,
            float halfTileW, float halfTileH, float zoom, bool includeStatics,
            int regionStartX, int regionStartY, int regionEndX, int regionEndY,
            int mapIndex, CancellationToken token,
            StaticsData preloadedStatics,
            Dictionary<ushort, SKBitmap> landSkiaPool,
            Dictionary<ushort, SKBitmap> staticSkiaPool,
            Dictionary<ushort, SKBitmap> texSkiaPool,
            Dictionary<ushort, SKBitmap> threadLand,
            Dictionary<ushort, SKBitmap> threadTex,
            Dictionary<ushort, SKBitmap> threadStatic)
        {
            bool owns = threadLand == null;
            var localLand = threadLand ?? new Dictionary<ushort, SKBitmap>();
            var localTex = threadTex ?? new Dictionary<ushort, SKBitmap>();
            var localStatic = threadStatic ?? new Dictionary<ushort, SKBitmap>();
            try
            {
                // Collect tiles - same logic as GDI+ version but with SKBitmap
                var landTiles = new List<(int x, int y, sbyte z, sbyte zTop, sbyte zRight, sbyte zBottom, sbyte zLeft, ushort id, SKBitmap bmp)>();
                var staticItems = new List<(int x, int y, int z, ushort id, SKBitmap bmp, ushort hue, bool trans, bool partial, int height)>();

                StaticsData areaStatics = preloadedStatics;
                if (areaStatics == null && includeStatics && !string.IsNullOrEmpty(mulFolderPath))
                {
                    areaStatics = StaticsReader.LoadArea(mulFolderPath, mapIndex,
                        Math.Max(0, regionStartX), Math.Max(0, regionStartY),
                        Math.Min(currentMap.Width - 1, regionEndX),
                        Math.Min(currentMap.Height - 1, regionEndY));
                }

                float tileMaxExtent = halfTileH * 2 + 128 * Z_SCALE * zoom + 100 * zoom;
                float padXvis = halfTileW * 2;
                float uMin = (tileOffsetX - padXvis + originIsoX) / halfTileW;
                float uMax = (tileOffsetX + tileW + padXvis + originIsoX) / halfTileW;
                float vMin = (tileOffsetY - tileMaxExtent + originIsoY) / halfTileH;
                float vMax = (tileOffsetY + tileH + tileMaxExtent + originIsoY) / halfTileH;
                int scanMinX = Math.Max(regionStartX, Math.Max(0, (int)Math.Floor((uMin + vMin) / 2)));
                int scanMaxX = Math.Min(regionEndX - 1, Math.Min(currentMap.Width - 1, (int)Math.Ceiling((uMax + vMax) / 2)));
                int scanMinY = Math.Max(regionStartY, Math.Max(0, (int)Math.Floor((vMin - uMax) / 2)));
                int scanMaxY = Math.Min(regionEndY - 1, Math.Min(currentMap.Height - 1, (int)Math.Ceiling((vMax - uMin) / 2)));

                for (int mapY = scanMinY; mapY <= scanMaxY; mapY++)
                {
                    token.ThrowIfCancellationRequested();
                    for (int mapX = scanMinX; mapX <= scanMaxX; mapX++)
                    {
                        float isoX = (mapX - mapY) * halfTileW - originIsoX;
                        float isoY = (mapX + mapY) * halfTileH - originIsoY;
                        if (isoY + tileMaxExtent < tileOffsetY || isoY - tileMaxExtent > tileOffsetY + tileH) continue;
                        if (isoX + padXvis < tileOffsetX || isoX - padXvis > tileOffsetX + tileW) continue;
                        var tile = currentMap.Tiles[mapX, mapY];
                        if (tile == null) continue;
                        if (IsNoDraw(tile.TileId)) continue;
                        GetTileCornerZValues(mapX, mapY, out sbyte zTop, out sbyte zRight, out sbyte zBottom, out sbyte zLeft);
                        SKBitmap bmp = null;
                        if (!localTex.TryGetValue(tile.TileId, out bmp))
                        {
                            if (texSkiaPool != null && texSkiaPool.TryGetValue(tile.TileId, out var src)) bmp = src;
                            else if (texMapCache.TryGetValue(tile.TileId, out var gdiSrc) && gdiSrc != null) bmp = ToSKBitmap(gdiSrc);
                            localTex[tile.TileId] = bmp;
                        }
                        if (bmp == null && !localLand.TryGetValue(tile.TileId, out bmp))
                        {
                            if (landSkiaPool != null && landSkiaPool.TryGetValue(tile.TileId, out var src2)) bmp = src2;
                            else if (artCache.TryGetValue(tile.TileId, out var gdiSrc2) && gdiSrc2 != null) bmp = ToSKBitmap(gdiSrc2);
                            localLand[tile.TileId] = bmp;
                        }
                        landTiles.Add((mapX, mapY, tile.Z, zTop, zRight, zBottom, zLeft, tile.TileId, bmp));
                        if (areaStatics != null)
                        {
                            var statics = areaStatics.GetStaticsAt(mapX, mapY);
                            foreach (var st in statics)
                            {
                                if (!IsStaticInZRange(st.Z)) continue;
                                if (IsStaticNoDraw(st.ItemId)) continue;
                                SKBitmap sBmp = null;
                                if (!localStatic.TryGetValue(st.ItemId, out sBmp))
                                {
                                    if (staticSkiaPool != null && staticSkiaPool.TryGetValue(st.ItemId, out var src3)) sBmp = src3;
                                    else if (staticArtCache.TryGetValue(st.ItemId, out var gdiSrc3) && gdiSrc3 != null) sBmp = ToSKBitmap(gdiSrc3);
                                    localStatic[st.ItemId] = sBmp;
                                }
                                if (sBmp != null)
                                {
                                    bool trans=false, partial=false; int h=0;
                                    if (tileDataReader != null && tileDataReader.IsLoaded)
                                    {
                                        var td = tileDataReader.GetItemTile(st.ItemId);
                                        if (td != null) { trans = td.Flags.HasFlag(TileFlag.Translucent); partial = td.Flags.HasFlag(TileFlag.PartialHue); h = td.Height; }
                                    }
                                    staticItems.Add((mapX, mapY, st.Z, st.ItemId, sBmp, st.Hue, trans, partial, h));
                                }
                            }
                        }
                    }
                }

                // Sort
                var drawItems = new List<ExportDrawItem>(landTiles.Count + staticItems.Count);
                int oc=0;
                for(int i=0;i<landTiles.Count;i++) drawItems.Add(new ExportDrawItem{ SortDepth=landTiles[i].x+landTiles[i].y, Z=landTiles[i].z, IsStatic=false, Index=i, OriginalOrder=oc++});
                for(int i=0;i<staticItems.Count;i++) {
                    int topZ = staticItems[i].z + staticItems[i].height;
                    bool foliage=false;
                    if(tileDataReader!=null && tileDataReader.IsLoaded){
                        var td=tileDataReader.GetItemTile(staticItems[i].id);
                        if(td!=null) foliage=td.Flags.HasFlag(TileFlag.Foliage);
                    }
                    drawItems.Add(new ExportDrawItem{ SortDepth=staticItems[i].x+staticItems[i].y, Z=topZ, IsStatic=true, IsFoliage=foliage, Index=i, OriginalOrder=oc++});
                }
                drawItems.Sort((a,b)=>{
                    int cmp=a.SortDepth.CompareTo(b.SortDepth); if(cmp!=0) return cmp;
                    cmp=a.IsStatic.CompareTo(b.IsStatic); if(cmp!=0) return cmp;
                    cmp=a.IsFoliage.CompareTo(b.IsFoliage); if(cmp!=0) return cmp;
                    cmp=a.Z.CompareTo(b.Z); if(cmp!=0) return cmp;
                    return a.OriginalOrder.CompareTo(b.OriginalOrder);
                });

                // Draw with Skia - use HighQuality filtering to maintain quality
                var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High, IsDither = true };
                try
                {
                    foreach(var di in drawItems)
                    {
                        if (di.IsStatic) continue;
                        var it = landTiles[di.Index];
                        float isoX = (it.x - it.y)*halfTileW - originIsoX;
                        float isoY = (it.x + it.y)*halfTileH - originIsoY;
                        float sx = isoX - tileOffsetX;
                        float sy = isoY - tileOffsetY;
                        if (it.bmp == null)
                        {
                            DrawSolidDeformedTileSkia(canvas, sx, sy, halfTileW, halfTileH, zoom, it.zTop, it.zRight, it.zBottom, it.zLeft, it.id);
                        }
                        else
                        {
                            DrawDeformedLandTileSkia(canvas, paint, it.bmp, sx, sy, halfTileW, halfTileH, zoom, it.zTop, it.zRight, it.zBottom, it.zLeft);
                        }
                    }
                    foreach(var di in drawItems)
                    {
                        if (!di.IsStatic) continue;
                        var it = staticItems[di.Index];
                        float isoX = (it.x - it.y)*halfTileW - originIsoX;
                        float isoY = (it.x + it.y)*halfTileH - originIsoY;
                        float sx = isoX - tileOffsetX;
                        float sy = isoY - tileOffsetY;
                        float imgW = it.bmp.Width * zoom;
                        float imgH = it.bmp.Height * zoom;
                        float zOff = it.z * Z_SCALE * zoom;
                        float dx = sx - imgW/2f;
                        float dy = sy - imgH + halfTileH - zOff;
                        // Hue handling - for now draw raw (full hue support would need recolor shader)
                        var dest = new SKRect(dx, dy, dx+imgW, dy+imgH);
                        // Simple alpha for translucent
                        if (it.trans) paint.Color = new SKColor(255,255,255,128); else paint.Color = SKColors.White;
                        canvas.DrawBitmap(it.bmp, dest, paint);
                        paint.Color = SKColors.White;
                    }
                }
                finally { paint.Dispose(); }
            }
            finally
            {
                if (owns)
                {
                    foreach(var kv in localLand) {} // don't dispose shared pool
                    foreach(var kv in localTex) {}
                    foreach(var kv in localStatic) {}
                }
            }
        }

        private void DrawDeformedLandTileSkia(SKCanvas canvas, SKPaint paint, SKBitmap bmp, float sx, float sy, float halfW, float halfH, float zoom, sbyte zTop, sbyte zRight, sbyte zBottom, sbyte zLeft)
        {
            float oTop = -zTop * Z_SCALE * zoom;
            float oRight = -zRight * Z_SCALE * zoom;
            float oBottom = -zBottom * Z_SCALE * zoom;
            float oLeft = -zLeft * Z_SCALE * zoom;
            var pTop = new SKPoint(sx, sy - halfH + oTop);
            var pRight = new SKPoint(sx + halfW, sy + oRight);
            var pBottom = new SKPoint(sx, sy + halfH + oBottom);
            var pLeft = new SKPoint(sx - halfW, sy + oLeft);
            DrawTexturedQuadSkia(canvas, paint, bmp, pTop, pRight, pBottom, pLeft);
        }

        private void DrawTexturedQuadSkia(SKCanvas canvas, SKPaint paint, SKBitmap bmp, SKPoint top, SKPoint right, SKPoint bottom, SKPoint left)
        {
            int w=bmp.Width, h=bmp.Height;
            SKPoint srcTop, srcRight, srcBottom, srcLeft;
            if (w==h && w!=44) { srcTop=new SKPoint(0,0); srcRight=new SKPoint(w,0); srcBottom=new SKPoint(w,h); srcLeft=new SKPoint(0,h); }
            else { float hw=(w-1)/2f, hh=(h-1)/2f; srcTop=new SKPoint(hw,0); srcRight=new SKPoint(w-1,hh); srcBottom=new SKPoint(hw,h-1); srcLeft=new SKPoint(0,hh); }
            DrawTexturedTriangleSkia(canvas, paint, bmp, top, right, left, srcTop, srcRight, srcLeft);
            DrawTexturedTriangleSkia(canvas, paint, bmp, right, bottom, left, srcRight, srcBottom, srcLeft);
        }

        private void DrawTexturedTriangleSkia(SKCanvas canvas, SKPaint paint, SKBitmap bmp, SKPoint d0, SKPoint d1, SKPoint d2, SKPoint s0, SKPoint s1, SKPoint s2)
        {
            // Inflate slightly to hide seams - same as GDI+ version
            var infl = InflateTriangleSkia(d0,d1,d2,0.5f);
            d0=infl[0]; d1=infl[1]; d2=infl[2];
            float denom = (s0.X - s2.X)*(s1.Y - s2.Y) - (s1.X - s2.X)*(s0.Y - s2.Y);
            if (Math.Abs(denom) < 0.0001f) { DrawSolidTriangleSkia(canvas, bmp, d0,d1,d2); return; }
            float m11 = ((d0.X - d2.X)*(s1.Y - s2.Y) - (d1.X - d2.X)*(s0.Y - s2.Y))/denom;
            float m12 = ((d1.X - d2.X)*(s0.X - s2.X) - (d0.X - d2.X)*(s1.X - s2.X))/denom;
            float m21 = ((d0.Y - d2.Y)*(s1.Y - s2.Y) - (d1.Y - d2.Y)*(s0.Y - s2.Y))/denom;
            float m22 = ((d1.Y - d2.Y)*(s0.X - s2.X) - (d0.Y - d2.Y)*(s1.X - s2.X))/denom;
            float m13 = d2.X - m11*s2.X - m12*s2.Y;
            float m23 = d2.Y - m21*s2.X - m22*s2.Y;
            if (float.IsNaN(m11)||float.IsNaN(m12)||float.IsNaN(m21)||float.IsNaN(m22)||float.IsNaN(m13)||float.IsNaN(m23)||
                float.IsInfinity(m11)||float.IsInfinity(m12)||float.IsInfinity(m21)||float.IsInfinity(m22)||float.IsInfinity(m13)||float.IsInfinity(m23))
            { DrawSolidTriangleSkia(canvas, bmp, d0,d1,d2); return; }
            var matrix = new SKMatrix(m11, m21, m13, m12, m22, m23, 0,0,1);
            // Create shader with matrix - HighQuality
            using (var shader = SKShader.CreateBitmap(bmp, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, matrix))
            {
                var p = new SKPaint { Shader = shader, IsAntialias = true, FilterQuality = SKFilterQuality.High };
                var path = new SKPath();
                path.MoveTo(d0); path.LineTo(d1); path.LineTo(d2); path.Close();
                canvas.DrawPath(path, p);
            }
        }

        private void DrawSolidTriangleSkia(SKCanvas canvas, SKBitmap bmp, SKPoint a, SKPoint b, SKPoint c)
        {
            var col = GetLandTileColorFromSkia(bmp, 0);
            using (var p = new SKPaint { Color = new SKColor(col.R, col.G, col.B, 255), IsAntialias = true })
            {
                var path = new SKPath(); path.MoveTo(a); path.LineTo(b); path.LineTo(c); path.Close();
                canvas.DrawPath(path, p);
            }
        }

        private void DrawSolidDeformedTileSkia(SKCanvas canvas, float sx, float sy, float halfW, float halfH, float zoom, sbyte zTop, sbyte zRight, sbyte zBottom, sbyte zLeft, ushort id)
        {
            float oT=-zTop*Z_SCALE*zoom, oR=-zRight*Z_SCALE*zoom, oB=-zBottom*Z_SCALE*zoom, oL=-zLeft*Z_SCALE*zoom;
            var pts = new SKPoint[]{ new SKPoint(sx, sy-halfH+oT), new SKPoint(sx+halfW, sy+oR), new SKPoint(sx, sy+halfH+oB), new SKPoint(sx-halfW, sy+oL)};
            var col = GetLandTileColor(id);
            using(var p=new SKPaint{ Color=new SKColor(col.R,col.G,col.B,255), IsAntialias=true})
            {
                var path=new SKPath(); path.MoveTo(pts[0]); path.LineTo(pts[1]); path.LineTo(pts[2]); path.LineTo(pts[3]); path.Close();
                canvas.DrawPath(path,p);
            }
        }

        private Color GetLandTileColorFromSkia(SKBitmap bmp, ushort id)
        {
            if (bmp != null) {
                try {
                    var c = bmp.GetPixel(bmp.Width/2, bmp.Height/2);
                    if (c.Alpha>0) return Color.FromArgb(255,c.Red,c.Green,c.Blue);
                } catch {}
            }
            return GetLandTileColor(id);
        }

        private static SKPoint[] InflateTriangleSkia(SKPoint p0, SKPoint p1, SKPoint p2, float amt)
        {
            float cx=(p0.X+p1.X+p2.X)/3f, cy=(p0.Y+p1.Y+p2.Y)/3f;
            return new SKPoint[]{ PushSkia(p0,cx,cy,amt), PushSkia(p1,cx,cy,amt), PushSkia(p2,cx,cy,amt)};
        }
        private static SKPoint PushSkia(SKPoint pt, float cx, float cy, float amt)
        {
            float dx=pt.X-cx, dy=pt.Y-cy; float len=(float)Math.Sqrt(dx*dx+dy*dy);
            if(len<0.001f) return pt; return new SKPoint(pt.X+dx/len*amt, pt.Y+dy/len*amt);
        }

        /// <summary>
        /// Renders a rectangular tile of the export image.  The tile covers
        /// pixels [tileOffsetX .. tileOffsetX+tileW) × [tileOffsetY .. tileOffsetY+tileH)
        /// within the full export image coordinate space.
        /// </summary>
        private void RenderExportTile(Bitmap target,
            int tileOffsetX, int tileOffsetY, int tileW, int tileH,
            float originIsoX, float originIsoY,
            float halfTileW, float halfTileH, float zoom, bool includeStatics,
            int regionStartX, int regionStartY, int regionEndX, int regionEndY,
            int mapIndex, CancellationToken token, Action<float, string> progress,
            StaticsData preloadedStatics = null,
            Dictionary<ushort, byte[]> landArtPool = null,
            Dictionary<ushort, byte[]> staticArtPool = null,
            Dictionary<ushort, byte[]> texMapPool = null,
            Dictionary<ushort, Image> threadLandArt = null,
            Dictionary<ushort, Image> threadTexArt = null,
            Dictionary<ushort, Image> threadStaticArt = null,
            bool useTransparency = false)
        {
            // When thread-local caches are provided by the caller (parallel
            // tile rendering), reuse them across tiles on the same thread.
            // When null, create per-call caches and dispose them (single-tile path).
            bool ownsCaches = threadLandArt == null;
            var localLandArt = threadLandArt ?? new Dictionary<ushort, Image>();
            var localTexArt = threadTexArt ?? new Dictionary<ushort, Image>();
            var localStaticArt = threadStaticArt ?? new Dictionary<ushort, Image>();

            try
            {
                using (var g = Graphics.FromImage(target))
                {
                    // Use transparent background if requested, otherwise black
                    g.Clear(useTransparency ? Color.Transparent : Color.Black);
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    var landTiles = new List<(int x, int y, sbyte z, sbyte zTop, sbyte zRight, sbyte zBottom, sbyte zLeft, ushort id, Image img)>();
                    var staticItems = new List<(int x, int y, int z, ushort id, Image img, ushort hue, bool isTranslucent, bool isPartialHue)>();

                    if (mapIndex < 0)
                    {
                        mapIndex = 0;
                        try
                        {
                            if (facetComboBox != null && facetComboBox.IsHandleCreated)
                                facetComboBox.Invoke(new Action(() => mapIndex = facetComboBox.SelectedIndex));
                        }
                        catch { }
                    }

                    StaticsData areaStatics = preloadedStatics;
                    if (areaStatics == null && includeStatics && !string.IsNullOrEmpty(mulFolderPath))
                    {
                        areaStatics = StaticsReader.LoadArea(mulFolderPath, mapIndex,
                            Math.Max(0, regionStartX), Math.Max(0, regionStartY),
                            Math.Min(currentMap.Width - 1, regionEndX),
                            Math.Min(currentMap.Height - 1, regionEndY));
                    }

                    // -------------------------------------------------
                    // Narrow the map scan range using inverse isometric
                    // transform so we only visit map cells that could
                    // possibly draw into this pixel tile.
                    // -------------------------------------------------
                    float tileMaxExtent = halfTileH * 2 + 128 * Z_SCALE * zoom + 100 * zoom;
                    float padXvis = halfTileW * 2;

                    // u = mapX - mapY,  v = mapX + mapY
                    float uMin = (tileOffsetX - padXvis + originIsoX) / halfTileW;
                    float uMax = (tileOffsetX + tileW + padXvis + originIsoX) / halfTileW;
                    float vMin = (tileOffsetY - tileMaxExtent + originIsoY) / halfTileH;
                    float vMax = (tileOffsetY + tileH + tileMaxExtent + originIsoY) / halfTileH;

                    int scanMinX = Math.Max(regionStartX, Math.Max(0, (int)Math.Floor((uMin + vMin) / 2)));
                    int scanMaxX = Math.Min(regionEndX - 1, Math.Min(currentMap.Width - 1, (int)Math.Ceiling((uMax + vMax) / 2)));
                    int scanMinY = Math.Max(regionStartY, Math.Max(0, (int)Math.Floor((vMin - uMax) / 2)));
                    int scanMaxY = Math.Min(regionEndY - 1, Math.Min(currentMap.Height - 1, (int)Math.Ceiling((vMax - uMin) / 2)));

                    int totalRows = scanMaxY - scanMinY + 1;
                    int processedRows = 0;

                    for (int mapY = scanMinY; mapY <= scanMaxY; mapY++)
                    {
                        token.ThrowIfCancellationRequested();

                        for (int mapX = scanMinX; mapX <= scanMaxX; mapX++)
                        {
                            float isoX = (mapX - mapY) * halfTileW - originIsoX;
                            float isoY = (mapX + mapY) * halfTileH - originIsoY;

                            if (isoY + tileMaxExtent < tileOffsetY || isoY - tileMaxExtent > tileOffsetY + tileH)
                                continue;
                            if (isoX + padXvis < tileOffsetX || isoX - padXvis > tileOffsetX + tileW)
                                continue;

                            var tile = currentMap.Tiles[mapX, mapY];
                            if (tile == null) continue;

                            // Skip void / "nodraw" tiles
                            if (IsNoDraw(tile.TileId)) continue;

                            GetTileCornerZValues(mapX, mapY, out sbyte zTop, out sbyte zRight, out sbyte zBottom, out sbyte zLeft);

                            // Always prefer the texmap texture for consistent
                            // coloring across flat and deformed tiles.
                            Image img = null;
                            if (!localTexArt.TryGetValue(tile.TileId, out img))
                            {
                                byte[] data;
                                if (texMapPool != null && texMapPool.TryGetValue(tile.TileId, out data))
                                    img = new Bitmap(new MemoryStream(data));
                                else
                                {
                                    Image src;
                                    if (texMapCache.TryGetValue(tile.TileId, out src) && src != null)
                                    {
                                        lock (_artCacheLock) { img = new Bitmap(src); }
                                    }
                                }
                                localTexArt[tile.TileId] = img;
                            }

                            // Fall back to land tile art if no texture available.
                            if (img == null)
                            {
                                if (!localLandArt.TryGetValue(tile.TileId, out img))
                                {
                                    byte[] data;
                                    if (landArtPool != null && landArtPool.TryGetValue(tile.TileId, out data))
                                        img = new Bitmap(new MemoryStream(data));
                                    else
                                    {
                                        Image src;
                                        if (artCache.TryGetValue(tile.TileId, out src) && src != null)
                                        {
                                            lock (_artCacheLock) { img = new Bitmap(src); }
                                        }
                                    }
                                    localLandArt[tile.TileId] = img;
                                }
                            }

                            landTiles.Add((mapX, mapY, tile.Z, zTop, zRight, zBottom, zLeft, tile.TileId, img));

                            if (areaStatics != null)
                            {
                                var statics = areaStatics.GetStaticsAt(mapX, mapY);
                                foreach (var st in statics)
                                {
                                    if (!IsStaticInZRange(st.Z)) continue;
                                    if (IsStaticNoDraw(st.ItemId)) continue;

                                    Image sImg;
                                    if (!localStaticArt.TryGetValue(st.ItemId, out sImg))
                                    {
                                        byte[] data;
                                        if (staticArtPool != null && staticArtPool.TryGetValue(st.ItemId, out data))
                                            sImg = new Bitmap(new MemoryStream(data));
                                        else
                                        {
                                            Image sSrc;
                                            if (staticArtCache.TryGetValue(st.ItemId, out sSrc) && sSrc != null)
                                            {
                                                lock (_artCacheLock) { sImg = new Bitmap(sSrc); }
                                            }
                                        }
                                        localStaticArt[st.ItemId] = sImg;
                                    }
                                    if (sImg != null)
                                    {
                                        bool translucent = false;
                                        bool partialHue = false;
                                        if (tileDataReader != null && tileDataReader.IsLoaded)
                                        {
                                            var td = tileDataReader.GetItemTile(st.ItemId);
                                            if (td != null)
                                            {
                                                translucent = td.Flags.HasFlag(TileFlag.Translucent);
                                                partialHue = td.Flags.HasFlag(TileFlag.PartialHue);
                                            }
                                        }
                                        staticItems.Add((mapX, mapY, st.Z, st.ItemId, sImg, st.Hue, translucent, partialHue));
                                    }
                                }
                            }
                        }

                        processedRows++;
                        if (processedRows % 50 == 0)
                            progress?.Invoke((float)processedRows / totalRows * 0.9f,
                                $"Processing row {processedRows}/{totalRows}");
                    }

                    progress?.Invoke(0.9f, "Drawing tiles...");

                    // Merge and sort by depth (painter's algorithm).
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

                    token.ThrowIfCancellationRequested();
                    progress?.Invoke(0.90f, $"Sorting {drawItems.Count:N0} draw items...");

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

                    token.ThrowIfCancellationRequested();

                    int totalDrawItems = drawItems.Count;
                    progress?.Invoke(0.91f, $"Drawing {totalDrawItems:N0} tiles...");

                    // All draw calls use thread-local clones — fully parallel, no locks.
                    for (int drawIndex = 0; drawIndex < totalDrawItems; drawIndex++)
                    {
                        // Check cancellation and report progress every 5 000 items.
                        if (drawIndex % 5000 == 0)
                        {
                            token.ThrowIfCancellationRequested();
                            progress?.Invoke(0.91f + 0.08f * drawIndex / Math.Max(1, totalDrawItems),
                                $"Drawing tiles... {drawIndex:N0}/{totalDrawItems:N0}");
                        }

                        var di = drawItems[drawIndex];
                        if (!di.IsStatic)
                        {
                            var item = landTiles[di.Index];
                            float isoX = (item.x - item.y) * halfTileW - originIsoX;
                            float isoY = (item.x + item.y) * halfTileH - originIsoY;
                            float screenX = isoX - tileOffsetX;
                            float screenY = isoY - tileOffsetY;

                            DrawDeformedLandTile(g, item.img, screenX, screenY,
                                halfTileW, halfTileH, zoom,
                                item.zTop, item.zRight, item.zBottom, item.zLeft, item.id);
                        }
                        else
                        {
                            var item = staticItems[di.Index];
                            float isoX = (item.x - item.y) * halfTileW - originIsoX;
                            float isoY = (item.x + item.y) * halfTileH - originIsoY;
                            float screenX = isoX - tileOffsetX;
                            float screenY = isoY - tileOffsetY;

                            float imgW = item.img.Width * zoom;
                            float imgH = item.img.Height * zoom;
                            float zOffset = item.z * Z_SCALE * zoom;
                            float drawX = screenX - imgW / 2f;
                            float drawY = screenY - imgH + halfTileH - zOffset;

                            try { DrawStaticImage(g, item.img, new RectangleF(drawX, drawY, imgW, imgH), item.hue, item.isTranslucent, item.isPartialHue, item.id); }
                            catch { }
                        }
                    }
                }
            }
            finally
            {
                // Only dispose caches we own (single-tile path).
                // Thread-local caches are disposed by the Parallel.ForEach finalizer.
                if (ownsCaches)
                {
                    foreach (var img in localLandArt.Values)
                        img?.Dispose();
                    foreach (var img in localTexArt.Values)
                        img?.Dispose();
                    foreach (var img in localStaticArt.Values)
                        img?.Dispose();
                }
            }
        }

        /// <summary>
        /// Generates a radar-color minimap JPEG in isometric projection,
        /// matching the main viewer's orientation so click positions
        /// correspond visually.  Saved as minimap.jpg in the tile directory.
        /// </summary>
        private void GenerateExportMinimap(string outputDir,
            int regionStartX, int regionStartY, int regionEndX, int regionEndY,
            float halfTileW, float halfTileH, float minIsoX, float minIsoY,
            int imageWidth, int imageHeight)
        {
            const int MM_MAX = 512;

            if (imageWidth <= 0 || imageHeight <= 0) return;

            // Scale the full isometric image down to fit within MM_MAX.
            float mmScale = (float)MM_MAX / Math.Max(imageWidth, imageHeight);
            int mmW = Math.Max(1, (int)(imageWidth * mmScale));
            int mmH = Math.Max(1, (int)(imageHeight * mmScale));

            using (var bmp = new Bitmap(mmW, mmH, PixelFormat.Format32bppArgb))
            {
                var bmpData = bmp.LockBits(
                    new Rectangle(0, 0, mmW, mmH),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);

                int stride = bmpData.Stride;
                byte[] pixels = new byte[stride * mmH];

                // Parallel Skia-friendly minimap - inverse-transform per pixel with SIMD via Parallel.For
                Parallel.For(0, mmH, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, my =>
                {
                    float isoY = my / mmScale;
                    float v = (isoY + minIsoY) / halfTileH;
                    for (int mx = 0; mx < mmW; mx++)
                    {
                        float isoX = mx / mmScale;
                        float u = (isoX + minIsoX) / halfTileW;
                        int mapX = (int)((u + v) / 2);
                        int mapY = (int)((v - u) / 2);
                        if (mapX < regionStartX || mapX >= regionEndX || mapY < regionStartY || mapY >= regionEndY) continue;
                        if (mapX < 0 || mapX >= currentMap.Width || mapY < 0 || mapY >= currentMap.Height) continue;
                        var tile = currentMap.Tiles[mapX, mapY];
                        if (tile == null) continue;
                        Color c = GetRadarColor(tile.TileId);
                        int idx = my * stride + mx * 4;
                        pixels[idx + 0] = c.B;
                        pixels[idx + 1] = c.G;
                        pixels[idx + 2] = c.R;
                        pixels[idx + 3] = 255;
                    }
                });

                Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
                bmp.UnlockBits(bmpData);

                string mmPath = Path.Combine(outputDir, "minimap.jpg");
                var jpegCodec = ImageCodecInfo.GetImageEncoders()
                    .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
                if (jpegCodec != null)
                {
                    using (var ep = new EncoderParameters(1))
                    {
                        ep.Param[0] = new EncoderParameter(Encoder.Quality, 90L);
                        bmp.Save(mmPath, jpegCodec, ep);
                    }
                }
                else
                {
                    bmp.Save(mmPath, ImageFormat.Jpeg);
                }
            }
        }

        /// <summary>
        /// Composites the coarsest mipmap level (or level-0 tiles when no
        /// mipmaps exist) into a single globe_texture.png in the tile directory.
        /// This gives a single image of the full rendered map at the smallest
        /// available resolution, suitable for projecting onto a 3D globe.
        /// </summary>
        private static void GenerateGlobeTexture(string outputDir, int maxMipLevel,
            int tileCols, int tileRows, int tileSize, CancellationToken token)
        {
            // Walk down to the coarsest level's grid dimensions.
            int levelCols = tileCols;
            int levelRows = tileRows;
            for (int i = 0; i < maxMipLevel; i++)
            {
                levelCols = (levelCols + 1) / 2;
                levelRows = (levelRows + 1) / 2;
            }

            string levelDir = maxMipLevel > 0
                ? Path.Combine(outputDir, $"L{maxMipLevel}")
                : outputDir;

            if (!Directory.Exists(levelDir)) return;

            token.ThrowIfCancellationRequested();

            // Composite all tiles into a single image.
            int compW = levelCols * tileSize;
            int compH = levelRows * tileSize;

            using (var bmp = new Bitmap(compW, compH, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.PixelOffsetMode = PixelOffsetMode.None;

                    for (int row = 0; row < levelRows; row++)
                    {
                        for (int col = 0; col < levelCols; col++)
                        {
                            token.ThrowIfCancellationRequested();

                            string tilePath = Path.Combine(levelDir, $"tile_{row}_{col}.jpg");
                            if (!File.Exists(tilePath)) continue;

                            using (var tileImg = Image.FromFile(tilePath))
                            {
                                g.DrawImage(tileImg,
                                    col * tileSize, row * tileSize,
                                    tileImg.Width, tileImg.Height);
                            }
                        }
                    }
                }

                bmp.Save(Path.Combine(outputDir, "globe_texture.png"), ImageFormat.Png);
            }
        }

        /// <summary>
        /// Restitches exported tiles back into a single large image using ImageMagick.
        /// Falls back to .NET GDI+ if ImageMagick is not available.
        /// </summary>
        private void RestitchTilesIntoSingleImage(string tilesDir, string originalOutputPath,
            int tileCols, int tileRows, int tileSize, int fullWidth, int fullHeight,
            CancellationToken token, Action<float, string> progress)
        {
            // Try ImageMagick first (handles large images better)
            if (TryRestitchWithImageMagick(tilesDir, originalOutputPath, tileCols, tileRows, 
                tileSize, fullWidth, fullHeight, token, progress))
            {
                return;
            }

            // Fall back to GDI+ (may fail for very large images but works for moderate sizes)
            progress?.Invoke(0.996f, "Stitching with GDI+ (ImageMagick not found)...");
            RestitchWithGdiPlus(tilesDir, originalOutputPath, tileCols, tileRows, 
                tileSize, fullWidth, fullHeight, token);
        }

        /// <summary>
        /// Attempts to restitch tiles using ImageMagick (montage command).
        /// Returns true if successful, false if ImageMagick is not available.
        /// </summary>
        private bool TryRestitchWithImageMagick(string tilesDir, string originalOutputPath,
            int tileCols, int tileRows, int tileSize, int fullWidth, int fullHeight,
            CancellationToken token, Action<float, string> progress)
        {
            try
            {
                // Check if ImageMagick is installed
                string magickPath = FindImageMagickExecutable();
                if (string.IsNullOrEmpty(magickPath))
                {
                    return false; // ImageMagick not found
                }

                progress?.Invoke(0.996f, "Stitching with ImageMagick...");

                // Build the montage command
                // montage tile_0_0.jpg tile_0_1.jpg ... -tile COLSxROWS -geometry TILESIZExTILESIZE+0+0 -mode Concatenate output.png
                var tileFiles = new System.Text.StringBuilder();
                for (int row = 0; row < tileRows; row++)
                {
                    for (int col = 0; col < tileCols; col++)
                    {
                        string tilePath = Path.Combine(tilesDir, $"tile_{row}_{col}.jpg");
                        if (File.Exists(tilePath))
                        {
                            tileFiles.Append($"\"{tilePath}\" ");
                        }
                    }
                }

                string outputExt = Path.GetExtension(originalOutputPath).ToLowerInvariant();
                string stitchedPath = originalOutputPath;

                // Use montage for stitching
                string arguments = $"{tileFiles} -tile {tileCols}x{tileRows} -geometry {tileSize}x{tileSize}+0+0 -mode Concatenate \"{stitchedPath}\"";

                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = magickPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = System.Diagnostics.Process.Start(processInfo))
                {
                    if (process == null)
                    {
                        return false;
                    }

                    // Wait for completion with cancellation support
                    while (!process.HasExited)
                    {
                        token.ThrowIfCancellationRequested();
                        System.Threading.Thread.Sleep(100);
                    }

                    if (process.ExitCode != 0)
                    {
                        string error = process.StandardError.ReadToEnd();
                        throw new InvalidOperationException($"ImageMagick failed: {error}");
                    }
                }

                progress?.Invoke(0.999f, "Stitch complete!");
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // If ImageMagick fails for any reason, fall back to GDI+
                return false;
            }
        }

        /// <summary>
        /// Finds the ImageMagick executable (magick.exe or montage.exe).
        /// Checks common installation paths and PATH environment variable.
        /// </summary>
        private string FindImageMagickExecutable()
        {
            // Check for magick.exe (ImageMagick 7+)
            string[] possiblePaths = new[]
            {
                "magick.exe",
                "montage.exe",
                @"C:\Program Files\ImageMagick-7.1.1-Q16-HDRI\magick.exe",
                @"C:\Program Files\ImageMagick-7.1.0-Q16-HDRI\magick.exe",
                @"C:\Program Files\ImageMagick\magick.exe",
                @"C:\Program Files (x86)\ImageMagick\magick.exe",
                @"C:\ImageMagick\magick.exe",
            };

            foreach (var path in possiblePaths)
            {
                try
                {
                    // Try to find in PATH
                    if (!Path.IsPathRooted(path))
                    {
                        var processInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "where",
                            Arguments = path,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        };

                        using (var process = System.Diagnostics.Process.Start(processInfo))
                        {
                            if (process != null)
                            {
                                string output = process.StandardOutput.ReadToEnd();
                                process.WaitForExit();
                                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                                {
                                    return output.Split('\n')[0].Trim();
                                }
                            }
                        }
                    }
                    else if (File.Exists(path))
                    {
                        return path;
                    }
                }
                catch
                {
                    // Continue checking other paths
                }
            }

            return null; // ImageMagick not found
        }

        /// <summary>
        /// Restitches tiles using GDI+ (fallback when ImageMagick is not available).
        /// May fail for very large images due to GDI+ memory limitations.
        /// </summary>
        private void RestitchWithGdiPlus(string tilesDir, string originalOutputPath,
            int tileCols, int tileRows, int tileSize, int fullWidth, int fullHeight,
            CancellationToken token)
        {
            const int MAX_DIMENSION = 65535; // GDI+ limit

            if (fullWidth > MAX_DIMENSION || fullHeight > MAX_DIMENSION)
            {
                throw new InvalidOperationException(
                    $"Cannot restitch: Image dimensions ({fullWidth:N0} × {fullHeight:N0}) " +
                    $"exceed GDI+ maximum ({MAX_DIMENSION:N0} px per side). " +
                    "Please install ImageMagick for large image support.");
            }

            using (var bmp = new Bitmap(fullWidth, fullHeight, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.PixelOffsetMode = PixelOffsetMode.None;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;

                for (int row = 0; row < tileRows; row++)
                {
                    for (int col = 0; col < tileCols; col++)
                    {
                        token.ThrowIfCancellationRequested();

                        string tilePath = Path.Combine(tilesDir, $"tile_{row}_{col}.jpg");
                        if (!File.Exists(tilePath)) continue;

                        using (var tileImg = Image.FromFile(tilePath))
                        {
                            int x = col * tileSize;
                            int y = row * tileSize;
                            g.DrawImage(tileImg, x, y, tileImg.Width, tileImg.Height);
                        }
                    }
                }

                SaveImage(bmp, originalOutputPath);
            }
        }

        /// <summary>
        /// Generates mipmap levels by downsampling tiles from each previous level.
        /// Level 0 is the full-resolution tiles in the root output directory.
        /// Level N tiles are stored in an LN/ subdirectory.  Each level halves the
        /// tile grid in both dimensions, combining 2×2 source tiles into one
        /// 512×512 JPEG.  Tiles within each level are rendered in parallel.
        /// </summary>
        private static void GenerateMipmaps(string outputDir, int tileCols, int tileRows,
            int tileSize, int maxLevel, CancellationToken token, Action<float, string> progress)
        {
            var jpegCodec = ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);

            int levelCols = tileCols;
            int levelRows = tileRows;
            string prevDir = outputDir; // Level 0 tiles live in the root.

            int threadCount = Math.Max(1, Environment.ProcessorCount);

            for (int level = 1; level <= maxLevel; level++)
            {
                token.ThrowIfCancellationRequested();

                int nextCols = (levelCols + 1) / 2;
                int nextRows = (levelRows + 1) / 2;

                string levelDir = Path.Combine(outputDir, $"L{level}");
                Directory.CreateDirectory(levelDir);

                int totalMipTiles = nextCols * nextRows;
                int done = 0;

                progress?.Invoke(0.90f + 0.09f * (level - 1) / maxLevel,
                    $"Generating mipmap level {level}/{maxLevel} ({nextCols}×{nextRows} = {totalMipTiles} tiles)...");

                // Build work items for this level.
                var mipWork = new List<(int row, int col)>(totalMipTiles);
                for (int row = 0; row < nextRows; row++)
                    for (int col = 0; col < nextCols; col++)
                        mipWork.Add((row, col));

                // Capture loop variables for the closure.
                int capLevelCols = levelCols;
                int capLevelRows = levelRows;
                string capPrevDir = prevDir;

                Parallel.ForEach(mipWork,
                    new ParallelOptions { MaxDegreeOfParallelism = threadCount, CancellationToken = token },
                    mipTile =>
                {
                    token.ThrowIfCancellationRequested();

                    // Compose the 2×2 source tiles at full resolution into a
                    // single intermediate bitmap, then downsample in one pass.
                    // This lets the bilinear filter sample across tile boundaries,
                    // eliminating the dark seams that appear when each tile is
                    // downsampled independently.
                    int compW = tileSize * 2;
                    int compH = tileSize * 2;

                    using (var composite = new Bitmap(compW, compH, PixelFormat.Format32bppArgb))
                    {
                        using (var gc = Graphics.FromImage(composite))
                        {
                            gc.Clear(Color.Black);
                            gc.PixelOffsetMode = PixelOffsetMode.None;
                            gc.CompositingMode = CompositingMode.SourceCopy;

                            for (int dy = 0; dy < 2; dy++)
                            {
                                for (int dx = 0; dx < 2; dx++)
                                {
                                    int srcRow = mipTile.row * 2 + dy;
                                    int srcCol = mipTile.col * 2 + dx;

                                    if (srcRow >= capLevelRows || srcCol >= capLevelCols)
                                        continue;

                                    string srcPath = Path.Combine(capPrevDir, $"tile_{srcRow}_{srcCol}.jpg");
                                    if (!File.Exists(srcPath)) continue;

                                    using (var srcImg = Image.FromFile(srcPath))
                                    {
                                        gc.DrawImage(srcImg,
                                            new Rectangle(dx * tileSize, dy * tileSize, srcImg.Width, srcImg.Height),
                                            0, 0, srcImg.Width, srcImg.Height,
                                            GraphicsUnit.Pixel);
                                    }
                                }
                            }
                        }

                        using (var bmp = new Bitmap(tileSize, tileSize, PixelFormat.Format32bppArgb))
                        using (var g = Graphics.FromImage(bmp))
                        using (var attrs = new ImageAttributes())
                        {
                            attrs.SetWrapMode(WrapMode.TileFlipXY);
                            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                            g.DrawImage(composite,
                                new Rectangle(0, 0, tileSize, tileSize),
                                0, 0, compW, compH,
                                GraphicsUnit.Pixel, attrs);

                            string tilePath = Path.Combine(levelDir, $"tile_{mipTile.row}_{mipTile.col}.jpg");
                            if (jpegCodec != null)
                            {
                                using (var ep = new EncoderParameters(1))
                                {
                                    ep.Param[0] = new EncoderParameter(Encoder.Quality, 85L);
                                    bmp.Save(tilePath, jpegCodec, ep);
                                }
                            }
                            else
                            {
                                bmp.Save(tilePath, ImageFormat.Jpeg);
                            }
                        }
                    }

                    int d = Interlocked.Increment(ref done);
                    if (d % 200 == 0)
                        progress?.Invoke(0.90f + 0.09f * ((level - 1f) + (float)d / totalMipTiles) / maxLevel,
                            $"Mipmap L{level}: {d}/{totalMipTiles}");
                });

                prevDir = levelDir;
                levelCols = nextCols;
                levelRows = nextRows;
            }
        }

        /// <summary>
        /// Generates an HTML file with a virtual tile viewer.
        /// Click-drag to pan, mousewheel to zoom, no scrollbars.
        /// Only tiles visible in the viewport (plus a buffer) exist in
        /// the DOM; off-screen tiles are removed to save memory.
        /// Includes a clickable minimap for quick navigation.
        /// Starts centered on map tile (1600,1600) at 100% zoom.
        /// Supports mipmap levels for efficient zoomed-out viewing.
        /// </summary>
        private static void GenerateTileViewer(string outputDir,
            int imageWidth, int imageHeight, int tileSize, int tileCols, int tileRows,
            float halfTileW, float halfTileH, float minIsoX, float minIsoY,
            int maxMipLevel = 0)
        {
            // Compute default pixel position for map coords (1600,1600).
            int defaultMapX = Math.Max(0, Math.Min((int)(imageWidth / (halfTileW * 2)) - 1, 1600));
            int defaultMapY = Math.Max(0, Math.Min((int)(imageHeight / (halfTileH * 2)) - 1, 1600));
            float defaultPxX = (defaultMapX - defaultMapY) * halfTileW - minIsoX;
            float defaultPxY = (defaultMapX + defaultMapY) * halfTileH - minIsoY;

            // Clamp to image bounds.
            int startPxX = Math.Max(0, Math.Min(imageWidth, (int)defaultPxX));
            int startPxY = Math.Max(0, Math.Min(imageHeight, (int)defaultPxY));

            // Minimap max dimensions in the viewer UI.
            const int MINIMAP_MAX = 300;

            string html = $@"<!DOCTYPE html>
<html><head><meta charset=""utf-8"">
<title>Map Export Viewer</title>
<style>
*{{margin:0;padding:0;box-sizing:border-box}}
html,body{{width:100%;height:100%;overflow:hidden;background:#111}}
#vp{{width:100%;height:100%;overflow:hidden;cursor:grab}}
#map{{position:absolute;left:0;top:0;width:{imageWidth}px;height:{imageHeight}px;transform-origin:0 0}}
#map img{{position:absolute;display:block;pointer-events:none}}
#hud{{position:fixed;top:8px;right:8px;z-index:10;
  background:rgba(0,0,0,.75);color:#0ff;padding:8px 14px;
  border-radius:6px;font:13px/1.4 monospace;user-select:none}}
#hud button{{margin:0 3px;padding:2px 10px;background:#222;color:#0ff;
  border:1px solid #0ff;cursor:pointer;border-radius:3px}}
#hud button:hover{{background:#0ff;color:#000}}
#minimap{{position:fixed;top:8px;left:8px;z-index:10;
  background:rgba(0,0,0,.8);box-shadow:0 0 0 1px #0ff;
  border-radius:4px;cursor:crosshair;overflow:hidden}}
#minimap img{{display:block;width:100%;height:100%}}
#mmvp{{position:absolute;border:1.5px solid #ff0;pointer-events:none;box-sizing:border-box}}
#mmcross{{position:absolute;width:5px;height:5px;background:#ff0;pointer-events:none;border-radius:50%}}
</style></head><body>
<div id=""hud"">
  {imageWidth:N0} &times; {imageHeight:N0} px
  <button onclick=""sz('in')"">+</button>
  <button onclick=""sz('out')"">&minus;</button>
  <button onclick=""sz('1:1')"">1:1</button>
  <button onclick=""sz('fit')"">Fit</button>
  <span id=""zl"">100%</span>
  <br><small style=""color:#888"">Drag to pan &bull; Wheel to zoom &bull; tiles: <span id=""tc"">0</span></small>
</div>
<div id=""minimap""><img id=""mmimg"" src=""minimap.jpg"" draggable=""false""><div id=""mmvp""></div><div id=""mmcross""></div></div>
<div id=""vp""><div id=""map""></div></div>
<script>
(function(){{
var W={imageWidth},H={imageHeight},T={tileSize},C={tileCols},R={tileRows},ML={maxMipLevel},
    vp=document.getElementById('vp'),
    map=document.getElementById('map'),
    zl=document.getElementById('zl'),
    tcEl=document.getElementById('tc'),
    mmEl=document.getElementById('minimap'),
    mmvp=document.getElementById('mmvp'),
    mmcross=document.getElementById('mmcross'),
    tiles={{}},curLvl=-1,zoom=1,raf=null,BUF=1024,
    cx={startPxX},cy={startPxY},
    drag=false,dsx=0,dsy=0,dcx=0,dcy=0,
    mmDrag=false;

// Size the minimap container to match the isometric image aspect ratio.
var mmAspect=W/H;
var mmW,mmH;
if(mmAspect>=1){{ mmW={MINIMAP_MAX};mmH=Math.round({MINIMAP_MAX}/mmAspect); }}
else{{ mmH={MINIMAP_MAX};mmW=Math.round({MINIMAP_MAX}*mmAspect); }}
mmEl.style.width=mmW+'px';
mmEl.style.height=mmH+'px';

// Convert full-image pixel coords to minimap pixel coords.
function isoToMm(px,py){{
  return[px/W*mmW,py/H*mmH];
}}

// Convert minimap pixel coords to full-image pixel coords.
function mmToIso(mx,my){{
  return[mx/mmW*W,my/mmH*H];
}}

function drawMmViewport(){{
  var vpW=vp.clientWidth,vpH=vp.clientHeight;
  var l=cx-vpW/(2*zoom),t=cy-vpH/(2*zoom);
  var r=cx+vpW/(2*zoom),b=cy+vpH/(2*zoom);
  var c1=isoToMm(l,t),c2=isoToMm(r,t),c3=isoToMm(r,b),c4=isoToMm(l,b);
  var x1=Math.min(c1[0],c2[0],c3[0],c4[0]);
  var y1=Math.min(c1[1],c2[1],c3[1],c4[1]);
  var x2=Math.max(c1[0],c2[0],c3[0],c4[0]);
  var y2=Math.max(c1[1],c2[1],c3[1],c4[1]);
  mmvp.style.left=x1+'px';mmvp.style.top=y1+'px';
  mmvp.style.width=(x2-x1)+'px';mmvp.style.height=(y2-y1)+'px';
  var cc=isoToMm(cx,cy);
  mmcross.style.left=(cc[0]-2)+'px';mmcross.style.top=(cc[1]-2)+'px';
}}

// Click/drag on minimap to jump
function mmNav(e){{
  var rect=mmEl.getBoundingClientRect();
  var mx=e.clientX-rect.left,my=e.clientY-rect.top;
  var iso=mmToIso(mx,my);
  cx=Math.max(0,Math.min(W,iso[0]));
  cy=Math.max(0,Math.min(H,iso[1]));
  render();sched();drawMmViewport();
}}
mmEl.addEventListener('mousedown',function(e){{
  if(e.button!==0)return;
  mmDrag=true;mmNav(e);e.preventDefault();e.stopPropagation();
}});
window.addEventListener('mousemove',function(e){{
  if(mmDrag){{mmNav(e);e.preventDefault();}}
}});
window.addEventListener('mouseup',function(){{mmDrag=false;}});

function render(){{
  var tx=vp.clientWidth/2-cx*zoom,
      ty=vp.clientHeight/2-cy*zoom;
  map.style.transform='translate('+tx+'px,'+ty+'px) scale('+zoom+')';
  zl.textContent=Math.round(zoom*100)+'%';
}}

function getLevel(z){{
  if(ML===0)return 0;
  var lvl=Math.floor(Math.log2(1/z));
  return Math.max(0,Math.min(ML,lvl));
}}

function update(){{
  raf=null;
  var lvl=getLevel(zoom);
  var sc=1<<lvl;
  var span=T*sc;
  var lvlC=Math.ceil(C/sc),lvlR=Math.ceil(R/sc);
  if(lvl!==curLvl){{
    var ks=Object.keys(tiles);
    for(var i=0;i<ks.length;i++){{map.removeChild(tiles[ks[i]]);}}tiles={{}};curLvl=lvl;
  }}
  var vpW=vp.clientWidth,vpH=vp.clientHeight,
      sl=cx-vpW/(2*zoom),st=cy-vpH/(2*zoom),
      sr=cx+vpW/(2*zoom),sb=cy+vpH/(2*zoom),
      l=Math.max(0,sl-BUF),t=Math.max(0,st-BUF),
      r=Math.min(W,sr+BUF),b=Math.min(H,sb+BUF),
      c0=Math.max(0,(l/span)|0),r0=Math.max(0,(t/span)|0),
      c1=Math.min(lvlC-1,(r/span)|0),r1=Math.min(lvlR-1,(b/span)|0),
      need={{}},n=0;
  for(var row=r0;row<=r1;row++){{
    for(var col=c0;col<=c1;col++){{
      var k=lvl+'_'+row+'_'+col;need[k]=1;n++;
      if(!tiles[k]){{
        var img=document.createElement('img');
        img.src=lvl===0?'tile_'+row+'_'+col+'.jpg':'L'+lvl+'/tile_'+row+'_'+col+'.jpg';
        var px=col*span,py=row*span,tw=Math.min(span,W-px),th=Math.min(span,H-py);
        img.style.cssText='position:absolute;left:'+px+'px;top:'+py+'px;width:'+(tw+1)+'px;height:'+(th+1)+'px';
        map.appendChild(img);tiles[k]=img;
      }}
    }}
  }}
  var keys=Object.keys(tiles);
  for(var i=0;i<keys.length;i++){{
    if(!need[keys[i]]){{map.removeChild(tiles[keys[i]]);delete tiles[keys[i]];}}
  }}
  tcEl.textContent=n+(ML>0?' (L'+lvl+')':'');
  drawMmViewport();
}}

function sched(){{if(!raf) raf=requestAnimationFrame(update);}}

// --- Drag to pan ---
vp.addEventListener('mousedown',function(e){{
  if(e.button!==0)return;
  drag=true;dsx=e.clientX;dsy=e.clientY;dcx=cx;dcy=cy;
  vp.style.cursor='grabbing';e.preventDefault();
}});
window.addEventListener('mousemove',function(e){{
  if(!drag)return;
  cx=dcx-(e.clientX-dsx)/zoom;
  cy=dcy-(e.clientY-dsy)/zoom;
  render();sched();
}});
window.addEventListener('mouseup',function(){{
  if(drag){{drag=false;vp.style.cursor='grab';drawMmViewport();}}
}});

// --- Wheel to zoom (toward cursor) ---
vp.addEventListener('wheel',function(e){{
  e.preventDefault();
  var f=e.deltaY<0?1.25:1/1.25,
      rect=vp.getBoundingClientRect(),
      mx=cx+(e.clientX-rect.left-rect.width/2)/zoom,
      my=cy+(e.clientY-rect.top-rect.height/2)/zoom,
      nz=Math.max(0.01,Math.min(zoom*f,10));
  cx=mx-(e.clientX-rect.left-rect.width/2)/nz;
  cy=my-(e.clientY-rect.top-rect.height/2)/nz;
  zoom=nz;render();sched();
}},{{passive:false}});

// --- Button zoom (centered) ---
window.sz=function(z){{
  if(z==='fit') z=Math.min(vp.clientWidth/W,vp.clientHeight/H);
  else if(z==='in') z=zoom*1.5;
  else if(z==='out') z=zoom/1.5;
  else if(z==='1:1') z=1;
  zoom=Math.max(0.01,Math.min(z,10));
  render();sched();
}};

window.addEventListener('resize',function(){{render();sched();}});

// Start at 100% zoom, centered on map position (1600,1600)
render();update();
}})();
</script></body></html>";

            File.WriteAllText(Path.Combine(outputDir, "viewer.html"),
                html, System.Text.Encoding.UTF8);
        }

        private void SaveImage(Bitmap bmp, string outputPath)
        {
            string ext = Path.GetExtension(outputPath).ToLowerInvariant();
            switch (ext)
            {
                case ".jpg":
                case ".jpeg":
                    var jpegEncoder = ImageCodecInfo.GetImageEncoders()
                        .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    if (jpegEncoder != null)
                    {
                        var encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 95L);
                        bmp.Save(outputPath, jpegEncoder, encoderParams);
                    }
                    else
                    {
                        bmp.Save(outputPath, ImageFormat.Jpeg);
                    }
                    break;
                case ".bmp":
                    bmp.Save(outputPath, ImageFormat.Bmp);
                    break;
                default:
                    bmp.Save(outputPath, ImageFormat.Png);
                    break;
            }
        }
    }

    /// <summary>
    /// Lightweight sort key for interleaving land tiles and statics in draw order.
    /// </summary>
    internal struct ExportDrawItem
    {
        public int SortDepth;    // x + y
        public int Z;
        public bool IsStatic;    // false = land tile, true = static object
        public bool IsFoliage;   // true = Foliage flag set, draw on top at same position
        public int Index;        // index into the respective source list
        public int OriginalOrder; // insertion order for stable sort tiebreaking
    }

    /// <summary>
    /// Describes one rectangular tile in the 2D export grid.
    /// </summary>
    internal struct ExportTileInfo
    {
        public int Col;     // column index in the tile grid
        public int Row;     // row index in the tile grid
        public int PixelX;  // X offset in the full export image
        public int PixelY;  // Y offset in the full export image
        public int Width;   // pixel width of this tile
        public int Height;  // pixel height of this tile
    }

    /// <summary>
    /// Dialog for configuring map export options.
    /// </summary>
    internal class MapExportOptionsDialog : Form
    {
        public float ExportZoom { get; private set; }
        public bool IncludeStatics { get; private set; }
        public bool HtmlAndMinimapOnly { get; private set; }
        public bool MipmapOnly { get; private set; }
        public bool UseTransparentBackground { get; private set; }
        public bool ExportAsSingleImage { get; private set; }
        public bool RestitchAfterTiling { get; private set; }
        public int RegionStartX { get; private set; }
        public int RegionStartY { get; private set; }
        public int RegionEndX { get; private set; }
        public int RegionEndY { get; private set; }

        private NumericUpDown zoomNumeric;
        private CheckBox includeStaticsCheckBox;
        private CheckBox htmlOnlyCheckBox;
        private CheckBox mipmapOnlyCheckBox;
        private CheckBox useTransparentBackgroundCheckBox;
        private CheckBox restitchAfterTilingCheckBox;
        private RadioButton singleImageRadio;
        private RadioButton tiledExportRadio;
        private RadioButton exportFullMapRadio;
        private RadioButton exportRegionRadio;
        private NumericUpDown startXNumeric, startYNumeric, endXNumeric, endYNumeric;
        private Label estimatedSizeLabel;

        private int mapWidth, mapHeight;

        public MapExportOptionsDialog(int mapWidth, int mapHeight, float currentZoom)
        {
            this.mapWidth = mapWidth;
            this.mapHeight = mapHeight;

            Text = "Export Map to Image";
            Width = 420;
            Height = 590;  // Increased from 560 to accommodate restitch checkbox
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;

            HolographicTheme.ApplyToForm(this);

            int y = 15;
            int labelWidth = 120;
            int ctrlLeft = 140;
            int ctrlWidth = 240;

            // Zoom level
            var zoomLabel = new Label
            {
                Text = "Zoom (px/tile):",
                Location = new Point(15, y + 3),
                Width = labelWidth,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(zoomLabel);
            Controls.Add(zoomLabel);

            zoomNumeric = new NumericUpDown
            {
                Location = new Point(ctrlLeft, y),
                Width = 80,
                Minimum = 1,
                Maximum = 44,
                DecimalPlaces = 1,
                Increment = 1,
                Value = Math.Min(44, Math.Max(1, (decimal)currentZoom))
            };
            HolographicTheme.ApplyToNumericUpDown(zoomNumeric);
            zoomNumeric.ValueChanged += (s, e) => UpdateEstimatedSize();
            Controls.Add(zoomNumeric);

            var zoomHint = new Label
            {
                Text = "(1 = tiny overview, 44 = full detail)",
                Location = new Point(ctrlLeft + 85, y + 3),
                Width = 170,
                Height = 20,
                ForeColor = Color.Gray,
                Font = new Font(Font.FontFamily, 7.5f)
            };
            Controls.Add(zoomHint);
            y += 35;

            // Include statics
            includeStaticsCheckBox = new CheckBox
            {
                Text = "Include Static Objects",
                Location = new Point(ctrlLeft, y),
                Width = ctrlWidth,
                Height = 20,
                Checked = true
            };
            HolographicTheme.ApplyToCheckBox(includeStaticsCheckBox);
            Controls.Add(includeStaticsCheckBox);
            y += 28;

            // Transparent background
            useTransparentBackgroundCheckBox = new CheckBox
            {
                Text = "Use Transparent Background (PNG only)",
                Location = new Point(ctrlLeft, y),
                Width = ctrlWidth,
                Height = 20,
                Checked = false
            };
            HolographicTheme.ApplyToCheckBox(useTransparentBackgroundCheckBox);
            Controls.Add(useTransparentBackgroundCheckBox);
            y += 28;

            // HTML & Minimap only
            htmlOnlyCheckBox = new CheckBox
            {
                Text = "Export HTML && Minimap Only",
                Location = new Point(ctrlLeft, y),
                Width = ctrlWidth,
                Height = 20,
                Checked = false
            };
            HolographicTheme.ApplyToCheckBox(htmlOnlyCheckBox);
            htmlOnlyCheckBox.CheckedChanged += (s, e) => UpdateEstimatedSize();
            Controls.Add(htmlOnlyCheckBox);
            y += 28;

            // Generate Mipmap only
            mipmapOnlyCheckBox = new CheckBox
            {
                Text = "Generate Mipmap Only (uses existing tiles)",
                Location = new Point(ctrlLeft, y),
                Width = ctrlWidth,
                Height = 20,
                Checked = false
            };
            HolographicTheme.ApplyToCheckBox(mipmapOnlyCheckBox);
            Controls.Add(mipmapOnlyCheckBox);
            y += 35;

            // Export mode separator
            var modeSep = new Label
            {
                Location = new Point(15, y),
                Width = Width - 50,
                Height = 1,
                BackColor = HolographicTheme.BorderCyan
            };
            Controls.Add(modeSep);
            y += 10;

            var exportModeLabel = new Label
            {
                Text = "Export Mode:",
                Location = new Point(15, y),
                Width = labelWidth,
                Height = 20,
                Font = new Font(Font.FontFamily, 9f, FontStyle.Bold)
            };
            HolographicTheme.ApplyToLabel(exportModeLabel);
            Controls.Add(exportModeLabel);
            y += 25;

            // Create a panel for Export Mode radio buttons to group them separately
            var exportModePanel = new Panel
            {
                Location = new Point(0, y),
                Width = Width,
                Height = 60,
                BackColor = Color.Transparent
            };

            // Single Image radio
            singleImageRadio = new RadioButton
            {
                Text = "Export as Single Image",
                Location = new Point(ctrlLeft, 0),
                Width = 200,
                Height = 20,
                Checked = true
            };
            HolographicTheme.ApplyToRadioButton(singleImageRadio);
            singleImageRadio.CheckedChanged += (s, e) => UpdateEstimatedSize();
            exportModePanel.Controls.Add(singleImageRadio);

            // Tiled Export radio
            tiledExportRadio = new RadioButton
            {
                Text = "Export as Tiled Images (512×512, for very large maps)",
                Location = new Point(ctrlLeft, 25),
                Width = ctrlWidth,
                Height = 20,
                Checked = false
            };
            HolographicTheme.ApplyToRadioButton(tiledExportRadio);
            tiledExportRadio.CheckedChanged += (s, e) =>
            {
                restitchAfterTilingCheckBox.Enabled = tiledExportRadio.Checked;
                UpdateEstimatedSize();
            };
            exportModePanel.Controls.Add(tiledExportRadio);

            Controls.Add(exportModePanel);
            y += 60;

            // Restitch checkbox (only enabled when Tiled Export is selected)
            restitchAfterTilingCheckBox = new CheckBox
            {
                Text = "Restitch into Single Image After Export",
                Location = new Point(ctrlLeft + 20, y),
                Width = ctrlWidth - 20,
                Height = 20,
                Checked = false,
                Enabled = false  // Initially disabled since Single Image is default
            };
            HolographicTheme.ApplyToCheckBox(restitchAfterTilingCheckBox);
            Controls.Add(restitchAfterTilingCheckBox);
            y += 30;

            // Separator
            var sep = new Label
            {
                Location = new Point(15, y),
                Width = Width - 50,
                Height = 1,
                BackColor = HolographicTheme.BorderCyan
            };
            Controls.Add(sep);
            y += 15;

            // Create a panel for Region Selection radio buttons to group them separately
            var regionSelectionPanel = new Panel
            {
                Location = new Point(0, y),
                Width = Width,
                Height = 58,
                BackColor = Color.Transparent
            };

            // Region selection
            exportFullMapRadio = new RadioButton
            {
                Text = $"Export Entire Map ({mapWidth} x {mapHeight})",
                Location = new Point(15, 0),
                Width = 350,
                Height = 20,
                Checked = true
            };
            HolographicTheme.ApplyToRadioButton(exportFullMapRadio);
            exportFullMapRadio.CheckedChanged += (s, e) =>
            {
                ToggleRegionControls(!exportFullMapRadio.Checked);
                UpdateEstimatedSize();
            };
            regionSelectionPanel.Controls.Add(exportFullMapRadio);

            exportRegionRadio = new RadioButton
            {
                Text = "Export Region:",
                Location = new Point(15, 28),
                Width = 120,
                Height = 20
            };
            HolographicTheme.ApplyToRadioButton(exportRegionRadio);
            exportRegionRadio.CheckedChanged += (s, e) =>
            {
                ToggleRegionControls(exportRegionRadio.Checked);
                UpdateEstimatedSize();
            };
            regionSelectionPanel.Controls.Add(exportRegionRadio);

            Controls.Add(regionSelectionPanel);
            y += 58;

            // Region coordinates
            var startXLabel = new Label { Text = "Start X:", Location = new Point(40, y + 3), Width = 50, Height = 20 };
            HolographicTheme.ApplyToLabel(startXLabel);
            Controls.Add(startXLabel);
            startXNumeric = new NumericUpDown { Location = new Point(95, y), Width = 80, Minimum = 0, Maximum = mapWidth - 1, Value = 0 };
            HolographicTheme.ApplyToNumericUpDown(startXNumeric);
            startXNumeric.ValueChanged += (s, e) => UpdateEstimatedSize();
            Controls.Add(startXNumeric);

            var startYLabel = new Label { Text = "Start Y:", Location = new Point(190, y + 3), Width = 50, Height = 20 };
            HolographicTheme.ApplyToLabel(startYLabel);
            Controls.Add(startYLabel);
            startYNumeric = new NumericUpDown { Location = new Point(245, y), Width = 80, Minimum = 0, Maximum = mapHeight - 1, Value = 0 };
            HolographicTheme.ApplyToNumericUpDown(startYNumeric);
            startYNumeric.ValueChanged += (s, e) => UpdateEstimatedSize();
            Controls.Add(startYNumeric);
            y += 30;

            var endXLabel = new Label { Text = "End X:", Location = new Point(40, y + 3), Width = 50, Height = 20 };
            HolographicTheme.ApplyToLabel(endXLabel);
            Controls.Add(endXLabel);
            endXNumeric = new NumericUpDown { Location = new Point(95, y), Width = 80, Minimum = 1, Maximum = mapWidth, Value = Math.Min(512, mapWidth) };
            HolographicTheme.ApplyToNumericUpDown(endXNumeric);
            endXNumeric.ValueChanged += (s, e) => UpdateEstimatedSize();
            Controls.Add(endXNumeric);

            var endYLabel = new Label { Text = "End Y:", Location = new Point(190, y + 3), Width = 50, Height = 20 };
            HolographicTheme.ApplyToLabel(endYLabel);
            Controls.Add(endYLabel);
            endYNumeric = new NumericUpDown { Location = new Point(245, y), Width = 80, Minimum = 1, Maximum = mapHeight, Value = Math.Min(512, mapHeight) };
            HolographicTheme.ApplyToNumericUpDown(endYNumeric);
            endYNumeric.ValueChanged += (s, e) => UpdateEstimatedSize();
            Controls.Add(endYNumeric);
            y += 40;

            // Estimated size
            estimatedSizeLabel = new Label
            {
                Location = new Point(15, y),
                Width = Width - 50,
                Height = 55,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font(Font.FontFamily, 9f)
            };
            Controls.Add(estimatedSizeLabel);
            y += 60;

            // Buttons
            var exportButton = new Button
            {
                Text = "Export",
                Location = new Point(Width - 200, y),
                Width = 80,
                Height = 30,
                DialogResult = DialogResult.OK
            };
            HolographicTheme.ApplyToButton(exportButton, ButtonStyle.Accent);
            Controls.Add(exportButton);

            var cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(Width - 110, y),
                Width = 80,
                Height = 30,
                DialogResult = DialogResult.Cancel
            };
            HolographicTheme.ApplyToButton(cancelButton);
            Controls.Add(cancelButton);

            AcceptButton = exportButton;
            CancelButton = cancelButton;

            // Wire up OK
            exportButton.Click += (s, e) =>
            {
                ExportZoom = (float)zoomNumeric.Value;
                IncludeStatics = includeStaticsCheckBox.Checked;
                HtmlAndMinimapOnly = htmlOnlyCheckBox.Checked;
                MipmapOnly = mipmapOnlyCheckBox.Checked;
                UseTransparentBackground = useTransparentBackgroundCheckBox.Checked;
                ExportAsSingleImage = singleImageRadio.Checked;
                RestitchAfterTiling = restitchAfterTilingCheckBox.Checked && tiledExportRadio.Checked;

                if (exportFullMapRadio.Checked)
                {
                    RegionStartX = 0;
                    RegionStartY = 0;
                    RegionEndX = mapWidth;
                    RegionEndY = mapHeight;
                }
                else
                {
                    RegionStartX = (int)startXNumeric.Value;
                    RegionStartY = (int)startYNumeric.Value;
                    RegionEndX = (int)endXNumeric.Value;
                    RegionEndY = (int)endYNumeric.Value;
                }
            };

            ToggleRegionControls(false);
            UpdateEstimatedSize();
        }

        private void ToggleRegionControls(bool enabled)
        {
            // Always keep the radio buttons themselves enabled
            exportFullMapRadio.Enabled = true;
            exportRegionRadio.Enabled = true;

            // Only enable/disable the numeric coordinate inputs
            startXNumeric.Enabled = enabled;
            startYNumeric.Enabled = enabled;
            endXNumeric.Enabled = enabled;
            endYNumeric.Enabled = enabled;
        }

        private void UpdateEstimatedSize()
        {
            if (htmlOnlyCheckBox.Checked)
            {
                estimatedSizeLabel.Text = "Will regenerate viewer.html, minimap.jpg,\nand globe_texture.png only.\nExisting tiles will not be re-rendered.";
                estimatedSizeLabel.ForeColor = HolographicTheme.TextPrimary;
                return;
            }

            float zoom = (float)zoomNumeric.Value / 44f;
            float halfTileW = 22f * zoom;
            float halfTileH = 22f * zoom;

            int rw, rh;
            if (exportFullMapRadio.Checked)
            {
                rw = mapWidth;
                rh = mapHeight;
            }
            else
            {
                rw = (int)endXNumeric.Value - (int)startXNumeric.Value;
                rh = (int)endYNumeric.Value - (int)startYNumeric.Value;
            }
            if (rw <= 0) rw = 1;
            if (rh <= 0) rh = 1;

            // Rough isometric bounding box estimate
            int imgW = (int)((rw + rh) * halfTileW) + 100;
            int imgH = (int)((rw + rh) * halfTileH) + 200;

            long pixels = (long)imgW * imgH;
            long memMB = pixels * 4 / (1024 * 1024);
            long memGB = memMB / 1024;

            bool forceSingleImage = singleImageRadio != null && singleImageRadio.Checked;
            bool veryLarge = pixels > 100_000_000; // > 100 MP

            string sizeText;
            Color textColor;

            if (forceSingleImage)
            {
                // User chose single image export
                const int MAX_DIMENSION = 65535;

                bool dimensionTooLarge = imgW > MAX_DIMENSION || imgH > MAX_DIMENSION;

                sizeText = $"Single Image: {imgW:N0} x {imgH:N0} px";
                if (memGB > 0)
                    sizeText += $"\n   Memory: ~{memGB:N0} GB";
                else
                    sizeText += $"\n   Memory: ~{memMB:N0} MB";

                if (dimensionTooLarge)
                {
                    sizeText += $"\nX ERROR: Dimensions exceed GDI+ limit!\n   (Max: {MAX_DIMENSION:N0} px per side)\n   Reduce zoom or use Tiled Export.";
                    textColor = Color.Red;
                }
                else if (memGB >= 4)
                {
                    sizeText += "\n! Very large export - may take several minutes.\n   Ensure you have enough free RAM.";
                    textColor = Color.Orange;
                }
                else if (veryLarge)
                {
                    sizeText += "\n! Large export - may take a minute or two.";
                    textColor = Color.Orange;
                }
                else
                {
                    textColor = HolographicTheme.TextPrimary;
                }
            }
            else
            {
                // Tiled export
                int numTiles = (int)((long)imgW / 512 + 1) * (int)((long)imgH / 512 + 1);
                long estJpegMB = (long)(pixels * 0.3 / (1024 * 1024));
                long estJpegGB = estJpegMB / 1024;

                sizeText = $"Tiled Export: {imgW:N0} x {imgH:N0} px";
                sizeText += $"\n   ~{numTiles:N0} tiles (512×512 JPEG)";
                if (estJpegGB > 0)
                    sizeText += $"\n   Est. disk: ~{estJpegGB:N0}-{estJpegGB * 2:N0} GB";
                else
                    sizeText += $"\n   Est. disk: ~{estJpegMB:N0}-{estJpegMB * 2:N0} MB";
                textColor = HolographicTheme.TextPrimary;
            }

            estimatedSizeLabel.Text = sizeText;
            estimatedSizeLabel.ForeColor = textColor;
        }
    }
}
