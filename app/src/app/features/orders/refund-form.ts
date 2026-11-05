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
import {
  FieldTree,
  FormField,
  TreeValidationResult,
  form,
  submit,
  validate,
} from '@angular/forms/signals';
import { Button } from '../../shared/ui/button';
import { Field } from '../../shared/ui/field';
import {
  ENTRY_MESSAGES,
  EntryFormOutcome,
  RefundFormModel,
  entryAmountError,
  entryDateError,
  entryReasonError,
  toRefundInput,
} from './entry-form-model';
import { EntryField, focusById, outcomeErrors, shownError, summaryOf } from './entry-form-support';
import { todayInWarsaw } from './order-form-model';
import {
  REFUND_KINDS,
  REFUND_KIND_DESCRIPTIONS,
  REFUND_KIND_LABELS,
  REFUND_METHODS,
  REFUND_METHOD_LABELS,
  RefundInput,
} from './order-models';

export type RefundFormAction = (input: RefundInput) => Promise<EntryFormOutcome>;

/**
 * Formularz zwrotu (dodanie i poprawa). Rodzaj to grupa radio bez wartości domyślnej,
 * z opisem wpływu na sprzedaż. Data ma `max` równe dzisiejszej dacie w Europe/Warsaw.
 */
@Component({
  selector: 'app-refund-form',
  imports: [Button, Field, FormField],
  templateUrl: './refund-form.html',
})
export class RefundForm {
  readonly initial = input.required<RefundFormModel>();
  readonly submitLabel = input('Zapisz zwrot');
  readonly action = input.required<RefundFormAction>();

  protected readonly kinds = REFUND_KINDS;
  protected readonly kindLabels = REFUND_KIND_LABELS;
  protected readonly kindDescriptions = REFUND_KIND_DESCRIPTIONS;
  protected readonly methods = REFUND_METHODS;
  protected readonly methodLabels = REFUND_METHOD_LABELS;
  protected readonly today = todayInWarsaw();

  private readonly injector = inject(Injector);
  private readonly summaryElement = viewChild<ElementRef<HTMLElement>>('summaryBox');

  private readonly model = linkedSignal(() => structuredClone(this.initial()));
  protected readonly form = form(this.model, (path) => {
    validate(path.kind, ({ value }) =>
      message(value() === '' ? ENTRY_MESSAGES.kindRequired : null),
    );
    validate(path.amount, ({ value }) => message(entryAmountError(value())));
    validate(path.date, ({ value }) => message(entryDateError(value(), this.today)));
    validate(path.method, ({ value }) =>
      message(value() === '' ? ENTRY_MESSAGES.methodRequired : null),
    );
    validate(path.reason, ({ value }) =>
      message(entryReasonError(value(), ENTRY_MESSAGES.reasonRequired)),
    );
  });

  private readonly fields: readonly EntryField[] = [
    {
      key: 'kind',
      id: `zwrot-rodzaj-${REFUND_KINDS[0]}`,
      field: () => this.form.kind as FieldTree<string>,
    },
    { key: 'amount', id: 'zwrot-kwota', field: () => this.form.amount },
    { key: 'date', id: 'zwrot-data', field: () => this.form.date },
    { key: 'method', id: 'zwrot-metoda', field: () => this.form.method as FieldTree<string> },
    { key: 'reason', id: 'zwrot-powod', field: () => this.form.reason },
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
    afterNextRender(() => document.getElementById(`zwrot-rodzaj-${REFUND_KINDS[0]}`)?.focus());
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
    const outcome = await this.action()(toRefundInput(this.model()));
    const { fieldErrors, general } = outcomeErrors(outcome, this.fields);
    this.general.set(general);
    return fieldErrors;
  }
}

function message(text: string | null) {
  return text ? { kind: 'entry', message: text } : null;
}
