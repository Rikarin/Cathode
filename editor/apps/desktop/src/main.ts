import {
  BrowserWindow,
  Menu,
  type MenuItemConstructorOptions,
  app,
  ipcMain,
  net,
  protocol,
  session,
  shell,
  systemPreferences
} from 'electron';
import { existsSync, statSync } from 'node:fs';
import { extname, join, normalize, resolve, sep } from 'node:path';
import { pathToFileURL } from 'node:url';
import { type MicrophoneAccess, MicrophoneSettingsUrl, microphoneAccess, requestMicrophone } from './microphone';
import { networkStatus, newToken, portFree } from './network';
import { FileInbox, isPresetFile, presetArguments, readPreset } from './open-files';
import {
  type AudioSettings,
  type NetworkSettings,
  type ServerTarget,
  defaultSettings,
  loadSettings,
  saveSettings
} from './settings';
import { type Connection, type IrCaptureOptions, Sidecars, listDevices, waitForServer } from './sidecars';
import { type Release, checkForUpdate } from './updates';

// Cathode desktop: the editor in a sandboxed window, and the engine (cathode-host + cathode-server) as supervised
// sidecars on loopback (and, when the user allows control from other devices, on the network too), or a remote Server
// (docs/02-architecture.md §7).

app.setName('Cathode');
if (process.env['CATHODE_USER_DATA']) {
  app.setPath('userData', process.env['CATHODE_USER_DATA']); // tests run with throwaway settings
}

/**
 * One Cathode at a time (per settings folder): a second would start a second engine on the same audio interface and a
 * second Server on the same presets. Launching it again brings the running one to the front instead.
 */
const primary = app.requestSingleInstanceLock();
if (!primary) {
  app.quit();
}

/** The renderer comes from the built editor through this scheme; nothing else is served from disk. */
const Scheme = 'app';
/** The app icon (apps/desktop/assets/icon.png), copied next to main.js by the build. */
const Icon = join(__dirname, 'icon.png');
const editorRoot = resolve(
  process.env['CATHODE_EDITOR_DIR'] ?? join(__dirname, app.isPackaged ? 'editor' : '../editor/browser')
);
const devServer = process.env['CATHODE_EDITOR_URL']; // `nx serve editor` during development
/** The same build for cathode-server to serve on the network: it cannot read inside the asar archive. */
const editorFiles = editorRoot.replace(`${sep}app.asar${sep}`, `${sep}app.asar.unpacked${sep}`);

/**
 * Cathode's licence and the notices and licences of the third-party code in the app (THIRD-PARTY-NOTICES.md), which
 * the package carries in its resources (scripts/package.mjs). Not there when run from the source tree.
 */
const licences = join(process.resourcesPath, 'licenses');

protocol.registerSchemesAsPrivileged([
  { scheme: Scheme, privileges: { standard: true, secure: true, supportFetchAPI: true, corsEnabled: true } }
]);

/** The update check: a little after the start, so it does not compete with it, and then once a day. */
const FirstUpdateCheckMs = 30_000;
const UpdateCheckEveryMs = 24 * 60 * 60 * 1_000;

/** How long to wait for a remote Server before the editor reports it unreachable. */
const RemoteTimeoutMs = 10_000;

/** Read once the app is ready (safeStorage decrypts the tokens only then); nothing uses them before. */
let settings = defaultSettings();
let sidecars: Sidecars | null = null;
let window: BrowserWindow | null = null;
let quitting = false;
/** Whether the window's editor answers "may the app quit?" (it says so once loaded; a reload starts over). */
let quitGuard = false;
/** The pending question, so a second ⌘Q or close while the editor asks does not ask twice. */
let askingToQuit: Promise<boolean> | null = null;
let quitQuestions = 0;
const quitAnswers = new Map<number, (ok: boolean) => void>();
/** A newer release than this app, once a check has found one: the menu then offers its page. */
let update: Release | null = null;
/**
 * Preset files (.cthpreset) the OS asked the app to open: double-clicked, dropped on the Dock icon or given on the
 * command line. They wait here until the editor has connected, and it imports them into the library.
 */
const presetFiles = new FileInbox();
/** Whether startup has opened the window (the editor is served, the settings are read): before that nothing else may. */
let started = false;

/** Shows the window, or opens one when macOS kept the app running without any. */
function showWindow(): void {
  if (window) {
    if (window.isMinimized()) {
      window.restore();
    }

    window.show();
    window.focus();
  } else if (started) {
    createWindow();
  }
}

function openPresets(paths: readonly string[]): void {
  for (const path of paths.filter(isPresetFile)) {
    presetFiles.add(path);
  }

  if (paths.length > 0) {
    showWindow();
  }
}

// macOS sends the file as an event, at any time from before the app is ready.
app.on('open-file', (event, path) => {
  event.preventDefault();
  openPresets([path]);
});

/**
 * Asks the editor whether the app may quit: with unsaved edits it asks the person, as loading a preset does, since the
 * working copy lives in the local Server and goes with it. A remote Server keeps it, so then nobody is asked; nor when
 * there is no editor to ask.
 */
function mayQuit(): Promise<boolean> {
  const contents = window?.webContents;
  if (!sidecars || !contents || contents.isDestroyed() || !quitGuard) {
    return Promise.resolve(true);
  }

  askingToQuit ??= new Promise<boolean>(done => {
    const id = ++quitQuestions;
    const gone = () => answer(true);
    const answer = (ok: boolean) => {
      quitAnswers.delete(id);
      contents.off('render-process-gone', gone);
      contents.off('destroyed', gone);
      done(ok);
    };
    quitAnswers.set(id, answer);
    contents.once('render-process-gone', gone);
    contents.once('destroyed', gone);
    contents.send('cathode:mayQuit', id);
  }).finally(() => (askingToQuit = null));
  return askingToQuit;
}

/**
 * Whether the engine host was last started on an audio interface without microphone access (macOS): its inputs are
 * then silent, and it is restarted once the access is there (microphoneStatus).
 */
let hostLacksMicrophone = false;

/**
 * Before the engine host opens an audio interface: on macOS, shows the microphone prompt if the user has never
 * answered it, so the host starts with the answer given. A refusal does not stop the engine: the outputs still play,
 * and Audio setup says why the inputs are silent. Without a device (the null backend) nothing is asked.
 */
async function prepareMicrophone(): Promise<void> {
  if (!settings.audio.device) {
    hostLacksMicrophone = false;
    return;
  }

  let access: MicrophoneAccess = 'not-determined';
  try {
    access = await requestMicrophone(process.platform, systemPreferences);
  } catch (error) {
    console.warn('Cathode: could not ask for microphone access', error);
  }

  hostLacksMicrophone = access !== 'granted';
}

/**
 * The microphone access as it stands, for Audio setup. When the user has granted it since the host started without,
 * the host is restarted so its inputs come alive.
 */
function microphoneStatus(): MicrophoneAccess {
  const access = microphoneAccess(process.platform, systemPreferences);
  if (access === 'granted' && hostLacksMicrophone) {
    hostLacksMicrophone = false;
    if (sidecars && settings.audio.device) {
      sidecars
        .applyAudio(settings.audio)
        .catch((error: unknown) => console.error('Cathode: could not restart the engine host', error));
    }
  }

  return access;
}

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

  sidecars ??= new Sidecars(
    settings.audio,
    settings.network,
    editorFiles,
    process.env['CATHODE_DATA_DIR'] ?? null,
    statuses => window?.webContents.send('cathode:sidecars', statuses)
  );
  const local = sidecars;
  // If the target changed while macOS asked, these sidecars are no longer the ones to start.
  return prepareMicrophone().then(() => (sidecars === local ? local.start() : connection()));
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
  // Closing the window quits on Windows and Linux: ask first, as quitting does (macOS keeps the app and its engine).
  let mayClose = false;
  window.on('close', event => {
    if (quitting || mayClose || process.platform === 'darwin' || !sidecars) {
      return;
    }

    event.preventDefault();
    void mayQuit().then(ok => {
      if (ok) {
        mayClose = true;
        window?.close();
      }
    });
  });
  window.webContents.on('did-navigate', () => {
    quitGuard = false;
    presetFiles.closed();
  });
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
      shell.openExternal(url).catch((error: unknown) => console.error('Cathode: could not open', url, error));
    }

    return { action: 'deny' };
  });
  window.on('closed', () => {
    window = null;
    presetFiles.closed();
  });
  window
    .loadURL(devServer ?? `${Scheme}://editor/index.html`)
    .catch((error: unknown) => console.error('Cathode: the editor did not load', error));
}

function handleIpc(): void {
  ipcMain.on('cathode:mayQuit:ready', event => {
    if (event.sender === window?.webContents) {
      quitGuard = true;
    }
  });
  ipcMain.on('cathode:mayQuit:reply', (_event, id: unknown, ok: unknown) => {
    if (typeof id === 'number') {
      quitAnswers.get(id)?.(ok === true);
    }
  });
  // The editor has connected and takes preset files now: each is read here (the sandboxed window cannot) and sent.
  ipcMain.on('cathode:openPreset:ready', event => {
    const contents = event.sender;
    if (contents === window?.webContents) {
      presetFiles.ready(
        path =>
          void readPreset(path).then(preset => {
            if (!contents.isDestroyed()) {
              contents.send('cathode:openPreset', preset);
            }
          })
      );
    }
  });
  ipcMain.handle('cathode:connection', () => connection());
  ipcMain.handle('cathode:target', () => settings.target);
  ipcMain.handle('cathode:setTarget', async (_event, target: unknown) => {
    settings = { ...settings, target: validTarget(target) };
    await saveSettings(settings);
    if (settings.target.mode === 'remote') {
      await sidecars?.stop();
      sidecars = null;
    }

    window?.reload();
  });
  ipcMain.handle('cathode:sidecars', () => sidecars?.statuses() ?? []);
  ipcMain.handle('cathode:sidecars:logs', () => sidecars?.logs() ?? { host: [], server: [] });
  ipcMain.handle('cathode:audio:devices', () => listDevices());
  ipcMain.handle('cathode:audio:settings', () => settings.audio);
  ipcMain.handle('cathode:audio:apply', async (_event, audio: unknown) => {
    settings = { ...settings, audio: validAudio(audio) };
    await saveSettings(settings);
    await prepareMicrophone();
    await sidecars?.applyAudio(settings.audio);
  });
  ipcMain.handle('cathode:audio:microphone', () => microphoneStatus());
  ipcMain.handle('cathode:audio:microphone:settings', async () => {
    if (process.platform === 'darwin') {
      await shell.openExternal(MicrophoneSettingsUrl);
    }
  });
  ipcMain.handle('cathode:audio:latencyTest', () =>
    sidecars ? sidecars.latencyTest() : { ok: false, output: 'Not running a local engine.', roundTripMs: null }
  );
  ipcMain.handle('cathode:network:status', () => networkStatus(settings.network, sidecars?.networkError ?? null));
  ipcMain.handle('cathode:network:apply', async (_event, network: unknown) => {
    const next = await validNetwork(network);
    settings = { ...settings, network: next };
    await saveSettings(settings);
    // The Server restarts with the new listeners and token; the window reloads to take the token.
    if (sidecars && settings.target.mode === 'local') {
      await sidecars.applyNetwork(settings.network);
      window?.reload();
    }

    return networkStatus(settings.network, sidecars?.networkError ?? null);
  });
  ipcMain.handle('cathode:audio:irCapture', (_event, options: unknown) =>
    sidecars
      ? sidecars.irCapture(validCapture(options))
      : { ok: false, output: 'Not running a local engine.', wav: null }
  );
}

/** What the renderer sent for a `T`: an object whose fields are all still to be checked. */
type Unchecked<T> = { readonly [K in keyof T]?: unknown };

/** The renderer is trusted less than main: everything it sends is checked, starting with its being an object. */
function fields(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : {};
}

function isWholeIn(value: unknown, min: number, max: number): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= min && value <= max;
}

function validAudio(value: unknown): AudioSettings {
  const audio: Unchecked<AudioSettings> = fields(value);
  const oneOf = (option: unknown, options: readonly number[], fallback: number) =>
    options.find(o => o === option) ?? fallback;
  const device = typeof audio.device === 'string' && audio.device.length < 512 ? audio.device : null;
  const sampleRate = oneOf(audio.sampleRate, [44_100, 48_000, 88_200, 96_000], 48_000);
  const bufferFrames = oneOf(audio.bufferFrames, [32, 64, 128, 256, 512], 64);
  const channels = (list: unknown): number[] =>
    Array.isArray(list) ? (list as unknown[]).filter(c => isWholeIn(c, 1, 64)).slice(0, 2) : [];
  // Outputs 1-4: the first channel of each pair, 0 for unmapped.
  const outputs = Array.isArray(audio.outputs)
    ? (audio.outputs as unknown[]).map(c => (isWholeIn(c, 0, 63) ? c : 0)).slice(0, 4)
    : [1];
  const inputs = Array.isArray(audio.inputs) ? (audio.inputs as unknown[]).slice(0, 6).map(channels) : [];
  const [first = [], ...rest] = inputs;
  return {
    device,
    sampleRate,
    bufferFrames,
    loopIn: channels(audio.loopIn),
    loopOut: channels(audio.loopOut),
    outputs,
    diOut: channels(audio.diOut),
    inputs: first.length > 0 ? inputs : [[1], ...rest]
  };
}

function validCapture(value: unknown): IrCaptureOptions {
  const options: Unchecked<IrCaptureOptions> = fields(value);
  const clamp = (number: unknown, min: number, max: number, fallback: number) =>
    typeof number === 'number' && Number.isFinite(number) ? Math.min(max, Math.max(min, number)) : fallback;
  return {
    seconds: clamp(options.seconds, 1, 30, 5),
    levelDb: clamp(options.levelDb, -60, 0, -18),
    lengthMs: clamp(options.lengthMs, 20, 2_000, 500)
  };
}

/** What the renderer asks for: on or off, the port, and optionally a new token. */
type NetworkRequest = Pick<NetworkSettings, 'enabled' | 'port'> & { renewToken?: boolean };

/**
 * A port of 1024 and up, and a token made the first time the setting goes on, or anew when asked (`renewToken`), which
 * signs every other device out. A port that is taken now is refused here, so the setting cannot be saved broken.
 */
async function validNetwork(value: unknown): Promise<NetworkSettings> {
  const network: Unchecked<NetworkRequest> = fields(value);
  const enabled = network.enabled === true;
  const port = isWholeIn(network.port, 1024, 65_535) ? network.port : null;
  if (port === null) {
    throw new Error('The port must be a whole number from 1024 to 65535.');
  }

  const current = settings.network;
  const listening = current.enabled && current.port === port && sidecars?.networkError === null;
  if (enabled && !listening && !(await portFree(port))) {
    throw new Error(`Port ${port} is in use by another program. Choose another port.`);
  }

  const token = network.renewToken === true || !current.token ? newToken() : current.token;
  return { enabled, port, token };
}

function validTarget(value: unknown): ServerTarget {
  const target: Unchecked<{ mode: string; baseUrl: string; token: string }> = fields(value);
  if (target.mode === 'remote') {
    const url = new URL(typeof target.baseUrl === 'string' ? target.baseUrl : '');
    if (url.protocol !== 'http:' && url.protocol !== 'https:') {
      throw new Error('The Server URL must be http or https.');
    }

    return { mode: 'remote', baseUrl: url.origin, token: typeof target.token === 'string' ? target.token : '' };
  }

  return { mode: 'local' };
}

/**
 * Asks GitHub whether a newer release exists (updates.ts) and puts it in the menu. Nothing is downloaded or installed:
 * the menu item opens the release's page. Only a packaged app checks (a build from the source tree has no release to
 * compare with), and only while the setting is on. A check that fails is tried again the next day.
 */
async function checkUpdates(): Promise<void> {
  if (!app.isPackaged || !settings.updates.check) {
    return;
  }

  try {
    const found = await checkForUpdate(app.getVersion(), (url, init) => net.fetch(url as string, init));
    if (found?.version !== update?.version) {
      update = found;
      buildMenu();
    }
  } catch (error) {
    console.warn('Cathode: could not check for a newer release', error);
  }
}

async function setUpdateCheck(check: boolean): Promise<void> {
  settings = { ...settings, updates: { check } };
  if (!check) {
    update = null;
  }

  buildMenu();
  await saveSettings(settings);
  await checkUpdates();
}

/**
 * The application menu: Electron's default menus, with Settings… (⌘,) where a Mac app keeps it, in the app menu
 * (and in File elsewhere). It asks the editor to open its Settings dialog. A newer release, when a check found one, and
 * the switch for that check are under About on macOS and in Help elsewhere; Help also opens the folder of licences the
 * package carries.
 */
function buildMenu(): void {
  const openSettings: MenuItemConstructorOptions = {
    label: 'Settings…',
    accelerator: 'CmdOrCtrl+,',
    click: () => window?.webContents.send('cathode:openSettings')
  };
  const found = update;
  const updates: MenuItemConstructorOptions[] = [
    ...(found
      ? [
          {
            label: `Cathode ${found.version} is Available…`,
            click: () =>
              void shell
                .openExternal(found.url)
                .catch((error: unknown) => console.error('Cathode: could not open', found.url, error))
          }
        ]
      : []),
    {
      label: 'Check for Updates Automatically',
      type: 'checkbox',
      checked: settings.updates.check,
      click: item =>
        void setUpdateCheck(item.checked).catch((error: unknown) =>
          console.error('Cathode: could not save the update setting', error)
        )
    }
  ];
  const mac = process.platform === 'darwin';
  const helpItems: MenuItemConstructorOptions[] = [
    ...(mac ? [] : updates),
    ...(existsSync(licences)
      ? [
          {
            label: 'Open Source Licences',
            click: () =>
              void shell.openPath(licences).then(error => error && console.error('Cathode: licences:', error))
          }
        ]
      : [])
  ];
  const help: MenuItemConstructorOptions[] = helpItems.length > 0 ? [{ role: 'help', submenu: helpItems }] : [];
  const template: MenuItemConstructorOptions[] = mac
    ? [
        {
          label: 'Cathode',
          submenu: [
            { role: 'about' },
            ...updates,
            { type: 'separator' },
            openSettings,
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
        { role: 'windowMenu' },
        ...help
      ]
    : [
        { label: 'File', submenu: [openSettings, { type: 'separator' }, { role: 'quit' }] },
        { role: 'editMenu' },
        { role: 'viewMenu' },
        { role: 'windowMenu' },
        ...help
      ];
  Menu.setApplicationMenu(Menu.buildFromTemplate(template));
}

// Opening a preset file on Windows or Linux starts the app again with the file as an argument: this one takes it.
app.on('second-instance', (_event, argv, workingDirectory) => {
  showWindow();
  openPresets(presetArguments(argv, workingDirectory));
});

app
  .whenReady()
  .then(async () => {
    if (!primary) {
      return;
    }

    settings = await loadSettings();

    // Unpackaged, macOS would show Electron's own icon in the Dock.
    if (existsSync(Icon)) {
      app.dock?.setIcon(Icon);
    }

    // No camera, microphone, notifications or anything else for the window: the editor needs none of it (the audio
    // goes through the engine host, whose microphone access main asks macOS for: prepareMicrophone). Writing the
    // clipboard (Copy the access token) is the one exception; reading it stays denied.
    session.defaultSession.setPermissionRequestHandler((_contents, permission, callback) =>
      callback(permission === 'clipboard-sanitized-write')
    );
    serveEditor();
    handleIpc();
    buildMenu();
    createWindow();
    started = true;
    setTimeout(() => void checkUpdates(), FirstUpdateCheckMs).unref();
    setInterval(() => void checkUpdates(), UpdateCheckEveryMs).unref();
    openPresets(presetArguments(process.argv, process.cwd()));
    app.on('activate', () => {
      if (BrowserWindow.getAllWindows().length === 0) {
        createWindow();
      }
    });
  })
  .catch((error: unknown) => console.error('Cathode: startup failed', error));

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit();
  }
});

app.on('before-quit', event => {
  if (quitting || !sidecars) {
    return;
  }

  // Ask about unsaved edits; then stop the engine cleanly (the host releases the audio device and deletes its socket)
  // and really quit.
  event.preventDefault();
  void mayQuit().then(ok => {
    if (!ok || quitting) {
      return;
    }

    quitting = true;
    void (sidecars?.stop() ?? Promise.resolve()).finally(() => app.quit());
  });
});
