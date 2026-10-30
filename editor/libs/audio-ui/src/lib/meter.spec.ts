import { TestBed } from '@angular/core/testing';
import { CthMeter, resetClipLights } from './meter';

describe('CthMeter clip light', () => {
  let frames: FrameRequestCallback[];

  beforeEach(() => {
    frames = [];
    vi.spyOn(window, 'requestAnimationFrame').mockImplementation(callback => frames.push(callback));
    vi.spyOn(window, 'cancelAnimationFrame').mockImplementation(() => undefined);
  });

  afterEach(() => vi.restoreAllMocks());

  function render(level: number, clipLight: 'always' | 'clipped' = 'always') {
    const fixture = TestBed.createComponent(CthMeter);
    fixture.componentRef.setInput('sources', [() => level]);
    fixture.componentRef.setInput('label', 'Amp output level');
    fixture.componentRef.setInput('clipLight', clipLight);
    fixture.detectChanges();
    TestBed.tick(); // the first render starts the frame loop
    frames.splice(0).forEach(frame => frame(16));
    fixture.detectChanges();
    const light = () => (fixture.nativeElement as HTMLElement).querySelector('button');
    return { fixture, light };
  }

  it('catches a clip and keeps it until clicked', () => {
    const { fixture, light } = render(2); // +6 dB
    expect(light()?.getAttribute('aria-label')).toBe('Amp output level clipped; click to reset');

    light()?.click();
    fixture.detectChanges();
    expect(light()?.getAttribute('aria-label')).toBe('Amp output level clip indicator');
  });

  it('shows a clipped-only light just while it holds a clip', () => {
    const quiet = render(0.5, 'clipped');
    expect(quiet.light()).toBeNull();

    const hot = render(2, 'clipped');
    expect(hot.light()).not.toBeNull();
  });

  it('puts out every clip light at once', () => {
    const amp = render(2);
    const drive = render(2, 'clipped');

    resetClipLights();
    TestBed.tick();
    amp.fixture.detectChanges();
    drive.fixture.detectChanges();

    expect(amp.light()?.getAttribute('aria-label')).toBe('Amp output level clip indicator');
    expect(drive.light()).toBeNull();
  });
});
