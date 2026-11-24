"""Load bctts (DLL or .so) via ctypes, synthesise a test line, write a WAV, report timing.
Usage: python test_tts.py <path-to-library> <out.wav>"""
import ctypes, sys, time, wave, array, math, os

TEXT = "Wingman here. Mining the Outer Drift, thirty four tonnes aboard."

def main():
    lib_path, wav_path = sys.argv[1], sys.argv[2]
    lib = ctypes.CDLL(os.path.abspath(lib_path))
    lib.bctts_init.restype = ctypes.c_int
    lib.bctts_init.argtypes = []
    lib.bctts_speak.restype = ctypes.c_int
    lib.bctts_speak.argtypes = [ctypes.c_char_p, ctypes.POINTER(ctypes.POINTER(ctypes.c_short)),
                                ctypes.POINTER(ctypes.c_int)]
    lib.bctts_free.restype = None
    lib.bctts_free.argtypes = [ctypes.POINTER(ctypes.c_short)]

    t0 = time.perf_counter()
    ok = lib.bctts_init()
    t_init = time.perf_counter() - t0
    assert ok == 1, "bctts_init failed"

    buf = ctypes.POINTER(ctypes.c_short)()
    rate = ctypes.c_int(0)
    times = []
    n = 0
    for i in range(5):  # first call + 4 repeats
        t0 = time.perf_counter()
        n = lib.bctts_speak(TEXT.encode("utf-8"), ctypes.byref(buf), ctypes.byref(rate))
        times.append(time.perf_counter() - t0)
        assert n > 0, "bctts_speak returned 0"
        if i < 4:
            lib.bctts_free(buf)
    samples = array.array("h", ctypes.string_at(buf, n * 2))
    lib.bctts_free(buf)

    # error/edge cases must not crash
    assert lib.bctts_speak(b"", ctypes.byref(buf), ctypes.byref(rate)) == 0
    assert lib.bctts_speak(None, ctypes.byref(buf), ctypes.byref(rate)) == 0
    n2 = lib.bctts_speak("Café naïve — 123 km/h!".encode("utf-8"), ctypes.byref(buf), ctypes.byref(rate))
    if n2 > 0:
        lib.bctts_free(buf)

    # speak from a (single) worker thread, as the game will
    import threading
    res = {}
    def worker():
        b = ctypes.POINTER(ctypes.c_short)(); r = ctypes.c_int(0)
        for _ in range(20):
            res["n"] = lib.bctts_speak(TEXT.encode("utf-8"), ctypes.byref(b), ctypes.byref(r))
            lib.bctts_free(b)
    th = threading.Thread(target=worker); th.start(); th.join()
    assert res["n"] == n, "worker-thread result differs"

    with wave.open(wav_path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(rate.value)
        w.writeframes(samples.tobytes())

    # verify by reading the WAV back
    with wave.open(wav_path, "rb") as w:
        sr, frames = w.getframerate(), w.getnframes()
        data = array.array("h", w.readframes(frames))
    rms = math.sqrt(sum(s * s for s in data) / len(data))
    peak = max(abs(s) for s in data)
    dur = frames / sr
    best = min(times[1:])
    print(f"library      : {lib_path} ({os.path.getsize(lib_path)} bytes)")
    print(f"init         : {t_init*1000:.1f} ms")
    print(f"samples      : {n} @ {sr} Hz -> {dur:.3f} s audio")
    print(f"synth first  : {times[0]*1000:.1f} ms  (RTF {times[0]/dur:.4f})")
    print(f"synth best/4 : {best*1000:.1f} ms  (RTF {best/dur:.4f}, {dur/best:.0f}x real time)")
    print(f"rms          : {rms:.1f}   peak: {peak}")
    print(f"utf8 extra   : {n2} samples; worker thread x20: {res['n']} samples each")
    assert sr == 16000 and 2.0 < dur < 8.0 and rms > 300, "WAV looks wrong"
    print("OK", wav_path)

if __name__ == "__main__":
    main()
