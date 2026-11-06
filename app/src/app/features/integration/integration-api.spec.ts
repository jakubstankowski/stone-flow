import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TimeoutError } from 'rxjs';
import {
  provideTestHttp,
  respondNetworkError,
  respondProblem,
} from '../../../testing/auth-testing';
import { orderError, syncStatus } from '../../../testing/integration-testing';
import {
  IntegrationApi,
  RETRY_TIMEOUT_MS,
  STATUS_TIMEOUT_MS,
  SYNC_TIMEOUT_MS,
} from './integration-api';

const BASE = '/api/integrations/woocommerce';

describe('IntegrationApi', () => {
  let api: IntegrationApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    api = TestBed.inject(IntegrationApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.useRealTimers();
    http.verify();
  });

  it('reads the status', async () => {
    const result = api.status();
    http.expectOne({ method: 'GET', url: `${BASE}/status` }).flush(syncStatus());
    expect(await result).toEqual(syncStatus());
  });

  it('reads a page of errors with the filter', async () => {
    const result = api.errors({ status: 'closed', page: 2, pageSize: 25 });
    const request = http.expectOne((r) => r.url === `${BASE}/errors`);
    expect(request.request.params.get('status')).toBe('closed');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('25');
    request.flush({ items: [orderError()], page: 2, pageSize: 25, total: 26 });
    expect((await result).total).toBe(26);
  });

  describe('requestSync', () => {
    it('accepts 202', async () => {
      const result = api.requestSync();
      const request = http.expectOne({ method: 'POST', url: `${BASE}/sync` });
      request.flush(null, { status: 202, statusText: 'Accepted' });
      expect(await result).toEqual({ kind: 'accepted' });
    });

    it.each([
      ['sync_in_progress', 'inProgress'],
      ['integration_not_configured', 'notConfigured'],
    ])('maps 409 %s', async (code, kind) => {
      const result = api.requestSync();
      respondProblem(http.expectOne(`${BASE}/sync`), 409, { code, detail: 'Opis' });
      expect(await result).toEqual({ kind, detail: 'Opis' });
    });

    it('maps the CSRF problem and network errors to errors', async () => {
      const csrf = api.requestSync();
      respondProblem(http.expectOne(`${BASE}/sync`), 400, { code: 'csrf_invalid' });
      expect(await csrf).toMatchObject({ kind: 'error', error: { code: 'csrf_invalid' } });

      const network = api.requestSync();
      respondNetworkError(http.expectOne(`${BASE}/sync`));
      expect(await network).toEqual({ kind: 'error', error: { kind: 'network' } });
    });
  });

  describe('retry', () => {
    const url = `${BASE}/errors/e1/retry`;

    it('returns the changed error on 200', async () => {
      const result = api.retry('e1');
      http.expectOne({ method: 'POST', url }).flush(orderError({ status: 'closed' }));
      expect(await result).toEqual({ kind: 'done', error: orderError({ status: 'closed' }) });
    });

    it.each([
      [404, 'error_not_found', 'notFound'],
      [409, 'error_closed', 'closed'],
      [409, 'integration_not_configured', 'notConfigured'],
      [502, 'retry_failed', 'shopFailed'],
      [503, 'retry_unavailable', 'unavailable'],
    ])('maps %i %s', async (status, code, kind) => {
      const result = api.retry('e1');
      respondProblem(http.expectOne(url), status, { code, detail: 'Opis' });
      expect(await result).toEqual({ kind, detail: 'Opis' });
    });

    it('maps 503 without a code and network errors to a network error', async () => {
      const unavailable = api.retry('e1');
      respondProblem(http.expectOne(url), 503);
      expect(await unavailable).toEqual({ kind: 'error', error: { kind: 'network' } });

      const network = api.retry('e1');
      respondNetworkError(http.expectOne(url));
      expect(await network).toEqual({ kind: 'error', error: { kind: 'network' } });
    });

    it('keeps 404 without a code as a problem', async () => {
      const result = api.retry('e1');
      respondProblem(http.expectOne(url), 404);
      expect(await result).toMatchObject({
        kind: 'error',
        error: { kind: 'problem', status: 404 },
      });
    });
  });

  describe('time limits', () => {
    beforeEach(() => vi.useFakeTimers());

    it('stops waiting for the status after 15 s', async () => {
      const result = api.status();
      const request = http.expectOne(`${BASE}/status`);
      const rejection = expect(result).rejects.toBeInstanceOf(TimeoutError);
      vi.advanceTimersByTime(STATUS_TIMEOUT_MS);
      await rejection;
      expect(request.cancelled).toBe(true);
    });

    it('stops waiting for the sync request after 15 s', async () => {
      const result = api.requestSync();
      http.expectOne(`${BASE}/sync`);
      vi.advanceTimersByTime(SYNC_TIMEOUT_MS);
      expect(await result).toEqual({ kind: 'timeout' });
    });

    it('waits 45 s for a retry', async () => {
      const result = api.retry('e1');
      const request = http.expectOne(`${BASE}/errors/e1/retry`);
      vi.advanceTimersByTime(STATUS_TIMEOUT_MS);
      expect(request.cancelled).toBe(false);
      vi.advanceTimersByTime(RETRY_TIMEOUT_MS - STATUS_TIMEOUT_MS);
      expect(await result).toEqual({ kind: 'timeout' });
    });
  });
});
