#!/bin/bash
# Copies the chosen library sounds into the Unity project as mono 44.1 kHz WAVs:
# leading silence removed, long tails cut with a fade, peaks normalised to -1 dBFS.
# Loops are only made mono and normalised (no trim, so their seams stay intact).
# Music is copied untouched (both arrangements must stay sample-aligned).
set -e
FF="/c/Users/bekna/AppData/Local/Microsoft/WinGet/Packages/Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/ffmpeg-9.0.2-full_build/bin/ffmpeg.exe"
OUT="E:/code/claude_unity/My project/Assets/Carrier/Resources/CarrierAudio"
MAN="E:/code/claude_unity/My project/Assets/Carrier/Data/audio_sources.txt"
SW="F:/non-unreal-assets/gamedevmarket assets/Sci Fi Sound Effects Bundle/Stormwave Audio Sci-Fi Sound Effects Bundle"
HB="F:/non-unreal-assets/humble bundle - 2d game creation mega bundle/gdm-sci-fi-game-sound-effects-bundle/Sci-Fi Game Sound Effects Bundle/SFXs"
MU="F:/non-unreal-assets/gamedevmarket assets/Space and Science Fiction Music Pack Vol 1/Space Science Fiction Music Pack/audio"
SF="F:/non-unreal-assets/gamedevmarket assets/Sci Fi Fantasy Music/Sci Fi Fantasy Music"

rm -rf "$OUT"; mkdir -p "$OUT"
echo "Library sounds used by the game (copied to Assets/Carrier/Resources/CarrierAudio)" > "$MAN"
echo "Format: <sound>/<file>  <-  <source>  [processing]" >> "$MAN"

# sfx <Name> <cap seconds or 0> <fade seconds> <source>...
sfx() {
  local name=$1 cap=$2 fade=$3; shift 3
  mkdir -p "$OUT/$name"
  local i=1
  for src in "$@"; do
    local dst="$OUT/$name/${name}_$i.wav" tmp="$OUT/$name/_tmp.wav"
    local af="silenceremove=start_periods=1:start_threshold=-55dB"
    if [ "$cap" != "0" ]; then
      af="$af,atrim=0:$cap,afade=t=out:st=$(awk "BEGIN{print $cap-$fade}"):d=$fade"
    fi
    "$FF" -y -loglevel error -i "$src" -ac 1 -ar 44100 -af "$af" -c:a pcm_s16le "$tmp"
    norm "$tmp" "$dst"
    echo "$name/${name}_$i.wav  <-  ${src#F:/non-unreal-assets/}  [mono, trimmed$( [ "$cap" != "0" ] && echo ", cap $cap s"), peak -1 dB]" >> "$MAN"
    i=$((i+1))
  done
}

# loop <Name> <source>: mono + normalise only
loop() {
  local name=$1 src=$2
  mkdir -p "$OUT/$name"
  local tmp="$OUT/$name/_tmp.wav"
  "$FF" -y -loglevel error -i "$src" -ac 1 -ar 44100 -c:a pcm_s16le "$tmp"
  norm "$tmp" "$OUT/$name/${name}.wav"
  echo "$name/${name}.wav  <-  ${src#F:/non-unreal-assets/}  [mono, peak -1 dB, untrimmed loop]" >> "$MAN"
}

norm() {
  local max=$("$FF" -hide_banner -i "$1" -af volumedetect -f null - 2>&1 | awk '/max_volume/{print $5}')
  local gain=$(awk "BEGIN{print -1 - ($max)}")
  "$FF" -y -loglevel error -i "$1" -af "volume=${gain}dB" -c:a pcm_s16le "$2"
  rm -f "$1"
}

sfx Cannon 0 0 "$SW/Weapons/Ballistic Weapons/Sci-Fi Heavy Ballistic Cannon Shot "{1,2,3}.wav
sfx PointDefense 0.4 0.15 "$HB/Sci-Fi Guns Sound Pack/SFXs/wav/Mono/MachineGun/machinegun_scifi_a_shot_single_0"{1,2,3,4,5,6}.wav
sfx Blaster 0.6 0.2 "$HB/Starship Sound Effects/SFXs/wav/starship_weapon_laser_mono_0"{1,2,3,4,5,6}.wav
sfx MissileLaunch 3.0 1.2 "$HB/Sci-Fi Game Sound Effects/SFXs/wav/Weapons/scifi_weapon_rocket_mono_0"{1,2,3}.wav
sfx ExplosionBig 0 0 "$SW/Weapons/Explosions/Heavy Gritty Sci-Fi Explosion "{1,2,3,4,5}.wav
sfx ExplosionSmall 0 0 "$SW/Weapons/Explosions/Sci-Fi Missile Impact Explosion "{1,2,3,4}.wav
sfx Impact 0 0 "$SW/Impacts/Gritty Sci-Fi Impact "{1,2,3,4,5}.wav
sfx ShieldHit 0 0 "$SW/Impacts/Sci-Fi Force Field Impact "{7,8,6,4,2,9,11}.wav
sfx Catapult 0 0 "$HB/Starship Sound Effects/SFXs/wav/starship_turbine_launch_0"{1,6}.wav
sfx UiLock 0 0 "$HB/Starship Sound Effects/SFXs/wav/starship_ui_blip_notification_02.wav"
sfx UiClear 0 0 "$HB/Starship Sound Effects/SFXs/wav/starship_ui_select_click_02.wav"
sfx UiError 0 0 "$SW/Vehicles/Sci-Fi Spaceship Interface Error Motification 1.wav"
sfx UiAlert 3.5 0.8 "$SW/Vehicles/Sci-Fi Spaceship Interface Missile Incoming Warning 1.wav"
sfx UiKill 0 0 "$HB/Starship Sound Effects/SFXs/wav/starship_ui_notification.wav"
sfx BeamCharge 0 0 "$HB/Energy Sound Effects/SFXs/wav/energy_charging_05.wav"
sfx BeamFire 0 0 "$SW/Weapons/Energy Weapons/Sci-Fi Heavy Beam Cannon Shot "{1,2}.wav
# mini-warp: the drive spooling up, the jump, the travel hum and the drop out of warp
sfx WarpCharge 3.2 0.5 "$HB/Starship Sound Effects/SFXs/wav/starship_engine_startup_01.wav"
sfx WarpJump 0 0 "$SW/Energy/Sci-Fi Warp Energy Discharge 2.wav"
sfx WarpExit 0 0 "$SW/Energy/Sci-Fi Warp Sound 1.wav" "$SW/Energy/Sci-Fi Warp Energy Discharge 4.wav"
loop WarpLoop "$SW/Vehicles/Sci-Fi Spaceship Hyperdrive Engine Loop 1.wav"
loop EngineLoop "$SW/Vehicles/Sci-Fi Spaceship Heavy Engine Loop 1.wav"
loop AmbientLoop "$SW/Ambiences/Sci-Fi Deep Space Ambience Background Loop 1.wav"

mkdir -p "$OUT/Music"
cp "$MU/Void [Ambient Loop].wav" "$OUT/Music/Music_Calm.wav"
cp "$MU/Void [Action Loop].wav" "$OUT/Music/Music_Combat.wav"
echo "Music/Music_Calm.wav  <-  gamedevmarket assets/Space and Science Fiction Music Pack Vol 1/.../Void [Ambient Loop].wav  [as is]" >> "$MAN"
echo "Music/Music_Combat.wav  <-  gamedevmarket assets/Space and Science Fiction Music Pack Vol 1/.../Void [Action Loop].wav  [as is]" >> "$MAN"
# asteroid ambush: a quiet track for the approach, a battle track once the ambush is sprung
cp "$MU/Deepness of Space [Ambient Loop].wav" "$OUT/Music/Travel_Calm.wav"
cp "$SF/Action - Battle Ensued LOOP.wav" "$OUT/Music/Ambush_Combat.wav"
echo "Music/Travel_Calm.wav  <-  gamedevmarket assets/Space and Science Fiction Music Pack Vol 1/.../Deepness of Space [Ambient Loop].wav  [as is]" >> "$MAN"
echo "Music/Ambush_Combat.wav  <-  gamedevmarket assets/Sci Fi Fantasy Music/.../Action - Battle Ensued LOOP.wav  [as is]" >> "$MAN"
echo "staged:"; find "$OUT" -name "*.wav" | wc -l
