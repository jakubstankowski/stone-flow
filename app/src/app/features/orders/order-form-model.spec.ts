import { orderDetails } from '../../../testing/orders-testing';
import {
  ORDER_MESSAGES,
  amountError,
  emptyOrderForm,
  isValidPostalCode,
  isValidTaxId,
  orderFormFrom,
  todayInWarsaw,
  toOrderInput,
} from './order-form-model';

describe('order form model', () => {
  it.each(['1234563218', '123-456-32-18', '123 456 32 18', ''])('accepts the tax ID %s', (nip) => {
    expect(isValidTaxId(nip)).toBe(true);
  });

  it.each(['1234567890', '123456321', '12345632189', 'PL1234563218', 'abcdefghij'])(
    'rejects the tax ID %s',
    (nip) => {
      expect(isValidTaxId(nip)).toBe(false);
    },
  );

  it('validates the postal code only when given', () => {
    expect(isValidPostalCode('')).toBe(true);
    expect(isValidPostalCode(' 00-001 ')).toBe(true);
    expect(isValidPostalCode('00001')).toBe(false);
  });

  it.each([
    ['', ORDER_MESSAGES.valueRequired],
    ['-1', ORDER_MESSAGES.valueInvalid],
    ['1,234', ORDER_MESSAGES.valueInvalid],
    ['abc', ORDER_MESSAGES.valueInvalid],
    ['10000000000000000', ORDER_MESSAGES.valueTooLarge],
    ['0', null],
    ['9 999 999 999 999 999,99', null],
  ])('checks the amount %s', (text, message) => {
    expect(amountError(text)).toBe(message);
  });

  it('gives today in Europe/Warsaw', () => {
    expect(todayInWarsaw(new Date('2026-10-31T23:30:00Z'))).toBe('2026-11-01');
  });

  it('round-trips server data without changing it', () => {
    const order = orderDetails();
    const input = toOrderInput(orderFormFrom(order));

    expect(input).toEqual({
      orderDate: order.orderDate,
      channel: order.channel,
      value: order.value,
      customer: order.customer,
      fulfillmentStatus: order.fulfillmentStatus,
      operational: order.operational,
      comment: order.comment,
    });
  });

  it('starts a new order as „W trakcie” for a person', () => {
    const model = emptyOrderForm('2026-10-28');
    expect(model.fulfillmentStatus).toBe('inProgress');
    expect(model.customer.kind).toBe('person');
    expect(model.orderDate).toBe('2026-10-28');
  });
});
