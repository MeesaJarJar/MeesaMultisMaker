using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MeesaMultisMaker.Audio
{
    /// <summary>
    /// Master prompt ledger: one pipe-delimited row per generation in
    /// AudioPrompts.csv (next to the audio + library files). Columns:
    ///   id | created | kind | seed | duration_s | audio_file | prompt
    /// The id doubles as the server-side filename stem, so filenames stay
    /// short while the full prompt is one grep away. Reader skips blank /
    /// malformed rows and unescapes fields; the pipe is the ONLY separator
    /// (no commas, no quotes, no tabs anywhere in this file).
    /// </summary>
    public class PromptLedgerRow
    {
        public string Id = string.Empty;
        public DateTime Created;
        public string Kind = string.Empty;
        public long Seed;
        public double DurationSec;
        public string AudioFile = string.Empty;
        public string Prompt = string.Empty;
    }

    public static class AudioPromptLedger
    {
        private static readonly object _gate = new object();

        public static string LedgerPath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "MeesaMultisMaker", "Audio");
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); }
            catch { }
            return Path.Combine(dir, "AudioPrompts.csv");
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            // Pipe is the only separator: flatten it plus newlines so a row
            // is always exactly one line with exactly 7 fields.
            return s.Replace("|", "/").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static string Unescape(string s)
        {
            return (s ?? string.Empty).Trim();
        }

        public static void Append(string id, string kind, long seed, double durationSec,
            string audioFile, string prompt)
        {
            try
            {
                string path = LedgerPath();
                bool fresh = !File.Exists(path);
                var sb = new StringBuilder();
                if (fresh)
                    sb.Append("id|created|kind|seed|duration_s|audio_file|prompt\r\n");
                sb.Append(Escape(id)).Append('|')
                  .Append(DateTime.Now.ToString("o")).Append('|')
                  .Append(Escape(kind)).Append('|')
                  .Append(seed).Append('|')
                  .Append(durationSec.ToString("0.###",
                      System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                  .Append(Escape(Path.GetFileName(audioFile ?? string.Empty))).Append('|')
                  .Append(Escape(prompt)).Append("\r\n");
                lock (_gate)
                {
                    File.AppendAllText(path, sb.ToString());
                }
            }
            catch { }
        }

        public static List<PromptLedgerRow> LoadAll()
        {
            var out_ = new List<PromptLedgerRow>();
            string path;
            try { path = LedgerPath(); }
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
                string[] parts = line.Split('|');
                if (parts.Length != 7) continue; // header + malformed
                if (string.Equals(parts[0].Trim(), "id", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var row = new PromptLedgerRow { Id = Unescape(parts[0]) };
                    DateTime dt;
                    if (DateTime.TryParse(parts[1].Trim(), out dt)) row.Created = dt;
                    row.Kind = Unescape(parts[2]);
                    long seed;
                    if (long.TryParse(parts[3].Trim(), out seed)) row.Seed = seed;
                    double dur;
                    if (double.TryParse(parts[4].Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out dur)) row.DurationSec = dur;
                    row.AudioFile = Unescape(parts[5]);
                    row.Prompt = Unescape(parts[6]);
                    if (row.Id.Length == 0) continue;
                    out_.Add(row);
                }
                catch { }
            }
            return out_;
        }
    }
}
