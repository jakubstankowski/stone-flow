import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  ExpenseCreated,
  ExpenseDetails,
  ExpenseInput,
  ExpenseListPage,
  ExpenseListQuery,
  ExpensePaymentMethod,
  ExpenseVersion,
} from './expense-models';

const BASE = '/api/expenses';

/** Wynik `POST /api/expenses`: `201` nowy wydatek albo `200` powtórzenie z tym samym kluczem. */
export interface CreateExpenseResponse {
  readonly created: boolean;
  readonly expense: ExpenseCreated;
}

/** Endpointy `/api/expenses` (względne adresy, JSON camelCase). Błędy jako `HttpErrorResponse`. */
@Injectable({ providedIn: 'root' })
export class ExpensesApi {
  private readonly http = inject(HttpClient);

  list(query: ExpenseListQuery): Promise<ExpenseListPage> {
    return firstValueFrom(this.http.get<ExpenseListPage>(BASE, { params: listParams(query) }));
  }

  get(id: string): Promise<ExpenseDetails> {
    return firstValueFrom(this.http.get<ExpenseDetails>(url(id)));
  }

  async create(input: ExpenseInput, idempotencyKey: string): Promise<CreateExpenseResponse> {
    const response = await firstValueFrom(
      this.http.post<ExpenseCreated>(BASE, input, {
        headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }),
        observe: 'response',
      }),
    );
    if (!response.body) {
      throw new Error('Pusta odpowiedź po zapisie wydatku.');
    }
    return { created: response.status === 201, expense: response.body };
  }

  update(id: string, input: ExpenseInput, version: string): Promise<ExpenseVersion> {
    return firstValueFrom(this.http.put<ExpenseVersion>(url(id), { ...input, version }));
  }

  markPaid(
    id: string,
    paidOn: string,
    paymentMethod: ExpensePaymentMethod | null,
    version: string,
  ): Promise<ExpenseVersion> {
    return firstValueFrom(
      this.http.post<ExpenseVersion>(`${url(id)}/mark-paid`, { paidOn, paymentMethod, version }),
    );
  }

  unmarkPaid(id: string, version: string): Promise<ExpenseVersion> {
    return firstValueFrom(this.http.post<ExpenseVersion>(`${url(id)}/unmark-paid`, { version }));
  }

  /** `DELETE` z `version` w ciele JSON, nie w adresie. */
  async delete(id: string, version: string): Promise<void> {
    await firstValueFrom(this.http.delete<void>(url(id), { body: { version } }));
  }
}

function url(id: string): string {
  return `${BASE}/${encodeURIComponent(id)}`;
}

function listParams(query: ExpenseListQuery): HttpParams {
  let params = new HttpParams()
    .set('page', query.page)
    .set('pageSize', query.pageSize)
    .set('sort', query.sort)
    .set('direction', query.direction);
  const optional: [string, string | null][] = [
    ['costDateFrom', query.costDateFrom],
    ['costDateTo', query.costDateTo],
    ['paidOnFrom', query.paidOnFrom],
    ['paidOnTo', query.paidOnTo],
    ['state', query.state],
    ['sign', query.sign],
    ['search', query.search],
    ['recurringDefinitionId', query.recurringDefinitionId],
  ];
  for (const [name, value] of optional) {
    if (value) {
      params = params.set(name, value);
    }
  }
  for (const category of query.categories) {
    params = params.append('category', category);
  }
  return params;
}
