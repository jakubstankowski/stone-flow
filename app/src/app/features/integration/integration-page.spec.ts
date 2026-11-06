import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import {
  nextRequest,
  provideTestHttp,
  respondNetworkError,
  respondProblem,
} from '../../../testing/auth-testing';
import { STATUS_URL, orderError, syncStatus } from '../../../testing/integration-testing';
import { OrderError, SyncStatus } from './integration-models';
import { IntegrationPage, SYNC_LOADING_REASON, SYNC_RUNNING_REASON } from './integration-page';
import { NOT_CONFIGURED_TEXT } from './integration-state';
import { IntegrationStatus } from './integration-status';

@Component({ template: '' })
class Blank {}

const ERRORS_URL = '/api/integrations/woocommerce/errors';
const SYNC_URL = '/api/integrations/woocommerce/sync';

function errorsPage(items: OrderError[] = [orderError()], total = items.length) {
  return { items, page: 1, pageSize: 25, total };
}

function buttonByText(element: HTMLElement, text: string): HTMLButtonElement {
  const button = Array.from(element.querySelectorAll('button')).find((candidate) =>
    candidate.textContent?.replace(/\s+/g, ' ').trim().startsWith(text),
  );
  if (!button) {
    throw new Error(`Brak przycisku „${text}”`);
  }
  return button;
}

describe('IntegrationPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    vi.setSystemTime(new Date('2026-11-06T12:00:00Z'));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'integracja', component: IntegrationPage },
          { path: '**', component: Blank },
        ]),
        provideTestHttp(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.useRealTimers();
    http.verify();
  });

  async function open(
    status: SyncStatus | 'network' = syncStatus(),
    errors: OrderError[] = [orderError()],
  ) {
    const harness = await RouterTestingHarness.create('/integracja');
    const element = harness.routeNativeElement as HTMLElement;
    const stable = () => harness.fixture.whenStable();
    const statusRequest = await nextRequest(STATUS_URL);
    if (status === 'network') {
      respondNetworkError(statusRequest);
    } else {
      statusRequest.flush(status);
    }
    (await nextRequest((r) => r.url === ERRORS_URL)).flush(errorsPage(errors));
    await stable();
    return { element, stable };
  }

  it('shows the state, its explanation and the last full check in Europe/Warsaw', async () => {
    const { element } = await open();

    expect(element.querySelector('h1')?.textContent).toBe('Integracja');
    expect(element.textContent).toContain('Aktualne');
    expect(element.textContent).toContain(
      'Ostatnie uzupełnianie się powiodło i nie ma otwartych błędów zamówień.',
    );
    expect(element.textContent).toContain('Ostatnia pełna kontrola');
    expect(element.textContent).toContain('05.11.2026, 22:50');
  });

  it('shows the last run with its message', async () => {
    const run = {
      ...syncStatus().lastRun!,
      status: 'abandoned',
      message: 'Przerwany (restart lub awaria)',
    };
    const { element } = await open(syncStatus({ lastRun: run }));

    expect(element.textContent).toContain('Błąd');
    expect(element.textContent).toContain('Przerwany');
    expect(element.textContent).toContain('Automatyczny (23:00)');
    expect(element.textContent).toContain('Przerwany (restart lub awaria)');
    expect(element.textContent).toContain('Przetworzone zamówienia');
  });

  it('says when no full check or run has happened yet', async () => {
    const { element } = await open(syncStatus({ lastSuccessAtUtc: null, lastRun: null }), []);

    expect(element.textContent).toContain('Dane niekompletne');
    expect(element.textContent).toContain('Pełna kontrola jeszcze się nie odbyła.');
    expect(element.textContent).toContain('Uzupełnianie jeszcze się nie odbyło.');
    expect(element.textContent).toContain('Brak otwartych błędów zamówień');
  });

  it('shows an error state with retry when the status cannot be read', async () => {
    const { element, stable } = await open('network');

    const alert = element.querySelector('app-error-state [role="alert"]') as HTMLElement;
    expect(alert.textContent).toContain('Nie udało się wczytać stanu integracji.');
    expect(buttonByText(element, 'Synchronizuj teraz').disabled).toBe(false);

    buttonByText(alert, 'Spróbuj ponownie').click();
    (await nextRequest(STATUS_URL)).flush(syncStatus());
    await stable();
    expect(element.textContent).toContain('Aktualne');
  });

  it('disables the sync while loading, with the visible reason linked', async () => {
    const harness = await RouterTestingHarness.create('/integracja');
    const element = harness.routeNativeElement as HTMLElement;
    await harness.fixture.whenStable();
    const button = buttonByText(element, 'Synchronizuj teraz');

    expect(button.disabled).toBe(true);
    const reason = document.getElementById(button.getAttribute('aria-describedby') ?? '');
    expect(element.textContent).toContain(SYNC_LOADING_REASON);
    expect(reason?.textContent).toBe(SYNC_LOADING_REASON);

    (await nextRequest(STATUS_URL)).flush(syncStatus());
    (await nextRequest((r) => r.url === ERRORS_URL)).flush(errorsPage());
  });

  it.each([
    [syncStatus({ isRunning: true }), SYNC_RUNNING_REASON],
    [syncStatus({ configured: false }), NOT_CONFIGURED_TEXT],
  ])('disables the sync with a visible reason', async (status, text) => {
    const { element } = await open(status);
    const button = buttonByText(element, 'Synchronizuj teraz');

    expect(button.disabled).toBe(true);
    expect(element.querySelector(`#${button.getAttribute('aria-describedby')}`)?.textContent).toBe(
      text,
    );
  });

  it('starts a sync, reports it and reads the status again', async () => {
    const { element, stable } = await open();

    buttonByText(element, 'Synchronizuj teraz').click();
    const request = await nextRequest(SYNC_URL);
    expect(request.request.method).toBe('POST');
    request.flush(null, { status: 202, statusText: 'Accepted' });
    (await nextRequest(STATUS_URL)).flush(syncStatus({ isRunning: true }));
    await stable();

    expect(element.querySelector('[role="status"]')?.textContent).toContain(
      'Synchronizacja uruchomiona.',
    );
    await vi.waitFor(async () => {
      await stable();
      expect(element.textContent).toContain('Synchronizacja trwa');
    });
  });

  it('reports a sync already in progress', async () => {
    const { element, stable } = await open();

    buttonByText(element, 'Synchronizuj teraz').click();
    respondProblem(await nextRequest(SYNC_URL), 409, {
      code: 'sync_in_progress',
      detail: 'Synchronizacja już trwa. Poczekaj na jej zakończenie.',
    });
    (await nextRequest(STATUS_URL)).flush(syncStatus({ isRunning: true }));
    await stable();

    expect(element.querySelector('[role="status"]')?.textContent).toContain(
      'Synchronizacja już trwa.',
    );
  });

  it('reports a CSRF problem without reading the status again', async () => {
    const { element, stable } = await open();

    buttonByText(element, 'Synchronizuj teraz').click();
    respondProblem(await nextRequest(SYNC_URL), 400, { code: 'csrf_invalid' });
    await stable();

    expect(element.querySelector('[role="alert"]')?.textContent).toContain(
      'Odśwież stronę i spróbuj ponownie.',
    );
    expect(http.match(STATUS_URL)).toHaveLength(0);
  });

  it('lists errors with a link to the order and the shop ID', async () => {
    const { element } = await open(syncStatus({ openErrors: 2 }), [
      orderError(),
      orderError({ id: 'e2', externalId: '1002', orderId: null, orderNumber: null }),
    ]);
    const rows = element.querySelectorAll('tbody tr');

    expect(rows).toHaveLength(2);
    const link = rows[0].querySelector('a') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/zamowienia/o1');
    expect(link.textContent?.trim()).toBe('Zamówienie Z/2026/0001');
    expect(rows[0].textContent).toContain('ID w sklepie: 1001');
    expect(rows[0].textContent).toContain('Kontekst: abc123');
    expect(rows[0].textContent).toContain('Webhook');
    expect(rows[1].querySelector('a')).toBeNull();
    expect(rows[1].textContent).toContain('ID w sklepie: 1002');
  });

  it('names each retry button after its order, also while busy', async () => {
    const { element, stable } = await open();
    const button = buttonByText(element, 'Ponów');

    expect(button.textContent?.replace(/\s+/g, ' ').trim()).toBe('Ponów zamówienie 1001');

    button.click();
    await stable();
    // Projekcja jest ukryta (`hidden`), więc nazwę w `busy` daje sama etykieta zajętości.
    const visible = button.querySelector('span:not([hidden]):not(.loading)');
    expect(visible?.textContent?.trim()).toBe('Ponawianie zamówienia 1001…');
    expect(button.querySelector('[hidden]')?.textContent).toContain('Ponów');

    (await nextRequest('/api/integrations/woocommerce/errors/e1/retry')).flush(
      orderError({ status: 'closed', closedAtUtc: '2026-11-06T11:00:00Z', closedBy: 'retry' }),
    );
    (await nextRequest((r) => r.url === ERRORS_URL)).flush(
      errorsPage([
        orderError({ status: 'closed', closedAtUtc: '2026-11-06T11:00:00Z', closedBy: 'retry' }),
      ]),
    );
    (await nextRequest(STATUS_URL)).flush(syncStatus());
    await stable();

    await vi.waitFor(async () => {
      await stable();
      expect(element.textContent).toContain('Zamknięty');
    });
    const region = element.querySelector('[data-region="retry-result"]') as HTMLElement;
    expect(region.querySelector('[role="status"]')?.textContent).toContain(
      'Zamówienie Z/2026/0001 zapisane. Błąd zamknięty.',
    );
    // Przycisk zniknął przy przeładowaniu: fokus jest na stałym regionie wyniku.
    await vi.waitFor(() => expect(document.activeElement).toBe(region));
    expect(region.getAttribute('tabindex')).toBe('-1');
    expect(element.textContent).toContain('06.11.2026, 12:00, Ponów');
  });

  it('keeps the result regions in the DOM before any result', async () => {
    const { element } = await open();
    const region = element.querySelector('[data-region="retry-result"]') as HTMLElement;

    expect(region.querySelector('[role="status"]')?.textContent?.trim()).toBe('');
    expect(region.querySelector('[role="alert"]')?.textContent?.trim()).toBe('');
    expect(element.querySelectorAll('[role="status"][aria-live="polite"]').length).toBe(2);
  });

  it('hides retry without a configuration', async () => {
    const { element } = await open(syncStatus({ configured: false }));
    expect(element.textContent).not.toContain('Ponów zamówienie');
  });

  it('filters by status through the API', async () => {
    const { element, stable } = await open();
    const select = element.querySelector('select') as HTMLSelectElement;

    select.value = 'closed';
    select.dispatchEvent(new Event('change'));
    const request: TestRequest = await nextRequest((r) => r.url === ERRORS_URL);
    expect(request.request.params.get('status')).toBe('closed');
    expect(request.request.params.get('page')).toBe('1');
    request.flush(errorsPage([]));
    await stable();

    expect(element.textContent).toContain('Status: Zamknięte');
    expect(element.textContent).toContain('Brak błędów dla wybranego filtra');
  });

  it('reloads the errors when a running sync ends', async () => {
    const { stable } = await open(syncStatus({ isRunning: true }));

    void TestBed.inject(IntegrationStatus).refresh({ force: true });
    (await nextRequest(STATUS_URL)).flush(syncStatus({ isRunning: false, openErrors: 1 }));
    await stable();

    (await nextRequest((r) => r.url === ERRORS_URL)).flush(errorsPage());
    await stable();
  });
});
