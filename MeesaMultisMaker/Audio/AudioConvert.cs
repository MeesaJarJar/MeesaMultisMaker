using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace MeesaMultisMaker.Audio
{
    /// <summary>
    /// Compressed audio (mp3/flac/ogg/...) to editable WaveData via an
    /// ffmpeg binary when one is discoverable (PATH, C:\ffmpeg, or beside
    /// the app). Decodes to 16-bit PCM WAV in memory at the source rate;
    /// WaveData DSP handles any resampling after. Throws a clear error
    /// naming the fallback (playback-only) when ffmpeg is absent.
    /// </summary>
    public static class AudioConvert
    {
        /// <summary>Locate ffmpeg.exe or return null.</summary>
        public static string FindFfmpeg()
        {
            try
            {
                string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                foreach (string dir in pathEnv.Split(';'))
                {
                    try
                    {
                        string cand = Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe");
                        if (File.Exists(cand)) return cand;
                    }
                    catch { }
                }
            }
            catch { }
            string[] fixedPaths = new string[]
            {
                @"C:\ffmpeg\bin\ffmpeg.exe",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe")
            };
            foreach (string cand in fixedPaths)
            {
                try { if (File.Exists(cand)) return cand; }
                catch { }
            }
            return null;
        }

        /// <summary>Decode straight to a WAV file (no in-memory pipe).</summary>
        public static void DecodeToFile(string srcPath, string dstWavPath)
        {
            if (string.IsNullOrEmpty(srcPath) || !File.Exists(srcPath))
                throw new Exception("Audio file not found:\r\n" + srcPath);
            string ffmpeg = FindFfmpeg();
            if (string.IsNullOrEmpty(ffmpeg))
                throw new Exception("No decoder available (ffmpeg not found).");
            var psi = new ProcessStartInfo(ffmpeg,
                "-hide_banner -nostats -loglevel error -y -i \"" + srcPath + "\" -map 0:a:0 -c:a pcm_s16le -f wav \"" + dstWavPath + "\"");
            psi.UseShellExecute = false;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            string err;
            using (var proc = new Process { StartInfo = psi })
            {
                proc.Start();
                err = proc.StandardError.ReadToEnd();
                if (!proc.WaitForExit(300000))
                {
                    try { proc.Kill(); }
                    catch { }
                    throw new Exception("Audio decode timed out.");
                }
                if (proc.ExitCode != 0)
                    throw new Exception("Audio decode failed" + (string.IsNullOrEmpty(err) ? "." : ": " + Tail(err.Trim(), 300)));
            }
            if (!File.Exists(dstWavPath))
                throw new Exception("Decoder produced no output" + (string.IsNullOrEmpty(err) ? "." : ": " + Tail(err.Trim(), 300)));
        }

        public static WaveData ToWave(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new Exception("Audio file not found:\r\n" + path);
            string ffmpeg = FindFfmpeg();
            if (string.IsNullOrEmpty(ffmpeg))
                throw new Exception("No decoder available (ffmpeg not found). The file stays playback-only.");
            var psi = new ProcessStartInfo(ffmpeg,
                "-hide_banner -nostats -loglevel error -i \"" + path + "\" -map 0:a:0 -c:a pcm_s16le -f wav pipe:1");
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            byte[] wav;
            string err;
            using (var proc = new Process { StartInfo = psi })
            {
                proc.Start();
                var outBytes = new List<byte[]>(64);
                int total = 0;
                byte[] buf = new byte[1 << 16];
                int n;
                var stdout = proc.StandardOutput.BaseStream;
                while ((n = stdout.Read(buf, 0, buf.Length)) > 0)
                {
                    var chunk = new byte[n];
                    Buffer.BlockCopy(buf, 0, chunk, 0, n);
                    outBytes.Add(chunk);
                    total += n;
                    if (total > 400 * 1024 * 1024)
                    {
                        try { proc.Kill(); }
                        catch { }
                        throw new Exception("Decoded audio exceeds 400 MB, refusing.");
                    }
                }
                err = proc.StandardError.ReadToEnd();
                if (!proc.WaitForExit(120000))
                {
                    try { proc.Kill(); }
                    catch { }
                    throw new Exception("Audio decode timed out.");
                }
                if (proc.ExitCode != 0)
                    throw new Exception("Audio decode failed" + (string.IsNullOrEmpty(err) ? "." : ": " + Tail(err, 300)));
                wav = new byte[total];
                int at = 0;
                foreach (var chunk in outBytes)
                {
                    Buffer.BlockCopy(chunk, 0, wav, at, chunk.Length);
                    at += chunk.Length;
                }
            }
            try
            {
                return WaveData.FromBytes(wav);
            }
            catch (Exception ex)
            {
                throw new Exception("Decoded output unreadable: " + ex.Message);
            }
        }

        private static string Tail(string s, int max)
        {
            if (s == null) return string.Empty;
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(s.Length - max);
        }
    }
}
