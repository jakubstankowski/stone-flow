import { Component, DestroyRef, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IntegrationStateBadge } from '../features/integration/integration-state-badge';
import { IntegrationStatus } from '../features/integration/integration-status';

/**
 * Stan integracji w nagłówku powłoki (design guide, sekcja 3): widoczna etykieta z ikoną jako
 * link do ekranu „Integracja”. Uruchamia odświeżanie stanu na czas życia powłoki.
 */
@Component({
  selector: 'app-integration-indicator',
  imports: [IntegrationStateBadge, RouterLink],
  templateUrl: './integration-indicator.html',
  host: { class: 'flex min-w-0 items-center' },
})
export class IntegrationIndicator {
  protected readonly status = inject(IntegrationStatus);

  /**
   * Nazwa dostępna linku: zaczyna się od widocznej etykiety (WCAG 2.5.3), potem dopisek
   * `aged` i czas ostatniej pełnej kontroli. W `loading` sama etykieta. Atrybut zamiast ukrytego
   * tekstu, bo białe znaki szablonu dawały pauzę przed przecinkiem; `title` celowo brak.
   */
  protected readonly accessibleName = computed(() => {
    const presentation = this.status.presentation();
    if (this.status.view().phase === 'loading') {
      return presentation.label;
    }
    const aged = presentation.agedText ? `, ${presentation.agedText.toLowerCase()}` : '';
    return `${presentation.label}${aged}, ostatnia pełna kontrola: ${this.status.lastFullCheck()}. Stan integracji — przejdź do ekranu Integracja`;
  });

  constructor() {
    this.status.start();
    inject(DestroyRef).onDestroy(() => this.status.stop());
  }
}
