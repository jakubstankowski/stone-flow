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
  applyWhen,
  form,
  submit,
  validate,
} from '@angular/forms/signals';
import { Button } from '../../shared/ui/button';
import { Field } from '../../shared/ui/field';
import {
  ENTRY_LIMITS,
  ENTRY_MESSAGES,
  EntryFormOutcome,
  PaymentFormModel,
  entryAmountError,
  entryDateError,
  toPaymentInput,
} from './entry-form-model';
import { EntryField, focusById, outcomeErrors, shownError, summaryOf } from './entry-form-support';
import { todayInWarsaw } from './order-form-model';
import { PAYMENT_METHODS, PAYMENT_METHOD_LABELS, PaymentInput } from './order-models';

export type PaymentFormAction = (input: PaymentInput) => Promise<EntryFormOutcome>;

/**
 * Formularz wpłaty (dodanie i poprawa). Pole „Przyjął” jest widoczne tylko przy gotówce
 * i czyści się po zmianie metody. Data ma `max` równe dzisiejszej dacie w Europe/Warsaw.
 */
@Component({
  selector: 'app-payment-form',
  imports: [Button, Field, FormField],
  templateUrl: './payment-form.html',
})
export class PaymentForm {
  readonly initial = input.required<PaymentFormModel>();
  readonly submitLabel = input('Zapisz wpłatę');
  /** Ostrzeżenie przed zapisem wpłaty do anulowanego zamówienia. */
  readonly cancelledWarning = input(false);
  readonly action = input.required<PaymentFormAction>();

  protected readonly methods = PAYMENT_METHODS;
  protected readonly methodLabels = PAYMENT_METHOD_LABELS;
  protected readonly today = todayInWarsaw();

  private readonly injector = inject(Injector);
  private readonly summaryElement = viewChild<ElementRef<HTMLElement>>('summaryBox');

  private readonly model = linkedSignal(() => structuredClone(this.initial()));
  protected readonly form = form(this.model, (path) => {
    validate(path.amount, ({ value }) => message(entryAmountError(value())));
    validate(path.date, ({ value }) => message(entryDateError(value(), this.today)));
    validate(path.method, ({ value }) =>
      message(value() === '' ? ENTRY_MESSAGES.methodRequired : null),
    );
    applyWhen(
      path.receivedBy,
      () => this.model().method === 'cash',
      (receivedBy) =>
        validate(receivedBy, ({ value }) =>
          message(
            value().trim().length > ENTRY_LIMITS.receivedBy
              ? ENTRY_MESSAGES.receivedByTooLong
              : null,
          ),
        ),
    );
  });

  private readonly fields: readonly EntryField[] = [
    { key: 'amount', id: 'wplata-kwota', field: () => this.form.amount },
    { key: 'date', id: 'wplata-data', field: () => this.form.date },
    { key: 'method', id: 'wplata-metoda', field: () => this.form.method as FieldTree<string> },
    { key: 'receivedBy', id: 'wplata-przyjal', field: () => this.form.receivedBy },
  ];

  protected readonly busy = computed(() => this.form().submitting());
  protected readonly isCash = computed(() => this.form.method().value() === 'cash');

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
    // „Przyjął” dotyczy tylko gotówki: inna metoda czyści pole (API odrzuca niepuste).
    effect(() => {
      if (this.form.method().value() !== 'cash') {
        untracked(() => {
          if (this.form.receivedBy().value() !== '') {
            this.form.receivedBy().value.set('');
          }
        });
      }
    });
    afterNextRender(() => document.getElementById('wplata-kwota')?.focus());
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
    const outcome = await this.action()(toPaymentInput(this.model()));
    const { fieldErrors, general } = outcomeErrors(outcome, this.fields);
    this.general.set(general);
    return fieldErrors;
  }
}

function message(text: string | null) {
  return text ? { kind: 'entry', message: text } : null;
}
