import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';
import { StartPage } from './features/start/start-page';

describe('app routes', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter(routes)] });
  });

  it('lazily loads the start page for the empty path', async () => {
    const startRoute = routes.find((route) => route.path === '');
    expect(startRoute?.component).toBeUndefined();
    expect(startRoute?.loadComponent).toBeDefined();

    const harness = await RouterTestingHarness.create();
    const page = await harness.navigateByUrl('', StartPage);

    expect(page).toBeInstanceOf(StartPage);
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent?.trim()).toBe('Stone Flow');
  });

  it('redirects an unknown path to the start page', async () => {
    const harness = await RouterTestingHarness.create();
    const page = await harness.navigateByUrl('/cokolwiek', StartPage);

    expect(page).toBeInstanceOf(StartPage);
    expect(TestBed.inject(Router).url).toBe('/');
  });
});
