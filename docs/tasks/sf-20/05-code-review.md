# Code review — SF-20

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Backend liczy karty, podział, przedziały i ostatnie operacje z tych samych źródeł (`ICashOperationSource`), tej samej reguły sum (`CashFlowTotals.Of`), tego samego przełomu uruchomienia i tej samej kolejności (`CashOperationReader`) co lista „Operacje”. Zgodność kart z `/api/operations` wynika więc z konstrukcji, a test integracyjny ją sprawdza (miesiąc, rok i własny zakres przecinający datę uruchomienia, z wydatkiem ujemnym, oboma rodzajami zwrotu i wypłatami). Wydzielenie `CashOperationReader` przenosi pętlę 1:1 (limit malejący, `> MaxOperations` → `null`, ta sama kolejność `Date → Kind → Id`). `ListOperationsHandlerTests` i `OperationsApiTests` nie były edytowane. Frontend nie liczy kwot, ma osobny stan i licznik żądań dla każdego widoku, a parametry `/operacje` znikają z adresu przez `replaceUrl`. Zostały drobne uwagi (martwa stała, test w `shared/` importujący obszar, długi komentarz). Żadna nie dotyczy kwot.
- Risk: high (human review required before merge)

## Project Invariant Violations

None.

| Invariant | Status | Uwagi |
|---|---|---|
| 1. Sales separate from cash | OK | Raport czyta wyłącznie `ICashOperationSource` (`GetCashFlowReport.cs:137`, `:147`). Nie czyta wartości zamówień, korekt sprzedaży ani nieopłaconych wydatków. Test integracyjny: nieopłacone zamówienie → wpływy 0, a wpłata nie zmienia `/api/reports/sales` (`CashFlowReportApiTests.cs:140`). |
| 2. Backend decimal money | OK | `decimal` w `CashFlowSummary.cs:49–56` (bez `Math.Abs`, bez zaokrągleń) i w `Comparison.Of`. Frontend: `Number(...)` występuje tylko w `chartNumber` (`chart-canvas.ts:39`, geometria) i dla roku (`dashboard.service.ts:43`, `:174–176`). „Razem” w tabeli wykresu to `kpis.*.value` (`cash-flow-view.html:255–266`). |
| 3. API-enforced access | OK | `CashFlowReportEndpoints.cs:67–69` bez `.AllowAnonymous()`, test `401` (`CashFlowReportApiTests.cs:314`). |
| 4. Retries never duplicate | Not applicable | Same odczyty. |
| 5. Financial audit trail | Not applicable | Brak zmian danych finansowych. |
| 6. Secrets stay server-side | OK | Brak nowych sekretów i zależności. |
| 7. Module and layer boundaries | OK | `CashFlowSummary` zależy tylko od domeny i `Shared.Kernel`. `CashOperationReader` należy do `Reporting.Application` i używa `Shared.Application.Operations`. `features/dashboard` nie importuje `features/operations` (grep pusty), słowniki leżą w `shared/dictionaries/operations.ts`. Uwaga niekrytyczna 2 dotyczy importu w spec w drugą stronę. |
| 8. No identifying data in docs | OK | Dane testowe i fikstury są fikcyjne („Właściciel A”, kwoty przykładowe). |

## Conventions Violations

### Critical (must fix before merge)

None.

### Non-Critical (should fix)

1. `app/src/app/shared/dictionaries/operations.spec.ts:1` — spec w `shared/` importuje `../../features/operations/operation-models`. Zależność idzie z `shared` do obszaru, a conventions (Frontend → Structure, `shared/dictionaries/`) zakładają kierunek odwrotny. Plan (F-D1) przewidział sprawdzenie re-eksportu `toBe`, ale to sprawdzenie pasuje do `features/operations/` (np. `operation-models.spec.ts`), a nie do testu słownika współdzielonego. Dotyczy tylko testu, nie kodu produkcyjnego.
2. `app/src/app/features/dashboard/report-text.ts:14` — linia komentarza ma 138 znaków. Doklejono do niej tekst bez złamania, a reszta komentarza ma szerokość ok. 100 znaków. Prettier tego nie wychwyci (nie łamie komentarzy).

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Pkt 1 — parametry i walidacja okresu jak w raporcie sprzedaży | Covered | `ReportPeriodInput.Validate` (`GetCashFlowReport.cs:125`); testy handlera i `400` w integracji. |
| Pkt 2 — efektywny początek, informacja o uruchomieniu | Covered | `period.EffectiveFrom` w odczycie; `launchNotice` we wspólnym nagłówku (`dashboard-page.ts`). |
| Pkt 3 — okres przed uruchomieniem: bez odpytywania, `kpis: null`, puste listy, komunikat | Covered | `GetCashFlowReport.cs:131–135`; `cash-flow-view.html:175–178`; test H-CF-3, I-CF-8, F-CF6. |
| Pkt 4 — porównanie per karta, baza sprzed uruchomienia, baza 0, baza ujemna | Covered | `GetCashFlowReport.cs:143–166`; test `Each_card_compares_with_its_own_base`. |
| Pkt 5 — przedziały wykresu | Covered | `period.Buckets()` sparowane po indeksie (`:168–170`); testy dni i miesięcy przyciętych. |
| Pkt 6–7 — reguły przepływów, sumy przez `CashFlowTotals`, wydatek ujemny bez wartości bezwzględnej | Covered | `CashFlowSummary.cs:49`; CF-4, I-CF-4. |
| Pkt 8 — podział wypływów: trzy pozycje w kolejności `OutflowKinds`, suma = Wypływy | Covered | `CashFlowPolicy.cs` (`OutflowKinds`), `CashFlowSummary.cs:50–52`; test kompletności `OutflowKinds` w `CashFlowPolicyTests`; CF-10, I-CF-2 (każda pozycja = `outflows` listy z jej `kind`). |
| Pkt 9 — szereg czasowy, suma przedziałów = karty | Covered | `CashFlowSummary.cs:53–56`; CF-9, H-CF-9. |
| Pkt 10 — kwoty bez ponownego zaokrąglania | Covered | CF-12 (`0.01`). |
| Pkt 11 — bezpiecznik 10 001 dla okresu i bazy, `422 report_range_too_large` | Covered | `CashOperationReader.cs:52–61`, `GetCashFlowReport.cs:138–151`; RD-2, H-CF-6, I-CF-9. |
| Pkt 12–13 — 10 ostatnich operacji w kolejności listy, kształt `OperationItemResponse` | Covered | `GetCashFlowReport.cs:172–175`, `CashFlowReportContracts.cs:45` (to samo mapowanie `OperationContracts.ToResponse`); H-CF-7 porównuje z `ListOperationsHandler`, I-CF-2 porównuje surowy JSON z `pageSize=10`. |
| Pkt 14 — „Pokaż wszystkie operacje” | Covered | `dashboard.service.ts:216`, `cash-flow-view.html:334–344`. |
| Pkt 15–16 — linki kart i podziału, brak linków przed uruchomieniem | Covered | `dashboard.service.ts:202–218`, `:273–276`; href z powtórzonym `rodzaj` sprawdzony w F-CF2. |
| Pkt 17–19 — odczyt `od`/`do`/`rodzaj` na `/operacje`, niepoprawne parametry, wejście bez parametrów | Covered | `operations-list-page.ts:39–51`, `:104–112`; `OperationsService.filterFromDashboard`; testy F-O1/F-O2/F-O3. Rozstrzygnięcia z planu (całe okno → bieżący miesiąc, `replaceUrl`) wdrożone. |
| Pkt 20–21 — wspólny wybór okresu i nagłówek, nagłówek z raportu aktywnego widoku, brak mieszania widoków | Covered | `dashboard-page.html:28–54`, `dashboard.service.ts:143–148` (`activeReport` = `null` w trakcie wczytywania). |
| Pkt 22 — wczytywanie tylko aktywnego widoku, stan w serwisie, bez odpytywania w tle | Covered | `loadActive`/`leave` (`dashboard.service.ts:445–471`), osobne liczniki; testy F-S1–F-S3. |
| Pkt 23 — „Stan na”, `app-incomplete-data-notice` w obu widokach | Covered | Blok `aktualnosc` poza `@if` widoku. |
| Pkt 24 / kryterium 6 — brak „zysk” i „saldo”, brak pola salda | Covered | F-CF7 sprawdza cały `DashboardPage` z `aria-label`/`title`, także po otwarciu tabeli; I-CF-1 przechodzi rekurencyjnie po nazwach właściwości. |
| Kryterium 7 — wartości dostępne tekstowo, canvas z `aria-label` i klawiaturą | Covered | Tabela danych z `caption`, lista podziału, tabela ostatnich operacji; F-CF8. |
| UI pkt 3 — karty, układ 1/2/3 kolumny | Covered (z odchyleniem z planu) | `md`/`xl` (768/1280 px) zamiast 1200 px, zgodnie z „Sprzedażą” (plan, Risks). |
| UI — stany: wczytywanie, błąd z ponowieniem, `422`, pusty okres z prawdziwymi zerami | Covered | `cash-flow-view.html:171–179`, `rangeError` aktywnego widoku; F-CF1, F-CF5. |
| Przeniesienie słowników do `shared/dictionaries` z re-eksportem | Covered | `shared/dictionaries/operations.ts`, `operation-models.ts` re-eksportuje dotychczasowe nazwy. Lista „Operacje” kompiluje się i przechodzi testy bez zmian importów. |
| `GET /api/operations` bez zmian zachowania | Covered | Diff `ListOperations.cs` zamienia tylko pętlę i sortowanie na wywołania readera. `ListOperationsHandlerTests.cs` i `OperationsApiTests.cs` bez zmian w drzewie roboczym. |
| Scenariusze testów ręcznych UI w `06` | Missing (poza tym etapem) | Należą do `sdd-done`. |

## Plan Deviations

- `app/src/app/features/dashboard/sales-view.ts` jest w planie jako „modify” (typ `ListLink` zamiast `ExpensesLink`), ale nie ma go w diffie. Plik nie odwoływał się do `ExpensesLink` (`grep` po zmianach nie znajduje tej nazwy w `app/src`), więc zmiana nie była potrzebna. Typecheck przechodzi.
- `OUTFLOW_KINDS` (`report-models.ts:258`) w planie miał służyć „tylko do testów i typów”, ale nie używa go żaden plik, także test (zob. Code Smells).

## Null Safety Issues

None. Handler sprawdza `null` z `CashOperationReader.ReadAsync` dla okresu i bazy. `Kpis` jest `null` tylko przy `BeforeLaunch`, a widok sprawdza `data.kpis` przed użyciem. `cashFlowLinks` i `outflowKindLink` zwracają `null` bez raportu i przed uruchomieniem, a szablon to obsługuje (`@if (... ; as target)`).

## Correctness Issues

None. Sprawdzone:

- **Uzgodnienie z listą:** lista liczy `CashFlowPolicy.EffectiveFrom(from, launch)` = `LaunchCutoff.EffectiveFrom`. Link przekazuje `period.effectiveFrom` i `period.to`, więc lista czyta ten sam zakres, a efektywny początek jest idempotentny. `CashFlowSummary` filtruje dodatkowo po `period.Effective`, które pokrywa się z zakresem odczytu, więc ostatnie operacje (z niefiltrowanego `current`) i sumy dotyczą tego samego zbioru.
- **Filtr rodzajów:** karta „Wypływy” daje `rodzaj=refund&rodzaj=expense&rodzaj=withdrawal` (kolejność `OPERATION_KINDS`). Lista zamienia to na `kind` powtórzony i obejmuje oba rodzaje zwrotu (`Refund`).
- **Porównanie:** `ForBase()` czyta zakres bazy od `Base.From` (`BaseAvailable` gwarantuje `≥ launch`), a dla bazy liczone są tylko `Totals`.
- **Stan Pulpitu:** opuszczenie widoku podbija licznik i zdejmuje `loading`, a `loadCashFlow`/`loadReport` ustawiają `loading = true` synchronicznie przed `await`. Dzięki temu `activeReport` nie pokaże nieaktualnego raportu innego okresu. `rangeError` dotyczy tylko aktywnego widoku.
- **`/operacje`:** `router.navigate([], { relativeTo, queryParams: {}, replaceUrl: true })` (`operations-list-page.ts:109`) nie tworzy komponentu ponownie (te same trasy). Test sprawdza powrót bez parametrów z zachowanym, zmienionym filtrem.

## Test Gaps

- Testów integracyjnych (`CashFlowReportApiTests`, regresja `OperationsApiTests`, `SalesReportApiTests`, `EndpointSecurityTests`, `OpenApiTests`) w tym przeglądzie nie uruchomiono: zakazało tego polecenie, bo maszyna ma mało pamięci. Przeczytałem je. Pokrywają kryteria 1–5 i uzgodnienie z listą, ale przed merge muszą przejść (lokalnie małymi partiami albo w CI).
- `dotnet format --verify-no-changes`, `npm run lint` i `npm run build` (budżet `initial`, Chart.js w leniwym fragmencie) nie były uruchamiane w przeglądzie.
- Uruchomione i zielone: `dotnet build stone-flow.slnx -c Release -m:1` (0 ostrzeżeń, 0 błędów); `dotnet test --project api/tests/StoneFlow.Reporting.Tests -c Release --no-build` (266/266); `npx ng test --watch=false --include src/app/features/dashboard --include src/app/features/operations --include src/app/shared/dictionaries` (18 plików, 175 testów); `npm run typecheck`; `npx prettier --check` na zmienionych katalogach frontu.

## Code Smells

1. `app/src/app/features/dashboard/report-models.ts:258` — `OUTFLOW_KINDS` to martwy kod: nie importuje go ani kod, ani test. UI renderuje kolejność z odpowiedzi API. Usunąć albo użyć w teście kolejności podziału.
2. `app/src/app/features/dashboard/cash-flow-view.ts:163` — `aria-label` wykresu podziału mówi „Wartości na liście obok”. Poniżej `xl` lista jest pod wykresem, nie obok, więc lepiej napisać neutralnie („na liście”). Tekst pochodzi z planu (F12), więc to drobna uwaga do decyzji.

## Recommendation

- Merge as-is. Uwagi niekrytyczne można poprawić przy okazji. Przed merge muszą przejść testy integracyjne i pełna weryfikacja z planu (`dotnet format`, `lint`, `build`), a ryzyko `high` wymaga przeglądu przez człowieka. Gałąź bazuje na SF-19, więc merge dopiero po SF-19.
