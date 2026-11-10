import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import {
  nextRequest,
  provideTestHttp,
  respondNetworkError,
  respondProblem,
} from '../../../testing/auth-testing';
import {
  beforeLaunchCashFlowReport,
  cashFlowReport,
  reportPeriod,
} from '../../../testing/dashboard-testing';
import { IntegrationStatusStub } from '../../../testing/integration-testing';
import { formatPln } from '../../shared/format/pl-format';
import { IntegrationStatus } from '../integration/integration-status';
import { ChartCanvas, ChartSpec } from './chart-canvas';
import { CHART_FACTORY, ChartHandle } from './chart-factory';
import { DashboardPage } from './dashboard-page';
import { DashboardService } from './dashboard.service';
import { CashFlowReport } from './report-models';

const CASH_FLOW = '/api/reports/cash-flow';

const fakeChart = (): ChartHandle => ({
  data: { labels: [], datasets: [] },
  update: vi.fn(),
  destroy: vi.fn(),
  setActiveElements: vi.fn(),
});

/** F-CF1 … F-CF8: cały `DashboardPage` w widoku „Przepływy pieniężne” (SF-20). */
describe('CashFlowView', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [DashboardPage],
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        provideTestHttp(),
        { provide: IntegrationStatus, useValue: new IntegrationStatusStub() },
        { provide: CHART_FACTORY, useValue: fakeChart },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    // Widok wybrany wcześniej (stan serwisu przetrwa powrót na Pulpit); pierwsze żądanie odrzucone.
    TestBed.inject(DashboardService).setView('cashFlow');
    http.match((r) => r.url === CASH_FLOW);
  });

  afterEach(() => http.verify());

  function create() {
    const fixture = TestBed.createComponent(DashboardPage);
    const element = fixture.nativeElement as HTMLElement;
    const settle = async (check: () => void) =>
      vi.waitFor(async () => {
        await fixture.whenStable();
        check();
      });
    return { fixture, element, settle };
  }

  async function render(report: CashFlowReport = cashFlowReport()) {
    const view = create();
    (await nextRequest((r) => r.url === CASH_FLOW)).flush(report);
    await view.settle(() => expect(view.element.querySelector('.skeleton')).toBeNull());
    return view;
  }

  const text = (element: HTMLElement, id: string) =>
    element.querySelector(`[data-testid="${id}"]`)?.textContent?.trim() ?? null;

  const specs = (fixture: ComponentFixture<DashboardPage>): ChartSpec[] =>
    fixture.debugElement
      .queryAll(By.directive(ChartCanvas))
      .map((debug) => (debug.componentInstance as ChartCanvas).spec());

  const button = (element: HTMLElement, label: string) =>
    Array.from(element.querySelectorAll('button')).find(
      (candidate) => candidate.textContent?.trim() === label,
    ) as HTMLButtonElement;

  async function openTable(element: HTMLElement, settle: (check: () => void) => Promise<void>) {
    button(element, 'Pokaż dane w tabeli').click();
    await settle(() => expect(element.querySelector('[data-testid="razem"]')).not.toBeNull());
  }

  describe('states (F-CF1)', () => {
    it('shows a skeleton without zeros and without the header while loading', async () => {
      const { element, fixture } = create();
      await fixture.whenStable();

      expect(element.querySelector('app-cash-flow-view .skeleton')).not.toBeNull();
      expect(element.querySelector('app-kpi-card')).toBeNull();
      expect(element.querySelector('[data-testid="okres"]')).toBeNull();
      expect(element.textContent).not.toContain(formatPln('0.00'));
      (await nextRequest((r) => r.url === CASH_FLOW)).flush(cashFlowReport());
    });

    it('shows the error with a retry that reads the cash flow report again', async () => {
      const { element, settle } = create();
      respondNetworkError(await nextRequest((r) => r.url === CASH_FLOW));
      await settle(() => expect(element.textContent).toContain('Nie udało się wczytać raportu.'));
      expect(element.querySelector('app-kpi-card')).toBeNull();

      button(element, 'Spróbuj ponownie').click();
      (await nextRequest((r) => r.url === CASH_FLOW)).flush(cashFlowReport());
      await settle(() => expect(element.querySelectorAll('app-kpi-card')).toHaveLength(3));
    });

    it('shows 422 at the period picker', async () => {
      const { element, settle } = create();
      respondProblem(await nextRequest((r) => r.url === CASH_FLOW), 422, {
        code: 'report_range_too_large',
        detail: 'Za dużo danych w wybranym okresie. Wybierz krótszy okres.',
      });

      await settle(() =>
        expect(element.querySelector('app-period-picker [role="alert"]')?.textContent).toContain(
          'Za dużo danych w wybranym okresie. Wybierz krótszy okres.',
        ),
      );
      expect(element.querySelector('app-kpi-card')).toBeNull();
    });
  });

  it('shows three cards with values, descriptions, comparisons, hints and links (F-CF2)', async () => {
    const { element } = await render();
    const cards = Array.from(element.querySelectorAll('app-kpi-card'));

    expect(cards).toHaveLength(3);
    expect(
      cards.map((card) => card.querySelector('[data-testid="wartosc"]')?.textContent?.trim()),
    ).toEqual([formatPln('1500.00'), formatPln('700.00'), formatPln('800.00')]);
    expect(cards[0].textContent).toContain(
      'Wpłaty za zamówienia według daty wpłaty. Nieopłacone zamówienia ich nie zwiększają.',
    );
    expect(cards[1].textContent).toContain(
      'Zwroty, opłacone wydatki według daty zapłaty i wypłaty właścicieli. Ujemne opłacone wydatki pomniejszają wypływy.',
    );
    expect(cards[2].textContent).toContain('Wpływy minus wypływy w tym samym okresie.');
    expect(cards[0].textContent).toContain(
      `+50,0% (+${formatPln('500.00')}) więcej niż w: październik 2026`,
    );
    expect(cards[1].textContent).toContain('brak podstawy porównania');
    expect(cards[2].textContent).toContain(
      `-20,0% (${formatPln('-200.00')}) mniej niż w: październik 2026`,
    );
    const links = cards.map((card) => card.querySelector('a'));
    expect(links.map((link) => link?.textContent)).toEqual([
      expect.stringContaining('Pokaż wpłaty'),
      expect.stringContaining('Pokaż wypływy'),
      expect.stringContaining('Pokaż operacje'),
    ]);
    expect(links.map((link) => link?.getAttribute('href'))).toEqual([
      '/operacje?od=2026-11-03&do=2026-11-30&rodzaj=payment',
      '/operacje?od=2026-11-03&do=2026-11-30&rodzaj=refund&rodzaj=expense&rodzaj=withdrawal',
      '/operacje?od=2026-11-03&do=2026-11-30',
    ]);
    expect(cards[2].className).toContain('md:col-span-2');
  });

  it('draws inflows and outflows in time and opens the data table with totals from the cards (F-CF3)', async () => {
    const { element, fixture, settle } = await render();
    const [timeline] = specs(fixture);

    expect(timeline.type).toBe('timeline');
    expect(timeline.labels).toEqual(['03.11', '04.11']);
    expect(timeline.series.map((series) => [series.label, series.token, series.shape])).toEqual([
      ['Wpływy', '--sf-accent', 'bar'],
      ['Wypływy', '--sf-text', 'bar'],
    ]);
    expect(timeline.series[1].values).toEqual(['400.00', '300.00']);
    expect(timeline.ariaLabel).toBe(
      'Wpływy i wypływy w czasie, listopad 2026, 01.11–30.11.2026, w zł. Dane w tabeli poniżej.',
    );

    const toggle = button(element, 'Pokaż dane w tabeli');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(toggle.getAttribute('aria-controls')).toBe('pulpit-przeplywy-dane-wykresu');
    await openTable(element, settle);

    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    const table = element.querySelector('#pulpit-przeplywy-dane-wykresu table')!;
    expect(
      Array.from(table.querySelectorAll('thead th')).map((th) => th.textContent?.trim()),
    ).toEqual(['Okres', 'Wpływy', 'Wypływy', 'Zmiana środków']);
    expect(table.querySelectorAll('tbody tr')[1].textContent).toContain(formatPln('-300.00'));
    // „Razem” to wartości kart z API (różne od sumy wierszy fikstury), nie suma w UI.
    const total = text(element, 'razem')!;
    expect(total).toContain(formatPln('1500.00'));
    expect(total).toContain(formatPln('700.00'));
    expect(total).toContain(formatPln('800.00'));
  });

  it('lists the outflows by kind with links, amounts and the sum label (F-CF4)', async () => {
    const { element, fixture } = await render(
      cashFlowReport({
        outflowsByKind: [
          { kind: 'expense', amount: '-200.00' },
          { kind: 'withdrawal', amount: '250.00' },
          { kind: 'refund', amount: '150.00' },
        ],
      }),
    );
    const items = Array.from(element.querySelectorAll('[data-testid="wyplywy-rodzaje"] a'));
    const breakdown = specs(fixture)[1];

    expect(items.map((item) => item.querySelector('span')?.textContent?.trim())).toEqual([
      'Opłacone wydatki',
      'Wypłaty właścicieli',
      'Zwroty',
    ]);
    expect(items[0].textContent).toContain(formatPln('-200.00'));
    expect(items.map((item) => item.getAttribute('href'))).toEqual([
      '/operacje?od=2026-11-03&do=2026-11-30&rodzaj=expense',
      '/operacje?od=2026-11-03&do=2026-11-30&rodzaj=withdrawal',
      '/operacje?od=2026-11-03&do=2026-11-30&rodzaj=refund',
    ]);
    expect(text(element, 'suma-pozycji')).toBe(
      'Suma pozycji równa się wypływom. Ujemne opłacone wydatki pomniejszają swoją pozycję.',
    );
    expect(breakdown.type).toBe('horizontalBar');
    expect(breakdown.labels).toEqual(['Opłacone wydatki', 'Wypłaty właścicieli', 'Zwroty']);
    expect(breakdown.series[0].token).toBe('--sf-text');
    expect(breakdown.ariaLabel).toBe(
      'Wypływy według rodzaju w zł. Wartości są też na liście przy wykresie.',
    );
  });

  it('shows recent operations like the operations list without the method (F-CF5)', async () => {
    const { element } = await render();
    const section = element.querySelector('section[aria-labelledby="pulpit-ostatnie-operacje"]')!;
    const headers = Array.from(section.querySelectorAll('th')).map((th) => th.textContent?.trim());
    const rows = Array.from(section.querySelectorAll('tbody tr'));
    const cells = (row: Element) =>
      Array.from(row.querySelectorAll('td')).map((td) => td.textContent?.trim() ?? '');

    expect(section.querySelector('h2')?.textContent?.trim()).toBe('Ostatnie operacje');
    expect(headers).toEqual(['Data', 'Rodzaj', 'Opis', 'Kierunek', 'Kwota']);
    // `mobileCards`: każda komórka ma też etykietę kolumny (widoczną tylko na telefonie).
    expect(cells(rows[0])).toEqual([
      expect.stringContaining('05.11.2026'),
      expect.stringContaining('Wpłata'),
      expect.stringContaining('ZAM/2026/11/0001'),
      expect.stringContaining('Wpływ'),
      expect.stringContaining(formatPln('1500.00')),
    ]);
    expect(rows[0].querySelector('a')?.getAttribute('href')).toBe('/zamowienia/o1');
    expect(rows[1].querySelector('a')?.getAttribute('href')).toBe('/wydatki/e1');
    expect(cells(rows[1])[3]).toContain('Wypływ');
    expect(cells(rows[1])[4]).toContain(formatPln('-50.00'));
    expect(cells(rows[1])[4]).toContain('Korekta / zwrot od dostawcy');
    expect(section.querySelector('th button')).toBeNull();
    const all = element.querySelector('[data-testid="wszystkie-operacje"]');
    expect(all?.textContent?.trim()).toBe('Pokaż wszystkie operacje');
    expect(all?.getAttribute('href')).toBe('/operacje?od=2026-11-03&do=2026-11-30');
  });

  it('shows real zeros and the empty recent operations state for a period without operations (F-CF5)', async () => {
    const zero = {
      value: '0.00',
      comparison: { status: 'noBasis', base: null, difference: null, percent: null },
    } as const;
    const { element } = await render(
      cashFlowReport({
        kpis: { inflows: zero, outflows: zero, change: zero },
        series: [
          {
            from: '2026-11-03',
            to: '2026-11-03',
            inflows: '0.00',
            outflows: '0.00',
            change: '0.00',
          },
        ],
        outflowsByKind: [
          { kind: 'expense', amount: '0.00' },
          { kind: 'withdrawal', amount: '0.00' },
          { kind: 'refund', amount: '0.00' },
        ],
        recentOperations: [],
      }),
    );

    expect(
      Array.from(element.querySelectorAll('app-kpi-card [data-testid="wartosc"]')).map((value) =>
        value.textContent?.trim(),
      ),
    ).toEqual([formatPln('0.00'), formatPln('0.00'), formatPln('0.00')]);
    expect(element.querySelectorAll('[data-testid="wyplywy-rodzaje"] a')).toHaveLength(3);
    expect(element.textContent).toContain('Brak operacji w wybranym okresie');
  });

  it('shows the before-launch message without cards, charts, tables and links (F-CF6)', async () => {
    const { element } = await render(beforeLaunchCashFlowReport());

    expect(element.querySelector('[role="status"]')?.textContent).toContain(
      'Wybrany okres jest w całości przed datą uruchomienia (01.09.2026). Raporty obejmują dane od tej daty.',
    );
    expect(element.querySelector('app-kpi-card')).toBeNull();
    expect(element.querySelector('canvas')).toBeNull();
    expect(element.querySelector('app-data-table')).toBeNull();
    expect(element.querySelector('a[href^="/operacje"]')).toBeNull();
    expect(element.querySelector('[data-testid="opis-porownania"]')).toBeNull();
  });

  it('never says profit or balance, also in labels and the chart data table (F-CF7)', async () => {
    const { element, settle } = await render(
      cashFlowReport({
        period: reportPeriod({ effectiveFrom: '2026-11-03', launchDate: '2026-11-03' }),
      }),
    );
    const assertNoWords = () => {
      const texts = [
        element.textContent ?? '',
        ...Array.from(element.querySelectorAll('[aria-label], [title]')).flatMap((node) => [
          node.getAttribute('aria-label') ?? '',
          node.getAttribute('title') ?? '',
        ]),
      ].map((value) => value.toLowerCase());
      for (const value of texts) {
        expect(value).not.toContain('zysk');
        expect(value).not.toContain('saldo');
      }
    };

    expect(element.querySelectorAll('canvas[aria-label]')).toHaveLength(2);
    assertNoWords();
    await openTable(element, settle);
    assertNoWords();
  });

  it('gives every value as text and the charts as focusable images (F-CF8)', async () => {
    const { element, settle } = await render();
    const canvases = Array.from(element.querySelectorAll('canvas'));

    expect(canvases).toHaveLength(2);
    for (const canvas of canvases) {
      expect(canvas.getAttribute('role')).toBe('img');
      expect(canvas.getAttribute('tabindex')).toBe('0');
      expect(canvas.getAttribute('aria-label')).not.toBe('');
    }
    expect(element.querySelector('[data-testid="karty"]')?.textContent).toContain(
      formatPln('1500.00'),
    );
    expect(element.querySelector('[data-testid="wyplywy-rodzaje"]')?.textContent).toContain(
      formatPln('250.00'),
    );
    await openTable(element, settle);
    expect(element.querySelector('#pulpit-przeplywy-dane-wykresu tbody')?.textContent).toContain(
      formatPln('1100.00'),
    );
    expect(
      element.querySelector('section[aria-labelledby="pulpit-ostatnie-operacje"] tbody')
        ?.textContent,
    ).toContain(formatPln('1500.00'));
  });
});
