// Publishes the engine for this machine's OS and CPU into dist/engine, the folder the packaged app carries as
// resources/engine (apps/desktop/src/engine.ts): cathode-host as a NativeAOT binary, cathode-server as a self-contained
// single file, and the native shims (built by engine/native/build.sh) beside them. NativeAOT cannot cross-compile, so
// each OS builds its own; CI runs this on one runner per OS (.github/workflows/desktop.yml).
//
// With CATHODE_ENGINE_PREBUILT=1 nothing is built: dist/engine already holds the engine for this OS and CPU, and the
// script only checks that it is all there. release.yml uses this on Linux, where the engine is linked in a Debian 12
// container (its `linux-engine` job) so that it runs on that system's C library and on every newer one, and is then
// packaged on the runner (docs/10-releases.md, "The oldest Linux it runs on").
//
// The licence files of what those binaries contain go into dist/engine/licenses, which the installers ship as their
// licenses folder (THIRD-PARTY-NOTICES.md): .NET's from the Server's publish (Cathode.Server.csproj), and here those of
// the code compiled into cth_nam.
import { execFileSync } from 'node:child_process';
import { cpSync, existsSync, mkdirSync, readFileSync, readdirSync, rmSync } from 'node:fs';
import { join, resolve } from 'node:path';

const workspace = resolve(import.meta.dirname, '../../..');
const engine = resolve(workspace, '../engine');
const out = resolve(workspace, 'dist/engine');

const rids = {
  'darwin-arm64': 'osx-arm64',
  'darwin-x64': 'osx-x64',
  'linux-x64': 'linux-x64',
  'linux-arm64': 'linux-arm64',
  'win32-x64': 'win-x64',
  'win32-arm64': 'win-arm64'
};
const rid = rids[`${process.platform}-${process.arch}`];
if (!rid) {
  throw new Error(`No .NET runtime identifier for ${process.platform}-${process.arch}`);
}

if (process.env['CATHODE_ENGINE_PREBUILT'] === '1') {
  const exe = process.platform === 'win32' ? '.exe' : '';
  const shim = name =>
    process.platform === 'win32' ? `${name}.dll` : `lib${name}.${process.platform === 'darwin' ? 'dylib' : 'so'}`;
  const needed = [`cathode-host${exe}`, `cathode-server${exe}`, shim('cth_audio'), shim('cth_nam'), 'licenses'];
  const missing = needed.filter(entry => !existsSync(join(out, entry)));
  if (missing.length > 0) {
    throw new Error(
      `CATHODE_ENGINE_PREBUILT is set, but ${out} lacks ${missing.join(', ')}: put the engine built for ${rid} there ` +
        'first, or unset the variable to build it here.'
    );
  }

  console.log(`Using the engine already in ${out} (CATHODE_ENGINE_PREBUILT)`);
  process.exit(0);
}

const run = (command, args) => execFileSync(command, args, { cwd: engine, stdio: 'inherit' });

rmSync(out, { recursive: true, force: true });
console.log(`Building the native shims and publishing the engine for ${rid}`);
// On Windows, Git Bash: a bare `bash` can resolve to the WSL launcher.
const gitBash = 'C:\\Program Files\\Git\\bin\\bash.exe';
run(process.platform === 'win32' && existsSync(gitBash) ? gitBash : 'bash', ['native/build.sh']);
run('dotnet', ['publish', 'src/Cathode.Host', '-c', 'Release', '-r', rid, '-o', out]);
run('dotnet', [
  'publish',
  'src/Cathode.Server',
  '-c',
  'Release',
  '-r',
  rid,
  '--self-contained',
  '-p:PublishSingleFile=true',
  '-p:IncludeNativeLibrariesForSelfExtract=true',
  '-p:DebugType=none',
  '-o',
  out
]);

// Debug symbols and API docs stay out of the app.
for (const entry of readdirSync(out)) {
  if (/\.(pdb|dbg|dSYM|xml)$/.test(entry)) {
    rmSync(join(out, entry), { recursive: true, force: true });
  }
}

// NeuralAmpModelerCore (MIT) and Eigen (MPL-2.0, with parts under the other COPYING files) as fetched for this build.
// nlohmann/json ships no licence file with its header: THIRD-PARTY-NOTICES.md carries its notice.
const tag = /GIT_TAG ([0-9a-f]{40})/.exec(readFileSync(join(engine, 'native/cth_nam/CMakeLists.txt'), 'utf8'))?.[1];
const nam = join(engine, `artifacts/deps/nam_core-${tag}`);
const eigen = join(nam, 'Dependencies/eigen');
const licences = join(out, 'licenses/native');
mkdirSync(join(licences, 'eigen'), { recursive: true });
cpSync(join(nam, 'LICENSE'), join(licences, 'NeuralAmpModelerCore-LICENSE.txt'));
const copying = readdirSync(eigen).filter(file => file.startsWith('COPYING.'));
if (!copying.includes('COPYING.MPL2')) {
  throw new Error(`${eigen} has no COPYING.MPL2: Eigen's licence files cannot be copied.`);
}

for (const file of copying) {
  cpSync(join(eigen, file), join(licences, 'eigen', `${file}.txt`));
}
