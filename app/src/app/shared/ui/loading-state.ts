import { Component, computed, input } from '@angular/core';

/** Stan ładowania: szkielet o docelowym rozmiarze, bez fikcyjnych wartości (design guide, sekcja 8). */
@Component({
  selector: 'app-loading-state',
  template: `<div role="status" class="flex flex-col gap-2">
    <span class="sr-only">{{ label() }}</span>
    @for (row of placeholders(); track row) {
      <div class="skeleton h-10 w-full rounded-field" aria-hidden="true"></div>
    }
  </div>`,
})
export class LoadingState {
  readonly rows = input(3);
  readonly label = input('Ładowanie…');

  protected readonly placeholders = computed(() =>
    Array.from({ length: Math.max(1, this.rows()) }, (_, index) => index),
  );
}
