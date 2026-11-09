import {
  EXPENSE_MESSAGES,
  emptyExpenseForm,
  expenseFormFrom,
  grossAmountError,
  netAmountError,
  netFromGross,
  paidEffectText,
  paidOnError,
  toExpenseInput,
} from './expense-form-model';
import { expenseDetails } from '../../../testing/expenses-testing';

describe('expense form model', () => {
  it.each(['', '  '])('requires the gross amount (%j)', (text) => {
    expect(grossAmountError(text)).toBe(EXPENSE_MESSAGES.grossRequired);
  });

  it.each(['0', '0,00', '-0', '-0,00'])('rejects a zero gross amount %s', (text) => {
    expect(grossAmountError(text)).toBe(EXPENSE_MESSAGES.grossZero);
  });

  it.each(['1 234,50', '-1 234,50', '−99', '0,01'])('accepts the gross amount %s', (text) => {
    expect(grossAmountError(text)).toBeNull();
  });

  it.each(['abc', '1,234', '--5'])('rejects the malformed gross amount %s', (text) => {
    expect(grossAmountError(text)).toBe(EXPENSE_MESSAGES.amountInvalid);
  });

  it('rejects an amount with more than 16 integer digits', () => {
    expect(grossAmountError('-12345678901234567')).toBe(EXPENSE_MESSAGES.amountTooLarge);
  });

  it.each([
    ['', '100', null],
    ['0', '100', null],
    ['0,00', '-100', null],
    ['81,30', '100', null],
    ['-406,50', '-500', null],
    ['406,50', '-500', EXPENSE_MESSAGES.netSign],
    ['-1', '100', EXPENSE_MESSAGES.netSign],
  ])('checks the net amount %j against gross %s', (net, gross, expected) => {
    expect(netAmountError(net, gross)).toBe(expected);
  });

  it('leaves |net| ≤ |gross| to the API', () => {
    expect(netAmountError('120,00', '100,00')).toBeNull();
  });

  it('rejects a payment date after today and requires it only in the paid action', () => {
    expect(paidOnError('2026-11-06', '2026-11-05')).toBe(EXPENSE_MESSAGES.paidOnInFuture);
    expect(paidOnError('2026-11-05', '2026-11-05')).toBeNull();
    expect(paidOnError('', '2026-11-05')).toBeNull();
    expect(paidOnError('', '2026-11-05', true)).toBe(EXPENSE_MESSAGES.paidOnRequired);
  });

  it('maps the form to the API input with canonical signed amounts and nulls', () => {
    const model = {
      ...emptyExpenseForm('2026-10-28'),
      description: '  Zwrot za transport ',
      category: 'hdsTransport' as const,
      grossAmount: '-1 234,50',
      netAmount: '0',
      payer: '  ',
    };

    expect(toExpenseInput(model)).toEqual({
      description: 'Zwrot za transport',
      category: 'hdsTransport',
      counterparty: null,
      costDate: '2026-10-28',
      dueDate: null,
      paidOn: null,
      grossAmount: '-1234.50',
      netAmount: '0.00',
      documentReference: null,
      paymentMethod: null,
      payer: null,
      comment: null,
    });
  });

  it('reads details into the form without arithmetic', () => {
    const form = expenseFormFrom(expenseDetails({ grossAmount: '-200.00', netAmount: null }));

    expect(form.grossAmount).toBe('-200,00');
    expect(form.netAmount).toBe('');
    expect(form.paidOn).toBe('');
  });

  it('explains the payment effect depending on the sign', () => {
    expect(paidEffectText('1234.50')).toBe('Wydatek trafi do wypływów w okresie daty zapłaty.');
    expect(paidEffectText('-200.00')).toBe(
      'Korekta / zwrot od dostawcy obniży wypływy w okresie daty zapłaty.',
    );
  });
});

describe('netFromGross', () => {
  it.each([
    ['1233', '1002,44'],
    ['123', '100,00'],
    ['1 234,50', '1003,66'],
    ['0,01', '0,01'],
    ['0,02', '0,02'],
    ['-246', '-200,00'],
    ['0', '0,00'],
  ])('brutto %s → netto %s (÷ 1,23, zaokrąglenie do grosza)', (gross, net) => {
    expect(netFromGross(gross)).toBe(net);
  });

  it.each(['', 'abc', '1,234'])('brak podpowiedzi dla niepoprawnego brutto %j', (gross) => {
    expect(netFromGross(gross)).toBeNull();
  });
});
