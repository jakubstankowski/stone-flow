import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { IntegrationStatusStub, knownView, syncStatus } from '../../testing/integration-testing';
import { IntegrationStatus } from '../features/integration/integration-status';
import { IntegrationIndicator } from './integration-indicator';

describe('IntegrationIndicator', () => {
  let stub: IntegrationStatusStub;

  beforeEach(() => {
    stub = new IntegrationStatusStub();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: IntegrationStatus, useValue: stub }],
    });
  });

  async function render() {
    const fixture = TestBed.createComponent(IntegrationIndicator);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    return { fixture, element, link: element.querySelector('a') as HTMLAnchorElement };
  }

  it('starts the status refresh for the life of the shell', async () => {
    const { fixture } = await render();
    expect(stub.start).toHaveBeenCalledTimes(1);
    fixture.destroy();
    expect(stub.stop).toHaveBeenCalledTimes(1);
  });

  it('links to the integration screen with the visible label and an icon', async () => {
    stub.view.set(knownView());
    const { link } = await render();

    expect(link.getAttribute('href')).toBe('/integracja');
    expect(link.querySelector('app-status-badge')?.textContent?.trim()).toBe('Aktualne');
    expect(link.querySelector('svg[data-icon="check"]')?.getAttribute('aria-hidden')).toBe('true');
    expect(link.hasAttribute('title')).toBe(false);
  });

  it('names the link starting with the label, then the last full check', async () => {
    stub.view.set(knownView());
    const { link } = await render();
    expect(link.getAttribute('aria-label')).toBe(
      'Aktualne, ostatnia pełna kontrola: 05.11.2026, 22:50. Stan integracji — przejdź do ekranu Integracja',
    );
  });

  it('shows an aged read neutrally, with a clock and the time of the read', async () => {
    stub.view.set(knownView(syncStatus(), true));
    const { link } = await render();

    expect(link.querySelector('svg[data-icon="clock"]')).not.toBeNull();
    expect(link.querySelector('svg[data-icon="check"]')).toBeNull();
    expect(link.textContent).toContain('Stan z 12:58');
    expect(link.querySelector('app-status-badge > span')?.className).not.toContain('sf-success');
    expect(link.getAttribute('aria-label')).toMatch(
      /^Aktualne, stan z 12:58, ostatnia pełna kontrola/,
    );
  });

  it('shows the loading label without a time', async () => {
    const { link } = await render();
    expect(link.getAttribute('aria-label')).toBe('Sprawdzanie stanu…');
  });

  it('announces state changes in a polite live region', async () => {
    const { fixture, element } = await render();
    stub.announcement.set('Stan integracji: Błąd');
    await fixture.whenStable();

    const region = element.querySelector('[role="status"]') as HTMLElement;
    expect(region.getAttribute('aria-live')).toBe('polite');
    expect(region.textContent?.trim()).toBe('Stan integracji: Błąd');
  });
});
