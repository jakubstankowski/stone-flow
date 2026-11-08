import { ExpenseDetails, ExpenseListItem } from '../app/features/expenses/expense-models';
import { JobStatus } from '../app/features/expenses/jobs-api';
import {
  RecurringExpenseDetails,
  RecurringExpenseListItem,
  RecurringExpensePreview,
} from '../app/features/expenses/recurring-expense-models';

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
    recurring: null,
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
    recurringDefinitionId: null,
    ...overrides,
  };
}

/** Fikcyjna definicja wydatku cyklicznego (SF-16). */
export function recurringDetails(
  overrides: Partial<RecurringExpenseDetails> = {},
): RecurringExpenseDetails {
  return {
    id: 'c1',
    description: 'Abonament reklamowy',
    category: 'marketing',
    counterparty: 'Portal B',
    grossAmount: '123.00',
    netAmount: '100.00',
    paymentMethod: 'transfer',
    payer: null,
    comment: null,
    dayOfMonth: 10,
    startsOn: '2026-09-01',
    endsOn: null,
    status: 'active',
    pausedOn: null,
    nextDueDate: '2099-12-10',
    occurrenceCount: 2,
    resumePreview: null,
    createdAtUtc: '2026-09-01T08:00:00Z',
    updatedAtUtc: '2026-09-01T08:00:00Z',
    version: 'AAAAAAAAC1E=',
    ...overrides,
  };
}

export function recurringListItem(
  overrides: Partial<RecurringExpenseListItem> = {},
): RecurringExpenseListItem {
  return {
    id: 'c1',
    description: 'Abonament reklamowy',
    category: 'marketing',
    counterparty: 'Portal B',
    grossAmount: '123.00',
    dayOfMonth: 10,
    startsOn: '2026-09-01',
    endsOn: null,
    status: 'active',
    pausedOn: null,
    nextDueDate: '2026-12-10',
    ...overrides,
  };
}

export function recurringPreview(
  overrides: Partial<RecurringExpensePreview> = {},
): RecurringExpensePreview {
  return {
    occurrencesToCreate: 0,
    firstImmediateDueDate: null,
    beforeLaunchCount: 0,
    nextDueDate: '2026-12-10',
    paused: false,
    droppedDueCount: 0,
    createdAfterEndCount: 0,
    createdBeforeStartCount: 0,
    ...overrides,
  };
}

/** Pozycja `GET /api/jobs` dla zadania `recurring-expenses`. */
export function recurringJob(overrides: Partial<JobStatus> = {}): JobStatus {
  return {
    key: 'recurring-expenses',
    schedule: 'daily 00:10 Europe/Warsaw',
    isRunning: false,
    lastRun: null,
    lastSuccessStartedAtUtc: null,
    consecutiveFailures: 0,
    nextAttemptAtUtc: null,
    ...overrides,
  };
}
