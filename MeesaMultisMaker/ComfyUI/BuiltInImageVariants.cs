// Built-in image / Flux / seamless / upscale workflow variants.
//
// Each static class below embeds one distinct UI-format workflow from the
// ComfyUI installs as a flat API-format prompt ({"id":{"class_type":...,"inputs":{...}}}
// with [nodeId, outSlot] links), following the same interpolated verbatim-JSON
// pattern as Flux2KleinImage2ImageWorkflow (no file I/O; callers upload inputs
// first and pass the server-side filename in).
//
// Conventions:
// - Node ids are preserved from the source files. Expanded subgraph nodes use
//   "<instance>_<internal>" ids (e.g. "75_61"); nested subgraph nodes append
//   again (e.g. "92_84_118").
// - The Flux.2-Klein single-edit (7b34...) and dual-edit (65c2...) subgraphs are
//   expanded inline to their primitive nodes (reference-edit chain), because the
//   /prompt API only accepts real node types, never subgraph ids.
// - Reroute nodes are UI-only and are bypassed (downstream links point at the
//   real source). MarkdownNote nodes carry no executable graph and are dropped.
// - PreviewImage / PreviewAny / MaskPreview nodes are kept verbatim (previews only).
//   Two preview-only nodes could not be represented and are omitted (noted per class):
//   ImageCompare (compare_view has no stored value) and PreviewGaussianSplat
//   (viewport_state is unconnected; it is UI-interactive state).
// - Dead LoadImage nodes with no consumers (WTF BRO "0x000E_window.png",
//   PERFECT_SEAMLESS_TEXTURE_GENERATOR "Texture 0x03EC.png" x3 and its SAVER
//   variants x1 each) are omitted: a LoadImage whose file is
//   absent from the server input folder fails VALIDATE_INPUTS and would reject
//   the whole prompt. The disconnected JarJarRemoveBackground(151) present in
//   all four RapidFlux files is omitted for the same reason (required image
//   input unconnected).
// - The UI "control_after_generate" widgets (seed increment/randomize) are a
//   frontend queue behaviour and have no API representation; builders expose
//   seed params instead (nullable = random, or nullable = file default).
// - "upload": "image" on LoadImage is preserved verbatim (tolerated by the API).
// - Model Patch Seamless (mtb) "startStep" is stored as JSON true (=1) in three
//   of the seamless files (MCPTEST1 stores 0); builders expose an int param
//   defaulting to 1 with a note.
// - TRAINER.json stores one stale null widget (removed input) between inputcount
//   and images_path; the trainer builder drops it and aligns the 16 documented
//   MusubiFluxKleinLoraTrainer inputs by name.
// - Vanger_Plants was saved against a 4-input JarJarShadow; current JarJarShadow
//   takes 6 inputs, so the builder appends skew=0.0 / stretch=1.0 (node defaults).
//
// Family aliases (same graph, different prompts/files where noted):
// - FluxKleinSingleEditWorkflow: JarJarImageEdit.json + FluxKleinExample.json
//   (older subgraph rev: cfg 5, 0.5MP) + VRAK_PROMPT_TO_HUMANOID_IMAGE.json (same older rev).
// - FluxKlein2ImageEditWorkflow: FluxKlein2ImageEdit.json +
//   image_flux2_klein_image_edit_4b_distilled.json + KAYKAY.json (all empty
//   template widgets) + image_flux2_klein_image_edit_4b_distilled (2)ddd.json
//   (same 8-node dual-branch graph with populated prompts).
// - RapidFluxWorkflow: ComfyUI_00064_.json + MeesaJarJar_RapidUO_Flux.json +
//   RAPIDFLUX.json (identical 44-node graphs; seeds/filenames/composite offsets differ).
// - SeamlessTextureWorkflow: 05-seamless_texture.json + SEAMLESS.json +
//   seamless_texture_test.json (no RepeatImageBatch/ImageGrid preview pair) +
//   MCPTEST1.json (seamless-mtb family).

using System;
using System.Globalization;

namespace MeesaMultisMaker.ComfyUI
{
    /// <summary>
    /// Flux.2-Klein single-image reference edit (expanded 7b34 subgraph, 19 nodes).
    /// Source: JarJarImageEdit.json (3n). Aliases: FluxKleinExample.json and
    /// VRAK_PROMPT_TO_HUMANOID_IMAGE.json (identical 3n graph, older subgraph rev
    /// with cfg 5 / 0.5MP - reproducible via the cfg/megapixels params).
    /// Chain: LoadImage(76) -&gt; ImageScaleToTotalPixels(75_80, nearest-exact)
    /// -&gt; GetImageSize(75_99) + VAEEncode(75_122); CLIPTextEncode(75_74) feeds
    /// both ReferenceLatent(75_123, positive) and ConditioningZeroOut(75_82) -&gt;
    /// ReferenceLatent(75_121, negative); CFGGuider(75_63) + SamplerCustomAdvanced(75_64)
    /// + KSamplerSelect + Flux2Scheduler(75_62, fixed 4 steps) + RandomNoise(75_73);
    /// VAEDecode(75_65) -&gt; SaveImage(9). Sink: SaveImage.
    /// </summary>
    public static class FluxKleinSingleEditWorkflow
    {
        public const string DEFAULT_UNET = Flux2KleinWorkflow.DEFAULT_UNET;
        public const string DEFAULT_CLIP = Flux2KleinWorkflow.DEFAULT_CLIP;
        public const string DEFAULT_VAE = Flux2KleinWorkflow.DEFAULT_VAE;
        public const double KLEIN_CFG_DEFAULT = 1.0;
        public const double KLEIN_MP_DEFAULT = 1.0;
        public const string DEFAULT_PREFIX = "Flux2-Klein";

        public static string CreateWorkflow(string positivePrompt, string inputImageFilename = "", long? seed = null, double cfg = KLEIN_CFG_DEFAULT, double megapixels = KLEIN_MP_DEFAULT, string filenamePrefix = DEFAULT_PREFIX, string unetName = null, string clipName = null, string vaeName = null)
        {
            positivePrompt = EscapeJson(positivePrompt ?? string.Empty);
            inputImageFilename = EscapeJson(inputImageFilename ?? string.Empty);
            filenamePrefix = EscapeJson(filenamePrefix ?? string.Empty);
            unetName = EscapeJson(unetName ?? DEFAULT_UNET);
            clipName = EscapeJson(clipName ?? DEFAULT_CLIP);
            vaeName = EscapeJson(vaeName ?? DEFAULT_VAE);
            if (!seed.HasValue || seed.Value < 0)
                seed = new Random().Next();
            var inv = CultureInfo.InvariantCulture;
            string mpStr = megapixels.ToString("R", inv);
            string cfgStr = cfg.ToString("R", inv);

            return $@"{{
              ""9"": {{
              ""inputs"": {{
                ""images"": [""75_65"", 0],
                ""filename_prefix"": ""{filenamePrefix}""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""76"": {{
              ""inputs"": {{
                ""image"": ""{inputImageFilename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }},
              ""75_61"": {{
              ""inputs"": {{
                ""sampler_name"": ""euler""
              }},
              ""class_type"": ""KSamplerSelect""
            }},
              ""75_62"": {{
              ""inputs"": {{
                ""steps"": 4,
                ""width"": [""75_99"", 0],
                ""height"": [""75_99"", 1]
              }},
              ""class_type"": ""Flux2Scheduler""
            }},
              ""75_63"": {{
              ""inputs"": {{
                ""model"": [""75_70"", 0],
                ""positive"": [""75_123"", 0],
                ""negative"": [""75_121"", 0],
                ""cfg"": {cfgStr}
              }},
              ""class_type"": ""CFGGuider""
            }},
              ""75_64"": {{
              ""inputs"": {{
                ""noise"": [""75_73"", 0],
                ""guider"": [""75_63"", 0],
                ""sampler"": [""75_61"", 0],
                ""sigmas"": [""75_62"", 0],
                ""latent_image"": [""75_66"", 0]
              }},
              ""class_type"": ""SamplerCustomAdvanced""
            }},
              ""75_65"": {{
              ""inputs"": {{
                ""samples"": [""75_64"", 0],
                ""vae"": [""75_72"", 0]
              }},
              ""class_type"": ""VAEDecode""
            }},
              ""75_66"": {{
              ""inputs"": {{
                ""width"": [""75_99"", 0],
                ""height"": [""75_99"", 1],
                ""batch_size"": 1
              }},
              ""class_type"": ""EmptyFlux2LatentImage""
            }},
              ""75_70"": {{
              ""inputs"": {{
                ""unet_name"": ""{unetName}"",
                ""weight_dtype"": ""default""
              }},
              ""class_type"": ""UNETLoader""
            }},
              ""75_71"": {{
              ""inputs"": {{
                ""clip_name"": ""{clipName}"",
                ""type"": ""flux2"",
                ""device"": ""default""
              }},
              ""class_type"": ""CLIPLoader""
            }},
              ""75_72"": {{
              ""inputs"": {{
                ""vae_name"": ""{vaeName}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""75_73"": {{
              ""inputs"": {{
                ""noise_seed"": {seed.Value}
              }},
              ""class_type"": ""RandomNoise""
            }},
              ""75_74"": {{
              ""inputs"": {{
                ""clip"": [""75_71"", 0],
                ""text"": ""{positivePrompt}""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""75_80"": {{
              ""inputs"": {{
                ""image"": [""76"", 0],
                ""upscale_method"": ""nearest-exact"",
                ""megapixels"": {mpStr},
                ""resolution_steps"": 1
              }},
              ""class_type"": ""ImageScaleToTotalPixels""
            }},
              ""75_82"": {{
              ""inputs"": {{
                ""conditioning"": [""75_74"", 0]
              }},
              ""class_type"": ""ConditioningZeroOut""
            }},
              ""75_99"": {{
              ""inputs"": {{
                ""image"": [""75_80"", 0]
              }},
              ""class_type"": ""GetImageSize""
            }},
              ""75_121"": {{
              ""inputs"": {{
                ""conditioning"": [""75_82"", 0],
                ""latent"": [""75_122"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""75_122"": {{
              ""inputs"": {{
                ""pixels"": [""75_80"", 0],
                ""vae"": [""75_72"", 0]
              }},
              ""class_type"": ""VAEEncode""
            }},
              ""75_123"": {{
              ""inputs"": {{
                ""conditioning"": [""75_74"", 0],
                ""latent"": [""75_122"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Flux.2-Klein dual-image reference edit (expanded 65c2 subgraph, 24 nodes).
    /// Source: WhiskeyTest.json (4n; FLUX.2-Klein caps note: stylize image1 with
    /// the colours/texture from image2). Both references condition both the
    /// positive and the negative via chained Reference Conditioning blocks
    /// (92_112 on image, then 92_84 on image_1). Sinks: SaveImage(94).
    /// The instance pins noise_seed 250386553643653 (subgraph default is randomize).
    /// </summary>
    public static class FluxKleinDualEditWorkflow
    {
        public const string DEFAULT_UNET = Flux2KleinWorkflow.DEFAULT_UNET;
        public const string DEFAULT_CLIP = Flux2KleinWorkflow.DEFAULT_CLIP;
        public const string DEFAULT_VAE = Flux2KleinWorkflow.DEFAULT_VAE;
        public const double KLEIN_CFG_DEFAULT = 1.0;
        public const double KLEIN_MP_DEFAULT = 1.0;
        public const long DEFAULT_SEED = 250386553643653L;
        public const string DEFAULT_PREFIX = "Flux2-Klein";

        public static string CreateWorkflow(string positivePrompt, string inputImageFilename = "", string inputImage2Filename = "", long? seed = null, double cfg = KLEIN_CFG_DEFAULT, double megapixels = KLEIN_MP_DEFAULT, string filenamePrefix = DEFAULT_PREFIX, string unetName = null, string clipName = null, string vaeName = null)
        {
            positivePrompt = EscapeJson(positivePrompt ?? string.Empty);
            inputImageFilename = EscapeJson(inputImageFilename ?? string.Empty);
            inputImage2Filename = EscapeJson(inputImage2Filename ?? string.Empty);
            filenamePrefix = EscapeJson(filenamePrefix ?? string.Empty);
            unetName = EscapeJson(unetName ?? DEFAULT_UNET);
            clipName = EscapeJson(clipName ?? DEFAULT_CLIP);
            vaeName = EscapeJson(vaeName ?? DEFAULT_VAE);
            long editSeed = seed ?? DEFAULT_SEED;
            var inv = CultureInfo.InvariantCulture;
            string mpStr = megapixels.ToString("R", inv);
            string cfgStr = cfg.ToString("R", inv);

            return $@"{{
              ""76"": {{
              ""inputs"": {{
                ""image"": ""{inputImageFilename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }},
              ""81"": {{
              ""inputs"": {{
                ""image"": ""{inputImage2Filename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }},
              ""94"": {{
              ""inputs"": {{
                ""images"": [""92_105"", 0],
                ""filename_prefix"": ""{filenamePrefix}""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""92_85"": {{
              ""inputs"": {{
                ""image"": [""81"", 0],
                ""upscale_method"": ""nearest-exact"",
                ""megapixels"": {mpStr},
                ""resolution_steps"": 1
              }},
              ""class_type"": ""ImageScaleToTotalPixels""
            }},
              ""92_86"": {{
              ""inputs"": {{
                ""conditioning"": [""92_109"", 0]
              }},
              ""class_type"": ""ConditioningZeroOut""
            }},
              ""92_101"": {{
              ""inputs"": {{
                ""sampler_name"": ""euler""
              }},
              ""class_type"": ""KSamplerSelect""
            }},
              ""92_102"": {{
              ""inputs"": {{
                ""steps"": 4,
                ""width"": [""92_114"", 0],
                ""height"": [""92_114"", 1]
              }},
              ""class_type"": ""Flux2Scheduler""
            }},
              ""92_103"": {{
              ""inputs"": {{
                ""model"": [""92_107"", 0],
                ""positive"": [""92_84_120"", 0],
                ""negative"": [""92_84_118"", 0],
                ""cfg"": {cfgStr}
              }},
              ""class_type"": ""CFGGuider""
            }},
              ""92_104"": {{
              ""inputs"": {{
                ""noise"": [""92_106"", 0],
                ""guider"": [""92_103"", 0],
                ""sampler"": [""92_101"", 0],
                ""sigmas"": [""92_102"", 0],
                ""latent_image"": [""92_113"", 0]
              }},
              ""class_type"": ""SamplerCustomAdvanced""
            }},
              ""92_105"": {{
              ""inputs"": {{
                ""samples"": [""92_104"", 0],
                ""vae"": [""92_110"", 0]
              }},
              ""class_type"": ""VAEDecode""
            }},
              ""92_106"": {{
              ""inputs"": {{
                ""noise_seed"": {editSeed}
              }},
              ""class_type"": ""RandomNoise""
            }},
              ""92_107"": {{
              ""inputs"": {{
                ""unet_name"": ""{unetName}"",
                ""weight_dtype"": ""default""
              }},
              ""class_type"": ""UNETLoader""
            }},
              ""92_108"": {{
              ""inputs"": {{
                ""clip_name"": ""{clipName}"",
                ""type"": ""flux2"",
                ""device"": ""default""
              }},
              ""class_type"": ""CLIPLoader""
            }},
              ""92_109"": {{
              ""inputs"": {{
                ""clip"": [""92_108"", 0],
                ""text"": ""{positivePrompt}""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""92_110"": {{
              ""inputs"": {{
                ""vae_name"": ""{vaeName}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""92_111"": {{
              ""inputs"": {{
                ""image"": [""76"", 0],
                ""upscale_method"": ""nearest-exact"",
                ""megapixels"": {mpStr},
                ""resolution_steps"": 1
              }},
              ""class_type"": ""ImageScaleToTotalPixels""
            }},
              ""92_113"": {{
              ""inputs"": {{
                ""width"": [""92_114"", 0],
                ""height"": [""92_114"", 1],
                ""batch_size"": 1
              }},
              ""class_type"": ""EmptyFlux2LatentImage""
            }},
              ""92_114"": {{
              ""inputs"": {{
                ""image"": [""92_111"", 0]
              }},
              ""class_type"": ""GetImageSize""
            }},
              ""92_84_118"": {{
              ""inputs"": {{
                ""conditioning"": [""92_112_115"", 0],
                ""latent"": [""92_84_119"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""92_84_119"": {{
              ""inputs"": {{
                ""pixels"": [""92_85"", 0],
                ""vae"": [""92_110"", 0]
              }},
              ""class_type"": ""VAEEncode""
            }},
              ""92_84_120"": {{
              ""inputs"": {{
                ""conditioning"": [""92_112_117"", 0],
                ""latent"": [""92_84_119"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""92_112_115"": {{
              ""inputs"": {{
                ""conditioning"": [""92_86"", 0],
                ""latent"": [""92_112_116"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""92_112_116"": {{
              ""inputs"": {{
                ""pixels"": [""92_111"", 0],
                ""vae"": [""92_110"", 0]
              }},
              ""class_type"": ""VAEEncode""
            }},
              ""92_112_117"": {{
              ""inputs"": {{
                ""conditioning"": [""92_109"", 0],
                ""latent"": [""92_112_116"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Flux.2-Klein dual-branch edit (expanded 7b34 single + 65c2 dual subgraphs, 42 nodes).
    /// Source: FluxKlein2ImageEdit.json (8n, empty template widgets). Aliases:
    /// image_flux2_klein_image_edit_4b_distilled.json and KAYKAY.json (same 8n graph,
    /// empty template widgets) + image_flux2_klein_image_edit_4b_distilled (2)ddd.json
    /// (same graph with populated prompts). Branch A (75_*): single-image reference
    /// edit of inputImage1 -&gt; SaveImage(9). Branch B (92_*): dual-image reference
    /// edit of inputImage1 + inputImage2 -&gt; SaveImage(94). Sinks: both SaveImages.
    /// </summary>
    public static class FluxKlein2ImageEditWorkflow
    {
        public const string DEFAULT_UNET = Flux2KleinWorkflow.DEFAULT_UNET;
        public const string DEFAULT_CLIP = Flux2KleinWorkflow.DEFAULT_CLIP;
        public const string DEFAULT_VAE = Flux2KleinWorkflow.DEFAULT_VAE;
        public const double KLEIN_CFG_DEFAULT = 1.0;
        public const double KLEIN_MP_DEFAULT = 1.0;
        public const string DEFAULT_PREFIX = "Flux2-Klein";

        public static string CreateWorkflow(string positiveSingle, string positiveDual = null, string inputImage1Filename = "", string inputImage2Filename = "", long? seedSingle = null, long? seedDual = null, double cfg = KLEIN_CFG_DEFAULT, double megapixels = KLEIN_MP_DEFAULT, string singlePrefix = DEFAULT_PREFIX, string dualPrefix = DEFAULT_PREFIX, string unetName = null, string clipName = null, string vaeName = null)
        {
            string dualRaw = positiveDual ?? positiveSingle;
            positiveSingle = EscapeJson(positiveSingle ?? string.Empty);
            positiveDual = EscapeJson(dualRaw ?? string.Empty);
            inputImage1Filename = EscapeJson(inputImage1Filename ?? string.Empty);
            inputImage2Filename = EscapeJson(inputImage2Filename ?? string.Empty);
            singlePrefix = EscapeJson(singlePrefix ?? string.Empty);
            dualPrefix = EscapeJson(dualPrefix ?? string.Empty);
            unetName = EscapeJson(unetName ?? DEFAULT_UNET);
            clipName = EscapeJson(clipName ?? DEFAULT_CLIP);
            vaeName = EscapeJson(vaeName ?? DEFAULT_VAE);
            if (!seedSingle.HasValue || seedSingle.Value < 0)
                seedSingle = new Random().Next();
            if (!seedDual.HasValue || seedDual.Value < 0)
                seedDual = new Random().Next();
            var inv = CultureInfo.InvariantCulture;
            string mpStr = megapixels.ToString("R", inv);
            string cfgStr = cfg.ToString("R", inv);

            return $@"{{
              ""9"": {{
              ""inputs"": {{
                ""images"": [""75_65"", 0],
                ""filename_prefix"": ""{singlePrefix}""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""76"": {{
              ""inputs"": {{
                ""image"": ""{inputImage1Filename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }},
              ""81"": {{
              ""inputs"": {{
                ""image"": ""{inputImage2Filename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }},
              ""94"": {{
              ""inputs"": {{
                ""images"": [""92_105"", 0],
                ""filename_prefix"": ""{dualPrefix}""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""75_61"": {{
              ""inputs"": {{
                ""sampler_name"": ""euler""
              }},
              ""class_type"": ""KSamplerSelect""
            }},
              ""75_62"": {{
              ""inputs"": {{
                ""steps"": 4,
                ""width"": [""75_99"", 0],
                ""height"": [""75_99"", 1]
              }},
              ""class_type"": ""Flux2Scheduler""
            }},
              ""75_63"": {{
              ""inputs"": {{
                ""model"": [""75_70"", 0],
                ""positive"": [""75_123"", 0],
                ""negative"": [""75_121"", 0],
                ""cfg"": {cfgStr}
              }},
              ""class_type"": ""CFGGuider""
            }},
              ""75_64"": {{
              ""inputs"": {{
                ""noise"": [""75_73"", 0],
                ""guider"": [""75_63"", 0],
                ""sampler"": [""75_61"", 0],
                ""sigmas"": [""75_62"", 0],
                ""latent_image"": [""75_66"", 0]
              }},
              ""class_type"": ""SamplerCustomAdvanced""
            }},
              ""75_65"": {{
              ""inputs"": {{
                ""samples"": [""75_64"", 0],
                ""vae"": [""75_72"", 0]
              }},
              ""class_type"": ""VAEDecode""
            }},
              ""75_66"": {{
              ""inputs"": {{
                ""width"": [""75_99"", 0],
                ""height"": [""75_99"", 1],
                ""batch_size"": 1
              }},
              ""class_type"": ""EmptyFlux2LatentImage""
            }},
              ""75_70"": {{
              ""inputs"": {{
                ""unet_name"": ""{unetName}"",
                ""weight_dtype"": ""default""
              }},
              ""class_type"": ""UNETLoader""
            }},
              ""75_71"": {{
              ""inputs"": {{
                ""clip_name"": ""{clipName}"",
                ""type"": ""flux2"",
                ""device"": ""default""
              }},
              ""class_type"": ""CLIPLoader""
            }},
              ""75_72"": {{
              ""inputs"": {{
                ""vae_name"": ""{vaeName}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""75_73"": {{
              ""inputs"": {{
                ""noise_seed"": {seedSingle.Value}
              }},
              ""class_type"": ""RandomNoise""
            }},
              ""75_74"": {{
              ""inputs"": {{
                ""clip"": [""75_71"", 0],
                ""text"": ""{positiveSingle}""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""75_80"": {{
              ""inputs"": {{
                ""image"": [""76"", 0],
                ""upscale_method"": ""nearest-exact"",
                ""megapixels"": {mpStr},
                ""resolution_steps"": 1
              }},
              ""class_type"": ""ImageScaleToTotalPixels""
            }},
              ""75_82"": {{
              ""inputs"": {{
                ""conditioning"": [""75_74"", 0]
              }},
              ""class_type"": ""ConditioningZeroOut""
            }},
              ""75_99"": {{
              ""inputs"": {{
                ""image"": [""75_80"", 0]
              }},
              ""class_type"": ""GetImageSize""
            }},
              ""92_85"": {{
              ""inputs"": {{
                ""image"": [""81"", 0],
                ""upscale_method"": ""nearest-exact"",
                ""megapixels"": {mpStr},
                ""resolution_steps"": 1
              }},
              ""class_type"": ""ImageScaleToTotalPixels""
            }},
              ""92_86"": {{
              ""inputs"": {{
                ""conditioning"": [""92_109"", 0]
              }},
              ""class_type"": ""ConditioningZeroOut""
            }},
              ""75_121"": {{
              ""inputs"": {{
                ""conditioning"": [""75_82"", 0],
                ""latent"": [""75_122"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""75_122"": {{
              ""inputs"": {{
                ""pixels"": [""75_80"", 0],
                ""vae"": [""75_72"", 0]
              }},
              ""class_type"": ""VAEEncode""
            }},
              ""75_123"": {{
              ""inputs"": {{
                ""conditioning"": [""75_74"", 0],
                ""latent"": [""75_122"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""92_101"": {{
              ""inputs"": {{
                ""sampler_name"": ""euler""
              }},
              ""class_type"": ""KSamplerSelect""
            }},
              ""92_102"": {{
              ""inputs"": {{
                ""steps"": 4,
                ""width"": [""92_114"", 0],
                ""height"": [""92_114"", 1]
              }},
              ""class_type"": ""Flux2Scheduler""
            }},
              ""92_103"": {{
              ""inputs"": {{
                ""model"": [""92_107"", 0],
                ""positive"": [""92_84_120"", 0],
                ""negative"": [""92_84_118"", 0],
                ""cfg"": {cfgStr}
              }},
              ""class_type"": ""CFGGuider""
            }},
              ""92_104"": {{
              ""inputs"": {{
                ""noise"": [""92_106"", 0],
                ""guider"": [""92_103"", 0],
                ""sampler"": [""92_101"", 0],
                ""sigmas"": [""92_102"", 0],
                ""latent_image"": [""92_113"", 0]
              }},
              ""class_type"": ""SamplerCustomAdvanced""
            }},
              ""92_105"": {{
              ""inputs"": {{
                ""samples"": [""92_104"", 0],
                ""vae"": [""92_110"", 0]
              }},
              ""class_type"": ""VAEDecode""
            }},
              ""92_106"": {{
              ""inputs"": {{
                ""noise_seed"": {seedDual.Value}
              }},
              ""class_type"": ""RandomNoise""
            }},
              ""92_107"": {{
              ""inputs"": {{
                ""unet_name"": ""{unetName}"",
                ""weight_dtype"": ""default""
              }},
              ""class_type"": ""UNETLoader""
            }},
              ""92_108"": {{
              ""inputs"": {{
                ""clip_name"": ""{clipName}"",
                ""type"": ""flux2"",
                ""device"": ""default""
              }},
              ""class_type"": ""CLIPLoader""
            }},
              ""92_109"": {{
              ""inputs"": {{
                ""clip"": [""92_108"", 0],
                ""text"": ""{positiveDual}""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""92_110"": {{
              ""inputs"": {{
                ""vae_name"": ""{vaeName}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""92_111"": {{
              ""inputs"": {{
                ""image"": [""76"", 0],
                ""upscale_method"": ""nearest-exact"",
                ""megapixels"": {mpStr},
                ""resolution_steps"": 1
              }},
              ""class_type"": ""ImageScaleToTotalPixels""
            }},
              ""92_113"": {{
              ""inputs"": {{
                ""width"": [""92_114"", 0],
                ""height"": [""92_114"", 1],
                ""batch_size"": 1
              }},
              ""class_type"": ""EmptyFlux2LatentImage""
            }},
              ""92_114"": {{
              ""inputs"": {{
                ""image"": [""92_111"", 0]
              }},
              ""class_type"": ""GetImageSize""
            }},
              ""92_84_118"": {{
              ""inputs"": {{
                ""conditioning"": [""92_112_115"", 0],
                ""latent"": [""92_84_119"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""92_84_119"": {{
              ""inputs"": {{
                ""pixels"": [""92_85"", 0],
                ""vae"": [""92_110"", 0]
              }},
              ""class_type"": ""VAEEncode""
            }},
              ""92_84_120"": {{
              ""inputs"": {{
                ""conditioning"": [""92_112_117"", 0],
                ""latent"": [""92_84_119"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""92_112_115"": {{
              ""inputs"": {{
                ""conditioning"": [""92_86"", 0],
                ""latent"": [""92_112_116"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""92_112_116"": {{
              ""inputs"": {{
                ""pixels"": [""92_111"", 0],
                ""vae"": [""92_110"", 0]
              }},
              ""class_type"": ""VAEEncode""
            }},
              ""92_112_117"": {{
              ""inputs"": {{
                ""conditioning"": [""92_109"", 0],
                ""latent"": [""92_112_116"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Flux.2-Klein edit + resize/composite preview chain (expanded 7b34, 26 nodes).
    /// Source: NatExample.json (11n; file prompt "Change this to be HD 4k quality").
    /// The edit input is pre-scaled to 512px (133, scale-dimensions/nearest-exact);
    /// the result is scaled back to the original size via GetImageSize(129) (125)
    /// and joined with the source alpha (135). Sinks: SaveImage(9); the rest are previews.
    /// </summary>
    public static class NatExampleWorkflow
    {
        public const string DEFAULT_UNET = Flux2KleinWorkflow.DEFAULT_UNET;
        public const string DEFAULT_CLIP = Flux2KleinWorkflow.DEFAULT_CLIP;
        public const string DEFAULT_VAE = Flux2KleinWorkflow.DEFAULT_VAE;
        public const double KLEIN_CFG_DEFAULT = 1.0;
        public const double KLEIN_MP_DEFAULT = 1.0;
        public const string DEFAULT_PREFIX = "Flux2-Klein";

        public static string CreateWorkflow(string positivePrompt, string inputImageFilename = "", long? seed = null, double cfg = KLEIN_CFG_DEFAULT, double megapixels = KLEIN_MP_DEFAULT, string filenamePrefix = DEFAULT_PREFIX, string unetName = null, string clipName = null, string vaeName = null)
        {
            positivePrompt = EscapeJson(positivePrompt ?? string.Empty);
            inputImageFilename = EscapeJson(inputImageFilename ?? string.Empty);
            filenamePrefix = EscapeJson(filenamePrefix ?? string.Empty);
            unetName = EscapeJson(unetName ?? DEFAULT_UNET);
            clipName = EscapeJson(clipName ?? DEFAULT_CLIP);
            vaeName = EscapeJson(vaeName ?? DEFAULT_VAE);
            if (!seed.HasValue || seed.Value < 0)
                seed = new Random().Next();
            var inv = CultureInfo.InvariantCulture;
            string mpStr = megapixels.ToString("R", inv);
            string cfgStr = cfg.ToString("R", inv);

            return $@"{{
              ""9"": {{
              ""inputs"": {{
                ""images"": [""75_65"", 0],
                ""filename_prefix"": ""{filenamePrefix}""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""76"": {{
              ""inputs"": {{
                ""image"": ""{inputImageFilename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }},
              ""125"": {{
              ""inputs"": {{
                ""input"": [""75_65"", 0],
                ""resize_type.width"": [""129"", 0],
                ""resize_type.height"": [""129"", 1],
                ""resize_type.crop"": ""disabled"",
                ""resize_type"": ""scale dimensions"",
                ""scale_method"": ""nearest-exact""
              }},
              ""class_type"": ""ResizeImageMaskNode""
            }},
              ""129"": {{
              ""inputs"": {{
                ""image"": [""76"", 0]
              }},
              ""class_type"": ""GetImageSize""
            }},
              ""133"": {{
              ""inputs"": {{
                ""input"": [""76"", 0],
                ""resize_type.width"": 512,
                ""resize_type.height"": 512,
                ""resize_type.crop"": ""disabled"",
                ""resize_type"": ""scale dimensions"",
                ""scale_method"": ""nearest-exact""
              }},
              ""class_type"": ""ResizeImageMaskNode""
            }},
              ""134"": {{
              ""inputs"": {{
                ""images"": [""125"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""135"": {{
              ""inputs"": {{
                ""image"": [""125"", 0],
                ""alpha"": [""76"", 1]
              }},
              ""class_type"": ""JoinImageWithAlpha""
            }},
              ""136"": {{
              ""inputs"": {{
                ""images"": [""135"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""137"": {{
              ""inputs"": {{
                ""images"": [""76"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""75_61"": {{
              ""inputs"": {{
                ""sampler_name"": ""euler""
              }},
              ""class_type"": ""KSamplerSelect""
            }},
              ""75_62"": {{
              ""inputs"": {{
                ""steps"": 6,
                ""width"": [""75_99"", 0],
                ""height"": [""75_99"", 1]
              }},
              ""class_type"": ""Flux2Scheduler""
            }},
              ""75_63"": {{
              ""inputs"": {{
                ""model"": [""75_70"", 0],
                ""positive"": [""75_123"", 0],
                ""negative"": [""75_121"", 0],
                ""cfg"": {cfgStr}
              }},
              ""class_type"": ""CFGGuider""
            }},
              ""75_64"": {{
              ""inputs"": {{
                ""noise"": [""75_73"", 0],
                ""guider"": [""75_63"", 0],
                ""sampler"": [""75_61"", 0],
                ""sigmas"": [""75_62"", 0],
                ""latent_image"": [""75_66"", 0]
              }},
              ""class_type"": ""SamplerCustomAdvanced""
            }},
              ""75_65"": {{
              ""inputs"": {{
                ""samples"": [""75_64"", 0],
                ""vae"": [""75_72"", 0]
              }},
              ""class_type"": ""VAEDecode""
            }},
              ""75_66"": {{
              ""inputs"": {{
                ""width"": [""75_99"", 0],
                ""height"": [""75_99"", 1],
                ""batch_size"": 1
              }},
              ""class_type"": ""EmptyFlux2LatentImage""
            }},
              ""75_70"": {{
              ""inputs"": {{
                ""unet_name"": ""{unetName}"",
                ""weight_dtype"": ""default""
              }},
              ""class_type"": ""UNETLoader""
            }},
              ""75_71"": {{
              ""inputs"": {{
                ""clip_name"": ""{clipName}"",
                ""type"": ""flux2"",
                ""device"": ""default""
              }},
              ""class_type"": ""CLIPLoader""
            }},
              ""75_72"": {{
              ""inputs"": {{
                ""vae_name"": ""{vaeName}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""75_73"": {{
              ""inputs"": {{
                ""noise_seed"": {seed.Value}
              }},
              ""class_type"": ""RandomNoise""
            }},
              ""75_74"": {{
              ""inputs"": {{
                ""clip"": [""75_71"", 0],
                ""text"": ""{positivePrompt}""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""75_80"": {{
              ""inputs"": {{
                ""image"": [""133"", 0],
                ""upscale_method"": ""nearest-exact"",
                ""megapixels"": {mpStr},
                ""resolution_steps"": 1
              }},
              ""class_type"": ""ImageScaleToTotalPixels""
            }},
              ""75_82"": {{
              ""inputs"": {{
                ""conditioning"": [""75_74"", 0]
              }},
              ""class_type"": ""ConditioningZeroOut""
            }},
              ""75_99"": {{
              ""inputs"": {{
                ""image"": [""75_80"", 0]
              }},
              ""class_type"": ""GetImageSize""
            }},
              ""75_121"": {{
              ""inputs"": {{
                ""conditioning"": [""75_82"", 0],
                ""latent"": [""75_122"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""75_122"": {{
              ""inputs"": {{
                ""pixels"": [""75_80"", 0],
                ""vae"": [""75_72"", 0]
              }},
              ""class_type"": ""VAEEncode""
            }},
              ""75_123"": {{
              ""inputs"": {{
                ""conditioning"": [""75_74"", 0],
                ""latent"": [""75_122"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Flux.2-Klein edit + background composite preview (expanded 7b34, 31 nodes).
    /// Source: NatExample_2.json (16n). Extends NatExampleWorkflow: the edited
    /// sprite is split (140), masked (142) and composited at (x, y) over a second
    /// background image (138 &lt;- 139) for side-by-side preview (141).
    /// Sinks: SaveImage(9); the composite branch is preview-only, so
    /// backgroundImageFilename must name a valid server-side input file.
    /// </summary>
    public static class NatExample2CompositeWorkflow
    {
        public const string DEFAULT_UNET = Flux2KleinWorkflow.DEFAULT_UNET;
        public const string DEFAULT_CLIP = Flux2KleinWorkflow.DEFAULT_CLIP;
        public const string DEFAULT_VAE = Flux2KleinWorkflow.DEFAULT_VAE;
        public const double KLEIN_CFG_DEFAULT = 1.0;
        public const double KLEIN_MP_DEFAULT = 1.0;
        public const string DEFAULT_PREFIX = "Flux2-Klein";

        public static string CreateWorkflow(string positivePrompt, string inputImageFilename = "", string backgroundImageFilename = "", int compositeX = 180, int compositeY = 180, long? seed = null, double cfg = KLEIN_CFG_DEFAULT, double megapixels = KLEIN_MP_DEFAULT, string filenamePrefix = DEFAULT_PREFIX, string unetName = null, string clipName = null, string vaeName = null)
        {
            positivePrompt = EscapeJson(positivePrompt ?? string.Empty);
            inputImageFilename = EscapeJson(inputImageFilename ?? string.Empty);
            backgroundImageFilename = EscapeJson(backgroundImageFilename ?? string.Empty);
            filenamePrefix = EscapeJson(filenamePrefix ?? string.Empty);
            unetName = EscapeJson(unetName ?? DEFAULT_UNET);
            clipName = EscapeJson(clipName ?? DEFAULT_CLIP);
            vaeName = EscapeJson(vaeName ?? DEFAULT_VAE);
            if (!seed.HasValue || seed.Value < 0)
                seed = new Random().Next();
            var inv = CultureInfo.InvariantCulture;
            string mpStr = megapixels.ToString("R", inv);
            string cfgStr = cfg.ToString("R", inv);

            return $@"{{
              ""9"": {{
              ""inputs"": {{
                ""images"": [""75_65"", 0],
                ""filename_prefix"": ""{filenamePrefix}""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""76"": {{
              ""inputs"": {{
                ""image"": ""{inputImageFilename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }},
              ""125"": {{
              ""inputs"": {{
                ""input"": [""75_65"", 0],
                ""resize_type"": ""scale dimensions"",
                ""resize_type.width"": [""129"", 0],
                ""resize_type.height"": [""129"", 1],
                ""resize_type.crop"": ""disabled"",
                ""scale_method"": ""nearest-exact""
              }},
              ""class_type"": ""ResizeImageMaskNode""
            }},
              ""129"": {{
              ""inputs"": {{
                ""image"": [""76"", 0]
              }},
              ""class_type"": ""GetImageSize""
            }},
              ""133"": {{
              ""inputs"": {{
                ""input"": [""76"", 0],
                ""resize_type"": ""scale dimensions"",
                ""resize_type.width"": 512,
                ""resize_type.height"": 512,
                ""resize_type.crop"": ""disabled"",
                ""scale_method"": ""nearest-exact""
              }},
              ""class_type"": ""ResizeImageMaskNode""
            }},
              ""134"": {{
              ""inputs"": {{
                ""images"": [""125"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""135"": {{
              ""inputs"": {{
                ""image"": [""125"", 0],
                ""alpha"": [""76"", 1]
              }},
              ""class_type"": ""JoinImageWithAlpha""
            }},
              ""136"": {{
              ""inputs"": {{
                ""images"": [""135"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""137"": {{
              ""inputs"": {{
                ""images"": [""76"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""138"": {{
              ""inputs"": {{
                ""destination"": [""139"", 0],
                ""source"": [""136"", 0],
                ""mask"": [""142"", 0],
                ""x"": {compositeX},
                ""y"": {compositeY},
                ""resize_source"": false
              }},
              ""class_type"": ""ImageCompositeMasked""
            }},
              ""139"": {{
              ""inputs"": {{
                ""image"": ""{backgroundImageFilename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }},
              ""140"": {{
              ""inputs"": {{
                ""image"": [""136"", 0]
              }},
              ""class_type"": ""SplitImageWithAlpha""
            }},
              ""141"": {{
              ""inputs"": {{
                ""images"": [""138"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""142"": {{
              ""inputs"": {{
                ""mask"": [""140"", 1]
              }},
              ""class_type"": ""InvertMask""
            }},
              ""75_61"": {{
              ""inputs"": {{
                ""sampler_name"": ""euler""
              }},
              ""class_type"": ""KSamplerSelect""
            }},
              ""75_62"": {{
              ""inputs"": {{
                ""steps"": 6,
                ""width"": [""75_99"", 0],
                ""height"": [""75_99"", 1]
              }},
              ""class_type"": ""Flux2Scheduler""
            }},
              ""75_63"": {{
              ""inputs"": {{
                ""model"": [""75_70"", 0],
                ""positive"": [""75_123"", 0],
                ""negative"": [""75_121"", 0],
                ""cfg"": {cfgStr}
              }},
              ""class_type"": ""CFGGuider""
            }},
              ""75_64"": {{
              ""inputs"": {{
                ""noise"": [""75_73"", 0],
                ""guider"": [""75_63"", 0],
                ""sampler"": [""75_61"", 0],
                ""sigmas"": [""75_62"", 0],
                ""latent_image"": [""75_66"", 0]
              }},
              ""class_type"": ""SamplerCustomAdvanced""
            }},
              ""75_65"": {{
              ""inputs"": {{
                ""samples"": [""75_64"", 0],
                ""vae"": [""75_72"", 0]
              }},
              ""class_type"": ""VAEDecode""
            }},
              ""75_66"": {{
              ""inputs"": {{
                ""width"": [""75_99"", 0],
                ""height"": [""75_99"", 1],
                ""batch_size"": 1
              }},
              ""class_type"": ""EmptyFlux2LatentImage""
            }},
              ""75_70"": {{
              ""inputs"": {{
                ""unet_name"": ""{unetName}"",
                ""weight_dtype"": ""default""
              }},
              ""class_type"": ""UNETLoader""
            }},
              ""75_71"": {{
              ""inputs"": {{
                ""clip_name"": ""{clipName}"",
                ""type"": ""flux2"",
                ""device"": ""default""
              }},
              ""class_type"": ""CLIPLoader""
            }},
              ""75_72"": {{
              ""inputs"": {{
                ""vae_name"": ""{vaeName}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""75_73"": {{
              ""inputs"": {{
                ""noise_seed"": {seed.Value}
              }},
              ""class_type"": ""RandomNoise""
            }},
              ""75_74"": {{
              ""inputs"": {{
                ""clip"": [""75_71"", 0],
                ""text"": ""{positivePrompt}""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""75_80"": {{
              ""inputs"": {{
                ""image"": [""133"", 0],
                ""upscale_method"": ""nearest-exact"",
                ""megapixels"": {mpStr},
                ""resolution_steps"": 1
              }},
              ""class_type"": ""ImageScaleToTotalPixels""
            }},
              ""75_82"": {{
              ""inputs"": {{
                ""conditioning"": [""75_74"", 0]
              }},
              ""class_type"": ""ConditioningZeroOut""
            }},
              ""75_99"": {{
              ""inputs"": {{
                ""image"": [""75_80"", 0]
              }},
              ""class_type"": ""GetImageSize""
            }},
              ""75_121"": {{
              ""inputs"": {{
                ""conditioning"": [""75_82"", 0],
                ""latent"": [""75_122"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""75_122"": {{
              ""inputs"": {{
                ""pixels"": [""75_80"", 0],
                ""vae"": [""75_72"", 0]
              }},
              ""class_type"": ""VAEEncode""
            }},
              ""75_123"": {{
              ""inputs"": {{
                ""conditioning"": [""75_74"", 0],
                ""latent"": [""75_122"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// UO-item generate-fast chain (expanded 7b34, 42 nodes).
    /// Source: MeesaJarJar_UO_Item_Generate_Fast.json (27n). The edit input is
    /// pre-scaled to 512px (133); post chain: resize-to-source (125), alpha join (135),
    /// black-background removal (144, 0.1), pixelate (150), dither (152), film grain
    /// (154, randomize) -&gt; SaveImage(9). A second composite branch (138-142 over
    /// 512forest.png) is preview-only. Sinks: SaveImage(9).
    /// The file pins edit seed 43301611940731 (that subgraph copy is fixed).
    /// </summary>
    public static class UoItemGenerateFastWorkflow
    {
        public const string DEFAULT_UNET = Flux2KleinWorkflow.DEFAULT_UNET;
        public const string DEFAULT_CLIP = Flux2KleinWorkflow.DEFAULT_CLIP;
        public const string DEFAULT_VAE = Flux2KleinWorkflow.DEFAULT_VAE;
        public const double KLEIN_CFG_DEFAULT = 1.0;
        public const double KLEIN_MP_DEFAULT = 1.0;
        public const long DEFAULT_EDIT_SEED = 43301611940731L;
        public const string DEFAULT_PREFIX = "Flux2-Klein";

        public static string CreateWorkflow(string positivePrompt, string inputImageFilename = "", string backgroundImageFilename = "", long? seed = null, long? noiseSeed = null, double cfg = KLEIN_CFG_DEFAULT, double megapixels = KLEIN_MP_DEFAULT, int pixelSize = 1, string ditherType = "floyd_steinberg", double ditherStrength = 0.04, double noiseStrength = 0.02, double removeThreshold = 0.1, string filenamePrefix = DEFAULT_PREFIX, string unetName = null, string clipName = null, string vaeName = null)
        {
            positivePrompt = EscapeJson(positivePrompt ?? string.Empty);
            inputImageFilename = EscapeJson(inputImageFilename ?? string.Empty);
            backgroundImageFilename = EscapeJson(backgroundImageFilename ?? string.Empty);
            filenamePrefix = EscapeJson(filenamePrefix ?? string.Empty);
            ditherType = EscapeJson(ditherType ?? string.Empty);
            unetName = EscapeJson(unetName ?? DEFAULT_UNET);
            clipName = EscapeJson(clipName ?? DEFAULT_CLIP);
            vaeName = EscapeJson(vaeName ?? DEFAULT_VAE);
            long editSeed = seed ?? DEFAULT_EDIT_SEED;
            if (!noiseSeed.HasValue || noiseSeed.Value < 0)
                noiseSeed = new Random().Next();
            var inv = CultureInfo.InvariantCulture;
            string mpStr = megapixels.ToString("R", inv);
            string cfgStr = cfg.ToString("R", inv);
            string ditherStrengthStr = ditherStrength.ToString("R", inv);
            string noiseStrengthStr = noiseStrength.ToString("R", inv);
            string removeThresholdStr = removeThreshold.ToString("R", inv);

            return $@"{{
              ""9"": {{
              ""inputs"": {{
                ""images"": [""75_65"", 0],
                ""filename_prefix"": ""{filenamePrefix}""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""76"": {{
              ""inputs"": {{
                ""image"": ""{inputImageFilename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }},
              ""125"": {{
              ""inputs"": {{
                ""input"": [""75_65"", 0],
                ""resize_type"": ""scale dimensions"",
                ""resize_type.width"": [""129"", 0],
                ""resize_type.height"": [""129"", 1],
                ""resize_type.crop"": ""disabled"",
                ""scale_method"": ""nearest-exact""
              }},
              ""class_type"": ""ResizeImageMaskNode""
            }},
              ""129"": {{
              ""inputs"": {{
                ""image"": [""76"", 0]
              }},
              ""class_type"": ""GetImageSize""
            }},
              ""133"": {{
              ""inputs"": {{
                ""input"": [""76"", 0],
                ""resize_type"": ""scale dimensions"",
                ""resize_type.width"": 512,
                ""resize_type.height"": 512,
                ""resize_type.crop"": ""disabled"",
                ""scale_method"": ""nearest-exact""
              }},
              ""class_type"": ""ResizeImageMaskNode""
            }},
              ""134"": {{
              ""inputs"": {{
                ""images"": [""125"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""135"": {{
              ""inputs"": {{
                ""image"": [""125"", 0],
                ""alpha"": [""149"", 0]
              }},
              ""class_type"": ""JoinImageWithAlpha""
            }},
              ""136"": {{
              ""inputs"": {{
                ""images"": [""135"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""137"": {{
              ""inputs"": {{
                ""images"": [""76"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""138"": {{
              ""inputs"": {{
                ""destination"": [""139"", 0],
                ""source"": [""152"", 0],
                ""mask"": [""142"", 0],
                ""x"": 180,
                ""y"": 180,
                ""resize_source"": false
              }},
              ""class_type"": ""ImageCompositeMasked""
            }},
              ""139"": {{
              ""inputs"": {{
                ""image"": ""{backgroundImageFilename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }},
              ""140"": {{
              ""inputs"": {{
                ""image"": [""136"", 0]
              }},
              ""class_type"": ""SplitImageWithAlpha""
            }},
              ""141"": {{
              ""inputs"": {{
                ""images"": [""138"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""142"": {{
              ""inputs"": {{
                ""mask"": [""140"", 1]
              }},
              ""class_type"": ""InvertMask""
            }},
              ""143"": {{
              ""inputs"": {{
                ""bg_removal_model"": [""147"", 0],
                ""image"": [""125"", 0]
              }},
              ""class_type"": ""RemoveBackground""
            }},
              ""144"": {{
              ""inputs"": {{
                ""image"": [""125"", 0],
                ""threshold"": {removeThresholdStr}
              }},
              ""class_type"": ""JarJarRemoveBackground""
            }},
              ""145"": {{
              ""inputs"": {{
                ""images"": [""144"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""146"": {{
              ""inputs"": {{
                ""mask"": [""143"", 0]
              }},
              ""class_type"": ""MaskPreview""
            }},
              ""147"": {{
              ""inputs"": {{
                ""bg_removal_name"": ""birefnet.safetensors""
              }},
              ""class_type"": ""LoadBackgroundRemovalModel""
            }},
              ""149"": {{
              ""inputs"": {{
                ""mask"": [""146"", 0]
              }},
              ""class_type"": ""InvertMask""
            }},
              ""150"": {{
              ""inputs"": {{
                ""image"": [""136"", 0],
                ""pixel_size"": {pixelSize}
              }},
              ""class_type"": ""JarJarPixelate""
            }},
              ""151"": {{
              ""inputs"": {{
                ""images"": [""150"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""152"": {{
              ""inputs"": {{
                ""image"": [""154"", 0],
                ""dither_type"": ""{ditherType}"",
                ""strength"": {ditherStrengthStr}
              }},
              ""class_type"": ""JarJarDither""
            }},
              ""153"": {{
              ""inputs"": {{
                ""images"": [""152"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""154"": {{
              ""inputs"": {{
                ""image"": [""151"", 0],
                ""seed"": {noiseSeed.Value},
                ""strength"": {noiseStrengthStr}
              }},
              ""class_type"": ""ImageAddNoise""
            }},
              ""75_61"": {{
              ""inputs"": {{
                ""sampler_name"": ""euler""
              }},
              ""class_type"": ""KSamplerSelect""
            }},
              ""75_62"": {{
              ""inputs"": {{
                ""steps"": 6,
                ""width"": [""75_99"", 0],
                ""height"": [""75_99"", 1]
              }},
              ""class_type"": ""Flux2Scheduler""
            }},
              ""75_63"": {{
              ""inputs"": {{
                ""model"": [""75_70"", 0],
                ""positive"": [""75_123"", 0],
                ""negative"": [""75_121"", 0],
                ""cfg"": {cfgStr}
              }},
              ""class_type"": ""CFGGuider""
            }},
              ""75_64"": {{
              ""inputs"": {{
                ""noise"": [""75_73"", 0],
                ""guider"": [""75_63"", 0],
                ""sampler"": [""75_61"", 0],
                ""sigmas"": [""75_62"", 0],
                ""latent_image"": [""75_66"", 0]
              }},
              ""class_type"": ""SamplerCustomAdvanced""
            }},
              ""75_65"": {{
              ""inputs"": {{
                ""samples"": [""75_64"", 0],
                ""vae"": [""75_72"", 0]
              }},
              ""class_type"": ""VAEDecode""
            }},
              ""75_66"": {{
              ""inputs"": {{
                ""width"": [""75_99"", 0],
                ""height"": [""75_99"", 1],
                ""batch_size"": 1
              }},
              ""class_type"": ""EmptyFlux2LatentImage""
            }},
              ""75_70"": {{
              ""inputs"": {{
                ""unet_name"": ""{unetName}"",
                ""weight_dtype"": ""default""
              }},
              ""class_type"": ""UNETLoader""
            }},
              ""75_71"": {{
              ""inputs"": {{
                ""clip_name"": ""{clipName}"",
                ""type"": ""flux2"",
                ""device"": ""default""
              }},
              ""class_type"": ""CLIPLoader""
            }},
              ""75_72"": {{
              ""inputs"": {{
                ""vae_name"": ""{vaeName}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""75_73"": {{
              ""inputs"": {{
                ""noise_seed"": {editSeed}
              }},
              ""class_type"": ""RandomNoise""
            }},
              ""75_74"": {{
              ""inputs"": {{
                ""clip"": [""75_71"", 0],
                ""text"": ""{positivePrompt}""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""75_80"": {{
              ""inputs"": {{
                ""image"": [""133"", 0],
                ""upscale_method"": ""nearest-exact"",
                ""megapixels"": {mpStr},
                ""resolution_steps"": 1
              }},
              ""class_type"": ""ImageScaleToTotalPixels""
            }},
              ""75_82"": {{
              ""inputs"": {{
                ""conditioning"": [""75_74"", 0]
              }},
              ""class_type"": ""ConditioningZeroOut""
            }},
              ""75_99"": {{
              ""inputs"": {{
                ""image"": [""75_80"", 0]
              }},
              ""class_type"": ""GetImageSize""
            }},
              ""75_121"": {{
              ""inputs"": {{
                ""conditioning"": [""75_82"", 0],
                ""latent"": [""75_122"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""75_122"": {{
              ""inputs"": {{
                ""pixels"": [""75_80"", 0],
                ""vae"": [""75_72"", 0]
              }},
              ""class_type"": ""VAEEncode""
            }},
              ""75_123"": {{
              ""inputs"": {{
                ""conditioning"": [""75_74"", 0],
                ""latent"": [""75_122"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Flux.2-Klein edit + auto-segment pass (expanded 7b34, 27 nodes).
    /// Source: dfasdgda.json (11n; file prompt "Make weapons that fit these silhouettes,
    /// ultra realistic medieval, black background."). The edited image is background-removed
    /// (128/birefnet), alpha-joined (131) and auto-segmented into sprites
    /// (124 JarJarAutoSegmentImage: background black, threshold 1, min_area 32,
    /// padding 4, max_sprites 64, big-to-small, feather 1). Sinks: SaveImage(9).
    /// </summary>
    public static class DfAsDgDaAutosegmentWorkflow
    {
        public const string DEFAULT_UNET = Flux2KleinWorkflow.DEFAULT_UNET;
        public const string DEFAULT_CLIP = Flux2KleinWorkflow.DEFAULT_CLIP;
        public const string DEFAULT_VAE = Flux2KleinWorkflow.DEFAULT_VAE;
        public const double KLEIN_CFG_DEFAULT = 1.0;
        public const double KLEIN_MP_DEFAULT = 1.0;
        public const string DEFAULT_PREFIX = "Flux2-Klein";

        public static string CreateWorkflow(string positivePrompt, string inputImageFilename = "", long? seed = null, double cfg = KLEIN_CFG_DEFAULT, double megapixels = KLEIN_MP_DEFAULT, string filenamePrefix = DEFAULT_PREFIX, string unetName = null, string clipName = null, string vaeName = null)
        {
            positivePrompt = EscapeJson(positivePrompt ?? string.Empty);
            inputImageFilename = EscapeJson(inputImageFilename ?? string.Empty);
            filenamePrefix = EscapeJson(filenamePrefix ?? string.Empty);
            unetName = EscapeJson(unetName ?? DEFAULT_UNET);
            clipName = EscapeJson(clipName ?? DEFAULT_CLIP);
            vaeName = EscapeJson(vaeName ?? DEFAULT_VAE);
            if (!seed.HasValue || seed.Value < 0)
                seed = new Random().Next();
            var inv = CultureInfo.InvariantCulture;
            string mpStr = megapixels.ToString("R", inv);
            string cfgStr = cfg.ToString("R", inv);

            return $@"{{
              ""9"": {{
              ""inputs"": {{
                ""images"": [""75_65"", 0],
                ""filename_prefix"": ""{filenamePrefix}""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""76"": {{
              ""inputs"": {{
                ""image"": ""{inputImageFilename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }},
              ""124"": {{
              ""inputs"": {{
                ""image"": [""131"", 0],
                ""background"": ""black"",
                ""threshold"": 1,
                ""min_area"": 32,
                ""padding"": 4,
                ""max_sprites"": 64,
                ""sort_order"": ""big-to-small"",
                ""feather"": 1
              }},
              ""class_type"": ""JarJarAutoSegmentImage""
            }},
              ""125"": {{
              ""inputs"": {{
                ""images"": [""124"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""126"": {{
              ""inputs"": {{
                ""source"": [""124"", 2]
              }},
              ""class_type"": ""PreviewAny""
            }},
              ""127"": {{
              ""inputs"": {{
                ""mask"": [""124"", 1]
              }},
              ""class_type"": ""MaskPreview""
            }},
              ""128"": {{
              ""inputs"": {{
                ""bg_removal_model"": [""129"", 0],
                ""image"": [""9"", 0]
              }},
              ""class_type"": ""RemoveBackground""
            }},
              ""129"": {{
              ""inputs"": {{
                ""bg_removal_name"": ""birefnet.safetensors""
              }},
              ""class_type"": ""LoadBackgroundRemovalModel""
            }},
              ""131"": {{
              ""inputs"": {{
                ""image"": [""9"", 0],
                ""alpha"": [""128"", 0]
              }},
              ""class_type"": ""JoinImageWithAlpha""
            }},
              ""132"": {{
              ""inputs"": {{
                ""images"": [""9"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""75_61"": {{
              ""inputs"": {{
                ""sampler_name"": ""euler""
              }},
              ""class_type"": ""KSamplerSelect""
            }},
              ""75_62"": {{
              ""inputs"": {{
                ""steps"": 4,
                ""width"": [""75_99"", 0],
                ""height"": [""75_99"", 1]
              }},
              ""class_type"": ""Flux2Scheduler""
            }},
              ""75_63"": {{
              ""inputs"": {{
                ""model"": [""75_70"", 0],
                ""positive"": [""75_123"", 0],
                ""negative"": [""75_121"", 0],
                ""cfg"": {cfgStr}
              }},
              ""class_type"": ""CFGGuider""
            }},
              ""75_64"": {{
              ""inputs"": {{
                ""noise"": [""75_73"", 0],
                ""guider"": [""75_63"", 0],
                ""sampler"": [""75_61"", 0],
                ""sigmas"": [""75_62"", 0],
                ""latent_image"": [""75_66"", 0]
              }},
              ""class_type"": ""SamplerCustomAdvanced""
            }},
              ""75_65"": {{
              ""inputs"": {{
                ""samples"": [""75_64"", 0],
                ""vae"": [""75_72"", 0]
              }},
              ""class_type"": ""VAEDecode""
            }},
              ""75_66"": {{
              ""inputs"": {{
                ""width"": [""75_99"", 0],
                ""height"": [""75_99"", 1],
                ""batch_size"": 1
              }},
              ""class_type"": ""EmptyFlux2LatentImage""
            }},
              ""75_70"": {{
              ""inputs"": {{
                ""unet_name"": ""{unetName}"",
                ""weight_dtype"": ""default""
              }},
              ""class_type"": ""UNETLoader""
            }},
              ""75_71"": {{
              ""inputs"": {{
                ""clip_name"": ""{clipName}"",
                ""type"": ""flux2"",
                ""device"": ""default""
              }},
              ""class_type"": ""CLIPLoader""
            }},
              ""75_72"": {{
              ""inputs"": {{
                ""vae_name"": ""{vaeName}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""75_73"": {{
              ""inputs"": {{
                ""noise_seed"": {seed.Value}
              }},
              ""class_type"": ""RandomNoise""
            }},
              ""75_74"": {{
              ""inputs"": {{
                ""clip"": [""75_71"", 0],
                ""text"": ""{positivePrompt}""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""75_80"": {{
              ""inputs"": {{
                ""image"": [""76"", 0],
                ""upscale_method"": ""nearest-exact"",
                ""megapixels"": {mpStr},
                ""resolution_steps"": 1
              }},
              ""class_type"": ""ImageScaleToTotalPixels""
            }},
              ""75_82"": {{
              ""inputs"": {{
                ""conditioning"": [""75_74"", 0]
              }},
              ""class_type"": ""ConditioningZeroOut""
            }},
              ""75_99"": {{
              ""inputs"": {{
                ""image"": [""75_80"", 0]
              }},
              ""class_type"": ""GetImageSize""
            }},
              ""75_121"": {{
              ""inputs"": {{
                ""conditioning"": [""75_82"", 0],
                ""latent"": [""75_122"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""75_122"": {{
              ""inputs"": {{
                ""pixels"": [""75_80"", 0],
                ""vae"": [""75_72"", 0]
              }},
              ""class_type"": ""VAEEncode""
            }},
              ""75_123"": {{
              ""inputs"": {{
                ""conditioning"": [""75_74"", 0],
                ""latent"": [""75_122"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Flat Flux.2-Klein batch HD processor (31 nodes). Source: HD_PROCESS_UO.json.
    /// Reference-edit chain (125-141, fixed 4 steps / cfg 1.0, 0.25MP) over images
    /// streamed from JarJarMeesaLoadImagesFromFolder(145) starting at SeedNode(150);
    /// output is resized to the source size (142/bicubic), alpha-joined (149/173)
    /// and written twice via JarJarSaveImage: HD (146, output_hd/ + filename) and
    /// low-res (151, output_lowres/ + filename). Sinks: both JarJarSaveImages.
    /// </summary>
    public static class HdProcessUoWorkflow
    {
        public const string DEFAULT_UNET = Flux2KleinWorkflow.DEFAULT_UNET;
        public const string DEFAULT_CLIP = Flux2KleinWorkflow.DEFAULT_CLIP;
        public const string DEFAULT_VAE = Flux2KleinWorkflow.DEFAULT_VAE;
        public const double KLEIN_MP_DEFAULT = 0.25;

        public static string CreateWorkflow(string positivePrompt, string folderPath = "", int seedStart = 558, long noiseSeed = 1, double megapixels = KLEIN_MP_DEFAULT, int loadCap = 1, string hdPrefix = "output_hd/", string lowPrefix = "output_lowres/", string unetName = null, string clipName = null, string clipType = "flux2", string clipDevice = "default", string vaeName = null)
        {
            positivePrompt = EscapeJson(positivePrompt ?? string.Empty);
            folderPath = EscapeJson(folderPath ?? string.Empty);
            hdPrefix = EscapeJson(hdPrefix ?? string.Empty);
            lowPrefix = EscapeJson(lowPrefix ?? string.Empty);
            unetName = EscapeJson(unetName ?? DEFAULT_UNET);
            clipName = EscapeJson(clipName ?? DEFAULT_CLIP);
            clipType = EscapeJson(clipType ?? string.Empty);
            clipDevice = EscapeJson(clipDevice ?? string.Empty);
            vaeName = EscapeJson(vaeName ?? DEFAULT_VAE);
            var inv = CultureInfo.InvariantCulture;
            string mpStr = megapixels.ToString("R", inv);

            return $@"{{
              ""125"": {{
              ""inputs"": {{
                ""sampler_name"": ""euler""
              }},
              ""class_type"": ""KSamplerSelect""
            }},
              ""126"": {{
              ""inputs"": {{
                ""unet_name"": ""{unetName}"",
                ""weight_dtype"": ""default""
              }},
              ""class_type"": ""UNETLoader""
            }},
              ""127"": {{
              ""inputs"": {{
                ""clip_name"": ""{clipName}"",
                ""type"": ""{clipType}"",
                ""device"": ""{clipDevice}""
              }},
              ""class_type"": ""CLIPLoader""
            }},
              ""128"": {{
              ""inputs"": {{
                ""vae_name"": ""{vaeName}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""129"": {{
              ""inputs"": {{
                ""width"": [""135"", 0],
                ""height"": [""135"", 1],
                ""batch_size"": 1
              }},
              ""class_type"": ""EmptyFlux2LatentImage""
            }},
              ""130"": {{
              ""inputs"": {{
                ""image"": [""145"", 0],
                ""upscale_method"": ""nearest-exact"",
                ""megapixels"": {mpStr},
                ""resolution_steps"": 1
              }},
              ""class_type"": ""ImageScaleToTotalPixels""
            }},
              ""131"": {{
              ""inputs"": {{
                ""steps"": 4,
                ""width"": [""135"", 0],
                ""height"": [""135"", 1]
              }},
              ""class_type"": ""Flux2Scheduler""
            }},
              ""132"": {{
              ""inputs"": {{
                ""clip"": [""127"", 0],
                ""text"": ""{positivePrompt}""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""133"": {{
              ""inputs"": {{
                ""conditioning"": [""132"", 0]
              }},
              ""class_type"": ""ConditioningZeroOut""
            }},
              ""134"": {{
              ""inputs"": {{
                ""conditioning"": [""133"", 0],
                ""latent"": [""137"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""135"": {{
              ""inputs"": {{
                ""image"": [""130"", 0]
              }},
              ""class_type"": ""GetImageSize""
            }},
              ""136"": {{
              ""inputs"": {{
                ""conditioning"": [""132"", 0],
                ""latent"": [""137"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""137"": {{
              ""inputs"": {{
                ""pixels"": [""130"", 0],
                ""vae"": [""128"", 0]
              }},
              ""class_type"": ""VAEEncode""
            }},
              ""138"": {{
              ""inputs"": {{
                ""noise"": [""140"", 0],
                ""guider"": [""141"", 0],
                ""sampler"": [""125"", 0],
                ""sigmas"": [""131"", 0],
                ""latent_image"": [""129"", 0]
              }},
              ""class_type"": ""SamplerCustomAdvanced""
            }},
              ""139"": {{
              ""inputs"": {{
                ""samples"": [""138"", 0],
                ""vae"": [""128"", 0]
              }},
              ""class_type"": ""VAEDecode""
            }},
              ""140"": {{
              ""inputs"": {{
                ""noise_seed"": {noiseSeed}
              }},
              ""class_type"": ""RandomNoise""
            }},
              ""141"": {{
              ""inputs"": {{
                ""model"": [""126"", 0],
                ""positive"": [""136"", 0],
                ""negative"": [""134"", 0],
                ""cfg"": 1
              }},
              ""class_type"": ""CFGGuider""
            }},
              ""142"": {{
              ""inputs"": {{
                ""input"": [""139"", 0],
                ""resize_type"": ""scale dimensions"",
                ""resize_type.width"": [""143"", 0],
                ""resize_type.height"": [""143"", 1],
                ""resize_type.crop"": ""disabled"",
                ""scale_method"": ""bicubic""
              }},
              ""class_type"": ""ResizeImageMaskNode""
            }},
              ""143"": {{
              ""inputs"": {{
                ""image"": [""145"", 0]
              }},
              ""class_type"": ""GetImageSize""
            }},
              ""145"": {{
              ""inputs"": {{
                ""folder"": ""{folderPath}"",
                ""image_load_cap"": {loadCap},
                ""start_index"": [""150"", 0]
              }},
              ""class_type"": ""JarJarMeesaLoadImagesFromFolder""
            }},
              ""146"": {{
              ""inputs"": {{
                ""image"": [""149"", 0],
                ""filenames"": [""157"", 0]
              }},
              ""class_type"": ""JarJarSaveImage""
            }},
              ""147"": {{
              ""inputs"": {{
                ""source"": [""146"", 0]
              }},
              ""class_type"": ""PreviewAny""
            }},
              ""149"": {{
              ""inputs"": {{
                ""image"": [""142"", 0],
                ""alpha"": [""145"", 1]
              }},
              ""class_type"": ""JoinImageWithAlpha""
            }},
              ""150"": {{
              ""inputs"": {{
                ""seed"": {seedStart}
              }},
              ""class_type"": ""SeedNode""
            }},
              ""151"": {{
              ""inputs"": {{
                ""image"": [""173"", 0],
                ""filenames"": [""156"", 0]
              }},
              ""class_type"": ""JarJarSaveImage""
            }},
              ""153"": {{
              ""inputs"": {{
                ""source"": [""151"", 0]
              }},
              ""class_type"": ""PreviewAny""
            }},
              ""156"": {{
              ""inputs"": {{
                ""string_a"": ""{hdPrefix}"",
                ""string_b"": [""145"", 4],
                ""delimiter"": """"
              }},
              ""class_type"": ""StringConcatenate""
            }},
              ""157"": {{
              ""inputs"": {{
                ""string_a"": ""{lowPrefix}"",
                ""string_b"": [""145"", 4],
                ""delimiter"": """"
              }},
              ""class_type"": ""StringConcatenate""
            }},
              ""169"": {{
              ""inputs"": {{
                ""mask"": [""145"", 1]
              }},
              ""class_type"": ""MaskPreview""
            }},
              ""170"": {{
              ""inputs"": {{
                ""images"": [""145"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""173"": {{
              ""inputs"": {{
                ""image"": [""139"", 0],
                ""alpha"": [""145"", 1]
              }},
              ""class_type"": ""JoinImageWithAlpha""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Flat Flux.2-Klein batch brick-ifier (31 nodes, dead LoadImage(76) omitted).
    /// Source: WTF BRO.json. Reference-edit chain (124-139, fixed 4 steps / cfg 1.0,
    /// 0.05MP) over JarJarMeesaLoadImagesFromFolder(141) from seedStart(142); output
    /// is ImageScale-matched to the source size (153), alpha-joined (144), batched
    /// (158) and saved (156) with a "RedBrick" + filename + "_" prefix (160/161).
    /// Sinks: SaveImage(156).
    /// </summary>
    public static class WtfBroWorkflow
    {
        public const string DEFAULT_UNET = Flux2KleinWorkflow.DEFAULT_UNET;
        public const string DEFAULT_CLIP = Flux2KleinWorkflow.DEFAULT_CLIP;
        public const string DEFAULT_VAE = Flux2KleinWorkflow.DEFAULT_VAE;
        public const double KLEIN_MP_DEFAULT = 0.05;

        public static string CreateWorkflow(string positivePrompt, string folderPath = "", int seedStart = 0, long noiseSeed = 244298271001961L, double megapixels = KLEIN_MP_DEFAULT, int loadCap = 1, string namePrefix = "RedBrick", string unetName = null, string clipName = null, string clipType = "flux2", string clipDevice = "default", string vaeName = null)
        {
            positivePrompt = EscapeJson(positivePrompt ?? string.Empty);
            folderPath = EscapeJson(folderPath ?? string.Empty);
            namePrefix = EscapeJson(namePrefix ?? string.Empty);
            unetName = EscapeJson(unetName ?? DEFAULT_UNET);
            clipName = EscapeJson(clipName ?? DEFAULT_CLIP);
            clipType = EscapeJson(clipType ?? string.Empty);
            clipDevice = EscapeJson(clipDevice ?? string.Empty);
            vaeName = EscapeJson(vaeName ?? DEFAULT_VAE);
            var inv = CultureInfo.InvariantCulture;
            string mpStr = megapixels.ToString("R", inv);

            return $@"{{
              ""124"": {{
              ""inputs"": {{
                ""sampler_name"": ""euler""
              }},
              ""class_type"": ""KSamplerSelect""
            }},
              ""125"": {{
              ""inputs"": {{
                ""unet_name"": ""{unetName}"",
                ""weight_dtype"": ""default""
              }},
              ""class_type"": ""UNETLoader""
            }},
              ""126"": {{
              ""inputs"": {{
                ""clip_name"": ""{clipName}"",
                ""type"": ""{clipType}"",
                ""device"": ""{clipDevice}""
              }},
              ""class_type"": ""CLIPLoader""
            }},
              ""127"": {{
              ""inputs"": {{
                ""vae_name"": ""{vaeName}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""128"": {{
              ""inputs"": {{
                ""width"": [""133"", 0],
                ""height"": [""133"", 1],
                ""batch_size"": 1
              }},
              ""class_type"": ""EmptyFlux2LatentImage""
            }},
              ""129"": {{
              ""inputs"": {{
                ""steps"": 4,
                ""width"": [""133"", 0],
                ""height"": [""133"", 1]
              }},
              ""class_type"": ""Flux2Scheduler""
            }},
              ""130"": {{
              ""inputs"": {{
                ""clip"": [""126"", 0],
                ""text"": ""{positivePrompt}""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""131"": {{
              ""inputs"": {{
                ""conditioning"": [""130"", 0]
              }},
              ""class_type"": ""ConditioningZeroOut""
            }},
              ""132"": {{
              ""inputs"": {{
                ""conditioning"": [""131"", 0],
                ""latent"": [""135"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""133"": {{
              ""inputs"": {{
                ""image"": [""140"", 0]
              }},
              ""class_type"": ""GetImageSize""
            }},
              ""134"": {{
              ""inputs"": {{
                ""conditioning"": [""130"", 0],
                ""latent"": [""135"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""135"": {{
              ""inputs"": {{
                ""pixels"": [""140"", 0],
                ""vae"": [""127"", 0]
              }},
              ""class_type"": ""VAEEncode""
            }},
              ""136"": {{
              ""inputs"": {{
                ""noise"": [""138"", 0],
                ""guider"": [""139"", 0],
                ""sampler"": [""124"", 0],
                ""sigmas"": [""129"", 0],
                ""latent_image"": [""128"", 0]
              }},
              ""class_type"": ""SamplerCustomAdvanced""
            }},
              ""137"": {{
              ""inputs"": {{
                ""samples"": [""136"", 0],
                ""vae"": [""127"", 0]
              }},
              ""class_type"": ""VAEDecode""
            }},
              ""138"": {{
              ""inputs"": {{
                ""noise_seed"": {noiseSeed}
              }},
              ""class_type"": ""RandomNoise""
            }},
              ""139"": {{
              ""inputs"": {{
                ""model"": [""125"", 0],
                ""positive"": [""134"", 0],
                ""negative"": [""132"", 0],
                ""cfg"": 1
              }},
              ""class_type"": ""CFGGuider""
            }},
              ""140"": {{
              ""inputs"": {{
                ""image"": [""141"", 0],
                ""upscale_method"": ""nearest-exact"",
                ""megapixels"": {mpStr},
                ""resolution_steps"": 1
              }},
              ""class_type"": ""ImageScaleToTotalPixels""
            }},
              ""141"": {{
              ""inputs"": {{
                ""folder"": ""{folderPath}"",
                ""image_load_cap"": {loadCap},
                ""start_index"": [""142"", 0]
              }},
              ""class_type"": ""JarJarMeesaLoadImagesFromFolder""
            }},
              ""142"": {{
              ""inputs"": {{
                ""seed"": {seedStart}
              }},
              ""class_type"": ""SeedNode""
            }},
              ""143"": {{
              ""inputs"": {{
                ""images"": [""141"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""144"": {{
              ""inputs"": {{
                ""image"": [""153"", 0],
                ""alpha"": [""155"", 0]
              }},
              ""class_type"": ""JoinImageWithAlpha""
            }},
              ""145"": {{
              ""inputs"": {{
                ""images"": [""137"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""152"": {{
              ""inputs"": {{
                ""image"": [""141"", 0]
              }},
              ""class_type"": ""GetImageSize""
            }},
              ""153"": {{
              ""inputs"": {{
                ""image"": [""137"", 0],
                ""upscale_method"": ""nearest-exact"",
                ""width"": [""152"", 0],
                ""height"": [""152"", 1],
                ""crop"": ""disabled""
              }},
              ""class_type"": ""ImageScale""
            }},
              ""155"": {{
              ""inputs"": {{
                ""mask"": [""141"", 1]
              }},
              ""class_type"": ""MaskPreview""
            }},
              ""156"": {{
              ""inputs"": {{
                ""images"": [""158"", 0],
                ""filename_prefix"": [""161"", 0]
              }},
              ""class_type"": ""SaveImage""
            }},
              ""157"": {{
              ""inputs"": {{
                ""source"": [""152"", 2]
              }},
              ""class_type"": ""PreviewAny""
            }},
              ""158"": {{
              ""inputs"": {{
                ""image"": [""144"", 0],
                ""batch_index"": 0,
                ""length"": 1
              }},
              ""class_type"": ""ImageFromBatch""
            }},
              ""160"": {{
              ""inputs"": {{
                ""value"": ""{namePrefix}""
              }},
              ""class_type"": ""PrimitiveString""
            }},
              ""161"": {{
              ""inputs"": {{
                ""string_a"": [""160"", 0],
                ""string_b"": [""141"", 4],
                ""delimiter"": ""_""
              }},
              ""class_type"": ""StringConcatenate""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// FLUX.3-Klein image edit with layout prompt + LLM prompt upsampler (25 nodes).
    /// Source: adfaf.json. FLUX3 int8 DiT (1) + qwen3vl CLIP (2) + flux2 VAE (3);
    /// ReferenceMultiLatent(17) binds image + image2 as &lt;ref_image_0/1&gt; at 0.5MP;
    /// KSampler(7, 14 steps / cfg 1.0) -&gt; VAEDecode(8) -&gt; SaveImage(9) + ImageCompare(18, omitted).
    /// Prompt path: CreateBoundingBoxes(33, 1360x752 + earring box) -&gt;
    /// Flux3LayoutPrompt(49) -&gt; PreviewAny(52) -&gt; ComfySwitch(51, false: LLM-upsampled
    /// TextGenerate(47, qwen3.5_9b upsampler) / true: layout prompt) -&gt; CLIPTextEncode(4).
    /// Sink: SaveImage(9). EmptyFlux2LatentImage(44) is dead but valid and kept verbatim.
    /// </summary>
    public static class Flux3LayoutPromptWorkflow
    {
        public const string DEFAULT_UNET = "flux3-image-klein-int8-convrot.safetensors";
        public const string DEFAULT_CLIP = "qwen3vl_4b_int8_convrot.safetensors";
        public const string DEFAULT_CLIP_TYPE = "flux3";
        public const string DEFAULT_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_UPSAMPLER_CLIP = "qwen3.5_9b_flux3_prompt_upsampler_w4a8.safetensors";
        public const string DEFAULT_PREFIX = "image_flux3_klein_distilled_image_edit";
        private static readonly string DEFAULT_UPSAMPLER_SYSTEM = @"You are the prompt upsampler of an image-editing model. You see the reference image(s), attached in order (the first image, the second image, ...), and the user's short request. Expand it into ONE detailed, self-contained instruction: the caption the editing model receives together with the same images. Output only the instruction as one continuous English prose block, with no greetings, explanations, Markdown, JSON, coordinates, ids or angle-bracket tokens. Read a vague or conversational request as the most plausible edit of the supplied images.\n\nPick the voice by task shape. When one reference stays and gets bounded changes, write an imperative editing instruction on it (\""Remove the ...\"", \""Change the ... to ...\"", \""Zoom in on ...\""); a whole-image transformation opens with its scope (\""Convert the entire image into ...\"", \""Apply a ... filter over the entire scene\"", \""Horizontally flip the entire image so that ...\"", \""Shift the camera viewpoint to ...\"", then where the major elements should now be). When the references are drawn into a newly composed frame (combining subjects, a new scene, a garment, object or style donor), write a generation request (\""Create a photograph of ...\"", \""Create an anime-style illustration of ...\"", \""Generate a new image featuring ...\"") that says what to take from each image, by ordinal, and what is newly generated.\n\nKeep every explicit edit, constraint, count, position, style cue and piece of wording, and decide what is left open as one concrete plan. Ground everything in what is actually visible: identify each referent unambiguously by its real appearance, never mention content that is not there, and use every image the user attached. State every end state concretely (which color, how large, which pose, which material) and give new content enough appearance to be generated. Cover the knock-on effects of the change (shadows, reflections, occlusion, perspective, scale, lighting) and say what fills a removed region. A zoom-in names what stays in frame and which neighbors are cropped out; an outpainting continues the scene in the same medium. Describe the change, not the whole scene, and close an editing instruction with one compact clause naming what stays unchanged; transferred people keep their identity, face and accessories unless asked otherwise. Transcribe on-image text verbatim in its original language inside double quotes, with line breaks as \\n, and match the original font, size and color when replacing it. Name recognizable public figures, brands and landmarks you can see. Directions are frame-relative; use no evaluative or quality words. Typical length is 50 to 150 words.\n\nOutput only the instruction.";
        private static readonly string DEFAULT_BBOX_JSON = @"[{""x"": 512, ""y"": 288, ""width"": 224, ""height"": 224, ""metadata"": {""type"": ""obj"", ""text"": """", ""desc"": ""earring"", ""palette"": []}}]";

        public static string CreateWorkflow(string layoutPrompt, string inputImageFilename = "", string inputImage2Filename = "", long? seed = null, int steps = 14, double cfg = 1.0, string samplerName = "euler", string scheduler = "simple", double resolutionMegapixels = 0.5, int boxWidth = 1360, int boxHeight = 752, string bboxJson = null, string upsamplerSystem = null, string filenamePrefix = DEFAULT_PREFIX, string unetName = null, string clipName = null, string vaeName = null, string upsamplerClipName = null)
        {
            layoutPrompt = EscapeJson(layoutPrompt ?? string.Empty);
            inputImageFilename = EscapeJson(inputImageFilename ?? string.Empty);
            inputImage2Filename = EscapeJson(inputImage2Filename ?? string.Empty);
            filenamePrefix = EscapeJson(filenamePrefix ?? string.Empty);
            samplerName = EscapeJson(samplerName ?? string.Empty);
            scheduler = EscapeJson(scheduler ?? string.Empty);
            unetName = EscapeJson(unetName ?? DEFAULT_UNET);
            clipName = EscapeJson(clipName ?? DEFAULT_CLIP);
            vaeName = EscapeJson(vaeName ?? DEFAULT_VAE);
            upsamplerClipName = EscapeJson(upsamplerClipName ?? DEFAULT_UPSAMPLER_CLIP);
            upsamplerSystem = EscapeJson(upsamplerSystem ?? DEFAULT_UPSAMPLER_SYSTEM);
            bboxJson = string.IsNullOrWhiteSpace(bboxJson) ? DEFAULT_BBOX_JSON : bboxJson;
            if (!seed.HasValue || seed.Value < 0)
                seed = new Random().Next();
            var inv = CultureInfo.InvariantCulture;
            string cfgStr = cfg.ToString("R", inv);
            string resolutionMpStr = resolutionMegapixels.ToString("R", inv);

            return $@"{{
              ""1"": {{
              ""inputs"": {{
                ""unet_name"": ""{unetName}"",
                ""weight_dtype"": ""default""
              }},
              ""class_type"": ""UNETLoader""
            }},
              ""2"": {{
              ""inputs"": {{
                ""clip_name"": ""{clipName}"",
                ""type"": ""flux3"",
                ""device"": ""default""
              }},
              ""class_type"": ""CLIPLoader""
            }},
              ""3"": {{
              ""inputs"": {{
                ""vae_name"": ""{vaeName}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""4"": {{
              ""inputs"": {{
                ""clip"": [""2"", 0],
                ""text"": [""52"", 0]
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""7"": {{
              ""inputs"": {{
                ""model"": [""1"", 0],
                ""positive"": [""17"", 0],
                ""negative"": [""17"", 1],
                ""latent_image"": [""17"", 2],
                ""seed"": {seed.Value},
                ""steps"": {steps},
                ""cfg"": {cfgStr},
                ""sampler_name"": ""{samplerName}"",
                ""scheduler"": ""{scheduler}"",
                ""denoise"": 1
              }},
              ""class_type"": ""KSampler""
            }},
              ""8"": {{
              ""inputs"": {{
                ""samples"": [""7"", 0],
                ""vae"": [""3"", 0]
              }},
              ""class_type"": ""VAEDecode""
            }},
              ""9"": {{
              ""inputs"": {{
                ""images"": [""8"", 0],
                ""filename_prefix"": ""{filenamePrefix}""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""11"": {{
              ""inputs"": {{
                ""image"": ""{inputImageFilename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }},
              ""17"": {{
              ""inputs"": {{
                ""positive"": [""4"", 0],
                ""negative"": [""32"", 0],
                ""vae"": [""3"", 0],
                ""images.image0"": [""11"", 0],
                ""images.image1"": [""54"", 0],
                ""resolution"": ""resize"",
                ""resolution.megapixels"": {resolutionMpStr}
              }},
              ""class_type"": ""ReferenceMultiLatent""
            }},
              ""32"": {{
              ""inputs"": {{
                ""conditioning"": [""4"", 0]
              }},
              ""class_type"": ""ConditioningZeroOut""
            }},
              ""33"": {{
              ""inputs"": {{
                ""background"": [""11"", 0],
                ""width"": {boxWidth},
                ""height"": {boxHeight},
                ""editor_state"": {bboxJson},
                ""last_incoming"": []
              }},
              ""class_type"": ""CreateBoundingBoxes""
            }},
              ""34"": {{
              ""inputs"": {{
                ""source"": [""49"", 0]
              }},
              ""class_type"": ""PreviewAny""
            }},
              ""44"": {{
              ""inputs"": {{
                ""width"": 1024,
                ""height"": 1024,
                ""batch_size"": 1
              }},
              ""class_type"": ""EmptyFlux2LatentImage""
            }},
              ""46"": {{
              ""inputs"": {{
                ""clip_name"": ""{upsamplerClipName}"",
                ""type"": ""flux3"",
                ""device"": ""default""
              }},
              ""class_type"": ""CLIPLoader""
            }},
              ""47"": {{
              ""inputs"": {{
                ""clip"": [""46"", 0],
                ""image"": [""50"", 0],
                ""system_prompt"": [""48"", 0],
                ""prompt"": [""34"", 0],
                ""max_length"": 3584,
                ""sampling_mode"": ""on"",
                ""sampling_mode.temperature"": 1,
                ""sampling_mode.top_k"": 1,
                ""sampling_mode.top_p"": 0.95,
                ""sampling_mode.min_p"": 0,
                ""sampling_mode.repetition_penalty"": 1.05,
                ""sampling_mode.seed"": 2,
                ""sampling_mode.presence_penalty"": 0,
                ""thinking"": false,
                ""use_default_template"": true,
                ""mtp"": ""off""
              }},
              ""class_type"": ""TextGenerate""
            }},
              ""48"": {{
              ""inputs"": {{
                ""value"": ""{upsamplerSystem}""
              }},
              ""class_type"": ""PrimitiveStringMultiline""
            }},
              ""49"": {{
              ""inputs"": {{
                ""element"": [""33"", 2],
                ""prompt"": ""{layoutPrompt}""
              }},
              ""class_type"": ""Flux3LayoutPrompt""
            }},
              ""50"": {{
              ""inputs"": {{
                ""images.image0"": [""11"", 0]
              }},
              ""class_type"": ""BatchImagesNode""
            }},
              ""51"": {{
              ""inputs"": {{
                ""on_false"": [""47"", 0],
                ""on_true"": [""34"", 0],
                ""switch"": false
              }},
              ""class_type"": ""ComfySwitchNode""
            }},
              ""52"": {{
              ""inputs"": {{
                ""source"": [""51"", 0]
              }},
              ""class_type"": ""PreviewAny""
            }},
              ""54"": {{
              ""inputs"": {{
                ""image"": ""{inputImage2Filename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// SD seamless PBR texture generator (mtb family, 20 nodes). Source: SEAMLESS.json.
    /// Aliases: 05-seamless_texture.json (revAnimated, 28 steps, seed 1002),
    /// seamless_texture_test.json (DreamShaper, randomize seed, no RepeatImageBatch /
    /// ImageGrid preview pair - this builder always includes them), MCPTEST1.json
    /// (revAnimated, patch startStep 0, seed 1001 fixed).
    /// Chain: CheckpointLoader(4) -&gt; Model Patch Seamless(91, tiling XY) -&gt;
    /// KSampler(3) -&gt; Vae Decode(mtb, 96) -&gt; Albedo SaveImage(46); decode output also
    /// feeds Deep Bump(mtb) x3 -&gt; Normal(63) / Curvature(67) / Height(69) SaveImages,
    /// plus RepeatImageBatch(98) + ImageTileOffset(97) + ImageGrid(100) tiled preview.
    /// Sinks: Albedo / Normal / Curvature / Height SaveImages.
    /// </summary>
    public static class SeamlessTextureWorkflow
    {
        public const string DEFAULT_CHECKPOINT = "dreamshaper_8.safetensors";
        public const string DEFAULT_VAE = "vae-ft-mse-840000-ema-pruned.safetensors";
        public const string DEFAULT_NEGATIVE = "((drawing, cartoon, painting, sketch, blur, depth of field, dof))";

        public static string CreateWorkflow(string positivePrompt, string negativePrompt = DEFAULT_NEGATIVE, string checkpoint = DEFAULT_CHECKPOINT, string vaeName = DEFAULT_VAE, long? seed = null, int steps = 12, double cfg = 8.0, string samplerName = "dpmpp_2m", string scheduler = "normal", int width = 768, int height = 768, int patchStartStep = 1, int tileRepeat = 2)
        {
            positivePrompt = EscapeJson(positivePrompt ?? string.Empty);
            negativePrompt = EscapeJson(negativePrompt ?? string.Empty);
            checkpoint = EscapeJson(checkpoint ?? string.Empty);
            vaeName = EscapeJson(vaeName ?? string.Empty);
            samplerName = EscapeJson(samplerName ?? string.Empty);
            scheduler = EscapeJson(scheduler ?? string.Empty);
            long samplerSeed = seed ?? 1037L;
            var inv = CultureInfo.InvariantCulture;
            string cfgStr = cfg.ToString("R", inv);

            return $@"{{
              ""3"": {{
              ""inputs"": {{
                ""model"": [""91"", 1],
                ""positive"": [""6"", 0],
                ""negative"": [""7"", 0],
                ""latent_image"": [""74"", 0],
                ""seed"": {samplerSeed},
                ""steps"": {steps},
                ""cfg"": {cfgStr},
                ""sampler_name"": ""{samplerName}"",
                ""scheduler"": ""{scheduler}"",
                ""denoise"": 1
              }},
              ""class_type"": ""KSampler""
            }},
              ""4"": {{
              ""inputs"": {{
                ""ckpt_name"": ""{checkpoint}""
              }},
              ""class_type"": ""CheckpointLoaderSimple""
            }},
              ""6"": {{
              ""inputs"": {{
                ""clip"": [""4"", 1],
                ""text"": ""{positivePrompt}""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""7"": {{
              ""inputs"": {{
                ""clip"": [""4"", 1],
                ""text"": ""{negativePrompt}""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""43"": {{
              ""inputs"": {{
                ""vae_name"": ""{vaeName}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""46"": {{
              ""inputs"": {{
                ""images"": [""96"", 0],
                ""filename_prefix"": ""Albedo""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""62"": {{
              ""inputs"": {{
                ""image"": [""96"", 0],
                ""mode"": ""Color to Normals"",
                ""color_to_normals_overlap"": ""SMALL"",
                ""normals_to_curvature_blur_radius"": ""SMALLEST"",
                ""normals_to_height_seamless"": true,
                ""auto_download"": true
              }},
              ""class_type"": ""Deep Bump (mtb)""
            }},
              ""63"": {{
              ""inputs"": {{
                ""images"": [""62"", 0],
                ""filename_prefix"": ""Normal""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""66"": {{
              ""inputs"": {{
                ""image"": [""62"", 0],
                ""mode"": ""Normals to Curvature"",
                ""color_to_normals_overlap"": ""SMALL"",
                ""normals_to_curvature_blur_radius"": ""SMALLEST"",
                ""normals_to_height_seamless"": true,
                ""auto_download"": true
              }},
              ""class_type"": ""Deep Bump (mtb)""
            }},
              ""67"": {{
              ""inputs"": {{
                ""images"": [""66"", 0],
                ""filename_prefix"": ""Curvature""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""68"": {{
              ""inputs"": {{
                ""image"": [""62"", 0],
                ""mode"": ""Normals to Height"",
                ""color_to_normals_overlap"": ""SMALL"",
                ""normals_to_curvature_blur_radius"": ""SMALLEST"",
                ""normals_to_height_seamless"": true,
                ""auto_download"": true
              }},
              ""class_type"": ""Deep Bump (mtb)""
            }},
              ""69"": {{
              ""inputs"": {{
                ""images"": [""68"", 0],
                ""filename_prefix"": ""Height""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""74"": {{
              ""inputs"": {{
                ""width"": {width},
                ""height"": {height},
                ""batch_size"": 1
              }},
              ""class_type"": ""EmptyLatentImage""
            }},
              ""91"": {{
              ""inputs"": {{
                ""model"": [""4"", 0],
                ""startStep"": {patchStartStep},
                ""stopStep"": 999,
                ""tilingX"": true,
                ""tilingY"": true
              }},
              ""class_type"": ""Model Patch Seamless (mtb)""
            }},
              ""93"": {{
              ""inputs"": {{
                ""images"": [""100"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""96"": {{
              ""inputs"": {{
                ""samples"": [""3"", 0],
                ""vae"": [""43"", 0],
                ""seamless_model"": true,
                ""use_tiling_decoder"": false,
                ""tile_size"": 512
              }},
              ""class_type"": ""Vae Decode (mtb)""
            }},
              ""97"": {{
              ""inputs"": {{
                ""image"": [""98"", 0],
                ""tilesX"": {tileRepeat},
                ""tilesY"": {tileRepeat}
              }},
              ""class_type"": ""Image Tile Offset (mtb)""
            }},
              ""98"": {{
              ""inputs"": {{
                ""image"": [""96"", 0],
                ""amount"": 4
              }},
              ""class_type"": ""RepeatImageBatch""
            }},
              ""100"": {{
              ""inputs"": {{
                ""images"": [""97"", 0],
                ""columns"": 2,
                ""cell_width"": 256,
                ""cell_height"": 256,
                ""padding"": 0
              }},
              ""class_type"": ""ImageGrid""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// SD seamless texture + pixel-art downscale/noise preview chain (33 nodes, 3 dead
    /// reference LoadImages(130/131/136, "Texture 0x03EC.png") omitted).
    /// Source: PERFECT_SEAMLESS_TEXTURE_GENERATOR.json. Base = SeamlessTextureWorkflow
    /// (realisticVision ckpt, 512px latent, 28 steps / cfg 2.2, seed 1032) plus:
    /// JarJarPixelate(125, 9) -&gt; ImageResizeKJv2 64px (128) -&gt; ImageAddNoise(132, 0.03)
    /// and 128px (139) -&gt; ImageAddNoise(134, 0.03) grid previews.
    /// Sinks: Albedo(46) / Normal(63) / Curvature(67) / Height(69) SaveImages.
    /// </summary>
    public static class PerfectSeamlessTextureWorkflow
    {
        public const string DEFAULT_CHECKPOINT = "realisticVisionV60B1_v51HyperVAE.safetensors";
        public const string DEFAULT_VAE = "vae-ft-mse-840000-ema-pruned.safetensors";
        public const string DEFAULT_NEGATIVE = "((drawing, cartoon, painting, sketch, blur, depth of field, dof))";

        public static string CreateWorkflow(string positivePrompt, string negativePrompt = DEFAULT_NEGATIVE, string checkpoint = DEFAULT_CHECKPOINT, string vaeName = DEFAULT_VAE, long? seed = null, int steps = 28, double cfg = 2.2, string samplerName = "euler", string scheduler = "simple", int patchStartStep = 1, int pixelSize = 9, long? noiseSeed1 = null, long? noiseSeed2 = null, double noiseStrength1 = 0.03, double noiseStrength2 = 0.03)
        {
            positivePrompt = EscapeJson(positivePrompt ?? string.Empty);
            negativePrompt = EscapeJson(negativePrompt ?? string.Empty);
            checkpoint = EscapeJson(checkpoint ?? string.Empty);
            vaeName = EscapeJson(vaeName ?? string.Empty);
            samplerName = EscapeJson(samplerName ?? string.Empty);
            scheduler = EscapeJson(scheduler ?? string.Empty);
            long samplerSeed = seed ?? 1032L;
            if (!noiseSeed1.HasValue || noiseSeed1.Value < 0)
                noiseSeed1 = new Random().Next();
            if (!noiseSeed2.HasValue || noiseSeed2.Value < 0)
                noiseSeed2 = new Random().Next();
            var inv = CultureInfo.InvariantCulture;
            string cfgStr = cfg.ToString("R", inv);
            string noiseStrength1Str = noiseStrength1.ToString("R", inv);
            string noiseStrength2Str = noiseStrength2.ToString("R", inv);

            return $@"{{
              ""3"": {{
              ""inputs"": {{
                ""model"": [""91"", 1],
                ""positive"": [""6"", 0],
                ""negative"": [""7"", 0],
                ""latent_image"": [""74"", 0],
                ""seed"": {samplerSeed},
                ""steps"": {steps},
                ""cfg"": {cfgStr},
                ""sampler_name"": ""{samplerName}"",
                ""scheduler"": ""{scheduler}"",
                ""denoise"": ""simple""
              }},
              ""class_type"": ""KSampler""
            }},
              ""4"": {{
              ""inputs"": {{
                ""ckpt_name"": ""{checkpoint}""
              }},
              ""class_type"": ""CheckpointLoaderSimple""
            }},
              ""6"": {{
              ""inputs"": {{
                ""clip"": [""4"", 1],
                ""text"": ""{positivePrompt}""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""7"": {{
              ""inputs"": {{
                ""clip"": [""4"", 1],
                ""text"": ""{negativePrompt}""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""43"": {{
              ""inputs"": {{
                ""vae_name"": ""{vaeName}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""46"": {{
              ""inputs"": {{
                ""images"": [""96"", 0],
                ""filename_prefix"": ""Albedo""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""62"": {{
              ""inputs"": {{
                ""image"": [""96"", 0],
                ""mode"": ""Color to Normals"",
                ""color_to_normals_overlap"": ""SMALL"",
                ""normals_to_curvature_blur_radius"": ""SMALLEST"",
                ""normals_to_height_seamless"": true,
                ""auto_download"": true
              }},
              ""class_type"": ""Deep Bump (mtb)""
            }},
              ""63"": {{
              ""inputs"": {{
                ""images"": [""62"", 0],
                ""filename_prefix"": ""Normal""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""66"": {{
              ""inputs"": {{
                ""image"": [""62"", 0],
                ""mode"": ""Normals to Curvature"",
                ""color_to_normals_overlap"": ""SMALL"",
                ""normals_to_curvature_blur_radius"": ""SMALLEST"",
                ""normals_to_height_seamless"": true,
                ""auto_download"": true
              }},
              ""class_type"": ""Deep Bump (mtb)""
            }},
              ""67"": {{
              ""inputs"": {{
                ""images"": [""66"", 0],
                ""filename_prefix"": ""Curvature""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""68"": {{
              ""inputs"": {{
                ""image"": [""62"", 0],
                ""mode"": ""Normals to Height"",
                ""color_to_normals_overlap"": ""SMALL"",
                ""normals_to_curvature_blur_radius"": ""SMALLEST"",
                ""normals_to_height_seamless"": true,
                ""auto_download"": true
              }},
              ""class_type"": ""Deep Bump (mtb)""
            }},
              ""69"": {{
              ""inputs"": {{
                ""images"": [""68"", 0],
                ""filename_prefix"": ""Height""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""74"": {{
              ""inputs"": {{
                ""width"": 512,
                ""height"": 512,
                ""batch_size"": 1
              }},
              ""class_type"": ""EmptyLatentImage""
            }},
              ""91"": {{
              ""inputs"": {{
                ""model"": [""4"", 0],
                ""startStep"": {patchStartStep},
                ""stopStep"": 999,
                ""tilingX"": true,
                ""tilingY"": true
              }},
              ""class_type"": ""Model Patch Seamless (mtb)""
            }},
              ""96"": {{
              ""inputs"": {{
                ""samples"": [""3"", 0],
                ""vae"": [""43"", 0],
                ""seamless_model"": true,
                ""use_tiling_decoder"": false,
                ""tile_size"": 512
              }},
              ""class_type"": ""Vae Decode (mtb)""
            }},
              ""98"": {{
              ""inputs"": {{
                ""images"": [""99"", 0],
                ""border_width"": 0,
                ""number_of_columns"": 3,
                ""max_cell_size"": 64,
                ""border_red"": 0,
                ""border_green"": 0,
                ""border_blue"": 0
              }},
              ""class_type"": ""Create Grid Image from Batch""
            }},
              ""99"": {{
              ""inputs"": {{
                ""image"": [""132"", 0],
                ""amount"": 9
              }},
              ""class_type"": ""RepeatImageBatch""
            }},
              ""100"": {{
              ""inputs"": {{
                ""images"": [""98"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""125"": {{
              ""inputs"": {{
                ""image"": [""96"", 0],
                ""pixel_size"": {pixelSize}
              }},
              ""class_type"": ""JarJarPixelate""
            }},
              ""126"": {{
              ""inputs"": {{
                ""images"": [""125"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""128"": {{
              ""inputs"": {{
                ""image"": [""125"", 0],
                ""width"": 64,
                ""height"": 64,
                ""upscale_method"": ""nearest-exact"",
                ""keep_proportion"": ""stretch"",
                ""pad_color"": ""0, 0, 0"",
                ""crop_position"": ""center"",
                ""divisible_by"": 2,
                ""device"": ""cpu""
              }},
              ""class_type"": ""ImageResizeKJv2""
            }},
              ""129"": {{
              ""inputs"": {{
                ""images"": [""132"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""132"": {{
              ""inputs"": {{
                ""image"": [""128"", 0],
                ""seed"": {noiseSeed1.Value},
                ""strength"": {noiseStrength1Str}
              }},
              ""class_type"": ""ImageAddNoise""
            }},
              ""133"": {{
              ""inputs"": {{
                ""images"": [""138"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""134"": {{
              ""inputs"": {{
                ""image"": [""139"", 0],
                ""seed"": {noiseSeed2.Value},
                ""strength"": {noiseStrength2Str}
              }},
              ""class_type"": ""ImageAddNoise""
            }},
              ""135"": {{
              ""inputs"": {{
                ""images"": [""134"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""137"": {{
              ""inputs"": {{
                ""image"": [""134"", 0],
                ""amount"": 9
              }},
              ""class_type"": ""RepeatImageBatch""
            }},
              ""138"": {{
              ""inputs"": {{
                ""images"": [""137"", 0],
                ""border_width"": 0,
                ""number_of_columns"": 3,
                ""max_cell_size"": 128,
                ""border_red"": 0,
                ""border_green"": 0,
                ""border_blue"": 0
              }},
              ""class_type"": ""Create Grid Image from Batch""
            }},
              ""139"": {{
              ""inputs"": {{
                ""image"": [""125"", 0],
                ""width"": 128,
                ""height"": 128,
                ""upscale_method"": ""nearest-exact"",
                ""keep_proportion"": ""stretch"",
                ""pad_color"": ""0, 0, 0"",
                ""crop_position"": ""center"",
                ""divisible_by"": 2,
                ""device"": ""cpu""
              }},
              ""class_type"": ""ImageResizeKJv2""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Batched seamless texture saver over a JarJarTextList word list (40 nodes, dead
    /// reference LoadImage(130, "Texture 0x03EC.png") omitted). Source: PERFECT_SEAMLESS_TEXTURE_GENERATOR_SAVER.json.
    /// Base = PerfectSeamlessTextureWorkflow at batch 8 with MeesaJarJar_*_512 prefixes;
    /// the positive prompt is "Closeup texture of " + JarJarListItem(150) over the
    /// terrain word list (151), indexed by easy rangeInt(156, step 0..5); selected noise
    /// stages are saved as MeesaJarJar_&lt;word&gt;_64_ (144, 64px) and _128_ (152).
    /// Sinks: Albedo/Normal/Curvature/Height (MeesaJarJar_*) + 64px + 128px SaveImages.
    /// </summary>
    public static class PerfectSeamlessTextureSaverWorkflow
    {
        public const string DEFAULT_CHECKPOINT = "realisticVisionV60B1_v51HyperVAE.safetensors";
        public const string DEFAULT_VAE = "vae-ft-mse-840000-ema-pruned.safetensors";
        public const string DEFAULT_NEGATIVE = "((drawing, cartoon, painting, sketch, blur, depth of field, dof))";
        public const string DEFAULT_SAVE_TAG = "MeesaJarJar_";
        private static readonly string DEFAULT_TEXTURE_LIST = @"Terrain / Grass	grass lush spring
Terrain / Grass	grass lush summer
Terrain / Grass	grass lush autumn
Terrain / Grass	grass lush winter dead";

        public static string CreateWorkflow(string textureList = null, string promptPrefix = "Closeup texture of ", string saveTag = DEFAULT_SAVE_TAG, string negativePrompt = DEFAULT_NEGATIVE, string checkpoint = DEFAULT_CHECKPOINT, string vaeName = DEFAULT_VAE, long? seed = null, int steps = 28, double cfg = 2.2, string samplerName = "euler", string scheduler = "simple", int latentBatch = 8, int patchStartStep = 1, int pixelSize = 9, long? noiseSeed1 = null, long? noiseSeed2 = null, double noiseStrength1 = 0.02, double noiseStrength2 = 0.02)
        {
            promptPrefix = EscapeJson(promptPrefix ?? string.Empty);
            saveTag = EscapeJson(saveTag ?? string.Empty);
            negativePrompt = EscapeJson(negativePrompt ?? string.Empty);
            checkpoint = EscapeJson(checkpoint ?? string.Empty);
            vaeName = EscapeJson(vaeName ?? string.Empty);
            samplerName = EscapeJson(samplerName ?? string.Empty);
            scheduler = EscapeJson(scheduler ?? string.Empty);
            textureList = EscapeJson(textureList ?? DEFAULT_TEXTURE_LIST);
            long samplerSeed = seed ?? 1042L;
            if (!noiseSeed1.HasValue || noiseSeed1.Value < 0)
                noiseSeed1 = new Random().Next();
            if (!noiseSeed2.HasValue || noiseSeed2.Value < 0)
                noiseSeed2 = new Random().Next();
            var inv = CultureInfo.InvariantCulture;
            string cfgStr = cfg.ToString("R", inv);
            string noiseStrength1Str = noiseStrength1.ToString("R", inv);
            string noiseStrength2Str = noiseStrength2.ToString("R", inv);

            return $@"{{
              ""3"": {{
              ""inputs"": {{
                ""model"": [""91"", 1],
                ""positive"": [""6"", 0],
                ""negative"": [""7"", 0],
                ""latent_image"": [""74"", 0],
                ""seed"": {samplerSeed},
                ""steps"": {steps},
                ""cfg"": {cfgStr},
                ""sampler_name"": ""{samplerName}"",
                ""scheduler"": ""{scheduler}"",
                ""denoise"": ""simple""
              }},
              ""class_type"": ""KSampler""
            }},
              ""4"": {{
              ""inputs"": {{
                ""ckpt_name"": ""{checkpoint}""
              }},
              ""class_type"": ""CheckpointLoaderSimple""
            }},
              ""6"": {{
              ""inputs"": {{
                ""clip"": [""4"", 1],
                ""text"": [""148"", 0]
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""7"": {{
              ""inputs"": {{
                ""clip"": [""4"", 1],
                ""text"": ""((drawing, cartoon, painting, sketch, blur, depth of field, dof))""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""43"": {{
              ""inputs"": {{
                ""vae_name"": ""{vaeName}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""46"": {{
              ""inputs"": {{
                ""images"": [""96"", 0],
                ""filename_prefix"": ""MeesaJarJar_Albedo_512""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""62"": {{
              ""inputs"": {{
                ""image"": [""96"", 0],
                ""mode"": ""Color to Normals"",
                ""color_to_normals_overlap"": ""SMALL"",
                ""normals_to_curvature_blur_radius"": ""SMALLEST"",
                ""normals_to_height_seamless"": true,
                ""auto_download"": true
              }},
              ""class_type"": ""Deep Bump (mtb)""
            }},
              ""63"": {{
              ""inputs"": {{
                ""images"": [""62"", 0],
                ""filename_prefix"": ""MeesaJarJar_Normal_512""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""66"": {{
              ""inputs"": {{
                ""image"": [""62"", 0],
                ""mode"": ""Normals to Curvature"",
                ""color_to_normals_overlap"": ""SMALL"",
                ""normals_to_curvature_blur_radius"": ""SMALLEST"",
                ""normals_to_height_seamless"": true,
                ""auto_download"": true
              }},
              ""class_type"": ""Deep Bump (mtb)""
            }},
              ""67"": {{
              ""inputs"": {{
                ""images"": [""66"", 0],
                ""filename_prefix"": ""MeesaJarJar_Curvature_512""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""68"": {{
              ""inputs"": {{
                ""image"": [""62"", 0],
                ""mode"": ""Normals to Height"",
                ""color_to_normals_overlap"": ""SMALL"",
                ""normals_to_curvature_blur_radius"": ""SMALLEST"",
                ""normals_to_height_seamless"": true,
                ""auto_download"": true
              }},
              ""class_type"": ""Deep Bump (mtb)""
            }},
              ""69"": {{
              ""inputs"": {{
                ""images"": [""68"", 0],
                ""filename_prefix"": ""MeesaJarJar_Height_512""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""74"": {{
              ""inputs"": {{
                ""width"": 512,
                ""height"": 512,
                ""batch_size"": {latentBatch}
              }},
              ""class_type"": ""EmptyLatentImage""
            }},
              ""91"": {{
              ""inputs"": {{
                ""model"": [""4"", 0],
                ""startStep"": {patchStartStep},
                ""stopStep"": 999,
                ""tilingX"": true,
                ""tilingY"": true
              }},
              ""class_type"": ""Model Patch Seamless (mtb)""
            }},
              ""96"": {{
              ""inputs"": {{
                ""samples"": [""3"", 0],
                ""vae"": [""43"", 0],
                ""seamless_model"": true,
                ""use_tiling_decoder"": false,
                ""tile_size"": 512
              }},
              ""class_type"": ""Vae Decode (mtb)""
            }},
              ""98"": {{
              ""inputs"": {{
                ""images"": [""99"", 0],
                ""border_width"": 0,
                ""number_of_columns"": 3,
                ""max_cell_size"": 64,
                ""border_red"": 0,
                ""border_green"": 0,
                ""border_blue"": 0
              }},
              ""class_type"": ""Create Grid Image from Batch""
            }},
              ""99"": {{
              ""inputs"": {{
                ""image"": [""132"", 0],
                ""amount"": 9
              }},
              ""class_type"": ""RepeatImageBatch""
            }},
              ""100"": {{
              ""inputs"": {{
                ""images"": [""98"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""125"": {{
              ""inputs"": {{
                ""image"": [""96"", 0],
                ""pixel_size"": {pixelSize}
              }},
              ""class_type"": ""JarJarPixelate""
            }},
              ""126"": {{
              ""inputs"": {{
                ""images"": [""125"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""128"": {{
              ""inputs"": {{
                ""image"": [""125"", 0],
                ""width"": 64,
                ""height"": 64,
                ""upscale_method"": ""nearest-exact"",
                ""keep_proportion"": ""stretch"",
                ""pad_color"": ""0, 0, 0"",
                ""crop_position"": ""center"",
                ""divisible_by"": 2,
                ""device"": ""cpu""
              }},
              ""class_type"": ""ImageResizeKJv2""
            }},
              ""129"": {{
              ""inputs"": {{
                ""images"": [""132"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""132"": {{
              ""inputs"": {{
                ""image"": [""128"", 0],
                ""seed"": {noiseSeed1.Value},
                ""strength"": {noiseStrength1Str}
              }},
              ""class_type"": ""ImageAddNoise""
            }},
              ""133"": {{
              ""inputs"": {{
                ""images"": [""138"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""134"": {{
              ""inputs"": {{
                ""image"": [""139"", 0],
                ""seed"": {noiseSeed2.Value},
                ""strength"": {noiseStrength2Str}
              }},
              ""class_type"": ""ImageAddNoise""
            }},
              ""135"": {{
              ""inputs"": {{
                ""images"": [""134"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""137"": {{
              ""inputs"": {{
                ""image"": [""134"", 0],
                ""amount"": 9
              }},
              ""class_type"": ""RepeatImageBatch""
            }},
              ""138"": {{
              ""inputs"": {{
                ""images"": [""137"", 0],
                ""border_width"": 0,
                ""number_of_columns"": 3,
                ""max_cell_size"": 128,
                ""border_red"": 0,
                ""border_green"": 0,
                ""border_blue"": 0
              }},
              ""class_type"": ""Create Grid Image from Batch""
            }},
              ""139"": {{
              ""inputs"": {{
                ""image"": [""125"", 0],
                ""width"": 128,
                ""height"": 128,
                ""upscale_method"": ""nearest-exact"",
                ""keep_proportion"": ""stretch"",
                ""pad_color"": ""0, 0, 0"",
                ""crop_position"": ""center"",
                ""divisible_by"": 2,
                ""device"": ""cpu""
              }},
              ""class_type"": ""ImageResizeKJv2""
            }},
              ""144"": {{
              ""inputs"": {{
                ""images"": [""132"", 0],
                ""filename_prefix"": [""153"", 0]
              }},
              ""class_type"": ""SaveImage""
            }},
              ""145"": {{
              ""inputs"": {{
                ""string_a"": ""{saveTag}"",
                ""string_b"": [""150"", 0],
                ""delimiter"": ""_""
              }},
              ""class_type"": ""StringConcatenate""
            }},
              ""148"": {{
              ""inputs"": {{
                ""string_a"": ""{promptPrefix}"",
                ""string_b"": [""150"", 0],
                ""delimiter"": """"
              }},
              ""class_type"": ""StringConcatenate""
            }},
              ""150"": {{
              ""inputs"": {{
                ""list_in"": [""151"", 0],
                ""index"": [""156"", 0]
              }},
              ""class_type"": ""JarJarListItem""
            }},
              ""151"": {{
              ""inputs"": {{
                ""text_block"": ""{textureList}""
              }},
              ""class_type"": ""JarJarTextList""
            }},
              ""152"": {{
              ""inputs"": {{
                ""images"": [""134"", 0],
                ""filename_prefix"": [""155"", 0]
              }},
              ""class_type"": ""SaveImage""
            }},
              ""153"": {{
              ""inputs"": {{
                ""string_a"": [""145"", 0],
                ""string_b"": ""64"",
                ""delimiter"": ""_""
              }},
              ""class_type"": ""StringConcatenate""
            }},
              ""154"": {{
              ""inputs"": {{
                ""string_a"": ""{saveTag}"",
                ""string_b"": [""150"", 0],
                ""delimiter"": ""_""
              }},
              ""class_type"": ""StringConcatenate""
            }},
              ""155"": {{
              ""inputs"": {{
                ""string_a"": [""154"", 0],
                ""string_b"": ""128"",
                ""delimiter"": ""_""
              }},
              ""class_type"": ""StringConcatenate""
            }},
              ""156"": {{
              ""inputs"": {{
                ""range_mode"": ""step"",
                ""start"": 0,
                ""stop"": 5,
                ""step"": 1,
                ""num_steps"": 0,
                ""end_mode"": ""Inclusive""
              }},
              ""class_type"": ""easy rangeInt""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Single-batch seamless texture saver over a soil word list (43 nodes, dead
    /// reference LoadImage(130, "Texture 0x03EC.png") omitted).
    /// Source: PERFECT_SEAMLESS_TEXTURE_GENERATOR_SAVER_StrippedDown.json. Variant of
    /// PerfectSeamlessTextureSaverWorkflow: batch 1, "Topdown, closeup texture of " prompt,
    /// easy-seed-driven ListItem(150), no Albedo SaveImage (Normal/Curvature/Height only),
    /// extra tiled-grid SaveImage(162, "..._tiled_") + PreviewAny(160) prompt echo.
    /// Sinks: Normal/Curvature/Height (MeesaJarJar_*) + 64px + 128px + tiled SaveImages.
    /// </summary>
    public static class PerfectSeamlessTextureSaverStrippedWorkflow
    {
        public const string DEFAULT_CHECKPOINT = "realisticVisionV60B1_v51HyperVAE.safetensors";
        public const string DEFAULT_VAE = "vae-ft-mse-840000-ema-pruned.safetensors";
        public const string DEFAULT_NEGATIVE = "((drawing, cartoon, painting, sketch, blur, depth of field, dof))";
        public const string DEFAULT_SAVE_TAG = "MeesaJarJar_";
        private static readonly string DEFAULT_TEXTURE_LIST = @"soil
dirt
mud
clay
loam
topsoil
cracked earth
dry riverbed
sand
beach sand
desert sand
dunes
wet sand
rippled sand
gravel
pebbles
crushed stone
river rock
shingle
grass
turf
lawn
wild grass
dead grass
moss-covered ground
snow
fresh snow
packed snow
glacial ice
frost
slush
ice
forest floor
leaf litter
pine needles
roots
humus
bedrock
rocky terrain
scree
lava rock
granite
marble
limestone
sandstone
slate
basalt
shale
travertine
quartzite
obsidian
cliff face
boulders
cave walls
strata
mossy rock
polished stone
honed stone
rough-hewn stone
split-face stone
oak bark
pine bark
birch bark
palm bark
logs
end grain
driftwood
hardwood flooring
decking
fencing
barn wood
reclaimed wood
oak
walnut
cherry
maple
pine
teak
bamboo
ebony
plywood
OSB
MDF
particle board
veneer
parquet
weathered wood
charred wood
painted wood
stained wood
rotting wood
steel
iron
aluminum
copper
brass
bronze
gold
silver
chrome
titanium
zinc
lead
brushed metal
polished metal
anodized metal
galvanized metal
powder-coated metal
hammered metal
diamond plate
perforated metal
corrugated metal
metal mesh
metal grating
standing seam metal
rusty metal
oxidized metal
patina
scratched metal
dented metal
corroded metal
verdigris
red brick
clay brick
glazed brick
painted brick
running bond
Flemish bond
herringbone brick
stack bond
concrete block
cinder block
adobe
cob
ashlar
rubble stone
drystone
cobblestone
flagstone
stone veneer
mortar
grout
poured concrete
polished concrete
exposed aggregate
board-formed concrete
precast concrete
cracked concrete
stained concrete
fresh asphalt
worn asphalt
cracked asphalt
patched asphalt
asphalt with road markings
stucco
plaster
venetian plaster
render
drywall
joint compound
ceramic tile
porcelain tile
mosaic tile
subway tile
terracotta
quarry tile
zellige
encaustic tile
hex tile
herringbone tile
basketweave tile
chevron tile
penny round tile
pavers
sidewalk slabs
brick paving
decomposed granite
cotton
linen
wool
silk
denim
canvas
burlap
tweed
twill
nylon
polyester
spandex
mesh fabric
fleece
jersey knit
sweater knit
ribbed knit
velvet
corduroy
chenille
brocade
damask
loop pile carpet
cut pile carpet
shag carpet
Persian rug
Berber carpet
ripstop
Kevlar
carbon-fiber weave
smooth leather
grain leather
suede
patent leather
distressed leather
embossed leather
crocodile leather
snake leather
fur
scales
feathers
hide
reptile skin
pleather
vinyl
ABS plastic
polycarbonate
acrylic
injection-molded plastic
textured plastic
translucent plastic
rubber
tire tread
gym flooring
gasket
foam
silicone
carbon fiber
fiberglass
laminate
Formica
Corian
clear glass
frosted glass
stained glass
tinted glass
bubbled glass
cracked glass
wired glass
patterned glass
obscure glass
mirror
lined paper
cardboard
kraft paper
newsprint
parchment
handmade paper
crumpled paper
damask wallpaper
floral wallpaper
geometric wallpaper
vintage wallpaper
peeling wallpaper
book cover cloth
leather binding
leaves
ferns
ivy
moss
lichen
bushes
hedges
wheat
hay
thatch
straw
bread crust
cheese
citrus peel
meat
vegetables
mushroom caps
coral
bone
shell
honeycomb
calm water
rippled water
waves
foam
caustics
shallow water
deep water
ice floes
oil
lava
blood
slime
honey
asphalt shingles
wood shingles
slate shingles
clay roof tiles
metal roofing
green roof
vinyl siding
clapboard
shiplap
board-and-batten
fiber cement siding
matte paint
satin paint
eggshell paint
textured paint
peeling paint
wainscoting
paneling
acoustic panels
cork
linoleum
vinyl flooring
laminate flooring
grates
manhole covers
pipes
sheet metal
shipping containers
graffiti walls
road surfaces
curbs
tarmac
rail track bed
sci-fi panels
hull plating
circuit boards
greebles
dirt overlay
grime
dust
scratches
rust streaks
water stains
cracks
bullet holes
graffiti
moss growth
Perlin noise
Worley noise
fractal noise
gradients
stripes
checks
dots
hex pattern
halftone
Voronoi
scale pattern
weave pattern
hair cards
fur cards
skin pores
normal detail maps
trim sheets
decal atlases";

        public static string CreateWorkflow(string textureList = null, string promptPrefix = "Topdown, closeup texture of ", string saveTag = DEFAULT_SAVE_TAG, string negativePrompt = DEFAULT_NEGATIVE, string checkpoint = DEFAULT_CHECKPOINT, string vaeName = DEFAULT_VAE, long? seed = null, int steps = 17, double cfg = 6.0, string samplerName = "euler", string scheduler = "simple", int patchStartStep = 1, int pixelSize = 9, long? noiseSeed1 = null, long? noiseSeed2 = null, double noiseStrength1 = 0.02, double noiseStrength2 = 0.02)
        {
            promptPrefix = EscapeJson(promptPrefix ?? string.Empty);
            saveTag = EscapeJson(saveTag ?? string.Empty);
            negativePrompt = EscapeJson(negativePrompt ?? string.Empty);
            checkpoint = EscapeJson(checkpoint ?? string.Empty);
            vaeName = EscapeJson(vaeName ?? string.Empty);
            samplerName = EscapeJson(samplerName ?? string.Empty);
            scheduler = EscapeJson(scheduler ?? string.Empty);
            textureList = EscapeJson(textureList ?? DEFAULT_TEXTURE_LIST);
            long samplerSeed = seed ?? 730468493023629L;
            if (!noiseSeed1.HasValue || noiseSeed1.Value < 0)
                noiseSeed1 = new Random().Next();
            if (!noiseSeed2.HasValue || noiseSeed2.Value < 0)
                noiseSeed2 = new Random().Next();
            var inv = CultureInfo.InvariantCulture;
            string cfgStr = cfg.ToString("R", inv);
            string noiseStrength1Str = noiseStrength1.ToString("R", inv);
            string noiseStrength2Str = noiseStrength2.ToString("R", inv);

            return $@"{{
              ""3"": {{
              ""inputs"": {{
                ""model"": [""91"", 1],
                ""positive"": [""6"", 0],
                ""negative"": [""7"", 0],
                ""latent_image"": [""74"", 0],
                ""seed"": {samplerSeed},
                ""steps"": {steps},
                ""cfg"": {cfgStr},
                ""sampler_name"": ""{samplerName}"",
                ""scheduler"": ""{scheduler}"",
                ""denoise"": ""simple""
              }},
              ""class_type"": ""KSampler""
            }},
              ""4"": {{
              ""inputs"": {{
                ""ckpt_name"": ""{checkpoint}""
              }},
              ""class_type"": ""CheckpointLoaderSimple""
            }},
              ""6"": {{
              ""inputs"": {{
                ""clip"": [""4"", 1],
                ""text"": [""148"", 0]
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""7"": {{
              ""inputs"": {{
                ""clip"": [""4"", 1],
                ""text"": ""((drawing, cartoon, painting, sketch, blur, depth of field, dof))""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""43"": {{
              ""inputs"": {{
                ""vae_name"": ""{vaeName}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""62"": {{
              ""inputs"": {{
                ""image"": [""96"", 0],
                ""mode"": ""Color to Normals"",
                ""color_to_normals_overlap"": ""SMALL"",
                ""normals_to_curvature_blur_radius"": ""SMALLEST"",
                ""normals_to_height_seamless"": true,
                ""auto_download"": true
              }},
              ""class_type"": ""Deep Bump (mtb)""
            }},
              ""63"": {{
              ""inputs"": {{
                ""images"": [""62"", 0],
                ""filename_prefix"": ""MeesaJarJar_Normal_512""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""66"": {{
              ""inputs"": {{
                ""image"": [""62"", 0],
                ""mode"": ""Normals to Curvature"",
                ""color_to_normals_overlap"": ""SMALL"",
                ""normals_to_curvature_blur_radius"": ""SMALLEST"",
                ""normals_to_height_seamless"": true,
                ""auto_download"": true
              }},
              ""class_type"": ""Deep Bump (mtb)""
            }},
              ""67"": {{
              ""inputs"": {{
                ""images"": [""66"", 0],
                ""filename_prefix"": ""MeesaJarJar_Curvature_512""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""68"": {{
              ""inputs"": {{
                ""image"": [""62"", 0],
                ""mode"": ""Normals to Height"",
                ""color_to_normals_overlap"": ""SMALL"",
                ""normals_to_curvature_blur_radius"": ""SMALLEST"",
                ""normals_to_height_seamless"": true,
                ""auto_download"": true
              }},
              ""class_type"": ""Deep Bump (mtb)""
            }},
              ""69"": {{
              ""inputs"": {{
                ""images"": [""68"", 0],
                ""filename_prefix"": ""MeesaJarJar_Height_512""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""74"": {{
              ""inputs"": {{
                ""width"": 512,
                ""height"": 512,
                ""batch_size"": 1
              }},
              ""class_type"": ""EmptyLatentImage""
            }},
              ""91"": {{
              ""inputs"": {{
                ""model"": [""4"", 0],
                ""startStep"": {patchStartStep},
                ""stopStep"": 999,
                ""tilingX"": true,
                ""tilingY"": true
              }},
              ""class_type"": ""Model Patch Seamless (mtb)""
            }},
              ""96"": {{
              ""inputs"": {{
                ""samples"": [""3"", 0],
                ""vae"": [""43"", 0],
                ""seamless_model"": true,
                ""use_tiling_decoder"": false,
                ""tile_size"": 512
              }},
              ""class_type"": ""Vae Decode (mtb)""
            }},
              ""98"": {{
              ""inputs"": {{
                ""images"": [""99"", 0],
                ""border_width"": 0,
                ""number_of_columns"": 3,
                ""max_cell_size"": 64,
                ""border_red"": 0,
                ""border_green"": 0,
                ""border_blue"": 0
              }},
              ""class_type"": ""Create Grid Image from Batch""
            }},
              ""99"": {{
              ""inputs"": {{
                ""image"": [""132"", 0],
                ""amount"": 9
              }},
              ""class_type"": ""RepeatImageBatch""
            }},
              ""100"": {{
              ""inputs"": {{
                ""images"": [""98"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""125"": {{
              ""inputs"": {{
                ""image"": [""96"", 0],
                ""pixel_size"": {pixelSize}
              }},
              ""class_type"": ""JarJarPixelate""
            }},
              ""126"": {{
              ""inputs"": {{
                ""images"": [""125"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""128"": {{
              ""inputs"": {{
                ""image"": [""125"", 0],
                ""width"": 64,
                ""height"": 64,
                ""upscale_method"": ""nearest-exact"",
                ""keep_proportion"": ""stretch"",
                ""pad_color"": ""0, 0, 0"",
                ""crop_position"": ""center"",
                ""divisible_by"": 2,
                ""device"": ""cpu""
              }},
              ""class_type"": ""ImageResizeKJv2""
            }},
              ""129"": {{
              ""inputs"": {{
                ""images"": [""132"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""132"": {{
              ""inputs"": {{
                ""image"": [""128"", 0],
                ""seed"": {noiseSeed1.Value},
                ""strength"": {noiseStrength1Str}
              }},
              ""class_type"": ""ImageAddNoise""
            }},
              ""133"": {{
              ""inputs"": {{
                ""images"": [""138"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""134"": {{
              ""inputs"": {{
                ""image"": [""139"", 0],
                ""seed"": {noiseSeed2.Value},
                ""strength"": {noiseStrength2Str}
              }},
              ""class_type"": ""ImageAddNoise""
            }},
              ""135"": {{
              ""inputs"": {{
                ""images"": [""134"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""137"": {{
              ""inputs"": {{
                ""image"": [""134"", 0],
                ""amount"": 9
              }},
              ""class_type"": ""RepeatImageBatch""
            }},
              ""138"": {{
              ""inputs"": {{
                ""images"": [""137"", 0],
                ""border_width"": 0,
                ""number_of_columns"": 3,
                ""max_cell_size"": 128,
                ""border_red"": 0,
                ""border_green"": 0,
                ""border_blue"": 0
              }},
              ""class_type"": ""Create Grid Image from Batch""
            }},
              ""139"": {{
              ""inputs"": {{
                ""image"": [""125"", 0],
                ""width"": 128,
                ""height"": 128,
                ""upscale_method"": ""nearest-exact"",
                ""keep_proportion"": ""stretch"",
                ""pad_color"": ""0, 0, 0"",
                ""crop_position"": ""center"",
                ""divisible_by"": 2,
                ""device"": ""cpu""
              }},
              ""class_type"": ""ImageResizeKJv2""
            }},
              ""144"": {{
              ""inputs"": {{
                ""images"": [""132"", 0],
                ""filename_prefix"": [""153"", 0]
              }},
              ""class_type"": ""SaveImage""
            }},
              ""145"": {{
              ""inputs"": {{
                ""string_a"": ""{saveTag}"",
                ""string_b"": [""150"", 0],
                ""delimiter"": ""_""
              }},
              ""class_type"": ""StringConcatenate""
            }},
              ""148"": {{
              ""inputs"": {{
                ""string_a"": ""{promptPrefix}"",
                ""string_b"": [""150"", 0],
                ""delimiter"": """"
              }},
              ""class_type"": ""StringConcatenate""
            }},
              ""150"": {{
              ""inputs"": {{
                ""list_in"": [""151"", 0],
                ""index"": [""159"", 0]
              }},
              ""class_type"": ""JarJarListItem""
            }},
              ""151"": {{
              ""inputs"": {{
                ""text_block"": ""{textureList}""
              }},
              ""class_type"": ""JarJarTextList""
            }},
              ""152"": {{
              ""inputs"": {{
                ""images"": [""134"", 0],
                ""filename_prefix"": [""155"", 0]
              }},
              ""class_type"": ""SaveImage""
            }},
              ""153"": {{
              ""inputs"": {{
                ""string_a"": [""145"", 0],
                ""string_b"": ""64"",
                ""delimiter"": ""_""
              }},
              ""class_type"": ""StringConcatenate""
            }},
              ""154"": {{
              ""inputs"": {{
                ""string_a"": ""{saveTag}"",
                ""string_b"": [""150"", 0],
                ""delimiter"": ""_""
              }},
              ""class_type"": ""StringConcatenate""
            }},
              ""155"": {{
              ""inputs"": {{
                ""string_a"": [""154"", 0],
                ""string_b"": ""128"",
                ""delimiter"": ""_""
              }},
              ""class_type"": ""StringConcatenate""
            }},
              ""159"": {{
              ""inputs"": {{
                ""seed"": 393
              }},
              ""class_type"": ""easy seed""
            }},
              ""160"": {{
              ""inputs"": {{
                ""source"": [""148"", 0]
              }},
              ""class_type"": ""PreviewAny""
            }},
              ""161"": {{
              ""inputs"": {{
                ""images"": [""96"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""162"": {{
              ""inputs"": {{
                ""images"": [""98"", 0],
                ""filename_prefix"": [""163"", 0]
              }},
              ""class_type"": ""SaveImage""
            }},
              ""163"": {{
              ""inputs"": {{
                ""string_a"": [""153"", 0],
                ""string_b"": ""tiled"",
                ""delimiter"": ""_""
              }},
              ""class_type"": ""StringConcatenate""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Single-image model upscale (4 nodes). Source: DLSS_Upscale.json.
    /// Chain: LoadImage(1) -&gt; UpscaleModelLoader(2) -&gt; ImageUpscaleWithModel(3)
    /// -&gt; SaveImage(4). Sink: SaveImage (Upscale).
    /// </summary>
    public static class DlssUpscaleWorkflow
    {
        public const string DEFAULT_UPSCALE_MODEL = "RealESRGAN_x4plus.pth";
        public const string DEFAULT_PREFIX = "DLSS/upscaled";

        public static string CreateWorkflow(string inputImageFilename = "", string upscaleModel = DEFAULT_UPSCALE_MODEL, string outputPrefix = DEFAULT_PREFIX)
        {
            inputImageFilename = EscapeJson(inputImageFilename ?? string.Empty);
            upscaleModel = EscapeJson(upscaleModel ?? string.Empty);
            outputPrefix = EscapeJson(outputPrefix ?? string.Empty);

            return $@"{{
              ""1"": {{
              ""inputs"": {{
                ""image"": ""{inputImageFilename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }},
              ""2"": {{
              ""inputs"": {{
                ""model_name"": ""{upscaleModel}""
              }},
              ""class_type"": ""UpscaleModelLoader""
            }},
              ""3"": {{
              ""inputs"": {{
                ""upscale_model"": [""2"", 0],
                ""image"": [""1"", 0]
              }},
              ""class_type"": ""ImageUpscaleWithModel""
            }},
              ""4"": {{
              ""inputs"": {{
                ""images"": [""3"", 0]
              }},
              ""class_type"": ""SaveImage""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Flux.2-Klein UO-sprite rapid pipeline (44 nodes; dead JarJarRemoveBackground(151)
    /// omitted - its image input was unconnected). Source: ComfyUI_00064_.json.
    /// Aliases: MeesaJarJar_RapidUO_Flux.json and RAPIDFLUX.json (identical 44n graphs;
    /// seeds, LoadImage filenames and composite offsets differ).
    /// Chain: reference-edit (76, 1MP, save-stitched preview 143/144) then UO post:
    /// dither(152) / outline(153) / pixelate-4px(154) / bg-remove(155) previews, 128px
    /// downscale (162), shadow (166), 256px + brightness/contrast (173/174), split-alpha
    /// (172) and composite over background (168) -&gt; preview; raw decode -&gt;
    /// SaveImage(165) and the shadowed sprite -&gt; SaveImage(147). Sinks: both SaveImages.
    /// </summary>
    public static class RapidFluxWorkflow
    {
        public const string DEFAULT_UNET = Flux2KleinWorkflow.DEFAULT_UNET;
        public const string DEFAULT_CLIP = Flux2KleinWorkflow.DEFAULT_CLIP;
        public const string DEFAULT_VAE = Flux2KleinWorkflow.DEFAULT_VAE;
        public const double KLEIN_MP_DEFAULT = 1.0;
        public const long DEFAULT_SEED = 1016576029057870L;

        public static string CreateWorkflow(string positivePrompt, string inputImageFilename = "", string backgroundImageFilename = "", long? seed = null, double megapixels = KLEIN_MP_DEFAULT, int pixelSize = 4, string ditherType = "ordered_4x4", double ditherStrength = 0.25, int outlineWidth = 0, double removeThreshold = 0.06, double shadowAngle = 269.60636996238577, int shadowDistance = 57, double shadowColor = 0.0, double shadowOpacity = 0.5, double shadowSkew = -0.18, double shadowStretch = 1.0, int compositeX = 800, int compositeY = 69, double brightness = -0.05, double contrast = 0.24, double saturation = 0.0, string spritePrefix = "Vanger_Wench", string rawPrefix = "ComfyUI", string unetName = null, string clipName = null, string clipType = "flux2", string clipDevice = "default", string vaeName = null)
        {
            positivePrompt = EscapeJson(positivePrompt ?? string.Empty);
            inputImageFilename = EscapeJson(inputImageFilename ?? string.Empty);
            backgroundImageFilename = EscapeJson(backgroundImageFilename ?? string.Empty);
            spritePrefix = EscapeJson(spritePrefix ?? string.Empty);
            rawPrefix = EscapeJson(rawPrefix ?? string.Empty);
            ditherType = EscapeJson(ditherType ?? string.Empty);
            unetName = EscapeJson(unetName ?? DEFAULT_UNET);
            clipName = EscapeJson(clipName ?? DEFAULT_CLIP);
            clipType = EscapeJson(clipType ?? string.Empty);
            clipDevice = EscapeJson(clipDevice ?? string.Empty);
            vaeName = EscapeJson(vaeName ?? DEFAULT_VAE);
            long editSeed = seed ?? DEFAULT_SEED;
            var inv = CultureInfo.InvariantCulture;
            string mpStr = megapixels.ToString("R", inv);
            string ditherStrengthStr = ditherStrength.ToString("R", inv);
            string removeThresholdStr = removeThreshold.ToString("R", inv);
            string shadowAngleStr = shadowAngle.ToString("R", inv);
            string shadowColorStr = shadowColor.ToString("R", inv);
            string shadowOpacityStr = shadowOpacity.ToString("R", inv);
            string shadowSkewStr = shadowSkew.ToString("R", inv);
            string shadowStretchStr = shadowStretch.ToString("R", inv);
            string brightnessStr = brightness.ToString("R", inv);
            string contrastStr = contrast.ToString("R", inv);
            string saturationStr = saturation.ToString("R", inv);

            return $@"{{
              ""76"": {{
              ""inputs"": {{
                ""image"": ""{inputImageFilename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }},
              ""125"": {{
              ""inputs"": {{
                ""sampler_name"": ""euler""
              }},
              ""class_type"": ""KSamplerSelect""
            }},
              ""126"": {{
              ""inputs"": {{
                ""unet_name"": ""{unetName}"",
                ""weight_dtype"": ""default""
              }},
              ""class_type"": ""UNETLoader""
            }},
              ""127"": {{
              ""inputs"": {{
                ""clip_name"": ""{clipName}"",
                ""type"": ""{clipType}"",
                ""device"": ""{clipDevice}""
              }},
              ""class_type"": ""CLIPLoader""
            }},
              ""128"": {{
              ""inputs"": {{
                ""vae_name"": ""{vaeName}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""129"": {{
              ""inputs"": {{
                ""width"": [""135"", 0],
                ""height"": [""135"", 1],
                ""batch_size"": 1
              }},
              ""class_type"": ""EmptyFlux2LatentImage""
            }},
              ""130"": {{
              ""inputs"": {{
                ""image"": [""76"", 0],
                ""upscale_method"": ""nearest-exact"",
                ""megapixels"": {mpStr},
                ""resolution_steps"": 1
              }},
              ""class_type"": ""ImageScaleToTotalPixels""
            }},
              ""131"": {{
              ""inputs"": {{
                ""steps"": 4,
                ""width"": [""135"", 0],
                ""height"": [""135"", 1]
              }},
              ""class_type"": ""Flux2Scheduler""
            }},
              ""132"": {{
              ""inputs"": {{
                ""clip"": [""127"", 0],
                ""text"": ""{positivePrompt}""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""133"": {{
              ""inputs"": {{
                ""conditioning"": [""132"", 0]
              }},
              ""class_type"": ""ConditioningZeroOut""
            }},
              ""134"": {{
              ""inputs"": {{
                ""conditioning"": [""133"", 0],
                ""latent"": [""137"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""135"": {{
              ""inputs"": {{
                ""image"": [""130"", 0]
              }},
              ""class_type"": ""GetImageSize""
            }},
              ""136"": {{
              ""inputs"": {{
                ""conditioning"": [""132"", 0],
                ""latent"": [""137"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""137"": {{
              ""inputs"": {{
                ""pixels"": [""130"", 0],
                ""vae"": [""128"", 0]
              }},
              ""class_type"": ""VAEEncode""
            }},
              ""138"": {{
              ""inputs"": {{
                ""noise"": [""140"", 0],
                ""guider"": [""141"", 0],
                ""sampler"": [""125"", 0],
                ""sigmas"": [""131"", 0],
                ""latent_image"": [""129"", 0]
              }},
              ""class_type"": ""SamplerCustomAdvanced""
            }},
              ""139"": {{
              ""inputs"": {{
                ""samples"": [""138"", 0],
                ""vae"": [""128"", 0]
              }},
              ""class_type"": ""VAEDecode""
            }},
              ""140"": {{
              ""inputs"": {{
                ""noise_seed"": {editSeed}
              }},
              ""class_type"": ""RandomNoise""
            }},
              ""141"": {{
              ""inputs"": {{
                ""model"": [""126"", 0],
                ""positive"": [""136"", 0],
                ""negative"": [""134"", 0],
                ""cfg"": 1
              }},
              ""class_type"": ""CFGGuider""
            }},
              ""143"": {{
              ""inputs"": {{
                ""image1"": [""76"", 0],
                ""image2"": [""139"", 0],
                ""direction"": ""right"",
                ""match_image_size"": true,
                ""spacing_width"": 0,
                ""spacing_color"": ""white""
              }},
              ""class_type"": ""ImageStitch""
            }},
              ""144"": {{
              ""inputs"": {{
                ""images"": [""143"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""147"": {{
              ""inputs"": {{
                ""images"": [""139"", 0],
                ""filename_prefix"": ""{spritePrefix}""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""152"": {{
              ""inputs"": {{
                ""image"": [""139"", 0],
                ""dither_type"": ""{ditherType}"",
                ""strength"": {ditherStrengthStr}
              }},
              ""class_type"": ""JarJarDither""
            }},
              ""153"": {{
              ""inputs"": {{
                ""image"": [""139"", 0],
                ""outline_width"": {outlineWidth}
              }},
              ""class_type"": ""JarJarOutline""
            }},
              ""154"": {{
              ""inputs"": {{
                ""image"": [""139"", 0],
                ""pixel_size"": {pixelSize}
              }},
              ""class_type"": ""JarJarPixelate""
            }},
              ""155"": {{
              ""inputs"": {{
                ""image"": [""139"", 0],
                ""threshold"": {removeThresholdStr}
              }},
              ""class_type"": ""JarJarRemoveBackground""
            }},
              ""156"": {{
              ""inputs"": {{
                ""images"": [""152"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""157"": {{
              ""inputs"": {{
                ""images"": [""153"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""158"": {{
              ""inputs"": {{
                ""images"": [""154"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""159"": {{
              ""inputs"": {{
                ""images"": [""155"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""160"": {{
              ""inputs"": {{
                ""images"": [""76"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""161"": {{
              ""inputs"": {{
                ""images"": [""162"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""162"": {{
              ""inputs"": {{
                ""image"": [""154"", 0],
                ""upscale_method"": ""bicubic"",
                ""width"": 128,
                ""height"": 128,
                ""crop"": ""disabled""
              }},
              ""class_type"": ""ImageScale""
            }},
              ""164"": {{
              ""inputs"": {{
                ""images"": [""162"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""165"": {{
              ""inputs"": {{
                ""images"": [""139"", 0],
                ""filename_prefix"": ""{rawPrefix}""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""166"": {{
              ""inputs"": {{
                ""image"": [""155"", 0],
                ""angle"": {shadowAngleStr},
                ""distance"": {shadowDistance},
                ""shadow_color"": {shadowColorStr},
                ""shadow_opacity"": {shadowOpacityStr},
                ""skew"": {shadowSkewStr},
                ""stretch"": {shadowStretchStr}
              }},
              ""class_type"": ""JarJarShadow""
            }},
              ""167"": {{
              ""inputs"": {{
                ""images"": [""166"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""168"": {{
              ""inputs"": {{
                ""destination"": [""170"", 0],
                ""source"": [""172"", 0],
                ""mask"": [""169"", 0],
                ""x"": {compositeX},
                ""y"": {compositeY},
                ""resize_source"": false
              }},
              ""class_type"": ""ImageCompositeMasked""
            }},
              ""169"": {{
              ""inputs"": {{
                ""mask"": [""172"", 1]
              }},
              ""class_type"": ""InvertMask""
            }},
              ""170"": {{
              ""inputs"": {{
                ""image"": ""{backgroundImageFilename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }},
              ""171"": {{
              ""inputs"": {{
                ""images"": [""168"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""172"": {{
              ""inputs"": {{
                ""image"": [""174"", 0]
              }},
              ""class_type"": ""SplitImageWithAlpha""
            }},
              ""173"": {{
              ""inputs"": {{
                ""image"": [""166"", 0],
                ""upscale_method"": ""bicubic"",
                ""width"": 256,
                ""height"": 256,
                ""crop"": ""disabled""
              }},
              ""class_type"": ""ImageScale""
            }},
              ""174"": {{
              ""inputs"": {{
                ""image"": [""173"", 0],
                ""brightness"": {brightnessStr},
                ""contrast"": {contrastStr},
                ""saturation"": {saturationStr}
              }},
              ""class_type"": ""JarJarBrightnessContrast""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Flux.2-Klein UO-sprite rapid pipeline, fast variant without the 256px
    /// brightness/contrast stage (42 nodes; dead node 151 omitted). Source: rs23.json.
    /// Same graph as RapidFluxWorkflow except SplitImageWithAlpha(172) is fed directly
    /// from JarJarShadow(166); composite offsets 258/258; shadow 337.05/25.
    /// Sinks: SaveImage(147) + SaveImage(165).
    /// </summary>
    public static class RapidFluxFastWorkflow
    {
        public const string DEFAULT_UNET = Flux2KleinWorkflow.DEFAULT_UNET;
        public const string DEFAULT_CLIP = Flux2KleinWorkflow.DEFAULT_CLIP;
        public const string DEFAULT_VAE = Flux2KleinWorkflow.DEFAULT_VAE;
        public const double KLEIN_MP_DEFAULT = 1.0;
        public const long DEFAULT_SEED = 1016576029057807L;

        public static string CreateWorkflow(string positivePrompt, string inputImageFilename = "", string backgroundImageFilename = "", long? seed = null, double megapixels = KLEIN_MP_DEFAULT, int pixelSize = 4, string ditherType = "ordered_4x4", double ditherStrength = 0.25, int outlineWidth = 0, double removeThreshold = 0.06, double shadowAngle = 337.04848531263633, int shadowDistance = 25, double shadowColor = 0.0, double shadowOpacity = 0.5, double shadowSkew = -0.65, double shadowStretch = 1.0, int compositeX = 258, int compositeY = 258, string spritePrefix = "Vanger_Wench", string rawPrefix = "ComfyUI", string unetName = null, string clipName = null, string clipType = "flux2", string clipDevice = "default", string vaeName = null)
        {
            positivePrompt = EscapeJson(positivePrompt ?? string.Empty);
            inputImageFilename = EscapeJson(inputImageFilename ?? string.Empty);
            backgroundImageFilename = EscapeJson(backgroundImageFilename ?? string.Empty);
            spritePrefix = EscapeJson(spritePrefix ?? string.Empty);
            rawPrefix = EscapeJson(rawPrefix ?? string.Empty);
            ditherType = EscapeJson(ditherType ?? string.Empty);
            unetName = EscapeJson(unetName ?? DEFAULT_UNET);
            clipName = EscapeJson(clipName ?? DEFAULT_CLIP);
            clipType = EscapeJson(clipType ?? string.Empty);
            clipDevice = EscapeJson(clipDevice ?? string.Empty);
            vaeName = EscapeJson(vaeName ?? DEFAULT_VAE);
            long editSeed = seed ?? DEFAULT_SEED;
            var inv = CultureInfo.InvariantCulture;
            string mpStr = megapixels.ToString("R", inv);
            string ditherStrengthStr = ditherStrength.ToString("R", inv);
            string removeThresholdStr = removeThreshold.ToString("R", inv);
            string shadowAngleStr = shadowAngle.ToString("R", inv);
            string shadowColorStr = shadowColor.ToString("R", inv);
            string shadowOpacityStr = shadowOpacity.ToString("R", inv);
            string shadowSkewStr = shadowSkew.ToString("R", inv);
            string shadowStretchStr = shadowStretch.ToString("R", inv);

            return $@"{{
              ""76"": {{
              ""inputs"": {{
                ""image"": ""{inputImageFilename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }},
              ""125"": {{
              ""inputs"": {{
                ""sampler_name"": ""euler""
              }},
              ""class_type"": ""KSamplerSelect""
            }},
              ""126"": {{
              ""inputs"": {{
                ""unet_name"": ""{unetName}"",
                ""weight_dtype"": ""default""
              }},
              ""class_type"": ""UNETLoader""
            }},
              ""127"": {{
              ""inputs"": {{
                ""clip_name"": ""{clipName}"",
                ""type"": ""{clipType}"",
                ""device"": ""{clipDevice}""
              }},
              ""class_type"": ""CLIPLoader""
            }},
              ""128"": {{
              ""inputs"": {{
                ""vae_name"": ""{vaeName}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""129"": {{
              ""inputs"": {{
                ""width"": [""135"", 0],
                ""height"": [""135"", 1],
                ""batch_size"": 1
              }},
              ""class_type"": ""EmptyFlux2LatentImage""
            }},
              ""130"": {{
              ""inputs"": {{
                ""image"": [""76"", 0],
                ""upscale_method"": ""nearest-exact"",
                ""megapixels"": {mpStr},
                ""resolution_steps"": 1
              }},
              ""class_type"": ""ImageScaleToTotalPixels""
            }},
              ""131"": {{
              ""inputs"": {{
                ""steps"": 4,
                ""width"": [""135"", 0],
                ""height"": [""135"", 1]
              }},
              ""class_type"": ""Flux2Scheduler""
            }},
              ""132"": {{
              ""inputs"": {{
                ""clip"": [""127"", 0],
                ""text"": ""{positivePrompt}""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""133"": {{
              ""inputs"": {{
                ""conditioning"": [""132"", 0]
              }},
              ""class_type"": ""ConditioningZeroOut""
            }},
              ""134"": {{
              ""inputs"": {{
                ""conditioning"": [""133"", 0],
                ""latent"": [""137"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""135"": {{
              ""inputs"": {{
                ""image"": [""130"", 0]
              }},
              ""class_type"": ""GetImageSize""
            }},
              ""136"": {{
              ""inputs"": {{
                ""conditioning"": [""132"", 0],
                ""latent"": [""137"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""137"": {{
              ""inputs"": {{
                ""pixels"": [""130"", 0],
                ""vae"": [""128"", 0]
              }},
              ""class_type"": ""VAEEncode""
            }},
              ""138"": {{
              ""inputs"": {{
                ""noise"": [""140"", 0],
                ""guider"": [""141"", 0],
                ""sampler"": [""125"", 0],
                ""sigmas"": [""131"", 0],
                ""latent_image"": [""129"", 0]
              }},
              ""class_type"": ""SamplerCustomAdvanced""
            }},
              ""139"": {{
              ""inputs"": {{
                ""samples"": [""138"", 0],
                ""vae"": [""128"", 0]
              }},
              ""class_type"": ""VAEDecode""
            }},
              ""140"": {{
              ""inputs"": {{
                ""noise_seed"": {editSeed}
              }},
              ""class_type"": ""RandomNoise""
            }},
              ""141"": {{
              ""inputs"": {{
                ""model"": [""126"", 0],
                ""positive"": [""136"", 0],
                ""negative"": [""134"", 0],
                ""cfg"": 1
              }},
              ""class_type"": ""CFGGuider""
            }},
              ""143"": {{
              ""inputs"": {{
                ""image1"": [""76"", 0],
                ""image2"": [""139"", 0],
                ""direction"": ""right"",
                ""match_image_size"": true,
                ""spacing_width"": 0,
                ""spacing_color"": ""white""
              }},
              ""class_type"": ""ImageStitch""
            }},
              ""144"": {{
              ""inputs"": {{
                ""images"": [""143"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""147"": {{
              ""inputs"": {{
                ""images"": [""139"", 0],
                ""filename_prefix"": ""{spritePrefix}""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""152"": {{
              ""inputs"": {{
                ""image"": [""139"", 0],
                ""dither_type"": ""{ditherType}"",
                ""strength"": {ditherStrengthStr}
              }},
              ""class_type"": ""JarJarDither""
            }},
              ""153"": {{
              ""inputs"": {{
                ""image"": [""139"", 0],
                ""outline_width"": {outlineWidth}
              }},
              ""class_type"": ""JarJarOutline""
            }},
              ""154"": {{
              ""inputs"": {{
                ""image"": [""139"", 0],
                ""pixel_size"": {pixelSize}
              }},
              ""class_type"": ""JarJarPixelate""
            }},
              ""155"": {{
              ""inputs"": {{
                ""image"": [""139"", 0],
                ""threshold"": {removeThresholdStr}
              }},
              ""class_type"": ""JarJarRemoveBackground""
            }},
              ""156"": {{
              ""inputs"": {{
                ""images"": [""152"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""157"": {{
              ""inputs"": {{
                ""images"": [""153"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""158"": {{
              ""inputs"": {{
                ""images"": [""154"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""159"": {{
              ""inputs"": {{
                ""images"": [""155"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""160"": {{
              ""inputs"": {{
                ""images"": [""76"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""161"": {{
              ""inputs"": {{
                ""images"": [""162"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""162"": {{
              ""inputs"": {{
                ""image"": [""154"", 0],
                ""upscale_method"": ""bicubic"",
                ""width"": 128,
                ""height"": 128,
                ""crop"": ""disabled""
              }},
              ""class_type"": ""ImageScale""
            }},
              ""164"": {{
              ""inputs"": {{
                ""images"": [""162"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""165"": {{
              ""inputs"": {{
                ""images"": [""139"", 0],
                ""filename_prefix"": ""{rawPrefix}""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""166"": {{
              ""inputs"": {{
                ""image"": [""155"", 0],
                ""angle"": {shadowAngleStr},
                ""distance"": {shadowDistance},
                ""shadow_color"": {shadowColorStr},
                ""shadow_opacity"": {shadowOpacityStr},
                ""skew"": {shadowSkewStr},
                ""stretch"": {shadowStretchStr}
              }},
              ""class_type"": ""JarJarShadow""
            }},
              ""167"": {{
              ""inputs"": {{
                ""images"": [""166"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""168"": {{
              ""inputs"": {{
                ""destination"": [""170"", 0],
                ""source"": [""172"", 0],
                ""mask"": [""169"", 0],
                ""x"": {compositeX},
                ""y"": {compositeY},
                ""resize_source"": false
              }},
              ""class_type"": ""ImageCompositeMasked""
            }},
              ""169"": {{
              ""inputs"": {{
                ""mask"": [""172"", 1]
              }},
              ""class_type"": ""InvertMask""
            }},
              ""170"": {{
              ""inputs"": {{
                ""image"": ""{backgroundImageFilename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }},
              ""171"": {{
              ""inputs"": {{
                ""images"": [""168"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""172"": {{
              ""inputs"": {{
                ""image"": [""166"", 0]
              }},
              ""class_type"": ""SplitImageWithAlpha""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Flux.2-Klein edit + side-by-side stitch, preview-only (20 nodes).
    /// Source: ForElminsterFromMystra_RapidFluxGen.json. Reference-edit chain
    /// (76, 1MP, randomize seed) stitched next to the source (143) for preview (144).
    /// NOTE: no file-output sink - the graph ends at ImageStitch + PreviewImage.
    /// </summary>
    public static class ForElminsterRapidFluxGenWorkflow
    {
        public const string DEFAULT_UNET = Flux2KleinWorkflow.DEFAULT_UNET;
        public const string DEFAULT_CLIP = Flux2KleinWorkflow.DEFAULT_CLIP;
        public const string DEFAULT_VAE = Flux2KleinWorkflow.DEFAULT_VAE;
        public const double KLEIN_MP_DEFAULT = 1.0;

        public static string CreateWorkflow(string positivePrompt, string inputImageFilename = "", long? seed = null, double megapixels = KLEIN_MP_DEFAULT, string unetName = null, string clipName = null, string clipType = "flux2", string clipDevice = "default", string vaeName = null)
        {
            positivePrompt = EscapeJson(positivePrompt ?? string.Empty);
            inputImageFilename = EscapeJson(inputImageFilename ?? string.Empty);
            unetName = EscapeJson(unetName ?? DEFAULT_UNET);
            clipName = EscapeJson(clipName ?? DEFAULT_CLIP);
            clipType = EscapeJson(clipType ?? string.Empty);
            clipDevice = EscapeJson(clipDevice ?? string.Empty);
            vaeName = EscapeJson(vaeName ?? DEFAULT_VAE);
            if (!seed.HasValue || seed.Value < 0)
                seed = new Random().Next();
            var inv = CultureInfo.InvariantCulture;
            string mpStr = megapixels.ToString("R", inv);

            return $@"{{
              ""76"": {{
              ""inputs"": {{
                ""image"": ""{inputImageFilename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }},
              ""125"": {{
              ""inputs"": {{
                ""sampler_name"": ""euler""
              }},
              ""class_type"": ""KSamplerSelect""
            }},
              ""126"": {{
              ""inputs"": {{
                ""unet_name"": ""{unetName}"",
                ""weight_dtype"": ""default""
              }},
              ""class_type"": ""UNETLoader""
            }},
              ""127"": {{
              ""inputs"": {{
                ""clip_name"": ""{clipName}"",
                ""type"": ""{clipType}"",
                ""device"": ""{clipDevice}""
              }},
              ""class_type"": ""CLIPLoader""
            }},
              ""128"": {{
              ""inputs"": {{
                ""vae_name"": ""{vaeName}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""129"": {{
              ""inputs"": {{
                ""width"": [""135"", 0],
                ""height"": [""135"", 1],
                ""batch_size"": 1
              }},
              ""class_type"": ""EmptyFlux2LatentImage""
            }},
              ""130"": {{
              ""inputs"": {{
                ""image"": [""76"", 0],
                ""upscale_method"": ""nearest-exact"",
                ""megapixels"": {mpStr},
                ""resolution_steps"": 1
              }},
              ""class_type"": ""ImageScaleToTotalPixels""
            }},
              ""131"": {{
              ""inputs"": {{
                ""steps"": 4,
                ""width"": [""135"", 0],
                ""height"": [""135"", 1]
              }},
              ""class_type"": ""Flux2Scheduler""
            }},
              ""132"": {{
              ""inputs"": {{
                ""clip"": [""127"", 0],
                ""text"": ""{positivePrompt}""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""133"": {{
              ""inputs"": {{
                ""conditioning"": [""132"", 0]
              }},
              ""class_type"": ""ConditioningZeroOut""
            }},
              ""134"": {{
              ""inputs"": {{
                ""conditioning"": [""133"", 0],
                ""latent"": [""137"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""135"": {{
              ""inputs"": {{
                ""image"": [""130"", 0]
              }},
              ""class_type"": ""GetImageSize""
            }},
              ""136"": {{
              ""inputs"": {{
                ""conditioning"": [""132"", 0],
                ""latent"": [""137"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""137"": {{
              ""inputs"": {{
                ""pixels"": [""130"", 0],
                ""vae"": [""128"", 0]
              }},
              ""class_type"": ""VAEEncode""
            }},
              ""138"": {{
              ""inputs"": {{
                ""noise"": [""140"", 0],
                ""guider"": [""141"", 0],
                ""sampler"": [""125"", 0],
                ""sigmas"": [""131"", 0],
                ""latent_image"": [""129"", 0]
              }},
              ""class_type"": ""SamplerCustomAdvanced""
            }},
              ""139"": {{
              ""inputs"": {{
                ""samples"": [""138"", 0],
                ""vae"": [""128"", 0]
              }},
              ""class_type"": ""VAEDecode""
            }},
              ""140"": {{
              ""inputs"": {{
                ""noise_seed"": {seed.Value}
              }},
              ""class_type"": ""RandomNoise""
            }},
              ""141"": {{
              ""inputs"": {{
                ""model"": [""126"", 0],
                ""positive"": [""136"", 0],
                ""negative"": [""134"", 0],
                ""cfg"": 1
              }},
              ""class_type"": ""CFGGuider""
            }},
              ""143"": {{
              ""inputs"": {{
                ""image1"": [""76"", 0],
                ""image2"": [""139"", 0],
                ""direction"": ""right"",
                ""match_image_size"": true,
                ""spacing_width"": 0,
                ""spacing_color"": ""white""
              }},
              ""class_type"": ""ImageStitch""
            }},
              ""144"": {{
              ""inputs"": {{
                ""images"": [""143"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Flux.2-Klein edit + remove-background + shadow (26 nodes).
    /// Source: Vanger_Plants_00100_.json. Reference-edit (76, 1MP, randomize seed),
    /// stitched preview (143/144), JarJarRemoveBackground(153, 0.05) + JarJarShadow(152)
    /// -&gt; SaveImage(147, "Vanger_Plants"). The file targets a 4-input JarJarShadow;
    /// the builder targets the current 6-input node, appending skew 0.0 / stretch 1.0.
    /// Sinks: SaveImage(147).
    /// </summary>
    public static class VangerPlantsWorkflow
    {
        public const string DEFAULT_UNET = Flux2KleinWorkflow.DEFAULT_UNET;
        public const string DEFAULT_CLIP = Flux2KleinWorkflow.DEFAULT_CLIP;
        public const string DEFAULT_VAE = Flux2KleinWorkflow.DEFAULT_VAE;
        public const double KLEIN_MP_DEFAULT = 1.0;
        public const string DEFAULT_PREFIX = "Vanger_Plants";

        public static string CreateWorkflow(string positivePrompt, string inputImageFilename = "", long? seed = null, double megapixels = KLEIN_MP_DEFAULT, double removeThreshold = 0.05, double shadowAngle = 135.0, int shadowDistance = 4, double shadowColor = 0.0, double shadowOpacity = 0.5, string filenamePrefix = DEFAULT_PREFIX, string unetName = null, string clipName = null, string clipType = "flux2", string clipDevice = "default", string vaeName = null)
        {
            positivePrompt = EscapeJson(positivePrompt ?? string.Empty);
            inputImageFilename = EscapeJson(inputImageFilename ?? string.Empty);
            filenamePrefix = EscapeJson(filenamePrefix ?? string.Empty);
            unetName = EscapeJson(unetName ?? DEFAULT_UNET);
            clipName = EscapeJson(clipName ?? DEFAULT_CLIP);
            clipType = EscapeJson(clipType ?? string.Empty);
            clipDevice = EscapeJson(clipDevice ?? string.Empty);
            vaeName = EscapeJson(vaeName ?? DEFAULT_VAE);
            if (!seed.HasValue || seed.Value < 0)
                seed = new Random().Next();
            var inv = CultureInfo.InvariantCulture;
            string mpStr = megapixels.ToString("R", inv);
            string removeThresholdStr = removeThreshold.ToString("R", inv);
            string shadowAngleStr = shadowAngle.ToString("R", inv);
            string shadowColorStr = shadowColor.ToString("R", inv);
            string shadowOpacityStr = shadowOpacity.ToString("R", inv);

            return $@"{{
              ""76"": {{
              ""inputs"": {{
                ""image"": ""{inputImageFilename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }},
              ""125"": {{
              ""inputs"": {{
                ""sampler_name"": ""euler""
              }},
              ""class_type"": ""KSamplerSelect""
            }},
              ""126"": {{
              ""inputs"": {{
                ""unet_name"": ""{unetName}"",
                ""weight_dtype"": ""default""
              }},
              ""class_type"": ""UNETLoader""
            }},
              ""127"": {{
              ""inputs"": {{
                ""clip_name"": ""{clipName}"",
                ""type"": ""{clipType}"",
                ""device"": ""{clipDevice}""
              }},
              ""class_type"": ""CLIPLoader""
            }},
              ""128"": {{
              ""inputs"": {{
                ""vae_name"": ""{vaeName}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""129"": {{
              ""inputs"": {{
                ""width"": [""135"", 0],
                ""height"": [""135"", 1],
                ""batch_size"": 1
              }},
              ""class_type"": ""EmptyFlux2LatentImage""
            }},
              ""130"": {{
              ""inputs"": {{
                ""image"": [""76"", 0],
                ""upscale_method"": ""nearest-exact"",
                ""megapixels"": {mpStr},
                ""resolution_steps"": 1
              }},
              ""class_type"": ""ImageScaleToTotalPixels""
            }},
              ""131"": {{
              ""inputs"": {{
                ""steps"": 4,
                ""width"": [""135"", 0],
                ""height"": [""135"", 1]
              }},
              ""class_type"": ""Flux2Scheduler""
            }},
              ""132"": {{
              ""inputs"": {{
                ""clip"": [""127"", 0],
                ""text"": ""{positivePrompt}""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""133"": {{
              ""inputs"": {{
                ""conditioning"": [""132"", 0]
              }},
              ""class_type"": ""ConditioningZeroOut""
            }},
              ""134"": {{
              ""inputs"": {{
                ""conditioning"": [""133"", 0],
                ""latent"": [""137"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""135"": {{
              ""inputs"": {{
                ""image"": [""130"", 0]
              }},
              ""class_type"": ""GetImageSize""
            }},
              ""136"": {{
              ""inputs"": {{
                ""conditioning"": [""132"", 0],
                ""latent"": [""137"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""137"": {{
              ""inputs"": {{
                ""pixels"": [""130"", 0],
                ""vae"": [""128"", 0]
              }},
              ""class_type"": ""VAEEncode""
            }},
              ""138"": {{
              ""inputs"": {{
                ""noise"": [""140"", 0],
                ""guider"": [""141"", 0],
                ""sampler"": [""125"", 0],
                ""sigmas"": [""131"", 0],
                ""latent_image"": [""129"", 0]
              }},
              ""class_type"": ""SamplerCustomAdvanced""
            }},
              ""139"": {{
              ""inputs"": {{
                ""samples"": [""138"", 0],
                ""vae"": [""128"", 0]
              }},
              ""class_type"": ""VAEDecode""
            }},
              ""140"": {{
              ""inputs"": {{
                ""noise_seed"": {seed.Value}
              }},
              ""class_type"": ""RandomNoise""
            }},
              ""141"": {{
              ""inputs"": {{
                ""model"": [""126"", 0],
                ""positive"": [""136"", 0],
                ""negative"": [""134"", 0],
                ""cfg"": 1
              }},
              ""class_type"": ""CFGGuider""
            }},
              ""143"": {{
              ""inputs"": {{
                ""image1"": [""76"", 0],
                ""image2"": [""139"", 0],
                ""direction"": ""right"",
                ""match_image_size"": true,
                ""spacing_width"": 0,
                ""spacing_color"": ""white""
              }},
              ""class_type"": ""ImageStitch""
            }},
              ""144"": {{
              ""inputs"": {{
                ""images"": [""143"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""147"": {{
              ""inputs"": {{
                ""images"": [""139"", 0],
                ""filename_prefix"": ""{filenamePrefix}""
              }},
              ""class_type"": ""SaveImage""
            }},
              ""152"": {{
              ""inputs"": {{
                ""image"": [""153"", 0],
                ""angle"": {shadowAngleStr},
                ""distance"": {shadowDistance},
                ""shadow_color"": {shadowColorStr},
                ""shadow_opacity"": {shadowOpacityStr},
                ""skew"": 0.0,
                ""stretch"": 1.0
              }},
              ""class_type"": ""JarJarShadow""
            }},
              ""153"": {{
              ""inputs"": {{
                ""image"": [""139"", 0],
                ""threshold"": {removeThresholdStr}
              }},
              ""class_type"": ""JarJarRemoveBackground""
            }},
              ""154"": {{
              ""inputs"": {{
                ""images"": [""152"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""155"": {{
              ""inputs"": {{
                ""images"": [""153"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""156"": {{
              ""inputs"": {{
                ""images"": [""139"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Ollama text generation (7 nodes, no image graph). Source: OLLAMA.json.
    /// OllamaConnectivityV2(4) + OllamaOptionsV2(5, verbatim) drive OllamaGenerateV2(3);
    /// result / thinking / context / meta are echoed via PreviewAny(7/8/10/9).
    /// NOTE: no image/file input or output - no SaveImage sink; the result is text.
    /// The default system prompt is the UO object-vocabulary taxonomy from the file.
    /// </summary>
    public static class OllamaTextWorkflow
    {
        public const string DEFAULT_MODEL = "qwen3.5:0.8b";
        public const string DEFAULT_URL = "http://127.0.0.1:11434";
        private static readonly string DEFAULT_SYSTEM_PROMPT = @"You are a visual description linguistic vocabulary expert for ultima online. 

Ultima Online Object Categories

Organized by broad object type, covering items, equipment, world objects, crafting materials, decorations, and interactive objects.

1. Weapons

Swords

Axes

Maces

Hammers

Polearms

Fencing weapons

Bows

Crossbows

Throwing weapons

Magical weapons

Siege weapons

Ammunition

2. Armor and Protection

Helmets

Shields

Chest armor

Leg armor

Arm armor

Gloves

Boots

Leather armor

Chainmail armor

Platemail armor

Bone armor

Studded armor

Ringmail armor

Robes

Magical armor

Clothing accessories

3. Clothing and Accessories

Shirts

Pants

Skirts

Dresses

Cloaks

Capes

Robes

Tunics

Hats

Hoods

Belts

Sashes

Footwear

Jewelry

Masks

Costumes

Uniforms

4. Tools and Equipment

Mining tools

Woodcutting tools

Blacksmithing tools

Tailoring tools

Carpentry tools

Cooking tools

Fishing equipment

Alchemy equipment

Inscription tools

Tinkering tools

Farming tools

Animal husbandry tools

Musical instruments

Lockpicking tools

Repair tools

Crafting kits

5. Crafting Materials

Ingots

Ore

Wood

Logs

Boards

Leather

Hides

Cloth

Fabric

Thread

Reagents

Gems

Bones

Feathers

Scales

Fish parts

Animal products

Stone

Glass

Clay

Sand

Wax

Ingredients

Magical components

6. Food and Consumables

Bread

Meat

Fish

Fruit

Vegetables

Cheese

Dairy products

Prepared meals

Baked goods

Desserts

Drinks

Alcoholic beverages

Potions

Healing supplies

Poison

Bandages

Cooking ingredients

Seeds

7. Magic and Spellcasting

Spellbooks

Spell scrolls

Magic wands

Magical staves

Spell reagents

Enchanted jewelry

Magical weapons

Magical armor

Magical artifacts

Summoning objects

Magical containers

Magical maps

Magical keys

Magical talismans

Magical instruments

8. Furniture and Interior Decorations

Chairs

Benches

Stools

Tables

Desks

Beds

Cabinets

Chests

Shelves

Bookcases

Dressers

Cupboards

Wardrobes

Rugs

Carpets

Curtains

Mirrors

Paintings

Tapestries

Statues

Vases

Pottery

Decorative plants

Candles

Candelabras

Wall decorations

9. Containers and Storage

Backpacks

Bags

Pouches

Chests

Crates

Barrels

Boxes

Cabinets

Lockboxes

Strongboxes

Gift boxes

Secure containers

Banks

Corpse containers

Resource containers

Magical containers

10. Buildings and Structures

Houses

Castles

Keeps

Towers

Shops

Inns

Taverns

Banks

Blacksmithies

Bakeries

Churches

Temples

Guildhalls

Stables

Farms

Mills

Dungeons

Fortifications

Bridges

Gates

Walls

Fences

Watchtowers

Ruins

11. World and Environmental Objects

Trees

Bushes

Flowers

Grass

Mushrooms

Rocks

Boulders

Mountains

Cliffs

Water features

Rivers

Lakes

Ponds

Caves

Stalagmites

Stalactites

Ice formations

Snow

Lava

Coral

Reefs

Dead trees

Fallen logs

Natural debris

12. Interactive World Objects

Doors

Gates

Levers

Switches

Pressure plates

Traps

Portals

Moongates

Teleporters

Ladders

Stairs

Elevators

Drawbridges

Fountains

Wells

Water troughs

Anvils

Forges

Looms

Spinning wheels

Cooking fires

Ovens

Mills

Workbenches

13. Lighting and Fire

Torches

Lanterns

Candles

Candlesticks

Candelabras

Oil lamps

Wall sconces

Street lamps

Brazier

Campfires

Fire pits

Hearths

Fireplaces

Magical lights

14. Books, Documents and Writing

Books

Spellbooks

Scrolls

Maps

Treasure maps

Letters

Journals

Diaries

Ledgers

Contracts

Deeds

Recipes

Instructions

Signs

Posters

Notices

Banners

Tomes

Blank books

Written messages

15. Treasure and Valuable Objects

Gold coins

Gold piles

Gems

Jewelry

Precious metals

Treasure chests

Treasure maps

Rare artifacts

Ancient relics

Decorative treasures

Rare weapons

Rare armor

Collectibles

Trophies

Rare furnishings

Historical objects

16. Quest and Story Objects

Quest items

Ancient artifacts

Relics

Keys

Seals

Emblems

Documents

Letters

Tokens

Mission supplies

Special containers

Ritual objects

Puzzle objects

Story-specific equipment

17. Transportation and Mount Equipment

Saddles

Saddle bags

Horse barding

Reins

Harnesses

Wagons

Carts

Ships

Ship components

Ship equipment

Docking objects

Travel tokens

Mount accessories

18. Farming and Animal Husbandry

Seeds

Crops

Hay

Feed

Animal products

Eggs

Milk

Wool

Animal hides

Livestock equipment

Troughs

Animal cages

Beehives

Farming tools

Plant pots

Planters

19. Fishing and Maritime Objects

Fishing poles

Fishing nets

Bait

Fish

Shells

Pearls

Coral

Seaweed

Ship parts

Anchors

Ropes

Sails

Ship cannons

Maritime treasure

Fishing traps

20. Traps and Mechanisms

Bear traps

Poison traps

Dart traps

Explosive traps

Hidden mechanisms

Tripwires

Pressure plates

Locked mechanisms

Mechanical devices

Gears

Springs

Levers

Cogs

Trap components

21. Crafting Stations and Production

Forges

Anvils

Smithing tools

Tailoring benches

Looms

Spinning wheels

Carpentry benches

Sawhorses

Tinkering benches

Alchemy tables

Cooking ovens

Butchering tables

Grinding mills

Glassblowing equipment

Pottery wheels

Workbenches

22. Signs, Banners and Heraldry

Shop signs

Street signs

Direction signs

House signs

Guild banners

Flags

Standards

Heraldic shields

Family crests

Emblems

Insignia

Notice boards

Posters

Decorative plaques

23. Religious and Ritual Objects

Altars

Shrines

Statues

Religious icons

Holy books

Sacred relics

Candles

Incense burners

Ritual vessels

Ceremonial weapons

Offerings

Totems

Magical circles

24. Combat and War Objects

Siege engines

Catapults

Ballistae

Cannons

Ammunition

Barricades

Spikes

War banners

Battle standards

Training dummies

Practice weapons

War trophies

Corpses

Gravestones

Battlefield debris

25. Music and Entertainment

Lutes

Harps

Drums

Tambourines

Flutes

Horns

Musical scores

Instruments

Playing cards

Dice

Board games

Toys

Juggling equipment

Performance props

26. Personal and Miscellaneous Items

Keys

Key rings

Coins

Bags

Bottles

Flasks

Buckets

Ropes

Chains

Locks

Mirrors

Combs

Brushes

Sewing kits

Household supplies

Personal keepsakes

Trinkets

27. Dungeon and Ruin Objects

Sarcophagi

Coffins

Tombstones

Gravestones

Bones

Skulls

Chains

Iron bars

Prison cells

Torture devices

Ancient altars

Ruined furniture

Broken statues

Dungeon doors

Secret doors

Ancient mechanisms

Dungeon traps

Ruined masonry

28. Decorative Ground Objects

Rugs

Carpets

Mats

Stones

Cobblestones

Floor tiles

Wooden flooring

Broken pottery

Debris

Scattered bones

Leaves

Branches

Hay piles

Hay bales

Straw

Shells

Scattered coins

Bloodstains

Ash piles

29. Corpses and Remains

Human corpses

Animal corpses

Monster corpses

Skeletons

Bones

Skulls

Gravestones

Tombstones

Ashes

Remains

Body parts

Burial objects

Memorial objects

30. Game System and Special Objects

Spawn points

Monster spawners

Item spawners

Teleport triggers

Quest triggers

Event objects

Vendor objects

Trade objects

Bank objects

Guild objects

House placement objects

House customization objects

Crafting menus

Skill training objects

Server-controlled objects

Event rewards

Special event decorations";
        private static readonly string DEFAULT_PROMPT = @"You are a visual description linguistic vocabulary expert. Generate ONE random medieval object. Start with its name, followed by a concise visual description of its appearance. Use keyword format with descriptive terms only. Do not include any preamble, introductory phrases, special punctuation, or Markdown. Do not include any other scenery. Output exactly ONE item, not a list. Do not repeat a previously used object. ";

        public static string CreateWorkflow(string prompt = null, string systemPrompt = null, string model = DEFAULT_MODEL, string url = DEFAULT_URL, int keepAlive = 5, string keepAliveUnit = "minutes", long? seed = null, bool think = false, bool keepContext = false, string format = "text")
        {
            prompt = EscapeJson(prompt ?? DEFAULT_PROMPT);
            systemPrompt = EscapeJson(systemPrompt ?? DEFAULT_SYSTEM_PROMPT);
            model = EscapeJson(model ?? string.Empty);
            url = EscapeJson(url ?? string.Empty);
            keepAliveUnit = EscapeJson(keepAliveUnit ?? string.Empty);
            format = EscapeJson(format ?? string.Empty);
            string thinkJson = think ? "true" : "false";
            string keepContextJson = keepContext ? "true" : "false";
            if (!seed.HasValue || seed.Value < 0)
                seed = new Random().Next();
            long optionsSeed = seed.Value;

            return $@"{{
              ""3"": {{
              ""inputs"": {{
                ""connectivity"": [""4"", 0],
                ""options"": [""5"", 0],
                ""system"": ""{systemPrompt}"",
                ""prompt"": ""{prompt}"",
                ""think"": {thinkJson},
                ""keep_context"": {keepContextJson},
                ""format"": ""{format}""
              }},
              ""class_type"": ""OllamaGenerateV2""
            }},
              ""4"": {{
              ""inputs"": {{
                ""url"": ""{url}"",
                ""model"": ""{model}"",
                ""keep_alive"": {keepAlive},
                ""keep_alive_unit"": ""{keepAliveUnit}""
              }},
              ""class_type"": ""OllamaConnectivityV2""
            }},
              ""5"": {{
              ""inputs"": {{
                ""enable_mirostat"": false,
                ""mirostat"": 0,
                ""enable_mirostat_eta"": false,
                ""mirostat_eta"": 0.1,
                ""enable_mirostat_tau"": false,
                ""mirostat_tau"": 5,
                ""enable_num_ctx"": true,
                ""num_ctx"": 20,
                ""enable_repeat_last_n"": false,
                ""repeat_last_n"": 64,
                ""enable_repeat_penalty"": false,
                ""repeat_penalty"": 1.1,
                ""enable_temperature"": false,
                ""temperature"": 0.8,
                ""enable_seed"": true,
                ""seed"": {optionsSeed},
                ""enable_stop"": false,
                ""stop"": """",
                ""enable_tfs_z"": false,
                ""tfs_z"": 1,
                ""enable_num_predict"": false,
                ""num_predict"": -1,
                ""enable_top_k"": false,
                ""top_k"": 40,
                ""enable_top_p"": false,
                ""top_p"": 0.9,
                ""enable_min_p"": false,
                ""min_p"": 0,
                ""debug"": false
              }},
              ""class_type"": ""OllamaOptionsV2""
            }},
              ""7"": {{
              ""inputs"": {{
                ""source"": [""3"", 0]
              }},
              ""class_type"": ""PreviewAny""
            }},
              ""8"": {{
              ""inputs"": {{
                ""source"": [""3"", 1]
              }},
              ""class_type"": ""PreviewAny""
            }},
              ""9"": {{
              ""inputs"": {{
                ""source"": [""3"", 2]
              }},
              ""class_type"": ""PreviewAny""
            }},
              ""10"": {{
              ""inputs"": {{
                ""source"": [""3"", 3]
              }},
              ""class_type"": ""PreviewAny""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Musubi-tuner FLUX Klein LoRA trainer (2 nodes). Source: TRAINER.json.
    /// MusubiFluxKleinLoraTrainer(1) -> PreviewAny(2, echoes the lora_path string).
    /// NOTE: no SaveImage sink - output is the trained LoRA path string; requires a
    /// musubi-tuner install (musubiPath) and training images (imagesPath or linked
    /// image_*/caption_* inputs, left unconnected here as in the file). The UI file
    /// stores one stale null widget between inputcount and images_path; it is dropped.
    /// </summary>
    public static class FluxKleinLoraTrainerWorkflow
    {
        public const string DEFAULT_VARIANT = "Klein Base 4B";
        public const string DEFAULT_DIT = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_VAE_MODEL = "ae.safetensors";
        public const string DEFAULT_TEXT_ENCODER = "qwen_3_4b.safetensors";
        public const string DEFAULT_VRAM_MODE = "Max (1256px) fp8";

        public static string CreateWorkflow(string imagesPath = "", string musubiPath = "", string modelVariant = DEFAULT_VARIANT, int inputCount = 4, string ditModel = DEFAULT_DIT, string vaeModel = DEFAULT_VAE_MODEL, string textEncoder = DEFAULT_TEXT_ENCODER, string caption = "photo of subject", int trainingSteps = 400, double learningRate = 0.0001, int loraRank = 32, int blocksToSwap = 0, string vramMode = DEFAULT_VRAM_MODE, bool keepLora = true, string outputName = "MyLora", string customPythonExe = "")
        {
            imagesPath = EscapeJson(imagesPath ?? string.Empty);
            musubiPath = EscapeJson(musubiPath ?? string.Empty);
            modelVariant = EscapeJson(modelVariant ?? string.Empty);
            ditModel = EscapeJson(ditModel ?? string.Empty);
            vaeModel = EscapeJson(vaeModel ?? string.Empty);
            textEncoder = EscapeJson(textEncoder ?? string.Empty);
            caption = EscapeJson(caption ?? string.Empty);
            vramMode = EscapeJson(vramMode ?? string.Empty);
            outputName = EscapeJson(outputName ?? string.Empty);
            customPythonExe = EscapeJson(customPythonExe ?? string.Empty);
            var inv = CultureInfo.InvariantCulture;
            string learningRateStr = learningRate.ToString("R", inv);
            string keepLoraJson = keepLora ? "true" : "false";

            return $@"{{
              ""1"": {{
              ""inputs"": {{
                ""model_variant"": ""{modelVariant}"",
                ""inputcount"": {inputCount},
                ""images_path"": ""{imagesPath}"",
                ""musubi_path"": ""{musubiPath}"",
                ""dit_model"": ""{ditModel}"",
                ""vae_model"": ""{vaeModel}"",
                ""text_encoder"": ""{textEncoder}"",
                ""caption"": ""{caption}"",
                ""training_steps"": {trainingSteps},
                ""learning_rate"": {learningRateStr},
                ""lora_rank"": {loraRank},
                ""blocks_to_swap"": {blocksToSwap},
                ""vram_mode"": ""{vramMode}"",
                ""keep_lora"": {keepLoraJson},
                ""output_name"": ""{outputName}"",
                ""custom_python_exe"": ""{customPythonExe}""
              }},
              ""class_type"": ""MusubiFluxKleinLoraTrainer""
            }},
              ""2"": {{
              ""inputs"": {{
                ""source"": [""1"", 0]
              }},
              ""class_type"": ""PreviewAny""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Flux.2-Klein edit -> TripoSplat image-to-3D (expanded f179 subgraph, 34 nodes).
    /// Source: LOOKHERE.json (19n). LoadImage(132) -> Klein edit (131_*, prompt from
    /// PrimitiveStringMultiline(138), fixed-seed subgraph rev) -> RemoveBackground(113) mask
    /// + TripoSplatPreprocessImage(103, 624px) -> TripoSplatConditioning(100, dino_v3 +
    /// flux2 VAE) -> KSampler(101, 12 steps / cfg 3.0, triposplat_fp16) ->
    /// VAEDecodeTripoSplat(107, 648k gaussians) -> SplatToFile3D(92, ply) -> SaveGLB(177)
    /// + JarJarSubmit3D(134, foundry API). PreviewGaussianSplat(144) is omitted
    /// (viewport_state is unconnected UI state). Sinks: SplatToFile3D, SaveGLB, JarJarSubmit3D.
    /// </summary>
    public static class LookhereTripoSplatWorkflow
    {
        public const string DEFAULT_UNET = Flux2KleinWorkflow.DEFAULT_UNET;
        public const string DEFAULT_CLIP = Flux2KleinWorkflow.DEFAULT_CLIP;
        public const string DEFAULT_VAE = Flux2KleinWorkflow.DEFAULT_VAE;
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FOUNDRY_URL = "https://meesajarjar.com/foundry-api.php";
        public const double KLEIN_MP_DEFAULT = 1.0;
        public const long DEFAULT_KLEIN_SEED = 43301611940738L;

        public static string CreateWorkflow(string prompt, string inputImageFilename = "", long tripoSeed = 681108631170808L, int tripoSteps = 12, double tripoCfg = 3.0, int numGaussians = 648000, long decodeSeed = 790219963981400L, long? kleinSeed = null, double kleinCfg = 1.0, double megapixels = KLEIN_MP_DEFAULT, int preprocSize = 624, int preprocErode = 1, string splatFormat = "ply", string glbPrefix = "", string foundryUrl = DEFAULT_FOUNDRY_URL, string foundryFilename = "", string tripoUnet = DEFAULT_TRIPO_UNET, string tripoVae = DEFAULT_TRIPO_VAE, string fluxVae = null, string clipVision = DEFAULT_CLIP_VISION, string bgRemovalModel = DEFAULT_BG_REMOVAL, string unetName = null, string clipName = null, string vaeName = null)
        {
            prompt = EscapeJson(prompt ?? string.Empty);
            inputImageFilename = EscapeJson(inputImageFilename ?? string.Empty);
            foundryUrl = EscapeJson(foundryUrl ?? string.Empty);
            foundryFilename = EscapeJson(foundryFilename ?? string.Empty);
            splatFormat = EscapeJson(splatFormat ?? string.Empty);
            glbPrefix = EscapeJson(glbPrefix ?? string.Empty);
            tripoUnet = EscapeJson(tripoUnet ?? string.Empty);
            tripoVae = EscapeJson(tripoVae ?? string.Empty);
            clipVision = EscapeJson(clipVision ?? string.Empty);
            bgRemovalModel = EscapeJson(bgRemovalModel ?? string.Empty);
            unetName = EscapeJson(unetName ?? DEFAULT_UNET);
            clipName = EscapeJson(clipName ?? DEFAULT_CLIP);
            fluxVae = EscapeJson(fluxVae ?? Flux2KleinWorkflow.DEFAULT_VAE);
            vaeName = EscapeJson(vaeName ?? Flux2KleinWorkflow.DEFAULT_VAE);
            long kleinSeedValue = kleinSeed ?? DEFAULT_KLEIN_SEED;
            var inv = CultureInfo.InvariantCulture;
            string tripoCfgStr = tripoCfg.ToString("R", inv);
            string kleinCfgStr = kleinCfg.ToString("R", inv);
            string mpStr = megapixels.ToString("R", inv);

            return $@"{{
              ""92"": {{
              ""inputs"": {{
                ""splat"": [""107"", 0],
                ""format"": ""{splatFormat}""
              }},
              ""class_type"": ""SplatToFile3D""
            }},
              ""100"": {{
              ""inputs"": {{
                ""clip_vision"": [""104"", 0],
                ""vae"": [""106"", 0],
                ""image"": [""103"", 0]
              }},
              ""class_type"": ""TripoSplatConditioning""
            }},
              ""101"": {{
              ""inputs"": {{
                ""model"": [""110"", 0],
                ""positive"": [""100"", 0],
                ""negative"": [""100"", 1],
                ""latent_image"": [""100"", 2],
                ""seed"": {tripoSeed},
                ""steps"": {tripoSteps},
                ""cfg"": {tripoCfgStr},
                ""sampler_name"": ""euler"",
                ""scheduler"": ""simple"",
                ""denoise"": 1
              }},
              ""class_type"": ""KSampler""
            }},
              ""103"": {{
              ""inputs"": {{
                ""image"": [""131_65"", 0],
                ""mask"": [""113"", 0],
                ""erode_radius"": {preprocErode},
                ""size"": {preprocSize}
              }},
              ""class_type"": ""TripoSplatPreprocessImage""
            }},
              ""104"": {{
              ""inputs"": {{
                ""clip_name"": ""{clipVision}""
              }},
              ""class_type"": ""CLIPVisionLoader""
            }},
              ""105"": {{
              ""inputs"": {{
                ""vae_name"": ""{tripoVae}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""106"": {{
              ""inputs"": {{
                ""vae_name"": ""{fluxVae}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""107"": {{
              ""inputs"": {{
                ""samples"": [""101"", 0],
                ""vae"": [""105"", 0],
                ""num_gaussians"": {numGaussians},
                ""seed"": {decodeSeed}
              }},
              ""class_type"": ""VAEDecodeTripoSplat""
            }},
              ""110"": {{
              ""inputs"": {{
                ""unet_name"": ""{tripoUnet}"",
                ""weight_dtype"": ""fp8_e5m2""
              }},
              ""class_type"": ""UNETLoader""
            }},
              ""113"": {{
              ""inputs"": {{
                ""bg_removal_model"": [""114"", 0],
                ""image"": [""131_65"", 0]
              }},
              ""class_type"": ""RemoveBackground""
            }},
              ""114"": {{
              ""inputs"": {{
                ""bg_removal_name"": ""{bgRemovalModel}""
              }},
              ""class_type"": ""LoadBackgroundRemovalModel""
            }},
              ""132"": {{
              ""inputs"": {{
                ""image"": ""{inputImageFilename}"",
                ""upload"": ""image""
              }},
              ""class_type"": ""LoadImage""
            }},
              ""133"": {{
              ""inputs"": {{
                ""images"": [""131_65"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""134"": {{
              ""inputs"": {{
                ""model_3d"": [""92"", 0],
                ""prompt"": ""{prompt}"",
                ""api_url"": ""{foundryUrl}"",
                ""filename"": ""{foundryFilename}""
              }},
              ""class_type"": ""JarJarSubmit3D""
            }},
              ""138"": {{
              ""inputs"": {{
                ""value"": ""{prompt}""
              }},
              ""class_type"": ""PrimitiveStringMultiline""
            }},
              ""177"": {{
              ""inputs"": {{
                ""mesh"": [""92"", 0],
                ""filename_prefix"": ""{glbPrefix}"",
                ""image"": """"
              }},
              ""class_type"": ""SaveGLB""
            }},
              ""179"": {{
              ""inputs"": {{
                ""images"": [""103"", 0]
              }},
              ""class_type"": ""PreviewImage""
            }},
              ""131_61"": {{
              ""inputs"": {{
                ""sampler_name"": ""euler""
              }},
              ""class_type"": ""KSamplerSelect""
            }},
              ""131_62"": {{
              ""inputs"": {{
                ""steps"": 4,
                ""width"": [""131_130"", 0],
                ""height"": [""131_130"", 1]
              }},
              ""class_type"": ""Flux2Scheduler""
            }},
              ""131_63"": {{
              ""inputs"": {{
                ""model"": [""131_70"", 0],
                ""positive"": [""131_123"", 0],
                ""negative"": [""131_121"", 0],
                ""cfg"": {kleinCfgStr}
              }},
              ""class_type"": ""CFGGuider""
            }},
              ""131_64"": {{
              ""inputs"": {{
                ""noise"": [""131_73"", 0],
                ""guider"": [""131_63"", 0],
                ""sampler"": [""131_61"", 0],
                ""sigmas"": [""131_62"", 0],
                ""latent_image"": [""131_66"", 0]
              }},
              ""class_type"": ""SamplerCustomAdvanced""
            }},
              ""131_65"": {{
              ""inputs"": {{
                ""samples"": [""131_64"", 0],
                ""vae"": [""131_72"", 0]
              }},
              ""class_type"": ""VAEDecode""
            }},
              ""131_66"": {{
              ""inputs"": {{
                ""width"": [""131_130"", 0],
                ""height"": [""131_130"", 1],
                ""batch_size"": 1
              }},
              ""class_type"": ""EmptyFlux2LatentImage""
            }},
              ""131_70"": {{
              ""inputs"": {{
                ""unet_name"": ""{unetName}"",
                ""weight_dtype"": ""default""
              }},
              ""class_type"": ""UNETLoader""
            }},
              ""131_71"": {{
              ""inputs"": {{
                ""clip_name"": ""{clipName}"",
                ""type"": ""flux2"",
                ""device"": ""default""
              }},
              ""class_type"": ""CLIPLoader""
            }},
              ""131_72"": {{
              ""inputs"": {{
                ""vae_name"": ""{vaeName}""
              }},
              ""class_type"": ""VAELoader""
            }},
              ""131_73"": {{
              ""inputs"": {{
                ""noise_seed"": {kleinSeedValue}
              }},
              ""class_type"": ""RandomNoise""
            }},
              ""131_74"": {{
              ""inputs"": {{
                ""clip"": [""131_71"", 0],
                ""text"": ""{prompt}""
              }},
              ""class_type"": ""CLIPTextEncode""
            }},
              ""131_80"": {{
              ""inputs"": {{
                ""image"": [""132"", 0],
                ""upscale_method"": ""nearest-exact"",
                ""megapixels"": {mpStr},
                ""resolution_steps"": 1
              }},
              ""class_type"": ""ImageScaleToTotalPixels""
            }},
              ""131_82"": {{
              ""inputs"": {{
                ""conditioning"": [""131_74"", 0]
              }},
              ""class_type"": ""ConditioningZeroOut""
            }},
              ""131_121"": {{
              ""inputs"": {{
                ""conditioning"": [""131_82"", 0],
                ""latent"": [""131_122"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""131_122"": {{
              ""inputs"": {{
                ""pixels"": [""131_80"", 0],
                ""vae"": [""131_72"", 0]
              }},
              ""class_type"": ""VAEEncode""
            }},
              ""131_123"": {{
              ""inputs"": {{
                ""conditioning"": [""131_74"", 0],
                ""latent"": [""131_122"", 0]
              }},
              ""class_type"": ""ReferenceLatent""
            }},
              ""131_130"": {{
              ""inputs"": {{
                ""image"": [""131_80"", 0]
              }},
              ""class_type"": ""GetImageSize""
            }}
            }}";
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

}
