import {
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { formatDateTime } from '../../shared/format/pl-format';
import { PlDateTimePipe } from '../../shared/format/pl-format-pipes';
import { Button } from '../../shared/ui/button';
import { DataTable, DataTableColumn } from '../../shared/ui/data-table';
import { ErrorState } from '../../shared/ui/error-state';
import { LoadingState } from '../../shared/ui/loading-state';
import { StatusBadge } from '../../shared/ui/status-badge';
import { TableCell } from '../../shared/ui/table-cell';
import { IntegrationApi } from './integration-api';
import { ERRORS_PAGE_SIZE, FILTER_LABELS, IntegrationErrors } from './integration-errors';
import { ActionMessage, syncMessage } from './integration-messages';
import { OrderError, OrderErrorFilter } from './integration-models';
import {
  CLOSED_BY_LABELS,
  ERROR_SOURCE_LABELS,
  NOT_CONFIGURED_TEXT,
  RUN_SOURCE_LABELS,
  RUN_STATUS_LABELS,
  explanationsOf,
  labelOf,
} from './integration-state';
import { IntegrationStateBadge } from './integration-state-badge';
import { IntegrationStatus } from './integration-status';

export const AGED_HINT = 'Stan może być nieaktualny — kliknij lub naciśnij klawisz, aby odświeżyć.';
export const SYNC_RUNNING_REASON = 'Synchronizacja trwa. Poczekaj na jej zakończenie.';
export const SYNC_LOADING_REASON = 'Trwa sprawdzanie stanu integracji.';

/** Ekran „Integracja” (spec SF-14, Behavior 5): stan, ostatni przebieg, akcje i błędy zamówień. */
@Component({
  selector: 'app-integration-page',
  imports: [
    Button,
    DataTable,
    ErrorState,
    IntegrationStateBadge,
    LoadingState,
    PlDateTimePipe,
    RouterLink,
    StatusBadge,
    TableCell,
  ],
  providers: [IntegrationErrors],
  templateUrl: './integration-page.html',
})
export class IntegrationPage {
  private readonly api = inject(IntegrationApi);
  protected readonly status = inject(IntegrationStatus);
  protected readonly errors = inject(IntegrationErrors);

  protected readonly filterLabels = FILTER_LABELS;
  protected readonly filterOptions: readonly OrderErrorFilter[] = ['open', 'closed', 'all'];
  protected readonly pageSize = ERRORS_PAGE_SIZE;
  protected readonly agedHint = AGED_HINT;
  protected readonly runStatusLabels = RUN_STATUS_LABELS;
  protected readonly runSourceLabels = RUN_SOURCE_LABELS;
  protected readonly closedByLabels = CLOSED_BY_LABELS;
  protected readonly labelOf = labelOf;
  protected readonly syncReasonId = 'integracja-powod-synchronizacji';

  private readonly retryResult = viewChild.required<ElementRef<HTMLElement>>('retryResult');

  protected readonly syncBusy = signal(false);
  protected readonly syncResult = signal<ActionMessage | null>(null);

  protected readonly known = computed(() => {
    const view = this.status.view();
    return view.phase === 'known' ? view : null;
  });

  protected readonly explanations = computed(() => {
    const known = this.known();
    return known ? explanationsOf(known.status, known.state) : [];
  });

  /** Powód wyłączenia „Synchronizuj teraz”; w `unknown` akcja jest dostępna (decyduje API). */
  protected readonly syncDisabledReason = computed<string | null>(() => {
    const view = this.status.view();
    if (view.phase === 'loading') {
      return SYNC_LOADING_REASON;
    }
    if (view.phase === 'known') {
      if (view.state.kind === 'notConfigured') {
        return NOT_CONFIGURED_TEXT;
      }
      if (view.state.kind === 'running') {
        return SYNC_RUNNING_REASON;
      }
    }
    return null;
  });

  /** „Ponów”: ukryte bez konfiguracji, wyłączone przed pierwszym odczytem. */
  protected readonly retryHidden = computed(() => this.known()?.state.kind === 'notConfigured');
  protected readonly retryDisabled = computed(() => this.status.view().phase === 'loading');

  protected readonly columns: readonly DataTableColumn<OrderError>[] = [
    { key: 'order', header: 'Zamówienie' },
    { key: 'message', header: 'Błąd' },
    { key: 'source', header: 'Źródło', value: (row) => labelOf(ERROR_SOURCE_LABELS, row.source) },
    { key: 'attempts', header: 'Próby', align: 'end', value: (row) => String(row.attempts) },
    {
      key: 'first',
      header: 'Pierwsze wystąpienie',
      value: (row) => formatDateTime(row.firstOccurredAtUtc),
    },
    {
      key: 'last',
      header: 'Ostatnie wystąpienie',
      value: (row) => formatDateTime(row.lastOccurredAtUtc),
    },
    { key: 'status', header: 'Status' },
    { key: 'action', header: 'Akcja' },
  ];

  protected readonly rowKey = (row: OrderError) => row.id;

  constructor() {
    if (this.status.view().phase === 'loading') {
      void this.status.refresh();
    }
    void this.errors.load();

    // Koniec synchronizacji (isRunning: true → false) mógł dodać błędy: przeładuj listę.
    let wasRunning: boolean | null = null;
    effect(() => {
      const running = this.status.status()?.isRunning ?? null;
      if (wasRunning === true && running === false) {
        untracked(() => void this.errors.load());
      }
      if (running !== null) {
        wasRunning = running;
      }
    });
  }

  protected async sync(): Promise<void> {
    if (this.syncBusy() || this.syncDisabledReason() !== null) {
      return;
    }
    this.syncBusy.set(true);
    this.syncResult.set(null);
    const outcome = await this.api.requestSync();
    this.syncBusy.set(false);
    const message = syncMessage(outcome);
    this.syncResult.set(message);
    if (message.refresh) {
      void this.status.refresh({ force: true });
    }
  }

  /**
   * „Ponów” podmienia tabelę na szkielet przy przeładowaniu, więc przycisk z fokusem znika.
   * Fokus trafia na stały region wyniku (`tabindex="-1"`).
   */
  protected async retry(row: OrderError): Promise<void> {
    await this.errors.retry(row);
    this.retryResult().nativeElement.focus();
  }

  protected retryStatus(): void {
    void this.status.refresh({ force: true });
  }

  protected onFilter(event: Event): void {
    this.errors.setFilter((event.target as HTMLSelectElement).value as OrderErrorFilter);
  }
}
