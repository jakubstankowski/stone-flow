import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { nextRequest, provideTestHttp, respondProblem } from '../../../testing/auth-testing';
import { RecurringExpenseInput } from './recurring-expense-models';
import { RecurringExpensesService, savedNotice } from './recurring-expenses.service';

const INPUT: RecurringExpenseInput = {
  description: 'Abonament reklamowy',
  category: 'marketing',
  counterparty: null,
  grossAmount: '123.00',
  netAmount: null,
  paymentMethod: null,
  payer: null,
  comment: null,
  dayOfMonth: 10,
  startsOn: '2026-09-01',
  endsOn: null,
};

describe('RecurringExpensesService', () => {
  let service: RecurringExpensesService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    service = TestBed.inject(RecurringExpensesService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads sorted by description and returns to page 1 after a filter change', async () => {
    const first = service.load();
    const request = await nextRequest((r) => r.url === '/api/recurring-expenses');
    expect(request.request.params.get('sort')).toBe('description');
    expect(request.request.params.get('direction')).toBe('asc');
    request.flush({ items: [], page: 1, pageSize: 25, total: 30 });
    await first;

    const paged = service.setPage(2);
    (await nextRequest((r) => r.url === '/api/recurring-expenses')).flush({
      items: [],
      page: 2,
      pageSize: 25,
      total: 30,
    });
    await paged;

    const filtered = service.updateFilters({ state: 'paused' });
    const filteredRequest = await nextRequest((r) => r.url === '/api/recurring-expenses');
    expect(filteredRequest.request.params.get('page')).toBe('1');
    expect(filteredRequest.request.params.get('state')).toBe('paused');
    filteredRequest.flush({ items: [], page: 1, pageSize: 25, total: 0 });
    await filtered;
    expect(service.activeFilters()).toEqual([{ id: 'state', label: 'Stan: Wstrzymane' }]);
    expect(service.registryEmpty()).toBe(false);
  });

  it('builds the save notice with the declined number of occurrences', () => {
    expect(savedNotice('Wydatek cykliczny zapisany.', 0)).toBe('Wydatek cykliczny zapisany.');
    expect(savedNotice('Wydatek cykliczny zapisany.', 3)).toBe(
      'Wydatek cykliczny zapisany. Utworzono 3 wystąpienia.',
    );
    expect(savedNotice('Zmiany zapisane.', 5)).toBe('Zmiany zapisane. Utworzono 5 wystąpień.');
  });

  it('maps create outcomes: key reused, deleted definition and 503', async () => {
    const reused = service.create(INPUT, 'k');
    respondProblem(await nextRequest({ method: 'POST', url: '/api/recurring-expenses' }), 409, {
      code: 'idempotency_key_reused',
      recurringExpenseId: 'c9',
    });
    expect(await reused).toEqual({ kind: 'keyReused', definitionId: 'c9' });

    const deleted = service.create(INPUT, 'k');
    respondProblem(await nextRequest({ method: 'POST', url: '/api/recurring-expenses' }), 409, {
      code: 'recurring_expense_deleted',
    });
    expect(await deleted).toEqual({ kind: 'deleted' });

    const busy = service.create(INPUT, 'k');
    respondProblem(await nextRequest({ method: 'POST', url: '/api/recurring-expenses' }), 503, {
      code: 'recurring_expense_busy',
    });
    expect(await busy).toEqual({ kind: 'retry' });
  });

  it('maps change outcomes by code', async () => {
    const cases: [number, string, string][] = [
      [409, 'recurring_expense_conflict', 'conflict'],
      [409, 'recurring_expense_already_paused', 'stateChanged'],
      [409, 'recurring_expense_ended', 'stateChanged'],
      [404, 'recurring_expense_not_found', 'notFound'],
      [503, 'recurring_expense_busy', 'retry'],
    ];
    for (const [status, code, kind] of cases) {
      const outcome = service.pause('c1', 'v1');
      respondProblem(await nextRequest('/api/recurring-expenses/c1/pause'), status, { code });
      expect((await outcome).kind).toBe(kind);
    }

    const resumed = service.resume('c1', 'v1');
    respondProblem(await nextRequest('/api/recurring-expenses/c1/resume'), 409, {
      code: 'recurring_expense_not_paused',
      detail: 'Wydatek cykliczny nie jest wstrzymany.',
    });
    expect(await resumed).toEqual({
      kind: 'stateChanged',
      detail: 'Wydatek cykliczny nie jest wstrzymany.',
    });

    const updated = service.update('c1', INPUT, 'v1');
    respondProblem(await nextRequest({ method: 'PUT', url: '/api/recurring-expenses/c1' }), 409, {
      code: 'recurring_expense_paused',
    });
    expect((await updated).kind).toBe('stateChanged');
  });
});
