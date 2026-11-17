# Code review — SF-6

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Implementacja pokrywa specyfikację i plan, a wszystkie kontrole przechodzą (backend: format, build, 179/179 testów na LocalDB; frontend: format, lint, typecheck, build, 200/200 testów). Rdzeń bezpieczeństwa jest poprawny: autoryzacja przed CSRF, polityka fallback z listą wyjątków sprawdzaną testem, globalny CSRF (także dla logowania), atrybuty ciasteczek, 60 min / 12 h / znacznik przy każdym żądaniu, token resetu 256 bit z SHA-256 i warunkowym `UPDATE`, link we fragmencie, ścisła walidacja `powrot`. Nie ma naruszeń niezmienników ani problemów krytycznych. Zostają problemy niekrytyczne: prawdopodobny wyciek adresu e-mail do logu przy wyścigu tworzenia konta, nieobsłużony wynik rotacji znacznika przy wylogowaniu, rozbieżność kodu z nową konwencją (`Observable`) i brakujące testy z planu.
- Risk: high (high: human review required before merge). Przegląd przez człowieka przed merge jest obowiązkowy niezależnie od wyniku. Lista kontrolna z planu (B9, „Verification”) nadal obowiązuje, w tym jawne potwierdzenie decyzji właściciela z przeglądu specyfikacji, pkt 14.

## Project Invariant Violations

None.

| Niezmiennik | Status | Uwagi |
|---|---|---|
| 1. Sprzedaż oddzielona od gotówki | Nie dotyczy | Brak danych finansowych. |
| 2. Kwoty decimal na backendzie | Nie dotyczy | Brak kwot. |
| 3. Dostęp wymuszany przez API | OK | `api/src/StoneFlow.Api/Security/SecuritySetup.cs:16-20` ustawia `DefaultPolicy` i `FallbackPolicy` = `RequireAuthenticatedUser`. Kolejność potoku jest zgodna z konwencją (`Program.cs:37-45`, autoryzacja przed CSRF). `AllowAnonymous` mają tylko endpointy z pkt 22 (`AuthEndpoints.cs:32-38`, `Program.cs:49,52`). Testy `EndpointSecurityTests.cs:27-72` porównują listę endpointów anonimowych (Production i Development) i sprawdzają `401` dla każdego chronionego endpointu oraz dla nieznanej ścieżki. Strażnicy we froncie są tylko wygodą. |
| 4. Ponowienia bez duplikatów | OK (w zakresie konta) | Unikalny `NormalizedEmail` (`AccessDbContext.cs:38`), konto tworzone tylko gdy w bazie nie ma żadnego (`RequestPasswordReset.cs:78-88`), obsługa wyścigu (`AccessUserStore.cs:32-36`, `IdentityAccountService.cs:41-45`). Testy: `PasswordResetTests.cs:69-82` i `:48-66`. |
| 5. Audyt zmian finansowych | Nie dotyczy | Zdarzenia bezpieczeństwa idą do logów (`SecurityEventLog.cs`). |
| 6. Sekrety po stronie serwera | OK | Baza trzyma tylko hash SHA-256 tokenu (`ResetToken.cs:18-22`, test `PasswordResetTests.cs:85-100`), hasło jest hashem Identity, hasło SMTP jest poza `appsettings` (test `AccessStartupTests.cs:254-261`), tryb `Log` działa tylko w Development (`AccessOptions.cs:115-118`), komunikaty walidacji nie zawierają wartości, w paczce frontu nie ma absolutnego adresu API (sprawdzone w `dist/`), a `SmtpEmailSender` loguje tylko typ błędu. Adres e-mail w logu (Correctness 1) dotyczy danych osobowych, nie sekretów. |
| 7. Granice modułów i warstw | OK | 4 projekty `Access`. Domain zależy tylko od `Shared.Kernel` i BCL, Application nie ma pakietów, Identity, EF i HTTP są w Infrastructure i Api. Testy architektury obejmują `Access` (`Modules.cs`, `DbContextSchemaTests.cs`) i przechodzą. |
| 8. Brak danych identyfikujących | OK | Wszędzie są placeholdery (`owner@example.com`, `app.example.com`, `<owner@example.com>` w `deploy/api.env.example`). Treść e-maila nie zawiera nazwy sklepu (`PasswordResetEmail.cs:27-32`). |

## Conventions Violations

### Critical (must fix before merge)

None.

### Non-Critical (should fix)

1. `app/src/app/core/auth/auth-api.ts:32-54`: `AuthApi` zwraca `Promise` (`firstValueFrom`), a konwencja dopisana w tym zadaniu (`.sdd/context/conventions.md:156`) mówi: „One client per area (`*-api.ts`) returns `Observable`s of typed DTOs”. Trzeba ujednolicić kod albo konwencję, bo następne obszary skopiują jedno z nich.
2. `api/src/Modules/Access/StoneFlow.Access.Api/Endpoints/AuthEndpoints.cs:54,72`: handlery endpointów wołają `XsrfCookie.Issue`. Konwencja (`conventions.md:64`) mówi „call only `IDispatcher` and map the result to HTTP”. Wydanie ciasteczka to sprawa HTTP, więc to odstępstwo jest uzasadnione, ale konwencja powinna je nazwać (wyjątek dla wydawania tokenu CSRF).
3. `api/src/Modules/Access/StoneFlow.Access.Api/AccessModule.cs:40-41`: limity per IP (10/min, 3/15 min) są literałami w rejestracji. Limit na konto ma stałe domenowe (`PasswordResetLimits`), limity per IP ich nie mają. Spec, „Założenia”, nazywa je „wartościami startowymi w konfiguracji kodu”, więc wystarczą nazwane stałe.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| 1. `Owner:Email` wymagany, błąd startu ze wskazaniem klucza | Covered | `AccessOptions.cs:65-80`; `AccessStartupTests.cs:193-207` (także brak wartości w komunikacie). |
| 2–3. Pierwsze konto tylko dla adresu właściciela, przez reset, bez hasła | Covered | `RequestPasswordReset.cs:78-88`; testy `PasswordResetTests.cs:15-66`, `PasswordResetHandlerTests`. |
| 4. Ekran logowania, pola wymagane (UI i API) | Covered | `login-page.html`, `Login.cs:29-43`; testy w obu warstwach. |
| 5. Sesja i przekierowanie do `/pulpit` lub `powrot` | Covered | `login-page.ts:71-73`, `return-url.ts`. |
| 6. Jeden komunikat dla błędnego hasła, nieznanego konta, konta bez hasła i blokady | Covered | `Login.cs:46-68`, `AuthProblems.cs`; test `LoginAndSessionTests.cs:28-45`. Różnica czasu przy zablokowanym koncie: Correctness 3. |
| 7. Blokada 5/15 min, udane logowanie zeruje licznik | Partial | Blokada działa i jest testowana (`LoginAndSessionTests.cs:47-66`). Zerowanie licznika po udanym logowaniu i odblokowanie po upływie 15 min nie mają testu (Test Gaps 1). |
| 8. 429 powyżej 10 logowań z IP na minutę | Covered | `AccessModule.cs:40`; `EndpointSecurityTests.cs:116-141`. |
| 9. Zalogowany na `/logowanie` → `/pulpit` | Covered | `auth-guards.ts:24-29`; `auth-guards.spec.ts:73`. |
| 10. Atrybuty ciasteczka sesji | Covered | `DependencyInjection.cs:103-116`; test Production `LoginAndSessionTests.cs:102-132`. Wariant Development bez testu (Test Gaps 2). |
| 11. 60 min bezczynności (przesuwane), maksymalnie 12 h | Covered | `SessionCookie.cs:41-58` (`ShouldRenew`, claim `sf:auth_time`); testy `LoginAndSessionTests.cs:188-230`. |
| 12. Wygasła sesja → 401 bez przekierowania; front: „Sesja wygasła…” | Covered | `SessionCookie.cs:26,66-71`; `unauthorized-interceptor.ts`, `auth-session.ts:84-95`, `auth-messages.ts:31`. |
| 13. Wyloguj → 204, usunięte ciasteczko, „Wylogowano.” | Covered | `AuthEndpoints.cs:63-74`, `auth-session.ts:66-78`; testy `LoginAndSessionTests.cs:83-100`, `user-menu.spec.ts:48`. Obsługa błędu rotacji: Correctness 2. |
| 14. Nowe hasło unieważnia wszystkie sesje (znacznik przy każdym żądaniu) | Covered | `IdentityAccountService.cs:94-95`, `SessionCookie.cs:48`; test `PasswordResetTests.cs:209-235`. |
| 15. `/reset-hasla`, zawsze 202 i stały komunikat | Covered | `RequestPasswordReset.cs`, `password-reset-request-page.*`. |
| 16. Token ≥ 256 bit, SHA-256, 30 min, unieważnienie poprzednich, link we fragmencie bez e-maila, usunięcie fragmentu przed żądaniem | Covered | `ResetToken.cs`, `PasswordResetToken.cs:11,45`, `PasswordResetTokenStore.cs:17-31`, `PasswordResetEmail.cs:14-21`, `set-password-page.ts:76-83`; testy backendu i `set-password-page.spec.ts:86-92`. |
| 17. 3 prośby z IP / 15 min → 429; 3 na konto → bez e-maila, 202 | Covered | `AccessModule.cs:41`, `RequestPasswordReset.cs:53-58`; testy `EndpointSecurityTests.cs:143-158`, `PasswordResetTests.cs:237-250`. Wyścig limitu na konto: Correctness 6. |
| 18. Nowe hasło ≥ 12 znaków, różne od e-maila, zgodność pól w UI | Covered | `PasswordPolicy.cs`, `set-password-page.ts:59-69`. |
| 19. Poprawny token: hasło, token zużyty, odblokowanie, unieważnienie sesji, bez auto-logowania | Covered | `ConfirmPasswordReset.cs:63-94`, `IdentityAccountService.cs:74-96`; test `PasswordResetTests.cs:209-235`. |
| 20. Token nieważny → 400 `reset_link_invalid`; słabe hasło → błąd pola | Covered | `AuthEndpoints.cs:104-110`; testy `PasswordResetTests.cs:84-188`. |
| 21. Domyślnie wymagana sesja, także dla odczytów | Covered | Patrz niezmiennik 3. |
| 22. Lista wyjątków sprawdzana testem | Covered | `EndpointSecurityTests.cs:17-44`. |
| 23. `GET /api/auth/session` → `{ email }` | Covered | `AuthEndpoints.cs:76-86`. |
| 24. CSRF dla każdego POST/PUT/PATCH/DELETE pod `/api`, także logowania; 400 „Odśwież…”; nowy token po zalogowaniu i wylogowaniu | Covered | `SecuritySetup.cs:54-73`, `AuthEndpoints.cs:54,72`; testy `EndpointSecurityTests.cs:74-95`, `LoginAndSessionTests.cs:154-171`. Brak testu na nieznaną ścieżkę `/api` z sesją (Test Gaps 3). |
| 25. CORS bez dozwolonych originów | Covered | `SecuritySetup.cs:22`; `EndpointSecurityTests.cs:97-114`. |
| 26. Trasy chronione, `powrot` tylko wewnętrzny, ekrany auth poza powłoką | Covered | `app.routes.ts:65-75`, `return-url.ts`; `return-url.spec.ts`. |
| 27. Stan ładowania do odpowiedzi `session` | Covered | `app.html:1-5`, `auth-guards.ts:13`; `app.spec.ts:18`. |
| 28. Każde 401 poza logowaniem → `expire()` | Covered | `unauthorized-interceptor.ts:12-26`. |
| 29. E-mail i „Wyloguj” w nagłówku | Covered | `user-menu.*`, `app-shell.html:21`. |
| 30. Etykiety, błędy przy polach, stany zapisu, blokada podwójnego wysłania, czyszczenie hasła po błędzie | Covered | Trzy ekrany i ich specyfikacje. |
| 31. Logi zdarzeń tylko z id konta i korelacją; bez e-maila, hasła i tokenu | Partial | `SecurityEventLog.cs` loguje tylko id. Ścieżka wyścigu przy tworzeniu konta prawdopodobnie zapisuje e-mail w logu EF Core (Correctness 1). Korelacja zdarzeń bez testu (Test Gaps 4). |
| Przypadek brzegowy: 503 przy niedostępnej bazie | Covered | `SecuritySetup.cs:28-44`; `ProblemDetailsAndCorrelationTests.cs:129-141` (na `/session`, nie na logowaniu, jak w planie). |
| Przypadek brzegowy: restart API (klucze w bazie) | Covered | `LoginAndSessionTests.cs:134-152`. |
| Przypadek brzegowy: błąd SMTP → 202, log bez treści | Covered | `PasswordResetTests.cs:252-269`. |
| Przypadek brzegowy: konfiguracja SMTP, `Mode=Log` poza Development | Covered | `AccessStartupTests.cs`. |
| Zaległości SF-3: `detail` tylko dla nieobsłużonych wyjątków; dyspozytor bez `TargetInvocationException` | Covered | `ProblemDetailsSetup.cs:19-26`, `Dispatcher.cs:42-47`; testy `ProblemDetailsAndCorrelationTests.cs:117-127`, `DispatcherTests.cs:50-64`. |
| README (właściciel, SMTP, proxy, link w panelu sklepu) i `conventions.md` | Covered | README, `deploy/api.env.example`, `conventions.md:62-84,149-176`. |
| Kryterium: sesja między domenami na wdrożonym środowisku | Missing (zgodnie z planem) | Otwarte do SF-4. |

## Plan Deviations

Odstępstwa zgłoszone przez autora (potwierdzone w kodzie):
- Zamiast `SecurityStampValidator` znacznik sprawdza `SignInManager.ValidateSecurityStampAsync` wewnątrz `OnValidatePrincipal` (`SessionCookie.cs:41-58`). Nie ma `OnRefreshingPrincipal`, bo principal się nie zmienia.
- `IdentitySessionManager` używa `CreateUserPrincipalAsync` i `HttpContext.SignInAsync` zamiast `SignInWithClaimsAsync` (`SessionCookie.cs:79-96`).
- Testy integracyjne są w `Access/LoginAndSessionTests.cs`, `Access/PasswordResetTests.cs`, `Access/AccessStartupTests.cs` i `Security/EndpointSecurityTests.cs` zamiast 14 plików z planu.
- Filtry testowe są połączone w `TestRequestStartupFilter` (`TestSupport.cs:18-45`, tylko w `api/tests`).
- Każdy test ma osobną bazę (`SqlServerFixture.NewDatabase`) zamiast `ResetAccessAsync`.
- Frontend: układ `AppShell` jest przed `AuthLayout` (`app.routes.ts:65-75`). `AuthSession` orkiestruje też wywołania resetu (`auth-session.ts:97-115`). Stan ładowania jest nad outletem (`app.html`). `main-nav.spec` używa `sectionRoutes`.

Odstępstwa niewymienione przez autora:
- Pliki z planu połączono w mniej plików. Zachowanie jest takie samo:
  - `Abstractions/I*.cs` i `AccessSettings.cs` → `Abstractions/Ports.cs`;
  - `ResetLink.cs` → `PasswordResetEmail.Link`;
  - pliki komend, handlerów i wyników → `Login/Login.cs`, `Session/Session.cs` (z wylogowaniem), `PasswordReset/RequestPasswordReset.cs`, `PasswordReset/ConfirmPasswordReset.cs`;
  - `Persistence/AccessUser.cs` i `Configurations/*.cs` → `AccessDbContext.cs`;
  - `AccessUnitOfWork` → `PasswordResetTokenStore.cs`;
  - `IdentitySetup.cs` i `ConfigureSessionCookie.cs` → `DependencyInjection.cs`;
  - `AccessClaimTypes`, `SessionCookieEvents` i `IdentitySessionManager` → `Identity/SessionCookie.cs`;
  - `Options/*` → `Options/AccessOptions.cs`;
  - `Email/*` → `Email/EmailSenders.cs`;
  - w `Access.Api`: `Contracts/*`, `Csrf/ConfigureAntiforgery.cs`, `RateLimiting/AuthRateLimiting.cs` i `CurrentAccount.cs` → `AccessModule.cs` i `Endpoints/AuthEndpoints.cs`; `XsrfCookie.cs` leży w katalogu głównym projektu, nie w `Csrf/`;
  - `Observability/ExceptionStatusCodes.cs` i `Security/CsrfValidationMiddleware.cs` → `Security/SecuritySetup.cs`.
- `TimeProvider.System` jest rejestrowany w `AddAccessInfrastructure` (`DependencyInjection.cs:38`), a nie w `StoneFlow.Shared.Infrastructure/DependencyInjection.cs`, który plan kazał zmienić. Ten plik nie jest zmieniony.
- `TestDatabase.cs` i `CapturingEmailSender.cs` → `TestSupport.cs`. Testy jednostkowe są połączone w `DomainRulesTests.cs`, `PasswordResetHandlerTests.cs`, `OptionsValidatorTests.cs` i `Application/Fakes.cs`.
- Nowe pliki spoza planu: `app/src/testing/auth-testing.ts` i zmiana `app/tsconfig.app.json` (wykluczenie `src/testing/**`). Plan nie wymienia też `System.Security.Cryptography.Pkcs` 10.0.14 w `Directory.Packages.props`; to przypięcie poprawionej wersji pakietu przechodniego, z komentarzem.
- Test 503 woła `GET /api/auth/session` z nagłówkiem testowym zamiast logowania (plan B8, `DatabaseUnavailableTests.Login_returns_503_without_detail`).
- Pętli 30 przebiegów `LaunchDateStartupTests` w CI (plan B9) nie da się zweryfikować w repozytorium. Commit `ff86a54` zawiera samą poprawkę.

## Null Safety Issues

1. `api/src/Modules/Access/StoneFlow.Access.Infrastructure/DependencyInjection.cs:76-77`: wynik `AppOptionsValidator.TryParse` jest pomijany, a `baseUrl` może być `null` mimo typu `Uri`. `owner.Email ?? string.Empty` po cichu podstawia pusty adres. Dziś chroni przed tym walidacja przy odczycie `IOptions.Value`, ale wynik powinien być sprawdzony jawnie (`?? throw`).
2. `api/src/Modules/Access/StoneFlow.Access.Infrastructure/Options/AccessOptions.cs:99`: `out uri!` deklaruje niepusty `Uri` także wtedy, gdy `TryCreate` zawiedzie. Lepsza byłaby sygnatura `[NotNullWhen(true)] out Uri? uri`.

## Correctness Issues

1. **Prawdopodobny e-mail w logu przy wyścigu tworzenia konta.** `api/src/Modules/Access/StoneFlow.Access.Infrastructure/Identity/AccessUserStore.cs:29-36`: przy naruszeniu unikalności EF Core loguje `SaveChangesFailed` na poziomie Error, zanim `catch` przechwyci `DbUpdateException`. Treść `SqlException` zawiera wartość zduplikowanego klucza, czyli `NormalizedEmail` właściciela. To przeczy pkt 31 i kryterium „Logi nie zawierają … adresów e-mail”. Ścieżka zachodzi tylko przy równoczesnych pierwszych prośbach. `Concurrent_first_requests_create_one_account` jej nie sprawdza w logach. Do potwierdzenia testem i do wyciszenia (np. przez blokadę lub `UPDLOCK`, sprawdzenie przed zapisem w transakcji serializowalnej albo filtr kategorii logu).
2. `api/src/Modules/Access/StoneFlow.Access.Infrastructure/Identity/IdentityAccountService.cs:98-105`: `RotateSecurityStampAsync` ignoruje `IdentityResult` z `UpdateSecurityStampAsync`. Przy `ConcurrencyFailure` (np. równoległa nieudana próba logowania aktualizuje ten sam wiersz) znacznik się nie zmienia, a wylogowanie i tak zwraca `204` i loguje „wszystkie sesje unieważnione”. Skopiowane ciasteczko zostaje wtedy ważne, wbrew decyzji 2 planu.
3. `api/src/Modules/Access/StoneFlow.Access.Infrastructure/Identity/IdentityAccountService.cs:62-63`: dla zablokowanego konta `CheckPasswordSignInAsync` kończy się przed liczeniem hasha. Odpowiedź jest więc szybsza, a blokada istnieje tylko dla istniejącego konta. Treść odpowiedzi jest identyczna (pkt 6), ale czas zdradza, że adres należy do konta. Plan (decyzja 11) wyrównuje czas tylko dla braku konta i konta bez hasła. Do dopisania fikcyjnej weryfikacji przy blokadzie albo do jawnego przyjęcia ryzyka.
4. `api/src/Modules/Access/StoneFlow.Access.Application/PasswordReset/ConfirmPasswordReset.cs:76-84`: każda porażka `SetPasswordAsync`, także `ConcurrencyFailure`, daje komunikat „Nie udało się ustawić tego hasła. Wybierz inne.”. To mylący komunikat dla błędu współbieżności. Token zostaje wtedy poprawnie ważny (rollback).
5. `api/src/Modules/Access/StoneFlow.Access.Application/PasswordReset/ConfirmPasswordReset.cs:52-61` z `AuthEndpoints.cs:38`: polityka hasła jest sprawdzana przed tokenem, a `confirm` nie ma limitu żądań. Kto zna identyfikator konta (jest tylko w linku), może bez limitu sprawdzać, czy dany tekst jest adresem e-mail konta (`SameAsEmail` → błąd pola, inny tekst → `reset_link_invalid`). Wpływ jest niski, bo identyfikator jest losowy (GUID v7), ale warto rozważyć limit `auth-reset` także na `confirm`.
6. `api/src/Modules/Access/StoneFlow.Access.Application/PasswordReset/RequestPasswordReset.cs:53-64`: limit na konto to odczyt, a potem zapis, bez blokady. Równoczesne prośby z różnych IP mogą wysłać więcej niż 3 e-maile w oknie 15 min. Wpływ niski.
7. `api/src/Modules/Access/StoneFlow.Access.Infrastructure/Email/EmailSenders.cs:37`: przy `UseStartTls=false` używany jest `SecureSocketOptions.Auto`, czyli dla portów innych niż 465 `StartTlsWhenAvailable`. Jeśli serwer nie ogłosi STARTTLS, MailKit uwierzytelni się otwartym tekstem. Bezpieczniej: `SslOnConnect` albo jawne `None` tylko w Development.
8. `app/src/app/features/auth/auth-messages.ts:52`: nieznany problem bez `detail` (np. 403 albo 404) pokazuje „Odśwież stronę i spróbuj ponownie.”, czyli tekst przeznaczony dla CSRF. Drobne.

## Test Gaps

1. Brak testów z planu B8: `Lockout_expired_allows_login_and_success_resets_counter` (zerowanie licznika i odblokowanie po 15 min, pkt 7), jednostkowych `LoginHandlerTests`, `LogoutHandlerTests` i `SecurityEventLogTests`, a także `DispatcherTests.Stack_trace_contains_handler`.
2. Brak `Development_cookies_without_host_prefix` (nazwy `sf_session`/`sf_af`, brak `Secure` po HTTP). Proxy deweloperskie zależy od tego wariantu.
3. Brak `CsrfCoverageTests.Unknown_api_post_is_also_validated`: POST na nieznaną ścieżkę `/api` z sesją powinien dać 400 CSRF przed 404. Test pośrednio potwierdza globalność middleware niezależnie od mapowania endpointów.
4. Brak `SecurityLogTests.Security_events_logged_with_account_id_and_correlation`, czyli testu, że zdarzenia 6000–6014 mają `CorrelationId` w zakresie (pkt 31).
5. Brak `AccessStartupTests.Log_mode_in_development_logs_link`: jest tylko test, że start się udaje (`AccessStartupTests.cs:244-252`), a nie test, że link trafia do logu.
6. Test logów (`LoginAndSessionTests.cs:232-251`) nie obejmuje ścieżki wyścigu tworzenia konta ani blokady (Correctness 1). Brak testu, że rotacja znacznika przy wylogowaniu zawodzi głośno (Correctness 2).
7. `set-password-page.spec.ts:86-92` sprawdza `router.url`, a nie `Location`/historię przeglądarki. Ręczna weryfikacja z planu („pasek adresu bez `#…`”) pozostaje konieczna.

## Code Smells

1. `api/tests/StoneFlow.Api.IntegrationTests/Access/PasswordResetTests.cs:226`: `Task.Delay(10)` to pozostałość po `SecurityStampValidator` z interwałem. Przy obecnym sprawdzaniu znacznika przy każdym żądaniu jest zbędne i sugeruje zależność od czasu.
2. `api/src/Modules/Access/StoneFlow.Access.Api/Endpoints/AuthEndpoints.cs:113-114`: odczyt `ClaimTypes.NameIdentifier` jest prywatną metodą endpointów `Access`. Plan przewidywał `CurrentAccount.cs`, a konwencja (`conventions.md:79`) każe modułom brać id z tego claimu, więc kolejne moduły powielą tę logikę.
3. `app/src/app/features/auth/login-page.html:3-14`: komunikat `expired`/`logged-out`/`password-set` z `role="status"` jest renderowany razem z ekranem. Czytniki często nie ogłaszają treści regionu na żywo obecnej od początku, a fokus nie trafia na komunikat. Do sprawdzenia czytnikiem ekranu (dostępność, spec „UI”).
4. `app/src/app/layout/user-menu.html:1`: adres e-mail w nagłówku nie ma kontekstu dla czytnika (np. „Zalogowano jako”). Drobne.

## Recommendation

- Merge as-is. Wymagany jest przegląd przez człowieka przed merge (ryzyko high) z listą kontrolną planu. Poprawki niekrytyczne zalecane przed merge: Correctness 1–2, Conventions 1 oraz Test Gaps 1–4. Pozostałe mogą trafić do osobnego zadania.

### Weryfikacja wykonana w przeglądzie (04.11.2026)

| Polecenie | Wynik |
|---|---|
| `dotnet restore stone-flow.slnx --locked-mode` | OK |
| `dotnet format stone-flow.slnx --verify-no-changes --no-restore` | OK |
| `dotnet build stone-flow.slnx -c Release --no-restore` | OK, 0 ostrzeżeń, 0 błędów |
| `dotnet test --solution stone-flow.slnx -c Release --no-build` (`STONEFLOW_TEST_SQLSERVER` = LocalDB) | OK, 179/179 |
| `npm ci --ignore-scripts` (kopia `package.json`, `package-lock.json`, `.npmrc` w katalogu tymczasowym) | OK (npm audit: 18 moderate) |
| `npm run format:check`, `lint`, `typecheck`, `build` (w `app/`) | OK |
| `npm test -- --watch=false` | OK, 200/200 (25 plików) |
| Brak absolutnego adresu API w `app/dist` | OK (brak `localhost` w paczce) |

Testy na kontenerze SQL Server (CI) i ręczny przepływ end-to-end nie zostały uruchomione w tym przeglądzie.
