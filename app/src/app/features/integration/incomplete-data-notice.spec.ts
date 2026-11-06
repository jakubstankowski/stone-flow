import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { IntegrationStatusStub, knownView, syncStatus } from '../../../testing/integration-testing';
import {
  AGED_TEXT,
  INCOMPLETE_TEXT,
  IncompleteDataNotice,
  UNKNOWN_TEXT,
} from './incomplete-data-notice';
import { IntegrationStatus } from './integration-status';

describe('IncompleteDataNotice', () => {
  let stub: IntegrationStatusStub;

  beforeEach(() => {
    stub = new IntegrationStatusStub();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: IntegrationStatus, useValue: stub }],
    });
  });

  async function render() {
    const fixture = TestBed.createComponent(IncompleteDataNotice);
    await fixture.whenStable();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('shows a compact last full check line for complete data', async () => {
    stub.view.set(knownView());
    const { element } = await render();

    expect(element.textContent).toContain('Ostatnia pełna kontrola: 05.11.2026, 22:50');
    expect(element.querySelector('[data-variant="warning"]')).toBeNull();
    expect(element.querySelector('a')?.getAttribute('href')).toBe('/integracja');
    expect(element.querySelector('[role="alert"]')).toBeNull();
  });

  it('warns about incomplete data with the last full check and a link', async () => {
    stub.view.set(knownView(syncStatus({ openErrors: 1 })));
    const { element } = await render();
    const warning = element.querySelector('[data-variant="warning"]') as HTMLElement;

    expect(warning.textContent).toContain(INCOMPLETE_TEXT);
    expect(warning.textContent).toContain('Ostatnia pełna kontrola: 05.11.2026, 22:50');
    expect(warning.querySelector('a')?.textContent?.trim()).toBe('Przejdź do integracji');
    expect(warning.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('warns while a sync is running', async () => {
    stub.view.set(
      knownView(
        syncStatus({
          isRunning: true,
          lastRun: { ...syncStatus().lastRun!, status: 'running', finishedAtUtc: null },
        }),
      ),
    );
    const { element } = await render();
    expect(element.textContent).toContain(INCOMPLETE_TEXT);
  });

  it('warns about an unknown state with the last known time', async () => {
    stub.view.set({ phase: 'unknown', last: syncStatus() });
    const { element } = await render();

    expect(element.textContent).toContain(UNKNOWN_TEXT);
    expect(element.textContent).toContain('05.11.2026, 22:50 (ostatni znany odczyt)');
  });

  it('warns about an aged read with its time', async () => {
    stub.view.set(knownView(syncStatus(), true));
    const { element } = await render();

    expect(element.textContent).toContain(AGED_TEXT);
    expect(element.textContent).toContain('Stan z 12:58');
  });

  it('shows only a checking line while loading, as tall as the complete line', async () => {
    const { element } = await render();
    expect(element.textContent?.trim()).toBe('Ostatnia pełna kontrola: Sprawdzanie…');
    expect(element.querySelector('p')?.classList).toContain('min-h-11');
  });

  it('updates when the state changes', async () => {
    stub.view.set(knownView());
    const { fixture, element } = await render();
    stub.view.set(knownView(syncStatus({ openErrors: 2 })));
    await fixture.whenStable();
    expect(element.textContent).toContain(INCOMPLETE_TEXT);
  });
});
