import { StatusTone } from '../../shared/ui/status-badge';
import { ExpenseCategory, ExpensePaymentMethod } from './expense-models';

/** Stan definicji wyliczany w API: `ended` ma pierwszeństwo przed `paused`. */
export type RecurringExpenseStatus = 'active' | 'paused' | 'ended';

export type RecurringExpenseSort = 'description' | 'startsOn' | 'grossAmount';

/**
 * Treść zapisu definicji (`POST`, `PUT`). Kwoty jako ciąg dziesiętny z kropką (`"1234.50"`),
 * daty `yyyy-MM-dd`, `dayOfMonth` to bieżący dzień miesiąca (liczba 1–31).
 */
export interface RecurringExpenseInput {
  readonly description: string;
  readonly category: ExpenseCategory | null;
  readonly counterparty: string | null;
  readonly grossAmount: string;
  readonly netAmount: string | null;
  readonly paymentMethod: ExpensePaymentMethod | null;
  readonly payer: string | null;
  readonly comment: string | null;
  readonly dayOfMonth: number | null;
  readonly startsOn: string;
  readonly endsOn: string | null;
}

export interface RecurringExpenseListItem {
  readonly id: string;
  readonly description: string;
  readonly category: ExpenseCategory;
  readonly counterparty: string | null;
  readonly grossAmount: string;
  readonly dayOfMonth: number;
  readonly startsOn: string;
  readonly endsOn: string | null;
  readonly status: RecurringExpenseStatus;
  readonly pausedOn: string | null;
  readonly nextDueDate: string | null;
}

export interface RecurringExpenseListPage {
  readonly items: readonly RecurringExpenseListItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface RecurringExpenseListQuery {
  readonly page: number;
  readonly pageSize: number;
  readonly sort: RecurringExpenseSort;
  readonly direction: 'asc' | 'desc';
  readonly state: RecurringExpenseStatus | null;
  readonly search: string | null;
}

/** Skutek wznowienia „dzisiaj” (tylko przy `pausedOn`). */
export interface RecurringResumePreview {
  readonly occurrencesToCreate: number;
  readonly nextDueDate: string | null;
}

export interface RecurringExpenseDetails {
  readonly id: string;
  readonly description: string;
  readonly category: ExpenseCategory;
  readonly counterparty: string | null;
  readonly grossAmount: string;
  readonly netAmount: string | null;
  readonly paymentMethod: ExpensePaymentMethod | null;
  readonly payer: string | null;
  readonly comment: string | null;
  readonly dayOfMonth: number;
  readonly startsOn: string;
  readonly endsOn: string | null;
  readonly status: RecurringExpenseStatus;
  readonly pausedOn: string | null;
  readonly nextDueDate: string | null;
  readonly occurrenceCount: number;
  readonly resumePreview: RecurringResumePreview | null;
  readonly createdAtUtc: string;
  readonly updatedAtUtc: string;
  readonly version: string;
}

/** Zapytanie podglądu: harmonogram po zmianie, przy edycji z `definitionId`. */
export interface RecurringPreviewQuery {
  readonly dayOfMonth: number;
  readonly startsOn: string;
  readonly endsOn: string | null;
  readonly definitionId: string | null;
}

/** Skutki zapisu policzone przez API na kopii stanu definicji. */
export interface RecurringExpensePreview {
  readonly occurrencesToCreate: number;
  readonly firstImmediateDueDate: string | null;
  readonly beforeLaunchCount: number;
  readonly nextDueDate: string | null;
  /** Definicja wstrzymana (tryb NC1): nic nie powstaje do wznowienia. */
  readonly paused: boolean;
  readonly droppedDueCount: number;
  readonly createdAfterEndCount: number;
  readonly createdBeforeStartCount: number;
}

export interface RecurringExpenseCreated {
  readonly id: string;
  readonly version: string;
  readonly createdOccurrences: number;
}

export interface RecurringExpenseSaved {
  readonly version: string;
  readonly createdOccurrences: number;
}

/** Kody Problem Details, na których rozgałęzia się interfejs. */
export const RECURRING_PROBLEM = {
  notFound: 'recurring_expense_not_found',
  conflict: 'recurring_expense_conflict',
  paused: 'recurring_expense_paused',
  alreadyPaused: 'recurring_expense_already_paused',
  notPaused: 'recurring_expense_not_paused',
  ended: 'recurring_expense_ended',
  deleted: 'recurring_expense_deleted',
  busy: 'recurring_expense_busy',
  keyReused: 'idempotency_key_reused',
} as const;

export const RECURRING_STATUSES: readonly RecurringExpenseStatus[] = ['active', 'paused', 'ended'];

export const RECURRING_STATUS_LABELS: Readonly<Record<RecurringExpenseStatus, string>> = {
  active: 'Aktywny',
  paused: 'Wstrzymany',
  ended: 'Zakończony',
};

export const RECURRING_STATUS_TONES: Readonly<Record<RecurringExpenseStatus, StatusTone>> = {
  active: 'success',
  paused: 'warning',
  ended: 'neutral',
};

export const RECURRING_STATE_FILTER_LABELS: Readonly<Record<RecurringExpenseStatus, string>> = {
  active: 'Aktywne',
  paused: 'Wstrzymane',
  ended: 'Zakończone',
};

/** Oznaczenie wystąpienia na liście wydatków. */
export const RECURRING_MARK = 'Cykliczny';

/** Dzień miesiąca, np. „10.”, a dla 29–31 z wyjaśnieniem krótszego miesiąca. */
export function dayOfMonthText(day: number): string {
  return day >= 29 ? `${day}. (w krótszym miesiącu ostatni dzień)` : `${day}.`;
}

export type PluralForm = 'one' | 'few' | 'many';

/** Polska odmiana liczebnika: 1 → `one`, 2–4 (bez 12–14) → `few`, pozostałe → `many`. */
export function pluralForm(count: number): PluralForm {
  const n = Math.abs(Math.trunc(count));
  if (n === 1) {
    return 'one';
  }
  const lastDigit = n % 10;
  const lastTwo = n % 100;
  return lastDigit >= 2 && lastDigit <= 4 && (lastTwo < 12 || lastTwo > 14) ? 'few' : 'many';
}

export function plural(count: number, forms: Readonly<Record<PluralForm, string>>): string {
  return forms[pluralForm(count)];
}

/** „1 wystąpienie”, „2 wystąpienia”, „5 wystąpień”. */
export function occurrencesText(count: number): string {
  return `${count} ${plural(count, { one: 'wystąpienie', few: 'wystąpienia', many: 'wystąpień' })}`;
}
