using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MeesaMultisMaker.ThreeD
{
    /// <summary>
    /// Live preview feed from ComfyUI. The backend only sends "executed"
    /// messages (which carry preview filenames) to the websocket client
    /// whose id matches the /prompt client_id, and /history only exists
    /// after the run - so without this socket there is no way to show the
    /// Flux reference image while the 3D bakes. Best-effort: any failure
    /// just means no live thumbnails (history fallback still applies).
    /// </summary>
    public class ComfyPreviewSocket : IDisposable
    {
        public class PreviewEvent
        {
            public string PromptId;
            public string NodeId;
            public string Filename;
            public string Subfolder;
            public string Type;
        }

        public event Action<PreviewEvent> PreviewReady;

        /// <summary>Optional diagnostics sink (connection, message types, errors).</summary>
        public Action<string> Trace;

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private bool _disposed;

        private void T(string msg)
        {
            try { if (Trace != null) Trace(msg); }
            catch { }
        }

        public async Task ConnectAsync(string baseUrl, string clientId, CancellationToken ct)
        {
            Close();
            string wsUrl = baseUrl.TrimEnd('/');
            if (wsUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                wsUrl = "wss://" + wsUrl.Substring("https://".Length);
            else if (wsUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                wsUrl = "ws://" + wsUrl.Substring("http://".Length);
            wsUrl += "/ws?clientId=" + Uri.EscapeDataString(clientId);

            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();
            _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            using (var link = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token))
            {
                await _ws.ConnectAsync(new Uri(wsUrl), link.Token);
            }
            T("connected to " + wsUrl);
            Task.Run(() => ListenLoop(_cts.Token));
        }

        private async Task ListenLoop(CancellationToken ct)
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            byte[] buf = new byte[1 << 16];
            var sb = new StringBuilder();
            try
            {
                T("listen loop started");
                while (!ct.IsCancellationRequested && _ws != null &&
                       _ws.State == WebSocketState.Open)
                {
                    sb.Length = 0;
                    WebSocketReceiveResult res;
                    do
                    {
                        var seg = new ArraySegment<byte>(buf);
                        res = await _ws.ReceiveAsync(seg, ct);
                        if (res.MessageType == WebSocketMessageType.Close)
                        {
                            T("server closed connection");
                            return;
                        }
                        if (res.MessageType != WebSocketMessageType.Text) break;
                        sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
                    } while (!res.EndOfMessage);

                    if (res.MessageType != WebSocketMessageType.Text || sb.Length == 0)
                        continue;

                    string text = sb.ToString();
                    PreviewEvent ev = Parse(ser, text, delegate (string s) { T(s); });
                    if (ev != null)
                    {
                        T("parsed preview: " + ev.Filename);
                        try
                        {
                            if (PreviewReady != null) PreviewReady(ev);
                        }
                        catch { }
                    }
                }
                T("listen loop ended");
            }
            catch (OperationCanceledException) { T("listen cancelled"); }
            catch (Exception ex) { T("listen error: " + ex.Message); }
        }

        internal static PreviewEvent Parse(JavaScriptSerializer ser, string json, Action<string> trace = null)
        {
            Dictionary<string, object> root;
            try { root = ser.DeserializeObject(json) as Dictionary<string, object>; }
            catch (Exception ex)
            {
                if (trace != null)
                {
                    try { trace("parse EX: " + ex.GetType().Name + ": " + ex.Message + " | len=" + (json == null ? -1 : json.Length)); }
                    catch { }
                }
                return null;
            }
            if (root == null) return null;
            object t;
            if (!root.TryGetValue("type", out t) || !string.Equals(t as string, "executed")) return null;
            var data = root.ContainsKey("data") ? root["data"] as Dictionary<string, object> : null;
            if (data == null) return null;
            string promptId = data.ContainsKey("prompt_id") ? data["prompt_id"] as string : null;
            string nodeId = data.ContainsKey("node") ? Convert.ToString(data["node"]) : null;
            var output = data.ContainsKey("output") ? data["output"] as Dictionary<string, object> : null;
            if (output == null || string.IsNullOrEmpty(promptId)) return null;
            // NOTE: JavaScriptSerializer yields object[], NOT ArrayList -
            // an `as ArrayList` cast silently returns null here.
            var images = output.ContainsKey("images") ? output["images"] as System.Collections.IList : null;
            if (images == null || images.Count == 0) return null;
            var first = images[0] as Dictionary<string, object>;
            if (first == null || !first.ContainsKey("filename")) return null;
            return new PreviewEvent
            {
                PromptId = promptId,
                NodeId = nodeId ?? string.Empty,
                Filename = first["filename"] as string,
                Subfolder = first.ContainsKey("subfolder") ? (first["subfolder"] as string ?? string.Empty) : string.Empty,
                Type = first.ContainsKey("type") ? (first["type"] as string ?? "output") : "output",
            };
        }

        public void Close()
        {
            try
            {
                if (_cts != null) { _cts.Cancel(); _cts.Dispose(); _cts = null; }
            }
            catch { }
            try
            {
                if (_ws != null)
                {
                    if (_ws.State == WebSocketState.Open)
                    {
                        try { _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).Wait(1500); }
                        catch { }
                    }
                    _ws.Dispose();
                    _ws = null;
                }
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Close();
        }
    }
}
