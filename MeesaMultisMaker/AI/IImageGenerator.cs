using System.Drawing;
using System.Threading.Tasks;

namespace MeesaMultisMaker.AI
{
    /// <summary>
    /// Unified interface for AI image generation backends
    /// Abstracts the ComfyUI backend behind a common API
    /// </summary>
    public interface IImageGenerator
    {
        /// <summary>
        /// Check if this backend is available and ready to use
        /// </summary>
        Task<bool> IsAvailable();

        /// <summary>
        /// Generate an image from text prompt
        /// </summary>
        Task<Bitmap> GenerateFromText(
            string prompt,
            string negativePrompt,
            int width,
            int height,
            int steps,
            double cfgScale,
            long? seed = null);

        /// <summary>
        /// Generate an image from input image (img2img)
        /// </summary>
        Task<Bitmap> GenerateFromImage(
            Bitmap inputImage,
            string prompt,
            string negativePrompt,
            int width,
            int height,
            int steps,
            double cfgScale,
            double denoise,
            long? seed = null);

        /// <summary>
        /// Generate an image with inpainting mask
        /// </summary>
        Task<Bitmap> GenerateWithMask(
            Bitmap inputImage,
            Bitmap maskImage,
            string prompt,
            string negativePrompt,
            int width,
            int height,
            int steps,
            double cfgScale,
            double denoise,
            long? seed = null);

        /// <summary>
        /// Get backend display name
        /// </summary>
        string GetName();

        /// <summary>
        /// Get backend description
        /// </summary>
        string GetDescription();
    }
}
