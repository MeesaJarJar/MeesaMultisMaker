using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Reads GUMP art from gumpartLegacyMUL.uop format (UOForever style)
    /// UOP files are self-contained with their own index
    /// </summary>
    public static class UopGumpReader
    {
        private const string GUMP_LEGACY_UOP_FILENAME = "gumpartLegacyMUL.uop";
        
        // The correct hash format for gump UOP files
        // Uses .tga extension (NOT .bin as previously thought)
        private const string GUMP_HASH_FORMAT = "build/gumpartlegacymul/{0:D8}.tga";
        
        // Cache the UOP index to avoid re-parsing - use ConcurrentDictionary for thread safety
        private static ConcurrentDictionary<int, UopEntry> _indexCache = null;
        private static string _cachedUopPath = null;
        private static readonly object _cacheLock = new object();
        
        // Pre-computed hash table (computed once, reused)
        private static ConcurrentDictionary<ulong, int> _hashToIndex = null;
        private static readonly object _hashLock = new object();
        
        private class UopEntry
        {
            public long Offset;
            public int CompressedSize;
            public int DecompressedSize;
            public short CompressionMethod;
            public int Extra; // Packed dimensions from per-file header (width in high 16 bits, height in low 16 bits)
            public int HeaderSize; // Size of per-file header
            public long RawOffset; // Original offset before adding headerSize
        }

        /// <summary>
        /// Check if the UOP gump file exists
        /// </summary>
        public static bool UopFileExists(string mulFolder)
        {
            return File.Exists(Path.Combine(mulFolder, GUMP_LEGACY_UOP_FILENAME));
        }

        /// <summary>
        /// Try to get dimensions of a gump from the UOP file's per-file header.
        /// Returns false if dimensions aren't available without loading the full image.
        /// </summary>
        public static bool TryGetDimensions(string mulFolder, int gumpId, out int width, out int height)
        {
            width = 0;
            height = 0;

            UopEntry entry;
            lock (_cacheLock)
            {
                if (_indexCache == null) return false;
                if (!_indexCache.TryGetValue(gumpId, out entry)) return false;
            }

            // Read per-file header to get Extra field with packed dimensions
            if (entry.HeaderSize < 6)
                return false;

            try
            {
                string uopPath = Path.Combine(mulFolder, GUMP_LEGACY_UOP_FILENAME);
                using (var stream = File.OpenRead(uopPath))
                using (var reader = new BinaryReader(stream))
                {
                    stream.Seek(entry.RawOffset, SeekOrigin.Begin);
                    reader.ReadUInt16(); // version/flags
                    int extra = reader.ReadInt32();

                    int w = (extra >> 16) & 0xFFFF;
                    int h = extra & 0xFFFF;
                    if (w > 0 && h > 0 && w <= 4096 && h <= 4096)
                    {
                        width = w;
                        height = h;
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Ensure the hash table is pre-computed (can be called early to warm up)
        /// </summary>
        public static void PrecomputeHashes()
        {
            if (_hashToIndex != null) return;
            
            lock (_hashLock)
            {
                if (_hashToIndex != null) return;
                BuildHashTableParallel();
            }
        }
        
        /// <summary>
        /// Build hash table in parallel for faster startup
        /// </summary>
        private static void BuildHashTableParallel()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            var hashTable = new ConcurrentDictionary<ulong, int>();
            
            // Use parallel processing to compute hashes faster
            Parallel.For(0, 0x10000, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
            {
                string path = string.Format(GUMP_HASH_FORMAT, i);
                ulong hash = HashLittle2(path);
                hashTable[hash] = i;
            });
            
            _hashToIndex = hashTable;
            
            sw.Stop();
            System.Diagnostics.Debug.WriteLine($"UopGumpReader: Hash table built in {sw.ElapsedMilliseconds}ms with {hashTable.Count} entries");
            
            // Log a sample hash for verification
            string samplePath = string.Format(GUMP_HASH_FORMAT, 0);
            ulong sampleHash = HashLittle2(samplePath);
            System.Diagnostics.Debug.WriteLine($"UopGumpReader: Sample hash for '{samplePath}' = 0x{sampleHash:X16}");
        }

        /// <summary>
        /// Get all valid GUMP IDs from the UOP index (synchronous)
        /// </summary>
        public static List<int> GetValidGumpIds(string mulFolder)
        {
            var validIds = new List<int>();
            string uopPath = Path.Combine(mulFolder, GUMP_LEGACY_UOP_FILENAME);
            
            if (!File.Exists(uopPath))
            {
                System.Diagnostics.Debug.WriteLine($"UopGumpReader: File not found: {uopPath}");
                return validIds;
            }
            
            System.Diagnostics.Debug.WriteLine($"UopGumpReader: Reading from {uopPath} (size: {new FileInfo(uopPath).Length} bytes)");
            
            // Build/refresh cache if needed
            lock (_cacheLock)
            {
                if (_indexCache == null || _cachedUopPath != uopPath)
                {
                    System.Diagnostics.Debug.WriteLine($"UopGumpReader: Building index cache...");
                    _indexCache = BuildUopIndex(uopPath);
                    _cachedUopPath = uopPath;
                    System.Diagnostics.Debug.WriteLine($"UopGumpReader: Index cache built with {_indexCache.Count} entries");
                }
            }
            
            // Extract all valid gump IDs from the index
            validIds.AddRange(_indexCache.Keys);
            validIds.Sort();
            
            System.Diagnostics.Debug.WriteLine($"UopGumpReader: Returning {validIds.Count} valid gump IDs");
            return validIds;
        }
        
        /// <summary>
        /// Get all valid GUMP IDs from the UOP index (async version)
        /// </summary>
        public static Task<List<int>> GetValidGumpIdsAsync(string mulFolder, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                var validIds = new List<int>();
                string uopPath = Path.Combine(mulFolder, GUMP_LEGACY_UOP_FILENAME);
                
                if (!File.Exists(uopPath))
                    return validIds;
                
                cancellationToken.ThrowIfCancellationRequested();
                
                // Build/refresh cache if needed
                lock (_cacheLock)
                {
                    if (_indexCache == null || _cachedUopPath != uopPath)
                    {
                        _indexCache = BuildUopIndex(uopPath);
                        _cachedUopPath = uopPath;
                    }
                }
                
                cancellationToken.ThrowIfCancellationRequested();
                
                // Extract all valid gump IDs from the index
                validIds.AddRange(_indexCache.Keys);
                validIds.Sort();
                
                System.Diagnostics.Debug.WriteLine($"UopGumpReader: Found {validIds.Count} valid gump IDs");
                return validIds;
            }, cancellationToken);
        }

        // Diagnostic counter - log first N load attempts to Trace output
        private static int _diagCount = 0;
        private const int DIAG_MAX = 3;

        /// <summary>
        /// Load a GUMP image from gumpartLegacyMUL.uop
        /// </summary>
        public static Bitmap LoadGumpArt(string mulFolder, int gumpId)
        {
            string uopPath = Path.Combine(mulFolder, GUMP_LEGACY_UOP_FILENAME);

            if (!File.Exists(uopPath))
            {
                return null;
            }

            // Build/refresh cache if needed
            lock (_cacheLock)
            {
                if (_indexCache == null || _cachedUopPath != uopPath)
                {
                    _indexCache = BuildUopIndex(uopPath);
                    _cachedUopPath = uopPath;
                }
            }

            if (!_indexCache.TryGetValue(gumpId, out var entry))
            {
                return null;
            }

            bool diag = _diagCount < DIAG_MAX;

            try
            {
                using (var stream = File.OpenRead(uopPath))
                using (var reader = new BinaryReader(stream))
                {
                    // Read per-file header to extract Extra field (gump dimensions)
                    int extra = 0;
                    if (entry.HeaderSize > 0)
                    {
                        stream.Seek(entry.RawOffset, SeekOrigin.Begin);
                        try
                        {
                            byte[] hdrBytes = reader.ReadBytes(entry.HeaderSize);
                            if (diag)
                            {
                                string hex = BitConverter.ToString(hdrBytes).Replace("-", " ");
                                System.Diagnostics.Trace.WriteLine($"[UOP DIAG] Gump 0x{gumpId:X4}: headerSize={entry.HeaderSize}, raw header bytes=[{hex}]");

                                // Log as dwords for analysis
                                for (int d = 0; d + 3 < hdrBytes.Length; d += 4)
                                {
                                    int val = BitConverter.ToInt32(hdrBytes, d);
                                    System.Diagnostics.Trace.WriteLine($"[UOP DIAG]   header offset {d}: 0x{val:X8} = {val} (as w/h: {(val >> 16) & 0xFFFF}x{val & 0xFFFF})");
                                }
                            }

                            // Per-file header for gumps: [type:2][extra:4] (offset 2) or [type:2][unknown:2][extra:4] (offset 4)
                            // Try offset 2 first — standard format used by ClassicUO: [flags:2][extra:4]
                            if (hdrBytes.Length >= 6)
                            {
                                int extraAt2 = BitConverter.ToInt32(hdrBytes, 2);
                                int w2 = (extraAt2 >> 16) & 0xFFFF;
                                int h2 = extraAt2 & 0xFFFF;

                                if (w2 > 0 && h2 > 0 && w2 <= 4096 && h2 <= 4096 && entry.DecompressedSize >= h2 * 4)
                                {
                                    extra = extraAt2;
                                }
                                else if (hdrBytes.Length >= 8)
                                {
                                    // Try offset 4 — alternative format: [flags:2][unknown:2][extra:4]
                                    int extraAt4 = BitConverter.ToInt32(hdrBytes, 4);
                                    int w4 = (extraAt4 >> 16) & 0xFFFF;
                                    int h4 = extraAt4 & 0xFFFF;

                                    if (w4 > 0 && h4 > 0 && w4 <= 4096 && h4 <= 4096 && entry.DecompressedSize >= h4 * 4)
                                    {
                                        extra = extraAt4;
                                    }
                                }
                            }

                            if (diag)
                                System.Diagnostics.Trace.WriteLine($"[UOP DIAG] Gump 0x{gumpId:X4}: chosen extra=0x{extra:X8}, w={(extra>>16)&0xFFFF}, h={extra&0xFFFF}");
                        }
                        catch (Exception ex)
                        {
                            if (diag)
                                System.Diagnostics.Trace.WriteLine($"[UOP DIAG] Gump 0x{gumpId:X4}: Failed reading per-file header: {ex.Message}");
                        }
                    }
                    else if (diag)
                    {
                        System.Diagnostics.Trace.WriteLine($"[UOP DIAG] Gump 0x{gumpId:X4}: headerSize=0 (no per-file header)");
                    }

                    // Read the actual data
                    stream.Seek(entry.Offset, SeekOrigin.Begin);

                    byte[] data;
                    if (entry.CompressionMethod != 0 && entry.CompressedSize != entry.DecompressedSize)
                    {
                        // Data is compressed - decompress it
                        byte[] compressedData = reader.ReadBytes(entry.CompressedSize);
                        if (diag)
                            System.Diagnostics.Trace.WriteLine($"[UOP DIAG] Gump 0x{gumpId:X4}: compressed, method={entry.CompressionMethod}, compSz={entry.CompressedSize}, decompSz={entry.DecompressedSize}, first2bytes=0x{(compressedData.Length>=2 ? compressedData[0].ToString("X2")+compressedData[1].ToString("X2") : "??")}");

                        data = Decompress(compressedData, entry.DecompressedSize);
                        if (data == null)
                        {
                            if (diag)
                                System.Diagnostics.Trace.WriteLine($"[UOP DIAG] Gump 0x{gumpId:X4}: Decompression FAILED");
                            _diagCount++;
                            return null;
                        }
                        else if (diag)
                        {
                            System.Diagnostics.Trace.WriteLine($"[UOP DIAG] Gump 0x{gumpId:X4}: Decompressed OK, {compressedData.Length} -> {data.Length} bytes");
                        }
                    }
                    else
                    {
                        // Data is not compressed
                        int size = entry.DecompressedSize > 0 ? entry.DecompressedSize : entry.CompressedSize;
                        data = reader.ReadBytes(size);
                        if (diag)
                            System.Diagnostics.Trace.WriteLine($"[UOP DIAG] Gump 0x{gumpId:X4}: uncompressed, size={size}, read={data.Length} bytes");
                    }

                    if (data == null || data.Length < 8)
                    {
                        if (diag)
                            System.Diagnostics.Trace.WriteLine($"[UOP DIAG] Gump 0x{gumpId:X4}: data too small ({data?.Length ?? 0} bytes)");
                        _diagCount++;
                        return null;
                    }

                    if (diag)
                    {
                        // Log first 5 dwords for format detection analysis
                        var dwords = new int[Math.Min(5, data.Length / 4)];
                        for (int i = 0; i < dwords.Length; i++)
                            dwords[i] = BitConverter.ToInt32(data, i * 4);
                        System.Diagnostics.Trace.WriteLine($"[UOP DIAG] Gump 0x{gumpId:X4}: dataLen={data.Length}, first dwords=[{string.Join(",", dwords)}], extra=0x{extra:X8}");
                    }

                    // Parse the gump data from the byte array
                    var result = ParseGumpData(data, gumpId, extra);

                    if (diag)
                    {
                        if (result != null)
                            System.Diagnostics.Trace.WriteLine($"[UOP DIAG] Gump 0x{gumpId:X4}: ParseGumpData SUCCESS -> {result.Width}x{result.Height}");
                        else
                            System.Diagnostics.Trace.WriteLine($"[UOP DIAG] Gump 0x{gumpId:X4}: ParseGumpData returned NULL");
                        _diagCount++;
                    }

                    return result;
                }
            }
            catch (Exception ex)
            {
                if (diag)
                {
                    System.Diagnostics.Trace.WriteLine($"[UOP DIAG] Gump 0x{gumpId:X4}: EXCEPTION: {ex.Message}");
                    _diagCount++;
                }
                return null;
            }
        }

        /// <summary>
        /// Parse GUMP data from raw bytes.
        /// Supports multiple data layouts:
        ///  1. [lookup_table][rle_data] — raw MUL-format data where dimensions come from the Extra field
        ///  2. [width:4][height:4][lookup_table][rle_data] — some UO clients embed dimensions in the data
        /// Detection heuristic: In raw MUL format, lookupTable[0] == height (row 0 data starts right
        /// after the lookup table). In embedded format, data[4..7] (height) == data[8..11] (lookupTable[0]).
        /// </summary>
        private static Bitmap ParseGumpData(byte[] data, int gumpId, int extra = 0)
        {
            if (data == null || data.Length < 8)
            {
                return null;
            }

            try
            {
                int width, height;
                int dataOffset; // where the lookup table starts in data[]

                // Read key dwords for format detection
                int dword0 = BitConverter.ToInt32(data, 0);
                int dword1 = BitConverter.ToInt32(data, 4);
                int dword2 = data.Length >= 12 ? BitConverter.ToInt32(data, 8) : -1;

                // Strategy 1: Use Extra field from per-file header, validated against lookupTable[0]
                int wExtra = (extra >> 16) & 0xFFFF;
                int hExtra = extra & 0xFFFF;
                bool extraDimsValid = wExtra > 0 && hExtra > 0 && wExtra <= 4096 && hExtra <= 4096
                                      && data.Length >= hExtra * 4
                                      && dword0 == hExtra; // lookupTable[0] must equal height

                // Strategy 2: Detect embedded [width:4][height:4] prefix
                // Key: if data has embedded dims, then data[4..7] (height) should equal data[8..11] (lookupTable[0])
                // because lookupTable[0] = height (in DWORDs, pointing past the lookup table)
                bool embeddedDimsValid = dword2 >= 0 && dword1 > 0 && dword0 > 0
                                         && dword0 <= 4096 && dword1 <= 4096
                                         && dword1 == dword2  // height field == first lookup entry
                                         && data.Length >= 8 + dword1 * 4;

                // Strategy 3: Raw MUL format — lookupTable[0] == height
                // In raw format, dword0 is the first lookup table entry which equals height
                bool rawMulValid = dword0 > 0 && dword0 <= 4096 && data.Length >= dword0 * 4;

                if (extraDimsValid)
                {
                    // Dimensions from per-file header Extra field, validated against lookupTable[0]
                    width = wExtra;
                    height = hExtra;
                    dataOffset = 0;
                }
                else if (embeddedDimsValid)
                {
                    // Data starts with explicit [width][height]
                    width = dword0;
                    height = dword1;
                    dataOffset = 8;
                }
                else if (rawMulValid)
                {
                    // Raw MUL format — derive height from lookupTable[0], width from RLE
                    height = dword0;
                    width = DeriveWidthFromRLE(data, 0, height);
                    if (width <= 0 || width > 4096)
                    {
                        if (!TryScanForDimensions(data, out width, out height, out dataOffset))
                        {
                            System.Diagnostics.Debug.WriteLine($"UopGumpReader: Cannot derive width for gump 0x{gumpId:X4} (derived height={dword0})");
                            return null;
                        }
                    }
                    else
                    {
                        dataOffset = 0;
                    }
                }
                else
                {
                    // No standard strategy worked; scan for dimensions in the data
                    if (!TryScanForDimensions(data, out width, out height, out dataOffset))
                    {
                        System.Diagnostics.Debug.WriteLine($"UopGumpReader: Cannot determine dimensions for gump 0x{gumpId:X4} (d0={dword0},d1={dword1},d2={dword2},extra=0x{extra:X8},dataLen={data.Length})");
                        return null;
                    }
                }

                // Sanity check: make sure we have enough data for the lookup table
                int lookupTableSize = height * 4;
                if (data.Length < dataOffset + lookupTableSize)
                {
                    return null;
                }

                // Buffer starts at the lookup table
                var rleBuffer = new byte[data.Length - dataOffset];
                Buffer.BlockCopy(data, dataOffset, rleBuffer, 0, rleBuffer.Length);

                // Read the lookup table (one int32 per row) from rleBuffer
                int[] lookupTable = new int[height];
                for (int i = 0; i < height; i++)
                {
                    lookupTable[i] = BitConverter.ToInt32(rleBuffer, i * 4);
                }

                // Create bitmap directly
                var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                var rect = new Rectangle(0, 0, width, height);
                var bmpData = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                
                try
                {
                    int stride = bmpData.Stride;
                    IntPtr scan0 = bmpData.Scan0;
                    
                    // Decode each row using RLE; offsets are DWORDs from start of rleBuffer
                    for (int y = 0; y < height; y++)
                    {
                        int rowStart = lookupTable[y] * 4;
                        
                        if (rowStart < 0 || rowStart >= rleBuffer.Length)
                        {
                            continue;
                        }
                        
                        int dataPos = rowStart;
                        int x = 0;
                        IntPtr rowPtr = IntPtr.Add(scan0, y * stride);
                        
                        while (x < width && dataPos + 4 <= rleBuffer.Length)
                        {
                            ushort color16 = (ushort)(rleBuffer[dataPos] | (rleBuffer[dataPos + 1] << 8));
                            ushort runLength = (ushort)(rleBuffer[dataPos + 2] | (rleBuffer[dataPos + 3] << 8));
                            dataPos += 4;
                            
                            if (runLength == 0)
                                break;
                            
                            int actualRun = Math.Min((int)runLength, width - x);
                            
                            if (color16 == 0)
                            {
                                x += actualRun;
                            }
                            else
                            {
                                color16 ^= 0x8000;
                                
                                int r = ((color16 >> 10) & 0x1F);
                                int g = ((color16 >> 5) & 0x1F);
                                int b = (color16 & 0x1F);
                                
                                r = (r << 3) | (r >> 2);
                                g = (g << 3) | (g >> 2);
                                b = (b << 3) | (b >> 2);
                                
                                int argb = (255 << 24) | (r << 16) | (g << 8) | b;
                                
                                IntPtr pixelPtr = IntPtr.Add(rowPtr, x * 4);
                                for (int i = 0; i < actualRun; i++)
                                {
                                    Marshal.WriteInt32(pixelPtr, argb);
                                    pixelPtr = IntPtr.Add(pixelPtr, 4);
                                }
                                
                                x += actualRun;
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
                System.Diagnostics.Debug.WriteLine($"UopGumpReader: Exception parsing gump 0x{gumpId:X4}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Scan the decompressed data for embedded dimensions at variable offsets.
        /// Some UOP formats prepend metadata (e.g., hash, offsets) before the standard MUL data.
        /// Strategy A: Look for [width:4][height:4] followed by lookupTable[0]==height.
        /// Strategy B: Look for raw MUL start (lookupTable[0]=height) at variable offset, derive width from RLE.
        /// </summary>
        private static bool TryScanForDimensions(byte[] data, out int width, out int height, out int dataOffset)
        {
            width = 0;
            height = 0;
            dataOffset = 0;

            int maxScanBytes = Math.Min(128, data.Length - 12);
            if (maxScanBytes < 0) return false;

            // Strategy A: Look for [width:4][height:4] where the next dword (lookupTable[0]) == height
            for (int off = 0; off <= maxScanBytes; off += 4)
            {
                if (off + 12 > data.Length) break;

                int w = BitConverter.ToInt32(data, off);
                int h = BitConverter.ToInt32(data, off + 4);

                if (w > 0 && w <= 4096 && h > 0 && h <= 4096
                    && data.Length >= off + 8 + h * 4)
                {
                    int lt0 = BitConverter.ToInt32(data, off + 8);
                    if (lt0 == h)
                    {
                        width = w;
                        height = h;
                        dataOffset = off + 8;
                        return true;
                    }
                }
            }

            // Strategy B: Look for raw MUL format at a variable offset
            for (int off = 4; off <= maxScanBytes; off += 4)
            {
                if (off + 4 > data.Length) break;

                int h = BitConverter.ToInt32(data, off);
                if (h > 0 && h <= 4096 && data.Length >= off + h * 4)
                {
                    int w = DeriveWidthFromRLE(data, off, h);
                    if (w > 0 && w <= 4096)
                    {
                        width = w;
                        height = h;
                        dataOffset = off;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Derive the width of a gump from the first row's RLE data when dimensions aren't available.
        /// Uses lookupTable[1] to bound the scan to just row 0's data.
        /// </summary>
        private static int DeriveWidthFromRLE(byte[] data, int dataOffset, int height)
        {
            try
            {
                if (height < 1) return 0;

                // The lookup table starts at dataOffset
                // lookupTable[0] tells us where first row data starts (in DWORDs from dataOffset)
                int firstRowDwordOffset = BitConverter.ToInt32(data, dataOffset);
                int rowStart = dataOffset + firstRowDwordOffset * 4;

                if (rowStart < 0 || rowStart >= data.Length)
                    return 0;

                // Determine where row 0's data ends
                int rowEnd;
                if (height >= 2)
                {
                    int secondRowDwordOffset = BitConverter.ToInt32(data, dataOffset + 4);
                    rowEnd = dataOffset + secondRowDwordOffset * 4;
                    if (rowEnd < rowStart || rowEnd > data.Length)
                        rowEnd = data.Length;
                }
                else
                {
                    rowEnd = data.Length;
                }

                // Sum run lengths of first row to get width
                int pos = rowStart;
                int width = 0;
                while (pos + 4 <= rowEnd)
                {
                    ushort run = (ushort)(data[pos + 2] | (data[pos + 3] << 8));
                    pos += 4;

                    width += run;

                    if (width > 4096)
                        return 0;
                }

                return width;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Build index of all entries in the UOP file using hash lookup
        /// </summary>
        private static ConcurrentDictionary<int, UopEntry> BuildUopIndex(string uopPath)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            var index = new ConcurrentDictionary<int, UopEntry>();
            
            // Ensure hash table is built (parallel)
            PrecomputeHashes();
            
            System.Diagnostics.Debug.WriteLine($"UopGumpReader: Building UOP index from {Path.GetFileName(uopPath)}...");
            
            int totalEntries = 0;
            int matchedEntries = 0;
            var unmatchedHashes = new List<ulong>();
            
            using (var stream = File.OpenRead(uopPath))
            using (var reader = new BinaryReader(stream))
            {
                // Read UOP header
                uint signature = reader.ReadUInt32();
                int version = reader.ReadInt32();
                uint timestamp = reader.ReadUInt32();
                long startAddress = reader.ReadInt64();
                uint blockSize = reader.ReadUInt32();
                int fileCount = reader.ReadInt32();
                
                System.Diagnostics.Debug.WriteLine($"UopGumpReader: Signature=0x{signature:X8}, Version={version}, FileCount={fileCount}, StartAddress={startAddress}");
                
                if (signature != 0x0050594D) // "MYP\0"
                {
                    System.Diagnostics.Debug.WriteLine($"UopGumpReader: Invalid UOP signature (expected 0x0050594D, got 0x{signature:X8})");
                    return index;
                }
                
                // Read all blocks
                long nextBlock = startAddress;
                int blocksRead = 0;
                
                while (nextBlock != 0 && blocksRead < 10000)
                {
                    stream.Seek(nextBlock, SeekOrigin.Begin);
                    
                    int filesInBlock = reader.ReadInt32();
                    long nextBlockAddress = reader.ReadInt64();
                    
                    if (filesInBlock < 0 || filesInBlock > 1000)
                    {
                        System.Diagnostics.Debug.WriteLine($"UopGumpReader: Invalid filesInBlock={filesInBlock} at block {blocksRead}");
                        break;
                    }
                    
                    for (int i = 0; i < filesInBlock; i++)
                    {
                        long offset = reader.ReadInt64();
                        int headerSize = reader.ReadInt32();
                        int compressedSize = reader.ReadInt32();
                        int decompressedSize = reader.ReadInt32();
                        ulong hash = reader.ReadUInt64();
                        uint adler = reader.ReadUInt32();
                        short compressionMethod = reader.ReadInt16();
                        
                        totalEntries++;
                        
                        if (offset == 0 || compressedSize == 0)
                            continue;
                        
                        if (_hashToIndex.TryGetValue(hash, out int gumpIndex))
                        {
                            matchedEntries++;

                            // Store entry info — per-file header will be read lazily in LoadGumpArt
                            // (seeking away during block iteration can corrupt the sequential read)
                            index[gumpIndex] = new UopEntry
                            {
                                Offset = offset + headerSize,
                                CompressedSize = compressedSize,
                                DecompressedSize = decompressedSize,
                                CompressionMethod = compressionMethod,
                                Extra = 0, // Will be read from per-file header on demand
                                HeaderSize = headerSize,
                                RawOffset = offset
                            };
                        }
                        else
                        {
                            // Collect first few unmatched hashes for debugging
                            if (unmatchedHashes.Count < 10)
                            {
                                unmatchedHashes.Add(hash);
                            }
                        }
                    }
                    
                    nextBlock = nextBlockAddress;
                    blocksRead++;
                    
                    if (nextBlock < 0 || nextBlock >= stream.Length)
                        break;
                }
                
                sw.Stop();
                System.Diagnostics.Trace.WriteLine($"[UOP DIAG] BuildUopIndex: Indexed {index.Count} entries from {blocksRead} blocks ({totalEntries} total, {matchedEntries} matched) in {sw.ElapsedMilliseconds}ms");

                // Log a sample entry for diagnostics
                if (index.Count > 0)
                {
                    // Find first valid entry
                    foreach (var kvp in index)
                    {
                        var e = kvp.Value;
                        System.Diagnostics.Trace.WriteLine($"[UOP DIAG] Sample entry 0x{kvp.Key:X4}: offset={e.Offset}, rawOffset={e.RawOffset}, headerSize={e.HeaderSize}, compSz={e.CompressedSize}, decompSz={e.DecompressedSize}, compress={e.CompressionMethod}");
                        break;
                    }
                }

                // If no matches, log some unmatched hashes for debugging
                if (matchedEntries == 0 && unmatchedHashes.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"UopGumpReader: WARNING - No hashes matched! Sample unmatched hashes:");
                    foreach (var h in unmatchedHashes)
                    {
                        System.Diagnostics.Debug.WriteLine($"  Hash: 0x{h:X16}");
                    }
                    
                    // Try to reverse-engineer by brute force matching first hash
                    TryIdentifyHashFormat(unmatchedHashes[0]);
                }
            }
            
            return index;
        }
        
        /// <summary>
        /// Try to identify the hash format by testing various path patterns
        /// </summary>
        private static void TryIdentifyHashFormat(ulong targetHash)
        {
            System.Diagnostics.Debug.WriteLine($"UopGumpReader: Attempting to identify hash format for 0x{targetHash:X16}...");
            
            // Common path patterns used by different UO clients
            string[] pathPatterns = new string[]
            {
                "build/gumpartlegacymul/{0:D8}.bin",
                "build/gumpartlegacymul/{0:D8}.tga",
                "build/gumpartlegacymul/{0:D5}.bin",
                "build/gumpartlegacymul/{0:D5}.tga",
                "build/gumpartlegacymul/{0}.bin",
                "build/gumpartlegacymul/{0}.tga",
                "build/gumpart/{0:D8}.bin",
                "build/gumpart/{0:D8}.tga",
                "build/gumpart/{0:D5}.bin",
                "build/gumpart/{0}.bin",
                "gumpart/{0:D8}.bin",
                "gumpart/{0}.bin",
            };
            
            // Try each pattern with indices 0-1000
            foreach (var pattern in pathPatterns)
            {
                for (int idx = 0; idx <= 1000; idx++)
                {
                    string path = string.Format(pattern, idx);
                    ulong hash = HashLittle2(path);
                    
                    if (hash == targetHash)
                    {
                        System.Diagnostics.Debug.WriteLine($"UopGumpReader: FOUND MATCH! Pattern: \"{pattern}\" with index {idx}");
                        System.Diagnostics.Debug.WriteLine($"UopGumpReader: Full path: \"{path}\"");
                        return;
                    }
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"UopGumpReader: Could not identify hash format");
        }
        
        /// <summary>
        /// Decompress zlib/deflate data
        /// </summary>
        private static byte[] Decompress(byte[] data, int decompressedSize)
        {
            if (data == null || data.Length < 2)
                return null;

            // Try zlib format (2-byte header: CMF + FLG)
            // CMF low nibble = 8 (deflate), high nibble = window size log2 - 8
            // Valid CMF bytes: 0x08, 0x18, 0x28, 0x38, 0x48, 0x58, 0x68, 0x78
            if ((data[0] & 0x0F) == 0x08 && (data[0] & 0x80) == 0)
            {
                try
                {
                    using (var input = new MemoryStream(data, 2, data.Length - 2))
                    using (var deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress))
                    using (var output = new MemoryStream(decompressedSize > 0 ? decompressedSize : data.Length * 2))
                    {
                        deflate.CopyTo(output);
                        var result = output.ToArray();
                        if (result.Length > 0)
                            return result;
                    }
                }
                catch { }
            }

            // Try raw deflate (no header)
            try
            {
                using (var input = new MemoryStream(data))
                using (var deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress))
                using (var output = new MemoryStream(decompressedSize > 0 ? decompressedSize : data.Length * 2))
                {
                    deflate.CopyTo(output);
                    var result = output.ToArray();
                    if (result.Length > 0)
                        return result;
                }
            }
            catch { }

            return null;
        }
        
        /// <summary>
        /// Jenkins hash function (HashLittle2) used by UO for file lookups
        /// </summary>
        private static ulong HashLittle2(string s)
        {
            uint a, b, c;
            int length = s.Length;

            a = b = c = 0xDEADBEEF + (uint)length;

            int i = 0;

            while (length > 12)
            {
                a += (uint)(s[i] + (s[i + 1] << 8) + (s[i + 2] << 16) + (s[i + 3] << 24));
                b += (uint)(s[i + 4] + (s[i + 5] << 8) + (s[i + 6] << 16) + (s[i + 7] << 24));
                c += (uint)(s[i + 8] + (s[i + 9] << 8) + (s[i + 10] << 16) + (s[i + 11] << 24));

                a -= c; a ^= (c << 4) | (c >> 28); c += b;
                b -= a; b ^= (a << 6) | (a >> 26); a += c;
                c -= b; c ^= (b << 8) | (b >> 24); b += a;
                a -= c; a ^= (c << 16) | (c >> 16); c += b;
                b -= a; b ^= (a << 19) | (a >> 13); a += c;
                c -= b; c ^= (b << 4) | (b >> 28); b += a;

                length -= 12;
                i += 12;
            }

            if (length > 0)
            {
                switch (length)
                {
                    case 12: c += (uint)(s[i + 11] << 24); goto case 11;
                    case 11: c += (uint)(s[i + 10] << 16); goto case 10;
                    case 10: c += (uint)(s[i + 9] << 8); goto case 9;
                    case 9: c += s[i + 8]; goto case 8;
                    case 8: b += (uint)(s[i + 7] << 24); goto case 7;
                    case 7: b += (uint)(s[i + 6] << 16); goto case 6;
                    case 6: b += (uint)(s[i + 5] << 8); goto case 5;
                    case 5: b += s[i + 4]; goto case 4;
                    case 4: a += (uint)(s[i + 3] << 24); goto case 3;
                    case 3: a += (uint)(s[i + 2] << 16); goto case 2;
                    case 2: a += (uint)(s[i + 1] << 8); goto case 1;
                    case 1: a += s[i]; break;
                }

                c ^= b; c -= (b << 14) | (b >> 18);
                a ^= c; a -= (c << 11) | (c >> 21);
                b ^= a; b -= (a << 25) | (a >> 7);
                c ^= b; c -= (b << 16) | (b >> 16);
                a ^= c; a -= (c << 4) | (c >> 28);
                b ^= a; b -= (a << 14) | (a >> 18);
                c ^= b; c -= (b << 24) | (b >> 8);
            }

            return ((ulong)b << 32) | c;
        }
        
        /// <summary>
        /// Clear the cache (useful when files change)
        /// </summary>
        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _indexCache = null;
                _cachedUopPath = null;
            }
        }
    }
}
