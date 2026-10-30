import { keepDialogFocusOffClose } from './dialog-focus';

describe('keepDialogFocusOffClose', () => {
  let stop: () => void;
  let pane: HTMLElement;
  let close: HTMLButtonElement;
  let field: HTMLInputElement;
  let outside: HTMLButtonElement;

  beforeEach(() => {
    document.body.innerHTML = `
      <button id="outside">Open</button>
      <div class="cdk-overlay-pane" role="dialog">
        <div><header><h2>Settings</h2><button aria-label="Close">×</button></header></div>
        <input id="field" />
      </div>`;
    pane = document.querySelector('.cdk-overlay-pane') as HTMLElement;
    close = pane.querySelector('button') as HTMLButtonElement;
    field = pane.querySelector('input') as HTMLInputElement;
    outside = document.getElementById('outside') as HTMLButtonElement;
    stop = keepDialogFocusOffClose(document);
  });

  afterEach(() => {
    stop();
    document.body.innerHTML = '';
  });

  it('focuses the dialog, not its Close button, as the dialog opens', () => {
    outside.focus();
    close.focus();

    expect(document.activeElement).toBe(pane);
  });

  it('leaves Close focused when focus moves to it within the dialog', () => {
    field.focus();
    close.focus();

    expect(document.activeElement).toBe(close);
  });

  it('leaves other buttons alone, and stops when asked', () => {
    outside.focus();
    expect(document.activeElement).toBe(outside);

    stop();
    close.focus();
    expect(document.activeElement).toBe(close);
  });
});
