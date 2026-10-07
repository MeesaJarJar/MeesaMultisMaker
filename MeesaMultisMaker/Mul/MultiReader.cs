using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace MeesaMultisMaker.Mul
{
    public class MultiComponent
    {
        public ushort TileId { get; set; }
        public short X { get; set; }
        public short Y { get; set; }
        public short Z { get; set; }
        public int Flags { get; set; } // signed as in UOFiddler
        public int Unk1 { get; set; } // HS only;0 for classic
    }

    public class MultiEntry
    {
        public int Index { get; set; }
        public int Offset { get; set; }
        public int Length { get; set; }
        public int Extra { get; set; }
        public int EntrySize { get; set; } //12 or16
        public List<MultiComponent> Components { get; set; } = new List<MultiComponent>();
        public override string ToString() => $"Multi {Index} ({Components.Count} parts)";
    }

    public static class MultiReader
    {
        private const string MULTI_UOP_FILENAME = "MultiCollection.uop";
        private const string MULTI_HASH_FORMAT = "build/multicollection/{0:D6}.bin";

        public static List<MultiEntry> Load(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentNullException(nameof(folder));
            var idxPath = Path.Combine(folder, "multi.idx");
            var mulPath = Path.Combine(folder, "multi.mul");
            var uopPath = Path.Combine(folder, MULTI_UOP_FILENAME);

            bool hasMul = File.Exists(idxPath) && File.Exists(mulPath);
            bool hasUop = File.Exists(uopPath);

            if (!hasMul && !hasUop)
                throw new FileNotFoundException("multi.idx/multi.mul and MultiCollection.uop not found in " + folder);

            if (hasMul)
            {
                return LoadFromMul(idxPath, mulPath);
            }
            else
            {
                return LoadFromUop(uopPath);
            }
        }

        private static List<MultiEntry> LoadFromMul(string idxPath, string mulPath)
        {
            var entries = new List<MultiEntry>();
            using (var idx = File.OpenRead(idxPath))
            using (var mul = File.OpenRead(mulPath))
            using (var idxReader = new BinaryReader(idx))
            using (var mulReader = new BinaryReader(mul))
            {
                int idxCount = (int)(idx.Length / 12);
                for (int i = 0; i < idxCount; i++)
                {
                    int offset = idxReader.ReadInt32();
                    int length = idxReader.ReadInt32();
                    int extra = idxReader.ReadInt32();
                    var entry = new MultiEntry { Index = i, Offset = offset, Length = length, Extra = extra };
                    if (offset < 0 || length <= 0)
                    {
                        entries.Add(entry);
                        continue;
                    }
                    mul.Seek(offset, SeekOrigin.Begin);
                    int entrySize = (length % 16 == 0) ? 16 : 12; // prefer HS when divisible by16
                    int count = length / entrySize;
                    entry.EntrySize = entrySize;
                    for (int c = 0; c < count; c++)
                    {
                        ushort id = mulReader.ReadUInt16();
                        short x = mulReader.ReadInt16();
                        short y = mulReader.ReadInt16();
                        short z = mulReader.ReadInt16();
                        int flags = mulReader.ReadInt32();
                        int unk1 = 0;
                        if (entrySize == 16)
                        {
                            unk1 = mulReader.ReadInt32();
                        }
                        entry.Components.Add(new MultiComponent { TileId = id, X = x, Y = y, Z = z, Flags = flags, Unk1 = unk1 });
                    }
                    entries.Add(entry);
                }
            }
            return entries;
        }

        private static List<MultiEntry> LoadFromUop(string uopPath)
        {
            System.Diagnostics.Debug.WriteLine($"MultiReader: Loading from UOP: {uopPath}");

            // Pre-compute hashes for multi indices
            var hashToIndex = new Dictionary<ulong, int>();
            for (int i = 0; i < 0x10000; i++)
            {
                string path = string.Format(MULTI_HASH_FORMAT, i);
                ulong hash = UopHashEngine.HashLittle2(path);
                hashToIndex[hash] = i;
            }

            var uopEntries = new Dictionary<int, UopMultiEntry>();

            using (var stream = File.OpenRead(uopPath))
            using (var reader = new BinaryReader(stream))
            {
                // Read UOP header
                uint signature = reader.ReadUInt32();
                if (signature != 0x0050594D)
                {
                    throw new InvalidDataException("Invalid UOP file signature in MultiCollection.uop");
                }

                int version = reader.ReadInt32();
                uint timestamp = reader.ReadUInt32();
                long startAddress = reader.ReadInt64();
                uint blockSizeCapacity = reader.ReadUInt32();
                int fileCount = reader.ReadInt32();

                System.Diagnostics.Debug.WriteLine($"MultiReader: UOP version={version}, fileCount={fileCount}");

                // Read all blocks
                long nextBlock = startAddress;
                int blocksRead = 0;

                while (nextBlock != 0 && blocksRead < 10000)
                {
                    stream.Seek(nextBlock, SeekOrigin.Begin);

                    int filesInBlock = reader.ReadInt32();
                    long nextBlockAddress = reader.ReadInt64();

                    if (filesInBlock < 0 || filesInBlock > 10000)
                        break;

                    for (int i = 0; i < filesInBlock; i++)
                    {
                        long offset = reader.ReadInt64();
                        int headerSize = reader.ReadInt32();
                        int compressedSize = reader.ReadInt32();
                        int decompressedSize = reader.ReadInt32();
                        ulong hash = reader.ReadUInt64();
                        uint adler = reader.ReadUInt32();
                        short compressionMethod = reader.ReadInt16();

                        if (offset == 0 || compressedSize == 0)
                            continue;

                        if (hashToIndex.TryGetValue(hash, out int entryIndex))
                        {
                            uopEntries[entryIndex] = new UopMultiEntry
                            {
                                Offset = offset + headerSize,
                                CompressedSize = compressedSize,
                                DecompressedSize = decompressedSize,
                                CompressionMethod = compressionMethod
                            };
                        }
                    }

                    nextBlock = nextBlockAddress;
                    blocksRead++;

                    if (nextBlock < 0 || nextBlock >= stream.Length)
                        break;
                }

                System.Diagnostics.Debug.WriteLine($"MultiReader: Found {uopEntries.Count} UOP multi entries from {blocksRead} blocks");

                // Find max index to size the output list
                int maxIndex = 0;
                foreach (var kvp in uopEntries)
                {
                    if (kvp.Key > maxIndex) maxIndex = kvp.Key;
                }

                var entries = new List<MultiEntry>();
                for (int i = 0; i <= maxIndex; i++)
                {
                    if (!uopEntries.TryGetValue(i, out var uopEntry))
                    {
                        entries.Add(new MultiEntry { Index = i, Offset = -1, Length = 0 });
                        continue;
                    }

                    stream.Seek(uopEntry.Offset, SeekOrigin.Begin);

                    byte[] data;
                    if (uopEntry.CompressionMethod != 0 && uopEntry.CompressedSize != uopEntry.DecompressedSize)
                    {
                        byte[] compressedData = reader.ReadBytes(uopEntry.CompressedSize);
                        data = Decompress(compressedData, uopEntry.DecompressedSize);
                    }
                    else
                    {
                        int size = uopEntry.DecompressedSize > 0 ? uopEntry.DecompressedSize : uopEntry.CompressedSize;
                        data = reader.ReadBytes(size);
                    }

                    var entry = new MultiEntry { Index = i, Offset = 0, Length = data?.Length ?? 0 };

                    if (data == null || data.Length < 4)
                    {
                        entries.Add(entry);
                        continue;
                    }

                    using (var dataStream = new MemoryStream(data))
                    using (var dataReader = new BinaryReader(dataStream))
                    {
                        // MultiCollection.uop uses the "Housing" bin format:
                        // uint id, uint count, then count * components
                        // Each component: ushort itemId, short x, short y, short z, ushort flags/unk
                        // But it may also just be raw MUL multi data.
                        // Try to detect the format:

                        // If length is divisible by 12 or 16 with no remainder, treat as raw MUL
                        // Otherwise try the UOP housing format (starts with uint id, uint count)
                        bool isHousingFormat = false;

                        if (data.Length >= 8)
                        {
                            // Peek at first 8 bytes
                            uint possibleId = BitConverter.ToUInt32(data, 0);
                            uint possibleCount = BitConverter.ToUInt32(data, 4);

                            // Housing format: id should match index, count should be reasonable
                            // and total size should be 8 + count * componentSize
                            long expectedSize12 = 8 + (long)possibleCount * 12;
                            long expectedSize16 = 8 + (long)possibleCount * 16;
                            long expectedSize14 = 8 + (long)possibleCount * 14;

                            if (possibleCount > 0 && possibleCount < 100000 &&
                                (data.Length == expectedSize12 || data.Length == expectedSize16 || data.Length == expectedSize14))
                            {
                                isHousingFormat = true;
                            }
                        }

                        if (isHousingFormat)
                        {
                            // Housing bin format
                            uint id = dataReader.ReadUInt32();
                            uint count = dataReader.ReadUInt32();

                            int remainingBytes = data.Length - 8;
                            int compSize = (count > 0) ? remainingBytes / (int)count : 0;
                            if (compSize < 12) compSize = 12;
                            entry.EntrySize = compSize;

                            for (uint c = 0; c < count; c++)
                            {
                                if (dataStream.Position + compSize > data.Length)
                                    break;

                                ushort tileId = dataReader.ReadUInt16();
                                short x = dataReader.ReadInt16();
                                short y = dataReader.ReadInt16();
                                short z = dataReader.ReadInt16();

                                int flags = 0;
                                int unk1 = 0;

                                if (compSize >= 12)
                                {
                                    flags = dataReader.ReadInt32();
                                }
                                if (compSize >= 16)
                                {
                                    unk1 = dataReader.ReadInt32();
                                }
                                // Skip any remaining bytes in the component
                                int skip = compSize - (compSize >= 16 ? 16 : (compSize >= 12 ? 12 : 8));
                                if (skip > 0)
                                    dataStream.Seek(skip, SeekOrigin.Current);

                                entry.Components.Add(new MultiComponent
                                {
                                    TileId = tileId,
                                    X = x,
                                    Y = y,
                                    Z = z,
                                    Flags = flags,
                                    Unk1 = unk1
                                });
                            }
                        }
                        else
                        {
                            // Raw MUL multi data (same as multi.mul format)
                            int entrySize = (data.Length % 16 == 0) ? 16 : 12;
                            int count = data.Length / entrySize;
                            entry.EntrySize = entrySize;

                            for (int c = 0; c < count; c++)
                            {
                                if (dataStream.Position + entrySize > data.Length)
                                    break;

                                ushort tileId = dataReader.ReadUInt16();
                                short x = dataReader.ReadInt16();
                                short y = dataReader.ReadInt16();
                                short z = dataReader.ReadInt16();
                                int flags = dataReader.ReadInt32();
                                int unk1 = 0;
                                if (entrySize == 16)
                                {
                                    unk1 = dataReader.ReadInt32();
                                }
                                entry.Components.Add(new MultiComponent
                                {
                                    TileId = tileId,
                                    X = x,
                                    Y = y,
                                    Z = z,
                                    Flags = flags,
                                    Unk1 = unk1
                                });
                            }
                        }
                    }

                    entries.Add(entry);
                }

                System.Diagnostics.Debug.WriteLine($"MultiReader: Loaded {entries.Count} multi entries from UOP");
                return entries;
            }
        }

        private static byte[] Decompress(byte[] data, int decompressedSize)
        {
            try
            {
                using (var input = new MemoryStream(data, 2, data.Length - 2))
                using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    deflate.CopyTo(output);
                    return output.ToArray();
                }
            }
            catch
            {
                return data;
            }
        }

        private class UopMultiEntry
        {
            public long Offset;
            public int CompressedSize;
            public int DecompressedSize;
            public short CompressionMethod;
        }
    }
}
