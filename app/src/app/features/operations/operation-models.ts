import { PAYMENT_METHOD_LABELS } from '../../shared/dictionaries/payment-methods';
import {
  OperationKind,
  OperationItem,
  OperationMethod,
} from '../../shared/dictionaries/operations';

// Słowniki wspólne z innymi obszarami są w `shared/dictionaries`; tu eksport pod dotychczasowymi nazwami.
export { EXPENSE_CATEGORY_LABELS as CATEGORY_LABELS } from '../../shared/dictionaries/expense-categories';
export { REFUND_KIND_LABELS } from '../../shared/dictionaries/order-dictionaries';
export { PAYMENT_METHOD_LABELS as METHOD_LABELS } from '../../shared/dictionaries/payment-methods';
export type {
  OperationCategory,
  OperationDirection,
  OperationItem,
  OperationKind,
  OperationMethod,
  OperationRefundKind,
  OperationSourceType,
} from '../../shared/dictionaries/operations';
export {
  DIRECTION_LABELS,
  isNegativeAmount,
  KIND_FILTER_LABELS,
  kindLabel,
  NEGATIVE_AMOUNT_LABEL,
  OPERATION_KINDS,
  sourceLink,
} from '../../shared/dictionaries/operations';

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

export const NO_METHOD = 'Nie podano';

export function methodLabel(method: OperationMethod | null): string {
  return method ? PAYMENT_METHOD_LABELS[method] : NO_METHOD;
}
