import { parseResetLink } from './reset-link';

describe('parseResetLink', () => {
  it('reads the account and the token from the fragment', () => {
    expect(parseResetLink('konto=3f2b8c1e-0000-4000-8000-000000000001&token=abc_DEF-123')).toEqual({
      accountId: '3f2b8c1e-0000-4000-8000-000000000001',
      token: 'abc_DEF-123',
    });
  });

  it('accepts a leading # and any order, decoding the values', () => {
    expect(parseResetLink('#token=a%2Bb%3D&konto=id%201')).toEqual({
      accountId: 'id 1',
      token: 'a+b=',
    });
  });

  it('keeps padding characters inside the token', () => {
    expect(parseResetLink('konto=1&token=abc==')?.token).toBe('abc==');
  });

  it.each([
    null,
    undefined,
    '',
    'konto=1',
    'token=abc',
    'konto=&token=abc',
    'konto=1&token=',
    'konto=1&token=%E0%A4%A',
    'cos=1&inne=2',
  ])('returns null for the incomplete fragment %s', (fragment) => {
    expect(parseResetLink(fragment)).toBeNull();
  });
});
