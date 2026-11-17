# Spec review — SF-17

## Summary

- Overall assessment: PASS WITH ISSUES
- Justification: Wersja 2 usuwa jedyny problem krytyczny z v1. Lista „Operacje” ma teraz kolumnę „Kierunek”, a kwota jest pokazywana jako zapisana, bez zmiany znaku, więc opłacony wydatek −50 zł widać jako `−50,00 zł` z dopiskiem „Korekta / zwrot od dostawcy”. To zgadza się z design guide §6 (formularz wydatku i listy) i z kryterium zgłoszenia. Uwagi niekrytyczne 1–9 z v1 są rozwiązane: metoda nullowalna, ujemne sumy, semantyka `amount`, reguły w `Reporting.Domain`, wspólne typy dla SF-20, limit w SQL, odcięcie daty uruchomienia w źródłach, testy URU-01 dla wpłat i nazwa `totals.change`. Zostały drobne niejasności (zachowanie strony poza zakresem, wartość `effectiveFrom` dla okresu w całości przed uruchomieniem, treść kolumny „Opis” dla wypłaty). Planowanie nie musi przez nie zgadywać zachowania biznesowego.

## Findings

### Critical Issues

None.

### Weryfikacja ustaleń z przeglądu v1

| Ustalenie v1 | Stan w v2 |
|---|---|
| Krytyczne 1 — znak wydatku ujemnego, brak kolumny kierunku | Rozwiązane. Behavior pkt 13 i UI: kolumna „Kierunek” (tekst), kwota zapisana `−50,00 zł` z dopiskiem; kryterium akceptacji sprawdza kierunek, kwotę i sumę. |
| NC 1 — metoda wydatku może być pusta | Rozwiązane: `Method` jako `string?`, `method` w JSON nullowalne, w UI „Nie podano”, przypadek brzegowy i test integracyjny. |
| NC 2 — ujemna suma wypływów | Rozwiązane: pkt 12 (pokazana ze znakiem minus, bez wartości bezwzględnej), przypadek brzegowy. |
| NC 3 — semantyka `amount` | Rozwiązane: kwota zapisana, kierunek niesie tylko `direction`. |
| NC 4 — miejsce reguły sum i daty uruchomienia | Rozwiązane: `CashFlowPolicy`, `CashFlowTotals` i `CashMovement` w `Reporting.Domain/CashFlow/`; `CashOperationKind` w `Shared.Kernel` (zgodnie z `ProjectReferenceTests`: Domain → tylko Kernel). |
| NC 5 — uzgodnienie z SF-20 | Rozwiązane: SF-20 ma użyć tych samych `ICashOperationSource`, `CashFlowPolicy` i `CashFlowTotals`. Limit 366 dni usunięto, więc nie koliduje z własnym zakresem widoku przepływów. |
| NC 6 — bezpiecznik po materializacji | Rozwiązane: `limit` w `ListAsync` (SQL `TOP`), wywołania z `10 001 − wczytane`. |
| NC 7 — gdzie odcinać datę uruchomienia | Rozwiązane: `EffectiveFrom` w domenie, przekazany do źródeł, które filtrują w SQL. Przy efektywnym początku > `to` źródła nie są odpytywane. |
| NC 8 — testy URU-01 dla wpłat | Rozwiązane: wpłata z dnia uruchomienia do zamówienia sprzed uruchomienia i wpłata z dnia poprzedniego są w Edge Cases, kryteriach i Verification. |
| NC 9 — nazwa `totals.net` | Rozwiązane: `totals.change`. |
| NC 10 — stany UI szczegółów i formularza | Rozwiązane częściowo: wczytywanie, błąd odczytu z „Spróbuj ponownie” i blokada przycisków w trakcie zapisu. Komunikat błędu sieci przy zapisie (poza `409`) nie jest opisany, zob. NC 3 niżej. |
| NC 11 — `page` poza zakresem | Nierozwiązane, zob. NC 1 niżej. |
| Niejasność — 366 dni włącznie czy różnica | Nieaktualne: limit usunięto. |
| Niejasność — `effectiveFrom` > `to` | Rozwiązane częściowo: zachowanie opisane (pusta lista, sumy 0, brak `400`). Wartość pola `effectiveFrom` w odpowiedzi nie jest podana, zob. niejasności niżej. |
| Niejasność — `sourceLabel` dla wydatku i wypłaty | Rozwiązane w Data / API → Format danych. Pojawiła się nowa niejasność co do kolumny „Opis”, zob. niżej. |
| Niejasność — `PUT` bez zmian a `version` / `UpdatedAtUtc` | Nierozwiązane, zob. NC 2 niżej. |

### Non-Critical Issues

1. **Strona poza zakresem (Behavior pkt 11; Data / API → Walidacja).** Nadal nie wiadomo, co zwraca `GET /api/operations` dla `page` większej niż liczba stron (pusta strona z sumami czy `400`) ani jaka jest dolna granica `page` (≥ 1). Plan powinien przyjąć wzór istniejących list (zamówienia, wydatki); warto to zapisać.
2. **`PUT` wypłaty bez zmienionych pól (Behavior pkt 5).** Specyfikacja mówi, że taki zapis nie tworzy audytu, ale nie mówi, czy zmienia `version` i `UpdatedAtUtc`. Należy przyjąć zachowanie `UpdateExpense` z SF-15.
3. **Błąd sieci lub `5xx` przy zapisie wypłaty i usuwaniu (UI → Formularz wypłaty, Szczegóły wypłaty).** Opisano `409`, `404` i błąd odczytu. Komunikat dla innego błędu zapisu i usunięcia (np. ogólny błąd z możliwością ponowienia przy tym samym `Idempotency-Key`) nie jest opisany. Wzorem jest `ExpenseForm`.
4. **Wpłaty do zamówień pending, failed i szkiców (Edge Cases).** Z braku filtra statusu wynika, że są na liście (to rzeczywiste pieniądze, tak jak przy zamówieniach anulowanych), ale specyfikacja tego nie mówi. Ta sama uwaga była w v1 (Assumptions Detected).
5. **Mapowanie kodów API na `CashOperationKind`.** Kody `payment`, `refund`, `expense`, `withdrawal` (pkt 9) i wartości `Payment`, `Refund`, `PaidExpense`, `OwnerWithdrawal` odpowiadają sobie tylko domyślnie. Jedna linia w Data / API usunęłaby wątpliwość.
6. **Wyczerpanie limitu przy kolejnym źródle (Types crossing a boundary).** Jeśli pierwsze źródło zwróci 10 001 wierszy, `limit` dla drugiego wynosi 0. Opis „gdy łącznie wczytano więcej niż 10 000” obejmuje ten przypadek, ale plan powinien pominąć wywołanie z `limit` ≤ 0 zamiast wysyłać `TOP 0`. Szczegół implementacyjny.

### Unclear or Ambiguous Sections

- **Behavior pkt 9 vs Data / API → Format danych (kolumna „Opis”).** Tabela pkt 9 mówi, że opis wypłaty to osoba, a opis wydatku to „opis wydatku i kategoria”. Pkt 13 mówi „Opis — link do źródła”, a `sourceLabel` (tekst linku) dla wypłaty to „Wypłata właściciela”, czyli to samo co kolumna „Rodzaj”. Nie wiadomo, czy w kolumnie „Opis” stoi `description` jako link, czy `sourceLabel`, i gdzie widać kategorię. Wybór nie wpływa na kwoty ani sumy, ale UI wymaga jednej reguły.
- **Edge Cases → okres całkowicie przed datą uruchomienia.** Nie podano, jaką wartość ma wtedy `effectiveFrom` w odpowiedzi. Wynik `CashFlowPolicy.EffectiveFrom` dałby datę uruchomienia późniejszą niż `to`. Warto to potwierdzić, bo UI używa tego pola do informacji pod filtrem.
- **Behavior pkt 13 → Rodzaj dla zwrotu.** „Zwrot sprzedaży” / „Zwrot pieniędzy” zależy od `refundKind`. Domyślnie wynika to z JSON, ale filtr rodzaju ma jedną pozycję „Zwrot” (design guide §6: „wpłata, zwrot, opłacony wydatek, wypłata właściciela”). Etykieta pozycji filtra nie jest podana.

### Invented or Unsupported Requirements

None. Wiersz sum, bezpiecznik 10 000, data wypłaty ≤ dziś, indeksy i typy wspólne z SF-20 są w Assumptions albo wynikają z FIN-05, D-08 i odpowiedzi właściciela z 09.11.2026.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Data / API → Types crossing a boundary (łączenie, sortowanie i stronicowanie w pamięci Reporting) | `conventions.md` → Data access („Lists filter, sort and paginate in the database”) | Jawne odstępstwo uzasadnione skalą (D-08) i ograniczone bezpiecznikiem w SQL. Akceptowalne, nie blokuje. |

Konflikty z design guide §6 opisane w v1 są usunięte. PRD §8 (podpowiedź netto ÷ 1,23) pominięte zgodnie z zakresem, bo zmianę wprowadzono na innej gałęzi.

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | OK | Wypłata jest tylko wypływem, nie trafia do kosztów, sprzedaży ani listy wydatków; lista bez korekt sprzedaży, nieopłaconych wydatków i wartości zamówień |
| 2. Backend decimal money | OK | `decimal(18,2)`, sumy w `CashFlowTotals` w backendzie, kwoty w JSON jako ciągi, frontend tylko formatuje |
| 3. API-enforced access | OK | Wszystkie endpointy z sesją, zapisy z CSRF, testy 401/CSRF w Verification i kryteriach |
| 4. Retries never duplicate | OK | `Idempotency-Key`, unikalny filtrowany indeks `CreationRequestId`, hash treści, `409 idempotency_key_reused` |
| 5. Financial audit trail | OK | Create, update i delete w tej samej jednostce pracy; usunięcie zapisuje stare wartości; `person` jako dane osobowe; data biznesowa = data wypłaty (conventions) |
| 6. Secrets stay server-side | Not applicable | Brak nowych sekretów |
| 7. Module and layer boundaries | OK | Kontrakt w `Shared.Application`, słownik w `Shared.Kernel`, reguły w `Reporting.Domain` bez I/O, implementacje w Infrastructure modułów-właścicieli, wywołania sekwencyjne na `ModuleConnection`, testy architektury bez wyjątków |
| 8. No identifying data in docs | OK | Fikcyjne przykłady; `sourceLabel` i etykieta audytu bez danych osobowych |

## Risk

- Recorded: high
- Assessed: high. Zadanie wprowadza nowy rodzaj wypływu, listę uzgodnienia przepływów (FIN-05) i reguły przełomu uruchomienia. Przed merge wymagany jest przegląd przez człowieka.

## Assumptions Detected

| Assumption | Stated in spec |
|---|---|
| Operacje sprzed daty uruchomienia ukryte, informacja o efektywnym początku (odpowiedź 1) | Tak |
| Osoba jako tekst z podpowiedziami, bez słownika (odpowiedź 2) | Tak |
| Kwota > 0, data ≤ dziś, metody przelew/BLIK/gotówka (odpowiedź 3) | Tak |
| Wypłata bez komentarza i referencji dokumentu | Tak |
| Brak osobnej pozycji nawigacji dla wypłat | Tak |
| Wiersz sum w zakresie SF-17 | Tak |
| Domyślny okres = bieżący miesiąc, bez górnego limitu | Tak |
| Łączenie w pamięci, bezpiecznik 10 000 w SQL | Tak |
| Prezentacja: kolumna „Kierunek” i kwota zapisana bez zmiany znaku | Tak |
| `CashOperationKind` w `Shared.Kernel` | Tak |
| Metoda „Pobranie” pokazywana dla wpłat | Tak |
| Wypłata z datą sprzed uruchomienia dozwolona | Tak |
| SF-20 użyje tych samych źródeł i reguł | Tak (Types crossing a boundary) |
| Wpłaty do zamówień anulowanych na liście | Tak (Edge Cases) |
| Wpłaty do zamówień pending/failed/szkiców na liście | Nie, wynika z braku filtra statusu (NC 4) |
| `page` poza zakresem i `PUT` bez zmian działają jak w istniejących modułach | Nie, niejawne (NC 1, NC 2) |
| Mapowanie kodów API na `CashOperationKind` | Nie, niejawne (NC 5) |

## Recommendation

- Proceed as-is. Uwagi niekrytyczne i niejasności (kolumna „Opis”, strona poza zakresem, `PUT` bez zmian, błąd zapisu, wartość `effectiveFrom`) plan może rozstrzygnąć według istniejących wzorów SF-15 i list zamówień, a wybór zapisać w `04-implementation-plan.md`.
