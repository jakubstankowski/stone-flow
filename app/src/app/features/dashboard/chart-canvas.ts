import {
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { formatPln } from '../../shared/format/pl-format';
import { ActivePoint, CHART_FACTORY, ChartConfig, ChartHandle } from './chart-factory';

/** Tokeny kolorów serii (design guide §7); wartości odczytuje `getComputedStyle`. */
export type ChartToken = '--sf-accent' | '--sf-text' | '--sf-info';

export interface ChartSeries {
  readonly label: string;
  /** Kwoty z API jako ciągi dziesiętne; liczbą stają się tylko dla geometrii (`chartNumber`). */
  readonly values: readonly string[];
  readonly token: ChartToken;
  readonly shape: 'bar' | 'dashedLine';
}

export interface ChartSpec {
  /** `timeline`: słupki i linia w czasie; `horizontalBar`: poziome słupki (kanały, kategorie). */
  readonly type: 'timeline' | 'horizontalBar';
  readonly labels: readonly string[];
  readonly series: readonly ChartSeries[];
  readonly ariaLabel: string;
}

/**
 * Jedyna konwersja tekstu kwoty na liczbę w aplikacji: wyłącznie do geometrii słupków i linii.
 * Tooltip, region `aria-live`, tabele i listy pokazują ciągi z API przez `formatPln`.
 */
export function chartNumber(text: string): number {
  return Number(text);
}

/** Tekst aktywnego punktu: etykieta przedziału i sformatowane kwoty z API. */
export function pointText(spec: ChartSpec, index: number): string {
  const values = spec.series.map((series) => `${series.label} ${formatPln(series.values[index])}`);
  return `${spec.labels[index]}: ${values.join(', ')}`;
}

/** Konfiguracja Chart.js: kolory z tokenów, wynik jako linia przerywana ze znacznikami, oś od zera. */
export function chartConfig(
  spec: ChartSpec,
  color: (token: ChartToken) => string,
  reducedMotion: boolean,
): ChartConfig {
  const horizontal = spec.type === 'horizontalBar';
  const valueAxis = {
    beginAtZero: true,
    title: { display: true, text: 'zł' },
  };
  return {
    type: 'bar',
    data: {
      labels: [...spec.labels],
      datasets: spec.series.map((series) =>
        series.shape === 'bar'
          ? {
              type: 'bar' as const,
              label: series.label,
              data: series.values.map(chartNumber),
              backgroundColor: color(series.token),
              borderColor: color(series.token),
            }
          : {
              type: 'line' as const,
              label: series.label,
              data: series.values.map(chartNumber),
              borderColor: color(series.token),
              backgroundColor: color(series.token),
              borderDash: [6, 4],
              pointRadius: 4,
              pointStyle: 'rectRot' as const,
              fill: false,
            },
      ),
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      animation: reducedMotion ? false : undefined,
      indexAxis: horizontal ? 'y' : 'x',
      interaction: { mode: 'index', intersect: false },
      plugins: {
        legend: { display: spec.series.length > 1 },
        tooltip: {
          callbacks: {
            label: (context) =>
              `${context.dataset.label ?? ''}: ${formatPln(spec.series[context.datasetIndex]?.values[context.dataIndex])}`,
          },
        },
      },
      scales: horizontal ? { x: valueAxis } : { y: valueAxis },
    },
  } as ChartConfig;
}

function tokenColor(token: ChartToken): string {
  return getComputedStyle(document.documentElement).getPropertyValue(token).trim();
}

function prefersReducedMotion(): boolean {
  return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/**
 * Adapter wykresu (SF-19, design guide §7): canvas z `role="img"`, opisem i fokusem; strzałki
 * (←/→ w czasie, ↑/↓ dla poziomych słupków), Home i End przesuwają aktywny punkt i pokazują
 * tooltip, Escape go chowa, a wartość jest ogłaszana w `aria-live`. Tworzenie wykresu idzie przez
 * `CHART_FACTORY`, więc testy w jsdom zastępują Chart.js.
 */
@Component({
  selector: 'app-chart-canvas',
  template: `<div class="relative w-full" [class]="heightClass()">
      <canvas
        #canvas
        role="img"
        tabindex="0"
        class="rounded-field focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-(--sf-accent)"
        [attr.aria-label]="spec().ariaLabel"
        (keydown)="onKey($event)"
      ></canvas>
    </div>
    <p class="sr-only" aria-live="polite">{{ live() }}</p>`,
  host: { class: 'block' },
})
export class ChartCanvas {
  readonly spec = input.required<ChartSpec>();

  private readonly factory = inject(CHART_FACTORY);
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private chart: ChartHandle | null = null;
  private active = -1;

  protected readonly live = signal('');
  protected readonly heightClass = computed(() =>
    this.spec().type === 'timeline' ? 'h-72' : 'h-56',
  );

  constructor() {
    effect(() => {
      const spec = this.spec();
      const canvas = this.canvas().nativeElement;
      untracked(() => {
        const config = chartConfig(spec, tokenColor, prefersReducedMotion());
        if (this.chart) {
          this.chart.data = config.data;
          this.chart.update();
        } else {
          this.chart = this.factory(canvas, config);
        }
        this.active = -1;
        this.live.set('');
      });
    });
    inject(DestroyRef).onDestroy(() => this.chart?.destroy());
  }

  protected onKey(event: KeyboardEvent): void {
    const spec = this.spec();
    const count = spec.labels.length;
    if (count === 0 || !this.chart) {
      return;
    }
    const horizontal = spec.type === 'horizontalBar';
    const next = horizontal ? 'ArrowDown' : 'ArrowRight';
    const previous = horizontal ? 'ArrowUp' : 'ArrowLeft';
    let index: number;
    switch (event.key) {
      case next:
        index = this.active < 0 ? 0 : Math.min(count - 1, this.active + 1);
        break;
      case previous:
        index = this.active < 0 ? count - 1 : Math.max(0, this.active - 1);
        break;
      case 'Home':
        index = 0;
        break;
      case 'End':
        index = count - 1;
        break;
      case 'Escape':
        event.preventDefault();
        this.activate([]);
        this.active = -1;
        this.live.set('');
        return;
      default:
        return;
    }
    event.preventDefault();
    this.active = index;
    this.activate(spec.series.map((_, datasetIndex) => ({ datasetIndex, index })));
    this.live.set(pointText(spec, index));
  }

  private activate(points: ActivePoint[]): void {
    const chart = this.chart;
    if (!chart) {
      return;
    }
    chart.setActiveElements(points);
    chart.tooltip?.setActiveElements(points, { x: 0, y: 0 });
    chart.update();
  }
}
