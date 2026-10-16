using MeesaMultisMaker.Audio;
using MeesaMultisMaker.ComfyUI;
using MeesaMultisMaker.Mul;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Slot browser upgrades + replacement workflows for the UO sound
    /// panel: full filterable list with captions, one-click "generate a
    /// replacement for this slot" (prompt seeded from the slot caption,
    /// auto-staged on arrival), and bulk "replace every slot from a folder
    /// of 0xHEX.wav files" (the generated set) with a single confirmation.
    /// </summary>
    public partial class AudioEditorForm
    {
        private readonly Dictionary<int, string> _slotCaptions = new Dictionary<int, string>();
        private bool _slotCaptionsLoaded;
        private string _slotFilter = string.Empty;
        private TextBox _slotFilterBox;
        private readonly HashSet<int> _replacedSlots = new HashSet<int>();
        private int _runSlotStage = -1;
        private bool _replaceAllBusy;

        /// <summary>Best-effort load of the shipped id,caption ledger for
        /// slot display + generate prompts. Never throws.</summary>
        private void EnsureSlotCaptions()
        {
            try
            {
                if (_slotCaptionsLoaded) return;
                _slotCaptionsLoaded = true;
                string exe = AppDomain.CurrentDomain.BaseDirectory;
                string[] cands = new string[]
                {
                    Path.Combine(exe, "Audio", "UO_Sounds.csv"),
                    Path.Combine(exe, "UO_Sounds.csv"),
                    Path.Combine(exe, "Captions", "UO_Sounds.csv")
                };
                string path = null;
                foreach (string c in cands)
                {
                    try { if (File.Exists(c)) { path = c; break; } }
                    catch { }
                }
                if (path == null) return;
                using (var rd = new StreamReader(path))
                {
                    bool first = true;
                    string line;
                    while ((line = rd.ReadLine()) != null)
                    {
                        if (first) { first = false; continue; } // header
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        int comma = line.IndexOf(',');
                        if (comma <= 0) continue;
                        string idPart = line.Substring(0, comma).Trim();
                        string cap = line.Substring(comma + 1).Trim().Trim('"');
                        if (idPart.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                            idPart = idPart.Substring(2);
                        int id;
                        try { id = Convert.ToInt32(idPart, 16); }
                        catch { continue; }
                        if (!string.IsNullOrEmpty(cap) && !_slotCaptions.ContainsKey(id))
                            _slotCaptions[id] = cap;
                    }
                }
                if (_slotCaptions.Count > 0)
                    Log("Slot captions loaded (" + _slotCaptions.Count + ").");
            }
            catch { }
        }

        private string SlotCaption(int id, string fallbackName)
        {
            try
            {
                EnsureSlotCaptions();
                string c;
                if (_slotCaptions.TryGetValue(id, out c) && !string.IsNullOrEmpty(c))
                    return c;
            }
            catch { }
            return string.IsNullOrEmpty(fallbackName) ? "fantasy sound effect" : fallbackName;
        }

        private static string ShortenCaption(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            s = s.Trim();
            return s.Length > max ? s.Substring(0, max) + "..." : s;
        }

        private string BuildSlotLabel(MulSlotView v)
        {
            try
            {
                double s = v.Pcm != null ? (double)v.Pcm.Length / 2 / 22050 : 0;
                string n = string.IsNullOrEmpty(v.Name) ? "(no name)" : v.Name;
                string label = string.Format("#{0} {1}", v.Id, n);
                string cap;
                if (_slotCaptions.TryGetValue(v.Id, out cap) && !string.IsNullOrEmpty(cap))
                    label += " -- " + ShortenCaption(cap, 48);
                label += string.Format(" ({0:0.0}s)", s);
                if (_replacedSlots.Contains(v.Id)) label += " [new]";
                return label;
            }
            catch { return "#" + v.Id; }
        }

        private bool SlotPassesFilter(MulSlotView v)
        {
            try
            {
                if (string.IsNullOrEmpty(_slotFilter)) return true;
                string q = _slotFilter.ToLowerInvariant();
                if (v.Id.ToString().Contains(q)) return true;
                if (!string.IsNullOrEmpty(v.Name) && v.Name.ToLowerInvariant().Contains(q)) return true;
                string cap;
                if (_slotCaptions.TryGetValue(v.Id, out cap) && cap.ToLowerInvariant().Contains(q)) return true;
                if (("0x" + v.Id.ToString("X")).ToLowerInvariant().Contains(q)) return true;
                return false;
            }
            catch { return true; }
        }

        private void MulSlotFilter_Changed(object sender, EventArgs e)
        {
            try
            {
                _slotFilter = _slotFilterBox != null ? _slotFilterBox.Text.Trim() : string.Empty;
                int keep = -1;
                try { if (mulSlotNum != null) keep = (int)mulSlotNum.Value; }
                catch { }
                RefreshMulSlotList(keep);
            }
            catch { }
        }

        /// <summary>
        /// One-click replacement: prompt seeded from the slot caption (or
        /// slot name), duration matched to the slot, SFX tab, auto-staged
        /// into the slot when the render lands. No dialog on arrival: the
        /// button press is the confirmation (logged).
        /// </summary>
        private void GenSlot_Click(object sender, EventArgs e)
        {
            try
            {
                if (_slotViews.Count == 0)
                {
                    MessageBox.Show(this, "Load the sounds first (Reload).", "Audio Editor");
                    return;
                }
                if (_cts != null)
                {
                    MessageBox.Show(this, "A generation is already running.", "Audio Editor");
                    return;
                }
                int id = mulSlotNum != null ? (int)mulSlotNum.Value : -1;
                var view = FindSlotView(id);
                if (view == null)
                {
                    MessageBox.Show(this, "Pick an existing slot first.", "Audio Editor");
                    return;
                }
                string caption = SlotCaption(id, view.Name);
                double seconds = view.Pcm != null ? (double)view.Pcm.Length / 2 / 22050 : 5;
                int dur = Math.Max(1, Math.Min(600, (int)Math.Round(seconds)));
                if (promptSfxBox == null || durSfxNum == null)
                {
                    MessageBox.Show(this, "SFX tab is not available.", "Audio Editor");
                    return;
                }
                promptSfxBox.Text = caption + " Length: " + dur + " seconds";
                try
                {
                    durSfxNum.Value = Math.Max(durSfxNum.Minimum,
                        Math.Min(durSfxNum.Maximum, dur));
                }
                catch { }
                SetMode(0);
                _runSlotStage = id;
                if (!SnapshotRunParams(0, true)) { _runSlotStage = -1; return; }
                _radioMode = 0;
                _radioHold = false;
                _radioRun = false;
                Log("Generating replacement for slot " + id + " (auto-stage on arrival).");
                LaunchRun();
            }
            catch (Exception ex) { _runSlotStage = -1; MessageBox.Show(this, ex.Message, "Audio Editor"); }
        }

        private static byte[] WaveToPcm16(WaveData work, int slotFrames)
        {
            var exact = new byte[slotFrames * 2];
            int n = Math.Min(work.Samples.Length, slotFrames);
            for (int i = 0; i < n; i++)
            {
                double v = work.Samples[i];
                if (v > 1) v = 1;
                else if (v < -1) v = -1;
                short sh = (short)Math.Round(v * 32767.0);
                exact[i * 2] = (byte)(sh & 0xFF);
                exact[i * 2 + 1] = (byte)((sh >> 8) & 0xFF);
            }
            return exact;
        }

        /// <summary>
        /// Stage the loaded clip into a slot. silent skips the UOP
        /// confirmation (bulk + generate flows log instead); legacy always
        /// stages to memory until Save MULs.
        /// </summary>
        private void StageCurrentWaveToSlot(int id, bool silent)
        {
            if (_wave == null) throw new Exception("No clip loaded.");
            string folder = mulFolderBox.Text.Trim();
            if (_mulBackend == MulBackend.Uop)
            {
                var target = FindSlotView(id);
                if (target == null || target.Pcm == null)
                    throw new Exception("UOP slots are fixed-size: pick an existing slot.");
                int slotFrames = target.Pcm.Length / 2;
                var work = _wave.Clone();
                work.ToMono();
                work.Resample(22050);
                work.Normalize(0.98f);
                bool padded = work.FitToFrames(slotFrames);
                if (padded) Log("Clip padded with silence to fit slot " + id + ".");
                byte[] exact = WaveToPcm16(work, slotFrames);
                if (!silent)
                {
                    if (MessageBox.Show(this,
                        "Overwrite slot " + id + " in " + SoundUopReader.SOUND_UOP_FILENAME +
                        " now? The file is backed up to .bak (first write only).",
                        "Audio Editor", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;
                }
                SoundUopReader.ReplacePcm(folder, id, exact);
                target.Pcm = (byte[])exact.Clone();
                _replacedSlots.Add(id);
                RefreshMulInfo();
                RefreshMulSlotList(id);
                Log("Wrote clip into UOP slot " + id + ".");
                SetStatus("Slot " + id + " replaced.", System.Drawing.Color.Green);
                return;
            }
            string name = _clip != null ? Path.GetFileNameWithoutExtension(_clip.Name) : "custom";
            _sounds.Replace(id, _wave, name);
            _mulDirty = true;
            var staged = _sounds.GetSlot(id);
            var view = FindSlotView(id);
            if (staged != null)
            {
                if (view == null) { view = new MulSlotView { Id = id }; _slotViews.Add(view); }
                view.Name = staged.Name;
                view.Pcm = staged.Pcm;
                _replacedSlots.Add(id);
                RefreshMulSlotList(id);
                RefreshMulInfo();
            }
            Log("Staged clip into slot " + id + " (memory). Save MULs to write.");
            SetStatus("Staged to slot " + id + ".", System.Drawing.Color.Green);
        }

        /// <summary>
        /// Bulk replace every slot from a folder of 0xHEX.wav files
        /// (22050 mono preferred; anything is fitted). UOP writes each
        /// slot immediately (.bak kept); legacy stages everything then
        /// saves once. One confirmation up front, progress in the log.
        /// </summary>
        private void ReplaceAll_Click(object sender, EventArgs e)
        {
            try
            {
                if (_replaceAllBusy) return;
                if (_mulBackend == MulBackend.None || _slotViews.Count == 0)
                {
                    MessageBox.Show(this, "Load the sounds first (Reload).", "Audio Editor");
                    return;
                }
                string gameFolder = mulFolderBox.Text.Trim();
                if (string.IsNullOrEmpty(gameFolder) || !Directory.Exists(gameFolder))
                {
                    MessageBox.Show(this, "Set the game folder first.", "Audio Editor");
                    return;
                }
                string srcDir;
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "Folder of replacement WAVs named 0xHEX.wav (e.g. 0x001.wav)";
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    srcDir = dlg.SelectedPath;
                }
                string[] files;
                try { files = Directory.GetFiles(srcDir, "0x*.wav"); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Cannot list folder:\r\n" + ex.Message, "Audio Editor");
                    return;
                }
                if (files.Length == 0)
                {
                    MessageBox.Show(this, "No 0xHEX.wav files in:\r\n" + srcDir, "Audio Editor");
                    return;
                }
                string what = _mulBackend == MulBackend.Uop
                    ? "UOP slots write immediately (whole-file .bak kept, first write only)."
                    : "Legacy pair: staged in memory, sound.mul rewritten once at the end (.bak kept).";
                if (MessageBox.Show(this,
                    "Replace from " + files.Length + " file(s) in:\r\n" + srcDir +
                    "\r\nInto " + _slotViews.Count + " loaded slots in:\r\n" + gameFolder +
                    "\r\n" + what + "\r\nContinue?",
                    "Audio Editor", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
                _replaceAllBusy = true;
                SetStatus("Replacing all slots...", System.Drawing.Color.Blue);
                Log("Bulk replace started (" + files.Length + " files).");
                Task.Run(() => ReplaceAllRun(srcDir, gameFolder));
            }
            catch (Exception ex) { _replaceAllBusy = false; MessageBox.Show(this, ex.Message, "Audio Editor"); }
        }

        private void ReplaceAllRun(string srcDir, string gameFolder)
        {
            int ok = 0, noSlot = 0, bad = 0;
            var badMsgs = new List<string>();
            try
            {
                string[] files;
                try { files = Directory.GetFiles(srcDir, "0x*.wav"); }
                catch (Exception ex)
                {
                    Log("Bulk replace aborted: " + ex.Message);
                    return;
                }
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                int done = 0;
                foreach (string fp in files)
                {
                    try
                    {
                        string stem = Path.GetFileNameWithoutExtension(fp);
                        var m = Regex.Match(stem, "^0[xX]([0-9A-Fa-f]+)$");
                        if (!m.Success) { bad++; continue; }
                        int id = Convert.ToInt32(m.Groups[1].Value, 16);
                        var view = FindSlotView(id);
                        if (view == null || view.Pcm == null) { noSlot++; continue; }
                        WaveData work;
                        try { work = WaveData.FromFile(fp); }
                        catch (Exception ex)
                        {
                            bad++;
                            if (badMsgs.Count < 8) badMsgs.Add(Path.GetFileName(fp) + ": " + ex.Message);
                            continue;
                        }
                        int slotFrames = view.Pcm.Length / 2;
                        work.ToMono();
                        work.Resample(22050);
                        work.Normalize(0.98f);
                        work.FitToFrames(slotFrames);
                        byte[] exact = WaveToPcm16(work, slotFrames);
                        if (_mulBackend == MulBackend.Uop)
                        {
                            SoundUopReader.ReplacePcm(gameFolder, id, exact);
                            view.Pcm = (byte[])exact.Clone();
                            _replacedSlots.Add(id);
                        }
                        else
                        {
                            var fitted = new WaveData
                            {
                                SampleRate = 22050,
                                Channels = 1,
                                Samples = new float[slotFrames]
                            };
                            for (int i = 0; i < slotFrames; i++)
                            {
                                short sh = (short)(exact[i * 2] | (exact[i * 2 + 1] << 8));
                                fitted.Samples[i] = sh / 32768f;
                            }
                            _sounds.Replace(id, fitted, "custom" + id);
                            var staged = _sounds.GetSlot(id);
                            if (staged != null)
                            {
                                view.Name = staged.Name;
                                view.Pcm = staged.Pcm;
                            }
                            _replacedSlots.Add(id);
                            _mulDirty = true;
                        }
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        bad++;
                        if (badMsgs.Count < 8) badMsgs.Add(Path.GetFileName(fp) + ": " + ex.Message);
                    }
                    done++;
                    if (done % 100 == 0)
                    {
                        int d = done;
                        SafeInvoke(() => SetStatus("Replacing... " + d + " files.", System.Drawing.Color.Blue));
                    }
                }
                if (_mulBackend == MulBackend.LegacyPair)
                {
                    try
                    {
                        _sounds.Save(gameFolder);
                        _mulDirty = false;
                        Log("Saved sound.mul + soundidx.mul (.bak kept).");
                    }
                    catch (Exception ex)
                    {
                        Log("Bulk legacy save failed: " + ex.Message);
                        bad++;
                    }
                }
                int okC = ok, noC = noSlot, badC = bad;
                SafeInvoke(() =>
                {
                    try
                    {
                        int keep = -1;
                        try { if (mulSlotNum != null) keep = (int)mulSlotNum.Value; }
                        catch { }
                        RefreshMulSlotList(keep);
                        RefreshMulInfo();
                    }
                    catch { }
                });
                Log(string.Format("Bulk replace done: {0} replaced, {1} no matching slot, {2} errors.",
                    okC, noC, badC));
                foreach (string m in badMsgs) Log("Bulk issue: " + m);
                string summary = string.Format(
                    "Replaced {0} slot(s).\r\n{1} file(s) had no matching slot.\r\n{2} error(s).",
                    okC, noC, badC);
                SafeInvoke(() =>
                {
                    SetStatus("Bulk replace done.", System.Drawing.Color.Green);
                    MessageBox.Show(this, summary, "Audio Editor");
                });
            }
            catch (Exception ex) { Log("Bulk replace failed: " + ex.Message); }
            finally { _replaceAllBusy = false; }
        }
    }
}
