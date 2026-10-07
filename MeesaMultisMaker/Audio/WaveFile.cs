using System;
using System.Collections.Generic;
using System.IO;

namespace MeesaMultisMaker.Audio
{
    /// <summary>
    /// Minimal WAV PCM engine (no third-party deps): reads 8/16/24/32-bit
    /// int + 32/64-bit float RIFF/WAVE (incl. WAVE_FORMAT_EXTENSIBLE PCM),
    /// works in float samples, writes 16-bit PCM. Everything the Audio
    /// Editor needs (trim/fade/normalize/gain/resample/mono/reverse/
    /// silence-crop/loop-crossfade) lives here.
    /// </summary>
    public class WaveData
    {
        public int SampleRate = 44100;
        public int Channels = 1;
        /// <summary>Interleaved float samples, -1..1.</summary>
        public float[] Samples = new float[0];

        public int FrameCount
        {
            get { return Channels > 0 ? Samples.Length / Channels : 0; }
        }

        public double DurationSec
        {
            get { return SampleRate > 0 ? (double)FrameCount / SampleRate : 0; }
        }

        public WaveData Clone()
        {
            return new WaveData
            {
                SampleRate = SampleRate,
                Channels = Channels,
                Samples = (float[])Samples.Clone()
            };
        }

        #region IO

        public static WaveData FromFile(string path)
        {
            return FromBytes(File.ReadAllBytes(path));
        }

        public static WaveData FromBytes(byte[] wav)
        {
            if (wav == null || wav.Length < 44)
                throw new Exception("Not a WAV file (too small).");
            if (wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F' ||
                wav[8] != 'W' || wav[9] != 'A' || wav[10] != 'V' || wav[11] != 'E')
                throw new Exception("Not a WAV file (missing RIFF/WAVE).");

            int fmtTag = 1, channels = 1, rate = 44100, bits = 16;
            int dataAt = -1, dataLen = 0;
            int pos = 12;
            while (pos + 8 <= wav.Length)
            {
                string id = new string(new char[] { (char)wav[pos], (char)wav[pos + 1], (char)wav[pos + 2], (char)wav[pos + 3] });
                int len = BitConverter.ToInt32(wav, pos + 4);
                if (id == "data" && (len < 0 || pos + 8 + len > wav.Length))
                {
                    // Piped/streamed WAVs carry placeholder sizes: the data
                    // runs to end of buffer.
                    len = wav.Length - (pos + 8);
                }
                if (len < 0 || pos + 8 + len > wav.Length + 1) break;
                if (id == "fmt " && len >= 16)
                {
                    fmtTag = BitConverter.ToUInt16(wav, pos + 8);
                    channels = BitConverter.ToUInt16(wav, pos + 10);
                    rate = BitConverter.ToInt32(wav, pos + 12);
                    bits = BitConverter.ToUInt16(wav, pos + 22);
                    if (fmtTag == 0xFFFE && len >= 40)
                    {
                        int sub = BitConverter.ToUInt16(wav, pos + 32);
                        if (sub == 1) fmtTag = 1;
                        else if (sub == 3) fmtTag = 3;
                    }
                }
                else if (id == "data")
                {
                    dataAt = pos + 8;
                    dataLen = Math.Min(len, wav.Length - dataAt);
                }
                pos += 8 + len + (len & 1);
            }
            if (dataAt < 0) throw new Exception("WAV has no data chunk.");
            if (channels < 1 || channels > 32) throw new Exception("Unsupported channel count: " + channels + ".");
            if (rate < 1000 || rate > 384000) throw new Exception("Unsupported sample rate: " + rate + ".");
            if (fmtTag != 1 && fmtTag != 3) throw new Exception("Only PCM/float WAV supported (format " + fmtTag + ").");
            if (fmtTag == 3 && bits != 32 && bits != 64) throw new Exception("Unsupported float depth: " + bits + ".");

            int bytesPerSample = bits / 8;
            int frames = dataLen / (bytesPerSample * channels);
            var out_ = new float[frames * channels];
            for (int f = 0; f < frames; f++)
            {
                for (int c = 0; c < channels; c++)
                {
                    int at = dataAt + (f * channels + c) * bytesPerSample;
                    double v = 0;
                    if (fmtTag == 1)
                    {
                        if (bits == 8) v = (wav[at] - 128) / 128.0;
                        else if (bits == 16) v = BitConverter.ToInt16(wav, at) / 32768.0;
                        else if (bits == 24)
                        {
                            int s = wav[at] | (wav[at + 1] << 8) | (wav[at + 2] << 16);
                            if ((s & 0x800000) != 0) s |= unchecked((int)0xFF000000);
                            v = s / 8388608.0;
                        }
                        else if (bits == 32) v = BitConverter.ToInt32(wav, at) / 2147483648.0;
                        else throw new Exception("Unsupported PCM depth: " + bits + ".");
                    }
                    else
                    {
                        if (bits == 32) v = BitConverter.ToSingle(wav, at);
                        else v = BitConverter.ToDouble(wav, at);
                    }
                    if (v > 1) v = 1;
                    else if (v < -1) v = -1;
                    out_[f * channels + c] = (float)v;
                }
            }
            return new WaveData { SampleRate = rate, Channels = channels, Samples = out_ };
        }

        /// <summary>16-bit PCM WAV bytes.</summary>
        public byte[] ToWavBytes()
        {
            int frames = FrameCount;
            int dataLen = frames * Channels * 2;
            var wav = new byte[44 + dataLen];
            WriteAscii(wav, 0, "RIFF");
            BitConverter.GetBytes(36 + dataLen).CopyTo(wav, 4);
            WriteAscii(wav, 8, "WAVE");
            WriteAscii(wav, 12, "fmt ");
            BitConverter.GetBytes(16).CopyTo(wav, 16);
            BitConverter.GetBytes((short)1).CopyTo(wav, 20);
            BitConverter.GetBytes((short)Channels).CopyTo(wav, 22);
            BitConverter.GetBytes(SampleRate).CopyTo(wav, 24);
            BitConverter.GetBytes(SampleRate * Channels * 2).CopyTo(wav, 28);
            BitConverter.GetBytes((short)(Channels * 2)).CopyTo(wav, 32);
            BitConverter.GetBytes((short)16).CopyTo(wav, 34);
            WriteAscii(wav, 36, "data");
            BitConverter.GetBytes(dataLen).CopyTo(wav, 40);
            for (int i = 0; i < Samples.Length; i++)
            {
                double v = Samples[i];
                if (v > 1) v = 1;
                else if (v < -1) v = -1;
                short s = (short)Math.Round(v * 32767.0);
                wav[44 + i * 2] = (byte)(s & 0xFF);
                wav[44 + i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            return wav;
        }

        public void Save(string path)
        {
            File.WriteAllBytes(path, ToWavBytes());
        }

        private static void WriteAscii(byte[] buf, int at, string s)
        {
            for (int i = 0; i < s.Length; i++) buf[at + i] = (byte)s[i];
        }

        #endregion

        #region DSP (in place unless noted)

        public float Peak()
        {
            float p = 0;
            foreach (float s in Samples)
            {
                float a = Math.Abs(s);
                if (a > p) p = a;
            }
            return p;
        }

        public void Normalize(float peak = 0.98f)
        {
            float p = Peak();
            if (p < 1e-6f) return;
            float g = peak / p;
            for (int i = 0; i < Samples.Length; i++) Samples[i] *= g;
        }

        public void GainDb(double db)
        {
            float g = (float)Math.Pow(10.0, db / 20.0);
            for (int i = 0; i < Samples.Length; i++) Samples[i] *= g;
        }

        public void FadeIn(double seconds)
        {
            int n = (int)(seconds * SampleRate);
            if (n < 2) return;
            n = Math.Min(n, FrameCount);
            for (int f = 0; f < n; f++)
            {
                float g = (float)f / n;
                for (int c = 0; c < Channels; c++) Samples[f * Channels + c] *= g;
            }
        }

        public void FadeOut(double seconds)
        {
            int n = (int)(seconds * SampleRate);
            if (n < 2) return;
            n = Math.Min(n, FrameCount);
            int total = FrameCount;
            for (int f = 0; f < n; f++)
            {
                float g = (float)(n - f) / n;
                int at = (total - n + f) * Channels;
                for (int c = 0; c < Channels; c++) Samples[at + c] *= g;
            }
        }

        public void Reverse()
        {
            int frames = FrameCount;
            var tmp = new float[Channels];
            for (int f = 0; f < frames / 2; f++)
            {
                int a = f * Channels, b = (frames - 1 - f) * Channels;
                for (int c = 0; c < Channels; c++) tmp[c] = Samples[a + c];
                for (int c = 0; c < Channels; c++) Samples[a + c] = Samples[b + c];
                for (int c = 0; c < Channels; c++) Samples[b + c] = tmp[c];
            }
        }

        public void ToMono()
        {
            if (Channels <= 1) return;
            int frames = FrameCount;
            var mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                double s = 0;
                for (int c = 0; c < Channels; c++) s += Samples[f * Channels + c];
                mono[f] = (float)(s / Channels);
            }
            Channels = 1;
            Samples = mono;
        }

        public void Resample(int newRate)
        {
            if (newRate == SampleRate || FrameCount == 0) { SampleRate = newRate; return; }
            int frames = FrameCount;
            int outFrames = Math.Max(1, (int)Math.Round((double)frames * newRate / SampleRate));
            var out_ = new float[outFrames * Channels];
            for (int f = 0; f < outFrames; f++)
            {
                double src = (double)f * frames / outFrames;
                int i0 = (int)src;
                if (i0 >= frames - 1) i0 = frames - 2;
                if (i0 < 0) i0 = 0;
                double t = src - i0;
                for (int c = 0; c < Channels; c++)
                    out_[f * Channels + c] = (float)(Samples[i0 * Channels + c] * (1 - t) + Samples[(i0 + 1) * Channels + c] * t);
            }
            Samples = out_;
            SampleRate = newRate;
        }

        /// <summary>New clip of [startSec, endSec).</summary>
        public WaveData Trim(double startSec, double endSec)
        {
            int total = FrameCount;
            int a = Math.Max(0, (int)(startSec * SampleRate));
            int b = Math.Min(total, (int)Math.Round(endSec * SampleRate));
            if (b <= a) throw new Exception("Empty trim range.");
            var out_ = new float[(b - a) * Channels];
            Array.Copy(Samples, a * Channels, out_, 0, out_.Length);
            return new WaveData { SampleRate = SampleRate, Channels = Channels, Samples = out_ };
        }

        /// <summary>
        /// Pad with silence or trim to an exact frame count (UOP slot fit).
        /// Returns true when padding was added.
        /// </summary>
        public bool FitToFrames(int frames)
        {
            if (frames <= 0) throw new Exception("Bad target length.");
            int have = FrameCount;
            if (have == frames) return false;
            if (have > frames)
            {
                var cut = new float[frames * Channels];
                Array.Copy(Samples, 0, cut, 0, cut.Length);
                Samples = cut;
                return false;
            }
            var padded = new float[frames * Channels];
            Array.Copy(Samples, 0, padded, 0, Samples.Length);
            Samples = padded;
            return true;
        }

        /// <summary>Crop leading/trailing silence below thresholdDb + pad.</summary>
        public WaveData CropSilence(double thresholdDb = -45, double padSec = 0.05)
        {
            float thr = (float)Math.Pow(10.0, thresholdDb / 20.0);
            int frames = FrameCount;
            int a = 0, b = frames;
            for (int f = 0; f < frames; f++)
            {
                bool loud = false;
                for (int c = 0; c < Channels; c++)
                    if (Math.Abs(Samples[f * Channels + c]) >= thr) { loud = true; break; }
                if (loud) { a = f; break; }
                if (f == frames - 1) return Clone(); // all silent: keep
            }
            for (int f = frames - 1; f >= 0; f--)
            {
                bool loud = false;
                for (int c = 0; c < Channels; c++)
                    if (Math.Abs(Samples[f * Channels + c]) >= thr) { loud = true; break; }
                if (loud) { b = f + 1; break; }
            }
            int pad = (int)(padSec * SampleRate);
            a = Math.Max(0, a - pad);
            b = Math.Min(frames, b + pad);
            if (b <= a) return Clone();
            return Trim((double)a / SampleRate, (double)b / SampleRate);
        }

        /// <summary>
        /// Seamless-loop prep: crossfade the TAIL into the HEAD so end wraps
        /// to start. Length unchanged.
        /// </summary>
        public void CrossfadeLoop(double fadeSec)
        {
            int n = (int)(fadeSec * SampleRate);
            if (n < 16) throw new Exception("Loop fade too short.");
            n = Math.Min(n, FrameCount / 2);
            if (n < 16) throw new Exception("Clip too short to loop.");
            int total = FrameCount;
            for (int f = 0; f < n; f++)
            {
                double t = (double)f / n; // 0 at loop start -> 1
                int head = f * Channels;
                int tail = (total - n + f) * Channels;
                for (int c = 0; c < Channels; c++)
                    Samples[head + c] = (float)(Samples[tail + c] * (1 - t) + Samples[head + c] * t);
            }
        }

        /// <summary>
        /// Trim near-silence ends of a WAV file in place + micro-fades to
        /// avoid edge clicks. Diffusion outputs often pad digital silence;
        /// trimming keeps on-air timing honest (swap math uses real audio,
        /// not padding). Returns seconds removed (0 when unchanged).
        /// Never throws. Safety: all-silent and tiny files are kept as-is.
        /// </summary>
        public static double TrimFileEnds(string path, double thresholdDb = -45,
            double padSec = 0.08, double fadeSec = 0.01)
        {
            try
            {
                var w = FromFile(path);
                int before = w.FrameCount;
                if (before < 64 || w.SampleRate <= 0) return 0;
                var cropped = w.CropSilence(thresholdDb, padSec);
                int after = cropped.FrameCount;
                if (after >= before || after < 32) return 0;
                double edge = Math.Min(fadeSec, cropped.DurationSec / 4.0);
                if (edge > 0.001)
                {
                    cropped.FadeIn(edge);
                    cropped.FadeOut(edge);
                }
                cropped.Save(path);
                return (before - after) / (double)w.SampleRate;
            }
            catch { return 0; }
        }

        /// <summary>Peak-abs per bucket across channels (waveform display).</summary>
        public float[] GetPeaks(int buckets)
        {
            if (buckets < 1) buckets = 1;
            var peaks = new float[buckets];
            int frames = FrameCount;
            if (frames == 0) return peaks;
            for (int b = 0; b < buckets; b++)
            {
                int a = (int)((long)b * frames / buckets);
                int z = (int)((long)(b + 1) * frames / buckets);
                if (z <= a) z = a + 1;
                if (z > frames) z = frames;
                float p = 0;
                for (int f = a; f < z; f++)
                    for (int c = 0; c < Channels; c++)
                    {
                        float v = Math.Abs(Samples[f * Channels + c]);
                        if (v > p) p = v;
                    }
                peaks[b] = p;
            }
            return peaks;
        }

        #endregion
    }
}
