using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Represents metadata for a saved map iteration.
    /// </summary>
    public class MapSaveInfo
    {
        public string Name { get; set; }
        public string FolderPath { get; set; }
        public DateTime SavedDate { get; set; }
        public int MapIndex { get; set; }
        public int MapWidth { get; set; }
        public int MapHeight { get; set; }
        public int StaticOverrideCount { get; set; }
        public string Description { get; set; }

        public override string ToString()
        {
            return $"{Name}  ({SavedDate:yyyy-MM-dd HH:mm})";
        }
    }

    /// <summary>
    /// Result of a save operation.
    /// </summary>
    public class SaveResult
    {
        public bool Success { get; set; }
        public bool MapFileSaved { get; set; }
        public bool StaticsFileSaved { get; set; }
        public string SaveFolder { get; set; }
        public string SaveName { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Orchestrates saving and loading map iterations with automatic backups.
    /// 
    /// Save layout under AppData/MeesaMultisMaker/MapSaves/:
    ///   map0_20240115_143022/
    ///     map0.mul
    ///     staidx0.mul
    ///     statics0.mul
    ///     save.info
    /// </summary>
    public class MapSaveManager
    {
        private readonly string _savesRootFolder;

        public MapSaveManager()
        {
            _savesRootFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MeesaMultisMaker", "MapSaves");
            Directory.CreateDirectory(_savesRootFolder);
        }

        /// <summary>
        /// Root folder where all save iterations are stored.
        /// </summary>
        public string SavesRootFolder => _savesRootFolder;

        /// <summary>
        /// Save the current map state as a new iteration.
        /// </summary>
        public SaveResult SaveIteration(
            string originalMulFolder,
            int mapIndex,
            MapData mapData,
            Dictionary<(int x, int y), List<StaticTile>> staticOverrides,
            string description = null)
        {
            var result = new SaveResult();

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string saveName = $"map{mapIndex}_{timestamp}";
            string saveFolder = Path.Combine(_savesRootFolder, saveName);
            Directory.CreateDirectory(saveFolder);

            try
            {
                // 1. Save map file
                string mapPath = Path.Combine(saveFolder, $"map{mapIndex}.mul");
                MapWriter.Save(mapPath, mapData);
                result.MapFileSaved = true;

                System.Diagnostics.Debug.WriteLine($"MapSaveManager: Saved map to {mapPath}");

                // 2. Save statics (merge overrides with original disk data)
                string staticsIdxPath = Path.Combine(saveFolder, $"staidx{mapIndex}.mul");
                string staticsMulPath = Path.Combine(saveFolder, $"statics{mapIndex}.mul");

                string srcIdxPath = Path.Combine(originalMulFolder, $"staidx{mapIndex}.mul");
                string srcMulPath = Path.Combine(originalMulFolder, $"statics{mapIndex}.mul");

                if (staticOverrides != null && staticOverrides.Count > 0
                    && File.Exists(srcIdxPath) && File.Exists(srcMulPath))
                {
                    StaticsWriter.Save(originalMulFolder, mapIndex, staticsIdxPath, staticsMulPath, staticOverrides);
                    result.StaticsFileSaved = true;
                }
                else
                {
                    // No overrides — copy original statics as-is
                    CopyIfExists(srcIdxPath, staticsIdxPath);
                    CopyIfExists(srcMulPath, staticsMulPath);
                    result.StaticsFileSaved = true;
                }

                System.Diagnostics.Debug.WriteLine($"MapSaveManager: Saved statics to {saveFolder}");

                // 3. Write metadata
                WriteSaveInfo(saveFolder, mapIndex, mapData, staticOverrides, description);

                result.Success = true;
                result.SaveFolder = saveFolder;
                result.SaveName = saveName;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"MapSaveManager: Save failed: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Backup original files and overwrite them in-place.
        /// <paramref name="sourceFolder"/> is where the current map/statics data
        /// was loaded from (may differ from <paramref name="mulFolder"/> when
        /// a saved iteration was loaded).
        /// </summary>
        public SaveResult DeployToGameFolder(
            string mulFolder,
            int mapIndex,
            MapData mapData,
            Dictionary<(int x, int y), List<StaticTile>> staticOverrides,
            string sourceFolder)
        {
            var result = new SaveResult();

            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                // Backup originals in the game folder
                BackupFile(mulFolder, $"map{mapIndex}.mul", timestamp);
                BackupFile(mulFolder, $"staidx{mapIndex}.mul", timestamp);
                BackupFile(mulFolder, $"statics{mapIndex}.mul", timestamp);

                // Save map
                string mapPath = Path.Combine(mulFolder, $"map{mapIndex}.mul");
                MapWriter.Save(mapPath, mapData);
                result.MapFileSaved = true;

                // Determine the statics source folder.
                // If we loaded from a save folder, the source statics are there.
                // If we loaded from the game folder itself, the source is the
                // backup we just created.
                bool loadedFromGameFolder = string.Equals(
                    Path.GetFullPath(sourceFolder),
                    Path.GetFullPath(mulFolder),
                    StringComparison.OrdinalIgnoreCase);

                bool hasOverrides = staticOverrides != null && staticOverrides.Count > 0;

                // Skip statics only if both source IS the game folder AND
                // there are no overrides (nothing changed).
                if (loadedFromGameFolder && !hasOverrides)
                {
                    // Statics are unchanged — nothing to write.
                    result.StaticsFileSaved = true;
                }
                else
                {
                    // We need to write statics.
                    // Build a merge-source folder that StaticsWriter can read
                    // while we write to mulFolder.
                    string mergeSourceFolder;
                    string tempSourceFolder = null;

                    if (loadedFromGameFolder)
                    {
                        // Source was the game folder — read from the backup we created
                        tempSourceFolder = Path.Combine(Path.GetTempPath(),
                            $"MeesaMultisMaker_deploy_{timestamp}");
                        Directory.CreateDirectory(tempSourceFolder);

                        string backupIdx = Path.Combine(mulFolder,
                            $"staidx{mapIndex}.mul.{timestamp}.bak");
                        string backupMul = Path.Combine(mulFolder,
                            $"statics{mapIndex}.mul.{timestamp}.bak");

                        File.Copy(backupIdx,
                            Path.Combine(tempSourceFolder, $"staidx{mapIndex}.mul"));
                        File.Copy(backupMul,
                            Path.Combine(tempSourceFolder, $"statics{mapIndex}.mul"));

                        mergeSourceFolder = tempSourceFolder;
                    }
                    else
                    {
                        // Source was a save folder — read directly from it
                        mergeSourceFolder = sourceFolder;
                    }

                    try
                    {
                        string idxPath = Path.Combine(mulFolder, $"staidx{mapIndex}.mul");
                        string mulPath = Path.Combine(mulFolder, $"statics{mapIndex}.mul");

                        // Pass overrides (may be empty — StaticsWriter handles that
                        // by copying all blocks verbatim, effectively a file copy
                        // with proper index rebuilding).
                        StaticsWriter.Save(mergeSourceFolder, mapIndex, idxPath, mulPath,
                            staticOverrides ?? new Dictionary<(int, int), List<StaticTile>>());
                    }
                    finally
                    {
                        if (tempSourceFolder != null)
                        {
                            try { Directory.Delete(tempSourceFolder, true); } catch { }
                        }
                    }

                    result.StaticsFileSaved = true;
                }

                result.Success = true;
                result.SaveFolder = mulFolder;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"MapSaveManager: Deploy failed: {ex}");
            }

            return result;
        }

        /// <summary>
        /// List all saved iterations, newest first.
        /// </summary>
        public List<MapSaveInfo> GetSavedIterations(int? filterMapIndex = null)
        {
            var saves = new List<MapSaveInfo>();

            if (!Directory.Exists(_savesRootFolder))
                return saves;

            foreach (var dir in Directory.GetDirectories(_savesRootFolder))
            {
                var info = ReadSaveInfo(dir);
                if (info == null) continue;

                if (filterMapIndex.HasValue && info.MapIndex != filterMapIndex.Value)
                    continue;

                saves.Add(info);
            }

            saves.Sort((a, b) => b.SavedDate.CompareTo(a.SavedDate));
            return saves;
        }

        /// <summary>
        /// Delete a saved iteration.
        /// </summary>
        public bool DeleteIteration(string saveFolder)
        {
            try
            {
                if (Directory.Exists(saveFolder))
                {
                    Directory.Delete(saveFolder, true);
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MapSaveManager: Delete failed: {ex.Message}");
            }
            return false;
        }

        // ---- Private helpers ----

        private static void BackupFile(string folder, string filename, string timestamp)
        {
            string src = Path.Combine(folder, filename);
            if (!File.Exists(src)) return;

            string backup = Path.Combine(folder, $"{filename}.{timestamp}.bak");
            if (!File.Exists(backup))
            {
                File.Copy(src, backup);
                System.Diagnostics.Debug.WriteLine($"MapSaveManager: Backed up {filename} → {Path.GetFileName(backup)}");
            }
        }

        private static void CopyIfExists(string src, string dst)
        {
            if (File.Exists(src))
                File.Copy(src, dst, true);
        }

        private static void WriteSaveInfo(
            string saveFolder, int mapIndex, MapData mapData,
            Dictionary<(int x, int y), List<StaticTile>> staticOverrides,
            string description)
        {
            string infoPath = Path.Combine(saveFolder, "save.info");

            using (var writer = new StreamWriter(infoPath))
            {
                writer.WriteLine($"MapIndex={mapIndex}");
                writer.WriteLine($"Width={mapData.Width}");
                writer.WriteLine($"Height={mapData.Height}");
                writer.WriteLine($"SavedDate={DateTime.Now:O}");
                writer.WriteLine($"StaticOverrides={staticOverrides?.Count ?? 0}");
                writer.WriteLine($"Description={description ?? ""}");
            }
        }

        private static MapSaveInfo ReadSaveInfo(string saveFolder)
        {
            string infoPath = Path.Combine(saveFolder, "save.info");
            if (!File.Exists(infoPath))
            {
                // Try to infer from folder name (map0_20240115_143022)
                return InferSaveInfo(saveFolder);
            }

            try
            {
                var info = new MapSaveInfo
                {
                    FolderPath = saveFolder,
                    Name = Path.GetFileName(saveFolder)
                };

                foreach (var line in File.ReadAllLines(infoPath))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;

                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "MapIndex":
                            int.TryParse(value, out int idx);
                            info.MapIndex = idx;
                            break;
                        case "Width":
                            int.TryParse(value, out int w);
                            info.MapWidth = w;
                            break;
                        case "Height":
                            int.TryParse(value, out int h);
                            info.MapHeight = h;
                            break;
                        case "SavedDate":
                            DateTime dt;
                            if (DateTime.TryParse(value, out dt))
                                info.SavedDate = dt;
                            break;
                        case "StaticOverrides":
                            int.TryParse(value, out int sc);
                            info.StaticOverrideCount = sc;
                            break;
                        case "Description":
                            info.Description = value;
                            break;
                    }
                }

                return info;
            }
            catch
            {
                return InferSaveInfo(saveFolder);
            }
        }

        private static MapSaveInfo InferSaveInfo(string saveFolder)
        {
            string name = Path.GetFileName(saveFolder);

            // Check if any map file exists
            bool hasMapFiles = false;
            int mapIndex = 0;

            for (int i = 0; i <= 10; i++)
            {
                if (File.Exists(Path.Combine(saveFolder, $"map{i}.mul")))
                {
                    hasMapFiles = true;
                    mapIndex = i;
                    break;
                }
            }

            if (!hasMapFiles) return null;

            return new MapSaveInfo
            {
                Name = name,
                FolderPath = saveFolder,
                MapIndex = mapIndex,
                SavedDate = Directory.GetCreationTime(saveFolder)
            };
        }
    }
}
