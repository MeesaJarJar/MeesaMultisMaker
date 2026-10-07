using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Centralized application configuration management.
    /// Stores all user-configurable settings in an XML file.
    /// </summary>
    public class AppConfig
    {
        private static AppConfig _instance;
        private static readonly object _lock = new object();
        private static string _configFilePath;

        /// <summary>
        /// Gets the singleton instance of AppConfig
        /// </summary>
        public static AppConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _configFilePath = Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "MeesaMultisMaker",
                                "config.xml");
                            _instance = Load();
                        }
                    }
                }
                return _instance;
            }
        }

        // ===== PATH SETTINGS =====

        /// <summary>
        /// Path to the Ultima Online MUL files folder (contains art.mul, map0.mul, etc.)
        /// </summary>
        public string MulFolderPath { get; set; } = "";

        /// <summary>
        /// Path to the PNG art files folder (UOFItems or similar)
        /// </summary>
        public string ArtFolderPath { get; set; } = "";

        /// <summary>
        /// List of common UO installation paths to search
        /// </summary>
        public List<string> UOSearchPaths { get; set; } = new List<string>
        {
            @"C:\Program Files (x86)\Electronic Arts\Ultima Online Classic",
            @"C:\Program Files (x86)\UOForever\UO",
            @"C:\Program Files (x86)\Electronic Arts\Ultima Online",
            @"C:\Program Files\Electronic Arts\Ultima Online Classic",
            @"C:\UO",
            @"C:\Ultima Online"
        };

        // ===== COMFYUI SETTINGS =====

        /// <summary>
        /// Selected AI backend
        /// </summary>
        public AIBackend SelectedAIBackend { get; set; } = AIBackend.ComfyUI;

        /// <summary>
        /// ComfyUI server URL
        /// </summary>
        public string ComfyUIUrl { get; set; } = "http://localhost:8188";

        /// <summary>
        /// Default positive prompt for AI generation
        /// </summary>
        public string DefaultPrompt { get; set; } = "cave surrounded by deep pools of blue glacial water and cliffs of ice, top down aerial view";

        /// <summary>
        /// Default negative prompt for AI generation
        /// </summary>
        public string DefaultNegativePrompt { get; set; } = "blurry, low quality, distorted, text, watermark";

        /// <summary>
        /// Default number of steps for AI generation
        /// </summary>
        public int DefaultSteps { get; set; } = 23;

        /// <summary>
        /// Default CFG scale for AI generation
        /// </summary>
        public double DefaultCFG { get; set; } = 6.0;

        /// <summary>
        /// Default denoise strength for AI generation
        /// </summary>
        public double DefaultDenoise { get; set; } = 0.70;

        /// <summary>
        /// Default sampler for AI generation
        /// </summary>
        public string DefaultSampler { get; set; } = "lcm";

        /// <summary>
        /// Default scheduler for AI generation
        /// </summary>
        public string DefaultScheduler { get; set; } = "ddim_uniform";

        /// <summary>
        /// Default resolution for AI generation
        /// </summary>
        public int DefaultResolution { get; set; } = 1536;

        // ===== CONTROLNET SETTINGS =====

        /// <summary>
        /// Whether to use the ControlNet depth workflow instead of standard img2img
        /// </summary>
        public bool UseControlNet { get; set; } = true;

        /// <summary>
        /// ControlNet model file name
        /// </summary>
        public string ControlNetModel { get; set; } = @"1.5\control_v11f1p_sd15_depth_fp16.safetensors";

        /// <summary>
        /// ControlNet conditioning strength
        /// </summary>
        public double ControlNetStrength { get; set; } = 0.5;

        /// <summary>
        /// ControlNet start percent
        /// </summary>
        public double ControlNetStartPercent { get; set; } = 0.0;

        /// <summary>
        /// ControlNet end percent
        /// </summary>
        public double ControlNetEndPercent { get; set; } = 1.0;

        /// <summary>
        /// Image blur radius for ControlNet preprocessing
        /// </summary>
        public int ControlNetBlurRadius { get; set; } = 7;

        /// <summary>
        /// Image blur sigma for ControlNet preprocessing
        /// </summary>
        public double ControlNetBlurSigma { get; set; } = 5.0;

        /// <summary>
        /// Background removal threshold for MeesaJarJar node
        /// </summary>
        public double ControlNetBGThreshold { get; set; } = 0.03;

        /// <summary>
        /// Background removal feather for MeesaJarJar node
        /// </summary>
        public double ControlNetBGFeather { get; set; } = 0.0;

        /// <summary>
        /// Channel mode for MeesaJarJar node
        /// </summary>
        public string ControlNetChannelMode { get; set; } = "rgb_max";

        /// <summary>
        /// Despill value for MeesaJarJar node
        /// </summary>
        public double ControlNetDespill { get; set; } = 0.0;

        /// <summary>
        /// Invert mask option for MeesaJarJar node
        /// </summary>
        public string ControlNetInvertMask { get; set; } = "no";

        // ===== UI SETTINGS =====

        /// <summary>
        /// Remember window positions between sessions
        /// </summary>
        public bool RememberWindowPositions { get; set; } = true;

        /// <summary>
        /// Auto-load art files on startup
        /// </summary>
        public bool AutoLoadArtOnStartup { get; set; } = true;

        // ===== MEESAJARJAR.COM SETTINGS =====

        /// <summary>
        /// MeesaJarJar server base URL
        /// </summary>
        public string JarJarApiUrl { get; set; } = "https://meesajarjar.com";

        /// <summary>
        /// Authentication token for the MeesaJarJar server (optional — only needed if server enforces auth)
        /// </summary>
        public string JarJarAuthToken { get; set; } = "";

        /// <summary>
        /// Master kill-switch for "Push to MeesaJarJar". False (default):
        /// push buttons render grayed-out and auto-push paths skip.
        /// Re-enable here to restore the feature without code changes.
        /// </summary>
        public bool JarJarPushEnabled { get; set; } = false;

        /// <summary>
        /// Automatically push to MeesaJarJar after AI generation completes
        /// </summary>
        public bool JarJarAutoPush { get; set; } = false;

        // ===== UPDATE SETTINGS =====

        /// <summary>
        /// Check for updates on startup
        /// </summary>
        public bool CheckForUpdatesOnStartup { get; set; } = true;

        /// <summary>
        /// Last time we checked for updates
        /// </summary>
        public DateTime LastUpdateCheck { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Version tag that user chose to skip (e.g., "v1.2.3")
        /// </summary>
        public string SkippedVersion { get; set; } = "";

        // ===== 3D EDITOR LLM PROMPTS (editable in the 3D Editor) =====

        /// <summary>
        /// System instruction for the local idea-generator LLM.
        /// </summary>
        public string LlmAgentPrompt { get; set; } =
            "You are an idea generator. You respond with a description of an object or group of objects.";

        /// <summary>
        /// User message sent to the local idea-generator LLM per idea.
        /// </summary>
        public string LlmIdeaPrompt { get; set; } =
            "Respond with a description of an object that one might find in the 1400s. Only the description, nothing else.";

        // ===== AUDIO WATCHER (editable in the Audio Editor) =====

        /// <summary>
        /// Ambient-sound prompt instruction for the watcher vision model.
        /// </summary>
        public string WatcherFoleyPrompt { get; set; } =
            "Generate a prompt for ambient sounds that fit this scene. Reply with ONLY the sound prompt, one or two dense sentences.";

        /// <summary>
        /// Background-music prompt instruction for the watcher vision model.
        /// </summary>
        public string WatcherMusicPrompt { get; set; } =
            "Generate a prompt for ambient background music that fits this scene. Reply with ONLY the music prompt, one or two dense sentences.";

        /// <summary>
        /// Frame-change threshold in PERCENT (0.5-20): a sense frame whose
        /// 16x16 gray-hash distance from the previous frame is BELOW this
        /// counts as static -- vision is skipped and on-air clips loop.
        /// </summary>
        public double WatcherSceneDeltaPct { get; set; } = 4.0;

        /// <summary>
        /// Watcher music with instrumental intent is transcribed; vocals
        /// bounce it to one hardened retry (air best).
        /// </summary>
        public bool WatcherBounceVocals { get; set; } = true;

        /// <summary>
        /// Watcher clips scoring under the CLAP match floor get one retry
        /// (air best).
        /// </summary>
        public bool WatcherClapGate { get; set; } = true;

        /// <summary>
        /// Attempts to find UO installation automatically
        /// </summary>
        public string FindMulFolder()
        {
            // First check if we have a saved path that still exists.
            // Trust user-configured paths without strict validation —
            // custom servers may only have map files without art files.
            if (!string.IsNullOrEmpty(MulFolderPath) && Directory.Exists(MulFolderPath))
            {
                return MulFolderPath;
            }

            // Search common paths (require validation for auto-detected ones)
            foreach (var path in UOSearchPaths)
            {
                if (Directory.Exists(path) && ValidateMulFolder(path))
                {
                    MulFolderPath = path;
                    Save();
                    return path;
                }
            }

            return null;
        }

        /// <summary>
        /// Validates that a folder contains UO data files (art MUL/UOP or map files)
        /// </summary>
        public bool ValidateMulFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return false;

            // Check for OSI-style MUL files (preferred)
            bool hasMulFiles = File.Exists(Path.Combine(path, "art.mul")) &&
                               File.Exists(Path.Combine(path, "artidx.mul"));

            // Check for UOP-only files (UOForever style / UO Classic)
            bool hasUopFiles = File.Exists(Path.Combine(path, "artLegacyMUL.uop"));

            // Check for UOP map files (UO Classic style)
            bool hasUopMaps = File.Exists(Path.Combine(path, "map0LegacyMUL.uop"));

            // Check for any map*.mul files (custom servers may only have maps)
            bool hasMapFiles = File.Exists(Path.Combine(path, "map0.mul"));

            return hasMulFiles || hasUopFiles || hasUopMaps || hasMapFiles;
        }

        /// <summary>
        /// Gets the art file format available in the folder
        /// </summary>
        public ArtFileFormat GetArtFileFormat(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return ArtFileFormat.None;

            // Check for Tecmo Expanded Art format (art.mul + artidx.mul with 0x40000 entries)
            string artIdxPath = Path.Combine(path, "artidx.mul");
            string artMulPath = Path.Combine(path, "art.mul");
            if (File.Exists(artIdxPath) && File.Exists(artMulPath))
            {
                try
                {
                    using (var idxStream = File.OpenRead(artIdxPath))
                    {
                        long count = idxStream.Length / 12; // 12 bytes per entry
                        // Tecmo format has 0x40000 (262144) entries
                        if (count >= 0x40000)
                            return ArtFileFormat.TecmoExpanded;
                    }
                }
                catch { }
                
                // Standard OSI-style MUL files
                return ArtFileFormat.MulFiles;
            }

            // Check for UOP-only files (UOForever style)
            if (File.Exists(Path.Combine(path, "artLegacyMUL.uop")))
                return ArtFileFormat.UopOnly;

            return ArtFileFormat.None;
        }

        /// <summary>
        /// Gets a description of what art files are in the folder
        /// </summary>
        public string GetArtFileDescription(string path)
        {
            var format = GetArtFileFormat(path);
            switch (format)
            {
                case ArtFileFormat.MulFiles:
                    return "OSI-style (art.mul + artidx.mul)";
                case ArtFileFormat.UopOnly:
                    return "UOForever-style (artLegacyMUL.uop only)";
                case ArtFileFormat.TecmoExpanded:
                    return "Tecmo Expanded Art (art.mul + artidx.mul, 262K entries)";
                default:
                    return "No art files found";
            }
        }

        /// <summary>
        /// Attempts to find the art PNG folder automatically
        /// </summary>
        public string FindArtFolder()
        {
            // First check if we have a saved path that still exists
            if (!string.IsNullOrEmpty(ArtFolderPath) && Directory.Exists(ArtFolderPath))
            {
                return ArtFolderPath;
            }

            // Check app directory
            string appDir = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
            string uofItemsPath = Path.Combine(appDir, "UOFItems");
            if (Directory.Exists(uofItemsPath))
            {
                ArtFolderPath = uofItemsPath;
                Save();
                return uofItemsPath;
            }

            // Check common development paths
            string[] devPaths = new string[]
            {
                @"C:\Users\SERVER\source\repos\MeesaMultisMaker\MeesaMultisMaker\UOFItems",
                Path.Combine(appDir, "..", "UOFItems"),
                Path.Combine(appDir, "..", "..", "UOFItems")
            };

            foreach (var path in devPaths)
            {
                if (Directory.Exists(path))
                {
                    ArtFolderPath = Path.GetFullPath(path);
                    Save();
                    return ArtFolderPath;
                }
            }

            return null;
        }

        /// <summary>
        /// Saves the configuration to disk
        /// </summary>
        public void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(_configFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var serializer = new XmlSerializer(typeof(AppConfig));
                using (var writer = new StreamWriter(_configFilePath))
                {
                    serializer.Serialize(writer, this);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving config: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the configuration from disk
        /// </summary>
        private static AppConfig Load()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var serializer = new XmlSerializer(typeof(AppConfig));
                    using (var reader = new StreamReader(_configFilePath))
                    {
                        var config = (AppConfig)serializer.Deserialize(reader);
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading config: {ex.Message}");
            }

            // Return default config if loading fails
            return new AppConfig();
        }

        /// <summary>
        /// Reloads configuration from disk
        /// </summary>
        public static void Reload()
        {
            lock (_lock)
            {
                _instance = Load();
            }
        }

        /// <summary>
        /// Gets the config file path for display purposes
        /// </summary>
        public static string ConfigFilePath => _configFilePath;
    }

    /// <summary>
    /// Enum for the type of art files available
    /// </summary>
    public enum ArtFileFormat
    {
        None,
        MulFiles,    // OSI-style: art.mul + artidx.mul
        UopOnly,     // UOForever-style: artLegacyMUL.uop only (no art.mul)
        TecmoExpanded // Tecmo Expanded Art: art.mul + artidx.mul with 0x40000 entries
    }

    /// <summary>
    /// Enumeration for AI backends
    /// </summary>
    public enum AIBackend
    {
        ComfyUI
    }
}
