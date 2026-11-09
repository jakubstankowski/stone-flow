import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiError, fieldErrorOf, toApiError } from '../../core/http/api-error';
import { formatDate, todayInWarsaw } from '../../shared/format/pl-format';
import { ActiveFilter, SortState } from '../../shared/ui/data-table';
import { ChangeHistoryApi } from './change-history-api';
import {
  AuditObjectTypeCode,
  ChangeHistoryItem,
  ChangeHistoryQuery,
  LIST_ERROR_MESSAGE,
  OBJECT_TYPES,
  OBJECT_TYPE_LABELS,
} from './change-history-models';

export const CHANGE_HISTORY_PAGE_SIZE = 25;

export const DATE_REQUIRED_MESSAGE = 'Podaj datę.';
export const DATE_RANGE_MESSAGE = 'Data „od” nie może być późniejsza niż data „do”.';

export interface ChangeHistoryFilters {
  /** Okres zmiany (wymagany), `yyyy-MM-dd` w Europe/Warsaw. */
  readonly changedFrom: string;
  readonly changedTo: string;
  /** Data biznesowa (opcjonalna); pusty tekst = brak granicy. */
  readonly businessFrom: string;
  readonly businessTo: string;
  readonly objectTypes: readonly AuditObjectTypeCode[];
}

/** Pola filtra, przy których pokazujemy błąd `400` z API (klucz = nazwa parametru). */
const FIELD_KEYS = [
  'changedFrom',
  'changedTo',
  'businessFrom',
  'businessTo',
  'objectType',
] as const;

const DEFAULT_SORT: SortState = { key: 'occurredAt', direction: 'desc' };

const PERIOD_DAYS = 30;

/**
 * Domyślny okres zmiany: ostatnie 30 dni, od dziś minus 29 dni do dziś (Europe/Warsaw).
 * Arytmetyka na dacie kalendarzowej, nie na kwotach.
 */
export function defaultChangePeriod(
  today = todayInWarsaw(),
): Pick<ChangeHistoryFilters, 'changedFrom' | 'changedTo'> {
  const [year, month, day] = today.split('-').map(Number);
  const start = new Date(Date.UTC(year, month - 1, day - (PERIOD_DAYS - 1)));
  return { changedFrom: start.toISOString().slice(0, 10), changedTo: today };
}

function defaultFilters(): ChangeHistoryFilters {
  return { ...defaultChangePeriod(), businessFrom: '', businessTo: '', objectTypes: [] };
}

/**
 * Stan listy „Historia zmian” na sygnałach (filtry, kierunek sortowania, strona, wynik, błędy).
 * Lista i liczba wpisów pochodzą z API, bez lokalnego filtrowania i stronicowania. Stan żyje w
 * serwisie, więc powrót z obiektu zachowuje filtry i stronę.
 */
@Injectable({ providedIn: 'root' })
export class ChangeHistoryService {
  private readonly api = inject(ChangeHistoryApi);

  private readonly pageState = signal(1);
  private readonly sortState = signal<SortState>(DEFAULT_SORT);
  private readonly filtersState = signal<ChangeHistoryFilters>(defaultFilters());
  /** Domyślny okres z chwili ostatniego ustawienia; chip okresu porównuje się z nim, nie z „dziś”. */
  private readonly defaultPeriodState = signal(defaultChangePeriod());
  private readonly itemsState = signal<readonly ChangeHistoryItem[]>([]);
  private readonly totalState = signal(0);
  private readonly loadingState = signal(false);
  private readonly errorState = signal<string | null>(null);
  private readonly apiErrorState = signal<ApiError | null>(null);
  private requestSeq = 0;

  readonly page = this.pageState.asReadonly();
  readonly pageSize = CHANGE_HISTORY_PAGE_SIZE;
  readonly sort = this.sortState.asReadonly();
  readonly filters = this.filtersState.asReadonly();
  readonly items = this.itemsState.asReadonly();
  readonly total = this.totalState.asReadonly();
  readonly loading = this.loadingState.asReadonly();
  /** Błąd sieci lub serwera: komunikat z „Spróbuj ponownie”. */
  readonly error = this.errorState.asReadonly();

  // Daty `yyyy-MM-dd` porównują się poprawnie jako tekst.
  private readonly changedFromLocal = computed(() => {
    const { changedFrom, changedTo } = this.filtersState();
    if (changedFrom === '') {
      return DATE_REQUIRED_MESSAGE;
    }
    return changedTo !== '' && changedFrom > changedTo ? DATE_RANGE_MESSAGE : null;
  });

  private readonly changedToLocal = computed(() =>
    this.filtersState().changedTo === '' ? DATE_REQUIRED_MESSAGE : null,
  );

  private readonly businessFromLocal = computed(() => {
    const { businessFrom, businessTo } = this.filtersState();
    return businessFrom !== '' && businessTo !== '' && businessFrom > businessTo
      ? DATE_RANGE_MESSAGE
      : null;
  });

  readonly changedFromError = computed(
    () => this.changedFromLocal() ?? this.apiFieldError('changedFrom'),
  );
  readonly changedToError = computed(
    () => this.changedToLocal() ?? this.apiFieldError('changedTo'),
  );
  readonly businessFromError = computed(
    () => this.businessFromLocal() ?? this.apiFieldError('businessFrom'),
  );
  readonly businessToError = computed(() => this.apiFieldError('businessTo'));
  readonly objectTypeError = computed(() => this.apiFieldError('objectType'));

  /** Błąd `400` z API przy parametrze, którego nie ma w filtrach (np. strona). */
  readonly generalError = computed(() => {
    const error = this.apiErrorState();
    if (error?.kind !== 'validation') {
      return null;
    }
    const known = new Set<string>(FIELD_KEYS.map((key) => key.toLowerCase()));
    const other = Object.entries(error.fieldErrors).find(
      ([key, messages]) => !known.has(key) && messages.length > 0,
    );
    return other?.[1][0] ?? (Object.keys(error.fieldErrors).length === 0 ? error.detail : null);
  });

  /** Filtry odrzucane już w przeglądarce: lista nie pyta API. */
  readonly filtersInvalid = computed(
    () =>
      this.changedFromLocal() !== null ||
      this.changedToLocal() !== null ||
      this.businessFromLocal() !== null,
  );

  /** Chipy. Okres zmiany tylko, gdy różni się od domyślnych 30 dni. */
  readonly activeFilters = computed<readonly ActiveFilter[]>(() => {
    const { changedFrom, changedTo, businessFrom, businessTo, objectTypes } = this.filtersState();
    const active: ActiveFilter[] = [];
    const period = this.defaultPeriodState();
    if (changedFrom !== period.changedFrom || changedTo !== period.changedTo) {
      // Także okres niepełny (brak daty), żeby pusta lista mówiła o filtrach, a nie o braku zmian.
      const from = changedFrom === '' ? '…' : formatDate(changedFrom);
      const to = changedTo === '' ? '…' : formatDate(changedTo);
      active.push({ id: 'period', label: `Zmiana: ${from} – ${to}` });
    }
    if (businessFrom !== '' && businessTo !== '') {
      active.push({
        id: 'business',
        label: `Data biznesowa: ${formatDate(businessFrom)} – ${formatDate(businessTo)}`,
      });
    } else if (businessFrom !== '') {
      active.push({ id: 'business', label: `Data biznesowa: od ${formatDate(businessFrom)}` });
    } else if (businessTo !== '') {
      active.push({ id: 'business', label: `Data biznesowa: do ${formatDate(businessTo)}` });
    }
    for (const type of objectTypes) {
      active.push({ id: `type:${type}`, label: `Rodzaj: ${OBJECT_TYPE_LABELS[type]}` });
    }
    return active;
  });

  /**
   * Przy wejściu na listę: jeśli okres jest nadal domyślny, a od jego ustawienia minęła północ,
   * przesuwa go na bieżące 30 dni. Okres wybrany przez użytkownika zostaje bez zmian.
   */
  refreshDefaultPeriod(): void {
    const current = this.defaultPeriodState();
    const fresh = defaultChangePeriod();
    if (current.changedTo === fresh.changedTo) {
      return;
    }
    const { changedFrom, changedTo } = this.filtersState();
    if (changedFrom === current.changedFrom && changedTo === current.changedTo) {
      this.filtersState.update((filters) => ({ ...filters, ...fresh }));
      this.pageState.set(1);
    }
    this.defaultPeriodState.set(fresh);
  }

  async load(): Promise<void> {
    const seq = ++this.requestSeq;
    this.errorState.set(null);
    if (this.filtersInvalid()) {
      // Błąd jest przy polu filtra; API nie jest pytane.
      this.apiErrorState.set(null);
      this.itemsState.set([]);
      this.totalState.set(0);
      this.loadingState.set(false);
      return;
    }
    const query = this.query();
    this.loadingState.set(true);
    try {
      const result = await this.api.list(query);
      if (seq !== this.requestSeq) {
        return;
      }
      this.apiErrorState.set(null);
      this.itemsState.set(result.items);
      this.totalState.set(result.total);
    } catch (error) {
      if (seq !== this.requestSeq) {
        return;
      }
      this.itemsState.set([]);
      this.totalState.set(0);
      const apiError = toApiError(error);
      if (apiError.kind === 'validation') {
        this.apiErrorState.set(apiError);
      } else {
        this.apiErrorState.set(null);
        this.errorState.set(LIST_ERROR_MESSAGE);
      }
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

  /** Sortowanie tylko po czasie zmiany. */
  setSort(sort: SortState): Promise<void> {
    if (sort.key !== DEFAULT_SORT.key) {
      return Promise.resolve();
    }
    this.sortState.set(sort);
    this.pageState.set(1);
    return this.load();
  }

  /** Każda zmiana filtra wraca do pierwszej strony. */
  updateFilters(change: Partial<Omit<ChangeHistoryFilters, 'objectTypes'>>): Promise<void> {
    return this.update(change);
  }

  /** Przełącza rodzaj; kolejność w zapytaniu i chipach jest stała (`OBJECT_TYPES`). */
  toggleObjectType(type: AuditObjectTypeCode, checked: boolean): Promise<void> {
    const current = this.filtersState().objectTypes.filter((candidate) => candidate !== type);
    const objectTypes = checked
      ? OBJECT_TYPES.filter((candidate) => candidate === type || current.includes(candidate))
      : current;
    return this.update({ objectTypes });
  }

  /** Usunięcie okresu wraca do domyślnych 30 dni, a daty biznesowej — do braku granic. */
  removeFilter(id: string): Promise<void> {
    if (id === 'period') {
      const period = defaultChangePeriod();
      this.defaultPeriodState.set(period);
      return this.update(period);
    }
    if (id === 'business') {
      return this.update({ businessFrom: '', businessTo: '' });
    }
    if (id.startsWith('type:')) {
      return this.toggleObjectType(id.slice('type:'.length) as AuditObjectTypeCode, false);
    }
    return Promise.resolve();
  }

  clearFilters(): Promise<void> {
    const filters = defaultFilters();
    this.defaultPeriodState.set({ changedFrom: filters.changedFrom, changedTo: filters.changedTo });
    return this.update(filters);
  }

  private update(change: Partial<ChangeHistoryFilters>): Promise<void> {
    this.filtersState.update((filters) => ({ ...filters, ...change }));
    this.pageState.set(1);
    this.apiErrorState.set(null);
    return this.load();
  }

  private apiFieldError(field: (typeof FIELD_KEYS)[number]): string | null {
    const error = this.apiErrorState();
    return error ? fieldErrorOf(error, field) : null;
  }

  private query(): ChangeHistoryQuery {
    const { changedFrom, changedTo, businessFrom, businessTo, objectTypes } = this.filtersState();
    return {
      changedFrom,
      changedTo,
      businessFrom,
      businessTo,
      objectTypes,
      direction: this.sortState().direction,
      page: this.pageState(),
      pageSize: CHANGE_HISTORY_PAGE_SIZE,
    };
  }
}
