import { HttpTestingController } from '@angular/common/http/testing';
import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { nextRequest, provideTestHttp, signIn, signOut } from '../testing/auth-testing';
import { routes, sectionRoutes } from './app.routes';
import { LoginPage } from './features/auth/login-page';
import { PasswordResetRequestPage } from './features/auth/password-reset-request-page';
import { SetPasswordPage } from './features/auth/set-password-page';
import { ChangeHistoryPage } from './features/change-history/change-history-page';
import { DashboardPage } from './features/dashboard/dashboard-page';
import { ExpensesListPage } from './features/expenses/expenses-list-page';
import { IntegrationPage } from './features/integration/integration-page';
import { OperationsListPage } from './features/operations/operations-list-page';
import { OrdersListPage } from './features/orders/orders-list-page';
import { NAV_ITEMS } from './layout/nav-items';
import {
  beforeLaunchReport,
  discardReportRequests,
  salesComponentsPage,
} from '../testing/dashboard-testing';
import { discardIntegrationRequests } from '../testing/integration-testing';

/** Sekcja: adres, nagłówek, komponent i czy to jeszcze placeholder (SF-14 wypełnia „Integracja”; „Wydatki”, „Operacje” i „Pulpit” testują osobne bloki — SF-15, SF-17, SF-19). */
const SECTIONS: [string, string, Type<unknown>, boolean][] = [
  ['/historia-zmian', 'Historia zmian', ChangeHistoryPage, true],
  ['/integracja', 'Integracja', IntegrationPage, false],
];

const AUTH_SCREENS: [string, string, Type<unknown>][] = [
  ['/logowanie', 'Logowanie', LoginPage],
  ['/reset-hasla', 'Reset hasła', PasswordResetRequestPage],
  ['/reset-hasla/nowe', 'Ustaw hasło', SetPasswordPage],
];

describe('app routes', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter(routes), provideTestHttp()] });
  });

  afterEach(() => {
    discardIntegrationRequests();
    discardReportRequests();
    TestBed.inject(HttpTestingController).verify();
  });

  it('defines a lazily loaded route for every navigation item', () => {
    for (const item of NAV_ITEMS) {
      const route = sectionRoutes.find((candidate) => candidate.path === item.path);
      expect(route?.loadComponent ?? route?.loadChildren).toBeDefined();
      expect(route?.component).toBeUndefined();
      expect(route?.title).toBe(item.label);
      expect(route?.data?.['section']).toBe(item.label);
    }
  });

  it.each(SECTIONS)('loads %s with the page heading %s', async (url, label, page, placeholder) => {
    await signIn();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);

    expect(
      harness.routeDebugElement?.query((node) => node.componentInstance instanceof page),
    ).not.toBeNull();
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent?.trim()).toBe(label);
    if (placeholder) {
      expect(harness.routeNativeElement?.textContent).toContain(
        'Ten widok powstanie w kolejnych etapach.',
      );
    }
    expect(harness.routeNativeElement?.tagName.toLowerCase()).toBe('app-shell');
  });

  it.each([
    ['/zamowienia', 'Zamówienia'],
    ['/zamowienia/nowe', 'Dodaj zamówienie'],
  ])('loads the orders section at %s with the heading %s', async (url, heading) => {
    await signIn();
    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl(url);
    if (url === '/zamowienia') {
      (await nextRequest((request) => request.url === '/api/orders')).flush({
        items: [],
        page: 1,
        pageSize: 25,
        total: 0,
      });
    }
    await navigation;

    expect(harness.routeNativeElement?.tagName.toLowerCase()).toBe('app-shell');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent?.trim()).toBe(heading);
  });

  it.each([
    ['/wydatki', 'Wydatki'],
    ['/wydatki/nowy', 'Dodaj wydatek'],
    ['/wydatki/cykliczne', 'Wydatki cykliczne'],
  ])('loads the expenses section at %s with the heading %s', async (url, heading) => {
    await signIn();
    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl(url);
    if (url === '/wydatki' || url === '/wydatki/cykliczne') {
      // SF-16: ostrzeżenie o zadaniu czyta `/api/jobs` raz przy wejściu.
      (await nextRequest('/api/jobs')).flush([]);
      const list = url === '/wydatki' ? '/api/expenses' : '/api/recurring-expenses';
      (await nextRequest((request) => request.url === list)).flush({
        items: [],
        page: 1,
        pageSize: 25,
        total: 0,
      });
    }
    await navigation;

    expect(harness.routeNativeElement?.tagName.toLowerCase()).toBe('app-shell');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent?.trim()).toBe(heading);
    if (url === '/wydatki') {
      expect(
        harness.routeDebugElement?.query(
          (node) => node.componentInstance instanceof ExpensesListPage,
        ),
      ).not.toBeNull();
    }
  });

  it.each([
    ['/operacje', 'Operacje'],
    ['/operacje/wyplaty/nowa', 'Dodaj wypłatę'],
  ])('loads the operations section at %s with the heading %s', async (url, heading) => {
    await signIn();
    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl(url);
    if (url === '/operacje') {
      (await nextRequest((request) => request.url === '/api/operations')).flush({
        items: [],
        page: 1,
        pageSize: 25,
        total: 0,
        totals: { inflows: '0.00', outflows: '0.00', change: '0.00' },
        effectiveFrom: '2026-11-01',
        launchDate: '2026-11-01',
      });
    } else {
      (await nextRequest('/api/owner-withdrawals/people')).flush({ people: [] });
    }
    await navigation;

    expect(harness.routeNativeElement?.tagName.toLowerCase()).toBe('app-shell');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent?.trim()).toBe(heading);
    if (url === '/operacje') {
      expect(
        harness.routeDebugElement?.query(
          (node) => node.componentInstance instanceof OperationsListPage,
        ),
      ).not.toBeNull();
    }
  });

  it('loads the dashboard at /pulpit with the report requests answered', async () => {
    await signIn();
    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl('/pulpit');
    (await nextRequest((request) => request.url === '/api/reports/sales')).flush(
      beforeLaunchReport(),
    );
    (await nextRequest((request) => request.url === '/api/reports/sales/components')).flush(
      salesComponentsPage({ sum: null }),
    );
    await navigation;
    await harness.fixture.whenStable();

    expect(harness.routeNativeElement?.tagName.toLowerCase()).toBe('app-shell');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent?.trim()).toBe('Pulpit');
    expect(
      harness.routeDebugElement?.query((node) => node.componentInstance instanceof DashboardPage),
    ).not.toBeNull();
    expect(harness.routeNativeElement?.textContent).not.toContain(
      'Ten widok powstanie w kolejnych etapach.',
    );
  });

  it('lists orders with the list page', async () => {
    await signIn();
    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl('/zamowienia');
    (await nextRequest((request) => request.url === '/api/orders')).flush({
      items: [],
      page: 1,
      pageSize: 25,
      total: 0,
    });
    await navigation;

    expect(
      harness.routeDebugElement?.query((node) => node.componentInstance instanceof OrdersListPage),
    ).not.toBeNull();
  });

  it.each(['/', '/cokolwiek'])('redirects %s to the dashboard', async (url) => {
    await signIn();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);

    expect(TestBed.inject(Router).url).toBe('/pulpit');
  });

  it('sends an anonymous user from a section to the login screen', async () => {
    await signOut();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/zamowienia');

    expect(TestBed.inject(Router).url).toBe('/logowanie?powrot=%2Fzamowienia');
  });

  it.each(AUTH_SCREENS)(
    'shows %s outside the shell with the heading %s',
    async (url, label, page) => {
      await signOut();
      const harness = await RouterTestingHarness.create();
      await harness.navigateByUrl(url);
      const element = harness.routeNativeElement as HTMLElement;

      expect(
        harness.routeDebugElement?.query((node) => node.componentInstance instanceof page),
      ).not.toBeNull();
      expect(element.tagName.toLowerCase()).toBe('app-auth-layout');
      expect(element.querySelector('h1')?.textContent?.trim()).toBe(label);
      expect(element.querySelector('nav')).toBeNull();
    },
  );
});
