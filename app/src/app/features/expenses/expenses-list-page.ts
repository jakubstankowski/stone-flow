import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
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
import {
  CostPeriodFilter,
  DATE_RANGE_MESSAGE,
  ExpensesService,
  SEARCH_MAX_LENGTH,
} from './expenses.service';
import { ExpensesSubnav } from './expenses-subnav';
import { RECURRING_MARK } from './recurring-expense-models';
import { RecurringJobNotice } from './recurring-job-notice';

/** Parametry przejścia z Pulpitu (SF-19): data kosztu od/do i kategorie (powtarzalny). */
const COST_PERIOD_PARAMS = ['kosztOd', 'kosztDo', 'kategoria'] as const;

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

export const GROSS_TOTAL_LABEL =
  'Suma kwot brutto wydatków spełniających filtry; korekty i zwroty od dostawców ją pomniejszają.';

/**
 * Filtr z parametrów przejścia z Pulpitu albo `null`, gdy żadnego parametru nie ma. Każdy
 * niepoprawny parametr jest pomijany osobno: data spoza `yyyy-MM-dd` albo nieistniejąca, „od”
 * późniejsze niż „do” (obie daty pominięte), nieznana kategoria.
 */
export function costPeriodFromParams(params: ParamMap): CostPeriodFilter | null {
  if (!COST_PERIOD_PARAMS.some((name) => params.has(name))) {
    return null;
  }
  let from = validDate(params.get('kosztOd'));
  let to = validDate(params.get('kosztDo'));
  // Daty `yyyy-MM-dd` porównują się poprawnie jako tekst.
  if (from !== '' && to !== '' && from > to) {
    from = '';
    to = '';
  }
  const requested = params.getAll('kategoria');
  return {
    from,
    to,
    categories: CATEGORIES.filter((category) => requested.includes(category)),
  };
}

function validDate(value: string | null): string {
  return value !== null && ISO_DATE.test(value) && formatDate(value) !== NO_DATA ? value : '';
}

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
 * wystąpienia ma pod opisem tekst „Cykliczny” (SF-16). Parametry `kosztOd`, `kosztDo` i
 * `kategoria` (przejście z Pulpitu, SF-19) ustawiają filtr daty kosztu i kategorii; nad tabelą
 * „Suma brutto” całego filtra z API.
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
  protected readonly grossTotalLabel = GROSS_TOTAL_LABEL;
  protected readonly formatPln = formatPln;
  /** Pusty wynik: przy filtrze daty tekst z design guide §8, w pozostałych przypadkach ogólny. */
  protected readonly filteredEmptyTitle = computed(() =>
    this.expenses.dateFilterActive()
      ? 'Brak wydatków w wybranym okresie'
      : 'Brak wydatków dla wybranych filtrów',
  );

  constructor() {
    const params = inject(ActivatedRoute).snapshot.queryParamMap;
    const definitionId = params.get('wydatekCykliczny');
    const costPeriod = costPeriodFromParams(params);
    if (definitionId) {
      void this.expenses.filterByRecurring(definitionId);
    } else if (costPeriod) {
      void this.expenses.filterByCostPeriod(costPeriod);
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
