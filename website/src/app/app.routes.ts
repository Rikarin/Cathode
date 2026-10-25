import type { Routes } from '@angular/router';

const TITLE = 'Cathode — a guitar processor for your computer';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/home').then(m => m.Home), title: TITLE },
  {
    path: 'catalog',
    loadComponent: () => import('./pages/catalog-page').then(m => m.CatalogPage),
    title: 'Catalogue: every amp, drive and block — Cathode'
  },
  // The same page under a path of its own, so it can be prerendered as the host's 404.html.
  { path: '404', loadComponent: () => import('./pages/not-found').then(m => m.NotFound), title: 'Not found — Cathode' },
  { path: '**', loadComponent: () => import('./pages/not-found').then(m => m.NotFound), title: 'Not found — Cathode' }
];
