import { StatusTone } from '../../shared/ui/status-badge';

/**
 * Typy kontraktu `GET /api/change-history` (SF-18, spec „Data / API”). Lista jest tylko do
 * odczytu. Wartości pól przychodzą w formacie zapisu audytu (kwota `1234.50`, data `yyyy-MM-dd`,
 * słownik jako kod) i są formatowane według katalogu pól (`audit-field-catalog.ts`).
 */

export type AuditObjectTypeCode =
  | 'order'
  | 'payment'
  | 'refund'
  | 'salesCorrection'
  | 'expense'
  | 'recurringExpenseDefinition'
  | 'ownerWithdrawal';

export type AuditOperationCode = 'created' | 'updated' | 'deleted';

export type AuditAuthorKindCode = 'account' | 'wooCommerceSync' | 'automatedJob';

export interface ChangeHistoryChange {
  readonly field: string;
  readonly oldValue: string | null;
  readonly newValue: string | null;
  readonly isPersonalData: boolean;
}

export interface ChangeHistoryObject {
  readonly type: AuditObjectTypeCode;
  readonly id: string;
  /** Data biznesowa obiektu zapisana we wpisie (po zmianie), `yyyy-MM-dd`. */
  readonly businessDate: string;
  /** Etykieta z audytu, już z rodzajem (np. „Wydatek – Paliwo”). */
  readonly label: string;
  readonly parent: { readonly type: AuditObjectTypeCode; readonly id: string } | null;
  /** W audycie jest wpis usunięcia tego obiektu (niezależnie od filtrów). */
  readonly deleted: boolean;
}

export interface ChangeHistoryItem {
  /** Identyfikator wpisu (liczba jako tekst). */
  readonly id: string;
  /** Czas zmiany, ISO 8601 UTC z „Z”. */
  readonly occurredAt: string;
  readonly author: { readonly kind: AuditAuthorKindCode };
  readonly object: ChangeHistoryObject;
  readonly operation: AuditOperationCode;
  readonly changes: readonly ChangeHistoryChange[];
}

export interface ChangeHistoryPage {
  readonly items: readonly ChangeHistoryItem[];
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
}

export interface ChangeHistoryQuery {
  readonly changedFrom: string;
  readonly changedTo: string;
  /** Pusty tekst = brak granicy (parametr pominięty). */
  readonly businessFrom: string;
  readonly businessTo: string;
  readonly objectTypes: readonly AuditObjectTypeCode[];
  readonly direction: 'asc' | 'desc';
  readonly page: number;
  readonly pageSize: number;
}

/** Stała kolejność rodzajów (filtr, chipy, zapytanie). */
export const OBJECT_TYPES: readonly AuditObjectTypeCode[] = [
  'order',
  'payment',
  'refund',
  'salesCorrection',
  'expense',
  'recurringExpenseDefinition',
  'ownerWithdrawal',
];

export const OBJECT_TYPE_LABELS: Readonly<Record<AuditObjectTypeCode, string>> = {
  order: 'Zamówienie',
  payment: 'Wpłata',
  refund: 'Zwrot',
  salesCorrection: 'Korekta sprzedaży',
  expense: 'Wydatek',
  recurringExpenseDefinition: 'Wydatek cykliczny',
  ownerWithdrawal: 'Wypłata właściciela',
};

/** Autor konta to zawsze „Właściciel” (jedno konto, D-05). */
export const AUTHOR_LABELS: Readonly<Record<AuditAuthorKindCode, string>> = {
  account: 'Właściciel',
  wooCommerceSync: 'Synchronizacja WooCommerce',
  automatedJob: 'Zadanie automatyczne',
};

export const OPERATION_LABELS: Readonly<Record<AuditOperationCode, string>> = {
  created: 'Utworzenie',
  updated: 'Zmiana',
  deleted: 'Usunięcie',
};

export const OPERATION_TONES: Readonly<Record<AuditOperationCode, StatusTone>> = {
  created: 'neutral',
  updated: 'info',
  deleted: 'warning',
};

export const DELETED_LABEL = 'Usunięto';
export const DELETED_TONE: StatusTone = 'warning';

export const BUSINESS_DATE_HINT =
  'Data biznesowa: data zamówienia, wpłaty, zwrotu, korekty, kosztu, startu cyklu albo wypłaty — po zmianie.';

export const EMPTY_TITLE = 'Nie ma jeszcze żadnych zmian.';
export const FILTERED_EMPTY_TITLE = 'Brak zmian dla wybranych filtrów.';
export const LIST_ERROR_MESSAGE = 'Nie udało się wczytać historii zmian.';

/**
 * Adres obiektu wpisu albo `null`, gdy obiekt usunięto (znacznik „Usunięto” zamiast linku).
 * Wpłata, zwrot i korekta nie mają własnej strony: prowadzą do zamówienia nadrzędnego (zamówień
 * nie usuwa się, więc ten link jest zawsze ważny).
 */
export function objectLink(item: Pick<ChangeHistoryItem, 'object'>): string[] | null {
  const { type, id, parent, deleted } = item.object;
  if (deleted) {
    return null;
  }
  switch (type) {
    case 'order':
      return ['/zamowienia', id];
    case 'payment':
    case 'refund':
    case 'salesCorrection':
      return parent ? ['/zamowienia', parent.id] : null;
    case 'expense':
      return ['/wydatki', id];
    case 'recurringExpenseDefinition':
      return ['/wydatki', 'cykliczne', id];
    case 'ownerWithdrawal':
      return ['/operacje', 'wyplaty', id];
  }
}
