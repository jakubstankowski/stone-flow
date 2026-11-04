import { NO_DATA, formatDate, formatDateTime, formatMonth, formatPln } from './pl-format';

const nbsp = ' ';

describe('formatPln', () => {
  it.each([
    [12345.67, `12${nbsp}345,67${nbsp}zł`],
    [1234.5, `1${nbsp}234,50${nbsp}zł`],
    ['1234.50', `1${nbsp}234,50${nbsp}zł`],
    [-1234.5, `-1${nbsp}234,50${nbsp}zł`],
    [1234567.89, `1${nbsp}234${nbsp}567,89${nbsp}zł`],
    [0, `0,00${nbsp}zł`],
    ['0.10', `0,10${nbsp}zł`],
  ])('formats %s as %s', (value, expected) => {
    expect(formatPln(value)).toBe(expected);
  });

  it('keeps the precision of a decimal string', () => {
    expect(formatPln('90071992547409.93')).toBe(
      `90${nbsp}071${nbsp}992${nbsp}547${nbsp}409,93${nbsp}zł`,
    );
  });

  it.each([null, undefined, '', 'abc', '1,5', Number.NaN, Number.POSITIVE_INFINITY])(
    'shows missing data for %s',
    (value) => {
      expect(formatPln(value)).toBe(NO_DATA);
    },
  );
});

describe('formatDate', () => {
  it('formats a business date', () => {
    expect(formatDate('2026-10-28')).toBe('28.10.2026');
  });

  it.each([null, undefined, '', '28.10.2026', '2026-02-30', '2026-10-28T10:00:00Z'])(
    'shows missing data for %s',
    (value) => {
      expect(formatDate(value)).toBe(NO_DATA);
    },
  );
});

describe('formatMonth', () => {
  it.each([
    ['2026-10', 'październik 2026'],
    ['2026-01-15', 'styczeń 2026'],
  ])('formats %s as %s', (value, expected) => {
    expect(formatMonth(value)).toBe(expected);
  });

  it.each([null, '', '2026-13', 'październik'])('shows missing data for %s', (value) => {
    expect(formatMonth(value)).toBe(NO_DATA);
  });
});

describe('formatDateTime', () => {
  it('presents a UTC timestamp in Europe/Warsaw summer time', () => {
    expect(formatDateTime('2026-10-24T22:30:00Z')).toBe('25.10.2026, 00:30');
  });

  it('presents a UTC timestamp in Europe/Warsaw winter time', () => {
    expect(formatDateTime('2026-10-28T13:05:00Z')).toBe('28.10.2026, 14:05');
  });

  it.each([null, '', '2026-10-28', 'nie data', '2026-10-28Tzz'])(
    'shows missing data for %s',
    (value) => {
      expect(formatDateTime(value)).toBe(NO_DATA);
    },
  );
});

describe('formatting edge cases from code review', () => {
  it('shows negative zero as zero', () => {
    expect(formatPln(-0)).toBe('0,00\u00a0zł');
    expect(formatPln('-0.00')).toBe('0,00\u00a0zł');
  });

  it('treats a timestamp without a zone as UTC', () => {
    expect(formatDateTime('2026-10-28T13:05:00')).toBe('28.10.2026, 14:05');
    expect(formatDateTime('2026-10-28T13:05:00.1234567')).toBe('28.10.2026, 14:05');
  });

  it('respects an explicit offset', () => {
    expect(formatDateTime('2026-10-28T14:05:00+01:00')).toBe('28.10.2026, 14:05');
  });
});
