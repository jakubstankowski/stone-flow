import { Routes } from '@angular/router';

/** Trasy sekcji „Operacje” (wewnątrz powłoki, leniwie ładowane). */
export const OPERATIONS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./operations-list-page').then((m) => m.OperationsListPage),
  },
  {
    path: 'wyplaty/nowa',
    title: 'Dodaj wypłatę',
    loadComponent: () => import('./withdrawal-create-page').then((m) => m.WithdrawalCreatePage),
  },
  {
    path: 'wyplaty/:id',
    title: 'Wypłata właściciela',
    loadComponent: () => import('./withdrawal-details-page').then((m) => m.WithdrawalDetailsPage),
  },
];
