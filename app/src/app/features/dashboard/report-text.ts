import { formatDate, formatMonth, formatPercent, formatPln } from '../../shared/format/pl-format';
import {
  AmountComparison,
  CountComparison,
  Granularity,
  NO_BASIS_TEXT,
  ReportComparisonHeader,
  ReportPeriod,
  SeriesPoint,
} from './report-models';

/**
 * Teksty widoków Pulpitu z danych API („Sprzedaż” SF-19, „Przepływy pieniężne” SF-20): etykiety
 * okresów i podstawy kwot, opis porównania, tekst porównania karty i etykiety przedziałów.
 * Wyłącznie operacje na tekście dat i kwot — bez arytmetyki pieniędzy (kwoty, różnice i procenty
 * liczy backend).
 */

const monthShort = new Intl.DateTimeFormat('pl-PL', {
  month: 'short',
  year: 'numeric',
  timeZone: 'UTC',
});

/** `yyyy-MM-dd` → `01.11`. */
function dayMonth(date: string): string {
  return `${date.slice(8, 10)}.${date.slice(5, 7)}`;
}

function yearOf(date: string): string {
  return date.slice(0, 4);
}

function range(from: string, to: string): string {
  return `${formatDate(from)}–${formatDate(to)}`;
}

/** Etykieta okresu: „listopad 2026, 01.11–30.11.2026”, „2026, 01.01–09.11.2026”, „01.09.2026–15.11.2026”. */
export function periodLabel(period: ReportPeriod): string {
  switch (period.kind) {
    case 'currentMonth':
    case 'previousMonth':
      return `${formatMonth(period.from)}, ${dayMonth(period.from)}–${formatDate(period.to)}`;
    case 'year':
      return `${yearOf(period.from)}, ${dayMonth(period.from)}–${formatDate(period.to)}`;
    case 'custom':
      return range(period.from, period.to);
  }
}

/** Informacja o przycięciu okresu do daty uruchomienia (jak na liście „Operacje”). */
export function launchNotice(period: ReportPeriod): string | null {
  // Daty `yyyy-MM-dd` porównują się poprawnie jako tekst.
  if (period.beforeLaunch || period.effectiveFrom <= period.from) {
    return null;
  }
  return `Raport liczony od ${formatDate(period.launchDate)} (data uruchomienia).`;
}

export function beforeLaunchMessage(period: ReportPeriod): string {
  return `Wybrany okres jest w całości przed datą uruchomienia (${formatDate(period.launchDate)}). Raporty obejmują dane od tej daty.`;
}

/** Ogólna etykieta podstawy kwot pod wyborem okresu. */
export function basisLabel(launchDate: string): string {
  return `Kwoty brutto w PLN. Raporty obejmują dane od ${formatDate(launchDate)}.`;
}

/** Etykieta podstawy kwot widoku „Przepływy pieniężne” (SF-20). */
export function cashFlowBasisLabel(launchDate: string): string {
  return `Kwoty brutto w PLN według daty operacji pieniężnej: wpłaty, zwrotu, zapłaty wydatku albo wypłaty. Raporty obejmują dane od ${formatDate(launchDate)}.`;
}

/** Nazwa okresu bazowego do tekstu karty: miesiąc, rok, odcinek roku albo zakres dat. */
export function baseLabel(period: ReportPeriod, comparison: ReportComparisonHeader): string {
  switch (period.kind) {
    case 'currentMonth':
    case 'previousMonth':
      return formatMonth(comparison.from);
    case 'year':
      return period.inProgress
        ? `${dayMonth(comparison.from)}–${formatDate(comparison.to)}`
        : yearOf(comparison.from);
    case 'custom':
      return range(comparison.from, comparison.to);
  }
}

/** Opis porównania pod wyborem okresu (spec pkt 5); okres bazowy pochodzi z API. */
export function comparisonDescription(
  period: ReportPeriod,
  comparison: ReportComparisonHeader,
): string {
  if (period.inProgress && period.kind === 'currentMonth') {
    return `Miesiąc w toku — porównanie z całym poprzednim miesiącem (${formatMonth(comparison.from)}).`;
  }
  if (period.inProgress && period.kind === 'year') {
    return `Rok w toku (dane do ${formatDate(period.to)}) — porównanie z ${dayMonth(comparison.from)}–${dayMonth(comparison.to)} ${yearOf(comparison.to)}.`;
  }
  return `Porównanie z: ${baseLabel(period, comparison)}`;
}

export type ComparisonArrow = '↑' | '↓' | '→';

export interface CardComparisonText {
  readonly arrow: ComparisonArrow | null;
  readonly text: string;
}

type Direction = 'up' | 'down' | 'same';

const ARROWS: Readonly<Record<Direction, ComparisonArrow>> = { up: '↑', down: '↓', same: '→' };

const WORDS: Readonly<Record<Direction, string>> = {
  up: 'więcej niż w:',
  down: 'mniej niż w:',
  same: 'bez zmian względem:',
};

/** Kierunek z tekstu różnicy z API: znak minus albo samo zero (bez liczenia). */
function directionOfText(difference: string): Direction {
  const trimmed = difference.trim();
  if (/^-?0+(\.0+)?$/.test(trimmed)) {
    return 'same';
  }
  return trimmed.startsWith('-') ? 'down' : 'up';
}

/**
 * Tekst porównania karty z kwotą: „+12,5% (+1 234,00 zł) więcej niż w: październik 2026”,
 * „-3,0% (-50,00 zł) mniej niż w: …”, „0,0% (0,00 zł) bez zmian względem: …” albo
 * „brak podstawy porównania”. Kolor neutralny; kierunek niesie strzałka i słowo.
 */
export function amountComparisonText(
  comparison: AmountComparison,
  base: string,
): CardComparisonText {
  if (comparison.status === 'noBasis' || comparison.difference === null) {
    return { arrow: null, text: NO_BASIS_TEXT };
  }
  const direction = directionOfText(comparison.difference);
  const amount = `${direction === 'up' ? '+' : ''}${formatPln(comparison.difference)}`;
  return {
    arrow: ARROWS[direction],
    text: `${formatPercent(comparison.percent)} (${amount}) ${WORDS[direction]} ${base}`,
  };
}

/** Tekst porównania liczby zamówień; różnica w sztukach („+3 zam.”). */
export function countComparisonText(comparison: CountComparison, base: string): CardComparisonText {
  if (comparison.status === 'noBasis' || comparison.difference === null) {
    return { arrow: null, text: NO_BASIS_TEXT };
  }
  const difference = comparison.difference;
  const direction: Direction = difference > 0 ? 'up' : difference < 0 ? 'down' : 'same';
  const count = `${difference > 0 ? '+' : ''}${difference} zam.`;
  return {
    arrow: ARROWS[direction],
    text: `${formatPercent(comparison.percent)} (${count}) ${WORDS[direction]} ${base}`,
  };
}

/** Etykieta przedziału na osi wykresu: dzień „01.11”, miesiąc „lis 2026”. */
export function bucketLabel(point: Pick<SeriesPoint, 'from'>, granularity: Granularity): string {
  if (granularity === 'day') {
    return dayMonth(point.from);
  }
  const [year, month] = point.from.split('-').map(Number);
  return monthShort.format(new Date(Date.UTC(year, month - 1, 1)));
}

/** Okres wiersza tabeli danych wykresu: dzień albo (przycięty) miesiąc jako zakres dat. */
export function bucketRange(point: Pick<SeriesPoint, 'from' | 'to'>): string {
  return point.from === point.to ? formatDate(point.from) : range(point.from, point.to);
}
