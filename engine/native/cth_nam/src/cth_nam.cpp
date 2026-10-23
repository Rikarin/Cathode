// SPDX-License-Identifier: GPL-3.0-or-later
#include "cth_nam.h"

#include <algorithm>
#include <cstring>
#include <exception>
#include <filesystem>
#include <memory>
#include <string>
#include <vector>

#include "NAM/activations.h"
#include "NAM/dsp.h"
#include "NAM/get_dsp.h"
#include "NAM/slimmable.h"
#include "NAM/version.h"

struct cth_nam_model
{
  std::unique_ptr<nam::DSP> dsp;
  nam::SlimmableModel* slimmable = nullptr;
  std::vector<double> input;  // NAM processes in double (NAM_SAMPLE); we convert at the boundary
  std::vector<double> output;
  int32_t max_frames = 0;
  double sample_rate = 0;
  cth_nam_info info{};
};

namespace
{
thread_local std::string last_error;

int32_t fail(const std::string& message)
{
  last_error = message;
  return -1;
}

void copy_string(char* destination, size_t size, const std::string& value)
{
  const auto length = std::min(size - 1, value.size());
  std::memcpy(destination, value.data(), length);
  destination[length] = 0;
}
} // namespace

extern "C" {

int32_t cth_nam_load(const char* path, cth_nam_model** model)
{
  if (!path || !model)
    return fail("invalid arguments");
  *model = nullptr;

  try
  {
    nam::dspData data;
    auto dsp = nam::get_dsp(std::filesystem::path(reinterpret_cast<const char8_t*>(path)), data);
    if (!dsp)
      return fail("model could not be created");
    if (dsp->NumInputChannels() != 1 || dsp->NumOutputChannels() != 1)
      return fail("only mono-in, mono-out models are supported");

    auto result = std::make_unique<cth_nam_model>();
    auto& info = result->info;
    copy_string(info.architecture, sizeof info.architecture, data.architecture);
    copy_string(info.version, sizeof info.version, data.version);
    info.expected_sample_rate = dsp->GetExpectedSampleRate();
    info.has_loudness = dsp->HasLoudness() ? 1 : 0;
    info.loudness = dsp->HasLoudness() ? dsp->GetLoudness() : 0.0;
    info.has_input_level = dsp->HasInputLevel() ? 1 : 0;
    info.input_level = dsp->HasInputLevel() ? dsp->GetInputLevel() : 0.0;
    info.has_output_level = dsp->HasOutputLevel() ? 1 : 0;
    info.output_level = dsp->HasOutputLevel() ? dsp->GetOutputLevel() : 0.0;
    info.prewarm_samples = dsp->GetPrewarmSamples();
    result->slimmable = dynamic_cast<nam::SlimmableModel*>(dsp.get());
    info.slimmable = result->slimmable ? 1 : 0;
    result->dsp = std::move(dsp);
    *model = result.release();
    return 0;
  }
  catch (const std::exception& e)
  {
    return fail(e.what());
  }
}

void cth_nam_get_info(const cth_nam_model* model, cth_nam_info* info)
{
  if (model && info)
    *info = model->info;
}

int32_t cth_nam_reset(cth_nam_model* model, double sample_rate, int32_t max_frames)
{
  if (!model || max_frames <= 0)
    return fail("invalid arguments");
  try
  {
    model->input.assign(static_cast<size_t>(max_frames), 0.0);
    model->output.assign(static_cast<size_t>(max_frames), 0.0);
    model->max_frames = max_frames;
    model->sample_rate = sample_rate;
    model->dsp->ResetAndPrewarm(sample_rate, max_frames);
    return 0;
  }
  catch (const std::exception& e)
  {
    return fail(e.what());
  }
}

int32_t cth_nam_set_size(cth_nam_model* model, double size)
{
  if (!model)
    return fail("invalid arguments");
  if (!model->slimmable)
    return fail("model is not slimmable");
  try
  {
    model->slimmable->SetSlimmableSize(std::clamp(size, 0.0, 1.0));
    if (model->max_frames > 0)
      model->dsp->ResetAndPrewarm(model->sample_rate, model->max_frames);
    return 0;
  }
  catch (const std::exception& e)
  {
    return fail(e.what());
  }
}

void cth_nam_process(cth_nam_model* model, const float* input, float* output, int32_t frames)
{
  for (int32_t offset = 0; offset < frames; offset += model->max_frames)
  {
    const int32_t count = std::min(model->max_frames, frames - offset);
    double* in = model->input.data();
    double* out = model->output.data();
    for (int32_t i = 0; i < count; i++)
      in[i] = input[offset + i];
    model->dsp->process(&in, &out, count);
    for (int32_t i = 0; i < count; i++)
      output[offset + i] = static_cast<float>(out[i]);
  }
}

void cth_nam_free(cth_nam_model* model)
{
  delete model;
}

void cth_nam_set_fast_tanh(int32_t enabled)
{
  if (enabled)
    nam::activations::Activation::enable_fast_tanh();
  else
    nam::activations::Activation::disable_fast_tanh();
}

const char* cth_nam_core_version(void)
{
  static const std::string version = std::to_string(NEURAL_AMP_MODELER_DSP_VERSION_MAJOR) + "."
                                     + std::to_string(NEURAL_AMP_MODELER_DSP_VERSION_MINOR) + "."
                                     + std::to_string(NEURAL_AMP_MODELER_DSP_VERSION_PATCH);
  return version.c_str();
}

const char* cth_nam_last_error(void)
{
  return last_error.c_str();
}

} // extern "C"
