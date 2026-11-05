import { Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { formatDate, formatPln } from '../../shared/format/pl-format';
import { Button } from '../../shared/ui/button';
import { DataTable, DataTableColumn, SortState } from '../../shared/ui/data-table';
import { EmptyState } from '../../shared/ui/empty-state';
import { Field } from '../../shared/ui/field';
import { StatusBadge } from '../../shared/ui/status-badge';
import { TableCell } from '../../shared/ui/table-cell';
import {
  CHANNELS,
  CHANNEL_LABELS,
  FulfillmentStatus,
  OrderChannel,
  OrderListItem,
  OrderSource,
  PAYMENT_STATE_FILTERS,
  PAYMENT_STATE_FILTER_LABELS,
  PAYMENT_STATE_LABELS,
  PAYMENT_STATE_TONES,
  PaymentStateFilter,
  SOURCES,
  SOURCE_LABELS,
  STATUSES,
  STATUS_LABELS,
  STATUS_TONES,
} from './order-models';
import { DATE_RANGE_MESSAGE, OrdersService, SEARCH_MAX_LENGTH } from './orders.service';

const COLUMNS: readonly DataTableColumn<OrderListItem>[] = [
  { key: 'number', header: 'Numer', sortable: true },
  {
    key: 'orderDate',
    header: 'Data',
    value: (row) => formatDate(row.orderDate),
    sortable: true,
  },
  { key: 'customer', header: 'Klient', value: (row) => row.customerName, sortable: true },
  { key: 'channel', header: 'Kanał', value: (row) => CHANNEL_LABELS[row.channel] },
  { key: 'source', header: 'Źródło', value: (row) => SOURCE_LABELS[row.source] },
  {
    key: 'value',
    header: 'Wartość brutto',
    value: (row) => formatPln(row.value),
    align: 'end',
    sortable: true,
  },
  { key: 'paid', header: 'Zapłacono', value: (row) => formatPln(row.paid), align: 'end' },
  { key: 'paymentState', header: 'Stan płatności' },
  { key: 'status', header: 'Status realizacji' },
];

/**
 * Ekran `/zamowienia`: jedna strona z API, filtry przy tabeli, sortowanie przez API.
 * Pusta ewidencja (bez filtrów) pokazuje „Brak zamówień” z akcją zamiast tabeli.
 */
@Component({
  selector: 'app-orders-list-page',
  imports: [Button, DataTable, EmptyState, Field, RouterLink, StatusBadge, TableCell],
  templateUrl: './orders-list-page.html',
})
export class OrdersListPage {
  protected readonly orders = inject(OrdersService);
  private readonly router = inject(Router);

  protected readonly columns = COLUMNS;
  protected readonly rowKey = (row: OrderListItem) => row.id;
  protected readonly channels = CHANNELS;
  protected readonly channelLabels = CHANNEL_LABELS;
  protected readonly sources = SOURCES;
  protected readonly sourceLabels = SOURCE_LABELS;
  protected readonly statuses = STATUSES;
  protected readonly statusLabels = STATUS_LABELS;
  protected readonly statusTones = STATUS_TONES;
  protected readonly paymentStateFilters = PAYMENT_STATE_FILTERS;
  protected readonly paymentStateFilterLabels = PAYMENT_STATE_FILTER_LABELS;
  protected readonly paymentStateLabels = PAYMENT_STATE_LABELS;
  protected readonly paymentStateTones = PAYMENT_STATE_TONES;
  protected readonly searchMaxLength = SEARCH_MAX_LENGTH;
  protected readonly dateRangeMessage = DATE_RANGE_MESSAGE;

  constructor() {
    void this.orders.load();
  }

  protected add(): void {
    void this.router.navigate(['/zamowienia', 'nowe']);
  }

  protected onSort(sort: SortState): void {
    void this.orders.setSort(sort);
  }

  protected onPage(page: number): void {
    void this.orders.setPage(page);
  }

  protected onDate(field: 'dateFrom' | 'dateTo', event: Event): void {
    const value = valueOf(event);
    void this.orders.updateFilters(field === 'dateFrom' ? { dateFrom: value } : { dateTo: value });
  }

  protected onChannel(event: Event): void {
    void this.orders.updateFilters({ channel: valueOf(event) as OrderChannel | '' });
  }

  protected onSource(event: Event): void {
    void this.orders.updateFilters({ source: valueOf(event) as OrderSource | '' });
  }

  protected onStatus(status: FulfillmentStatus, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    const current = this.orders.filters().statuses.filter((candidate) => candidate !== status);
    // Kolejność statusów w zapytaniu jest stała (jak w słowniku), niezależnie od kolejności kliknięć.
    const next = checked
      ? STATUSES.filter((candidate) => candidate === status || current.includes(candidate))
      : current;
    void this.orders.updateFilters({ statuses: next });
  }

  protected onPaymentState(state: PaymentStateFilter, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    const current = this.orders.filters().paymentStates.filter((candidate) => candidate !== state);
    const next = checked
      ? PAYMENT_STATE_FILTERS.filter(
          (candidate) => candidate === state || current.includes(candidate),
        )
      : current;
    void this.orders.updateFilters({ paymentStates: next });
  }

  protected isPaymentStateChecked(state: PaymentStateFilter): boolean {
    return this.orders.filters().paymentStates.includes(state);
  }

  protected onSearch(event: Event): void {
    event.preventDefault();
    const input = (event.target as HTMLFormElement).querySelector<HTMLInputElement>(
      '#zamowienia-szukaj',
    );
    this.applySearch(input?.value ?? '');
  }

  protected onSearchChange(event: Event): void {
    this.applySearch(valueOf(event));
  }

  protected isStatusChecked(status: FulfillmentStatus): boolean {
    return this.orders.filters().statuses.includes(status);
  }

  private applySearch(value: string): void {
    if (value.trim() === this.orders.filters().search.trim()) {
      return;
    }
    void this.orders.updateFilters({ search: value });
  }
}

function valueOf(event: Event): string {
  return (event.target as HTMLInputElement | HTMLSelectElement).value;
}
