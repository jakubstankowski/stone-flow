import { StatusTone } from '../../shared/ui/status-badge';

/** Kategoria kosztu (stały kod API, PRD §7). */
export type ExpenseCategory =
  | 'materialPurchase'
  | 'cutting'
  | 'hdsTransport'
  | 'marketing'
  | 'otherOperations'
  | 'vatTax'
  | 'ppeTax';

export type ExpensePaymentMethod = 'transfer' | 'blik' | 'cash' | 'cashOnDelivery';

/** Stan wydatku z backendu: wynika wyłącznie z daty zapłaty. */
export type ExpensePaymentStatus = 'planned' | 'paid';

/** Filtr stanu listy; `overdue` to planowane po terminie. */
export type ExpenseStateFilter = 'planned' | 'paid' | 'overdue';

export type ExpenseSignFilter = 'positive' | 'negative';

export type ExpenseSort = 'costDate' | 'dueDate' | 'paidOn' | 'grossAmount';

/**
 * Treść zapisu (`POST`, `PUT`). Kwoty jako ciąg dziesiętny z kropką i opcjonalnym minusem
 * (`"-1234.50"`), daty `yyyy-MM-dd`, puste pola jako `null`.
 */
export interface ExpenseInput {
  readonly description: string;
  readonly category: ExpenseCategory | null;
  readonly counterparty: string | null;
  readonly costDate: string;
  readonly dueDate: string | null;
  readonly paidOn: string | null;
  readonly grossAmount: string;
  readonly netAmount: string | null;
  readonly documentReference: string | null;
  readonly paymentMethod: ExpensePaymentMethod | null;
  readonly payer: string | null;
  readonly comment: string | null;
}

export interface ExpenseDetails {
  readonly id: string;
  readonly description: string;
  readonly category: ExpenseCategory;
  readonly counterparty: string | null;
  readonly costDate: string;
  readonly dueDate: string | null;
  readonly paidOn: string | null;
  readonly grossAmount: string;
  readonly netAmount: string | null;
  readonly documentReference: string | null;
  readonly paymentMethod: ExpensePaymentMethod | null;
  readonly payer: string | null;
  readonly comment: string | null;
  readonly paymentStatus: ExpensePaymentStatus;
  readonly overdue: boolean;
  /** Wystąpienie wydatku cyklicznego (SF-16); `null` dla wydatku dodanego ręcznie. */
  readonly recurring: ExpenseRecurrence | null;
  readonly createdAtUtc: string;
  readonly updatedAtUtc: string;
  readonly version: string;
}

export interface ExpenseListItem {
  readonly id: string;
  readonly description: string;
  readonly category: ExpenseCategory;
  readonly counterparty: string | null;
  readonly costDate: string;
  readonly dueDate: string | null;
  readonly paidOn: string | null;
  readonly grossAmount: string;
  readonly paymentStatus: ExpensePaymentStatus;
  readonly overdue: boolean;
  /** Definicja, z której powstało wystąpienie (SF-16); `null` dla wydatku dodanego ręcznie. */
  readonly recurringDefinitionId: string | null;
}

/** Pochodzenie wystąpienia: definicja, okres `yyyy-MM` i to, czy definicja jeszcze istnieje. */
export interface ExpenseRecurrence {
  readonly definitionId: string;
  readonly period: string;
  readonly definitionExists: boolean;
}

export interface ExpenseListPage {
  readonly items: readonly ExpenseListItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export interface ExpenseListQuery {
  readonly page: number;
  readonly pageSize: number;
  readonly sort: ExpenseSort;
  readonly direction: 'asc' | 'desc';
  readonly costDateFrom: string | null;
  readonly costDateTo: string | null;
  readonly paidOnFrom: string | null;
  readonly paidOnTo: string | null;
  readonly categories: readonly ExpenseCategory[];
  readonly state: ExpenseStateFilter | null;
  readonly sign: ExpenseSignFilter | null;
  readonly search: string | null;
  /** Tylko wystąpienia jednej definicji (SF-16). */
  readonly recurringDefinitionId: string | null;
}

export interface ExpenseCreated {
  readonly id: string;
  readonly version: string;
}

export interface ExpenseVersion {
  readonly version: string;
}

/** Kody Problem Details, na których rozgałęzia się interfejs. */
export const EXPENSE_PROBLEM = {
  notFound: 'expense_not_found',
  conflict: 'expense_conflict',
  keyReused: 'idempotency_key_reused',
  alreadyPaid: 'expense_already_paid',
  notPaid: 'expense_not_paid',
} as const;

export const CATEGORIES: readonly ExpenseCategory[] = [
  'materialPurchase',
  'cutting',
  'hdsTransport',
  'marketing',
  'otherOperations',
  'vatTax',
  'ppeTax',
];

export const CATEGORY_LABELS: Readonly<Record<ExpenseCategory, string>> = {
  materialPurchase: 'Zakup materiału u dostawców',
  cutting: 'Cięcie',
  hdsTransport: 'Transport HDS',
  marketing: 'Marketing',
  otherOperations: 'Pozostała działalność',
  vatTax: 'Podatek VAT',
  ppeTax: 'Podatek PPE',
};

export const PAYMENT_METHODS: readonly ExpensePaymentMethod[] = [
  'transfer',
  'blik',
  'cash',
  'cashOnDelivery',
];

export const PAYMENT_METHOD_LABELS: Readonly<Record<ExpensePaymentMethod, string>> = {
  transfer: 'Przelew',
  blik: 'BLIK',
  cash: 'Gotówka',
  cashOnDelivery: 'Pobranie',
};

export const PAYMENT_STATUS_LABELS: Readonly<Record<ExpensePaymentStatus, string>> = {
  planned: 'Planowany',
  paid: 'Opłacony',
};

export const PAYMENT_STATUS_TONES: Readonly<Record<ExpensePaymentStatus, StatusTone>> = {
  planned: 'neutral',
  paid: 'success',
};

export const OVERDUE_LABEL = 'Po terminie';

export const STATE_FILTERS: readonly ExpenseStateFilter[] = ['planned', 'paid', 'overdue'];

export const STATE_FILTER_LABELS: Readonly<Record<ExpenseStateFilter, string>> = {
  planned: 'Planowane',
  paid: 'Opłacone',
  overdue: 'Po terminie',
};

export const SIGN_FILTERS: readonly ExpenseSignFilter[] = ['positive', 'negative'];

export const SIGN_FILTER_LABELS: Readonly<Record<ExpenseSignFilter, string>> = {
  positive: 'Wydatki (kwoty dodatnie)',
  negative: 'Korekty i zwroty (kwoty ujemne)',
};

/** Etykieta wydatku z ujemną kwotą (design guide, formularz wydatku). */
export const NEGATIVE_AMOUNT_LABEL = 'Korekta / zwrot od dostawcy';

/** Kwota z API (`"-200.00"`) jest ujemna — sprawdzenie znaku tekstu, bez liczenia. */
export function isNegativeAmount(amount: string | null | undefined): boolean {
  return (amount ?? '').trim().startsWith('-');
}

/** Pola dat w filtrach listy. */
export type ExpenseFilterKey = 'costDateFrom' | 'costDateTo' | 'paidOnFrom' | 'paidOnTo';
