using MeesaMultisMaker.ComfyUI;
using MeesaMultisMaker.AI;
using MeesaMultisMaker.Mul;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    partial class MapViewerForm
    {
        private ComfyUIClient comfyClient;
        private const string DEFAULT_COMFY_URL = "http://localhost:8188";

        // Cache for original tile artwork (before AI replacement)
        private readonly Dictionary<ushort, Image> originalArtCache = new Dictionary<ushort, Image>();

        // Cache for AI-modified tile artwork
        private readonly Dictionary<ushort, Image> modifiedArtCache = new Dictionary<ushort, Image>();

        // Cache for original static artwork (before AI replacement)
        private readonly Dictionary<ushort, Image> originalStaticArtCache = new Dictionary<ushort, Image>();

        // Cache for AI-modified static artwork
        private readonly Dictionary<int, Image> modifiedStaticArtCache = new Dictionary<int, Image>();

        // Track whether we're showing original or modified art
        private bool showingOriginalArt = false;

        private class UnderlayPlane
        {
            public int Facet { get; set; }
            public Rectangle Bounds { get; set; }
            public Bitmap Image { get; set; }
            public int GumpId { get; set; }
            public string FilePath { get; set; }
            public bool IsFromServer { get; set; }
            public float OffsetX { get; set; }
            public float OffsetY { get; set; }
            public float Scale { get; set; } = 1f;
            public float RotationDeg { get; set; }
        }

        private class OverlayPlane
        {
            public int Facet { get; set; }
            public Rectangle Bounds { get; set; }
            public Bitmap Image { get; set; }
            public string FilePath { get; set; }
            public bool IsFromServer { get; set; }
            public float OffsetX { get; set; }
            public float OffsetY { get; set; }
            public float Scale { get; set; } = 1f;
            public float RotationDeg { get; set; }
        }

        private readonly List<UnderlayPlane> underlayPlanes = new List<UnderlayPlane>();
        private bool showUnderlayPlanes = true;
        private readonly List<OverlayPlane> overlayPlanes = new List<OverlayPlane>();
        private bool showOverlayPlanes = true;
        private bool _lastPainterPlaneIsOverlay = false;
        private int _lastPainterPlaneIndex = -1;

        /// <summary>
        /// Initialize ComfyUI client
        /// </summary>
        private void InitializeComfyUI()
        {
            comfyClient = new ComfyUIClient(ComfyUIUrl);
        }

        /// <summary>
        /// Toggle between original and modified artwork display
        /// </summary>
        private void ToggleOldNewArt()
        {
            if (modifiedArtCache.Count == 0 && modifiedStaticArtCache.Count == 0)
            {
                // No modifications yet, nothing to toggle
                return;
            }

            showingOriginalArt = !showingOriginalArt;

            if (showingOriginalArt)
            {
                // Switch to original art - land tiles
                foreach (var kvp in originalArtCache)
                {
                    artCache[kvp.Key] = kvp.Value;
                }
                // Switch to original art - statics
                foreach (var kvp in originalStaticArtCache)
                {
                    staticArtCache[kvp.Key] = kvp.Value;
                }
            }
            else
            {
                // Switch to modified art - land tiles
                foreach (var kvp in modifiedArtCache)
                {
                    artCache[kvp.Key] = kvp.Value;
                }
                // Switch to modified art - statics
                foreach (var kvp in modifiedStaticArtCache)
                {
                    staticArtCache[kvp.Key] = kvp.Value;
                }
            }

            GenerateMapImage();
        }

        private float GetAverageLandZForBounds(Rectangle bounds)
        {
            if (currentMap == null || bounds.Width <= 0 || bounds.Height <= 0)
                return 0f;

            int left = Math.Max(0, bounds.Left);
            int top = Math.Max(0, bounds.Top);
            int right = Math.Min(currentMap.Width, bounds.Right);
            int bottom = Math.Min(currentMap.Height, bounds.Bottom);
            if (right <= left || bottom <= top)
                return 0f;

            long area = (long)(right - left) * (bottom - top);
            int step = 1;
            if (area > 1024)
                step = Math.Max(1, (int)Math.Sqrt(area / 1024.0));

            long sum = 0;
            int count = 0;

            for (int y = top; y < bottom; y += step)
            {
                for (int x = left; x < right; x += step)
                {
                    var t = currentMap.Tiles[x, y];
                    if (t == null) continue;
                    sum += t.Z;
                    count++;
                }
            }

            return count > 0 ? (float)sum / count : 0f;
        }

        /// <summary>
        /// Restore original artwork for a tile
        /// </summary>
        internal void RestoreTileOriginalArt(ushort tileId)
        {
            // Remove from modified cache so renderer falls back to texmap/original
            modifiedArtCache.Remove(tileId);

            // If we have original art cached, restore it
            if (originalArtCache.ContainsKey(tileId))
            {
                artCache[tileId] = originalArtCache[tileId];
            }
            else
            {
                // Load fresh from art.mul and cache as original
                if (tileId < 0x4000 && !string.IsNullOrEmpty(mulFolderPath))
                {
                    try
                    {
                        var landArt = LandTileArtReader.LoadLandTile(mulFolderPath, tileId);
                        if (landArt != null)
                        {
                            originalArtCache[tileId] = landArt;
                            artCache[tileId] = landArt;
                        }
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Restore original artwork for a static item
        /// </summary>
        internal void RestoreStaticOriginalArt(ushort itemId)
        {
            if (originalStaticArtCache.ContainsKey(itemId))
            {
                staticArtCache[itemId] = originalStaticArtCache[itemId];
            }
            else
            {
                // Load fresh from art.mul
                if (!string.IsNullOrEmpty(mulFolderPath))
                {
                    try
                    {
                        var staticArt = LoadStaticImage(itemId);
                        if (staticArt != null)
                        {
                            originalStaticArtCache[itemId] = staticArt;
                            staticArtCache[itemId] = staticArt;
                        }
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Replace selected tiles with AI-generated art
        /// </summary>
        private async Task ReplaceSelectedTilesWithAI()
        {
            if (replaceTiles.Count == 0) return;

            // Check which backend is selected
            var selectedBackend = SelectedBackend;
            
            // Determine if we're using context-aware inpainting (requires ComfyUI)
            bool useContextAware = contextTiles.Count > 0;
            
            // Context-aware inpainting requires ComfyUI's InpaintWorkflow
            if (useContextAware && selectedBackend != AIBackend.ComfyUI)
            {
                MessageBox.Show(
                    "Context-aware inpainting (using context tiles) requires ComfyUI.\n\n" +
                    "Either switch to ComfyUI backend, or use single-tile mode (no context tiles selected).",
                    "Backend Not Supported",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // Validate backend availability
            if (selectedBackend == AIBackend.ComfyUI)
            {
                comfyClient = new ComfyUIClient(ComfyUIUrl);
            }
            else
            {
                bool available = await ImageGeneratorFactory.EnsureBackendAvailable(selectedBackend, this);
                if (!available) return;
            }

            int targetWidth = ResolutionWidth;
            int targetHeight = ResolutionHeight;
            string backendName = ImageGeneratorFactory.GetBackendName(selectedBackend);

            // Show progress dialog
            var progressForm = new Form
            {
                Text = $"AI Tile Replacement ({backendName})",
                Width = 600,
                Height = 250,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            HolographicTheme.ApplyToForm(progressForm);

            var progressLabel = new Label
            {
                Text = "Preparing...",
                Location = new Point(20, 20),
                Width = 550,
                Height = 30
            };
            HolographicTheme.ApplyToLabel(progressLabel);
            progressForm.Controls.Add(progressLabel);

            var progressBar = new ProgressBar
            {
                Location = new Point(20, 60),
                Width = 550,
                Height = 25,
                Style = ProgressBarStyle.Continuous,
                Maximum = 100
            };
            progressForm.Controls.Add(progressBar);

            var detailsTextBox = new TextBox
            {
                Location = new Point(20, 95),
                Width = 550,
                Height = 100,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical
            };
            HolographicTheme.ApplyToTextBox(detailsTextBox);
            progressForm.Controls.Add(detailsTextBox);

            progressForm.Show(this);
            Application.DoEvents();

            try
            {
                // Restore original artwork for all selected tiles
                progressLabel.Text = "Restoring original artwork...";
                Application.DoEvents();

                var allSelectedTiles = new List<SelectedTile>();
                allSelectedTiles.AddRange(replaceTiles);
                allSelectedTiles.AddRange(contextTiles);

                var uniqueTileIds = allSelectedTiles.Select(t => t.TileId).Distinct().ToList();
                foreach (var tileId in uniqueTileIds)
                    RestoreTileOriginalArt(tileId);
                
                detailsTextBox.AppendText($"Restored {uniqueTileIds.Count} unique tile type(s) to original artwork\r\n");
                detailsTextBox.AppendText($"Backend: {backendName}\r\n");
                progressBar.Value = 5;

                if (useContextAware)
                {
                    // Context-aware inpainting mode (ComfyUI only)
                    await ProcessContextAwareInpainting(progressForm, progressLabel, progressBar, detailsTextBox, targetWidth, targetHeight);
                }
                else
                {
                    // Single tile mode - supports both backends
                    await ProcessSingleTileMode(progressForm, progressLabel, progressBar, detailsTextBox, targetWidth, targetHeight, selectedBackend);
                }

                progressLabel.Text = "Complete! Refreshing map...";
                progressBar.Value = 100;
                Application.DoEvents();

                GenerateMapImage();

                // Auto-push to JarJar if enabled
                if (AppConfig.Instance.JarJarPushEnabled && AppConfig.Instance.JarJarAutoPush && pendingArtChanges.Count > 0)
                {
                    progressLabel.Text = "Auto-pushing to MeesaJarJar...";
                    Application.DoEvents();
                    await PushToJarJar();
                }

                await Task.Delay(500);
                progressForm.Close();
            }
            catch (Exception ex)
            {
                progressForm.Close();
                MessageBox.Show($"Error during tile replacement: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Process tiles in single-tile mode (supports both ComfyUI and Z Image)
        /// </summary>
        private async Task ProcessSingleTileMode(Form progressForm, Label progressLabel, ProgressBar progressBar,
            TextBox detailsTextBox, int targetWidth, int targetHeight, AIBackend backend)
        {
            string backendName = ImageGeneratorFactory.GetBackendName(backend);
            detailsTextBox.AppendText($"=== Single Tile Mode ({backendName}) ===\r\n");
            detailsTextBox.AppendText($"Processing {replaceTiles.Count} tiles\r\n");
            detailsTextBox.AppendText($"Resolution: {targetWidth}x{targetHeight}\r\n");
            detailsTextBox.AppendText($"Settings: Steps={Steps}, CFG={CFG}, Denoise={Denoise}\r\n\r\n");

            var tileGroups = replaceTiles.GroupBy(t => t.TileId).ToList();
            int currentGroup = 0;

            foreach (var group in tileGroups)
            {
                var tileId = group.Key;
                var tilesInGroup = group.ToList();

                currentGroup++;
                int progress = (int)((currentGroup / (float)tileGroups.Count) * 100);
                progressBar.Value = progress;

                progressLabel.Text = $"Generating art for tile ID 0x{tileId:X4} ({currentGroup}/{tileGroups.Count})...";
                Application.DoEvents();

                var originalImage = LoadTileImage(tileId);
                if (originalImage == null)
                {
                    detailsTextBox.AppendText($"Skipping 0x{tileId:X4}: No image loaded\r\n");
                    continue;
                }

                try
                {
                    Image newImage;
                    if (backend == AIBackend.ComfyUI)
                    {
                        newImage = await GenerateTileArtWithComfyUI(originalImage, tileId, targetWidth, targetHeight);
                    }
                    else
                    {
                        throw new NotSupportedException("Only ComfyUI backend is supported.");
                    }

                    if (newImage != null)
                    {
                        artCache[tileId] = newImage;
                        modifiedArtCache[tileId] = newImage;
                        MarkArtModified(tileId, newImage);
                        detailsTextBox.AppendText($"? Replaced 0x{tileId:X4} ({tilesInGroup.Count} instances)\r\n");
                    }
                    else
                    {
                        detailsTextBox.AppendText($"? Failed to generate for 0x{tileId:X4}\r\n");
                    }
                }
                catch (Exception ex)
                {
                    detailsTextBox.AppendText($"? Error on 0x{tileId:X4}: {ex.Message}\r\n");
                }

                Application.DoEvents();
            }
        }

        /// <summary>
        /// Process context-aware inpainting (ComfyUI only)
        /// </summary>
        private async Task ProcessContextAwareInpainting(Form progressForm, Label progressLabel, ProgressBar progressBar,
            TextBox detailsTextBox, int targetWidth, int targetHeight)
        {
            detailsTextBox.AppendText($"=== Context-Aware Inpainting Mode ===\r\n");
            detailsTextBox.AppendText($"Replace: {replaceTiles.Count} tiles\r\n");
            detailsTextBox.AppendText($"Context: {contextTiles.Count} tiles\r\n");
            detailsTextBox.AppendText($"Resolution: {targetWidth}x{targetHeight}\r\n");
            detailsTextBox.AppendText($"Settings: Steps={Steps}, CFG={CFG}, Denoise={Denoise}\r\n\r\n");

            progressBar.Value = 10;
            progressLabel.Text = $"Rendering selected area to {targetWidth}x{targetHeight}...";
            Application.DoEvents();

            Rectangle bounds;
            Dictionary<Point, ushort> tileMapping;
            var renderedImage = RenderSelectedAreaToImage(out bounds, out tileMapping, targetWidth, targetHeight);

            if (renderedImage == null)
                throw new Exception("Failed to render selected area");

            detailsTextBox.AppendText($"Rendered area: {bounds.Width}x{bounds.Height} tiles\r\n");
            progressBar.Value = 20;

            progressLabel.Text = "Creating inpaint mask...";
            Application.DoEvents();

            var maskImage = CreateInpaintMask(bounds, targetWidth, targetHeight);
            detailsTextBox.AppendText($"Created mask for {replaceTiles.Count} tiles to replace\r\n");
            progressBar.Value = 30;

            progressLabel.Text = "Uploading images to ComfyUI...";
            Application.DoEvents();

            byte[] imageBytes;
            using (var ms = new MemoryStream())
            {
                renderedImage.Save(ms, ImageFormat.Png);
                imageBytes = ms.ToArray();
            }
            var uploadedImageName = await comfyClient.UploadImage(imageBytes, "context_area.png");
            detailsTextBox.AppendText($"Uploaded context image ({targetWidth}x{targetHeight})\r\n");

            byte[] maskBytes;
            using (var ms = new MemoryStream())
            {
                maskImage.Save(ms, ImageFormat.Png);
                maskBytes = ms.ToArray();
            }
            var uploadedMaskName = await comfyClient.UploadImage(maskBytes, "inpaint_mask.png");
            detailsTextBox.AppendText($"Uploaded inpaint mask\r\n");
            progressBar.Value = 50;

            renderedImage.Dispose();
            maskImage.Dispose();

            progressLabel.Text = "Generating with AI...";
            Application.DoEvents();

            string prompt = string.IsNullOrWhiteSpace(Prompt)
                ? "high quality terrain texture, isometric game tiles, detailed, seamless"
                : Prompt;

            var workflow = InpaintWorkflow.CreateWorkflow(
                prompt, NegativePrompt, uploadedImageName, uploadedMaskName,
                targetWidth, targetHeight, Steps, CFG, Denoise, Seed, Sampler, Scheduler, Checkpoint
            );

            var promptId = await comfyClient.QueuePrompt(workflow);
            if (string.IsNullOrEmpty(promptId))
                throw new Exception("Failed to queue prompt");

            detailsTextBox.AppendText($"Queued prompt ID: {promptId}\r\n");
            detailsTextBox.AppendText($"Waiting for generation...\r\n");
            progressBar.Value = 60;

            var images = await comfyClient.GetGeneratedImages(promptId, maxAttempts: 300, pollIntervalMs: 1000);
            if (images.Count == 0)
                throw new Exception("No images generated");

            progressBar.Value = 80;
            progressLabel.Text = "Downloading and extracting tiles...";
            Application.DoEvents();

            var imageData = await comfyClient.DownloadImage(images[0]);
            if (imageData == null || imageData.Length == 0)
                throw new Exception("Download from ComfyUI failed");
            using (var ms = new MemoryStream(imageData))
            using (var generatedImage = Image.FromStream(ms))
            {
                detailsTextBox.AppendText($"Downloaded generated image\r\n");
                await ExtractAndApplyTiles(generatedImage, bounds, replaceTiles, targetWidth, targetHeight);
                detailsTextBox.AppendText($"Applied {replaceTiles.Count} replacement tiles\r\n");
            }

            progressBar.Value = 100;
        }

        /// <summary>
        /// Generate new art for a tile using ComfyUI
        /// </summary>
        private async Task<Image> GenerateTileArtWithComfyUI(Image originalImage, ushort tileId, int targetWidth = 512, int targetHeight = 512)
        {
            const int TILE_SIZE = 44;

            // Prepare the image - tile the pattern to fill more space
            Bitmap preparedImage;
            using (var bmp = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

                    int tilesX = (targetWidth / TILE_SIZE) + 2;
                    int tilesY = (targetHeight / TILE_SIZE) + 2;
                    int startX = (targetWidth - (tilesX * TILE_SIZE)) / 2;
                    int startY = (targetHeight - (tilesY * TILE_SIZE)) / 2;

                    for (int ty = 0; ty < tilesY; ty++)
                    {
                        for (int tx = 0; tx < tilesX; tx++)
                        {
                            int x = startX + (tx * TILE_SIZE);
                            int y = startY + (ty * TILE_SIZE);
                            g.DrawImage(originalImage, x, y, TILE_SIZE, TILE_SIZE);
                        }
                    }
                }
                preparedImage = new Bitmap(bmp);
            }

            try
            {
                string basePrompt = GetPromptForTileId(tileId);
                string fullPrompt = string.IsNullOrWhiteSpace(Prompt) ? basePrompt : $"{Prompt}, {basePrompt}";

                // Use ComfyUI for generation
                byte[] imageBytes;
                using (var ms = new MemoryStream())
                {
                    preparedImage.Save(ms, ImageFormat.Png);
                    imageBytes = ms.ToArray();
                }

                var comfyClient = new ComfyUIClient(ComfyUIUrl);
                string uploadedFilename = await comfyClient.UploadImage(imageBytes, $"tile_{tileId}.png");

                string workflow = ComfyUI.Image2ImageWorkflow.CreateWorkflow(
                    fullPrompt,
                    NegativePrompt,
                    uploadedFilename,
                    targetWidth,
                    targetHeight,
                    Steps,
                    CFG,
                    Denoise,
                    Seed,
                    Sampler,
                    Scheduler
                );

                string promptId = await comfyClient.QueuePrompt(workflow);
                if (string.IsNullOrEmpty(promptId))
                {
                    throw new Exception("Failed to queue ComfyUI prompt");
                }

                var images = await comfyClient.GetGeneratedImages(promptId, maxAttempts: 120, pollIntervalMs: 1000);
                if (images.Count == 0)
                {
                    throw new Exception("No images generated by ComfyUI");
                }

                byte[] newImageData = await comfyClient.DownloadImage(images[0]);
                if (newImageData == null || newImageData.Length == 0)
                    throw new Exception("Download from ComfyUI failed");
                using (var ms = new MemoryStream(newImageData))
                {
                    return new Bitmap(ms);
                }
            }
            finally
            {
                preparedImage.Dispose();
            }
        }

        /// <summary>
        /// Get an appropriate AI prompt based on the tile ID
        /// </summary>
        private string GetPromptForTileId(ushort tileId)
        {
            // Water tiles
            if ((tileId >= 0x00A8 && tileId <= 0x00AB) || (tileId >= 0x0136 && tileId <= 0x01AF))
                return "water texture, ocean surface, blue water, ripples, isometric game tile, high detail";

            // Grass tiles
            if (tileId <= 0x0015 || (tileId >= 0x0230 && tileId <= 0x023F))
                return "grass texture, green grass field, nature, isometric game tile, high detail";

            // Dirt tiles
            if (tileId >= 0x0016 && tileId <= 0x002B)
                return "dirt texture, brown earth, soil, ground, isometric game tile, high detail";

            // Sand tiles
            if (tileId >= 0x002C && tileId <= 0x004F)
                return "sand texture, beach sand, desert sand, tan, isometric game tile, high detail";

            // Stone/Rock tiles
            if (tileId >= 0x0050 && tileId <= 0x009B)
                return "stone texture, gray rock, cobblestone, isometric game tile, high detail";

            // Swamp tiles
            if (tileId >= 0x009C && tileId <= 0x00A7)
                return "swamp texture, murky water, moss, wet ground, isometric game tile, high detail";

            // Cave/Dark stone
            if (tileId >= 0x00AC && tileId <= 0x00D7)
                return "dark stone texture, cave floor, dark rock, isometric game tile, high detail";

            // Snow tiles
            if (tileId >= 0x010C && tileId <= 0x011F)
                return "snow texture, white snow, frozen ground, winter, isometric game tile, high detail";

            // Lava tiles
            if (tileId >= 0x01F4 && tileId <= 0x0212)
                return "lava texture, molten rock, red hot magma, flowing lava, isometric game tile, high detail";

            // Jungle tiles
            if (tileId >= 0x0578 && tileId <= 0x05FB)
                return "jungle texture, dense vegetation, tropical plants, isometric game tile, high detail";

            // Default
            return "fantasy game terrain texture, isometric tile, high detail, realistic";
        }

        /// <summary>
        /// Apply a diamond mask to an image for isometric tile rendering
        /// </summary>
        private Bitmap ApplyDiamondMask(Image source)
        {
            int size = source.Width; // Assume square image
            var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);

            using (var g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                // Create diamond shape for isometric tile
                // For a 44x44 tile, the diamond spans the full width/height
                float centerX = size / 2f;
                float centerY = size / 2f;
                float halfW = size / 2f;
                float halfH = size / 2f;

                // Define diamond points: Top, Right, Bottom, Left
                PointF[] diamond = new PointF[]
                {
                    new PointF(centerX, 0),           // Top
                    new PointF(size, centerY),        // Right
                    new PointF(centerX, size),        // Bottom
                    new PointF(0, centerY)            // Left
                };

                // Set clipping region to diamond shape
                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    path.AddPolygon(diamond);
                    g.SetClip(path);
                }

                // Draw the source image (will be clipped to diamond)
                g.DrawImage(source, 0, 0, size, size);
            }

            return result;
        }

        /// <summary>
        /// Render selected tile area to an image for ComfyUI processing
        /// </summary>
        private Bitmap RenderSelectedAreaToImage(out Rectangle bounds, out Dictionary<Point, ushort> tileMapping, int targetWidth = 512, int targetHeight = 512)
        {
            tileMapping = new Dictionary<Point, ushort>();

            // Get all selected tiles (both replace and context)
            var allTiles = new List<SelectedTile>();
            allTiles.AddRange(replaceTiles);
            allTiles.AddRange(contextTiles);

            if (allTiles.Count == 0)
            {
                bounds = Rectangle.Empty;
                return null;
            }

            // Calculate bounding box of all selected tiles
            int minX = allTiles[0].X;
            int maxX = allTiles[0].X;
            int minY = allTiles[0].Y;
            int maxY = allTiles[0].Y;

            foreach (var tile in allTiles)
            {
                if (tile.X < minX) minX = tile.X;
                if (tile.X > maxX) maxX = tile.X;
                if (tile.Y < minY) minY = tile.Y;
                if (tile.Y > maxY) maxY = tile.Y;
            }

            bounds = new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);

            // Create canvas at target resolution
            var canvas = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);

            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Black);
                // Use NearestNeighbor for sharp pixel art quality
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;

                const int TILE_WIDTH = 44;
                const int TILE_HEIGHT = 44;

                // Calculate scale to fit the selected area into target resolution
                // We need to account for isometric projection
                int gridWidth = bounds.Width;
                int gridHeight = bounds.Height;

                // In isometric view, the rendered size is:
                // Width: (gridWidth + gridHeight) * (TILE_WIDTH / 2)
                // Height: (gridWidth + gridHeight) * (TILE_HEIGHT / 2)
                float isoWidth = (gridWidth + gridHeight) * (TILE_WIDTH / 2f);
                float isoHeight = (gridWidth + gridHeight) * (TILE_HEIGHT / 2f);

                // Calculate scale to fit in target resolution with some padding
                float scaleX = (targetWidth * 0.9f) / isoWidth;
                float scaleY = (targetHeight * 0.9f) / isoHeight;
                float scale = Math.Min(scaleX, scaleY);

                float scaledTileW = TILE_WIDTH * scale;
                float scaledTileH = TILE_HEIGHT * scale;
                float halfTileW = scaledTileW / 2f;
                float halfTileH = scaledTileH / 2f;

                float centerX = targetWidth / 2f;
                float centerY = targetHeight / 2f;

                // Render each tile in the selection
                foreach (var tile in allTiles)
                {
                    // Relative position within the bounding box (centered at origin)
                    float centerBoundsX = (minX + maxX) / 2f;
                    float centerBoundsY = (minY + maxY) / 2f;

                    float relX = tile.X - centerBoundsX;
                    float relY = tile.Y - centerBoundsY;

                    // Convert to isometric coordinates
                    float isoX = (relX + relY) * halfTileW;
                    float isoY = (relY - relX) * halfTileH;

                    float screenX = centerX + isoX;
                    float screenY = centerY + isoY;

                    float drawX = screenX - scaledTileW / 2f;
                    float drawY = screenY - scaledTileH / 2f;

                    var dest = new RectangleF(drawX, drawY, scaledTileW, scaledTileH);

                    // Load the tile image
                    var img = LoadTileImage(tile.TileId);
                    if (img != null)
                    {
                        try
                        {
                            g.DrawImage(img, dest);

                            // Store mapping for reverse lookup
                            tileMapping[new Point(tile.X, tile.Y)] = tile.TileId;
                        }
                        catch
                        {
                            // Fallback to colored rectangle
                            using (var brush = new SolidBrush(GetLandTileColor(tile.TileId)))
                            {
                                g.FillRectangle(brush, dest);
                            }
                        }
                    }
                }
            }

            return canvas;
        }

        /// <summary>
        /// Create inpaint mask where replace tiles are white (to be regenerated)
        /// </summary>
        private Bitmap CreateInpaintMask(Rectangle bounds, int targetWidth = 512, int targetHeight = 512)
        {
            const int TILE_WIDTH = 44;
            const int TILE_HEIGHT = 44;

            var mask = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);

            using (var g = Graphics.FromImage(mask))
            {
                g.Clear(Color.Black); // Black = keep original
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                // Calculate same scale as RenderSelectedAreaToImage
                int gridWidth = bounds.Width;
                int gridHeight = bounds.Height;

                float isoWidth = (gridWidth + gridHeight) * (TILE_WIDTH / 2f);
                float isoHeight = (gridWidth + gridHeight) * (TILE_HEIGHT / 2f);

                float scaleX = (targetWidth * 0.9f) / isoWidth;
                float scaleY = (targetHeight * 0.9f) / isoHeight;
                float scale = Math.Min(scaleX, scaleY);

                float scaledTileW = TILE_WIDTH * scale;
                float scaledTileH = TILE_HEIGHT * scale;
                float halfTileW = scaledTileW / 2f;
                float halfTileH = scaledTileH / 2f;

                float centerX = targetWidth / 2f;
                float centerY = targetHeight / 2f;

                // Draw white diamonds for tiles to be replaced
                using (var whiteBrush = new SolidBrush(Color.White))
                {
                    foreach (var tile in replaceTiles)
                    {
                        // Use same centering as rendering
                        float centerBoundsX = (bounds.Left + bounds.Right - 1) / 2f;
                        float centerBoundsY = (bounds.Top + bounds.Bottom - 1) / 2f;

                        float relX = tile.X - centerBoundsX;
                        float relY = tile.Y - centerBoundsY;

                        float isoX = (relX - relY) * halfTileW;
                        float isoY = (relX + relY) * halfTileH;

                        float screenX = centerX + isoX;
                        float screenY = centerY + isoY;

                        // Create diamond shape for mask
                        PointF[] diamond = new PointF[]
                        {
                            new PointF(screenX, screenY - halfTileH),
                            new PointF(screenX + halfTileW, screenY),
                            new PointF(screenX, screenY + halfTileH),
                            new PointF(screenX - halfTileW, screenY)
                        };

                        g.FillPolygon(whiteBrush, diamond);
                    }
                }
            }

            return mask;
        }

        private async Task GenerateUnderlayPlaneWithAI()
        {
            var selectedBackend = SelectedBackend;
            bool cancelRequested = false;

            var progressForm = new Form
            {
                Text = "Generating Underlay...",
                Width = 520,
                Height = 180,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            HolographicTheme.ApplyToForm(progressForm);

            var progressLabel = new Label
            {
                Left = 16,
                Top = 16,
                Width = 470,
                Height = 36,
                Text = "Preparing underlay generation..."
            };
            HolographicTheme.ApplyToLabel(progressLabel);
            progressForm.Controls.Add(progressLabel);

            var progressBar = new ProgressBar
            {
                Left = 16,
                Top = 58,
                Width = 470,
                Height = 20,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            progressForm.Controls.Add(progressBar);

            var cancelButton = new Button
            {
                Text = "Cancel",
                Left = 386,
                Top = 92,
                Width = 100,
                Height = 28
            };
            cancelButton.Click += (s, e) =>
            {
                cancelRequested = true;
                cancelButton.Enabled = false;
                progressLabel.Text = "Cancelling...";
            };
            HolographicTheme.ApplyToButton(cancelButton, ButtonStyle.Danger);
            progressForm.Controls.Add(cancelButton);

            Action<string, int> setProgress = (text, value) =>
            {
                progressLabel.Text = text;
                progressBar.Value = Math.Max(progressBar.Minimum, Math.Min(progressBar.Maximum, value));
                Application.DoEvents();
            };

            progressForm.Show(this);
            Application.DoEvents();

            var allTiles = new List<SelectedTile>();
            allTiles.AddRange(replaceTiles);
            allTiles.AddRange(contextTiles);
            allTiles = allTiles.Distinct().ToList();

            if (allTiles.Count == 0)
            {
                MessageBox.Show(this,
                    "Select a rectangle of tiles around the void first (replace/context selection).",
                    "Generate Underlay",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            int minX = allTiles.Min(t => t.X);
            int maxX = allTiles.Max(t => t.X);
            int minY = allTiles.Min(t => t.Y);
            int maxY = allTiles.Max(t => t.Y);
            var bounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);

            int targetWidth = ResolutionWidth;
            int targetHeight = ResolutionHeight;
            setProgress("Rendering selected area...", 8);
            var contextImage = RenderMapBoundsToImage(bounds, targetWidth, targetHeight);
            if (contextImage == null)
            {
                progressForm.Close();
                MessageBox.Show(this, "Failed to render map area for underlay generation.", "Generate Underlay",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (cancelRequested)
            {
                contextImage.Dispose();
                progressForm.Close();
                return;
            }

            setProgress("Creating inpaint mask...", 15);
            var maskImage = CreateBlackVoidMask(contextImage, threshold: 10, dilatePixels: 5);
            if (maskImage == null)
            {
                contextImage.Dispose();
                progressForm.Close();
                MessageBox.Show(this, "Failed to generate void mask.", "Generate Underlay",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Restrict the generation mask to selected tile locations only.
            IntersectMaskWithSelectedTiles(maskImage, bounds, allTiles);

            // Ensure mask contains at least some white pixels.
            bool hasVoid = false;
            for (int y = 0; y < maskImage.Height && !hasVoid; y++)
            {
                for (int x = 0; x < maskImage.Width; x++)
                {
                    if (maskImage.GetPixel(x, y).R > 200)
                    {
                        hasVoid = true;
                        break;
                    }
                }
            }
            if (!hasVoid)
            {
                contextImage.Dispose();
                maskImage.Dispose();
                progressForm.Close();
                MessageBox.Show(this,
                    "No black/void region detected in selected area.",
                    "Generate Underlay",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                string prompt = string.IsNullOrWhiteSpace(Prompt)
                    ? "high quality fantasy terrain, seamless continuation of surrounding environment, isometric game world underlay, detailed"
                    : Prompt;
                Bitmap generated;

                if (cancelRequested)
                    throw new OperationCanceledException();

                if (selectedBackend == AIBackend.ComfyUI)
                {
                    setProgress("Connecting to ComfyUI...", 22);
                    comfyClient = new ComfyUIClient(ComfyUIUrl);

                    byte[] contextBytes;
                    using (var ms = new MemoryStream())
                    {
                        contextImage.Save(ms, ImageFormat.Png);
                        contextBytes = ms.ToArray();
                    }
                    byte[] maskBytes;
                    using (var ms = new MemoryStream())
                    {
                        maskImage.Save(ms, ImageFormat.Png);
                        maskBytes = ms.ToArray();
                    }

                    if (cancelRequested)
                        throw new OperationCanceledException();

                    setProgress("Uploading context image...", 30);
                    var uploadedImageName = await WaitWithCancel(
                        comfyClient.UploadImage(contextBytes, "underlay_context.png"),
                        () => cancelRequested);

                    setProgress("Uploading mask image...", 36);
                    var uploadedMaskName = await WaitWithCancel(
                        comfyClient.UploadImage(maskBytes, "underlay_mask.png"),
                        () => cancelRequested);

                    bool useFlux2KleinWorkflow =
                        !string.IsNullOrWhiteSpace(Checkpoint) &&
                        (Checkpoint.IndexOf("flux-2-klein", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         Checkpoint.IndexOf("flux2", StringComparison.OrdinalIgnoreCase) >= 0);

                    var workflow = useFlux2KleinWorkflow
                        ? Flux2KleinInpaintWorkflow.CreateWorkflow(
                            prompt,
                            NegativePrompt,
                            uploadedImageName,
                            uploadedMaskName,
                            targetWidth,
                            targetHeight,
                            Steps,
                            CFG,
                            Seed,
                            Sampler,
                            unetName: Checkpoint)
                        : InpaintWorkflow.CreateWorkflow(
                            prompt,
                            NegativePrompt,
                            uploadedImageName,
                            uploadedMaskName,
                            targetWidth,
                            targetHeight,
                            Steps,
                            CFG,
                            Denoise,
                            Seed,
                            Sampler,
                            Scheduler,
                            Checkpoint);

                    setProgress(useFlux2KleinWorkflow ? "Queueing Flux2 Klein inpaint workflow..." : "Queueing inpaint workflow...", 45);
                    var promptId = await WaitWithCancel(
                        comfyClient.QueuePrompt(workflow),
                        () => cancelRequested,
                        async () => await comfyClient.Interrupt());
                    if (string.IsNullOrEmpty(promptId))
                        throw new Exception("Failed to queue ComfyUI prompt.");

                    setProgress("Generating underlay (this can take a while)...", 60);
                    var images = await WaitWithCancel(
                        comfyClient.GetGeneratedImages(promptId, maxAttempts: 300, pollIntervalMs: 1000),
                        () => cancelRequested,
                        async () => await comfyClient.Interrupt());
                    if (images.Count == 0)
                        throw new Exception("No underlay image generated.");

                    setProgress("Downloading generated image...", 85);
                    var imageData = await WaitWithCancel(
                        comfyClient.DownloadImage(images[0]),
                        () => cancelRequested,
                        async () => await comfyClient.Interrupt());
                    if (imageData == null || imageData.Length == 0)
                        throw new Exception("Download from ComfyUI failed");
                    using (var ms = new MemoryStream(imageData))
                    using (var img = Image.FromStream(ms))
                    {
                        generated = new Bitmap(img);
                    }
                }
                else
                {
                    setProgress("Preparing local backend...", 22);
                    bool available = await ImageGeneratorFactory.EnsureBackendAvailable(selectedBackend, this);
                    if (!available)
                        return;

                    if (cancelRequested)
                        throw new OperationCanceledException();

                    setProgress("Generating underlay locally...", 60);
                    generated = await WaitWithCancel(
                        GenerateUnderlayWithLocalBackend(contextImage, maskImage, prompt, selectedBackend, targetWidth, targetHeight),
                        () => cancelRequested);
                    if (generated == null)
                        throw new Exception("Local backend failed to generate underlay.");
                }

                if (cancelRequested)
                    throw new OperationCanceledException();

                setProgress("Applying selection mask...", 90);
                // Keep generated underlay only on user-selected input tile locations.
                ApplySelectionMaskToUnderlay(generated, bounds, allTiles);
                RemoveNearBlackEdgeFringe(generated, blackThreshold: 18, radius: 1);

                string underlayFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Underlays");
                if (!Directory.Exists(underlayFolder))
                    Directory.CreateDirectory(underlayFolder);

                string filePath = Path.Combine(underlayFolder,
                    $"underlay_f{(facetComboBox?.SelectedIndex ?? 0)}_{bounds.X}_{bounds.Y}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                generated.Save(filePath, ImageFormat.Png);

                setProgress("Indexing underlay for JarJar...", 96);
                MarkUnderlayModified(facetComboBox?.SelectedIndex ?? 0, bounds, generated, filePath);

                underlayPlanes.Add(new UnderlayPlane
                {
                    Facet = facetComboBox?.SelectedIndex ?? 0,
                    Bounds = bounds,
                    Image = new Bitmap(generated),
                    GumpId = -1,
                    FilePath = filePath
                });

                generated.Dispose();

                GenerateMapImage();
                progressForm.Close();
                MessageBox.Show(this,
                    $"Underlay generated and saved as PNG.\nPNG: {filePath}\n(Queued for JarJar underlay upload)",
                    "Underlay Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                progressForm.Close();
                MessageBox.Show(this,
                    "Underlay generation was cancelled.",
                    "Generate Underlay",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                progressForm.Close();
                MessageBox.Show(this,
                    $"Underlay generation failed: {ex.Message}",
                    "Generate Underlay",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                contextImage.Dispose();
                maskImage.Dispose();
            }
        }

        private void RemoveNearBlackEdgeFringe(Bitmap image, int blackThreshold, int radius)
        {
            if (image == null || image.Width <= 2 || image.Height <= 2)
                return;

            using (var source = new Bitmap(image))
            {
                for (int y = 1; y < image.Height - 1; y++)
                {
                    for (int x = 1; x < image.Width - 1; x++)
                    {
                        var c = source.GetPixel(x, y);
                        if (c.A == 0)
                            continue;

                        bool nearBlack = c.R <= blackThreshold && c.G <= blackThreshold && c.B <= blackThreshold;
                        if (!nearBlack)
                            continue;

                        bool touchesTransparent = false;
                        int sumR = 0, sumG = 0, sumB = 0, count = 0;

                        for (int oy = -radius; oy <= radius; oy++)
                        {
                            for (int ox = -radius; ox <= radius; ox++)
                            {
                                if (ox == 0 && oy == 0)
                                    continue;

                                var n = source.GetPixel(x + ox, y + oy);
                                if (n.A == 0)
                                {
                                    touchesTransparent = true;
                                    continue;
                                }

                                if (n.R <= blackThreshold && n.G <= blackThreshold && n.B <= blackThreshold)
                                    continue;

                                sumR += n.R;
                                sumG += n.G;
                                sumB += n.B;
                                count++;
                            }
                        }

                        if (!touchesTransparent)
                            continue;

                        if (count > 0)
                        {
                            image.SetPixel(x, y, Color.FromArgb(c.A, sumR / count, sumG / count, sumB / count));
                        }
                        else
                        {
                            image.SetPixel(x, y, Color.Transparent);
                        }
                    }
                }
            }
        }

        private async Task<T> WaitWithCancel<T>(Task<T> task, Func<bool> isCanceled, Func<Task> onCancel = null)
        {
            while (!task.IsCompleted)
            {
                if (isCanceled != null && isCanceled())
                {
                    if (onCancel != null)
                    {
                        try { await onCancel(); } catch { }
                    }
                    throw new OperationCanceledException();
                }
                await Task.Delay(200);
            }

            return await task;
        }

        private async Task<Bitmap> GenerateUnderlayWithLocalBackend(Bitmap contextImage, Bitmap maskImage, string prompt, AIBackend backend, int targetWidth, int targetHeight)
        {
            // Remove non-ComfyUI backend generation since we're keeping only ComfyUI
            throw new NotSupportedException("Only ComfyUI backend is supported.");
        }

        private Bitmap BlendWithMask(Bitmap original, Bitmap generated, Bitmap mask)
        {
            int w = Math.Min(original.Width, Math.Min(generated.Width, mask.Width));
            int h = Math.Min(original.Height, Math.Min(generated.Height, mask.Height));
            var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var m = mask.GetPixel(x, y);
                    var src = m.R > 127 ? generated.GetPixel(x, y) : original.GetPixel(x, y);
                    result.SetPixel(x, y, src);
                }
            }

            return result;
        }

        private void IntersectMaskWithSelectedTiles(Bitmap inpaintMask, Rectangle bounds, List<SelectedTile> selectedTiles)
        {
            if (inpaintMask == null || selectedTiles == null || selectedTiles.Count == 0)
                return;

            var selectionMask = CreateSelectedTilesMaskImage(inpaintMask.Width, inpaintMask.Height, bounds, selectedTiles);
            try
            {
                for (int y = 0; y < inpaintMask.Height; y++)
                {
                    for (int x = 0; x < inpaintMask.Width; x++)
                    {
                        var a = inpaintMask.GetPixel(x, y);
                        var b = selectionMask.GetPixel(x, y);
                        if (a.R > 200 && b.R > 200)
                            inpaintMask.SetPixel(x, y, Color.White);
                        else
                            inpaintMask.SetPixel(x, y, Color.Black);
                    }
                }
            }
            finally
            {
                selectionMask.Dispose();
            }
        }

        private Bitmap CreateSelectedTilesMaskImage(int width, int height, Rectangle bounds, List<SelectedTile> selectedTiles)
        {
            const int TILE_WIDTH = 44;
            const int TILE_HEIGHT = 44;

            var mask = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(mask))
            {
                g.Clear(Color.Black);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

                int gridWidth = bounds.Width;
                int gridHeight = bounds.Height;
                float isoWidth = (gridWidth + gridHeight) * (TILE_WIDTH / 2f);
                float isoHeight = (gridWidth + gridHeight) * (TILE_HEIGHT / 2f);
                float scaleX = (width * 0.9f) / Math.Max(1f, isoWidth);
                float scaleY = (height * 0.9f) / Math.Max(1f, isoHeight);
                float scale = Math.Min(scaleX, scaleY);

                float scaledTileW = TILE_WIDTH * scale;
                float scaledTileH = TILE_HEIGHT * scale;
                float halfTileW = scaledTileW / 2f;
                float halfTileH = scaledTileH / 2f;
                float centerX = width / 2f;
                float centerY = height / 2f;
                float centerBoundsX = (bounds.Left + bounds.Right - 1) / 2f;
                float centerBoundsY = (bounds.Top + bounds.Bottom - 1) / 2f;

                using (var white = new SolidBrush(Color.White))
                {
                    foreach (var t in selectedTiles)
                    {
                        if (t.X < bounds.Left || t.X >= bounds.Right || t.Y < bounds.Top || t.Y >= bounds.Bottom)
                            continue;

                        float relX = t.X - centerBoundsX;
                        float relY = t.Y - centerBoundsY;
                        float isoX = (relX - relY) * halfTileW;
                        float isoY = (relX + relY) * halfTileH;
                        float sx = centerX + isoX;
                        float sy = centerY + isoY;

                        PointF[] diamond =
                        {
                            new PointF(sx, sy - halfTileH),
                            new PointF(sx + halfTileW, sy),
                            new PointF(sx, sy + halfTileH),
                            new PointF(sx - halfTileW, sy)
                        };

                        g.FillPolygon(white, diamond);
                    }
                }
            }

            return mask;
        }

        private Bitmap RenderMapBoundsToImage(Rectangle bounds, int targetWidth, int targetHeight)
        {
            if (currentMap == null || bounds.Width <= 0 || bounds.Height <= 0)
                return null;

            const int TILE_WIDTH = 44;
            const int TILE_HEIGHT = 44;

            var canvas = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Black);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;

                int gridWidth = bounds.Width;
                int gridHeight = bounds.Height;

                float isoWidth = (gridWidth + gridHeight) * (TILE_WIDTH / 2f);
                float isoHeight = (gridWidth + gridHeight) * (TILE_HEIGHT / 2f);
                float scaleX = (targetWidth * 0.9f) / Math.Max(1f, isoWidth);
                float scaleY = (targetHeight * 0.9f) / Math.Max(1f, isoHeight);
                float scale = Math.Min(scaleX, scaleY);

                float scaledTileW = TILE_WIDTH * scale;
                float scaledTileH = TILE_HEIGHT * scale;
                float halfTileW = scaledTileW / 2f;
                float halfTileH = scaledTileH / 2f;
                float centerX = targetWidth / 2f;
                float centerY = targetHeight / 2f;

                float centerBoundsX = (bounds.Left + bounds.Right - 1) / 2f;
                float centerBoundsY = (bounds.Top + bounds.Bottom - 1) / 2f;

                for (int y = bounds.Top; y < bounds.Bottom; y++)
                {
                    if (y < 0 || y >= currentMap.Height) continue;
                    for (int x = bounds.Left; x < bounds.Right; x++)
                    {
                        if (x < 0 || x >= currentMap.Width) continue;
                        var tile = currentMap.Tiles[x, y];
                        if (tile == null) continue;
                        if (IsNoDraw(tile.TileId)) continue;

                        Image img = null;
                        if (modifiedArtCache.TryGetValue(tile.TileId, out var modified) && modified != null)
                            img = modified;
                        else
                            img = LoadTexMap(tile.TileId) ?? LoadTileImage(tile.TileId);

                        if (img == null) continue;

                        float relX = x - centerBoundsX;
                        float relY = y - centerBoundsY;
                        float isoX = (relX - relY) * halfTileW;
                        float isoY = (relX + relY) * halfTileH;

                        float screenX = centerX + isoX;
                        float screenY = centerY + isoY;
                        var dest = new RectangleF(screenX - scaledTileW / 2f, screenY - scaledTileH / 2f, scaledTileW, scaledTileH);
                        g.DrawImage(img, dest);
                    }
                }
            }

            return canvas;
        }

        private Bitmap CreateBlackVoidMask(Bitmap source, int threshold, int dilatePixels)
        {
            var mask = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            bool[,] white = new bool[source.Width, source.Height];

            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    var c = source.GetPixel(x, y);
                    white[x, y] = c.R <= threshold && c.G <= threshold && c.B <= threshold;
                }
            }

            for (int pass = 0; pass < dilatePixels; pass++)
            {
                var next = (bool[,])white.Clone();
                for (int y = 1; y < source.Height - 1; y++)
                {
                    for (int x = 1; x < source.Width - 1; x++)
                    {
                        if (white[x, y]) continue;
                        bool near = false;
                        for (int oy = -1; oy <= 1 && !near; oy++)
                            for (int ox = -1; ox <= 1; ox++)
                                if (white[x + ox, y + oy]) { near = true; break; }
                        if (near) next[x, y] = true;
                    }
                }
                white = next;
            }

            using (var g = Graphics.FromImage(mask))
            {
                g.Clear(Color.Black);
            }

            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    if (white[x, y]) mask.SetPixel(x, y, Color.White);
                }
            }

            return mask;
        }

        private int TryFindFreeGumpId(string folder, int startId, int endId)
        {
            try
            {
                if (string.IsNullOrEmpty(folder)) return -1;
                string idxPath = Path.Combine(folder, "gumpidx.mul");
                if (!File.Exists(idxPath)) return -1;

                using (var fs = new FileStream(idxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var br = new BinaryReader(fs))
                {
                    for (int id = startId; id <= endId; id++)
                    {
                        long pos = (long)id * 12;
                        if (pos + 12 > fs.Length) return id;
                        fs.Seek(pos, SeekOrigin.Begin);
                        int offset = br.ReadInt32();
                        int length = br.ReadInt32();
                        br.ReadInt32();
                        if (offset < 0 || length <= 0)
                            return id;
                    }
                }
            }
            catch { }

            return -1;
        }

        private bool TrySaveUnderlayToGump(Bitmap image, out int savedGumpId, out string usedFolder)
        {
            savedGumpId = -1;
            usedFolder = null;

            const int UNDERLAY_GUMP_MIN = 0x7000;
            const int UNDERLAY_GUMP_MAX = 0xFFFE;

            var folders = new List<string>();
            if (!string.IsNullOrWhiteSpace(mapSourceFolder)) folders.Add(mapSourceFolder);
            if (!string.IsNullOrWhiteSpace(mulFolderPath) && !folders.Contains(mulFolderPath, StringComparer.OrdinalIgnoreCase))
                folders.Add(mulFolderPath);

            foreach (var folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    continue;

                try
                {
                    var existingIds = new HashSet<int>(GumpArtReader.GetValidGumpIds(folder));

                    // Prefer IDs that already exist in gump lookup data.
                    // JarJar's updateGump endpoint requires IDs present in gumps.jargump.
                    var occupiedByUnderlays = new HashSet<int>(underlayPlanes
                        .Where(p => p != null && p.GumpId >= UNDERLAY_GUMP_MIN && p.GumpId <= UNDERLAY_GUMP_MAX)
                        .Select(p => p.GumpId));

                    for (int id = UNDERLAY_GUMP_MIN; id <= UNDERLAY_GUMP_MAX; id++)
                    {
                        if (!existingIds.Contains(id))
                            continue;
                        if (occupiedByUnderlays.Contains(id))
                            continue;

                        if (GumpArtWriter.SaveGump(folder, id, image))
                        {
                            savedGumpId = id;
                            usedFolder = folder;
                            return true;
                        }
                    }

                    // Fallback for local-only use: create a new free slot if no existing slot is available.
                    for (int id = UNDERLAY_GUMP_MIN; id <= UNDERLAY_GUMP_MAX; id++)
                    {
                        if (existingIds.Contains(id))
                            continue;

                        if (GumpArtWriter.SaveGump(folder, id, image))
                        {
                            savedGumpId = id;
                            usedFolder = folder;
                            return true;
                        }
                    }
                }
                catch
                {
                    // Try next folder candidate.
                }
            }

            return false;
        }

        private void ApplySelectionMaskToUnderlay(Bitmap underlayImage, Rectangle bounds, List<SelectedTile> selectedTilesInBounds)
        {
            if (underlayImage == null || selectedTilesInBounds == null || selectedTilesInBounds.Count == 0)
                return;

            const int TILE_WIDTH = 44;
            const int TILE_HEIGHT = 44;

            var selected = selectedTilesInBounds
                .Where(t => t.X >= bounds.Left && t.X < bounds.Right && t.Y >= bounds.Top && t.Y < bounds.Bottom)
                .ToList();
            if (selected.Count == 0)
                return;

            using (var mask = new Bitmap(underlayImage.Width, underlayImage.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(mask))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

                    int gridWidth = bounds.Width;
                    int gridHeight = bounds.Height;
                    float isoWidth = (gridWidth + gridHeight) * (TILE_WIDTH / 2f);
                    float isoHeight = (gridWidth + gridHeight) * (TILE_HEIGHT / 2f);
                    float scaleX = (underlayImage.Width * 0.9f) / Math.Max(1f, isoWidth);
                    float scaleY = (underlayImage.Height * 0.9f) / Math.Max(1f, isoHeight);
                    float scale = Math.Min(scaleX, scaleY);
                    float scaledTileW = TILE_WIDTH * scale;
                    float scaledTileH = TILE_HEIGHT * scale;
                    float halfTileW = scaledTileW / 2f;
                    float halfTileH = scaledTileH / 2f;
                    float centerX = underlayImage.Width / 2f;
                    float centerY = underlayImage.Height / 2f;
                    float centerBoundsX = (bounds.Left + bounds.Right - 1) / 2f;
                    float centerBoundsY = (bounds.Top + bounds.Bottom - 1) / 2f;

                    using (var white = new SolidBrush(Color.White))
                    {
                        foreach (var t in selected)
                        {
                            float relX = t.X - centerBoundsX;
                            float relY = t.Y - centerBoundsY;
                            float isoX = (relX - relY) * halfTileW;
                            float isoY = (relX + relY) * halfTileH;
                            float sx = centerX + isoX;
                            float sy = centerY + isoY;

                            PointF[] diamond =
                            {
                                new PointF(sx, sy - halfTileH),
                                new PointF(sx + halfTileW, sy),
                                new PointF(sx, sy + halfTileH),
                                new PointF(sx - halfTileW, sy)
                            };
                            g.FillPolygon(white, diamond);
                        }
                    }
                }

                for (int y = 0; y < underlayImage.Height; y++)
                {
                    for (int x = 0; x < underlayImage.Width; x++)
                    {
                        var m = mask.GetPixel(x, y);
                        if (m.A == 0)
                            underlayImage.SetPixel(x, y, Color.Transparent);
                    }
                }
            }
        }

        private void DrawUnderlayPlanes(Graphics g, int viewW, int viewH, float zoom, float halfTileW, float halfTileH, float centerX, float centerY)
        {
            if (!showUnderlayPlanes) return;
            if (underlayPlanes.Count == 0) return;

            const int TILE_WIDTH = 44;
            const int TILE_HEIGHT = 44;

            foreach (var plane in underlayPlanes)
            {
                if (plane?.Image == null) continue;
                if (plane.Facet != (facetComboBox?.SelectedIndex ?? 0)) continue;

                var b = plane.Bounds;
                float gridWidth = b.Width;
                float gridHeight = b.Height;
                float isoWidth = (gridWidth + gridHeight) * (TILE_WIDTH / 2f) * zoom;
                float isoHeight = (gridWidth + gridHeight) * (TILE_HEIGHT / 2f) * zoom;

                float drawW = (isoWidth / 0.9f) * Math.Max(0.1f, plane.Scale);
                float drawH = (isoHeight / 0.9f) * Math.Max(0.1f, plane.Scale);

                float cx = (b.Left + b.Right - 1) / 2f + plane.OffsetX;
                float cy = (b.Top + b.Bottom - 1) / 2f + plane.OffsetY;

                float relX = cx - cameraX;
                float relY = cy - cameraY;
                float isoX = (relX - relY) * halfTileW;
                float isoY = (relX + relY) * halfTileH;
                float screenX = centerX + isoX;
                float screenY = centerY + isoY;

                float avgZ = GetAverageLandZForBounds(b);
                screenY -= avgZ * Z_SCALE * zoom;

                float drawX = screenX - drawW / 2f;
                float drawY = screenY - drawH / 2f;

                if (drawX > viewW || drawY > viewH || drawX + drawW < 0 || drawY + drawH < 0)
                    continue;

                if (Math.Abs(plane.RotationDeg) < 0.01f)
                {
                    g.DrawImage(plane.Image, drawX, drawY, drawW, drawH);
                }
                else
                {
                    var state = g.Save();
                    g.TranslateTransform(screenX, screenY);
                    g.RotateTransform(plane.RotationDeg);
                    g.DrawImage(plane.Image, -drawW / 2f, -drawH / 2f, drawW, drawH);
                    g.Restore(state);
                }
            }
        }

        private void ClearAllUnderlays()
        {
            foreach (var p in underlayPlanes)
                p?.Image?.Dispose();
            underlayPlanes.Clear();
            GenerateMapImage();
        }

        private void RemoveUnderlaysInSelection()
        {
            var allTiles = new List<SelectedTile>();
            allTiles.AddRange(replaceTiles);
            allTiles.AddRange(contextTiles);
            allTiles = allTiles.Distinct().ToList();
            if (allTiles.Count == 0)
            {
                MessageBox.Show(this, "Select tiles first.", "Underlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int facet = facetComboBox?.SelectedIndex ?? 0;
            int minX = allTiles.Min(t => t.X);
            int maxX = allTiles.Max(t => t.X);
            int minY = allTiles.Min(t => t.Y);
            int maxY = allTiles.Max(t => t.Y);
            var selBounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);

            int removed = 0;
            for (int i = underlayPlanes.Count - 1; i >= 0; i--)
            {
                var p = underlayPlanes[i];
                if (p.Facet != facet) continue;
                if (!p.Bounds.IntersectsWith(selBounds)) continue;

                p.Image?.Dispose();
                underlayPlanes.RemoveAt(i);
                removed++;
            }

            GenerateMapImage();
            MessageBox.Show(this, $"Removed {removed} underlay plane(s).", "Underlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ClearUnderlayAreaInSelection()
        {
            var allTiles = new List<SelectedTile>();
            allTiles.AddRange(replaceTiles);
            allTiles.AddRange(contextTiles);
            allTiles = allTiles.Distinct().ToList();
            if (allTiles.Count == 0)
            {
                MessageBox.Show(this, "Select tiles first.", "Underlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            const int TILE_WIDTH = 44;
            const int TILE_HEIGHT = 44;
            int facet = facetComboBox?.SelectedIndex ?? 0;
            int cleared = 0;

            foreach (var plane in underlayPlanes)
            {
                if (plane == null || plane.Image == null) continue;
                if (plane.Facet != facet) continue;

                int gridWidth = plane.Bounds.Width;
                int gridHeight = plane.Bounds.Height;
                float isoWidth = (gridWidth + gridHeight) * (TILE_WIDTH / 2f);
                float isoHeight = (gridWidth + gridHeight) * (TILE_HEIGHT / 2f);
                float scaleX = (plane.Image.Width * 0.9f) / Math.Max(1f, isoWidth);
                float scaleY = (plane.Image.Height * 0.9f) / Math.Max(1f, isoHeight);
                float scale = Math.Min(scaleX, scaleY);
                float scaledTileW = TILE_WIDTH * scale;
                float scaledTileH = TILE_HEIGHT * scale;
                float halfTileW = scaledTileW / 2f;
                float halfTileH = scaledTileH / 2f;
                float centerX = plane.Image.Width / 2f;
                float centerY = plane.Image.Height / 2f;
                float centerBoundsX = (plane.Bounds.Left + plane.Bounds.Right - 1) / 2f;
                float centerBoundsY = (plane.Bounds.Top + plane.Bounds.Bottom - 1) / 2f;

                using (var g = Graphics.FromImage(plane.Image))
                {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                    using (var clearBrush = new SolidBrush(Color.Transparent))
                    {
                        foreach (var t in allTiles)
                        {
                            if (t.X < plane.Bounds.Left || t.X >= plane.Bounds.Right || t.Y < plane.Bounds.Top || t.Y >= plane.Bounds.Bottom)
                                continue;

                            float relX = t.X - centerBoundsX;
                            float relY = t.Y - centerBoundsY;
                            float isoX = (relX - relY) * halfTileW;
                            float isoY = (relX + relY) * halfTileH;
                            float sx = centerX + isoX;
                            float sy = centerY + isoY;

                            PointF[] diamond = new PointF[]
                            {
                                new PointF(sx, sy - halfTileH),
                                new PointF(sx + halfTileW, sy),
                                new PointF(sx, sy + halfTileH),
                                new PointF(sx - halfTileW, sy)
                            };

                            g.FillPolygon(clearBrush, diamond);
                            cleared++;
                        }
                    }
                }
            }

            GenerateMapImage();
            MessageBox.Show(this, $"Cleared {cleared} tile area(s) from underlay.", "Underlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async Task GenerateLandOverlayWithAI()
        {
            var allTiles = new List<SelectedTile>();
            allTiles.AddRange(replaceTiles);
            allTiles.AddRange(contextTiles);
            allTiles = allTiles.Distinct().ToList();
            if (allTiles.Count == 0)
            {
                MessageBox.Show(this, "Select tiles first.", "Generate Overlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool cancelRequested = false;
            var progressForm = new Form
            {
                Text = "Generating Overlay...",
                Width = 520,
                Height = 180,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            HolographicTheme.ApplyToForm(progressForm);

            var progressLabel = new Label { Left = 16, Top = 16, Width = 470, Height = 36, Text = "Preparing overlay generation..." };
            HolographicTheme.ApplyToLabel(progressLabel);
            progressForm.Controls.Add(progressLabel);
            var progressBar = new ProgressBar { Left = 16, Top = 58, Width = 470, Height = 20, Minimum = 0, Maximum = 100, Value = 0 };
            progressForm.Controls.Add(progressBar);
            var cancelButton = new Button { Text = "Cancel", Left = 386, Top = 92, Width = 100, Height = 28 };
            cancelButton.Click += (s, e) => { cancelRequested = true; cancelButton.Enabled = false; progressLabel.Text = "Cancelling..."; };
            HolographicTheme.ApplyToButton(cancelButton, ButtonStyle.Danger);
            progressForm.Controls.Add(cancelButton);

            Action<string, int> setProgress = (text, value) =>
            {
                progressLabel.Text = text;
                progressBar.Value = Math.Max(progressBar.Minimum, Math.Min(progressBar.Maximum, value));
                Application.DoEvents();
            };

            progressForm.Show(this);
            Application.DoEvents();

            int minX = allTiles.Min(t => t.X);
            int maxX = allTiles.Max(t => t.X);
            int minY = allTiles.Min(t => t.Y);
            int maxY = allTiles.Max(t => t.Y);
            var bounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);

            int targetWidth = ResolutionWidth;
            int targetHeight = ResolutionHeight;

            setProgress("Rendering selected area...", 8);
            var contextImage = RenderMapBoundsToImage(bounds, targetWidth, targetHeight);
            if (contextImage == null)
            {
                progressForm.Close();
                MessageBox.Show(this, "Failed to render map area.", "Generate Overlay", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            float focusSize = ((float)(overlayFocusNumeric?.Value ?? 70m)) / 100f;
            int feather = (int)(overlayFeatherNumeric?.Value ?? 36m);
            bool sparse = overlayUseSparseDeltaCheckBox?.Checked ?? true;
            int diffThreshold = (int)(overlayDiffThresholdNumeric?.Value ?? 8m);
            double alphaScale = (double)(overlayAlphaScaleNumeric?.Value ?? 140m) / 100.0;

            var selectionMask = CreateSelectedTilesMaskImage(targetWidth, targetHeight, bounds, allTiles);
            var maskImage = CreateFocusedOverlayMask(selectionMask, focusSize, feather);
            selectionMask.Dispose();

            try
            {
                if (cancelRequested)
                    throw new OperationCanceledException();

                string prompt = string.IsNullOrWhiteSpace(Prompt)
                    ? "small subtle terrain detail overlay, preserve base terrain, seamless isometric map"
                    : Prompt;

                Bitmap generated;
                var selectedBackend = SelectedBackend;

                if (selectedBackend == AIBackend.ComfyUI)
                {
                    setProgress("Connecting to ComfyUI...", 20);
                    comfyClient = new ComfyUIClient(ComfyUIUrl);

                    byte[] contextBytes;
                    using (var ms = new MemoryStream()) { contextImage.Save(ms, ImageFormat.Png); contextBytes = ms.ToArray(); }
                    byte[] maskBytes;
                    using (var ms = new MemoryStream()) { maskImage.Save(ms, ImageFormat.Png); maskBytes = ms.ToArray(); }

                    setProgress("Uploading context...", 30);
                    var uploadedImageName = await WaitWithCancel(comfyClient.UploadImage(contextBytes, "overlay_context.png"), () => cancelRequested);
                    setProgress("Uploading mask...", 36);
                    var uploadedMaskName = await WaitWithCancel(comfyClient.UploadImage(maskBytes, "overlay_mask.png"), () => cancelRequested);

                    bool useFlux2KleinWorkflow =
                        !string.IsNullOrWhiteSpace(Checkpoint) &&
                        (Checkpoint.IndexOf("flux-2-klein", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         Checkpoint.IndexOf("flux2", StringComparison.OrdinalIgnoreCase) >= 0);

                    var workflow = useFlux2KleinWorkflow
                        ? Flux2KleinInpaintWorkflow.CreateWorkflow(
                            prompt, NegativePrompt, uploadedImageName, uploadedMaskName,
                            targetWidth, targetHeight, Steps, CFG, Seed, Sampler, unetName: Checkpoint)
                        : InpaintWorkflow.CreateWorkflow(
                            prompt, NegativePrompt, uploadedImageName, uploadedMaskName,
                            targetWidth, targetHeight, Steps, CFG, Denoise, Seed, Sampler, Scheduler, Checkpoint);

                    setProgress("Queueing overlay workflow...", 45);
                    var promptId = await WaitWithCancel(comfyClient.QueuePrompt(workflow), () => cancelRequested, async () => await comfyClient.Interrupt());
                    if (string.IsNullOrEmpty(promptId))
                        throw new Exception("Failed to queue ComfyUI prompt.");

                    setProgress("Generating overlay...", 60);
                    var images = await WaitWithCancel(comfyClient.GetGeneratedImages(promptId, maxAttempts: 300, pollIntervalMs: 1000), () => cancelRequested, async () => await comfyClient.Interrupt());
                    if (images.Count == 0)
                        throw new Exception("No overlay image generated.");

                    setProgress("Downloading result...", 82);
                    var imageData = await WaitWithCancel(comfyClient.DownloadImage(images[0]), () => cancelRequested, async () => await comfyClient.Interrupt());
                    if (imageData == null || imageData.Length == 0)
                        throw new Exception("Download from ComfyUI failed");
                    using (var ms = new MemoryStream(imageData))
                    using (var img = Image.FromStream(ms))
                        generated = new Bitmap(img);
                }
                else
                {
                    setProgress("Preparing local backend...", 20);
                    bool available = await ImageGeneratorFactory.EnsureBackendAvailable(selectedBackend, this);
                    if (!available)
                        return;

                    setProgress("Generating overlay locally...", 60);
                    generated = await GenerateUnderlayWithLocalBackend(contextImage, maskImage, prompt, selectedBackend, targetWidth, targetHeight);
                    if (generated == null)
                        throw new Exception("Local backend failed to generate overlay.");
                }

                if (cancelRequested)
                    throw new OperationCanceledException();

                ApplySelectionMaskToUnderlay(generated, bounds, allTiles);
                if (sparse)
                {
                    var deltaOverlay = ExtractOverlayDelta(contextImage, generated, maskImage, diffThreshold, alphaScale);
                    generated.Dispose();
                    generated = deltaOverlay;
                }
                else
                {
                    ApplyGlobalAlphaToImage(generated, alphaScale);
                }

                setProgress("Saving overlay...", 94);
                string overlayFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Overlays");
                if (!Directory.Exists(overlayFolder))
                    Directory.CreateDirectory(overlayFolder);

                string filePath = Path.Combine(overlayFolder,
                    $"overlay_f{(facetComboBox?.SelectedIndex ?? 0)}_{bounds.X}_{bounds.Y}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                generated.Save(filePath, ImageFormat.Png);

                MarkOverlayModified(facetComboBox?.SelectedIndex ?? 0, bounds, generated, filePath);

                overlayPlanes.Add(new OverlayPlane
                {
                    Facet = facetComboBox?.SelectedIndex ?? 0,
                    Bounds = bounds,
                    Image = new Bitmap(generated),
                    FilePath = filePath,
                    IsFromServer = false
                });

                generated.Dispose();
                GenerateMapImage();
                progressForm.Close();

                MessageBox.Show(this,
                    $"Overlay generated and saved as PNG.\nPNG: {filePath}",
                    "Overlay Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                progressForm.Close();
                MessageBox.Show(this, "Overlay generation was cancelled.", "Generate Overlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                progressForm.Close();
                MessageBox.Show(this,
                    $"Overlay generation failed: {ex.Message}",
                    "Generate Overlay",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                contextImage.Dispose();
                maskImage.Dispose();
            }
        }

        private Bitmap CreateFocusedOverlayMask(Bitmap selectedMask, float focusSize, int featherPixels)
        {
            if (selectedMask == null)
                return null;

            var focused = new Bitmap(selectedMask.Width, selectedMask.Height, PixelFormat.Format32bppArgb);

            float cx = selectedMask.Width / 2f;
            float cy = selectedMask.Height / 2f;
            float rx = Math.Max(8f, selectedMask.Width * focusSize * 0.5f);
            float ry = Math.Max(8f, selectedMask.Height * focusSize * 0.5f);
            float featherNorm = featherPixels / Math.Max(1f, Math.Min(rx, ry));

            for (int y = 0; y < selectedMask.Height; y++)
            {
                for (int x = 0; x < selectedMask.Width; x++)
                {
                    var s = selectedMask.GetPixel(x, y);
                    if (s.R < 128)
                    {
                        focused.SetPixel(x, y, Color.Black);
                        continue;
                    }

                    float dx = (x - cx) / rx;
                    float dy = (y - cy) / ry;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);

                    float w;
                    if (d <= 1f) w = 1f;
                    else if (d <= 1f + featherNorm) w = 1f - ((d - 1f) / Math.Max(0.0001f, featherNorm));
                    else w = 0f;

                    int v = (int)Math.Max(0, Math.Min(255, w * 255f));
                    focused.SetPixel(x, y, Color.FromArgb(255, v, v, v));
                }
            }

            return focused;
        }

        private Bitmap ExtractOverlayDelta(Bitmap original, Bitmap generated, Bitmap mask, int diffThreshold, double alphaScale)
        {
            int w = Math.Min(original.Width, Math.Min(generated.Width, mask.Width));
            int h = Math.Min(original.Height, Math.Min(generated.Height, mask.Height));
            var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var m = mask.GetPixel(x, y);
                    if (m.R < 8)
                    {
                        result.SetPixel(x, y, Color.Transparent);
                        continue;
                    }

                    var o = original.GetPixel(x, y);
                    var g = generated.GetPixel(x, y);

                    int dr = Math.Abs(g.R - o.R);
                    int dg = Math.Abs(g.G - o.G);
                    int db = Math.Abs(g.B - o.B);
                    int d = (dr + dg + db) / 3;

                    if (d < diffThreshold)
                    {
                        result.SetPixel(x, y, Color.Transparent);
                        continue;
                    }

                    int a = (int)Math.Max(0, Math.Min(255, 40 + (d * alphaScale)));
                    a = (a * m.R) / 255;
                    result.SetPixel(x, y, Color.FromArgb(a, g.R, g.G, g.B));
                }
            }

            return result;
        }

        private void ApplyGlobalAlphaToImage(Bitmap image, double alphaScale)
        {
            if (image == null) return;
            int alpha = (int)Math.Max(0, Math.Min(255, alphaScale * 255.0));

            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    var c = image.GetPixel(x, y);
                    if (c.A == 0) continue;
                    int a = (c.A * alpha) / 255;
                    image.SetPixel(x, y, Color.FromArgb(a, c.R, c.G, c.B));
                }
            }
        }

        private void DrawLandOverlayPlanes(Graphics g, int viewW, int viewH, float zoom, float halfTileW, float halfTileH, float centerX, float centerY)
        {
            if (!showOverlayPlanes) return;
            if (overlayPlanes.Count == 0) return;

            const int TILE_WIDTH = 44;
            const int TILE_HEIGHT = 44;

            foreach (var plane in overlayPlanes)
            {
                if (plane?.Image == null) continue;
                if (plane.Facet != (facetComboBox?.SelectedIndex ?? 0)) continue;

                var b = plane.Bounds;
                float gridWidth = b.Width;
                float gridHeight = b.Height;
                float isoWidth = (gridWidth + gridHeight) * (TILE_WIDTH / 2f) * zoom;
                float isoHeight = (gridWidth + gridHeight) * (TILE_HEIGHT / 2f) * zoom;

                float drawW = (isoWidth / 0.9f) * Math.Max(0.1f, plane.Scale);
                float drawH = (isoHeight / 0.9f) * Math.Max(0.1f, plane.Scale);

                float cx = (b.Left + b.Right - 1) / 2f + plane.OffsetX;
                float cy = (b.Top + b.Bottom - 1) / 2f + plane.OffsetY;

                float relX = cx - cameraX;
                float relY = cy - cameraY;
                float isoX = (relX - relY) * halfTileW;
                float isoY = (relX + relY) * halfTileH;
                float screenX = centerX + isoX;
                float screenY = centerY + isoY;

                float avgZ = GetAverageLandZForBounds(b);
                screenY -= avgZ * Z_SCALE * zoom;

                float drawX = screenX - drawW / 2f;
                float drawY = screenY - drawH / 2f;

                if (drawX > viewW || drawY > viewH || drawX + drawW < 0 || drawY + drawH < 0)
                    continue;

                if (Math.Abs(plane.RotationDeg) < 0.01f)
                {
                    g.DrawImage(plane.Image, drawX, drawY, drawW, drawH);
                }
                else
                {
                    var state = g.Save();
                    g.TranslateTransform(screenX, screenY);
                    g.RotateTransform(plane.RotationDeg);
                    g.DrawImage(plane.Image, -drawW / 2f, -drawH / 2f, drawW, drawH);
                    g.Restore(state);
                }
            }
        }

        private void ClearAllOverlays()
        {
            foreach (var p in overlayPlanes)
            {
                if (p != null)
                    MarkOverlayRemoved(p.Facet, p.Bounds);
                p?.Image?.Dispose();
            }
            overlayPlanes.Clear();
            GenerateMapImage();
        }

        private void RemoveOverlaysInSelection()
        {
            var allTiles = new List<SelectedTile>();
            allTiles.AddRange(replaceTiles);
            allTiles.AddRange(contextTiles);
            allTiles = allTiles.Distinct().ToList();
            if (allTiles.Count == 0)
            {
                MessageBox.Show(this, "Select tiles first.", "Overlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int facet = facetComboBox?.SelectedIndex ?? 0;
            int minX = allTiles.Min(t => t.X);
            int maxX = allTiles.Max(t => t.X);
            int minY = allTiles.Min(t => t.Y);
            int maxY = allTiles.Max(t => t.Y);
            var selBounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);

            int removed = 0;
            for (int i = overlayPlanes.Count - 1; i >= 0; i--)
            {
                var p = overlayPlanes[i];
                if (p.Facet != facet) continue;
                if (!p.Bounds.IntersectsWith(selBounds)) continue;

                MarkOverlayRemoved(p.Facet, p.Bounds);
                p.Image?.Dispose();
                overlayPlanes.RemoveAt(i);
                removed++;
            }

            GenerateMapImage();
            MessageBox.Show(this, $"Removed {removed} overlay plane(s).", "Overlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ClearOverlayAreaInSelection()
        {
            var allTiles = new List<SelectedTile>();
            allTiles.AddRange(replaceTiles);
            allTiles.AddRange(contextTiles);
            allTiles = allTiles.Distinct().ToList();
            if (allTiles.Count == 0)
            {
                MessageBox.Show(this, "Select tiles first.", "Overlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            const int TILE_WIDTH = 44;
            const int TILE_HEIGHT = 44;
            int facet = facetComboBox?.SelectedIndex ?? 0;
            int cleared = 0;

            foreach (var plane in overlayPlanes)
            {
                if (plane == null || plane.Image == null) continue;
                if (plane.Facet != facet) continue;

                bool planeChanged = false;

                int gridWidth = plane.Bounds.Width;
                int gridHeight = plane.Bounds.Height;
                float isoWidth = (gridWidth + gridHeight) * (TILE_WIDTH / 2f);
                float isoHeight = (gridWidth + gridHeight) * (TILE_HEIGHT / 2f);
                float scaleX = (plane.Image.Width * 0.9f) / Math.Max(1f, isoWidth);
                float scaleY = (plane.Image.Height * 0.9f) / Math.Max(1f, isoHeight);
                float scale = Math.Min(scaleX, scaleY);
                float scaledTileW = TILE_WIDTH * scale;
                float scaledTileH = TILE_HEIGHT * scale;
                float halfTileW = scaledTileW / 2f;
                float halfTileH = scaledTileH / 2f;
                float centerX = plane.Image.Width / 2f;
                float centerY = plane.Image.Height / 2f;
                float centerBoundsX = (plane.Bounds.Left + plane.Bounds.Right - 1) / 2f;
                float centerBoundsY = (plane.Bounds.Top + plane.Bounds.Bottom - 1) / 2f;

                using (var g = Graphics.FromImage(plane.Image))
                {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                    using (var clearBrush = new SolidBrush(Color.Transparent))
                    {
                        foreach (var t in allTiles)
                        {
                            if (t.X < plane.Bounds.Left || t.X >= plane.Bounds.Right || t.Y < plane.Bounds.Top || t.Y >= plane.Bounds.Bottom)
                                continue;

                            float relX = t.X - centerBoundsX;
                            float relY = t.Y - centerBoundsY;
                            float isoX = (relX - relY) * halfTileW;
                            float isoY = (relX + relY) * halfTileH;
                            float sx = centerX + isoX;
                            float sy = centerY + isoY;

                            PointF[] diamond =
                            {
                                new PointF(sx, sy - halfTileH),
                                new PointF(sx + halfTileW, sy),
                                new PointF(sx, sy + halfTileH),
                                new PointF(sx - halfTileW, sy)
                            };

                            g.FillPolygon(clearBrush, diamond);
                            cleared++;
                            planeChanged = true;
                        }
                    }
                }

                if (planeChanged)
                    MarkOverlayModified(plane.Facet, plane.Bounds, plane.Image, plane.FilePath);
            }

            GenerateMapImage();
            MessageBox.Show(this, $"Cleared {cleared} tile area(s) from overlay.", "Overlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        internal void AddPainterImageToMapLayer(Bitmap image, bool asOverlay)
        {
            if (image == null || currentMap == null)
                return;

            int sumTiles = Math.Max(8, (int)Math.Round((image.Width * 0.9f) / 22f));
            int side = Math.Max(4, sumTiles / 2);

            int left = Math.Max(0, cameraX - side / 2);
            int top = Math.Max(0, cameraY - side / 2);
            int width = Math.Min(currentMap.Width - left, side);
            int height = Math.Min(currentMap.Height - top, side);

            var planeBounds = new Rectangle(left, top, Math.Max(1, width), Math.Max(1, height));

            if (asOverlay)
            {
                var newOverlay = new OverlayPlane
                {
                    Facet = facetComboBox?.SelectedIndex ?? 0,
                    Bounds = planeBounds,
                    Image = new Bitmap(image),
                    FilePath = string.Empty,
                    IsFromServer = false,
                    Scale = 1f,
                    RotationDeg = 0f,
                    OffsetX = 0f,
                    OffsetY = 0f
                };
                overlayPlanes.Add(newOverlay);
                MarkOverlayModified(newOverlay.Facet, newOverlay.Bounds, newOverlay.Image, newOverlay.FilePath);
                _lastPainterPlaneIsOverlay = true;
                _lastPainterPlaneIndex = overlayPlanes.Count - 1;
            }
            else
            {
                underlayPlanes.Add(new UnderlayPlane
                {
                    Facet = facetComboBox?.SelectedIndex ?? 0,
                    Bounds = planeBounds,
                    Image = new Bitmap(image),
                    GumpId = -1,
                    FilePath = string.Empty,
                    IsFromServer = false,
                    Scale = 1f,
                    RotationDeg = 0f,
                    OffsetX = 0f,
                    OffsetY = 0f
                });
                _lastPainterPlaneIsOverlay = false;
                _lastPainterPlaneIndex = underlayPlanes.Count - 1;
            }

            GenerateMapImage();
            statusLabel.Text = "Painter image sent. Use Alt+Arrows to move, Alt+[ / ] to scale, Alt+, / . to rotate.";
        }

        private void AdjustLastPainterPlane(float deltaX, float deltaY, float scaleMul, float deltaRot)
        {
            if (_lastPainterPlaneIndex < 0) return;

            if (_lastPainterPlaneIsOverlay)
            {
                if (_lastPainterPlaneIndex >= overlayPlanes.Count) return;
                var p = overlayPlanes[_lastPainterPlaneIndex];
                p.OffsetX += deltaX;
                p.OffsetY += deltaY;
                p.Scale = Math.Max(0.1f, Math.Min(10f, p.Scale * scaleMul));
                p.RotationDeg += deltaRot;
            }
            else
            {
                if (_lastPainterPlaneIndex >= underlayPlanes.Count) return;
                var p = underlayPlanes[_lastPainterPlaneIndex];
                p.OffsetX += deltaX;
                p.OffsetY += deltaY;
                p.Scale = Math.Max(0.1f, Math.Min(10f, p.Scale * scaleMul));
                p.RotationDeg += deltaRot;
            }

            GenerateMapImage();
        }

        /// <summary>
        /// Extract tiles from generated image and apply to tile cache
        /// </summary>
        private async Task ExtractAndApplyTiles(Image generatedImage, Rectangle bounds, List<SelectedTile> tilesToReplace, int targetWidth = 512, int targetHeight = 512)
        {
            const int TILE_WIDTH = 44;
            const int TILE_HEIGHT = 44;
            const int TILE_SIZE = 44;

            // Calculate same scale as rendering
            int gridWidth = bounds.Width;
            int gridHeight = bounds.Height;

            float isoWidth = (gridWidth + gridHeight) * (TILE_WIDTH / 2f);
            float isoHeight = (gridWidth + gridHeight) * (TILE_HEIGHT / 2f);

            float scaleX = (targetWidth * 0.9f) / isoWidth;
            float scaleY = (targetHeight * 0.9f) / isoHeight;
            float scale = Math.Min(scaleX, scaleY);

            float scaledTileW = TILE_WIDTH * scale;
            float scaledTileH = TILE_HEIGHT * scale;
            float halfTileW = scaledTileW / 2f;
            float halfTileH = scaledTileH / 2f;

            float centerX = targetWidth / 2f;
            float centerY = targetHeight / 2f;

            // Extract each replacement tile
            foreach (var tile in tilesToReplace)
            {
                // Calculate tile position in generated image (SAME math as rendering)
                float centerBoundsX = (bounds.Left + bounds.Right - 1) / 2f;
                float centerBoundsY = (bounds.Top + bounds.Bottom - 1) / 2f;

                float relX = tile.X - centerBoundsX;
                float relY = tile.Y - centerBoundsY;

                float isoX = (relX + relY) * halfTileW;
                float isoY = (relX - relY) * halfTileH;

                float screenX = centerX + isoX;
                float screenY = centerY + isoY;

                float drawX = screenX - scaledTileW / 2f;
                float drawY = screenY - scaledTileH / 2f;

                // Extract this tile area from the generated image
                var extractedTile = new Bitmap(TILE_SIZE, TILE_SIZE, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(extractedTile))
                {
                    // Use NearestNeighbor for sharp, crisp pixel art quality
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;

                    // Draw the section of the generated image that corresponds to this tile
                    g.DrawImage(generatedImage,
                        new RectangleF(0, 0, TILE_SIZE, TILE_SIZE),
                        new RectangleF(drawX, drawY, scaledTileW, scaledTileH),
                        GraphicsUnit.Pixel);
                }

                // Apply diamond mask
                var maskedTile = ApplyDiamondMask(extractedTile);
                extractedTile.Dispose();

                // Update art cache
                if (!originalArtCache.ContainsKey(tile.TileId))
                {
                    // Store original before replacing
                    var original = LoadTileImage(tile.TileId);
                    if (original != null)
                    {
                        originalArtCache[tile.TileId] = new Bitmap(original);
                    }
                }

                artCache[tile.TileId] = maskedTile;
                modifiedArtCache[tile.TileId] = maskedTile; // Update modified cache

                // Mark as modified for save tracking
                MarkArtModified(tile.TileId, maskedTile);
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Replace selected statics with AI-generated art
        /// </summary>
        private async Task ReplaceSelectedStaticsWithAI()
        {
            if (selectedStatics.Count == 0) return;

            // Check which backend is selected
            var selectedBackend = SelectedBackend;
            
            // Validate backend availability
            if (selectedBackend == AIBackend.ComfyUI)
            {
                comfyClient = new ComfyUIClient(ComfyUIUrl);
            }
            else
            {
                bool available = await ImageGeneratorFactory.EnsureBackendAvailable(selectedBackend, this);
                if (!available) return;
            }

            int targetWidth = ResolutionWidth;
            int targetHeight = ResolutionHeight;
            string backendName = ImageGeneratorFactory.GetBackendName(selectedBackend);

            // Show progress dialog
            var progressForm = new Form
            {
                Text = $"AI Static Replacement ({backendName})",
                Width = 600,
                Height = 250,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            HolographicTheme.ApplyToForm(progressForm);

            var progressLabel = new Label
            {
                Text = "Preparing...",
                Location = new Point(20, 20),
                Width = 550,
                Height = 30
            };
            HolographicTheme.ApplyToLabel(progressLabel);
            progressForm.Controls.Add(progressLabel);

            var progressBar = new ProgressBar
            {
                Location = new Point(20, 60),
                Width = 550,
                Height = 25,
                Style = ProgressBarStyle.Continuous,
                Maximum = 100
            };
            progressForm.Controls.Add(progressBar);

            var detailsTextBox = new TextBox
            {
                Location = new Point(20, 95),
                Width = 550,
                Height = 100,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical
            };
            HolographicTheme.ApplyToTextBox(detailsTextBox);
            progressForm.Controls.Add(detailsTextBox);

            progressForm.Show(this);
            Application.DoEvents();

            try
            {
                detailsTextBox.AppendText($"=== Static Item AI Replacement ({backendName}) ===\r\n");
                detailsTextBox.AppendText($"Processing {selectedStatics.Count} static(s)\r\n");
                detailsTextBox.AppendText($"Resolution: {targetWidth}x{targetHeight}\r\n");
                detailsTextBox.AppendText($"Settings: Steps={Steps}, CFG={CFG}, Denoise={Denoise}\r\n\r\n");

                // Group by unique item ID
                var staticGroups = selectedStatics.GroupBy(s => s.ItemId).ToList();
                int currentGroup = 0;

                foreach (var group in staticGroups)
                {
                    var itemId = group.Key;
                    var staticsInGroup = group.ToList();

                    currentGroup++;
                    int progress = (int)((currentGroup / (float)staticGroups.Count) * 100);
                    progressBar.Value = progress;

                    progressLabel.Text = $"Generating art for static ID 0x{itemId:X4} ({currentGroup}/{staticGroups.Count})...";
                    Application.DoEvents();

                    // Restore original artwork first
                    RestoreStaticOriginalArt(itemId);

                    // Load the original static image
                    var originalImage = LoadStaticImage(itemId);

                    if (originalImage == null)
                    {
                        detailsTextBox.AppendText($"Skipping 0x{itemId:X4}: No image loaded\r\n");
                        continue;
                    }

try
                    {
                        Image newImage;
                        if (selectedBackend == AIBackend.ComfyUI)
                        {
                            newImage = await GenerateStaticArtWithComfyUI(originalImage, itemId, targetWidth, targetHeight);
                        }
                        else
                        {
                            throw new NotSupportedException("Only ComfyUI backend is supported.");
                        }

                        if (newImage != null)
                        {
                            // Store original if not already stored
                            if (!originalStaticArtCache.ContainsKey(itemId))
                            {
                                originalStaticArtCache[itemId] = new Bitmap(originalImage);
                            }

                            staticArtCache[itemId] = newImage;
                            modifiedStaticArtCache[itemId] = newImage;

                            // Mark as modified for save tracking
                            MarkArtModified(itemId, newImage);

                            detailsTextBox.AppendText($"? Replaced 0x{itemId:X4} ({staticsInGroup.Count} instances)\r\n");
                        }
                        else
                        {
                            detailsTextBox.AppendText($"? Failed to generate for 0x{itemId:X4}\r\n");
                        }
                    }
                    catch (Exception ex)
                    {
                        detailsTextBox.AppendText($"? Error on 0x{itemId:X4}: {ex.Message}\r\n");
                    }

                    Application.DoEvents();
                }

                progressLabel.Text = "Complete! Refreshing map...";
                progressBar.Value = 100;
                Application.DoEvents();

                GenerateMapImage();

                await Task.Delay(500);
                progressForm.Close();
            }
            catch (Exception ex)
            {
                progressForm.Close();
                MessageBox.Show($"Error during static replacement: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
}

        /// <summary>
        /// Generate new art for a static item using ComfyUI
        /// </summary>
        private async Task<Image> GenerateStaticArtWithComfyUI(Image originalImage, ushort itemId, int targetWidth = 512, int targetHeight = 512)
        {
            int originalWidth = originalImage.Width;
            int originalHeight = originalImage.Height;

            // Scale the static to fit in targetSize while preserving aspect ratio
            float scale = Math.Min((float)targetWidth / originalWidth, (float)targetHeight / originalHeight);
            int scaledWidth = (int)(originalWidth * scale * 0.8f); // 80% to leave some padding
            int scaledHeight = (int)(originalHeight * scale * 0.8f);

            // Prepare the image for ComfyUI - tile the static in a pattern
            Bitmap preparedImage;
            using (var bmp = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    // Use NearestNeighbor for sharp pixel art when upscaling
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;

                    // Draw the static multiple times to give AI more context
                    // Center one, plus surrounding copies
                    int centerX = (targetWidth - scaledWidth) / 2;
                    int centerY = (targetHeight - scaledHeight) / 2;

                    // Draw a 3x3 grid pattern for better AI context
                    for (int row = -1; row <= 1; row++)
                    {
                        for (int col = -1; col <= 1; col++)
                        {
                            int x = centerX + col * (scaledWidth + 10);
                            int y = centerY + row * (scaledHeight + 10);
                            g.DrawImage(originalImage, x, y, scaledWidth, scaledHeight);
                        }
                    }
                }
                preparedImage = new Bitmap(bmp);
            }

            try
            {
                byte[] imageBytes;
                using (var ms = new MemoryStream())
                {
                    preparedImage.Save(ms, ImageFormat.Png);
                    imageBytes = ms.ToArray();
                }

                var uploadedFilename = await comfyClient.UploadImage(imageBytes, $"static_{itemId:X4}.png");

                // Get appropriate prompt for static items
                string basePrompt = GetPromptForStaticId(itemId);
                string fullPrompt = string.IsNullOrWhiteSpace(Prompt) ? basePrompt : $"{Prompt}, {basePrompt}";

                double effectiveDenoise = Denoise;

                // Create workflow - use ControlNet if enabled
                string workflow;
                if (UseControlNet)
                {
                    var config = AppConfig.Instance;
                    workflow = ControlNetDepthWorkflow.CreateWorkflow(
                        fullPrompt,
                        NegativePrompt,
                        uploadedFilename,
                        Steps,
                        CFG,
                        effectiveDenoise,
                        Seed,
                        Sampler,
                        Scheduler,
                        Checkpoint,
                        config.ControlNetModel,
                        config.ControlNetStrength,
                        config.ControlNetStartPercent,
                        config.ControlNetEndPercent,
                        config.ControlNetBlurRadius,
                        config.ControlNetBlurSigma,
                        config.ControlNetBGThreshold,
                        config.ControlNetBGFeather,
                        config.ControlNetChannelMode,
                        config.ControlNetDespill,
                        config.ControlNetInvertMask
                    );
                }
                else
                {
                    workflow = Image2ImageWorkflow.CreateWorkflow(
                        fullPrompt,
                        NegativePrompt,
                        uploadedFilename,
                        targetWidth,
                        targetHeight,
                        Steps,
                        CFG,
                        effectiveDenoise,
                        Seed,
                        Sampler,
                        Scheduler,
                        Checkpoint
                    );
                }

                var promptId = await comfyClient.QueuePrompt(workflow);
                if (string.IsNullOrEmpty(promptId))
                {
                    return null;
                }

                var images = await comfyClient.GetGeneratedImages(promptId, maxAttempts: 300, pollIntervalMs: 1000);
                if (images.Count == 0)
                {
                    return null;
                }

                var imageData = await comfyClient.DownloadImage(images[0]);
                if (imageData == null || imageData.Length == 0)
                {
                    return null;
                }
                using (var ms = new MemoryStream(imageData))
                {
                    var generatedImage = Image.FromStream(ms);

                    // Extract the center portion at original static size using NearestNeighbor for sharp edges
                    var croppedImage = new Bitmap(originalWidth, originalHeight, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(croppedImage))
                    {
                        // Use NearestNeighbor for sharp, crisp pixel art quality
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;

                        // Calculate where the center static was in the generated image
                        int centerX = (targetWidth - scaledWidth) / 2;
                        int centerY = (targetHeight - scaledHeight) / 2;

                        g.DrawImage(generatedImage,
                            new Rectangle(0, 0, originalWidth, originalHeight),
                            new Rectangle(centerX, centerY, scaledWidth, scaledHeight),
                            GraphicsUnit.Pixel);
                    }

                    generatedImage.Dispose();

                    // Preserve transparency from original image
                    var finalImage = PreserveTransparency(croppedImage, originalImage);
                    croppedImage.Dispose();

                    return finalImage;
                }
            }
            finally
            {
                preparedImage.Dispose();
            }
        }

        /// <summary>
        /// Preserve transparency mask from original image
        /// </summary>
        private Bitmap PreserveTransparency(Bitmap newImage, Image originalImage)
        {
            var result = new Bitmap(newImage.Width, newImage.Height, PixelFormat.Format32bppArgb);
            var originalBmp = new Bitmap(originalImage);

            for (int y = 0; y < result.Height; y++)
            {
                for (int x = 0; x < result.Width; x++)
                {
                    var origPixel = originalBmp.GetPixel(x, y);
                    var newPixel = newImage.GetPixel(x, y);

                    // Use original alpha, new RGB
                    result.SetPixel(x, y, Color.FromArgb(origPixel.A, newPixel.R, newPixel.G, newPixel.B));
                }
            }

            originalBmp.Dispose();
            return result;
        }

        /// <summary>
        /// Get appropriate AI prompt for static item ID
        /// </summary>
        private string GetPromptForStaticId(ushort itemId)
        {
            // Trees (common ranges)
            if ((itemId >= 0x0C95 && itemId <= 0x0CCE) || // Trees
                (itemId >= 0x0CD0 && itemId <= 0x0CF7) || // More trees
                (itemId >= 0x0CF8 && itemId <= 0x0D03))   // Jungle trees
                return "fantasy game tree, detailed bark texture, lush green leaves, isometric game sprite, high detail";

            // Rocks/Boulders
            if ((itemId >= 0x1363 && itemId <= 0x136D) ||
                (itemId >= 0x0DEF && itemId <= 0x0E02))
                return "fantasy game rock, detailed stone texture, boulder, isometric game sprite, high detail";

            // Furniture/Indoor items
            if (itemId >= 0x0B2C && itemId <= 0x0B4F)
                return "medieval furniture, wooden texture, fantasy game item, isometric sprite, high detail";

            // Walls/Building parts
            if ((itemId >= 0x0001 && itemId <= 0x0064) ||
                (itemId >= 0x0079 && itemId <= 0x00A8))
                return "stone wall texture, medieval castle brick, fantasy game building, isometric sprite, high detail";

            // Floors/Ground
            if (itemId >= 0x0495 && itemId <= 0x04A4)
                return "stone floor texture, cobblestone, medieval floor tile, fantasy game, isometric sprite";

            // Default
            return "fantasy game object, detailed texture, isometric game sprite, high quality, medieval style";
        }

        /// <summary>
        /// Replace all selected statics as a single composite image.
        /// Composites every unique selected static into one canvas, sends to AI,
        /// then splits the result back into individual per-item images.
        /// </summary>
        private async Task ReplaceSelectedStaticsAsOne()
        {
            if (selectedStatics.Count == 0) return;

            var selectedBackend = SelectedBackend;

            if (selectedBackend == AIBackend.ComfyUI)
            {
                comfyClient = new ComfyUIClient(ComfyUIUrl);
            }
            else
            {
                bool available = await ImageGeneratorFactory.EnsureBackendAvailable(selectedBackend, this);
                if (!available) return;
            }

            int targetWidth = ResolutionWidth;
            int targetHeight = ResolutionHeight;
            string backendName = ImageGeneratorFactory.GetBackendName(selectedBackend);

            // Progress dialog
            var progressForm = new Form
            {
                Text = $"AI Statics As One ({backendName})",
                Width = 600,
                Height = 250,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            HolographicTheme.ApplyToForm(progressForm);

            var progressLabel = new Label
            {
                Text = "Preparing composite...",
                Location = new Point(20, 20),
                Width = 550,
                Height = 30
            };
            HolographicTheme.ApplyToLabel(progressLabel);
            progressForm.Controls.Add(progressLabel);

            var progressBar = new ProgressBar
            {
                Location = new Point(20, 60),
                Width = 550,
                Height = 25,
                Style = ProgressBarStyle.Continuous,
                Maximum = 100
            };
            progressForm.Controls.Add(progressBar);

            var detailsTextBox = new TextBox
            {
                Location = new Point(20, 95),
                Width = 550,
                Height = 100,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical
            };
            HolographicTheme.ApplyToTextBox(detailsTextBox);
            progressForm.Controls.Add(detailsTextBox);

            progressForm.Show(this);
            Application.DoEvents();

            try
            {
                detailsTextBox.AppendText($"=== Generate Statics As One ({backendName}) ===\r\n");

                // Collect unique item IDs and load their images
                var uniqueIds = selectedStatics.Select(s => s.ItemId).Distinct().ToList();
                detailsTextBox.AppendText($"Selected statics: {selectedStatics.Count} ({uniqueIds.Count} unique IDs)\r\n");

                // Restore originals first
                foreach (var itemId in uniqueIds)
                    RestoreStaticOriginalArt(itemId);

                // Load each unique static image and record its bounds
                var itemImages = new Dictionary<ushort, Image>();
                foreach (var itemId in uniqueIds)
                {
                    var img = LoadStaticImage(itemId);
                    if (img != null)
                        itemImages[itemId] = img;
                    else
                        detailsTextBox.AppendText($"  Skipping 0x{itemId:X4}: no image\r\n");
                }

                if (itemImages.Count == 0)
                {
                    detailsTextBox.AppendText("No valid images to process.\r\n");
                    await Task.Delay(1000);
                    progressForm.Close();
                    return;
                }

                progressBar.Value = 10;
                progressLabel.Text = "Compositing statics...";
                Application.DoEvents();

                // Lay out items in a grid to form the composite
                int cols = (int)Math.Ceiling(Math.Sqrt(itemImages.Count));
                int rows = (int)Math.Ceiling((double)itemImages.Count / cols);

                // Find max item dimensions for uniform cell size
                int cellW = 0, cellH = 0;
                foreach (var img in itemImages.Values)
                {
                    if (img.Width > cellW) cellW = img.Width;
                    if (img.Height > cellH) cellH = img.Height;
                }

                int padding = 4;
                int compositeW = cols * (cellW + padding) + padding;
                int compositeH = rows * (cellH + padding) + padding;

                // Record where each item was placed in the composite
                var itemBounds = new Dictionary<ushort, Rectangle>();

                var composite = new Bitmap(compositeW, compositeH, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(composite))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

                    int idx = 0;
                    foreach (var kvp in itemImages)
                    {
                        int col = idx % cols;
                        int row = idx / cols;
                        int x = padding + col * (cellW + padding);
                        int y = padding + row * (cellH + padding);

                        // Center the item within its cell
                        int offsetX = (cellW - kvp.Value.Width) / 2;
                        int offsetY = (cellH - kvp.Value.Height) / 2;

                        g.DrawImage(kvp.Value, x + offsetX, y + offsetY, kvp.Value.Width, kvp.Value.Height);
                        itemBounds[kvp.Key] = new Rectangle(x + offsetX, y + offsetY, kvp.Value.Width, kvp.Value.Height);
                        idx++;
                    }
                }

                detailsTextBox.AppendText($"Composite: {compositeW}x{compositeH} ({itemImages.Count} items in {cols}x{rows} grid)\r\n");
                progressBar.Value = 20;

                // Scale composite to fit target resolution while preserving aspect ratio
                double scale = Math.Min((double)targetWidth / compositeW, (double)targetHeight / compositeH);
                int scaledW = Math.Max(1, (int)Math.Round(compositeW * scale));
                int scaledH = Math.Max(1, (int)Math.Round(compositeH * scale));
                int padX = (targetWidth - scaledW) / 2;
                int padY = (targetHeight - scaledH) / 2;

                var padded = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(padded))
                {
                    g.Clear(Color.Black);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.DrawImage(composite, padX, padY, scaledW, scaledH);
                }

                detailsTextBox.AppendText($"Padded to {targetWidth}x{targetHeight} (scale {scale:F2})\r\n");
                progressBar.Value = 30;

                // Generate with AI
                Bitmap generatedBitmap = null;

                progressLabel.Text = $"Generating with {backendName}...";
                Application.DoEvents();

                string prompt = string.IsNullOrWhiteSpace(Prompt)
                    ? "fantasy game objects, detailed textures, isometric game sprites, high quality, medieval style"
                    : Prompt;

                if (selectedBackend == AIBackend.ComfyUI)
                {
                    detailsTextBox.AppendText($"Uploading to ComfyUI...\r\n");
                    byte[] imageBytes;
                    using (var ms = new MemoryStream())
                    {
                        padded.Save(ms, ImageFormat.Png);
                        imageBytes = ms.ToArray();
                    }

                    var uploadedName = await comfyClient.UploadImage(imageBytes, "statics_composite.png");
                    var workflow = ComfyUI.Image2ImageWorkflow.CreateWorkflow(
                        prompt, NegativePrompt, uploadedName,
                        targetWidth, targetHeight, Steps, CFG, Denoise, Seed,
                        Sampler, Scheduler);

                    var promptId = await comfyClient.QueuePrompt(workflow);
                    if (string.IsNullOrEmpty(promptId))
                        throw new Exception("Failed to queue ComfyUI prompt");

                    detailsTextBox.AppendText($"Queued prompt: {promptId}\r\n");
                    detailsTextBox.AppendText("Waiting for generation...\r\n");

                    progressBar.Value = 50;
                    Application.DoEvents();

                    var images = await comfyClient.GetGeneratedImages(promptId, maxAttempts: 300, pollIntervalMs: 1000);
                    if (images.Count == 0)
                        throw new Exception("No images generated by ComfyUI");

                    var data = await comfyClient.DownloadImage(images[0]);
                    if (data == null || data.Length < 8)
                        throw new Exception("Invalid image data from ComfyUI");

                    using (var ms = new MemoryStream(data))
                        generatedBitmap = new Bitmap(ms);
                }

                padded.Dispose();

                if (generatedBitmap == null)
                    throw new Exception("AI generation returned no image");

                detailsTextBox.AppendText($"Generated: {generatedBitmap.Width}x{generatedBitmap.Height}\r\n");
                progressBar.Value = 70;
                progressLabel.Text = "Extracting individual statics...";
                Application.DoEvents();

                // Scale generated image back to composite coordinates
                // generated → crop out the padded region → scale back to original composite size
                using (generatedBitmap)
                {
                    // The generated image is targetWidth x targetHeight.
                    // The composite was placed at (padX, padY) with size (scaledW, scaledH).
                    // We need to map back: for each item, find its region in the generated image.

                    float genScaleX = (float)generatedBitmap.Width / targetWidth;
                    float genScaleY = (float)generatedBitmap.Height / targetHeight;

                    foreach (var kvp in itemBounds)
                    {
                        ushort itemId = kvp.Key;
                        Rectangle origBounds = kvp.Value;

                        // Map from composite coords to padded/target coords
                        float srcX = (padX + origBounds.X * (float)scaledW / compositeW) * genScaleX;
                        float srcY = (padY + origBounds.Y * (float)scaledH / compositeH) * genScaleY;
                        float srcW = (origBounds.Width * (float)scaledW / compositeW) * genScaleX;
                        float srcH = (origBounds.Height * (float)scaledH / compositeH) * genScaleY;

                        // Extract the slice at original item dimensions
                        var slice = new Bitmap(origBounds.Width, origBounds.Height, PixelFormat.Format32bppArgb);
                        using (var g = Graphics.FromImage(slice))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                            g.DrawImage(generatedBitmap,
                                new RectangleF(0, 0, origBounds.Width, origBounds.Height),
                                new RectangleF(srcX, srcY, srcW, srcH),
                                GraphicsUnit.Pixel);
                        }

                        // Re-apply original alpha mask so shape is preserved
                        if (itemImages.ContainsKey(itemId))
                        {
                            using (var origBmp = new Bitmap(itemImages[itemId]))
                            {
                                if (origBmp.Width == slice.Width && origBmp.Height == slice.Height)
                                    ApplyAlphaMaskToStatic(slice, origBmp);
                            }
                        }

                        // Store original if not already stored
                        if (!originalStaticArtCache.ContainsKey(itemId) && itemImages.ContainsKey(itemId))
                            originalStaticArtCache[itemId] = new Bitmap(itemImages[itemId]);

                        staticArtCache[itemId] = slice;
                        modifiedStaticArtCache[itemId] = slice;
                        MarkArtModified(itemId, slice);

                        detailsTextBox.AppendText($"OK Replaced 0x{itemId:X4}\r\n");
                    }
                }

                composite.Dispose();

                progressBar.Value = 90;
                progressLabel.Text = "Refreshing map...";
                Application.DoEvents();

                GenerateMapImage();

                // Auto-push to JarJar if enabled
                if (AppConfig.Instance.JarJarPushEnabled && AppConfig.Instance.JarJarAutoPush && pendingArtChanges.Count > 0)
                {
                    progressLabel.Text = "Auto-pushing to MeesaJarJar...";
                    Application.DoEvents();
                    await PushToJarJar();
                }

                progressBar.Value = 100;
                progressLabel.Text = $"Done! {itemBounds.Count} statics regenerated as one.";
                detailsTextBox.AppendText($"\r\n=== Complete: {itemBounds.Count} statics ===\r\n");
                Application.DoEvents();

                await Task.Delay(800);
                progressForm.Close();
            }
            catch (Exception ex)
            {
                progressForm.Close();
                MessageBox.Show($"Error during 'Statics As One':\n\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Apply alpha mask from original static to preserve transparency shape.
        /// Copies the alpha channel from the mask to the target.
        /// </summary>
        private static void ApplyAlphaMaskToStatic(Bitmap target, Bitmap mask)
        {
            if (target.Width != mask.Width || target.Height != mask.Height) return;

            for (int y = 0; y < target.Height; y++)
            {
                for (int x = 0; x < target.Width; x++)
                {
                    var maskPixel = mask.GetPixel(x, y);
                    if (maskPixel.A == 0)
                    {
                        target.SetPixel(x, y, Color.Transparent);
                    }
                    else
                    {
                        var targetPixel = target.GetPixel(x, y);
                        target.SetPixel(x, y, Color.FromArgb(maskPixel.A, targetPixel.R, targetPixel.G, targetPixel.B));
                    }
                }
            }
        }
    }
}
