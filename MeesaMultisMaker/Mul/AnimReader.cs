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
    /// Reads mobile animations from anim.mul, anim.idx, and anim2.mul, anim3.mul, anim4.mul, anim5.mul files
    /// 
    /// Animation format:
    /// - Each animation has multiple actions (walk, run, attack, etc.)
    /// - Each action has 5 directions (some files have 8)
    /// - Each direction has multiple frames
    /// - Frames are stored as palette-indexed compressed data
    /// </summary>
    public static class AnimReader
    {
        // Standard UO animation files
        private static readonly string[] AnimFiles = { "anim.mul", "anim2.mul", "anim3.mul", "anim4.mul", "anim5.mul" };
        private static readonly string[] IdxFiles = { "anim.idx", "anim2.idx", "anim3.idx", "anim4.idx", "anim5.idx" };

        // Cache of opened animation archives per folder
        private static readonly Dictionary<string, AnimArchive[]> archives = new Dictionary<string, AnimArchive[]>(StringComparer.OrdinalIgnoreCase);
        private static readonly object archivesLock = new object();

        public enum AnimationType
        {
            Monster = 0,    // anim.mul  (low detail)
            Monster2 = 1,   // anim2.mul (high detail) 
            Monster3 = 2,   // anim3.mul
            People = 3,     // anim4.mul (5 directions)
            People2 = 4     // anim5.mul (8 directions)
        }

        public class AnimationInfo
        {
            public int BodyId { get; set; }
            public int Action { get; set; }
            public int Direction { get; set; }
            public AnimationType Type { get; set; }
            public int FrameCount { get; set; }
            public List<AnimationFrame> Frames { get; set; }
        }

        public class AnimationFrame
        {
            public int FrameIndex { get; set; }
            public Bitmap Image { get; set; }
            public int CenterX { get; set; }
            public int CenterY { get; set; }
        }

        /// <summary>
        /// Load all frames for a specific animation sequence
        /// </summary>
        public static AnimationInfo LoadAnimation(string mulFolder, int bodyId, int action, int direction, AnimationType type = AnimationType.Monster)
        {
            try
            {
                var archives = GetOrCreateArchives(mulFolder);
                if (archives == null || archives.Length == 0)
                    return null;

                int fileIndex = (int)type;
                if (fileIndex < 0 || fileIndex >= archives.Length || archives[fileIndex] == null)
                    return null;

                return archives[fileIndex].ReadAnimation(bodyId, action, direction);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AnimReader: Exception loading animation Body={bodyId}, Action={action}, Dir={direction}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get available animation types for a body ID
        /// </summary>
        public static List<AnimationInfo> GetAvailableAnimations(string mulFolder, int bodyId)
        {
            var result = new List<AnimationInfo>();

            try
            {
                var archives = GetOrCreateArchives(mulFolder);
                if (archives == null)
                    return result;

                // Check all animation files
                for (int fileIndex = 0; fileIndex < archives.Length; fileIndex++)
                {
                    if (archives[fileIndex] == null)
                        continue;

                    var animations = archives[fileIndex].GetAnimationsForBody(bodyId);
                    foreach (var anim in animations)
                    {
                        anim.Type = (AnimationType)fileIndex;
                        result.Add(anim);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AnimReader: Exception getting animations for Body={bodyId}: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Get list of all body IDs that have animations
        /// </summary>
        public static List<int> GetAvailableBodyIds(string mulFolder, AnimationType type = AnimationType.Monster)
        {
            var result = new List<int>();

            try
            {
                var archives = GetOrCreateArchives(mulFolder);
                if (archives == null)
                    return result;

                int fileIndex = (int)type;
                if (fileIndex >= 0 && fileIndex < archives.Length && archives[fileIndex] != null)
                {
                    result = archives[fileIndex].GetValidBodyIds();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AnimReader: Exception getting body IDs: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Clear the archive cache
        /// </summary>
        public static void ClearCache()
        {
            lock (archivesLock)
            {
                foreach (var archiveSet in archives.Values)
                {
                    foreach (var archive in archiveSet)
                    {
                        archive?.Dispose();
                    }
                }
                archives.Clear();
            }
        }

        private static AnimArchive[] GetOrCreateArchives(string mulFolder)
        {
            lock (archivesLock)
            {
                if (archives.TryGetValue(mulFolder, out var existing))
                    return existing;

                var archiveSet = new AnimArchive[AnimFiles.Length];

                for (int i = 0; i < AnimFiles.Length; i++)
                {
                    string idxPath = Path.Combine(mulFolder, IdxFiles[i]);
                    string mulPath = Path.Combine(mulFolder, AnimFiles[i]);

                    if (File.Exists(idxPath) && File.Exists(mulPath))
                    {
                        try
                        {
                            archiveSet[i] = new AnimArchive(idxPath, mulPath, i);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"AnimReader: Failed to open {AnimFiles[i]}: {ex.Message}");
                        }
                    }
                }

                archives[mulFolder] = archiveSet;
                return archiveSet;
            }
        }

        /// <summary>
        /// Represents an opened anim.mul + anim.idx pair
        /// </summary>
        private class AnimArchive : IDisposable
        {
            private struct IndexEntry
            {
                public int Offset;
                public int Length;
                public int Extra;
            }

            private readonly IndexEntry[] indexEntries;
            private readonly FileStream mulStream;
            private readonly object streamLock = new object();
            private readonly int fileIndex;

            public AnimArchive(string idxPath, string mulPath, int fileIndex)
            {
                this.fileIndex = fileIndex;

                // Read index
                using (var idxStream = File.OpenRead(idxPath))
                using (var idxReader = new BinaryReader(idxStream))
                {
                    long count = idxStream.Length / 12;
                    if (count <= 0)
                        throw new InvalidDataException($"{Path.GetFileName(idxPath)} contains no entries");

                    indexEntries = new IndexEntry[count];

                    for (long i = 0; i < count; i++)
                    {
                        indexEntries[i].Offset = idxReader.ReadInt32();
                        indexEntries[i].Length = idxReader.ReadInt32();
                        indexEntries[i].Extra = idxReader.ReadInt32();
                    }
                }

                // Open mul stream
                mulStream = new FileStream(mulPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }

            public List<int> GetValidBodyIds()
            {
                var result = new HashSet<int>();

                // Animation index is: (BodyID * 110) + (Action * 5) + Direction
                // For people files with 8 directions: (BodyID * 175) + (Action * 5) + Direction
                int actionsPerBody = (fileIndex >= 3) ? 175 : 110; // People files have more actions

                for (int i = 0; i < indexEntries.Length; i++)
                {
                    var entry = indexEntries[i];
                    if (entry.Offset != -1 && entry.Offset >= 0 && entry.Length > 0)
                    {
                        int bodyId = i / actionsPerBody;
                        if (bodyId >= 0 && bodyId < 10000) // Reasonable limit
                        {
                            result.Add(bodyId);
                        }
                    }
                }

                var list = new List<int>(result);
                list.Sort();
                return list;
            }

            public List<AnimationInfo> GetAnimationsForBody(int bodyId)
            {
                var result = new List<AnimationInfo>();
                int actionsPerBody = (fileIndex >= 3) ? 175 : 110;
                int maxActions = (fileIndex >= 3) ? 35 : 22;
                int maxDirections = 5;

                int baseIndex = bodyId * actionsPerBody;

                for (int action = 0; action < maxActions; action++)
                {
                    for (int dir = 0; dir < maxDirections; dir++)
                    {
                        int index = baseIndex + (action * 5) + dir;
                        if (index >= 0 && index < indexEntries.Length)
                        {
                            var entry = indexEntries[index];
                            if (entry.Offset != -1 && entry.Length > 0)
                            {
                                result.Add(new AnimationInfo
                                {
                                    BodyId = bodyId,
                                    Action = action,
                                    Direction = dir
                                });
                            }
                        }
                    }
                }

                return result;
            }

            public AnimationInfo ReadAnimation(int bodyId, int action, int direction)
            {
                int actionsPerBody = (fileIndex >= 3) ? 175 : 110;
                int index = (bodyId * actionsPerBody) + (action * 5) + direction;

                System.Diagnostics.Debug.WriteLine($"AnimArchive.ReadAnimation: Start - Body={bodyId}, Action={action}, Dir={direction}, Index={index}");

                if (index < 0 || index >= indexEntries.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"AnimArchive.ReadAnimation: Index out of range");
                    return null;
                }

                var entry = indexEntries[index];
                System.Diagnostics.Debug.WriteLine($"AnimArchive.ReadAnimation: Entry - Offset={entry.Offset}, Length={entry.Length}");

                if (entry.Offset < 0 || entry.Length <= 0)
                {
                    System.Diagnostics.Debug.WriteLine($"AnimArchive.ReadAnimation: Invalid entry");
                    return null;
                }

                lock (streamLock)
                {
                    try
                    {
                        mulStream.Seek(entry.Offset, SeekOrigin.Begin);

                        using (var reader = new BinaryReader(mulStream, System.Text.Encoding.Default, leaveOpen: true))
                        {
                            // Read palette (256 colors * 2 bytes each = 512 bytes)
                            // Palette is stored as RGB555 with high bit as alpha
                            var palette = new ushort[256];
                            for (int i = 0; i < 256; i++)
                            {
                                palette[i] = (ushort)(reader.ReadUInt16() ^ 0x8000);
                            }

                            // CRITICAL: Save position AFTER palette, BEFORE frameCount
                            // This is the base for frame offset calculations!
                            long start = mulStream.Position;

                            int frameCount = reader.ReadInt32();
                            System.Diagnostics.Debug.WriteLine($"AnimArchive.ReadAnimation: Frame count = {frameCount}, start position (after palette) = {start}");

                            if (frameCount <= 0 || frameCount > 1024)
                            {
                                System.Diagnostics.Debug.WriteLine($"AnimArchive.ReadAnimation: Invalid frame count");
                                return null;
                            }

                            // Read frame lookup table - offsets are relative to 'start'
                            var frameLookup = new int[frameCount];
                            for (int i = 0; i < frameCount; i++)
                            {
                                frameLookup[i] = reader.ReadInt32();
                            }
                            
                            System.Diagnostics.Debug.WriteLine($"AnimArchive.ReadAnimation: First few frame offsets (relative to start): {string.Join(", ", frameLookup.Take(Math.Min(3, frameCount)))}");

                            var frames = new List<AnimationFrame>();

                            for (int f = 0; f < frameCount; f++)
                            {
                                if (frameLookup[f] <= 0)
                                {
                                    System.Diagnostics.Debug.WriteLine($"AnimArchive.ReadAnimation: Frame {f} has invalid offset {frameLookup[f]}");
                                    continue;
                                }

                                // Frame offsets are relative to START (position after palette)
                                long framePos = start + frameLookup[f];
                                
                                System.Diagnostics.Debug.WriteLine($"AnimArchive.ReadAnimation: Reading frame {f} at position {framePos} (start={start} + offset={frameLookup[f]})");
                                mulStream.Seek(framePos, SeekOrigin.Begin);

                                // Read frame header
                                short centerX = reader.ReadInt16();
                                short centerY = reader.ReadInt16();
                                ushort width = reader.ReadUInt16();
                                ushort height = reader.ReadUInt16();

                                System.Diagnostics.Debug.WriteLine($"AnimArchive.ReadAnimation: Frame {f} dims: {width}x{height}, center: ({centerX},{centerY})");

                                if (width <= 0 || height <= 0 || width > 1024 || height > 1024)
                                {
                                    System.Diagnostics.Debug.WriteLine($"AnimArchive.ReadAnimation: Frame {f} invalid dimensions");
                                    continue;
                                }

                                var frame = DecodeFrame(reader, width, height, centerX, centerY, palette);
                                if (frame != null)
                                {
                                    frames.Add(new AnimationFrame
                                    {
                                        FrameIndex = f,
                                        Image = frame,
                                        CenterX = centerX,
                                        CenterY = centerY
                                    });
                                    System.Diagnostics.Debug.WriteLine($"AnimArchive.ReadAnimation: Frame {f} decoded successfully");
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"AnimArchive.ReadAnimation: Frame {f} decode returned null");
                                }
                            }

                            System.Diagnostics.Debug.WriteLine($"AnimArchive.ReadAnimation: Total frames decoded: {frames.Count}");

                            if (frames.Count == 0)
                            {
                                System.Diagnostics.Debug.WriteLine($"AnimArchive.ReadAnimation: No frames were decoded, returning null");
                                return null;
                            }

                            return new AnimationInfo
                            {
                                BodyId = bodyId,
                                Action = action,
                                Direction = direction,
                                FrameCount = frames.Count,
                                Frames = frames
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"AnimArchive: Exception reading animation: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"AnimArchive: Stack trace: {ex.StackTrace}");
                        return null;
                    }
                }
            }

            private Bitmap DecodeFrame(BinaryReader reader, int width, int height, int centerX, int centerY, ushort[] palette)
            {
                try
                {
                    // Use 16-bit ARGB1555 format like UOFiddler does
                    var bmp = new Bitmap(width, height, PixelFormat.Format16bppArgb1555);
                    var bmpData = bmp.LockBits(
                        new Rectangle(0, 0, width, height), 
                        ImageLockMode.WriteOnly, 
                        PixelFormat.Format16bppArgb1555);

                    try
                    {
                        unsafe
                        {
                            ushort* line = (ushort*)bmpData.Scan0;
                            int delta = bmpData.Stride >> 1; // Stride in ushorts (divide by 2)

                            const int doubleXor = (0x200 << 22) | (0x200 << 12);
                            const int terminator = 0x7FFF7FFF;

                            // Calculate base offsets like UOFiddler does
                            int xBase = centerX - 0x200;
                            int yBase = (centerY + height) - 0x200;

                            // Offset the base line pointer
                            line += xBase;
                            line += yBase * delta;

                            // Decode run-length encoded data
                            int header;
                            while ((header = reader.ReadInt32()) != terminator)
                            {
                                header ^= doubleXor;

                                // Extract coordinates and run length from header
                                int x = (header >> 22) & 0x3FF;
                                int y = (header >> 12) & 0x3FF;
                                int runLength = header & 0xFFF;

                                // Calculate pointer position
                                ushort* cur = line + (y * delta) + x;
                                ushort* end = cur + runLength;

                                // Read palette indices and write colors directly
                                while (cur < end)
                                {
                                    byte paletteIndex = reader.ReadByte();
                                    *cur++ = palette[paletteIndex];
                                }
                            }
                        }
                    }
                    finally
                    {
                        bmp.UnlockBits(bmpData);
                    }

                    return bmp;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"DecodeFrame: Exception: {ex.Message}");
                    return null;
                }
            }

            public void Dispose()
            {
                mulStream?.Dispose();
            }
        }
    }
}
