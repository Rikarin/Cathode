import { DOCUMENT } from '@angular/common';
import { inject, Injectable } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { SITE_NAME, SITE_URL } from './site';

export interface PageMeta {
  /** The whole document title, as search results and tabs show it (about 60 characters at most). */
  readonly title: string;
  /** The search result's snippet and the social card's text (about 155 characters at most). */
  readonly description: string;
  /** The page's path from the site root, without a trailing slash: `''` for the home page, `'catalog'`. */
  readonly path: string;
  /** Keep the page out of search results (the 404 page). */
  readonly noindex?: boolean;
  /** schema.org data for the page, written as JSON-LD. */
  readonly structuredData?: object;
}

/** The social card every page shares: the main window, cropped to 1200 × 630. */
const SOCIAL_IMAGE = {
  url: `${SITE_URL}/social.jpg`,
  width: '1200',
  height: '630',
  alt: 'The Cathode editor: a guitar rig on a grid of amps, cabs and effects, with the amp open below it.'
};

/**
 * Writes a page's title, description, canonical link, Open Graph and Twitter card tags and JSON-LD into the document
 * head. Each page calls it as it is created, so the prerendered HTML already carries them for crawlers that do not run
 * JavaScript, and a navigation in the browser replaces the previous page's.
 */
@Injectable({ providedIn: 'root' })
export class Seo {
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);
  private readonly document = inject(DOCUMENT);

  set(page: PageMeta): void {
    const url = page.path ? `${SITE_URL}/${page.path}` : `${SITE_URL}/`;

    this.title.setTitle(page.title);
    this.name('description', page.description);
    this.name('robots', page.noindex ? 'noindex' : 'index, follow');

    this.property('og:type', 'website');
    this.property('og:site_name', SITE_NAME);
    this.property('og:locale', 'en');
    this.property('og:title', page.title);
    this.property('og:description', page.description);
    this.property('og:url', url);
    this.property('og:image', SOCIAL_IMAGE.url);
    this.property('og:image:width', SOCIAL_IMAGE.width);
    this.property('og:image:height', SOCIAL_IMAGE.height);
    this.property('og:image:alt', SOCIAL_IMAGE.alt);

    this.name('twitter:card', 'summary_large_image');
    this.name('twitter:title', page.title);
    this.name('twitter:description', page.description);
    this.name('twitter:image', SOCIAL_IMAGE.url);
    this.name('twitter:image:alt', SOCIAL_IMAGE.alt);

    // A not-found page has no address of its own to point search engines at.
    this.canonical(page.noindex ? null : url);
    this.structuredData(page.structuredData ?? null);
  }

  private name(name: string, content: string): void {
    this.meta.updateTag({ name, content });
  }

  private property(property: string, content: string): void {
    this.meta.updateTag({ property, content });
  }

  private canonical(url: string | null): void {
    let link = this.document.head.querySelector<HTMLLinkElement>('link[rel="canonical"]');

    if (!url) {
      link?.remove();
      return;
    }

    if (!link) {
      link = this.document.createElement('link');
      link.rel = 'canonical';
      this.document.head.appendChild(link);
    }

    link.href = url;
  }

  private structuredData(data: object | null): void {
    let script = this.document.head.querySelector<HTMLScriptElement>('script#structured-data');

    if (!data) {
      script?.remove();
      return;
    }

    if (!script) {
      script = this.document.createElement('script');
      script.id = 'structured-data';
      script.type = 'application/ld+json';
      this.document.head.appendChild(script);
    }

    script.textContent = JSON.stringify(data);
  }
}
