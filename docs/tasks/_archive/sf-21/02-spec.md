# Task Specification — SF-21

## Source

[SF-21](https://standev.youtrack.cloud/issue/SF-21)

## Goal

Przygotować wszystko, co wykonawca może dostarczyć w repozytorium przed produkcyjnym uruchomieniem MVP (PRD §11, §13), i jasno oddzielić to od kroków, które może wykonać tylko właściciel albo które wymagają hostingu produkcyjnego. Zadanie obejmuje:

- wersjonowane procedury operacyjne (kopie i odtworzenie, powrót do działającej wersji, ustawienie daty uruchomienia, checklistę odbioru MVP z miejscem na dowody);
- skrypt sprawdzający odtworzenie kopii `.bak` (odtworzenie do bazy roboczej, migracje, kontrole spójności) z testem w CI;
- testy E2E kluczowych procesów (Playwright) z automatyczną kontrolą dostępności (axe-core, WCAG 2.2 AA) i obsługi klawiaturą, jako osobne zadanie CI na PR;
- pakiet testów integracyjnych przechodzący wszystkie scenariusze finansowe z PRD §11 i uzgadniający oba widoki (z kartą „Koszty”) z rekordami źródłowymi i listami, z wyjaśnieniem zmiany raportu na liście „Historia zmian”;
- dodatkowy skan zbudowanego pakietu frontendu pod kątem nazw kluczy konfiguracji serwera i wzorców sekretów.

Zadanie nie dodaje funkcji produktu. Kryteria wymagające hostingu (próba odtworzenia, migracja i powrót na hostingu, prawdziwa data uruchomienia) zostają otwarte dla właściciela, a checklista odbioru jest miejscem na ich dowody.

## Context

- **Procedury i konfiguracja wdrożeń:** `wdrozenia.md` (w katalogu głównym repozytorium; `conventions.md` i `docs/tasks/sf-4` podają `docs/wdrozenia.md`, patrz Assumptions 14), workflow `.github/workflows/ci.yml` (`backend`, `frontend`, `secrets`, `deploy-dry-run`) i `deploy.yml` (CI → backend: migracje → FTPS → `/health` → frontend Vercel z kontrolą `/logowanie` i `/api/auth/csrf`). Skrypty w `deploy/scripts/`: `contexts.sh` (6 kontekstów EF w stałej kolejności), `migrate.sh`, `test-migrate.sh` (próba migracji w `deploy-dry-run`, zmienne `STONEFLOW_TEST_SQLSERVER` i `SQLCMD`), `publish-api.sh`, `upload-api.sh`, `wait-for-url.sh`, `check-fresh-commit.sh`.
- **Kopie zapasowe:** D-08 (PRD §12) — codzienna kopia SQL, retencja 30 dni w niezależnym miejscu, RPO 24 h, RTO 8 h; miejsce kopii i próba odtworzenia są otwarte. SF-1 (`docs/tasks/sf-1/01-weryfikacja.md`) odnotowuje, że według artykułu dostawcy panel robi kopie co drugi dzień, a płatny CustomBackup ma retencję tygodnia. SF-4 (Q4) zostawił „kopię przed migracją” dla SF-21. `wdrozenia.md` §7 odsyła do „procedury kopii: zadanie SF-21”.
- **Data uruchomienia:** `ILaunchDate` (`StoneFlow.Shared.Application/LaunchDate`), `LaunchDateOptions` + `LaunchDateOptionsValidator` + `ValidateOnStart()` w `StoneFlow.Shared.Infrastructure/DependencyInjection.cs`. Brak klucza `StoneFlow:LaunchDate` albo zły format zatrzymuje start hosta w każdym środowisku. `appsettings.json` nie ma daty, a `appsettings.Development.json` ma przykładową `2026-01-01`. Pokrycie: `api/tests/StoneFlow.Api.IntegrationTests/LaunchDateStartupTests.cs` (brak, błędny format, brak w `appsettings.json`; fabryka testowa domyślnie działa w środowisku Production). Wartość daty jest widoczna w API: `launchDate` w odpowiedzi `GET /api/operations` i w okresie raportów.
- **Raporty i listy (bez zmian):** `GET /api/reports/sales` (karty `sales`, `costs`, `result`, `orderCount`, szereg, kanały, kategorie), `GET /api/reports/sales/components` (`sum` całego filtra), `GET /api/reports/cash-flow` (karty `inflows`, `outflows`, `change`, szereg, podział wypływów), `GET /api/operations` (`totals`), `GET /api/expenses` (`grossTotal`, filtry `costDateFrom`/`costDateTo`), `GET /api/change-history` (`changedFrom`/`changedTo`, `businessFrom`/`businessTo`, `objectType`).
- **Testy integracyjne:** `api/tests/StoneFlow.Api.IntegrationTests` — kolekcja `SqlServerCollection` (Testcontainers albo `STONEFLOW_TEST_SQLSERVER`), pomocnicy `OrdersTest.CreateAsync(sqlServer, now, settings)` z `FakeTimeProvider`, `OrderEntryRequests` (zamówienia, wpłaty, korekty), `ExpensesTest.Input`, `Recurring` (definicje cykliczne, uruchomienie zadania `recurring-expenses` przez `IJobRunner.TryRunNowAsync`). Istniejące pliki `Reporting/SalesReportApiTests.cs`, `CashFlowReportApiTests.cs`, `OperationsApiTests.cs`, `ChangeHistoryApiTests.cs` sprawdzają pojedyncze reguły; brakuje jednego przebiegu wszystkich scenariuszy §11 na wspólnych danych.
- **Frontend:** `app/` (Angular 22.2.4, Node 24.21.0, npm 11), `npm run build` → `app/dist/stone-flow/browser`, serwer deweloperski z `app/proxy.conf.mjs` (`/api` → `STONEFLOW_API_URL`, domyślnie `http://localhost:5080`). Trasy: `/logowanie`, `/reset-hasla`, `/pulpit`, `/zamowienia` (`nowe`, `:id`), `/wydatki` (`nowy`, `cykliczne`, `cykliczne/:id`, `:id`), `/operacje` (`wyplaty/nowa`, `wyplaty/:id`), `/historia-zmian`, `/integracja`. Powłoka ma link „Przejdź do treści” (`layout/app-shell.html`). Brak infrastruktury E2E; E2E odroczono do SF-21 w SF-15 i SF-16.
- **Sesja:** wygasa po 60 min bezczynności (`SessionPolicy.IdleTimeout`), „Wyloguj” kończy sesje na wszystkich urządzeniach, 401 → `/logowanie` z „Sesja wygasła…”. W Development ciasteczko sesji nie wymaga HTTPS (`DevelopmentSessionCookieName`), a e-mail w trybie `Log` zapisuje treść wiadomości (z linkiem resetu) w logu API (`EventId 6051`). Konto właściciela powstaje przy pierwszej prośbie o reset hasła. Limit logowania: 10 prób na minutę na adres.
- **Skan sekretów:** `.gitleaks.toml` (domyślne reguły), zadanie `secrets` (cała historia), krok „Brak sekretów w pakiecie frontendu” w `frontend` (`dist`) i w `deploy.yml` (`.vercel/output/static`).

## Scope

### In scope

1. **Procedury operacyjne** w nowym katalogu `docs/operations/`:
   - `kopie-i-odtworzenie.md` — harmonogram i retencja, niezależne miejsce kopii, kopia przed wdrożeniem z migracją, pobranie kopii, sprawdzenie skryptem, próba odtworzenia na hostingu, pomiar RTO;
   - `powrot-do-wersji.md` — powrót frontendu (Vercel) i backendu (SmarterASP.NET), zasada „rollback kodu nie cofa migracji”, odtworzenie bazy jako ostateczność, próba procedury;
   - `data-uruchomienia.md` — kto podaje datę, gdzie się ją ustawia, jak sprawdzić, co się dzieje przy braku lub złym formacie;
   - `odbior-mvp.md` — checklista odbioru: każde kryterium zgłoszenia i PRD §11 z rodzajem dowodu, miejscem na datę, osobę, wynik i link do dowodu.
2. **Skrypt `deploy/scripts/verify-restore.sh`** i jego test **`deploy/scripts/test-verify-restore.sh`** uruchamiany w zadaniu CI `deploy-dry-run`.
3. **Testy E2E** w `app/e2e/` (Playwright, TypeScript) z kontrolą dostępności (`@axe-core/playwright`) i klawiatury oraz **nowe zadanie CI `e2e`** w `ci.yml`.
4. **Pakiet testów integracyjnych scenariuszy finansowych** `api/tests/StoneFlow.Api.IntegrationTests/Acceptance/MvpFinancialScenariosTests.cs` (nazwa robocza).
5. **Skan pakietu frontendu** `app/scripts/check-bundle-secrets.mjs` z testem `app/scripts/check-bundle-secrets.test.mjs` (uruchamiany przez `npm run test:scripts`), wywoływany w zadaniu `frontend` w `ci.yml` i przed publikacją w `deploy.yml`. Dodatkowa reguła gitleaks dla kluczy REST WooCommerce w `.gitleaks.toml`.
6. **Data uruchomienia:** bez zmian kodu; istniejące `LaunchDateStartupTests` są dowodem, a procedura (`data-uruchomienia.md`) opisuje ustawienie i kontrolę.
7. **Poprawki dostępności** znalezione przez axe i testy klawiatury, ograniczone do usunięcia naruszeń WCAG 2.2 AA i wymagań design guide §9–§10 w istniejących widokach (Assumptions 10).
8. **Aktualizacje dokumentacji:** `wdrozenia.md` (warunki włączenia publikacji, wymagane kontrole PR z `e2e`, §7 z odnośnikami do nowych procedur), `.sdd/context/conventions.md` (sekcja Verification: E2E i skrypt odtworzenia; ścieżka `app/e2e/` w tabeli układu), README (uruchomienie E2E).
9. Scenariusze testów ręcznych UI w `06-implementation-done.md`.

### Out of scope

- Migracja historii z Excela (ticket, PRD §4, D-06).
- Anonimizacja danych klienta (SF-22, D-09).
- Nowe funkcje produktu, nowe endpointy, zmiany reguł finansowych i schematu bazy.
- Pełny przepływ WooCommerce w E2E (brak sklepu testowego); zostaje w testach integracyjnych (`WooCommerceWebhookTests`, `WooCommerceBackfillTests`).
- Czynności na hostingu i w panelach dostawców: konfiguracja harmonogramu i retencji kopii, próba odtworzenia, migracja i powrót na hostingu, ustawienie prawdziwej daty uruchomienia, włączenie `DEPLOY_ENABLED`. Wykonawca dostarcza procedury i checklistę; wykonuje je właściciel (lub administrator na jego zlecenie).
- Automatyczne kopiowanie produkcyjnej bazy poza hosting (np. do GitHub Actions) — dane klientów nie trafiają do CI (Assumptions 5).
- Zamknięcie D-05 i D-08 w PRD.
- Zmiana lokalizacji `wdrozenia.md`.
- Testy wydajności i obciążenia (cele wydajności są otwarte w D-08).

## Behavior

### A. Procedura kopii i odtworzenia (`docs/operations/kopie-i-odtworzenie.md`)

1. **Cele** (D-08): kopia codzienna, retencja 30 dni, kopia w miejscu niezależnym od hostingu, RPO 24 h, RTO 8 h. Procedura mówi wprost, że to cele do potwierdzenia próbą, nie gwarancja dostawcy.
2. **Konfiguracja na hostingu** (krok właściciela): ustawienie kopii w panelu SmarterASP.NET. Procedura zapisuje rozbieżność z SF-1: według artykułu dostawcy (2021) standardowa kopia jest co drugi dzień, a CustomBackup ma retencję tygodnia. Jeśli panel nie zapewnia codziennej kopii z 30-dniową retencją, właściciel wybiera rozwiązanie i zapisuje je w checkliście (Assumptions 4). Wynik konfiguracji (częstotliwość, godzina, retencja, miejsce) trafia do checklisty jako dowód.
3. **Niezależne miejsce kopii** (krok właściciela): kopia jest pobierana z hostingu do miejsca wybranego przez właściciela, z dostępem ograniczonym do właściciela. Kopia zawiera dane osobowe klientów, więc nie trafia do repozytorium, zgłoszeń, CI ani artefaktów GitHub.
4. **Kopia przed wdrożeniem z migracją** (SF-4 Q4): zanim PR zawierający nową migrację zostanie zmergowany do `main` (merge uruchamia publikację), administrator wykonuje kopię ręczną w panelu i zapisuje jej nazwę w opisie PR. Dotyczy to też pierwszej publikacji na produkcję. Workflow tego nie wymusza (brak API panelu); punkt jest na liście kontrolnej PR w procedurze.
5. **Sprawdzenie kopii skryptem** (wykonawca dostarcza, właściciel uruchamia lokalnie): plik `.bak` (po rozpakowaniu, jeśli panel daje archiwum) kopiuje się do lokalnego SQL Server (np. `docker cp` do kontenera z `deploy/docker-compose.yml`) i uruchamia `verify-restore.sh` (sekcja B). Po próbie baza robocza i lokalna kopia pliku są usuwane.
6. **Próba odtworzenia na hostingu** (krok właściciela, kryterium zgłoszenia): odtworzenie kopii przez panel do osobnej bazy, jeśli plan na to pozwala; w przeciwnym razie do bazy produkcyjnej **przed** startem produkcyjnym (gdy nie ma w niej prawdziwych danych). Następnie `migrate.sh` przez workflow albo wariant awaryjny z `wdrozenia.md` §7, `GET /health` = 200, logowanie i odczyt Pulpitu. Zapisuje się czas od decyzji o odtworzeniu do działającej aplikacji (porównanie z RTO 8 h) oraz wiek odtworzonej kopii (porównanie z RPO 24 h).
7. **Odtworzenie po awarii:** decyzja właściciela, wybór kopii, `app_offline.htm` (zatrzymanie API), odtworzenie, migracje (kopia może być starsza niż kod), usunięcie `app_offline.htm`, `/health`, kontrola Pulpitu i listy „Historia zmian”. Procedura mówi, że dane zapisane po wykonaniu kopii przepadają, a zamówienia ze sklepu z tego okresu wróci dzienne uzupełnianie (SF-13) od ostatniego sukcesu zapisanego w odtworzonej bazie. Wpisy ręczne użytkownik musi wprowadzić ponownie.

### B. Skrypt `deploy/scripts/verify-restore.sh`

8. Wejście: ścieżka pliku `.bak` **widziana przez SQL Server** (np. `/var/opt/mssql/backup/stoneflow.bak` w kontenerze) jako argument; zmienne `STONEFLOW_TEST_SQLSERVER` i `SQLCMD` jak w `test-migrate.sh`. Opcjonalnie `KEEP_DB=1` zostawia bazę roboczą do ręcznego przeglądu.
9. Przebieg:
   1. `RESTORE FILELISTONLY` odczytuje pliki logiczne; `RESTORE VERIFYONLY` sprawdza czytelność kopii;
   2. `RESTORE DATABASE` do bazy roboczej `StoneFlowRestoreCheck_<id>` z `MOVE` plików do domyślnych katalogów danych i logu serwera (nigdy nie nadpisuje istniejącej bazy o innej nazwie);
   3. `DBCC CHECKDB ... WITH NO_INFOMSGS` — błąd kończy skrypt;
   4. wypisanie ostatniej migracji z `__EFMigrationsHistory` każdego schematu z `contexts.sh` (stan kopii);
   5. `migrate.sh` na bazie roboczej — dowód, że bieżący kod migruje kopię (kopia może być starsza niż kod);
   6. kontrola, że jest 6 tabel historii migracji, i wypisanie liczby wierszy tabel głównych (zamówienia, wpisy pieniężne zamówień, wydatki, definicje cykliczne, wypłaty, wpisy audytu) — do porównania z liczbami w aplikacji;
   7. usunięcie bazy roboczej (pułapka `EXIT`), chyba że `KEEP_DB=1`.
10. Kod wyjścia ≠ 0 przy każdym błędzie z komunikatem `::error::` wskazującym krok. Skrypt nie wypisuje connection stringu ani danych z tabel (tylko liczby i nazwy migracji).
11. Wspólne funkcje (`sql`, `connection_for`) z `test-migrate.sh` są wydzielone do pliku dołączanego (np. `deploy/scripts/sql-common.sh`, wzorem `contexts.sh`), a `test-migrate.sh` ich używa. Zachowanie `test-migrate.sh` się nie zmienia.
12. **Test `test-verify-restore.sh`** (w `deploy-dry-run` po próbie migracji):
    1. baza z pełnymi migracjami i kilkoma wierszami fikcyjnych danych → `BACKUP DATABASE` do pliku w kontenerze → `verify-restore.sh` kończy się sukcesem, a liczby wierszy się zgadzają;
    2. baza zmigrowana tylko do pierwszego kontekstu (Access) → kopia → `verify-restore.sh` dociąga pozostałe migracje (6 tabel historii);
    3. uszkodzony plik (obcięty) → kod ≠ 0;
    4. nieistniejący plik → kod ≠ 0;
    5. po każdym przypadku baza robocza nie istnieje.

### C. Powrót do działającej wersji (`docs/operations/powrot-do-wersji.md`)

13. **Zasada:** rollback kodu nie cofa migracji (PRD §10). Migracje są expand/contract (`wdrozenia.md` §6), więc poprzednia wersja API działa na nowszym schemacie. Cofnięcie schematu tylko przez odtworzenie kopii (sekcja A pkt 7), z utratą danych od kopii.
14. **Backend (SmarterASP.NET):** `git revert` problematycznego commitu na `main` przez PR (wymagane kontrole) → `deploy.yml` publikuje poprzedni kod (migracje bez zmian, bo revert nie usuwa plików zastosowanych migracji — procedura zabrania revertu zmieniającego zastosowane migracje; wtedy fix forward). `check-fresh-commit.sh` uniemożliwia ponowne uruchomienie publikacji starszego commitu, więc revert jest jedyną drogą powrotu backendu. Kontrola: `/health` = 200, logowanie, Pulpit.
15. **Frontend (Vercel):** natychmiastowo — „Instant Rollback”/„Promote” poprzedniego wdrożenia w panelu Vercel (bez budowy). Następna publikacja z `main` nadpisze ten stan, dlatego równolegle wykonuje się revert z pkt 14. Poprzedni frontend musi działać z bieżącym API (`wdrozenia.md` §6).
16. **Kolejność przy awarii obu części:** najpierw frontend w panelu Vercel (najszybszy), potem revert na `main` (publikuje backend i frontend). Przerwana wysyłka FTPS: `wdrozenia.md` §7.
17. **Próba procedury** (krok właściciela/administratora, kryterium zgłoszenia): przed startem produkcyjnym na pustej produkcji — publikacja nieszkodliwej zmiany (np. tekstu w `app_offline.htm` albo komentarza), jej revert, kontrola `/health`, `/logowanie` i `/api/auth/csrf`, oraz jedno „Promote” poprzedniego wdrożenia w Vercel. Dowód: linki do przebiegów workflow, identyfikatory wdrożeń Vercel, czas trwania.

### D. Data uruchomienia (`docs/operations/data-uruchomienia.md`)

18. Datę podaje właściciel przed startem produkcyjnym (PRD §4). Administrator wpisuje ją jako `StoneFlow:LaunchDate` (`yyyy-MM-dd`) w `appsettings.Production.json` na serwerze (`wdrozenia.md` §4), nigdy w repozytorium. Zmiana pliku restartuje aplikację w IIS.
19. Brak klucza albo zły format zatrzymuje start API (istniejące zachowanie, `LaunchDateStartupTests`); przyczyna jest w `logs/stdout_*.log`, bez wartości. Produkcja nigdy nie przyjmuje daty domyślnej: `appsettings.json` jej nie ma, a `appsettings.Development.json` nie jest wczytywany w Production.
20. Kontrola po ustawieniu: `launchDate` w odpowiedzi `GET /api/operations` (po zalogowaniu) oraz komunikat „Raport liczony od …” na Pulpicie dla okresu przecinającego datę. Wynik trafia do checklisty.
21. Procedura ostrzega, że data jest jednorazowa: zmiana po starcie zmienia raporty wstecz, więc wymaga decyzji właściciela. Dane próbne wpisane na produkcji przed startem usuwa się przed ustawieniem prawdziwej daty (usunięcie zostaje w „Historii zmian”, co jest zgodne z invariantem 5), albo próby wykonuje się na odtworzonej bazie (sekcja A pkt 6).

### E. Checklista odbioru (`docs/operations/odbior-mvp.md`)

22. Tabela: kryterium (dosłownie ze zgłoszenia SF-21 i PRD §11), rodzaj dowodu (`automatyczny` — test/CI z nazwą testu lub zadania; `hosting` — krok właściciela; `ręczny` — scenariusz UI), stan, data, kto, wynik, link do dowodu. Wiersze automatyczne wypełnia wykonawca linkiem do przebiegu CI z PR SF-21; wiersze `hosting` zostają puste z opisem, co jest dowodem.
23. Pozycje właściciela obejmują co najmniej: konfigurację kopii, próbę odtworzenia z pomiarem RTO/RPO, migrację na hostingu (log kroku „Migracje” pierwszej publikacji), próbę powrotu, ustawienie daty uruchomienia, weryfikację obu hostingów i odświeżenia podstrony SPA na Vercel (`wdrozenia.md` §12), zamknięcie D-05 i D-08, oraz „codzienna obsługa nie wymaga równoległego prowadzenia arkuszy” (PRD §11 — ocena właściciela po okresie użytkowania).

### F. Testy E2E (`app/e2e/`)

24. **Stos:** prawdziwa aplikacja Angular (`ng serve --configuration production` z `proxy.conf.mjs`) + prawdziwe API (`dotnet run --no-build -c Release`, środowisko `Development`, `http://localhost:5080`) + SQL Server (świeża baza na przebieg, migracje przez `migrate.sh`). Przeglądarka: Chromium. Testy w jednym wątku (`workers: 1`), każdy test tworzy własne dane i nie zależy od innych testów.
25. **Konfiguracja API dla E2E** przez zmienne środowiskowe (nie pliki w repozytorium): `ConnectionStrings__StoneFlow`, `StoneFlow__LaunchDate` = pierwszy dzień miesiąca sprzed dwóch miesięcy względem dnia przebiegu (wyliczane w konfiguracji Playwright), `StoneFlow__Owner__Email` = fikcyjny adres `owner@example.com`, `StoneFlow__Email__Mode=Log`, `Scheduling__Enabled=false`.
26. **Konto właściciela (global setup):** setup uruchamia API jako proces potomny z przechwyconym wyjściem, czeka na `/health`, wysyła prośbę o reset hasła dla adresu właściciela, odczytuje link z logu (zdarzenie 6051), ustawia losowe hasło przez `POST /api/auth/password-reset/confirm` i zapisuje stan zalogowanej sesji (`storageState`) przez logowanie w UI. Hasło istnieje tylko w pamięci przebiegu i w katalogu wyników testów (poza repozytorium). Bez endpointów i zmian kodu tylko dla testów.
27. **Procesy (każdy jako osobny test, przez UI):**
    1. **Logowanie i wygaśnięcie sesji:** złe hasło → komunikat bez ujawniania szczegółów; poprawne → `/pulpit`. Zakończenie sesji z innego kontekstu („Wyloguj” kończy sesje na wszystkich urządzeniach) → kolejna nawigacja w pierwszym kontekście prowadzi do `/logowanie` z „Sesja wygasła…”, a strona nie pokazuje wcześniej wczytanych danych finansowych. 60 minut bezczynności pokrywa test integracyjny `Session_expires_after_60_minutes_idle_and_activity_extends_it` (zegar testowy).
    2. **Zamówienie ręczne z wpłatą:** utworzenie na `/zamowienia/nowe`, podwójne kliknięcie „Zapisz” tworzy jedno zamówienie (design guide §10), dodanie wpłaty w szczegółach, stan płatności i lista „Operacje” pokazują wpłatę.
    3. **Wydatek jednorazowy i opłacenie:** utworzenie na `/wydatki/nowy` bez daty zapłaty, oznaczenie jako opłacony, pozycja w liście wydatków i w „Operacjach” dopiero po opłaceniu.
    4. **Wypłata właściciela:** `/operacje/wyplaty/nowa`, pozycja w „Operacjach” jako wypływ.
    5. **Pulpit — sprzedaż i przepływy:** dla danych testu (własny zakres obejmujący tylko jego daty) karty „Sprzedaż” i „Wpływy” pokazują oczekiwane kwoty w formacie polskim; przełączenie widoku zmienia karty i podpisy.
    6. **Historia zmian wyjaśnia zmianę raportu:** zamówienie z datą w poprzednim miesiącu → wartość sprzedaży poprzedniego miesiąca na Pulpicie → edycja kwoty → Pulpit pokazuje nową wartość → `/historia-zmian` z filtrem daty biznesowej poprzedniego miesiąca pokazuje wpis zmiany kwoty ze starą i nową wartością.
    7. **Odświeżenie podstrony SPA:** przeładowanie strony na `/zamowienia/{id}` i `/wydatki/cykliczne` zostawia użytkownika na tej podstronie z danymi (sesja odtworzona, trasa zachowana).
28. **Wydatek cykliczny w E2E:** utworzenie definicji przez UI i widoczność wystąpienia bieżącego okresu (tworzonego przy zapisie definicji, SF-16) na liście wydatków. Ponowienie i restart zadania pokrywają testy integracyjne (`RecurringExpensesJobTests`).
29. **Dostępność (axe-core):** `AxeBuilder` z tagami `wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa`, `wcag22aa` na stronach: `/logowanie`, `/reset-hasla`, `/pulpit` (oba widoki), `/zamowienia`, `/zamowienia/nowe`, szczegóły zamówienia, `/wydatki`, `/wydatki/nowy`, szczegóły wydatku, `/wydatki/cykliczne`, `/operacje`, `/operacje/wyplaty/nowa`, `/historia-zmian`, `/integracja`, oraz otwarty dialog potwierdzenia usunięcia. Wymagane zero naruszeń. Wyłączenie reguły albo elementu tylko z uzasadnieniem w kodzie testu i w `06-implementation-done.md`; w tym zadaniu nie planuje się żadnego.
30. **Klawiatura:**
    1. menu główne: od początku strony „Przejdź do treści” jest pierwszym fokusem i przenosi fokus do treści; Tab dochodzi do każdej z sześciu pozycji menu, Enter otwiera sekcję, a fokus jest widoczny (niezerowy `outline` albo `box-shadow` z `:focus-visible`);
    2. formularz wydatku: wypełnienie wszystkich pól, wybór kategorii i dat oraz zapis wyłącznie klawiaturą; błąd walidacji przenosi lub wskazuje pole z powiązanym komunikatem (`aria-describedby`), a wpisane dane zostają;
    3. dialog potwierdzenia: fokus wewnątrz dialogu, Escape zamyka, fokus wraca do przycisku wywołującego.
31. **Reflow:** na szerokości 375 px strony z pkt 29 nie mają poziomego przewijania całej strony (`scrollWidth` ≤ `clientWidth` elementu dokumentu), design guide §9.
32. **Uruchomienie:** `npm run e2e` w `app/` (wymaga zbudowanego API i bazy wskazanej zmienną `STONEFLOW_E2E_SQLSERVER`, connection string do serwera); instrukcja lokalna w README. Raport HTML i ślady (`trace: 'retain-on-failure'`) w `app/test-results/` i `app/playwright-report/` (ignorowane w git). W CI `retries: 1`; test zielony dopiero przy ponowieniu jest odnotowywany w `06-implementation-done.md` jako niestabilny.

### G. Zadanie CI `e2e`

33. Nowe zadanie w `ci.yml` (PR do `main` i `workflow_call`, więc także przed publikacją), równoległe do pozostałych:
    - usługa `mssql` (ten sam obraz i jednorazowe hasło co `deploy-dry-run`);
    - `setup-dotnet` (`global.json`), `dotnet tool restore`, restore `--locked-mode`, build Release;
    - `migrate.sh` na nowej bazie `StoneFlowE2E`;
    - `setup-node` (`app/.nvmrc`), `npm ci`, `npx playwright install --with-deps chromium`;
    - `npm run e2e`;
    - upload raportu i śladów jako artefakt przy błędzie (`if: failure()`), retencja 7 dni. Dane w raporcie są fikcyjne.
    - `timeout-minutes: 30`; uprawnienia `contents: read`, bez sekretów.
34. `wdrozenia.md` §3 dopisuje `e2e` do wymaganych kontroli PR.

### H. Scenariusze finansowe PRD §11 (testy integracyjne)

35. Nowa klasa w `StoneFlow.Api.IntegrationTests/Acceptance/` w kolekcji `SqlServerCollection`. Dane wyłącznie przez API modułów (jak w istniejących testach Reporting), zegar `FakeTimeProvider`, data uruchomienia ustawiona w teście. Kwoty fikcyjne, dobrane tak, aby każdy składnik był rozpoznawalny w sumach.
36. **Scenariusze** — każdy jako osobny test sprawdzający oczekiwane wartości obu widoków w dotkniętych okresach:
    1. niezapłacone zamówienie — sprzedaż rośnie, wpływy nie;
    2. wpłata w innym miesiącu — sprzedaż w miesiącu zamówienia, wpływ w miesiącu wpłaty;
    3. korekta sprzedaży — obniża sprzedaż w okresie swojej daty, nie zmienia przepływów;
    4. zwrot sprzedaży — obniża sprzedaż i jest wypływem;
    5. zwrot nadpłaty (zwrot pieniędzy) — wypływ bez zmiany sprzedaży;
    6. oddanie towaru bez zwrotu pieniędzy — korekta sprzedaży, bez wypływu;
    7. korekta od dostawcy — wydatek ujemny: obniża koszty w dacie kosztu, a po opłaceniu obniża wypływy;
    8. anulowanie opłaconego zamówienia ze zwrotem — zamówienie znika ze sprzedaży, wpłata i zwrot zostają w przepływach;
    9. zamówienie otwarte w dniu uruchomienia — data zamówienia i wcześniejsza wpłata sprzed uruchomienia nie wchodzą do raportów, wpłata od daty uruchomienia jest wpływem, zwrot od daty uruchomienia jest tylko wypływem;
    10. wydatek otwarty w dniu uruchomienia — koszt sprzed uruchomienia nie wchodzi do kosztów, zapłata od daty uruchomienia jest wypływem;
    11. wydatek planowany (bez daty zapłaty) — jest kosztem, nie jest wypływem;
    12. wydatek cykliczny — wystąpienie utworzone przez zadanie `recurring-expenses` jest kosztem w dacie kosztu, wypływem dopiero po opłaceniu; ponowne uruchomienie zadania nie tworzy duplikatu;
    13. wypłata właściciela — wypływ, nie koszt i nie obniża wyniku przed wypłatami.
37. **Uzgodnienie z rekordami źródłowymi** (osobny test na wspólnym zestawie wszystkich scenariuszy, dla miesiąca, roku i własnego zakresu przecinającego datę uruchomienia):
    - `kpis.sales.value` = `sum` z `GET /api/reports/sales/components` dla tego samego okresu;
    - `kpis.costs.value` (karta „Koszty”) = `grossTotal` z `GET /api/expenses?costDateFrom={efektywny początek}&costDateTo={to}`, a suma `categories` = `kpis.costs.value`;
    - `kpis.result.value` = sprzedaż − koszty;
    - suma `series` = karty (sprzedaż, koszty, wynik; wpływy, wypływy, zmiana);
    - `kpis` przepływów = `totals` z `GET /api/operations?from={efektywny początek}&to={to}`; suma podziału wypływów = `outflows`;
    - każda wartość jest też porównana z kwotą wyliczoną w teście ze znanych danych wejściowych (nie tylko między endpointami).
38. **Zmiana raportu za miniony okres:** po zebraniu wyników poprzedniego miesiąca test przesuwa zegar do kolejnego miesiąca, zmienia kwotę zamówienia z poprzedniego miesiąca i dodaje wydatek z datą kosztu w poprzednim miesiącu, sprawdza nowe wartości raportu i to, że `GET /api/change-history?businessFrom=…&businessTo=…` (poprzedni miesiąc) zwraca wpisy obu zmian z polem kwoty, starą i nową wartością, a różnica wartości wyjaśnia różnicę raportu.

### I. Skan pakietu frontendu

39. `app/scripts/check-bundle-secrets.mjs <katalog>` przeszukuje wszystkie pliki tekstowe katalogu (`.js`, `.css`, `.html`, `.json`, `.map`, `.txt`) i kończy się kodem ≠ 0, wypisując plik i nazwę reguły (bez dopasowanej wartości), gdy znajdzie:
    - nazwy kluczy konfiguracji serwera z `deploy/api.env.example` i `wdrozenia.md` §4: `ConnectionStrings`, `WebhookSecret`, `ConsumerKey`, `ConsumerSecret`, sekcja `Smtp` z `Password`, oraz ich postać ze zmiennych środowiskowych (`__`);
    - fragmenty connection stringu SQL (`Password=`, `User Id=` / `Uid=`, `Initial Catalog=`);
    - klucze REST WooCommerce (`ck_`/`cs_` + 40 znaków szesnastkowych);
    - nagłówki kluczy prywatnych (`-----BEGIN … PRIVATE KEY-----`);
    - nazwy sekretów wdrożeniowych z `wdrozenia.md` §3 (`VERCEL_TOKEN`, `API_FTP_PASSWORD`, `PRODUCTION_MIGRATIONS_CONNECTION`).
40. Lista reguł jest w skrypcie (jedno miejsce). Brak katalogu albo pusty katalog → kod ≠ 0 (skan nie przechodzi „na pusto”).
41. Wywołania: zadanie `frontend` po `npm run build` (`dist`), `deploy.yml` przed `vercel deploy` (`.vercel/output/static`), obok istniejącego gitleaks.
42. `.gitleaks.toml` dostaje regułę kluczy REST WooCommerce (`ck_`/`cs_` + 40 znaków szesnastkowych), działającą w skanie historii i pakietu. Wyjątki tylko dla konkretnych plików z placeholderami (zasada z nagłówka pliku).

## Data / API

### Storage

- None. Bez migracji i zmian schematu. Skrypty tworzą i usuwają tylko bazy robocze o nazwach `StoneFlowRestoreCheck_*`, `StoneFlowDeployTest_*` i `StoneFlowE2E` na serwerach testowych.

### Endpoints

Brak nowych i zmienionych endpointów. Testy używają istniejących:

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| GET | `/api/reports/sales` | `period`, `year`, `from`, `to` | karty, szereg, kanały, kategorie | sesja | odczyt w testach H |
| GET | `/api/reports/sales/components` | okres + `pageSize` | pozycje, `sum` | sesja | odczyt w testach H |
| GET | `/api/reports/cash-flow` | okres | karty, szereg, podział | sesja | odczyt w testach H |
| GET | `/api/operations` | `from`, `to`, `kind` | pozycje, `totals`, `launchDate` | sesja | testy H, kontrola daty (D) |
| GET | `/api/expenses` | `costDateFrom`, `costDateTo` | pozycje, `grossTotal` | sesja | testy H |
| GET | `/api/change-history` | `businessFrom`, `businessTo`, `objectType` | wpisy | sesja | testy H, E2E |
| POST | `/api/auth/password-reset/request`, `/confirm` | adres, token, hasło | 202/204 | anonimowe (istniejące) | global setup E2E |

### Types crossing a boundary

- Brak nowych typów. Testy czytają istniejące odpowiedzi JSON (kwoty jako ciągi dziesiętne).
- Kontrakt skryptu `verify-restore.sh`: argument — ścieżka `.bak` po stronie serwera SQL; zmienne `STONEFLOW_TEST_SQLSERVER`, `SQLCMD`, opcjonalnie `KEEP_DB`; kod 0 — kopia odtworzona, spójna i zmigrowana.
- Kontrakt `check-bundle-secrets.mjs`: argument — katalog; kod 0 — brak trafień.

## Invariant impact

| Invariant | How it is respected |
|---|---|
| 1. Sales separate from cash | Bez zmian reguł. Testy H sprawdzają rozdzielenie w każdym scenariuszu: niezapłacone zamówienie nie zwiększa wpływów, wpłata nie tworzy sprzedaży, wydatek planowany nie jest wypływem. |
| 2. Backend decimal money | Bez zmian obliczeń. Testy porównują ciągi kwot z API i sumy liczone w C# na `decimal`; E2E sprawdzają tylko tekst sformatowany przez UI. |
| 3. API-enforced access | Bez nowych endpointów i wyjątków w `EndpointSecurityTests`. E2E sprawdza, że po zakończeniu sesji UI nie pokazuje danych, a API odpowiada 401 (przekierowanie przez interceptor). Global setup używa tylko istniejących anonimowych endpointów resetu hasła. |
| 4. Retries never duplicate | Scenariusz 12 sprawdza, że ponowne uruchomienie zadania cyklicznego nie tworzy duplikatu; E2E sprawdza, że podwójne kliknięcie zapisu tworzy jeden rekord. `verify-restore.sh` jest bezpieczny do powtórzenia (unikalna nazwa bazy roboczej, sprzątanie). |
| 5. Financial audit trail | Bez zmian zapisu audytu. Test 38 i E2E pkt 27.6 sprawdzają, że zmiana raportu jest wyjaśniona wpisem „Historii zmian”. Procedura odtworzenia mówi, że odtworzenie kopii cofa też historię zmian do chwili kopii i wymaga odnotowania w checkliście. |
| 6. Secrets stay server-side | Nowy skan pakietu (I) obok gitleaks. Data uruchomienia, sekrety i connection stringi zostają w konfiguracji serwera, nie w repozytorium. E2E i CI nie używają sekretów środowiska `production`; hasło właściciela E2E jest losowe i jednorazowe. Skrypt odtworzenia nie wypisuje connection stringu ani danych. |
| 7. Module and layer boundaries | Bez zmian kodu modułów. Testy H komunikują się z modułami tylko przez HTTP. |
| 8. No identifying data in docs | Procedury i checklista używają placeholderów (`<adres-api>`, `<adres-aplikacji>`), dane testów są fikcyjne (`owner@example.com`), zrzuty i raporty E2E powstają na danych fikcyjnych. Kopie produkcyjne nie trafiają do repozytorium, CI ani artefaktów; checklista przechowuje tylko opis i link do dowodu, nie zrzuty z danymi klientów. |

## Risk

**high** — zadanie dopuszcza produkcyjne uruchomienie systemu finansowego. Obejmuje odtwarzanie bazy (operacja nieodwracalna przy pomyłce bazy docelowej), procedurę powrotu przy migracjach, które się nie cofają, i ostateczne uzgodnienie obu widoków finansowych. Błąd w procedurze może skończyć się utratą danych albo błędnymi raportami bez widocznego błędu. Wymaga przeglądu przez człowieka przed merge.

## UI

- Brak nowych tras, komponentów i tekstów.
- Zmiany UI są możliwe wyłącznie jako poprawki naruszeń znalezionych przez axe, testy klawiatury i reflow (F pkt 29–31) w istniejących widokach. Każda poprawka używa tokenów `--sf-*` i komponentów z `shared/ui/`, ma test jednostkowy komponentu tam, gdzie zmienia zachowanie, i jest wymieniona w `06-implementation-done.md`.
- Odbiór według design guide §10 — podział dowodów:
  - kontrast rzeczywistych par: axe (`color-contrast`) w E2E + tabela kontrastów tokenów w design guide §2; stany hover/focus/selected i błędy — scenariusz ręczny;
  - klawiatura, fokus, dialogi, etykiety: E2E (F pkt 30) + axe;
  - tekstowy odczyt wykresów: istniejące testy komponentów (SF-19, SF-20) + axe;
  - ładowanie, pustka, błąd, wygaśnięcie sesji, niekompletność danych: istniejące testy komponentów (SF-14, SF-19, SF-20), E2E dla wygaśnięcia sesji, scenariusz ręczny dla niekompletnej integracji;
  - telefon i tablet: E2E reflow 375 px + scenariusz ręczny zapisu formularza na telefonie;
  - podwójne kliknięcie zapisu: E2E (F pkt 27.2);
  - brak danych identyfikujących w przykładach i zrzutach: przegląd dokumentów zadania.

## Edge Cases

- **Kopia z nowszej wersji SQL Server niż lokalny kontener** (`2022-CU27`) — `RESTORE` się nie powiedzie. Skrypt kończy się błędem z komunikatem o wersji; procedura każe sprawdzić wersję SQL na hostingu i użyć zgodnego obrazu.
- **Kopia w archiwum** (`.zip`) — procedura każe rozpakować; skrypt przyjmuje tylko `.bak`.
- **Kopia z migracjami nowszymi niż kod** (np. po powrocie kodu) — `migrate.sh` nic nie stosuje; skrypt wypisuje ostatnią migrację każdego schematu, a procedura wyjaśnia, że to stan oczekiwany przy expand/contract.
- **Baza robocza o tej samej nazwie już istnieje** — nazwa zawiera unikalny identyfikator przebiegu; skrypt nigdy nie odtwarza `WITH REPLACE` na bazę o innej nazwie niż robocza.
- **Przerwanie skryptu** — pułapka `EXIT` usuwa bazę roboczą (jak `test-migrate.sh`).
- **Pomyłka bazy przy odtwarzaniu na hostingu** — procedura wymaga odtwarzania do osobnej bazy, jeśli plan na to pozwala, i świeżej kopii przed odtworzeniem na bazę produkcyjną.
- **Revert commitu zawierającego migrację** — zabroniony w procedurze (usunąłby plik zastosowanej migracji); w tym przypadku fix forward.
- **Vercel „Promote” poprzedniej wersji a następna publikacja** — kolejna publikacja z `main` nadpisze przywróconą wersję, jeśli nie ma revertu; procedura każe zrobić oba kroki.
- **E2E na przełomie miesiąca/doby** — daty testów wyliczane raz na przebieg; testy używają dat w środku miesiąca i własnych zakresów, aby przebieg o północy nie zmieniał okresu.
- **Limit logowania 10/min** — testy logują się w UI tylko w teście logowania i w global setup; pozostałe używają `storageState`.
- **Link resetu nie pojawił się w logu** (np. zmiana formatu logu) — global setup kończy się czytelnym błędem po limicie czasu, zamiast zawieszać przebieg.
- **Fałszywe trafienie skanu pakietu** (np. słowo „Password” w etykiecie) — reguły szukają nazw kluczy serwera i fragmentów `Klucz=` connection stringu, nie słów UI; test skryptu sprawdza, że aktualny build przechodzi, a pliki z każdym wzorcem są wykrywane.
- **Scenariusz zwrotu do zamówienia sprzed uruchomienia** — tylko wypływ, nie obniża sprzedaży (PRD §4).
- **Wydatek ujemny nieopłacony** — obniża koszty, nie wypływy.

## Dependencies

- SF-4 (procedura wdrożeń, `migrate.sh`, `deploy-dry-run`) — w main.
- SF-18 (Historia zmian) — w main.
- SF-20 (Przepływy pieniężne) i SF-19 (Sprzedaż) — w main.
- SF-16 (wydatki cykliczne i zadanie) — w main.
- **Kroki na hostingu** (nie blokują implementacji, blokują zamknięcie kryteriów hostingowych): D-08 (aktywny plan, zdalny SQL, miejsce kopii), D-05 (SMTP — bez niego produkcyjne API nie wystartuje), konta Vercel i SmarterASP.NET, `DEPLOY_ENABLED=true`, data uruchomienia od właściciela.

## Assumptions

Krok pytań został wykonany: właściciel polecił pracę autonomiczną. Poniższe rozstrzygnięcia są decyzjami wykonawcy do potwierdzenia przez właściciela. Żadna nie jest sprzeczna z PRD ani z zapisanymi decyzjami.

1. **Decyzja wykonawcy z 11.11.2026 (do potwierdzenia przez właściciela):** kryteria wymagające hostingu produkcyjnego albo właściciela (konfiguracja harmonogramu i retencji kopii w panelu, próba odtworzenia, migracja na hostingu, próba powrotu, ustawienie prawdziwej daty uruchomienia) nie mogą być wykonane przez wykonawcę. Wykonawca dostarcza wersjonowane procedury w `docs/operations/` oraz skrypty, które da się zautomatyzować i sprawdzić lokalnie lub w CI. Te kryteria zostają otwarte, a checklista `odbior-mvp.md` jest miejscem na dowody. Zadanie może przejść do `done` z otwartymi pozycjami właściciela wymienionymi w `06-implementation-done.md`.
2. **Decyzja wykonawcy z 11.11.2026 (do potwierdzenia przez właściciela):** produkcja nie może przyjąć daty domyślnej — przy braku lub złym formacie API nie startuje. Sprawdzono, że tak jest już dziś (`ValidateOnStart`, `LaunchDateStartupTests` w środowisku Production, brak daty w `appsettings.json`), więc zadanie nie zmienia kodu, tylko dokumentuje procedurę. Wartość daty podaje właściciel przez konfigurację hostingu; nie jest zapisywana w repozytorium.
3. **Decyzja wykonawcy z 11.11.2026 (do potwierdzenia przez właściciela):** E2E w Playwright (TypeScript) w `app/e2e/`, na prawdziwej aplikacji Angular, prawdziwym API i SQL Server, w CI jako osobne zadanie `e2e` na PR do `main`. Procesy: logowanie i wygaśnięcie sesji, zamówienie ręczne z wpłatą, wydatek jednorazowy i opłacenie, wypłata właściciela, Pulpit (sprzedaż i przepływy), Historia zmian wyjaśniająca zmianę raportu, odświeżenie podstrony SPA. Pełny przepływ WooCommerce zostaje w testach integracyjnych (brak sklepu w E2E). Dochodzą automatyczne kontrole dostępności `@axe-core/playwright` (tagi WCAG 2.2 AA) na głównych stronach i obsługa klawiaturą menu głównego oraz formularza wydatku.
4. **Decyzja wykonawcy z 11.11.2026 (do potwierdzenia przez właściciela):** scenariusze PRD §11 przechodzi pakiet testów integracyjnych backendu (istniejący projekt, SQL Server), który buduje każdy wymieniony scenariusz i dodatkowo wydatek cykliczny, oraz sprawdza, że oba widoki (`/api/reports/sales` z kartą „Koszty”, `/api/reports/cash-flow`) uzgadniają się z rekordami źródłowymi i listami (zestawienie składników sprzedaży, `/api/operations`, `grossTotal` listy wydatków), a zmiana minionego okresu jest widoczna w `/api/change-history`.
5. **Decyzja wykonawcy z 11.11.2026 (do potwierdzenia przez właściciela):** sekrety — skrypt w CI skanuje zbudowany pakiet frontendu pod kątem wzorców sekretów i znanych kluczy konfiguracji serwera, w uzupełnieniu istniejącego skanu repozytorium i pakietu (gitleaks).
6. **Decyzja wykonawcy z 11.11.2026 (do potwierdzenia przez właściciela):** bez nowych funkcji produktu, bez migracji danych z Excela, bez anonimizacji (SF-22).
7. **Decyzja wykonawcy z 11.11.2026 (do potwierdzenia przez właściciela):** kopia przed wdrożeniem z migracją (SF-4 Q4) jest krokiem ręcznym z procedury, zapisanym w opisie PR, bo panel hostingu nie ma potwierdzonego API do kopii. Nie dodaje się automatycznej kopii bazy produkcyjnej do workflow.
8. **Decyzja wykonawcy z 11.11.2026 (do potwierdzenia przez właściciela):** niezależna kopia nie jest robiona przez GitHub Actions (eksport bazy do artefaktów), bo przeniosłaby dane osobowe klientów do CI. Miejsce kopii wybiera właściciel (D-08 pkt 4 — otwarte).
9. **Decyzja wykonawcy z 11.11.2026 (do potwierdzenia przez właściciela):** E2E uruchamia API w środowisku Development (ciasteczko sesji bez HTTPS, e-mail w trybie `Log`), z datą uruchomienia i połączeniem ze zmiennych środowiskowych. Konto właściciela powstaje istniejącym przepływem resetu hasła, z linkiem odczytanym z logu API. Nie dodaje się endpointów ani przełączników tylko dla testów. Ciasteczko `__Host-` i HTTPS w Production pokrywają istniejące testy integracyjne (`HttpsTests`, `LoginAndSessionTests`) i kontrole po publikacji w `deploy.yml`.
10. **Decyzja wykonawcy z 11.11.2026 (do potwierdzenia przez właściciela):** naruszenia znalezione przez axe, testy klawiatury i reflow są poprawiane w tym zadaniu, jeśli dotyczą istniejących widoków i nie zmieniają ich funkcji. Naruszenie wymagające przeprojektowania widoku zostaje zgłoszone jako osobne zadanie i wpisane do checklisty jako otwarte (bez wyłączania reguły axe).
11. **Decyzja wykonawcy z 11.11.2026 (do potwierdzenia przez właściciela):** wygaśnięcie sesji w E2E jest wywoływane wylogowaniem z drugiego kontekstu (kończy wszystkie sesje), a nie 60-minutowym oczekiwaniem; 60 minut bezczynności pokrywa test integracyjny z zegarem testowym.
12. **Decyzja wykonawcy z 11.11.2026 (do potwierdzenia przez właściciela):** powrót backendu odbywa się przez revert na `main` i zwykłą publikację, bez przechowywania poprzednich paczek API. `check-fresh-commit.sh` zostaje bez zmian.
13. Rozbieżność SF-1 / D-08: artykuł dostawcy podaje kopie co drugi dzień i CustomBackup z tygodniową retencją, a D-08 zakłada codzienną kopię z 30-dniową retencją. Procedura wymaga sprawdzenia w panelu; jeśli panel nie spełnia celu, rozwiązanie wybiera właściciel (pytanie otwarte 1).
14. Rozbieżność w dokumentach: `wdrozenia.md` jest w katalogu głównym repozytorium, a `conventions.md` (tabela układu) i artefakty SF-4 podają `docs/wdrozenia.md`. Zadanie nie przenosi pliku (poza zakresem); nowe procedury odwołują się do rzeczywistej ścieżki, a `conventions.md` dostaje poprawioną ścieżkę razem z dopisaniem `docs/operations/`.
15. W chwili pisania specyfikacji `README.md` ma niezatwierdzone zmiany innej osoby. Implementacja dopisuje instrukcję E2E dopiero po ich zatwierdzeniu albo w uzgodnieniu z autorem, bez nadpisywania.
16. `@playwright/test` i `@axe-core/playwright` dochodzą jako `devDependencies` z dokładnymi wersjami (zasada przypinania z `conventions.md`), najnowsze stabilne w dniu implementacji; wersje i ich zgodność z Node 24 sprawdza plan.
17. Ryzyko pozostaje **high**.

### Pytania otwarte do właściciela (nie blokują implementacji)

1. Gdzie ma być przechowywana niezależna kopia (D-08 pkt 4) i kto ją pobiera — ręcznie czy narzędziem po stronie właściciela?
2. Czy plan SmarterASP.NET pozwala odtworzyć kopię do osobnej bazy (próba bez dotykania produkcji)? Jeśli nie — czy próba odtworzenia ma się odbyć na bazie produkcyjnej przed startem?
3. Data uruchomienia (PRD §4) — do podania przed startem produkcyjnym.

## Acceptance Criteria

Kryteria ze zgłoszenia:

- [ ] Odtworzenie kopii i migracja schematu zostały sprawdzone na hostingu. **Wykonawca:** `verify-restore.sh` + `test-verify-restore.sh` przechodzą w `deploy-dry-run` (kopia pełna, kopia częściowo zmigrowana, plik uszkodzony, brak pliku), procedura `kopie-i-odtworzenie.md` istnieje. **Właściciel (otwarte):** próba na hostingu z pomiarem RTO/RPO i log migracji pierwszej publikacji w `odbior-mvp.md`.
- [ ] Sprawdzono procedurę powrotu do działającej wersji. **Wykonawca:** `powrot-do-wersji.md` z zasadą „rollback kodu nie cofa migracji”, krokami dla Vercel i SmarterASP.NET i opisem próby. **Właściciel (otwarte):** próba na hostingu w `odbior-mvp.md`.
- [ ] Repozytorium i pakiet frontendu nie zawierają sekretów. Zadanie `secrets` (gitleaks z nową regułą WooCommerce) i skan `dist` (gitleaks + `check-bundle-secrets.mjs`) przechodzą w CI; test skryptu wykrywa każdy wzorzec z pkt 39 i przepuszcza bieżący build.
- [ ] Wszystkie scenariusze z sekcji 11 przechodzą, w tym zamówienie i wydatek otwarte w dniu uruchomienia. Testy H pkt 36 (1–13) przechodzą.
- [ ] Oba widoki uzgadniają się z rekordami źródłowymi, także karta „Koszty”. Test H pkt 37 przechodzi dla miesiąca, roku i zakresu przecinającego datę uruchomienia.
- [ ] Kluczowe procesy mają testy E2E, a CI przechodzi. Zadanie `e2e` z procesami F pkt 27–28 jest zielone na PR SF-21 razem z `backend`, `frontend`, `secrets`, `deploy-dry-run`.
- [ ] Spełnione są kontrast, obsługa klawiatury i prezentacja niekompletności danych. Axe bez naruszeń (pkt 29), testy klawiatury (pkt 30) i reflow (pkt 31) przechodzą; scenariusze ręczne (stany hover/focus, niekompletna integracja, telefon) w `06-implementation-done.md`.

Kryteria wynikające z zakresu zgłoszenia i PRD:

- [ ] Zmiana raportu za miniony okres jest wyjaśniona listą „Historia zmian” — test H pkt 38 i E2E pkt 27.6.
- [ ] Data uruchomienia: brak lub zły format zatrzymuje start API w Production (istniejące `LaunchDateStartupTests` przechodzą); `data-uruchomienia.md` opisuje ustawienie i kontrolę. **Właściciel (otwarte):** ustawienie prawdziwej daty i dowód w `odbior-mvp.md`.
- [ ] SPA działa po odświeżeniu podstrony — E2E pkt 27.7; na Vercel — kontrola `/logowanie` w `deploy.yml` i pozycja właściciela w checkliście.
- [ ] `odbior-mvp.md` zawiera każde kryterium zgłoszenia i PRD §11 z rodzajem dowodu; wiersze automatyczne mają link do przebiegu CI.
- [ ] `wdrozenia.md` wymienia `e2e` w wymaganych kontrolach PR i odsyła do nowych procedur; `conventions.md` opisuje uruchomienie E2E i skryptu odtworzenia.
- [ ] Bez zmian schematu, endpointów i reguł finansowych; istniejące testy przechodzą bez zmian (poza ewentualnym wydzieleniem funkcji w `test-migrate.sh`).

## Verification

- build:
  - `dotnet restore stone-flow.slnx --locked-mode`;
  - `dotnet format stone-flow.slnx --verify-no-changes --no-restore`;
  - `dotnet build stone-flow.slnx -c Release --no-restore`;
  - `app/`: `npm ci`, `npm run format:check`, `npm run lint`, `npm run typecheck` (obejmuje `app/e2e/`), `npm run build`.
- tests:
  - `dotnet test --solution stone-flow.slnx -c Release --no-build` — w tym `Acceptance/MvpFinancialScenariosTests` i `LaunchDateStartupTests`;
  - `app/`: `npm test -- --watch=false`, `npm run test:scripts` (w tym `check-bundle-secrets.test.mjs`), `node scripts/check-bundle-secrets.mjs dist`;
  - `deploy/scripts/test-migrate.sh` i `deploy/scripts/test-verify-restore.sh` (lokalnie z `STONEFLOW_TEST_SQLSERVER` i `SQLCMD` albo w `deploy-dry-run`);
  - `app/`: `npm run e2e` (lokalnie i zadanie CI `e2e`);
  - CI na PR SF-21: `backend`, `frontend`, `secrets`, `deploy-dry-run`, `e2e` zielone.
- manual: scenariusze testów ręcznych UI w `06-implementation-done.md`:
  - stany hover, focus, selected i błędów na Pulpicie, w formularzach i tabelach (kontrast);
  - niekompletna integracja na Pulpicie (komunikat i „Ostatnia pełna kontrola”);
  - zapis zamówienia i wydatku na telefonie (375 px) i tablecie;
  - czytnik ekranu na wykresach i dialogu potwierdzenia;
  - przejście `docs/operations/kopie-i-odtworzenie.md` lokalnie na kopii z `BACKUP DATABASE` lokalnej bazy deweloperskiej.
- owner (poza repozytorium, dowody w `docs/operations/odbior-mvp.md`): konfiguracja kopii, próba odtworzenia, migracja na hostingu, próba powrotu, data uruchomienia, weryfikacja obu hostingów.
