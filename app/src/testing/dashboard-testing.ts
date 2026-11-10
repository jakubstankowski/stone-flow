import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  CashFlowReport,
  ReportPeriod,
  SalesComponentItem,
  SalesComponentsPage,
  SalesReport,
} from '../app/features/dashboard/report-models';
import { OperationItem } from '../app/shared/dictionaries/operations';

/** Fikcyjne dane raportu sprzedaży do testów (bez danych identyfikujących). Tylko specyfikacje. */
export function reportPeriod(overrides: Partial<ReportPeriod> = {}): ReportPeriod {
  return {
    kind: 'currentMonth',
    from: '2026-11-01',
    to: '2026-11-30',
    effectiveFrom: '2026-11-01',
    launchDate: '2026-09-01',
    beforeLaunch: false,
    inProgress: true,
    granularity: 'day',
    ...overrides,
  };
}

export function salesReport(overrides: Partial<SalesReport> = {}): SalesReport {
  return {
    period: reportPeriod(),
    comparison: { from: '2026-10-01', to: '2026-10-31', available: true, reason: null },
    kpis: {
      sales: {
        value: '1200.00',
        comparison: { status: 'available', base: '800.00', difference: '400.00', percent: '50.0' },
      },
      costs: {
        value: '300.00',
        comparison: { status: 'noBasis', base: null, difference: null, percent: null },
      },
      result: {
        value: '900.00',
        comparison: { status: 'available', base: '950.00', difference: '-50.00', percent: '-5.3' },
      },
      orderCount: {
        value: 2,
        comparison: { status: 'available', base: 1, difference: 1, percent: '100.0' },
      },
    },
    series: [
      { from: '2026-11-01', to: '2026-11-01', sales: '1000.00', costs: '300.00', result: '700.00' },
      { from: '2026-11-02', to: '2026-11-02', sales: '200.00', costs: '0.00', result: '200.00' },
    ],
    channels: [
      { channel: 'shop', amount: '0.00' },
      { channel: 'olx', amount: '800.00' },
      { channel: 'acquaintance', amount: '0.00' },
      { channel: 'phone', amount: '400.00' },
      { channel: 'other', amount: '0.00' },
    ],
    categories: [
      { category: 'materialPurchase', amount: '0.00' },
      { category: 'cutting', amount: '300.00' },
      { category: 'hdsTransport', amount: '0.00' },
      { category: 'marketing', amount: '0.00' },
      { category: 'otherOperations', amount: '0.00' },
      { category: 'vatTax', amount: '0.00' },
      { category: 'ppeTax', amount: '0.00' },
    ],
    calculatedAtUtc: '2026-11-09T11:15:00+00:00',
    ...overrides,
  };
}

/** Raport okresu w całości przed uruchomieniem: bez kart i list (brak danych ≠ zero). */
export function beforeLaunchReport(): SalesReport {
  return salesReport({
    period: reportPeriod({
      kind: 'custom',
      from: '2026-08-01',
      to: '2026-08-31',
      effectiveFrom: '2026-09-01',
      beforeLaunch: true,
      inProgress: false,
    }),
    comparison: { from: '2026-07-01', to: '2026-07-31', available: false, reason: 'beforeLaunch' },
    kpis: null,
    series: [],
    channels: [],
    categories: [],
  });
}

/** Pozycja ostatnich operacji (kształt pozycji `GET /api/operations`). */
export function cashFlowOperation(overrides: Partial<OperationItem> = {}): OperationItem {
  return {
    kind: 'payment',
    id: 'p1',
    date: '2026-11-05',
    direction: 'inflow',
    amount: '1500.00',
    method: 'transfer',
    refundKind: null,
    description: 'ZAM/2026/11/0001',
    category: null,
    sourceType: 'order',
    sourceId: 'o1',
    sourceLabel: 'ZAM/2026/11/0001',
    ...overrides,
  };
}

/** Fikcyjny raport przepływów (SF-20): kwoty z API jako ciągi, bez liczenia w testach. */
export function cashFlowReport(overrides: Partial<CashFlowReport> = {}): CashFlowReport {
  return {
    period: reportPeriod({ effectiveFrom: '2026-11-03' }),
    comparison: { from: '2026-10-01', to: '2026-10-31', available: true, reason: null },
    kpis: {
      inflows: {
        value: '1500.00',
        comparison: { status: 'available', base: '1000.00', difference: '500.00', percent: '50.0' },
      },
      outflows: {
        value: '700.00',
        comparison: { status: 'noBasis', base: null, difference: null, percent: null },
      },
      change: {
        value: '800.00',
        comparison: {
          status: 'available',
          base: '1000.00',
          difference: '-200.00',
          percent: '-20.0',
        },
      },
    },
    series: [
      {
        from: '2026-11-03',
        to: '2026-11-03',
        inflows: '1500.00',
        outflows: '400.00',
        change: '1100.00',
      },
      {
        from: '2026-11-04',
        to: '2026-11-04',
        inflows: '0.00',
        outflows: '300.00',
        change: '-300.00',
      },
    ],
    outflowsByKind: [
      { kind: 'expense', amount: '300.00' },
      { kind: 'withdrawal', amount: '250.00' },
      { kind: 'refund', amount: '150.00' },
    ],
    recentOperations: [
      cashFlowOperation(),
      cashFlowOperation({
        kind: 'expense',
        id: 'e1',
        date: '2026-11-04',
        direction: 'outflow',
        amount: '-50.00',
        method: null,
        description: 'Zwrot za cięcie',
        category: 'cutting',
        sourceType: 'expense',
        sourceId: 'e1',
        sourceLabel: 'Zwrot za cięcie',
      }),
    ],
    calculatedAtUtc: '2026-11-09T11:15:00+00:00',
    ...overrides,
  };
}

/** Raport przepływów okresu w całości przed uruchomieniem: bez kart i list. */
export function beforeLaunchCashFlowReport(): CashFlowReport {
  return cashFlowReport({
    period: reportPeriod({
      kind: 'custom',
      from: '2026-08-01',
      to: '2026-08-31',
      effectiveFrom: '2026-09-01',
      beforeLaunch: true,
      inProgress: false,
    }),
    comparison: { from: '2026-07-01', to: '2026-07-31', available: false, reason: 'beforeLaunch' },
    kpis: null,
    series: [],
    outflowsByKind: [],
    recentOperations: [],
  });
}

export function salesComponentItem(
  overrides: Partial<SalesComponentItem> = {},
): SalesComponentItem {
  return {
    kind: 'order',
    id: 'c1',
    date: '2026-11-04',
    amount: '1000.00',
    orderId: 'o1',
    orderNumber: 'ZAM/2026/11/0001',
    channel: 'olx',
    ...overrides,
  };
}

export function salesComponentsPage(
  overrides: Partial<SalesComponentsPage> = {},
): SalesComponentsPage {
  const items = overrides.items ?? [];
  return {
    period: reportPeriod(),
    items,
    page: 1,
    pageSize: 25,
    total: items.length,
    sum: '1200.00',
    ...overrides,
  };
}

/**
 * Odrzuca otwarte żądania raportów Pulpitu: Pulpit wczytuje raport aktywnego widoku przy każdym wejściu,
 * a testy powłoki i tras, które go nie dotyczą, nawigują na `/pulpit`.
 */
export function discardReportRequests(): void {
  TestBed.inject(HttpTestingController).match((request) => request.url.startsWith('/api/reports/'));
}
