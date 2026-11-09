import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  WithdrawalCreated,
  WithdrawalDetails,
  WithdrawalInput,
  WithdrawalVersion,
} from './withdrawal-models';

const BASE = '/api/owner-withdrawals';

/** Wynik `POST`: `201` nowa wypłata albo `200` powtórzenie z tym samym kluczem. */
export interface CreateWithdrawalResponse {
  readonly created: boolean;
  readonly withdrawal: WithdrawalCreated;
}

/** Endpointy `/api/owner-withdrawals` (względne adresy, JSON camelCase). Błędy jako `HttpErrorResponse`. */
@Injectable({ providedIn: 'root' })
export class OwnerWithdrawalsApi {
  private readonly http = inject(HttpClient);

  async people(): Promise<readonly string[]> {
    const response = await firstValueFrom(
      this.http.get<{ people: readonly string[] }>(`${BASE}/people`),
    );
    return response.people;
  }

  get(id: string): Promise<WithdrawalDetails> {
    return firstValueFrom(this.http.get<WithdrawalDetails>(url(id)));
  }

  async create(input: WithdrawalInput, idempotencyKey: string): Promise<CreateWithdrawalResponse> {
    const response = await firstValueFrom(
      this.http.post<WithdrawalCreated>(BASE, input, {
        headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }),
        observe: 'response',
      }),
    );
    if (!response.body) {
      throw new Error('Pusta odpowiedź po zapisie wypłaty.');
    }
    return { created: response.status === 201, withdrawal: response.body };
  }

  update(id: string, input: WithdrawalInput, version: string): Promise<WithdrawalVersion> {
    return firstValueFrom(this.http.put<WithdrawalVersion>(url(id), { ...input, version }));
  }

  /** `DELETE` z `version` w ciele JSON, nie w adresie. */
  async delete(id: string, version: string): Promise<void> {
    await firstValueFrom(this.http.delete<void>(url(id), { body: { version } }));
  }
}

function url(id: string): string {
  return `${BASE}/${encodeURIComponent(id)}`;
}
