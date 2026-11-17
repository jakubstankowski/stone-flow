# Implementation Plan — SF-6

Tryb: plan fullstack (backend + frontend, nowy kontrakt `/api/auth`). Dwa plany specjalistów
powstały niezależnie (osobni agenci) na podstawie `02-spec.md` i `03-spec-review.md`, potem
zostały sprawdzone względem siebie. Bez podziału na części: kontrakt, backend i frontend są
ze sobą powiązane i dopiero razem dają działające logowanie; zmiana jest duża, ale jedna
część pozwala sprawdzić cały przepływ w jednym przeglądzie człowieka (ryzyko high).

## Scope

Moduł `Access` (Identity z własnym magazynem jednej tabeli użytkownika, sesja w ciasteczku
60 min / 12 h, rotacja znacznika przy wylogowaniu i resecie, CSRF globalnie, domyślna
polityka „wymagaj sesji”, CORS bez originów, rate limiting, reset hasła z tokenem
haszowanym i linkiem we fragmencie, SMTP przez MailKit / log w Development, klucze Data
Protection w bazie), poprawki zaległości SF-3 (Problem Details, dyspozytor, niestabilny
test startu), testy DB lokalnie na LocalDB (`STONEFLOW_TEST_SQLSERVER`), a we frontendzie:
`HttpClient` z XSRF, proxy `/api`, sesja na sygnałach, strażnicy tras, ekrany
`/logowanie`, `/reset-hasla`, `/reset-hasla/nowe`, menu użytkownika, przebudowa tras na
układy.

Rozstrzygnięcia uwag z `03-spec-review.md` (decyzje planisty, przekazane obu specjalistom):

| Uwaga | Decyzja |
|---|---|
| 1. Zmiana `Owner:Email` tworzy drugie konto | Konto powstaje przez reset tylko gdy w bazie nie ma żadnego konta i e-mail = `Owner:Email` |
| 2. Wylogowanie tylko usuwa ciasteczko | Wylogowanie rotuje znacznik bezpieczeństwa (wylogowuje wszystkie urządzenia; README) |
| 3. 401 vs 400 CSRF po wygaśnięciu | Potok: uwierzytelnianie i autoryzacja przed walidacją CSRF; frontend pobiera token po `401` |
| 4. Limit 12 h ginie przy odświeżeniu principala | Claim `sf:auth_time` sprawdzany w `OnValidatePrincipal` i kopiowany w `OnRefreshingPrincipal`; `ValidationInterval = 0` |
| 5. Limity per IP za proxy | Partycja po `RemoteIpAddress`; `ForwardedHeaders` i zaufane proxy → SF-4 (ryzyko) |
| 6. Blokada jako DoS | Ryzyko przyjęte; reset odblokowuje konto |
| 7. Token w query string | Link `…/reset-hasla/nowe#konto={id}&token={token}`; frontend czyści fragment z historii; spec pkt 16 zaktualizowany |
| 8. Współbieżność | Zużycie tokenu warunkowym `UPDATE` (1 wiersz); pierwsze konto: unikalny indeks + obsługa wyścigu |
| 9. Atrybuty ciasteczek CSRF | Antiforgery `__Host-sf_af` HttpOnly Strict; `XSRF-TOKEN` czytelne dla JS, Strict, `Path=/` |
| 10. Minimal API nie waliduje antiforgery | Globalny middleware dla POST/PUT/PATCH/DELETE pod `/api` + test wyliczający endpointy |
| 11. Rozróżnialność po czasie | Logowanie: fikcyjna weryfikacja hasha; reset: ryzyko przyjęte |
| 12. Klucze Data Protection | `SetApplicationName("StoneFlow")`; szyfrowanie kluczy → SF-4 / D-08 (ryzyko) |
| 13. D-05 | Dostawca musi być wybrany przed SF-4 (otwarty punkt) |
| 14. Potwierdzenia właściciela | Lista do jawnego potwierdzenia w opisie PR i w przeglądzie człowieka |
| 15. Kryteria bez 429, CSRF logowania, konfiguracji, `powrot`, 503 | Testy w tabelach backendu i frontendu |

## Backend Plan


### B1. Zakres

Plan obejmuje:
- nowy moduł `StoneFlow.Access.{Domain,Application,Infrastructure,Api}` ze schematem `access` i migracją `InitialAccess`;
- konto właściciela na ASP.NET Core Identity z własnym magazynem użytkownika, ograniczonym do jednej tabeli;
- sesję w ciasteczku: 60 min bezczynności, 12 h od zalogowania, znacznik bezpieczeństwa sprawdzany przy każdym żądaniu;
- CSRF sprawdzany globalnie, domyślną politykę autoryzacji z jawną listą wyjątków, CORS bez dozwolonych originów i limity żądań;
- reset hasła z tokenem, którego hash trzyma baza, oraz wysyłkę e-mail przez MailKit SMTP i `Log` dostępny tylko w Development;
- klucze Data Protection w bazie;
- poprawki zaległości SF-3: `ProblemDetailsSetup` i `Dispatcher`;
- poprawkę niestabilnego `LaunchDateStartupTests` jako osobny commit;
- testy DB na istniejącym SQL Server (LocalDB) przez zmienną `STONEFLOW_TEST_SQLSERVER`, w CI na Testcontainers;
- aktualizację testów architektury, `conventions.md`, README i `deploy/api.env.example`.

Warstwy: wyłącznie backend i dokumentacja backendu. Frontend planuje osobny specjalista. Kontrakt dla niego jest w sekcji 5.

### B2. Obszary, których dotyczy zmiana

| Warstwa | Moduł / obszar | Dlaczego |
|---|---|---|
| Domain | `StoneFlow.Access.Domain` | Reguły: normalizacja e-maila, polityka hasła, polityka sesji i blokady, encja `PasswordResetToken`, limity resetu |
| Application | `StoneFlow.Access.Application` | Komendy i zapytania (logowanie, wylogowanie, sesja, prośba o reset, potwierdzenie resetu), porty, budowa linku i treści e-maila, generowanie i hash tokenu |
| Infrastructure | `StoneFlow.Access.Infrastructure` | `AccessDbContext`, własny `IUserStore`, `UserManager`/`SignInManager`, ciasteczko sesji, `SecurityStampValidator`, Data Protection w EF, opcje z walidacją, SMTP/Log, logi zdarzeń bezpieczeństwa |
| Api (moduł) | `StoneFlow.Access.Api` | Minimal API `/api/auth`, kontrakty DTO, wydawanie `XSRF-TOKEN`, konfiguracja antiforgery, polityki rate limitingu, mapowanie wyników na Problem Details |
| Host | `api/src/StoneFlow.Api` | Kolejność potoku, domyślna polityka autoryzacji, CORS, globalny middleware CSRF, `503` dla niedostępnej bazy, poprawka `detail`, `AllowAnonymous` dla OpenAPI |
| Shared | `StoneFlow.Shared.Infrastructure` | `Dispatcher` przekazuje oryginalny wyjątek; rejestracja `TimeProvider.System` |
| Testy | Architecture, Shared, nowy `StoneFlow.Access.Tests`, Api.IntegrationTests | Moduł `Access` w testach architektury, testy jednostkowe i integracyjne, fixture SQL Server z LocalDB, poprawka testu startu |
| Docs | `.sdd/context/conventions.md`, `README.md`, `deploy/api.env.example` | Styl endpointów, moduł `Access`, konfiguracja, testy na LocalDB |

### B3. Pliki

#### Nowy moduł `api/src/Modules/Access/`

| Plik | Akcja | Cel |
|---|---|---|
| `StoneFlow.Access.Domain/StoneFlow.Access.Domain.csproj` | create | Referencja tylko do `StoneFlow.Shared.Kernel` |
| `StoneFlow.Access.Domain/AssemblyReference.cs` | create | Znacznik zestawu |
| `StoneFlow.Access.Domain/Accounts/EmailAddress.cs` | create | `Normalize(string)` = Trim + ToUpperInvariant; `IsValid(string)` przez `MailAddress.TryCreate` z wymogiem, że `Address` jest równy przyciętemu wejściu; `MaxLength = 256` |
| `StoneFlow.Access.Domain/Accounts/PasswordPolicy.cs` | create | `MinLength = 12`; `Check(password, email)` zwraca `PasswordViolation?` (`TooShort`, `SameAsEmail`; porównanie po przycięciu, bez wielkości liter) |
| `StoneFlow.Access.Domain/Accounts/LockoutPolicy.cs` | create | `MaxFailedAttempts = 5`, `Duration = 15 min` |
| `StoneFlow.Access.Domain/Sessions/SessionPolicy.cs` | create | `IdleTimeout = 60 min`, `AbsoluteLifetime = 12 h`, `IsWithinAbsoluteLifetime(authenticatedAt, now)` |
| `StoneFlow.Access.Domain/PasswordReset/PasswordResetToken.cs` | create | Encja: `Id`, `UserId`, `TokenHash` (32 B), `CreatedAtUtc`, `ExpiresAtUtc`, `ConsumedAtUtc?`, `RevokedAtUtc?`; `Issue(userId, hash, now)`, `Lifetime = 30 min` |
| `StoneFlow.Access.Domain/PasswordReset/PasswordResetLimits.cs` | create | `MaxRequestsPerAccount = 3`, `Window = 15 min` |
| `StoneFlow.Access.Application/StoneFlow.Access.Application.csproj` | create | Referencje do własnego Domain i `StoneFlow.Shared.Application`; bez pakietów |
| `StoneFlow.Access.Application/AssemblyReference.cs` | create | Znacznik zestawu |
| `StoneFlow.Access.Application/AccessSettings.cs` | create | `sealed record AccessSettings(string OwnerEmail, Uri AppBaseUrl)` z walidowanych opcji. Application nie widzi `IOptions`, bo nie ma pakietów |
| `StoneFlow.Access.Application/Abstractions/IAccountService.cs` | create | Port konta (sygnatury w sekcji 5) |
| `StoneFlow.Access.Application/Abstractions/ISessionManager.cs` | create | Port sesji: `SignInAsync(Guid accountId, DateTimeOffset authenticatedAt, ct)`, `SignOutAsync(ct)` |
| `StoneFlow.Access.Application/Abstractions/IPasswordResetTokenStore.cs` | create | `CountIssuedSinceAsync`, `ReplaceActiveAsync` (unieważnia stare i dodaje nowy), `TryConsumeAsync` (atomowo) |
| `StoneFlow.Access.Application/Abstractions/IAccessUnitOfWork.cs` | create | `ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>>, ct)` |
| `StoneFlow.Access.Application/Abstractions/IEmailSender.cs` | create | `Task<EmailDeliveryResult> SendAsync(EmailMessage, ct)`; `EmailMessage(To, Subject, TextBody)`; `EmailDeliveryResult { Sent, Failed }` |
| `StoneFlow.Access.Application/Abstractions/ISecurityEvents.cs` | create | Zdarzenia bezpieczeństwa z `Guid?` konta, bez e-maila, hasła i tokenu |
| `StoneFlow.Access.Application/PasswordReset/ResetToken.cs` | create | `Generate()` (32 B z `RandomNumberGenerator`, `Base64Url`), `Hash(string)` → SHA-256 (32 B), `TryHash` odrzuca złą długość lub znaki |
| `StoneFlow.Access.Application/PasswordReset/ResetLink.cs` | create | `{BaseUrl bez końcowego /}/reset-hasla/nowe#konto={id}&token={token}` (decyzja 7) |
| `StoneFlow.Access.Application/PasswordReset/PasswordResetEmail.cs` | create | Polska treść tekstowa: link, ważność 30 min, „jeśli to nie Ty, zignoruj”; bez nazwy sklepu |
| `StoneFlow.Access.Application/AccessMessages.cs` | create | Komunikaty pól walidacji (sekcja 5) |
| `StoneFlow.Access.Application/Login/LoginCommand.cs`, `LoginHandler.cs`, `LoginResult.cs` | create | Logowanie |
| `StoneFlow.Access.Application/Logout/LogoutCommand.cs`, `LogoutHandler.cs` | create | Wylogowanie z rotacją znacznika (decyzja 2) |
| `StoneFlow.Access.Application/Session/GetSessionQuery.cs`, `GetSessionHandler.cs`, `SessionDto.cs` | create | `{ email }` dla bieżącego konta |
| `StoneFlow.Access.Application/PasswordReset/RequestPasswordResetCommand.cs`, `RequestPasswordResetHandler.cs`, `RequestPasswordResetResult.cs` | create | Prośba o link, w tym utworzenie pierwszego konta |
| `StoneFlow.Access.Application/PasswordReset/ConfirmPasswordResetCommand.cs`, `ConfirmPasswordResetHandler.cs`, `ConfirmPasswordResetResult.cs` | create | Ustawienie hasła z tokenu |
| `StoneFlow.Access.Infrastructure/StoneFlow.Access.Infrastructure.csproj` | create | `FrameworkReference Microsoft.AspNetCore.App`; pakiety `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` i `MailKit`; referencje do własnego Application i `StoneFlow.Shared.Infrastructure` |
| `StoneFlow.Access.Infrastructure/AssemblyReference.cs` | create | Znacznik zestawu |
| `StoneFlow.Access.Infrastructure/DependencyInjection.cs` | create | `AddAccessInfrastructure()`: DbContext, Identity, cookie, Data Protection, opcje z `ValidateOnStart`, e-mail, porty, handlery |
| `StoneFlow.Access.Infrastructure/Persistence/AccessDbContext.cs` | create | `Schema = "access"`, `IDataProtectionKeyContext`, `DbSet`: `Users`, `PasswordResetTokens`, `DataProtectionKeys` |
| `StoneFlow.Access.Infrastructure/Persistence/AccessUser.cs` | create | Model użytkownika w kształcie Identity: tylko kolumny ze specyfikacji |
| `StoneFlow.Access.Infrastructure/Persistence/Configurations/AccessUserConfiguration.cs` | create | Tabela `access.Users`, unikalny `NormalizedEmail`, `ConcurrencyStamp` jako token współbieżności |
| `StoneFlow.Access.Infrastructure/Persistence/Configurations/PasswordResetTokenConfiguration.cs` | create | `binary(32)` unikalny, FK, indeks `(UserId, CreatedAtUtc)` |
| `StoneFlow.Access.Infrastructure/Persistence/Configurations/DataProtectionKeyConfiguration.cs` | create | Tabela `access.DataProtectionKeys` |
| `StoneFlow.Access.Infrastructure/Persistence/PasswordResetTokenStore.cs` | create | Implementacja portu; `TryConsumeAsync` przez `ExecuteUpdateAsync` |
| `StoneFlow.Access.Infrastructure/Persistence/AccessUnitOfWork.cs` | create | Transakcja na `AccessDbContext` |
| `StoneFlow.Access.Infrastructure/Persistence/Migrations/{ts}_InitialAccess.cs` + `.Designer.cs` + `AccessDbContextModelSnapshot.cs` | create | Migracja generowana przez `dotnet ef` |
| `StoneFlow.Access.Infrastructure/Identity/AccessUserStore.cs` | create | `IUserPasswordStore`, `IUserSecurityStampStore`, `IUserEmailStore`, `IUserLockoutStore` (bez claim/login/token/role) |
| `StoneFlow.Access.Infrastructure/Identity/IdentityAccountService.cs` | create | `IAccountService` przez `UserManager` i `SignInManager.CheckPasswordSignInAsync`, z wyrównaniem czasu |
| `StoneFlow.Access.Infrastructure/Identity/IdentitySessionManager.cs` | create | `ISessionManager`: `SignInWithClaimsAsync` + claim `sf:auth_time`; ustawia `HttpContext.User` po zalogowaniu i wylogowaniu |
| `StoneFlow.Access.Infrastructure/Identity/AccessClaimTypes.cs` | create | `AuthTime = "sf:auth_time"` |
| `StoneFlow.Access.Infrastructure/Identity/IdentitySetup.cs` | create | Opcje Identity (blokada, hasło, użytkownik), `AddSignInManager`, schemat cookie, `SecurityStampValidatorOptions` |
| `StoneFlow.Access.Infrastructure/Identity/ConfigureSessionCookie.cs` | create | `IConfigureNamedOptions<CookieAuthenticationOptions>`: atrybuty zależne od środowiska, `TimeProvider`, zdarzenia |
| `StoneFlow.Access.Infrastructure/Identity/SessionCookieEvents.cs` | create | `OnValidatePrincipal` (limit 12 h, potem znacznik, `ShouldRenew`), `OnRedirectToLogin` → 401, `OnRedirectToAccessDenied` → 403 |
| `StoneFlow.Access.Infrastructure/Options/OwnerOptions.cs`, `OwnerOptionsValidator.cs` | create | `StoneFlow:Owner:Email` |
| `StoneFlow.Access.Infrastructure/Options/AppOptions.cs`, `AppOptionsValidator.cs` | create | `StoneFlow:App:BaseUrl` (absolutny http/https, bez query i fragmentu) |
| `StoneFlow.Access.Infrastructure/Options/EmailOptions.cs`, `EmailOptionsValidator.cs` | create | `StoneFlow:Email:Mode` i `:Smtp:*`; `Log` tylko w Development |
| `StoneFlow.Access.Infrastructure/Email/SmtpEmailSender.cs` | create | MailKit; łapie błędy transportu, loguje typ błędu, zwraca `Failed` |
| `StoneFlow.Access.Infrastructure/Email/LogEmailSender.cs` | create | Development: loguje temat i treść z linkiem |
| `StoneFlow.Access.Infrastructure/Logging/SecurityEventLog.cs` | create | `ISecurityEvents` przez `[LoggerMessage]` (EventId 6000–6099) |
| `StoneFlow.Access.Api/StoneFlow.Access.Api.csproj` | create | `FrameworkReference Microsoft.AspNetCore.App`; tylko referencja do własnego Application; bez pakietów |
| `StoneFlow.Access.Api/AssemblyReference.cs` | create | Znacznik zestawu |
| `StoneFlow.Access.Api/AccessModule.cs` | create | `AddAccessModule()` (antiforgery, rate limiting), `MapAccessEndpoints()` (grupa `/api/auth`) |
| `StoneFlow.Access.Api/Endpoints/AuthEndpoints.cs` | create | Sześć endpointów; każdy woła dyspozytor i mapuje wynik |
| `StoneFlow.Access.Api/Contracts/LoginRequest.cs`, `PasswordResetRequest.cs`, `PasswordResetConfirmRequest.cs`, `SessionResponse.cs` | create | DTO (pola `string?`) |
| `StoneFlow.Access.Api/Csrf/ConfigureAntiforgery.cs` | create | `IConfigureOptions<AntiforgeryOptions>` zależne od środowiska (decyzja 9) |
| `StoneFlow.Access.Api/Csrf/XsrfCookie.cs` | create | `Issue(HttpContext)`: `GetAndStoreTokens` i ciasteczko `XSRF-TOKEN` |
| `StoneFlow.Access.Api/RateLimiting/AuthRateLimiting.cs` | create | Polityki `auth-login` (10/min/IP) i `auth-reset` (3/15 min/IP), `OnRejected` → 429 Problem Details |
| `StoneFlow.Access.Api/AuthProblems.cs` | create | Teksty i kody Problem Details |
| `StoneFlow.Access.Api/CurrentAccount.cs` | create | `Guid? From(ClaimsPrincipal)` z `ClaimTypes.NameIdentifier` |

#### Host, Shared, konfiguracja

| Plik | Akcja | Cel |
|---|---|---|
| `api/src/StoneFlow.Api/Program.cs` | modify | Rejestracja `Access`, bezpieczeństwo, jawna kolejność potoku, `MapOpenApi().AllowAnonymous()`, `MapAccessEndpoints()` |
| `api/src/StoneFlow.Api/StoneFlow.Api.csproj` | modify | Referencje do `StoneFlow.Access.Api` i `StoneFlow.Access.Infrastructure` |
| `api/src/StoneFlow.Api/Observability/ProblemDetailsSetup.cs` | modify | Usuwa `detail` i `exception` tylko gdy `context.Exception is not null` i poza Development |
| `api/src/StoneFlow.Api/Observability/ExceptionStatusCodes.cs` | create | `StatusCodeSelector`: `DbException` w łańcuchu `InnerException` → 503, inaczej 500 |
| `api/src/StoneFlow.Api/Security/SecuritySetup.cs` | create | `AddStoneFlowSecurity()`: `FallbackPolicy` i `DefaultPolicy` = `RequireAuthenticatedUser`; CORS `AddDefaultPolicy(p => p.SetIsOriginAllowed(_ => false))` |
| `api/src/StoneFlow.Api/Security/CsrfValidationMiddleware.cs` | create | `/api/*` + POST/PUT/PATCH/DELETE → `IAntiforgery.ValidateRequestAsync`; przy błędzie 400 Problem Details |
| `api/src/StoneFlow.Api/appsettings.Development.json` | modify | `StoneFlow:App:BaseUrl = http://localhost:4200`, `StoneFlow:Email:Mode = Log` |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/Cqrs/Dispatcher.cs` | modify | `method.Invoke(handler, BindingFlags.DoNotWrapExceptions, null, args, null)`; wynik przez `as Task<TResult> ?? throw` zamiast `!` |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/DependencyInjection.cs` | modify | `TryAddSingleton(TimeProvider.System)` |
| `Directory.Packages.props` | modify | Nowe pakiety (sekcja 4, krok 1) |
| `stone-flow.slnx` | modify | Folder `/api/src/Modules/Access/`, 4 projekty i `StoneFlow.Access.Tests` |
| `deploy/api.env.example` | modify | `StoneFlow__Owner__Email=<owner@example.com>`, `StoneFlow__App__BaseUrl`, `StoneFlow__Email__Mode=Smtp`, `StoneFlow__Email__Smtp__*` z placeholderami |
| `README.md` | modify | Konfiguracja właściciela i SMTP, user-secrets, `dotnet ef database update` dla `AccessDbContext`, testy na LocalDB, wylogowanie wszystkich urządzeń, zmiana `Owner:Email` po utworzeniu konta, `/health` wołany bezpośrednio przez monitoring, link w panelu sklepu (wspólnie z frontendem) |
| `.sdd/context/conventions.md` | modify | Styl endpointów (rozstrzyga TODO), moduł `Access`, schemat `access`, `STONEFLOW_TEST_SQLSERVER` w weryfikacji |
| `**/packages.lock.json` (nowe i zmienione projekty) | create/modify | Regenerowane przez `dotnet restore` |

#### Testy

| Plik | Akcja | Cel |
|---|---|---|
| `api/tests/StoneFlow.Access.Tests/StoneFlow.Access.Tests.csproj` | create | Referencje do Access.Domain, Application i Infrastructure; pakiety `Microsoft.Extensions.TimeProvider.Testing` i `Microsoft.Extensions.Diagnostics.Testing` |
| `api/tests/StoneFlow.Access.Tests/Domain/*Tests.cs` | create | `EmailAddressTests`, `PasswordPolicyTests`, `SessionPolicyTests`, `PasswordResetTokenTests` |
| `api/tests/StoneFlow.Access.Tests/Application/*Tests.cs` | create | Testy handlerów, `ResetTokenTests`, `ResetLinkTests`, `PasswordResetEmailTests` |
| `api/tests/StoneFlow.Access.Tests/Infrastructure/*Tests.cs` | create | Walidatory opcji, `SecurityEventLogTests` |
| `api/tests/StoneFlow.Access.Tests/Fakes/*.cs` | create | Ręczne fałszywki portów (repo nie ma biblioteki mocków) |
| `api/tests/StoneFlow.Shared.Tests/Cqrs/DispatcherTests.cs` | modify | Testy oryginalnego wyjątku |
| `api/tests/StoneFlow.Architecture.Tests/Modules.cs` | modify | `"Access"` w `Names` i w czterech `switch` |
| `api/tests/StoneFlow.Architecture.Tests/StoneFlow.Architecture.Tests.csproj` | modify | 4 referencje do projektów Access |
| `api/tests/StoneFlow.Architecture.Tests/DbContextSchemaTests.cs` | modify | Gałąź `"Access"` → `AddAccessInfrastructure`, `AccessDbContext`, `"access"` |
| `api/tests/StoneFlow.Api.IntegrationTests/StoneFlow.Api.IntegrationTests.csproj` | modify | `Microsoft.Extensions.TimeProvider.Testing` |
| `.../Infrastructure/SqlServerFixture.cs` | modify | `STONEFLOW_TEST_SQLSERVER` albo Testcontainers; `GetDatabaseAsync(string key)` z izolowaną bazą na klasę |
| `.../Infrastructure/TestDatabase.cs` | create | `CREATE DATABASE`, migracje wszystkich kontekstów, `ResetAccessAsync()`, `DROP` przy dispose |
| `.../Infrastructure/StoneFlowApiFactory.cs` | modify | Domyślna konfiguracja Access, `BaseAddress = https://localhost`, `CapturingEmailSender`, opcjonalny `FakeTimeProvider`, `StartupLogs` należące do fabryki, filtry testowe |
| `.../Infrastructure/TestAuthenticationStartupFilter.cs` | create | Tylko w testach: nagłówek `X-Test-Account` ustawia `HttpContext.User` przed uwierzytelnianiem (testy 404/500/CSRF bez bazy) |
| `.../Infrastructure/TestRemoteIpStartupFilter.cs` | create | Tylko w testach: nagłówek `X-Test-Remote-Ip` ustawia `Connection.RemoteIpAddress` |
| `.../Infrastructure/CapturingEmailSender.cs` | create | Zbiera wiadomości i pozwala wyciągnąć `konto` i `token` z fragmentu |
| `.../Infrastructure/AuthClient.cs` | create | Pomocnik: `GetCsrfAsync`, `LoginAsync`, `RequestResetAsync`, `ConfirmAsync`, nagłówek `X-XSRF-TOKEN` z ciasteczka |
| `.../Infrastructure/StartupFailure.cs` | create | Deterministyczne wykrycie `OptionsValidationException` (sekcja 4, krok 3) |
| `.../Access/LoginTests.cs`, `SessionTests.cs`, `LogoutTests.cs`, `PasswordResetTests.cs`, `OwnerAccountTests.cs`, `CsrfTests.cs`, `RateLimitingTests.cs`, `AccessStartupTests.cs`, `SecurityLogTests.cs`, `CookieAttributesTests.cs`, `DatabaseUnavailableTests.cs` | create | Tabela testów w sekcji 8 |
| `.../Security/AnonymousEndpointsTests.cs`, `CsrfCoverageTests.cs`, `CorsTests.cs` | create | Wyliczenie endpointów, CSRF na każdym endpoincie zmieniającym stan, CORS |
| `.../ProblemDetailsAndCorrelationTests.cs` | modify | `/nie-ma` i `/__test/throw` z `X-Test-Account`; nowy test anonimowego `/nie-ma` → 401 Problem Details z `traceId`; test, że 4xx zachowuje `detail` |
| `.../LaunchDateStartupTests.cs` | modify | Asercja przez `StartupFailure` |
| `.../DatabaseTests.cs` | modify | Baza z `TestDatabase`; migracje obejmują `AccessDbContext` i schemat `access`; sprawdzenie zestawu tabel |

---

### B4. Kroki implementacji

Kroki 2 i 3 to osobne commity zaległości. Każdy krok kończy `dotnet build` i testy danego obszaru.

1. **Pakiety.** W `Directory.Packages.props` dodać:
   - `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` **10.0.14** (Runtime);
   - `MailKit` **4.x**, najnowsze stabilne (Runtime);
   - `Microsoft.Extensions.TimeProvider.Testing` **10.11.0** (Tests, ta sama linia co `Microsoft.Extensions.Diagnostics.Testing` 10.11.0).

   Przed zapisem sprawdzić wersje poleceniem `dotnet package search <id> --exact-match`. Dla MailKit wybrać najnowsze stabilne wydanie co najmniej 2 tygodnie starsze od daty wdrożenia. MimeKit i BouncyCastle przychodzą przechodnio.

   Identity nie potrzebuje pakietu: `UserManager`, `SignInManager`, `PasswordHasher`, `SecurityStampValidator`, antiforgery i rate limiting są we frameworku `Microsoft.AspNetCore.App`. Pakiet `Microsoft.AspNetCore.Identity.EntityFrameworkCore` jest świadomie pominięty (krok 6).

2. **Zaległość SF-3: `Dispatcher`** (`Dispatcher.cs`, `DispatcherTests.cs`). Wywołanie z `BindingFlags.DoNotWrapExceptions`, dzięki czemu stos jest zachowany bez `ExceptionDispatchInfo`, i jawny wyjątek, gdy wynik nie jest `Task<TResult>`. Testy: handler rzucający synchronicznie, handler zwracający zadanie z błędem, ślad stosu zawiera metodę handlera.

3. **Niestabilny `LaunchDateStartupTests`** (osobny commit, bez zmiany zachowania aplikacji).
   - **Hipoteza do potwierdzenia.** W minimal hostingu `WebApplicationFactory` używa `DeferredHost`. `Program` sam woła `app.RunAsync()` na wątku punktu wejścia. Przy błędzie `ValidateOnStart` `RunAsync` loguje „Hosting failed to start” i w `finally` zwalnia host. Równolegle `DeferredHost.StartAsync` sięga po `_host.Services` (`IHostApplicationLifetime`). Jeśli host jest już zwolniony, `CreateClient` rzuca `ObjectDisposedException` (albo wyjątek bez `OptionsValidationException` w łańcuchu). To wyścig zależny od szybkości maszyny, częstszy na Linuksie w CI.
   - **Diagnoza.** Asercja zawsze dołącza `exception.ToString()` do komunikatu błędu. W CI uruchomić test w pętli, np. 30 × `dotnet test --filter-class *LaunchDateStartupTests` w jednorazowym kroku na gałęzi, i zapisać faktyczny typ wyjątku w `06-implementation-done`.
   - **Poprawka.** `StoneFlowApiFactory` ma własny `FakeLogCollector` (`StartupLogs`), podłączony przez `logging.AddProvider(new FakeLoggerProvider(StartupLogs))`. Kolektor żyje dłużej niż host. `StartupFailure.AssertOptionsValidation(factory, key)`:
     1. wymaga, aby `CreateClient` rzucił dowolny wyjątek;
     2. szuka `OptionsValidationException` najpierw w łańcuchu wyjątku (z `AggregateException`), potem w `Exception` rekordów `StartupLogs`;
     3. wymaga, aby komunikat zawierał klucz, a nie zawierał wartości.

     Z helpera korzystają `LaunchDateStartupTests` i nowe `AccessStartupTests`.

4. **Szkielet modułu.** 4 csproj zgodne z `ProjectReferenceTests`, `AssemblyReference.cs`, wpisy w `stone-flow.slnx`. W testach architektury: `Modules.cs`, `StoneFlow.Architecture.Tests.csproj`, `DbContextSchemaTests.cs`. Po tym kroku testy architektury są czerwone do kroku 7, bo brakuje `AccessDbContext`; można je zazielenić stubem kontekstu.

5. **Domain.** Pliki z sekcji 3 i testy jednostkowe `StoneFlow.Access.Tests/Domain`. Używane typy BCL: `System.Net.Mail.MailAddress` i `TimeSpan`, bez frameworków.

6. **Persistence i Identity.**
   - **`AccessUser`.** Kolumny: `Id` (Guid v7), `Email`, `NormalizedEmail`, `PasswordHash?`, `SecurityStamp`, `ConcurrencyStamp`, `AccessFailedCount`, `LockoutEndUtc?`, `CreatedAtUtc`.
   - **Własny `AccessUserStore` zamiast `IdentityUserContext` i `UserOnlyStore`.** `UserOnlyStore` implementuje `IUserClaimStore`, a `UserClaimsPrincipalFactory` czyta wtedy tabelę `UserClaims`. `IdentityUserContext` mapuje też `UserLogins` i `UserTokens`. Specyfikacja zabrania tych tabel. `UserName` w magazynie jest równy `Email`.
   - **Duplikat przy wyścigu.** `CreateAsync` łapie `DbUpdateException` z `SqlException.Number` 2601 lub 2627 → `IdentityResult.Failed(DuplicateEmail)` i czyści `ChangeTracker`.
   - **`UpdateAsync`.** Nowy `ConcurrencyStamp`; `DbUpdateConcurrencyException` → `ConcurrencyFailure`.
   - **`IdentitySetup`:**
     - `AddIdentityCore<AccessUser>`: `Lockout.MaxFailedAccessAttempts = 5`, `DefaultLockoutTimeSpan = 15 min`, `AllowedForNewUsers = true`;
     - `Password.RequiredLength = 12`, reguły składu wyłączone, `RequiredUniqueChars = 1`;
     - `User.RequireUniqueEmail = true`, `User.AllowedUserNameCharacters = ""` (e-mail jest nazwą użytkownika);
     - `.AddUserStore<AccessUserStore>().AddSignInManager()`;
     - `AddAuthentication(IdentityConstants.ApplicationScheme).AddCookie(IdentityConstants.ApplicationScheme)`;
     - `SecurityStampValidatorOptions`: `ValidationInterval = TimeSpan.Zero`, `TimeProvider` z DI, `OnRefreshingPrincipal` kopiuje `sf:auth_time` z `CurrentPrincipal` do `NewPrincipal` (decyzja 4).
   - **`ConfigureSessionCookie`:**
     - nazwa `__Host-sf_session` poza Development, `sf_session` w Development;
     - `HttpOnly`; `SecurePolicy` `Always` poza Development, `SameAsRequest` w Development;
     - `SameSite = Lax`, `Path = "/"`, bez `Domain`;
     - `ExpireTimeSpan = SessionPolicy.IdleTimeout`, `SlidingExpiration = true`, `TimeProvider` jawnie z DI;
     - ciasteczko sesyjne przeglądarki (`isPersistent: false`).
   - **`SessionCookieEvents.OnValidatePrincipal`.** Kolejność: brak lub nieczytelny `sf:auth_time` albo przekroczone `SessionPolicy.AbsoluteLifetime` → `RejectPrincipal()` i `SignOutAsync(scheme)`. Inaczej `SecurityStampValidator.ValidatePrincipalAsync(ctx)`. Jeśli principal nadal ważny → `ShouldRenew = true`, co przesuwa termin przy każdym żądaniu (wyjaśnia niejasność z przeglądu). `OnRedirectToLogin` ustawia 401 bez `Location`, a treść Problem Details dopisuje `UseStatusCodePages`. `OnRedirectToAccessDenied` działa tak samo z 403.
   - **`IdentityAccountService`:**
     - `VerifyCredentialsAsync`. Brak konta → `PasswordHasher.VerifyHashedPassword` na stałym fikcyjnym hashu (wyrównanie czasu) → `InvalidCredentials`. Konto bez hasła → to samo wyrównanie, potem `CheckPasswordSignInAsync(user, pwd, lockoutOnFailure: true)`, które zwiększa licznik (rozstrzyga niejasność). Zwraca `{ Status: Succeeded | InvalidCredentials | LockedOut, AccountId?, BecameLockedOut }`.
     - `SetPasswordAsync`: walidatory Identity, nowy hash, `UpdateSecurityStampAsync`, `SetLockoutEndDateAsync(null)`, `ResetAccessFailedCountAsync`. Jeden `SaveChanges` przez `UpdateAsync`.
     - `RotateSecurityStampAsync`.
   - **`IdentitySessionManager`:**
     - `SignInAsync`: `SignInWithClaimsAsync(user, false, [sf:auth_time = unix sekundy])`, potem `HttpContext.User = principal`, aby nowy token CSRF był związany z nową tożsamością;
     - `SignOutAsync`: `HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme)` bez `SignInManager.SignOutAsync`, który dotyka schematów zewnętrznych; potem `HttpContext.User` = anonimowy.
   - **`AccessDbContext` i konfiguracje.** `PasswordResetTokenStore`:
     - `TryConsumeAsync`: `ExecuteUpdateAsync` z `WHERE UserId=@id AND TokenHash=@h AND ConsumedAtUtc IS NULL AND RevokedAtUtc IS NULL AND ExpiresAtUtc > @now`, `SET ConsumedAtUtc=@now`; sukces tylko przy 1 wierszu (decyzja 8);
     - `ReplaceActiveAsync`: `ExecuteUpdateAsync` ustawia `RevokedAtUtc` dla aktywnych i dodaje nowy token w jednej transakcji.

     `AccessUnitOfWork` opakowuje te operacje w `Database.BeginTransactionAsync`.
   - **Data Protection.** `AddDataProtection().SetApplicationName("StoneFlow").PersistKeysToDbContext<AccessDbContext>()` (decyzja 12).
   - **Migracja.** `dotnet tool run dotnet-ef migrations add InitialAccess --project api/src/Modules/Access/StoneFlow.Access.Infrastructure --startup-project api/src/StoneFlow.Api --context AccessDbContext --output-dir Persistence/Migrations`. `Down` usuwa tabele i zostawia schemat, jak w pozostałych modułach.

7. **Opcje, e-mail i logi.**
   - **Walidatory** jako `IValidateOptions` z `ValidateOnStart`, wzorem `LaunchDateOptionsValidator`. Komunikaty wskazują klucz bez wartości.
   - **Reguły walidacji:**
     - `Owner:Email`: wymagany i poprawny;
     - `App:BaseUrl`: absolutny http/https, bez query i fragmentu;
     - `Email:Mode`: wymagany, `Smtp` albo `Log`; `Log` poza Development → błąd (walidator dostaje `IHostEnvironment`);
     - dla `Smtp`: `Host` wymagany, `Port` 1–65535, `FromAddress` poprawny, `UserName` i `Password` razem albo wcale, `UseStartTls` domyślnie `true`.
   - **`AccessSettings`** jako singleton z wartości opcji.
   - **`SmtpEmailSender`:** MailKit `SmtpClient`, `SecureSocketOptions.StartTls` albo `Auto`, timeout 15 s. Łapie `SmtpCommandException`, `SmtpProtocolException`, `AuthenticationException`, `IOException`, `SocketException` i loguje tylko typ błędu.
   - **Dlaczego MailKit.** `System.Net.Mail.SmtpClient` jest oznaczony przez Microsoft jako niezalecany (DE0005) i słabo obsługuje współczesne mechanizmy. MailKit jest rekomendowaną alternatywą, utrzymywaną, bez zależności natywnych.
   - **`LogEmailSender`** rejestrowany tylko przy `Mode=Log`.
   - **`SecurityEventLog`** zawiera wyłącznie `AccountId`.

8. **Application.** Handlery opisane w sekcji 5 („Logika”). Rejestracja `AddHandlersFromAssembly(Access.Application.AssemblyReference.Assembly)` w `AddAccessInfrastructure`. Testy jednostkowe na fałszywkach portów z `FakeTimeProvider`.

9. **Access.Api.** Endpointy, DTO, `XsrfCookie`, `ConfigureAntiforgery`:
   - ciasteczko antiforgery: `__Host-sf_af` poza Development (`sf_af` w Development), `HttpOnly`, `Secure` poza Development, `SameSite=Strict`, `Path=/`;
   - `HeaderName = "X-XSRF-TOKEN"`;
   - `XSRF-TOKEN`: `HttpOnly=false`, `Secure` poza Development, `SameSite=Strict`, `Path=/`.

   `AuthRateLimiting`: `PartitionedRateLimiter` z `FixedWindow` po `RemoteIpAddress?.ToString() ?? "unknown"` (decyzja 5). `AuthProblems` z tekstami i kodami.

10. **Host.** `SecuritySetup`, `CsrfValidationMiddleware`, `ExceptionStatusCodes`, poprawka `ProblemDetailsSetup`. `Program.cs` w tej kolejności (decyzja 3: autoryzacja przed CSRF):
    ```
    UseMiddleware<CorrelationIdMiddleware>()
    UseExceptionHandler(new ExceptionHandlerOptions { StatusCodeSelector = ExceptionStatusCodes.Select })
    UseStatusCodePages()
    UseRouting()
    UseCors()
    UseRateLimiter()
    UseAuthentication()
    UseAuthorization()
    UseMiddleware<CsrfValidationMiddleware>()
    MapOpenApi().AllowAnonymous()          // tylko Development
    MapHealthChecks("/health").AllowAnonymous()
    MapAccessEndpoints(); Map{Orders,Expenses,Reporting,Integrations}Endpoints()
    ```
    Rejestracja: `AddStoneFlowSecurity()`, `AddAccessInfrastructure().AddAccessModule()`. Bez `ForwardedHeaders` (decyzja 5).

11. **Infrastruktura testów integracyjnych.**
    - **`SqlServerFixture`.** Jeśli `STONEFLOW_TEST_SQLSERVER` jest ustawiona (connection string do serwera, np. `Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=30`), kontener nie startuje. W przeciwnym razie działa Testcontainers jak dziś.
    - **Izolacja baz.** `GetDatabaseAsync(string key)` tworzy i cache'uje `TestDatabase` o nazwie `StoneFlowTests_{key}_{guid:N}` dla każdej klasy testów. Wszystkie konteksty mają zastosowane migracje. Bazy są usuwane w `DisposeAsync` fixture'a: `SqlConnection.ClearAllPools()`, `ALTER DATABASE … SET SINGLE_USER WITH ROLLBACK IMMEDIATE`, `DROP DATABASE`.
    - **Dlaczego cache, a nie fixture klasy.** Pozwala uniknąć zależności od wstrzykiwania fixture'a kolekcji do fixture'a klasy w xUnit v3.
    - **Czyszczenie między testami.** `ResetAccessAsync()` czyści `access.PasswordResetTokens` i `access.Users`, aby testy w klasie były niezależne.
    - **Fabryka.** `StoneFlowApiFactory`: domyślne `StoneFlow:Owner:Email=owner@example.com`, `StoneFlow:App:BaseUrl=https://app.example.com`, `StoneFlow:Email:Mode=Smtp`, `Smtp:Host=localhost`, `Smtp:Port=2525`, `Smtp:FromAddress=noreply@example.com`. `IEmailSender` jest zastąpiony przez `CapturingEmailSender`, chyba że `UseRealEmailSender = true`. `ClientOptions.BaseAddress = https://localhost`, bo ciasteczka `Secure` i `__Host-` wymagają HTTPS w `CookieContainer`, a antiforgery z `SecurePolicy.Always` wymaga `IsHttps`. Do tego opcjonalny `FakeTimeProvider` oraz filtry `TestAuthentication` i `TestRemoteIp`.

12. **Testy integracyjne** z sekcji 8.

13. **Dokumentacja:**
    - `conventions.md`, sekcja Backend/Naming. Zamiast TODO wpis:
      - endpointy to Minimal API w `StoneFlow.{Module}.Api`, `Map{Module}Endpoints` z `MapGroup("/api/{obszar}")`;
      - jeden plik `Endpoints/*Endpoints.cs` na grupę, DTO w `Contracts/`;
      - `TypedResults`, każde wywołanie przez `IDispatcher`, bez logiki biznesowej;
      - nazwy `.WithName("{Module}.{Akcja}")`;
      - domyślnie wymagana sesja (fallback policy); `.AllowAnonymous()` tylko z listy wyjątków, pilnowanej testem `AnonymousEndpointsTests`;
      - CSRF globalnie dla `/api/*` i metod zmieniających stan.

      Dodać moduł `Access` (schemat `access`, użytkownik w kształcie Identity z własnym magazynem) i zmienną `STONEFLOW_TEST_SQLSERVER` w sekcji Verification.
    - README i `deploy/api.env.example` według tabeli plików.

---

### B5. Dane i kontrakty

#### Storage (schemat `access`, migracja `InitialAccess`)

- `access.Users`: `Id uniqueidentifier PK`, `Email nvarchar(256) NOT NULL`, `NormalizedEmail nvarchar(256) NOT NULL UNIQUE`, `PasswordHash nvarchar(max) NULL`, `SecurityStamp nvarchar(64) NOT NULL`, `ConcurrencyStamp nvarchar(64) NOT NULL` (token współbieżności), `AccessFailedCount int NOT NULL`, `LockoutEndUtc datetime2 NULL` (konwerter DateTimeOffset ↔ UTC), `CreatedAtUtc datetime2 NOT NULL`. Bez tabel ról, claimów, loginów i tokenów.
- `access.PasswordResetTokens`: `Id uniqueidentifier PK`, `UserId FK → Users(Id) ON DELETE CASCADE`, `TokenHash binary(32) UNIQUE`, `CreatedAtUtc`, `ExpiresAtUtc`, `ConsumedAtUtc NULL`, `RevokedAtUtc NULL`; indeks `(UserId, CreatedAtUtc)`.
- `access.DataProtectionKeys`: `Id int identity PK`, `FriendlyName nvarchar(max) NULL`, `Xml nvarchar(max) NULL`.
- Historia migracji w `access.__EFMigrationsHistory` (przez `UseModuleSqlServer`).
- Jedno konto gwarantują: sprawdzenie „brak jakiegokolwiek użytkownika” przed utworzeniem i unikalny `NormalizedEmail` (decyzje 1 i 8). Brak dodatkowego ograniczenia DB „max 1 wiersz” (ryzyko 10).

### API (grupa `/api/auth`, JSON camelCase)

| Metoda | Ścieżka | Żądanie | Odpowiedź | Auth | Uwagi |
|---|---|---|---|---|---|
| GET | `/api/auth/csrf` | — | `204`, ciasteczko antiforgery i `XSRF-TOKEN` (świeży token dla bieżącej tożsamości) | anonimowy | Frontend woła go przy wejściu na ekrany auth i po każdym `401` |
| POST | `/api/auth/login` | `{ email, password }` | `204` + ciasteczko sesji + nowy `XSRF-TOKEN`; `400` walidacja lub CSRF; `401` `code=invalid_credentials`; `429` | anonimowy, CSRF, `auth-login` | |
| POST | `/api/auth/logout` | — | `204`, usunięte ciasteczko sesji, nowy anonimowy `XSRF-TOKEN`; `401` bez sesji | sesja, CSRF | Rotacja znacznika wylogowuje wszystkie urządzenia (decyzja 2) |
| GET | `/api/auth/session` | — | `200 { "email": "…" }`; `401` | sesja | |
| POST | `/api/auth/password-reset/request` | `{ email }` | `202` zawsze, także przy limicie konta i błędzie SMTP; `400` pusty lub zły format; `429` | anonimowy, CSRF, `auth-reset` | |
| POST | `/api/auth/password-reset/confirm` | `{ accountId, token, newPassword }` | `204`; `400` `code=reset_link_invalid`; `400` `ValidationProblemDetails` `errors.newPassword` | anonimowy, CSRF | `accountId` jako `string?`: nie-GUID daje ten sam `reset_link_invalid` |

**Problem Details** zawierają zawsze `traceId`. Tekst dla użytkownika jest w `detail`, a kod w rozszerzeniu `code`:

| Sytuacja | Status | `detail` | `code` |
|---|---|---|---|
| Błędne logowanie | 401 | „Nieprawidłowy e-mail lub hasło. Po 5 nieudanych próbach logowanie jest blokowane na 15 minut.” | `invalid_credentials` |
| CSRF | 400 | „Odśwież stronę i spróbuj ponownie.” | `csrf_invalid` |
| Link resetu | 400 | „Link wygasł lub został już użyty. Poproś o nowy link.” | `reset_link_invalid` |
| Limit żądań | 429 | „Zbyt wiele prób. Spróbuj ponownie za chwilę.” | `rate_limited` |
| Brak lub wygasła sesja | 401 | (domyślne z `UseStatusCodePages`) | — |
| Baza niedostępna | 503 | (bez `detail`) | — |

**Komunikaty pól** (`AccessMessages`):
- `email`: „Podaj adres e-mail.”, „Podaj poprawny adres e-mail.”;
- `password`: „Podaj hasło.”;
- `newPassword`: „Podaj nowe hasło.”, „Hasło musi mieć co najmniej 12 znaków.”, „Hasło nie może być takie samo jak adres e-mail.”.

**Link w e-mailu:** `{StoneFlow:App:BaseUrl}/reset-hasla/nowe#konto={accountId}&token={token}`. Frontend czyta fragment i czyści go przez `history.replaceState` (decyzja 7). Specyfikacja w punkcie 16 podaje `?konto=`, więc ten punkt trzeba zaktualizować.

**Ciasteczka:**

| Ciasteczko | Nazwa poza Development / w Development | HttpOnly | Secure | SameSite | Path |
|---|---|---|---|---|---|
| Sesja | `__Host-sf_session` / `sf_session` | tak | poza Development | Lax | `/` |
| Antiforgery | `__Host-sf_af` / `sf_af` | tak | poza Development | Strict | `/` |
| `XSRF-TOKEN` | bez zmian | nie | poza Development | Strict | `/` |

Nagłówek żądania: `X-XSRF-TOKEN` (domyślny w Angularze).

#### Porty w Application (sygnatury poglądowe)

```
IAccountService
  Task<AccountInfo?> FindByEmailAsync(string normalizedEmail, CancellationToken ct)
  Task<AccountInfo?> FindByIdAsync(Guid accountId, CancellationToken ct)
  Task<bool> AnyAccountExistsAsync(CancellationToken ct)
  Task<AccountInfo> CreateWithoutPasswordAsync(string email, CancellationToken ct)   // wyścig → zwraca istniejące
  Task<CredentialCheck> VerifyCredentialsAsync(string normalizedEmail, string password, CancellationToken ct)
  Task<bool> SetPasswordAsync(Guid accountId, string newPassword, CancellationToken ct)
  Task RotateSecurityStampAsync(Guid accountId, CancellationToken ct)
ISessionManager      SignInAsync(Guid, DateTimeOffset, ct) / SignOutAsync(ct)
IPasswordResetTokenStore
  Task<int> CountIssuedSinceAsync(Guid accountId, DateTimeOffset since, ct)
  Task ReplaceActiveAsync(PasswordResetToken token, DateTimeOffset now, ct)
  Task<bool> TryConsumeAsync(Guid accountId, byte[] tokenHash, DateTimeOffset now, ct)
IAccessUnitOfWork    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, ct)
IEmailSender         Task<EmailDeliveryResult> SendAsync(EmailMessage message, ct)
ISecurityEvents      LoginSucceeded(Guid) | LoginFailed(Guid?) | LockedOut(Guid) | LoggedOut(Guid)
                     ResetRequested(Guid?) | ResetSuppressedByAccountLimit(Guid) | ResetEmailFailed(Guid)
                     PasswordSet(Guid) | ResetTokenRejected(Guid?) | AccountCreated(Guid)
```

#### Logika: kto ma którą regułę

- **`LoginHandler`:**
  1. walidacja wymaganych pól → `ValidationFailed`;
  2. `EmailAddress.Normalize`;
  3. `VerifyCredentialsAsync`;
  4. `Succeeded` → `SignInAsync(id, timeProvider.GetUtcNow())`, log `LoginSucceeded`; `LockedOut` → log `LockedOut` przy `BecameLockedOut`; pozostałe → log `LoginFailed`;
  5. każdy błąd daje ten sam `LoginResult.InvalidCredentials`.
- **`LogoutHandler`:** `RotateSecurityStampAsync`, potem `SignOutAsync`, log `LoggedOut`.
- **`GetSessionHandler`:** konto po id. Brak konta → `null` → endpoint zwraca 401.
- **`RequestPasswordResetHandler`:**
  1. walidacja formatu;
  2. szukanie konta po znormalizowanym e-mailu;
  3. gdy brak: utworzenie konta tylko jeśli `!AnyAccountExistsAsync()` i e-mail jest równy znormalizowanemu `AccessSettings.OwnerEmail`, inaczej log `ResetRequested(null)` i `Accepted`;
  4. `CountIssuedSinceAsync(now − 15 min) ≥ 3` → log `ResetSuppressedByAccountLimit`, `Accepted`;
  5. `ResetToken.Generate`, `ReplaceActiveAsync(PasswordResetToken.Issue(…))`;
  6. `PasswordResetEmail` z `ResetLink`, potem `IEmailSender.SendAsync`; `Failed` → log `ResetEmailFailed`;
  7. zawsze `Accepted`.
- **`ConfirmPasswordResetHandler`:**
  1. `accountId` nie-GUID albo `ResetToken.TryHash` nieudane → `InvalidLink`, log `ResetTokenRejected`;
  2. konto po id; brak → `InvalidLink`;
  3. `PasswordPolicy.Check(newPassword, account.Email)` → `InvalidPassword(errors)` **przed** zużyciem tokenu;
  4. w `ExecuteInTransactionAsync`: `TryConsumeAsync` (`false` → `InvalidLink`), potem `SetPasswordAsync` (`false` → rollback i błąd pola);
  5. log `PasswordSet`, wynik `Succeeded`. Sesja nie jest tworzona.

### B6. Przypadki brzegowe i null safety

| Przypadek | Gdzie obsłużony |
|---|---|
| E-mail z wielkimi literami lub spacjami | `EmailAddress.Normalize` w handlerach; `NormalizedEmail` w magazynie |
| Logowanie na konto bez hasła → ogólny 401, licznik rośnie | `IdentityAccountService.VerifyCredentialsAsync` |
| Nieistniejące konto: ten sam 401 i podobny czas | Fikcyjna weryfikacja hasha w `IdentityAccountService` |
| Dwie prośby o reset → działa tylko ostatni link | `ReplaceActiveAsync` |
| Link użyty drugi raz, wygasły, zmieniony token, `konto` niepasujące lub nie-GUID | `ConfirmPasswordResetHandler` + warunkowy `UPDATE`; ten sam `reset_link_invalid` |
| Dwa równoczesne `confirm` | `TryConsumeAsync` — 1 wiersz (decyzja 8) |
| Dwie równoczesne pierwsze prośby | Unikalny indeks + `CreateWithoutPasswordAsync` zwraca istniejące konto; wynik 202 |
| Zmiana `Owner:Email` po utworzeniu konta | Konto już istnieje, więc nie powstaje nowe; README (decyzja 1) |
| Reset w trakcie sesji w innej karcie → 401 | Rotacja znacznika + `ValidationInterval = 0` |
| Wylogowanie i skopiowane ciasteczko → 401 | Rotacja znacznika w `LogoutHandler` (decyzja 2) |
| 11 h 59 min aktywności → koniec po 12 h | `sf:auth_time` + `OnValidatePrincipal` + `OnRefreshingPrincipal` |
| Restart API → sesje i CSRF ważne | Klucze w `access.DataProtectionKeys`, `SetApplicationName` |
| Baza niedostępna → 503 bez szczegółów | `ExceptionStatusCodes` + `ProblemDetailsSetup` (`context.Exception` niepuste) |
| Brak konfiguracji Owner, App lub SMTP; `Log` poza Development | Walidatory + `ValidateOnStart` |
| Błąd SMTP → 202, błąd w logach bez treści | `SmtpEmailSender` → `Failed` → `ResetEmailFailed` |
| Brak lub zły nagłówek CSRF → 400 | `CsrfValidationMiddleware` |
| Wygasła sesja + POST ze starym tokenem → 401, nie 400 | Kolejność potoku (decyzja 3) |
| Inny origin → brak nagłówków CORS | `SecuritySetup` + `UseCors` |
| Nieznana ścieżka bez sesji → 401 zamiast 404 | `FallbackPolicy` obejmuje żądania bez endpointu (zamierzone; testy dostosowane) |
| DTO z polami `null`, pusty body | Pola `string?`, walidacja w handlerach, bez `!` |
| `HttpContext` niedostępny w `IdentitySessionManager` | `IHttpContextAccessor.HttpContext ?? throw InvalidOperationException` (port używany tylko w żądaniu) |
| Brak lub nieczytelny `sf:auth_time` | Odrzucenie principala (fail-closed) |
| Wynik `Invoke` nie jest `Task<TResult>` | Jawny wyjątek w `Dispatcher` |

### B7. Niezmienniki

| Niezmiennik | Gdzie egzekwowany | Test, który go dowodzi |
|---|---|---|
| 3. Dostęp wymuszany przez API | `SecuritySetup` (`FallbackPolicy` = zalogowany), `AllowAnonymous` tylko na 5 endpointach + OpenAPI w Development | `AnonymousEndpointsTests.Anonymous_endpoints_match_allowed_list` (Production i Development), `AnonymousEndpointsTests.Every_protected_endpoint_returns_401_without_session`, `SessionTests.*_returns_401` |
| 4. Ponowienia bez duplikatów (tylko w zakresie konta) | Unikalny `NormalizedEmail` + warunek „brak konta”; `ReplaceActiveAsync` | `OwnerAccountTests.Concurrent_first_requests_create_one_account`, `PasswordResetTests.Second_request_revokes_first_link` |
| 6. Sekrety po stronie serwera | Hash SHA-256 tokenu, hash hasła Identity, `SecurityEventLog` tylko z id, link we fragmencie, `Log` tylko w Development, hasło SMTP poza appsettings | `SecurityLogTests.Logs_contain_no_email_password_or_token`, `PasswordResetTests.Database_stores_only_token_hash`, `AccessStartupTests.Log_mode_outside_development_fails`, `AccessStartupTests.Appsettings_contain_no_owner_email_or_smtp_password`, `ProblemDetailsAndCorrelationTests.Production_error_has_no_exception_details` |
| 7. Granice modułów i warstw | 4 projekty, Domain i Application bez pakietów i frameworków, porty w Application, Identity, EF i HTTP w Infrastructure i Api | `LayerDependencyTests`, `ProjectReferenceTests`, `ModuleBoundaryTests`, `DbContextSchemaTests` z `Access` |
| 8. Brak danych identyfikujących | `owner@example.com`, `app.example.com`, `noreply@example.com` w testach, README i `api.env.example`; treść e-maila bez nazwy sklepu | `PasswordResetEmailTests.Email_contains_no_shop_identifiers` + przegląd |
| 1, 2, 5 | Nie dotyczy (brak danych finansowych) | — |

**Testy architektury.** Wystarczy dodać `Access` w `Modules.cs`, csproj i `DbContextSchemaTests`. Żadna reguła nie wymaga zmiany:
- `Access.Api` ma tylko `FrameworkReference` i referencję do własnego Application; antiforgery i rate limiting są we frameworku;
- `Access.Application` nie ma pakietów, a typy Identity leżą w przestrzeni `Microsoft.AspNetCore.Identity`, więc istniejący zakaz `Microsoft.AspNetCore*` je obejmuje;
- `Infrastructure` może mieć pakiety.

Logowanie przez cookie (`SignInManager`) jest w Infrastructure, bo wymaga typu `AccessUser`, którego `Access.Api` nie widzi.

### B8. Testy

#### Jednostkowe — `StoneFlow.Access.Tests`, `StoneFlow.Shared.Tests`

| Test | Typ | Co dowodzi |
|---|---|---|
| `EmailAddressTests.Normalize_trims_and_uppercases`, `IsValid_rejects_display_name_and_garbage` | unit | Normalizacja i format e-maila |
| `PasswordPolicyTests.Rejects_shorter_than_12`, `Accepts_12_without_composition_rules`, `Rejects_password_equal_to_email_case_insensitive` | unit | Pkt 18 |
| `SessionPolicyTests.Within_12h`, `Expired_after_12h` | unit | Limit 12 h |
| `PasswordResetTokenTests.Issue_sets_expiry_30_minutes` | unit | Pkt 16 |
| `ResetTokenTests.Token_has_256_bits_and_is_url_safe`, `Hash_is_sha256_32_bytes`, `TryHash_rejects_malformed` | unit | Pkt 16, przegląd 7 |
| `ResetLinkTests.Uses_fragment_and_no_email`, `Trims_trailing_slash` | unit | Decyzja 7, pkt 16 |
| `PasswordResetEmailTests.Contains_link_and_validity`, `Email_contains_no_shop_identifiers` | unit | Pkt 16, niezmiennik 8 |
| `LoginHandlerTests.Success_signs_in_with_auth_time`, `Unknown_account_returns_generic_failure`, `Locked_out_returns_generic_failure_and_logs_lockout`, `Missing_fields_return_validation` | unit | Pkt 4–7, 31 |
| `LogoutHandlerTests.Rotates_stamp_then_signs_out` | unit | Decyzja 2 |
| `RequestPasswordResetHandlerTests.Creates_account_only_for_owner_when_none_exists`, `Ignores_owner_email_when_account_exists`, `Other_email_never_creates_account`, `Fourth_request_in_15_min_sends_no_email`, `Smtp_failure_still_accepted`, `Invalid_email_returns_validation` | unit | Pkt 2, 3, 17, decyzja 1, przegląd 1 |
| `ConfirmPasswordResetHandlerTests.Weak_password_does_not_consume_token`, `Invalid_account_id_returns_invalid_link`, `Consumed_token_returns_invalid_link`, `Success_sets_password_without_session` | unit | Pkt 18–20 |
| `OwnerOptionsValidatorTests`, `AppOptionsValidatorTests`, `EmailOptionsValidatorTests.Log_only_in_development`, `Smtp_requires_host_port_from`, `Credentials_both_or_none`, `Messages_name_key_without_value` | unit | Konfiguracja, przegląd 15 |
| `SecurityEventLogTests.Events_carry_account_id_only` | unit | Pkt 31 |
| `DispatcherTests.Sync_handler_exception_is_original`, `Faulted_task_exception_is_original`, `Stack_trace_contains_handler` | unit | Zaległość SF-3 |

#### Integracyjne — `StoneFlow.Api.IntegrationTests`

Wszystkie testy z bazą działają w kolekcji `SQL Server` na izolowanej bazie klasy, z LocalDB albo w kontenerze.

| Test | Typ | Co dowodzi |
|---|---|---|
| `AnonymousEndpointsTests.Anonymous_endpoints_match_allowed_list` (Production: 5; Development: +`/openapi/{documentName}.json`) | integracyjny, bez bazy | Kryterium 1, pkt 22 |
| `AnonymousEndpointsTests.Every_protected_endpoint_returns_401_without_session` (wyliczenie z `EndpointDataSource`) | integracyjny, bez bazy | Kryterium 1, DOST-01, także odczyty |
| `CsrfCoverageTests.Every_state_changing_api_endpoint_rejects_missing_token` (wyliczenie POST/PUT/PATCH/DELETE pod `/api`; chronione z `X-Test-Account`) | integracyjny, bez bazy | Przegląd 10, kryterium 6 |
| `CsrfCoverageTests.Unknown_api_post_is_also_validated` | integracyjny | Globalność middleware |
| `CsrfTests.Login_without_header_returns_400_with_detail`, `Wrong_token_returns_400`, `Login_refreshes_xsrf_cookie`, `Anonymous_token_rejected_after_login`, `Logout_issues_new_anonymous_token` | integracyjny | Pkt 24, login CSRF (przegląd 15) |
| `CsrfTests.Expired_session_post_returns_401_not_400` | integracyjny (FakeTime) | Decyzja 3, przegląd 3 |
| `CorsTests.Cross_origin_get_has_no_cors_headers`, `Preflight_has_no_allow_headers` | integracyjny, bez bazy | Pkt 25, kryterium 6 |
| `CookieAttributesTests.Production_session_cookie_is_host_prefixed_secure_httponly_lax_session`, `Antiforgery_cookies_attributes`, `Development_cookies_without_host_prefix` | integracyjny | Pkt 10, decyzja 9, przegląd 9 |
| `LoginTests.Valid_credentials_return_204_and_session`, `Email_case_and_spaces_normalized`, `Wrong_password_returns_generic_401`, `Unknown_email_same_401_body`, `Account_without_password_returns_401_and_counts_attempt` | integracyjny | Pkt 5, 6, kryterium 5 |
| `LoginTests.Five_failures_lock_for_15_minutes` (`LockoutEndUtc` ≈ teraz + 15 min; poprawne hasło przy blokadzie → ten sam 401) | integracyjny | Pkt 7, kryterium 5 |
| `LoginTests.Lockout_expired_allows_login_and_success_resets_counter` (`LockoutEndUtc` w przeszłości ustawiony w DB) | integracyjny | Pkt 7 |
| `SessionTests.Session_returns_email`, `Without_cookie_returns_401_problem_details` | integracyjny | Pkt 21, 23 |
| `SessionTests.Expires_after_60_minutes_idle` (FakeTime +61 min) | integracyjny | Pkt 11, kryterium 2 |
| `SessionTests.Activity_extends_idle_window` (co 50 min → 200) | integracyjny | Pkt 11, niejasność „każde żądanie” |
| `SessionTests.Ends_12h_after_login_despite_activity` (kroki 30 min do 11 h 59 min → 200, potem → 401) | integracyjny | Pkt 11, decyzja 4, przegląd 4 |
| `SessionTests.Survives_api_restart` (dwie fabryki, ta sama baza) | integracyjny | Klucze w bazie |
| `LogoutTests.Logout_returns_204_and_clears_cookie`, `Copied_cookie_rejected_after_logout`, `Logout_without_session_returns_401` | integracyjny | Pkt 13, decyzja 2, przegląd 2 |
| `PasswordResetTests.Always_202_for_unknown_email`, `Link_works_once`, `Link_expires_after_30_minutes` (FakeTime), `Second_request_revokes_first_link`, `Tampered_token_or_account_returns_same_400`, `Weak_password_400_field_error_and_token_kept`, `Confirm_unlocks_account`, `Confirm_does_not_create_session`, `Database_stores_only_token_hash` | integracyjny | Pkt 15–20, kryterium 3 |
| `PasswordResetTests.Reset_invalidates_existing_sessions` (sesja w kliencie A, reset w B, +1 s → A dostaje 401) | integracyjny | Pkt 14, kryterium 4 |
| `PasswordResetTests.Concurrent_confirms_succeed_once` | integracyjny | Decyzja 8, przegląd 8 |
| `PasswordResetTests.Smtp_failure_returns_202_and_logs_without_content` (prawdziwy `SmtpEmailSender` na `127.0.0.1:1`) | integracyjny | Przypadek brzegowy SMTP |
| `OwnerAccountTests.First_reset_for_owner_creates_account_without_password`, `Other_email_never_creates_account`, `Changed_owner_email_does_not_create_second_account`, `Concurrent_first_requests_create_one_account` | integracyjny | Pkt 2–3, kryterium 7, decyzja 1, przegląd 1 |
| `RateLimitingTests.Eleventh_login_in_minute_returns_429`, `Other_ip_not_limited`, `Fourth_reset_from_ip_returns_429`, `Fourth_reset_for_account_from_other_ips_sends_no_email` | integracyjny | Pkt 8, 17, decyzja 5, przegląd 15 |
| `AccessStartupTests.Missing_owner_email_fails`, `Invalid_owner_email_fails`, `Missing_base_url_fails`, `Smtp_missing_host_fails`, `Log_mode_outside_development_fails`, `Log_mode_in_development_logs_link`, `Appsettings_contain_no_owner_email_or_smtp_password` | integracyjny (`StartupFailure`) | Przypadki brzegowe konfiguracji, przegląd 15 |
| `DatabaseUnavailableTests.Login_returns_503_without_detail` (CSRF przy dostępnej bazie, potem podmiana connection stringa w `IConfiguration` na nieosiągalny) | integracyjny | Przypadek 503, przegląd 15 |
| `SecurityLogTests.Logs_contain_no_email_password_or_token` (pełny przepływ w Production, przegląd `FakeLogCollector`) | integracyjny | Kryterium 10, pkt 31 |
| `SecurityLogTests.Security_events_logged_with_account_id_and_correlation` | integracyjny | Pkt 31 |
| `ProblemDetailsAndCorrelationTests.Anonymous_unknown_path_returns_401_problem_details`, `Not_found_returns_problem_details_with_trace_id` (z `X-Test-Account`), `Client_error_keeps_detail_in_production` (CSRF 400), `Production_error_has_no_exception_details` (z `X-Test-Account`) | integracyjny | Zaległość SF-3: `detail` |
| `LaunchDateStartupTests.*` (helper `StartupFailure`) | integracyjny | Poprawka niestabilności |
| `DatabaseTests.All_module_migrations_apply_to_empty_database` (+`access`; tabele `Users`, `PasswordResetTokens`, `DataProtectionKeys`; brak `Roles`/`UserClaims`/`UserLogins`/`UserTokens`) | integracyjny | Storage, migracja `InitialAccess` |
| `OpenApiTests.Document_available_in_development` (bez zmian, wymaga `AllowAnonymous`) | integracyjny | Pkt 22 |

#### Architektury

| Test | Typ | Co dowodzi |
|---|---|---|
| `LayerDependencyTests.*`, `ModuleBoundaryTests.*`, `ProjectReferenceTests.*`, `DbContextSchemaTests.*` dla `Access` | architektury | Kryterium 11, niezmiennik 7 |

### B9. Weryfikacja

Polecenia uruchamiane z katalogu głównego repozytorium.

```
dotnet tool restore
dotnet restore stone-flow.slnx --locked-mode      # po pierwszym restore z nowymi pakietami: commit lockfile'ów
dotnet format stone-flow.slnx --verify-no-changes --no-restore
dotnet build stone-flow.slnx -c Release --no-restore
# lokalnie bez Dockera (PowerShell):
$env:STONEFLOW_TEST_SQLSERVER = 'Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=30'
dotnet test --solution stone-flow.slnx -c Release --no-build
# CI (ubuntu, Docker): bez zmiennej → Testcontainers; ci.yml bez zmian
```

Dodatkowo:
- **Niestabilny test.** Na gałęzi tymczasowo uruchomić w CI pętlę `LaunchDateStartupTests` (30 przebiegów) i zapisać faktyczny typ wyjątku. Krok usunąć przed merge.
- **Migracja lokalnie.** `dotnet tool run dotnet-ef database update --project api/src/Modules/Access/StoneFlow.Access.Infrastructure --startup-project api/src/StoneFlow.Api --context AccessDbContext`.
- **Ręcznie** (API w Development, `Mode=Log`, `Owner:Email` w user-secrets, frontend z proxy):
  1. reset dla adresu właściciela → link z fragmentem w logu → ustawienie hasła → logowanie;
  2. 5 błędnych haseł → blokada z ogólnym komunikatem;
  3. wylogowanie → skopiowane ciasteczko daje 401;
  4. ponowne użycie linku → 400;
  5. `curl -i http://localhost:5080/api/auth/session` → 401 Problem Details;
  6. `curl -i -X POST http://localhost:5080/api/auth/login -H "Content-Type: application/json" -d "{}"` → 400 CSRF;
  7. `curl -i -H "Origin: https://evil.example" http://localhost:5080/health` → brak `Access-Control-Allow-Origin`.
- **Przegląd przez człowieka przed merge (ryzyko high).** Lista kontrolna:
  - kolejność potoku;
  - lista `AllowAnonymous`;
  - brak logowania e-maila, tokenu i hasła;
  - `TestAuthenticationStartupFilter` i `TestRemoteIpStartupFilter` istnieją wyłącznie w `api/tests`;
  - właściciel jawnie potwierdza decyzje z przeglądu specyfikacji, pkt 14: same-origin z przekierowaniem `/api/*` w SF-4, odłożenie D-05, tworzenie konta przez reset, limit 12 h, blokada 5/15 min, wylogowanie wszystkich urządzeń, link we fragmencie.

### B10. Ryzyka

1. **Limity per IP za proxy (decyzja 5).** Bez `ForwardedHeaders` limity za przekierowaniem Vercel będą globalne. Konfiguracja zaufanego proxy przechodzi do SF-4 i trzeba ją zapisać w zgłoszeniu SF-4. Ta sama zależność dotyczy HTTPS: jeśli proxy przekaże ruch po HTTP, antiforgery i ciasteczka `Secure` poza Development przestaną działać.
2. **Blokada konta jako DoS (decyzja 6).** Każdy, kto zna adres właściciela, może podtrzymywać blokadę. Ryzyko przyjęte; reset odblokowuje konto.
3. **Rozróżnialność po czasie (decyzja 11).** Przy logowaniu czas jest wyrównany fikcyjnym hashem. Przy prośbie o reset (tworzenie konta, synchroniczny SMTP) ryzyko przyjęte.
4. **Klucze Data Protection jawne w bazie (decyzja 12).** Kto ma odczyt bazy lub kopii, może podrobić sesję. Szyfrowanie kluczy odłożone do SF-4 i D-08.
5. **D-05 otwarte (decyzja 13).** Bez wybranego dostawcy SMTP reset, czyli jedyna droga utworzenia konta, nie działa na produkcji. Musi być zamknięte przed SF-4.
6. **Rozbieżność ze specyfikacją.** Pkt 16 podaje `?konto=…&token=…`, a plan stosuje fragment `#konto=…&token=…` (decyzja 7). Trzeba zaktualizować `02-spec.md` i potwierdzić z frontendem: odczyt fragmentu, `history.replaceState`, `Referrer-Policy: no-referrer` na trasie.
7. **Wylogowanie kończy sesje na wszystkich urządzeniach (decyzja 2).** Opisane w README.
8. **Odczyt z bazy przy każdym żądaniu.** `ValidationInterval = 0` oznacza odczyt użytkownika przy każdym uwierzytelnionym żądaniu; akceptowalne przy jednym koncie. `SecurityStampValidator` waliduje tylko przy upływie czasu > 0, dlatego testy z `FakeTimeProvider` muszą przesuwać zegar między resetem a kolejnym żądaniem.
9. **Do sprawdzenia przy implementacji.**
   - Czy Identity (blokada) w .NET 10 korzysta z `TimeProvider` z DI. Testy blokady na to nie liczą: sprawdzają przedział czasu rzeczywistego i ustawiają `LockoutEndUtc` w bazie.
   - Cookie i `SecurityStampValidatorOptions` dostają `TimeProvider` jawnie.
   - Wersje MailKit i `Microsoft.Extensions.TimeProvider.Testing` (`dotnet package search`).
10. **Jedno konto w bazie.** Pilnuje go logika aplikacji (brak konta przed utworzeniem + unikalny e-mail), nie ograniczenie DB na liczbę wierszy. Zmiana `Owner:Email` po utworzeniu konta jest ignorowana; zmiana e-maila konta jest poza zakresem.
11. **Niestabilny test startu.** Przyczyna to hipoteza (wyścig zwolnienia hosta w `DeferredHost`) do potwierdzenia w CI. Poprawka nie zależy od niej, bo opiera się na logu hosta, który powstaje przed rzuceniem wyjątku. Jeśli log też okaże się niedeterministyczny, trzeba się zatrzymać i zapytać (limit dwóch prób).
12. **Testy na LocalDB działają tylko na Windows; CI zostaje na Testcontainers.** Dwa silniki (LocalDB i SQL Server 2022 w kontenerze) mogą różnić się drobiazgami, więc rozstrzygające jest CI.
13. **`FallbackPolicy` obejmuje też ścieżki bez endpointu.** Anonimowy `GET /nie-ma` daje 401 zamiast 404. To zgodne z DOST-01; istniejące testy Problem Details trzeba dostosować.
14. **Testowe filtry w projekcie testów.** `TestAuthenticationStartupFilter` to obejście uwierzytelniania żyjące tylko w `api/tests`. Pilnuje tego przegląd człowieka.
15. **`/health` poza `/api`.** Endpoint nie jest dostępny przez proxy frontendu; monitoring woła API bezpośrednio (README).
16. **Potwierdzenia właściciela (przegląd, pkt 14).** Zbiorcza zgoda „lec z /sdd-spec” wymaga jawnego potwierdzenia przy przeglądzie przed merge (sekcja 9).

## Frontend Plan

Plan specjalisty frontendu (`app/`, Angular 22.2.4, standalone, zoneless, sygnały). Bez
nowych zależności npm: `@angular/forms` 22.2.4 zawiera Signal Forms
(`@angular/forms/signals`, `@publicApi 22.0`).

### F1. Zakres

- warstwa HTTP: `provideHttpClient` z XSRF i funkcyjnym interceptorem `401`, wyłącznie
  względne `/api/*`, proxy dev-servera;
- usługa sesji na sygnałach, strażnicy tras, bezpieczny parametr `powrot`;
- trzy ekrany poza powłoką w układzie `layout/auth-layout`: `/logowanie`, `/reset-hasla`,
  `/reset-hasla/nowe`;
- menu użytkownika w nagłówku powłoki (e-mail i „Wyloguj”);
- przebudowa tras: powłoka staje się trasą-układem z sekcjami SF-5 jako dziećmi;
- aktualizacja testów SF-5, `conventions.md` (HTTP i stan, folder `core/`) i README (proxy).

Ochrona tras jest wygodą; dostęp wymusza API (niezmiennik 3).

### F2. Zachowanie XSRF w Angular 22.2.4 (potwierdzone w kodzie `xsrfInterceptorFn`)

Nagłówek `X-XSRF-TOKEN` nie jest dodawany do `GET`/`HEAD` ani do żądań na inny origin; nie
nadpisuje ręcznego nagłówka; ciasteczko `XSRF-TOKEN` jest czytane z `document.cookie` przy
każdym żądaniu (świeży token po `GET /api/auth/csrf` działa od razu). Dlatego w paczce nie
może być absolutnego adresu API.

### F3. Routing

| Ścieżka | Strona | Ładowanie | Strażnik |
|---|---|---|---|
| `''` (układ) | `AuthLayout` (`layout/auth-layout.ts`) | `component` | — |
| ↳ `logowanie` | `LoginPage`, title „Logowanie” | `loadComponent` | `canActivate: [guestGuard]` (zalogowany → `/pulpit`) |
| ↳ `reset-hasla` | `PasswordResetRequestPage`, title „Reset hasła” | `loadComponent` | — |
| ↳ `reset-hasla/nowe` | `SetPasswordPage`, title „Ustaw hasło” | `loadComponent` | — |
| `''` (układ) | `AppShell` | `component` | `canActivate` + `canActivateChild: [authGuard]` |
| ↳ `''` | redirect `pulpit` | — | — |
| ↳ sześć sekcji SF-5 (`section()` bez zmian) | strony sekcji | `loadComponent` | dziedziczy |
| `**` | redirect `pulpit` (przez `authGuard`) | — | — |

- Dzieci powłoki eksportowane jako `sectionRoutes` (test „route for every navigation item”).
- `canActivate`, nie `canMatch`, bo strażnik potrzebuje `state.url` do `powrot`.
- `authGuard`: `await session.ensureKnown()`; `authenticated` → `true`; inaczej `UrlTree`
  `/logowanie?powrot={ścieżka bez query i fragmentu}`.
- Sesja sprawdzana leniwie w strażnikach (nie `provideAppInitializer`): bez białej strony,
  ekrany resetu nie czekają, jedno żądanie w locie współdzielone; `App` pokazuje
  `app-loading-state` „Sprawdzanie sesji…”, dopóki `session.checking()`.

### F4. Komponenty

| Komponent | Plik | Wejścia / stan |
|---|---|---|
| `App` | `app.ts`, `app.html` | `<router-outlet />` + stan ładowania sesji |
| `AppShell` | `layout/app-shell.*` | jak w SF-5 + `<app-user-menu />` |
| `UserMenu` (`app-user-menu`) | `layout/user-menu.*` | `session.email()`; `busy`, `error`; `app-button` secondary „Wyloguj”, `busyLabel="Wylogowywanie…"` |
| `AuthLayout` (`app-auth-layout`) | `layout/auth-layout.*` | nazwa produktu, `<main id="tresc" tabindex="-1">`, karta z `<router-outlet />`, bez nawigacji |
| `LoginPage` | `features/auth/login-page.*` | Signal Form `{ email, password }`; `notice`, `formError`; link „Nie pamiętam hasła” |
| `PasswordResetRequestPage` | `features/auth/password-reset-request-page.*` | Signal Form `{ email }`; `sent`, `formError`; link „Wróć do logowania” |
| `SetPasswordPage` | `features/auth/set-password-page.*` | Signal Form `{ newPassword, repeatPassword }`; prywatne `link`, `linkInvalid`, `formError` |

Reużycie: `app-field` (`#control` + `[formField]`), `app-button` (`busy`, `type="submit"`),
`app-loading-state`. Komunikaty formularzy jako akapity `role="alert"` / `role="status"`.

### F5. Dostęp do danych

| Usługa | Plik | Zwraca |
|---|---|---|
| `provideAppHttp()` | `core/http/app-http.ts` | `provideHttpClient(withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }), withInterceptors([unauthorizedInterceptor]))` |
| `toApiError()` | `core/http/api-error.ts` | `ApiError`: `network` (status 0 lub ≥ 500, w tym 503) \| `validation { fieldErrors, detail }` (400 z `errors`, klucze bez wielkości liter) \| `problem { status, detail, code }`; `NETWORK_ERROR_MESSAGE` |
| `AuthApi` | `core/auth/auth-api.ts` | sześć endpointów, względne URL-e, DTO camelCase |
| `AuthSession` | `core/auth/auth-session.ts` | sygnały i przepływy (F6) |
| `unauthorizedInterceptor` | `core/auth/unauthorized-interceptor.ts` | `401` poza `/api/auth/login` przy `authenticated` → `session.expire()`; błąd zawsze przekazywany dalej |
| `safeReturnUrl()` | `core/auth/return-url.ts` | poprawny `powrot` albo `/pulpit` |
| `parseResetLink()` | `features/auth/reset-link.ts` | `{ accountId, token } \| null` z fragmentu |

### F6. Stan

- `AuthSession` (root): `state: { status: 'unknown' } | { status: 'anonymous' } |
  { status: 'authenticated'; email }`; pochodne `email`, `checking`.
  - `ensureKnown()`: jedno żądanie w locie; `200` → `authenticated`; `401` → `anonymous`;
    sieć / 5xx → `anonymous` + notice `unreachable`.
  - `login()`: csrf → login → csrf → session → `authenticated`; błędy jako `ApiError`.
  - `logout()`: csrf → logout → csrf → `anonymous`, notice `logged-out`, `/logowanie`.
  - `expire()`: idempotentne (tylko z `authenticated`): `anonymous`, notice `expired`, csrf
    (fire-and-forget), `/logowanie?powrot=…`.
  - `passwordSet()`: `anonymous`, notice `password-set`, `/logowanie`.
  - `takeNotice()`: jednorazowy odczyt; komunikaty nie trafiają do URL ani
    `history.state`.
- Lokalnie: modele Signal Forms, `formError`, `sent`, `linkInvalid`, `busy`.
- Walidacja UI: login `required` („Podaj adres e-mail.”, „Podaj hasło.”); reset `required` +
  `email` („Podaj poprawny adres e-mail.”); nowe hasło `required`, `minLength 12` („Hasło
  musi mieć co najmniej 12 znaków.”); powtórzenie — zgodność („Hasła nie są takie same.”);
  „różne od e-maila” tylko w API. Formularze `novalidate`.
- Mapowanie błędów: `validation` → pola; `problem 401` (login) → tekst ze spec; `429` → tekst
  limitu; `400` z `code=csrf_invalid` → „Odśwież stronę i spróbuj ponownie.”; `400` z
  `code=reset_link_invalid` → `linkInvalid` (integracja: rozróżnienie po `code`, nie po
  samym statusie); `network` → „Nie udało się połączyć z serwerem.”; po błędzie logowania
  hasło czyszczone, e-mail zostaje; e-mail wysyłany po `trim()`.
- Fragment linku: `SetPasswordPage` czyta `ActivatedRoute.snapshot.fragment`, parsuje do
  prywatnego pola i od razu `router.navigate([], { relativeTo, replaceUrl: true, fragment:
  undefined })` — przed jakimkolwiek żądaniem; brak/niepełny fragment → `linkInvalid`;
  token nigdy w `console`, szablonie, sygnale ani URL.
- `safeReturnUrl(raw)`: tylko `^/[a-z0-9-]+(/[a-z0-9-]+)*$` z pierwszym segmentem z
  `NAV_ITEMS`; reszta (w tym `//…`, `/\…`, trasy auth, query) → `/pulpit`.

### F7. Style

Tokeny SF-5 (`bg-base-100`, `bg-base-200`, `text-(--sf-muted)`, `text-(--sf-error)`,
`text-(--sf-success)`, `border-(--sf-border-strong)`), zero literałów kolorów. Główna akcja
`primary`: „Zaloguj” („Logowanie…”), „Wyślij link” („Wysyłanie…”), „Ustaw hasło”
(„Zapisywanie…”); „Wyloguj” secondary. Układ auth: jedna kolumna, karta `max-w-md`.
Nagłówek: `app-user-menu` z `basis-full min-w-0 … lg:ml-auto lg:basis-auto`, e-mail
`truncate`; poniżej 1024 px stały wiersz pod nazwą produktu (poza zwijanym menu); brak
poziomego przewijania przy 320 px.

### F8. Dostępność

Kolejność fokusu w powłoce: skip link → produkt → „Menu” → nawigacja → „Wyloguj”. Pola:
e-mail `type="email" autocomplete="username" autocapitalize="none" spellcheck="false"`,
hasło `autocomplete="current-password"`, nowe hasła `autocomplete="new-password"`; wszystkie
`required` przez `app-field`. Notice `expired`/`logged-out`/`password-set` i sukces resetu
`role="status"`; `unreachable` i błędy formularzy `role="alert"`. Fokus po wyniku: pierwsze
pole z błędem; błąd ogólny / sukces / `linkInvalid` → komunikat (`tabindex="-1"`).

### F9. Pliki

| Plik | Akcja | Cel |
|---|---|---|
| `app/proxy.conf.mjs` | create | `/api` → `process.env.STONEFLOW_API_URL ?? 'http://localhost:5080'` |
| `app/angular.json` | modify | `serve.options.proxyConfig` |
| `app/src/app/app.config.ts` | modify | `provideAppHttp()` |
| `app/src/app/app.routes.ts` | modify | dwa układy, `sectionRoutes`, strażnicy, trasy auth |
| `app/src/app/app.ts`, `app.html` | modify | outlet + stan ładowania sesji |
| `app/src/app/core/http/app-http.ts`, `api-error.ts` | create | HTTP i błędy |
| `app/src/app/core/auth/auth-api.ts`, `auth-session.ts`, `auth-guards.ts`, `unauthorized-interceptor.ts`, `return-url.ts` | create | sesja i ochrona |
| `app/src/app/layout/auth-layout.*`, `user-menu.*` | create | układ auth, menu |
| `app/src/app/layout/app-shell.*` | modify | `<app-user-menu />` |
| `app/src/app/features/auth/auth-messages.ts`, `reset-link.ts` | create | teksty, fragment |
| `app/src/app/features/auth/login-page.*`, `password-reset-request-page.*`, `set-password-page.*` | create | ekrany |
| `*.spec.ts` nowych plików | create | testy (F11) |
| `app.spec.ts`, `app.routes.spec.ts`, `layout/app-shell.spec.ts`, `layout/main-nav.spec.ts`, `layout/page-title-strategy.spec.ts` | modify | strażnik i nowa struktura |
| `.sdd/context/conventions.md` | modify | `core/`, „State and data fetching”, trasy auth |
| `README.md` | modify | proxy, `STONEFLOW_API_URL`, `/logowanie` |

### F10. Przypadki brzegowe

Zalogowany na `/logowanie` → `/pulpit`; sekcja bez sesji → `/logowanie?powrot=/sekcja`;
zły `powrot` → `/pulpit`; dowolne `401` poza loginem → `expire()` (idempotentne przy wielu
równoległych); `401` z `session` przy starcie → `anonymous` bez komunikatu; sieć/503 przy
starcie lub logowaniu → komunikat sieci; `429` → tekst limitu; CSRF `400` → „Odśwież…”
(następna próba i tak pobiera świeży token); podwójne wysłanie blokowane; link bez
fragmentu → `linkInvalid` bez żądania; niezgodne hasła → błąd pola bez żądania; po
`passwordSet()` stan `anonymous`; odświeżenie logowania nie powtarza komunikatu.

### F11. Testy (Vitest, `provideAppHttp()` + `provideHttpClientTesting()`, `HttpTestingController.verify()`)

| Test | Co dowodzi |
|---|---|
| `core/http/api-error.spec.ts` | status 0 i 5xx → `network`; 400 z `errors` → `validation` (klucze bez wielkości liter); `problem` z `detail` i `code` |
| `core/auth/auth-api.spec.ts` | względne `/api/auth/...`, metody, body camelCase; `X-XSRF-TOKEN` na POST, brak na GET; odświeżone ciasteczko użyte w kolejnym POST; brak nagłówka na obcy origin |
| `core/auth/return-url.spec.ts` | `/zamowienia`, `/zamowienia/12` OK; `//evil.com`, `https://evil.com`, `/\evil.com`, `/logowanie`, `/reset-hasla`, `/nieznana`, `/pulpit?x=1`, `''`, `null`, `javascript:…` → `/pulpit` |
| `core/auth/auth-session.spec.ts` | `ensureKnown` (jedno żądanie, 200/401/sieć), kolejność `login` i `logout`, `expire` idempotentne z `powrot`, `passwordSet`, `takeNotice` jednorazowe |
| `core/auth/unauthorized-interceptor.spec.ts` | `401` z `/api/x` przy `authenticated` → `expire()`; login/`unknown`/`anonymous` ignorowane; inne statusy bez zmian; dwa `401` → jedna nawigacja |
| `core/auth/auth-guards.spec.ts` | przekierowanie z `powrot` (bez query), sekcja z sesją, `guestGuard`, czekanie na `session` |
| `app.spec.ts` (aktualizacja) | outlet; „Sprawdzanie sesji…” bez `app-shell` w trakcie żądania |
| `layout/auth-layout.spec.ts` | produkt, `main#tresc`, brak `nav` |
| `features/auth/reset-link.spec.ts` | parsowanie fragmentu i dekodowanie |
| `features/auth/login-page.spec.ts` | etykiety, `autocomplete`, walidacja bez żądań, sukces z `powrot`/bez, `401` (alert, hasło puste, e-mail zachowany, fokus), `429`, sieć, VPD, `busy`, notice'y, link resetu |
| `features/auth/password-reset-request-page.spec.ts` | walidacja, `202` → komunikat i fokus, `429`, sieć, VPD, `busy`, csrf przed POST |
| `features/auth/set-password-page.spec.ts` | fragment usunięty przed żądaniem; body `accountId`/`token`; brak fragmentu → link do `/reset-hasla`; walidacja; `204` → `/logowanie`; `reset_link_invalid` → `linkInvalid`; VPD `NewPassword` → błąd pola; `busy`; brak tokenu w `console` |
| `layout/user-menu.spec.ts` | e-mail; „Wyloguj” → csrf, logout, `/logowanie`, „Wylogowano.”; `busy`; błąd sieci; klasy `basis-full min-w-0`, `truncate` |
| `app.routes.spec.ts`, `layout/app-shell.spec.ts`, `layout/main-nav.spec.ts`, `layout/page-title-strategy.spec.ts` (aktualizacje) | `sectionRoutes`; sesja `authenticated` przez flush `session`; trasy auth bez powłoki; menu użytkownika w nagłówku; tytuł „Logowanie · Stone Flow” |

### F12. Ryzyka frontendu

1. Kolejność `401`/`400` CSRF zależy od backendu (decyzja 3).
2. Rozróżnienie `400` po `code` (`csrf_invalid`, `reset_link_invalid`) zależy od backendu.
3. Nazwa i atrybuty `XSRF-TOKEN`: prefiks `__Host-`, `HttpOnly` albo inna ścieżka po
   stronie API wyłączą nagłówek po cichu.
4. Błąd sieci przy sprawdzaniu sesji → `anonymous` (świadoma decyzja).
5. Sekcje SF-5 nie wołają API: po 60 min bezczynności wygaśnięcie wychodzi przy pierwszym
   żądaniu (np. wylogowanie); dane chroni API.
6. Pierwsze użycie Signal Forms — wzorzec trafia do `conventions.md`.
7. 5xx mapowane na komunikat sieci.
8. Teksty spoza spec do potwierdzenia: komunikaty walidacji, „Sprawdzanie sesji…”,
   „Wylogowywanie…”, „Wróć do logowania”, „Poproś o nowy link”, tytuły ekranów.
9. Produkcja zależy od przekierowania `/api/*` w SF-4.
10. Dodatkowy wiersz nagłówka na telefonie (e-mail + „Wyloguj”).
11. Proxy dev: `STONEFLOW_API_URL` dla profilu https.
12. Testy wieloetapowych przepływów wymagają `await` między krokami `HttpTestingController`.

## API Contract

Grupa `/api/auth` (Minimal API w `StoneFlow.Access.Api`), JSON camelCase, błędy jako
Problem Details z `traceId` i rozszerzeniem `code`.

| Metoda | Ścieżka | Żądanie | Odpowiedź | Auth | CSRF |
|---|---|---|---|---|---|
| GET | `/api/auth/csrf` | — | `204` + ciasteczka antiforgery i `XSRF-TOKEN` | anonimowy | — |
| POST | `/api/auth/login` | `{ email, password }` | `204` + sesja + nowy `XSRF-TOKEN`; `400` VPD lub `code=csrf_invalid`; `401` `code=invalid_credentials`; `429` `code=rate_limited`; `503` | anonimowy | tak |
| POST | `/api/auth/logout` | — | `204` (rotacja znacznika, nowy anonimowy `XSRF-TOKEN`); `401` | sesja | tak |
| GET | `/api/auth/session` | — | `200 { email }`; `401` | sesja | — |
| POST | `/api/auth/password-reset/request` | `{ email }` | `202` zawsze; `400` VPD / CSRF; `429` | anonimowy | tak |
| POST | `/api/auth/password-reset/confirm` | `{ accountId, token, newPassword }` | `204`; `400` `code=reset_link_invalid`; `400` VPD `errors.newPassword`; `400` CSRF | anonimowy | tak |

Link w e-mailu: `{StoneFlow:App:BaseUrl}/reset-hasla/nowe#konto={accountId}&token={token}`.
Ciasteczka: sesja `__Host-sf_session` (Development: `sf_session`), HttpOnly, Secure poza
Development, Lax; antiforgery `__Host-sf_af` (`sf_af`), HttpOnly, Strict; `XSRF-TOKEN`
czytelne dla JS, Strict, `Path=/`.

## Integration Check

| Frontend oczekuje | Backend zapewnia | Luka |
|---|---|---|
| Sześć endpointów pod względnym `/api/auth/*` | Te same metody i ścieżki | brak |
| `XSRF-TOKEN` bez prefiksu, czytelne dla JS, `Path=/`; nagłówek `X-XSRF-TOKEN` | Tak (decyzja 9) | brak |
| `GET /session` → `{ email }` | `SessionResponse { email }` | brak |
| Body `confirm`: `accountId` (string), `token`, `newPassword` | DTO `string?`, nie-GUID → `reset_link_invalid` | brak |
| Klucze VPD `email`, `password`, `newPassword` | Te same pola | brak (frontend i tak porównuje bez wielkości liter) |
| `401` przy wygasłej sesji, nie `400` CSRF | Kolejność potoku (decyzja 3), test `Expired_session_post_returns_401_not_400` | brak |
| Rozróżnienie `400` na `confirm` | Rozszerzenie `code` (`reset_link_invalid`, `csrf_invalid`) | Plan frontendu pierwotnie traktował każde `400` bez `errors` jako nieważny link → **ustalone: frontend mapuje po `code`** |
| Fragment `#konto=…&token=…` | `ResetLink` generuje fragment | brak (spec pkt 16 zaktualizowany) |
| `503` przy niedostępnej bazie | `ExceptionStatusCodes` → 503 | brak |
| `429` z `detail` | `OnRejected` → Problem Details `rate_limited` | brak |

## Invariant Check

| Niezmiennik | Gdzie egzekwowany | Test, który go dowodzi |
|---|---|---|
| 1. Sprzedaż oddzielona od gotówki | Nie dotyczy | — |
| 2. Kwoty decimal na backendzie | Nie dotyczy | — |
| 3. Dostęp wymuszany przez API | `FallbackPolicy` = zalogowany; `AllowAnonymous` tylko z listy; frontend tylko wygoda (interceptor `401`) | `AnonymousEndpointsTests.*`, `SessionTests.*_returns_401`, `unauthorized-interceptor.spec.ts` |
| 4. Ponowienia bez duplikatów (konto, tokeny) | Unikalny `NormalizedEmail` + „brak konta”; `ReplaceActiveAsync`; warunkowe zużycie tokenu | `OwnerAccountTests.Concurrent_first_requests_create_one_account`, `PasswordResetTests.Second_request_revokes_first_link`, `Concurrent_confirms_succeed_once` |
| 5. Audyt zmian finansowych | Nie dotyczy (zdarzenia bezpieczeństwa w logach) | `SecurityLogTests.*` |
| 6. Sekrety po stronie serwera | Hash tokenu i hasła; logi tylko z id; link we fragmencie; `Log` tylko w Development; hasło SMTP poza appsettings; brak absolutnego URL API w paczce | `SecurityLogTests.Logs_contain_no_email_password_or_token`, `PasswordResetTests.Database_stores_only_token_hash`, `AccessStartupTests.*`, `auth-api.spec.ts`, `set-password-page.spec.ts` |
| 7. Granice modułów i warstw | `Access` w 4 projektach; Domain/Application bez frameworków; Identity/EF/HTTP w Infrastructure i Api | `LayerDependencyTests`, `ProjectReferenceTests`, `ModuleBoundaryTests`, `DbContextSchemaTests` z `Access` |
| 8. Brak danych identyfikujących | `owner@example.com`, `app.example.com` w testach, README i przykładach; e-mail bez nazwy sklepu | `PasswordResetEmailTests.Email_contains_no_shop_identifiers` + przegląd |

## Unified Implementation Order

1. Zaległości SF-3 (osobne commity): `Dispatcher` + testy; `LaunchDateStartupTests`
   (`StartupFailure`).
2. Infrastruktura testów DB: `SqlServerFixture` z `STONEFLOW_TEST_SQLSERVER`, `TestDatabase`.
3. Pakiety (`Directory.Packages.props`), szkielet modułu `Access`, `stone-flow.slnx`, testy
   architektury z `Access`.
4. Domain `Access` + testy jednostkowe.
5. Persistence i Identity (`AccessDbContext`, `AccessUserStore`, konfiguracje), migracja
   `InitialAccess`.
6. Opcje, e-mail (MailKit / Log), logi zdarzeń bezpieczeństwa.
7. Application: porty, handlery + testy jednostkowe.
8. Access.Api: DTO, endpointy, antiforgery, `XsrfCookie`, rate limiting.
9. Host: `SecuritySetup`, `CsrfValidationMiddleware`, `ExceptionStatusCodes`, poprawka
   `ProblemDetailsSetup`, kolejność potoku w `Program.cs`.
10. Testy integracyjne backendu (wszystkie z tabeli backendu), aktualizacja istniejących.
11. Frontend: proxy, `core/http`, `core/auth` (API, sesja, interceptor, strażnicy,
    `return-url`) + testy.
12. Frontend: układy i przebudowa tras; aktualizacja testów SF-5.
13. Frontend: ekrany auth, menu użytkownika + testy.
14. Dokumentacja: `conventions.md` (styl endpointów, moduł `Access`, HTTP i stan, `core/`),
    README (konfiguracja, proxy, LocalDB, link w panelu sklepu), `deploy/api.env.example`.
15. Pełna weryfikacja (sekcja Verification) i ręczny przepływ end-to-end.

## Verification

Backend (z katalogu głównego):

```
dotnet tool restore
dotnet restore stone-flow.slnx --locked-mode
dotnet format stone-flow.slnx --verify-no-changes --no-restore
dotnet build stone-flow.slnx -c Release --no-restore
$env:STONEFLOW_TEST_SQLSERVER = 'Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=30'   # lokalnie
dotnet test --solution stone-flow.slnx -c Release --no-build
```

Frontend (z `app/`): `npm ci`, `npm run format:check`, `npm run lint`, `npm run typecheck`,
`npm run build`, `npm test -- --watch=false`.

Ręcznie (API w Development, `StoneFlow:Email:Mode=Log`, `Owner:Email` w user-secrets,
`npm start` z proxy): pełny przepływ resetu z linkiem z logu (pasek adresu bez `#…` po
otwarciu), logowanie, 5 błędnych haseł, wylogowanie, ponowne użycie linku, `curl` bez
ciasteczka i bez CSRF, klawiatura na trzech ekranach, 320 px z długim e-mailem.

**Ryzyko high: przegląd przez człowieka przed merge.** Lista kontrolna: kolejność potoku,
lista `AllowAnonymous`, brak logowania e-maila/tokenu/hasła, filtry testowe tylko w
`api/tests`, jawne potwierdzenie decyzji właściciela (same-origin z przekierowaniem w SF-4,
odłożenie D-05, konto przez reset, 12 h, blokada 5/15 min, wylogowanie wszystkich urządzeń,
link we fragmencie).

## Risks

Ryzyka backendu: sekcja 10 planu backendu. Ryzyka frontendu: F12. Najważniejsze zbiorczo:
SF-4 musi skonfigurować przekierowanie `/api/*`, HTTPS i zaufane proxy (rate limiting); D-05
musi być zamknięte przed produkcją; klucze Data Protection nieszyfrowane; blokada konta jako
DoS przyjęta; testy na LocalDB tylko lokalnie, rozstrzyga CI.
