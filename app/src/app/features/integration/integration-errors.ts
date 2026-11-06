import { Injectable, computed, inject, signal } from '@angular/core';
import { ActiveFilter } from '../../shared/ui/data-table';
import { IntegrationApi } from './integration-api';
import { ActionMessage, retryMessage } from './integration-messages';
import { OrderError, OrderErrorFilter } from './integration-models';
import { IntegrationStatus } from './integration-status';

export const ERRORS_PAGE_SIZE = 25;
export const ERRORS_LIST_ERROR = 'Nie udało się wczytać błędów synchronizacji.';

export const FILTER_LABELS: Readonly<Record<OrderErrorFilter, string>> = {
  open: 'Otwarte',
  closed: 'Zamknięte',
  all: 'Wszystkie',
};

/**
 * Lista błędów zamówień i „Ponów” dla ekranu „Integracja” (provider strony). Filtrowanie i
 * paginacja odbywają się w API; stan listy ustala tylko odpowiedź najnowszego przeładowania.
 */
@Injectable()
export class IntegrationErrors {
  private readonly api = inject(IntegrationApi);
  private readonly status = inject(IntegrationStatus);

  private readonly filterState = signal<OrderErrorFilter>('open');
  private readonly pageState = signal(1);
  private readonly itemsState = signal<readonly OrderError[]>([]);
  private readonly totalState = signal(0);
  private readonly loadingState = signal(false);
  private readonly errorState = signal<string | null>(null);
  private readonly busyState = signal<ReadonlySet<string>>(new Set());
  private readonly resultState = signal<ActionMessage | null>(null);
  private seq = 0;

  readonly pageSize = ERRORS_PAGE_SIZE;
  readonly filter = this.filterState.asReadonly();
  readonly page = this.pageState.asReadonly();
  readonly items = this.itemsState.asReadonly();
  readonly total = this.totalState.asReadonly();
  readonly loading = this.loadingState.asReadonly();
  readonly error = this.errorState.asReadonly();
  /** Wynik ostatnio zakończonego „Ponów”. */
  readonly result = this.resultState.asReadonly();

  readonly activeFilters = computed<readonly ActiveFilter[]>(() => {
    const filter = this.filterState();
    return filter === 'open' ? [] : [{ id: 'status', label: `Status: ${FILTER_LABELS[filter]}` }];
  });

  async load(): Promise<void> {
    const seq = ++this.seq;
    this.loadingState.set(true);
    this.errorState.set(null);
    try {
      const page = await this.api.errors({
        status: this.filterState(),
        page: this.pageState(),
        pageSize: ERRORS_PAGE_SIZE,
      });
      if (seq !== this.seq) {
        return;
      }
      if (page.items.length === 0 && this.pageState() > 1) {
        // Np. zamknięto ostatni błąd na stronie: cofnij o stronę.
        this.pageState.set(this.pageState() - 1);
        return this.load();
      }
      this.itemsState.set(page.items);
      this.totalState.set(page.total);
      this.loadingState.set(false);
    } catch {
      if (seq !== this.seq) {
        return;
      }
      this.errorState.set(ERRORS_LIST_ERROR);
      this.loadingState.set(false);
    }
  }

  setFilter(filter: OrderErrorFilter): void {
    this.filterState.set(filter);
    this.pageState.set(1);
    void this.load();
  }

  clearFilter(): void {
    this.setFilter('open');
  }

  goTo(page: number): void {
    this.pageState.set(page);
    void this.load();
  }

  isBusy(id: string): boolean {
    return this.busyState().has(id);
  }

  /**
   * „Ponów” jednego błędu; kolejne kliknięcie tego samego wiersza w toku nic nie wysyła. Kończy
   * się po przeładowaniu listy, żeby ekran mógł przenieść fokus na wynik.
   */
  async retry(item: OrderError): Promise<void> {
    if (this.busyState().has(item.id)) {
      return;
    }
    this.busyState.update((busy) => new Set([...busy, item.id]));
    // Czyści region wyniku, żeby ten sam tekst wstawiony ponownie był znów ogłoszony.
    this.resultState.set(null);
    const outcome = await this.api.retry(item.id);
    this.busyState.update((busy) => {
      const next = new Set(busy);
      next.delete(item.id);
      return next;
    });
    const order =
      outcome.kind === 'done'
        ? (outcome.error.orderNumber ?? outcome.error.externalId)
        : (item.orderNumber ?? item.externalId);
    const message = retryMessage(outcome, order);
    this.resultState.set(message);
    if (message.refresh) {
      void this.status.refresh({ force: true });
    }
    if (message.reload) {
      await this.load();
    }
  }
}
