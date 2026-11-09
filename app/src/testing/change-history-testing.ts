import {
  ChangeHistoryItem,
  ChangeHistoryObject,
  ChangeHistoryPage,
} from '../app/features/change-history/change-history-models';

/** Fikcyjny wpis historii zmian (bez danych identyfikujących sklep i klientów). */
export function changeHistoryItem(
  overrides: Partial<Omit<ChangeHistoryItem, 'object'>> & {
    object?: Partial<ChangeHistoryObject>;
  } = {},
): ChangeHistoryItem {
  const { object, ...rest } = overrides;
  return {
    id: '1',
    occurredAt: '2026-11-05T12:00:00.0000000Z',
    author: { kind: 'account' },
    operation: 'updated',
    changes: [
      { field: 'grossAmount', oldValue: '100.00', newValue: '1234.50', isPersonalData: false },
    ],
    ...rest,
    object: {
      type: 'expense',
      id: 'e1',
      businessDate: '2026-10-28',
      label: 'Wydatek – Cięcie',
      parent: null,
      deleted: false,
      ...object,
    },
  };
}

export function changeHistoryPage(overrides: Partial<ChangeHistoryPage> = {}): ChangeHistoryPage {
  const items = overrides.items ?? [];
  return { items, total: items.length, page: 1, pageSize: 25, ...overrides };
}
