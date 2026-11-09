import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  RecurringExpenseCreated,
  RecurringExpenseDetails,
  RecurringExpenseInput,
  RecurringExpenseListPage,
  RecurringExpenseListQuery,
  RecurringExpensePreview,
  RecurringExpenseSaved,
  RecurringPreviewQuery,
} from './recurring-expense-models';

const BASE = '/api/recurring-expenses';

/** Wynik `POST`: `201` nowa definicja albo `200` powtórzenie z tym samym kluczem. */
export interface CreateRecurringExpenseResponse {
  readonly created: boolean;
  readonly definition: RecurringExpenseCreated;
}

/** Endpointy `/api/recurring-expenses` (względne adresy, JSON camelCase). Błędy jako `HttpErrorResponse`. */
@Injectable({ providedIn: 'root' })
export class RecurringExpensesApi {
  private readonly http = inject(HttpClient);

  list(query: RecurringExpenseListQuery): Promise<RecurringExpenseListPage> {
    return firstValueFrom(
      this.http.get<RecurringExpenseListPage>(BASE, { params: listParams(query) }),
    );
  }

  get(id: string): Promise<RecurringExpenseDetails> {
    return firstValueFrom(this.http.get<RecurringExpenseDetails>(url(id)));
  }

  preview(query: RecurringPreviewQuery): Promise<RecurringExpensePreview> {
    let params = new HttpParams()
      .set('dayOfMonth', query.dayOfMonth)
      .set('startsOn', query.startsOn);
    if (query.endsOn) {
      params = params.set('endsOn', query.endsOn);
    }
    if (query.definitionId) {
      params = params.set('definitionId', query.definitionId);
    }
    return firstValueFrom(this.http.get<RecurringExpensePreview>(`${BASE}/preview`, { params }));
  }

  async create(
    input: RecurringExpenseInput,
    idempotencyKey: string,
  ): Promise<CreateRecurringExpenseResponse> {
    const response = await firstValueFrom(
      this.http.post<RecurringExpenseCreated>(BASE, input, {
        headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }),
        observe: 'response',
      }),
    );
    if (!response.body) {
      throw new Error('Pusta odpowiedź po zapisie wydatku cyklicznego.');
    }
    return { created: response.status === 201, definition: response.body };
  }

  update(
    id: string,
    input: RecurringExpenseInput,
    version: string,
  ): Promise<RecurringExpenseSaved> {
    return firstValueFrom(this.http.put<RecurringExpenseSaved>(url(id), { ...input, version }));
  }

  pause(id: string, version: string): Promise<RecurringExpenseSaved> {
    return firstValueFrom(this.http.post<RecurringExpenseSaved>(`${url(id)}/pause`, { version }));
  }

  resume(id: string, version: string): Promise<RecurringExpenseSaved> {
    return firstValueFrom(this.http.post<RecurringExpenseSaved>(`${url(id)}/resume`, { version }));
  }

  /** `DELETE` z `version` w ciele JSON, nie w adresie. */
  async delete(id: string, version: string): Promise<void> {
    await firstValueFrom(this.http.delete<void>(url(id), { body: { version } }));
  }
}

function url(id: string): string {
  return `${BASE}/${encodeURIComponent(id)}`;
}

function listParams(query: RecurringExpenseListQuery): HttpParams {
  let params = new HttpParams()
    .set('page', query.page)
    .set('pageSize', query.pageSize)
    .set('sort', query.sort)
    .set('direction', query.direction);
  if (query.state) {
    params = params.set('state', query.state);
  }
  if (query.search) {
    params = params.set('search', query.search);
  }
  return params;
}
