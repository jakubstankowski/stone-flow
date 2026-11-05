import { ApiError, NETWORK_ERROR_MESSAGE } from '../../core/http/api-error';
import { parsePlnAmount } from '../../shared/format/pl-format';
import { ORDER_MESSAGES, isValidDate, textOrNull, todayInWarsaw } from './order-form-model';
import {
  PaymentEntry,
  PaymentInput,
  PaymentMethod,
  RefundEntry,
  RefundInput,
  RefundKind,
  RefundMethod,
} from './order-models';

/** Model formularza wpłaty: kwota jako tekst wpisany po polsku. */
export interface PaymentFormModel {
  amount: string;
  date: string;
  method: PaymentMethod | '';
  receivedBy: string;
}

/** Model formularza zwrotu: rodzaj bez wartości domyślnej (`''`). */
export interface RefundFormModel {
  kind: RefundKind | '';
  amount: string;
  date: string;
  method: RefundMethod | '';
  reason: string;
}

/**
 * Wynik zapisu przekazany formularzowi wpisu: `null`, gdy rodzic obsłużył wynik sam,
 * błąd API (pola albo komunikat ogólny) albo komunikat reguły `422`.
 */
export type EntryFormOutcome = null | { readonly error: ApiError } | { readonly message: string };

export const ENTRY_LIMITS = {
  receivedBy: 100,
  reason: 500,
  /** Cyfry części całkowitej w `decimal(18,2)`. */
  amountIntegerDigits: 16,
} as const;

export const ENTRY_MESSAGES = {
  amountRequired: 'Podaj kwotę.',
  amountInvalid:
    'Podaj kwotę większą od 0 zł z najwyżej dwoma miejscami po przecinku, np. 1 234,50.',
  amountTooLarge: 'Kwota jest zbyt duża.',
  dateRequired: 'Podaj datę.',
  dateInFuture: 'Data nie może być późniejsza niż dzisiaj.',
  methodRequired: 'Wybierz metodę.',
  receivedByTooLong: `Pole „Przyjął” może mieć najwyżej ${ENTRY_LIMITS.receivedBy} znaków.`,
  kindRequired: 'Wybierz rodzaj zwrotu.',
  reasonRequired: 'Podaj powód zwrotu.',
  reasonTooLong: `Powód może mieć najwyżej ${ENTRY_LIMITS.reason} znaków.`,
  saveFailed: 'Nie udało się zapisać wpisu.',
} as const;

export function emptyPaymentForm(date = todayInWarsaw()): PaymentFormModel {
  return { amount: '', date, method: '', receivedBy: '' };
}

export function emptyRefundForm(date = todayInWarsaw()): RefundFormModel {
  return { kind: '', amount: '', date, method: '', reason: '' };
}

/** Wpis z serwera → formularz. Kwota `300.00` staje się `300,00` (zamiana znaku, bez liczenia). */
export function paymentFormFrom(entry: PaymentEntry): PaymentFormModel {
  return {
    amount: entry.amount.replace('.', ','),
    date: entry.date,
    method: entry.method,
    receivedBy: entry.receivedBy ?? '',
  };
}

export function refundFormFrom(entry: RefundEntry): RefundFormModel {
  return {
    kind: entry.kind,
    amount: entry.amount.replace('.', ','),
    date: entry.date,
    method: entry.method,
    reason: entry.reason,
  };
}

/** Formularz → treść żądania. „Przyjął” idzie tylko przy gotówce; puste pole to `null`. */
export function toPaymentInput(model: PaymentFormModel): PaymentInput {
  return {
    amount: parsePlnAmount(model.amount) ?? model.amount.trim(),
    date: model.date,
    method: model.method as PaymentMethod,
    receivedBy: model.method === 'cash' ? textOrNull(model.receivedBy) : null,
  };
}

export function toRefundInput(model: RefundFormModel): RefundInput {
  return {
    kind: model.kind as RefundKind,
    amount: parsePlnAmount(model.amount) ?? model.amount.trim(),
    date: model.date,
    method: model.method as RefundMethod,
    reason: model.reason.trim(),
  };
}

/** Kwota wpisu: wymagana, większa od 0 zł, najwyżej 2 miejsca, w zakresie `decimal(18,2)`. */
export function entryAmountError(text: string): string | null {
  if (text.trim() === '') {
    return ENTRY_MESSAGES.amountRequired;
  }
  const amount = parsePlnAmount(text);
  // Zero rozpoznajemy po tekście kanonicznym (`0.00`), bez arytmetyki na kwocie.
  if (amount === null || amount === '0.00') {
    return ENTRY_MESSAGES.amountInvalid;
  }
  return amount.indexOf('.') > ENTRY_LIMITS.amountIntegerDigits
    ? ENTRY_MESSAGES.amountTooLarge
    : null;
}

/** Data wpisu: wymagana i nie późniejsza niż dzisiaj według Europe/Warsaw. */
export function entryDateError(text: string, today = todayInWarsaw()): string | null {
  if (text.trim() === '' || !isValidDate(text)) {
    return ENTRY_MESSAGES.dateRequired;
  }
  // Daty `yyyy-MM-dd` porównują się poprawnie jako tekst.
  return text > today ? ENTRY_MESSAGES.dateInFuture : null;
}

/** Komunikat ogólny formularza wpisu dla błędu, który nie dotyczy konkretnego pola. */
export function entryErrorMessage(
  error: ApiError,
  fallback: string = ENTRY_MESSAGES.saveFailed,
): string {
  if (error.kind === 'network') {
    return NETWORK_ERROR_MESSAGE;
  }
  if (error.kind === 'problem' && error.code === 'csrf_invalid') {
    return ORDER_MESSAGES.csrf;
  }
  return error.detail ?? fallback;
}
