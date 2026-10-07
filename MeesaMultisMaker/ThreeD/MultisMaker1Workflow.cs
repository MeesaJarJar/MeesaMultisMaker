using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace MeesaMultisMaker.ThreeD
{
    /// <summary>
    /// Wires the MultisMaker1 UI-format workflow into an API-format prompt.
    ///
    /// Handles the two things that make this workflow special:
    ///  1. Node 131 is a *subgraph* node ("Image Edit (Flux.2 Klein 4B
    ///     Distilled)", type f1793303-...) whose definition lives in the
    ///     file's "definitions.subgraphs". The backend has no such node type
    ///     (it is expanded client-side by the ComfyUI frontend), so we expand
    ///     it inline: inner nodes are remapped to sg131_* ids, outer values
    ///     (prompt link, init-image link, model filenames, seed) are pushed
    ///     into the subgraph inputs, and outer links are rewritten to the
    ///     inner VAEDecode output.
    ///  2. Accepts API-format files directly as well.
    ///
    /// Preview/upload-only nodes (JarJarSubmit3D, PreviewImage,
    /// PreviewGaussianSplat) are dropped. SplatToFile3D keeps format "ply".
    /// </summary>
    public static class MultisMaker1Workflow
    {
        public const string DefaultWorkflowPath =
            @"D:\ComfyUIPortable\ComfyUI\user\default\workflows\MultisMaker1.json";

        // Node ids in MultisMaker1.json (see workflow file).
        private const string PromptNode = "138";
        private const string KSamplerNode = "101";
        private const string TripoDecodeNode = "107";
        private const string FluxNode = "131";
        private const string LoadImageNode = "132";

        // JarJarSubmit3D is dropped (server-side upload we don't need).
        // The Preview nodes MUST stay: the backend rejects prompts with no
        // OUTPUT_NODE ("prompt_no_outputs"), and SplatToFile3D alone does
        // not count as one.
        private static readonly HashSet<string> DroppedNodes = new HashSet<string>(
            new string[] { "134" });

        // Seed-control widgets have no API input; drop them before mapping.
        private static readonly HashSet<string> ControlWords = new HashSet<string>(
            new string[] { "randomize", "fixed", "increment", "decrement" });

        public class BuildResult
        {
            public string PromptJson;
            public long Seed;
            public string FormatNote;
        }

        /// <param name="fluxOnly">Image-only graph: just the Flux image stage
        /// plus a SaveImage (for "Generate Init Image"), no 3D nodes.</param>
        /// <param name="skipFlux">3D graph without the Flux image stage: the
        /// staged init image feeds the Tripo/remove-background stages
        /// directly (for "Generate 3D", which no longer regenerates 2D).
        /// Mutually exclusive with fluxOnly (fluxOnly wins).</param>
        public static BuildResult Build(string templatePath, string prompt, long seed,
            string initImageName, int steps, int fluxSteps, bool fluxOnly = false, bool skipFlux = false)
        {
            if (string.IsNullOrEmpty(templatePath)) templatePath = DefaultWorkflowPath;
            string json = File.ReadAllText(templatePath);
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            object raw;
            try { raw = ser.DeserializeObject(json); }
            catch (Exception ex) { throw new Exception("Workflow file is not valid JSON: " + ex.Message); }
            var root = raw as Dictionary<string, object>;
            if (root == null) throw new Exception("Workflow template is not a JSON object.");

            var rnd = new Random();
            if (seed < 0)
            {
                seed = ((long)(uint)rnd.Next() << 32) | (long)(uint)rnd.Next();
                if (seed < 0) seed = -seed;
            }

            var nodes = root.ContainsKey("nodes") ? root["nodes"] as IList : null;
            var links = root.ContainsKey("links") ? root["links"] as IList : null;
            if (nodes != null && links != null)
            {
                string out_ = ConvertUiFormat(root, nodes, links, prompt, seed, initImageName, steps, fluxSteps, fluxOnly, skipFlux, ser);
                string note = fluxOnly ? "UI format flux-only"
                    : skipFlux ? "UI format no-flux (init feeds Tripo directly)"
                    : "UI format (" + nodes.Count + " nodes)";
                return new BuildResult
                {
                    PromptJson = out_,
                    Seed = seed,
                    FormatNote = note
                };
            }

            if (steps < 1) steps = 20;
            if (fluxSteps < 1) fluxSteps = 4;
            if (IsApiFormat(root))
            {
                string out_ = ConvertApiFormat(root, prompt, seed, initImageName, steps, fluxSteps, ser);
                return new BuildResult
                {
                    PromptJson = out_,
                    Seed = seed,
                    FormatNote = "API format (" + root.Count + " nodes)"
                };
            }

            var keys = new List<string>(root.Keys);
            throw new Exception("Workflow template is neither UI format (nodes/links) nor API format " +
                "(node-id map). Top-level keys: " + string.Join(", ", keys.ToArray()) + ".");
        }

        #region API format path

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

        private static string ConvertApiFormat(Dictionary<string, object> root, string prompt,
            long seed, string initImageName, int steps, int fluxSteps, JavaScriptSerializer ser)
        {
            long kSeed = seed & 0x7FFFFFFFFFFFFFFF;
            long fluxSeed = (seed ^ 0x9E3779B9L) & 0x7FFFFFFFFFFFFFFF;

            var api = ser.DeserializeObject(ser.Serialize(root)) as Dictionary<string, object>;

            var dropIds = new HashSet<string>();
            string promptId = null;
            foreach (var kv in api)
            {
                var node = kv.Value as Dictionary<string, object>;
                if (node == null) continue;
                string ct = node.ContainsKey("class_type") ? node["class_type"] as string : null;
                if (ct == "PreviewImage" || ct == "PreviewGaussianSplat" || ct == "JarJarSubmit3D")
                    dropIds.Add(kv.Key);
                else if (ct == "PrimitiveStringMultiline" && promptId == null)
                    promptId = kv.Key;
            }
            if (api.ContainsKey(PromptNode))
            {
                var n = api[PromptNode] as Dictionary<string, object>;
                if (n != null && (n["class_type"] as string) == "PrimitiveStringMultiline")
                    promptId = PromptNode;
            }

            foreach (string id in dropIds) api.Remove(id);

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
                    if (arr != null && arr.Count >= 1 && arr[0] is string && dropIds.Contains((string)arr[0]))
                        dead.Add(ik.Key);
                }
                foreach (string k in dead) inputs.Remove(k);

                string ct = node.ContainsKey("class_type") ? node["class_type"] as string : null;
                if (kv.Key == promptId) inputs["value"] = prompt;
                if (ct == "KSampler" && inputs.ContainsKey("seed")) inputs["seed"] = kSeed;
                if (ct == "KSampler" && inputs.ContainsKey("steps")) inputs["steps"] = steps;
                if (ct == "Flux2Scheduler" && inputs.ContainsKey("steps")) inputs["steps"] = fluxSteps;
                if (ct == "VAEDecodeTripoSplat" && inputs.ContainsKey("seed")) inputs["seed"] = fluxSeed;
                if (inputs.ContainsKey("noise_seed")) inputs["noise_seed"] = fluxSeed;
                if (ct == "LoadImage" && !string.IsNullOrEmpty(initImageName) && inputs.ContainsKey("image"))
                    inputs["image"] = initImageName;
            }

            if (promptId == null)
                throw new Exception("No PrimitiveStringMultiline prompt node found in API workflow.");
            EnsurePlySaver(api, null);
            return ser.Serialize(api);
        }

        #endregion

        #region UI format path (with subgraph expansion)

        private class UiContext
        {
            public Dictionary<int, int[]> LinkOrigin = new Dictionary<int, int[]>();
            public Dictionary<string, string> RefRewrite = new Dictionary<string, string>(); // outerId -> "newId:slot"
            public HashSet<string> Drop = new HashSet<string>();
            public long KSeed;
            public long FluxSeed;
            public string Prompt;
            public string InitImage;
            public int Steps;
            public int FluxSteps;
        }

        private static string ConvertUiFormat(Dictionary<string, object> root, IList nodes, IList links,
            string prompt, long seed, string initImageName, int steps, int fluxSteps, bool fluxOnly, bool skipFlux,
            JavaScriptSerializer ser)
        {
            var ctx = new UiContext
            {
                Prompt = prompt,
                InitImage = initImageName,
                Steps = steps,
                FluxSteps = fluxSteps,
                KSeed = seed & 0x7FFFFFFFFFFFFFFF,
                FluxSeed = (seed ^ 0x9E3779B9L) & 0x7FFFFFFFFFFFFFFF
            };
            foreach (string d in DroppedNodes) ctx.Drop.Add(d);
            // No-flux 3D: drop the Flux subgraph shell and the prompt box
            // (nothing else consumes it: 134 is dropped, 131 is dropped).
            if (skipFlux && !fluxOnly)
            {
                ctx.Drop.Add(FluxNode);
                ctx.Drop.Add(PromptNode);
            }

            foreach (object lo in links)
            {
                var l = lo as IList;
                if (l == null || l.Count < 5) continue;
                ctx.LinkOrigin[ToInt(l[0])] = new int[] { ToInt(l[1]), ToInt(l[2]) };
            }

            // No-flux 3D: everything the Flux IMAGE output fed
            // (remove-background, Tripo preprocess, join-alpha, preview)
            // now reads the staged init image straight from LoadImage.
            if (skipFlux && !fluxOnly)
            {
                var remap = new List<int>();
                foreach (var kv in ctx.LinkOrigin)
                    if (kv.Value[0] == 131 && kv.Value[1] == 0) remap.Add(kv.Key);
                foreach (int linkId in remap)
                    ctx.LinkOrigin[linkId] = new int[] { 132, 0 };
            }

            // Index outer nodes by id.
            var byId = new Dictionary<string, Dictionary<string, object>>();
            foreach (object no in nodes)
            {
                var node = no as Dictionary<string, object>;
                if (node == null) continue;
                string id = Convert.ToString(node["id"], System.Globalization.CultureInfo.InvariantCulture);
                byId[id] = node;
            }

            // Subgraph definitions by id.
            var subgraphs = new Dictionary<string, Dictionary<string, object>>();
            object defsObj;
            if (root.TryGetValue("definitions", out defsObj))
            {
                var defs = defsObj as Dictionary<string, object>;
                if (defs != null)
                {
                    object subsObj;
                    if (defs.TryGetValue("subgraphs", out subsObj))
                    {
                        var subs = subsObj as IList;
                        if (subs != null)
                        {
                            foreach (object so in subs)
                            {
                                var sd = so as Dictionary<string, object>;
                                if (sd == null || !sd.ContainsKey("id")) continue;
                                subgraphs[Convert.ToString(sd["id"])] = sd;
                            }
                        }
                    }
                }
            }

            var api = new Dictionary<string, object>();

            // First pass: expand subgraph nodes.
            foreach (var kv in byId)
            {
                string id = kv.Key;
                if (ctx.Drop.Contains(id)) continue;
                string type = kv.Value.ContainsKey("type") ? kv.Value["type"] as string : null;
                if (type == null || !subgraphs.ContainsKey(type)) continue;
                ExpandSubgraph(id, kv.Value, subgraphs[type], ctx, api);
            }

            // Second pass: convert plain nodes (skipping expanded subgraph shells).
            // Flux-only mode keeps just LoadImage + prompt; everything else
            // comes from the expansion above.
            foreach (var kv in byId)
            {
                string id = kv.Key;
                if (ctx.Drop.Contains(id)) continue;
                if (fluxOnly && id != LoadImageNode && id != PromptNode) continue;
                string type = kv.Value.ContainsKey("type") ? kv.Value["type"] as string : null;
                if (string.IsNullOrEmpty(type)) continue;
                if (subgraphs.ContainsKey(type)) continue; // expanded above
                api[id] = new Dictionary<string, object>
                {
                    { "class_type", type },
                    { "inputs", BuildInputs(id, kv.Value, ctx) }
                };
            }

            if (!skipFlux && !api.ContainsKey(PromptNode)) throw new Exception("Prompt node 138 missing from template.");
            if (!skipFlux && !api.ContainsKey(FluxNode) && !ctx.RefRewrite.ContainsKey(FluxNode))
                throw new Exception("Flux node 131 missing from template and no subgraph expansion covered it.");
            if (fluxOnly)
                EnsureImageSaver(api, ctx);
            else
                EnsurePlySaver(api, ctx);
            return ser.Serialize(api);
        }

        /// <summary>
        /// Flux-only companion to EnsurePlySaver: saves the expanded Flux
        /// image (VAEDecode output) as a regular image for init-image use.
        /// Also a real OUTPUT_NODE, keeping validation happy.
        /// </summary>
        private static void EnsureImageSaver(Dictionary<string, object> api, UiContext ctx)
        {
            if (api.ContainsKey("mm_saveimg")) return;
            string rw;
            if (ctx == null || !ctx.RefRewrite.TryGetValue(FluxNode, out rw))
                throw new Exception("Flux expansion missing; cannot build image-only graph.");
            int c = rw.LastIndexOf(':');
            int slot;
            if (c <= 0 || !int.TryParse(rw.Substring(c + 1), out slot))
                throw new Exception("Flux expansion missing; cannot build image-only graph.");
            api["mm_saveimg"] = new Dictionary<string, object>
            {
                { "class_type", "SaveImage" },
                { "inputs", new Dictionary<string, object>
                    {
                        { "filename_prefix", "mm_init" },
                        { "images", new object[] { rw.Substring(0, c), slot } }
                    }
                }
            };
        }

        /// <summary>
        /// SplatToFile3D only converts SPLAT -&gt; model_3d in memory; the file
        /// itself is written by a saver node (SaveGLB with a 3d/ prefix writes
        /// the .ply, mirroring the proven frontend setup). Without it the run
        /// "succeeds" but no file ever lands on disk or in history.
        /// SaveGLB is also a real OUTPUT_NODE, which keeps validation happy.
        /// </summary>
        private static void EnsurePlySaver(Dictionary<string, object> api, UiContext ctx)
        {
            string splatId = null;
            foreach (var kv in api)
            {
                var node = kv.Value as Dictionary<string, object>;
                if (node == null) continue;
                if (string.Equals(node.ContainsKey("class_type") ? node["class_type"] as string : null,
                    "SplatToFile3D", StringComparison.Ordinal))
                { splatId = kv.Key; break; }
            }
            if (splatId == null || api.ContainsKey("mm_saveply")) return;
            object meshRef = new object[] { splatId, 0 };
            string rw;
            if (ctx != null && ctx.RefRewrite.TryGetValue(splatId, out rw))
            {
                int c = rw.LastIndexOf(':');
                int slot;
                if (c > 0 && int.TryParse(rw.Substring(c + 1), out slot))
                    meshRef = new object[] { rw.Substring(0, c), slot };
            }
            api["mm_saveply"] = new Dictionary<string, object>
            {
                { "class_type", "SaveGLB" },
                { "inputs", new Dictionary<string, object>
                    {
                        { "mesh", meshRef },
                        { "filename_prefix", "3d/PLY_" }
                    }
                }
            };
        }

        /// <summary>
        /// Expand one subgraph node into plain API nodes. Returns via api +
        /// ctx.RefRewrite (outer output 0 -> inner node "id:slot").
        /// </summary>
        private static void ExpandSubgraph(string outerId, Dictionary<string, object> outerNode,
            Dictionary<string, object> sub, UiContext ctx, Dictionary<string, object> api)
        {
            string prefix = "sg" + outerId + "_";

            // Outer values feeding the subgraph inputs (links resolved with
            // rewrite of previously-expanded nodes; widgets with injections).
            var outerInputs = BuildInputs(outerId, outerNode, ctx);
            var outerByName = new Dictionary<string, object>();
            var outerList = outerNode.ContainsKey("inputs") ? outerNode["inputs"] as IList : null;
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

            // Subgraph inputs: linkId set + name lookup.
            var sgInputLinks = new Dictionary<int, string>(); // innerLinkId -> input name
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

            // Inner link origins from outputs' link arrays.
            var origin = new Dictionary<int, string>(); // linkId -> "nodeId:outputIndex"
            foreach (object no in innerNodes)
            {
                var node = no as Dictionary<string, object>;
                if (node == null) continue;
                string nid = prefix + Convert.ToString(node["id"], System.Globalization.CultureInfo.InvariantCulture);
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

            // Convert inner nodes.
            foreach (object no in innerNodes)
            {
                var node = no as Dictionary<string, object>;
                if (node == null) continue;
                string nid = prefix + Convert.ToString(node["id"], System.Globalization.CultureInfo.InvariantCulture);
                string type = node.ContainsKey("type") ? node["type"] as string : null;
                if (string.IsNullOrEmpty(type)) continue;

                var inputs = new Dictionary<string, object>();
                var inList = node.ContainsKey("inputs") ? node["inputs"] as IList : null;
                var fullWidgets = FullWidgetNames(inList);
                var values = WidgetValues(node, fullWidgets.Count);
                var named = NamedWidgets(node);

                for (int k = 0; k < fullWidgets.Count; k++)
                {
                    string wname = fullWidgets[k];
                    // Is this widget shadowed by a link?
                    int linkId = InnerLinkOf(inList, wname);
                    if (linkId >= 0)
                    {
                        string sgIn;
                        if (sgInputLinks.TryGetValue(linkId, out sgIn))
                        {
                            // Fed by the outer node: link or literal.
                            object ov;
                            if (outerByName.TryGetValue(sgIn, out ov) && ov != null)
                                inputs[wname] = ov;
                            // else: leave unset -> backend default.
                        }
                        else
                        {
                            string org;
                            if (origin.TryGetValue(linkId, out org))
                            {
                                int c = org.LastIndexOf(':');
                                inputs[wname] = new object[]
                                {
                                    org.Substring(0, c),
                                    int.Parse(org.Substring(c + 1))
                                };
                            }
                        }
                        continue;
                    }
                    // Pure widget input.
                    object v;
                    if (named != null && named.TryGetValue(wname, out v)) inputs[wname] = v;
                    else if (k < values.Count) inputs[wname] = values[k];
                    // User's Flux steps dial (distilled schedule count).
                    if (string.Equals(type, "Flux2Scheduler", StringComparison.Ordinal) && wname == "steps")
                        inputs[wname] = ctx.FluxSteps;
                }

                // Non-widget linked inputs (MODEL/CLIP/LATENT/... have no widget).
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
                                inputs[n] = new object[]
                                {
                                    org.Substring(0, c),
                                    int.Parse(org.Substring(c + 1))
                                };
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

            // Subgraph outputs -> rewrite outer references.
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
                                        ctx.RefRewrite[outerId] = org;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (!ctx.RefRewrite.ContainsKey(outerId))
                throw new Exception("Could not resolve subgraph output for node " + outerId + ".");
        }

        /// <summary>Link id of the named input, or -1 when unlinked/absent.</summary>
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

        /// <summary>Build API inputs for one UI node (outer graph).</summary>
        private static Dictionary<string, object> BuildInputs(string id,
            Dictionary<string, object> node, UiContext ctx)
        {
            var inputs = new Dictionary<string, object>();
            var nodeInputs = node.ContainsKey("inputs") ? node["inputs"] as IList : null;

            var linkedNames = new HashSet<string>();
            if (nodeInputs != null)
            {
                foreach (object io in nodeInputs)
                {
                    var inp = io as Dictionary<string, object>;
                    if (inp == null || !inp.ContainsKey("name")) continue;
                    string iname = inp["name"] as string;
                    object linkObj = inp.ContainsKey("link") ? inp["link"] : null;
                    if (linkObj == null) continue;
                    int linkId = ToInt(linkObj);
                    int[] org;
                    if (!ctx.LinkOrigin.TryGetValue(linkId, out org)) continue;
                    string originId = org[0].ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (ctx.Drop.Contains(originId)) continue;
                    string rewrite;
                    if (ctx.RefRewrite.TryGetValue(originId, out rewrite))
                    {
                        int c = rewrite.LastIndexOf(':');
                        inputs[iname] = new object[]
                        {
                            rewrite.Substring(0, c),
                            int.Parse(rewrite.Substring(c + 1))
                        };
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
                if (linkedNames.Contains(wname)) continue; // link wins
                object val;
                if (named != null && named.TryGetValue(wname, out val)) { /* use named */ }
                else val = k < values.Count ? values[k] : null;

                if (id == PromptNode && wname == "value") val = ctx.Prompt;
                else if (id == KSamplerNode && wname == "seed") val = ctx.KSeed;
                else if (id == KSamplerNode && wname == "steps") val = ctx.Steps;
                else if (id == TripoDecodeNode && wname == "seed") val = ctx.FluxSeed;
                else if (id == FluxNode && wname == "noise_seed") val = ctx.FluxSeed;
                else if (id == LoadImageNode && wname == "image" && !string.IsNullOrEmpty(ctx.InitImage)) val = ctx.InitImage;
                inputs[wname] = val;
            }
            return inputs;
        }

        /// <summary>Widget input names in declaration order (incl. linked ones
        /// so widgets_values positions stay aligned; UI-only "upload" skipped).</summary>
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

        /// <summary>widgets_values minus seed-control widgets.</summary>
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
