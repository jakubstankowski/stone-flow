import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  provideTestHttp,
  respondNetworkError,
  respondProblem,
} from '../../../testing/auth-testing';
import { operationItem, operationListPage } from '../../../testing/operations-testing';
import { todayInWarsaw } from '../../shared/format/pl-format';
import {
  DATE_RANGE_MESSAGE,
  DATE_REQUIRED_MESSAGE,
  LIST_ERROR_MESSAGE,
  OperationsService,
  currentMonth,
} from './operations.service';

describe('currentMonth', () => {
  it.each([
    ['2026-11-09', '2026-11-01', '2026-11-30'],
    ['2026-02-15', '2026-02-01', '2026-02-28'],
    ['2028-02-10', '2028-02-01', '2028-02-29'],
    ['2026-12-31', '2026-12-01', '2026-12-31'],
  ])('gives the month of %s as %s – %s', (today, from, to) => {
    expect(currentMonth(today)).toEqual({ from, to });
  });
});

describe('OperationsService', () => {
  let service: OperationsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    service = TestBed.inject(OperationsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function listRequest(): TestRequest {
    return http.expectOne((r) => r.url === '/api/operations');
  }

  it('starts with the current month in Europe/Warsaw, all kinds and the newest first', async () => {
    const load = service.load();
    const request = listRequest();
    request.flush(operationListPage());
    await load;

    const month = currentMonth(todayInWarsaw());
    expect(service.filters()).toEqual({ ...month, kinds: [] });
    expect(request.request.params.get('from')).toBe(month.from);
    expect(request.request.params.get('to')).toBe(month.to);
    expect(request.request.params.get('direction')).toBe('desc');
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('25');
  });

  it('copies items, total and totals from the API without arithmetic', async () => {
    const items = [
      operationItem(),
      operationItem({
        kind: 'expense',
        id: 'e1',
        direction: 'outflow',
        amount: '-50.00',
        sourceType: 'expense',
      }),
    ];
    const totals = { inflows: '1000.00', outflows: '-50.00', change: '1050.00' };
    const load = service.load();
    listRequest().flush(operationListPage({ items, total: 27, totals }));
    await load;

    expect(service.items()).toEqual(items);
    expect(service.total()).toBe(27);
    expect(service.totals()).toEqual(totals);
  });

  it('keeps the totals while another page loads and hides them while a new filter loads', async () => {
    const totals = { inflows: '1000.00', outflows: '200.00', change: '800.00' };
    const first = service.load();
    listRequest().flush(operationListPage({ total: 60, totals }));
    await first;

    const paged = service.setPage(2);
    expect(service.totals()).toEqual(totals);
    listRequest().flush(operationListPage({ page: 2, total: 60, totals }));
    await paged;

    const filtered = service.toggleKind('payment', true);
    expect(service.totals()).toBeNull();
    listRequest().flush(operationListPage({ totals }));
    await filtered;
  });

  it('does not ask the API for a missing or inverted period and explains it at the field', async () => {
    await service.updatePeriod({ from: '' });
    expect(service.fromError()).toBe(DATE_REQUIRED_MESSAGE);
    expect(service.periodInvalid()).toBe(true);

    await service.updatePeriod({ from: '2026-11-20', to: '2026-11-10' });
    expect(service.fromError()).toBe(DATE_RANGE_MESSAGE);
    expect(service.toError()).toBeNull();

    await service.updatePeriod({ to: '' });
    expect(service.toError()).toBe(DATE_REQUIRED_MESSAGE);

    http.expectNone((r) => r.url === '/api/operations');
    expect(service.items()).toEqual([]);
    expect(service.totals()).toBeNull();
  });

  it('returns to the first page after a filter change and keeps the kind order fixed', async () => {
    const paged = service.setPage(3);
    listRequest().flush(operationListPage({ page: 3 }));
    await paged;
    expect(service.page()).toBe(3);

    const withdrawal = service.toggleKind('withdrawal', true);
    listRequest().flush(operationListPage());
    await withdrawal;
    const payment = service.toggleKind('payment', true);
    const request = listRequest();
    request.flush(operationListPage());
    await payment;

    expect(service.page()).toBe(1);
    expect(request.request.params.getAll('kind')).toEqual(['payment', 'withdrawal']);
    expect(service.kindFilterActive()).toBe(true);
  });

  it('sorts only by date', async () => {
    await service.setSort({ key: 'amount', direction: 'asc' });
    http.expectNone((r) => r.url === '/api/operations');

    const sorted = service.setSort({ key: 'date', direction: 'asc' });
    const request = listRequest();
    request.flush(operationListPage());
    await sorted;
    expect(request.request.params.get('direction')).toBe('asc');
  });

  it('shows the period and kinds as chips and removes them', async () => {
    const period = service.updatePeriod({ from: '2026-10-01', to: '2026-10-31' });
    listRequest().flush(operationListPage());
    await period;
    const refund = service.toggleKind('refund', true);
    listRequest().flush(operationListPage());
    await refund;

    expect(service.activeFilters().map((filter) => filter.label)).toEqual([
      'Okres: 01.10.2026 – 31.10.2026',
      'Rodzaj: Zwrot',
    ]);

    const kindRemoval = service.removeFilter('kind:refund');
    listRequest().flush(operationListPage());
    await kindRemoval;
    expect(service.filters().kinds).toEqual([]);

    const periodRemoval = service.removeFilter('period');
    listRequest().flush(operationListPage());
    await periodRemoval;
    expect(service.filters()).toEqual({ ...currentMonth(todayInWarsaw()), kinds: [] });
  });

  it('tells where operations start when the period begins before the launch date', async () => {
    const before = service.updatePeriod({ from: '2026-10-01', to: '2026-11-30' });
    listRequest().flush(
      operationListPage({ effectiveFrom: '2026-11-03', launchDate: '2026-11-03' }),
    );
    await before;
    expect(service.launchNotice()).toBe('Operacje liczone od 03.11.2026 (data uruchomienia).');

    const after = service.updatePeriod({ from: '2026-11-10' });
    listRequest().flush(
      operationListPage({ effectiveFrom: '2026-11-10', launchDate: '2026-11-03' }),
    );
    await after;
    expect(service.launchNotice()).toBeNull();
  });

  it('shows 422 operations_range_too_large at the period and hides the totals', async () => {
    const load = service.load();
    respondProblem(listRequest(), 422, {
      code: 'operations_range_too_large',
      detail: 'Za dużo operacji w wybranym okresie. Wybierz krótszy okres.',
    });
    await load;

    expect(service.rangeError()).toBe(
      'Za dużo operacji w wybranym okresie. Wybierz krótszy okres.',
    );
    expect(service.error()).toBeNull();
    expect(service.items()).toEqual([]);
    expect(service.totals()).toBeNull();
  });

  it('shows a list error after a network failure', async () => {
    const load = service.load();
    respondNetworkError(listRequest());
    await load;

    expect(service.error()).toBe(LIST_ERROR_MESSAGE);
    expect(service.rangeError()).toBeNull();
    expect(service.totals()).toBeNull();
  });
});
