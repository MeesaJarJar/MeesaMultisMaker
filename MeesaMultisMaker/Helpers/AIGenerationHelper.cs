using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using MeesaMultisMaker.AI;
using MeesaMultisMaker.Controls;

namespace MeesaMultisMaker.Helpers
{
    /// <summary>
    /// Helper class to simplify AI generation calls
    /// Handles backend selection and common operations
    /// </summary>
    public static class AIGenerationHelper
    {
        /// <summary>
        /// Generate an image using the currently selected AI backend
        /// </summary>
        public static async Task<Bitmap> GenerateImage(
            AISettingsPanel settings,
            Bitmap inputImage = null)
        {
            // Ensure backend is available
            var backend = settings.SelectedBackend;
            bool available = await ImageGeneratorFactory.EnsureBackendAvailable(
                backend,
                settings.FindForm());

            if (!available)
            {
                throw new InvalidOperationException($"{ImageGeneratorFactory.GetBackendName(backend)} is not available");
            }

            // Create generator
            IImageGenerator generator = ImageGeneratorFactory.CreateGenerator(backend, settings);

            // Generate based on whether we have input image
            Bitmap result;
            if (inputImage != null)
            {
                result = await generator.GenerateFromImage(
                    inputImage,
                    settings.Prompt,
                    settings.NegativePrompt,
                    settings.ResolutionWidth,
                    settings.ResolutionHeight,
                    settings.Steps,
                    settings.Cfg,
                    settings.Denoise,
                    settings.Seed
                );
            }
            else
            {
                result = await generator.GenerateFromText(
                    settings.Prompt,
                    settings.NegativePrompt,
                    settings.ResolutionWidth,
                    settings.ResolutionHeight,
                    settings.Steps,
                    settings.Cfg,
                    settings.Seed
                );
            }

            // Apply black pixel removal if enabled
            if (settings.DropBlackPixels && result != null)
            {
                result = RemoveBlackPixels(result, settings.BlackThreshold);
            }

            return result;
        }

        /// <summary>
        /// Generate with inpainting mask
        /// </summary>
        public static async Task<Bitmap> GenerateWithMask(
            AISettingsPanel settings,
            Bitmap inputImage,
            Bitmap maskImage)
        {
            var backend = settings.SelectedBackend;
            bool available = await ImageGeneratorFactory.EnsureBackendAvailable(
                backend,
                settings.FindForm());

            if (!available)
            {
                throw new InvalidOperationException($"{ImageGeneratorFactory.GetBackendName(backend)} is not available");
            }

            IImageGenerator generator = ImageGeneratorFactory.CreateGenerator(backend, settings);

            var result = await generator.GenerateWithMask(
                inputImage,
                maskImage,
                settings.Prompt,
                settings.NegativePrompt,
                settings.ResolutionWidth,
                settings.ResolutionHeight,
                settings.Steps,
                settings.Cfg,
                settings.Denoise,
                settings.Seed
            );

            if (settings.DropBlackPixels && result != null)
            {
                result = RemoveBlackPixels(result, settings.BlackThreshold);
            }

            return result;
        }

        /// <summary>
        /// Remove near-black pixels from an image
        /// </summary>
        private static Bitmap RemoveBlackPixels(Bitmap source, int threshold)
        {
            var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

            var sourceData = source.LockBits(
                new Rectangle(0, 0, source.Width, source.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            var resultData = result.LockBits(
                new Rectangle(0, 0, result.Width, result.Height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

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
                            // Make transparent
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

        /// <summary>
        /// Check if the selected backend is available
        /// </summary>
        public static async Task<bool> IsBackendAvailable(AISettingsPanel settings)
        {
            var backend = settings.SelectedBackend;
            IImageGenerator generator = ImageGeneratorFactory.CreateGenerator(backend, settings);
            return await generator.IsAvailable();
        }
    }
}
