import { FieldTree, ValidationError } from '@angular/forms/signals';
import { AuthNotice } from '../../core/auth/auth-session';
import { ApiError, NETWORK_ERROR_MESSAGE, fieldErrorOf } from '../../core/http/api-error';

export const INVALID_CREDENTIALS_MESSAGE =
  'Nieprawidłowy e-mail lub hasło. Po 5 nieudanych próbach logowanie jest blokowane na 15 minut.';
export const RATE_LIMITED_MESSAGE = 'Zbyt wiele prób. Spróbuj ponownie za chwilę.';
export const CSRF_MESSAGE = 'Odśwież stronę i spróbuj ponownie.';
export const RESET_LINK_INVALID_MESSAGE = 'Link wygasł lub został już użyty. Poproś o nowy link.';
export const RESET_SENT_MESSAGE =
  'Jeśli konto istnieje, wysłaliśmy link do ustawienia hasła. Link jest ważny 30 minut.';

export const VALIDATION_MESSAGES = {
  emailRequired: 'Podaj adres e-mail.',
  emailFormat: 'Podaj poprawny adres e-mail.',
  passwordRequired: 'Podaj hasło.',
  newPasswordRequired: 'Podaj nowe hasło.',
  newPasswordTooShort: 'Hasło musi mieć co najmniej 12 znaków.',
  passwordsDiffer: 'Hasła nie są takie same.',
} as const;

export const NEW_PASSWORD_MIN_LENGTH = 12;

export interface NoticeView {
  readonly text: string;
  /** `status` dla informacji, `alert` dla problemu. */
  readonly role: 'status' | 'alert';
}

const NOTICES: Record<AuthNotice, NoticeView> = {
  expired: { text: 'Sesja wygasła. Zaloguj się ponownie.', role: 'status' },
  'logged-out': { text: 'Wylogowano.', role: 'status' },
  'password-set': { text: 'Hasło ustawione. Zaloguj się.', role: 'status' },
  unreachable: { text: NETWORK_ERROR_MESSAGE, role: 'alert' },
};

export function noticeView(notice: AuthNotice | null): NoticeView | null {
  return notice ? NOTICES[notice] : null;
}

/** Komunikat ogólny formularza dla błędu, który nie dotyczy konkretnego pola. */
export function formErrorMessage(error: ApiError): string {
  if (error.kind === 'network') {
    return NETWORK_ERROR_MESSAGE;
  }
  if (error.kind === 'problem' && error.status === 429) {
    return RATE_LIMITED_MESSAGE;
  }
  if (error.kind === 'problem' && error.code === 'csrf_invalid') {
    return CSRF_MESSAGE;
  }
  return error.detail ?? CSRF_MESSAGE;
}

/** Błąd pola do wyświetlenia: po dotknięciu pola albo próbie wysłania formularza. */
export function visibleError(field: FieldTree<string>): string | null {
  const state = field();
  if (!state.touched()) {
    return null;
  }
  return state.errors().find((error) => error.message)?.message ?? null;
}

/** Błędy pól z `ValidationProblemDetails` przypisane do pól formularza (nazwy bez wielkości liter). */
export function serverFieldErrors(
  error: ApiError,
  fields: Readonly<Record<string, FieldTree<string>>>,
): ValidationError.WithFieldTree[] {
  return Object.entries(fields).flatMap(([name, fieldTree]) => {
    const message = fieldErrorOf(error, name);
    return message ? [{ kind: 'server', message, fieldTree }] : [];
  });
}
