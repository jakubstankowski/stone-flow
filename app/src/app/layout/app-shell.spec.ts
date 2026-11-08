import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTestHttp, signIn } from '../../testing/auth-testing';
import { sectionRoutes } from '../app.routes';
import { AppShell } from './app-shell';
import { discardIntegrationRequests } from '../../testing/integration-testing';

describe('AppShell', () => {
  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [AppShell],
      providers: [provideRouter(sectionRoutes), provideTestHttp()],
    });
    await signIn('owner@example.com');
  });

  afterEach(() => {
    discardIntegrationRequests();
    TestBed.inject(HttpTestingController).verify();
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
    // Sekcja bez zapytań do API (lista wydatków od SF-15 pobiera dane).
    const { fixture, element } = await render('/historia-zmian');
    const header = element.querySelector('header') as HTMLElement;

    expect(header.querySelector('a[href="/pulpit"]')?.textContent?.trim()).toBe('Stone Flow');
    expect(header.textContent).toContain('Historia zmian');

    await TestBed.inject(Router).navigateByUrl('/operacje');
    await fixture.whenStable();
    expect(header.textContent).toContain('Operacje');
  });

  it('shows the user menu after the navigation in the header', async () => {
    const { element } = await render('/pulpit');
    const header = element.querySelector('header') as HTMLElement;
    const menu = header.querySelector('app-user-menu') as HTMLElement;
    const focusable = Array.from(header.querySelectorAll('a, button')).map((node) =>
      node.textContent?.trim(),
    );

    expect(menu.textContent).toContain('owner@example.com');
    expect(focusable[0]).toBe('Stone Flow');
    expect(focusable[1]).toBe('Menu');
    expect(focusable.at(-1)).toBe('Wyloguj');
  });

  it('shows the integration state in the header, before the user menu', async () => {
    const { element } = await render('/pulpit');
    const header = element.querySelector('header') as HTMLElement;
    const indicator = header.querySelector('app-integration-indicator') as HTMLElement;

    expect(indicator.querySelector('a')?.getAttribute('href')).toBe('/integracja');
    expect(indicator.textContent).toContain('Sprawdzanie stanu…');
    expect(
      indicator.compareDocumentPosition(header.querySelector('app-user-menu') as HTMLElement) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });
});
