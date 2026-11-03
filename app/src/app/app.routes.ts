import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/start/start-page').then((m) => m.StartPage),
  },
  { path: '**', redirectTo: '' },
];
