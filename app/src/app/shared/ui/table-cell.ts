import { Directive, TemplateRef, inject, input } from '@angular/core';

export interface TableCellContext<T> {
  $implicit: T;
}

/**
 * Szablon komórki dla kolumny `app-data-table` — dla statusów, linków do obiektu
 * i oznaczeń, których nie da się wyrazić tekstem.
 *
 * `<ng-template appTableCell="status" [appTableCellOf]="rows" let-row>…</ng-template>`
 * (`appTableCellOf` służy tylko do typowania `row`).
 */
@Directive({ selector: 'ng-template[appTableCell]' })
export class TableCell<T> {
  readonly appTableCell = input.required<string>();
  readonly appTableCellOf = input<readonly T[]>();

  readonly template = inject<TemplateRef<TableCellContext<T>>>(TemplateRef);

  static ngTemplateContextGuard<T>(
    directive: TableCell<T>,
    context: unknown,
  ): context is TableCellContext<T> {
    return directive !== null && context !== null;
  }
}
