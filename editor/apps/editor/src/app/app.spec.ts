import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { App } from './app';
import { Notify } from './core/notify';
import { Startup } from './core/startup';
import { CompareStore } from './state/compare.store';
import { LibraryStore } from './state/library.store';
import { LiveStore } from './state/live.store';
import { MidiLearnStore } from './state/midi-learn.store';
import { PresetSwitcher } from './state/preset-switcher';
import { SetlistPlayer } from './state/setlist-player';
import { type SettingsTab, UiStore } from './state/ui.store';

// The shell: Save and Save as, and the shortcuts it registers, pressed as real keys on the document.
describe('App', () => {
  interface Preset {
    id: string;
    name: string;
    factory?: boolean;
    scenes: unknown[];
  }

  function render(preset: Preset | undefined, stored: boolean | Error = true) {
    const live = {
      preset: signal(preset),
      state: signal<{ muted?: boolean } | undefined>({ muted: false }),
      save: vi.fn((_name?: string, _asNew?: boolean) => Promise.resolve(true)),
      undo: vi.fn(() => Promise.resolve()),
      redo: vi.fn(() => Promise.resolve()),
      setMuted: vi.fn(() => Promise.resolve()),
      selectScene: vi.fn(() => Promise.resolve()),
      resetChannel: vi.fn(() => Promise.resolve()),
      channelOf: () => 'B'
    };
    const ui = {
      presetPicker: signal(false),
      tunerOpen: signal(false),
      settings: signal<SettingsTab | null>(null),
      saveBlockOpen: signal(false),
      block: signal<{ id: string } | null>(null),
      channels: () => 4,
      setChannel: vi.fn(),
      nextChannel: vi.fn(),
      toggleBypass: vi.fn(),
      allScenes: vi.fn(),
      makeGlobal: vi.fn()
    };
    const isStored = vi.fn(() => (stored instanceof Error ? Promise.reject(stored) : Promise.resolve(stored)));
    const error = vi.fn();
    const step = vi.fn(() => Promise.resolve());
    TestBed.configureTestingModule({
      providers: [
        {
          provide: Startup,
          useValue: {
            state: signal('ready'),
            error: signal(null),
            target: signal(null),
            start: () => Promise.resolve()
          }
        },
        { provide: LiveStore, useValue: live },
        { provide: CompareStore, useValue: {} },
        { provide: UiStore, useValue: ui },
        { provide: PresetSwitcher, useValue: { recent: signal([]), loading: signal(null) } },
        { provide: LibraryStore, useValue: { isStored } },
        { provide: SetlistPlayer, useValue: { step } },
        { provide: MidiLearnStore, useValue: {} },
        { provide: Router, useValue: {} },
        {
          provide: Notify,
          useValue: {
            error,
            run: async (action: () => Promise<unknown>) => {
              try {
                return await action();
              } catch (failure) {
                error(failure);
                return undefined;
              }
            }
          }
        }
      ]
    });
    TestBed.overrideComponent(App, { set: { template: '', imports: [] } });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const app = fixture.componentInstance as unknown as {
      save(): Promise<void>;
      openSaveAs(first?: boolean): void;
      saveAs(): Promise<void>;
      saveAsOpen: { (): boolean; set(open: boolean): void };
      saveAsFirst(): boolean;
      saveAsName: { (): string; set(name: string): void };
      levelingOpen(): boolean;
    };
    /** Presses a key on the page, as the browser would deliver it; returns whether a shortcut took it. */
    const press = (key: string, init: KeyboardEventInit = {}) => {
      const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
      document.body.dispatchEvent(event);
      return event.defaultPrevented;
    };
    return { app, live, ui, isStored, error, step, press, fixture };
  }

  /** Lets the promises already resolved run their continuations. */
  const settle = () => new Promise(resolve => setTimeout(resolve));

  const saved: Preset = { id: 'p1', name: 'Lead', scenes: [{}, {}] };

  afterEach(() => {
    document.body.querySelector('[aria-modal]')?.remove();
  });

  describe('Save', () => {
    it('saves a preset the library already holds, without asking for a name', async () => {
      const { app, live, isStored } = render(saved);

      await app.save();

      expect(isStored).toHaveBeenCalledWith('p1');
      expect(live.save).toHaveBeenCalledWith();
      expect(app.saveAsOpen()).toBe(false);
    });

    it('asks for a name the first time a preset is saved', async () => {
      const { app, live } = render(saved, false);

      await app.save();

      expect(live.save).not.toHaveBeenCalled();
      expect(app.saveAsOpen()).toBe(true);
      expect(app.saveAsFirst()).toBe(true);
      expect(app.saveAsName()).toBe('Lead');
    });

    it('asks for a name when the preset has none, without asking the Server', async () => {
      const { app, live, isStored } = render({ ...saved, name: '  ' });

      await app.save();

      expect(isStored).not.toHaveBeenCalled();
      expect(live.save).not.toHaveBeenCalled();
      expect(app.saveAsOpen()).toBe(true);
      expect(app.saveAsFirst()).toBe(true);
    });

    it('saves a copy of a factory preset, which is read-only', async () => {
      const { app, live, isStored } = render({ ...saved, factory: true });

      await app.save();

      expect(isStored).not.toHaveBeenCalled();
      expect(live.save).not.toHaveBeenCalled();
      expect(app.saveAsOpen()).toBe(true);
      expect(app.saveAsFirst()).toBe(false);
    });

    it('says so, and neither saves nor asks, when the Server cannot say whether the preset is stored', async () => {
      const { app, live, error } = render(saved, new Error('offline'));

      await app.save();

      expect(error).toHaveBeenCalledTimes(1);
      expect(live.save).not.toHaveBeenCalled();
      expect(app.saveAsOpen()).toBe(false);
    });

    it('does nothing with no preset loaded', async () => {
      const { app, live, isStored } = render(undefined);

      await app.save();

      expect(isStored).not.toHaveBeenCalled();
      expect(live.save).not.toHaveBeenCalled();
      expect(app.saveAsOpen()).toBe(false);
    });
  });

  describe('Save as', () => {
    it('opens on the preset’s name and saves a new preset under the name typed, trimmed', async () => {
      const { app, live } = render(saved);
      app.openSaveAs();
      expect(app.saveAsName()).toBe('Lead');
      expect(app.saveAsFirst()).toBe(false);

      app.saveAsName.set('  Lead 2 ');
      await app.saveAs();

      expect(live.save).toHaveBeenCalledWith('Lead 2', true);
      expect(app.saveAsOpen()).toBe(false);
    });

    it('saves nothing without a name', async () => {
      const { app, live } = render(saved);
      app.openSaveAs();
      app.saveAsName.set('   ');

      await app.saveAs();

      expect(live.save).not.toHaveBeenCalled();
      expect(app.saveAsOpen()).toBe(true);
    });

    it('stays open with the name as typed when the save fails', async () => {
      const { app, live } = render(saved);
      live.save.mockResolvedValueOnce(false);
      app.openSaveAs();
      app.saveAsName.set('Taken');

      await app.saveAs();

      expect(app.saveAsOpen()).toBe(true);
      expect(app.saveAsName()).toBe('Taken');
    });
  });

  describe('shortcuts', () => {
    it('saves on ⌘S and opens Save as on ⇧⌘S', async () => {
      const { app, live, press } = render(saved);

      expect(press('s', { metaKey: true })).toBe(true);
      await settle();
      expect(live.save).toHaveBeenCalledWith();

      expect(press('S', { metaKey: true, shiftKey: true })).toBe(true);
      expect(app.saveAsOpen()).toBe(true);
    });

    it('selects only the scenes the preset has', () => {
      const { live, press } = render(saved);

      press('2', { code: 'Digit2', metaKey: true });
      expect(live.selectScene).toHaveBeenCalledWith(1);

      press('3', { code: 'Digit3', metaKey: true });
      expect(live.selectScene).toHaveBeenCalledTimes(1);
    });

    it('mutes, undoes and redoes', () => {
      const { live, press } = render(saved);

      press('m');
      expect(live.setMuted).toHaveBeenCalledWith(true);
      press('z', { metaKey: true });
      expect(live.undo).toHaveBeenCalledTimes(1);
      press('z', { metaKey: true, shiftKey: true });
      press('y', { metaKey: true });
      expect(live.redo).toHaveBeenCalledTimes(2);
    });

    it('resets the selected block’s channel, and nothing without a block', () => {
      const { live, ui, press } = render(saved);

      press('i', { metaKey: true });
      expect(live.resetChannel).not.toHaveBeenCalled();

      ui.block.set({ id: 'delay1' });
      press('i', { metaKey: true });
      expect(live.resetChannel).toHaveBeenCalledWith('delay1', 'B');
    });

    it('opens the Save block dialog only with a block selected', () => {
      const { ui, press } = render(saved);

      press('b', { metaKey: true });
      expect(ui.saveBlockOpen()).toBe(false);

      ui.block.set({ id: 'delay1' });
      press('b', { metaKey: true });
      expect(ui.saveBlockOpen()).toBe(true);
    });

    it('toggles the tuner, Settings and the shortcut list', () => {
      const { ui, press } = render(saved);

      press('t', { metaKey: true });
      expect(ui.tunerOpen()).toBe(true);
      press('t');
      expect(ui.tunerOpen()).toBe(false);

      press(',', { metaKey: true });
      expect(ui.settings()).toBe('preferences');
      press('?', { shiftKey: true });
      expect(ui.settings()).toBe('shortcuts');
      press('?', { shiftKey: true });
      expect(ui.settings()).toBeNull();
    });

    it('steps through the setlist’s songs', () => {
      const { step, press } = render(saved);

      press('PageDown', { metaKey: true, shiftKey: true });
      press('PageUp', { metaKey: true, shiftKey: true });

      expect(step.mock.calls).toEqual([[1], [-1]]);
    });

    it('runs the bare keys a browser delivers as well: 1–8, L and ⇧PgUp/PgDn', () => {
      const { app, live, step, press } = render(saved);

      press('2', { code: 'Digit2' });
      expect(live.selectScene).toHaveBeenCalledWith(1);
      press('l');
      expect(app.levelingOpen()).toBe(true);
      press('PageDown', { shiftKey: true });
      expect(step).toHaveBeenCalledWith(1);
    });

    it('leaves the rig alone while a dialog is open: no save, no scene change, but the tuner and mute still work', async () => {
      const { live, ui, press } = render(saved);
      const dialog = document.createElement('div');
      dialog.setAttribute('aria-modal', 'true');
      document.body.append(dialog);

      expect(press('s', { metaKey: true })).toBe(false);
      expect(press('2', { code: 'Digit2', metaKey: true })).toBe(false);
      expect(press('z', { metaKey: true })).toBe(false);
      await settle();
      expect(live.save).not.toHaveBeenCalled();
      expect(live.selectScene).not.toHaveBeenCalled();
      expect(live.undo).not.toHaveBeenCalled();

      expect(press('t')).toBe(true);
      expect(ui.tunerOpen()).toBe(true);
      expect(press('m')).toBe(true);
      expect(live.setMuted).toHaveBeenCalledTimes(1);
    });

    it('takes no key typed into a field', () => {
      const { live, ui, fixture } = render(saved);
      const field = document.createElement('input');
      (fixture.nativeElement as HTMLElement).append(field);

      for (const key of ['m', 't', 'p']) {
        field.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
      }

      expect(live.setMuted).not.toHaveBeenCalled();
      expect(ui.tunerOpen()).toBe(false);
      expect(ui.presetPicker()).toBe(false);
    });

    it('stops listening when the shell is destroyed', () => {
      const { live, press, fixture } = render(saved);
      fixture.destroy();

      expect(press('m')).toBe(false);
      expect(live.setMuted).not.toHaveBeenCalled();
    });
  });
});
