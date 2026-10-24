import { RenderMode, type ServerRoute } from '@angular/ssr';

/**
 * The site reads the same for everyone, so every page is rendered at build time and served as a static file: there is
 * no server. An unknown path gets the prerendered 404 page from the host, or the app's own once it has booted.
 */
export const serverRoutes: ServerRoute[] = [
  { path: '', renderMode: RenderMode.Prerender },
  { path: '404', renderMode: RenderMode.Prerender },
  { path: '**', renderMode: RenderMode.Client }
];
