import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiError, toApiError } from '../../core/http/api-error';
import { formatDate } from '../../shared/format/pl-format';
import { ActiveFilter, SortState } from '../../shared/ui/data-table';
import {
  CHANNEL_LABELS,
  FulfillmentStatus,
  ORDER_PROBLEM,
  OrderChannel,
  OrderDetails,
  OrderInput,
  OrderListItem,
  OrderListQuery,
  OrderSort,
  OrderSource,
  SOURCE_LABELS,
  STATUS_LABELS,
} from './order-models';
import { OrdersApi } from './orders-api';

export const ORDERS_PAGE_SIZE = 25;
export const SEARCH_MAX_LENGTH = 100;

export interface OrderFilters {
  readonly dateFrom: string;
  readonly dateTo: string;
  readonly channel: OrderChannel | '';
  readonly source: OrderSource | '';
  readonly statuses: readonly FulfillmentStatus[];
  readonly search: string;
}

export const EMPTY_FILTERS: OrderFilters = {
  dateFrom: '',
  dateTo: '',
  channel: '',
  source: '',
  statuses: [],
  search: '',
};

const DEFAULT_SORT: SortState = { key: 'orderDate', direction: 'desc' };
const SORT_KEYS: readonly OrderSort[] = ['orderDate', 'number', 'customer', 'value'];

export const LIST_ERROR_MESSAGE = 'Nie udało się wczytać zamówień.';
export const DATE_RANGE_MESSAGE = 'Data „od” nie może być późniejsza niż data „do”.';

export type LoadOrderOutcome =
  | { readonly kind: 'loaded'; readonly order: OrderDetails }
  | { readonly kind: 'notFound' }
  | { readonly kind: 'error'; readonly error: ApiError };

export type CreateOrderOutcome =
  | { readonly kind: 'saved'; readonly id: string; readonly number: string }
  | { readonly kind: 'keyReused'; readonly orderId: string; readonly orderNumber: string }
  | { readonly kind: 'error'; readonly error: ApiError };

export type ChangeOrderOutcome =
  | { readonly kind: 'saved'; readonly version: string }
  | { readonly kind: 'conflict' }
  | { readonly kind: 'notFound' }
  /** Akcja niezgodna ze stanem (`order_already_cancelled` / `order_not_cancelled`). */
  | { readonly kind: 'stateChanged'; readonly detail: string | null }
  | { readonly kind: 'error'; readonly error: ApiError };

/**
 * Stan listy zamówień na sygnałach (strona, sortowanie, filtry, wynik) oraz przepływy
 * zapisu, konfliktu i akcji. Sortowanie i paginacja odbywają się w API; zmiana filtra
 * wraca do pierwszej strony.
 */
@Injectable({ providedIn: 'root' })
export class OrdersService {
  private readonly api = inject(OrdersApi);

  private readonly pageState = signal(1);
  private readonly sortState = signal<SortState>(DEFAULT_SORT);
  private readonly filtersState = signal<OrderFilters>(EMPTY_FILTERS);
  private readonly itemsState = signal<readonly OrderListItem[]>([]);
  private readonly totalState = signal(0);
  private readonly loadingState = signal(false);
  private readonly errorState = signal<string | null>(null);
  private readonly loadedState = signal(false);
  private requestSeq = 0;
  private notice: string | null = null;

  readonly page = this.pageState.asReadonly();
  readonly pageSize = ORDERS_PAGE_SIZE;
  readonly sort = this.sortState.asReadonly();
  readonly filters = this.filtersState.asReadonly();
  readonly items = this.itemsState.asReadonly();
  readonly total = this.totalState.asReadonly();
  readonly loading = this.loadingState.asReadonly();
  readonly error = this.errorState.asReadonly();
  /** Odpowiedź API dla bieżących parametrów została już wyświetlona. */
  readonly loaded = this.loadedState.asReadonly();

  readonly dateRangeInvalid = computed(() => {
    const { dateFrom, dateTo } = this.filtersState();
    // Daty `yyyy-MM-dd` porównują się poprawnie jako tekst.
    return dateFrom !== '' && dateTo !== '' && dateFrom > dateTo;
  });

  readonly activeFilters = computed<readonly ActiveFilter[]>(() => {
    const filters = this.filtersState();
    const active: ActiveFilter[] = [];
    if (filters.dateFrom) {
      active.push({ id: 'dateFrom', label: `Od: ${formatDate(filters.dateFrom)}` });
    }
    if (filters.dateTo) {
      active.push({ id: 'dateTo', label: `Do: ${formatDate(filters.dateTo)}` });
    }
    if (filters.channel) {
      active.push({ id: 'channel', label: `Kanał: ${CHANNEL_LABELS[filters.channel]}` });
    }
    if (filters.source) {
      active.push({ id: 'source', label: `Źródło: ${SOURCE_LABELS[filters.source]}` });
    }
    for (const status of filters.statuses) {
      active.push({ id: `status:${status}`, label: `Status: ${STATUS_LABELS[status]}` });
    }
    if (filters.search.trim()) {
      active.push({ id: 'search', label: `Szukaj: ${filters.search.trim()}` });
    }
    return active;
  });

  /** Brak zamówień w ogóle: odpowiedź bez filtrów z `total = 0`. */
  readonly registryEmpty = computed(
    () =>
      this.loadedState() &&
      !this.loadingState() &&
      this.errorState() === null &&
      this.totalState() === 0 &&
      this.activeFilters().length === 0,
  );

  async load(): Promise<void> {
    const seq = ++this.requestSeq;
    if (this.dateRangeInvalid()) {
      // API odrzuca `dateFrom > dateTo`; formularz filtrów pokazuje błąd przy polu.
      this.itemsState.set([]);
      this.totalState.set(0);
      this.errorState.set(null);
      this.loadingState.set(false);
      return;
    }
    this.loadingState.set(true);
    this.errorState.set(null);
    try {
      const result = await this.api.list(this.query());
      if (seq !== this.requestSeq) {
        return;
      }
      this.itemsState.set(result.items);
      this.totalState.set(result.total);
      this.loadedState.set(true);
    } catch {
      if (seq !== this.requestSeq) {
        return;
      }
      this.itemsState.set([]);
      this.errorState.set(LIST_ERROR_MESSAGE);
    } finally {
      if (seq === this.requestSeq) {
        this.loadingState.set(false);
      }
    }
  }

  setPage(page: number): Promise<void> {
    this.pageState.set(Math.max(1, Math.floor(page)));
    return this.load();
  }

  setSort(sort: SortState): Promise<void> {
    if (!SORT_KEYS.includes(sort.key as OrderSort)) {
      return Promise.resolve();
    }
    this.sortState.set(sort);
    this.pageState.set(1);
    return this.load();
  }

  /** Każda zmiana filtra wraca do pierwszej strony. */
  updateFilters(change: Partial<OrderFilters>): Promise<void> {
    this.filtersState.update((filters) => ({ ...filters, ...change }));
    this.pageState.set(1);
    return this.load();
  }

  removeFilter(id: string): Promise<void> {
    if (id.startsWith('status:')) {
      const status = id.slice('status:'.length);
      return this.updateFilters({
        statuses: this.filtersState().statuses.filter((candidate) => candidate !== status),
      });
    }
    if (id in EMPTY_FILTERS) {
      return this.updateFilters({ [id]: EMPTY_FILTERS[id as keyof OrderFilters] });
    }
    return Promise.resolve();
  }

  clearFilters(): Promise<void> {
    return this.updateFilters(EMPTY_FILTERS);
  }

  async loadOrder(id: string): Promise<LoadOrderOutcome> {
    try {
      return { kind: 'loaded', order: await this.api.get(id) };
    } catch (error) {
      const apiError = toApiError(error);
      return isNotFound(apiError) ? { kind: 'notFound' } : { kind: 'error', error: apiError };
    }
  }

  /** `201` i `200` (powtórzenie z tym samym kluczem) dają zapis; inne dane z kluczem — `keyReused`. */
  async create(input: OrderInput, idempotencyKey: string): Promise<CreateOrderOutcome> {
    try {
      const { order } = await this.api.create(input, idempotencyKey);
      this.notice = 'Zamówienie zapisane.';
      return { kind: 'saved', id: order.id, number: order.number };
    } catch (error) {
      const reused = keyReusedOf(error);
      return reused ?? { kind: 'error', error: toApiError(error) };
    }
  }

  async update(id: string, input: OrderInput, version: string): Promise<ChangeOrderOutcome> {
    return this.change(() => this.api.update(id, { ...input, version }), 'Zmiany zapisane.');
  }

  cancel(id: string, version: string): Promise<ChangeOrderOutcome> {
    return this.change(() => this.api.cancel(id, version), 'Zamówienie anulowane.');
  }

  restore(id: string, version: string): Promise<ChangeOrderOutcome> {
    return this.change(() => this.api.restore(id, version), 'Zamówienie przywrócone.');
  }

  /** Jednorazowy komunikat sukcesu dla ekranu szczegółów. */
  takeNotice(): string | null {
    const notice = this.notice;
    this.notice = null;
    return notice;
  }

  private async change(
    call: () => Promise<{ version: string }>,
    notice: string,
  ): Promise<ChangeOrderOutcome> {
    try {
      const { version } = await call();
      this.notice = notice;
      return { kind: 'saved', version };
    } catch (error) {
      const apiError = toApiError(error);
      if (apiError.kind === 'problem') {
        if (apiError.code === ORDER_PROBLEM.conflict) {
          return { kind: 'conflict' };
        }
        if (
          apiError.code === ORDER_PROBLEM.alreadyCancelled ||
          apiError.code === ORDER_PROBLEM.notCancelled
        ) {
          return { kind: 'stateChanged', detail: apiError.detail };
        }
        if (isNotFound(apiError)) {
          return { kind: 'notFound' };
        }
      }
      return { kind: 'error', error: apiError };
    }
  }

  private query(): OrderListQuery {
    const filters = this.filtersState();
    const sort = this.sortState();
    const search = filters.search.trim();
    return {
      page: this.pageState(),
      pageSize: ORDERS_PAGE_SIZE,
      sort: sort.key as OrderSort,
      direction: sort.direction,
      dateFrom: filters.dateFrom || null,
      dateTo: filters.dateTo || null,
      channel: filters.channel || null,
      source: filters.source || null,
      statuses: filters.statuses,
      search: search ? search.slice(0, SEARCH_MAX_LENGTH) : null,
    };
  }
}

function isNotFound(error: ApiError): boolean {
  return error.kind === 'problem' && error.status === 404;
}

/** `409 idempotency_key_reused` z rozszerzeniami Problem Details `orderId` i `orderNumber`. */
function keyReusedOf(error: unknown): CreateOrderOutcome | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 409) {
    return null;
  }
  const body: unknown = error.error;
  if (typeof body !== 'object' || body === null) {
    return null;
  }
  const { code, orderId, orderNumber } = body as Record<string, unknown>;
  if (code !== ORDER_PROBLEM.keyReused || typeof orderId !== 'string') {
    return null;
  }
  return {
    kind: 'keyReused',
    orderId,
    orderNumber: typeof orderNumber === 'string' ? orderNumber : '',
  };
}
