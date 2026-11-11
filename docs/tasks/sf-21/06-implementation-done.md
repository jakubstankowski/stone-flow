# Implementation done — SF-21

> Manifest zaktualizowany po przeglądzie kodu (`05-code-review.md`: **PASS WITH ISSUES**). Przegląd
> znalazł jeden błąd krytyczny (C-1, `docker exec -i` w pętli `verify-restore.sh`), poprawiony razem z
> T-1, N-3 i N-4; uwagi N-1, N-2, T-2 zostają otwarte (Open Items).

## Summary

- Skrypty `deploy/scripts/`: wspólne funkcje SQL w `sql-common.sh` (refaktor `test-migrate.sh` bez
  zmiany zachowania), sprawdzenie kopii `.bak` `verify-restore.sh` i jego test
  `test-verify-restore.sh` (kopia pełna, częściowo zmigrowana, obcięta, brak pliku) w zadaniu CI
  `deploy-dry-run`.
- Reguła gitleaks `woocommerce-rest-key` z kontrolą w zadaniu `secrets`; skan zbudowanego pakietu
  `app/scripts/check-bundle-secrets.mjs` z testem, w `frontend` (CI) i przed publikacją (`deploy.yml`).
- Testy integracyjne `Acceptance/MvpFinancialScenariosTests`: 13 scenariuszy PRD §11, uzgodnienie
  obu widoków z rekordami (miesiąc, rok, zakres przecinający uruchomienie) i zmiana minionego okresu
  wyjaśniona „Historią zmian”.
- E2E Playwright w `app/e2e/` (procesy, axe WCAG 2.2 AA, reflow 375 px, klawiatura, sesja) i nowe
  zadanie CI `e2e`.
- Jedna poprawka dostępności (F-A1): lista definicji w sekcji „Uwagi” szczegółów zamówienia.
- Dokumenty: `docs/operations/kopie-i-odtworzenie.md`, `powrot-do-wersji.md`,
  `data-uruchomienia.md`, `odbior-mvp.md`; zmiany w `wdrozenia.md` (§1, §3, §7) i
  `.sdd/context/conventions.md`.
- Bez zmian w `api/src/**`, schemacie bazy i migracjach.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| A. Procedura kopii i odtworzenia | Implemented | `docs/operations/kopie-i-odtworzenie.md`; kroki hostingowe otwarte dla właściciela |
| B. `verify-restore.sh` + `test-verify-restore.sh` w `deploy-dry-run` | Implemented | po poprawce C-1 z przeglądu (stdin `docker exec -i`); lokalnie na LocalDB przeszło także z symulacją `docker exec -i` (sekcja Verification) |
| C. Powrót do działającej wersji | Implemented | `powrot-do-wersji.md` ze ścieżką częściowego revertu commitu z migracją |
| D. Data uruchomienia | Implemented | `data-uruchomienia.md`; bez zmian kodu, dowód `LaunchDateStartupTests` |
| E. Checklista odbioru | Implemented | `odbior-mvp.md`: 7 kryteriów SF-21, PRD §11, 9 pozycji design guide §10, pozycje właściciela; linki CI jako placeholder |
| F. Testy E2E (pkt 24–32) | Implemented | odstępstwa: porty konfigurowalne, `reuseExistingServer: false`, `@playwright/test` 1.64.1 (sekcja Deviations) |
| G. Zadanie CI `e2e` | Implemented | nie uruchomione w CI (brak push) |
| H. Scenariusze PRD §11 (pkt 35–38) | Implemented | 15 testów, przeszły na LocalDB |
| I. Skan pakietu i reguła gitleaks | Implemented | |
| Scope 7. Poprawki dostępności | Implemented | 1 poprawka; brak pozycji wymagających przeprojektowania |
| Scope 8. `wdrozenia.md`, `conventions.md` | Implemented | |
| Scope 8. README (uruchomienie E2E) | Not implemented | README ma niezatwierdzone zmiany innej osoby (Assumptions 15); instrukcja jest w `conventions.md` (Verification) — Open Items |
| Scope 9. Scenariusze testów ręcznych UI | Implemented | sekcja poniżej |

## Files

### Created

- `deploy/scripts/sql-common.sh`, `deploy/scripts/verify-restore.sh`, `deploy/scripts/test-verify-restore.sh`
- `api/tests/StoneFlow.Api.IntegrationTests/Acceptance/AcceptanceTest.cs`
- `api/tests/StoneFlow.Api.IntegrationTests/Acceptance/MvpFinancialScenariosTests.cs`
- `app/scripts/check-bundle-secrets.mjs`, `app/scripts/check-bundle-secrets.test.mjs`
- `app/playwright.config.ts`, `app/tsconfig.e2e.json`
- `app/e2e/global-setup.ts`, `app/e2e/auth.setup.ts`
- `app/e2e/orders.e2e.ts`, `expenses.e2e.ts`, `withdrawals.e2e.ts`, `dashboard.e2e.ts`,
  `change-history.e2e.ts`, `spa-refresh.e2e.ts`, `a11y.e2e.ts`, `a11y-auth.e2e.ts`,
  `keyboard.e2e.ts`, `session.e2e.ts`
- `app/e2e/support/env.ts`, `api-process.ts`, `connection.ts`, `dates.ts`, `tag.ts`, `money.ts`,
  `fixtures.ts`, `ui.ts`, `a11y.ts`
- `docs/operations/kopie-i-odtworzenie.md`, `powrot-do-wersji.md`, `data-uruchomienia.md`, `odbior-mvp.md`

### Modified

- `deploy/scripts/test-migrate.sh`
- po przeglądzie kodu: `deploy/scripts/sql-common.sh`, `deploy/scripts/verify-restore.sh`,
  `deploy/scripts/test-verify-restore.sh`, `docs/operations/powrot-do-wersji.md`
- `.gitleaks.toml`, `.github/workflows/ci.yml`, `.github/workflows/deploy.yml`
- `app/package.json`, `app/package-lock.json`, `app/angular.json`, `app/.gitignore`, `app/.prettierignore`
- `app/src/app/features/orders/order-details-page.html`
- `wdrozenia.md`, `.sdd/context/conventions.md`

## Planned Units

| Item from the plan | Status |
|---|---|
| B-S0 `sql-common.sh` + refaktor `test-migrate.sh` | Exists |
| B-S1 `verify-restore.sh` | Exists |
| B-S2 reguła gitleaks `woocommerce-rest-key` | Exists |
| B-S3 krok CI „Reguła kluczy WooCommerce wykrywa wzorzec” (`secrets`) | Exists |
| B-S4 `test-verify-restore.sh` | Exists |
| B-S5 krok CI „Próba odtworzenia kopii…” (`deploy-dry-run`, limit 35 min) | Exists |
| B-T1 `Acceptance/AcceptanceTest.cs` | Exists |
| B-T2…B-T5 `MvpFinancialScenariosTests` (H-1…H-15) | Exists |
| F-E0 zależności, `tsconfig.e2e.json`, `typecheck`, `lintFilePatterns`, ignore | Exists |
| F-E1 `playwright.config.ts` (projekty `setup`, `chromium`, `session`) | Exists |
| F-E2 global setup; teardown | Exists — teardown jako funkcja zwracana z global setup (bez osobnego `global-teardown.ts`) |
| F-E3 `auth.setup.ts` | Exists |
| F-E4, F-E5 E-2…E-8 | Exists |
| F-E7 axe i reflow (E-9) | Exists |
| F-E8 klawiatura (E-10) | Exists |
| F-E9 sesja (E-1) | Exists |
| F-A0 punkt kontrolny dostępności | Exists — 1 naruszenie (niżej) |
| F-A1 poprawki | Exists — 1 poprawka |
| F-S1…F-S3 skan pakietu, test, kroki CI | Exists |
| F-C1 zadanie `e2e` | Exists |
| D-1…D-4 procedury i checklista | Exists |
| D-5 `wdrozenia.md`, `conventions.md` | Exists |
| D-6 README | Missing — Open Items |

**F-A0 — wynik punktu kontrolnego** (pierwszy lokalny przebieg axe, klawiatury i reflow):

| Strona | Reguła axe | Element | Klasyfikacja | Działanie |
|---|---|---|---|---|
| Szczegóły zamówienia | `definition-list`, `dlitem` (serious) | sekcja „Uwagi”: `<dl>` zawierał `div.grid` z parami `dt`/`dd` | poprawka w istniejącym widoku | F-A1: pary bezpośrednio w `<dl>` (siatka `sm:grid-cols-2`, komentarz `sm:col-span-2`); wygląd bez zmian, zachowanie bez zmian (bez nowego testu komponentu) |

Klawiatura i reflow: bez naruszeń. Pozycji „wymaga przeprojektowania”: brak. Wyłączeń reguł axe: brak.

## Database

None.

## Tests

| Test from the plan | Status | Run result |
|---|---|---|
| H-1…H-13 `MvpFinancialScenariosTests` | Exists | Passed (LocalDB) |
| H-14 `Both_views_reconcile_with_source_records_for_month_year_and_range_across_launch` | Exists | Passed (LocalDB) |
| H-15 `Change_of_a_past_period_is_explained_by_change_history` | Exists | Passed (LocalDB) |
| `LaunchDateStartupTests` (bez zmian) | Exists | Passed (LocalDB, razem z H: 21/21) |
| M-1 `test-migrate.sh` po refaktorze | Exists | Passed (LocalDB) |
| R-1…R-5 `test-verify-restore.sh` | Exists | Passed (LocalDB, 4 przypadki + sprzątanie); po poprawce C-1 Passed też z `SQLCMD` czytającym stdin (symulacja `docker exec -i`), przed poprawką ten wariant Failed (`Msg 1834`) |
| G-1 kontrola reguły gitleaks | Exists | odpowiednik lokalnie: gitleaks 8.30.1 wykrył próbkę (kod 1), próbka 39 znaków nie (kod 0); krok CI Not run |
| S-1…S-5 `check-bundle-secrets.test.mjs` | Exists | Passed (`npm run test:scripts` 20/20) |
| S-6 skan `dist` | Exists | Passed (44 pliki) |
| E-0 typecheck, lint | Exists | Passed |
| E-1…E-10 Playwright | Exists | Passed lokalnie: 27/27 (`npm run e2e`, Chromium, LocalDB, świeża baza) |
| U-… testy komponentów dla F-A1 | — | brak zmiany zachowania; `ng test` 950/950 |

## Verification

| Command | Outcome |
|---|---|
| `dotnet build stone-flow.slnx -c Release` | Passed |
| `dotnet format stone-flow.slnx --verify-no-changes --no-restore` | Passed |
| `dotnet test … --filter-class …MvpFinancialScenariosTests --filter-class …LaunchDateStartupTests` (LocalDB) | Passed (21/21) |
| `dotnet test --solution stone-flow.slnx` (pełny) | Not run — zostawione dla CI (ponad 20 min, plan V-L) |
| `deploy/scripts/test-migrate.sh` (LocalDB) | Passed |
| `deploy/scripts/test-verify-restore.sh` (LocalDB, `RESTORE_TEST_DIR` w `%LOCALAPPDATA%\Temp`) | Passed (przed przeglądem i ponownie po poprawkach C-1/T-1) |
| `deploy/scripts/test-verify-restore.sh` z `SQLCMD` = wrapper czytający stdin (symulacja `docker exec -i`, przegląd) | Failed przed poprawką C-1 (`Msg 1834`); Passed po poprawce |
| `deploy/scripts/test-migrate.sh` po zmianie `sql-common.sh` (przegląd) | Passed |
| `dotnet test … MvpFinancialScenariosTests` + `LaunchDateStartupTests` po przeglądzie | Passed (21/21) |
| `npx playwright test e2e/expenses.e2e.ts` (porty 5280/4400, przegląd) | Passed (3/3) |
| `npm run typecheck` | Passed |
| `npm run lint` | Passed |
| `npm run format:check` | Passed |
| `npm run build` | Passed |
| `npx ng test --watch=false` | Passed (82 pliki, 950 testów) |
| `npm run test:scripts` | Passed (20/20) |
| `node scripts/check-bundle-secrets.mjs dist` | Passed |
| `npm ci --dry-run` | Passed |
| `npm run e2e` (porty 5280/4400, `STONEFLOW_E2E_SQLSERVER` = LocalDB) | Passed (27/27) |
| gitleaks 8.30.1 `git .` i `dir app/dist` (lokalnie, z nową regułą) | Passed (no leaks) |
| CI na PR (`backend`, `frontend`, `secrets`, `deploy-dry-run`, `e2e`) | Not run — gałąź nie została wypchnięta |
| `npm audit` | 10 ostrzeżeń moderate (eslint i zależności), istniejące przed zadaniem; bez zmian |

Uwagi do przebiegów lokalnych: pierwsze przebiegi E2E ujawniły błędy w testach (lokatory pól,
wyścig nawigacji po zapisie wpłaty, pusty stan tabeli) — poprawione przed ostatnim pełnym, zielonym
przebiegiem. Pełny przebieg na świeżej bazie przeszedł bez ponowień.

## Deviations

- `@playwright/test` **1.64.1** (plan: 1.65.0) i jawna zależność `playwright-core` 1.64.1 — 1.65.0 miało
  6 dni, a `@axe-core/playwright` wciągał `playwright-core` 1.65.0 jako peer; przypięto 1.64.1
  (14.10.2026). `@axe-core/playwright` 4.14.0 i `@types/node` 24.19.2 jak w planie.
- `package-lock.json`: npm 11.6.1 na Windows usuwał wpisy `@emnapi/core` i `@emnapi/runtime`
  (dodane celowo w commicie 76a3748 dla `npm ci` na Linuksie); wpisy przywrócono, a lockfile zawiera
  tylko dodane pakiety i zmianę flagi `peer` przy `jiti`.
- Porty E2E konfigurowalne zmiennymi `STONEFLOW_E2E_API_PORT` i `STONEFLOW_E2E_APP_PORT` (domyślnie
  5080 i 4200 jak w planie) — lokalnie porty 5080, 4200 i 4300 zajmowały inne serwery.
- `webServer.reuseExistingServer: false` (plan: `!CI`) — działający serwer deweloperski mógłby
  kierować proxy do API deweloperskiego.
- Teardown jako funkcja zwracana z `global-setup.ts` zamiast osobnego `global-teardown.ts`.
- Link resetu jest szukany w logu JSON API (`AddJsonConsole`), gdzie `&` ma postać `&`.
- H-14: rok 2026 kończy się na `2026-12-10` (raport liczy rok do dzisiaj), nie `2026-12-31`.
- Kwoty scenariuszy H inne niż przykłady w tabeli planu (np. korekta od dostawcy −250 zamiast −200,
  wypłata 1500 zamiast 500), żeby każdy składnik był rozpoznawalny w sumach testu uzgodnienia.
- E-6: wydatek cykliczny tworzony przez „Dodaj wydatek cykliczny” → formularz `/wydatki/nowy` z
  zaznaczonym „Powtarzaj co miesiąc” (tak działa istniejący UI); po zapisie szczegóły definicji.
- E-10: kategoria wybierana klawiszem litery (`M` → „Marketing”), bo `ArrowDown` na zamkniętym
  `select` w Chromium nie zmieniał wartości.
- `verify-restore.sh` i `test-verify-restore.sh` ustawiają `SET QUOTED_IDENTIFIER ON` dla `DBCC
  CHECKDB` i wstawiania danych (tabele z indeksami filtrowanymi).
- Instrukcja lokalnego E2E w `.sdd/context/conventions.md` (Verification) zamiast w README (D-6).
- Hasło właściciela E2E trafia do raportu HTML Playwright (tytuł kroku `Fill "…"`), wbrew
  rozstrzygnięciu planu „hasło nie trafia na dysk” — uwaga N-2 przeglądu, otwarta.

## Additional Implementation

Additional implementation not covered by the original documents:

- `app/e2e/support/env.ts` — konfiguracja portów (patrz Deviations).
- Komunikat global setup przy braku linku resetu wskazuje limit 3 linków na 15 minut na konto
  (`PasswordResetLimits`), który ogranicza częstotliwość lokalnych przebiegów na tej samej bazie.

## Open Items

1. **Human review required before merge** (risk `high`): procedury `docs/operations/*` (zwłaszcza
   odtworzenie do właściwej bazy i częściowy revert), `verify-restore.sh` (brak `REPLACE`, sprzątanie,
   brak wypisywania danych), zadanie `e2e` bez sekretów, brak danych identyfikujących.
2. Uwagi otwarte z przeglądu kodu (`05-code-review.md`): **N-1** kontrola reguły gitleaks w `secrets`
   nie sprawdza `RuleID` (zalecenie: raport JSON i `grep` na `woocommerce-rest-key`); **N-2** hasło
   konta testowego E2E w `playwright-report` (artefakt CI przy błędzie) — decyzja autora; **T-2**
   asercja negatywna w `expenses.e2e.ts` przed opłaceniem może przejść przed wczytaniem listy.
3. Push gałęzi, PR i zielone CI: `backend`, `frontend`, `secrets`, `deploy-dry-run`, `e2e` — nie
   uruchomione. W CI odnotować testy E2E zielone dopiero przy ponowieniu (niestabilne).
4. Linki do przebiegów CI w `odbior-mvp.md` (placeholder `<link do przebiegu CI — uzupełnia właściciel po merge>`).
5. README — sekcja „Testy E2E” (D-6) do dopisania po zatwierdzeniu bieżących zmian README przez ich
   autora; treść do przeniesienia z `conventions.md` (Verification).
6. Ochrona gałęzi `main`: `e2e` jako wymagana kontrola (administrator repozytorium).
7. Kroki właściciela na hostingu (Assumptions 1), dowody w `odbior-mvp.md` §4: konfiguracja kopii
   (rozbieżność SF-1), próba odtworzenia z RTO/RPO, migracja na hostingu, próba powrotu, prawdziwa
   data uruchomienia, weryfikacja obu hostingów i odświeżenia SPA na Vercel, zamknięcie D-05 i D-08,
   ocena „bez arkuszy”.
8. Pytania otwarte do właściciela ze spec: miejsce niezależnej kopii (D-08 pkt 4), możliwość
   odtworzenia do osobnej bazy w planie SmarterASP.NET, data uruchomienia.
9. Lokalny E2E: najwyżej ~3 przebiegi na 15 minut na tej samej bazie `StoneFlowE2E` (limit linków
   resetu na konto); na reużywanej bazie przy wielu przebiegach tego samego dnia wiersz testu w
   „Operacjach” może wypaść poza 1. stronę (25 pozycji, remis sortowania po id). W CI baza jest świeża.

## Scenariusze testów ręcznych (UI)

Warunki: zalogowany właściciel, API w Development (data uruchomienia z `appsettings.Development.json`
albo ustawiona ręcznie), dane fikcyjne. Przeglądarka desktop; dla scenariuszy 7–8 tryb urządzenia
375 px i 768 px w narzędziach deweloperskich (albo telefon/tablet).

| # | Kroki | Oczekiwany wynik |
|---|---|---|
| 1 | Pulpit → widok „Sprzedaż”: najedź kursorem na karty z odnośnikiem i przyciski okresu, potem przejdź po nich klawiszem Tab | Hover zmienia tło; fokus ma widoczny obrys w kolorze akcentu; teksty czytelne w obu stanach. |
| 2 | Pulpit: przełącz „Sprzedaż” / „Przepływy pieniężne” | Wybrany przycisk widoku wyróżniony nie tylko kolorem (stan zaznaczenia czytelny); podpisy kart różne (Sprzedaż/Koszty/Wynik przed wypłatami vs Wpływy/Wypływy/Zmiana środków). |
| 3 | Menu główne: kliknij kolejne sekcje | Aktywna pozycja ma podkreślenie i pogrubienie (nie tylko kolor), `aria-current="page"`. |
| 4 | Formularz wydatku: zapisz bez kwoty i opisu | Podsumowanie „Popraw błędy w formularzu” z linkami; pola z błędem mają czerwony komunikat pod polem, czytelny kontrast; wpisane dane zostają. |
| 5 | Listy (Zamówienia, Wydatki, Operacje): najedź na wiersz, sortuj kolumnę, przejdź Tabem po nagłówkach | Stan hover i fokus widoczny; kierunek sortowania oznaczony nie tylko kolorem. |
| 6 | Integracja nieskonfigurowana albo z błędem: otwórz Pulpit | Komunikat o niekompletnych danych integracji widoczny przy nagłówku okresu; wskaźnik integracji w nagłówku z tekstem „Ostatnia pełna kontrola: …”. |
| 7 | Telefon (375 px): dodaj zamówienie ręczne z wpłatą, zapisz | Formularz bez poziomego przewijania, pola i przyciski dostępne; zapis się udaje, kwoty czytelne. |
| 8 | Telefon i tablet (375 / 768 px): dodaj wydatek, otwórz listę wydatków i użyj filtrów | Zapis i filtrowanie działają; tabela przewija się w swoim kontenerze, strona nie. |
| 9 | Czytnik ekranu (NVDA/Narrator): Pulpit — wykresy obu widoków | Wykres ma tekstowy odpowiednik (opis/tabela) odczytywany przez czytnik; kwoty z „zł”. |
| 10 | Czytnik ekranu: szczegóły wydatku → „Usuń” | Dialog odczytany z tytułem „Usunąć wydatek?” i treścią; Escape zamyka, fokus wraca na „Usuń”. |
| 11 | Szczegóły zamówienia: sekcja „Uwagi” (po poprawce F-A1) | Komentarz w pełnej szerokości, „Utworzono” i „Ostatnia zmiana” obok siebie na szerokim ekranie, jeden pod drugim na wąskim; czytnik ekranu odczytuje pary nazwa–wartość. |
| 12 | Wyloguj w drugiej karcie/przeglądarce, potem w pierwszej kliknij „Zamówienia” | Przejście na `/logowanie` z komunikatem „Sesja wygasła. Zaloguj się ponownie.”, bez kwot z poprzedniej strony. |
| 13 | Lokalnie: `BACKUP DATABASE` bazy deweloperskiej do pliku `.bak` (LocalDB), potem `deploy/scripts/verify-restore.sh <ścieżka>` według `docs/operations/kopie-i-odtworzenie.md` §5 wariant B | Kod 0, „Kopia sprawdzona: odtworzona, spójna, zmigrowana.”, liczby wierszy zgodne z aplikacją; baza `StoneFlowRestoreCheck_*` usunięta. |
| 14 | Ocena układu: Pulpit w obu widokach obok referencji z `documents/` | Hierarchia, karty i wykresy nawiązują do referencji (ocena właściciela → `odbior-mvp.md` U2). |

## Kroki właściciela na hostingu

Według `docs/operations/odbior-mvp.md` §4 (dowody wpisuje właściciel):

1. Konfiguracja kopii w panelu SmarterASP.NET i niezależne miejsce kopii (`kopie-i-odtworzenie.md` §2–3).
2. Kopia ręczna przed pierwszą publikacją i przed każdym PR z migracją (§4).
3. Próba odtworzenia do osobnej bazy (albo przed startem do produkcyjnej) z pomiarem RTO i RPO (§6).
4. Pierwsza publikacja przez `deploy.yml` — log kroku „Migracje”.
5. Próba powrotu: revert nieszkodliwej zmiany i Promote w Vercel (`powrot-do-wersji.md` §5).
6. Ustawienie prawdziwej daty uruchomienia i kontrola (`data-uruchomienia.md`).
7. Weryfikacja obu hostingów i odświeżenia podstrony SPA na Vercel (`wdrozenia.md` §12).
8. Zamknięcie D-05 i D-08 w PRD; ochrona gałęzi z `e2e`; ocena „bez równoległych arkuszy”.
