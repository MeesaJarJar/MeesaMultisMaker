using System;
using System.Globalization;

namespace MeesaMultisMaker.ComfyUI
{
    public static class Flux2KleinInpaintWorkflow
    {
        public const string DEFAULT_UNET = "flux-2-klein-9b.safetensors";
        public const string DEFAULT_CLIP = "qwen_3_8b_fp8mixed.safetensors";
        public const string DEFAULT_VAE = "flux2-vae.safetensors";

        public static string CreateWorkflow(
            string positivePrompt,
            string negativePrompt,
            string inputImageFilename,
            string maskImageFilename,
            int width,
            int height,
            int steps,
            double cfg,
            long? seed,
            string samplerName,
            string unetName = null,
            string clipName = null,
            string vaeName = null)
        {
            if (!seed.HasValue)
                seed = new Random().Next();

            positivePrompt = EscapeJson(positivePrompt ?? string.Empty);
            negativePrompt = EscapeJson(negativePrompt ?? string.Empty);
            inputImageFilename = EscapeJson(inputImageFilename ?? string.Empty);
            maskImageFilename = EscapeJson(maskImageFilename ?? string.Empty);
            unetName = EscapeJson(string.IsNullOrWhiteSpace(unetName) ? DEFAULT_UNET : unetName);
            clipName = EscapeJson(string.IsNullOrWhiteSpace(clipName) ? DEFAULT_CLIP : clipName);
            vaeName = EscapeJson(string.IsNullOrWhiteSpace(vaeName) ? DEFAULT_VAE : vaeName);
            samplerName = EscapeJson(string.IsNullOrWhiteSpace(samplerName) ? "euler" : samplerName);

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
      ""type"": ""flux2"",
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
      ""text"": ""{positivePrompt}"",
      ""clip"": [""2"", 0]
    }},
    ""class_type"": ""CLIPTextEncode""
  }},
  ""5"": {{
    ""inputs"": {{
      ""text"": ""{negativePrompt}"",
      ""clip"": [""2"", 0]
    }},
    ""class_type"": ""CLIPTextEncode""
  }},
  ""6"": {{
    ""inputs"": {{
      ""image"": ""{inputImageFilename}""
    }},
    ""class_type"": ""LoadImage""
  }},
  ""7"": {{
    ""inputs"": {{
      ""image"": ""{maskImageFilename}""
    }},
    ""class_type"": ""LoadImage""
  }},
  ""8"": {{
    ""inputs"": {{
      ""channel"": ""red"",
      ""image"": [""7"", 0]
    }},
    ""class_type"": ""ImageToMask""
  }},
  ""9"": {{
    ""inputs"": {{
      ""positive"": [""4"", 0],
      ""negative"": [""5"", 0],
      ""vae"": [""3"", 0],
      ""pixels"": [""6"", 0],
      ""mask"": [""8"", 0],
      ""noise_mask"": true
    }},
    ""class_type"": ""InpaintModelConditioning""
  }},
  ""10"": {{
    ""inputs"": {{
      ""noise_seed"": {seed.Value}
    }},
    ""class_type"": ""RandomNoise""
  }},
  ""11"": {{
    ""inputs"": {{
      ""sampler_name"": ""{samplerName}""
    }},
    ""class_type"": ""KSamplerSelect""
  }},
  ""12"": {{
    ""inputs"": {{
      ""steps"": {steps},
      ""width"": {Math.Max(64, width)},
      ""height"": {Math.Max(64, height)}
    }},
    ""class_type"": ""Flux2Scheduler""
  }},
  ""13"": {{
    ""inputs"": {{
      ""model"": [""1"", 0],
      ""positive"": [""9"", 0],
      ""negative"": [""9"", 1],
      ""cfg"": {cfg.ToString("0.0", CultureInfo.InvariantCulture)}
    }},
    ""class_type"": ""CFGGuider""
  }},
  ""14"": {{
    ""inputs"": {{
      ""noise"": [""10"", 0],
      ""guider"": [""13"", 0],
      ""sampler"": [""11"", 0],
      ""sigmas"": [""12"", 0],
      ""latent_image"": [""9"", 2]
    }},
    ""class_type"": ""SamplerCustomAdvanced""
  }},
  ""15"": {{
    ""inputs"": {{
      ""samples"": [""14"", 0],
      ""vae"": [""3"", 0]
    }},
    ""class_type"": ""VAEDecode""
  }},
  ""16"": {{
    ""inputs"": {{
      ""filename_prefix"": ""Flux2KleinInpaint"",
      ""images"": [""15"", 0]
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
}
