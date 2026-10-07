using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MeesaMultisMaker.Audio
{
    /// <summary>
    /// Client for the quality-gate sidecar (AudioGateServer.py): whisper
    /// vocal detection + CLAP prompt-audio match scoring. Mirrors the
    /// vision engine lifecycle: spawn the python child on first use, plain
    /// HTTP after. Every failure degrades to "gates unavailable" -- calls
    /// return ok:false and generation continues ungated, never throws.
    ///
    /// Pure decision helpers (LooksInstrumental, BestAttemptIndex,
    /// HardenMusic) live here too so the harness can pin the policy.
    /// </summary>
    public static class AudioGates
    {
        public const int Port = 18171;
        /// <summary>Transcribed words at/above this on an instrumental-intent
        /// track bounces it (one retry, air best).</summary>
        public const int WordsBounceAt = 4;
        /// <summary>CLAP cosine below this rejects the attempt (one retry,
        /// air best). Calibrated on real outputs: matches ~0.25+, clear
        /// mismatches &lt; 0.1.</summary>
        public const double ClapRejectBelow = 0.10;

        public class TranscribeResult
        {
            public bool Ok;
            public int Words;
            public string Text = string.Empty;
        }

        public class ClapResult
        {
            public bool Ok;
            public double Score;
        }

        public class CaptionResult
        {
            public bool Ok;
            public string Text = string.Empty;
        }

        private static readonly object _gate = new object();
        private static Process _proc;
        private static string _url;
        private static bool _loggedDown;
        private static readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static bool IsRunning
        {
            get
            {
                try { return _proc != null && !_proc.HasExited; }
                catch { return false; }
            }
        }

        public static string PythonExe()
        {
            try
            {
                string[] cands = new string[]
                {
                    @"D:\python312\python.exe",
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "python.exe")
                };
                foreach (string c in cands)
                    if (File.Exists(c)) return c;
                string env = Environment.GetEnvironmentVariable("MEESA_PYTHON");
                if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
            }
            catch { }
            return "python";
        }

        public static string ServerScript()
        {
            try
            {
                string[] cands = new string[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio", "AudioGateServer.py"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AudioGateServer.py")
                };
                foreach (string c in cands)
                    if (File.Exists(c)) return c;
            }
            catch { }
            return null;
        }

        private static bool PortFree(int port)
        {
            try
            {
                var l = new TcpListener(IPAddress.Loopback, port);
                l.Start();
                l.Stop();
                return true;
            }
            catch { return false; }
        }

        private static async Task<bool> HealthOk(string baseUrl)
        {
            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(3);
                    var r = await http.GetAsync(baseUrl + "/health");
                    if (!r.IsSuccessStatusCode) return false;
                    string body = await r.Content.ReadAsStringAsync();
                    return body != null && body.Contains("\"status\": \"ok\"")
                        && body.Contains("\"whisper\": true")
                        && body.Contains("\"clap\": true");
                }
            }
            catch { return false; }
        }

        /// <summary>Base URL or null when the sidecar cannot run.</summary>
        public static async Task<string> EnsureServerAsync(CancellationToken ct, Action<string> log)
        {
            if (IsRunning && _url != null) return _url;
            // A previous python already serving (dev rerun)?
            if (!PortFree(Port))
            {
                string reuse = "http://127.0.0.1:" + Port;
                if (await HealthOk(reuse))
                {
                    _url = reuse;
                    return _url;
                }
            }
            string script = ServerScript();
            if (string.IsNullOrEmpty(script))
            {
                NoteDown(log, "Gate server script missing (AudioGateServer.py not deployed).");
                return null;
            }
            string py = PythonExe();
            var psi = new ProcessStartInfo
            {
                FileName = py,
                Arguments = "\"" + script + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            try
            {
                string hf = @"D:\MeesaAI\hf-cache";
                try { if (!Directory.Exists(hf)) Directory.CreateDirectory(hf); }
                catch { }
                psi.EnvironmentVariables["HF_HUB_CACHE"] = hf;
                psi.EnvironmentVariables["HF_HOME"] = hf;
                // ctranslate2/cuBLAS resolution (see DLL search notes).
                try
                {
                    string pydir = Path.GetDirectoryName(py);
                    string sitePkgs = Path.Combine(pydir, "Lib", "site-packages");
                    var bins = new List<string>();
                    try
                    {
                        string nv = Path.Combine(sitePkgs, "nvidia");
                        if (Directory.Exists(nv))
                            foreach (string d in Directory.GetDirectories(nv))
                            {
                                string b = Path.Combine(d, "bin");
                                if (Directory.Exists(b)) bins.Add(b);
                            }
                    }
                    catch { }
                    string oldPath = psi.EnvironmentVariables["PATH"] ?? string.Empty;
                    if (bins.Count > 0)
                        psi.EnvironmentVariables["PATH"] = string.Join(";", bins.ToArray()) + ";" + oldPath;
                }
                catch { }
            }
            catch { }
            Process proc;
            try
            {
                proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                proc.Start();
            }
            catch (Exception ex)
            {
                NoteDown(log, "Gate server failed to start: " + ex.Message);
                return null;
            }
            if (log != null)
            {
                try { log("Quality gates starting (models load once, ~20s)..."); }
                catch { }
            }
            string url = "http://127.0.0.1:" + Port;
            DateTime deadline = DateTime.UtcNow.AddMinutes(6);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (proc.HasExited)
                    {
                        NoteDown(log, "Gate server exited during startup.");
                        return null;
                    }
                }
                catch (InvalidOperationException) { }
                if (await HealthOk(url)) break;
                await Task.Delay(2000, ct);
            }
            if (!await HealthOk(url))
            {
                try { proc.Kill(); }
                catch { }
                NoteDown(log, "Gate server did not become ready.");
                return null;
            }
            lock (_gate)
            {
                if (IsRunning && _url != null)
                {
                    try { proc.Kill(); }
                    catch { }
                    return _url;
                }
                _proc = proc;
                _url = url;
                _loggedDown = false;
            }
            return url;
        }

        /// <summary>Kill the sidecar (called when the Audio Editor
        /// closes -- otherwise the python process would outlive the app
        /// holding ~2 GB of VRAM).</summary>
        public static void Unload()
        {
            lock (_gate)
            {
                try
                {
                    if (_proc != null)
                    {
                        try { if (!_proc.HasExited) _proc.Kill(); }
                        catch { }
                        try { _proc.Dispose(); }
                        catch { }
                    }
                }
                catch { }
                _proc = null;
                _url = null;
            }
        }

        private static void NoteDown(Action<string> log, string msg)
        {
            try
            {
                if (_loggedDown) return;
                _loggedDown = true;
                if (log != null) log("Quality gates unavailable: " + msg + " Generation continues ungated.");
            }
            catch { }
        }

        private static async Task<Dictionary<string, object>> Post(string url, string path,
            Dictionary<string, object> payload, int timeoutSec, CancellationToken ct)
        {
            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(timeoutSec);
                string json = _json.Serialize(payload);
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (var resp = await http.PostAsync(url + path, content, ct))
                {
                    string body = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode) return null;
                    return _json.DeserializeObject(body) as Dictionary<string, object>;
                }
            }
        }

        public static async Task<TranscribeResult> TranscribeAsync(string wavPath,
            CancellationToken ct, Action<string> log)
        {
            var out_ = new TranscribeResult();
            try
            {
                string url = await EnsureServerAsync(ct, log);
                if (url == null) return out_;
                var root = await Post(url, "/transcribe",
                    new Dictionary<string, object> { { "path", wavPath } }, 180, ct);
                if (root == null) return out_;
                object ok;
                if (!root.TryGetValue("ok", out ok) || !(ok is bool) || !(bool)ok) return out_;
                out_.Ok = true;
                object w;
                if (root.TryGetValue("words", out w)) out_.Words = ToInt(w);
                object t;
                if (root.TryGetValue("text", out t) && t is string) out_.Text = (string)t;
                return out_;
            }
            catch { return out_; }
        }

        public static async Task<ClapResult> ClapAsync(string wavPath, string prompt,
            CancellationToken ct, Action<string> log)
        {
            var out_ = new ClapResult();
            try
            {
                if (string.IsNullOrWhiteSpace(prompt)) return out_;
                string url = await EnsureServerAsync(ct, log);
                if (url == null) return out_;
                var root = await Post(url, "/clap",
                    new Dictionary<string, object> { { "path", wavPath }, { "prompt", prompt } }, 120, ct);
                if (root == null) return out_;
                object ok;
                if (!root.TryGetValue("ok", out ok) || !(ok is bool) || !(bool)ok) return out_;
                object s;
                if (root.TryGetValue("score", out s))
                {
                    try { out_.Score = Convert.ToDouble(s); out_.Ok = true; }
                    catch { }
                }
                return out_;
            }
            catch { return out_; }
        }

        /// <summary>Free-form description of what a clip contains
        /// (sound-effect captioner, first 30s). Generous timeout: the
        /// first call also loads the model.</summary>
        public static async Task<CaptionResult> CaptionAsync(string wavPath,
            CancellationToken ct, Action<string> log)
        {
            var out_ = new CaptionResult();
            try
            {
                if (string.IsNullOrEmpty(wavPath) || !File.Exists(wavPath)) return out_;
                string url = await EnsureServerAsync(ct, log);
                if (url == null) return out_;
                var root = await Post(url, "/caption",
                    new Dictionary<string, object> { { "path", wavPath } }, 240, ct);
                if (root == null) return out_;
                object ok;
                if (!root.TryGetValue("ok", out ok) || !(ok is bool) || !(bool)ok) return out_;
                object t;
                if (root.TryGetValue("caption", out t) && t is string)
                {
                    out_.Text = ((string)t).Trim();
                    out_.Ok = out_.Text.Length > 0;
                }
                return out_;
            }
            catch { return out_; }
        }

        private static int ToInt(object o)
        {
            try { return Convert.ToInt32(o); }
            catch { return 0; }
        }

        /// <summary>True when the run asks for no vocals (prompt or negative
        /// says instrumental / no-vocal / no-lyrics). The bouncer only fires
        /// on intent, so wanted vocal tracks never bounce.</summary>
        public static bool LooksInstrumental(string prompt, string negative)
        {
            try
            {
                string s = ((prompt ?? string.Empty) + " " + (negative ?? string.Empty)).ToLowerInvariant();
                if (s.Contains("instrumental") || s.Contains("no vocal") ||
                    s.Contains("no lyric") || s.Contains("without vocal") ||
                    s.Contains("without lyric"))
                    return true;
                // Negatives list unwanted things, so bare vocal words in
                // the NEGATIVE alone mean "no vocals" (a positive asking
                // for vocals must never match here).
                string n = (negative ?? string.Empty).ToLowerInvariant();
                return n.Contains("vocal") || n.Contains("lyric") ||
                    n.Contains("singing") || n.Contains("talking") ||
                    n.Contains("choir");
            }
            catch { return false; }
        }

        /// <summary>Retry prompt after a vocal bounce: same scene, explicit
        /// instrumental demand.</summary>
        public static string HardenMusicPrompt(string cleanPrompt, int durationSec)
        {
            string c = (cleanPrompt ?? "calm ambient loop").Trim();
            if (c.IndexOf("instrumental", StringComparison.OrdinalIgnoreCase) < 0)
                c += " Pure instrumental, no vocals, no lyrics, no talking, no singing.";
            return c + " Length: " + durationSec + " seconds";
        }

        /// <summary>Retry negative after a vocal bounce: keep the user's
        /// steering, add explicit vocal blocks.</summary>
        public static string HardenMusicNegative(string negative)
        {
            string n = (negative ?? string.Empty).Trim().TrimEnd(',', ' ');
            string[] add = new string[] { "vocals", "lyrics", "singing", "talking", "choir", "voice" };
            foreach (string a in add)
                if (n.IndexOf(a, StringComparison.OrdinalIgnoreCase) < 0)
                    n = (n.Length > 0 ? n + ", " : string.Empty) + a;
            return n;
        }

        /// <summary>Best attempt index: fewest transcribed words wins, then
        /// highest CLAP. words&lt;0 means unmeasured (loses to any measured
        /// count). Never throws.</summary>
        public static int BestAttemptIndex(int[] words, double[] claps)
        {
            try
            {
                if (words == null || words.Length == 0) return 0;
                int best = 0;
                for (int i = 1; i < words.Length; i++)
                {
                    int w = words[i] < 0 ? int.MaxValue : words[i];
                    int bw = words[best] < 0 ? int.MaxValue : words[best];
                    if (w < bw) { best = i; continue; }
                    if (w != bw) continue;
                    double c = claps != null && i < claps.Length ? claps[i] : 0;
                    double bc = claps != null && best < claps.Length ? claps[best] : 0;
                    if (c > bc) best = i;
                }
                return best;
            }
            catch { return 0; }
        }
    }
}
