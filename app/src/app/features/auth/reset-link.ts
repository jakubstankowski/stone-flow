import { ResetLink } from '../../core/auth/auth-session';

/**
 * Odczytuje `konto` i `token` z fragmentu linku resetu (`#konto={id}&token={token}`).
 * Brak lub niepełny fragment daje `null`. Token nie jest nigdzie logowany ani zapisywany.
 */
export function parseResetLink(fragment: string | null | undefined): ResetLink | null {
  if (!fragment) {
    return null;
  }
  const values = new Map<string, string>();
  for (const part of fragment.replace(/^#/, '').split('&')) {
    const separator = part.indexOf('=');
    if (separator <= 0) {
      continue;
    }
    const value = decode(part.slice(separator + 1));
    if (value) {
      values.set(part.slice(0, separator), value);
    }
  }
  const accountId = values.get('konto');
  const token = values.get('token');
  return accountId && token ? { accountId, token } : null;
}

function decode(value: string): string | null {
  try {
    return decodeURIComponent(value);
  } catch {
    return null;
  }
}
