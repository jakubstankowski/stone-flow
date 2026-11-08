import { Component, computed, input, output } from '@angular/core';

export type ButtonVariant = 'primary' | 'secondary' | 'danger';

const VARIANT_CLASSES: Record<ButtonVariant, string> = {
  primary: 'btn-primary hover:bg-(--sf-accent-hover) hover:border-(--sf-accent-hover)',
  secondary: 'bg-base-100 text-base-content border-(--sf-border-strong) hover:bg-base-200',
  danger: 'btn-error',
};

/**
 * Przycisk aplikacji (design guide, sekcja 6). W stanie `busy` pozostaje fokusowalny,
 * ale blokuje kolejne kliknięcia i wysłanie formularza.
 */
@Component({
  selector: 'app-button',
  templateUrl: './button.html',
  host: { class: 'inline-flex' },
})
export class Button {
  readonly variant = input<ButtonVariant>('secondary');
  readonly type = input<'button' | 'submit'>('button');
  readonly busy = input(false);
  readonly busyLabel = input('Zapisywanie…');
  readonly disabled = input(false);
  /** Id elementu z opisem (np. powodem wyłączenia) dla `aria-describedby` natywnego przycisku. */
  readonly describedBy = input<string | null>(null);

  readonly pressed = output<void>();

  protected readonly classes = computed(
    () => `btn min-h-11 h-auto px-5 text-base font-medium ${VARIANT_CLASSES[this.variant()]}`,
  );

  protected onClick(event: MouseEvent): void {
    if (this.busy() || this.disabled()) {
      event.preventDefault();
      event.stopImmediatePropagation();
      return;
    }
    this.pressed.emit();
  }
}
