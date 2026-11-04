#!/bin/bash
# Gamepad check for the web build, in headless Chrome with a stand-in Xbox 360 controller
# (fakepad.js). Renders in software (SwiftShader), so the GPU isn't touched.
#
#   Tools/WebTest/padtest.sh <standard|linux> [build dir] [seconds]
#
# standard: the controller as Chrome on Windows reports it (W3C standard mapping).
# linux:    reported raw, as Firefox on Linux does (the xpad driver); the page's xbox360.js
#           should present it to the game as a standard gamepad.
# Prints the [PadTest] (what the game read), [FakePad] (what was pressed) and [Pad] lines.
set -e
KIND=${1:-standard}
HERE=$(cd "$(dirname "$0")" && pwd)
BUILD=${2:-$HERE/../../../Builds/StarSystemWeb}
SECS=${3:-75}
CHROME=${CHROME:-"/c/Program Files/Google/Chrome/Application/chrome.exe"}
WORK=$(mktemp -d)
cp -r "$BUILD/." "$WORK/"
cp "$HERE/fakepad.js" "$WORK/fakepad.js"
# the stand-in controller goes in before xbox360.js (as the browser's own pads would be)
sed -i 's|<script src="xbox360.js"></script>|<script src="fakepad.js"></script>\n  <script src="xbox360.js"></script>|' "$WORK/index.html"
grep -q fakepad.js "$WORK/index.html" || { echo "no xbox360.js script in the page"; exit 1; }
(cd "$WORK" && exec python -m http.server 8767 --bind 127.0.0.1 > /dev/null 2>&1) &
SERVER=$!
sleep 2
set +e
timeout "$SECS" "$CHROME" --headless=new --no-first-run --no-default-browser-check --user-data-dir="$(cygpath -w "$WORK/profile" 2>/dev/null || echo "$WORK/profile")" \
  --window-size=1280,720 --enable-logging=stderr --v=0 --autoplay-policy=no-user-gesture-required \
  --enable-unsafe-swiftshader --use-gl=angle --use-angle=swiftshader --disable-features=WebGPU \
  "http://localhost:8767/index.html?padtest&fakepad=$KIND" 2> "$WORK/chrome.log"
kill $SERVER 2> /dev/null
grep -a -o '\[\(PadTest\|FakePad\|Pad\)\][^"]*' "$WORK/chrome.log"
