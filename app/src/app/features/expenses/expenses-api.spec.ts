import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTestHttp, respondNoContent } from '../../../testing/auth-testing';
import { ExpenseInput, ExpenseListQuery } from './expense-models';
import { ExpensesApi } from './expenses-api';

const INPUT: ExpenseInput = {
  description: 'Cięcie płyt',
  category: 'cutting',
  counterparty: null,
  costDate: '2026-10-28',
  dueDate: null,
  paidOn: null,
  grossAmount: '-200.00',
  netAmount: null,
  documentReference: null,
  paymentMethod: null,
  payer: null,
  comment: null,
};

describe('ExpensesApi', () => {
  let api: ExpensesApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    api = TestBed.inject(ExpensesApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends list filters as query parameters, repeating categories', async () => {
    const query: ExpenseListQuery = {
      page: 2,
      pageSize: 25,
      sort: 'paidOn',
      direction: 'asc',
      costDateFrom: '2026-10-01',
      costDateTo: null,
      paidOnFrom: null,
      paidOnTo: '2026-11-05',
      categories: ['cutting', 'vatTax'],
      state: 'overdue',
      sign: 'negative',
      search: 'FV',
      recurringDefinitionId: 'c1',
    };
    const result = api.list(query);
    const request = http.expectOne((r) => r.url === '/api/expenses');
    request.flush({ items: [], page: 2, pageSize: 25, total: 0 });
    await result;

    const params = request.request.params;
    expect(params.get('page')).toBe('2');
    expect(params.get('sort')).toBe('paidOn');
    expect(params.get('costDateFrom')).toBe('2026-10-01');
    expect(params.has('costDateTo')).toBe(false);
    expect(params.get('paidOnTo')).toBe('2026-11-05');
    expect(params.getAll('category')).toEqual(['cutting', 'vatTax']);
    expect(params.get('state')).toBe('overdue');
    expect(params.get('sign')).toBe('negative');
    expect(params.get('recurringDefinitionId')).toBe('c1');
    expect(params.get('search')).toBe('FV');
  });

  it('creates with the idempotency key and tells a repeat from a new expense', async () => {
    const result = api.create(INPUT, 'klucz-1');
    const request = http.expectOne('/api/expenses');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Idempotency-Key')).toBe('klucz-1');
    expect(request.request.body).toEqual(INPUT);
    request.flush({ id: 'w1', version: 'v1' }, { status: 200, statusText: 'OK' });

    expect(await result).toEqual({ created: false, expense: { id: 'w1', version: 'v1' } });
  });

  it('marks paid, unmarks and deletes with the version in the body', async () => {
    const mark = api.markPaid('w1', '2026-11-05', 'blik', 'v1');
    const markRequest = http.expectOne('/api/expenses/w1/mark-paid');
    expect(markRequest.request.body).toEqual({
      paidOn: '2026-11-05',
      paymentMethod: 'blik',
      version: 'v1',
    });
    markRequest.flush({ version: 'v2' });
    await mark;

    const unmark = api.unmarkPaid('w1', 'v2');
    const unmarkRequest = http.expectOne('/api/expenses/w1/unmark-paid');
    expect(unmarkRequest.request.body).toEqual({ version: 'v2' });
    unmarkRequest.flush({ version: 'v3' });
    await unmark;

    const removal = api.delete('w1', 'v3');
    const deleteRequest = http.expectOne('/api/expenses/w1');
    expect(deleteRequest.request.method).toBe('DELETE');
    expect(deleteRequest.request.body).toEqual({ version: 'v3' });
    respondNoContent(deleteRequest);
    await removal;
  });

  it('updates with the version next to the input', async () => {
    const update = api.update('w1', INPUT, 'v1');
    const request = http.expectOne('/api/expenses/w1');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ ...INPUT, version: 'v1' });
    request.flush({ version: 'v2' });

    expect(await update).toEqual({ version: 'v2' });
  });
});
