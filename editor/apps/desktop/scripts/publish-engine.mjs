// Publishes the engine for this machine's OS and CPU into dist/engine, the folder the packaged app carries as
// resources/engine (apps/desktop/src/engine.ts): cathode-host as a NativeAOT binary, cathode-server as a self-contained
// single file, and the native shims (built by engine/native/build.sh) beside them. NativeAOT cannot cross-compile, so
// each OS builds its own; CI runs this on one runner per OS (.github/workflows/desktop.yml).
import { execFileSync } from 'node:child_process';
import { existsSync, readdirSync, rmSync } from 'node:fs';
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
