import { Component, input, output } from '@angular/core';
import { Button } from './button';

/** Stan błędu: skutek i ponowienie, bez szczegółów technicznych (design guide, sekcja 8). */
@Component({
  selector: 'app-error-state',
  imports: [Button],
  template: `<div
    role="alert"
    class="flex flex-col items-start gap-3 rounded-box border border-(--sf-error) bg-base-100 p-6"
  >
    <p class="font-semibold text-(--sf-error)">{{ message() }}</p>
    <app-button (pressed)="retry.emit()">Spróbuj ponownie</app-button>
  </div>`,
})
export class ErrorState {
  readonly message = input('Nie udało się wczytać danych.');
  readonly retry = output<void>();
}
