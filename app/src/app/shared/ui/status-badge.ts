import { Component, computed, input } from '@angular/core';

export type StatusTone = 'neutral' | 'success' | 'warning' | 'error' | 'info';

const TONE_CLASSES: Record<StatusTone, string> = {
  neutral: 'text-base-content border-(--sf-border-strong)',
  success: 'text-(--sf-success) border-(--sf-success)',
  warning: 'text-(--sf-warning) border-(--sf-warning)',
  error: 'text-(--sf-error) border-(--sf-error)',
  info: 'text-(--sf-info) border-(--sf-info)',
};

/**
 * Etykieta statusu: tekst jest zawsze widoczny, ton jest tylko wzmocnieniem
 * (design guide, sekcje 2 i 6). Nie definiuje słownika statusów biznesowych.
 */
@Component({
  selector: 'app-status-badge',
  template: `<span [class]="classes()">
    @if (marker()) {
      <span class="size-2 rounded-full bg-current" aria-hidden="true"></span>
    }
    {{ label() }}
  </span>`,
})
export class StatusBadge {
  readonly label = input.required<string>();
  readonly tone = input<StatusTone>('neutral');
  readonly marker = input(false);

  protected readonly classes = computed(
    () =>
      `badge h-auto min-h-6 gap-1.5 border bg-base-100 px-2.5 py-0.5 text-sm font-medium ${TONE_CLASSES[this.tone()]}`,
  );
}
