import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ConnectionService } from '../core/connection';
import { Notify } from '../core/notify';
import { GlobalStore } from '../state/global.store';
import { LiveStore } from '../state/live.store';
import { ControllersPage, scopeName } from './controllers-page';

describe('scopeName', () => {
  it('names a modifier scope', () => {
    expect(scopeName(null)).toBe('All channels');
    expect(scopeName('all')).toBe('All channels');
    expect(scopeName('B')).toBe('Channel B only');
  });
});

describe('ControllersPage externals', () => {
  function render() {
    const put = vi.fn(() => Promise.resolve({ response: new Response(null, { status: 204 }) }));
    TestBed.configureTestingModule({
      providers: [
        { provide: LiveStore, useValue: { preset: signal(undefined), activeScene: signal(0) } },
        { provide: GlobalStore, useValue: { settings: signal(undefined) } },
        { provide: ConnectionService, useValue: { client: { PUT: put } } },
        { provide: Notify, useValue: { run: (action: () => Promise<unknown>) => action() } }
      ]
    });
    TestBed.overrideComponent(ControllersPage, { set: { template: '', imports: [] } });
    const fixture = TestBed.createComponent(ControllersPage);
    fixture.detectChanges();
    const page = fixture.componentInstance as unknown as {
      externals: () => readonly number[];
      setExternal: (index: number, value: number) => Promise<void>;
    };
    return { page, put };
  }

  it('offers External 1 to 8, at rest until set', () => {
    const { page } = render();
    expect(page.externals()).toEqual([0, 0, 0, 0, 0, 0, 0, 0]);
  });

  it('drives an External through PUT /live/external/{index} and keeps the value it sent', async () => {
    const { page, put } = render();
    await page.setExternal(0, 0.5);
    await page.setExternal(7, 1.4);

    expect(put).toHaveBeenCalledWith('/api/v1/live/external/{index}', {
      params: { path: { index: 0 } },
      body: { value: 0.5 }
    });
    expect(put).toHaveBeenCalledWith('/api/v1/live/external/{index}', {
      params: { path: { index: 7 } },
      body: { value: 1 }
    });
    expect(page.externals()).toEqual([0.5, 0, 0, 0, 0, 0, 0, 1]);
  });
});
