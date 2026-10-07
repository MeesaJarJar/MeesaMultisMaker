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

namespace MeesaMultisMaker.LLM
{
    /// <summary>
    /// Embedded llama.cpp inference via a privately-owned child server
    /// process (llama-server.exe, vendored). No Ollama, no services, no
    /// P/Invoke: the app starts the server on first use (localhost only),
    /// talks plain HTTP to it, and kills it on unload. Model + engine are
    /// verified working headlessly before any UI is wired to them.
    /// All calls are serialized; call from a background thread.
    /// </summary>
    public static class LlamaEngine
    {
        public const string ModelFileName = "Qwen3.5-0.8B-Q4_K_M.gguf";
        public const string ModelUrl = "https://huggingface.co/unsloth/Qwen3.5-0.8B-GGUF/resolve/main/Qwen3.5-0.8B-Q4_K_M.gguf";
        public const long ModelBytes = 532517120L;

        public const string ServerExe = "llama-server.exe";
        public const int FirstPort = 18081;

        public static string Status = "not running";
        public static string ServerUrl;
        public static bool IsRunning
        {
            get
            {
                try { return _proc != null && !_proc.HasExited; }
                catch { return false; }
            }
        }

        private static readonly object _gate = new object();
        private static Process _proc;
        private static int _port;
        private static readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        #region Location + download

        public static string BinDir()
        {
            try
            {
                string env = Environment.GetEnvironmentVariable("MEESA_LLAMA_BIN");
                if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, ServerExe)))
                    return env;
            }
            catch { }
            string exe = AppDomain.CurrentDomain.BaseDirectory;
            string[] cands = new string[]
            {
                Path.Combine(exe, "LLM", "bin"),
                Path.GetFullPath(Path.Combine(exe, "..", "LLM", "bin")),
            };
            foreach (string c in cands)
            {
                try { if (File.Exists(Path.Combine(c, ServerExe))) return c; }
                catch { }
            }
            return cands[0];
        }

        public static string FindModel()
        {
            try
            {
                string menv = Environment.GetEnvironmentVariable("MEESA_LLAMA_MODEL");
                if (!string.IsNullOrEmpty(menv) && File.Exists(menv)) return menv;
            }
            catch { }
            string exe = AppDomain.CurrentDomain.BaseDirectory;
            string[] cands = new string[]
            {
                Path.Combine(exe, "LLM", "models", ModelFileName),
                Path.GetFullPath(Path.Combine(exe, "..", "LLM", "models", ModelFileName)),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MeesaMultisMaker", "LLM", "models", ModelFileName),
            };
            foreach (string c in cands)
            {
                try { if (File.Exists(c)) return c; }
                catch { }
            }
            return null;
        }

        public static string DefaultDownloadPath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MeesaMultisMaker", "LLM", "models");
            return Path.Combine(dir, ModelFileName);
        }

        public static async Task DownloadModelAsync(string dest, IProgress<double> progress, CancellationToken ct)
        {
            string dir = Path.GetDirectoryName(dest);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string tmp = dest + ".part";
            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromHours(2);
                using (var resp = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? ModelBytes;
                    using (var src = await resp.Content.ReadAsStreamAsync())
                    using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
                    {
                        byte[] buf = new byte[1 << 16];
                        long done = 0;
                        int n;
                        while ((n = await src.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                        {
                            await dst.WriteAsync(buf, 0, n, ct);
                            done += n;
                            if (progress != null && total > 0)
                                progress.Report((double)done / total);
                        }
                    }
                }
            }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
        }

        #endregion

        #region Server lifecycle

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
                    return body != null && body.ToLowerInvariant().Contains("ok");
                }
            }
            catch { return false; }
        }

        /// <summary>Start the child server if needed; return its base URL.</summary>
        public static async Task<string> EnsureServerAsync(CancellationToken ct)
        {
            if (IsRunning) return ServerUrl;
            lock (_gate)
            {
                if (IsRunning) return ServerUrl;
            }

            string bin = BinDir();
            string exe = Path.Combine(bin, ServerExe);
            if (!File.Exists(exe))
                throw new Exception("LLM engine not found (" + exe + ").");
            string model = FindModel();
            if (model == null)
                throw new Exception("LLM model not found. Expected " + ModelFileName +
                    " next to the app (LLM\\models) or at " + DefaultDownloadPath() + ".");

            int port = 0;
            for (int p = FirstPort; p < FirstPort + 20; p++)
            {
                if (PortFree(p)) { port = p; break; }
            }
            if (port == 0) throw new Exception("No free localhost port for the LLM server.");

            int threads = Math.Max(1, Environment.ProcessorCount);
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "-m \"" + model + "\" --host 127.0.0.1 --port " + port +
                            " -c 2048 -t " + threads + " --no-webui --log-disable",
                WorkingDirectory = bin,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            try { proc.Start(); }
            catch (Exception ex) { throw new Exception("Could not start LLM server: " + ex.Message); }

            string url = "http://127.0.0.1:" + port;
            DateTime deadline = DateTime.UtcNow.AddSeconds(120);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (proc.HasExited)
                        throw new Exception("LLM server exited during startup.");
                }
                catch (InvalidOperationException) { }
                if (await HealthOk(url)) break;
                await Task.Delay(500, ct);
            }
            if (!await HealthOk(url))
            {
                try { proc.Kill(); } catch { }
                throw new Exception("LLM server did not become ready.");
            }

            lock (_gate)
            {
                if (IsRunning)
                {
                    try { proc.Kill(); } catch { }
                    return ServerUrl;
                }
                _proc = proc;
                _port = port;
                ServerUrl = url;
                Status = "running (" + url + ")";
            }
            return url;
        }

        public static void Unload()
        {
            lock (_gate)
            {
                if (_proc != null)
                {
                    try
                    {
                        if (!_proc.HasExited) _proc.Kill();
                        _proc.Dispose();
                    }
                    catch { }
                    _proc = null;
                    ServerUrl = null;
                    Status = "stopped";
                }
            }
        }

        #endregion

        #region Generate

        private static async Task<string> Post(string url, Dictionary<string, object> payload, CancellationToken ct)
        {
            string json = _json.Serialize(payload);
            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromMinutes(10);
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (var resp = await http.PostAsync(url, content, ct))
                {
                    string body = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode)
                        throw new Exception("LLM request failed: " + resp.StatusCode + " " + body);
                    return body;
                }
            }
        }

        /// <summary>
        /// Qwen3.5 chat format with thinking closed out. The GGUF template
        /// emits an empty think block unless enable_thinking=true (verified
        /// by reading tokenizer.chat_template from the model file), so we
        /// emit it ourselves and the model answers directly.
        /// </summary>
        public static string ChatPrompt(string system, string user)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(system))
                sb.Append("<|im_start|>system\n").Append(system).Append("<|im_end|>\n");
            sb.Append("<|im_start|>user\n").Append(user).Append("<|im_end|>\n");
            sb.Append("<|im_start|>assistant\n<think>\n\n</think>\n\n");
            return sb.ToString();
        }

        private static readonly Random _rng = new Random();

        /// <summary>
        /// Chat-style generation via the raw endpoint (full client control
        /// of the template, thinking deterministically off). A fresh random
        /// seed goes out on every call unless seed >= 0 is given: without
        /// this the server samples deterministically and repeats itself.
        /// </summary>
        public static async Task<string> ChatAsync(string system, string user, int maxTokens,
            float temp, string[] stops, CancellationToken ct, int seed = -1)
        {
            string url = await EnsureServerAsync(ct);
            if (seed < 0)
            {
                lock (_rng) seed = _rng.Next();
            }
            var payload = new Dictionary<string, object>
            {
                { "prompt", ChatPrompt(system, user) },
                { "temperature", temp },
                { "top_k", 20 },
                { "top_p", 0.95 },
                { "repeat_penalty", 1.15 },
                { "seed", seed },
                { "n_predict", maxTokens },
                { "cache_prompt", true },
                { "stream", false },
            };
            if (stops != null && stops.Length > 0) payload["stop"] = stops;
            string body = await Post(url + "/completion", payload, ct);
            var root = _json.DeserializeObject(body) as Dictionary<string, object>;
            string text = root != null && root.ContainsKey("content") ? root["content"] as string : null;
            if (text == null)
                throw new Exception("LLM returned no text. Raw reply: " +
                    (body == null ? "<null>" : body.Substring(0, Math.Min(500, body.Length))));
            return text.Trim();
        }

        /// <summary>Raw prefix continuation (for autocomplete). KV-cached.</summary>
        public static async Task<string> CompleteAsync(string prefix, int maxTokens,
            float temp, string[] stops, CancellationToken ct, int seed = -1)
        {
            string url = await EnsureServerAsync(ct);
            if (seed < 0)
            {
                lock (_rng) seed = _rng.Next();
            }
            var payload = new Dictionary<string, object>
            {
                { "prompt", prefix },
                { "temperature", temp },
                { "top_k", 20 },
                { "top_p", 0.95 },
                { "seed", seed },
                { "n_predict", maxTokens },
                { "cache_prompt", true },
                { "stream", false },
            };
            if (stops != null && stops.Length > 0) payload["stop"] = stops;
            string body = await Post(url + "/completion", payload, ct);
            var root = _json.DeserializeObject(body) as Dictionary<string, object>;
            string text = root != null && root.ContainsKey("content") ? root["content"] as string : null;
            if (text == null) throw new Exception("LLM returned no text.");
            return text;
        }

        #endregion
    }
}
