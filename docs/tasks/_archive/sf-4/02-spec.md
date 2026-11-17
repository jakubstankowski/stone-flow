# Task Specification — SF-4

## Source

[SF-4](https://standev.youtrack.cloud/issue/SF-4)

## Goal

Przygotować w repozytorium kompletny, automatyczny proces publikacji frontendu (Vercel) i
backendu (SmarterASP.NET) uruchamiany dopiero po przejściu kontroli CI na `main`, z kontrolowaną
kolejnością migracji, zatrzymaniem publikacji po nieudanej migracji, sekretami wyłącznie w
GitHub Environment `production`, izolacją preview od produkcji, HTTPS i opisanym monitoringiem.
Konta hostingowe nie istnieją (D-08 otwarte), więc publikacja na prawdziwe środowiska jest
wyłączona przełącznikiem, a wszystko, co da się sprawdzić bez kont (budowa paczek, kolejność i
zatrzymanie migracji, routing SPA w konfiguracji Vercel, brak sekretów w pakiecie frontendu),
jest sprawdzane w CI na każdym PR.

## Context

- **CI/CD:** `.github/workflows/ci.yml` (zadanie SF-3) i nowy `.github/workflows/deploy.yml`.
- **Skrypty wdrożeniowe:** `deploy/scripts/` (bash, uruchamiane w GitHub Actions na Ubuntu).
- **Backend:** host `StoneFlow.Api` (potok HTTP: HTTPS, HSTS), pięć kontekstów EF Core
  (`AccessDbContext`, `OrdersDbContext`, `ExpensesDbContext`, `ReportingDbContext`,
  `IntegrationsDbContext`) z migracjami w warstwie Infrastructure każdego modułu. Aplikacja nie
  stosuje migracji przy starcie (SF-3) i tak zostaje.
- **Frontend:** `app/` — konfiguracja Vercel (`app/vercel.json`) i skrypt generujący jej
  wariant produkcyjny w `app/scripts/`. Frontend woła API względnym `/api` (SF-6), więc na
  Vercel `/api/*` jest przepisywane na origin API (jeden origin, ciasteczko pierwszej strony).
- **Dokumentacja:** `docs/wdrozenia.md` (procedura), `README.md`, `.sdd/context/conventions.md`.

## Scope

### In scope

- Workflow publikacji: CI → backend (migracje → publikacja API → kontrola `/health`) →
  frontend (Vercel → kontrola strony i podstrony), uruchamiany po pushu na `main`.
- Przełącznik `DEPLOY_ENABLED` (zmienna repozytorium); bez niego zadania publikacji są
  pomijane, a CI działa normalnie.
- Paczki migracji EF (`migrations bundle`) dla pięciu kontekstów i skrypt uruchamiający je w
  stałej kolejności, zatrzymujący się na pierwszym błędzie.
- Publikacja API przez FTPS z `app_offline.htm` i zachowaniem plików konfiguracji serwera.
- Konfiguracja Vercel: fallback SPA, `/api/*` na origin API tylko w produkcji, wyłączone
  automatyczne wdrożenia Git (bez niekontrolowanych preview).
- Kontrole w CI na PR: budowa paczek wdrożeniowych, migracje na pustej bazie, ponowne
  uruchomienie bez zmian, zatrzymanie przy błędzie migracji, testy konfiguracji Vercel, skan
  sekretów w zbudowanym pakiecie frontendu.
- HTTPS po stronie API: HSTS i przekierowanie na HTTPS poza Development.
- Procedura w `docs/wdrozenia.md`: przygotowanie środowisk, sekrety, kolejność, postępowanie
  po nieudanej migracji, rollback, monitoring Better Stack.

### Out of scope

- Kopie zapasowe i próba odtworzenia (SF-21, zgodnie ze zgłoszeniem).
- Zakup i konfiguracja kont SmarterASP.NET, Vercel i Better Stack oraz włączenie publikacji —
  decyzja użytkownika z 04.11.2026; D-08 pozostaje otwarte.
- Wybór dostawcy e-mail (D-05) — decyzja użytkownika z 04.11.2026 („póki co bez e-maila”).
- Własna domena — użytkownik jej nie ma; domyślne adresy dostawców.
- Szyfrowanie kluczy Data Protection w bazie i zaufane proxy (`ForwardedHeaders`) — patrz
  Assumptions.
- Staging (osobne API i baza dla preview).

## Behavior

### Kontrole na PR (`ci.yml`)

1. Istniejące zadania `backend`, `frontend`, `secrets` bez zmian zachowania.
2. Zadanie `frontend` dodatkowo: testy skryptu konfiguracji Vercel (`node --test`) oraz skan
   gitleaks katalogu zbudowanego pakietu (`dist/`); znaleziony sekret kończy zadanie błędem.
3. Nowe zadanie `deploy-dry-run`:
   1. buduje paczkę API (`dotnet publish`, Release) i pięć paczek migracji tym samym skryptem,
      którego używa publikacja;
   2. uruchamia migracje na pustej bazie SQL Server (kontener usługi) w kolejności
      access → orders → expenses → reporting → integrations; wszystkie muszą się udać;
   3. uruchamia je ponownie — brak zmian i sukces (idempotencja);
   4. uruchamia je na nieosiągalnym serwerze — skrypt musi zakończyć się błędem na pierwszej
      paczce i nie uruchomić kolejnych (dowód „nieudana migracja zatrzymuje publikację”).
4. `ci.yml` jest też wywoływalny (`workflow_call`); na `main` uruchamia go `deploy.yml`, więc
   CI nie wykonuje się dwa razy dla tego samego commita. Anulowanie poprzedniego przebiegu
   dotyczy tylko PR, nigdy `main`.

### Publikacja (`deploy.yml`, push na `main`)

1. Zadanie `ci` wywołuje `ci.yml`. Bez zielonego CI nic nie jest publikowane.
2. Gdy `vars.DEPLOY_ENABLED != 'true'`, zadania publikacji są pomijane (stan obecny).
3. Zadanie `backend` (environment `production`):
   1. buduje paczkę API i paczki migracji z tego samego commita;
   2. uruchamia migracje w ustalonej kolejności połączeniem z sekretu
      `PRODUCTION_MIGRATIONS_CONNECTION`; pierwszy błąd kończy zadanie — kod API nie jest
      publikowany, frontend nie jest publikowany, działa poprzednia wersja;
   3. wysyła `app_offline.htm`, synchronizuje pliki przez FTPS (usuwa nieaktualne), nie dotyka
      `appsettings.Production.json` ani katalogu `logs/`, usuwa `app_offline.htm`;
   4. sprawdza `GET {STONEFLOW_API_ORIGIN}/health` z ponowieniami; brak `200` kończy zadanie
      błędem (frontend nie jest publikowany).
4. Zadanie `frontend` (environment `production`, po `backend`):
   1. generuje produkcyjny `vercel.json` z `STONEFLOW_API_ORIGIN`;
   2. `vercel pull`, `vercel build --prod`, skan gitleaks pakietu, `vercel deploy --prebuilt
      --prod`;
   3. sprawdza `GET {STONEFLOW_APP_URL}/`, `GET {STONEFLOW_APP_URL}/logowanie` (odświeżenie
      podstrony) i `GET {STONEFLOW_APP_URL}/api/auth/csrf` (przepisanie do API) — każde `200`.
5. Publikacje nie biegną równolegle i nie są anulowane w trakcie (grupa współbieżności
   `deploy-production`, `cancel-in-progress: false`).

### Konfiguracja Vercel

- Repozytoryjny `app/vercel.json` (każde wdrożenie poza produkcyjnym workflow, w tym ręczne
  preview): komendy budowy, katalog wyjściowy, `git.deploymentEnabled: false`, fallback SPA
  do `index.html` dla ścieżek innych niż `/api/*`. **Brak przepisania `/api/*`** — preview nie
  ma drogi do API, bazy ani integracji; `/api/*` zwraca 404.
- Wariant produkcyjny (tylko w workflow) dodaje przed fallbackiem przepisanie `/api/:path*` →
  `{STONEFLOW_API_ORIGIN}/api/:path*`. Origin musi być `https://`, bez ścieżki, zapytania i
  danych logowania; inaczej generator kończy się błędem.

### HTTPS w API

- Poza Development: `UseHsts()` i `UseHttpsRedirection()`. W Development bez zmian (`http`).
- Ciasteczka sesji już wymagają HTTPS (`__Host-`, SF-6).

### Postępowanie po nieudanej migracji (opis w `docs/wdrozenia.md`)

- Publikacja jest zatrzymana, działa poprzednia wersja kodu; baza jest w stanie po ostatniej
  udanej migracji (każda migracja EF w osobnej transakcji).
- Nie edytować zastosowanych migracji; naprawa nową migracją („fix forward”) i ponowne
  uruchomienie workflow. Gdy dane są uszkodzone — odtworzenie kopii (procedura SF-21).
- Rollback kodu nie cofa migracji (PRD sekcja 10); migracje muszą być zgodne z poprzednią
  wersją kodu (expand/contract), bo uruchamiają się przed publikacją nowego kodu.

## Data / API

### Storage

- None. Bez nowych migracji; zmienia się tylko sposób ich uruchamiania na środowiskach.

### Endpoints

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| — | — | — | — | — | Bez nowych endpointów; `GET /health` (anonimowy) używany przez kontrolę po publikacji i monitoring |

### Types crossing a boundary

- Konfiguracja wdrożenia (nie w repozytorium):
  - zmienne repozytorium/environment: `DEPLOY_ENABLED`, `STONEFLOW_API_ORIGIN`,
    `STONEFLOW_APP_URL`, `VERCEL_ORG_ID`, `VERCEL_PROJECT_ID`;
  - sekrety environment `production`: `VERCEL_TOKEN`, `PRODUCTION_MIGRATIONS_CONNECTION`,
    `API_FTP_HOST`, `API_FTP_USER`, `API_FTP_PASSWORD`, `API_FTP_PATH`;
  - na serwerze, poza repozytorium: `appsettings.Production.json` z kluczami z
    `deploy/api.env.example` (połączenie aplikacji, data uruchomienia, właściciel, adres
    aplikacji, SMTP).

## Invariant impact

| Invariant | How it is respected |
|---|---|
| 6. Secrets stay server-side | Sekrety tylko w GitHub Environment `production` i w pliku konfiguracji na serwerze; frontend nie dostaje zmiennych środowiskowych, woła względne `/api`; skan gitleaks zbudowanego pakietu w CI i przed publikacją; skrypty nie wypisują połączeń ani haseł (maskowanie GitHub, brak `set -x`). |
| 3. API-enforced access | Bez zmian w autoryzacji; preview nie ma przepisania do API, a CORS API odrzuca obce originy (SF-6). |
| 7. Module and layer boundaries | Każdy kontekst ma własną paczkę migracji i schemat; kolejność nie tworzy zależności między modułami. Zmiany w hoście tylko w potoku HTTP. |
| 8. No identifying data in docs | Dokumentacja i przykłady używają placeholderów (`<adres-api>`, `example.com`); prawdziwe adresy tylko w zmiennych GitHub. |

## Risk

high — operacje nieodwracalne (migracje produkcyjnej bazy) i dostęp do sekretów produkcyjnych.
Wymagany przegląd człowieka przed merge.

## UI

Not applicable.

## Edge Cases

- Dwa szybkie pushe na `main`: drugi przebieg czeka na koniec pierwszego (bez anulowania w
  trakcie migracji).
- Migracja jednego kontekstu się powiodła, kolejnego nie: wcześniejsze konteksty pozostają
  zmigrowane (zgodne z poprzednim kodem dzięki expand/contract), kolejne nie są uruchamiane.
- Brak zmian w migracjach: paczki kończą się sukcesem bez zmian.
- `/health` zwraca `503` (baza niedostępna) po publikacji: zadanie kończy się błędem, frontend
  nie jest publikowany, `app_offline.htm` jest już usunięty (API działa, lecz zgłasza
  niezdrowe) — opisane w procedurze.
- Przerwanie wysyłki FTPS: `app_offline.htm` zostaje na serwerze (API wyłączone, komunikat
  prac serwisowych) do ponownego uruchomienia workflow — opisane w procedurze.
- `STONEFLOW_API_ORIGIN` bez `https://` lub ze ścieżką: generator `vercel.json` przerywa
  publikację frontendu.
- Brak `DEPLOY_ENABLED`: zadania publikacji pominięte, workflow zielony.
- Fork / PR: `deploy.yml` nie uruchamia się dla PR; sekrety environment dostępne tylko z
  `main` (reguła gałęzi environment).

## Dependencies

- SF-1 (częściowo): D-08 otwarte — publikacja pozostaje wyłączona do jego zamknięcia.
- SF-3: CI, migracje per kontekst, brak migracji przy starcie.
- SF-6: względne `/api`, ciasteczka `__Host-`, CORS deny-all, konfiguracja SMTP.

## Assumptions

- **Q1 Konta hostingowe / D-08:** „narazie odpuszczamy hosting robimy bez”. Workflow i
  procedura powstają bez kont; publikacja wyłączona przełącznikiem `DEPLOY_ENABLED`, który
  wolno ustawić dopiero po zamknięciu D-08. To nie jest ostateczna konfiguracja środowisk, więc
  nie narusza blokady D-08. Kryteria wymagające działających środowisk zostają otwarte.
- **Q2 Metoda publikacji API:** bez odpowiedzi (brak konta). Przyjęto FTPS przez `lftp` na
  Ubuntu z `app_offline.htm` — nie wymaga Web Deploy na runnerze. Do potwierdzenia w panelu.
- **Q3 Migracje:** paczki EF uruchamiane z GitHub Actions przed publikacją kodu. Wymaga
  zdalnego dostępu do SQL na hostingu — niepotwierdzone (otwarte).
- **Q4 Kopia przed migracją:** pozostawiona dla SF-21 (propozycja przyjęta).
- **Q5 Zatwierdzanie:** automatycznie po zielonym CI na `main` (propozycja przyjęta).
  Sekrety w environment `production` z regułą gałęzi `main`. Widoczność repozytorium i plan
  GitHub niepotwierdzone — jeśli reguły environment są niedostępne, procedura wskazuje
  alternatywę (sekrety repozytorium + warunek `github.ref == 'refs/heads/main'` w workflow,
  który i tak jest stosowany).
- **Q6 Vercel:** publikacja przez Vercel CLI (`vercel@64.7.0`) z GitHub Actions, automatyczne
  wdrożenia Git wyłączone.
- **Q7 Preview:** bez przepisania `/api/*` i bez stagingu (propozycja przyjęta).
- **Q8 Domena:** „nie mam własnej domeny” — domyślne adresy dostawców, w repozytorium tylko
  placeholdery.
- **Q9 Monitoring:** „monitoring better stack” — dwa monitory HTTP (API `/health`, frontend
  `/logowanie`) i kontrola certyfikatu, alert e-mailem; konfiguracja ręczna opisana w
  procedurze (konto nie istnieje).
- **Q10 Data uruchomienia:** tymczasowa wartość w konfiguracji serwera, do zmiany przed
  startem (propozycja przyjęta).
- **Q11 Logi:** stdout ANCM do `logs/` na serwerze, przez `web.config` w projekcie API
  (propozycja przyjęta). Pliki nie są rotowane — procedura opisuje okresowe czyszczenie.
- **Q12 Zaufane proxy:** Vercel nie ma stałych adresów wychodzących i nie może dodać
  nagłówka uwierzytelniającego do przepisanego żądania, więc zaufanie do `X-Forwarded-For`
  pozwalałoby obejść limity bezpośrednim wywołaniem API. **Nie włączamy `ForwardedHeaders`**;
  limity działają per adres wyjściowy Vercel (przy jednym użytkowniku akceptowalne). TLS kończy
  IIS hostingu, więc API widzi HTTPS bez nagłówków. Otwarte do weryfikacji na hostingu.
- **D-05 e-mail:** „póki co bez emaila”. Produkcyjne API nie wystartuje bez konfiguracji
  SMTP (SF-6) — to warunek włączenia publikacji, zapisany w procedurze.
- Szyfrowanie kluczy Data Protection (otwarte z SF-6) wymaga certyfikatu na hostingu —
  pozostaje otwarte do konfiguracji hostingu.
- API publikowane jako framework-dependent (`net10.0`) — zakłada runtime 10.0.x na hostingu
  (deklaracja oferty, niepotwierdzone na koncie).
- Paczki migracji używają konfiguracji hosta (brak fabryk design-time), tak jak lokalne
  `dotnet ef database update` z README.

## Acceptance Criteria

- [ ] Oba wdrożenia uruchamiają się automatycznie po przejściu kontroli. *(mechanizm: CI w
  `deploy.yml` przed publikacją; działanie na środowiskach otwarte do włączenia `DEPLOY_ENABLED`)*
- [ ] Odświeżenie podstrony SPA nie powoduje błędu routingu. *(konfiguracja testowana w CI;
  kontrola `/logowanie` po publikacji otwarta)*
- [ ] Nieudana migracja zatrzymuje publikację, a procedura postępowania jest opisana.
  *(dowód w `deploy-dry-run`; `docs/wdrozenia.md`)*
- [ ] Preview nie ma dostępu do produkcyjnej bazy ani integracji. *(brak przepisania `/api/*`
  w repozytoryjnym `vercel.json`, sekrety tylko w environment `production`)*
- [ ] Pakiet frontendu nie zawiera sekretów. *(skan gitleaks `dist/` w CI i przed publikacją)*
- [ ] API poza Development wysyła HSTS i przekierowuje HTTP na HTTPS (test integracyjny).

## Verification

- build: `dotnet build stone-flow.slnx -c Release`; `npm run build` w `app/`.
- tests: `dotnet test --solution stone-flow.slnx -c Release`; `npm test -- --watch=false`;
  `node --test app/scripts/`.
- deploy dry run: `deploy/scripts/build-api.sh` i `deploy/scripts/migrate.sh` lokalnie na
  LocalDB/kontenerze (pusta baza, ponownie, nieosiągalny serwer); zadanie `deploy-dry-run` w
  CI zielone.
- manual: `actionlint` na workflow (jeśli dostępny); po włączeniu hostingu — przebieg
  `deploy.yml`, odświeżenie `/logowanie`, logowanie przez Vercel, monitory Better Stack.
