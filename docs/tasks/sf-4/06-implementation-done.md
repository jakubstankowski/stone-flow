# Implementation done — SF-4

## Summary

Dostarczono proces publikacji: workflow `deploy.yml` (CI → migracje w stałej kolejności → API
przez FTPS → `/health` → frontend na Vercel → kontrole strony, podstrony i przepisania `/api`),
wyłączony zmienną repozytorium `DEPLOY_ENABLED`; `ci.yml` jako workflow wywoływalny z zadaniem
`deploy-dry-run` i skanem sekretów pakietu frontendu; skrypty w `deploy/scripts/`; konfigurację
Vercel z generatorem wariantu produkcyjnego; HSTS i przekierowanie HTTPS w API poza
Development; procedurę `docs/wdrozenia.md`. Publikacja nie była uruchomiona na środowiskach —
konta hostingowe nie istnieją (decyzja właściciela z 04.11.2026, D-08 otwarte).

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Publikacja frontendu na Vercel z routingiem SPA i konfigurowalnym adresem API | Implemented | `app/vercel.json`, `app/scripts/vercel-config.mjs`, zadanie `frontend` w `deploy.yml`; nieuruchomione na Vercel |
| Publikacja backendu na SmarterASP.NET po kontrolach | Implemented | zadanie `backend` (`needs: ci`), `upload-api.sh`; nieuruchomione na hostingu |
| Kontrolowana kolejność migracji i obsługa błędu | Implemented | `contexts.sh`, `migrate.sh`; dowód `test-migrate.sh` (CI `deploy-dry-run`, lokalnie LocalDB) |
| Sekrety poza repozytorium | Implemented | GitHub Environment `production`, `appsettings.Production.json` na serwerze, wykluczenia z paczki |
| Ograniczone uprawnienia | Implemented | `permissions: contents: read`, environment z regułą `main`, `check-fresh-commit.sh`; ustawienia GitHub opisane w `docs/wdrozenia.md` (sekcja 3), niewykonane |
| Izolacja preview | Implemented | bazowy `vercel.json` bez `/api/*`, `git.deploymentEnabled: false` |
| HTTPS | Implemented | `HttpsSetup.cs` (HSTS, 308 na 443) poza Development; generator wymaga `https://` |
| Monitoring dostępności (Better Stack) | Implemented | konfiguracja ręczna opisana w `docs/wdrozenia.md` (sekcja 9); konto nie istnieje |
| Procedura po nieudanej migracji | Implemented | `docs/wdrozenia.md` sekcja 7 |
| Kryterium: oba wdrożenia automatycznie po kontrolach | Implemented | mechanizm; przebieg na środowiskach otwarty |
| Kryterium: odświeżenie podstrony SPA | Implemented | konfiguracja i testy; kontrola `/logowanie` po publikacji otwarta |
| Kryterium: nieudana migracja zatrzymuje publikację | Implemented | `test-migrate.sh` scenariusze 3 i 4 |
| Kryterium: preview bez dostępu do produkcji | Implemented | `vercel-config.test.mjs` |
| Kryterium: pakiet frontendu bez sekretów | Implemented | gitleaks `dist/` w CI, `.vercel/output/static` przed publikacją |
| Kryterium: HSTS i przekierowanie HTTPS | Implemented | `HttpsTests` |

## Files

### Created

- `.github/workflows/deploy.yml`, `.github/actions/setup-gitleaks/action.yml`
- `deploy/app_offline.htm`, `deploy/scripts/{contexts,migrate,publish-api,upload-api,wait-for-url,check-fresh-commit,test-migrate}.sh`
- `api/src/StoneFlow.Api/Security/HttpsSetup.cs`, `api/src/StoneFlow.Api/web.config`
- `api/tests/StoneFlow.Api.IntegrationTests/Security/HttpsTests.cs`
- `app/vercel.json`, `app/scripts/vercel-config.mjs`, `app/scripts/vercel-config.test.mjs`
- `docs/wdrozenia.md`
- `docs/tasks/sf-4/{02-spec,03-spec-review,04-implementation-plan,05-code-review,06-implementation-done}.md`

### Modified

- `.github/workflows/ci.yml`, `.gitignore`
- `api/src/StoneFlow.Api/Program.cs`, `api/src/StoneFlow.Api/StoneFlow.Api.csproj`
- `app/package.json`, `app/.prettierignore`
- `README.md`, `.sdd/context/conventions.md`, `docs/tasks/sf-4/task.md`

## Planned Units

| Item from the plan | Status |
|---|---|
| `contexts.sh`, `migrate.sh` | Exists |
| `test-migrate.sh` (4 scenariusze) | Exists |
| `publish-api.sh`, `web.config`, `upload-api.sh`, `wait-for-url.sh` | Exists |
| `check-fresh-commit.sh` | Exists |
| `HttpsSetup.cs` + potok w `Program.cs` | Exists |
| `app/vercel.json`, `vercel-config.mjs` | Exists |
| Akcja `setup-gitleaks` | Exists |
| `ci.yml` (`workflow_call`, anulowanie tylko PR, `deploy-dry-run`, skan `dist`) | Exists |
| `deploy.yml` | Exists |
| `docs/wdrozenia.md`, README, conventions | Exists |

## Database

None. Bez nowych migracji.

## Tests

| Test from the plan | Status | Run result |
|---|---|---|
| `HttpsTests` (HSTS, 308, Development bez zmian) | Exists | Passed |
| `vercel-config.test.mjs` (15 testów) | Exists | Passed |
| `test-migrate.sh` scenariusze 1–4 | Exists | Passed (LocalDB); w CI — Not run (uruchomi się na PR) |
| gitleaks `dist/` | Exists | Passed (lokalnie, gitleaks 8.30.1) |
| Istniejące testy backendu i frontendu | Exists | Passed |

## Verification

| Command | Outcome |
|---|---|
| `dotnet format stone-flow.slnx --verify-no-changes` | Passed |
| `dotnet build stone-flow.slnx -c Release` | Passed (0 ostrzeżeń) |
| `dotnet test --solution stone-flow.slnx -c Release` (LocalDB) | Passed (189) |
| `npm run format:check`, `lint`, `typecheck`, `build` | Passed |
| `npm test -- --watch=false` | Passed (25 plików, 200 testów) |
| `npm run test:scripts` | Passed (15) |
| `deploy/scripts/publish-api.sh` (lokalnie) | Passed |
| `deploy/scripts/test-migrate.sh` (LocalDB, po poprawkach z przeglądu) | Passed |
| `bash -n deploy/scripts/*.sh` | Passed |
| `actionlint`, `shellcheck` | Not run (narzędzia niedostępne) |
| CI na PR (`backend`, `frontend`, `secrets`, `deploy-dry-run`) | Not run (przed utworzeniem PR) |
| `deploy.yml` na środowiskach | Not run (brak kont) |

## Deviations

- Migracje uruchamiane `dotnet ef database update --no-build` zamiast paczek `migrations
  bundle` (spec: Scope, Verification `build-api.sh`); skrypt budowy nazywa się
  `publish-api.sh`. Zapisane w planie (krok 1).
- Generator `vercel.json` uruchamiany przed `vercel pull`.
- Kontrola `/api/auth/csrf` po publikacji oczekuje `204` (spec: „każde 200”) — endpoint
  zwraca `204`.

## Additional Implementation

Additional implementation not covered by the original documents:

- `<Content Update="appsettings.Development.json" CopyToPublishDirectory="Never" />` w
  `StoneFlow.Api.csproj`.
- `.vercel/` w `.gitignore` i `app/.prettierignore`.

## Open Items

- **Human review required before merge** (risk high).
- Poprawki po przeglądzie kodu (05) wprowadzone bez ponownego niezależnego przeglądu:
  oczekiwany status `204` dla `/api/auth/csrf` (`wait-for-url.sh` z parametrem statusu),
  przerwa 20 s po `app_offline.htm` w `upload-api.sh`, `ASPNETCORE_ENVIRONMENT=Production` w
  `migrate.sh`, usunięcie końcowego `/` z adresów w `deploy.yml`, odporny `connection_for` w
  `test-migrate.sh`, poprawka README, opis tokenu Vercel w `docs/wdrozenia.md`.
- Nierozwiązane uwagi z przeglądu: `upload-api.sh`, `check-fresh-commit.sh`, `wait-for-url.sh`
  bez testów; adnotacje `::error::` z oczekiwanych błędów w zielonym `deploy-dry-run`;
  powtórzone kroki budowy w trzech zadaniach; brak `actionlint`/`shellcheck`.
- Kryteria wymagające środowisk: przebieg obu publikacji, odświeżenie podstrony na Vercel,
  przepisanie `/api/*` z ciasteczkami `__Host-` i CSRF, sesja przez Vercel (z SF-6).
- Warunki włączenia `DEPLOY_ENABLED` (`docs/wdrozenia.md` sekcja 1): D-08, D-05/SMTP, HTTPS
  adresu API, zdalny SQL, runtime na koncie, ustawienia GitHub/Vercel/Better Stack.
- Szyfrowanie kluczy Data Protection i zaufane proxy — otwarte do konfiguracji hostingu.
- Zgłoszenie SF-4 pozostaje otwarte w trackerze do włączenia publikacji (albo zadanie
  następcze po zamknięciu D-08).
- Wymagane kontrole PR (`deploy-dry-run`) ustawia właściciel w GitHub.
