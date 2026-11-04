# Implementation done — SF-6

## Summary

Na gałęzi `sf-6-auth` dostarczono logowanie jedynym kontem właściciela, reset hasła, sesję i
ochronę API.

- **Backend:** nowy moduł `StoneFlow.Access` (4 warstwy, schemat `access`, migracja
  `InitialAccess`) na ASP.NET Core Identity z własnym magazynem jednej tabeli:
  - sesja w ciasteczku: 60 min bezczynności, 12 h od zalogowania, znacznik bezpieczeństwa przy
    każdym żądaniu; wylogowanie i reset kończą wszystkie sesje;
  - reset hasła: token 256 bit, w bazie tylko SHA-256, 30 min, jednorazowy, link we fragmencie;
  - domyślna polityka „wymagaj sesji” z jawną listą wyjątków, globalny CSRF dla `/api`, CORS bez
    originów, limity żądań, blokada 5 prób / 15 min;
  - SMTP (MailKit) albo log w Development; klucze Data Protection w bazie.
- **Frontend:** `HttpClient` z XSRF i względnym `/api` przez proxy; sesja na sygnałach; strażnicy
  tras; ekrany `/logowanie`, `/reset-hasla`, `/reset-hasla/nowe`; menu użytkownika z „Wyloguj”.
- **Zaległości SF-3:** `detail` Problem Details tylko dla błędów klienta, dyspozytor bez
  `TargetInvocationException`, deterministyczny test błędu startu.
- **Testy:** testy bazy na LocalDB przez `STONEFLOW_TEST_SQLSERVER` (CI: Testcontainers).

Przegląd specyfikacji: PASS WITH ISSUES. Przegląd kodu: PASS WITH ISSUES (bez naruszeń
niezmienników, bez uwag krytycznych); uwagi zalecane przed merge poprawione w `c9f09a5`.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| 1. `Owner:Email` wymagany, błąd startu ze wskazaniem klucza | Implemented | |
| 2–3. Pierwsze konto tylko dla adresu właściciela, przez reset, bez hasła; nigdy drugie konto | Implemented | |
| 4–5. Ekran logowania, walidacja w UI i API, przekierowanie do `/pulpit` / `powrot` | Implemented | |
| 6. Jeden komunikat dla błędnego hasła, nieznanego konta, konta bez hasła i blokady | Implemented | Czas odpowiedzi wyrównany (także przy blokadzie) |
| 7. Blokada 5 prób / 15 min, udane logowanie zeruje licznik | Implemented | |
| 8. 429 powyżej 10 logowań z IP / min | Implemented | Za proxy wymaga konfiguracji w SF-4 |
| 9. Zalogowany na `/logowanie` → `/pulpit` | Implemented | |
| 10. Ciasteczko sesji HttpOnly, Secure, Lax, `__Host-` poza Development | Implemented | |
| 11. 60 min bezczynności (przesuwane), maks. 12 h | Implemented | |
| 12. Wygasła sesja → 401, frontend „Sesja wygasła…” bez danych | Implemented | |
| 13. „Wyloguj” → 204, „Wylogowano.” | Implemented | Kończy sesje na wszystkich urządzeniach |
| 14. Nowe hasło unieważnia wszystkie sesje | Implemented | |
| 15–17. Prośba o reset: zawsze 202, token 256 bit / SHA-256 / 30 min, link we fragmencie, limity IP i konta | Implemented | Spec pkt 16 zmieniony na fragment (uwaga 7 przeglądu spec) |
| 18–20. Ustawienie hasła: 12+ znaków, różne od e-maila, jednorazowo, odblokowanie, bez auto-logowania; błędy `reset_link_invalid` / pole | Implemented | |
| 21–22. Domyślnie wymagana sesja, lista wyjątków sprawdzana testem | Implemented | |
| 23. `GET /api/auth/session` → `{ email }` | Implemented | |
| 24. CSRF dla każdego żądania zmieniającego stan pod `/api`, także logowania; rotacja tokenu | Implemented | |
| 25. CORS bez dozwolonych originów | Implemented | |
| 26–30. Ochrona tras, bezpieczny `powrot`, stan ładowania, obsługa 401, menu użytkownika, formularze | Implemented | |
| 31. Logi zdarzeń bezpieczeństwa tylko z id konta | Implemented | |
| Przypadki brzegowe: 503 przy niedostępnej bazie, restart API, błąd SMTP, konfiguracja, `Mode=Log` | Implemented | |
| README (właściciel, SMTP, proxy, link w panelu sklepu), `conventions.md`, `deploy/api.env.example` | Implemented | |
| Kryterium: sesja między domenami na wdrożonym środowisku | Not implemented | Otwarte do SF-4 (zgodnie ze spec) |

## Files

### Created

- **Moduł Access (backend):**
  - `api/src/Modules/Access/StoneFlow.Access.Domain/`: `Accounts/` (EmailAddress, PasswordPolicy,
    LockoutPolicy), `Sessions/SessionPolicy.cs`, `PasswordReset/` (PasswordResetToken,
    PasswordResetLimits);
  - `api/src/Modules/Access/StoneFlow.Access.Application/`: `Abstractions/Ports.cs`,
    `AccessMessages.cs`, `Login/Login.cs`, `Session/Session.cs`, `PasswordReset/` (ResetToken,
    PasswordResetEmail, RequestPasswordReset, ConfirmPasswordReset);
  - `api/src/Modules/Access/StoneFlow.Access.Infrastructure/`: `DependencyInjection.cs`,
    `Persistence/` (AccessDbContext, PasswordResetTokenStore, `Migrations/*_InitialAccess*`),
    `Identity/` (AccessUserStore, IdentityAccountService, SessionCookie), `Options/AccessOptions.cs`,
    `Email/EmailSenders.cs`, `Logging/SecurityEventLog.cs`;
  - `api/src/Modules/Access/StoneFlow.Access.Api/`: `AccessModule.cs`, `AuthProblems.cs`,
    `XsrfCookie.cs`, `Endpoints/AuthEndpoints.cs`;
  - `AssemblyReference.cs` i `packages.lock.json` w czterech projektach.
- **Host:** `api/src/StoneFlow.Api/Security/SecuritySetup.cs` (polityki, CORS, 503/400,
  `CsrfValidationMiddleware`).
- **Testy backendu:**
  - `api/tests/StoneFlow.Access.Tests/` (domena, token i link, handlery, walidatory);
  - `api/tests/StoneFlow.Api.IntegrationTests/Access/` (LoginAndSessionTests, PasswordResetTests,
    AccessStartupTests);
  - `api/tests/StoneFlow.Api.IntegrationTests/Security/EndpointSecurityTests.cs`;
  - `api/tests/StoneFlow.Api.IntegrationTests/Infrastructure/` (AuthClient, TestSupport,
    StartupFailure).
- **Frontend:**
  - `app/proxy.conf.mjs`;
  - `app/src/app/core/http/` (app-http, api-error);
  - `app/src/app/core/auth/` (auth-api, auth-session, auth-guards, unauthorized-interceptor,
    return-url);
  - `app/src/app/layout/` (auth-layout, user-menu);
  - `app/src/app/features/auth/` (auth-messages, reset-link, login-page, password-reset-request-page,
    set-password-page);
  - `app/src/testing/auth-testing.ts`;
  - specyfikacje testów.
- **Dokumenty:** `docs/tasks/sf-6/00-raw-task.md`, `02-spec.md`, `03-spec-review.md`,
  `04-implementation-plan.md`, `05-code-review.md`, `06-implementation-done.md`, `task.md`.

### Modified

- **Konfiguracja repozytorium:** `Directory.Packages.props` (MailKit 4.18.1,
  DataProtection.EntityFrameworkCore 10.0.14, TimeProvider.Testing 10.11.0, przypięty
  `System.Security.Cryptography.Pkcs` 10.0.14), `stone-flow.slnx`.
- **Host:** `api/src/StoneFlow.Api/Program.cs`, `StoneFlow.Api.csproj`, `appsettings.Development.json`,
  `Observability/ProblemDetailsSetup.cs`.
- **Część wspólna:** `api/src/Shared/StoneFlow.Shared.Infrastructure/Cqrs/Dispatcher.cs`.
- **Testy backendu:**
  - `api/tests/StoneFlow.Shared.Tests/Cqrs/DispatcherTests.cs`;
  - testy architektury (`Modules.cs`, csproj, `DbContextSchemaTests.cs`);
  - testy integracyjne (`StoneFlowApiFactory`, `SqlServerFixture`, `DatabaseTests`,
    `LaunchDateStartupTests`, `ProblemDetailsAndCorrelationTests`, `OpenApiTests`, csproj).
- **Frontend:** `app/angular.json`, `app/tsconfig.app.json`, `app/src/app/app.config.ts`,
  `app.routes.ts`, `app.ts`, `app.html`, `layout/app-shell.*` oraz specyfikacje SF-5.
- **Dokumentacja:** `.sdd/context/conventions.md`, `README.md`, `deploy/api.env.example`.

## Planned Units

| Item from the plan | Status |
|---|---|
| Moduł Access: 4 projekty, schemat `access`, `AccessDbContext`, migracja `InitialAccess` | Exists |
| Domain: EmailAddress, PasswordPolicy, LockoutPolicy, SessionPolicy, PasswordResetToken, PasswordResetLimits | Exists |
| Application: porty, ResetToken, link i treść e-maila, handlery Login, Logout, GetSession, RequestPasswordReset, ConfirmPasswordReset | Exists (pliki połączone, np. `Ports.cs`, `Session.cs`) |
| Infrastructure: AccessUserStore, IdentityAccountService, IdentitySessionManager, zdarzenia ciasteczka, opcje i walidatory, SMTP i Log, SecurityEventLog, PasswordResetTokenStore, AccessUnitOfWork, Data Protection w bazie | Exists |
| Access.Api: endpointy `/api/auth`, DTO, antiforgery, `XsrfCookie`, rate limiting, `AuthProblems` | Exists |
| Host: SecuritySetup, CsrfValidationMiddleware, StatusCodeSelector (503/400), kolejność potoku, poprawka `ProblemDetailsSetup` | Exists |
| Dispatcher (zaległość SF-3) | Exists |
| `LaunchDateStartupTests` + `StartupFailure` | Exists |
| `SqlServerFixture` z `STONEFLOW_TEST_SQLSERVER`, `TestDatabase` | Exists |
| Testy architektury z modułem Access | Exists |
| Frontend: proxy, `core/http`, `core/auth`, układy, ekrany auth, menu użytkownika | Exists |
| `conventions.md`, README, `deploy/api.env.example` | Exists |

## Database

SQL Server, nowy schemat `access`, migracja `20261104090207_InitialAccess`:

- `access.Users`: konto, unikalny `NormalizedEmail`, hash hasła, znacznik bezpieczeństwa,
  token współbieżności, licznik prób, koniec blokady;
- `access.PasswordResetTokens`: hash `binary(32)` unikalny, ważność, użycie, unieważnienie;
  FK do `Users`;
- `access.DataProtectionKeys`;
- historia migracji w `access.__EFMigrationsHistory`.

Bez tabel ról, claimów, loginów i tokenów Identity (sprawdzane testem). Pozostałe moduły bez
zmian.

## Tests

| Test from the plan | Status | Run result |
|---|---|---|
| Domena (EmailAddress, PasswordPolicy, SessionPolicy, PasswordResetToken) | Exists | Passed |
| ResetToken, link i treść e-maila | Exists | Passed |
| Handlery: RequestPasswordReset, ConfirmPasswordReset, Login, Logout | Exists | Passed |
| Walidatory opcji (Owner, App, Email; Log tylko w Development; komunikaty bez wartości) | Exists | Passed |
| SecurityEventLogTests (jednostkowy) | Not created | — (pokryte integracyjnie: brak e-maila, hasła i tokenu w logach) |
| Dispatcher: oryginalny wyjątek (synchroniczny i z zadania), ślad stosu | Exists | Passed |
| Lista endpointów anonimowych (Production, Development) | Exists | Passed |
| Każdy chroniony endpoint → 401 bez sesji; nieznana ścieżka → 401 | Exists | Passed |
| CSRF: każdy endpoint zmieniający stan, nieznana ścieżka `/api`, logowanie, token sprzed logowania, wygasła sesja → 401 | Exists | Passed |
| CORS bez nagłówków | Exists | Passed |
| Atrybuty ciasteczek (Production i Development) | Exists | Passed |
| Logowanie: sukces z normalizacją, ten sam 401, walidacja, blokada 5/15, wygaśnięcie blokady i zerowanie licznika | Exists | Passed |
| Sesja: 60 min bezczynności, przedłużanie, 12 h, restart API, wylogowanie i skopiowane ciasteczko | Exists | Passed |
| Reset: pierwsze konto, inny e-mail, zmiana `Owner:Email`, równoczesne prośby, jednorazowość, hash w bazie, 30 min, unieważnienie poprzedniego, zmieniony token, słabe hasło, równoczesne potwierdzenia, unieważnienie sesji i odblokowanie, limit konta, błąd SMTP, błędny JSON → 400 | Exists | Passed |
| Konfiguracja startu (Owner, BaseUrl, SMTP, Log poza Development) | Exists | Passed |
| Problem Details: 401 anonimowy, `detail` błędu klienta, 503 bez szczegółów | Exists | Passed |
| Migracje wszystkich modułów i zestaw tabel `access` | Exists | Passed |
| Korelacja w zdarzeniach bezpieczeństwa (test) | Not created | — |
| Frontend: api-error, auth-api, return-url, auth-session, interceptor, strażnicy, układ auth, fragment linku, trzy ekrany, menu użytkownika, testy SF-5 | Exists | Passed (25 plików, 200 testów) |

## Verification

Uruchomione 2026-11-04 (SDK 10.0.403, Node 24.21.0, npm 11.6.1).

| Command | Outcome |
|---|---|
| `dotnet tool restore` | Passed |
| `dotnet restore stone-flow.slnx --locked-mode` | Passed |
| `dotnet format stone-flow.slnx --verify-no-changes --no-restore` | Passed |
| `dotnet build stone-flow.slnx -c Release --no-restore` | Passed (0 ostrzeżeń) |
| `dotnet test --solution stone-flow.slnx -c Release --no-build` z `STONEFLOW_TEST_SQLSERVER` = LocalDB | Passed (186/186) |
| Testy bazy na SQL Server w kontenerze (CI) | Not run (wynik w CI PR) |
| `npm ci --ignore-scripts` w czystym katalogu | Passed |
| `npm ci` w `app/` | Not run (blokady plików na Windows; czysty katalog jak wyżej) |
| `npm run format:check`, `lint`, `typecheck`, `build` | Passed |
| `npm test -- --watch=false` | Passed (200/200) |
| Brak absolutnego adresu API w `dist/` | Passed |
| Ręcznie, HTTP przez proxy dev-servera (API Release w Development na LocalDB, `Mode=Log`) | Passed (szczegóły niżej) |
| Ręcznie w przeglądarce: ekrany, usunięcie fragmentu z paska adresu, klawiatura, 320 px | Not run (automatyzacja przeglądarki blokowana przez inne rozszerzenie Chrome; sprawdzone tylko przekierowanie `/wydatki` → `/logowanie?powrot=%2Fwydatki` i treść ekranu logowania) |

Ręczny przepływ przez proxy (`http://localhost:4400/api/...`):
- prośba o reset → 202 i link `…/reset-hasla/nowe#konto=…&token=<43 znaki>` w logu;
- ustawienie hasła → 204, ten sam link drugi raz → 400 `reset_link_invalid`;
- logowanie z `" Owner@Example.com "` → 204, `GET /session` → `{"email":"owner@example.com"}`;
- wylogowanie → 204, `GET /session` → 401;
- `POST` bez CSRF → 400 `csrf_invalid`, błędny JSON → 400 (wcześniej 500 — poprawione w
  `d346138`), żądanie z obcego originu → brak nagłówków CORS;
- w logach brak hasła, tokenu (poza treścią e-maila w trybie Log) i adresu właściciela.

## Deviations

- Spec pkt 16: link resetu we fragmencie (`#konto=…&token=…`) zamiast query string (decyzja po
  przeglądzie spec, uwaga 7; spec zaktualizowany).
- Znacznik bezpieczeństwa sprawdzany przez `SignInManager.ValidateSecurityStampAsync` w zdarzeniu
  ciasteczka zamiast `SecurityStampValidator`: wbudowany walidator wylogowuje schematy
  dwuskładnikowe i zewnętrzne, których moduł nie rejestruje (dawało 500).
- Logowanie sesji przez `CreateUserPrincipalAsync` + `HttpContext.SignInAsync` zamiast
  `SignInWithClaimsAsync`.
- Pliki zgrupowane inaczej niż w planie (np. `Ports.cs`, `Session.cs`, `SessionCookie.cs`,
  `AccessOptions.cs`, `EmailSenders.cs`, `SecuritySetup.cs` z middleware CSRF; testy integracyjne
  w czterech klasach; filtry testowe w `TestRequestStartupFilter`; baza na test zamiast
  `ResetAccessAsync`).
- `TimeProvider` rejestrowany w module Access (`TryAddSingleton`), nie w części wspólnej.
- Test 503 na `GET /api/auth/session` (z testowym użytkownikiem) zamiast na logowaniu.
- Frontend:
  - układ powłoki przed układem auth w trasach;
  - `AuthSession` orkiestruje także żądania resetu;
  - stan ładowania sesji nad outletem;
  - klient `auth-api` zwraca Promise (konwencja zaktualizowana).
- `System.Security.Cryptography.Pkcs` przypięty do 10.0.14 (przechodnio z MailKit; luki w 10.0.0).
- SMTP: `UseStartTls=false` oznacza TLS od początku połączenia (bez trybu nieszyfrowanego).

## Additional Implementation

Additional implementation not covered by the original documents:

- Rozszerzenie `code` w Problem Details (`invalid_credentials`, `csrf_invalid`,
  `reset_link_invalid`, `rate_limited`), po którym frontend rozróżnia błędy.
- `BadHttpRequestException` zachowuje swój status 4xx (wcześniej 500).
- EF nie loguje nieudanego zapisu w module Access (duplikat klucza zawierałby e-mail).
- Pomocnik testowy `src/testing/auth-testing.ts` we frontendzie (wyłączony z builda aplikacji).

## Open Items

1. **Human review required before merge** (ryzyko high). Lista kontrolna:
   - kolejność potoku w `Program.cs`;
   - lista `AllowAnonymous` i test `EndpointSecurityTests`;
   - brak e-maila, hasła i tokenu w logach;
   - filtry testowe tylko w `api/tests`;
   - atrybuty ciasteczek;
   - zachowanie wylogowania na wszystkich urządzeniach.
2. **Jawne potwierdzenie decyzji przez właściciela** (zbiorcza zgoda „lec z /sdd-spec”):
   - same-origin z przekierowaniem `/api/*` w SF-4;
   - odłożenie D-05;
   - konto tworzone przez reset;
   - limit 12 h, blokada 5/15 min;
   - wylogowanie wszystkich urządzeń;
   - link we fragmencie.
3. **SF-4:**
   - przekierowanie `/api/*` na Vercel, HTTPS;
   - zaufane proxy dla limitów żądań (inaczej limit na IP staje się wspólny);
   - szyfrowanie kluczy Data Protection;
   - weryfikacja sesji na wdrożonym środowisku (kryterium otwarte).
4. **D-05:** wybór dostawcy SMTP przed produkcją — bez niego reset (jedyna droga utworzenia konta)
   nie działa na produkcji.
5. **Ręczna kontrola w przeglądarce:** usunięcie fragmentu z paska i historii, klawiatura na trzech
   ekranach, 320 px z długim e-mailem, menedżer haseł.
6. **Code review — przyjęte ryzyka:**
   - limit resetu na konto nie jest atomowy;
   - `confirm` sprawdza siłę hasła przed tokenem, bez limitu żądań (można zgadywać e-mail przy
     znanym id konta, niski wpływ);
   - różnica czasu przy tworzeniu konta i synchronicznym SMTP.
7. **Code review — brakujące testy:** korelacja w zdarzeniach bezpieczeństwa, `SecurityEventLog`
   jednostkowo, link w logu w trybie Log, historia przeglądarki po usunięciu fragmentu.
8. **Testy bazy w kontenerze SQL Server 2022** potwierdzi CI; lokalnie uruchamiane na LocalDB
   (SQL Server 2019).
9. **`npm audit`:** 18 podatności moderate w zależnościach deweloperskich (bez zmian względem
   `main`).
