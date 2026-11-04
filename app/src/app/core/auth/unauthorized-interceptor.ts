import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthSession } from './auth-session';

const LOGIN_URL = '/api/auth/login';

/**
 * Każde `401` z API (poza samym logowaniem, gdzie oznacza błędne dane) kończy sesję w
 * interfejsie. Błąd jest zawsze przekazywany dalej.
 */
export const unauthorizedInterceptor: HttpInterceptorFn = (request, next) => {
  const session = inject(AuthSession);
  return next(request).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        request.url.split('?', 1)[0] !== LOGIN_URL
      ) {
        session.expire();
      }
      return throwError(() => error);
    }),
  );
};
