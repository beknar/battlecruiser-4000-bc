#!/bin/bash
# Gamepad check for the web build, in headless Chrome with a stand-in Xbox 360 controller
# (fakepad.js). Renders in software (SwiftShader), so the GPU isn't touched.
#
#   Tools/WebTest/padtest.sh [kind] [build dir] [seconds]
#
# kind: standard (Chrome on Windows), linux, linuxwireless, thirdparty (Firefox on Linux),
#       mac (Firefox on macOS); see fakepad.js. Default: all of them, one after another.
# Prints what was pressed ([FakePad]) and what the game read ([PadTest]), then checks that
# every control came through and that R3 / B open and close the map and Start / B the pause
# menu. Exits 1 if anything is missing.
HERE=$(cd "$(dirname "$0")" && pwd)
KIND=${1:-all}
BUILD=${2:-$HERE/../../../Builds/StarSystemWeb}
SECS=${3:-75}
PORT=8767
CHROME=${CHROME:-"/c/Program Files/Google/Chrome/Application/chrome.exe"}

if [ "$KIND" = all ]; then
  status=0
  for k in standard linux linuxwireless thirdparty mac; do
    echo "===== $k"
    "$0" "$k" "$BUILD" "$SECS" || status=1
  done
  exit $status
fi

if (echo > /dev/tcp/127.0.0.1/$PORT) 2> /dev/null; then
  echo "port $PORT is already in use (a server left over from another run?)"; exit 1
fi

WORK=$(mktemp -d)
SERVER=
cleanup() { [ -n "$SERVER" ] && kill "$SERVER" 2> /dev/null; rm -rf "$WORK"; }
trap cleanup EXIT INT TERM

cp -r "$BUILD/." "$WORK/"
cp "$HERE/fakepad.js" "$WORK/fakepad.js"
# the stand-in controller goes in before xbox360.js (as the browser's own pads would be)
sed -i 's|<script src="xbox360.js"></script>|<script src="fakepad.js"></script>\n  <script src="xbox360.js"></script>|' "$WORK/index.html"
grep -q fakepad.js "$WORK/index.html" || { echo "no xbox360.js script in the page"; exit 1; }
(cd "$WORK" && exec python -m http.server $PORT --bind 127.0.0.1 > "$WORK/server.log" 2>&1) &
SERVER=$!
sleep 2
kill -0 "$SERVER" 2> /dev/null || { echo "the web server didn't start:"; cat "$WORK/server.log"; exit 1; }

PROFILE=$(cygpath -w "$WORK/profile" 2> /dev/null || echo "$WORK/profile")
timeout "$SECS" "$CHROME" --headless=new --no-first-run --no-default-browser-check --user-data-dir="$PROFILE" \
  --window-size=1280,720 --enable-logging=stderr --v=0 --autoplay-policy=no-user-gesture-required \
  --enable-unsafe-swiftshader --use-gl=angle --use-angle=swiftshader --disable-features=WebGPU \
  "http://localhost:$PORT/index.html?padtest&fakepad=$KIND" 2> "$WORK/chrome.log"
grep -a -o '\[\(PadTest\|FakePad\|Pad\)\][^"]*' "$WORK/chrome.log" > "$WORK/pad.log"
cat "$WORK/pad.log"

missing=0
expect() { grep -qF -- "$1" "$WORK/pad.log" || { echo "MISSING: $1"; missing=1; }; }
expect "[PadTest] gamepad: layout WebGLGamepad"
for c in buttonSouth buttonWest buttonNorth leftShoulder rightShoulder select leftStickPress \
         dpad/up dpad/down dpad/left dpad/right rightStickPress buttonEast start; do
  expect "[PadTest] pressed $c"
done
expect "[PadTest] left trigger 1.00"; expect "[PadTest] left trigger released"
expect "[PadTest] right trigger 1.00"; expect "[PadTest] right trigger released"
expect "[PadTest] left stick right"; expect "[PadTest] left stick up"
expect "[PadTest] right stick left"; expect "[PadTest] right stick down"
expect "[PadTest] system map opened"; expect "[PadTest] system map closed"
expect "[PadTest] pause menu opened"; expect "[PadTest] pause menu closed"
expect "[FakePad] rumble dual-rumble"
expect "[FakePad] done"
if grep -qF "[PadTest] FAIL" "$WORK/pad.log"; then missing=1; fi
[ $missing = 0 ] && echo "PASS: $KIND" || echo "FAIL: $KIND"
exit $missing
