#!/bin/sh
# Rebuild the Empire ships' models (the four imperial frigates) from the raw GLBs in "ai models"
# (Git Bash / POSIX sh, Blender 5.2, headless), into the game:
#
#   sh Tools/Blender/build_empire.sh                Assets/Carrier/Models/Empire_Assault|Patrol|Raider|Recon.fbx
#                                                   and _BaseColor.png, Assets/Carrier/Data/empire_<id>_hardpoints.json
#   sh Tools/Blender/build_empire.sh --preview DIR  also renders of the turrets cut out of each, into DIR
#   sh Tools/Blender/build_empire.sh Patrol         only that one (ids as in empire_spec.json)
#
# empire_spec.json holds what was measured on each model (alignment, turret boxes, points), with
# convert_lizards.py doing the work; align_grid.py --marks renders the spec over a model to check it.
# A rebuild reproduces the hardpoint files byte for byte. Blender runs one process at a time.
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
    "$HERE/empire_spec.json" "$AI" "$PROJECT/Assets/Carrier/Models" "$PROJECT/Assets/Carrier/Data" "$@"
