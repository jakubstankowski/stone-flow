import {
  CHANNEL_LABELS,
  CUSTOMER_KIND_LABELS,
  CustomerKind,
  FULFILLMENT_STATUS_LABELS,
  FulfillmentStatus,
  OrderChannel,
  REFUND_KIND_LABELS,
  RefundKind,
} from '../../shared/dictionaries/order-dictionaries';
import {
  PAYMENT_METHOD_LABELS,
  PaymentMethodCode,
} from '../../shared/dictionaries/payment-methods';
import { StatusTone } from '../../shared/ui/status-badge';

// Słowniki wspólne z innymi obszarami są w `shared/dictionaries`; tu eksport pod dotychczasowymi nazwami.
export type {
  CustomerKind,
  FulfillmentStatus,
  OrderChannel,
  RefundKind,
} from '../../shared/dictionaries/order-dictionaries';
export {
  CHANNEL_LABELS,
  CUSTOMER_KIND_LABELS,
  FULFILLMENT_STATUS_LABELS as STATUS_LABELS,
  REFUND_KIND_LABELS,
} from '../../shared/dictionaries/order-dictionaries';
export { PAYMENT_METHOD_LABELS } from '../../shared/dictionaries/payment-methods';

/**
 * Typy kontraktu `/api/orders` (SF-8 i wpisy płatności SF-9, spec „Data / API”). Kwoty są ciągami dziesiętnymi
 * z kropką (`"1234.50"`), daty biznesowe mają postać `yyyy-MM-dd`, a słowniki przechodzą
 * jako stabilne kody camelCase.
 */

export type OrderSource = 'manual' | 'wooCommerce';

/** Pola zamówienia WooCommerce tylko do odczytu w edycji (`OrderDetails.shopOwnedFields`). */
export type ShopOwnedField = 'orderDate' | 'value' | 'channel' | 'customer';

export interface OrderCustomer {
  readonly kind: CustomerKind;
  readonly name: string;
  readonly phone: string | null;
  readonly email: string | null;
  readonly street: string | null;
  readonly postalCode: string | null;
  readonly city: string | null;
  readonly taxId: string | null;
}

export interface OrderOperational {
  readonly suppliers: readonly string[];
  readonly zoReferences: readonly string[];
  readonly invoiceNumber: string | null;
  readonly hasPhotos: boolean;
  readonly hasReview: boolean;
  readonly notes: string | null;
}

/**
 * Treść `POST` i `PUT`. W `PUT` zamówienia ze sklepu pola należące do sklepu można pominąć;
 * `fulfillmentStatus` jest w `POST` opcjonalny (domyślnie `inProgress`).
 */
export interface OrderInput {
  readonly orderDate?: string;
  readonly channel?: OrderChannel;
  readonly value?: string;
  readonly customer?: OrderCustomer;
  readonly fulfillmentStatus: FulfillmentStatus;
  readonly operational: OrderOperational;
  readonly comment: string | null;
}

export interface OrderUpdate extends OrderInput {
  readonly version: string;
}

/** Metody wpłaty (SF-9). Pobranie jest tylko metodą: zamówienie bez wpłaty pozostaje Nieopłacone. */
export type PaymentMethod = PaymentMethodCode;
/** Metody zwrotu: bez pobrania. */
export type RefundMethod = 'transfer' | 'blik' | 'cash';
/** Stan płatności wyliczany przez backend; zamówienie anulowane ma `null`. */
export type PaymentState = 'unpaid' | 'partiallyPaid' | 'paid';
/** Wartość filtra listy: stany oraz `none` (bez stanu, czyli zamówienia anulowane). */
export type PaymentStateFilter = PaymentState | 'none';

/** Podsumowanie płatności z backendu. Frontend tylko je wyświetla, niczego nie liczy. */
export interface OrderPaymentSummary {
  readonly paid: string;
  readonly state: PaymentState | null;
  readonly amountDue: string | null;
  readonly overpayment: string | null;
  readonly toRefund: string | null;
}

export interface PaymentEntry {
  readonly id: string;
  readonly amount: string;
  readonly date: string;
  readonly method: PaymentMethod;
  readonly receivedBy: string | null;
  readonly createdAtUtc: string;
  readonly version: string;
}

export interface RefundEntry {
  readonly id: string;
  readonly kind: RefundKind;
  readonly amount: string;
  readonly date: string;
  readonly method: RefundMethod;
  readonly reason: string;
  readonly createdAtUtc: string;
  readonly version: string;
}

/** Treść `POST /api/orders/{id}/payments`; `receivedBy` tylko przy gotówce. */
export interface PaymentInput {
  readonly amount: string;
  readonly date: string;
  readonly method: PaymentMethod;
  readonly receivedBy: string | null;
}

/** Treść `POST /api/orders/{id}/refunds`. */
export interface RefundInput {
  readonly kind: RefundKind;
  readonly amount: string;
  readonly date: string;
  readonly method: RefundMethod;
  readonly reason: string;
}

/**
 * Korekta sprzedaży (SF-10): oddany towar bez zwrotu pieniędzy. Obniża sprzedaż, nie ma metody
 * i nie zmienia kwoty Zapłacono.
 */
export interface SalesCorrectionEntry {
  readonly id: string;
  readonly amount: string;
  readonly date: string;
  readonly reason: string;
  readonly createdAtUtc: string;
  readonly version: string;
}

/** Treść `POST /api/orders/{id}/sales-corrections`. */
export interface SalesCorrectionInput {
  readonly amount: string;
  readonly date: string;
  readonly reason: string;
}

/**
 * Obniżenia sprzedaży wyliczone w backendzie: zwroty sprzedaży i korekty. `excess` to nadwyżka
 * ponad wartość zamówienia albo `null`.
 */
export interface SalesReductions {
  readonly returnsTotal: string;
  readonly correctionsTotal: string;
  readonly total: string;
  readonly excess: string | null;
}

export interface EntryCreated {
  readonly id: string;
  readonly version: string;
}

export interface OrderListItem {
  readonly id: string;
  readonly number: string;
  readonly orderDate: string;
  readonly customerName: string;
  readonly channel: OrderChannel;
  readonly source: OrderSource;
  readonly value: string;
  readonly fulfillmentStatus: FulfillmentStatus;
  readonly paid: string;
  readonly paymentState: PaymentState | null;
  /** Surowy kod statusu WooCommerce (np. `processing`); `null` dla zamówień ręcznych (SF-12). */
  readonly shopStatus: string | null;
}

export interface OrderListPage {
  readonly items: readonly OrderListItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

/**
 * Ostrzeżenia o rozbieżności ERP i sklepu wyliczane w backendzie (SF-12). Tylko informacja:
 * ERP niczego nie wysyła do sklepu.
 */
export type ShopWarning = 'activeInShop' | 'deletedInShop' | 'cancelledInShop' | 'refundedInShop';

export interface OrderDetails {
  readonly id: string;
  readonly number: string;
  readonly source: OrderSource;
  readonly externalId: string | null;
  readonly shopStatus: string | null;
  readonly orderDate: string;
  readonly channel: OrderChannel;
  readonly value: string;
  readonly fulfillmentStatus: FulfillmentStatus;
  readonly customer: OrderCustomer;
  readonly operational: OrderOperational;
  readonly comment: string | null;
  readonly createdAtUtc: string;
  readonly updatedAtUtc: string;
  readonly version: string;
  readonly shopOwnedFields: readonly string[];
  readonly payment: OrderPaymentSummary;
  /** Po dacie rosnąco, potem `createdAtUtc` i `id` (kolejność z backendu). */
  readonly payments: readonly PaymentEntry[];
  readonly refunds: readonly RefundEntry[];
  readonly salesCorrections: readonly SalesCorrectionEntry[];
  readonly salesReductions: SalesReductions;
  /** Data modyfikacji w sklepie z ostatnio zastosowanej dostawy (ISO UTC). */
  readonly shopModifiedAtUtc: string | null;
  /** Oznaczenie usunięcia zamówienia w sklepie (ISO UTC). */
  readonly shopDeletedAtUtc: string | null;
  /** Suma zwrotów w sklepie; tylko informacja, nie tworzy wypływu. */
  readonly shopRefundedAmount: string;
  readonly shopWarnings: readonly ShopWarning[];
  /** Stan akcji „Anonimizuj dane klienta” (SF-22); wylicza go API. */
  readonly anonymization: OrderAnonymization;
}

/** Stan akcji anonimizacji danych klienta (SF-22). */
export type AnonymizationStatus = 'disabled' | 'retentionPending' | 'available' | 'anonymized';

/**
 * Anonimizacja danych klienta: daty retencji (`yyyy-MM-dd`) tylko dla `retentionPending` i `available`,
 * czas anonimizacji (ISO UTC) tylko dla `anonymized`. Frontend dat nie liczy.
 */
export interface OrderAnonymization {
  readonly status: AnonymizationStatus;
  readonly retentionStartDate: string | null;
  readonly availableFrom: string | null;
  readonly anonymizedAtUtc: string | null;
}

/** Oznaczenie zanonimizowanego zamówienia w nagłówku szczegółów. */
export const ANONYMIZED_BADGE_LABEL = 'Dane klienta zanonimizowane';

export interface OrderCreated {
  readonly id: string;
  readonly number: string;
  readonly version: string;
}

export interface OrderVersion {
  readonly version: string;
}

export type OrderSort = 'orderDate' | 'number' | 'customer' | 'value';

export interface OrderListQuery {
  readonly page: number;
  readonly pageSize: number;
  readonly sort: OrderSort;
  readonly direction: 'asc' | 'desc';
  readonly dateFrom: string | null;
  readonly dateTo: string | null;
  readonly channel: OrderChannel | null;
  readonly source: OrderSource | null;
  readonly statuses: readonly FulfillmentStatus[];
  readonly paymentStates: readonly PaymentStateFilter[];
  readonly search: string | null;
  /** `true` pokazuje zamówienia ze sklepu o statusie pending, failed i checkout-draft (SF-12). */
  readonly includeHiddenShopStatuses: boolean;
}

/** Kody Problem Details, na których rozgałęzia się interfejs. */
export const ORDER_PROBLEM = {
  conflict: 'order_conflict',
  keyReused: 'idempotency_key_reused',
  alreadyCancelled: 'order_already_cancelled',
  notCancelled: 'order_not_cancelled',
  notFound: 'order_not_found',
  alreadyAnonymized: 'order_already_anonymized',
  anonymizationDisabled: 'anonymization_disabled',
  retentionNotElapsed: 'retention_not_elapsed',
  customerAnonymized: 'order_customer_anonymized',
} as const;

/** Kody Problem Details tras wpisów płatności (SF-9). */
export const ENTRY_PROBLEM = {
  notFound: 'entry_not_found',
  conflict: 'entry_conflict',
  keyReused: 'idempotency_key_reused',
  paidWouldBeNegative: 'paid_would_be_negative',
  refundExceedsPaid: 'refund_exceeds_paid',
  salesReductionLimitExceeded: 'sales_reduction_limit_exceeded',
} as const;

export const REFUND_METHOD_LABELS: Readonly<Record<RefundMethod, string>> = {
  transfer: 'Przelew',
  blik: 'BLIK',
  cash: 'Gotówka',
};

/** Krótki opis wpływu rodzaju zwrotu na sprzedaż (design guide, szczegóły zamówienia). */
export const REFUND_KIND_DESCRIPTIONS: Readonly<Record<RefundKind, string>> = {
  salesReturn: 'Oddany towar, wycofana sprzedaż. Obniży sprzedaż.',
  moneyReturn: 'Zwrot nadpłaty lub zaliczki. Nie zmienia sprzedaży.',
};

export const PAYMENT_STATE_LABELS: Readonly<Record<PaymentState, string>> = {
  unpaid: 'Nieopłacone',
  partiallyPaid: 'Częściowo opłacone',
  paid: 'Opłacone',
};

export const PAYMENT_STATE_TONES: Readonly<Record<PaymentState, StatusTone>> = {
  unpaid: 'warning',
  partiallyPaid: 'info',
  paid: 'success',
};

export const PAYMENT_STATE_FILTER_LABELS: Readonly<Record<PaymentStateFilter, string>> = {
  ...PAYMENT_STATE_LABELS,
  none: 'Bez stanu (anulowane)',
};

export const SOURCE_LABELS: Readonly<Record<OrderSource, string>> = {
  manual: 'Ręczne',
  wooCommerce: 'Sklep',
};

export const STATUS_TONES: Readonly<Record<FulfillmentStatus, StatusTone>> = {
  inProgress: 'info',
  completed: 'success',
  problem: 'warning',
  cancelled: 'neutral',
};

export const CHANNELS = Object.keys(CHANNEL_LABELS) as readonly OrderChannel[];
export const SOURCES = Object.keys(SOURCE_LABELS) as readonly OrderSource[];
export const STATUSES = Object.keys(FULFILLMENT_STATUS_LABELS) as readonly FulfillmentStatus[];
/** Statusy do wyboru w formularzu: „Anulowane” ustawia tylko akcja „Anuluj zamówienie”. */
export const EDITABLE_STATUSES: readonly FulfillmentStatus[] = [
  'inProgress',
  'completed',
  'problem',
];
export const CUSTOMER_KINDS = Object.keys(CUSTOMER_KIND_LABELS) as readonly CustomerKind[];
export const PAYMENT_METHODS = Object.keys(PAYMENT_METHOD_LABELS) as readonly PaymentMethod[];
export const REFUND_METHODS = Object.keys(REFUND_METHOD_LABELS) as readonly RefundMethod[];
export const REFUND_KINDS = Object.keys(REFUND_KIND_LABELS) as readonly RefundKind[];
export const PAYMENT_STATE_FILTERS = Object.keys(
  PAYMENT_STATE_FILTER_LABELS,
) as readonly PaymentStateFilter[];
