using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MeesaMultisMaker.Audio
{
    /// <summary>
    /// File playback (wav/mp3/...) plus microphone capture through
    /// winmm MCI -- no NuGet, no COM, works on stock Windows. One play
    /// alias and one record alias; UI thread only. Callers render edits
    /// to a temp WAV and hand the path here.
    /// </summary>
    public class AudioPlayer : IDisposable
    {
        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern int mciSendString(string command, StringBuilder ret,
            int retLen, IntPtr hwndCallback);

        private readonly string PlayAlias;
        private readonly string RecAlias;

        /// <summary>alias isolates MCI devices so layered players (music /
        /// foley / manual) never steal each other's streams.</summary>
        public AudioPlayer(string alias = "mm_audio_play")
        {
            if (string.IsNullOrEmpty(alias)) alias = "mm_audio_play";
            PlayAlias = alias;
            RecAlias = alias + "_rec";
        }

        private bool _disposed;

        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsRecording { get; private set; }

        private static string Send(string command)
        {
            var ret = new StringBuilder(256);
            int err = mciSendString(command, ret, ret.Capacity, IntPtr.Zero);
            if (err != 0) throw new Exception("Audio device error " + err + " on: " + command);
            return ret.ToString().Trim();
        }

        private static void Quiet(string command)
        {
            try { mciSendString(command, null, 0, IntPtr.Zero); }
            catch { }
        }

        private static string Quote(string path)
        {
            return "\"" + path.Replace("\"", "") + "\"";
        }

        #region Playback

        public void Play(string path, int fromMs = 0, int toMs = -1)
        {
            Stop();
            Send("open " + Quote(path) + " alias " + PlayAlias);
            try
            {
                Send("set " + PlayAlias + " time format milliseconds");
                string cmd = "play " + PlayAlias + " from " + Math.Max(0, fromMs);
                if (toMs >= 0 && toMs > fromMs) cmd += " to " + toMs;
                Send(cmd);
                IsPlaying = true;
                IsPaused = false;
            }
            catch
            {
                Quiet("close " + PlayAlias);
                IsPlaying = false;
                throw;
            }
        }

        public void Pause()
        {
            if (!IsPlaying || IsPaused) return;
            Send("pause " + PlayAlias);
            IsPaused = true;
        }

        public void Resume()
        {
            if (!IsPlaying || !IsPaused) return;
            Send("resume " + PlayAlias);
            IsPaused = false;
        }

        public void Stop()
        {
            try
            {
                if (IsPlaying) Quiet("stop " + PlayAlias);
            }
            finally
            {
                Quiet("close " + PlayAlias);
                IsPlaying = false;
                IsPaused = false;
            }
        }

        /// <summary>
        /// True while the stream is audibly playing (excludes finished /
        /// stopped; paused counts as not-playing). Single MCI round trip.
        /// </summary>
        public bool PollPlaying(out int positionMs, out int lengthMs)
        {
            positionMs = -1;
            lengthMs = -1;
            if (!IsPlaying) return false;
            try
            {
                string mode = Send("status " + PlayAlias + " mode");
                if (mode.StartsWith("playing", StringComparison.OrdinalIgnoreCase))
                {
                    string pos = Send("status " + PlayAlias + " position");
                    string len = Send("status " + PlayAlias + " length");
                    int ms, total;
                    if (int.TryParse(pos, out ms)) positionMs = ms;
                    if (int.TryParse(len, out total)) lengthMs = total;
                    return true;
                }
                if (mode.StartsWith("paused", StringComparison.OrdinalIgnoreCase))
                    return false;
                IsPlaying = false;
                IsPaused = false;
                return false;
            }
            catch
            {
                return IsPlaying;
            }
        }

        /// <summary>Position ms, or -1 when idle/finished.</summary>
        public int PositionMs()
        {
            if (!IsPlaying) return -1;
            try
            {
                string mode = Send("status " + PlayAlias + " mode");
                if (!mode.StartsWith("playing", StringComparison.OrdinalIgnoreCase) &&
                    !mode.StartsWith("paused", StringComparison.OrdinalIgnoreCase))
                {
                    IsPlaying = false;
                    IsPaused = false;
                    return -1;
                }
                string pos = Send("status " + PlayAlias + " position");
                int ms;
                if (int.TryParse(pos, out ms)) return ms;
            }
            catch { }
            return -1;
        }

        public int LengthMs()
        {
            if (!IsPlaying) return -1;
            try
            {
                string s = Send("status " + PlayAlias + " length");
                int ms;
                if (int.TryParse(s, out ms)) return ms;
            }
            catch { }
            return -1;
        }

        public void SetVolume(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            try
            {
                if (IsPlaying) Send("setaudio " + PlayAlias + " volume to " + (percent * 10));
            }
            catch { }
        }

        #endregion

        #region Microphone record (voice-clone reference takes)

        public void RecordStart(int sampleRate = 22050)
        {
            RecordStop();
            try
            {
                Send("open new type waveaudio alias " + RecAlias);
                Send("set " + RecAlias + " time format milliseconds");
                try { Send("set " + RecAlias + " channels 1"); }
                catch { }
                try { Send("set " + RecAlias + " samplespersec " + sampleRate); }
                catch { }
                try { Send("set " + RecAlias + " bitspersecond 16"); }
                catch { }
                Send("record " + RecAlias);
                IsRecording = true;
            }
            catch
            {
                Quiet("close " + RecAlias);
                IsRecording = false;
                throw new Exception("Microphone unavailable (in use, disabled, or no input device).");
            }
        }

        public int RecordPositionMs()
        {
            if (!IsRecording) return 0;
            try
            {
                string pos = Send("status " + RecAlias + " position");
                int ms;
                if (int.TryParse(pos, out ms)) return ms;
            }
            catch { }
            return 0;
        }

        /// <summary>Stop and write the take to a WAV file.</summary>
        public void RecordStop(string savePath)
        {
            try
            {
                if (!IsRecording) return;
                Send("stop " + RecAlias);
                Send("save " + RecAlias + " " + Quote(savePath));
            }
            finally
            {
                Quiet("close " + RecAlias);
                IsRecording = false;
            }
        }

        public void RecordStop()
        {
            try { if (IsRecording) Quiet("stop " + RecAlias); }
            finally
            {
                Quiet("close " + RecAlias);
                IsRecording = false;
            }
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { RecordStop(); }
            catch { }
            try { Stop(); }
            catch { }
        }
    }
}
