import { Component, computed, input, linkedSignal } from '@angular/core';
import { FieldTree, FormField, form, submit, validate } from '@angular/forms/signals';
import { todayInWarsaw } from '../../shared/format/pl-format';
import { Button } from '../../shared/ui/button';
import { Field } from '../../shared/ui/field';
import { ExpensePaidFormModel, paidEffectText, paidOnError } from './expense-form-model';
import { ExpensePaymentMethod, PAYMENT_METHODS, PAYMENT_METHOD_LABELS } from './expense-models';

/** Zapis akcji: `null` po obsłużeniu wyniku przez rodzica albo komunikat błędu dla daty zapłaty. */
export type ExpensePaidAction = (
  paidOn: string,
  paymentMethod: ExpensePaymentMethod | null,
) => Promise<string | null>;

/**
 * Formularz „Oznacz jako opłacony”: data zapłaty (domyślnie dzisiaj w Europe/Warsaw, nie później)
 * i metoda wypełniona aktualną. Opis skutku zależy od znaku kwoty brutto.
 */
@Component({
  selector: 'app-expense-paid-form',
  imports: [Button, Field, FormField],
  templateUrl: './expense-paid-form.html',
})
export class ExpensePaidForm {
  readonly grossAmount = input.required<string>();
  readonly paymentMethod = input<ExpensePaymentMethod | null>(null);
  readonly action = input.required<ExpensePaidAction>();

  protected readonly today = todayInWarsaw();
  protected readonly methods = PAYMENT_METHODS;
  protected readonly methodLabels = PAYMENT_METHOD_LABELS;
  protected readonly effectText = computed(() => paidEffectText(this.grossAmount()));

  private readonly model = linkedSignal<ExpensePaidFormModel>(() => ({
    paidOn: this.today,
    paymentMethod: this.paymentMethod() ?? '',
  }));
  protected readonly form = form(this.model, (path) => {
    validate(path.paidOn, ({ value }) => {
      const text = paidOnError(value(), this.today, true);
      return text ? { kind: 'expense', message: text } : null;
    });
  });

  protected readonly busy = computed(() => this.form().submitting());

  protected errorOf<T>(field: FieldTree<T>): string | null {
    const state = field();
    if (!state.touched()) {
      return null;
    }
    return state.errors().find((error) => error.message)?.message ?? null;
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    await submit(this.form, async () => {
      const { paidOn, paymentMethod } = this.model();
      const error = await this.action()(paidOn, paymentMethod || null);
      return error ? { kind: 'server', message: error, fieldTree: this.form.paidOn } : null;
    });
  }
}
