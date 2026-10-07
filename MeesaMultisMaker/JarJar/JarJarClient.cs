using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace MeesaMultisMaker.JarJar
{
    /// <summary>
    /// Result of a sprite upload to the MeesaJarJar server.
    /// </summary>
    public class UploadResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string GraphicId { get; set; }
        public string Atlas { get; set; }
        public string NewHash { get; set; }
    }

    public class UnderlayUploadRequest
    {
        public int Facet { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public Bitmap Image { get; set; }
        public string SourcePath { get; set; }
    }

    public class OverlayDeleteRequest
    {
        public int Facet { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public class OverlayUploadRequest
    {
        public int Facet { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public Bitmap Image { get; set; }
        public string SourcePath { get; set; }
    }

    /// <summary>
    /// A single entry from the server-side underlay index.
    /// </summary>
    public class UnderlayIndexEntry
    {
        public int Facet { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string ImageUrl { get; set; }
        public string Hash { get; set; }
        public string UpdatedAt { get; set; }
    }

    public class OverlayIndexEntry
    {
        public int Facet { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string ImageUrl { get; set; }
        public string Hash { get; set; }
        public string UpdatedAt { get; set; }
    }

    /// <summary>
    /// HTTP client for the MeesaJarJar.com artwork API.
    /// Supports querying, downloading, and replacing sprites in the atlas system.
    /// </summary>
    public class JarJarClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _authToken;

        private LookupTable _cachedLookup;
        private DateTime _lookupCacheTime = DateTime.MinValue;
        private static readonly TimeSpan LookupCacheExpiry = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Creates a new JarJarClient with the specified base URL and optional auth token.
        /// </summary>
        public JarJarClient(string baseUrl, string authToken = null)
        {
            _baseUrl = (baseUrl ?? "https://meesajarjar.com").TrimEnd('/');
            _authToken = authToken;

            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(2);
        }

        /// <summary>
        /// Downloads the lookup.bin file and parses it into a LookupTable.
        /// Results are cached for 10 minutes.
        /// </summary>
        public async Task<LookupTable> GetLookupTableAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && _cachedLookup != null &&
                (DateTime.UtcNow - _lookupCacheTime) < LookupCacheExpiry)
            {
                return _cachedLookup;
            }

            var url = $"{_baseUrl}/gameArt.php?action=download&file=lookup.bin";
            System.Diagnostics.Debug.WriteLine($"[JarJar] Downloading lookup table from {url}");

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadAsByteArrayAsync();
            _cachedLookup = new LookupTable(data);
            _lookupCacheTime = DateTime.UtcNow;

            System.Diagnostics.Debug.WriteLine($"[JarJar] Lookup table loaded: {_cachedLookup.EntryCount} entries");
            return _cachedLookup;
        }

        /// <summary>
        /// Gets the sprite info (atlas position, dimensions) for a graphic ID.
        /// Returns null if the graphic ID is not in the lookup table.
        /// </summary>
        public async Task<LookupEntry?> GetSpriteInfoAsync(ushort graphicId)
        {
            var lookup = await GetLookupTableAsync();
            return lookup.GetEntry(graphicId);
        }

        /// <summary>
        /// Downloads the current sprite image for a graphic ID from the server.
        /// </summary>
        public async Task<Bitmap> DownloadSpriteAsync(ushort graphicId)
        {
            var entry = await GetSpriteInfoAsync(graphicId);
            if (!entry.HasValue)
                return null;

            var e = entry.Value;
            var url = $"{_baseUrl}/get-sprite.php?atlas={e.AtlasIndex}&x={e.X}&y={e.Y}&w={e.Width}&h={e.Height}";

            System.Diagnostics.Debug.WriteLine($"[JarJar] Downloading sprite 0x{graphicId:X4} from {url}");

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            using (var stream = await response.Content.ReadAsStreamAsync())
            {
                return new Bitmap(stream);
            }
        }

        /// <summary>
        /// Uploads a replacement sprite for the given graphic ID.
        /// The image dimensions MUST exactly match the lookup table entry.
        /// </summary>
        public async Task<UploadResult> UploadSpriteAsync(ushort graphicId, Bitmap image)
        {
            if (image == null)
                throw new ArgumentNullException("image");

            var entry = await GetSpriteInfoAsync(graphicId);
            if (!entry.HasValue)
            {
                return new UploadResult
                {
                    Success = false,
                    Message = $"Graphic 0x{graphicId:X4} not found in lookup table"
                };
            }

            var e = entry.Value;

            // Resize if dimensions don't match
            Bitmap toUpload = image;
            bool needsDispose = false;

            if (image.Width != e.Width || image.Height != e.Height)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[JarJar] Resizing 0x{graphicId:X4} from {image.Width}x{image.Height} to {e.Width}x{e.Height}");

                toUpload = new Bitmap(e.Width, e.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(toUpload))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.DrawImage(image, 0, 0, e.Width, e.Height);
                }
                needsDispose = true;
            }

            try
            {
                // Convert bitmap to PNG bytes
                byte[] pngBytes;
                using (var ms = new MemoryStream())
                {
                    toUpload.Save(ms, ImageFormat.Png);
                    pngBytes = ms.ToArray();
                }

                // Build multipart form data
                string hexId = graphicId.ToString("X4");
                using (var content = new MultipartFormDataContent())
                {
                    content.Add(new StringContent(hexId), "hexid");

                    if (!string.IsNullOrEmpty(_authToken))
                        content.Add(new StringContent(_authToken), "auth_token");

                    var imageContent = new ByteArrayContent(pngBytes);
                    imageContent.Headers.ContentType =
                        new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                    content.Add(imageContent, "image", "sprite.png");

                    var url = $"{_baseUrl}/updateGraphic.php";
                    System.Diagnostics.Debug.WriteLine(
                        $"[JarJar] Uploading sprite 0x{hexId} ({e.Width}x{e.Height}) to {url}");

                    var response = await _httpClient.PostAsync(url, content);
                    var body = await response.Content.ReadAsStringAsync();

                    // Handle both "status":"ok" and "status": "ok" (with/without space)
                    bool isOk = response.IsSuccessStatusCode &&
                                (body.Contains("\"status\":\"ok\"") || body.Contains("\"status\": \"ok\""));

                    if (isOk)
                    {
                        return new UploadResult
                        {
                            Success = true,
                            Message = $"Graphic 0x{hexId} updated successfully",
                            GraphicId = "0x" + hexId,
                            Atlas = ParseJsonField(body, "atlas"),
                            NewHash = ParseJsonField(body, "new_hash")
                        };
                    }
                    else
                    {
                        string errorMsg = ParseJsonField(body, "message")
                                       ?? ParseJsonField(body, "error")
                                       ?? $"HTTP {(int)response.StatusCode}: {body}";

                        return new UploadResult
                        {
                            Success = false,
                            Message = errorMsg,
                            GraphicId = "0x" + hexId
                        };
                    }
                }
            }
            finally
            {
                if (needsDispose && toUpload != null)
                    toUpload.Dispose();
            }
        }

        /// <summary>
        /// Uploads multiple sprites. Returns per-item results.
        /// The onProgress callback reports (completed, total) for UI updates.
        /// </summary>
        public async Task<List<UploadResult>> UploadMultipleSpritesAsync(
            Dictionary<ushort, Bitmap> items,
            Action<int, int> onProgress = null)
        {
            var results = new List<UploadResult>();

            // Snapshot the dictionary to avoid "Collection was modified" if the
            // caller's collection changes while we're iterating (e.g. UI events
            // processed via Application.DoEvents during async gaps).
            var snapshot = new List<KeyValuePair<ushort, Bitmap>>(items);
            int completed = 0;
            int total = snapshot.Count;

            // Pre-fetch the lookup table once
            await GetLookupTableAsync();

            foreach (var kvp in snapshot)
            {
                try
                {
                    var result = await UploadSpriteAsync(kvp.Key, kvp.Value);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    results.Add(new UploadResult
                    {
                        Success = false,
                        Message = ex.Message,
                        GraphicId = "0x" + kvp.Key.ToString("X4")
                    });
                }

                completed++;
                onProgress?.Invoke(completed, total);
            }

            return results;
        }

        /// <summary>
        /// Deletes one overlay area record + image payload by facet/bounds.
        /// Server endpoint: POST /gameOverlays.php?action=delete
        /// </summary>
        public async Task<UploadResult> DeleteOverlayAsync(OverlayDeleteRequest request)
        {
            if (request == null)
                throw new ArgumentNullException("request");

            using (var content = new MultipartFormDataContent())
            {
                content.Add(new StringContent(request.Facet.ToString()), "facet");
                content.Add(new StringContent(request.Left.ToString()), "left");
                content.Add(new StringContent(request.Top.ToString()), "top");
                content.Add(new StringContent(request.Width.ToString()), "width");
                content.Add(new StringContent(request.Height.ToString()), "height");

                if (!string.IsNullOrEmpty(_authToken))
                    content.Add(new StringContent(_authToken), "auth_token");

                var url = $"{_baseUrl}/gameOverlays.php?action=delete";
                var response = await _httpClient.PostAsync(url, content);
                var body = await response.Content.ReadAsStringAsync();

                bool isOk = response.IsSuccessStatusCode &&
                            (body.Contains("\"status\":\"ok\"") || body.Contains("\"status\": \"ok\""));

                string areaId = $"facet:{request.Facet} [{request.Left},{request.Top},{request.Width},{request.Height}]";
                if (isOk)
                {
                    return new UploadResult
                    {
                        Success = true,
                        Message = ParseJsonField(body, "message") ?? "Overlay deleted successfully",
                        GraphicId = areaId
                    };
                }

                string errorMsg = ParseJsonField(body, "message")
                               ?? ParseJsonField(body, "error")
                               ?? $"HTTP {(int)response.StatusCode}: {body}";

                return new UploadResult
                {
                    Success = false,
                    Message = errorMsg,
                    GraphicId = areaId
                };
            }
        }

        /// <summary>
        /// Deletes multiple overlay areas in sequence.
        /// </summary>
        public async Task<List<UploadResult>> DeleteMultipleOverlaysAsync(
            List<OverlayDeleteRequest> items,
            Action<int, int> onProgress = null)
        {
            var results = new List<UploadResult>();
            if (items == null || items.Count == 0)
                return results;

            int completed = 0;
            int total = items.Count;

            foreach (var item in items)
            {
                try
                {
                    var result = await DeleteOverlayAsync(item);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    results.Add(new UploadResult
                    {
                        Success = false,
                        Message = ex.Message,
                        GraphicId = $"facet:{item?.Facet ?? -1}"
                    });
                }

                completed++;
                onProgress?.Invoke(completed, total);
            }

            return results;
        }

        /// <summary>
        /// Gets file hashes from the server for change detection.
        /// </summary>
        public async Task<Dictionary<string, string>> GetHashesAsync()
        {
            var url = $"{_baseUrl}/gameArt.php?action=hashes";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            return ParseFileHashes(body);
        }

        /// <summary>
        /// Tests connectivity to the MeesaJarJar server.
        /// Returns true if the server responds successfully.
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var url = $"{_baseUrl}/gameArt.php?action=list";
                using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    var response = await _httpClient.GetAsync(url, cts.Token);
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        // ================================================================
        //  Texmap support (gameTexmaps.php)
        // ================================================================

        /// <summary>
        /// Downloads the texmap lookup table from the server.
        /// </summary>
        public async Task<byte[]> DownloadTexmapLookupAsync()
        {
            var url = $"{_baseUrl}/gameTexmaps.php?action=download&file=lookup.bin";
            System.Diagnostics.Debug.WriteLine($"[JarJar] Downloading texmap lookup from {url}");

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }

        /// <summary>
        /// Downloads a texmap sprite from the server by atlas coordinates.
        /// </summary>
        public async Task<Bitmap> DownloadTexmapSpriteAsync(int atlasIndex, int x, int y, int w, int h)
        {
            var url = $"{_baseUrl}/get-sprite.php?atlas={atlasIndex}&x={x}&y={y}&w={w}&h={h}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            using (var stream = await response.Content.ReadAsStreamAsync())
            {
                return new Bitmap(stream);
            }
        }

        /// <summary>
        /// Gets file hashes for texmaps (for change detection).
        /// </summary>
        public async Task<Dictionary<string, string>> GetTexmapHashesAsync()
        {
            var url = $"{_baseUrl}/gameTexmaps.php?action=hashes";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            return ParseFileHashes(body);
        }

        // ================================================================
        //  Gump support (gameGumps.php)
        // ================================================================

        /// <summary>
        /// Downloads the gump lookup table from the server.
        /// </summary>
        public async Task<byte[]> DownloadGumpLookupAsync()
        {
            var url = $"{_baseUrl}/gameGumps.php?action=download&file=lookup.bin";
            System.Diagnostics.Debug.WriteLine($"[JarJar] Downloading gump lookup from {url}");

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }

        /// <summary>
        /// Gets file hashes for gumps (for change detection).
        /// </summary>
        public async Task<Dictionary<string, string>> GetGumpHashesAsync()
        {
            var url = $"{_baseUrl}/gameGumps.php?action=hashes";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            return ParseFileHashes(body);
        }

        /// <summary>
        /// Uploads a replacement gump image via the updateGump.php endpoint.
        /// The server reads the gumps.jargump binary lookup to locate the
        /// sprite inside its atlas and composites the new image in place.
        /// </summary>
        public async Task<UploadResult> UploadGumpAsync(ushort gumpId, Bitmap image, bool allowResize = true)
        {
            if (image == null)
                throw new ArgumentNullException("image");

            byte[] pngBytes;
            using (var ms = new MemoryStream())
            {
                image.Save(ms, ImageFormat.Png);
                pngBytes = ms.ToArray();
            }

            string idStr = "0x" + gumpId.ToString("X4");

            using (var content = new MultipartFormDataContent())
            {
                content.Add(new StringContent(idStr), "gumpid");

                if (allowResize)
                    content.Add(new StringContent("1"), "allow_resize");

                if (!string.IsNullOrEmpty(_authToken))
                    content.Add(new StringContent(_authToken), "auth_token");

                var imageContent = new ByteArrayContent(pngBytes);
                imageContent.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                content.Add(imageContent, "image", "gump.png");

                var url = $"{_baseUrl}/updateGump.php";
                System.Diagnostics.Debug.WriteLine(
                    $"[JarJar] Uploading gump {idStr} ({image.Width}x{image.Height}) to {url}");

                var response = await _httpClient.PostAsync(url, content);
                var body = await response.Content.ReadAsStringAsync();

                bool isOk = response.IsSuccessStatusCode &&
                            (body.Contains("\"status\":\"ok\"") || body.Contains("\"status\": \"ok\""));

                if (isOk)
                {
                    return new UploadResult
                    {
                        Success = true,
                        Message = $"Gump {idStr} updated successfully",
                        GraphicId = idStr,
                        Atlas = ParseJsonField(body, "atlas"),
                        NewHash = ParseJsonField(body, "new_hash")
                    };
                }
                else
                {
                    string errorMsg = ParseJsonField(body, "message")
                                   ?? ParseJsonField(body, "error")
                                   ?? $"HTTP {(int)response.StatusCode}: {body}";

                    return new UploadResult
                    {
                        Success = false,
                        Message = errorMsg,
                        GraphicId = idStr
                    };
                }
            }
        }

        /// <summary>
        /// Uploads multiple gump images. Returns per-item results.
        /// The onProgress callback reports (completed, total) for UI updates.
        /// </summary>
        public async Task<List<UploadResult>> UploadMultipleGumpsAsync(
            Dictionary<ushort, Bitmap> items,
            bool allowResize = true,
            Action<int, int> onProgress = null)
        {
            var results = new List<UploadResult>();
            var snapshot = new List<KeyValuePair<ushort, Bitmap>>(items);
            int completed = 0;
            int total = snapshot.Count;

            foreach (var kvp in snapshot)
            {
                try
                {
                    var result = await UploadGumpAsync(kvp.Key, kvp.Value, allowResize);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    results.Add(new UploadResult
                    {
                        Success = false,
                        Message = ex.Message,
                        GraphicId = "0x" + kvp.Key.ToString("X4")
                    });
                }

                completed++;
                onProgress?.Invoke(completed, total);
            }

            return results;
        }

        // ================================================================
        //  Underlay support (JarJar-native format)
        // ================================================================

        /// <summary>
        /// Uploads one underlay area + image payload.
        /// Server endpoint is expected to index by facet/bounds and store PNG separately.
        /// </summary>
        public async Task<UploadResult> UploadUnderlayAsync(UnderlayUploadRequest underlay)
        {
            if (underlay == null)
                throw new ArgumentNullException("underlay");
            if (underlay.Image == null)
                throw new ArgumentNullException("underlay.Image");

            byte[] pngBytes;
            using (var ms = new MemoryStream())
            {
                underlay.Image.Save(ms, ImageFormat.Png);
                pngBytes = ms.ToArray();
            }

            using (var content = new MultipartFormDataContent())
            {
                content.Add(new StringContent(underlay.Facet.ToString()), "facet");
                content.Add(new StringContent(underlay.Left.ToString()), "left");
                content.Add(new StringContent(underlay.Top.ToString()), "top");
                content.Add(new StringContent(underlay.Width.ToString()), "width");
                content.Add(new StringContent(underlay.Height.ToString()), "height");

                if (!string.IsNullOrWhiteSpace(underlay.SourcePath))
                    content.Add(new StringContent(underlay.SourcePath), "source_path");

                if (!string.IsNullOrEmpty(_authToken))
                    content.Add(new StringContent(_authToken), "auth_token");

                var imageContent = new ByteArrayContent(pngBytes);
                imageContent.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                content.Add(imageContent, "image", "underlay.png");

                var url = $"{_baseUrl}/updateUnderlay.php";
                var response = await _httpClient.PostAsync(url, content);
                var body = await response.Content.ReadAsStringAsync();

                bool isOk = response.IsSuccessStatusCode &&
                            (body.Contains("\"status\":\"ok\"") || body.Contains("\"status\": \"ok\""));

                string areaId = $"facet:{underlay.Facet} [{underlay.Left},{underlay.Top},{underlay.Width},{underlay.Height}]";
                if (isOk)
                {
                    return new UploadResult
                    {
                        Success = true,
                        Message = "Underlay uploaded successfully",
                        GraphicId = areaId,
                        Atlas = ParseJsonField(body, "atlas"),
                        NewHash = ParseJsonField(body, "new_hash")
                    };
                }

                string errorMsg = ParseJsonField(body, "message")
                               ?? ParseJsonField(body, "error")
                               ?? $"HTTP {(int)response.StatusCode}: {body}";

                return new UploadResult
                {
                    Success = false,
                    Message = errorMsg,
                    GraphicId = areaId
                };
            }
        }

        /// <summary>
        /// Uploads multiple underlay area payloads in sequence.
        /// </summary>
        public async Task<List<UploadResult>> UploadMultipleUnderlaysAsync(
            List<UnderlayUploadRequest> items,
            Action<int, int> onProgress = null)
        {
            var results = new List<UploadResult>();
            if (items == null || items.Count == 0)
                return results;

            int completed = 0;
            int total = items.Count;

            foreach (var item in items)
            {
                try
                {
                    var result = await UploadUnderlayAsync(item);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    results.Add(new UploadResult
                    {
                        Success = false,
                        Message = ex.Message,
                        GraphicId = $"facet:{item?.Facet ?? -1}"
                    });
                }

                completed++;
                onProgress?.Invoke(completed, total);
            }

            return results;
        }

        /// <summary>
        /// Fetches the underlay index for a given facet from the server.
        /// Expects GET /gameUnderlays.php?action=list&amp;facet={facet}
        /// Returns a JSON array of underlay records.
        /// </summary>
        public async Task<List<UnderlayIndexEntry>> GetUnderlayIndexAsync(int facet)
        {
            var url = $"{_baseUrl}/gameUnderlays.php?action=list&facet={facet}";
            System.Diagnostics.Debug.WriteLine($"[JarJar] Fetching underlay index from {url}");

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();

            return ParseUnderlayIndex(body);
        }

        /// <summary>
        /// Downloads an underlay PNG by its server-relative or absolute URL.
        /// </summary>
        public async Task<Bitmap> DownloadUnderlayImageAsync(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return null;

            // Allow relative paths returned by the server
            string url = imageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? imageUrl
                : _baseUrl.TrimEnd('/') + "/" + imageUrl.TrimStart('/');

            System.Diagnostics.Debug.WriteLine($"[JarJar] Downloading underlay image from {url}");

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            using (var stream = await response.Content.ReadAsStreamAsync())
            {
                return new Bitmap(stream);
            }
        }

        /// <summary>
        /// Parses a JSON array of underlay index entries.
        /// Handles both {"underlays":[...]} wrapper and bare [...] arrays.
        /// </summary>
        private static List<UnderlayIndexEntry> ParseUnderlayIndex(string json)
        {
            var entries = new List<UnderlayIndexEntry>();
            if (string.IsNullOrWhiteSpace(json)) return entries;

            // Strip wrapper object if present: {"underlays":[...]}
            int arrayStart = json.IndexOf('[');
            int arrayEnd   = json.LastIndexOf(']');
            if (arrayStart < 0 || arrayEnd < 0) return entries;

            string arrayBody = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);

            // Split on object boundaries: find each {...} block
            int depth = 0;
            int objStart = -1;
            for (int i = 0; i < arrayBody.Length; i++)
            {
                char c = arrayBody[i];
                if (c == '{') { if (depth == 0) objStart = i; depth++; }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && objStart >= 0)
                    {
                        string obj = arrayBody.Substring(objStart, i - objStart + 1);
                        var entry = ParseUnderlayObject(obj);
                        if (entry != null) entries.Add(entry);
                        objStart = -1;
                    }
                }
            }

            return entries;
        }

        private static UnderlayIndexEntry ParseUnderlayObject(string obj)
        {
            var e = new UnderlayIndexEntry();
            e.Facet     = ParseJsonInt(obj, "facet");
            e.Left      = ParseJsonInt(obj, "left");
            e.Top       = ParseJsonInt(obj, "top");
            e.Width     = ParseJsonInt(obj, "width");
            e.Height    = ParseJsonInt(obj, "height");
            e.ImageUrl  = ParseJsonField(obj, "image") ?? ParseJsonField(obj, "path");
            e.Hash      = ParseJsonField(obj, "hash") ?? ParseJsonField(obj, "new_hash");
            e.UpdatedAt = ParseJsonField(obj, "updated_at");

            if (string.IsNullOrWhiteSpace(e.ImageUrl)) return null;
            return e;
        }

        // ================================================================
        //  Overlay support (JarJar-native format)
        // ================================================================

        /// <summary>
        /// Uploads one overlay area + image payload.
        /// Server endpoint is expected to index by facet/bounds and store PNG separately.
        /// </summary>
        public async Task<UploadResult> UploadOverlayAsync(OverlayUploadRequest overlay)
        {
            if (overlay == null)
                throw new ArgumentNullException("overlay");
            if (overlay.Image == null)
                throw new ArgumentNullException("overlay.Image");

            byte[] pngBytes;
            using (var ms = new MemoryStream())
            {
                overlay.Image.Save(ms, ImageFormat.Png);
                pngBytes = ms.ToArray();
            }

            using (var content = new MultipartFormDataContent())
            {
                content.Add(new StringContent(overlay.Facet.ToString()), "facet");
                content.Add(new StringContent(overlay.Left.ToString()), "left");
                content.Add(new StringContent(overlay.Top.ToString()), "top");
                content.Add(new StringContent(overlay.Width.ToString()), "width");
                content.Add(new StringContent(overlay.Height.ToString()), "height");

                if (!string.IsNullOrWhiteSpace(overlay.SourcePath))
                    content.Add(new StringContent(overlay.SourcePath), "source_path");

                if (!string.IsNullOrEmpty(_authToken))
                    content.Add(new StringContent(_authToken), "auth_token");

                var imageContent = new ByteArrayContent(pngBytes);
                imageContent.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                content.Add(imageContent, "image", "overlay.png");

                var url = $"{_baseUrl}/updateOverlay.php";
                var response = await _httpClient.PostAsync(url, content);
                var body = await response.Content.ReadAsStringAsync();

                bool isOk = response.IsSuccessStatusCode &&
                            (body.Contains("\"status\":\"ok\"") || body.Contains("\"status\": \"ok\""));

                string areaId = $"facet:{overlay.Facet} [{overlay.Left},{overlay.Top},{overlay.Width},{overlay.Height}]";
                if (isOk)
                {
                    return new UploadResult
                    {
                        Success = true,
                        Message = "Overlay uploaded successfully",
                        GraphicId = areaId,
                        Atlas = ParseJsonField(body, "atlas"),
                        NewHash = ParseJsonField(body, "new_hash")
                    };
                }

                string errorMsg = ParseJsonField(body, "message")
                               ?? ParseJsonField(body, "error")
                               ?? $"HTTP {(int)response.StatusCode}: {body}";

                return new UploadResult
                {
                    Success = false,
                    Message = errorMsg,
                    GraphicId = areaId
                };
            }
        }

        /// <summary>
        /// Uploads multiple overlay area payloads in sequence.
        /// </summary>
        public async Task<List<UploadResult>> UploadMultipleOverlaysAsync(
            List<OverlayUploadRequest> items,
            Action<int, int> onProgress = null)
        {
            var results = new List<UploadResult>();
            if (items == null || items.Count == 0)
                return results;

            int completed = 0;
            int total = items.Count;

            foreach (var item in items)
            {
                try
                {
                    var result = await UploadOverlayAsync(item);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    results.Add(new UploadResult
                    {
                        Success = false,
                        Message = ex.Message,
                        GraphicId = $"facet:{item?.Facet ?? -1}"
                    });
                }

                completed++;
                onProgress?.Invoke(completed, total);
            }

            return results;
        }

        /// <summary>
        /// Fetches the overlay index for a given facet from the server.
        /// Expects GET /gameOverlays.php?action=list&amp;facet={facet}
        /// Returns a JSON array of overlay records.
        /// </summary>
        public async Task<List<OverlayIndexEntry>> GetOverlayIndexAsync(int facet)
        {
            var url = $"{_baseUrl}/gameOverlays.php?action=list&facet={facet}";
            System.Diagnostics.Debug.WriteLine($"[JarJar] Fetching overlay index from {url}");

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();

            return ParseOverlayIndex(body);
        }

        /// <summary>
        /// Downloads an overlay PNG by its server-relative or absolute URL.
        /// </summary>
        public async Task<Bitmap> DownloadOverlayImageAsync(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return null;

            string url = imageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? imageUrl
                : _baseUrl.TrimEnd('/') + "/" + imageUrl.TrimStart('/');

            System.Diagnostics.Debug.WriteLine($"[JarJar] Downloading overlay image from {url}");

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            using (var stream = await response.Content.ReadAsStreamAsync())
            {
                return new Bitmap(stream);
            }
        }

        /// <summary>
        /// Parses a JSON array of overlay index entries.
        /// Handles both {"overlays":[...]} wrapper and bare [...] arrays.
        /// </summary>
        private static List<OverlayIndexEntry> ParseOverlayIndex(string json)
        {
            var entries = new List<OverlayIndexEntry>();
            if (string.IsNullOrWhiteSpace(json)) return entries;

            int arrayStart = json.IndexOf('[');
            int arrayEnd = json.LastIndexOf(']');
            if (arrayStart < 0 || arrayEnd < 0) return entries;

            string arrayBody = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);

            int depth = 0;
            int objStart = -1;
            for (int i = 0; i < arrayBody.Length; i++)
            {
                char c = arrayBody[i];
                if (c == '{') { if (depth == 0) objStart = i; depth++; }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && objStart >= 0)
                    {
                        string obj = arrayBody.Substring(objStart, i - objStart + 1);
                        var entry = ParseOverlayObject(obj);
                        if (entry != null) entries.Add(entry);
                        objStart = -1;
                    }
                }
            }

            return entries;
        }

        private static OverlayIndexEntry ParseOverlayObject(string obj)
        {
            var e = new OverlayIndexEntry();
            e.Facet = ParseJsonInt(obj, "facet");
            e.Left = ParseJsonInt(obj, "left");
            e.Top = ParseJsonInt(obj, "top");
            e.Width = ParseJsonInt(obj, "width");
            e.Height = ParseJsonInt(obj, "height");
            e.ImageUrl = ParseJsonField(obj, "image") ?? ParseJsonField(obj, "path");
            e.Hash = ParseJsonField(obj, "hash") ?? ParseJsonField(obj, "new_hash");
            e.UpdatedAt = ParseJsonField(obj, "updated_at");

            if (string.IsNullOrWhiteSpace(e.ImageUrl)) return null;
            return e;
        }

        private static int ParseJsonInt(string json, string fieldName)
        {
            // Try quoted value first, then unquoted number
            string quoted = ParseJsonField(json, fieldName);
            if (quoted != null && int.TryParse(quoted, out int qv)) return qv;

            string marker = "\"" + fieldName + "\":";
            int pos = json.IndexOf(marker);
            if (pos < 0) { marker = "\"" + fieldName + "\": "; pos = json.IndexOf(marker); }
            if (pos < 0) return 0;

            pos += marker.Length;
            while (pos < json.Length && (json[pos] == ' ' || json[pos] == '"')) pos++;

            int end = pos;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) end++;

            return int.TryParse(json.Substring(pos, end - pos), out int v) ? v : 0;
        }

        // ================================================================
        //  List backups / restore
        // ================================================================

        /// <summary>
        /// Lists available server-side backups for a given asset type.
        /// </summary>
        public async Task<string> ListBackupsAsync(string type = "art", int? atlasId = null)
        {
            var url = $"{_baseUrl}/backup-manager.php?action=list&type={type}";
            if (atlasId.HasValue)
                url += $"&atlas={atlasId.Value}";

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// Restores a server-side backup.
        /// </summary>
        public async Task<string> RestoreBackupAsync(string type, string file)
        {
            var url = $"{_baseUrl}/backup-manager.php?action=restore&type={type}&file={Uri.EscapeDataString(file)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        // ================================================================
        //  Helpers
        // ================================================================

        /// <summary>
        /// Parses file hashes from a JSON response body.
        /// </summary>
        private static Dictionary<string, string> ParseFileHashes(string body)
        {
            var hashes = new Dictionary<string, string>();
            int filesStart = body.IndexOf("\"files\"");
            if (filesStart < 0) return hashes;

            int braceStart = body.IndexOf('{', filesStart);
            int braceEnd = body.IndexOf('}', braceStart + 1);
            if (braceStart < 0 || braceEnd < 0) return hashes;

            string filesBlock = body.Substring(braceStart + 1, braceEnd - braceStart - 1);
            var pairs = filesBlock.Split(',');

            foreach (var pair in pairs)
            {
                var parts = pair.Split(':');
                if (parts.Length == 2)
                {
                    string key = parts[0].Trim().Trim('"');
                    string val = parts[1].Trim().Trim('"');
                    hashes[key] = val;
                }
            }
            return hashes;
        }

        /// <summary>
        /// Simple JSON field extractor (avoids adding a JSON library dependency).
        /// </summary>
        private static string ParseJsonField(string json, string fieldName)
        {
            string marker = "\"" + fieldName + "\":\"";
            int pos = json.IndexOf(marker);
            if (pos < 0)
            {
                // Try with space after colon
                marker = "\"" + fieldName + "\": \"";
                pos = json.IndexOf(marker);
            }
            if (pos < 0) return null;

            pos += marker.Length;
            int endPos = json.IndexOf('"', pos);
            if (endPos < 0) return null;

            return json.Substring(pos, endPos - pos);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
