import { HttpErrorResponse } from '@angular/common/http';

export const NETWORK_ERROR_MESSAGE = 'Nie udało się połączyć z serwerem.';

/** Błąd API sprowadzony do tego, czego potrzebuje interfejs (Problem Details z `detail` i `code`). */
export type ApiError =
  | { readonly kind: 'network' }
  | {
      readonly kind: 'validation';
      /** Błędy pól; klucze małymi literami, bo API może zwrócić `NewPassword` albo `newPassword`. */
      readonly fieldErrors: Readonly<Record<string, readonly string[]>>;
      readonly detail: string | null;
    }
  | {
      readonly kind: 'problem';
      readonly status: number;
      readonly detail: string | null;
      readonly code: string | null;
    };

/**
 * Brak połączenia (status 0) i błędy serwera (5xx, w tym 503 przy niedostępnej bazie) dają
 * `network`; `400` z `errors` daje `validation`; reszta to `problem`.
 */
export function toApiError(error: unknown): ApiError {
  if (!(error instanceof HttpErrorResponse) || error.status === 0 || error.status >= 500) {
    return { kind: 'network' };
  }
  const body = asRecord(error.error);
  const detail = stringOrNull(body?.['detail']);
  const errors = asRecord(body?.['errors']);
  if (error.status === 400 && errors) {
    return { kind: 'validation', fieldErrors: normalizeFieldErrors(errors), detail };
  }
  return { kind: 'problem', status: error.status, detail, code: stringOrNull(body?.['code']) };
}

/** Pierwszy komunikat API dla pola, bez rozróżniania wielkości liter w nazwie. */
export function fieldErrorOf(error: ApiError, field: string): string | null {
  if (error.kind !== 'validation') {
    return null;
  }
  return error.fieldErrors[field.toLowerCase()]?.[0] ?? null;
}

function normalizeFieldErrors(errors: Record<string, unknown>): Record<string, readonly string[]> {
  const result: Record<string, string[]> = {};
  for (const [key, value] of Object.entries(errors)) {
    const messages = Array.isArray(value)
      ? value.filter((item): item is string => typeof item === 'string')
      : [];
    const normalized = key.toLowerCase();
    result[normalized] = [...(result[normalized] ?? []), ...messages];
  }
  return result;
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function stringOrNull(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}
