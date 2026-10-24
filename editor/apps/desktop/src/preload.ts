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
  connection: () => ipcRenderer.invoke('cathode:connection'),
  target: () => ipcRenderer.invoke('cathode:target'),
  setTarget: (target: unknown) => ipcRenderer.invoke('cathode:setTarget', target),
  sidecars: () => ipcRenderer.invoke('cathode:sidecars'),
  audio: {
    devices: () => ipcRenderer.invoke('cathode:audio:devices'),
    settings: () => ipcRenderer.invoke('cathode:audio:settings'),
    apply: (settings: unknown) => ipcRenderer.invoke('cathode:audio:apply', settings),
    latencyTest: () => ipcRenderer.invoke('cathode:audio:latencyTest'),
    irCapture: (options: unknown) => ipcRenderer.invoke('cathode:audio:irCapture', options)
  }
});
