import { formatDate, formatDateTime } from '../../shared/format/pl-format';
import { OrderAnonymization } from './order-models';

/**
 * Opis stanu akcji „Anonimizuj dane klienta” (SF-22) w bloku „Dane osobowe klienta”. Daty pochodzą z
 * API i są tylko formatowane; brak daty daje „Brak danych”.
 */
export function anonymizationText(anonymization: OrderAnonymization): string {
  switch (anonymization.status) {
    case 'available':
      return (
        `Okres retencji upłynął ${formatDate(anonymization.availableFrom)}. Anonimizacja usunie ` +
        'dane klienta, uwagi, komentarz i powody zwrotów i korekt.'
      );
    case 'retentionPending':
      return (
        `Dostępne po upływie okresu retencji, od ${formatDate(anonymization.availableFrom)}. ` +
        `Okres liczy się od ${formatDate(anonymization.retentionStartDate)} — najpóźniejszej z dat ` +
        'zamówienia, jego wpisów i ostatniej zmiany.'
      );
    case 'disabled':
      return 'Funkcja wyłączona do decyzji o okresie retencji.';
    case 'anonymized':
      return (
        `Dane klienta zanonimizowano ${formatDateTime(anonymization.anonymizedAtUtc)} ` +
        '(Europe/Warsaw). Operacji nie można cofnąć.'
      );
  }
}
