import { afterEach, describe, expect, it, vi } from 'vitest';
import { developmentSwitch } from './development';

const electron = vi.hoisted(() => ({ app: { isPackaged: false } }));

vi.mock('electron', () => electron);

describe('developmentSwitch', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    electron.app.isPackaged = false;
  });

  it('reads the variable in a development run', () => {
    vi.stubEnv('CATHODE_ENGINE_DIR', '/work/engine');

    expect(developmentSwitch('CATHODE_ENGINE_DIR')).toBe('/work/engine');
  });

  it('is undefined when the variable is unset or empty', () => {
    vi.stubEnv('CATHODE_EDITOR_URL', '');

    expect(developmentSwitch('CATHODE_EDITOR_URL')).toBeUndefined();
    expect(developmentSwitch('CATHODE_EDITOR_DIR')).toBeUndefined();
  });

  it.each(['CATHODE_EDITOR_URL', 'CATHODE_EDITOR_DIR', 'CATHODE_ENGINE_DIR'] as const)(
    'ignores %s in a packaged app',
    name => {
      vi.stubEnv(name, 'http://localhost:4200');
      electron.app.isPackaged = true;

      expect(developmentSwitch(name)).toBeUndefined();
    }
  );
});
