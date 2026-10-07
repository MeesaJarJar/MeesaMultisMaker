using System;
using System.Collections.Generic;
using System.IO;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Writes statics data to statics{N}.mul and staidx{N}.mul.
    /// Supports merging in-memory overrides with original disk data so that
    /// only blocks containing changed positions are re-encoded;
    /// unchanged blocks are copied verbatim for speed and safety.
    /// </summary>
    public static class StaticsWriter
    {
        private const int INDEX_ENTRY_SIZE = 12;
        private const int STATIC_ENTRY_SIZE = 7;

        /// <summary>
        /// Save statics files by merging <paramref name="overrides"/> into the
        /// original data found in <paramref name="originalFolder"/>.
        /// Writes to <paramref name="outputIdxPath"/> and <paramref name="outputMulPath"/>
        /// via temp files + atomic rename.
        /// </summary>
        public static void Save(
            string originalFolder,
            int mapIndex,
            string outputIdxPath,
            string outputMulPath,
            Dictionary<(int x, int y), List<StaticTile>> overrides)
        {
            string srcIdxPath = Path.Combine(originalFolder, $"staidx{mapIndex}.mul");
            string srcMulPath = Path.Combine(originalFolder, $"statics{mapIndex}.mul");

            if (!File.Exists(srcIdxPath) || !File.Exists(srcMulPath))
                throw new FileNotFoundException($"Source statics files not found for map {mapIndex} in {originalFolder}");

            // Determine block dimensions from the index file
            int blocksX, blocksY;
            StaticsReader.GetBlockDimensions(mapIndex, srcIdxPath, out blocksX, out blocksY);

            if (blocksX == 0 || blocksY == 0)
                throw new InvalidOperationException($"Could not determine block dimensions for map {mapIndex}");

            // Group overrides by their parent 8×8 block
            var overridesByBlock = new Dictionary<long, Dictionary<(int x, int y), List<StaticTile>>>();
            if (overrides != null)
            {
                foreach (var kvp in overrides)
                {
                    int bx = kvp.Key.x / 8;
                    int by = kvp.Key.y / 8;
                    long blockKey = (long)bx * blocksY + by;

                    if (!overridesByBlock.ContainsKey(blockKey))
                        overridesByBlock[blockKey] = new Dictionary<(int x, int y), List<StaticTile>>();
                    overridesByBlock[blockKey][kvp.Key] = kvp.Value;
                }
            }

            string tempMulPath = outputMulPath + ".tmp";
            string tempIdxPath = outputIdxPath + ".tmp";

            try
            {
                using (var srcIdx = File.OpenRead(srcIdxPath))
                using (var srcMul = File.OpenRead(srcMulPath))
                using (var srcIdxReader = new BinaryReader(srcIdx))
                using (var srcMulReader = new BinaryReader(srcMul))
                using (var outMul = File.Create(tempMulPath))
                using (var outIdx = File.Create(tempIdxPath))
                using (var idxWriter = new BinaryWriter(outIdx))
                using (var mulWriter = new BinaryWriter(outMul))
                {
                    int blocksModified = 0;
                    int blocksCopied = 0;

                    for (int bx = 0; bx < blocksX; bx++)
                    {
                        for (int by = 0; by < blocksY; by++)
                        {
                            long linearIndex = (long)bx * blocksY + by;
                            long indexOffset = linearIndex * INDEX_ENTRY_SIZE;

                            // Read original index entry
                            int origDataOffset = -1;
                            int origDataLength = 0;
                            int origExtra = 0;

                            if (indexOffset + INDEX_ENTRY_SIZE <= srcIdx.Length)
                            {
                                srcIdx.Seek(indexOffset, SeekOrigin.Begin);
                                origDataOffset = srcIdxReader.ReadInt32();
                                origDataLength = srcIdxReader.ReadInt32();
                                origExtra = srcIdxReader.ReadInt32();
                            }

                            bool hasOverrides = overridesByBlock.ContainsKey(linearIndex);

                            if (hasOverrides)
                            {
                                // Read original statics for this block into a dictionary
                                var blockStatics = new Dictionary<(int x, int y), List<StaticTile>>();

                                if (origDataOffset >= 0 && origDataLength > 0
                                    && origDataOffset + origDataLength <= srcMul.Length)
                                {
                                    srcMul.Seek(origDataOffset, SeekOrigin.Begin);
                                    int numStatics = origDataLength / STATIC_ENTRY_SIZE;

                                    for (int i = 0; i < numStatics; i++)
                                    {
                                        if (srcMul.Position + STATIC_ENTRY_SIZE > srcMul.Length) break;

                                        ushort itemId = srcMulReader.ReadUInt16();
                                        byte x = srcMulReader.ReadByte();
                                        byte y = srcMulReader.ReadByte();
                                        sbyte z = srcMulReader.ReadSByte();
                                        ushort hue = srcMulReader.ReadUInt16();

                                        int worldX = bx * 8 + x;
                                        int worldY = by * 8 + y;
                                        var posKey = (worldX, worldY);

                                        if (!blockStatics.ContainsKey(posKey))
                                            blockStatics[posKey] = new List<StaticTile>();

                                        blockStatics[posKey].Add(new StaticTile
                                        {
                                            ItemId = itemId,
                                            X = x,
                                            Y = y,
                                            Z = z,
                                            Hue = hue,
                                            WorldX = worldX,
                                            WorldY = worldY
                                        });
                                    }
                                }

                                // Apply overrides — replace entire position lists
                                foreach (var ovr in overridesByBlock[linearIndex])
                                {
                                    if (ovr.Value == null || ovr.Value.Count == 0)
                                        blockStatics.Remove(ovr.Key);
                                    else
                                        blockStatics[ovr.Key] = ovr.Value;
                                }

                                // Write merged statics for this block
                                long dataOffset = outMul.Position;
                                int dataLength = 0;

                                foreach (var posKvp in blockStatics)
                                {
                                    foreach (var s in posKvp.Value)
                                    {
                                        byte localX = (byte)(posKvp.Key.x % 8);
                                        byte localY = (byte)(posKvp.Key.y % 8);

                                        mulWriter.Write(s.ItemId);
                                        mulWriter.Write(localX);
                                        mulWriter.Write(localY);
                                        mulWriter.Write(s.Z);
                                        mulWriter.Write(s.Hue);
                                        dataLength += STATIC_ENTRY_SIZE;
                                    }
                                }

                                if (dataLength > 0)
                                {
                                    idxWriter.Write((int)dataOffset);
                                    idxWriter.Write(dataLength);
                                    idxWriter.Write(0);
                                }
                                else
                                {
                                    idxWriter.Write(-1);
                                    idxWriter.Write(-1);
                                    idxWriter.Write(-1);
                                }

                                blocksModified++;
                            }
                            else
                            {
                                // No overrides — copy original data verbatim
                                if (origDataOffset >= 0 && origDataLength > 0
                                    && origDataOffset + origDataLength <= srcMul.Length)
                                {
                                    long newOffset = outMul.Position;

                                    srcMul.Seek(origDataOffset, SeekOrigin.Begin);
                                    byte[] data = new byte[origDataLength];
                                    int bytesRead = srcMul.Read(data, 0, origDataLength);
                                    outMul.Write(data, 0, bytesRead);

                                    idxWriter.Write((int)newOffset);
                                    idxWriter.Write(origDataLength);
                                    idxWriter.Write(origExtra);
                                }
                                else
                                {
                                    idxWriter.Write(-1);
                                    idxWriter.Write(-1);
                                    idxWriter.Write(-1);
                                }

                                blocksCopied++;
                            }
                        }
                    }

                    System.Diagnostics.Debug.WriteLine(
                        $"StaticsWriter: {blocksModified} blocks modified, {blocksCopied} blocks copied verbatim");
                }

                // Atomic replace
                if (File.Exists(outputMulPath)) File.Delete(outputMulPath);
                File.Move(tempMulPath, outputMulPath);

                if (File.Exists(outputIdxPath)) File.Delete(outputIdxPath);
                File.Move(tempIdxPath, outputIdxPath);

                System.Diagnostics.Debug.WriteLine(
                    $"StaticsWriter: Saved statics for map {mapIndex} to {outputMulPath}");
            }
            catch
            {
                if (File.Exists(tempMulPath)) File.Delete(tempMulPath);
                if (File.Exists(tempIdxPath)) File.Delete(tempIdxPath);
                throw;
            }
        }

        /// <summary>
        /// Simple save that writes all statics from a <see cref="StaticsData"/> object
        /// (no merging with original files — used when all data is already in memory).
        /// </summary>
        public static void SaveFull(
            string outputIdxPath,
            string outputMulPath,
            StaticsData staticsData,
            int blocksX,
            int blocksY)
        {
            if (staticsData == null)
                throw new ArgumentNullException(nameof(staticsData));

            string tempMulPath = outputMulPath + ".tmp";
            string tempIdxPath = outputIdxPath + ".tmp";

            try
            {
                using (var outMul = File.Create(tempMulPath))
                using (var outIdx = File.Create(tempIdxPath))
                using (var idxWriter = new BinaryWriter(outIdx))
                using (var mulWriter = new BinaryWriter(outMul))
                {
                    for (int bx = 0; bx < blocksX; bx++)
                    {
                        for (int by = 0; by < blocksY; by++)
                        {
                            long dataOffset = outMul.Position;
                            int dataLength = 0;

                            // Write all statics within this block
                            for (int ty = 0; ty < 8; ty++)
                            {
                                for (int tx = 0; tx < 8; tx++)
                                {
                                    int worldX = bx * 8 + tx;
                                    int worldY = by * 8 + ty;

                                    var statics = staticsData.GetStaticsAt(worldX, worldY);
                                    foreach (var s in statics)
                                    {
                                        mulWriter.Write(s.ItemId);
                                        mulWriter.Write((byte)tx);
                                        mulWriter.Write((byte)ty);
                                        mulWriter.Write(s.Z);
                                        mulWriter.Write(s.Hue);
                                        dataLength += STATIC_ENTRY_SIZE;
                                    }
                                }
                            }

                            if (dataLength > 0)
                            {
                                idxWriter.Write((int)dataOffset);
                                idxWriter.Write(dataLength);
                                idxWriter.Write(0);
                            }
                            else
                            {
                                idxWriter.Write(-1);
                                idxWriter.Write(-1);
                                idxWriter.Write(-1);
                            }
                        }
                    }
                }

                if (File.Exists(outputMulPath)) File.Delete(outputMulPath);
                File.Move(tempMulPath, outputMulPath);
                if (File.Exists(outputIdxPath)) File.Delete(outputIdxPath);
                File.Move(tempIdxPath, outputIdxPath);
            }
            catch
            {
                if (File.Exists(tempMulPath)) File.Delete(tempMulPath);
                if (File.Exists(tempIdxPath)) File.Delete(tempIdxPath);
                throw;
            }
        }
    }
}
