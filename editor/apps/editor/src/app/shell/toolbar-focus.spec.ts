import { keepToolbarClicksUnfocused } from './toolbar-focus';

describe('keepToolbarClicksUnfocused', () => {
  let stop: () => void;

  beforeEach(() => {
    document.body.innerHTML = `
      <nav class="chrome-bar"><button id="metronome"><span id="icon"></span></button></nav>
      <main><button id="other"></button></main>`;
    stop = keepToolbarClicksUnfocused(document);
  });

  afterEach(() => {
    stop();
    document.body.innerHTML = '';
  });

  const press = (id: string, button = 0) => {
    const event = new MouseEvent('mousedown', { bubbles: true, cancelable: true, button });
    document.getElementById(id)?.dispatchEvent(event);
    return event;
  };

  it('keeps a click on a top-bar button, or its icon, from focusing it', () => {
    expect(press('metronome').defaultPrevented).toBe(true);
    expect(press('icon').defaultPrevented).toBe(true);
  });

  it('leaves buttons elsewhere, other mouse buttons, and a stopped watch alone', () => {
    expect(press('other').defaultPrevented).toBe(false);
    expect(press('metronome', 2).defaultPrevented).toBe(false);

    stop();
    expect(press('metronome').defaultPrevented).toBe(false);
  });
});
