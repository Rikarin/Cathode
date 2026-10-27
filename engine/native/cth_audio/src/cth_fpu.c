/* SPDX-License-Identifier: GPL-3.0-or-later */
#include "cth_audio.h"

#if defined(__x86_64__) || defined(_M_X64) || defined(__i386__) || defined(_M_IX86)
#include <xmmintrin.h>
#elif defined(_M_ARM64)
#include <intrin.h> /* _ReadStatusReg/_WriteStatusReg and ARM64_FPCR (arm64intr.h) */
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
#elif defined(_M_ARM64)
    /* MSVC on ARM64 defines _M_ARM64, not __aarch64__, and has no inline assembly. */
    _WriteStatusReg(ARM64_FPCR, _ReadStatusReg(ARM64_FPCR) | (1ll << 24)); /* FZ */
#else
/* Other architectures keep the per-block flushing only (ADR-0002 finding 4). */
#endif
}
