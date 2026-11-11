import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTestHttp } from '../../../testing/auth-testing';
import {
  cashFlowReport,
  salesComponentsPage,
  salesReport,
} from '../../../testing/dashboard-testing';
import { ReportsApi } from './reports-api';

describe('ReportsApi', () => {
  let api: ReportsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    api = TestBed.inject(ReportsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends only the period parameters the period kind needs', async () => {
    const month = api.salesReport({ kind: 'currentMonth', year: '2026', from: 'x', to: 'y' });
    const monthRequest = http.expectOne((r) => r.url === '/api/reports/sales');
    expect(monthRequest.request.params.keys()).toEqual(['period']);
    expect(monthRequest.request.params.get('period')).toBe('currentMonth');
    monthRequest.flush(salesReport());
    await month;

    const year = api.salesReport({ kind: 'year', year: '2025', from: 'x', to: 'y' });
    const yearRequest = http.expectOne((r) => r.url === '/api/reports/sales');
    expect(yearRequest.request.params.keys()).toEqual(['period', 'year']);
    expect(yearRequest.request.params.get('year')).toBe('2025');
    yearRequest.flush(salesReport());
    await year;

    const custom = api.salesReport({
      kind: 'custom',
      year: '2026',
      from: '2026-09-01',
      to: '2026-11-15',
    });
    const customRequest = http.expectOne((r) => r.url === '/api/reports/sales');
    expect(customRequest.request.params.keys()).toEqual(['period', 'from', 'to']);
    expect(customRequest.request.params.get('from')).toBe('2026-09-01');
    expect(customRequest.request.params.get('to')).toBe('2026-11-15');
    customRequest.flush(salesReport());
    await custom;
  });

  it('sends the same period with filters, direction and page to the components endpoint', async () => {
    const page = api.salesComponents(
      { kind: 'previousMonth', year: '2026', from: '', to: '' },
      { channel: 'olx', kind: 'order', direction: 'asc', page: 2, pageSize: 25 },
    );
    const request = http.expectOne((r) => r.url === '/api/reports/sales/components');
    const params = request.request.params;
    expect(params.get('period')).toBe('previousMonth');
    expect(params.get('channel')).toBe('olx');
    expect(params.get('kind')).toBe('order');
    expect(params.get('direction')).toBe('asc');
    expect(params.get('page')).toBe('2');
    expect(params.get('pageSize')).toBe('25');
    request.flush(salesComponentsPage());
    await page;

    const unfiltered = api.salesComponents(
      { kind: 'currentMonth', year: '2026', from: '', to: '' },
      { channel: null, kind: null, direction: 'desc', page: 1, pageSize: 25 },
    );
    const plain = http.expectOne((r) => r.url === '/api/reports/sales/components');
    expect(plain.request.params.has('channel')).toBe(false);
    expect(plain.request.params.has('kind')).toBe(false);
    plain.flush(salesComponentsPage());
    await unfiltered;
  });
  it('reads the cash flow report with only the period parameters the period kind needs', async () => {
    const month = api.cashFlowReport({ kind: 'previousMonth', year: '2026', from: 'x', to: 'y' });
    const monthRequest = http.expectOne((r) => r.url === '/api/reports/cash-flow');
    expect(monthRequest.request.method).toBe('GET');
    expect(monthRequest.request.params.keys()).toEqual(['period']);
    expect(monthRequest.request.params.get('period')).toBe('previousMonth');
    monthRequest.flush(cashFlowReport());
    expect(await month).toEqual(cashFlowReport());

    const year = api.cashFlowReport({ kind: 'year', year: '2025', from: 'x', to: 'y' });
    const yearRequest = http.expectOne((r) => r.url === '/api/reports/cash-flow');
    expect(yearRequest.request.params.keys()).toEqual(['period', 'year']);
    yearRequest.flush(cashFlowReport());
    await year;

    const custom = api.cashFlowReport({
      kind: 'custom',
      year: '2026',
      from: '2026-09-01',
      to: '2026-11-15',
    });
    const customRequest = http.expectOne((r) => r.url === '/api/reports/cash-flow');
    expect(customRequest.request.params.keys()).toEqual(['period', 'from', 'to']);
    expect(customRequest.request.params.get('from')).toBe('2026-09-01');
    expect(customRequest.request.params.get('to')).toBe('2026-11-15');
    customRequest.flush(cashFlowReport());
    await custom;
  });
});
