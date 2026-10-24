/*
 * WASAPI backend. Windows splits one interface into a render and a capture endpoint, so a cth device is a render
 * endpoint paired with a capture endpoint of the same hardware (same container id; the default capture endpoint wins
 * when several match). A uid of the form "<render id>|<capture id>" pairs any two explicitly, "<render id>|" opens
 * no input, and a NULL uid opens the default render and capture endpoints.
 *
 * Each endpoint is opened in exclusive event-driven mode when the device allows it (the lowest latency WASAPI has),
 * and otherwise in shared mode: IAudioClient3's small engine periods when the rate matches the mix format, the
 * classic shared stream after that. Setting CTH_WASAPI_SHARED=1 skips exclusive mode, so other applications keep
 * playing. One MMCSS "Pro Audio" thread waits on both endpoint events; capture goes through a small FIFO primed with
 * one period, and each render event pulls the planar callback in chunks of at most one period.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#define COBJMACROS
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <initguid.h> /* defines the GUIDs and property keys declared below, in this file */
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <functiondiscoverykeys_devpkey.h>
#include <ksmedia.h>
#include <avrt.h>
#include <process.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "cth_audio.h"

#define MAX_CHANNELS 64
#define FIFO_FRAMES 16384 /* power of two; capture waiting for the render side */

/* The SDK declares these without defining them in any import library C can use, so they are defined here. */
DEFINE_GUID(CTH_CLSID_MMDeviceEnumerator, 0xbcde0395, 0xe52f, 0x467c, 0x8e, 0x3d, 0xc4, 0x57, 0x92, 0x91, 0x69, 0x2e);
DEFINE_GUID(CTH_IID_IMMDeviceEnumerator, 0xa95664d2, 0x9614, 0x4f35, 0xa7, 0x46, 0xde, 0x8d, 0xb6, 0x36, 0x17, 0xe6);
DEFINE_GUID(CTH_IID_IAudioClient, 0x1cb9ad4c, 0xdbfa, 0x4c32, 0xb1, 0x78, 0xc2, 0xf5, 0x68, 0xa7, 0x03, 0xb2);
DEFINE_GUID(CTH_IID_IAudioClient3, 0x7ed4ee07, 0x8e67, 0x4cd4, 0x8c, 0x1a, 0x2b, 0x7a, 0x59, 0x87, 0xad, 0x42);
DEFINE_GUID(CTH_IID_IAudioRenderClient, 0xf294acfc, 0x3146, 0x4483, 0xa7, 0xbf, 0xad, 0xdc, 0xa7, 0xc2, 0x60, 0xe2);
DEFINE_GUID(CTH_IID_IAudioCaptureClient, 0xc8adbd64, 0xe71e, 0x48a0, 0xa4, 0xde, 0x18, 0x5c, 0x39, 0x5c, 0xd3, 0x17);
DEFINE_GUID(CTH_SUBTYPE_PCM, 0x00000001, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71);
DEFINE_GUID(CTH_SUBTYPE_IEEE_FLOAT, 0x00000003, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71);
DEFINE_PROPERTYKEY(CTH_PKEY_AudioEngine_DeviceFormat, 0xf19f064d, 0x82c, 0x4e27, 0xbc, 0x73, 0x68, 0x82, 0xa1, 0xbb, 0x8e, 0x4c, 0);

typedef enum { SAMPLE_F32, SAMPLE_I32, SAMPLE_I24, SAMPLE_I16 } sample_kind;

typedef struct endpoint {
    IMMDevice *device;
    IAudioClient *client;
    IAudioRenderClient *render;
    IAudioCaptureClient *capture;
    HANDLE event;
    sample_kind kind;
    int32_t channels;    /* device channels in the stream */
    int32_t frame_bytes;
    UINT32 buffer_size;  /* endpoint buffer, frames */
    int32_t period;      /* frames per event */
    int32_t latency;     /* stream latency, frames */
    double rate;
    int exclusive;
} endpoint;

struct cth_stream {
    endpoint out;
    endpoint in; /* in.client is NULL when no input was opened */
    cth_process_fn process;
    void *user;
    int32_t input_channels;
    int32_t output_channels;
    int32_t buffer_frames;
    int32_t input_latency;
    int32_t output_latency;
    volatile LONG64 callbacks;
    volatile LONG64 overloads;
    volatile LONG reset_timing; /* set by the control thread, applied by the audio thread */
    int running;
    double sample_rate;
    double ticks_to_ns;
    HANDLE thread;
    HANDLE stop_event;
    HANDLE started_event;
    HRESULT thread_status;
    int capture_primed;
    cth_stream_timing timing; /* written only by the audio thread */
    float *planar;            /* (inputs + outputs) * CTH_MAX_FRAMES */
    float *fifo;              /* inputs * FIFO_FRAMES, planar; touched only by the audio thread */
    uint32_t fifo_read;
    uint32_t fifo_write;
    const float *in_ptrs[MAX_CHANNELS];
    float *out_ptrs[MAX_CHANNELS];
};

static __declspec(thread) char last_error[256];

static int32_t fail(const char *what, HRESULT status)
{
    snprintf(last_error, sizeof last_error, "%s (HRESULT 0x%08lX)", what, (unsigned long)status);
    return -1;
}

const char *cth_audio_last_error(void) { return last_error; }
const char *cth_audio_backend(void) { return "wasapi"; }

/* ---- COM scaffolding -------------------------------------------------------------------------------------------- */

/* Joins the multithreaded apartment for the duration of a call. A thread already in an STA keeps it; the MMDevice
 * objects used here are free-threaded, so either works. Returns whether com_leave must uninitialise. */
static int com_enter(void)
{
    HRESULT hr = CoInitializeEx(NULL, COINIT_MULTITHREADED);
    return hr == S_OK || hr == S_FALSE;
}

static void com_leave(int entered)
{
    if (entered) CoUninitialize();
}

#define RELEASE(p)                          \
    do {                                    \
        if (p) {                            \
            (p)->lpVtbl->Release(p);        \
            (p) = NULL;                     \
        }                                   \
    } while (0)

static IMMDeviceEnumerator *enumerator(void)
{
    IMMDeviceEnumerator *e = NULL;
    CoCreateInstance(&CTH_CLSID_MMDeviceEnumerator, NULL, CLSCTX_ALL, &CTH_IID_IMMDeviceEnumerator, (void **)&e);
    return e;
}

static void to_utf8(const wchar_t *text, char *out, size_t size)
{
    out[0] = 0;
    if (text && WideCharToMultiByte(CP_UTF8, 0, text, -1, out, (int)size, NULL, NULL) == 0) out[size - 1] = 0;
}

static wchar_t *from_utf8(const char *text, size_t length)
{
    int n = MultiByteToWideChar(CP_UTF8, 0, text, (int)length, NULL, 0);
    wchar_t *wide = (wchar_t *)calloc((size_t)n + 1, sizeof(wchar_t));
    if (wide) MultiByteToWideChar(CP_UTF8, 0, text, (int)length, wide, n);
    return wide;
}

/* ---- Endpoint properties ---------------------------------------------------------------------------------------- */

static void device_id(IMMDevice *device, char *out, size_t size)
{
    LPWSTR id = NULL;
    out[0] = 0;
    if (SUCCEEDED(IMMDevice_GetId(device, &id))) {
        to_utf8(id, out, size);
        CoTaskMemFree(id);
    }
}

static void friendly_name(IMMDevice *device, char *out, size_t size)
{
    IPropertyStore *store = NULL;
    PROPVARIANT value;
    PropVariantInit(&value);
    out[0] = 0;
    if (SUCCEEDED(IMMDevice_OpenPropertyStore(device, STGM_READ, &store))) {
        if (SUCCEEDED(IPropertyStore_GetValue(store, &PKEY_Device_FriendlyName, &value)) && value.vt == VT_LPWSTR)
            to_utf8(value.pwszVal, out, size);
        PropVariantClear(&value);
        RELEASE(store);
    }
}

static int container_id(IMMDevice *device, GUID *out)
{
    IPropertyStore *store = NULL;
    PROPVARIANT value;
    int found = 0;
    PropVariantInit(&value);
    if (SUCCEEDED(IMMDevice_OpenPropertyStore(device, STGM_READ, &store))) {
        if (SUCCEEDED(IPropertyStore_GetValue(store, &PKEY_Device_ContainerId, &value)) && value.vt == VT_CLSID && value.puuid) {
            *out = *value.puuid;
            found = 1;
        }
        PropVariantClear(&value);
        RELEASE(store);
    }
    return found;
}

/* The format the audio engine runs the hardware at (what exclusive mode should ask for first), copied into *out. */
static int device_format(IMMDevice *device, WAVEFORMATEXTENSIBLE *out)
{
    IPropertyStore *store = NULL;
    PROPVARIANT value;
    int found = 0;
    PropVariantInit(&value);
    if (SUCCEEDED(IMMDevice_OpenPropertyStore(device, STGM_READ, &store))) {
        if (SUCCEEDED(IPropertyStore_GetValue(store, &CTH_PKEY_AudioEngine_DeviceFormat, &value)) && value.vt == VT_BLOB
            && value.blob.cbSize >= sizeof(WAVEFORMATEX)) {
            memset(out, 0, sizeof *out);
            memcpy(out, value.blob.pBlobData, value.blob.cbSize < sizeof *out ? value.blob.cbSize : sizeof *out);
            found = 1;
        }
        PropVariantClear(&value);
        RELEASE(store);
    }
    return found;
}

/* Channels and rate as the device runs: its engine format, or the shared mix format. */
static void describe(IMMDevice *device, int32_t *channels, double *rate)
{
    WAVEFORMATEXTENSIBLE format;
    *channels = 0;
    *rate = 0;
    if (device_format(device, &format)) {
        *channels = format.Format.nChannels;
        *rate = format.Format.nSamplesPerSec;
        return;
    }
    IAudioClient *client = NULL;
    WAVEFORMATEX *mix = NULL;
    if (SUCCEEDED(IMMDevice_Activate(device, &CTH_IID_IAudioClient, CLSCTX_ALL, NULL, (void **)&client))
        && SUCCEEDED(IAudioClient_GetMixFormat(client, &mix))) {
        *channels = mix->nChannels;
        *rate = mix->nSamplesPerSec;
        CoTaskMemFree(mix);
    }
    RELEASE(client);
}

static IMMDeviceCollection *active_endpoints(IMMDeviceEnumerator *e, EDataFlow flow)
{
    IMMDeviceCollection *collection = NULL;
    if (FAILED(IMMDeviceEnumerator_EnumAudioEndpoints(e, flow, DEVICE_STATE_ACTIVE, &collection))) return NULL;
    return collection;
}

static IMMDevice *default_endpoint(IMMDeviceEnumerator *e, EDataFlow flow)
{
    IMMDevice *device = NULL;
    IMMDeviceEnumerator_GetDefaultAudioEndpoint(e, flow, eConsole, &device);
    return device;
}

/* The capture endpoint on the same hardware as render: the default capture endpoint when it qualifies, else the
 * first active one in the container. NULL when the render endpoint has no container or no capture sibling. */
static IMMDevice *paired_capture(IMMDeviceEnumerator *e, IMMDevice *render)
{
    GUID container, candidate;
    if (!container_id(render, &container)) return NULL;

    IMMDevice *preferred = default_endpoint(e, eCapture);
    if (preferred && container_id(preferred, &candidate) && IsEqualGUID(&container, &candidate)) return preferred;
    RELEASE(preferred);

    IMMDeviceCollection *captures = active_endpoints(e, eCapture);
    UINT count = 0;
    IMMDevice *found = NULL;
    if (captures) IMMDeviceCollection_GetCount(captures, &count);
    for (UINT i = 0; i < count && !found; i++) {
        IMMDevice *device = NULL;
        if (FAILED(IMMDeviceCollection_Item(captures, i, &device))) continue;
        if (container_id(device, &candidate) && IsEqualGUID(&container, &candidate)) found = device;
        else RELEASE(device);
    }
    RELEASE(captures);
    return found;
}

/* ---- Enumeration ------------------------------------------------------------------------------------------------ */

int32_t cth_audio_device_count(void)
{
    int com = com_enter();
    IMMDeviceEnumerator *e = enumerator();
    IMMDeviceCollection *renders = e ? active_endpoints(e, eRender) : NULL;
    UINT count = 0;
    if (renders) IMMDeviceCollection_GetCount(renders, &count);
    RELEASE(renders);
    RELEASE(e);
    com_leave(com);
    return (int32_t)count;
}

int32_t cth_audio_device_info(int32_t index, cth_device_info *info)
{
    if (!info || index < 0) return fail("device index out of range", 0);
    int com = com_enter();
    int32_t result = 0;
    IMMDeviceEnumerator *e = enumerator();
    IMMDeviceCollection *renders = e ? active_endpoints(e, eRender) : NULL;
    IMMDevice *render = NULL, *capture = NULL;
    UINT count = 0;
    if (renders) IMMDeviceCollection_GetCount(renders, &count);
    if ((UINT)index >= count || FAILED(IMMDeviceCollection_Item(renders, (UINT)index, &render))) {
        result = fail("device index out of range", 0);
    } else {
        memset(info, 0, sizeof *info);
        device_id(render, info->uid, sizeof info->uid);
        friendly_name(render, info->name, sizeof info->name);
        describe(render, &info->output_channels, &info->sample_rate);
        capture = paired_capture(e, render);
        if (capture) {
            double capture_rate;
            describe(capture, &info->input_channels, &capture_rate);
        }
    }
    RELEASE(capture);
    RELEASE(render);
    RELEASE(renders);
    RELEASE(e);
    com_leave(com);
    return result;
}

int32_t cth_audio_channel_name(int32_t device_index, int32_t is_input, int32_t channel, char *name, int32_t size)
{
    if (!name || size <= 0) return fail("invalid arguments", 0);
    name[0] = 0;
    cth_device_info info;
    if (cth_audio_device_info(device_index, &info) != 0) return -1;
    if (channel < 0 || channel >= (is_input ? info.input_channels : info.output_channels))
        return fail("channel out of range", 0);
    /* WASAPI endpoints carry no per-channel names, so every channel reads as unnamed. */
    return 0;
}

static IMMDevice *device_by_id(IMMDeviceEnumerator *e, const char *id, size_t length)
{
    wchar_t *wide = from_utf8(id, length);
    IMMDevice *device = NULL;
    if (wide) IMMDeviceEnumerator_GetDevice(e, wide, &device);
    free(wide);
    return device;
}

/* Resolves a cth uid (see the top of the file) to its render and capture endpoints. */
static HRESULT find_devices(IMMDeviceEnumerator *e, const char *uid, int want_input, IMMDevice **render, IMMDevice **capture)
{
    *render = NULL;
    *capture = NULL;
    if (!uid || !uid[0]) {
        *render = default_endpoint(e, eRender);
        if (*render && want_input) *capture = default_endpoint(e, eCapture);
    } else {
        const char *bar = strchr(uid, '|');
        *render = device_by_id(e, uid, bar ? (size_t)(bar - uid) : strlen(uid));
        if (*render && want_input) {
            if (!bar) *capture = paired_capture(e, *render);
            else if (bar[1]) *capture = device_by_id(e, bar + 1, strlen(bar + 1));
        }
    }
    return *render ? S_OK : E_INVALIDARG;
}

/* ---- Formats ---------------------------------------------------------------------------------------------------- */

static void make_format(WAVEFORMATEXTENSIBLE *f, int32_t channels, DWORD mask, double rate, sample_kind kind)
{
    static const WORD bits[] = {32, 32, 24, 16};
    memset(f, 0, sizeof *f);
    f->Format.wFormatTag = WAVE_FORMAT_EXTENSIBLE;
    f->Format.nChannels = (WORD)channels;
    f->Format.nSamplesPerSec = (DWORD)rate;
    f->Format.wBitsPerSample = bits[kind];
    f->Format.nBlockAlign = (WORD)(channels * bits[kind] / 8);
    f->Format.nAvgBytesPerSec = f->Format.nSamplesPerSec * f->Format.nBlockAlign;
    f->Format.cbSize = sizeof(WAVEFORMATEXTENSIBLE) - sizeof(WAVEFORMATEX);
    f->Samples.wValidBitsPerSample = kind == SAMPLE_I32 ? 24 : bits[kind];
    f->dwChannelMask = mask;
    f->SubFormat = kind == SAMPLE_F32 ? CTH_SUBTYPE_IEEE_FLOAT : CTH_SUBTYPE_PCM;
}

/* How the shim reads and writes samples of this format; 0 for formats it does not handle. */
static int classify(const WAVEFORMATEX *f, sample_kind *kind)
{
    int is_float = f->wFormatTag == WAVE_FORMAT_IEEE_FLOAT;
    int is_pcm = f->wFormatTag == WAVE_FORMAT_PCM;
    if (f->wFormatTag == WAVE_FORMAT_EXTENSIBLE && f->cbSize >= 22) {
        const WAVEFORMATEXTENSIBLE *x = (const WAVEFORMATEXTENSIBLE *)f;
        is_float = IsEqualGUID(&x->SubFormat, &CTH_SUBTYPE_IEEE_FLOAT);
        is_pcm = IsEqualGUID(&x->SubFormat, &CTH_SUBTYPE_PCM);
    }
    if (is_float && f->wBitsPerSample == 32) *kind = SAMPLE_F32;
    else if (is_pcm && f->wBitsPerSample == 32) *kind = SAMPLE_I32;
    else if (is_pcm && f->wBitsPerSample == 24) *kind = SAMPLE_I24;
    else if (is_pcm && f->wBitsPerSample == 16) *kind = SAMPLE_I16;
    else return 0;
    return 1;
}

static float read_sample(const BYTE *p, sample_kind kind)
{
    switch (kind) {
    case SAMPLE_F32: { float v; memcpy(&v, p, 4); return v; }
    case SAMPLE_I32: { int32_t v; memcpy(&v, p, 4); return (float)v * (1.0f / 2147483648.0f); }
    case SAMPLE_I24: return (float)((int32_t)(((uint32_t)p[0] << 8) | ((uint32_t)p[1] << 16) | ((uint32_t)p[2] << 24)) >> 8) * (1.0f / 8388608.0f);
    default: { int16_t v; memcpy(&v, p, 2); return (float)v * (1.0f / 32768.0f); }
    }
}

static void write_sample(BYTE *p, sample_kind kind, float v)
{
    if (kind == SAMPLE_F32) {
        memcpy(p, &v, 4);
        return;
    }
    if (v > 1.0f) v = 1.0f;
    if (v < -1.0f) v = -1.0f;
    switch (kind) {
    case SAMPLE_I32: { int32_t i = v >= 1.0f ? INT32_MAX : (int32_t)(v * 2147483648.0f); memcpy(p, &i, 4); break; }
    case SAMPLE_I24: {
        int32_t i = v >= 1.0f ? 8388607 : (int32_t)(v * 8388608.0f);
        p[0] = (BYTE)i; p[1] = (BYTE)(i >> 8); p[2] = (BYTE)(i >> 16);
        break;
    }
    default: { int16_t i = v >= 1.0f ? INT16_MAX : (int16_t)(v * 32768.0f); memcpy(p, &i, 2); break; }
    }
}

static int bytes_of(sample_kind kind) { return kind == SAMPLE_I16 ? 2 : kind == SAMPLE_I24 ? 3 : 4; }

/* ---- Opening an endpoint ---------------------------------------------------------------------------------------- */

static REFERENCE_TIME frames_to_hns(int32_t frames, double rate) { return (REFERENCE_TIME)(10000000.0 * frames / rate + 0.5); }
static int32_t hns_to_frames(REFERENCE_TIME hns, double rate) { return (int32_t)(hns * rate / 10000000.0 + 0.5); }

static void close_endpoint(endpoint *ep)
{
    RELEASE(ep->render);
    RELEASE(ep->capture);
    RELEASE(ep->client);
    RELEASE(ep->device);
    if (ep->event) CloseHandle(ep->event);
    ep->event = NULL;
}

static HRESULT activate(endpoint *ep)
{
    RELEASE(ep->client);
    return IMMDevice_Activate(ep->device, &CTH_IID_IAudioClient, CLSCTX_ALL, NULL, (void **)&ep->client);
}

/* Exclusive event-driven stream at rate, in the first sample format the device accepts. */
static HRESULT open_exclusive(endpoint *ep, double rate, int32_t frames)
{
    WAVEFORMATEXTENSIBLE native, candidates[5];
    int n = 0;
    int32_t channels = 2;
    DWORD mask = KSAUDIO_SPEAKER_STEREO;
    if (device_format(ep->device, &native)) {
        channels = native.Format.nChannels;
        mask = native.Format.wFormatTag == WAVE_FORMAT_EXTENSIBLE ? native.dwChannelMask : 0;
        sample_kind kind;
        if (classify(&native.Format, &kind)) {
            candidates[n] = native;
            candidates[n].Format.nSamplesPerSec = (DWORD)rate;
            candidates[n].Format.nAvgBytesPerSec = (DWORD)rate * native.Format.nBlockAlign;
            n++;
        }
    }
    make_format(&candidates[n++], channels, mask, rate, SAMPLE_F32);
    make_format(&candidates[n++], channels, mask, rate, SAMPLE_I32);
    make_format(&candidates[n++], channels, mask, rate, SAMPLE_I24);
    make_format(&candidates[n++], channels, mask, rate, SAMPLE_I16);

    HRESULT hr = activate(ep);
    if (FAILED(hr)) return hr;
    const WAVEFORMATEXTENSIBLE *format = NULL;
    for (int i = 0; i < n && !format; i++) {
        if (IAudioClient_IsFormatSupported(ep->client, AUDCLNT_SHAREMODE_EXCLUSIVE, &candidates[i].Format, NULL) == S_OK)
            format = &candidates[i];
    }
    if (!format) return AUDCLNT_E_UNSUPPORTED_FORMAT;

    REFERENCE_TIME default_period = 0, minimum_period = 0;
    IAudioClient_GetDevicePeriod(ep->client, &default_period, &minimum_period);
    REFERENCE_TIME period = frames > 0 ? frames_to_hns(frames, rate) : default_period;
    if (period < minimum_period) period = minimum_period;

    hr = IAudioClient_Initialize(ep->client, AUDCLNT_SHAREMODE_EXCLUSIVE, AUDCLNT_STREAMFLAGS_EVENTCALLBACK, period, period,
                                 &format->Format, NULL);
    if (hr == AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED) {
        /* The device wants a slightly different period: take the buffer size it offers and try once more. */
        UINT32 aligned = 0;
        IAudioClient_GetBufferSize(ep->client, &aligned);
        period = frames_to_hns((int32_t)aligned, rate);
        hr = activate(ep);
        if (SUCCEEDED(hr))
            hr = IAudioClient_Initialize(ep->client, AUDCLNT_SHAREMODE_EXCLUSIVE, AUDCLNT_STREAMFLAGS_EVENTCALLBACK, period,
                                         period, &format->Format, NULL);
    }
    if (FAILED(hr)) return hr;

    classify(&format->Format, &ep->kind);
    ep->channels = format->Format.nChannels;
    ep->exclusive = 1;
    IAudioClient_GetBufferSize(ep->client, &ep->buffer_size);
    ep->period = (int32_t)ep->buffer_size; /* exclusive event mode ping-pongs buffers of one period */
    return S_OK;
}

/* Shared event-driven stream. At the mix rate it asks IAudioClient3 for the smallest engine period that covers
 * frames; at any other rate the engine converts (capture following a render endpoint that runs another rate). */
static HRESULT open_shared(endpoint *ep, double rate, int32_t frames)
{
    HRESULT hr = activate(ep);
    if (FAILED(hr)) return hr;
    WAVEFORMATEX *mix = NULL;
    hr = IAudioClient_GetMixFormat(ep->client, &mix);
    if (FAILED(hr)) return hr;
    if (rate <= 0) rate = mix->nSamplesPerSec;

    WAVEFORMATEXTENSIBLE format;
    if (mix->nSamplesPerSec == (DWORD)rate) {
        memcpy(&format, mix, mix->wFormatTag == WAVE_FORMAT_EXTENSIBLE ? sizeof format : sizeof(WAVEFORMATEX));
    } else {
        make_format(&format, mix->nChannels,
                    mix->wFormatTag == WAVE_FORMAT_EXTENSIBLE ? ((WAVEFORMATEXTENSIBLE *)mix)->dwChannelMask : 0, rate,
                    SAMPLE_F32);
    }
    int at_mix_rate = mix->nSamplesPerSec == (DWORD)rate;
    CoTaskMemFree(mix);
    if (!classify(&format.Format, &ep->kind)) return AUDCLNT_E_UNSUPPORTED_FORMAT;

    hr = E_NOTIMPL;
    IAudioClient3 *client3 = NULL;
    if (at_mix_rate && SUCCEEDED(IAudioClient_QueryInterface(ep->client, &CTH_IID_IAudioClient3, (void **)&client3))) {
        UINT32 default_period = 0, fundamental = 0, minimum = 0, maximum = 0;
        if (SUCCEEDED(IAudioClient3_GetSharedModeEnginePeriod(client3, &format.Format, &default_period, &fundamental, &minimum,
                                                              &maximum))
            && fundamental > 0) {
            UINT32 want = frames > 0 ? (UINT32)frames : default_period;
            want = (want + fundamental - 1) / fundamental * fundamental;
            if (want < minimum) want = minimum;
            if (want > maximum) want = maximum;
            hr = IAudioClient3_InitializeSharedAudioStream(client3, AUDCLNT_STREAMFLAGS_EVENTCALLBACK, want, &format.Format, NULL);
            if (SUCCEEDED(hr)) ep->period = (int32_t)want;
        }
        RELEASE(client3);
    }

    if (FAILED(hr)) {
        hr = activate(ep); /* a failed Initialize leaves the client unusable */
        if (FAILED(hr)) return hr;
        DWORD flags = AUDCLNT_STREAMFLAGS_EVENTCALLBACK;
        if (!at_mix_rate) flags |= AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;
        REFERENCE_TIME default_period = 0, minimum_period = 0;
        IAudioClient_GetDevicePeriod(ep->client, &default_period, &minimum_period);
        hr = IAudioClient_Initialize(ep->client, AUDCLNT_SHAREMODE_SHARED, flags, 0, 0, &format.Format, NULL);
        if (FAILED(hr)) return hr;
        ep->period = hns_to_frames(default_period, rate);
    }

    ep->channels = format.Format.nChannels;
    ep->exclusive = 0;
    IAudioClient_GetBufferSize(ep->client, &ep->buffer_size);
    return S_OK;
}

static int prefer_shared(void)
{
    char value[8];
    DWORD n = GetEnvironmentVariableA("CTH_WASAPI_SHARED", value, sizeof value);
    return n > 0 && n < sizeof value && value[0] != '0';
}

/* Opens ep->device at rate (0: the device's rate), exclusive first unless shared is preferred. */
static HRESULT open_endpoint(endpoint *ep, int is_capture, double rate, int32_t frames)
{
    HRESULT hr = E_FAIL;
    if (!prefer_shared()) {
        double exclusive_rate = rate;
        if (exclusive_rate <= 0) {
            int32_t channels;
            describe(ep->device, &channels, &exclusive_rate);
        }
        if (exclusive_rate > 0) hr = open_exclusive(ep, exclusive_rate, frames);
        if (SUCCEEDED(hr)) ep->rate = exclusive_rate;
    }
    if (FAILED(hr)) {
        hr = open_shared(ep, rate, frames);
        if (FAILED(hr)) return hr;
        WAVEFORMATEX *mix = NULL;
        ep->rate = rate;
        if (rate <= 0 && SUCCEEDED(IAudioClient_GetMixFormat(ep->client, &mix))) {
            ep->rate = mix->nSamplesPerSec;
            CoTaskMemFree(mix);
        }
    }

    ep->frame_bytes = ep->channels * bytes_of(ep->kind);
    REFERENCE_TIME latency = 0;
    IAudioClient_GetStreamLatency(ep->client, &latency);
    ep->latency = hns_to_frames(latency, ep->rate);

    ep->event = CreateEventW(NULL, FALSE, FALSE, NULL);
    if (!ep->event) return HRESULT_FROM_WIN32(GetLastError());
    hr = IAudioClient_SetEventHandle(ep->client, ep->event);
    if (FAILED(hr)) return hr;
    return is_capture ? IAudioClient_GetService(ep->client, &CTH_IID_IAudioCaptureClient, (void **)&ep->capture)
                      : IAudioClient_GetService(ep->client, &CTH_IID_IAudioRenderClient, (void **)&ep->render);
}

/* ---- The audio thread ------------------------------------------------------------------------------------------- */

static uint32_t fifo_fill(const cth_stream *s) { return s->fifo_write - s->fifo_read; }

/* Moves every captured packet into the FIFO, dropping the oldest frames if the render side stopped pulling. */
static void drain_capture(cth_stream *s)
{
    UINT32 packet = 0;
    while (SUCCEEDED(IAudioCaptureClient_GetNextPacketSize(s->in.capture, &packet)) && packet > 0) {
        BYTE *data = NULL;
        UINT32 frames = 0;
        DWORD flags = 0;
        if (FAILED(IAudioCaptureClient_GetBuffer(s->in.capture, &data, &frames, &flags, NULL, NULL))) return;
        if ((flags & AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY) && s->capture_primed)
            InterlockedIncrement64(&s->overloads); /* the first packet after Start always carries the flag */
        s->capture_primed = 1;

        if (fifo_fill(s) + frames > FIFO_FRAMES) {
            s->fifo_read = s->fifo_write + frames - FIFO_FRAMES;
            InterlockedIncrement64(&s->overloads);
        }
        int silent = (flags & AUDCLNT_BUFFERFLAGS_SILENT) || !data;
        int bytes = bytes_of(s->in.kind);
        for (UINT32 i = 0; i < frames; i++) {
            uint32_t slot = (s->fifo_write + i) & (FIFO_FRAMES - 1);
            const BYTE *frame = data + (size_t)i * s->in.frame_bytes;
            for (int32_t c = 0; c < s->input_channels; c++)
                s->fifo[(size_t)c * FIFO_FRAMES + slot] = silent ? 0.0f : read_sample(frame + c * bytes, s->in.kind);
        }
        s->fifo_write += frames;
        IAudioCaptureClient_ReleaseBuffer(s->in.capture, frames);
    }
}

/* Fills the planar inputs for one chunk from the FIFO; an underrun is padded with silence, and counted once running
 * (the first fill of a shared-mode buffer asks for more than the one-period cushion holds). */
static void pull_inputs(cth_stream *s, int32_t frames, int started)
{
    uint32_t available = fifo_fill(s);
    uint32_t take = available < (uint32_t)frames ? available : (uint32_t)frames;
    if (take < (uint32_t)frames && started) InterlockedIncrement64(&s->overloads);
    for (int32_t c = 0; c < s->input_channels; c++) {
        float *dst = (float *)s->in_ptrs[c];
        const float *ring = s->fifo + (size_t)c * FIFO_FRAMES;
        for (uint32_t i = 0; i < take; i++) dst[i] = ring[(s->fifo_read + i) & (FIFO_FRAMES - 1)];
        memset(dst + take, 0, (size_t)(frames - (int32_t)take) * sizeof(float));
    }
    s->fifo_read += take;

    /* Separate render and capture clocks drift; past three periods of backlog, fall back to the one-period cushion. */
    uint32_t cushion = (uint32_t)s->buffer_frames;
    if (fifo_fill(s) > 3 * cushion + (uint32_t)s->in.period) {
        s->fifo_read = s->fifo_write - cushion;
        InterlockedIncrement64(&s->overloads);
    }
}

/* Fills whatever the render buffer has room for; returns the frames written. */
static UINT32 render(cth_stream *s, int started)
{
    UINT32 frames = s->out.buffer_size;
    if (!s->out.exclusive) {
        UINT32 padding = 0;
        if (FAILED(IAudioClient_GetCurrentPadding(s->out.client, &padding))) return 0;
        if (padding == 0 && started) InterlockedIncrement64(&s->overloads); /* the engine ran dry */
        frames -= padding;
    }
    if (frames == 0) return 0;

    BYTE *data = NULL;
    if (FAILED(IAudioRenderClient_GetBuffer(s->out.render, frames, &data))) return 0;
    int bytes = bytes_of(s->out.kind);
    for (UINT32 offset = 0; offset < frames;) {
        int32_t chunk = (int32_t)(frames - offset);
        if (chunk > s->buffer_frames) chunk = s->buffer_frames;
        if (s->input_channels > 0) pull_inputs(s, chunk, started);
        for (int32_t c = 0; c < s->output_channels; c++) memset(s->out_ptrs[c], 0, (size_t)chunk * sizeof(float));
        s->process(s->user, s->in_ptrs, s->input_channels, s->out_ptrs, s->output_channels, chunk);
        for (int32_t i = 0; i < chunk; i++) {
            BYTE *frame = data + (size_t)(offset + (UINT32)i) * s->out.frame_bytes;
            for (int32_t c = 0; c < s->out.channels; c++)
                write_sample(frame + c * bytes, s->out.kind, c < s->output_channels ? s->out_ptrs[c][i] : 0.0f);
        }
        offset += (UINT32)chunk;
    }
    IAudioRenderClient_ReleaseBuffer(s->out.render, frames, 0);
    InterlockedIncrement64(&s->callbacks);
    return frames;
}

static void record_timing(cth_stream *s, LONGLONG started, UINT32 frames)
{
    if (frames == 0 || s->sample_rate <= 0) return;
    LARGE_INTEGER now;
    QueryPerformanceCounter(&now);
    double elapsed_ns = (double)(now.QuadPart - started) * s->ticks_to_ns;
    double percent = elapsed_ns / (frames / s->sample_rate * 1e9) * 100.0;
    int bin = percent >= CTH_TIMING_BINS - 1 ? CTH_TIMING_BINS - 1 : (int)percent;
    s->timing.bins[bin]++;
    if (percent > s->timing.max_percent) s->timing.max_percent = percent;
    if (percent >= 100.0) s->timing.over_budget++;
}

static unsigned __stdcall audio_thread(void *arg)
{
    cth_stream *s = (cth_stream *)arg;
    int com = com_enter();
    DWORD task_index = 0;
    HANDLE task = AvSetMmThreadCharacteristicsW(L"Pro Audio", &task_index);
    if (task) AvSetMmThreadPriority(task, AVRT_PRIORITY_HIGH);
    else SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_TIME_CRITICAL);
    cth_fpu_flush_denormals();

    /* Start with the render buffer full of silence (as WASAPI asks) and one period of input cushion. */
    if (s->fifo) memset(s->fifo, 0, (size_t)s->input_channels * FIFO_FRAMES * sizeof(float));
    s->fifo_read = 0;
    s->fifo_write = s->input_channels > 0 ? (uint32_t)s->buffer_frames : 0;
    s->capture_primed = 0;
    BYTE *data = NULL;
    if (SUCCEEDED(IAudioRenderClient_GetBuffer(s->out.render, s->out.buffer_size, &data)))
        IAudioRenderClient_ReleaseBuffer(s->out.render, s->out.buffer_size, AUDCLNT_BUFFERFLAGS_SILENT);

    HRESULT hr = S_OK;
    if (s->in.client) hr = IAudioClient_Start(s->in.client);
    if (SUCCEEDED(hr)) hr = IAudioClient_Start(s->out.client);
    s->thread_status = hr;
    SetEvent(s->started_event);

    HANDLE events[3] = {s->stop_event, s->out.event, s->in.event};
    DWORD count = s->in.client ? 3 : 2;
    int started = 0;
    while (SUCCEEDED(hr)) {
        DWORD wait = WaitForMultipleObjects(count, events, FALSE, 2000);
        if (wait == WAIT_OBJECT_0) break;
        if (wait == WAIT_TIMEOUT) {
            InterlockedIncrement64(&s->overloads); /* the device stopped calling (unplugged or taken away) */
            continue;
        }
        if (wait == WAIT_FAILED) break;

        if (InterlockedExchange(&s->reset_timing, 0)) memset(&s->timing, 0, sizeof s->timing);
        if (s->in.client) drain_capture(s);
        if (wait != WAIT_OBJECT_0 + 1) continue; /* a capture event only fills the FIFO */

        LARGE_INTEGER before;
        QueryPerformanceCounter(&before);
        record_timing(s, before.QuadPart, render(s, started));
        started = 1;
    }

    IAudioClient_Stop(s->out.client);
    if (s->in.client) IAudioClient_Stop(s->in.client);
    if (task) AvRevertMmThreadCharacteristics(task);
    com_leave(com);
    return 0;
}

/* ---- Public API ------------------------------------------------------------------------------------------------- */

static void free_stream(cth_stream *s)
{
    close_endpoint(&s->out);
    close_endpoint(&s->in);
    if (s->stop_event) CloseHandle(s->stop_event);
    if (s->started_event) CloseHandle(s->started_event);
    free(s->planar);
    free(s->fifo);
    free(s);
}

int32_t cth_audio_open(const char *device_uid, cth_stream_config *config, cth_process_fn process, void *user, cth_stream **stream)
{
    if (!config || !process || !stream) return fail("invalid arguments", 0);
    *stream = NULL;

    cth_stream *s = (cth_stream *)calloc(1, sizeof *s);
    if (!s) return fail("out of memory", 0);
    int com = com_enter();
    int32_t result = 0;
    HRESULT hr;
    IMMDeviceEnumerator *e = enumerator();
    if (!e) {
        result = fail("could not create the device enumerator", E_FAIL);
        goto done;
    }
    if (FAILED(find_devices(e, device_uid, config->input_channels > 0, &s->out.device, &s->in.device))) {
        result = fail("audio device not found", 0);
        goto done;
    }

    hr = open_endpoint(&s->out, 0, config->sample_rate, config->buffer_frames);
    if (FAILED(hr)) {
        result = fail("could not open the output", hr);
        goto done;
    }
    s->sample_rate = s->out.rate;
    s->buffer_frames = s->out.period > 0 ? s->out.period : (int32_t)s->out.buffer_size;
    if (s->buffer_frames > CTH_MAX_FRAMES) s->buffer_frames = CTH_MAX_FRAMES;
    s->output_channels = s->out.channels > MAX_CHANNELS ? MAX_CHANNELS : s->out.channels;

    if (s->in.device) {
        /* The capture endpoint follows the render rate; it converts in shared mode if its hardware runs another. */
        hr = open_endpoint(&s->in, 1, s->sample_rate, s->buffer_frames);
        if (FAILED(hr)) {
            result = fail("could not open the input", hr);
            goto done;
        }
        int32_t inputs = s->in.channels < config->input_channels ? s->in.channels : config->input_channels;
        s->input_channels = inputs > MAX_CHANNELS ? MAX_CHANNELS : inputs;
    }

    s->output_latency = s->out.latency + s->buffer_frames;
    s->input_latency = s->input_channels > 0 ? s->in.latency + s->in.period + s->buffer_frames : 0; /* + the FIFO cushion */

    s->planar = (float *)calloc((size_t)(s->input_channels + s->output_channels) * CTH_MAX_FRAMES, sizeof(float));
    s->fifo = s->input_channels > 0 ? (float *)calloc((size_t)s->input_channels * FIFO_FRAMES, sizeof(float)) : NULL;
    s->stop_event = CreateEventW(NULL, TRUE, FALSE, NULL);
    s->started_event = CreateEventW(NULL, FALSE, FALSE, NULL);
    if (!s->planar || (s->input_channels > 0 && !s->fifo) || !s->stop_event || !s->started_event) {
        result = fail("out of memory", E_OUTOFMEMORY);
        goto done;
    }
    for (int32_t c = 0; c < s->input_channels; c++) s->in_ptrs[c] = s->planar + (size_t)c * CTH_MAX_FRAMES;
    for (int32_t c = 0; c < s->output_channels; c++) s->out_ptrs[c] = s->planar + (size_t)(s->input_channels + c) * CTH_MAX_FRAMES;

    LARGE_INTEGER frequency;
    QueryPerformanceFrequency(&frequency);
    s->ticks_to_ns = 1e9 / (double)frequency.QuadPart;
    s->process = process;
    s->user = user;

    config->sample_rate = s->sample_rate;
    config->buffer_frames = s->buffer_frames;
    config->input_channels = s->input_channels;
    config->output_channels = s->output_channels;

done:
    RELEASE(e);
    if (result != 0) free_stream(s);
    else *stream = s;
    com_leave(com);
    return result;
}

int32_t cth_audio_start(cth_stream *s)
{
    if (!s) return fail("no stream", 0);
    if (s->running) return 0;
    ResetEvent(s->stop_event);
    s->thread = (HANDLE)_beginthreadex(NULL, 0, audio_thread, s, 0, NULL);
    if (!s->thread) return fail("could not create the audio thread", HRESULT_FROM_WIN32(GetLastError()));
    WaitForSingleObject(s->started_event, INFINITE);
    if (FAILED(s->thread_status)) {
        WaitForSingleObject(s->thread, INFINITE);
        CloseHandle(s->thread);
        s->thread = NULL;
        IAudioClient_Reset(s->out.client);
        if (s->in.client) IAudioClient_Reset(s->in.client);
        return fail("could not start the device", s->thread_status);
    }
    s->running = 1;
    return 0;
}

int32_t cth_audio_stop(cth_stream *s)
{
    if (!s || !s->running) return 0;
    SetEvent(s->stop_event);
    WaitForSingleObject(s->thread, INFINITE);
    CloseHandle(s->thread);
    s->thread = NULL;
    s->running = 0;
    /* Discard what is queued so a later start begins clean. */
    IAudioClient_Reset(s->out.client);
    if (s->in.client) IAudioClient_Reset(s->in.client);
    return 0;
}

void cth_audio_close(cth_stream *s)
{
    if (!s) return;
    cth_audio_stop(s);
    int com = com_enter();
    free_stream(s);
    com_leave(com);
}

void cth_audio_stats(const cth_stream *s, cth_stream_stats *stats)
{
    if (!stats) return;
    memset(stats, 0, sizeof *stats);
    if (!s) return;
    stats->callbacks = (uint64_t)InterlockedCompareExchange64((volatile LONG64 *)&s->callbacks, 0, 0);
    stats->overloads = (uint64_t)InterlockedCompareExchange64((volatile LONG64 *)&s->overloads, 0, 0);
    stats->input_latency_frames = s->input_latency;
    stats->output_latency_frames = s->output_latency;
}

void cth_audio_timing(const cth_stream *s, cth_stream_timing *timing)
{
    if (!timing) return;
    if (!s) {
        memset(timing, 0, sizeof *timing);
        return;
    }
    memcpy(timing, &s->timing, sizeof *timing); /* racy snapshot of counters; fine for statistics */
}

void cth_audio_reset_timing(cth_stream *s)
{
    if (s) InterlockedExchange(&s->reset_timing, 1);
}
