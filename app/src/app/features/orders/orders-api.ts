import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  EntryCreated,
  OrderCreated,
  OrderDetails,
  OrderInput,
  OrderListPage,
  OrderListQuery,
  OrderUpdate,
  OrderVersion,
  PaymentInput,
  RefundInput,
  SalesCorrectionInput,
} from './order-models';

const BASE = '/api/orders';

/** Wynik `POST /api/orders`: `201` nowe zamówienie albo `200` powtórzenie z tym samym kluczem. */
export interface CreateResponse {
  readonly created: boolean;
  readonly order: OrderCreated;
}

/** Rodzaj wpisu zamówienia: wpłata i zwrot (SF-9) albo korekta sprzedaży (SF-10). */
export type EntryType = 'payment' | 'refund' | 'salesCorrection';

/** Treść zapisu danego rodzaju wpisu. */
export interface EntryInputs {
  readonly payment: PaymentInput;
  readonly refund: RefundInput;
  readonly salesCorrection: SalesCorrectionInput;
}

const ENTRY_SEGMENTS: Readonly<Record<EntryType, string>> = {
  payment: 'payments',
  refund: 'refunds',
  salesCorrection: 'sales-corrections',
};

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

  /** `POST …/payments|refunds` z `Idempotency-Key`: `201` nowy wpis, `200` powtórzenie. */
  addEntry<T extends EntryType>(
    type: T,
    orderId: string,
    input: EntryInputs[T],
    idempotencyKey: string,
  ): Promise<EntryCreated> {
    return firstValueFrom(
      this.http.post<EntryCreated>(entriesUrl(type, orderId), input, {
        headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }),
      }),
    );
  }

  updateEntry<T extends EntryType>(
    type: T,
    orderId: string,
    entryId: string,
    input: EntryInputs[T],
    version: string,
  ): Promise<OrderVersion> {
    return firstValueFrom(
      this.http.put<OrderVersion>(entryUrl(type, orderId, entryId), { ...input, version }),
    );
  }

  /** `DELETE` z `version` w ciele JSON (jak `cancel` i `restore`), nie w adresie. */
  async deleteEntry(
    type: EntryType,
    orderId: string,
    entryId: string,
    version: string,
  ): Promise<void> {
    await firstValueFrom(
      this.http.delete<void>(entryUrl(type, orderId, entryId), { body: { version } }),
    );
  }
}

function entriesUrl(type: EntryType, orderId: string): string {
  return `${BASE}/${encodeURIComponent(orderId)}/${ENTRY_SEGMENTS[type]}`;
}

function entryUrl(type: EntryType, orderId: string, entryId: string): string {
  return `${entriesUrl(type, orderId)}/${encodeURIComponent(entryId)}`;
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
  for (const state of query.paymentStates) {
    params = params.append('paymentState', state);
  }
  return params;
}
