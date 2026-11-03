using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MeesaMultisMaker.Utils
{
    /// <summary>
    /// Progress report for <see cref="ComfySamInstaller"/>. Fraction is
    /// 0..1 while a download runs, -1 when the step is indeterminate
    /// (git clone, pip install, waiting on the server).
    /// </summary>
    public sealed class SamInstallProgress
    {
        public readonly string Message;
        public readonly double Fraction;
        public SamInstallProgress(string message, double fraction)
        {
            Message = message;
            Fraction = fraction;
        }
    }

    /// <summary>Outcome of <see cref="ComfySamInstaller.InstallAsync"/>.</summary>
    public sealed class SamInstallResult
    {
        /// <summary>Node-pack files + weights are in place on the server side.</summary>
        public bool Success;
        /// <summary>A re-probe found a SAM loader node (no restart needed).</summary>
        public bool LoaderDetected;
        /// <summary>Files are in but the running server has not loaded them yet.</summary>
        public bool RestartRequired;
        /// <summary>Server is not on this machine: no local file ops were possible.</summary>
        public bool IsRemote;
        /// <summary>Human-readable log tail / manual steps when not successful.</summary>
        public string Details = string.Empty;
    }

    /// <summary>
    /// One-click SAM auto-install for the 3D Editor AI Select path.
    /// Order: (a) ComfyUI-Manager API best-effort, (b) git clone into the
    /// local server's custom_nodes + pip install requirements, (c) SAM
    /// ViT-B weights download with .part + atomic move, (d) re-probe and
    /// report whether a server restart is still needed. Remote servers get
    /// manual steps (no local file access). All failures are returned as
    /// data, never thrown, except cancellation / unreachable server.
    /// </summary>
    public static class ComfySamInstaller
    {
        public const string PackRepoUrl = "https://github.com/storyicon/comfyui_segment_anything";
        public const string PackDirName = "comfyui_segment_anything";
        public const string SamVitBUrl = "https://dl.fbaipublicfiles.com/segment_anything/sam_vit_b_01ec64.pth";
        public const string SamVitBFileName = "sam_vit_b_01ec64.pth";
        public const long SamVitBBytes = 375L * 1024L * 1024L;

        public static readonly string[] LoaderNodeClasses = new string[]
        {
            "SAMLoader", "SamLoader", "SAM2Loader", "Sam2Loader",
            "SamAutomaticMaskGenerator", "GroundingDinoModelLoader"
        };

        private static void Say(IProgress<SamInstallProgress> progress, string message, double fraction)
        {
            try { if (progress != null) progress.Report(new SamInstallProgress(message, fraction)); }
            catch { }
        }

        private static string NormBase(string baseUrl)
        {
            string b = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            if (b.Length == 0) b = "http://localhost:8188";
            return b;
        }

        /// <summary>First SAM loader class the server reports via /object_info, else null.</summary>
        public static async Task<string> ProbeLoaderAsync(string baseUrl, CancellationToken ct)
        {
            string b = NormBase(baseUrl);
            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(10);
                foreach (string c in LoaderNodeClasses)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using (var r = await http.GetAsync(b + "/object_info/" + c, ct).ConfigureAwait(false))
                        {
                            if (!r.IsSuccessStatusCode) continue;
                            string body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(body) && body.Contains("\"" + c + "\""))
                                return c;
                        }
                    }
                    catch { }
                }
            }
            return null;
        }

        /// <summary>True when the URL points at this machine (so local file ops apply).</summary>
        public static bool IsLocalServer(string baseUrl)
        {
            try
            {
                var uri = new Uri(NormBase(baseUrl));
                string host = (uri.Host ?? string.Empty).Trim().ToLowerInvariant();
                if (host == "localhost" || host == "127.0.0.1" || host == "::1" || host == "[::1]")
                    return true;
                try
                {
                    string mine = Environment.MachineName.ToLowerInvariant();
                    if (host == mine || host == (mine + ".local")) return true;
                    string dns = System.Net.Dns.GetHostName().ToLowerInvariant();
                    if (host == dns) return true;
                }
                catch { }
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// Full install flow. Throws only when the server is unreachable or
        /// the operation is cancelled; every install-step failure is folded
        /// into the returned result with manual steps in Details.
        /// </summary>
        public static async Task<SamInstallResult> InstallAsync(
            string baseUrl, IProgress<SamInstallProgress> progress, CancellationToken ct)
        {
            string b = NormBase(baseUrl);
            var res = new SamInstallResult();
            var log = new StringBuilder();

            Say(progress, "Probing server…", -1);
            if (!await ServerReachableAsync(b, ct).ConfigureAwait(false))
            {
                res.Details = "ComfyUI is not reachable at " + b + ". Start the server first, then retry.";
                return res;
            }

            string already = await ProbeLoaderAsync(b, ct).ConfigureAwait(false);
            if (already != null)
            {
                res.Success = true;
                res.LoaderDetected = true;
                res.Details = "Found '" + already + "' (already installed).";
                return res;
            }

            // (a) ComfyUI-Manager API, best effort: probe first, ignore failure.
            bool managerTried = false;
            try { managerTried = await TryManagerInstallAsync(b, progress, ct).ConfigureAwait(false); }
            catch { managerTried = false; }
            if (managerTried)
            {
                string via = await ProbeLoaderAsync(b, ct).ConfigureAwait(false);
                if (via != null)
                {
                    res.Success = true;
                    res.LoaderDetected = true;
                    res.Details = "Installed via ComfyUI-Manager; found '" + via + "'.";
                    return res;
                }
                log.AppendLine("ComfyUI-Manager accepted the install request but no loader is visible yet.");
            }

            bool local = IsLocalServer(b);
            res.IsRemote = !local;
            if (!local)
            {
                res.Details = "The server (" + b + ") is not on this machine, so files cannot be installed automatically.\n\n"
                    + ManualStepsText(true);
                return res;
            }

            // (b) Local file install: resolve server root.
            Say(progress, "Installing…", -1);
            string root = await ResolveServerRootAsync(b, ct).ConfigureAwait(false);
            if (root == null)
            {
                res.Details = "Could not locate the local ComfyUI folder (tried D:\\ComfyUIPortable\\ComfyUI, "
                    + "D:\\PixalComfy\\ComfyUI, Documents\\ComfyUI and /system_stats hints).\n\n"
                    + ManualStepsText(false);
                return res;
            }
            log.AppendLine("Server root: " + root);

            string packDir = Path.Combine(root, "custom_nodes", PackDirName);
            if (!Directory.Exists(packDir))
            {
                Say(progress, "Installing… (cloning node pack)", -1);
                ProcOutcome clone = await GitCloneAsync(packDir, ct).ConfigureAwait(false);
                if (!clone.Ok)
                {
                    res.Details = "git clone failed" + (string.IsNullOrEmpty(clone.Error) ? "." : ": " + clone.Error)
                        + "\n\n" + ManualStepsText(false);
                    return res;
                }
                log.AppendLine("Cloned " + PackRepoUrl + ".");
            }
            else
            {
                log.AppendLine("Node pack already present: " + packDir);
            }

            string req = Path.Combine(packDir, "requirements.txt");
            if (File.Exists(req))
            {
                Say(progress, "Installing… (python requirements)", -1);
                ProcOutcome pip = await PipInstallRequirementsAsync(root, req, ct).ConfigureAwait(false);
                if (!pip.Ok)
                {
                    // Non-fatal: torch and friends may already be installed.
                    // The loader probe + restart prompt below is the real gate.
                    log.AppendLine("WARNING: pip install reported failure: " + pip.Error);
                }
                else
                {
                    log.AppendLine("requirements.txt installed.");
                }
            }

            // (c) Weights: models/sams/sam_vit_b_01ec64.pth (~375MB budget).
            string samsDir = Path.Combine(root, "models", "sams");
            string dest = Path.Combine(samsDir, SamVitBFileName);
            bool haveWeights = false;
            try { haveWeights = File.Exists(dest) && new FileInfo(dest).Length > 10L * 1024L * 1024L; }
            catch { haveWeights = false; }
            if (!haveWeights)
            {
                var frac = new Progress<double>(f => Say(progress, "Downloading…", f));
                Say(progress, "Downloading…", 0);
                try
                {
                    await DownloadFileAsync(SamVitBUrl, dest, SamVitBBytes, frac, ct).ConfigureAwait(false);
                    log.AppendLine("Downloaded " + SamVitBFileName + ".");
                }
                catch (Exception ex)
                {
                    res.Details = "Weights download failed: " + ex.Message + "\n\n" + ManualStepsText(false);
                    return res;
                }
            }
            else
            {
                log.AppendLine("Weights already present: " + dest);
            }

            // (d) Re-probe: a running server will not see new nodes until restart.
            Say(progress, "Probing server…", -1);
            string after = await ProbeLoaderAsync(b, ct).ConfigureAwait(false);
            res.Success = true;
            if (after != null)
            {
                res.LoaderDetected = true;
                res.Details = "Installed; found '" + after + "'. " + log;
            }
            else
            {
                res.RestartRequired = true;
                res.Details = log.ToString();
            }
            return res;
        }

        /// <summary>
        /// Poll until a SAM loader appears (use after the user restarts the
        /// server). Returns the loader class name, or null on timeout/cancel.
        /// </summary>
        public static async Task<string> WaitForLoaderAsync(
            string baseUrl, int timeoutMs, IProgress<SamInstallProgress> progress, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                ct.ThrowIfCancellationRequested();
                string l = null;
                try { l = await ProbeLoaderAsync(baseUrl, ct).ConfigureAwait(false); }
                catch { l = null; }
                if (l != null) return l;
                Say(progress, "Waiting for server…", -1);
                try { await Task.Delay(2000, ct).ConfigureAwait(false); }
                catch { break; }
            }
            return null;
        }

        /// <summary>Copy-paste fallback for offline / remote / no-git cases.</summary>
        public static string ManualStepsText(bool isRemote)
        {
            var sb = new StringBuilder();
            if (isRemote)
                sb.AppendLine("The ComfyUI server is not on this machine, so install it there:");
            else
                sb.AppendLine("Install SAM manually:");
            sb.AppendLine("1. On the machine running ComfyUI, open a terminal in the ComfyUI folder.");
            sb.AppendLine("2. git clone " + PackRepoUrl + " custom_nodes\\" + PackDirName);
            sb.AppendLine("3. Install requirements with ComfyUI's python, e.g.:");
            sb.AppendLine("     python_embeded\\python.exe -m pip install -r custom_nodes\\" + PackDirName + "\\requirements.txt");
            sb.AppendLine("4. Download the SAM ViT-B weights (~375 MB):");
            sb.AppendLine("     " + SamVitBUrl);
            sb.AppendLine("   into ComfyUI\\models\\sams\\" + SamVitBFileName);
            sb.AppendLine("5. Restart the ComfyUI server, then press AI Select (SAM) again.");
            sb.AppendLine();
            sb.AppendLine("Meanwhile 'Load mask' works today: paint white where you want selected and load it.");
            return sb.ToString();
        }

        #region Steps

        private static async Task<bool> ServerReachableAsync(string baseUrl, CancellationToken ct)
        {
            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(8);
                    using (var r = await http.GetAsync(baseUrl + "/system_stats", ct).ConfigureAwait(false))
                        return r.IsSuccessStatusCode;
                }
            }
            catch { return false; }
        }

        /// <summary>Outcome of a hidden helper-process run.</summary>
        private sealed class ProcOutcome
        {
            public bool Ok;
            public string Error = string.Empty;
        }
        /// <summary>
        /// Best-effort install through ComfyUI-Manager. Probes GET /manager
        /// first; tries a small list of install routes and returns true when
        /// one is accepted (caller re-probes /object_info to confirm).
        /// Any failure (no manager, 404s) returns false.
        /// </summary>
        private static async Task<bool> TryManagerInstallAsync(
            string baseUrl, IProgress<SamInstallProgress> progress, CancellationToken ct)
        {
            bool present = false;
            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(5);
                    using (var r = await http.GetAsync(baseUrl + "/manager", ct).ConfigureAwait(false))
                        present = r.IsSuccessStatusCode;
                }
            }
            catch { return false; }
            if (!present) return false;

            Say(progress, "Installing… (via ComfyUI-Manager)", -1);
            string[] routes = new string[]
            {
                "/manager/install",
                "/manager/action",
                "/manager/customnode/install",
            };
            foreach (string route in routes)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using (var http = new HttpClient())
                    {
                        http.Timeout = TimeSpan.FromSeconds(90);
                        var payload = "{\"url\":\"" + PackRepoUrl + "\"}";
                        using (var content = new StringContent(payload, Encoding.UTF8, "application/json"))
                        using (var resp = await http.PostAsync(baseUrl + route, content, ct).ConfigureAwait(false))
                        {
                            if (resp.IsSuccessStatusCode) return true;
                        }
                    }
                }
                catch { }
            }
            return false;
        }

        /// <summary>
        /// Local ComfyUI root: /system_stats path hints first, then known
        /// local folders. A root is valid when it holds main.py and/or
        /// custom_nodes. Null when nothing matches.
        /// </summary>
        public static async Task<string> ResolveServerRootAsync(string baseUrl, CancellationToken ct)
        {
            var tried = new List<string>();

            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(8);
                    using (var r = await http.GetAsync(NormBase(baseUrl) + "/system_stats", ct).ConfigureAwait(false))
                    {
                        if (r.IsSuccessStatusCode)
                        {
                            string body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                            foreach (string hint in ExtractComfyHints(body))
                            {
                                ct.ThrowIfCancellationRequested();
                                string root = RootFromHint(hint);
                                if (root != null && !tried.Contains(root)) tried.Add(root);
                            }
                        }
                    }
                }
            }
            catch { }

            tried.Add(@"D:\ComfyUIPortable\ComfyUI");
            tried.Add(@"D:\PixalComfy\ComfyUI");
            try { tried.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ComfyUI")); }
            catch { }
            try { tried.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ComfyUI")); }
            catch { }
            tried.Add(@"C:\ComfyUI");

            foreach (string c in tried)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (IsServerRoot(c)) return c;
                }
                catch { }
            }
            return null;
        }

        private static bool IsServerRoot(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            try
            {
                if (File.Exists(Path.Combine(dir, "main.py"))) return true;
                if (Directory.Exists(Path.Combine(dir, "custom_nodes"))) return true;
            }
            catch { }
            return false;
        }

        private static List<string> ExtractComfyHints(string body)
        {
            var out_ = new List<string>();
            if (string.IsNullOrEmpty(body)) return out_;
            try
            {
                var rx = new Regex(@"[A-Za-z]:\\[^\s""'<>|]+", RegexOptions.Compiled);
                foreach (Match m in rx.Matches(body))
                {
                    string p = m.Value.TrimEnd('\\', '.', ',', ';');
                    if (p.IndexOf("omfy", StringComparison.OrdinalIgnoreCase) >= 0
                        && !out_.Contains(p)) out_.Add(p);
                }
            }
            catch { }
            return out_;
        }

        private static string RootFromHint(string hint)
        {
            try
            {
                string cur = hint;
                try
                {
                    if (File.Exists(cur)) cur = Path.GetDirectoryName(cur);
                }
                catch { }
                for (int i = 0; i < 4 && !string.IsNullOrEmpty(cur); i++)
                {
                    if (IsServerRoot(cur)) return cur;
                    try { cur = Path.GetDirectoryName(cur); }
                    catch { break; }
                }
            }
            catch { }
            return null;
        }

        private static async Task<ProcOutcome> GitCloneAsync(string destDir, CancellationToken ct)
        {
            var outcome = new ProcOutcome();
            try
            {
                string parent = Path.GetDirectoryName(destDir);
                if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
                if (Directory.Exists(destDir))
                {
                    outcome.Error = "destination already exists";
                    return outcome;
                }
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "clone --depth 1 \"" + PackRepoUrl + "\" \"" + destDir + "\"",
                    WorkingDirectory = parent,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = true,
                };
                using (var proc = new Process { StartInfo = psi })
                {
                    try { proc.Start(); }
                    catch (Exception ex)
                    {
                        outcome.Error = "git not found (" + ex.Message + ")";
                        return outcome;
                    }
                    var sw = Stopwatch.StartNew();
                    const int timeoutMs = 10 * 60 * 1000;
                    while (!proc.HasExited)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            try { proc.Kill(); } catch { }
                            ct.ThrowIfCancellationRequested();
                        }
                        if (sw.ElapsedMilliseconds > timeoutMs)
                        {
                            try { proc.Kill(); } catch { }
                            outcome.Error = "timed out after 10 min";
                            return outcome;
                        }
                        try { if (proc.WaitForExit(500)) break; }
                        catch { break; }
                    }
                    string stderr = string.Empty;
                    try { stderr = proc.StandardError.ReadToEnd(); } catch { }
                    int code = -1;
                    try { code = proc.HasExited ? proc.ExitCode : -1; } catch { }
                    if (code != 0 || !Directory.Exists(destDir))
                    {
                        outcome.Error = (stderr ?? string.Empty).Trim();
                        if (outcome.Error.Length > 500) outcome.Error = outcome.Error.Substring(0, 500);
                        if (outcome.Error.Length == 0) outcome.Error = "exit " + code;
                        return outcome;
                    }
                    outcome.Ok = true;
                    return outcome;
                }
            }
            catch (Exception ex)
            {
                outcome.Error = ex.Message;
                return outcome;
            }
        }

        private static string FindServerPython(string serverRoot)
        {
            string[] cands = new string[]
            {
                Path.Combine(serverRoot, "python_embeded", "python.exe"),
                Path.Combine(serverRoot, "python_embed", "python.exe"),
                Path.Combine(serverRoot, "venv", "Scripts", "python.exe"),
                Path.Combine(serverRoot, ".venv", "Scripts", "python.exe"),
            };
            foreach (string p in cands)
            {
                try { if (File.Exists(p)) return p; }
                catch { }
            }
            return "python"; // PATH fallback
        }

        private static async Task<ProcOutcome> PipInstallRequirementsAsync(
            string serverRoot, string requirementsPath, CancellationToken ct)
        {
            var outcome = new ProcOutcome();
            try
            {
                string py = FindServerPython(serverRoot);
                string packDir = Path.GetDirectoryName(requirementsPath);
                var psi = new ProcessStartInfo
                {
                    FileName = py,
                    Arguments = "-m pip install -r \"" + requirementsPath + "\"",
                    WorkingDirectory = packDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = true,
                };
                using (var proc = new Process { StartInfo = psi })
                {
                    try { proc.Start(); }
                    catch (Exception ex)
                    {
                        outcome.Error = "could not launch python (" + ex.Message + ")";
                        return outcome;
                    }
                    var sw = Stopwatch.StartNew();
                    const int timeoutMs = 15 * 60 * 1000;
                    while (!proc.HasExited)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            try { proc.Kill(); } catch { }
                            ct.ThrowIfCancellationRequested();
                        }
                        if (sw.ElapsedMilliseconds > timeoutMs)
                        {
                            try { proc.Kill(); } catch { }
                            outcome.Error = "timed out after 15 min";
                            return outcome;
                        }
                        try { if (proc.WaitForExit(1000)) break; }
                        catch { break; }
                    }
                    string stderr = string.Empty;
                    try { stderr = proc.StandardError.ReadToEnd(); } catch { }
                    int code = -1;
                    try { code = proc.HasExited ? proc.ExitCode : -1; } catch { }
                    if (code != 0)
                    {
                        outcome.Error = (stderr ?? string.Empty).Trim();
                        if (outcome.Error.Length > 500) outcome.Error = outcome.Error.Substring(0, 500);
                        if (outcome.Error.Length == 0) outcome.Error = "exit " + code;
                        return outcome;
                    }
                    outcome.Ok = true;
                    return outcome;
                }
            }
            catch (Exception ex)
            {
                outcome.Error = ex.Message;
                return outcome;
            }
        }

        /// <summary>
        /// Stream-download with .part + atomic move (same pattern as the LLM
        /// model downloader). Fraction 0..1 reported when total is known.
        /// </summary>
        private static async Task DownloadFileAsync(
            string url, string dest, long expectedBytes, IProgress<double> fraction, CancellationToken ct)
        {
            string dir = Path.GetDirectoryName(dest);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string tmp = dest + ".part";
            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromHours(2);
                using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? expectedBytes;
                    using (var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
                    {
                        byte[] buf = new byte[1 << 16];
                        long done = 0;
                        int n;
                        while ((n = await src.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
                        {
                            await dst.WriteAsync(buf, 0, n, ct).ConfigureAwait(false);
                            done += n;
                            if (fraction != null && total > 0)
                            {
                                double f = (double)done / (double)total;
                                if (f > 1.0) f = 1.0;
                                try { fraction.Report(f); } catch { }
                            }
                        }
                    }
                }
            }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
        }

        #endregion
    }
}
