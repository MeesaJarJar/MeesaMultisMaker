using MeesaMultisMaker.Audio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// UO sound.mul / soundidx.mul access (legacy MUL pair only).
    ///
    /// Layout (mirrors UOFiddler): soundidx holds 12-byte entries
    /// (int32 data offset, int32 length, int32 extra; -1 = empty slot).
    /// Each sound.mul entry is a 32-byte ASCII name plus raw PCM signed
    /// 16-bit mono 22050 Hz. WrapWav() prepends a standard 44-byte header
    /// so entries play/edit as regular WAVs.
    /// </summary>
    public class SoundEntry
    {
        public int Id;
        public string Name = string.Empty;
        /// <summary>Raw PCM bytes (mono 22050/16).</summary>
        public byte[] Pcm = new byte[0];

        public double DurationSec
        {
            get { return Pcm != null && Pcm.Length > 0 ? (double)Pcm.Length / 2 / 22050 : 0; }
        }

        public string Label
        {
            get
            {
                string n = string.IsNullOrEmpty(Name) ? "(no name)" : Name;
                return string.Format("#{0} {1} ({2:0.0}s)", Id, n, DurationSec);
            }
        }
    }

    public class SoundMul
    {
        public const int MaxSlots = 4095; // 0xFFF
        public const int EntryRate = 22050;

        private readonly List<SoundEntry> _slots = new List<SoundEntry>();

        public int SlotCount
        {
            get { return _slots.Count; }
        }

        public SoundEntry GetSlot(int id)
        {
            if (id < 0 || id >= _slots.Count) return null;
            return _slots[id].Pcm != null ? _slots[id] : null;
        }

        public static string FindFolder()
        {
            try { return AppConfig.Instance.FindMulFolder(); }
            catch { return string.Empty; }
        }

        public void Load(string folder)
        {
            _slots.Clear();
            if (string.IsNullOrEmpty(folder))
                throw new Exception("No MUL folder configured.");
            string idxPath = Path.Combine(folder, "soundidx.mul");
            string mulPath = Path.Combine(folder, "sound.mul");
            if (!File.Exists(idxPath)) throw new Exception("soundidx.mul not found in:\r\n" + folder);
            if (!File.Exists(mulPath)) throw new Exception("sound.mul not found in:\r\n" + folder);

            byte[] idx = File.ReadAllBytes(idxPath);
            int count = Math.Min(MaxSlots, idx.Length / 12);
            byte[] mul = File.ReadAllBytes(mulPath);
            for (int i = 0; i < count; i++)
            {
                int offset = BitConverter.ToInt32(idx, i * 12);
                int length = BitConverter.ToInt32(idx, i * 12 + 4);
                var entry = new SoundEntry { Id = i };
                if (offset >= 0 && length > 32 && offset + length <= mul.Length)
                {
                    string name = Encoding.ASCII.GetString(mul, offset, 32);
                    int nul = name.IndexOf('\0');
                    if (nul >= 0) name = name.Substring(0, nul);
                    entry.Name = name.Trim();
                    entry.Pcm = new byte[length - 32];
                    Buffer.BlockCopy(mul, offset + 32, entry.Pcm, 0, length - 32);
                    _slots.Add(entry);
                }
                else
                {
                    entry.Pcm = null; // empty slot
                    _slots.Add(entry);
                }
            }
        }

        /// <summary>Standard 44-byte WAV header + raw entry bytes.</summary>
        public static byte[] WrapWav(SoundEntry entry)
        {
            if (entry == null || entry.Pcm == null)
                throw new Exception("Empty sound slot.");
            int dataLen = entry.Pcm.Length;
            var wav = new byte[44 + dataLen];
            WriteAscii(wav, 0, "RIFF");
            BitConverter.GetBytes(36 + dataLen).CopyTo(wav, 4);
            WriteAscii(wav, 8, "WAVE");
            WriteAscii(wav, 12, "fmt ");
            BitConverter.GetBytes(16).CopyTo(wav, 16);
            BitConverter.GetBytes((short)1).CopyTo(wav, 20);
            BitConverter.GetBytes((short)1).CopyTo(wav, 22);
            BitConverter.GetBytes(EntryRate).CopyTo(wav, 24);
            BitConverter.GetBytes(EntryRate * 2).CopyTo(wav, 28);
            BitConverter.GetBytes((short)2).CopyTo(wav, 32);
            BitConverter.GetBytes((short)16).CopyTo(wav, 34);
            WriteAscii(wav, 36, "data");
            BitConverter.GetBytes(dataLen).CopyTo(wav, 40);
            Buffer.BlockCopy(entry.Pcm, 0, wav, 44, dataLen);
            return wav;
        }

        private static void WriteAscii(byte[] buf, int at, string s)
        {
            for (int i = 0; i < s.Length; i++) buf[at + i] = (byte)s[i];
        }

        /// <summary>
        /// Stage a 22050 Hz mono 16-bit clip into a slot (in memory; call
        /// Save() to write). Resamples/converts as needed.
        /// </summary>
        public void Replace(int id, WaveData clip, string name)
        {
            if (id < 0 || id >= _slots.Count)
                throw new Exception("Slot id out of range (0.." + (_slots.Count - 1) + ").");
            if (clip == null || clip.FrameCount == 0)
                throw new Exception("Clip is empty.");
            var work = clip.Clone();
            work.ToMono();
            work.Resample(EntryRate);
            work.Normalize(0.98f);
            int frames = work.FrameCount;
            var pcm = new byte[frames * 2];
            for (int i = 0; i < work.Samples.Length; i++)
            {
                double v = work.Samples[i];
                if (v > 1) v = 1;
                else if (v < -1) v = -1;
                short s = (short)Math.Round(v * 32767.0);
                pcm[i * 2] = (byte)(s & 0xFF);
                pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            string clean = string.Empty;
            if (!string.IsNullOrEmpty(name))
            {
                var sb = new StringBuilder();
                foreach (char c in name)
                {
                    if (c >= 32 && c < 127) sb.Append(c);
                    if (sb.Length >= 31) break;
                }
                clean = sb.ToString();
            }
            _slots[id] = new SoundEntry { Id = id, Name = clean, Pcm = pcm };
        }

        /// <summary>
        /// Rewrite both files from the loaded/staged slots. Existing files
        /// are backed up to .bak first (only when no .bak exists yet, so the
        /// pristine originals are never clobbered by repeat saves).
        /// </summary>
        public void Save(string folder)
        {
            if (string.IsNullOrEmpty(folder))
                throw new Exception("No MUL folder configured.");
            string idxPath = Path.Combine(folder, "soundidx.mul");
            string mulPath = Path.Combine(folder, "sound.mul");
            if (_slots.Count == 0)
                throw new Exception("No sounds loaded.");
            BackupOnce(idxPath);
            BackupOnce(mulPath);
            using (var fsidx = new FileStream(idxPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var fsmul = new FileStream(mulPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var binidx = new BinaryWriter(fsidx))
            using (var binmul = new BinaryWriter(fsmul))
            {
                for (int i = 0; i < _slots.Count; i++)
                {
                    var s = _slots[i];
                    if (s == null || s.Pcm == null || s.Pcm.Length == 0)
                    {
                        binidx.Write(-1);
                        binidx.Write(-1);
                        binidx.Write(-1);
                        continue;
                    }
                    binidx.Write((int)fsmul.Position);
                    binidx.Write(s.Pcm.Length + 32);
                    binidx.Write(0);
                    byte[] nameBuf = new byte[32];
                    if (!string.IsNullOrEmpty(s.Name))
                    {
                        byte[] nb = Encoding.ASCII.GetBytes(s.Name);
                        Buffer.BlockCopy(nb, 0, nameBuf, 0, Math.Min(nb.Length, 31));
                    }
                    binmul.Write(nameBuf, 0, 32);
                    binmul.Write(s.Pcm, 0, s.Pcm.Length);
                }
            }
        }

        private static void BackupOnce(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                string bak = path + ".bak";
                if (File.Exists(bak)) return; // keep the pristine originals
                File.Copy(path, bak);
            }
            catch { }
        }
    }
}
