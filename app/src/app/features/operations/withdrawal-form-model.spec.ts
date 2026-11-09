import { NETWORK_ERROR_MESSAGE } from '../../core/http/api-error';
import { withdrawalDetails } from '../../../testing/operations-testing';
import {
  WITHDRAWAL_MESSAGES,
  amountError,
  emptyWithdrawalForm,
  methodError,
  personError,
  toWithdrawalInput,
  withdrawalErrorMessage,
  withdrawalFormFrom,
  withdrawnOnError,
} from './withdrawal-form-model';

describe('withdrawal form model', () => {
  it('requires the person and limits it to 100 characters after trimming', () => {
    expect(personError('  ')).toBe(WITHDRAWAL_MESSAGES.personRequired);
    expect(personError(` ${'a'.repeat(100)} `)).toBeNull();
    expect(personError('a'.repeat(101))).toBe(WITHDRAWAL_MESSAGES.personTooLong);
  });

  it.each([
    ['', WITHDRAWAL_MESSAGES.amountRequired],
    ['0', WITHDRAWAL_MESSAGES.amountNotPositive],
    ['0,00', WITHDRAWAL_MESSAGES.amountNotPositive],
    ['-200', WITHDRAWAL_MESSAGES.amountNotPositive],
    ['−0,01', WITHDRAWAL_MESSAGES.amountNotPositive],
    ['1,234', WITHDRAWAL_MESSAGES.amountScale],
    ['10.005', WITHDRAWAL_MESSAGES.amountScale],
    ['abc', WITHDRAWAL_MESSAGES.amountInvalid],
    ['12345678901234567', WITHDRAWAL_MESSAGES.amountTooLarge],
  ])('rejects the amount %j', (text, expected) => {
    expect(amountError(text)).toBe(expected);
  });

  it.each(['0,01', '2 000,00', '2000.5', '1234567890123456'])('accepts the amount %s', (text) => {
    expect(amountError(text)).toBeNull();
  });

  it('requires a withdrawal date not later than today', () => {
    expect(withdrawnOnError('', '2026-11-05')).toBe(WITHDRAWAL_MESSAGES.dateRequired);
    expect(withdrawnOnError('05.11.2026', '2026-11-05')).toBe(WITHDRAWAL_MESSAGES.dateInvalid);
    expect(withdrawnOnError('2026-11-06', '2026-11-05')).toBe(WITHDRAWAL_MESSAGES.dateInFuture);
    expect(withdrawnOnError('2026-11-05', '2026-11-05')).toBeNull();
    expect(withdrawnOnError('2025-01-01', '2026-11-05')).toBeNull();
  });

  it('requires a method', () => {
    expect(methodError('')).toBe(WITHDRAWAL_MESSAGES.methodRequired);
    expect(methodError('blik')).toBeNull();
  });

  it('starts with today and no method', () => {
    expect(emptyWithdrawalForm('2026-11-05')).toEqual({
      person: '',
      amount: '',
      withdrawnOn: '2026-11-05',
      method: '',
    });
  });

  it('maps the form to the API input with a trimmed person and a canonical amount', () => {
    expect(
      toWithdrawalInput({
        person: '  Właściciel A ',
        amount: '2 000,5',
        withdrawnOn: '2026-11-05',
        method: 'cash',
      }),
    ).toEqual({
      person: 'Właściciel A',
      amount: '2000.50',
      withdrawnOn: '2026-11-05',
      method: 'cash',
    });
  });

  it('reads details into the form without arithmetic', () => {
    expect(withdrawalFormFrom(withdrawalDetails({ amount: '1234.50' }))).toEqual({
      person: 'Właściciel A',
      amount: '1234,50',
      withdrawnOn: '2026-11-05',
      method: 'transfer',
    });
  });

  it('chooses the general error message', () => {
    const fallback = WITHDRAWAL_MESSAGES.saveFailed;
    expect(withdrawalErrorMessage({ kind: 'network' }, fallback)).toBe(NETWORK_ERROR_MESSAGE);
    expect(
      withdrawalErrorMessage(
        { kind: 'problem', status: 400, detail: 'Token', code: 'csrf_invalid' },
        fallback,
      ),
    ).toBe(WITHDRAWAL_MESSAGES.csrf);
    expect(
      withdrawalErrorMessage(
        { kind: 'problem', status: 422, detail: 'Opis z API.', code: null },
        fallback,
      ),
    ).toBe('Opis z API.');
    expect(
      withdrawalErrorMessage({ kind: 'problem', status: 403, detail: null, code: null }, fallback),
    ).toBe(fallback);
  });
});
