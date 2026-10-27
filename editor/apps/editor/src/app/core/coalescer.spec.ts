import { Coalescer } from './coalescer';

describe('Coalescer', () => {
  function sender() {
    const sent: [string, number][] = [];
    const answers: (() => void)[] = [];
    const send = (key: string, value: number) => {
      sent.push([key, value]);
      return new Promise<void>(resolve => answers.push(resolve));
    };
    const answer = async () => {
      answers.shift()?.();
      await new Promise(resolve => setTimeout(resolve));
    };
    return { sent, send, answer };
  }

  it('keeps one request in flight per key and sends only the latest value set meanwhile', async () => {
    const { sent, send, answer } = sender();
    const coalescer = new Coalescer<number>(send);

    // A drag: five moves while the first request is in flight.
    for (let value = 1; value <= 5; value++) {
      void coalescer.set('level', value);
    }

    expect(sent).toEqual([['level', 1]]);
    expect(coalescer.pending('level')).toBe(5);
    await answer();
    expect(sent).toEqual([
      ['level', 1],
      ['level', 5]
    ]);
    expect(coalescer.pending('level')).toBe(5);
    await answer();
    expect(sent).toHaveLength(2);
    expect(coalescer.pending('level')).toBeUndefined();
  });

  it('sends different keys independently', () => {
    const { sent, send } = sender();
    const coalescer = new Coalescer<number>(send);

    void coalescer.set('manual:0', 0.5);
    void coalescer.set('manual:1', 0.25);

    expect(sent).toEqual([
      ['manual:0', 0.5],
      ['manual:1', 0.25]
    ]);
  });

  it('carries on after a failed request', async () => {
    const sent: number[] = [];
    let fail = true;
    const coalescer = new Coalescer<number>(async (_key, value) => {
      sent.push(value);
      if (fail) {
        fail = false;
        throw new Error('offline');
      }
    });

    const first = coalescer.set('level', 1);
    void coalescer.set('level', 2);
    await first;

    expect(sent).toEqual([1, 2]);
    expect(coalescer.pending('level')).toBeUndefined();
  });
});
