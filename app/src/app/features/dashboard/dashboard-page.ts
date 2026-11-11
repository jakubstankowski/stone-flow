import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { formatDateTime } from '../../shared/format/pl-format';
import { EmptyState } from '../../shared/ui/empty-state';
import { IncompleteDataNotice } from '../integration/incomplete-data-notice';
import { DashboardService, DashboardView } from './dashboard.service';
import { PeriodPicker } from './period-picker';
import { PeriodSelection } from './report-models';
import { basisLabel, comparisonDescription, launchNotice, periodLabel } from './report-text';
import { SalesView } from './sales-view';

/**
 * Ekran `/pulpit` (SF-19): przełącznik widoku [Sprzedaż / Przepływy pieniężne], wybór okresu,
 * aktualność danych i widok „Sprzedaż”. Każde wejście wczytuje raport ponownie z zachowanym
 * stanem (`DashboardService.enter`); bez odpytywania w tle. „Przepływy pieniężne” to zaślepka do SF-20.
 */
@Component({
  selector: 'app-dashboard-page',
  imports: [EmptyState, IncompleteDataNotice, PeriodPicker, RouterLink, SalesView],
  templateUrl: './dashboard-page.html',
})
export class DashboardPage {
  protected readonly dashboard = inject(DashboardService);

  protected readonly header = computed(() => {
    const report = this.dashboard.report();
    if (!report || this.dashboard.reportLoading()) {
      return null;
    }
    const { period, comparison } = report;
    return {
      period: periodLabel(period),
      launch: launchNotice(period),
      comparison: period.beforeLaunch ? null : comparisonDescription(period, comparison),
      calculatedAt: `Stan na ${formatDateTime(report.calculatedAtUtc)}`,
      basis: basisLabel(period.launchDate),
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
