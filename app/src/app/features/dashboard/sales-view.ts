import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { formatPln } from '../../shared/format/pl-format';
import { ErrorState } from '../../shared/ui/error-state';
import { LoadingState } from '../../shared/ui/loading-state';
import { ChartCanvas, ChartSpec } from './chart-canvas';
import { DashboardService } from './dashboard.service';
import { KpiCard, KpiAction } from './kpi-card';
import {
  CATEGORY_LABELS,
  CHANNEL_LABELS,
  CostCategory,
  KPI_DESCRIPTIONS,
  SalesChannel,
} from './report-models';
import {
  CardComparisonText,
  amountComparisonText,
  baseLabel,
  beforeLaunchMessage,
  bucketLabel,
  bucketRange,
  countComparisonText,
  periodLabel,
} from './report-text';
import { SalesComponents } from './sales-components';

interface CardView {
  readonly key: 'sales' | 'costs' | 'result' | 'orders';
  readonly label: string;
  readonly value: string;
  readonly description: string;
  readonly comparison: CardComparisonText;
  readonly action: KpiAction;
  readonly hint: string | null;
}

function prefersReducedMotion(): boolean {
  return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/**
 * Widok „Sprzedaż” Pulpitu (SF-19): cztery karty KPI z porównaniem, wykresy (w czasie, kanały,
 * kategorie) z alternatywą tekstową i zestawienie składników. Wszystkie wartości są ciągami z API
 * formatowanymi przez `shared/format`; „Razem” w tabeli danych to wartości kart, nie suma w UI.
 */
@Component({
  selector: 'app-sales-view',
  imports: [ChartCanvas, ErrorState, KpiCard, LoadingState, RouterLink, SalesComponents],
  templateUrl: './sales-view.html',
  host: { class: 'flex flex-col gap-6' },
})
export class SalesView {
  protected readonly dashboard = inject(DashboardService);

  private readonly components = viewChild(SalesComponents);

  protected readonly formatPln = formatPln;
  protected readonly bucketRange = bucketRange;
  protected readonly channelLabels = CHANNEL_LABELS;
  protected readonly categoryLabels = CATEGORY_LABELS;
  protected readonly tableOpen = signal(false);
  protected readonly tableId = 'pulpit-dane-wykresu';

  protected readonly report = this.dashboard.report;

  protected readonly beforeLaunch = computed(() => {
    const report = this.report();
    return report?.period.beforeLaunch ? beforeLaunchMessage(report.period) : null;
  });

  protected readonly periodText = computed(() => {
    const report = this.report();
    return report ? periodLabel(report.period) : '';
  });

  protected readonly cards = computed<readonly CardView[]>(() => {
    const report = this.report();
    const kpis = report?.kpis;
    if (!report || !kpis) {
      return [];
    }
    const base = baseLabel(report.period, report.comparison);
    const costsLink = this.dashboard.costsLink();
    return [
      {
        key: 'sales',
        label: 'Sprzedaż',
        value: formatPln(kpis.sales.value),
        description: KPI_DESCRIPTIONS.sales,
        comparison: amountComparisonText(kpis.sales.comparison, base),
        action: { kind: 'button' },
        hint: 'Pokaż składniki',
      },
      {
        key: 'costs',
        label: 'Koszty',
        value: formatPln(kpis.costs.value),
        description: KPI_DESCRIPTIONS.costs,
        comparison: amountComparisonText(kpis.costs.comparison, base),
        action: costsLink ? { kind: 'link', target: costsLink } : null,
        hint: costsLink ? 'Pokaż wydatki' : null,
      },
      {
        key: 'result',
        label: 'Wynik przed wypłatami',
        value: formatPln(kpis.result.value),
        description: KPI_DESCRIPTIONS.result,
        comparison: amountComparisonText(kpis.result.comparison, base),
        action: null,
        hint: null,
      },
      {
        key: 'orders',
        label: 'Liczba zamówień',
        value: String(kpis.orderCount.value),
        description: KPI_DESCRIPTIONS.orderCount,
        comparison: countComparisonText(kpis.orderCount.comparison, base),
        action: { kind: 'button' },
        hint: 'Pokaż w zestawieniu',
      },
    ];
  });

  protected readonly timeline = computed<ChartSpec | null>(() => {
    const report = this.report();
    if (!report?.kpis) {
      return null;
    }
    const series = report.series;
    return {
      type: 'timeline',
      labels: series.map((point) => bucketLabel(point, report.period.granularity)),
      series: [
        {
          label: 'Sprzedaż',
          values: series.map((p) => p.sales),
          token: '--sf-accent',
          shape: 'bar',
        },
        { label: 'Koszty', values: series.map((p) => p.costs), token: '--sf-text', shape: 'bar' },
        {
          label: 'Wynik przed wypłatami',
          values: series.map((p) => p.result),
          token: '--sf-info',
          shape: 'dashedLine',
        },
      ],
      ariaLabel: `Sprzedaż i koszty w czasie, ${periodLabel(report.period)}, w zł. Dane w tabeli poniżej.`,
    };
  });

  protected readonly channelsChart = computed<ChartSpec | null>(() => {
    const channels = this.report()?.channels ?? [];
    return channels.length === 0
      ? null
      : {
          type: 'horizontalBar',
          labels: channels.map((c) => CHANNEL_LABELS[c.channel]),
          series: [
            {
              label: 'Sprzedaż',
              values: channels.map((c) => c.amount),
              token: '--sf-accent',
              shape: 'bar',
            },
          ],
          ariaLabel: 'Sprzedaż według kanałów w zł. Wartości na liście obok.',
        };
  });

  protected readonly categoriesChart = computed<ChartSpec | null>(() => {
    const categories = this.report()?.categories ?? [];
    return categories.length === 0
      ? null
      : {
          type: 'horizontalBar',
          labels: categories.map((c) => CATEGORY_LABELS[c.category]),
          series: [
            {
              label: 'Koszty',
              values: categories.map((c) => c.amount),
              token: '--sf-text',
              shape: 'bar',
            },
          ],
          ariaLabel: 'Koszty według kategorii w zł. Wartości na liście obok.',
        };
  });

  protected categoryLink(category: CostCategory) {
    return this.dashboard.categoryLink(category);
  }

  protected onCard(key: CardView['key']): void {
    if (key === 'sales') {
      void this.dashboard.showAllComponents();
    } else if (key === 'orders') {
      void this.dashboard.showOrders();
    } else {
      return;
    }
    this.focusComponents();
  }

  protected onChannel(channel: SalesChannel): void {
    void this.dashboard.showChannel(channel);
    this.focusComponents();
  }

  private focusComponents(): void {
    this.components()?.focusHeading(!prefersReducedMotion());
  }
}
