import { Routes } from '@angular/router';

/** Trasy sekcji „Wydatki” (wewnątrz powłoki, leniwie ładowane); `cykliczne` przed `:id`. */
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
    path: 'cykliczne',
    title: 'Wydatki cykliczne',
    loadComponent: () =>
      import('./recurring-expenses-list-page').then((m) => m.RecurringExpensesListPage),
  },
  {
    path: 'cykliczne/:id',
    title: 'Wydatek cykliczny',
    loadComponent: () =>
      import('./recurring-expense-details-page').then((m) => m.RecurringExpenseDetailsPage),
  },
  {
    path: ':id',
    title: 'Wydatek',
    loadComponent: () => import('./expense-details-page').then((m) => m.ExpenseDetailsPage),
  },
];
