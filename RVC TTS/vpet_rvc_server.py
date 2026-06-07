import argparse
import json
import os
import sys
import threading
import traceback
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


def configure_ffmpeg(script_dir):
    ffmpeg_root = os.path.join(script_dir, "ffmpeg")
    if not os.path.isdir(ffmpeg_root):
        return

    for root, _, files in os.walk(ffmpeg_root):
        if "ffmpeg.exe" in files:
            os.environ["PATH"] = root + os.pathsep + os.environ.get("PATH", "")
            return


class RvcServer:
    def __init__(self, args):
        script_dir = os.path.dirname(os.path.abspath(__file__))
        configure_ffmpeg(script_dir)
        sys.path.insert(0, script_dir)

        from tts_with_rvc import TTS_RVC

        os.makedirs(args.output_dir, exist_ok=True)
        self.args = args
        self.lock = threading.Lock()
        self.tts = TTS_RVC(
            model_path=args.model,
            index_path=args.index,
            voice=args.voice,
            device=args.device,
            output_directory=args.output_dir,
            f0_method=args.f0_method,
        )
        self.tts.set_voice(args.voice)

    def speak(self, text, filename):
        if not text or not text.strip():
            raise ValueError("text is empty")

        with self.lock:
            output_path = self.tts(
                text=text,
                pitch=self.args.pitch,
                index_rate=self.args.index_rate,
                output_filename=filename or None,
                is_half=False if self.args.device.lower() == "cpu" else None,
            )
        return os.path.abspath(output_path)


def make_handler(server_state):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, format, *args):
            return

        def do_GET(self):
            if self.path != "/health":
                self.send_error(404)
                return
            self._send_json(200, {"ok": True})

        def do_POST(self):
            if self.path != "/speak":
                self.send_error(404)
                return

            try:
                length = int(self.headers.get("Content-Length", "0"))
                body = self.rfile.read(length).decode("utf-8")
                payload = json.loads(body) if body else {}
                output_path = server_state.speak(
                    payload.get("text", ""),
                    payload.get("filename", ""),
                )
                self._send_json(200, {"ok": True, "path": output_path})
            except Exception as exc:
                traceback.print_exc()
                self._send_json(500, {"ok": False, "error": str(exc)})

        def _send_json(self, status, payload):
            data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

    return Handler


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=53683)
    parser.add_argument("--model", required=True)
    parser.add_argument("--index", default="")
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--voice", default="zh-TW-HsiaoYuNeural")
    parser.add_argument("--device", default="cpu")
    parser.add_argument("--pitch", type=int, default=0)
    parser.add_argument("--index-rate", type=float, default=0.75)
    parser.add_argument("--f0-method", default="rmvpe")
    args = parser.parse_args()

    server_state = RvcServer(args)
    httpd = ThreadingHTTPServer((args.host, args.port), make_handler(server_state))
    print(f"VPET_RVC_SERVER_READY http://{args.host}:{args.port}", flush=True)
    httpd.serve_forever()


if __name__ == "__main__":
    main()
