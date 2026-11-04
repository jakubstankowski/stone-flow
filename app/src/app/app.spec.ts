import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { nextRequest, provideTestHttp } from '../testing/auth-testing';
import { App } from './app';
import { routes } from './app.routes';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), provideTestHttp()],
    }).compileComponents();
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('shows the session check instead of the shell until the API answers', async () => {
    const fixture = TestBed.createComponent(App);
    const element = fixture.nativeElement as HTMLElement;
    const navigation = TestBed.inject(Router).navigateByUrl('/pulpit');
    const request = await nextRequest('/api/auth/session');
    fixture.detectChanges();

    expect(element.querySelector('[role="status"]')?.textContent).toContain('Sprawdzanie sesji…');
    expect(element.querySelector('app-shell')).toBeNull();
    expect(element.querySelector('router-outlet')).not.toBeNull();

    request.flush({ email: 'owner@example.com' });
    await navigation;
    await fixture.whenStable();

    expect(element.textContent).not.toContain('Sprawdzanie sesji…');
    expect(element.querySelector('app-shell main#tresc router-outlet')).not.toBeNull();
  });
});
