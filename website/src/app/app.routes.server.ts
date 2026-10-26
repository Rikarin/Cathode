import { RenderMode, type ServerRoute } from '@angular/ssr';

/**
 * The site reads the same for everyone, so every page is rendered at build time and served as a static file: there is
 * no server. An unknown path gets the prerendered 404 page from the host, or the app's own once it has booted.
 */
export const serverRoutes: ServerRoute[] = [
  { path: '', renderMode: RenderMode.Prerender },
  { path: 'catalog', renderMode: RenderMode.Prerender },
  { path: 'manual', renderMode: RenderMode.Prerender },
  {
    path: 'manual/blocks/:typeId',
    renderMode: RenderMode.Prerender,
    getPrerenderParams: async () => {
      const { MANUAL_BLOCKS } = await import('./manual/manual');
      return MANUAL_BLOCKS.map(block => ({ typeId: block.typeId }));
    }
  },
  {
    path: 'manual/:slug',
    renderMode: RenderMode.Prerender,
    getPrerenderParams: async () => {
      const { MANUAL_CHAPTERS } = await import('./manual/manual');
      return MANUAL_CHAPTERS.map(chapter => ({ slug: chapter.slug }));
    }
  },
  { path: '404', renderMode: RenderMode.Prerender },
  { path: '**', renderMode: RenderMode.Client }
];
