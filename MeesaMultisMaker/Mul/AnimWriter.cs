using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Writes mobile animations to anim.mul and anim.idx files
    /// </summary>
    public static class AnimWriter
    {
        /// <summary>
        /// Write an animation sequence to anim files
        /// </summary>
        public static bool WriteAnimation(string mulFolder, AnimReader.AnimationInfo animation, AnimReader.AnimationType type)
        {
            if (animation == null || animation.Frames == null || animation.Frames.Count == 0)
                return false;

            try
            {
                string mulFile = GetMulFileName(type);
                string idxFile = GetIdxFileName(type);
                string mulPath = Path.Combine(mulFolder, mulFile);
                string idxPath = Path.Combine(mulFolder, idxFile);

                // Backup existing files
                BackupFile(mulPath);
                BackupFile(idxPath);

                // Calculate the index for this animation
                int actionsPerBody = (type >= AnimReader.AnimationType.People) ? 175 : 110;
                int index = (animation.BodyId * actionsPerBody) + (animation.Action * 5) + animation.Direction;

                // Read existing index
                var indexEntries = ReadIndex(idxPath);
                if (index >= indexEntries.Count)
                {
                    // Expand index if needed
                    while (indexEntries.Count <= index)
                    {
                        indexEntries.Add(new IndexEntry { Offset = -1, Length = 0, Extra = 0 });
                    }
                }

                // Encode the animation
                byte[] animData = EncodeAnimation(animation);
                if (animData == null)
                    return false;

                // Find or allocate space in mul file
                int offset = AppendToMulFile(mulPath, animData);
                
                // Update index entry
                indexEntries[index] = new IndexEntry
                {
                    Offset = offset,
                    Length = animData.Length,
                    Extra = 0 // Not used for animations
                };

                // Write updated index
                WriteIndex(idxPath, indexEntries);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AnimWriter: Exception writing animation: {ex.Message}");
                return false;
            }
        }

        private static string GetMulFileName(AnimReader.AnimationType type)
        {
            switch (type)
            {
                case AnimReader.AnimationType.Monster: return "anim.mul";
                case AnimReader.AnimationType.Monster2: return "anim2.mul";
                case AnimReader.AnimationType.Monster3: return "anim3.mul";
                case AnimReader.AnimationType.People: return "anim4.mul";
                case AnimReader.AnimationType.People2: return "anim5.mul";
                default: return "anim.mul";
            }
        }

        private static string GetIdxFileName(AnimReader.AnimationType type)
        {
            switch (type)
            {
                case AnimReader.AnimationType.Monster: return "anim.idx";
                case AnimReader.AnimationType.Monster2: return "anim2.idx";
                case AnimReader.AnimationType.Monster3: return "anim3.idx";
                case AnimReader.AnimationType.People: return "anim4.idx";
                case AnimReader.AnimationType.People2: return "anim5.idx";
                default: return "anim.idx";
            }
        }

        private static void BackupFile(string filePath)
        {
            if (!File.Exists(filePath))
                return;

            string backupPath = filePath + $".backup_{DateTime.Now:yyyyMMddHHmmss}";
            try
            {
                File.Copy(filePath, backupPath, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AnimWriter: Failed to backup {filePath}: {ex.Message}");
            }
        }

        private static List<IndexEntry> ReadIndex(string idxPath)
        {
            var entries = new List<IndexEntry>();

            if (!File.Exists(idxPath))
                return entries;

            try
            {
                using (var fs = new FileStream(idxPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(fs))
                {
                    long count = fs.Length / 12;
                    for (long i = 0; i < count; i++)
                    {
                        entries.Add(new IndexEntry
                        {
                            Offset = reader.ReadInt32(),
                            Length = reader.ReadInt32(),
                            Extra = reader.ReadInt32()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AnimWriter: Exception reading index: {ex.Message}");
            }

            return entries;
        }

        private static void WriteIndex(string idxPath, List<IndexEntry> entries)
        {
            using (var fs = new FileStream(idxPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(fs))
            {
                foreach (var entry in entries)
                {
                    writer.Write(entry.Offset);
                    writer.Write(entry.Length);
                    writer.Write(entry.Extra);
                }
            }
        }

        private static int AppendToMulFile(string mulPath, byte[] data)
        {
            int offset;
            
            using (var fs = new FileStream(mulPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            {
                fs.Seek(0, SeekOrigin.End);
                offset = (int)fs.Position;
                fs.Write(data, 0, data.Length);
            }

            return offset;
        }

        private static byte[] EncodeAnimation(AnimReader.AnimationInfo animation)
        {
            try
            {
                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms))
                {
                    // Build palette from all frames
                    var palette = BuildPalette(animation.Frames);
                    
                    // Write palette (256 colors, RGB555 format)
                    foreach (var color in palette)
                    {
                        ushort rgb555 = ColorToRGB555(color);
                        writer.Write(rgb555);
                    }

                    // Write frame count
                    writer.Write(animation.Frames.Count);

                    // Reserve space for frame offsets
                    long frameOffsetsPos = ms.Position;
                    for (int i = 0; i < animation.Frames.Count; i++)
                    {
                        writer.Write(0); // Placeholder
                    }

                    // Write frame data and record offsets
                    var frameOffsets = new List<int>();
                    long dataStart = ms.Position;

                    foreach (var frame in animation.Frames)
                    {
                        frameOffsets.Add((int)(ms.Position - dataStart));
                        WriteFrame(writer, frame, palette);
                    }

                    // Go back and write actual frame offsets
                    ms.Seek(frameOffsetsPos, SeekOrigin.Begin);
                    foreach (var offset in frameOffsets)
                    {
                        writer.Write(offset);
                    }

                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AnimWriter: Exception encoding animation: {ex.Message}");
                return null;
            }
        }

        private static void WriteFrame(BinaryWriter writer, AnimReader.AnimationFrame frame, List<Color> palette)
        {
            if (frame.Image == null)
                return;

            // Write frame header
            writer.Write((short)frame.CenterX);
            writer.Write((short)frame.CenterY);
            writer.Write((ushort)frame.Image.Width);
            writer.Write((ushort)frame.Image.Height);

            // Convert image to palette indices
            var pixels = GetPixelData(frame.Image);
            var paletteIndices = ConvertToPaletteIndices(pixels, frame.Image.Width, frame.Image.Height, palette);

            // Write run-length encoded data
            WriteRLEData(writer, paletteIndices, frame.Image.Width, frame.Image.Height);
        }

        private static int[] GetPixelData(Bitmap image)
        {
            var rect = new Rectangle(0, 0, image.Width, image.Height);
            var bmpData = image.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int pixelCount = image.Width * image.Height;
                int[] pixels = new int[pixelCount];
                Marshal.Copy(bmpData.Scan0, pixels, 0, pixelCount);
                return pixels;
            }
            finally
            {
                image.UnlockBits(bmpData);
            }
        }

        private static byte[] ConvertToPaletteIndices(int[] pixels, int width, int height, List<Color> palette)
        {
            var indices = new byte[pixels.Length];
            
            for (int i = 0; i < pixels.Length; i++)
            {
                int argb = pixels[i];
                int alpha = (argb >> 24) & 0xFF;
                
                if (alpha < 128)
                {
                    indices[i] = 0; // Transparent
                }
                else
                {
                    Color color = Color.FromArgb(argb);
                    indices[i] = (byte)FindClosestPaletteIndex(color, palette);
                }
            }

            return indices;
        }

        private static void WriteRLEData(BinaryWriter writer, byte[] indices, int width, int height)
        {
            for (int y = 0; y < height; y++)
            {
                int x = 0;
                int rowStart = y * width;

                while (x < width)
                {
                    // Find run of similar pixels
                    if (indices[rowStart + x] == 0)
                    {
                        // Transparent run
                        int runLength = 1;
                        while (x + runLength < width && indices[rowStart + x + runLength] == 0 && runLength < 0x0FFF)
                        {
                            runLength++;
                        }

                        // Write compressed run header
                        int header = 0x8000 | runLength;
                        writer.Write(header);
                        x += runLength;
                    }
                    else
                    {
                        // Color run
                        int runLength = 1;
                        while (x + runLength < width && indices[rowStart + x + runLength] != 0 && runLength < 0x0FFF)
                        {
                            runLength++;
                        }

                        // Write uncompressed run header
                        int header = runLength;
                        writer.Write(header);

                        // Write pixel data
                        for (int i = 0; i < runLength; i++)
                        {
                            writer.Write(indices[rowStart + x + i]);
                        }

                        x += runLength;
                    }
                }

                // End of row marker
                writer.Write(0);
            }
        }

        private static List<Color> BuildPalette(List<AnimReader.AnimationFrame> frames)
        {
            var colorCounts = new Dictionary<Color, int>();

            foreach (var frame in frames)
            {
                if (frame.Image == null)
                    continue;

                var pixels = GetPixelData(frame.Image);
                foreach (int argb in pixels)
                {
                    int alpha = (argb >> 24) & 0xFF;
                    if (alpha < 128)
                        continue;

                    Color color = Color.FromArgb(argb);
                    if (colorCounts.ContainsKey(color))
                        colorCounts[color]++;
                    else
                        colorCounts[color] = 1;
                }
            }

            // Sort by frequency and take top 255 (index 0 is transparent)
            var palette = colorCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(255)
                .Select(kvp => kvp.Key)
                .ToList();

            // Add transparent color at index 0
            palette.Insert(0, Color.Transparent);

            // Fill remaining slots with black
            while (palette.Count < 256)
            {
                palette.Add(Color.Black);
            }

            return palette;
        }

        private static int FindClosestPaletteIndex(Color color, List<Color> palette)
        {
            int bestIndex = 0;
            int bestDistance = int.MaxValue;

            for (int i = 1; i < palette.Count; i++) // Skip 0 (transparent)
            {
                int distance = ColorDistance(color, palette[i]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static int ColorDistance(Color c1, Color c2)
        {
            int dr = c1.R - c2.R;
            int dg = c1.G - c2.G;
            int db = c1.B - c2.B;
            return dr * dr + dg * dg + db * db;
        }

        private static ushort ColorToRGB555(Color color)
        {
            if (color.A < 128)
                return 0;

            int r = (color.R * 31) / 255;
            int g = (color.G * 31) / 255;
            int b = (color.B * 31) / 255;

            return (ushort)((r << 10) | (g << 5) | b | 0x8000);
        }

        private struct IndexEntry
        {
            public int Offset;
            public int Length;
            public int Extra;
        }
    }
}
