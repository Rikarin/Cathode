import { TestBed } from '@angular/core/testing';
import type { FrequencyResponse, PresetBlock } from '@cathode/api-client';
import { ConnectionService } from '../../core/connection';
import { LiveStore } from '../../state/live.store';
import { EqResponse, RefetchDelayMs, showsEqResponse } from './eq-response';

const geq = (values: Record<string, number> = {}): PresetBlock => ({
  id: 'geq1',
  type: 'geq',
  row: 0,
  column: 0,
  assets: {},
  channels: { A: values },
  bypassMode: 'thru',
  sceneIgnore: []
});

const response = (db: number): FrequencyResponse => ({
  frequencies: [20, 1_000, 20_000],
  db: [0, db, 0],
  centers: [1_000]
});

/** A GET that answers when the test says so, in any order. */
function server() {
  const calls: { resolve: (body: FrequencyResponse) => void }[] = [];
  const get = vi.fn(
    () =>
      new Promise(resolve =>
        calls.push({ resolve: body => resolve({ data: body, response: new Response(null, { status: 200 }) }) })
      )
  );
  return { get, calls };
}

function render(get: ReturnType<typeof server>['get']) {
  TestBed.configureTestingModule({
    providers: [
      { provide: ConnectionService, useValue: { client: { GET: get } } },
      { provide: LiveStore, useValue: { channelOf: () => 'A' } }
    ]
  });
  // jsdom has no canvas for Konva: the stage is swapped for the points it would be given.
  TestBed.overrideComponent(EqResponse, {
    set: { imports: [], template: '<span data-testid="curve">{{ curve().points.join(" ") }}</span>' }
  });
  const fixture = TestBed.createComponent(EqResponse);
  fixture.componentRef.setInput('block', geq());
  fixture.detectChanges();
  const panel = fixture.componentInstance as unknown as {
    response: () => FrequencyResponse | null;
    curve: () => { points: number[] };
    centres: () => unknown[];
  };
  return { fixture, panel };
}

/** Lets the debounce run out and any answered request land. */
async function settle(fixture: { detectChanges: () => void }) {
  fixture.detectChanges();
  await vi.advanceTimersByTimeAsync(RefetchDelayMs);
  fixture.detectChanges();
}

describe('EqResponse', () => {
  beforeAll(() => {
    // jsdom has no ResizeObserver; the tile only needs one to exist.
    if (!('ResizeObserver' in globalThis)) {
      globalThis.ResizeObserver = class {
        observe = () => undefined;
        disconnect = () => undefined;
      } as unknown as typeof ResizeObserver;
    }
  });

  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('fetches the block response from the Server and draws it', async () => {
    const { get, calls } = server();
    const { fixture, panel } = render(get);

    await settle(fixture);
    expect(get).toHaveBeenCalledWith('/api/v1/live/blocks/{id}/response', { params: { path: { id: 'geq1' } } });
    expect(panel.curve().points).toEqual([]);

    calls[0]?.resolve(response(6));
    await settle(fixture);
    expect(panel.response()?.db).toEqual([0, 6, 0]);
    expect(panel.curve().points).toHaveLength(6); // three points, x and y
    const [, flat = 0, , boosted = 0] = panel.curve().points;
    expect(boosted).toBeLessThan(flat); // up the stage
    expect(panel.centres()).toHaveLength(1);
    expect((fixture.nativeElement as HTMLElement).textContent.trim().split(' ')).toHaveLength(6);
  });

  it('asks again once the values rest, keeping the last curve meanwhile', async () => {
    const { get, calls } = server();
    const { fixture, panel } = render(get);
    await settle(fixture);
    calls[0]?.resolve(response(3));
    await settle(fixture);

    fixture.componentRef.setInput('block', geq({ g1k: 4 }));
    fixture.detectChanges();
    fixture.componentRef.setInput('block', geq({ g1k: 6 }));
    await settle(fixture);

    expect(get).toHaveBeenCalledTimes(2); // the two changes were one request
    expect(panel.response()?.db[1]).toBe(3);
  });

  it('drops an answer that a newer request overtook', async () => {
    const { get, calls } = server();
    const { fixture, panel } = render(get);
    await settle(fixture);

    fixture.componentRef.setInput('block', geq({ g1k: 6 }));
    await settle(fixture);
    calls[1]?.resolve(response(6));
    await settle(fixture);
    calls[0]?.resolve(response(0)); // the first request, answered last
    await settle(fixture);

    expect(get).toHaveBeenCalledTimes(2);
    expect(panel.response()?.db[1]).toBe(6);
  });

  it('keeps the last curve when a read fails', async () => {
    const { get, calls } = server();
    const { fixture, panel } = render(get);
    await settle(fixture);
    calls[0]?.resolve(response(6));
    await settle(fixture);

    get.mockImplementationOnce(() => Promise.reject(new Error('offline')));
    fixture.componentRef.setInput('block', geq({ g1k: 2 }));
    await settle(fixture);

    expect(panel.response()?.db[1]).toBe(6);
  });
});

describe('showsEqResponse', () => {
  it('puts the Graphic EQ on its first page', () => {
    expect(showsEqResponse('geq', null, [])).toBe(true);
    expect(showsEqResponse('geq', 'Basic', ['Basic', 'More'])).toBe(true);
    expect(showsEqResponse('geq', 'More', ['Basic', 'More'])).toBe(false);
  });

  it("puts the Drive and the Amp on their EQ's own page", () => {
    const drive = ['Basic', 'Tone', 'Graphic EQ'];
    expect(showsEqResponse('drive', 'Graphic EQ', drive)).toBe(true);
    expect(showsEqResponse('drive', 'Basic', drive)).toBe(false);
    const amp = ['Basic', 'Preamp', 'EQ', 'Dynamics'];
    expect(showsEqResponse('circuitamp', 'EQ', amp)).toBe(true);
    expect(showsEqResponse('circuitamp', 'Basic', amp)).toBe(false);
  });

  it('shows nothing for a block without a graphic EQ', () => {
    expect(showsEqResponse('reverb', 'Basic', ['Basic'])).toBe(false);
    expect(showsEqResponse('peq', null, [])).toBe(false);
  });
});
