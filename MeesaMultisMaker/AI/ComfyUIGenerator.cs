using System;
using System.Drawing;
using System.Threading.Tasks;
using MeesaMultisMaker.ComfyUI;

namespace MeesaMultisMaker.AI
{
    /// <summary>
    /// ComfyUI implementation of IImageGenerator
    /// Wraps existing ComfyUIClient functionality
    /// </summary>
    public class ComfyUIGenerator : IImageGenerator
    {
        private readonly string _serverUrl;
        private ComfyUIClient _client;

        public ComfyUIGenerator(string serverUrl)
        {
            _serverUrl = serverUrl;
            _client = new ComfyUIClient(serverUrl);
        }

        public async Task<bool> IsAvailable()
        {
            try
            {
                return await _client.TestConnection();
            }
            catch
            {
                return false;
            }
        }

        public async Task<Bitmap> GenerateFromText(
            string prompt,
            string negativePrompt,
            int width,
            int height,
            int steps,
            double cfgScale,
            long? seed = null)
        {
            // Use existing Text2ImageWorkflow
            string workflow = Text2ImageWorkflow.CreateWorkflow(
                prompt,
                negativePrompt,
                width,
                height,
                steps,
                cfgScale,
                seed,
                "euler",
                "normal",
                Text2ImageWorkflow.DEFAULT_CHECKPOINT
            );

            string promptId = await _client.QueuePrompt(workflow);
            if (string.IsNullOrEmpty(promptId))
                throw new Exception("Failed to queue prompt");

            var images = await _client.GetGeneratedImages(promptId, maxAttempts: 120, pollIntervalMs: 1000);
            if (images.Count == 0)
                throw new Exception("No images generated");

            byte[] imageData = await _client.DownloadImage(images[0])
                ?? throw new Exception("Download from ComfyUI failed");
            using (var ms = new System.IO.MemoryStream(imageData))
            {
                return new Bitmap(ms);
            }
        }

        public async Task<Bitmap> GenerateFromImage(
            Bitmap inputImage,
            string prompt,
            string negativePrompt,
            int width,
            int height,
            int steps,
            double cfgScale,
            double denoise,
            long? seed = null)
        {
            // Upload input image
            byte[] imageBytes;
            using (var ms = new System.IO.MemoryStream())
            {
                inputImage.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                imageBytes = ms.ToArray();
            }

            string uploadedFilename = await _client.UploadImage(imageBytes, $"input_{Guid.NewGuid()}.png");

            // Use existing Image2ImageWorkflow
            string workflow = Image2ImageWorkflow.CreateWorkflow(
                prompt,
                negativePrompt,
                uploadedFilename,
                width,
                height,
                steps,
                cfgScale,
                denoise,
                seed,
                "euler",
                "normal",
                Text2ImageWorkflow.DEFAULT_CHECKPOINT
            );

            string promptId = await _client.QueuePrompt(workflow);
            if (string.IsNullOrEmpty(promptId))
                throw new Exception("Failed to queue prompt");

            var images = await _client.GetGeneratedImages(promptId, maxAttempts: 120, pollIntervalMs: 1000);
            if (images.Count == 0)
                throw new Exception("No images generated");

            byte[] imageData = await _client.DownloadImage(images[0])
                ?? throw new Exception("Download from ComfyUI failed");
            using (var ms = new System.IO.MemoryStream(imageData))
            {
                return new Bitmap(ms);
            }
        }

        public async Task<Bitmap> GenerateWithMask(
            Bitmap inputImage,
            Bitmap maskImage,
            string prompt,
            string negativePrompt,
            int width,
            int height,
            int steps,
            double cfgScale,
            double denoise,
            long? seed = null)
        {
            // Upload input image
            byte[] imageBytes;
            using (var ms = new System.IO.MemoryStream())
            {
                inputImage.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                imageBytes = ms.ToArray();
            }
            string uploadedImageName = await _client.UploadImage(imageBytes, $"input_{Guid.NewGuid()}.png");

            // Upload mask
            byte[] maskBytes;
            using (var ms = new System.IO.MemoryStream())
            {
                maskImage.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                maskBytes = ms.ToArray();
            }
            string uploadedMaskName = await _client.UploadImage(maskBytes, $"mask_{Guid.NewGuid()}.png");

            // Use existing InpaintWorkflow
            string workflow = InpaintWorkflow.CreateWorkflow(
                prompt,
                negativePrompt,
                uploadedImageName,
                uploadedMaskName,
                width,
                height,
                steps,
                cfgScale,
                denoise,
                seed,
                "euler",
                "normal",
                Text2ImageWorkflow.DEFAULT_CHECKPOINT
            );

            string promptId = await _client.QueuePrompt(workflow);
            if (string.IsNullOrEmpty(promptId))
                throw new Exception("Failed to queue prompt");

            var images = await _client.GetGeneratedImages(promptId, maxAttempts: 120, pollIntervalMs: 1000);
            if (images.Count == 0)
                throw new Exception("No images generated");

            byte[] imageData = await _client.DownloadImage(images[0])
                ?? throw new Exception("Download from ComfyUI failed");
            using (var ms = new System.IO.MemoryStream(imageData))
            {
                return new Bitmap(ms);
            }
        }

        public string GetName() => "ComfyUI";

        public string GetDescription() => "Server-based AI generation using ComfyUI with Stable Diffusion models";
    }
}
