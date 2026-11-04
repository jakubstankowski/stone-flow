import { NAV_ITEMS } from '../../layout/nav-items';

export const DEFAULT_RETURN_URL = '/pulpit';

const INTERNAL_PATH = /^\/[a-z0-9-]+(\/[a-z0-9-]+)*$/;

/**
 * Parametr `powrot` po zalogowaniu: tylko ścieżka wewnętrzna sekcji aplikacji (bez query,
 * fragmentu, `//…` i `/\…`). Każda inna wartość prowadzi na pulpit.
 */
export function safeReturnUrl(raw: string | null | undefined): string {
  if (!raw || !INTERNAL_PATH.test(raw)) {
    return DEFAULT_RETURN_URL;
  }
  const section = raw.split('/')[1];
  return NAV_ITEMS.some((item) => item.path === section) ? raw : DEFAULT_RETURN_URL;
}

/** Ścieżka adresu bez query i fragmentu. */
export function pathOf(url: string): string {
  return url.split(/[?#]/, 1)[0];
}
