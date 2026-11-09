import { Component, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import {
  NO_DATA,
  formatDate,
  formatDateTime,
  formatPln,
  todayInWarsaw,
} from '../../shared/format/pl-format';
import { Button } from '../../shared/ui/button';
import { ConfirmDialog } from '../../shared/ui/confirm-dialog';
import { EmptyState } from '../../shared/ui/empty-state';
import { ErrorState } from '../../shared/ui/error-state';
import { LoadingState } from '../../shared/ui/loading-state';
import { StatusBadge } from '../../shared/ui/status-badge';
import { ExpenseForm, ExpenseFormOutcome } from './expense-form';
import { ExpenseFormModel } from './expense-form-model';
import { CATEGORY_LABELS, PAYMENT_METHOD_LABELS } from './expense-models';
import {
  RECURRING_MESSAGES,
  RecurringFormOptions,
  recurringContentFrom,
  recurringErrorMessage,
  scheduleFrom,
} from './recurring-expense-form-model';
import {
  RECURRING_STATUS_LABELS,
  RECURRING_STATUS_TONES,
  RecurringExpenseDetails,
  RecurringExpenseInput,
  dayOfMonthText,
} from './recurring-expense-models';
import { nextOccurrenceText } from './recurring-expense-preview';
import { ChangeRecurringOutcome, RecurringExpensesService } from './recurring-expenses.service';

const STATE_CHANGED_MESSAGE = 'Stan wydatku cyklicznego zmienił się w międzyczasie.';

/** „Najbliższy termin” w szczegółach definicji (kolejność reguł ze spec). */
export function nextDueText(definition: RecurringExpenseDetails, today: string): string {
  if (definition.pausedOn) {
    return 'Brak — wydatek cykliczny jest wstrzymany';
  }
  const next = definition.nextDueDate;
  if (!next) {
    return 'Brak';
  }
  // Daty `yyyy-MM-dd` porównują się poprawnie jako tekst.
  if (next <= today) {
    return `Termin ${formatDate(next)} już minął, a wystąpienie jeszcze nie powstało — utworzy je zadanie automatyczne.`;
  }
  return nextOccurrenceText(next);
}

/** Treść dialogu „Wznów” wyłącznie z `resumePreview` szczegółów. */
export function resumeDialogLines(definition: RecurringExpenseDetails): readonly string[] {
  const lines: string[] = [];
  const preview = definition.resumePreview;
  if (preview) {
    if (preview.occurrencesToCreate > 0) {
      lines.push('Po wznowieniu od razu powstanie wystąpienie za bieżący okres.');
    } else if (preview.nextDueDate) {
      lines.push(`Kolejne wystąpienie powstanie ${formatDate(preview.nextDueDate)}.`);
    } else {
      lines.push('Nie powstaną kolejne wystąpienia — do końca nie wypada już żaden termin.');
    }
  }
  lines.push(
    `Okresy z terminem w czasie wstrzymania (od ${formatDate(definition.pausedOn)}) pozostaną puste.`,
  );
  return lines;
}

type ViewStatus = 'loading' | 'notFound' | 'error' | 'loaded';
type Action = 'pause' | 'resume' | 'delete';

interface Alert {
  readonly text: string;
  /** „Wczytaj ponownie” (konflikt, zmiana stanu) albo „Spróbuj ponownie” (sieć, `503`). */
  readonly action: 'reload' | 'retry' | null;
}

/**
 * Ekran `/wydatki/cykliczne/:id`: szczegóły definicji, edycja z `version`, wstrzymanie, wznowienie
 * i usunięcie po potwierdzeniu. Konflikt pokazuje „Wczytaj ponownie” i zostawia dane formularza;
 * `503` i brak połączenia — „Spróbuj ponownie” z tym samym żądaniem.
 */
@Component({
  selector: 'app-recurring-expense-details-page',
  imports: [
    Button,
    ConfirmDialog,
    EmptyState,
    ErrorState,
    ExpenseForm,
    LoadingState,
    RouterLink,
    StatusBadge,
  ],
  templateUrl: './recurring-expense-details-page.html',
})
export class RecurringExpenseDetailsPage {
  private readonly recurring = inject(RecurringExpensesService);
  private readonly router = inject(Router);

  private readonly id = toSignal(
    inject(ActivatedRoute).paramMap.pipe(map((params) => params.get('id') ?? '')),
    { requireSync: true },
  );

  protected readonly status = signal<ViewStatus>('loading');
  protected readonly definition = signal<RecurringExpenseDetails | null>(null);
  protected readonly editing = signal(false);
  protected readonly formInitial = signal<ExpenseFormModel | null>(null);
  protected readonly formOptions = signal<RecurringFormOptions | null>(null);
  protected readonly notice = signal<string | null>(this.recurring.takeNotice());
  protected readonly alert = signal<Alert | null>(null);
  protected readonly reloading = signal(false);
  protected readonly actionBusy = signal<Action | null>(null);

  protected readonly today = todayInWarsaw();
  protected readonly categoryLabels = CATEGORY_LABELS;
  protected readonly methodLabels = PAYMENT_METHOD_LABELS;
  protected readonly statusLabels = RECURRING_STATUS_LABELS;
  protected readonly statusTones = RECURRING_STATUS_TONES;
  protected readonly noData = NO_DATA;
  protected readonly formatDate = formatDate;
  protected readonly formatDateTime = formatDateTime;
  protected readonly formatPln = formatPln;
  protected readonly dayOfMonthText = dayOfMonthText;

  protected readonly canPause = computed(() => {
    const definition = this.definition();
    return definition !== null && !definition.pausedOn && definition.status === 'active';
  });
  protected readonly canResume = computed(() => Boolean(this.definition()?.pausedOn));
  protected readonly nextDue = computed(() => {
    const definition = this.definition();
    return definition ? nextDueText(definition, this.today) : NO_DATA;
  });
  protected readonly resumeLines = computed(() => {
    const definition = this.definition();
    return definition ? resumeDialogLines(definition) : [];
  });

  private readonly deleteDialog = viewChild<ConfirmDialog>('deleteDialog');
  private readonly editForm = viewChild(ExpenseForm);
  private readonly pauseDialog = viewChild<ConfirmDialog>('pauseDialog');
  private readonly resumeDialog = viewChild<ConfirmDialog>('resumeDialog');

  /** Ostatnia operacja zapisu do „Spróbuj ponownie”. */
  private lastCall: (() => Promise<void>) | null = null;

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => void this.load(id));
    });
  }

  protected retry(): Promise<void> {
    return this.load(this.id());
  }

  protected async reloadCurrent(): Promise<void> {
    this.reloading.set(true);
    try {
      await this.load(this.id(), true);
    } finally {
      this.reloading.set(false);
    }
  }

  protected async retryLast(): Promise<void> {
    const call = this.lastCall;
    if (!call || this.reloading() || this.editForm()?.saving()) {
      return;
    }
    this.reloading.set(true);
    try {
      await call();
    } finally {
      this.reloading.set(false);
    }
  }

  protected startEdit(): void {
    const definition = this.definition();
    if (!definition) {
      return;
    }
    this.notice.set(null);
    this.alert.set(null);
    this.fillForm(definition);
    this.editing.set(true);
  }

  protected closeForm(): void {
    this.editing.set(false);
    this.alert.set(null);
  }

  protected goToList(): void {
    void this.router.navigate(['/wydatki', 'cykliczne']);
  }

  protected async pause(): Promise<void> {
    const definition = this.definition();
    if (!definition || this.actionBusy() || !(await this.pauseDialog()?.open())) {
      return;
    }
    await this.runAction('pause', () => this.recurring.pause(definition.id, definition.version));
  }

  protected async resume(): Promise<void> {
    const definition = this.definition();
    if (!definition || this.actionBusy() || !(await this.resumeDialog()?.open())) {
      return;
    }
    await this.runAction('resume', () => this.recurring.resume(definition.id, definition.version));
  }

  protected async remove(): Promise<void> {
    const definition = this.definition();
    if (!definition || this.actionBusy() || !(await this.deleteDialog()?.open())) {
      return;
    }
    await this.runAction('delete', () => this.recurring.delete(definition.id, definition.version));
  }

  private fillForm(definition: RecurringExpenseDetails): void {
    this.formInitial.set(recurringContentFrom(definition));
    this.formOptions.set({
      kind: 'edit',
      schedule: scheduleFrom(definition),
      checked: true,
      definitionId: definition.id,
      paused: definition.pausedOn !== null,
      action: (input) => this.update(input),
    });
  }

  private async update(input: RecurringExpenseInput): Promise<ExpenseFormOutcome> {
    const definition = this.definition();
    if (!definition) {
      return null;
    }
    this.alert.set(null);
    const call = async (): Promise<ExpenseFormOutcome> => {
      const outcome = await this.recurring.update(definition.id, input, definition.version);
      if (outcome.kind === 'error') {
        return { error: outcome.error };
      }
      // Ponowienie zapisuje BIEŻĄCE dane formularza przez jego ścieżkę zapisu, więc błędy pól
      // (np. `400` dla `endsOn`) trafiają do formularza, a nie są porzucane.
      await this.handleChange(outcome, 'update', async () => {
        await this.editForm()?.resubmit();
      });
      return null;
    };
    return call();
  }

  private async runAction(
    action: Action,
    call: () => Promise<ChangeRecurringOutcome>,
  ): Promise<void> {
    this.actionBusy.set(action);
    this.notice.set(null);
    this.alert.set(null);
    const run = async (): Promise<void> => {
      await this.handleChange(await call(), action, run);
    };
    try {
      await run();
    } finally {
      this.actionBusy.set(null);
    }
  }

  private async handleChange(
    outcome: ChangeRecurringOutcome,
    action: Action | 'update',
    again: () => Promise<void>,
  ): Promise<void> {
    switch (outcome.kind) {
      case 'saved': {
        this.lastCall = null;
        if (action === 'delete') {
          await this.router.navigate(['/wydatki', 'cykliczne']);
          return;
        }
        const notice = this.recurring.takeNotice();
        this.editing.set(false);
        await this.load(this.id(), true);
        this.notice.set(notice);
        return;
      }
      case 'conflict':
        this.alert.set({ text: RECURRING_MESSAGES.conflict, action: 'reload' });
        return;
      case 'stateChanged':
        this.alert.set({ text: outcome.detail ?? STATE_CHANGED_MESSAGE, action: 'reload' });
        return;
      case 'retry':
        this.lastCall = again;
        this.alert.set({ text: RECURRING_MESSAGES.retry, action: 'retry' });
        return;
      case 'notFound':
        this.editing.set(false);
        this.alert.set(null);
        this.status.set('notFound');
        return;
      case 'error':
        this.alert.set({ text: recurringErrorMessage(outcome.error), action: null });
        return;
    }
  }

  private async load(id: string, keepView = false): Promise<void> {
    if (!keepView) {
      this.status.set('loading');
    }
    const outcome = await this.recurring.loadDefinition(id);
    switch (outcome.kind) {
      case 'loaded':
        this.definition.set(outcome.definition);
        this.status.set('loaded');
        this.alert.set(null);
        if (this.editing()) {
          this.fillForm(outcome.definition);
        }
        return;
      case 'notFound':
        this.definition.set(null);
        this.editing.set(false);
        this.status.set('notFound');
        return;
      case 'error':
        if (keepView && this.definition()) {
          this.alert.set({ text: 'Nie udało się wczytać wydatku cyklicznego.', action: 'reload' });
        } else {
          this.status.set('error');
        }
        return;
    }
  }
}
