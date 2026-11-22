#!/bin/sh
# Rebuild the Greys ships' models (the four Greys frigates) from the raw GLBs in "ai models"
# (Git Bash / POSIX sh, Blender 5.2, headless), into the game:
#
#   sh Tools/Blender/build_greys.sh                 Assets/Carrier/Models/Greys_Scout|Shroud|Lance|Reaper.fbx
#                                                   and _BaseColor.png, Assets/Carrier/Data/greys_<id>_hardpoints.json
#   sh Tools/Blender/build_greys.sh --preview DIR   also renders of any turrets cut out, into DIR (the Greys
#                                                   have none: check them with align_grid.py --marks)
#   sh Tools/Blender/build_greys.sh Lance           only that one (ids as in greys_spec.json)
#
# greys_spec.json holds what was measured on each model (alignment, guns, points), with
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
    "$HERE/greys_spec.json" "$AI" "$PROJECT/Assets/Carrier/Models" "$PROJECT/Assets/Carrier/Data" "$@"
