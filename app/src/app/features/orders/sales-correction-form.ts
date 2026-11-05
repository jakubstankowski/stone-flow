import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  linkedSignal,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { FormField, TreeValidationResult, form, submit, validate } from '@angular/forms/signals';
import { Button } from '../../shared/ui/button';
import { Field } from '../../shared/ui/field';
import {
  ENTRY_MESSAGES,
  EntryFormOutcome,
  SalesCorrectionFormModel,
  entryAmountError,
  entryDateError,
  entryReasonError,
  toSalesCorrectionInput,
} from './entry-form-model';
import { EntryField, focusById, outcomeErrors, shownError, summaryOf } from './entry-form-support';
import { todayInWarsaw } from './order-form-model';
import { SalesCorrectionInput } from './order-models';

export type SalesCorrectionFormAction = (input: SalesCorrectionInput) => Promise<EntryFormOutcome>;

/**
 * Formularz korekty sprzedaży (SF-10): kwota, data i powód, bez metody. Limitu obniżeń nie
 * sprawdza we frontendzie: decyduje backend (`422 sales_reduction_limit_exceeded`), bo poprawa
 * zmniejszająca nadwyżkę jest dozwolona nawet przy przekroczonym limicie.
 */
@Component({
  selector: 'app-sales-correction-form',
  imports: [Button, Field, FormField],
  templateUrl: './sales-correction-form.html',
})
export class SalesCorrectionForm {
  readonly initial = input.required<SalesCorrectionFormModel>();
  readonly submitLabel = input('Zapisz korektę');
  readonly action = input.required<SalesCorrectionFormAction>();

  protected readonly today = todayInWarsaw();

  private readonly injector = inject(Injector);
  private readonly summaryElement = viewChild<ElementRef<HTMLElement>>('summaryBox');

  private readonly model = linkedSignal(() => structuredClone(this.initial()));
  protected readonly form = form(this.model, (path) => {
    validate(path.amount, ({ value }) => message(entryAmountError(value())));
    validate(path.date, ({ value }) => message(entryDateError(value(), this.today)));
    validate(path.reason, ({ value }) =>
      message(entryReasonError(value(), ENTRY_MESSAGES.correctionReasonRequired)),
    );
  });

  private readonly fields: readonly EntryField[] = [
    { key: 'amount', id: 'korekta-kwota', field: () => this.form.amount },
    { key: 'date', id: 'korekta-data', field: () => this.form.date },
    { key: 'reason', id: 'korekta-powod', field: () => this.form.reason },
  ];

  protected readonly busy = computed(() => this.form().submitting());

  private readonly attempted = signal(false);
  private readonly general = signal<readonly string[]>([]);
  protected readonly summary = computed(() =>
    this.attempted() ? summaryOf(this.fields, this.general()) : [],
  );

  protected readonly errorOf = shownError;
  protected readonly focusField = focusById;

  constructor() {
    // Nowy stan początkowy („Wczytaj aktualne dane”) czyści dotknięcia i podsumowanie.
    effect(() => {
      this.initial();
      untracked(() => {
        this.form().reset();
        this.attempted.set(false);
        this.general.set([]);
      });
    });
    afterNextRender(() => document.getElementById('korekta-kwota')?.focus());
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    this.general.set([]);
    this.attempted.set(false);
    await submit(this.form, () => this.save());
    if (this.form().invalid() || this.general().length > 0) {
      this.attempted.set(true);
      afterNextRender(() => this.summaryElement()?.nativeElement.focus(), {
        injector: this.injector,
      });
    }
  }

  private async save(): Promise<TreeValidationResult> {
    const outcome = await this.action()(toSalesCorrectionInput(this.model()));
    const { fieldErrors, general } = outcomeErrors(outcome, this.fields);
    this.general.set(general);
    return fieldErrors;
  }
}

function message(text: string | null) {
  return text ? { kind: 'entry', message: text } : null;
}
