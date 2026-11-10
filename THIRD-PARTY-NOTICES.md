# Third-party notices

Cathode is licensed under GPL-3.0-or-later (see [LICENSE](LICENSE)). This file lists the third-party code its released
binaries contain, with the licence each is used under. None of it is kept in this repository: the builds fetch it,
pinned (the native code into `engine/artifacts/deps`, NuGet packages by `engine/Directory.Packages.props`, npm packages
by `editor/pnpm-lock.yaml`).

The installers carry this file and the licence texts in a `licenses` folder: in the desktop app's resources (Help ▸
Open Source Licences opens it), and at the top of the headless package (installed to `/opt/cathode/licenses`). The
packaging scripts fill it: `editor/apps/desktop/scripts/publish-engine.mjs` and `package.mjs`, and
`packaging/headless/build.sh`.

## Native libraries (`engine/native`)

| Component | Used in | Version | Licence used | Source | Licence text |
|---|---|---|---|---|---|
| NeuralAmpModelerCore | `cth_nam` | v0.6.0 (pinned commit in `engine/native/cth_nam/CMakeLists.txt`) | MIT | https://github.com/sdatkinson/NeuralAmpModelerCore | `licenses/native/NeuralAmpModelerCore-LICENSE.txt` |
| Eigen (NeuralAmpModelerCore's submodule) | `cth_nam` | as pinned by NeuralAmpModelerCore | MPL-2.0 | https://gitlab.com/libeigen/eigen | `licenses/native/eigen/` |
| nlohmann/json (bundled with NeuralAmpModelerCore) | `cth_nam` | as bundled | MIT, Copyright (c) 2013-2025 Niels Lohmann | https://github.com/nlohmann/json | [MIT License](#mit-license) below |
| Steinberg ASIO SDK | `cth_audio` on Windows (the ASIO backend; headers only) | 2.3.4 (2025-10-15, pinned by URL and SHA-256 in `engine/native/cth_audio/CMakeLists.txt`) | **GPLv3**, one of the SDK's two licences | https://www.steinberg.net/asiosdk | `licenses/LICENSE.txt` (the same GPLv3 text) |

## .NET (`cathode-host`, `cathode-server`)

| Component | Used in | Version | Licence used | Source | Licence text |
|---|---|---|---|---|---|
| .NET runtime and libraries | `cathode-server` (self-contained) and `cathode-host` (compiled in by NativeAOT) | the runtime pack of the SDK in `engine/global.json` | MIT | https://github.com/dotnet/runtime | `licenses/dotnet/Microsoft.NETCore.App-*.txt` (the licence, and the runtime's own third-party notices) |
| ASP.NET Core | `cathode-server` | as above | MIT | https://github.com/dotnet/aspnetcore | `licenses/dotnet/Microsoft.AspNetCore.App-*.txt` |
| Microsoft.AspNetCore.OpenApi | `cathode-server` | `engine/Directory.Packages.props` | MIT, Copyright (c) .NET Foundation and Contributors | https://github.com/dotnet/aspnetcore | [MIT License](#mit-license) below |
| Microsoft.OpenApi (its dependency) | `cathode-server` | as resolved by NuGet | MIT, Copyright (c) Microsoft Corporation | https://github.com/microsoft/OpenAPI.NET | [MIT License](#mit-license) below |

## The editor and the desktop app

| Component | Used in | Version | Licence used | Source | Licence text |
|---|---|---|---|---|---|
| The editor's npm libraries (Angular, RxJS, ECharts, Konva, the Material icons, xUI and the rest of its bundle) | the editor's web build | `editor/pnpm-lock.yaml` | each its own (MIT, Apache-2.0, BSD-3-Clause, 0BSD) | each package's page on npm | `licenses/editor-3rdpartylicenses.txt`, written by the Angular build from the packages in the bundle |
| qrcode-generator | the desktop app's main process (the QR code for Control from other devices) | `editor/pnpm-lock.yaml` | MIT, Copyright (c) 2009 Kazuhiko Arase | https://github.com/kazuhikoarase/qrcode-generator | [MIT License](#mit-license) below |
| Electron, with Chromium and Node.js | the desktop app | `editor/pnpm-lock.yaml` | MIT (Electron); Chromium's and Node.js's components each their own | https://github.com/electron/electron | `LICENSE.electron.txt` and `LICENSES.chromium.html`: beside the executable on Windows and Linux, in `licenses/` on macOS |

`package.mjs` refuses to package a desktop app whose main process bundles an npm package this file does not list.

## MIT License

For the components above marked MIT with a copyright line here, whose packages ship no licence file of their own:

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Steinberg ASIO SDK

Since version 2.3.4 the ASIO SDK is dual-licensed: under the proprietary Steinberg ASIO License, or under the GNU
General Public License version 3. Cathode uses it under **GPLv3**, which is compatible with Cathode's own
GPL-3.0-or-later; a Windows build that includes the ASIO backend is therefore distributed under GPLv3 as a whole.
Cathode has not signed the proprietary agreement, so a closed-source derivative that keeps the ASIO backend would need
its own agreement with Steinberg.

ASIO is a trademark and software of Steinberg Media Technologies GmbH. Cathode does not use the ASIO logo; the name
appears only to say which driver standard a device uses.
