import { shopStatusLabel } from './shop-status';

describe('shopStatusLabel', () => {
  it.each([
    ['pending', 'Oczekuje na płatność'],
    ['processing', 'W realizacji'],
    ['on-hold', 'Wstrzymane'],
    ['completed', 'Zrealizowane'],
    ['cancelled', 'Anulowane w sklepie'],
    ['refunded', 'Zwrócone'],
    ['failed', 'Nieudane'],
    ['trash', 'W koszu'],
    ['checkout-draft', 'Szkic'],
  ])('labels %s as %s', (code, label) => {
    expect(shopStatusLabel(code)).toBe(label);
  });

  it('returns an unknown code unchanged', () => {
    expect(shopStatusLabel('wc-custom-shipped')).toBe('wc-custom-shipped');
    expect(shopStatusLabel('toString')).toBe('toString');
  });
});
