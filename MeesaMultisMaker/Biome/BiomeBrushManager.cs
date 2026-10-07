using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace MeesaMultisMaker.Biome
{
    /// <summary>
    /// Manages saving, loading, and listing biome brushes on disk.
    /// Brushes are stored as XML files in %AppData%/MeesaMultisMaker/Biomes/.
    /// </summary>
    public static class BiomeBrushManager
    {
        private static readonly string BiomesFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MeesaMultisMaker", "Biomes");

        /// <summary>
        /// Save a biome brush to disk
        /// </summary>
        public static void Save(BiomeBrush brush)
        {
            if (!Directory.Exists(BiomesFolder))
                Directory.CreateDirectory(BiomesFolder);

            string filename = SanitizeFilename(brush.Name) + ".xml";
            string filepath = Path.Combine(BiomesFolder, filename);

            var serializer = new XmlSerializer(typeof(BiomeBrush));
            using (var writer = new StreamWriter(filepath))
            {
                serializer.Serialize(writer, brush);
            }
        }

        /// <summary>
        /// Load all biome brushes from disk
        /// </summary>
        public static List<BiomeBrush> LoadAll()
        {
            var brushes = new List<BiomeBrush>();

            if (!Directory.Exists(BiomesFolder))
                return brushes;

            foreach (var file in Directory.GetFiles(BiomesFolder, "*.xml"))
            {
                try
                {
                    var serializer = new XmlSerializer(typeof(BiomeBrush));
                    using (var reader = new StreamReader(file))
                    {
                        var brush = (BiomeBrush)serializer.Deserialize(reader);
                        brushes.Add(brush);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading biome {file}: {ex.Message}");
                }
            }

            return brushes;
        }

        /// <summary>
        /// Delete a biome brush from disk
        /// </summary>
        public static bool Delete(string biomeName)
        {
            if (!Directory.Exists(BiomesFolder))
                return false;

            string filename = SanitizeFilename(biomeName) + ".xml";
            string filepath = Path.Combine(BiomesFolder, filename);

            if (File.Exists(filepath))
            {
                File.Delete(filepath);
                return true;
            }
            return false;
        }

        private static string SanitizeFilename(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string sanitized = name;
            foreach (var c in invalid)
                sanitized = sanitized.Replace(c, '_');
            return sanitized;
        }
    }
}
