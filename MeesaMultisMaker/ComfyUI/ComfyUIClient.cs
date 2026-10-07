using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace MeesaMultisMaker.ComfyUI
{
    public class ComfyUIClient
    {
        public class OutputImage
        {
            public string Filename { get; set; }
            public string Subfolder { get; set; }
            public string Type { get; set; }
        }
        private readonly string _baseUrl;
        private readonly HttpClient _httpClient;

        public ComfyUIClient(string baseUrl = "http://localhost:8188")
        {
            // Every endpoint below is built as $"{_baseUrl}/...": a user
            // pasting "http://host:8188/" (trailing slash) produced
            // "http://host:8188//prompt", which ComfyUI rejects -- while
            // "http://localhost:8188" (no slash) worked. Normalize once.
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl)
                ? "http://localhost:8188"
                : baseUrl.Trim().TrimEnd('/');
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        public Task<string> QueuePrompt(string workflowJson)
        {
            return QueuePrompt(workflowJson, Guid.NewGuid().ToString());
        }

        /// <summary>
        /// Queue with an explicit client id. Pass the same id you connected
        /// a preview websocket with to receive live "executed" thumbnails.
        /// </summary>
        public async Task<string> QueuePrompt(string workflowJson, string clientId)
        {
            try
            {
                var payload = $"{{\"prompt\":{workflowJson},\"client_id\":\"{clientId}\"}}";

                System.Diagnostics.Debug.WriteLine($"Queueing prompt to {_baseUrl}/prompt");
                System.Diagnostics.Debug.WriteLine($"Workflow length: {workflowJson.Length} chars");

                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_baseUrl}/prompt", content);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Queue prompt failed: {response.StatusCode} - {error}");

                    // Check for common errors
                    if (error.Contains("node_errors") || error.Contains("class_type"))
                    {
                        throw new Exception($"ComfyUI workflow error - a required node may be missing.\n" +
                            "Please ensure you have the 'dreamshaper_8.safetensors' model installed.\n\n" +
                            $"Server response: {error.Substring(0, Math.Min(500, error.Length))}");
                    }

                    throw new Exception($"Failed to queue prompt: {response.StatusCode} - {error}");
                }

                var result = await response.Content.ReadAsStringAsync();

                // Debug: Log the raw response
                System.Diagnostics.Debug.WriteLine($"ComfyUI Response: {result}");

                // Try multiple patterns to find the prompt_id
                // Pattern 1: "prompt_id":"value"
                var marker = "\"prompt_id\":\"";
                int pos = result.IndexOf(marker);
                if (pos >= 0)
                {
                    pos += marker.Length;
                    int endPos = result.IndexOf("\"", pos);
                    if (endPos > pos)
                    {
                        return result.Substring(pos, endPos - pos);
                    }
                }

                // Pattern 2: "prompt_id": "value" (with space)
                marker = "\"prompt_id\": \"";
                pos = result.IndexOf(marker);
                if (pos >= 0)
                {
                    pos += marker.Length;
                    int endPos = result.IndexOf("\"", pos);
                    if (endPos > pos)
                    {
                        return result.Substring(pos, endPos - pos);
                    }
                }

                // If we still haven't found it, log the response and fail
                System.Diagnostics.Debug.WriteLine($"Failed to parse prompt_id from: {result}");
                return null;
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"QueuePrompt HTTP exception: {ex}");
                throw new Exception($"Could not connect to ComfyUI at {_baseUrl}. Please check:\n" +
                    "1. ComfyUI is running\n" +
                    "2. The URL is correct\n" +
                    "3. There's no firewall blocking the connection\n\n" +
                    $"Technical details: {ex.Message}");
            }
            catch (TaskCanceledException ex)
            {
                System.Diagnostics.Debug.WriteLine($"QueuePrompt timeout: {ex}");
                throw new Exception($"Connection to ComfyUI timed out. The server at {_baseUrl} may be overloaded or unresponsive.");
            }
            catch (Exception ex) when (ex.Message.Contains("ComfyUI"))
            {
                // Re-throw our formatted exceptions
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"QueuePrompt exception: {ex}");
                throw new Exception($"Error communicating with ComfyUI: {ex.Message}");
            }
        }

        public async Task Interrupt()
        {
            try
            {
                var response = await _httpClient.PostAsync($"{_baseUrl}/interrupt", new StringContent("{}", Encoding.UTF8, "application/json"));
                if (!response.IsSuccessStatusCode)
                {
                    // Best effort; do not throw hard here.
                    System.Diagnostics.Debug.WriteLine($"ComfyUI interrupt returned {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ComfyUI interrupt failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Get generated images from the prompt history
        /// This method polls the /history/{prompt_id} endpoint and extracts output filenames
        /// </summary>
        public async Task<List<OutputImage>> GetGeneratedImages(string promptId, int maxAttempts = 300, int pollIntervalMs = 1000)
        {
            var images = new List<OutputImage>();

            System.Diagnostics.Debug.WriteLine($"GetGeneratedImages: Starting to poll for prompt {promptId}");

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    var response = await _httpClient.GetAsync($"{_baseUrl}/history/{promptId}");

                    if (!response.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Debug.WriteLine($"History request failed (attempt {attempt + 1}): {response.StatusCode}");
                        await Task.Delay(pollIntervalMs);
                        continue;
                    }

                    var result = await response.Content.ReadAsStringAsync();

                    System.Diagnostics.Debug.WriteLine($"History response length: {result.Length}");

                    if (string.IsNullOrEmpty(result) || result == "{}" || result == "null")
                    {
                        System.Diagnostics.Debug.WriteLine($"Empty or null history (attempt {attempt + 1}), waiting...");
                        await Task.Delay(pollIntervalMs);
                        continue;
                    }

                    if (!result.Contains($"\"{promptId}\""))
                    {
                        System.Diagnostics.Debug.WriteLine($"Prompt ID not in history yet (attempt {attempt + 1}), waiting...");
                        await Task.Delay(pollIntervalMs);
                        continue;
                    }

                    if (!result.Contains("\"outputs\""))
                    {
                        System.Diagnostics.Debug.WriteLine($"No outputs section yet (attempt {attempt + 1}), waiting...");
                        await Task.Delay(pollIntervalMs);
                        continue;
                    }

                    System.Diagnostics.Debug.WriteLine($"History response sample: {result.Substring(0, Math.Min(1000, result.Length))}");

                    // Parse filenames from the outputs section
                    // ComfyUI history format: 
                    // {
                    //   "prompt_id": {
                    //     "outputs": {
                    //       "node_id": {
                    //         "images": [
                    //           {"filename": "ComfyUI_00001_.png", "subfolder": "", "type": "output"}
                    //         ]
                    //       }
                    //     }
                    //   }
                    // }

                    var imageMarker = "\"filename\"";
                    int pos = 0;
                    int safetyCounter = 0;

                    while ((pos = result.IndexOf(imageMarker, pos, StringComparison.Ordinal)) != -1 && safetyCounter++ < 200)
                    {
                        pos += imageMarker.Length;
                        while (pos < result.Length && (result[pos] == ':' || result[pos] == ' ' || result[pos] == '"')) pos++;
                        int endPos = result.IndexOf('"', pos);
                        if (endPos <= pos) break;
                        var filename = result.Substring(pos, endPos - pos);

                        int nextFilename = result.IndexOf(imageMarker, endPos, StringComparison.Ordinal);
                        string subfolder = string.Empty;
                        string imageType = "output";

                        var subMarker = "\"subfolder\"";
                        var typeMarker = "\"type\"";

                        int subPos = result.IndexOf(subMarker, endPos, StringComparison.Ordinal);
                        if (subPos != -1 && (nextFilename == -1 || subPos < nextFilename))
                        {
                            subPos += subMarker.Length;
                            while (subPos < result.Length && (result[subPos] == ':' || result[subPos] == ' ')) subPos++;
                            if (subPos < result.Length && result[subPos] == '"')
                            {
                                subPos++;
                                int subEnd = result.IndexOf('"', subPos);
                                if (subEnd > subPos)
                                    subfolder = result.Substring(subPos, subEnd - subPos);
                                else if (subEnd == subPos)
                                    subfolder = string.Empty;
                            }
                        }

                        int typePos = result.IndexOf(typeMarker, endPos, StringComparison.Ordinal);
                        if (typePos != -1 && (nextFilename == -1 || typePos < nextFilename))
                        {
                            typePos += typeMarker.Length;
                            while (typePos < result.Length && (result[typePos] == ':' || result[typePos] == ' ')) typePos++;
                            if (typePos < result.Length && result[typePos] == '"')
                            {
                                typePos++;
                                int typeEnd = result.IndexOf('"', typePos);
                                if (typeEnd > typePos)
                                    imageType = result.Substring(typePos, typeEnd - typePos);
                                else if (typeEnd == typePos)
                                    imageType = "output";
                            }
                        }

                        if (!string.IsNullOrEmpty(filename) && imageType == "output" && !images.Any(i => i.Filename == filename))
                        {
                            System.Diagnostics.Debug.WriteLine($"Found output image filename: {filename} (subfolder='{subfolder}', type='{imageType}')");
                            images.Add(new OutputImage { Filename = filename, Subfolder = subfolder, Type = imageType });
                        }
                        else if (!string.IsNullOrEmpty(filename))
                        {
                            System.Diagnostics.Debug.WriteLine($"Skipped image: {filename} (subfolder='{subfolder}', type='{imageType}', already added={images.Any(i => i.Filename == filename)})");
                        }

                        pos = endPos + 1;
                    }

                    if (images.Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"Successfully found {images.Count} image(s) after {attempt + 1} attempts");
                        return images;
                    }

                    System.Diagnostics.Debug.WriteLine($"No images found in outputs yet (attempt {attempt + 1}), waiting...");
                    await Task.Delay(pollIntervalMs);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error on attempt {attempt + 1}: {ex.Message}");
                    await Task.Delay(pollIntervalMs);
                }
            }

            System.Diagnostics.Debug.WriteLine($"ERROR: No images found after {maxAttempts} attempts");

            return images;
        }

        public async Task<byte[]> DownloadImage(OutputImage image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            System.Diagnostics.Debug.WriteLine($"DownloadImage: filename={image.Filename}, subfolder='{image.Subfolder}', type='{image.Type}'");
            return await DownloadImage(image.Filename, image.Subfolder ?? string.Empty, image.Type ?? "output");
        }

        public async Task<byte[]> DownloadImage(string filename, string subfolder = "", string type = "output")
        {
            var url = $"{_baseUrl}/view?filename={Uri.EscapeDataString(filename)}";
            if (!string.IsNullOrEmpty(subfolder))
                url += $"&subfolder={Uri.EscapeDataString(subfolder)}";
            if (!string.IsNullOrEmpty(type))
                url += $"&type={type}";

            System.Diagnostics.Debug.WriteLine($"Download URL: {url}");

            var response = await _httpClient.GetAsync(url);

            System.Diagnostics.Debug.WriteLine($"Download response: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"Download failed: {response.StatusCode}");
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes == null || bytes.Length == 0)
            {
                System.Diagnostics.Debug.WriteLine("Download returned empty content.");
                return null;
            }
            return bytes;
        }

        /// <summary>
        /// Poll /history/{promptId} until a file with one of the given extensions
        /// appears (e.g. ".ply"). Used for 3D outputs which are not images.
        /// Returns matching OutputImage entries (filename/subfolder/type as reported).
        /// </summary>
        public async Task<List<OutputImage>> GetOutputFiles(string promptId, string[] extensions, int maxAttempts = 600, int pollIntervalMs = 2000)
        {
            var found = new List<OutputImage>();
            if (extensions == null || extensions.Length == 0) return found;
            var lower = extensions.Select(e => e.ToLowerInvariant()).ToArray();

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    var response = await _httpClient.GetAsync(string.Format("{0}/history/{1}", _baseUrl, promptId));
                    if (!response.IsSuccessStatusCode) { await Task.Delay(pollIntervalMs); continue; }
                    var result = await response.Content.ReadAsStringAsync();
                    if (string.IsNullOrEmpty(result) || !result.Contains(promptId) || !result.Contains("\"outputs\""))
                    { await Task.Delay(pollIntervalMs); continue; }

                    var imageMarker = "\"filename\"";
                    int pos = 0;
                    int safety = 0;
                    while ((pos = result.IndexOf(imageMarker, pos, StringComparison.Ordinal)) != -1 && safety++ < 500)
                    {
                        pos += imageMarker.Length;
                        while (pos < result.Length && (result[pos] == ':' || result[pos] == ' ' || result[pos] == '"')) pos++;
                        int endPos = result.IndexOf('"', pos);
                        if (endPos <= pos) break;
                        var filename = result.Substring(pos, endPos - pos);
                        string ext = System.IO.Path.GetExtension(filename);
                        if (ext == null) ext = string.Empty;
                        ext = ext.ToLowerInvariant();

                        bool match = false;
                        foreach (var e in lower) { if (ext == e) { match = true; break; } }

                        int nextFilename = result.IndexOf(imageMarker, endPos, StringComparison.Ordinal);
                        string subfolder = string.Empty;
                        string ftype = "output";
                        var subMarker = "\"subfolder\"";
                        int subPos = result.IndexOf(subMarker, endPos, StringComparison.Ordinal);
                        if (subPos != -1 && (nextFilename == -1 || subPos < nextFilename))
                        {
                            subPos += subMarker.Length;
                            while (subPos < result.Length && (result[subPos] == ':' || result[subPos] == ' ')) subPos++;
                            if (subPos < result.Length && result[subPos] == '"')
                            {
                                subPos++;
                                int subEnd = result.IndexOf('"', subPos);
                                if (subEnd > subPos) subfolder = result.Substring(subPos, subEnd - subPos);
                            }
                        }
                        var typeMarker = "\"type\"";
                        int typePos = result.IndexOf(typeMarker, endPos, StringComparison.Ordinal);
                        if (typePos != -1 && (nextFilename == -1 || typePos < nextFilename))
                        {
                            typePos += typeMarker.Length;
                            while (typePos < result.Length && (result[typePos] == ':' || result[typePos] == ' ')) typePos++;
                            if (typePos < result.Length && result[typePos] == '"')
                            {
                                typePos++;
                                int typeEnd = result.IndexOf('"', typePos);
                                if (typeEnd > typePos) ftype = result.Substring(typePos, typeEnd - typePos);
                            }
                        }

                        if (match && !found.Any(i => string.Equals(i.Filename, filename, StringComparison.OrdinalIgnoreCase)))
                            found.Add(new OutputImage { Filename = filename, Subfolder = subfolder, Type = ftype });

                        pos = endPos + 1;
                    }

                    if (found.Count > 0) return found;
                    await Task.Delay(pollIntervalMs);
                }
                catch { await Task.Delay(pollIntervalMs); }
            }
            return found;
        }

        /// <summary>Download any output file (ply, obj, glb...) via /view.</summary>
        public async Task<byte[]> DownloadFile(OutputImage file)
        {
            if (file == null) throw new ArgumentNullException("file");
            return await DownloadImage(file.Filename, file.Subfolder ?? string.Empty, file.Type ?? "output");
        }

        public class QueueState
        {
            public List<string> Running = new List<string>();
            public List<string> Pending = new List<string>();
        }

        /// <summary>
        /// Snapshot of the server queue: which prompt_ids are running vs pending.
        /// A prompt that is neither here nor in /history is gone (server restart).
        /// </summary>
        public async Task<QueueState> GetQueue()
        {
            var state = new QueueState();
            try
            {
                var response = await _httpClient.GetAsync(string.Format("{0}/queue", _baseUrl));
                if (!response.IsSuccessStatusCode) return state;
                var json = await response.Content.ReadAsStringAsync();
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var root = ser.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null) return state;
                CollectQueueIds(root, "queue_running", state.Running);
                CollectQueueIds(root, "queue_pending", state.Pending);
            }
            catch { }
            return state;
        }

        private static void CollectQueueIds(Dictionary<string, object> root, string key, List<string> into)
        {
            object v;
            if (!root.TryGetValue(key, out v)) return;
            var list = v as System.Collections.IList;
            if (list == null) return;
            foreach (object item in list)
            {
                var entry = item as System.Collections.IList;
                // Entries are [number, prompt_id, ...].
                if (entry != null && entry.Count >= 2 && entry[1] is string)
                    into.Add((string)entry[1]);
            }
        }

        public class HistoryStatus
        {
            public bool HasEntry;
            public bool Completed;
            public string StatusStr = string.Empty;
            public string Error = string.Empty;
            /// <summary>e.g. "57 (SaveAudioAdvanced)" from execution_error.</summary>
            public string ErrorNode = string.Empty;
            public List<string> OutputNodeIds = new List<string>();
        }

        /// <summary>
        /// Parse one /history entry properly: completion flag, status string,
        /// first error/exception message, and which nodes produced outputs.
        /// String-matching "completed:false" misses newer/differently spaced
        /// formats and leaves runs spinning in "Finishing" forever.
        /// </summary>
        public static HistoryStatus ParseHistoryEntry(string historyJson, string promptId)
        {
            var st = new HistoryStatus();
            if (string.IsNullOrEmpty(historyJson) || string.IsNullOrEmpty(promptId))
                return st;
            try
            {
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var root = ser.DeserializeObject(historyJson) as Dictionary<string, object>;
                if (root == null) return st;
                object entryObj;
                if (!root.TryGetValue(promptId, out entryObj)) return st;
                var entry = entryObj as Dictionary<string, object>;
                if (entry == null) return st;
                st.HasEntry = true;
                object statusObj;
                if (entry.TryGetValue("status", out statusObj))
                {
                    var status = statusObj as Dictionary<string, object>;
                    if (status != null)
                    {
                        object ss;
                        if (status.TryGetValue("status_str", out ss) && ss is string)
                            st.StatusStr = (string)ss;
                        object comp;
                        if (status.TryGetValue("completed", out comp) && comp is bool)
                            st.Completed = (bool)comp;
                        object msgs;
                        if (status.TryGetValue("messages", out msgs))
                        {
                            var list = msgs as System.Collections.IList;
                            if (list != null)
                            {
                                foreach (object mo in list)
                                {
                                    var pair = mo as System.Collections.IList;
                                    if (pair == null || pair.Count < 2) continue;
                                    string kind = pair[0] as string;
                                    // Newer servers send dict payloads
                                    // (execution_error with node/type/message).
                                    var detail = pair[1] as Dictionary<string, object>;
                                    if (detail != null)
                                    {
                                        if (!string.Equals(kind, "execution_error",
                                            StringComparison.OrdinalIgnoreCase)) continue;
                                        object nid, ntype, emsg, etype;
                                        string nodeId = detail.TryGetValue("node_id", out nid)
                                            ? Convert.ToString(nid) : "?";
                                        string nodeType = detail.TryGetValue("node_type", out ntype)
                                            ? Convert.ToString(ntype) : "?";
                                        string msg = detail.TryGetValue("exception_message", out emsg)
                                            ? Convert.ToString(emsg) : string.Empty;
                                        string exc = detail.TryGetValue("exception_type", out etype)
                                            ? Convert.ToString(etype) : string.Empty;
                                        st.ErrorNode = nodeId + " (" + nodeType + ")";
                                        string flat = (exc + " " + msg).Trim();
                                        // One line: server tracebacks bury the lead.
                                        int nl = flat.IndexOf('\n');
                                        if (nl > 0) flat = flat.Substring(0, nl).Trim();
                                        if (flat.Length > 400) flat = flat.Substring(0, 400);
                                        if (flat.Length > 0)
                                        {
                                            if (st.Error.Length > 0) st.Error += " | ";
                                            st.Error += flat;
                                        }
                                        continue;
                                    }
                                    string text = pair[1] as string;
                                    if (string.IsNullOrEmpty(text)) continue;
                                    if (st.Error.Length < 800)
                                    {
                                        if (st.Error.Length > 0) st.Error += " | ";
                                        st.Error += text.Length > 500 ? text.Substring(0, 500) : text;
                                    }
                                }
                            }
                        }
                    }
                }
                object outputsObj;
                if (entry.TryGetValue("outputs", out outputsObj))
                {
                    var outputs = outputsObj as Dictionary<string, object>;
                    if (outputs != null)
                    {
                        foreach (var kv in outputs) st.OutputNodeIds.Add(kv.Key);
                    }
                }
            }
            catch { }
            return st;
        }

        /// <summary>Raw /history/{promptId} JSON ({} when unknown or not finished).</summary>
        public async Task<string> GetHistoryJson(string promptId)
        {
            try
            {
                var response = await _httpClient.GetAsync(string.Format("{0}/history/{1}", _baseUrl, promptId));
                if (!response.IsSuccessStatusCode) return string.Empty;
                return await response.Content.ReadAsStringAsync() ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        /// <summary>
        /// Scan a history JSON blob for files with the given extensions.
        /// Matches any output key ("images", "3d", "gltf", ...) since 3D saver
        /// nodes report under their own data type, not "images".
        /// </summary>
        public static List<OutputImage> ScanHistoryForFiles(string historyJson, string[] extensions)
        {
            var found = new List<OutputImage>();
            if (string.IsNullOrEmpty(historyJson) || extensions == null || extensions.Length == 0)
                return found;
            var lower = new List<string>();
            foreach (var e in extensions) lower.Add(e.ToLowerInvariant());

            var imageMarker = "\"filename\"";
            int pos = 0;
            int safety = 0;
            while ((pos = historyJson.IndexOf(imageMarker, pos, StringComparison.Ordinal)) != -1 && safety++ < 500)
            {
                pos += imageMarker.Length;
                while (pos < historyJson.Length && (historyJson[pos] == ':' || historyJson[pos] == ' ' || historyJson[pos] == '"')) pos++;
                int endPos = historyJson.IndexOf('"', pos);
                if (endPos <= pos) break;
                var filename = historyJson.Substring(pos, endPos - pos);
                string ext = string.Empty;
                try { ext = System.IO.Path.GetExtension(filename); } catch { }
                if (ext == null) ext = string.Empty;
                ext = ext.ToLowerInvariant();

                bool match = false;
                foreach (var e in lower) { if (ext == e) { match = true; break; } }

                int nextFilename = historyJson.IndexOf(imageMarker, endPos, StringComparison.Ordinal);
                string subfolder = string.Empty;
                string ftype = "output";
                var subMarker = "\"subfolder\"";
                int subPos = historyJson.IndexOf(subMarker, endPos, StringComparison.Ordinal);
                if (subPos != -1 && (nextFilename == -1 || subPos < nextFilename))
                {
                    subPos += subMarker.Length;
                    while (subPos < historyJson.Length && (historyJson[subPos] == ':' || historyJson[subPos] == ' ')) subPos++;
                    if (subPos < historyJson.Length && historyJson[subPos] == '"')
                    {
                        subPos++;
                        int subEnd = historyJson.IndexOf('"', subPos);
                        if (subEnd > subPos) subfolder = historyJson.Substring(subPos, subEnd - subPos);
                    }
                }
                var typeMarker = "\"type\"";
                int typePos = historyJson.IndexOf(typeMarker, endPos, StringComparison.Ordinal);
                if (typePos != -1 && (nextFilename == -1 || typePos < nextFilename))
                {
                    typePos += typeMarker.Length;
                    while (typePos < historyJson.Length && (historyJson[typePos] == ':' || historyJson[typePos] == ' ')) typePos++;
                    if (typePos < historyJson.Length && historyJson[typePos] == '"')
                    {
                        typePos++;
                        int typeEnd = historyJson.IndexOf('"', typePos);
                        if (typeEnd > typePos) ftype = historyJson.Substring(typePos, typeEnd - typePos);
                    }
                }

                if (match && !found.Exists(i => string.Equals(i.Filename, filename, StringComparison.OrdinalIgnoreCase)))
                    found.Add(new OutputImage { Filename = filename, Subfolder = subfolder, Type = ftype });

                pos = endPos + 1;
            }
            return found;
        }

        public async Task<bool> TestConnection()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/system_stats");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get list of available checkpoint models from ComfyUI
        /// </summary>
        public async Task<List<string>> GetAvailableCheckpoints()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/object_info/CheckpointLoaderSimple");

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"GetAvailableCheckpoints failed: {response.StatusCode}");
                    return new List<string>();
                }

                var result = await response.Content.ReadAsStringAsync();
                var checkpoints = new List<string>();

                System.Diagnostics.Debug.WriteLine($"GetAvailableCheckpoints response length: {result.Length}");
                System.Diagnostics.Debug.WriteLine($"GetAvailableCheckpoints response sample: {result.Substring(0, Math.Min(2000, result.Length))}");

                // ComfyUI format: "ckpt_name": [["model1.safetensors", "model2.ckpt", ...], {"default": "..."}]
                // We need to find the array inside "ckpt_name"

                // Try multiple patterns
                string[] markers = new[] {
                    "\"ckpt_name\": [[",
                    "\"ckpt_name\":[[",
                    "\"ckpt_name\" : [[",
                    "\"ckpt_name\":[["
                };

                int pos = -1;
                foreach (var marker in markers)
                {
                    pos = result.IndexOf(marker);
                    if (pos >= 0)
                    {
                        pos += marker.Length;
                        break;
                    }
                }

                if (pos >= 0)
                {
                    // Find the end of the array (first ]])
                    int endPos = result.IndexOf("]", pos);
                    if (endPos > pos)
                    {
                        var arrayContent = result.Substring(pos, endPos - pos);
                        System.Diagnostics.Debug.WriteLine($"Checkpoint array content: {arrayContent}");

                        // Parse the quoted strings - handle both "name" and "name/subdir/name" formats
                        int parsePos = 0;
                        while (parsePos < arrayContent.Length)
                        {
                            // Find opening quote
                            int quoteStart = arrayContent.IndexOf('"', parsePos);
                            if (quoteStart < 0) break;

                            // Find closing quote (handle escaped quotes)
                            int quoteEnd = quoteStart + 1;
                            while (quoteEnd < arrayContent.Length)
                            {
                                if (arrayContent[quoteEnd] == '"' && (quoteEnd == 0 || arrayContent[quoteEnd - 1] != '\\'))
                                    break;
                                quoteEnd++;
                            }

                            if (quoteEnd > quoteStart + 1 && quoteEnd < arrayContent.Length)
                            {
                                var checkpoint = arrayContent.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                                if (!string.IsNullOrWhiteSpace(checkpoint) && !checkpoints.Contains(checkpoint))
                                {
                                    checkpoints.Add(checkpoint);
                                    System.Diagnostics.Debug.WriteLine($"Found checkpoint: {checkpoint}");
                                }
                            }

                            parsePos = quoteEnd + 1;
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Total checkpoints found: {checkpoints.Count}");
                return checkpoints;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetAvailableCheckpoints exception: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// Get list of available samplers from ComfyUI
        /// </summary>
        public async Task<List<string>> GetAvailableSamplers()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/object_info/KSampler");
                if (!response.IsSuccessStatusCode) return new List<string>();

                var result = await response.Content.ReadAsStringAsync();
                var samplers = new List<string>();

                // Try multiple patterns
                string[] markers = new[] { "\"sampler_name\": [[", "\"sampler_name\":[[" };

                int pos = -1;
                foreach (var marker in markers)
                {
                    pos = result.IndexOf(marker);
                    if (pos >= 0) { pos += marker.Length; break; }
                }

                if (pos >= 0)
                {
                    int endPos = result.IndexOf("]", pos);
                    if (endPos > pos)
                    {
                        var arrayContent = result.Substring(pos, endPos - pos);
                        int parsePos = 0;
                        while (parsePos < arrayContent.Length)
                        {
                            int quoteStart = arrayContent.IndexOf('"', parsePos);
                            if (quoteStart < 0) break;
                            int quoteEnd = arrayContent.IndexOf('"', quoteStart + 1);
                            if (quoteEnd > quoteStart + 1)
                            {
                                var sampler = arrayContent.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                                if (!string.IsNullOrWhiteSpace(sampler) && !samplers.Contains(sampler))
                                    samplers.Add(sampler);
                            }
                            parsePos = quoteEnd + 1;
                        }
                    }
                }
                return samplers;
            }
            catch { return new List<string>(); }
        }

        /// <summary>
        /// Get list of available schedulers from ComfyUI
        /// </summary>
        public async Task<List<string>> GetAvailableSchedulers()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/object_info/KSampler");
                if (!response.IsSuccessStatusCode) return new List<string>();

                var result = await response.Content.ReadAsStringAsync();
                var schedulers = new List<string>();

                // Try multiple patterns
                string[] markers = new[] { "\"scheduler\": [[", "\"scheduler\":[[" };

                int pos = -1;
                foreach (var marker in markers)
                {
                    pos = result.IndexOf(marker);
                    if (pos >= 0) { pos += marker.Length; break; }
                }

                if (pos >= 0)
                {
                    int endPos = result.IndexOf("]", pos);
                    if (endPos > pos)
                    {
                        var arrayContent = result.Substring(pos, endPos - pos);
                        int parsePos = 0;
                        while (parsePos < arrayContent.Length)
                        {
                            int quoteStart = arrayContent.IndexOf('"', parsePos);
                            if (quoteStart < 0) break;
                            int quoteEnd = arrayContent.IndexOf('"', quoteStart + 1);
                            if (quoteEnd > quoteStart + 1)
                            {
                                var scheduler = arrayContent.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                                if (!string.IsNullOrWhiteSpace(scheduler) && !schedulers.Contains(scheduler))
                                    schedulers.Add(scheduler);
                            }
                            parsePos = quoteEnd + 1;
                        }
                    }
                }
                return schedulers;
            }
            catch { return new List<string>(); }
        }

        public async Task<string> UploadImage(byte[] imageData, string filename)
        {
            try
            {
                using (var content = new MultipartFormDataContent())
                {
                    var imageContent = new ByteArrayContent(imageData);
                    imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                    content.Add(imageContent, "image", filename);

                    content.Add(new StringContent("true"), "overwrite");

                    System.Diagnostics.Debug.WriteLine($"Uploading image to {_baseUrl}/upload/image ({imageData.Length} bytes)");

                    var response = await _httpClient.PostAsync($"{_baseUrl}/upload/image", content);

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        System.Diagnostics.Debug.WriteLine($"Upload failed: {response.StatusCode} - {error}");
                        throw new Exception($"Failed to upload image: {response.StatusCode} - {error}");
                    }

                    var result = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Upload response: {result}");

                    var nameMarker = "\"name\":\"";
                    int pos = result.IndexOf(nameMarker);
                    if (pos >= 0)
                    {
                        pos += nameMarker.Length;
                        int endPos = result.IndexOf("\"", pos);
                        if (endPos > pos)
                        {
                            return result.Substring(pos, endPos - pos);
                        }
                    }

                    return filename;
                }
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"UploadImage HTTP exception: {ex}");
                throw new Exception($"Could not connect to ComfyUI at {_baseUrl}. Please check:\n" +
                    "1. ComfyUI is running\n" +
                    "2. The URL is correct\n" +
                    "3. There's no firewall blocking the connection\n\n" +
                    $"Technical details: {ex.Message}");
            }
            catch (TaskCanceledException ex)
            {
                System.Diagnostics.Debug.WriteLine($"UploadImage timeout: {ex}");
                throw new Exception($"Connection to ComfyUI timed out. The server at {_baseUrl} may be overloaded or unresponsive.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UploadImage exception: {ex}");
                throw new Exception($"Error uploading image to ComfyUI: {ex.Message}");
            }
        }

        /// <summary>
        /// Upload a clip (wav/mp3/...) for voice-clone reference or audio
        /// nodes. Returns the server-side filename for LoadAudio widgets.
        /// Tries /upload/audio first, then /upload/image (which accepts any
        /// file type into the input folder) for servers that reject audio
        /// uploads with 404/405.
        /// </summary>
        public async Task<string> UploadAudio(byte[] audioData, string filename)
        {
            string mime = "audio/wav";
            string ext = "";
            try { ext = System.IO.Path.GetExtension(filename).ToLowerInvariant(); }
            catch { }
            if (ext == ".mp3") mime = "audio/mpeg";
            else if (ext == ".ogg" || ext == ".opus") mime = "audio/ogg";
            else if (ext == ".flac") mime = "audio/flac";
            else if (ext == ".m4a") mime = "audio/mp4";

            string lastError = string.Empty;
            string[] routes = new string[] { "upload/audio", "upload/image" };
            for (int r = 0; r < routes.Length; r++)
            {
                try
                {
                    using (var content = new MultipartFormDataContent())
                    {
                        var audioContent = new ByteArrayContent(audioData);
                        audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
                        // Both upload routes read the "image" field name.
                        content.Add(audioContent, "image", filename);
                        content.Add(new StringContent("true"), "overwrite");

                        System.Diagnostics.Debug.WriteLine($"Uploading audio to {_baseUrl}/{routes[r]} ({audioData.Length} bytes)");

                        var response = await _httpClient.PostAsync($"{_baseUrl}/{routes[r]}", content);

                        if (!response.IsSuccessStatusCode)
                        {
                            var error = await response.Content.ReadAsStringAsync();
                            System.Diagnostics.Debug.WriteLine($"Audio upload failed ({routes[r]}): {response.StatusCode} - {error}");
                            lastError = $"{routes[r]}: {response.StatusCode} - {error}";
                            continue;
                        }

                        var result = await response.Content.ReadAsStringAsync();
                        var nameMarker = "\"name\":\"";
                        int pos = result.IndexOf(nameMarker);
                        if (pos >= 0)
                        {
                            pos += nameMarker.Length;
                            int endPos = result.IndexOf("\"", pos);
                            if (endPos > pos)
                            {
                                return result.Substring(pos, endPos - pos);
                            }
                        }

                        return filename;
                    }
                }
                catch (HttpRequestException ex)
                {
                    throw new Exception($"Could not connect to ComfyUI at {_baseUrl}. Please check:\n" +
                        "1. ComfyUI is running\n" +
                        "2. The URL is correct\n" +
                        "3. There's no firewall blocking the connection\n\n" +
                        $"Technical details: {ex.Message}");
                }
                catch (TaskCanceledException ex)
                {
                    throw new Exception($"Connection to ComfyUI timed out. The server at {_baseUrl} may be overloaded or unresponsive.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"UploadAudio exception ({routes[r]}): {ex}");
                    lastError = $"{routes[r]}: {ex.Message}";
                }
            }
            throw new Exception($"Failed to upload audio ({lastError}).");
        }
    }

    public class Text2ImageWorkflow
    {
        public const string DEFAULT_CHECKPOINT = "dreamshaper_8.safetensors";

        public static string CreateWorkflow(string positivePrompt, string negativePrompt = "", int width = 512, int height = 512, int steps = 20, double cfg = 6.0, long? seed = null, string samplerName = "euler", string scheduler = "simple", string checkpoint = null)
        {
            if (!seed.HasValue || seed.Value < 0)
                seed = new Random().Next();

            checkpoint = checkpoint ?? DEFAULT_CHECKPOINT;
            positivePrompt = positivePrompt.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
            negativePrompt = negativePrompt.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

            return $@"{{
  ""3"": {{
    ""inputs"": {{
      ""seed"": {seed},
      ""steps"": {steps},
      ""cfg"": {cfg.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)},
      ""sampler_name"": ""{samplerName}"",
      ""scheduler"": ""{scheduler}"",
      ""denoise"": 1.0,
      ""model"": [""4"", 0],
      ""positive"": [""16"", 0],
      ""negative"": [""40"", 0],
      ""latent_image"": [""53"", 0]
    }},
    ""class_type"": ""KSampler""
  }},
  ""4"": {{
    ""inputs"": {{
      ""ckpt_name"": ""{checkpoint}""
    }},
    ""class_type"": ""CheckpointLoaderSimple""
  }},
  ""8"": {{
    ""inputs"": {{
      ""samples"": [""3"", 0],
      ""vae"": [""4"", 2]
    }},
    ""class_type"": ""VAEDecode""
  }},
  ""9"": {{
    ""inputs"": {{
      ""filename_prefix"": ""ComfyUI"",
      ""images"": [""8"", 0]
    }},
    ""class_type"": ""SaveImage""
  }},
  ""16"": {{
    ""inputs"": {{
      ""text"": ""{positivePrompt}"",
      ""clip"": [""4"", 1]
    }},
    ""class_type"": ""CLIPTextEncode""
  }},
  ""40"": {{
    ""inputs"": {{
      ""text"": ""{negativePrompt}"",
      ""clip"": [""4"", 1]
    }},
    ""class_type"": ""CLIPTextEncode""
  }},
  ""53"": {{
    ""inputs"": {{
      ""width"": {width},
      ""height"": {height},
      ""batch_size"": 1
    }},
    ""class_type"": ""EmptySD3LatentImage""
  }}
}}";
        }
    }

    public class Image2ImageWorkflow
    {
        public const string DEFAULT_CHECKPOINT = "dreamshaper_8.safetensors";

        public static string CreateWorkflow(string positivePrompt, string negativePrompt = "", string inputImageFilename = "", int resizeWidth = 512, int resizeHeight = 512, int steps = 20, double cfg = 6.0, double denoise = 0.5, long? seed = null, string samplerName = "euler", string scheduler = "simple", string checkpoint = null)
        {
            if (!seed.HasValue || seed.Value < 0)
                seed = new Random().Next();

            checkpoint = checkpoint ?? DEFAULT_CHECKPOINT;
            positivePrompt = positivePrompt.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
            negativePrompt = negativePrompt.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
            inputImageFilename = inputImageFilename.Replace("\\", "\\\\").Replace("\"", "\\\"");

            return $@"{{
  ""1"": {{
    ""inputs"": {{
      ""ckpt_name"": ""{checkpoint}""
    }},
    ""class_type"": ""CheckpointLoaderSimple""
  }},
  ""2"": {{
    ""inputs"": {{
      ""image"": ""{inputImageFilename}"",
      ""upload"": ""image""
    }},
    ""class_type"": ""LoadImage""
  }},
  ""3"": {{
    ""inputs"": {{
      ""pixels"": [""2"", 0],
      ""vae"": [""1"", 2]
    }},
    ""class_type"": ""VAEEncode""
  }},
  ""4"": {{
    ""inputs"": {{
      ""text"": ""{positivePrompt}"",
      ""clip"": [""1"", 1]
    }},
    ""class_type"": ""CLIPTextEncode""
  }},
  ""5"": {{
    ""inputs"": {{
      ""text"": ""{negativePrompt}"",
      ""clip"": [""1"", 1]
    }},
    ""class_type"": ""CLIPTextEncode""
  }},
  ""6"": {{
    ""inputs"": {{
      ""seed"": {seed},
      ""steps"": {steps},
      ""cfg"": {cfg.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)},
      ""sampler_name"": ""{samplerName}"",
      ""scheduler"": ""{scheduler}"",
      ""denoise"": {denoise.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)},
      ""model"": [""1"", 0],
      ""positive"": [""4"", 0],
      ""negative"": [""5"", 0],
      ""latent_image"": [""3"", 0]
    }},
    ""class_type"": ""KSampler""
  }},
  ""7"": {{
    ""inputs"": {{
      ""samples"": [""6"", 0],
      ""vae"": [""1"", 2]
    }},
    ""class_type"": ""VAEDecode""
  }},
  ""8"": {{
    ""inputs"": {{
      ""filename_prefix"": ""ComfyUI"",
      ""images"": [""7"", 0]
    }},
    ""class_type"": ""SaveImage""
  }}
}}";
        }
    }

    public class DreamshaperWorkflow
    {
        public static string CreateWorkflow(string positivePrompt, string negativePrompt = "", int width = 512, int height = 512, int steps = 20, double cfg = 6.0, long? seed = null, string samplerName = "euler", string scheduler = "simple", string checkpoint = null)
        {
            return Text2ImageWorkflow.CreateWorkflow(positivePrompt, negativePrompt, width, height, steps, cfg, seed, samplerName, scheduler, checkpoint);
        }
    }

    public class InpaintWorkflow
    {
        public const string DEFAULT_CHECKPOINT = "v1-5-pruned-emaonly.ckpt";

        public static string CreateWorkflow(string positivePrompt, string negativePrompt = "", string inputImageFilename = "", string maskImageFilename = "", int width = 512, int height = 512, int steps = 20, double cfg = 7.0, double denoise = 0.73, long? seed = null, string samplerName = "ddpm", string scheduler = "normal", string checkpoint = null)
        {
            if (!seed.HasValue || seed.Value < 0)
                seed = new Random().Next();

            checkpoint = checkpoint ?? DEFAULT_CHECKPOINT;
            positivePrompt = positivePrompt.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
            negativePrompt = negativePrompt.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
            inputImageFilename = inputImageFilename.Replace("\\", "\\\\").Replace("\"", "\\\"");
            maskImageFilename = maskImageFilename.Replace("\\", "\\\\").Replace("\"", "\\\"");

            return $@"{{
  ""3"": {{
    ""inputs"": {{
      ""seed"": {seed},
      ""steps"": {steps},
      ""cfg"": {cfg.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)},
      ""sampler_name"": ""{samplerName}"",
      ""scheduler"": ""{scheduler}"",
      ""denoise"": {denoise.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)},
      ""model"": [""4"", 0],
      ""positive"": [""16"", 0],
      ""negative"": [""40"", 0],
      ""latent_image"": [""76"", 0]
    }},
    ""class_type"": ""KSampler""
  }},
  ""4"": {{
    ""inputs"": {{
      ""ckpt_name"": ""{checkpoint}""
    }},
    ""class_type"": ""CheckpointLoaderSimple""
  }},
  ""8"": {{
    ""inputs"": {{
      ""samples"": [""3"", 0],
      ""vae"": [""4"", 2]
    }},
    ""class_type"": ""VAEDecode""
  }},
  ""9"": {{
    ""inputs"": {{
      ""filename_prefix"": ""ComfyUI"",
      ""images"": [""8"", 0]
    }},
    ""class_type"": ""SaveImage""
  }},
  ""16"": {{
    ""inputs"": {{
      ""text"": ""{positivePrompt}"",
      ""clip"": [""4"", 1]
    }},
    ""class_type"": ""CLIPTextEncode""
  }},
  ""40"": {{
    ""inputs"": {{
      ""text"": ""{negativePrompt}"",
      ""clip"": [""4"", 1]
    }},
    ""class_type"": ""CLIPTextEncode""
  }},
  ""55"": {{
    ""inputs"": {{
      ""image"": ""{inputImageFilename}""
    }},
    ""class_type"": ""LoadImage""
  }},
  ""70"": {{
    ""inputs"": {{
      ""image"": ""{maskImageFilename}""
    }},
    ""class_type"": ""LoadImage""
  }},
  ""74"": {{
    ""inputs"": {{
      ""channel"": ""red"",
      ""image"": [""70"", 0]
    }},
    ""class_type"": ""ImageToMask""
  }},
  ""75"": {{
    ""inputs"": {{
      ""pixels"": [""55"", 0],
      ""vae"": [""4"", 2]
    }},
    ""class_type"": ""VAEEncode""
  }},
    ""76"": {{
      ""inputs"": {{
        ""samples"": [""75"", 0],
        ""mask"": [""74"", 0]
      }},
      ""class_type"": ""SetLatentNoiseMask""
    }}
  }}";
          }
      }

      /// <summary>
      /// ControlNet Depth workflow using MeesaJarJar background removal, image blur,
      /// depth-based ControlNet conditioning, and latent noise masking.
      /// </summary>
      public class ControlNetDepthWorkflow
      {
          public const string DEFAULT_CHECKPOINT = "dreamshaper_8.safetensors";
          public const string DEFAULT_CONTROLNET_MODEL = @"1.5\control_v11f1p_sd15_depth_fp16.safetensors";

          public static string CreateWorkflow(
              string positivePrompt,
              string negativePrompt = "",
              string inputImageFilename = "",
              int steps = 17,
              double cfg = 4.0,
              double denoise = 1.0,
              long? seed = null,
              string samplerName = "euler",
              string scheduler = "ddim_uniform",
              string checkpoint = null,
              string controlNetModel = null,
              double controlNetStrength = 0.5,
              double controlNetStartPercent = 0.0,
              double controlNetEndPercent = 1.0,
              int blurRadius = 7,
              double blurSigma = 5.0,
              double bgThreshold = 0.03,
              double bgFeather = 0.0,
              string channelMode = "rgb_max",
              double despill = 0.0,
              string invertMask = "no")
          {
              if (!seed.HasValue)
                  seed = new Random().Next();

              checkpoint = checkpoint ?? DEFAULT_CHECKPOINT;
              controlNetModel = controlNetModel ?? DEFAULT_CONTROLNET_MODEL;
              positivePrompt = positivePrompt.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
              negativePrompt = negativePrompt.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
              inputImageFilename = inputImageFilename.Replace("\\", "\\\\").Replace("\"", "\\\"");
              controlNetModel = controlNetModel.Replace("\\", "\\\\").Replace("\"", "\\\"");

              var inv = System.Globalization.CultureInfo.InvariantCulture;

              return $@"{{
    ""1"": {{
      ""inputs"": {{
        ""ckpt_name"": ""{checkpoint}""
      }},
      ""class_type"": ""CheckpointLoaderSimple""
    }},
    ""2"": {{
      ""inputs"": {{
        ""image"": ""{inputImageFilename}""
      }},
      ""class_type"": ""LoadImage""
    }},
    ""3"": {{
      ""inputs"": {{
        ""pixels"": [""13"", 0],
        ""vae"": [""1"", 2]
      }},
      ""class_type"": ""VAEEncode""
    }},
    ""4"": {{
      ""inputs"": {{
        ""text"": ""{positivePrompt}"",
        ""clip"": [""1"", 1]
      }},
      ""class_type"": ""CLIPTextEncode""
    }},
    ""5"": {{
      ""inputs"": {{
        ""text"": ""{negativePrompt}"",
        ""clip"": [""1"", 1]
      }},
      ""class_type"": ""CLIPTextEncode""
    }},
    ""6"": {{
      ""inputs"": {{
        ""seed"": {seed},
        ""steps"": {steps},
        ""cfg"": {cfg.ToString("0.0", inv)},
        ""sampler_name"": ""{samplerName}"",
        ""scheduler"": ""{scheduler}"",
        ""denoise"": {denoise.ToString("0.00", inv)},
        ""model"": [""1"", 0],
        ""positive"": [""9"", 0],
        ""negative"": [""9"", 1],
        ""latent_image"": [""15"", 0]
      }},
      ""class_type"": ""KSampler""
    }},
    ""7"": {{
      ""inputs"": {{
        ""samples"": [""6"", 0],
        ""vae"": [""1"", 2]
      }},
      ""class_type"": ""VAEDecode""
    }},
    ""8"": {{
      ""inputs"": {{
        ""filename_prefix"": ""ComfyUI"",
        ""images"": [""7"", 0]
      }},
      ""class_type"": ""SaveImage""
    }},
    ""9"": {{
      ""inputs"": {{
        ""strength"": {controlNetStrength.ToString("0.00", inv)},
        ""start_percent"": {controlNetStartPercent.ToString("0.000", inv)},
        ""end_percent"": {controlNetEndPercent.ToString("0.000", inv)},
        ""positive"": [""4"", 0],
        ""negative"": [""5"", 0],
        ""control_net"": [""12"", 0],
        ""image"": [""16"", 0],
        ""vae"": [""1"", 2]
      }},
      ""class_type"": ""ControlNetApplyAdvanced""
    }},
    ""12"": {{
      ""inputs"": {{
        ""control_net_name"": ""{controlNetModel}""
      }},
      ""class_type"": ""ControlNetLoader""
    }},
    ""13"": {{
      ""inputs"": {{
        ""threshold"": {bgThreshold.ToString("0.00", inv)},
        ""feather"": {bgFeather.ToString("0.00", inv)},
        ""channel_mode"": ""{channelMode}"",
        ""despill"": {despill.ToString("0.00", inv)},
        ""invert_mask"": ""{invertMask}"",
        ""image"": [""21"", 0]
      }},
      ""class_type"": ""MeesaJarJar""
    }},
    ""15"": {{
      ""inputs"": {{
        ""samples"": [""3"", 0],
        ""mask"": [""18"", 0]
      }},
      ""class_type"": ""SetLatentNoiseMask""
    }},
    ""16"": {{
      ""inputs"": {{
        ""image"": [""13"", 0]
      }},
      ""class_type"": ""SplitImageWithAlpha""
    }},
    ""18"": {{
      ""inputs"": {{
        ""mask"": [""16"", 1]
      }},
      ""class_type"": ""InvertMask""
    }},
    ""21"": {{
      ""inputs"": {{
        ""blur_radius"": {blurRadius},
        ""sigma"": {blurSigma.ToString("0.0", inv)},
        ""image"": [""2"", 0]
      }},
      ""class_type"": ""ImageBlur""
    }}
  }}";
          }
      }
  }
