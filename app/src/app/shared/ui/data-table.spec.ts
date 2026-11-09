import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { formatPln } from '../format/pl-format';
import { ActiveFilter, DataTable, DataTableColumn, SortState } from './data-table';

interface Row {
  id: number;
  description: string;
  amount: string;
}

const COLUMNS: readonly DataTableColumn<Row>[] = [
  { key: 'description', header: 'Opis', value: (row) => row.description, sortable: true },
  {
    key: 'amount',
    header: 'Kwota',
    value: (row) => formatPln(row.amount),
    align: 'end',
    sortable: true,
  },
];

@Component({
  imports: [DataTable],
  template: `<app-data-table
    caption="Wydatki"
    emptyTitle="Brak wydatków w wybranym okresie."
    [columns]="columns"
    [rows]="rows()"
    [rowKey]="rowKey"
    [total]="total()"
    [page]="page()"
    [pageSize]="20"
    [sort]="sort()"
    [filters]="filters()"
    [filterable]="true"
    [loading]="loading()"
    [error]="error()"
    [mobileCards]="mobileCards()"
    (pageChange)="events.push('page:' + $event)"
    (sortChange)="events.push('sort:' + $event.key + ':' + $event.direction)"
    (filterRemove)="events.push('remove:' + $event)"
    (filtersClear)="events.push('clear')"
    (retry)="events.push('retry')"
  >
    <form tableFilters aria-label="Filtry wydatków"><input aria-label="Szukaj" /></form>
  </app-data-table>`,
})
class Host {
  readonly columns = COLUMNS;
  readonly rowKey = (row: Row) => row.id;
  readonly rows = signal<Row[]>([
    { id: 1, description: 'Czynsz', amount: '2500.00' },
    { id: 2, description: 'Paliwo', amount: '-120.5' },
  ]);
  readonly total = signal(45);
  readonly page = signal(3);
  readonly sort = signal<SortState | null>({ key: 'amount', direction: 'asc' });
  readonly filters = signal<ActiveFilter[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly mobileCards = signal(false);
  readonly events: string[] = [];
}

async function setup() {
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  const element = fixture.nativeElement as HTMLElement;
  return { fixture, host: fixture.componentInstance, element };
}

function buttonByText(element: HTMLElement, text: string): HTMLButtonElement {
  const button = Array.from(element.querySelectorAll('button')).find(
    (candidate) => candidate.textContent?.trim() === text,
  );
  if (!button) {
    throw new Error(`Brak przycisku „${text}”`);
  }
  return button;
}

describe('DataTable', () => {
  it('renders rows with formatted, right-aligned amounts in a labelled region', async () => {
    const { element } = await setup();
    const region = element.querySelector('[role="region"]');
    const cells = element.querySelectorAll('tbody td');

    expect(region?.getAttribute('aria-label')).toBe('Wydatki');
    expect(element.querySelector('caption')?.textContent?.trim()).toBe('Wydatki');
    expect(cells[1].textContent?.trim()).toBe('2 500,00 zł');
    expect(cells[1].classList).toContain('text-right');
    expect(cells[1].classList).toContain('tabular-nums');
    expect(cells[3].textContent?.trim()).toBe('-120,50 zł');
    expect(cells[0].classList).toContain('sticky');
  });

  it('shows the range of the last, partial page and disables Next', async () => {
    const { element } = await setup();

    expect(element.textContent).toContain('41–45 z 45');
    expect(buttonByText(element, 'Następna').disabled).toBe(true);
    expect(buttonByText(element, 'Poprzednia').disabled).toBe(false);
  });

  it('emits page changes instead of paging locally', async () => {
    const { host, element } = await setup();
    buttonByText(element, 'Poprzednia').click();
    expect(host.events).toEqual(['page:2']);
  });

  it('disables Previous on the first page and handles a page out of range', async () => {
    const { fixture, host, element } = await setup();
    host.page.set(1);
    await fixture.whenStable();
    expect(buttonByText(element, 'Poprzednia').disabled).toBe(true);
    expect(element.textContent).toContain('1–20 z 45');

    host.page.set(9);
    host.rows.set([]);
    await fixture.whenStable();
    expect(element.textContent).toContain('0 z 45');
  });

  it('exposes the sort state and toggles the direction', async () => {
    const { host, element } = await setup();
    const headers = element.querySelectorAll('th');

    expect(headers[1].getAttribute('aria-sort')).toBe('ascending');
    expect(headers[0].hasAttribute('aria-sort')).toBe(false);

    (headers[1].querySelector('button') as HTMLButtonElement).click();
    (headers[0].querySelector('button') as HTMLButtonElement).click();

    expect(host.events).toEqual(['sort:amount:desc', 'sort:description:asc']);
  });

  it('lists active filters with removal and Clear filters', async () => {
    const { fixture, host, element } = await setup();
    host.filters.set([{ id: 'okres', label: 'Okres: październik 2026' }]);
    await fixture.whenStable();

    const remove = element.querySelector(
      '[aria-label="Usuń filtr: Okres: październik 2026"]',
    ) as HTMLButtonElement;
    remove.click();
    buttonByText(element, 'Wyczyść filtry').click();

    expect(host.events).toEqual(['remove:okres', 'clear']);
  });

  it('projects the filters into a panel toggled on narrow screens', async () => {
    const { fixture, element } = await setup();
    const toggle = buttonByText(element, 'Filtry');
    const panel = element.querySelector(`#${toggle.getAttribute('aria-controls')}`);

    expect(panel?.querySelector('form[aria-label="Filtry wydatków"]')).not.toBeNull();
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(panel?.classList).toContain('hidden');

    toggle.click();
    await fixture.whenStable();

    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(panel?.classList).not.toContain('hidden');
  });

  it('shows the loading state without rows', async () => {
    const { fixture, host, element } = await setup();
    host.loading.set(true);
    await fixture.whenStable();

    expect(element.querySelector('[role="status"]')?.textContent).toContain('Ładowanie…');
    expect(element.querySelector('table')).toBeNull();
  });

  it('shows the error state with retry', async () => {
    const { fixture, host, element } = await setup();
    host.error.set('Nie udało się wczytać wydatków.');
    await fixture.whenStable();

    expect(element.querySelector('[role="alert"]')?.textContent).toContain(
      'Nie udało się wczytać wydatków.',
    );
    buttonByText(element, 'Spróbuj ponownie').click();
    expect(host.events).toEqual(['retry']);
  });

  it('distinguishes an empty register from an empty filter result', async () => {
    const { fixture, host, element } = await setup();
    host.rows.set([]);
    host.total.set(0);
    host.page.set(1);
    await fixture.whenStable();
    expect(element.textContent).toContain('Brak wydatków w wybranym okresie.');
    expect(element.textContent).toContain('0 z 0');

    host.filters.set([{ id: 'kategoria', label: 'Kategoria: Paliwo' }]);
    await fixture.whenStable();
    expect(element.textContent).toContain('Brak wyników dla wybranych filtrów.');
  });

  it('keeps the plain table layout by default', async () => {
    const { element } = await setup();

    expect(element.querySelectorAll('[data-cell-header]')).toHaveLength(0);
    expect(element.querySelector('thead')?.classList.contains('max-md:sr-only')).toBe(false);
    expect(element.querySelector('tbody tr')?.className).toBe('border-b border-base-300');
  });

  it('turns rows into cards with a visible column header in every cell on phones', async () => {
    const { fixture, host, element } = await setup();
    host.mobileCards.set(true);
    await fixture.whenStable();

    expect(element.querySelector('thead')?.classList.contains('max-md:sr-only')).toBe(true);
    const rows = element.querySelectorAll('tbody tr');
    expect(rows).toHaveLength(2);
    for (const row of Array.from(rows)) {
      expect(row.classList.contains('max-md:block')).toBe(true);
      const headers = Array.from(row.querySelectorAll('td [data-cell-header]'));
      expect(headers.map((header) => header.textContent?.trim())).toEqual(['Opis', 'Kwota']);
      for (const header of headers) {
        expect(header.classList.contains('md:hidden')).toBe(true);
      }
    }
    expect(rows[0].querySelector('td')?.textContent).toContain('Czynsz');
  });
});
