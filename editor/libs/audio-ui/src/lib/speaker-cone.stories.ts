import { signal } from '@angular/core';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { type ConeMic, type ConeMove, CthSpeakerCone } from './speaker-cone';

const Mics: [ConeMic, ConeMic, ConeMic, ConeMic] = [
  { slot: 0, name: 'SM57', position: 25, distance: 1, angle: 0, muted: false },
  { slot: 1, name: 'R-121', position: 55, distance: 4, angle: 0, muted: false },
  { slot: 2, name: 'MD 421', position: 35, distance: 2, angle: 30, muted: true },
  { slot: 3, name: 'U 87', position: 0, distance: 12, angle: 0, muted: true }
];

/**
 * The Cab block's mic placement: the cone from cap to edge, the DynaCab and IR pack slots in front of it (muted ones
 * hollow). Drag a mic, or focus it and use the
 * arrow keys (⇧ for big steps).
 */
const meta: Meta<CthSpeakerCone> = {
  title: 'Audio UI/Speaker Cone',
  component: CthSpeakerCone,
  render: args => {
    const mics = signal(args.mics); // meta.args gives every story the four mics unless it sets its own
    const selected = signal<number | null>(args.selected ?? 0);
    return {
      props: {
        ...args,
        mics,
        selected,
        move: (m: ConeMove) => mics.update(list => list.map(mic => (mic.slot === m.slot ? { ...mic, ...m } : mic)))
      },
      template: `
        <cth-speaker-cone
          class="w-[480px]"
          [mics]="mics()"
          [selected]="selected()"
          [maxDistance]="maxDistance"
          (moved)="move($event)"
          (selectedChange)="selected.set($event)"
        />
      `
    };
  },
  args: { mics: Mics, selected: 0, maxDistance: 12 }
};

export default meta;
type Story = StoryObj<CthSpeakerCone>;

export const FourMics: Story = {};

/** One close mic on the cap edge, the classic SM57 placement. */
export const SingleMic: Story = { args: { mics: [Mics[0]] } };

/**
 * A Cab mixing sources: slot 2 plays an IR file, so it is not on the cone; slot 3 plays an IR pack (no angle) and slot 4
 * is muted.
 */
export const MixedSources: Story = {
  args: {
    mics: [Mics[0], { slot: 2, name: 'My 4x12', position: 40, distance: 2, angle: 0, muted: false }, Mics[3]],
    selected: 2
  }
};
