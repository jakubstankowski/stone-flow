import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  CashFlowReport,
  ComponentsQuery,
  PeriodSelection,
  SalesComponentsPage,
  SalesReport,
} from './report-models';

/**
 * Endpointy raportów Pulpitu (Reporting): `GET /api/reports/sales` i
 * `GET /api/reports/sales/components` (SF-19) oraz `GET /api/reports/cash-flow` (SF-20). Wszystkie
 * dostają ten sam wybór okresu. Błędy jako `HttpErrorResponse`.
 */
@Injectable({ providedIn: 'root' })
export class ReportsApi {
  private readonly http = inject(HttpClient);

  salesReport(selection: PeriodSelection): Promise<SalesReport> {
    return firstValueFrom(
      this.http.get<SalesReport>('/api/reports/sales', { params: periodParams(selection) }),
    );
  }

  cashFlowReport(selection: PeriodSelection): Promise<CashFlowReport> {
    return firstValueFrom(
      this.http.get<CashFlowReport>('/api/reports/cash-flow', { params: periodParams(selection) }),
    );
  }

  salesComponents(
    selection: PeriodSelection,
    query: ComponentsQuery,
  ): Promise<SalesComponentsPage> {
    let params = periodParams(selection)
      .set('direction', query.direction)
      .set('page', query.page)
      .set('pageSize', query.pageSize);
    if (query.channel) {
      params = params.set('channel', query.channel);
    }
    if (query.kind) {
      params = params.set('kind', query.kind);
    }
    return firstValueFrom(
      this.http.get<SalesComponentsPage>('/api/reports/sales/components', { params }),
    );
  }
}

/** Parametry okresu: tylko te potrzebne dla rodzaju (`year` przy roku, daty przy własnym zakresie). */
function periodParams(selection: PeriodSelection): HttpParams {
  let params = new HttpParams().set('period', selection.kind);
  if (selection.kind === 'year') {
    params = params.set('year', selection.year);
  }
  if (selection.kind === 'custom') {
    params = params.set('from', selection.from).set('to', selection.to);
  }
  return params;
}
