import { Component, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { map } from 'rxjs';
import { formatDate, formatDateTime, formatPln } from '../../shared/format/pl-format';
import { Button } from '../../shared/ui/button';
import { ConfirmDialog } from '../../shared/ui/confirm-dialog';
import { EmptyState } from '../../shared/ui/empty-state';
import { ErrorState } from '../../shared/ui/error-state';
import { LoadingState } from '../../shared/ui/loading-state';
import { ChangeWithdrawalOutcome, OwnerWithdrawalsService } from './owner-withdrawals.service';
import { WithdrawalForm, WithdrawalFormOutcome } from './withdrawal-form';
import {
  WITHDRAWAL_MESSAGES,
  WithdrawalFormModel,
  withdrawalErrorMessage,
  withdrawalFormFrom,
} from './withdrawal-form-model';
import { WITHDRAWAL_METHOD_LABELS, WithdrawalDetails, WithdrawalInput } from './withdrawal-models';

export const CONFLICT_MESSAGE = 'Wypłata została zmieniona w międzyczasie. Wczytaj aktualne dane.';

type ViewStatus = 'loading' | 'notFound' | 'error' | 'loaded';
type Mode = 'view' | 'edit';

interface Alert {
  readonly text: string;
  /** Pokazuje akcję „Wczytaj ponownie”. */
  readonly reload: boolean;
}

/**
 * Ekran `/operacje/wyplaty/:id`: szczegóły wypłaty właściciela, poprawa w miejscu z `version` i
 * usunięcie po potwierdzeniu. Konflikt `409 owner_withdrawal_conflict` pokazuje „Wczytaj ponownie”
 * i zostawia wpisane dane.
 */
@Component({
  selector: 'app-withdrawal-details-page',
  imports: [Button, ConfirmDialog, EmptyState, ErrorState, LoadingState, WithdrawalForm],
  templateUrl: './withdrawal-details-page.html',
})
export class WithdrawalDetailsPage {
  private readonly withdrawals = inject(OwnerWithdrawalsService);
  private readonly router = inject(Router);

  private readonly id = toSignal(
    inject(ActivatedRoute).paramMap.pipe(map((params) => params.get('id') ?? '')),
    { requireSync: true },
  );

  protected readonly status = signal<ViewStatus>('loading');
  protected readonly withdrawal = signal<WithdrawalDetails | null>(null);
  protected readonly mode = signal<Mode>('view');
  protected readonly formInitial = signal<WithdrawalFormModel | null>(null);
  protected readonly people = signal<readonly string[]>([]);
  protected readonly notice = signal<string | null>(this.withdrawals.takeNotice());
  protected readonly alert = signal<Alert | null>(null);
  protected readonly reloading = signal(false);
  protected readonly deleting = signal(false);

  protected readonly methodLabels = WITHDRAWAL_METHOD_LABELS;
  protected readonly formatDate = formatDate;
  protected readonly formatDateTime = formatDateTime;
  protected readonly formatPln = formatPln;

  private readonly deleteDialog = viewChild<ConfirmDialog>('deleteDialog');
  private peopleRequested = false;

  protected readonly save = (input: WithdrawalInput): Promise<WithdrawalFormOutcome> =>
    this.update(input);

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => void this.load(id));
    });
  }

  protected retry(): Promise<void> {
    return this.load(this.id());
  }

  /** Zastępuje widok i formularz stanem z serwera (po konflikcie). */
  protected async reloadCurrent(): Promise<void> {
    this.reloading.set(true);
    try {
      await this.load(this.id(), true);
    } finally {
      this.reloading.set(false);
    }
  }

  protected startEdit(): void {
    const withdrawal = this.withdrawal();
    if (!withdrawal) {
      return;
    }
    this.notice.set(null);
    this.alert.set(null);
    this.formInitial.set(withdrawalFormFrom(withdrawal));
    this.mode.set('edit');
    if (!this.peopleRequested) {
      this.peopleRequested = true;
      void this.withdrawals.people().then((people) => this.people.set(people));
    }
  }

  protected closeForm(): void {
    this.mode.set('view');
    this.alert.set(null);
  }

  protected goToList(): void {
    void this.router.navigate(['/operacje']);
  }

  protected async remove(): Promise<void> {
    const withdrawal = this.withdrawal();
    if (!withdrawal || this.deleting() || !(await this.deleteDialog()?.open())) {
      return;
    }
    this.deleting.set(true);
    this.notice.set(null);
    this.alert.set(null);
    try {
      const outcome = await this.withdrawals.delete(withdrawal.id, withdrawal.version);
      if (outcome.kind === 'saved') {
        await this.router.navigate(['/operacje']);
        return;
      }
      await this.handleChange(outcome, WITHDRAWAL_MESSAGES.deleteFailed);
    } finally {
      this.deleting.set(false);
    }
  }

  private async update(input: WithdrawalInput): Promise<WithdrawalFormOutcome> {
    const withdrawal = this.withdrawal();
    if (!withdrawal) {
      return null;
    }
    this.alert.set(null);
    const outcome = await this.withdrawals.update(withdrawal.id, input, withdrawal.version);
    if (outcome.kind === 'error') {
      return { error: outcome.error };
    }
    await this.handleChange(outcome, WITHDRAWAL_MESSAGES.saveFailed);
    return null;
  }

  private async handleChange(outcome: ChangeWithdrawalOutcome, fallback: string): Promise<void> {
    switch (outcome.kind) {
      case 'saved': {
        const notice = this.withdrawals.takeNotice();
        this.mode.set('view');
        await this.load(this.id(), true);
        this.notice.set(notice);
        return;
      }
      case 'conflict':
        this.alert.set({ text: CONFLICT_MESSAGE, reload: true });
        return;
      case 'notFound':
        // Pusty stan „Nie znaleziono wypłaty” mówi to sam; bez drugiego komunikatu w alercie.
        this.mode.set('view');
        this.alert.set(null);
        this.status.set('notFound');
        return;
      case 'error':
        this.alert.set({ text: withdrawalErrorMessage(outcome.error, fallback), reload: false });
        return;
    }
  }

  private async load(id: string, keepView = false): Promise<void> {
    if (!keepView) {
      this.status.set('loading');
    }
    const outcome = await this.withdrawals.load(id);
    switch (outcome.kind) {
      case 'loaded':
        this.withdrawal.set(outcome.withdrawal);
        this.status.set('loaded');
        this.alert.set(null);
        if (this.mode() === 'edit') {
          this.formInitial.set(withdrawalFormFrom(outcome.withdrawal));
        }
        return;
      case 'notFound':
        this.withdrawal.set(null);
        this.mode.set('view');
        this.status.set('notFound');
        return;
      case 'error':
        if (keepView && this.withdrawal()) {
          this.alert.set({
            text: withdrawalErrorMessage(outcome.error, 'Nie udało się wczytać wypłaty.'),
            reload: true,
          });
        } else {
          this.status.set('error');
        }
        return;
    }
  }
}
