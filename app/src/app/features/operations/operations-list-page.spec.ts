import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import {
  nextRequest,
  provideTestHttp,
  respondNetworkError,
  respondProblem,
} from '../../../testing/auth-testing';
import { operationItem, operationListPage } from '../../../testing/operations-testing';
import { formatPln } from '../../shared/format/pl-format';
import { OperationListPage } from './operation-models';
import { OperationsListPage } from './operations-list-page';
import { OperationsService } from './operations.service';

const ITEMS = [
  operationItem(),
  operationItem({
    kind: 'refund',
    id: 'r1',
    direction: 'outflow',
    amount: '200.00',
    refundKind: 'moneyReturn',
    method: 'blik',
  }),
  operationItem({
    kind: 'expense',
    id: 'e1',
    direction: 'outflow',
    amount: '-50.00',
    method: null,
    description: 'Zwrot za cięcie',
    category: 'cutting',
    sourceType: 'expense',
    sourceId: 'w1',
    sourceLabel: 'Zwrot za cięcie',
  }),
  operationItem({
    kind: 'withdrawal',
    id: 'wp1',
    direction: 'outflow',
    amount: '2000.00',
    method: 'cash',
    description: 'Właściciel A',
    sourceType: 'withdrawal',
    sourceId: 'wp1',
    sourceLabel: 'Właściciel A',
  }),
];

describe('OperationsListPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [OperationsListPage],
      providers: [provideRouter([{ path: '**', children: [] }]), provideTestHttp()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(page: OperationListPage = operationListPage()) {
    const fixture = TestBed.createComponent(OperationsListPage);
    (await nextRequest((r) => r.url === '/api/operations')).flush(page);
    const element = fixture.nativeElement as HTMLElement;
    const settle = async (check: () => void) =>
      vi.waitFor(async () => {
        await fixture.whenStable();
        check();
      });
    await settle(() => expect(element.querySelector('.skeleton')).toBeNull());
    return { fixture, element, settle };
  }

  it('has the add withdrawal action as the main action', async () => {
    const { element } = await render();
    const add = Array.from(element.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === 'Dodaj wypłatę',
    ) as HTMLButtonElement;

    expect(element.querySelector('h1')?.textContent?.trim()).toBe('Operacje');
    expect(add.className).toContain('btn-primary');
    add.click();
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/operacje/wyplaty/nowa'));
  });

  it('shows columns with kind, direction, method and the correction label as text', async () => {
    const { element } = await render(operationListPage({ items: ITEMS }));
    const headers = Array.from(element.querySelectorAll('th')).map((th) =>
      (th.textContent ?? '').replace(/[▼▲↕]/g, '').trim(),
    );
    const rows = Array.from(element.querySelectorAll('tbody tr'));
    const cells = (row: Element) =>
      Array.from(row.querySelectorAll('td')).map((td) => td.textContent?.trim() ?? '');

    expect(headers).toEqual(['Data', 'Rodzaj', 'Opis', 'Kierunek', 'Kwota', 'Metoda']);
    expect(element.querySelectorAll('th button')).toHaveLength(1);
    expect(cells(rows[0])).toEqual([
      '05.11.2026',
      'Wpłata',
      'ZAM/2026/11/0001',
      'Wpływ',
      formatPln('1000.00'),
      'Przelew',
    ]);
    expect(cells(rows[1])[1]).toBe('Zwrot pieniędzy');
    expect(cells(rows[1])[3]).toBe('Wypływ');
    expect(cells(rows[1])[4]).toBe(formatPln('200.00'));
    expect(cells(rows[2])[4]).toContain(formatPln('-50.00'));
    expect(cells(rows[2])[4]).toContain('Korekta / zwrot od dostawcy');
    expect(cells(rows[2])[5]).toBe('Nie podano');
    expect(cells(rows[3])[1]).toBe('Wypłata właściciela');
    expect(cells(rows[3])[4]).not.toContain('Korekta');
  });

  it('links the description to the source with the label and the expense category', async () => {
    const { element } = await render(operationListPage({ items: ITEMS }));
    const links = Array.from(element.querySelectorAll('tbody a')).map((a) => [
      a.getAttribute('href'),
      a.textContent?.trim(),
    ]);

    expect(links).toEqual([
      ['/zamowienia/o1', 'ZAM/2026/11/0001'],
      ['/zamowienia/o1', 'ZAM/2026/11/0001'],
      ['/wydatki/w1', 'Zwrot za cięcie'],
      ['/operacje/wyplaty/wp1', 'Właściciel A'],
    ]);
    const rows = element.querySelectorAll('tbody tr');
    expect(rows[2].textContent).toContain('Cięcie');
    expect(rows[0].querySelectorAll('td')[2].querySelector('span')).toBeNull();
  });

  it('shows the totals from the API with their sign and the basis label', async () => {
    const { element } = await render(
      operationListPage({
        items: ITEMS,
        totals: { inflows: '0.00', outflows: '-50.00', change: '50.00' },
      }),
    );

    expect(element.querySelector('[data-testid="wplywy"]')?.textContent?.trim()).toBe(
      formatPln('0.00'),
    );
    expect(element.querySelector('[data-testid="wyplywy"]')?.textContent?.trim()).toBe(
      formatPln('-50.00'),
    );
    expect(element.querySelector('[data-testid="zmiana"]')?.textContent?.trim()).toBe(
      formatPln('50.00'),
    );
    expect(element.textContent).toContain('Kwoty brutto w PLN według daty operacji pieniężnej.');
  });

  it('labels the kind filter and filters by kind with its own empty title', async () => {
    const { element, settle } = await render();
    const labels = Array.from(element.querySelectorAll('fieldset label')).map((label) =>
      label.textContent?.trim(),
    );

    expect(element.querySelector('fieldset legend')?.textContent?.trim()).toBe('Rodzaj');
    expect(labels).toEqual(['Wpłata', 'Zwrot', 'Opłacony wydatek', 'Wypłata właściciela']);
    expect(element.textContent).toContain('Brak operacji w wybranym okresie');

    const refund = element.querySelectorAll<HTMLInputElement>('fieldset input')[1];
    refund.checked = true;
    refund.dispatchEvent(new Event('change', { bubbles: true }));
    const request = await nextRequest((r) => r.url === '/api/operations');
    expect(request.request.params.getAll('kind')).toEqual(['refund']);
    request.flush(operationListPage());

    await settle(() =>
      expect(element.textContent).toContain('Brak operacji wybranych rodzajów w wybranym okresie'),
    );
    expect(element.textContent).toContain('Rodzaj: Zwrot');
  });

  it('shows the launch date notice under the filters', async () => {
    // Okres ustawiony wcześniej (stan serwisu przetrwa powrót na listę).
    const period = TestBed.inject(OperationsService).updatePeriod({
      from: '2026-10-01',
      to: '2026-11-30',
    });
    (await nextRequest((r) => r.url === '/api/operations')).flush(operationListPage());
    await period;
    const { element } = await render(
      operationListPage({ effectiveFrom: '2026-11-03', launchDate: '2026-11-03' }),
    );

    expect(element.querySelector('[data-testid="uruchomienie"]')?.textContent?.trim()).toBe(
      'Operacje liczone od 03.11.2026 (data uruchomienia).',
    );
  });

  it('shows too many operations at the period filter without totals', async () => {
    const fixture = TestBed.createComponent(OperationsListPage);
    respondProblem(await nextRequest((r) => r.url === '/api/operations'), 422, {
      code: 'operations_range_too_large',
      detail: 'Za dużo operacji w wybranym okresie. Wybierz krótszy okres.',
    });
    const element = fixture.nativeElement as HTMLElement;
    await vi.waitFor(async () => {
      await fixture.whenStable();
      expect(element.querySelector('#operacje-od')?.closest('app-field')?.textContent).toContain(
        'Za dużo operacji w wybranym okresie.',
      );
    });
    expect(element.querySelector('[data-testid="wyplywy"]')).toBeNull();
    expect(element.textContent).not.toContain('Brak operacji w wybranym okresie');
  });

  it('shows the list error with a retry action and no totals', async () => {
    const fixture = TestBed.createComponent(OperationsListPage);
    respondNetworkError(await nextRequest((r) => r.url === '/api/operations'));
    const element = fixture.nativeElement as HTMLElement;
    await vi.waitFor(async () => {
      await fixture.whenStable();
      expect(element.textContent).toContain('Nie udało się wczytać operacji.');
    });
    expect(element.textContent).toContain('Spróbuj ponownie');
    expect(element.querySelector('[data-testid="wplywy"]')).toBeNull();
  });

  it('shows the loading state while the list is read', async () => {
    const fixture = TestBed.createComponent(OperationsListPage);
    const request = await nextRequest((r) => r.url === '/api/operations');
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('.skeleton')).not.toBeNull();
    request.flush(operationListPage());
  });
});
