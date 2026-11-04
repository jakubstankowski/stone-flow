import { HttpErrorResponse, HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { delay, of, throwError } from 'rxjs';

/**
 * TRYB DEMO (gałąź `demo-teaser`): udaje endpointy `/api/auth/*` w przeglądarce, więc aplikację
 * uruchamia samo `npm start` — bez API i bazy. Dowolny e-mail i niepuste hasło logują.
 * Sesja trwa do zamknięcia karty (sessionStorage).
 */
const SESSION_KEY = 'stone-flow-demo-session';
const LATENCY_MS = 300;

export const demoBackendInterceptor: HttpInterceptorFn = (request, next) => {
  const path = request.url.split('?', 1)[0];
  if (!path.startsWith('/api/')) {
    return next(request);
  }

  const respond = <T>(status: number, body: T | null = null) =>
    of(new HttpResponse<T>({ status, body, url: request.url })).pipe(delay(LATENCY_MS));
  const fail = (status: number) =>
    throwError(() => new HttpErrorResponse({ status, url: request.url })).pipe(delay(LATENCY_MS));

  switch (`${request.method} ${path}`) {
    case 'GET /api/auth/csrf':
      return respond(204);
    case 'POST /api/auth/login': {
      const body = request.body as { email?: string; password?: string } | null;
      if (!body?.email?.trim() || !body.password) {
        return fail(401);
      }
      sessionStorage.setItem(SESSION_KEY, body.email.trim());
      return respond(204);
    }
    case 'GET /api/auth/session': {
      const email = sessionStorage.getItem(SESSION_KEY);
      return email ? respond(200, { email }) : fail(401);
    }
    case 'POST /api/auth/logout':
      sessionStorage.removeItem(SESSION_KEY);
      return respond(204);
    case 'POST /api/auth/password-reset/request':
    case 'POST /api/auth/password-reset/confirm':
      return respond(204);
    default:
      return fail(404);
  }
};
