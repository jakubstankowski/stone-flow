import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  DIRECTION_LABELS,
  NEGATIVE_AMOUNT_LABEL,
  OperationItem,
  isNegativeAmount,
  kindLabel,
  sourceLink,
} from '../../shared/dictionaries/operations';
import { formatDate, formatPln } from '../../shared/format/pl-format';
import { DataTable, DataTableColumn } from '../../shared/ui/data-table';
import { ErrorState } from '../../shared/ui/error-state';
import { LoadingState } from '../../shared/ui/loading-state';
import { TableCell } from '../../shared/ui/table-cell';
import { ChartCanvas, ChartSpec } from './chart-canvas';
import { CashFlowLinks, DashboardService } from './dashboard.service';
import { KpiAction, KpiCard } from './kpi-card';
import {
  CASH_FLOW_KPI_DESCRIPTIONS,
  OUTFLOWS_BREAKDOWN_LABEL,
  OUTFLOW_KIND_LABELS,
  OutflowKind,
} from './report-models';
import {
  CardComparisonText,
  amountComparisonText,
  baseLabel,
  beforeLaunchMessage,
  bucketLabel,
  bucketRange,
  periodLabel,
} from './report-text';

type CardKey = 'inflows' | 'outflows' | 'change';

interface CardView {
  readonly key: CardKey;
  readonly label: string;
  readonly value: string;
  readonly description: string;
  readonly comparison: CardComparisonText;
  readonly action: KpiAction;
  readonly hint: string;
}

const CARDS: readonly { readonly key: CardKey; readonly label: string; readonly hint: string }[] = [
  { key: 'inflows', label: 'Wpływy', hint: 'Pokaż wpłaty' },
  { key: 'outflows', label: 'Wypływy', hint: 'Pokaż wypływy' },
  { key: 'change', label: 'Zmiana środków', hint: 'Pokaż operacje' },
];

/** Kolumny jak na liście „Operacje” bez „Metody” (metoda jest na pełnej liście). */
const COLUMNS: readonly DataTableColumn<OperationItem>[] = [
  { key: 'date', header: 'Data', value: (row) => formatDate(row.date) },
  { key: 'kind', header: 'Rodzaj', value: (row) => kindLabel(row) },
  { key: 'description', header: 'Opis' },
  { key: 'direction', header: 'Kierunek', value: (row) => DIRECTION_LABELS[row.direction] },
  { key: 'amount', header: 'Kwota', align: 'end' },
];

/**
 * Widok „Przepływy pieniężne” Pulpitu (SF-20): trzy karty KPI z porównaniem prowadzące do listy
 * „Operacje”, wykres wpływów i wypływów w czasie z tabelą danych, podział wypływów według rodzaju
 * z listą i ostatnie operacje. Wszystkie wartości są ciągami z API formatowanymi przez
 * `shared/format`; „Razem” w tabeli danych to wartości kart, nie suma w UI. Bez salda.
 */
@Component({
  selector: 'app-cash-flow-view',
  imports: [ChartCanvas, DataTable, ErrorState, KpiCard, LoadingState, RouterLink, TableCell],
  templateUrl: './cash-flow-view.html',
  host: { class: 'flex flex-col gap-6' },
})
export class CashFlowView {
  protected readonly dashboard = inject(DashboardService);

  protected readonly formatPln = formatPln;
  protected readonly bucketRange = bucketRange;
  protected readonly sourceLink = sourceLink;
  protected readonly isNegative = isNegativeAmount;
  protected readonly negativeLabel = NEGATIVE_AMOUNT_LABEL;
  protected readonly outflowKindLabels = OUTFLOW_KIND_LABELS;
  protected readonly breakdownLabel = OUTFLOWS_BREAKDOWN_LABEL;
  protected readonly columns = COLUMNS;
  protected readonly rowKey = (row: OperationItem) => `${row.kind}:${row.id}`;
  protected readonly tableOpen = signal(false);
  protected readonly tableId = 'pulpit-przeplywy-dane-wykresu';

  protected readonly report = this.dashboard.cashFlowReport;
  protected readonly links = this.dashboard.cashFlowLinks;

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
    const links: CashFlowLinks | null = this.links();
    return CARDS.map(({ key, label, hint }) => ({
      key,
      label,
      hint,
      value: formatPln(kpis[key].value),
      description: CASH_FLOW_KPI_DESCRIPTIONS[key],
      comparison: amountComparisonText(kpis[key].comparison, base),
      action: links ? { kind: 'link', target: links[key] } : null,
    }));
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
          label: 'Wpływy',
          values: series.map((p) => p.inflows),
          token: '--sf-accent',
          shape: 'bar',
        },
        {
          label: 'Wypływy',
          values: series.map((p) => p.outflows),
          token: '--sf-text',
          shape: 'bar',
        },
      ],
      ariaLabel: `Wpływy i wypływy w czasie, ${periodLabel(report.period)}, w zł. Dane w tabeli poniżej.`,
    };
  });

  protected readonly outflowsChart = computed<ChartSpec | null>(() => {
    const items = this.report()?.outflowsByKind ?? [];
    return items.length === 0
      ? null
      : {
          type: 'horizontalBar',
          labels: items.map((item) => OUTFLOW_KIND_LABELS[item.kind]),
          series: [
            {
              label: 'Wypływy',
              values: items.map((item) => item.amount),
              token: '--sf-text',
              shape: 'bar',
            },
          ],
          ariaLabel: 'Wypływy według rodzaju w zł. Wartości są też na liście przy wykresie.',
        };
  });

  protected outflowKindLink(kind: OutflowKind) {
    return this.dashboard.outflowKindLink(kind);
  }
}
