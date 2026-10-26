import { ViewportScroller } from '@angular/common';
import { computed, Directive, inject, input } from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';
import { Router } from '@angular/router';

/**
 * Shows HTML rendered from the manual's Markdown (manual/markdown.ts), and routes a click on a link to another page of
 * the site inside the app instead of reloading it. The HTML is the site's own, written in the repository, so it is
 * trusted: Angular's sanitiser would strip the heading anchors' ids.
 */
@Directive({
  selector: '[cwProse]',
  host: {
    class: 'prose',
    '[innerHTML]': 'safe()',
    '(click)': 'follow($event)'
  }
})
export class Prose {
  readonly cwProse = input.required<string>();

  private readonly sanitizer = inject(DomSanitizer);
  private readonly router = inject(Router);
  private readonly scroller = inject(ViewportScroller);

  protected readonly safe = computed(() => this.sanitizer.bypassSecurityTrustHtml(this.cwProse()));

  protected follow(event: MouseEvent): void {
    if (
      event.defaultPrevented ||
      event.button !== 0 ||
      event.metaKey ||
      event.ctrlKey ||
      event.shiftKey ||
      event.altKey
    ) {
      return;
    }

    const link = (event.target as Element | null)?.closest?.('a');
    const href = link?.getAttribute('href');

    if (!link || link.target || !href?.startsWith('/') || href.startsWith('//')) {
      return;
    }

    event.preventDefault();
    const [path, fragment] = href.split('#');

    if (path === this.router.url.split(/[?#]/)[0] && fragment) {
      // The same page: the router would see no change, so scroll to the anchor here and keep it in the address.
      history.replaceState(history.state, '', href);
      this.scroller.scrollToAnchor(fragment);
      return;
    }

    void this.router.navigateByUrl(href);
  }
}
