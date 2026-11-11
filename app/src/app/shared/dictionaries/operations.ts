import { ExpenseCategory } from './expense-categories';
import { REFUND_KIND_LABELS, RefundKind } from './order-dictionaries';
import { PaymentMethodCode } from './payment-methods';

// Słowniki operacji pieniężnych wspólne dla listy „Operacje” (SF-17) i Pulpitu (SF-20, widok
// „Przepływy pieniężne”): typy pozycji, rodzaje, kierunki, etykiety i adresy źródeł.

/** Rodzaj operacji pieniężnej (stały kod API `GET /api/operations`, `GET /api/reports/cash-flow`). */
export type OperationKind = 'payment' | 'refund' | 'expense' | 'withdrawal';

export type OperationDirection = 'inflow' | 'outflow';

export type OperationRefundKind = RefundKind;

export type OperationMethod = PaymentMethodCode;

export type OperationSourceType = 'order' | 'expense' | 'withdrawal';

/** Kategoria opłaconego wydatku (kod API). */
export type OperationCategory = ExpenseCategory;

/**
 * Pozycja listy „Operacje” i ostatnich operacji Pulpitu. `amount` to kwota zapisana jako ciąg
 * dziesiętny (`"-50.00"`), bez zmiany znaku przez kierunek; kierunek niesie wyłącznie `direction`.
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
