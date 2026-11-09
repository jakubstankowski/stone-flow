import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiError, toApiError } from '../../core/http/api-error';
import { ActiveFilter, SortState } from '../../shared/ui/data-table';
import {
  RECURRING_PROBLEM,
  RECURRING_STATE_FILTER_LABELS,
  RecurringExpenseDetails,
  RecurringExpenseInput,
  RecurringExpenseListItem,
  RecurringExpenseListQuery,
  RecurringExpenseSort,
  RecurringExpenseStatus,
  occurrencesText,
} from './recurring-expense-models';
import { RecurringExpensesApi } from './recurring-expenses-api';

export const RECURRING_PAGE_SIZE = 25;
export const RECURRING_SEARCH_MAX_LENGTH = 100;
export const RECURRING_LIST_ERROR = 'Nie udało się wczytać wydatków cyklicznych.';

export interface RecurringExpenseFilters {
  readonly search: string;
  readonly state: RecurringExpenseStatus | '';
}

export const EMPTY_RECURRING_FILTERS: RecurringExpenseFilters = { search: '', state: '' };

const DEFAULT_SORT: SortState = { key: 'description', direction: 'asc' };
const SORT_KEYS: readonly RecurringExpenseSort[] = ['description', 'startsOn', 'grossAmount'];

export type LoadRecurringOutcome =
  | { readonly kind: 'loaded'; readonly definition: RecurringExpenseDetails }
  | { readonly kind: 'notFound' }
  | { readonly kind: 'error'; readonly error: ApiError };

export type CreateRecurringOutcome =
  | { readonly kind: 'saved'; readonly id: string }
  /** `409 idempotency_key_reused`: ten formularz zapisał już inną definicję. */
  | { readonly kind: 'keyReused'; readonly definitionId: string | null }
  /** `409 recurring_expense_deleted`: definicja zapisana tym kluczem została usunięta. */
  | { readonly kind: 'deleted' }
  /** Sieć albo `503`: dane zostają, „Spróbuj ponownie”. */
  | { readonly kind: 'retry' }
  | { readonly kind: 'error'; readonly error: ApiError };

export type ChangeRecurringOutcome =
  | { readonly kind: 'saved' }
  | { readonly kind: 'conflict' }
  | { readonly kind: 'notFound' }
  /** Akcja niezgodna ze stanem przy aktualnej wersji (wstrzymana, zakończona, niewstrzymana). */
  | { readonly kind: 'stateChanged'; readonly detail: string | null }
  | { readonly kind: 'retry' }
  | { readonly kind: 'error'; readonly error: ApiError };

const STATE_CODES: readonly string[] = [
  RECURRING_PROBLEM.paused,
  RECURRING_PROBLEM.alreadyPaused,
  RECURRING_PROBLEM.notPaused,
  RECURRING_PROBLEM.ended,
];

/** „Wydatek cykliczny zapisany.” z liczbą wystąpień utworzonych od razu, gdy są. */
export function savedNotice(base: string, createdOccurrences: number): string {
  return createdOccurrences > 0
    ? `${base} Utworzono ${occurrencesText(createdOccurrences)}.`
    : base;
}

/**
 * Stan listy wydatków cyklicznych na sygnałach (jak `ExpensesService`) oraz przepływy zapisu,
 * wstrzymania, wznowienia i usunięcia definicji. Filtrowanie, sortowanie i paginacja w API.
 */
@Injectable({ providedIn: 'root' })
export class RecurringExpensesService {
  private readonly api = inject(RecurringExpensesApi);

  private readonly pageState = signal(1);
  private readonly sortState = signal<SortState>(DEFAULT_SORT);
  private readonly filtersState = signal<RecurringExpenseFilters>(EMPTY_RECURRING_FILTERS);
  private readonly itemsState = signal<readonly RecurringExpenseListItem[]>([]);
  private readonly totalState = signal(0);
  private readonly loadingState = signal(false);
  private readonly errorState = signal<string | null>(null);
  private readonly loadedState = signal(false);
  private requestSeq = 0;
  private notice: string | null = null;

  readonly page = this.pageState.asReadonly();
  readonly pageSize = RECURRING_PAGE_SIZE;
  readonly sort = this.sortState.asReadonly();
  readonly filters = this.filtersState.asReadonly();
  readonly items = this.itemsState.asReadonly();
  readonly total = this.totalState.asReadonly();
  readonly loading = this.loadingState.asReadonly();
  readonly error = this.errorState.asReadonly();

  readonly activeFilters = computed<readonly ActiveFilter[]>(() => {
    const filters = this.filtersState();
    const active: ActiveFilter[] = [];
    if (filters.search.trim()) {
      active.push({ id: 'search', label: `Szukaj: ${filters.search.trim()}` });
    }
    if (filters.state) {
      active.push({ id: 'state', label: `Stan: ${RECURRING_STATE_FILTER_LABELS[filters.state]}` });
    }
    return active;
  });

  /** Brak definicji w ogóle: odpowiedź bez filtrów z `total = 0`. */
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
      this.errorState.set(RECURRING_LIST_ERROR);
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
    if (!SORT_KEYS.includes(sort.key as RecurringExpenseSort)) {
      return Promise.resolve();
    }
    this.sortState.set(sort);
    this.pageState.set(1);
    return this.load();
  }

  updateFilters(change: Partial<RecurringExpenseFilters>): Promise<void> {
    this.filtersState.update((filters) => ({ ...filters, ...change }));
    this.pageState.set(1);
    return this.load();
  }

  removeFilter(id: string): Promise<void> {
    if (id in EMPTY_RECURRING_FILTERS) {
      const key = id as keyof RecurringExpenseFilters;
      return this.updateFilters({ [key]: EMPTY_RECURRING_FILTERS[key] });
    }
    return Promise.resolve();
  }

  clearFilters(): Promise<void> {
    return this.updateFilters(EMPTY_RECURRING_FILTERS);
  }

  async loadDefinition(id: string): Promise<LoadRecurringOutcome> {
    try {
      return { kind: 'loaded', definition: await this.api.get(id) };
    } catch (error) {
      const apiError = toApiError(error);
      return isNotFound(apiError) ? { kind: 'notFound' } : { kind: 'error', error: apiError };
    }
  }

  /** `201` i `200` (powtórzenie z tym samym kluczem) dają zapis z liczbą utworzonych wystąpień. */
  async create(
    input: RecurringExpenseInput,
    idempotencyKey: string,
  ): Promise<CreateRecurringOutcome> {
    try {
      const { definition } = await this.api.create(input, idempotencyKey);
      this.notice = savedNotice('Wydatek cykliczny zapisany.', definition.createdOccurrences);
      return { kind: 'saved', id: definition.id };
    } catch (error) {
      const reused = keyReusedOf(error);
      if (reused) {
        return reused;
      }
      const apiError = toApiError(error);
      if (apiError.kind === 'network') {
        return { kind: 'retry' };
      }
      if (apiError.kind === 'problem' && apiError.code === RECURRING_PROBLEM.deleted) {
        return { kind: 'deleted' };
      }
      return { kind: 'error', error: apiError };
    }
  }

  async update(
    id: string,
    input: RecurringExpenseInput,
    version: string,
  ): Promise<ChangeRecurringOutcome> {
    return this.change(async () => {
      const saved = await this.api.update(id, input, version);
      return savedNotice('Zmiany zapisane.', saved.createdOccurrences);
    });
  }

  pause(id: string, version: string): Promise<ChangeRecurringOutcome> {
    return this.change(async () => {
      await this.api.pause(id, version);
      return 'Wstrzymano wydatek cykliczny.';
    });
  }

  resume(id: string, version: string): Promise<ChangeRecurringOutcome> {
    return this.change(async () => {
      await this.api.resume(id, version);
      return 'Wznowiono wydatek cykliczny.';
    });
  }

  delete(id: string, version: string): Promise<ChangeRecurringOutcome> {
    return this.change(async () => {
      await this.api.delete(id, version);
      return 'Wydatek cykliczny usunięty.';
    });
  }

  /** Jednorazowy komunikat sukcesu dla kolejnego ekranu. */
  takeNotice(): string | null {
    const notice = this.notice;
    this.notice = null;
    return notice;
  }

  private async change(call: () => Promise<string>): Promise<ChangeRecurringOutcome> {
    try {
      this.notice = await call();
      return { kind: 'saved' };
    } catch (error) {
      const apiError = toApiError(error);
      if (apiError.kind === 'network') {
        return { kind: 'retry' };
      }
      if (apiError.kind === 'problem') {
        if (apiError.code === RECURRING_PROBLEM.conflict) {
          return { kind: 'conflict' };
        }
        if (apiError.code !== null && STATE_CODES.includes(apiError.code)) {
          return { kind: 'stateChanged', detail: apiError.detail };
        }
        if (isNotFound(apiError)) {
          return { kind: 'notFound' };
        }
      }
      return { kind: 'error', error: apiError };
    }
  }

  private query(): RecurringExpenseListQuery {
    const filters = this.filtersState();
    const sort = this.sortState();
    const search = filters.search.trim();
    return {
      page: this.pageState(),
      pageSize: RECURRING_PAGE_SIZE,
      sort: sort.key as RecurringExpenseSort,
      direction: sort.direction,
      state: filters.state || null,
      search: search ? search.slice(0, RECURRING_SEARCH_MAX_LENGTH) : null,
    };
  }
}

function isNotFound(error: ApiError): boolean {
  return error.kind === 'problem' && error.status === 404;
}

/** `409 idempotency_key_reused` z rozszerzeniem Problem Details `recurringExpenseId`. */
function keyReusedOf(error: unknown): CreateRecurringOutcome | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 409) {
    return null;
  }
  const body: unknown = error.error;
  if (typeof body !== 'object' || body === null) {
    return null;
  }
  const record = body as Record<string, unknown>;
  if (record['code'] !== RECURRING_PROBLEM.keyReused) {
    return null;
  }
  const id = record['recurringExpenseId'];
  return { kind: 'keyReused', definitionId: typeof id === 'string' ? id : null };
}
