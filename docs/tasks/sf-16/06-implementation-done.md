# Implementation done — SF-16

## Summary

Dostarczono wydatki cykliczne (backend + frontend):
- **definicja** (`expenses.RecurringExpenseDefinitions`):
  - historia dni miesiąca (`RecurringExpenseDays`, od okresu);
  - przedziały wyłączeń (`RecurringExpenseExclusions`: `Pause`, `StartMoved`);
  - rejestr okresów z PK (definicja, okres), który zostaje po usunięciu wystąpienia;
  - klucze idempotencji zachowane po usunięciu definicji;
- **kwalifikacja okresu** jako czysta funkcja stanu definicji:
  - dzień 29–31 → ostatni dzień krótszego miesiąca;
  - termin ∈ [początek, koniec], poza wyłączeniami, ≤ dziś, nie w rejestrze;
- **tworzenie wystąpień:**
  - przy dodaniu, edycji i wznowieniu, w tej samej jednostce pracy, pod blokadą wiersza definicji;
  - zadanie `recurring-expenses` codziennie o 00:10 Europe/Warsaw: nowy zakres na definicję, błąd jednej nie blokuje innych, ponowienie tylko w zadaniu;
- **wstrzymanie i wznowienie:** zmiana dnia na wstrzymanej definicji → `409`; wstrzymana nie generuje;
- **audyt:** wystąpienia z autorem „Zadanie automatyczne” i rodzicem = definicja; zmiany definicji z autorem = konto;
- **API** `/api/recurring-expenses`: lista, szczegóły, podgląd, dodanie z `Idempotency-Key`, edycja, wstrzymanie, wznowienie, usunięcie; filtr `recurringDefinitionId` na liście wydatków;
- **UI:**
  - „Powtarzaj co miesiąc” w „Dodaj wydatek” z podglądem z backendu;
  - `/wydatki/cykliczne` z listą i szczegółami;
  - dialogi;
  - oznaczenie „Cykliczny” i link do definicji;
  - `recurring-job-notice` (ostrzeżenie o nieudanym zadaniu, odczyt tylko przy wejściu na stronę);
- **PRD §5** zaktualizowane o odpowiedzi właściciela z 08.11.2026.

Specyfikacja przeszła 6 rund przeglądu (v1–v5 FAIL; v5 zmieniła model na historię dni i wyłączenia; v6 PASS WITH ISSUES). Przegląd kodu dał PASS WITH ISSUES z 3 uwagami krytycznymi. Poprawiono:
- 547 tylko dla FK, `CHECK` → błąd przebiegu;
- ponowienie z bieżącymi danymi przy dodawaniu i edycji;
- odporność odczytu na usunięcie w trakcie;
- testy: wycofanie przy błędzie audytu, brakujące `CHECK`;
- ostrzeżenie przy `running` po błędach, stan ostrożny tylko dla sieci i 5xx.

Ponownego przeglądu kodu nie wykonano.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Definicja (pola, kwota > 0, dzień, początek, koniec, aktywność) | Implemented | |
| Wystąpienie w dniu terminu, 29–31 → ostatni dzień, unikalna para definicja–okres | Implemented | |
| Zapis nowej definicji tworzy od początku do dziś; podgląd liczby | Implemented | |
| Zaległe po przerwie | Implemented | zadanie |
| Wstrzymanie/wznowienie (odp. 3), zmiana definicji tylko nieutworzone, zmiana dnia (odp. 1), przedłużenie (odp. 2) | Implemented | |
| Ślad usunięcia blokuje ponowne utworzenie | Implemented | rejestr |
| Audyt „Zadanie automatyczne” | Implemented | |
| Ostrzeżenie o niepowodzeniu zadania | Implemented | |
| E2E | Not implemented | odroczone do SF-21 |

## Files

Backend: `api/src/Modules/Expenses/**` (Domain/Recurring, Application — przypadki użycia `*RecurringExpense*`, `GenerateRecurringOccurrences`, `ListRecurringGenerationCandidates`, `Recurring/` —, Infrastructure — konfiguracje, `RecurringDefinitionLock`, `RecurringExpenseStore`, `RecurringExpenseReader`, migracja `20261108102216_RecurringExpenses`, `Recurring/RecurringExpensesJob` —, Api — `RecurringExpensesEndpoints`, `RecurringExpenseContracts` —, zmiany SF-15). Testy: `api/tests/StoneFlow.Expenses.Tests/**`, `api/tests/StoneFlow.Api.IntegrationTests/Expenses/Recurring*`. Frontend: `app/src/app/features/expenses/**`, `app/src/testing/expenses-testing.ts`, `app/src/app/app.routes.spec.ts`. Dokumenty: `docs/PRD-stone-flow.md` §5, `docs/tasks/sf-16/*`.

## Planned Units

Wszystkie jednostki z planu: Exists. Testy stałej liczby zapytań i równoległej zmiany kwoty z przebiegiem: Missing.

## Database

`ExpensesDbContext`, migracja `20261108102216_RecurringExpenses` (addytywna):
- tabele definicji, dni, wyłączeń, rejestru okresów, żądań;
- kolumny `RecurringDefinitionId` i `RecurringPeriod` w `Expenses`;
- kaskady i ograniczenia `CHECK`.

## Tests

| Test | Status | Run result |
|---|---|---|
| Expenses.Tests (domena terminów, kwalifikacja, wejście, audyt) | Exists | Passed 206/206 |
| RecurringExpensesJobTests (LocalDB) | Exists | Passed 13/13 |
| RecurringExpensesApiTests | Exists | Passed 20/20 |
| ExpensesApiTests (regresja SF-15) | Exists | Passed 31/31 |
| Scheduler + JobsEndpoint + EndpointSecurity | Exists | Passed 27/27 (przed rundą poprawek) |
| Frontend | Exists | Passed 676/676 |

## Verification

| Command | Outcome |
|---|---|
| `dotnet build stone-flow.slnx -c Release -m:1` | Passed |
| `dotnet format --verify-no-changes` | Passed |
| Architecture.Tests | Passed 56/56 |
| `npm run typecheck`, `test`, `lint`, `format:check`, `build` | Passed |
| Pełny `dotnet test`, `test-migrate.sh` | Not run (CI) |

## Deviations

- Części domeny w jednym pliku (`RecurringDefinitionParts.cs`).
- Dni i wyłączenia jako typy owned EF (`OwnsMany`).
- Podgląd nowej definicji zwraca 0 zamiast `null` w licznikach.
- Audyt wystąpień zapisywany pojedynczo (wsady tylko dla wydatków i rejestru), około 320 dodatkowych zapytań przy definicji od 2000 r.
- 2601/2627 traktowane jako kolizja rejestru bez sprawdzania nazwy indeksu.
- Komunikat walidacji wersji mówi „wydatku”.
- Pomocniki w `ExpensesEndpoints` są `internal`.
- Frontend:
  - brak `min`/`max` na polu dnia (NG8022);
  - filtr `wydatekCykliczny` czyści inne filtry, a link „Wszystkie wydatki” nie zdejmuje filtra definicji;
  - kilka tekstów spoza specyfikacji.
- Odczyt harmonogramu: odporność na usunięcie w trakcie zamiast wspólnej migawki.

## Additional Implementation

Additional implementation not covered by the original documents: netto wyliczane z brutto (÷ 1,23, VAT 23%) w formularzu wydatku jednorazowego i cyklicznego (prośba właściciela z 08.11.2026, commit `475eb49`); netto wpisane ręcznie nie jest nadpisywane.

## Scenariusze testów ręcznych (UI)

Warunki: zalogowany właściciel, pusta baza, dzisiaj = 08.11.2026 (przy innej dacie przesuń oczekiwane terminy). Kwoty dowolne > 0 zł, jeśli nie podano inaczej.

| # | Kroki | Oczekiwany wynik |
|---|---|---|
| 1 | **Wydatki → Dodaj wydatek**, wpisz brutto `1233` | Netto od razu `1002,44`. |
| 2 | W tym samym formularzu wpisz netto `1141,67`, potem zmień brutto na `1300` | Netto zostaje `1141,67` (ręczne netto nie jest nadpisywane). |
| 3 | Zaznacz **Powtarzaj co miesiąc**, dzień `31`, początek `01.08.2026`, bez końca | Znikają „Daty” i „Referencja dokumentu”, pojawia się „Harmonogram”. Podgląd: **3** wystąpienia do utworzenia (31.08, 30.09, 31.10); 30.11 jeszcze nie. |
| 4 | Zapisz | Przejście do szczegółów definicji, stan **Aktywny**, „Utworzone wystąpienia” = 3, najbliższy termin 30.11.2026. |
| 5 | **Pokaż wystąpienia** | Lista wydatków tylko z 3 wystąpieniami, każde z dopiskiem **Cykliczny**, daty kosztu 31.08 / 30.09 / 31.10. |
| 6 | Wejdź w wystąpienie z 30.09 → **Przejdź do wydatku cyklicznego** | Powrót do szczegółów definicji. |
| 7 | Usuń wystąpienie z 30.09 (szczegóły wydatku → **Usuń**) | Na liście 2 wystąpienia. |
| 8 | W definicji **Edytuj**, zmień kwotę brutto, zapisz | Wystąpienie z 30.09 **nie wraca**. Istniejące 31.08 i 31.10 mają starą kwotę (zmiana tylko dla nieutworzonych). |
| 9 | **Edytuj**, zmień początek na `01.06.2026`, zapisz | Dochodzą 30.06 i 31.07 (razem 4 na liście); 30.09 nadal nie wraca. |
| 10 | **Dodaj wydatek** cykliczny: dzień `5`, początek `01.08.2026` | Podgląd: **4** (05.08, 05.09, 05.10, 05.11 — termin 05.11 już minął). |
| 11 | W definicji z p. 10 **Edytuj**, dzień `20`, zapisz | Brak nowego wystąpienia w listopadzie (05.11 już jest; nowy dzień od bieżącego miesiąca bez duplikatu). Najbliższy termin 20.12.2026. |
| 12 | **Wstrzymaj** → potwierdź | Stan **Wstrzymany od 08.11.2026**. |
| 13 | Wstrzymana definicja → **Edytuj** | Pole „Dzień miesiąca” zablokowane z opisem „Wznów wydatek cykliczny, aby zmienić dzień miesiąca.”; informacja „Zmiany zadziałają od wznowienia”. Zmiana kwoty zapisuje się. |
| 14 | **Wznów** → potwierdź | Stan **Aktywny**, brak nowego wystąpienia (żaden termin nie minął w czasie wstrzymania). |
| 15 | **Dodaj wydatek** cykliczny z końcem: dzień `10`, początek `01.08.2026`, koniec `30.09.2026` | Podgląd: **2** (10.08, 10.09). Po zapisie brak najbliższego terminu. |
| 16 | Formularz cykliczny: brutto `0`, potem `-50` | Błąd „Kwota wydatku cyklicznego musi być większa od 0 zł.” |
| 17 | Formularz cykliczny: koniec przed początkiem | Błąd przy polu „Koniec”, zapis zablokowany. |
| 18 | Definicja z p. 3 → **Usuń** → potwierdź | Komunikat „Wydatek cykliczny został usunięty.”, definicji nie ma na **Wydatki cykliczne**; jej wystąpienia **zostają** na liście wydatków. |
| 19 | — (widok **Historia zmian** powstaje w SF-18; dziś zaślepka) | Wpisy audytu (definicja: autor konto; wystąpienia: **Zadanie automatyczne**) sprawdzisz w UI dopiero po SF-18. |
| 20 | **Wydatki cykliczne** → filtry stanu | „Aktywny” / „Wstrzymany” filtrują listę; pusta lista pokazuje „Brak wydatków cyklicznych”. |
| 21 | Widok telefonu (F12 → tryb urządzenia, ~390 px): p. 3–5 i 12 | Formularz i szczegóły czytelne, bez przewijania w poziomie. |

Niesprawdzalne lokalnie bez dodatkowego przygotowania:
- ostrzeżenie o nieudanym zadaniu (`recurring-job-notice`) — wymaga nieudanego przebiegu `recurring-expenses`;
- zaległe po przerwie (zadanie o 00:10) — wymaga zmiany daty systemowej albo poczekania do następnego dnia i wejścia na `/health`.

## Open Items

- **Human review required before merge** (ryzyko high).
- Pełny zestaw testów i `test-migrate.sh` — CI.
- Ponowny przegląd kodu po poprawkach nie został wykonany.
- Z `05-code-review.md`:
  - liczby podglądu w handlerze zamiast w domenie;
  - `null!` w readerze;
  - reguły tekstów w komponentach; `today` liczone raz;
  - „Edytuj” niezablokowane w trakcie akcji;
  - brakujące testy: stała liczba zapytań, równoległa zmiana kwoty, luki we frontendzie.
- Ręczna weryfikacja UI w przeglądarce.
- E2E — SF-21.
- Dowód z hostingu (D-08 pkt 2) przy pierwszym wdrożeniu zadania.
