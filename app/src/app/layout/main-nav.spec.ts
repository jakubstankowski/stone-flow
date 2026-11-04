import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../app.routes';
import { MainNav } from './main-nav';

describe('MainNav', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [MainNav], providers: [provideRouter(routes)] });
  });

  afterEach(() => {
    document.body.innerHTML = '';
  });

  async function setup(url = '/zamowienia') {
    await RouterTestingHarness.create(url);
    const fixture = TestBed.createComponent(MainNav);
    document.body.appendChild(fixture.nativeElement);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    return {
      fixture,
      element,
      menuButton: element.querySelector('button') as HTMLButtonElement,
      nav: element.querySelector('nav') as HTMLElement,
    };
  }

  it('lists the six sections in order as keyboard-reachable links', async () => {
    const { element } = await setup();
    const links = Array.from(element.querySelectorAll('nav a'));

    expect(links.map((link) => link.textContent?.trim())).toEqual([
      'Pulpit',
      'Zamówienia',
      'Wydatki',
      'Operacje',
      'Historia zmian',
      'Integracja',
    ]);
    expect(links.map((link) => link.getAttribute('href'))).toEqual([
      '/pulpit',
      '/zamowienia',
      '/wydatki',
      '/operacje',
      '/historia-zmian',
      '/integracja',
    ]);
    links.forEach((link) => expect(link.classList).toContain('min-h-11'));
  });

  it('marks the active section as the current page', async () => {
    const { element } = await setup('/zamowienia');
    const current = element.querySelectorAll('[aria-current="page"]');

    expect(current).toHaveLength(1);
    expect(current[0].textContent?.trim()).toBe('Zamówienia');
  });

  it('toggles the mobile menu with the Menu button', async () => {
    const { fixture, menuButton, nav } = await setup();

    expect(menuButton.textContent?.trim()).toBe('Menu');
    expect(menuButton.getAttribute('aria-controls')).toBe(nav.id);
    expect(menuButton.getAttribute('aria-expanded')).toBe('false');
    expect(nav.classList).toContain('hidden');
    expect(nav.classList).toContain('lg:block');

    menuButton.click();
    await fixture.whenStable();

    expect(menuButton.getAttribute('aria-expanded')).toBe('true');
    expect(nav.classList).not.toContain('hidden');
  });

  it('closes on Escape and returns focus to the Menu button', async () => {
    const { fixture, menuButton, nav } = await setup();
    menuButton.click();
    await fixture.whenStable();
    (nav.querySelector('a') as HTMLAnchorElement).focus();

    nav.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await fixture.whenStable();

    expect(menuButton.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(menuButton);
  });

  it('closes after navigation', async () => {
    const { fixture, menuButton } = await setup();
    menuButton.click();
    await fixture.whenStable();

    await TestBed.inject(Router).navigateByUrl('/wydatki');
    await fixture.whenStable();

    expect(menuButton.getAttribute('aria-expanded')).toBe('false');
  });
});

describe('MainNav link selection', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [MainNav], providers: [provideRouter(routes)] });
  });

  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('closes the menu and moves focus to the content when the active item is chosen', async () => {
    await RouterTestingHarness.create('/zamowienia');
    const fixture = TestBed.createComponent(MainNav);
    const main = document.createElement('main');
    main.id = 'tresc';
    main.tabIndex = -1;
    document.body.append(fixture.nativeElement, main);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const menuButton = element.querySelector('button') as HTMLButtonElement;

    menuButton.click();
    await fixture.whenStable();
    (element.querySelector('[aria-current="page"]') as HTMLAnchorElement).click();
    await fixture.whenStable();

    expect(menuButton.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(main);
  });
});
