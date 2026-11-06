import { ApiError } from '../../core/http/api-error';

/** Odpowiedź `GET /api/integrations/woocommerce/status` (SF-13); znaczniki to ISO 8601 UTC. */
export interface SyncStatus {
  readonly configured: boolean;
  /** Kursor ostatniego sukcesu (start udanego przebiegu − 10 min); `null` przed pierwszym. */
  readonly lastSuccessAtUtc: string | null;
  readonly lastRun: SyncRun | null;
  readonly isRunning: boolean;
  readonly openErrors: number;
}

/** Ostatni przebieg uzupełniania; `status`: `running`, `succeeded`, `failed`, `abandoned`. */
export interface SyncRun {
  readonly status: string;
  readonly startedAtUtc: string;
  readonly finishedAtUtc: string | null;
  /** `schedule` albo `manual`. */
  readonly source: string;
  readonly ordersProcessed: number;
  readonly errors: number;
  readonly message: string | null;
}

export type OrderErrorStatus = 'open' | 'closed';
export type OrderErrorFilter = OrderErrorStatus | 'all';

/** Błąd zamówienia z historii; `source` i `closedBy`: `backfill`, `webhook`, `retry`. */
export interface OrderError {
  readonly id: string;
  readonly externalId: string;
  readonly orderId: string | null;
  readonly orderNumber: string | null;
  readonly status: string;
  readonly source: string;
  readonly firstOccurredAtUtc: string;
  readonly lastOccurredAtUtc: string;
  readonly attempts: number;
  readonly message: string;
  readonly context: string;
  readonly closedAtUtc: string | null;
  readonly closedBy: string | null;
}

export interface OrderErrorPage {
  readonly items: readonly OrderError[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface OrderErrorQuery {
  readonly status: OrderErrorFilter;
  readonly page: number;
  readonly pageSize: number;
}

/** Wynik „Synchronizuj teraz”. */
export type SyncRequestOutcome =
  | { readonly kind: 'accepted' }
  | { readonly kind: 'inProgress'; readonly detail: string | null }
  | { readonly kind: 'notConfigured'; readonly detail: string | null }
  | { readonly kind: 'timeout' }
  | { readonly kind: 'error'; readonly error: ApiError };

/** Wynik „Ponów”. */
export type RetryOutcome =
  | { readonly kind: 'done'; readonly error: OrderError }
  | {
      readonly kind: 'notFound' | 'closed' | 'notConfigured' | 'shopFailed' | 'unavailable';
      readonly detail: string | null;
    }
  | { readonly kind: 'timeout' }
  | { readonly kind: 'error'; readonly error: ApiError };
