import { EnvironmentProviders } from '@angular/core';
import { provideHttpClient, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { demoBackendInterceptor } from '../../demo/demo-backend';
import { unauthorizedInterceptor } from '../auth/unauthorized-interceptor';

/**
 * Klient HTTP aplikacji. API jest pod tym samym originem (`/api/*` przez proxy), więc
 * Angular dokleja nagłówek `X-XSRF-TOKEN` z ciasteczka `XSRF-TOKEN` do żądań zmieniających
 * stan. Adresy w kodzie muszą być względne: na obcy origin nagłówek nie jest wysyłany.
 */
export function provideAppHttp(options: { demo?: boolean } = {}): EnvironmentProviders {
  return provideHttpClient(
    withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
    // TRYB DEMO: demoBackendInterceptor udaje API (gałąź demo-teaser).
    withInterceptors(
      options.demo ? [unauthorizedInterceptor, demoBackendInterceptor] : [unauthorizedInterceptor],
    ),
  );
}
