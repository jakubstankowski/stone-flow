import { Component, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { map } from 'rxjs';
import { NO_DATA, formatDate, formatDateTime, formatPln } from '../../shared/format/pl-format';
import { Button } from '../../shared/ui/button';
import { ConfirmDialog } from '../../shared/ui/confirm-dialog';
import { EmptyState } from '../../shared/ui/empty-state';
import { ErrorState } from '../../shared/ui/error-state';
import { LoadingState } from '../../shared/ui/loading-state';
import { StatusBadge } from '../../shared/ui/status-badge';
import { ExpenseForm, ExpenseFormOutcome } from './expense-form';
import { ExpenseFormModel, expenseErrorMessage, expenseFormFrom } from './expense-form-model';
import {
  CATEGORY_LABELS,
  ExpenseDetails,
  ExpenseInput,
  ExpensePaymentMethod,
  NEGATIVE_AMOUNT_LABEL,
  OVERDUE_LABEL,
  PAYMENT_METHOD_LABELS,
  PAYMENT_STATUS_LABELS,
  PAYMENT_STATUS_TONES,
  isNegativeAmount,
} from './expense-models';
import { ExpensePaidForm } from './expense-paid-form';
import { ChangeExpenseOutcome, ExpensesService } from './expenses.service';

export const CONFLICT_MESSAGE = 'Wydatek został zmieniony w międzyczasie. Wczytaj aktualne dane.';
const STATE_CHANGED_MESSAGE = 'Stan wydatku zmienił się w międzyczasie.';

type ViewStatus = 'loading' | 'notFound' | 'error' | 'loaded';
type Mode = 'view' | 'edit' | 'pay';

interface Alert {
  readonly text: string;
  /** Pokazuje akcję „Wczytaj ponownie”. */
  readonly reload: boolean;
}

/**
 * Ekran `/wydatki/:id`: szczegóły, poprawa z `version`, „Oznacz jako opłacony”, „Cofnij
 * oznaczenie zapłaty” i usunięcie po potwierdzeniu. Konflikt `409 expense_conflict` pokazuje
 * „Wczytaj ponownie” i zostawia wpisane dane. Wyczyszczenie daty zapłaty w formularzu używa tego
 * samego potwierdzenia co cofnięcie zapłaty.
 */
@Component({
  selector: 'app-expense-details-page',
  imports: [
    Button,
    ConfirmDialog,
    EmptyState,
    ErrorState,
    ExpenseForm,
    ExpensePaidForm,
    LoadingState,
    StatusBadge,
  ],
  templateUrl: './expense-details-page.html',
})
export class ExpenseDetailsPage {
  private readonly expenses = inject(ExpensesService);
  private readonly router = inject(Router);

  private readonly id = toSignal(
    inject(ActivatedRoute).paramMap.pipe(map((params) => params.get('id') ?? '')),
    { requireSync: true },
  );

  protected readonly status = signal<ViewStatus>('loading');
  protected readonly expense = signal<ExpenseDetails | null>(null);
  protected readonly mode = signal<Mode>('view');
  protected readonly formInitial = signal<ExpenseFormModel | null>(null);
  protected readonly notice = signal<string | null>(this.expenses.takeNotice());
  protected readonly alert = signal<Alert | null>(null);
  protected readonly reloading = signal(false);
  protected readonly actionBusy = signal<'unmark' | 'delete' | null>(null);

  protected readonly paid = computed(() => this.expense()?.paymentStatus === 'paid');
  protected readonly negative = computed(() => isNegativeAmount(this.expense()?.grossAmount));
  protected readonly categoryLabels = CATEGORY_LABELS;
  protected readonly methodLabels = PAYMENT_METHOD_LABELS;
  protected readonly statusLabels = PAYMENT_STATUS_LABELS;
  protected readonly statusTones = PAYMENT_STATUS_TONES;
  protected readonly overdueLabel = OVERDUE_LABEL;
  protected readonly negativeLabel = NEGATIVE_AMOUNT_LABEL;
  protected readonly noData = NO_DATA;
  protected readonly formatDate = formatDate;
  protected readonly formatDateTime = formatDateTime;
  protected readonly formatPln = formatPln;

  private readonly deleteDialog = viewChild<ConfirmDialog>('deleteDialog');
  private readonly unmarkDialog = viewChild<ConfirmDialog>('unmarkDialog');

  protected readonly save = (input: ExpenseInput): Promise<ExpenseFormOutcome> =>
    this.update(input);
  /** Potwierdzenie wyczyszczenia daty zapłaty w formularzu (tylko UI). */
  protected readonly confirmClearPaid = async (): Promise<boolean> =>
    (await this.unmarkDialog()?.open()) ?? false;
  protected readonly pay = (
    paidOn: string,
    paymentMethod: ExpensePaymentMethod | null,
  ): Promise<string | null> => this.markPaid(paidOn, paymentMethod);

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => void this.load(id));
    });
  }

  protected retry(): Promise<void> {
    return this.load(this.id());
  }

  /** Zastępuje widok i formularz stanem z serwera (po konflikcie albo zmianie stanu). */
  protected async reloadCurrent(): Promise<void> {
    this.reloading.set(true);
    try {
      await this.load(this.id(), true);
    } finally {
      this.reloading.set(false);
    }
  }

  protected startEdit(): void {
    const expense = this.expense();
    if (!expense) {
      return;
    }
    this.notice.set(null);
    this.alert.set(null);
    this.formInitial.set(expenseFormFrom(expense));
    this.mode.set('edit');
  }

  protected startPay(): void {
    this.notice.set(null);
    this.alert.set(null);
    this.mode.set('pay');
  }

  protected closeForm(): void {
    this.mode.set('view');
    this.alert.set(null);
  }

  protected goToList(): void {
    void this.router.navigate(['/wydatki']);
  }

  protected async unmark(): Promise<void> {
    const expense = this.expense();
    if (!expense || this.actionBusy() || !(await this.unmarkDialog()?.open())) {
      return;
    }
    await this.runAction('unmark', () => this.expenses.unmarkPaid(expense.id, expense.version));
  }

  protected async remove(): Promise<void> {
    const expense = this.expense();
    if (!expense || this.actionBusy() || !(await this.deleteDialog()?.open())) {
      return;
    }
    this.actionBusy.set('delete');
    this.notice.set(null);
    this.alert.set(null);
    try {
      const outcome = await this.expenses.delete(expense.id, expense.version);
      if (outcome.kind === 'saved') {
        await this.router.navigate(['/wydatki']);
        return;
      }
      await this.handleChange(outcome);
    } finally {
      this.actionBusy.set(null);
    }
  }

  private async update(input: ExpenseInput): Promise<ExpenseFormOutcome> {
    const expense = this.expense();
    if (!expense) {
      return null;
    }
    this.alert.set(null);
    const outcome = await this.expenses.update(expense.id, input, expense.version);
    if (outcome.kind === 'error') {
      return { error: outcome.error };
    }
    await this.handleChange(outcome);
    return null;
  }

  private async markPaid(
    paidOn: string,
    paymentMethod: ExpensePaymentMethod | null,
  ): Promise<string | null> {
    const expense = this.expense();
    if (!expense) {
      return null;
    }
    this.alert.set(null);
    const outcome = await this.expenses.markPaid(
      expense.id,
      paidOn,
      paymentMethod,
      expense.version,
    );
    if (outcome.kind === 'error') {
      return outcome.error.kind === 'validation'
        ? (Object.values(outcome.error.fieldErrors)[0]?.[0] ?? expenseErrorMessage(outcome.error))
        : expenseErrorMessage(outcome.error);
    }
    await this.handleChange(outcome);
    return null;
  }

  private async runAction(
    action: 'unmark',
    call: () => Promise<ChangeExpenseOutcome>,
  ): Promise<void> {
    this.actionBusy.set(action);
    this.notice.set(null);
    this.alert.set(null);
    try {
      await this.handleChange(await call());
    } finally {
      this.actionBusy.set(null);
    }
  }

  private async handleChange(outcome: ChangeExpenseOutcome): Promise<void> {
    switch (outcome.kind) {
      case 'saved': {
        const notice = this.expenses.takeNotice();
        this.mode.set('view');
        await this.load(this.id(), true);
        this.notice.set(notice);
        return;
      }
      case 'conflict':
        this.alert.set({ text: CONFLICT_MESSAGE, reload: true });
        return;
      case 'stateChanged':
        this.alert.set({ text: outcome.detail ?? STATE_CHANGED_MESSAGE, reload: true });
        return;
      case 'notFound':
        // Pusty stan „Nie znaleziono wydatku” mówi to sam; bez drugiego komunikatu w alercie.
        this.mode.set('view');
        this.alert.set(null);
        this.status.set('notFound');
        return;
      case 'error':
        this.alert.set({ text: expenseErrorMessage(outcome.error), reload: false });
        return;
    }
  }

  private async load(id: string, keepView = false): Promise<void> {
    if (!keepView) {
      this.status.set('loading');
    }
    const outcome = await this.expenses.loadExpense(id);
    switch (outcome.kind) {
      case 'loaded':
        this.expense.set(outcome.expense);
        this.status.set('loaded');
        this.alert.set(null);
        if (this.mode() === 'edit') {
          this.formInitial.set(expenseFormFrom(outcome.expense));
        }
        return;
      case 'notFound':
        this.expense.set(null);
        this.mode.set('view');
        this.status.set('notFound');
        return;
      case 'error':
        if (keepView && this.expense()) {
          this.alert.set({ text: expenseErrorMessage(outcome.error), reload: true });
        } else {
          this.status.set('error');
        }
        return;
    }
  }
}
