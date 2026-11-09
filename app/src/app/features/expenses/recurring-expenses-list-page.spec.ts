import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { nextRequest, provideTestHttp, respondNetworkError } from '../../../testing/auth-testing';
import { recurringListItem } from '../../../testing/expenses-testing';
import { formatPln } from '../../shared/format/pl-format';
import { RecurringExpenseListItem } from './recurring-expense-models';
import { RecurringExpensesListPage } from './recurring-expenses-list-page';
import { RecurringExpensesService } from './recurring-expenses.service';

describe('RecurringExpensesListPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RecurringExpensesListPage],
      providers: [provideRouter([{ path: 'wydatki/nowy', children: [] }]), provideTestHttp()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(items: readonly RecurringExpenseListItem[]) {
    const fixture = TestBed.createComponent(RecurringExpensesListPage);
    (await nextRequest('/api/jobs')).flush([]);
    (await nextRequest((r) => r.url === '/api/recurring-expenses')).flush({
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

  it('shows the empty registry with the add action leading to the recurring form', async () => {
    const element = await render([]);

    expect(element.querySelector('h1')?.textContent?.trim()).toBe('Wydatki cykliczne');
    expect(element.textContent).toContain('Brak wydatków cyklicznych');
    const add = Array.from(element.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === 'Dodaj wydatek cykliczny',
    ) as HTMLButtonElement;
    add.click();
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/wydatki/nowy?cykliczny=1'));
  });

  it('shows the columns, day explanation, period, next due date and the state badge', async () => {
    const element = await render([
      recurringListItem(),
      recurringListItem({
        id: 'c2',
        description: 'Czynsz',
        dayOfMonth: 31,
        startsOn: '2026-11-01',
        endsOn: '2027-12-31',
        status: 'paused',
        pausedOn: '2026-11-05',
        nextDueDate: null,
      }),
      recurringListItem({ id: 'c3', status: 'ended', nextDueDate: null }),
    ]);
    const rows = Array.from(element.querySelectorAll('tbody tr'));
    const headers = Array.from(element.querySelectorAll('th')).map((th) =>
      (th.textContent ?? '').replace(/[▼▲↕]/g, '').trim(),
    );

    expect(headers).toEqual([
      'Opis',
      'Kategoria',
      'Kwota brutto',
      'Dzień miesiąca',
      'Okres',
      'Najbliższy termin',
      'Stan',
    ]);
    expect(rows[0].textContent).toContain('10.');
    expect(rows[0].textContent).toContain('od 01.09.2026');
    expect(rows[0].textContent).toContain('10.12.2026');
    expect(rows[0].textContent).toContain(formatPln('123.00'));
    expect(rows[0].textContent).toContain('Aktywny');
    expect(rows[0].querySelector('a')?.getAttribute('href')).toBe('/wydatki/cykliczne/c1');
    expect(rows[1].textContent).toContain('31. (w krótszym miesiącu ostatni dzień)');
    expect(rows[1].textContent).toContain('01.11.2026 – 31.12.2027');
    expect(rows[1].textContent).toContain('Brak');
    expect(rows[1].textContent).toContain('Wstrzymany');
    expect(rows[2].textContent).toContain('Zakończony');
    expect(element.textContent).toContain('Wszystkie wydatki');
  });

  it('tells an empty filter result from the empty registry', async () => {
    const element = await render([recurringListItem()]);
    const service = TestBed.inject(RecurringExpensesService);

    const filtered = service.updateFilters({ state: 'ended' });
    (await nextRequest((r) => r.url === '/api/recurring-expenses')).flush({
      items: [],
      page: 1,
      pageSize: 25,
      total: 0,
    });
    await filtered;
    await vi.waitFor(() =>
      expect(element.textContent).toContain('Brak wydatków cyklicznych dla wybranych filtrów'),
    );
    expect(element.textContent).toContain('Wyczyść filtry');
  });

  it('shows the list error with a retry action', async () => {
    const fixture = TestBed.createComponent(RecurringExpensesListPage);
    (await nextRequest('/api/jobs')).flush([]);
    respondNetworkError(await nextRequest((r) => r.url === '/api/recurring-expenses'));
    await vi.waitFor(async () => {
      await fixture.whenStable();
      expect(fixture.nativeElement.textContent).toContain(
        'Nie udało się wczytać wydatków cyklicznych.',
      );
    });
    expect(fixture.nativeElement.textContent).toContain('Spróbuj ponownie');
  });
});
