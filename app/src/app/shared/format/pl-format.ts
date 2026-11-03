/**
 * Polskie formaty prezentacji (design guide, sekcja 7). Funkcje tylko formatują wartości
 * otrzymane z API: nie sumują, nie zaokrąglają do rozliczeń i nie przeliczają netto/brutto.
 */

export const NO_DATA = 'Brak danych';

const WARSAW = 'Europe/Warsaw';
const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;
const ISO_MONTH = /^(\d{4})-(\d{2})$/;
const DECIMAL = /^-?\d+(\.\d+)?$/;

const amountFormat = new Intl.NumberFormat('pl-PL', {
  style: 'currency',
  currency: 'PLN',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
  useGrouping: 'always',
});

const dateFormat = new Intl.DateTimeFormat('pl-PL', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  timeZone: 'UTC',
});

const monthFormat = new Intl.DateTimeFormat('pl-PL', {
  month: 'long',
  year: 'numeric',
  timeZone: 'UTC',
});

const dateTimeFormat = new Intl.DateTimeFormat('pl-PL', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
  timeZone: WARSAW,
});

export type AmountInput = number | string | null | undefined;

/**
 * Kwota w PLN, np. `12 345,67 zł` (separator tysięcy i spacja przed walutą to U+00A0).
 * Ciąg dziesiętny z API jest formatowany bez konwersji na liczbę zmiennoprzecinkową.
 */
export function formatPln(value: AmountInput): string {
  if (value === null || value === undefined) {
    return NO_DATA;
  }
  if (typeof value === 'number') {
    return Number.isFinite(value) ? amountFormat.format(value) : NO_DATA;
  }
  const trimmed = value.trim();
  if (!DECIMAL.test(trimmed)) {
    return NO_DATA;
  }
  // Intl.NumberFormat przyjmuje ciąg dziesiętny jako dokładną wartość (ES2023).
  return amountFormat.format(trimmed as `${number}`);
}

/** Data biznesowa `yyyy-MM-dd` → `28.10.2026`. */
export function formatDate(value: string | null | undefined): string {
  const date = parseIsoDate(value);
  return date ? dateFormat.format(date) : NO_DATA;
}

/** Miesiąc `yyyy-MM` lub `yyyy-MM-dd` → `październik 2026`. */
export function formatMonth(value: string | null | undefined): string {
  if (!value) {
    return NO_DATA;
  }
  const month = ISO_MONTH.exec(value);
  const date = month ? utcDate(Number(month[1]), Number(month[2]), 1) : parseIsoDate(value);
  return date ? monthFormat.format(date) : NO_DATA;
}

/** Znacznik techniczny UTC (ISO 8601) → `28.10.2026, 14:05` w strefie Europe/Warsaw. */
export function formatDateTime(value: string | null | undefined): string {
  if (!value || !/^\d{4}-\d{2}-\d{2}T/.test(value)) {
    return NO_DATA;
  }
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? NO_DATA : dateTimeFormat.format(date);
}

function parseIsoDate(value: string | null | undefined): Date | null {
  if (!value) {
    return null;
  }
  const match = ISO_DATE.exec(value);
  return match ? utcDate(Number(match[1]), Number(match[2]), Number(match[3])) : null;
}

function utcDate(year: number, month: number, day: number): Date | null {
  const date = new Date(Date.UTC(year, month - 1, day));
  const valid =
    date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
  return valid ? date : null;
}
