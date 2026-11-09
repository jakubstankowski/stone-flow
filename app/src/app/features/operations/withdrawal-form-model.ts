import { ApiError, NETWORK_ERROR_MESSAGE } from '../../core/http/api-error';
import { parsePlnAmount, parseSignedPlnAmount, todayInWarsaw } from '../../shared/format/pl-format';
import { WithdrawalDetails, WithdrawalInput, WithdrawalMethod } from './withdrawal-models';

/** Model formularza wypłaty: wszystkie pola jako tekst (puste = brak wartości). */
export interface WithdrawalFormModel {
  person: string;
  amount: string;
  withdrawnOn: string;
  method: WithdrawalMethod | '';
}

export const WITHDRAWAL_LIMITS = {
  person: 100,
  /** Cyfry części całkowitej w `decimal(18,2)`. */
  amountIntegerDigits: 16,
} as const;

/** Komunikaty walidacji jak w API oraz ogólne komunikaty zapisu i usunięcia. */
export const WITHDRAWAL_MESSAGES = {
  personRequired: 'Podaj osobę.',
  personTooLong: `Osoba może mieć najwyżej ${WITHDRAWAL_LIMITS.person} znaków.`,
  amountRequired: 'Podaj kwotę.',
  amountInvalid: 'Podaj kwotę jako liczbę, np. 1234.50.',
  amountScale: 'Kwota może mieć najwyżej dwa miejsca po przecinku.',
  amountTooLarge: 'Kwota jest zbyt duża.',
  amountNotPositive: 'Kwota musi być większa od 0 zł.',
  dateRequired: 'Podaj datę wypłaty.',
  dateInvalid: 'Podaj poprawną datę wypłaty.',
  dateInFuture: 'Data wypłaty nie może być późniejsza niż dzisiaj.',
  methodRequired: 'Wybierz metodę.',
  saveFailed: 'Nie udało się zapisać wypłaty.',
  deleteFailed: 'Nie udało się usunąć wypłaty.',
  csrf: 'Odśwież stronę i spróbuj ponownie.',
} as const;

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;
const TOO_MANY_DECIMALS = /[.,]\d{3,}$/;

/** Nowa wypłata: data = dziś (Europe/Warsaw), metoda bez wartości domyślnej. */
export function emptyWithdrawalForm(today = todayInWarsaw()): WithdrawalFormModel {
  return { person: '', amount: '', withdrawnOn: today, method: '' };
}

/** Stan z serwera → formularz. Kwota `2000.00` staje się `2000,00` (zamiana znaku, bez liczenia). */
export function withdrawalFormFrom(withdrawal: WithdrawalDetails): WithdrawalFormModel {
  return {
    person: withdrawal.person,
    amount: withdrawal.amount.replace('.', ','),
    withdrawnOn: withdrawal.withdrawnOn,
    method: withdrawal.method,
  };
}

/** Formularz → treść żądania: osoba przycięta, kwota jako kanoniczny ciąg `"2000.00"`. */
export function toWithdrawalInput(model: WithdrawalFormModel): WithdrawalInput {
  return {
    person: model.person.trim(),
    amount: parsePlnAmount(model.amount) ?? model.amount.trim(),
    withdrawnOn: model.withdrawnOn,
    method: model.method as WithdrawalMethod,
  };
}

export function personError(text: string): string | null {
  const trimmed = text.trim();
  if (trimmed === '') {
    return WITHDRAWAL_MESSAGES.personRequired;
  }
  return trimmed.length > WITHDRAWAL_LIMITS.person ? WITHDRAWAL_MESSAGES.personTooLong : null;
}

/** Kwota: wymagana, > 0 zł, najwyżej 2 miejsca po przecinku i 16 cyfr całkowitych (tekstowo). */
export function amountError(text: string): string | null {
  const trimmed = text.trim();
  if (trimmed === '') {
    return WITHDRAWAL_MESSAGES.amountRequired;
  }
  const amount = parsePlnAmount(trimmed);
  if (amount === null) {
    if (parseSignedPlnAmount(trimmed) !== null) {
      return WITHDRAWAL_MESSAGES.amountNotPositive;
    }
    return TOO_MANY_DECIMALS.test(trimmed)
      ? WITHDRAWAL_MESSAGES.amountScale
      : WITHDRAWAL_MESSAGES.amountInvalid;
  }
  if (amount.indexOf('.') > WITHDRAWAL_LIMITS.amountIntegerDigits) {
    return WITHDRAWAL_MESSAGES.amountTooLarge;
  }
  return amount === '0.00' ? WITHDRAWAL_MESSAGES.amountNotPositive : null;
}

/** Data wypłaty: wymagana i nie późniejsza niż dziś (porównanie tekstu `yyyy-MM-dd`). */
export function withdrawnOnError(text: string, today: string): string | null {
  if (text === '') {
    return WITHDRAWAL_MESSAGES.dateRequired;
  }
  if (!ISO_DATE.test(text)) {
    return WITHDRAWAL_MESSAGES.dateInvalid;
  }
  return text > today ? WITHDRAWAL_MESSAGES.dateInFuture : null;
}

export function methodError(method: string): string | null {
  return method === '' ? WITHDRAWAL_MESSAGES.methodRequired : null;
}

/** Komunikat ogólny dla błędu, który nie dotyczy konkretnego pola (wzór wydatków). */
export function withdrawalErrorMessage(error: ApiError, fallback: string): string {
  if (error.kind === 'network') {
    return NETWORK_ERROR_MESSAGE;
  }
  if (error.kind === 'problem' && error.code === 'csrf_invalid') {
    return WITHDRAWAL_MESSAGES.csrf;
  }
  return error.detail ?? fallback;
}
