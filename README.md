# Stone Flow

ERP dla sklepu z kamieniem naturalnym: zamówienia ręczne i z WooCommerce, wydatki jednorazowe
i miesięczne oraz raporty sprzedaży i przepływów pieniężnych. Wymagania produktu opisuje
[PRD](docs/PRD-stone-flow.md), a wygląd [design guide](docs/DESIGN-GUID-stone-flow.md).

## Układ repozytorium

| Ścieżka | Zawartość |
|---|---|
| `api/src/` | backend ASP.NET Core: host `StoneFlow.Api`, część wspólna `Shared/`, moduły `Modules/{Orders,Expenses,Reporting,Integrations}` w warstwach Domain, Application, Infrastructure, Api |
| `api/tests/` | testy backendu: architektura, część wspólna, integracyjne |
| `app/` | frontend Angular (testy jednostkowe obok kodu w `app/src`) |
| `deploy/` | konfiguracja wdrożeń; obecnie lokalny SQL Server i wzory zmiennych |
| `docs/` | PRD, design guide, artefakty zadań (`docs/tasks/`) |
| `stone-flow.slnx` | solution dla Visual Studio i `dotnet` CLI |

## Wymagane narzędzia

| Narzędzie | Wersja | Gdzie zapisana |
|---|---|---|
| .NET SDK | 10.0.403 (runtime .NET i ASP.NET Core 10.0.14) | `global.json`, kontrola runtime w CI |
| Node.js | 24.21.0 | `app/.nvmrc`, `engines` w `app/package.json` |
| npm | 11.x | `engines` i `packageManager` w `app/package.json` |
| Docker | dowolna aktualna | lokalny SQL Server i testy integracyjne bazy |
| IDE | Visual Studio z obsługą `.slnx` (Visual Studio 2026 lub 2022 17.14+) albo `dotnet` CLI | — |

Wersje pochodzą z weryfikacji w zadaniu SF-1 (decyzja D-08 w PRD jest jeszcze otwarta). Po jej
zamknięciu właściciel zleca ewentualną korektę wersji jako osobne zadanie. Minimalna wersja
Visual Studio obsługująca `.slnx` nie była sprawdzana w tym repozytorium.

Pakiety NuGet mają wersje w `Directory.Packages.props` i lockfile `packages.lock.json` w każdym
projekcie; zależności npm są przypięte dokładnie w `app/package.json` i `app/package-lock.json`.

## Start lokalny

1. Baza danych:

   ```
   cp deploy/.env.example deploy/.env        # ustaw własne MSSQL_SA_PASSWORD
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d
   ```

2. Backend:

   ```
   dotnet tool restore
   dotnet user-secrets set "ConnectionStrings:StoneFlow" "Server=localhost,1433;Database=StoneFlow;User Id=sa;Password=<hasło z deploy/.env>;TrustServerCertificate=True" --project api/src/StoneFlow.Api
   dotnet user-secrets set "StoneFlow:Owner:Email" "<twój adres e-mail>" --project api/src/StoneFlow.Api
   dotnet ef database update --project api/src/Modules/Access/StoneFlow.Access.Infrastructure --startup-project api/src/StoneFlow.Api --context AccessDbContext
   dotnet ef database update --project api/src/Modules/Orders/StoneFlow.Orders.Infrastructure --startup-project api/src/StoneFlow.Api --context OrdersDbContext
   dotnet ef database update --project api/src/Modules/Expenses/StoneFlow.Expenses.Infrastructure --startup-project api/src/StoneFlow.Api --context ExpensesDbContext
   dotnet ef database update --project api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure --startup-project api/src/StoneFlow.Api --context ReportingDbContext
   dotnet ef database update --project api/src/Modules/Integrations/StoneFlow.Integrations.Infrastructure --startup-project api/src/StoneFlow.Api --context IntegrationsDbContext
   dotnet run --project api/src/StoneFlow.Api --launch-profile http
   ```

   API działa pod `http://localhost:5080`. Aplikacja nie stosuje migracji przy starcie.

3. Frontend:

   ```
   cd app
   npm ci
   npm start
   ```

   Aplikacja działa pod `http://localhost:4200`. Serwer deweloperski przekazuje `/api/*` do API
   (`app/proxy.conf.mjs`; inny adres API: zmienna `STONEFLOW_API_URL`), więc przeglądarka
   rozmawia z jednym originem i ciasteczko sesji jest ciasteczkiem pierwszej strony.

4. Pierwsze logowanie: na `http://localhost:4200/reset-hasla` podaj adres z
   `StoneFlow:Owner:Email`. W Development wiadomość nie jest wysyłana — link do ustawienia hasła
   jest w logu API (`[Development] Wiadomość e-mail niewysłana`). Po ustawieniu hasła zaloguj się
   na `/logowanie`.

## Logowanie i sesja

- Jest jedno konto: właściciela. Powstaje przy pierwszej prośbie o reset hasła dla adresu
  `StoneFlow:Owner:Email`, tylko gdy w bazie nie ma jeszcze żadnego konta (zmiana tego klucza
  później nie tworzy drugiego konta). Rejestracji nie ma, hasła nie ma w konfiguracji.
- Hasło: co najmniej 12 znaków. Po 5 nieudanych próbach logowanie jest blokowane na 15 minut;
  reset hasła odblokowuje konto.
- Sesja wygasa po 60 minutach bezczynności i najpóźniej 12 godzin po zalogowaniu.
  „Wyloguj” oraz ustawienie nowego hasła kończą sesje na **wszystkich** urządzeniach.
- Link resetu jest ważny 30 minut, działa raz, a nowa prośba unieważnia poprzedni link.
- API bez sesji odpowiada `401` na każdy odczyt i zapis. Wyjątki: `GET /health`,
  `GET /api/auth/csrf`, `POST /api/auth/login`, `POST /api/auth/password-reset/request`,
  `POST /api/auth/password-reset/confirm` oraz OpenAPI w Development.

### Link z panelu sklepu

Link do ERP dodaje właściciel ręcznie w panelu administracyjnym sklepu (np. jako własny link w
menu lub widżet), wskazując adres `https://<adres-aplikacji>/logowanie`. Link prowadzi tylko do
ekranu logowania i niczego nie uwierzytelnia; ERP nie zmienia niczego w sklepie.

## Konfiguracja API

| Klucz | Zmienna środowiskowa | Opis |
|---|---|---|
| `ConnectionStrings:StoneFlow` | `ConnectionStrings__StoneFlow` | połączenie z SQL Server; lokalnie przez `dotnet user-secrets`, na środowiskach przez konfigurację wdrożenia |
| `StoneFlow:LaunchDate` | `StoneFlow__LaunchDate` | data uruchomienia w formacie `yyyy-MM-dd` (PRD sekcja 4, „Przełom uruchomienia”) |
| `StoneFlow:Owner:Email` | `StoneFlow__Owner__Email` | adres właściciela; tylko dla niego może powstać konto |
| `StoneFlow:App:BaseUrl` | `StoneFlow__App__BaseUrl` | adres aplikacji używany w linku resetu hasła |
| `StoneFlow:Email:Mode` | `StoneFlow__Email__Mode` | `Smtp` albo `Log` (`Log` tylko w Development: treść wiadomości w logu) |
| `StoneFlow:Email:Smtp:*` | `StoneFlow__Email__Smtp__*` | `Host`, `Port`, `UseStartTls` (`true`: STARTTLS, `false`: TLS od początku połączenia, np. port 465 — nigdy bez szyfrowania), `UserName`, `Password` (tylko sekrety), `FromAddress`, `FromName` |

- Brak lub błąd konfiguracji właściciela, adresu aplikacji albo SMTP zatrzymuje start API
  (komunikat wskazuje klucz, bez wartości). Dostawca SMTP nie jest jeszcze wybrany (PRD D-05).

- Data uruchomienia jest ustawiana wyłącznie w konfiguracji wdrożenia i nie ma jej w
  `appsettings.json`. Brak albo zły format zatrzymuje start API. Środowisko Development ma
  przykładową datę w `appsettings.Development.json`. Aplikacja nie pozwala jej zmienić.
- Wzór zmiennych: `deploy/api.env.example`. Prawdziwych wartości nie zapisuje się w repozytorium.

## Endpointy diagnostyczne

| Ścieżka | Opis |
|---|---|
| `GET /health` | anonimowy; `200 Healthy` albo `503 Unhealthy` (sprawdza połączenie z SQL Server), bez szczegółów |
| `GET /openapi/v1.json` | dokument OpenAPI, tylko w środowisku Development |

Każda odpowiedź ma nagłówek `X-Correlation-ID` (W3C TraceId). Ta sama wartość jest w logach
(`CorrelationId`) i w polu `traceId` odpowiedzi błędów (Problem Details).

## Testy i weryfikacja

Backend (z katalogu głównego):

```
dotnet restore stone-flow.slnx --locked-mode
dotnet format stone-flow.slnx --verify-no-changes --no-restore
dotnet build stone-flow.slnx -c Release --no-restore
dotnet test --solution stone-flow.slnx -c Release --no-build
```

Testy bazy danych (kolekcja „SQL Server” w `StoneFlow.Api.IntegrationTests`) uruchamiają SQL
Server w kontenerze i wymagają Dockera. Bez Dockera można wskazać istniejący serwer, np. LocalDB
(PowerShell):

```
$env:STONEFLOW_TEST_SQLSERVER = 'Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True'
dotnet test --solution stone-flow.slnx -c Release --no-build
```

Każda klasa testów dostaje własną bazę, usuwaną po zakończeniu testów.

Frontend (z katalogu `app/`):

```
npm ci
npm run format:check
npm run lint
npm run typecheck
npm run build
npm test -- --watch=false
```

## CI

Workflow `.github/workflows/ci.yml` uruchamia się przy push i pull request do `main`:

| Zadanie | Sprawdza |
|---|---|
| `backend` | wersja runtime 10.0.14, restore z lockfile, formatowanie, build Release (ostrzeżenia jako błędy), wszystkie testy, w tym bazy danych |
| `frontend` | `npm ci`, Prettier, ESLint, typy (kod i testy), build produkcyjny, testy Vitest |
| `secrets` | skan całej historii repozytorium narzędziem gitleaks (`.gitleaks.toml`) |

## Ochrona gałęzi `main`

Czerwone CI blokuje merge dopiero po włączeniu wymaganych kontroli. To ustawienie GitHub, nie
kod; wykonuje je właściciel repozytorium po pierwszym zielonym przebiegu CI na `main` (nazwy
kontroli muszą już istnieć).

W ustawieniach repozytorium: *Settings → Branches → Add branch ruleset* (lub *branch protection
rule*) dla `main`: wymagane kontrole `backend`, `frontend`, `secrets` i aktualna gałąź przed
merge. Alternatywnie poleceniem:

```
gh api -X PUT "repos/{owner}/{repo}/branches/main/protection" --input - <<'EOF'
{
  "required_status_checks": { "strict": true, "contexts": ["backend", "frontend", "secrets"] },
  "enforce_admins": false,
  "required_pull_request_reviews": null,
  "restrictions": null
}
EOF
```

Sprawdzenie: `gh api "repos/{owner}/{repo}/branches/main/protection"`.
