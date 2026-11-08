import { ApiError } from '../../core/http/api-error';
import { parsePlnAmount, parseSignedPlnAmount, todayInWarsaw } from '../../shared/format/pl-format';
import { ExpenseFormModel, emptyExpenseForm, isValidDate, textOrNull } from './expense-form-model';
import type { ExpenseFormOutcome } from './expense-form';
import {
  RecurringExpenseDetails,
  RecurringExpenseInput,
  RecurringPreviewQuery,
} from './recurring-expense-models';

/** Pola harmonogramu w formularzu (tekst; puste = brak wartości). */
export interface RecurringScheduleModel {
  dayOfMonth: string;
  startsOn: string;
  endsOn: string;
}

/** Model formularza wydatku rozszerzony o tryb definicji („Powtarzaj co miesiąc”). */
export interface ExpenseEditorModel extends ExpenseFormModel, RecurringScheduleModel {
  recurring: boolean;
}

export type RecurringFormAction = (input: RecurringExpenseInput) => Promise<ExpenseFormOutcome>;

/**
 * Tryb definicji formularza wydatku. `create`: pole wyboru „Powtarzaj co miesiąc” (dodanie),
 * `edit`: stały tryb definicji (edycja), bez pola wyboru.
 */
export interface RecurringFormOptions {
  readonly kind: 'create' | 'edit';
  readonly schedule: RecurringScheduleModel;
  /** Przy `create`: opcja zaznaczona na wejściu (`?cykliczny=1`). */
  readonly checked: boolean;
  /** Przy edycji: id do podglądu. */
  readonly definitionId: string | null;
  /** Definicja wstrzymana: pole dnia zablokowane (NC6). */
  readonly paused: boolean;
  readonly action: RecurringFormAction;
}

export const RECURRING_LIMITS = {
  minDay: 1,
  maxDay: 31,
  minDate: '2000-01-01',
  maxDate: '2099-12-31',
} as const;

export const RECURRING_MESSAGES = {
  dayInvalid: 'Dzień miesiąca musi być liczbą od 1 do 31.',
  startsOnRequired: 'Podaj początek.',
  startsOnRange: 'Początek musi przypadać w latach 2000–2099.',
  endsOnRange: 'Koniec musi przypadać nie później niż 31.12.2099.',
  endsBeforeStart: 'Koniec nie może być wcześniejszy niż początek.',
  grossRequired: 'Podaj kwotę brutto.',
  amountInvalid: 'Podaj kwotę z najwyżej dwoma miejscami po przecinku, np. 1 234,50.',
  grossNotPositive: 'Kwota wydatku cyklicznego musi być większa od 0 zł.',
  netNegative: 'Kwota netto nie może być ujemna.',
  saveFailed: 'Nie udało się zapisać wydatku cyklicznego.',
  /** `503` (kolizja, zakleszczenie) i brak połączenia przy zapisie. */
  retry: 'Nie udało się zapisać. Spróbuj ponownie.',
  conflict: 'Wydatek cykliczny został zmieniony w międzyczasie. Wczytaj aktualne dane.',
  notFound: 'Nie znaleziono wydatku cyklicznego. Mógł zostać usunięty.',
  csrf: 'Odśwież stronę i spróbuj ponownie.',
} as const;

export const RECURRING_HINTS = {
  gross: 'Kwota większa od 0 zł.',
  day: 'Dla dni 29–31 w krótszym miesiącu wystąpienie przypada na ostatni dzień miesiąca.',
  dayEdit: 'Nowy dzień obowiązuje od bieżącego miesiąca.',
  dayPaused: 'Wznów wydatek cykliczny, aby zmienić dzień miesiąca.',
  editInfo:
    'Zmiany dotyczą tylko wystąpień, które jeszcze nie powstały, także zaległych. Nowy dzień miesiąca obowiązuje od bieżącego miesiąca. Utworzone wystąpienia poprawia się pojedynczo na liście wydatków.',
  pausedInfo: 'Wydatek cykliczny jest wstrzymany. Zmiany zadziałają od wznowienia.',
} as const;

export function emptySchedule(today = todayInWarsaw()): RecurringScheduleModel {
  return { dayOfMonth: '', startsOn: today, endsOn: '' };
}

export function scheduleFrom(definition: RecurringExpenseDetails): RecurringScheduleModel {
  return {
    dayOfMonth: String(definition.dayOfMonth),
    startsOn: definition.startsOn,
    endsOn: definition.endsOn ?? '',
  };
}

/** Treść definicji → pola wspólne formularza wydatku (daty jednorazowe zostają puste). */
export function recurringContentFrom(definition: RecurringExpenseDetails): ExpenseFormModel {
  return {
    ...emptyExpenseForm(''),
    description: definition.description,
    category: definition.category,
    counterparty: definition.counterparty ?? '',
    grossAmount: definition.grossAmount.replace('.', ','),
    netAmount: definition.netAmount?.replace('.', ',') ?? '',
    paymentMethod: definition.paymentMethod ?? '',
    payer: definition.payer ?? '',
    comment: definition.comment ?? '',
  };
}

/** „1”–„31” → liczba; inne teksty → `null`. */
export function parseDayOfMonth(text: string): number | null {
  const trimmed = String(text ?? '').trim();
  if (!/^\d{1,2}$/.test(trimmed)) {
    return null;
  }
  const day = Number(trimmed);
  return day >= RECURRING_LIMITS.minDay && day <= RECURRING_LIMITS.maxDay ? day : null;
}

/**
 * Formularz → treść definicji. `dayOfMonth` pochodzi zawsze z modelu, także gdy pole jest
 * zablokowane przy wstrzymanej definicji (NC6) — formularz nie pomija pola.
 */
export function toRecurringExpenseInput(
  model: ExpenseFormModel & RecurringScheduleModel,
): RecurringExpenseInput {
  const net = model.netAmount.trim();
  return {
    description: model.description.trim(),
    category: model.category || null,
    counterparty: textOrNull(model.counterparty),
    grossAmount: parseSignedPlnAmount(model.grossAmount) ?? model.grossAmount.trim(),
    netAmount: net === '' ? null : (parseSignedPlnAmount(net) ?? net),
    paymentMethod: model.paymentMethod || null,
    payer: textOrNull(model.payer),
    comment: textOrNull(model.comment),
    dayOfMonth: parseDayOfMonth(model.dayOfMonth),
    startsOn: model.startsOn,
    endsOn: model.endsOn || null,
  };
}

/** Zapytanie podglądu przy poprawnym formacie harmonogramu; w przeciwnym razie `null`. */
export function previewQueryOf(
  schedule: RecurringScheduleModel,
  definitionId: string | null,
): RecurringPreviewQuery | null {
  const day = parseDayOfMonth(schedule.dayOfMonth);
  if (day === null || startsOnError(schedule.startsOn) !== null) {
    return null;
  }
  if (endsOnError(schedule.endsOn, schedule.startsOn) !== null) {
    return null;
  }
  return {
    dayOfMonth: day,
    startsOn: schedule.startsOn,
    endsOn: schedule.endsOn || null,
    definitionId,
  };
}

export function dayOfMonthError(text: string): string | null {
  return parseDayOfMonth(text) === null ? RECURRING_MESSAGES.dayInvalid : null;
}

// Daty `yyyy-MM-dd` porównują się poprawnie jako tekst.
export function startsOnError(text: string): string | null {
  if (text === '') {
    return RECURRING_MESSAGES.startsOnRequired;
  }
  if (!isValidDate(text)) {
    return 'Podaj poprawną datę.';
  }
  return text < RECURRING_LIMITS.minDate || text > RECURRING_LIMITS.maxDate
    ? RECURRING_MESSAGES.startsOnRange
    : null;
}

export function endsOnError(text: string, startsOn: string): string | null {
  if (text === '') {
    return null;
  }
  if (!isValidDate(text)) {
    return 'Podaj poprawną datę.';
  }
  if (text > RECURRING_LIMITS.maxDate) {
    return RECURRING_MESSAGES.endsOnRange;
  }
  return isValidDate(startsOn) && text < startsOn ? RECURRING_MESSAGES.endsBeforeStart : null;
}

/** Brutto definicji: wymagane, bez znaku, większe od 0 zł. */
export function recurringGrossError(text: string): string | null {
  if (text.trim() === '') {
    return RECURRING_MESSAGES.grossRequired;
  }
  const signed = parseSignedPlnAmount(text);
  if (signed === null) {
    return RECURRING_MESSAGES.amountInvalid;
  }
  if (signed.startsWith('-') || signed === '0.00') {
    return RECURRING_MESSAGES.grossNotPositive;
  }
  return amountTooLarge(signed);
}

/** Netto definicji: opcjonalne, ≥ 0. Porównanie z brutto robi wyłącznie API. */
export function recurringNetError(text: string): string | null {
  if (text.trim() === '') {
    return null;
  }
  const signed = parseSignedPlnAmount(text);
  if (signed === null) {
    return RECURRING_MESSAGES.amountInvalid;
  }
  if (signed.startsWith('-')) {
    return RECURRING_MESSAGES.netNegative;
  }
  return amountTooLarge(parsePlnAmount(text) ?? signed);
}

function amountTooLarge(amount: string): string | null {
  return amount.indexOf('.') > 16 ? 'Kwota jest zbyt duża.' : null;
}

/** Komunikat ogólny zapisu definicji: sieć i `503` → „Nie udało się zapisać. Spróbuj ponownie.” */
export function recurringErrorMessage(error: ApiError): string {
  if (error.kind === 'network') {
    return RECURRING_MESSAGES.retry;
  }
  if (error.kind === 'problem' && error.code === 'csrf_invalid') {
    return RECURRING_MESSAGES.csrf;
  }
  return error.detail ?? RECURRING_MESSAGES.saveFailed;
}
