import * as models from '../../features/operations/operation-models';
import {
  DIRECTION_LABELS,
  isNegativeAmount,
  KIND_FILTER_LABELS,
  kindLabel,
  NEGATIVE_AMOUNT_LABEL,
  OPERATION_KINDS,
  sourceLink,
} from './operations';

describe('słowniki operacji pieniężnych', () => {
  it('keeps the fixed kind order', () => {
    expect(OPERATION_KINDS).toEqual(['payment', 'refund', 'expense', 'withdrawal']);
  });

  it('labels a refund with its own kind and other kinds with the filter label', () => {
    expect(kindLabel({ kind: 'refund', refundKind: 'salesReturn' })).toBe('Zwrot sprzedaży');
    expect(kindLabel({ kind: 'refund', refundKind: 'moneyReturn' })).toBe('Zwrot pieniędzy');
    expect(kindLabel({ kind: 'refund', refundKind: null })).toBe('Zwrot');
    expect(kindLabel({ kind: 'payment', refundKind: null })).toBe('Wpłata');
    expect(kindLabel({ kind: 'expense', refundKind: null })).toBe('Opłacony wydatek');
    expect(kindLabel({ kind: 'withdrawal', refundKind: null })).toBe('Wypłata właściciela');
  });

  it('labels directions as text', () => {
    expect(DIRECTION_LABELS).toEqual({ inflow: 'Wpływ', outflow: 'Wypływ' });
  });

  it('detects a negative amount from the text', () => {
    expect(isNegativeAmount('-50.00')).toBe(true);
    expect(isNegativeAmount(' -0.01')).toBe(true);
    expect(isNegativeAmount('0.00')).toBe(false);
    expect(isNegativeAmount('50.00')).toBe(false);
    expect(isNegativeAmount(null)).toBe(false);
    expect(isNegativeAmount(undefined)).toBe(false);
  });

  it('links each source type to its page', () => {
    expect(sourceLink({ sourceType: 'order', sourceId: 'o1' })).toEqual(['/zamowienia', 'o1']);
    expect(sourceLink({ sourceType: 'expense', sourceId: 'e1' })).toEqual(['/wydatki', 'e1']);
    expect(sourceLink({ sourceType: 'withdrawal', sourceId: 'w1' })).toEqual([
      '/operacje',
      'wyplaty',
      'w1',
    ]);
  });

  it('is re-exported unchanged by the operations area', () => {
    expect(models.OPERATION_KINDS).toBe(OPERATION_KINDS);
    expect(models.KIND_FILTER_LABELS).toBe(KIND_FILTER_LABELS);
    expect(models.DIRECTION_LABELS).toBe(DIRECTION_LABELS);
    expect(models.NEGATIVE_AMOUNT_LABEL).toBe(NEGATIVE_AMOUNT_LABEL);
    expect(models.kindLabel).toBe(kindLabel);
    expect(models.isNegativeAmount).toBe(isNegativeAmount);
    expect(models.sourceLink).toBe(sourceLink);
  });
});
