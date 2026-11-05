import { StatusTone } from '../../shared/ui/status-badge';

/**
 * Typy kontraktu `/api/orders` (SF-8, spec „Data / API”). Kwoty są ciągami dziesiętnymi
 * z kropką (`"1234.50"`), daty biznesowe mają postać `yyyy-MM-dd`, a słowniki przechodzą
 * jako stabilne kody camelCase.
 */

export type OrderSource = 'manual' | 'wooCommerce';
export type OrderChannel = 'shop' | 'olx' | 'acquaintance' | 'phone' | 'other';
export type FulfillmentStatus = 'inProgress' | 'completed' | 'problem' | 'cancelled';
export type CustomerKind = 'person' | 'company';

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

export interface OrderListItem {
  readonly id: string;
  readonly number: string;
  readonly orderDate: string;
  readonly customerName: string;
  readonly channel: OrderChannel;
  readonly source: OrderSource;
  readonly value: string;
  readonly fulfillmentStatus: FulfillmentStatus;
}

export interface OrderListPage {
  readonly items: readonly OrderListItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

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
}

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
  readonly search: string | null;
}

/** Kody Problem Details, na których rozgałęzia się interfejs. */
export const ORDER_PROBLEM = {
  conflict: 'order_conflict',
  keyReused: 'idempotency_key_reused',
  alreadyCancelled: 'order_already_cancelled',
  notCancelled: 'order_not_cancelled',
  notFound: 'order_not_found',
} as const;

export const CHANNEL_LABELS: Readonly<Record<OrderChannel, string>> = {
  shop: 'Sklep',
  olx: 'OLX',
  acquaintance: 'Znajomy',
  phone: 'Telefon',
  other: 'Inne',
};

export const SOURCE_LABELS: Readonly<Record<OrderSource, string>> = {
  manual: 'Ręczne',
  wooCommerce: 'Sklep',
};

export const STATUS_LABELS: Readonly<Record<FulfillmentStatus, string>> = {
  inProgress: 'W trakcie',
  completed: 'Zakończone',
  problem: 'Problem / reklamacja',
  cancelled: 'Anulowane',
};

export const STATUS_TONES: Readonly<Record<FulfillmentStatus, StatusTone>> = {
  inProgress: 'info',
  completed: 'success',
  problem: 'warning',
  cancelled: 'neutral',
};

export const CUSTOMER_KIND_LABELS: Readonly<Record<CustomerKind, string>> = {
  person: 'Osoba',
  company: 'Firma',
};

export const CHANNELS = Object.keys(CHANNEL_LABELS) as readonly OrderChannel[];
export const SOURCES = Object.keys(SOURCE_LABELS) as readonly OrderSource[];
export const STATUSES = Object.keys(STATUS_LABELS) as readonly FulfillmentStatus[];
/** Statusy do wyboru w formularzu: „Anulowane” ustawia tylko akcja „Anuluj zamówienie”. */
export const EDITABLE_STATUSES: readonly FulfillmentStatus[] = [
  'inProgress',
  'completed',
  'problem',
];
export const CUSTOMER_KINDS = Object.keys(CUSTOMER_KIND_LABELS) as readonly CustomerKind[];
