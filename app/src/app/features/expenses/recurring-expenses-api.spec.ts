import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTestHttp, respondNoContent } from '../../../testing/auth-testing';
import { recurringPreview } from '../../../testing/expenses-testing';
import { JobsApi } from './jobs-api';
import { RecurringExpenseInput } from './recurring-expense-models';
import { RecurringExpensesApi } from './recurring-expenses-api';

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

describe('RecurringExpensesApi', () => {
  let api: RecurringExpensesApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    api = TestBed.inject(RecurringExpensesApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends list filters as query parameters', async () => {
    const result = api.list({
      page: 2,
      pageSize: 25,
      sort: 'startsOn',
      direction: 'desc',
      state: 'paused',
      search: 'Abon',
    });
    const request = http.expectOne((r) => r.url === '/api/recurring-expenses');
    expect(request.request.params.toString()).toBe(
      'page=2&pageSize=25&sort=startsOn&direction=desc&state=paused&search=Abon',
    );
    request.flush({ items: [], page: 2, pageSize: 25, total: 0 });
    expect((await result).total).toBe(0);
  });

  it('asks for a preview with optional end and definition id', async () => {
    const result = api.preview({
      dayOfMonth: 31,
      startsOn: '2026-09-01',
      endsOn: '2027-01-31',
      definitionId: 'c1',
    });
    const request = http.expectOne((r) => r.url === '/api/recurring-expenses/preview');
    expect(request.request.params.toString()).toBe(
      'dayOfMonth=31&startsOn=2026-09-01&endsOn=2027-01-31&definitionId=c1',
    );
    request.flush(recurringPreview({ occurrencesToCreate: 2 }));
    expect((await result).occurrencesToCreate).toBe(2);
  });

  it('creates with the Idempotency-Key header and tells 201 from 200', async () => {
    const created = api.create(INPUT, 'key-1');
    const first = http.expectOne({ method: 'POST', url: '/api/recurring-expenses' });
    expect(first.request.headers.get('Idempotency-Key')).toBe('key-1');
    expect(first.request.body).toEqual(INPUT);
    first.flush(
      { id: 'c1', version: 'v1', createdOccurrences: 3 },
      { status: 201, statusText: 'Created' },
    );
    expect(await created).toEqual({
      created: true,
      definition: { id: 'c1', version: 'v1', createdOccurrences: 3 },
    });

    const repeated = api.create(INPUT, 'key-1');
    http
      .expectOne({ method: 'POST', url: '/api/recurring-expenses' })
      .flush({ id: 'c1', version: 'v1', createdOccurrences: 3 });
    expect((await repeated).created).toBe(false);
  });

  it('sends the version with update, pause, resume and delete', async () => {
    const update = api.update('c1', INPUT, 'v1');
    const put = http.expectOne({ method: 'PUT', url: '/api/recurring-expenses/c1' });
    expect(put.request.body).toEqual({ ...INPUT, version: 'v1' });
    put.flush({ version: 'v2', createdOccurrences: 0 });
    await update;

    const pause = api.pause('c1', 'v2');
    const pauseRequest = http.expectOne('/api/recurring-expenses/c1/pause');
    expect(pauseRequest.request.body).toEqual({ version: 'v2' });
    pauseRequest.flush({ version: 'v3', createdOccurrences: 1 });
    expect((await pause).createdOccurrences).toBe(1);

    const resume = api.resume('c1', 'v3');
    http
      .expectOne('/api/recurring-expenses/c1/resume')
      .flush({ version: 'v4', createdOccurrences: 0 });
    await resume;

    const remove = api.delete('c1', 'v4');
    const deleteRequest = http.expectOne({ method: 'DELETE', url: '/api/recurring-expenses/c1' });
    expect(deleteRequest.request.body).toEqual({ version: 'v4' });
    respondNoContent(deleteRequest);
    await remove;
  });

  it('reads the job statuses', async () => {
    const result = TestBed.inject(JobsApi).list();
    http.expectOne({ method: 'GET', url: '/api/jobs' }).flush([]);
    expect(await result).toEqual([]);
  });
});
