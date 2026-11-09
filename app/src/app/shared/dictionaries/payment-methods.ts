/**
 * Metody płatności (stałe kody API) z etykietami: wpłaty, wydatki, operacje i historia zmian. Zwroty
 * i wypłaty używają podzbioru tych kodów (bez pobrania) z tymi samymi etykietami.
 */
export type PaymentMethodCode = 'transfer' | 'blik' | 'cash' | 'cashOnDelivery';

export const PAYMENT_METHOD_LABELS: Readonly<Record<PaymentMethodCode, string>> = {
  transfer: 'Przelew',
  blik: 'BLIK',
  cash: 'Gotówka',
  cashOnDelivery: 'Pobranie',
};
