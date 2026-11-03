using System;
using System.Globalization;

namespace MeesaMultisMaker.Audio
{
    /// <summary>
    /// Built-in frozen copies of the remaining Qwen3 voice ComfyUI workflows
    /// as API-format (QueuePrompt-ready) JSON builders. No file I/O: the
    /// graphs the UI-format files describe are emitted directly, with text /
    /// instruct / language / seed / ref-audio / prompt-maker fields injected.
    ///
    /// Freeze sources (D:\ComfyUIPortable\ComfyUI\user\default\workflows\):
    /// - 9SecondVoiceCloneFromSample.json and PERFECT_VOICE_CLONE.json: same
    ///   7-node voice-clone graph as TEXT2SPEACHWORKINGFASTQWEN.json
    ///   (Qwen3Loader, Qwen3ASRLoader, Qwen3ASRTranscribe, LoadAudio,
    ///   Qwen3VoiceClone, PreviewAudio, PreviewAny); only the template ref
    ///   clip differs (abby5.mp3 vs DIABLO.mp3).
    /// - PERFECT_VOICE_DESIGN.json: 4-node voice-design graph (Qwen3Loader
    ///   with the VoiceDesign repo, Qwen3VoiceDesign, PreviewAudio,
    ///   JarJarAudioSaveAudio with an absolute local file_path).
    /// - QWEN_VOICE_GENERATOR_25_SECONDS.json: 6-node design-to-prompt graph
    ///   (VoiceDesign loader + Qwen3VoiceDesign + PreviewAudio, Base loader
    ///   + Qwen3PromptMaker fed by the design audio + Qwen3SavePrompt).
    /// - VOICE_DESIGN_EXAMPLE_AND_LOADER.json: 10-node combo graph (the 25s
    ///   design/prompt-maker/save chain plus a Base loader + Qwen3LoadPrompt
    ///   + prompt-fed Qwen3VoiceClone + PreviewAudio).
    ///
    /// Post-patch semantics mirror Qwen3VoiceCloneWorkflow: PreviewAny
    /// viewers are never emitted; the absolute-path JarJarAudioSaveAudio disk
    /// sink is never emitted; text/seed/language land on the voice nodes and
    /// the server-side ref clip lands on LoadAudio; a mm_savewav SaveAudio
    /// sink (mm_audio prefix) is always present.
    /// </summary>

    /// <summary>
    /// Built-in frozen copy of 9SecondVoiceCloneFromSample.json (7 UI nodes)
    /// as an API-format builder. Emits 7 nodes: 6/1/2/4/9/7 + mm_savewav;
    /// PreviewAny dropped. Template ref clip: abby5.mp3.
    /// </summary>
    public static class NineSecondCloneWorkflow
    {
        // Model repos frozen from nodes 6 (Qwen3Loader) and 1 (Qwen3ASRLoader).
        public const string DEFAULT_TTS_REPO = "Qwen/Qwen3-TTS-12Hz-1.7B-Base";
        public const string DEFAULT_ASR_REPO = "Qwen/Qwen3-ASR-1.7B";
        // Template default ref clip (used when no server-side clip is set).
        public const string DEFAULT_REF_AUDIO = "abby5.mp3";
        public const string DEFAULT_LANGUAGE = "Auto";
        public const string DEFAULT_PRECISION = "bf16";
        public const string DEFAULT_ATTENTION = "auto";
        public const int DEFAULT_MAX_NEW_TOKENS = 2048;
        public const int DEFAULT_REF_AUDIO_MAX_SECONDS = 30;
        /// <summary>Node count of the emitted graph.</summary>
        public const int NODE_COUNT = 7;

        /// <summary>Build a 9-second-sample voice-clone API-format prompt.</summary>
        public static string CreateWorkflow(
            string text,
            string language = DEFAULT_LANGUAGE,
            string refAudioServerName = null,
            long seed = -1,
            int maxNewTokens = DEFAULT_MAX_NEW_TOKENS,
            int refAudioMaxSeconds = DEFAULT_REF_AUDIO_MAX_SECONDS)
        {
            if (string.IsNullOrEmpty(text)) text = "Hello.";
            if (string.IsNullOrEmpty(language)) language = DEFAULT_LANGUAGE;
            if (string.IsNullOrEmpty(refAudioServerName)) refAudioServerName = DEFAULT_REF_AUDIO;
            if (seed < 0) seed = RandomSeed();
            if (maxNewTokens < 1) maxNewTokens = DEFAULT_MAX_NEW_TOKENS;
            if (refAudioMaxSeconds < 1) refAudioMaxSeconds = DEFAULT_REF_AUDIO_MAX_SECONDS;

            var inv = CultureInfo.InvariantCulture;
            string t = EscapeJson(text);
            string l = EscapeJson(language);
            string r = EscapeJson(refAudioServerName);

            return
                "{\n" +
                "  \"6\": {\n" +
                "    \"inputs\": {\n" +
                "      \"repo_id\": \"" + DEFAULT_TTS_REPO + "\",\n" +
                "      \"source\": \"HuggingFace\",\n" +
                "      \"precision\": \"" + DEFAULT_PRECISION + "\",\n" +
                "      \"attention\": \"" + DEFAULT_ATTENTION + "\",\n" +
                "      \"local_model_path\": \"\"\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3Loader\"\n" +
                "  },\n" +
                "  \"1\": {\n" +
                "    \"inputs\": {\n" +
                "      \"repo_id\": \"" + DEFAULT_ASR_REPO + "\",\n" +
                "      \"source\": \"HuggingFace\",\n" +
                "      \"precision\": \"" + DEFAULT_PRECISION + "\",\n" +
                "      \"attention\": \"" + DEFAULT_ATTENTION + "\",\n" +
                "      \"forced_aligner\": \"None\",\n" +
                "      \"local_model_path\": \"\"\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3ASRLoader\"\n" +
                "  },\n" +
                "  \"2\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"1\", 0],\n" +
                "      \"audio\": [\"4\", 0],\n" +
                "      \"language\": \"auto\",\n" +
                "      \"context\": \"\",\n" +
                "      \"return_timestamps\": false\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3ASRTranscribe\"\n" +
                "  },\n" +
                "  \"4\": {\n" +
                "    \"inputs\": {\n" +
                "      \"audio\": \"" + r + "\",\n" +
                "      \"audioUI\": null\n" +
                "    },\n" +
                "    \"class_type\": \"LoadAudio\"\n" +
                "  },\n" +
                "  \"9\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"6\", 0],\n" +
                "      \"ref_audio\": [\"4\", 0],\n" +
                "      \"text\": \"" + t + "\",\n" +
                "      \"seed\": " + seed.ToString(inv) + ",\n" +
                "      \"language\": \"" + l + "\",\n" +
                "      \"ref_text\": [\"2\", 0],\n" +
                "      \"max_new_tokens\": " + maxNewTokens.ToString(inv) + ",\n" +
                "      \"ref_audio_max_seconds\": " + refAudioMaxSeconds.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3VoiceClone\"\n" +
                "  },\n" +
                "  \"7\": {\n" +
                "    \"inputs\": {\n" +
                "      \"audio\": [\"9\", 0],\n" +
                "      \"audioUI\": null\n" +
                "    },\n" +
                "    \"class_type\": \"PreviewAudio\"\n" +
                "  },\n" +
                "  \"mm_savewav\": {\n" +
                "    \"inputs\": {\n" +
                "      \"filename_prefix\": \"mm_audio\",\n" +
                "      \"audio\": [\"9\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"SaveAudio\"\n" +
                "  }\n" +
                "}";
        }

        private static long RandomSeed()
        {
            var rnd = new Random();
            long s = ((long)(uint)rnd.Next() << 32) | (long)(uint)rnd.Next();
            return s < 0 ? -s : s;
        }

        private static string EscapeJson(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Built-in frozen copy of PERFECT_VOICE_CLONE.json (7 UI nodes) as an
    /// API-format builder. Emits 7 nodes: 6/1/2/4/9/7 + mm_savewav;
    /// PreviewAny dropped. Template ref clip: DIABLO.mp3.
    /// </summary>
    public static class PerfectVoiceCloneWorkflow
    {
        // Model repos frozen from nodes 6 (Qwen3Loader) and 1 (Qwen3ASRLoader).
        public const string DEFAULT_TTS_REPO = "Qwen/Qwen3-TTS-12Hz-1.7B-Base";
        public const string DEFAULT_ASR_REPO = "Qwen/Qwen3-ASR-1.7B";
        // Template default ref clip (used when no server-side clip is set).
        public const string DEFAULT_REF_AUDIO = "DIABLO.mp3";
        public const string DEFAULT_LANGUAGE = "Auto";
        public const string DEFAULT_PRECISION = "bf16";
        public const string DEFAULT_ATTENTION = "auto";
        public const int DEFAULT_MAX_NEW_TOKENS = 2048;
        public const int DEFAULT_REF_AUDIO_MAX_SECONDS = 30;
        /// <summary>Node count of the emitted graph.</summary>
        public const int NODE_COUNT = 7;

        /// <summary>Build a perfect voice-clone API-format prompt.</summary>
        public static string CreateWorkflow(
            string text,
            string language = DEFAULT_LANGUAGE,
            string refAudioServerName = null,
            long seed = -1,
            int maxNewTokens = DEFAULT_MAX_NEW_TOKENS,
            int refAudioMaxSeconds = DEFAULT_REF_AUDIO_MAX_SECONDS)
        {
            if (string.IsNullOrEmpty(text)) text = "Hello.";
            if (string.IsNullOrEmpty(language)) language = DEFAULT_LANGUAGE;
            if (string.IsNullOrEmpty(refAudioServerName)) refAudioServerName = DEFAULT_REF_AUDIO;
            if (seed < 0) seed = RandomSeed();
            if (maxNewTokens < 1) maxNewTokens = DEFAULT_MAX_NEW_TOKENS;
            if (refAudioMaxSeconds < 1) refAudioMaxSeconds = DEFAULT_REF_AUDIO_MAX_SECONDS;

            var inv = CultureInfo.InvariantCulture;
            string t = EscapeJson(text);
            string l = EscapeJson(language);
            string r = EscapeJson(refAudioServerName);

            return
                "{\n" +
                "  \"6\": {\n" +
                "    \"inputs\": {\n" +
                "      \"repo_id\": \"" + DEFAULT_TTS_REPO + "\",\n" +
                "      \"source\": \"HuggingFace\",\n" +
                "      \"precision\": \"" + DEFAULT_PRECISION + "\",\n" +
                "      \"attention\": \"" + DEFAULT_ATTENTION + "\",\n" +
                "      \"local_model_path\": \"\"\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3Loader\"\n" +
                "  },\n" +
                "  \"1\": {\n" +
                "    \"inputs\": {\n" +
                "      \"repo_id\": \"" + DEFAULT_ASR_REPO + "\",\n" +
                "      \"source\": \"HuggingFace\",\n" +
                "      \"precision\": \"" + DEFAULT_PRECISION + "\",\n" +
                "      \"attention\": \"" + DEFAULT_ATTENTION + "\",\n" +
                "      \"forced_aligner\": \"None\",\n" +
                "      \"local_model_path\": \"\"\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3ASRLoader\"\n" +
                "  },\n" +
                "  \"2\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"1\", 0],\n" +
                "      \"audio\": [\"4\", 0],\n" +
                "      \"language\": \"auto\",\n" +
                "      \"context\": \"\",\n" +
                "      \"return_timestamps\": false\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3ASRTranscribe\"\n" +
                "  },\n" +
                "  \"4\": {\n" +
                "    \"inputs\": {\n" +
                "      \"audio\": \"" + r + "\",\n" +
                "      \"audioUI\": null\n" +
                "    },\n" +
                "    \"class_type\": \"LoadAudio\"\n" +
                "  },\n" +
                "  \"9\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"6\", 0],\n" +
                "      \"ref_audio\": [\"4\", 0],\n" +
                "      \"text\": \"" + t + "\",\n" +
                "      \"seed\": " + seed.ToString(inv) + ",\n" +
                "      \"language\": \"" + l + "\",\n" +
                "      \"ref_text\": [\"2\", 0],\n" +
                "      \"max_new_tokens\": " + maxNewTokens.ToString(inv) + ",\n" +
                "      \"ref_audio_max_seconds\": " + refAudioMaxSeconds.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3VoiceClone\"\n" +
                "  },\n" +
                "  \"7\": {\n" +
                "    \"inputs\": {\n" +
                "      \"audio\": [\"9\", 0],\n" +
                "      \"audioUI\": null\n" +
                "    },\n" +
                "    \"class_type\": \"PreviewAudio\"\n" +
                "  },\n" +
                "  \"mm_savewav\": {\n" +
                "    \"inputs\": {\n" +
                "      \"filename_prefix\": \"mm_audio\",\n" +
                "      \"audio\": [\"9\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"SaveAudio\"\n" +
                "  }\n" +
                "}";
        }

        private static long RandomSeed()
        {
            var rnd = new Random();
            long s = ((long)(uint)rnd.Next() << 32) | (long)(uint)rnd.Next();
            return s < 0 ? -s : s;
        }

        private static string EscapeJson(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Built-in frozen copy of PERFECT_VOICE_DESIGN.json (4 UI nodes:
    /// Qwen3Loader with the VoiceDesign repo, Qwen3VoiceDesign,
    /// PreviewAudio, JarJarAudioSaveAudio) as an API-format builder.
    /// Emits 4 nodes: 1/9/18 + mm_savewav; the absolute-path JarJar disk
    /// sink is dropped in favor of the mm_ saver, mirroring BuildVoiceDesign
    /// (loader + designer + saver) semantics.
    /// </summary>
    public static class PerfectVoiceDesignWorkflow
    {
        // Voice-design model repo frozen from node 1 (Qwen3Loader).
        public const string DEFAULT_DESIGN_REPO = "Qwen/Qwen3-TTS-12Hz-1.7B-VoiceDesign";
        public const string DEFAULT_LANGUAGE = "English";
        public const string DEFAULT_PRECISION = "bf16";
        public const string DEFAULT_ATTENTION = "auto";
        /// <summary>Node count of the emitted graph.</summary>
        public const int NODE_COUNT = 4;

        /// <summary>
        /// Build a voice-DESIGN API-format prompt (no reference audio: the
        /// voice is invented from the instruct description).
        /// </summary>
        public static string CreateWorkflow(
            string text,
            string instruct,
            string language = DEFAULT_LANGUAGE,
            long seed = -1)
        {
            if (string.IsNullOrEmpty(text)) text = "Hello.";
            if (string.IsNullOrEmpty(instruct)) instruct = "A natural narrator voice.";
            if (string.IsNullOrEmpty(language)) language = DEFAULT_LANGUAGE;
            if (seed < 0) seed = RandomSeed();

            var inv = CultureInfo.InvariantCulture;
            string t = EscapeJson(text);
            string n = EscapeJson(instruct);
            string l = EscapeJson(language);

            return
                "{\n" +
                "  \"1\": {\n" +
                "    \"inputs\": {\n" +
                "      \"repo_id\": \"" + DEFAULT_DESIGN_REPO + "\",\n" +
                "      \"source\": \"HuggingFace\",\n" +
                "      \"precision\": \"" + DEFAULT_PRECISION + "\",\n" +
                "      \"attention\": \"" + DEFAULT_ATTENTION + "\",\n" +
                "      \"local_model_path\": \"\"\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3Loader\"\n" +
                "  },\n" +
                "  \"9\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"1\", 0],\n" +
                "      \"text\": \"" + t + "\",\n" +
                "      \"instruct\": \"" + n + "\",\n" +
                "      \"language\": \"" + l + "\",\n" +
                "      \"seed\": " + seed.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3VoiceDesign\"\n" +
                "  },\n" +
                "  \"18\": {\n" +
                "    \"inputs\": {\n" +
                "      \"audio\": [\"9\", 0],\n" +
                "      \"audioUI\": null\n" +
                "    },\n" +
                "    \"class_type\": \"PreviewAudio\"\n" +
                "  },\n" +
                "  \"mm_savewav\": {\n" +
                "    \"inputs\": {\n" +
                "      \"filename_prefix\": \"mm_audio\",\n" +
                "      \"audio\": [\"9\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"SaveAudio\"\n" +
                "  }\n" +
                "}";
        }

        private static long RandomSeed()
        {
            var rnd = new Random();
            long s = ((long)(uint)rnd.Next() << 32) | (long)(uint)rnd.Next();
            return s < 0 ? -s : s;
        }

        private static string EscapeJson(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Built-in frozen copy of QWEN_VOICE_GENERATOR_25_SECONDS.json (6 UI
    /// nodes) as an API-format builder. The graph designs a voice
    /// (VoiceDesign loader 1 + Qwen3VoiceDesign 9 + PreviewAudio 10), then
    /// distills the design audio into a reusable voice prompt (Base loader 6
    /// + Qwen3PromptMaker 5 fed by the design audio + Qwen3SavePrompt 11).
    /// Emits 7 nodes: 1/9/10/6/5/11 + mm_savewav on the design audio.
    /// </summary>
    public static class Qwen25SecWorkflow
    {
        // Model repos frozen from nodes 1 (VoiceDesign loader) and 6 (Base loader).
        public const string DEFAULT_DESIGN_REPO = "Qwen/Qwen3-TTS-12Hz-1.7B-VoiceDesign";
        public const string DEFAULT_BASE_REPO = "Qwen/Qwen3-TTS-12Hz-1.7B-Base";
        public const string DEFAULT_LANGUAGE = "English";
        public const string DEFAULT_PRECISION = "bf16";
        public const string DEFAULT_ATTENTION = "auto";
        // SavePrompt filename frozen from node 11 (Qwen3SavePrompt).
        public const string DEFAULT_PROMPT_NAME = "old_man";
        public const int DEFAULT_REF_AUDIO_MAX_SECONDS = 30;
        /// <summary>Node count of the emitted graph.</summary>
        public const int NODE_COUNT = 7;

        /// <summary>
        /// Build a design-then-distill API-format prompt. refText feeds the
        /// prompt-maker transcript (defaults to the design text); promptName
        /// is the SavePrompt filename for the distilled voice prompt.
        /// </summary>
        public static string CreateWorkflow(
            string text,
            string instruct,
            string language = DEFAULT_LANGUAGE,
            long seed = -1,
            string promptName = DEFAULT_PROMPT_NAME,
            string refText = null,
            int refAudioMaxSeconds = DEFAULT_REF_AUDIO_MAX_SECONDS)
        {
            if (string.IsNullOrEmpty(text)) text = "Hello.";
            if (string.IsNullOrEmpty(instruct)) instruct = "A natural narrator voice.";
            if (string.IsNullOrEmpty(language)) language = DEFAULT_LANGUAGE;
            if (seed < 0) seed = RandomSeed();
            if (string.IsNullOrEmpty(promptName)) promptName = DEFAULT_PROMPT_NAME;
            if (refText == null) refText = text;
            if (refAudioMaxSeconds < 1) refAudioMaxSeconds = DEFAULT_REF_AUDIO_MAX_SECONDS;

            var inv = CultureInfo.InvariantCulture;
            string t = EscapeJson(text);
            string n = EscapeJson(instruct);
            string l = EscapeJson(language);
            string p = EscapeJson(promptName);
            string f = EscapeJson(refText);

            return
                "{\n" +
                "  \"1\": {\n" +
                "    \"inputs\": {\n" +
                "      \"repo_id\": \"" + DEFAULT_DESIGN_REPO + "\",\n" +
                "      \"source\": \"HuggingFace\",\n" +
                "      \"precision\": \"" + DEFAULT_PRECISION + "\",\n" +
                "      \"attention\": \"" + DEFAULT_ATTENTION + "\",\n" +
                "      \"local_model_path\": \"\"\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3Loader\"\n" +
                "  },\n" +
                "  \"9\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"1\", 0],\n" +
                "      \"text\": \"" + t + "\",\n" +
                "      \"instruct\": \"" + n + "\",\n" +
                "      \"language\": \"" + l + "\",\n" +
                "      \"seed\": " + seed.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3VoiceDesign\"\n" +
                "  },\n" +
                "  \"10\": {\n" +
                "    \"inputs\": {\n" +
                "      \"audio\": [\"9\", 0],\n" +
                "      \"audioUI\": null\n" +
                "    },\n" +
                "    \"class_type\": \"PreviewAudio\"\n" +
                "  },\n" +
                "  \"6\": {\n" +
                "    \"inputs\": {\n" +
                "      \"repo_id\": \"" + DEFAULT_BASE_REPO + "\",\n" +
                "      \"source\": \"HuggingFace\",\n" +
                "      \"precision\": \"" + DEFAULT_PRECISION + "\",\n" +
                "      \"attention\": \"" + DEFAULT_ATTENTION + "\",\n" +
                "      \"local_model_path\": \"\"\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3Loader\"\n" +
                "  },\n" +
                "  \"5\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"6\", 0],\n" +
                "      \"ref_audio\": [\"9\", 0],\n" +
                "      \"ref_text\": \"" + f + "\",\n" +
                "      \"ref_audio_max_seconds\": " + refAudioMaxSeconds.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3PromptMaker\"\n" +
                "  },\n" +
                "  \"11\": {\n" +
                "    \"inputs\": {\n" +
                "      \"prompt\": [\"5\", 0],\n" +
                "      \"filename\": \"" + p + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3SavePrompt\"\n" +
                "  },\n" +
                "  \"mm_savewav\": {\n" +
                "    \"inputs\": {\n" +
                "      \"filename_prefix\": \"mm_audio\",\n" +
                "      \"audio\": [\"9\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"SaveAudio\"\n" +
                "  }\n" +
                "}";
        }

        private static long RandomSeed()
        {
            var rnd = new Random();
            long s = ((long)(uint)rnd.Next() << 32) | (long)(uint)rnd.Next();
            return s < 0 ? -s : s;
        }

        private static string EscapeJson(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }

    /// <summary>
    /// Built-in frozen copy of VOICE_DESIGN_EXAMPLE_AND_LOADER.json (10 UI
    /// nodes) as an API-format builder. The graph combines the 25s
    /// design/prompt-maker/save chain (loaders 1/6, design 9, prompt-maker 5,
    /// save-prompt 11, preview 10) with a prompt-fed clone branch (Base
    /// loader 12 + Qwen3LoadPrompt 13 reading the saved prompt file +
    /// Qwen3VoiceClone 14 + PreviewAudio 15). The clone node takes the
    /// prompt link instead of ref_audio/ref_text wiring. Emits 12 nodes:
    /// the 10 kept nodes + mm_savewav (design audio) + mm_savewav_clone
    /// (clone audio).
    /// </summary>
    public static class VoiceDesignExampleWorkflow
    {
        // Model repos frozen from nodes 1 (VoiceDesign), 6 and 12 (Base).
        public const string DEFAULT_DESIGN_REPO = "Qwen/Qwen3-TTS-12Hz-1.7B-VoiceDesign";
        public const string DEFAULT_BASE_REPO = "Qwen/Qwen3-TTS-12Hz-1.7B-Base";
        public const string DEFAULT_DESIGN_LANGUAGE = "English";
        public const string DEFAULT_CLONE_LANGUAGE = "English";
        public const string DEFAULT_PRECISION = "bf16";
        public const string DEFAULT_ATTENTION = "auto";
        // Prompt filename frozen from nodes 11 (Qwen3SavePrompt) and 13
        // (Qwen3LoadPrompt reads the same file with its stored extension).
        public const string DEFAULT_PROMPT_NAME = "old_man";
        public const int DEFAULT_MAX_NEW_TOKENS = 2048;
        public const int DEFAULT_REF_AUDIO_MAX_SECONDS = 30;
        /// <summary>Node count of the emitted graph.</summary>
        public const int NODE_COUNT = 12;

        /// <summary>
        /// Build a design + prompt-distill + prompt-fed-clone API-format
        /// prompt. The save/load prompt filename is shared (promptName, with
        /// the loader reading promptName + ".safetensors"); promptRefText
        /// feeds the prompt-maker transcript (defaults to the design text).
        /// </summary>
        public static string CreateWorkflow(
            string designText,
            string instruct,
            string cloneText,
            string designLanguage = DEFAULT_DESIGN_LANGUAGE,
            string cloneLanguage = DEFAULT_CLONE_LANGUAGE,
            long designSeed = -1,
            long cloneSeed = -1,
            int maxNewTokens = DEFAULT_MAX_NEW_TOKENS,
            int refAudioMaxSeconds = DEFAULT_REF_AUDIO_MAX_SECONDS,
            string promptName = DEFAULT_PROMPT_NAME,
            string promptRefText = null)
        {
            if (string.IsNullOrEmpty(designText)) designText = "Hello.";
            if (string.IsNullOrEmpty(instruct)) instruct = "A natural narrator voice.";
            if (string.IsNullOrEmpty(cloneText)) cloneText = "Hello.";
            if (string.IsNullOrEmpty(designLanguage)) designLanguage = DEFAULT_DESIGN_LANGUAGE;
            if (string.IsNullOrEmpty(cloneLanguage)) cloneLanguage = DEFAULT_CLONE_LANGUAGE;
            if (designSeed < 0) designSeed = RandomSeed();
            if (cloneSeed < 0) cloneSeed = RandomSeed();
            if (maxNewTokens < 1) maxNewTokens = DEFAULT_MAX_NEW_TOKENS;
            if (refAudioMaxSeconds < 1) refAudioMaxSeconds = DEFAULT_REF_AUDIO_MAX_SECONDS;
            if (string.IsNullOrEmpty(promptName)) promptName = DEFAULT_PROMPT_NAME;
            if (promptRefText == null) promptRefText = designText;

            var inv = CultureInfo.InvariantCulture;
            string dt = EscapeJson(designText);
            string ni = EscapeJson(instruct);
            string dl = EscapeJson(designLanguage);
            string ct = EscapeJson(cloneText);
            string cl = EscapeJson(cloneLanguage);
            string pn = EscapeJson(promptName);
            string pf = EscapeJson(promptName + ".safetensors");
            string pr = EscapeJson(promptRefText);

            return
                "{\n" +
                "  \"1\": {\n" +
                "    \"inputs\": {\n" +
                "      \"repo_id\": \"" + DEFAULT_DESIGN_REPO + "\",\n" +
                "      \"source\": \"HuggingFace\",\n" +
                "      \"precision\": \"" + DEFAULT_PRECISION + "\",\n" +
                "      \"attention\": \"" + DEFAULT_ATTENTION + "\",\n" +
                "      \"local_model_path\": \"\"\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3Loader\"\n" +
                "  },\n" +
                "  \"9\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"1\", 0],\n" +
                "      \"text\": \"" + dt + "\",\n" +
                "      \"instruct\": \"" + ni + "\",\n" +
                "      \"language\": \"" + dl + "\",\n" +
                "      \"seed\": " + designSeed.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3VoiceDesign\"\n" +
                "  },\n" +
                "  \"10\": {\n" +
                "    \"inputs\": {\n" +
                "      \"audio\": [\"9\", 0],\n" +
                "      \"audioUI\": null\n" +
                "    },\n" +
                "    \"class_type\": \"PreviewAudio\"\n" +
                "  },\n" +
                "  \"6\": {\n" +
                "    \"inputs\": {\n" +
                "      \"repo_id\": \"" + DEFAULT_BASE_REPO + "\",\n" +
                "      \"source\": \"HuggingFace\",\n" +
                "      \"precision\": \"" + DEFAULT_PRECISION + "\",\n" +
                "      \"attention\": \"" + DEFAULT_ATTENTION + "\",\n" +
                "      \"local_model_path\": \"\"\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3Loader\"\n" +
                "  },\n" +
                "  \"5\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"6\", 0],\n" +
                "      \"ref_audio\": [\"9\", 0],\n" +
                "      \"ref_text\": \"" + pr + "\",\n" +
                "      \"ref_audio_max_seconds\": " + refAudioMaxSeconds.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3PromptMaker\"\n" +
                "  },\n" +
                "  \"11\": {\n" +
                "    \"inputs\": {\n" +
                "      \"prompt\": [\"5\", 0],\n" +
                "      \"filename\": \"" + pn + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3SavePrompt\"\n" +
                "  },\n" +
                "  \"12\": {\n" +
                "    \"inputs\": {\n" +
                "      \"repo_id\": \"" + DEFAULT_BASE_REPO + "\",\n" +
                "      \"source\": \"HuggingFace\",\n" +
                "      \"precision\": \"" + DEFAULT_PRECISION + "\",\n" +
                "      \"attention\": \"" + DEFAULT_ATTENTION + "\",\n" +
                "      \"local_model_path\": \"\"\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3Loader\"\n" +
                "  },\n" +
                "  \"13\": {\n" +
                "    \"inputs\": {\n" +
                "      \"prompt_file\": \"" + pf + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3LoadPrompt\"\n" +
                "  },\n" +
                "  \"14\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"12\", 0],\n" +
                "      \"prompt\": [\"13\", 0],\n" +
                "      \"text\": \"" + ct + "\",\n" +
                "      \"seed\": " + cloneSeed.ToString(inv) + ",\n" +
                "      \"language\": \"" + cl + "\",\n" +
                "      \"max_new_tokens\": " + maxNewTokens.ToString(inv) + ",\n" +
                "      \"ref_audio_max_seconds\": " + refAudioMaxSeconds.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"Qwen3VoiceClone\"\n" +
                "  },\n" +
                "  \"15\": {\n" +
                "    \"inputs\": {\n" +
                "      \"audio\": [\"14\", 0],\n" +
                "      \"audioUI\": null\n" +
                "    },\n" +
                "    \"class_type\": \"PreviewAudio\"\n" +
                "  },\n" +
                "  \"mm_savewav\": {\n" +
                "    \"inputs\": {\n" +
                "      \"filename_prefix\": \"mm_audio\",\n" +
                "      \"audio\": [\"9\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"SaveAudio\"\n" +
                "  },\n" +
                "  \"mm_savewav_clone\": {\n" +
                "    \"inputs\": {\n" +
                "      \"filename_prefix\": \"mm_audio_clone\",\n" +
                "      \"audio\": [\"14\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"SaveAudio\"\n" +
                "  }\n" +
                "}";
        }

        private static long RandomSeed()
        {
            var rnd = new Random();
            long s = ((long)(uint)rnd.Next() << 32) | (long)(uint)rnd.Next();
            return s < 0 ? -s : s;
        }

        private static string EscapeJson(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }
    }
}
