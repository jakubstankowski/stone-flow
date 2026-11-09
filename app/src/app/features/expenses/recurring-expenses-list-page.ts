import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { formatDate, formatPln } from '../../shared/format/pl-format';
import { Button } from '../../shared/ui/button';
import { DataTable, DataTableColumn, SortState } from '../../shared/ui/data-table';
import { EmptyState } from '../../shared/ui/empty-state';
import { Field } from '../../shared/ui/field';
import { StatusBadge } from '../../shared/ui/status-badge';
import { TableCell } from '../../shared/ui/table-cell';
import { CATEGORY_LABELS } from './expense-models';
import { ExpensesSubnav } from './expenses-subnav';
import {
  RECURRING_STATE_FILTER_LABELS,
  RECURRING_STATUSES,
  RECURRING_STATUS_LABELS,
  RECURRING_STATUS_TONES,
  RecurringExpenseListItem,
  RecurringExpenseStatus,
  dayOfMonthText,
} from './recurring-expense-models';
import { RecurringJobNotice } from './recurring-job-notice';
import {
  RECURRING_SEARCH_MAX_LENGTH,
  RecurringExpensesService,
} from './recurring-expenses.service';

/** „od 01.11.2026” albo „01.11.2026 – 31.12.2027”. */
export function recurringPeriodText(startsOn: string, endsOn: string | null): string {
  return endsOn ? `${formatDate(startsOn)} – ${formatDate(endsOn)}` : `od ${formatDate(startsOn)}`;
}

const COLUMNS: readonly DataTableColumn<RecurringExpenseListItem>[] = [
  { key: 'description', header: 'Opis', sortable: true },
  { key: 'category', header: 'Kategoria', value: (row) => CATEGORY_LABELS[row.category] },
  {
    key: 'grossAmount',
    header: 'Kwota brutto',
    value: (row) => formatPln(row.grossAmount),
    align: 'end',
    sortable: true,
  },
  { key: 'dayOfMonth', header: 'Dzień miesiąca', value: (row) => dayOfMonthText(row.dayOfMonth) },
  {
    key: 'startsOn',
    header: 'Okres',
    value: (row) => recurringPeriodText(row.startsOn, row.endsOn),
    sortable: true,
  },
  {
    key: 'nextDueDate',
    header: 'Najbliższy termin',
    value: (row) => (row.nextDueDate ? formatDate(row.nextDueDate) : 'Brak'),
  },
  { key: 'status', header: 'Stan' },
];

/**
 * Ekran `/wydatki/cykliczne`: definicje z filtrami stanu i tekstu, sortowanie przez API, stan
 * w serwisie. Nad tabelą podsekcja i ostrzeżenie o zadaniu tworzenia wystąpień.
 */
@Component({
  selector: 'app-recurring-expenses-list-page',
  imports: [
    Button,
    DataTable,
    EmptyState,
    ExpensesSubnav,
    Field,
    RecurringJobNotice,
    RouterLink,
    StatusBadge,
    TableCell,
  ],
  templateUrl: './recurring-expenses-list-page.html',
})
export class RecurringExpensesListPage {
  protected readonly recurring = inject(RecurringExpensesService);
  private readonly router = inject(Router);

  /** Komunikat po usunięciu definicji w szczegółach. */
  protected readonly notice = signal<string | null>(this.recurring.takeNotice());

  protected readonly columns = COLUMNS;
  protected readonly rowKey = (row: RecurringExpenseListItem) => row.id;
  protected readonly states = RECURRING_STATUSES;
  protected readonly stateLabels = RECURRING_STATE_FILTER_LABELS;
  protected readonly statusLabels = RECURRING_STATUS_LABELS;
  protected readonly statusTones = RECURRING_STATUS_TONES;
  protected readonly searchMaxLength = RECURRING_SEARCH_MAX_LENGTH;

  constructor() {
    void this.recurring.load();
  }

  protected add(): void {
    void this.router.navigate(['/wydatki', 'nowy'], { queryParams: { cykliczny: 1 } });
  }

  protected onSort(sort: SortState): void {
    void this.recurring.setSort(sort);
  }

  protected onPage(page: number): void {
    void this.recurring.setPage(page);
  }

  protected onState(event: Event): void {
    void this.recurring.updateFilters({ state: valueOf(event) as RecurringExpenseStatus | '' });
  }

  protected onSearch(event: Event): void {
    event.preventDefault();
    const input = (event.target as HTMLFormElement).querySelector<HTMLInputElement>(
      '#cykliczne-szukaj',
    );
    this.applySearch(input?.value ?? '');
  }

  protected onSearchChange(event: Event): void {
    this.applySearch(valueOf(event));
  }

  private applySearch(value: string): void {
    if (value.trim() === this.recurring.filters().search.trim()) {
      return;
    }
    void this.recurring.updateFilters({ search: value });
  }
}

function valueOf(event: Event): string {
  return (event.target as HTMLInputElement | HTMLSelectElement).value;
}
