import { Injectable, computed, inject, signal } from '@angular/core';
import { toApiError } from '../../core/http/api-error';
import { formatDate, todayInWarsaw } from '../../shared/format/pl-format';
import { ActiveFilter, SortState } from '../../shared/ui/data-table';
import {
  KIND_FILTER_LABELS,
  OPERATIONS_PROBLEM,
  OPERATION_KINDS,
  OperationItem,
  OperationKind,
  OperationListQuery,
  OperationTotals,
} from './operation-models';
import { OperationsApi } from './operations-api';

export const OPERATIONS_PAGE_SIZE = 25;

export const LIST_ERROR_MESSAGE = 'Nie udało się wczytać operacji.';
export const DATE_REQUIRED_MESSAGE = 'Podaj datę.';
export const DATE_RANGE_MESSAGE = 'Data „od” nie może być późniejsza niż data „do”.';
export const RANGE_TOO_LARGE_MESSAGE =
  'Za dużo operacji w wybranym okresie. Wybierz krótszy okres.';

export interface OperationFilters {
  readonly from: string;
  readonly to: string;
  readonly kinds: readonly OperationKind[];
}

const DEFAULT_SORT: SortState = { key: 'date', direction: 'desc' };

/**
 * Bieżący miesiąc w Europe/Warsaw jako okres `yyyy-MM-dd` (operacja na dacie kalendarzowej,
 * nie na kwotach).
 */
export function currentMonth(today = todayInWarsaw()): Pick<OperationFilters, 'from' | 'to'> {
  const [year, month] = today.split('-').map(Number);
  const lastDay = new Date(Date.UTC(year, month, 0)).getUTCDate();
  const prefix = today.slice(0, 7);
  return { from: `${prefix}-01`, to: `${prefix}-${String(lastDay).padStart(2, '0')}` };
}

function defaultFilters(): OperationFilters {
  return { ...currentMonth(), kinds: [] };
}

interface LaunchInfo {
  /** `from` zapytania, dla którego API podało efektywny początek. */
  readonly from: string;
  readonly effectiveFrom: string;
  readonly launchDate: string;
}

/**
 * Stan listy „Operacje” na sygnałach (okres, rodzaje, kierunek sortowania, strona, wynik).
 * Lista, sumy i efektywny początek pochodzą z API; serwis niczego nie sumuje i nie zmienia znaku
 * kwot. Stan żyje w serwisie, więc powrót ze źródła operacji zachowuje widok.
 */
@Injectable({ providedIn: 'root' })
export class OperationsService {
  private readonly api = inject(OperationsApi);

  private readonly pageState = signal(1);
  private readonly sortState = signal<SortState>(DEFAULT_SORT);
  private readonly filtersState = signal<OperationFilters>(defaultFilters());
  private readonly itemsState = signal<readonly OperationItem[]>([]);
  private readonly totalState = signal(0);
  private readonly totalsState = signal<OperationTotals | null>(null);
  private readonly launchState = signal<LaunchInfo | null>(null);
  private readonly loadingState = signal(false);
  private readonly errorState = signal<string | null>(null);
  private readonly rangeErrorState = signal<string | null>(null);
  private requestSeq = 0;

  readonly page = this.pageState.asReadonly();
  readonly pageSize = OPERATIONS_PAGE_SIZE;
  readonly sort = this.sortState.asReadonly();
  readonly filters = this.filtersState.asReadonly();
  readonly items = this.itemsState.asReadonly();
  readonly total = this.totalState.asReadonly();
  /** Sumy z API dla całego filtra; `null` przy błędzie, przekroczeniu limitu i w trakcie odczytu. */
  readonly totals = this.totalsState.asReadonly();
  readonly loading = this.loadingState.asReadonly();
  readonly error = this.errorState.asReadonly();
  /** `422 operations_range_too_large`: komunikat przy filtrze okresu. */
  readonly rangeError = this.rangeErrorState.asReadonly();

  // Daty `yyyy-MM-dd` porównują się poprawnie jako tekst.
  readonly fromError = computed(() => {
    const { from, to } = this.filtersState();
    if (from === '') {
      return DATE_REQUIRED_MESSAGE;
    }
    return to !== '' && from > to ? DATE_RANGE_MESSAGE : null;
  });

  readonly toError = computed(() => (this.filtersState().to === '' ? DATE_REQUIRED_MESSAGE : null));

  readonly periodInvalid = computed(() => this.fromError() !== null || this.toError() !== null);

  readonly kindFilterActive = computed(() => this.filtersState().kinds.length > 0);

  /** Informacja pod filtrem, gdy okres zaczyna się przed datą uruchomienia (daty z API). */
  readonly launchNotice = computed(() => {
    const launch = this.launchState();
    if (!launch || launch.effectiveFrom <= launch.from) {
      return null;
    }
    return `Operacje liczone od ${formatDate(launch.launchDate)} (data uruchomienia).`;
  });

  readonly activeFilters = computed<readonly ActiveFilter[]>(() => {
    const { from, to, kinds } = this.filtersState();
    const active: ActiveFilter[] = [];
    if (from !== '' && to !== '') {
      active.push({ id: 'period', label: `Okres: ${formatDate(from)} – ${formatDate(to)}` });
    }
    for (const kind of kinds) {
      active.push({ id: `kind:${kind}`, label: `Rodzaj: ${KIND_FILTER_LABELS[kind]}` });
    }
    return active;
  });

  /**
   * Wczytuje stronę. Sumy dotyczą całego filtra, więc zmiana strony albo sortowania
   * (`keepTotals`) nie chowa ich na czas wczytywania; zmiana filtra chowa nieaktualne sumy.
   */
  async load(keepTotals = false): Promise<void> {
    const seq = ++this.requestSeq;
    this.errorState.set(null);
    this.rangeErrorState.set(null);
    if (!keepTotals) {
      this.totalsState.set(null);
    }
    if (this.periodInvalid()) {
      // API odrzuca brak daty i „od” późniejsze niż „do”; filtr pokazuje błąd przy polu.
      this.itemsState.set([]);
      this.totalState.set(0);
      this.launchState.set(null);
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
      this.itemsState.set(result.items);
      this.totalState.set(result.total);
      this.totalsState.set(result.totals);
      this.launchState.set({
        from: query.from,
        effectiveFrom: result.effectiveFrom,
        launchDate: result.launchDate,
      });
    } catch (error) {
      if (seq !== this.requestSeq) {
        return;
      }
      this.itemsState.set([]);
      this.totalState.set(0);
      this.launchState.set(null);
      const apiError = toApiError(error);
      if (apiError.kind === 'problem' && apiError.code === OPERATIONS_PROBLEM.rangeTooLarge) {
        this.rangeErrorState.set(apiError.detail ?? RANGE_TOO_LARGE_MESSAGE);
      } else {
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
    return this.load(true);
  }

  /** Sortowanie tylko po dacie operacji. */
  setSort(sort: SortState): Promise<void> {
    if (sort.key !== DEFAULT_SORT.key) {
      return Promise.resolve();
    }
    this.sortState.set(sort);
    this.pageState.set(1);
    return this.load(true);
  }

  /** Każda zmiana filtra wraca do pierwszej strony. */
  updatePeriod(change: Partial<Pick<OperationFilters, 'from' | 'to'>>): Promise<void> {
    return this.update(change);
  }

  /** Przełącza rodzaj; kolejność w zapytaniu jest stała (`OPERATION_KINDS`). */
  toggleKind(kind: OperationKind, checked: boolean): Promise<void> {
    const current = this.filtersState().kinds.filter((candidate) => candidate !== kind);
    const kinds = checked
      ? OPERATION_KINDS.filter((candidate) => candidate === kind || current.includes(candidate))
      : current;
    return this.update({ kinds });
  }

  /** Usunięcie okresu wraca do bieżącego miesiąca. */
  removeFilter(id: string): Promise<void> {
    if (id === 'period') {
      return this.update(currentMonth());
    }
    if (id.startsWith('kind:')) {
      return this.toggleKind(id.slice('kind:'.length) as OperationKind, false);
    }
    return Promise.resolve();
  }

  clearFilters(): Promise<void> {
    return this.update(defaultFilters());
  }

  private update(change: Partial<OperationFilters>): Promise<void> {
    this.filtersState.update((filters) => ({ ...filters, ...change }));
    this.pageState.set(1);
    return this.load();
  }

  private query(): OperationListQuery {
    const { from, to, kinds } = this.filtersState();
    return {
      from,
      to,
      kinds,
      direction: this.sortState().direction,
      page: this.pageState(),
      pageSize: OPERATIONS_PAGE_SIZE,
    };
  }
}
