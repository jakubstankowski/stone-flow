# Code review — SF-5

## Summary

- Overall result: FAIL
- Justification: Implementacja pokrywa prawie całą specyfikację, żaden niezmiennik nie jest naruszony, a `format:check`, `lint` (z `lint:colors`), `typecheck`, `build` i `test` (85 testów) przechodzą. `npm ci` jednak nie przechodzi: `package-lock.json` z tej gałęzi nie jest zgodny z `package.json` dla npm 11.6.1, a lockfile z `main` przechodzi. To pierwszy krok CI (`.github/workflows/ci.yml:84`) i kryterium odbioru („build: `npm ci && npm run build`”), więc w obecnym stanie CI nie zadziała. Drugi problem krytyczny: `app-data-table` wyświetla w komórkach tylko tekst, a specyfikacja (sekcja „Ryzyko”) wymaga, żeby API wystarczyło listom z design guide sekcja 6 (etykiety statusów, przejście do źródła lub obiektu). Obie poprawki są niewielkie.
- Risk: medium

## Project Invariant Violations

None.

| Invariant | Status | Note |
|---|---|---|
| 1. Sprzedaż oddzielona od gotówki | Nie dotyczy | Brak danych finansowych; nawigacja ma osobne sekcje Zamówienia / Wydatki / Operacje (`app/src/app/layout/nav-items.ts:7-14`). |
| 2. Kwoty decimal na backendzie | OK | `app/src/app/shared/format/pl-format.ts:50-63` tylko formatuje; ciąg dziesiętny trafia do `Intl.NumberFormat.format` bez konwersji na `number`, brak arytmetyki. Test precyzji: `pl-format.spec.ts:18-22`. |
| 3. Dostęp wymuszany przez API | Nie dotyczy | Brak wywołań API; powłoka nie udaje logowania. |
| 4. Ponowienia bez duplikatów | Nie dotyczy | Brak integracji i zadań. Blokada podwójnego kliknięcia jest po stronie UI (`button.ts:33-40`), ale nie zastępuje ochrony w API. |
| 5. Ślad audytu finansów | Nie dotyczy | Brak zmian danych finansowych. |
| 6. Sekrety po stronie serwera | OK | W diffie nie ma adresów API, kluczy ani zmiennych środowiskowych. |
| 7. Granice modułów i warstw | OK | Zmiany tylko we frontendzie; wspólne elementy w `shared/`, sekcje w `features/{obszar}/`, komponenty bez logiki biznesowej. |
| 8. Brak danych identyfikujących | OK | Teksty i dane testowe fikcyjne; brak nazwy handlowej i domeny sklepu. |

## Conventions Violations

### Critical (must fix before merge)

1. `app/package-lock.json:4013-4078` — lockfile nie przechodzi `npm ci` (npm 11.6.1, Node 24.21.0): `npm error code EUSAGE … Missing: @emnapi/wasi-threads@1.2.3 from lock file`. Wpisy dodane razem z `@tailwindcss/oxide-wasm32-wasi` 4.3.4 (zależności dołączone w paczce, `inBundle`) npm uznaje za niezsynchronizowane. Sprawdzone w czystym katalogu: `npm ci --dry-run` z `package.json` i `package-lock.json` z `main` kończy się kodem 0, z `HEAD` kodem 1. CI uruchamia `npm ci` jako pierwszy krok (`.github/workflows/ci.yml:84`), więc całe CI frontendu się wyłoży. Konwencja „Verification” i kryterium odbioru wymagają działającego `npm ci`.
2. `app/src/app/shared/ui/data-table.ts:9-15`, `app/src/app/shared/ui/data-table.html:121` — kolumna ma tylko `value: (row: T) => string`, a komórka renderuje sam tekst. Specyfikacja (sekcja „Ryzyko”) mówi: „API komponentów wspólnych musi wystarczyć list zamówień, wydatków, operacji i historii zmian z design guide sekcja 6”. Te listy potrzebują w komórkach: `app-status-badge` (stan płatności i realizacji zamówienia, stan wydatku), linku („opis z przejściem do źródła” w Operacjach, „wpis prowadzi do obiektu” w Historii zmian) i znacznika cykliczności lub korekty. Konwencja (`.sdd/context/conventions.md:114`) każe listom używać `app-data-table`, a statusom `app-status-badge`, więc obu zasad nie da się dziś spełnić naraz. Potrzebny jest mechanizm szablonu komórki (np. `ng-template` per klucz kolumny) albo link wiersza, zanim użyje go pierwszy moduł.

### Non-Critical (should fix)

1. `app/src/app/shared/ui/field.html:7`, `app/src/styles.css:96-98`, `app/src/styles.css:44-72` — reguła `[aria-invalid='true'] { border-color: var(--sf-error) }` jest w `@layer base`, a style daisyUI `.input`/`.select`/`.textarea` są w `@layer utilities > daisyui` i ją nadpisują (sprawdzone w zbudowanym `styles-*.css`). Przy kontrolce z klasą `input` granica błędu z pkt 10 specyfikacji się nie pojawi. Do tego domyślna granica `.input` (`--input-color` = 20% `base-content`) ma na białym tle około 1,45:1, czyli poniżej 3:1 (WCAG 1.4.11; design guide: „mocniejsze obramowanie” rozpoznawalnych kontrolek, `--sf-border-strong`). Ani motyw, ani `app-field`, ani konwencja nie mapują granicy pól na `--sf-border-strong`.
2. `app/src/app/shared/ui/button.ts:5-9`, `app/src/styles.css:91-94` — daisyUI `.btn` ustawia `outline-color: var(--btn-color, …)` w wyższej warstwie niż globalny `:focus-visible`. Fokus przycisku głównego jest więc w kolorze akcentu (3,69:1 na `--sf-bg`, 4,51:1 na `--sf-surface`), a destrukcyjnego w kolorze błędu, a nie grafitowy, jak wymaga pkt 8 specyfikacji. Kontrast wystarcza, ale zachowanie odbiega od specyfikacji.
3. `app/src/app/app.config.ts:14` — `LOCALE_ID` ma wartość `'pl'`, a specyfikacja pkt 17 wymaga `'pl-PL'`.
4. `app/src/app/layout/nav-items.ts:1`, `app/src/app/app.routes.ts:4-44`, `.sdd/context/conventions.md:94` — komentarz i konwencja nazywają `nav-items.ts` „jednym źródłem dla tras i menu”, ale trasy powtarzają ścieżki i etykiety literalnie. Spójność pilnuje tylko test (`app.routes.spec.ts:28-36`). Trzeba albo budować trasy z `NAV_ITEMS`, albo poprawić opis.
5. `app/src/app/features/*/…-page.html:3` (np. `orders-page.html:3`), `app/src/app/shared/ui/empty-state.ts:17`, `app/src/app/shared/ui/confirm-dialog.ts:17` — wejście nazwane `title` przekazane statycznie (`title="…"`) zostaje też atrybutem DOM hosta (widać w skompilowanym `consts: [[\`title\`, …]]`). Daje to natywny dymek nad całym stanem pustki, a w dialogu nad treścią dialogu, który jest potomkiem hosta. Lepiej zmienić nazwę wejścia, np. `heading`.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| 1. Motyw `stone-flow`, `data-theme`, brak literałów kolorów | Covered | `index.html:2`, `styles.css:11-73`; `lint:colors` przechodzi (36 plików). |
| 2. `/` i nieznane → `/pulpit`; usunięcie strony startowej | Covered | `app.routes.ts:5,45`; `features/start` usunięte. |
| 3. Skip link, `header`, `nav`, `main#tresc`, 1600 px, marginesy | Covered | `app-shell.html`; `px-4 lg:px-8`. |
| 4. Sześć pozycji w kolejności, `loadComponent` | Covered | `nav-items.ts`, `app.routes.ts`. |
| 5. `aria-current`, tytuł „{Sekcja} · Stone Flow”, nazwa sekcji w nagłówku | Covered | `main-nav.html:33-34`, `page-title-strategy.ts`, `app-shell.ts:26-32`. |
| 6. Nawigacja pozioma ≥ 1024 px, menu „Menu” < 1024 px | Partial | Od 1024 px nawigacja stoi w tym samym wierszu co nazwa produktu (`main-nav.html:25` `lg:basis-auto`), a nie „pod nagłówkiem”. Wybór aktywnej już pozycji nie zwija menu (patrz Correctness 1). |
| 7. Zaślepki z `<h1>` i stanem pustki; 320 px | Covered | Szerokości 320 px nie sprawdziłem ręcznie w tym przeglądzie. |
| 8. Widoczny fokus grafitowy, `prefers-reduced-motion` | Partial | Przyciski daisyUI mają obwódkę w kolorze wariantu (Non-Critical 2). |
| 9. Przycisk: warianty, `busy`, `aria-busy`, 44 px, promień | Partial | Zrealizowany jako komponent z wewnętrznym `<button>` (decyzja planu); brak wariantu dla `<a>` (spec: „dyrektywa na natywnym `<button>`/`<a>`”). |
| 10. Pole formularza | Partial | Atrybuty ARIA działają; granica `--sf-error` nie działa na kontrolkach daisyUI (Non-Critical 1). |
| 11. Dialog potwierdzenia | Covered | Fokus na „Anuluj”, Escape → `false`, zwrot fokusu, fallback `#tresc`. Pułapkę fokusu daje natywny `showModal()`; w przeglądarce jej nie sprawdzałem. |
| 12. Tabela danych | Partial | Wejścia, wyjścia, stany, `aria-sort`, paginacja i region przewijania są. API komórek nie wystarcza listom z design guide sekcja 6 (Critical 2). |
| 13. Etykieta statusu | Covered | `status-badge.ts`. |
| 14–16. Stany ładowania, pustki, błędu | Covered | `loading-state.ts`, `empty-state.ts`, `error-state.ts`. |
| 17. Formatowanie po polsku | Covered | Przykłady z „Przypadki brzegowe” przechodzą (U+00A0, DST). `LOCALE_ID` `'pl'` zamiast `'pl-PL'` (Non-Critical 3). |
| Aktualizacja `conventions.md` i `role.md` | Covered | TODO wersji w `role.md` zamknięte. |
| `npm ci`, `format:check`, `lint`, `typecheck`, `build`, `test` | Partial | `npm ci` FAIL (Critical 1); pozostałe PASS. |

## Plan Deviations

- `app/src/app/shared/no-color-literals.spec.ts` (plan, sekcja 3 i 8) nie powstał. Zastępuje go `app/scripts/check-color-literals.mjs`, uruchamiany przez `npm run lint` (`lint:colors`, `app/package.json:10,13`). Kontrola działa i obejmuje ją CI (`ci.yml:86`), ale nie jest testem w `npm test`. Wzorzec obejmuje dodatkowo `oklch(`/`oklab(` i pomija `*.spec.ts`.
- `app/src/app/features/section-pages.spec.ts` (plan) nie powstał. Zaślepki są sprawdzane w `app/src/app/app.routes.spec.ts:38-47` (`<h1>` i tekst stanu pustki).
- `app/src/app/shared/ui/status-badge.spec.ts` (plan) nie powstał. Testy etykiety statusu są w `app/src/app/shared/ui/states.spec.ts:64-76`.
- `DataTableColumn` ma wymagane `value: (row: T) => string` zamiast opcjonalnego `cell?` z planu (sekcja 5).
- `app.config.ts:14`: `LOCALE_ID` `'pl'` (plan nie podaje wartości, specyfikacja wymaga `'pl-PL'`).
- Gałąź zawiera też `docs/tasks/sf-4/00-raw-task.md` i `docs/tasks/sf-4/task.md` (commit `616003a`), których nie ma w planie SF-5.

## Null Safety Issues

1. `app/src/app/shared/ui/data-table.ts:66-77` — `pageSize` ≤ 0 nie jest obsłużony: przy `pageSize = 0` zakres to „1–0 z {total}”, a `hasNext` jest zawsze `true`. Wejście pochodzi od modułu, więc ryzyko jest niskie, ale przydałaby się normalizacja (np. `Math.max(1, …)`, jak w `loading-state.ts:18`).

## Correctness Issues

1. `app/src/app/layout/main-nav.ts:24-29` — menu zwija się tylko po `NavigationEnd`. Wybór pozycji, która jest już aktywna (ten sam URL, `onSameUrlNavigation: 'ignore'`), nie wywołuje nawigacji, więc menu zostaje otwarte, wbrew pkt 6 („Wybór pozycji … zwija menu”). Po nawigacji z menu mobilnego fokus zostaje na linku, który właśnie się ukrył (`display: none`), więc trafia na `body`. Nie ma przeniesienia fokusu na `main#tresc` ani `<h1>`.
2. `app/src/app/shared/format/pl-format.ts:82-88` — `formatDateTime` przyjmuje znacznik bez strefy (np. `2026-10-24T22:30:00`), a `new Date()` interpretuje go jako czas lokalny przeglądarki. W Warszawie wynik to `24.10.2026, 22:30` zamiast `25.10.2026, 00:30` (sprawdzone w Node). Domyślna serializacja `DateTime` o `Kind = Unspecified` w .NET (np. odczyt `datetime2` przez EF Core) nie dodaje `Z`. Funkcja powinna wymagać `Z` lub przesunięcia (i inaczej zwracać „Brak danych”) albo jawnie traktować brak strefy jako UTC.
3. `app/src/app/shared/format/pl-format.ts:54-62` — `-0`, `"-0.00"` i `"-0.001"` dają `-0,00 zł` (sprawdzone). Zero powinno wyglądać jak `0,00 zł` (design guide sekcja 7). Wystarczy `signDisplay: 'negative'`.
4. `app/src/app/shared/ui/confirm-dialog.html:1`, `app/src/app/shared/ui/confirm-dialog.ts:29-40` — brak obsługi zdarzenia `close`. Gdy dialog zamknie się inną drogą niż `finish()` (np. zamknięcie przez przeglądarkę albo zniszczenie komponentu z otwartym dialogiem), obietnica z `open()` nigdy się nie rozstrzyga.
5. `app/src/app/shared/ui/data-table.html:131-141` — paginacja jest zawsze renderowana jako landmark `nav aria-label="Paginacja"`, także w stanie ładowania, błędu i pustej ewidencji („0 z 0”). Dwie tabele na jednej stronie dadzą dwa landmarki o tej samej nazwie. Lepiej ukryć paginację przy `total = 0` albo powiązać jej nazwę z `caption`.
6. `docs/tasks/sf-5/04-implementation-plan.md` (tabela kontrastów, wiersz „`--sf-accent` / `--sf-surface-soft`”) — plan podaje 4,2:1, obliczony kontrast to 4,04:1. Wymóg 3:1 nadal jest spełniony, ale tabela jest nieścisła.

Kontrasty par użytych w kodzie, a nieobecnych w tabeli planu (wzór WCAG): `--sf-error`/`--sf-bg` 5,55; `--sf-accent-text`/`--sf-bg` („Wyczyść filtry”) 5,61; `--sf-error`/`--sf-surface-soft` 6,08; `--sf-muted`/`--sf-surface-soft` (wskaźnik sortowania) 5,44; `--sf-border-strong`/`--sf-surface-soft` 3,87; obwódka `--sf-accent`/`--sf-bg` 3,69. Wszystkie spełniają AA. Granica kontenera tabeli `--sf-secondary`/`--sf-surface` (2,51) i nagłówka (2,05) jest dekoracyjna, więc to dopuszczalne.

## Test Gaps

1. `app/src/app/shared/ui/button.spec.ts:51-66` — test blokady `busy` nie ma kontroli pozytywnej, czyli sprawdzenia, że bez `busy` przycisk `submit` wysyła formularz (`submitted === 1`). Bez niej `submitted === 0` mogłoby przejść także wtedy, gdy jsdom w ogóle nie wysyła formularza. Linia 65 sprawdza `toContain('Zapisywanie…')`, ale nie sprawdza, że etykieta „Zapisz” jest ukryta.
2. `app/src/app/shared/format/pl-format.spec.ts:58-72` — brak przypadków znacznika bez strefy (Correctness 2), ułamków sekund z .NET (7 cyfr) i ujemnego zera (Correctness 3).
3. `app/src/app/shared/ui/field.spec.ts` — brak testu z kontrolką ze stylem daisyUI (`class="input"`), który wykryłby Non-Critical 1. W jsdom tego się nie sprawdzi, więc potrzebna jest pozycja w weryfikacji ręcznej.
4. `app/src/app/layout/main-nav.spec.ts:90` — brak testu wyboru aktywnej już pozycji w menu mobilnym.
5. `app/scripts/check-color-literals.mjs` — skrypt nie ma testu (np. pliku z literałem, który musi go wyłożyć). Działanie potwierdza tylko wynik na obecnym kodzie.
6. Pułapki fokusu w dialogu, układu 320 px i Tab przez nawigację nie da się sprawdzić w jsdom. Plan przewiduje weryfikację ręczną, a w repozytorium nie ma śladu, że ją wykonano. W tym przeglądzie też jej nie wykonałem.

## Code Smells

1. `app/src/app/app.routes.ts:4-44` — ścieżki i etykiety powtórzone względem `NAV_ITEMS` (Non-Critical 4).
2. `app/src/app/features/*/…-page.ts` — sześć identycznych komponentów zaślepek. To akceptowalne jako tymczasowe, bo każda sekcja dostanie własną treść.
3. `app/src/app/shared/ui/data-table.html:4-12`, `app/src/app/layout/main-nav.html:1-8` — przyciski drugorzędne budowane ręcznie (`btn … border-(--sf-border-strong) bg-base-100`) zamiast `app-button variant="secondary"`, które powielają klasy z `button.ts:7`. Wynika to z potrzeby `aria-expanded`/`aria-controls` i `#menuButton`, których `app-button` nie przekazuje.

## Recommendation

- Fix critical issues before merge. Wygenerować lockfile tak, żeby `npm ci` przechodził na npm 11.6.1 (i potwierdzić to w CI). Dodać do `app-data-table` szablon komórki lub link wiersza, wystarczający dla statusów i przejść z design guide sekcja 6. Pozostałe uwagi poprawić w tej zmianie albo jawnie przenieść do SF-6/SF-8.

Weryfikacja wykonana w `app/` (03.11.2026, npm 11.6.1, Node 24.21.0):

| Polecenie | Wynik |
|---|---|
| `npm ci` | FAIL — `EUSAGE`, `Missing: @emnapi/wasi-threads@1.2.3 from lock file` (lockfile z `main`: PASS w `--dry-run`) |
| `npm run format:check` | PASS |
| `npm run lint` (z `lint:colors`) | PASS — „All files pass linting.”, „Brak lokalnych kodów kolorów w 36 plikach src/app.” |
| `npm run typecheck` | PASS |
| `npm run build` | PASS — sześć leniwych chunków sekcji |
| `npm test -- --watch=false` | PASS — 12 plików, 85 testów |
