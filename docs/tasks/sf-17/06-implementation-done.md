# Implementation done — SF-17

## Summary

- Wypłaty właścicieli (moduł Expenses): encja, tabela `expenses.OwnerWithdrawals`, API `/api/owner-withdrawals` (podpowiedzi osób, szczegóły, dodanie z `Idempotency-Key`, poprawa i usunięcie z wersją), audyt każdej zmiany.
- Lista „Operacje” (moduł Reporting): `GET /api/operations` z filtrem okresu i rodzaju, sumami, stroną i bezpiecznikiem `422`; reguły `CashFlowPolicy` i `CashFlowTotals` w `Reporting.Domain`; źródła `ICashOperationSource` w Orders i Expenses.
- Frontend `/operacje`, `/operacje/wyplaty/nowa`, `/operacje/wyplaty/:id`.
- Specyfikacja: v1 FAIL, v2 PASS WITH ISSUES. Przegląd kodu: PASS WITH ISSUES, bez uwag krytycznych. Poprawiono uwagi 1–3: sumy nad tabelą, sumy nie znikają przy zmianie strony, `description` może być `null`.

## Tests

| Test | Result |
|---|---|
| Reporting.Tests | 72/72 |
| Expenses.Tests | 102/102 |
| Architecture.Tests | 56/56 |
| OwnerWithdrawalsApiTests + OperationsApiTests (LocalDB) | 38/38 |
| ExpensesApiTests, PaymentsApiTests, EndpointSecurityTests | 31/31, 31/31, 9/9 |
| Frontend: typecheck, `ng test` (62 pliki), lint, format, build | Passed |
| `dotnet build -c Release -m:1`, `dotnet format --verify-no-changes` | Passed |
| Pełny zestaw integracyjny, `test-migrate.sh` | Not run (CI) |

## Deviations

- `sourceLabel` wypłaty to osoba (decyzja 7 planu), a nie „Wypłata właściciela” jak w `02-spec.md`.
- Znak minus `-` (U+002D) z `formatPln`, jak na liście wydatków, a nie U+2212.
- Migracje z BOM, jak istniejące.

## Open Items

- **Human review required before merge** (ryzyko high).
- Merge `main` (SF-16): migracja `20261109064821_OwnerWithdrawals` wygenerowana ponownie przez `dotnet ef` na snapshocie z SF-16 (Up/Down bez zmian, Designer i snapshot z pełnym modelem); `has-pending-model-changes` bez zmian. `RecurringExpensesEndpoints` korzysta z `EndpointSupport`.
- Brak testu integracyjnego: wydatek opłacony przed datą uruchomienia znika z listy. Pokrywa to test jednostkowy wspólnego filtra.

## Scenariusze testów ręcznych (UI)

Warunki: zalogowany właściciel. Data uruchomienia w Development: 01.01.2026. Dzisiaj: 09.11.2026.

| # | Kroki | Oczekiwany wynik |
|---|---|---|
| 1 | Menu **Operacje** | Lista za bieżący miesiąc (chip „Okres: 01.11.2026 – 30.11.2026”), sumy „Wpływy / Wypływy / Zmiana środków” nad tabelą, przycisk **Dodaj wypłatę**. |
| 2 | **Dodaj wypłatę**: osoba `Właściciel A`, kwota `2000`, data dziś, metoda Przelew → zapisz | Szczegóły wypłaty. Na liście: rodzaj „Wypłata właściciela”, kierunek „Wypływ”, kwota `2 000,00 zł`; „Wypływy” rosną o 2000 zł. |
| 3 | Dodaj drugą wypłatę: zacznij wpisywać osobę | Pole podpowiada „Właściciel A”. |
| 4 | Formularz: kwota `0`, `-5`, `1,234`; data jutro; bez metody | Błędy przy polach, zapis zablokowany. |
| 5 | Szczegóły wypłaty → **Edytuj**, kwota `2500` → zapisz | Nowa kwota na liście, suma wypływów się zmienia. |
| 6 | Szczegóły → **Usuń** → dialog „Usunąć wypłatę?” → **Usuń** | Powrót do listy, wypłaty nie ma. |
| 7 | Zamówienia: dodaj zamówienie z wpłatą 1000 zł z dzisiejszą datą i zwrotem pieniędzy 100 zł | Na liście „Wpłata” (Wpływ, 1 000,00 zł) i „Zwrot pieniędzy” (Wypływ, 100,00 zł); opis to numer zamówienia z linkiem do zamówienia. |
| 8 | Na tym zamówieniu dodaj korektę sprzedaży | Korekty nie ma na liście „Operacje”. |
| 9 | Wydatki: dodaj wydatek bez daty zapłaty | Nie ma go na liście. Po „Oznacz jako opłacony” z dzisiejszą datą jest: „Opłacony wydatek”, Wypływ. |
| 10 | Wydatek `-50` z dzisiejszą datą zapłaty | Na liście kwota `-50,00 zł` z dopiskiem „Korekta / zwrot od dostawcy”; wypływy maleją o 50 zł. |
| 11 | Filtr rodzaju: zaznacz tylko „Wpłata” | Tylko wpłaty; sumy liczą tylko wpłaty. Chip rodzaju, „Wyczyść filtry” wraca do bieżącego miesiąca. |
| 12 | Okres `01.12.2025 – 31.01.2026` | Informacja „Operacje liczone od 01.01.2026 (data uruchomienia).”; operacje sprzed 01.01.2026 nie są widoczne. |
| 13 | Okres `01.10.2025 – 31.10.2025` (całość przed uruchomieniem) | Pusta lista, sumy `0,00 zł`. |
| 14 | „Data od” późniejsza niż „Data do” | Błąd przy polu, brak zapytania. |
| 15 | Kliknij opis wiersza wydatku, potem wróć | Szczegóły wydatku; po powrocie te same filtry i strona. |
| 16 | Widok telefonu (~390 px) | Filtry w rozwijanym panelu, bez przewijania w poziomie. |

Historii zmian (audyt wypłat) nie da się sprawdzić w UI przed SF-18.
