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

const timeFormat = new Intl.DateTimeFormat('pl-PL', {
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
  timeZone: WARSAW,
});

/** Klucz dnia kalendarzowego w Europe/Warsaw (`yyyy-MM-dd`). */
const warsawDayFormat = new Intl.DateTimeFormat('en-CA', {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
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
    if (!Number.isFinite(value)) {
      return NO_DATA;
    }
    return amountFormat.format(value === 0 ? 0 : value);
  }
  const trimmed = value.trim();
  if (!DECIMAL.test(trimmed)) {
    return NO_DATA;
  }
  if (Number(trimmed) === 0) {
    return amountFormat.format(0);
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

/**
 * Znacznik techniczny UTC (ISO 8601) → `28.10.2026, 14:05` w strefie Europe/Warsaw.
 * Znacznik bez strefy (np. `DateTimeKind.Unspecified` z .NET) jest traktowany jako UTC.
 */
export function formatDateTime(value: string | null | undefined): string {
  const date = parseTimestamp(value);
  return date ? dateTimeFormat.format(date) : NO_DATA;
}

/** Znacznik techniczny UTC (ISO 8601) → `14:05` w strefie Europe/Warsaw. */
export function formatTime(value: string | null | undefined): string {
  const date = parseTimestamp(value);
  return date ? timeFormat.format(date) : NO_DATA;
}

/**
 * Czas odczytu względem chwili `nowMs`: sama godzina (`14:05`), gdy to ten sam dzień w
 * Europe/Warsaw, a w przeciwnym razie data z godziną (`28.10.2026, 14:05`).
 */
export function formatReadTime(value: string | null | undefined, nowMs: number): string {
  const date = parseTimestamp(value);
  if (!date) {
    return NO_DATA;
  }
  const sameDay = warsawDayFormat.format(date) === warsawDayFormat.format(new Date(nowMs));
  return sameDay ? timeFormat.format(date) : dateTimeFormat.format(date);
}

function parseTimestamp(value: string | null | undefined): Date | null {
  if (!value || !/^\d{4}-\d{2}-\d{2}T/.test(value)) {
    return null;
  }
  const hasZone = /(?:Z|[+-]\d{2}:?\d{2})$/i.test(value);
  const date = new Date(hasZone ? value : `${value}Z`);
  return Number.isNaN(date.getTime()) ? null : date;
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

const AMOUNT_INPUT = /^(\d+|\d{1,3}(?:[ \u00A0\u202F]\d{3})+)(?:[.,](\d{1,2}))?$/;

/**
 * Kwota wpisana przez użytkownika po polsku (`1 234,50`, `1234,5`, `1234.50`, `0`) →
 * kanoniczny ciąg dziesiętny dla API (`"1234.50"`). Konwersja działa wyłącznie na tekście,
 * bez arytmetyki zmiennoprzecinkowej. Zwraca `null` dla wartości ujemnej, więcej niż dwóch
 * miejsc po przecinku, tekstu i pustego pola. Zakres sprawdza walidacja formularza i API.
 */
export function parsePlnAmount(text: string | null | undefined): string | null {
  const match = AMOUNT_INPUT.exec((text ?? '').trim());
  if (!match) {
    return null;
  }
  const integer = match[1].replace(/[ \u00A0\u202F]/g, '').replace(/^0+(?=\d)/, '');
  const fraction = (match[2] ?? '').padEnd(2, '0');
  return `${integer}.${fraction}`;
}

/**
 * Kwota ze znakiem wpisana po polsku (`-1 234,50`, `−1234,5`, `1234.50`) → kanoniczny ciąg
 * dziesiętny dla API (`"-1234.50"`), wyłącznie operacjami na tekście. Minus może być znakiem
 * `-` albo `−` (U+2212). `-0` daje `"0.00"`. Zwraca `null` dla pustego pola, tekstu i więcej niż
 * dwóch miejsc po przecinku.
 */
export function parseSignedPlnAmount(text: string | null | undefined): string | null {
  const trimmed = (text ?? '').trim();
  const negative = /^[-−]/.test(trimmed);
  const amount = parsePlnAmount(negative ? trimmed.slice(1).trimStart() : trimmed);
  if (amount === null) {
    return null;
  }
  return negative && !/^0\.00$/.test(amount) ? `-${amount}` : amount;
}

/** Dzisiejsza data w strefie Europe/Warsaw jako `yyyy-MM-dd`. */
export function todayInWarsaw(now = new Date()): string {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: WARSAW,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(now);
}
