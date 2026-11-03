import { Routes } from '@angular/router';

/** Sekcje z leniwym ładowaniem; kolejność i etykiety zgodne z `layout/nav-items.ts`. */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'pulpit' },
  {
    path: 'pulpit',
    title: 'Pulpit',
    data: { section: 'Pulpit' },
    loadComponent: () => import('./features/dashboard/dashboard-page').then((m) => m.DashboardPage),
  },
  {
    path: 'zamowienia',
    title: 'Zamówienia',
    data: { section: 'Zamówienia' },
    loadComponent: () => import('./features/orders/orders-page').then((m) => m.OrdersPage),
  },
  {
    path: 'wydatki',
    title: 'Wydatki',
    data: { section: 'Wydatki' },
    loadComponent: () => import('./features/expenses/expenses-page').then((m) => m.ExpensesPage),
  },
  {
    path: 'operacje',
    title: 'Operacje',
    data: { section: 'Operacje' },
    loadComponent: () =>
      import('./features/operations/operations-page').then((m) => m.OperationsPage),
  },
  {
    path: 'historia-zmian',
    title: 'Historia zmian',
    data: { section: 'Historia zmian' },
    loadComponent: () =>
      import('./features/change-history/change-history-page').then((m) => m.ChangeHistoryPage),
  },
  {
    path: 'integracja',
    title: 'Integracja',
    data: { section: 'Integracja' },
    loadComponent: () =>
      import('./features/integration/integration-page').then((m) => m.IntegrationPage),
  },
  { path: '**', redirectTo: 'pulpit' },
];
