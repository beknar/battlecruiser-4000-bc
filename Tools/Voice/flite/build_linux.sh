#!/bin/sh
# Build libbctts.so (Linux x86_64) from Flite v2.2 + bctts.c, without autoconf.
# Usage: build_linux.sh <flite_src_dir> <bctts.c> <out_dir>   (CC defaults to gcc)
set -eu
FLITE=$(cd "$1" && pwd); BCTTS=$(cd "$(dirname "$2")" && pwd)/$(basename "$2"); OUT=$(mkdir -p "$3" && cd "$3" && pwd)
HERE=$(cd "$(dirname "$0")" && pwd)
. "$HERE/sources.sh"
CC=${CC:-gcc}
OBJ="$OUT/obj_linux"; mkdir -p "$OBJ"
CFLAGS="-O2 -fPIC -fvisibility=hidden -ffunction-sections -fdata-sections -w $FLITE_DEFS"
cd "$FLITE"
objs=""
for s in $FLITE_SRCS "$BCTTS"; do
  o="$OBJ/$(echo "$s" | sed 's#^/##; s#[/.]#_#g').o"; objs="$objs $o"
  printf '%s %s\n' "$s" "$o"
done > "$OBJ/list.txt"
# compile in parallel (4 jobs)
export CC CFLAGS FLITE_INCS
xargs -P 4 -n 2 sh -c '$CC $CFLAGS $FLITE_INCS -c "$0" -o "$1" || exit 255' < "$OBJ/list.txt"
$CC -shared -o "$OUT/libbctts.so" $objs -Wl,--gc-sections -Wl,-soname,libbctts.so -s -lm
ls -l "$OUT/libbctts.so"
