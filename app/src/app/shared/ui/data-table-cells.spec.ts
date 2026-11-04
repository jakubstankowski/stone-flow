import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DataTable, DataTableColumn } from './data-table';
import { StatusBadge } from './status-badge';
import { TableCell } from './table-cell';

interface Order {
  id: number;
  number: string;
  paid: boolean;
}

@Component({
  imports: [DataTable, StatusBadge, TableCell],
  template: `<app-data-table
    caption="Zamówienia"
    [columns]="columns"
    [rows]="rows"
    [rowKey]="rowKey"
    [total]="2"
    [pageSize]="pageSize()"
  >
    <ng-template appTableCell="number" [appTableCellOf]="rows" let-row>
      <a [href]="'/zamowienia/' + row.id">{{ row.number }}</a>
    </ng-template>
    <ng-template appTableCell="payment" [appTableCellOf]="rows" let-row>
      <app-status-badge
        [label]="row.paid ? 'Opłacone' : 'Nieopłacone'"
        [tone]="row.paid ? 'success' : 'warning'"
      />
    </ng-template>
  </app-data-table>`,
})
class Host {
  readonly rows: Order[] = [
    { id: 7, number: 'ZAM-7', paid: true },
    { id: 8, number: 'ZAM-8', paid: false },
  ];
  readonly columns: readonly DataTableColumn<Order>[] = [
    { key: 'number', header: 'Numer' },
    { key: 'payment', header: 'Stan płatności' },
    { key: 'plain', header: 'Bez wartości' },
  ];
  readonly rowKey = (row: Order) => row.id;
  readonly pageSize = signal(25);
}

describe('DataTable cell templates', () => {
  it('renders links and status badges from typed cell templates', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr');

    const link = rows[0].querySelector('td a') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/zamowienia/7');
    expect(link.textContent?.trim()).toBe('ZAM-7');
    expect(rows[0].querySelectorAll('td')[1].textContent?.trim()).toBe('Opłacone');
    expect(rows[1].querySelectorAll('td')[1].textContent?.trim()).toBe('Nieopłacone');
    expect(rows[0].querySelectorAll('td')[2].textContent?.trim()).toBe('');
  });

  it('guards against a non-positive page size', async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.pageSize.set(0);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.textContent).toContain('1–1 z 2');
    expect(element.querySelector('nav')?.getAttribute('aria-label')).toBe('Paginacja: Zamówienia');
  });
});
