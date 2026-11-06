import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { IntegrationApi } from './integration-api';
import { SyncStatus } from './integration-models';
import {
  IntegrationView,
  KIND_LABELS,
  LOADING_LABEL,
  UNKNOWN_LABEL,
  dataCompleteOf,
  integrationStateOf,
  lastFullCheckText,
  presentationOf,
} from './integration-state';

/** Okres odświeżania poza „Synchronizacja trwa”. */
export const PERIOD_MS = 60_000;
/** Okres odświeżania, gdy ostatni wyświetlany stan to „Synchronizacja trwa”. */
export const RUNNING_PERIOD_MS = 10_000;
/** Zegar lokalny dla progu 25 h (bez żądań). */
export const CLOCK_MS = 60_000;

const ACTIVITY_EVENTS = ['keydown', 'pointerdown'] as const;
const LISTENER_OPTIONS: AddEventListenerOptions = { capture: true, passive: true };

type Phase = 'loading' | 'known' | 'unknown';

/**
 * Stan integracji WooCommerce dla całej aplikacji (spec SF-14, Behavior 2 i 3).
 *
 * Każde żądanie przedłuża sesję (DOST-01), więc odczyt w tle wychodzi wyłącznie w obsłudze
 * celowej aktywności użytkownika (`keydown`, `pointerdown`, powrót na widoczną kartę), gdy od
 * ostatniego odczytu minął okres. Zegar nie wysyła żądań: tylko przesuwa „teraz” (próg 25 h)
 * i oznacza zbyt stary odczyt jako `aged` (wariant ostrożny, bez kompletności danych).
 */
@Injectable({ providedIn: 'root' })
export class IntegrationStatus {
  private readonly api = inject(IntegrationApi);
  private readonly document = inject(DOCUMENT);

  private readonly phaseState = signal<Phase>('loading');
  private readonly statusState = signal<SyncStatus | null>(null);
  private readonly readAtState = signal<number | null>(null);
  /** Wiek odczytu, po którym stan jest `aged`: dwa okresy z chwili odczytu. */
  private readonly agedAfterState = signal(2 * PERIOD_MS);
  private readonly nowState = signal(Date.now());
  private readonly announcementState = signal('');

  /** Ostatnia udana odpowiedź; nieudany odczyt jej nie czyści. */
  readonly status = this.statusState.asReadonly();
  readonly now = this.nowState.asReadonly();
  /** Tekst regionu `aria-live` w nagłówku; zmienia się tylko przy zmianie wyświetlanego stanu. */
  readonly announcement = this.announcementState.asReadonly();

  readonly view = computed<IntegrationView>(() => {
    const status = this.statusState();
    switch (this.phaseState()) {
      case 'loading':
        return { phase: 'loading' };
      case 'unknown':
        return { phase: 'unknown', last: status };
      case 'known': {
        const readAt = this.readAtState();
        if (!status || readAt === null) {
          return { phase: 'loading' };
        }
        return {
          phase: 'known',
          status,
          state: integrationStateOf(status, this.nowState()),
          // Wiek liczony z zegara przy każdym wyliczeniu; timer tylko przyspiesza przejście.
          aged: this.nowState() - readAt > this.agedAfterState(),
          readAtUtc: new Date(readAt).toISOString(),
        };
      }
    }
  });
  readonly presentation = computed(() => presentationOf(this.view(), this.nowState()));
  readonly dataComplete = computed(() => dataCompleteOf(this.view()));
  readonly lastFullCheck = computed(() => lastFullCheckText(this.view()));

  private started = false;
  /** Po `stop()` (wylogowanie, zniszczenie powłoki) spóźnione `refresh()` nic nie wysyła. */
  private stopped = false;
  private seq = 0;
  private inFlight: Promise<void> | null = null;
  private lastActivityAt = Number.NEGATIVE_INFINITY;
  private lastReadAt = Number.NEGATIVE_INFINITY;
  /** Ostatni udany odczyt to „Synchronizacja trwa”; nieudany odczyt jej nie kończy. */
  private runningSeries = false;
  private clockTimer: ReturnType<typeof setInterval> | null = null;
  private agedTimer: ReturnType<typeof setTimeout> | null = null;
  private lastKey: string | null = null;

  /** Wywołuje wskaźnik w nagłówku powłoki; idempotentne. */
  start(): void {
    if (this.started) {
      return;
    }
    this.started = true;
    this.stopped = false;
    for (const type of ACTIVITY_EVENTS) {
      this.document.addEventListener(type, this.onActivity, LISTENER_OPTIONS);
    }
    this.document.addEventListener('visibilitychange', this.onVisibility, LISTENER_OPTIONS);
    this.clockTimer = setInterval(() => this.tick(), CLOCK_MS);
    void this.refresh({ force: true });
  }

  /** Usuwa nasłuch i timery, a stan wraca do „Sprawdzanie stanu…”; idempotentne. */
  stop(): void {
    if (!this.started) {
      return;
    }
    this.started = false;
    this.stopped = true;
    for (const type of ACTIVITY_EVENTS) {
      this.document.removeEventListener(type, this.onActivity, LISTENER_OPTIONS);
    }
    this.document.removeEventListener('visibilitychange', this.onVisibility, LISTENER_OPTIONS);
    if (this.clockTimer !== null) {
      clearInterval(this.clockTimer);
      this.clockTimer = null;
    }
    this.clearAgedTimer();
    this.seq++;
    this.inFlight = null;
    this.lastActivityAt = Number.NEGATIVE_INFINITY;
    this.lastReadAt = Number.NEGATIVE_INFINITY;
    this.runningSeries = false;
    this.lastKey = null;
    this.phaseState.set('loading');
    this.statusState.set(null);
    this.readAtState.set(null);
    this.announcementState.set('');
  }

  /**
   * Odczyt `/status`. Bez `force` dołącza do odczytu w toku; z `force` (po akcjach i „Spróbuj
   * ponownie”) wysyła nowy. Stan zmienia tylko odpowiedź najnowszego odczytu.
   */
  refresh(options: { readonly force?: boolean } = {}): Promise<void> {
    if (this.stopped) {
      return Promise.resolve();
    }
    if (!options.force && this.inFlight) {
      return this.inFlight;
    }
    const seq = ++this.seq;
    this.lastReadAt = Date.now();
    const request = this.api.status().then(
      (status) => this.applySuccess(seq, status),
      (error: unknown) => this.applyFailure(seq, error),
    );
    this.inFlight = request;
    void request.then(() => {
      if (this.inFlight === request) {
        this.inFlight = null;
      }
    });
    return request;
  }

  private readonly onActivity = (): void => {
    // Najpierw znacznik aktywności, potem warunki (porównania ścisłe, spec Assumptions 33–34).
    this.lastActivityAt = Date.now();
    if (!this.started || this.document.visibilityState !== 'visible') {
      return;
    }
    // Wysyłka w chwili aktywności, więc `now − lastActivityAt = 0 ≤ okres` z definicji. Należność
    // (`lastActivityAt − lastReadAt ≥ okres > 0`) obejmuje ścisłe `lastActivityAt > lastReadAt`,
    // a stan `aged` (wiek > 2 okresy) zawsze oznacza odczyt należny.
    if (this.lastActivityAt - this.lastReadAt >= this.period()) {
      void this.refresh();
    }
  };

  private readonly onVisibility = (): void => {
    if (this.document.visibilityState === 'visible') {
      // Po wybudzeniu timery mogą być opóźnione: przelicz wiek odczytu od razu.
      this.tick();
      this.onActivity();
    }
  };

  private applySuccess(seq: number, status: SyncStatus): void {
    if (seq !== this.seq) {
      return;
    }
    const now = Date.now();
    this.statusState.set(status);
    this.readAtState.set(now);
    this.nowState.set(now);
    this.phaseState.set('known');
    this.runningSeries = integrationStateOf(status, now).kind === 'running';
    this.agedAfterState.set(2 * this.period());
    this.scheduleAged();
    this.announce();
  }

  private applyFailure(seq: number, error: unknown): void {
    if (seq !== this.seq) {
      return;
    }
    if (error instanceof HttpErrorResponse && error.status === 401) {
      // Wylogowanie obsługuje globalny interceptor; powłoka zaraz zniknie.
      return;
    }
    this.clearAgedTimer();
    this.phaseState.set('unknown');
    this.announce();
  }

  private period(): number {
    return this.runningSeries ? RUNNING_PERIOD_MS : PERIOD_MS;
  }

  /** Timer na chwilę przekroczenia dwóch okresów od odczytu (warunek ścisły: +1 ms). */
  private scheduleAged(): void {
    this.clearAgedTimer();
    if (!this.started) {
      return;
    }
    this.agedTimer = setTimeout(() => {
      this.agedTimer = null;
      this.tick();
    }, this.agedAfterState() + 1);
  }

  private clearAgedTimer(): void {
    if (this.agedTimer !== null) {
      clearTimeout(this.agedTimer);
      this.agedTimer = null;
    }
  }

  private tick(): void {
    this.nowState.set(Date.now());
    this.announce();
  }

  /** Ogłasza zmianę wyświetlanego stanu; pierwszy odczyt (wyjście z `loading`) bez ogłoszenia. */
  private announce(): void {
    const view = this.view();
    const key =
      view.phase === 'known' ? `${view.state.kind}${view.aged ? ':aged' : ''}` : view.phase;
    if (key === 'loading') {
      return;
    }
    // Wyjście z `aged` bez zmiany stanu (świeży odczyt tego samego stanu) nie jest ogłaszane.
    const leftAged = this.lastKey === `${key}:aged`;
    if (this.lastKey !== null && key !== this.lastKey && !leftAged) {
      this.announcementState.set(announcementOf(view, this.nowState()));
    }
    this.lastKey = key;
  }
}

function announcementOf(view: IntegrationView, nowMs: number): string {
  switch (view.phase) {
    case 'loading':
      return `Stan integracji: ${LOADING_LABEL}`;
    case 'unknown':
      return `Stan integracji: ${UNKNOWN_LABEL}`;
    case 'known': {
      const label = KIND_LABELS[view.state.kind];
      const aged = presentationOf(view, nowMs).agedText;
      return aged
        ? `Stan integracji: ${label}, ${aged.charAt(0).toLowerCase()}${aged.slice(1)}`
        : `Stan integracji: ${label}`;
    }
  }
}
