# Specyfikacja zadania — SF-3

## Źródło

[SF-3](https://standev.youtrack.cloud/issue/SF-3) — Monorepo, CI i szkielet modułów backendu.
PRD: sekcja 4 (Przełom uruchomienia), sekcja 10.

## Cel

Utworzyć odtwarzalny szkielet repozytorium Stone Flow: solution `stone-flow.slnx` z backendem
ASP.NET Core jako modularnym monolitem (moduły Orders, Expenses, Reporting, Integrations, każdy
w czterech warstwach Onion jako osobne projekty), pustą aplikacją Angular w trybie strict,
przypiętymi wersjami narzędzi, EF Core z SQL Server i wersjonowanymi migracjami per moduł,
anonimowym health endpointem, odczytem daty uruchomienia z konfiguracji wdrożenia oraz CI w
GitHub Actions, które blokuje błąd kompilacji, typów, formatowania lub testu. Zadanie nie
zawiera logiki biznesowej.

## Kontekst

Repozytorium zawiera obecnie tylko `docs/`, `documents/` i `.sdd/`; nie ma kodu aplikacji.
SF-3 tworzy struktury, w których powstają wszystkie kolejne zadania (SF-4 … SF-22), dlatego
granice modułów i warstw ustalone tutaj są współdzielone przez cały projekt (ryzyko medium).

Warstwy w nazewnictwie konwencji projektu: domena (reguły biznesowe), aplikacja (przypadki
użycia, CQRS), infrastruktura (dostęp do danych, usługi zewnętrzne), API (HTTP). Moduły
początkowe według PRD sekcja 10: zamówienia (także wpłaty i zwroty), wydatki (także wypłaty
właścicieli), raportowanie, integracje.

Projekty referencyjne wskazane w zgłoszeniu: `C:\projects\inne\autoecom` (układ `api/` +
`app/`, `global.json`, `Directory.Build.props`, xUnit v3 na Microsoft.Testing.Platform) oraz
`C:\projects\inne\sit` (klasyczne warstwy Core/Application/Infrastructure/API). Ze względu na
wybrany podział (4 projekty na moduł) SF-3 przejmuje z nich układ katalogów i konfigurację
narzędzi, a nie strukturę modułów.

## Zakres

### W zakresie

- Solution `stone-flow.slnx` w katalogu głównym repozytorium, otwierane w Visual Studio.
- Struktura monorepo:

  | Ścieżka | Zawartość |
  |---|---|
  | `api/src/` | projekty backendu |
  | `api/tests/` | testy backendu |
  | `app/` | aplikacja Angular wraz z jej testami jednostkowymi |
  | `deploy/` | konfiguracja wdrożeń (w SF-3 tylko lokalny `docker-compose` z SQL Server i przykładowe pliki konfiguracji) |
  | `docs/` | dokumentacja (istniejąca) |
  | `.github/workflows/` | CI |

- Projekty backendu (przestrzenie nazw `StoneFlow.*`):

  | Projekt | Rola | Może zależeć od |
  |---|---|---|
  | `StoneFlow.Api` | host ASP.NET Core: kompozycja modułów, OpenAPI, Problem Details, logowanie, health | `*.Api` i `*.Infrastructure` modułów, `StoneFlow.Shared.*` |
  | `StoneFlow.Shared.Kernel` | mały, wspólny rdzeń domeny bez logiki modułów | nic |
  | `StoneFlow.Shared.Application` | abstrakcje CQRS (`ICommand`, `IQuery`, `ICommandHandler`, `IQueryHandler`, dyspozytor) oraz abstrakcja daty uruchomienia | `Shared.Kernel` |
  | `StoneFlow.Shared.Infrastructure` | implementacja dyspozytora, odczyt daty uruchomienia z konfiguracji, wspólne ustawienia EF Core | `Shared.Application` |
  | `StoneFlow.{Modul}.Domain` | domena modułu (pusta w SF-3) | `Shared.Kernel` |
  | `StoneFlow.{Modul}.Application` | przypadki użycia modułu (puste w SF-3) | `{Modul}.Domain`, `Shared.Application` (kontrakty innych modułów — patrz Założenia) |
  | `StoneFlow.{Modul}.Infrastructure` | `DbContext` modułu, migracje, rejestracja zależności | `{Modul}.Application`, `Shared.Infrastructure`, EF Core |
  | `StoneFlow.{Modul}.Api` | rejestracja endpointów modułu (brak endpointów w SF-3) | `{Modul}.Application`, ASP.NET Core |

  gdzie `{Modul}` ∈ `Orders`, `Expenses`, `Reporting`, `Integrations`.
- Projekty testów: `StoneFlow.Architecture.Tests` (reguły zależności), `StoneFlow.Api.IntegrationTests`
  (health, Problem Details, konfiguracja daty uruchomienia, migracje na SQL Server w kontenerze),
  `StoneFlow.Shared.Tests` (dyspozytor CQRS, parsowanie daty uruchomienia). Testy jednostkowe
  domen modułów powstają w zadaniach, które dodają logikę.
- Przypięte wersje: `global.json` (SDK + `rollForward`), `Directory.Build.props`
  (`net10.0`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`), `Directory.Packages.props`
  (centralne wersje pakietów NuGet), `.nvmrc` / `engines` w `app/package.json` (Node),
  `package-lock.json`, `.editorconfig`.
- Backend: JSON API z dokumentem OpenAPI, Problem Details dla błędów, korelacja logów.
- EF Core z SQL Server: osobny `DbContext` na moduł, każdy we własnym schemacie (`orders`,
  `expenses`, `reporting`, `integrations`), z własną tabelą historii migracji w tym schemacie
  i z migracją początkową tworzącą tylko schemat.
- Health endpoint `/health`.
- Odczyt daty uruchomienia z konfiguracji wdrożenia, dostępny dla modułów.
- Aplikacja Angular w trybie strict: standalone, jedna strona startowa, lazy routing gotowy na
  obszary funkcjonalne, Vitest, ESLint, Prettier.
- CI w GitHub Actions: kompilacja obu aplikacji, typy, formatowanie, lint, testy.
- `README.md` z instrukcją startu i przykładowa konfiguracja bez sekretów.
- Uzupełnienie TODO w `.sdd/context/conventions.md` i `.sdd/context/role.md`, które SF-3
  rozstrzyga (ścieżki, wersje, nazewnictwo, komendy weryfikacji).

### Poza zakresem

- Publikacja na hostingi Vercel i SmarterASP.NET oraz kontrolowane stosowanie migracji na
  środowiskach (SF-4).
- Motyw daisyUI, Tailwind CSS i powłoka aplikacji (SF-5).
- Uwierzytelnianie, autoryzacja, CORS i sesja (SF-6).
- Jakiekolwiek encje, tabele, endpointy biznesowe i logika domenowa.
- Trwały harmonogram zadań (SF-11).
- Ostateczne rozstrzygnięcie D-08.

## Zachowanie

1. Programista klonuje repozytorium i wykonuje kroki z `README.md`: instaluje wersje SDK i
   Node podane w `global.json` i `.nvmrc`, uruchamia SQL Server z `deploy/docker-compose.yml`,
   ustawia connection string i datę uruchomienia przez `dotnet user-secrets` lub zmienne
   środowiskowe (wzór w pliku przykładowym), stosuje migracje lokalnie poleceniem `dotnet ef`
   opisanym w README, uruchamia API i aplikację Angular.
2. API przy starcie waliduje konfigurację. Brak daty uruchomienia lub wartość niebędąca datą
   w formacie `yyyy-MM-dd` kończy start aplikacji błędem z czytelnym komunikatem (bez wartości
   sekretów). `appsettings.Development.json` zawiera przykładową datę, więc start lokalny działa
   bez dodatkowej konfiguracji daty.
3. Data uruchomienia jest jednym dniem (`DateOnly`) bez strefy czasowej, dostępnym dla modułów
   przez abstrakcję w `Shared.Application` (tylko odczyt). Aplikacja nie udostępnia żadnej
   ścieżki zapisu ani zmiany tej wartości; zmiana wymaga zmiany konfiguracji wdrożenia i
   restartu.
4. API nie stosuje migracji automatycznie przy starcie.
5. `GET /health` jest anonimowy. Sprawdza działanie aplikacji i połączenie z SQL Server.
   Zwraca `200` z treścią `Healthy`, gdy wszystko działa, a `503` z `Unhealthy`, gdy baza jest
   niedostępna. Odpowiedź nie zawiera szczegółów, nazw serwerów, wyjątków ani danych.
6. Każde żądanie dostaje identyfikator korelacji równy `TraceId` bieżącej `Activity`
   (W3C `traceparent`; przy braku nagłówka generowany nowy). Identyfikator trafia do zakresu
   logów, do nagłówka odpowiedzi `X-Correlation-ID` i do pola `traceId` w Problem Details.
   Logi są pisane przez `Microsoft.Extensions.Logging` w formacie JSON na konsolę.
7. Nieobsłużony wyjątek i odpowiedzi błędów 4xx/5xx bez treści zwracają Problem Details
   (`application/problem+json`) bez stack trace i szczegółów wyjątku poza środowiskiem
   Development.
8. Dokument OpenAPI jest dostępny pod `/openapi/v1.json` w środowisku Development.
9. Dyspozytor CQRS rozwiązuje handler komendy lub zapytania z kontenera DI i wywołuje go
   asynchronicznie z `CancellationToken`. Brak zarejestrowanego handlera kończy się wyjątkiem.
   Bez MediatR.
10. Aplikacja Angular buduje się w trybie produkcyjnym i pokazuje stronę startową z nazwą
    produktu po polsku. Nie wywołuje API.
11. CI uruchamia się przy push i pull request do `main`. Zadania: backend (restore z
    zablokowanymi wersjami, `dotnet format --verify-no-changes`, build Release z ostrzeżeniami
    jako błędami, testy, w tym integracyjne z SQL Server w kontenerze) oraz frontend
    (`npm ci`, Prettier check, ESLint, build produkcyjny ze strict templates, testy Vitest).
    Niepowodzenie dowolnego kroku oznacza czerwone CI.

## Dane / API

### Storage

- SQL Server: cztery schematy — `orders`, `expenses`, `reporting`, `integrations` — każdy
  tworzony migracją początkową swojego `DbContext`. Każdy kontekst ma własną tabelę historii
  migracji w swoim schemacie (`{schemat}.__EFMigrationsHistory`). Brak tabel biznesowych.
- Migracje są w projekcie `StoneFlow.{Modul}.Infrastructure`, w katalogu `Migrations/`.

### Endpointy

| Metoda | Ścieżka | Żądanie | Odpowiedź | Auth | Zakres |
|---|---|---|---|---|---|
| GET | `/health` | — | `200 Healthy` / `503 Unhealthy`, `text/plain` | anonimowy | aplikacja + SQL Server |
| GET | `/openapi/v1.json` | — | dokument OpenAPI | anonimowy, tylko Development | — |

### Typy przekraczające granicę

- Abstrakcja daty uruchomienia (`Shared.Application`) — zwraca `DateOnly`. Nazwa typu ustalana
  w planie.
- Interfejsy CQRS (`Shared.Application`).
- Klucz konfiguracji daty uruchomienia: `StoneFlow:LaunchDate` (zmienna środowiskowa
  `StoneFlow__LaunchDate`), format `yyyy-MM-dd`.
- Connection string: `ConnectionStrings:StoneFlow` (wspólna baza, osobne schematy).

## Wpływ na niezmienniki

| Niezmiennik | Jak jest respektowany |
|---|---|
| 1. Sprzedaż oddzielona od gotówki | Nie dotyczy — brak logiki finansowej. Granica modułów zamówień i wydatków przygotowuje rozdzielenie. |
| 2. Kwoty decimal na backendzie | Nie dotyczy — brak kwot. Frontend nie zawiera żadnych obliczeń. |
| 3. Dostęp wymuszany przez API | Brak endpointów z danymi. Jedyny wyjątek to anonimowy `/health`, który nie zwraca danych finansowych ani szczegółów infrastruktury (decyzja z pytań). OpenAPI tylko w Development. Uwierzytelnianie dochodzi w SF-6. |
| 4. Ponowienia bez duplikatów | Nie dotyczy. |
| 5. Audyt zmian finansowych | Nie dotyczy — brak danych finansowych. |
| 6. Sekrety po stronie serwera | Repozytorium nie zawiera connection stringów z hasłami ani kluczy; lokalnie `user-secrets`/zmienne środowiskowe, w repo tylko pliki przykładowe z placeholderami. Hasło SA w `docker-compose` pochodzi z pliku `.env` ignorowanego przez git (wzór `.env.example`). Logi i Problem Details nie zawierają connection stringów. CI skanuje repozytorium narzędziem do wykrywania sekretów. |
| 7. Granice modułów i warstw | Granice wymuszone referencjami projektów (Domain nie ma referencji do ASP.NET Core, EF Core ani hostingu) oraz testami architektury w CI: domena nie zależy od `Microsoft.AspNetCore*`, `Microsoft.EntityFrameworkCore*` ani `Microsoft.Extensions.Hosting*`; projekt modułu nie odwołuje się do projektów Domain/Application/Infrastructure innego modułu; każdy `DbContext` mapuje wyłącznie schemat swojego modułu. |
| 8. Brak danych identyfikujących w dokumentach | README i pliki przykładowe nie zawierają nazwy handlowej ani domeny sklepu, danych klientów ani sekretów. |

## Ryzyko

**medium** — zgodnie z `task.md`. Brak pieniędzy, danych klientów i nieodwracalnych operacji,
ale struktura, granice modułów, konfiguracja wersji i CI są współdzielone przez wszystkie
kolejne zadania; błąd tutaj powiela się w każdym module. Przegląd pod kątem wszystkich
modułów-konsumentów.

## UI

Nie dotyczy — poza stroną startową bez funkcji. Motyw i powłoka należą do SF-5. Strona
startowa: tekst po polsku, `lang="pl"` w `index.html`.

## Przypadki brzegowe

- Brak `StoneFlow:LaunchDate` → API nie startuje, komunikat wskazuje brakujący klucz.
- `StoneFlow:LaunchDate` w złym formacie (np. `03.11.2026`, `2026-02-30`, data z godziną) →
  API nie startuje.
- SQL Server niedostępny przy starcie → API startuje (brak migracji przy starcie), `/health`
  zwraca `503 Unhealthy`.
- Żądanie z nagłówkiem `traceparent` → ten sam `TraceId` w logach, `X-Correlation-ID` i
  Problem Details.
- Żądanie na nieistniejącą ścieżkę → `404` jako Problem Details.
- Wersja SDK lub Node niezgodna z przypiętą → `dotnet` / `npm` zgłasza błąd zgodnie z
  `global.json` (`rollForward: latestPatch`) i `engines` (`engine-strict`).
- Zmiana pliku bez formatowania → CI czerwone.
- Domena modułu z dodaną referencją do EF Core albo projekt modułu z referencją do innego
  modułu → testy architektury czerwone.

## Zależności

- **SF-1 / D-08** — niezamknięte. Wersje przypinane są według odczytu z
  `docs/tasks/sf-1/01-weryfikacja.md`; po zamknięciu D-08 wersje mogą wymagać korekty w
  osobnej zmianie. Zgodnie z PRD D-08 blokuje ostateczną konfigurację środowisk, której SF-3
  nie obejmuje (SF-4).

## Założenia

Odpowiedzi na pytania doprecyzowujące (03.11.2026):

- **Wersje przy otwartym D-08:** przypiąć odczyt z SF-1 — .NET SDK 10.0.403 / runtime 10.0.14,
  Angular 22.2.x (najnowsza stabilna z dokładnym numerem i zgodnym TypeScript ustalonym przy
  instalacji), Node 24 LTS w wersji spełniającej `^24.15.0`. Wersje są założeniem do
  potwierdzenia po zamknięciu D-08.
- **Podział warstw:** cztery projekty (Domain, Application, Infrastructure, Api) na każdy z
  czterech modułów.
- **Układ repozytorium:** jak w autoecom — `api/`, `app/`, plus `deploy/`, solution w katalogu
  głównym.
- **Testy i CQRS:** xUnit v3 na Microsoft.Testing.Platform; własny prosty dyspozytor bez
  MediatR.
- **Solution:** `stone-flow.slnx` (format XML), przestrzenie nazw i projekty `StoneFlow.*`.
  Zgłoszenie mówi „sln”; przyjęto, że chodzi o solution Visual Studio, a nie o format pliku.
- **EF Core:** `DbContext` na moduł, każdy we własnym schemacie z własną historią migracji.
- **Health:** `/health` anonimowy, sprawdza aplikację i SQL Server, bez szczegółów w odpowiedzi;
  jawny wyjątek od niezmiennika 3.
- **Data uruchomienia:** brak lub zły format blokuje start (walidacja przy starcie); dev ma
  przykładową datę w `appsettings.Development.json`.
- **Frontend:** goła aplikacja strict (standalone, Vitest, ESLint, Prettier); Tailwind CSS i
  daisyUI w SF-5.
- **Logi:** wbudowany `ILogger` z JSON na konsolę; korelacja przez `Activity.TraceId`, nagłówek
  `X-Correlation-ID`, `traceId` w Problem Details.
- **CI i baza:** GitHub Actions (repozytorium jest na GitHub); SQL Server w kontenerze lokalnie
  (`docker-compose`) i w testach integracyjnych (Testcontainers).

Pozostałe założenia przyjęte przy pisaniu:

- „moduły nie sięgają do swoich encji” w kryteriach odbioru rozumiane jako: moduły nie sięgają
  do encji innych modułów.
- Komunikacja między modułami (jawne interfejsy lub zdarzenia) nie jest potrzebna w SF-3; jeśli
  pojawi się, moduł wystawia osobny projekt `StoneFlow.{Modul}.Contracts`. SF-3 nie tworzy
  projektów Contracts.
- SF-3 nie dodaje endpointów modułów, więc nie ustala stylu endpointów (kontrolery czy Minimal
  API); ustali go pierwsze zadanie z endpointem (SF-6). Projekty `*.Api` zawierają tylko metodę
  rejestracji modułu.
- Migracje stosuje lokalnie programista poleceniem `dotnet ef`; kontrolowana kolejność migracji
  na środowiskach to SF-4.
- Format: `dotnet format` według `.editorconfig` i Prettier dla `app/`; lint: angular-eslint.
- Wykrywanie sekretów w CI (np. gitleaks) jest sposobem weryfikacji kryterium „Repozytorium nie
  zawiera sekretów”, a nie nowym wymaganiem produktu.
- Centralne zarządzanie pakietami z włączonym `RestorePackagesWithLockFile` / `--locked-mode` w
  CI, aby restore był odtwarzalny.
- Druga ścieżka referencyjna w zgłoszeniu (`C:\projects\inne\sitC:\projects\inne\sit`) to
  `C:\projects\inne\sit`.
- W rules.md pozostaje TODO dotyczące maksymalnej długości pliku — nie należy do SF-3.

## Kryteria odbioru

- [ ] Projekt da się odtworzyć i uruchomić według instrukcji w `README.md`, z zapisanymi
      wersjami (`global.json`, `Directory.Packages.props`, lockfile NuGet i npm, `.nvmrc`).
- [ ] `stone-flow.slnx` otwiera się w Visual Studio i zawiera wszystkie projekty z tabeli.
- [ ] CI przechodzi dla frontendu i backendu i blokuje błąd typów, formatowania, lintu lub
      testu (sprawdzone celowo zepsutą gałęzią).
- [ ] Domena nie zależy od HTTP, EF Core ani hostingu, a moduły nie sięgają do encji innych
      modułów — wymuszone referencjami projektów i testami architektury.
- [ ] Każdy moduł ma `DbContext` we własnym schemacie z migracją początkową; migracje
      stosują się na pustej bazie SQL Server.
- [ ] `GET /health` odpowiada `200 Healthy` przy dostępnej bazie i `503 Unhealthy` przy
      niedostępnej, bez szczegółów.
- [ ] Data uruchomienia jest dostępna z konfiguracji dla modułów; brak lub zły format blokuje
      start; aplikacja nie ma ścieżki jej zmiany.
- [ ] Błędy zwracają Problem Details z `traceId`; odpowiedź ma `X-Correlation-ID` równy
      identyfikatorowi w logach.
- [ ] Dokument OpenAPI jest dostępny w Development.
- [ ] Aplikacja Angular buduje się w trybie strict i przechodzi testy, lint i format.
- [ ] Repozytorium nie zawiera sekretów (skan w CI przechodzi; tylko pliki przykładowe).
- [ ] TODO w `.sdd/context/conventions.md` i `role.md` rozstrzygnięte przez SF-3 są uzupełnione.

## Weryfikacja

- build: `dotnet build stone-flow.slnx -c Release`; `npm ci && npm run build` w `app/`
- testy: `dotnet test --solution stone-flow.slnx` (z Dockerem dla testów integracyjnych);
  `npm test` w `app/`
- format/lint: `dotnet format stone-flow.slnx --verify-no-changes`;
  `npm run format:check && npm run lint` w `app/`
- ręcznie:
  - świeży klon → kroki z README → API i aplikacja startują;
  - `curl -i http://localhost:{port}/health` przy uruchomionym i zatrzymanym kontenerze SQL;
  - usunięcie `StoneFlow:LaunchDate` → API nie startuje;
  - żądanie na nieistniejącą ścieżkę → Problem Details z `traceId` zgodnym z `X-Correlation-ID`
    i logiem;
  - gałąź z błędem formatowania, typu i testu → CI czerwone dla każdego przypadku.
