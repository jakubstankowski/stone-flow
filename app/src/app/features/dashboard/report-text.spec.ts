import { reportPeriod } from '../../../testing/dashboard-testing';
import { formatPln } from '../../shared/format/pl-format';
import {
  amountComparisonText,
  baseLabel,
  basisLabel,
  beforeLaunchMessage,
  bucketLabel,
  bucketRange,
  comparisonDescription,
  countComparisonText,
  launchNotice,
  periodLabel,
} from './report-text';

const october = { from: '2026-10-01', to: '2026-10-31', available: true, reason: null } as const;

describe('report text', () => {
  it('labels months, years and custom ranges', () => {
    expect(periodLabel(reportPeriod())).toBe('listopad 2026, 01.11–30.11.2026');
    expect(periodLabel(reportPeriod({ kind: 'year', from: '2026-01-01', to: '2026-11-09' }))).toBe(
      '2026, 01.01–09.11.2026',
    );
    expect(
      periodLabel(reportPeriod({ kind: 'custom', from: '2026-09-01', to: '2026-11-15' })),
    ).toBe('01.09.2026–15.11.2026');
  });

  it('tells about the launch date only when the period was cut', () => {
    expect(launchNotice(reportPeriod())).toBeNull();
    expect(
      launchNotice(reportPeriod({ effectiveFrom: '2026-11-03', launchDate: '2026-11-03' })),
    ).toBe('Raport liczony od 03.11.2026 (data uruchomienia).');
    expect(
      launchNotice(
        reportPeriod({ effectiveFrom: '2026-11-03', launchDate: '2026-11-03', beforeLaunch: true }),
      ),
    ).toBeNull();
    expect(beforeLaunchMessage(reportPeriod({ launchDate: '2026-11-03' }))).toBe(
      'Wybrany okres jest w całości przed datą uruchomienia (03.11.2026). Raporty obejmują dane od tej daty.',
    );
    expect(basisLabel('2026-11-03')).toBe(
      'Kwoty brutto w PLN. Raporty obejmują dane od 03.11.2026.',
    );
  });

  it('describes the comparison of a month in progress, a year in progress and other periods', () => {
    expect(comparisonDescription(reportPeriod(), october)).toBe(
      'Miesiąc w toku — porównanie z całym poprzednim miesiącem (październik 2026).',
    );
    expect(
      comparisonDescription(reportPeriod({ kind: 'year', from: '2026-01-01', to: '2026-11-09' }), {
        from: '2025-01-01',
        to: '2025-11-09',
        available: true,
        reason: null,
      }),
    ).toBe('Rok w toku (dane do 09.11.2026) — porównanie z 01.01–09.11 2025.');
    expect(
      comparisonDescription(reportPeriod({ kind: 'previousMonth', inProgress: false }), october),
    ).toBe('Porównanie z: październik 2026');
    expect(
      comparisonDescription(
        reportPeriod({ kind: 'year', from: '2025-01-01', to: '2025-12-31', inProgress: false }),
        { from: '2024-01-01', to: '2024-12-31', available: true, reason: null },
      ),
    ).toBe('Porównanie z: 2024');
    expect(
      baseLabel(reportPeriod({ kind: 'custom', inProgress: false }), {
        from: '2026-10-22',
        to: '2026-10-31',
        available: true,
        reason: null,
      }),
    ).toBe('22.10.2026–31.10.2026');
  });

  it('writes the card comparison with an arrow and a word, never with a computed value', () => {
    expect(
      amountComparisonText(
        { status: 'available', base: '1000.00', difference: '1234.00', percent: '12.5' },
        'październik 2026',
      ),
    ).toEqual({
      arrow: '↑',
      text: `+12,5% (+${formatPln('1234.00')}) więcej niż w: październik 2026`,
    });
    expect(
      amountComparisonText(
        { status: 'available', base: '1000.00', difference: '-50.00', percent: '-3.0' },
        'październik 2026',
      ),
    ).toEqual({ arrow: '↓', text: `-3,0% (${formatPln('-50.00')}) mniej niż w: październik 2026` });
    expect(
      amountComparisonText(
        { status: 'available', base: '1000.00', difference: '0.00', percent: '0.0' },
        'październik 2026',
      ),
    ).toEqual({
      arrow: '→',
      text: `0,0% (${formatPln('0.00')}) bez zmian względem: październik 2026`,
    });
    expect(
      amountComparisonText({ status: 'noBasis', base: null, difference: null, percent: null }, 'x'),
    ).toEqual({ arrow: null, text: 'brak podstawy porównania' });
  });

  it('writes the order count comparison in pieces', () => {
    expect(
      countComparisonText({ status: 'available', base: 8, difference: 3, percent: '37.5' }, 'X'),
    ).toEqual({ arrow: '↑', text: '+37,5% (+3 zam.) więcej niż w: X' });
    expect(
      countComparisonText({ status: 'available', base: 3, difference: -1, percent: '-33.3' }, 'X'),
    ).toEqual({ arrow: '↓', text: '-33,3% (-1 zam.) mniej niż w: X' });
    expect(
      countComparisonText({ status: 'noBasis', base: null, difference: null, percent: null }, 'X'),
    ).toEqual({ arrow: null, text: 'brak podstawy porównania' });
  });

  it('labels chart buckets by day or month', () => {
    expect(bucketLabel({ from: '2026-11-01' }, 'day')).toBe('01.11');
    expect(bucketLabel({ from: '2026-01-15' }, 'month')).toBe('sty 2026');
    expect(bucketRange({ from: '2026-11-01', to: '2026-11-01' })).toBe('01.11.2026');
    expect(bucketRange({ from: '2026-02-10', to: '2026-02-28' })).toBe('10.02.2026–28.02.2026');
  });
});
