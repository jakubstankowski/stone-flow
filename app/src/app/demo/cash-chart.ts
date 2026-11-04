import { Component, computed, input } from '@angular/core';
import { formatPln } from '../shared/format/pl-format';
import { MonthSummary } from './demo-data';

const WIDTH = 1100;
const HEIGHT = 330;
const PAD = { top: 16, right: 12, bottom: 32, left: 64 };
const STEP = 20_000;

const shortMonth = new Intl.DateTimeFormat('pl-PL', { month: 'short', timeZone: 'UTC' });

interface BarGroup {
  readonly x: number;
  readonly label: string;
  readonly salesY: number;
  readonly inflowsY: number;
  readonly title: string;
}

/**
 * TRYB DEMO: wykres miesięczny — słupki sprzedaży i wpływów, linia wydatków. Prosty SVG bez
 * biblioteki; kolory z tokenów motywu. Pod wykresem tabela z tymi samymi danymi (dostępność).
 */
@Component({
  selector: 'app-cash-chart',
  template: `
    <figure class="flex flex-col gap-3">
      <div class="flex flex-wrap gap-4 text-sm text-(--sf-muted)" aria-hidden="true">
        <span class="flex items-center gap-2"
          ><span class="size-3 rounded-sm bg-(--sf-accent)"></span>Sprzedaż</span
        >
        <span class="flex items-center gap-2"
          ><span class="size-3 rounded-sm bg-(--sf-info)"></span>Wpływy</span
        >
        <span class="flex items-center gap-2"
          ><span class="h-0.5 w-4 bg-(--sf-error)"></span>Wydatki</span
        >
      </div>
      <svg
        [attr.viewBox]="viewBox"
        class="h-auto w-full"
        role="img"
        aria-label="Sprzedaż, wpływy i wydatki w ostatnich 12 miesiącach"
      >
        @for (tick of ticks(); track tick.value) {
          <line
            [attr.x1]="pad.left"
            [attr.x2]="width - pad.right"
            [attr.y1]="tick.y"
            [attr.y2]="tick.y"
            class="stroke-(--sf-bg)"
            stroke-width="1"
          />
          <text
            [attr.x]="pad.left - 8"
            [attr.y]="tick.y + 4"
            text-anchor="end"
            class="fill-(--sf-muted) text-[13px]"
          >
            {{ tick.label }}
          </text>
        }
        @for (group of groups(); track group.label) {
          <g>
            <title>{{ group.title }}</title>
            <rect
              [attr.x]="group.x"
              [attr.y]="group.salesY"
              [attr.width]="barWidth()"
              [attr.height]="baseline - group.salesY"
              rx="3"
              class="fill-(--sf-accent)"
            />
            <rect
              [attr.x]="group.x + barWidth() + 3"
              [attr.y]="group.inflowsY"
              [attr.width]="barWidth()"
              [attr.height]="baseline - group.inflowsY"
              rx="3"
              class="fill-(--sf-info)"
            />
            <text
              [attr.x]="group.x + barWidth() + 1.5"
              [attr.y]="height - 10"
              text-anchor="middle"
              class="fill-(--sf-muted) text-[13px]"
            >
              {{ group.label }}
            </text>
          </g>
        }
        <polyline
          [attr.points]="expensePoints()"
          fill="none"
          class="stroke-(--sf-error)"
          stroke-width="2.5"
          stroke-linejoin="round"
        />
        @for (point of expenseDots(); track point.x) {
          <circle [attr.cx]="point.x" [attr.cy]="point.y" r="3.5" class="fill-(--sf-error)" />
        }
      </svg>
      <figcaption class="sr-only">
        <table>
          <caption>
            Dane wykresu
          </caption>
          <tr>
            <th scope="col">Miesiąc</th>
            <th scope="col">Sprzedaż</th>
            <th scope="col">Wpływy</th>
            <th scope="col">Wydatki</th>
          </tr>
          @for (month of months(); track month.month) {
            <tr>
              <th scope="row">{{ month.month }}</th>
              <td>{{ pln(month.sales) }}</td>
              <td>{{ pln(month.inflows) }}</td>
              <td>{{ pln(month.expenses) }}</td>
            </tr>
          }
        </table>
      </figcaption>
    </figure>
  `,
})
export class CashChart {
  readonly months = input.required<readonly MonthSummary[]>();

  protected readonly width = WIDTH;
  protected readonly height = HEIGHT;
  protected readonly pad = PAD;
  protected readonly viewBox = `0 0 ${WIDTH} ${HEIGHT}`;
  protected readonly baseline = HEIGHT - PAD.bottom;
  protected readonly pln = formatPln;

  private readonly max = computed(() => {
    const peak = Math.max(...this.months().flatMap((m) => [m.sales, m.inflows, m.expenses]));
    return Math.ceil(peak / STEP) * STEP;
  });

  private readonly slot = computed(
    () => (WIDTH - PAD.left - PAD.right) / Math.max(this.months().length, 1),
  );

  protected readonly barWidth = computed(() => Math.max((this.slot() - 14) / 2, 4));

  protected readonly ticks = computed(() => {
    const ticks = [];
    for (let value = 0; value <= this.max(); value += STEP) {
      ticks.push({ value, y: this.y(value), label: value === 0 ? '0' : `${value / 1000} tys.` });
    }
    return ticks;
  });

  protected readonly groups = computed<BarGroup[]>(() =>
    this.months().map((m, index) => ({
      x: PAD.left + index * this.slot() + 7,
      label: shortMonth.format(new Date(`${m.month}-01T00:00:00Z`)).replace('.', ''),
      salesY: this.y(m.sales),
      inflowsY: this.y(m.inflows),
      title: `${m.month}: sprzedaż ${formatPln(m.sales)}, wpływy ${formatPln(m.inflows)}, wydatki ${formatPln(m.expenses)}`,
    })),
  );

  protected readonly expenseDots = computed(() =>
    this.months().map((m, index) => ({
      x: PAD.left + index * this.slot() + this.slot() / 2,
      y: this.y(m.expenses),
    })),
  );

  protected readonly expensePoints = computed(() =>
    this.expenseDots()
      .map((point) => `${point.x},${point.y}`)
      .join(' '),
  );

  private y(value: number): number {
    const plot = HEIGHT - PAD.top - PAD.bottom;
    return PAD.top + plot - (value / this.max()) * plot;
  }
}
