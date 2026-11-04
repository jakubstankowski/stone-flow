import { Route, Routes } from '@angular/router';
import { NAV_ITEMS } from './layout/nav-items';

/**
 * Trasa sekcji: ścieżka, tytuł i nazwa w nagłówku pochodzą z `layout/nav-items.ts`.
 * `loadComponent` pozostaje literałem, aby każda sekcja była osobnym, leniwym fragmentem.
 */
function section(path: string, loadComponent: Route['loadComponent']): Route {
  const item = NAV_ITEMS.find((candidate) => candidate.path === path);
  if (!item) {
    throw new Error(`Brak sekcji „${path}” w NAV_ITEMS.`);
  }
  return { path, title: item.label, data: { section: item.label }, loadComponent };
}

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'pulpit' },
  section('pulpit', () =>
    import('./features/dashboard/dashboard-page').then((m) => m.DashboardPage),
  ),
  section('zamowienia', () => import('./features/orders/orders-page').then((m) => m.OrdersPage)),
  section('wydatki', () => import('./features/expenses/expenses-page').then((m) => m.ExpensesPage)),
  section('operacje', () =>
    import('./features/operations/operations-page').then((m) => m.OperationsPage),
  ),
  section('historia-zmian', () =>
    import('./features/change-history/change-history-page').then((m) => m.ChangeHistoryPage),
  ),
  section('integracja', () =>
    import('./features/integration/integration-page').then((m) => m.IntegrationPage),
  ),
  { path: '**', redirectTo: 'pulpit' },
];
