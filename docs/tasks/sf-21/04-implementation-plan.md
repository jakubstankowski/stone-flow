# Implementation Plan — SF-21

Tryb: **fullstack, bez nowego kontraktu między warstwami.** Zadanie zmienia część backendową
(pakiet testów integracyjnych scenariuszy PRD §11, skrypty `deploy/scripts/`, zadania CI) i
frontendową (Playwright E2E z axe, skan pakietu, ewentualne poprawki dostępności), a do tego
dokumenty operacyjne. Kodu produkcyjnego API nie zmienia: brak endpointów, migracji i reguł
finansowych. Plany backendu i frontendu napisał jeden autor po kolei (bez subagentów): najpierw
backend, potem frontend. Kontrakt, z którego korzysta E2E, wyprowadzono ze spec (sekcje Context,
Data / API) i z kodu tras Angulara, nie z planu backendu. Oba porównano w „Integration Check”.

**Podział na części: nie.** Wszystkie jednostki trafiają do jednego PR. Checklista `odbior-mvp.md`
odwołuje się do nazw testów i zadań CI z tego samego PR, a zadanie `e2e` ma być wymaganą kontrolą
od chwili merge. Żadna część nie ma sensu bez pozostałych. Jednostki są jednak ułożone tak, żeby
każdą dało się zatwierdzić osobnym commitem i sprawdzić osobno (sekcja „Unified Implementation
Order”).

Ryzyko `high`: **przegląd człowieka przed merge** (sekcja „Verification”, krok V-H).

## Scope

- **Skrypty (`deploy/scripts/`):** wydzielenie `sql-common.sh` z `test-migrate.sh` (bez zmiany
  zachowania); nowe `verify-restore.sh` i `test-verify-restore.sh`; nowy krok w zadaniu CI
  `deploy-dry-run`.
- **Testy integracyjne:** `api/tests/StoneFlow.Api.IntegrationTests/Acceptance/MvpFinancialScenariosTests.cs`
  oraz pomocnik `Acceptance/AcceptanceTest.cs` (sekcja H spec: 13 scenariuszy, uzgodnienie, zmiana
  minionego okresu).
- **Frontend (`app/`):** E2E w `app/e2e/` (Playwright 1.65.0, `@axe-core/playwright` 4.14.0,
  `@types/node` 24.19.2), `playwright.config.ts`, `tsconfig.e2e.json`, skrypty `e2e` i `typecheck`,
  wzorce `ng lint`, `.gitignore` i `.prettierignore`; skan `app/scripts/check-bundle-secrets.mjs` z
  testem; poprawki dostępności tylko po punkcie kontrolnym F-A0.
- **CI:** nowe zadanie `e2e` w `ci.yml`; skan pakietu w zadaniu `frontend` i w `deploy.yml`;
  kontrola nowej reguły gitleaks w zadaniu `secrets`; `test-verify-restore.sh` w `deploy-dry-run`.
- **Konfiguracja skanu:** reguła `woocommerce-rest-key` w `.gitleaks.toml`.
- **Dokumenty:** `docs/operations/kopie-i-odtworzenie.md`, `powrot-do-wersji.md`,
  `data-uruchomienia.md`, `odbior-mvp.md`; zmiany w `wdrozenia.md` (§1, §3, §7),
  `.sdd/context/conventions.md` (układ repozytorium, Verification) i `README.md` (uruchomienie E2E,
  po uzgodnieniu z autorem bieżących zmian, Assumptions 15).
- **Bez zmian:** `api/src/**` (chyba że F-A0 wykaże naruszenie dostępności w Angularze, wtedy tylko
  `app/src/**`), schemat bazy, migracje, `contexts.sh`, `migrate.sh`, `check-fresh-commit.sh`,
  `LaunchDateStartupTests`, `EndpointSecurityTests`, `deploy/scripts/demo-data.sql` (nieśledzony
  plik innej osoby, nie wchodzi do zadania).

### Rozstrzygnięcia uwag z przeglądu (`03-spec-review.md`)

| # | Uwaga przeglądu | Decyzja | Gdzie w planie |
|---|---|---|---|
| 1 | „Wyloguj” w teście 27.1 unieważnia wspólną sesję `storageState` (`LogoutHandler` → `RotateSecurityStampAsync`) | Test 27.1 trafia do **osobnego pliku `e2e/session.e2e.ts` i osobnego projektu Playwright `session`**, który ma `dependencies: ['chromium']`, więc rusza dopiero po wszystkich testach korzystających z sesji. Nie używa `storageState`: loguje się sam w dwóch nowych kontekstach przeglądarki (A i B). W projekcie `chromium` żaden test nie wylogowuje. Na początku każdego przebiegu projekt `setup` loguje się od nowa i zapisuje świeży `storageState`, więc kolejny przebieg nie zależy od poprzedniego. Logowania na przebieg: `setup` 1 + `session` 3 (złe hasło, A, B), a przy ponowieniu w CI dochodzą 3. To 7 z 10/min, w limicie. Skutek uboczny: gdy któryś test `chromium` nie przejdzie, Playwright pomija `session`, ale CI i tak jest czerwone, a pominięcie widać w raporcie. Lokalnie test da się uruchomić sam: `npx playwright test --project=session --no-deps`. | F-E2, F-E9, test E-1 |
| 2 | Izolacja danych E2E na wspólnej bazie | (a) **Unikalny znacznik** na test: `tag = e2e-<czas base36>-<4 losowe znaki>` w opisie, kontrahencie, nazwie klienta i komentarzu. Rekord testu znajduje się po `search=<tag>` (listy zamówień i wydatków mają `search`) albo przez filtr listy w UI. (b) **Asercje na różnicach:** kwoty na Pulpicie i w „Historii zmian” odczytuje się przed i po akcji. (c) **Rozłączne daty biznesowe:** testy Pulpitu i Historii zmian mają własne dni w **poprzednim miesiącu** (27.5 → 12., 27.6 → 14.) i własny zakres niestandardowy obejmujący tylko ten dzień. Testy 27.2–27.4 i 28 używają dat z bieżącego miesiąca i nie są czytane przez testy kwot. Dzięki (b) wynik jest poprawny także po ponowieniu w CI i przy lokalnej bazie wielokrotnego użytku. Kwoty z UI („12 345,67 zł”) zamienia na grosze (liczba całkowita) pomocnik `e2e/support/money.ts`, bez arytmetyki zmiennoprzecinkowej. | F-E1, F-E4, F-E5 |
| 3 | Dzień terminu wydatku cyklicznego w E2E (28) | **Dzień miesiąca = 1, początek cyklu = 1. dzień bieżącego miesiąca** (Europe/Warsaw). Wystąpienie z datą kosztu 1. dnia bieżącego miesiąca zawsze jest ≤ dzisiaj, więc powstaje przy zapisie definicji (SF-16), także w dniu 1. Początek cyklu jest po dacie uruchomienia E2E (1. dzień miesiąca sprzed dwóch miesięcy). | F-E4, test E-6 |
| 4 | Wielkość liter i fałszywe trafienia skanu pakietu | **Wszystkie reguły rozróżniają wielkość liter i są zakotwiczone** (tabela w B-S2). Fragmenty connection stringu pasują tylko po początku tekstu, `;`, cudzysłowie albo białym znaku i tylko z niepustą wartością (`Password=x`). Dlatego `e.uid=1`, `n.Password=t`, `type="password"` ani etykieta „Hasło” nie są trafieniami. Nazwy kluczy serwera pasują z granicą bez litery i cyfry po obu stronach, więc trafiają też w postać `__` (`StoneFlow__WooCommerce__ConsumerKey`). Test skryptu sprawdza obie strony: każda reguła wykrywa próbkę pozytywną, a zestaw próbek negatywnych typowych dla zminifikowanego JS przechodzi. Prawdziwy build sprawdza krok CI na `dist`. | B-S2, B-S3, testy S-1…S-5 |
| 5 | Powrót backendu, gdy problematyczny commit zawiera migrację | `powrot-do-wersji.md` dostaje **ścieżkę częściowego revertu**: `git revert --no-commit <sha>`, potem przywrócenie z `<sha>` plików migracji, `*ModelSnapshot.cs` i mapowania EF dodanych elementów schematu (`git checkout <sha> -- <ścieżki>`). Cofa się tylko logikę, a migracja zostaje. Warunek konieczny: model EF po revercie musi się zgadzać ze snapshotem. W przeciwnym razie `dotnet ef database update` w `migrate.sh` zatrzyma publikację na ostrzeżeniu o oczekujących zmianach modelu. Kontrola: `dotnet ef migrations has-pending-model-changes` dla każdego kontekstu z `contexts.sh` oraz zielone `deploy-dry-run` na PR revertu. Jeśli migracja była krokiem „contract” (usunięcie, zmiana typu) albo warunek się nie spełnia: **fix forward**, a w ostateczności odtworzenie kopii. | D-2 |
| 6 | Mapowanie design guide §10 niepełne | `odbior-mvp.md` ma **jeden wiersz na każdą z 9 pozycji §10**, w tym brakujące trzy: „cztery kolory zapisane centralnie” → `npm run lint` (`lint:colors`) i tokeny `--sf-*` w `app/src/styles.css` (automatyczny); „układ nawiązuje do referencji” → ocena właściciela na podstawie Pulpitu i referencji z `documents/` (ręczny); „widoki sprzedaży i przepływów różnią etykiety” → E2E 27.5 (podpisy kart po przełączeniu) i istniejące testy komponentów SF-20 (brak „zysk”/„saldo”) (automatyczny). | D-4, test E-5 |
| 7 | Brak `/reset-hasla/nowe` na liście axe i reflow | Strona jest na obu listach. Formularz wyświetla się z fikcyjnym fragmentem `#konto=00000000-0000-0000-0000-000000000000&token=e2e` (strona sprawdza tylko obecność obu wartości i nie wysyła żądania przed zapisem). Strony auth sprawdza się w kontekście bez `storageState`, bo `guestGuard` przekierowuje zalogowanego użytkownika. | F-E7, test E-9 |
| 8 | `typecheck` nie obejmuje `app/e2e/`; `.gitignore` dla wyników Playwright | Nowy `app/tsconfig.e2e.json` (`types: ["node"]`, `include: ["e2e/**/*.ts", "playwright.config.ts"]`); `typecheck` dostaje trzeci krok `tsc -p tsconfig.e2e.json --noEmit`. `ng lint` dostaje wzorce `e2e/**/*.ts` i `playwright.config.ts`. `app/.gitignore` i `app/.prettierignore`: `/playwright-report`, `/test-results`, `/e2e/.auth`. Pliki E2E mają końcówkę `*.e2e.ts` (a nie `*.spec.ts`), żeby nie trafiły do Vitest (`ng test`) ani do `tsconfig.spec.json`. | F-E0, test E-0 |
| 9 | Link do przebiegu CI w `odbior-mvp.md` | Wiersze automatyczne podają **nazwę zadania CI i testu** oraz placeholder `<link do przebiegu CI — uzupełnia właściciel po merge>`. Wykonawca nie dopisuje linku dodatkowym commitem, więc nie trzeba ponownie uruchamiać CI. Placeholder jest otwartą pozycją w `06-implementation-done.md`. | D-4 |

Niejasności z przeglądu:

| Niejasność | Rozstrzygnięcie | Gdzie |
|---|---|---|
| H pkt 38: „stara i nowa wartość” dla dodanego wydatku | Wpis **utworzenia** wydatku: `operation = "created"`, zmiana pola `grossAmount` z `oldValue = null` i `newValue` = kwota. Wpis **edycji** zamówienia: `operation = "updated"`, pole `value` ze starą i nową kwotą (nazwy pól jak w `OrdersApiTests` i `ExpensesApiTests`). Różnica raportu: sprzedaż = `new − old` zamówienia, koszty = kwota wydatku. | B-T5 |
| B pkt 9.6: „tabele główne” | `orders.Orders`, `orders.Payments`, `orders.Refunds`, `orders.SalesCorrections`, `expenses.Expenses`, `expenses.RecurringExpenseDefinitions`, `expenses.OwnerWithdrawals`, `reporting.AuditEntries` (nazwy z modeli EF i snapshotów migracji). Lista jest w jednym miejscu w `verify-restore.sh`. | B-S1 |
| F pkt 26: hasło w katalogu wyników | **Hasło nie jest zapisywane na dysk.** Global setup trzyma je w `process.env.STONEFLOW_E2E_OWNER_PASSWORD` (dziedziczą je procesy robocze Playwright). Na dysku jest tylko `storageState` w `app/e2e/.auth/owner.json` (ignorowany w git, poza artefaktem CI). Projekty `setup` i `session` mają `trace: 'off'`, bo ślad zapisuje wartości wpisane w polach. Przechwycony log API (z linkiem resetu) zostaje w pamięci. Nie jest wypisywany po wysłaniu prośby o reset ani zapisywany do pliku. | F-E2 |
| Scope pkt 7: zakres poprawek dostępności | **Punkt kontrolny F-A0** po pierwszym lokalnym przebiegu axe, klawiatury i reflow: lista naruszeń (strona, reguła, element) z klasyfikacją „poprawka w istniejącym widoku” albo „wymaga przeprojektowania”. Poprawki z pierwszej grupy wchodzą do F-A1. Pozycja z drugiej grupy **zatrzymuje pracę i wymaga decyzji właściciela** (rules: human in the loop). Bez wyłączania reguł axe, bo czerwone `e2e` blokuje merge. | F-A0, F-A1 |

Inne rozstrzygnięcia przyjęte z kodu:

- **Uruchomienie API w E2E:** global setup uruchamia zbudowany plik
  `api/src/StoneFlow.Api/bin/Release/net10.0/StoneFlow.Api.dll` przez `dotnet <dll>` z katalogiem
  roboczym = katalog wyjściowy (content root z `appsettings*.json`), z `ASPNETCORE_ENVIRONMENT=Development`
  i `ASPNETCORE_URLS=http://localhost:5080`. Spec pkt 24 wspomina `dotnet run --no-build`, ale
  `dotnet run` uruchamia proces potomny, który na Windows może zostać po zabiciu rodzica i zająć port
  5080. Bezpośredni proces zachowuje się tak samo i daje się pewnie zakończyć. Pliki
  `launchSettings.json` nie są czytane (brak `dotnet run`). Brak DLL → czytelny błąd „zbuduj API:
  `dotnet build stone-flow.slnx -c Release`”.
- **Port zajęty:** jeśli `http://localhost:5080/health` odpowiada przed uruchomieniem procesu, global
  setup kończy się błędem „port 5080 zajęty — zatrzymaj lokalne API”. E2E nie może przypadkiem pisać do
  bazy deweloperskiej.
- **Migracje E2E:** nie ma ich w `npm run e2e`. CI uruchamia `migrate.sh` na bazie `StoneFlowE2E`
  (świeży kontener, czyli świeża baza na przebieg). Lokalnie README podaje to samo polecenie. Testy są
  odporne na dane z wcześniejszych przebiegów (uwaga 2), więc lokalna baza może być używana wielokrotnie.
  Kolejny przebieg ponownie ustawia hasło właściciela przez reset (konto już istnieje).
- **Data i strefa:** global setup liczy raz „dzisiaj” w Europe/Warsaw (`Intl.DateTimeFormat` z
  `timeZone: 'Europe/Warsaw'`) i zapisuje je w `process.env.STONEFLOW_E2E_TODAY`. Z niego liczy się data
  uruchomienia (`StoneFlow__LaunchDate` = 1. dzień miesiąca sprzed dwóch miesięcy) i dni testów.
  Przeglądarka: `locale: 'pl-PL'`, `timezoneId: 'Europe/Warsaw'`, co daje deterministyczny format
  i kolejność segmentów pola `type="date"` przy wpisywaniu z klawiatury.
- **Wersje zależności** (sprawdzone w npm 11.11.2026): `@playwright/test` 1.65.0 (5.11.2026,
  `engines.node >=20`), `@axe-core/playwright` 4.14.0 (15.10.2026, `axe-core ~4.14.0`, peer
  `playwright-core >=1.0.0`), `@types/node` 24.19.2 (gałąź zgodna z Node 24.21.0). Instalacja z
  dokładną wersją (`.npmrc` ma `save-exact=true`). Tag `wcag22aa` jest w axe-core od 4.5.

## Backend Plan

### Affected areas

| Layer | Module / feature | Why |
|---|---|---|
| Tests | `StoneFlow.Api.IntegrationTests/Acceptance/` | scenariusze PRD §11, uzgodnienie obu widoków z rekordami, zmiana minionego okresu |
| Deploy scripts | `deploy/scripts/` | sprawdzenie kopii `.bak` i jego test; wspólne funkcje SQL |
| CI | `.github/workflows/ci.yml` (`deploy-dry-run`, `secrets`) | test skryptu odtworzenia, kontrola reguły gitleaks |
| Config | `.gitleaks.toml` | reguła kluczy REST WooCommerce |

Bez zmian w `api/src/**`: moduły, endpointy, `Program.cs`, opcje. Testy rozmawiają z modułami tylko
przez HTTP (invariant 7).

### API contract (backend)

Brak nowych i zmienionych endpointów. Testy H i E2E używają wyłącznie istniejących, w tym:

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| GET | `/api/reports/sales` | `period` (`currentMonth`, `previousMonth`, `year`, `custom`), `year`, `from`, `to` | `period` (m.in. `effectiveFrom`), `kpis.sales/costs/result/orderCount`, `series`, `channels`, `categories` | sesja | jedna firma |
| GET | `/api/reports/sales/components` | okres + `page`, `pageSize` | `items`, `total`, `sum` | sesja | jw. |
| GET | `/api/reports/cash-flow` | okres | `kpis.inflows/outflows/change`, `series`, `outflowsByKind` | sesja | jw. |
| GET | `/api/operations` | `from`, `to`, `kind[]` | `items`, `totals`, `launchDate` | sesja | jw. |
| GET | `/api/expenses` | `costDateFrom`, `costDateTo`, `search`, `recurringDefinitionId` | `items`, `total`, `grossTotal` | sesja | jw. |
| POST | `/api/expenses/{id}/mark-paid` | wersja, data zapłaty | wersja | sesja + CSRF | jw. |
| POST | `/api/recurring-expenses` | definicja (`dayOfMonth`, `startsOn`, …) | `id`, liczba utworzonych | sesja + CSRF | jw. |
| POST | `/api/orders/{id}/cancel` | wersja | wersja | sesja + CSRF | jw. |
| GET | `/api/change-history` | `businessFrom`, `businessTo`, `objectType` | `items[].operation`, `object`, `changes[].field/oldValue/newValue` | sesja | jw. |
| GET | `/api/auth/csrf`; POST `/api/auth/password-reset/request`, `/confirm`, `/login`, `/logout` | jak dziś | 204/202 | anonimowe (istniejące) / sesja | global setup E2E, test sesji |

### Files

| File path | Action | Purpose |
|---|---|---|
| `deploy/scripts/sql-common.sh` | create | `sql <baza> <zapytanie>`, `connection_for <baza>`, `drop_database <baza>` (z `test-migrate.sh`); dołączany przez `source`, wzorem `contexts.sh` (`# shellcheck shell=bash`) |
| `deploy/scripts/test-migrate.sh` | modify | `source deploy/scripts/sql-common.sh` zamiast lokalnych `sql`/`connection_for`; `cleanup` przez `drop_database`; przypadki 1–4 i komunikaty bez zmian |
| `deploy/scripts/verify-restore.sh` | create | sprawdzenie kopii `.bak` (B pkt 8–11 spec) |
| `deploy/scripts/test-verify-restore.sh` | create | przypadki 12.1–12.5 spec |
| `.github/workflows/ci.yml` | modify | `deploy-dry-run`: krok „Próba odtworzenia kopii”; `secrets`: krok „Reguła kluczy WooCommerce wykrywa wzorzec” (zadania frontendowe w Frontend Plan) |
| `.gitleaks.toml` | modify | `[[rules]]` `woocommerce-rest-key` |
| `api/tests/StoneFlow.Api.IntegrationTests/Acceptance/AcceptanceTest.cs` | create | `internal static` pomocnicy scenariuszy na `OrdersTest` (B-T1) |
| `api/tests/StoneFlow.Api.IntegrationTests/Acceptance/MvpFinancialScenariosTests.cs` | create | testy H-1 … H-15 |

Bez zmian: `OrdersTestSupport.cs`, `ExpensesTest.cs`, `RecurringExpensesTest.cs` (pomocnicy nowego
pakietu ich używają, nie zmieniają), `LaunchDateStartupTests.cs`, `.csproj`, `packages.lock.json`.

### Implementation steps

**Skrypty**

- **B-S0 `sql-common.sh`.** Przenieść `sql()` i `connection_for()` bez zmian treści. Dodać
  `drop_database <baza>`, czyli dotychczasowe ciało pętli `cleanup` z `test-migrate.sh`
  (`IF DB_ID… SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE`). Plik nie ustawia `set -e`
  ani nie czyta zmiennych przy dołączeniu. Funkcje używają `$SQLCMD` i `$STONEFLOW_TEST_SQLSERVER`
  dopiero przy wywołaniu. `test-migrate.sh` dołącza plik po `cd "$root"`. Kontrola: `deploy-dry-run`
  zielone bez zmian w wyjściu przypadków 1–4.
- **B-S1 `verify-restore.sh <ścieżka .bak po stronie serwera>`** (`set -euo pipefail`, `source`
  `contexts.sh` i `sql-common.sh`; wymagane `STONEFLOW_TEST_SQLSERVER`, `SQLCMD`; opcjonalnie `KEEP_DB=1`):
  1. Brak argumentu → `::error::` + kod 2. Nazwa bazy roboczej `StoneFlowRestoreCheck_$(date +%s)$$`.
     Jeśli `DB_ID` tej nazwy istnieje → błąd (bez `REPLACE`). Pułapka `EXIT` woła
     `drop_database`, chyba że `KEEP_DB=1` (wtedy wypisuje nazwę bazy do ręcznego usunięcia).
  2. Krok „Odczyt kopii”: `RESTORE FILELISTONLY FROM DISK = N'<plik>'` przez `sqlcmd -s "|" -W -h -1`.
     Z wierszy bierze `LogicalName` (kolumna 1) i `Type` (kolumna 3: `D`/`L`). Pusta lista albo
     błąd → `::error::Krok „Odczyt kopii”: plik nie istnieje albo nie jest kopią SQL Server.` Potem
     `RESTORE VERIFYONLY`.
  3. Krok „Odtworzenie”: katalogi z `SERVERPROPERTY('InstanceDefaultDataPath')` i
     `SERVERPROPERTY('InstanceDefaultLogPath')`, wartości zwracane przez serwer z separatorem na
     końcu (działa i dla Linux, i dla Windows/LocalDB). `MOVE` każdego pliku logicznego do
     `<katalog><baza>_<n>.mdf|.ldf`, bez `REPLACE`. Gdy wyjście `sqlcmd` zawiera `Msg 3169`
     (kopia z nowszej wersji serwera), osobny komunikat: „Kopia pochodzi z nowszej wersji SQL Server
     niż serwer próby — użyj zgodnego obrazu (patrz `docs/operations/kopie-i-odtworzenie.md`)”.
     Rozpoznanie idzie po numerze błędu, nie po tekście.
  4. Krok „Spójność”: `DBCC CHECKDB ([baza]) WITH NO_INFOMSGS`; `sqlcmd -b` zwraca ≠ 0 przy błędzie.
  5. Krok „Stan kopii”: dla każdego schematu z `contexts.sh` (schemat = nazwa projektu, np.
     `access`, `orders`, …, `scheduling`; mapowanie w tablicy skryptu obok listy tabel) wypisuje
     `Ostatnia migracja <schemat>: <MigrationId>` albo `brak` (`TOP 1 … ORDER BY MigrationId DESC`
     z `<schemat>.__EFMigrationsHistory`, gdy tabela istnieje).
  6. Krok „Migracje”: `ConnectionStrings__StoneFlow="$(connection_for "$db")" deploy/scripts/migrate.sh`.
     Błąd → `::error::Krok „Migracje”…`. Wymaga zbudowanego rozwiązania (Release) i `dotnet-ef`, jak
     `migrate.sh`.
  7. Krok „Kontrola”: liczba tabel `__EFMigrationsHistory` = 6, inaczej błąd. Dla tabel głównych
     (Rozstrzygnięcia) wypisuje `Wiersze <schemat>.<tabela>: <liczba>`, a format jest stały, bo
     test go parsuje. Na końcu `Kopia sprawdzona: odtworzona, spójna, zmigrowana.`
  8. Skrypt nie wypisuje connection stringu, haseł ani wartości z tabel (tylko liczby i
     `MigrationId`). `sqlcmd` dostaje połączenie przez `$SQLCMD`, jak w `test-migrate.sh`.
- **B-S2 Reguła gitleaks** w `.gitleaks.toml`: `[[rules]] id = "woocommerce-rest-key"`,
  `description`, `regex = '''\b(?:ck|cs)_[0-9a-f]{40}\b'''`, `keywords = ["ck_", "cs_"]`. Bez
  wyjątków: placeholdery `<ck_klucz-rest>` w `deploy/api.env.example` i `wdrozenia.md` nie pasują
  (brak 40 znaków szesnastkowych). Ta sama postać wzorca jest regułą `woocommerce-rest-key` w
  `check-bundle-secrets.mjs` (Frontend Plan, F-S1). Dwa narzędzia, dwie kopie jednego wzorca:
  komentarz w obu plikach wskazuje drugi.
- **B-S3 Krok CI w `secrets`** („Reguła kluczy WooCommerce wykrywa wzorzec”): tworzy w
  `$RUNNER_TEMP/gitleaks-rule` plik z kluczem złożonym w czasie przebiegu
  (`printf 'ck_%s' "$(printf 'a%.0s' {1..40})"`, więc żaden plik w repozytorium nie zawiera
  pasującego tekstu), uruchamia `gitleaks dir` z `--config .gitleaks.toml --exit-code 1` i oczekuje
  kodu ≠ 0. Kod 0 → `::error::`. Następnie zwykły skan historii bez zmian.
- **B-S4 `test-verify-restore.sh`** (zmienne jak `test-migrate.sh` oraz:
  `RESTORE_TEST_DIR` — katalog widziany przez SQL Server, domyślnie
  `SERVERPROPERTY('InstanceDefaultBackupPath')`, a gdy `NULL`, to `InstanceDefaultDataPath`;
  `SQL_HOST_EXEC` — przedrostek poleceń na maszynie serwera, w CI `docker exec -i <kontener>`,
  lokalnie pusty):
  1. **Pełna kopia:** baza `StoneFlowRestoreTest_full_<id>` → `migrate.sh` → wstawienie 2 fikcyjnych
     wierszy do `expenses.OwnerWithdrawals` (lista kolumn z migracji Expenses, wartości fikcyjne)
     → `BACKUP DATABASE … TO DISK = N'<dir>full_<id>.bak' WITH INIT, COPY_ONLY` →
     `verify-restore.sh` kod 0 → wiersze `Wiersze <t>: <n>` zgodne z `SELECT COUNT(*)` bazy
     źródłowej dla każdej tabeli głównej.
  2. **Kopia częściowo zmigrowana:** baza z tylko pierwszym kontekstem z `contexts.sh`
     (`dotnet ef database update --no-build -c Release --context AccessDbContext …`, w Production
     jak w `migrate.sh`) → kopia → `verify-restore.sh` kod 0, w wyjściu `Ostatnia migracja orders: brak`,
     a skrypt kończy z 6 tabelami historii (kontrola w kroku 7 skryptu).
  3. **Plik uszkodzony:** `${SQL_HOST_EXEC} sh -c 'head -c 65536 "$0" > "$1"' <full.bak> <broken.bak>`
     → `verify-restore.sh` kod ≠ 0. Lokalnie na Windows ścieżkę dla `sh` zamienia `cygpath -u`, gdy
     polecenie istnieje.
  4. **Brak pliku:** `<dir>missing_<id>.bak` → kod ≠ 0.
  5. **Sprzątanie:** po każdym przypadku `SELECT COUNT(*) FROM sys.databases WHERE name LIKE
     'StoneFlowRestoreCheck[_]%'` = liczba sprzed testu. Pułapka `EXIT` testu usuwa bazy źródłowe
     (`drop_database`) i pliki `.bak` (`${SQL_HOST_EXEC} rm -f`).
- **B-S5 Krok CI w `deploy-dry-run`** po próbie migracji: „Próba odtworzenia kopii (pełna, częściowa,
  uszkodzona, brak pliku)” z tymi samymi `STONEFLOW_TEST_SQLSERVER` i `SQLCMD` oraz
  `SQL_HOST_EXEC="docker exec -i ${MSSQL_CONTAINER}"` i `RESTORE_TEST_DIR=/var/opt/mssql/data/`.
  `timeout-minutes` zadania: 25 → 35 (dochodzą 2 migracje i 3 odtworzenia).

**Testy integracyjne (sekcja H spec)**

- **B-T1 `Acceptance/AcceptanceTest.cs`**, `internal static class Acceptance` z rozszerzeniami
  `OrdersTest`. Dane wyłącznie przez HTTP, fikcyjne:
  - stałe zegara i daty: `LaunchDate = "2026-10-15"`, `Now = 2026-12-10 12:00 UTC`, `CreateAsync(sqlServer, now = Now)`
    → `OrdersTest.CreateAsync(sqlServer, now, settings: { ["StoneFlow:LaunchDate"] = LaunchDate })`.
    Okresy: wrzesień (przed uruchomieniem), październik (przecina uruchomienie), listopad (pełny,
    `previousMonth`), grudzień (bieżący);
  - `AdvanceToAsync(this OrdersTest, DateTimeOffset)`: `Time.SetUtcNow` + ponowne `Client.LoginAsync()`
    (sesja wygasa po 60 min zegara; wzór z `ExpensesTest.AdvanceToAsync`);
  - `CreateExpenseAsync(this OrdersTest, object body)` → `POST /api/expenses` z kluczem
    idempotencji, wynik `(Id, Version)`; `MarkPaidAsync(id, version, paidOn)`;
    `AddWithdrawalAsync(amount, date)`; `CreateRecurringAsync(body)`;
    `RunRecurringJobAsync(this OrdersTest)` → `IJobRunner.TryRunNowAsync("recurring-expenses")` z
    `test.Factory.Services` i asercją `JobRunOutcome.Completed` ze statusem `Succeeded`;
  - odczyty: `SalesAsync(query)`, `CashFlowAsync(query)`, `ComponentsSumAsync(query)` (`pageSize` = 1,
    czytany tylko `sum`), `ExpensesGrossTotalAsync(from, to)`, `OperationsTotalsAsync(from, to)`,
    `ChangeHistoryAsync(businessFrom, businessTo)`; kwoty jako ciągi z API, porównanie przez
    `decimal.Parse(…, CultureInfo.InvariantCulture)`;
  - `Money(string)` i stałe kwot fikcyjnych dobranych tak, by każdy składnik był rozpoznawalny w sumie
    (różne rzędy wielkości, np. 1000.00, 200.00, 30.00, 4.00, 0.50).
  Pomocnicy z `ExpensesTest`/`Recurring` nie pasują typem (`ExpensesTest` nie przyjmuje ustawień daty
  uruchomienia), więc te kilka wywołań jest w nowym pliku. Ciała żądań bierze się z istniejących
  `ExpensesTest.Input(...)` i `Recurring.Input(...)` (statyczne fabryki), a nie z kopii.
- **B-T2 Scenariusze 36.1–36.13** jako osobne `[Fact]` w `MvpFinancialScenariosTests`
  (`[Collection(SqlServerCollection.Name)]`), każdy na własnej bazie. Każdy sprawdza oba widoki w
  dotkniętych okresach (oczekiwane wartości liczone w teście z danych wejściowych):

  | Test | Dane | Oczekiwanie |
  |---|---|---|
  | H-1 niezapłacone zamówienie | zamówienie 1000.00, 2026-11-10 | sprzedaż listopada +1000, wpływy listopada 0 |
  | H-2 wpłata w innym miesiącu | zamówienie 2026-11-10, wpłata 2026-12-02 | sprzedaż w XI, wpływ w XII; XI wpływy 0, XII sprzedaż 0 |
  | H-3 korekta sprzedaży | korekta −200 z datą 2026-12-03 do zamówienia z XI | sprzedaż XII −200, XI bez zmian, przepływy bez zmian |
  | H-4 zwrot sprzedaży | wpłata, potem zwrot `salesReturn` 30 w XII | sprzedaż XII −30, wypływ XII +30 (podział: `refund`) |
  | H-5 zwrot nadpłaty | nadpłata i zwrot `moneyReturn` | wypływ +kwota, sprzedaż bez zmian |
  | H-6 oddanie towaru bez zwrotu pieniędzy | korekta sprzedaży bez zwrotu | sprzedaż −kwota, wypływy 0 |
  | H-7 korekta od dostawcy | wydatek −200 (koszt XI), opłacony w XII | koszty XI −200; wypływy XI 0, XII −200 |
  | H-8 anulowanie opłaconego ze zwrotem | zamówienie, wpłata, zwrot (rodzaj zgodny z regułą `CancelOrder` z Orders), `cancel` | sprzedaż 0, wpływ i wypływ zostają |
  | H-9 zamówienie otwarte w dniu uruchomienia | zamówienie 2026-10-10, wpłata 2026-10-12, wpłata 2026-10-20, zwrot 2026-11-05 | sprzedaż X i XI bez zamówienia; wpływy X tylko 2026-10-20; zwrot to tylko wypływ XI, bez obniżenia sprzedaży |
  | H-10 wydatek otwarty w dniu uruchomienia | koszt 2026-10-01, zapłata 2026-10-20 | koszty X 0; wypływy X +kwota |
  | H-11 wydatek planowany | koszt XI, bez daty zapłaty | koszty XI +kwota; wypływy 0 |
  | H-12 wydatek cykliczny | zegar 2026-11-05; definicja `dayOfMonth = 1`, `startsOn = 2026-11-01` (wystąpienie XI przy zapisie) → `AdvanceToAsync(2026-12-10)` → `RunRecurringJobAsync` → wystąpienie XII; drugie uruchomienie → bez nowego (`GET /api/expenses?recurringDefinitionId=` `total` = 2) | koszty XII +kwota, wypływy XII 0; po `mark-paid` wypływy XII +kwota |
  | H-13 wypłata właściciela | wypłata 500 w XII | wypływ XII +500 (podział `withdrawal`); sprzedaż, koszty i `result` bez zmian |

  W H-12 data uruchomienia zostaje `2026-10-15`, ale zegar startuje 2026-11-05: `Acceptance.CreateAsync`
  ma opcjonalny parametr `now` (domyślnie `Now` = 2026-12-10).
- **B-T3 Uzgodnienie (H-14, spec pkt 37):** jedna baza ze wszystkimi danymi H-1…H-13 (budowa przez
  te same pomocnicze metody, wspólna metoda `SeedAllScenariosAsync` zwracająca oczekiwane sumy).
  Wykonuje się dla `period=previousMonth`, `period=year&year=2026` i
  `period=custom&from=2026-10-01&to=2026-11-30` (przecina uruchomienie; `effectiveFrom = 2026-10-15`):
  - `kpis.sales.value` = `sum` z `/api/reports/sales/components` (ten sam okres);
  - `kpis.costs.value` = `grossTotal` z `/api/expenses?costDateFrom={effectiveFrom}&costDateTo={to}`;
    suma `categories[].value` = `kpis.costs.value`;
  - `kpis.result.value` = sprzedaż − koszty;
  - suma `series` = karty (sprzedaż, koszty, wynik; wpływy, wypływy, zmiana);
  - `kpis` przepływów = `totals` z `/api/operations?from={effectiveFrom}&to={to}`; suma
    `outflowsByKind` = `outflows`;
  - każda wartość = oczekiwana suma z `SeedAllScenariosAsync` (nie tylko równość między endpointami).
  Dla H-12 w tym teście zegar przechodzi jak w H-12, a odczyty idą przy zegarze 2026-12-10.
- **B-T4 Rozdzielenie (invariant 1)** jest częścią H-1, H-2, H-11, a nie osobnym testem.
- **B-T5 Zmiana minionego okresu (H-15, spec pkt 38):** zegar 2026-12-10, zamówienie 1000.00 z
  2026-11-14. Odczyt XI (`previousMonth`): sprzedaż S0, koszty K0. `AdvanceToAsync(2027-01-05)`.
  Edycja zamówienia: `value` 1000.00 → 1250.00. Nowy wydatek 40.00 z kosztem 2026-11-20. Odczyt XI
  przez `custom` 2026-11-01..2026-11-30: sprzedaż S0 + 250, koszty K0 + 40.
  `GET /api/change-history?businessFrom=2026-11-01&businessTo=2026-11-30` zawiera: wpis `updated`
  zamówienia (`object.id` = zamówienie) ze zmianą `value` `1000.00` → `1250.00` oraz wpis `created`
  wydatku ze zmianą `grossAmount` `null` → `40.00`. Test sprawdza, że `new − old` = różnica
  sprzedaży i `newValue` wydatku = różnica kosztów.

### Authorization

Bez zmian. Testy H działają na zalogowanym kliencie `OrdersTest`. Dostęp anonimowy do raportów jest
już sprawdzony (`EndpointSecurityTests`, `*ApiTests.Anonymous_request_is_rejected`). Global setup E2E
woła tylko istniejące anonimowe endpointy (`/api/auth/csrf`, `password-reset/request`, `/confirm`) z
nagłówkiem `X-XSRF-TOKEN`.

### Background jobs

| Job | Trigger | Context it carries | Idempotency |
|---|---|---|---|
| `recurring-expenses` (istniejący) | H-12, H-14: `IJobRunner.TryRunNowAsync` | brak (zegar testu) | drugie uruchomienie nie tworzy wystąpienia (H-12, invariant 4) |

W E2E pętla harmonogramu jest wyłączona (`Scheduling__Enabled=false`).

### Edge cases & null safety (backend)

| Edge case (spec) | Gdzie |
|---|---|
| Kopia z nowszej wersji SQL Server | B-S1 krok 3 (`Msg 3169` → komunikat), procedura D-1 |
| Kopia w archiwum `.zip` | procedura D-1 (rozpakować); skrypt sprawdza tylko odczyt `FILELISTONLY` |
| Kopia z migracjami nowszymi niż kod | B-S1 krok 5 wypisuje stan; `migrate.sh` nic nie stosuje; D-1 wyjaśnia |
| Baza robocza już istnieje | B-S1 krok 1 (unikalna nazwa + kontrola `DB_ID`, bez `REPLACE`) |
| Przerwanie skryptu | pułapka `EXIT` (B-S1, B-S4) |
| `InstanceDefaultBackupPath` = `NULL` (LocalDB) | B-S4: `RESTORE_TEST_DIR`, potem fallback |
| Wartość `null` w odpowiedzi (np. `kpis` przed uruchomieniem) | B-T1: odczyty kart tylko dla okresów po uruchomieniu; `GetProperty` rzuca czytelnie, gdy pole nie istnieje |
| Wpis utworzenia bez starej wartości | B-T5: `oldValue` = `null` (`JsonValueKind.Null`) |
| Zwrot do zamówienia sprzed uruchomienia | H-9 |
| Wydatek ujemny nieopłacony | H-7 (odczyt przed opłaceniem: koszty −200, wypływy 0) |

### Risks (backend)

- **Czas `backend` w CI:** 15 nowych testów po ~10 s na bazę to ~3 min więcej (dziś ponad 20 min,
  limit 75).
- **`InstanceDefaultBackupPath` na LocalDB** może być `NULL` albo wskazywać katalog bez praw. Dlatego
  `RESTORE_TEST_DIR` (lokalnie np. `%LOCALAPPDATA%\Temp\stoneflow-restore\`, katalog utworzony
  wcześniej).
- **Reguła `CancelOrder`** (H-8) może wymagać określonego rodzaju zwrotu przed anulowaniem. Implementer
  czyta regułę w `StoneFlow.Orders.Domain` i dobiera dane. Oczekiwanie scenariusza (sprzedaż 0,
  pieniądze zostają) się nie zmienia.

## Frontend Plan

### Step 0 — aplikacja

Jedna aplikacja: `app/` (Angular 22.2). E2E w `app/e2e/`, konfiguracja `app/playwright.config.ts`.

### Expected API contract (wyprowadzony ze spec, Context i Data / API)

| Method | Route | Request shape | Response shape | Used by |
|---|---|---|---|---|
| GET | `/health` | — | 200 | global setup (gotowość API) |
| GET | `/api/auth/csrf` | — | 204 + ciasteczko `XSRF-TOKEN` | global setup |
| POST | `/api/auth/password-reset/request` | `{ email }` + `X-XSRF-TOKEN` | 202 | global setup |
| POST | `/api/auth/password-reset/confirm` | `{ accountId, token, newPassword }` + `X-XSRF-TOKEN` | 204 | global setup |
| GET | `/api/orders?search=` | `search` | `items`, `total` | E-2 (jeden rekord po podwójnym kliknięciu) |
| GET | `/api/expenses?search=` | `search` | `items`, `total` | E-6 (wystąpienie cykliczne) |
| — | wszystkie inne | przez UI (interceptor, proxy `/api`) | — | testy procesów |

Link resetu: `{BaseUrl}/reset-hasla/nowe#konto={id}&token={token}` w treści zdarzenia 6051 logu API.

### Routing (bez zmian; trasy odwiedzane przez E2E)

| Path | Page | Loaded how | Guard |
|---|---|---|---|
| `/logowanie`, `/reset-hasla`, `/reset-hasla/nowe` | auth | lazy | `guestGuard` (logowanie) |
| `/pulpit`, `/zamowienia`, `/zamowienia/nowe`, `/zamowienia/:id`, `/wydatki`, `/wydatki/nowy`, `/wydatki/:id`, `/wydatki/cykliczne`, `/wydatki/cykliczne/:id`, `/operacje`, `/operacje/wyplaty/nowa`, `/historia-zmian`, `/integracja` | sekcje | lazy | `authGuard` |

### Files

| File path | Action | Purpose |
|---|---|---|
| `app/package.json` | modify | `devDependencies`: `@playwright/test` 1.65.0, `@axe-core/playwright` 4.14.0, `@types/node` 24.19.2; skrypty `e2e` (`playwright test`), `typecheck` (+ `tsc -p tsconfig.e2e.json --noEmit`) |
| `app/package-lock.json` | modify | lockfile po `npm install -D --save-exact` |
| `app/tsconfig.e2e.json` | create | `extends ./tsconfig.json`, `types: ["node"]`, `include: ["e2e/**/*.ts", "playwright.config.ts"]` |
| `app/angular.json` | modify | `lint.options.lintFilePatterns` + `e2e/**/*.ts`, `playwright.config.ts` |
| `app/.gitignore` | modify | `/playwright-report`, `/test-results`, `/e2e/.auth` |
| `app/.prettierignore` | modify | te same trzy ścieżki |
| `app/playwright.config.ts` | create | projekty, `webServer` (Angular), global setup/teardown, `retries`, `trace`, `use` (F-E1) |
| `app/e2e/support/dates.ts` | create | `warsawToday()`, `launchDateFor(today)`, `previousMonthDay(today, day)`, `currentMonthFirst(today)`, format `yyyy-MM-dd` |
| `app/e2e/support/tag.ts` | create | `uniqueTag()` → `e2e-<Date.now().toString(36)>-<4 znaki>` |
| `app/e2e/support/money.ts` | create | `plnToGrosze(text): number` (całkowite grosze z „12 345,67 zł”/„−200,00 zł”, bez `parseFloat`), `grosze(amount: string)` |
| `app/e2e/support/api-process.ts` | create | uruchomienie i zatrzymanie API, bufor wyjścia, `waitForResetLink(timeout)` |
| `app/e2e/support/connection.ts` | create | `databaseConnection(server, 'StoneFlowE2E')` (usuwa `Database`/`Initial Catalog`, dopisuje bazę; logika jak `connection_for`) |
| `app/e2e/support/a11y.ts` | create | `expectNoAxeViolations(page)` (`AxeBuilder` z tagami `wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa`, `wcag22aa`; komunikat z listą reguł i selektorów), `expectNoHorizontalScroll(page)` |
| `app/e2e/support/fixtures.ts` | create | `test` rozszerzony o `tag`, `today` (z `STONEFLOW_E2E_TODAY`) |
| `app/e2e/global-setup.ts` | create | F-E2 |
| `app/e2e/global-teardown.ts` | create | zatrzymanie API |
| `app/e2e/auth.setup.ts` | create | logowanie w UI → `e2e/.auth/owner.json` |
| `app/e2e/orders.e2e.ts` | create | E-2 (27.2) |
| `app/e2e/expenses.e2e.ts` | create | E-3 (27.3), E-6 (28) |
| `app/e2e/withdrawals.e2e.ts` | create | E-4 (27.4) |
| `app/e2e/dashboard.e2e.ts` | create | E-5 (27.5) |
| `app/e2e/change-history.e2e.ts` | create | E-7 (27.6) |
| `app/e2e/spa-refresh.e2e.ts` | create | E-8 (27.7) |
| `app/e2e/a11y.e2e.ts` | create | E-9 (pkt 29, 31: strony w powłoce + dialog) |
| `app/e2e/a11y-auth.e2e.ts` | create | E-9 (strony auth, kontekst bez sesji) |
| `app/e2e/keyboard.e2e.ts` | create | E-10 (pkt 30) |
| `app/e2e/session.e2e.ts` | create | E-1 (27.1), projekt `session` |
| `app/scripts/check-bundle-secrets.mjs` | create | skan pakietu (F-S1) |
| `app/scripts/check-bundle-secrets.test.mjs` | create | S-1 … S-5 |
| `.github/workflows/ci.yml` | modify | `frontend`: krok skanu; nowe zadanie `e2e` (F-C1) |
| `.github/workflows/deploy.yml` | modify | `frontend`: `node app/scripts/check-bundle-secrets.mjs .vercel/output/static` przed `vercel deploy` |
| `app/src/app/**` | modify (warunkowo) | tylko poprawki z F-A0/F-A1, z testem komponentu tam, gdzie zmienia się zachowanie |

### Implementation steps

**Infrastruktura E2E**

- **F-E0 Zależności i narzędzia.** `npm install -D --save-exact @playwright/test@1.65.0
  @axe-core/playwright@4.14.0 @types/node@24.19.2` (najpierw instalacja, potem kod; rules). Następnie
  `tsconfig.e2e.json`, `typecheck`, `lintFilePatterns`, `.gitignore`, `.prettierignore`. Kontrola:
  `npm run typecheck`, `npm run lint`, `npm run format:check` przechodzą (na razie z samym configiem),
  a `npm test -- --watch=false` nie znajduje plików `e2e/` (końcówka `.e2e.ts`).
- **F-E1 `playwright.config.ts`:**
  - `testDir: 'e2e'`, `fullyParallel: false`, `workers: 1`, `retries: process.env.CI ? 1 : 0`,
    `forbidOnly: !!process.env.CI`, `timeout` 60 s na test, `expect.timeout` 10 s;
  - `reporter`: `[['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]]`,
    `outputDir: 'test-results'`;
  - `use`: `baseURL: 'http://localhost:4200'`, `locale: 'pl-PL'`, `timezoneId: 'Europe/Warsaw'`,
    `trace: 'retain-on-failure'`, `screenshot: 'only-on-failure'`;
  - `globalSetup: './e2e/global-setup.ts'`, `globalTeardown: './e2e/global-teardown.ts'`;
  - `webServer`: `command: 'npx ng serve --configuration production --port 4200'`,
    `url: 'http://localhost:4200/logowanie'`, `reuseExistingServer: !process.env.CI`, `timeout: 240_000`,
    `env: { STONEFLOW_API_URL: 'http://localhost:5080' }`. Interfejs jest potrzebny dopiero w
    projekcie `setup`, a nie w global setup, więc kolejność startu `webServer` i global setup nie ma
    znaczenia;
  - projekty: `setup` (`testMatch: /auth\.setup\.ts/`, `use.trace: 'off'`); `chromium`
    (`testMatch: /.*\.e2e\.ts/`, `testIgnore: /session\.e2e\.ts/`, `dependencies: ['setup']`,
    `use: { ...devices['Desktop Chrome'], storageState: 'e2e/.auth/owner.json' }`); `session`
    (`testMatch: /session\.e2e\.ts/`, `dependencies: ['chromium']`, `use: { ...devices['Desktop Chrome'],
    trace: 'off' }`, bez `storageState`). Plik `a11y-auth.e2e.ts` nadpisuje `storageState` pustym stanem
    (`test.use`).
- **F-E2 Global setup i teardown:**
  1. Wymagane `STONEFLOW_E2E_SQLSERVER`. Brak → błąd z instrukcją z README.
  2. `today = warsawToday()` → `process.env.STONEFLOW_E2E_TODAY`; `launchDate = launchDateFor(today)`.
  3. Kontrola portu 5080 (odpowiedź `/health` → błąd „port zajęty”) i istnienia DLL API.
  4. `spawn('dotnet', [dll], { cwd: <katalog wyjściowy>, env })` z `ASPNETCORE_ENVIRONMENT=Development`,
     `ASPNETCORE_URLS=http://localhost:5080`, `ConnectionStrings__StoneFlow` = `databaseConnection(...)`,
     `StoneFlow__LaunchDate`, `StoneFlow__Owner__Email=owner@example.com`,
     `StoneFlow__App__BaseUrl=http://localhost:4200`, `StoneFlow__Email__Mode=Log`,
     `Scheduling__Enabled=false`, `Logging__LogLevel__Default=Information`. Wyjście stdout i stderr
     trafia do bufora w pamięci (ograniczonego do ostatnich ~2 MB). PID zapisuje się w
     `process.env.STONEFLOW_E2E_API_PID`, a uchwyt w zmiennej modułu dla teardown.
  5. Oczekiwanie na `/health` = 200 (co 1 s, do 120 s). Wyjście procesu albo przekroczenie czasu →
     błąd z ostatnimi 50 liniami bufora (przed resetem nie ma w nim tokenu).
  6. `request.newContext({ baseURL: 'http://localhost:5080' })`: `GET /api/auth/csrf` → `X-XSRF-TOKEN`
     z ciasteczka → `POST password-reset/request` `{ email }` → `waitForResetLink(30 s)` szuka w
     buforze dopasowania `/reset-hasla\/nowe#konto=([0-9a-f-]{36})&token=([^\s]+)/` → `POST
     password-reset/confirm` z losowym hasłem (`crypto.randomBytes(24).toString('base64url')`, spełnia
     minimum długości). Brak linku po 30 s → błąd „Link resetu nie pojawił się w logu API (zdarzenie
     6051)” bez treści logu. Odpowiedź 5xx → błąd „czy baza StoneFlowE2E jest zmigrowana
     (`deploy/scripts/migrate.sh`)?”.
  7. `process.env.STONEFLOW_E2E_OWNER_PASSWORD = hasło`. Nic nie trafia na dysk.
  8. Global setup zwraca funkcję teardown (albo `global-teardown.ts`) zabijającą proces API
     (`kill()`, a po 5 s `SIGKILL`). Na Windows `kill()` kończy proces `dotnet` bez potomków, bo
     uruchamiamy DLL.
- **F-E3 `auth.setup.ts`:** `/logowanie`, wypełnienie „E-mail” i „Hasło” z `process.env`, „Zaloguj”,
  `expect(page).toHaveURL(/\/pulpit/)`, `context.storageState({ path: 'e2e/.auth/owner.json' })`.

**Procesy (każdy test tworzy własne dane z `tag`)**

- **F-E4 Procesy 27.2–27.4 i 28:**
  - **E-2 `orders.e2e.ts`:** `/zamowienia/nowe`. Wypełnia: rodzaj klienta, „Imię i nazwisko lub
    nazwa firmy” = `Klient ${tag}`, „Data zamówienia” = dziś, kanał, „Wartość brutto (zł)” =
    `123,45`. Przycisk zapisu dostaje `dblclick()`. Oczekiwanie: komunikat sukcesu, a
    `page.request.get('/api/orders?search=<tag>')` ma `total` = 1. Szczegóły zamówienia: „Dodaj
    wpłatę”, „Kwota (zł)” = `100,00`, „Data wpłaty” = dziś, zapis. Stan płatności pokazuje
    częściową płatność (tekst statusu). `/operacje` z filtrem bieżącego miesiąca pokazuje wiersz
    wpłaty z kwotą `100,00 zł` i numerem zamówienia.
  - **E-3 `expenses.e2e.ts` (wydatek jednorazowy):** `/wydatki/nowy` bez daty zapłaty, opis
    `Wydatek ${tag}`. W `/operacje` dla bieżącego miesiąca brak wiersza z `tag`. Szczegóły → oznaczenie
    jako opłacony (data = dziś). Wtedy wiersz jest na liście wydatków (wyszukanie `tag`) i w
    `/operacje`.
  - **E-6 `expenses.e2e.ts` (cykliczny):** `/wydatki/cykliczne` → „Dodaj wydatek cykliczny” →
    formularz z „Powtarzaj co miesiąc”, „Dzień miesiąca” = 1, „Początek” = 1. dzień bieżącego
    miesiąca, opis `Cykliczny ${tag}`. Po zapisie lista `/wydatki` (wyszukanie `tag`) zawiera
    wystąpienie z datą kosztu 1. dnia bieżącego miesiąca (uwaga 3).
  - **E-4 `withdrawals.e2e.ts`:** `/operacje/wyplaty/nowa`, „Osoba” = `Właściciel ${tag}` (pole
    tekstowe, fikcyjne), kwota, data = dziś. `/operacje` pokazuje wiersz jako wypływ (tekst kierunku
    lub rodzaju z listy).
- **F-E5 Pulpit i Historia zmian:**
  - **E-5 `dashboard.e2e.ts`:** `day = previousMonthDay(today, 12)`. `/pulpit` → „Okres” =
    niestandardowy, „Data od” = „Data do” = `day`. Odczyt kart „Sprzedaż” i „Wpływy” (S0, W0).
    Utworzenie przez UI zamówienia z datą `day`, wartością `321,00` i wpłatą `21,00` z datą `day`.
    Powrót na Pulpit z tym samym zakresem: Sprzedaż = S0 + 32100 gr; przełączenie na „Przepływy
    pieniężne”: Wpływy = W0 + 2100 gr. Podpisy kart różnią się między widokami (Sprzedaż/Koszty/Wynik
    kontra Wpływy/Wypływy/Zmiana; dowód dla §10 „różne etykiety”). Kwoty w formacie `^-?\d{1,3}( \d{3})*,\d{2} zł$`.
  - **E-7 `change-history.e2e.ts`:** `day = previousMonthDay(today, 14)`. Odczyt sprzedaży dla zakresu
    `day` (S0) → zamówienie `Klient ${tag}` z datą `day`, wartość `500,00` → Pulpit S0 + 50000 gr →
    edycja wartości na `650,00` → Pulpit S0 + 65000 gr → `/historia-zmian` z filtrem daty biznesowej
    `day`–`day`. Wpis zmiany zamówienia (po numerze z nagłówka szczegółów) pokazuje pole kwoty ze
    starą `500,00 zł` i nową `650,00 zł`.
  - **E-8 `spa-refresh.e2e.ts`:** zamówienie z `tag` → `/zamowienia/{id}` → `page.reload()` → URL bez
    zmian, widoczna nazwa klienta. `/wydatki/cykliczne` → `reload()` → nagłówek „Wydatki cykliczne”
    i brak przekierowania na `/logowanie`.

**Dostępność, klawiatura, reflow**

- **F-E7 axe i reflow (E-9):** lista stron w `a11y.e2e.ts` (tablica `{ name, path }`) i w
  `a11y-auth.e2e.ts`:
  - w powłoce: `/pulpit` (widok „Sprzedaż” i po przełączeniu „Przepływy pieniężne”), `/zamowienia`,
    `/zamowienia/nowe`, szczegóły zamówienia (utworzonego w teście), `/wydatki`, `/wydatki/nowy`,
    szczegóły wydatku, `/wydatki/cykliczne`, `/operacje`, `/operacje/wyplaty/nowa`, `/historia-zmian`,
    `/integracja`, otwarty dialog potwierdzenia usunięcia w szczegółach wydatku (otwarty i zamknięty
    przez „Anuluj”, bez usuwania);
  - auth (bez sesji): `/logowanie`, `/reset-hasla`, `/reset-hasla/nowe#konto=…&token=e2e` (formularz
    widoczny).
  Dla każdej strony: oczekiwanie na zakończenie ładowania (brak `app-loading-state`, nagłówek `h1`),
  `expectNoAxeViolations`, potem `page.setViewportSize({ width: 375, height: 800 })` i
  `expectNoHorizontalScroll` (`document.documentElement.scrollWidth <= clientWidth`). Jeden test na
  stronę (`for … test(name)`), żeby raport wskazywał stronę. Wyłączeń reguł nie ma.
- **F-E8 Klawiatura (E-10, `keyboard.e2e.ts`):**
  1. menu: `/pulpit`, `page.keyboard.press('Tab')` → fokus na „Przejdź do treści”, `Enter` → fokus w
     `main` (`document.activeElement` w obszarze treści). Kolejne `Tab` aż do każdej z 6 pozycji
     `NAV_ITEMS`, a każda z nich ma niezerowy `outline-width` albo `box-shadow` ≠ `none` z
     `getComputedStyle` przy `:focus-visible`. `Enter` na „Wydatki” → URL `/wydatki`;
  2. formularz wydatku: `/wydatki/nowy` tylko klawiaturą (`Tab`, wpisywanie, strzałki w `select`
     kategorii, cyfry dat w kolejności `pl-PL`) i zapis `Enter`. Najpierw zapis z pustą kwotą: fokus
     albo `aria-invalid="true"` na polu kwoty, `aria-describedby` wskazuje element z komunikatem,
     opis z `tag` zostaje w polu. Potem poprawny zapis i komunikat sukcesu;
  3. dialog: szczegóły wydatku → fokus na „Usuń” → `Enter` → fokus wewnątrz `dialog` → `Escape` →
     dialog zamknięty, `document.activeElement` = przycisk „Usuń”, rekord istnieje.
- **F-E9 Sesja (E-1, `session.e2e.ts`, projekt `session`):** `browser.newContext()` A: `/logowanie`,
  złe hasło → komunikat ogólny (bez wskazania, czy zły jest e-mail, czy hasło); poprawne → `/pulpit`,
  karty widoczne. Kontekst B: logowanie → menu użytkownika → „Wyloguj”. Kontekst A: klik w
  „Zamówienia” (żądanie API → 401) → URL `/logowanie`, tekst „Sesja wygasła…”, `page.getByText(/zł/)`
  ma 0 elementów. Komentarz w teście: musi biec ostatni, bo kończy wszystkie sesje konta.
- **F-A0 Punkt kontrolny dostępności.** Pierwszy lokalny przebieg `chromium` z E-9/E-10 → lista
  naruszeń w notatce roboczej zadania (strona, reguła axe lub krok klawiatury, element). Klasyfikacja
  według Assumptions 10. Pozycja „wymaga przeprojektowania” → stop i pytanie do właściciela.
- **F-A1 Poprawki** (tylko z F-A0): w istniejących komponentach, z tokenami `--sf-*` i `shared/ui/`,
  z testem komponentu (Vitest) tam, gdzie zmienia się zachowanie (np. fokus, `aria-*`). Każda trafia
  do listy w `06-implementation-done.md`.

**Skan pakietu**

- **F-S1 `check-bundle-secrets.mjs <katalog>`** (ESM, bez zależności, wzorem
  `check-color-literals.mjs`; eksportuje `RULES` i `scanDirectory(dir)` dla testu, a uruchomiony
  bezpośrednio wypisuje wynik i ustawia kod):

  | Reguła | Wzorzec (wielkość liter rozróżniana) |
  |---|---|
  | `server-config-key` | `(?<![A-Za-z0-9])(?:ConnectionStrings\|WebhookSecret\|ConsumerKey\|ConsumerSecret)(?![a-z0-9])` |
  | `smtp-password-key` | `Smtp(?::\|__)Password(?![a-z0-9])` |
  | `sql-connection-fragment` | `(?:^\|[;"'\s])(?:Password\|User Id\|Uid\|Initial Catalog)\s*=\s*[^;"'\s]` |
  | `woocommerce-rest-key` | `\b(?:ck\|cs)_[0-9a-f]{40}\b` (ta sama postać co w `.gitleaks.toml`) |
  | `private-key-header` | `-----BEGIN [A-Z ]*PRIVATE KEY-----` |
  | `deploy-secret-name` | `\b(?:VERCEL_TOKEN\|API_FTP_PASSWORD\|PRODUCTION_MIGRATIONS_CONNECTION)\b` |

  Pliki `.js`, `.mjs`, `.css`, `.html`, `.json`, `.map`, `.txt` rekurencyjnie. Brak katalogu, zero
  plików albo zero plików tekstowych → kod 1 („skan nie przechodzi na pusto”). Trafienie: wypisuje
  ścieżkę względną i `id` reguły, bez dopasowanego tekstu, i kończy kodem 1. Sukces: „Brak wzorców
  sekretów w N plikach”.
- **F-S2 Test `check-bundle-secrets.test.mjs`** (`node:test`, katalogi w `os.tmpdir()`, próbki
  składane w czasie testu, np. `'ck_' + 'a'.repeat(40)` i `'-----BEGIN ' + 'PRIVATE KEY-----'`, więc
  gitleaks nie widzi pasującego tekstu w repozytorium): S-1 każda reguła ma próbkę wykrytą; S-2
  próbki negatywne przechodzą (`e.uid=1`, `n.Password=t`, `{uid:e}`, `type="password"`, `Hasło`,
  `connectionStrings`, `consumerKey` w camelCase, `ck_` + 39 znaków, `CK_`+40 wielkimi); S-3 brak
  katalogu i pusty katalog → błąd; S-4 wyjście nie zawiera dopasowanego tekstu; S-5 wynik wskazuje
  plik i regułę. Uruchamiany przez istniejące `npm run test:scripts`.
- **F-S3 Wywołania:** `ci.yml` `frontend` po buildzie: krok „Brak wzorców sekretów w pakiecie
  (skrypt)” `node scripts/check-bundle-secrets.mjs dist`, obok gitleaks. `deploy.yml` `frontend` po
  gitleaks, przed „Vercel — publikacja”: `node app/scripts/check-bundle-secrets.mjs .vercel/output/static`.

**CI**

- **F-C1 Zadanie `e2e`** w `ci.yml` (`runs-on: ubuntu-latest`, `timeout-minutes: 30`, uprawnienia z
  poziomu workflow `contents: read`, bez sekretów i bez `environment`):
  - `env.SA_PASSWORD` i `services.mssql` jak w `deploy-dry-run` (ten sam obraz `2022-CU27`). Runner
    GitHub ma Dockera, więc wzór jest sprawdzony; `backend` korzysta z Testcontainers na tym samym
    runnerze;
  - kroki: checkout → `setup-dotnet` (`global.json`) → `dotnet tool restore` → `dotnet restore
    stone-flow.slnx --locked-mode` → `dotnet build stone-flow.slnx -c Release --no-restore` →
    „Oczekiwanie na SQL Server” (kopia kroku z `deploy-dry-run`; drugie użycie, wydzielenie akcji
    poza zakresem) → „Migracje bazy E2E”: `ConnectionStrings__StoneFlow="Server=localhost,1433;Database=StoneFlowE2E;User Id=sa;Password=${SA_PASSWORD};TrustServerCertificate=True" deploy/scripts/migrate.sh`
    → `setup-node` (`app/.nvmrc`, cache npm) → `npm ci` (w `app`) → `npx playwright install
    --with-deps chromium` → `npm run e2e` z `STONEFLOW_E2E_SQLSERVER="Server=localhost,1433;User Id=sa;Password=${SA_PASSWORD};TrustServerCertificate=True"`;
  - `actions/upload-artifact` (ten sam przypięty SHA co w `backend`) z `if: failure()`, nazwa
    `e2e-report`, ścieżki `app/playwright-report` i `app/test-results`, `retention-days: 7`. Bez
    `app/e2e/.auth`.
  Zadanie biegnie równolegle do pozostałych. Przez `workflow_call` uruchamia się też przed publikacją
  w `deploy.yml`.

### State

Bez zmian w aplikacji. Stan E2E: `process.env` (`STONEFLOW_E2E_TODAY`, `STONEFLOW_E2E_OWNER_PASSWORD`),
plik `e2e/.auth/owner.json`, `tag` per test.

### Styling

Bez nowych styli. Poprawki F-A1 tylko z tokenami `--sf-*` i klasami motywu (`lint:colors`).

### Accessibility

E-9, E-10 i F-A0/F-A1. Fokus widoczny zgodnie z design guide §9, dialog z pułapką fokusu i powrotem
do wywołującego, reflow 375 px.

### Edge cases (frontend)

| Edge case (spec) | Gdzie |
|---|---|
| E2E na przełomie miesiąca/doby | `STONEFLOW_E2E_TODAY` liczone raz w global setup (Europe/Warsaw); testy kwot używają dni 12 i 14 poprzedniego miesiąca |
| Limit logowania 10/min | logują tylko `setup` (1) i `session` (3); pozostałe używają `storageState` |
| Link resetu nie pojawił się w logu | F-E2 krok 6: limit 30 s, czytelny błąd bez treści logu |
| Unieważnienie sesji przez „Wyloguj” | projekt `session` ostatni (uwaga 1) |
| Dane z poprzednich przebiegów / ponowienie w CI | `tag` + asercje różnic (uwaga 2) |
| `guestGuard` na `/logowanie` przy zalogowanym kontekście | `a11y-auth.e2e.ts` z pustym `storageState` |
| Fałszywe trafienia skanu | F-S1 reguły z wielkością liter i kotwicami, S-2 |
| Port 5080 zajęty / brak zbudowanego API | F-E2 krok 3 |

### Risks (frontend)

- **Pamięć lokalnie:** `ng serve --configuration production`, API, Chromium i LocalDB naraz. Plan
  weryfikacji lokalnej (V-L) uruchamia E2E po zamknięciu innych serwerów, tylko z Chromium, a w razie
  braku pamięci pojedynczymi plikami (`npx playwright test e2e/orders.e2e.ts`).
- **Wpisywanie dat z klawiatury** w polu `type="date"` zależy od lokalizacji Chromium. Stąd
  `locale: 'pl-PL'`. Jeśli kolejność segmentów będzie inna na runnerze, test E-10 wpisuje datę przez
  segmenty z `ArrowRight`, nadal tylko klawiaturą.
- **Zakres F-A1 nieznany** przed pierwszym przebiegiem (punkt kontrolny F-A0).
- **`@playwright/test` 1.65.0 ma 6 dni.** Jeśli przy instalacji wystąpi problem z przeglądarką albo
  Node 24, cofnięcie do 1.64.1 (14.10.2026) jest zmianą jednej linii. Odnotować w `06`.

## API Contract

Brak nowych i zmienionych endpointów. Strony korzystają z istniejących, zgodnie z tabelą „API
contract (backend)”:

| Method | Route | Request | Response | Auth | Konsument w SF-21 |
|---|---|---|---|---|---|
| GET | `/health` | — | 200 / 503 | anonimowe | global setup E2E |
| GET | `/api/auth/csrf` | — | 204 + `XSRF-TOKEN` | anonimowe | global setup E2E |
| POST | `/api/auth/password-reset/request` | `{ email }` | 202 | anonimowe + CSRF | global setup E2E |
| POST | `/api/auth/password-reset/confirm` | `{ accountId, token, newPassword }` | 204 | anonimowe + CSRF | global setup E2E |
| POST | `/api/auth/login`, `/api/auth/logout` | jak dziś | 204 | anonimowe (login) / sesja | UI w `setup`, `session` |
| GET | `/api/reports/sales`, `/sales/components`, `/cash-flow` | okres | jak dziś | sesja | H-1…H-15; UI Pulpitu w E-5, E-7 |
| GET | `/api/operations` | `from`, `to` | `totals`, `launchDate` | sesja | H-14; UI w E-2…E-4; procedura D-3 |
| GET | `/api/expenses` | `costDateFrom`, `costDateTo`, `search`, `recurringDefinitionId` | `grossTotal`, `total` | sesja | H-12, H-14; E-6 |
| GET | `/api/orders` | `search` | `total` | sesja | E-2 |
| GET | `/api/change-history` | `businessFrom`, `businessTo` | wpisy ze zmianami | sesja | H-15; UI w E-7 |

## Integration Check

| Frontend expects | Backend provides | Gap |
|---|---|---|
| Link resetu w logu API w Development (zdarzenie 6051, `#konto=…&token=…`) | `LogEmailSender` (`EventId 6051`), `PasswordResetEmail` (`{BaseUrl}/reset-hasla/nowe#konto={id}&token={token}`) | brak |
| `POST /confirm` z `accountId`, `token`, `newPassword` | `PasswordResetConfirmRequest(AccountId, Token, NewPassword)` | brak |
| CSRF dla anonimowych POST | `CsrfValidationMiddleware` + `GET /api/auth/csrf` | brak |
| Ciasteczko sesji przez HTTP w Development (proxy `localhost:4200`) | `DevelopmentSessionCookieName` | brak |
| `Scheduling__Enabled=false` wyłącza pętlę | `SchedulingOptions.Enabled` (sekcja `Scheduling`) | brak |
| `StoneFlow__Email__Mode=Log` dozwolony | `EmailOptionsValidator`: `Log` tylko w Development | brak (E2E uruchamia Development) |
| `/health` = 200 przy dostępnej bazie | `SqlServerHealthCheck` | brak |
| `search` na listach zamówień i wydatków | `OrdersEndpoints`/`ExpensesEndpoints` `[FromQuery] string? search` | brak |

No integration gaps.

## Invariant Check

| Invariant | Where it is enforced | Test that proves it |
|---|---|---|
| 1. Sales separate from cash | bez zmian reguł; dowód w testach | H-1 (niezapłacone: brak wpływu), H-2 (wpłata nie tworzy sprzedaży), H-11 (planowany: brak wypływu), H-14; E-5 (karty obu widoków) |
| 2. Backend decimal money | bez zmian obliczeń; testy porównują ciągi API przez `decimal`; E2E liczy różnice w groszach całkowitych (`money.ts`), bez `parseFloat` | H-14 (sumy liczone w C# na `decimal`); E-5, E-7 (różnice w groszach całkowitych) |
| 3. API-enforced access | bez nowych endpointów i wyjątków w `EndpointSecurityTests`; global setup używa tylko istniejących anonimowych endpointów | `EndpointSecurityTests` bez zmian; E-1 (401 po zakończeniu sesji → `/logowanie`, brak danych finansowych) |
| 4. Retries never duplicate | bez zmian; `verify-restore.sh` z unikalną bazą i sprzątaniem | H-12 (drugie uruchomienie zadania), E-2 (podwójne kliknięcie → `total` = 1), B-S4 przypadek 5 |
| 5. Financial audit trail | bez zmian zapisu audytu; D-1 wymaga odnotowania cofnięcia historii po odtworzeniu kopii | H-15 (wpisy `updated`/`created` z wartościami wyjaśniają różnicę), E-7 |
| 6. Secrets stay server-side | `check-bundle-secrets.mjs` + reguła gitleaks; E2E bez sekretów `production`; hasło E2E tylko w pamięci; `verify-restore.sh` nie wypisuje connection stringu ani danych; data uruchomienia tylko w konfiguracji serwera (D-3) | S-1…S-5, krok CI na `dist` i `.vercel/output/static`, B-S3 (reguła wykrywa wzorzec), zadanie `secrets` |
| 7. Module and layer boundaries | bez zmian kodu modułów; testy H tylko przez HTTP (`IJobRunner` jest kontraktem `Shared.Application`) | `StoneFlow.Architecture.Tests` bez zmian |
| 8. No identifying data in docs | dokumenty z placeholderami (`<adres-api>`, `<adres-aplikacji>`); dane E2E fikcyjne (`owner@example.com`, `Klient e2e-…`); kopie produkcyjne poza repozytorium, CI i artefaktami (D-1); checklista bez zrzutów z danymi klientów | przegląd dokumentów w V-H; gitleaks |

Wynik: każdy invariant ma miejsce egzekwowania i dowód. Brak blokera.

## Unified Implementation Order

Każda pozycja to osobny commit, który da się sprawdzić sam (komenda w nawiasie).

1. **B-S0** `sql-common.sh` + refaktor `test-migrate.sh` (`test-migrate.sh` lokalnie na LocalDB).
2. **B-S1, B-S4, B-S5** `verify-restore.sh`, `test-verify-restore.sh`, krok `deploy-dry-run`
   (`test-verify-restore.sh` lokalnie na LocalDB).
3. **B-S2, B-S3** reguła gitleaks i kontrola w `secrets` (CI na PR).
4. **B-T1…B-T5** `Acceptance/` (filtr klasy na LocalDB).
5. **F-S1…F-S3** skan pakietu, test, kroki CI (`npm run test:scripts`,
   `node scripts/check-bundle-secrets.mjs dist`).
6. **F-E0, F-E1** zależności, konfiguracja, tsconfig, lint, ignore (`typecheck`, `lint`,
   `format:check`, `npm test`).
7. **F-E2, F-E3** global setup/teardown, `auth.setup.ts` (`npx playwright test --project=setup`).
8. **F-E4, F-E5** procesy E-2…E-8 (`npx playwright test --project=chromium`).
9. **F-E7, F-E8** axe, reflow, klawiatura → **F-A0** punkt kontrolny → **F-A1** poprawki (Vitest dla
   zmienionych komponentów + ponowny przebieg).
10. **F-E9** test sesji (`npm run e2e`, pełny przebieg z `session` na końcu).
11. **F-C1** zadanie `e2e` w CI.
12. **D-1…D-4** procedury i checklista; **D-5** `wdrozenia.md`, `conventions.md`; **D-6** README
    (po uzgodnieniu z autorem bieżących zmian, Assumptions 15).
13. Push, PR, wszystkie zadania CI zielone. Checklista zostaje z placeholderem linku (uwaga 9).

### Dokumenty (D)

- **D-1 `docs/operations/kopie-i-odtworzenie.md`:** cele D-08 (codziennie, 30 dni, niezależne miejsce,
  RPO 24 h, RTO 8 h) jako cele do potwierdzenia próbą. Konfiguracja w panelu z rozbieżnością SF-1 i
  decyzją właściciela. Niezależne miejsce: dostęp tylko właściciela; kopia nie trafia do repozytorium,
  zgłoszeń, CI ani artefaktów. **Lista kontrolna PR z migracją:** kopia ręczna przed merge, nazwa kopii
  w opisie PR, także przed pierwszą publikacją. Sprawdzenie skryptem lokalnie w dwóch wariantach:
  (a) Docker z `deploy/docker-compose.yml`: `docker cp` do `/var/opt/mssql/backup/`, `SQLCMD="docker
  exec -i stone-flow-sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P <hasło>"`;
  (b) Windows z LocalDB: ścieżka Windows, `SQLCMD='sqlcmd -S (localdb)\MSSQLLocalDB -E'`, Git Bash.
  Wymagania: bash, .NET SDK z `global.json`, `dotnet tool restore`, `dotnet build -c Release`. Kroki
  sprzątania kopii lokalnej. Sekcja „Kopia z nowszej wersji SQL Server”. Próba odtworzenia na
  hostingu z pomiarem RTO i RPO oraz odtworzenie po awarii (A pkt 6–7 spec, w tym `app_offline.htm`,
  utrata danych od kopii, cofnięcie „Historii zmian” do chwili kopii z wpisem w checkliście,
  uzupełnienie zamówień sklepu przez SF-13).
- **D-2 `docs/operations/powrot-do-wersji.md`:** zasada „rollback kodu nie cofa migracji”. Frontend:
  Instant Rollback/Promote w Vercel i równoległy revert. Backend: revert na `main` przez PR.
  **Ścieżka częściowego revertu commitu z migracją** (uwaga 5, z kontrolą
  `has-pending-model-changes` w pętli po `contexts.sh` i `deploy-dry-run`). Kryteria fix forward.
  Odtworzenie kopii jako ostateczność. Kolejność przy awarii obu części. Próba procedury przed
  startem z listą dowodów.
- **D-3 `docs/operations/data-uruchomienia.md`:** kto podaje datę; `StoneFlow:LaunchDate` w
  `appsettings.Production.json` na serwerze (nigdy w repozytorium); restart IIS; zachowanie przy
  braku lub złym formacie (`LaunchDateStartupTests`, `logs/stdout_*.log`); kontrola przez `launchDate`
  w `GET /api/operations` i „Raport liczony od …” na Pulpicie; data jednorazowa; usunięcie danych
  próbnych przed ustawieniem prawdziwej daty.
- **D-4 `docs/operations/odbior-mvp.md`:** tabela z kolumnami: kryterium (dosłownie), rodzaj dowodu
  (`automatyczny`/`hosting`/`ręczny`), dowód (nazwa testu lub zadania CI), stan, data, kto, wynik,
  link. Wiersze: 7 kryteriów zgłoszenia SF-21, 9 punktów PRD §11, 9 punktów design guide §10
  (uwaga 6), pozycje właściciela z E pkt 23. Wiersze automatyczne: placeholder linku (uwaga 9).
  Bez nazw handlowych, domen i zrzutów.
- **D-5 `wdrozenia.md`:** §1 dochodzą punkty „procedury z `docs/operations/` przeczytane, pozycje
  hostingowe `odbior-mvp.md` wypełnione przed `DEPLOY_ENABLED`” i „kopia ręczna przed pierwszą
  publikacją”. §3 wymagane kontrole: `+ e2e`. §7 „procedura kopii: zadanie SF-21” zamienia się na
  odnośniki do `docs/operations/kopie-i-odtworzenie.md` i `powrot-do-wersji.md`, a akapit
  „Rollback kodu” dostaje odnośnik do ścieżki częściowego revertu. `.sdd/context/conventions.md`:
  tabela układu z `app/e2e/` i `docs/operations/`, ścieżka `wdrozenia.md` (zamiast
  `docs/wdrozenia.md`); Verification: `npm run e2e` (wymagania), `test-verify-restore.sh`,
  `check-bundle-secrets.mjs`.
- **D-6 `README.md`:** sekcja „Testy E2E”: wymagania (zbudowane API Release, SQL Server, `npx
  playwright install chromium`), migracja `StoneFlowE2E`, `STONEFLOW_E2E_SQLSERVER`, `npm run e2e`,
  uruchomienie pojedynczego pliku i `--project=session --no-deps`, raport. **Najpierw uzgodnienie z
  autorem niezatwierdzonej zmiany README** (stan na dziś: 1 linia), bez nadpisywania jej.

## Tests

| ID | Test | Type | What it proves |
|---|---|---|---|
| H-1…H-13 | `MvpFinancialScenariosTests.*` (scenariusze 36.1–36.13) | integracyjny (SQL Server) | każdy scenariusz PRD §11 w obu widokach, w tym przełom uruchomienia (H-9, H-10), cykliczny bez duplikatu (H-12) |
| H-14 | `Both_views_reconcile_with_source_records_for_month_year_and_range_across_launch` | integracyjny | kryterium „oba widoki uzgadniają się, także karta Koszty” (spec pkt 37) |
| H-15 | `Change_of_a_past_period_is_explained_by_change_history` | integracyjny | spec pkt 38, invariant 5 |
| — | `LaunchDateStartupTests` (istniejące, bez zmian) | integracyjny | brak lub zły format daty zatrzymuje start w Production |
| M-1 | `test-migrate.sh` po refaktorze | skrypt (CI `deploy-dry-run`) | wydzielenie `sql-common.sh` nie zmienia zachowania |
| R-1…R-5 | `test-verify-restore.sh` | skrypt (CI `deploy-dry-run`) | odtworzenie, spójność, migracja kopii pełnej i częściowej; błąd przy uszkodzonym i brakującym pliku; sprzątanie |
| G-1 | krok „Reguła kluczy WooCommerce wykrywa wzorzec” | CI `secrets` | nowa reguła gitleaks działa |
| S-1…S-5 | `check-bundle-secrets.test.mjs` | `node:test` | każda reguła wykrywa wzorzec, typowe fragmenty zminifikowanego JS przechodzą, brak przejścia na pusto, brak wartości w wyjściu |
| S-6 | `node scripts/check-bundle-secrets.mjs dist` | CI `frontend` / `deploy.yml` | bieżący build nie zawiera wzorców |
| E-0 | `npm run typecheck`, `npm run lint` | statyczny | kod E2E jest typowany i lintowany |
| E-1 | `session.e2e.ts` | E2E | logowanie, komunikat przy złym haśle, wygaśnięcie sesji bez ujawnienia danych |
| E-2 | `orders.e2e.ts` | E2E | zamówienie ręczne z wpłatą; podwójne kliknięcie = jeden rekord |
| E-3 | `expenses.e2e.ts` — jednorazowy | E2E | wydatek bez zapłaty nie jest operacją; po opłaceniu jest |
| E-4 | `withdrawals.e2e.ts` | E2E | wypłata jako wypływ w „Operacjach” |
| E-5 | `dashboard.e2e.ts` | E2E | karty Sprzedaż/Wpływy rosną o dane testu; format PL; różne etykiety widoków |
| E-6 | `expenses.e2e.ts` — cykliczny | E2E | wystąpienie bieżącego okresu po zapisie definicji |
| E-7 | `change-history.e2e.ts` | E2E | zmiana raportu za miniony okres wyjaśniona w „Historii zmian” |
| E-8 | `spa-refresh.e2e.ts` | E2E | odświeżenie podstrony SPA zachowuje trasę i sesję |
| E-9 | `a11y.e2e.ts`, `a11y-auth.e2e.ts` | E2E (axe) | zero naruszeń WCAG 2.2 AA i brak poziomego przewijania przy 375 px na 16 widokach + dialog |
| E-10 | `keyboard.e2e.ts` | E2E | menu, formularz wydatku i dialog z klawiatury; widoczny fokus; błędy powiązane z polem |
| U-… | testy komponentów dla poprawek F-A1 (jeśli są) | Vitest | poprawka dostępności nie psuje zachowania |

## Verification

**V-L — lokalnie (Windows, LocalDB, bez Dockera, mało pamięci).** Polecenia uruchamiać pojedynczo,
po zamknięciu innych serwerów deweloperskich i IDE z debuggerem.

Backend (z katalogu głównego, PowerShell albo Git Bash):

```
dotnet restore stone-flow.slnx --locked-mode
dotnet format stone-flow.slnx --verify-no-changes --no-restore
dotnet build stone-flow.slnx -c Release --no-restore
# Git Bash:
export STONEFLOW_TEST_SQLSERVER='Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True'
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests/StoneFlow.Api.IntegrationTests.csproj -c Release --no-build \
  --filter-class 'StoneFlow.Api.IntegrationTests.Acceptance.MvpFinancialScenariosTests' \
  --filter-class 'StoneFlow.Api.IntegrationTests.LaunchDateStartupTests'
```

Pełny `dotnet test --solution` (ponad 20 min, dużo baz) zostaje dla CI. Lokalnie najwyżej po
zmianach w pomocnikach testów, w osobnym podejściu.

Skrypty (Git Bash; `sqlcmd` i LocalDB są w `PATH`):

```
export STONEFLOW_TEST_SQLSERVER='Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True'
export SQLCMD='sqlcmd -S (localdb)\MSSQLLocalDB -E'
mkdir -p "$LOCALAPPDATA/Temp/stoneflow-restore"
export RESTORE_TEST_DIR="$(cygpath -w "$LOCALAPPDATA/Temp/stoneflow-restore")\\"
deploy/scripts/test-migrate.sh
deploy/scripts/test-verify-restore.sh
```

Frontend (z `app/`):

```
npm ci
npm run format:check
npm run lint
npm run typecheck
npm run build
npm test -- --watch=false
npm run test:scripts
node scripts/check-bundle-secrets.mjs dist
```

E2E lokalnie (tylko Chromium, bez `--with-deps` na Windows):

```
npx playwright install chromium
# Git Bash, z katalogu głównego:
ConnectionStrings__StoneFlow='Server=(localdb)\MSSQLLocalDB;Database=StoneFlowE2E;Integrated Security=True;TrustServerCertificate=True' deploy/scripts/migrate.sh
cd app
export STONEFLOW_E2E_SQLSERVER='Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True'
npm run e2e
```

Przy braku pamięci: `npx playwright test --project=setup` → kolejne pliki `npx playwright test
e2e/<plik>.e2e.ts` → na końcu `npx playwright test --project=session --no-deps`. Po przebiegu
sprawdzić, że na portach 5080 i 4200 nie zostały procesy (`Get-NetTCPConnection -LocalPort 5080`).
Bazę `StoneFlowE2E` można zostawić do kolejnych przebiegów.

**V-CI — na PR SF-21:** `backend`, `frontend` (z krokiem skanu skryptem), `secrets` (z kontrolą
reguły), `deploy-dry-run` (z próbą odtworzenia), `e2e` — wszystkie zielone. W `06` odnotować
każdy test E2E, który przeszedł dopiero przy ponowieniu (niestabilny). Ochronę gałęzi z `e2e` jako
wymaganą kontrolą włącza administrator repozytorium (pozycja w `06`).

**V-M — ręczne** (scenariusze UI w `06-implementation-done.md`, tabela testów ręcznych): stany
hover/focus/selected i błędów (kontrast) na Pulpicie, w formularzach i tabelach; niekompletna
integracja na Pulpicie („Ostatnia pełna kontrola”); zapis zamówienia i wydatku na telefonie (375 px)
i tablecie; czytnik ekranu na wykresach i dialogu; przejście `kopie-i-odtworzenie.md` lokalnie na
kopii z `BACKUP DATABASE` lokalnej bazy deweloperskiej (wariant LocalDB); ocena układu względem
referencji (§10).

**V-H — przegląd człowieka przed merge (risk `high`):** procedury `docs/operations/*` (zwłaszcza
odtworzenie do właściwej bazy i ścieżka częściowego revertu), brak danych identyfikujących w
dokumentach i raportach, `verify-restore.sh` (brak `REPLACE`, sprzątanie, brak wypisywania danych),
zadanie `e2e` bez sekretów. Wynik w `05-code-review.md` i `06`.

**V-O — właściciel (poza repozytorium, dowody w `odbior-mvp.md`):** konfiguracja kopii, próba
odtworzenia z RTO/RPO, migracja na hostingu, próba powrotu, data uruchomienia, weryfikacja obu
hostingów i odświeżenia SPA na Vercel, zamknięcie D-05 i D-08, ocena „bez arkuszy”, linki do przebiegów
CI.

## Risks

- **Ryzyko `high`** bez zmian: przegląd człowieka przed merge (V-H). Kryteria hostingowe zostają
  otwarte dla właściciela (Assumptions 1).
- **Pominięcie `session` przy błędzie w `chromium`** (uwaga 1): świadomy koszt kolejności. CI jest wtedy
  i tak czerwone.
- **Częściowy revert a oczekujące zmiany modelu EF** (uwaga 5): bez zgodności modelu ze snapshotem
  `migrate.sh` zatrzyma publikację. Procedura każe to sprawdzić przed merge revertu; jeśli się nie
  da, fix forward.
- **Zakres poprawek dostępności** nieznany do F-A0. Naruszenie wymagające przeprojektowania
  zatrzymuje pracę i wymaga decyzji właściciela, bo czerwone `e2e` blokuje merge.
- **Pamięć i czas lokalnie** (Windows, LocalDB, bez Dockera): pełny `dotnet test` i pełne E2E mogą być
  zbyt ciężkie. Plan zakłada filtry i przebiegi pojedynczymi plikami, a pełny przebieg w CI.
- **LocalDB a `InstanceDefaultBackupPath`/ścieżki w Git Bash:** obsłużone przez `RESTORE_TEST_DIR` i
  `cygpath`. W razie problemów lokalny `test-verify-restore.sh` może nie przejść mimo zielonego CI.
  Odnotować w `06` jako ograniczenie środowiska, nie jako wynik.
- **Wersje** `@playwright/test` 1.65.0 (6 dni) i `@axe-core/playwright` 4.14.0: fallback 1.64.1.
- **README** ma niezatwierdzoną zmianę innej osoby (Assumptions 15). D-6 czeka na uzgodnienie, a
  zadanie nie nadpisuje tej zmiany.
- **Czas CI:** `backend` +~3 min, `deploy-dry-run` +~5 min (limit 35), nowe `e2e` ~15–20 min równolegle.
