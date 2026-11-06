import { formatDateTime, formatReadTime } from '../../shared/format/pl-format';
import { StatusTone } from '../../shared/ui/status-badge';
import { SyncStatus } from './integration-models';

/** „Dane niekompletne”, gdy od ostatniego sukcesu minęło ponad 25 godzin (PRD sekcja 5). */
export const STALE_AFTER_MS = 25 * 60 * 60 * 1000;

export type IntegrationKind = 'notConfigured' | 'running' | 'error' | 'incomplete' | 'current';
export type IncompleteReason = 'openErrors' | 'stale' | 'neverSucceeded';

/** Stan INT-01 wyliczony z odpowiedzi `/status`. */
export interface IntegrationState {
  readonly kind: IntegrationKind;
  /** Powody niekompletności w stałej kolejności, wyliczane zawsze. */
  readonly reasons: readonly IncompleteReason[];
  readonly dataComplete: boolean;
}

/**
 * Stan integracji (INT-01, PRD sekcja 5), sprawdzany w kolejności: „Nie skonfigurowano” →
 * „Synchronizacja trwa” → „Błąd” → „Dane niekompletne” → „Aktualne”. Czas `nowMs` pochodzi
 * z zegara klienta.
 */
export function integrationStateOf(status: SyncStatus, nowMs: number): IntegrationState {
  const reasons = reasonsOf(status, nowMs);
  if (!status.configured) {
    return { kind: 'notConfigured', reasons, dataComplete: false };
  }
  if (status.isRunning) {
    // Ostatni znany wynik musi być sukcesem; wynik trwającego przebiegu nie jest znany.
    const lastSucceeded = status.lastRun?.status === 'succeeded';
    return { kind: 'running', reasons, dataComplete: lastSucceeded && reasons.length === 0 };
  }
  if (status.lastRun && status.lastRun.status !== 'succeeded') {
    return { kind: 'error', reasons, dataComplete: false };
  }
  if (reasons.length > 0) {
    return { kind: 'incomplete', reasons, dataComplete: false };
  }
  return { kind: 'current', reasons, dataComplete: true };
}

function reasonsOf(status: SyncStatus, nowMs: number): IncompleteReason[] {
  const reasons: IncompleteReason[] = [];
  if (status.openErrors > 0) {
    reasons.push('openErrors');
  }
  const success = status.lastSuccessAtUtc === null ? NaN : Date.parse(status.lastSuccessAtUtc);
  if (Number.isNaN(success)) {
    reasons.push('neverSucceeded');
  } else if (nowMs - success > STALE_AFTER_MS) {
    reasons.push('stale');
  }
  return reasons;
}

/** Wyświetlany stan: przed pierwszą odpowiedzią, po nieudanym odczycie albo znany. */
export type IntegrationView =
  | { readonly phase: 'loading' }
  | { readonly phase: 'unknown'; readonly last: SyncStatus | null }
  | {
      readonly phase: 'known';
      readonly status: SyncStatus;
      readonly state: IntegrationState;
      /** Odczyt starszy niż dwa okresy odświeżania (reguła wieku odczytu). */
      readonly aged: boolean;
      /** Chwila odpowiedzi ostatniego udanego odczytu (ISO 8601). */
      readonly readAtUtc: string;
    };

export const KIND_LABELS: Readonly<Record<IntegrationKind, string>> = {
  current: 'Aktualne',
  running: 'Synchronizacja trwa',
  error: 'Błąd',
  incomplete: 'Dane niekompletne',
  notConfigured: 'Nie skonfigurowano',
};

export const LOADING_LABEL = 'Sprawdzanie stanu…';
export const UNKNOWN_LABEL = 'Stan nieznany';
export const LAST_KNOWN_SUFFIX = '(ostatni znany odczyt)';

const KIND_TONES: Readonly<Record<IntegrationKind, StatusTone>> = {
  current: 'success',
  running: 'info',
  error: 'error',
  incomplete: 'warning',
  notConfigured: 'warning',
};

export type IntegrationIcon =
  'check' | 'sync' | 'error' | 'warning' | 'plug' | 'question' | 'clock';

const KIND_ICONS: Readonly<Record<IntegrationKind, IntegrationIcon>> = {
  current: 'check',
  running: 'sync',
  error: 'error',
  incomplete: 'warning',
  notConfigured: 'plug',
};

/** Jak pokazać stan: tekst zawsze, ton i ikona tylko go wzmacniają. */
export interface IntegrationPresentation {
  readonly label: string;
  readonly tone: StatusTone;
  readonly icon: IntegrationIcon | null;
  /** „Stan z 14:05” w wariancie `aged`, inaczej `null`. */
  readonly agedText: string | null;
}

/**
 * Prezentacja stanu. W wariancie `aged` każdy stan dostaje dopisek z czasem odczytu, a
 * „Aktualne” dodatkowo traci ton `success` i „✓” (neutralny, zegar; plan NC1 i NC5).
 */
export function presentationOf(view: IntegrationView, nowMs: number): IntegrationPresentation {
  switch (view.phase) {
    case 'loading':
      return { label: LOADING_LABEL, tone: 'neutral', icon: null, agedText: null };
    case 'unknown':
      return { label: UNKNOWN_LABEL, tone: 'neutral', icon: 'question', agedText: null };
    case 'known': {
      const kind = view.state.kind;
      const agedText = view.aged ? `Stan z ${formatReadTime(view.readAtUtc, nowMs)}` : null;
      // Zbyt stary odczyt nie może potwierdzać kompletności: „Aktualne” traci ton i „✓”.
      // Pozostałe stany zachowują ton i ikonę ostrzeżenia, z dopiskiem czasu odczytu.
      if (view.aged && kind === 'current') {
        return { label: KIND_LABELS[kind], tone: 'neutral', icon: 'clock', agedText };
      }
      return { label: KIND_LABELS[kind], tone: KIND_TONES[kind], icon: KIND_ICONS[kind], agedText };
    }
  }
}

/** Dane są kompletne tylko przy świeżym, znanym odczycie, który to potwierdza. */
export function dataCompleteOf(view: IntegrationView): boolean {
  return view.phase === 'known' && !view.aged && view.state.dataComplete;
}

/** Czas „Ostatniej pełnej kontroli” do wyświetlenia (Europe/Warsaw). */
export function lastFullCheckText(view: IntegrationView): string {
  switch (view.phase) {
    case 'loading':
      return 'Sprawdzanie…';
    case 'unknown':
      return view.last
        ? `${formatDateTime(view.last.lastSuccessAtUtc)} ${LAST_KNOWN_SUFFIX}`
        : formatDateTime(null);
    case 'known':
      return formatDateTime(view.status.lastSuccessAtUtc);
  }
}

export const NOT_CONFIGURED_TEXT =
  'Integracja ze sklepem nie ma skonfigurowanych kluczy REST. Dzienne uzupełnianie, „Synchronizuj teraz” i „Ponów” nie działają.';

/** Wyjaśnienia stanu na ekranie „Integracja” w stałej kolejności. */
export function explanationsOf(status: SyncStatus, state: IntegrationState): string[] {
  const lines: string[] = [];
  switch (state.kind) {
    case 'notConfigured':
      lines.push(NOT_CONFIGURED_TEXT);
      break;
    case 'running':
      lines.push('Trwa uzupełnianie zamówień ze sklepu.');
      break;
    case 'error':
      lines.push('Ostatni przebieg uzupełniania nie powiódł się. Wyniki mogą być niepełne.');
      break;
    case 'current':
      lines.push('Ostatnie uzupełnianie się powiodło i nie ma otwartych błędów zamówień.');
      break;
    case 'incomplete':
      break;
  }
  if (state.kind !== 'current') {
    for (const reason of state.reasons) {
      lines.push(reasonText(reason, status));
    }
  }
  return lines;
}

function reasonText(reason: IncompleteReason, status: SyncStatus): string {
  switch (reason) {
    case 'openErrors':
      return `Otwarte błędy zamówień: ${status.openErrors}.`;
    case 'stale':
      return 'Od ostatniej pełnej kontroli minęło ponad 25 godzin.';
    case 'neverSucceeded':
      return 'Pełna kontrola jeszcze się nie odbyła.';
  }
}

/** Słowniki z API; nieznana wartość wyświetla się jako surowy kod. */
export const RUN_STATUS_LABELS: Readonly<Record<string, string>> = {
  succeeded: 'Udany',
  failed: 'Nieudany',
  abandoned: 'Przerwany',
  running: 'W toku',
};

export const RUN_SOURCE_LABELS: Readonly<Record<string, string>> = {
  schedule: 'Automatyczny (23:00)',
  manual: 'Ręczny',
};

export const ERROR_SOURCE_LABELS: Readonly<Record<string, string>> = {
  webhook: 'Webhook',
  backfill: 'Uzupełnianie',
  retry: 'Ponowienie',
};

export const CLOSED_BY_LABELS: Readonly<Record<string, string>> = {
  webhook: 'Webhook',
  backfill: 'Uzupełnianie',
  retry: 'Ponów',
};

export function labelOf(labels: Readonly<Record<string, string>>, code: string): string {
  return Object.hasOwn(labels, code) ? labels[code] : code;
}
