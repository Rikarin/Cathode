import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LICENCE, RELEASES, REPOSITORY, SECTIONS } from '../site';

const PROJECT_LINKS = [
  { label: 'GitHub', href: REPOSITORY },
  { label: 'Releases', href: RELEASES },
  { label: 'Issues', href: `${REPOSITORY}/issues` },
  { label: 'Licence (GPL-3.0)', href: LICENCE }
];

@Component({
  selector: 'cw-site-footer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  host: { class: 'block border-t border-white/[0.06]' },
  template: `
    <div class="mx-auto max-w-[100rem] px-4 py-14 sm:px-6">
      <div class="grid gap-10 md:grid-cols-[1.4fr_1fr_1fr]">
        <div class="max-w-md">
          <a routerLink="/" class="inline-flex items-center gap-2.5 text-xl font-semibold tracking-tight">
            <img src="logo.png" alt="" width="32" height="32" class="size-8 rounded-lg" />
            Cathode
          </a>
          <p class="text-foreground-muted mt-3 text-sm leading-relaxed">
            A guitar processor for your computer: circuit-modelled amps, cabinets, drives and effects on a grid, for
            macOS, Windows and Linux.
          </p>
          <p class="text-foreground-subtle mt-4 text-sm leading-relaxed">
            Free software under the GNU GPL, version 3 or later. Cathode is not affiliated with or endorsed by Fractal
            Audio Systems; Axe-Fx is their trademark. Amplifier, speaker and microphone names belong to their owners and
            are used only to say which equipment a model is based on.
          </p>
        </div>

        <nav aria-label="On this page">
          <p class="mb-3 text-sm font-medium">On this page</p>
          <ul class="flex flex-col gap-2">
            @for (item of sections; track item.id) {
              <li>
                <a
                  class="text-foreground-muted hover:text-foreground text-sm hover:underline"
                  routerLink="/"
                  [fragment]="item.id"
                  >{{ item.label }}</a
                >
              </li>
            }
          </ul>
        </nav>

        <nav aria-label="Project">
          <p class="mb-3 text-sm font-medium">Project</p>
          <ul class="flex flex-col gap-2">
            @for (item of project; track item.href) {
              <li>
                <a
                  class="text-foreground-muted hover:text-foreground text-sm hover:underline"
                  [href]="item.href"
                  rel="noreferrer noopener"
                  target="_blank"
                  >{{ item.label }}</a
                >
              </li>
            }
          </ul>
        </nav>
      </div>
    </div>
  `
})
export class SiteFooter {
  protected readonly sections = SECTIONS;
  protected readonly project = PROJECT_LINKS;
}
