import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { formatDate } from '../../shared/format/pl-format';
import {
  RecurringExpensePreview,
  RecurringPreviewQuery,
  occurrencesText,
  plural,
} from './recurring-expense-models';
import { RecurringExpensesApi } from './recurring-expenses-api';

/** Opóźnienie podglądu po ostatniej zmianie harmonogramu. */
export const PREVIEW_DELAY_MS = 300;

export const PREVIEW_TEXTS = {
  noNext: 'Po zapisie nie powstaną kolejne wystąpienia — do końca nie wypada już żaden termin.',
  paused:
    'Wydatek cykliczny jest wstrzymany — nowe wystąpienia nie powstają; zmiany zadziałają od wznowienia.',
  error: 'Nie udało się przygotować podglądu.',
} as const;

export function nextOccurrenceText(nextDueDate: string): string {
  return `Następne wystąpienie: ${formatDate(nextDueDate)}. Powstanie tego dnia jako wydatek planowany — wcześniej nie będzie go na liście i nie można go opłacić.`;
}

function immediateText(count: number, firstDueDate: string | null): string {
  const verb = plural(count, { one: 'powstanie', few: 'powstaną', many: 'powstanie' });
  return `Po zapisie od razu ${verb} ${occurrencesText(count)} za terminy od ${formatDate(firstDueDate)} do dzisiaj.`;
}

function beforeLaunchText(count: number): string {
  const forms = plural(count, {
    one: 'ma termin sprzed uruchomienia systemu — nie wejdzie do raportów.',
    few: 'mają termin sprzed uruchomienia systemu — nie wejdą do raportów.',
    many: 'ma termin sprzed uruchomienia systemu — nie wejdą do raportów.',
  });
  return `${count} z nich ${forms}`;
}

function droppedText(count: number): string {
  const forms = plural(count, {
    one: 'należne wystąpienie, które jeszcze nie powstało, przestaje',
    few: 'należne wystąpienia, które jeszcze nie powstały, przestają',
    many: 'należnych wystąpień, które jeszcze nie powstały, przestaje',
  });
  return `${count} ${forms} się liczyć po tej zmianie.`;
}

function createdOutsideText(
  count: number,
  boundary: 'po nowym końcu' | 'przed nowym początkiem',
): string {
  const forms = plural(count, {
    one: `utworzone wystąpienie ma termin ${boundary} — pozostanie.`,
    few: `utworzone wystąpienia mają termin ${boundary} — pozostaną.`,
    many: `utworzonych wystąpień ma termin ${boundary} — pozostaną.`,
  });
  return `${count} ${forms}`;
}

/**
 * Teksty podglądu zapisu. Definicja wstrzymana (NC1) zamiast linii o następnym wystąpieniu
 * i liczbach pokazuje tylko informację o wstrzymaniu, a liczby utworzonych wystąpień spoza
 * nowego zakresu nadal.
 */
export function previewLines(preview: RecurringExpensePreview): readonly string[] {
  const lines: string[] = [];
  if (preview.paused) {
    lines.push(PREVIEW_TEXTS.paused);
  } else {
    lines.push(
      preview.nextDueDate ? nextOccurrenceText(preview.nextDueDate) : PREVIEW_TEXTS.noNext,
    );
    if (preview.occurrencesToCreate > 0) {
      lines.push(immediateText(preview.occurrencesToCreate, preview.firstImmediateDueDate));
    }
    if (preview.beforeLaunchCount > 0) {
      lines.push(beforeLaunchText(preview.beforeLaunchCount));
    }
    if (preview.droppedDueCount > 0) {
      lines.push(droppedText(preview.droppedDueCount));
    }
  }
  if (preview.createdAfterEndCount > 0) {
    lines.push(createdOutsideText(preview.createdAfterEndCount, 'po nowym końcu'));
  }
  if (preview.createdBeforeStartCount > 0) {
    lines.push(createdOutsideText(preview.createdBeforeStartCount, 'przed nowym początkiem'));
  }
  return lines;
}

export type PreviewState =
  | { readonly phase: 'idle' }
  | { readonly phase: 'loading' }
  | { readonly phase: 'loaded'; readonly preview: RecurringExpensePreview }
  | { readonly phase: 'error' };

/**
 * Podgląd z opóźnieniem: każde `request` odkłada odczyt o 300 ms, a odpowiedź starszego
 * żądania jest pomijana (ostatnie żądanie wygrywa). `null` (niepoprawny format) czyści podgląd.
 */
export class RecurringPreviewLoader {
  private readonly stateSignal = signal<PreviewState>({ phase: 'idle' });
  private timer: ReturnType<typeof setTimeout> | null = null;
  private seq = 0;

  readonly state = this.stateSignal.asReadonly();

  constructor(
    private readonly fetch: (query: RecurringPreviewQuery) => Promise<RecurringExpensePreview>,
    private readonly delayMs = PREVIEW_DELAY_MS,
  ) {}

  request(query: RecurringPreviewQuery | null): void {
    const seq = ++this.seq;
    this.clearTimer();
    if (!query) {
      this.stateSignal.set({ phase: 'idle' });
      return;
    }
    this.timer = setTimeout(() => {
      this.timer = null;
      void this.run(query, seq);
    }, this.delayMs);
  }

  dispose(): void {
    this.seq++;
    this.clearTimer();
  }

  private async run(query: RecurringPreviewQuery, seq: number): Promise<void> {
    if (this.stateSignal().phase !== 'loaded') {
      this.stateSignal.set({ phase: 'loading' });
    }
    try {
      const preview = await this.fetch(query);
      if (seq === this.seq) {
        this.stateSignal.set({ phase: 'loaded', preview });
      }
    } catch {
      if (seq === this.seq) {
        this.stateSignal.set({ phase: 'error' });
      }
    }
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }
}

/**
 * Region `aria-live="polite"` pod „Harmonogramem”: skutki zapisu wyliczone przez API. Nie ma
 * akcji zapłaty. Błąd podglądu nie blokuje zapisu.
 */
@Component({
  selector: 'app-recurring-expense-preview',
  template: `<div aria-live="polite" class="flex flex-col gap-1" data-testid="podglad-cykliczny">
    @if (lines().length > 0) {
      <div class="flex flex-col gap-1 rounded-box border border-(--sf-info) bg-base-100 p-3">
        @for (line of lines(); track $index) {
          <p>{{ line }}</p>
        }
      </div>
    }
  </div>`,
})
export class RecurringExpensePreviewPanel {
  readonly query = input<RecurringPreviewQuery | null>(null);

  private readonly api = inject(RecurringExpensesApi);
  private readonly loader = new RecurringPreviewLoader((query) => this.api.preview(query));

  protected readonly lines = computed<readonly string[]>(() => {
    const state = this.loader.state();
    if (state.phase === 'loaded') {
      return previewLines(state.preview);
    }
    return state.phase === 'error' ? [PREVIEW_TEXTS.error] : [];
  });

  constructor() {
    effect(() => {
      const query = this.query();
      untracked(() => this.loader.request(query));
    });
    inject(DestroyRef).onDestroy(() => this.loader.dispose());
  }
}
