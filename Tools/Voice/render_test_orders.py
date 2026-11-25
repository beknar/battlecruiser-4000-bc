#!/usr/bin/env python3
"""The recorded orders the offline voice commands are tested with (VoskTests, EditMode).

Each order is spoken by Piper in the stock lines' voice (en_US-norman-medium, public domain; see
render_stock.py) and kept as a small 16 kHz mono 16-bit WAV in Tools/Voice/testdata, as the game's
Microphone records it for Vosk. The test feeds each one to Vosk and checks the message it sends.

Piper's voice varies a little from one run to the next, and the shortest orders are the likeliest
to be misheard (of six runs, "Wingman, dock" was heard as "wingman dock" in two, and as "wingman
stop" or "wingman start" in the others): run VoskTests after making them again.

    python Tools/Voice/render_test_orders.py --piper <piper.exe> --voice <en_US-norman-medium.onnx> [--ffmpeg ffmpeg]
"""
import argparse
import json
import os
import subprocess
import sys
import tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Tools", "Voice", "testdata")

# (the file's name, what's said; VoskTests has what each should send)
ORDERS = {
    "follow_me": "Wingman, follow me.",
    "mine_inner_belt": "Mine at the Inner Belt.",
    "all_ships_regroup": "All ships, regroup.",
    "dock": "Wingman, dock.",
    "warp_thalassa": "Warp to Thalassa.",
    "sell_ore": "Sell the ore.",
    "give_credits": "Give me two thousand credits.",
    # (a place the small model doesn't know: heard as "warp to [unk]")
    "warp_gilgal": "Warp to Gilgal.",
}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--piper", required=True)
    ap.add_argument("--voice", required=True)
    ap.add_argument("--ffmpeg", default="ffmpeg")
    a = ap.parse_args()
    os.makedirs(OUT, exist_ok=True)
    work = tempfile.mkdtemp(prefix="voskorders_")
    feed = "\n".join(json.dumps({"text": t, "output_file": os.path.join(work, k + ".wav")}) for k, t in ORDERS.items()) + "\n"
    r = subprocess.run([a.piper, "-m", a.voice, "--json-input", "--length_scale", "0.95", "--sentence_silence", "0.1"],
                       input=feed.encode("utf-8"), capture_output=True)
    if r.returncode != 0:
        sys.exit("piper failed: " + r.stderr.decode(errors="replace")[-800:])
    for k in ORDERS:
        dst = os.path.join(OUT, "order_" + k + ".wav")
        r = subprocess.run([a.ffmpeg, "-v", "error", "-y", "-i", os.path.join(work, k + ".wav"), "-ar", "16000", "-ac", "1",
                            "-c:a", "pcm_s16le", "-map_metadata", "-1", "-fflags", "+bitexact", "-flags:a", "+bitexact", dst],
                           capture_output=True)
        if r.returncode != 0:
            sys.exit(f"ffmpeg failed on {k}: " + r.stderr.decode(errors="replace")[-600:])
    print(f"{len(ORDERS)} orders in {OUT}")


if __name__ == "__main__":
    main()
