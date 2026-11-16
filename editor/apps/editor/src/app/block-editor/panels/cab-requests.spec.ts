import { type Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { type PresetBlock } from '@cathode/api-client';
import { ConnectionService } from '../../core/connection';
import { Notify } from '../../core/notify';
import { LiveStore } from '../../state/live.store';
import { CabAlign } from './cab-align';
import { CabResonanceHelper } from './cab-resonance-helper';

// The Cab panel's two helpers that talk to the Server while the player works: the order their answers land in must
// not decide what they show or leave playing.

/** A Server whose answers wait until the test lets each one go, in whatever order it likes. */
function server() {
  const requests: { method: string; path: string; id: string; body: unknown; answer: (data: unknown) => void }[] = [];
  const call = (method: string) => (path: string, init: { params: { path: { id: string } }; body?: unknown }) =>
    new Promise(resolve =>
      requests.push({
        method,
        path,
        id: init.params.path.id,
        body: init.body,
        answer: data => resolve({ data, response: new Response(null, { status: 200 }) })
      })
    );
  return { requests, client: { GET: call('GET'), PUT: call('PUT') } };
}

const settle = () => new Promise(resolve => setTimeout(resolve));
const block = (id: string): PresetBlock =>
  ({ id, type: 'cab', row: 2, column: 4, channels: {}, assets: {} }) as PresetBlock;

function render<T>(component: Type<T>, client: unknown, first: PresetBlock) {
  TestBed.configureTestingModule({
    providers: [
      { provide: ConnectionService, useValue: { client } },
      { provide: LiveStore, useValue: { channelOf: () => 'A', setParam: () => undefined } },
      { provide: Notify, useValue: { run: (action: () => Promise<unknown>) => action(), success: () => undefined } }
    ]
  });
  TestBed.overrideComponent(component, { set: { template: '', imports: [] } });
  const fixture = TestBed.createComponent(component);
  fixture.componentRef.setInput('block', first);
  fixture.detectChanges();
  return fixture;
}

describe('the Cab resonance helper', () => {
  it('sends its test-tone requests one at a time, so the closing "off" is the last to land (#629)', async () => {
    const { requests, client } = server();
    const fixture = render(CabResonanceHelper, client, block('cab1'));
    const helper = fixture.componentInstance as unknown as {
      start(): Promise<void>;
      sweep(position: number): void;
      stop(): Promise<void>;
    };

    void helper.start();
    await settle();
    requests.shift()?.answer({ baseHz: 100, hz: 120, percent: 100 }); // the speaker resonance
    await settle();
    expect(requests.map(r => r.body)).toEqual([{ enabled: true, frequency: 120, level: -24 }]);

    helper.sweep(0.2);
    helper.sweep(0.8); // both wait behind the first "on"
    const stopped = helper.stop();
    await settle();
    expect(requests).toHaveLength(1); // nothing else goes out until it has landed

    const after: unknown[] = [];
    while (requests.length > 0) {
      const next = requests.shift();
      after.push(next?.body);
      next?.answer(null);
      await settle();
      expect(requests.length).toBeLessThanOrEqual(1); // still one at a time
    }

    await stopped;
    // The sweeps queued behind the first "on" were dropped by the stop, and the "off" went last.
    expect(after.slice(1).map(b => (b as { enabled: boolean }).enabled)).toEqual([false]);
  });

  it('ends with the tone off: the last request sent is the "off"', async () => {
    const { requests, client } = server();
    const fixture = render(CabResonanceHelper, client, block('cab1'));
    const helper = fixture.componentInstance as unknown as {
      start(): Promise<void>;
      sweep(position: number): void;
      stop(): Promise<void>;
    };
    const bodies: unknown[] = [];

    void helper.start();
    await settle();
    requests.shift()?.answer({ baseHz: 100, hz: 120, percent: 100 });
    await settle();
    helper.sweep(0.2);
    void helper.stop();
    while (requests.length > 0) {
      const next = requests.shift();
      bodies.push(next?.body);
      next?.answer(null);
      await settle();
    }

    expect(bodies.at(-1)).toMatchObject({ enabled: false });
    expect(bodies.filter(b => !(b as { enabled: boolean }).enabled)).toHaveLength(1);
  });
});

describe('the Cab Align view', () => {
  it('shows the block asked for last, whatever order the answers land in (#630)', async () => {
    const { requests, client } = server();
    const slot = (delayMs: number) => ({
      slot: 0,
      plays: true,
      audible: true,
      onsetMs: 0,
      autoDelayMs: 0,
      delayMs,
      head: []
    });
    const fixture = render(CabAlign, client, block('cabA'));
    await settle();
    fixture.componentRef.setInput('block', block('cabB'));
    fixture.detectChanges();
    await settle();
    const [a, b] = requests;
    expect([a?.id, b?.id]).toEqual(['cabA', 'cabB']);

    b?.answer({ sampleRate: 48_000, slots: [slot(2)] });
    await settle();
    a?.answer({ sampleRate: 44_100, slots: [slot(1)] }); // the older request answers last
    await settle();

    const shown = (fixture.componentInstance as unknown as { slots: () => { delayMs: number }[] }).slots();
    expect(shown.map(s => s.delayMs)).toEqual([2]);
  });
});
