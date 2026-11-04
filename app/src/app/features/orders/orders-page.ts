import { Component } from '@angular/core';
import { DemoOrder, ORDERS } from '../../demo/demo-data';
import { formatDate, formatPln } from '../../shared/format/pl-format';
import { DataTable, DataTableColumn } from '../../shared/ui/data-table';
import { StatusBadge } from '../../shared/ui/status-badge';
import { TableCell } from '../../shared/ui/table-cell';
import { STATUS_TONES } from '../dashboard/dashboard-page';

/** TRYB DEMO: lista zamówień z `demo/demo-data.ts`. */
@Component({
  selector: 'app-orders-page',
  imports: [DataTable, TableCell, StatusBadge],
  templateUrl: './orders-page.html',
})
export class OrdersPage {
  protected readonly rows = ORDERS;
  protected readonly statusTones = STATUS_TONES;
  protected readonly rowKey = (order: DemoOrder) => order.number;

  protected readonly columns: readonly DataTableColumn<DemoOrder>[] = [
    { key: 'number', header: 'Numer', value: (o) => o.number },
    { key: 'date', header: 'Data', value: (o) => formatDate(o.date) },
    { key: 'customer', header: 'Klient', value: (o) => o.customer },
    { key: 'product', header: 'Produkt', value: (o) => o.product },
    { key: 'channel', header: 'Kanał', value: (o) => o.channel },
    { key: 'value', header: 'Wartość', value: (o) => formatPln(o.value), align: 'end' },
    { key: 'paid', header: 'Zapłacono', value: (o) => formatPln(o.paid), align: 'end' },
    { key: 'status', header: 'Status' },
  ];
}
