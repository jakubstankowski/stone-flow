import { paymentEntry, refundEntry } from '../../../testing/orders-testing';
import {
  ENTRY_MESSAGES,
  emptyRefundForm,
  entryAmountError,
  entryDateError,
  paymentFormFrom,
  refundFormFrom,
  toPaymentInput,
  toRefundInput,
} from './entry-form-model';

describe('entry form model', () => {
  it.each([
    ['', ENTRY_MESSAGES.amountRequired],
    ['0', ENTRY_MESSAGES.amountInvalid],
    ['0,00', ENTRY_MESSAGES.amountInvalid],
    ['-5', ENTRY_MESSAGES.amountInvalid],
    ['1,234', ENTRY_MESSAGES.amountInvalid],
    ['abc', ENTRY_MESSAGES.amountInvalid],
    ['12345678901234567', ENTRY_MESSAGES.amountTooLarge],
    ['0,01', null],
    ['1 234,50', null],
    ['1234567890123456,99', null],
  ])('validates the amount %j', (text, expected) => {
    expect(entryAmountError(text)).toBe(expected);
  });

  it('rejects a missing date and a date after today in Europe/Warsaw', () => {
    expect(entryDateError('', '2026-11-05')).toBe(ENTRY_MESSAGES.dateRequired);
    expect(entryDateError('2026-11-06', '2026-11-05')).toBe(ENTRY_MESSAGES.dateInFuture);
    expect(entryDateError('2026-11-05', '2026-11-05')).toBeNull();
    expect(entryDateError('2020-01-01', '2026-11-05')).toBeNull();
  });

  it('sends „Przyjął” only with cash, trimmed or null', () => {
    const base = { amount: '1 234,5', date: '2026-10-29', receivedBy: '  Anna ' };
    expect(toPaymentInput({ ...base, method: 'cash' })).toEqual({
      amount: '1234.50',
      date: '2026-10-29',
      method: 'cash',
      receivedBy: 'Anna',
    });
    expect(toPaymentInput({ ...base, method: 'transfer' }).receivedBy).toBeNull();
    expect(toPaymentInput({ ...base, method: 'cash', receivedBy: '  ' }).receivedBy).toBeNull();
  });

  it('starts a refund without a kind and trims the reason', () => {
    expect(emptyRefundForm('2026-11-05')).toEqual({
      kind: '',
      amount: '',
      date: '2026-11-05',
      method: '',
      reason: '',
    });
    expect(
      toRefundInput({
        kind: 'salesReturn',
        amount: '200',
        date: '2026-10-30',
        method: 'transfer',
        reason: '  Oddany towar ',
      }),
    ).toEqual({
      kind: 'salesReturn',
      amount: '200.00',
      date: '2026-10-30',
      method: 'transfer',
      reason: 'Oddany towar',
    });
  });

  it('fills the edit forms from entries without arithmetic', () => {
    expect(paymentFormFrom(paymentEntry({ method: 'cash', receivedBy: 'Anna' }))).toEqual({
      amount: '300,00',
      date: '2026-10-29',
      method: 'cash',
      receivedBy: 'Anna',
    });
    expect(refundFormFrom(refundEntry())).toEqual({
      kind: 'moneyReturn',
      amount: '100,00',
      date: '2026-10-30',
      method: 'blik',
      reason: 'Zwrot nadpłaty',
    });
  });
});
