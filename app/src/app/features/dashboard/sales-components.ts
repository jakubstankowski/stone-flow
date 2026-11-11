import { Component, ElementRef, computed, inject, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { formatDate, formatPln } from '../../shared/format/pl-format';
import { DataTable, DataTableColumn, SortState } from '../../shared/ui/data-table';
import { Field } from '../../shared/ui/field';
import { TableCell } from '../../shared/ui/table-cell';
import { DashboardService } from './dashboard.service';
import {
  CHANNEL_LABELS,
  COMPONENTS_BASIS_LABEL,
  COMPONENT_KINDS,
  COMPONENT_KIND_LABELS,
  SalesChannel,
  SalesComponentItem,
  SalesComponentKind,
} from './report-models';

export const COMPONENTS_HEADING_ID = 'zestawienie-skladnikow';

const COLUMNS: readonly DataTableColumn<SalesComponentItem>[] = [
  { key: 'date', header: 'Data', value: (row) => formatDate(row.date), sortable: true },
  { key: 'kind', header: 'Rodzaj', value: (row) => COMPONENT_KIND_LABELS[row.kind] },
  { key: 'order', header: 'Zamówienie' },
  { key: 'channel', header: 'Kanał', value: (row) => CHANNEL_LABELS[row.channel] },
  { key: 'amount', header: 'Kwota', value: (row) => formatPln(row.amount), align: 'end' },
];

/**
 * Zestawienie składników sprzedaży (SF-19): pozycje, filtry kanału i rodzaju jako aktywne chipy,
 * sortowanie po dacie i strona z API. „Suma zestawienia” obejmuje cały filtr i pochodzi z API (bez
 * filtrów równa karcie „Sprzedaż”).
 */
@Component({
  selector: 'app-sales-components',
  imports: [DataTable, Field, RouterLink, TableCell],
  templateUrl: './sales-components.html',
  host: { class: 'flex flex-col gap-4' },
})
export class SalesComponents {
  protected readonly dashboard = inject(DashboardService);

  private readonly heading = viewChild.required<ElementRef<HTMLElement>>('heading');

  protected readonly headingId = COMPONENTS_HEADING_ID;
  protected readonly columns = COLUMNS;
  protected readonly rowKey = (row: SalesComponentItem) => `${row.kind}:${row.id}`;
  protected readonly kinds = COMPONENT_KINDS;
  protected readonly kindLabels = COMPONENT_KIND_LABELS;
  protected readonly channelLabels = CHANNEL_LABELS;
  protected readonly basisLabel = COMPONENTS_BASIS_LABEL;
  protected readonly formatPln = formatPln;

  /** Kanały w kolejności z raportu (słownik Orders). */
  protected readonly channels = computed<readonly SalesChannel[]>(
    () => this.dashboard.report()?.channels.map((channel) => channel.channel) ?? [],
  );
  protected readonly items = computed(() => this.dashboard.componentsPage()?.items ?? []);
  protected readonly total = computed(() => this.dashboard.componentsPage()?.total ?? 0);
  protected readonly sum = computed(() => this.dashboard.componentsPage()?.sum ?? null);

  /** Przewija do nagłówka zestawienia i ustawia na nim fokus (przejścia z kart i kanałów). */
  focusHeading(smooth: boolean): void {
    const element = this.heading().nativeElement;
    if (typeof element.scrollIntoView === 'function') {
      element.scrollIntoView({ behavior: smooth ? 'smooth' : 'auto', block: 'start' });
    }
    element.focus();
  }

  protected onChannel(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    void this.dashboard.setChannel(value === '' ? null : (value as SalesChannel));
  }

  protected onKind(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    void this.dashboard.setKind(value === '' ? null : (value as SalesComponentKind));
  }

  protected onSort(sort: SortState): void {
    void this.dashboard.setComponentsSort(sort);
  }

  protected onPage(page: number): void {
    void this.dashboard.setComponentsPage(page);
  }
}
