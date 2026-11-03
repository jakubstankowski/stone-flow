import {
  Component,
  ElementRef,
  Renderer2,
  computed,
  contentChild,
  effect,
  inject,
  input,
} from '@angular/core';

/**
 * Pole formularza: etykieta nad kontrolką, wymagalność, tekst pomocniczy i błąd
 * (design guide, sekcja 6). Kontrolka jest projekcją oznaczoną `#control`;
 * komponent nie waliduje, tylko wiąże atrybuty dostępności.
 */
@Component({
  selector: 'app-field',
  templateUrl: './field.html',
  host: { class: 'flex flex-col gap-1' },
})
export class Field {
  readonly label = input.required<string>();
  readonly fieldId = input.required<string>();
  readonly required = input(false);
  readonly hint = input<string | null>(null);
  readonly error = input<string | null>(null);

  protected readonly hintId = computed(() => `${this.fieldId()}-pomoc`);
  protected readonly errorId = computed(() => `${this.fieldId()}-blad`);

  private readonly control = contentChild('control', { read: ElementRef });
  private readonly renderer = inject(Renderer2);

  constructor() {
    effect(() => {
      const control = this.control();
      if (!control) {
        return;
      }
      const element = control.nativeElement as HTMLElement;
      const describedBy = [this.hint() ? this.hintId() : null, this.error() ? this.errorId() : null]
        .filter((id): id is string => id !== null)
        .join(' ');

      this.renderer.setAttribute(element, 'id', this.fieldId());
      this.setOrRemove(element, 'aria-describedby', describedBy || null);
      this.setOrRemove(element, 'aria-invalid', this.error() ? 'true' : null);
      this.setOrRemove(element, 'aria-required', this.required() ? 'true' : null);
    });
  }

  private setOrRemove(element: HTMLElement, name: string, value: string | null): void {
    if (value === null) {
      this.renderer.removeAttribute(element, name);
    } else {
      this.renderer.setAttribute(element, name, value);
    }
  }
}
