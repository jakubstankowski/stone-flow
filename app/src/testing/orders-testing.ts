import { OrderDetails, OrderListItem, OrderListPage } from '../app/features/orders/order-models';

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
    ...overrides,
  };
}

export function listPage(items: OrderListItem[], total = items.length, page = 1): OrderListPage {
  return { items, page, pageSize: 25, total };
}
