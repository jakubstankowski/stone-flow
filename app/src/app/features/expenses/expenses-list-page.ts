import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { NO_DATA, formatDate, formatPln } from '../../shared/format/pl-format';
import { Button } from '../../shared/ui/button';
import { DataTable, DataTableColumn, SortState } from '../../shared/ui/data-table';
import { EmptyState } from '../../shared/ui/empty-state';
import { Field } from '../../shared/ui/field';
import { StatusBadge } from '../../shared/ui/status-badge';
import { TableCell } from '../../shared/ui/table-cell';
import {
  CATEGORIES,
  CATEGORY_LABELS,
  ExpenseCategory,
  ExpenseFilterKey,
  ExpenseListItem,
  ExpenseSignFilter,
  ExpenseStateFilter,
  NEGATIVE_AMOUNT_LABEL,
  OVERDUE_LABEL,
  PAYMENT_STATUS_LABELS,
  PAYMENT_STATUS_TONES,
  SIGN_FILTERS,
  SIGN_FILTER_LABELS,
  STATE_FILTERS,
  STATE_FILTER_LABELS,
  isNegativeAmount,
} from './expense-models';
import { DATE_RANGE_MESSAGE, ExpensesService, SEARCH_MAX_LENGTH } from './expenses.service';
import { ExpensesSubnav } from './expenses-subnav';
import { RECURRING_MARK } from './recurring-expense-models';
import { RecurringJobNotice } from './recurring-job-notice';

const COLUMNS: readonly DataTableColumn<ExpenseListItem>[] = [
  {
    key: 'costDate',
    header: 'Data kosztu',
    value: (row) => formatDate(row.costDate),
    sortable: true,
  },
  { key: 'description', header: 'Opis' },
  { key: 'category', header: 'Kategoria', value: (row) => CATEGORY_LABELS[row.category] },
  { key: 'counterparty', header: 'Kontrahent', value: (row) => row.counterparty ?? NO_DATA },
  {
    key: 'grossAmount',
    header: 'Kwota brutto',
    value: (row) => formatPln(row.grossAmount),
    align: 'end',
    sortable: true,
  },
  {
    key: 'dueDate',
    header: 'Termin płatności',
    value: (row) => formatDate(row.dueDate),
    sortable: true,
  },
  {
    key: 'paidOn',
    header: 'Data zapłaty',
    value: (row) => formatDate(row.paidOn),
    sortable: true,
  },
  { key: 'state', header: 'Stan' },
];

/**
 * Ekran `/wydatki`: jedna strona z API, filtry przy tabeli (na telefonie w panelu „Filtry”),
 * sortowanie przez API. Pusta ewidencja (bez filtrów) pokazuje „Brak wydatków” z akcją.
 * Parametr `wydatekCykliczny` (odczytany raz) ustawia filtr wystąpień jednej definicji; wiersz
 * wystąpienia ma pod opisem tekst „Cykliczny” (SF-16).
 */
@Component({
  selector: 'app-expenses-list-page',
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
  templateUrl: './expenses-list-page.html',
})
export class ExpensesListPage {
  protected readonly expenses = inject(ExpensesService);
  private readonly router = inject(Router);

  /** Komunikat po usunięciu wydatku w szczegółach. */
  protected readonly notice = signal<string | null>(this.expenses.takeNotice());

  protected readonly columns = COLUMNS;
  protected readonly rowKey = (row: ExpenseListItem) => row.id;
  protected readonly categories = CATEGORIES;
  protected readonly categoryLabels = CATEGORY_LABELS;
  protected readonly stateFilters = STATE_FILTERS;
  protected readonly stateFilterLabels = STATE_FILTER_LABELS;
  protected readonly signFilters = SIGN_FILTERS;
  protected readonly signFilterLabels = SIGN_FILTER_LABELS;
  protected readonly statusLabels = PAYMENT_STATUS_LABELS;
  protected readonly statusTones = PAYMENT_STATUS_TONES;
  protected readonly overdueLabel = OVERDUE_LABEL;
  protected readonly negativeLabel = NEGATIVE_AMOUNT_LABEL;
  protected readonly recurringMark = RECURRING_MARK;
  protected readonly isNegative = isNegativeAmount;
  protected readonly searchMaxLength = SEARCH_MAX_LENGTH;
  protected readonly dateRangeMessage = DATE_RANGE_MESSAGE;
  /** Pusty wynik: przy filtrze daty tekst z design guide §8, w pozostałych przypadkach ogólny. */
  protected readonly filteredEmptyTitle = computed(() =>
    this.expenses.dateFilterActive()
      ? 'Brak wydatków w wybranym okresie'
      : 'Brak wydatków dla wybranych filtrów',
  );

  constructor() {
    const definitionId = inject(ActivatedRoute).snapshot.queryParamMap.get('wydatekCykliczny');
    if (definitionId) {
      void this.expenses.filterByRecurring(definitionId);
    } else {
      void this.expenses.load();
    }
  }

  protected add(): void {
    void this.router.navigate(['/wydatki', 'nowy']);
  }

  protected onSort(sort: SortState): void {
    void this.expenses.setSort(sort);
  }

  protected onPage(page: number): void {
    void this.expenses.setPage(page);
  }

  protected onDate(field: ExpenseFilterKey, event: Event): void {
    void this.expenses.updateFilters({ [field]: valueOf(event) });
  }

  protected onCategory(category: ExpenseCategory, event: Event): void {
    void this.expenses.toggleCategory(category, (event.target as HTMLInputElement).checked);
  }

  protected isCategoryChecked(category: ExpenseCategory): boolean {
    return this.expenses.filters().categories.includes(category);
  }

  protected onState(event: Event): void {
    void this.expenses.updateFilters({ state: valueOf(event) as ExpenseStateFilter | '' });
  }

  protected onSign(event: Event): void {
    void this.expenses.updateFilters({ sign: valueOf(event) as ExpenseSignFilter | '' });
  }

  protected onSearch(event: Event): void {
    event.preventDefault();
    const input = (event.target as HTMLFormElement).querySelector<HTMLInputElement>(
      '#wydatki-szukaj',
    );
    this.applySearch(input?.value ?? '');
  }

  protected onSearchChange(event: Event): void {
    this.applySearch(valueOf(event));
  }

  private applySearch(value: string): void {
    if (value.trim() === this.expenses.filters().search.trim()) {
      return;
    }
    void this.expenses.updateFilters({ search: value });
  }
}

function valueOf(event: Event): string {
  return (event.target as HTMLInputElement | HTMLSelectElement).value;
}
