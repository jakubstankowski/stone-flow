import { pathOf, safeReturnUrl } from './return-url';

describe('safeReturnUrl', () => {
  it.each(['/zamowienia', '/zamowienia/12', '/historia-zmian', '/pulpit'])(
    'keeps the internal section path %s',
    (path) => {
      expect(safeReturnUrl(path)).toBe(path);
    },
  );

  it.each([
    '//evil.com',
    'https://evil.com',
    '/\\evil.com',
    '/logowanie',
    '/reset-hasla',
    '/reset-hasla/nowe',
    '/nieznana',
    '/pulpit?x=1',
    '/pulpit#x',
    'zamowienia',
    '',
    null,
    undefined,
    'javascript:alert(1)',
  ])('replaces %s with the dashboard', (raw) => {
    expect(safeReturnUrl(raw)).toBe('/pulpit');
  });
});

describe('pathOf', () => {
  it('drops the query and the fragment', () => {
    expect(pathOf('/wydatki?strona=2#lista')).toBe('/wydatki');
    expect(pathOf('/operacje')).toBe('/operacje');
  });
});
