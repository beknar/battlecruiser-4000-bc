/* bctts.c - minimal C ABI wrapper around CMU Flite (cmu_us_kal16 voice).
 *
 *   int  bctts_init(void);                                   1 = ok
 *   int  bctts_speak(const char *utf8, short **out, int *rate);  sample count, 0 = fail
 *   void bctts_free(short *p);
 *
 * Output is mono signed 16-bit PCM at *rate Hz (16000 for kal16).
 * Not re-entrant: call bctts_speak from one thread at a time (Flite keeps
 * some global state, e.g. the error jump buffer).
 */
#include <stdlib.h>
#include <string.h>
#include <setjmp.h>
#include "flite.h"

#if defined(_WIN32)
#  define BCTTS_API __declspec(dllexport)
/* Flite's cst_file.h defines c99_snprintf/c99_vsnprintf as plain __inline
   functions on _WIN32 (an MSVC idiom). Under C99 inline rules clang/gcc emit
   no out-of-line copy, so provide the one external definition here. */
extern int c99_vsnprintf(char *str, size_t size, const char *format, va_list ap);
extern int c99_snprintf(char *str, size_t size, const char *format, ...);
#else
#  define BCTTS_API __attribute__((visibility("default")))
#endif

cst_voice *register_cmu_us_kal16(const char *voxdir);

static cst_voice *g_voice = NULL;

BCTTS_API int bctts_init(void)
{
    if (g_voice) return 1;
    flite_init();
    g_voice = register_cmu_us_kal16(NULL);
    return g_voice ? 1 : 0;
}

BCTTS_API int bctts_speak(const char *utf8, short **out_samples, int *out_rate)
{
    jmp_buf jb;
    cst_wave *volatile w = NULL;
    short *buf;
    int n;

    if (out_samples) *out_samples = NULL;
    if (out_rate) *out_rate = 0;
    if (!utf8 || !out_samples || !*utf8 || !bctts_init()) return 0;

    /* Flite's cst_error() calls exit() unless cst_errjmp is set; never let
       it take the host process down. (On error some memory may leak.) */
    cst_errjmp = &jb;
    if (setjmp(jb) != 0) {
        cst_errjmp = NULL;
        return 0;
    }
    w = flite_text_to_wave(utf8, g_voice);
    cst_errjmp = NULL;

    if (!w) return 0;
    n = cst_wave_num_samples(w);
    if (n <= 0 || cst_wave_num_channels(w) != 1 || !w->samples) {
        delete_wave(w);
        return 0;
    }
    buf = (short *)malloc((size_t)n * sizeof(short));
    if (!buf) { delete_wave(w); return 0; }
    memcpy(buf, w->samples, (size_t)n * sizeof(short));
    *out_samples = buf;
    if (out_rate) *out_rate = cst_wave_sample_rate(w);
    delete_wave(w);
    return n;
}

BCTTS_API void bctts_free(short *p)
{
    free(p);
}
