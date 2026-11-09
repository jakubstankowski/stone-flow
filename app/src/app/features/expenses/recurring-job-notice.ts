import { Component, computed, inject, signal } from '@angular/core';
import { toApiError } from '../../core/http/api-error';
import { NO_DATA, formatDateTime } from '../../shared/format/pl-format';
import { JobsApi } from './jobs-api';
import {
  RecurringJobWarning,
  recurringJobStatus,
  recurringJobWarning,
} from './recurring-job-warning';

export const RECURRING_JOB_UNKNOWN_TEXT =
  'Nie udało się sprawdzić automatycznego tworzenia wydatków cyklicznych. Koszty mogą być zaniżone.';

type NoticeState =
  | { readonly phase: 'loading' }
  | { readonly phase: 'unknown' }
  | { readonly phase: 'loaded'; readonly warning: RecurringJobWarning };

/** Pierwsze zdanie ostrzeżenia z czasem przebiegu (albo sukcesu) w Europe/Warsaw. */
export function recurringJobFailedText(shownAtUtc: string): string {
  return `Automatyczne tworzenie wydatków cyklicznych nie powiodło się (${formatDateTime(shownAtUtc)}). Koszty mogą być zaniżone.`;
}

export function recurringJobLastSuccessText(lastSuccessStartedAtUtc: string | null): string {
  return `Ostatnie udane tworzenie: ${lastSuccessStartedAtUtc ? formatDateTime(lastSuccessStartedAtUtc) : NO_DATA}`;
}

/**
 * Trwałe ostrzeżenie o niepowodzeniu zadania `recurring-expenses` (wygląd jak
 * `app-incomplete-data-notice`). Czyta `GET /api/jobs` raz przy wejściu na stronę — bez
 * odpytywania w tle (DOST-01). Bez wejść, więc osadzi go także SF-19. W czasie ładowania i bez
 * ostrzeżenia nic nie pokazuje.
 */
@Component({
  selector: 'app-recurring-job-notice',
  template: `@if (content(); as text) {
    <div
      role="status"
      data-variant="warning"
      class="flex items-start gap-3 rounded-box border border-(--sf-warning) bg-base-100 p-4"
    >
      <svg
        aria-hidden="true"
        focusable="false"
        class="mt-0.5 size-5 shrink-0 text-(--sf-warning)"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
      >
        <path d="M12 3 2 20h20L12 3Z" />
        <path d="M12 10v4M12 17h.01" />
      </svg>
      <div class="flex flex-col gap-1">
        <h2 class="font-semibold text-(--sf-warning)">{{ text.heading }}</h2>
        @if (text.detail; as detail) {
          <p class="text-sm">{{ detail }}</p>
        }
      </div>
    </div>
  }`,
})
export class RecurringJobNotice {
  private readonly jobs = inject(JobsApi);
  private readonly state = signal<NoticeState>({ phase: 'loading' });

  protected readonly content = computed<{ heading: string; detail: string | null } | null>(() => {
    const state = this.state();
    if (state.phase === 'loading') {
      return null;
    }
    if (state.phase === 'unknown') {
      return { heading: RECURRING_JOB_UNKNOWN_TEXT, detail: null };
    }
    const warning = state.warning;
    if (warning.kind === 'none') {
      return null;
    }
    return {
      heading: recurringJobFailedText(warning.shownAtUtc),
      detail: recurringJobLastSuccessText(warning.lastSuccessStartedAtUtc),
    };
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      const jobs = await this.jobs.list();
      this.state.set({
        phase: 'loaded',
        warning: recurringJobWarning(recurringJobStatus(jobs), Date.now()),
      });
    } catch (error) {
      // Stan ostrożny tylko przy braku połączenia i 5xx; inny błąd (4xx) nie jest informacją
      // o zadaniu, więc komponent nic nie pokazuje.
      this.state.set(
        toApiError(error).kind === 'network'
          ? { phase: 'unknown' }
          : { phase: 'loaded', warning: recurringJobWarning(null, Date.now()) },
      );
    }
  }
}
