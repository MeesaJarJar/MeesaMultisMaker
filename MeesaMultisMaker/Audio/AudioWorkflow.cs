using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace MeesaMultisMaker.Audio
{
    /// <summary>
    /// ComfyUI audio graph builder (SFX / music / voice-clone TTS).
    ///
    /// Graphs are built in (StableAudioWorkflow / Qwen3VoiceCloneWorkflow):
    /// frozen copies of the ComfyUI workflows as QueuePrompt-ready API
    /// JSON, with prompt / seed / duration / steps / text / ref-audio
    /// injected by those builders. No template file is read.
    ///
    /// Disk-only sink nodes with absolute local paths (JarJarAudioSaveAudio)
    /// are not emitted: they break remote servers and never appear in
    /// history. PreviewAny viewers are not emitted either. History-visible
    /// savers (SaveAudio*, PreviewAudio) are kept, plus the mm_savewav
    /// SaveAudio sink the graphs always gain.
    /// </summary>
    public static class AudioWorkflow
    {
        public class BuildResult
        {
            public string PromptJson;
            public long Seed;
            public string FormatNote;
            /// <summary>Short id stamped on every saver prefix (Errno 22
            /// guard: template filename builders inherit prompt text).</summary>
            public string ServerPrefix = string.Empty;
        }

        private static readonly string[] AudioExts =
            new string[] { ".wav", ".mp3", ".flac", ".ogg", ".opus" };

        public static string[] OutputExtensions()
        {
            return (string[])AudioExts.Clone();
        }


        private static long RandomSeed()
        {
            var rnd = new Random();
            long s = ((long)(uint)rnd.Next() << 32) | (long)(uint)rnd.Next();
            return s < 0 ? -s : s;
        }

        #region SFX / music (built-in Stable Audio graph)

        /// <summary>
        /// Build a text-to-audio run from the built-in Stable Audio graph
        /// (no template file). SFX categories emit the FASTAUDIOTGEN layout,
        /// anything else the WORKINGAUDIO layout; prompt/negative/duration/
        /// seed/steps are stamped by the builder.
        /// </summary>
        public static BuildResult BuildTextToAudio(string prompt,
            double durationSec, long seed, int steps, bool useReprompt = false,
            string categoryOverride = null, string negativePrompt = null)
        {
            if (string.IsNullOrEmpty(prompt))
                throw new Exception("Enter a prompt first.");
            if (durationSec < 1) durationSec = 1;
            if (durationSec > 600) durationSec = 600;
            if (seed < 0) seed = RandomSeed();
            if (steps < 1) steps = 1;

            string category = string.IsNullOrEmpty(categoryOverride) ? "Music" : categoryOverride;
            string serverPrefix = NewAudioId(category);
            string promptJson = StableAudioWorkflow.CreateWorkflow(
                prompt, negativePrompt ?? string.Empty, durationSec, seed, steps,
                useReprompt, category, serverPrefix);
            bool sfx = StableAudioWorkflow.IsSfxStyle(category);
            int count = StableAudioWorkflow.NodeCount(useReprompt, sfx);

            return new BuildResult
            {
                PromptJson = promptJson,
                Seed = seed,
                FormatNote = "audio (" + count + " nodes" + (useReprompt ? ", reprompt on" : ", reprompt off") + ")",
                ServerPrefix = serverPrefix
            };
        }

        /// <summary>
        /// Legacy template-file overload. The file is no longer read: the
        /// built-in graph is used instead. The layout (SFX vs Music) is
        /// inferred from the old template filename unless categoryOverride
        /// is given, so existing callers keep their previous behavior.
        /// </summary>
        [Obsolete("Audio workflows are built in; templatePath is ignored. Use BuildTextToAudio(prompt, durationSec, seed, steps, useReprompt, categoryOverride, negativePrompt) instead.")]
        public static BuildResult BuildTextToAudio(string templatePath, string prompt,
            double durationSec, long seed, int steps, bool useReprompt = false,
            string categoryOverride = null, string negativePrompt = null)
        {
            if (string.IsNullOrEmpty(templatePath))
                throw new Exception("Workflow file is not set.");
            string category = categoryOverride;
            if (string.IsNullOrEmpty(category))
                category = IsSfxTemplatePath(templatePath) ? "SFX" : "Music";
            return BuildTextToAudio(prompt, durationSec, seed, steps,
                useReprompt, category, negativePrompt);
        }

        /// <summary>
        /// Layout inference for the legacy overload: FASTAUDIOTGEN callers
        /// keep the SFX (SaveAudioAdvanced) layout, everything else keeps
        /// the WORKINGAUDIO (Music) layout.
        /// </summary>
        private static bool IsSfxTemplatePath(string templatePath)
        {
            try
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(templatePath);
                if (string.IsNullOrEmpty(name)) return false;
                return name.IndexOf("SFX", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("FAST", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// Replace LINKED saver filename_prefix inputs (prompt-derived via
        /// StringReplace chains) with a short literal id, and truncate
        /// absurd literal prefixes. Author literals (mm_audio) pass through.
        /// </summary>
        public static int PinSaverPrefixes(Dictionary<string, object> api, string prefix)
        {
            int pinned = 0;
            if (api == null || string.IsNullOrEmpty(prefix)) return pinned;
            foreach (var kv in api)
            {
                var node = kv.Value as Dictionary<string, object>;
                if (node == null) continue;
                string ct = node.ContainsKey("class_type") ? node["class_type"] as string : null;
                if (ct == null || !AudioSinkClasses.Contains(ct)) continue;
                var inputs = node.ContainsKey("inputs") ? node["inputs"] as Dictionary<string, object> : null;
                if (inputs == null || !inputs.ContainsKey("filename_prefix")) continue;
                object cur = inputs["filename_prefix"];
                if (cur is IList)
                {
                    inputs["filename_prefix"] = prefix;
                    pinned++;
                }
                else
                {
                    string s = cur as string;
                    if (!string.IsNullOrEmpty(s) && s.Length > 80)
                    {
                        inputs["filename_prefix"] = prefix;
                        pinned++;
                    }
                }
            }
            return pinned;
        }

        private static readonly HashSet<string> AudioSinkClasses = new HashSet<string>(StringComparer.Ordinal)
        {
            "SaveAudio", "SaveAudioMP3", "SaveAudioAdvanced", "SaveAudioOpus",
            "SaveAudioFLAC", "SaveAudioOGG", "PreviewAudio"
        };

        #endregion

        #region Voice-clone TTS (built-in Qwen3 graph)

        /// <summary>
        /// Build a voice-clone run from the built-in Qwen3 graph (no
        /// template file): text into Qwen3VoiceClone, optional server-side
        /// ref clip into LoadAudio (else the template default).
        /// </summary>
        public static BuildResult BuildVoiceClone(string text,
            long seed, string refAudioServerName, string language)
        {
            if (string.IsNullOrEmpty(text))
                throw new Exception("Enter text to speak first.");
            if (seed < 0) seed = RandomSeed();
            if (string.IsNullOrEmpty(language)) language = "Auto";

            return new BuildResult
            {
                PromptJson = Qwen3VoiceCloneWorkflow.CreateWorkflow(
                    text, language, refAudioServerName, seed),
                Seed = seed,
                FormatNote = "voice-clone (7 nodes)"
            };
        }

        /// <summary>
        /// Legacy template-file overload. The file is no longer read: the
        /// built-in Qwen3 graph is used instead.
        /// </summary>
        [Obsolete("Audio workflows are built in; templatePath is ignored. Use BuildVoiceClone(text, seed, refAudioServerName, language) instead.")]
        public static BuildResult BuildVoiceClone(string templatePath, string text,
            long seed, string refAudioServerName, string language)
        {
            if (string.IsNullOrEmpty(templatePath))
                throw new Exception("Workflow file is not set.");
            return BuildVoiceClone(text, seed, refAudioServerName, language);
        }

        /// <summary>
        /// Build a voice-DESIGN run (no reference audio): the Qwen
        /// VoiceDesign model invents a voice from a text description.
        /// The graph is synthesized (loader + designer + WAV saver) since
        /// no template file ships one; node classes/inputs mirror the
        /// installed comfyui-qwen3-tts pack. The server resolves the model
        /// from its local folder when present, else downloads the repo.
        /// </summary>
        public static BuildResult BuildVoiceDesign(string text, string instruct,
            long seed, string language)
        {
            if (string.IsNullOrEmpty(text))
                throw new Exception("Enter text to speak first.");
            if (string.IsNullOrEmpty(instruct))
                throw new Exception("Describe the voice first (e.g. gruff old orc guard).");
            if (seed < 0) seed = RandomSeed();
            if (string.IsNullOrEmpty(language)) language = "Auto";

            var api = new Dictionary<string, object>();
            api["loader"] = new Dictionary<string, object>
            {
                { "class_type", "Qwen3Loader" },
                { "inputs", new Dictionary<string, object>
                    {
                        { "repo_id", "Qwen/Qwen3-TTS-12Hz-1.7B-VoiceDesign" },
                        { "source", "HuggingFace" },
                        { "precision", "bf16" },
                        { "attention", "auto" },
                        { "local_model_path", "" }
                    }
                }
            };
            api["design"] = new Dictionary<string, object>
            {
                { "class_type", "Qwen3VoiceDesign" },
                { "inputs", new Dictionary<string, object>
                    {
                        { "model", new object[] { "loader", 0 } },
                        { "text", text },
                        { "instruct", instruct },
                        { "language", language },
                        { "seed", seed }
                    }
                }
            };
            api["mm_savewav"] = new Dictionary<string, object>
            {
                { "class_type", "SaveAudio" },
                { "inputs", new Dictionary<string, object>
                    {
                        { "filename_prefix", "mm_audio" },
                        { "audio", new object[] { "design", 0 } }
                    }
                }
            };

            return new BuildResult
            {
                PromptJson = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(api),
                Seed = seed,
                FormatNote = "voice-design (3 nodes)"
            };
        }

        /// <summary>
        /// Write a negative prompt into the text encoder feeding sampler
        /// "negative" inputs. Only literal (unlinked) text widgets are
        /// touched: a negative chained to the shared prompt source is the
        /// template author's design, never overwritten. Returns nodes set.
        /// </summary>
        public static int SetNegativePrompt(Dictionary<string, object> api, string negative)
        {
            int count = 0;
            if (api == null || string.IsNullOrEmpty(negative)) return count;
            foreach (var kv in api)
            {
                var node = kv.Value as Dictionary<string, object>;
                if (node == null) continue;
                string ct = node.ContainsKey("class_type") ? node["class_type"] as string : null;
                if (ct == null || ct.IndexOf("Sampler", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var inputs = node.ContainsKey("inputs") ? node["inputs"] as Dictionary<string, object> : null;
                if (inputs == null || !inputs.ContainsKey("negative")) continue;
                var arr = inputs["negative"] as IList;
                if (arr == null || arr.Count < 1) continue;
                string fromId = arr[0] as string;
                if (string.IsNullOrEmpty(fromId)) continue;
                object rawEnc;
                if (!api.TryGetValue(fromId, out rawEnc)) continue;
                var enc = rawEnc as Dictionary<string, object>;
                if (enc == null) continue;
                string ect = enc.ContainsKey("class_type") ? enc["class_type"] as string : null;
                if (ect == null) continue;
                bool isEnc = ect.IndexOf("TextEncode", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    TextSourceClasses.Contains(ect);
                if (!isEnc) continue;
                var encInputs = enc.ContainsKey("inputs")
                    ? enc["inputs"] as Dictionary<string, object> : null;
                if (encInputs == null) continue;
                string key = encInputs.ContainsKey("text") ? "text"
                    : encInputs.ContainsKey("value") ? "value" : null;
                if (key == null || !(encInputs[key] is string)) continue; // linked: author's design
                encInputs[key] = negative;
                count++;
            }
            return count;
        }

        #endregion

        #region IDs, stems and prompt helpers

        /// <summary>
        /// Short stable id for server-side output filenames + AudioPrompts
        /// ledger: yyMMdd_HHmmss kind-ish. Never derived from prompt text,
        /// so no length blowup and no Errno 22. The prompt itself lives in
        /// the ledger row (pipe-delimited) and the app library.
        /// </summary>
        public static string NewAudioId(string kind)
        {
            string k = string.IsNullOrEmpty(kind) ? "audio" : kind;
            var sb = new System.Text.StringBuilder();
            foreach (char c in k)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                    sb.Append(char.ToLowerInvariant(c));
                if (sb.Length >= 12) break;
            }
            if (sb.Length == 0) sb.Append("audio");
            return DateTime.Now.ToString("yyMMdd_HHmmss") + "_" + sb.ToString();
        }

        /// <summary>
        /// Legacy stem helper: kept for the one harness assertion and any
        /// outside callers. Prefer NewAudioId for filenames.
        /// </summary>
        public static string SafeFileStem(string prompt, int maxLen)
        {
            if (string.IsNullOrEmpty(prompt)) return "audio";
            if (maxLen < 8) maxLen = 8;
            if (maxLen > 64) maxLen = 64;
            var sb = new System.Text.StringBuilder();
            foreach (char c in prompt)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                    sb.Append(c);
                else if (c == ' ' || c == '-' || c == '_')
                    sb.Append('_');
                if (sb.Length >= maxLen) break;
            }
            string s = sb.ToString().Trim('_');
            if (s.Length == 0) s = "audio";
            return s.ToLowerInvariant();
        }


        /// <summary>

        private static readonly HashSet<string> TextSourceClasses = new HashSet<string>(StringComparer.Ordinal)
        {
            "PrimitiveString", "PrimitiveStringMultiline", "TextMultiline"
        };

        /// <summary>
        /// Point every user_input link at the user's prompt by walking
        /// upstream past StringReplace filename-sanitizer nodes to the
        /// source text widget. Never touches reprompt template nodes.
        /// Returns how many text widgets were set.
        /// </summary>
        public static int SetPromptUpstream(Dictionary<string, object> api, string prompt)
        {
            int count = 0;
            if (api == null) return 0;
            foreach (var kv in api)
            {
                var node = kv.Value as Dictionary<string, object>;
                if (node == null) continue;
                var inputs = node.ContainsKey("inputs") ? node["inputs"] as Dictionary<string, object> : null;
                if (inputs == null || !inputs.ContainsKey("user_input")) continue;
                var arr = inputs["user_input"] as IList;
                if (arr == null || arr.Count < 1) continue;
                string fromId = arr[0] as string;
                if (string.IsNullOrEmpty(fromId)) continue;
                if (SetUpstreamText(api, fromId, prompt, 0)) count++;
            }
            return count;
        }

        private static bool SetUpstreamText(Dictionary<string, object> api, string id,
            string prompt, int depth)
        {
            if (depth > 6) return false;
            object rawNode;
            if (!api.TryGetValue(id, out rawNode)) return false;
            var node = rawNode as Dictionary<string, object>;
            if (node == null) return false;
            string ct = node.ContainsKey("class_type") ? node["class_type"] as string : null;
            if (ct == null) return false;
            var inputs = node.ContainsKey("inputs") ? node["inputs"] as Dictionary<string, object> : null;
            if (inputs == null) return false;
            if (TextSourceClasses.Contains(ct))
            {
                string key = inputs.ContainsKey("value") ? "value"
                    : inputs.ContainsKey("text") ? "text" : null;
                if (key == null) return false;
                if (inputs[key] is IList) return false; // linked: not the source
                inputs[key] = prompt;
                return true;
            }
            if (string.Equals(ct, "StringReplace", StringComparison.Ordinal) && inputs.ContainsKey("string"))
            {
                var arr = inputs["string"] as IList;
                if (arr == null || arr.Count < 1) return false;
                string fromId = arr[0] as string;
                if (string.IsNullOrEmpty(fromId)) return false;
                return SetUpstreamText(api, fromId, prompt, depth + 1);
            }
            return false;
        }


        private static string ToStr(object o)
        {
            if (o == null) return string.Empty;
            return Convert.ToString(o, CultureInfo.InvariantCulture);
        }

        private static int ToInt(object o)
        {
            if (o == null) return 0;
            if (o is int) return (int)o;
            if (o is long)
            {
                long l = (long)o;
                if (l > int.MaxValue) return int.MaxValue;
                if (l < int.MinValue) return int.MinValue;
                return (int)l;
            }
            if (o is double) return (int)(double)o;
            int v;
            if (int.TryParse(Convert.ToString(o), out v)) return v;
            return 0;
        }

        #endregion
    }
}


