using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MeesaMultisMaker.ComfyUI
{
    /// <summary>
    /// Builds a single ComfyUI Flux2Klein inpaint workflow that processes
    /// multiple tiles as one batched forward pass using ImageBatch nodes.
    /// All tiles must share the same width and height.
    /// The SaveImage node outputs one PNG per tile in batch order.
    /// </summary>
    public static class Flux2KleinInpaintBatchWorkflow
    {
        /// <summary>Max tiles per single ComfyUI submission (tune to available VRAM).</summary>
        public const int MAX_BATCH_SIZE = 8;

        public static string CreateWorkflow(
            string positivePrompt,
            string negativePrompt,
            IList<string> contextFilenames,
            IList<string> maskFilenames,
            int tileWidth,
            int tileHeight,
            int steps,
            double cfg,
            long? seed,
            string samplerName,
            string unetName = null,
            string clipName = null,
            string vaeName = null)
        {
            if (contextFilenames == null || contextFilenames.Count == 0)
                throw new ArgumentException("contextFilenames must not be empty.");
            if (maskFilenames == null || maskFilenames.Count != contextFilenames.Count)
                throw new ArgumentException("maskFilenames must match contextFilenames count.");

            if (!seed.HasValue)
                seed = new Random().Next();

            positivePrompt = EscapeJson(positivePrompt ?? string.Empty);
            negativePrompt = EscapeJson(negativePrompt ?? string.Empty);
            unetName    = EscapeJson(string.IsNullOrWhiteSpace(unetName)    ? Flux2KleinInpaintWorkflow.DEFAULT_UNET : unetName);
            clipName    = EscapeJson(string.IsNullOrWhiteSpace(clipName)    ? Flux2KleinInpaintWorkflow.DEFAULT_CLIP : clipName);
            vaeName     = EscapeJson(string.IsNullOrWhiteSpace(vaeName)     ? Flux2KleinInpaintWorkflow.DEFAULT_VAE  : vaeName);
            samplerName = EscapeJson(string.IsNullOrWhiteSpace(samplerName) ? "euler"                                : samplerName);

            int N = contextFilenames.Count;
            int nextId = 1;

            // Fixed backbone nodes
            int unetId = nextId++;
            int clipId = nextId++;
            int vaeId  = nextId++;
            int posId  = nextId++;
            int negId  = nextId++;

            // LoadImage nodes: N context + N mask
            var ctxIds = new int[N];
            for (int i = 0; i < N; i++) ctxIds[i] = nextId++;

            var mskIds = new int[N];
            for (int i = 0; i < N; i++) mskIds[i] = nextId++;

            // ImageBatch chains
            var ctxBatchIds = N > 1 ? new int[N - 1] : new int[0];
            if (N > 1)
                for (int i = 0; i < N - 1; i++) ctxBatchIds[i] = nextId++;

            var mskBatchIds = N > 1 ? new int[N - 1] : new int[0];
            if (N > 1)
                for (int i = 0; i < N - 1; i++) mskBatchIds[i] = nextId++;

            int finalCtxId    = N == 1 ? ctxIds[0] : ctxBatchIds[N - 2];
            int finalMskImgId = N == 1 ? mskIds[0] : mskBatchIds[N - 2];

            // Remaining pipeline nodes
            int maskConvId   = nextId++;
            int condId       = nextId++;
            int noiseId      = nextId++;
            int samplerSelId = nextId++;
            int schedulerId  = nextId++;
            int guiderId     = nextId++;
            int advSamplId   = nextId++;
            int decodeId     = nextId++;
            int saveId       = nextId++;

            var sb = new StringBuilder();
            sb.AppendLine("{");

            // ---- Backbone ----
            Append(sb, unetId,  $"{{ \"unet_name\": \"{unetName}\", \"weight_dtype\": \"default\" }}",                              "UNETLoader",              true);
            Append(sb, clipId,  $"{{ \"clip_name\": \"{clipName}\", \"type\": \"flux2\", \"device\": \"default\" }}",               "CLIPLoader",              true);
            Append(sb, vaeId,   $"{{ \"vae_name\": \"{vaeName}\" }}",                                                               "VAELoader",               true);
            Append(sb, posId,   $"{{ \"text\": \"{positivePrompt}\", \"clip\": [\"{clipId}\", 0] }}",                               "CLIPTextEncode",          true);
            Append(sb, negId,   $"{{ \"text\": \"{negativePrompt}\", \"clip\": [\"{clipId}\", 0] }}",                               "CLIPTextEncode",          true);

            // ---- Tile LoadImage nodes ----
            for (int i = 0; i < N; i++)
                Append(sb, ctxIds[i], $"{{ \"image\": \"{EscapeJson(contextFilenames[i])}\" }}", "LoadImage", true);

            for (int i = 0; i < N; i++)
                Append(sb, mskIds[i], $"{{ \"image\": \"{EscapeJson(maskFilenames[i])}\" }}", "LoadImage", true);

            // ---- ImageBatch chains ----
            if (N > 1)
            {
                Append(sb, ctxBatchIds[0],
                    $"{{ \"image1\": [\"{ctxIds[0]}\", 0], \"image2\": [\"{ctxIds[1]}\", 0] }}",
                    "ImageBatch", true);
                for (int i = 1; i < N - 1; i++)
                    Append(sb, ctxBatchIds[i],
                        $"{{ \"image1\": [\"{ctxBatchIds[i - 1]}\", 0], \"image2\": [\"{ctxIds[i + 1]}\", 0] }}",
                        "ImageBatch", true);

                Append(sb, mskBatchIds[0],
                    $"{{ \"image1\": [\"{mskIds[0]}\", 0], \"image2\": [\"{mskIds[1]}\", 0] }}",
                    "ImageBatch", true);
                for (int i = 1; i < N - 1; i++)
                    Append(sb, mskBatchIds[i],
                        $"{{ \"image1\": [\"{mskBatchIds[i - 1]}\", 0], \"image2\": [\"{mskIds[i + 1]}\", 0] }}",
                        "ImageBatch", true);
            }

            // ---- Mask + inpaint conditioning ----
            Append(sb, maskConvId,
                $"{{ \"channel\": \"red\", \"image\": [\"{finalMskImgId}\", 0] }}",
                "ImageToMask", true);
            Append(sb, condId,
                $"{{ \"positive\": [\"{posId}\", 0], \"negative\": [\"{negId}\", 0], \"vae\": [\"{vaeId}\", 0], \"pixels\": [\"{finalCtxId}\", 0], \"mask\": [\"{maskConvId}\", 0], \"noise_mask\": true }}",
                "InpaintModelConditioning", true);

            // ---- Sampler pipeline ----
            Append(sb, noiseId,
                $"{{ \"noise_seed\": {seed.Value} }}",
                "RandomNoise", true);
            Append(sb, samplerSelId,
                $"{{ \"sampler_name\": \"{samplerName}\" }}",
                "KSamplerSelect", true);
            Append(sb, schedulerId,
                $"{{ \"steps\": {steps}, \"width\": {Math.Max(64, tileWidth)}, \"height\": {Math.Max(64, tileHeight)} }}",
                "Flux2Scheduler", true);
            Append(sb, guiderId,
                $"{{ \"model\": [\"{unetId}\", 0], \"positive\": [\"{condId}\", 0], \"negative\": [\"{condId}\", 1], \"cfg\": {cfg.ToString("0.0", CultureInfo.InvariantCulture)} }}",
                "CFGGuider", true);
            Append(sb, advSamplId,
                $"{{ \"noise\": [\"{noiseId}\", 0], \"guider\": [\"{guiderId}\", 0], \"sampler\": [\"{samplerSelId}\", 0], \"sigmas\": [\"{schedulerId}\", 0], \"latent_image\": [\"{condId}\", 2] }}",
                "SamplerCustomAdvanced", true);

            // ---- Decode + save (last node: no trailing comma) ----
            Append(sb, decodeId,
                $"{{ \"samples\": [\"{advSamplId}\", 0], \"vae\": [\"{vaeId}\", 0] }}",
                "VAEDecode", true);
            Append(sb, saveId,
                $"{{ \"filename_prefix\": \"TileBatch\", \"images\": [\"{decodeId}\", 0] }}",
                "SaveImage", false);

            sb.Append("}");
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, int nodeId, string inputs, string classType, bool trailingComma)
        {
            sb.AppendLine($"  \"{nodeId}\": {{ \"inputs\": {inputs}, \"class_type\": \"{classType}\" }}{(trailingComma ? "," : "")}");
        }

        private static string EscapeJson(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", string.Empty);
        }
    }
}
