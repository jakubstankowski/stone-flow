import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import {
  nextRequest,
  provideTestHttp,
  respondNetworkError,
  respondProblem,
} from '../../../testing/auth-testing';
import {
  beforeLaunchReport,
  reportPeriod,
  salesComponentItem,
  salesComponentsPage,
  salesReport,
} from '../../../testing/dashboard-testing';
import { IntegrationStatusStub, knownView, syncStatus } from '../../../testing/integration-testing';
import { formatDateTime, formatPln } from '../../shared/format/pl-format';
import { IntegrationStatus } from '../integration/integration-status';
import { CHART_FACTORY, ChartHandle } from './chart-factory';
import { DashboardPage } from './dashboard-page';
import { DashboardService } from './dashboard.service';
import { SalesComponentsPage, SalesReport } from './report-models';

const fakeChart = (): ChartHandle => ({
  data: { labels: [], datasets: [] },
  update: vi.fn(),
  destroy: vi.fn(),
  setActiveElements: vi.fn(),
});

describe('DashboardPage', () => {
  let http: HttpTestingController;
  let integration: IntegrationStatusStub;

  beforeEach(() => {
    integration = new IntegrationStatusStub();
    TestBed.configureTestingModule({
      imports: [DashboardPage],
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        provideTestHttp(),
        { provide: IntegrationStatus, useValue: integration },
        { provide: CHART_FACTORY, useValue: fakeChart },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(
    report: SalesReport = salesReport(),
    page: SalesComponentsPage = salesComponentsPage({
      items: [
        salesComponentItem(),
        salesComponentItem({
          kind: 'salesReturn',
          id: 'r1',
          amount: '-300.00',
          date: '2026-11-20',
        }),
      ],
    }),
  ) {
    const fixture = TestBed.createComponent(DashboardPage);
    (await nextRequest((r) => r.url === '/api/reports/sales')).flush(report);
    (await nextRequest((r) => r.url === '/api/reports/sales/components')).flush(page);
    const element = fixture.nativeElement as HTMLElement;
    const settle = async (check: () => void) =>
      vi.waitFor(async () => {
        await fixture.whenStable();
        check();
      });
    await settle(() => expect(element.querySelector('.skeleton')).toBeNull());
    return { fixture, element, settle };
  }

  const text = (element: HTMLElement, id: string) =>
    element.querySelector(`[data-testid="${id}"]`)?.textContent?.trim() ?? null;

  it('shows the view switch as radios in a fieldset and the cash flow placeholder', async () => {
    const { element, settle } = await render();
    const legend = element.querySelector('fieldset legend');
    const radios = Array.from(
      element.querySelectorAll<HTMLInputElement>('fieldset input[type="radio"]'),
    );

    expect(legend?.textContent?.trim()).toBe('Widok');
    expect(radios.map((radio) => radio.getAttribute('aria-label'))).toEqual([
      'Sprzedaż',
      'Przepływy pieniężne',
    ]);
    expect(radios[0].checked).toBe(true);

    radios[1].checked = true;
    radios[1].dispatchEvent(new Event('change'));
    await settle(() =>
      expect(element.textContent).toContain(
        'Widok przepływów pieniężnych powstanie w kolejnym etapie.',
      ),
    );
    expect(element.textContent).toContain('Operacje pieniężne są na liście „Operacje”.');
    expect(element.querySelector('a[href="/operacje"]')).not.toBeNull();
    expect(element.querySelector('app-period-picker')).toBeNull();
  });

  it('shows the period, comparison description, freshness, basis and the integration notice', async () => {
    integration.view.set(knownView(syncStatus()));
    const { element } = await render();

    expect(text(element, 'okres')).toBe('listopad 2026, 01.11–30.11.2026');
    expect(text(element, 'opis-porownania')).toBe(
      'Miesiąc w toku — porównanie z całym poprzednim miesiącem (październik 2026).',
    );
    expect(text(element, 'stan-na')).toBe(`Stan na ${formatDateTime('2026-11-09T11:15:00+00:00')}`);
    expect(text(element, 'podstawa')).toBe(
      'Kwoty brutto w PLN. Raporty obejmują dane od 01.09.2026.',
    );
    expect(element.textContent).toContain('Ostatnia pełna kontrola');
    expect(element.querySelector('[data-testid="uruchomienie"]')).toBeNull();
  });

  it('warns about incomplete integration data', async () => {
    integration.view.set(knownView(syncStatus({ openErrors: 2 })));
    const { element } = await render();

    expect(element.textContent).toContain(
      'Nie pobrano wszystkich zamówień. Wyniki mogą być niepełne.',
    );
  });

  it('tells about the launch date when the period was cut', async () => {
    const { element } = await render(
      salesReport({
        period: reportPeriod({ effectiveFrom: '2026-11-03', launchDate: '2026-11-03' }),
        comparison: {
          from: '2026-10-01',
          to: '2026-10-31',
          available: false,
          reason: 'beforeLaunch',
        },
      }),
    );

    expect(text(element, 'uruchomienie')).toBe('Raport liczony od 03.11.2026 (data uruchomienia).');
  });

  it('shows four cards with values, descriptions, comparisons and hints; the result has no action', async () => {
    const { element } = await render();
    const cards = Array.from(element.querySelectorAll('app-kpi-card'));

    expect(cards).toHaveLength(4);
    expect(
      cards.map((card) => card.querySelector('[data-testid="wartosc"]')?.textContent?.trim()),
    ).toEqual([formatPln('1200.00'), formatPln('300.00'), formatPln('900.00'), '2']);
    expect(cards[0].textContent).toContain('Wartość brutto zamówień według daty zamówienia');
    expect(cards[0].textContent).toContain(
      `+50,0% (+${formatPln('400.00')}) więcej niż w: październik 2026`,
    );
    expect(cards[1].textContent).toContain('brak podstawy porównania');
    expect(cards[2].textContent).toContain(
      `-5,3% (${formatPln('-50.00')}) mniej niż w: październik 2026`,
    );
    expect(cards[3].textContent).toContain('+100,0% (+1 zam.) więcej niż w: październik 2026');
    expect(cards[0].querySelector('button')?.textContent).toContain('Pokaż składniki');
    expect(cards[1].querySelector('a')?.textContent).toContain('Pokaż wydatki');
    expect(cards[1].querySelector('a')?.getAttribute('href')).toBe(
      '/wydatki?kosztOd=2026-11-01&kosztDo=2026-11-30',
    );
    expect(cards[2].querySelector('a, button')).toBeNull();
    expect(cards[3].querySelector('button')?.textContent).toContain('Pokaż w zestawieniu');
  });

  it('opens the chart data table with a total row taken from the cards', async () => {
    const { element, settle } = await render();
    const toggle = Array.from(element.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === 'Pokaż dane w tabeli',
    )!;

    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    toggle.click();
    await settle(() => expect(element.querySelector('[data-testid="razem"]')).not.toBeNull());

    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(element.querySelector(`#${toggle.getAttribute('aria-controls')} table`)).not.toBeNull();
    const total = element.querySelector('[data-testid="razem"]')!.textContent!;
    expect(total).toContain(formatPln('1200.00'));
    expect(total).toContain(formatPln('300.00'));
    expect(total).toContain(formatPln('900.00'));
    expect(element.querySelectorAll('canvas[role="img"]')).toHaveLength(3);
  });

  it('always lists channels and categories; a channel filters the components and focuses them', async () => {
    const { element, settle } = await render();
    const channels = element.querySelectorAll('[data-testid="kanaly"] li');
    const categories = element.querySelectorAll('[data-testid="kategorie"] a');

    expect(channels).toHaveLength(5);
    expect(channels[1].textContent).toContain('OLX');
    expect(channels[1].textContent).toContain(formatPln('800.00'));
    expect(categories).toHaveLength(7);
    expect(categories[1].getAttribute('href')).toBe(
      '/wydatki?kosztOd=2026-11-01&kosztDo=2026-11-30&kategoria=cutting',
    );

    channels[1].querySelector('button')!.click();
    const request = await nextRequest((r) => r.url === '/api/reports/sales/components');
    expect(request.request.params.get('channel')).toBe('olx');
    request.flush(salesComponentsPage({ sum: '800.00' }));

    await settle(() => expect(element.textContent).toContain('Kanał: OLX'));
    expect(document.activeElement?.id).toBe('zestawienie-skladnikow');
  });

  it('filters the components by orders from the order count card', async () => {
    const { element } = await render();

    element.querySelectorAll('app-kpi-card')[3].querySelector('button')!.click();
    const request = await nextRequest((r) => r.url === '/api/reports/sales/components');
    expect(request.request.params.get('kind')).toBe('order');
    request.flush(salesComponentsPage());
  });

  it('shows components with order links, signed amounts and the sum with its basis', async () => {
    const { element } = await render();
    const rows = Array.from(element.querySelectorAll('app-sales-components tbody tr'));

    expect(rows).toHaveLength(2);
    expect(rows[0].querySelector('a')?.getAttribute('href')).toBe('/zamowienia/o1');
    expect(rows[0].textContent).toContain('ZAM/2026/11/0001');
    expect(rows[1].textContent).toContain('Zwrot sprzedaży');
    expect(rows[1].textContent).toContain(formatPln('-300.00'));
    expect(text(element, 'suma-zestawienia')).toContain(
      `Suma zestawienia: ${formatPln('1200.00')}`,
    );
    expect(text(element, 'suma-zestawienia')).toContain(
      'Kwoty brutto w PLN. Zamówienia według daty zamówienia, zwroty sprzedaży i korekty sprzedaży według ich dat.',
    );
  });

  it('tells an empty period from an empty filter result', async () => {
    const { element, settle } = await render(salesReport(), salesComponentsPage({ sum: '0.00' }));
    expect(element.textContent).toContain('Brak składników sprzedaży w wybranym okresie');

    const filtered = TestBed.inject(DashboardService).setKind('salesCorrection');
    (await nextRequest((r) => r.url === '/api/reports/sales/components')).flush(
      salesComponentsPage({ sum: '0.00' }),
    );
    await filtered;
    await settle(() =>
      expect(element.textContent).toContain('Brak składników dla wybranych filtrów'),
    );
    expect(element.textContent).toContain('Wyczyść filtry');
  });

  it('shows the before-launch message instead of cards, charts and components', async () => {
    const { element } = await render(beforeLaunchReport(), salesComponentsPage({ sum: null }));

    expect(element.querySelector('[role="status"]')?.textContent).toContain(
      'Wybrany okres jest w całości przed datą uruchomienia (01.09.2026). Raporty obejmują dane od tej daty.',
    );
    expect(element.querySelector('app-kpi-card')).toBeNull();
    expect(element.querySelector('canvas')).toBeNull();
    expect(element.querySelector('app-sales-components')).toBeNull();
    expect(element.querySelector('[data-testid="opis-porownania"]')).toBeNull();
  });

  it('shows the report error with a retry and the 422 message at the period picker', async () => {
    const fixture = TestBed.createComponent(DashboardPage);
    respondNetworkError(await nextRequest((r) => r.url === '/api/reports/sales'));
    (await nextRequest((r) => r.url === '/api/reports/sales/components')).flush(
      salesComponentsPage(),
    );
    const element = fixture.nativeElement as HTMLElement;
    await vi.waitFor(async () => {
      await fixture.whenStable();
      expect(element.textContent).toContain('Nie udało się wczytać raportu.');
    });
    expect(element.textContent).toContain('Spróbuj ponownie');
    expect(element.querySelector('app-kpi-card')).toBeNull();

    TestBed.inject(DashboardService).setSelection({ kind: 'previousMonth' });
    respondProblem(await nextRequest((r) => r.url === '/api/reports/sales'), 422, {
      code: 'report_range_too_large',
      detail: 'Za dużo danych w wybranym okresie. Wybierz krótszy okres.',
    });
    (await nextRequest((r) => r.url === '/api/reports/sales/components')).flush(
      salesComponentsPage(),
    );
    await vi.waitFor(async () => {
      await fixture.whenStable();
      expect(element.querySelector('app-period-picker [role="alert"]')?.textContent).toContain(
        'Za dużo danych w wybranym okresie. Wybierz krótszy okres.',
      );
    });
  });

  it('shows a skeleton without zeros while loading', async () => {
    const fixture = TestBed.createComponent(DashboardPage);
    const element = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();

    expect(element.querySelector('.skeleton')).not.toBeNull();
    expect(element.querySelector('app-kpi-card')).toBeNull();
    expect(element.textContent).not.toContain(formatPln('0.00'));

    (await nextRequest((r) => r.url === '/api/reports/sales')).flush(salesReport());
    (await nextRequest((r) => r.url === '/api/reports/sales/components')).flush(
      salesComponentsPage(),
    );
  });

  it('shows a separate components error with a retry', async () => {
    const fixture = TestBed.createComponent(DashboardPage);
    (await nextRequest((r) => r.url === '/api/reports/sales')).flush(salesReport());
    respondNetworkError(await nextRequest((r) => r.url === '/api/reports/sales/components'));
    const element = fixture.nativeElement as HTMLElement;
    await vi.waitFor(async () => {
      await fixture.whenStable();
      expect(element.textContent).toContain('Nie udało się wczytać zestawienia.');
    });
    expect(element.querySelectorAll('app-kpi-card')).toHaveLength(4);

    const retry = Array.from(element.querySelectorAll('app-sales-components button')).find(
      (button) => button.textContent?.trim() === 'Spróbuj ponownie',
    ) as HTMLButtonElement;
    retry.click();
    (await nextRequest((r) => r.url === '/api/reports/sales/components')).flush(
      salesComponentsPage(),
    );
  });

  it('reads the report again on every entry', async () => {
    const { fixture } = await render();
    fixture.destroy();

    TestBed.createComponent(DashboardPage);
    (await nextRequest((r) => r.url === '/api/reports/sales')).flush(salesReport());
    (await nextRequest((r) => r.url === '/api/reports/sales/components')).flush(
      salesComponentsPage(),
    );
  });
});
