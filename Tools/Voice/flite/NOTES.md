# bctts: CMU Flite TTS as a small native library

## Sources and tools
- Flite: https://github.com/festvox/flite, tag `v2.2` (the only release tag), commit
  `e9e2e37c329dbe98bfeb27a1828ef9a71fa84f88` (2020-08-12). Flite source files are unmodified.
- Voice `cmu_us_kal16` (diphone, 16 kHz). Front end `lang/usenglish`, lexicon `lang/cmulex`. Both are compiled into the library, so no voice or data files are needed at runtime.
- zig 0.14.1 for Windows x86_64:
  https://ziglang.org/download/0.14.1/zig-x86_64-windows-0.14.1.zip
  sha256 `554f5378228923ffd558eac35e21af020c73789d87afeabf4bfd16f2e6feed2c` (matches ziglang.org/download/index.json).
  Unpacked into `scratchpad\flite\tools\zig\`. Nothing was installed system-wide.
- Linux: gcc 13.3.0 (Ubuntu 24.04) on storm@192.168.1.61, in `~/flite_build`.

## Files
| file | purpose |
|---|---|
| bctts.c | the wrapper (3 exported functions) |
| sources.sh | Flite source list, include dirs and defines, used by both build scripts |
| build_win.sh | Windows DLL build with `zig cc -target x86_64-windows-gnu` |
| build_linux.sh | Linux .so build with gcc (set `CC=...` to use another compiler) |
| test_tts.py | ctypes test that works with either library. It writes a WAV and prints timing and RMS. |
| pe_info.py | lists a DLL's exports and imports (pure Python) |
| bctts.dll, libbctts.so | the built libraries |
| test_windows.wav, test_linux.wav | test output |
| COPYING.flite | Flite licence (BSD-like, from the v2.2 tree) |

Neither build uses autoconf or a config header. Both compile the same 93 Flite .c files plus bctts.c in one pass:
- `src/{audio,cg,hrg,lexicon,regex,speech,stats,synth,utils,wavesynth}`
- `lang/usenglish`, `lang/cmulex` and `lang/cmu_us_kal16`

The build uses `-DCST_AUDIO_NONE -DCST_NO_SOCKETS`, `au_none.c`, `cst_mmap_none.c` and `cst_file_stdio.c`. So there is no audio device code, no sockets and no mmap.

## Build commands
Windows, run in Git Bash on the laptop from `scratchpad/flite`:
```
sh out/build_win.sh "$PWD/tools/zig/zig-x86_64-windows-0.14.1/zig.exe" src/flite out/bctts.c build
```
This runs, for each source file:
`zig cc -target x86_64-windows-gnu -O2 -ffunction-sections -fdata-sections -w -DCST_AUDIO_NONE -DCST_NO_SOCKETS -Iinclude -Isrc/audio -Isrc/regex -Ilang/usenglish -Ilang/cmulex -Ilang/cmu_us_kal16 -c X.c -o X.o`

The link step is:
`zig cc -target x86_64-windows-gnu -shared -o bctts.dll *.o -Wl,--gc-sections -s`

zig caches are kept in `build/zig-cache-*` through ZIG_GLOBAL_CACHE_DIR and ZIG_LOCAL_CACHE_DIR. A build takes about 10–45 s.

Linux, run on .61 from `~/flite_build`:
```
sh ./build_linux.sh flite bctts.c build
```
This runs, for each source file:
`gcc -O2 -fPIC -fvisibility=hidden -ffunction-sections -fdata-sections -w -DCST_AUDIO_NONE -DCST_NO_SOCKETS <same -I> -c`

The link step is:
`gcc -shared -o libbctts.so *.o -Wl,--gc-sections -Wl,-soname,libbctts.so -s -lm`

A build takes about 10 s with 4 jobs.

Tests:
```
python -I out/test_tts.py build/bctts.dll build/test_windows.wav     # laptop
python3 -I test_tts.py build/libbctts.so test_linux.wav              # .61
```

## API (C ABI)
```c
int  bctts_init(void);                                         // 1 = ok; idempotent
int  bctts_speak(const char *utf8, short **out_samples, int *out_rate); // n samples, 0 = fail
void bctts_free(short *p);                                     // free the buffer from bctts_speak
```
- The output is mono 16-bit PCM. `*out_rate` is 16000.
- `bctts_speak` calls `bctts_init` itself if needed.
- It returns 0 for NULL or empty text.
- Flite's `cst_error()` normally calls `exit()`. The wrapper sets `cst_errjmp` and uses `setjmp`, so an internal Flite error makes the call return 0 and does not kill the game. A little memory may leak in that case.
- The library is not re-entrant, because Flite uses globals. Calls from one worker thread are fine. The test runs 20 calls on a Python worker thread. Do not call it from two threads at the same time.
- Possible C# binding: `[DllImport("bctts")] static extern int bctts_speak(byte[] utf8z, out IntPtr samples, out int rate);`. Pass the NUL-terminated UTF-8 bytes, copy the samples with `Marshal.Copy` into a `short[]`, then call `bctts_free`.

## Exports and dependencies
**bctts.dll** (PE32+, x86-64, checked with pe_info.py):
- Exports: `bctts_free`, `bctts_init` and `bctts_speak`. There are also three harmless Flite data symbols: `flite_lang_list`, `flite_lang_list_length` and `flite_voice_list`. `flite.h` marks them `__declspec(dllexport)` under `WIN32`, which windows.h always defines (see Problems).
- Imports: `KERNEL32.dll` and the Universal CRT API sets `api-ms-win-crt-{convert,heap,private,runtime,stdio,string,utility}-l1-1-0.dll`. The UCRT is part of Windows 10 and 11, and Windows 7/8.1 get it through Windows Update KB2999226.
- No mingw runtime DLL is needed (no libgcc_s, libwinpthread or msvcrt). zig links the mingw-w64 startup code and compiler-rt statically.

**libbctts.so** (ELF x86-64, stripped):
- Dynamic exports: only `bctts_free`, `bctts_init` and `bctts_speak`, because of `-fvisibility=hidden` (checked with `nm -D --defined-only`).
- NEEDED: `libm.so.6` and `libc.so.6`. The newest symbol version it requires is GLIBC_2.14, so it works on any reasonably modern distro.

## Sizes
| file | bytes |
|---|---|
| bctts.dll | 4,729,856 (4.5 MiB), sha256 6b59bc55…988341 |
| libbctts.so | 4,848,104 (4.6 MiB), sha256 4e763155…88c131 |
| each test WAV | 132,906 |

Most of the size is voice data (kal16 LPC/residual tables) and the cmulex lexicon. The code is small.

## Test results
Text: "Wingman here. Mining the Outer Drift, thirty four tonnes aboard."

| | Windows (laptop, bctts.dll) | Linux (.61, libbctts.so) |
|---|---|---|
| samples / rate | 66,431 @ 16 kHz | 66,431 @ 16 kHz |
| audio length | 4.152 s | 4.152 s |
| first speak call | 1.9–3.4 ms (RTF 0.0005–0.0008) | 3.5–11.5 ms (RTF 0.0008–0.0028) |
| best of 4 repeats | 1.4–1.5 ms (RTF 0.0004, ~2800x real time) | 2.8–4.4 ms (RTF 0.0007–0.0011, ~1000–1500x) |
| bctts_init | < 0.1 ms | < 0.1 ms |
| RMS / peak | 1159.7 / 9661 | 1159.7 / 9661 |

- The two WAVs are byte-identical (sha256 14f9ef4c…7772).
- The test also covered:
  - empty and NULL text, which return 0;
  - a UTF-8 string with accents, an em dash and numbers, which gave 54,498 samples;
  - 20 calls on a worker thread.
- In 2,000 repeated calls on Linux, maxrss stayed flat at 15 MB, so there is no leak.

## Problems hit
1. **`c99_snprintf` undefined (Windows link).** Under `_WIN32`, `include/cst_file.h` defines `c99_snprintf` and `c99_vsnprintf` as plain `__inline` functions, which is an MSVC idiom. Under C99 inline rules clang emits no out-of-line copy, so the link failed. The fix is in bctts.c, inside `#ifdef _WIN32`: two `extern` declarations make that file emit the single external definition. Flite itself was not modified.
2. **Extra DLL exports.** `flite.h` defines `GLOBALVARDEF __declspec(dllexport)` under `WIN32`. `-UWIN32` does not help, because mingw's `minwindef.h` (pulled in through windows.h) defines it again. A .def file only adds exports, and zig's linker rejects `--exclude-symbols`. These three exports are data symbols and do no harm. Removing them would mean patching flite.h.
3. zig has no objdump, so the PE exports and imports were checked with the small `pe_info.py` parser instead.
4. Neither library was loaded in Unity, as instructed. Only Python ctypes tests were run.
