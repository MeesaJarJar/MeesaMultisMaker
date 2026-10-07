using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace MeesaMultisMaker.Audio
{
    /// <summary>
    /// Append-only library of everything the Audio Editor generates:
    /// one JSON line per entry in library.jsonl under Documents, each with
    /// its prompt, kind, seed, workflow, and the saved audio path. Entries
    /// whose files go missing are surfaced (not silently dropped) and can
    /// be pruned. Radio draws from here; the describe prompt lives in
    /// AppConfig, not the library.
    /// </summary>
    public class LibraryEntry
    {
        public string Id = string.Empty;
        public DateTime Created = DateTime.Now;
        public string Kind = string.Empty; // SFX, Music, VoiceClone, VoiceDesign, WatcherFoley, WatcherMusic
        public string Prompt = string.Empty;
        public long Seed;
        public string Workflow = string.Empty;
        public string AudioPath = string.Empty;
        public double DurationSec;
        public string Scene = string.Empty; // watcher scene description, else empty
        public string Caption = string.Empty; // what the clip actually contains (captioner)

        public bool FileAlive
        {
            get
            {
                try { return !string.IsNullOrEmpty(AudioPath) && File.Exists(AudioPath); }
                catch { return false; }
            }
        }

        public string Label
        {
            get
            {
                string p = string.IsNullOrEmpty(Prompt) ? "(no prompt)" : Prompt;
                if (p.Length > 64) p = p.Substring(0, 64) + "...";
                return string.Format("[{0:MM-dd HH:mm}] {1}: {2}", Created, Kind, p);
            }
        }
    }

    public static class AudioLibrary
    {
        private static readonly object _gate = new object();
        private static readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static string LibraryPath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "MeesaMultisMaker", "Audio");
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); }
            catch { }
            return Path.Combine(dir, "library.jsonl");
        }

        public static void Append(LibraryEntry entry)
        {
            if (entry == null) return;
            if (string.IsNullOrEmpty(entry.Id)) entry.Id = Guid.NewGuid().ToString("N");
            try
            {
                var dict = new Dictionary<string, object>
                {
                    { "id", entry.Id },
                    { "created", entry.Created.ToString("o") },
                    { "kind", entry.Kind ?? string.Empty },
                    { "prompt", entry.Prompt ?? string.Empty },
                    { "seed", entry.Seed },
                    { "workflow", entry.Workflow ?? string.Empty },
                    { "audio", entry.AudioPath ?? string.Empty },
                    { "duration", entry.DurationSec },
                    { "scene", entry.Scene ?? string.Empty },
                    { "caption", entry.Caption ?? string.Empty }
                };
                lock (_gate)
                {
                    File.AppendAllText(LibraryPath(), _json.Serialize(dict) + "\r\n");
                }
            }
            catch { }
        }

        public static List<LibraryEntry> LoadAll()
        {
            var out_ = new List<LibraryEntry>();
            string path;
            try { path = LibraryPath(); }
            catch { return out_; }
            string[] lines;
            try
            {
                if (!File.Exists(path)) return out_;
                lines = File.ReadAllLines(path);
            }
            catch { return out_; }
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var dict = _json.DeserializeObject(line) as Dictionary<string, object>;
                    if (dict == null) continue;
                    var e = new LibraryEntry();
                    object v;
                    if (dict.TryGetValue("id", out v) && v is string) e.Id = (string)v;
                    if (dict.TryGetValue("created", out v) && v is string)
                    {
                        DateTime dt;
                        if (DateTime.TryParse((string)v, out dt)) e.Created = dt;
                    }
                    if (dict.TryGetValue("kind", out v) && v is string) e.Kind = (string)v;
                    if (dict.TryGetValue("prompt", out v) && v is string) e.Prompt = (string)v;
                    if (dict.TryGetValue("seed", out v)) e.Seed = ToLong(v);
                    if (dict.TryGetValue("workflow", out v) && v is string) e.Workflow = (string)v;
                    if (dict.TryGetValue("audio", out v) && v is string) e.AudioPath = (string)v;
                    if (dict.TryGetValue("duration", out v)) e.DurationSec = ToDouble(v);
                    if (dict.TryGetValue("scene", out v) && v is string) e.Scene = (string)v;
                    if (dict.TryGetValue("caption", out v) && v is string) e.Caption = (string)v;
                    if (string.IsNullOrEmpty(e.Id)) continue;
                    out_.Add(e);
                }
                catch { }
            }
            out_.Sort((a, b) => b.Created.CompareTo(a.Created));
            return out_;
        }

        /// <summary>
        /// Case-insensitive substring match over prompt + kind + scene.
        /// Empty query returns everything (newest first).
        /// </summary>
        public static List<LibraryEntry> Search(string query)
        {
            var all = LoadAll();
            if (string.IsNullOrWhiteSpace(query)) return all;
            string q = query.Trim().ToLowerInvariant();
            return all.Where(e =>
                (e.Prompt ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (e.Kind ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (e.Scene ?? string.Empty).ToLowerInvariant().Contains(q) ||
                (e.Caption ?? string.Empty).ToLowerInvariant().Contains(q)).ToList();
        }

        /// <summary>Backfill a caption onto the newest entry for an audio
        /// path (Describe buttons). False when no entry matches.</summary>
        public static bool SetCaptionByPath(string audioPath, string caption)
        {
            try
            {
                if (string.IsNullOrEmpty(audioPath)) return false;
                var all = LoadAll();
                LibraryEntry hit = null;
                foreach (var e in all)
                {
                    if (string.Equals(e.AudioPath, audioPath, StringComparison.OrdinalIgnoreCase))
                    {
                        if (hit == null || e.Created > hit.Created) hit = e;
                    }
                }
                if (hit == null) return false;
                hit.Caption = caption ?? string.Empty;
                lock (_gate)
                {
                    var lines = new List<string>(all.Count);
                    all.Sort((a, b) => a.Created.CompareTo(b.Created));
                    foreach (var e in all)
                    {
                        lines.Add(_json.Serialize(new Dictionary<string, object>
                        {
                            { "id", e.Id },
                            { "created", e.Created.ToString("o") },
                            { "kind", e.Kind ?? string.Empty },
                            { "prompt", e.Prompt ?? string.Empty },
                            { "seed", e.Seed },
                            { "workflow", e.Workflow ?? string.Empty },
                            { "audio", e.AudioPath ?? string.Empty },
                            { "duration", e.DurationSec },
                            { "scene", e.Scene ?? string.Empty },
                            { "caption", e.Caption ?? string.Empty }
                        }));
                    }
                    File.WriteAllLines(LibraryPath(), lines.ToArray());
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>Rewrite the file without entries whose audio is gone.</summary>
        public static int PruneMissing()
        {
            var all = LoadAll();
            var keep = all.Where(e => e.FileAlive).ToList();
            int dropped = all.Count - keep.Count;
            if (dropped <= 0) return 0;
            try
            {
                lock (_gate)
                {
                    var lines = new List<string>(keep.Count);
                    // Keep file order oldest-first for readability.
                    keep.Sort((a, b) => a.Created.CompareTo(b.Created));
                    foreach (var e in keep)
                    {
                        var dict = new Dictionary<string, object>
                        {
                            { "id", e.Id },
                            { "created", e.Created.ToString("o") },
                            { "kind", e.Kind ?? string.Empty },
                            { "prompt", e.Prompt ?? string.Empty },
                            { "seed", e.Seed },
                            { "workflow", e.Workflow ?? string.Empty },
                            { "audio", e.AudioPath ?? string.Empty },
                            { "duration", e.DurationSec },
                            { "scene", e.Scene ?? string.Empty },
                            { "caption", e.Caption ?? string.Empty }
                        };
                        lines.Add(_json.Serialize(dict));
                    }
                    File.WriteAllLines(LibraryPath(), lines.ToArray());
                }
            }
            catch { return 0; }
            return dropped;
        }

        private static long ToLong(object v)
        {
            try
            {
                if (v is long) return (long)v;
                if (v is int) return (int)v;
                if (v is double) return (long)(double)v;
                if (v is string)
                {
                    long r;
                    if (long.TryParse((string)v, out r)) return r;
                }
            }
            catch { }
            return 0;
        }

        private static double ToDouble(object v)
        {
            try
            {
                if (v is double) return (double)v;
                if (v is int) return (int)v;
                if (v is long) return (long)v;
                if (v is string)
                {
                    double r;
                    if (double.TryParse((string)v,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out r)) return r;
                }
            }
            catch { }
            return 0;
        }
    }
}
