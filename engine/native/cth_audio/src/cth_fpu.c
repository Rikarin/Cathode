/* SPDX-License-Identifier: GPL-3.0-or-later */
#include "cth_audio.h"

#if defined(__x86_64__) || defined(_M_X64) || defined(__i386__) || defined(_M_IX86)
#include <xmmintrin.h>
#endif

void cth_fpu_flush_denormals(void)
{
#if defined(__x86_64__) || defined(_M_X64) || defined(__i386__) || defined(_M_IX86)
    /* FTZ (bit 15) and DAZ (bit 6). */
    _mm_setcsr(_mm_getcsr() | 0x8040u);
#elif defined(__aarch64__)
    uint64_t fpcr;
    __asm__ volatile("mrs %0, fpcr" : "=r"(fpcr));
    fpcr |= (1ull << 24); /* FZ */
    __asm__ volatile("msr fpcr, %0" : : "r"(fpcr));
#endif
}
