import { changeHistoryItem } from '../../../testing/change-history-testing';
import { NO_DATA, formatPln } from '../../shared/format/pl-format';
import {
  AUDIT_FIELD_CATALOG,
  changeSummary,
  fieldLabel,
  formatFieldValue,
} from './audit-field-catalog';
import { AuditObjectTypeCode, OBJECT_TYPES, objectLink } from './change-history-models';

describe('AUDIT_FIELD_CATALOG', () => {
  it('describes every object type', () => {
    expect(Object.keys(AUDIT_FIELD_CATALOG).sort()).toEqual([...OBJECT_TYPES].sort());
  });

  it.each<[AuditObjectTypeCode, string, string]>([
    ['order', 'orderDate', 'Data zamówienia'],
    ['order', 'value', 'Wartość brutto'],
    ['order', 'customerName', 'Imię i nazwisko lub nazwa firmy'],
    ['order', 'zoReferences', 'Referencje ZO'],
    ['order', 'shopRefundedAmount', 'Kwota zwrócona w sklepie'],
    ['payment', 'receivedBy', 'Przyjął'],
    ['refund', 'kind', 'Rodzaj zwrotu'],
    ['salesCorrection', 'date', 'Data korekty'],
    ['expense', 'costDate', 'Data kosztu'],
    ['expense', 'paidOn', 'Data zapłaty'],
    ['recurringExpenseDefinition', 'dayOfMonth', 'Dzień miesiąca'],
    ['recurringExpenseDefinition', 'pausedOn', 'Data wstrzymania'],
    ['ownerWithdrawal', 'withdrawnOn', 'Data wypłaty'],
  ])('labels %s.%s as %s', (type, field, label) => {
    expect(fieldLabel(type, field)).toBe(label);
  });

  it('shows the field name of a field unknown to the catalog', () => {
    expect(fieldLabel('expense', 'futureField')).toBe('futureField');
    expect(fieldLabel('expense', 'toString')).toBe('toString');
  });
});

describe('formatFieldValue', () => {
  it('formats amounts from the audit text without arithmetic', () => {
    expect(formatFieldValue('expense', 'grossAmount', '1234.50')).toBe(formatPln('1234.50'));
    expect(formatFieldValue('expense', 'grossAmount', '1234.50')).toBe('1 234,50 zł');
    expect(formatFieldValue('order', 'value', '-50.00')).toBe(formatPln('-50.00'));
  });

  it('formats dates as dd.MM.yyyy', () => {
    expect(formatFieldValue('payment', 'date', '2026-11-04')).toBe('04.11.2026');
    expect(formatFieldValue('recurringExpenseDefinition', 'startsOn', '2026-01-31')).toBe(
      '31.01.2026',
    );
  });

  it.each<[AuditObjectTypeCode, string, string, string]>([
    ['payment', 'method', 'cashOnDelivery', 'Pobranie'],
    ['refund', 'method', 'blik', 'BLIK'],
    ['ownerWithdrawal', 'method', 'cash', 'Gotówka'],
    ['expense', 'paymentMethod', 'transfer', 'Przelew'],
    ['refund', 'kind', 'salesReturn', 'Zwrot sprzedaży'],
    ['expense', 'category', 'cutting', 'Cięcie'],
    ['recurringExpenseDefinition', 'category', 'marketing', 'Marketing'],
    ['order', 'channel', 'olx', 'OLX'],
    ['order', 'fulfillmentStatus', 'problem', 'Problem / reklamacja'],
    ['order', 'customerKind', 'company', 'Firma'],
    ['order', 'shopStatus', 'on-hold', 'Wstrzymane'],
  ])('labels the %s.%s code %s as %s', (type, field, code, label) => {
    expect(formatFieldValue(type, field, code)).toBe(label);
  });

  it('shows an unknown dictionary code unchanged', () => {
    expect(formatFieldValue('expense', 'category', 'fuel')).toBe('fuel');
    expect(formatFieldValue('order', 'shopStatus', 'wc-custom')).toBe('wc-custom');
    expect(formatFieldValue('payment', 'method', 'toString')).toBe('toString');
  });

  it('shows booleans as tak/nie', () => {
    expect(formatFieldValue('order', 'hasPhotos', 'true')).toBe('tak');
    expect(formatFieldValue('order', 'shopDeleted', 'false')).toBe('nie');
    expect(formatFieldValue('order', 'hasReview', 'maybe')).toBe('maybe');
  });

  it('joins list values and keeps an unreadable list raw', () => {
    expect(formatFieldValue('order', 'suppliers', '["A","B"]')).toBe('A, B');
    expect(formatFieldValue('order', 'zoReferences', '[]')).toBe('');
    expect(formatFieldValue('order', 'suppliers', 'not json')).toBe('not json');
    expect(formatFieldValue('order', 'suppliers', '[1,2]')).toBe('[1,2]');
    expect(formatFieldValue('order', 'suppliers', '{"a":1}')).toBe('{"a":1}');
  });

  it('keeps text and numbers unchanged', () => {
    expect(formatFieldValue('expense', 'description', 'Cięcie płyt')).toBe('Cięcie płyt');
    expect(formatFieldValue('recurringExpenseDefinition', 'dayOfMonth', '15')).toBe('15');
  });

  it('shows a missing value as Brak danych', () => {
    expect(formatFieldValue('expense', 'paidOn', null)).toBe(NO_DATA);
    expect(formatFieldValue('expense', 'futureField', null)).toBe(NO_DATA);
  });

  it('shows the raw value of a field unknown to the catalog without throwing', () => {
    expect(formatFieldValue('order', 'futureField', '1234.50')).toBe('1234.50');
    expect(formatFieldValue('order', '__proto__', 'x')).toBe('x');
  });
});

describe('changeSummary', () => {
  const changes = [
    { field: 'grossAmount', oldValue: '100.00', newValue: '120.50', isPersonalData: false },
    { field: 'category', oldValue: 'cutting', newValue: 'marketing', isPersonalData: false },
    { field: 'comment', oldValue: null, newValue: 'Uwaga', isPersonalData: false },
    { field: 'payer', oldValue: 'A', newValue: 'B', isPersonalData: true },
  ];

  it('shows the first two changes as label: was → is and counts the rest', () => {
    expect(changeSummary(changeHistoryItem({ operation: 'updated', changes }))).toEqual({
      lines: [
        `Kwota brutto: ${formatPln('100.00')} → ${formatPln('120.50')}`,
        'Kategoria: Cięcie → Marketing',
      ],
      more: 2,
    });
  });

  it('shows only the new value of a creation', () => {
    const summary = changeSummary(
      changeHistoryItem({
        operation: 'created',
        changes: [
          { field: 'description', oldValue: null, newValue: 'Cięcie płyt', isPersonalData: false },
        ],
      }),
    );
    expect(summary).toEqual({ lines: ['Opis: Cięcie płyt'], more: 0 });
  });

  it('shows only the last value of a deletion', () => {
    const summary = changeSummary(
      changeHistoryItem({
        operation: 'deleted',
        changes: [
          { field: 'grossAmount', oldValue: '1234.50', newValue: null, isPersonalData: false },
          { field: 'paidOn', oldValue: null, newValue: null, isPersonalData: false },
          { field: 'payer', oldValue: 'Właściciel', newValue: null, isPersonalData: true },
        ],
      }),
    );
    expect(summary).toEqual({
      lines: [`Kwota brutto: ${formatPln('1234.50')}`, `Data zapłaty: ${NO_DATA}`],
      more: 1,
    });
  });
});

describe('objectLink', () => {
  it.each<[AuditObjectTypeCode, string[]]>([
    ['order', ['/zamowienia', 'x1']],
    ['expense', ['/wydatki', 'x1']],
    ['recurringExpenseDefinition', ['/wydatki', 'cykliczne', 'x1']],
    ['ownerWithdrawal', ['/operacje', 'wyplaty', 'x1']],
  ])('links a %s to its page', (type, link) => {
    expect(objectLink(changeHistoryItem({ object: { type, id: 'x1' } }))).toEqual(link);
  });

  it.each<AuditObjectTypeCode>(['payment', 'refund', 'salesCorrection'])(
    'links a %s to the parent order',
    (type) => {
      const item = changeHistoryItem({
        object: { type, id: 'x1', parent: { type: 'order', id: 'o1' } },
      });
      expect(objectLink(item)).toEqual(['/zamowienia', 'o1']);
    },
  );

  it('has no link for a deleted object', () => {
    expect(
      objectLink(changeHistoryItem({ object: { type: 'expense', deleted: true } })),
    ).toBeNull();
    const payment = changeHistoryItem({
      object: { type: 'payment', deleted: true, parent: { type: 'order', id: 'o1' } },
    });
    expect(objectLink(payment)).toBeNull();
  });

  it('has no link for an entry without the expected parent', () => {
    expect(objectLink(changeHistoryItem({ object: { type: 'refund', parent: null } }))).toBeNull();
  });

  describe('anonymization entries (SF-22)', () => {
    it('labels the operation fields of an order and of its entries', () => {
      expect(fieldLabel('order', 'anonymizedFields')).toBe('Zanonimizowane pola');
      expect(fieldLabel('order', 'anonymizedReasons')).toBe(
        'Zanonimizowane powody zwrotów i korekt',
      );
      expect(fieldLabel('order', 'retentionStartDate')).toBe('Początek okresu retencji');
      expect(fieldLabel('order', 'retentionMonths')).toBe('Okres retencji (miesiące)');
      expect(fieldLabel('refund', 'anonymizedFields')).toBe('Zanonimizowane pola');
      expect(fieldLabel('salesCorrection', 'anonymizedFields')).toBe('Zanonimizowane pola');
    });

    it('shows anonymized field codes as field labels', () => {
      expect(
        formatFieldValue('order', 'anonymizedFields', '["customerName","customerEmail","notes"]'),
      ).toBe('Imię i nazwisko lub nazwa firmy, E-mail, Uwagi do realizacji');
      expect(formatFieldValue('refund', 'anonymizedFields', '["reason"]')).toBe('Powód');
      expect(formatFieldValue('order', 'anonymizedFields', '[]')).toBe(NO_DATA);
      expect(formatFieldValue('order', 'anonymizedFields', '["futureField"]')).toBe('futureField');
      expect(formatFieldValue('order', 'anonymizedFields', 'nie-json')).toBe('nie-json');
      expect(formatFieldValue('order', 'retentionStartDate', '2021-03-15')).toBe('15.03.2021');
      expect(formatFieldValue('order', 'retentionMonths', '60')).toBe('60');
      expect(formatFieldValue('order', 'customerName', '[zanonimizowano]')).toBe(
        '[zanonimizowano]',
      );
    });

    it('summarizes an anonymization with the new values only', () => {
      const summary = changeSummary(
        changeHistoryItem({
          operation: 'anonymized',
          object: { type: 'order' },
          changes: [
            {
              field: 'anonymizedFields',
              oldValue: null,
              newValue: '["customerName"]',
              isPersonalData: false,
            },
            { field: 'anonymizedReasons', oldValue: null, newValue: '2', isPersonalData: false },
          ],
        }),
      );

      expect(summary.lines).toEqual([
        'Zanonimizowane pola: Imię i nazwisko lub nazwa firmy',
        'Zanonimizowane powody zwrotów i korekt: 2',
      ]);
    });
  });
});
