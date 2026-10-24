import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SiteFooter } from '../layout/site-footer';
import { SiteHeader } from '../layout/site-header';

@Component({
  selector: 'cw-not-found',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, SiteHeader, SiteFooter],
  host: { class: 'block min-h-svh' },
  template: `
    <cw-site-header />
    <main id="main" class="relative overflow-x-clip">
      <div class="glow pointer-events-none absolute inset-x-0 top-0 h-[32rem]" aria-hidden="true"></div>
      <div class="relative mx-auto max-w-3xl px-4 py-32 text-center sm:px-6">
        <h1 class="display gradient text-5xl sm:text-6xl">No signal here.</h1>
        <p class="text-foreground-muted mx-auto mt-5 max-w-xl text-lg">
          There is no page at this address. The whole site is one page; start from the top.
        </p>
        <a routerLink="/" class="cta mt-9">Back to Cathode</a>
      </div>
    </main>
    <cw-site-footer />
  `
})
export class NotFound {}
