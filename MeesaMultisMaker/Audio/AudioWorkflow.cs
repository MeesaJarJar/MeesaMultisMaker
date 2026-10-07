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
    /// Templates are the user's own UI-format workflow files (nodes/links,
    /// optionally with subgraph definitions); API-format prompt maps work
    /// too. Conversion is generic: subgraph instances expand, outer widget
    /// values feed inner links, unlinked widgets become literals, linked
    /// inputs become [node, slot] refs. Value injection is by node class
    /// (prompt/seed/duration/steps/text/ref-audio), never by hardcoded id,
    /// so users can rearrange their workflows freely.
    ///
    /// Disk-only sink nodes with absolute local paths (JarJarAudioSaveAudio)
    /// are dropped: they break remote servers and never appear in history.
    /// History-visible savers (SaveAudio*, PreviewAudio) are kept as-is.
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

        public static string DefaultWorkflowDir()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "ComfyUI", "user", "default", "workflows");
                if (Directory.Exists(dir)) return dir;
            }
            catch { }
            return "D:\\ComfyUIPortable\\ComfyUI\\user\\default\\workflows";
        }

        public static string DefaultSfxWorkflowPath()
        {
            return Path.Combine(DefaultWorkflowDir(), "FASTAUDIOTGEN.json");
        }

        public static string DefaultMusicWorkflowPath()
        {
            return Path.Combine(DefaultWorkflowDir(), "WORKINGAUDIO.json");
        }

        public static string DefaultTtsWorkflowPath()
        {
            return Path.Combine(DefaultWorkflowDir(), "TEXT2SPEACHWORKINGFASTQWEN.json");
        }

        private static readonly string[] AudioExts =
            new string[] { ".wav", ".mp3", ".flac", ".ogg", ".opus" };

        public static string[] OutputExtensions()
        {
            return (string[])AudioExts.Clone();
        }

        // Disk-path sink nodes: write straight to a local folder, never
        // appear in history, and hardcode the author's machine paths.
        private static readonly HashSet<string> DropClasses = new HashSet<string>(StringComparer.Ordinal)
        {
            "JarJarAudioSaveAudio"
        };

        private static long RandomSeed()
        {
            var rnd = new Random();
            long s = ((long)(uint)rnd.Next() << 32) | (long)(uint)rnd.Next();
            return s < 0 ? -s : s;
        }

        #region SFX / music (Stable Audio subgraph templates)

        /// <summary>
        /// Build a text-to-audio run. prompt/duration/seed land on the
        /// subgraph instance's exposed inputs (user_input, duration, seed);
        /// steps lands on the inner sampler found post-expansion.
        /// </summary>
        public static BuildResult BuildTextToAudio(string templatePath, string prompt,
            double durationSec, long seed, int steps, bool useReprompt = false,
            string categoryOverride = null, string negativePrompt = null)
        {
            if (string.IsNullOrEmpty(templatePath))
                throw new Exception("Workflow file not set.");
            if (string.IsNullOrEmpty(prompt))
                throw new Exception("Enter a prompt first.");
            if (durationSec < 1) durationSec = 1;
            if (durationSec > 600) durationSec = 600;
            if (seed < 0) seed = RandomSeed();
            if (steps < 1) steps = 1;

            var overrides = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "user_input", prompt },
                { "duration", durationSec },
                { "seed", seed }
            };
            if (!string.IsNullOrEmpty(categoryOverride))
                overrides["category"] = categoryOverride;
            var api = ConvertFile(templatePath, overrides, prompt, useReprompt);
            // Errno 22 guard: template filename builders (StringReplace of
            // the prompt into SaveAudio* filename_prefix) produce 400-char
            // Windows paths and crash the whole execution AFTER rendering.
            // Pin every saver prefix to a short id; literal author prefixes
            // like mm_audio are left alone.
            string serverPrefix = NewAudioId(categoryOverride);
            PinSaverPrefixes(api, serverPrefix);
            // Negative prompt: the template's negative encoder (empty text
            // in the stock graphs) is otherwise never written, so user
            // negatives had nowhere to land.
            if (!string.IsNullOrWhiteSpace(negativePrompt))
                SetNegativePrompt(api, negativePrompt.Trim());

            // Inner sampler steps (Stable Audio KSampler etc.).
            foreach (var kv in api)
            {
                var node = kv.Value as Dictionary<string, object>;
                if (node == null) continue;
                string ct = node.ContainsKey("class_type") ? node["class_type"] as string : null;
                if (ct == null) continue;
                var inputs = node.ContainsKey("inputs") ? node["inputs"] as Dictionary<string, object> : null;
                if (inputs == null) continue;
                if ((ct == "KSampler" || ct.IndexOf("Sampler", StringComparison.OrdinalIgnoreCase) >= 0)
                    && inputs.ContainsKey("steps"))
                {
                    inputs["steps"] = steps;
                    if (inputs.ContainsKey("seed")) inputs["seed"] = seed;
                }
            }

            bool expanded = false;
            foreach (var kv in api)
            {
                if (kv.Key.StartsWith("sg", StringComparison.Ordinal)) { expanded = true; break; }
            }
            if (!expanded)
                throw new Exception("No audio-generator subgraph found in " + templatePath + " (expected a node exposing user_input/duration/seed).");

            return new BuildResult
            {
                PromptJson = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(api),
                Seed = seed,
                FormatNote = "audio (" + api.Count + " nodes" + (useReprompt ? ", reprompt on" : ", reprompt off") + ")",
                ServerPrefix = serverPrefix
            };
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

        #endregion

        #region Voice-clone TTS (Qwen3 template)

        /// <summary>
        /// Build a voice-clone run: text into Qwen3VoiceClone, optional
        /// server-side ref clip into LoadAudio (else template default).
        /// </summary>
        public static BuildResult BuildVoiceClone(string templatePath, string text,
            long seed, string refAudioServerName, string language)
        {
            if (string.IsNullOrEmpty(templatePath))
                throw new Exception("Workflow file not set.");
            if (string.IsNullOrEmpty(text))
                throw new Exception("Enter text to speak first.");
            if (seed < 0) seed = RandomSeed();
            if (string.IsNullOrEmpty(language)) language = "Auto";

            var api = ConvertFile(templatePath);
            bool gotVoice = false, gotAudio = false;

            foreach (var kv in api)
            {
                var node = kv.Value as Dictionary<string, object>;
                if (node == null) continue;
                string ct = node.ContainsKey("class_type") ? node["class_type"] as string : null;
                if (ct == null) continue;
                var inputs = node.ContainsKey("inputs") ? node["inputs"] as Dictionary<string, object> : null;
                if (inputs == null) continue;

                if (ct.IndexOf("VoiceClone", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (inputs.ContainsKey("text")) inputs["text"] = text;
                    if (inputs.ContainsKey("seed")) inputs["seed"] = seed;
                    if (inputs.ContainsKey("language")) inputs["language"] = language;
                    gotVoice = true;
                }
                else if (!string.IsNullOrEmpty(refAudioServerName) &&
                    string.Equals(ct, "LoadAudio", StringComparison.Ordinal))
                {
                    inputs["audio"] = refAudioServerName;
                    gotAudio = true;
                }
            }

            if (!gotVoice)
                throw new Exception("No voice-clone node found in " + templatePath + ".");
            if (!string.IsNullOrEmpty(refAudioServerName) && !gotAudio)
                throw new Exception("Template has no LoadAudio node for the reference clip.");

            return new BuildResult
            {
                PromptJson = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(api),
                Seed = seed,
                FormatNote = "voice-clone (" + api.Count + " nodes)"
            };
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

        #region Generic UI/API conversion

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

        /// <summary>Load a template file and return an API-format prompt graph.</summary>
        public static Dictionary<string, object> ConvertFile(string templatePath)
        {
            return ConvertFile(templatePath, null);
        }

        /// <summary>
        /// Same, but first stamps values onto subgraph-instance widgets by
        /// exposed input name, so they flow into the expansion. When
        /// promptUpstream is set, the prompt is additionally written to the
        /// PrimitiveString source feeding user_input through StringReplace
        /// chains (the outer widget is link-shadowed in that layout).
        /// </summary>
        public static Dictionary<string, object> ConvertFile(string templatePath,
            Dictionary<string, object> outerOverrides)
        {
            return ConvertFile(templatePath, outerOverrides, null);
        }

        public static Dictionary<string, object> ConvertFile(string templatePath,
            Dictionary<string, object> outerOverrides, string promptUpstream)
        {
            return ConvertFile(templatePath, outerOverrides, promptUpstream, false);
        }

        public static Dictionary<string, object> ConvertFile(string templatePath,
            Dictionary<string, object> outerOverrides, string promptUpstream, bool useReprompt)
        {
            string json = File.ReadAllText(templatePath);
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            object raw;
            try { raw = ser.DeserializeObject(json); }
            catch (Exception ex) { throw new Exception("Workflow file is not valid JSON: " + ex.Message); }
            var root = raw as Dictionary<string, object>;
            if (root == null) throw new Exception("Workflow template is not a JSON object.");

            var nodes = root.ContainsKey("nodes") ? root["nodes"] as IList : null;
            var links = root.ContainsKey("links") ? root["links"] as IList : null;
            if (nodes != null && links != null)
                return ConvertUi(root, nodes, links, outerOverrides, promptUpstream, useReprompt);
            if (IsApiFormat(root))
                return CloneApi(root, ser, useReprompt);
            var keys = new List<string>(root.Keys);
            throw new Exception("Workflow template is neither UI format (nodes/links) nor API format. Top-level keys: " +
                string.Join(", ", keys.ToArray()) + ".");
        }

        private static bool IsApiFormat(Dictionary<string, object> root)
        {
            int hits = 0, total = 0;
            foreach (var kv in root)
            {
                var node = kv.Value as Dictionary<string, object>;
                if (node == null) return false;
                total++;
                object ct;
                if (node.TryGetValue("class_type", out ct) && ct is string) hits++;
            }
            return total > 0 && hits == total;
        }

        private static Dictionary<string, object> CloneApi(Dictionary<string, object> root, JavaScriptSerializer ser)
        {
            return CloneApi(root, ser, false);
        }

        private static Dictionary<string, object> CloneApi(Dictionary<string, object> root,
            JavaScriptSerializer ser, bool useReprompt)
        {
            var api = ser.DeserializeObject(ser.Serialize(root)) as Dictionary<string, object>;
            // Drop disk-path sinks (same hazard as UI path).
            var drop = new List<string>();
            foreach (var kv in api)
            {
                var node = kv.Value as Dictionary<string, object>;
                if (node == null) continue;
                string ct = node.ContainsKey("class_type") ? node["class_type"] as string : null;
                if (ct != null && DropClasses.Contains(ct)) drop.Add(kv.Key);
            }
            foreach (string id in drop) api.Remove(id);
            foreach (var kv in api)
            {
                var node = kv.Value as Dictionary<string, object>;
                if (node == null) continue;
                var inputs = node.ContainsKey("inputs") ? node["inputs"] as Dictionary<string, object> : null;
                if (inputs == null) continue;
                var dead = new List<string>();
                foreach (var ik in inputs)
                {
                    var arr = ik.Value as IList;
                    if (arr != null && arr.Count >= 1 && arr[0] is string && drop.Contains((string)arr[0]))
                        dead.Add(ik.Key);
                }
                foreach (string k in dead) inputs.Remove(k);
            }
            int auto = 0;
            FinishAudioGraph(api, useReprompt, ref auto);
            return api;
        }

        private static Dictionary<string, object> ConvertUi(Dictionary<string, object> root, IList nodes, IList links,
            Dictionary<string, object> outerOverrides, string promptUpstream)
        {
            return ConvertUi(root, nodes, links, outerOverrides, promptUpstream, false);
        }

        private static Dictionary<string, object> ConvertUi(Dictionary<string, object> root, IList nodes, IList links,
            Dictionary<string, object> outerOverrides, string promptUpstream, bool useReprompt)
        {
            // linkId -> [fromNodeId, fromSlot].
            var linkOrigin = new Dictionary<int, int[]>();
            foreach (object lo in links)
            {
                var arr = lo as IList;
                if (arr == null || arr.Count < 5) continue;
                linkOrigin[ToInt(arr[0])] = new int[] { ToInt(arr[1]), ToInt(arr[2]) };
            }

            var byId = new Dictionary<string, Dictionary<string, object>>();
            foreach (object no in nodes)
            {
                var node = no as Dictionary<string, object>;
                if (node == null || !node.ContainsKey("id")) continue;
                byId[ToStr(node["id"])] = node;
            }

            var subgraphs = new Dictionary<string, Dictionary<string, object>>();
            object defsObj;
            if (root.TryGetValue("definitions", out defsObj))
            {
                var defs = defsObj as Dictionary<string, object>;
                object subsObj;
                if (defs != null && defs.TryGetValue("subgraphs", out subsObj))
                {
                    var subs = subsObj as IList;
                    if (subs != null)
                    {
                        foreach (object so in subs)
                        {
                            var sd = so as Dictionary<string, object>;
                            if (sd == null || !sd.ContainsKey("id")) continue;
                            subgraphs[ToStr(sd["id"])] = sd;
                        }
                    }
                }
            }

            var api = new Dictionary<string, object>();
            var reroute = new Dictionary<string, string>(); // outer reroute id -> "nodeId:slot"
            int auto = 0;

            if (!string.IsNullOrEmpty(promptUpstream))
                InjectPromptUpstreamUi(byId, linkOrigin, subgraphs, promptUpstream);

            // Expand subgraph instances first (their outputs rewrite refs).
            var rewrite = new Dictionary<string, string>(); // outerId -> "nodeId:slot"
            foreach (var kv in byId)
            {
                string type = kv.Value.ContainsKey("type") ? kv.Value["type"] as string : null;
                if (string.IsNullOrEmpty(type) || !subgraphs.ContainsKey(type)) continue;
                if (outerOverrides != null && outerOverrides.Count > 0)
                    ApplyOuterOverrides(kv.Value, outerOverrides);
                ExpandSubgraph(kv.Key, kv.Value, subgraphs[type], linkOrigin, api, rewrite);
            }

            // Plain nodes (Reroute recorded, drop-classes skipped).
            foreach (var kv in byId)
            {
                string id = kv.Key;
                string type = kv.Value.ContainsKey("type") ? kv.Value["type"] as string : null;
                if (string.IsNullOrEmpty(type)) continue;
                if (subgraphs.ContainsKey(type)) continue; // expanded above
                if (DropClasses.Contains(type)) continue;
                if (string.Equals(type, "Reroute", StringComparison.OrdinalIgnoreCase))
                {
                    string target = ResolveReroute(kv.Value, linkOrigin, byId, rewrite);
                    if (target != null) reroute[id] = target;
                    continue;
                }
                api[id] = new Dictionary<string, object>
                {
                    { "class_type", type },
                    { "inputs", BuildInputs(kv.Value, linkOrigin, byId, rewrite, reroute) }
                };
            }

            // Resolve reroute aliases left inside inputs.
            foreach (var kv in api)
            {
                var node = kv.Value as Dictionary<string, object>;
                if (node == null) continue;
                var inputs = node.ContainsKey("inputs") ? node["inputs"] as Dictionary<string, object> : null;
                if (inputs == null) continue;
                var keys = new List<string>(inputs.Keys);
                foreach (string k in keys)
                {
                    var arr = inputs[k] as IList;
                    if (arr == null || arr.Count < 2) continue;
                    string refId = arr[0] as string;
                    if (refId == null) continue;
                    string finalTarget = refId;
                    int guard = 0;
                    string next;
                    while (reroute.TryGetValue(finalTarget, out next) && guard++ < 25)
                        finalTarget = next;
                    if (finalTarget != refId)
                    {
                        int c = finalTarget.LastIndexOf(':');
                        if (c > 0)
                            inputs[k] = new object[] { finalTarget.Substring(0, c), int.Parse(finalTarget.Substring(c + 1)) };
                    }
                }
            }

            // Reprompt bypass, dead-branch prune, saver assurance.
            FinishAudioGraph(api, useReprompt, ref auto);
            return api;
        }

        /// <summary>
        /// Final graph surgery, mirroring what native subgraph execution
        /// does that flattening would otherwise lose:
        /// 1. String preview viewers (PreviewAny) are dropped -- they exist
        ///    only to display text in the UI, but flattened they become
        ///    output nodes that force their (possibly broken/heavy) upstream
        ///    to execute.
        /// 2. With use_reprompt off, ComfySwitchNodes whose on_true branch
        ///    is reprompt text get on_true rewired to the raw prompt, so the
        ///    deselected LLM branch never executes.
        /// 3. Nodes unreachable from any audio sink are pruned.
        /// 4. A WAV saver is added when no audio sink exists at all.
        /// </summary>
        private static void FinishAudioGraph(Dictionary<string, object> api, bool useReprompt, ref int auto)
        {
            // 1. Drop string preview viewers.
            var dropPreview = new List<string>();
            foreach (var kv in api)
            {
                var node = kv.Value as Dictionary<string, object>;
                if (node == null) continue;
                string ct = node.ContainsKey("class_type") ? node["class_type"] as string : null;
                if (string.Equals(ct, "PreviewAny", StringComparison.Ordinal))
                    dropPreview.Add(kv.Key);
            }
            foreach (string id in dropPreview) api.Remove(id);
            RemoveDanglingRefs(api, dropPreview);

            // 2. Reprompt bypass.
            if (!useReprompt)
                SeverRepromptBranch(api);

            // 3. Prune branches feeding nothing kept.
            PruneDeadBranches(api);

            // 4. Saver assurance.
            EnsureAudioSaver(api, ref auto);
        }

        private static void RemoveDanglingRefs(Dictionary<string, object> api, List<string> dropIds)
        {
            if (dropIds == null || dropIds.Count == 0) return;
            var gone = new HashSet<string>(dropIds);
            foreach (var kv in api)
            {
                var node = kv.Value as Dictionary<string, object>;
                if (node == null) continue;
                var inputs = node.ContainsKey("inputs") ? node["inputs"] as Dictionary<string, object> : null;
                if (inputs == null) continue;
                var dead = new List<string>();
                foreach (var ik in inputs)
                {
                    var arr = ik.Value as IList;
                    if (arr != null && arr.Count >= 1 && arr[0] is string && gone.Contains((string)arr[0]))
                        dead.Add(ik.Key);
                }
                foreach (string k in dead) inputs.Remove(k);
            }
        }

        /// <summary>
        /// With reprompt off, any switch choosing between raw and reprompted
        /// text gets its reprompt side rewired to the raw side, so the LLM
        /// branch has no consumers and is pruned below.
        /// </summary>
        private static void SeverRepromptBranch(Dictionary<string, object> api)
        {
            foreach (var kv in api)
            {
                var node = kv.Value as Dictionary<string, object>;
                if (node == null) continue;
                string ct = node.ContainsKey("class_type") ? node["class_type"] as string : null;
                if (!string.Equals(ct, "ComfySwitchNode", StringComparison.Ordinal)) continue;
                var inputs = node.ContainsKey("inputs") ? node["inputs"] as Dictionary<string, object> : null;
                if (inputs == null || !inputs.ContainsKey("on_true") || !inputs.ContainsKey("on_false")) continue;
                var onTrue = inputs["on_true"] as IList;
                if (onTrue == null || onTrue.Count < 1) continue;
                string fromId = onTrue[0] as string;
                if (string.IsNullOrEmpty(fromId)) continue;
                if (UltimateSourceClass(api, fromId, 0) != "TextGenerate") continue;
                var onFalse = inputs["on_false"] as IList;
                if (onFalse == null || onFalse.Count < 2) continue;
                inputs["on_true"] = new object[] { onFalse[0], onFalse[1] };
            }
        }

        /// <summary>
        /// Class of the node producing a ref, following linear single-link
        /// chains (StringReplace filename builders etc.). Null when unknown.
        /// </summary>
        private static string UltimateSourceClass(Dictionary<string, object> api, string id, int depth)
        {
            if (depth > 4) return null;
            object rawNode;
            if (!api.TryGetValue(id, out rawNode)) return null;
            var node = rawNode as Dictionary<string, object>;
            if (node == null) return null;
            string ct = node.ContainsKey("class_type") ? node["class_type"] as string : null;
            if (string.Equals(ct, "TextGenerate", StringComparison.Ordinal)) return ct;
            var inputs = node.ContainsKey("inputs") ? node["inputs"] as Dictionary<string, object> : null;
            if (inputs == null) return ct;
            string singleFrom = null;
            foreach (var ik in inputs)
            {
                var arr = ik.Value as IList;
                if (arr == null || arr.Count < 1) continue;
                string fid = arr[0] as string;
                if (string.IsNullOrEmpty(fid)) continue;
                if (singleFrom != null) return ct; // fan-in: stop here
                singleFrom = fid;
            }
            if (singleFrom == null) return ct;
            string inner = UltimateSourceClass(api, singleFrom, depth + 1);
            return inner ?? ct;
        }

        private static readonly HashSet<string> AudioSinkClasses = new HashSet<string>(StringComparer.Ordinal)
        {
            "SaveAudio", "SaveAudioMP3", "SaveAudioAdvanced", "SaveAudioOpus",
            "SaveAudioFLAC", "SaveAudioOGG", "PreviewAudio"
        };

        /// <summary>
        /// Drop every node outside the backward closure of the audio sinks.
        /// Skipped entirely when no sink exists (server reports clearly).
        /// </summary>
        private static void PruneDeadBranches(Dictionary<string, object> api)
        {
            var roots = new List<string>();
            foreach (var kv in api)
            {
                var node = kv.Value as Dictionary<string, object>;
                if (node == null) continue;
                string ct = node.ContainsKey("class_type") ? node["class_type"] as string : null;
                if (ct != null && AudioSinkClasses.Contains(ct)) roots.Add(kv.Key);
            }
            if (roots.Count == 0) return;
            var keep = new HashSet<string>(roots);
            var stack = new Stack<string>(roots);
            while (stack.Count > 0)
            {
                string id = stack.Pop();
                object rawNode;
                if (!api.TryGetValue(id, out rawNode)) continue;
                var node = rawNode as Dictionary<string, object>;
                if (node == null) continue;
                var inputs = node.ContainsKey("inputs") ? node["inputs"] as Dictionary<string, object> : null;
                if (inputs == null) continue;
                foreach (var ik in inputs)
                {
                    var arr = ik.Value as IList;
                    if (arr == null || arr.Count < 1) continue;
                    string fid = arr[0] as string;
                    if (string.IsNullOrEmpty(fid) || keep.Contains(fid)) continue;
                    if (!api.ContainsKey(fid)) continue;
                    keep.Add(fid);
                    stack.Push(fid);
                }
            }
            var drop = new List<string>();
            foreach (var kv in api)
                if (!keep.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (string id in drop) api.Remove(id);
        }

        /// <summary>
        /// Follow a Reroute node to its ultimate source ("nodeId:slot").
        /// </summary>
        private static string ResolveReroute(Dictionary<string, object> node,
            Dictionary<int, int[]> linkOrigin, Dictionary<string, Dictionary<string, object>> byId,
            Dictionary<string, string> rewrite)
        {
            try
            {
                var inList = node.ContainsKey("inputs") ? node["inputs"] as IList : null;
                if (inList == null) return null;
                foreach (object io in inList)
                {
                    var inp = io as Dictionary<string, object>;
                    if (inp == null) continue;
                    object lk = inp.ContainsKey("link") ? inp["link"] : null;
                    if (lk == null) continue;
                    int[] org;
                    if (!linkOrigin.TryGetValue(ToInt(lk), out org)) continue;
                    string oid = org[0].ToString(CultureInfo.InvariantCulture);
                    string rw;
                    if (rewrite.TryGetValue(oid, out rw)) return rw;
                    Dictionary<string, object> src;
                    if (byId.TryGetValue(oid, out src))
                    {
                        string st = src.ContainsKey("type") ? src["type"] as string : null;
                        if (string.Equals(st, "Reroute", StringComparison.OrdinalIgnoreCase))
                        {
                            string inner = ResolveReroute(src, linkOrigin, byId, rewrite);
                            if (inner != null) return inner;
                        }
                    }
                    return oid + ":" + org[1];
                }
            }
            catch { }
            return null;
        }

        private static void ExpandSubgraph(string outerId, Dictionary<string, object> outerNode,
            Dictionary<string, object> sub, Dictionary<int, int[]> linkOrigin,
            Dictionary<string, object> api, Dictionary<string, string> rewrite)
        {
            string prefix = "sg" + outerId + "_";

            // Outer-provided values by exposed input name (links + widgets).
            var outerInputs = BuildInputs(outerNode, linkOrigin, null, null, null);
            var outerList = outerNode.ContainsKey("inputs") ? outerNode["inputs"] as IList : null;
            var outerByName = new Dictionary<string, object>();
            if (outerList != null)
            {
                foreach (object io in outerList)
                {
                    var inp = io as Dictionary<string, object>;
                    if (inp == null || !inp.ContainsKey("name")) continue;
                    string n = inp["name"] as string;
                    if (n != null && outerInputs.ContainsKey(n)) outerByName[n] = outerInputs[n];
                }
            }
            // Unlinked outer widget values feed the subgraph inputs too.
            var outerWidgets = FullWidgetNames(outerList);
            var outerVals = WidgetValues(outerNode, outerWidgets.Count);
            var outerNamed = NamedWidgets(outerNode);
            for (int k = 0; k < outerWidgets.Count; k++)
            {
                string wname = outerWidgets[k];
                if (outerByName.ContainsKey(wname)) continue; // link wins
                object v;
                if (outerNamed != null && outerNamed.TryGetValue(wname, out v)) outerByName[wname] = v;
                else if (k < outerVals.Count) outerByName[wname] = outerVals[k];
            }

            // Subgraph input defs: inner link ids per exposed name.
            var sgInputLinks = new Dictionary<int, string>();
            object sInObj;
            if (sub.TryGetValue("inputs", out sInObj))
            {
                var sIns = sInObj as IList;
                if (sIns != null)
                {
                    foreach (object so in sIns)
                    {
                        var si = so as Dictionary<string, object>;
                        if (si == null || !si.ContainsKey("name")) continue;
                        string n = si["name"] as string;
                        // Match by widget name OR label: instance inputs use
                        // short names (user_input) while defs may label them
                        // differently (reprompt_category). Index matters most.
                        object lids;
                        if (si.TryGetValue("linkIds", out lids))
                        {
                            var lidList = lids as IList;
                            if (lidList != null)
                                foreach (object lid in lidList)
                                    sgInputLinks[ToInt(lid)] = n;
                        }
                    }
                }
            }
            var innerNodes = sub.ContainsKey("nodes") ? sub["nodes"] as IList : null;
            if (innerNodes == null) throw new Exception("Subgraph '" + outerId + "' has no nodes.");

            var origin = new Dictionary<int, string>();
            foreach (object no in innerNodes)
            {
                var node = no as Dictionary<string, object>;
                if (node == null || !node.ContainsKey("id")) continue;
                string nid = prefix + ToStr(node["id"]);
                var outs = node.ContainsKey("outputs") ? node["outputs"] as IList : null;
                if (outs == null) continue;
                for (int oi = 0; oi < outs.Count; oi++)
                {
                    var o = outs[oi] as Dictionary<string, object>;
                    if (o == null) continue;
                    object lo;
                    if (!o.TryGetValue("links", out lo)) continue;
                    var lids = lo as IList;
                    if (lids == null) continue;
                    foreach (object lid in lids)
                        origin[ToInt(lid)] = nid + ":" + oi;
                }
            }

            foreach (object no in innerNodes)
            {
                var node = no as Dictionary<string, object>;
                if (node == null || !node.ContainsKey("id")) continue;
                string nid = prefix + ToStr(node["id"]);
                string type = node.ContainsKey("type") ? node["type"] as string : null;
                if (string.IsNullOrEmpty(type)) continue;
                if (DropClasses.Contains(type)) continue;

                var inputs = new Dictionary<string, object>();
                var inList = node.ContainsKey("inputs") ? node["inputs"] as IList : null;
                var fullWidgets = FullWidgetNames(inList);
                var values = WidgetValues(node, fullWidgets.Count);
                var named = NamedWidgets(node);

                for (int k = 0; k < fullWidgets.Count; k++)
                {
                    string wname = fullWidgets[k];
                    int linkId = InnerLinkOf(inList, wname);
                    if (linkId >= 0)
                    {
                        string sgIn;
                        if (sgInputLinks.TryGetValue(linkId, out sgIn))
                        {
                            object ov;
                            if (outerByName.TryGetValue(sgIn, out ov) && ov != null)
                                inputs[wname] = ov;
                        }
                        else
                        {
                            string org;
                            if (origin.TryGetValue(linkId, out org))
                            {
                                int c = org.LastIndexOf(':');
                                inputs[wname] = new object[] { org.Substring(0, c), int.Parse(org.Substring(c + 1)) };
                            }
                        }
                        continue;
                    }
                    object v;
                    if (named != null && named.TryGetValue(wname, out v)) inputs[wname] = v;
                    else if (k < values.Count) inputs[wname] = values[k];
                }

                if (inList != null)
                {
                    foreach (object io in inList)
                    {
                        var inp = io as Dictionary<string, object>;
                        if (inp == null || !inp.ContainsKey("name")) continue;
                        string n = inp["name"] as string;
                        if (n == null || inputs.ContainsKey(n)) continue;
                        object lk = inp.ContainsKey("link") ? inp["link"] : null;
                        if (lk == null) continue;
                        int linkId = ToInt(lk);
                        string sgIn;
                        if (sgInputLinks.TryGetValue(linkId, out sgIn))
                        {
                            object ov;
                            if (outerByName.TryGetValue(sgIn, out ov) && ov != null)
                                inputs[n] = ov;
                        }
                        else
                        {
                            string org;
                            if (origin.TryGetValue(linkId, out org))
                            {
                                int c = org.LastIndexOf(':');
                                inputs[n] = new object[] { org.Substring(0, c), int.Parse(org.Substring(c + 1)) };
                            }
                        }
                    }
                }

                api[nid] = new Dictionary<string, object>
                {
                    { "class_type", type },
                    { "inputs", inputs }
                };
            }

            object sOutObj;
            if (sub.TryGetValue("outputs", out sOutObj))
            {
                var sOuts = sOutObj as IList;
                if (sOuts != null && sOuts.Count > 0)
                {
                    var so = sOuts[0] as Dictionary<string, object>;
                    if (so != null)
                    {
                        object lids;
                        if (so.TryGetValue("linkIds", out lids))
                        {
                            var lidList = lids as IList;
                            if (lidList != null)
                            {
                                foreach (object lid in lidList)
                                {
                                    string org;
                                    if (origin.TryGetValue(ToInt(lid), out org))
                                    {
                                        rewrite[outerId] = org;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (!rewrite.ContainsKey(outerId))
                throw new Exception("Could not resolve subgraph output for node " + outerId + ".");
        }

        /// <summary>API inputs for one UI node: links first, then widgets.</summary>
        private static Dictionary<string, object> BuildInputs(Dictionary<string, object> node,
            Dictionary<int, int[]> linkOrigin,
            Dictionary<string, Dictionary<string, object>> byId,
            Dictionary<string, string> rewrite,
            Dictionary<string, string> reroute)
        {
            var inputs = new Dictionary<string, object>();
            var nodeInputs = node.ContainsKey("inputs") ? node["inputs"] as IList : null;

            var linkedNames = new HashSet<string>();
            if (nodeInputs != null && linkOrigin != null)
            {
                foreach (object io in nodeInputs)
                {
                    var inp = io as Dictionary<string, object>;
                    if (inp == null || !inp.ContainsKey("name")) continue;
                    string iname = inp["name"] as string;
                    object linkObj = inp.ContainsKey("link") ? inp["link"] : null;
                    if (linkObj == null) continue;
                    int[] org;
                    if (!linkOrigin.TryGetValue(ToInt(linkObj), out org)) continue;
                    string originId = org[0].ToString(CultureInfo.InvariantCulture);
                    string rw;
                    if (rewrite != null && rewrite.TryGetValue(originId, out rw))
                    {
                        int c = rw.LastIndexOf(':');
                        inputs[iname] = new object[] { rw.Substring(0, c), int.Parse(rw.Substring(c + 1)) };
                    }
                    else
                    {
                        inputs[iname] = new object[] { originId, org[1] };
                    }
                    linkedNames.Add(iname);
                }
            }

            var fullWidgets = FullWidgetNames(nodeInputs);
            var values = WidgetValues(node, fullWidgets.Count);
            var named = NamedWidgets(node);
            for (int k = 0; k < fullWidgets.Count; k++)
            {
                string wname = fullWidgets[k];
                if (linkedNames.Contains(wname)) continue;
                object val;
                if (named != null && named.TryGetValue(wname, out val)) { /* use named */ }
                else val = k < values.Count ? values[k] : null;
                inputs[wname] = val;
            }
            return inputs;
        }

        private static List<string> FullWidgetNames(IList nodeInputs)
        {
            var names = new List<string>();
            if (nodeInputs == null) return names;
            foreach (object io in nodeInputs)
            {
                var inp = io as Dictionary<string, object>;
                if (inp == null || !inp.ContainsKey("widget")) continue;
                object w = inp["widget"];
                string wname = null;
                var wd = w as Dictionary<string, object>;
                if (wd != null && wd.ContainsKey("name")) wname = wd["name"] as string;
                if (wname == null && inp.ContainsKey("name")) wname = inp["name"] as string;
                if (wname == null) wname = "value";
                if (string.Equals(wname, "upload", StringComparison.OrdinalIgnoreCase)) continue;
                names.Add(wname);
            }
            return names;
        }

        private static readonly HashSet<string> ControlWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "fixed", "increment", "decrement", "randomize"
        };

        private static List<object> WidgetValues(Dictionary<string, object> node, int want)
        {
            var values = new List<object>();
            object wv;
            if (!node.TryGetValue("widgets_values", out wv)) return values;
            var widgets = wv as IList;
            if (widgets == null) return values;
            foreach (object v in widgets)
            {
                if (v is string && ControlWords.Contains((string)v) && widgets.Count - 1 >= want)
                    continue;
                values.Add(v);
            }
            return values;
        }

        private static Dictionary<string, object> NamedWidgets(Dictionary<string, object> node)
        {
            object nv;
            if (!node.TryGetValue("widgets_values_named", out nv)) return null;
            return nv as Dictionary<string, object>;
        }

        /// <summary>
        /// Stamp values onto a subgraph instance's widgets by exposed input
        /// name (both the positional array and the named dict when present).
        /// </summary>
        private static void ApplyOuterOverrides(Dictionary<string, object> outerNode,
            Dictionary<string, object> overrides)
        {
            try
            {
                var inList = outerNode.ContainsKey("inputs") ? outerNode["inputs"] as IList : null;
                var names = FullWidgetNames(inList);
                for (int k = 0; k < names.Count; k++)
                {
                    object nv;
                    if (!overrides.TryGetValue(names[k], out nv)) continue;
                    SetWidgetByName(outerNode, names[k], nv);
                }
            }
            catch { }
        }

        /// <summary>Set one widget by name on a UI node (array + named dict).</summary>
        private static bool SetWidgetByName(Dictionary<string, object> node, string name, object value)
        {
            try
            {
                var inList = node.ContainsKey("inputs") ? node["inputs"] as IList : null;
                var names = FullWidgetNames(inList);
                object wv;
                if (!node.TryGetValue("widgets_values", out wv)) return false;
                var arr = wv as IList;
                if (arr == null) return false;
                var rawPos = new List<int>();
                for (int r = 0; r < arr.Count; r++)
                {
                    object v = arr[r];
                    if (v is string && ControlWords.Contains((string)v) && arr.Count - 1 >= names.Count)
                        continue;
                    rawPos.Add(r);
                }
                bool set = false;
                for (int k = 0; k < names.Count && k < rawPos.Count; k++)
                {
                    if (!string.Equals(names[k], name, StringComparison.Ordinal)) continue;
                    try { arr[rawPos[k]] = value; set = true; }
                    catch { }
                }
                var named = NamedWidgets(node);
                try { if (set && named != null && named.ContainsKey(name)) named[name] = value; }
                catch { }
                return set;
            }
            catch { return false; }
        }

        private static readonly HashSet<string> PromptChainPass = new HashSet<string>(StringComparer.Ordinal)
        {
            "StringReplace"
        };

        /// <summary>
        /// UI-graph prompt injection: for every subgraph instance (or any
        /// node) exposing a LINKED user_input, walk upstream past
        /// StringReplace sanitizers to the source PrimitiveString and set
        /// it. Unlinked user_input widgets are handled by overrides.
        /// </summary>
        private static void InjectPromptUpstreamUi(Dictionary<string, Dictionary<string, object>> byId,
            Dictionary<int, int[]> linkOrigin,
            Dictionary<string, Dictionary<string, object>> subgraphs, string prompt)
        {
            try
            {
                foreach (var kv in byId)
                {
                    var node = kv.Value;
                    string type = node.ContainsKey("type") ? node["type"] as string : null;
                    if (string.IsNullOrEmpty(type)) continue;
                    bool isSub = subgraphs.ContainsKey(type);
                    var inList = node.ContainsKey("inputs") ? node["inputs"] as IList : null;
                    if (inList == null) continue;
                    foreach (object io in inList)
                    {
                        var inp = io as Dictionary<string, object>;
                        if (inp == null) continue;
                        if (!string.Equals(inp.ContainsKey("name") ? inp["name"] as string : null,
                            "user_input", StringComparison.Ordinal)) continue;
                        object lk = inp.ContainsKey("link") ? inp["link"] : null;
                        if (lk == null) continue; // unlinked: overrides cover it
                        int[] org;
                        if (!linkOrigin.TryGetValue(ToInt(lk), out org)) continue;
                        WalkPromptChain(byId, org[0].ToString(CultureInfo.InvariantCulture), prompt, 0, isSub);
                    }
                }
            }
            catch { }
        }

        private static bool WalkPromptChain(Dictionary<string, Dictionary<string, object>> byId,
            string nodeId, string prompt, int depth, bool required)
        {
            if (depth > 6) return false;
            Dictionary<string, object> node;
            if (!byId.TryGetValue(nodeId, out node) || node == null) return false;
            string type = node.ContainsKey("type") ? node["type"] as string : null;
            if (TextSourceClasses.Contains(type))
            {
                var inList = node.ContainsKey("inputs") ? node["inputs"] as IList : null;
                var names = FullWidgetNames(inList);
                string key = names.Contains("value") ? "value" : names.Contains("text") ? "text" : null;
                if (key == null && names.Count == 1) key = names[0];
                if (key == null) return false;
                // Linked text source: keep walking if possible, else stop.
                return SetWidgetByName(node, key, prompt);
            }
            if (PromptChainPass.Contains(type))
            {
                var inList = node.ContainsKey("inputs") ? node["inputs"] as IList : null;
                if (inList == null) return false;
                foreach (object io in inList)
                {
                    var inp = io as Dictionary<string, object>;
                    if (inp == null) continue;
                    if (!string.Equals(inp.ContainsKey("name") ? inp["name"] as string : null,
                        "string", StringComparison.Ordinal)) continue;
                    object lk = inp.ContainsKey("link") ? inp["link"] : null;
                    if (lk == null) return false;
                    // linkOrigin not passed here: resolve via byId scan below.
                    return WalkPromptChainLink(byId, ToInt(lk), prompt, depth + 1);
                }
            }
            return false;
        }

        private static bool WalkPromptChainLink(Dictionary<string, Dictionary<string, object>> byId,
            int linkId, string prompt, int depth)
        {
            // Find the link origin by scanning outputs (UI graphs only).
            foreach (var kv in byId)
            {
                var node = kv.Value;
                var outs = node.ContainsKey("outputs") ? node["outputs"] as IList : null;
                if (outs == null) continue;
                foreach (object oo in outs)
                {
                    var o = oo as Dictionary<string, object>;
                    if (o == null) continue;
                    object lo;
                    if (!o.TryGetValue("links", out lo)) continue;
                    var lids = lo as IList;
                    if (lids == null) continue;
                    foreach (object lid in lids)
                    {
                        if (ToInt(lid) == linkId)
                            return WalkPromptChain(byId, kv.Key, prompt, depth, false);
                    }
                }
            }
            return false;
        }

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

        private static int InnerLinkOf(IList inList, string name)
        {
            if (inList == null) return -1;
            foreach (object io in inList)
            {
                var inp = io as Dictionary<string, object>;
                if (inp == null) continue;
                if (!string.Equals(inp.ContainsKey("name") ? inp["name"] as string : null, name)) continue;
                object lk = inp.ContainsKey("link") ? inp["link"] : null;
                if (lk == null) return -1;
                return ToInt(lk);
            }
            return -1;
        }

        private static void EnsureAudioSaver(Dictionary<string, object> api, ref int auto)
        {
            // A permanent WAV matters: the editor edits WAV, previews are
            // temp files, and templates may emit mp3 only. Keep existing
            // savers; add one SaveAudio sibling when nothing writes WAV.
            if (HasWavSaver(api)) return;
            string[] keepers = new string[]
            {
                "SaveAudio", "SaveAudioMP3", "SaveAudioAdvanced", "SaveAudioOpus",
                "PreviewAudio", "SaveAudioFLAC", "SaveAudioOGG"
            };
            string feedId = null;
            int feedSlot = 0;
            foreach (var kv in api)
            {
                var node = kv.Value as Dictionary<string, object>;
                if (node == null) continue;
                string ct = node.ContainsKey("class_type") ? node["class_type"] as string : null;
                if (ct == null) continue;
                bool keeper = false;
                foreach (string k in keepers)
                    if (string.Equals(ct, k, StringComparison.Ordinal)) { keeper = true; break; }
                if (!keeper) continue;
                var inputs = node.ContainsKey("inputs") ? node["inputs"] as Dictionary<string, object> : null;
                if (inputs == null || !inputs.ContainsKey("audio")) continue;
                var arr = inputs["audio"] as IList;
                if (arr == null || arr.Count < 2) continue;
                feedId = arr[0] as string;
                feedSlot = ToInt(arr[1]);
                if (!string.IsNullOrEmpty(feedId)) break;
            }
            if (string.IsNullOrEmpty(feedId)) return; // nothing to save; server will say so
            string id = "mm_savewav";
            while (api.ContainsKey(id)) id = "mm_savewav" + (++auto);
            api[id] = new Dictionary<string, object>
            {
                { "class_type", "SaveAudio" },
                { "inputs", new Dictionary<string, object>
                    {
                        { "filename_prefix", "mm_audio" },
                        { "audio", new object[] { feedId, feedSlot } }
                    }
                }
            };
        }

        /// <summary>True when some saver already writes WAV files.</summary>
        private static bool HasWavSaver(Dictionary<string, object> api)
        {
            foreach (var kv in api)
            {
                var node = kv.Value as Dictionary<string, object>;
                if (node == null) continue;
                string ct = node.ContainsKey("class_type") ? node["class_type"] as string : null;
                if (string.Equals(ct, "SaveAudio", StringComparison.Ordinal)) return true;
                if (string.Equals(ct, "SaveAudioAdvanced", StringComparison.Ordinal))
                {
                    var inputs = node.ContainsKey("inputs") ? node["inputs"] as Dictionary<string, object> : null;
                    if (inputs == null) continue;
                    foreach (var ik in inputs)
                    {
                        if (ik.Value is IList) continue;
                        string s = ik.Value as string;
                        if (!string.IsNullOrEmpty(s) && s.IndexOf("wav", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }
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


