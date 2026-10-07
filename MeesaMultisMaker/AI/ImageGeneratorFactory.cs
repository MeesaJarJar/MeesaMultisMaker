    using System;
using System.Drawing;
using System.Threading.Tasks;
using MeesaMultisMaker.Controls;

namespace MeesaMultisMaker.AI
{
    public static class ImageGeneratorFactory
    {
        /// <summary>
        /// Create an image generator based on the selected backend
        /// </summary>
        public static IImageGenerator CreateGenerator(AIBackend backend, AISettingsPanel settings)
        {
            switch (backend)
            {
                case AIBackend.ComfyUI:
                    return new ComfyUIGenerator(settings.ComfyUrl);
                
                default:
                    throw new ArgumentException($"Unknown AI backend: {backend}");
            }
        }

        /// <summary>
        /// Create an image generator from the current app config
        /// </summary>
        public static IImageGenerator CreateFromConfig()
        {
            var config = AppConfig.Instance;
            
            switch (config.SelectedAIBackend)
            {
                case AIBackend.ComfyUI:
                    return new ComfyUIGenerator(config.ComfyUIUrl);
                
                default:
                    return new ComfyUIGenerator(config.ComfyUIUrl);
            }
        }

        /// <summary>
        /// Ensure the selected backend is available and ready
        /// If not, prompt user to set it up
        /// </summary>
        public static async Task<bool> EnsureBackendAvailable(
            AIBackend backend,
            System.Windows.Forms.Form parentForm)
        {
            switch (backend)
            {
                case AIBackend.ComfyUI:
                    // ComfyUI just needs a running server, no setup required here
                    return true;
                
                default:
                    return false;
            }
        }

        /// <summary>
        /// Get a user-friendly name for the backend
        /// </summary>
        public static string GetBackendName(AIBackend backend)
        {
            switch (backend)
            {
                case AIBackend.ComfyUI:
                    return "ComfyUI (Server)";
                default:
                    return "Unknown";
            }
        }

        /// <summary>
        /// Get a description of the backend
        /// </summary>
        public static string GetBackendDescription(AIBackend backend)
        {
            switch (backend)
            {
                case AIBackend.ComfyUI:
                    return "Requires a running ComfyUI server. Supports many Stable Diffusion models and custom workflows.";
                default:
                    return "";
            }
        }
    }
}
