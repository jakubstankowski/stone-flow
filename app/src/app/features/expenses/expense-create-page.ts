import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Button } from '../../shared/ui/button';
import { ExpenseForm, ExpenseFormOutcome } from './expense-form';
import { ExpenseFormModel, emptyExpenseForm } from './expense-form-model';
import { ExpenseInput } from './expense-models';
import {
  RECURRING_MESSAGES,
  RecurringFormOptions,
  emptySchedule,
} from './recurring-expense-form-model';
import { RecurringExpenseInput } from './recurring-expense-models';
import { RecurringExpensesService } from './recurring-expenses.service';
import { ExpensesService } from './expenses.service';

/** Wynik zapisu definicji, który zostaje nad formularzem (SF-16). */
type RecurringNotice =
  | { readonly kind: 'keyReused'; readonly definitionId: string | null }
  | { readonly kind: 'deleted' }
  | { readonly kind: 'retry' };

export const RECURRING_DELETED_MESSAGE =
  'Ten formularz zapisał już wydatek cykliczny, który został później usunięty. Odśwież formularz, aby dodać nowy.';

/**
 * Ekran `/wydatki/nowy` (`?cykliczny=1` zaznacza „Powtarzaj co miesiąc”). Klucze idempotencji
 * wydatku i definicji powstają przy otwarciu (osobno) i zostają do udanego zapisu, więc ponowienie
 * po utracie odpowiedzi nie tworzy duplikatu. Po `409 idempotency_key_reused` nowy klucz daje tylko
 * „Dodaj kolejny wydatek”.
 */
@Component({
  selector: 'app-expense-create-page',
  imports: [Button, ExpenseForm, RouterLink],
  templateUrl: './expense-create-page.html',
})
export class ExpenseCreatePage {
  private readonly expenses = inject(ExpensesService);
  private readonly recurringExpenses = inject(RecurringExpensesService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly startsRecurring =
    inject(ActivatedRoute).snapshot.queryParamMap.get('cykliczny') === '1';

  private idempotencyKey = crypto.randomUUID();
  private recurringKey = crypto.randomUUID();

  protected readonly initial = signal<ExpenseFormModel>(emptyExpenseForm());
  protected readonly recurring = signal<RecurringFormOptions>(this.recurringOptions());
  /** Wydatek zapisany już tym formularzem (`409 idempotency_key_reused`); `''` bez identyfikatora. */
  protected readonly keyReused = signal<string | null>(null);
  protected readonly recurringNotice = signal<RecurringNotice | null>(null);
  protected readonly retrying = computed(() => this.formRef()?.saving() ?? false);
  protected readonly deletedMessage = RECURRING_DELETED_MESSAGE;
  protected readonly retryMessage = RECURRING_MESSAGES.retry;

  private readonly reusedMessage = viewChild<ElementRef<HTMLElement>>('reusedMessage');
  private readonly formRef = viewChild(ExpenseForm);

  protected readonly save = (input: ExpenseInput): Promise<ExpenseFormOutcome> =>
    this.create(input);

  protected startNext(): void {
    this.idempotencyKey = crypto.randomUUID();
    this.recurringKey = crypto.randomUUID();
    this.keyReused.set(null);
    this.recurringNotice.set(null);
    this.initial.set(emptyExpenseForm());
    this.recurring.set(this.recurringOptions());
  }

  protected goToList(): void {
    void this.router.navigate(['/wydatki']);
  }

  /**
   * „Spróbuj ponownie” po `503` albo braku połączenia: zapis BIEŻĄCYCH danych formularza (walidacja
   * i `busy` jak przy „Zapisz”) z tym samym kluczem idempotencji. Ten sam klucz jest bezpieczny także
   * po zmianie danych: jeśli pierwsze żądanie jednak się zapisało, API odpowie
   * `409 idempotency_key_reused` (nowy wydatek nie powstanie), a jeśli nie — zapisze nowe dane.
   * Nowy klucz mógłby utworzyć drugi wydatek cykliczny.
   */
  protected async retryRecurring(): Promise<void> {
    const form = this.formRef();
    if (!form || form.saving()) {
      return;
    }
    await form.resubmit();
  }

  private recurringOptions(): RecurringFormOptions {
    return {
      kind: 'create',
      schedule: emptySchedule(),
      checked: this.startsRecurring,
      definitionId: null,
      paused: false,
      action: (input) => this.createRecurring(input),
    };
  }

  private async create(input: ExpenseInput): Promise<ExpenseFormOutcome> {
    this.keyReused.set(null);
    this.recurringNotice.set(null);
    const outcome = await this.expenses.create(input, this.idempotencyKey);
    switch (outcome.kind) {
      case 'saved':
        await this.router.navigate(['/wydatki', outcome.id]);
        return null;
      case 'keyReused':
        this.keyReused.set(outcome.expenseId ?? '');
        this.focusMessage();
        return null;
      case 'error':
        return { error: outcome.error };
    }
  }

  private async createRecurring(input: RecurringExpenseInput): Promise<ExpenseFormOutcome> {
    this.keyReused.set(null);
    this.recurringNotice.set(null);
    const outcome = await this.recurringExpenses.create(input, this.recurringKey);
    switch (outcome.kind) {
      case 'saved':
        await this.router.navigate(['/wydatki', 'cykliczne', outcome.id]);
        return null;
      case 'keyReused':
        this.recurringNotice.set({ kind: 'keyReused', definitionId: outcome.definitionId });
        this.focusMessage();
        return null;
      case 'deleted':
        this.recurringNotice.set({ kind: 'deleted' });
        this.focusMessage();
        return null;
      case 'retry':
        this.recurringNotice.set({ kind: 'retry' });
        return null;
      case 'error':
        return { error: outcome.error };
    }
  }

  private focusMessage(): void {
    afterNextRender(() => this.reusedMessage()?.nativeElement.focus(), {
      injector: this.injector,
    });
  }
}
