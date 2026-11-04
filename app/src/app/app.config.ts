import { registerLocaleData } from '@angular/common';
import localePl from '@angular/common/locales/pl';
import { ApplicationConfig, LOCALE_ID, provideBrowserGlobalErrorListeners } from '@angular/core';
import { TitleStrategy, provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { provideAppHttp } from './core/http/app-http';
import { PageTitleStrategy } from './layout/page-title-strategy';

registerLocaleData(localePl, 'pl-PL');

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideAppHttp(),
    { provide: LOCALE_ID, useValue: 'pl-PL' },
    { provide: TitleStrategy, useClass: PageTitleStrategy },
  ],
};
