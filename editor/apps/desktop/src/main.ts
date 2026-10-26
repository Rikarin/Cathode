import {
  BrowserWindow,
  Menu,
  type MenuItemConstructorOptions,
  app,
  ipcMain,
  net,
  protocol,
  session,
  shell
} from 'electron';
import { existsSync, statSync } from 'node:fs';
import { extname, join, normalize, resolve, sep } from 'node:path';
import { pathToFileURL } from 'node:url';
import { type AudioSettings, type ServerTarget, loadSettings, saveSettings } from './settings';
import { type Connection, type IrCaptureOptions, Sidecars, listDevices, waitForServer } from './sidecars';

// Cathode desktop: the editor in a sandboxed window, and the engine (cathode-host + cathode-server) as supervised
// sidecars on loopback, or a remote Server (docs/02-architecture.md §7).

app.setName('Cathode');
if (process.env['CATHODE_USER_DATA']) {
  app.setPath('userData', process.env['CATHODE_USER_DATA']); // tests run with throwaway settings
}

/** The renderer comes from the built editor through this scheme; nothing else is served from disk. */
const Scheme = 'app';
/** The app icon (apps/desktop/assets/icon.png), copied next to main.js by the build. */
const Icon = join(__dirname, 'icon.png');
const editorRoot = resolve(
  process.env['CATHODE_EDITOR_DIR'] ?? join(__dirname, app.isPackaged ? 'editor' : '../editor/browser')
);
const devServer = process.env['CATHODE_EDITOR_URL']; // `nx serve editor` during development

protocol.registerSchemesAsPrivileged([
  { scheme: Scheme, privileges: { standard: true, secure: true, supportFetchAPI: true, corsEnabled: true } }
]);

/** How long to wait for a remote Server before the editor reports it unreachable. */
const RemoteTimeoutMs = 10_000;

let settings = loadSettings();
let sidecars: Sidecars | null = null;
let window: BrowserWindow | null = null;
let quitting = false;

function connection(): Promise<Connection> {
  const target = settings.target;
  if (target.mode === 'remote') {
    // A rig on the network answers within a few seconds or not at all: say so, and let the editor offer the way back.
    return waitForServer(
      target,
      RemoteTimeoutMs,
      `No answer from the Cathode server at ${target.baseUrl}. Check that it is on and on this network.`
    ).then(() => ({ baseUrl: target.baseUrl, token: target.token }));
  }

  sidecars ??= new Sidecars(settings.audio, process.env['CATHODE_DATA_DIR'] ?? null, statuses =>
    window?.webContents.send('cathode:sidecars', statuses)
  );
  return sidecars.start();
}

/** Allows the renderer to reach exactly the Server it uses, and nothing else. */
function contentSecurityPolicy(): string {
  const remote = settings.target.mode === 'remote' ? new URL(settings.target.baseUrl) : null;
  const servers = remote
    ? `${remote.origin} ${remote.protocol === 'https:' ? 'wss' : 'ws'}://${remote.host}`
    : 'http://127.0.0.1:* ws://127.0.0.1:*';
  return [
    "default-src 'self'",
    "script-src 'self'",
    // Angular and the overlay CDK set inline styles.
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: blob:",
    "font-src 'self' data:",
    `connect-src 'self' ${servers}`,
    "object-src 'none'",
    "base-uri 'self'",
    "frame-ancestors 'none'"
  ].join('; ');
}

function serveEditor(): void {
  protocol.handle(Scheme, async request => {
    const { pathname } = new URL(request.url);
    const file = normalize(join(editorRoot, decodeURIComponent(pathname)));
    // Refuse anything outside the editor build. Paths without an extension, like the root a reload asks for
    // (app://editor/#/irs), get the app shell; a missing file is a 404, so a page older than the build fails to load a
    // chunk instead of being handed the shell as JavaScript.
    const inside = file.startsWith(editorRoot + sep);
    if (inside && extname(file) && !isFile(file)) {
      return new Response('Not found', { status: 404 });
    }

    const target = inside && isFile(file) ? file : join(editorRoot, 'index.html');
    const response = await net.fetch(pathToFileURL(target).toString());
    const headers = new Headers(response.headers);
    headers.set('Content-Security-Policy', contentSecurityPolicy());
    return new Response(response.body, { status: response.status, headers });
  });
}

function isFile(path: string): boolean {
  return statSync(path, { throwIfNoEntry: false })?.isFile() ?? false;
}

function createWindow(): void {
  window = new BrowserWindow({
    // The editor's grid, its block page and both bars fit without scrolling at 1440 x 1094, the drawer half the height
    // below the bars (the amp's Basic page with the cab resonance helper under it); smaller hides them.
    width: 1440,
    height: 1094,
    minWidth: 1440,
    minHeight: 1094,
    title: 'Cathode',
    backgroundColor: '#111113',
    show: false,
    icon: Icon,
    // On macOS the editor's navbar is the title bar: no system bar, just the traffic lights, centred in its 48 px.
    ...(process.platform === 'darwin'
      ? { titleBarStyle: 'hidden' as const, trafficLightPosition: { x: 16, y: 17 } }
      : {}),
    webPreferences: {
      preload: join(__dirname, 'preload.js'),
      contextIsolation: true,
      sandbox: true,
      nodeIntegration: false,
      spellcheck: false
    }
  });

  window.once('ready-to-show', () => window?.show());
  // Full screen hides the traffic lights, so the navbar can give their space back.
  window.on('enter-full-screen', () => window?.webContents.send('cathode:fullScreen', true));
  window.on('leave-full-screen', () => window?.webContents.send('cathode:fullScreen', false));
  // The window never navigates away from the editor; links to the web open in the browser.
  window.webContents.on('will-navigate', (event, url) => {
    if (!url.startsWith(`${Scheme}://`) && !(devServer && url.startsWith(devServer))) {
      event.preventDefault();
    }
  });
  window.webContents.setWindowOpenHandler(({ url }) => {
    if (url.startsWith('https://')) {
      void shell.openExternal(url);
    }

    return { action: 'deny' };
  });
  window.on('closed', () => (window = null));
  void window.loadURL(devServer ?? `${Scheme}://editor/index.html`);
}

function handleIpc(): void {
  ipcMain.handle('cathode:connection', () => connection());
  ipcMain.handle('cathode:target', () => settings.target);
  ipcMain.handle('cathode:setTarget', async (_event, target: ServerTarget) => {
    settings = { ...settings, target: validTarget(target) };
    saveSettings(settings);
    if (settings.target.mode === 'remote') {
      await sidecars?.stop();
      sidecars = null;
    }

    window?.reload();
  });
  ipcMain.handle('cathode:sidecars', () => sidecars?.statuses() ?? []);
  ipcMain.handle('cathode:audio:devices', () => listDevices());
  ipcMain.handle('cathode:audio:settings', () => settings.audio);
  ipcMain.handle('cathode:audio:apply', async (_event, audio: AudioSettings) => {
    settings = { ...settings, audio: validAudio(audio) };
    saveSettings(settings);
    await sidecars?.applyAudio(settings.audio);
  });
  ipcMain.handle('cathode:audio:latencyTest', () =>
    sidecars ? sidecars.latencyTest() : { ok: false, output: 'Not running a local engine.', roundTripMs: null }
  );
  ipcMain.handle('cathode:audio:irCapture', (_event, options: IrCaptureOptions) =>
    sidecars
      ? sidecars.irCapture(validCapture(options))
      : { ok: false, output: 'Not running a local engine.', wav: null }
  );
}

/** The renderer is trusted less than main: everything it sends is checked. */
function validAudio(audio: AudioSettings): AudioSettings {
  const device = typeof audio?.device === 'string' && audio.device.length < 512 ? audio.device : null;
  const sampleRate = [44_100, 48_000, 88_200, 96_000].includes(audio?.sampleRate) ? audio.sampleRate : 48_000;
  const bufferFrames = [32, 64, 128, 256, 512].includes(audio?.bufferFrames) ? audio.bufferFrames : 64;
  const channels = (list: unknown) =>
    Array.isArray(list) ? list.filter(c => Number.isInteger(c) && c >= 1 && c <= 64).slice(0, 2) : [];
  // Outputs 1-4: the first channel of each pair, 0 for unmapped.
  const outputs = Array.isArray(audio?.outputs)
    ? audio.outputs.map(c => (Number.isInteger(c) && c >= 0 && c <= 63 ? c : 0)).slice(0, 4)
    : [1];
  const inputs = Array.isArray(audio?.inputs) ? audio.inputs.slice(0, 6).map(channels) : [];
  return {
    device,
    sampleRate,
    bufferFrames,
    loopIn: channels(audio?.loopIn),
    loopOut: channels(audio?.loopOut),
    outputs,
    diOut: channels(audio?.diOut),
    inputs: inputs.length > 0 && inputs[0].length > 0 ? inputs : [[1], ...inputs.slice(1)]
  };
}

function validCapture(options: IrCaptureOptions): IrCaptureOptions {
  const clamp = (value: unknown, min: number, max: number, fallback: number) =>
    typeof value === 'number' && Number.isFinite(value) ? Math.min(max, Math.max(min, value)) : fallback;
  return {
    seconds: clamp(options?.seconds, 1, 30, 5),
    levelDb: clamp(options?.levelDb, -60, 0, -18),
    lengthMs: clamp(options?.lengthMs, 20, 2_000, 500)
  };
}

function validTarget(target: ServerTarget): ServerTarget {
  if (target?.mode === 'remote') {
    const url = new URL(target.baseUrl);
    if (url.protocol !== 'http:' && url.protocol !== 'https:') {
      throw new Error('The Server URL must be http or https.');
    }

    return { mode: 'remote', baseUrl: url.origin, token: String(target.token ?? '') };
  }

  return { mode: 'local' };
}

/**
 * The application menu: Electron's default menus, with Settings… (⌘,) where a Mac app keeps it, in the app menu
 * (and in File elsewhere). It asks the editor to open its Settings dialog.
 */
function buildMenu(): void {
  const settings: MenuItemConstructorOptions = {
    label: 'Settings…',
    accelerator: 'CmdOrCtrl+,',
    click: () => window?.webContents.send('cathode:openSettings')
  };
  const template: MenuItemConstructorOptions[] =
    process.platform === 'darwin'
      ? [
          {
            label: 'Cathode',
            submenu: [
              { role: 'about' },
              { type: 'separator' },
              settings,
              { type: 'separator' },
              { role: 'services' },
              { type: 'separator' },
              { role: 'hide' },
              { role: 'hideOthers' },
              { role: 'unhide' },
              { type: 'separator' },
              { role: 'quit' }
            ]
          },
          { role: 'fileMenu' },
          { role: 'editMenu' },
          { role: 'viewMenu' },
          { role: 'windowMenu' }
        ]
      : [
          { label: 'File', submenu: [settings, { type: 'separator' }, { role: 'quit' }] },
          { role: 'editMenu' },
          { role: 'viewMenu' },
          { role: 'windowMenu' }
        ];
  Menu.setApplicationMenu(Menu.buildFromTemplate(template));
}

app.whenReady().then(() => {
  // Unpackaged, macOS would show Electron's own icon in the Dock.
  if (existsSync(Icon)) {
    app.dock?.setIcon(Icon);
  }

  // No camera, microphone, notifications or anything else: the editor needs none of it.
  session.defaultSession.setPermissionRequestHandler((_contents, _permission, callback) => callback(false));
  serveEditor();
  handleIpc();
  buildMenu();
  createWindow();
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createWindow();
    }
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit();
  }
});

app.on('before-quit', event => {
  if (quitting || !sidecars) {
    return;
  }

  // Stop the engine cleanly (the host releases the audio device and deletes its socket), then really quit.
  event.preventDefault();
  quitting = true;
  void sidecars.stop().finally(() => app.quit());
});
