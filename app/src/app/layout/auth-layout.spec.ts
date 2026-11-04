import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthLayout } from './auth-layout';

describe('AuthLayout', () => {
  it('shows the product name and a focusable main area without navigation', async () => {
    TestBed.configureTestingModule({ imports: [AuthLayout], providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(AuthLayout);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const main = element.querySelector('main') as HTMLElement;

    expect(element.textContent).toContain('Stone Flow');
    expect(main.id).toBe('tresc');
    expect(main.getAttribute('tabindex')).toBe('-1');
    expect(main.querySelector('router-outlet')).not.toBeNull();
    expect(element.querySelector('nav')).toBeNull();
    expect(element.querySelector('header')).toBeNull();
  });
});
