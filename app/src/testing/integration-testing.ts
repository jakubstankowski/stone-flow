import { HttpTestingController } from '@angular/common/http/testing';
import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { OrderError, SyncStatus } from '../app/features/integration/integration-models';
import {
  IntegrationView,
  dataCompleteOf,
  integrationStateOf,
  lastFullCheckText,
  presentationOf,
} from '../app/features/integration/integration-state';

/** Fikcyjne dane integracji do testów (bez danych identyfikujących). Tylko specyfikacje. */
export const STATUS_URL = '/api/integrations/woocommerce/status';

export function syncStatus(overrides: Partial<SyncStatus> = {}): SyncStatus {
  return {
    configured: true,
    lastSuccessAtUtc: '2026-11-05T21:50:00Z',
    lastRun: {
      status: 'succeeded',
      startedAtUtc: '2026-11-05T22:00:00Z',
      finishedAtUtc: '2026-11-05T22:04:00Z',
      source: 'schedule',
      ordersProcessed: 12,
      errors: 0,
      message: null,
    },
    isRunning: false,
    openErrors: 0,
    ...overrides,
  };
}

export function orderError(overrides: Partial<OrderError> = {}): OrderError {
  return {
    id: 'e1',
    externalId: '1001',
    orderId: 'o1',
    orderNumber: 'Z/2026/0001',
    status: 'open',
    source: 'webhook',
    firstOccurredAtUtc: '2026-11-05T10:00:00Z',
    lastOccurredAtUtc: '2026-11-05T11:00:00Z',
    attempts: 2,
    message: 'Nieczytelna treść zamówienia',
    context: 'abc123',
    closedAtUtc: null,
    closedBy: null,
    ...overrides,
  };
}

/**
 * Odrzuca otwarte żądania integracji: stan wysyłany przez wskaźnik w nagłówku powłoki i lista
 * błędów ekranu „Integracja” w testach, które ich nie dotyczą.
 */
export function discardIntegrationRequests(): void {
  TestBed.inject(HttpTestingController).match((request) =>
    request.url.startsWith('/api/integrations/'),
  );
}

/** Chwila „teraz” w testach komponentów: 6.11.2026, 13:00 w Warszawie. */
export const TEST_NOW = Date.parse('2026-11-06T12:00:00Z');

/** Znany stan z odpowiedzi `/status` dla zastępczego serwisu. */
export function knownView(status: SyncStatus = syncStatus(), aged = false): IntegrationView {
  return {
    phase: 'known',
    status,
    state: integrationStateOf(status, TEST_NOW),
    aged,
    readAtUtc: '2026-11-06T11:58:00Z',
  };
}

/** Zastępczy `IntegrationStatus` na sygnałach: komponenty bez timerów i żądań. */
export class IntegrationStatusStub {
  readonly view = signal<IntegrationView>({ phase: 'loading' });
  readonly now = signal(TEST_NOW);
  readonly announcement = signal('');
  readonly status = computed(() => {
    const view = this.view();
    return view.phase === 'known' ? view.status : view.phase === 'unknown' ? view.last : null;
  });
  readonly presentation = computed(() => presentationOf(this.view(), this.now()));
  readonly dataComplete = computed(() => dataCompleteOf(this.view()));
  readonly lastFullCheck = computed(() => lastFullCheckText(this.view()));
  readonly start = vi.fn();
  readonly stop = vi.fn();
  readonly refresh = vi.fn(async () => undefined);
}
