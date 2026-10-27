import { contextBridge, ipcRenderer } from 'electron';

// The only surface the sandboxed renderer gets (typed in apps/editor/src/app/core/desktop.ts).
contextBridge.exposeInMainWorld('cathode', {
  platform: process.platform,
  onFullScreen: (listener: (fullScreen: boolean) => void) => {
    ipcRenderer.on('cathode:fullScreen', (_event, fullScreen: boolean) => listener(fullScreen === true));
  },
  onOpenSettings: (listener: () => void) => {
    ipcRenderer.on('cathode:openSettings', () => listener());
  },
  onQuit: (mayQuit: () => boolean | Promise<boolean>) => {
    ipcRenderer.on('cathode:mayQuit', async (_event, id: number) => {
      let ok = true;
      try {
        ok = (await mayQuit()) !== false;
      } catch {
        // an editor that cannot answer does not keep the app open
      }

      ipcRenderer.send('cathode:mayQuit:reply', id, ok);
    });
    ipcRenderer.send('cathode:mayQuit:ready');
  },
  connection: () => ipcRenderer.invoke('cathode:connection'),
  target: () => ipcRenderer.invoke('cathode:target'),
  setTarget: (target: unknown) => ipcRenderer.invoke('cathode:setTarget', target),
  sidecars: () => ipcRenderer.invoke('cathode:sidecars'),
  onSidecars: (listener: (statuses: unknown) => void) => {
    ipcRenderer.on('cathode:sidecars', (_event, statuses: unknown) => listener(statuses));
  },
  logs: () => ipcRenderer.invoke('cathode:sidecars:logs'),
  network: {
    status: () => ipcRenderer.invoke('cathode:network:status'),
    apply: (network: unknown) => ipcRenderer.invoke('cathode:network:apply', network)
  },
  audio: {
    devices: () => ipcRenderer.invoke('cathode:audio:devices'),
    settings: () => ipcRenderer.invoke('cathode:audio:settings'),
    apply: (settings: unknown) => ipcRenderer.invoke('cathode:audio:apply', settings),
    latencyTest: () => ipcRenderer.invoke('cathode:audio:latencyTest'),
    irCapture: (options: unknown) => ipcRenderer.invoke('cathode:audio:irCapture', options)
  }
});
