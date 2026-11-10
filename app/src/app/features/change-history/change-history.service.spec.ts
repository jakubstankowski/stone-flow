import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  provideTestHttp,
  respondNetworkError,
  respondProblem,
} from '../../../testing/auth-testing';
import { changeHistoryItem, changeHistoryPage } from '../../../testing/change-history-testing';
import { formatDate, todayInWarsaw } from '../../shared/format/pl-format';
import { LIST_ERROR_MESSAGE } from './change-history-models';
import {
  ChangeHistoryService,
  DATE_RANGE_MESSAGE,
  DATE_REQUIRED_MESSAGE,
  defaultChangePeriod,
} from './change-history.service';

describe('defaultChangePeriod', () => {
  it.each([
    ['2026-11-30', '2026-11-01'],
    ['2026-11-09', '2026-10-11'],
    ['2026-01-15', '2025-12-17'],
    ['2028-03-10', '2028-02-10'],
    ['2026-03-10', '2026-02-09'],
  ])('gives the 30 days ending %s from %s', (today, from) => {
    expect(defaultChangePeriod(today)).toEqual({ changedFrom: from, changedTo: today });
  });
});

describe('ChangeHistoryService', () => {
  let service: ChangeHistoryService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    service = TestBed.inject(ChangeHistoryService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function listRequest(): TestRequest {
    return http.expectOne((r) => r.url === '/api/change-history');
  }

  async function respond(page = changeHistoryPage()): Promise<TestRequest> {
    const request = listRequest();
    request.flush(page);
    await Promise.resolve();
    return request;
  }

  it('starts with the last 30 days in Europe/Warsaw, all types and the newest first', async () => {
    const load = service.load();
    const request = listRequest();
    request.flush(changeHistoryPage());
    await load;

    const period = defaultChangePeriod(todayInWarsaw());
    expect(service.filters()).toEqual({
      ...period,
      businessFrom: '',
      businessTo: '',
      objectTypes: [],
    });
    const params = request.request.params;
    expect(params.get('changedFrom')).toBe(period.changedFrom);
    expect(params.get('changedTo')).toBe(period.changedTo);
    expect(params.has('businessFrom')).toBe(false);
    expect(params.has('objectType')).toBe(false);
    expect(params.get('direction')).toBe('desc');
    expect(params.get('page')).toBe('1');
    expect(params.get('pageSize')).toBe('25');
    expect(service.activeFilters()).toEqual([]);
  });

  it('copies items and total from the API', async () => {
    const items = [changeHistoryItem(), changeHistoryItem({ id: '2' })];
    const load = service.load();
    listRequest().flush(changeHistoryPage({ items, total: 27 }));
    await load;

    expect(service.items()).toEqual(items);
    expect(service.total()).toBe(27);
    expect(service.loading()).toBe(false);
  });

  it('goes back to page 1 on every filter change', async () => {
    const page = service.setPage(3);
    const first = listRequest();
    expect(first.request.params.get('page')).toBe('3');
    first.flush(changeHistoryPage());
    await page;

    const change = service.updateFilters({ businessFrom: '2026-10-01' });
    const request = listRequest();
    request.flush(changeHistoryPage());
    await change;

    expect(service.page()).toBe(1);
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('businessFrom')).toBe('2026-10-01');
  });

  it('sorts only by the change time', async () => {
    await service.setSort({ key: 'author', direction: 'asc' });
    http.expectNone((r) => r.url === '/api/change-history');

    const sort = service.setSort({ key: 'occurredAt', direction: 'asc' });
    const request = listRequest();
    request.flush(changeHistoryPage());
    await sort;
    expect(request.request.params.get('direction')).toBe('asc');
  });

  it('does not ask the API with a missing or reversed period', async () => {
    await service.updateFilters({ changedFrom: '' });
    expect(service.changedFromError()).toBe(DATE_REQUIRED_MESSAGE);
    expect(service.filtersInvalid()).toBe(true);

    await service.updateFilters({ changedFrom: '2026-11-10', changedTo: '2026-11-01' });
    expect(service.changedFromError()).toBe(DATE_RANGE_MESSAGE);

    await service.updateFilters({ changedFrom: '2026-11-01', changedTo: '' });
    expect(service.changedToError()).toBe(DATE_REQUIRED_MESSAGE);

    await service.updateFilters({
      changedTo: '2026-11-30',
      businessFrom: '2026-10-31',
      businessTo: '2026-10-01',
    });
    expect(service.businessFromError()).toBe(DATE_RANGE_MESSAGE);
    http.expectNone((r) => r.url === '/api/change-history');
    expect(service.items()).toEqual([]);
  });

  it('treats an incomplete period as a filter, so the empty list talks about filters', async () => {
    await service.updateFilters({ changedFrom: '' });

    expect(service.activeFilters()).toEqual([
      { id: 'period', label: `Zmiana: … – ${formatDate(defaultChangePeriod().changedTo)}` },
    ]);
    http.expectNone((r) => r.url === '/api/change-history');
  });

  describe('across midnight', () => {
    beforeEach(() => {
      vi.useFakeTimers({ toFake: ['Date'] });
      vi.setSystemTime(new Date('2026-11-09T12:00:00Z'));
    });

    afterEach(() => vi.useRealTimers());

    it('moves an untouched default period to the new day and shows no period chip', () => {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({ providers: [provideTestHttp()] });
      const later = TestBed.inject(ChangeHistoryService);
      http = TestBed.inject(HttpTestingController);

      vi.setSystemTime(new Date('2026-11-10T12:00:00Z'));
      later.refreshDefaultPeriod();

      expect(later.filters().changedTo).toBe('2026-11-10');
      expect(later.filters().changedFrom).toBe('2026-10-12');
      expect(later.activeFilters()).toEqual([]);
    });

    it('keeps a period the user chose', async () => {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({ providers: [provideTestHttp()] });
      const later = TestBed.inject(ChangeHistoryService);
      http = TestBed.inject(HttpTestingController);
      const update = later.updateFilters({ changedFrom: '2026-10-01', changedTo: '2026-10-31' });
      http.expectOne((r) => r.url === '/api/change-history').flush(changeHistoryPage());
      await update;

      vi.setSystemTime(new Date('2026-11-10T12:00:00Z'));
      later.refreshDefaultPeriod();

      expect(later.filters().changedFrom).toBe('2026-10-01');
      expect(later.filters().changedTo).toBe('2026-10-31');
    });
  });

  it('pins a 400 to the filter field and shows an unknown key as a general error', async () => {
    const load = service.load();
    respondProblem(listRequest(), 400, {
      errors: {
        businessTo: ['Podaj datę w formacie RRRR-MM-DD.'],
        objectType: ['Nieznany rodzaj obiektu.'],
      },
    });
    await load;
    expect(service.businessToError()).toBe('Podaj datę w formacie RRRR-MM-DD.');
    expect(service.objectTypeError()).toBe('Nieznany rodzaj obiektu.');
    expect(service.generalError()).toBeNull();
    expect(service.error()).toBeNull();

    const again = service.setPage(2);
    respondProblem(listRequest(), 400, {
      errors: { page: ['Numer strony musi być liczbą całkowitą od 1.'] },
    });
    await again;
    expect(service.generalError()).toBe('Numer strony musi być liczbą całkowitą od 1.');

    const change = service.toggleObjectType('expense', true);
    expect(service.generalError()).toBeNull();
    expect(service.objectTypeError()).toBeNull();
    await respond();
    await change;
  });

  it('shows the list error after a network failure', async () => {
    const load = service.load();
    respondNetworkError(listRequest());
    await load;

    expect(service.error()).toBe(LIST_ERROR_MESSAGE);
    expect(service.items()).toEqual([]);
  });

  it('shows chips for a changed period, business dates and types, and removes them', async () => {
    const steps = [
      () => service.updateFilters({ changedFrom: '2026-10-01', changedTo: '2026-10-31' }),
      () => service.updateFilters({ businessFrom: '2026-10-01' }),
      () => service.toggleObjectType('ownerWithdrawal', true),
      () => service.toggleObjectType('expense', true),
    ];
    let last: TestRequest | null = null;
    for (const step of steps) {
      const running = step();
      last = await respond();
      await running;
    }

    expect(service.activeFilters()).toEqual([
      { id: 'period', label: `Zmiana: ${formatDate('2026-10-01')} – ${formatDate('2026-10-31')}` },
      { id: 'business', label: 'Data biznesowa: od 01.10.2026' },
      { id: 'type:expense', label: 'Rodzaj: Wydatek' },
      { id: 'type:ownerWithdrawal', label: 'Rodzaj: Wypłata właściciela' },
    ]);
    expect(last?.request.params.getAll('objectType')).toEqual(['expense', 'ownerWithdrawal']);

    const onlyTo = service.updateFilters({ businessFrom: '', businessTo: '2026-10-31' });
    await respond();
    await onlyTo;
    expect(service.activeFilters()[1].label).toBe('Data biznesowa: do 31.10.2026');

    const both = service.updateFilters({ businessFrom: '2026-10-01' });
    await respond();
    await both;
    expect(service.activeFilters()[1].label).toBe('Data biznesowa: 01.10.2026 – 31.10.2026');

    for (const id of ['type:expense', 'business', 'period']) {
      const removal = service.removeFilter(id);
      await respond();
      await removal;
    }
    expect(service.activeFilters()).toEqual([
      { id: 'type:ownerWithdrawal', label: 'Rodzaj: Wypłata właściciela' },
    ]);
    expect(service.filters()).toMatchObject({
      ...defaultChangePeriod(),
      businessFrom: '',
      businessTo: '',
    });

    const clear = service.clearFilters();
    await respond();
    await clear;
    expect(service.activeFilters()).toEqual([]);
    expect(service.filters().objectTypes).toEqual([]);
  });

  it('keeps the filters and page for the next injection (root service)', async () => {
    const change = service.updateFilters({ businessFrom: '2026-10-01' });
    await respond();
    await change;
    const page = service.setPage(2);
    await respond();
    await page;

    const again = TestBed.inject(ChangeHistoryService);
    expect(again).toBe(service);
    expect(again.filters().businessFrom).toBe('2026-10-01');
    expect(again.page()).toBe(2);
  });
});
