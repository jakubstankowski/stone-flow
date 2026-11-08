import { Component, input } from '@angular/core';
import { StatusBadge } from '../../shared/ui/status-badge';
import { IntegrationPresentation } from './integration-state';

/**
 * Stan integracji: widoczna etykieta z ikoną (`aria-hidden`), a w wariancie `aged` dopisek
 * „Stan z …”. Kolor nigdy nie jest jedynym nośnikiem stanu.
 */
@Component({
  selector: 'app-integration-state-badge',
  imports: [StatusBadge],
  template: `<span class="inline-flex flex-wrap items-center gap-x-2 gap-y-1">
    <app-status-badge [label]="presentation().label" [tone]="presentation().tone">
      @switch (presentation().icon) {
        @case ('check') {
          <svg
            data-icon="check"
            aria-hidden="true"
            focusable="false"
            class="size-4"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <circle cx="12" cy="12" r="9" />
            <path d="m8 12 3 3 5-6" />
          </svg>
        }
        @case ('sync') {
          <svg
            data-icon="sync"
            aria-hidden="true"
            focusable="false"
            class="size-4 motion-safe:animate-spin"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <path d="M20 12a8 8 0 0 1-14 5.3M4 12a8 8 0 0 1 14-5.3" />
            <path d="M18 3v4h-4M6 21v-4h4" />
          </svg>
        }
        @case ('error') {
          <svg
            data-icon="error"
            aria-hidden="true"
            focusable="false"
            class="size-4"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
          >
            <circle cx="12" cy="12" r="9" />
            <path d="m9 9 6 6M15 9l-6 6" />
          </svg>
        }
        @case ('warning') {
          <svg
            data-icon="warning"
            aria-hidden="true"
            focusable="false"
            class="size-4"
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
        }
        @case ('plug') {
          <svg
            data-icon="plug"
            aria-hidden="true"
            focusable="false"
            class="size-4"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
          >
            <path d="M9 3v4M15 3v4M7 7h10v4a5 5 0 0 1-10 0V7ZM12 16v5M4 4l16 16" />
          </svg>
        }
        @case ('question') {
          <svg
            data-icon="question"
            aria-hidden="true"
            focusable="false"
            class="size-4"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
          >
            <circle cx="12" cy="12" r="9" />
            <path d="M9.5 9a2.5 2.5 0 1 1 3.5 2.3c-.6.3-1 .9-1 1.7M12 17h.01" />
          </svg>
        }
        @case ('clock') {
          <svg
            data-icon="clock"
            aria-hidden="true"
            focusable="false"
            class="size-4"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
          >
            <circle cx="12" cy="12" r="9" />
            <path d="M12 7v5l3 2" />
          </svg>
        }
      }
    </app-status-badge>
    @if (presentation().agedText; as aged) {
      <span class="text-sm text-(--sf-muted)">{{ aged }}</span>
    }
  </span>`,
})
export class IntegrationStateBadge {
  readonly presentation = input.required<IntegrationPresentation>();
}
