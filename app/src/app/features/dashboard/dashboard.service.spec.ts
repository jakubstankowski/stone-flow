import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  provideTestHttp,
  respondNetworkError,
  respondProblem,
} from '../../../testing/auth-testing';
import {
  beforeLaunchCashFlowReport,
  cashFlowReport,
  reportPeriod,
  salesComponentItem,
  salesComponentsPage,
  salesReport,
} from '../../../testing/dashboard-testing';
import {
  CASH_FLOW_ERROR_MESSAGE,
  COMPONENTS_ERROR_MESSAGE,
  DashboardService,
  REPORT_ERROR_MESSAGE,
  latestEnd,
} from './dashboard.service';

const REPORT = '/api/reports/sales';
const COMPONENTS = '/api/reports/sales/components';
const CASH_FLOW = '/api/reports/cash-flow';
const RANGE_PROBLEM = {
  code: 'report_range_too_large',
  detail: 'Za dużo danych w wybranym okresie. Wybierz krótszy okres.',
};

describe('DashboardService', () => {
  let service: DashboardService;
  let http: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-11-09T12:00:00Z'));
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    service = TestBed.inject(DashboardService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.useRealTimers();
    http.verify();
  });

  const report = (): TestRequest => http.expectOne((r) => r.url === REPORT);
  const components = (): TestRequest => http.expectOne((r) => r.url === COMPONENTS);
  const cashFlow = (): TestRequest => http.expectOne((r) => r.url === CASH_FLOW);

  /** Przełączenie na „Przepływy pieniężne” z wczytanym raportem przepływów. */
  async function toCashFlow(answer = cashFlowReport()): Promise<void> {
    service.setView('cashFlow');
    cashFlow().flush(answer);
    await vi.waitFor(() => expect(service.cashFlowLoading()).toBe(false));
  }

  async function enter(page = salesComponentsPage()): Promise<void> {
    service.enter();
    report().flush(salesReport());
    components().flush(page);
    await vi.waitFor(() =>
      expect(service.reportLoading() || service.componentsLoading()).toBe(false),
    );
  }

  it('starts with the current month of the current year and the sales view', async () => {
    expect(service.view()).toBe('sales');
    expect(service.selection()).toEqual({ kind: 'currentMonth', year: '2026', from: '', to: '' });
    await enter();
    expect(service.report()?.kpis?.sales.value).toBe('1200.00');
    expect(service.componentsPage()?.sum).toBe('1200.00');
  });

  it('does not ask the API for an invalid custom range and shows the messages at the fields', async () => {
    service.setSelection({ kind: 'custom' });
    expect(service.selectionErrors()).toEqual({ from: 'Podaj datę.', to: 'Podaj datę.' });

    service.setSelection({ from: '2026-11-10', to: '2026-11-01' });
    expect(service.selectionErrors().from).toBe('Data „od” nie może być późniejsza niż data „do”.');

    service.setSelection({ from: '2026-11-01', to: '2027-11-10' });
    expect(service.selectionErrors().to).toBe(
      'Data końcowa nie może być późniejsza niż 09.11.2027 (rok od dziś).',
    );
    http.expectNone((r) => r.url.startsWith(REPORT));
    expect(service.report()).toBeNull();

    service.setSelection({ to: '2027-11-09' });
    expect(service.selectionInvalid()).toBe(false);
    report().flush(salesReport());
    components().flush(salesComponentsPage());
  });

  it('computes the latest end date as text, with 29 February moving to 28 February', () => {
    expect(latestEnd('2026-11-09')).toBe('2027-11-09');
    expect(latestEnd('2028-02-29')).toBe('2029-02-28');
    expect(service.maxDate()).toBe('2027-11-09');
  });

  it('resets the components to page one without filters when the period changes', async () => {
    await enter();
    const filtered = service.showChannel('olx');
    components().flush(salesComponentsPage());
    await filtered;
    const paged = service.setComponentsPage(3);
    components().flush(salesComponentsPage());
    await paged;

    service.setSelection({ kind: 'previousMonth' });
    expect(report().request.params.get('period')).toBe('previousMonth');
    const next = components();
    expect(next.request.params.get('page')).toBe('1');
    expect(next.request.params.has('channel')).toBe(false);
    expect(next.request.params.get('direction')).toBe('desc');
    expect(service.channel()).toBeNull();
  });

  it('reloads the report and the current components page with the kept state on every entry', async () => {
    await enter();
    service.setSelection({ kind: 'year', year: '2026' });
    report().flush(salesReport());
    components().flush(salesComponentsPage());
    const kind = service.showOrders();
    components().flush(salesComponentsPage());
    await kind;
    const page = service.setComponentsPage(2);
    components().flush(salesComponentsPage());
    await page;

    service.enter();

    const again = report();
    expect(again.request.params.get('period')).toBe('year');
    expect(again.request.params.get('year')).toBe('2026');
    again.flush(salesReport({ calculatedAtUtc: '2026-11-09T12:30:00+00:00' }));
    const current = components();
    expect(current.request.params.get('kind')).toBe('order');
    expect(current.request.params.get('page')).toBe('2');
    current.flush(salesComponentsPage());
    await vi.waitFor(() =>
      expect(service.report()?.calculatedAtUtc).toBe('2026-11-09T12:30:00+00:00'),
    );
  });

  it('shows the 422 detail at the period picker and hides the data', async () => {
    service.enter();
    respondProblem(report(), 422, {
      code: 'report_range_too_large',
      detail: 'Za dużo danych w wybranym okresie. Wybierz krótszy okres.',
    });
    components().flush(salesComponentsPage());

    await vi.waitFor(() =>
      expect(service.rangeError()).toBe(
        'Za dużo danych w wybranym okresie. Wybierz krótszy okres.',
      ),
    );
    expect(service.report()).toBeNull();
    expect(service.reportError()).toBeNull();
  });

  it('keeps report and components errors apart', async () => {
    service.enter();
    respondNetworkError(report());
    components().flush(salesComponentsPage());
    await vi.waitFor(() => expect(service.reportError()).toBe(REPORT_ERROR_MESSAGE));
    expect(service.componentsError()).toBeNull();

    service.enter();
    report().flush(salesReport());
    respondNetworkError(components());
    await vi.waitFor(() => expect(service.componentsError()).toBe(COMPONENTS_ERROR_MESSAGE));
    expect(service.reportError()).toBeNull();
  });

  it('ignores a stale report answer', async () => {
    service.enter();
    const stale = report();
    components().flush(salesComponentsPage());
    service.setSelection({ kind: 'previousMonth' });
    const fresh = report();
    components().flush(salesComponentsPage());

    fresh.flush(salesReport({ period: reportPeriod({ kind: 'previousMonth' }) }));
    stale.flush(salesReport());

    await vi.waitFor(() => expect(service.reportLoading()).toBe(false));
    expect(service.report()?.period.kind).toBe('previousMonth');
  });

  it('switches the components filter for cards and channels and lists active chips', async () => {
    await enter();

    const orders = service.showOrders();
    const ordersRequest = components();
    expect(ordersRequest.request.params.get('kind')).toBe('order');
    expect(ordersRequest.request.params.has('channel')).toBe(false);
    ordersRequest.flush(salesComponentsPage());
    await orders;
    expect(service.activeComponentFilters().map((f) => f.label)).toEqual(['Rodzaj: Zamówienie']);

    const channel = service.showChannel('phone');
    const channelRequest = components();
    expect(channelRequest.request.params.get('channel')).toBe('phone');
    expect(channelRequest.request.params.has('kind')).toBe(false);
    channelRequest.flush(salesComponentsPage());
    await channel;
    expect(service.activeComponentFilters().map((f) => f.label)).toEqual(['Kanał: Telefon']);

    const all = service.showAllComponents();
    components().flush(salesComponentsPage({ items: [salesComponentItem()] }));
    await all;
    expect(service.activeComponentFilters()).toEqual([]);
  });

  it('offers years from the launch year and builds expense links from the effective period', async () => {
    expect(service.yearOptions()).toEqual(['2026']);
    service.enter();
    report().flush(
      salesReport({
        period: reportPeriod({ launchDate: '2024-06-01', effectiveFrom: '2026-11-03' }),
      }),
    );
    components().flush(salesComponentsPage());
    await vi.waitFor(() => expect(service.report()).not.toBeNull());

    expect(service.yearOptions()).toEqual(['2026', '2025', '2024']);
    expect(service.costsLink()).toEqual({
      link: ['/wydatki'],
      queryParams: { kosztOd: '2026-11-03', kosztDo: '2026-11-30' },
    });
    expect(service.categoryLink('vatTax')?.queryParams).toEqual({
      kosztOd: '2026-11-03',
      kosztDo: '2026-11-30',
      kategoria: 'vatTax',
    });
  });

  it('keeps known years selectable when loading a past year fails', async () => {
    service.enter();
    report().flush(salesReport({ period: reportPeriod({ launchDate: '2024-06-01' }) }));
    components().flush(salesComponentsPage());
    await vi.waitFor(() => expect(service.report()).not.toBeNull());

    service.setSelection({ kind: 'year', year: '2025' });
    respondNetworkError(report());
    components().flush(salesComponentsPage());
    await vi.waitFor(() => expect(service.reportError()).toBe(REPORT_ERROR_MESSAGE));

    expect(service.report()).toBeNull();
    expect(service.yearOptions()).toEqual(['2026', '2025', '2024']);
    expect(service.selection().year).toBe('2025');
  });

  it('passes amounts through from the API without changing them', async () => {
    service.enter();
    report().flush(salesReport());
    components().flush(salesComponentsPage({ sum: '-300.00' }));
    await vi.waitFor(() => expect(service.componentsPage()).not.toBeNull());

    expect(service.componentsPage()?.sum).toBe('-300.00');
    expect(service.report()?.kpis?.result.comparison.difference).toBe('-50.00');
  });
  describe('cash flow view (SF-20)', () => {
    it('loads only the active view on entry, view switch and period change', async () => {
      // F-S1
      await toCashFlow();
      http.expectNone((r) => r.url === REPORT || r.url === COMPONENTS);
      expect(service.cashFlowReport()?.kpis?.inflows.value).toBe('1500.00');

      service.enter();
      cashFlow().flush(cashFlowReport());
      http.expectNone((r) => r.url === REPORT || r.url === COMPONENTS);

      service.setView('cashFlow');
      http.expectNone((r) => r.url === CASH_FLOW);

      service.setView('sales');
      report().flush(salesReport());
      components().flush(salesComponentsPage());
      http.expectNone((r) => r.url === CASH_FLOW);
    });

    it('resets the sales components on a period change in the cash flow view and loads them later', async () => {
      // F-S1
      await enter();
      const filtered = service.showChannel('olx');
      components().flush(salesComponentsPage());
      await filtered;
      const paged = service.setComponentsPage(3);
      components().flush(salesComponentsPage());
      await paged;
      await toCashFlow();

      service.setSelection({ kind: 'previousMonth' });
      const request = cashFlow();
      expect(request.request.params.get('period')).toBe('previousMonth');
      request.flush(cashFlowReport());
      http.expectNone((r) => r.url === REPORT || r.url === COMPONENTS);
      expect(service.channel()).toBeNull();
      expect(service.page()).toBe(1);
      expect(service.sort()).toEqual({ key: 'date', direction: 'desc' });

      service.setView('sales');
      expect(report().request.params.get('period')).toBe('previousMonth');
      const next = components();
      expect(next.request.params.get('page')).toBe('1');
      expect(next.request.params.has('channel')).toBe(false);
      expect(next.request.params.get('period')).toBe('previousMonth');
    });

    it('keeps the 422 message of one view out of the other', async () => {
      // F-S2
      service.enter();
      respondProblem(report(), 422, RANGE_PROBLEM);
      components().flush(salesComponentsPage());
      await vi.waitFor(() => expect(service.rangeError()).toBe(RANGE_PROBLEM.detail));

      service.setView('cashFlow');
      expect(service.rangeError()).toBeNull();
      respondProblem(cashFlow(), 422, RANGE_PROBLEM);
      await vi.waitFor(() => expect(service.rangeError()).toBe(RANGE_PROBLEM.detail));
      expect(service.cashFlowError()).toBeNull();
      expect(service.cashFlowReport()).toBeNull();

      service.setView('sales');
      expect(service.rangeError()).toBeNull();
      report().flush(salesReport());
      components().flush(salesComponentsPage());
    });

    it('keeps the cash flow error apart from the sales errors and clears it on leaving', async () => {
      // F-S2
      service.setView('cashFlow');
      respondNetworkError(cashFlow());
      await vi.waitFor(() => expect(service.cashFlowError()).toBe(CASH_FLOW_ERROR_MESSAGE));
      expect(service.reportError()).toBeNull();
      expect(service.componentsError()).toBeNull();

      service.setView('sales');
      expect(service.cashFlowError()).toBeNull();
      respondNetworkError(report());
      components().flush(salesComponentsPage());
      await vi.waitFor(() => expect(service.reportError()).toBe(REPORT_ERROR_MESSAGE));
      expect(service.cashFlowError()).toBeNull();

      service.setView('cashFlow');
      expect(service.reportError()).toBeNull();
      cashFlow().flush(cashFlowReport());
    });

    it('ignores stale cash flow answers and sales answers after leaving the sales view', async () => {
      // F-S3
      service.enter();
      const salesAnswer = report();
      const componentsAnswer = components();
      service.setView('cashFlow');
      expect(service.reportLoading()).toBe(false);
      expect(service.componentsLoading()).toBe(false);
      const first = cashFlow();

      salesAnswer.flush(salesReport());
      componentsAnswer.flush(salesComponentsPage());
      await Promise.resolve();
      expect(service.report()).toBeNull();
      expect(service.componentsPage()).toBeNull();
      expect(service.reportLoading()).toBe(false);

      service.setSelection({ kind: 'previousMonth' });
      const second = cashFlow();
      service.setSelection({ kind: 'currentMonth' });
      const third = cashFlow();
      third.flush(cashFlowReport({ calculatedAtUtc: '2026-11-09T12:30:00+00:00' }));
      second.flush(cashFlowReport({ period: reportPeriod({ kind: 'previousMonth' }) }));
      first.flush(cashFlowReport({ period: reportPeriod({ kind: 'previousMonth' }) }));

      await vi.waitFor(() => expect(service.cashFlowLoading()).toBe(false));
      expect(service.cashFlowReport()?.calculatedAtUtc).toBe('2026-11-09T12:30:00+00:00');
      expect(service.cashFlowReport()?.period.kind).toBe('currentMonth');
      expect(service.report()).toBeNull();
      expect(service.componentsPage()).toBeNull();
      expect(service.reportLoading()).toBe(false);
    });

    it('ignores a cash flow answer after switching sales, cash flow, sales quickly', async () => {
      // F-S3: A → B → A
      service.enter();
      report().flush(salesReport());
      components().flush(salesComponentsPage());
      service.setView('cashFlow');
      const pending = cashFlow();
      service.setView('sales');
      report().flush(salesReport());
      components().flush(salesComponentsPage());

      pending.flush(cashFlowReport());
      await vi.waitFor(() => expect(service.reportLoading()).toBe(false));
      expect(service.cashFlowReport()).toBeNull();
      expect(service.cashFlowLoading()).toBe(false);
    });

    it('gives the active report to the header and none while it loads', async () => {
      await enter();
      expect(service.activeReport()).toBe(service.report());

      service.setView('cashFlow');
      expect(service.activeReport()).toBeNull();
      cashFlow().flush(cashFlowReport({ calculatedAtUtc: '2026-11-09T12:45:00+00:00' }));
      await vi.waitFor(() =>
        expect(service.activeReport()?.calculatedAtUtc).toBe('2026-11-09T12:45:00+00:00'),
      );
    });

    it('builds links to the operations list from the effective period', async () => {
      // F-S4
      expect(service.cashFlowLinks()).toBeNull();
      expect(service.outflowKindLink('expense')).toBeNull();
      await toCashFlow();

      const base = { od: '2026-11-03', do: '2026-11-30' };
      expect(service.cashFlowLinks()).toEqual({
        inflows: { link: ['/operacje'], queryParams: { ...base, rodzaj: 'payment' } },
        outflows: {
          link: ['/operacje'],
          queryParams: { ...base, rodzaj: ['refund', 'expense', 'withdrawal'] },
        },
        change: { link: ['/operacje'], queryParams: base },
        all: { link: ['/operacje'], queryParams: base },
      });
      expect(service.outflowKindLink('withdrawal')).toEqual({
        link: ['/operacje'],
        queryParams: { ...base, rodzaj: 'withdrawal' },
      });
      expect(service.costsLink()).toBeNull();

      service.setSelection({ kind: 'custom', from: '2026-08-01', to: '2026-08-31' });
      cashFlow().flush(beforeLaunchCashFlowReport());
      await vi.waitFor(() => expect(service.cashFlowLoading()).toBe(false));
      expect(service.cashFlowLinks()).toBeNull();
      expect(service.outflowKindLink('refund')).toBeNull();
    });

    it('offers years from the launch date of the last loaded report', async () => {
      // F-S5
      await toCashFlow(cashFlowReport({ period: reportPeriod({ launchDate: '2024-06-01' }) }));
      expect(service.yearOptions()).toEqual(['2026', '2025', '2024']);

      service.setView('sales');
      report().flush(salesReport({ period: reportPeriod({ launchDate: '2025-03-01' }) }));
      components().flush(salesComponentsPage());
      await vi.waitFor(() => expect(service.report()).not.toBeNull());
      expect(service.yearOptions()).toEqual(['2026', '2025']);
    });

    it('passes the cash flow strings through from the API without arithmetic', async () => {
      // F-S6
      const answer = cashFlowReport();
      await toCashFlow(answer);

      expect(service.cashFlowReport()).toEqual(answer);
      expect(service.cashFlowReport()?.recentOperations[1].amount).toBe('-50.00');
      expect(service.cashFlowReport()?.kpis?.change.comparison.difference).toBe('-200.00');
    });
  });
});
