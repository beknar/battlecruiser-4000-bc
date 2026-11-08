#!/bin/bash
# Co-op check for the web build: a local co-op server, the scripted second player
# (Tools/CoopTest/bot.py, which joins first and hosts), and the web build in headless Chrome
# running the co-op self-test (?systemtest&coopquick&server=...&connect). Renders in software
# (SwiftShader, so WebGL 2), so the GPU isn't touched; the connection is the same WebSocket code
# under WebGPU.
#
#   Tools/WebTest/cooptest.sh [build dir] [seconds]
#
# COOPSERVER: the server program (default ../Builds/CoopServer/win-x64/CoopServer.exe)
# PYTHON: a Python with the 'websockets' package (default: python)
# Prints the self-test's lines and exits 1 unless it passes.
HERE=$(cd "$(dirname "$0")" && pwd)
BUILD=${1:-$HERE/../../../Builds/StarSystemWeb}
SECS=${2:-300}
PORT=8768
COOP=7793
CHROME=${CHROME:-"/c/Program Files/Google/Chrome/Application/chrome.exe"}
COOPSERVER=${COOPSERVER:-$HERE/../../../Builds/CoopServer/win-x64/CoopServer.exe}
PYTHON=${PYTHON:-python}

for p in $PORT $COOP; do
  if (echo > /dev/tcp/127.0.0.1/$p) 2> /dev/null; then
    echo "port $p is already in use (a server left over from another run?)"; exit 1
  fi
done

WORK=$(mktemp -d)
WEB= SRV= BOT= CHR= PROFILE=
cleanup() {
  for p in $CHR $BOT $SRV $WEB; do kill "$p" 2> /dev/null; done
  # headless Chrome's helper processes (only this run's: they use its own profile folder)
  if command -v powershell.exe > /dev/null && [ -n "$PROFILE" ]; then
    powershell.exe -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='chrome.exe'\" | Where-Object { \$_.CommandLine -like '*$PROFILE*' } | ForEach-Object { Stop-Process -Id \$_.ProcessId -Force -ErrorAction SilentlyContinue }" > /dev/null 2>&1
  fi
  wait 2> /dev/null
  rm -rf "$WORK"
}
trap cleanup EXIT INT TERM

mkdir -p "$WORK/site" && cp -r "$BUILD/." "$WORK/site/"
(cd "$WORK/site" && exec "$PYTHON" -m http.server $PORT --bind 127.0.0.1 > "$WORK/web.log" 2>&1) &
WEB=$!
"$COOPSERVER" --bind 127.0.0.1 --port $COOP < /dev/null > "$WORK/coop.log" 2>&1 &
SRV=$!
sleep 2
kill -0 "$WEB" 2> /dev/null || { echo "the web server didn't start:"; cat "$WORK/web.log"; exit 1; }
kill -0 "$SRV" 2> /dev/null || { echo "the co-op server didn't start:"; cat "$WORK/coop.log"; exit 1; }
"$PYTHON" -u "$HERE/../CoopTest/bot.py" "ws://127.0.0.1:$COOP/coop" StarSystem > "$WORK/bot.log" 2>&1 &
BOT=$!
sleep 2

PROFILE=$(cygpath -w "$WORK/profile" 2> /dev/null || echo "$WORK/profile")
"$CHROME" --headless=new --no-first-run --no-default-browser-check --user-data-dir="$PROFILE" \
  --window-size=1280,720 --enable-logging=stderr --v=0 --autoplay-policy=no-user-gesture-required \
  --enable-unsafe-swiftshader --use-gl=angle --use-angle=swiftshader --disable-features=WebGPU \
  "http://localhost:$PORT/index.html?systemtest&coopquick&server=127.0.0.1:$COOP&name=WebTester&connect" 2> "$WORK/chrome.log" &
CHR=$!

# the web game doesn't quit when the test is done: wait for its verdict (or the time limit)
end=$((SECONDS + SECS))
while [ $SECONDS -lt $end ] && ! grep -a -q -E "(PASS|FAIL): co-op" "$WORK/chrome.log"; do sleep 2; done
sleep 1
grep -a -o '\[SystemTest\][^"]*' "$WORK/chrome.log" | sed 's/\\n$//' > "$WORK/test.log"
if [ ! -s "$WORK/test.log" ]; then grep -a -o -E '(connected to|  bot:|    |PASS|FAIL)[^"]*' "$WORK/chrome.log" > "$WORK/test.log"; fi
cat "$WORK/test.log"
echo "----- bot"; tail -6 "$WORK/bot.log"
if grep -q "PASS: co-op" "$WORK/test.log"; then echo "PASS: web co-op"; exit 0; fi
echo "FAIL: web co-op (no pass within $SECS s)"; exit 1
