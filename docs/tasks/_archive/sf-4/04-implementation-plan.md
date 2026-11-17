# Implementation Plan — SF-4

Tryb: **pojedynczy obszar (infrastruktura wdrożeń)**. Zmiany dotyczą CI/CD, skryptów i
konfiguracji; backend zmienia tylko potok HTTP hosta (HTTPS), frontend tylko konfigurację
Vercel. Nie powstaje nowy kontrakt między warstwami, więc plan fullstack nie jest potrzebny.
Bez podziału na części — zmiana jest spójna i ma jedno miejsce weryfikacji (CI).

## 1. Scope

Workflow `deploy.yml` (CI → migracje → API przez FTPS → frontend na Vercel, z kontrolami po
każdym kroku), wyłączony do zamknięcia D-08; `ci.yml` jako workflow wywoływalny z próbą
wdrożenia (`deploy-dry-run`) i skanem pakietu frontendu; skrypty w `deploy/scripts/`;
konfiguracja Vercel z generatorem wariantu produkcyjnego i testami; HSTS i przekierowanie
HTTPS w API; procedura `docs/wdrozenia.md` z listą warunków włączenia publikacji.

## 2. Affected areas

| Layer | Module / feature | Why |
|---|---|---|
| CI/CD | `.github/workflows`, `.github/actions` | publikacja po kontrolach, próba wdrożenia, skan pakietu |
| Skrypty | `deploy/scripts` | budowa, migracje w kolejności, wysyłka FTPS, kontrole URL, test migracji |
| Host API | `StoneFlow.Api` (`Program.cs`, `Security/`, `web.config`) | HTTPS (HSTS, przekierowanie), logi stdout ANCM |
| Frontend | `app/vercel.json`, `app/scripts` | routing SPA, `/api/*` tylko w produkcji, brak automatycznych preview |
| Testy | `StoneFlow.Api.IntegrationTests` | HSTS i przekierowanie HTTPS |
| Dokumentacja | `docs/wdrozenia.md`, `README.md`, `conventions.md` | procedura, warunki włączenia, reguły zgodności migracji i API |

## 3. Files

| File path | Action | Purpose |
|---|---|---|
| `.github/workflows/ci.yml` | modify | `workflow_call`, bez `push: main`, anulowanie tylko dla PR, skan `dist/`, testy skryptów, zadanie `deploy-dry-run` |
| `.github/workflows/deploy.yml` | create | publikacja produkcyjna |
| `.github/actions/setup-gitleaks/action.yml` | create | jedna instalacja gitleaks (wersja + SHA256) dla zadań `secrets`, `frontend`, `deploy` |
| `deploy/scripts/contexts.sh` | create | jedna lista kontekstów EF w ustalonej kolejności (projekt + kontekst) |
| `deploy/scripts/migrate.sh` | create | migracje w kolejności, zatrzymanie na pierwszym błędzie |
| `deploy/scripts/publish-api.sh` | create | `dotnet publish` API do katalogu wyjściowego |
| `deploy/scripts/upload-api.sh` | create | FTPS: `app_offline.htm` → mirror z wykluczeniami → usunięcie `app_offline.htm` |
| `deploy/scripts/wait-for-url.sh` | create | GET z ponowieniami, oczekiwany status 200 |
| `deploy/scripts/check-fresh-commit.sh` | create | przerywa publikację, gdy commit nie jest bieżącą głową `main` |
| `deploy/scripts/test-migrate.sh` | create | próba migracji: pusta baza, ponownie, błąd w środku sekwencji, nieosiągalny serwer |
| `deploy/app_offline.htm` | create | strona prac serwisowych na czas wysyłki |
| `api/src/StoneFlow.Api/web.config` | create | ANCM in-process, stdout do `.\logs\stdout` |
| `api/src/StoneFlow.Api/Security/HttpsSetup.cs` | create | opcje HSTS i przekierowania (poza Development) |
| `api/src/StoneFlow.Api/Program.cs` | modify | `UseHsts`/`UseHttpsRedirection` poza Development |
| `api/tests/StoneFlow.Api.IntegrationTests/Security/HttpsTests.cs` | create | HSTS na hoście innym niż localhost, przekierowanie HTTP→HTTPS, brak w Development |
| `app/vercel.json` | create | wariant bazowy (preview-safe) |
| `app/scripts/vercel-config.mjs` | create | generator wariantu produkcyjnego z walidacją originu |
| `app/scripts/vercel-config.test.mjs` | create | testy `node --test` |
| `app/package.json` | modify | skrypt `test:scripts` |
| `docs/wdrozenia.md` | create | procedura |
| `README.md` | modify | odnośnik do procedury, `deploy/` w układzie repozytorium |
| `.sdd/context/conventions.md` | modify | expand/contract, zgodność API z poprzednim frontendem, kolejność potoku z HTTPS |

## 4. Implementation steps

1. **Kontekst EF i migracje** (`deploy/scripts/contexts.sh`, `migrate.sh`):
   - lista: `Access/AccessDbContext`, `Orders/OrdersDbContext`, `Expenses/ExpensesDbContext`,
     `Reporting/ReportingDbContext`, `Integrations/IntegrationsDbContext`;
   - `migrate.sh` wymaga zmiennej `ConnectionStrings__StoneFlow` (host czyta ją jak każdą
     konfigurację; nie trafia do argumentów procesu), wykonuje dla każdego kontekstu
     `dotnet ef database update --no-build --configuration Release …`, wypisuje nazwę
     kontekstu przed i po; `set -euo pipefail`, bez `set -x`; pierwszy błąd → komunikat
     `::error::` z nazwą kontekstu i kod ≠ 0, kolejne konteksty nie są uruchamiane.
   - **Odstępstwo od spec:** zamiast paczek `migrations bundle` — `dotnet ef database update`
     z repozytorium zbudowanego w tym samym zadaniu. Próba lokalna: paczka 1 kontekstu ~1 min
     40 s (×5), `database update --no-build` ~8 s; semantyka ta sama (te same migracje, każda
     w transakcji, kod wyjścia ≠ 0 przy błędzie — sprawdzone). Rozwiązuje też uwagę 9 przeglądu.
2. **Test migracji** (`test-migrate.sh`), parametry: connection string do serwera i komenda
   `sqlcmd` (`SQLCMD`, w CI `docker exec` do kontenera usługi):
   1. nowa baza → `migrate.sh` sukces; sprawdzenie, że istnieje 5 tabel historii;
   2. ponownie → sukces;
   3. nowa baza z przygotowaną kolizją: schemat `orders` i tabela
      `orders.__EFMigrationsHistory` o złej strukturze → `migrate.sh` ≠ 0; `access` zmigrowany,
      schematy `expenses`, `reporting`, `integrations` nie istnieją (sekwencja zatrzymana w
      środku — uwaga 2);
   4. nieosiągalny serwer → ≠ 0;
   5. sprzątanie baz testowych.
3. **Budowa i wysyłka API**:
   - `publish-api.sh`: `dotnet publish api/src/StoneFlow.Api -c Release --no-build -o <out>`
     (framework-dependent, przenośne, `net10.0`, ANCM in-process); kontrola, że wynik nie
     zawiera `appsettings.Development.json` ani `appsettings.Production.json`.
   - `web.config` w projekcie: `hostingModel="inprocess"`, `stdoutLogEnabled="true"`,
     `stdoutLogFile=".\logs\stdout"`; publikacja uzupełnia `processPath`/`arguments`.
   - `upload-api.sh`: `lftp` z `ftp:ssl-force true`, `ssl:verify-certificate true`, hasło przez
     `LFTP_PASSWORD` (`--env-password`); kolejność: `mkdir -p logs`, `put app_offline.htm`,
     `mirror -R --delete --exclude-glob appsettings.Production.json --exclude-glob
     app_offline.htm --exclude ^logs/`, `rm app_offline.htm`.
   - `wait-for-url.sh URL`: `curl` z ponowieniami (np. 10 × 15 s), sukces tylko przy 200.
4. **Świeżość commita** (`check-fresh-commit.sh`): `git ls-remote origin refs/heads/main`
   musi równać się `GITHUB_SHA`; inaczej błąd „nowszy commit na main — publikacja pominięta”
   (ponowne uruchomienie starego przebiegu nie nadpisze nowszego kodu — uwaga 6).
5. **HTTPS w API** (`HttpsSetup.cs`, `Program.cs`): poza Development
   `AddHsts(MaxAge = 365 dni, IncludeSubDomains = false, Preload = false)` i
   `AddHttpsRedirection(RedirectStatusCode = 308, HttpsPort = 443)`; w potoku zaraz po
   `UseExceptionHandler`/`UseStatusCodePages`, przed `UseRouting`. `/health` też jest
   przekierowywane z HTTP (monitoring używa HTTPS).
6. **Vercel** (`app/vercel.json`, `vercel-config.mjs`):
   - bazowy: `$schema`, `installCommand: npm ci`, `buildCommand: npm run build`,
     `outputDirectory: dist/stone-flow/browser`, `framework: null`,
     `git.deploymentEnabled: false`, `rewrites: [{ source: "/((?!api(?:/|$)).*)", destination:
     "/index.html" }]` (pliki statyczne mają pierwszeństwo przed rewrites — zachowanie Vercel);
   - generator: wejście bazowy JSON + `STONEFLOW_API_ORIGIN`; walidacja `URL`: protokół
     `https:`, brak ścieżki innej niż `/`, zapytania, fragmentu, użytkownika i hasła; wynik =
     bazowy z dodanym **na początku** `{ source: "/api/:path*", destination:
     "<origin>/api/:path*" }`; zapis do wskazanego pliku; błąd walidacji → kod 1 bez echa
     wartości poza samym originem.
7. **Akcja gitleaks** (`.github/actions/setup-gitleaks`): przeniesienie instalacji z zadania
   `secrets` (wersja, SHA256) do akcji złożonej; `secrets` używa akcji.
8. **`ci.yml`**: `on: pull_request` + `workflow_call` (bez `push`); `concurrency.cancel-in-
   progress: ${{ github.event_name == 'pull_request' }}`; w `frontend` po buildzie
   `npm run test:scripts` i `gitleaks dir dist --config ../.gitleaks.toml --redact
   --exit-code 1`; nowe zadanie `deploy-dry-run` z usługą `mssql` (obraz jak w
   `SqlServerFixture`), kroki: setup-dotnet, `dotnet tool restore`, restore locked, build
   Release, `publish-api.sh`, `test-migrate.sh`.
9. **`deploy.yml`**: `on: push: branches [main]` i `workflow_dispatch`; `permissions:
   contents: read`; `concurrency: { group: deploy-production, cancel-in-progress: false }`;
   - `ci`: `uses: ./.github/workflows/ci.yml`;
   - `backend`: `needs: ci`, `if: vars.DEPLOY_ENABLED == 'true' && github.ref ==
     'refs/heads/main'`, `environment: production`; kroki: checkout, świeżość commita,
     setup-dotnet, narzędzia, restore, build, `migrate.sh` (env z
     `secrets.PRODUCTION_MIGRATIONS_CONNECTION`), `publish-api.sh`, instalacja `lftp`,
     ponowna kontrola świeżości, `upload-api.sh`, `wait-for-url.sh $API/health`;
   - `frontend`: `needs: backend`, ten sam warunek, `environment: production`; setup-node,
     `npx vercel@64.7.0 pull --yes --environment=production`, generator `vercel.json`,
     `vercel build --prod`, gitleaks na `.vercel/output/static`, `vercel deploy --prebuilt
     --prod`, `wait-for-url.sh` dla `/`, `/logowanie`, `/api/auth/csrf`.
   - Sekrety tylko jako `env` kroku, który ich potrzebuje.
10. **Dokumentacja** (`docs/wdrozenia.md`): architektura wdrożenia; **lista warunków
    włączenia `DEPLOY_ENABLED`** (D-08 zamknięte, D-05/SMTP skonfigurowany, HTTPS na adresie
    API, zdalny SQL z GitHub Actions, runtime 10.0.x na koncie, FTPS); konfiguracja GitHub
    (environment `production` z regułą gałęzi `main`, sekrety i zmienne, `DEPLOY_ENABLED` jako
    zmienna repozytorium edytowalna tylko przez administratora, wymagane kontrole PR:
    `backend`, `frontend`, `secrets`, `deploy-dry-run`); **ograniczone uprawnienia** (token
    Vercel ograniczony do zespołu/projektu, konto FTP tylko do katalogu witryny, osobny login
    SQL do migracji z prawami DDL i login aplikacji tylko z odczytem/zapisem — jeśli hosting
    na to pozwala; uprawnienia workflow `contents: read`); konfiguracja serwera
    (`appsettings.Production.json`, tymczasowa `LaunchDate`); Vercel (projekt, katalog `app`,
    wyłączone wdrożenia Git); kolejność publikacji i zgodność (expand/contract, nowe API z
    poprzednim frontendem); **postępowanie po nieudanej migracji** i inne awarie
    (`app_offline.htm` pozostawiony, `/health` 503, nieudane wdrożenie Vercel); rollback;
    szybkie kolejne pushe (środkowy oczekujący przebieg jest zastępowany); logi stdout i ich
    czyszczenie; monitoring Better Stack (API `/health`, frontend `/logowanie`, certyfikaty,
    alert e-mail); decyzja właściciela z 04.11.2026; kryteria otwarte do włączenia.
11. **README i conventions**: odnośnik, układ `deploy/`; w conventions: migracje
    expand/contract, API zgodne z poprzednią wersją frontendu w ramach jednego wdrożenia,
    potok: korelacja → wyjątki → status pages → HSTS/HTTPS (poza Development) → routing …

## 5. Data & contracts

- Storage: None.
- API endpoints: None (zmiana tylko nagłówków HSTS i przekierowania poza Development).
- Shared types: konfiguracja wdrożenia (zmienne/sekrety) zgodnie ze spec; generator Vercel
  przyjmuje `STONEFLOW_API_ORIGIN`.

## 6. Edge cases & null safety

| Edge case | Gdzie |
|---|---|
| Dwa/trzy szybkie pushe | `concurrency` deploy (kolejka, środkowy oczekujący zastąpiony), `check-fresh-commit.sh` |
| Ponowne uruchomienie starego przebiegu | `check-fresh-commit.sh` (przed migracją i przed wysyłką) |
| Błąd migracji w środku sekwencji | `migrate.sh`; dowód `test-migrate.sh` krok 3 |
| Brak zmian w migracjach | `test-migrate.sh` krok 2 |
| `/health` 503 po publikacji | `wait-for-url.sh` → błąd, frontend pominięty; procedura |
| Przerwana wysyłka FTPS | `app_offline.htm` zostaje; procedura |
| Zły `STONEFLOW_API_ORIGIN` / brak | generator: walidacja, kod 1 |
| Brak `DEPLOY_ENABLED` | warunek `if` zadań → pominięte |
| Brak sekretu (`ConnectionStrings__StoneFlow` puste) | `migrate.sh` sprawdza obecność przed pierwszym kontekstem |
| PR z forka | `deploy.yml` nie reaguje na PR; environment z regułą `main` |

## 7. Invariants

| Invariant | Where it is enforced | Test that proves it |
|---|---|---|
| 6. Secrets stay server-side | sekrety tylko w env kroków `deploy.yml`; połączenie przez zmienną środowiskową; `lftp --env-password`; `vercel.json` bazowy bez API; frontend bez zmiennych | gitleaks `dist/` w CI i `.vercel/output/static` przed publikacją; `vercel-config.test.mjs` (brak `/api` w bazowym, odrzucenie originu z danymi logowania); `publish-api.sh` kontrola braku plików konfiguracji |
| 3. API-enforced access | bez zmian; preview bez drogi do API | `vercel-config.test.mjs` (bazowy nie przepisuje `/api`) |
| 7. Module and layer boundaries | osobny kontekst i schemat na moduł w `contexts.sh` | `test-migrate.sh` (5 tabel historii w 5 schematach) |
| 8. No identifying data in docs | placeholdery w `docs/wdrozenia.md` | przegląd; gitleaks |

## 8. Tests

| Test | Type | What it proves |
|---|---|---|
| `HttpsTests`: Production na `https://api.example.com` → `Strict-Transport-Security` z `max-age=31536000` | integracyjny | HSTS poza Development (host inny niż localhost) |
| `HttpsTests`: Production `http://api.example.com/health` → 308 na `https://api.example.com/health` | integracyjny | przekierowanie HTTPS |
| `HttpsTests`: Development → brak HSTS i przekierowania | integracyjny | lokalny `http` bez zmian |
| `vercel-config.test.mjs`: bazowy — fallback SPA dla `/logowanie`, `/zamowienia/1`; brak przepisania `/api`; `git.deploymentEnabled` false; `outputDirectory` zgodny z `angular.json` | jednostkowy (node:test) | routing SPA i izolacja preview |
| `vercel-config.test.mjs`: produkcja — `/api/:path*` pierwsze, origin poprawny; odrzucenie `http://`, ścieżki, zapytania, danych logowania, pustej wartości | jednostkowy | konfigurowalny adres API, walidacja |
| `test-migrate.sh` kroki 1–4 | próba wdrożenia (CI `deploy-dry-run`) | kolejność, idempotencja, zatrzymanie w środku, błąd połączenia |
| gitleaks `dist/` | CI `frontend` | brak sekretów w pakiecie |
| Istniejące testy backendu i frontendu | regresja | brak zmian zachowania |

Uwaga: dopasowanie wzorców `source` testuje się wyrażeniem regularnym odpowiadającym
składni path-to-regexp użytej we wzorcu; faktyczne zachowanie Vercel weryfikuje kontrola po
publikacji (`/logowanie` → 200) — otwarte do włączenia.

## 9. Verification

- `dotnet build stone-flow.slnx -c Release` i `dotnet format --verify-no-changes`
- `dotnet test --solution stone-flow.slnx -c Release` (LocalDB: `STONEFLOW_TEST_SQLSERVER`)
- `cd app && npm run format:check && npm run lint && npm run typecheck && npm run build &&
  npm test -- --watch=false && npm run test:scripts`
- `deploy/scripts/publish-api.sh` lokalnie; `deploy/scripts/test-migrate.sh` na LocalDB
  (Git Bash, `sqlcmd`)
- składnia: `bash -n deploy/scripts/*.sh`; `actionlint` przez `go run`/obraz, jeśli dostępny,
  inaczej kontrola CI po pushu
- CI na PR: `backend`, `frontend`, `secrets`, `deploy-dry-run` zielone
- **Ręczny przegląd człowieka przed merge (risk high):** workflow `deploy.yml` (warunki,
  sekrety, uprawnienia), kolejność migracji i procedura błędu, `docs/wdrozenia.md`.

## 10. Risks

- Brak kont: `upload-api.sh`, kroki Vercel i kontrole po publikacji nie zostaną uruchomione
  na prawdziwych środowiskach; składnia `lftp`/Vercel CLI sprawdzona tylko dokumentacją i
  lokalnie, jeśli narzędzia są dostępne.
- Zdalny dostęp do SQL na SmarterASP z GitHub Actions niepotwierdzony; bez niego migracje z
  workflow nie zadziałają (alternatywa: skrypt `migrations script --idempotent` uruchamiany w
  panelu — opisana w procedurze jako wariant awaryjny).
- HTTPS na domyślnym adresie API niepotwierdzony; bez niego generator zatrzyma publikację
  frontendu (zamierzone).
- `UseHttpsRedirection` z `HttpsPort = 443` zakłada standardowy port HTTPS na hostingu.
- Wymagane kontrole PR (`deploy-dry-run`) ustawia właściciel w GitHub — poza repozytorium.
- Odstępstwo od spec (krok 1): `database update` zamiast paczek migracji.
