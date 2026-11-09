import { recurringDetails } from '../../../testing/expenses-testing';
import {
  RECURRING_MESSAGES,
  dayOfMonthError,
  endsOnError,
  parseDayOfMonth,
  previewQueryOf,
  recurringContentFrom,
  recurringErrorMessage,
  recurringGrossError,
  recurringNetError,
  scheduleFrom,
  startsOnError,
  toRecurringExpenseInput,
} from './recurring-expense-form-model';

describe('recurring expense form model', () => {
  it('maps a definition to the form and back without losing the current day', () => {
    const definition = recurringDetails({ dayOfMonth: 31, endsOn: '2027-12-31', payer: 'Anna' });
    const model = { ...recurringContentFrom(definition), ...scheduleFrom(definition) };

    expect(model.grossAmount).toBe('123,00');
    expect(model.dayOfMonth).toBe('31');
    expect(toRecurringExpenseInput(model)).toEqual({
      description: 'Abonament reklamowy',
      category: 'marketing',
      counterparty: 'Portal B',
      grossAmount: '123.00',
      netAmount: '100.00',
      paymentMethod: 'transfer',
      payer: 'Anna',
      comment: null,
      dayOfMonth: 31,
      startsOn: '2026-09-01',
      endsOn: '2027-12-31',
    });
  });

  it('validates the day of month 1–31', () => {
    expect(parseDayOfMonth('1')).toBe(1);
    expect(parseDayOfMonth('31')).toBe(31);
    expect(parseDayOfMonth('0')).toBeNull();
    expect(parseDayOfMonth('32')).toBeNull();
    expect(parseDayOfMonth('1.5')).toBeNull();
    expect(dayOfMonthError('')).toBe(RECURRING_MESSAGES.dayInvalid);
    expect(dayOfMonthError('15')).toBeNull();
  });

  it('validates the start, the end and their order', () => {
    expect(startsOnError('')).toBe(RECURRING_MESSAGES.startsOnRequired);
    expect(startsOnError('1999-12-31')).toBe(RECURRING_MESSAGES.startsOnRange);
    expect(startsOnError('2026-11-01')).toBeNull();
    expect(endsOnError('', '2026-11-01')).toBeNull();
    expect(endsOnError('2026-10-31', '2026-11-01')).toBe(RECURRING_MESSAGES.endsBeforeStart);
    expect(endsOnError('2026-11-01', '2026-11-01')).toBeNull();
    expect(endsOnError('2100-01-01', '2026-11-01')).toBe(RECURRING_MESSAGES.endsOnRange);
  });

  it('requires a positive gross and a non-negative net amount', () => {
    expect(recurringGrossError('')).toBe(RECURRING_MESSAGES.grossRequired);
    expect(recurringGrossError('0')).toBe(RECURRING_MESSAGES.grossNotPositive);
    expect(recurringGrossError('-10,00')).toBe(RECURRING_MESSAGES.grossNotPositive);
    expect(recurringGrossError('1 234,50')).toBeNull();
    expect(recurringNetError('')).toBeNull();
    expect(recurringNetError('0')).toBeNull();
    expect(recurringNetError('-1')).toBe(RECURRING_MESSAGES.netNegative);
  });

  it('builds a preview query only for a valid schedule', () => {
    expect(previewQueryOf({ dayOfMonth: '10', startsOn: '2026-11-01', endsOn: '' }, 'c1')).toEqual({
      dayOfMonth: 10,
      startsOn: '2026-11-01',
      endsOn: null,
      definitionId: 'c1',
    });
    expect(previewQueryOf({ dayOfMonth: '', startsOn: '2026-11-01', endsOn: '' }, null)).toBeNull();
    expect(
      previewQueryOf({ dayOfMonth: '10', startsOn: '2026-11-01', endsOn: '2026-10-01' }, null),
    ).toBeNull();
  });

  it('maps network errors and 503 to the retry message', () => {
    expect(recurringErrorMessage({ kind: 'network' })).toBe(
      'Nie udało się zapisać. Spróbuj ponownie.',
    );
    expect(recurringErrorMessage({ kind: 'problem', status: 409, detail: 'Opis', code: 'x' })).toBe(
      'Opis',
    );
  });
});
