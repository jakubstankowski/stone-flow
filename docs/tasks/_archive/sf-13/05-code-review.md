# Code review — SF-13

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Implementacja pokrywa wszystkie wymagania specyfikacji v2 i uściślenia planu (okno `from = kursor − 10 min`, `to = start − 10 min`, kursor = `to`; zestaw `trash` bez filtrów dat; flaga jednego oczekującego „Synchronizuj teraz”; 404 → usunięcie tylko dla kodów WooCommerce). Nie znaleziono naruszeń niezmienników: klucze REST są tylko w nagłówku `Authorization`, komunikaty są składane przez kod, Integrations nie odwołuje się do Orders, a nowe endpointy wymagają sesji i CSRF. Wszystkie 9 zgłoszonych odstępstw jest poprawnych i bezpiecznych. Najważniejsza uwaga (W1): nieudany zapis do historii błędów w trakcie uzupełniania jest tylko logowany, a mimo to kursor się przesuwa. Zamówienie z błędem może wtedy zniknąć bez śladu w historii.
- Risk: high (przed merge wymagany przegląd przez człowieka)

## Project Invariant Violations

None.

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | OK | Uzupełnianie, webhook i „Ponów” idą przez `IShopOrderSyncRunner` → `IShopOrderSync` (SF-12). Nowy kod nie tworzy wpłat ani wypływów. |
| 2. Backend decimal money | OK | Mapowanie `WooCommerceOrderPayload` bez zmian kwot. Doszły tylko `ExternalId` i `ModifiedAtUtc` w `PayloadReadResult`. |
| 3. API-enforced access | OK | `WooCommerceEndpoints.cs:23-28` nie ma `AllowAnonymous`, więc obowiązuje polityka domyślna. POST przechodzi przez globalny CSRF. Test `Endpoints_require_session_and_csrf` sprawdza `401` dla 4 tras i `400` bez XSRF. |
| 4. Retries never duplicate | OK | Idempotencja SF-12, dzierżawa SF-11 (`TryRunNowAsync` wykonuje przebieg synchronicznie, więc flaga `ManualSyncService` trwa do końca przebiegu), filtrowany unikalny indeks otwartych błędów plus `UPDLOCK, HOLDLOCK` (`WooCommerceStores.cs:250-253`). Testy: `Concurrent_failures_of_one_order_give_one_open_error`, `Sync_now_runs_in_background_once_and_second_request_gets_409`. |
| 5. Financial audit trail | OK | Zmiany zamówień audytuje SF-12 z autorem `WooCommerceSync`. Historia błędów i przebiegów to dane techniczne. |
| 6. Secrets stay server-side | OK | Basic w nagłówku (`WooCommerceRestClient.cs:41-42`), klucze nie trafiają do adresu (`PageUri`, `:100-123`), `RemoveAllLoggers()` (`DependencyInjection.cs:38`). Komunikaty bez treści odpowiedzi i bez `exception.Message` (`StatusMessage`, `:150-159`; `OrderOutcome.cs:101` podaje tylko typ wyjątku). Test `Keys_stay_in_authorization_header_and_out_of_logs_and_history` przeszukuje logi, historię błędów, przebiegi i `scheduling.JobRuns`. |
| 7. Module and layer boundaries | OK | Integrations nie ma referencji do Orders. Kontrakt `IShopOrderLookup` jest w `Shared.Application/ShopOrders`, implementacja w `Orders.Infrastructure/Persistence/ShopOrderLookup.cs`. Domain bez EF i HTTP. |
| 8. No identifying data in docs | OK | `docs/wdrozenia.md` i `deploy/api.env.example` używają `<adres-sklepu>`, testy fikcyjnych danych (`sklep.example.com`, „Jan Testowy”). |

## Conventions Violations

### Critical (must fix before merge)

None.

### Non-Critical (should fix)

1. `.sdd/context/conventions.md:156` nadal wymienia Integrations wśród modułów finansowych, w których każdy zmieniający stan przypadek użycia działa w `IUnitOfWork.ExecuteInTransactionAsync`. Nowy akapit w `:268-270` zwalnia `IntegrationsDbContext` z `ModuleConnection` i `IUnitOfWork`. Plik przeczy sam sobie; trzeba doprecyzować zdanie w `:156`.
2. `api/src/Modules/Integrations/StoneFlow.Integrations.Api/WooCommerceEndpoints.cs` leży w katalogu głównym projektu. Konwencja mówi `Endpoints/*Endpoints.cs`, tak jak w `Orders.Api/Endpoints/`. Autor powiela lokalny precedens SF-12 (`WooCommerceWebhookEndpoint.cs`), więc to drobiazg, ale niezgodność rośnie.
3. `docs/tasks/sf-13/WooCommerceOrderPayload.cs` to pusty (0 B), nieśledzony plik `.cs` w folderze zadania, pozostałość po pracy. Trzeba go usunąć przed commitem.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Konfiguracja REST (trójka albo brak, `https`, `RequestTimeout`) | Covered | `WooCommerceWebhook.cs` (walidator: pełna trójka, `https` bez query, limit (0, 5 min]). |
| Klient REST: Basic w nagłówku, limit czasu, paginacja, klasyfikacja 404 | Covered | `WooCommerceRestClient.cs`. 404 → `NotFound` tylko przy JSON `code` ∈ {`woocommerce_rest_shop_order_invalid_id`, `woocommerce_rest_invalid_id`} (`:180-194`); każdy inny 404 → `Failed`. |
| Zadanie `woocommerce-backfill` 23:00, 30 min, rejestrowane zawsze | Covered | `BackfillJob.cs:12-13`, `DependencyInjection.cs:46`. |
| Okno: `from` = kursor − 10 min / północ dnia uruchomienia w Europe/Warsaw; `to` = start − 10 min; kursor = `to` | Covered | `BackfillWindow.cs:26-34`. Format `yyyy-MM-ddTHH:mm:ss` z `dates_are_gmt=true` (`WooCommerceRestClient.cs:109,125`). Testy jednostkowe i integracyjne (`modified_after=2026-11-02T23:00:00`). |
| Zestawy `any` (z oknem), potem `trash` (bez dat) | Covered | `RunBackfill.cs:102-103`; test `Order_moved_to_trash_without_webhook_is_cancelled`. |
| Stabilność `X-WP-Total`: jedna powtórka, druga zmiana → nieudany przebieg | Covered | `RunBackfill.cs:121-138,157-164`; testy `Changed_total_refetches_the_set_once`, `Second_change_of_total_fails_the_run_and_keeps_cursor`. |
| Sukces = wszystkie strony obu zestawów; kursor i `succeeded` w jednej transakcji | Covered | `WooCommerceStores.cs:45-70`. Zob. uwagę C2. |
| Niepowodzenie: `failed`, kontrolowany komunikat, kursor bez zmian, wyjątek dla SF-11 | Covered | `RunBackfill.cs:105-110`; test integracyjny sprawdza `scheduling.JobRuns.Error` = „WooCommerceBackfillException: Sklep odpowiedział HTTP 503 na stronie 1”. |
| Anulowanie: brak zapisów; kolejny przebieg oznacza `running` jako przerwane; odczyt `abandoned` | Covered | `RunBackfill.cs` nie łapie `OperationCanceledException`, a `OrderOutcome` przepuszcza je przy anulowanym tokenie. `StartRunAsync` (`WooCommerceStores.cs:21-43`), `GetSyncStatus.cs:30-33`. Zob. C3 i T2. |
| Pozycja bez `id` → `Unidentified`, przebieg dalej | Covered | `OrderOutcome.cs:69-73`. |
| „Synchronizuj teraz”: `202`/`409 sync_in_progress`/`409 integration_not_configured`, jedno oczekujące, flaga zawsze zwalniana | Covered | `RequestSync.cs`, `ManualSyncService.cs` (CAS, zwolnienie w `finally`, `:64-67`). Zamknięcie aplikacji: `stoppingToken` → anulowanie przebiegu. |
| Historia błędów: upsert, jeden otwarty, `Attempts`, większa `FailedModifiedAtUtc`, ponowienie przy 2601/2627 | Covered | `WooCommerceStores.cs:130-182`. |
| Zamykanie z warunkiem wersji, usunięcie zamyka zawsze, `ClosedBy` | Covered | `OrderErrorRules.CanClose` pod blokadą (`WooCommerceStores.cs:184-220`); test `Successful_webhook_closes_error_but_older_delivery_does_not`. |
| Webhook zapisuje błąd bez zmiany kodów (`200` nieczytelne, `503` błąd zapisu) | Covered | `ReceiveWebhook.cs:105-138`. Brak testu, że błąd zapisu historii nie zmienia kodu (T1). |
| „Ponów”: `404`, `409 error_closed`, `409 integration_not_configured`, `200`, 404-kod → usunięcie, inny 404 → `502`, błąd ERP → `503` | Covered | `RetryOrderError.cs`. Kontekst = TraceId (= `X-Correlation-ID`, test to sprawdza). Brak testu `503` (T3). |
| `GET /status`, `GET /errors` (walidacja, sortowanie, `orderId`/`orderNumber`) | Covered | `GetSyncStatus.cs`, `ListOrderErrors.cs`; stabilne sortowanie `LastOccurredAtUtc desc, Id desc`. |
| `IShopOrderLookup` (Shared → Orders) | Covered | Jedno zapytanie bez N+1. |
| Migracja `WooCommerceBackfill` | Covered | Trzy tabele, filtrowany unikalny indeks `[Status] = N'open'`, indeksy malejące (`descending: new bool[0]` = wszystkie kolumny malejąco). Tylko dodawanie (expand), zgodne z poprzednią wersją API. |
| Dokumentacja (PRD sekcja 5 i ZAM-02, `wdrozenia.md` sekcje 11–12, `api.env.example`) | Covered | Znane ograniczenie, klucz „Odczyt”, objaw obciętego nagłówka, weryfikacja tylko do odczytu. |

## Plan Deviations

Wszystkie zgłoszone przez autora odstępstwa sprawdziłem. Są zgodne z kodem i bezpieczne.

1. **Cały `IntegrationsDbContext` ma własne połączenie** (`DependencyInjection.cs:21-24`). Plan zakładał osobne połączenie tylko dla magazynu błędów. Integrations nie uczestniczy w żadnej jednostce pracy Orders (zapis zamówień idzie przez `ShopOrderSyncRunner` w nowym zakresie), więc rozdzielenie jest poprawne. Dzięki niemu wycofany zapis zamówienia nie cofa zapisu błędu. Konwencja została uzupełniona, ale zob. niezgodność 1 w Conventions.
2. **`Scheduling:Enabled=false` w `StoneFlowApiFactory`, `RemoveAll<JobDefinition>()` w `SchedulerHost` i `JobsEndpointTests`.** Bez tego zadanie rejestrowane zawsze startowałoby samo w testach innych obszarów. `SchedulerHost` ustawia `Scheduling:Enabled` jawnie (`:76`), więc testy SF-11 nadal sprawdzają pętlę. Konfiguracja produkcyjna bez zmian.
3. **`RemoveAllLoggers()` zamiast ograniczenia do `Warning`.** Ostrzejsze niż w planie. Tracimy diagnostykę HTTP, ale kontrolowane komunikaty przebiegów to rekompensują.
4. **Korelacja „Ponów” przez `Activity.Current.TraceId`** (`WooCommerceEndpoints.cs:65`). `CorrelationIdMiddleware` wylicza identyfikator z tego samego TraceId i uruchamia aktywność zastępczą, gdy jej brak, więc wartości się zgadzają. Test porównuje z `X-Correlation-ID`. Powiązanie jest niejawne (moduł nie może odwołać się do hosta), ale działa.
5. **Zamknięcie przez odczyt `UPDLOCK, HOLDLOCK` i regułę w domenie** zamiast warunkowego `ExecuteUpdate`. Semantyka ta sama. Blokada szereguje zamknięcie z `RecordAsync` dla tego samego `ExternalId`, a reguła ma test jednostkowy z 8 przypadkami.
6. **Id przebiegu = `JobRunContext.RunId`.** `JobScheduler.TryStartAsync` nadaje nowy `Guid` na każdy przebieg, więc PK nie koliduje. Wiersz Integrations łączy się z historią SF-11.
7. **Liczniki po unikalnych ID** (`RunBackfill.cs:185-213`). Powtórka zestawu nie zawyża liczb. Zob. jednak S3.
8. **Bezpiecznik 1000 stron** (`RunBackfill.cs:145-148`) z kontrolowanym komunikatem: 100 000 zamówień w jednym zestawie, bezpieczny limit.
9. **Limit odpowiedzi 32 MB** (`WooCommerceRestClient.cs:20,33`). Przekroczenie daje `HttpRequestException`, czyli nieudany przebieg. Komunikat jest mylący (S5).

Inne różnice względem planu (bez wpływu):
- `BackfillWindowTests` i `OrderErrorRulesTests` połączono w `WooCommerceRulesTests`. Nazwy testów z planu (`Rest_client_never_puts_keys_in_url`, `Backfill_twice_no_duplicates`, `Error_messages_have_no_customer_data`, `Sync_now_twice_one_run`) mają odpowiedniki o innych nazwach (`Page_request_uses_basic_header_and_never_puts_keys_in_url`, `Missed_webhook_is_backfilled_from_launch_date_once`, `Keys_stay_…` i `Save_failure_is_recorded_without_exception_text…`, `Sync_now_runs_in_background_once…`).
- Klasa przebiegu nazywa się `WooCommerceBackfill` (plik `Backfill/RunBackfill.cs`, jak w planie).

## Null Safety Issues

None.

Wykrzykniki (`response!`, `page!.Value`, `options.BaseUrl!`, `result.Error!`) stoją po sprawdzeniach, które gwarantują wartość (`failure is null`, walidacja pól, `RestConfigured`, `Outcome == Done`). Rzutowania na zamknięte hierarchie `RestPageResult` i `RestOrderResult` (prywatny konstruktor) są bezpieczne.

## Correctness Issues

**Ważne**

- **W1. Nieudany zapis historii błędu przy uzupełnianiu nie zatrzymuje przesunięcia kursora.** `OrderOutcome.cs:132-143` (`RecordAsync`) łapie każdy wyjątek zapisu błędu i tylko go loguje (`:139-142`). `RunBackfill.cs:166-170` traktuje pozycję jako obsłużoną, a `:112` przesuwa kursor na `to`. Przykład: zapis zamówienia w Orders się nie udaje, a zapis jego błędu też, np. przez chwilowy deadlock `1205`, limit czasu albo przepełnienie puli połączeń. Wtedy zamówienie nie trafia ani do ERP, ani do historii błędów, a kolejny przebieg startuje za nim (`from = to − 10 min`). Odzyska je tylko kolejna zmiana w sklepie albo jego webhook. Przeczy to celowi „błąd … trafia do historii” i priorytetowi testów 4 z `rules.md` („missed data backfilled”). Plan (krok 5) celowo przyjął „zapis błędu → tylko log” dla wszystkich źródeł, ale specyfikacja wymaga tego wprost tylko dla webhooka („Zapis błędu sam w sobie nie może zmienić kodu odpowiedzi”). Proponowana poprawka w ramach spec: w uzupełnianiu nieudany zapis błędu ma kończyć przebieg niepowodzeniem albo przynajmniej nie przesuwać kursora. Dla webhooka i „Ponów” można zostawić samo logowanie.

**Drobne**

- **C2. `SucceedRunAsync` przesuwa kursor, nawet gdy przebiegu nie ma już w stanie `running`.** `WooCommerceStores.cs:68` ignoruje wynik `FinishAsync` (liczbę zmienionych wierszy). Przy utracie dzierżawy, gdy kolejny przebieg zdążył oznaczyć ten jako przerwany, kursor przesuwa się w transakcji z zerową zmianą wiersza przebiegu. Danych to nie gubi, bo przebieg pobrał wszystkie strony, ale historia przebiegów i kursor się rozjeżdżają. Wystarczy sprawdzić wynik i wycofać transakcję.
- **C3. Brak jawnego `ThrowIfCancellationRequested` przed końcowymi zapisami** (`RunBackfill.cs:105-112`). Brak zapisu po anulowaniu wynika tylko z tego, że EF lub SqlClient zgłosi wyjątek dla anulowanego tokenu przy `BeginTransactionAsync`. Kontrakt `IScheduledJob` jest dziś zachowany, ale niejawnie. Test `Cancellation_writes_nothing_after_it` sprawdza zachowanie fałszywego magazynu, a nie EF.
- **C4. Wyścig zostawiający nieaktualny otwarty błąd.** `CloseAsync` (`WooCommerceStores.cs:195`) nic nie zamyka, gdy otwartego błędu jeszcze nie ma. Jeśli równolegle webhook wersji V2 nie zapisze zamówienia, a uzupełnianie tę samą V2 zapisze, to późniejszy `RecordAsync` z webhooka otworzy błąd dla wersji, która już jest w ERP. Duplikatów nie ma (niezmiennik 4 zachowany). Błąd zamknie „Ponów” albo kolejna dostawa. Wystarczy go odnotować.
- **C5. Ryzyko interpretacji dat przez sklep.** Okno opiera się na `dates_are_gmt=true` (`WooCommerceRestClient.cs:109`). Jeśli sklep zignoruje ten parametr, potraktuje granice jako czas lokalny. Górna granica przesunie się wtedy o 1–2 h wstecz, a kursor i tak przejdzie na `to`. Margines 10 min tego nie pokrywa. Założenie jest zapisane w `wdrozenia.md` sekcja 12. Warto dopisać do weryfikacji konkretny krok: zamówienie zmienione na mniej niż 2 h przed przebiegiem musi pojawić się w odpowiedzi.

## Test Gaps

- **T1.** Brak testu, że nieudany zapis historii błędu nie zmienia kodu odpowiedzi webhooka (`200`/`503`) ani wyniku przebiegu. Fałszywe `IWooCommerceSyncEvents.ErrorRecordFailed` istnieje w `BackfillFakes.cs`, ale żaden test nie wywołuje tej ścieżki. To wymaganie spec („Zapis błędu sam w sobie nie może zmienić kodu odpowiedzi”).
- **T2.** Brak testu odczytu `GET /status` z `lastRun.status = abandoned` (wiersz `running` bez ważnej dzierżawy).
- **T3.** Brak testu `503 retry_unavailable` dla „Ponów” przy błędzie zapisu w ERP.
- **T4.** Brak testu `409 sync_in_progress` przy ważnej dzierżawie przebiegu harmonogramu (ścieżka `BackfillLease.IsValidAsync` w `RequestSync.cs:38-41`). Test pokrywa tylko flagę oczekującego żądania.
- **T5.** Testy integracyjne uruchamiają przebieg przez `TryRunNowAsync`, więc zawsze ze źródłem `manual`. Wartość `schedule` (`BackfillJob.cs:18`) i rejestracja 23:00/30 min nie mają testu (np. przez `GET /api/jobs`).
- **T6.** Anulowanie i porzucenie przebiegu są testowane tylko jednostkowo, na fałszywym magazynie (zob. C3). Brak testu na SQL, że po anulowaniu wiersz zostaje `running`, a kursor bez zmian.

Testy są poza tym rzeczowe i pokrywają wszystkie kryteria akceptacji. Sprawdziłem jeden: `dotnet test api/tests/StoneFlow.Api.IntegrationTests --no-build -- --filter-method "*Sync_now_runs_in_background_once*"` dał 1/1. Pozostałe wyniki z opisu autora nie były uruchamiane w tym przeglądzie, bo pamięć jest ograniczona.

## Code Smells

- **S1.** Webhook loguje wyjątek zapisu dwa razy: `OrderOutcome.cs:100` (`events.ApplyFailed`) i `ReceiveWebhook.cs:133-136` (`events.Failed`).
- **S2.** `lastSuccessAtUtc` w `GET /status` to kursor, czyli start przebiegu − 10 min (`GetSyncStatus.cs:36`), a nie chwila ostatniego sukcesu. Zgodne z planem, ale SF-14 pokaże czas o 10 min wcześniejszy. Warto to zaznaczyć w kontrakcie (komentarz `SyncStatusResponse`) albo w zgłoszeniu SF-14.
- **S3.** `OrdersProcessed` obejmuje co noc cały kosz (`RunBackfill.cs:103`, zestaw bez dat), więc licznik w SF-14 będzie zawyżony o niezmienione zamówienia z kosza.
- **S4.** Komunikat `WooCommerceBackfill.AbandonedMessage` „Przerwany (restart lub awaria)” trafia też do przebiegów przerwanych przez limit `MaxDuration` albo utratę dzierżawy. Powód jest wtedy tylko w historii SF-11.
- **S5.** Za duża odpowiedź (> 32 MB) daje komunikat „Brak połączenia ze sklepem albo przerwana odpowiedź” (`WooCommerceRestClient.cs:141-144`), który myli przy diagnozie.

## Recommendation

- Merge as-is — po obowiązkowym przeglądzie przez człowieka (ryzyko high). Przed merge zalecam poprawić W1 i usunąć pusty plik `docs/tasks/sf-13/WooCommerceOrderPayload.cs`. Uwagi C2, C3 i T1–T4 są tanie i warto je zrobić w tej samej zmianie.
- **Przegląd przez człowieka przed merge jest wymagany niezależnie od wyniku (ryzyko high):** pierwsze wywołania do sklepu z kluczami REST, automatyczne anulowanie zamówień (kosz, 404 w „Ponów”), kursor decydujący o kompletności danych.
