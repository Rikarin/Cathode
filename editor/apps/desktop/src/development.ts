import { app } from 'electron';

/** The environment variables that point a development run at another editor or engine. */
export type DevelopmentSwitch = 'CATHODE_EDITOR_URL' | 'CATHODE_EDITOR_DIR' | 'CATHODE_ENGINE_DIR';

/**
 * A development switch from the environment, or undefined when it is not set. A packaged app never honours them: each
 * makes the app load other code (a page with the preload bridge attached, or other binaries as the engine), and that
 * code would run under Cathode's identity and its microphone permission for whoever set the variable.
 */
export function developmentSwitch(name: DevelopmentSwitch): string | undefined {
  return app.isPackaged ? undefined : process.env[name] || undefined;
}
