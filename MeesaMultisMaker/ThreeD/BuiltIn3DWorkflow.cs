using System;
using System.Globalization;

namespace MeesaMultisMaker.ThreeD
{
    /// <summary>
    /// Built-in frozen copy of MultisMaker1.json as an API-format builder.
    /// No File.ReadAllText / template path: the graph that
    /// MultisMaker1Workflow.Build produced from the UI-format file
    /// (23 outer nodes + 17-node Flux.2 Klein subgraph f1793303-...)
    /// is emitted directly, with prompt / seeds / steps injected.
    ///
    /// Freeze source: D:\ComfyUIPortable\ComfyUI\user\default\workflows\MultisMaker1.json
    /// Outer nodes: 100 TripoSplatConditioning, 102 ComfySwitchNode,
    /// 103 TripoSplatPreprocessImage, 104 CLIPVisionLoader,
    /// 105 VAELoader (tripo), 106 VAELoader (flux2, feeds conditioning),
    /// 110 UNETLoader (tripo), 111 ComfySwitchNode, 112 TripoSplatSamplingPreview,
    /// 113 RemoveBackground, 114 LoadBackgroundRemovalModel, 115 InvertMask,
    /// 116 JoinImageWithAlpha, 101 KSampler, 107 VAEDecodeTripoSplat,
    /// 92 SplatToFile3D, 132 LoadImage, 138 PrimitiveStringMultiline,
    /// 133/145 PreviewImage, 144 PreviewGaussianSplat.
    /// Dropped: 134 JarJarSubmit3D (server-side upload we don't need).
    /// Sinks: mm_saveply SaveGLB (3d/PLY_, the real OUTPUT_NODE for 3D),
    /// mm_saveimg SaveImage (mm_init, OUTPUT_NODE for flux-only 2D).
    ///
    /// Flux subgraph (node 131 shell expanded inline, prefix sg131_):
    /// 70 UNETLoader, 71 CLIPLoader, 72 VAELoader, 80 ImageScaleToTotalPixels,
    /// 130 GetImageSize, 122 VAEEncode, 74 CLIPTextEncode, 82 ConditioningZeroOut,
    /// 123 ReferenceLatent (positive), 121 ReferenceLatent (negative),
    /// 66 EmptyFlux2LatentImage, 62 Flux2Scheduler, 63 CFGGuider,
    /// 61 KSamplerSelect, 73 RandomNoise, 64 SamplerCustomAdvanced, 65 VAEDecode.
    /// Outer 131 outputs rewrite to sg131_65:0 (VAEDecode IMAGE).
    /// JoinImageWithAlpha (node 116) is a core-ComfyUI node, not a pack node.
    /// </summary>
    public static class BuiltIn3DWorkflow
    {
        // Tripo / conditioning models (frozen from MultisMaker1.json widgets).
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";

        // Flux.2 Klein image-stage models (same as ComfyUI Flux2KleinWorkflow).
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";

        public const int DEFAULT_NUM_GAUSSIANS = 259200;
        public const int DEFAULT_TRIPO_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;

        // Frozen widget values.
        public const int TRIPO_CFG = 3;
        public const double TRIPO_DENOISE = 1.0;
        public const string TRIPO_SAMPLER = "euler";
        public const string TRIPO_SCHEDULER = "simple";
        public const int TRIPO_ERODE_RADIUS = 1;
        public const int TRIPO_PREPROCESS_SIZE = 1024;
        public const int PREVIEW_OCTREE_LEVEL = 5;
        public const int PREVIEW_NUM_GAUSSIANS = 36000;
        public const int PREVIEW_YAW = 90;
        public const int PREVIEW_PITCH = 15;
        public const int PREVIEW_POINT_SIZE = 2;
        public const double FLUX_CFG = 1.0;
        public const string FLUX_SAMPLER = "euler";
        public const double FLUX_MEGAPIXELS = 0.25;
        public const string FLUX_UPSCALE_METHOD = "nearest-exact";

        /// <param name="fluxOnly">2D-only: just the Flux image stage plus
        /// SaveImage (for "Generate Init Image"), no 3D nodes.</param>
        /// <param name="skipFlux">3D without the Flux stage: the staged init
        /// image feeds Tripo/remove-background directly (for "Generate 3D").
        /// Mutually exclusive with fluxOnly (fluxOnly wins).</param>
        public static string CreateWorkflow(
            string prompt,
            long seed,
            int steps,
            int numGaussians = DEFAULT_NUM_GAUSSIANS,
            string inputImageFilename = "",
            int fluxSteps = DEFAULT_FLUX_STEPS,
            bool fluxOnly = false,
            bool skipFlux = false,
            string tripoUnet = null,
            string tripoVae = null,
            string condVae = null,
            string clipVision = null,
            string bgRemoval = null,
            string fluxUnet = null,
            string fluxClip = null,
            string fluxVae = null)
        {
            if (seed < 0)
                seed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (seed < 0) seed = -seed;
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;

            long kSeed = seed & 0x7FFFFFFFFFFFFFFFL;
            long fluxSeed = (seed ^ 0x9E3779B9L) & 0x7FFFFFFFFFFFFFFFL;

            prompt = EscapeJson(prompt ?? string.Empty);
            inputImageFilename = EscapeJson(inputImageFilename ?? string.Empty);
            tripoUnet = EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            tripoVae = EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            condVae = EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            clipVision = EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            bgRemoval = EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            fluxUnet = EscapeJson(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            fluxClip = EscapeJson(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            fluxVae = EscapeJson(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);

            var inv = CultureInfo.InvariantCulture;
            string kSeedStr = kSeed.ToString(inv);
            string fluxSeedStr = fluxSeed.ToString(inv);

            if (fluxOnly)
                return FluxOnlyJson(prompt, inputImageFilename, fluxSteps, fluxSeedStr,
                    fluxUnet, fluxClip, fluxVae);

            string fluxOutputRef = skipFlux ? "[\"132\", 0]" : "[\"sg131_65\", 0]";

            string fluxStage = skipFlux ? string.Empty : FluxStageJson(
                prompt, inputImageFilename, fluxSteps, fluxSeedStr,
                fluxUnet, fluxClip, fluxVae);

            string promptNode = skipFlux ? string.Empty :
                "  \"138\": {" + "\n" +
                "    \"inputs\": {" + "\n" +
                "      \"value\": \"" + prompt + "\"\n" +
                "    }," + "\n" +
                "    \"class_type\": \"PrimitiveStringMultiline\"\n" +
                "  }," + "\n";

            // NOTE: braces below are literal JSON braces inside an interpolated
            // verbatim string only in the Flux2Klein* builders; here plain
            // concatenation is used so no brace-escaping drift is possible.
            return "{" + "\n" +
                promptNode +
                fluxStage +
                "  \"100\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip_vision\": [\"104\", 0],\n" +
                "      \"vae\": [\"106\", 0],\n" +
                "      \"image\": [\"103\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"TripoSplatConditioning\"\n" +
                "  },\n" +
                "  \"102\": {\n" +
                "    \"inputs\": {\n" +
                "      \"on_true\": [\"113\", 0],\n" +
                "      \"switch\": true\n" +
                "    },\n" +
                "    \"class_type\": \"ComfySwitchNode\"\n" +
                "  },\n" +
                "  \"103\": {\n" +
                "    \"inputs\": {\n" +
                "      \"image\": " + fluxOutputRef + ",\n" +
                "      \"mask\": [\"102\", 0],\n" +
                "      \"erode_radius\": " + TRIPO_ERODE_RADIUS.ToString(inv) + ",\n" +
                "      \"size\": " + TRIPO_PREPROCESS_SIZE.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"TripoSplatPreprocessImage\"\n" +
                "  },\n" +
                "  \"104\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip_name\": \"" + clipVision + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPVisionLoader\"\n" +
                "  },\n" +
                "  \"105\": {\n" +
                "    \"inputs\": {\n" +
                "      \"vae_name\": \"" + tripoVae + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"VAELoader\"\n" +
                "  },\n" +
                "  \"106\": {\n" +
                "    \"inputs\": {\n" +
                "      \"vae_name\": \"" + condVae + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"VAELoader\"\n" +
                "  },\n" +
                "  \"110\": {\n" +
                "    \"inputs\": {\n" +
                "      \"unet_name\": \"" + tripoUnet + "\",\n" +
                "      \"weight_dtype\": \"default\"\n" +
                "    },\n" +
                "    \"class_type\": \"UNETLoader\"\n" +
                "  },\n" +
                "  \"111\": {\n" +
                "    \"inputs\": {\n" +
                "      \"on_false\": [\"110\", 0],\n" +
                "      \"on_true\": [\"112\", 0],\n" +
                "      \"switch\": true\n" +
                "    },\n" +
                "    \"class_type\": \"ComfySwitchNode\"\n" +
                "  },\n" +
                "  \"112\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"110\", 0],\n" +
                "      \"vae\": [\"105\", 0],\n" +
                "      \"octree_level\": " + PREVIEW_OCTREE_LEVEL.ToString(inv) + ",\n" +
                "      \"num_gaussians\": " + PREVIEW_NUM_GAUSSIANS.ToString(inv) + ",\n" +
                "      \"yaw\": " + PREVIEW_YAW.ToString(inv) + ",\n" +
                "      \"pitch\": " + PREVIEW_PITCH.ToString(inv) + ",\n" +
                "      \"point_size\": " + PREVIEW_POINT_SIZE.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"TripoSplatSamplingPreview\"\n" +
                "  },\n" +
                "  \"113\": {\n" +
                "    \"inputs\": {\n" +
                "      \"bg_removal_model\": [\"114\", 0],\n" +
                "      \"image\": " + fluxOutputRef + "\n" +
                "    },\n" +
                "    \"class_type\": \"RemoveBackground\"\n" +
                "  },\n" +
                "  \"114\": {\n" +
                "    \"inputs\": {\n" +
                "      \"bg_removal_name\": \"" + bgRemoval + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"LoadBackgroundRemovalModel\"\n" +
                "  },\n" +
                "  \"115\": {\n" +
                "    \"inputs\": {\n" +
                "      \"mask\": [\"113\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"InvertMask\"\n" +
                "  },\n" +
                "  \"116\": {\n" +
                "    \"inputs\": {\n" +
                "      \"image\": " + fluxOutputRef + ",\n" +
                "      \"alpha\": [\"115\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"JoinImageWithAlpha\"\n" +
                "  },\n" +
                "  \"101\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"111\", 0],\n" +
                "      \"positive\": [\"100\", 0],\n" +
                "      \"negative\": [\"100\", 1],\n" +
                "      \"latent_image\": [\"100\", 2],\n" +
                "      \"seed\": " + kSeedStr + ",\n" +
                "      \"steps\": " + steps.ToString(inv) + ",\n" +
                "      \"cfg\": " + TRIPO_CFG.ToString(inv) + ",\n" +
                "      \"sampler_name\": \"" + TRIPO_SAMPLER + "\",\n" +
                "      \"scheduler\": \"" + TRIPO_SCHEDULER + "\",\n" +
                "      \"denoise\": 1\n" +
                "    },\n" +
                "    \"class_type\": \"KSampler\"\n" +
                "  },\n" +
                "  \"107\": {\n" +
                "    \"inputs\": {\n" +
                "      \"samples\": [\"101\", 0],\n" +
                "      \"vae\": [\"105\", 0],\n" +
                "      \"num_gaussians\": " + numGaussians.ToString(inv) + ",\n" +
                "      \"seed\": " + fluxSeedStr + "\n" +
                "    },\n" +
                "    \"class_type\": \"VAEDecodeTripoSplat\"\n" +
                "  },\n" +
                "  \"92\": {\n" +
                "    \"inputs\": {\n" +
                "      \"splat\": [\"107\", 0],\n" +
                "      \"format\": \"ply\"\n" +
                "    },\n" +
                "    \"class_type\": \"SplatToFile3D\"\n" +
                "  },\n" +
                "  \"132\": {\n" +
                "    \"inputs\": {\n" +
                "      \"image\": \"" + inputImageFilename + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"LoadImage\"\n" +
                "  },\n" +
                "  \"133\": {\n" +
                "    \"inputs\": {\n" +
                "      \"images\": " + fluxOutputRef + "\n" +
                "    },\n" +
                "    \"class_type\": \"PreviewImage\"\n" +
                "  },\n" +
                "  \"144\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model_3d\": [\"92\", 0],\n" +
                "      \"viewport_state\": \"\",\n" +
                "      \"width\": 1024,\n" +
                "      \"height\": 1024\n" +
                "    },\n" +
                "    \"class_type\": \"PreviewGaussianSplat\"\n" +
                "  },\n" +
                "  \"145\": {\n" +
                "    \"inputs\": {\n" +
                "      \"images\": [\"116\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"PreviewImage\"\n" +
                "  },\n" +
                "  \"mm_saveply\": {\n" +
                "    \"inputs\": {\n" +
                "      \"mesh\": [\"92\", 0],\n" +
                "      \"filename_prefix\": \"3d/PLY_\"\n" +
                "    },\n" +
                "    \"class_type\": \"SaveGLB\"\n" +
                "  }\n" +
                "}";
        }

        private static string FluxOnlyJson(string prompt, string inputImage, int fluxSteps,
            string fluxSeedStr, string fluxUnet, string fluxClip, string fluxVae)
        {
            return "{" + "\n" +
                "  \"132\": {\n" +
                "    \"inputs\": {\n" +
                "      \"image\": \"" + inputImage + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"LoadImage\"\n" +
                "  },\n" +
                "  \"138\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": \"" + prompt + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveStringMultiline\"\n" +
                "  },\n" +
                FluxStageJson(prompt, inputImage, fluxSteps, fluxSeedStr, fluxUnet, fluxClip, fluxVae) +
                "  \"mm_saveimg\": {\n" +
                "    \"inputs\": {\n" +
                "      \"filename_prefix\": \"mm_init\",\n" +
                "      \"images\": [\"sg131_65\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"SaveImage\"\n" +
                "  }\n" +
                "}";
        }

        private static string FluxStageJson(string prompt, string inputImage, int fluxSteps,
            string fluxSeedStr, string fluxUnet, string fluxClip, string fluxVae,
            bool isLast = false)
        {
            var inv = CultureInfo.InvariantCulture;
            string comma = isLast ? string.Empty : ",";
            string mpStr = FLUX_MEGAPIXELS.ToString("0.##", inv);
            // Frozen file stores cfg/denoise as int 1; emit 1 (not 1.0) to match.
            return
                "  \"sg131_70\": {\n" +
                "    \"inputs\": {\n" +
                "      \"unet_name\": \"" + fluxUnet + "\",\n" +
                "      \"weight_dtype\": \"default\"\n" +
                "    },\n" +
                "    \"class_type\": \"UNETLoader\"\n" +
                "  },\n" +
                "  \"sg131_71\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip_name\": \"" + fluxClip + "\",\n" +
                "      \"type\": \"flux2\",\n" +
                "      \"device\": \"default\"\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPLoader\"\n" +
                "  },\n" +
                "  \"sg131_72\": {\n" +
                "    \"inputs\": {\n" +
                "      \"vae_name\": \"" + fluxVae + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"VAELoader\"\n" +
                "  },\n" +
                "  \"sg131_80\": {\n" +
                "    \"inputs\": {\n" +
                "      \"image\": [\"132\", 0],\n" +
                "      \"upscale_method\": \"" + FLUX_UPSCALE_METHOD + "\",\n" +
                "      \"megapixels\": " + mpStr + ",\n" +
                "      \"resolution_steps\": 1\n" +
                "    },\n" +
                "    \"class_type\": \"ImageScaleToTotalPixels\"\n" +
                "  },\n" +
                "  \"sg131_130\": {\n" +
                "    \"inputs\": {\n" +
                "      \"image\": [\"sg131_80\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"GetImageSize\"\n" +
                "  },\n" +
                "  \"sg131_122\": {\n" +
                "    \"inputs\": {\n" +
                "      \"pixels\": [\"sg131_80\", 0],\n" +
                "      \"vae\": [\"sg131_72\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"VAEEncode\"\n" +
                "  },\n" +
                "  \"sg131_74\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip\": [\"sg131_71\", 0],\n" +
                "      \"text\": [\"138\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPTextEncode\"\n" +
                "  },\n" +
                "  \"sg131_82\": {\n" +
                "    \"inputs\": {\n" +
                "      \"conditioning\": [\"sg131_74\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"ConditioningZeroOut\"\n" +
                "  },\n" +
                "  \"sg131_123\": {\n" +
                "    \"inputs\": {\n" +
                "      \"conditioning\": [\"sg131_74\", 0],\n" +
                "      \"latent\": [\"sg131_122\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"ReferenceLatent\"\n" +
                "  },\n" +
                "  \"sg131_121\": {\n" +
                "    \"inputs\": {\n" +
                "      \"conditioning\": [\"sg131_82\", 0],\n" +
                "      \"latent\": [\"sg131_122\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"ReferenceLatent\"\n" +
                "  },\n" +
                "  \"sg131_66\": {\n" +
                "    \"inputs\": {\n" +
                "      \"width\": [\"sg131_130\", 0],\n" +
                "      \"height\": [\"sg131_130\", 1],\n" +
                "      \"batch_size\": 1\n" +
                "    },\n" +
                "    \"class_type\": \"EmptyFlux2LatentImage\"\n" +
                "  },\n" +
                "  \"sg131_62\": {\n" +
                "    \"inputs\": {\n" +
                "      \"steps\": " + fluxSteps.ToString(inv) + ",\n" +
                "      \"width\": [\"sg131_130\", 0],\n" +
                "      \"height\": [\"sg131_130\", 1]\n" +
                "    },\n" +
                "    \"class_type\": \"Flux2Scheduler\"\n" +
                "  },\n" +
                "  \"sg131_63\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"sg131_70\", 0],\n" +
                "      \"positive\": [\"sg131_123\", 0],\n" +
                "      \"negative\": [\"sg131_121\", 0],\n" +
                "      \"cfg\": 1\n" +
                "    },\n" +
                "    \"class_type\": \"CFGGuider\"\n" +
                "  },\n" +
                "  \"sg131_61\": {\n" +
                "    \"inputs\": {\n" +
                "      \"sampler_name\": \"" + FLUX_SAMPLER + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"KSamplerSelect\"\n" +
                "  },\n" +
                "  \"sg131_73\": {\n" +
                "    \"inputs\": {\n" +
                "      \"noise_seed\": " + fluxSeedStr + "\n" +
                "    },\n" +
                "    \"class_type\": \"RandomNoise\"\n" +
                "  },\n" +
                "  \"sg131_64\": {\n" +
                "    \"inputs\": {\n" +
                "      \"noise\": [\"sg131_73\", 0],\n" +
                "      \"guider\": [\"sg131_63\", 0],\n" +
                "      \"sampler\": [\"sg131_61\", 0],\n" +
                "      \"sigmas\": [\"sg131_62\", 0],\n" +
                "      \"latent_image\": [\"sg131_66\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"SamplerCustomAdvanced\"\n" +
                "  },\n" +
                "  \"sg131_65\": {\n" +
                "    \"inputs\": {\n" +
                "      \"samples\": [\"sg131_64\", 0],\n" +
                "      \"vae\": [\"sg131_72\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"VAEDecode\"\n" +
                "  }" + comma + "\n";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }
}
