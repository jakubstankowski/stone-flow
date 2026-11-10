/**
 * Słowniki zamówienia (stałe kody API camelCase) z etykietami: kanał, stan realizacji, rodzaj
 * klienta i rodzaj zwrotu. Wspólne dla zamówień, operacji i historii zmian.
 */
export type OrderChannel = 'shop' | 'olx' | 'acquaintance' | 'phone' | 'other';
export type FulfillmentStatus = 'inProgress' | 'completed' | 'problem' | 'cancelled';
export type CustomerKind = 'person' | 'company';
export type RefundKind = 'salesReturn' | 'moneyReturn';

export const CHANNEL_LABELS: Readonly<Record<OrderChannel, string>> = {
  shop: 'Sklep',
  olx: 'OLX',
  acquaintance: 'Znajomy',
  phone: 'Telefon',
  other: 'Inne',
};

export const FULFILLMENT_STATUS_LABELS: Readonly<Record<FulfillmentStatus, string>> = {
  inProgress: 'W trakcie',
  completed: 'Zakończone',
  problem: 'Problem / reklamacja',
  cancelled: 'Anulowane',
};

export const CUSTOMER_KIND_LABELS: Readonly<Record<CustomerKind, string>> = {
  person: 'Osoba',
  company: 'Firma',
};

export const REFUND_KIND_LABELS: Readonly<Record<RefundKind, string>> = {
  salesReturn: 'Zwrot sprzedaży',
  moneyReturn: 'Zwrot pieniędzy',
};
