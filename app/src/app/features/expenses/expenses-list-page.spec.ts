import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { nextRequest, provideTestHttp, respondNetworkError } from '../../../testing/auth-testing';
import { expenseListItem } from '../../../testing/expenses-testing';
import { NO_DATA, formatPln } from '../../shared/format/pl-format';
import { ExpenseListItem } from './expense-models';
import { ExpensesListPage } from './expenses-list-page';
import { ExpensesService } from './expenses.service';

describe('ExpensesListPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ExpensesListPage],
      providers: [provideRouter([]), provideTestHttp()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(items: readonly ExpenseListItem[]) {
    const fixture = TestBed.createComponent(ExpensesListPage);
    (await nextRequest((r) => r.url === '/api/expenses')).flush({
      items,
      page: 1,
      pageSize: 25,
      total: items.length,
    });
    await vi.waitFor(async () => {
      await fixture.whenStable();
      expect(fixture.nativeElement.querySelector('.skeleton')).toBeNull();
    });
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the empty registry with the add action', async () => {
    const element = await render([]);

    expect(element.textContent).toContain('Brak wydatków');
    expect(element.textContent).toContain('Dodaj wydatek');
    expect(element.querySelector('table')).toBeNull();
  });

  it('shows date columns, the signed amount with the correction label and state badges', async () => {
    const element = await render([
      expenseListItem({ id: 'w1', grossAmount: '-200.00', overdue: true, dueDate: '2026-11-01' }),
      expenseListItem({
        id: 'w2',
        description: 'Reklama',
        category: 'marketing',
        paidOn: '2026-11-03',
        paymentStatus: 'paid',
        dueDate: null,
      }),
    ]);
    const headers = Array.from(element.querySelectorAll('th')).map((th) =>
      (th.textContent ?? '').replace(/[▼▲↕]/g, '').trim(),
    );
    const rows = Array.from(element.querySelectorAll('tbody tr'));

    expect(headers).toEqual(
      expect.arrayContaining(['Data kosztu', 'Termin płatności', 'Data zapłaty', 'Stan']),
    );
    expect(rows[0].textContent).toContain(formatPln('-200.00'));
    expect(rows[0].textContent).toContain('Korekta / zwrot od dostawcy');
    expect(rows[0].textContent).toContain('Planowany');
    expect(rows[0].textContent).toContain('Po terminie');
    expect(rows[1].textContent).toContain('Opłacony');
    expect(rows[1].textContent).not.toContain('Po terminie');
    expect(rows[1].textContent).toContain('03.11.2026');
    expect(rows[0].querySelector('a')?.getAttribute('href')).toBe('/wydatki/w1');
  });

  it('shows missing counterparty, due date and payment date as „Brak danych”', async () => {
    const element = await render([
      expenseListItem({ counterparty: null, dueDate: null, paidOn: null }),
    ]);
    const cells = Array.from(element.querySelectorAll('tbody td')).map((td) =>
      td.textContent?.trim(),
    );

    expect(cells.filter((text) => text === NO_DATA)).toHaveLength(3);
    expect(element.querySelector('tbody')?.textContent).not.toContain('—');
  });

  it('tells an empty period from other empty filter results', async () => {
    const element = await render([expenseListItem()]);
    const service = TestBed.inject(ExpensesService);
    const fixture = () => element;

    const byDate = service.updateFilters({ costDateFrom: '2027-01-01' });
    (await nextRequest((r) => r.url === '/api/expenses')).flush({
      items: [],
      page: 1,
      pageSize: 25,
      total: 0,
    });
    await byDate;
    await vi.waitFor(() =>
      expect(fixture().textContent).toContain('Brak wydatków w wybranym okresie'),
    );

    const byCategory = service.updateFilters({ costDateFrom: '', categories: ['ppeTax'] });
    (await nextRequest((r) => r.url === '/api/expenses')).flush({
      items: [],
      page: 1,
      pageSize: 25,
      total: 0,
    });
    await byCategory;
    await vi.waitFor(() =>
      expect(fixture().textContent).toContain('Brak wydatków dla wybranych filtrów'),
    );
    expect(fixture().textContent).toContain('Wyczyść filtry');
  });

  it('shows the list error with a retry action', async () => {
    const fixture = TestBed.createComponent(ExpensesListPage);
    respondNetworkError(await nextRequest((r) => r.url === '/api/expenses'));
    await vi.waitFor(async () => {
      await fixture.whenStable();
      expect(fixture.nativeElement.textContent).toContain('Nie udało się wczytać wydatków.');
    });
    expect(fixture.nativeElement.textContent).toContain('Spróbuj ponownie');
  });
});
