using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace MeesaMultisMaker.Diagnostics
{
    /// <summary>
    /// Small diagnostic helper to inspect raw gump data and write decoded PNGs for comparison.
    /// Not used by the app at runtime; invoke manually when investigating decoding issues.
    /// </summary>
    public static class GumpDebug
    {
        public static void DumpMulGump(string mulFolder, int gumpId, bool lookupOffsetsAreDwords)
        {
            string idxPath = Path.Combine(mulFolder, "gumpidx.mul");
            string mulPath = Path.Combine(mulFolder, "gumpart.mul");
            if (!File.Exists(idxPath) || !File.Exists(mulPath))
            {
                Debug.WriteLine($"[GumpDebug] MUL files missing in {mulFolder}");
                return;
            }

            // Read index entry
            long idxPos = gumpId * 12L;
            using (var idx = File.OpenRead(idxPath))
            using (var brIdx = new BinaryReader(idx))
            {
                if (idx.Length < idxPos + 12)
                {
                    Debug.WriteLine($"[GumpDebug] index out of range for {gumpId}");
                    return;
                }
                idx.Seek(idxPos, SeekOrigin.Begin);
                int offset = brIdx.ReadInt32();
                int length = brIdx.ReadInt32();
                int extra = brIdx.ReadInt32();
                int width = (extra >> 16) & 0xFFFF;
                int height = extra & 0xFFFF;

                Debug.WriteLine($"[GumpDebug] IDX gump {gumpId:X4}: off={offset}, len={length}, w={width}, h={height}");

                using (var mul = File.OpenRead(mulPath))
                using (var brMul = new BinaryReader(mul))
                {
                    if (offset < 0 || length <= 0 || mul.Length < offset + length)
                    {
                        Debug.WriteLine($"[GumpDebug] invalid entry bounds");
                        return;
                    }

                    mul.Seek(offset, SeekOrigin.Begin);
                    int[] lookup = new int[height];
                    for (int i = 0; i < height; i++) lookup[i] = brMul.ReadInt32();

                    long dataStart = offset + height * 4L;
                    var pixels = new byte[width * height * 4];

                    for (int y = 0; y < height; y++)
                    {
                        long rowStart = dataStart + (lookupOffsetsAreDwords ? lookup[y] * 4L : lookup[y]);
                        if (rowStart < offset || rowStart >= offset + length) continue;
                        mul.Seek(rowStart, SeekOrigin.Begin);
                        int x = 0;
                        while (x < width && mul.Position + 4 <= offset + length)
                        {
                            ushort color16 = brMul.ReadUInt16();
                            ushort run = brMul.ReadUInt16();
                            if (run == 0) break;
                            int actualRun = Math.Min(run, (ushort)(width - x));
                            if (color16 == 0)
                            {
                                x += actualRun;
                            }
                            else
                            {
                                int r = (color16 >> 10) & 0x1F;
                                int g = (color16 >> 5) & 0x1F;
                                int b = color16 & 0x1F;
                                // Expand 5-bit
                                r = (r << 3) | (r >> 2);
                                g = (g << 3) | (g >> 2);
                                b = (b << 3) | (b >> 2);
                                int argb = (255 << 24) | (r << 16) | (g << 8) | b;
                                for (int i = 0; i < actualRun; i++, x++)
                                {
                                    int idxPix = (y * width + x) * 4;
                                    pixels[idxPix + 0] = (byte)(argb & 0xFF);
                                    pixels[idxPix + 1] = (byte)((argb >> 8) & 0xFF);
                                    pixels[idxPix + 2] = (byte)((argb >> 16) & 0xFF);
                                    pixels[idxPix + 3] = 255;
                                }
                            }
                        }
                    }

                    WritePng(width, height, pixels, $"mul_gump_{gumpId:X4}_{(lookupOffsetsAreDwords ? "dword" : "byte")}.png");
                }
            }
        }

        public static void DumpUopGump(byte[] data, int gumpId, bool lookupOffsetsAreDwords)
        {
            int width = BitConverter.ToInt32(data, 0);
            int height = BitConverter.ToInt32(data, 4);
            int lookupStart = 8;
            int lookupSize = height * 4;
            int dataStart = lookupStart + lookupSize;
            var pixels = new byte[width * height * 4];

            for (int y = 0; y < height; y++)
            {
                int offset = BitConverter.ToInt32(data, lookupStart + y * 4);
                int rowStart = dataStart + (lookupOffsetsAreDwords ? offset * 4 : offset);
                if (rowStart < 0 || rowStart >= data.Length) continue;
                int pos = rowStart;
                int x = 0;
                while (x < width && pos + 4 <= data.Length)
                {
                    ushort color16 = (ushort)(data[pos] | (data[pos + 1] << 8));
                    ushort run = (ushort)(data[pos + 2] | (data[pos + 3] << 8));
                    pos += 4;
                    if (run == 0) break;
                    int actualRun = Math.Min(run, (ushort)(width - x));
                    if (color16 == 0)
                    {
                        x += actualRun;
                    }
                    else
                    {
                        color16 ^= 0x8000;
                        int r = (color16 >> 10) & 0x1F;
                        int g = (color16 >> 5) & 0x1F;
                        int b = color16 & 0x1F;
                        r = (r << 3) | (r >> 2);
                        g = (g << 3) | (g >> 2);
                        b = (b << 3) | (b >> 2);
                        int argb = (255 << 24) | (r << 16) | (g << 8) | b;
                        for (int i = 0; i < actualRun; i++, x++)
                        {
                            int idxPix = (y * width + x) * 4;
                            pixels[idxPix + 0] = (byte)(argb & 0xFF);
                            pixels[idxPix + 1] = (byte)((argb >> 8) & 0xFF);
                            pixels[idxPix + 2] = (byte)((argb >> 16) & 0xFF);
                            pixels[idxPix + 3] = 255;
                        }
                    }
                }
            }

            WritePng(width, height, pixels, $"uop_gump_{gumpId:X4}_{(lookupOffsetsAreDwords ? "dword" : "byte")}.png");
        }

        public static void DumpUopGump(string mulFolder, int gumpId, bool lookupOffsetsAreDwords)
        {
            var uopPath = Path.Combine(mulFolder, "gumpartLegacyMUL.uop");
            if (!File.Exists(uopPath))
            {
                Debug.WriteLine($"[GumpDebug] UOP file missing in {mulFolder}");
                return;
            }

            // Warm cache
            MeesaMultisMaker.Mul.UopGumpReader.GetValidGumpIds(mulFolder);
            var field = typeof(MeesaMultisMaker.Mul.UopGumpReader).GetField("_indexCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var cache = field?.GetValue(null) as System.Collections.Concurrent.ConcurrentDictionary<int, object>;
            if (cache == null || !cache.ContainsKey(gumpId))
            {
                Debug.WriteLine($"[GumpDebug] gump {gumpId:X4} not in UOP index");
                return;
            }

            var entry = cache[gumpId];
            var entryType = entry.GetType();
            long offset = (long)entryType.GetField("Offset")?.GetValue(entry);
            int compressedSize = (int)entryType.GetField("CompressedSize")?.GetValue(entry);
            int decompressedSize = (int)entryType.GetField("DecompressedSize")?.GetValue(entry);
            short compressionMethod = (short)entryType.GetField("CompressionMethod")?.GetValue(entry);

            using (var stream = File.OpenRead(uopPath))
            using (var reader = new BinaryReader(stream))
            {
                stream.Seek(offset, SeekOrigin.Begin);
                byte[] data;
                if (compressionMethod != 0 && compressedSize != decompressedSize)
                {
                    var compressed = reader.ReadBytes(compressedSize);
                    using (var input = new MemoryStream(compressed, 2, compressed.Length - 2))
                    using (var deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress))
                    using (var output = new MemoryStream())
                    {
                        deflate.CopyTo(output);
                        data = output.ToArray();
                    }
                }
                else
                {
                    int size = decompressedSize > 0 ? decompressedSize : compressedSize;
                    data = reader.ReadBytes(size);
                }

                DumpUopGump(data, gumpId, lookupOffsetsAreDwords);
            }
        }

        private static void WritePng(int width, int height, byte[] pixels, string filename)
        {
            string dir = Path.Combine(Path.GetTempPath(), "GumpDebug");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, filename);

            using (var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                var rect = new Rectangle(0, 0, width, height);
                var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
                }
                finally
                {
                    bmp.UnlockBits(data);
                }
                bmp.Save(path, ImageFormat.Png);
            }
            Debug.WriteLine($"[GumpDebug] Wrote {path}");
        }
    }
}
