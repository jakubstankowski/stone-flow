# Code review — SF-5

Przegląd ponowny po poprawkach z commita `ba90f60` („fix(app): poprawki po przeglądzie kodu SF-5”). Zakres: `git diff --name-only main...HEAD` (73 pliki), każdy zmieniony plik źródłowy przeczytany w całości.

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Oba problemy krytyczne z poprzedniego przeglądu są usunięte. `npm ci --ignore-scripts` na czystym katalogu z `package.json`, `package-lock.json` i `.npmrc` z gałęzi kończy się kodem 0 (npm 11.6.1, Node 24.21.0), a `npm install --package-lock-only` nie zmienia lockfile. `app-data-table` ma teraz szablony komórek `appTableCell` z typowanym `row`, więc listy z design guide sekcja 6 mogą pokazać statusy, linki i znaczniki. Wszystkie polecenia weryfikacji przechodzą (13 plików, 93 testy). Żaden niezmiennik nie jest naruszony. Zostały drobne uwagi: ujemne kwoty zaokrąglane do zera (`-0.001`) nadal dają `-0,00 zł`, obietnica dialogu wisi po zniszczeniu komponentu z otwartym dialogiem, a nawigacja od 1024 px stoi w wierszu nagłówka zamiast pod nim.
- Risk: medium

## Project Invariant Violations

None.

| Invariant | Status | Note |
|---|---|---|
| 1. Sprzedaż oddzielona od gotówki | Nie dotyczy | Brak danych finansowych. Nawigacja ma osobne sekcje Zamówienia, Wydatki i Operacje (`app/src/app/layout/nav-items.ts:7-14`). |
| 2. Kwoty decimal na backendzie | OK | `app/src/app/shared/format/pl-format.ts:50-69` tylko formatuje. Ciąg dziesiętny trafia do `Intl.NumberFormat.format` bez konwersji na `number`. `Number(trimmed) === 0` (`:64`) to porównanie, a nie arytmetyka na kwocie. Test precyzji: `pl-format.spec.ts:18-22`. |
| 3. Dostęp wymuszany przez API | Nie dotyczy | Brak wywołań API, a powłoka nie udaje logowania. |
| 4. Ponowienia bez duplikatów | Nie dotyczy | Brak integracji i zadań. Blokada `busy` (`button.ts:33-40`) chroni tylko UI. |
| 5. Ślad audytu finansów | Nie dotyczy | Brak zmian danych finansowych. |
| 6. Sekrety po stronie serwera | OK | W diffie nie ma adresów API, kluczy ani zmiennych środowiskowych. Lockfile wskazuje tylko `registry.npmjs.org`. |
| 7. Granice modułów i warstw | OK | Zmiany tylko w `app/`. Wspólne elementy leżą w `shared/`, sekcje w `features/{obszar}/`, a komponenty nie mają logiki biznesowej. |
| 8. Brak danych identyfikujących | OK | Teksty i dane testowe są fikcyjne (`ZAM-7`, „Usunąć wydatek?”). Brak nazwy handlowej i domeny sklepu. |

## Conventions Violations

### Critical (must fix before merge)

None.

### Non-Critical (should fix)

1. `app/src/app/shared/format/pl-format.ts:13-19`, `:58`, `:64-68` — poprawka ujemnego zera obejmuje tylko wartości równe zeru. `formatPln('-0.001')`, `formatPln(-0.001)` i `formatPln('-0.004')` nadal zwracają `-0,00 zł`. Sprawdziłem to w Node z tymi samymi opcjami `Intl.NumberFormat`. Design guide sekcja 7 i konwencja („Amounts and dates are displayed only through `shared/format/`”) oczekują `0,00 zł`. Wystarczy `signDisplay: 'negative'` w `amountFormat`, a wtedy specjalne gałęzie `:58` i `:64-66` przestają być potrzebne.
2. `app/src/app/layout/main-nav.html:25`, `app/src/app/layout/app-shell.html:10` — od 1024 px nawigacja (`lg:basis-auto`) stoi w jednym wierszu z nazwą produktu w kontenerze `flex-wrap`. Specyfikacja pkt 6 mówi: „Od 1024 px nawigacja jest pozioma pod nagłówkiem”. To uwaga z poprzedniego przeglądu, nadal bez zmian. Trzeba poprawić układ albo zapisać odstępstwo w specyfikacji.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| 1. Motyw `stone-flow`, `data-theme`, brak literałów kolorów | Covered | `index.html:2`, `styles.css:11-73`. `lint:colors` przechodzi (37 plików). |
| 2. `/` i nieznane → `/pulpit`; usunięcie strony startowej | Covered | `app.routes.ts:17,32`. `features/start` usunięte. |
| 3. Skip link, `header`, `nav`, `main#tresc`, 1600 px, marginesy | Covered | `app-shell.html:1-29`. |
| 4. Sześć pozycji w kolejności, `loadComponent` | Covered | `nav-items.ts:7-14`. Trasy budowane z `NAV_ITEMS` (`app.routes.ts:8-14`). Build daje sześć leniwych chunków. |
| 5. `aria-current`, tytuł „{Sekcja} · Stone Flow”, nazwa sekcji w nagłówku | Covered | `main-nav.html:34`, `page-title-strategy.ts:11-14`, `app-shell.ts:26-32`. |
| 6. Nawigacja pozioma ≥ 1024 px, menu „Menu” < 1024 px | Partial | Wybór pozycji, także bieżącej, zwija teraz menu i przenosi fokus do `#tresc` (`main-nav.ts:40-46`). Od 1024 px nawigacja stoi w wierszu nagłówka, a nie pod nim (Non-Critical 2). |
| 7. Zaślepki z `<h1>` i stanem pustki; 320 px | Covered | `features/*/…-page.html`, test `app.routes.spec.ts:38-47`. Szerokości 320 px nie sprawdzałem w przeglądarce. |
| 8. Widoczny fokus grafitowy, `prefers-reduced-motion` | Covered | `styles.css:111-114` jest poza warstwami, więc wygrywa z `.btn` daisyUI (sprawdzone w zbudowanym `styles-*.css`). Reduced motion: `styles.css:95-104`. |
| 9. Przycisk: warianty, `busy`, `aria-busy`, 44 px, promień | Partial | Bez zmian względem poprzedniego przeglądu: komponent z wewnętrznym `<button>` (decyzja planu, uwaga 9), bez wariantu dla `<a>`. |
| 10. Pole formularza | Covered | Granice `.input/.select/.textarea` używają `--sf-border-strong` (`styles.css:116-120`, 3,54:1 na `--sf-bg`). `[aria-invalid='true']` → `--sf-error` (`:122-124`). Obie reguły są poza warstwami, a reguła błędu stoi później, więc wygrywa przy tej samej specyficzności. |
| 11. Dialog potwierdzenia | Covered | Nowa obsługa zdarzenia `close` (`confirm-dialog.html:6`, `confirm-dialog.ts:48-52`) z testem. Pułapka fokusu to natywny `showModal()`; w przeglądarce jej nie sprawdzałem. |
| 12. Tabela danych | Covered | Szablony komórek `appTableCell` (`table-cell.ts`, `data-table.ts:75-82`, `data-table.html:121-125`) wystarczają na `app-status-badge`, linki „przejście do źródła/obiektu” i znaczniki cykliczności z design guide sekcja 6. Test: `data-table-cells.spec.ts:49-60`. |
| 13. Etykieta statusu | Covered | `status-badge.ts`. |
| 14–16. Stany ładowania, pustki, błędu | Covered | Wejście `heading` zamiast `title` (`empty-state.ts:17`). |
| 17. Formatowanie po polsku | Covered | `LOCALE_ID` `'pl-PL'` (`app.config.ts:8,14`). Znacznik bez strefy jest traktowany jako UTC (`pl-format.ts:95-96`). Przypadki z „Przypadki brzegowe” przechodzą. Resztka ujemnego zera: Non-Critical 1. |
| Aktualizacja `conventions.md` i `role.md` | Covered | `conventions.md:114-119` opisuje `appTableCell` i zasadę `heading`. `role.md:23-24` podaje wersje. Treść zgadza się z kodem. |
| `npm ci`, `format:check`, `lint`, `typecheck`, `build`, `test` | Covered | Wszystkie przechodzą (tabela w „Recommendation”). |

## Plan Deviations

- `app/src/app/shared/no-color-literals.spec.ts` nie powstał. Zastępuje go `app/scripts/check-color-literals.mjs`, uruchamiany w `npm run lint` (`package.json:10,14`). Bez zmian od poprzedniego przeglądu.
- `app/src/app/features/section-pages.spec.ts` i `app/src/app/shared/ui/status-badge.spec.ts` nie powstały. Ich zakres pokrywają `app.routes.spec.ts:38-47` i `states.spec.ts`. Bez zmian.
- `DataTableColumn` ma opcjonalne `value?` (`data-table.ts:23`) i osobną dyrektywę `TableCell` (`table-cell.ts`, nowy plik spoza planu) zamiast `cell?: (row: T) => string` z planu (sekcja 5). Mechanizm daje więcej niż plan, bo komórka może być dowolną treścią.
- Nowe pliki spoza planu: `app/src/app/shared/ui/table-cell.ts`, `app/src/app/shared/ui/data-table-cells.spec.ts`. `app/tsconfig.json:16` dostał `lib: ["ES2023", …]` dla typu `` `${number}` `` w `Intl.NumberFormat.format`.
- Gałąź nadal zawiera `docs/tasks/sf-4/00-raw-task.md` i `docs/tasks/sf-4/task.md` (commit `616003a`), których nie ma w planie SF-5.
- `docs/tasks/sf-5/04-implementation-plan.md:184` nadal podaje 4,2:1 dla `--sf-accent`/`--sf-surface-soft`, a obliczony kontrast to 4,04:1 (wymóg 3:1 spełniony).

### Status uwag z poprzedniego przeglądu

| Uwaga | Status | Dowód |
|---|---|---|
| Critical 1 — `npm ci` nie przechodzi (lockfile) | Naprawione | `app/package-lock.json:1044-1054` (`@emnapi/wasi-threads` na najwyższym poziomie), usunięte wpisy `inBundle` `@emnapi/core`/`runtime`. `npm ci --ignore-scripts` w czystym katalogu: kod 0. `npm install --package-lock-only` nie zmienia pliku. `npm audit`: 18 umiarkowanych, tyle samo co na `main`. |
| Critical 2 — komórki tabeli tylko tekstowe | Naprawione | `table-cell.ts:14-27`, `data-table.html:121-125`, test z linkiem i `app-status-badge`. |
| Non-Critical 1 — granica pól i błędu przegrywa z daisyUI | Naprawione | `styles.css:116-124` poza warstwami. |
| Non-Critical 2 — fokus przycisków w kolorze wariantu | Naprawione | `styles.css:111-114` poza warstwami. |
| Non-Critical 3 — `LOCALE_ID` `'pl'` | Naprawione | `app.config.ts:8,14`. |
| Non-Critical 4 — trasy powtarzają `NAV_ITEMS` | Naprawione | `app.routes.ts:8-14`. |
| Non-Critical 5 — wejście `title` daje natywny dymek | Naprawione | `heading` w `empty-state.ts:17`, `confirm-dialog.ts:17`, zaślepkach i tabeli. |
| Null safety 1 — `pageSize` ≤ 0 | Naprawione | `data-table.ts:87`, test `data-table-cells.spec.ts:62-70`. |
| Correctness 1 — menu nie zwija się przy bieżącej pozycji, fokus ginie | Naprawione | `main-nav.ts:40-46`, `main-nav.html:36`, test `main-nav.spec.ts:111-129`. |
| Correctness 2 — znacznik bez strefy jako czas lokalny | Naprawione | `pl-format.ts:95-96`, testy z 7 cyframi ułamka. |
| Correctness 3 — `-0,00 zł` | Częściowo | `-0` i `"-0.00"` naprawione. `"-0.001"`, `-0.001` i `"-0.004"` nadal dają `-0,00 zł` (Non-Critical 1). |
| Correctness 4 — obietnica dialogu bez rozstrzygnięcia | Częściowo | Zamknięcie przez `close` jest obsłużone. Zniszczenie komponentu z otwartym dialogiem nadal zostawia obietnicę bez rozstrzygnięcia (Correctness 1). |
| Correctness 5 — zduplikowane landmarki „Paginacja” | Naprawione | `data-table.html:135-138` (`'Paginacja: ' + caption()`). |
| Correctness 6 — kontrast 4,2 w planie | Nie naprawione | `04-implementation-plan.md:184`. Błąd tylko w dokumentacji. |
| Specyfikacja pkt 6 — nawigacja „pod nagłówkiem” | Nie naprawione | Non-Critical 2. |
| Test Gaps 1–6 | Częściowo | Patrz „Test Gaps”. |

## Null Safety Issues

None.

## Correctness Issues

1. `app/src/app/shared/ui/confirm-dialog.ts:16-77` — komponent nie ma `DestroyRef`/`ngOnDestroy`. Usunięcie otwartego `<dialog>` z DOM (np. zmiana trasy, gdy dialog czeka na odpowiedź) nie wywołuje zdarzenia `close`, więc obietnica z `open()` się nie rozstrzyga, a `await dialog.open()` w module nigdy się nie kończy. Wystarczy przy zniszczeniu rozstrzygnąć oczekujący wynik jako `false`.
2. `app/src/app/shared/ui/confirm-dialog.ts:30-35` — drugie wywołanie `open()` przy otwartym dialogu rozstrzyga poprzednią obietnicę, ale nie czyści `resolveResult` przed `showModal()`. Nadpisuje też `returnFocusTo` elementem wewnątrz dialogu (przycisk „Anuluj”), więc po zamknięciu fokus nie wróci do pierwotnego wywołującego. Przypadek mało prawdopodobny. Wystarczy zignorować `open()`, gdy `dialog.open === true`, albo zachować pierwotny cel fokusu.
3. `app/src/app/shared/ui/data-table.html:121-125`, `data-table.ts:23` — kolumna bez `value` i bez pasującego szablonu `appTableCell` (np. literówka w kluczu) renderuje pustą komórkę bez żadnego sygnału. Test `data-table-cells.spec.ts:42,59` utrwala to zachowanie. Puste pole jest nieodróżnialne od „brak danych”, a design guide rozróżnia zero, brak danych i dane niepełne. Lepiej, żeby błędna konfiguracja kolumny była widoczna, np. przez `console.error` w trybie deweloperskim albo wymagane `value` przy braku szablonu.

## Test Gaps

1. `app/src/app/shared/format/pl-format.spec.ts:74-78` — brak przypadków `"-0.001"` i `-0.001` (Non-Critical 1).
2. `app/src/app/shared/ui/button.spec.ts` — nowy test `:89-103` sprawdza kliknięcie po zdjęciu `busy`, ale nadal nie ma kontroli pozytywnej, że bez `busy` przycisk `submit` wysyła formularz.
3. `app/src/app/shared/ui/confirm-dialog.spec.ts` — brak testu zniszczenia hosta z otwartym dialogiem (Correctness 1).
4. `app/scripts/check-color-literals.mjs` — nadal bez testu na pliku z literałem.
5. Weryfikacja ręczna z planu (sekcja 9: Tab przez nawigację, 320/375 px, pułapka fokusu dialogu, grafitowy fokus i granice `.input` w przeglądarce) — w repozytorium nie ma śladu jej wykonania. W tym przeglądzie też jej nie wykonałem; poprawki CSS sprawdziłem tylko w zbudowanym arkuszu.

## Code Smells

1. `app/src/styles.css:111-124` — reguły poza warstwami wygrywają z każdą klasą Tailwind (`@layer utilities`). Moduł nie nadpisze więc granicy `.input` ani obwódki fokusu utility (np. `border-(--sf-success)`, `focus-visible:outline-*`). Na dziś to zamierzone, ale konwencja (`conventions.md:132-143`) o tym nie mówi. Warto dopisać jedno zdanie, zanim ktoś zacznie szukać, dlaczego utility nie działa. Komentarz `styles.css:108` mówi o `@layer components`, a daisyUI w zbudowanym CSS siedzi w `@layer utilities > daisyui`. Wniosek się nie zmienia, ale opis jest nieścisły.
2. `app/src/app/shared/ui/table-cell.ts:21-26` — `ngTemplateContextGuard` zwraca `directive !== null && context !== null` zamiast `true`. Warunek nie wnosi nic do typowania, a czytelnik może pomyśleć, że ma znaczenie w czasie działania.
3. `app/src/app/shared/ui/data-table.html:4-12`, `app/src/app/layout/main-nav.html:1-8` — przyciski drugorzędne budowane ręcznie zamiast `app-button`. Bez zmian, uzasadnione potrzebą `aria-expanded`/`aria-controls`.
4. `app/src/app/features/*/…-page.ts` — sześć identycznych zaślepek. Akceptowalne jako tymczasowe.

## Recommendation

- Merge as-is. Nie ma problemów krytycznych ani naruszeń niezmienników. Non-Critical 1–2 i Correctness 1 warto poprawić w tej zmianie (każda poprawka ma kilka linii) albo jawnie przenieść do SF-8, czyli pierwszego zadania z listą i dialogiem. Weryfikację ręczną z planu (sekcja 9) trzeba wykonać przed zamknięciem zadania (`sdd-done`).

Weryfikacja wykonana 03.11.2026 (npm 11.6.1, Node 24.21.0):

| Polecenie | Wynik |
|---|---|
| `npm ci --ignore-scripts` w czystym katalogu tymczasowym (`package.json`, `package-lock.json`, `.npmrc` z gałęzi) | PASS — kod 0. `npm audit`: 18 umiarkowanych, tyle samo co na `main` |
| `npm install --package-lock-only --ignore-scripts` (tamże) | PASS — lockfile bez zmian |
| `npm run format:check` | PASS — „All matched files use Prettier code style!” |
| `npm run lint` (z `lint:colors`) | PASS — „All files pass linting.”, „Brak lokalnych kodów kolorów w 37 plikach src/app.” |
| `npm run typecheck` | PASS |
| `npm run build` | PASS — sześć leniwych chunków sekcji. W `styles-*.css` reguły `:focus-visible`, `.input,.select,.textarea` i `[aria-invalid=true]` są poza warstwami |
| `npm test -- --watch=false` | PASS — 13 plików, 93 testy |

`npm ci` nie był uruchamiany w `app/`, bo działający serwer deweloperski blokuje pliki natywne na Windows.
