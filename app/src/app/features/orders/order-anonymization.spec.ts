import { formatDateTime } from '../../shared/format/pl-format';
import { anonymizationText } from './order-anonymization';
import { OrderAnonymization } from './order-models';

function state(overrides: Partial<OrderAnonymization>): OrderAnonymization {
  return {
    status: 'disabled',
    retentionStartDate: null,
    availableFrom: null,
    anonymizedAtUtc: null,
    ...overrides,
  };
}

describe('anonymizationText', () => {
  it('describes the available action with the first day of availability', () => {
    expect(
      anonymizationText(
        state({
          status: 'available',
          retentionStartDate: '2021-03-15',
          availableFrom: '2026-03-15',
        }),
      ),
    ).toBe(
      'Okres retencji upłynął 15.03.2026. Anonimizacja usunie dane klienta, uwagi, komentarz i powody zwrotów i korekt.',
    );
  });

  it('describes the pending retention with both dates', () => {
    expect(
      anonymizationText(
        state({
          status: 'retentionPending',
          retentionStartDate: '2026-11-05',
          availableFrom: '2026-12-05',
        }),
      ),
    ).toBe(
      'Dostępne po upływie okresu retencji, od 05.12.2026. Okres liczy się od 05.11.2026 — najpóźniejszej z dat zamówienia, jego wpisów i ostatniej zmiany.',
    );
  });

  it('describes the disabled feature', () => {
    expect(anonymizationText(state({ status: 'disabled' }))).toBe(
      'Funkcja wyłączona do decyzji o okresie retencji.',
    );
  });

  it('describes an anonymized order with the Warsaw time', () => {
    const at = '2026-11-13T10:15:00Z';

    expect(anonymizationText(state({ status: 'anonymized', anonymizedAtUtc: at }))).toBe(
      `Dane klienta zanonimizowano ${formatDateTime(at)} (Europe/Warsaw). Operacji nie można cofnąć.`,
    );
    expect(formatDateTime(at)).toBe('13.11.2026, 11:15');
  });

  it('shows "Brak danych" instead of a missing date without throwing', () => {
    expect(anonymizationText(state({ status: 'retentionPending' }))).toContain('od Brak danych');
    expect(anonymizationText(state({ status: 'anonymized' }))).toContain(
      'zanonimizowano Brak danych',
    );
  });
});
