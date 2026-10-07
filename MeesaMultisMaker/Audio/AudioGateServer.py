"""Quality-gate sidecar for the ambient watcher (localhost only).

One process, models loaded once, plain HTTP after that -- same lifecycle
pattern as the vision engine. Endpoints (all JSON):

  GET  /health                 {"status":"ok","whisper":bool,"clap":bool,
                               "caption":bool}
  POST /transcribe {"path"}     {"ok":true,"words":N,"text":"..."}
  POST /clap {"path","prompt"}  {"ok":true,"score":0.23}
  POST /caption {"path"}        {"ok":true,"caption":"..."}

Exit codes don't matter; the C# client treats any failure as
"gates unavailable" and generation continues ungated.
"""
import json
import os
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PORT = 18171
_whisper = None
_clap_model = None
_clap_proc = None
_cap_model = None
_cap_proc = None
_gpu_lock = threading.Lock()


def load_models():
    global _whisper, _clap_model, _clap_proc
    import torch
    if not torch.cuda.is_available():
        raise RuntimeError("CUDA unavailable for gate models")
    from faster_whisper import WhisperModel
    _whisper = WhisperModel("base.en", device="cuda",
                            compute_type="float16")
    from transformers import ClapModel, ClapProcessor
    _clap_model = ClapModel.from_pretrained(
        "laion/clap-htsat-unfused").cuda().eval()
    _clap_proc = ClapProcessor.from_pretrained(
        "laion/clap-htsat-unfused")


def transcribe(path):
    import torch
    with _gpu_lock:
        segments, _ = _whisper.transcribe(path, vad_filter=True)
        text = " ".join(s.text for s in segments).strip()
    return {"ok": True, "words": len(text.split()), "text": text[:500]}


def ensure_captioner():
    """Lazy: the captioner loads on first /caption, not at startup."""
    global _cap_model, _cap_proc
    if _cap_model is not None:
        return
    from transformers import (WhisperForConditionalGeneration,
                              WhisperProcessor)
    _cap_proc = WhisperProcessor.from_pretrained("openai/whisper-small")
    _cap_model = WhisperForConditionalGeneration.from_pretrained(
        "laion/sound-effect-captioning-whisper").cuda().eval()
    _cap_model.generation_config.forced_decoder_ids = None


def caption(path):
    import librosa
    import torch
    ensure_captioner()
    y, _ = librosa.load(path, sr=16000, mono=True)
    y = y[:16000 * 30]
    feats = _cap_proc.feature_extractor(
        y, sampling_rate=16000,
        return_tensors="pt").input_features.cuda()
    with _gpu_lock:
        with torch.no_grad():
            ids = _cap_model.generate(feats, max_new_tokens=200,
                                      num_beams=1, do_sample=False)
    text = _cap_proc.batch_decode(
        ids, skip_special_tokens=True)[0].strip()
    return {"ok": True, "caption": text[:600]}


def clap_score(path, prompt):
    import librosa
    import numpy as np
    import torch
    y, _ = librosa.load(path, sr=48000, mono=True)
    win = 480000  # CLAP trains on ~10s windows; mean-pool longer clips
    if len(y) < win:
        y = np.pad(y, (0, win - len(y)))
    sims = []
    with _gpu_lock:
        with torch.no_grad():
            for s in range(0, len(y) - win + 1, win):
                inp = _clap_proc(text=[prompt], audio=y[s:s + win],
                                 sampling_rate=48000, return_tensors="pt",
                                 padding=True, truncation=True)
                inp = {k: (v.cuda() if hasattr(v, "cuda") else v)
                       for k, v in inp.items()}
                out = _clap_model(**inp)
                a = out.audio_embeds / out.audio_embeds.norm(
                    dim=-1, keepdim=True)
                t = out.text_embeds / out.text_embeds.norm(
                    dim=-1, keepdim=True)
                sims.append(float((a @ t.T)[0][0].cpu()))
    return {"ok": True,
            "score": sum(sims) / len(sims) if sims else 0.0}


class Handler(BaseHTTPRequestHandler):
    server_version = "GateServer/1"

    def _send(self, obj):
        body = json.dumps(obj).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _body(self):
        try:
            n = int(self.headers.get("Content-Length", 0))
        except Exception:
            n = 0
        if n <= 0 or n > 4096:
            return {}
        try:
            return json.loads(self.rfile.read(n).decode("utf-8"))
        except Exception:
            return {}

    def do_GET(self):
        if self.path == "/health":
            self._send({"status": "ok",
                        "whisper": _whisper is not None,
                        "clap": _clap_model is not None,
                        "caption": _cap_model is not None})
        else:
            self._send({"ok": False, "error": "unknown endpoint"})

    def do_POST(self):
        try:
            req = self._body()
            if self.path == "/transcribe":
                path = req.get("path", "")
                if not path or not os.path.isfile(path):
                    self._send({"ok": False, "error": "file not found"})
                else:
                    self._send(transcribe(path))
            elif self.path == "/caption":
                path = req.get("path", "")
                if not path or not os.path.isfile(path):
                    self._send({"ok": False, "error": "file not found"})
                else:
                    self._send(caption(path))
            elif self.path == "/clap":
                path = req.get("path", "")
                prompt = (req.get("prompt", "") or "")[:600]
                if not path or not os.path.isfile(path):
                    self._send({"ok": False, "error": "file not found"})
                elif not prompt.strip():
                    self._send({"ok": False, "error": "empty prompt"})
                else:
                    self._send(clap_score(path, prompt))
            else:
                self._send({"ok": False, "error": "unknown endpoint"})
        except Exception as ex:
            try:
                self._send({"ok": False, "error": str(ex)[:300]})
            except Exception:
                pass

    def log_message(self, *args):
        pass


if __name__ == "__main__":
    load_models()
    print("GATE_READY", flush=True)
    srv = ThreadingHTTPServer(("127.0.0.1", PORT), Handler)
    srv.serve_forever()
