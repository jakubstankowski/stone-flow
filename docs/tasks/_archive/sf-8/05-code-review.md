# Code review — SF-8

Przegląd commita `5160893` (diff `55b57a6..5160893`, gałąź `sf-8-orders`) względem
`02-spec.md`, `03-spec-review.md`, `04-implementation-plan.md`, `.sdd/context/rules.md`
i `.sdd/context/conventions.md`. Recenzent nie jest autorem kodu.

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Implementacja pokrywa specyfikację i plan. Idempotencja, numeracja w transakcji
  jednostki pracy SF-7, kolejność sprawdzeń w edycji, reguły statusu, pola sklepu, audyt i stany UI
  są zrobione zgodnie z planem, a żaden niezmiennik nie jest naruszony. Problemy dotyczą głównie
  testów. Tłumaczenie `DbUpdateConcurrencyException` i ścieżka `DuplicateRequest` nie mają
  deterministycznego testu, chociaż spec go wymaga. Brakuje też testu wycofania edycji po błędzie
  audytu (AC). Do tego dochodzi kilka drobnych uwag w kodzie.
- Risk: high. Przed merge wymagany jest przegląd człowieka, niezależnie od wyniku.

## Project Invariant Violations

None.

| Invariant | Status | Gdzie sprawdzone |
|---|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | OK | Model bez pól płatności; `Create_does_not_create_money_records_or_payment_state` (`OrdersApiTests.cs:526`) |
| 2. Kwoty decimal na backendzie | OK | `decimal` w domenie, `HasPrecision(18, 2)` (`OrderConfiguration.cs:42`), migracja `decimal(18,2)`, kwoty jako tekst w API; parser `parsePlnAmount` działa na tekście (`pl-format.ts:123`), bez arytmetyki zmiennoprzecinkowej na kwotach |
| 3. Dostęp egzekwowany przez API | OK | Brak `AllowAnonymous`; `Anonymous_requests_are_rejected` (6 tras → 401), `Write_without_xsrf_is_rejected` |
| 4. Ponowienia nie duplikują | OK | Unikalne `CreationRequestId`, `Number`, `(Source, ExternalId)` filtrowany; skrót znormalizowanej komendy z domyślnym statusem uzupełnionym przed liczeniem |
| 5. Audyt finansowy | OK | `IAuditLog.RecordAsync` w tym samym `IUnitOfWork` przy create/edit/cancel/restore; brak audytu bez zmian; `IsPersonalData` dla `customer*` poza `customerKind`; etykieta = numer; brak `DELETE` |
| 6. Sekrety po stronie serwera | OK | Brak nowych sekretów i logowania danych klienta w module Orders |
| 7. Granice modułów i warstw | OK | Orders nie dotyka `reporting.*` poza `IAuditLog`; domena bez EF/HTTP; odczyty przez porty z DTO; testy architektury przechodzą w buildzie |
| 8. Brak danych identyfikujących | OK | Fikcyjni klienci („Jan Testowy”, `example.com`, testowy NIP z poprawną sumą kontrolną) |

## Conventions Violations

### Critical (must fix before merge)

None.

### Non-Critical (should fix)

- **K1 (niska).** `api/src/Modules/Orders/StoneFlow.Orders.Application/EditOrder/EditOrder.cs:98`.
  Handler sam porównuje pola sklepu (`ShopFieldsChangedBy` + `Unreadable`) i zwraca `422`, zanim
  wywoła `Order.Edit`. Gałąź `FieldOwnedByShop` w domenie (`Order.cs:193-200`) jest więc z handlera
  nieosiągalna. Reguła własności pól ma dwa miejsca decyzji. Konwencje mówią, że regułę
  decydującą o wyniku posiada domena. Lepiej przekazać nieczytelne pola do domeny albo zostawić
  porównanie tylko w niej.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Model zamówienia, słowniki, migracja `OrdersAndNumbering` | Covered | Kolumny, indeksy i check constraints zgodne ze Storage |
| POST z `Idempotency-Key`, normalizacja, SHA-256, `200`/`409 idempotency_key_reused` z `orderId`/`orderNumber` | Covered | Domyślny status przed skrótem (N3); test normalizacji spacji/NIP/statusu |
| Wyścig o klucz: `DuplicateRequest`, rollback, odczyt po kluczu, ponowne rzucenie oryginalnego wyjątku | Covered (kod) / Partial (test) | `OrderStore.cs:30`, `CreateOrder.cs:70-75`; test tylko niedeterministyczny (T2) |
| Numeracja `ZR-{rok}-{NNNN}` w transakcji, rollback cofa licznik, rok z Europe/Warsaw | Covered | `UPDATE … WITH (UPDLOCK, HOLDLOCK) … OUTPUT` dołącza do transakcji przez interceptor SF-7 (`CommandInitialized`); test wycofania sprawdza `OrderNumberCounters` = 0 |
| Lista: filtry, sort ze stabilnym `Id`, paginacja w bazie, `COUNT`, `pageSize ≤ 100`, `LIKE` z `ESCAPE` | Covered | `OrderReader.cs`; test `%`, `_`, wielokrotnego `status` |
| Szczegóły z `shopOwnedFields`, czasy UTC, `version` base64 | Covered | |
| Kolejność w `PUT`: 400 → 404 → 409 → 400/422 zależne od źródła → 422 statusu → brak zmian → zapis | Covered | `EditOrder.cs`; testy `Edit_validates_manual_order_fields_after_loading`, `staleCancelledStatus` (409 przed 422) |
| `OriginalValue` z wersji klienta, `DbUpdateConcurrencyException` → `409` | Covered (kod) / Partial (test) | `OrderStore.cs:42,47`; brak testu tłumaczenia (T1) |
| Anulowanie i przywrócenie: 404 → 409 wersja → 409 stan, audyt `fulfillmentStatus` | Covered | `OrderStatusAction.cs`; test kolejności |
| Brak `DELETE` (405), brak akcji „Usuń” | Covered | |
| Audyt `Created`/`Updated`, format wartości, brak audytu bez zmian | Covered | `OrderAudit.cs`, `OrderAuditTests`, test integracyjny formatów |
| Nieudany audyt cofa utworzenie **lub zmianę** | Partial | Test tylko dla utworzenia (T3) |
| Pola sklepu w `PUT` (data, wartość, kanał, klient) → `422 field_owned_by_shop` | Covered | Test na zamówieniu WooCommerce zasianym w bazie, także kanał (N5) |
| UI: lista, pusty stan z akcją, filtry, sort, paginacja | Covered | |
| UI: formularz, podsumowanie błędów z fokusem, klucze błędów API, pola tylko do odczytu, status anulowanego | Covered | |
| UI: klucz idempotencji do udanego zapisu, „Dodaj kolejne zamówienie”, podwójne kliknięcie | Covered | `order-create-page.spec.ts` sprawdza jedno żądanie (`expectOne` + `verify`) |
| UI: konflikt `409` zostawia dane, „Wczytaj aktualne dane” | Covered | |
| UI: dialogi z numerem i skutkiem, `danger`/`secondary`, `aria-live` | Covered | |

## Plan Deviations

- Test nazwany w planie `Parallel_creates_get_distinct_numbers` ma nazwę
  `Parallel_creates_get_distinct_consecutive_numbers`.
- Wspólny przebieg anulowania i przywrócenia leży w `Orders.Application/OrderStatusAction.cs`
  (helper modułu), a plan nie wymieniał tego pliku. Konwencje na to pozwalają.
- Zgłoszone przez autora: brak deterministycznego testu ścieżki `2627` licznika (zgodnie z planem,
  N4) i brak testu wymuszającego `DbUpdateConcurrencyException` w `SaveChanges` (odstępstwo od
  spec — patrz T1).

## Null Safety Issues

None. Nullable są jawne. `!` występuje tylko po ustalonych niezmiennikach
(`attempt.Duplicate!` w `CreateOrder.cs:75` jest osiągalne wyłącznie przy `Created == null` i
`Existing == null`, a wtedy `Duplicate` jest ustawione).

## Correctness Issues

- **P1 (niska).** `api/src/Modules/Orders/StoneFlow.Orders.Application/OrderInput.cs:492`.
  `\d` w .NET dopasowuje także cyfry Unicode (np. arabsko-indyjskie). Taki tekst przechodzi wzorzec,
  a potem `decimal.TryParse` się nie udaje i użytkownik dostaje mylący komunikat „Wartość jest zbyt
  duża”. Zapis nie jest możliwy, więc nie ma ryzyka dla danych. Lepiej użyć `[0-9]` albo
  `RegexOptions.ECMAScript`.
- **P2 (niska).** `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/OrderReader.cs:137`.
  Domyślny sort to `OrderDate DESC, Id DESC`, a indeks `IX_Orders_OrderDate_Id` to
  `(OrderDate DESC, Id ASC)`, więc indeks nie pokrywa sortowania. Przy wolumenie z D-08 nie ma to
  znaczenia, ale kierunki warto ujednolicić.

Zweryfikowane bez uwag:

- Idempotencja: skrót po normalizacji z domyślnym statusem. Po `DuplicateRequest` odczyt po
  wycofaniu przez `AsNoTracking`, a tracker jest czyszczony przez `ModuleConnection.EndTransaction`.
  Gdy zamówienia z kluczem nie ma, rzucany jest oryginalny `DbUpdateException` przez
  `ExceptionDispatchInfo` (→ 503 zgodnie z udokumentowaną rozbieżnością).
- Numeracja: polecenia surowego SQL dołączają do transakcji przez `CommandInitialized`. `UPDLOCK,
  HOLDLOCK` serializuje pierwsze zamówienia roku, a `2627` jest kodem obronnym.
- Edycja bez zmian zwraca `Saved` (commit), ale `UnitOfWork` nie woła `SaveChanges`, więc nic nie
  jest zapisane, a `version` zostaje bez zmian (potwierdzone testem).
- Wynik `Conflict` z `SaveAsync` daje `commitWhen = false` i wycofanie razem z audytem.

## Test Gaps

- **T1 (średnia).** Brak testu tłumaczenia `DbUpdateConcurrencyException` na `409`
  (`api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/OrderStore.cs:42-50`). Testy
  „starej wersji” (`OrdersApiTests.cs:267`, `:376`) trafiają w kontrolę wersji w handlerze
  (`EditOrder.cs:72`, `OrderStatusAction.cs:52`). Usunięcie linii 42 albo bloku `catch` z linii 47
  nie zepsułoby żadnego testu. Integralność danych i tak jest chroniona, bo EF sprawdza
  `rowversion` z wartością wczytaną w tej samej transakcji. Wyścig między wczytaniem a `UPDATE`
  kończyłby się jednak `500` zamiast `409`. Spec (Data / API → Endpoints) wymaga testu z kodem HTTP
  dla każdego tłumaczenia. **Ocena odstępstwa:** nie jest krytyczne dla danych, ale przy ryzyku
  high nie powinno zostać bez testu. Taki test da się napisać deterministycznie na poziomie portu:
  zakres DI, `LoadAsync`, zmiana wiersza osobnym połączeniem, `SaveAsync` → `Conflict`. Można też
  przez API z interceptorem poleceń, który zmienia wiersz przed `UPDATE`.
- **T2 (średnia).** Ścieżka `DuplicateRequest` (`OrderStore.cs:30`, `CreateOrder.cs:70-75`) jest
  pokryta tylko przez `Parallel_creates_with_same_key_create_one_order`
  (`api/tests/StoneFlow.Api.IntegrationTests/Orders/OrdersApiTests.cs:144`). Ten test przechodzi
  także wtedy, gdy wszystkie żądania trafią na odczyt po kluczu w kroku 1, więc nie dowodzi, że
  tłumaczenie `2601` działa. Nie jest też testowane ponowne rzucenie oryginalnego wyjątku przy
  duplikacie `Number` (→ `503`). Spec wymaga testu z kodem HTTP także dla tego tłumaczenia.
- **T3 (średnia).** AC „Nieudany zapis audytu cofa utworzenie lub zmianę zamówienia (test
  integracyjny)” jest pokryte tylko dla utworzenia (`OrdersApiTests.cs:201`). Brak testu dla `PUT`
  oraz `cancel`/`restore` z odrzucanym wpisem audytu, sprawdzającego, że wartość i `version` się nie
  zmieniły.
- **T4 (niska).** `OrdersApiTests.cs:511-513`. Test XSRF obejmuje `POST`, `PUT` i `cancel`, ale nie
  `restore`. Ochrona jest globalna, więc ryzyko jest małe.
- **T5 (niska).** `OrdersApiTests.cs:20`. Rok numeru pochodzi z prawdziwego zegara, a nie z
  `TimeProvider`. Brak testu edge case „po przełomie roku numeracja zaczyna się od
  `ZR-{nowy rok}-0001` według czasu Europe/Warsaw” (np. 31.12 23:30 UTC = 1.01 w Warszawie). Test
  może też być niestabilny o północy w Sylwestra.

## Code Smells

- **S1 (niska).** Podwójne porównanie pól sklepu w handlerze i domenie (patrz K1).
- **S2 (niska, dostępność).** `app/src/app/features/orders/order-form.html:245`. Pole „Status
  realizacji” nie ma `[error]`, więc błąd API pod kluczem `fulfillmentStatus` pokazuje się tylko
  w podsumowaniu, bez powiązania `aria-describedby` z polem. W praktyce UI nie wysyła złego kodu.

## Weryfikacja wykonana przez recenzenta

| Polecenie | Wynik |
|---|---|
| `dotnet build stone-flow.slnx -c Release` | sukces, 0 ostrzeżeń, 0 błędów |
| `dotnet format stone-flow.slnx --verify-no-changes --no-restore` | brak zgłoszonych zmian |
| `dotnet test --project api/tests/StoneFlow.Orders.Tests -c Release --no-build` | 59/59 |
| `dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class StoneFlow.Api.IntegrationTests.Orders.OrdersApiTests` (LocalDB) | 32/32 |
| `npm test -- --watch=false` (app) | 32 pliki, 301/301 |
| `npm run lint` (z `lint:colors`), `npm run format:check`, `npm run typecheck` | sukces |

Pełny `dotnet test --solution` i `npm run build` nie były uruchamiane w tym przeglądzie.

## Recommendation

- Fix critical issues before merge: nie ma problemów krytycznych. Zalecane jest dopisanie testów
  T1–T3 przed merge (wymagane przez spec i AC przy ryzyku high). Uwagi K1, P1, P2, T4, T5 i S2 można
  poprawić teraz albo zostawić jako dług techniczny. Przed merge wymagany jest przegląd człowieka
  (risk high), ze szczególną uwagą na idempotencję, numerację w transakcji, kontrolę wersji i dane
  osobowe w audycie.
