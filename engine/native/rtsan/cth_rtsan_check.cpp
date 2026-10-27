// cth_rtsan_check: the native real-time paths under Clang's RealtimeSanitizer (docs/03-testing-strategy.md §6).
// SPDX-License-Identifier: GPL-3.0-or-later
//
// RTSan aborts with a report when code in its real-time context allocates, frees, takes a lock or makes a blocking
// system call. The context is entered by functions marked [[clang::nonblocking]] (here) and by the ALSA backend's
// cycle in a CTH_RTSAN build (cth_audio_linux.h), and it covers everything they call, the libraries included.
//
//   cth_rtsan_check <models dir>                 every .nam model through cth_nam_process at several block sizes, then
//                                                a stream on ALSA's "null" PCM whose callback runs a model as well
//   cth_rtsan_check <models dir> canary-direct   allocates in a nonblocking function: must be caught
//   cth_rtsan_check <models dir> canary-shim     allocates in an unmarked stream callback: caught only when the
//                                                ALSA cycle really runs in the real-time context
//
// The canaries prove the check is armed, so a build where RTSan silently does nothing cannot pass for a clean run.
#include <cth_audio.h>
#include <cth_nam.h>

#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <string>
#include <thread>
#include <vector>

namespace {

constexpr double SampleRate = 48'000;
constexpr int MaxFrames = 512;
// 1, odd, typical and the largest reset size, plus one past it (cth_nam_process splits it).
constexpr int BlockSizes[] = {1, 7, 64, 128, MaxFrames, MaxFrames + 300};

void* volatile sink;

void process_model(cth_nam_model* model, float* buffer, int frames) [[clang::nonblocking]]
{
  cth_nam_process(model, buffer, buffer, frames);
}

void allocate_in_realtime() [[clang::nonblocking]]
{
  sink = std::malloc(64);
}

struct StreamState
{
  cth_nam_model* model;
  std::atomic<long> calls{0};
};

void stream_callback(void* user, const float* const* inputs, int32_t input_channels, float* const* outputs,
                     int32_t output_channels, int32_t frames) [[clang::nonblocking]]
{
  auto* state = static_cast<StreamState*>(user);
  for (int32_t c = 0; c < output_channels; c++)
    for (int32_t i = 0; i < frames; i++)
      outputs[c][i] = input_channels > 0 ? inputs[c % input_channels][i] : 0.0f;
  if (state->model && output_channels > 0)
    cth_nam_process(state->model, outputs[0], outputs[0], frames);
  state->calls.fetch_add(1, std::memory_order_relaxed);
}

// Deliberately unmarked: only the shim's own real-time context can catch this.
void allocating_callback(void*, const float* const*, int32_t, float* const*, int32_t, int32_t)
{
  sink = std::malloc(64);
}

// Runs a stream on ALSA's "null" PCM (no hardware; it never waits, so the cycle runs flat out) until the callback
// has run `wanted` times or 10 s pass. Returns the number of callbacks, or -1 if the stream could not run.
long run_stream(cth_process_fn callback, StreamState* state, long wanted)
{
  cth_stream_config config{SampleRate, 128, 2, 0};
  cth_stream* stream = nullptr;
  if (cth_audio_open("null", &config, callback, state, &stream) != 0)
  {
    std::fprintf(stderr, "cannot open ALSA's null PCM: %s\n", cth_audio_last_error());
    return -1;
  }
  std::printf("stream: %s, %.0f Hz, %d frames, %d in, %d out\n", cth_audio_backend(), config.sample_rate,
              config.buffer_frames, config.input_channels, config.output_channels);
  if (cth_audio_start(stream) != 0)
  {
    std::fprintf(stderr, "cannot start the stream: %s\n", cth_audio_last_error());
    cth_audio_close(stream);
    return -1;
  }
  cth_stream_stats stats{};
  const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(10);
  do
  {
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
    cth_audio_stats(stream, &stats);
  } while (static_cast<long>(stats.callbacks) < wanted && std::chrono::steady_clock::now() < deadline);
  cth_audio_stop(stream);
  cth_audio_stats(stream, &stats);
  cth_audio_close(stream);
  return static_cast<long>(stats.callbacks);
}

} // namespace

int main(int argc, char** argv)
{
  if (argc < 2)
  {
    std::fprintf(stderr, "usage: cth_rtsan_check <models dir> [canary-direct|canary-shim]\n");
    return 2;
  }
  const std::string mode = argc > 2 ? argv[2] : "";
  if (mode == "canary-direct")
  {
    allocate_in_realtime();
    std::fprintf(stderr, "canary-direct: the allocation was not caught\n");
    return 0;
  }
  if (mode == "canary-shim")
  {
    StreamState state{nullptr};
    const long calls = run_stream(allocating_callback, &state, 100);
    std::fprintf(stderr, "canary-shim: %ld callbacks allocated and none was caught\n", calls);
    return calls < 0 ? 2 : 0;
  }

  std::vector<std::filesystem::path> paths;
  for (const auto& entry : std::filesystem::directory_iterator(argv[1]))
    if (entry.path().extension() == ".nam")
      paths.push_back(entry.path());
  std::vector<float> buffer(MaxFrames + 300);
  cth_nam_model* stream_model = nullptr;
  int checked = 0;
  for (const auto& path : paths)
  {
    cth_nam_model* model = nullptr;
    if (cth_nam_load(path.string().c_str(), &model) != 0 || cth_nam_reset(model, SampleRate, MaxFrames) != 0)
    {
      std::printf("skipped %s: %s\n", path.filename().string().c_str(), cth_nam_last_error());
      if (model)
        cth_nam_free(model);
      continue;
    }
    unsigned seed = 1;
    for (int frames : BlockSizes)
      for (int block = 0; block < 200; block++)
      {
        for (int i = 0; i < frames; i++)
        {
          seed = seed * 1664525u + 1013904223u;
          buffer[static_cast<size_t>(i)] = static_cast<float>(seed >> 8) / 16777216.0f - 0.5f;
        }
        process_model(model, buffer.data(), frames);
      }
    std::printf("cth_nam_process: %s clean\n", path.filename().string().c_str());
    checked++;
    if (!stream_model)
      stream_model = model;
    else
      cth_nam_free(model);
  }
  if (checked == 0)
  {
    std::fprintf(stderr, "no .nam model in %s could be loaded\n", argv[1]);
    return 1;
  }

  StreamState state{stream_model};
  const long calls = run_stream(stream_callback, &state, 2000);
  cth_nam_free(stream_model);
  if (calls <= 0 || state.calls.load() <= 0)
  {
    std::fprintf(stderr, "the stream did not run (%ld callbacks)\n", calls);
    return 1;
  }
  std::printf("cth_audio (ALSA cycle and a NAM model in the callback): %ld callbacks clean\n", calls);
  return 0;
}
