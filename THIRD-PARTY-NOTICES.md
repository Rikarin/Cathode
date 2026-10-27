# Third-party notices

Cathode is licensed under GPL-3.0-or-later (see [LICENSE](LICENSE)). This file lists the third-party code compiled
into its native libraries (`engine/native`), with the licence each is used under. The build fetches all of it, pinned,
into `engine/artifacts/deps`; none of it is kept in this repository. NuGet and npm packages are listed by their package
managers' lock files.

| Component | Used in | Version | Licence used | Source |
|---|---|---|---|---|
| NeuralAmpModelerCore | `cth_nam` | v0.6.0 (pinned commit in `engine/native/cth_nam/CMakeLists.txt`) | MIT | https://github.com/sdatkinson/NeuralAmpModelerCore |
| Eigen (NeuralAmpModelerCore's submodule) | `cth_nam` | as pinned by NeuralAmpModelerCore | MPL-2.0 | https://gitlab.com/libeigen/eigen |
| nlohmann/json (bundled with NeuralAmpModelerCore) | `cth_nam` | as bundled | MIT | https://github.com/nlohmann/json |
| Steinberg ASIO SDK | `cth_audio` on Windows (the ASIO backend; headers only) | 2.3.4 (2025-10-15, pinned by URL and SHA-256 in `engine/native/cth_audio/CMakeLists.txt`) | **GPLv3**, one of the SDK's two licences | https://www.steinberg.net/asiosdk |

## Steinberg ASIO SDK

Since version 2.3.4 the ASIO SDK is dual-licensed: under the proprietary Steinberg ASIO License, or under the GNU
General Public License version 3. Cathode uses it under **GPLv3**, which is compatible with Cathode's own
GPL-3.0-or-later; a Windows build that includes the ASIO backend is therefore distributed under GPLv3 as a whole.
Cathode has not signed the proprietary agreement, so a closed-source derivative that keeps the ASIO backend would need
its own agreement with Steinberg.

ASIO is a trademark and software of Steinberg Media Technologies GmbH. Cathode does not use the ASIO logo; the name
appears only to say which driver standard a device uses.
