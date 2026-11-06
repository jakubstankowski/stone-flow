import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IntegrationStatus } from './integration-status';

export const INCOMPLETE_TEXT = 'Nie pobrano wszystkich zamówień. Wyniki mogą być niepełne.';
export const UNKNOWN_TEXT = 'Nie udało się sprawdzić stanu integracji. Wyniki mogą być niepełne.';
export const AGED_TEXT = 'Stan integracji może być nieaktualny. Wyniki mogą być niepełne.';

type Variant = 'loading' | 'complete' | 'incomplete' | 'unknown' | 'aged';

/**
 * „Ostatnia pełna kontrola” przy wynikach (PRD sekcja 5: zawsze), a przy niekompletnych danych
 * także ostrzeżenie (design guide sekcja 8). Bez wejść: czyta wspólny `IntegrationStatus`, więc
 * zgadza się z nagłówkiem. Osadzają go widoki raportowe (SF-19, SF-20).
 */
@Component({
  selector: 'app-incomplete-data-notice',
  imports: [RouterLink],
  template: `@switch (variant()) {
    @case ('loading') {
      <p class="flex min-h-11 items-center text-sm text-(--sf-muted)">
        Ostatnia pełna kontrola: Sprawdzanie…
      </p>
    }
    @case ('complete') {
      <p class="flex flex-wrap items-center gap-x-2 text-sm text-(--sf-muted)">
        <span>Ostatnia pełna kontrola: {{ status.lastFullCheck() }}</span>
        <a
          class="link inline-flex min-h-11 items-center text-(--sf-accent-text)"
          routerLink="/integracja"
          >Integracja</a
        >
      </p>
    }
    @default {
      <div
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
          <p class="font-semibold text-(--sf-warning)">{{ warning() }}</p>
          @if (status.presentation().agedText; as aged) {
            <p class="text-sm">{{ aged }}</p>
          }
          <p class="text-sm">Ostatnia pełna kontrola: {{ status.lastFullCheck() }}</p>
          <a
            class="link inline-flex min-h-11 items-center self-start text-(--sf-accent-text)"
            routerLink="/integracja"
            >Przejdź do integracji</a
          >
        </div>
      </div>
    }
  }`,
})
export class IncompleteDataNotice {
  protected readonly status = inject(IntegrationStatus);

  protected readonly variant = computed<Variant>(() => {
    const view = this.status.view();
    if (view.phase === 'loading') {
      return 'loading';
    }
    if (view.phase === 'unknown') {
      return 'unknown';
    }
    if (view.aged) {
      return 'aged';
    }
    return view.state.dataComplete ? 'complete' : 'incomplete';
  });

  protected readonly warning = computed(() => {
    switch (this.variant()) {
      case 'unknown':
        return UNKNOWN_TEXT;
      case 'aged':
        return AGED_TEXT;
      default:
        return INCOMPLETE_TEXT;
    }
  });
}
