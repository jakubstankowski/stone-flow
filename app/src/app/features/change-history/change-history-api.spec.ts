import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTestHttp } from '../../../testing/auth-testing';
import { changeHistoryItem, changeHistoryPage } from '../../../testing/change-history-testing';
import { ChangeHistoryApi } from './change-history-api';

describe('ChangeHistoryApi', () => {
  let api: ChangeHistoryApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    api = TestBed.inject(ChangeHistoryApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends the periods, direction and page, repeating object types', async () => {
    const result = api.list({
      changedFrom: '2026-11-01',
      changedTo: '2026-11-30',
      businessFrom: '2026-10-01',
      businessTo: '2026-10-31',
      objectTypes: ['ownerWithdrawal', 'expense'],
      direction: 'asc',
      page: 2,
      pageSize: 25,
    });
    const request = http.expectOne((r) => r.url === '/api/change-history');
    expect(request.request.method).toBe('GET');
    const page = changeHistoryPage({ items: [changeHistoryItem()], page: 2 });
    request.flush(page);

    expect(await result).toEqual(page);
    const params = request.request.params;
    expect(params.get('changedFrom')).toBe('2026-11-01');
    expect(params.get('changedTo')).toBe('2026-11-30');
    expect(params.get('businessFrom')).toBe('2026-10-01');
    expect(params.get('businessTo')).toBe('2026-10-31');
    expect(params.getAll('objectType')).toEqual(['expense', 'ownerWithdrawal']);
    expect(params.get('direction')).toBe('asc');
    expect(params.get('page')).toBe('2');
    expect(params.get('pageSize')).toBe('25');
  });

  it('omits empty business dates and object types', async () => {
    const result = api.list({
      changedFrom: '2026-11-01',
      changedTo: '2026-11-30',
      businessFrom: '',
      businessTo: '2026-10-31',
      objectTypes: [],
      direction: 'desc',
      page: 1,
      pageSize: 25,
    });
    const request = http.expectOne((r) => r.url === '/api/change-history');
    request.flush(changeHistoryPage());
    await result;

    expect(request.request.params.has('businessFrom')).toBe(false);
    expect(request.request.params.get('businessTo')).toBe('2026-10-31');
    expect(request.request.params.has('objectType')).toBe(false);
  });
});
