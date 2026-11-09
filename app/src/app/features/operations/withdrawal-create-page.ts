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
import { OwnerWithdrawalsService } from './owner-withdrawals.service';
import { WithdrawalForm, WithdrawalFormOutcome } from './withdrawal-form';
import { WithdrawalFormModel, emptyWithdrawalForm } from './withdrawal-form-model';
import { WithdrawalInput } from './withdrawal-models';

/**
 * Ekran `/operacje/wyplaty/nowa`. Klucz idempotencji powstaje przy otwarciu i zostaje do udanego
 * zapisu, więc ponowienie po błędzie nie tworzy duplikatu. Po `409 idempotency_key_reused` nowy
 * klucz daje tylko „Dodaj kolejną wypłatę”.
 */
@Component({
  selector: 'app-withdrawal-create-page',
  imports: [Button, RouterLink, WithdrawalForm],
  templateUrl: './withdrawal-create-page.html',
})
export class WithdrawalCreatePage {
  private readonly withdrawals = inject(OwnerWithdrawalsService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);

  private idempotencyKey = crypto.randomUUID();

  protected readonly initial = signal<WithdrawalFormModel>(emptyWithdrawalForm());
  protected readonly people = signal<readonly string[]>([]);
  /** Wypłata zapisana już tym formularzem (`409 idempotency_key_reused`); `''` bez identyfikatora. */
  protected readonly keyReused = signal<string | null>(null);

  private readonly reusedMessage = viewChild<ElementRef<HTMLElement>>('reusedMessage');

  protected readonly save = (input: WithdrawalInput): Promise<WithdrawalFormOutcome> =>
    this.create(input);

  constructor() {
    void this.withdrawals.people().then((people) => this.people.set(people));
  }

  protected startNext(): void {
    this.idempotencyKey = crypto.randomUUID();
    this.keyReused.set(null);
    this.initial.set(emptyWithdrawalForm());
  }

  protected goToList(): void {
    void this.router.navigate(['/operacje']);
  }

  private async create(input: WithdrawalInput): Promise<WithdrawalFormOutcome> {
    this.keyReused.set(null);
    const outcome = await this.withdrawals.create(input, this.idempotencyKey);
    switch (outcome.kind) {
      case 'saved':
        await this.router.navigate(['/operacje', 'wyplaty', outcome.id]);
        return null;
      case 'keyReused':
        this.keyReused.set(outcome.withdrawalId ?? '');
        afterNextRender(() => this.reusedMessage()?.nativeElement.focus(), {
          injector: this.injector,
        });
        return null;
      case 'error':
        return { error: outcome.error };
    }
  }
}
