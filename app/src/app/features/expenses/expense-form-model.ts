import { ApiError, NETWORK_ERROR_MESSAGE } from '../../core/http/api-error';
import { parseSignedPlnAmount, todayInWarsaw } from '../../shared/format/pl-format';
import {
  ExpenseCategory,
  ExpenseDetails,
  ExpenseInput,
  ExpensePaymentMethod,
  isNegativeAmount,
} from './expense-models';

/** Model formularza wydatku: wszystkie pola jako tekst (puste = brak wartości). */
export interface ExpenseFormModel {
  description: string;
  category: ExpenseCategory | '';
  counterparty: string;
  costDate: string;
  dueDate: string;
  paidOn: string;
  grossAmount: string;
  netAmount: string;
  documentReference: string;
  paymentMethod: ExpensePaymentMethod | '';
  payer: string;
  comment: string;
}

/** Model formularza „Oznacz jako opłacony”. */
export interface ExpensePaidFormModel {
  paidOn: string;
  paymentMethod: ExpensePaymentMethod | '';
}

export const EXPENSE_LIMITS = {
  description: 200,
  counterparty: 200,
  documentReference: 100,
  payer: 100,
  comment: 2000,
  /** Cyfry części całkowitej w `decimal(18,2)`. */
  amountIntegerDigits: 16,
} as const;

export const EXPENSE_MESSAGES = {
  descriptionRequired: 'Podaj opis wydatku.',
  descriptionTooLong: `Opis może mieć najwyżej ${EXPENSE_LIMITS.description} znaków.`,
  categoryRequired: 'Wybierz kategorię.',
  counterpartyTooLong: `Kontrahent może mieć najwyżej ${EXPENSE_LIMITS.counterparty} znaków.`,
  costDateRequired: 'Podaj datę kosztu.',
  dateInvalid: 'Podaj poprawną datę.',
  paidOnRequired: 'Podaj datę zapłaty.',
  paidOnInFuture: 'Data zapłaty nie może być późniejsza niż dzisiaj.',
  grossRequired: 'Podaj kwotę brutto.',
  amountInvalid:
    'Podaj kwotę z najwyżej dwoma miejscami po przecinku, np. 1 234,50 albo -1 234,50.',
  amountTooLarge: 'Kwota jest zbyt duża.',
  grossZero: 'Kwota brutto nie może wynosić 0 zł.',
  netSign: 'Kwota netto musi mieć ten sam znak co brutto.',
  documentReferenceTooLong: `Referencja dokumentu może mieć najwyżej ${EXPENSE_LIMITS.documentReference} znaków.`,
  payerTooLong: `Płatnik może mieć najwyżej ${EXPENSE_LIMITS.payer} znaków.`,
  commentTooLong: `Komentarz może mieć najwyżej ${EXPENSE_LIMITS.comment} znaków.`,
  saveFailed: 'Nie udało się zapisać wydatku.',
  csrf: 'Odśwież stronę i spróbuj ponownie.',
} as const;

/** Opis ujemnej kwoty brutto przy polu (design guide, formularz wydatku). */
export const NEGATIVE_AMOUNT_HINT =
  'Obniża koszty w okresie daty kosztu, a po zapłacie — wypływy w okresie daty zapłaty.';

/** Opis skutku zapłaty zależny od znaku kwoty brutto. */
export function paidEffectText(grossAmount: string): string {
  return isNegativeAmount(grossAmount)
    ? 'Korekta / zwrot od dostawcy obniży wypływy w okresie daty zapłaty.'
    : 'Wydatek trafi do wypływów w okresie daty zapłaty.';
}

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

export function emptyExpenseForm(costDate = todayInWarsaw()): ExpenseFormModel {
  return {
    description: '',
    category: '',
    counterparty: '',
    costDate,
    dueDate: '',
    paidOn: '',
    grossAmount: '',
    netAmount: '',
    documentReference: '',
    paymentMethod: '',
    payer: '',
    comment: '',
  };
}

/** Stan z serwera → formularz. Kwota `-1234.50` staje się `-1234,50` (zamiana znaku, bez liczenia). */
export function expenseFormFrom(expense: ExpenseDetails): ExpenseFormModel {
  return {
    description: expense.description,
    category: expense.category,
    counterparty: expense.counterparty ?? '',
    costDate: expense.costDate,
    dueDate: expense.dueDate ?? '',
    paidOn: expense.paidOn ?? '',
    grossAmount: expense.grossAmount.replace('.', ','),
    netAmount: expense.netAmount?.replace('.', ',') ?? '',
    documentReference: expense.documentReference ?? '',
    paymentMethod: expense.paymentMethod ?? '',
    payer: expense.payer ?? '',
    comment: expense.comment ?? '',
  };
}

/** Formularz → treść żądania: teksty przycięte, puste jako `null`, kwoty jako `"-1234.50"`. */
export function toExpenseInput(model: ExpenseFormModel): ExpenseInput {
  const net = model.netAmount.trim();
  return {
    description: model.description.trim(),
    category: model.category || null,
    counterparty: textOrNull(model.counterparty),
    costDate: model.costDate,
    dueDate: model.dueDate || null,
    paidOn: model.paidOn || null,
    grossAmount: parseSignedPlnAmount(model.grossAmount) ?? model.grossAmount.trim(),
    netAmount: net === '' ? null : (parseSignedPlnAmount(net) ?? net),
    documentReference: textOrNull(model.documentReference),
    paymentMethod: model.paymentMethod || null,
    payer: textOrNull(model.payer),
    comment: textOrNull(model.comment),
  };
}

export function textOrNull(text: string): string | null {
  const trimmed = text.trim();
  return trimmed.length > 0 ? trimmed : null;
}

export function isValidDate(text: string): boolean {
  return ISO_DATE.test(text);
}

/** Kwota ze znakiem: format i zakres; `null`, gdy poprawna. Zero brutto sprawdza `grossAmountError`. */
function signedAmountError(text: string): string | null {
  const amount = parseSignedPlnAmount(text);
  if (amount === null) {
    return EXPENSE_MESSAGES.amountInvalid;
  }
  const integerDigits = amount.replace('-', '').indexOf('.');
  return integerDigits > EXPENSE_LIMITS.amountIntegerDigits
    ? EXPENSE_MESSAGES.amountTooLarge
    : null;
}

/** Brutto: wymagane, format, różne od 0 zł. */
export function grossAmountError(text: string): string | null {
  if (text.trim() === '') {
    return EXPENSE_MESSAGES.grossRequired;
  }
  const format = signedAmountError(text);
  if (format) {
    return format;
  }
  return parseSignedPlnAmount(text) === '0.00' ? EXPENSE_MESSAGES.grossZero : null;
}

/**
 * Netto: opcjonalne, 0,00 zł dozwolone; niezerowe ma znak brutto. Porównanie |netto| ≤ |brutto|
 * robi wyłącznie API (frontend nie liczy kwot).
 */
export function netAmountError(net: string, gross: string): string | null {
  if (net.trim() === '') {
    return null;
  }
  const format = signedAmountError(net);
  if (format) {
    return format;
  }
  const netAmount = parseSignedPlnAmount(net);
  const grossAmount = parseSignedPlnAmount(gross);
  if (
    netAmount === null ||
    grossAmount === null ||
    netAmount === '0.00' ||
    grossAmount === '0.00'
  ) {
    return null;
  }
  return isNegativeAmount(netAmount) !== isNegativeAmount(grossAmount)
    ? EXPENSE_MESSAGES.netSign
    : null;
}

/** Data opcjonalna: pusta albo poprawna. */
export function optionalDateError(text: string): string | null {
  return text === '' || isValidDate(text) ? null : EXPENSE_MESSAGES.dateInvalid;
}

/** Data zapłaty: opcjonalna (albo wymagana w akcji zapłaty) i nie późniejsza niż dzisiaj. */
export function paidOnError(text: string, today: string, required = false): string | null {
  if (text === '') {
    return required ? EXPENSE_MESSAGES.paidOnRequired : null;
  }
  if (!isValidDate(text)) {
    return EXPENSE_MESSAGES.dateInvalid;
  }
  // Daty `yyyy-MM-dd` porównują się poprawnie jako tekst.
  return text > today ? EXPENSE_MESSAGES.paidOnInFuture : null;
}

/** Komunikat ogólny formularza dla błędu, który nie dotyczy konkretnego pola. */
export function expenseErrorMessage(error: ApiError): string {
  if (error.kind === 'network') {
    return NETWORK_ERROR_MESSAGE;
  }
  if (error.kind === 'problem' && error.code === 'csrf_invalid') {
    return EXPENSE_MESSAGES.csrf;
  }
  return error.detail ?? EXPENSE_MESSAGES.saveFailed;
}
