# Spec review — SF-11

## Summary

- Overall assessment: FAIL
- Justification: Specyfikacja dobrze pokrywa zgłoszenie: trwały stan w SQL, nadrobienie terminu, blokadę, ślad przebiegu i widoczność niepowodzenia. Projekt wyzwalacza z sekretnym tokenem w ścieżce narusza jednak niezmiennik 6. Sama specyfikacja przyznaje, że URL z tokenem trafi do logów IIS, a w obecnej konfiguracji produkcyjnej także do logów aplikacji (`Microsoft.AspNetCore.Hosting.Diagnostics` na poziomie `Information`). Spec twierdzi, że aplikacja tokenu nie loguje, ale nie podaje mechanizmu, który by to zapewniał. Brakuje też zachowania przy utracie dzierżawy i zabezpieczenia zapisu zakończenia przed przebiegiem, który już stracił dzierżawę (fencing), a od tego zależy kryterium zgłoszenia „Zadanie nie działa równolegle samo ze sobą”.

## Findings

### Critical Issues

1. **Niezmiennik 6: token wyzwalacza trafia do logów** (sekcje „Wyzwolenie” pkt 2, „Invariant impact” wiersz 6, „Assumptions” – token w ścieżce).
   - Logi hostingu: spec sama stwierdza, że URL z tokenem „może trafić do logów IIS po stronie hostingu”, i przyjmuje to ryzyko. Niezmiennik 6 nie dopuszcza wyjątków („secrets never reach … logs”). Autor specyfikacji nie może sam przyjąć takiego ryzyka. Do tego „bezpieczne uwierzytelnienie wywołań” to nadal otwarty punkt 2 D-08 (`docs/tasks/sf-1/01-weryfikacja.md`, PRD sekcja 12).
   - Logi aplikacji: `api/src/StoneFlow.Api/appsettings.json` ustawia `"Microsoft.AspNetCore": "Information"`. Szablon `appsettings.Production.json` w `docs/wdrozenia.md` sekcja 4 tego nie nadpisuje. W efekcie wbudowany log hostingu („Request starting HTTP/1.1 GET https://…/api/jobs/tick/<token>” i „Request finished …”) zapisuje pełną ścieżkę do `logs/stdout_*.log`, bo `web.config` ma `stdoutLogEnabled="true"`. W kodzie nie ma własnego „logu żądania”, który dałoby się przestawić na szablon trasy lub zamaskować token. Spec opiera się więc na nieprawdziwym założeniu o kodzie i nie wskazuje, jak spełni swoje kryterium „token nie pojawia się w logach aplikacji”. W środowisku Development (`Microsoft.AspNetCore: Warning`) problem nie wystąpi, więc test w złym środowisku przejdzie, a produkcja nadal będzie logować token.
   - Spec nie uzasadnia też, po co w ogóle sekret. Według sekcji „Wyzwolenie” sprawdzenie należnych zadań uruchamiają też start aplikacji i timer co 5 minut. Harmonogram hostingu musi więc tylko obudzić proces, a robi to każde żądanie HTTP, np. istniejący anonimowy `GET /health`.
   - Potrzebne jest jedno z dwojga: decyzja właściciela zapisana przy D-08 albo projekt, w którym sekret nie trafia do URL ani do logów, z uzasadnieniem względem niezmienników 3 i 6.

2. **Brak zachowania przy utracie dzierżawy i brak fencingu przy zakończeniu** (sekcje „Przebieg i blokada” pkt 1, 4, 5, „Edge Cases”).
   - Spec nie mówi, co runner robi, gdy przedłużenie dzierżawy się nie uda (błąd bazy) albo gdy warunkowa aktualizacja nie dotknie wiersza, bo dzierżawę przejęła inna instancja. Zadanie biegnie wtedy dalej, a po wygaśnięciu dzierżawy druga instancja (np. przy nakładaniu się procesów podczas recyklingu) uruchomi to samo zadanie równolegle.
   - Spec nie mówi, czy zapis zakończenia (`Succeeded`/`Failed`, `LastSuccessStartedAtUtc`, zwolnienie dzierżawy) zależy od tego, czy `LeaseRunId` nadal wskazuje własny przebieg. Bez tego warunku spóźniony przebieg nadpisze stan przebiegu, który przejął dzierżawę, i może zwolnić cudzą dzierżawę. `RowVersion` jest w tabeli, ale spec nie mówi, do czego służy.
   - Spec nie opisuje zadania, które po anulowaniu przy przekroczeniu `MaxDuration` nadal działa: czy runner czeka na nie i dalej przedłuża dzierżawę, czy ją zwalnia?
   - To podstawowa gwarancja kryterium zgłoszenia „Zadanie nie działa równolegle samo ze sobą”. Plan nie powinien jej dopowiadać sam.

### Non-Critical Issues

1. **Brakujący token daje `401`, a nie `404`** (sekcje „Wyzwolenie” pkt 2, „Endpoints”, „Edge Cases”, „Acceptance Criteria”). `GET /api/jobs/tick` bez segmentu tokenu nie pasuje do trasy, więc polityka fallback (`SecuritySetup`) zwraca `401`. Kryterium „z … brakującym 404” w obecnym potoku nie przejdzie. Odpowiedź „`404` bez treści” też jest niezgodna z kodem: `UseStatusCodePages` dopisuje Problem Details do pustej odpowiedzi 4xx, chyba że spec każe to wyłączyć.
2. **Niejednoznaczne skutki `abandoned` i przerwania przy zamykaniu** (sekcje „Przebieg i blokada” pkt 2 i 6, „Edge Cases”). Przebieg `abandoned` „liczy się jako niepowodzenie”. Spec nie mówi, czy zwiększa `ConsecutiveFailures` i ustawia `NextAttemptAtUtc`. Jeśli ustawia, przeczy temu przypadek brzegowy „Restart w trakcie przebiegu”, w którym zadanie startuje od nowa przy następnym wyzwoleniu. To samo pytanie dotyczy `Failed` „Przerwano przy zamykaniu aplikacji” po zwykłym recyklingu puli: czy ma 15-minutowy odstęp?
3. **Praca w tle po `202` bez wskazanego mechanizmu** (sekcja „Wyzwolenie” pkt 2). Spec nie mówi, czy endpoint sygnalizuje istniejący `BackgroundService` (np. przez kanał), czy uruchamia zadanie „odpal i zapomnij”. `conventions.md` zabrania `Task.Run` wokół asynchronicznego I/O. Ważne są też: łagodne zamykanie hosta (czy czeka na tę pracę), niezależność od `RequestAborted` i wyjątki bez obserwatora.
4. **Uśpienie procesu a długość przebiegu** (sekcja „Assumptions”, punkt o `202`). IIS liczy bezczynność tylko po żądaniach, a praca w tle procesu nie podtrzymuje. Przy limicie bezczynności krótszym niż 15 minut (SF-1 go nie potwierdził) przebieg dłuższy niż ten limit byłby przerywany za każdym razem. Spec powinna to powiązać z `MaxDuration` = 30 min i jawnie zaplanować sprawdzenie przy próbie dla D-08.
5. **Potrzeby konsumentów nie są w pełni pokryte kontraktem** (sekcje „Stan zadań” pkt 2, „Types crossing a boundary”, „Out of scope”).
   - SF-13 „Synchronizuj teraz” uruchamia to samo uzupełnianie co o 23:00 (PRD sekcja 5). Kontrakt nie pozwala ani uruchomić zarejestrowanego zadania na żądanie, ani przejąć tej samej dzierżawy, więc SF-13 musiałby budować własną blokadę albo zmieniać kontrakt SF-11.
   - Spec wspomina „kontrakt w `Shared.Application`” do odczytu stanu, ale go nie definiuje, a INT-01 potrzebuje stanu „Synchronizacja trwa”.
   - Spec powinna jasno napisać, że `LastSuccessStartedAt` z harmonogramu nie jest biznesowym kursorem „od ostatniego sukcesu” dla SF-13. Sukces synchronizacji w PRD sekcja 5 ma inną definicję („sklep odpowiedział i pobrano wszystkie strony”), a ręczne „Synchronizuj teraz” omija harmonogram.
6. **Rozdział zakresów DI i połączenia** (sekcje „Wyzwolenie” pkt 3, „Storage”). Spec nie mówi, czy `SchedulingDbContext` korzysta ze wspólnego `ModuleConnection` (`UseModuleSqlServer(serviceProvider, …)`) ani czy zapisy runnera idą w zakresie innym niż praca zadania. `conventions.md` zastrzega, że po wycofanej jednostce pracy zakres nie wykonuje dalszych zapisów, więc zapis `Failed` w zakresie zadania złamałby tę regułę.
7. **Testowalność w obecnej infrastrukturze testów** (sekcja „Verification”).
   - `StoneFlowApiFactory` domyślnie uruchamia środowisko `Production`. Po wprowadzeniu walidatora tokenu wszystkie istniejące testy integracyjne zatrzymają się na starcie, jeśli fabryka nie dostanie tokenu. Spec tego nie wymienia.
   - Wyzwolenie przy starcie i timer działają w każdym hoście testowym, a spec nie daje sposobu, by je wyłączyć lub sterować nimi (np. przez `FakeTimeProvider`). Testy równoległości i ponowień mogą przez to zależeć od kolejności zdarzeń.
8. **Kolejność wdrożenia sekretu** (sekcje „Types crossing a boundary”, „Acceptance Criteria”). Brak tokenu w `Production` zatrzymuje start, więc `Scheduling:TriggerToken` musi trafić do `appsettings.Production.json` na serwerze przed publikacją SF-11, inaczej wdrożenie wyłączy API. Kryterium wymaga tylko opisu sekretu. Kolejność nie jest wymieniona, a `deploy/api.env.example` nie został objęty zmianą.
9. **Zmiany konwencji nie są wymienione jako zmiana w `conventions.md`** (sekcje „Context”, „Assumptions”). Konwencje mówią, że każda tabela ma moduł-właściciela, schemat to nazwa modułu, Shared zawiera tylko CQRS, datę uruchomienia i konfigurację providera, a endpointy leżą w `StoneFlow.{Module}.Api`. SF-11 zmienia wszystkie cztery zasady. `conventions.md` każe aktualizować plik przy zmianie konwencji, a spec tego nie wymienia.
10. **Testy architektury** (sekcja „Assumptions”). `DbContextSchemaTests` jest sparametryzowany listą `Modules.All`, a `Modules` mapuje każdą nazwę na zestawy Domain/Application/Api. „Dopisanie kontekstu” wymaga więc osobnego testu, a nie wpisu na listę. Spec nie wymienia też wpisu w `EndpointSecurityTests.AllowedAnonymous`, którego `conventions.md` wymaga dla każdego nowego endpointu anonimowego.
11. **Limit żądań bez parametrów** (sekcja „Endpoints”). Spec nie podaje limitu, okna, partycji ani miejsca rejestracji polityki. Polityki są dziś rejestrowane w `AccessModule`, a odrzucenie zwraca Problem Details z `AuthProblems`, czyli `429`. Spec nie mówi, czy `429` przy złym tokenie jest dopuszczalne obok obiecanego `404`.
12. **`GET /api/jobs` a CQRS** (sekcja „Stan zadań”). Spec nie mówi, czy endpoint hosta woła `IDispatcher` z zapytaniem i portem, czy czyta stan bezpośrednio. `conventions.md` każe endpointom wołać wyłącznie `IDispatcher`. Spec nie definiuje też `isRunning`: czy to ważna dzierżawa, czy przebieg w stanie `running`? Po awarii procesu te dwie odpowiedzi się różnią.

### Unclear or Ambiguous Sections

- **„Należność zadania” pkt 4 i „Edge Cases”:** zadanie bez sukcesu uruchamia się od razu po wdrożeniu. Dla SF-13 oznacza to pierwszy przebieg o dowolnej porze, choć PRD mówi „raz dziennie o 23:00”. Spec nie mówi, czy to zamierzone i kto o tym decyduje (mechanizm czy zadanie).
- **„Wyzwolenie” pkt 3:** zadania biegną po kolei, a kolejne wyzwolenie w trakcie „kończy się bez działania”. Przy dwóch zadaniach (SF-13, SF-16) długi przebieg jednego opóźnia drugie. Spec nie mówi, czy to dopuszczalne dla wymogu SF-16, by wystąpienie powstało „w dniu terminu”.
- **„Storage” → `DueAtUtc`:** dla zadania bez sukcesu albo po kilku pominiętych terminach nie wiadomo, który termin się zapisuje: najpóźniejszy czy pierwszy pominięty.
- **„Przebieg i blokada” pkt 2:** spec nie mówi, czy oznaczenie `abandoned` i przejęcie dzierżawy odbywają się w jednej transakcji.
- **„Types crossing a boundary” → `JobTrigger`:** spec podaje typ, ale nie wymienia jego wartości ani ich związku z kodami `http`, `startup` i `timer` w `JobRuns.Trigger`.

### Invented or Unsupported Requirements

- Natychmiastowe uruchomienie zadania bez sukcesu po pierwszym wdrożeniu („Należność zadania” pkt 4): brak źródła w zgłoszeniu i PRD, a nie jest wymienione w Assumptions.
- Pozostałe wartości techniczne (timer 5 min, `MaxDuration` 30 min, odstępy 15 min i 1 h, długość tokenu) są jawnie oznaczone w Assumptions jako przyjęte w specyfikacji. To dopuszczalne.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Invariant impact (6), Assumptions (token w ścieżce) | `rules.md` niezmiennik 6 | Spec świadomie przyjmuje, że sekret trafi do logów IIS. Twierdzenie, że aplikacja go nie loguje, jest niezgodne z `appsettings.json` i logowaniem hostingu (`Information`). |
| Wyzwolenie, Endpoints | PRD sekcja 12 D-08, SF-1 „Do ustalenia” pkt 2 | „Bezpieczne uwierzytelnienie wywołań” to otwarty element D-08. Spec rozstrzyga go sama, bez decyzji właściciela. `rules.md` zabrania implementować obszar zablokowany niezatwierdzoną decyzją, a spec nie wyjaśnia, dlaczego ten zakaz jej nie dotyczy. |
| Endpoints, Acceptance Criteria (`404` przy braku tokenu) | `SecuritySetup` (fallback policy), `conventions.md` „Access and security” | Ścieżka bez endpointu daje `401`, a nie `404`. `UseStatusCodePages` dodaje treść do `404`. |
| Context, Storage | `conventions.md` „Naming”, „Data access”, „Structure” | Schemat spoza listy modułów, tabela bez modułu-właściciela, DbContext i BackgroundService w Shared, endpointy w hoście. Uzasadnienie istnieje w Assumptions, ale spec nie przewiduje aktualizacji `conventions.md`. |
| Assumptions (`DbContextSchemaTests`) | `api/tests/StoneFlow.Architecture.Tests/Modules.cs` | Test jest sparametryzowany listą modułów z czterema warstwami. Kontekstu Shared nie da się „dopisać” bez osobnego testu. |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | Not applicable | Mechanizm nie dotyka sprzedaży, wpłat ani wydatków. |
| 2. Backend decimal money | Not applicable | Brak kwot. |
| 3. API-enforced access | OK | `GET /api/jobs` wymaga sesji. Wyzwalacz anonimowy niczego nie zwraca i nie przyjmuje parametrów. Brakuje wpisu w `EndpointSecurityTests` (Non-Critical 10). |
| 4. Retries never duplicate | OK | Spec przenosi unikalność na SF-13 i SF-16 zgodnie z niezmiennikiem. Gwarancję „jeden przebieg” osłabia jednak brak fencingu (Critical 2). |
| 5. Financial audit trail | Not applicable | Mechanizm nie zmienia danych finansowych. Autor systemowy jest dostępny dla zadań (`AuditAuthor.AutomatedJob`, `WooCommerceSync` istnieją w `IAuditLog.cs`). |
| 6. Secrets stay server-side | Violated | Token w ścieżce trafia do logów IIS (spec to przyznaje) i, przy obecnej konfiguracji, do logów stdout aplikacji (Critical 1). |
| 7. Module and layer boundaries | OK | Kontrakt w `Shared.Application` bez EF Core i HTTP, implementacja w `Shared.Infrastructure`, moduły nie znają tabel `scheduling`. Zgodne z testami `LayerDependencyTests` i `ProjectReferenceTests`. |
| 8. No identifying data in docs | OK | Instrukcja używa `<adres-api>` i `<token>`. |

## Risk

- Recorded: medium
- Assessed: medium. Mechanizm sam nie zmienia pieniędzy ani danych klientów, ale jest współdzielony przez SF-13 i SF-16 (oba `high`). Jego błąd (brak przebiegu, podwójny przebieg) przenosi się na import zamówień i koszty. Przegląd kodu musi uwzględnić obu konsumentów, jak zapisano w specyfikacji.

## Assumptions Detected

| Założenie | Czy spec je podaje |
|---|---|
| Harmonogram hostingu nie wysyła nagłówków, więc token musi być w ścieżce | Tak (Assumptions) |
| Ryzyko tokenu w logach IIS jest akceptowalne | Tak, ale bez decyzji właściciela, w konflikcie z niezmiennikiem 6 |
| Aplikacja nie loguje ścieżek żądań lub da się to przestawić na szablon | Nie. Założenie ukryte i niezgodne z kodem (`appsettings.json`, logi hostingu, `stdoutLogEnabled`) |
| Proces nie jest usypiany w ciągu kilku minut po żądaniu (limit IIS ~20 min) | Tak, do potwierdzenia na hostingu. Brak powiązania z `MaxDuration` 30 min |
| Brakujący token da `404` | Nie. Ukryte i błędne (fallback policy → `401`) |
| Zadania konsumentów są idempotentne i znoszą ponowny przebieg | Tak (Invariant impact 4) |
| `LastSuccessStartedAt` z harmonogramu wystarcza konsumentom do nadrabiania | Częściowo. Dla SF-13 nie wystarcza (ręczne „Synchronizuj teraz”, inna definicja sukcesu) |
| Istniejące testy integracyjne (środowisko `Production`) działają bez tokenu | Nie. Ukryte i błędne |
| Token zostanie skonfigurowany na serwerze przed publikacją | Nie. Ukryte |
| Timer i wyzwolenie przy starcie nie zakłócają testów | Nie. Ukryte |
| Zegary instancji są zgodne, bo dzierżawa liczona jest z `TimeProvider` aplikacji, a nie z czasu SQL | Nie. Ukryte (przy jednym serwerze ryzyko małe) |
| Dzienny harmonogram o stałej godzinie wystarcza SF-16 | Tak (Assumptions) |
| Historia przebiegów nie wymaga retencji | Tak (Assumptions) |

## Recommendation

- Revise specification. Przed planowaniem trzeba (1) rozwiązać konflikt z niezmiennikiem 6 i otwartym punktem D-08: albo decyzja właściciela, albo wyzwalacz bez sekretu w URL i w logach, z uzasadnieniem, po co wyzwalacz z tokenem, skoro budzi każde żądanie, a sprawdzenie robią start i timer; oraz (2) opisać zachowanie przy utracie dzierżawy i warunkowe zakończenie przebiegu. Uwagi niekrytyczne warto poprawić w tej samej rewizji.
