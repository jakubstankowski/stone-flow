/** Sekcje głównej nawigacji (design guide, sekcja 3) — jedno źródło dla tras i menu. */
export interface NavItem {
  readonly label: string;
  readonly path: string;
}

export const NAV_ITEMS: readonly NavItem[] = [
  { label: 'Pulpit', path: 'pulpit' },
  { label: 'Zamówienia', path: 'zamowienia' },
  { label: 'Wydatki', path: 'wydatki' },
  { label: 'Operacje', path: 'operacje' },
  { label: 'Historia zmian', path: 'historia-zmian' },
  { label: 'Integracja', path: 'integracja' },
];

export const PRODUCT_NAME = 'Stone Flow';
