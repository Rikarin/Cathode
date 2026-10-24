import type { Route } from '@angular/router';

export const appRoutes: Route[] = [
  { path: '', loadComponent: () => import('./pages/editor-page').then(m => m.EditorPage), title: 'Cathode' },
  {
    path: 'library',
    loadComponent: () => import('./pages/library-page').then(m => m.LibraryPage),
    title: 'Library · Cathode'
  },
  {
    path: 'irs',
    loadComponent: () => import('./pages/irs-page').then(m => m.IrsPage),
    title: 'Impulse responses · Cathode'
  },
  {
    path: 'setup',
    loadComponent: () => import('./pages/setup-page').then(m => m.SetupPage),
    title: 'Setup · Cathode'
  },
  {
    path: 'scenes',
    loadComponent: () => import('./pages/scene-manager').then(m => m.SceneManagerPage),
    title: 'Scenes · Cathode'
  },
  {
    path: 'controllers',
    loadComponent: () => import('./pages/controllers-page').then(m => m.ControllersPage),
    title: 'Controllers · Cathode'
  },
  {
    path: 'global',
    loadComponent: () => import('./pages/global-page').then(m => m.GlobalPage),
    title: 'Global · Cathode'
  },
  {
    path: 'perform',
    loadComponent: () => import('./pages/perform-page').then(m => m.PerformPage),
    title: 'Perform · Cathode'
  },
  {
    path: 'manage',
    loadComponent: () => import('./pages/manage-page').then(m => m.ManagePage),
    title: 'Manage presets and IRs · Cathode'
  },
  { path: '**', redirectTo: '' }
];
