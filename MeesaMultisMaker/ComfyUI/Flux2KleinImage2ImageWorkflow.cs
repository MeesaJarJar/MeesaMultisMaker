using System;
using System.Globalization;

namespace MeesaMultisMaker.ComfyUI
{
    /// <summary>
    /// Flux 2 Klein reference-edit img2img — baseline converted from the user's
    /// UI-format workflow LOOKHERE_FLUXKLEIN2_IMAGE2IMAGE.json (19 nodes) to flat
    /// API format ({"id":{"class_type":...,"inputs":{...}}} with [nodeId, outIdx]
    /// links resolved from the file's `links` array). Node IDs below match the
    /// UI file so the queued graph is recognizable in the ComfyUI queue UI.
    ///
    /// Chain: LoadImage(76) -&gt; ImageScaleToTotalPixels(129, nearest-exact)
    /// -&gt; GetImageSize(134) + VAEEncode(136); CLIPTextEncode(131) feeds both
    /// ReferenceLatent(135, positive) and ConditioningZeroOut(132) -&gt;
    /// ReferenceLatent(133, negative); CFGGuider(140) joins model + both
    /// conditionings; SamplerCustomAdvanced(137) runs noise(139) + guider(140)
    /// + sampler(124) + sigmas(130) + empty latent(128); VAEDecode(138) -&gt;
    /// SaveImage(9). There is NO classic KSampler and NO denoise input —
    /// denoise plumbing was removed from this path (SD paths keep denoise).
    /// Steps are fixed at 4 and cfg at 1.0 per the user's workflow.
    ///
    /// Size mapping: the user's graph sizes the render from the scaled input
    /// image (129 -&gt; 134 -&gt; 128/130 links preserved). The app's width/height
    /// are mapped onto (a) ImageScaleToTotalPixels megapixels = w*h/1e6 clamped
    /// to [0.1, 1.0] (0.2 fallback, the file's value), and (b) the widget
    /// defaults of EmptyFlux2LatentImage(128) and Flux2Scheduler(130) which are
    /// set to the requested w/h (they are link-overridden at runtime but record
    /// intent and serve as fallbacks).
    /// </summary>
    public static class Flux2KleinImage2ImageWorkflow
    {
        public const string DEFAULT_UNET = Flux2KleinWorkflow.DEFAULT_UNET;
        public const string DEFAULT_CLIP = Flux2KleinWorkflow.DEFAULT_CLIP;
        public const string DEFAULT_VAE = Flux2KleinWorkflow.DEFAULT_VAE;

        // Fixed by the user's workflow: 4 steps, cfg 1.0. No denoise input exists
        // on this reference-edit graph, so the old MIN_STEPS/EffectiveSteps
        // (min-8) helper and all denoise plumbing were removed.
        public const int KLEIN_STEPS = 4;
        public const double KLEIN_CFG = 1.0;

        public static string CreateWorkflow(
            string positivePrompt,
            string negativePrompt = "",
            string inputImageFilename = "",
            int width = 1024,
            int height = 1024,
            long? seed = null,
            string samplerName = "euler",
            string unetName = null,
            string clipName = null,
            string vaeName = null)
        {
            if (!seed.HasValue || seed.Value < 0)
                seed = new Random().Next();

            // Klein takes no negative prompt; accept the parameter for call-site
            // compat but ignore it — the negative conditioning comes from
            // ConditioningZeroOut(132) + ReferenceLatent(133) in the graph.
            positivePrompt = EscapeJson(positivePrompt ?? string.Empty);
            inputImageFilename = EscapeJson(inputImageFilename ?? string.Empty);
            unetName = EscapeJson(string.IsNullOrWhiteSpace(unetName) ? DEFAULT_UNET : unetName);
            clipName = EscapeJson(string.IsNullOrWhiteSpace(clipName) ? DEFAULT_CLIP : clipName);
            vaeName = EscapeJson(string.IsNullOrWhiteSpace(vaeName) ? DEFAULT_VAE : vaeName);
            samplerName = EscapeJson(string.IsNullOrWhiteSpace(samplerName) ? "euler" : samplerName);

            int reqW = Math.Max(16, width <= 0 ? 1024 : width);
            int reqH = Math.Max(16, height <= 0 ? 1024 : height);
            double megapixels = reqW * (double)reqH / 1000000.0;
            if (double.IsNaN(megapixels) || double.IsInfinity(megapixels))
                megapixels = 0.2;
            megapixels = Math.Max(0.1, Math.Min(1.0, megapixels));

            var inv = CultureInfo.InvariantCulture;
            string mpStr = megapixels.ToString("0.###", inv);
            string cfgStr = KLEIN_CFG.ToString("0.0", inv);

            return $@"{{
  ""76"": {{
    ""inputs"": {{
      ""image"": ""{inputImageFilename}""
    }},
    ""class_type"": ""LoadImage""
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
      ""type"": ""flux2"",
      ""device"": ""default""
    }},
    ""class_type"": ""CLIPLoader""
  }},
  ""127"": {{
    ""inputs"": {{
      ""vae_name"": ""{vaeName}""
    }},
    ""class_type"": ""VAELoader""
  }},
  ""129"": {{
    ""inputs"": {{
      ""image"": [""76"", 0],
      ""upscale_method"": ""nearest-exact"",
      ""megapixels"": {mpStr},
      ""resolution_steps"": 1
    }},
    ""class_type"": ""ImageScaleToTotalPixels""
  }},
  ""134"": {{
    ""inputs"": {{
      ""image"": [""129"", 0]
    }},
    ""class_type"": ""GetImageSize""
  }},
  ""136"": {{
    ""inputs"": {{
      ""pixels"": [""129"", 0],
      ""vae"": [""127"", 0]
    }},
    ""class_type"": ""VAEEncode""
  }},
  ""131"": {{
    ""inputs"": {{
      ""text"": ""{positivePrompt}"",
      ""clip"": [""126"", 0]
    }},
    ""class_type"": ""CLIPTextEncode""
  }},
  ""132"": {{
    ""inputs"": {{
      ""conditioning"": [""131"", 0]
    }},
    ""class_type"": ""ConditioningZeroOut""
  }},
  ""133"": {{
    ""inputs"": {{
      ""conditioning"": [""132"", 0],
      ""latent"": [""136"", 0]
    }},
    ""class_type"": ""ReferenceLatent""
  }},
  ""135"": {{
    ""inputs"": {{
      ""conditioning"": [""131"", 0],
      ""latent"": [""136"", 0]
    }},
    ""class_type"": ""ReferenceLatent""
  }},
  ""140"": {{
    ""inputs"": {{
      ""model"": [""125"", 0],
      ""positive"": [""135"", 0],
      ""negative"": [""133"", 0],
      ""cfg"": {cfgStr}
    }},
    ""class_type"": ""CFGGuider""
  }},
  ""139"": {{
    ""inputs"": {{
      ""noise_seed"": {seed.Value}
    }},
    ""class_type"": ""RandomNoise""
  }},
  ""124"": {{
    ""inputs"": {{
      ""sampler_name"": ""{samplerName}""
    }},
    ""class_type"": ""KSamplerSelect""
  }},
  ""130"": {{
    ""inputs"": {{
      ""steps"": {KLEIN_STEPS},
      ""width"": [""134"", 0],
      ""height"": [""134"", 1]
    }},
    ""class_type"": ""Flux2Scheduler""
  }},
  ""128"": {{
    ""inputs"": {{
      ""width"": [""134"", 0],
      ""height"": [""134"", 1],
      ""batch_size"": 1
    }},
    ""class_type"": ""EmptyFlux2LatentImage""
  }},
  ""137"": {{
    ""inputs"": {{
      ""noise"": [""139"", 0],
      ""guider"": [""140"", 0],
      ""sampler"": [""124"", 0],
      ""sigmas"": [""130"", 0],
      ""latent_image"": [""128"", 0]
    }},
    ""class_type"": ""SamplerCustomAdvanced""
  }},
  ""138"": {{
    ""inputs"": {{
      ""samples"": [""137"", 0],
      ""vae"": [""127"", 0]
    }},
    ""class_type"": ""VAEDecode""
  }},
  ""9"": {{
    ""inputs"": {{
      ""filename_prefix"": ""Flux2-Klein"",
      ""images"": [""138"", 0]
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
