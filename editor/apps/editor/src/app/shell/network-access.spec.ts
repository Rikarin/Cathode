import { TestBed } from '@angular/core/testing';
import { ConnectionService } from '../core/connection';
import type { NetworkStatus } from '../core/desktop';
import { Notify } from '../core/notify';
import { NetworkAccess } from './network-access';

describe('NetworkAccess', () => {
  const off: NetworkStatus = { enabled: false, port: 5180, token: 'secret', urls: [], qr: null, error: null };
  const on: NetworkStatus = { ...off, enabled: true, port: 6000, urls: ['http://192.168.1.20:6000'] };

  /** Lets the promises already resolved run their continuations. */
  const settle = () => new Promise(resolve => setTimeout(resolve));

  function render(desktop: boolean, applied: NetworkStatus | Error = on) {
    const status = vi.fn(() => Promise.resolve(off));
    const apply = vi.fn((_network: { enabled: boolean; port: number; renewToken?: boolean }) =>
      applied instanceof Error ? Promise.reject(applied) : Promise.resolve(applied)
    );
    const success = vi.fn();
    const error = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        { provide: ConnectionService, useValue: { desktop: desktop ? { network: { status, apply } } : null } },
        {
          provide: Notify,
          useValue: {
            success,
            error,
            run: async (action: () => Promise<unknown>) => {
              try {
                return await action();
              } catch (failure) {
                error(failure);
                return undefined;
              }
            }
          }
        }
      ]
    });
    TestBed.overrideComponent(NetworkAccess, { set: { template: '', imports: [] } });
    const fixture = TestBed.createComponent(NetworkAccess);
    fixture.detectChanges();
    const access = fixture.componentInstance as unknown as {
      status(): NetworkStatus | null;
      port: { (): number; set(port: number): void };
      applying(): boolean;
      apply(enabled: boolean, port: number, renewToken?: boolean): Promise<void>;
      copy(text: string, what: 'address' | 'access token'): Promise<void>;
    };
    return { access, status, apply, success, error };
  }

  afterEach(() => vi.unstubAllGlobals());

  it('shows what the desktop app says is in use', async () => {
    const { access, status } = render(true);
    await settle();

    expect(status).toHaveBeenCalledTimes(1);
    expect(access.status()).toEqual(off);
    expect(access.port()).toBe(5180);
  });

  it('shows nothing and asks nothing in a browser, which has no Server of its own to open', async () => {
    const { access, apply } = render(false);
    await settle();
    expect(access.status()).toBeNull();

    await access.apply(true, 5180);
    expect(apply).not.toHaveBeenCalled();
    expect(access.applying()).toBe(false);
  });

  it('applies the switch and the port typed, and shows what the Server then uses', async () => {
    const { access, apply } = render(true);
    await settle();
    access.port.set(6000);

    const applying = access.apply(true, access.port());
    expect(access.applying()).toBe(true);
    await applying;

    expect(apply).toHaveBeenCalledWith({ enabled: true, port: 6000, renewToken: false });
    expect(access.status()).toEqual(on);
    expect(access.port()).toBe(6000);
    expect(access.applying()).toBe(false);
  });

  it('asks for a new token on the port in use', async () => {
    const { access, apply } = render(true, { ...on, token: 'fresh' });
    await settle();

    await access.apply(true, 6000, true);

    expect(apply).toHaveBeenCalledWith({ enabled: true, port: 6000, renewToken: true });
    expect(access.status()?.token).toBe('fresh');
  });

  it('goes back to what is really in use when the change is refused', async () => {
    const { access, status, error } = render(true, new Error('Port 80 is not allowed'));
    await settle();
    access.port.set(80);

    await access.apply(true, 80);

    expect(error).toHaveBeenCalledTimes(1);
    expect(status).toHaveBeenCalledTimes(2);
    expect(access.status()).toEqual(off);
    expect(access.port()).toBe(5180);
    expect(access.applying()).toBe(false);
  });

  it('copies an address or the token, and says so', async () => {
    const { access, success } = render(true);
    const writeText = vi.fn(() => Promise.resolve());
    vi.stubGlobal('navigator', { clipboard: { writeText } });

    await access.copy('secret', 'access token');

    expect(writeText).toHaveBeenCalledWith('secret');
    expect(success).toHaveBeenCalledWith('Copied the access token.');
  });

  it('says why when the clipboard is not available (a page that is not a secure context)', async () => {
    const { access, success, error } = render(true);
    vi.stubGlobal('navigator', {});

    await access.copy('http://192.168.1.20:6000', 'address');

    expect(success).not.toHaveBeenCalled();
    expect(error).toHaveBeenCalledTimes(1);
  });
});
