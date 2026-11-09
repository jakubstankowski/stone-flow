import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { formatDate, formatPln } from '../../shared/format/pl-format';
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
import { OperationsService } from './operations.service';
import { OwnerWithdrawalsService } from './owner-withdrawals.service';

const EMPTY_TITLE = 'Brak operacji w wybranym okresie';

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
    void this.operations.load();
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
