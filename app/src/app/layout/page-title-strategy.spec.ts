import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { TitleStrategy, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../app.routes';
import { PageTitleStrategy } from './page-title-strategy';

describe('PageTitleStrategy', () => {
  it('sets the document title to "{section} · Stone Flow"', async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), { provide: TitleStrategy, useClass: PageTitleStrategy }],
    });

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/historia-zmian');

    expect(TestBed.inject(Title).getTitle()).toBe('Historia zmian · Stone Flow');
  });
});
