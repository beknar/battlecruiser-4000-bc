#!/bin/bash
# Quit check for the web build, in headless Chrome (software rendering, so no GPU is touched):
# the game is played for a few seconds with a stand-in mouse moving and clicking over it
# (fakemouse.js), then quits through the pause menu's Quit (?quittest). Passes if the page left
# for its "ended" page, the console never showed the engine's "memory access out of bounds" or
# "null function", and a file the game wrote just before the quit was still there when the page
# was opened again (?quittestcheck).
#
#   Tools/WebTest/quittest.sh [build dir] [seconds]
#
# CHROME: the browser (default: Chrome on Windows). HEADLESS: its headless flag (default
# --headless=new; the chrome-headless-shell needs none, so pass HEADLESS=" "). CHROME_EXTRA: more
# flags (e.g. --no-sandbox, for Chrome as a user on Ubuntu, which blocks its sandbox). QUIT_FROM=page
# quits from the page instead (?fakequit), for a build too old to have ?quittest; it then skips
# the save check. KEEP=dir keeps the browser's logs there. AFTER: seconds to watch on after the
# quit (default 15).
HERE=$(cd "$(dirname "$0")" && pwd)
BUILD=${1:-$HERE/../../../Builds/StarSystemWeb}
SECS=${2:-420}   # (software rendering on a small machine takes minutes to start the engine)
PORT=${PORT:-8769}
CHROME=${CHROME:-"/c/Program Files/Google/Chrome/Application/chrome.exe"}
HEADLESS=${HEADLESS:---headless=new}
CHROME_EXTRA=${CHROME_EXTRA:-}
QUIT_FROM=${QUIT_FROM:-game}

if (echo > /dev/tcp/127.0.0.1/$PORT) 2> /dev/null; then
  echo "port $PORT is already in use (a server left over from another run?)"; exit 1
fi

WORK=$(mktemp -d)
SERVER=
cleanup() { [ -n "$SERVER" ] && kill "$SERVER" 2> /dev/null; [ -n "$KEEP" ] && cp "$WORK"/*.log "$KEEP"/ 2> /dev/null; rm -rf "$WORK"; }
trap cleanup EXIT INT TERM

mkdir "$WORK/site"
cp -r "$BUILD/." "$WORK/site/"
cp "$HERE/fakemouse.js" "$WORK/site/fakemouse.js"
sed -i 's|<script src="xbox360.js"></script>|<script src="fakemouse.js"></script>\n  <script src="xbox360.js"></script>|' "$WORK/site/index.html"
grep -q fakemouse.js "$WORK/site/index.html" || { echo "no xbox360.js script in the page"; exit 1; }
PY=$(command -v python3 || command -v python)
(cd "$WORK/site" && exec "$PY" -m http.server $PORT --bind 127.0.0.1 > "$WORK/server.log" 2>&1) &
SERVER=$!
sleep 2
kill -0 "$SERVER" 2> /dev/null || { echo "the web server didn't start:"; cat "$WORK/server.log"; exit 1; }

PROFILE=$(cygpath -w "$WORK/profile" 2> /dev/null || echo "$WORK/profile")
chrome() {   # chrome <query> <seconds> <log> <pattern>: stops 15 s after <pattern> is logged
  timeout "$2" "$CHROME" $HEADLESS $CHROME_EXTRA --no-first-run --no-default-browser-check --user-data-dir="$PROFILE"     --window-size=1280,720 --enable-logging=stderr --v=0 --autoplay-policy=no-user-gesture-required     --enable-unsafe-swiftshader --use-gl=angle --use-angle=swiftshader --disable-features=WebGPU     "http://localhost:$PORT/index.html?$1" 2> "$3" &
  local pid=$!
  while kill -0 $pid 2> /dev/null; do
    if grep -aqF -- "$4" "$3"; then sleep "${AFTER:-15}"; kill $pid 2> /dev/null; break; fi
    sleep 1
  done
  wait $pid 2> /dev/null
}
TOKEN=$RANDOM$RANDOM
if [ "$QUIT_FROM" = page ]; then chrome "fakequit=15" "$SECS" "$WORK/play.log" "[FakeMouse] quitting from the page"
else chrome "quittest=$TOKEN" "$SECS" "$WORK/play.log" "[Page] ended"; fi
grep -a -o '\[\(QuitTest\|Page\|FakeMouse\)\][^"]*' "$WORK/play.log"

fail=0
expect() { grep -aqF -- "$1" "$2" || { echo "MISSING: $1"; fail=1; }; }
[ "$QUIT_FROM" = page ] || { expect "[QuitTest] wrote $TOKEN" "$WORK/play.log"; expect "[Page] quit" "$WORK/play.log"; }
expect "[Page] ended" "$WORK/play.log"
if grep -aiE "memory access out of bounds|null function|RuntimeError" "$WORK/play.log" | head -5 | grep .; then
  echo "ERRORS in the console (above)"; fail=1
fi
if [ "$QUIT_FROM" != page ]; then
  chrome "quittestcheck=$TOKEN" "$SECS" "$WORK/check.log" "[QuitTest] "
  grep -a -o '\[QuitTest\][^"]*' "$WORK/check.log"
  expect "[QuitTest] found $TOKEN" "$WORK/check.log"
fi
[ $fail = 0 ] && echo "PASS" || echo "FAIL"
exit $fail
