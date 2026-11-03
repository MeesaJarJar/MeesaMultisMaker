using System;
using System.Globalization;

namespace MeesaMultisMaker.ThreeD
{
    /// <summary>
    /// Extended and experimental 3D workflow variants as built-in API-format (prompt) builders.
    /// Each static class below freezes one UI-format ComfyUI workflow (see per-class headers for
    /// sources, node inventories, and dropped/bypassed nodes) as a string-concatenated JSON graph,
    /// following the BuiltIn3DWorkflow pattern: string concat, ExtendedJson.Escape, DEFAULT_* consts.
    /// Subgraph instances are expanded inline (sg-prefixed ids); Reroutes resolved; display-only
    /// nodes (MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck) dropped. No file I/O.
    /// </summary>
    internal static class ExtendedJson
    {
        public static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Armor-part factory: Flux.2 Klein T-pose armor edit feeding one TripoSplat chain, then JarJar submit + UniRig rig.
    /// Freeze source(s):
    /// - D:\ComfyUIPortable\ComfyUI\user\default\workflows\ArmorGen.json
    /// UI nodes: 153 (source-muted/bypassed, kept active unless noted: 141, 169, 187, 205, 223, 224, 226, 245, 246, 284, 285, 287, 289, 290, ...).
    /// API nodes: 41.
    /// Graph nodes:
    /// 9: SaveImage
    /// 76: LoadImage
    /// 248: SeedNode
    /// 292: CLIPVisionLoader
    /// 295: VAELoader
    /// 302: VAELoader
    /// 303: LoadBackgroundRemovalModel
    /// 304: UNETLoader
    /// 416: PrimitiveString
    /// 435: TripoSplatPreprocessImage
    /// 436: TripoSplatConditioning
    /// 437: VAEDecodeTripoSplat
    /// 438: KSampler
    /// 439: JarJarSplatToFilePath
    /// 440: PreviewGaussianSplat
    /// 441: TripoSplatSamplingPreview
    /// 442: JoinImageWithAlpha
    /// 443: PreviewImage
    /// 444: StringConcatenate
    /// 445: InvertMask
    /// 446: RemoveBackground
    /// 447: JarJarSubmit3D
    /// 448: JarJarUnirig
    /// 449: JarJarLoadSplatPly
    /// sg75_121: ReferenceLatent
    /// sg75_122: VAEEncode
    /// sg75_123: ReferenceLatent
    /// sg75_61: KSamplerSelect
    /// sg75_62: Flux2Scheduler
    /// sg75_63: CFGGuider
    /// sg75_64: SamplerCustomAdvanced
    /// sg75_65: VAEDecode
    /// sg75_66: EmptyFlux2LatentImage
    /// sg75_70: UNETLoader
    /// sg75_71: CLIPLoader
    /// sg75_72: VAELoader
    /// sg75_73: RandomNoise
    /// sg75_74: CLIPTextEncode
    /// sg75_80: ImageScaleToTotalPixels
    /// sg75_82: ConditioningZeroOut
    /// sg75_99: GetImageSize
    /// Sinks (no downstream consumers): 440 PreviewGaussianSplat, 443 PreviewImage, 447 JarJarSubmit3D, 448 JarJarUnirig.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// Freeze keeps the single unbypassed chain: LoadImage 76 -> Flux-Edit subgraph 75 (expanded inline as sg75_*) -> SaveImage 9 -> TripoSplat chain 435/436/438/441/437 -> JarJarSplatToFilePath 439 -> JarJarSubmit3D 447, plus JarJarLoadSplatPly 449 -> JarJarUnirig 448.
    /// Dropped 116 bypassed nodes: 9 Flux-Edit part chains, 9 TripoSplat part chains with their previews/filenames, 5 dead PreviewImages (225/227/247/286/323); 2 Reroutes resolved.
    /// </summary>
    public static class ArmorGenWorkflow
    {
        public const int DEFAULT_SEED = 5;
        public const int DEFAULT_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const int DEFAULT_NUM_GAUSSIANS = 1048576;
        public const string DEFAULT_INPUT_IMAGE = "Gump 0xF0E4.png";
        public const string DEFAULT_PROMPT = "Generate a T-Pose full medieval armor suit and helmet of this exact input image type and style, no shadow, no human showing, front view black background";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_SUBMIT_PROMPT = "Test23";
        public const string DEFAULT_SUBMIT_API = "https://meesajarjar.com/foundry-api.php";
        public const string DEFAULT_SUBMIT_EXT = ".ply";
        public const string DEFAULT_UNIRIG_OUTPUT = "./output";
        public const string DEFAULT_UNIRIG_FILENAME = "rigged_model.fbx";
        public const string DEFAULT_BLENDER = "C:\\Program Files\\Blender Foundation\\Blender 5.1\\blender.exe";
        public const string DEFAULT_SKELETON = "mixamo";
        public const string DEFAULT_FILENAME_BASE = "platemail_001_";
        public const string DEFAULT_FILENAME_PART = "FullBody";
        public const string DEFAULT_SAVE_PREFIX = "Flux2-Klein";

        public static string CreateWorkflow(
            long seed = DEFAULT_SEED,
            int steps = DEFAULT_STEPS,
            int fluxSteps = DEFAULT_FLUX_STEPS,
            int numGaussians = DEFAULT_NUM_GAUSSIANS,
            string inputImage = DEFAULT_INPUT_IMAGE,
            string prompt = DEFAULT_PROMPT,
            string fluxUnet = DEFAULT_FLUX_UNET,
            string fluxClip = DEFAULT_FLUX_CLIP,
            string fluxVae = DEFAULT_FLUX_VAE,
            string tripoUnet = DEFAULT_TRIPO_UNET,
            string tripoVae = DEFAULT_TRIPO_VAE,
            string condVae = DEFAULT_COND_VAE,
            string clipVision = DEFAULT_CLIP_VISION,
            string bgRemoval = DEFAULT_BG_REMOVAL
        )
        {
            if (seed < 0)
                seed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (seed < 0) seed = -seed;
            if (steps < 1) steps = DEFAULT_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            var inv = CultureInfo.InvariantCulture;
            string seedStr = (seed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string escInputImage = ExtendedJson.Escape(inputImage ?? string.Empty);
            string escPrompt = ExtendedJson.Escape(prompt ?? string.Empty);
            fluxUnet = ExtendedJson.Escape(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            fluxClip = ExtendedJson.Escape(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            fluxVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);
            tripoUnet = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            tripoVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            condVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            clipVision = ExtendedJson.Escape(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            bgRemoval = ExtendedJson.Escape(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            return "{" + "\n" +
            "  \"9\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"sg75_65\", 0],\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_SAVE_PREFIX) + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SaveImage\"\n" +
            "  },\n" +
            "  \"76\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + escInputImage + "\",\n" +
            "      \"upload\": \"" + "image" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  },\n" +
            "  \"248\": {\n" +
            "    \"inputs\": {\n" +
            "      \"seed\": " + seedStr + "\n" +
            "    },\n" +
            "    \"class_type\": \"SeedNode\"\n" +
            "  },\n" +
            "  \"292\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip_name\": \"" + clipVision + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"CLIPVisionLoader\"\n" +
            "  },\n" +
            "  \"295\": {\n" +
            "    \"inputs\": {\n" +
            "      \"vae_name\": \"" + tripoVae + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"VAELoader\"\n" +
            "  },\n" +
            "  \"302\": {\n" +
            "    \"inputs\": {\n" +
            "      \"vae_name\": \"" + condVae + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"VAELoader\"\n" +
            "  },\n" +
            "  \"303\": {\n" +
            "    \"inputs\": {\n" +
            "      \"bg_removal_name\": \"" + bgRemoval + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadBackgroundRemovalModel\"\n" +
            "  },\n" +
            "  \"304\": {\n" +
            "    \"inputs\": {\n" +
            "      \"unet_name\": \"" + tripoUnet + "\",\n" +
            "      \"weight_dtype\": \"" + "default" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UNETLoader\"\n" +
            "  },\n" +
            "  \"416\": {\n" +
            "    \"inputs\": {\n" +
            "      \"value\": \"" + ExtendedJson.Escape(DEFAULT_FILENAME_BASE) + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"PrimitiveString\"\n" +
            "  },\n" +
            "  \"435\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"9\", 0],\n" +
            "      \"mask\": [\"446\", 0],\n" +
            "      \"erode_radius\": 1,\n" +
            "      \"size\": 1024\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatPreprocessImage\"\n" +
            "  },\n" +
            "  \"436\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip_vision\": [\"292\", 0],\n" +
            "      \"vae\": [\"302\", 0],\n" +
            "      \"image\": [\"435\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatConditioning\"\n" +
            "  },\n" +
            "  \"437\": {\n" +
            "    \"inputs\": {\n" +
            "      \"samples\": [\"438\", 0],\n" +
            "      \"vae\": [\"295\", 0],\n" +
            "      \"num_gaussians\": " + numGaussians.ToString(inv) + ",\n" +
            "      \"seed\": " + seedStr + "\n" +
            "    },\n" +
            "    \"class_type\": \"VAEDecodeTripoSplat\"\n" +
            "  },\n" +
            "  \"438\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"441\", 0],\n" +
            "      \"positive\": [\"436\", 0],\n" +
            "      \"negative\": [\"436\", 1],\n" +
            "      \"latent_image\": [\"436\", 2],\n" +
            "      \"seed\": " + seedStr + ",\n" +
            "      \"steps\": " + steps.ToString(inv) + ",\n" +
            "      \"cfg\": 6,\n" +
            "      \"sampler_name\": \"" + "euler" + "\",\n" +
            "      \"scheduler\": \"" + "simple" + "\",\n" +
            "      \"denoise\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"KSampler\"\n" +
            "  },\n" +
            "  \"439\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat\": [\"437\", 0],\n" +
            "      \"format\": \"" + "ply" + "\",\n" +
            "      \"filename\": [\"444\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarSplatToFilePath\"\n" +
            "  },\n" +
            "  \"440\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model_3d\": [\"439\", 0],\n" +
            "      \"viewport_state\": \"" + "" + "\",\n" +
            "      \"width\": 1024,\n" +
            "      \"height\": 1024\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewGaussianSplat\"\n" +
            "  },\n" +
            "  \"441\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"304\", 0],\n" +
            "      \"vae\": [\"295\", 0],\n" +
            "      \"octree_level\": 5,\n" +
            "      \"num_gaussians\": 36000,\n" +
            "      \"yaw\": 90,\n" +
            "      \"pitch\": 15,\n" +
            "      \"point_size\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatSamplingPreview\"\n" +
            "  },\n" +
            "  \"442\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"9\", 0],\n" +
            "      \"alpha\": [\"445\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"JoinImageWithAlpha\"\n" +
            "  },\n" +
            "  \"443\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"442\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"444\": {\n" +
            "    \"inputs\": {\n" +
            "      \"string_a\": [\"416\", 0],\n" +
            "      \"string_b\": \"" + ExtendedJson.Escape(DEFAULT_FILENAME_PART) + "\",\n" +
            "      \"delimiter\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"StringConcatenate\"\n" +
            "  },\n" +
            "  \"445\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mask\": [\"446\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"InvertMask\"\n" +
            "  },\n" +
            "  \"446\": {\n" +
            "    \"inputs\": {\n" +
            "      \"bg_removal_model\": [\"303\", 0],\n" +
            "      \"image\": [\"9\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"RemoveBackground\"\n" +
            "  },\n" +
            "  \"447\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model_3d\": [\"439\", 0],\n" +
            "      \"prompt\": \"" + ExtendedJson.Escape(DEFAULT_SUBMIT_PROMPT) + "\",\n" +
            "      \"api_url\": \"" + ExtendedJson.Escape(DEFAULT_SUBMIT_API) + "\",\n" +
            "      \"filename\": \"" + ExtendedJson.Escape(DEFAULT_SUBMIT_EXT) + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarSubmit3D\"\n" +
            "  },\n" +
            "  \"448\": {\n" +
            "    \"inputs\": {\n" +
            "      \"trimesh\": [\"449\", 0],\n" +
            "      \"output_folder\": \"" + ExtendedJson.Escape(DEFAULT_UNIRIG_OUTPUT) + "\",\n" +
            "      \"filename\": \"" + ExtendedJson.Escape(DEFAULT_UNIRIG_FILENAME) + "\",\n" +
            "      \"blender_path\": \"" + ExtendedJson.Escape(DEFAULT_BLENDER) + "\",\n" +
            "      \"skeleton_template\": \"" + ExtendedJson.Escape(DEFAULT_SKELETON) + "\",\n" +
            "      \"auto_weights\": true\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarUnirig\"\n" +
            "  },\n" +
            "  \"449\": {\n" +
            "    \"inputs\": {\n" +
            "      \"file_path\": [\"439\", 1],\n" +
            "      \"opacity_cutoff\": 0.05,\n" +
            "      \"poisson_depth\": 9,\n" +
            "      \"max_points\": 100000,\n" +
            "      \"rotate_x\": 0,\n" +
            "      \"rotate_y\": 0,\n" +
            "      \"rotate_z\": 0\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarLoadSplatPly\"\n" +
            "  },\n" +
            "  \"sg75_121\": {\n" +
            "    \"inputs\": {\n" +
            "      \"conditioning\": [\"sg75_82\", 0],\n" +
            "      \"latent\": [\"sg75_122\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"ReferenceLatent\"\n" +
            "  },\n" +
            "  \"sg75_122\": {\n" +
            "    \"inputs\": {\n" +
            "      \"pixels\": [\"sg75_80\", 0],\n" +
            "      \"vae\": [\"sg75_72\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"VAEEncode\"\n" +
            "  },\n" +
            "  \"sg75_123\": {\n" +
            "    \"inputs\": {\n" +
            "      \"conditioning\": [\"sg75_74\", 0],\n" +
            "      \"latent\": [\"sg75_122\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"ReferenceLatent\"\n" +
            "  },\n" +
            "  \"sg75_61\": {\n" +
            "    \"inputs\": {\n" +
            "      \"sampler_name\": \"" + "euler" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"KSamplerSelect\"\n" +
            "  },\n" +
            "  \"sg75_62\": {\n" +
            "    \"inputs\": {\n" +
            "      \"steps\": " + fluxSteps.ToString(inv) + ",\n" +
            "      \"width\": [\"sg75_99\", 0],\n" +
            "      \"height\": [\"sg75_99\", 1]\n" +
            "    },\n" +
            "    \"class_type\": \"Flux2Scheduler\"\n" +
            "  },\n" +
            "  \"sg75_63\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"sg75_70\", 0],\n" +
            "      \"positive\": [\"sg75_123\", 0],\n" +
            "      \"negative\": [\"sg75_121\", 0],\n" +
            "      \"cfg\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"CFGGuider\"\n" +
            "  },\n" +
            "  \"sg75_64\": {\n" +
            "    \"inputs\": {\n" +
            "      \"noise\": [\"sg75_73\", 0],\n" +
            "      \"guider\": [\"sg75_63\", 0],\n" +
            "      \"sampler\": [\"sg75_61\", 0],\n" +
            "      \"sigmas\": [\"sg75_62\", 0],\n" +
            "      \"latent_image\": [\"sg75_66\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"SamplerCustomAdvanced\"\n" +
            "  },\n" +
            "  \"sg75_65\": {\n" +
            "    \"inputs\": {\n" +
            "      \"samples\": [\"sg75_64\", 0],\n" +
            "      \"vae\": [\"sg75_72\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"VAEDecode\"\n" +
            "  },\n" +
            "  \"sg75_66\": {\n" +
            "    \"inputs\": {\n" +
            "      \"width\": [\"sg75_99\", 0],\n" +
            "      \"height\": [\"sg75_99\", 1],\n" +
            "      \"batch_size\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"EmptyFlux2LatentImage\"\n" +
            "  },\n" +
            "  \"sg75_70\": {\n" +
            "    \"inputs\": {\n" +
            "      \"unet_name\": \"" + fluxUnet + "\",\n" +
            "      \"weight_dtype\": \"" + "default" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UNETLoader\"\n" +
            "  },\n" +
            "  \"sg75_71\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip_name\": \"" + fluxClip + "\",\n" +
            "      \"type\": \"" + "flux2" + "\",\n" +
            "      \"device\": \"" + "default" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"CLIPLoader\"\n" +
            "  },\n" +
            "  \"sg75_72\": {\n" +
            "    \"inputs\": {\n" +
            "      \"vae_name\": \"" + fluxVae + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"VAELoader\"\n" +
            "  },\n" +
            "  \"sg75_73\": {\n" +
            "    \"inputs\": {\n" +
            "      \"noise_seed\": [\"248\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"RandomNoise\"\n" +
            "  },\n" +
            "  \"sg75_74\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip\": [\"sg75_71\", 0],\n" +
            "      \"text\": \"" + escPrompt + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"CLIPTextEncode\"\n" +
            "  },\n" +
            "  \"sg75_80\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"76\", 0],\n" +
            "      \"upscale_method\": \"" + "nearest-exact" + "\",\n" +
            "      \"megapixels\": 1,\n" +
            "      \"resolution_steps\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"ImageScaleToTotalPixels\"\n" +
            "  },\n" +
            "  \"sg75_82\": {\n" +
            "    \"inputs\": {\n" +
            "      \"conditioning\": [\"sg75_74\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"ConditioningZeroOut\"\n" +
            "  },\n" +
            "  \"sg75_99\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"sg75_80\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"GetImageSize\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// Hy3D repaint over a TripoSplat base: image -> TripoSplat subgraph 100 -> splat file + path -> Hy3DMultiViewsGenerator paint + bake/inpaint texture.
    /// Freeze source(s):
    /// - D:\ComfyUIPortable\ComfyUI\user\default\workflows\FixThisWorkflow.json
    /// UI nodes: 17.
    /// API nodes: 33.
    /// Graph nodes:
    /// 1: Hy3DMultiViewsGenerator
    /// 4: LoadImage
    /// 5: PreviewImage
    /// 6: JarJarLoadSplatPly
    /// 101: LoadImage
    /// 102: SplatToFile3D
    /// 103: SaveGLB
    /// 104: PreviewImage
    /// 105: PreviewImage
    /// 106: PreviewImage
    /// 109: JarJarSplatToFilePath
    /// 138: Hy3D21CameraConfig
    /// 139: Hy3DBakeMultiViews
    /// 140: Hy3DInPaint
    /// 153: PreviewImage
    /// 154: PreviewImage
    /// sg100_11: VAELoader
    /// sg100_16: UNETLoader
    /// sg100_2: TripoSplatPreprocessImage
    /// sg100_22: PreviewImage
    /// sg100_24: TripoSplatConditioning
    /// sg100_25: KSampler
    /// sg100_35: ComfySwitchNode
    /// sg100_55: VAEDecodeTripoSplat
    /// sg100_7: CLIPVisionLoader
    /// sg100_8: VAELoader
    /// sg100_85_81: RemoveBackground
    /// sg100_85_82: LoadBackgroundRemovalModel
    /// sg100_85_83: InvertMask
    /// sg100_85_84: JoinImageWithAlpha
    /// sg100_95: InvertMask
    /// sg100_96: ComfySwitchNode
    /// sg100_97: TripoSplatSamplingPreview
    /// Sinks (no downstream consumers): 103 SaveGLB, 104 PreviewImage, 105 PreviewImage, 106 PreviewImage, 153 PreviewImage, 154 PreviewImage, 5 PreviewImage, sg100_22 PreviewImage, sg100_85_84 JoinImageWithAlpha.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// TripoSplat subgraph 100 expanded inline as sg100_* (nested background-removal as sg100_85_*).
    /// LoadImage 101 feeds the splat stage; LoadImage 4 feeds the Hy3D repaint reference; JarJarLoadSplatPly 6 reloads the JarJarSplatToFilePath 109 result as the Hy3D trimesh.
    /// </summary>
    public static class FixThisHy3DWorkflow
    {
        public const long DEFAULT_SEED = 825572352762154;
        public const int DEFAULT_SPLAT_SEED = 46;
        public const int DEFAULT_STEPS = 20;
        public const int DEFAULT_HY_STEPS = 24;
        public const int DEFAULT_NUM_GAUSSIANS = 262176;
        public const string DEFAULT_INPUT_IMAGE = "38711fdb-ff2e-445a-9236-37e2091db4a4.png";
        public const string DEFAULT_REPAINT_IMAGE = "image-1.webp";
        public const string DEFAULT_OUTPUT_NAME = "ComfyUI_TripoSplat_00002__textured";
        public const string DEFAULT_PAINT_MODEL = "hunyuan3d-paintpbr-v2-1";
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_CLIP = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_SAVE_PREFIX = "3d/ComfyUI_TripoSplat";

        public static string CreateWorkflow(
            long seed = DEFAULT_SEED,
            long splatSeed = DEFAULT_SPLAT_SEED,
            int steps = DEFAULT_STEPS,
            int hySteps = DEFAULT_HY_STEPS,
            int numGaussians = DEFAULT_NUM_GAUSSIANS,
            string inputImage = DEFAULT_INPUT_IMAGE,
            string repaintImage = DEFAULT_REPAINT_IMAGE,
            string outputName = DEFAULT_OUTPUT_NAME,
            string paintModel = DEFAULT_PAINT_MODEL,
            string tripoUnet = DEFAULT_TRIPO_UNET,
            string tripoClip = DEFAULT_TRIPO_CLIP,
            string tripoVae = DEFAULT_TRIPO_VAE,
            string condVae = DEFAULT_COND_VAE,
            string bgRemoval = DEFAULT_BG_REMOVAL
        )
        {
            if (seed < 0)
                seed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (seed < 0) seed = -seed;
            if (splatSeed < 0)
                splatSeed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (splatSeed < 0) splatSeed = -splatSeed;
            if (steps < 1) steps = DEFAULT_STEPS;
            if (hySteps < 1) hySteps = DEFAULT_HY_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            var inv = CultureInfo.InvariantCulture;
            string seedStr = (seed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string splatSeedStr = (splatSeed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string escInputImage = ExtendedJson.Escape(inputImage ?? string.Empty);
            string escRepaintImage = ExtendedJson.Escape(repaintImage ?? string.Empty);
            string escOutputName = ExtendedJson.Escape(outputName ?? string.Empty);
            paintModel = ExtendedJson.Escape(string.IsNullOrWhiteSpace(paintModel) ? DEFAULT_PAINT_MODEL : paintModel);
            tripoUnet = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            tripoClip = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoClip) ? DEFAULT_TRIPO_CLIP : tripoClip);
            tripoVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            condVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            bgRemoval = ExtendedJson.Escape(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            return "{" + "\n" +
            "  \"1\": {\n" +
            "    \"inputs\": {\n" +
            "      \"trimesh\": [\"6\", 0],\n" +
            "      \"camera_config\": [\"138\", 0],\n" +
            "      \"image\": [\"4\", 0],\n" +
            "      \"paint_model\": \"" + paintModel + "\",\n" +
            "      \"view_size\": 1024,\n" +
            "      \"steps\": " + hySteps.ToString(inv) + ",\n" +
            "      \"guidance_scale\": 3,\n" +
            "      \"texture_size\": 2048,\n" +
            "      \"unwrap_mesh\": true,\n" +
            "      \"seed\": " + seedStr + ",\n" +
            "      \"diffusion_backend\": \"" + "pytorch" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"Hy3DMultiViewsGenerator\"\n" +
            "  },\n" +
            "  \"4\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + escRepaintImage + "\",\n" +
            "      \"upload\": \"" + "image" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  },\n" +
            "  \"5\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"1\", 1]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"6\": {\n" +
            "    \"inputs\": {\n" +
            "      \"file_path\": [\"109\", 1],\n" +
            "      \"opacity_cutoff\": 0.05,\n" +
            "      \"poisson_depth\": 9,\n" +
            "      \"max_points\": 100000,\n" +
            "      \"rotate_x\": 180,\n" +
            "      \"rotate_y\": 90,\n" +
            "      \"rotate_z\": 0\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarLoadSplatPly\"\n" +
            "  },\n" +
            "  \"101\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + escInputImage + "\",\n" +
            "      \"upload\": \"" + "image" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  },\n" +
            "  \"102\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat\": [\"sg100_55\", 0],\n" +
            "      \"format\": \"" + "ply" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SplatToFile3D\"\n" +
            "  },\n" +
            "  \"103\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mesh\": [\"102\", 0],\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_SAVE_PREFIX) + "\",\n" +
            "      \"image\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SaveGLB\"\n" +
            "  },\n" +
            "  \"104\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"1\", 2]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"105\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"1\", 3]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"106\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"1\", 4]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"109\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat\": [\"sg100_55\", 0],\n" +
            "      \"format\": \"" + "ply" + "\",\n" +
            "      \"filename\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarSplatToFilePath\"\n" +
            "  },\n" +
            "  \"138\": {\n" +
            "    \"inputs\": {\n" +
            "      \"camera_azimuths\": \"" + "45, 135, 225" + "\",\n" +
            "      \"camera_elevations\": \"" + "30,30,30" + "\",\n" +
            "      \"view_weights\": \"" + "1,1,1" + "\",\n" +
            "      \"ortho_scale\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"Hy3D21CameraConfig\"\n" +
            "  },\n" +
            "  \"139\": {\n" +
            "    \"inputs\": {\n" +
            "      \"pipeline\": [\"1\", 0],\n" +
            "      \"camera_config\": [\"1\", 5],\n" +
            "      \"albedo\": [\"1\", 1],\n" +
            "      \"mr\": [\"1\", 2]\n" +
            "    },\n" +
            "    \"class_type\": \"Hy3DBakeMultiViews\"\n" +
            "  },\n" +
            "  \"140\": {\n" +
            "    \"inputs\": {\n" +
            "      \"pipeline\": [\"139\", 0],\n" +
            "      \"albedo\": [\"139\", 1],\n" +
            "      \"albedo_mask\": [\"139\", 2],\n" +
            "      \"mr\": [\"139\", 3],\n" +
            "      \"mr_mask\": [\"139\", 4],\n" +
            "      \"output_mesh_name\": \"" + escOutputName + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"Hy3DInPaint\"\n" +
            "  },\n" +
            "  \"153\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"4\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"154\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"140\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"sg100_11\": {\n" +
            "    \"inputs\": {\n" +
            "      \"vae_name\": \"" + tripoVae + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"VAELoader\"\n" +
            "  },\n" +
            "  \"sg100_16\": {\n" +
            "    \"inputs\": {\n" +
            "      \"unet_name\": \"" + tripoUnet + "\",\n" +
            "      \"weight_dtype\": \"" + "default" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UNETLoader\"\n" +
            "  },\n" +
            "  \"sg100_2\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"101\", 0],\n" +
            "      \"mask\": [\"sg100_35\", 0],\n" +
            "      \"erode_radius\": 1,\n" +
            "      \"size\": 1024\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatPreprocessImage\"\n" +
            "  },\n" +
            "  \"sg100_22\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"sg100_2\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"sg100_24\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip_vision\": [\"sg100_7\", 0],\n" +
            "      \"vae\": [\"sg100_8\", 0],\n" +
            "      \"image\": [\"sg100_2\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatConditioning\"\n" +
            "  },\n" +
            "  \"sg100_25\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"sg100_96\", 0],\n" +
            "      \"positive\": [\"sg100_24\", 0],\n" +
            "      \"negative\": [\"sg100_24\", 1],\n" +
            "      \"latent_image\": [\"sg100_24\", 2],\n" +
            "      \"seed\": " + splatSeedStr + ",\n" +
            "      \"steps\": " + steps.ToString(inv) + ",\n" +
            "      \"cfg\": 3,\n" +
            "      \"sampler_name\": \"" + "dpmpp_2m" + "\",\n" +
            "      \"scheduler\": \"" + "simple" + "\",\n" +
            "      \"denoise\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"KSampler\"\n" +
            "  },\n" +
            "  \"sg100_35\": {\n" +
            "    \"inputs\": {\n" +
            "      \"on_false\": [\"sg100_95\", 0],\n" +
            "      \"on_true\": [\"sg100_85_81\", 0],\n" +
            "      \"switch\": true\n" +
            "    },\n" +
            "    \"class_type\": \"ComfySwitchNode\"\n" +
            "  },\n" +
            "  \"sg100_55\": {\n" +
            "    \"inputs\": {\n" +
            "      \"samples\": [\"sg100_25\", 0],\n" +
            "      \"vae\": [\"sg100_11\", 0],\n" +
            "      \"num_gaussians\": " + numGaussians.ToString(inv) + ",\n" +
            "      \"seed\": " + splatSeedStr + "\n" +
            "    },\n" +
            "    \"class_type\": \"VAEDecodeTripoSplat\"\n" +
            "  },\n" +
            "  \"sg100_7\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip_name\": \"" + tripoClip + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"CLIPVisionLoader\"\n" +
            "  },\n" +
            "  \"sg100_8\": {\n" +
            "    \"inputs\": {\n" +
            "      \"vae_name\": \"" + condVae + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"VAELoader\"\n" +
            "  },\n" +
            "  \"sg100_85_81\": {\n" +
            "    \"inputs\": {\n" +
            "      \"bg_removal_model\": [\"sg100_85_82\", 0],\n" +
            "      \"image\": [\"101\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"RemoveBackground\"\n" +
            "  },\n" +
            "  \"sg100_85_82\": {\n" +
            "    \"inputs\": {\n" +
            "      \"bg_removal_name\": \"" + bgRemoval + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadBackgroundRemovalModel\"\n" +
            "  },\n" +
            "  \"sg100_85_83\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mask\": [\"sg100_85_81\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"InvertMask\"\n" +
            "  },\n" +
            "  \"sg100_85_84\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"101\", 0],\n" +
            "      \"alpha\": [\"sg100_85_83\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"JoinImageWithAlpha\"\n" +
            "  },\n" +
            "  \"sg100_95\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mask\": [\"101\", 1]\n" +
            "    },\n" +
            "    \"class_type\": \"InvertMask\"\n" +
            "  },\n" +
            "  \"sg100_96\": {\n" +
            "    \"inputs\": {\n" +
            "      \"on_false\": [\"sg100_16\", 0],\n" +
            "      \"on_true\": [\"sg100_97\", 0],\n" +
            "      \"switch\": true\n" +
            "    },\n" +
            "    \"class_type\": \"ComfySwitchNode\"\n" +
            "  },\n" +
            "  \"sg100_97\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"sg100_16\", 0],\n" +
            "      \"vae\": [\"sg100_11\", 0],\n" +
            "      \"octree_level\": 5,\n" +
            "      \"num_gaussians\": 16384,\n" +
            "      \"yaw\": 90,\n" +
            "      \"pitch\": 15,\n" +
            "      \"point_size\": 2\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatSamplingPreview\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// Hy3D repaint over a TripoSplat base (lighter paint settings than FixThisHy3DWorkflow).
    /// Freeze source(s):
    /// - D:\ComfyUIPortable\ComfyUI\user\default\workflows\FixThisWorkflow.backup.json
    /// - D:\ComfyUIPortable\ComfyUI\user\default\workflows\HunyuanSplikt.json
    /// UI nodes: 16.
    /// API nodes: 32.
    /// Graph nodes:
    /// 1: Hy3DMultiViewsGenerator
    /// 4: LoadImage
    /// 5: PreviewImage
    /// 6: JarJarLoadSplatPly
    /// 101: LoadImage
    /// 102: SplatToFile3D
    /// 103: SaveGLB
    /// 104: PreviewImage
    /// 105: PreviewImage
    /// 106: PreviewImage
    /// 109: JarJarSplatToFilePath
    /// 138: Hy3D21CameraConfig
    /// 139: Hy3DBakeMultiViews
    /// 140: Hy3DInPaint
    /// 141: PreviewImage
    /// sg100_11: VAELoader
    /// sg100_16: UNETLoader
    /// sg100_2: TripoSplatPreprocessImage
    /// sg100_22: PreviewImage
    /// sg100_24: TripoSplatConditioning
    /// sg100_25: KSampler
    /// sg100_35: ComfySwitchNode
    /// sg100_55: VAEDecodeTripoSplat
    /// sg100_7: CLIPVisionLoader
    /// sg100_8: VAELoader
    /// sg100_85_81: RemoveBackground
    /// sg100_85_82: LoadBackgroundRemovalModel
    /// sg100_85_83: InvertMask
    /// sg100_85_84: JoinImageWithAlpha
    /// sg100_95: InvertMask
    /// sg100_96: ComfySwitchNode
    /// sg100_97: TripoSplatSamplingPreview
    /// Sinks (no downstream consumers): 103 SaveGLB, 104 PreviewImage, 105 PreviewImage, 106 PreviewImage, 141 PreviewImage, 5 PreviewImage, sg100_22 PreviewImage, sg100_85_84 JoinImageWithAlpha.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// FixThisWorkflow.backup.json and HunyuanSplikt.json produce byte-identical API graphs (same normalized hash); embedded once.
    /// Hy3D paint here is lighter: view 512, 6 steps, texture 1024; Hy3DInPaint output name left empty.
    /// </summary>
    public static class HunyuanSpliktWorkflow
    {
        public const long DEFAULT_SEED = 825572352762154;
        public const int DEFAULT_SPLAT_SEED = 46;
        public const int DEFAULT_STEPS = 20;
        public const int DEFAULT_HY_STEPS = 6;
        public const int DEFAULT_NUM_GAUSSIANS = 262176;
        public const string DEFAULT_INPUT_IMAGE = "38711fdb-ff2e-445a-9236-37e2091db4a4.png";
        public const string DEFAULT_REPAINT_IMAGE = "Flux2-Klein_00034_.png";
        public const string DEFAULT_OUTPUT_NAME = "";
        public const string DEFAULT_PAINT_MODEL = "hunyuan3d-paintpbr-v2-1";
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_CLIP = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_SAVE_PREFIX = "3d/ComfyUI_TripoSplat";

        public static string CreateWorkflow(
            long seed = DEFAULT_SEED,
            long splatSeed = DEFAULT_SPLAT_SEED,
            int steps = DEFAULT_STEPS,
            int hySteps = DEFAULT_HY_STEPS,
            int numGaussians = DEFAULT_NUM_GAUSSIANS,
            string inputImage = DEFAULT_INPUT_IMAGE,
            string repaintImage = DEFAULT_REPAINT_IMAGE,
            string outputName = DEFAULT_OUTPUT_NAME,
            string paintModel = DEFAULT_PAINT_MODEL,
            string tripoUnet = DEFAULT_TRIPO_UNET,
            string tripoClip = DEFAULT_TRIPO_CLIP,
            string tripoVae = DEFAULT_TRIPO_VAE,
            string condVae = DEFAULT_COND_VAE,
            string bgRemoval = DEFAULT_BG_REMOVAL
        )
        {
            if (seed < 0)
                seed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (seed < 0) seed = -seed;
            if (splatSeed < 0)
                splatSeed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (splatSeed < 0) splatSeed = -splatSeed;
            if (steps < 1) steps = DEFAULT_STEPS;
            if (hySteps < 1) hySteps = DEFAULT_HY_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            var inv = CultureInfo.InvariantCulture;
            string seedStr = (seed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string splatSeedStr = (splatSeed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string escInputImage = ExtendedJson.Escape(inputImage ?? string.Empty);
            string escRepaintImage = ExtendedJson.Escape(repaintImage ?? string.Empty);
            string escOutputName = ExtendedJson.Escape(outputName ?? string.Empty);
            paintModel = ExtendedJson.Escape(string.IsNullOrWhiteSpace(paintModel) ? DEFAULT_PAINT_MODEL : paintModel);
            tripoUnet = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            tripoClip = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoClip) ? DEFAULT_TRIPO_CLIP : tripoClip);
            tripoVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            condVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            bgRemoval = ExtendedJson.Escape(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            return "{" + "\n" +
            "  \"1\": {\n" +
            "    \"inputs\": {\n" +
            "      \"trimesh\": [\"6\", 0],\n" +
            "      \"camera_config\": [\"138\", 0],\n" +
            "      \"image\": [\"4\", 0],\n" +
            "      \"paint_model\": \"" + paintModel + "\",\n" +
            "      \"view_size\": 512,\n" +
            "      \"steps\": " + hySteps.ToString(inv) + ",\n" +
            "      \"guidance_scale\": 3,\n" +
            "      \"texture_size\": 1024,\n" +
            "      \"unwrap_mesh\": true,\n" +
            "      \"seed\": " + seedStr + ",\n" +
            "      \"diffusion_backend\": \"" + "pytorch" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"Hy3DMultiViewsGenerator\"\n" +
            "  },\n" +
            "  \"4\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + escRepaintImage + "\",\n" +
            "      \"upload\": \"" + "image" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  },\n" +
            "  \"5\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"1\", 1]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"6\": {\n" +
            "    \"inputs\": {\n" +
            "      \"file_path\": [\"109\", 1],\n" +
            "      \"opacity_cutoff\": 0.05,\n" +
            "      \"poisson_depth\": 9,\n" +
            "      \"max_points\": 100000,\n" +
            "      \"rotate_x\": 180,\n" +
            "      \"rotate_y\": 90,\n" +
            "      \"rotate_z\": 0\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarLoadSplatPly\"\n" +
            "  },\n" +
            "  \"101\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + escInputImage + "\",\n" +
            "      \"upload\": \"" + "image" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  },\n" +
            "  \"102\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat\": [\"sg100_55\", 0],\n" +
            "      \"format\": \"" + "ply" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SplatToFile3D\"\n" +
            "  },\n" +
            "  \"103\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mesh\": [\"102\", 0],\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_SAVE_PREFIX) + "\",\n" +
            "      \"image\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SaveGLB\"\n" +
            "  },\n" +
            "  \"104\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"1\", 2]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"105\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"1\", 3]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"106\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"1\", 4]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"109\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat\": [\"sg100_55\", 0],\n" +
            "      \"format\": \"" + "ply" + "\",\n" +
            "      \"filename\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarSplatToFilePath\"\n" +
            "  },\n" +
            "  \"138\": {\n" +
            "    \"inputs\": {\n" +
            "      \"camera_azimuths\": \"" + "45, 135, 225" + "\",\n" +
            "      \"camera_elevations\": \"" + "30,30,30" + "\",\n" +
            "      \"view_weights\": \"" + "1,1,1" + "\",\n" +
            "      \"ortho_scale\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"Hy3D21CameraConfig\"\n" +
            "  },\n" +
            "  \"139\": {\n" +
            "    \"inputs\": {\n" +
            "      \"pipeline\": [\"1\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"Hy3DBakeMultiViews\"\n" +
            "  },\n" +
            "  \"140\": {\n" +
            "    \"inputs\": {\n" +
            "      \"pipeline\": [\"139\", 0],\n" +
            "      \"output_mesh_name\": \"" + escOutputName + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"Hy3DInPaint\"\n" +
            "  },\n" +
            "  \"141\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"140\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"sg100_11\": {\n" +
            "    \"inputs\": {\n" +
            "      \"vae_name\": \"" + tripoVae + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"VAELoader\"\n" +
            "  },\n" +
            "  \"sg100_16\": {\n" +
            "    \"inputs\": {\n" +
            "      \"unet_name\": \"" + tripoUnet + "\",\n" +
            "      \"weight_dtype\": \"" + "default" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UNETLoader\"\n" +
            "  },\n" +
            "  \"sg100_2\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"101\", 0],\n" +
            "      \"mask\": [\"sg100_35\", 0],\n" +
            "      \"erode_radius\": 1,\n" +
            "      \"size\": 1024\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatPreprocessImage\"\n" +
            "  },\n" +
            "  \"sg100_22\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"sg100_2\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"sg100_24\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip_vision\": [\"sg100_7\", 0],\n" +
            "      \"vae\": [\"sg100_8\", 0],\n" +
            "      \"image\": [\"sg100_2\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatConditioning\"\n" +
            "  },\n" +
            "  \"sg100_25\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"sg100_96\", 0],\n" +
            "      \"positive\": [\"sg100_24\", 0],\n" +
            "      \"negative\": [\"sg100_24\", 1],\n" +
            "      \"latent_image\": [\"sg100_24\", 2],\n" +
            "      \"seed\": " + splatSeedStr + ",\n" +
            "      \"steps\": " + steps.ToString(inv) + ",\n" +
            "      \"cfg\": 3,\n" +
            "      \"sampler_name\": \"" + "dpmpp_2m" + "\",\n" +
            "      \"scheduler\": \"" + "simple" + "\",\n" +
            "      \"denoise\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"KSampler\"\n" +
            "  },\n" +
            "  \"sg100_35\": {\n" +
            "    \"inputs\": {\n" +
            "      \"on_false\": [\"sg100_95\", 0],\n" +
            "      \"on_true\": [\"sg100_85_81\", 0],\n" +
            "      \"switch\": true\n" +
            "    },\n" +
            "    \"class_type\": \"ComfySwitchNode\"\n" +
            "  },\n" +
            "  \"sg100_55\": {\n" +
            "    \"inputs\": {\n" +
            "      \"samples\": [\"sg100_25\", 0],\n" +
            "      \"vae\": [\"sg100_11\", 0],\n" +
            "      \"num_gaussians\": " + numGaussians.ToString(inv) + ",\n" +
            "      \"seed\": " + splatSeedStr + "\n" +
            "    },\n" +
            "    \"class_type\": \"VAEDecodeTripoSplat\"\n" +
            "  },\n" +
            "  \"sg100_7\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip_name\": \"" + tripoClip + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"CLIPVisionLoader\"\n" +
            "  },\n" +
            "  \"sg100_8\": {\n" +
            "    \"inputs\": {\n" +
            "      \"vae_name\": \"" + condVae + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"VAELoader\"\n" +
            "  },\n" +
            "  \"sg100_85_81\": {\n" +
            "    \"inputs\": {\n" +
            "      \"bg_removal_model\": [\"sg100_85_82\", 0],\n" +
            "      \"image\": [\"101\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"RemoveBackground\"\n" +
            "  },\n" +
            "  \"sg100_85_82\": {\n" +
            "    \"inputs\": {\n" +
            "      \"bg_removal_name\": \"" + bgRemoval + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadBackgroundRemovalModel\"\n" +
            "  },\n" +
            "  \"sg100_85_83\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mask\": [\"sg100_85_81\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"InvertMask\"\n" +
            "  },\n" +
            "  \"sg100_85_84\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"101\", 0],\n" +
            "      \"alpha\": [\"sg100_85_83\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"JoinImageWithAlpha\"\n" +
            "  },\n" +
            "  \"sg100_95\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mask\": [\"101\", 1]\n" +
            "    },\n" +
            "    \"class_type\": \"InvertMask\"\n" +
            "  },\n" +
            "  \"sg100_96\": {\n" +
            "    \"inputs\": {\n" +
            "      \"on_false\": [\"sg100_16\", 0],\n" +
            "      \"on_true\": [\"sg100_97\", 0],\n" +
            "      \"switch\": true\n" +
            "    },\n" +
            "    \"class_type\": \"ComfySwitchNode\"\n" +
            "  },\n" +
            "  \"sg100_97\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"sg100_16\", 0],\n" +
            "      \"vae\": [\"sg100_11\", 0],\n" +
            "      \"octree_level\": 5,\n" +
            "      \"num_gaussians\": 16384,\n" +
            "      \"yaw\": 90,\n" +
            "      \"pitch\": 15,\n" +
            "      \"point_size\": 2\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatSamplingPreview\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// Hy3D-only GLB retexture: load mesh + reference image, regenerate multiviews, bake and inpaint.
    /// Freeze source(s):
    /// - D:\ComfyUIPortable\ComfyUI\user\default\workflows\Hunyuan_Repaint_GLB.json
    /// UI nodes: 9.
    /// API nodes: 9.
    /// Graph nodes:
    /// 1: JarJarLoadMesh
    /// 2: LoadImage
    /// 3: Hy3D21CameraConfig
    /// 4: Hy3DMultiViewsGenerator
    /// 5: Hy3DBakeMultiViews
    /// 6: Hy3DInPaint
    /// 7: PreviewImage
    /// 8: PreviewImage
    /// 9: PreviewImage
    /// Sinks (no downstream consumers): 7 PreviewImage, 8 PreviewImage, 9 PreviewImage.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// </summary>
    public static class HunyuanRepaintGlbWorkflow
    {
        public const int DEFAULT_SEED = 42;
        public const int DEFAULT_STEPS = 10;
        public const string DEFAULT_INPUT_MESH = "D:\\ComfyUIPortable\\ComfyUI\\output\\3D\\ComfyUI_TripoSplat_00004_.glb";
        public const string DEFAULT_INPUT_IMAGE = "example.png";
        public const string DEFAULT_PAINT_MODEL = "hunyuan3d-paintpbr-v2-1";
        public const string DEFAULT_OUTPUT_NAME = "repainted";

        public static string CreateWorkflow(
            long seed = DEFAULT_SEED,
            int steps = DEFAULT_STEPS,
            string inputMesh = DEFAULT_INPUT_MESH,
            string inputImage = DEFAULT_INPUT_IMAGE,
            string paintModel = DEFAULT_PAINT_MODEL,
            string outputName = DEFAULT_OUTPUT_NAME
        )
        {
            if (seed < 0)
                seed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (seed < 0) seed = -seed;
            if (steps < 1) steps = DEFAULT_STEPS;
            var inv = CultureInfo.InvariantCulture;
            string seedStr = (seed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string escInputMesh = ExtendedJson.Escape(inputMesh ?? string.Empty);
            string escInputImage = ExtendedJson.Escape(inputImage ?? string.Empty);
            paintModel = ExtendedJson.Escape(string.IsNullOrWhiteSpace(paintModel) ? DEFAULT_PAINT_MODEL : paintModel);
            string escOutputName = ExtendedJson.Escape(outputName ?? string.Empty);
            return "{" + "\n" +
            "  \"1\": {\n" +
            "    \"inputs\": {\n" +
            "      \"file_path\": \"" + escInputMesh + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarLoadMesh\"\n" +
            "  },\n" +
            "  \"2\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + escInputImage + "\",\n" +
            "      \"upload\": \"" + "image" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  },\n" +
            "  \"3\": {\n" +
            "    \"inputs\": {\n" +
            "      \"camera_azimuths\": \"" + "0, 90, 180, 270, 0, 180" + "\",\n" +
            "      \"camera_elevations\": \"" + "0, 0, 0, 0, 90, -90" + "\",\n" +
            "      \"view_weights\": \"" + "1, 0.1, 0.5, 0.1, 0.05, 0.05" + "\",\n" +
            "      \"ortho_scale\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"Hy3D21CameraConfig\"\n" +
            "  },\n" +
            "  \"4\": {\n" +
            "    \"inputs\": {\n" +
            "      \"trimesh\": [\"1\", 0],\n" +
            "      \"camera_config\": [\"3\", 0],\n" +
            "      \"image\": [\"2\", 0],\n" +
            "      \"paint_model\": \"" + paintModel + "\",\n" +
            "      \"view_size\": 512,\n" +
            "      \"steps\": " + steps.ToString(inv) + ",\n" +
            "      \"guidance_scale\": 3,\n" +
            "      \"texture_size\": 1024,\n" +
            "      \"unwrap_mesh\": true,\n" +
            "      \"seed\": " + seedStr + ",\n" +
            "      \"diffusion_backend\": \"" + "pytorch" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"Hy3DMultiViewsGenerator\"\n" +
            "  },\n" +
            "  \"5\": {\n" +
            "    \"inputs\": {\n" +
            "      \"pipeline\": [\"4\", 0],\n" +
            "      \"camera_config\": [\"4\", 5],\n" +
            "      \"albedo\": [\"4\", 1],\n" +
            "      \"mr\": [\"4\", 2]\n" +
            "    },\n" +
            "    \"class_type\": \"Hy3DBakeMultiViews\"\n" +
            "  },\n" +
            "  \"6\": {\n" +
            "    \"inputs\": {\n" +
            "      \"pipeline\": [\"5\", 0],\n" +
            "      \"albedo\": [\"5\", 1],\n" +
            "      \"albedo_mask\": [\"5\", 2],\n" +
            "      \"mr\": [\"5\", 3],\n" +
            "      \"mr_mask\": [\"5\", 4],\n" +
            "      \"output_mesh_name\": \"" + escOutputName + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"Hy3DInPaint\"\n" +
            "  },\n" +
            "  \"7\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"5\", 5]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"8\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"5\", 6]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"9\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"6\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// PartField splat part segmentation: folder of PLYs -> pick item -> load -> inference + clustering -> export parts.
    /// Freeze source(s):
    /// - D:\ComfyUIPortable\ComfyUI\user\default\workflows\MEESAJARJAR_PART_SPLITTER.json
    /// UI nodes: 10.
    /// API nodes: 9.
    /// Graph nodes:
    /// 1: PartFieldSplatInference
    /// 5: PartFieldModelDownLoader
    /// 6: PartFieldSplatClustering
    /// 7: PreviewImage
    /// 17: PartFieldSplatExportParts
    /// 35: JarJarListItem
    /// 36: JarJarFolderFilesByExt
    /// 47: JarJarLoadSplatPly
    /// 49: GeomPackPreviewMeshVTK
    /// Sinks (no downstream consumers): 17 PartFieldSplatExportParts, 49 GeomPackPreviewMeshVTK, 7 PreviewImage.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// </summary>
    public static class PartSplitterWorkflow
    {
        public const string DEFAULT_SPLAT_FOLDER = "C:\\Users\\meesa\\OneDrive\\Documents\\MeesaMultisMaker\\3D";
        public const long DEFAULT_SEED = 583145251023758;
        public const int DEFAULT_INFER_SEED = 1;
        public const int DEFAULT_NUM_CLUSTERS = 6;
        public const int DEFAULT_MAX_POINTS = 100000;
        public const string DEFAULT_MODEL_NAME = "model_objaverse.ckpt";
        public const string DEFAULT_EXPORT_PREFIX = "partfield_splat_parts";

        public static string CreateWorkflow(
            string splatFolder = DEFAULT_SPLAT_FOLDER,
            long seed = DEFAULT_SEED,
            long inferSeed = DEFAULT_INFER_SEED,
            int numClusters = DEFAULT_NUM_CLUSTERS,
            int maxPoints = DEFAULT_MAX_POINTS,
            string modelName = DEFAULT_MODEL_NAME,
            string exportPrefix = DEFAULT_EXPORT_PREFIX
        )
        {
            if (seed < 0)
                seed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (seed < 0) seed = -seed;
            if (inferSeed < 0)
                inferSeed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (inferSeed < 0) inferSeed = -inferSeed;
            if (numClusters < 1) numClusters = DEFAULT_NUM_CLUSTERS;
            if (maxPoints < 1) maxPoints = DEFAULT_MAX_POINTS;
            var inv = CultureInfo.InvariantCulture;
            string seedStr = (seed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string inferSeedStr = (inferSeed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string escSplatFolder = ExtendedJson.Escape(splatFolder ?? string.Empty);
            modelName = ExtendedJson.Escape(string.IsNullOrWhiteSpace(modelName) ? DEFAULT_MODEL_NAME : modelName);
            string escExportPrefix = ExtendedJson.Escape(exportPrefix ?? string.Empty);
            return "{" + "\n" +
            "  \"1\": {\n" +
            "    \"inputs\": {\n" +
            "      \"partfield_model\": [\"5\", 0],\n" +
            "      \"ply_path\": [\"35\", 0],\n" +
            "      \"max_points\": " + maxPoints.ToString(inv) + ",\n" +
            "      \"seed\": " + inferSeedStr + "\n" +
            "    },\n" +
            "    \"class_type\": \"PartFieldSplatInference\"\n" +
            "  },\n" +
            "  \"5\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model_name\": \"" + modelName + "\",\n" +
            "      \"use_gpu\": true\n" +
            "    },\n" +
            "    \"class_type\": \"PartFieldModelDownLoader\"\n" +
            "  },\n" +
            "  \"6\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat_xyz\": [\"1\", 0],\n" +
            "      \"features\": [\"1\", 1],\n" +
            "      \"num_clusters\": " + numClusters.ToString(inv) + ",\n" +
            "      \"seed\": " + seedStr + ",\n" +
            "      \"azim\": -38,\n" +
            "      \"elev\": 20,\n" +
            "      \"rotate_points\": true,\n" +
            "      \"rot_z\": 0\n" +
            "    },\n" +
            "    \"class_type\": \"PartFieldSplatClustering\"\n" +
            "  },\n" +
            "  \"7\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"6\", 1]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"17\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat_points\": [\"6\", 0],\n" +
            "      \"filename_prefix\": \"" + escExportPrefix + "\",\n" +
            "      \"source_path\": \"" + "" + "\",\n" +
            "      \"add_label\": true\n" +
            "    },\n" +
            "    \"class_type\": \"PartFieldSplatExportParts\"\n" +
            "  },\n" +
            "  \"35\": {\n" +
            "    \"inputs\": {\n" +
            "      \"list_in\": [\"36\", 0],\n" +
            "      \"index\": 276\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarListItem\"\n" +
            "  },\n" +
            "  \"36\": {\n" +
            "    \"inputs\": {\n" +
            "      \"folder\": \"" + escSplatFolder + "\",\n" +
            "      \"extension\": \"" + ".ply" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarFolderFilesByExt\"\n" +
            "  },\n" +
            "  \"47\": {\n" +
            "    \"inputs\": {\n" +
            "      \"file_path\": [\"35\", 0],\n" +
            "      \"opacity_cutoff\": 0.05,\n" +
            "      \"poisson_depth\": 6,\n" +
            "      \"max_points\": 100000,\n" +
            "      \"rotate_x\": 0,\n" +
            "      \"rotate_y\": 0,\n" +
            "      \"rotate_z\": 0\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarLoadSplatPly\"\n" +
            "  },\n" +
            "  \"49\": {\n" +
            "    \"inputs\": {\n" +
            "      \"trimesh\": [\"47\", 0],\n" +
            "      \"mode\": \"" + "fields" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"GeomPackPreviewMeshVTK\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// PartField splat part segmentation, compact variant (no preview-mesh tail).
    /// Freeze source(s):
    /// - D:\ComfyUIPortable\ComfyUI\user\default\workflows\WATERTIGHTTEST.json
    /// UI nodes: 8.
    /// API nodes: 7.
    /// Graph nodes:
    /// 1: PartFieldSplatInference
    /// 5: PartFieldModelDownLoader
    /// 6: PartFieldSplatClustering
    /// 7: PreviewImage
    /// 17: PartFieldSplatExportParts
    /// 35: JarJarListItem
    /// 36: JarJarFolderFilesByExt
    /// Sinks (no downstream consumers): 17 PartFieldSplatExportParts, 7 PreviewImage.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// Graph differs from PartSplitterWorkflow: no JarJarLoadSplatPly/GeomPack preview tail.
    /// </summary>
    public static class WatertightWorkflow
    {
        public const string DEFAULT_SPLAT_FOLDER = "C:\\Users\\meesa\\OneDrive\\Documents\\MeesaMultisMaker\\3D";
        public const long DEFAULT_SEED = 670010596717303;
        public const int DEFAULT_INFER_SEED = 1;
        public const int DEFAULT_NUM_CLUSTERS = 6;
        public const int DEFAULT_MAX_POINTS = 100000;
        public const string DEFAULT_MODEL_NAME = "model_objaverse.ckpt";
        public const string DEFAULT_EXPORT_PREFIX = "partfield_splat_parts";

        public static string CreateWorkflow(
            string splatFolder = DEFAULT_SPLAT_FOLDER,
            long seed = DEFAULT_SEED,
            long inferSeed = DEFAULT_INFER_SEED,
            int numClusters = DEFAULT_NUM_CLUSTERS,
            int maxPoints = DEFAULT_MAX_POINTS,
            string modelName = DEFAULT_MODEL_NAME,
            string exportPrefix = DEFAULT_EXPORT_PREFIX
        )
        {
            if (seed < 0)
                seed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (seed < 0) seed = -seed;
            if (inferSeed < 0)
                inferSeed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (inferSeed < 0) inferSeed = -inferSeed;
            if (numClusters < 1) numClusters = DEFAULT_NUM_CLUSTERS;
            if (maxPoints < 1) maxPoints = DEFAULT_MAX_POINTS;
            var inv = CultureInfo.InvariantCulture;
            string seedStr = (seed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string inferSeedStr = (inferSeed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string escSplatFolder = ExtendedJson.Escape(splatFolder ?? string.Empty);
            modelName = ExtendedJson.Escape(string.IsNullOrWhiteSpace(modelName) ? DEFAULT_MODEL_NAME : modelName);
            string escExportPrefix = ExtendedJson.Escape(exportPrefix ?? string.Empty);
            return "{" + "\n" +
            "  \"1\": {\n" +
            "    \"inputs\": {\n" +
            "      \"partfield_model\": [\"5\", 0],\n" +
            "      \"ply_path\": [\"35\", 0],\n" +
            "      \"max_points\": " + maxPoints.ToString(inv) + ",\n" +
            "      \"seed\": " + inferSeedStr + "\n" +
            "    },\n" +
            "    \"class_type\": \"PartFieldSplatInference\"\n" +
            "  },\n" +
            "  \"5\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model_name\": \"" + modelName + "\",\n" +
            "      \"use_gpu\": true\n" +
            "    },\n" +
            "    \"class_type\": \"PartFieldModelDownLoader\"\n" +
            "  },\n" +
            "  \"6\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat_xyz\": [\"1\", 0],\n" +
            "      \"features\": [\"1\", 1],\n" +
            "      \"num_clusters\": " + numClusters.ToString(inv) + ",\n" +
            "      \"seed\": " + seedStr + ",\n" +
            "      \"azim\": -38,\n" +
            "      \"elev\": 20,\n" +
            "      \"rotate_points\": true,\n" +
            "      \"rot_z\": 0\n" +
            "    },\n" +
            "    \"class_type\": \"PartFieldSplatClustering\"\n" +
            "  },\n" +
            "  \"7\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"6\", 1]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"17\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat_points\": [\"6\", 0],\n" +
            "      \"filename_prefix\": \"" + escExportPrefix + "\",\n" +
            "      \"source_path\": \"" + "" + "\",\n" +
            "      \"add_label\": true\n" +
            "    },\n" +
            "    \"class_type\": \"PartFieldSplatExportParts\"\n" +
            "  },\n" +
            "  \"35\": {\n" +
            "    \"inputs\": {\n" +
            "      \"list_in\": [\"36\", 0],\n" +
            "      \"index\": 200\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarListItem\"\n" +
            "  },\n" +
            "  \"36\": {\n" +
            "    \"inputs\": {\n" +
            "      \"folder\": \"" + escSplatFolder + "\",\n" +
            "      \"extension\": \"" + ".ply" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarFolderFilesByExt\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// Debug preview: load a splat PLY and view it as a mesh.
    /// Freeze source(s):
    /// - D:\ComfyUIPortable\ComfyUI\user\default\workflows\FIXTHIS1.json
    /// UI nodes: 5.
    /// API nodes: 2.
    /// Graph nodes:
    /// 1: JarJarLoadSplatPly
    /// 2: JarJarPreviewMesh
    /// Sinks (no downstream consumers): 2 JarJarPreviewMesh.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// </summary>
    public static class FixThis1PreviewWorkflow
    {
        public const string DEFAULT_INPUT_PLY = "D:\\ComfyUIPortable\\ComfyUI\\output\\3d\\splat_retex_20260921_010827.ply";
        public const int DEFAULT_WIDTH = 1024;
        public const int DEFAULT_HEIGHT = 1024;

        public static string CreateWorkflow(
            string inputPly = DEFAULT_INPUT_PLY,
            int width = DEFAULT_WIDTH,
            int height = DEFAULT_HEIGHT
        )
        {
            if (width < 1) width = DEFAULT_WIDTH;
            if (height < 1) height = DEFAULT_HEIGHT;
            var inv = CultureInfo.InvariantCulture;
            string escInputPly = ExtendedJson.Escape(inputPly ?? string.Empty);
            return "{" + "\n" +
            "  \"1\": {\n" +
            "    \"inputs\": {\n" +
            "      \"file_path\": \"" + escInputPly + "\",\n" +
            "      \"opacity_cutoff\": 0.05,\n" +
            "      \"poisson_depth\": 9,\n" +
            "      \"max_points\": 100000,\n" +
            "      \"rotate_x\": 0,\n" +
            "      \"rotate_y\": 0,\n" +
            "      \"rotate_z\": 0\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarLoadSplatPly\"\n" +
            "  },\n" +
            "  \"2\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mesh\": [\"1\", 0],\n" +
            "      \"width\": " + width.ToString(inv) + ",\n" +
            "      \"height\": " + height.ToString(inv) + ",\n" +
            "      \"mesh_path\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarPreviewMesh\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// Debug preview with auto-framed camera: load a splat PLY, frame it, and view it.
    /// Freeze source(s):
    /// - D:\ComfyUIPortable\ComfyUI\user\default\workflows\FIXTHIS1_FRAMED.json
    /// UI nodes: 6.
    /// API nodes: 3.
    /// Graph nodes:
    /// 1: JarJarLoadSplatPly
    /// 2: JarJarPreviewMesh
    /// 6: JarJarFrameCamera
    /// Sinks (no downstream consumers): 2 JarJarPreviewMesh.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// JarJarFrameCamera widget order (yaw/pitch/distance) taken from the JarJar node schema; FRAMED serialization carries no input metadata.
    /// </summary>
    public static class FixThis1FramedPreviewWorkflow
    {
        public const string DEFAULT_INPUT_PLY = "D:\\ComfyUIPortable\\ComfyUI\\output\\3d\\splat_retex_20260921_010827.ply";
        public const int DEFAULT_WIDTH = 1024;
        public const int DEFAULT_HEIGHT = 1024;
        public const double DEFAULT_FRAME_YAW = 30.0;
        public const double DEFAULT_FRAME_PITCH = 20.0;
        public const double DEFAULT_FRAME_DISTANCE = 2.2;

        public static string CreateWorkflow(
            string inputPly = DEFAULT_INPUT_PLY,
            int width = DEFAULT_WIDTH,
            int height = DEFAULT_HEIGHT,
            double frameYaw = DEFAULT_FRAME_YAW,
            double framePitch = DEFAULT_FRAME_PITCH,
            double frameDistance = DEFAULT_FRAME_DISTANCE
        )
        {
            if (width < 1) width = DEFAULT_WIDTH;
            if (height < 1) height = DEFAULT_HEIGHT;
            var inv = CultureInfo.InvariantCulture;
            string escInputPly = ExtendedJson.Escape(inputPly ?? string.Empty);
            return "{" + "\n" +
            "  \"1\": {\n" +
            "    \"inputs\": {\n" +
            "      \"file_path\": \"" + escInputPly + "\",\n" +
            "      \"opacity_cutoff\": 0.05,\n" +
            "      \"poisson_depth\": 9,\n" +
            "      \"max_points\": 100000,\n" +
            "      \"rotate_x\": 0,\n" +
            "      \"rotate_y\": 0,\n" +
            "      \"rotate_z\": 0\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarLoadSplatPly\"\n" +
            "  },\n" +
            "  \"2\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mesh\": [\"1\", 0],\n" +
            "      \"camera_info\": [\"6\", 0],\n" +
            "      \"width\": " + width.ToString(inv) + ",\n" +
            "      \"height\": " + height.ToString(inv) + ",\n" +
            "      \"mesh_path\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarPreviewMesh\"\n" +
            "  },\n" +
            "  \"6\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mesh\": [\"1\", 0],\n" +
            "      \"yaw_deg\": " + frameYaw.ToString(inv) + ",\n" +
            "      \"pitch_deg\": " + framePitch.ToString(inv) + ",\n" +
            "      \"distance_scale\": " + frameDistance.ToString(inv) + "\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarFrameCamera\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// TripoSplat proof of concept: image -> conditioning -> sampler -> splat file + watertight mesh.
    /// Freeze source(s):
    /// - D:\ComfyUIPortable\ComfyUI\user\default\workflows\PROOF_CONCEPT_1.json
    /// UI nodes: 18.
    /// API nodes: 21.
    /// Graph nodes:
    /// 51: SaveGLB
    /// 67: SaveGLB
    /// 76: SplatToMesh
    /// 92: SplatToFile3D
    /// 99: LoadImage
    /// 100: TripoSplatConditioning
    /// 101: KSampler
    /// 102: ComfySwitchNode
    /// 103: TripoSplatPreprocessImage
    /// 104: CLIPVisionLoader
    /// 105: VAELoader
    /// 106: VAELoader
    /// 107: VAEDecodeTripoSplat
    /// 109: InvertMask
    /// 110: UNETLoader
    /// 111: ComfySwitchNode
    /// 112: TripoSplatSamplingPreview
    /// sg108_81: RemoveBackground
    /// sg108_82: LoadBackgroundRemovalModel
    /// sg108_83: InvertMask
    /// sg108_84: JoinImageWithAlpha
    /// Sinks (no downstream consumers): 51 SaveGLB, 67 SaveGLB, sg108_84 JoinImageWithAlpha.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// Background-removal subgraph 108 expanded inline as sg108_*. SplatToMesh yields the MESH_ GLB alongside the PLY_ splat.
    /// </summary>
    public static class ProofConcept1Workflow
    {
        public const int DEFAULT_SEED = 46;
        public const int DEFAULT_STEPS = 20;
        public const int DEFAULT_NUM_GAUSSIANS = 262144;
        public const string DEFAULT_INPUT_IMAGE = "gorilla-with-white-background-black-body_416902-1383.png";
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_PLY_PREFIX = "3d/PLY_";
        public const string DEFAULT_MESH_PREFIX = "3d/MESH_";

        public static string CreateWorkflow(
            long seed = DEFAULT_SEED,
            int steps = DEFAULT_STEPS,
            int numGaussians = DEFAULT_NUM_GAUSSIANS,
            string inputImage = DEFAULT_INPUT_IMAGE,
            string tripoUnet = DEFAULT_TRIPO_UNET,
            string tripoVae = DEFAULT_TRIPO_VAE,
            string condVae = DEFAULT_COND_VAE,
            string clipVision = DEFAULT_CLIP_VISION,
            string bgRemoval = DEFAULT_BG_REMOVAL
        )
        {
            if (seed < 0)
                seed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (seed < 0) seed = -seed;
            if (steps < 1) steps = DEFAULT_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            var inv = CultureInfo.InvariantCulture;
            string seedStr = (seed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string escInputImage = ExtendedJson.Escape(inputImage ?? string.Empty);
            tripoUnet = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            tripoVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            condVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            clipVision = ExtendedJson.Escape(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            bgRemoval = ExtendedJson.Escape(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            return "{" + "\n" +
            "  \"51\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mesh\": [\"92\", 0],\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_PLY_PREFIX) + "\",\n" +
            "      \"image\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SaveGLB\"\n" +
            "  },\n" +
            "  \"67\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mesh\": [\"76\", 0],\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_MESH_PREFIX) + "\",\n" +
            "      \"image\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SaveGLB\"\n" +
            "  },\n" +
            "  \"76\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat\": [\"107\", 0],\n" +
            "      \"resolution\": 384,\n" +
            "      \"kernel\": 5,\n" +
            "      \"smooth\": 0,\n" +
            "      \"level\": 0.6,\n" +
            "      \"min_component\": 500,\n" +
            "      \"min_opacity\": 0.02,\n" +
            "      \"color_sharpen\": 2\n" +
            "    },\n" +
            "    \"class_type\": \"SplatToMesh\"\n" +
            "  },\n" +
            "  \"92\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat\": [\"107\", 0],\n" +
            "      \"format\": \"" + "ply" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SplatToFile3D\"\n" +
            "  },\n" +
            "  \"99\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + escInputImage + "\",\n" +
            "      \"upload\": \"" + "image" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  },\n" +
            "  \"100\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip_vision\": [\"104\", 0],\n" +
            "      \"vae\": [\"106\", 0],\n" +
            "      \"image\": [\"103\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatConditioning\"\n" +
            "  },\n" +
            "  \"101\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"111\", 0],\n" +
            "      \"positive\": [\"100\", 0],\n" +
            "      \"negative\": [\"100\", 1],\n" +
            "      \"latent_image\": [\"100\", 2],\n" +
            "      \"seed\": " + seedStr + ",\n" +
            "      \"steps\": " + steps.ToString(inv) + ",\n" +
            "      \"cfg\": 3,\n" +
            "      \"sampler_name\": \"" + "dpmpp_2m" + "\",\n" +
            "      \"scheduler\": \"" + "simple" + "\",\n" +
            "      \"denoise\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"KSampler\"\n" +
            "  },\n" +
            "  \"102\": {\n" +
            "    \"inputs\": {\n" +
            "      \"on_false\": [\"109\", 0],\n" +
            "      \"on_true\": [\"sg108_81\", 0],\n" +
            "      \"switch\": true\n" +
            "    },\n" +
            "    \"class_type\": \"ComfySwitchNode\"\n" +
            "  },\n" +
            "  \"103\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"99\", 0],\n" +
            "      \"mask\": [\"102\", 0],\n" +
            "      \"erode_radius\": 1,\n" +
            "      \"size\": 1024\n" +
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
            "  \"107\": {\n" +
            "    \"inputs\": {\n" +
            "      \"samples\": [\"101\", 0],\n" +
            "      \"vae\": [\"105\", 0],\n" +
            "      \"num_gaussians\": " + numGaussians.ToString(inv) + ",\n" +
            "      \"seed\": 790219963981395\n" +
            "    },\n" +
            "    \"class_type\": \"VAEDecodeTripoSplat\"\n" +
            "  },\n" +
            "  \"109\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mask\": [\"99\", 1]\n" +
            "    },\n" +
            "    \"class_type\": \"InvertMask\"\n" +
            "  },\n" +
            "  \"110\": {\n" +
            "    \"inputs\": {\n" +
            "      \"unet_name\": \"" + tripoUnet + "\",\n" +
            "      \"weight_dtype\": \"" + "default" + "\"\n" +
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
            "      \"octree_level\": 5,\n" +
            "      \"num_gaussians\": 16384,\n" +
            "      \"yaw\": 90,\n" +
            "      \"pitch\": 15,\n" +
            "      \"point_size\": 2\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatSamplingPreview\"\n" +
            "  },\n" +
            "  \"sg108_81\": {\n" +
            "    \"inputs\": {\n" +
            "      \"bg_removal_model\": [\"sg108_82\", 0],\n" +
            "      \"image\": [\"99\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"RemoveBackground\"\n" +
            "  },\n" +
            "  \"sg108_82\": {\n" +
            "    \"inputs\": {\n" +
            "      \"bg_removal_name\": \"" + bgRemoval + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadBackgroundRemovalModel\"\n" +
            "  },\n" +
            "  \"sg108_83\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mask\": [\"sg108_81\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"InvertMask\"\n" +
            "  },\n" +
            "  \"sg108_84\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"99\", 0],\n" +
            "      \"alpha\": [\"sg108_83\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"JoinImageWithAlpha\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// Faster TripoSplat proof of concept: fewer steps/gaussians, half-size preprocess.
    /// Freeze source(s):
    /// - D:\ComfyUIPortable\ComfyUI\user\default\workflows\PROOF_CONCEPT_FAST.json
    /// UI nodes: 21.
    /// API nodes: 21.
    /// Graph nodes:
    /// 51: SaveGLB
    /// 67: SaveGLB
    /// 76: SplatToMesh
    /// 92: SplatToFile3D
    /// 99: LoadImage
    /// 100: TripoSplatConditioning
    /// 101: KSampler
    /// 102: ComfySwitchNode
    /// 103: TripoSplatPreprocessImage
    /// 104: CLIPVisionLoader
    /// 105: VAELoader
    /// 106: VAELoader
    /// 107: VAEDecodeTripoSplat
    /// 109: InvertMask
    /// 110: UNETLoader
    /// 111: ComfySwitchNode
    /// 112: TripoSplatSamplingPreview
    /// 113: RemoveBackground
    /// 114: LoadBackgroundRemovalModel
    /// 115: InvertMask
    /// 116: JoinImageWithAlpha
    /// Sinks (no downstream consumers): 116 JoinImageWithAlpha, 51 SaveGLB, 67 SaveGLB.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// Source serializes without input metadata; widget order recovered from the matching PROOF_CONCEPT_1 nodes.
    /// </summary>
    public static class ProofConceptFastWorkflow
    {
        public const int DEFAULT_SEED = 77482913;
        public const int DEFAULT_STEPS = 8;
        public const int DEFAULT_NUM_GAUSSIANS = 65536;
        public const string DEFAULT_INPUT_IMAGE = "dining_table_white_bg.png";
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_PLY_PREFIX = "3d/PLY_";
        public const string DEFAULT_MESH_PREFIX = "3d/MESH_";

        public static string CreateWorkflow(
            long seed = DEFAULT_SEED,
            int steps = DEFAULT_STEPS,
            int numGaussians = DEFAULT_NUM_GAUSSIANS,
            string inputImage = DEFAULT_INPUT_IMAGE,
            string tripoUnet = DEFAULT_TRIPO_UNET,
            string tripoVae = DEFAULT_TRIPO_VAE,
            string condVae = DEFAULT_COND_VAE,
            string clipVision = DEFAULT_CLIP_VISION,
            string bgRemoval = DEFAULT_BG_REMOVAL
        )
        {
            if (seed < 0)
                seed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (seed < 0) seed = -seed;
            if (steps < 1) steps = DEFAULT_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            var inv = CultureInfo.InvariantCulture;
            string seedStr = (seed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string escInputImage = ExtendedJson.Escape(inputImage ?? string.Empty);
            tripoUnet = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            tripoVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            condVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            clipVision = ExtendedJson.Escape(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            bgRemoval = ExtendedJson.Escape(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            return "{" + "\n" +
            "  \"51\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mesh\": [\"92\", 0],\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_PLY_PREFIX) + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SaveGLB\"\n" +
            "  },\n" +
            "  \"67\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mesh\": [\"76\", 0],\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_MESH_PREFIX) + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SaveGLB\"\n" +
            "  },\n" +
            "  \"76\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat\": [\"107\", 0],\n" +
            "      \"resolution\": 192,\n" +
            "      \"kernel\": 5,\n" +
            "      \"smooth\": 0,\n" +
            "      \"level\": 0.6,\n" +
            "      \"min_component\": 500,\n" +
            "      \"min_opacity\": 0.02,\n" +
            "      \"color_sharpen\": 2\n" +
            "    },\n" +
            "    \"class_type\": \"SplatToMesh\"\n" +
            "  },\n" +
            "  \"92\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat\": [\"107\", 0],\n" +
            "      \"format\": \"" + "ply" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SplatToFile3D\"\n" +
            "  },\n" +
            "  \"99\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + escInputImage + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  },\n" +
            "  \"100\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip_vision\": [\"104\", 0],\n" +
            "      \"vae\": [\"106\", 0],\n" +
            "      \"image\": [\"103\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatConditioning\"\n" +
            "  },\n" +
            "  \"101\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"111\", 0],\n" +
            "      \"positive\": [\"100\", 0],\n" +
            "      \"negative\": [\"100\", 1],\n" +
            "      \"latent_image\": [\"100\", 2],\n" +
            "      \"seed\": " + seedStr + ",\n" +
            "      \"steps\": " + steps.ToString(inv) + ",\n" +
            "      \"cfg\": 3,\n" +
            "      \"sampler_name\": \"" + "dpmpp_2m" + "\",\n" +
            "      \"scheduler\": \"" + "simple" + "\",\n" +
            "      \"denoise\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"KSampler\"\n" +
            "  },\n" +
            "  \"102\": {\n" +
            "    \"inputs\": {\n" +
            "      \"on_false\": [\"109\", 0],\n" +
            "      \"on_true\": [\"113\", 0],\n" +
            "      \"switch\": true\n" +
            "    },\n" +
            "    \"class_type\": \"ComfySwitchNode\"\n" +
            "  },\n" +
            "  \"103\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"99\", 0],\n" +
            "      \"mask\": [\"102\", 0],\n" +
            "      \"erode_radius\": 1,\n" +
            "      \"size\": 512\n" +
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
            "  \"107\": {\n" +
            "    \"inputs\": {\n" +
            "      \"samples\": [\"101\", 0],\n" +
            "      \"vae\": [\"105\", 0],\n" +
            "      \"num_gaussians\": " + numGaussians.ToString(inv) + ",\n" +
            "      \"seed\": 790219963981395\n" +
            "    },\n" +
            "    \"class_type\": \"VAEDecodeTripoSplat\"\n" +
            "  },\n" +
            "  \"109\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mask\": [\"99\", 1]\n" +
            "    },\n" +
            "    \"class_type\": \"InvertMask\"\n" +
            "  },\n" +
            "  \"110\": {\n" +
            "    \"inputs\": {\n" +
            "      \"unet_name\": \"" + tripoUnet + "\",\n" +
            "      \"weight_dtype\": \"" + "default" + "\"\n" +
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
            "      \"octree_level\": 5,\n" +
            "      \"num_gaussians\": 16384,\n" +
            "      \"yaw\": 90,\n" +
            "      \"pitch\": 15,\n" +
            "      \"point_size\": 2\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatSamplingPreview\"\n" +
            "  },\n" +
            "  \"113\": {\n" +
            "    \"inputs\": {\n" +
            "      \"bg_removal_model\": [\"114\", 0],\n" +
            "      \"image\": [\"99\", 0]\n" +
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
            "      \"image\": [\"99\", 0],\n" +
            "      \"alpha\": [\"115\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"JoinImageWithAlpha\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// TripoSplat with turntable render, mesh extraction (SplatToMesh + JarJarSplatToMesh) and video/GLB outputs.
    /// Freeze source(s):
    /// - D:\ComfyUIPortable\ComfyUI\user\default\workflows\3d_triposplat_image_to_gaussian_splat (2).json
    /// UI nodes: 14 (source-muted/bypassed, kept active unless noted: 41, 42, 67, 75, 76, 79).
    /// API nodes: 28.
    /// Graph nodes:
    /// 41: CreateVideo
    /// 42: SaveVideo
    /// 51: SaveGLB
    /// 67: SaveGLB
    /// 75: RenderSplat
    /// 76: SplatToMesh
    /// 79: CreateCameraInfo
    /// 92: SplatToFile3D
    /// 99: LoadImage
    /// 100: JarJarSplatToMesh
    /// 102: JarJarPreviewMesh
    /// sg88_11: VAELoader
    /// sg88_16: UNETLoader
    /// sg88_2: TripoSplatPreprocessImage
    /// sg88_22: PreviewImage
    /// sg88_24: TripoSplatConditioning
    /// sg88_35: ComfySwitchNode
    /// sg88_55: VAEDecodeTripoSplat
    /// sg88_6: KSampler
    /// sg88_7: CLIPVisionLoader
    /// sg88_8: VAELoader
    /// sg88_85_81: RemoveBackground
    /// sg88_85_82: LoadBackgroundRemovalModel
    /// sg88_85_83: InvertMask
    /// sg88_85_84: JoinImageWithAlpha
    /// sg88_95: InvertMask
    /// sg88_96: ComfySwitchNode
    /// sg88_97: TripoSplatSamplingPreview
    /// Sinks (no downstream consumers): 102 JarJarPreviewMesh, 42 SaveVideo, 51 SaveGLB, 67 SaveGLB, sg88_22 PreviewImage, sg88_85_84 JoinImageWithAlpha.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// TripoSplat subgraph 88 expanded inline as sg88_* (nested background-removal as sg88_85_*).
    /// Render/mesh branch nodes were muted in source; they are included active here, so the freeze renders video + mesh + splat.
    /// </summary>
    public static class TripoSplatVideoWorkflow
    {
        public const int DEFAULT_SEED = 50;
        public const int DEFAULT_STEPS = 20;
        public const int DEFAULT_NUM_GAUSSIANS = 262144;
        public const string DEFAULT_INPUT_IMAGE = "Zoidberg.webp";
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_CLIP = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_MESH_FOLDER = "./output";
        public const string DEFAULT_MESH_FORMAT = "glb";
        public const string DEFAULT_SAVE_PREFIX = "3d/ComfyUI_TripoSplat";
        public const string DEFAULT_VIDEO_PREFIX = "video/ComfyUI_TripoSplat";

        public static string CreateWorkflow(
            long seed = DEFAULT_SEED,
            int steps = DEFAULT_STEPS,
            int numGaussians = DEFAULT_NUM_GAUSSIANS,
            string inputImage = DEFAULT_INPUT_IMAGE,
            string tripoUnet = DEFAULT_TRIPO_UNET,
            string tripoClip = DEFAULT_TRIPO_CLIP,
            string tripoVae = DEFAULT_TRIPO_VAE,
            string condVae = DEFAULT_COND_VAE,
            string bgRemoval = DEFAULT_BG_REMOVAL
        )
        {
            if (seed < 0)
                seed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (seed < 0) seed = -seed;
            if (steps < 1) steps = DEFAULT_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            var inv = CultureInfo.InvariantCulture;
            string seedStr = (seed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string escInputImage = ExtendedJson.Escape(inputImage ?? string.Empty);
            tripoUnet = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            tripoClip = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoClip) ? DEFAULT_TRIPO_CLIP : tripoClip);
            tripoVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            condVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            bgRemoval = ExtendedJson.Escape(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            return "{" + "\n" +
            "  \"41\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"75\", 0],\n" +
            "      \"fps\": 25,\n" +
            "      \"bit_depth\": \"" + "auto" + "\",\n" +
            "      \"color_space\": \"" + "sRGB" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"CreateVideo\"\n" +
            "  },\n" +
            "  \"42\": {\n" +
            "    \"inputs\": {\n" +
            "      \"video\": [\"41\", 0],\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_VIDEO_PREFIX) + "\",\n" +
            "      \"format\": \"" + "auto" + "\",\n" +
            "      \"format.codec\": \"" + "auto" + "\",\n" +
            "      \"codec\": \"" + "auto" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SaveVideo\"\n" +
            "  },\n" +
            "  \"51\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mesh\": [\"92\", 0],\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_SAVE_PREFIX) + "\",\n" +
            "      \"image\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SaveGLB\"\n" +
            "  },\n" +
            "  \"67\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mesh\": [\"76\", 0],\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_SAVE_PREFIX) + "\",\n" +
            "      \"image\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SaveGLB\"\n" +
            "  },\n" +
            "  \"75\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat\": [\"sg88_55\", 0],\n" +
            "      \"camera_info\": [\"79\", 0],\n" +
            "      \"width\": 1024,\n" +
            "      \"height\": 1024,\n" +
            "      \"frames\": 75,\n" +
            "      \"splat_scale\": 1,\n" +
            "      \"sharpen\": 2,\n" +
            "      \"headlight_shading\": 0,\n" +
            "      \"opacity_threshold\": 0,\n" +
            "      \"render_style\": \"" + "color" + "\",\n" +
            "      \"background\": \"" + "#848484" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"RenderSplat\"\n" +
            "  },\n" +
            "  \"76\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat\": [\"sg88_55\", 0],\n" +
            "      \"resolution\": 384,\n" +
            "      \"kernel\": 5,\n" +
            "      \"smooth\": 0,\n" +
            "      \"level\": 0.6,\n" +
            "      \"min_component\": 500,\n" +
            "      \"min_opacity\": 0.02,\n" +
            "      \"color_sharpen\": 2\n" +
            "    },\n" +
            "    \"class_type\": \"SplatToMesh\"\n" +
            "  },\n" +
            "  \"79\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mode\": \"" + "orbit" + "\",\n" +
            "      \"mode.yaw\": 35,\n" +
            "      \"mode.pitch\": 30,\n" +
            "      \"mode.distance\": 2.5,\n" +
            "      \"target_x\": 0,\n" +
            "      \"target_y\": 0,\n" +
            "      \"target_z\": 0,\n" +
            "      \"roll\": 0,\n" +
            "      \"fov\": 35,\n" +
            "      \"zoom\": 1,\n" +
            "      \"camera_type\": \"" + "perspective" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"CreateCameraInfo\"\n" +
            "  },\n" +
            "  \"92\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat\": [\"sg88_55\", 0],\n" +
            "      \"format\": \"" + "ply" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SplatToFile3D\"\n" +
            "  },\n" +
            "  \"99\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + escInputImage + "\",\n" +
            "      \"upload\": \"" + "image" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  },\n" +
            "  \"100\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model_3d\": [\"92\", 0],\n" +
            "      \"file_path\": \"" + "glb" + "\",\n" +
            "      \"format\": \"" + ExtendedJson.Escape(DEFAULT_MESH_FORMAT) + "\",\n" +
            "      \"output_folder\": \"" + ExtendedJson.Escape(DEFAULT_MESH_FOLDER) + "\",\n" +
            "      \"filename\": \"" + "" + "\",\n" +
            "      \"opacity_cutoff\": 0.05,\n" +
            "      \"poisson_depth\": 12,\n" +
            "      \"max_points\": 100000,\n" +
            "      \"rotate_x\": 0,\n" +
            "      \"rotate_y\": 0,\n" +
            "      \"rotate_z\": true,\n" +
            "      \"decimate_enable\": 10000,\n" +
            "      \"target_faces\": 50000,\n" +
            "      \"preserve_topology\": true,\n" +
            "      \"quality_threshold\": 0.3,\n" +
            "      \"bake_texture\": true,\n" +
            "      \"texture_size\": 2048\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarSplatToMesh\"\n" +
            "  },\n" +
            "  \"102\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mesh\": [\"100\", 0],\n" +
            "      \"width\": 1024,\n" +
            "      \"height\": 1024,\n" +
            "      \"mesh_path\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarPreviewMesh\"\n" +
            "  },\n" +
            "  \"sg88_11\": {\n" +
            "    \"inputs\": {\n" +
            "      \"vae_name\": \"" + tripoVae + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"VAELoader\"\n" +
            "  },\n" +
            "  \"sg88_16\": {\n" +
            "    \"inputs\": {\n" +
            "      \"unet_name\": \"" + tripoUnet + "\",\n" +
            "      \"weight_dtype\": \"" + "default" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UNETLoader\"\n" +
            "  },\n" +
            "  \"sg88_2\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"99\", 0],\n" +
            "      \"mask\": [\"sg88_35\", 0],\n" +
            "      \"erode_radius\": 1,\n" +
            "      \"size\": 1024\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatPreprocessImage\"\n" +
            "  },\n" +
            "  \"sg88_22\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"sg88_2\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"sg88_24\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip_vision\": [\"sg88_7\", 0],\n" +
            "      \"vae\": [\"sg88_8\", 0],\n" +
            "      \"image\": [\"sg88_2\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatConditioning\"\n" +
            "  },\n" +
            "  \"sg88_35\": {\n" +
            "    \"inputs\": {\n" +
            "      \"on_false\": [\"sg88_95\", 0],\n" +
            "      \"on_true\": [\"sg88_85_81\", 0],\n" +
            "      \"switch\": true\n" +
            "    },\n" +
            "    \"class_type\": \"ComfySwitchNode\"\n" +
            "  },\n" +
            "  \"sg88_55\": {\n" +
            "    \"inputs\": {\n" +
            "      \"samples\": [\"sg88_6\", 0],\n" +
            "      \"vae\": [\"sg88_11\", 0],\n" +
            "      \"num_gaussians\": " + numGaussians.ToString(inv) + ",\n" +
            "      \"seed\": 790219963981395\n" +
            "    },\n" +
            "    \"class_type\": \"VAEDecodeTripoSplat\"\n" +
            "  },\n" +
            "  \"sg88_6\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"sg88_96\", 0],\n" +
            "      \"positive\": [\"sg88_24\", 0],\n" +
            "      \"negative\": [\"sg88_24\", 1],\n" +
            "      \"latent_image\": [\"sg88_24\", 2],\n" +
            "      \"seed\": " + seedStr + ",\n" +
            "      \"steps\": " + steps.ToString(inv) + ",\n" +
            "      \"cfg\": 3,\n" +
            "      \"sampler_name\": \"" + "dpmpp_2m" + "\",\n" +
            "      \"scheduler\": \"" + "simple" + "\",\n" +
            "      \"denoise\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"KSampler\"\n" +
            "  },\n" +
            "  \"sg88_7\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip_name\": \"" + tripoClip + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"CLIPVisionLoader\"\n" +
            "  },\n" +
            "  \"sg88_8\": {\n" +
            "    \"inputs\": {\n" +
            "      \"vae_name\": \"" + condVae + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"VAELoader\"\n" +
            "  },\n" +
            "  \"sg88_85_81\": {\n" +
            "    \"inputs\": {\n" +
            "      \"bg_removal_model\": [\"sg88_85_82\", 0],\n" +
            "      \"image\": [\"99\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"RemoveBackground\"\n" +
            "  },\n" +
            "  \"sg88_85_82\": {\n" +
            "    \"inputs\": {\n" +
            "      \"bg_removal_name\": \"" + bgRemoval + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadBackgroundRemovalModel\"\n" +
            "  },\n" +
            "  \"sg88_85_83\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mask\": [\"sg88_85_81\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"InvertMask\"\n" +
            "  },\n" +
            "  \"sg88_85_84\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"99\", 0],\n" +
            "      \"alpha\": [\"sg88_85_83\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"JoinImageWithAlpha\"\n" +
            "  },\n" +
            "  \"sg88_95\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mask\": [\"99\", 1]\n" +
            "    },\n" +
            "    \"class_type\": \"InvertMask\"\n" +
            "  },\n" +
            "  \"sg88_96\": {\n" +
            "    \"inputs\": {\n" +
            "      \"on_false\": [\"sg88_16\", 0],\n" +
            "      \"on_true\": [\"sg88_97\", 0],\n" +
            "      \"switch\": true\n" +
            "    },\n" +
            "    \"class_type\": \"ComfySwitchNode\"\n" +
            "  },\n" +
            "  \"sg88_97\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"sg88_16\", 0],\n" +
            "      \"vae\": [\"sg88_11\", 0],\n" +
            "      \"octree_level\": 5,\n" +
            "      \"num_gaussians\": 16384,\n" +
            "      \"yaw\": 90,\n" +
            "      \"pitch\": 15,\n" +
            "      \"point_size\": 2\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatSamplingPreview\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// TripoSplat with turntable render plus UniRig auto-rig and rig preview.
    /// Freeze source(s):
    /// - D:\PixalComfy\ComfyUI\user\default\workflows\3d_triposplat_image_to_gaussian_splat.json
    /// UI nodes: 15.
    /// API nodes: 29.
    /// Graph nodes:
    /// 41: CreateVideo
    /// 42: SaveVideo
    /// 51: SaveGLB
    /// 67: SaveGLB
    /// 75: RenderSplat
    /// 76: SplatToMesh
    /// 79: CreateCameraInfo
    /// 92: SplatToFile3D
    /// 99: LoadImage
    /// 100: UniRigLoadModel
    /// 101: UniRigAutoRig
    /// 102: UniRigPreviewRiggedMesh
    /// sg88_11: VAELoader
    /// sg88_16: UNETLoader
    /// sg88_2: TripoSplatPreprocessImage
    /// sg88_22: PreviewImage
    /// sg88_24: TripoSplatConditioning
    /// sg88_35: ComfySwitchNode
    /// sg88_55: VAEDecodeTripoSplat
    /// sg88_6: KSampler
    /// sg88_7: CLIPVisionLoader
    /// sg88_8: VAELoader
    /// sg88_85_81: RemoveBackground
    /// sg88_85_82: LoadBackgroundRemovalModel
    /// sg88_85_83: InvertMask
    /// sg88_85_84: JoinImageWithAlpha
    /// sg88_95: InvertMask
    /// sg88_96: ComfySwitchNode
    /// sg88_97: TripoSplatSamplingPreview
    /// Sinks (no downstream consumers): 102 UniRigPreviewRiggedMesh, 42 SaveVideo, 51 SaveGLB, 67 SaveGLB, sg88_22 PreviewImage, sg88_85_84 JoinImageWithAlpha.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// Source instance 88 carries no overrides; frozen values are the subgraph inner defaults (same template as TripoSplatVideoWorkflow).
    /// </summary>
    public static class TripoSplatUniRigWorkflow
    {
        public const int DEFAULT_SEED = 46;
        public const int DEFAULT_STEPS = 20;
        public const int DEFAULT_NUM_GAUSSIANS = 262144;
        public const string DEFAULT_INPUT_IMAGE = "image-2.webp";
        public const string DEFAULT_SKELETON = "mixamo";
        public const int DEFAULT_TARGET_FACES = 50000;
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_CLIP = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_SAVE_PREFIX = "3d/ComfyUI_TripoSplat";
        public const string DEFAULT_VIDEO_PREFIX = "video/ComfyUI_TripoSplat";

        public static string CreateWorkflow(
            long seed = DEFAULT_SEED,
            int steps = DEFAULT_STEPS,
            int numGaussians = DEFAULT_NUM_GAUSSIANS,
            string inputImage = DEFAULT_INPUT_IMAGE,
            string skeletonTemplate = DEFAULT_SKELETON,
            int targetFaceCount = DEFAULT_TARGET_FACES,
            string tripoUnet = DEFAULT_TRIPO_UNET,
            string tripoClip = DEFAULT_TRIPO_CLIP,
            string tripoVae = DEFAULT_TRIPO_VAE,
            string condVae = DEFAULT_COND_VAE,
            string bgRemoval = DEFAULT_BG_REMOVAL
        )
        {
            if (seed < 0)
                seed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (seed < 0) seed = -seed;
            if (steps < 1) steps = DEFAULT_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            if (targetFaceCount < 1) targetFaceCount = DEFAULT_TARGET_FACES;
            var inv = CultureInfo.InvariantCulture;
            string seedStr = (seed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string escInputImage = ExtendedJson.Escape(inputImage ?? string.Empty);
            skeletonTemplate = ExtendedJson.Escape(string.IsNullOrWhiteSpace(skeletonTemplate) ? DEFAULT_SKELETON : skeletonTemplate);
            tripoUnet = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            tripoClip = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoClip) ? DEFAULT_TRIPO_CLIP : tripoClip);
            tripoVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            condVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            bgRemoval = ExtendedJson.Escape(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            return "{" + "\n" +
            "  \"41\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"75\", 0],\n" +
            "      \"fps\": 25\n" +
            "    },\n" +
            "    \"class_type\": \"CreateVideo\"\n" +
            "  },\n" +
            "  \"42\": {\n" +
            "    \"inputs\": {\n" +
            "      \"video\": [\"41\", 0],\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_VIDEO_PREFIX) + "\",\n" +
            "      \"format\": \"" + "auto" + "\",\n" +
            "      \"codec\": \"" + "auto" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SaveVideo\"\n" +
            "  },\n" +
            "  \"51\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mesh\": [\"92\", 0],\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_SAVE_PREFIX) + "\",\n" +
            "      \"image\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SaveGLB\"\n" +
            "  },\n" +
            "  \"67\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mesh\": [\"76\", 0],\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_SAVE_PREFIX) + "\",\n" +
            "      \"image\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SaveGLB\"\n" +
            "  },\n" +
            "  \"75\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat\": [\"sg88_55\", 0],\n" +
            "      \"camera_info\": [\"79\", 0],\n" +
            "      \"width\": 1024,\n" +
            "      \"height\": 1024,\n" +
            "      \"frames\": 75,\n" +
            "      \"splat_scale\": 1,\n" +
            "      \"sharpen\": 2,\n" +
            "      \"headlight_shading\": 0,\n" +
            "      \"opacity_threshold\": 0,\n" +
            "      \"render_style\": \"" + "color" + "\",\n" +
            "      \"background\": \"" + "#848484" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"RenderSplat\"\n" +
            "  },\n" +
            "  \"76\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat\": [\"sg88_55\", 0],\n" +
            "      \"resolution\": 384,\n" +
            "      \"kernel\": 5,\n" +
            "      \"smooth\": 0,\n" +
            "      \"level\": 0.6,\n" +
            "      \"min_component\": 500,\n" +
            "      \"min_opacity\": 0.02,\n" +
            "      \"color_sharpen\": 2\n" +
            "    },\n" +
            "    \"class_type\": \"SplatToMesh\"\n" +
            "  },\n" +
            "  \"79\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mode\": \"" + "orbit" + "\",\n" +
            "      \"mode.yaw\": 35,\n" +
            "      \"mode.pitch\": 30,\n" +
            "      \"mode.distance\": 2.5,\n" +
            "      \"target_x\": 0,\n" +
            "      \"target_y\": 0,\n" +
            "      \"target_z\": 0,\n" +
            "      \"roll\": 0,\n" +
            "      \"fov\": 35,\n" +
            "      \"zoom\": 1,\n" +
            "      \"camera_type\": \"" + "perspective" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"CreateCameraInfo\"\n" +
            "  },\n" +
            "  \"92\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat\": [\"sg88_55\", 0],\n" +
            "      \"format\": \"" + "ply" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SplatToFile3D\"\n" +
            "  },\n" +
            "  \"99\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + escInputImage + "\",\n" +
            "      \"upload\": \"" + "image" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  },\n" +
            "  \"100\": {\n" +
            "    \"inputs\": {\n" +
            "      \"precision\": \"" + "auto" + "\",\n" +
            "      \"attn_backend\": \"" + "auto" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigLoadModel\"\n" +
            "  },\n" +
            "  \"101\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"100\", 0],\n" +
            "      \"skeleton_template\": \"" + skeletonTemplate + "\",\n" +
            "      \"fbx_name\": \"" + "" + "\",\n" +
            "      \"target_face_count\": " + targetFaceCount.ToString(inv) + ",\n" +
            "      \"debug\": true\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigAutoRig\"\n" +
            "  },\n" +
            "  \"102\": {\n" +
            "    \"inputs\": {\n" +
            "      \"fbx_output_path\": [\"101\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigPreviewRiggedMesh\"\n" +
            "  },\n" +
            "  \"sg88_11\": {\n" +
            "    \"inputs\": {\n" +
            "      \"vae_name\": \"" + tripoVae + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"VAELoader\"\n" +
            "  },\n" +
            "  \"sg88_16\": {\n" +
            "    \"inputs\": {\n" +
            "      \"unet_name\": \"" + tripoUnet + "\",\n" +
            "      \"weight_dtype\": \"" + "default" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UNETLoader\"\n" +
            "  },\n" +
            "  \"sg88_2\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"99\", 0],\n" +
            "      \"mask\": [\"sg88_35\", 0],\n" +
            "      \"erode_radius\": 1,\n" +
            "      \"size\": 1024\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatPreprocessImage\"\n" +
            "  },\n" +
            "  \"sg88_22\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"sg88_2\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"sg88_24\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip_vision\": [\"sg88_7\", 0],\n" +
            "      \"vae\": [\"sg88_8\", 0],\n" +
            "      \"image\": [\"sg88_2\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatConditioning\"\n" +
            "  },\n" +
            "  \"sg88_35\": {\n" +
            "    \"inputs\": {\n" +
            "      \"on_false\": [\"sg88_95\", 0],\n" +
            "      \"on_true\": [\"sg88_85_81\", 0],\n" +
            "      \"switch\": true\n" +
            "    },\n" +
            "    \"class_type\": \"ComfySwitchNode\"\n" +
            "  },\n" +
            "  \"sg88_55\": {\n" +
            "    \"inputs\": {\n" +
            "      \"samples\": [\"sg88_6\", 0],\n" +
            "      \"vae\": [\"sg88_11\", 0],\n" +
            "      \"num_gaussians\": " + numGaussians.ToString(inv) + ",\n" +
            "      \"seed\": 790219963981395\n" +
            "    },\n" +
            "    \"class_type\": \"VAEDecodeTripoSplat\"\n" +
            "  },\n" +
            "  \"sg88_6\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"sg88_96\", 0],\n" +
            "      \"positive\": [\"sg88_24\", 0],\n" +
            "      \"negative\": [\"sg88_24\", 1],\n" +
            "      \"latent_image\": [\"sg88_24\", 2],\n" +
            "      \"seed\": " + seedStr + ",\n" +
            "      \"steps\": " + steps.ToString(inv) + ",\n" +
            "      \"cfg\": 3,\n" +
            "      \"sampler_name\": \"" + "dpmpp_2m" + "\",\n" +
            "      \"scheduler\": \"" + "simple" + "\",\n" +
            "      \"denoise\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"KSampler\"\n" +
            "  },\n" +
            "  \"sg88_7\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip_name\": \"" + tripoClip + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"CLIPVisionLoader\"\n" +
            "  },\n" +
            "  \"sg88_8\": {\n" +
            "    \"inputs\": {\n" +
            "      \"vae_name\": \"" + condVae + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"VAELoader\"\n" +
            "  },\n" +
            "  \"sg88_85_81\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"99\", 0],\n" +
            "      \"bg_removal_model\": [\"sg88_85_82\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"RemoveBackground\"\n" +
            "  },\n" +
            "  \"sg88_85_82\": {\n" +
            "    \"inputs\": {\n" +
            "      \"bg_removal_name\": \"" + bgRemoval + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadBackgroundRemovalModel\"\n" +
            "  },\n" +
            "  \"sg88_85_83\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mask\": [\"sg88_85_81\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"InvertMask\"\n" +
            "  },\n" +
            "  \"sg88_85_84\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"99\", 0],\n" +
            "      \"alpha\": [\"sg88_85_83\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"JoinImageWithAlpha\"\n" +
            "  },\n" +
            "  \"sg88_95\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mask\": [\"99\", 1]\n" +
            "    },\n" +
            "    \"class_type\": \"InvertMask\"\n" +
            "  },\n" +
            "  \"sg88_96\": {\n" +
            "    \"inputs\": {\n" +
            "      \"on_false\": [\"sg88_16\", 0],\n" +
            "      \"on_true\": [\"sg88_97\", 0],\n" +
            "      \"switch\": true\n" +
            "    },\n" +
            "    \"class_type\": \"ComfySwitchNode\"\n" +
            "  },\n" +
            "  \"sg88_97\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"sg88_16\", 0],\n" +
            "      \"vae\": [\"sg88_11\", 0],\n" +
            "      \"octree_level\": 5,\n" +
            "      \"num_gaussians\": 16384,\n" +
            "      \"yaw\": 90,\n" +
            "      \"pitch\": 15,\n" +
            "      \"point_size\": 2\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatSamplingPreview\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// Pixal3D image-to-3D example: LoadImage -> Pixal3DImageTo3D -> Pixal3DExportGLB.
    /// Freeze source(s):
    /// - D:\PixalComfy\ComfyUI\user\default\workflows\ABC123.json
    /// - D:\PixalComfy\ComfyUI\user\default\workflows\pixal3d_example_workflow.json
    /// UI nodes: 9.
    /// API nodes: 4.
    /// Graph nodes:
    /// 15: LoadImage
    /// 17: Pixal3DExportGLB
    /// 23: Pixal3DModelLoader
    /// 24: Pixal3DImageTo3D
    /// Sinks (no downstream consumers): 17 Pixal3DExportGLB.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// ABC123.json and pixal3d_example_workflow.json differ by a single byte in a dropped PreviewAny widget; normalized graphs are identical, embedded once.
    /// </summary>
    public static class Pixal3DExampleWorkflow
    {
        public const long DEFAULT_SEED = 575109877223668;
        public const int DEFAULT_STEPS = 12;
        public const string DEFAULT_INPUT_IMAGE = "image.webp";
        public const string DEFAULT_PIPELINE = "1024_cascade";
        public const string DEFAULT_MODEL_REPO = "TencentARC/Pixal3D";
        public const string DEFAULT_EXPORT_PREFIX = "pixal3d";

        public static string CreateWorkflow(
            long seed = DEFAULT_SEED,
            int steps = DEFAULT_STEPS,
            string inputImage = DEFAULT_INPUT_IMAGE,
            string pipelineType = DEFAULT_PIPELINE,
            string modelRepo = DEFAULT_MODEL_REPO
        )
        {
            if (seed < 0)
                seed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (seed < 0) seed = -seed;
            if (steps < 1) steps = DEFAULT_STEPS;
            var inv = CultureInfo.InvariantCulture;
            string seedStr = (seed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string escInputImage = ExtendedJson.Escape(inputImage ?? string.Empty);
            pipelineType = ExtendedJson.Escape(string.IsNullOrWhiteSpace(pipelineType) ? DEFAULT_PIPELINE : pipelineType);
            modelRepo = ExtendedJson.Escape(string.IsNullOrWhiteSpace(modelRepo) ? DEFAULT_MODEL_REPO : modelRepo);
            return "{" + "\n" +
            "  \"15\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + escInputImage + "\",\n" +
            "      \"upload\": \"" + "image" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  },\n" +
            "  \"17\": {\n" +
            "    \"inputs\": {\n" +
            "      \"pixal3d_result\": [\"24\", 0],\n" +
            "      \"decimation_target\": 1000000,\n" +
            "      \"texture_size\": 2048,\n" +
            "      \"remesh\": true,\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_EXPORT_PREFIX) + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DExportGLB\"\n" +
            "  },\n" +
            "  \"23\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model_repo\": \"" + modelRepo + "\",\n" +
            "      \"hf_endpoint\": \"" + "https://huggingface.co" + "\",\n" +
            "      \"attention_backend\": \"" + "flash_attn_3" + "\",\n" +
            "      \"vram_mode\": \"" + "hybrid_low_vram" + "\",\n" +
            "      \"download_if_missing\": false,\n" +
            "      \"load_moge\": true,\n" +
            "      \"load_rembg\": true,\n" +
            "      \"naf_mode\": \"" + "fallback_if_missing" + "\",\n" +
            "      \"naf_target_size\": \"" + "upstream" + "\",\n" +
            "      \"preload_naf\": false,\n" +
            "      \"force_reload\": false\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DModelLoader\"\n" +
            "  },\n" +
            "  \"24\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"23\", 0],\n" +
            "      \"image\": [\"15\", 0],\n" +
            "      \"seed\": " + seedStr + ",\n" +
            "      \"pipeline_type\": \"" + pipelineType + "\",\n" +
            "      \"background_mode\": \"" + "auto_remove" + "\",\n" +
            "      \"camera_mode\": \"" + "moge" + "\",\n" +
            "      \"manual_camera_angle_x\": 0.857556,\n" +
            "      \"manual_distance\": 2,\n" +
            "      \"mesh_scale\": 1,\n" +
            "      \"extend_pixel\": 0,\n" +
            "      \"camera_resolution\": 512,\n" +
            "      \"steps\": " + steps.ToString(inv) + ",\n" +
            "      \"guidance\": 7.5,\n" +
            "      \"texture_guidance\": 1,\n" +
            "      \"max_num_tokens\": 49152,\n" +
            "      \"force_offload\": false\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DImageTo3D\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// TripoSplat + UniRig + Pixal3D retexture: splat and mesh from one image, rig the mesh, retexture a second GLB.
    /// Freeze source(s):
    /// - D:\PixalComfy\ComfyUI\user\default\workflows\faggh.json
    /// UI nodes: 23 (source-muted/bypassed, kept active unless noted: 106, 107, 108, 110).
    /// API nodes: 32.
    /// Graph nodes:
    /// 51: SaveGLB
    /// 67: SaveGLB
    /// 92: SplatToFile3D
    /// 99: LoadImage
    /// 101: UniRigAutoRig
    /// 102: JarJarLoadMesh
    /// 105: UniRigLoadModel
    /// 106: StringConcatenate
    /// 107: UniRigApplyAnimation
    /// 112: Pixal3DRetextureGLB
    /// 115: StringConcatenate
    /// 116: SplatToMesh
    /// 122: LoadImage
    /// 127: JarJarIntToString
    /// 129: StringConcatenate
    /// sg88_11: VAELoader
    /// sg88_16: UNETLoader
    /// sg88_2: TripoSplatPreprocessImage
    /// sg88_22: PreviewImage
    /// sg88_24: TripoSplatConditioning
    /// sg88_35: ComfySwitchNode
    /// sg88_55: VAEDecodeTripoSplat
    /// sg88_6: KSampler
    /// sg88_7: CLIPVisionLoader
    /// sg88_8: VAELoader
    /// sg88_85_81: RemoveBackground
    /// sg88_85_82: LoadBackgroundRemovalModel
    /// sg88_85_83: InvertMask
    /// sg88_85_84: JoinImageWithAlpha
    /// sg88_95: InvertMask
    /// sg88_96: ComfySwitchNode
    /// sg88_97: TripoSplatSamplingPreview
    /// Sinks (no downstream consumers): 107 UniRigApplyAnimation, 51 SaveGLB, 67 SaveGLB, sg88_22 PreviewImage, sg88_85_84 JoinImageWithAlpha.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// PrimitiveNode 117 (seed 6, increment) rewired to a literal; seed fans out to the TripoSplat KSampler and the JarJarIntToString filename chain.
    /// Source JarJarLoadMesh 102 path embeds stray quotes; preserved verbatim in the frozen default, overridden by inputGlb.
    /// Bypassed animation branch (106/107) included active; display-only Preview3D/PreviewAny nodes dropped.
    /// </summary>
    public static class FagghWorkflow
    {
        public const int DEFAULT_SEED = 6;
        public const int DEFAULT_STEPS = 20;
        public const int DEFAULT_NUM_GAUSSIANS = 262144;
        public const string DEFAULT_INPUT_IMAGE = "image-1.webp";
        public const string DEFAULT_RETEXTURE_IMAGE = "Flux2-Klein_00034_.png";
        public const string DEFAULT_INPUT_GLB = "C:\\Users\\meesa\\Desktop\\PixalComfy\\ComfyUI\\output\\TEST_00002_.glb";
        public const int DEFAULT_TEXTURE_SIZE = 4096;
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_CLIP = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_SAVE_PREFIX = "3d/ComfyUI_TripoSplat";
        public const string DEFAULT_TEST_PREFIX = "TEST";

        public static string CreateWorkflow(
            long seed = DEFAULT_SEED,
            int steps = DEFAULT_STEPS,
            int numGaussians = DEFAULT_NUM_GAUSSIANS,
            string inputImage = DEFAULT_INPUT_IMAGE,
            string retextureImage = DEFAULT_RETEXTURE_IMAGE,
            string inputGlb = DEFAULT_INPUT_GLB,
            int textureSize = DEFAULT_TEXTURE_SIZE,
            string tripoUnet = DEFAULT_TRIPO_UNET,
            string tripoClip = DEFAULT_TRIPO_CLIP,
            string tripoVae = DEFAULT_TRIPO_VAE,
            string condVae = DEFAULT_COND_VAE,
            string bgRemoval = DEFAULT_BG_REMOVAL
        )
        {
            if (seed < 0)
                seed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (seed < 0) seed = -seed;
            if (steps < 1) steps = DEFAULT_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            if (textureSize < 1) textureSize = DEFAULT_TEXTURE_SIZE;
            var inv = CultureInfo.InvariantCulture;
            string seedStr = (seed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string escInputImage = ExtendedJson.Escape(inputImage ?? string.Empty);
            string escRetextureImage = ExtendedJson.Escape(retextureImage ?? string.Empty);
            string escInputGlb = ExtendedJson.Escape(inputGlb ?? string.Empty);
            tripoUnet = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            tripoClip = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoClip) ? DEFAULT_TRIPO_CLIP : tripoClip);
            tripoVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            condVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            bgRemoval = ExtendedJson.Escape(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            return "{" + "\n" +
            "  \"51\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mesh\": [\"92\", 0],\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_SAVE_PREFIX) + "\",\n" +
            "      \"image\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SaveGLB\"\n" +
            "  },\n" +
            "  \"67\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mesh\": [\"116\", 0],\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_TEST_PREFIX) + "\",\n" +
            "      \"image\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SaveGLB\"\n" +
            "  },\n" +
            "  \"92\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat\": [\"sg88_55\", 0],\n" +
            "      \"format\": \"" + "spz" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"SplatToFile3D\"\n" +
            "  },\n" +
            "  \"99\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + escInputImage + "\",\n" +
            "      \"upload\": \"" + "image" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  },\n" +
            "  \"101\": {\n" +
            "    \"inputs\": {\n" +
            "      \"trimesh\": [\"102\", 0],\n" +
            "      \"model\": [\"105\", 0],\n" +
            "      \"skeleton_template\": \"" + "mixamo" + "\",\n" +
            "      \"fbx_name\": [\"112\", 0],\n" +
            "      \"target_face_count\": 500000,\n" +
            "      \"debug\": true\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigAutoRig\"\n" +
            "  },\n" +
            "  \"102\": {\n" +
            "    \"inputs\": {\n" +
            "      \"file_path\": \"" + escInputGlb + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarLoadMesh\"\n" +
            "  },\n" +
            "  \"105\": {\n" +
            "    \"inputs\": {\n" +
            "      \"precision\": \"" + "auto" + "\",\n" +
            "      \"attn_backend\": \"" + "auto" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigLoadModel\"\n" +
            "  },\n" +
            "  \"106\": {\n" +
            "    \"inputs\": {\n" +
            "      \"string_a\": \"" + "C:\\\\Users\\\\meesa\\\\Desktop\\\\PixalComfy\\\\ComfyUI\\\\output\\\\" + "\",\n" +
            "      \"string_b\": [\"101\", 0],\n" +
            "      \"delimiter\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"StringConcatenate\"\n" +
            "  },\n" +
            "  \"107\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model_fbx_path\": [\"106\", 0],\n" +
            "      \"animation_type\": \"" + "smpl" + "\",\n" +
            "      \"animation_file\": \"" + "Breakdance.fbx" + "\",\n" +
            "      \"output_name\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigApplyAnimation\"\n" +
            "  },\n" +
            "  \"112\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"122\", 0],\n" +
            "      \"glb_path\": [\"129\", 0],\n" +
            "      \"texture_size\": " + textureSize.ToString(inv) + ",\n" +
            "      \"filename_prefix\": \"" + "pixal3d_retextured" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DRetextureGLB\"\n" +
            "  },\n" +
            "  \"115\": {\n" +
            "    \"inputs\": {\n" +
            "      \"string_a\": \"" + "C:\\\\Users\\\\meesa\\\\Desktop\\\\PixalComfy\\\\ComfyUI\\\\output\\\\3d\\\\ComfyUI_TripoSplat_000" + "\",\n" +
            "      \"string_b\": [\"127\", 0],\n" +
            "      \"delimiter\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"StringConcatenate\"\n" +
            "  },\n" +
            "  \"116\": {\n" +
            "    \"inputs\": {\n" +
            "      \"splat\": [\"sg88_55\", 0],\n" +
            "      \"resolution\": 768,\n" +
            "      \"kernel\": 5,\n" +
            "      \"smooth\": 0,\n" +
            "      \"level\": 0.23,\n" +
            "      \"min_component\": 500,\n" +
            "      \"min_opacity\": 0.02,\n" +
            "      \"color_sharpen\": 2\n" +
            "    },\n" +
            "    \"class_type\": \"SplatToMesh\"\n" +
            "  },\n" +
            "  \"122\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + escRetextureImage + "\",\n" +
            "      \"upload\": \"" + "image" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  },\n" +
            "  \"127\": {\n" +
            "    \"inputs\": {\n" +
            "      \"value\": " + seedStr + "\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarIntToString\"\n" +
            "  },\n" +
            "  \"129\": {\n" +
            "    \"inputs\": {\n" +
            "      \"string_a\": [\"115\", 0],\n" +
            "      \"string_b\": \"" + "_.spz" + "\",\n" +
            "      \"delimiter\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"StringConcatenate\"\n" +
            "  },\n" +
            "  \"sg88_11\": {\n" +
            "    \"inputs\": {\n" +
            "      \"vae_name\": \"" + tripoVae + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"VAELoader\"\n" +
            "  },\n" +
            "  \"sg88_16\": {\n" +
            "    \"inputs\": {\n" +
            "      \"unet_name\": \"" + tripoUnet + "\",\n" +
            "      \"weight_dtype\": \"" + "default" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UNETLoader\"\n" +
            "  },\n" +
            "  \"sg88_2\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"99\", 0],\n" +
            "      \"mask\": [\"sg88_35\", 0],\n" +
            "      \"erode_radius\": 1,\n" +
            "      \"size\": 1024\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatPreprocessImage\"\n" +
            "  },\n" +
            "  \"sg88_22\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"sg88_2\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"sg88_24\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip_vision\": [\"sg88_7\", 0],\n" +
            "      \"vae\": [\"sg88_8\", 0],\n" +
            "      \"image\": [\"sg88_2\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatConditioning\"\n" +
            "  },\n" +
            "  \"sg88_35\": {\n" +
            "    \"inputs\": {\n" +
            "      \"on_false\": [\"sg88_95\", 0],\n" +
            "      \"on_true\": [\"sg88_85_81\", 0],\n" +
            "      \"switch\": true\n" +
            "    },\n" +
            "    \"class_type\": \"ComfySwitchNode\"\n" +
            "  },\n" +
            "  \"sg88_55\": {\n" +
            "    \"inputs\": {\n" +
            "      \"samples\": [\"sg88_6\", 0],\n" +
            "      \"vae\": [\"sg88_11\", 0],\n" +
            "      \"num_gaussians\": " + numGaussians.ToString(inv) + ",\n" +
            "      \"seed\": 790219963981395\n" +
            "    },\n" +
            "    \"class_type\": \"VAEDecodeTripoSplat\"\n" +
            "  },\n" +
            "  \"sg88_6\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"sg88_96\", 0],\n" +
            "      \"positive\": [\"sg88_24\", 0],\n" +
            "      \"negative\": [\"sg88_24\", 1],\n" +
            "      \"latent_image\": [\"sg88_24\", 2],\n" +
            "      \"seed\": " + seedStr + ",\n" +
            "      \"steps\": " + steps.ToString(inv) + ",\n" +
            "      \"cfg\": 3,\n" +
            "      \"sampler_name\": \"" + "dpmpp_2m" + "\",\n" +
            "      \"scheduler\": \"" + "simple" + "\",\n" +
            "      \"denoise\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"KSampler\"\n" +
            "  },\n" +
            "  \"sg88_7\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip_name\": \"" + tripoClip + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"CLIPVisionLoader\"\n" +
            "  },\n" +
            "  \"sg88_8\": {\n" +
            "    \"inputs\": {\n" +
            "      \"vae_name\": \"" + condVae + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"VAELoader\"\n" +
            "  },\n" +
            "  \"sg88_85_81\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"99\", 0],\n" +
            "      \"bg_removal_model\": [\"sg88_85_82\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"RemoveBackground\"\n" +
            "  },\n" +
            "  \"sg88_85_82\": {\n" +
            "    \"inputs\": {\n" +
            "      \"bg_removal_name\": \"" + bgRemoval + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadBackgroundRemovalModel\"\n" +
            "  },\n" +
            "  \"sg88_85_83\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mask\": [\"sg88_85_81\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"InvertMask\"\n" +
            "  },\n" +
            "  \"sg88_85_84\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"99\", 0],\n" +
            "      \"alpha\": [\"sg88_85_83\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"JoinImageWithAlpha\"\n" +
            "  },\n" +
            "  \"sg88_95\": {\n" +
            "    \"inputs\": {\n" +
            "      \"mask\": [\"99\", 1]\n" +
            "    },\n" +
            "    \"class_type\": \"InvertMask\"\n" +
            "  },\n" +
            "  \"sg88_96\": {\n" +
            "    \"inputs\": {\n" +
            "      \"on_false\": [\"sg88_16\", 0],\n" +
            "      \"on_true\": [\"sg88_97\", 0],\n" +
            "      \"switch\": true\n" +
            "    },\n" +
            "    \"class_type\": \"ComfySwitchNode\"\n" +
            "  },\n" +
            "  \"sg88_97\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"sg88_16\", 0],\n" +
            "      \"vae\": [\"sg88_11\", 0],\n" +
            "      \"octree_level\": 5,\n" +
            "      \"num_gaussians\": 16384,\n" +
            "      \"yaw\": 90,\n" +
            "      \"pitch\": 15,\n" +
            "      \"point_size\": 2\n" +
            "    },\n" +
            "    \"class_type\": \"TripoSplatSamplingPreview\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// Flux-Edit T-pose -> Pixal3D human -> UniRig mixamo rig + rig preview.
    /// Freeze source(s):
    /// - D:\PixalComfy\ComfyUI\user\default\workflows\JarJarV3_HumanAndRig.json
    /// UI nodes: 16 (source-muted/bypassed, kept active unless noted: 9, 29).
    /// API nodes: 27.
    /// Graph nodes:
    /// 17: Pixal3DExportGLB
    /// 23: Pixal3DModelLoader
    /// 24: Pixal3DImageTo3D
    /// 35: UniRigPreviewRiggedMesh
    /// 37: UniRigAutoRig
    /// 38: UniRigLoadModel
    /// 41: JarJarLoadMesh
    /// 42: UniRigSaveMesh
    /// 124: LoadImage
    /// 127: PreviewImage
    /// sg125_121: ReferenceLatent
    /// sg125_122: VAEEncode
    /// sg125_123: ReferenceLatent
    /// sg125_61: KSamplerSelect
    /// sg125_62: Flux2Scheduler
    /// sg125_63: CFGGuider
    /// sg125_64: SamplerCustomAdvanced
    /// sg125_65: VAEDecode
    /// sg125_66: EmptyFlux2LatentImage
    /// sg125_70: UNETLoader
    /// sg125_71: CLIPLoader
    /// sg125_72: VAELoader
    /// sg125_73: RandomNoise
    /// sg125_74: CLIPTextEncode
    /// sg125_80: ImageScaleToTotalPixels
    /// sg125_82: ConditioningZeroOut
    /// sg125_99: GetImageSize
    /// Sinks (no downstream consumers): 127 PreviewImage, 35 UniRigPreviewRiggedMesh, 42 UniRigSaveMesh.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// Flux-Edit subgraph 125 expanded inline as sg125_*. Instance carries no overrides; flux model/prompt values are the subgraph inner defaults.
    /// </summary>
    public static class JarJarHumanRigWorkflow
    {
        public const long DEFAULT_SEED = 662676578605371;
        public const int DEFAULT_STEPS = 12;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_INPUT_IMAGE = "pasted/image.png";
        public const string DEFAULT_PROMPT = "Show this character in T-Pose with palms flat out, on a black background.";
        public const string DEFAULT_PIPELINE = "1024_cascade";
        public const string DEFAULT_MODEL_REPO = "TencentARC/Pixal3D";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_SKELETON = "mixamo";
        public const int DEFAULT_TARGET_FACES = 50000;
        public const string DEFAULT_EXPORT_PREFIX = "/output/pixal3d";

        public static string CreateWorkflow(
            long seed = DEFAULT_SEED,
            int steps = DEFAULT_STEPS,
            int fluxSteps = DEFAULT_FLUX_STEPS,
            string inputImage = DEFAULT_INPUT_IMAGE,
            string prompt = DEFAULT_PROMPT,
            string pipelineType = DEFAULT_PIPELINE,
            string modelRepo = DEFAULT_MODEL_REPO,
            string fluxUnet = DEFAULT_FLUX_UNET,
            string fluxClip = DEFAULT_FLUX_CLIP,
            string fluxVae = DEFAULT_FLUX_VAE,
            string skeletonTemplate = DEFAULT_SKELETON,
            int targetFaceCount = DEFAULT_TARGET_FACES
        )
        {
            if (seed < 0)
                seed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (seed < 0) seed = -seed;
            if (steps < 1) steps = DEFAULT_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (targetFaceCount < 1) targetFaceCount = DEFAULT_TARGET_FACES;
            var inv = CultureInfo.InvariantCulture;
            string seedStr = (seed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string escInputImage = ExtendedJson.Escape(inputImage ?? string.Empty);
            string escPrompt = ExtendedJson.Escape(prompt ?? string.Empty);
            pipelineType = ExtendedJson.Escape(string.IsNullOrWhiteSpace(pipelineType) ? DEFAULT_PIPELINE : pipelineType);
            modelRepo = ExtendedJson.Escape(string.IsNullOrWhiteSpace(modelRepo) ? DEFAULT_MODEL_REPO : modelRepo);
            fluxUnet = ExtendedJson.Escape(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            fluxClip = ExtendedJson.Escape(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            fluxVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);
            skeletonTemplate = ExtendedJson.Escape(string.IsNullOrWhiteSpace(skeletonTemplate) ? DEFAULT_SKELETON : skeletonTemplate);
            return "{" + "\n" +
            "  \"17\": {\n" +
            "    \"inputs\": {\n" +
            "      \"pixal3d_result\": [\"24\", 0],\n" +
            "      \"decimation_target\": 50000,\n" +
            "      \"texture_size\": 2048,\n" +
            "      \"remesh\": false,\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_EXPORT_PREFIX) + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DExportGLB\"\n" +
            "  },\n" +
            "  \"23\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model_repo\": \"" + modelRepo + "\",\n" +
            "      \"hf_endpoint\": \"" + "https://huggingface.co" + "\",\n" +
            "      \"attention_backend\": \"" + "auto" + "\",\n" +
            "      \"vram_mode\": \"" + "dynamic_vram" + "\",\n" +
            "      \"download_if_missing\": true,\n" +
            "      \"load_moge\": true,\n" +
            "      \"load_rembg\": true,\n" +
            "      \"naf_mode\": \"" + "fallback_if_missing" + "\",\n" +
            "      \"naf_target_size\": \"" + "upstream" + "\",\n" +
            "      \"preload_naf\": true,\n" +
            "      \"force_reload\": true\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DModelLoader\"\n" +
            "  },\n" +
            "  \"24\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"23\", 0],\n" +
            "      \"image\": [\"sg125_65\", 0],\n" +
            "      \"seed\": " + seedStr + ",\n" +
            "      \"pipeline_type\": \"" + pipelineType + "\",\n" +
            "      \"background_mode\": \"" + "auto_remove" + "\",\n" +
            "      \"camera_mode\": \"" + "moge" + "\",\n" +
            "      \"manual_camera_angle_x\": 0.857556,\n" +
            "      \"manual_distance\": 2,\n" +
            "      \"mesh_scale\": 1,\n" +
            "      \"extend_pixel\": 0,\n" +
            "      \"camera_resolution\": 512,\n" +
            "      \"steps\": " + steps.ToString(inv) + ",\n" +
            "      \"guidance\": 7.5,\n" +
            "      \"texture_guidance\": 1,\n" +
            "      \"max_num_tokens\": 49152,\n" +
            "      \"force_offload\": false\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DImageTo3D\"\n" +
            "  },\n" +
            "  \"35\": {\n" +
            "    \"inputs\": {\n" +
            "      \"fbx_output_path\": [\"37\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigPreviewRiggedMesh\"\n" +
            "  },\n" +
            "  \"37\": {\n" +
            "    \"inputs\": {\n" +
            "      \"trimesh\": [\"41\", 0],\n" +
            "      \"model\": [\"38\", 0],\n" +
            "      \"skeleton_template\": \"" + skeletonTemplate + "\",\n" +
            "      \"fbx_name\": \"" + "" + "\",\n" +
            "      \"target_face_count\": " + targetFaceCount.ToString(inv) + ",\n" +
            "      \"debug\": true\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigAutoRig\"\n" +
            "  },\n" +
            "  \"38\": {\n" +
            "    \"inputs\": {\n" +
            "      \"precision\": \"" + "fp32" + "\",\n" +
            "      \"attn_backend\": \"" + "auto" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigLoadModel\"\n" +
            "  },\n" +
            "  \"41\": {\n" +
            "    \"inputs\": {\n" +
            "      \"file_path\": [\"17\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarLoadMesh\"\n" +
            "  },\n" +
            "  \"42\": {\n" +
            "    \"inputs\": {\n" +
            "      \"trimesh\": [\"41\", 0],\n" +
            "      \"file_path\": [\"37\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigSaveMesh\"\n" +
            "  },\n" +
            "  \"124\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + escInputImage + "\",\n" +
            "      \"upload\": \"" + "image" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  },\n" +
            "  \"127\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"sg125_65\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"sg125_121\": {\n" +
            "    \"inputs\": {\n" +
            "      \"conditioning\": [\"sg125_82\", 0],\n" +
            "      \"latent\": [\"sg125_122\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"ReferenceLatent\"\n" +
            "  },\n" +
            "  \"sg125_122\": {\n" +
            "    \"inputs\": {\n" +
            "      \"pixels\": [\"sg125_80\", 0],\n" +
            "      \"vae\": [\"sg125_72\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"VAEEncode\"\n" +
            "  },\n" +
            "  \"sg125_123\": {\n" +
            "    \"inputs\": {\n" +
            "      \"conditioning\": [\"sg125_74\", 0],\n" +
            "      \"latent\": [\"sg125_122\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"ReferenceLatent\"\n" +
            "  },\n" +
            "  \"sg125_61\": {\n" +
            "    \"inputs\": {\n" +
            "      \"sampler_name\": \"" + "euler" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"KSamplerSelect\"\n" +
            "  },\n" +
            "  \"sg125_62\": {\n" +
            "    \"inputs\": {\n" +
            "      \"steps\": " + fluxSteps.ToString(inv) + ",\n" +
            "      \"width\": [\"sg125_99\", 0],\n" +
            "      \"height\": [\"sg125_99\", 1]\n" +
            "    },\n" +
            "    \"class_type\": \"Flux2Scheduler\"\n" +
            "  },\n" +
            "  \"sg125_63\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"sg125_70\", 0],\n" +
            "      \"positive\": [\"sg125_123\", 0],\n" +
            "      \"negative\": [\"sg125_121\", 0],\n" +
            "      \"cfg\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"CFGGuider\"\n" +
            "  },\n" +
            "  \"sg125_64\": {\n" +
            "    \"inputs\": {\n" +
            "      \"noise\": [\"sg125_73\", 0],\n" +
            "      \"guider\": [\"sg125_63\", 0],\n" +
            "      \"sampler\": [\"sg125_61\", 0],\n" +
            "      \"sigmas\": [\"sg125_62\", 0],\n" +
            "      \"latent_image\": [\"sg125_66\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"SamplerCustomAdvanced\"\n" +
            "  },\n" +
            "  \"sg125_65\": {\n" +
            "    \"inputs\": {\n" +
            "      \"samples\": [\"sg125_64\", 0],\n" +
            "      \"vae\": [\"sg125_72\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"VAEDecode\"\n" +
            "  },\n" +
            "  \"sg125_66\": {\n" +
            "    \"inputs\": {\n" +
            "      \"width\": 1024,\n" +
            "      \"height\": 1024,\n" +
            "      \"batch_size\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"EmptyFlux2LatentImage\"\n" +
            "  },\n" +
            "  \"sg125_70\": {\n" +
            "    \"inputs\": {\n" +
            "      \"unet_name\": \"" + fluxUnet + "\",\n" +
            "      \"weight_dtype\": \"" + "default" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UNETLoader\"\n" +
            "  },\n" +
            "  \"sg125_71\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip_name\": \"" + fluxClip + "\",\n" +
            "      \"type\": \"" + "flux2" + "\",\n" +
            "      \"device\": \"" + "default" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"CLIPLoader\"\n" +
            "  },\n" +
            "  \"sg125_72\": {\n" +
            "    \"inputs\": {\n" +
            "      \"vae_name\": \"" + fluxVae + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"VAELoader\"\n" +
            "  },\n" +
            "  \"sg125_73\": {\n" +
            "    \"inputs\": {\n" +
            "      \"noise_seed\": " + seedStr + "\n" +
            "    },\n" +
            "    \"class_type\": \"RandomNoise\"\n" +
            "  },\n" +
            "  \"sg125_74\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip\": [\"sg125_71\", 0],\n" +
            "      \"text\": \"" + escPrompt + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"CLIPTextEncode\"\n" +
            "  },\n" +
            "  \"sg125_80\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"124\", 0],\n" +
            "      \"upscale_method\": \"" + "nearest-exact" + "\",\n" +
            "      \"megapixels\": 1,\n" +
            "      \"resolution_steps\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"ImageScaleToTotalPixels\"\n" +
            "  },\n" +
            "  \"sg125_82\": {\n" +
            "    \"inputs\": {\n" +
            "      \"conditioning\": [\"sg125_74\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"ConditioningZeroOut\"\n" +
            "  },\n" +
            "  \"sg125_99\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"sg125_80\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"GetImageSize\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// Pixal3D human with animation: image -> Pixal3D -> export -> UniRig rig -> apply Capoeira animation.
    /// Freeze source(s):
    /// - D:\PixalComfy\ComfyUI\user\default\workflows\JarJarV3_HumanAndRig22.json
    /// UI nodes: 16.
    /// API nodes: 12.
    /// Graph nodes:
    /// 17: Pixal3DExportGLB
    /// 23: Pixal3DModelLoader
    /// 35: UniRigPreviewRiggedMesh
    /// 37: UniRigAutoRig
    /// 38: UniRigLoadModel
    /// 41: JarJarLoadMesh
    /// 127: PreviewImage
    /// 129: LoadImage
    /// 138: UniRigApplyAnimation
    /// 140: StringConcatenate
    /// 142: Pixal3DImageTo3D
    /// 143: PreviewImage
    /// Sinks (no downstream consumers): 127 PreviewImage, 138 UniRigApplyAnimation, 143 PreviewImage, 35 UniRigPreviewRiggedMesh.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// </summary>
    public static class JarJarHumanRig22Workflow
    {
        public const long DEFAULT_SEED = 622145567953093;
        public const int DEFAULT_STEPS = 12;
        public const string DEFAULT_INPUT_IMAGE = "image-2.webp";
        public const string DEFAULT_PIPELINE = "1536_cascade";
        public const string DEFAULT_MODEL_REPO = "TencentARC/Pixal3D";
        public const string DEFAULT_ANIMATION = "Capoeira.fbx";
        public const int DEFAULT_TARGET_FACES = 210000;
        public const string DEFAULT_EXPORT_PREFIX = "JARTEST";

        public static string CreateWorkflow(
            long seed = DEFAULT_SEED,
            int steps = DEFAULT_STEPS,
            string inputImage = DEFAULT_INPUT_IMAGE,
            string pipelineType = DEFAULT_PIPELINE,
            string modelRepo = DEFAULT_MODEL_REPO,
            string animationFile = DEFAULT_ANIMATION,
            int targetFaceCount = DEFAULT_TARGET_FACES
        )
        {
            if (seed < 0)
                seed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (seed < 0) seed = -seed;
            if (steps < 1) steps = DEFAULT_STEPS;
            if (targetFaceCount < 1) targetFaceCount = DEFAULT_TARGET_FACES;
            var inv = CultureInfo.InvariantCulture;
            string seedStr = (seed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string escInputImage = ExtendedJson.Escape(inputImage ?? string.Empty);
            pipelineType = ExtendedJson.Escape(string.IsNullOrWhiteSpace(pipelineType) ? DEFAULT_PIPELINE : pipelineType);
            modelRepo = ExtendedJson.Escape(string.IsNullOrWhiteSpace(modelRepo) ? DEFAULT_MODEL_REPO : modelRepo);
            string escAnimationFile = ExtendedJson.Escape(animationFile ?? string.Empty);
            return "{" + "\n" +
            "  \"17\": {\n" +
            "    \"inputs\": {\n" +
            "      \"pixal3d_result\": [\"142\", 0],\n" +
            "      \"decimation_target\": 200000,\n" +
            "      \"texture_size\": 4096,\n" +
            "      \"remesh\": true,\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_EXPORT_PREFIX) + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DExportGLB\"\n" +
            "  },\n" +
            "  \"23\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model_repo\": \"" + modelRepo + "\",\n" +
            "      \"hf_endpoint\": \"" + "https://huggingface.co" + "\",\n" +
            "      \"attention_backend\": \"" + "flash_attn_3" + "\",\n" +
            "      \"vram_mode\": \"" + "hybrid_low_vram" + "\",\n" +
            "      \"download_if_missing\": true,\n" +
            "      \"load_moge\": true,\n" +
            "      \"load_rembg\": true,\n" +
            "      \"naf_mode\": \"" + "fallback_if_missing" + "\",\n" +
            "      \"naf_target_size\": \"" + "upstream" + "\",\n" +
            "      \"preload_naf\": true,\n" +
            "      \"force_reload\": false\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DModelLoader\"\n" +
            "  },\n" +
            "  \"35\": {\n" +
            "    \"inputs\": {\n" +
            "      \"fbx_output_path\": [\"37\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigPreviewRiggedMesh\"\n" +
            "  },\n" +
            "  \"37\": {\n" +
            "    \"inputs\": {\n" +
            "      \"trimesh\": [\"41\", 0],\n" +
            "      \"model\": [\"38\", 0],\n" +
            "      \"skeleton_template\": \"" + "mixamo" + "\",\n" +
            "      \"fbx_name\": \"" + "" + "\",\n" +
            "      \"target_face_count\": " + targetFaceCount.ToString(inv) + ",\n" +
            "      \"debug\": false\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigAutoRig\"\n" +
            "  },\n" +
            "  \"38\": {\n" +
            "    \"inputs\": {\n" +
            "      \"precision\": \"" + "fp16" + "\",\n" +
            "      \"attn_backend\": \"" + "flash_attn" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigLoadModel\"\n" +
            "  },\n" +
            "  \"41\": {\n" +
            "    \"inputs\": {\n" +
            "      \"file_path\": [\"17\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarLoadMesh\"\n" +
            "  },\n" +
            "  \"127\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"129\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"129\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + escInputImage + "\",\n" +
            "      \"upload\": \"" + "image" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  },\n" +
            "  \"138\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model_fbx_path\": [\"140\", 0],\n" +
            "      \"animation_type\": \"" + "mixamo" + "\",\n" +
            "      \"animation_file\": \"" + escAnimationFile + "\",\n" +
            "      \"output_name\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigApplyAnimation\"\n" +
            "  },\n" +
            "  \"140\": {\n" +
            "    \"inputs\": {\n" +
            "      \"string_a\": \"" + "C:\\\\Users\\\\meesa\\\\Desktop\\\\PixalComfy\\\\ComfyUI\\\\output\\\\" + "\",\n" +
            "      \"string_b\": [\"37\", 0],\n" +
            "      \"delimiter\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"StringConcatenate\"\n" +
            "  },\n" +
            "  \"142\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"23\", 0],\n" +
            "      \"image\": [\"129\", 0],\n" +
            "      \"seed\": " + seedStr + ",\n" +
            "      \"pipeline_type\": \"" + pipelineType + "\",\n" +
            "      \"background_mode\": \"" + "auto_remove" + "\",\n" +
            "      \"camera_mode\": \"" + "moge" + "\",\n" +
            "      \"manual_camera_angle_x\": 0.857556,\n" +
            "      \"manual_distance\": 2,\n" +
            "      \"mesh_scale\": 1,\n" +
            "      \"extend_pixel\": 0,\n" +
            "      \"camera_resolution\": 1024,\n" +
            "      \"steps\": " + steps.ToString(inv) + ",\n" +
            "      \"guidance\": 8,\n" +
            "      \"texture_guidance\": 2,\n" +
            "      \"max_num_tokens\": 49152,\n" +
            "      \"force_offload\": true\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DImageTo3D\"\n" +
            "  },\n" +
            "  \"143\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"142\", 1]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// Pixal3D human with animation plus a bypassed Flux-Edit prepass (kept active).
    /// Freeze source(s):
    /// - D:\PixalComfy\ComfyUI\user\default\workflows\JarJarV3_HumanAndRig22Faster.json
    /// UI nodes: 17 (source-muted/bypassed, kept active unless noted: 125).
    /// API nodes: 30.
    /// Graph nodes:
    /// 17: Pixal3DExportGLB
    /// 23: Pixal3DModelLoader
    /// 35: UniRigPreviewRiggedMesh
    /// 37: UniRigAutoRig
    /// 38: UniRigLoadModel
    /// 41: JarJarLoadMesh
    /// 127: PreviewImage
    /// 129: LoadImage
    /// 138: UniRigApplyAnimation
    /// 140: StringConcatenate
    /// 142: Pixal3DImageTo3D
    /// 143: PreviewImage
    /// sg125_121: ReferenceLatent
    /// sg125_122: VAEEncode
    /// sg125_123: ReferenceLatent
    /// sg125_137: UnloadModel
    /// sg125_61: KSamplerSelect
    /// sg125_62: Flux2Scheduler
    /// sg125_63: CFGGuider
    /// sg125_64: SamplerCustomAdvanced
    /// sg125_65: VAEDecode
    /// sg125_66: EmptyFlux2LatentImage
    /// sg125_70: UNETLoader
    /// sg125_71: CLIPLoader
    /// sg125_72: VAELoader
    /// sg125_73: RandomNoise
    /// sg125_74: CLIPTextEncode
    /// sg125_80: ImageScaleToTotalPixels
    /// sg125_82: ConditioningZeroOut
    /// sg125_99: GetImageSize
    /// Sinks (no downstream consumers): 127 PreviewImage, 138 UniRigApplyAnimation, 143 PreviewImage, 35 UniRigPreviewRiggedMesh.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// Flux-Edit subgraph instance 125 was bypassed in source; included here as an active prepass (LoadImage 129 -> sg125_* -> PreviewImage 127).
    /// Close to PipelineImageToRiggedWorkflow: export prefix /output/pixal3d here vs there, vram hybrid vs native, debug false vs true, Loader precision fp16 vs auto, pixal seed differs by one.
    /// </summary>
    public static class JarJarHumanRig22FasterWorkflow
    {
        public const long DEFAULT_SEED = 810010318060877;
        public const int DEFAULT_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_INPUT_IMAGE = "image-2.webp";
        public const string DEFAULT_PROMPT = "Create an unclothed lord of the rings elf with shimmering white skin and long golden hair, wearing only a tight loincloth, male, in this TPOSE T-POSE position, palms flat facing forward open, showing fingers spread with space, black background";
        public const string DEFAULT_PIPELINE = "1536_cascade";
        public const string DEFAULT_MODEL_REPO = "TencentARC/Pixal3D";
        public const string DEFAULT_ANIMATION = "Capoeira.fbx";
        public const int DEFAULT_TARGET_FACES = 200000;
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_EXPORT_PREFIX = "/output/pixal3d";

        public static string CreateWorkflow(
            long seed = DEFAULT_SEED,
            int steps = DEFAULT_STEPS,
            int fluxSteps = DEFAULT_FLUX_STEPS,
            string inputImage = DEFAULT_INPUT_IMAGE,
            string prompt = DEFAULT_PROMPT,
            string pipelineType = DEFAULT_PIPELINE,
            string modelRepo = DEFAULT_MODEL_REPO,
            string animationFile = DEFAULT_ANIMATION,
            int targetFaceCount = DEFAULT_TARGET_FACES,
            string fluxUnet = DEFAULT_FLUX_UNET,
            string fluxClip = DEFAULT_FLUX_CLIP,
            string fluxVae = DEFAULT_FLUX_VAE
        )
        {
            if (seed < 0)
                seed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (seed < 0) seed = -seed;
            if (steps < 1) steps = DEFAULT_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (targetFaceCount < 1) targetFaceCount = DEFAULT_TARGET_FACES;
            var inv = CultureInfo.InvariantCulture;
            string seedStr = (seed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string escInputImage = ExtendedJson.Escape(inputImage ?? string.Empty);
            string escPrompt = ExtendedJson.Escape(prompt ?? string.Empty);
            pipelineType = ExtendedJson.Escape(string.IsNullOrWhiteSpace(pipelineType) ? DEFAULT_PIPELINE : pipelineType);
            modelRepo = ExtendedJson.Escape(string.IsNullOrWhiteSpace(modelRepo) ? DEFAULT_MODEL_REPO : modelRepo);
            string escAnimationFile = ExtendedJson.Escape(animationFile ?? string.Empty);
            fluxUnet = ExtendedJson.Escape(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            fluxClip = ExtendedJson.Escape(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            fluxVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);
            return "{" + "\n" +
            "  \"17\": {\n" +
            "    \"inputs\": {\n" +
            "      \"pixal3d_result\": [\"142\", 0],\n" +
            "      \"decimation_target\": 200000,\n" +
            "      \"texture_size\": 4096,\n" +
            "      \"remesh\": true,\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_EXPORT_PREFIX) + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DExportGLB\"\n" +
            "  },\n" +
            "  \"23\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model_repo\": \"" + modelRepo + "\",\n" +
            "      \"hf_endpoint\": \"" + "https://huggingface.co" + "\",\n" +
            "      \"attention_backend\": \"" + "flash_attn_3" + "\",\n" +
            "      \"vram_mode\": \"" + "hybrid_low_vram" + "\",\n" +
            "      \"download_if_missing\": true,\n" +
            "      \"load_moge\": true,\n" +
            "      \"load_rembg\": true,\n" +
            "      \"naf_mode\": \"" + "fallback_if_missing" + "\",\n" +
            "      \"naf_target_size\": \"" + "upstream" + "\",\n" +
            "      \"preload_naf\": true,\n" +
            "      \"force_reload\": false\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DModelLoader\"\n" +
            "  },\n" +
            "  \"35\": {\n" +
            "    \"inputs\": {\n" +
            "      \"fbx_output_path\": [\"37\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigPreviewRiggedMesh\"\n" +
            "  },\n" +
            "  \"37\": {\n" +
            "    \"inputs\": {\n" +
            "      \"trimesh\": [\"41\", 0],\n" +
            "      \"model\": [\"38\", 0],\n" +
            "      \"skeleton_template\": \"" + "mixamo" + "\",\n" +
            "      \"fbx_name\": \"" + "" + "\",\n" +
            "      \"target_face_count\": " + targetFaceCount.ToString(inv) + ",\n" +
            "      \"debug\": false\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigAutoRig\"\n" +
            "  },\n" +
            "  \"38\": {\n" +
            "    \"inputs\": {\n" +
            "      \"precision\": \"" + "fp16" + "\",\n" +
            "      \"attn_backend\": \"" + "sdpa" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigLoadModel\"\n" +
            "  },\n" +
            "  \"41\": {\n" +
            "    \"inputs\": {\n" +
            "      \"file_path\": [\"17\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarLoadMesh\"\n" +
            "  },\n" +
            "  \"127\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"sg125_137\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"129\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + escInputImage + "\",\n" +
            "      \"upload\": \"" + "image" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  },\n" +
            "  \"138\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model_fbx_path\": [\"140\", 0],\n" +
            "      \"animation_type\": \"" + "mixamo" + "\",\n" +
            "      \"animation_file\": \"" + escAnimationFile + "\",\n" +
            "      \"output_name\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigApplyAnimation\"\n" +
            "  },\n" +
            "  \"140\": {\n" +
            "    \"inputs\": {\n" +
            "      \"string_a\": \"" + "C:\\\\Users\\\\meesa\\\\Desktop\\\\PixalComfy\\\\ComfyUI\\\\output\\\\" + "\",\n" +
            "      \"string_b\": [\"37\", 0],\n" +
            "      \"delimiter\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"StringConcatenate\"\n" +
            "  },\n" +
            "  \"142\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"23\", 0],\n" +
            "      \"image\": [\"sg125_137\", 0],\n" +
            "      \"seed\": " + seedStr + ",\n" +
            "      \"pipeline_type\": \"" + pipelineType + "\",\n" +
            "      \"background_mode\": \"" + "auto_remove" + "\",\n" +
            "      \"camera_mode\": \"" + "moge" + "\",\n" +
            "      \"manual_camera_angle_x\": 0.857556,\n" +
            "      \"manual_distance\": 2,\n" +
            "      \"mesh_scale\": 1,\n" +
            "      \"extend_pixel\": 0,\n" +
            "      \"camera_resolution\": 1536,\n" +
            "      \"steps\": " + steps.ToString(inv) + ",\n" +
            "      \"guidance\": 6,\n" +
            "      \"texture_guidance\": 2,\n" +
            "      \"max_num_tokens\": 49152,\n" +
            "      \"force_offload\": false\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DImageTo3D\"\n" +
            "  },\n" +
            "  \"143\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"142\", 1]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"sg125_121\": {\n" +
            "    \"inputs\": {\n" +
            "      \"conditioning\": [\"sg125_82\", 0],\n" +
            "      \"latent\": [\"sg125_122\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"ReferenceLatent\"\n" +
            "  },\n" +
            "  \"sg125_122\": {\n" +
            "    \"inputs\": {\n" +
            "      \"pixels\": [\"sg125_80\", 0],\n" +
            "      \"vae\": [\"sg125_72\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"VAEEncode\"\n" +
            "  },\n" +
            "  \"sg125_123\": {\n" +
            "    \"inputs\": {\n" +
            "      \"conditioning\": [\"sg125_74\", 0],\n" +
            "      \"latent\": [\"sg125_122\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"ReferenceLatent\"\n" +
            "  },\n" +
            "  \"sg125_137\": {\n" +
            "    \"inputs\": {\n" +
            "      \"value\": [\"sg125_65\", 0],\n" +
            "      \"model\": [\"sg125_70\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"UnloadModel\"\n" +
            "  },\n" +
            "  \"sg125_61\": {\n" +
            "    \"inputs\": {\n" +
            "      \"sampler_name\": \"" + "euler" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"KSamplerSelect\"\n" +
            "  },\n" +
            "  \"sg125_62\": {\n" +
            "    \"inputs\": {\n" +
            "      \"steps\": " + fluxSteps.ToString(inv) + ",\n" +
            "      \"width\": [\"sg125_99\", 0],\n" +
            "      \"height\": [\"sg125_99\", 1]\n" +
            "    },\n" +
            "    \"class_type\": \"Flux2Scheduler\"\n" +
            "  },\n" +
            "  \"sg125_63\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"sg125_70\", 0],\n" +
            "      \"positive\": [\"sg125_123\", 0],\n" +
            "      \"negative\": [\"sg125_121\", 0],\n" +
            "      \"cfg\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"CFGGuider\"\n" +
            "  },\n" +
            "  \"sg125_64\": {\n" +
            "    \"inputs\": {\n" +
            "      \"noise\": [\"sg125_73\", 0],\n" +
            "      \"guider\": [\"sg125_63\", 0],\n" +
            "      \"sampler\": [\"sg125_61\", 0],\n" +
            "      \"sigmas\": [\"sg125_62\", 0],\n" +
            "      \"latent_image\": [\"sg125_66\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"SamplerCustomAdvanced\"\n" +
            "  },\n" +
            "  \"sg125_65\": {\n" +
            "    \"inputs\": {\n" +
            "      \"samples\": [\"sg125_64\", 0],\n" +
            "      \"vae\": [\"sg125_72\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"VAEDecode\"\n" +
            "  },\n" +
            "  \"sg125_66\": {\n" +
            "    \"inputs\": {\n" +
            "      \"width\": 1024,\n" +
            "      \"height\": 1024,\n" +
            "      \"batch_size\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"EmptyFlux2LatentImage\"\n" +
            "  },\n" +
            "  \"sg125_70\": {\n" +
            "    \"inputs\": {\n" +
            "      \"unet_name\": \"" + fluxUnet + "\",\n" +
            "      \"weight_dtype\": \"" + "default" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UNETLoader\"\n" +
            "  },\n" +
            "  \"sg125_71\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip_name\": \"" + fluxClip + "\",\n" +
            "      \"type\": \"" + "flux2" + "\",\n" +
            "      \"device\": \"" + "default" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"CLIPLoader\"\n" +
            "  },\n" +
            "  \"sg125_72\": {\n" +
            "    \"inputs\": {\n" +
            "      \"vae_name\": \"" + fluxVae + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"VAELoader\"\n" +
            "  },\n" +
            "  \"sg125_73\": {\n" +
            "    \"inputs\": {\n" +
            "      \"noise_seed\": " + seedStr + "\n" +
            "    },\n" +
            "    \"class_type\": \"RandomNoise\"\n" +
            "  },\n" +
            "  \"sg125_74\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip\": [\"sg125_71\", 0],\n" +
            "      \"text\": \"" + escPrompt + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"CLIPTextEncode\"\n" +
            "  },\n" +
            "  \"sg125_80\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"129\", 0],\n" +
            "      \"upscale_method\": \"" + "nearest-exact" + "\",\n" +
            "      \"megapixels\": 1,\n" +
            "      \"resolution_steps\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"ImageScaleToTotalPixels\"\n" +
            "  },\n" +
            "  \"sg125_82\": {\n" +
            "    \"inputs\": {\n" +
            "      \"conditioning\": [\"sg125_74\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"ConditioningZeroOut\"\n" +
            "  },\n" +
            "  \"sg125_99\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"sg125_80\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"GetImageSize\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// Pixal3D goblin with UniRig rig (rig branch bypassed in source, included active).
    /// Freeze source(s):
    /// - D:\PixalComfy\ComfyUI\user\default\workflows\JarJarV3_HumanAndRigV5.json
    /// UI nodes: 14 (source-muted/bypassed, kept active unless noted: 35, 37, 38, 41, 43, 125, 127, 129).
    /// API nodes: 27.
    /// Graph nodes:
    /// 17: Pixal3DExportGLB
    /// 23: Pixal3DModelLoader
    /// 24: Pixal3DImageTo3D
    /// 35: UniRigPreviewRiggedMesh
    /// 37: UniRigAutoRig
    /// 38: UniRigLoadModel
    /// 41: JarJarLoadMesh
    /// 127: PreviewImage
    /// 129: LoadImage
    /// 139: LoadImage
    /// sg125_121: ReferenceLatent
    /// sg125_122: VAEEncode
    /// sg125_123: ReferenceLatent
    /// sg125_61: KSamplerSelect
    /// sg125_62: Flux2Scheduler
    /// sg125_63: CFGGuider
    /// sg125_64: SamplerCustomAdvanced
    /// sg125_65: VAEDecode
    /// sg125_66: EmptyFlux2LatentImage
    /// sg125_70: UNETLoader
    /// sg125_71: CLIPLoader
    /// sg125_72: VAELoader
    /// sg125_73: RandomNoise
    /// sg125_74: CLIPTextEncode
    /// sg125_80: ImageScaleToTotalPixels
    /// sg125_82: ConditioningZeroOut
    /// sg125_99: GetImageSize
    /// Sinks (no downstream consumers): 127 PreviewImage, 35 UniRigPreviewRiggedMesh.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// Live pixal input is LoadImage 139; bypassed LoadImage 129 feeds the bypassed Flux-Edit 125 chain (both kept active).
    /// DEFAULT_PROMPT is truncated in this listing only; the full frozen string is embedded.
    /// </summary>
    public static class JarJarHumanRigV5Workflow
    {
        public const long DEFAULT_SEED = 662676578605372;
        public const int DEFAULT_STEPS = 12;
        public const string DEFAULT_INPUT_IMAGE = "crane.png";
        public const string DEFAULT_PROMPT = "Create a green goblin character, in medieval realistic style, in the same pose. The character should be wearing torn ragged leather clothing, and be very dirty. The five fingered hands should be palms forward and fingers spread outward.";
        public const string DEFAULT_PIPELINE = "1024_cascade";
        public const string DEFAULT_MODEL_REPO = "TencentARC/Pixal3D";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_SKELETON = "mixamo";
        public const int DEFAULT_TARGET_FACES = 100000;
        public const string DEFAULT_EXPORT_PREFIX = "/output/pixal3d";

        public static string CreateWorkflow(
            long seed = DEFAULT_SEED,
            int steps = DEFAULT_STEPS,
            string inputImage = DEFAULT_INPUT_IMAGE,
            string prompt = DEFAULT_PROMPT,
            string pipelineType = DEFAULT_PIPELINE,
            string modelRepo = DEFAULT_MODEL_REPO,
            string fluxUnet = DEFAULT_FLUX_UNET,
            string fluxClip = DEFAULT_FLUX_CLIP,
            string fluxVae = DEFAULT_FLUX_VAE,
            string skeletonTemplate = DEFAULT_SKELETON,
            int targetFaceCount = DEFAULT_TARGET_FACES
        )
        {
            if (seed < 0)
                seed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (seed < 0) seed = -seed;
            if (steps < 1) steps = DEFAULT_STEPS;
            if (targetFaceCount < 1) targetFaceCount = DEFAULT_TARGET_FACES;
            var inv = CultureInfo.InvariantCulture;
            string seedStr = (seed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string escInputImage = ExtendedJson.Escape(inputImage ?? string.Empty);
            string escPrompt = ExtendedJson.Escape(prompt ?? string.Empty);
            pipelineType = ExtendedJson.Escape(string.IsNullOrWhiteSpace(pipelineType) ? DEFAULT_PIPELINE : pipelineType);
            modelRepo = ExtendedJson.Escape(string.IsNullOrWhiteSpace(modelRepo) ? DEFAULT_MODEL_REPO : modelRepo);
            fluxUnet = ExtendedJson.Escape(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            fluxClip = ExtendedJson.Escape(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            fluxVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);
            skeletonTemplate = ExtendedJson.Escape(string.IsNullOrWhiteSpace(skeletonTemplate) ? DEFAULT_SKELETON : skeletonTemplate);
            return "{" + "\n" +
            "  \"17\": {\n" +
            "    \"inputs\": {\n" +
            "      \"pixal3d_result\": [\"24\", 0],\n" +
            "      \"decimation_target\": 200000,\n" +
            "      \"texture_size\": 4096,\n" +
            "      \"remesh\": true,\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_EXPORT_PREFIX) + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DExportGLB\"\n" +
            "  },\n" +
            "  \"23\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model_repo\": \"" + modelRepo + "\",\n" +
            "      \"hf_endpoint\": \"" + "https://huggingface.co" + "\",\n" +
            "      \"attention_backend\": \"" + "auto" + "\",\n" +
            "      \"vram_mode\": \"" + "hybrid_low_vram" + "\",\n" +
            "      \"download_if_missing\": true,\n" +
            "      \"load_moge\": true,\n" +
            "      \"load_rembg\": true,\n" +
            "      \"naf_mode\": \"" + "fallback_if_missing" + "\",\n" +
            "      \"naf_target_size\": \"" + "upstream" + "\",\n" +
            "      \"preload_naf\": true,\n" +
            "      \"force_reload\": true\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DModelLoader\"\n" +
            "  },\n" +
            "  \"24\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"23\", 0],\n" +
            "      \"image\": [\"139\", 0],\n" +
            "      \"seed\": " + seedStr + ",\n" +
            "      \"pipeline_type\": \"" + pipelineType + "\",\n" +
            "      \"background_mode\": \"" + "auto_remove" + "\",\n" +
            "      \"camera_mode\": \"" + "moge" + "\",\n" +
            "      \"manual_camera_angle_x\": 0.1,\n" +
            "      \"manual_distance\": 2,\n" +
            "      \"mesh_scale\": 1,\n" +
            "      \"extend_pixel\": 2,\n" +
            "      \"camera_resolution\": 1024,\n" +
            "      \"steps\": " + steps.ToString(inv) + ",\n" +
            "      \"guidance\": 7.5,\n" +
            "      \"texture_guidance\": 3,\n" +
            "      \"max_num_tokens\": 12288,\n" +
            "      \"force_offload\": false\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DImageTo3D\"\n" +
            "  },\n" +
            "  \"35\": {\n" +
            "    \"inputs\": {\n" +
            "      \"fbx_output_path\": [\"37\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigPreviewRiggedMesh\"\n" +
            "  },\n" +
            "  \"37\": {\n" +
            "    \"inputs\": {\n" +
            "      \"trimesh\": [\"41\", 0],\n" +
            "      \"model\": [\"38\", 0],\n" +
            "      \"skeleton_template\": \"" + skeletonTemplate + "\",\n" +
            "      \"fbx_name\": \"" + "" + "\",\n" +
            "      \"target_face_count\": " + targetFaceCount.ToString(inv) + ",\n" +
            "      \"debug\": false\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigAutoRig\"\n" +
            "  },\n" +
            "  \"38\": {\n" +
            "    \"inputs\": {\n" +
            "      \"precision\": \"" + "fp16" + "\",\n" +
            "      \"attn_backend\": \"" + "auto" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigLoadModel\"\n" +
            "  },\n" +
            "  \"41\": {\n" +
            "    \"inputs\": {\n" +
            "      \"file_path\": [\"17\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarLoadMesh\"\n" +
            "  },\n" +
            "  \"127\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"sg125_65\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"129\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + "testbiped.png" + "\",\n" +
            "      \"upload\": \"" + "image" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  },\n" +
            "  \"139\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + escInputImage + "\",\n" +
            "      \"upload\": \"" + "image" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  },\n" +
            "  \"sg125_121\": {\n" +
            "    \"inputs\": {\n" +
            "      \"conditioning\": [\"sg125_82\", 0],\n" +
            "      \"latent\": [\"sg125_122\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"ReferenceLatent\"\n" +
            "  },\n" +
            "  \"sg125_122\": {\n" +
            "    \"inputs\": {\n" +
            "      \"pixels\": [\"sg125_80\", 0],\n" +
            "      \"vae\": [\"sg125_72\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"VAEEncode\"\n" +
            "  },\n" +
            "  \"sg125_123\": {\n" +
            "    \"inputs\": {\n" +
            "      \"conditioning\": [\"sg125_74\", 0],\n" +
            "      \"latent\": [\"sg125_122\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"ReferenceLatent\"\n" +
            "  },\n" +
            "  \"sg125_61\": {\n" +
            "    \"inputs\": {\n" +
            "      \"sampler_name\": \"" + "euler" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"KSamplerSelect\"\n" +
            "  },\n" +
            "  \"sg125_62\": {\n" +
            "    \"inputs\": {\n" +
            "      \"steps\": 4,\n" +
            "      \"width\": [\"sg125_99\", 0],\n" +
            "      \"height\": [\"sg125_99\", 1]\n" +
            "    },\n" +
            "    \"class_type\": \"Flux2Scheduler\"\n" +
            "  },\n" +
            "  \"sg125_63\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"sg125_70\", 0],\n" +
            "      \"positive\": [\"sg125_123\", 0],\n" +
            "      \"negative\": [\"sg125_121\", 0],\n" +
            "      \"cfg\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"CFGGuider\"\n" +
            "  },\n" +
            "  \"sg125_64\": {\n" +
            "    \"inputs\": {\n" +
            "      \"noise\": [\"sg125_73\", 0],\n" +
            "      \"guider\": [\"sg125_63\", 0],\n" +
            "      \"sampler\": [\"sg125_61\", 0],\n" +
            "      \"sigmas\": [\"sg125_62\", 0],\n" +
            "      \"latent_image\": [\"sg125_66\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"SamplerCustomAdvanced\"\n" +
            "  },\n" +
            "  \"sg125_65\": {\n" +
            "    \"inputs\": {\n" +
            "      \"samples\": [\"sg125_64\", 0],\n" +
            "      \"vae\": [\"sg125_72\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"VAEDecode\"\n" +
            "  },\n" +
            "  \"sg125_66\": {\n" +
            "    \"inputs\": {\n" +
            "      \"width\": 1024,\n" +
            "      \"height\": 1024,\n" +
            "      \"batch_size\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"EmptyFlux2LatentImage\"\n" +
            "  },\n" +
            "  \"sg125_70\": {\n" +
            "    \"inputs\": {\n" +
            "      \"unet_name\": \"" + fluxUnet + "\",\n" +
            "      \"weight_dtype\": \"" + "default" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UNETLoader\"\n" +
            "  },\n" +
            "  \"sg125_71\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip_name\": \"" + fluxClip + "\",\n" +
            "      \"type\": \"" + "flux2" + "\",\n" +
            "      \"device\": \"" + "default" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"CLIPLoader\"\n" +
            "  },\n" +
            "  \"sg125_72\": {\n" +
            "    \"inputs\": {\n" +
            "      \"vae_name\": \"" + fluxVae + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"VAELoader\"\n" +
            "  },\n" +
            "  \"sg125_73\": {\n" +
            "    \"inputs\": {\n" +
            "      \"noise_seed\": " + seedStr + "\n" +
            "    },\n" +
            "    \"class_type\": \"RandomNoise\"\n" +
            "  },\n" +
            "  \"sg125_74\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip\": [\"sg125_71\", 0],\n" +
            "      \"text\": \"" + escPrompt + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"CLIPTextEncode\"\n" +
            "  },\n" +
            "  \"sg125_80\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"129\", 0],\n" +
            "      \"upscale_method\": \"" + "nearest-exact" + "\",\n" +
            "      \"megapixels\": 1,\n" +
            "      \"resolution_steps\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"ImageScaleToTotalPixels\"\n" +
            "  },\n" +
            "  \"sg125_82\": {\n" +
            "    \"inputs\": {\n" +
            "      \"conditioning\": [\"sg125_74\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"ConditioningZeroOut\"\n" +
            "  },\n" +
            "  \"sg125_99\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"sg125_80\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"GetImageSize\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// Image-to-rigged pipeline: Pixal3D human -> export -> UniRig rig + animation, with Flux-Edit prepass.
    /// Freeze source(s):
    /// - D:\PixalComfy\ComfyUI\user\default\workflows\PIPELINE_IMAGETORIGGED.json
    /// UI nodes: 17 (source-muted/bypassed, kept active unless noted: 125).
    /// API nodes: 30.
    /// Graph nodes:
    /// 17: Pixal3DExportGLB
    /// 23: Pixal3DModelLoader
    /// 35: UniRigPreviewRiggedMesh
    /// 37: UniRigAutoRig
    /// 38: UniRigLoadModel
    /// 41: JarJarLoadMesh
    /// 127: PreviewImage
    /// 129: LoadImage
    /// 138: UniRigApplyAnimation
    /// 140: StringConcatenate
    /// 142: Pixal3DImageTo3D
    /// 143: PreviewImage
    /// sg125_121: ReferenceLatent
    /// sg125_122: VAEEncode
    /// sg125_123: ReferenceLatent
    /// sg125_137: UnloadModel
    /// sg125_61: KSamplerSelect
    /// sg125_62: Flux2Scheduler
    /// sg125_63: CFGGuider
    /// sg125_64: SamplerCustomAdvanced
    /// sg125_65: VAEDecode
    /// sg125_66: EmptyFlux2LatentImage
    /// sg125_70: UNETLoader
    /// sg125_71: CLIPLoader
    /// sg125_72: VAELoader
    /// sg125_73: RandomNoise
    /// sg125_74: CLIPTextEncode
    /// sg125_80: ImageScaleToTotalPixels
    /// sg125_82: ConditioningZeroOut
    /// sg125_99: GetImageSize
    /// Sinks (no downstream consumers): 127 PreviewImage, 138 UniRigApplyAnimation, 143 PreviewImage, 35 UniRigPreviewRiggedMesh.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// Flux-Edit subgraph instance 125 was bypassed in source; included here as an active prepass.
    /// Close to JarJarHumanRig22FasterWorkflow: pixal seed +1 here, export decimation 500000 vs 200000, Loader precision auto vs fp16 with sdpa attn in both, UniRig debug true vs false.
    /// DEFAULT_PROMPT is truncated in this listing only; the full frozen string is embedded.
    /// </summary>
    public static class PipelineImageToRiggedWorkflow
    {
        public const long DEFAULT_SEED = 810010318060878;
        public const int DEFAULT_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_INPUT_IMAGE = "pasted/image (38).png";
        public const string DEFAULT_PROMPT = "Create an unclothed lord of the rings elf with shimmering white skin and long golden hair, wearing only a tight loincloth, male, in this TPOSE T-POSE position, palms flat facing forward open, showing fingers spread with space, black background";
        public const string DEFAULT_PIPELINE = "1536_cascade";
        public const string DEFAULT_MODEL_REPO = "TencentARC/Pixal3D";
        public const string DEFAULT_ANIMATION = "Capoeira.fbx";
        public const int DEFAULT_TARGET_FACES = 500000;
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_EXPORT_PREFIX = "/output/pixal3d";

        public static string CreateWorkflow(
            long seed = DEFAULT_SEED,
            int steps = DEFAULT_STEPS,
            int fluxSteps = DEFAULT_FLUX_STEPS,
            string inputImage = DEFAULT_INPUT_IMAGE,
            string prompt = DEFAULT_PROMPT,
            string pipelineType = DEFAULT_PIPELINE,
            string modelRepo = DEFAULT_MODEL_REPO,
            string animationFile = DEFAULT_ANIMATION,
            int targetFaceCount = DEFAULT_TARGET_FACES,
            string fluxUnet = DEFAULT_FLUX_UNET,
            string fluxClip = DEFAULT_FLUX_CLIP,
            string fluxVae = DEFAULT_FLUX_VAE
        )
        {
            if (seed < 0)
                seed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (seed < 0) seed = -seed;
            if (steps < 1) steps = DEFAULT_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (targetFaceCount < 1) targetFaceCount = DEFAULT_TARGET_FACES;
            var inv = CultureInfo.InvariantCulture;
            string seedStr = (seed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string escInputImage = ExtendedJson.Escape(inputImage ?? string.Empty);
            string escPrompt = ExtendedJson.Escape(prompt ?? string.Empty);
            pipelineType = ExtendedJson.Escape(string.IsNullOrWhiteSpace(pipelineType) ? DEFAULT_PIPELINE : pipelineType);
            modelRepo = ExtendedJson.Escape(string.IsNullOrWhiteSpace(modelRepo) ? DEFAULT_MODEL_REPO : modelRepo);
            string escAnimationFile = ExtendedJson.Escape(animationFile ?? string.Empty);
            fluxUnet = ExtendedJson.Escape(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            fluxClip = ExtendedJson.Escape(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            fluxVae = ExtendedJson.Escape(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);
            return "{" + "\n" +
            "  \"17\": {\n" +
            "    \"inputs\": {\n" +
            "      \"pixal3d_result\": [\"142\", 0],\n" +
            "      \"decimation_target\": 500000,\n" +
            "      \"texture_size\": 4096,\n" +
            "      \"remesh\": true,\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_EXPORT_PREFIX) + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DExportGLB\"\n" +
            "  },\n" +
            "  \"23\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model_repo\": \"" + modelRepo + "\",\n" +
            "      \"hf_endpoint\": \"" + "https://huggingface.co" + "\",\n" +
            "      \"attention_backend\": \"" + "flash_attn_3" + "\",\n" +
            "      \"vram_mode\": \"" + "native_low_vram" + "\",\n" +
            "      \"download_if_missing\": true,\n" +
            "      \"load_moge\": true,\n" +
            "      \"load_rembg\": true,\n" +
            "      \"naf_mode\": \"" + "fallback_if_missing" + "\",\n" +
            "      \"naf_target_size\": \"" + "upstream" + "\",\n" +
            "      \"preload_naf\": true,\n" +
            "      \"force_reload\": false\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DModelLoader\"\n" +
            "  },\n" +
            "  \"35\": {\n" +
            "    \"inputs\": {\n" +
            "      \"fbx_output_path\": [\"37\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigPreviewRiggedMesh\"\n" +
            "  },\n" +
            "  \"37\": {\n" +
            "    \"inputs\": {\n" +
            "      \"trimesh\": [\"41\", 0],\n" +
            "      \"model\": [\"38\", 0],\n" +
            "      \"skeleton_template\": \"" + "mixamo" + "\",\n" +
            "      \"fbx_name\": \"" + "" + "\",\n" +
            "      \"target_face_count\": " + targetFaceCount.ToString(inv) + ",\n" +
            "      \"debug\": true\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigAutoRig\"\n" +
            "  },\n" +
            "  \"38\": {\n" +
            "    \"inputs\": {\n" +
            "      \"precision\": \"" + "auto" + "\",\n" +
            "      \"attn_backend\": \"" + "sdpa" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigLoadModel\"\n" +
            "  },\n" +
            "  \"41\": {\n" +
            "    \"inputs\": {\n" +
            "      \"file_path\": [\"17\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarLoadMesh\"\n" +
            "  },\n" +
            "  \"127\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"sg125_137\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"129\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + escInputImage + "\",\n" +
            "      \"upload\": \"" + "image" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  },\n" +
            "  \"138\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model_fbx_path\": [\"140\", 0],\n" +
            "      \"animation_type\": \"" + "mixamo" + "\",\n" +
            "      \"animation_file\": \"" + escAnimationFile + "\",\n" +
            "      \"output_name\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigApplyAnimation\"\n" +
            "  },\n" +
            "  \"140\": {\n" +
            "    \"inputs\": {\n" +
            "      \"string_a\": \"" + "C:\\\\Users\\\\meesa\\\\Desktop\\\\PixalComfy\\\\ComfyUI\\\\output\\\\" + "\",\n" +
            "      \"string_b\": [\"37\", 0],\n" +
            "      \"delimiter\": \"" + "" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"StringConcatenate\"\n" +
            "  },\n" +
            "  \"142\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"23\", 0],\n" +
            "      \"image\": [\"sg125_137\", 0],\n" +
            "      \"seed\": " + seedStr + ",\n" +
            "      \"pipeline_type\": \"" + pipelineType + "\",\n" +
            "      \"background_mode\": \"" + "auto_remove" + "\",\n" +
            "      \"camera_mode\": \"" + "moge" + "\",\n" +
            "      \"manual_camera_angle_x\": 0.857556,\n" +
            "      \"manual_distance\": 2,\n" +
            "      \"mesh_scale\": 1,\n" +
            "      \"extend_pixel\": 0,\n" +
            "      \"camera_resolution\": 1536,\n" +
            "      \"steps\": " + steps.ToString(inv) + ",\n" +
            "      \"guidance\": 6,\n" +
            "      \"texture_guidance\": 2,\n" +
            "      \"max_num_tokens\": 49152,\n" +
            "      \"force_offload\": false\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DImageTo3D\"\n" +
            "  },\n" +
            "  \"143\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"142\", 1]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"sg125_121\": {\n" +
            "    \"inputs\": {\n" +
            "      \"conditioning\": [\"sg125_82\", 0],\n" +
            "      \"latent\": [\"sg125_122\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"ReferenceLatent\"\n" +
            "  },\n" +
            "  \"sg125_122\": {\n" +
            "    \"inputs\": {\n" +
            "      \"pixels\": [\"sg125_80\", 0],\n" +
            "      \"vae\": [\"sg125_72\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"VAEEncode\"\n" +
            "  },\n" +
            "  \"sg125_123\": {\n" +
            "    \"inputs\": {\n" +
            "      \"conditioning\": [\"sg125_74\", 0],\n" +
            "      \"latent\": [\"sg125_122\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"ReferenceLatent\"\n" +
            "  },\n" +
            "  \"sg125_137\": {\n" +
            "    \"inputs\": {\n" +
            "      \"value\": [\"sg125_65\", 0],\n" +
            "      \"model\": [\"sg125_70\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"UnloadModel\"\n" +
            "  },\n" +
            "  \"sg125_61\": {\n" +
            "    \"inputs\": {\n" +
            "      \"sampler_name\": \"" + "euler" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"KSamplerSelect\"\n" +
            "  },\n" +
            "  \"sg125_62\": {\n" +
            "    \"inputs\": {\n" +
            "      \"steps\": " + fluxSteps.ToString(inv) + ",\n" +
            "      \"width\": [\"sg125_99\", 0],\n" +
            "      \"height\": [\"sg125_99\", 1]\n" +
            "    },\n" +
            "    \"class_type\": \"Flux2Scheduler\"\n" +
            "  },\n" +
            "  \"sg125_63\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"sg125_70\", 0],\n" +
            "      \"positive\": [\"sg125_123\", 0],\n" +
            "      \"negative\": [\"sg125_121\", 0],\n" +
            "      \"cfg\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"CFGGuider\"\n" +
            "  },\n" +
            "  \"sg125_64\": {\n" +
            "    \"inputs\": {\n" +
            "      \"noise\": [\"sg125_73\", 0],\n" +
            "      \"guider\": [\"sg125_63\", 0],\n" +
            "      \"sampler\": [\"sg125_61\", 0],\n" +
            "      \"sigmas\": [\"sg125_62\", 0],\n" +
            "      \"latent_image\": [\"sg125_66\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"SamplerCustomAdvanced\"\n" +
            "  },\n" +
            "  \"sg125_65\": {\n" +
            "    \"inputs\": {\n" +
            "      \"samples\": [\"sg125_64\", 0],\n" +
            "      \"vae\": [\"sg125_72\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"VAEDecode\"\n" +
            "  },\n" +
            "  \"sg125_66\": {\n" +
            "    \"inputs\": {\n" +
            "      \"width\": 1024,\n" +
            "      \"height\": 1024,\n" +
            "      \"batch_size\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"EmptyFlux2LatentImage\"\n" +
            "  },\n" +
            "  \"sg125_70\": {\n" +
            "    \"inputs\": {\n" +
            "      \"unet_name\": \"" + fluxUnet + "\",\n" +
            "      \"weight_dtype\": \"" + "default" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UNETLoader\"\n" +
            "  },\n" +
            "  \"sg125_71\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip_name\": \"" + fluxClip + "\",\n" +
            "      \"type\": \"" + "flux2" + "\",\n" +
            "      \"device\": \"" + "default" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"CLIPLoader\"\n" +
            "  },\n" +
            "  \"sg125_72\": {\n" +
            "    \"inputs\": {\n" +
            "      \"vae_name\": \"" + fluxVae + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"VAELoader\"\n" +
            "  },\n" +
            "  \"sg125_73\": {\n" +
            "    \"inputs\": {\n" +
            "      \"noise_seed\": " + seedStr + "\n" +
            "    },\n" +
            "    \"class_type\": \"RandomNoise\"\n" +
            "  },\n" +
            "  \"sg125_74\": {\n" +
            "    \"inputs\": {\n" +
            "      \"clip\": [\"sg125_71\", 0],\n" +
            "      \"text\": \"" + escPrompt + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"CLIPTextEncode\"\n" +
            "  },\n" +
            "  \"sg125_80\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"129\", 0],\n" +
            "      \"upscale_method\": \"" + "nearest-exact" + "\",\n" +
            "      \"megapixels\": 1,\n" +
            "      \"resolution_steps\": 1\n" +
            "    },\n" +
            "    \"class_type\": \"ImageScaleToTotalPixels\"\n" +
            "  },\n" +
            "  \"sg125_82\": {\n" +
            "    \"inputs\": {\n" +
            "      \"conditioning\": [\"sg125_74\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"ConditioningZeroOut\"\n" +
            "  },\n" +
            "  \"sg125_99\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"sg125_80\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"GetImageSize\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// UniRig-only humanoid rig: load mesh -> auto-rig -> preview.
    /// Freeze source(s):
    /// - D:\PixalComfy\ComfyUI\user\default\workflows\unirig_humanoid.json
    /// UI nodes: 5 (source-muted/bypassed, kept active unless noted: 27).
    /// API nodes: 5.
    /// Graph nodes:
    /// 10: UniRigPreviewRiggedMesh
    /// 27: UniRigLoadMesh
    /// 39: UniRigAutoRig
    /// 40: UniRigLoadModel
    /// 41: JarJarLoadMesh
    /// Sinks (no downstream consumers): 10 UniRigPreviewRiggedMesh, 27 UniRigLoadMesh.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// UniRigLoadMesh 27 was muted in source; included active.
    /// </summary>
    public static class UniRigHumanoidWorkflow
    {
        public const string DEFAULT_INPUT_MESH = "D:\\ComfyUIPortable\\ComfyUI\\output\\A_withered_zombie_—_grayish-green_necrotic_flesh_with_patches_sloughed_away_revealing_muscle_beneath_one_clouded_milky_eye_stiff_jerky_posture_implied_even_in_the_pose_dressed_in_the_moldering_remains_of_a_farmer's_shirt_and_trousers._00001.ply";
        public const string DEFAULT_SOURCE_FOLDER = "input";
        public const string DEFAULT_MESH_FILE = "3d/rigged_human.fbx";
        public const string DEFAULT_SKELETON = "mixamo";
        public const int DEFAULT_TARGET_FACES = 50000;

        public static string CreateWorkflow(
            string inputMesh = DEFAULT_INPUT_MESH,
            string sourceFolder = DEFAULT_SOURCE_FOLDER,
            string meshFile = DEFAULT_MESH_FILE,
            string skeletonTemplate = DEFAULT_SKELETON,
            int targetFaceCount = DEFAULT_TARGET_FACES
        )
        {
            if (targetFaceCount < 1) targetFaceCount = DEFAULT_TARGET_FACES;
            var inv = CultureInfo.InvariantCulture;
            string escInputMesh = ExtendedJson.Escape(inputMesh ?? string.Empty);
            string escSourceFolder = ExtendedJson.Escape(sourceFolder ?? string.Empty);
            string escMeshFile = ExtendedJson.Escape(meshFile ?? string.Empty);
            skeletonTemplate = ExtendedJson.Escape(string.IsNullOrWhiteSpace(skeletonTemplate) ? DEFAULT_SKELETON : skeletonTemplate);
            return "{" + "\n" +
            "  \"10\": {\n" +
            "    \"inputs\": {\n" +
            "      \"fbx_output_path\": [\"39\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigPreviewRiggedMesh\"\n" +
            "  },\n" +
            "  \"27\": {\n" +
            "    \"inputs\": {\n" +
            "      \"source_folder\": \"" + escSourceFolder + "\",\n" +
            "      \"file_path\": \"" + escMeshFile + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigLoadMesh\"\n" +
            "  },\n" +
            "  \"39\": {\n" +
            "    \"inputs\": {\n" +
            "      \"trimesh\": [\"41\", 0],\n" +
            "      \"model\": [\"40\", 0],\n" +
            "      \"skeleton_template\": \"" + skeletonTemplate + "\",\n" +
            "      \"fbx_name\": \"" + "" + "\",\n" +
            "      \"target_face_count\": " + targetFaceCount.ToString(inv) + ",\n" +
            "      \"debug\": false\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigAutoRig\"\n" +
            "  },\n" +
            "  \"40\": {\n" +
            "    \"inputs\": {\n" +
            "      \"precision\": \"" + "auto" + "\",\n" +
            "      \"attn_backend\": \"" + "auto" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"UniRigLoadModel\"\n" +
            "  },\n" +
            "  \"41\": {\n" +
            "    \"inputs\": {\n" +
            "      \"file_path\": \"" + escInputMesh + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"JarJarLoadMesh\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// High-detail Pixal3D still: 1536 cascade, 2048 camera, 30 steps.
    /// Freeze source(s):
    /// - D:\PixalComfy\ComfyUI\user\default\workflows\SPACE.json
    /// UI nodes: 7.
    /// API nodes: 5.
    /// Graph nodes:
    /// 150: Pixal3DExportGLB
    /// 152: PreviewImage
    /// 153: Pixal3DModelLoader
    /// 157: Pixal3DImageTo3D
    /// 162: LoadImage
    /// Sinks (no downstream consumers): 150 Pixal3DExportGLB, 152 PreviewImage.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// </summary>
    public static class SpacePixalWorkflow
    {
        public const long DEFAULT_SEED = 810010318060863;
        public const int DEFAULT_STEPS = 30;
        public const string DEFAULT_INPUT_IMAGE = "pasted/image (12).png";
        public const string DEFAULT_PIPELINE = "1536_cascade";
        public const int DEFAULT_CAMERA_RES = 2048;
        public const string DEFAULT_MODEL_REPO = "TencentARC/Pixal3D";
        public const string DEFAULT_EXPORT_PREFIX = "/output/pixal3d";

        public static string CreateWorkflow(
            long seed = DEFAULT_SEED,
            int steps = DEFAULT_STEPS,
            string inputImage = DEFAULT_INPUT_IMAGE,
            string pipelineType = DEFAULT_PIPELINE,
            int cameraResolution = DEFAULT_CAMERA_RES,
            string modelRepo = DEFAULT_MODEL_REPO
        )
        {
            if (seed < 0)
                seed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (seed < 0) seed = -seed;
            if (steps < 1) steps = DEFAULT_STEPS;
            if (cameraResolution < 1) cameraResolution = DEFAULT_CAMERA_RES;
            var inv = CultureInfo.InvariantCulture;
            string seedStr = (seed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            string escInputImage = ExtendedJson.Escape(inputImage ?? string.Empty);
            pipelineType = ExtendedJson.Escape(string.IsNullOrWhiteSpace(pipelineType) ? DEFAULT_PIPELINE : pipelineType);
            modelRepo = ExtendedJson.Escape(string.IsNullOrWhiteSpace(modelRepo) ? DEFAULT_MODEL_REPO : modelRepo);
            return "{" + "\n" +
            "  \"150\": {\n" +
            "    \"inputs\": {\n" +
            "      \"pixal3d_result\": [\"157\", 0],\n" +
            "      \"decimation_target\": 1000000,\n" +
            "      \"texture_size\": 4096,\n" +
            "      \"remesh\": true,\n" +
            "      \"filename_prefix\": \"" + ExtendedJson.Escape(DEFAULT_EXPORT_PREFIX) + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DExportGLB\"\n" +
            "  },\n" +
            "  \"152\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"157\", 1]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"153\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model_repo\": \"" + modelRepo + "\",\n" +
            "      \"hf_endpoint\": \"" + "https://huggingface.co" + "\",\n" +
            "      \"attention_backend\": \"" + "flash_attn_3" + "\",\n" +
            "      \"vram_mode\": \"" + "hybrid_low_vram" + "\",\n" +
            "      \"download_if_missing\": true,\n" +
            "      \"load_moge\": true,\n" +
            "      \"load_rembg\": true,\n" +
            "      \"naf_mode\": \"" + "fallback_if_missing" + "\",\n" +
            "      \"naf_target_size\": \"" + "upstream" + "\",\n" +
            "      \"preload_naf\": true,\n" +
            "      \"force_reload\": false\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DModelLoader\"\n" +
            "  },\n" +
            "  \"157\": {\n" +
            "    \"inputs\": {\n" +
            "      \"model\": [\"153\", 0],\n" +
            "      \"image\": [\"162\", 0],\n" +
            "      \"seed\": " + seedStr + ",\n" +
            "      \"pipeline_type\": \"" + pipelineType + "\",\n" +
            "      \"background_mode\": \"" + "auto_remove" + "\",\n" +
            "      \"camera_mode\": \"" + "moge" + "\",\n" +
            "      \"manual_camera_angle_x\": 0.857556,\n" +
            "      \"manual_distance\": 2,\n" +
            "      \"mesh_scale\": 1,\n" +
            "      \"extend_pixel\": 0,\n" +
            "      \"camera_resolution\": " + cameraResolution.ToString(inv) + ",\n" +
            "      \"steps\": " + steps.ToString(inv) + ",\n" +
            "      \"guidance\": 7.5,\n" +
            "      \"texture_guidance\": 1,\n" +
            "      \"max_num_tokens\": 199680,\n" +
            "      \"force_offload\": false\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DImageTo3D\"\n" +
            "  },\n" +
            "  \"162\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + escInputImage + "\",\n" +
            "      \"upload\": \"" + "image" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  }\n" +
            "}";
        }
    }

    /// <summary>
    /// Pixal3D GLB retexture job: reference image + GLB in, retextured GLB out, plus texture extraction previews.
    /// Freeze source(s):
    /// - D:\PixalComfy\ComfyUI\user\default\workflows\Job 6d934aea-7587-4787-b729-7b38f0abd783.json
    /// UI nodes: 7.
    /// API nodes: 6.
    /// Graph nodes:
    /// 142: LoadImage
    /// 143: Pixal3DRetextureGLB
    /// 145: Pixal3DExtractTexture
    /// 146: PreviewImage
    /// 147: Pixal3DExtractTexture
    /// 148: PreviewImage
    /// Sinks (no downstream consumers): 146 PreviewImage, 148 PreviewImage.
    /// Dropped UI-only nodes: MarkdownNote / PreviewAny / Preview3D / Pixal3DEnvironmentCheck; Reroutes resolved inline.
    /// </summary>
    public static class JobRetextureWorkflow
    {
        public const string DEFAULT_INPUT_IMAGE = "38711fdb-ff2e-445a-9236-37e2091db4a4.png";
        public const string DEFAULT_INPUT_GLB = "C:\\Users\\meesa\\Desktop\\PixalComfy\\ComfyUI\\output\\output_pixal3d_20260714_213137.glb";
        public const int DEFAULT_TEXTURE_SIZE = 4608;
        public const string DEFAULT_FILENAME_PREFIX = "pixal3d_retextured";

        public static string CreateWorkflow(
            string inputImage = DEFAULT_INPUT_IMAGE,
            string inputGlb = DEFAULT_INPUT_GLB,
            int textureSize = DEFAULT_TEXTURE_SIZE,
            string filenamePrefix = DEFAULT_FILENAME_PREFIX
        )
        {
            if (textureSize < 1) textureSize = DEFAULT_TEXTURE_SIZE;
            var inv = CultureInfo.InvariantCulture;
            string escInputImage = ExtendedJson.Escape(inputImage ?? string.Empty);
            string escInputGlb = ExtendedJson.Escape(inputGlb ?? string.Empty);
            string escFilenamePrefix = ExtendedJson.Escape(filenamePrefix ?? string.Empty);
            return "{" + "\n" +
            "  \"142\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": \"" + escInputImage + "\",\n" +
            "      \"upload\": \"" + "image" + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"LoadImage\"\n" +
            "  },\n" +
            "  \"143\": {\n" +
            "    \"inputs\": {\n" +
            "      \"image\": [\"142\", 0],\n" +
            "      \"glb_path\": \"" + escInputGlb + "\",\n" +
            "      \"texture_size\": " + textureSize.ToString(inv) + ",\n" +
            "      \"filename_prefix\": \"" + escFilenamePrefix + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DRetextureGLB\"\n" +
            "  },\n" +
            "  \"145\": {\n" +
            "    \"inputs\": {\n" +
            "      \"glb_path\": \"" + escInputGlb + "\"\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DExtractTexture\"\n" +
            "  },\n" +
            "  \"146\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"145\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  },\n" +
            "  \"147\": {\n" +
            "    \"inputs\": {\n" +
            "      \"glb_path\": [\"143\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"Pixal3DExtractTexture\"\n" +
            "  },\n" +
            "  \"148\": {\n" +
            "    \"inputs\": {\n" +
            "      \"images\": [\"147\", 0]\n" +
            "    },\n" +
            "    \"class_type\": \"PreviewImage\"\n" +
            "  }\n" +
            "}";
        }
    }
}
