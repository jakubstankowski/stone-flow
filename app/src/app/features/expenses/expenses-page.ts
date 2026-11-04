import { Component } from '@angular/core';
import { CURRENT_MONTH, DemoExpense, EXPENSES } from '../../demo/demo-data';
import { formatDate, formatPln } from '../../shared/format/pl-format';
import { PlMonthPipe, PlnAmountPipe } from '../../shared/format/pl-format-pipes';
import { DataTable, DataTableColumn } from '../../shared/ui/data-table';
import { StatusBadge } from '../../shared/ui/status-badge';
import { TableCell } from '../../shared/ui/table-cell';

/** TRYB DEMO: lista wydatków z `demo/demo-data.ts`. */
@Component({
  selector: 'app-expenses-page',
  imports: [DataTable, TableCell, StatusBadge, PlnAmountPipe, PlMonthPipe],
  templateUrl: './expenses-page.html',
})
export class ExpensesPage {
  protected readonly rows = EXPENSES;
  protected readonly month = CURRENT_MONTH.month;
  protected readonly total = EXPENSES.reduce((sum, expense) => sum + expense.amount, 0);
  protected readonly recurringTotal = EXPENSES.filter((e) => e.recurring).reduce(
    (sum, expense) => sum + expense.amount,
    0,
  );
  protected readonly unpaidTotal = EXPENSES.filter((e) => !e.paid).reduce(
    (sum, expense) => sum + expense.amount,
    0,
  );
  protected readonly rowKey = (expense: DemoExpense) => `${expense.date}-${expense.description}`;

  protected readonly columns: readonly DataTableColumn<DemoExpense>[] = [
    { key: 'date', header: 'Data kosztu', value: (e) => formatDate(e.date) },
    { key: 'description', header: 'Opis', value: (e) => e.description },
    { key: 'category', header: 'Kategoria', value: (e) => e.category },
    { key: 'type', header: 'Rodzaj', value: (e) => (e.recurring ? 'Miesięczny' : 'Jednorazowy') },
    { key: 'amount', header: 'Kwota', value: (e) => formatPln(e.amount), align: 'end' },
    { key: 'paid', header: 'Płatność' },
  ];
}
