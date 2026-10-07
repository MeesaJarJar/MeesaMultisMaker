using System;
using System.Collections.Generic;
using System.IO;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Reads sound entries from soundLegacyMUL.uop (UO Classic style).
    /// UOP files are self-contained: entries are hashed as
    /// build/soundlegacymul/{id:D8}.dat. Each entry payload is the same
    /// 32-byte ASCII name + raw PCM 16-bit mono 22050 Hz as sound.mul.
    /// Entries may be zlib-compressed (method 1) or stored raw (method 0).
    /// </summary>
    public static class SoundUopReader
    {
        public const string SOUND_UOP_FILENAME = "soundLegacyMUL.uop";
        private const string SOUND_HASH_FORMAT = "build/soundlegacymul/{0:D8}.dat";
        public const int MaxSlots = 4095;

        public class UopSoundEntry
        {
            public int Id;
            public long DataOffset; // absolute file offset of payload
            public int StoredSize; // bytes on disk
            public int PayloadSize; // decompressed bytes
            public short CompressionMethod; // 0 = raw, 1 = zlib
            public string Name = string.Empty;
            public byte[] Pcm; // raw PCM (decompressed)
        }

        public static List<UopSoundEntry> Load(string mulFolder)
        {
            var out_ = new List<UopSoundEntry>();
            if (string.IsNullOrEmpty(mulFolder)) return out_;
            string uopPath = Path.Combine(mulFolder, SOUND_UOP_FILENAME);
            if (!File.Exists(uopPath)) return out_;

            var hashToIndex = new Dictionary<ulong, int>();
            for (int i = 0; i <= MaxSlots; i++)
                hashToIndex[HashLittle2(string.Format(SOUND_HASH_FORMAT, i))] = i;

            var raw = new Dictionary<int, RawEntry>();
            using (var stream = File.OpenRead(uopPath))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < 28) return out_;
                uint signature = reader.ReadUInt32();
                reader.ReadInt32(); // version
                reader.ReadUInt32(); // timestamp
                long nextBlock = reader.ReadInt64();
                reader.ReadUInt32(); // blockSize
                reader.ReadInt32(); // fileCount
                if (signature != 0x0050594D) return out_;

                int blocksRead = 0;
                while (nextBlock != 0 && blocksRead < 10000)
                {
                    if (nextBlock < 0 || nextBlock >= stream.Length) break;
                    stream.Seek(nextBlock, SeekOrigin.Begin);
                    int filesInBlock = reader.ReadInt32();
                    long nextBlockAddress = reader.ReadInt64();
                    if (filesInBlock < 0 || filesInBlock > 10000) break;
                    for (int i = 0; i < filesInBlock; i++)
                    {
                        long offset = reader.ReadInt64();
                        int headerSize = reader.ReadInt32();
                        int compressedSize = reader.ReadInt32();
                        int decompressedSize = reader.ReadInt32();
                        ulong hash = reader.ReadUInt64();
                        reader.ReadUInt32(); // adler
                        short method = reader.ReadInt16();
                        if (offset == 0 || compressedSize == 0) continue;
                        int idx;
                        if (!hashToIndex.TryGetValue(hash, out idx)) continue;
                        raw[idx] = new RawEntry
                        {
                            Offset = offset + headerSize,
                            StoredSize = compressedSize,
                            PayloadSize = decompressedSize,
                            Method = method
                        };
                    }
                    nextBlock = nextBlockAddress;
                    blocksRead++;
                }

                foreach (var kv in raw)
                {
                    var r = kv.Value;
                    if (r.Offset < 0 || r.Offset + r.StoredSize > stream.Length) continue;
                    if (r.StoredSize <= 0 || r.PayloadSize <= 0 || r.PayloadSize > 64 * 1024 * 1024) continue;
                    stream.Seek(r.Offset, SeekOrigin.Begin);
                    byte[] stored = new byte[r.StoredSize];
                    int got = 0;
                    while (got < r.StoredSize)
                    {
                        int n = stream.Read(stored, got, r.StoredSize - got);
                        if (n <= 0) break;
                        got += n;
                    }
                    if (got != r.StoredSize) continue;
                    byte[] payload = stored;
                    if (r.Method != 0 && r.StoredSize != r.PayloadSize)
                    {
                        try
                        {
                            using (var input = new MemoryStream(stored, 2, stored.Length - 2))
                            using (var deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress))
                            using (var output = new MemoryStream())
                            {
                                deflate.CopyTo(output);
                                payload = output.ToArray();
                            }
                        }
                        catch { continue; }
                    }
                    if (payload.Length < 33) continue;
                    string name = System.Text.Encoding.ASCII.GetString(payload, 0, 32);
                    int nul = name.IndexOf('\0');
                    if (nul >= 0) name = name.Substring(0, nul);
                    byte[] pcm = new byte[payload.Length - 32];
                    Buffer.BlockCopy(payload, 32, pcm, 0, pcm.Length);
                    out_.Add(new UopSoundEntry
                    {
                        Id = kv.Key,
                        DataOffset = r.Offset,
                        StoredSize = r.StoredSize,
                        PayloadSize = r.PayloadSize,
                        CompressionMethod = r.Method,
                        Name = name.Trim(),
                        Pcm = pcm
                    });
                }
            }
            out_.Sort((a, b) => a.Id.CompareTo(b.Id));
            return out_;
        }

        /// <summary>
        /// Replace an entry's PCM bytes in place (size must match exactly;
        /// the entry name field is preserved). Only safe for raw (method 0)
        /// entries -- the probe-verified case for soundLegacyMUL.uop.
        /// Backs up the whole UOP to .bak first (once -- pristine kept).
        /// </summary>
        public static void ReplacePcm(string mulFolder, int id, byte[] pcm22050Mono)
        {
            if (string.IsNullOrEmpty(mulFolder))
                throw new Exception("No MUL folder configured.");
            if (pcm22050Mono == null || pcm22050Mono.Length == 0)
                throw new Exception("Clip is empty.");
            string uopPath = Path.Combine(mulFolder, SOUND_UOP_FILENAME);
            if (!File.Exists(uopPath)) throw new Exception(SOUND_UOP_FILENAME + " not found in:\r\n" + mulFolder);

            var all = Load(mulFolder);
            UopSoundEntry target = null;
            foreach (var e in all)
            {
                if (e.Id == id) { target = e; break; }
            }
            if (target == null)
                throw new Exception("Slot " + id + " not present in " + SOUND_UOP_FILENAME + ".");
            if (target.CompressionMethod != 0)
                throw new Exception("Slot " + id + " is compressed (method " + target.CompressionMethod + "); in-place replace refused.");
            if (target.Pcm == null || pcm22050Mono.Length != target.Pcm.Length)
                throw new Exception(string.Format(
                    "Size mismatch: slot {0} holds {1} PCM bytes but the clip has {2}. Use Fit clip to slot first.",
                    id, target.Pcm != null ? target.Pcm.Length : 0, pcm22050Mono.Length));

            string bak = uopPath + ".bak";
            try
            {
                if (!File.Exists(bak)) File.Copy(uopPath, bak);
            }
            catch (Exception ex) { throw new Exception("Backup failed, refusing to write: " + ex.Message); }

            using (var fs = new FileStream(uopPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                // Payload = 32-byte name (preserved) + PCM.
                fs.Seek(target.DataOffset + 32, SeekOrigin.Begin);
                fs.Write(pcm22050Mono, 0, pcm22050Mono.Length);
                fs.Flush();
            }
        }

        private class RawEntry
        {
            public long Offset;
            public int StoredSize;
            public int PayloadSize;
            public short Method;
        }

        /// <summary>
        /// Jenkins HashLittle2, byte-for-byte the same convention as
        /// UopArtReader (including ((ulong)b &lt;&lt; 32) | c return order
        /// and final-mix only on a nonzero remainder).
        /// </summary>
        public static ulong HashLittle2(string s)
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
    }
}
