/** Rodzaj operacji pieniężnej (stały kod API `GET /api/operations`). */
export type OperationKind = 'payment' | 'refund' | 'expense' | 'withdrawal';

export type OperationDirection = 'inflow' | 'outflow';

export type OperationRefundKind = 'salesReturn' | 'moneyReturn';

export type OperationMethod = 'transfer' | 'blik' | 'cash' | 'cashOnDelivery';

export type OperationSourceType = 'order' | 'expense' | 'withdrawal';

/** Kategoria opłaconego wydatku (kod API, jak w `features/expenses`). */
export type OperationCategory =
  | 'materialPurchase'
  | 'cutting'
  | 'hdsTransport'
  | 'marketing'
  | 'otherOperations'
  | 'vatTax'
  | 'ppeTax';

/**
 * Pozycja listy „Operacje”. `amount` to kwota zapisana jako ciąg dziesiętny (`"-50.00"`), bez
 * zmiany znaku przez kierunek; kierunek niesie wyłącznie `direction`.
 */
export interface OperationItem {
  readonly kind: OperationKind;
  readonly id: string;
  readonly date: string;
  readonly direction: OperationDirection;
  readonly amount: string;
  readonly method: OperationMethod | null;
  readonly refundKind: OperationRefundKind | null;
  readonly description: string | null;
  readonly category: OperationCategory | null;
  readonly sourceType: OperationSourceType;
  readonly sourceId: string;
  readonly sourceLabel: string;
}

/** Sumy dla całego filtra, policzone w backendzie (ciągi dziesiętne, mogą być ujemne). */
export interface OperationTotals {
  readonly inflows: string;
  readonly outflows: string;
  readonly change: string;
}

export interface OperationListPage {
  readonly items: readonly OperationItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
  readonly totals: OperationTotals;
  /** max(`from`, data uruchomienia), `yyyy-MM-dd`. */
  readonly effectiveFrom: string;
  readonly launchDate: string;
}

export interface OperationListQuery {
  readonly from: string;
  readonly to: string;
  readonly kinds: readonly OperationKind[];
  readonly direction: 'asc' | 'desc';
  readonly page: number;
  readonly pageSize: number;
}

/** Kody Problem Details listy, na których rozgałęzia się interfejs. */
export const OPERATIONS_PROBLEM = {
  rangeTooLarge: 'operations_range_too_large',
} as const;

/** Stała kolejność rodzajów (filtr, zapytanie, remis sortowania). */
export const OPERATION_KINDS: readonly OperationKind[] = [
  'payment',
  'refund',
  'expense',
  'withdrawal',
];

/** Etykiety filtra rodzaju; „Zwrot” obejmuje oba rodzaje zwrotu. */
export const KIND_FILTER_LABELS: Readonly<Record<OperationKind, string>> = {
  payment: 'Wpłata',
  refund: 'Zwrot',
  expense: 'Opłacony wydatek',
  withdrawal: 'Wypłata właściciela',
};

export const REFUND_KIND_LABELS: Readonly<Record<OperationRefundKind, string>> = {
  salesReturn: 'Zwrot sprzedaży',
  moneyReturn: 'Zwrot pieniędzy',
};

/** Etykieta kolumny „Rodzaj”: zwrot pokazuje swój rodzaj. */
export function kindLabel(item: Pick<OperationItem, 'kind' | 'refundKind'>): string {
  if (item.kind === 'refund' && item.refundKind) {
    return REFUND_KIND_LABELS[item.refundKind];
  }
  return KIND_FILTER_LABELS[item.kind];
}

export const DIRECTION_LABELS: Readonly<Record<OperationDirection, string>> = {
  inflow: 'Wpływ',
  outflow: 'Wypływ',
};

export const METHOD_LABELS: Readonly<Record<OperationMethod, string>> = {
  transfer: 'Przelew',
  blik: 'BLIK',
  cash: 'Gotówka',
  cashOnDelivery: 'Pobranie',
};

export const NO_METHOD = 'Nie podano';

export function methodLabel(method: OperationMethod | null): string {
  return method ? METHOD_LABELS[method] : NO_METHOD;
}

/** Kopia słownika z `features/expenses` (bez importów między obszarami). */
export const CATEGORY_LABELS: Readonly<Record<OperationCategory, string>> = {
  materialPurchase: 'Zakup materiału u dostawców',
  cutting: 'Cięcie',
  hdsTransport: 'Transport HDS',
  marketing: 'Marketing',
  otherOperations: 'Pozostała działalność',
  vatTax: 'Podatek VAT',
  ppeTax: 'Podatek PPE',
};

/** Dopisek przy opłaconym wydatku z ujemną kwotą (jak na liście wydatków). */
export const NEGATIVE_AMOUNT_LABEL = 'Korekta / zwrot od dostawcy';

/** Kwota z API (`"-50.00"`) jest ujemna — sprawdzenie znaku tekstu, bez liczenia. */
export function isNegativeAmount(amount: string | null | undefined): boolean {
  return (amount ?? '').trim().startsWith('-');
}

/** Adres źródła operacji: zamówienie, wydatek albo wypłata. */
export function sourceLink(item: Pick<OperationItem, 'sourceType' | 'sourceId'>): string[] {
  switch (item.sourceType) {
    case 'order':
      return ['/zamowienia', item.sourceId];
    case 'expense':
      return ['/wydatki', item.sourceId];
    case 'withdrawal':
      return ['/operacje', 'wyplaty', item.sourceId];
  }
}
