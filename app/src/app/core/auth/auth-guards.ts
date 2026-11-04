import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthSession } from './auth-session';
import { DEFAULT_RETURN_URL, pathOf, safeReturnUrl } from './return-url';

/**
 * Trasy powłoki wymagają sesji; bez niej → `/logowanie?powrot={ścieżka}`. Działa także jako
 * `canActivateChild` (ta sama sygnatura). To wygoda, nie zabezpieczenie: dostęp wymusza API.
 */
export const authGuard: CanActivateFn = async (_route, state) => {
  const session = inject(AuthSession);
  const router = inject(Router);
  await session.ensureKnown();
  if (session.status() === 'authenticated') {
    return true;
  }
  const path = pathOf(state.url);
  return router.createUrlTree(['/logowanie'], {
    queryParams: safeReturnUrl(path) === path ? { powrot: path } : {},
  });
};

/** Zalogowany użytkownik nie widzi ekranu logowania. */
export const guestGuard: CanActivateFn = async () => {
  const session = inject(AuthSession);
  const router = inject(Router);
  await session.ensureKnown();
  return session.status() === 'authenticated' ? router.parseUrl(DEFAULT_RETURN_URL) : true;
};
