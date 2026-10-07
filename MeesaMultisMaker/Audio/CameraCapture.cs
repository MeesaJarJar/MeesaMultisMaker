using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;

namespace MeesaMultisMaker.Audio
{
    /// <summary>
    /// Webcam frames via ffmpeg's DirectShow path (sees OBS Virtual Camera
    /// and real webcams uniformly -- avicap32 only enumerates legacy VfW
    /// drivers and cannot see OBS at all). No preview window: frames are
    /// grabbed per-shot for preview refresh + vision snapshots.
    /// </summary>
    public static class CameraCapture
    {
        /// <summary>Video capture device names, e.g. "OBS Virtual Camera".</summary>
        public static List<string> ListVideoDevices(int timeoutMs = 15000)
        {
            string ffmpeg = AudioConvert.FindFfmpeg();
            if (string.IsNullOrEmpty(ffmpeg))
                throw new Exception("ffmpeg not found (needed for camera capture).");
            // NOTE: no -loglevel here: the device list itself is printed
            // at info level, so silencing logs would erase the answer.
            var psi = new ProcessStartInfo(ffmpeg,
                "-hide_banner -nostats -list_devices true -f dshow -i dummy");
            psi.UseShellExecute = false;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            string err;
            using (var proc = new Process { StartInfo = psi })
            {
                proc.Start();
                err = proc.StandardError.ReadToEnd();
                if (!proc.WaitForExit(Math.Max(5000, timeoutMs)))
                {
                    try { proc.Kill(); }
                    catch { }
                    throw new Exception("Camera enumeration timed out.");
                }
            }
            var names = new List<string>();
            // ffmpeg prints: [dshow @ ...] "OBS Virtual Camera" (video)
            foreach (Match m in Regex.Matches(err ?? string.Empty, "\"([^\"]+)\"\\s*\\(video\\)"))
            {
                string name = m.Groups[1].Value.Trim();
                if (name.Length > 0 && !names.Contains(name)) names.Add(name);
            }
            return names;
        }

        /// <summary>Grab one JPEG frame from a DirectShow device.</summary>
        public static byte[] GrabFrameJpeg(string device, int maxDim, int timeoutMs = 25000)
        {
            if (string.IsNullOrEmpty(device))
                throw new Exception("No camera selected.");
            string ffmpeg = AudioConvert.FindFfmpeg();
            if (string.IsNullOrEmpty(ffmpeg))
                throw new Exception("ffmpeg not found (needed for camera capture).");
            var psi = new ProcessStartInfo(ffmpeg,
                "-hide_banner -nostats -loglevel error -f dshow -i video=\"" + device.Replace("\"", "") + "\"" +
                " -frames:v 1 -q:v 4 -f mjpeg pipe:1");
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            byte[] raw;
            string err;
            using (var proc = new Process { StartInfo = psi })
            {
                proc.Start();
                var chunks = new List<byte[]>(8);
                int total = 0;
                byte[] buf = new byte[1 << 16];
                int n;
                var stdout = proc.StandardOutput.BaseStream;
                while ((n = stdout.Read(buf, 0, buf.Length)) > 0)
                {
                    var chunk = new byte[n];
                    Buffer.BlockCopy(buf, 0, chunk, 0, n);
                    chunks.Add(chunk);
                    total += n;
                    if (total > 64 * 1024 * 1024)
                    {
                        try { proc.Kill(); }
                        catch { }
                        throw new Exception("Frame too large, aborting.");
                    }
                }
                err = proc.StandardError.ReadToEnd();
                if (!proc.WaitForExit(Math.Max(5000, timeoutMs)))
                {
                    try { proc.Kill(); }
                    catch { }
                    throw new Exception("Frame grab timed out.");
                }
                if (proc.ExitCode != 0 || total == 0)
                    throw new Exception("Frame grab failed" +
                        (string.IsNullOrEmpty(err) ? "." : ": " + Tail(err.Trim(), 300)));
                raw = new byte[total];
                int at = 0;
                foreach (var chunk in chunks)
                {
                    Buffer.BlockCopy(chunk, 0, raw, at, chunk.Length);
                    at += chunk.Length;
                }
            }
            try
            {
                using (var ms = new MemoryStream(raw, false))
                using (var src = new Bitmap(ms))
                {
                    int w = src.Width, h = src.Height;
                    if (w <= 0 || h <= 0) throw new Exception("Empty frame.");
                    double f = Math.Min(1.0, (double)Math.Max(64, maxDim) / Math.Max(w, h));
                    int dw = Math.Max(64, (int)(w * f)), dh = Math.Max(64, (int)(h * f));
                    using (var small = new Bitmap(src, new Size(dw, dh)))
                    using (var outMs = new MemoryStream())
                    {
                        var enc = GetEncoder(ImageFormat.Jpeg);
                        if (enc == null)
                        {
                            small.Save(outMs, ImageFormat.Jpeg);
                        }
                        else
                        {
                            var ep = new EncoderParameters(1);
                            try
                            {
                                ep.Param[0] = new EncoderParameter(Encoder.Quality, 70L);
                                small.Save(outMs, enc, ep);
                            }
                            finally { ep.Dispose(); }
                        }
                        return outMs.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Frame decode failed: " + ex.Message);
            }
        }

        private static string Tail(string s, int max)
        {
            if (s == null) return string.Empty;
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(s.Length - max);
        }

        private static ImageCodecInfo GetEncoder(ImageFormat fmt)
        {
            try
            {
                foreach (var c in ImageCodecInfo.GetImageEncoders())
                    if (c.FormatID == fmt.Guid) return c;
            }
            catch { }
            return null;
        }
    }
}
