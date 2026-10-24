import { signal } from '@angular/core';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { type ConeMic, type ConeMove, CthSpeakerCone } from './speaker-cone';

const Mics: ConeMic[] = [
  { slot: 0, name: 'SM57', position: 25, distance: 1, angle: 0, muted: false },
  { slot: 1, name: 'R-121', position: 55, distance: 4, angle: 0, muted: false },
  { slot: 2, name: 'MD 421', position: 35, distance: 2, angle: 30, muted: true },
  { slot: 3, name: 'U 87', position: 0, distance: 12, angle: 0, muted: true }
];

/**
 * DynaCab's mic placement: the cone from cap to edge, the mics in front of it. Drag a mic, or focus it and use the
 * arrow keys (⇧ for big steps).
 */
const meta: Meta<CthSpeakerCone> = {
  title: 'Audio UI/Speaker Cone',
  component: CthSpeakerCone,
  render: args => {
    const mics = signal(args.mics ?? Mics);
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
