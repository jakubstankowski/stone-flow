import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTestHttp } from '../../../testing/auth-testing';
import { operationListPage } from '../../../testing/operations-testing';
import { OperationsApi } from './operations-api';

describe('OperationsApi', () => {
  let api: OperationsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    api = TestBed.inject(OperationsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends the period, direction and page, repeating kinds', async () => {
    const result = api.list({
      from: '2026-11-01',
      to: '2026-11-30',
      kinds: ['refund', 'withdrawal'],
      direction: 'asc',
      page: 2,
      pageSize: 25,
    });
    const request = http.expectOne((r) => r.url === '/api/operations');
    expect(request.request.method).toBe('GET');
    const page = operationListPage({ page: 2 });
    request.flush(page);

    expect(await result).toEqual(page);
    const params = request.request.params;
    expect(params.get('from')).toBe('2026-11-01');
    expect(params.get('to')).toBe('2026-11-30');
    expect(params.getAll('kind')).toEqual(['refund', 'withdrawal']);
    expect(params.get('direction')).toBe('asc');
    expect(params.get('page')).toBe('2');
    expect(params.get('pageSize')).toBe('25');
  });

  it('omits kind when all kinds are wanted', async () => {
    const result = api.list({
      from: '2026-11-01',
      to: '2026-11-30',
      kinds: [],
      direction: 'desc',
      page: 1,
      pageSize: 25,
    });
    const request = http.expectOne((r) => r.url === '/api/operations');
    request.flush(operationListPage());
    await result;

    expect(request.request.params.has('kind')).toBe(false);
  });
});
