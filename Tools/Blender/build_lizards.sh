#!/bin/sh
# Rebuild the Vrask ships' models (the four lizardman frigates) from the raw GLBs in "ai models"
# (Git Bash / POSIX sh, Blender 5.2, headless), into the game:
#
#   sh Tools/Blender/build_lizards.sh               Assets/Carrier/Models/Lizard_Frigate1..4.fbx and
#                                                   _BaseColor.png, Assets/Carrier/Data/lizard_frigateN_hardpoints.json
#   sh Tools/Blender/build_lizards.sh --preview DIR also renders of the turrets cut out of Frigate1 and 3, into DIR
#   sh Tools/Blender/build_lizards.sh Frigate2      only that one (ids as in lizard_spec.json)
#
# Then Tools > Carrier > Build Everything makes the prefabs (CarrierBuilder.Vrask, Data/vrask.json).
# lizard_spec.json holds what was measured on each model (alignment, turret boxes, points); a
# rebuild reproduces the hardpoint files byte for byte. Blender runs one process at a time.
set -e
BLENDER=${BLENDER:-"/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"}
HERE=$(cd "$(dirname "$0")" && pwd)
PROJECT=$(cd "$HERE/../.." && pwd)
AI=${AI:-"$PROJECT/../ai models"}
for a in "$@"; do
    if [ "$prev" = "--preview" ]; then mkdir -p "$a"; fi
    prev=$a
done
"$BLENDER" -b --factory-startup --python "$HERE/convert_lizards.py" -- \
    "$HERE/lizard_spec.json" "$AI" "$PROJECT/Assets/Carrier/Models" "$PROJECT/Assets/Carrier/Data" "$@"
