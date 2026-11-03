using System;
using System.Globalization;

namespace MeesaMultisMaker.Audio
{
    /// <summary>
    /// Built-in frozen copies of the remaining Stable Audio SFX/Music ComfyUI
    /// workflows as API-format (QueuePrompt-ready) JSON builders. No
    /// File.ReadAllText / template path: the graphs that AudioWorkflow
    /// conversion produced from the UI-format files are emitted directly, with
    /// prompt / duration / seed / steps / reprompt / model names injected.
    ///
    /// Freeze sources (D:\ComfyUIPortable\ComfyUI\user\default\workflows\):
    /// - FASTAUDIOTGEN_5mil.json (14 nodes, CSV batch): prompt + seconds rows
    ///   from UO_Captions.csv (JarJarCSVFileToList 67/73 + JarJarListItem 62/74),
    ///   SeedNode 70 row counter, JarJarIntToString 71 + StringConcatenate 72
    ///   output-dir saver suffix. Emits 22 nodes (29 with reprompt).
    /// - FASTAUDIOTGEN_UO_MUSIC_REPLACE.json (16 nodes): UO music REPLACE batch
    ///   with the "medieval, " prompt prefix (81), captions/seconds CSVs, the
    ///   music-names CSV saver chain (superseded by the saver pin, not emitted),
    ///   and one extra RepeatLatentBatch (85) in the subgraph core. Emits 22
    ///   nodes (29 with reprompt).
    /// - FASTAUDIOTGEN_UO_SFX_REPLACE.json (13 nodes): UO SFX REPLACE batch,
    ///   same shape as the 5Mil batch with the seconds CSV split out
    ///   (UO_Captions_Seconds.csv). Emits 22 nodes (29 with reprompt).
    /// - MeesaJarJar_FastAudioFX_FoleyArtist_ComfyUI.json (7 nodes): FoleyArtist
    ///   text-list batch (JarJarTextList 69, 5 ant prompts) with space/comma
    ///   saver sanitizers (58/71). Emits 18 nodes (25 with reprompt).
    /// - a_giant_wasp_being_hurt_buzzing_stuttering_angry_high-pitched_squeal_00002.json
    ///   (24 nodes, Ollama reprompt): Ollama song-description front end (79) +
    ///   category classifier (91/92) feeding the synth, PrimitiveInt 97 (300s)
    ///   duration, regex/sanitize filename chain. Emits 32 nodes (39 with reprompt).
    ///
    /// Post-patch semantics mirror StableAudioWorkflow (subgraph 8b66c757-...
    /// Audio Generation Stable Audio 3 Medium expanded inline as sg52_* nodes,
    /// subgraph outputs rewritten to sg52_12:0; JarJarAudioSaveAudio and
    /// PreviewAny viewers never emitted; the ComfySwitchNode reprompt branch is
    /// rewired to the raw prompt when use_reprompt is off and the LLM reprompt
    /// chain omitted with it; a mm_savewav SaveAudio sink (mm_audio prefix) is
    /// always present; linked saver prefixes are pinned to saverPrefix):
    /// - A null/empty prompt keeps the variant's native prompt source (CSV row
    ///   / text block / Ollama output); a non-empty prompt overrides it at the
    ///   terminal prompt widget, and the bypassed CSV readers are pruned so
    ///   queues stay green on machines without the CSVs. Pure string/plumbing
    ///   nodes left unreferenced are kept vestigially, mirroring the base SFX
    ///   builder's prompt-derived prefix chain.
    /// - durationSec &lt;= 0 keeps CSV row seconds (CSV variants); &gt; 0 stamps
    ///   the duration directly (clamped 1..600) and prunes the bypassed seconds
    ///   chain. FoleyArtist duration is always direct (default 3s); Wasp duration
    ///   rides PrimitiveInt 97 (default 300s, also feeding the "_sec_N" saver
    ///   fragment) and is always stamped.
    /// </summary>
    internal static class AudioVariantCore
    {
        // Frozen sampler widget values (inner KSampler node 3).
        public const int SAMPLER_CFG = 1;
        public const string SAMPLER_NAME = "lcm";
        public const string SCHEDULER = "simple";
        public const int SAMPLER_DENOISE = 1;

        // Frozen TextGenerate (reprompt LLM, inner node 28) widget values.
        public const int REPROMPT_MAX_LENGTH = 256;
        public const double REPROMPT_TEMPERATURE = 0.7;
        public const int REPROMPT_TOP_K = 64;
        public const double REPROMPT_TOP_P = 0.95;
        public const double REPROMPT_MIN_P = 0.05;
        public const double REPROMPT_REPETITION_PENALTY = 1.05;

        private const string PromptTemplate38 = "SYSTEM_PROMPTS\n\nInput: USER_INPUT\nTarget audio length: AUDIO_LENGTH seconds.\nOutput:";

        // Full JsonExtractString system-prompt JSON, frozen from inner node 49.
        // (Decoded text is identical to the StableAudioWorkflow copy; only the
        // file-level escaping of one dash differs.)
        internal const string RepromptTemplateJson = "{\\n  \\\"Music\\\": \\\"You are an expert musician and musicologist and prompt engineer. Transform the user's input into a detailed, vivid music prompt for a full instrumental track.\\\\n\\\\n1. Start with the genre or style and optional adjectives (e.g., upbeat, dreamy, aggressive).\\\\n2. List the main instruments that define the track.\\\\n3. Add supporting elements or layers such as pads, harmonics, effects, or field recordings.\\\\n4. Include rhythm or percussion elements like drums, hi-hats, congas, brushes, or polyrhythms.\\\\n5. Integrate mood and energy naturally in the sentence (e.g., \\\\\\\"creating suspenseful tension\\\\\\\" or \\\\\\\"bright and uplifting\\\\\\\").\\\\n6. Specify the BPM.\\\\n7. Specify the track length as an integer in seconds. Use ranges: energetic/dance 120-180s, pop/rock 180-210s, cinematic/ambient 240-300s.\\\\n8. Combine all elements into one natural, fluid sentence. Avoid semicolons.\\\\n\\\\nTemplate:\\\\nGenre/Style with main instruments, supporting instruments/layers, and rhythm/percussion creating mood/energy. BPM: X. Length: Y seconds\\\\n\\\\nExamples:\\\\n- Jazz ballad with smooth saxophone lead, piano chords, upright bass, brushed drums, and soft strings that swing gently for a warm and cozy evening. BPM: 85. Length: 180 seconds\\\\n- EDM festival track with pulsing synth leads, plucked arpeggios, layered pads, side-chained bass, punchy kick and snare, and hi-hat rolls creating bright, energetic, and uplifting dance energy. BPM: 128. Length: 150 seconds\\\\n- Lo-fi hip-hop chill track with mellow electric piano, soft vinyl crackle, subtle synth pads, low-pass filtered drums, percussion loops, and soft plucked bass for a relaxed, dreamy vibe. BPM: 75. Length: 150 seconds\\\\n- Heavy metal anthem with distorted electric guitars, bass guitar, double bass drums, and cymbal crashes with fast palm-muted riffs creating intense, aggressive energy. BPM: 160. Length: 180 seconds\\\\n- Melancholic piano piece with soft piano lead, string pads, subtle atmospheric synths, and minimal brush percussion evoking a reflective rainy-day feeling. BPM: 60. Length: 240 seconds\\\\n- Suspenseful electronic thriller with pulsing bass synth, arpeggiated lead synth, cinematic pads, glitchy percussion, and high string stabs creating dark and tense energy. BPM: 100. Length: 200 seconds\\\\n- Dreamy ambient soundscape with layered pads, soft bell textures, gentle drones, and wind and water field recordings for ethereal and spacious meditation. BPM: 40. Length: 300 seconds\\\\n- Fingerpicking acoustic guitar solo with harmonics, subtle reverb, occasional shaker and soft stomp percussion, and soft pad layers for warm intimate storytelling. BPM: 70. Length: 120 seconds\\\\n- Synthwave 80s retro track with arpeggiated synth leads, analog pads, electric bass, punchy electronic drums, gated reverb snares, and atmospheric FX for nostalgic and vibrant energy. BPM: 110. Length: 180 seconds\\\\n- Tribal percussion ensemble with congas, djembes, bongos, shakers, and frame drums layered with deep synthetic sub-bass in complex polyrhythms. BPM: 100. Length: 140 seconds\\\\n- 1920s swing jazz with brass section, upright bass, piano, brushed drums, banjo, clarinet, and soft strings that swing lively for energetic dance vibes. BPM: 110. Length: 180 seconds\\\\n- Futuristic electronic sci-fi track with pulsing bass synth, evolving lead synths, layered pads, glitch percussion, robotic FX, and sub-bass for tense cinematic energy. BPM: 125. Length: 200 seconds\\\\n- Ambient underwater soundscape with flowing water textures, soft piano motifs, synth drones, distant bells, and underwater reverb for spacious meditative immersion. BPM: 45. Length: 300 seconds\\\\n- Horror cinematic track with dissonant strings, eerie piano stabs, cinematic percussion including taiko and low toms, and synth FX producing suspenseful creepy tension. BPM: 90. Length: 240 seconds\\\\n- Reggae track with offbeat guitar, warm basslines, snare, kick, congas, and horn stabs giving laid-back groovy energy. BPM: 85. Length: 150 seconds\\\\n- Blues track with soulful electric guitar solos, walking bass, piano, and shuffle drums creating expressive and emotive storytelling. BPM: 90. Length: 180 seconds\\\\n- Latin salsa with congas, timbales, horns, piano montunos, bass, and layered percussion for vibrant danceable energy. BPM: 120. Length: 210 seconds\\\\n- Afrobeat track with electric guitar stabs, horns, layered percussion, congas, shakers, bass groove, and synth pads for vibrant rhythmic energy. BPM: 105. Length: 200 seconds\\\\n- Indie rock track with electric guitar riffs, bass, live drum kit, layered synths, and subtle strings for energetic yet emotional feel. BPM: 110. Length: 180 seconds\\\\n- Funk groove with slap bass, electric guitar chords, brass stabs, drums, congas, and rhythmic keyboards creating high-energy danceable rhythm. BPM: 105. Length: 180 seconds\\\\n- Drum and bass track with fast breakbeat drums, deep sub-bass, sharp synth leads, pads, and atmospheric FX for high-energy club motion. BPM: 175. Length: 150 seconds\\\\n- Dark ambient track with drones, distant bells, low rumbles, soft wind textures, and synth pads producing eerie immersive tension. BPM: 50. Length: 300 seconds\\\\n- Tropical house track with marimba, steel drums, soft synths, smooth bass, layered percussion, and light piano riffs for sunny chill dance vibes. BPM: 110. Length: 180 seconds\\\\n- Progressive rock track with electric guitar leads, organ, bass, drum kit, synth layers, and occasional strings for epic layered energy. BPM: 100. Length: 220 seconds\\\\n- Music box melody with delicate metallic tones and soft resonance, lullaby style, with gentle ambient reverb. BPM: 60. Length: 20 seconds\\\\n- Soft piano arpeggio with warm felted tone and slow attack, lullaby style, with intimate room ambience. BPM: 60. Length: 30 seconds\\\\n- Harp gentle plucked pattern with airy resonance, lullaby style, with dreamy reverb tail. BPM: 65. Length: 25 seconds\\\\n- Acoustic guitar fingerstyle pattern with warm nylon strings and soft dynamics, lullaby style, with subtle room resonance. BPM: 60. Length: 30 seconds\\\\n- Ambient synth pad with smooth evolving texture and soft harmonics, lullaby style, with wide stereo ambience. BPM: 50. Length: 40 seconds\\\\n- Early rock piano with walking left-hand bass line, shuffle rhythms, and blues scale improvisations in energetic 1950s boogie-woogie style. BPM: 160. Length: 180 seconds\\\\n- Trip Hop track with jazzy sampled vibraphone, mid-tempo breakbeat drums, harp, Latin ethnic percussion, and sweeping cinematic strings creating airy, relaxing, soulful lounge vibes. BPM: 90. Length: 180 seconds\\\\n- Country outlaw cinematic instrumental with blues pedal steel guitar, rustic mandolin, fiddle call-and-response, tape-driven rattly drum kit, autoharp, and soaring accordion solo for raw, emotional southern blues expression. BPM: 85. Length: 200 seconds\\\\n- Neo Classical track with sweeping string section, elegant horns, and delicate piano creating soothing, hypnotic, modern, soft, and classic mood. BPM: 70. Length: 180 seconds\\\\n- Art Rock desert track with desolate piano chords, western-themed rhythm guitars, unique lead guitars, rattly vintage drum kit, and supporting bass creating lonely, expansive, beautiful, and strange atmospheres. BPM: 95. Length: 180 seconds\\\\n- Cinematic Sci-Fi score with dramatic horn section, building marcato strings, gliding bassoon, thunderous cymbals, subdued timpani, and subtle synth drones producing awe-inspiring, uplifting, epic intergalactic energy. BPM: 100. Length: 220 seconds\\\\n- West Coast Hip Hop instrumental with cascading harp melodies, smooth Rhodes piano chops, vintage boom bap drums, and walking double bass producing raw, street, and soulful block-party vibes. BPM: 92. Length: 180 seconds\\\\n- Synthwave futuristic track with pulsating synth bass, exciting chords, soaring leads, and reverberating drum machine patterns creating gritty, pounding, and cool energy. BPM: 110. Length: 180 seconds\\\\n- Breakbeat track with complex percussion, intricate breakbeats, gritty synths, lush pads, and 808 bassline producing fresh, modern, futuristic, and rave-ready energy. BPM: 140. Length: 160 seconds\\\\n- Lounge Jazz 1960s smooth track with laid-back drums, piano chords, double bass, soft electric piano, subtle flute, and unique percussion creating beautiful, atmospheric, eclectic, retro, and chill vibes. BPM: 85. Length: 180 seconds\\\\n- Latin Jazz 1950s blissful track with laid-back Latin drums, euphoric piano chords, double bass, orchestral accompaniment, acoustic guitar, and vibraphone producing nostalgic, beautiful, atmospheric, cinematic, and chill mood. BPM: 95. Length: 180 seconds\\\\n- Acid Jazz 1970s summertime track with smooth electric piano, trippy synth leads, laid-back vintage drum kit, fuzzy electric bass, and uplifting violin producing retro, psychedelic, jazzy, relaxing energy. BPM: 100. Length: 180 seconds\\\\n- Progressive Soul 1970s track with feel-good piano, psychedelic organ, groovy vintage drum kit with percussion, fuzzy electric bass, and synth strings producing retro, raw, soulful, joyous atmosphere. BPM: 90. Length: 180 seconds\\\\n- Discotheque 1970s French-inspired track with sultry piano, psychedelic guitars, groovy drum kit, fuzzy electric bass, and melancholic organ producing retro, raw, laid-back, and relaxing mood. BPM: 105. Length: 180 seconds\\\\n- Soul Jazz 1970s track with expressive saxophone, smooth piano, groovy drum kit, rhythmic upright bass, sweeping strings, and minimal vibraphone producing retro, raw, laid-back, and epic energy. BPM: 95. Length: 180 seconds\\\\n- Vintage R&B 1970s live studio track with subtle brass, smooth piano, sweeping strings, and minimal drums producing retro, beautiful, uplifting, nostalgic mood. BPM: 85. Length: 180 seconds\\\\n- 50s Pop track with Latin influence, string section, bold brass, vibraphone, acoustic guitar, flute, ethnic percussion, and brushed drums creating sexy, epic, vintage, retro, melancholic, jazzy, dramatic energy. BPM: 100. Length: 180 seconds\\\\n- A piece of calm, quiet, mellow, serene music perfect for a peaceful film score, featuring soft modulating piano, ambient sfx and foley, beautiful vibraphone, and subtle synthesizer drones. The mood is cinematic, thoughtful, serene and nostalgic. BPM: 55. Length: 300 seconds\\\",\\n  \\\"Instrument\\\": \\\"You are a music metadata expert. Given an instrument, generate a descriptive prompt for a generative audio model.\\\\n\\\\n1. Identify the instrument.\\\\n2. Add playing style or technique.\\\\n3. Include details about material, timbre, or texture.\\\\n4. Add musical style or mood. Specify the genre, context, or emotional character.\\\\n5. Add spatial or production qualities.\\\\n6. Specify BPM: Always include a BPM appropriate to the style and context.\\\\n7. Specify length: Provide an integer in seconds (6–20 s for loops, 20–180 s for stems).\\\\n\\\\nExamples:\\\\n- Synth arpeggio loop with bright detuned oscillators. BPM: 120. Length: 8 seconds\\\\n- Chord stab loop with sharp percussive attack. BPM: 90. Length: 6 seconds\\\\n- Guitar muted strum loop with tight rhythmic feel. BPM: 100. Length: 8 seconds\\\\n- Pluck sequence loop with bright resonant tone. BPM: 128. Length: 10 seconds\\\\n- Marimba and vibraphone percussive loop with resonant wooden and metallic tones. BPM: 110. Length: 12 seconds\\\\n- Drum loop with deep muffled kick on beat one, snappy rimshot snare on beats two and four with rolling ghost note fills, and tight closed hi-hats with subtle open accents. BPM: 85. Length: 10 seconds\\\\n- Drum groove loop with brushed snare swinging on the ride, soft feathered kick on downbeats, and light closed hi-hat taps on the upbeats. BPM: 130. Length: 12 seconds\\\\n- Kick and hi-hat loop with four-on-the-floor punchy kick, tight closed hi-hats on every eighth note, and a sharp dry snare on beats two and four. BPM: 130. Length: 15 seconds\\\\n- Vinyl crackle drum loop with warm low-pass filtered kick, dusty snare with tape saturation, and shuffled closed hi-hats with subtle vinyl crackle ambiance. BPM: 80. Length: 10 seconds\\\\n- Ambient pad loop with evolving texture. BPM: 80. Length: 12 seconds\\\\n- Melodic synth bass groove loop with pumping sidechain feel. BPM: 122. Length: 10 seconds\\\\n- Melodic Bass slap and pop rhythm loop. BPM: 100. Length: 8 seconds\\\\n- Acoustic bass walking line loop with natural wooden resonance. BPM: 120. Length: 12 seconds\\\\n- String pizzicato motif loop, suspenseful, with tight string texture. BPM: 90. Length: 8 seconds\\\\n- Brass staccato riff loop with sharp bright attack. BPM: 130. Length: 10 seconds\\\\n- Flute airy melodic loop with wooden headjoint resonance. BPM: 100. Length: 6 seconds\\\\n- Pan flute ambient loop with breathy timbre. BPM: 75. Length: 8 seconds\\\\n- Clarinet riff loop with warm smooth reed tone. BPM: 120. Length: 10 seconds\\\\n- Oboe motif loop, orchestral, with rich double reed resonance. BPM: 80. Length: 8 seconds\\\\n- Recorder Renaissance motif loop with soft wooden timbre. BPM: 100. Length: 6 seconds\\\\n- Electric sitar riff loop with buzzing resonant tone. BPM: 90. Length: 10 seconds\\\\n- Koto plucked motif loop with resonant wooden strings. BPM: 90. Length: 8 seconds\\\\n- Shamisen folk melody loop with percussive twang. BPM: 100. Length: 8 seconds\\\\n- Banjo fingerpicking loop with metallic string resonance. BPM: 110. Length: 10 seconds\\\\n- Mandolin tremolo loop with crisp wooden body tone. BPM: 120. Length: 10 seconds\\\\n- Acoustic guitar chord vamp loop with natural room resonance. BPM: 110. Length: 12 seconds\\\\n- Nylon string guitar arpeggio loop with warm, soft timbre. BPM: 90. Length: 15 seconds\\\\n- Electric guitar riff loop with driven distorted tone. BPM: 130. Length: 10 seconds\\\\n- Slide guitar melody loop with warm resonant glide. BPM: 100. Length: 12 seconds\\\\n- Steel guitar slide loop with bright pedal steel tone. BPM: 95. Length: 12 seconds\\\\n- Harpsichord arpeggio loop with crisp plucked attack. BPM: 120. Length: 10 seconds\\\\n- Rhodes chord vamp loop with warm electric piano tone. BPM: 100. Length: 12 seconds\\\\n- Clavinet funky rhythm loop. BPM: 105. Length: 10 seconds\\\\n- Organ chord vamp loop with full drawbar warmth. BPM: 90. Length: 12 seconds\\\\n- Drum loop with booming 808 kick on beat one, crisp snare on beat three, and rapid triplet hi-hat rolls with open hat accents for aggressive high-energy feel. BPM: 140. Length: 8 seconds\\\\n- Breakbeat drum loop with chopped Amen-style snare flurries, driving kick on the one, fast sixteenth-note closed hi-hats, and syncopated open hat accents. BPM: 170. Length: 10 seconds\\\\n- Glitch percussion loop with stuttered kick transients, randomised snare hits processed with bit-crushing, and erratic hi-hat patterns with pitch-shifted metallic ticks. BPM: 120. Length: 12 seconds\\\\n- Metallic hits loop with distorted kick impacts, processed metal-plate snare slams, and grinding hi-hat noise bursts for aggressive mechanical texture. BPM: 120. Length: 10 seconds\\\\n- Timpani hits loop, cinematic, with deep resonant kick-like timpani strikes on beat one, rolling snare-style timpani fills, and no hi-hats for a grand orchestral feel. BPM: 70. Length: 8 seconds\\\\n- Snare roll loop, dramatic, with accelerating snare drum rolls building from soft to crashing, deep supporting kick pulses, and no hi-hats for maximum impact. BPM: 100. Length: 8 seconds\\\\n- Accordion motif loop with bright reedy bellows tone. BPM: 100. Length: 10 seconds\\\\n- Harmonica blues riff loop with expressive reed timbre. BPM: 90. Length: 10 seconds\\\\n- Trombone riff loop with warm sliding brass tone. BPM: 120. Length: 10 seconds\\\\n- French horn melodic loop, cinematic. BPM: 80. Length: 12 seconds\\\\n- Soprano sax ballad loop. BPM: 70. Length: 12 seconds\\\\n- Alto sax bebop riff loop. BPM: 200. Length: 10 seconds\\\\n- Electric violin melodic loop with reverb. BPM: 90. Length: 10 seconds\\\\n- String pad loop with cinematic texture. BPM: 70. Length: 15 seconds\\\\n- Granular synth evolving texture loop. BPM: 90. Length: 15 seconds\\\\n- Piano motif loop with soft felt hammer tone. BPM: 80. Length: 10 seconds\\\\n- Pad and synth loop with lush detuned shimmer. BPM: 85. Length: 12 seconds\\\\n- Synth lead loop with sidechain pumping compression. BPM: 128. Length: 10 seconds\\\\n- Analog synth bassline loop with deep warm low-end. BPM: 122. Length: 12 seconds\\\\n- FM synth lead motif loop with bright metallic shimmer. BPM: 110. Length: 10 seconds\\\\n- Bass groove loop with tight rhythmic two-bar pattern. BPM: 100. Length: 16 seconds\\\\n- Acoustic guitar fingerstyle motif loop with warm wood resonance. BPM: 90. Length: 45 seconds\\\\n- Sombre acoustic guitar motif loop with cavernous reverb, delicate fingerpicking, and expressive melancholic tone. BPM: 70. Length: 45 seconds\\\\n- Electric guitar rock riff motif loop. BPM: 130. Length: 40 seconds\\\\n- Vintage electric guitar motif loop, live-recorded in a vintage studio, with expressive and dynamic solo performance. BPM: 90. Length: 40 seconds\\\\n- Piano chord progression motif loop with rich harmonic movement. BPM: 120. Length: 60 seconds\\\\n- String ensemble cinematic motif loop with rich wooden resonance. BPM: 80. Length: 120 seconds\\\\n- Brass ensemble cinematic motif loop with bright metallic timbre. BPM: 90. Length: 90 seconds\\\\n- Ethnic percussion ensemble motif loop with deep resonant djembe kick tones, slapped snare-like rim hits on congas, and layered shakers and bells providing hi-hat-like rhythmic texture with polyrhythmic patterns. BPM: 100. Length: 90 seconds\\\\n- Synth ambient motif loop with evolving textures. BPM: 80. Length: 180 seconds\\\\n- Motif loop with warm dusty vinyl crackle and tape saturation. BPM: 80. Length: 60 seconds\\\\n- Synth lead and bass motif loop with bright punchy energy. BPM: 128. Length: 90 seconds\\\\n- Funk band motif loop: bass, drums, guitar. BPM: 100. Length: 90 seconds\\\\n- Ethnic flute motif for cinematic use. BPM: 80. Length: 30 seconds\\\\n- Steel drum melodic motif loop with bright metallic resonance. BPM: 110. Length: 20 seconds\\\\n- Marimba percussive motif loop with resonant wooden tone. BPM: 100. Length: 20 seconds\\\\n- Vibraphone melodic motif loop with metallic shimmer. BPM: 90. Length: 25 seconds\\\\n- Piano cinematic motif loop with resonant wooden tone. BPM: 80. Length: 30 seconds\\\\n- Violin expressive cinematic motif loop with rich wooden resonance. BPM: 75. Length: 25 seconds\\\\n- Cello expressive motif loop with deep wooden resonance. BPM: 70. Length: 30 seconds\\\\n- Trumpet expressive motif loop with brassy overtones. BPM: 100. Length: 25 seconds\\\\n- Sax expressive motif loop with warm reed timbre. BPM: 95. Length: 25 seconds\\\\n- Ethnic drum ensemble motif loop with booming natural-skin bass drum kicks, sharp hand-slap snare accents on djembes and talking drums, and layered wooden and metal percussion providing rhythmic hi-hat-like patterns. BPM: 95. Length: 30 seconds\\\\n- Ambient drone motif loop. BPM: 60. Length: 180 seconds\\\\n- Orchestral tension motif loop. BPM: 90. Length: 150 seconds\\\\n- Electronic track motif loop with drums, bass, synth. BPM: 128. Length: 180 seconds\\\",\\n  \\\"SFX\\\": \\\"You are a professional sound design expert. Convert the user's input into a precise, vivid sound effects description suitable for generative audio models.\\\\n\\\\nDescribe clearly:\\\\n- Sound source\\\\n- Physical character (texture, timbre, material: metal, wood, glass, concrete, etc.)\\\\n- Spatial qualities (indoor/outdoor, cave/open field/underwater, dry/reverberant, close-up/distant, echoing/muffled)\\\\n- Temporal evolution (attack, decay, movement, transitions over time)\\\\n- Include motion or spatial movement if applicable (passing, approaching, stereo movement)\\\\n\\\\nAudio length rules:\\\\n- Very short sounds (impacts, clicks, gunshots): 1–3 seconds\\\\n- Medium actions (footsteps, object movement, transitions): 3–6 seconds\\\\n- Ambience / environments: 6–15 seconds\\\\n- Always append: Length: X seconds (integer only, no decimals).\\\\n\\\\nOutput constraints:\\\\n- Length: 1–2 dense sentences maximum\\\\n- Output ONLY the final rewritten prompt\\\\n- No explanations, no formatting, no quotes\\\\n- Use concise but dense technical language\\\\n- Focus strictly on sound effects or ambience\\\\n- Always append: Length: X seconds (integer only, no decimals).\\\\n\\\\nQuality guidelines:\\\\n- Be specific and avoid vague terms\\\\n- Prioritize clarity and realism\\\\n- Combine elements into one coherent scene\\\\n- Avoid redundancy\\\\n\\\\nExamples:\\\\n- Heavy rain hitting a metal roof during a thunderstorm, distant thunder rumbles, stereo, realistic ambience. Length: 45 seconds\\\\n- Quiet forest at dawn with birds chirping, soft wind through leaves, distant stream flowing. Length: 60 seconds\\\\n- Busy city street at night, cars passing, muffled conversations, occasional horn, urban ambience. Length: 50 seconds\\\\n- Ocean waves crashing against rocky cliffs, strong wind, dramatic and cinematic. Length: 70 seconds\\\\n- Wooden door creaking open slowly in an old house, echoing interior, eerie tone. Length: 3 seconds\\\\n- Glass bottle shattering on concrete, sharp impact, scattered fragments. Length: 2 seconds\\\\n- Footsteps on gravel, steady walking pace, close perspective. Length: 8 seconds\\\\n- Typing rapidly on a mechanical keyboard, crisp tactile clicks. Length: 5 seconds\\\\n- Punch impact with deep bass hit, cinematic trailer style. Length: 2 seconds\\\\n- Car speeding past at high velocity, doppler effect, realistic whoosh. Length: 3 seconds\\\\n- Object falling from height and hitting ground with a heavy thud. Length: 2 seconds\\\\n- Sword swing whooshing through air, fast motion, clean metallic tone. Length: 2 seconds\\\\n- Futuristic laser blast, clean energy pulse, high-tech sound design. Length: 1 seconds\\\\n- Spaceship engine humming, low frequency rumble, interior perspective. Length: 90 seconds\\\\n- Magical spell casting, shimmering particles, rising tonal energy. Length: 8 seconds\\\\n- Teleportation effect, glitchy digital distortion with a soft whoosh. Length: 5 seconds\\\\n- Dark eerie drone with distant whispers, creepy, slow build tension. Length: 120 seconds\\\\n- Sudden horror jump scare sting, sharp violin hit, cinematic. Length: 1 second\\\\n- Metal scraping slowly in a dark tunnel, echoing and ominous. Length: 20 seconds\\\\n- Explosion with debris scattering, deep bass, cinematic realism. Length: 4 seconds\\\\n- Building collapsing, rumbling concrete, dust and debris falling. Length: 25 seconds\\\\n- Fire crackling intensely, wood burning, close-up detail. Length: 80 seconds\\\\n- Gunshot in a large empty warehouse, loud echo decay. Length: 2 seconds\\\\n- Retro arcade coin insert sound, 8-bit style. Length: 1 second\\\\n- Level up chime, bright, rewarding, fantasy RPG style. Length: 2 seconds\\\\n- Error buzzer, short, digital, UI feedback. Length: 1 second\\\\n- Menu navigation clicks, soft futuristic interface sounds. Length: 3 seconds\\\\n- Layered soundscape: rain, thunder, footsteps, and distant sirens all blending naturally. Length: 90 seconds\\\\n- Rapid sequence of three impacts: metal hit, glass break, wood crack, spaced evenly. Length: 4 seconds\\\\n- Sound moving from left to right stereo field: passing motorcycle. Length: 5 seconds\\\\n- Close vs far perspective transition: footsteps approaching then fading away. Length: 6 seconds\\\\n- Tape stop sub drop, a massive sub-bass note that mimics a vinyl record or tape machine being turned off, the pitch and speed drop simultaneously, causing the high-end harmonics to smear and thicken as the sound grinds to a halt at a sub-sonic frequency. Length: 11 seconds\\\\n- Gravel and leaves footsteps, the sound of a hard boot stepping onto dry leaves or gravel, crisp and natural with detailed texture. Length: 11 seconds\\\\n- Ghostship moan, a massive, deep wooden groan with a low-frequency moan, like heavy timber under immense structural tension, swaying slowly, processed with long, dark wooden room reverb for a sense of scale. Length: 11 seconds\\\\n- Bicycle chain, a continuous metallic whirring sound of a chain moving over sprockets, with individual teeth catching the links, processed with resonant band-pass filter to emphasize metallic singing. Length: 11 seconds\\\\n- Warp drive, a sound that starts with a massive suck-back of ambient noise, followed by a supersonic crack and high-pitched zing that disappears into the distance, giving the sense of stretching space-time. Length: 11 seconds\\\\n- Ice cubes, high-pitched musical clinking of hard ice hitting a thin glass, bright resonant ring with subtle liquid sloshing around the edges. Length: 11 seconds\\\\n- Paper shuffle, the sound of a thick stack of heavy bond paper being squared up on a desk, dry papery thud with a quick fanning sound as air moves between the pages. Length: 11 seconds\\\\n- Drawer slam, a blunt, powerful thud made by slamming a wooden desk drawer shut, pronounced low-mid body, slightly distorted for aggressive character. Length: 3 seconds\\\",\\n  \\\"One-shot\\\": \\\"You are a music metadata expert. Given an instrument or sound, generate a descriptive prompt for a short, isolated one-shot audio sample for music production.\\\\n\\\\n1. Identify the instrument or sound source.\\\\n2. Describe the playing technique or hit type (e.g., pluck, slam, tap, stab).\\\\n3. Include details about material, timbre, or texture.\\\\n4. Add spatial or production qualities (dry/wet, room, close-mic).\\\\n5. Specify length: short integer in seconds (1–11 s).\\\\n\\\\nExamples:\\\\n- Piano key hit with bright percussive attack and resonant wooden body. Length: 2 seconds\\\\n- Kick drum punchy low-end hit with warm skin resonance. Length: 2 seconds\\\\n- Snare drum rimshot accent with crisp snare wires. Length: 2 seconds\\\\n- Acoustic guitar fingerstyle note with warm spruce tone. Length: 3 seconds\\\\n- Bass pluck with jazzy tone and resonant wooden body. Length: 3 seconds\\\\n- Electric guitar power chord with distortion. Length: 3 seconds\\\\n- Metallic glitch percussion hit with sharp metallic texture. Length: 2 seconds\\\\n- Tabla resonant tone hit with natural skin timbre. Length: 2 seconds\\\\n- Djembe slap accent with dry wooden resonance. Length: 2 seconds\\\\n- Synth stab with reverb tail. Length: 3 seconds\\\\n- Violin expressive note with vibrato and rich wooden resonance. Length: 3 seconds\\\\n- Cello legato note, cinematic, with warm resonant body. Length: 3 seconds\\\\n- Trumpet bright accent with slightly brassy overtones. Length: 2 seconds\\\\n- Melodic saxophone jazz riff with smooth reed timbre and a slight vibrato bend. Length: 3 seconds\\\\n- Harp pluck with airy tone and resonant strings. Length: 2 seconds\\\\n- Glockenspiel bell-like note with bright metallic clarity. Length: 2 seconds\\\\n- Metallic clang sound design hit. Length: 2 seconds\\\\n- Granular texture hit. Length: 3 seconds\\\\n- Reversed piano hit. Length: 2 seconds\\\\n- Synth riser effect. Length: 6 seconds\\\\n- Percussion impact hit. Length: 2 seconds\\\\n- Cinematic hit. Length: 2 seconds\\\\n- Dry clap, a crisp, natural single hand clap recorded in a dead room with an extremely sharp transient and no room reflections. Length: 1 second\\\\n- Studio hat, a classic, natural recording of 14-inch hi-hats played tightly closed, zero ring, very fast decay. Length: 1 second\\\\n- Disco open hat, bright 14-inch open hi-hat with long, shimmering decay, perfect for disco or dance grooves. Length: 1 second\\\\n- Pillow kick, acoustic kick drum muffled with a heavy blanket, producing a short, dry \\\\\\\"thump\\\\\\\" with almost zero resonance. Length: 1 second\\\\n- Short 808, punchy 808 kick with sharp, distorted transient and fast-decaying sub-tail. Length: 1 second\\\\n- Egg shaker, classic plastic egg shaker recorded with a small-diaphragm condenser mic, producing a light, consistent \\\\\\\"tick\\\\\\\" with very short sustain. Length: 1 second\\\\n- African drums, dynamic African drums and percussion ensemble with natural acoustic textures. Length: 3 seconds\\\\n- Latin drums, dynamic Latin drums and percussion ensemble featuring authentic rhythmic patterns. Length: 3 seconds\\\\n- String quartet, euphoric string quartet with dynamic and emotional playing, full of expressive harmonies and movement. Length: 3 seconds\\\\n- Piano, nostalgic, atmospheric piano piece with dynamic and emotional performance, intimate and resonant. Length: 3 seconds\\\\n- Analogue drift pad, warm polyphonic pad with three detuned oscillators (saw + triangle), subtle pitch drift, and lush bucket-brigade chorus for wide, nostalgic stereo image. Length: 11 seconds\\\\n- Phase distortion bass, Casio CZ-style phase-distorted sine wave warped into a jagged sawtooth for retro synth bass tone. Length: 11 seconds\\\\n- Vibrato saxophone, bright lyrical alto sax with fast fluttery vibrato, reedy vintage tone, captured with ribbon mic for warm nostalgic sound. Length: 11 seconds\\\\n- Lofi upright bass, upright bass recorded with ribbon mic in a wooden room, natural air with slightly boxy resonance, tape-saturated for dusty 1950s jazz feel. Length: 2 seconds\\\"\\n}";

        internal static string RepromptChainJson(string category, string qwenClip, CultureInfo inv)
        {
            return
                "  \"sg52_43\": {\n" +
                "    \"inputs\": {\n" +
                "      \"choice\": \"" + category + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"CustomCombo\"\n" +
                "  },\n" +
                "  \"sg52_49\": {\n" +
                "    \"inputs\": {\n" +
                "      \"json_string\": \"" + RepromptTemplateJson + "\",\n" +
                "      \"key\": [\"sg52_43\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"JsonExtractString\"\n" +
                "  },\n" +
                "  \"sg52_38\": {\n" +
                "    \"inputs\": {\n" +
                "      \"string\": \"" + PromptTemplate38 + "\",\n" +
                "      \"find\": \"SYSTEM_PROMPTS\",\n" +
                "      \"replace\": [\"sg52_49\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"StringReplace\"\n" +
                "  },\n" +
                "  \"sg52_39\": {\n" +
                "    \"inputs\": {\n" +
                "      \"string\": [\"sg52_38\", 0],\n" +
                "      \"find\": \"USER_INPUT\",\n" +
                "      \"replace\": [\"sg52_31\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"StringReplace\"\n" +
                "  },\n" +
                "  \"sg52_40\": {\n" +
                "    \"inputs\": {\n" +
                "      \"string\": [\"sg52_39\", 0],\n" +
                "      \"find\": \"AUDIO_LENGTH\"\n" +
                "    },\n" +
                "    \"class_type\": \"StringReplace\"\n" +
                "  },\n" +
                "  \"sg52_29\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip_name\": \"" + qwenClip + "\",\n" +
                "      \"type\": \"stable_diffusion\",\n" +
                "      \"device\": \"default\"\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPLoader\"\n" +
                "  },\n" +
                "  \"sg52_28\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip\": [\"sg52_29\", 0],\n" +
                "      \"prompt\": [\"sg52_40\", 0],\n" +
                "      \"max_length\": " + REPROMPT_MAX_LENGTH.ToString(inv) + ",\n" +
                "      \"sampling_mode\": \"on\",\n" +
                "      \"sampling_mode.temperature\": " + REPROMPT_TEMPERATURE.ToString(inv) + ",\n" +
                "      \"sampling_mode.top_k\": " + REPROMPT_TOP_K.ToString(inv) + ",\n" +
                "      \"sampling_mode.top_p\": " + REPROMPT_TOP_P.ToString(inv) + ",\n" +
                "      \"sampling_mode.min_p\": " + REPROMPT_MIN_P.ToString(inv) + ",\n" +
                "      \"sampling_mode.repetition_penalty\": " + REPROMPT_REPETITION_PENALTY.ToString(inv) + ",\n" +
                "      \"sampling_mode.seed\": 0,\n" +
                "      \"sampling_mode.presence_penalty\": 0,\n" +
                "      \"thinking\": false,\n" +
                "      \"use_default_template\": true\n" +
                "    },\n" +
                "    \"class_type\": \"TextGenerate\"\n" +
                "  }";
        }
    }

    /// <summary>
    /// Built-in frozen copy of FASTAUDIOTGEN_5mil.json (14 UI nodes, CSV batch
    /// SFX) as an API-format builder. Prompt + duration ride CSV rows
    /// (UO_Captions.csv) selected by batchIndex; pass prompt / durationSec to
    /// override a single shot (bypassed CSV readers are pruned, pure plumbing
    /// nodes kept vestigially). Saver is SaveAudioAdvanced pinned to
    /// saverPrefix; the int-suffixed output-dir chain (71/72) is kept for shape.
    /// Emits 22 nodes (29 with reprompt), minus 2 per prompt override and 3 per
    /// duration override.
    /// </summary>
    public static class StableAudio5MilWorkflow
    {
        // Model filenames frozen from the outer subgraph-node widgets.
        public const string DEFAULT_CKPT = "stable_audio_3_medium.safetensors";
        public const string DEFAULT_SA_CLIP = "t5gemma_b_b_ul2.safetensors";
        public const string DEFAULT_QWEN_CLIP = "minimax_music3_text_encoder_pruned_int8_convrot.safetensors";
        // Literal saver prefix frozen from node 57 (pinned via saverPrefix).
        public const string DEFAULT_SAVER_PREFIX = "audio/stable_audio_3";
        public const string DEFAULT_CATEGORY = "SFX";
        public const double DEFAULT_DURATION = 10;
        public const int DEFAULT_STEPS = 8;
        // Frozen CSV/batch inputs (absolute paths from the template machine).
        public const string DEFAULT_CSV_PROMPTS = "C:\\Users\\meesa\\source\\repos\\MeesaMultisMaker\\MeesaMultisMaker\\Captions\\UO_Captions.csv";
        public const string DEFAULT_CSV_SECONDS = "C:\\Users\\meesa\\source\\repos\\MeesaMultisMaker\\MeesaMultisMaker\\Captions\\UO_Captions.csv";
        public const string DEFAULT_OUTPUT_DIR = "D:/ComfyUIPortable/ComfyUI/output/audio/";
        public const int DEFAULT_BATCH_INDEX = 0;

        /// <summary>Node count of the emitted graph.</summary>
        public static int NodeCount(bool useReprompt, bool promptOverride, bool durationOverride)
        {
            return 22 + (useReprompt ? 7 : 0) - (promptOverride ? 2 : 0) - (durationOverride ? 3 : 0);
        }

        /// <summary>
        /// Build a 5Mil-batch API-format prompt. A null/empty prompt (or
        /// durationSec &lt;= 0) keeps the CSV row source; otherwise the value is
        /// stamped directly and the bypassed CSV readers are pruned.
        /// </summary>
        public static string CreateWorkflow(
            string prompt = null,
            string negativePrompt = "",
            double durationSec = 0,
            long seed = -1,
            int steps = DEFAULT_STEPS,
            bool useReprompt = false,
            string category = null,
            int batchIndex = DEFAULT_BATCH_INDEX,
            string csvPrompts = null,
            string csvSeconds = null,
            string saverPrefix = null,
            string ckptName = null,
            string saClip = null,
            string qwenClip = null)
        {
            bool promptOverride = !string.IsNullOrEmpty(prompt);
            bool durationOverride = durationSec > 0;
            if (durationOverride)
            {
                if (durationSec < 1) durationSec = 1;
                if (durationSec > 600) durationSec = 600;
            }
            if (seed < 0) seed = RandomSeed();
            if (steps < 1) steps = DEFAULT_STEPS;
            if (string.IsNullOrEmpty(category)) category = DEFAULT_CATEGORY;
            if (string.IsNullOrEmpty(csvPrompts)) csvPrompts = DEFAULT_CSV_PROMPTS;
            if (string.IsNullOrEmpty(csvSeconds)) csvSeconds = DEFAULT_CSV_SECONDS;
            if (string.IsNullOrEmpty(saverPrefix)) saverPrefix = DEFAULT_SAVER_PREFIX;
            if (string.IsNullOrWhiteSpace(ckptName)) ckptName = DEFAULT_CKPT;
            if (string.IsNullOrWhiteSpace(saClip)) saClip = DEFAULT_SA_CLIP;
            if (string.IsNullOrWhiteSpace(qwenClip)) qwenClip = DEFAULT_QWEN_CLIP;

            var inv = CultureInfo.InvariantCulture;
            string p = EscapeJson(prompt ?? string.Empty);
            string n = EscapeJson(negativePrompt ?? string.Empty);
            string c = EscapeJson(category);
            string d = durationSec.ToString("0.###", inv);
            string s = seed.ToString(inv);
            string t = steps.ToString(inv);
            string csv = EscapeJson(csvPrompts);
            string sec = EscapeJson(csvSeconds);
            string sv = EscapeJson(saverPrefix);
            string ck = EscapeJson(ckptName);
            string sa = EscapeJson(saClip);
            string qw = EscapeJson(qwenClip);

            string promptFeed = promptOverride ? "\"" + p + "\"" : "[\"62\", 0]";
            string durationFeed = durationOverride ? d : "[\"75\", 0]";
            string onTrue = useReprompt ? "[\"sg52_28\", 0]" : "[\"sg52_31\", 0]";

            string json =
                "{\n" +
                "  \"sg52_31\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": [\"60\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveStringMultiline\"\n" +
                "  },\n" +
                "  \"sg52_36\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": " + durationFeed + "\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveFloat\"\n" +
                "  },\n" +
                "  \"sg52_35\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": false\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveBoolean\"\n" +
                "  },\n" +
                "  \"sg52_26\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip_name\": \"" + sa + "\",\n" +
                "      \"type\": \"stable_audio\",\n" +
                "      \"device\": \"default\"\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPLoader\"\n" +
                "  },\n" +
                "  \"sg52_25\": {\n" +
                "    \"inputs\": {\n" +
                "      \"ckpt_name\": \"" + ck + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"CheckpointLoaderSimple\"\n" +
                "  },\n" +
                "  \"sg52_34\": {\n" +
                "    \"inputs\": {\n" +
                "      \"on_false\": [\"sg52_31\", 0],\n" +
                "      \"on_true\": " + onTrue + ",\n" +
                "      \"switch\": [\"sg52_35\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"ComfySwitchNode\"\n" +
                "  },\n" +
                "  \"sg52_6\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip\": [\"sg52_26\", 0],\n" +
                "      \"text\": [\"sg52_34\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPTextEncode\"\n" +
                "  },\n" +
                "  \"sg52_7\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip\": [\"sg52_26\", 0],\n" +
                "      \"text\": \"" + n + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPTextEncode\"\n" +
                "  },\n" +
                "  \"sg52_3\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"sg52_25\", 0],\n" +
                "      \"positive\": [\"sg52_6\", 0],\n" +
                "      \"negative\": [\"sg52_7\", 0],\n" +
                "      \"latent_image\": [\"sg52_11\", 0],\n" +
                "      \"seed\": " + s + ",\n" +
                "      \"steps\": " + t + ",\n" +
                "      \"cfg\": " + AudioVariantCore.SAMPLER_CFG.ToString(inv) + ",\n" +
                "      \"sampler_name\": \"" + AudioVariantCore.SAMPLER_NAME + "\",\n" +
                "      \"scheduler\": \"" + AudioVariantCore.SCHEDULER + "\",\n" +
                "      \"denoise\": " + AudioVariantCore.SAMPLER_DENOISE.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"KSampler\"\n" +
                "  },\n" +
                "  \"sg52_11\": {\n" +
                "    \"inputs\": {\n" +
                "      \"seconds\": [\"sg52_36\", 0],\n" +
                "      \"batch_size\": 1\n" +
                "    },\n" +
                "    \"class_type\": \"EmptyLatentAudio\"\n" +
                "  },\n" +
                "  \"sg52_12\": {\n" +
                "    \"inputs\": {\n" +
                "      \"samples\": [\"sg52_3\", 0],\n" +
                "      \"vae\": [\"sg52_25\", 2]\n" +
                "    },\n" +
                "    \"class_type\": \"VAEDecodeAudio\"\n" +
                "  }";

            if (useReprompt)
                json += ",\n" + AudioVariantCore.RepromptChainJson(c, qw, inv);

            // Terminal prompt widget (CSV-fed link or single-shot literal).
            json += ",\n" +
                "  \"60\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": " + promptFeed + "\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveString\"\n" +
                "  },\n" +
                "  \"70\": {\n" +
                "    \"inputs\": {\n" +
                "      \"seed\": " + batchIndex.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"SeedNode\"\n" +
                "  },\n" +
                "  \"71\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": [\"70\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"JarJarIntToString\"\n" +
                "  },\n" +
                "  \"72\": {\n" +
                "    \"inputs\": {\n" +
                "      \"string_a\": \"" + DEFAULT_OUTPUT_DIR + "\",\n" +
                "      \"string_b\": [\"71\", 0],\n" +
                "      \"delimiter\": \"\"\n" +
                "    },\n" +
                "    \"class_type\": \"StringConcatenate\"\n" +
                "  }";

            if (!promptOverride)
                json += ",\n" +
                "  \"62\": {\n" +
                "    \"inputs\": {\n" +
                "      \"list_in\": [\"67\", 0],\n" +
                "      \"index\": [\"70\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"JarJarListItem\"\n" +
                "  },\n" +
                "  \"67\": {\n" +
                "    \"inputs\": {\n" +
                "      \"file_path\": \"" + csv + "\",\n" +
                "      \"delimiter\": \",\",\n" +
                "      \"column\": 0,\n" +
                "      \"has_header\": false,\n" +
                "      \"skip_empty\": true\n" +
                "    },\n" +
                "    \"class_type\": \"JarJarCSVFileToList\"\n" +
                "  }";

            if (!durationOverride)
                json += ",\n" +
                "  \"74\": {\n" +
                "    \"inputs\": {\n" +
                "      \"list_in\": [\"73\", 0],\n" +
                "      \"index\": [\"70\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"JarJarListItem\"\n" +
                "  },\n" +
                "  \"73\": {\n" +
                "    \"inputs\": {\n" +
                "      \"file_path\": \"" + sec + "\",\n" +
                "      \"delimiter\": \",\",\n" +
                "      \"column\": 0,\n" +
                "      \"has_header\": false,\n" +
                "      \"skip_empty\": true\n" +
                "    },\n" +
                "    \"class_type\": \"JarJarCSVFileToList\"\n" +
                "  },\n" +
                "  \"75\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": [\"74\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"ComfyNumberConvert\"\n" +
                "  }";

            json += ",\n" +
                "  \"57\": {\n" +
                "    \"inputs\": {\n" +
                "      \"audio\": [\"sg52_12\", 0],\n" +
                "      \"filename_prefix\": \"" + sv + "\",\n" +
                "      \"audioUI\": null,\n" +
                "      \"format\": \"mp3\",\n" +
                "      \"format.quality\": \"V0\"\n" +
                "    },\n" +
                "    \"class_type\": \"SaveAudioAdvanced\"\n" +
                "  },\n" +
                "  \"mm_savewav\": {\n" +
                "    \"inputs\": {\n" +
                "      \"filename_prefix\": \"mm_audio\",\n" +
                "      \"audio\": [\"sg52_12\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"SaveAudio\"\n" +
                "  }\n" +
                "}";

            return json;
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
    /// Built-in frozen copy of FASTAUDIOTGEN_UO_MUSIC_REPLACE.json (16 UI nodes,
    /// UO music REPLACE batch) as an API-format builder. Captions gain the
    /// frozen "medieval, " style prefix (node 81); seconds ride UO_Music_Seconds.csv.
    /// The music-names CSV saver chain (72/83/82) is superseded by the saver pin
    /// and never emitted. The subgraph core carries the file's extra
    /// RepeatLatentBatch passthrough (sg52_85, amount 1). Emits 22 nodes (29 with
    /// reprompt), minus 3 per prompt override and 3 per duration override.
    /// </summary>
    public static class StableAudioUoMusicWorkflow
    {
        // Model filenames frozen from the outer subgraph-node widgets.
        public const string DEFAULT_CKPT = "stable_audio_3_medium.safetensors";
        public const string DEFAULT_SA_CLIP = "t5gemma_b_b_ul2.safetensors";
        public const string DEFAULT_QWEN_CLIP = "minimax_music3_text_encoder_pruned_int8_convrot.safetensors";
        // Literal saver prefix frozen from node 57 (pinned via saverPrefix).
        public const string DEFAULT_SAVER_PREFIX = "audio/stable_audio_3";
        public const string DEFAULT_CATEGORY = "Music";
        public const double DEFAULT_DURATION = 10;
        public const int DEFAULT_STEPS = 8;
        // Frozen CSV/batch inputs (absolute paths from the template machine).
        public const string DEFAULT_CSV_CAPTIONS = "C:\\Users\\meesa\\source\\repos\\MeesaMultisMaker\\MeesaMultisMaker\\Captions\\UO_Music_Captions.csv";
        public const string DEFAULT_CSV_SECONDS = "C:\\Users\\meesa\\source\\repos\\MeesaMultisMaker\\MeesaMultisMaker\\Captions\\UO_Music_Seconds.csv";
        public const int DEFAULT_BATCH_INDEX = 0;

        /// <summary>Node count of the emitted graph.</summary>
        public static int NodeCount(bool useReprompt, bool promptOverride, bool durationOverride)
        {
            return 22 + (useReprompt ? 7 : 0) - (promptOverride ? 3 : 0) - (durationOverride ? 3 : 0);
        }

        /// <summary>
        /// Build a UO-music REPLACE batch API-format prompt. A null/empty prompt
        /// (or durationSec &lt;= 0) keeps the CSV row source (captions gain the
        /// frozen "medieval, " prefix); otherwise the value is stamped directly
        /// and the bypassed CSV readers are pruned.
        /// </summary>
        public static string CreateWorkflow(
            string prompt = null,
            string negativePrompt = "",
            double durationSec = 0,
            long seed = -1,
            int steps = DEFAULT_STEPS,
            bool useReprompt = false,
            string category = null,
            int batchIndex = DEFAULT_BATCH_INDEX,
            string csvCaptions = null,
            string csvSeconds = null,
            string saverPrefix = null,
            string ckptName = null,
            string saClip = null,
            string qwenClip = null)
        {
            bool promptOverride = !string.IsNullOrEmpty(prompt);
            bool durationOverride = durationSec > 0;
            if (durationOverride)
            {
                if (durationSec < 1) durationSec = 1;
                if (durationSec > 600) durationSec = 600;
            }
            if (seed < 0) seed = RandomSeed();
            if (steps < 1) steps = DEFAULT_STEPS;
            if (string.IsNullOrEmpty(category)) category = DEFAULT_CATEGORY;
            if (string.IsNullOrEmpty(csvCaptions)) csvCaptions = DEFAULT_CSV_CAPTIONS;
            if (string.IsNullOrEmpty(csvSeconds)) csvSeconds = DEFAULT_CSV_SECONDS;
            if (string.IsNullOrEmpty(saverPrefix)) saverPrefix = DEFAULT_SAVER_PREFIX;
            if (string.IsNullOrWhiteSpace(ckptName)) ckptName = DEFAULT_CKPT;
            if (string.IsNullOrWhiteSpace(saClip)) saClip = DEFAULT_SA_CLIP;
            if (string.IsNullOrWhiteSpace(qwenClip)) qwenClip = DEFAULT_QWEN_CLIP;

            var inv = CultureInfo.InvariantCulture;
            string p = EscapeJson(prompt ?? string.Empty);
            string n = EscapeJson(negativePrompt ?? string.Empty);
            string c = EscapeJson(category);
            string d = durationSec.ToString("0.###", inv);
            string s = seed.ToString(inv);
            string t = steps.ToString(inv);
            string csv = EscapeJson(csvCaptions);
            string sec = EscapeJson(csvSeconds);
            string sv = EscapeJson(saverPrefix);
            string ck = EscapeJson(ckptName);
            string sa = EscapeJson(saClip);
            string qw = EscapeJson(qwenClip);

            string promptFeed = promptOverride ? "\"" + p + "\"" : "[\"81\", 0]";
            string durationFeed = durationOverride ? d : "[\"79\", 0]";
            string onTrue = useReprompt ? "[\"sg52_28\", 0]" : "[\"sg52_31\", 0]";

            string json =
                "{\n" +
                "  \"sg52_31\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": [\"60\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveStringMultiline\"\n" +
                "  },\n" +
                "  \"sg52_36\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": " + durationFeed + "\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveFloat\"\n" +
                "  },\n" +
                "  \"sg52_35\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": false\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveBoolean\"\n" +
                "  },\n" +
                "  \"sg52_26\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip_name\": \"" + sa + "\",\n" +
                "      \"type\": \"stable_audio\",\n" +
                "      \"device\": \"default\"\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPLoader\"\n" +
                "  },\n" +
                "  \"sg52_25\": {\n" +
                "    \"inputs\": {\n" +
                "      \"ckpt_name\": \"" + ck + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"CheckpointLoaderSimple\"\n" +
                "  },\n" +
                "  \"sg52_34\": {\n" +
                "    \"inputs\": {\n" +
                "      \"on_false\": [\"sg52_31\", 0],\n" +
                "      \"on_true\": " + onTrue + ",\n" +
                "      \"switch\": [\"sg52_35\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"ComfySwitchNode\"\n" +
                "  },\n" +
                "  \"sg52_6\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip\": [\"sg52_26\", 0],\n" +
                "      \"text\": [\"sg52_34\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPTextEncode\"\n" +
                "  },\n" +
                "  \"sg52_7\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip\": [\"sg52_26\", 0],\n" +
                "      \"text\": \"" + n + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPTextEncode\"\n" +
                "  },\n" +
                "  \"sg52_3\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"sg52_25\", 0],\n" +
                "      \"positive\": [\"sg52_6\", 0],\n" +
                "      \"negative\": [\"sg52_7\", 0],\n" +
                "      \"latent_image\": [\"sg52_85\", 0],\n" +
                "      \"seed\": " + s + ",\n" +
                "      \"steps\": " + t + ",\n" +
                "      \"cfg\": " + AudioVariantCore.SAMPLER_CFG.ToString(inv) + ",\n" +
                "      \"sampler_name\": \"" + AudioVariantCore.SAMPLER_NAME + "\",\n" +
                "      \"scheduler\": \"" + AudioVariantCore.SCHEDULER + "\",\n" +
                "      \"denoise\": " + AudioVariantCore.SAMPLER_DENOISE.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"KSampler\"\n" +
                "  },\n" +
                "  \"sg52_11\": {\n" +
                "    \"inputs\": {\n" +
                "      \"seconds\": [\"sg52_36\", 0],\n" +
                "      \"batch_size\": 1\n" +
                "    },\n" +
                "    \"class_type\": \"EmptyLatentAudio\"\n" +
                "  },\n" +
                "  \"sg52_85\": {\n" +
                "    \"inputs\": {\n" +
                "      \"samples\": [\"sg52_11\", 0],\n" +
                "      \"amount\": 1\n" +
                "    },\n" +
                "    \"class_type\": \"RepeatLatentBatch\"\n" +
                "  },\n" +
                "  \"sg52_12\": {\n" +
                "    \"inputs\": {\n" +
                "      \"samples\": [\"sg52_3\", 0],\n" +
                "      \"vae\": [\"sg52_25\", 2]\n" +
                "    },\n" +
                "    \"class_type\": \"VAEDecodeAudio\"\n" +
                "  }";

            if (useReprompt)
                json += ",\n" + AudioVariantCore.RepromptChainJson(c, qw, inv);

            // Terminal prompt widget (CSV-fed link or single-shot literal).
            json += ",\n" +
                "  \"60\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": " + promptFeed + "\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveString\"\n" +
                "  },\n" +
                "  \"70\": {\n" +
                "    \"inputs\": {\n" +
                "      \"seed\": " + batchIndex.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"SeedNode\"\n" +
                "  }";

            if (!promptOverride)
                json += ",\n" +
                "  \"81\": {\n" +
                "    \"inputs\": {\n" +
                "      \"string_a\": \"medieval, \",\n" +
                "      \"string_b\": [\"62\", 0],\n" +
                "      \"delimiter\": \"\"\n" +
                "    },\n" +
                "    \"class_type\": \"StringConcatenate\"\n" +
                "  },\n" +
                "  \"62\": {\n" +
                "    \"inputs\": {\n" +
                "      \"list_in\": [\"67\", 0],\n" +
                "      \"index\": [\"70\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"JarJarListItem\"\n" +
                "  },\n" +
                "  \"67\": {\n" +
                "    \"inputs\": {\n" +
                "      \"file_path\": \"" + csv + "\",\n" +
                "      \"delimiter\": \",\",\n" +
                "      \"column\": 0,\n" +
                "      \"has_header\": true,\n" +
                "      \"skip_empty\": true\n" +
                "    },\n" +
                "    \"class_type\": \"JarJarCSVFileToList\"\n" +
                "  }";

            if (!durationOverride)
                json += ",\n" +
                "  \"74\": {\n" +
                "    \"inputs\": {\n" +
                "      \"list_in\": [\"73\", 0],\n" +
                "      \"index\": [\"70\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"JarJarListItem\"\n" +
                "  },\n" +
                "  \"73\": {\n" +
                "    \"inputs\": {\n" +
                "      \"file_path\": \"" + sec + "\",\n" +
                "      \"delimiter\": \",\",\n" +
                "      \"column\": 0,\n" +
                "      \"has_header\": false,\n" +
                "      \"skip_empty\": true\n" +
                "    },\n" +
                "    \"class_type\": \"JarJarCSVFileToList\"\n" +
                "  },\n" +
                "  \"79\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": [\"74\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"ComfyNumberConvert\"\n" +
                "  }";

            json += ",\n" +
                "  \"57\": {\n" +
                "    \"inputs\": {\n" +
                "      \"audio\": [\"sg52_12\", 0],\n" +
                "      \"filename_prefix\": \"" + sv + "\",\n" +
                "      \"audioUI\": null,\n" +
                "      \"format\": \"mp3\",\n" +
                "      \"format.quality\": \"V0\"\n" +
                "    },\n" +
                "    \"class_type\": \"SaveAudioAdvanced\"\n" +
                "  },\n" +
                "  \"mm_savewav\": {\n" +
                "    \"inputs\": {\n" +
                "      \"filename_prefix\": \"mm_audio\",\n" +
                "      \"audio\": [\"sg52_12\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"SaveAudio\"\n" +
                "  }\n" +
                "}";

            return json;
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
    /// Built-in frozen copy of FASTAUDIOTGEN_UO_SFX_REPLACE.json (13 UI nodes,
    /// UO SFX REPLACE batch) as an API-format builder. Prompt + duration ride
    /// CSV rows (UO_Captions.csv / UO_Captions_Seconds.csv) selected by
    /// batchIndex; pass prompt / durationSec to override a single shot
    /// (bypassed CSV readers are pruned, pure plumbing nodes kept vestigially).
    /// Saver is SaveAudioAdvanced pinned to saverPrefix; the int-suffixed
    /// output-dir chain (71/72) is kept for shape. Emits 22 nodes (29 with
    /// reprompt), minus 2 per prompt override and 3 per duration override.
    /// </summary>
    public static class StableAudioUoSfxWorkflow
    {
        // Model filenames frozen from the outer subgraph-node widgets.
        public const string DEFAULT_CKPT = "stable_audio_3_medium.safetensors";
        public const string DEFAULT_SA_CLIP = "t5gemma_b_b_ul2.safetensors";
        public const string DEFAULT_QWEN_CLIP = "minimax_music3_text_encoder_pruned_int8_convrot.safetensors";
        // Literal saver prefix frozen from node 57 (pinned via saverPrefix).
        public const string DEFAULT_SAVER_PREFIX = "audio/stable_audio_3";
        public const string DEFAULT_CATEGORY = "SFX";
        public const double DEFAULT_DURATION = 10;
        public const int DEFAULT_STEPS = 8;
        // Frozen CSV/batch inputs (absolute paths from the template machine).
        public const string DEFAULT_CSV_PROMPTS = "C:\\Users\\meesa\\source\\repos\\MeesaMultisMaker\\MeesaMultisMaker\\Captions\\UO_Captions.csv";
        public const string DEFAULT_CSV_SECONDS = "C:\\Users\\meesa\\source\\repos\\MeesaMultisMaker\\MeesaMultisMaker\\Captions\\UO_Captions_Seconds.csv";
        public const string DEFAULT_OUTPUT_DIR = "D:/ComfyUIPortable/ComfyUI/output/audio/";
        public const int DEFAULT_BATCH_INDEX = 0;

        /// <summary>Node count of the emitted graph.</summary>
        public static int NodeCount(bool useReprompt, bool promptOverride, bool durationOverride)
        {
            return 22 + (useReprompt ? 7 : 0) - (promptOverride ? 2 : 0) - (durationOverride ? 3 : 0);
        }

        /// <summary>
        /// Build a UO-SFX REPLACE batch API-format prompt. A null/empty prompt
        /// (or durationSec &lt;= 0) keeps the CSV row source; otherwise the value
        /// is stamped directly and the bypassed CSV readers are pruned.
        /// </summary>
        public static string CreateWorkflow(
            string prompt = null,
            string negativePrompt = "",
            double durationSec = 0,
            long seed = -1,
            int steps = DEFAULT_STEPS,
            bool useReprompt = false,
            string category = null,
            int batchIndex = DEFAULT_BATCH_INDEX,
            string csvPrompts = null,
            string csvSeconds = null,
            string saverPrefix = null,
            string ckptName = null,
            string saClip = null,
            string qwenClip = null)
        {
            bool promptOverride = !string.IsNullOrEmpty(prompt);
            bool durationOverride = durationSec > 0;
            if (durationOverride)
            {
                if (durationSec < 1) durationSec = 1;
                if (durationSec > 600) durationSec = 600;
            }
            if (seed < 0) seed = RandomSeed();
            if (steps < 1) steps = DEFAULT_STEPS;
            if (string.IsNullOrEmpty(category)) category = DEFAULT_CATEGORY;
            if (string.IsNullOrEmpty(csvPrompts)) csvPrompts = DEFAULT_CSV_PROMPTS;
            if (string.IsNullOrEmpty(csvSeconds)) csvSeconds = DEFAULT_CSV_SECONDS;
            if (string.IsNullOrEmpty(saverPrefix)) saverPrefix = DEFAULT_SAVER_PREFIX;
            if (string.IsNullOrWhiteSpace(ckptName)) ckptName = DEFAULT_CKPT;
            if (string.IsNullOrWhiteSpace(saClip)) saClip = DEFAULT_SA_CLIP;
            if (string.IsNullOrWhiteSpace(qwenClip)) qwenClip = DEFAULT_QWEN_CLIP;

            var inv = CultureInfo.InvariantCulture;
            string p = EscapeJson(prompt ?? string.Empty);
            string n = EscapeJson(negativePrompt ?? string.Empty);
            string c = EscapeJson(category);
            string d = durationSec.ToString("0.###", inv);
            string s = seed.ToString(inv);
            string t = steps.ToString(inv);
            string csv = EscapeJson(csvPrompts);
            string sec = EscapeJson(csvSeconds);
            string sv = EscapeJson(saverPrefix);
            string ck = EscapeJson(ckptName);
            string sa = EscapeJson(saClip);
            string qw = EscapeJson(qwenClip);

            string promptFeed = promptOverride ? "\"" + p + "\"" : "[\"62\", 0]";
            string durationFeed = durationOverride ? d : "[\"79\", 0]";
            string onTrue = useReprompt ? "[\"sg52_28\", 0]" : "[\"sg52_31\", 0]";

            string json =
                "{\n" +
                "  \"sg52_31\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": [\"60\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveStringMultiline\"\n" +
                "  },\n" +
                "  \"sg52_36\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": " + durationFeed + "\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveFloat\"\n" +
                "  },\n" +
                "  \"sg52_35\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": false\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveBoolean\"\n" +
                "  },\n" +
                "  \"sg52_26\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip_name\": \"" + sa + "\",\n" +
                "      \"type\": \"stable_audio\",\n" +
                "      \"device\": \"default\"\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPLoader\"\n" +
                "  },\n" +
                "  \"sg52_25\": {\n" +
                "    \"inputs\": {\n" +
                "      \"ckpt_name\": \"" + ck + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"CheckpointLoaderSimple\"\n" +
                "  },\n" +
                "  \"sg52_34\": {\n" +
                "    \"inputs\": {\n" +
                "      \"on_false\": [\"sg52_31\", 0],\n" +
                "      \"on_true\": " + onTrue + ",\n" +
                "      \"switch\": [\"sg52_35\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"ComfySwitchNode\"\n" +
                "  },\n" +
                "  \"sg52_6\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip\": [\"sg52_26\", 0],\n" +
                "      \"text\": [\"sg52_34\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPTextEncode\"\n" +
                "  },\n" +
                "  \"sg52_7\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip\": [\"sg52_26\", 0],\n" +
                "      \"text\": \"" + n + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPTextEncode\"\n" +
                "  },\n" +
                "  \"sg52_3\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"sg52_25\", 0],\n" +
                "      \"positive\": [\"sg52_6\", 0],\n" +
                "      \"negative\": [\"sg52_7\", 0],\n" +
                "      \"latent_image\": [\"sg52_11\", 0],\n" +
                "      \"seed\": " + s + ",\n" +
                "      \"steps\": " + t + ",\n" +
                "      \"cfg\": " + AudioVariantCore.SAMPLER_CFG.ToString(inv) + ",\n" +
                "      \"sampler_name\": \"" + AudioVariantCore.SAMPLER_NAME + "\",\n" +
                "      \"scheduler\": \"" + AudioVariantCore.SCHEDULER + "\",\n" +
                "      \"denoise\": " + AudioVariantCore.SAMPLER_DENOISE.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"KSampler\"\n" +
                "  },\n" +
                "  \"sg52_11\": {\n" +
                "    \"inputs\": {\n" +
                "      \"seconds\": [\"sg52_36\", 0],\n" +
                "      \"batch_size\": 1\n" +
                "    },\n" +
                "    \"class_type\": \"EmptyLatentAudio\"\n" +
                "  },\n" +
                "  \"sg52_12\": {\n" +
                "    \"inputs\": {\n" +
                "      \"samples\": [\"sg52_3\", 0],\n" +
                "      \"vae\": [\"sg52_25\", 2]\n" +
                "    },\n" +
                "    \"class_type\": \"VAEDecodeAudio\"\n" +
                "  }";

            if (useReprompt)
                json += ",\n" + AudioVariantCore.RepromptChainJson(c, qw, inv);

            // Terminal prompt widget (CSV-fed link or single-shot literal).
            json += ",\n" +
                "  \"60\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": " + promptFeed + "\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveString\"\n" +
                "  },\n" +
                "  \"70\": {\n" +
                "    \"inputs\": {\n" +
                "      \"seed\": " + batchIndex.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"SeedNode\"\n" +
                "  },\n" +
                "  \"71\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": [\"70\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"JarJarIntToString\"\n" +
                "  },\n" +
                "  \"72\": {\n" +
                "    \"inputs\": {\n" +
                "      \"string_a\": \"" + DEFAULT_OUTPUT_DIR + "\",\n" +
                "      \"string_b\": [\"71\", 0],\n" +
                "      \"delimiter\": \"\"\n" +
                "    },\n" +
                "    \"class_type\": \"StringConcatenate\"\n" +
                "  }";

            if (!promptOverride)
                json += ",\n" +
                "  \"62\": {\n" +
                "    \"inputs\": {\n" +
                "      \"list_in\": [\"67\", 0],\n" +
                "      \"index\": [\"70\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"JarJarListItem\"\n" +
                "  },\n" +
                "  \"67\": {\n" +
                "    \"inputs\": {\n" +
                "      \"file_path\": \"" + csv + "\",\n" +
                "      \"delimiter\": \",\",\n" +
                "      \"column\": 0,\n" +
                "      \"has_header\": false,\n" +
                "      \"skip_empty\": true\n" +
                "    },\n" +
                "    \"class_type\": \"JarJarCSVFileToList\"\n" +
                "  }";

            if (!durationOverride)
                json += ",\n" +
                "  \"74\": {\n" +
                "    \"inputs\": {\n" +
                "      \"list_in\": [\"73\", 0],\n" +
                "      \"index\": [\"70\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"JarJarListItem\"\n" +
                "  },\n" +
                "  \"73\": {\n" +
                "    \"inputs\": {\n" +
                "      \"file_path\": \"" + sec + "\",\n" +
                "      \"delimiter\": \",\",\n" +
                "      \"column\": 0,\n" +
                "      \"has_header\": false,\n" +
                "      \"skip_empty\": true\n" +
                "    },\n" +
                "    \"class_type\": \"JarJarCSVFileToList\"\n" +
                "  },\n" +
                "  \"79\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": [\"74\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"ComfyNumberConvert\"\n" +
                "  }";

            json += ",\n" +
                "  \"57\": {\n" +
                "    \"inputs\": {\n" +
                "      \"audio\": [\"sg52_12\", 0],\n" +
                "      \"filename_prefix\": \"" + sv + "\",\n" +
                "      \"audioUI\": null,\n" +
                "      \"format\": \"mp3\",\n" +
                "      \"format.quality\": \"V0\"\n" +
                "    },\n" +
                "    \"class_type\": \"SaveAudioAdvanced\"\n" +
                "  },\n" +
                "  \"mm_savewav\": {\n" +
                "    \"inputs\": {\n" +
                "      \"filename_prefix\": \"mm_audio\",\n" +
                "      \"audio\": [\"sg52_12\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"SaveAudio\"\n" +
                "  }\n" +
                "}";

            return json;
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
    /// Built-in frozen copy of MeesaJarJar_FastAudioFX_FoleyArtist_ComfyUI.json
    /// (7 UI nodes) as an API-format builder. The prompt rides the inline
    /// JarJarTextList batch (5 ant foley lines) selected by batchIndex and feeds
    /// both the synth and the space/comma saver sanitizers (58/71); pass prompt
    /// to replace the block with a single line (batchIndex forced to 0).
    /// Duration is always direct (default 3s). Emits 18 nodes (25 with reprompt).
    /// </summary>
    public static class FoleyArtistWorkflow
    {
        // Model filenames frozen from the outer subgraph-node widgets.
        public const string DEFAULT_CKPT = "stable_audio_3_medium.safetensors";
        public const string DEFAULT_SA_CLIP = "t5gemma_b_b_ul2.safetensors";
        public const string DEFAULT_QWEN_CLIP = "qwen3.5_2b_bf16.safetensors";
        // Literal saver prefix frozen from node 57 (pinned via saverPrefix).
        public const string DEFAULT_SAVER_PREFIX = "audio/stable_audio_3";
        public const string DEFAULT_CATEGORY = "SFX";
        public const double DEFAULT_DURATION = 3;
        public const int DEFAULT_STEPS = 8;
        // Frozen text batch (node 69) and row selector default.
        public const string DEFAULT_TEXT_BLOCK = "giant ant chittering softly, layered clicking, antennae tapping\ngiant ant attacking, mandibles snapping shut with aggressive clicking chatter, sharp bite\ngiant ant legs skittering rapidly across a stone floor, tapping and scratching\ngiant ant being hurt, short high-pitched chitter, chitin crunching\ngiant ant dying, frantic chittering fading out, legs twitching, final crunchy collapse";
        public const int DEFAULT_BATCH_INDEX = 0;

        /// <summary>Node count of the emitted graph.</summary>
        public static int NodeCount(bool useReprompt)
        {
            return 18 + (useReprompt ? 7 : 0);
        }

        /// <summary>
        /// Build a FoleyArtist batch API-format prompt. A null/empty prompt keeps
        /// the text block (textBlock ?? frozen) with batchIndex row selection; a
        /// non-empty prompt replaces the block with that single line.
        /// </summary>
        public static string CreateWorkflow(
            string prompt = null,
            string negativePrompt = "",
            double durationSec = DEFAULT_DURATION,
            long seed = -1,
            int steps = DEFAULT_STEPS,
            bool useReprompt = false,
            string category = null,
            int batchIndex = DEFAULT_BATCH_INDEX,
            string textBlock = null,
            string saverPrefix = null,
            string ckptName = null,
            string saClip = null,
            string qwenClip = null)
        {
            bool promptOverride = !string.IsNullOrEmpty(prompt);
            if (durationSec < 1) durationSec = 1;
            if (durationSec > 600) durationSec = 600;
            if (seed < 0) seed = RandomSeed();
            if (steps < 1) steps = DEFAULT_STEPS;
            if (string.IsNullOrEmpty(category)) category = DEFAULT_CATEGORY;
            if (string.IsNullOrEmpty(textBlock)) textBlock = DEFAULT_TEXT_BLOCK;
            if (string.IsNullOrEmpty(saverPrefix)) saverPrefix = DEFAULT_SAVER_PREFIX;
            if (string.IsNullOrWhiteSpace(ckptName)) ckptName = DEFAULT_CKPT;
            if (string.IsNullOrWhiteSpace(saClip)) saClip = DEFAULT_SA_CLIP;
            if (string.IsNullOrWhiteSpace(qwenClip)) qwenClip = DEFAULT_QWEN_CLIP;

            // Single-shot override replaces the whole batch block (row 0).
            string block = promptOverride ? prompt : textBlock;
            int row = promptOverride ? 0 : batchIndex;

            var inv = CultureInfo.InvariantCulture;
            string b = EscapeJson(block);
            string n = EscapeJson(negativePrompt ?? string.Empty);
            string c = EscapeJson(category);
            string d = durationSec.ToString("0.###", inv);
            string s = seed.ToString(inv);
            string t = steps.ToString(inv);
            string sv = EscapeJson(saverPrefix);
            string ck = EscapeJson(ckptName);
            string sa = EscapeJson(saClip);
            string qw = EscapeJson(qwenClip);

            string onTrue = useReprompt ? "[\"sg52_28\", 0]" : "[\"sg52_31\", 0]";

            string json =
                "{\n" +
                "  \"sg52_31\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": [\"70\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveStringMultiline\"\n" +
                "  },\n" +
                "  \"sg52_36\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": " + d + "\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveFloat\"\n" +
                "  },\n" +
                "  \"sg52_35\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": false\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveBoolean\"\n" +
                "  },\n" +
                "  \"sg52_26\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip_name\": \"" + sa + "\",\n" +
                "      \"type\": \"stable_audio\",\n" +
                "      \"device\": \"default\"\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPLoader\"\n" +
                "  },\n" +
                "  \"sg52_25\": {\n" +
                "    \"inputs\": {\n" +
                "      \"ckpt_name\": \"" + ck + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"CheckpointLoaderSimple\"\n" +
                "  },\n" +
                "  \"sg52_34\": {\n" +
                "    \"inputs\": {\n" +
                "      \"on_false\": [\"sg52_31\", 0],\n" +
                "      \"on_true\": " + onTrue + ",\n" +
                "      \"switch\": [\"sg52_35\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"ComfySwitchNode\"\n" +
                "  },\n" +
                "  \"sg52_6\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip\": [\"sg52_26\", 0],\n" +
                "      \"text\": [\"sg52_34\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPTextEncode\"\n" +
                "  },\n" +
                "  \"sg52_7\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip\": [\"sg52_26\", 0],\n" +
                "      \"text\": \"" + n + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPTextEncode\"\n" +
                "  },\n" +
                "  \"sg52_3\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"sg52_25\", 0],\n" +
                "      \"positive\": [\"sg52_6\", 0],\n" +
                "      \"negative\": [\"sg52_7\", 0],\n" +
                "      \"latent_image\": [\"sg52_11\", 0],\n" +
                "      \"seed\": " + s + ",\n" +
                "      \"steps\": " + t + ",\n" +
                "      \"cfg\": " + AudioVariantCore.SAMPLER_CFG.ToString(inv) + ",\n" +
                "      \"sampler_name\": \"" + AudioVariantCore.SAMPLER_NAME + "\",\n" +
                "      \"scheduler\": \"" + AudioVariantCore.SCHEDULER + "\",\n" +
                "      \"denoise\": " + AudioVariantCore.SAMPLER_DENOISE.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"KSampler\"\n" +
                "  },\n" +
                "  \"sg52_11\": {\n" +
                "    \"inputs\": {\n" +
                "      \"seconds\": [\"sg52_36\", 0],\n" +
                "      \"batch_size\": 1\n" +
                "    },\n" +
                "    \"class_type\": \"EmptyLatentAudio\"\n" +
                "  },\n" +
                "  \"sg52_12\": {\n" +
                "    \"inputs\": {\n" +
                "      \"samples\": [\"sg52_3\", 0],\n" +
                "      \"vae\": [\"sg52_25\", 2]\n" +
                "    },\n" +
                "    \"class_type\": \"VAEDecodeAudio\"\n" +
                "  }";

            if (useReprompt)
                json += ",\n" + AudioVariantCore.RepromptChainJson(c, qw, inv);

            json += ",\n" +
                "  \"69\": {\n" +
                "    \"inputs\": {\n" +
                "      \"text_block\": \"" + b + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"JarJarTextList\"\n" +
                "  },\n" +
                "  \"70\": {\n" +
                "    \"inputs\": {\n" +
                "      \"list_in\": [\"69\", 0],\n" +
                "      \"index\": [\"72\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"JarJarListItem\"\n" +
                "  },\n" +
                "  \"72\": {\n" +
                "    \"inputs\": {\n" +
                "      \"seed\": " + row.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"SeedNode\"\n" +
                "  },\n" +
                "  \"58\": {\n" +
                "    \"inputs\": {\n" +
                "      \"string\": [\"70\", 0],\n" +
                "      \"find\": \" \",\n" +
                "      \"replace\": \"_\"\n" +
                "    },\n" +
                "    \"class_type\": \"StringReplace\"\n" +
                "  },\n" +
                "  \"71\": {\n" +
                "    \"inputs\": {\n" +
                "      \"string\": [\"58\", 0],\n" +
                "      \"find\": \",\",\n" +
                "      \"replace\": \"\"\n" +
                "    },\n" +
                "    \"class_type\": \"StringReplace\"\n" +
                "  },\n" +
                "  \"57\": {\n" +
                "    \"inputs\": {\n" +
                "      \"audio\": [\"sg52_12\", 0],\n" +
                "      \"filename_prefix\": \"" + sv + "\",\n" +
                "      \"audioUI\": null,\n" +
                "      \"format\": \"mp3\",\n" +
                "      \"format.quality\": \"V0\"\n" +
                "    },\n" +
                "    \"class_type\": \"SaveAudioAdvanced\"\n" +
                "  },\n" +
                "  \"mm_savewav\": {\n" +
                "    \"inputs\": {\n" +
                "      \"filename_prefix\": \"mm_audio\",\n" +
                "      \"audio\": [\"sg52_12\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"SaveAudio\"\n" +
                "  }\n" +
                "}";

            return json;
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
    /// Built-in frozen copy of
    /// a_giant_wasp_being_hurt_buzzing_stuttering_angry_high-pitched_squeal_00002.json
    /// (24 UI nodes, Ollama reprompt) as an API-format builder. The Ollama
    /// song-description front end (79, steered by prompt) feeds the synth; the
    /// category classifier (91/92) plus regex/sanitize chain feeds the saver
    /// prefix (pinned to saverPrefix, chain kept for shape). Duration rides
    /// PrimitiveInt 97 (default 300s, also stamped into the "_sec_N" saver
    /// fragment). Nodes 69/70/72 are vestigial in the source file and kept
    /// verbatim. Emits 32 nodes (39 with reprompt).
    /// </summary>
    public static class WaspOllamaWorkflow
    {
        // Model filenames frozen from the outer subgraph-node widgets.
        public const string DEFAULT_CKPT = "stable_audio_3_medium.safetensors";
        public const string DEFAULT_SA_CLIP = "t5gemma_b_b_ul2.safetensors";
        public const string DEFAULT_QWEN_CLIP = "qwen3.5_2b_bf16.safetensors";
        // Literal saver prefix frozen from node 57 (pinned via saverPrefix).
        public const string DEFAULT_SAVER_PREFIX = "audio/stable_audio_3";
        public const string DEFAULT_CATEGORY = "Music";
        public const double DEFAULT_DURATION = 300;
        public const int DEFAULT_STEPS = 8;
        // Frozen Ollama front-end inputs.
        public const string DEFAULT_OLLAMA_URL = "http://127.0.0.1:11434";
        public const string DEFAULT_OLLAMA_MODEL = "qwen3.5:0.8b";
        public const long DEFAULT_OLLAMA_SEED = 1556853908;
        public const string DEFAULT_OLLAMA_SYSTEM = "You are an orchestra composer. ";
        public const string DEFAULT_OLLAMA_PROMPT = "Give me a description of a song that would be used for the game Ultima Online for a specific type of location in the game, but do not name the location, only use generic keywords like dungeon or city, forest, swamp. Do not include any preamble or pre or post text. Do not use emoji or any sort of text markup, only include basic punctuation and describe it in a single paragraph. Start with the genre, the instruments used etc.";
        public const string DEFAULT_CATEGORY_QUESTION = "Respond in a single category word that determines the best category for the following music clip to be sorted into. The categories you may choose are City, Forest, Swamp, Dungeon, Frozen, Jungle, Desert, Mountain, Plains, Coast, Cave, Graveyard, Arena, Ambient. ";

        /// <summary>Node count of the emitted graph.</summary>
        public static int NodeCount(bool useReprompt)
        {
            return 32 + (useReprompt ? 7 : 0);
        }

        /// <summary>
        /// Build a wasp-style Ollama-fronted API-format prompt. prompt steers the
        /// Ollama song-description request (null/empty = frozen UO text); the
        /// inner LLM reprompt (useReprompt) is independent of that front end.
        /// </summary>
        public static string CreateWorkflow(
            string prompt = null,
            string negativePrompt = "",
            double durationSec = DEFAULT_DURATION,
            long seed = -1,
            int steps = DEFAULT_STEPS,
            bool useReprompt = false,
            string category = null,
            long ollamaSeed = -1,
            string ollamaUrl = null,
            string ollamaModel = null,
            string saverPrefix = null,
            string ckptName = null,
            string saClip = null,
            string qwenClip = null)
        {
            if (string.IsNullOrEmpty(prompt)) prompt = DEFAULT_OLLAMA_PROMPT;
            if (durationSec < 1) durationSec = 1;
            if (durationSec > 600) durationSec = 600;
            if (seed < 0) seed = RandomSeed();
            if (ollamaSeed < 0) ollamaSeed = RandomSeed() & 0x7FFFFFFFL;
            if (steps < 1) steps = DEFAULT_STEPS;
            if (string.IsNullOrEmpty(category)) category = DEFAULT_CATEGORY;
            if (string.IsNullOrEmpty(ollamaUrl)) ollamaUrl = DEFAULT_OLLAMA_URL;
            if (string.IsNullOrEmpty(ollamaModel)) ollamaModel = DEFAULT_OLLAMA_MODEL;
            if (string.IsNullOrEmpty(saverPrefix)) saverPrefix = DEFAULT_SAVER_PREFIX;
            if (string.IsNullOrWhiteSpace(ckptName)) ckptName = DEFAULT_CKPT;
            if (string.IsNullOrWhiteSpace(saClip)) saClip = DEFAULT_SA_CLIP;
            if (string.IsNullOrWhiteSpace(qwenClip)) qwenClip = DEFAULT_QWEN_CLIP;

            int durationInt = (int)Math.Round(durationSec);

            var inv = CultureInfo.InvariantCulture;
            string p = EscapeJson(prompt);
            string n = EscapeJson(negativePrompt ?? string.Empty);
            string c = EscapeJson(category);
            string s = seed.ToString(inv);
            string t = steps.ToString(inv);
            string o = ollamaSeed.ToString(inv);
            string u = EscapeJson(ollamaUrl);
            string m = EscapeJson(ollamaModel);
            string sys = EscapeJson(DEFAULT_OLLAMA_SYSTEM);
            string catq = EscapeJson(DEFAULT_CATEGORY_QUESTION);
            string sv = EscapeJson(saverPrefix);
            string ck = EscapeJson(ckptName);
            string sa = EscapeJson(saClip);
            string qw = EscapeJson(qwenClip);

            string onTrue = useReprompt ? "[\"sg52_28\", 0]" : "[\"sg52_31\", 0]";

            string json =
                "{\n" +
                "  \"sg52_31\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": [\"79\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveStringMultiline\"\n" +
                "  },\n" +
                "  \"sg52_36\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": [\"96\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveFloat\"\n" +
                "  },\n" +
                "  \"sg52_35\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": false\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveBoolean\"\n" +
                "  },\n" +
                "  \"sg52_26\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip_name\": \"" + sa + "\",\n" +
                "      \"type\": \"stable_audio\",\n" +
                "      \"device\": \"default\"\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPLoader\"\n" +
                "  },\n" +
                "  \"sg52_25\": {\n" +
                "    \"inputs\": {\n" +
                "      \"ckpt_name\": \"" + ck + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"CheckpointLoaderSimple\"\n" +
                "  },\n" +
                "  \"sg52_34\": {\n" +
                "    \"inputs\": {\n" +
                "      \"on_false\": [\"sg52_31\", 0],\n" +
                "      \"on_true\": " + onTrue + ",\n" +
                "      \"switch\": [\"sg52_35\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"ComfySwitchNode\"\n" +
                "  },\n" +
                "  \"sg52_6\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip\": [\"sg52_26\", 0],\n" +
                "      \"text\": [\"sg52_34\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPTextEncode\"\n" +
                "  },\n" +
                "  \"sg52_7\": {\n" +
                "    \"inputs\": {\n" +
                "      \"clip\": [\"sg52_26\", 0],\n" +
                "      \"text\": \"" + n + "\"\n" +
                "    },\n" +
                "    \"class_type\": \"CLIPTextEncode\"\n" +
                "  },\n" +
                "  \"sg52_3\": {\n" +
                "    \"inputs\": {\n" +
                "      \"model\": [\"sg52_25\", 0],\n" +
                "      \"positive\": [\"sg52_6\", 0],\n" +
                "      \"negative\": [\"sg52_7\", 0],\n" +
                "      \"latent_image\": [\"sg52_11\", 0],\n" +
                "      \"seed\": " + s + ",\n" +
                "      \"steps\": " + t + ",\n" +
                "      \"cfg\": " + AudioVariantCore.SAMPLER_CFG.ToString(inv) + ",\n" +
                "      \"sampler_name\": \"" + AudioVariantCore.SAMPLER_NAME + "\",\n" +
                "      \"scheduler\": \"" + AudioVariantCore.SCHEDULER + "\",\n" +
                "      \"denoise\": " + AudioVariantCore.SAMPLER_DENOISE.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"KSampler\"\n" +
                "  },\n" +
                "  \"sg52_11\": {\n" +
                "    \"inputs\": {\n" +
                "      \"seconds\": [\"sg52_36\", 0],\n" +
                "      \"batch_size\": 1\n" +
                "    },\n" +
                "    \"class_type\": \"EmptyLatentAudio\"\n" +
                "  },\n" +
                "  \"sg52_12\": {\n" +
                "    \"inputs\": {\n" +
                "      \"samples\": [\"sg52_3\", 0],\n" +
                "      \"vae\": [\"sg52_25\", 2]\n" +
                "    },\n" +
                "    \"class_type\": \"VAEDecodeAudio\"\n" +
                "  }";

            if (useReprompt)
                json += ",\n" + AudioVariantCore.RepromptChainJson(c, qw, inv);

            json += ",\n" +
                "  \"80\": {\n" +
                "    \"inputs\": {\n" +
                "      \"url\": \"" + u + "\",\n" +
                "      \"model\": \"" + m + "\",\n" +
                "      \"keep_alive\": 5,\n" +
                "      \"keep_alive_unit\": \"minutes\"\n" +
                "    },\n" +
                "    \"class_type\": \"OllamaConnectivityV2\"\n" +
                "  },\n" +
                "  \"81\": {\n" +
                "    \"inputs\": {\n" +
                "      \"enable_mirostat\": false,\n" +
                "      \"mirostat\": 0,\n" +
                "      \"enable_mirostat_eta\": false,\n" +
                "      \"mirostat_eta\": 0.1,\n" +
                "      \"enable_mirostat_tau\": false,\n" +
                "      \"mirostat_tau\": 5,\n" +
                "      \"enable_num_ctx\": false,\n" +
                "      \"num_ctx\": 2048,\n" +
                "      \"enable_repeat_last_n\": false,\n" +
                "      \"repeat_last_n\": 64,\n" +
                "      \"enable_repeat_penalty\": false,\n" +
                "      \"repeat_penalty\": 1.1,\n" +
                "      \"enable_temperature\": false,\n" +
                "      \"temperature\": 0.8,\n" +
                "      \"enable_seed\": false,\n" +
                "      \"seed\": " + o + ",\n" +
                "      \"enable_stop\": false,\n" +
                "      \"stop\": \"\",\n" +
                "      \"enable_tfs_z\": false,\n" +
                "      \"tfs_z\": 1,\n" +
                "      \"enable_num_predict\": false,\n" +
                "      \"num_predict\": -1,\n" +
                "      \"enable_top_k\": false,\n" +
                "      \"top_k\": 40,\n" +
                "      \"enable_top_p\": false,\n" +
                "      \"top_p\": 0.9,\n" +
                "      \"enable_min_p\": false,\n" +
                "      \"min_p\": 0,\n" +
                "      \"debug\": false\n" +
                "    },\n" +
                "    \"class_type\": \"OllamaOptionsV2\"\n" +
                "  },\n" +
                "  \"79\": {\n" +
                "    \"inputs\": {\n" +
                "      \"connectivity\": [\"80\", 0],\n" +
                "      \"options\": [\"81\", 0],\n" +
                "      \"system\": \"" + sys + "\",\n" +
                "      \"prompt\": \"" + p + "\",\n" +
                "      \"think\": false,\n" +
                "      \"keep_context\": false,\n" +
                "      \"format\": \"text\"\n" +
                "    },\n" +
                "    \"class_type\": \"OllamaGenerateV2\"\n" +
                "  },\n" +
                "  \"90\": {\n" +
                "    \"inputs\": {\n" +
                "      \"string\": [\"79\", 0],\n" +
                "      \"regex_pattern\": \"[<>:\\\"/\\\\\\\\|?*\\\\x00-\\\\x1F]\",\n" +
                "      \"replace\": \"\",\n" +
                "      \"case_insensitive\": true,\n" +
                "      \"multiline\": false,\n" +
                "      \"dotall\": false,\n" +
                "      \"count\": 0\n" +
                "    },\n" +
                "    \"class_type\": \"RegexReplace\"\n" +
                "  },\n" +
                "  \"98\": {\n" +
                "    \"inputs\": {\n" +
                "      \"string\": [\"90\", 0],\n" +
                "      \"find\": \" \",\n" +
                "      \"replace\": \"_\"\n" +
                "    },\n" +
                "    \"class_type\": \"StringReplace\"\n" +
                "  },\n" +
                "  \"99\": {\n" +
                "    \"inputs\": {\n" +
                "      \"string\": [\"98\", 0],\n" +
                "      \"find\": \",\",\n" +
                "      \"replace\": \"\"\n" +
                "    },\n" +
                "    \"class_type\": \"StringReplace\"\n" +
                "  },\n" +
                "  \"100\": {\n" +
                "    \"inputs\": {\n" +
                "      \"string\": [\"99\", 0],\n" +
                "      \"find\": \"-\",\n" +
                "      \"replace\": \"\"\n" +
                "    },\n" +
                "    \"class_type\": \"StringReplace\"\n" +
                "  },\n" +
                "  \"74\": {\n" +
                "    \"inputs\": {\n" +
                "      \"string\": [\"100\", 0],\n" +
                "      \"start\": 0,\n" +
                "      \"end\": 80\n" +
                "    },\n" +
                "    \"class_type\": \"StringSubstring\"\n" +
                "  },\n" +
                "  \"77\": {\n" +
                "    \"inputs\": {\n" +
                "      \"values.a\": [\"97\", 0],\n" +
                "      \"f_string\": \"{a}\"\n" +
                "    },\n" +
                "    \"class_type\": \"StringFormat\"\n" +
                "  },\n" +
                "  \"75\": {\n" +
                "    \"inputs\": {\n" +
                "      \"string_a\": [\"74\", 0],\n" +
                "      \"string_b\": [\"77\", 0],\n" +
                "      \"delimiter\": \"_sec_\"\n" +
                "    },\n" +
                "    \"class_type\": \"StringConcatenate\"\n" +
                "  },\n" +
                "  \"92\": {\n" +
                "    \"inputs\": {\n" +
                "      \"string_a\": \"" + catq + "\",\n" +
                "      \"string_b\": [\"79\", 0],\n" +
                "      \"delimiter\": \"\"\n" +
                "    },\n" +
                "    \"class_type\": \"StringConcatenate\"\n" +
                "  },\n" +
                "  \"91\": {\n" +
                "    \"inputs\": {\n" +
                "      \"connectivity\": [\"80\", 0],\n" +
                "      \"options\": [\"81\", 0],\n" +
                "      \"system\": \"" + sys + "\",\n" +
                "      \"prompt\": [\"92\", 0],\n" +
                "      \"think\": false,\n" +
                "      \"keep_context\": false,\n" +
                "      \"format\": \"text\"\n" +
                "    },\n" +
                "    \"class_type\": \"OllamaGenerateV2\"\n" +
                "  },\n" +
                "  \"93\": {\n" +
                "    \"inputs\": {\n" +
                "      \"string_a\": [\"91\", 0],\n" +
                "      \"string_b\": [\"75\", 0],\n" +
                "      \"delimiter\": \"_\"\n" +
                "    },\n" +
                "    \"class_type\": \"StringConcatenate\"\n" +
                "  },\n" +
                "  \"103\": {\n" +
                "    \"inputs\": {\n" +
                "      \"string\": [\"93\", 0],\n" +
                "      \"regex_pattern\": \"[<>:\\\"/\\\\\\\\|?*\\\\x00-\\\\x1F]\",\n" +
                "      \"replace\": \"\",\n" +
                "      \"case_insensitive\": true,\n" +
                "      \"multiline\": false,\n" +
                "      \"dotall\": false,\n" +
                "      \"count\": 0\n" +
                "    },\n" +
                "    \"class_type\": \"RegexReplace\"\n" +
                "  },\n" +
                "  \"96\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": [\"97\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"ComfyNumberConvert\"\n" +
                "  },\n" +
                "  \"97\": {\n" +
                "    \"inputs\": {\n" +
                "      \"value\": " + durationInt.ToString(inv) + "\n" +
                "    },\n" +
                "    \"class_type\": \"PrimitiveInt\"\n" +
                "  },\n" +
                "  \"69\": {\n" +
                "    \"inputs\": {\n" +
                "      \"text_block\": \"A sea shanty happy irish song.\"\n" +
                "    },\n" +
                "    \"class_type\": \"JarJarTextList\"\n" +
                "  },\n" +
                "  \"70\": {\n" +
                "    \"inputs\": {\n" +
                "      \"list_in\": [\"69\", 0],\n" +
                "      \"index\": [\"72\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"JarJarListItem\"\n" +
                "  },\n" +
                "  \"72\": {\n" +
                "    \"inputs\": {\n" +
                "      \"seed\": 0\n" +
                "    },\n" +
                "    \"class_type\": \"SeedNode\"\n" +
                "  },\n" +
                "  \"57\": {\n" +
                "    \"inputs\": {\n" +
                "      \"audio\": [\"sg52_12\", 0],\n" +
                "      \"filename_prefix\": \"" + sv + "\",\n" +
                "      \"audioUI\": null,\n" +
                "      \"format\": \"mp3\",\n" +
                "      \"format.quality\": \"V0\"\n" +
                "    },\n" +
                "    \"class_type\": \"SaveAudioAdvanced\"\n" +
                "  },\n" +
                "  \"mm_savewav\": {\n" +
                "    \"inputs\": {\n" +
                "      \"filename_prefix\": \"mm_audio\",\n" +
                "      \"audio\": [\"sg52_12\", 0]\n" +
                "    },\n" +
                "    \"class_type\": \"SaveAudio\"\n" +
                "  }\n" +
                "}";

            return json;
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
