import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiError, toApiError } from '../../core/http/api-error';
import { formatDate, formatPln } from '../../shared/format/pl-format';
import { ActiveFilter, SortState } from '../../shared/ui/data-table';
import {
  CHANNEL_LABELS,
  ENTRY_PROBLEM,
  FulfillmentStatus,
  ORDER_PROBLEM,
  OrderChannel,
  OrderDetails,
  OrderInput,
  OrderListItem,
  OrderListQuery,
  OrderSort,
  OrderSource,
  PAYMENT_STATE_FILTER_LABELS,
  PaymentStateFilter,
  SOURCE_LABELS,
  STATUS_LABELS,
} from './order-models';
import { EntryInputs, EntryType, OrdersApi } from './orders-api';

export const ORDERS_PAGE_SIZE = 25;
export const SEARCH_MAX_LENGTH = 100;

export interface OrderFilters {
  readonly dateFrom: string;
  readonly dateTo: string;
  readonly channel: OrderChannel | '';
  readonly source: OrderSource | '';
  readonly statuses: readonly FulfillmentStatus[];
  readonly paymentStates: readonly PaymentStateFilter[];
  readonly search: string;
  /** Pokazuje zamówienia ze sklepu ukryte domyślnie (pending, failed, checkout-draft). */
  readonly includeHiddenShopStatuses: boolean;
}

export const EMPTY_FILTERS: OrderFilters = {
  dateFrom: '',
  dateTo: '',
  channel: '',
  source: '',
  statuses: [],
  paymentStates: [],
  search: '',
  includeHiddenShopStatuses: false,
};

export const HIDDEN_SHOP_STATUSES_LABEL = 'Pokaż oczekujące, nieudane i szkice ze sklepu';

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
  /**
   * Akcja niezgodna ze stanem (`order_already_cancelled` / `order_not_cancelled`) albo niedostępna
   * anonimizacja (`order_already_anonymized`, `anonymization_disabled`, `retention_not_elapsed`).
   */
  | { readonly kind: 'stateChanged'; readonly detail: string | null }
  | { readonly kind: 'error'; readonly error: ApiError };

/** Wynik zapisu albo usunięcia wpisu płatności (SF-9). */
export type EntryOutcome =
  | { readonly kind: 'saved' }
  /** `409 idempotency_key_reused`: ten formularz zapisał już wpis z innymi danymi. */
  | { readonly kind: 'keyReused' }
  /** `409 entry_conflict`: wpis zmieniono w międzyczasie. */
  | { readonly kind: 'conflict' }
  /** `404 entry_not_found`: wpisu nie ma (usunięty albo należy do innego zamówienia). */
  | { readonly kind: 'entryNotFound' }
  | { readonly kind: 'orderNotFound' }
  /** `422`: reguła Zapłacono; komunikat zawiera kwotę `available`, gdy API ją podało. */
  | { readonly kind: 'rejected'; readonly message: string }
  | { readonly kind: 'error'; readonly error: ApiError };

export const PAID_WOULD_BE_NEGATIVE_MESSAGE =
  'Zapłacono spadłoby poniżej 0 zł. Najpierw popraw lub usuń zwrot.';
export const REFUND_EXCEEDS_PAID_MESSAGE = 'Zwrot nie może przekroczyć kwoty Zapłacono.';

export const SALES_REDUCTION_LIMIT_MESSAGE =
  'Suma zwrotów sprzedaży i korekt nie może przekroczyć wartości zamówienia.';

/** Komunikat `422 sales_reduction_limit_exceeded` przy korekcie i zwrocie, z kwotą `available`. */
export function salesReductionLimitMessage(available: string | null): string {
  return available === null
    ? SALES_REDUCTION_LIMIT_MESSAGE
    : `${SALES_REDUCTION_LIMIT_MESSAGE} Dostępna kwota: ${formatPln(available)}.`;
}

/** Komunikat limitu przy obniżeniu wartości zamówienia, z kwotą `minimumValue`. */
export function minimumValueMessage(minimumValue: string | null): string {
  return minimumValue === null
    ? 'Wartość nie może być niższa niż suma zwrotów sprzedaży i korekt.'
    : `Wartość nie może być niższa niż suma zwrotów sprzedaży i korekt (${formatPln(minimumValue)}).`;
}

/** Komunikat `422 refund_exceeds_paid` z kwotą z backendu (bez liczenia we frontendzie). */
export function refundExceedsPaidMessage(available: string | null): string {
  return available === null
    ? REFUND_EXCEEDS_PAID_MESSAGE
    : `Zwrot nie może przekroczyć kwoty Zapłacono (${formatPln(available)}).`;
}

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
    for (const state of filters.paymentStates) {
      active.push({
        id: `paymentState:${state}`,
        label: `Stan płatności: ${PAYMENT_STATE_FILTER_LABELS[state]}`,
      });
    }
    if (filters.search.trim()) {
      active.push({ id: 'search', label: `Szukaj: ${filters.search.trim()}` });
    }
    if (filters.includeHiddenShopStatuses) {
      active.push({
        id: 'includeHiddenShopStatuses',
        label: 'Ze sklepu także: oczekujące, nieudane i szkice',
      });
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
    if (id.startsWith('paymentState:')) {
      const state = id.slice('paymentState:'.length);
      return this.updateFilters({
        paymentStates: this.filtersState().paymentStates.filter((candidate) => candidate !== state),
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

  /** „Anonimizuj dane klienta” (SF-22); dostępność sprawdza API. */
  anonymize(id: string, version: string): Promise<ChangeOrderOutcome> {
    return this.change(() => this.api.anonymize(id, version), 'Dane klienta zanonimizowane.');
  }

  /** Dodanie wpłaty albo zwrotu; `200` (powtórzenie z tym samym kluczem) też jest zapisem. */
  addEntry<T extends EntryType>(
    type: T,
    orderId: string,
    input: EntryInputs[T],
    idempotencyKey: string,
  ): Promise<EntryOutcome> {
    return this.entryChange(() => this.api.addEntry(type, orderId, input, idempotencyKey));
  }

  updateEntry<T extends EntryType>(
    type: T,
    orderId: string,
    entryId: string,
    input: EntryInputs[T],
    version: string,
  ): Promise<EntryOutcome> {
    return this.entryChange(() => this.api.updateEntry(type, orderId, entryId, input, version));
  }

  deleteEntry(
    type: EntryType,
    orderId: string,
    entryId: string,
    version: string,
  ): Promise<EntryOutcome> {
    return this.entryChange(() => this.api.deleteEntry(type, orderId, entryId, version));
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
        if (apiError.code === ENTRY_PROBLEM.salesReductionLimitExceeded) {
          // SF-10: obniżenie wartości poniżej sumy obniżeń; komunikat trafia do pola `value`.
          return { kind: 'error', error: valueLimitError(error) };
        }
        if (apiError.code === ORDER_PROBLEM.conflict) {
          return { kind: 'conflict' };
        }
        if (
          apiError.code === ORDER_PROBLEM.alreadyCancelled ||
          apiError.code === ORDER_PROBLEM.notCancelled ||
          apiError.code === ORDER_PROBLEM.alreadyAnonymized ||
          apiError.code === ORDER_PROBLEM.anonymizationDisabled ||
          apiError.code === ORDER_PROBLEM.retentionNotElapsed
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

  private async entryChange(call: () => Promise<unknown>): Promise<EntryOutcome> {
    try {
      await call();
      return { kind: 'saved' };
    } catch (error) {
      return entryFailure(error);
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
      paymentStates: filters.paymentStates,
      search: search ? search.slice(0, SEARCH_MAX_LENGTH) : null,
      includeHiddenShopStatuses: filters.includeHiddenShopStatuses,
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

/** Błąd zapisu wpisu → wynik; rozszerzenie `available` czyta z ciała Problem Details. */
function entryFailure(error: unknown): EntryOutcome {
  const apiError = toApiError(error);
  if (apiError.kind !== 'problem') {
    return { kind: 'error', error: apiError };
  }
  switch (apiError.code) {
    case ENTRY_PROBLEM.conflict:
      return { kind: 'conflict' };
    case ENTRY_PROBLEM.keyReused:
      return { kind: 'keyReused' };
    case ENTRY_PROBLEM.notFound:
      return { kind: 'entryNotFound' };
    case ENTRY_PROBLEM.paidWouldBeNegative:
      return { kind: 'rejected', message: PAID_WOULD_BE_NEGATIVE_MESSAGE };
    case ENTRY_PROBLEM.refundExceedsPaid:
      return {
        kind: 'rejected',
        message: refundExceedsPaidMessage(extensionOf(error, 'available')),
      };
    case ENTRY_PROBLEM.salesReductionLimitExceeded:
      return {
        kind: 'rejected',
        message: salesReductionLimitMessage(extensionOf(error, 'available')),
      };
  }
  if (apiError.status === 404) {
    return { kind: 'orderNotFound' };
  }
  return { kind: 'error', error: apiError };
}

/** Tekstowe rozszerzenie Problem Details (`available`, `minimumValue`) albo `null`. */
function extensionOf(error: unknown, name: string): string | null {
  if (!(error instanceof HttpErrorResponse)) {
    return null;
  }
  const body: unknown = error.error;
  if (typeof body !== 'object' || body === null) {
    return null;
  }
  const value = (body as Record<string, unknown>)[name];
  return typeof value === 'string' ? value : null;
}

/** `422 sales_reduction_limit_exceeded` przy edycji zamówienia → błąd pola `value` z `minimumValue`. */
function valueLimitError(error: unknown): ApiError {
  return {
    kind: 'validation',
    fieldErrors: { value: [minimumValueMessage(extensionOf(error, 'minimumValue'))] },
    detail: null,
  };
}
