import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CashChart } from '../../demo/cash-chart';
import {
  CURRENT_MONTH,
  EXPENSES,
  MONTHS,
  ORDERS,
  OrderStatus,
  PREVIOUS_MONTH,
} from '../../demo/demo-data';
import { PlDatePipe, PlMonthPipe, PlnAmountPipe } from '../../shared/format/pl-format-pipes';
import { StatusBadge, StatusTone } from '../../shared/ui/status-badge';

interface Kpi {
  readonly label: string;
  readonly value: number;
  readonly previous: number;
  readonly hint: string;
  /** Czy wzrost jest dobry (dla wydatków — nie). */
  readonly higherIsBetter: boolean;
}

export const STATUS_TONES: Record<OrderStatus, StatusTone> = {
  Opłacone: 'success',
  'Częściowo opłacone': 'warning',
  Nieopłacone: 'info',
  Anulowane: 'neutral',
};

/** TRYB DEMO: pulpit z danymi z `demo/demo-data.ts`. */
@Component({
  selector: 'app-dashboard-page',
  imports: [CashChart, PlnAmountPipe, PlMonthPipe, PlDatePipe, StatusBadge, RouterLink],
  templateUrl: './dashboard-page.html',
})
export class DashboardPage {
  protected readonly months = MONTHS;
  protected readonly month = CURRENT_MONTH.month;
  protected readonly statusTones = STATUS_TONES;
  protected readonly recentOrders = ORDERS.slice(0, 5);

  protected readonly kpis: readonly Kpi[] = [
    {
      label: 'Sprzedaż',
      value: CURRENT_MONTH.sales,
      previous: PREVIOUS_MONTH.sales,
      hint: 'wartość zamówień z datą w miesiącu',
      higherIsBetter: true,
    },
    {
      label: 'Wpływy',
      value: CURRENT_MONTH.inflows,
      previous: PREVIOUS_MONTH.inflows,
      hint: 'pieniądze faktycznie otrzymane',
      higherIsBetter: true,
    },
    {
      label: 'Wydatki',
      value: CURRENT_MONTH.expenses,
      previous: PREVIOUS_MONTH.expenses,
      hint: 'koszty z datą w miesiącu',
      higherIsBetter: false,
    },
    {
      label: 'Zmiana środków',
      value: CURRENT_MONTH.inflows - CURRENT_MONTH.expenses,
      previous: PREVIOUS_MONTH.inflows - PREVIOUS_MONTH.expenses,
      hint: 'wpływy minus wydatki',
      higherIsBetter: true,
    },
  ];

  protected readonly categories = (() => {
    const totals = new Map<string, number>();
    for (const expense of EXPENSES) {
      totals.set(expense.category, (totals.get(expense.category) ?? 0) + expense.amount);
    }
    const sorted = [...totals.entries()].sort((a, b) => b[1] - a[1]);
    const max = sorted[0]?.[1] ?? 1;
    return sorted.map(([name, amount]) => ({ name, amount, share: (amount / max) * 100 }));
  })();

  protected change(kpi: Kpi): { text: string; good: boolean } {
    const percent = ((kpi.value - kpi.previous) / Math.abs(kpi.previous)) * 100;
    const sign = percent >= 0 ? '+' : '−';
    return {
      text: `${sign}${Math.abs(percent).toFixed(1).replace('.', ',')}% m/m`,
      good: percent >= 0 === kpi.higherIsBetter,
    };
  }
}
