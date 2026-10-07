using System;
using System.Collections.Generic;

namespace MeesaMultisMaker.Audio
{
    /// <summary>
    /// Change-gating for the ambient watcher: decide whether the scene
    /// actually moved on (render something new) or is static (loop what
    /// is already on air, zero GPU). Pure math, no I/O, fully harness-
    /// testable. Two stages:
    ///   frame gate  = 16x16 gray-hash distance of consecutive frames
    ///                  (cheap; skips the vision call entirely), and
    ///   prompt gate = word-Jaccard of the fresh VL prompt vs the prompt
    ///                 behind the clip currently rendering/on air (catches
    ///                 pixel churn with identical meaning, e.g. flicker).
    /// </summary>
    public static class SceneGate
    {
        /// <summary>
        /// Word-set Jaccard 0..1 of two prompts (case-insensitive,
        /// punctuation-blind). 1 = same words. Empty/empty = 1.
        /// </summary>
        public static double PromptSimilarity(string a, string b)
        {
            var sa = WordSet(a);
            var sb = WordSet(b);
            if (sa.Count == 0 && sb.Count == 0) return 1.0;
            if (sa.Count == 0 || sb.Count == 0) return 0.0;
            int inter = 0;
            foreach (string w in sa)
                if (sb.Contains(w)) inter++;
            int union = sa.Count + sb.Count - inter;
            return union <= 0 ? 1.0 : (double)inter / union;
        }

        private static HashSet<string> WordSet(string s)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(s)) return set;
            int start = -1;
            for (int i = 0; i <= s.Length; i++)
            {
                char c = i < s.Length ? s[i] : ' ';
                bool word = char.IsLetterOrDigit(c);
                if (word && start < 0) start = i;
                if (!word && start >= 0)
                {
                    if (i - start >= 2) // skip "a", "of", "to" noise
                        set.Add(s.Substring(start, i - start).ToLowerInvariant());
                    start = -1;
                }
            }
            return set;
        }

        /// <summary>
        /// Mean absolute byte distance 0..1 of two equal-length gray
        /// hashes. Returns 1 (changed) on null/length mismatch.
        /// </summary>
        public static double HashDistance(byte[] ha, byte[] hb)
        {
            if (ha == null || hb == null || ha.Length != hb.Length || ha.Length == 0)
                return 1.0;
            long acc = 0;
            for (int i = 0; i < ha.Length; i++)
            {
                int d = ha[i] - hb[i];
                acc += d < 0 ? -d : d;
            }
            return (double)acc / (ha.Length * 255.0);
        }
    }
}
