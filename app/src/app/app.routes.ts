import { Route, Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth/auth-guards';
import { AppShell } from './layout/app-shell';
import { AuthLayout } from './layout/auth-layout';
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

/** Sekcje z SF-5 wewnątrz powłoki. */
export const sectionRoutes: Routes = [
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
];

/** Ekrany logowania i resetu hasła: poza powłoką, bez ochrony sesją. */
const authRoutes: Routes = [
  {
    path: 'logowanie',
    title: 'Logowanie',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login-page').then((m) => m.LoginPage),
  },
  {
    path: 'reset-hasla',
    title: 'Reset hasła',
    loadComponent: () =>
      import('./features/auth/password-reset-request-page').then((m) => m.PasswordResetRequestPage),
  },
  {
    path: 'reset-hasla/nowe',
    title: 'Ustaw hasło',
    loadComponent: () => import('./features/auth/set-password-page').then((m) => m.SetPasswordPage),
  },
];

/**
 * Dwa układy: ekrany auth w `AuthLayout` i sekcje w `AppShell` chronione `authGuard`
 * (`canActivate`, bo strażnik potrzebuje `state.url` do parametru `powrot`). Powłoka jest
 * pierwsza: pusty adres trafia do jej przekierowania na pulpit, a nie do pustego układu auth.
 * Nieznane ścieżki prowadzą na pulpit, a więc przez strażnika.
 */
export const routes: Routes = [
  {
    path: '',
    component: AppShell,
    canActivate: [authGuard],
    canActivateChild: [authGuard],
    children: sectionRoutes,
  },
  { path: '', component: AuthLayout, children: authRoutes },
  { path: '**', redirectTo: 'pulpit' },
];
