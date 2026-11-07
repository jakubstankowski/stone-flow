import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiError, toApiError } from '../../core/http/api-error';
import { formatDate } from '../../shared/format/pl-format';
import { ActiveFilter, SortState } from '../../shared/ui/data-table';
import {
  CATEGORIES,
  CATEGORY_LABELS,
  EXPENSE_PROBLEM,
  ExpenseCategory,
  ExpenseDetails,
  ExpenseInput,
  ExpenseListItem,
  ExpenseListQuery,
  ExpensePaymentMethod,
  ExpenseSignFilter,
  ExpenseSort,
  ExpenseStateFilter,
  SIGN_FILTER_LABELS,
  STATE_FILTER_LABELS,
} from './expense-models';
import { ExpensesApi } from './expenses-api';

export const EXPENSES_PAGE_SIZE = 25;
export const SEARCH_MAX_LENGTH = 100;

export interface ExpenseFilters {
  readonly search: string;
  readonly costDateFrom: string;
  readonly costDateTo: string;
  readonly paidOnFrom: string;
  readonly paidOnTo: string;
  readonly categories: readonly ExpenseCategory[];
  readonly state: ExpenseStateFilter | '';
  readonly sign: ExpenseSignFilter | '';
}

export const EMPTY_FILTERS: ExpenseFilters = {
  search: '',
  costDateFrom: '',
  costDateTo: '',
  paidOnFrom: '',
  paidOnTo: '',
  categories: [],
  state: '',
  sign: '',
};

const DEFAULT_SORT: SortState = { key: 'costDate', direction: 'desc' };
const SORT_KEYS: readonly ExpenseSort[] = ['costDate', 'dueDate', 'paidOn', 'grossAmount'];

export const LIST_ERROR_MESSAGE = 'Nie udało się wczytać wydatków.';
export const DATE_RANGE_MESSAGE = 'Data „od” nie może być późniejsza niż data „do”.';

export type LoadExpenseOutcome =
  | { readonly kind: 'loaded'; readonly expense: ExpenseDetails }
  | { readonly kind: 'notFound' }
  | { readonly kind: 'error'; readonly error: ApiError };

export type CreateExpenseOutcome =
  | { readonly kind: 'saved'; readonly id: string }
  | { readonly kind: 'keyReused'; readonly expenseId: string | null }
  | { readonly kind: 'error'; readonly error: ApiError };

export type ChangeExpenseOutcome =
  | { readonly kind: 'saved' }
  | { readonly kind: 'conflict' }
  | { readonly kind: 'notFound' }
  /** Akcja niezgodna ze stanem przy aktualnej wersji (`expense_already_paid` / `expense_not_paid`). */
  | { readonly kind: 'stateChanged'; readonly detail: string | null }
  | { readonly kind: 'error'; readonly error: ApiError };

/**
 * Stan listy wydatków na sygnałach (strona, sortowanie, filtry, wynik) oraz przepływy zapisu,
 * akcji zapłaty i usunięcia. Filtrowanie, sortowanie i paginacja odbywają się w API; zmiana filtra
 * wraca do pierwszej strony. Stan żyje w serwisie, więc powrót ze szczegółów zachowuje widok.
 */
@Injectable({ providedIn: 'root' })
export class ExpensesService {
  private readonly api = inject(ExpensesApi);

  private readonly pageState = signal(1);
  private readonly sortState = signal<SortState>(DEFAULT_SORT);
  private readonly filtersState = signal<ExpenseFilters>(EMPTY_FILTERS);
  private readonly itemsState = signal<readonly ExpenseListItem[]>([]);
  private readonly totalState = signal(0);
  private readonly loadingState = signal(false);
  private readonly errorState = signal<string | null>(null);
  private readonly loadedState = signal(false);
  private requestSeq = 0;
  private notice: string | null = null;

  readonly page = this.pageState.asReadonly();
  readonly pageSize = EXPENSES_PAGE_SIZE;
  readonly sort = this.sortState.asReadonly();
  readonly filters = this.filtersState.asReadonly();
  readonly items = this.itemsState.asReadonly();
  readonly total = this.totalState.asReadonly();
  readonly loading = this.loadingState.asReadonly();
  readonly error = this.errorState.asReadonly();
  readonly loaded = this.loadedState.asReadonly();

  // Daty `yyyy-MM-dd` porównują się poprawnie jako tekst.
  readonly costDateRangeInvalid = computed(() => {
    const { costDateFrom, costDateTo } = this.filtersState();
    return costDateFrom !== '' && costDateTo !== '' && costDateFrom > costDateTo;
  });

  readonly paidOnRangeInvalid = computed(() => {
    const { paidOnFrom, paidOnTo } = this.filtersState();
    return paidOnFrom !== '' && paidOnTo !== '' && paidOnFrom > paidOnTo;
  });

  /** Aktywny filtr daty: pusty wynik dostaje wtedy tekst „w wybranym okresie” (design guide §8). */
  readonly dateFilterActive = computed(() => {
    const { costDateFrom, costDateTo, paidOnFrom, paidOnTo } = this.filtersState();
    return [costDateFrom, costDateTo, paidOnFrom, paidOnTo].some((value) => value !== '');
  });

  readonly activeFilters = computed<readonly ActiveFilter[]>(() => {
    const filters = this.filtersState();
    const active: ActiveFilter[] = [];
    if (filters.search.trim()) {
      active.push({ id: 'search', label: `Szukaj: ${filters.search.trim()}` });
    }
    const dates: [keyof ExpenseFilters, string][] = [
      ['costDateFrom', 'Data kosztu od'],
      ['costDateTo', 'Data kosztu do'],
      ['paidOnFrom', 'Data zapłaty od'],
      ['paidOnTo', 'Data zapłaty do'],
    ];
    for (const [id, label] of dates) {
      const value = filters[id] as string;
      if (value) {
        active.push({ id, label: `${label}: ${formatDate(value)}` });
      }
    }
    for (const category of filters.categories) {
      active.push({ id: `category:${category}`, label: `Kategoria: ${CATEGORY_LABELS[category]}` });
    }
    if (filters.state) {
      active.push({ id: 'state', label: `Stan: ${STATE_FILTER_LABELS[filters.state]}` });
    }
    if (filters.sign) {
      active.push({ id: 'sign', label: `Kwota: ${SIGN_FILTER_LABELS[filters.sign]}` });
    }
    return active;
  });

  /** Brak wydatków w ogóle: odpowiedź bez filtrów z `total = 0`. */
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
    if (this.costDateRangeInvalid() || this.paidOnRangeInvalid()) {
      // API odrzuca „od” późniejsze niż „do”; formularz filtrów pokazuje błąd przy polu.
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
    if (!SORT_KEYS.includes(sort.key as ExpenseSort)) {
      return Promise.resolve();
    }
    this.sortState.set(sort);
    this.pageState.set(1);
    return this.load();
  }

  /** Każda zmiana filtra wraca do pierwszej strony. */
  updateFilters(change: Partial<ExpenseFilters>): Promise<void> {
    this.filtersState.update((filters) => ({ ...filters, ...change }));
    this.pageState.set(1);
    return this.load();
  }

  /** Przełącza kategorię; kolejność w zapytaniu jest stała (jak w słowniku). */
  toggleCategory(category: ExpenseCategory, checked: boolean): Promise<void> {
    const current = this.filtersState().categories.filter((candidate) => candidate !== category);
    const next = checked
      ? CATEGORIES.filter((candidate) => candidate === category || current.includes(candidate))
      : current;
    return this.updateFilters({ categories: next });
  }

  removeFilter(id: string): Promise<void> {
    if (id.startsWith('category:')) {
      return this.toggleCategory(id.slice('category:'.length) as ExpenseCategory, false);
    }
    if (id in EMPTY_FILTERS) {
      return this.updateFilters({ [id]: EMPTY_FILTERS[id as keyof ExpenseFilters] });
    }
    return Promise.resolve();
  }

  clearFilters(): Promise<void> {
    return this.updateFilters(EMPTY_FILTERS);
  }

  async loadExpense(id: string): Promise<LoadExpenseOutcome> {
    try {
      return { kind: 'loaded', expense: await this.api.get(id) };
    } catch (error) {
      const apiError = toApiError(error);
      return isNotFound(apiError) ? { kind: 'notFound' } : { kind: 'error', error: apiError };
    }
  }

  /** `201` i `200` (powtórzenie z tym samym kluczem) dają zapis; inne dane z kluczem — `keyReused`. */
  async create(input: ExpenseInput, idempotencyKey: string): Promise<CreateExpenseOutcome> {
    try {
      const { expense } = await this.api.create(input, idempotencyKey);
      this.notice = 'Wydatek zapisany.';
      return { kind: 'saved', id: expense.id };
    } catch (error) {
      return keyReusedOf(error) ?? { kind: 'error', error: toApiError(error) };
    }
  }

  update(id: string, input: ExpenseInput, version: string): Promise<ChangeExpenseOutcome> {
    return this.change(() => this.api.update(id, input, version), 'Zmiany zapisane.');
  }

  markPaid(
    id: string,
    paidOn: string,
    paymentMethod: ExpensePaymentMethod | null,
    version: string,
  ): Promise<ChangeExpenseOutcome> {
    return this.change(
      () => this.api.markPaid(id, paidOn, paymentMethod, version),
      'Wydatek oznaczony jako opłacony.',
    );
  }

  unmarkPaid(id: string, version: string): Promise<ChangeExpenseOutcome> {
    return this.change(() => this.api.unmarkPaid(id, version), 'Cofnięto oznaczenie zapłaty.');
  }

  delete(id: string, version: string): Promise<ChangeExpenseOutcome> {
    return this.change(() => this.api.delete(id, version), 'Wydatek usunięty.');
  }

  /** Jednorazowy komunikat sukcesu dla kolejnego ekranu. */
  takeNotice(): string | null {
    const notice = this.notice;
    this.notice = null;
    return notice;
  }

  private async change(
    call: () => Promise<unknown>,
    notice: string,
  ): Promise<ChangeExpenseOutcome> {
    try {
      await call();
      this.notice = notice;
      return { kind: 'saved' };
    } catch (error) {
      const apiError = toApiError(error);
      if (apiError.kind === 'problem') {
        if (apiError.code === EXPENSE_PROBLEM.conflict) {
          return { kind: 'conflict' };
        }
        if (
          apiError.code === EXPENSE_PROBLEM.alreadyPaid ||
          apiError.code === EXPENSE_PROBLEM.notPaid
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

  private query(): ExpenseListQuery {
    const filters = this.filtersState();
    const sort = this.sortState();
    const search = filters.search.trim();
    return {
      page: this.pageState(),
      pageSize: EXPENSES_PAGE_SIZE,
      sort: sort.key as ExpenseSort,
      direction: sort.direction,
      costDateFrom: filters.costDateFrom || null,
      costDateTo: filters.costDateTo || null,
      paidOnFrom: filters.paidOnFrom || null,
      paidOnTo: filters.paidOnTo || null,
      categories: filters.categories,
      state: filters.state || null,
      sign: filters.sign || null,
      search: search ? search.slice(0, SEARCH_MAX_LENGTH) : null,
    };
  }
}

function isNotFound(error: ApiError): boolean {
  return error.kind === 'problem' && error.status === 404;
}

/** `409 idempotency_key_reused` z rozszerzeniem Problem Details `expenseId`. */
function keyReusedOf(error: unknown): CreateExpenseOutcome | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 409) {
    return null;
  }
  const body: unknown = error.error;
  if (typeof body !== 'object' || body === null) {
    return null;
  }
  const record = body as Record<string, unknown>;
  if (record['code'] !== EXPENSE_PROBLEM.keyReused) {
    return null;
  }
  const expenseId = record['expenseId'];
  return { kind: 'keyReused', expenseId: typeof expenseId === 'string' ? expenseId : null };
}
