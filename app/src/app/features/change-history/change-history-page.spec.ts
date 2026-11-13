import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { nextRequest, provideTestHttp, respondNetworkError } from '../../../testing/auth-testing';
import { changeHistoryItem, changeHistoryPage } from '../../../testing/change-history-testing';
import { formatDateTime, formatPln } from '../../shared/format/pl-format';
import {
  BUSINESS_DATE_HINT,
  ChangeHistoryItem,
  ChangeHistoryPage as Page,
} from './change-history-models';
import { ChangeHistoryPage } from './change-history-page';

const ITEMS: ChangeHistoryItem[] = [
  changeHistoryItem({
    id: '11',
    occurredAt: '2026-11-05T12:00:00.0000000Z',
    operation: 'updated',
    changes: [
      { field: 'grossAmount', oldValue: '100.00', newValue: '1234.50', isPersonalData: false },
      { field: 'category', oldValue: 'cutting', newValue: 'marketing', isPersonalData: false },
      { field: 'payer', oldValue: 'A', newValue: 'B', isPersonalData: true },
      { field: 'futureField', oldValue: 'x', newValue: 'y', isPersonalData: false },
    ],
  }),
  changeHistoryItem({
    id: '12',
    author: { kind: 'wooCommerceSync' },
    operation: 'created',
    object: {
      type: 'order',
      id: 'o1',
      label: 'Zamówienie ZAM/2026/11/0001',
      businessDate: '2026-10-28',
    },
    changes: [{ field: 'channel', oldValue: null, newValue: 'shop', isPersonalData: false }],
  }),
  changeHistoryItem({
    id: '13',
    author: { kind: 'automatedJob' },
    operation: 'deleted',
    object: {
      type: 'payment',
      id: 'p1',
      label: 'Wpłata – ZAM/2026/11/0001',
      parent: { type: 'order', id: 'o1' },
      deleted: true,
    },
    changes: [
      { field: 'amount', oldValue: '300.00', newValue: null, isPersonalData: false },
      { field: 'method', oldValue: 'cashOnDelivery', newValue: null, isPersonalData: false },
    ],
  }),
  changeHistoryItem({
    id: '14',
    object: {
      type: 'refund',
      id: 'r1',
      label: 'Zwrot – ZAM/2026/11/0001',
      parent: { type: 'order', id: 'o2' },
    },
  }),
  changeHistoryItem({
    id: '15',
    object: {
      type: 'salesCorrection',
      id: 'k1',
      label: 'Korekta – ZAM/2026/11/0001',
      parent: { type: 'order', id: 'o3' },
    },
  }),
  changeHistoryItem({
    id: '16',
    object: {
      type: 'recurringExpenseDefinition',
      id: 'd1',
      label: 'Wydatek cykliczny – Marketing',
    },
  }),
  changeHistoryItem({
    id: '17',
    object: { type: 'ownerWithdrawal', id: 'w1', label: 'Wypłata – Właściciel A' },
  }),
];

describe('ChangeHistoryPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ChangeHistoryPage],
      providers: [provideRouter([{ path: '**', children: [] }]), provideTestHttp()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(page: Page = changeHistoryPage()) {
    const fixture = TestBed.createComponent(ChangeHistoryPage);
    (await nextRequest((r) => r.url === '/api/change-history')).flush(page);
    const element = fixture.nativeElement as HTMLElement;
    const settle = async (check: () => void) =>
      vi.waitFor(async () => {
        await fixture.whenStable();
        check();
      });
    await settle(() => expect(element.querySelector('.skeleton')).toBeNull());
    return { fixture, element, settle };
  }

  /** Wiersze listy (bez wierszy rozwiniętych tabel zmian). */
  function rows(element: HTMLElement): HTMLTableRowElement[] {
    return Array.from(
      element.querySelectorAll<HTMLTableRowElement>('table.table:not(.table-sm) > tbody > tr'),
    );
  }

  function cell(row: HTMLTableRowElement, index: number): HTMLTableCellElement {
    return row.querySelectorAll<HTMLTableCellElement>(':scope > td')[index];
  }

  /** Tekst komórki bez nagłówka kolumny widocznego tylko w układzie kart. */
  function cellText(row: HTMLTableRowElement, index: number): string {
    const copy = cell(row, index).cloneNode(true) as HTMLElement;
    copy.querySelectorAll('[data-cell-header]').forEach((header) => header.remove());
    return copy.textContent?.trim() ?? '';
  }

  function buttonByText(root: Element, text: string): HTMLButtonElement | undefined {
    return Array.from(root.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === text,
    );
  }

  it('is read only: a heading without add or edit actions', async () => {
    const { element } = await render(changeHistoryPage({ items: ITEMS }));

    expect(element.querySelector('h1')?.textContent?.trim()).toBe('Historia zmian');
    expect(element.querySelector('.btn-primary')).toBeNull();
    const texts = Array.from(element.querySelectorAll('button')).map(
      (b) => b.textContent?.trim() ?? '',
    );
    expect(texts.some((text) => /Dodaj|Edytuj|Usuń wpis|Zapisz/.test(text))).toBe(false);
  });

  it('shows the columns with time, author, object, operation and the change summary', async () => {
    const { element } = await render(changeHistoryPage({ items: ITEMS }));
    const headers = Array.from(
      element.querySelectorAll('table.table:not(.table-sm) > thead th'),
    ).map((th) => (th.textContent ?? '').replace(/[▼▲↕]/g, '').trim());
    const [updated, created, deleted] = rows(element);

    expect(headers).toEqual(['Czas', 'Autor', 'Obiekt', 'Operacja', 'Zakres zmiany']);
    expect(element.querySelectorAll('thead th button')).toHaveLength(1);
    const time = cell(updated, 0).querySelector('time');
    expect(time?.getAttribute('datetime')).toBe('2026-11-05T12:00:00.0000000Z');
    expect(time?.textContent?.trim()).toBe(formatDateTime('2026-11-05T12:00:00.0000000Z'));
    expect(cellText(updated, 1)).toBe('Właściciel');
    expect(cellText(created, 1)).toBe('Synchronizacja WooCommerce');
    expect(cellText(deleted, 1)).toBe('Zadanie automatyczne');

    expect(cell(updated, 2).querySelector('.sr-only')?.textContent?.trim()).toBe('Wydatek:');
    expect(cell(updated, 2).textContent).toContain('Wydatek – Cięcie');
    expect(cell(updated, 2).textContent).toContain('28.10.2026');

    const badge = (row: HTMLTableRowElement) => cell(row, 3).querySelector('.badge');
    expect(badge(updated)?.textContent?.trim()).toBe('Zmiana');
    expect(badge(updated)?.className).toContain('text-(--sf-info)');
    expect(badge(created)?.textContent?.trim()).toBe('Utworzenie');
    expect(badge(deleted)?.textContent?.trim()).toBe('Usunięcie');
    expect(badge(deleted)?.className).toContain('text-(--sf-warning)');

    const summary = Array.from(cell(updated, 4).querySelectorAll('li')).map((li) =>
      li.textContent?.trim(),
    );
    expect(summary).toEqual([
      `Kwota brutto: ${formatPln('100.00')} → ${formatPln('1234.50')}`,
      'Kategoria: Cięcie → Marketing',
      'i 2 więcej',
    ]);
    expect(cell(created, 4).querySelector('li')?.textContent?.trim()).toBe('Kanał: Sklep');
    expect(cell(deleted, 4).querySelectorAll('li')[1].textContent?.trim()).toBe('Metoda: Pobranie');
  });

  it('links every existing object to its page and marks a deleted one without a link', async () => {
    const { element } = await render(changeHistoryPage({ items: ITEMS }));
    const links = rows(element).map(
      (row) => cell(row, 2).querySelector('a')?.getAttribute('href') ?? null,
    );

    expect(links).toEqual([
      '/wydatki/e1',
      '/zamowienia/o1',
      null,
      '/zamowienia/o2',
      '/zamowienia/o3',
      '/wydatki/cykliczne/d1',
      '/operacje/wyplaty/w1',
    ]);
    const deleted = cell(rows(element)[2], 2);
    expect(deleted.textContent).toContain('Wpłata – ZAM/2026/11/0001');
    const marker = deleted.querySelector('.badge');
    expect(marker?.textContent?.trim()).toBe('Usunięto');
    expect(marker?.className).toContain('text-(--sf-warning)');
  });

  it('expands the change table with columns matching the operation and keeps focus on the button', async () => {
    const { element, settle } = await render(changeHistoryPage({ items: ITEMS }));
    const [updated, created, deleted] = rows(element);
    const show = buttonByText(cell(updated, 4), 'Pokaż zmiany')!;

    expect(show.getAttribute('aria-expanded')).toBe('false');
    expect(show.getAttribute('aria-controls')).toBe('zmiany-11');
    expect(show.getAttribute('aria-label')).toMatch(
      /^Pokaż zmiany: .+, Zmiana, \d{2}\.\d{2}\.\d{4}/,
    );
    expect(element.querySelector('#zmiany-11')?.hasAttribute('hidden')).toBe(true);

    show.focus();
    show.click();
    await settle(() => expect(show.getAttribute('aria-expanded')).toBe('true'));
    const panel = element.querySelector('#zmiany-11')!;
    expect(panel.hasAttribute('hidden')).toBe(false);
    expect(show.textContent?.trim()).toBe('Ukryj zmiany');
    const table = panel.querySelector('table')!;
    expect(table.querySelector('caption')?.textContent).toContain('Zmiany:');
    expect(
      Array.from(table.querySelectorAll('thead th')).map((th) => [
        th.textContent?.trim(),
        th.getAttribute('scope'),
      ]),
    ).toEqual([
      ['Pole', 'col'],
      ['Było', 'col'],
      ['Jest', 'col'],
    ]);
    const body = Array.from(table.querySelectorAll(':scope > tbody > tr')).map((tr) =>
      Array.from(tr.children).map((c) => c.textContent?.trim()),
    );
    expect(body).toEqual([
      ['Kwota brutto', formatPln('100.00'), formatPln('1234.50')],
      ['Kategoria', 'Cięcie', 'Marketing'],
      ['Płatnik', 'A', 'B'],
      ['futureField', 'x', 'y'],
    ]);
    expect(table.querySelector(':scope > tbody th')?.getAttribute('scope')).toBe('row');

    show.click();
    await settle(() => expect(show.getAttribute('aria-expanded')).toBe('false'));
    expect(document.activeElement).toBe(show);
    expect(panel.hasAttribute('hidden')).toBe(true);

    buttonByText(cell(created, 4), 'Pokaż zmiany')!.click();
    buttonByText(cell(deleted, 4), 'Pokaż zmiany')!.click();
    await settle(() => expect(element.querySelector('#zmiany-13 table')).not.toBeNull());
    const heads = (id: string) =>
      Array.from(element.querySelectorAll(`#${id} thead th`)).map((th) => th.textContent?.trim());
    expect(heads('zmiany-12')).toEqual(['Pole', 'Jest']);
    expect(heads('zmiany-13')).toEqual(['Pole', 'Było']);
    expect(element.querySelector('#zmiany-13 table > tbody > tr')?.textContent).toContain(
      formatPln('300.00'),
    );
  });

  it('has the filters with the business date hint and seven object types', async () => {
    const { element } = await render();
    const labels = Array.from(element.querySelectorAll('app-field label')).map((label) =>
      label.textContent?.replace(/\s+/g, ' ').trim(),
    );
    const types = Array.from(element.querySelectorAll('fieldset label')).map((label) =>
      label.textContent?.trim(),
    );

    expect(labels).toEqual([
      'Zmiana od (wymagane)',
      'Zmiana do (wymagane)',
      'Data biznesowa od',
      'Data biznesowa do',
    ]);
    expect(element.textContent).toContain(BUSINESS_DATE_HINT);
    expect(element.querySelector('#historia-biznesowa-od')?.getAttribute('aria-describedby')).toBe(
      'historia-biznesowa-od-pomoc',
    );
    expect(element.querySelector('fieldset legend')?.textContent?.trim()).toBe('Rodzaj obiektu');
    expect(types).toEqual([
      'Zamówienie',
      'Wpłata',
      'Zwrot',
      'Korekta sprzedaży',
      'Wydatek',
      'Wydatek cykliczny',
      'Wypłata właściciela',
    ]);
  });

  it('shows the empty register without Clear filters and the filtered empty result with it', async () => {
    const { element, settle } = await render();

    expect(element.textContent).toContain('Nie ma jeszcze żadnych zmian.');
    expect(buttonByText(element, 'Wyczyść filtry')).toBeUndefined();

    const expense = element.querySelectorAll<HTMLInputElement>('fieldset input')[4];
    expense.checked = true;
    expense.dispatchEvent(new Event('change', { bubbles: true }));
    const request = await nextRequest((r) => r.url === '/api/change-history');
    expect(request.request.params.getAll('objectType')).toEqual(['expense']);
    request.flush(changeHistoryPage());

    await settle(() => expect(element.textContent).toContain('Brak zmian dla wybranych filtrów.'));
    expect(element.textContent).toContain('Rodzaj: Wydatek');
    expect(buttonByText(element, 'Wyczyść filtry')).toBeDefined();
  });

  it('shows a reversed business date range at the field without asking the API', async () => {
    const { element, settle } = await render();
    const from = element.querySelector<HTMLInputElement>('#historia-biznesowa-od')!;
    const to = element.querySelector<HTMLInputElement>('#historia-biznesowa-do')!;

    to.value = '2026-10-01';
    to.dispatchEvent(new Event('change'));
    (await nextRequest((r) => r.url === '/api/change-history')).flush(changeHistoryPage());
    from.value = '2026-10-31';
    from.dispatchEvent(new Event('change'));

    await settle(() =>
      expect(from.closest('app-field')?.textContent).toContain(
        'Data „od” nie może być późniejsza niż data „do”.',
      ),
    );
    http.expectNone((r) => r.url === '/api/change-history');
  });

  it('shows the list error with a retry action', async () => {
    const fixture = TestBed.createComponent(ChangeHistoryPage);
    respondNetworkError(await nextRequest((r) => r.url === '/api/change-history'));
    const element = fixture.nativeElement as HTMLElement;
    await vi.waitFor(async () => {
      await fixture.whenStable();
      expect(element.textContent).toContain('Nie udało się wczytać historii zmian.');
    });
    buttonByText(element, 'Spróbuj ponownie')!.click();
    (await nextRequest((r) => r.url === '/api/change-history')).flush(changeHistoryPage());
  });

  it('shows the loading state while the list is read', async () => {
    const fixture = TestBed.createComponent(ChangeHistoryPage);
    const request = await nextRequest((r) => r.url === '/api/change-history');
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('.skeleton')).not.toBeNull();
    request.flush(changeHistoryPage());
  });

  it('shows an anonymization as a warning with only the new values (SF-22)', async () => {
    const anonymization = changeHistoryItem({
      id: '21',
      operation: 'anonymized',
      object: { type: 'order', id: 'o1', label: 'ZR-2026-0001' },
      changes: [
        {
          field: 'anonymizedFields',
          oldValue: null,
          newValue: '["customerName","notes"]',
          isPersonalData: false,
        },
        { field: 'retentionMonths', oldValue: null, newValue: '60', isPersonalData: false },
      ],
    });
    const earlier = changeHistoryItem({
      id: '22',
      operation: 'updated',
      object: { type: 'order', id: 'o1', label: 'ZR-2026-0001' },
      changes: [
        {
          field: 'customerName',
          oldValue: '[zanonimizowano]',
          newValue: '[zanonimizowano]',
          isPersonalData: true,
        },
      ],
    });
    const { element, settle } = await render(
      changeHistoryPage({ items: [anonymization, earlier] }),
    );
    const [first, second] = rows(element);

    const badge = cell(first, 3).querySelector('.badge');
    expect(badge?.textContent?.trim()).toBe('Anonimizacja');
    expect(badge?.className).toContain('text-(--sf-warning)');
    expect(cellText(first, 4)).toContain(
      'Zanonimizowane pola: Imię i nazwisko lub nazwa firmy, Uwagi do realizacji',
    );

    buttonByText(cell(first, 4), 'Pokaż zmiany')!.click();
    buttonByText(cell(second, 4), 'Pokaż zmiany')!.click();
    await settle(() => expect(element.querySelector('#zmiany-22 table')).not.toBeNull());
    const heads = (id: string) =>
      Array.from(element.querySelectorAll(`#${id} thead th`)).map((th) => th.textContent?.trim());
    expect(heads('zmiany-21')).toEqual(['Pole', 'Jest']);
    expect(heads('zmiany-22')).toEqual(['Pole', 'Było', 'Jest']);
    expect(element.querySelector('#zmiany-21 tbody')?.textContent).toContain(
      'Okres retencji (miesiące)',
    );
    expect(element.querySelector('#zmiany-22 tbody')?.textContent).toContain('[zanonimizowano]');
  });
});
