using MeesaMultisMaker.LLM;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Embedded LLM prompt assistance (Qwen3.5 0.8B, in-process child
    /// server, no Ollama needed): Random Prompt button + ghost-text
    /// autocomplete inside the prompt box. The prompt box stays fully
    /// editable; suggestions never overwrite user text unasked.
    /// </summary>
    public partial class ThreeDEditorForm
    {
        private Button randomPromptButton;
        private Label _ghost;
        private System.Windows.Forms.Timer _acTimer;
        private CancellationTokenSource _llmCts;
        private CancellationTokenSource _acCts;
        private int _acSerial;
        private bool _accepting;
        private bool _progSet;
        private bool _llmBusy;
        private bool _acEnabled = false; // autocomplete temporarily disabled by request

        private const string IdeaSystem =
            "You are an idea generator. You respond with a description of an object or group of objects.";

        private const string IdeaUser =
            "Respond with a description of an object that one might find in the 1400s. Only the description, nothing else.";

        private const string CompleteSystem = "";

        private TextBox agentPromptBox;
        private TextBox ideaPromptBox;

        private string AgentPrompt()
        {
            try
            {
                string t = agentPromptBox != null ? agentPromptBox.Text.Trim() : string.Empty;
                if (t.Length > 0) return agentPromptBox.Text;
            }
            catch { }
            return IdeaSystem;
        }

        private string IdeaPrompt()
        {
            try
            {
                string t = ideaPromptBox != null ? ideaPromptBox.Text.Trim() : string.Empty;
                if (t.Length > 0) return ideaPromptBox.Text;
            }
            catch { }
            return IdeaUser;
        }

        private void SaveLlmPrompts()
        {
            try
            {
                var cfg = AppConfig.Instance;
                if (agentPromptBox != null) cfg.LlmAgentPrompt = agentPromptBox.Text;
                if (ideaPromptBox != null) cfg.LlmIdeaPrompt = ideaPromptBox.Text;
                cfg.Save();
            }
            catch { }
        }

        private void BuildLlmPromptUi(Panel left, ref int y)
        {
            var llmTip = new ToolTip { ShowAlways = true };

            var agentLbl = new Label
            {
                Location = new Point(10, y), Width = 320, Height = 16,
                Text = "Agent instruction (LLM system):",
                ForeColor = Color.Gray
            };
            left.Controls.Add(agentLbl);
            y += 18;
            agentPromptBox = new TextBox
            {
                Location = new Point(10, y), Width = 320, Height = 36,
                Multiline = true, ScrollBars = ScrollBars.Vertical,
                Text = AppConfig.Instance.LlmAgentPrompt
            };
            agentPromptBox.Leave += (s, e) => SaveLlmPrompts();
            left.Controls.Add(agentPromptBox);
            try { llmTip.SetToolTip(agentPromptBox, "System instruction for the local idea-generator LLM. Saved automatically."); } catch { }
            y += 42;

            var ideaLbl = new Label
            {
                Location = new Point(10, y), Width = 320, Height = 16,
                Text = "Idea prompt (LLM user message):",
                ForeColor = Color.Gray
            };
            left.Controls.Add(ideaLbl);
            y += 18;
            ideaPromptBox = new TextBox
            {
                Location = new Point(10, y), Width = 320, Height = 48,
                Multiline = true, ScrollBars = ScrollBars.Vertical,
                Text = AppConfig.Instance.LlmIdeaPrompt
            };
            ideaPromptBox.Leave += (s, e) => SaveLlmPrompts();
            left.Controls.Add(ideaPromptBox);
            try { llmTip.SetToolTip(ideaPromptBox, "Sent to the local LLM every time an idea is needed (Random prompt button, loop-with-random-prompt). Saved automatically."); } catch { }
            y += 54;

            randomPromptButton = new Button
            {
                Location = new Point(10, y), Width = 320, Height = 26,
                Text = "Random prompt"
            };
            randomPromptButton.Click += RandomPrompt_Click;
            left.Controls.Add(randomPromptButton);
            var tip = new ToolTip { ShowAlways = true };
            try
            {
                tip.SetToolTip(randomPromptButton,
                    "Generate a new prompt idea with the embedded Qwen3.5 0.8B model (runs locally, no server needed).");
                tip.SetToolTip(promptTextBox,
                    "Type freely. Pause and the model suggests the rest in gray - Tab accepts, Esc dismisses.");
            }
            catch { }
            y += 32;

            _ghost = new Label
            {
                Visible = false,
                ForeColor = Color.Gray,
                BackColor = Color.Transparent,
                AutoSize = true,
                Font = promptTextBox.Font
            };
            promptTextBox.Controls.Add(_ghost);

            promptTextBox.TextChanged += PromptBox_TextChanged;
            promptTextBox.PreviewKeyDown += PromptBox_PreviewKeyDown;
            promptTextBox.KeyDown += PromptBox_KeyDown;

            _acTimer = new System.Windows.Forms.Timer { Interval = 450 };
            _acTimer.Tick += AcTimer_Tick;
        }

        #region Random prompt

        /// <summary>
        /// One idea from the local LLM (null when cancelled, missing
        /// model declined, or empty). Shared by the button and loop mode.
        /// </summary>
        private async Task<string> GenerateRandomPromptAsync(CancellationToken ct)
        {
            if (!await EnsureLlmModelAsync(ct)) return null;
            ct.ThrowIfCancellationRequested();
            string raw = await LlamaEngine.ChatAsync(AgentPrompt(), IdeaPrompt(), 60, 1.0f,
                new string[] { "\n<|im_end|>", "<|im_end|>" }, ct);
            return ForceBlackBackground(CleanIdea(raw));
        }

        private void RandomPrompt_Click(object sender, EventArgs e)
        {
            if (_llmBusy) return;
            _llmBusy = true;
            randomPromptButton.Enabled = false;
            HideGhost();
            try
            {
                if (_llmCts != null) { try { _llmCts.Cancel(); } catch { } }
                _llmCts = new CancellationTokenSource();
            }
            catch { _llmCts = new CancellationTokenSource(); }
            var ct = _llmCts.Token;
            Task.Run(async () =>
            {
                try
                {
                    SetStatus("Dreaming up a prompt…", HolographicTheme.BlueAccent);
                    string idea = await GenerateRandomPromptAsync(ct);
                    if (string.IsNullOrEmpty(idea))
                    {
                        Log("Random prompt came back empty; try again.");
                        SetStatus("LLM gave nothing usable.", Color.Orange);
                        return;
                    }
                    BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            _progSet = true;
                            HideGhost();
                            try { _acTimer.Stop(); } catch { }
                            promptTextBox.Text = idea;
                            promptTextBox.SelectionStart = promptTextBox.Text.Length;
                            promptTextBox.SelectionLength = 0;
                        }
                        finally { _progSet = false; }
                        Log("Random prompt: " + idea);
                        SetStatus("Idle.", Color.Gray);
                    }));
                }
                catch (OperationCanceledException)
                {
                    SetStatus("Cancelled.", Color.Orange);
                }
                catch (Exception ex)
                {
                    Log("Random prompt failed: " + ex.Message);
                    SetStatus("LLM error.", Color.Red);
                }
                finally
                {
                    BeginInvoke(new Action(() =>
                    {
                        _llmBusy = false;
                        randomPromptButton.Enabled = true;
                    }));
                }
            });
        }

        /// <summary>
        /// The app owns the background, always: strip anything the model
        /// said about backdrops and append " on a black background".
        /// </summary>
        private static string ForceBlackBackground(string idea)
        {
            if (string.IsNullOrEmpty(idea)) return string.Empty;
            string t = idea.Trim();
            string[] tails = new string[]
            {
                @"[,.\s]*\bset against .*$",
                @"[,.\s]*\bagainst .*background$",
                @"[,.\s]*\bon .*background$",
                @"[,.\s]*\bwith .*background$",
                @"[,.\s]*black background$",
                @"[,.\s]*\bon a$",
            };
            bool changed;
            do
            {
                changed = false;
                foreach (string rx in tails)
                {
                    string n = System.Text.RegularExpressions.Regex.Replace(
                        t, rx, string.Empty,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase).TrimEnd(' ', ',', '.', ';', ':');
                    if (n.Length != t.Length) { t = n; changed = true; }
                }
            } while (changed && t.Length > 0);
            if (t.Length < 4) return string.Empty;
            return t + " on a black background";
        }

        private static string CleanIdea(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            string[] lines = raw.Replace("\r", "\n").Split('\n');
            foreach (string ln in lines)
            {
                string t = ln.Trim().TrimStart('-', '*', '•', '>', ' ', '\t');
                // Strip "1. "/"1) " style prefixes.
                int k = 0;
                while (k < t.Length && char.IsDigit(t[k])) k++;
                if (k > 0 && k < t.Length && (t[k] == '.' || t[k] == ')' || t[k] == ':'))
                    t = t.Substring(k + 1).TrimStart();
                t = t.Trim().Trim('"', '\'', '“', '”');
                if (t.Length >= 4) return t;
            }
            return string.Empty;
        }

        private async Task<bool> EnsureLlmModelAsync(CancellationToken ct)
        {
            if (LlamaEngine.FindModel() != null) return true;
            DialogResult dr = DialogResult.No;
            try
            {
                dr = (DialogResult)Invoke(new Func<DialogResult>(() => MessageBox.Show(this,
                    "Prompt ideas need the Qwen3.5 0.8B model (532 MB, one-time download).\n\nDownload it now?",
                    "3D Editor", MessageBoxButtons.YesNo, MessageBoxIcon.Question)));
            }
            catch { return false; }
            if (dr != DialogResult.Yes) return false;
            try
            {
                var prog = new Progress<double>(f =>
                {
                    try { SetStatus(string.Format("Downloading LLM model {0:0}%…", f * 100), HolographicTheme.BlueAccent); }
                    catch { }
                });
                await LlamaEngine.DownloadModelAsync(LlamaEngine.DefaultDownloadPath(), prog, ct);
                Log("LLM model downloaded.");
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log("LLM download failed: " + ex.Message);
                SetStatus("LLM download failed.", Color.Red);
                return false;
            }
        }

        #endregion

        #region Autocomplete ghost text

        private void HideGhost()
        {
            try
            {
                if (_ghost != null) _ghost.Visible = false;
            }
            catch { }
        }

        private void PromptBox_TextChanged(object sender, EventArgs e)
        {
            if (_accepting || _progSet) return;
            if (!_acEnabled) { HideGhost(); return; }
            HideGhost();
            try
            {
                if (_acTimer == null) return;
                _acTimer.Stop();
                string text = promptTextBox.Text;
                if (string.IsNullOrWhiteSpace(text)) return;
                if (LlamaEngine.FindModel() == null) return; // never auto-download while typing
                _acTimer.Start();
            }
            catch { }
        }

        private void PromptBox_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            if (e.KeyCode == Keys.Tab)
                e.IsInputKey = true;
        }

        private void PromptBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && _ghost != null && _ghost.Visible)
            {
                HideGhost();
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Tab && _ghost != null && _ghost.Visible && !string.IsNullOrEmpty(_ghost.Text))
            {
                try
                {
                    _accepting = true;
                    int caret = promptTextBox.SelectionStart;
                    promptTextBox.SelectedText = _ghost.Text;
                    promptTextBox.SelectionStart = caret + _ghost.Text.Length;
                    promptTextBox.SelectionLength = 0;
                }
                finally { _accepting = false; }
                HideGhost();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        }

        private void AcTimer_Tick(object sender, EventArgs e)
        {
            try { _acTimer.Stop(); }
            catch { }
            string text = string.Empty;
            int caret = 0;
            try
            {
                text = promptTextBox.Text;
                caret = promptTextBox.SelectionStart;
                if (string.IsNullOrWhiteSpace(text)) return;
                if (LlamaEngine.FindModel() == null) return;
            }
            catch { return; }

            int serial;
            unchecked { serial = ++_acSerial; }
            try { if (_acCts != null) _acCts.Cancel(); } catch { }
            _acCts = new CancellationTokenSource();
            var token = _acCts.Token;
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(50, token);
                    // Only complete the line the caret is on.
                    int lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
                    int lineEnd = text.IndexOf('\n', caret);
                    if (lineEnd < 0) lineEnd = text.Length;
                    string before = text.Substring(lineStart, Math.Max(0, caret - lineStart));
                    string after = text.Substring(caret, Math.Max(0, lineEnd - caret));
                    if (string.IsNullOrWhiteSpace(before) && string.IsNullOrWhiteSpace(after)) return;
                    string raw = await LlamaEngine.ChatAsync(CompleteSystem,
                        "Complete this prompt fragment: \"" + before + "\"",
                        24, 0.4f, new string[] { "\n" }, token);
                    string suggestion = FirstLine(raw);
                    if (string.IsNullOrEmpty(suggestion)) return;
                    // Avoid doubled spaces at the joint.
                    if (before.Length > 0 && char.IsWhiteSpace(before[before.Length - 1]) &&
                        suggestion.Length > 0 && char.IsWhiteSpace(suggestion[0]))
                        suggestion = suggestion.TrimStart();
                    // Don't suggest text already there.
                    if (!string.IsNullOrEmpty(after) && suggestion.StartsWith(after))
                        suggestion = suggestion.Substring(after.Length);
                    if (string.IsNullOrEmpty(suggestion)) return;
                    BeginInvoke(new Action(() =>
                    {
                        if (serial != _acSerial) return;
                        if (promptTextBox.Text != text) return;
                        if (promptTextBox.SelectionStart != caret) return;
                        try
                        {
                            Point pt = promptTextBox.GetPositionFromCharIndex(caret);
                            _ghost.Location = new Point(pt.X + 1, pt.Y);
                            _ghost.Text = suggestion.Length > 140 ? suggestion.Substring(0, 140) : suggestion;
                            _ghost.Visible = true;
                            _ghost.BringToFront();
                        }
                        catch { }
                    }));
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Autocomplete failed: " + ex.Message);
                }
            });
        }

        private static string FirstLine(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            string line = raw.Replace("\r", "\n").Split('\n')[0].Trim();
            line = line.Trim('"', '\'', '“', '”', ' ');
            return line;
        }

        #endregion
    }
}
