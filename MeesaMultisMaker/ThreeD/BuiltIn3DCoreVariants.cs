using System;
using System.Collections;
using System.Globalization;

namespace MeesaMultisMaker.ThreeD
{
    // =====================================================================
    // Built-in frozen copies of the core TripoSplat 3D workflow variants.
    //
    // Freeze source directory (UI format):
    //   D:\ComfyUIPortable\ComfyUI\user\default\workflows\
    // One public static class per file: B1.json, B2.json, B3.json,
    // B4.json, B5.json, B6.json, B7.json, BEST 3D EVER .json,
    // BEST 3D EVER _TRY2.json, MultisMaker3.json, MultisMaker4.json,
    // MultisMaker5.json, MultisMaker12.json, MultisMaker13.json,
    // MultisMaker15.json, MultisMaker16.json, MultisMaker17.json,
    // MultisMaker_ObjectGenerator.json, TripoSR_Moshu_Example.json,
    // GLB OUTPUT.json, ODIN_CLICK_HERE.json,
    // VRAK_IMAGE_TO_3D_BIPED.json, HELP HEREV5.json, HELP HEREV6.json.
    // Skipped: MultisMaker1 - Copy.json (byte duplicate of MultisMaker1,
    // already covered by BuiltIn3DWorkflow) and *.bak files.
    //
    // Expansion semantics mirror MultisMaker1Workflow.Build (read-only
    // reference, not edited): a Flux.2 Klein subgraph shell
    // (f1793303-e48d-4710-ab47-8ffa8cb117f0, 17 inner nodes) is expanded
    // inline with prefix sg131_ (sg150_/sg174_ for the B6 chain), outer
    // links to the shell IMAGE output are rewritten to the inner
    // VAEDecode output, and the tripo KSampler seed / decode seed are
    // derived from the caller seed (kSeed = seed masked,
    // dSeed = seed ^ 0x9E3779B9 masked), exactly like BuiltIn3DWorkflow.
    // The b64333d5-... tripo-core subgraph used by the BEST/HELP/GLB/
    // ODIN/VRAK files (14 inner nodes + nested 3a670214-... background
    // subgraph with 4 inner nodes) is likewise expanded inline with
    // prefix sg88_ (background shell further prefixed sg88_85_).
    // MultisMaker16.json is already flat (41 fused nodes, no subgraph)
    // and is emitted flat with the same node ids.
    //
    // Uniformly dropped (documented per class): JarJarSubmit3D upload
    // nodes (MultisMaker1Workflow.DroppedNodes precedent), MarkdownNote,
    // PreviewAny debug viewers, JarJar preview/load meshes, the BEST
    // Blender/UV bake chain (103 UnwrapMesh, 104 RenderUVAtlas, 106/116
    // PreviewImage, 107 GeomPackUVUnwrap, 108 JarJarUVUnwrap with a
    // hard-coded Blender path, 110 JarJarLoadSplatPly, 111 JarJarLoadMesh,
    // 112 JarJarPreviewMesh, 115 RenderMesh), MM13 node 154 Reroute
    // (wired through: 158 feeds the flux shell directly), 157 PreviewAny,
    // and 159/160 StringReplace (their 147 slot-3 STRING target only
    // exists in the file's older SaveGaussianSplat revision).
    // DynamicCombo inputs are encoded flattened (combo name -> selected
    // key plus sibling sub-inputs), matching backend execution mapping.
    // Two-widget SaveGLB nodes (['3d/...', '']) predate the current
    // single-input schema; the empty second widget is dropped.
    // MM16 144/147 carry 2 widgets (older revision without viewport_state)
    // and are emitted as the 4-input union with viewport_state "".
    // No File I/O: every graph is emitted from string concatenation with
    // EscapeJson, following the BuiltIn3DWorkflow pattern.
    // =====================================================================

    /// <summary>
    /// Shared string-concatenation emitters for the core variants.
    /// Per-file differences stay in each variant class as DEFAULT_* consts.
    /// </summary>
    internal static class CoreVariantJson
    {
        public static string EscapeJson(string value)
        {
            if (value == null)
                return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
        }

        public static void SplitSeeds(long seed, out string kSeedStr, out string fluxSeedStr)
        {
            if (seed < 0)
                seed = ((long)(uint)new Random().Next() << 32) | (long)(uint)new Random().Next();
            if (seed < 0)
                seed = -seed;
            var inv = CultureInfo.InvariantCulture;
            kSeedStr = (seed & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
            fluxSeedStr = ((seed ^ 0x9E3779B9L) & 0x7FFFFFFFFFFFFFFFL).ToString(inv);
        }

        public static string B(bool value)
        {
            return value ? "true" : "false";
        }

        public static string JoinNodes(ArrayList parts)
        {
            return string.Join(",\n", (string[])parts.ToArray(typeof(string)));
        }

        // Flux.2 Klein edit subgraph (f1793303-..., also B6 c497d832-... /
        // 14d43442-...) expanded inline. Output node id is prefix + "65".
        // promptRef / imageRef are preformatted JSON refs, e.g. "[\"138\", 0]".
        public static string FluxStage(string p, string promptRef, string imageRef,
            string fluxSteps, string noiseSeed, string unet, string clip, string vae)
        {
            ArrayList n = new ArrayList();
            n.Add("  \"" + p + "70\": {\n" +
                "    \"inputs\": {\n" +
                "      \"unet_name\": \"" + unet + "\",\n" +
                "      \"weight_dtype\": \"default\"\n" +
                "    },\n" +
                "    \"class_type\": \"UNETLoader\"\n" +
                "  }");
            n.Add("  \"" + p + "71\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip_name\": \"" + clip + "\",\n" +
                "      \"type\": \"flux2\",\n" +
                "      \"device\": \"default\"\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPLoader\"\n" +
                "  }");
            n.Add("  \"" + p + "72\": {\n" +
                "    \"inputs\": {\n" +
                "      \"vae_name\": \"" + vae + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"VAELoader\"\n" +
                "  }");
            n.Add("  \"" + p + "80\": {\n" +
                "    \"inputs\": {\n" +
                "      \"image\": " + imageRef + ",\n" +
                "      \"upscale_method\": \"nearest-exact\",\n" +
                "      \"megapixels\": 0.25,\n" +
                "      \"resolution_steps\": 1\n" +
                "    },\n" +
                "    \"class_type\": \"ImageScaleToTotalPixels\"\n" +
                "  }");
            n.Add("  \"" + p + "130\": {\n" +
                "    \"inputs\": {\n" +
                "      \"image\": [\"" + p + "80\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"GetImageSize\"\n" +
                "  }");
            n.Add("  \"" + p + "122\": {\n" +
                "    \"inputs\": {\n" +
                "      \"pixels\": [\"" + p + "80\", 0],\n" +
                "      \"vae\": [\"" + p + "72\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"VAEEncode\"\n" +
                "  }");
            n.Add("  \"" + p + "74\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip\": [\"" + p + "71\", 0],\n" +
                "      \"text\": " + promptRef + "\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPTextEncode\"\n" +
                "  }");
            n.Add("  \"" + p + "82\": {\n" +
                "    \"inputs\": {\n" +
                "      \"conditioning\": [\"" + p + "74\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"ConditioningZeroOut\"\n" +
                "  }");
            n.Add("  \"" + p + "123\": {\n" +
                "    \"inputs\": {\n" +
                "      \"conditioning\": [\"" + p + "74\", 0],\n" +
                "      \"latent\": [\"" + p + "122\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"ReferenceLatent\"\n" +
                "  }");
            n.Add("  \"" + p + "121\": {\n" +
                "    \"inputs\": {\n" +
                "      \"conditioning\": [\"" + p + "82\", 0],\n" +
                "      \"latent\": [\"" + p + "122\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"ReferenceLatent\"\n" +
                "  }");
            n.Add("  \"" + p + "66\": {\n" +
                "    \"inputs\": {\n" +
                "      \"width\": [\"" + p + "130\", 0],\n" +
                "      \"height\": [\"" + p + "130\", 1],\n" +
                "      \"batch_size\": 1\n" +
                "    },\n" +
                "    \"class_type\": \"EmptyFlux2LatentImage\"\n" +
                "  }");
            n.Add("  \"" + p + "62\": {\n" +
                "    \"inputs\": {\n" +
                "      \"steps\": " + fluxSteps + ",\n" +
                "      \"width\": [\"" + p + "130\", 0],\n" +
                "      \"height\": [\"" + p + "130\", 1]\n" +
                "    },\n" +
                "    \"class_type\": \"Flux2Scheduler\"\n" +
                "  }");
            n.Add("  \"" + p + "63\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"" + p + "70\", 0],\n" +
                "      \"positive\": [\"" + p + "123\", 0],\n" +
                "      \"negative\": [\"" + p + "121\", 0],\n" +
                "      \"cfg\": 1\n" +
                "    },\n" +
                "    \"class_type\": \"CFGGuider\"\n" +
                "  }");
            n.Add("  \"" + p + "61\": {\n" +
                "    \"inputs\": {\n" +
                "      \"sampler_name\": \"euler\"\n" +
                "    },\n" +
                "    \"class_type\": \"KSamplerSelect\"\n" +
                "  }");
            n.Add("  \"" + p + "73\": {\n" +
                "    \"inputs\": {\n" +
                "      \"noise_seed\": " + noiseSeed + "\n" +
                "    },\n" +
                "    \"class_type\": \"RandomNoise\"\n" +
                "  }");
            n.Add("  \"" + p + "64\": {\n" +
                "    \"inputs\": {\n" +
                "      \"noise\": [\"" + p + "73\", 0],\n" +
                "      \"guider\": [\"" + p + "63\", 0],\n" +
                "      \"sampler\": [\"" + p + "61\", 0],\n" +
                "      \"sigmas\": [\"" + p + "62\", 0],\n" +
                "      \"latent_image\": [\"" + p + "66\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"SamplerCustomAdvanced\"\n" +
                "  }");
            n.Add("  \"" + p + "65\": {\n" +
                "    \"inputs\": {\n" +
                "      \"samples\": [\"" + p + "64\", 0],\n" +
                "      \"vae\": [\"" + p + "72\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"VAEDecode\"\n" +
                "  }");
            return JoinNodes(n);
        }

        public static string LoadImageNode(string id, string file)
        {
            return "  \"" + id + "\": {\n" +
                "    \"inputs\": {\n" +
                "      \"image\": \"" + file + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"LoadImage\"\n" +
                "  }";
        }

        public static string PromptNode(string id, string cls, string prompt)
        {
            return "  \"" + id + "\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": \"" + prompt + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"" + cls + "\"\n" +
                "  }";
        }

        public static string SaveImageNode(string id, string prefix, string imagesRef)
        {
            return "  \"" + id + "\": {\n" +
                "    \"inputs\": {\n" +
                "      \"filename_prefix\": \"" + prefix + "\",\n" +
                "      \"images\": " + imagesRef + "\n" +
                "    },\n" +
                "    \"class_type\": \"SaveImage\"\n" +
                "  }";
        }

        public static string SaveGlbNode(string id, string meshRef, string prefix)
        {
            return "  \"" + id + "\": {\n" +
                "    \"inputs\": {\n" +
                "      \"mesh\": " + meshRef + ",\n" +
                "      \"filename_prefix\": \"" + prefix + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"SaveGLB\"\n" +
                "  }";
        }

        public static string SplatMeshNode(string id, string splatRef, OuterSpec s)
        {
            var inv = CultureInfo.InvariantCulture;
            return "  \"" + id + "\": {\n" +
                "    \"inputs\": {\n" +
                "      \"splat\": " + splatRef + ",\n" +
                "      \"resolution\": " + s.MeshRes.ToString(inv) + ",\n" +
                "      \"kernel\": " + s.MeshKernel.ToString(inv) + ",\n" +
                "      \"smooth\": " + s.MeshSmooth.ToString(inv) + ",\n" +
                "      \"level\": " + s.MeshLevel.ToString(inv) + ",\n" +
                "      \"min_component\": " + s.MeshMinComp.ToString(inv) + ",\n" +
                "      \"min_opacity\": " + s.MeshMinOp.ToString(inv) + ",\n" +
                "      \"color_sharpen\": " + s.MeshSharp.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"SplatToMesh\"\n" +
                "  }";
        }

        // Tripo outer body for the MultisMaker-shaped files (100/101/103/
        // 104/105/106/107/110/113/114 + switches, previews, savers).
        // fluxRef is a preformatted JSON ref feeding 103/113/116/133.
        public static string TripoBody(OuterSpec s, TripoRt r, string fluxRef, string loadRef)
        {
            var inv = CultureInfo.InvariantCulture;
            ArrayList n = new ArrayList();
            n.Add("  \"100\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip_vision\": [\"104\", 0],\n" +
                "      \"vae\": [\"106\", 0],\n" +
                "      \"image\": [\"103\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"TripoSplatConditioning\"\n" +
                "  }");
            if (s.HasSwitch102)
            {
                string onFalse = s.HasInvert109 ? "\n      \"on_false\": [\"109\", 0]," : string.Empty;
                n.Add("  \"102\": {\n" +
                    "    \"inputs\": {" + onFalse + "\n" +
                    "      \"on_true\": [\"113\", 0],\n" +
                    "      \"switch\": true\n" +
                    "    },\n" +
                    "    \"class_type\": \"ComfySwitchNode\"\n" +
                    "  }");
            }
            if (s.HasInvert109)
            {
                n.Add("  \"109\": {\n" +
                    "    \"inputs\": {\n" +
                    "      \"mask\": " + loadRef.Replace(", 0]", ", 1]") + "\n" +
                    "    },\n" +
                    "    \"class_type\": \"InvertMask\"\n" +
                    "  }");
            }
            string maskRef = s.HasSwitch102 ? "[\"102\", 0]" : "[\"113\", 0]";
            n.Add("  \"103\": {\n" +
                "    \"inputs\": {\n" +
                "      \"image\": " + fluxRef + ",\n" +
                "      \"mask\": " + maskRef + ",\n" +
                "      \"erode_radius\": 1,\n" +
                "      \"size\": 1024\n" +
                "    },\n" +
                "    \"class_type\": \"TripoSplatPreprocessImage\"\n" +
                "  }");
            n.Add("  \"104\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip_name\": \"" + r.ClipVision + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPVisionLoader\"\n" +
                "  }");
            n.Add("  \"105\": {\n" +
                "    \"inputs\": {\n" +
                "      \"vae_name\": \"" + r.TripoVae + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"VAELoader\"\n" +
                "  }");
            n.Add("  \"106\": {\n" +
                "    \"inputs\": {\n" +
                "      \"vae_name\": \"" + r.CondVae + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"VAELoader\"\n" +
                "  }");
            n.Add("  \"110\": {\n" +
                "    \"inputs\": {\n" +
                "      \"unet_name\": \"" + r.TripoUnet + "\",\n" +
                "      \"weight_dtype\": \"" + s.UnetDtype + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"UNETLoader\"\n" +
                "  }");
            if (s.HasSwitch111)
            {
                n.Add("  \"111\": {\n" +
                    "    \"inputs\": {\n" +
                    "      \"on_false\": [\"110\", 0],\n" +
                    "      \"on_true\": [\"112\", 0],\n" +
                    "      \"switch\": true\n" +
                    "    },\n" +
                    "    \"class_type\": \"ComfySwitchNode\"\n" +
                    "  }");
            }
            if (s.HasPreview112)
            {
                n.Add("  \"112\": {\n" +
                    "    \"inputs\": {\n" +
                    "      \"model\": [\"110\", 0],\n" +
                    "      \"vae\": [\"105\", 0],\n" +
                    "      \"octree_level\": 5,\n" +
                    "      \"num_gaussians\": " + s.Preview112Nums.ToString(inv) + ",\n" +
                    "      \"yaw\": 90,\n" +
                    "      \"pitch\": 15,\n" +
                    "      \"point_size\": " + s.Preview112Point.ToString(inv) + "\n" +
                    "    },\n" +
                    "    \"class_type\": \"TripoSplatSamplingPreview\"\n" +
                    "  }");
            }
            n.Add("  \"113\": {\n" +
                "    \"inputs\": {\n" +
                "      \"bg_removal_model\": [\"114\", 0],\n" +
                "      \"image\": " + fluxRef + "\n" +
                "    },\n" +
                "    \"class_type\": \"RemoveBackground\"\n" +
                "  }");
            n.Add("  \"114\": {\n" +
                "    \"inputs\": {\n" +
                "      \"bg_removal_name\": \"" + r.BgRemoval + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"LoadBackgroundRemovalModel\"\n" +
                "  }");
            if (!s.NoAlphaJoin)
            {
                n.Add("  \"115\": {\n" +
                    "    \"inputs\": {\n" +
                    "      \"mask\": [\"113\", 0]\n" +
                    "    },\n" +
                    "    \"class_type\": \"InvertMask\"\n" +
                    "  }");
                n.Add("  \"116\": {\n" +
                    "    \"inputs\": {\n" +
                    "      \"image\": " + fluxRef + ",\n" +
                    "      \"alpha\": [\"115\", 0]\n" +
                    "    },\n" +
                    "    \"class_type\": \"JoinImageWithAlpha\"\n" +
                    "  }");
            }
            string modelRef = s.HasSwitch111 ? "[\"111\", 0]" : "[\"110\", 0]";
            n.Add("  \"101\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": " + modelRef + ",\n" +
                "      \"positive\": [\"100\", 0],\n" +
                "      \"negative\": [\"100\", 1],\n" +
                "      \"latent_image\": [\"100\", 2],\n" +
                "      \"seed\": " + r.KSeed + ",\n" +
                "      \"steps\": " + r.Steps + ",\n" +
                "      \"cfg\": " + s.Cfg + ",\n" +
                "      \"sampler_name\": \"" + s.Sampler + "\",\n" +
                "      \"scheduler\": \"simple\",\n" +
                "      \"denoise\": 1\n" +
                "    },\n" +
                "    \"class_type\": \"KSampler\"\n" +
                "  }");
            n.Add("  \"107\": {\n" +
                "    \"inputs\": {\n" +
                "      \"samples\": [\"101\", 0],\n" +
                "      \"vae\": [\"105\", 0],\n" +
                "      \"num_gaussians\": " + r.NumGaussians + ",\n" +
                "      \"seed\": " + r.DSeed + "\n" +
                "    },\n" +
                "    \"class_type\": \"VAEDecodeTripoSplat\"\n" +
                "  }");
            n.Add("  \"92\": {\n" +
                "    \"inputs\": {\n" +
                "      \"splat\": [\"107\", 0],\n" +
                "      \"format\": \"" + s.SplatFormat + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"SplatToFile3D\"\n" +
                "  }");
            if (s.HasP133)
            {
                n.Add("  \"133\": {\n" +
                    "    \"inputs\": {\n" +
                    "      \"images\": " + fluxRef + "\n" +
                    "    },\n" +
                    "    \"class_type\": \"PreviewImage\"\n" +
                    "  }");
            }
            if (s.HasP144)
            {
                n.Add("  \"144\": {\n" +
                    "    \"inputs\": {\n" +
                    "      \"model_3d\": [\"92\", 0],\n" +
                    "      \"viewport_state\": \"\",\n" +
                    "      \"width\": " + s.PreviewWH.ToString(inv) + ",\n" +
                    "      \"height\": " + s.PreviewWH.ToString(inv) + "\n" +
                    "    },\n" +
                    "    \"class_type\": \"PreviewGaussianSplat\"\n" +
                    "  }");
            }
            if (s.HasP145)
            {
                n.Add("  \"145\": {\n" +
                    "    \"inputs\": {\n" +
                    "      \"images\": [\"116\", 0]\n" +
                    "    },\n" +
                    "    \"class_type\": \"PreviewImage\"\n" +
                    "  }");
            }
            if (s.MeshKind == 1)
            {
                n.Add(SplatMeshNode("76", "[\"107\", 0]", s));
                n.Add(SaveGlbNode("67", "[\"76\", 0]", s.MeshPrefix));
            }
            else if (s.MeshKind == 2)
            {
                n.Add(SplatMeshNode("136", "[\"107\", 0]", s));
                n.Add(SaveGlbNode("137", "[\"136\", 0]", s.MeshPrefix));
            }
            if (s.Saver51)
                n.Add(SaveGlbNode("51", "[\"92\", 0]", s.PlyPrefix));
            if (s.Saver137Standalone)
                n.Add(SaveGlbNode("137", "[\"92\", 0]", s.PlyPrefix));
            if (s.Saver177)
                n.Add(SaveGlbNode("177", "[\"92\", 0]", s.Saver177Prefix));
            if (s.HasGaussExtra)
            {
                string m146 = s.Gauss146FromPreview ? "[\"144\", 0]" : "[\"92\", 0]";
                n.Add(SaveGlbNode("146", m146, s.GaussPrefix));
                n.Add("  \"147\": {\n" +
                    "    \"inputs\": {\n" +
                    "      \"model_3d\": [\"92\", 0],\n" +
                    "      \"filename_prefix\": \"" + s.GaussPrefix + "\",\n" +
                    "      \"viewport_state\": \"\",\n" +
                    "      \"width\": " + s.PreviewWH.ToString(inv) + ",\n" +
                    "      \"height\": " + s.PreviewWH.ToString(inv) + "\n" +
                    "    },\n" +
                    "    \"class_type\": \"SaveGaussianSplat\"\n" +
                    "  }");
            }
            return JoinNodes(n);
        }

        // Full outer graph for the MultisMaker-shaped files.
        // flatHead carries pre-expanded nodes that must precede the body
        // (MultisMaker16 flat flux block); promptId "158" selects the MM13
        // JarJar prompt chain instead of a Primitive box.
        public static string EmitOuter(OuterSpec s, TripoRt r, bool fluxOnly, bool skipFlux, string flatHead)
        {
            ArrayList parts = new ArrayList();
            string loadId = s.LoadImageId;
            string loadRef = "[\"" + loadId + "\", 0]";
            string fluxOutRef;
            if (skipFlux || (!s.HasFlux && s.ImageRefOverride == null))
                fluxOutRef = loadRef;
            else if (s.ImageRefOverride != null)
                fluxOutRef = s.ImageRefOverride;
            else
                fluxOutRef = "[\"" + s.FluxPrefix + "65\", 0]";

            string promptRef = null;
            if (s.PromptChain)
                promptRef = "[\"158\", 0]";
            else if (s.PromptId != null)
                promptRef = "[\"" + s.PromptId + "\", 0]";

            bool emitFlux = s.HasFlux && !skipFlux && !fluxOnly;
            bool emitFlat = flatHead != null && !skipFlux && !fluxOnly;

            if (fluxOnly)
            {
                parts.Add(LoadImageNode(loadId, r.ImageFile));
                if (s.PromptChain)
                    parts.Add(PromptChainNodes(r));
                else if (s.PromptId != null && (s.HasFlux || flatHead != null))
                    parts.Add(PromptNode(s.PromptId, s.PromptClass, r.Prompt));
                if (flatHead != null)
                    parts.Add(flatHead);
                else if (s.HasFlux)
                    parts.Add(FluxStage(s.FluxPrefix, promptRef, loadRef, r.FluxSteps, r.FluxSeed, r.FluxUnet, r.FluxClip, r.FluxVae));
                string imgRef = flatHead != null ? s.ImageRefOverride : (s.HasFlux ? "[\"" + s.FluxPrefix + "65\", 0]" : loadRef);
                parts.Add(SaveImageNode("mm_saveimg", "mm_init", imgRef));
                return "{\n" + JoinNodes(parts) + "\n}";
            }

            if (s.PromptChain)
            {
                if (!skipFlux)
                    parts.Add(PromptChainNodes(r));
            }
            else if (s.PromptId != null && s.HasFlux && !skipFlux)
            {
                parts.Add(PromptNode(s.PromptId, s.PromptClass, r.Prompt));
            }
            if (emitFlat)
                parts.Add(flatHead);
            else if (emitFlux)
                parts.Add(FluxStage(s.FluxPrefix, promptRef, loadRef, r.FluxSteps, r.FluxSeed, r.FluxUnet, r.FluxClip, r.FluxVae));
            parts.Add(LoadImageNode(loadId, r.ImageFile));
            parts.Add(TripoBody(s, r, fluxOutRef, loadRef));
            if (!s.Saver51 && !s.Saver137Standalone && !s.Saver177 && !s.HasGaussExtra)
                parts.Add(SaveGlbNode("mm_saveply", "[\"92\", 0]", s.PlyPrefix));
            return "{\n" + JoinNodes(parts) + "\n}";
        }

        // MM13 prompt chain: 156 JarJarTextList -> 155 JarJarListItem ->
        // 158 StringConcatenate (+ suffix). 161 PrimitiveInt carries the
        // batch index (the file links 161 -> 155, so the link wins over the
        // widget shadow value 1; frozen effective index is 20).
        public static string PromptChainNodes(TripoRt r)
        {
            ArrayList n = new ArrayList();
            n.Add("  \"156\": {\n" +
                "    \"inputs\": {\n" +
                "      \"text_block\": \"" + r.PromptListText + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"JarJarTextList\"\n" +
                "  }");
            n.Add("  \"161\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": " + r.BatchIndex + "\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveInt\"\n" +
                "  }");
            n.Add("  \"155\": {\n" +
                "    \"inputs\": {\n" +
                "      \"list_in\": [\"156\", 0],\n" +
                "      \"index\": [\"161\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"JarJarListItem\"\n" +
                "  }");
            n.Add("  \"158\": {\n" +
                "    \"inputs\": {\n" +
                "      \"string_a\": [\"155\", 0],\n" +
                "      \"string_b\": \"" + r.PromptSuffix + "\",\n" +
                "      \"delimiter\": \"\"\n" +
                "    },\n" +
                "    \"class_type\": \"StringConcatenate\"\n" +
                "  }");
            return JoinNodes(n);
        }

        // Tripo-core subgraph (b64333d5-...) + background subgraph
        // (3a670214-...) expanded inline with prefix q (e.g. "sg88_").
        // outerImage / outerMask are preformatted refs to the staged image.
        public static string TripoCore(CoreSpec s, CoreRt r, string outerImage, string outerMask)
        {
            string q = s.Prefix;
            ArrayList n = new ArrayList();
            n.Add("  \"" + q + "85_82\": {\n" +
                "    \"inputs\": {\n" +
                "      \"bg_removal_name\": \"" + r.BgRemoval + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"LoadBackgroundRemovalModel\"\n" +
                "  }");
            n.Add("  \"" + q + "85_81\": {\n" +
                "    \"inputs\": {\n" +
                "      \"bg_removal_model\": [\"" + q + "85_82\", 0],\n" +
                "      \"image\": " + outerImage + "\n" +
                "    },\n" +
                "    \"class_type\": \"RemoveBackground\"\n" +
                "  }");
            n.Add("  \"" + q + "85_83\": {\n" +
                "    \"inputs\": {\n" +
                "      \"mask\": [\"" + q + "85_81\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"InvertMask\"\n" +
                "  }");
            n.Add("  \"" + q + "85_84\": {\n" +
                "    \"inputs\": {\n" +
                "      \"image\": " + outerImage + ",\n" +
                "      \"alpha\": [\"" + q + "85_83\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"JoinImageWithAlpha\"\n" +
                "  }");
            n.Add("  \"" + q + "95\": {\n" +
                "    \"inputs\": {\n" +
                "      \"mask\": " + outerMask + "\n" +
                "    },\n" +
                "    \"class_type\": \"InvertMask\"\n" +
                "  }");
            n.Add("  \"" + q + "35\": {\n" +
                "    \"inputs\": {\n" +
                "      \"on_false\": [\"" + q + "95\", 0],\n" +
                "      \"on_true\": [\"" + q + "85_83\", 0],\n" +
                "      \"switch\": true\n" +
                "    },\n" +
                "    \"class_type\": \"ComfySwitchNode\"\n" +
                "  }");
            n.Add("  \"" + q + "2\": {\n" +
                "    \"inputs\": {\n" +
                "      \"image\": " + outerImage + ",\n" +
                "      \"mask\": [\"" + q + "35\", 0],\n" +
                "      \"erode_radius\": 1,\n" +
                "      \"size\": " + s.PreprocessSize + "\n" +
                "    },\n" +
                "    \"class_type\": \"TripoSplatPreprocessImage\"\n" +
                "  }");
            n.Add("  \"" + q + "7\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip_name\": \"" + r.ClipVision + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPVisionLoader\"\n" +
                "  }");
            n.Add("  \"" + q + "8\": {\n" +
                "    \"inputs\": {\n" +
                "      \"vae_name\": \"" + r.CondVae + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"VAELoader\"\n" +
                "  }");
            n.Add("  \"" + q + "11\": {\n" +
                "    \"inputs\": {\n" +
                "      \"vae_name\": \"" + r.TripoVae + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"VAELoader\"\n" +
                "  }");
            n.Add("  \"" + q + "16\": {\n" +
                "    \"inputs\": {\n" +
                "      \"unet_name\": \"" + r.TripoUnet + "\",\n" +
                "      \"weight_dtype\": \"default\"\n" +
                "    },\n" +
                "    \"class_type\": \"UNETLoader\"\n" +
                "  }");
            n.Add("  \"" + q + "24\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip_vision\": [\"" + q + "7\", 0],\n" +
                "      \"vae\": [\"" + q + "8\", 0],\n" +
                "      \"image\": [\"" + q + "2\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"TripoSplatConditioning\"\n" +
                "  }");
            n.Add("  \"" + q + "97\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"" + q + "16\", 0],\n" +
                "      \"vae\": [\"" + q + "11\", 0],\n" +
                "      \"octree_level\": " + s.PreviewOct + ",\n" +
                "      \"num_gaussians\": " + s.PreviewNums + ",\n" +
                "      \"yaw\": 90,\n" +
                "      \"pitch\": 15,\n" +
                "      \"point_size\": " + s.PreviewPoint + "\n" +
                "    },\n" +
                "    \"class_type\": \"TripoSplatSamplingPreview\"\n" +
                "  }");
            n.Add("  \"" + q + "96\": {\n" +
                "    \"inputs\": {\n" +
                "      \"on_false\": [\"" + q + "16\", 0],\n" +
                "      \"on_true\": [\"" + q + "97\", 0],\n" +
                "      \"switch\": true\n" +
                "    },\n" +
                "    \"class_type\": \"ComfySwitchNode\"\n" +
                "  }");
            n.Add("  \"" + q + "6\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"" + q + "96\", 0],\n" +
                "      \"positive\": [\"" + q + "24\", 0],\n" +
                "      \"negative\": [\"" + q + "24\", 1],\n" +
                "      \"latent_image\": [\"" + q + "24\", 2],\n" +
                "      \"seed\": " + r.KSeed + ",\n" +
                "      \"steps\": " + r.Steps + ",\n" +
                "      \"cfg\": " + s.TripoCfg + ",\n" +
                "      \"sampler_name\": \"" + s.TripoSampler + "\",\n" +
                "      \"scheduler\": \"simple\",\n" +
                "      \"denoise\": 1\n" +
                "    },\n" +
                "    \"class_type\": \"KSampler\"\n" +
                "  }");
            n.Add("  \"" + q + "55\": {\n" +
                "    \"inputs\": {\n" +
                "      \"samples\": [\"" + q + "6\", 0],\n" +
                "      \"vae\": [\"" + q + "11\", 0],\n" +
                "      \"num_gaussians\": " + r.NumGaussians + ",\n" +
                "      \"seed\": " + r.DSeed + "\n" +
                "    },\n" +
                "    \"class_type\": \"VAEDecodeTripoSplat\"\n" +
                "  }");
            n.Add("  \"" + q + "22\": {\n" +
                "    \"inputs\": {\n" +
                "      \"images\": [\"" + q + "2\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"PreviewImage\"\n" +
                "  }");
            return JoinNodes(n);
        }

        // Render turntable chain (75/79/41/42) shared by the BEST/GLB/
        // ODIN/VRAK files. splatRef is e.g. "[\"sg88_55\", 0]".
        public static string RenderChain(string splatRef, string videoPrefix)
        {
            ArrayList n = new ArrayList();
            n.Add("  \"79\": {\n" +
                "    \"inputs\": {\n" +
                "      \"mode\": \"orbit\",\n" +
                "      \"yaw\": 35,\n" +
                "      \"pitch\": 30,\n" +
                "      \"distance\": 2.5,\n" +
                "      \"target_x\": 0,\n" +
                "      \"target_y\": 0,\n" +
                "      \"target_z\": 0,\n" +
                "      \"roll\": 0,\n" +
                "      \"fov\": 35,\n" +
                "      \"zoom\": 1,\n" +
                "      \"camera_type\": \"perspective\"\n" +
                "    },\n" +
                "    \"class_type\": \"CreateCameraInfo\"\n" +
                "  }");
            n.Add("  \"75\": {\n" +
                "    \"inputs\": {\n" +
                "      \"splat\": " + splatRef + ",\n" +
                "      \"width\": 1024,\n" +
                "      \"height\": 1024,\n" +
                "      \"frames\": 75,\n" +
                "      \"splat_scale\": 1,\n" +
                "      \"sharpen\": 2,\n" +
                "      \"headlight_shading\": 0,\n" +
                "      \"opacity_threshold\": 0,\n" +
                "      \"render_style\": \"color\",\n" +
                "      \"background\": \"#848484\",\n" +
                "      \"camera_info\": [\"79\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"RenderSplat\"\n" +
                "  }");
            n.Add("  \"41\": {\n" +
                "    \"inputs\": {\n" +
                "      \"images\": [\"75\", 0],\n" +
                "      \"fps\": 25,\n" +
                "      \"bit_depth\": \"auto\",\n" +
                "      \"color_space\": \"sRGB\",\n" +
                "      \"codec\": \"none\"\n" +
                "    },\n" +
                "    \"class_type\": \"CreateVideo\"\n" +
                "  }");
            n.Add("  \"42\": {\n" +
                "    \"inputs\": {\n" +
                "      \"video\": [\"41\", 0],\n" +
                "      \"filename_prefix\": \"" + videoPrefix + "\",\n" +
                "      \"format\": \"auto\"\n" +
                "    },\n" +
                "    \"class_type\": \"SaveVideo\"\n" +
                "  }");
            return JoinNodes(n);
        }

        // Outer graph for the b64-subgraph (image-to-3D, no prompt input).
        public static string EmitCore(CoreSpec s, CoreRt r, bool fluxOnly)
        {
            ArrayList parts = new ArrayList();
            string q = s.Prefix;
            parts.Add(LoadImageNode("99", r.ImageFile));
            if (fluxOnly)
            {
                parts.Add(SaveImageNode("mm_saveimg", "mm_init", "[\"99\", 0]"));
                return "{\n" + JoinNodes(parts) + "\n}";
            }
            string outerImage = "[\"99\", 0]";
            if (s.ResizeNode)
            {
                parts.Add("  \"124\": {\n" +
                    "    \"inputs\": {\n" +
                    "      \"input\": [\"99\", 0],\n" +
                    "      \"resize_type\": \"scale dimensions\",\n" +
                    "      \"width\": 2048,\n" +
                    "      \"height\": 2048,\n" +
                    "      \"crop\": \"disabled\",\n" +
                    "      \"scale_method\": \"nearest-exact\"\n" +
                    "    },\n" +
                    "    \"class_type\": \"ResizeImageMaskNode\"\n" +
                    "  }");
                outerImage = "[\"124\", 0]";
            }
            parts.Add(TripoCore(s, r, outerImage, "[\"99\", 1]"));
            string splatRef = "[\"" + q + "55\", 0]";
            string fileSplatRef = splatRef;
            if (s.ShadowChain)
            {
                parts.Add("  \"118\": {\n" +
                    "    \"inputs\": {\n" +
                    "      \"splat\": " + splatRef + ",\n" +
                    "      \"min_opacity\": 0.02,\n" +
                    "      \"max_scale\": 0.84\n" +
                    "    },\n" +
                    "    \"class_type\": \"IdolPruneSplat\"\n" +
                    "  }");
                fileSplatRef = "[\"118\", 0]";
            }
            parts.Add("  \"92\": {\n" +
                "    \"inputs\": {\n" +
                "      \"splat\": " + fileSplatRef + ",\n" +
                "      \"format\": \"" + s.SplatFormat + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"SplatToFile3D\"\n" +
                "  }");
            parts.Add(SaveGlbNode("51", "[\"92\", 0]", s.PlyPrefix));
            if (s.MeshKind == 1)
            {
                OuterSpec ms = new OuterSpec();
                ms.MeshRes = s.MeshRes;
                ms.MeshKernel = s.MeshKernel;
                ms.MeshSmooth = s.MeshSmooth;
                ms.MeshLevel = s.MeshLevel;
                ms.MeshMinComp = s.MeshMinComp;
                ms.MeshMinOp = s.MeshMinOp;
                ms.MeshSharp = s.MeshSharp;
                ms.MeshPrefix = s.MeshPrefix;
                parts.Add(SplatMeshNode("76", splatRef, ms));
                parts.Add(SaveGlbNode("67", "[\"76\", 0]", s.MeshPrefix));
            }
            if (s.RenderChainFlag)
                parts.Add(RenderChain(splatRef, s.VideoPrefix));
            if (s.Remesh)
            {
                parts.Add("  \"101\": {\n" +
                    "    \"inputs\": {\n" +
                    "      \"mesh\": [\"76\", 0],\n" +
                    "      \"resolution\": 512,\n" +
                    "      \"sign_mode\": \"udf\",\n" +
                    "      \"qef\": false,\n" +
                    "      \"drop_inverted_components\": false,\n" +
                    "      \"drop_enclosed_components\": false,\n" +
                    "      \"band\": 1,\n" +
                    "      \"project_back\": 0,\n" +
                    "      \"fix_poles\": false,\n" +
                    "      \"smooth_iters\": 0,\n" +
                    "      \"drop_small_components\": 0.01,\n" +
                    "      \"precluster_max_verts\": 20000000\n" +
                    "    },\n" +
                    "    \"class_type\": \"RemeshMesh\"\n" +
                    "  }");
                parts.Add(SaveGlbNode("102", "[\"101\", 0]", s.RemeshPrefix));
            }
            if (s.JarJarMesh)
            {
                parts.Add("  \"100\": {\n" +
                    "    \"inputs\": {\n" +
                    "      \"file_path\": \"glb\",\n" +
                    "      \"model_3d\": [\"92\", 0],\n" +
                    "      \"format\": \"glb\",\n" +
                    "      \"output_folder\": \"./output\",\n" +
                    "      \"filename\": \"\",\n" +
                    "      \"opacity_cutoff\": 0.05,\n" +
                    "      \"poisson_depth\": 12,\n" +
                    "      \"max_points\": 500000,\n" +
                    "      \"rotate_x\": 0,\n" +
                    "      \"rotate_y\": 0,\n" +
                    "      \"rotate_z\": 0,\n" +
                    "      \"decimate_enable\": false,\n" +
                    "      \"target_faces\": 100000,\n" +
                    "      \"preserve_topology\": true,\n" +
                    "      \"quality_threshold\": 0,\n" +
                    "      \"bake_texture\": true,\n" +
                    "      \"texture_size\": 2048,\n" +
                    "      \"flip_faces\": false\n" +
                    "    },\n" +
                    "    \"class_type\": \"JarJarSplatToMesh\"\n" +
                    "  }");
            }
            if (s.JarJarSaveGlb)
            {
                parts.Add("  \"102\": {\n" +
                    "    \"inputs\": {\n" +
                    "      \"mesh\": [\"76\", 0],\n" +
                    "      \"file_path\": \"./output/mesh.glb\"\n" +
                    "    },\n" +
                    "    \"class_type\": \"JarJarSaveGLB\"\n" +
                    "  }");
            }
            if (s.ShadowChain)
            {
                parts.Add("  \"126\": {\n" +
                    "    \"inputs\": {\n" +
                    "      \"splat\": [\"118\", 0],\n" +
                    "      \"fill\": 0.25,\n" +
                    "      \"threshold\": 0.06\n" +
                    "    },\n" +
                    "    \"class_type\": \"JarJarSplatFillLight\"\n" +
                    "  }");
                parts.Add("  \"127\": {\n" +
                    "    \"inputs\": {\n" +
                    "      \"splat\": [\"126\", 0],\n" +
                    "      \"format\": \"spz\"\n" +
                    "    },\n" +
                    "    \"class_type\": \"SplatToFile3D\"\n" +
                    "  }");
                parts.Add(SaveGlbNode("128", "[\"127\", 0]", "3d/ComfyUI_ShadowRemoved"));
                parts.Add(SaveGlbNode("187", "[\"127\", 0]", "3d/ComfyUI"));
            }
            return "{\n" + JoinNodes(parts) + "\n}";
        }

        // MultisMaker16 flat fused Flux.2 image stage (no subgraph shell).
        // Output node id is "160". promptRef / imageRef are preformatted refs.
        public static string FlatFlux16(string promptRef, string imageRef,
            string fluxSteps, string noiseSeed, string unet, string clip, string vae)
        {
            ArrayList n = new ArrayList();
            n.Add("  \"149\": {\n" +
                "    \"inputs\": {\n" +
                "      \"unet_name\": \"" + unet + "\",\n" +
                "      \"weight_dtype\": \"default\"\n" +
                "    },\n" +
                "    \"class_type\": \"UNETLoader\"\n" +
                "  }");
            n.Add("  \"150\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip_name\": \"" + clip + "\",\n" +
                "      \"type\": \"flux2\",\n" +
                "      \"device\": \"default\"\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPLoader\"\n" +
                "  }");
            n.Add("  \"151\": {\n" +
                "    \"inputs\": {\n" +
                "      \"vae_name\": \"" + vae + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"VAELoader\"\n" +
                "  }");
            n.Add("  \"164\": {\n" +
                "    \"inputs\": {\n" +
                "      \"image\": " + imageRef + ",\n" +
                "      \"upscale_method\": \"lanczos\",\n" +
                "      \"megapixels\": 1.5,\n" +
                "      \"resolution_steps\": 64\n" +
                "    },\n" +
                "    \"class_type\": \"ImageScaleToTotalPixels\"\n" +
                "  }");
            n.Add("  \"156\": {\n" +
                "    \"inputs\": {\n" +
                "      \"image\": [\"164\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"GetImageSize\"\n" +
                "  }");
            n.Add("  \"158\": {\n" +
                "    \"inputs\": {\n" +
                "      \"pixels\": [\"164\", 0],\n" +
                "      \"vae\": [\"151\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"VAEEncode\"\n" +
                "  }");
            n.Add("  \"163\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip\": [\"150\", 0],\n" +
                "      \"text\": " + promptRef + "\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPTextEncode\"\n" +
                "  }");
            n.Add("  \"154\": {\n" +
                "    \"inputs\": {\n" +
                "      \"conditioning\": [\"163\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"ConditioningZeroOut\"\n" +
                "  }");
            n.Add("  \"155\": {\n" +
                "    \"inputs\": {\n" +
                "      \"conditioning\": [\"163\", 0],\n" +
                "      \"latent\": [\"158\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"ReferenceLatent\"\n" +
                "  }");
            n.Add("  \"157\": {\n" +
                "    \"inputs\": {\n" +
                "      \"conditioning\": [\"154\", 0],\n" +
                "      \"latent\": [\"158\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"ReferenceLatent\"\n" +
                "  }");
            n.Add("  \"152\": {\n" +
                "    \"inputs\": {\n" +
                "      \"width\": [\"156\", 0],\n" +
                "      \"height\": [\"156\", 1],\n" +
                "      \"batch_size\": 1\n" +
                "    },\n" +
                "    \"class_type\": \"EmptyFlux2LatentImage\"\n" +
                "  }");
            n.Add("  \"153\": {\n" +
                "    \"inputs\": {\n" +
                "      \"steps\": " + fluxSteps + ",\n" +
                "      \"width\": [\"156\", 0],\n" +
                "      \"height\": [\"156\", 1]\n" +
                "    },\n" +
                "    \"class_type\": \"Flux2Scheduler\"\n" +
                "  }");
            n.Add("  \"162\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"149\", 0],\n" +
                "      \"positive\": [\"157\", 0],\n" +
                "      \"negative\": [\"155\", 0],\n" +
                "      \"cfg\": 3\n" +
                "    },\n" +
                "    \"class_type\": \"CFGGuider\"\n" +
                "  }");
            n.Add("  \"148\": {\n" +
                "    \"inputs\": {\n" +
                "      \"sampler_name\": \"euler\"\n" +
                "    },\n" +
                "    \"class_type\": \"KSamplerSelect\"\n" +
                "  }");
            n.Add("  \"161\": {\n" +
                "    \"inputs\": {\n" +
                "      \"noise_seed\": " + noiseSeed + "\n" +
                "    },\n" +
                "    \"class_type\": \"RandomNoise\"\n" +
                "  }");
            n.Add("  \"159\": {\n" +
                "    \"inputs\": {\n" +
                "      \"noise\": [\"161\", 0],\n" +
                "      \"guider\": [\"162\", 0],\n" +
                "      \"sampler\": [\"148\", 0],\n" +
                "      \"sigmas\": [\"153\", 0],\n" +
                "      \"latent_image\": [\"152\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"SamplerCustomAdvanced\"\n" +
                "  }");
            n.Add("  \"160\": {\n" +
                "    \"inputs\": {\n" +
                "      \"samples\": [\"159\", 0],\n" +
                "      \"vae\": [\"151\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"VAEDecode\"\n" +
                "  }");
            return JoinNodes(n);
        }
    }

    /// <summary>Option bag for <see cref="CoreVariantJson"/> outer emission.</summary>
    internal sealed class OuterSpec
    {
        public string PromptId = "138";
        public string PromptClass = "PrimitiveStringMultiline";
        public string LoadImageId = "132";
        public bool HasFlux = true;
        public string FluxPrefix = "sg131_";
        public string ImageRefOverride;
        public bool HasSwitch102 = true;
        public bool HasInvert109;
        public bool HasSwitch111 = true;
        public bool HasPreview112 = true;
        public int Preview112Nums = 36000;
        public int Preview112Point = 2;
        public bool NoAlphaJoin;
        public string Sampler = "euler";
        public string Cfg = "3";
        public string UnetDtype = "default";
        public string SplatFormat = "ply";
        public bool Saver51;
        public string PlyPrefix = "3d/PLY_";
        public bool Saver137Standalone;
        public bool Saver177;
        public string Saver177Prefix = "";
        public bool HasP133 = true;
        public bool HasP144;
        public bool HasP145;
        public int PreviewWH = 1024;
        public int MeshKind;
        public int MeshRes = 384;
        public int MeshKernel = 5;
        public int MeshSmooth;
        public double MeshLevel = 0.4;
        public int MeshMinComp = 500;
        public double MeshMinOp = 0.02;
        public int MeshSharp = 2;
        public string MeshPrefix = "3d/MESH_";
        public bool HasGaussExtra;
        public string GaussPrefix = "3d/ComfyUI";
        public bool Gauss146FromPreview;
        public bool PromptChain;
    }

    /// <summary>Runtime (caller supplied) values for outer emission.</summary>
    internal sealed class TripoRt
    {
        public string Prompt = "";
        public string KSeed = "0";
        public string DSeed = "0";
        public string Steps = "20";
        public string NumGaussians = "64800";
        public string ImageFile = "";
        public string FluxSteps = "4";
        public string FluxSeed = "0";
        public string TripoUnet = "";
        public string TripoVae = "";
        public string CondVae = "";
        public string ClipVision = "";
        public string BgRemoval = "";
        public string FluxUnet = "";
        public string FluxClip = "";
        public string FluxVae = "";
        public string BatchIndex = "20";
        public string PromptSuffix = "";
        public string PromptListText = "";
    }

    /// <summary>Option bag for <see cref="CoreVariantJson"/> core emission.</summary>
    internal sealed class CoreSpec
    {
        public string Prefix = "sg88_";
        public string PreprocessSize = "1024";
        public string PreviewOct = "5";
        public string PreviewNums = "16384";
        public string PreviewPoint = "2";
        public string TripoCfg = "3";
        public string TripoSampler = "dpmpp_2m";
        public string SplatFormat = "ply";
        public string PlyPrefix = "3d/ComfyUI_TripoSplat";
        public int MeshKind;
        public int MeshRes = 768;
        public int MeshKernel = 7;
        public int MeshSmooth;
        public double MeshLevel = 0.03;
        public int MeshMinComp = 250;
        public double MeshMinOp = 0.02;
        public int MeshSharp = 2;
        public string MeshPrefix = "3d/ComfyUI_TripoSplat";
        public bool RenderChainFlag;
        public string VideoPrefix = "video/ComfyUI_TripoSplat";
        public bool Remesh;
        public string RemeshPrefix = "3d/ComfyUI_TripoSplat_rem";
        public bool JarJarMesh;
        public bool JarJarSaveGlb;
        public bool ShadowChain;
        public bool ResizeNode;
    }

    /// <summary>Runtime (caller supplied) values for core emission.</summary>
    internal sealed class CoreRt
    {
        public string KSeed = "0";
        public string DSeed = "0";
        public string Steps = "20";
        public string NumGaussians = "262144";
        public string ImageFile = "";
        public string TripoUnet = "";
        public string TripoVae = "";
        public string CondVae = "";
        public string ClipVision = "";
        public string BgRemoval = "";
    }

    /// <summary>
    /// B1.json: 21 outer nodes, no Flux subgraph, no prompt box.
    /// LoadImage 99 feeds Tripo preprocess / RemoveBackground / alpha join
    /// directly. 102 selects between the inverted LoadImage mask (109) and
    /// the RemoveBackground mask; 76 SplatToMesh + 67 SaveGLB keep a mesh
    /// sink beside the 51 PLY saver. Frozen: KSampler dpmpp_2m 20 steps
    /// cfg 3, decode 262144, preview 5/262144/90/15/2. No PreviewImage or
    /// Submit3D nodes. prompt is accepted for signature uniformity but
    /// ignored (image-to-3D only).
    /// </summary>
    public static class B1Workflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 262144;
        public const int DEFAULT_TRIPO_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_SAMPLER = "dpmpp_2m";
        public const int DEFAULT_PREVIEW_GAUSSIANS = 262144;
        public const string DEFAULT_PLY_PREFIX = "3d/PLY_";
        public const string DEFAULT_MESH_PREFIX = "3d/MESH_";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            TripoRt rt = new TripoRt();
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            OuterSpec spec = new OuterSpec();
            spec.PromptId = null;
            spec.LoadImageId = "99";
            spec.HasFlux = false;
            spec.HasInvert109 = true;
            spec.Preview112Nums = DEFAULT_PREVIEW_GAUSSIANS;
            spec.Sampler = DEFAULT_SAMPLER;
            spec.Saver51 = true;
            spec.PlyPrefix = DEFAULT_PLY_PREFIX;
            spec.MeshKind = 1;
            spec.MeshSmooth = 1;
            spec.MeshMinComp = 0;
            spec.MeshPrefix = DEFAULT_MESH_PREFIX;
            spec.HasP133 = false;
            return CoreVariantJson.EmitOuter(spec, rt, fluxOnly, skipFlux, null);
        }
    }

    /// <summary>
    /// B2.json: 21 outer nodes, same direct-image topology as B1.
    /// Differences from B1: KSampler dpm_fast, decode 32768, preview
    /// 5/36000/90/15/2. Frozen seeds/images differ only. prompt is
    /// accepted for signature uniformity but ignored.
    /// </summary>
    public static class B2Workflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 32768;
        public const int DEFAULT_TRIPO_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_SAMPLER = "dpm_fast";
        public const int DEFAULT_PREVIEW_GAUSSIANS = 36000;
        public const string DEFAULT_PLY_PREFIX = "3d/PLY_";
        public const string DEFAULT_MESH_PREFIX = "3d/MESH_";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            TripoRt rt = new TripoRt();
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            OuterSpec spec = new OuterSpec();
            spec.PromptId = null;
            spec.LoadImageId = "99";
            spec.HasFlux = false;
            spec.HasInvert109 = true;
            spec.Preview112Nums = DEFAULT_PREVIEW_GAUSSIANS;
            spec.Sampler = DEFAULT_SAMPLER;
            spec.Saver51 = true;
            spec.PlyPrefix = DEFAULT_PLY_PREFIX;
            spec.MeshKind = 1;
            spec.MeshSmooth = 1;
            spec.MeshMinComp = 0;
            spec.MeshPrefix = DEFAULT_MESH_PREFIX;
            spec.HasP133 = false;
            return CoreVariantJson.EmitOuter(spec, rt, fluxOnly, skipFlux, null);
        }
    }

    /// <summary>
    /// B3.json: 22 outer nodes + 17-node Flux.2 Klein subgraph (131).
    /// Prompt box is PrimitiveString 135 (not Multiline 138). Has 133
    /// PreviewImage but no 144/145 previews. KSampler dpm_fast 20 steps
    /// cfg 3, decode 64800, preview 5/36000/90/15/2. 51 PLY saver kept.
    /// Frozen flux seed 574621040692282 (runtime uses derived fluxSeed).
    /// </summary>
    public static class B3Workflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 64800;
        public const int DEFAULT_TRIPO_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_SAMPLER = "dpm_fast";
        public const string DEFAULT_PLY_PREFIX = "3d/PLY_";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            TripoRt rt = new TripoRt();
            rt.Prompt = CoreVariantJson.EscapeJson(prompt ?? string.Empty);
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.FluxSteps = fluxSteps.ToString(inv);
            rt.FluxSeed = fluxSeed;
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            rt.FluxUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            rt.FluxClip = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            rt.FluxVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);
            OuterSpec spec = new OuterSpec();
            spec.PromptId = "135";
            spec.PromptClass = "PrimitiveString";
            spec.Sampler = DEFAULT_SAMPLER;
            spec.Saver51 = true;
            spec.PlyPrefix = DEFAULT_PLY_PREFIX;
            return CoreVariantJson.EmitOuter(spec, rt, fluxOnly, skipFlux, null);
        }
    }

    /// <summary>
    /// B4.json: 22 outer nodes + 17-node Flux subgraph (131). Same shape
    /// as B3 (PrimitiveString 135, 133 preview only, dpm_fast, 64800).
    /// Only the frozen flux seed differs (450402430212362); runtime seeds
    /// are derived from the caller seed in both builders.
    /// </summary>
    public static class B4Workflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 64800;
        public const int DEFAULT_TRIPO_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_SAMPLER = "dpm_fast";
        public const string DEFAULT_PLY_PREFIX = "3d/PLY_";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            TripoRt rt = new TripoRt();
            rt.Prompt = CoreVariantJson.EscapeJson(prompt ?? string.Empty);
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.FluxSteps = fluxSteps.ToString(inv);
            rt.FluxSeed = fluxSeed;
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            rt.FluxUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            rt.FluxClip = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            rt.FluxVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);
            OuterSpec spec = new OuterSpec();
            spec.PromptId = "135";
            spec.PromptClass = "PrimitiveString";
            spec.Sampler = DEFAULT_SAMPLER;
            spec.Saver51 = true;
            spec.PlyPrefix = DEFAULT_PLY_PREFIX;
            return CoreVariantJson.EmitOuter(spec, rt, fluxOnly, skipFlux, null);
        }
    }

    /// <summary>
    /// B5.json: 24 outer nodes + 17-node Flux subgraph (131).
    /// Differences: KSampler euler 18 steps cfg 3, UNET weight_dtype
    /// fp8_e5m2, 136 SplatToMesh [512,5,0,0.4,500,0.02,2] + 137 SaveGLB
    /// (3d/ComfyUI) beside the 51 PLY saver, PrimitiveString 135 prompt,
    /// 133 preview only. Frozen flux seed 918089705855577.
    /// </summary>
    public static class B5Workflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 64800;
        public const int DEFAULT_TRIPO_STEPS = 18;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_UNET_DTYPE = "fp8_e5m2";
        public const int DEFAULT_MESH_RES = 512;
        public const int DEFAULT_MESH_KERNEL = 5;
        public const int DEFAULT_MESH_SMOOTH = 0;
        public const double DEFAULT_MESH_LEVEL = 0.4;
        public const int DEFAULT_MESH_MIN_COMP = 500;
        public const double DEFAULT_MESH_MIN_OP = 0.02;
        public const int DEFAULT_MESH_SHARP = 2;
        public const string DEFAULT_MESH_PREFIX = "3d/ComfyUI";
        public const string DEFAULT_PLY_PREFIX = "3d/PLY_";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            TripoRt rt = new TripoRt();
            rt.Prompt = CoreVariantJson.EscapeJson(prompt ?? string.Empty);
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.FluxSteps = fluxSteps.ToString(inv);
            rt.FluxSeed = fluxSeed;
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            rt.FluxUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            rt.FluxClip = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            rt.FluxVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);
            OuterSpec spec = new OuterSpec();
            spec.PromptId = "135";
            spec.PromptClass = "PrimitiveString";
            spec.UnetDtype = DEFAULT_UNET_DTYPE;
            spec.Saver51 = true;
            spec.PlyPrefix = DEFAULT_PLY_PREFIX;
            spec.MeshKind = 2;
            spec.MeshRes = DEFAULT_MESH_RES;
            spec.MeshKernel = DEFAULT_MESH_KERNEL;
            spec.MeshSmooth = DEFAULT_MESH_SMOOTH;
            spec.MeshLevel = DEFAULT_MESH_LEVEL;
            spec.MeshMinComp = DEFAULT_MESH_MIN_COMP;
            spec.MeshMinOp = DEFAULT_MESH_MIN_OP;
            spec.MeshSharp = DEFAULT_MESH_SHARP;
            spec.MeshPrefix = DEFAULT_MESH_PREFIX;
            return CoreVariantJson.EmitOuter(spec, rt, fluxOnly, skipFlux, null);
        }
    }

    /// <summary>
    /// B6.json: 7 outer nodes + two chained 17-node Flux.2 Klein edit
    /// subgraphs (c497d832-... then 14d43442-...), no 3D nodes at all.
    /// 152 LoadImage -> 150 (prompt 151) -> 174 (prompt 175, image from
    /// 150) with 149/176 PreviewImages. Inner ids are structurally
    /// identical to the f1793303 body, so stage 2 is emitted renumbered
    /// to 61-80 under prefix sg174_. Frozen scheduler steps 2, frozen
    /// noise seeds 860350185845898 / 664512726821; runtime uses kSeed for
    /// stage 1 and the derived fluxSeed for stage 2, fluxSteps for both
    /// schedulers. fluxOnly/skipFlux are meaningless here (no 3D stage)
    /// and return the full two-stage edit. steps is unused (no KSampler).
    /// </summary>
    public static class B6Workflow
    {
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_FLUX_STEPS = 2;
        public const string DEFAULT_FIRST_PROMPT = "A pile of hay stacked many high in the center";
        public const string DEFAULT_SECOND_PROMPT = "extact the center subject of this image and show it on a black background.";

        public static string CreateWorkflow(
            string prompt,
            long seed,
            int steps,
            int numGaussians = 64800,
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
            string fluxVae = null,
            string firstPrompt = null)
        {
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            string fs = fluxSteps.ToString(inv);
            string file = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            string unet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            string clip = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            string vae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);
            string p1 = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(firstPrompt) ? DEFAULT_FIRST_PROMPT : firstPrompt);
            string p2 = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(prompt) ? DEFAULT_SECOND_PROMPT : prompt);
            ArrayList parts = new ArrayList();
            parts.Add(CoreVariantJson.LoadImageNode("152", file));
            parts.Add(CoreVariantJson.PromptNode("151", "PrimitiveStringMultiline", p1));
            parts.Add(CoreVariantJson.PromptNode("175", "PrimitiveStringMultiline", p2));
            parts.Add(CoreVariantJson.FluxStage("sg150_", "[\"151\", 0]", "[\"152\", 0]", fs, kSeed, unet, clip, vae));
            parts.Add(CoreVariantJson.FluxStage("sg174_", "[\"175\", 0]", "[\"sg150_65\", 0]", fs, fluxSeed, unet, clip, vae));
            parts.Add("  \"149\": {\n" +
                "    \"inputs\": {\n" +
                "      \"images\": [\"sg150_65\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"PreviewImage\"\n" +
                "  }");
            parts.Add("  \"176\": {\n" +
                "    \"inputs\": {\n" +
                "      \"images\": [\"sg174_65\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"PreviewImage\"\n" +
                "  }");
            parts.Add(CoreVariantJson.SaveImageNode("mm_saveimg", "mm_init", "[\"sg174_65\", 0]"));
            return "{\n" + CoreVariantJson.JoinNodes(parts) + "\n}";
        }
    }
        // ----1__

    /// <summary>
    /// B7.json: 23 outer nodes + 17-node Flux subgraph (131).
    /// PrimitiveStringMultiline 138 prompt, previews 133/144/145, KSampler
    /// euler 20 steps cfg 3, decode 64800. No SaveGLB saver in the source
    /// (134 JarJarSubmit3D dropped), so the mm_saveply sink is added.
    /// Frozen flux seed 783581404700681.
    /// </summary>
    public static class B7Workflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 64800;
        public const int DEFAULT_TRIPO_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_PLY_PREFIX = "3d/PLY_";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            TripoRt rt = new TripoRt();
            rt.Prompt = CoreVariantJson.EscapeJson(prompt ?? string.Empty);
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.FluxSteps = fluxSteps.ToString(inv);
            rt.FluxSeed = fluxSeed;
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            rt.FluxUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            rt.FluxClip = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            rt.FluxVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);
            OuterSpec spec = new OuterSpec();
            spec.HasP144 = true;
            spec.HasP145 = true;
            spec.PlyPrefix = DEFAULT_PLY_PREFIX;
            return CoreVariantJson.EmitOuter(spec, rt, fluxOnly, skipFlux, null);
        }
    }

    /// <summary>
    /// BEST 3D EVER .json: 24 outer nodes + b64333d5 tripo-core subgraph
    /// (14 inner nodes) + nested background subgraph (4 nodes), image-only
    /// (LoadImage 99, no prompt/flux). Outer num_gaussians 33219, seed 47;
    /// inner KSampler dpmpp_2m 20 steps cfg 3, preprocess 1024, preview
    /// 5/16384/90/15/2, decode frozen 262144/790219963981395. Extras: 76
    /// SplatToMesh [768,7,0,0.03,250,0.02,2] + 67 saver, render turntable
    /// chain (75/79/41/42), 101 RemeshMesh + 102 saver. UV/Blender chain,
    /// MarkdownNotes and JarJar nodes dropped (see file header).
    /// </summary>
    public static class Best3DEverWorkflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 33219;
        public const int DEFAULT_TRIPO_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_SAMPLER = "dpmpp_2m";
        public const string DEFAULT_CFG = "3";
        public const int DEFAULT_PREVIEW_GAUSSIANS = 16384;
        public const string DEFAULT_SPLAT_FORMAT = "spz";
        public const string DEFAULT_PLY_PREFIX = "3d/ComfyUI_TripoSplat";
        public const int DEFAULT_MESH_RES = 768;
        public const int DEFAULT_MESH_KERNEL = 7;
        public const int DEFAULT_MESH_SMOOTH = 0;
        public const double DEFAULT_MESH_LEVEL = 0.03;
        public const int DEFAULT_MESH_MIN_COMP = 250;
        public const double DEFAULT_MESH_MIN_OP = 0.02;
        public const int DEFAULT_MESH_SHARP = 2;
        public const string DEFAULT_VIDEO_PREFIX = "video/ComfyUI_TripoSplat";
        public const string DEFAULT_REMESH_PREFIX = "3d/ComfyUI_TripoSplat_rem";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            CoreRt rt = new CoreRt();
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            CoreSpec spec = new CoreSpec();
            spec.TripoCfg = DEFAULT_CFG;
            spec.TripoSampler = DEFAULT_SAMPLER;
            spec.PreviewNums = DEFAULT_PREVIEW_GAUSSIANS.ToString(inv);
            spec.SplatFormat = DEFAULT_SPLAT_FORMAT;
            spec.PlyPrefix = DEFAULT_PLY_PREFIX;
            spec.MeshKind = 1;
            spec.MeshRes = DEFAULT_MESH_RES;
            spec.MeshKernel = DEFAULT_MESH_KERNEL;
            spec.MeshSmooth = DEFAULT_MESH_SMOOTH;
            spec.MeshLevel = DEFAULT_MESH_LEVEL;
            spec.MeshMinComp = DEFAULT_MESH_MIN_COMP;
            spec.MeshMinOp = DEFAULT_MESH_MIN_OP;
            spec.MeshSharp = DEFAULT_MESH_SHARP;
            spec.MeshPrefix = DEFAULT_PLY_PREFIX;
            spec.RenderChainFlag = true;
            spec.VideoPrefix = DEFAULT_VIDEO_PREFIX;
            spec.Remesh = true;
            spec.RemeshPrefix = DEFAULT_REMESH_PREFIX;
            return CoreVariantJson.EmitCore(spec, rt, fluxOnly);
        }
    }

    /// <summary>
    /// BEST 3D EVER _TRY2.json: 12 outer nodes + b64/background subgraphs.
    /// Same core as BEST 3D EVER (dpmpp_2m, spz, mesh, render chain) with
    /// outer num_gaussians 332192 and NO RemeshMesh/UV extras.
    /// </summary>
    public static class Best3DEverTry2Workflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 332192;
        public const int DEFAULT_TRIPO_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_SAMPLER = "dpmpp_2m";
        public const string DEFAULT_CFG = "3";
        public const int DEFAULT_PREVIEW_GAUSSIANS = 16384;
        public const string DEFAULT_SPLAT_FORMAT = "spz";
        public const string DEFAULT_PLY_PREFIX = "3d/ComfyUI_TripoSplat";
        public const int DEFAULT_MESH_RES = 768;
        public const int DEFAULT_MESH_KERNEL = 7;
        public const int DEFAULT_MESH_SMOOTH = 0;
        public const double DEFAULT_MESH_LEVEL = 0.03;
        public const int DEFAULT_MESH_MIN_COMP = 250;
        public const double DEFAULT_MESH_MIN_OP = 0.02;
        public const int DEFAULT_MESH_SHARP = 2;
        public const string DEFAULT_VIDEO_PREFIX = "video/ComfyUI_TripoSplat";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            CoreRt rt = new CoreRt();
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            CoreSpec spec = new CoreSpec();
            spec.TripoCfg = DEFAULT_CFG;
            spec.TripoSampler = DEFAULT_SAMPLER;
            spec.PreviewNums = DEFAULT_PREVIEW_GAUSSIANS.ToString(inv);
            spec.SplatFormat = DEFAULT_SPLAT_FORMAT;
            spec.PlyPrefix = DEFAULT_PLY_PREFIX;
            spec.MeshKind = 1;
            spec.MeshRes = DEFAULT_MESH_RES;
            spec.MeshKernel = DEFAULT_MESH_KERNEL;
            spec.MeshSmooth = DEFAULT_MESH_SMOOTH;
            spec.MeshLevel = DEFAULT_MESH_LEVEL;
            spec.MeshMinComp = DEFAULT_MESH_MIN_COMP;
            spec.MeshMinOp = DEFAULT_MESH_MIN_OP;
            spec.MeshSharp = DEFAULT_MESH_SHARP;
            spec.MeshPrefix = DEFAULT_PLY_PREFIX;
            spec.RenderChainFlag = true;
            spec.VideoPrefix = DEFAULT_VIDEO_PREFIX;
            return CoreVariantJson.EmitCore(spec, rt, fluxOnly);
        }
    }
        // ----2__

    /// <summary>
    /// MultisMaker3.json: 23 outer nodes + 17-node Flux subgraph (131).
    /// Multiline 138 prompt, previews 133/144/145, KSampler euler 20 steps
    /// cfg 3, decode 64800. Distinctive 92 SplatToFile3D format "ksplat".
    /// No SaveGLB saver in the source, so the mm_saveply sink is added.
    /// </summary>
    public static class MultisMaker3Workflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 64800;
        public const int DEFAULT_TRIPO_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_SPLAT_FORMAT = "ksplat";
        public const string DEFAULT_PLY_PREFIX = "3d/PLY_";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            TripoRt rt = new TripoRt();
            rt.Prompt = CoreVariantJson.EscapeJson(prompt ?? string.Empty);
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.FluxSteps = fluxSteps.ToString(inv);
            rt.FluxSeed = fluxSeed;
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            rt.FluxUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            rt.FluxClip = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            rt.FluxVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);
            OuterSpec spec = new OuterSpec();
            spec.SplatFormat = DEFAULT_SPLAT_FORMAT;
            spec.HasP144 = true;
            spec.HasP145 = true;
            spec.PlyPrefix = DEFAULT_PLY_PREFIX;
            return CoreVariantJson.EmitOuter(spec, rt, fluxOnly, skipFlux, null);
        }
    }

    /// <summary>
    /// MultisMaker4.json: 23 outer nodes + 17-node Flux subgraph (131).
    /// Same shape as MultisMaker3 but 92 format "ply". No saver in the
    /// source, so the mm_saveply sink is added.
    /// </summary>
    public static class MultisMaker4Workflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 64800;
        public const int DEFAULT_TRIPO_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_PLY_PREFIX = "3d/PLY_";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            TripoRt rt = new TripoRt();
            rt.Prompt = CoreVariantJson.EscapeJson(prompt ?? string.Empty);
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.FluxSteps = fluxSteps.ToString(inv);
            rt.FluxSeed = fluxSeed;
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            rt.FluxUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            rt.FluxClip = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            rt.FluxVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);
            OuterSpec spec = new OuterSpec();
            spec.HasP144 = true;
            spec.HasP145 = true;
            spec.PlyPrefix = DEFAULT_PLY_PREFIX;
            return CoreVariantJson.EmitOuter(spec, rt, fluxOnly, skipFlux, null);
        }
    }

    /// <summary>
    /// MultisMaker5.json: 20 outer nodes + 17-node Flux subgraph (131).
    /// No 102/109 mask switch (113 feeds 103 directly), no 111 model
    /// switch (110 feeds 101 directly), no Submit3D node at all.
    /// KSampler euler 20 steps cfg 6.8, decode 1000000, preview
    /// 5/36000/90/15/1. Previews 133/144/145 kept; mm_saveply added.
    /// </summary>
    public static class MultisMaker5Workflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 1000000;
        public const int DEFAULT_TRIPO_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_CFG = "6.8";
        public const int DEFAULT_PREVIEW_POINT = 1;
        public const string DEFAULT_PLY_PREFIX = "3d/PLY_";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            TripoRt rt = new TripoRt();
            rt.Prompt = CoreVariantJson.EscapeJson(prompt ?? string.Empty);
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.FluxSteps = fluxSteps.ToString(inv);
            rt.FluxSeed = fluxSeed;
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            rt.FluxUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            rt.FluxClip = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            rt.FluxVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);
            OuterSpec spec = new OuterSpec();
            spec.HasSwitch102 = false;
            spec.HasSwitch111 = false;
            spec.Cfg = DEFAULT_CFG;
            spec.Preview112Point = DEFAULT_PREVIEW_POINT;
            spec.HasP144 = true;
            spec.HasP145 = true;
            spec.PlyPrefix = DEFAULT_PLY_PREFIX;
            return CoreVariantJson.EmitOuter(spec, rt, fluxOnly, skipFlux, null);
        }
    }
        // ----3__

    /// <summary>
    /// MultisMaker12.json: 25 outer nodes + 17-node Flux subgraph (131).
    /// Extras: 146 SaveGLB fed by the 144 preview passthrough and 147
    /// SaveGaussianSplat (3d/ComfyUI, 1024x1024) beside them. Preview 112
    /// is 5/262144/90/15/1. KSampler euler, decode 64800.
    /// </summary>
    public static class MultisMaker12Workflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 64800;
        public const int DEFAULT_TRIPO_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const int DEFAULT_PREVIEW_GAUSSIANS = 262144;
        public const int DEFAULT_PREVIEW_POINT = 1;
        public const string DEFAULT_GAUSS_PREFIX = "3d/ComfyUI";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            TripoRt rt = new TripoRt();
            rt.Prompt = CoreVariantJson.EscapeJson(prompt ?? string.Empty);
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.FluxSteps = fluxSteps.ToString(inv);
            rt.FluxSeed = fluxSeed;
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            rt.FluxUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            rt.FluxClip = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            rt.FluxVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);
            OuterSpec spec = new OuterSpec();
            spec.Preview112Nums = DEFAULT_PREVIEW_GAUSSIANS;
            spec.Preview112Point = DEFAULT_PREVIEW_POINT;
            spec.HasP144 = true;
            spec.HasP145 = true;
            spec.HasGaussExtra = true;
            spec.GaussPrefix = DEFAULT_GAUSS_PREFIX;
            spec.Gauss146FromPreview = true;
            return CoreVariantJson.EmitOuter(spec, rt, fluxOnly, skipFlux, null);
        }
    }

    /// <summary>
    /// MultisMaker13.json: 28 outer nodes + 17-node Flux subgraph (131).
    /// Batch prompt chain: frozen 24-entry JarJarTextList 156, 155 picks
    /// batchIndex (linked 161 value wins over the widget shadow 1; frozen
    /// effective index 20), 158 appends promptSuffix. No 102/111 switches
    /// (direct 113/110 wiring), no 112 preview, no 144 preview, 133 + 145
    /// previews, 146 SaveGLB + 147 SaveGaussianSplat extras. KSampler
    /// dpmpp_2m_sde_gpu 17 steps cfg 3, decode 64800/5838225169337.
    /// A non-empty prompt replaces the whole frozen list (single entry).
    /// </summary>
    public static class MultisMaker13Workflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 64800;
        public const int DEFAULT_TRIPO_STEPS = 17;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_SAMPLER = "dpmpp_2m_sde_gpu";
        public const int DEFAULT_BATCH_INDEX = 20;
        public const string DEFAULT_PROMPT_SUFFIX = ", T-POSE TPOSE, flat lighting";
        public const string DEFAULT_GAUSS_PREFIX = "3d/ComfyUI";
        public static readonly string[] DEFAULT_PROMPT_LIST = new string[]
        {
            "A green orc \\u2014 pointy ears, short ragged tattered loincloth, evil clawed hands, protruding lower tusks, mottled olive-green hide with old scars across the chest.",
            "A single-eye ogre cyclops \\u2014 yellow leathery skin, bald head with long wiry strands of hair sprouting from his ears, wearing a rusted chainmail coif, cinched at the waist with a brown cord belt, knuckles dragging near his knees.",
            "A slender wood elf \\u2014 tall and willowy, shiny gold-toned skin, long pointed ears, platinum hair braided down the back, dressed in simple forest-green leathers with no visible metal.",
            "A reptile man (lizardman) \\u2014 long tapering snout, olive and dark-brown mottled scales, a ridge of small spines running from the skull down the spine, wearing a woven grass skirt and carrying nothing but a bone necklace.",
            "A hulking gray ogre \\u2014 a single crooked yellowed tusk jutting from the lower jaw, thick knotted muscles, coarse patchy fur on the shoulders, wrapped in a crude iron-studded leather harness that crosses the chest.",
            "A gaunt undead ghoul \\u2014 grayish-blue rotting flesh pulled tight over visible ribs, sunken black eye sockets, tattered burial shroud hanging in strips, exposed bone at the fingertips where the flesh has sloughed away.",
            "A dark elf (drow-type) \\u2014 ash-black skin with a faint violet sheen, stark white hair pulled into a tight topknot, narrow angular face, dressed in fitted matte-black leather with silver thread stitching.",
            "A ratman (ratkin) \\u2014 hunched posture, coarse brown fur, a long bald pink tail, whiskered snout with small sharp teeth, wearing a filthy patchwork vest sewn from scraps of cloth and fur.",
            "A stone gargoyle \\u2014 smooth charcoal-gray hide like carved granite, small curved horns sweeping back from the temples, leathery folded wings pinned tight against the back, glowing faint orange eyes, otherwise unclothed.",
            "A minotaur \\u2014 massive bull head with wide curved horns, coarse dark-brown fur covering a heavily muscled humanoid body, a brass ring through the nose, wearing only a wide leather waist strap with a metal buckle.",
            "A pallid ancient lich \\u2014 desiccated gray-white skin stretched thin over a skeletal frame, hollow glowing blue eye sockets, wrapped in a tattered black-and-purple funerary robe with faded gold embroidery, bony fingers tipped in long yellowed nails.",
            "A troll \\u2014 sickly green-gray warty skin, a long hooked nose, sparse greasy black hair in a single braid, unnaturally long arms that hang past the knees, wearing nothing but a crude fur loincloth held by a rope.",
            "An ophidian snake-man \\u2014 smooth yellow-and-black banded scales, a flat hooded serpentine head with slit pupils, a thin forked tongue, dressed in wrapped desert-tan linen bandaging around the torso and forearms.",
            "A savage harpy \\u2014 human female torso covered in mottled brown feathers that transition to taloned bird feet, feather-crested hair, sharp beak-like nose, wearing only a simple woven twine chest wrap.",
            "A brutish ettin (two-headed ogre) \\u2014 two lumpy misshapen heads sharing one thick neck, sickly gray-green skin, one head balding and the other with a matted red beard, wrapped in a patchwork of stitched-together animal hides.",
            "A frost-pale ice troll \\u2014 bluish-white translucent skin with visible dark veins beneath, icicle-like shards of brittle hair, frost crusted along the knuckles and jawline, no clothing save a fur-wrapped loincloth stiff with frost.",
            "A goblin skirmisher \\u2014 small wiry frame, sickly yellow-green skin, oversized bat-like ears, a crooked underbite with jagged teeth, dressed in mismatched scraps of leather armor clearly looted and ill-fitting.",
            "A satyr \\u2014 waist-up human male with tan skin and a light dusting of chest hair, small curled ram horns at the temples, shaggy goat-brown fur from the waist down ending in cloven hooves, bare-chested with a simple rope belt.",
            "A withered zombie \\u2014 grayish-green necrotic flesh with patches sloughed away revealing muscle beneath, one clouded milky eye, stiff jerky posture implied even in the pose, dressed in the moldering remains of a farmer's shirt and trousers.",
            "A golden-scaled naga (bipedal variant) \\u2014 upper body human-like with smooth bronze skin, lower legs replaced by a single stout scaled tail-like limb structure fused at the hip (still standing bipedally on a forked base), adorned with layered golden armbands.",
            "A vampire noble \\u2014 porcelain-white skin, jet-black slicked hair, elongated canine fangs, dressed in an immaculate high-collared burgundy velvet coat with black trim and silver clasps.",
            "A hobgoblin brute \\u2014 reddish-brown leathery skin, a broad flat nose, small tusks, heavily muscled frame, wearing a crude breastplate of hammered scrap iron over a stained red tunic.",
            "A dryad \\u2014 bark-textured light-brown skin with faint moss-green patches, hair made of woven thin vines and small leaves, slender elegant frame, entirely unclothed save for living vines wrapping the forearms and calves.",
            "A skeletal warrior \\u2014 bare bleached-white bone frame held together with faint traces of desiccated ligament, an empty eye socket glow of dim green, wearing the corroded remains of a breastplate and a single rusted pauldron."
        };

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
            string fluxVae = null,
            int batchIndex = DEFAULT_BATCH_INDEX,
            string promptSuffix = null,
            string promptListOverride = null)
        {
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            string listText = string.IsNullOrWhiteSpace(promptListOverride)
                ? string.Join("\n", DEFAULT_PROMPT_LIST)
                : promptListOverride.Trim();
            if (!string.IsNullOrWhiteSpace(prompt) && string.IsNullOrWhiteSpace(promptListOverride))
                listText = prompt.Trim();
            TripoRt rt = new TripoRt();
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.FluxSteps = fluxSteps.ToString(inv);
            rt.FluxSeed = fluxSeed;
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            rt.FluxUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            rt.FluxClip = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            rt.FluxVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);
            rt.BatchIndex = batchIndex.ToString(inv);
            rt.PromptSuffix = CoreVariantJson.EscapeJson(promptSuffix ?? DEFAULT_PROMPT_SUFFIX);
            rt.PromptListText = CoreVariantJson.EscapeJson(listText ?? string.Empty);
            OuterSpec spec = new OuterSpec();
            spec.PromptChain = true;
            spec.HasSwitch102 = false;
            spec.HasSwitch111 = false;
            spec.HasPreview112 = false;
            spec.Sampler = DEFAULT_SAMPLER;
            spec.HasP145 = true;
            spec.HasGaussExtra = true;
            spec.GaussPrefix = DEFAULT_GAUSS_PREFIX;
            return CoreVariantJson.EmitOuter(spec, rt, fluxOnly, skipFlux, null);
        }
    }
        // ----4__

    /// <summary>
    /// MultisMaker15.json: 23 outer nodes + 17-node Flux subgraph (131).
    /// Same shape as MultisMaker4 (138 prompt, 133/144/145 previews, euler,
    /// ply, mm_saveply sink). Only frozen seeds/images differ.
    /// </summary>
    public static class MultisMaker15Workflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 64800;
        public const int DEFAULT_TRIPO_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_PLY_PREFIX = "3d/PLY_";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            TripoRt rt = new TripoRt();
            rt.Prompt = CoreVariantJson.EscapeJson(prompt ?? string.Empty);
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.FluxSteps = fluxSteps.ToString(inv);
            rt.FluxSeed = fluxSeed;
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            rt.FluxUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            rt.FluxClip = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            rt.FluxVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);
            OuterSpec spec = new OuterSpec();
            spec.HasP144 = true;
            spec.HasP145 = true;
            spec.PlyPrefix = DEFAULT_PLY_PREFIX;
            return CoreVariantJson.EmitOuter(spec, rt, fluxOnly, skipFlux, null);
        }
    }

    /// <summary>
    /// MultisMaker16.json: 41 flat nodes, NO subgraph (fused Tripo + flat
    /// Flux.2 image stage with ids 148-164). Flat flux differences from the
    /// subgraph body: lanczos 1.5 MP / 64-step scaler, scheduler frozen 12
    /// steps, CFGGuider cfg 3, CLIPTextEncode fed by 138. KSampler euler
    /// 36 steps cfg 4.5, decode 150000, preview 5/262144/90/15/1, extras
    /// 146 SaveGLB (fed by the 144 preview) + 147 SaveGaussianSplat
    /// (2048x2048, viewport_state union). fluxSteps drives the flat
    /// scheduler; skipFlux drops 138 + 148-164 (refs fall back to 132).
    /// </summary>
    public static class MultisMaker16Workflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 150000;
        public const int DEFAULT_TRIPO_STEPS = 36;
        public const int DEFAULT_FLUX_STEPS = 12;
        public const string DEFAULT_CFG = "4.5";
        public const int DEFAULT_PREVIEW_GAUSSIANS = 262144;
        public const int DEFAULT_PREVIEW_POINT = 1;
        public const int DEFAULT_PREVIEW_SIZE = 2048;
        public const string DEFAULT_GAUSS_PREFIX = "3d/ComfyUI";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            TripoRt rt = new TripoRt();
            rt.Prompt = CoreVariantJson.EscapeJson(prompt ?? string.Empty);
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.FluxSteps = fluxSteps.ToString(inv);
            rt.FluxSeed = fluxSeed;
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            rt.FluxUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            rt.FluxClip = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            rt.FluxVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);
            OuterSpec spec = new OuterSpec();
            spec.HasFlux = false;
            spec.ImageRefOverride = "[\"160\", 0]";
            spec.Cfg = DEFAULT_CFG;
            spec.Preview112Nums = DEFAULT_PREVIEW_GAUSSIANS;
            spec.Preview112Point = DEFAULT_PREVIEW_POINT;
            spec.PreviewWH = DEFAULT_PREVIEW_SIZE;
            spec.HasP144 = true;
            spec.HasP145 = true;
            spec.HasGaussExtra = true;
            spec.GaussPrefix = DEFAULT_GAUSS_PREFIX;
            spec.Gauss146FromPreview = true;
            string flat = CoreVariantJson.FlatFlux16("[\"138\", 0]", "[\"132\", 0]", rt.FluxSteps, fluxSeed, rt.FluxUnet, rt.FluxClip, rt.FluxVae);
            return CoreVariantJson.EmitOuter(spec, rt, fluxOnly, skipFlux, flat);
        }
    }

    /// <summary>
    /// MultisMaker17.json: 23 outer nodes + 17-node Flux subgraph (131).
    /// Same shape as MultisMaker4/15 (138 prompt, 133/144/145 previews,
    /// euler, ply, mm_saveply sink). Only frozen seeds/images differ.
    /// </summary>
    public static class MultisMaker17Workflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 64800;
        public const int DEFAULT_TRIPO_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_PLY_PREFIX = "3d/PLY_";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            TripoRt rt = new TripoRt();
            rt.Prompt = CoreVariantJson.EscapeJson(prompt ?? string.Empty);
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.FluxSteps = fluxSteps.ToString(inv);
            rt.FluxSeed = fluxSeed;
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            rt.FluxUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            rt.FluxClip = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            rt.FluxVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);
            OuterSpec spec = new OuterSpec();
            spec.HasP144 = true;
            spec.HasP145 = true;
            spec.PlyPrefix = DEFAULT_PLY_PREFIX;
            return CoreVariantJson.EmitOuter(spec, rt, fluxOnly, skipFlux, null);
        }
    }
        // ----5__

    /// <summary>
    /// MultisMaker_ObjectGenerator.json: 23 outer nodes + 17-node Flux
    /// subgraph (131). Same shape as MultisMaker4/15/17 (138 prompt,
    /// 133/144/145 previews, euler, ply, mm_saveply sink).
    /// </summary>
    public static class ObjectGeneratorWorkflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 64800;
        public const int DEFAULT_TRIPO_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_PLY_PREFIX = "3d/PLY_";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            TripoRt rt = new TripoRt();
            rt.Prompt = CoreVariantJson.EscapeJson(prompt ?? string.Empty);
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.FluxSteps = fluxSteps.ToString(inv);
            rt.FluxSeed = fluxSeed;
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            rt.FluxUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            rt.FluxClip = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            rt.FluxVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);
            OuterSpec spec = new OuterSpec();
            spec.HasP144 = true;
            spec.HasP145 = true;
            spec.PlyPrefix = DEFAULT_PLY_PREFIX;
            return CoreVariantJson.EmitOuter(spec, rt, fluxOnly, skipFlux, null);
        }
    }

    /// <summary>
    /// TripoSR_Moshu_Example.json: 18 outer nodes + 17-node Flux subgraph.
    /// Minimal tripo path: no 102/111 switches, no 109/112/115/116/145.
    /// 113 feeds 103 and 110 feeds 101 directly. UNET fp8_e5m2, decode
    /// 648000, previews 133/144 only, saver is 177 SaveGLB with an empty
    /// filename prefix. 134 JarJarSubmit3D dropped.
    /// </summary>
    public static class TripoSRMoshuWorkflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 648000;
        public const int DEFAULT_TRIPO_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_UNET_DTYPE = "fp8_e5m2";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            TripoRt rt = new TripoRt();
            rt.Prompt = CoreVariantJson.EscapeJson(prompt ?? string.Empty);
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.FluxSteps = fluxSteps.ToString(inv);
            rt.FluxSeed = fluxSeed;
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            rt.FluxUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxUnet) ? DEFAULT_FLUX_UNET : fluxUnet);
            rt.FluxClip = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxClip) ? DEFAULT_FLUX_CLIP : fluxClip);
            rt.FluxVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(fluxVae) ? DEFAULT_FLUX_VAE : fluxVae);
            OuterSpec spec = new OuterSpec();
            spec.HasSwitch102 = false;
            spec.HasSwitch111 = false;
            spec.HasPreview112 = false;
            spec.NoAlphaJoin = true;
            spec.UnetDtype = DEFAULT_UNET_DTYPE;
            spec.HasP144 = true;
            spec.Saver177 = true;
            return CoreVariantJson.EmitOuter(spec, rt, fluxOnly, skipFlux, null);
        }
    }

    /// <summary>
    /// GLB OUTPUT.json: 15 outer nodes + b64/background subgraphs.
    /// Outer num_gaussians 262144, seed 51; inner dpmpp_2m 20 steps cfg 3.
    /// Extras: 76 SplatToMesh [384,5,0,0.6,500,0.02,2] + 67 saver, render
    /// chain, 100 JarJarSplatToMesh (glb into ./output; writes outside the
    /// ComfyUI output folder by design). JarJarPreviewMesh dropped.
    /// </summary>
    public static class GlbOutputWorkflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 262144;
        public const int DEFAULT_TRIPO_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_SAMPLER = "dpmpp_2m";
        public const string DEFAULT_CFG = "3";
        public const int DEFAULT_PREVIEW_GAUSSIANS = 16384;
        public const string DEFAULT_PLY_PREFIX = "3d/ComfyUI_TripoSplat";
        public const int DEFAULT_MESH_RES = 384;
        public const int DEFAULT_MESH_KERNEL = 5;
        public const int DEFAULT_MESH_SMOOTH = 0;
        public const double DEFAULT_MESH_LEVEL = 0.6;
        public const int DEFAULT_MESH_MIN_COMP = 500;
        public const double DEFAULT_MESH_MIN_OP = 0.02;
        public const int DEFAULT_MESH_SHARP = 2;
        public const string DEFAULT_VIDEO_PREFIX = "video/ComfyUI_TripoSplat";
        public const string DEFAULT_MESH_OUTPUT_FOLDER = "./output";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            CoreRt rt = new CoreRt();
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            CoreSpec spec = new CoreSpec();
            spec.TripoCfg = DEFAULT_CFG;
            spec.TripoSampler = DEFAULT_SAMPLER;
            spec.PreviewNums = DEFAULT_PREVIEW_GAUSSIANS.ToString(inv);
            spec.PlyPrefix = DEFAULT_PLY_PREFIX;
            spec.MeshKind = 1;
            spec.MeshRes = DEFAULT_MESH_RES;
            spec.MeshKernel = DEFAULT_MESH_KERNEL;
            spec.MeshSmooth = DEFAULT_MESH_SMOOTH;
            spec.MeshLevel = DEFAULT_MESH_LEVEL;
            spec.MeshMinComp = DEFAULT_MESH_MIN_COMP;
            spec.MeshMinOp = DEFAULT_MESH_MIN_OP;
            spec.MeshSharp = DEFAULT_MESH_SHARP;
            spec.MeshPrefix = DEFAULT_PLY_PREFIX;
            spec.RenderChainFlag = true;
            spec.VideoPrefix = DEFAULT_VIDEO_PREFIX;
            spec.JarJarMesh = true;
            return CoreVariantJson.EmitCore(spec, rt, fluxOnly);
        }
    }

    /// <summary>
    /// ODIN_CLICK_HERE.json: 14 outer nodes + b64/background subgraphs.
    /// Outer num_gaussians 262144, seed 47; inner dpmpp_2m 20 steps cfg 3.
    /// Extras: 76 SplatToMesh [768,7,0,0.04,50,0.03,2] + 67 saver, render
    /// chain, 102 JarJarSaveGLB (./output/mesh.glb side-effect write).
    /// PreviewAny viewer dropped.
    /// </summary>
    public static class OdinWorkflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 262144;
        public const int DEFAULT_TRIPO_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_SAMPLER = "dpmpp_2m";
        public const string DEFAULT_CFG = "3";
        public const int DEFAULT_PREVIEW_GAUSSIANS = 16384;
        public const string DEFAULT_PLY_PREFIX = "3d/ComfyUI_TripoSplat";
        public const int DEFAULT_MESH_RES = 768;
        public const int DEFAULT_MESH_KERNEL = 7;
        public const int DEFAULT_MESH_SMOOTH = 0;
        public const double DEFAULT_MESH_LEVEL = 0.04;
        public const int DEFAULT_MESH_MIN_COMP = 50;
        public const double DEFAULT_MESH_MIN_OP = 0.03;
        public const int DEFAULT_MESH_SHARP = 2;
        public const string DEFAULT_VIDEO_PREFIX = "video/ComfyUI_TripoSplat";
        public const string DEFAULT_SAVE_PATH = "./output/mesh.glb";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            CoreRt rt = new CoreRt();
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            CoreSpec spec = new CoreSpec();
            spec.TripoCfg = DEFAULT_CFG;
            spec.TripoSampler = DEFAULT_SAMPLER;
            spec.PreviewNums = DEFAULT_PREVIEW_GAUSSIANS.ToString(inv);
            spec.PlyPrefix = DEFAULT_PLY_PREFIX;
            spec.MeshKind = 1;
            spec.MeshRes = DEFAULT_MESH_RES;
            spec.MeshKernel = DEFAULT_MESH_KERNEL;
            spec.MeshSmooth = DEFAULT_MESH_SMOOTH;
            spec.MeshLevel = DEFAULT_MESH_LEVEL;
            spec.MeshMinComp = DEFAULT_MESH_MIN_COMP;
            spec.MeshMinOp = DEFAULT_MESH_MIN_OP;
            spec.MeshSharp = DEFAULT_MESH_SHARP;
            spec.MeshPrefix = DEFAULT_PLY_PREFIX;
            spec.RenderChainFlag = true;
            spec.VideoPrefix = DEFAULT_VIDEO_PREFIX;
            spec.JarJarSaveGlb = true;
            return CoreVariantJson.EmitCore(spec, rt, fluxOnly);
        }
    }
        // ----6__

    /// <summary>
    /// VRAK_IMAGE_TO_3D_BIPED.json: 10 outer nodes + b64/background
    /// subgraphs. Outer num_gaussians 262176, seed 47; inner dpmpp_2m
    /// 20 steps cfg 3. Extras: 76 SplatToMesh [768,7,0,0.04,50,0.03,2] +
    /// 67 saver and the render turntable chain. No other extras.
    /// </summary>
    public static class VrakBipedWorkflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 262176;
        public const int DEFAULT_TRIPO_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_SAMPLER = "dpmpp_2m";
        public const string DEFAULT_CFG = "3";
        public const int DEFAULT_PREVIEW_GAUSSIANS = 16384;
        public const string DEFAULT_PLY_PREFIX = "3d/ComfyUI_TripoSplat";
        public const int DEFAULT_MESH_RES = 768;
        public const int DEFAULT_MESH_KERNEL = 7;
        public const int DEFAULT_MESH_SMOOTH = 0;
        public const double DEFAULT_MESH_LEVEL = 0.04;
        public const int DEFAULT_MESH_MIN_COMP = 50;
        public const double DEFAULT_MESH_MIN_OP = 0.03;
        public const int DEFAULT_MESH_SHARP = 2;
        public const string DEFAULT_VIDEO_PREFIX = "video/ComfyUI_TripoSplat";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            CoreRt rt = new CoreRt();
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            CoreSpec spec = new CoreSpec();
            spec.TripoCfg = DEFAULT_CFG;
            spec.TripoSampler = DEFAULT_SAMPLER;
            spec.PreviewNums = DEFAULT_PREVIEW_GAUSSIANS.ToString(inv);
            spec.PlyPrefix = DEFAULT_PLY_PREFIX;
            spec.MeshKind = 1;
            spec.MeshRes = DEFAULT_MESH_RES;
            spec.MeshKernel = DEFAULT_MESH_KERNEL;
            spec.MeshSmooth = DEFAULT_MESH_SMOOTH;
            spec.MeshLevel = DEFAULT_MESH_LEVEL;
            spec.MeshMinComp = DEFAULT_MESH_MIN_COMP;
            spec.MeshMinOp = DEFAULT_MESH_MIN_OP;
            spec.MeshSharp = DEFAULT_MESH_SHARP;
            spec.MeshPrefix = DEFAULT_PLY_PREFIX;
            spec.RenderChainFlag = true;
            spec.VideoPrefix = DEFAULT_VIDEO_PREFIX;
            return CoreVariantJson.EmitCore(spec, rt, fluxOnly);
        }
    }

    /// <summary>
    /// HELP HEREV5.json: 4 outer nodes + b64/background subgraphs, the
    /// minimal full-res variant. Outer num_gaussians 1048576, seed 49;
    /// inner dpmpp_2m 20 steps cfg 4, preview 7/16384/90/15/2. 92 format
    /// spz straight into the 3d/ComfyUI_TripoSplat_FullRes saver. prompt,
    /// fluxSteps and skipFlux are accepted for uniformity but have no
    /// effect (no text input, no flux stage, nothing to skip).
    /// </summary>
    public static class HelpHereV5Workflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 1048576;
        public const int DEFAULT_TRIPO_STEPS = 20;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_SAMPLER = "dpmpp_2m";
        public const string DEFAULT_CFG = "4";
        public const int DEFAULT_PREVIEW_OCT = 7;
        public const int DEFAULT_PREVIEW_GAUSSIANS = 16384;
        public const string DEFAULT_SPLAT_FORMAT = "spz";
        public const string DEFAULT_PLY_PREFIX = "3d/ComfyUI_TripoSplat_FullRes";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            CoreRt rt = new CoreRt();
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            CoreSpec spec = new CoreSpec();
            spec.TripoCfg = DEFAULT_CFG;
            spec.TripoSampler = DEFAULT_SAMPLER;
            spec.PreviewOct = DEFAULT_PREVIEW_OCT.ToString(inv);
            spec.PreviewNums = DEFAULT_PREVIEW_GAUSSIANS.ToString(inv);
            spec.SplatFormat = DEFAULT_SPLAT_FORMAT;
            spec.PlyPrefix = DEFAULT_PLY_PREFIX;
            return CoreVariantJson.EmitCore(spec, rt, fluxOnly);
        }
    }

    /// <summary>
    /// HELP HEREV6.json: 12 outer nodes + b64/background subgraphs.
    /// Outer num_gaussians 1048576, seed 53; inner dpmpp_2m 21 steps cfg 3,
    /// preprocess size 2048, preview 7/16384/90/15/2. Post chain: 118
    /// IdolPruneSplat (0.02/0.84) feeds the ply saver 51 (FullRes) while
    /// 126 JarJarSplatFillLight (0.25/0.06) + 127 SplatToFile3D spz feed
    /// 128 (ShadowRemoved) and 187 (3d/ComfyUI) savers. 124
    /// ResizeImageMaskNode (2048x2048) pre-scales the core image input;
    /// the 188 JarJarSubmit3D upload is dropped.
    /// </summary>
    public static class HelpHereV6Workflow
    {
        public const string DEFAULT_TRIPO_UNET = "triposplat_fp16.safetensors";
        public const string DEFAULT_TRIPO_VAE = "triposplat_vae_decoder_fp16.safetensors";
        public const string DEFAULT_COND_VAE = "flux2-vae.safetensors";
        public const string DEFAULT_CLIP_VISION = "dino_v3_vit_h.safetensors";
        public const string DEFAULT_BG_REMOVAL = "birefnet.safetensors";
        public const string DEFAULT_FLUX_UNET = "flux-2-klein-4b-fp8.safetensors";
        public const string DEFAULT_FLUX_CLIP = "qwen_3_4b.safetensors";
        public const string DEFAULT_FLUX_VAE = "flux2-vae.safetensors";
        public const int DEFAULT_NUM_GAUSSIANS = 1048576;
        public const int DEFAULT_TRIPO_STEPS = 21;
        public const int DEFAULT_FLUX_STEPS = 4;
        public const string DEFAULT_SAMPLER = "dpmpp_2m";
        public const string DEFAULT_CFG = "3";
        public const int DEFAULT_PREPROCESS_SIZE = 2048;
        public const int DEFAULT_PREVIEW_OCT = 7;
        public const int DEFAULT_PREVIEW_GAUSSIANS = 16384;
        public const string DEFAULT_PLY_PREFIX = "3d/ComfyUI_TripoSplat_FullRes";
        public const double DEFAULT_PRUNE_OPACITY = 0.02;
        public const double DEFAULT_PRUNE_MAX_SCALE = 0.84;
        public const double DEFAULT_FILL = 0.25;
        public const double DEFAULT_FILL_THRESHOLD = 0.06;
        public const string DEFAULT_SHADOW_PREFIX = "3d/ComfyUI_ShadowRemoved";
        public const string DEFAULT_POST_PREFIX = "3d/ComfyUI";

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
            var inv = CultureInfo.InvariantCulture;
            string kSeed, fluxSeed;
            CoreVariantJson.SplitSeeds(seed, out kSeed, out fluxSeed);
            if (steps < 1) steps = DEFAULT_TRIPO_STEPS;
            if (fluxSteps < 1) fluxSteps = DEFAULT_FLUX_STEPS;
            if (numGaussians < 1) numGaussians = DEFAULT_NUM_GAUSSIANS;
            CoreRt rt = new CoreRt();
            rt.KSeed = kSeed;
            rt.DSeed = fluxSeed;
            rt.Steps = steps.ToString(inv);
            rt.NumGaussians = numGaussians.ToString(inv);
            rt.ImageFile = CoreVariantJson.EscapeJson(inputImageFilename ?? string.Empty);
            rt.TripoUnet = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoUnet) ? DEFAULT_TRIPO_UNET : tripoUnet);
            rt.TripoVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(tripoVae) ? DEFAULT_TRIPO_VAE : tripoVae);
            rt.CondVae = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(condVae) ? DEFAULT_COND_VAE : condVae);
            rt.ClipVision = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(clipVision) ? DEFAULT_CLIP_VISION : clipVision);
            rt.BgRemoval = CoreVariantJson.EscapeJson(string.IsNullOrWhiteSpace(bgRemoval) ? DEFAULT_BG_REMOVAL : bgRemoval);
            CoreSpec spec = new CoreSpec();
            spec.TripoCfg = DEFAULT_CFG;
            spec.TripoSampler = DEFAULT_SAMPLER;
            spec.PreprocessSize = DEFAULT_PREPROCESS_SIZE.ToString(inv);
            spec.PreviewOct = DEFAULT_PREVIEW_OCT.ToString(inv);
            spec.PreviewNums = DEFAULT_PREVIEW_GAUSSIANS.ToString(inv);
            spec.PlyPrefix = DEFAULT_PLY_PREFIX;
            spec.ShadowChain = true;
            spec.ResizeNode = true;
            return CoreVariantJson.EmitCore(spec, rt, fluxOnly);
        }
    }
}