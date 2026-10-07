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
    /// Local vision model for the ambient watcher: Qwen2-VL-2B GGUF served
    /// by a private llama-server child process (separate from the text LLM
    /// engine, which has no vision projector). Mirrors LlamaEngine's
    /// lifecycle: download once, spawn on localhost on first describe,
    /// plain HTTP after, kill on unload.
    ///
    /// Describe requests use the OpenAI chat shape with a base64 JPEG.
    /// All calls are serialized; call from a background thread.
    /// </summary>
    public static class VisionEngine
    {
        public const string ModelFileName = "Qwen2-VL-2B-Instruct-Q4_K_M.gguf";
        public const string MmprojFileName = "mmproj-Qwen2-VL-2B-Instruct-Q8_0.gguf";
        public const string ModelRepo = "runanywhere/Qwen2-VL-2B-Instruct-GGUF";
        public const long ModelBytes = 940L * 1024 * 1024;
        public const long MmprojBytes = 676L * 1024 * 1024;

        public static string ModelUrl
        {
            get { return "https://huggingface.co/" + ModelRepo + "/resolve/main/" + ModelFileName; }
        }

        public static string MmprojUrl
        {
            get { return "https://huggingface.co/" + ModelRepo + "/resolve/main/" + MmprojFileName; }
        }

        public const string ServerExe = "llama-server.exe";
        public const int FirstPort = 18101;

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

        public static string ModelDir()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MeesaMultisMaker", "LLM", "models", "vision");
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); }
            catch { }
            return dir;
        }

        public static string ModelPath()
        {
            return Path.Combine(ModelDir(), ModelFileName);
        }

        public static string MmprojPath()
        {
            return Path.Combine(ModelDir(), MmprojFileName);
        }

        public static bool ModelPresent()
        {
            try { return File.Exists(ModelPath()) && File.Exists(MmprojPath()); }
            catch { return false; }
        }

        public static async Task DownloadModelAsync(IProgress<Tuple<string, double>> progress, CancellationToken ct)
        {
            await DownloadOne(ModelUrl, ModelPath(), ModelBytes, "vision model", progress, ct);
            await DownloadOne(MmprojUrl, MmprojPath(), MmprojBytes, "vision projector", progress, ct);
        }

        private static async Task DownloadOne(string url, string dest, long expectBytes,
            string label, IProgress<Tuple<string, double>> progress, CancellationToken ct)
        {
            if (File.Exists(dest) && new FileInfo(dest).Length > expectBytes / 2) return;
            string dir = Path.GetDirectoryName(dest);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string tmp = dest + ".part";
            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromHours(4);
                using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? expectBytes;
                    using (var src = await resp.Content.ReadAsStreamAsync())
                    using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
                    {
                        byte[] buf = new byte[1 << 16];
                        long done = 0, lastPct = -1;
                        int n;
                        while ((n = await src.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                        {
                            await dst.WriteAsync(buf, 0, n, ct);
                            done += n;
                            long pct = total > 0 ? done * 100 / total : 0;
                            if (pct != lastPct && pct % 10 == 0)
                            {
                                lastPct = pct;
                                if (progress != null)
                                    progress.Report(Tuple.Create(label, (double)done / Math.Max(1, total)));
                            }
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
            return await EnsureServerAsync(ct, null);
        }

        public static async Task<string> EnsureServerAsync(CancellationToken ct, Action<string> log)
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
            if (!ModelPresent())
            {
                if (log != null) log("Downloading vision model (~1.6 GB, one time)...");
                await DownloadModelAsync(new Progress<Tuple<string, double>>(t =>
                {
                    try
                    {
                        if (log != null)
                            log(string.Format("Downloading {0}: {1:0}%", t.Item1, t.Item2 * 100));
                    }
                    catch { }
                }), ct);
            }

            int port = 0;
            for (int p = FirstPort; p < FirstPort + 20; p++)
            {
                if (PortFree(p)) { port = p; break; }
            }
            if (port == 0) throw new Exception("No free localhost port for the vision server.");

            int threads = Math.Max(1, Environment.ProcessorCount);
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "-m \"" + ModelPath() + "\" --mmproj \"" + MmprojPath() + "\"" +
                            " --host 127.0.0.1 --port " + port +
                            " -c 4096 -t " + threads + " --no-webui --log-disable",
                WorkingDirectory = bin,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            try { proc.Start(); }
            catch (Exception ex) { throw new Exception("Could not start vision server: " + ex.Message); }

            if (log != null) log("Vision server starting (first load takes a while)...");
            string url = "http://127.0.0.1:" + port;
            DateTime deadline = DateTime.UtcNow.AddMinutes(10);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (proc.HasExited)
                        throw new Exception("Vision server exited during startup.");
                }
                catch (InvalidOperationException) { }
                if (await HealthOk(url)) break;
                await Task.Delay(1000, ct);
            }
            if (!await HealthOk(url))
            {
                try { proc.Kill(); } catch { }
                throw new Exception("Vision server did not become ready.");
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

        #region Describe

        /// <summary>
        /// One scene description for a JPEG frame. Keeps it to a sentence or
        /// two suited for ambient sound design.
        /// </summary>
        public static async Task<string> DescribeAsync(byte[] jpegBytes, CancellationToken ct)
        {
            return await DescribeAsync(jpegBytes,
                "Describe what you see in one or two short sentences, focusing on the setting and anything that could make sound.",
                120, ct);
        }

        public static async Task<string> DescribeAsync(byte[] jpegBytes, string instruction,
            int maxTokens, CancellationToken ct)
        {
            if (jpegBytes == null || jpegBytes.Length == 0)
                throw new Exception("Empty frame.");
            string url = await EnsureServerAsync(ct);
            string b64;
            try { b64 = Convert.ToBase64String(jpegBytes); }
            catch (Exception ex) { throw new Exception("Frame encode failed: " + ex.Message); }
            var payload = new Dictionary<string, object>
            {
                { "temperature", 0.2 },
                { "max_tokens", Math.Max(16, maxTokens) },
                { "stream", false },
                { "messages", new object[]
                    {
                        new Dictionary<string, object>
                        {
                            { "role", "user" },
                            { "content", new object[]
                                {
                                    new Dictionary<string, object>
                                    {
                                        { "type", "text" },
                                        { "text", instruction }
                                    },
                                    new Dictionary<string, object>
                                    {
                                        { "type", "image_url" },
                                        { "image_url", new Dictionary<string, object>
                                            {
                                                { "url", "data:image/jpeg;base64," + b64 }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
            string json = _json.Serialize(payload);
            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromMinutes(5);
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (var resp = await http.PostAsync(url + "/v1/chat/completions", content, ct))
                {
                    string body = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode)
                        throw new Exception("Vision request failed: " + resp.StatusCode + " " + body);
                    var root = _json.DeserializeObject(body) as Dictionary<string, object>;
                    try
                    {
                        var choices = root["choices"] as System.Collections.IList;
                        var first = choices[0] as Dictionary<string, object>;
                        var message = first["message"] as Dictionary<string, object>;
                        string text = message["content"] as string;
                        if (string.IsNullOrEmpty(text))
                            throw new Exception("empty reply");
                        return text.Trim();
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Vision reply unreadable: " + ex.Message);
                    }
                }
            }
        }

        #endregion
    }
}
