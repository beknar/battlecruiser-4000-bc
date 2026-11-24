#!/usr/bin/env python3
"""The AI wingman's stock voice: every line it always says the same way, recorded ahead of time.

Reads the lines from the game's own data:
  - Resources/Data/wingman_dialogue.json: the personality's lines with no blanks, or whose only
    blank is {me} (recorded as "Wingman", the bot's usual name);
  - Resources/Data/wingman_replies.json: the replies to orders with no blanks, and those whose
    only blanks are {place} or {field}, once for each place or asteroid field in the star systems
    (Data/star_system.json, Data/nine_hells.json).
Each is spoken by Piper (one voice, public domain: en_US-norman-medium), given the radio's sound
(band-limited, a little compressed, a squelch of static at each end; put into the file, since the
web player has no audio filters) and kept as a small Ogg in Resources/WingmanVoice, with
index.json from each line as the game matches it (normalised: WingmanVoice.Key in C#, key() here)
to its clip. The game plays a clip when a wingman's line matches one, and speaks any other line
with the live voice.

    python Tools/Voice/render_stock.py --piper <piper.exe> --voice <en_US-norman-medium.onnx> [--ffmpeg ffmpeg]
    python Tools/Voice/render_stock.py --check      # every line has a clip, and nothing else

Run on the CPU, with no Unity and no game: about a minute.
"""
import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unicodedata
import uuid

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RES = os.path.join(ROOT, "Assets", "Carrier", "Resources")
OUT = os.path.join(RES, "WingmanVoice")
SYSTEMS = [os.path.join(ROOT, "Assets", "Carrier", "Data", f) for f in ("star_system.json", "nine_hells.json")]
BLANK = re.compile(r"\{(\w+)\}")
BOT_NAME = "Wingman"
VOICE_NAME = "en_US-norman-medium"


# a clip's import settings: mono, not normalised (the level is the radio's, set here), compressed
# at about half quality (plenty for a 16 kHz radio voice), loaded with the clip
META = """fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 0.5
    conversionMode: 0
    preloadAudioData: 1
  platformSettingOverrides: {{}}
  forceToMono: 1
  normalize: 0
  loadInBackground: 0
  ambisonic: 0
  3D: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def key(text):
    """A line as it is matched: lower case, apostrophes dropped, anything but letters and digits a
    space, spaces single (WingmanPersonality.Normalize, WingmanVoice.Key)."""
    out, space = [], True
    for ch in (text or "").lower():
        if ch in "'’":
            continue
        if unicodedata.category(ch) in ("Lu", "Ll", "Lt", "Lm", "Lo", "Nd"):
            out.append(ch)
            space = False
        elif not space:
            out.append(" ")
            space = True
    return "".join(out).strip()


def places():
    """Every place and asteroid field named in the star systems' data."""
    names, fields = set(), set()

    def walk(o):
        if isinstance(o, dict):
            n = o.get("name") or o.get("displayName")
            if isinstance(n, str) and n.strip():
                names.add(n.strip())
            for v in o.values():
                walk(v)
        elif isinstance(o, list):
            for v in o:
                walk(v)

    for f in SYSTEMS:
        with open(f, encoding="utf-8") as fh:
            d = json.load(fh)
        walk(d)
        for field in d.get("fields", []):
            if isinstance(field, dict) and field.get("name"):
                fields.add(field["name"].strip())
    return sorted(names), sorted(fields)


def lines():
    """Every line to record: the text as the wingman says it (after its "Name: ")."""
    out = []
    with open(os.path.join(RES, "Data", "wingman_dialogue.json"), encoding="utf-8") as fh:
        d = json.load(fh)
    said = list(d.get("reprompt", [])) + list(d.get("paused", []))
    for node in d.get("nodes", []):
        for say in node.get("say", []):
            said += say.get("lines", [])
    for line in said:
        blanks = set(BLANK.findall(line))
        if blanks <= {"me"}:
            out.append(line.replace("{me}", BOT_NAME))
    with open(os.path.join(RES, "Data", "wingman_replies.json"), encoding="utf-8") as fh:
        replies = json.load(fh)["replies"]
    names, fields = places()
    for r in replies:
        line = r["line"]
        blanks = set(BLANK.findall(line))
        if r["key"] == "help":
            continue    # (a list of orders with <these>: read, not heard)
        if not blanks:
            out.append(line)
        elif blanks == {"place"}:
            out += [line.replace("{place}", p) for p in names]
        elif blanks == {"field"}:
            out += [line.replace("{field}", p) for p in fields]
    seen, unique = set(), []
    for line in out:
        k = key(line)
        if k and k not in seen:
            seen.add(k)
            unique.append(line)
    return unique


def clip_name(k):
    return "v" + hashlib.sha1(k.encode("utf-8")).hexdigest()[:10]


def spoken(line):
    """The words as Piper should say them (the page's punctuation tidied for speech)."""
    t = line.replace(";", ",").replace("(", "").replace(")", "")
    t = re.sub(r'"', "", t)
    return t[0].upper() + t[1:] if t else t


def render(args):
    todo = lines()
    os.makedirs(OUT, exist_ok=True)
    work = tempfile.mkdtemp(prefix="wingvoice_")
    try:
        jobs = []
        for line in todo:
            k = key(line)
            name = clip_name(k)
            jobs.append({"text": spoken(line), "output_file": os.path.join(work, name + ".wav"), "name": name, "key": k})
        feed = "\n".join(json.dumps({"text": j["text"], "output_file": j["output_file"]}) for j in jobs) + "\n"
        r = subprocess.run([args.piper, "-m", args.voice, "--json-input", "--length_scale", "0.95", "--sentence_silence", "0.1"],
                           input=feed.encode("utf-8"), capture_output=True)
        if r.returncode != 0:
            sys.exit("piper failed: " + r.stderr.decode(errors="replace")[-800:])
        # the radio: a squelch of static, the voice band-limited and compressed, a squelch again
        radio = ("anoisesrc=d=0.07:c=white:a=0.06[n1];anoisesrc=d=0.05:c=white:a=0.05[n2];"
                 "[0:a]aresample=16000,highpass=f=320,lowpass=f=3300,acompressor=threshold=-20dB:ratio=4:attack=5:release=80,volume=2.6[v];"
                 "[n1]aresample=16000,highpass=f=800,lowpass=f=4000[s1];[n2]aresample=16000,highpass=f=800,lowpass=f=4000[s2];"
                 "[s1][v][s2]concat=n=3:v=0:a=1,aformat=sample_rates=16000:channel_layouts=mono[out]")
        # (a clip no longer wanted goes, with its .meta; the others keep theirs, so Unity's ids for
        # them don't change from one recording to the next)
        wanted = {j["name"] for j in jobs}
        for old in os.listdir(OUT):
            stem = old.split(".")[0]
            if (old.endswith(".ogg") or old.endswith(".ogg.meta")) and stem not in wanted:
                os.remove(os.path.join(OUT, old))
        for j in jobs:
            dst = os.path.join(OUT, j["name"] + ".ogg")
            r = subprocess.run([args.ffmpeg, "-v", "error", "-y", "-i", j["output_file"], "-filter_complex", radio, "-map", "[out]",
                                "-c:a", "libvorbis", "-q:a", "0", dst], capture_output=True)
            if r.returncode != 0 or not os.path.exists(dst):
                sys.exit(f"ffmpeg failed on {j['text']!r}: " + r.stderr.decode(errors="replace")[-600:])
        for j in jobs:
            meta = os.path.join(OUT, j["name"] + ".ogg.meta")
            if os.path.exists(meta):
                text = open(meta, encoding="utf-8").read()
                guid = text.split("guid:")[1].split()[0]
            else:
                guid = uuid.uuid4().hex
            with open(meta, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(META.format(guid=guid))
        index = {"note": f"The wingman's stock voice (Tools/Voice/render_stock.py, Piper {VOICE_NAME}, public domain): "
                         "each line as the game matches it (WingmanVoice.Key) and its clip here.",
                 "voice": VOICE_NAME,
                 "clips": [{"key": j["key"], "clip": j["name"], "text": j["text"]} for j in jobs]}
        with open(os.path.join(OUT, "index.json"), "w", encoding="utf-8", newline="\n") as fh:
            json.dump(index, fh, indent=1, ensure_ascii=False)
            fh.write("\n")
        size = sum(os.path.getsize(os.path.join(OUT, f)) for f in os.listdir(OUT) if f.endswith(".ogg"))
        print(f"{len(jobs)} clips, {size / 1e6:.1f} MB, in {OUT}")
    finally:
        shutil.rmtree(work, ignore_errors=True)


def check():
    with open(os.path.join(OUT, "index.json"), encoding="utf-8") as fh:
        index = json.load(fh)
    have = {c["key"]: c["clip"] for c in index["clips"]}
    bad = 0
    for line in lines():
        k = key(line)
        if k not in have:
            print("no clip:", line)
            bad += 1
        elif not os.path.exists(os.path.join(OUT, have[k] + ".ogg")):
            print("clip missing:", have[k], line)
            bad += 1
    for line in lines():
        if any(ord(ch) > 127 for ch in line):
            print("not plain text (the game and this tool might key it differently):", line)
            bad += 1
    extra = set(have) - {key(l) for l in lines()}
    for k in sorted(extra):
        print("clip for no line:", k)
    if bad or extra:
        sys.exit(1)
    print(f"ok: {len(have)} clips, one for every line")


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--piper")
    ap.add_argument("--voice")
    ap.add_argument("--ffmpeg", default="ffmpeg")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--list", action="store_true", help="print the lines, record nothing")
    a = ap.parse_args()
    if a.list:
        for l in lines():
            print(l)
    elif a.check:
        check()
    else:
        if not a.piper or not a.voice:
            ap.error("--piper and --voice are needed to record")
        render(a)
