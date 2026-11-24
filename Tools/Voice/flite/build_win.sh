#!/bin/sh
# Cross-build bctts.dll (Windows x64) from Flite v2.2 + bctts.c with zig cc.
# Usage: build_win.sh <zig.exe> <flite_src_dir> <bctts.c> <out_dir>
# Runs in Git Bash on Windows (or any POSIX sh with a zig binary).
set -eu
ZIG=$1
FLITE=$(cd "$2" && pwd); BCTTS=$(cd "$(dirname "$3")" && pwd)/$(basename "$3"); OUT=$(mkdir -p "$4" && cd "$4" && pwd)
HERE=$(cd "$(dirname "$0")" && pwd)
. "$HERE/sources.sh"
OBJ="$OUT/obj_win"; mkdir -p "$OBJ"
# keep zig's caches next to the build instead of %LOCALAPPDATA%
export ZIG_GLOBAL_CACHE_DIR="$OUT/zig-cache-global" ZIG_LOCAL_CACHE_DIR="$OUT/zig-cache-local"
TARGET="-target x86_64-windows-gnu"
CFLAGS="$TARGET -O2 -ffunction-sections -fdata-sections -w $FLITE_DEFS"
cd "$FLITE"
objs=""
for s in $FLITE_SRCS "$BCTTS"; do
  o="$OBJ/$(echo "$s" | sed 's#^/##; s#^[A-Za-z]:##; s#[/.:]#_#g').o"; objs="$objs $o"
  printf '%s %s\n' "$s" "$o"
done > "$OBJ/list.txt"
export ZIG CFLAGS FLITE_INCS
xargs -P 4 -n 2 sh -c '"$ZIG" cc $CFLAGS $FLITE_INCS -c "$0" -o "$1" || exit 255' < "$OBJ/list.txt"
"$ZIG" cc $TARGET -shared -o "$OUT/bctts.dll" $objs -Wl,--gc-sections -s
ls -l "$OUT/bctts.dll"
