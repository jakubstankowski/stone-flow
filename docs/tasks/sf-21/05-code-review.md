# Code review — SF-21

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Implementacja pokrywa całą specyfikację (procedury, `verify-restore.sh` z testem,
  E2E z axe i klawiaturą, zadanie CI `e2e`, 15 testów scenariuszy PRD §11 z uzgodnieniem, skan
  pakietu), a sumy testów finansowych zgadzają się z PRD §4/§11 (przeliczone ręcznie). Przegląd
  znalazł jeden błąd krytyczny: w CI i w wariancie Docker procedury `verify-restore.sh` odtwarzał
  tylko plik danych, bo `docker exec -i` w pętli `while read` zjadał pozostałe wiersze listy plików.
  `deploy-dry-run` byłby czerwony, a ręczna próba w Dockerze by się nie udała. Błąd odtworzono
  lokalnie, poprawiono w tym przeglądzie i sprawdzono. Zostają dwie uwagi niekrytyczne (kontrola
  reguły gitleaks i hasło E2E w raporcie HTML) do decyzji autora.
- Risk: high — **przed merge wymagany jest przegląd człowieka** (procedury `docs/operations/*`,
  `verify-restore.sh`, zadanie `e2e`), niezależnie od wyniku.

Przed poprawkami wynik byłby **FAIL** (C-1). Poprawki C-1, N-3, N-4 i T-1 wprowadzono w tym
przeglądzie (sekcja „Poprawki wprowadzone w przeglądzie”).

## Project Invariant Violations

| Invariant | File:line | What is wrong |
|---|---|---|

None.

Sprawdzenie po kolei:

| Invariant | Wynik | Uwagi |
|---|---|---|
| 1. Sales separate from cash | OK | H-1, H-2, H-11, H-14 sprawdzają rozdzielenie; E-3 (wydatek nie jest operacją przed opłaceniem). |
| 2. Backend decimal money | OK | Testy H porównują `decimal` z ciągów API (`Acceptance.Money`, `InvariantCulture`); E2E liczy w całkowitych groszach (`app/e2e/support/money.ts:6-14`), bez `parseFloat`. |
| 3. API-enforced access | OK | Bez nowych endpointów i wyjątków; global setup woła tylko istniejące anonimowe endpointy resetu hasła z CSRF. E-1 sprawdza 401 → `/logowanie` bez kwot. |
| 4. Retries never duplicate | OK | H-12 (drugie uruchomienie zadania, `total` = 2), E-2 (`dblclick` → `total` = 1), unikalna baza robocza i sprzątanie w `verify-restore.sh`. |
| 5. Financial audit trail | OK | H-15 i E-7; procedura odtworzenia każe odnotować cofnięcie „Historii zmian”. |
| 6. Secrets stay server-side | OK | Skan pakietu i reguła gitleaks; skrypty nie wypisują connection stringu (sprawdzone wyjście `sqlcmd` przy błędach — bez hasła); hasło `sa` w CI jest jednorazowe, jak w istniejącym `deploy-dry-run`. Hasło konta testowego E2E w raporcie HTML (N-2) to dane testowe efemerycznej bazy, nie sekret produkcyjny — uwaga niekrytyczna. |
| 7. Module and layer boundaries | OK | Bez zmian `api/src/**`; testy H tylko przez HTTP i `IJobRunner` (kontrakt `Shared.Application`). |
| 8. No identifying data in docs | OK | `docs/operations/*` — tylko placeholdery `<adres-api>`, `<adres-aplikacji>`, `<hasło-sa>`; dane testów fikcyjne (`owner@example.com`); brak domen i nazw handlowych (grep adresów i domen bez trafień). |

## Conventions Violations

### Critical (must fix before merge)

None. (Błąd krytyczny C-1 dotyczy poprawności — sekcja „Correctness Issues”; poprawiony.)

### Non-Critical (should fix)

1. **N-1 — `.github/workflows/ci.yml:116-126` kontrola reguły gitleaks nie sprawdza, która reguła
   zadziałała.** Krok uznaje sukces przy dowolnym trafieniu w próbce `consumer_key=ck_…`. Gdyby
   reguła `woocommerce-rest-key` przestała działać (np. błąd w `regex`), a próbkę złapała reguła
   domyślna (np. `generic-api-key` przy innej próbce), kontrola dalej by przechodziła. Zalecenie:
   `--report-format json --report-path "$RUNNER_TEMP/gitleaks-rule.json"` i `grep` na
   `"RuleID": "woocommerce-rest-key"`. **Otwarte** — gitleaks nie jest dostępny lokalnie, więc zmiany
   kroku nie dało się sprawdzić przed CI.
2. **N-2 — hasło właściciela E2E trafia do raportu HTML Playwright.** **Rozwiązane po przeglądzie:** `fillSecret` w `app/e2e/support/ui.ts` wpisuje hasło przez `evaluate()`; E2E 27/27. Playwright 1.64 zapisuje tytuł
   kroku `Fill "{value}"` (`Frame.fill` w `playwright-core/lib/coreBundle.js`), więc
   `app/e2e/auth.setup.ts:9` i `app/e2e/session.e2e.ts:15,23,32` wpisują hasło jawnym tekstem do
   `app/playwright-report/index.html` (sprawdzone na lokalnym przebiegu `setup`). W CI raport jest
   artefaktem przy błędzie (`ci.yml:265-274`, 7 dni). Wyłączenie śladu (`trace: 'off'`) tego nie
   obejmuje. To sprzeczne z decyzją planu („Hasło nie jest zapisywane na dysk”, Rozstrzygnięcia F pkt
   26) i z komentarzem `app/e2e/global-setup.ts:3-4`. Ryzyko jest niskie: hasło jest losowe, ważne
   jeden przebieg, a baza CI istnieje tylko w czasie zadania. **Otwarte — decyzja autora:** albo
   artefakt bez `playwright-report` (lub z raportem tylko z projektu `chromium`), albo świadoma
   akceptacja i poprawienie komentarza oraz zapisu w planie/manifeście.
3. **N-3 — `docs/operations/powrot-do-wersji.md:51-55` pętla `has-pending-model-changes` nie
   zatrzymuje się przy błędzie,** a następna linia to `git commit`. Administrator mógł zatwierdzić
   częściowy revert mimo oczekujących zmian modelu. **Poprawione:** dopisany warunek „każde polecenie
   pętli musi zakończyć się kodem 0; inaczej nie rób commita i przejdź do fix forward”.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| A. Procedura kopii i odtworzenia (pkt 1–7) | Covered | `docs/operations/kopie-i-odtworzenie.md`; wariant Docker działa dopiero po poprawce C-1 |
| B. `verify-restore.sh` (pkt 8–11) | Covered | po poprawce C-1; brak `REPLACE`, kontrola `DB_ID`, pułapka `EXIT`, `KEEP_DB`, `Msg 3169`, brak wypisywania danych |
| B. `test-verify-restore.sh` (pkt 12.1–12.5) | Covered | w CI dopiero po poprawce C-1 (wcześniej przypadek 1 kończyłby się błędem `Msg 1834`) |
| C. Powrót do wersji (pkt 13–17) | Covered | ze ścieżką częściowego revertu (N-3 poprawione) |
| D. Data uruchomienia (pkt 18–21) | Covered | bez zmian kodu; `LaunchDateStartupTests` przechodzą |
| E. Checklista odbioru (pkt 22–23) | Covered | linki CI jako placeholder (plan, uwaga 9) |
| F. E2E — stos, konfiguracja, global setup (pkt 24–26) | Covered | API przez `dotnet <dll>` zamiast `dotnet run` (plan); hasło w raporcie HTML — N-2 |
| F. Procesy 27.1–27.7 i 28 | Covered | `session`, `orders`, `expenses`, `withdrawals`, `dashboard`, `change-history`, `spa-refresh` |
| F. Axe (pkt 29) | Covered | 16 widoków + dialog, bez wyłączeń reguł |
| F. Klawiatura (pkt 30) | Covered | `keyboard.e2e.ts` |
| F. Reflow 375 px (pkt 31) | Covered | `expectNoHorizontalScroll` |
| F. Uruchomienie, raport, `retries` (pkt 32) | Covered | instrukcja w `conventions.md` zamiast README (D-6 otwarte) |
| G. Zadanie CI `e2e` (pkt 33–34) | Covered | YAML poprawny; usługa `mssql` 2022-CU27, port 1433, oczekiwanie, `migrate.sh` (6 kontekstów z `contexts.sh`), `npm ci`, Chromium `--with-deps`, artefakt `if: failure()` 7 dni bez `e2e/.auth`, `timeout-minutes: 30`, bez sekretów; nie uruchomione w CI |
| H. Scenariusze 36.1–36.13 | Covered | 13 testów; oczekiwania zgodne z PRD §4/§11 (zwrot do zamówienia sprzed uruchomienia tylko wypływem, wydatek ujemny obniża koszty i wypływy po opłaceniu) |
| H. Uzgodnienie (pkt 37) | Covered | sumy przeliczone ręcznie: XI 7440/160/1750/845; rok 7210/460/3950/(750+1500+925); 1.10–30.11 7440/160/1950/(700+845) — zgodne |
| H. Zmiana minionego okresu (pkt 38) | Covered | `updated` zamówienia `value` 1000.00 → 1250.00, `created` wydatku `grossAmount` null → 40.00 |
| I. Skan pakietu i reguła gitleaks (pkt 39–42) | Covered | kontrola reguły w `secrets` niespecyficzna — N-1 |
| Scope 7. Poprawki dostępności | Covered | `order-details-page.html`: pary `dt`/`dd` w `div` bezpośrednio w `dl` — poprawne według HTML (grupowanie `div` w `dl`), wygląd zachowany przez `sm:grid-cols-2` / `sm:col-span-2` |
| Scope 8. `wdrozenia.md`, `conventions.md` | Covered | |
| Scope 8. README | Missing | świadomie odłożone (Assumptions 15, niezatwierdzone zmiany innej osoby) — otwarte w `06` |
| Scope 9. Scenariusze ręczne UI w `06` | Covered | |

## Plan Deviations

- `@playwright/test` 1.64.1 i jawne `playwright-core` 1.64.1 (plan: 1.65.0) — opisane w `06`.
- Porty E2E konfigurowalne (`STONEFLOW_E2E_API_PORT`, `STONEFLOW_E2E_APP_PORT`); `reuseExistingServer: false` (plan: `!CI`).
- Teardown jako funkcja zwracana z `global-setup.ts`, bez `global-teardown.ts`.
- Link resetu szukany także w postaci JSON (`&`) — log API jest w formacie JSON.
- H-14: rok kończy się na `2026-12-10`; kwoty scenariuszy inne niż przykłady planu.
- E-6 przez formularz `/wydatki/nowy` z „Powtarzaj co miesiąc”; E-10 wybór kategorii literą.
- `SET QUOTED_IDENTIFIER ON` w `DBCC CHECKDB` i wstawianiu danych testu.
- D-6 (README) niewykonane; instrukcja E2E w `.sdd/context/conventions.md`.
- Plan F-E2 krok 7 / Rozstrzygnięcia F pkt 26 („hasło nie trafia na dysk”) nie jest spełniony w raporcie HTML — N-2.

Wszystkie poza ostatnią są odnotowane w `06-implementation-done.md` (Deviations).

## Null Safety Issues

None. Rzutowania `as string` w `app/e2e/support/ui.ts:42,82` są poprzedzone asercją adresu z
identyfikatorem; `Map.get(...) as number` w `dashboard.e2e.ts:26,36` przy braku karty daje `NaN` i
czytelnie nieudaną asercję; `Acceptance.Money(string?)` rzuca przy braku pola.

## Correctness Issues

1. **C-1 (krytyczne) — `deploy/scripts/verify-restore.sh:75-86` z `deploy/scripts/sql-common.sh:11`:
   `docker exec -i` w pętli `while read … done <<<"$filelist"` zjada pozostałe wiersze listy plików.**
   Wewnątrz pętli `sql master …` (linia 82) uruchamia `$SQLCMD`. W CI (`ci.yml:190`) i w wariancie A
   procedury (`docs/operations/kopie-i-odtworzenie.md:72`) to `docker exec -i …`, który czyta stdin
   rodzica, czyli resztę here-stringu. Pętla kończy się po pierwszym pliku (danych), a `RESTORE`
   próbuje odtworzyć plik logu w jego pierwotnej ścieżce. W teście baza źródłowa jeszcze istnieje,
   więc kończy się to błędem `Msg 1834 … cannot be overwritten. It is being used by database`
   (`deploy-dry-run` czerwony). Przy kopii z hostingu ścieżka pliku logu z innego serwera dałaby
   błąd albo kolizję. Ten sam mechanizm w `deploy/scripts/test-verify-restore.sh:88-92` (pętla
   `< <(sed …)` z `sql` w środku): test porównywał po cichu tylko pierwszą tabelę. Lokalne przebiegi
   na LocalDB (`sqlcmd` nie czyta stdin) tego nie pokazały. **Odtworzone** lokalnie wrapperem
   `sqlcmd`, który czyta stdin jak `docker exec -i` (wynik: `Msg 1834`, kod 1). **Poprawione:** `sql()`
   oraz bezpośrednie wywołania `$SQLCMD` w `verify-restore.sh` dostają `</dev/null` (komentarz z
   przyczyną w `sql-common.sh`). Po poprawce test przechodzi i z tym wrapperem, i ze zwykłym `sqlcmd`.

## Test Gaps

1. **T-1 — `deploy/scripts/test-verify-restore.sh:88-93`** nie sprawdzał, że porównano wszystkie
   wypisane tabele (przez C-1 pętla mogła skończyć się po pierwszej, a `rows > 0` przechodziło).
   **Poprawione:** dodana kontrola, że liczba porównanych tabel = liczba wierszy `Wiersze …` w wyjściu.
2. **T-2 — `app/e2e/expenses.e2e.ts:19-20`:** asercja negatywna (brak wiersza w „Operacjach” przed
   opłaceniem) czeka tylko na `app-loading-state` = 0. Jeśli lista nie zdąży zacząć ładowania, obie
   asercje przejdą od razu. Ryzyko niskie (stan ładowania ustawiany przy tworzeniu strony), do
   rozważenia: czekać na wiersz sum (`operations.totals()`) albo stan pusty. Otwarte.
3. **T-3 — zadania CI `deploy-dry-run` i `e2e` nie były uruchomione** (gałąź niewypchnięta). C-1
   pokazuje, że lokalne przebiegi na LocalDB nie zastępują wariantu z `docker exec -i`. Zielone CI na PR
   jest warunkiem merge.

## Code Smells

- `deploy/scripts/verify-restore.sh:82` — `|| fail` po kilku spacjach w tej samej linii (pozostałość
  formatowania). **Poprawione** (przeniesione do linii kontynuacji jak w reszcie skryptu) — N-4.
- `.github/workflows/ci.yml:165-175` i `228-238` — skopiowany krok „Oczekiwanie na SQL Server”;
  plan świadomie odłożył wydzielenie akcji. Bez zmian.

## Package lock

`app/package-lock.json` zawiera tylko nowe pakiety (`@axe-core/playwright`, `axe-core`,
`@playwright/test`, `playwright`, `playwright-core`, `@types/node`, `undici-types`) i zmianę flagi
`peer` przy `jiti`. Wpisy `@emnapi/core`, `@emnapi/runtime`, `@emnapi/wasi-threads` są zachowane
(linie 1065–1090). `playwright` 1.64.1 nie ma `optionalDependencies` (sprawdzone w `node_modules`),
więc brak wpisu `fsevents` dla niego jest poprawny. `npm ci` na Linuksie rozstrzygnie CI.

## Poprawki wprowadzone w przeglądzie

| # | Plik | Zmiana |
|---|---|---|
| C-1 | `deploy/scripts/sql-common.sh`, `deploy/scripts/verify-restore.sh` | `</dev/null` dla każdego wywołania `$SQLCMD` (w tym `sql()`), komentarz z przyczyną |
| T-1 | `deploy/scripts/test-verify-restore.sh` | kontrola, że porównano każdą wypisaną tabelę |
| N-3 | `docs/operations/powrot-do-wersji.md` | warunek kodu 0 dla pętli `has-pending-model-changes`, zakaz commita przy zmianach modelu |
| N-4 | `deploy/scripts/verify-restore.sh` | formatowanie linii `|| fail` |

Sprawdzenie po poprawkach (lokalnie, Windows, LocalDB):

| Command | Outcome |
|---|---|
| `dotnet build stone-flow.slnx -c Release` | Passed (0 ostrzeżeń, 0 błędów) |
| `deploy/scripts/test-verify-restore.sh` przed poprawką, `SQLCMD` = wrapper czytający stdin (symulacja `docker exec -i`) | Failed — `Msg 1834` (błąd odtworzony) |
| `deploy/scripts/test-verify-restore.sh` po poprawce, ten sam wrapper | Passed (4 przypadki, 8 tabel porównanych) |
| `deploy/scripts/test-verify-restore.sh` po poprawce, zwykły `sqlcmd` | Passed; brak baz `StoneFlowRestore*` i plików `.bak` po przebiegu |
| `deploy/scripts/test-migrate.sh` (po zmianie `sql-common.sh`) | Passed |
| `dotnet test … --filter-class …MvpFinancialScenariosTests --filter-class …LaunchDateStartupTests` | Passed (21/21) |
| `npx playwright test e2e/expenses.e2e.ts` (porty 5280/4400, z projektem `setup`) | Passed (3/3); raport sprawdzony pod kątem N-2, potem usunięty |
| CI na PR | Not run (bez push) |

## Recommendation

- Merge as-is — po poprawkach z tego przeglądu (C-1 był krytyczny i jest poprawiony), pod
  warunkami: zielone CI na PR (zwłaszcza `deploy-dry-run` i `e2e`), decyzja autora w N-1 i N-2 oraz
  **przegląd człowieka przed merge (ryzyko high)**.
