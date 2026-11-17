# Code review — SF-17

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Implementacja pokrywa spec i plan. Reguły pieniędzy siedzą w `Reporting.Domain` (`CashFlowPolicy`, `CashFlowTotals`), wszystko liczy się na `decimal`, a datę uruchomienia odcina SQL w obu źródłach. URU-01 działa (filtr po dacie wpisu, nie po dacie zamówienia ani kosztu). Audyt wypłat zapisuje się w tej samej jednostce pracy, ma flagę danych osobowych przy `person` i etykietę bez osoby. Idempotencja i `rowversion` idą wzorem SF-15. Reporting korzysta tylko z kontraktów `Shared`. Nie znalazłem problemów krytycznych ani poważnych. Uwagi niekrytyczne dotyczą układu i zachowania wiersza sum, nullowalności `description` we froncie, udokumentowania odstępstw od spec oraz ryzyka scalenia migracji z SF-16.
- Risk: high (wymagany przegląd człowieka przed merge)

## Project Invariant Violations

None.

| Invariant | Status | Gdzie sprawdzone |
|---|---|---|
| 1. Sales separate from cash | OK | Wypłata żyje tylko w `expenses.OwnerWithdrawals` i jako `Outflow` (`CashFlowPolicy.cs:22-27`). Źródła czytają wyłącznie wpłaty, zwroty, wydatki z `PaidOn != null` i wypłaty (`ExpenseCashOperationSource.cs:30`, `OrderCashOperationSource.cs:28-74`); korekty sprzedaży nie są czytane. Test `Withdrawal_is_not_an_expense`, `Sales_corrections_unpaid_expenses_and_orders_without_payments_are_not_listed`. |
| 2. Backend decimal money | OK | `decimal(18,2)` + `CHECK (Amount > 0)` w migracji (`20261109064821_OwnerWithdrawals.cs:21,33`); sumy w `CashFlowTotals.Of` (`CashFlowTotals.cs:15-34`) bez zaokrągleń i bez wartości bezwzględnej; JSON jako tekst `0.00` w kulturze niezmiennej (`OperationCodes.cs:56`). Frontend tylko formatuje ciągi (`formatPln` bez `Number` dla kwot ≠ 0; walidacja kwoty tekstowa, `withdrawal-form-model.ts:74-92`). |
| 3. API-enforced access | OK | Brak `AllowAnonymous`; polityka domyślna hosta i globalny CSRF. Testy `Anonymous_requests_and_writes_without_csrf_are_rejected` (wszystkie 5 tras wypłat, 401 i `csrf_invalid`) i `Anonymous_request_is_rejected` dla `/api/operations`. |
| 4. Retries never duplicate | OK | `UX_OwnerWithdrawals_CreationRequestId` (filtrowany, unikalny), skrót treści, `Repeated`/`KeyReused` (`CreateOwnerWithdrawal.cs:56-76`); test wyścigu `Parallel_creates_with_the_same_key_create_one_withdrawal`. |
| 5. Financial audit trail | OK | `Created`/`Updated`/`Deleted` w `IUnitOfWork.ExecuteInTransactionAsync` z `commitWhen` tylko dla sukcesu; `Deleted` zapisany przed `DELETE` (`DeleteOwnerWithdrawal.cs:48-53`) ze wszystkimi starymi wartościami; `IsPersonalData(Person)` (`OwnerWithdrawalAudit.cs:66`); etykieta stała „Wypłata właściciela” (`OwnerWithdrawalAudit.cs:14`); data biznesowa = data wypłaty, przy zmianie nowa. Testy `Every_change_is_audited_without_the_person_in_the_label`, `Audit_failure_rolls_back_the_withdrawal_change`, `Change_between_load_and_save_or_delete_returns_409_without_audit`. |
| 6. Secrets stay server-side | OK | Brak nowych sekretów; Problem Details bez szczegółów technicznych. |
| 7. Module and layer boundaries | OK | Reporting referencjonuje tylko `Shared.Application`/`Shared.Kernel` (sprawdzone `.csproj` i `using`); `CashOperationKind` w Kernel, kontrakt w `Shared.Application`, implementacje w Infrastructure modułów-właścicieli; encje nie wychodzą poza Infrastructure (projekcje do typów anonimowych). `StoneFlow.Architecture.Tests` przechodzą bez nowych wyjątków (56/56). |
| 8. No identifying data in docs | OK | Testy i dokumenty używają fikcyjnych osób („Właściciel A”, „Wspólnik B”). Osoba w `description`/`sourceLabel` listy to dane w odpowiedzi API, nie w dokumentach (decyzja 7 planu). |

## Conventions Violations

### Critical (must fix before merge)

None.

### Non-Critical (should fix)

1. `app/src/app/features/operations/operations-list-page.html:214-244` — wiersz sum stoi pod tabelą. Spec (UI → Lista „Operacje”) wymienia go w kolejności: filtry, informacja o dacie uruchomienia, sumy, potem `DataTable`. Przy 25 wierszach na stronie sumy uzgadniane z przepływami (FIN-05) lądują pod całą tabelą. Kwot to nie zmienia, ale układ różni się od spec.
2. `app/src/app/features/operations/operations.service.ts:128` — `load()` zeruje `totals` na początku każdego odczytu, także przy zmianie strony i sortowania, choć sumy od nich nie zależą. Wiersz sum znika i wraca, a region `aria-live="polite"` (`operations-list-page.html:214`) ogłasza sumy przy każdym przejściu strony. Wystarczy ukrywać sumy przy błędzie i `422`, tak jak opisuje plan F5.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Encja `OwnerWithdrawal`, tabela, `CHECK`, indeksy, `rowversion`, migracja addytywna | Covered | `OwnerWithdrawalConfiguration.cs`, migracja tylko `CreateTable` + 2 indeksy; `Down` usuwa tabelę. |
| Indeksy `IX_Payments_PaidOn`, `IX_Refunds_RefundedOn` (INCLUDE `Amount`, `OrderId`) | Covered | `OrderEntryConfiguration.cs:61-64, 80-83`; migracja tylko `CreateIndex`/`DropIndex`. |
| API wypłat: `/people`, `GET`, `POST` z `Idempotency-Key`, `PUT`, `DELETE` z wersją | Covered | `OwnerWithdrawalsEndpoints.cs`; kody i teksty błędów zgodne ze spec. |
| Walidacja: osoba przycięta ≤ 100, kwota > 0 i ≤ 2 miejsca i ≤ 16 cyfr, data ≤ dziś (Warsaw), metoda ze słownika | Covered | `OwnerWithdrawalInput.cs:24-91`; `WarsawTime.Today(timeProvider)` w handlerach; test granicy dnia. |
| Audyt każdej zmiany, brak audytu bez zmian, usunięcie ze starymi wartościami w tej samej transakcji | Covered | Edycja bez zmian: ta sama wersja, bez `SaveChanges`, `UpdatedAtUtc` bez zmian (decyzja 2). |
| Wypłata sprzed uruchomienia zapisywalna, niewidoczna na liście | Covered | Test `Withdrawal_date_today_is_judged_in_Warsaw_time_and_dates_before_launch_are_accepted` i URU-01. |
| Kontrakt `ICashOperationSource`, `CashOperation`, `CashOperationKind` | Covered | Zgodny z „Types crossing a boundary”. |
| Źródła Orders (wpłaty, zwroty) i Expenses (opłacone wydatki, wypłaty), projekcja w SQL z `TOP` | Covered | `Take(limit)` i `Take(remaining)`; drugie zapytanie pomijane przy `remaining ≤ 0`. |
| `CashFlowPolicy.DirectionOf`, `EffectiveFrom`, `CashFlowTotals.Of`, `CashMovement` w `Reporting.Domain` | Covered | Testy jednostkowe dla każdego rodzaju, ujemnego wydatku i samych korekt. |
| `GET /api/operations`: filtr okresu i rodzaju, sortowanie z remisem, stronicowanie 25/100, sumy dla całego filtra | Covered | `ListOperations.cs:56-115`; remis: rodzaj (kolejność enum) i `Id` w obu kierunkach. |
| Efektywny początek, brak odpytywania źródeł przy okresie przed uruchomieniem, URU-01 | Covered | `ListOperations.cs:67-74`; integracja `Operations_count_from_launch_by_their_own_date_not_the_order_or_cost_date`, `Period_entirely_before_launch_is_empty_with_zero_totals`. |
| Bezpiecznik 10 000 → `422 operations_range_too_large` bez sum | Covered | `ListOperations.cs:85-97`; test 10 000 → 200, 10 001 → 422, także z drugiego źródła. |
| Walidacja parametrów listy (`from` > `to` przy `from`, `page`, `pageSize`, `kind`, `direction`) | Covered | |
| Format pozycji (`kind`, `direction`, `amount` zapisana, `method?`, `refundKind?`, `category?`, `sourceType`, `sourceId`, `sourceLabel`) | Partial | Zgodnie z decyzją 7 planu `sourceLabel` = osoba dla wypłat; spec mówi „Wypłata właściciela” (zob. Plan Deviations). |
| UI lista: „Dodaj wypłatę”, filtry z chipami, informacja o uruchomieniu, sumy z etykietą podstawy, kolumny Data/Rodzaj/Opis/Kierunek/Kwota/Metoda, „Nie podano”, dopisek korekty, stany | Covered | Uwagi o położeniu i znikaniu wiersza sum w Conventions → Non-Critical 1–2. |
| Stan filtra i strony po powrocie ze źródła | Covered | `OperationsService` `providedIn: 'root'`. |
| UI formularz: `datalist` osób, `select` bez domyślnej metody, błędy przy polach i podsumowanie, fokus, `409` | Covered | Klucze błędów API mapowane po `toLowerCase` (zgodnie z `normalizeFieldErrors`). |
| UI szczegóły: stany, edycja w miejscu, dialog „Usunąć wypłatę?”, powrót z „Wypłata usunięta.”, `404` | Covered | |
| Klucz idempotencji zachowany po błędzie, nowy tylko po „Dodaj kolejną wypłatę” | Covered | `withdrawal-create-page.ts:32, 48-52`. |
| Kwoty ujemne ze znakiem `−` (U+2212) | Partial | `formatPln` daje `-` (U+002D); odstępstwo przyjęte w planie (zob. Plan Deviations). |
| Scenariusze testów ręcznych UI w `06-implementation-done.md` | Missing | Należy do etapu `sdd-done`; jeszcze nie istnieje. |

## Plan Deviations

- `sourceLabel` wypłaty to osoba, nie „Wypłata właściciela” (decyzja 7 planu). Zmienia to zdanie spec (Data / API → Format danych) i uwagę w tabeli niezmienników spec. Niezmiennik 8 dotyczy dokumentów, a osoba jest i tak w `description` tej samej pozycji, więc to nie naruszenie. `02-spec.md` w tym miejscu nie zgadza się jednak z kodem i odstępstwo trzeba zapisać w `06-implementation-done.md`.
- Znak minus: `formatPln` daje U+002D, spec (UI → Dostępność) wymienia U+2212. Plan zostawia formater bez zmian, bo lista wydatków używa tej samej ścieżki. To odstępstwo od spec, nie od planu.
- Migracje mają BOM UTF-8 tak jak większość istniejących (np. `20261106210851_Expenses.cs`, `20261106103530_ShopOrderSync.cs`); to spójne z repozytorium.
- Poza tym pliki z sekcji „Files” planu są obecne. `ExpensesEndpoints.IdempotencyKeyHeader` (stała publiczna) przeniesiono do `EndpointSupport`; nikt jej nie używał.

## Null Safety Issues

1. `app/src/app/features/operations/operation-models.ts:34` — `description: string`, a kontrakt backendu ma `string? Description` (`CashOperation.cs:28`, `OperationContracts.cs:17`). Dziś `null` nie wystąpi (numer zamówienia, wymagany opis wydatku, wymagana osoba), a szablon używa `sourceLabel`, który nie jest nullowalny. Typ frontu powinien jednak odpowiadać kontraktowi albo backend powinien zawęzić typ do `string`.

## Correctness Issues

None. Sprawdzone: kierunek tylko z rodzaju (`Payment` → wpływ), kwota bez zmiany znaku, `Change = Inflows − Outflows`, `EffectiveFrom = max(from, launch)`, brak odpytywania przy `effectiveFrom > to`, limit `10 001 − wczytane` i przerwanie pętli, sumy liczone przed stronicowaniem, przepełnienie `skip` zabezpieczone `long`, porównanie wersji przed zapisem i `OriginalValue` w `UPDATE`/`DELETE`, `commitWhen` tylko dla sukcesu (konflikt wycofuje także audyt `Deleted`).

## Test Gaps

1. `api/tests/StoneFlow.Api.IntegrationTests/Reporting/OperationsApiTests.cs:119-142` — URU-01 sprawdza wpłatę i wypłatę z dnia przed uruchomieniem (pominięte) oraz zwrot i zapłatę wydatku od uruchomienia (obecne). Brakuje wydatku z `PaidOn` sprzed uruchomienia (powinien zniknąć). Reguła jest wspólna (filtr `from` w źródle), a test jednostkowy `Sources_get_the_effective_start_after_launch` sprawdza przekazanie daty, więc ryzyko jest niskie.
2. Brak testu frontu, że wiersz sum nie znika przy zmianie strony; dziś znika (Non-Critical 2).

## Code Smells

- `api/src/Modules/Reporting/StoneFlow.Reporting.Application/Abstractions/FieldErrors.cs` — kopia typu z Expenses. Plan to dopuszcza (moduły się nie referencjonują). Gdy trzeci moduł będzie go potrzebował, warto go przenieść do `Shared.Application`.
- `app/src/app/features/operations/operation-models.ts:107-129` — kopie słowników metod i kategorii z `features/orders` i `features/expenses`. Plan to dopuszcza. Nowa kategoria wydatku wymaga zmiany w dwóch miejscach.
- Ryzyko scalenia: `ExpensesDbContextModelSnapshot.cs` zmienia też SF-16 (wydatki cykliczne). Po scaleniu drugiej gałęzi trzeba odtworzyć snapshot i sprawdzić kolejność migracji (`dotnet ef migrations list`), a nie rozwiązywać konfliktu ręcznie.

## Verification

Uruchomione przez recenzenta (Release, kolejno):

- `dotnet test --project api/tests/StoneFlow.Reporting.Tests -c Release` — 72/72 zaliczone.
- `dotnet test --project api/tests/StoneFlow.Expenses.Tests -c Release` — 102/102 zaliczone.
- `dotnet test --project api/tests/StoneFlow.Architecture.Tests -c Release` — 56/56 zaliczone.
- `dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release -- --filter-class "*OwnerWithdrawalsApiTests" --filter-class "*OperationsApiTests"` (LocalDB) — 38/38 zaliczone (7 min 26 s).

Nie uruchomione: pełny zestaw integracyjny, `dotnet format --verify-no-changes`, polecenia frontendu (`typecheck`, `test`, `lint`, `format:check`, `build`).

## Recommendation

- Merge as-is (po przeglądzie człowieka wymaganym dla ryzyka high). Uwagi niekrytyczne można poprawić przed merge albo zapisać w `06-implementation-done.md` jako otwarte.
