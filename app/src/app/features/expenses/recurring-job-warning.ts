import { JobStatus } from './jobs-api';

/** Klucz zadania tworzenia wystąpień (SF-16). */
export const RECURRING_JOB_KEY = 'recurring-expenses';

/** Wiek ostatniego sukcesu, po którym pokazuje się ostrzeżenie (zadanie biegnie raz dziennie). */
export const RECURRING_JOB_SUCCESS_MAX_AGE_MS = 25 * 60 * 60 * 1000;

export type RecurringJobWarning =
  | { readonly kind: 'none' }
  | {
      readonly kind: 'warning';
      /** Czas w nawiasie pierwszego zdania (przebieg albo — przy wieku sukcesu — sukces). */
      readonly shownAtUtc: string;
      readonly lastSuccessStartedAtUtc: string | null;
    };

const NONE: RecurringJobWarning = { kind: 'none' };

/** Pozycja zadania `recurring-expenses` z listy `GET /api/jobs`. */
export function recurringJobStatus(jobs: readonly JobStatus[]): JobStatus | undefined {
  return jobs.find((job) => job.key === RECURRING_JOB_KEY);
}

/**
 * Ostrzeżenie o niepowodzeniu zadania: ostatni przebieg `failed`/`abandoned` albo ostatni sukces
 * starszy niż 25 h. Brak pozycji, brak przebiegów i pierwszy `running` bez sukcesu nie ostrzegają;
 * `running` po wcześniejszych błędach ostrzega.
 */
export function recurringJobWarning(
  status: JobStatus | null | undefined,
  now: number,
): RecurringJobWarning {
  if (!status) {
    return NONE;
  }
  const lastRun = status.lastRun;
  const lastSuccess = status.lastSuccessStartedAtUtc;
  if (lastRun && (lastRun.status === 'failed' || lastRun.status === 'abandoned')) {
    return {
      kind: 'warning',
      shownAtUtc: lastRun.startedAtUtc,
      lastSuccessStartedAtUtc: lastSuccess,
    };
  }
  // Ponowienie po wcześniejszych błędach (`running` przy `consecutiveFailures > 0`): ostatni
  // zakończony przebieg był nieudany, więc ostrzeżenie zostaje — zwolniony jest tylko pierwszy przebieg.
  if (lastRun && lastRun.status === 'running' && status.consecutiveFailures > 0) {
    return {
      kind: 'warning',
      shownAtUtc: lastRun.startedAtUtc,
      lastSuccessStartedAtUtc: lastSuccess,
    };
  }
  if (!lastSuccess) {
    return NONE;
  }
  const successAt = Date.parse(lastSuccess);
  if (Number.isNaN(successAt) || now - successAt <= RECURRING_JOB_SUCCESS_MAX_AGE_MS) {
    return NONE;
  }
  // Przebieg trwający przy zbyt starym sukcesie: w nawiasie czas tego przebiegu.
  const shownAtUtc = lastRun && lastRun.status === 'running' ? lastRun.startedAtUtc : lastSuccess;
  return { kind: 'warning', shownAtUtc, lastSuccessStartedAtUtc: lastSuccess };
}
