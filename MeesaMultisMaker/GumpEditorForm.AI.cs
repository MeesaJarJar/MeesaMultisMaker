using MeesaMultisMaker.ComfyUI;
using MeesaMultisMaker.AI;
using MeesaMultisMaker.Helpers;
using MeesaMultisMaker.JarJar;
using MeesaMultisMaker.Mul;
using MeesaMultisMaker.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    public partial class GumpEditorForm
    {
        // AI Generation methods
        private async Task<T> AwaitGumpPollWithCancel<T>(Task<T> pollTask, System.Threading.CancellationToken token, ComfyUIClient client)
        {
            while (!pollTask.IsCompleted)
            {
                try { await Task.Delay(500, token); }
                catch (OperationCanceledException)
                {
                    try { await client.Interrupt(); } catch { }
                    throw;
                }
            }
            return await pollTask;
        }

        private void CancelGumpGeneration()
        {
            var cts = _gumpGenCts;
            if (cts == null) return;
            try { cts.Cancel(); } catch { }
            try
            {
                var url = aiSettingsPanel?.ComfyUrl;
                if (!string.IsNullOrWhiteSpace(url))
                {
                    var client = new ComfyUIClient(url);
                    _ = client.Interrupt();
                }
            }
            catch { }
            SetStatus("Stopping...");
        }

        private async void RegenerateSelectedGump()
        {
            if (_gumpGenCts != null)
            {
                SetStatus("Generation already running - press STOP to cancel.");
                aiSettingsPanel.SetStatus("Already running", Color.Orange);
                return;
            }
            _gumpGenCts = new System.Threading.CancellationTokenSource();
            var gumpCts = _gumpGenCts;
            aiSettingsPanel.SetStopEnabled(true);
            try
            {
                await RegenerateSelectedGumpCore(gumpCts);
            }
            catch (OperationCanceledException)
            {
                SetStatus("Stopped by user.");
                aiSettingsPanel.SetStatus("Stopped by user", Color.Orange);
            }
            catch (Exception ex)
            {
                SetStatus($"AI regeneration error: {ex.Message}");
                aiSettingsPanel.SetStatus($"Error: {ex.Message}", Color.Red);
            }
            finally
            {
                aiSettingsPanel.SetStopEnabled(false);
                _gumpGenCts = null;
                gumpCts.Dispose();
            }
        }

        private async Task RegenerateSelectedGumpCore(System.Threading.CancellationTokenSource gumpCts)
        {
            if (selectedGumps.Count == 0)
            {
                SetStatus("No GUMPs selected to regenerate.");
                aiSettingsPanel.SetStatus("Select GUMP(s) first", Color.Orange);
                return;
            }

            // If multiple gumps are selected, use batch processing
            if (selectedGumps.Count > 1)
            {
                await RegenerateMultipleGumps(gumpCts);
                return;
            }

            // Single gump regeneration (original behavior)
            if (selectedGump == null || selectedGump.Image == null)
            {
                SetStatus("No GUMP selected to regenerate.");
                aiSettingsPanel.SetStatus("Select a GUMP first", Color.Orange);
                return;
            }

            SetStatus("Regenerating selected GUMP with AI...");
            aiSettingsPanel.SetStatus("Regenerating...", HolographicTheme.CyanAccent);

            try
            {
                // Check which backend is selected
                var selectedBackend = aiSettingsPanel.SelectedBackend;
                
                // Validate backend availability
                if (selectedBackend == AIBackend.ComfyUI)
                {
                    string comfyUrl = aiSettingsPanel.ComfyUrl;
                    if (string.IsNullOrEmpty(comfyUrl))
                    {
                        SetStatus("ComfyUI URL not configured.");
                        aiSettingsPanel.SetStatus("No URL configured", Color.Red);
                        return;
                    }
                }
                else
                {
                    bool available = await ImageGeneratorFactory.EnsureBackendAvailable(selectedBackend, this);
                    if (!available)
                    {
                        SetStatus("Local models not available.");
                        aiSettingsPanel.SetStatus("Models not available", Color.Red);
                        return;
                    }
                }

                int storageKey = GetGumpStorageKey(selectedGump);
                if (!_originalGumpImages.ContainsKey(storageKey))
                {
                    _originalGumpImages[storageKey] = new Bitmap(selectedGump.Image);
                }

                // Get the original gump's display size (what it looks like on canvas)
                int originalDisplayWidth = selectedGump.Width;
                int originalDisplayHeight = selectedGump.Height;
                
                // Get the actual source image size
                int sourceWidth = selectedGump.Image.Width;
                int sourceHeight = selectedGump.Image.Height;

                // Determine AI generation size - use panel settings but ensure minimum size
                int aiWidth = aiSettingsPanel.ResolutionWidth;
                int aiHeight = aiSettingsPanel.ResolutionHeight;
                
                // Scale up the source image if it's smaller than the AI resolution
                float scaleToAI = 1.0f;
                if (sourceWidth < aiWidth || sourceHeight < aiHeight)
                {
                    float scaleW = (float)aiWidth / sourceWidth;
                    float scaleH = (float)aiHeight / sourceHeight;
                    scaleToAI = Math.Max(scaleW, scaleH);
                }

                // Create the image to send to AI (scaled up if needed)
                Bitmap imageForAI;
                if (scaleToAI > 1.0f)
                {
                    int scaledWidth = ((int)(sourceWidth * scaleToAI) + 7) / 8 * 8; // Round to multiple of 8
                    int scaledHeight = ((int)(sourceHeight * scaleToAI) + 7) / 8 * 8;
                    
                    imageForAI = new Bitmap(scaledWidth, scaledHeight, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(imageForAI))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.Clear(Color.Transparent);
                        g.DrawImage(selectedGump.Image, 0, 0, scaledWidth, scaledHeight);
                    }
                    aiWidth = scaledWidth;
                    aiHeight = scaledHeight;
                    SetStatus($"Upscaling {sourceWidth}x{sourceHeight} -> {aiWidth}x{aiHeight} for AI...");
                }
                else
                {
                    imageForAI = new Bitmap(selectedGump.Image);
                    aiWidth = sourceWidth;
                    aiHeight = sourceHeight;
                }

                Bitmap newBmp = null;

                if (selectedBackend != AIBackend.ComfyUI)
                {
                    throw new NotSupportedException("Only ComfyUI backend is supported.");
                }
                else
                {
                    // Use ComfyUI backend
                    _comfyClient = new ComfyUIClient(aiSettingsPanel.ComfyUrl);

                    byte[] imageBytes;
                    using (var ms = new MemoryStream())
                    {
                        imageForAI.Save(ms, ImageFormat.Png);
                        imageBytes = ms.ToArray();
                    }

                    string uploadedFilename = await _comfyClient.UploadImage(imageBytes, $"gump_{storageKey}.png");

                    long effSeed = aiSettingsPanel.Seed ?? new Random().Next();
                    SetStatus($"Generating with seed {effSeed}...");

                    string workflow = Image2ImageWorkflow.CreateWorkflow(
                        aiSettingsPanel.Prompt,
                        aiSettingsPanel.NegativePrompt,
                        uploadedFilename,
                        aiWidth, aiHeight,
                        aiSettingsPanel.Steps,
                        aiSettingsPanel.Cfg,
                        aiSettingsPanel.Denoise,
                        effSeed,
                        aiSettingsPanel.Sampler,
                        aiSettingsPanel.Scheduler,
                        aiSettingsPanel.Checkpoint
                    );

                    string promptId = await _comfyClient.QueuePrompt(workflow);
                    if (string.IsNullOrEmpty(promptId))
                    {
                        SetStatus("Failed to queue prompt");
                        aiSettingsPanel.SetStatus("Queue failed", Color.Red);
                        imageForAI.Dispose();
                        return;
                    }

                    var images = await AwaitGumpPollWithCancel(_comfyClient.GetGeneratedImages(promptId, maxAttempts: 120, pollIntervalMs: 1000), gumpCts.Token, _comfyClient);
                    gumpCts.Token.ThrowIfCancellationRequested();
                    if (images.Count == 0)
                    {
                        SetStatus("No images generated");
                        aiSettingsPanel.SetStatus("No images", Color.Red);
                        imageForAI.Dispose();
                        return;
                    }

                    byte[] newImageData = await _comfyClient.DownloadImage(images[0]);
                    if (newImageData == null || newImageData.Length < 8)
                    {
                        SetStatus("Download failed or empty.");
                        aiSettingsPanel.SetStatus("Download failed", Color.Red);
                        imageForAI.Dispose();
                        return;
                    }
                    using (var ms = new MemoryStream(newImageData))
                    {
                        newBmp = new Bitmap(ms);
                    }
                }

                // Clean up the image we sent to AI
                imageForAI.Dispose();

                if (newBmp == null)
                {
                    SetStatus("Failed to generate image");
                    aiSettingsPanel.SetStatus("Generation failed", Color.Red);
                    return;
                }

                // Resize the AI output back to the ORIGINAL SOURCE dimensions
                // This ensures the gump stays the same size on canvas
                if (newBmp.Width != sourceWidth || newBmp.Height != sourceHeight)
                {
                    SetStatus($"Resizing AI output {newBmp.Width}x{newBmp.Height} -> {sourceWidth}x{sourceHeight}...");
                    var resizedBmp = new Bitmap(sourceWidth, sourceHeight, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(resizedBmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.Clear(Color.Transparent);
                        g.DrawImage(newBmp, 0, 0, sourceWidth, sourceHeight);
                    }
                    newBmp.Dispose();
                    newBmp = resizedBmp;
                }

                // Store the raw AI output BEFORE black pixel removal for post-generation adjustment
                if (_rawAIOutputImages.ContainsKey(storageKey))
                {
                    _rawAIOutputImages[storageKey]?.Dispose();
                }
                _rawAIOutputImages[storageKey] = new Bitmap(newBmp);

                // Apply black pixel removal if enabled
                if (aiSettingsPanel.DropBlackPixels)
                {
                    newBmp = RemoveBlackPixels(newBmp, aiSettingsPanel.BlackThreshold);
                }

                PushUndo();
                selectedGump.Image?.Dispose();
                selectedGump.Image = newBmp;
                
                // Store regenerated image for old/new toggle
                if (_regeneratedGumpImages.ContainsKey(storageKey))
                {
                    _regeneratedGumpImages[storageKey]?.Dispose();
                }
                _regeneratedGumpImages[storageKey] = new Bitmap(newBmp);
                
                canvasBox.Invalidate();

                aiSettingsPanel.SetOldNewEnabled(true);
                aiSettingsPanel.SetPushToJarJarEnabled(true);
                aiSettingsPanel.SetPendingChanges(_regeneratedGumpImages.Count);
                string backendName = ImageGeneratorFactory.GetBackendName(selectedBackend);
                SetStatus($"Selected GUMP regenerated with {backendName}!");
                aiSettingsPanel.SetStatus("Regenerated!", Color.LimeGreen);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                SetStatus($"AI regeneration error: {ex.Message}");
                aiSettingsPanel.SetStatus($"Error: {ex.Message}", Color.Red);
            }
        }

        /// <summary>
        /// Regenerate multiple selected gumps as a combined image, then split back
        /// </summary>
        private async Task RegenerateMultipleGumps(System.Threading.CancellationTokenSource gumpCts)
        {
            SetStatus($"Regenerating {selectedGumps.Count} selected GUMPs as one...");
            aiSettingsPanel.SetStatus("Combining images...", HolographicTheme.CyanAccent);

            try
            {
                // Check which backend is selected
                var selectedBackend = aiSettingsPanel.SelectedBackend;
                
                // Validate backend availability
                if (selectedBackend == AIBackend.ComfyUI)
                {
                    string comfyUrl = aiSettingsPanel.ComfyUrl;
                    if (string.IsNullOrEmpty(comfyUrl))
                    {
                        SetStatus("ComfyUI URL not configured.");
                        aiSettingsPanel.SetStatus("No URL configured", Color.Red);
                        return;
                    }
                    _comfyClient = new ComfyUIClient(comfyUrl);
                }
                else
                {
                    bool available = await ImageGeneratorFactory.EnsureBackendAvailable(selectedBackend, this);
                    if (!available)
                    {
                        SetStatus("Local models not available.");
                        aiSettingsPanel.SetStatus("Models not available", Color.Red);
                        return;
                    }
                }

                // Calculate bounding box of all selected gumps
                int minX = int.MaxValue, minY = int.MaxValue;
                int maxX = int.MinValue, maxY = int.MinValue;

                foreach (var gump in selectedGumps)
                {
                    if (gump.Image == null) continue;
                    var bounds = gump.GetBounds();
                    minX = Math.Min(minX, bounds.Left);
                    minY = Math.Min(minY, bounds.Top);
                    maxX = Math.Max(maxX, bounds.Right);
                    maxY = Math.Max(maxY, bounds.Bottom);
                }

                int combinedWidth = maxX - minX;
                int combinedHeight = maxY - minY;

                if (combinedWidth <= 0 || combinedHeight <= 0)
                {
                    SetStatus("Invalid selection bounds");
                    aiSettingsPanel.SetStatus("Invalid bounds", Color.Red);
                    return;
                }

                // Store original images for each selected gump
                var gumpInfos = new List<(PlacedGump Gump, Rectangle LocalBounds, Bitmap OriginalImage)>();
                foreach (var gump in selectedGumps)
                {
                    if (gump.Image == null) continue;

                    int storageKey = GetGumpStorageKey(gump);
                    if (!_originalGumpImages.ContainsKey(storageKey))
                    {
                        _originalGumpImages[storageKey] = new Bitmap(gump.Image);
                    }

                    var localBounds = new Rectangle(
                        gump.X - minX,
                        gump.Y - minY,
                        gump.Width,
                        gump.Height
                    );
                    gumpInfos.Add((gump, localBounds, new Bitmap(gump.Image)));
                }

                // Create combined image
                var combinedImage = new Bitmap(combinedWidth, combinedHeight, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(combinedImage))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                    foreach (var info in gumpInfos)
                    {
                        g.DrawImage(info.OriginalImage, info.LocalBounds);
                    }
                }

                // Scale up to minimum AI size if needed
                int aiWidth = combinedWidth;
                int aiHeight = combinedHeight;
                float scaleFactor = 1.0f;

                if (combinedWidth < MIN_AI_SIZE || combinedHeight < MIN_AI_SIZE)
                {
                    float scaleW = (float)MIN_AI_SIZE / combinedWidth;
                    float scaleH = (float)MIN_AI_SIZE / combinedHeight;
                    scaleFactor = Math.Max(scaleW, scaleH);

                    aiWidth = (int)(combinedWidth * scaleFactor);
                    aiHeight = (int)(combinedHeight * scaleFactor);

                    aiWidth = ((aiWidth + 7) / 8) * 8;
                    aiHeight = ((aiHeight + 7) / 8) * 8;

                    SetStatus($"Upscaling {combinedWidth}x{combinedHeight} -> {aiWidth}x{aiHeight}...");
                }

                // Upscale combined image if needed
                Bitmap imageToUpload = combinedImage;
                if (scaleFactor > 1.0f)
                {
                    imageToUpload = new Bitmap(aiWidth, aiHeight, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(imageToUpload))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.Clear(Color.Transparent);
                        g.DrawImage(combinedImage, 0, 0, aiWidth, aiHeight);
                    }
                }

                Bitmap generatedImage = null;

                if (selectedBackend != AIBackend.ComfyUI)
                {
                    throw new NotSupportedException("Only ComfyUI backend is supported.");
                }
                else
                {
                    // Use ComfyUI backend
                    byte[] imageBytes;
                    using (var ms = new MemoryStream())
                    {
                        imageToUpload.Save(ms, ImageFormat.Png);
                        imageBytes = ms.ToArray();
                    }

                    SetStatus("Uploading combined image...");
                    aiSettingsPanel.SetStatus("Uploading...", HolographicTheme.CyanAccent);

                    string uploadedFilename = await _comfyClient.UploadImage(imageBytes, $"gump_combined_{DateTime.Now.Ticks}.png");

                    long effSeed = aiSettingsPanel.Seed ?? new Random().Next();
                    SetStatus($"Generating {selectedGumps.Count} GUMPs with seed {effSeed}...");

                    string workflow = Image2ImageWorkflow.CreateWorkflow(
                        aiSettingsPanel.Prompt,
                        aiSettingsPanel.NegativePrompt,
                        uploadedFilename,
                        aiWidth, aiHeight,
                        aiSettingsPanel.Steps,
                        aiSettingsPanel.Cfg,
                        aiSettingsPanel.Denoise,
                        effSeed,
                        aiSettingsPanel.Sampler,
                        aiSettingsPanel.Scheduler,
                        aiSettingsPanel.Checkpoint
                    );

                    SetStatus("Queuing AI generation...");
                    aiSettingsPanel.SetStatus("Queuing...", HolographicTheme.CyanAccent);

                    string promptId = await _comfyClient.QueuePrompt(workflow);
                    if (string.IsNullOrEmpty(promptId))
                    {
                        SetStatus("Failed to queue prompt");
                        aiSettingsPanel.SetStatus("Queue failed", Color.Red);
                        CleanupBitmaps(combinedImage, imageToUpload, gumpInfos);
                        return;
                    }

                    SetStatus("Generating... (this may take a while)");
                    aiSettingsPanel.SetStatus("Generating...", HolographicTheme.CyanAccent);

                    var images = await AwaitGumpPollWithCancel(_comfyClient.GetGeneratedImages(promptId, maxAttempts: 120, pollIntervalMs: 1000), gumpCts.Token, _comfyClient);
                    gumpCts.Token.ThrowIfCancellationRequested();
                    if (images.Count == 0)
                    {
                        SetStatus("No images generated");
                        aiSettingsPanel.SetStatus("No images", Color.Red);
                        CleanupBitmaps(combinedImage, imageToUpload, gumpInfos);
                        return;
                    }

                    SetStatus("Downloading generated image...");
                    byte[] newImageData = await _comfyClient.DownloadImage(images[0]);
                    if (newImageData == null || newImageData.Length < 8)
                    {
                        SetStatus("Download failed or empty.");
                        aiSettingsPanel.SetStatus("Download failed", Color.Red);
                        CleanupBitmaps(combinedImage, imageToUpload, gumpInfos);
                        return;
                    }

                    using (var ms = new MemoryStream(newImageData))
                    {
                        generatedImage = new Bitmap(ms);
                    }
                }

                if (generatedImage == null)
                {
                    SetStatus("Failed to generate image");
                    aiSettingsPanel.SetStatus("Generation failed", Color.Red);
                    CleanupBitmaps(combinedImage, imageToUpload, gumpInfos);
                    return;
                }

                // Downscale back to original size if we upscaled
                Bitmap finalGeneratedImage = generatedImage;
                if (scaleFactor > 1.0f)
                {
                    SetStatus($"Downscaling {generatedImage.Width}x{generatedImage.Height} -> {combinedWidth}x{combinedHeight}...");
                    finalGeneratedImage = new Bitmap(combinedWidth, combinedHeight, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(finalGeneratedImage))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.Clear(Color.Transparent);
                        g.DrawImage(generatedImage, 0, 0, combinedWidth, combinedHeight);
                    }
                    generatedImage.Dispose();
                }

                // Split the generated image back into individual gumps
                SetStatus("Splitting result back into pieces...");
                PushUndo();

                foreach (var info in gumpInfos)
                {
                    var newGumpImage = new Bitmap(info.LocalBounds.Width, info.LocalBounds.Height, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(newGumpImage))
                    {
                        g.DrawImage(finalGeneratedImage,
                            new Rectangle(0, 0, info.LocalBounds.Width, info.LocalBounds.Height),
                            info.LocalBounds,
                            GraphicsUnit.Pixel);
                    }

                    newGumpImage = ApplyAlphaMask(newGumpImage, info.OriginalImage);

                    int storageKey = GetGumpStorageKey(info.Gump);

                    // Store the raw AI output BEFORE black pixel removal
                    if (_rawAIOutputImages.ContainsKey(storageKey))
                    {
                        _rawAIOutputImages[storageKey]?.Dispose();
                    }
                    _rawAIOutputImages[storageKey] = new Bitmap(newGumpImage);

                    if (aiSettingsPanel.DropBlackPixels)
                    {
                        newGumpImage = RemoveBlackPixels(newGumpImage, aiSettingsPanel.BlackThreshold);
                    }

                    info.Gump.Image?.Dispose();
                    info.Gump.Image = newGumpImage;

                    // Store regenerated image for old/new toggle, save, and push
                    if (_regeneratedGumpImages.ContainsKey(storageKey))
                    {
                        _regeneratedGumpImages[storageKey]?.Dispose();
                    }
                    _regeneratedGumpImages[storageKey] = new Bitmap(newGumpImage);
                }

                // Cleanup
                CleanupBitmaps(combinedImage, imageToUpload, gumpInfos);
                if (finalGeneratedImage != generatedImage)
                    finalGeneratedImage.Dispose();

                canvasBox.Invalidate();
                aiSettingsPanel.SetOldNewEnabled(true);
                aiSettingsPanel.SetPushToJarJarEnabled(true);
                aiSettingsPanel.SetPendingChanges(_regeneratedGumpImages.Count);
                string backendName = ImageGeneratorFactory.GetBackendName(selectedBackend);
                SetStatus($"Regenerated {selectedGumps.Count} GUMPs with {backendName}!");
                aiSettingsPanel.SetStatus($"Regenerated {selectedGumps.Count}!", Color.LimeGreen);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                SetStatus($"AI regeneration error: {ex.Message}");
                aiSettingsPanel.SetStatus($"Error: {ex.Message}", Color.Red);
                System.Diagnostics.Debug.WriteLine($"RegenerateMultipleGumps error: {ex}");
            }
        }

        private void CleanupBitmaps(Bitmap combinedImage, Bitmap imageToUpload, List<(PlacedGump Gump, Rectangle LocalBounds, Bitmap OriginalImage)> gumpInfos)
        {
            combinedImage?.Dispose();
            if (imageToUpload != combinedImage)
                imageToUpload?.Dispose();
            foreach (var info in gumpInfos)
            {
                info.OriginalImage?.Dispose();
            }
        }

        /// <summary>
        /// Apply the alpha channel from the original image to the new image.
        /// </summary>
        private Bitmap ApplyAlphaMask(Bitmap newImage, Bitmap originalImage)
        {
            int width = Math.Min(newImage.Width, originalImage.Width);
            int height = Math.Min(newImage.Height, originalImage.Height);

            var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            var newData = newImage.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var origData = originalImage.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var resultData = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try
            {
                int byteCount = Math.Abs(newData.Stride) * height;
                byte[] newPixels = new byte[byteCount];
                byte[] origPixels = new byte[byteCount];
                byte[] resultPixels = new byte[byteCount];

                System.Runtime.InteropServices.Marshal.Copy(newData.Scan0, newPixels, 0, byteCount);
                System.Runtime.InteropServices.Marshal.Copy(origData.Scan0, origPixels, 0, byteCount);

                int stride = newData.Stride;

                for (int y = 0; y < height; y++)
                {
                    int rowOffset = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        int offset = rowOffset + x * 4;
                        resultPixels[offset] = newPixels[offset];
                        resultPixels[offset + 1] = newPixels[offset + 1];
                        resultPixels[offset + 2] = newPixels[offset + 2];
                        resultPixels[offset + 3] = origPixels[offset + 3];
                    }
                }

                System.Runtime.InteropServices.Marshal.Copy(resultPixels, 0, resultData.Scan0, byteCount);
            }
            finally
            {
                newImage.UnlockBits(newData);
                originalImage.UnlockBits(origData);
                result.UnlockBits(resultData);
            }

            newImage.Dispose();
            return result;
        }

        /// <summary>
        /// Remove pixels where R, G, and B are all below threshold.
        /// </summary>
        private Bitmap RemoveBlackPixels(Bitmap source, int threshold)
        {
            var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

            var sourceData = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var resultData = result.LockBits(new Rectangle(0, 0, result.Width, result.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try
            {
                int byteCount = Math.Abs(sourceData.Stride) * source.Height;
                byte[] srcPixels = new byte[byteCount];
                byte[] dstPixels = new byte[byteCount];

                System.Runtime.InteropServices.Marshal.Copy(sourceData.Scan0, srcPixels, 0, byteCount);

                int stride = sourceData.Stride;

                for (int y = 0; y < source.Height; y++)
                {
                    int rowOffset = y * stride;
                    for (int x = 0; x < source.Width; x++)
                    {
                        int offset = rowOffset + x * 4;
                        byte b = srcPixels[offset];
                        byte g = srcPixels[offset + 1];
                        byte r = srcPixels[offset + 2];
                        byte a = srcPixels[offset + 3];

                        if (r <= threshold && g <= threshold && b <= threshold)
                        {
                            dstPixels[offset] = 0;
                            dstPixels[offset + 1] = 0;
                            dstPixels[offset + 2] = 0;
                            dstPixels[offset + 3] = 0;
                        }
                        else
                        {
                            dstPixels[offset] = b;
                            dstPixels[offset + 1] = g;
                            dstPixels[offset + 2] = r;
                            dstPixels[offset + 3] = a;
                        }
                    }
                }

                System.Runtime.InteropServices.Marshal.Copy(dstPixels, 0, resultData.Scan0, byteCount);
            }
            finally
            {
                source.UnlockBits(sourceData);
                result.UnlockBits(resultData);
            }

            source.Dispose();
            return result;
        }

        private void RevertAIChanges()
        {
            if (_originalGumpImages.Count == 0)
            {
                SetStatus("No AI changes to revert.");
                aiSettingsPanel.SetStatus("Nothing to revert", Color.Orange);
                return;
            }

            PushUndo();
            int revertedCount = 0;

            foreach (var gump in placedGumps.ToList())
            {
                int storageKey = GetGumpStorageKey(gump);
                if (_originalGumpImages.TryGetValue(storageKey, out var originalImage))
                {
                    gump.Image?.Dispose();
                    gump.Image = new Bitmap(originalImage);
                    revertedCount++;
                }
            }

            // Clean up stored images
            foreach (var kvp in _originalGumpImages.ToList())
            {
                kvp.Value?.Dispose();
            }
            _originalGumpImages.Clear();

            foreach (var kvp in _regeneratedGumpImages.ToList())
            {
                kvp.Value?.Dispose();
            }
            _regeneratedGumpImages.Clear();

            _showingOriginal = false;

            canvasBox.Invalidate();
            SetStatus($"Reverted {revertedCount} gump(s) to original.");
            aiSettingsPanel.SetStatus($"Reverted {revertedCount}", Color.LimeGreen);
            aiSettingsPanel.SetOldNewEnabled(false);
            aiSettingsPanel.SetPushToJarJarEnabled(false);
            aiSettingsPanel.SetPendingChanges(0);
            aiSettingsPanel.SetOldNewButtonText("Old/New");
        }

        private void ToggleOldNew()
        {
            if (selectedGump == null)
            {
                SetStatus("No gump selected.");
                return;
            }

            int storageKey = GetGumpStorageKey(selectedGump);

            if (!_originalGumpImages.TryGetValue(storageKey, out var originalImage))
            {
                SetStatus("No original image stored for this gump.");
                return;
            }

            _showingOriginal = !_showingOriginal;

            if (_showingOriginal)
            {
                // Store current (regenerated) image before switching to original
                if (!_regeneratedGumpImages.ContainsKey(storageKey))
                {
                    _regeneratedGumpImages[storageKey] = new Bitmap(selectedGump.Image);
                }
                else
                {
                    // Update with current regenerated version
                    _regeneratedGumpImages[storageKey]?.Dispose();
                    _regeneratedGumpImages[storageKey] = new Bitmap(selectedGump.Image);
                }
                
                // Switch to original
                selectedGump.Image?.Dispose();
                selectedGump.Image = new Bitmap(originalImage);
                aiSettingsPanel.SetOldNewButtonText("Show New");
                SetStatus("Showing ORIGINAL image");
            }
            else
            {
                // Switch back to regenerated
                if (_regeneratedGumpImages.TryGetValue(storageKey, out var regeneratedImage))
                {
                    selectedGump.Image?.Dispose();
                    selectedGump.Image = new Bitmap(regeneratedImage);
                    aiSettingsPanel.SetOldNewButtonText("Old/New");
                    SetStatus("Showing REGENERATED image");
                }
                else
                {
                    SetStatus("No regenerated image available - regenerate first");
                    _showingOriginal = true; // Stay on original
                }
            }

            canvasBox.Invalidate();
        }

        /// <summary>
        /// Reapply black pixel removal to the selected gump based on current settings.
        /// Called when user adjusts the checkbox or threshold after AI generation.
        /// </summary>
        private void ReapplyBlackPixelRemoval()
        {
            if (selectedGump == null)
            {
                return;
            }

            int storageKey = GetGumpStorageKey(selectedGump);

            // Check if we have a raw AI output for this gump
            Bitmap rawImage;
            if (!_rawAIOutputImages.TryGetValue(storageKey, out rawImage) || rawImage == null)
            {
                // No raw AI output stored - this gump hasn't been regenerated
                // Don't show an error, just silently return
                return;
            }

            try
            {
                // Start from the raw AI output
                Bitmap processedImage = new Bitmap(rawImage);

                // Apply black pixel removal if enabled
                if (aiSettingsPanel.DropBlackPixels)
                {
                    processedImage = RemoveBlackPixels(processedImage, aiSettingsPanel.BlackThreshold);
                }

                // Update the gump image
                selectedGump.Image?.Dispose();
                selectedGump.Image = processedImage;

                // Update the stored regenerated image
                if (_regeneratedGumpImages.ContainsKey(storageKey))
                {
                    _regeneratedGumpImages[storageKey]?.Dispose();
                }
                _regeneratedGumpImages[storageKey] = new Bitmap(processedImage);

                canvasBox.Invalidate();
                SetStatus(aiSettingsPanel.DropBlackPixels 
                    ? $"Black pixels removed (threshold: {aiSettingsPanel.BlackThreshold})" 
                    : "Black pixel removal disabled");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReapplyBlackPixelRemoval error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get a stable storage key for a placed gump.
        /// Uses GumpId if available, otherwise uses a combination of position and layer.
        /// </summary>
        private int GetGumpStorageKey(PlacedGump gump)
        {
            if (gump.GumpId >= 0)
            {
                return gump.GumpId;
            }
            
            // For custom images (GumpId = -1), use object's RuntimeHelpers identity,
            // forced negative: real GumpIds are always >= 0, so custom keys can
            // never collide with them (non-negative hashes map to -hash - 1).
            int hash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(gump);
            return hash >= 0 ? unchecked(-hash - 1) : hash;
        }

        /// <summary>
        /// Push AI-modified gumps to MeesaJarJar.com.
        /// Collects all regenerated gump images and uploads them via the API.
        /// </summary>
        private async Task PushGumpsToJarJar()
        {
            if (_regeneratedGumpImages.Count == 0)
            {
                MessageBox.Show("No AI-modified gumps to push.", "Push to MeesaJarJar",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var config = AppConfig.Instance;
            if (!config.JarJarPushEnabled)
            {
                MessageBox.Show("Push to MeesaJarJar is disabled in settings.",
                    "Push Disabled", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Collect valid gump items (only those with real GumpIds)
            var itemsToUpload = new Dictionary<ushort, Bitmap>();
            foreach (var kvp in _regeneratedGumpImages)
            {
                if (kvp.Key >= 0 && kvp.Key <= 0xFFFF && kvp.Value != null)
                    itemsToUpload[(ushort)kvp.Key] = kvp.Value;
            }

            if (itemsToUpload.Count == 0)
            {
                MessageBox.Show("No valid gump IDs to push (custom images without IDs are skipped).",
                    "Push to MeesaJarJar", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Push {itemsToUpload.Count} modified gump(s) to MeesaJarJar.com?\n\n" +
                $"Server: {config.JarJarApiUrl}\n\n" +
                "Connected game clients will see changes within ~30 seconds.",
                "Push Gumps to MeesaJarJar",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            JarJarClient client = null;
            try
            {
                client = new JarJarClient(config.JarJarApiUrl, config.JarJarAuthToken);

                aiSettingsPanel.SetStatus("Connecting to JarJar...");
                Application.DoEvents();

                bool connected = await client.TestConnectionAsync();
                if (!connected)
                {
                    MessageBox.Show(
                        $"Cannot connect to {config.JarJarApiUrl}",
                        "Connection Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    aiSettingsPanel.SetStatus("Connection failed", HolographicTheme.ButtonDanger);
                    return;
                }

                var results = await client.UploadMultipleGumpsAsync(
                    itemsToUpload,
                    allowResize: true,
                    onProgress: (completed, total) =>
                    {
                        aiSettingsPanel.SetStatus($"Pushing {completed}/{total}...");
                        Application.DoEvents();
                    });

                int successCount = 0;
                int failCount = 0;
                var errors = new StringBuilder();

                foreach (var r in results)
                {
                    if (r.Success)
                        successCount++;
                    else
                    {
                        failCount++;
                        errors.AppendLine($"  {r.GraphicId}: {r.Message}");
                    }
                }

                if (failCount == 0)
                {
                    MessageBox.Show(
                        $"Successfully pushed {successCount} gump(s) to MeesaJarJar!\n\n" +
                        "Game clients will auto-update within ~30 seconds.",
                        "Push Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    aiSettingsPanel.SetStatus($"Pushed {successCount} gumps", HolographicTheme.ButtonSuccess);
                }
                else if (successCount > 0)
                {
                    MessageBox.Show(
                        $"Pushed {successCount} of {results.Count} gumps.\n\nFailed ({failCount}):\n{errors}",
                        "Partial Push", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    aiSettingsPanel.SetStatus($"Pushed {successCount}/{results.Count}", HolographicTheme.ButtonWarning);
                }
                else
                {
                    MessageBox.Show(
                        $"Failed to push gumps:\n\n{errors}",
                        "Push Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    aiSettingsPanel.SetStatus("Push failed!", HolographicTheme.ButtonDanger);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error pushing to MeesaJarJar:\n\n{ex.Message}",
                    "Push Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                aiSettingsPanel.SetStatus($"Error: {ex.Message}", HolographicTheme.ButtonDanger);
            }
            finally
            {
                client?.Dispose();
            }
        }

        /// <summary>
        /// Save AI-modified gumps back to the MUL files.
        /// </summary>
        private void SaveGumpsToMul()
        {
            if (_regeneratedGumpImages.Count == 0)
            {
                MessageBox.Show("No AI-modified gumps to save.", "Save Art",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrEmpty(mulFolder) || !Directory.Exists(mulFolder))
            {
                MessageBox.Show("MUL folder not set. Please configure it in Settings.",
                    "Save Art", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Check if gump files exist in MUL or UOP format
            string gumpIdxPath = Path.Combine(mulFolder, "gumpidx.mul");
            string gumpMulPath = Path.Combine(mulFolder, "gumpart.mul");
            bool hasMulFiles = File.Exists(gumpIdxPath) && File.Exists(gumpMulPath);
            bool hasUopFile = UopGumpReader.UopFileExists(mulFolder);

            if (!hasMulFiles && !hasUopFile)
            {
                MessageBox.Show(
                    "Gump files were not found in:\n\n" +
                    $"{mulFolder}\n\n" +
                    "Neither gumpart.mul/gumpidx.mul nor gumpartLegacyMUL.uop were found.\n" +
                    "Please verify your MUL folder path in Settings.",
                    "Files Not Found",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int saved = 0;
            int failed = 0;
            var errors = new StringBuilder();

            foreach (var kvp in _regeneratedGumpImages)
            {
                if (kvp.Key < 0 || kvp.Key > 0xFFFF || kvp.Value == null)
                    continue;

                try
                {
                    if (GumpArtWriter.SaveGump(mulFolder, kvp.Key, kvp.Value))
                        saved++;
                    else
                    {
                        failed++;
                        errors.AppendLine($"  Gump 0x{kvp.Key:X4}: write failed");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SaveGump 0x{kvp.Key:X4} error: {ex.Message}");
                    failed++;
                    errors.AppendLine($"  Gump 0x{kvp.Key:X4}: {ex.Message}");
                }
            }

            if (failed == 0)
            {
                MessageBox.Show($"Saved {saved} gump(s) to art files.",
                    "Save Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                aiSettingsPanel.SetStatus($"Saved {saved} gumps", HolographicTheme.ButtonSuccess);
                aiSettingsPanel.SetPendingChanges(0);
            }
            else
            {
                MessageBox.Show(
                    $"Saved {saved}, failed {failed} gump(s).\n\n{errors}",
                    "Save Partial", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                aiSettingsPanel.SetStatus($"Saved {saved}/{saved + failed}", HolographicTheme.ButtonWarning);
            }
        }

        private List<int> GetSelectedPaletteGumpIds()
        {
            var ids = new List<int>();
            foreach (int index in gumpListView.SelectedIndices)
            {
                if (index >= 0 && index < filteredGumpIds.Count)
                {
                    ids.Add(filteredGumpIds[index]);
                }
            }
            return ids.Distinct().ToList();
        }

        private void ApplyImageEffectsToPaletteGumps(bool applyToAllSelected)
        {
            if (imageEditingPanel == null)
                return;

            // Prefer canvas selection (what user has selected in editor), then fallback to palette selection.
            var targetCanvasGumps = applyToAllSelected
                ? selectedGumps?.Where(g => g?.Image != null).Distinct().ToList()
                : (selectedGump != null && selectedGump.Image != null
                    ? new List<PlacedGump> { selectedGump }
                    : new List<PlacedGump>());

            if (targetCanvasGumps != null && targetCanvasGumps.Count > 0)
            {
                int appliedCanvas = 0;
                foreach (var g in targetCanvasGumps)
                {
                    int storageKey = GetGumpStorageKey(g);
                    if (!_originalGumpImages.ContainsKey(storageKey))
                        _originalGumpImages[storageKey] = new Bitmap(g.Image);

                    using (var source = new Bitmap(_originalGumpImages[storageKey]))
                    {
                        var result = ImageEffects.ApplyEffects(
                            source,
                            imageEditingPanel.Brightness,
                            imageEditingPanel.Contrast,
                            imageEditingPanel.Hue,
                            imageEditingPanel.Saturation,
                            imageEditingPanel.PixelSize,
                            imageEditingPanel.PixelizeEnabled,
                            imageEditingPanel.PaletteColors,
                            imageEditingPanel.NoiseIntensity,
                            imageEditingPanel.DitherLevels,
                            imageEditingPanel.EdgeDarkening,
                            imageEditingPanel.ColorBands,
                            imageEditingPanel.FillHoles,
                            imageEditingPanel.RemoveBackgroundEnabled,
                            imageEditingPanel.BackgroundMode,
                            imageEditingPanel.BackgroundColor,
                            imageEditingPanel.BackgroundThreshold
                        );

                        g.Image?.Dispose();
                        g.Image = result;

                        // Keep palette/cache in sync only for real palette gumps.
                        if (g.GumpId >= 0)
                        {
                            if (_regeneratedGumpImages.TryGetValue(g.GumpId, out var oldReg))
                                oldReg?.Dispose();
                            _regeneratedGumpImages[g.GumpId] = new Bitmap(result);

                            if (gumpCache.TryGetValue(g.GumpId, out var oldCached))
                                oldCached?.Dispose();
                            gumpCache[g.GumpId] = new Bitmap(result);
                        }
                    }

                    appliedCanvas++;
                }

                if (appliedCanvas > 0)
                {
                    aiSettingsPanel.SetPendingChanges(_regeneratedGumpImages.Count);
                    aiSettingsPanel.SetOldNewEnabled(_regeneratedGumpImages.Count > 0);
                    gumpListView.Invalidate();
                    canvasBox.Invalidate();
                    SetStatus($"Applied image FX to {appliedCanvas} selected canvas GUMP(s)");
                }
                else
                {
                    SetStatus("No selected canvas GUMPs could be processed.");
                }
                return;
            }

            // Fallback: palette selection behavior.
            var targetIds = applyToAllSelected
                ? GetSelectedPaletteGumpIds()
                : new List<int> { selectedPaletteGump };

            targetIds = targetIds.Where(id => id >= 0).Distinct().ToList();
            if (targetIds.Count == 0)
            {
                SetStatus("Select gumps on canvas (or in left palette) first.");
                return;
            }

            int applied = 0;
            foreach (int gumpId in targetIds)
            {
                Bitmap current = null;
                if (_regeneratedGumpImages.TryGetValue(gumpId, out var modified) && modified != null)
                    current = modified;
                else
                    current = GetGumpImage(gumpId);

                if (current == null)
                    continue;

                if (!_originalGumpImages.ContainsKey(gumpId))
                    _originalGumpImages[gumpId] = new Bitmap(current);

                using (var source = new Bitmap(_originalGumpImages[gumpId]))
                {
                    var result = ImageEffects.ApplyEffects(
                        source,
                        imageEditingPanel.Brightness,
                        imageEditingPanel.Contrast,
                        imageEditingPanel.Hue,
                        imageEditingPanel.Saturation,
                        imageEditingPanel.PixelSize,
                        imageEditingPanel.PixelizeEnabled,
                        imageEditingPanel.PaletteColors,
                        imageEditingPanel.NoiseIntensity,
                        imageEditingPanel.DitherLevels,
                        imageEditingPanel.EdgeDarkening,
                        imageEditingPanel.ColorBands,
                        imageEditingPanel.FillHoles,
                        imageEditingPanel.RemoveBackgroundEnabled,
                        imageEditingPanel.BackgroundMode,
                        imageEditingPanel.BackgroundColor,
                        imageEditingPanel.BackgroundThreshold
                    );

                    if (_regeneratedGumpImages.TryGetValue(gumpId, out var old))
                        old?.Dispose();
                    _regeneratedGumpImages[gumpId] = result;

                    if (gumpCache.TryGetValue(gumpId, out var oldCached))
                        oldCached?.Dispose();
                    gumpCache[gumpId] = new Bitmap(result);

                    foreach (var g in placedGumps)
                    {
                        if (g.GumpId == gumpId && g.Image != null)
                        {
                            g.Image.Dispose();
                            g.Image = new Bitmap(result);
                        }
                    }
                }

                applied++;
            }

            if (applied > 0)
            {
                aiSettingsPanel.SetPendingChanges(_regeneratedGumpImages.Count);
                aiSettingsPanel.SetOldNewEnabled(true);
                gumpListView.Invalidate();
                canvasBox.Invalidate();
                SetStatus($"Applied image FX to {applied} palette GUMP(s)");
            }
            else
            {
                SetStatus("No selected GUMPs could be processed.");
            }
        }
    }
}
