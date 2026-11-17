# SF-12 — Import zamówień WooCommerce

| | |
|---|---|
| **Source** | [SF-12](https://standev.youtrack.cloud/issue/SF-12) |
| **Project** | SF |
| **Type** | Zadanie |
| **State** | Otwarta |
| **Priority** | Poważny |
| **Sprint** | |
| **Assignee** | standev |
| **Reporter** | standev |
| **Estimate** | |
| **Created** | 2026-11-02 20:10:53 |
| **Updated** | 2026-11-02 20:15:43 |
| **Fetched** | 2026-11-06 |

> **Uwagi do zgłoszenia (tylko sygnalizacja):**
> - „Zadanie 2”, „zadanie 13” i „Depends on: 2, 4, 10” to lokalne numery z breakdownu, czyli SF-2, SF-13 oraz SF-2, SF-4 i SF-10.
> - SF-2 (`docs/tasks/sf-2/01-weryfikacja.md`) nie jest zakończone: nie wykonano testów webhooków, kosza, usunięcia ani podpisu na rzeczywistym payloadzie; brak kopii testowej sklepu. Zgłoszenie każe uwzględnić „wnioski z zadania 2”.
> - SF-2 odnotowało włączone multi-currency w WooPayments, a zgłoszenie i PRD zakładają wyłącznie PLN.
> - Ryzyko high: przed merge wymagany jest przegląd przez człowieka.

---

## Context
Zamówienia ze sklepu docierają na bieżąco webhookami i mają zmieniać dane w ERP tylko według ustalonej własności pól. Webhooki zmieniają zamówienia, więc to obszar wysokiego ryzyka. PRD sekcja 5 „Zamówienie WooCommerce”, ZAM-02, 06, ZAM-08.

## Scope
- Endpoint webhooków z weryfikacją autentyczności; klucze tylko po stronie serwera.
- Identyfikacja według źródła i zewnętrznego ID z ograniczeniem unikalności.
- Własność pól: WooCommerce (ID, data, klient, wartość) tylko do odczytu w ERP; pola ERP nietknięte.
- Pomijanie danych starszych według daty modyfikacji i zamówień sprzed daty uruchomienia.
- Status ze sklepu; pending, failed i szkice ukryte domyślnie i dostępne filtrem.
- Jednorazowe przejście na „Anulowane”, usunięcie w sklepie, ostrzeżenie „Zamówienie aktywne w sklepie”, status przy imporcie.
- Audyt z autorem „Synchronizacja WooCommerce”, także dla zmian statusu wpływających na sprzedaż.
- Do rozstrzygnięcia w specyfikacji: wnioski z zadania 2, pola na oznaczenie usunięcia i informację o zwrocie w sklepie, ostrzeżenie przy przywróceniu w ERP zamówienia anulowanego w sklepie, równoczesny zapis użytkownika i synchronizacji.

## Acceptance criteria
- [ ] Wielokrotne dostarczenie tego samego ID daje jeden rekord.
- [ ] Synchronizacja nie zmienia pól ERP poza jednorazowym przejściem na „Anulowane”.
- [ ] Ponowne dostarczenie tego samego statusu nie zmienia statusu realizacji.
- [ ] Dostawa starsza od zapisanych danych niczego nie zmienia.
- [ ] Zamówienie z datą sprzed uruchomienia nie tworzy rekordu ani błędu.
- [ ] Przywrócenie w sklepie i anulowanie w ERP zamówienia aktywnego w sklepie pokazują ostrzeżenie i niczego nie zmieniają w sklepie.
- [ ] Nowe zamówienie ma status „W trakcie”, a zaimportowane jako anulowane — „Anulowane”.
- [ ] Integracja nie tworzy wpłat ani wypływów.
- [ ] Obniżenie wartości poniżej sumy obniżeń sprzedaży pokazuje ostrzeżenie.

## Out of scope
- Dzienne uzupełnianie i ponowienia (zadanie 13).
- Zapis czegokolwiek w sklepie.
- Waluty inne niż PLN.

## Metadata
- Breakdown ID: 12
- Risk: high — human review required before merge
- Depends on: 2, 4, 10
- PRD sections: 5 (Zamówienie WooCommerce), 6 (ZAM-02, 06, 08), 7, 10 (Integracje)
