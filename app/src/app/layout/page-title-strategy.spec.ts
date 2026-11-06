import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { TitleStrategy, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTestHttp, signIn, signOut } from '../../testing/auth-testing';
import { routes } from '../app.routes';
import { PageTitleStrategy } from './page-title-strategy';
import { discardIntegrationRequests } from '../../testing/integration-testing';

describe('PageTitleStrategy', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideTestHttp(),
        { provide: TitleStrategy, useClass: PageTitleStrategy },
      ],
    });
  });

  afterEach(() => {
    discardIntegrationRequests();
    TestBed.inject(HttpTestingController).verify();
  });

  it('sets the document title to "{section} · Stone Flow"', async () => {
    await signIn();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/historia-zmian');

    expect(TestBed.inject(Title).getTitle()).toBe('Historia zmian · Stone Flow');
  });

  it('names the login screen', async () => {
    await signOut();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/logowanie');

    expect(TestBed.inject(Title).getTitle()).toBe('Logowanie · Stone Flow');
  });
});
