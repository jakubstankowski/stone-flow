import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { ApiError, toApiError } from '../../core/http/api-error';
import { OwnerWithdrawalsApi } from './owner-withdrawals-api';
import { WITHDRAWAL_PROBLEM, WithdrawalDetails, WithdrawalInput } from './withdrawal-models';

export type LoadWithdrawalOutcome =
  | { readonly kind: 'loaded'; readonly withdrawal: WithdrawalDetails }
  | { readonly kind: 'notFound' }
  | { readonly kind: 'error'; readonly error: ApiError };

export type CreateWithdrawalOutcome =
  | { readonly kind: 'saved'; readonly id: string }
  | { readonly kind: 'keyReused'; readonly withdrawalId: string | null }
  | { readonly kind: 'error'; readonly error: ApiError };

export type ChangeWithdrawalOutcome =
  | { readonly kind: 'saved' }
  | { readonly kind: 'conflict' }
  | { readonly kind: 'notFound' }
  | { readonly kind: 'error'; readonly error: ApiError };

/**
 * Przepływy wypłaty właściciela: odczyt, dodanie z `Idempotency-Key`, poprawa i usunięcie z
 * wersją, podpowiedzi osób oraz jednorazowy komunikat sukcesu dla kolejnego ekranu.
 */
@Injectable({ providedIn: 'root' })
export class OwnerWithdrawalsService {
  private readonly api = inject(OwnerWithdrawalsApi);
  private notice: string | null = null;

  /** Podpowiedzi są tylko pomocą: błąd odczytu daje pustą listę. */
  async people(): Promise<readonly string[]> {
    try {
      return await this.api.people();
    } catch {
      return [];
    }
  }

  async load(id: string): Promise<LoadWithdrawalOutcome> {
    try {
      return { kind: 'loaded', withdrawal: await this.api.get(id) };
    } catch (error) {
      const apiError = toApiError(error);
      return isNotFound(apiError) ? { kind: 'notFound' } : { kind: 'error', error: apiError };
    }
  }

  /** `201` i `200` (powtórzenie z tym samym kluczem) dają zapis; inne dane z kluczem — `keyReused`. */
  async create(input: WithdrawalInput, idempotencyKey: string): Promise<CreateWithdrawalOutcome> {
    try {
      const { withdrawal } = await this.api.create(input, idempotencyKey);
      this.notice = 'Wypłata zapisana.';
      return { kind: 'saved', id: withdrawal.id };
    } catch (error) {
      return keyReusedOf(error) ?? { kind: 'error', error: toApiError(error) };
    }
  }

  update(id: string, input: WithdrawalInput, version: string): Promise<ChangeWithdrawalOutcome> {
    return this.change(() => this.api.update(id, input, version), 'Zmiany zapisane.');
  }

  delete(id: string, version: string): Promise<ChangeWithdrawalOutcome> {
    return this.change(() => this.api.delete(id, version), 'Wypłata usunięta.');
  }

  takeNotice(): string | null {
    const notice = this.notice;
    this.notice = null;
    return notice;
  }

  private async change(
    call: () => Promise<unknown>,
    notice: string,
  ): Promise<ChangeWithdrawalOutcome> {
    try {
      await call();
      this.notice = notice;
      return { kind: 'saved' };
    } catch (error) {
      const apiError = toApiError(error);
      if (apiError.kind === 'problem') {
        if (apiError.code === WITHDRAWAL_PROBLEM.conflict) {
          return { kind: 'conflict' };
        }
        if (isNotFound(apiError)) {
          return { kind: 'notFound' };
        }
      }
      return { kind: 'error', error: apiError };
    }
  }
}

function isNotFound(error: ApiError): boolean {
  return error.kind === 'problem' && error.status === 404;
}

/** `409 idempotency_key_reused` z rozszerzeniem Problem Details `ownerWithdrawalId`. */
function keyReusedOf(error: unknown): CreateWithdrawalOutcome | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 409) {
    return null;
  }
  const body: unknown = error.error;
  if (typeof body !== 'object' || body === null) {
    return null;
  }
  const record = body as Record<string, unknown>;
  if (record['code'] !== WITHDRAWAL_PROBLEM.keyReused) {
    return null;
  }
  const id = record['ownerWithdrawalId'];
  return { kind: 'keyReused', withdrawalId: typeof id === 'string' ? id : null };
}
