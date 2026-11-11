/** Wybór okresu raportu (kod API `GET /api/reports/sales`). */
export type PeriodKind = 'currentMonth' | 'previousMonth' | 'year' | 'custom';

export type Granularity = 'day' | 'month';

export type ComparisonStatus = 'available' | 'noBasis';

/** Rodzaj składnika sprzedaży (kod API). */
export type SalesComponentKind = 'order' | 'salesReturn' | 'salesCorrection';

/** Kanał zamówienia (kod API, jak w `features/orders`). */
export type SalesChannel = 'shop' | 'olx' | 'acquaintance' | 'phone' | 'other';

/** Kategoria wydatku (kod API, jak w `features/expenses`). */
export type CostCategory =
  | 'materialPurchase'
  | 'cutting'
  | 'hdsTransport'
  | 'marketing'
  | 'otherOperations'
  | 'vatTax'
  | 'ppeTax';

/** Wybór okresu w UI; `year`, `from` i `to` mają znaczenie tylko dla swojego rodzaju. */
export interface PeriodSelection {
  readonly kind: PeriodKind;
  readonly year: string;
  readonly from: string;
  readonly to: string;
}

/** Rozwiązany okres z API; daty `yyyy-MM-dd`. */
export interface ReportPeriod {
  readonly kind: PeriodKind;
  readonly from: string;
  readonly to: string;
  /** max(`from`, data uruchomienia). */
  readonly effectiveFrom: string;
  readonly launchDate: string;
  readonly beforeLaunch: boolean;
  readonly inProgress: boolean;
  readonly granularity: Granularity;
}

/** Okres bazowy porównania; `available = false` oznacza bazę sprzed uruchomienia. */
export interface ReportComparisonHeader {
  readonly from: string;
  readonly to: string;
  readonly available: boolean;
  readonly reason: 'beforeLaunch' | null;
}

/** Porównanie karty z kwotą (ciągi dziesiętne z API; `null` przy `noBasis`). */
export interface AmountComparison {
  readonly status: ComparisonStatus;
  readonly base: string | null;
  readonly difference: string | null;
  readonly percent: string | null;
}

/** Porównanie liczby zamówień: baza i różnica jako liczby całkowite. */
export interface CountComparison {
  readonly status: ComparisonStatus;
  readonly base: number | null;
  readonly difference: number | null;
  readonly percent: string | null;
}

export interface AmountKpi {
  readonly value: string;
  readonly comparison: AmountComparison;
}

export interface CountKpi {
  readonly value: number;
  readonly comparison: CountComparison;
}

export interface SalesKpis {
  readonly sales: AmountKpi;
  readonly costs: AmountKpi;
  readonly result: AmountKpi;
  readonly orderCount: CountKpi;
}

/** Przedział wykresu: kwoty policzone w backendzie. */
export interface SeriesPoint {
  readonly from: string;
  readonly to: string;
  readonly sales: string;
  readonly costs: string;
  readonly result: string;
}

export interface ChannelAmount {
  readonly channel: SalesChannel;
  readonly amount: string;
}

export interface CategoryAmount {
  readonly category: CostCategory;
  readonly amount: string;
}

/**
 * Raport widoku „Sprzedaż”. Wszystkie kwoty, sumy, porównania i podziały liczy backend;
 * `kpis = null` i puste listy przy okresie w całości przed uruchomieniem (brak danych ≠ zero).
 */
export interface SalesReport {
  readonly period: ReportPeriod;
  readonly comparison: ReportComparisonHeader;
  readonly kpis: SalesKpis | null;
  readonly series: readonly SeriesPoint[];
  readonly channels: readonly ChannelAmount[];
  readonly categories: readonly CategoryAmount[];
  readonly calculatedAtUtc: string;
}

/** Pozycja zestawienia; `amount` to kwota ze znakiem (zwrot i korekta ujemne). */
export interface SalesComponentItem {
  readonly kind: SalesComponentKind;
  readonly id: string;
  readonly date: string;
  readonly amount: string;
  readonly orderId: string;
  readonly orderNumber: string;
  readonly channel: SalesChannel;
}

/** Strona zestawienia; `sum` dla całego filtra, `null` przy okresie przed uruchomieniem. */
export interface SalesComponentsPage {
  readonly period: ReportPeriod;
  readonly items: readonly SalesComponentItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
  readonly sum: string | null;
}

/** Filtry, kierunek i strona zestawienia. */
export interface ComponentsQuery {
  readonly channel: SalesChannel | null;
  readonly kind: SalesComponentKind | null;
  readonly direction: 'asc' | 'desc';
  readonly page: number;
  readonly pageSize: number;
}

/** Kody Problem Details raportu, na których rozgałęzia się interfejs. */
export const REPORT_PROBLEM = {
  rangeTooLarge: 'report_range_too_large',
} as const;

export const PERIOD_KINDS: readonly PeriodKind[] = [
  'currentMonth',
  'previousMonth',
  'year',
  'custom',
];

export const PERIOD_LABELS: Readonly<Record<PeriodKind, string>> = {
  currentMonth: 'Bieżący miesiąc',
  previousMonth: 'Poprzedni miesiąc',
  year: 'Rok',
  custom: 'Własny zakres',
};

export const COMPONENT_KINDS: readonly SalesComponentKind[] = [
  'order',
  'salesReturn',
  'salesCorrection',
];

export const COMPONENT_KIND_LABELS: Readonly<Record<SalesComponentKind, string>> = {
  order: 'Zamówienie',
  salesReturn: 'Zwrot sprzedaży',
  salesCorrection: 'Korekta sprzedaży',
};

/** Kopia słownika z `features/orders` (bez importów między obszarami, jak w SF-17). */
export const CHANNEL_LABELS: Readonly<Record<SalesChannel, string>> = {
  shop: 'Sklep',
  olx: 'OLX',
  acquaintance: 'Znajomy',
  phone: 'Telefon',
  other: 'Inne',
};

/** Kopia słownika z `features/expenses` (bez importów między obszarami). */
export const CATEGORY_LABELS: Readonly<Record<CostCategory, string>> = {
  materialPurchase: 'Zakup materiału u dostawców',
  cutting: 'Cięcie',
  hdsTransport: 'Transport HDS',
  marketing: 'Marketing',
  otherOperations: 'Pozostała działalność',
  vatTax: 'Podatek VAT',
  ppeTax: 'Podatek PPE',
};

/** Opisy podstaw kart KPI (spec UI pkt 4). */
export const KPI_DESCRIPTIONS = {
  sales:
    'Wartość brutto zamówień według daty zamówienia minus zwroty sprzedaży i korekty sprzedaży według ich dat. Niezależna od wpłat.',
  costs:
    'Wydatki brutto, opłacone i nieopłacone, według daty kosztu. Ujemne wydatki pomniejszają koszty.',
  result: 'Sprzedaż minus koszty w tym samym okresie. Wypłaty właścicieli go nie obniżają.',
  orderCount:
    'Zamówienia wchodzące do sprzedaży według daty zamówienia. Zamówienie ze zwrotem lub korektą nadal się liczy.',
} as const;

export const COMPONENTS_BASIS_LABEL =
  'Kwoty brutto w PLN. Zamówienia według daty zamówienia, zwroty sprzedaży i korekty sprzedaży według ich dat.';

export const NO_BASIS_TEXT = 'brak podstawy porównania';
