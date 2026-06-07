import argparse
import os
import sys


def configure_ffmpeg(script_dir):
    ffmpeg_root = os.path.join(script_dir, "ffmpeg")
    if not os.path.isdir(ffmpeg_root):
        return

    for root, _, files in os.walk(ffmpeg_root):
        if "ffmpeg.exe" in files:
            os.environ["PATH"] = root + os.pathsep + os.environ.get("PATH", "")
            return


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--text", required=True)
    parser.add_argument("--model", required=True)
    parser.add_argument("--index", default="")
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--voice", default="zh-TW-HsiaoYuNeural")
    parser.add_argument("--device", default="cpu")
    parser.add_argument("--pitch", type=int, default=0)
    parser.add_argument("--index-rate", type=float, default=0.75)
    parser.add_argument("--f0-method", default="rmvpe")
    parser.add_argument("--filename", default="")
    args = parser.parse_args()

    script_dir = os.path.dirname(os.path.abspath(__file__))
    configure_ffmpeg(script_dir)
    sys.path.insert(0, script_dir)

    from tts_with_rvc import TTS_RVC

    os.makedirs(args.output_dir, exist_ok=True)

    tts = TTS_RVC(
        model_path=args.model,
        index_path=args.index,
        voice=args.voice,
        device=args.device,
        output_directory=args.output_dir,
        f0_method=args.f0_method,
    )
    tts.set_voice(args.voice)
    output_path = tts(
        text=args.text,
        pitch=args.pitch,
        index_rate=args.index_rate,
        output_filename=args.filename or None,
        is_half=False if args.device.lower() == "cpu" else None,
    )
    print(os.path.abspath(output_path), flush=True)


if __name__ == "__main__":
    main()
