#!/bin/bash
# Copies the soundtrack's playlist into the Unity project (Resources/CarrierAudio/Music/Calm and
# .../Combat) and writes Music/playlist.txt, which MusicDirector reads:
#   <state>|<resource path>|<title>|<loops: 1 or 0>|<the same piece's other arrangement, or empty>
# Each track is brought to the same loudness (calm -17 LUFS, combat -15 LUFS integrated, never
# above -1 dBFS) by a plain gain, so it stays sample for sample as it was (the Ambient and
# Action arrangements of a Space and Science Fiction Music Pack Vol 1 piece still run in step).
# Run by stage_audio.sh, or on its own (it replaces only the playlist's tracks).
set -e
FF="/c/Users/bekna/AppData/Local/Microsoft/WinGet/Packages/Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/ffmpeg-9.0.2-full_build/bin/ffmpeg.exe"
OUT="E:/code/claude_unity/My project/Assets/Carrier/Resources/CarrierAudio/Music"
MAN="E:/code/claude_unity/My project/Assets/Carrier/Data/audio_sources.txt"
LIB="F:/non-unreal-assets"
V1="$LIB/gamedevmarket assets/Space and Science Fiction Music Pack Vol 1/Space Science Fiction Music Pack/audio"
V2="$LIB/gamedevmarket assets/Space and Science Fiction Music Pack Vol 2/Space_Science_Fiction_MusicPackVol.2/Music"
SF="$LIB/gamedevmarket assets/Sci Fi Fantasy Music/Sci Fi Fantasy Music"
SJ="$LIB/fanatical bundle packs/Game Music Power Bundle/Space Journey Music Pack"

rm -rf "$OUT/Calm" "$OUT/Combat" "$OUT/Calm.meta" "$OUT/Combat.meta"
mkdir -p "$OUT/Calm" "$OUT/Combat"
LIST="$OUT/playlist.txt"
echo "# state|resource|title|loops|paired with (written by Tools/Audio/stage_music.sh)" > "$LIST"
[ -f "$MAN" ] && { grep -v "^Music/Calm/\|^Music/Combat/" "$MAN" > "$MAN.tmp" || true; mv "$MAN.tmp" "$MAN"; }

# track <state> <file name> <title> <loops> <paired> <source>
track() {
  local state=$1 name=$2 title=$3 loops=$4 pair=$5 src=$6
  local dir=Calm target=-17
  [ "$state" = combat ] && { dir=Combat; target=-15; }
  local dst="$OUT/$dir/$name.wav"
  local m=$("$FF" -hide_banner -nostats -i "$src" -af ebur128=peak=true -f null - 2>&1)
  local I=$(echo "$m" | awk '/^ *I:/{v=$2} END{print v}')
  local P=$(echo "$m" | awk '/^ *Peak:/{v=$2} END{print v}')
  local gain=$(awk "BEGIN{g=$target-($I); c=-1-($P); if (c<g) g=c; printf \"%.2f\", g}")
  "$FF" -y -loglevel error -i "$src" -ar 44100 -af "volume=${gain}dB" -c:a pcm_s16le "$dst"
  echo "$state|Music/$dir/$name|$title|$loops|$pair" >> "$LIST"
  echo "Music/$dir/$name.wav  <-  ${src#$LIB/}  [stereo, gain ${gain} dB (from $I LUFS)]" >> "$MAN"
}

# Space and Science Fiction Music Pack Vol 1: each piece as calm (Ambient) and combat (Action),
# the two arrangements in step
for p in "Antimatter Fountain" "Binary Star" "Bright Star" "Dangerous Space" "Deepness of Space" \
         "Into the Unknown" "Mystery Unveils" "Nebula" "Peaceful System" "Pulsar" "Solar Wind" \
         "Star Formation" "Uncharted Sector" "Void" "White Dwarf"; do
  f=${p// /_}
  track calm "$f" "$p" 1 "Music/Combat/$f" "$V1/$p [Ambient Loop].wav"
  track combat "$f" "$p" 1 "Music/Calm/$f" "$V1/$p [Action Loop].wav"
done

# Space and Science Fiction Music Pack Vol 2: the slow, atmospheric pieces calm, the two that
# build to a storm combat (their long loops: a track changes before it comes round)
for p in "Comet Tail" "Event Horizon" "Galactic Center" "Ring Nebula" "Star Cluster" "Stellar Dust"; do
  track calm "${p// /_}" "$p" 1 "" "$V2/${p// /_}/${p}_loop.wav"
done
for p in "Gravitational Wave" "Star Collapsing"; do
  track combat "${p// /_}" "$p" 1 "" "$V2/${p// /_}/${p}_loop.wav"
done

# Sci Fi Fantasy Music: exploration calm, action combat (their loops)
for p in "Familiar Planet" "Find a Way" "Mission Control" "Planetary Outskirts" "Too Dark To Tell"; do
  track calm "${p// /_}" "$p" 1 "" "$SF/Exploration - $p LOOP.wav"
done
for p in "Battle Ensued" "Inferior Design" "Let's Ride!" "On Your Tail" "Rise of the Apprentice"; do
  n=$(echo "$p" | tr -d "'!"); track combat "${n// /_}" "$p" 1 "" "$SF/Action - $p LOOP.wav"
done

# Space Journey Music Pack: two driving themes for combat (not loops: they change before the end)
track combat "Space_Journey_Theme_2" "Space Journey Theme 2" 0 "" "$SJ/Space Journey Theme #2.wav"
track combat "Space_Journey_Theme_4" "Space Journey Theme 4" 0 "" "$SJ/Space Journey Theme #4.wav"

echo "playlist: $(grep -c '^calm|' "$LIST") calm, $(grep -c '^combat|' "$LIST") combat"
