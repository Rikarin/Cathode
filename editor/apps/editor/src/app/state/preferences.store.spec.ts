import { TestBed } from '@angular/core/testing';
import { DefaultPreferences, PreferencesStorage, PreferencesStore, parsePreferences } from './preferences.store';

describe('preferences', () => {
  function store(initial: string | null = null) {
    const saved = new Map<string, string>(initial ? [['cathode.preferences', initial]] : []);
    TestBed.configureTestingModule({
      providers: [
        {
          provide: PreferencesStorage,
          useValue: { getItem: (k: string) => saved.get(k) ?? null, setItem: (k: string, v: string) => saved.set(k, v) }
        }
      ]
    });
    return { prefs: TestBed.inject(PreferencesStore), saved };
  }

  it('start from the defaults, and keep only known values from storage', () => {
    expect(parsePreferences(null)).toEqual(DefaultPreferences);
    expect(parsePreferences('not json')).toEqual(DefaultPreferences);
    expect(parsePreferences('{"knobMode":"circular","wheel":"sideways","meterRate":30,"tooltips":false}')).toEqual({
      ...DefaultPreferences,
      knobMode: 'circular',
      meterRate: 30,
      tooltips: false
    });
  });

  it('are stored per user when changed, and read back', () => {
    const { prefs, saved } = store('{"wheel":"reverse"}');
    expect(prefs.wheel()).toBe('reverse');

    prefs.set('ampView', 'ideal');
    prefs.set('meterRate', 15);

    expect(JSON.parse(saved.get('cathode.preferences')!)).toMatchObject({
      ampView: 'ideal',
      meterRate: 15,
      wheel: 'reverse'
    });
    expect(prefs.ampView()).toBe('ideal');
  });

  it('turn every tooltip off with one class on the document', () => {
    const { prefs } = store();
    prefs.set('tooltips', false);
    TestBed.tick();
    expect(document.documentElement.classList.contains('cth-no-tooltips')).toBe(true);
    prefs.set('tooltips', true);
    TestBed.tick();
    expect(document.documentElement.classList.contains('cth-no-tooltips')).toBe(false);
  });
});
