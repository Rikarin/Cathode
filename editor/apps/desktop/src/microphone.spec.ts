import { describe, expect, it } from 'vitest';
import { type MediaAccess, microphoneAccess, requestMicrophone } from './microphone';

/** macOS as the app sees it: a status, and a prompt the user answers with `allow`. */
function mac(status: string, allow = false): MediaAccess & { asked: number } {
  return {
    asked: 0,
    getMediaAccessStatus: () => status,
    askForMediaAccess() {
      this.asked++;
      return Promise.resolve(allow);
    }
  };
}

describe('microphoneAccess', () => {
  it('reports what macOS says', () => {
    expect(microphoneAccess('darwin', mac('granted'))).toBe('granted');
    expect(microphoneAccess('darwin', mac('denied'))).toBe('denied');
    expect(microphoneAccess('darwin', mac('restricted'))).toBe('restricted');
    expect(microphoneAccess('darwin', mac('not-determined'))).toBe('not-determined');
    expect(microphoneAccess('darwin', mac('unknown'))).toBe('not-determined');
  });

  it('is always granted where the OS does not gate audio inputs', () => {
    expect(microphoneAccess('win32', mac('denied'))).toBe('granted');
    expect(microphoneAccess('linux', mac('unknown'))).toBe('granted');
  });
});

describe('requestMicrophone', () => {
  it('asks a user who has never been asked, and reports the answer', async () => {
    const allowed = mac('not-determined', true);
    expect(await requestMicrophone('darwin', allowed)).toBe('granted');
    expect(allowed.asked).toBe(1);

    const refused = mac('not-determined', false);
    expect(await requestMicrophone('darwin', refused)).toBe('denied');
    expect(refused.asked).toBe(1);
  });

  it('does not ask again once macOS has an answer, nor on another OS', async () => {
    for (const [platform, status] of [
      ['darwin', 'granted'],
      ['darwin', 'denied'],
      ['darwin', 'restricted'],
      ['win32', 'not-determined'],
      ['linux', 'not-determined']
    ] as const) {
      const access = mac(status, true);
      await requestMicrophone(platform, access);
      expect(access.asked, `${platform} ${status}`).toBe(0);
    }
  });
});
