import { OperationItem, OperationListPage } from '../app/features/operations/operation-models';
import { WithdrawalDetails } from '../app/features/operations/withdrawal-models';

/** Fikcyjna operacja do testów (bez danych identyfikujących sklep i klientów). */
export function operationItem(overrides: Partial<OperationItem> = {}): OperationItem {
  return {
    kind: 'payment',
    id: 'p1',
    date: '2026-11-05',
    direction: 'inflow',
    amount: '1000.00',
    method: 'transfer',
    refundKind: null,
    description: 'ZAM/2026/11/0001',
    category: null,
    sourceType: 'order',
    sourceId: 'o1',
    sourceLabel: 'ZAM/2026/11/0001',
    ...overrides,
  };
}

export function operationListPage(overrides: Partial<OperationListPage> = {}): OperationListPage {
  const items = overrides.items ?? [];
  return {
    items,
    page: 1,
    pageSize: 25,
    total: items.length,
    totals: { inflows: '0.00', outflows: '0.00', change: '0.00' },
    effectiveFrom: '2026-11-01',
    launchDate: '2026-10-01',
    ...overrides,
  };
}

/** Fikcyjna wypłata właściciela („Właściciel A”). */
export function withdrawalDetails(overrides: Partial<WithdrawalDetails> = {}): WithdrawalDetails {
  return {
    id: 'wp1',
    person: 'Właściciel A',
    amount: '2000.00',
    withdrawnOn: '2026-11-05',
    method: 'transfer',
    createdAtUtc: '2026-11-05T12:00:00Z',
    updatedAtUtc: '2026-11-05T12:00:00Z',
    version: 'AAAAAAAAB9E=',
    ...overrides,
  };
}
