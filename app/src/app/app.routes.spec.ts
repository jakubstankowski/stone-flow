import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';
import { ChangeHistoryPage } from './features/change-history/change-history-page';
import { DashboardPage } from './features/dashboard/dashboard-page';
import { ExpensesPage } from './features/expenses/expenses-page';
import { IntegrationPage } from './features/integration/integration-page';
import { OperationsPage } from './features/operations/operations-page';
import { OrdersPage } from './features/orders/orders-page';
import { NAV_ITEMS } from './layout/nav-items';

const SECTIONS: [string, string, Type<unknown>][] = [
  ['/pulpit', 'Pulpit', DashboardPage],
  ['/zamowienia', 'Zamówienia', OrdersPage],
  ['/wydatki', 'Wydatki', ExpensesPage],
  ['/operacje', 'Operacje', OperationsPage],
  ['/historia-zmian', 'Historia zmian', ChangeHistoryPage],
  ['/integracja', 'Integracja', IntegrationPage],
];

describe('app routes', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter(routes)] });
  });

  it('defines a lazily loaded route for every navigation item', () => {
    for (const item of NAV_ITEMS) {
      const route = routes.find((candidate) => candidate.path === item.path);
      expect(route?.loadComponent).toBeDefined();
      expect(route?.component).toBeUndefined();
      expect(route?.title).toBe(item.label);
      expect(route?.data?.['section']).toBe(item.label);
    }
  });

  it.each(SECTIONS)('loads %s with the page heading %s', async (url, label, page) => {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl(url, page);

    expect(component).toBeInstanceOf(page);
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent?.trim()).toBe(label);
    expect(harness.routeNativeElement?.textContent).toContain(
      'Ten widok powstanie w kolejnych etapach.',
    );
  });

  it.each(['/', '/cokolwiek'])('redirects %s to the dashboard', async (url) => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, DashboardPage);

    expect(TestBed.inject(Router).url).toBe('/pulpit');
  });
});
