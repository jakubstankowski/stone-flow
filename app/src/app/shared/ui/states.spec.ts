import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EmptyState } from './empty-state';
import { ErrorState } from './error-state';
import { LoadingState } from './loading-state';
import { StatusBadge, StatusTone } from './status-badge';

@Component({
  imports: [EmptyState, ErrorState, LoadingState, StatusBadge],
  template: `
    <app-loading-state [rows]="4" />
    <app-empty-state
      title="Brak wydatków w wybranym okresie."
      description="Dodaj pierwszy wydatek."
    >
      <button type="button">Dodaj wydatek</button>
    </app-empty-state>
    <app-error-state (retry)="retried = retried + 1" />
    <app-status-badge label="Opłacone" [tone]="tone()" [marker]="true" />
  `,
})
class Host {
  readonly tone = signal<StatusTone>('success');
  retried = 0;
}

async function setup() {
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  return {
    fixture,
    host: fixture.componentInstance,
    element: fixture.nativeElement as HTMLElement,
  };
}

describe('system states and status badge', () => {
  it('announces loading and renders skeleton rows without values', async () => {
    const { element } = await setup();
    const status = element.querySelector('app-loading-state [role="status"]');

    expect(status?.textContent?.trim()).toBe('Ładowanie…');
    expect(status?.querySelectorAll('.skeleton[aria-hidden="true"]')).toHaveLength(4);
  });

  it('renders the empty state with its action', async () => {
    const { element } = await setup();
    const empty = element.querySelector('app-empty-state') as HTMLElement;

    expect(empty.textContent).toContain('Brak wydatków w wybranym okresie.');
    expect(empty.textContent).toContain('Dodaj pierwszy wydatek.');
    expect(empty.querySelector('button')?.textContent).toBe('Dodaj wydatek');
  });

  it('renders the error as an alert with retry', async () => {
    const { host, element } = await setup();
    const alert = element.querySelector('app-error-state [role="alert"]') as HTMLElement;

    expect(alert.textContent).toContain('Nie udało się wczytać danych.');
    (alert.querySelector('button') as HTMLButtonElement).click();
    expect(host.retried).toBe(1);
  });

  it('always shows the status text, with tone only as reinforcement', async () => {
    const { fixture, host, element } = await setup();
    const badge = () => element.querySelector('app-status-badge > span') as HTMLElement;

    expect(badge().textContent?.trim()).toBe('Opłacone');
    expect(badge().className).toContain('text-(--sf-success)');
    expect(badge().querySelector('[aria-hidden="true"]')).not.toBeNull();

    host.tone.set('error');
    await fixture.whenStable();
    expect(badge().textContent?.trim()).toBe('Opłacone');
    expect(badge().className).toContain('text-(--sf-error)');
  });
});
