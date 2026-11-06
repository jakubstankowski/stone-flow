import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, TimeoutError, firstValueFrom, timeout } from 'rxjs';
import { toApiError } from '../../core/http/api-error';
import {
  OrderError,
  OrderErrorPage,
  OrderErrorQuery,
  RetryOutcome,
  SyncRequestOutcome,
  SyncStatus,
} from './integration-models';

const BASE = '/api/integrations/woocommerce';

/**
 * Limity czasu po stronie klienta (spec v4, Assumptions 36). Przerywają tylko oczekiwanie:
 * serwer może dokończyć operację, a duplikatów pilnuje backend.
 */
export const STATUS_TIMEOUT_MS = 15_000;
export const ERRORS_TIMEOUT_MS = 15_000;
export const SYNC_TIMEOUT_MS = 15_000;
/** Backend woła sklep z limitem 30 s, więc „Ponów” czeka dłużej. */
export const RETRY_TIMEOUT_MS = 45_000;

/** Endpointy `/api/integrations/woocommerce` (SF-13): względne adresy, JSON camelCase. */
@Injectable({ providedIn: 'root' })
export class IntegrationApi {
  private readonly http = inject(HttpClient);

  /** Stan synchronizacji; błąd (także `TimeoutError`) jest przekazywany dalej. */
  status(): Promise<SyncStatus> {
    return within(this.http.get<SyncStatus>(`${BASE}/status`), STATUS_TIMEOUT_MS);
  }

  errors(query: OrderErrorQuery): Promise<OrderErrorPage> {
    const params = new HttpParams()
      .set('status', query.status)
      .set('page', query.page)
      .set('pageSize', query.pageSize);
    return within(this.http.get<OrderErrorPage>(`${BASE}/errors`, { params }), ERRORS_TIMEOUT_MS);
  }

  async requestSync(): Promise<SyncRequestOutcome> {
    try {
      await within(this.http.post<unknown>(`${BASE}/sync`, null), SYNC_TIMEOUT_MS);
      return { kind: 'accepted' };
    } catch (error) {
      if (error instanceof TimeoutError) {
        return { kind: 'timeout' };
      }
      const apiError = toApiError(error);
      if (apiError.kind === 'problem' && apiError.status === 409) {
        if (apiError.code === 'sync_in_progress') {
          return { kind: 'inProgress', detail: apiError.detail };
        }
        if (apiError.code === 'integration_not_configured') {
          return { kind: 'notConfigured', detail: apiError.detail };
        }
      }
      return { kind: 'error', error: apiError };
    }
  }

  async retry(id: string): Promise<RetryOutcome> {
    try {
      const error = await within(
        this.http.post<OrderError>(`${BASE}/errors/${encodeURIComponent(id)}/retry`, null),
        RETRY_TIMEOUT_MS,
      );
      return { kind: 'done', error };
    } catch (error) {
      if (error instanceof TimeoutError) {
        return { kind: 'timeout' };
      }
      // `toApiError` sprowadza 5xx do `network` i gubi `code`, więc 502/503 z kodem czytamy tu.
      if (error instanceof HttpErrorResponse && (error.status === 502 || error.status === 503)) {
        const body = problemBody(error);
        if (error.status === 502 && body.code === 'retry_failed') {
          return { kind: 'shopFailed', detail: body.detail };
        }
        if (error.status === 503 && body.code === 'retry_unavailable') {
          return { kind: 'unavailable', detail: body.detail };
        }
      }
      const apiError = toApiError(error);
      if (apiError.kind === 'problem') {
        if (apiError.status === 404 && apiError.code === 'error_not_found') {
          return { kind: 'notFound', detail: apiError.detail };
        }
        if (apiError.status === 409 && apiError.code === 'error_closed') {
          return { kind: 'closed', detail: apiError.detail };
        }
        if (apiError.status === 409 && apiError.code === 'integration_not_configured') {
          return { kind: 'notConfigured', detail: apiError.detail };
        }
      }
      return { kind: 'error', error: apiError };
    }
  }
}

function within<T>(source: Observable<T>, ms: number): Promise<T> {
  return firstValueFrom(source.pipe(timeout(ms)));
}

function problemBody(error: HttpErrorResponse): {
  readonly code: string | null;
  readonly detail: string | null;
} {
  const body: unknown = error.error;
  if (typeof body !== 'object' || body === null) {
    return { code: null, detail: null };
  }
  const record = body as Record<string, unknown>;
  const text = (value: unknown) => (typeof value === 'string' && value.length > 0 ? value : null);
  return { code: text(record['code']), detail: text(record['detail']) };
}
