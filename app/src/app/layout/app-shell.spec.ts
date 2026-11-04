import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../app.routes';
import { AppShell } from './app-shell';

describe('AppShell', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [AppShell], providers: [provideRouter(routes)] });
  });

  async function render(url: string) {
    await RouterTestingHarness.create(url);
    const fixture = TestBed.createComponent(AppShell);
    await fixture.whenStable();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('starts with a skip link to the focusable main content', async () => {
    const { element } = await render('/pulpit');
    const firstFocusable = element.querySelector('a, button') as HTMLAnchorElement;
    const main = element.querySelector('main') as HTMLElement;

    expect(firstFocusable.textContent?.trim()).toBe('Przejdź do treści');
    expect(firstFocusable.getAttribute('href')).toBe('#tresc');
    expect(main.id).toBe('tresc');
    expect(main.getAttribute('tabindex')).toBe('-1');
  });

  it('moves focus to the main content from the skip link', async () => {
    const { element } = await render('/pulpit');
    document.body.appendChild(element);

    (element.querySelector('a[href="#tresc"]') as HTMLAnchorElement).click();

    expect(document.activeElement).toBe(element.querySelector('main'));
    element.remove();
  });

  it('has header, navigation and main landmarks', async () => {
    const { element } = await render('/pulpit');

    expect(element.querySelector('header')).not.toBeNull();
    expect(element.querySelector('nav[aria-label="Główna"]')).not.toBeNull();
    expect(element.querySelector('main')).not.toBeNull();
  });

  it('shows the product name linking to the dashboard and the current section', async () => {
    const { fixture, element } = await render('/wydatki');
    const header = element.querySelector('header') as HTMLElement;

    expect(header.querySelector('a[href="/pulpit"]')?.textContent?.trim()).toBe('Stone Flow');
    expect(header.textContent).toContain('Wydatki');

    await TestBed.inject(Router).navigateByUrl('/operacje');
    await fixture.whenStable();
    expect(header.textContent).toContain('Operacje');
  });
});
