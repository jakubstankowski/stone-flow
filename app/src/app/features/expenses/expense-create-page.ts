import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Button } from '../../shared/ui/button';
import { ExpenseForm, ExpenseFormOutcome } from './expense-form';
import { ExpenseFormModel, emptyExpenseForm } from './expense-form-model';
import { ExpenseInput } from './expense-models';
import { ExpensesService } from './expenses.service';

/**
 * Ekran `/wydatki/nowy`. Klucz idempotencji powstaje przy otwarciu i zostaje do udanego zapisu,
 * więc ponowienie po utracie odpowiedzi nie tworzy duplikatu. Po `409 idempotency_key_reused`
 * nowy klucz daje tylko „Dodaj kolejny wydatek”.
 */
@Component({
  selector: 'app-expense-create-page',
  imports: [Button, ExpenseForm, RouterLink],
  templateUrl: './expense-create-page.html',
})
export class ExpenseCreatePage {
  private readonly expenses = inject(ExpensesService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);

  private idempotencyKey = crypto.randomUUID();

  protected readonly initial = signal<ExpenseFormModel>(emptyExpenseForm());
  /** Wydatek zapisany już tym formularzem (`409 idempotency_key_reused`); `''` bez identyfikatora. */
  protected readonly keyReused = signal<string | null>(null);

  private readonly reusedMessage = viewChild<ElementRef<HTMLElement>>('reusedMessage');

  protected readonly save = (input: ExpenseInput): Promise<ExpenseFormOutcome> =>
    this.create(input);

  protected startNext(): void {
    this.idempotencyKey = crypto.randomUUID();
    this.keyReused.set(null);
    this.initial.set(emptyExpenseForm());
  }

  protected goToList(): void {
    void this.router.navigate(['/wydatki']);
  }

  private async create(input: ExpenseInput): Promise<ExpenseFormOutcome> {
    this.keyReused.set(null);
    const outcome = await this.expenses.create(input, this.idempotencyKey);
    switch (outcome.kind) {
      case 'saved':
        await this.router.navigate(['/wydatki', outcome.id]);
        return null;
      case 'keyReused':
        this.keyReused.set(outcome.expenseId ?? '');
        afterNextRender(() => this.reusedMessage()?.nativeElement.focus(), {
          injector: this.injector,
        });
        return null;
      case 'error':
        return { error: outcome.error };
    }
  }
}
