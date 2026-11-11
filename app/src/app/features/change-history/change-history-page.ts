import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { formatDate, formatDateTime } from '../../shared/format/pl-format';
import { DataTable, DataTableColumn, SortState } from '../../shared/ui/data-table';
import { Field } from '../../shared/ui/field';
import { StatusBadge } from '../../shared/ui/status-badge';
import { TableCell } from '../../shared/ui/table-cell';
import { changeSummary, fieldLabel, formatFieldValue } from './audit-field-catalog';
import {
  AUTHOR_LABELS,
  AuditObjectTypeCode,
  BUSINESS_DATE_HINT,
  ChangeHistoryItem,
  DELETED_LABEL,
  DELETED_TONE,
  EMPTY_TITLE,
  FILTERED_EMPTY_TITLE,
  OBJECT_TYPES,
  OBJECT_TYPE_LABELS,
  OPERATION_LABELS,
  OPERATION_TONES,
  objectLink,
} from './change-history-models';
import { ChangeHistoryFilters, ChangeHistoryService } from './change-history.service';

type DateFilter = keyof Omit<ChangeHistoryFilters, 'objectTypes'>;

const COLUMNS: readonly DataTableColumn<ChangeHistoryItem>[] = [
  {
    key: 'occurredAt',
    header: 'Czas',
    value: (row) => formatDateTime(row.occurredAt),
    sortable: true,
  },
  { key: 'author', header: 'Autor', value: (row) => AUTHOR_LABELS[row.author.kind] },
  { key: 'object', header: 'Obiekt' },
  { key: 'operation', header: 'Operacja' },
  { key: 'changes', header: 'Zakres zmiany' },
];

/**
 * Ekran `/historia-zmian` (SF-18): wpisy audytu tylko do odczytu, z filtrami okresu zmiany, daty
 * biznesowej i rodzaju obiektu. Lista pochodzi z API; strona formatuje wartości według katalogu
 * pól i nie ma żadnych akcji zmieniających dane.
 */
@Component({
  selector: 'app-change-history-page',
  imports: [DataTable, Field, RouterLink, StatusBadge, TableCell],
  templateUrl: './change-history-page.html',
})
export class ChangeHistoryPage {
  protected readonly history = inject(ChangeHistoryService);

  protected readonly columns = COLUMNS;
  protected readonly rowKey = (row: ChangeHistoryItem) => row.id;
  protected readonly objectTypes = OBJECT_TYPES;
  protected readonly objectTypeLabels = OBJECT_TYPE_LABELS;
  protected readonly operationLabels = OPERATION_LABELS;
  protected readonly operationTones = OPERATION_TONES;
  protected readonly deletedLabel = DELETED_LABEL;
  protected readonly deletedTone = DELETED_TONE;
  protected readonly businessDateHint = BUSINESS_DATE_HINT;
  protected readonly emptyTitle = EMPTY_TITLE;
  protected readonly filteredEmptyTitle = FILTERED_EMPTY_TITLE;
  protected readonly objectLink = objectLink;
  protected readonly changeSummary = changeSummary;
  protected readonly fieldLabel = fieldLabel;
  protected readonly formatFieldValue = formatFieldValue;
  protected readonly formatDate = formatDate;
  protected readonly formatDateTime = formatDateTime;

  /** Rozwinięte wiersze (identyfikatory wpisów); stan lokalny widoku. */
  protected readonly expanded = signal<ReadonlySet<string>>(new Set());

  constructor() {
    this.history.refreshDefaultPeriod();
    void this.history.load();
  }

  protected onSort(sort: SortState): void {
    void this.history.setSort(sort);
  }

  protected onPage(page: number): void {
    void this.history.setPage(page);
  }

  protected onDate(field: DateFilter, event: Event): void {
    void this.history.updateFilters({ [field]: (event.target as HTMLInputElement).value });
  }

  protected onObjectType(type: AuditObjectTypeCode, event: Event): void {
    void this.history.toggleObjectType(type, (event.target as HTMLInputElement).checked);
  }

  protected isTypeChecked(type: AuditObjectTypeCode): boolean {
    return this.history.filters().objectTypes.includes(type);
  }

  protected isExpanded(row: ChangeHistoryItem): boolean {
    return this.expanded().has(row.id);
  }

  /** Przycisk zostaje w DOM, więc po zwinięciu fokus jest dalej na nim. */
  protected toggle(row: ChangeHistoryItem): void {
    this.expanded.update((current) => {
      const next = new Set(current);
      if (!next.delete(row.id)) {
        next.add(row.id);
      }
      return next;
    });
  }

  /** Nazwa dostępna przycisku rozwinięcia: akcja, obiekt i czas, żeby wiersze były rozróżnialne. */
  protected toggleLabel(row: ChangeHistoryItem): string {
    const action = this.isExpanded(row) ? 'Ukryj zmiany' : 'Pokaż zmiany';
    return `${action}: ${row.object.label}, ${OPERATION_LABELS[row.operation]}, ${formatDateTime(row.occurredAt)}`;
  }

  protected panelId(row: ChangeHistoryItem): string {
    return `zmiany-${row.id}`;
  }
}
