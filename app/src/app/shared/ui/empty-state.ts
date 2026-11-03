import { Component, input } from '@angular/core';

/** Stan pustki z opcjonalną akcją w projekcji (design guide, sekcja 8). */
@Component({
  selector: 'app-empty-state',
  template: `<div
    class="flex flex-col items-start gap-3 rounded-box border border-dashed border-(--sf-border-strong) bg-base-100 p-6"
  >
    <p class="text-lg font-semibold">{{ title() }}</p>
    @if (description()) {
      <p class="text-(--sf-muted)">{{ description() }}</p>
    }
    <ng-content />
  </div>`,
})
export class EmptyState {
  readonly title = input.required<string>();
  readonly description = input<string | null>(null);
}
