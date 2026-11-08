import { ExpenseDetails, ExpenseListItem } from '../app/features/expenses/expense-models';

/** Fikcyjny wydatek do testów (bez danych identyfikujących sklep). */
export function expenseDetails(overrides: Partial<ExpenseDetails> = {}): ExpenseDetails {
  return {
    id: 'w1',
    description: 'Cięcie płyt',
    category: 'cutting',
    counterparty: 'Dostawca A',
    costDate: '2026-10-28',
    dueDate: '2026-11-10',
    paidOn: null,
    grossAmount: '1234.50',
    netAmount: '1003.66',
    documentReference: 'FV 12/2026',
    paymentMethod: 'transfer',
    payer: null,
    comment: null,
    paymentStatus: 'planned',
    overdue: false,
    createdAtUtc: '2026-11-05T12:00:00Z',
    updatedAtUtc: '2026-11-05T12:00:00Z',
    version: 'AAAAAAAAB9E=',
    ...overrides,
  };
}

export function expenseListItem(overrides: Partial<ExpenseListItem> = {}): ExpenseListItem {
  return {
    id: 'w1',
    description: 'Cięcie płyt',
    category: 'cutting',
    counterparty: 'Dostawca A',
    costDate: '2026-10-28',
    dueDate: '2026-11-10',
    paidOn: null,
    grossAmount: '1234.50',
    paymentStatus: 'planned',
    overdue: false,
    ...overrides,
  };
}
