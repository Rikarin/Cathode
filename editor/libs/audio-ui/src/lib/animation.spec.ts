import { themeColors } from './animation';

/** MutationObserver callbacks run as microtasks; one turn of the queue delivers them. */
const flush = () => new Promise<void>(resolve => setTimeout(resolve));

describe('themeColors', () => {
  let element: HTMLElement;

  beforeEach(() => {
    element = document.createElement('div');
    document.body.append(element);
    element.style.setProperty('--ok', '#00ff00');
    element.style.setProperty('--clip', '#ff0000');
  });

  afterEach(() => {
    element.remove();
    document.documentElement.className = '';
    vi.restoreAllMocks();
  });

  it('resolves the tokens once and reuses them on later frames', () => {
    const spy = vi.spyOn(window, 'getComputedStyle');
    const colors = themeColors({ ok: '--ok', clip: '--clip' });

    const first = colors(element);
    const second = colors(element);
    colors(element);

    expect(first).toEqual({ ok: '#00ff00', clip: '#ff0000' });
    expect(second).toBe(first);
    expect(spy).toHaveBeenCalledTimes(1);
  });

  it('resolves again after the theme class on <html> changes', async () => {
    const colors = themeColors({ ok: '--ok' });
    expect(colors(element).ok).toBe('#00ff00');

    const spy = vi.spyOn(window, 'getComputedStyle');
    element.style.setProperty('--ok', '#008800');
    document.documentElement.classList.add('dark');
    await flush();

    expect(colors(element).ok).toBe('#008800');
    expect(spy).toHaveBeenCalledTimes(1);
  });

  it('resolves again after the data-theme attribute changes', async () => {
    const colors = themeColors({ ok: '--ok' });
    colors(element);

    element.style.setProperty('--ok', '#00aa00');
    document.documentElement.setAttribute('data-theme', 'light');
    await flush();
    document.documentElement.removeAttribute('data-theme');

    expect(colors(element).ok).toBe('#00aa00');
  });

  it('keeps trying while a token is unresolved, falling back to currentColor', () => {
    const colors = themeColors({ ok: '--ok', missing: '--missing' });
    const spy = vi.spyOn(window, 'getComputedStyle');

    expect(colors(element).missing).toBe('currentColor');
    element.style.setProperty('--missing', '#123456');

    expect(colors(element).missing).toBe('#123456');
    expect(spy).toHaveBeenCalledTimes(2);
  });
});
