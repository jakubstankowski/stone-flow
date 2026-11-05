import { Component, input, output } from '@angular/core';
import { formatDate, formatPln } from '../../shared/format/pl-format';
import { Button } from '../../shared/ui/button';
import { OrderDetails, SalesCorrectionEntry } from './order-models';

/**
 * Sekcja „Korekty sprzedaży” (SF-10): opis, sumy obniżeń i ostrzeżenie o nadwyżce z backendu,
 * tabela korekt z akcjami „Popraw” i „Usuń” oraz akcja „Dodaj korektę sprzedaży”. Komponent
 * tylko prezentuje: formularz i komunikaty są projekcją (`sectionForm`, `sectionMessages`)
 * z `app-order-payments`, który trzyma wspólny stan wpisów (jeden formularz naraz, operacje
 * i wykluczenie z edycją zamówienia). Kwot nie liczy: pokazuje wartości z API.
 */
@Component({
  selector: 'app-sales-corrections',
  imports: [Button],
  templateUrl: './sales-corrections.html',
})
export class SalesCorrections {
  readonly order = input.required<OrderDetails>();
  /** Edycja zamówienia jest otwarta (wtedy sekcja wyjaśnia, dlaczego akcje są wyłączone). */
  readonly locked = input(false);
  readonly actionsDisabled = input(false);
  readonly deletingId = input<string | null>(null);

  readonly add = output<void>();
  readonly edit = output<SalesCorrectionEntry>();
  readonly remove = output<SalesCorrectionEntry>();

  protected readonly formatDate = formatDate;
  protected readonly formatPln = formatPln;
}
