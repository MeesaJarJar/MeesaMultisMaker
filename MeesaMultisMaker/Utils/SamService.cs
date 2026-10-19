using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MeesaMultisMaker.Utils
{
    /// <summary>
    /// SAM 2.1 point-prompt segmentation gate (spatial "SAM Select").
    /// Model side is external and verified: python + sam_select.py + checkpoint
    /// below. This class only shells out to it hidden/off-UI-thread and loads
    /// the resulting 0/255 L-mode mask PNG. Never mutates caller art.
    /// </summary>
    public static class SamService
    {
        public const string PythonPath = @"D:\mms_pbr_venv\Scripts\python.exe";
        public const string ScriptPath = @"D:\mms_pbr\sam_select.py";
        public const string CheckpointPath = @"D:\mms_pbr\models\sam2.1\sam2.1_hiera_large.pt";
        public const string WorkingDirectory = @"D:\mms_pbr";
        public const int TimeoutMs = 120000;

        /// <summary>First missing SAM path, or null when all present.</summary>
        public static string GetMissingPath()
        {
            try
            {
                if (!File.Exists(PythonPath)) return PythonPath;
                if (!File.Exists(ScriptPath)) return ScriptPath;
                if (!File.Exists(CheckpointPath)) return CheckpointPath;
                if (!Directory.Exists(WorkingDirectory)) return WorkingDirectory;
            }
            catch { }
            return null;
        }

        public static bool IsAvailable => GetMissingPath() == null;

        /// <summary>Throws FileNotFoundException naming the missing path.</summary>
        public static void EnsureAvailable()
        {
            string missing = GetMissingPath();
            if (missing != null)
                throw new FileNotFoundException("SAM files missing: " + missing, missing);
        }

        /// <summary>
        /// Runs sam_select.py off the UI thread and returns the mask Bitmap.
        /// Caller owns the returned Bitmap and must dispose it.
        /// Mask is 0/255 L-mode PNG, same WxH as <paramref name="image"/>.
        /// </summary>
        public static async Task<Bitmap> RunSamAsync(
            Bitmap image,
            List<(int x, int y, int label)> points,
            CancellationToken ct)
        {
            EnsureAvailable();
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (points == null || points.Count == 0)
                throw new InvalidOperationException("SAM needs at least one point.");
            // Clone inputs that GDI+ might touch from the UI thread.
            int w, h;
            try { w = image.Width; h = image.Height; }
            catch (Exception ex) { throw new InvalidOperationException("Source image is not usable.", ex); }
            if (w <= 0 || h <= 0) throw new InvalidOperationException("Source image is empty.");
            var ptsCopy = points.Select(p => (p.x, p.y, p.label)).ToList();

            // Must never block the canvas: the whole blocking process wait
            // runs on a pool thread. The awaiting UI thread stays responsive.
            return await Task.Run(() => RunSamSync(image, ptsCopy, ct), ct).ConfigureAwait(false);
        }

        private static Bitmap RunSamSync(
            Bitmap image,
            List<(int x, int y, int label)> points,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string inPath = Path.Combine(Path.GetTempPath(), "sam_in_" + Guid.NewGuid().ToString("N") + ".png");
            string outPath = Path.Combine(Path.GetTempPath(), "sam_out_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                // Save source to temp PNG (same WxH; sam_select.py segments this).
                using (var copy = new Bitmap(image))
                    copy.Save(inPath, ImageFormat.Png);
                ct.ThrowIfCancellationRequested();

                string pointsArg = string.Join(";", points.Select(p => p.x + "," + p.y + "," + p.label));
                string args = "\"" + ScriptPath + "\" --image \"" + inPath + "\" --points \"" + pointsArg + "\" --out \"" + outPath + "\"";

                var psi = new ProcessStartInfo
                {
                    FileName = PythonPath,
                    Arguments = args,
                    WorkingDirectory = WorkingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                string stderr;
                int exitCode;
                using (var proc = new Process { StartInfo = psi, EnableRaisingEvents = false })
                {
                    try { proc.Start(); }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("Failed to launch SAM python (" + PythonPath + "): " + ex.Message, ex);
                    }
                    // Poll so cancellation + timeout can kill the runaway process.
                    var sw = Stopwatch.StartNew();
                    while (!proc.HasExited)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            try { proc.Kill(); } catch { }
                            ct.ThrowIfCancellationRequested();
                        }
                        if (sw.ElapsedMilliseconds > TimeoutMs)
                        {
                            try { proc.Kill(); } catch { }
                            throw new TimeoutException("SAM segmentation timed out after " + (TimeoutMs / 1000) + "s.");
                        }
                        // Wait in short slices; true => exited.
                        try
                        {
                            if (proc.WaitForExit(500)) break;
                        }
                        catch { break; }
                    }
                    try { stderr = proc.StandardError.ReadToEnd(); }
                    catch { stderr = string.Empty; }
                    try { exitCode = proc.HasExited ? proc.ExitCode : -1; }
                    catch { exitCode = -1; }
                }

                ct.ThrowIfCancellationRequested();

                if (exitCode != 0 || !File.Exists(outPath))
                {
                    string err = (stderr ?? string.Empty).Trim();
                    if (err.Length > 2000) err = err.Substring(0, 2000);
                    throw new InvalidOperationException(
                        "SAM segmentation failed (exit " + exitCode + ")." +
                        (string.IsNullOrEmpty(err) ? string.Empty : " stderr: " + err));
                }

                // Load mask and detach from the temp file so temps can be deleted.
                using (var tmp = new Bitmap(outPath))
                    return new Bitmap(tmp);
            }
            finally
            {
                try { if (File.Exists(inPath)) File.Delete(inPath); } catch { }
                try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
            }
        }
    }
}
