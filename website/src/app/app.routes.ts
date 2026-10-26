import type { Routes } from '@angular/router';

// Each page sets its own title and meta tags (seo.ts), so they live beside the page they describe.
export const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/home').then(m => m.Home) },
  {
    path: 'catalog',
    loadComponent: () => import('./pages/catalog-page').then(m => m.CatalogPage)
  },
  { path: 'manual', loadComponent: () => import('./pages/manual/manual-home').then(m => m.ManualHome) },
  {
    path: 'manual/blocks/:typeId',
    loadComponent: () => import('./pages/manual/manual-block').then(m => m.ManualBlock)
  },
  {
    path: 'manual/:slug',
    loadComponent: () => import('./pages/manual/manual-chapter').then(m => m.ManualChapter)
  },
  // The same page under a path of its own, so it can be prerendered as the host's 404.html.
  { path: '404', loadComponent: () => import('./pages/not-found').then(m => m.NotFound) },
  { path: '**', loadComponent: () => import('./pages/not-found').then(m => m.NotFound) }
];
