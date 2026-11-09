/**
 * Kategorie kosztu (stały kod API, PRD §7) z etykietami. Słownik wspólny dla wydatków, operacji i
 * historii zmian (bez importów między obszarami).
 */
export type ExpenseCategory =
  | 'materialPurchase'
  | 'cutting'
  | 'hdsTransport'
  | 'marketing'
  | 'otherOperations'
  | 'vatTax'
  | 'ppeTax';

export const EXPENSE_CATEGORY_LABELS: Readonly<Record<ExpenseCategory, string>> = {
  materialPurchase: 'Zakup materiału u dostawców',
  cutting: 'Cięcie',
  hdsTransport: 'Transport HDS',
  marketing: 'Marketing',
  otherOperations: 'Pozostała działalność',
  vatTax: 'Podatek VAT',
  ppeTax: 'Podatek PPE',
};
