import { Component, computed, inject } from '@angular/core';
import { formatDateTime } from '../../shared/format/pl-format';
import { IncompleteDataNotice } from '../integration/incomplete-data-notice';
import { CashFlowView } from './cash-flow-view';
import { DashboardService, DashboardView } from './dashboard.service';
import { PeriodPicker } from './period-picker';
import { PeriodSelection } from './report-models';
import {
  basisLabel,
  cashFlowBasisLabel,
  comparisonDescription,
  launchNotice,
  periodLabel,
} from './report-text';
import { SalesView } from './sales-view';

/**
 * Ekran `/pulpit`: przełącznik widoku [Sprzedaż / Przepływy pieniężne], wspólny wybór okresu i
 * nagłówek aktualności (okres, porównanie, „Stan na”, podstawa kwot aktywnego widoku) oraz widok
 * „Sprzedaż” (SF-19) albo „Przepływy pieniężne” (SF-20). Każde wejście wczytuje aktywny widok
 * ponownie z zachowanym stanem (`DashboardService.enter`); bez odpytywania w tle.
 */
@Component({
  selector: 'app-dashboard-page',
  imports: [CashFlowView, IncompleteDataNotice, PeriodPicker, SalesView],
  templateUrl: './dashboard-page.html',
})
export class DashboardPage {
  protected readonly dashboard = inject(DashboardService);

  protected readonly header = computed(() => {
    const report = this.dashboard.activeReport();
    if (!report) {
      return null;
    }
    const { period, comparison } = report;
    return {
      period: periodLabel(period),
      launch: launchNotice(period),
      comparison: period.beforeLaunch ? null : comparisonDescription(period, comparison),
      calculatedAt: `Stan na ${formatDateTime(report.calculatedAtUtc)}`,
      basis:
        this.dashboard.view() === 'sales'
          ? basisLabel(period.launchDate)
          : cashFlowBasisLabel(period.launchDate),
    };
  });

  constructor() {
    this.dashboard.enter();
  }

  protected onView(view: DashboardView): void {
    this.dashboard.setView(view);
  }

  protected onSelection(change: Partial<PeriodSelection>): void {
    this.dashboard.setSelection(change);
  }
}
