import { Routes } from '@angular/router';

/** Trasy sekcji „Wydatki” (wewnątrz powłoki, leniwie ładowane). */
export const EXPENSES_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./expenses-list-page').then((m) => m.ExpensesListPage),
  },
  {
    path: 'nowy',
    title: 'Dodaj wydatek',
    loadComponent: () => import('./expense-create-page').then((m) => m.ExpenseCreatePage),
  },
  {
    path: ':id',
    title: 'Wydatek',
    loadComponent: () => import('./expense-details-page').then((m) => m.ExpenseDetailsPage),
  },
];
