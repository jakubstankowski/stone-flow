/**
 * Polskie etykiety statusów zamówienia WooCommerce (SF-12, spec „Lista zamówień”). Kod przychodzi
 * z API bez zmian; nieznany kod (np. status z wtyczki sklepu) pokazujemy surowo.
 */
const SHOP_STATUS_LABELS: Readonly<Record<string, string>> = {
  pending: 'Oczekuje na płatność',
  processing: 'W realizacji',
  'on-hold': 'Wstrzymane',
  completed: 'Zrealizowane',
  cancelled: 'Anulowane w sklepie',
  refunded: 'Zwrócone',
  failed: 'Nieudane',
  trash: 'W koszu',
  'checkout-draft': 'Szkic',
};

export function shopStatusLabel(code: string): string {
  return Object.hasOwn(SHOP_STATUS_LABELS, code) ? SHOP_STATUS_LABELS[code] : code;
}
