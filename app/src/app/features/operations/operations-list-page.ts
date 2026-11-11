import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { NO_DATA, formatDate, formatPln } from '../../shared/format/pl-format';
import { Button } from '../../shared/ui/button';
import { DataTable, DataTableColumn, SortState } from '../../shared/ui/data-table';
import { Field } from '../../shared/ui/field';
import { TableCell } from '../../shared/ui/table-cell';
import {
  CATEGORY_LABELS,
  DIRECTION_LABELS,
  KIND_FILTER_LABELS,
  NEGATIVE_AMOUNT_LABEL,
  OPERATION_KINDS,
  OperationItem,
  OperationKind,
  isNegativeAmount,
  kindLabel,
  methodLabel,
  sourceLink,
} from './operation-models';
import { OperationFilters, OperationsService, currentMonth } from './operations.service';
import { OwnerWithdrawalsService } from './owner-withdrawals.service';

const EMPTY_TITLE = 'Brak operacji w wybranym okresie';

/** Parametry przejścia z Pulpitu (SF-20): okres i powtarzalny rodzaj. */
const DASHBOARD_PARAMS = ['od', 'do', 'rodzaj'] as const;

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

/**
 * Filtr z adresu `/operacje?od=…&do=…&rodzaj=…` (przejście z Pulpitu); `null`, gdy w adresie nie
 * ma żadnego z tych parametrów. Okres to całe okno: brak, niepoprawna albo nieistniejąca
 * którakolwiek data albo `od` > `do` daje bieżący miesiąc (obie daty). Nieznane rodzaje są
 * pomijane; brak rodzajów oznacza wszystkie.
 */
export function dashboardFilterFromParams(params: ParamMap): OperationFilters | null {
  if (!DASHBOARD_PARAMS.some((name) => params.has(name))) {
    return null;
  }
  const from = validDate(params.get('od'));
  const to = validDate(params.get('do'));
  // Daty `yyyy-MM-dd` porównują się poprawnie jako tekst.
  const period = from !== null && to !== null && from <= to ? { from, to } : currentMonth();
  const requested = params.getAll('rodzaj');
  return { ...period, kinds: OPERATION_KINDS.filter((kind) => requested.includes(kind)) };
}

function validDate(value: string | null): string | null {
  return value !== null && ISO_DATE.test(value) && formatDate(value) !== NO_DATA ? value : null;
}

const COLUMNS: readonly DataTableColumn<OperationItem>[] = [
  { key: 'date', header: 'Data', value: (row) => formatDate(row.date), sortable: true },
  { key: 'kind', header: 'Rodzaj', value: (row) => kindLabel(row) },
  { key: 'description', header: 'Opis' },
  { key: 'direction', header: 'Kierunek', value: (row) => DIRECTION_LABELS[row.direction] },
  { key: 'amount', header: 'Kwota', align: 'end' },
  { key: 'method', header: 'Metoda', value: (row) => methodLabel(row.method) },
];

/**
 * Ekran `/operacje`: operacje pieniężne z Orders i Expenses w wybranym okresie, z sumami dla
 * całego filtra. Lista, sumy i efektywny początek pochodzą z API; strona tylko je formatuje.
 * Parametry przejścia z Pulpitu (`od`, `do`, `rodzaj`, SF-20) są czytane raz, ustawiają filtr w
 * serwisie i znikają z adresu (`replaceUrl`), więc „Wstecz” ze źródła operacji wraca do listy z
 * filtrem zmienionym na liście, a nie do parametrów z Pulpitu.
 */
@Component({
  selector: 'app-operations-list-page',
  imports: [Button, DataTable, Field, RouterLink, TableCell],
  templateUrl: './operations-list-page.html',
})
export class OperationsListPage {
  protected readonly operations = inject(OperationsService);
  private readonly router = inject(Router);

  /** Komunikat po usunięciu wypłaty w szczegółach. */
  protected readonly notice = signal<string | null>(inject(OwnerWithdrawalsService).takeNotice());

  protected readonly columns = COLUMNS;
  protected readonly rowKey = (row: OperationItem) => `${row.kind}:${row.id}`;
  protected readonly kinds = OPERATION_KINDS;
  protected readonly kindLabels = KIND_FILTER_LABELS;
  protected readonly categoryLabels = CATEGORY_LABELS;
  protected readonly negativeLabel = NEGATIVE_AMOUNT_LABEL;
  protected readonly isNegative = isNegativeAmount;
  protected readonly sourceLink = sourceLink;
  protected readonly formatPln = formatPln;
  /** Przy `422` pusta tabela powtarza komunikat z filtra zamiast „Brak operacji…”. */
  protected readonly emptyTitle = computed(() => this.operations.rangeError() ?? EMPTY_TITLE);
  protected readonly filteredEmptyTitle = computed(
    () =>
      this.operations.rangeError() ??
      (this.operations.kindFilterActive()
        ? 'Brak operacji wybranych rodzajów w wybranym okresie'
        : EMPTY_TITLE),
  );
  /** Błąd przy „Data od”: brak daty, odwrócony okres albo `422` (za dużo operacji). */
  protected readonly fromError = computed(
    () => this.operations.fromError() ?? this.operations.rangeError(),
  );

  constructor() {
    const route = inject(ActivatedRoute);
    const filter = dashboardFilterFromParams(route.snapshot.queryParamMap);
    if (filter) {
      void this.operations.filterFromDashboard(filter);
      void this.router.navigate([], { relativeTo: route, queryParams: {}, replaceUrl: true });
    } else {
      void this.operations.load();
    }
  }

  protected add(): void {
    void this.router.navigate(['/operacje', 'wyplaty', 'nowa']);
  }

  protected onSort(sort: SortState): void {
    void this.operations.setSort(sort);
  }

  protected onPage(page: number): void {
    void this.operations.setPage(page);
  }

  protected onDate(field: 'from' | 'to', event: Event): void {
    void this.operations.updatePeriod({ [field]: (event.target as HTMLInputElement).value });
  }

  protected onKind(kind: OperationKind, event: Event): void {
    void this.operations.toggleKind(kind, (event.target as HTMLInputElement).checked);
  }

  protected isKindChecked(kind: OperationKind): boolean {
    return this.operations.filters().kinds.includes(kind);
  }
}
