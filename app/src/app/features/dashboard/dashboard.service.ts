import { Injectable, computed, inject, signal } from '@angular/core';
import { toApiError } from '../../core/http/api-error';
import { formatDate, todayInWarsaw } from '../../shared/format/pl-format';
import { ActiveFilter, SortState } from '../../shared/ui/data-table';
import {
  CHANNEL_LABELS,
  COMPONENT_KIND_LABELS,
  CostCategory,
  PeriodSelection,
  REPORT_PROBLEM,
  SalesChannel,
  SalesComponentKind,
  SalesComponentsPage,
  SalesReport,
} from './report-models';
import { ReportsApi } from './reports-api';

export type DashboardView = 'sales' | 'cashFlow';

export const COMPONENTS_PAGE_SIZE = 25;

export const REPORT_ERROR_MESSAGE = 'Nie udało się wczytać raportu.';
export const COMPONENTS_ERROR_MESSAGE = 'Nie udało się wczytać zestawienia.';
export const RANGE_TOO_LARGE_MESSAGE = 'Za dużo danych w wybranym okresie. Wybierz krótszy okres.';
export const DATE_REQUIRED_MESSAGE = 'Podaj datę.';
export const DATE_RANGE_MESSAGE = 'Data „od” nie może być późniejsza niż data „do”.';

/** Ten sam tekst co walidacja API (`to` > dziś + 1 rok). */
export function latestEndMessage(latestEnd: string): string {
  return `Data końcowa nie może być późniejsza niż ${formatDate(latestEnd)} (rok od dziś).`;
}

/**
 * Najpóźniejszy koniec własnego zakresu: dziś + 1 rok, operacją na tekście daty `yyyy-MM-dd`
 * (29.02 → 28.02, jak `DateOnly.AddYears` w API).
 */
export function latestEnd(today: string): string {
  const year = String(Number(today.slice(0, 4)) + 1).padStart(4, '0');
  const monthDay = today.slice(5) === '02-29' ? '02-28' : today.slice(5);
  return `${year}-${monthDay}`;
}

export interface SelectionErrors {
  readonly from: string | null;
  readonly to: string | null;
}

/** Odnośnik do listy wydatków z filtrem okresu kosztu (i kategorii) z Pulpitu. */
export interface ExpensesLink {
  readonly link: readonly string[];
  readonly queryParams: Readonly<Record<string, string>>;
}

const DEFAULT_SORT: SortState = { key: 'date', direction: 'desc' };

function defaultSelection(): PeriodSelection {
  return { kind: 'currentMonth', year: todayInWarsaw().slice(0, 4), from: '', to: '' };
}

/**
 * Stan Pulpitu na sygnałach (SF-19): widok, wybór okresu, raport i zestawienie składników z
 * filtrami, kierunkiem i stroną. Stan żyje w serwisie, więc powrót z zamówienia albo wydatku go
 * zachowuje, a każde wejście na `/pulpit` (`enter`) wczytuje dane ponownie. Wszystkie kwoty,
 * sumy, procenty i liczby pochodzą z API — serwis niczego nie liczy. Bez odświeżania w tle.
 */
@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly api = inject(ReportsApi);

  private readonly viewState = signal<DashboardView>('sales');
  private readonly selectionState = signal<PeriodSelection>(defaultSelection());
  private readonly reportState = signal<SalesReport | null>(null);
  private readonly launchDate = signal<string | null>(null);
  private readonly reportLoadingState = signal(false);
  private readonly reportErrorState = signal<string | null>(null);
  private readonly reportRangeError = signal<string | null>(null);
  private readonly channelState = signal<SalesChannel | null>(null);
  private readonly kindState = signal<SalesComponentKind | null>(null);
  private readonly sortState = signal<SortState>(DEFAULT_SORT);
  private readonly pageState = signal(1);
  private readonly componentsState = signal<SalesComponentsPage | null>(null);
  private readonly componentsLoadingState = signal(false);
  private readonly componentsErrorState = signal<string | null>(null);
  private readonly componentsRangeError = signal<string | null>(null);
  private reportSeq = 0;
  private componentsSeq = 0;

  readonly view = this.viewState.asReadonly();
  readonly selection = this.selectionState.asReadonly();
  readonly report = this.reportState.asReadonly();
  readonly reportLoading = this.reportLoadingState.asReadonly();
  readonly reportError = this.reportErrorState.asReadonly();
  readonly channel = this.channelState.asReadonly();
  readonly kind = this.kindState.asReadonly();
  readonly sort = this.sortState.asReadonly();
  readonly page = this.pageState.asReadonly();
  readonly pageSize = COMPONENTS_PAGE_SIZE;
  readonly componentsPage = this.componentsState.asReadonly();
  readonly componentsLoading = this.componentsLoadingState.asReadonly();
  readonly componentsError = this.componentsErrorState.asReadonly();

  /** `422 report_range_too_large` (raport albo zestawienie): komunikat przy wyborze okresu. */
  readonly rangeError = computed(() => this.reportRangeError() ?? this.componentsRangeError());

  /** Dziś + 1 rok (`max` pól dat i walidacja „do”). */
  readonly maxDate = computed(() => latestEnd(todayInWarsaw()));

  /** Błędy własnego zakresu w UI (bez żądania); daty `yyyy-MM-dd` porównują się jako tekst. */
  readonly selectionErrors = computed<SelectionErrors>(() => {
    const { kind, from, to } = this.selectionState();
    if (kind !== 'custom') {
      return { from: null, to: null };
    }
    const max = this.maxDate();
    return {
      from:
        from === '' ? DATE_REQUIRED_MESSAGE : to !== '' && from > to ? DATE_RANGE_MESSAGE : null,
      to: to === '' ? DATE_REQUIRED_MESSAGE : to > max ? latestEndMessage(max) : null,
    };
  });

  readonly selectionInvalid = computed(() => {
    const errors = this.selectionErrors();
    return errors.from !== null || errors.to !== null;
  });

  /** Lata od roku daty uruchomienia (gdy znana z raportu) do bieżącego, malejąco. */
  readonly yearOptions = computed(() => {
    const current = Number(todayInWarsaw().slice(0, 4));
    const launch = this.launchDate();
    const first = launch ? Math.min(Number(launch.slice(0, 4)), current) : current;
    const years: string[] = [];
    for (let year = current; year >= first; year--) {
      years.push(String(year));
    }
    return years;
  });

  /** Karta „Koszty”: lista wydatków z datą kosztu w liczonym okresie raportu. */
  readonly costsLink = computed<ExpensesLink | null>(() => {
    const period = this.reportState()?.period;
    if (!period || period.beforeLaunch) {
      return null;
    }
    return {
      link: ['/wydatki'],
      queryParams: { kosztOd: period.effectiveFrom, kosztDo: period.to },
    };
  });

  readonly componentFiltersActive = computed(
    () => this.channelState() !== null || this.kindState() !== null,
  );

  readonly activeComponentFilters = computed<readonly ActiveFilter[]>(() => {
    const active: ActiveFilter[] = [];
    const channel = this.channelState();
    const kind = this.kindState();
    if (channel) {
      active.push({ id: 'channel', label: `Kanał: ${CHANNEL_LABELS[channel]}` });
    }
    if (kind) {
      active.push({ id: 'kind', label: `Rodzaj: ${COMPONENT_KIND_LABELS[kind]}` });
    }
    return active;
  });

  /** Wejście na `/pulpit`: ponowny odczyt raportu i bieżącej strony zestawienia z zachowanym stanem. */
  enter(): void {
    void this.loadReport();
    void this.loadComponents();
  }

  setView(view: DashboardView): void {
    this.viewState.set(view);
  }

  /** Zmiana okresu: raport i zestawienie od strony 1, bez filtrów, data malejąco. */
  setSelection(change: Partial<PeriodSelection>): void {
    this.selectionState.update((selection) => ({ ...selection, ...change }));
    this.resetComponents();
    void this.loadReport();
    void this.loadComponents();
  }

  /** Link wiersza kategorii: lista wydatków z datą kosztu okresu i kategorią. */
  categoryLink(category: CostCategory): ExpensesLink | null {
    const costs = this.costsLink();
    return costs ? { ...costs, queryParams: { ...costs.queryParams, kategoria: category } } : null;
  }

  async loadReport(): Promise<void> {
    const seq = ++this.reportSeq;
    this.reportErrorState.set(null);
    this.reportRangeError.set(null);
    if (this.selectionInvalid()) {
      // Błąd przy polu daty; API nie dostaje niepoprawnego zakresu.
      this.reportState.set(null);
      this.reportLoadingState.set(false);
      return;
    }
    this.reportLoadingState.set(true);
    try {
      const report = await this.api.salesReport(this.selectionState());
      if (seq === this.reportSeq) {
        this.reportState.set(report);
        this.launchDate.set(report.period.launchDate);
      }
    } catch (error) {
      if (seq !== this.reportSeq) {
        return;
      }
      this.reportState.set(null);
      const rangeError = rangeErrorOf(error);
      if (rangeError) {
        this.reportRangeError.set(rangeError);
      } else {
        this.reportErrorState.set(REPORT_ERROR_MESSAGE);
      }
    } finally {
      if (seq === this.reportSeq) {
        this.reportLoadingState.set(false);
      }
    }
  }

  async loadComponents(): Promise<void> {
    const seq = ++this.componentsSeq;
    this.componentsErrorState.set(null);
    this.componentsRangeError.set(null);
    if (this.selectionInvalid()) {
      this.componentsState.set(null);
      this.componentsLoadingState.set(false);
      return;
    }
    this.componentsLoadingState.set(true);
    try {
      const page = await this.api.salesComponents(this.selectionState(), {
        channel: this.channelState(),
        kind: this.kindState(),
        direction: this.sortState().direction,
        page: this.pageState(),
        pageSize: COMPONENTS_PAGE_SIZE,
      });
      if (seq === this.componentsSeq) {
        this.componentsState.set(page);
      }
    } catch (error) {
      if (seq !== this.componentsSeq) {
        return;
      }
      this.componentsState.set(null);
      const rangeError = rangeErrorOf(error);
      if (rangeError) {
        this.componentsRangeError.set(rangeError);
      } else {
        this.componentsErrorState.set(COMPONENTS_ERROR_MESSAGE);
      }
    } finally {
      if (seq === this.componentsSeq) {
        this.componentsLoadingState.set(false);
      }
    }
  }

  /** Karta „Sprzedaż”: całe zestawienie bez filtrów. */
  showAllComponents(): Promise<void> {
    return this.filterComponents(null, null);
  }

  /** Wiersz kanału: filtr kanału zestawienia (suma = wartość kanału). */
  showChannel(channel: SalesChannel): Promise<void> {
    return this.filterComponents(channel, null);
  }

  /** Karta „Liczba zamówień”: rodzaj „Zamówienie” (liczba pozycji = karta). */
  showOrders(): Promise<void> {
    return this.filterComponents(null, 'order');
  }

  setChannel(channel: SalesChannel | null): Promise<void> {
    return this.filterComponents(channel, this.kindState());
  }

  setKind(kind: SalesComponentKind | null): Promise<void> {
    return this.filterComponents(this.channelState(), kind);
  }

  removeComponentFilter(id: string): Promise<void> {
    if (id === 'channel') {
      return this.setChannel(null);
    }
    if (id === 'kind') {
      return this.setKind(null);
    }
    return Promise.resolve();
  }

  setComponentsPage(page: number): Promise<void> {
    this.pageState.set(Math.max(1, Math.floor(page)));
    return this.loadComponents();
  }

  /** Sortowanie tylko po dacie. */
  setComponentsSort(sort: SortState): Promise<void> {
    if (sort.key !== DEFAULT_SORT.key) {
      return Promise.resolve();
    }
    this.sortState.set(sort);
    this.pageState.set(1);
    return this.loadComponents();
  }

  private filterComponents(
    channel: SalesChannel | null,
    kind: SalesComponentKind | null,
  ): Promise<void> {
    this.channelState.set(channel);
    this.kindState.set(kind);
    this.pageState.set(1);
    return this.loadComponents();
  }

  private resetComponents(): void {
    this.channelState.set(null);
    this.kindState.set(null);
    this.sortState.set(DEFAULT_SORT);
    this.pageState.set(1);
  }
}

function rangeErrorOf(error: unknown): string | null {
  const apiError = toApiError(error);
  return apiError.kind === 'problem' && apiError.code === REPORT_PROBLEM.rangeTooLarge
    ? (apiError.detail ?? RANGE_TOO_LARGE_MESSAGE)
    : null;
}
