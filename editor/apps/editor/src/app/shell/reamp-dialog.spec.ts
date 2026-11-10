import { TestBed } from '@angular/core/testing';
import { ConnectionService } from '../core/connection';
import { Notify } from '../core/notify';
import { ReampDialog, reampedName } from './reamp-dialog';

describe('ReampDialog', () => {
  const fetchMock = vi.fn<typeof fetch>();
  const success = vi.fn();
  const error = vi.fn();

  async function open() {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: ConnectionService,
          useValue: { connection: () => ({ baseUrl: 'http://rig.local:5180', token: 'secret' }) }
        },
        {
          provide: Notify,
          useValue: {
            success,
            error,
            run: async (action: () => Promise<unknown>) => {
              try {
                return await action();
              } catch (e) {
                error(e);
                return undefined;
              }
            }
          }
        }
      ]
    });
    const fixture = TestBed.createComponent(ReampDialog);
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    await fixture.whenStable();
    const find = (testid: string) => {
      const element = document.body.querySelector<HTMLButtonElement & HTMLInputElement>(`[data-testid="${testid}"]`);
      if (!element) {
        throw new Error(`No ${testid}`);
      }

      return element;
    };
    const choose = async (file: File) => {
      const input = find('reamp-input');
      Object.defineProperty(input, 'files', { value: [file], configurable: true });
      input.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      await fixture.whenStable();
    };
    const render = async () => {
      find('reamp-render').click();
      await fixture.whenStable();
      await new Promise(resolve => setTimeout(resolve));
      fixture.detectChanges();
    };
    return { find, choose, render };
  }

  beforeEach(() => {
    fetchMock.mockReset();
    success.mockReset();
    error.mockReset();
    vi.stubGlobal('fetch', fetchMock);
    URL.createObjectURL = vi.fn(() => 'blob:render');
    URL.revokeObjectURL = vi.fn();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('renders nothing until a recording is chosen', async () => {
    const { find } = await open();

    expect(find('reamp-render').disabled).toBe(true);
    expect(find('reamp-file').textContent).toContain('No file chosen');
  });

  it('sends the recording with the tail to the Server and saves the render under the recording’s name', async () => {
    const saved: string[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      saved.push(this.download);
    });
    fetchMock.mockResolvedValue(new Response(new Blob(['RIFF']), { status: 200 }));
    const { choose, render } = await open();
    const file = new File(['RIFF'], 'DI take 3.WAV');

    await choose(file);
    await render();

    const [url, init] = fetchMock.mock.calls[0] ?? [];
    expect(url).toBe('http://rig.local:5180/api/v1/live/render?tail=2');
    expect(init?.method).toBe('POST');
    expect(init?.body).toBe(file);
    expect(init?.headers).toMatchObject({ Authorization: 'Bearer secret', 'Content-Type': 'audio/wav' });
    expect(saved).toEqual(['DI take 3 (reamped).wav']);
    expect(success).toHaveBeenCalledWith('Rendered “DI take 3 (reamped).wav”.');
  });

  it('reports the Server’s reason when it refuses the recording, and saves nothing', async () => {
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ detail: 'The recording is empty.' }), {
        status: 400,
        headers: { 'Content-Type': 'application/problem+json' }
      })
    );
    const { choose, render, find } = await open();

    await choose(new File([], 'empty.wav'));
    await render();

    expect((error.mock.calls[0]?.[0] as Error).message).toBe('The recording is empty.');
    expect(click).not.toHaveBeenCalled();
    expect(success).not.toHaveBeenCalled();
    expect(find('reamp-render').disabled).toBe(false);
  });

  it('names the render after the recording', () => {
    expect(reampedName('riff.wav')).toBe('riff (reamped).wav');
    expect(reampedName('no extension')).toBe('no extension (reamped).wav');
  });
});
