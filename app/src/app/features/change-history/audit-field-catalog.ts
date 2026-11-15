import { EXPENSE_CATEGORY_LABELS } from '../../shared/dictionaries/expense-categories';
import {
  CHANNEL_LABELS,
  CUSTOMER_KIND_LABELS,
  FULFILLMENT_STATUS_LABELS,
  REFUND_KIND_LABELS,
} from '../../shared/dictionaries/order-dictionaries';
import { PAYMENT_METHOD_LABELS } from '../../shared/dictionaries/payment-methods';
import { shopStatusLabel } from '../../shared/dictionaries/shop-status';
import { NO_DATA, formatDate, formatPln } from '../../shared/format/pl-format';
import { AuditObjectTypeCode, ChangeHistoryItem } from './change-history-models';

/**
 * Katalog pól audytu (SF-18): etykieta i sposób formatowania każdego pola według pary (rodzaj
 * obiektu, pole). Nazwy pól to camelCase z zapisu audytu. Pole spoza katalogu pokazuje się jako
 * nazwa i surowa wartość. Formatowanie niczego nie liczy — kwoty formatuje `formatPln` z tekstu.
 */

/** `fields`: tablica JSON kodów pól obiektu (anonimizacja, SF-22), pokazywana jako etykiety pól. */
export type FieldFormat =
  'text' | 'amount' | 'date' | 'dictionary' | 'boolean' | 'list' | 'number' | 'fields';

export interface AuditFieldDefinition {
  readonly label: string;
  readonly format: FieldFormat;
  /** Etykieta kodu słownika; nieznany kod wraca bez zmian. */
  readonly dictionary?: (code: string) => string;
}

function fromLabels(labels: Readonly<Record<string, string>>): (code: string) => string {
  return (code) => (Object.hasOwn(labels, code) ? labels[code] : code);
}

const text = (label: string): AuditFieldDefinition => ({ label, format: 'text' });
const amount = (label: string): AuditFieldDefinition => ({ label, format: 'amount' });
const date = (label: string): AuditFieldDefinition => ({ label, format: 'date' });
const yesNo = (label: string): AuditFieldDefinition => ({ label, format: 'boolean' });
const list = (label: string): AuditFieldDefinition => ({ label, format: 'list' });
const number = (label: string): AuditFieldDefinition => ({ label, format: 'number' });
const fieldList = (label: string): AuditFieldDefinition => ({ label, format: 'fields' });
const ANONYMIZED_FIELDS = 'Zanonimizowane pola';
const dictionary = (
  label: string,
  labels: Readonly<Record<string, string>> | ((code: string) => string),
): AuditFieldDefinition => ({
  label,
  format: 'dictionary',
  dictionary: typeof labels === 'function' ? labels : fromLabels(labels),
});

const methods = PAYMENT_METHOD_LABELS;
const categories = EXPENSE_CATEGORY_LABELS;

export const AUDIT_FIELD_CATALOG: Readonly<
  Record<AuditObjectTypeCode, Readonly<Record<string, AuditFieldDefinition>>>
> = {
  order: {
    orderDate: date('Data zamówienia'),
    channel: dictionary('Kanał', CHANNEL_LABELS),
    value: amount('Wartość brutto'),
    fulfillmentStatus: dictionary('Status realizacji', FULFILLMENT_STATUS_LABELS),
    customerKind: dictionary('Rodzaj klienta', CUSTOMER_KIND_LABELS),
    customerName: text('Imię i nazwisko lub nazwa firmy'),
    customerPhone: text('Telefon'),
    customerEmail: text('E-mail'),
    customerStreet: text('Ulica i numer'),
    customerPostalCode: text('Kod pocztowy'),
    customerCity: text('Miejscowość'),
    customerTaxId: text('NIP'),
    suppliers: list('Dostawcy'),
    zoReferences: list('Referencje ZO'),
    invoiceNumber: text('Numer faktury'),
    notes: text('Uwagi do realizacji'),
    comment: text('Komentarz'),
    hasPhotos: yesNo('Zdjęcia'),
    hasReview: yesNo('Opinia'),
    shopStatus: dictionary('Status w sklepie', shopStatusLabel),
    shopDeleted: yesNo('Usunięte w sklepie'),
    shopRefundedAmount: amount('Kwota zwrócona w sklepie'),
    anonymizedFields: fieldList(ANONYMIZED_FIELDS),
    anonymizedReasons: number('Zanonimizowane powody zwrotów i korekt'),
    retentionStartDate: date('Początek okresu retencji'),
    retentionMonths: number('Okres retencji (miesiące)'),
  },
  payment: {
    amount: amount('Kwota'),
    date: date('Data wpłaty'),
    method: dictionary('Metoda', methods),
    receivedBy: text('Przyjął'),
  },
  refund: {
    kind: dictionary('Rodzaj zwrotu', REFUND_KIND_LABELS),
    amount: amount('Kwota'),
    date: date('Data zwrotu'),
    method: dictionary('Metoda', methods),
    reason: text('Powód'),
    anonymizedFields: fieldList(ANONYMIZED_FIELDS),
  },
  salesCorrection: {
    amount: amount('Kwota'),
    date: date('Data korekty'),
    reason: text('Powód'),
    anonymizedFields: fieldList(ANONYMIZED_FIELDS),
  },
  expense: {
    description: text('Opis'),
    category: dictionary('Kategoria', categories),
    counterparty: text('Kontrahent'),
    costDate: date('Data kosztu'),
    dueDate: date('Termin płatności'),
    paidOn: date('Data zapłaty'),
    grossAmount: amount('Kwota brutto'),
    netAmount: amount('Kwota netto'),
    documentReference: text('Referencja dokumentu'),
    paymentMethod: dictionary('Metoda płatności', methods),
    payer: text('Płatnik'),
    comment: text('Komentarz'),
  },
  recurringExpenseDefinition: {
    description: text('Opis'),
    category: dictionary('Kategoria', categories),
    counterparty: text('Kontrahent'),
    grossAmount: amount('Kwota brutto'),
    netAmount: amount('Kwota netto'),
    paymentMethod: dictionary('Metoda płatności', methods),
    payer: text('Płatnik'),
    comment: text('Komentarz'),
    dayOfMonth: number('Dzień miesiąca'),
    startsOn: date('Początek'),
    endsOn: date('Koniec'),
    pausedOn: date('Data wstrzymania'),
  },
  ownerWithdrawal: {
    person: text('Osoba'),
    amount: amount('Kwota'),
    withdrawnOn: date('Data wypłaty'),
    method: dictionary('Metoda', methods),
  },
};

function definitionOf(type: AuditObjectTypeCode, field: string): AuditFieldDefinition | null {
  const fields = AUDIT_FIELD_CATALOG[type];
  return fields && Object.hasOwn(fields, field) ? fields[field] : null;
}

/** Etykieta pola; pole spoza katalogu pokazuje swoją nazwę. */
export function fieldLabel(type: AuditObjectTypeCode, field: string): string {
  return definitionOf(type, field)?.label ?? field;
}

/** Wartość pola do wyświetlenia. Nigdy nie rzuca wyjątku; brak wartości to „Brak danych”. */
export function formatFieldValue(
  type: AuditObjectTypeCode,
  field: string,
  value: string | null,
): string {
  if (value === null) {
    return NO_DATA;
  }
  const definition = definitionOf(type, field);
  switch (definition?.format) {
    case 'amount':
      return formatPln(value);
    case 'date':
      return formatDate(value);
    case 'dictionary':
      return definition.dictionary ? definition.dictionary(value) : value;
    case 'boolean':
      return value === 'true' ? 'tak' : value === 'false' ? 'nie' : value;
    case 'list':
      return formatList(value);
    case 'fields':
      return formatFields(type, value);
    default:
      return value;
  }
}

/** Tablica JSON tekstów jako „A, B”; inna albo nieczytelna wartość bez zmian. */
function formatList(value: string): string {
  try {
    const parsed: unknown = JSON.parse(value);
    if (Array.isArray(parsed) && parsed.every((entry) => typeof entry === 'string')) {
      return parsed.join(', ');
    }
  } catch {
    // Wartość nie jest JSON-em: pokazujemy ją surowo.
  }
  return value;
}

/**
 * Tablica JSON kodów pól jako etykiety pól obiektu („Imię i nazwisko lub nazwa firmy, E-mail”); pusta
 * tablica to „Brak danych”, nieznany kod pokazuje się jako kod, nieczytelna wartość bez zmian.
 */
function formatFields(type: AuditObjectTypeCode, value: string): string {
  try {
    const parsed: unknown = JSON.parse(value);
    if (Array.isArray(parsed) && parsed.every((entry) => typeof entry === 'string')) {
      return parsed.length > 0
        ? parsed.map((code: string) => fieldLabel(type, code)).join(', ')
        : NO_DATA;
    }
  } catch {
    // Wartość nie jest JSON-em: pokazujemy ją surowo.
  }
  return value;
}

export interface ChangeSummary {
  readonly lines: readonly string[];
  /** Liczba pól poza skrótem („i N więcej”). */
  readonly more: number;
}

const SUMMARY_LENGTH = 2;

/**
 * Skrót zakresu zmiany: pierwsze dwa pola w kolejności z audytu. Utworzenie i anonimizacja
 * pokazują wartość „jest”, usunięcie wartość „było”, a zmiana „było → jest” (te same kolumny co rozwinięcie).
 */
export function changeSummary(
  item: Pick<ChangeHistoryItem, 'object' | 'operation' | 'changes'>,
): ChangeSummary {
  const type = item.object.type;
  const lines = item.changes.slice(0, SUMMARY_LENGTH).map((change) => {
    const label = fieldLabel(type, change.field);
    const was = formatFieldValue(type, change.field, change.oldValue);
    const is = formatFieldValue(type, change.field, change.newValue);
    switch (item.operation) {
      case 'created':
      case 'anonymized':
        return `${label}: ${is}`;
      case 'deleted':
        return `${label}: ${was}`;
      case 'updated':
        return `${label}: ${was} → ${is}`;
    }
  });
  return { lines, more: Math.max(0, item.changes.length - SUMMARY_LENGTH) };
}
