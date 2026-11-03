import { Component, computed, input, output, signal } from '@angular/core';
import { Button } from './button';
import { EmptyState } from './empty-state';
import { ErrorState } from './error-state';
import { LoadingState } from './loading-state';

let nextId = 0;

export interface DataTableColumn<T> {
  key: string;
  header: string;
  value: (row: T) => string;
  align?: 'start' | 'end';
  sortable?: boolean;
}

export type SortDirection = 'asc' | 'desc';

export interface SortState {
  key: string;
  direction: SortDirection;
}

export interface ActiveFilter {
  id: string;
  label: string;
}

/**
 * Tabela prezentacyjna dla list modułów (design guide, sekcje 6 i 8). Nie pobiera,
 * nie sortuje i nie paginuje danych lokalnie — emituje zdarzenia do rodzica, który
 * pyta API. Filtry są projekcją z atrybutem `tableFilters`.
 */
@Component({
  selector: 'app-data-table',
  imports: [Button, EmptyState, ErrorState, LoadingState],
  templateUrl: './data-table.html',
  host: { class: 'flex min-w-0 flex-col gap-4' },
})
export class DataTable<T> {
  readonly caption = input.required<string>();
  readonly columns = input.required<readonly DataTableColumn<T>[]>();
  readonly rows = input.required<readonly T[]>();
  readonly rowKey = input.required<(row: T) => string | number>();
  readonly total = input.required<number>();
  readonly page = input(1);
  readonly pageSize = input(25);
  readonly sort = input<SortState | null>(null);
  readonly filters = input<readonly ActiveFilter[]>([]);
  readonly filterable = input(false);
  readonly loading = input(false);
  readonly error = input<string | null>(null);
  readonly emptyTitle = input('Brak rekordów.');
  readonly emptyDescription = input<string | null>(null);
  readonly filteredEmptyTitle = input('Brak wyników dla wybranych filtrów.');

  readonly pageChange = output<number>();
  readonly sortChange = output<SortState>();
  readonly filterRemove = output<string>();
  readonly filtersClear = output<void>();
  readonly retry = output<void>();

  protected readonly filtersOpen = signal(false);
  protected readonly filtersPanelId = `filtry-${nextId++}`;

  protected readonly hasPrevious = computed(() => this.page() > 1);
  protected readonly hasNext = computed(() => this.page() * this.pageSize() < this.total());

  protected readonly rangeText = computed(() => {
    const total = Math.max(0, this.total());
    const from = (this.page() - 1) * this.pageSize() + 1;
    if (total === 0 || from > total || from < 1) {
      return `0 z ${total}`;
    }
    const to = Math.min(this.page() * this.pageSize(), total);
    return `${from}–${to} z ${total}`;
  });

  protected ariaSort(column: DataTableColumn<T>): 'ascending' | 'descending' | null {
    const sort = this.sort();
    if (!column.sortable || sort?.key !== column.key) {
      return null;
    }
    return sort.direction === 'asc' ? 'ascending' : 'descending';
  }

  protected toggleSort(column: DataTableColumn<T>): void {
    const sort = this.sort();
    const direction: SortDirection =
      sort?.key === column.key && sort.direction === 'asc' ? 'desc' : 'asc';
    this.sortChange.emit({ key: column.key, direction });
  }

  protected goTo(page: number): void {
    this.pageChange.emit(page);
  }
}
