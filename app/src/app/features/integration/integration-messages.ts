import { ApiError, NETWORK_ERROR_MESSAGE } from '../../core/http/api-error';
import { RetryOutcome, SyncRequestOutcome } from './integration-models';
import { NOT_CONFIGURED_TEXT } from './integration-state';

/** Wynik akcji do pokazania w regionie wyniku oraz dalsze kroki. */
export interface ActionMessage {
  readonly text: string;
  /** `role="alert"` dla błędów, `role="status"` dla informacji. */
  readonly alert: boolean;
  /** Wymusić odczyt `/status` po wyniku. */
  readonly refresh: boolean;
  /** Przeładować listę błędów po wyniku („Ponów”). */
  readonly reload: boolean;
}

export const CSRF_MESSAGE = 'Odśwież stronę i spróbuj ponownie.';
export const SYNC_IN_PROGRESS_MESSAGE = 'Synchronizacja już trwa. Poczekaj na jej zakończenie.';
export const RETRY_GENERIC_MESSAGE = 'Nie udało się ponowić. Spróbuj ponownie później.';

/** Komunikat „Synchronizuj teraz” (spec, Behavior 5). */
export function syncMessage(outcome: SyncRequestOutcome): ActionMessage {
  switch (outcome.kind) {
    case 'accepted':
      return info('Synchronizacja uruchomiona. Stan odświeży się automatycznie.');
    case 'inProgress':
      return info(outcome.detail ?? SYNC_IN_PROGRESS_MESSAGE);
    case 'notConfigured':
      return {
        text: outcome.detail ?? NOT_CONFIGURED_TEXT,
        alert: true,
        refresh: true,
        reload: false,
      };
    case 'timeout':
      return {
        text: 'Nie udało się potwierdzić uruchomienia synchronizacji. Sprawdź stan powyżej.',
        alert: true,
        refresh: true,
        reload: false,
      };
    case 'error':
      return failure(outcome.error, false);
  }
}

/** Komunikat „Ponów” (spec, Behavior 5); `order` to numer w ERP albo ID w sklepie. */
export function retryMessage(outcome: RetryOutcome, order: string): ActionMessage {
  const done = (text: string, alert = true): ActionMessage => ({
    text,
    alert,
    refresh: true,
    reload: true,
  });
  switch (outcome.kind) {
    case 'done':
      return outcome.error.status === 'closed'
        ? done(`Zamówienie ${order} zapisane. Błąd zamknięty.`, false)
        : done(
            `Zamówienie ${order} zapisane, ale błąd pozostaje otwarty. Spróbuj ponownie później.`,
          );
    case 'notFound':
      return done(outcome.detail ?? 'Nie znaleziono błędu synchronizacji.');
    case 'closed':
      return done(outcome.detail ?? 'Ten błąd jest już zamknięty.');
    case 'notConfigured':
      return done(outcome.detail ?? NOT_CONFIGURED_TEXT);
    case 'shopFailed':
      return done(
        outcome.detail ??
          'Sklep nie zwrócił zamówienia. Błąd pozostaje otwarty; spróbuj ponownie później.',
      );
    case 'unavailable':
      return done(
        outcome.detail ??
          'Nie udało się zapisać zamówienia. Błąd pozostaje otwarty; spróbuj ponownie później.',
      );
    case 'timeout':
      // Wynik po stronie serwera jest nieznany; ustali go przeładowanie listy.
      return done(RETRY_GENERIC_MESSAGE);
    case 'error':
      return failure(outcome.error, true);
  }
}

function info(text: string): ActionMessage {
  return { text, alert: false, refresh: true, reload: false };
}

/** Sieć i `5xx` oraz `csrf_invalid`: bez odświeżenia; inny problem: komunikat ogólny. */
function failure(error: ApiError, retry: boolean): ActionMessage {
  if (error.kind === 'network') {
    return { text: NETWORK_ERROR_MESSAGE, alert: true, refresh: false, reload: false };
  }
  if (error.kind === 'problem' && error.code === 'csrf_invalid') {
    return { text: CSRF_MESSAGE, alert: true, refresh: false, reload: false };
  }
  if (retry) {
    return {
      text: error.detail ?? RETRY_GENERIC_MESSAGE,
      alert: true,
      refresh: true,
      reload: true,
    };
  }
  return { text: error.detail ?? NETWORK_ERROR_MESSAGE, alert: true, refresh: true, reload: false };
}
