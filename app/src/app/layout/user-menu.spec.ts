import { HttpTestingController } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import {
  nextRequest,
  provideTestHttp,
  respondNetworkError,
  respondNoContent,
  signIn,
} from '../../testing/auth-testing';
import { AuthSession } from '../core/auth/auth-session';
import { UserMenu } from './user-menu';

@Component({ template: '' })
class Blank {}

describe('UserMenu', () => {
  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [UserMenu],
      providers: [provideRouter([{ path: '**', component: Blank }]), provideTestHttp()],
    });
    await signIn('wlasciciel.z.bardzo.dlugim.adresem@example.com');
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  async function setup() {
    const fixture = TestBed.createComponent(UserMenu);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    return { fixture, element, button: element.querySelector('button') as HTMLButtonElement };
  }

  it('shows the email in a row that wraps on narrow screens', async () => {
    const { element, button } = await setup();
    const email = element.querySelector('span.truncate') as HTMLElement;

    expect(email.textContent).toContain('wlasciciel.z.bardzo.dlugim.adresem@example.com');
    expect(element.classList).toContain('basis-full');
    expect(element.classList).toContain('min-w-0');
    expect(element.classList).toContain('lg:ml-auto');
    expect(button.textContent?.trim()).toBe('Wyloguj');
    expect(button.classList).not.toContain('btn-primary');
  });

  it('logs out through csrf, logout and csrf, then shows the login screen', async () => {
    const { fixture, button } = await setup();
    button.click();
    await fixture.whenStable();

    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(button.textContent).toContain('Wylogowywanie…');
    button.click();

    respondNoContent(await nextRequest('/api/auth/csrf'));
    respondNoContent(await nextRequest({ method: 'POST', url: '/api/auth/logout' }));
    respondNoContent(await nextRequest('/api/auth/csrf'));

    const router = TestBed.inject(Router);
    await vi.waitFor(() => expect(router.url).toBe('/logowanie'));
    expect(TestBed.inject(AuthSession).takeNotice()).toBe('logged-out');
  });

  it('shows a network error and stays signed in', async () => {
    const { fixture, element, button } = await setup();
    button.click();

    respondNetworkError(await nextRequest('/api/auth/csrf'));
    await vi.waitFor(async () => {
      await fixture.whenStable();
      expect(element.querySelector('[role="alert"]')?.textContent).toContain(
        'Nie udało się połączyć z serwerem.',
      );
    });

    expect(TestBed.inject(AuthSession).status()).toBe('authenticated');
    expect(button.hasAttribute('aria-busy')).toBe(false);
  });
});
