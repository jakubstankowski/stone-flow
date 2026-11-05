import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  OrderCreated,
  OrderDetails,
  OrderInput,
  OrderListPage,
  OrderListQuery,
  OrderUpdate,
  OrderVersion,
} from './order-models';

const BASE = '/api/orders';

/** Wynik `POST /api/orders`: `201` nowe zamówienie albo `200` powtórzenie z tym samym kluczem. */
export interface CreateResponse {
  readonly created: boolean;
  readonly order: OrderCreated;
}

/** Endpointy `/api/orders` (względne adresy, JSON camelCase). Błędy jako `HttpErrorResponse`. */
@Injectable({ providedIn: 'root' })
export class OrdersApi {
  private readonly http = inject(HttpClient);

  list(query: OrderListQuery): Promise<OrderListPage> {
    return firstValueFrom(this.http.get<OrderListPage>(BASE, { params: listParams(query) }));
  }

  get(id: string): Promise<OrderDetails> {
    return firstValueFrom(this.http.get<OrderDetails>(`${BASE}/${encodeURIComponent(id)}`));
  }

  async create(input: OrderInput, idempotencyKey: string): Promise<CreateResponse> {
    const response = await firstValueFrom(
      this.http.post<OrderCreated>(BASE, input, {
        headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }),
        observe: 'response',
      }),
    );
    if (!response.body) {
      throw new Error('Pusta odpowiedź po zapisie zamówienia.');
    }
    return { created: response.status === 201, order: response.body };
  }

  update(id: string, update: OrderUpdate): Promise<OrderVersion> {
    return firstValueFrom(this.http.put<OrderVersion>(`${BASE}/${encodeURIComponent(id)}`, update));
  }

  cancel(id: string, version: string): Promise<OrderVersion> {
    return firstValueFrom(
      this.http.post<OrderVersion>(`${BASE}/${encodeURIComponent(id)}/cancel`, { version }),
    );
  }

  restore(id: string, version: string): Promise<OrderVersion> {
    return firstValueFrom(
      this.http.post<OrderVersion>(`${BASE}/${encodeURIComponent(id)}/restore`, { version }),
    );
  }
}

function listParams(query: OrderListQuery): HttpParams {
  let params = new HttpParams()
    .set('page', query.page)
    .set('pageSize', query.pageSize)
    .set('sort', query.sort)
    .set('direction', query.direction);
  const optional: [string, string | null][] = [
    ['dateFrom', query.dateFrom],
    ['dateTo', query.dateTo],
    ['channel', query.channel],
    ['source', query.source],
    ['search', query.search],
  ];
  for (const [name, value] of optional) {
    if (value) {
      params = params.set(name, value);
    }
  }
  for (const status of query.statuses) {
    params = params.append('status', status);
  }
  return params;
}
