using System;
using System.Collections.Generic;
using MeesaMultisMaker.Audio;
using MeesaMultisMaker.ThreeD;

namespace MeesaMultisMaker.ComfyUI
{
    /// <summary>
    /// Central registry of every built-in ComfyUI workflow builder.
    ///
    /// Each entry names the frozen UI-format source file, a display name, a
    /// category (SFX / Music / VoiceClone / VoiceDesign / 3D / Image /
    /// Utility) and a factory delegate returning QueuePrompt-ready API JSON.
    /// Byte-identical copies are embedded once and noted as aliases:
    /// - MultisMaker1 - Copy.json == MultisMaker1.json (skipped, covered by "MultisMaker1")
    /// - ABC123.json == pixal3d_example_workflow.json (covered by "Pixal3DExample")
    /// - FixThisWorkflow.backup.json == HunyuanSplikt.json (covered by "HunyuanSplikt")
    /// - RapidFlux x3: ComfyUI_00064_.json == MeesaJarJar_RapidUO_Flux.json ==
    ///   RAPIDFLUX.json (covered by "RapidFlux")
    /// - FluxKlein2ImageEdit family: FluxKlein2ImageEdit.json ==
    ///   image_flux2_klein_image_edit_4b_distilled.json == KAYKAY.json, plus
    ///   image_flux2_klein_image_edit_4b_distilled (2)ddd.json (same 8-node
    ///   dual-branch graph with populated prompts; covered by "FluxKlein2ImageEdit")
    /// - FluxKlein single-edit family: JarJarImageEdit.json ==
    ///   FluxKleinExample.json == VRAK_PROMPT_TO_HUMANOID_IMAGE.json
    /// - Seamless family: SEAMLESS.json == 05-seamless_texture.json ==
    ///   seamless_texture_test.json (+ MCPTEST1.json, same mtb family)
    /// - Voice-clone trio TEXT2SPEACHWORKINGFASTQWEN.json /
    ///   9SecondVoiceCloneFromSample.json / PERFECT_VOICE_CLONE.json share one
    ///   7-node graph; only the template ref clip differs (DIABLO.mp3 vs
    ///   abby5.mp3), so each keeps its own entry with its own ref default.
    /// </summary>
    public sealed class BuiltInWorkflowEntry
    {
        public string Key;
        public string SourceFile;
        public string DisplayName;
        public string Category;
        public string[] Aliases;
        public Func<string> Build;
    }

    public static class BuiltInWorkflowRegistry
    {
        public static readonly IReadOnlyList<BuiltInWorkflowEntry> All;

        static BuiltInWorkflowRegistry()
        {
            var all = new List<BuiltInWorkflowEntry>();

            // ---------------- Audio: SFX / Music ----------------
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "StableAudioSfx", SourceFile = "FASTAUDIOTGEN.json",
                DisplayName = "Stable Audio SFX (FASTAUDIOTGEN layout)", Category = "SFX",
                Aliases = new string[0],
                Build = () => StableAudioWorkflow.CreateWorkflow("test", "", 10, 1, 8, false, "SFX")
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "StableAudioMusic", SourceFile = "WORKINGAUDIO.json",
                DisplayName = "Stable Audio Music (WORKINGAUDIO layout)", Category = "Music",
                Aliases = new string[0],
                Build = () => StableAudioWorkflow.CreateWorkflow("test", "", 10, 1)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "StableAudio5Mil", SourceFile = "FASTAUDIOTGEN_5mil.json",
                DisplayName = "Stable Audio 5M batch (UO captions CSV)", Category = "SFX",
                Aliases = new string[0],
                Build = () => StableAudio5MilWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "StableAudioUoMusic", SourceFile = "FASTAUDIOTGEN_UO_MUSIC_REPLACE.json",
                DisplayName = "Stable Audio UO music REPLACE batch", Category = "Music",
                Aliases = new string[0],
                Build = () => StableAudioUoMusicWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "StableAudioUoSfx", SourceFile = "FASTAUDIOTGEN_UO_SFX_REPLACE.json",
                DisplayName = "Stable Audio UO SFX REPLACE batch", Category = "SFX",
                Aliases = new string[0],
                Build = () => StableAudioUoSfxWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "FoleyArtist", SourceFile = "MeesaJarJar_FastAudioFX_FoleyArtist_ComfyUI.json",
                DisplayName = "FoleyArtist text-list batch (ant SFX)", Category = "SFX",
                Aliases = new string[0],
                Build = () => FoleyArtistWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "WaspOllama", SourceFile = "a_giant_wasp_being_hurt_buzzing_stuttering_angry_high-pitched_squeal_00002.json",
                DisplayName = "Wasp SFX (Ollama reprompt)", Category = "SFX",
                Aliases = new string[0],
                Build = () => WaspOllamaWorkflow.CreateWorkflow()
            });

            // ---------------- Audio: VoiceClone / VoiceDesign ----------------
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "Qwen3VoiceClone", SourceFile = "TEXT2SPEACHWORKINGFASTQWEN.json",
                DisplayName = "Qwen3 voice clone (DIABLO ref)", Category = "VoiceClone",
                Aliases = new string[0],
                Build = () => Qwen3VoiceCloneWorkflow.CreateWorkflow("hello")
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "NineSecondClone", SourceFile = "9SecondVoiceCloneFromSample.json",
                DisplayName = "Qwen3 9-second voice clone (abby5 ref)", Category = "VoiceClone",
                Aliases = new string[] { "Same 7-node graph as TEXT2SPEACHWORKINGFASTQWEN.json; ref clip abby5.mp3." },
                Build = () => NineSecondCloneWorkflow.CreateWorkflow("hello", seed: 1)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "PerfectVoiceClone", SourceFile = "PERFECT_VOICE_CLONE.json",
                DisplayName = "Qwen3 perfect voice clone (DIABLO ref)", Category = "VoiceClone",
                Aliases = new string[] { "Same 7-node graph as TEXT2SPEACHWORKINGFASTQWEN.json; ref clip DIABLO.mp3." },
                Build = () => PerfectVoiceCloneWorkflow.CreateWorkflow("hello", seed: 1)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "PerfectVoiceDesign", SourceFile = "PERFECT_VOICE_DESIGN.json",
                DisplayName = "Qwen3 voice design", Category = "VoiceDesign",
                Aliases = new string[0],
                Build = () => PerfectVoiceDesignWorkflow.CreateWorkflow("hello", "A natural narrator voice.", seed: 1)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "Qwen25Sec", SourceFile = "QWEN_VOICE_GENERATOR_25_SECONDS.json",
                DisplayName = "Qwen3 voice design + prompt distill (25s)", Category = "VoiceDesign",
                Aliases = new string[0],
                Build = () => Qwen25SecWorkflow.CreateWorkflow("hello", "A natural narrator voice.", seed: 1)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "VoiceDesignExample", SourceFile = "VOICE_DESIGN_EXAMPLE_AND_LOADER.json",
                DisplayName = "Qwen3 design + prompt-fed clone", Category = "VoiceDesign",
                Aliases = new string[0],
                Build = () => VoiceDesignExampleWorkflow.CreateWorkflow("hello", "A natural narrator voice.", "hello again")
            });

            // ---------------- 3D: MultisMaker1 + core variants ----------------
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "MultisMaker1", SourceFile = "MultisMaker1.json",
                DisplayName = "MultisMaker1 (Flux.2 Klein + TripoSplat)", Category = "3D",
                Aliases = new string[] { "MultisMaker1 - Copy.json (byte-identical, skipped)" },
                Build = () => MultisMaker1Workflow.BuildBuiltIn("test", 1, "", 20, 4).PromptJson
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "B1", SourceFile = "B1.json",
                DisplayName = "B1 (direct-image TripoSplat)", Category = "3D",
                Aliases = new string[0],
                Build = () => B1Workflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "B2", SourceFile = "B2.json",
                DisplayName = "B2 (direct-image TripoSplat, dpm_fast)", Category = "3D",
                Aliases = new string[0],
                Build = () => B2Workflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "B3", SourceFile = "B3.json",
                DisplayName = "B3 (Flux + TripoSplat)", Category = "3D",
                Aliases = new string[0],
                Build = () => B3Workflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "B4", SourceFile = "B4.json",
                DisplayName = "B4 (Flux + TripoSplat)", Category = "3D",
                Aliases = new string[0],
                Build = () => B4Workflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "B5", SourceFile = "B5.json",
                DisplayName = "B5 (Flux + TripoSplat)", Category = "3D",
                Aliases = new string[0],
                Build = () => B5Workflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "B6", SourceFile = "B6.json",
                DisplayName = "B6 (chained dual Flux edit + TripoSplat)", Category = "3D",
                Aliases = new string[0],
                Build = () => B6Workflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "B7", SourceFile = "B7.json",
                DisplayName = "B7 (Flux + TripoSplat)", Category = "3D",
                Aliases = new string[0],
                Build = () => B7Workflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "Best3DEver", SourceFile = "BEST 3D EVER .json",
                DisplayName = "Best 3D Ever (tripo-core subgraph)", Category = "3D",
                Aliases = new string[0],
                Build = () => Best3DEverWorkflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "Best3DEverTry2", SourceFile = "BEST 3D EVER _TRY2.json",
                DisplayName = "Best 3D Ever Try2", Category = "3D",
                Aliases = new string[0],
                Build = () => Best3DEverTry2Workflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "MultisMaker3", SourceFile = "MultisMaker3.json",
                DisplayName = "MultisMaker3 (Flux + TripoSplat)", Category = "3D",
                Aliases = new string[0],
                Build = () => MultisMaker3Workflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "MultisMaker4", SourceFile = "MultisMaker4.json",
                DisplayName = "MultisMaker4 (Flux + TripoSplat)", Category = "3D",
                Aliases = new string[0],
                Build = () => MultisMaker4Workflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "MultisMaker5", SourceFile = "MultisMaker5.json",
                DisplayName = "MultisMaker5 (Flux + TripoSplat)", Category = "3D",
                Aliases = new string[0],
                Build = () => MultisMaker5Workflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "MultisMaker12", SourceFile = "MultisMaker12.json",
                DisplayName = "MultisMaker12 (Flux + TripoSplat)", Category = "3D",
                Aliases = new string[0],
                Build = () => MultisMaker12Workflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "MultisMaker13", SourceFile = "MultisMaker13.json",
                DisplayName = "MultisMaker13 (Flux + TripoSplat, batch)", Category = "3D",
                Aliases = new string[0],
                Build = () => MultisMaker13Workflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "MultisMaker15", SourceFile = "MultisMaker15.json",
                DisplayName = "MultisMaker15 (Flux + TripoSplat)", Category = "3D",
                Aliases = new string[0],
                Build = () => MultisMaker15Workflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "MultisMaker16", SourceFile = "MultisMaker16.json",
                DisplayName = "MultisMaker16 (flat fused TripoSplat)", Category = "3D",
                Aliases = new string[0],
                Build = () => MultisMaker16Workflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "MultisMaker17", SourceFile = "MultisMaker17.json",
                DisplayName = "MultisMaker17 (Flux + TripoSplat)", Category = "3D",
                Aliases = new string[0],
                Build = () => MultisMaker17Workflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "ObjectGenerator", SourceFile = "MultisMaker_ObjectGenerator.json",
                DisplayName = "Object generator (Flux + TripoSplat)", Category = "3D",
                Aliases = new string[0],
                Build = () => ObjectGeneratorWorkflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "TripoSRMoshu", SourceFile = "TripoSR_Moshu_Example.json",
                DisplayName = "TripoSR Moshu example", Category = "3D",
                Aliases = new string[0],
                Build = () => TripoSRMoshuWorkflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "GlbOutput", SourceFile = "GLB OUTPUT.json",
                DisplayName = "GLB output (tripo-core subgraph)", Category = "3D",
                Aliases = new string[0],
                Build = () => GlbOutputWorkflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "Odin", SourceFile = "ODIN_CLICK_HERE.json",
                DisplayName = "Odin (tripo-core subgraph)", Category = "3D",
                Aliases = new string[0],
                Build = () => OdinWorkflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "VrakBiped", SourceFile = "VRAK_IMAGE_TO_3D_BIPED.json",
                DisplayName = "Vrak image-to-3D biped", Category = "3D",
                Aliases = new string[0],
                Build = () => VrakBipedWorkflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "HelpHereV5", SourceFile = "HELP HEREV5.json",
                DisplayName = "Help Here V5", Category = "3D",
                Aliases = new string[0],
                Build = () => HelpHereV5Workflow.CreateWorkflow("test", 1, 20)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "HelpHereV6", SourceFile = "HELP HEREV6.json",
                DisplayName = "Help Here V6", Category = "3D",
                Aliases = new string[0],
                Build = () => HelpHereV6Workflow.CreateWorkflow("test", 1, 20)
            });

            // ---------------- 3D: extended variants ----------------
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "ArmorGen", SourceFile = "ArmorGen.json",
                DisplayName = "ArmorGen (Flux armor edit + TripoSplat + UniRig)", Category = "3D",
                Aliases = new string[0],
                Build = () => ArmorGenWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "FixThisHy3D", SourceFile = "FixThisWorkflow.json",
                DisplayName = "FixThis Hy3D (Tripo + Hy paint + splat)", Category = "3D",
                Aliases = new string[0],
                Build = () => FixThisHy3DWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "HunyuanSplikt", SourceFile = "HunyuanSplikt.json",
                DisplayName = "Hunyuan splat (Tripo + Hy paint + splat)", Category = "3D",
                Aliases = new string[] { "FixThisWorkflow.backup.json (byte-identical API graph, embedded once)" },
                Build = () => HunyuanSpliktWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "HunyuanRepaintGlb", SourceFile = "Hunyuan_Repaint_GLB.json",
                DisplayName = "Hunyuan repaint GLB", Category = "3D",
                Aliases = new string[0],
                Build = () => HunyuanRepaintGlbWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "PartSplitter", SourceFile = "MEESAJARJAR_PART_SPLITTER.json",
                DisplayName = "Part splitter (splat clustering)", Category = "3D",
                Aliases = new string[0],
                Build = () => PartSplitterWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "Watertight", SourceFile = "WATERTIGHTTEST.json",
                DisplayName = "Watertight test (splat clustering)", Category = "3D",
                Aliases = new string[0],
                Build = () => WatertightWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "FixThis1Preview", SourceFile = "FIXTHIS1.json",
                DisplayName = "FixThis1 splat preview", Category = "3D",
                Aliases = new string[0],
                Build = () => FixThis1PreviewWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "FixThis1FramedPreview", SourceFile = "FIXTHIS1_FRAMED.json",
                DisplayName = "FixThis1 framed splat preview", Category = "3D",
                Aliases = new string[0],
                Build = () => FixThis1FramedPreviewWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "ProofConcept1", SourceFile = "PROOF_CONCEPT_1.json",
                DisplayName = "Proof of concept 1 (TripoSplat)", Category = "3D",
                Aliases = new string[0],
                Build = () => ProofConcept1Workflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "ProofConceptFast", SourceFile = "PROOF_CONCEPT_FAST.json",
                DisplayName = "Proof of concept fast (TripoSplat)", Category = "3D",
                Aliases = new string[0],
                Build = () => ProofConceptFastWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "TripoSplatVideo", SourceFile = "3d_triposplat_image_to_gaussian_splat (2).json",
                DisplayName = "TripoSplat video (image to gaussian splat)", Category = "3D",
                Aliases = new string[0],
                Build = () => TripoSplatVideoWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "TripoSplatUniRig", SourceFile = "3d_triposplat_image_to_gaussian_splat.json",
                DisplayName = "TripoSplat + UniRig (PixalComfy)", Category = "3D",
                Aliases = new string[0],
                Build = () => TripoSplatUniRigWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "Pixal3DExample", SourceFile = "ABC123.json",
                DisplayName = "Pixal 3D example (Flux + TripoSplat + rig)", Category = "3D",
                Aliases = new string[] { "pixal3d_example_workflow.json (differs by one dropped-PreviewAny byte; embedded once)" },
                Build = () => Pixal3DExampleWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "Faggh", SourceFile = "faggh.json",
                DisplayName = "Faggh (Tripo + retexture + GLB)", Category = "3D",
                Aliases = new string[0],
                Build = () => FagghWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "JarJarHumanRig", SourceFile = "JarJarV3_HumanAndRig.json",
                DisplayName = "JarJar human + rig (Flux + TripoSplat)", Category = "3D",
                Aliases = new string[0],
                Build = () => JarJarHumanRigWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "JarJarHumanRig22", SourceFile = "JarJarV3_HumanAndRig22.json",
                DisplayName = "JarJar human + rig 22", Category = "3D",
                Aliases = new string[0],
                Build = () => JarJarHumanRig22Workflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "JarJarHumanRig22Faster", SourceFile = "JarJarV3_HumanAndRig22Faster.json",
                DisplayName = "JarJar human + rig 22 faster", Category = "3D",
                Aliases = new string[0],
                Build = () => JarJarHumanRig22FasterWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "JarJarHumanRigV5", SourceFile = "JarJarV3_HumanAndRigV5.json",
                DisplayName = "JarJar human + rig V5", Category = "3D",
                Aliases = new string[0],
                Build = () => JarJarHumanRigV5Workflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "PipelineImageToRigged", SourceFile = "PIPELINE_IMAGETORIGGED.json",
                DisplayName = "Pipeline image-to-rigged", Category = "3D",
                Aliases = new string[0],
                Build = () => PipelineImageToRiggedWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "UniRigHumanoid", SourceFile = "unirig_humanoid.json",
                DisplayName = "UniRig humanoid", Category = "3D",
                Aliases = new string[0],
                Build = () => UniRigHumanoidWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "SpacePixal", SourceFile = "SPACE.json",
                DisplayName = "Space Pixal (Flux + TripoSplat)", Category = "3D",
                Aliases = new string[0],
                Build = () => SpacePixalWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "JobRetexture", SourceFile = "Job 6d934aea-7587-4787-b729-7b38f0abd783.json",
                DisplayName = "Job retexture (GLB paint-over)", Category = "3D",
                Aliases = new string[0],
                Build = () => JobRetextureWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "LookhereTripoSplat", SourceFile = "LOOKHERE.json",
                DisplayName = "Lookhere (Klein edit + TripoSplat)", Category = "3D",
                Aliases = new string[0],
                Build = () => LookhereTripoSplatWorkflow.CreateWorkflow("test")
            });

            // ---------------- Image: existing generic builders ----------------
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "Text2Image", SourceFile = "(generic hand-written builder; no frozen UI file)",
                DisplayName = "Text to image (SD)", Category = "Image",
                Aliases = new string[0],
                Build = () => Text2ImageWorkflow.CreateWorkflow("test")
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "Flux2Klein", SourceFile = "(generic hand-written builder; no frozen UI file)",
                DisplayName = "Flux.2 Klein text to image", Category = "Image",
                Aliases = new string[0],
                Build = () => Flux2KleinWorkflow.CreateWorkflow("test")
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "Image2Image", SourceFile = "(generic hand-written builder; no frozen UI file)",
                DisplayName = "Image to image (SD)", Category = "Image",
                Aliases = new string[0],
                Build = () => Image2ImageWorkflow.CreateWorkflow("test")
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "Dreamshaper", SourceFile = "(generic hand-written builder; no frozen UI file)",
                DisplayName = "Dreamshaper (SD)", Category = "Image",
                Aliases = new string[0],
                Build = () => DreamshaperWorkflow.CreateWorkflow("test")
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "Inpaint", SourceFile = "(generic hand-written builder; no frozen UI file)",
                DisplayName = "Inpaint (SD)", Category = "Image",
                Aliases = new string[0],
                Build = () => InpaintWorkflow.CreateWorkflow("test")
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "ControlNetDepth", SourceFile = "(generic hand-written builder; no frozen UI file)",
                DisplayName = "ControlNet depth", Category = "Image",
                Aliases = new string[0],
                Build = () => ControlNetDepthWorkflow.CreateWorkflow("test")
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "Flux2KleinImage2Image", SourceFile = "LOOKHERE_FLUXKLEIN2_IMAGE2IMAGE.json",
                DisplayName = "Flux.2 Klein reference-edit img2img", Category = "Image",
                Aliases = new string[0],
                Build = () => Flux2KleinImage2ImageWorkflow.CreateWorkflow("test")
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "Flux2KleinInpaint", SourceFile = "(generic hand-written builder; no frozen UI file)",
                DisplayName = "Flux.2 Klein inpaint", Category = "Image",
                Aliases = new string[0],
                Build = () => Flux2KleinInpaintWorkflow.CreateWorkflow("test", "", "in.png", "mask.png", 512, 512, 20, 7.0, 1, "euler")
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "Flux2KleinInpaintBatch", SourceFile = "(generic hand-written builder; no frozen UI file)",
                DisplayName = "Flux.2 Klein inpaint batch", Category = "Image",
                Aliases = new string[0],
                Build = () => Flux2KleinInpaintBatchWorkflow.CreateWorkflow("test", "", new List<string> { "a.png" }, new List<string> { "m.png" }, 512, 512, 20, 7.0, 1, "euler")
            });

            // ---------------- Image: frozen variants ----------------
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "FluxKleinSingleEdit", SourceFile = "JarJarImageEdit.json",
                DisplayName = "Flux.2 Klein single-image edit", Category = "Image",
                Aliases = new string[] { "FluxKleinExample.json", "VRAK_PROMPT_TO_HUMANOID_IMAGE.json" },
                Build = () => FluxKleinSingleEditWorkflow.CreateWorkflow("test", seed: 1)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "FluxKleinDualEdit", SourceFile = "WhiskeyTest.json",
                DisplayName = "Flux.2 Klein dual-image edit (style transfer)", Category = "Image",
                Aliases = new string[0],
                Build = () => FluxKleinDualEditWorkflow.CreateWorkflow("test", seed: 1)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "FluxKlein2ImageEdit", SourceFile = "FluxKlein2ImageEdit.json",
                DisplayName = "Flux.2 Klein 2-image edit (dual branch)", Category = "Image",
                Aliases = new string[] { "image_flux2_klein_image_edit_4b_distilled.json", "KAYKAY.json", "image_flux2_klein_image_edit_4b_distilled (2)ddd.json" },
                Build = () => FluxKlein2ImageEditWorkflow.CreateWorkflow("test single", "test dual", seedSingle: 1)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "NatExample", SourceFile = "NatExample.json",
                DisplayName = "Nat example (HD edit)", Category = "Image",
                Aliases = new string[0],
                Build = () => NatExampleWorkflow.CreateWorkflow("test", seed: 1)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "NatExample2Composite", SourceFile = "NatExample_2.json",
                DisplayName = "Nat example 2 (edit + composite)", Category = "Image",
                Aliases = new string[0],
                Build = () => NatExample2CompositeWorkflow.CreateWorkflow("test", seed: 1)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "UoItemGenerateFast", SourceFile = "MeesaJarJar_UO_Item_Generate_Fast.json",
                DisplayName = "UO item generate fast", Category = "Image",
                Aliases = new string[0],
                Build = () => UoItemGenerateFastWorkflow.CreateWorkflow("test", seed: 1)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "DfAsDgDaAutosegment", SourceFile = "dfasdgda.json",
                DisplayName = "Autosegment silhouette weapons", Category = "Image",
                Aliases = new string[0],
                Build = () => DfAsDgDaAutosegmentWorkflow.CreateWorkflow("test", seed: 1)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "HdProcessUo", SourceFile = "HD_PROCESS_UO.json",
                DisplayName = "HD process UO (folder batch)", Category = "Image",
                Aliases = new string[0],
                Build = () => HdProcessUoWorkflow.CreateWorkflow("test")
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "WtfBro", SourceFile = "WTF BRO.json",
                DisplayName = "WTF BRO (reference edit + pixelize)", Category = "Image",
                Aliases = new string[0],
                Build = () => WtfBroWorkflow.CreateWorkflow("test")
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "Flux3LayoutPrompt", SourceFile = "adfaf.json",
                DisplayName = "FLUX3 layout prompt (int8 DiT)", Category = "Image",
                Aliases = new string[0],
                Build = () => Flux3LayoutPromptWorkflow.CreateWorkflow("test layout", seed: 1)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "SeamlessTexture", SourceFile = "SEAMLESS.json",
                DisplayName = "Seamless texture (SD + mtb patch)", Category = "Image",
                Aliases = new string[] { "05-seamless_texture.json", "seamless_texture_test.json", "MCPTEST1.json" },
                Build = () => SeamlessTextureWorkflow.CreateWorkflow("test", seed: 1)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "PerfectSeamlessTexture", SourceFile = "PERFECT_SEAMLESS_TEXTURE_GENERATOR.json",
                DisplayName = "Perfect seamless texture generator", Category = "Image",
                Aliases = new string[0],
                Build = () => PerfectSeamlessTextureWorkflow.CreateWorkflow("test", seed: 1)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "PerfectSeamlessTextureSaver", SourceFile = "PERFECT_SEAMLESS_TEXTURE_GENERATOR_SAVER.json",
                DisplayName = "Perfect seamless texture saver (batch)", Category = "Image",
                Aliases = new string[0],
                Build = () => PerfectSeamlessTextureSaverWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "PerfectSeamlessTextureSaverStripped", SourceFile = "PERFECT_SEAMLESS_TEXTURE_GENERATOR_SAVER_StrippedDown.json",
                DisplayName = "Perfect seamless texture saver (stripped)", Category = "Image",
                Aliases = new string[0],
                Build = () => PerfectSeamlessTextureSaverStrippedWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "DlssUpscale", SourceFile = "DLSS_Upscale.json",
                DisplayName = "DLSS upscale", Category = "Image",
                Aliases = new string[0],
                Build = () => DlssUpscaleWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "RapidFlux", SourceFile = "ComfyUI_00064_.json",
                DisplayName = "RapidFlux UO sprite gen", Category = "Image",
                Aliases = new string[] { "MeesaJarJar_RapidUO_Flux.json", "RAPIDFLUX.json (identical 44-node graphs)" },
                Build = () => RapidFluxWorkflow.CreateWorkflow("test", seed: 1)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "RapidFluxFast", SourceFile = "rs23.json",
                DisplayName = "RapidFlux fast (rs23)", Category = "Image",
                Aliases = new string[0],
                Build = () => RapidFluxFastWorkflow.CreateWorkflow("test", seed: 1)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "ForElminsterRapidFluxGen", SourceFile = "ForElminsterFromMystra_RapidFluxGen.json",
                DisplayName = "RapidFlux gen (Elminster)", Category = "Image",
                Aliases = new string[0],
                Build = () => ForElminsterRapidFluxGenWorkflow.CreateWorkflow("test", seed: 1)
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "VangerPlants", SourceFile = "Vanger_Plants_00100_.json",
                DisplayName = "Vanger plants (reference edit + shadow)", Category = "Image",
                Aliases = new string[0],
                Build = () => VangerPlantsWorkflow.CreateWorkflow("test", seed: 1)
            });

            // ---------------- Utility ----------------
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "OllamaText", SourceFile = "OLLAMA.json",
                DisplayName = "Ollama text generation", Category = "Utility",
                Aliases = new string[0],
                Build = () => OllamaTextWorkflow.CreateWorkflow()
            });
            all.Add(new BuiltInWorkflowEntry
            {
                Key = "FluxKleinLoraTrainer", SourceFile = "TRAINER.json",
                DisplayName = "Flux Klein LoRA trainer (Musubi)", Category = "Utility",
                Aliases = new string[0],
                Build = () => FluxKleinLoraTrainerWorkflow.CreateWorkflow()
            });

            All = all.AsReadOnly();
        }

        public static BuiltInWorkflowEntry Find(string key)
        {
            for (int i = 0; i < All.Count; i++)
                if (string.Equals(All[i].Key, key, StringComparison.OrdinalIgnoreCase))
                    return All[i];
            return null;
        }

        public static IEnumerable<BuiltInWorkflowEntry> ByCategory(string category)
        {
            for (int i = 0; i < All.Count; i++)
                if (string.Equals(All[i].Category, category, StringComparison.OrdinalIgnoreCase))
                    yield return All[i];
        }
    }
}
