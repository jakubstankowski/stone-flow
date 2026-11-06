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
import { ExpensesPage } from './features/expenses/expenses-page';
import { IntegrationPage } from './features/integration/integration-page';
import { OperationsPage } from './features/operations/operations-page';
import { OrdersListPage } from './features/orders/orders-list-page';
import { NAV_ITEMS } from './layout/nav-items';
import { discardIntegrationRequests } from '../testing/integration-testing';

/** Sekcja: adres, nagłówek, komponent i czy to jeszcze placeholder (SF-14 wypełnia „Integracja”). */
const SECTIONS: [string, string, Type<unknown>, boolean][] = [
  ['/pulpit', 'Pulpit', DashboardPage, true],
  ['/wydatki', 'Wydatki', ExpensesPage, true],
  ['/operacje', 'Operacje', OperationsPage, true],
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
