import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTestHttp, respondProblem } from '../../../testing/auth-testing';
import { ExpensesService } from './expenses.service';

describe('ExpensesService', () => {
  let service: ExpensesService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    service = TestBed.inject(ExpensesService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function flushList(total = 0): void {
    http
      .expectOne((r) => r.url === '/api/expenses')
      .flush({ items: [], page: 1, pageSize: 25, total });
  }

  it('lists active filters and treats the registry as empty only without filters', async () => {
    const load = service.load();
    flushList();
    await load;
    expect(service.registryEmpty()).toBe(true);

    const filtered = service.updateFilters({ costDateFrom: '2026-10-01', state: 'overdue' });
    flushList();
    await filtered;
    const category = service.toggleCategory('vatTax', true);
    flushList();
    await category;

    expect(service.activeFilters().map((filter) => filter.label)).toEqual([
      'Data kosztu od: 01.10.2026',
      'Kategoria: Podatek VAT',
      'Stan: Po terminie',
    ]);
    expect(service.registryEmpty()).toBe(false);
    expect(service.dateFilterActive()).toBe(true);

    const removal = service.removeFilter('category:vatTax');
    flushList();
    await removal;
    expect(service.filters().categories).toEqual([]);
  });

  it('does not ask the API for an inverted payment date range', async () => {
    await service.updateFilters({ paidOnFrom: '2026-11-05', paidOnTo: '2026-11-01' });

    expect(service.paidOnRangeInvalid()).toBe(true);
    http.expectNone((r) => r.url === '/api/expenses');
  });

  it.each([
    [409, 'expense_conflict', 'conflict'],
    [409, 'expense_already_paid', 'stateChanged'],
    [409, 'expense_not_paid', 'stateChanged'],
    [404, 'expense_not_found', 'notFound'],
  ])('maps %i %s to %s', async (status, code, kind) => {
    const outcome = service.markPaid('w1', '2026-11-05', null, 'v1');
    respondProblem(http.expectOne('/api/expenses/w1/mark-paid'), status, { code, detail: 'Tekst' });

    expect((await outcome).kind).toBe(kind);
  });

  it('recognises a reused idempotency key on create', async () => {
    const outcome = service.create(
      {
        description: 'Cięcie',
        category: 'cutting',
        counterparty: null,
        costDate: '2026-10-28',
        dueDate: null,
        paidOn: null,
        grossAmount: '10.00',
        netAmount: null,
        documentReference: null,
        paymentMethod: null,
        payer: null,
        comment: null,
      },
      'klucz',
    );
    respondProblem(http.expectOne('/api/expenses'), 409, {
      code: 'idempotency_key_reused',
      expenseId: 'w9',
    });

    expect(await outcome).toEqual({ kind: 'keyReused', expenseId: 'w9' });
  });

  it('keeps a one-time notice after delete', async () => {
    const outcome = service.delete('w1', 'v1');
    http.expectOne('/api/expenses/w1').flush(null, { status: 204, statusText: 'No Content' });

    expect((await outcome).kind).toBe('saved');
    expect(service.takeNotice()).toBe('Wydatek usunięty.');
    expect(service.takeNotice()).toBeNull();
  });
});
