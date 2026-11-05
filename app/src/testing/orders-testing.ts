import {
  OrderDetails,
  OrderListItem,
  OrderListPage,
  PaymentEntry,
  RefundEntry,
} from '../app/features/orders/order-models';

/** Fikcyjne dane zamówień do testów (bez danych identyfikujących). Tylko specyfikacje. */
export function orderDetails(overrides: Partial<OrderDetails> = {}): OrderDetails {
  return {
    id: 'a1',
    number: 'ZR-2026-0001',
    source: 'manual',
    externalId: null,
    shopStatus: null,
    orderDate: '2026-10-28',
    channel: 'olx',
    value: '1234.50',
    fulfillmentStatus: 'inProgress',
    customer: {
      kind: 'person',
      name: 'Jan Testowy',
      phone: '500 000 000',
      email: 'jan@example.com',
      street: 'Testowa 1',
      postalCode: '00-001',
      city: 'Testowo',
      taxId: '1234563218',
    },
    operational: {
      suppliers: ['Dostawca A', 'Dostawca B'],
      zoReferences: ['ZO/1'],
      invoiceNumber: 'FV/1/2026',
      hasPhotos: true,
      hasReview: false,
      notes: null,
    },
    comment: 'Komentarz testowy',
    createdAtUtc: '2026-10-28T10:00:00Z',
    updatedAtUtc: '2026-10-28T11:30:00Z',
    version: 'AAAAAAAAB9E=',
    shopOwnedFields: [],
    payment: {
      paid: '0.00',
      state: 'unpaid',
      amountDue: '1234.50',
      overpayment: null,
      toRefund: null,
    },
    payments: [],
    refunds: [],
    ...overrides,
  };
}

export function listItem(overrides: Partial<OrderListItem> = {}): OrderListItem {
  return {
    id: 'a1',
    number: 'ZR-2026-0001',
    orderDate: '2026-10-28',
    customerName: 'Jan Testowy',
    channel: 'olx',
    source: 'manual',
    value: '1234.50',
    fulfillmentStatus: 'inProgress',
    paid: '0.00',
    paymentState: 'unpaid',
    ...overrides,
  };
}

export function paymentEntry(overrides: Partial<PaymentEntry> = {}): PaymentEntry {
  return {
    id: 'p1',
    amount: '300.00',
    date: '2026-10-29',
    method: 'transfer',
    receivedBy: null,
    createdAtUtc: '2026-10-29T09:00:00Z',
    version: 'AAAAAAAAC1E=',
    ...overrides,
  };
}

export function refundEntry(overrides: Partial<RefundEntry> = {}): RefundEntry {
  return {
    id: 'r1',
    kind: 'moneyReturn',
    amount: '100.00',
    date: '2026-10-30',
    method: 'blik',
    reason: 'Zwrot nadpłaty',
    createdAtUtc: '2026-10-30T09:00:00Z',
    version: 'AAAAAAAAD1E=',
    ...overrides,
  };
}

export function listPage(items: OrderListItem[], total = items.length, page = 1): OrderListPage {
  return { items, page, pageSize: 25, total };
}
