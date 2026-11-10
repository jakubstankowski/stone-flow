import { Component, input, output } from '@angular/core';
import { Field } from '../../shared/ui/field';
import { SelectionErrors } from './dashboard.service';
import { PERIOD_KINDS, PERIOD_LABELS, PeriodKind, PeriodSelection } from './report-models';

/**
 * Wybór okresu Pulpitu (SF-19): select „Okres”, przy „Rok” select „Rok”, przy „Własny zakres”
 * pola „Data od” i „Data do” (`max` = dziś + 1 rok). Komunikaty przy polach przychodzą z serwisu,
 * a `422` z API jest pokazywany w `role="alert"` pod wyborem. Komponent nie waliduje.
 */
@Component({
  selector: 'app-period-picker',
  imports: [Field],
  templateUrl: './period-picker.html',
  host: { class: 'flex flex-col gap-3' },
})
export class PeriodPicker {
  readonly selection = input.required<PeriodSelection>();
  readonly errors = input<SelectionErrors>({ from: null, to: null });
  readonly rangeError = input<string | null>(null);
  readonly yearOptions = input.required<readonly string[]>();
  readonly maxDate = input.required<string>();

  readonly selectionChange = output<Partial<PeriodSelection>>();

  protected readonly kinds = PERIOD_KINDS;
  protected readonly labels = PERIOD_LABELS;

  protected onKind(event: Event): void {
    this.selectionChange.emit({ kind: valueOf(event) as PeriodKind });
  }

  protected onYear(event: Event): void {
    this.selectionChange.emit({ year: valueOf(event) });
  }

  protected onDate(field: 'from' | 'to', event: Event): void {
    this.selectionChange.emit({ [field]: valueOf(event) });
  }
}

function valueOf(event: Event): string {
  return (event.target as HTMLInputElement | HTMLSelectElement).value;
}
