import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ExpensesLink } from './dashboard.service';
import { CardComparisonText } from './report-text';

export type KpiIcon = 'sales' | 'costs' | 'result' | 'orders';

/** Akcja karty: link (Koszty), przycisk (Sprzedaż, Liczba zamówień) albo brak (Wynik). */
export type KpiAction =
  { readonly kind: 'link'; readonly target: ExpensesLink } | { readonly kind: 'button' } | null;

/**
 * Karta KPI (design guide §4): ikona na stonowanym tle, etykieta, duża wartość, opis podstawy i
 * porównanie w kolorze neutralnym (kierunek strzałką i słowem, bez zieleni i czerwieni). Wartość i
 * porównanie przychodzą już sformatowane z danych API.
 */
@Component({
  selector: 'app-kpi-card',
  imports: [NgTemplateOutlet, RouterLink],
  templateUrl: './kpi-card.html',
  host: { class: 'block' },
})
export class KpiCard {
  readonly label = input.required<string>();
  readonly value = input.required<string>();
  readonly description = input.required<string>();
  readonly comparison = input.required<CardComparisonText>();
  readonly icon = input.required<KpiIcon>();
  readonly action = input<KpiAction>(null);
  readonly hint = input<string | null>(null);

  readonly activate = output<void>();

  protected readonly link = computed(() => {
    const action = this.action();
    return action?.kind === 'link' ? action.target : null;
  });

  protected readonly button = computed(() => this.action()?.kind === 'button');
}
