using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Web.Script.Serialization;

namespace MeesaMultisMaker.Audio
{
    /// <summary>
    /// Per-clip loudness discipline via ffmpeg's two-pass EBU R128
    /// loudnorm (the ffmpeg binary is already a hard dependency for
    /// decoding). Watcher beds come out of diffusion at wildly different
    /// perceived levels; matching every aired clip to a layer LUFS target
    /// keeps the mix stable without touching dynamics by hand. In-place
    /// (temp + replace), never throws: false + error string on failure.
    /// </summary>
    public static class AudioLoudness
    {
        /// <summary>LUFS target for background music beds.</summary>
        public const double MusicTargetI = -19.0;
        /// <summary>LUFS target for foley spots (a touch hotter).</summary>
        public const double FoleyTargetI = -16.0;
        public const double TargetTP = -2.0;
        public const double TargetLRA = 11.0;

        /// <summary>Normalize a WAV file to targetI LUFS in place.</summary>
        public static bool TryNormalize(string wavPath, double targetI, out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrEmpty(wavPath) || !File.Exists(wavPath))
                {
                    error = "file not found";
                    return false;
                }
                string ffmpeg = AudioConvert.FindFfmpeg();
                if (string.IsNullOrEmpty(ffmpeg))
                {
                    error = "ffmpeg not found";
                    return false;
                }
                string pass1 = Run(ffmpeg,
                    "-hide_banner -nostats -i \"" + wavPath + "\" -map 0:a:0 -af loudnorm=I=" +
                    F(targetI) + ":TP=" + F(TargetTP) + ":LRA=" + F(TargetLRA) +
                    ":print_format=json -f null -", 120000, out error);
                if (pass1 == null) return false;
                var measured = ParseLoudnorm(pass1);
                if (measured == null)
                {
                    error = "loudness scan unreadable";
                    return false;
                }
                string tmp = wavPath + ".loudnorm.wav";
                try
                {
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
                catch { }
                // loudnorm upsamples to 192 kHz internally and stays there:
                // pin the output back to the source rate or every clip
                // quadruples in size and breaks swap timing.
                int keepRate = 44100;
                try { keepRate = WaveData.FromFile(wavPath).SampleRate; }
                catch { keepRate = 44100; }
                if (keepRate < 8000 || keepRate > 192000) keepRate = 44100;
                string pass2 = Run(ffmpeg,
                    "-hide_banner -nostats -loglevel error -y -i \"" + wavPath + "\" -map 0:a:0 -af loudnorm=I=" +
                    F(targetI) + ":TP=" + F(TargetTP) + ":LRA=" + F(TargetLRA) +
                    ":measured_I=" + measured[0] + ":measured_TP=" + measured[1] +
                    ":measured_LRA=" + measured[2] + ":measured_thresh=" + measured[3] +
                    ":offset=" + measured[4] + ":linear=true " +
                    "-ar " + keepRate + " -c:a pcm_s16le \"" + tmp + "\"", 180000, out error);
                if (pass2 == null) return false;
                FileInfo fi;
                try
                {
                    fi = new FileInfo(tmp);
                    if (!fi.Exists || fi.Length < 1000)
                    {
                        error = "normalizer produced no output";
                        return false;
                    }
                }
                catch
                {
                    error = "normalizer produced no output";
                    return false;
                }
                try
                {
                    File.Delete(wavPath);
                    File.Move(tmp, wavPath);
                }
                catch (Exception ex)
                {
                    error = "replace failed: " + ex.Message;
                    return false;
                }
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Parse ffmpeg loudnorm JSON (new input_* keys with
        /// measured_* fallback) into the five measured_* option values.
        /// Null when unreadable. Pure + harness-testable.</summary>
        public static string[] ParseLoudnorm(string stderrJson)
        {
            try
            {
                if (string.IsNullOrEmpty(stderrJson)) return null;
                int a = stderrJson.IndexOf('{');
                int b = stderrJson.LastIndexOf('}');
                if (a < 0 || b <= a) return null;
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var root = ser.DeserializeObject(stderrJson.Substring(a, b - a + 1)) as Dictionary<string, object>;
                if (root == null) return null;
                string[] vals = new string[]
                {
                    Pick(root, "input_i", "measured_I"),
                    Pick(root, "input_tp", "measured_TP"),
                    Pick(root, "input_lra", "measured_LRA"),
                    Pick(root, "input_thresh", "measured_thresh"),
                    Pick(root, "target_offset", "measured_offset")
                };
                foreach (string v in vals)
                    if (string.IsNullOrEmpty(v)) return null;
                return vals;
            }
            catch { return null; }
        }

        private static string Pick(Dictionary<string, object> root, string a, string b)
        {
            try
            {
                object v;
                if (root.TryGetValue(a, out v) && v != null) return Convert.ToString(v);
                if (root.TryGetValue(b, out v) && v != null) return Convert.ToString(v);
            }
            catch { }
            return null;
        }

        private static string F(double d)
        {
            return d.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string Run(string ffmpeg, string args, int timeoutMs, out string error)
        {
            error = null;
            try
            {
                var psi = new ProcessStartInfo(ffmpeg, args)
                {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (var proc = new Process { StartInfo = psi })
                {
                    proc.Start();
                    // Drain stdout (null sink still pipes on -f null? no
                    // bytes; read anyway to avoid any block).
                    try { proc.StandardOutput.ReadToEnd(); }
                    catch { }
                    string err = proc.StandardError.ReadToEnd();
                    if (!proc.WaitForExit(timeoutMs))
                    {
                        try { proc.Kill(); }
                        catch { }
                        error = "ffmpeg timed out";
                        return null;
                    }
                    if (proc.ExitCode != 0)
                    {
                        error = "ffmpeg exit " + proc.ExitCode + ": " + Tail(err, 300);
                        return null;
                    }
                    return err ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
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
