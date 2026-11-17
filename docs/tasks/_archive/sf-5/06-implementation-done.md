# Implementation done — SF-5

## Summary

Na gałęzi `sf-5-theme-shell` (commity `616003a`, `983eadc`, `ba90f60`) dodano do aplikacji
`app/`:

- Tailwind CSS 4.3.4 (`@tailwindcss/postcss`, PostCSS 8.5.32) i daisyUI 5.7.50;
- tokeny `--sf-*` i jeden jasny motyw daisyUI `stone-flow` w `app/src/styles.css`;
- powłokę aplikacji (`layout/`): link „Przejdź do treści”, nagłówek z nazwą produktu i
  sekcji, nawigację poziomą od 1024 px i menu „Menu” poniżej;
- sześć leniwie ładowanych sekcji-zaślepek (`features/`);
- wspólne elementy UI (`shared/ui/`): przycisk, pole, dialog potwierdzenia, tabela danych z
  szablonami komórek, etykieta statusu, stany ładowania, pustki i błędu;
- formatowanie po polsku (`shared/format/`);
- skrypt `lint:colors`;
- aktualizację `conventions.md` i `role.md`.

Strona startowa z SF-3 została usunięta. Przegląd kodu: pierwszy FAIL (lockfile, komórki
tabeli), po poprawkach PASS WITH ISSUES.

Proces wykonano autonomicznie na polecenie właściciela (03.11.2026). Rozstrzygnięcia pytań
doprecyzowujących z `02-spec.md` („Założenia”) czekają na potwierdzenie właściciela.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Tailwind CSS i daisyUI w dokładnych wersjach | Implemented | `app/package.json`, `app/.postcssrc.json` |
| Tokeny `--sf-*` (paleta obowiązkowa + uzupełniające) centralnie, jeden motyw daisyUI | Implemented | `app/src/styles.css`; `data-theme="stone-flow"` w `index.html` |
| Brak literałów kolorów w `app/src/app/**` | Implemented | `npm run lint:colors` (37 plików) |
| Pary kolorów WCAG 2.2 AA, status nie tylko kolorem | Implemented | tabela kontrastów w planie; `app-status-badge` zawsze z tekstem |
| `/` i nieznane ścieżki → `/pulpit` | Implemented | |
| Powłoka: link pomijający, `header`, `nav aria-label="Główna"`, `main#tresc tabindex="-1"`, max 1600 px | Implemented | |
| Nawigacja: sześć pozycji, `aria-current`, leniwe trasy, tytuł „{Sekcja} · Stone Flow” | Implemented | |
| Menu mobilne < 1024 px z `aria-expanded`, Escape, 44 px | Implemented | Od 1024 px nawigacja stoi w wierszu nagłówka, nie pod nim (patrz Deviations). |
| Zaślepki sekcji z `<h1>` i stanem pustki | Implemented | |
| Widoczny fokus (2 px grafit, odstęp 2 px), `prefers-reduced-motion` | Implemented | |
| Przycisk: warianty, `busy` bez ponownego kliknięcia i wysłania | Implemented | `app-button` |
| Pole: etykieta, „(wymagane)”, `aria-describedby`, `aria-invalid`, granica błędu | Implemented | `app-field` |
| Dialog potwierdzenia: fokus na „Anuluj”, Escape, zwrot fokusu | Implemented | `app-confirm-dialog`; pułapka fokusu natywnego `<dialog>` niesprawdzona w przeglądarce |
| Tabela: filtry, aktywne filtry, „Wyczyść filtry”, sortowanie z `aria-sort`, paginacja, stany, przewijanie w kontenerze | Implemented | `app-data-table`, plus szablony komórek `appTableCell` |
| Etykieta statusu, stany ładowania, pustki, błędu | Implemented | |
| Formaty kwot, dat, miesięcy i czasu Europe/Warsaw, „Brak danych” | Implemented | `-0.001` nadal daje `-0,00 zł` (Open Items) |
| Aktualizacja `conventions.md` i `role.md` | Implemented | |

## Files

### Created

- `app/.postcssrc.json`
- `app/scripts/check-color-literals.mjs`
- `app/src/test-setup.ts`
- `app/src/app/layout/app-shell.ts`, `app-shell.html`, `app-shell.spec.ts`
- `app/src/app/layout/main-nav.ts`, `main-nav.html`, `main-nav.spec.ts`
- `app/src/app/layout/nav-items.ts`
- `app/src/app/layout/page-title-strategy.ts`, `page-title-strategy.spec.ts`
- `app/src/app/features/{dashboard,orders,expenses,operations,change-history,integration}/{obszar}-page.ts`, `.html` (12 plików)
- `app/src/app/shared/format/pl-format.ts`, `pl-format.spec.ts`
- `app/src/app/shared/format/pl-format-pipes.ts`, `pl-format-pipes.spec.ts`
- `app/src/app/shared/ui/button.ts`, `button.html`, `button.spec.ts`
- `app/src/app/shared/ui/field.ts`, `field.html`, `field.spec.ts`
- `app/src/app/shared/ui/confirm-dialog.ts`, `confirm-dialog.html`, `confirm-dialog.spec.ts`
- `app/src/app/shared/ui/data-table.ts`, `data-table.html`, `data-table.spec.ts`
- `app/src/app/shared/ui/data-table-cells.spec.ts`
- `app/src/app/shared/ui/table-cell.ts`
- `app/src/app/shared/ui/status-badge.ts`
- `app/src/app/shared/ui/loading-state.ts`, `empty-state.ts`, `error-state.ts`, `states.spec.ts`
- `docs/tasks/sf-5/00-raw-task.md`, `02-spec.md`, `03-spec-review.md`,
  `04-implementation-plan.md`, `05-code-review.md`, `06-implementation-done.md`, `task.md`
- `docs/tasks/sf-4/00-raw-task.md`, `task.md` (pobranie SF-4, osobne zadanie)

### Modified

- `app/package.json`, `app/package-lock.json`
- `app/angular.json` (`setupFiles` testów), `app/tsconfig.json` (`lib` ES2023),
  `app/tsconfig.spec.json`
- `app/src/index.html`, `app/src/styles.css`
- `app/src/app/app.ts`, `app.html`, `app.config.ts`, `app.routes.ts`, `app.routes.spec.ts`,
  `app.spec.ts`
- `.sdd/context/conventions.md`, `.sdd/context/role.md`

Usunięte: `app/src/app/features/start/start-page.ts`, `start-page.html`, `start-page.spec.ts`.

## Planned Units

| Item from the plan | Status |
|---|---|
| Zależności Tailwind CSS, `@tailwindcss/postcss`, PostCSS, daisyUI | Exists |
| `.postcssrc.json` | Exists |
| Tokeny i motyw `stone-flow` w `styles.css`; `data-theme` w `index.html` | Exists |
| `test-setup.ts` (stub `<dialog>`) | Exists |
| `app.config.ts` — locale `pl-PL`, `PageTitleStrategy` | Exists |
| `app.ts`/`app.html` — `<app-shell />` | Exists |
| `app.routes.ts` — sześć tras + przekierowania | Exists |
| Usunięcie `features/start` | Exists (usunięte) |
| `layout/app-shell` | Exists |
| `layout/main-nav` | Exists |
| `layout/nav-items.ts` | Exists |
| `layout/page-title-strategy` | Exists |
| Zaślepki sześciu sekcji w `features/` | Exists |
| `features/section-pages.spec.ts` | Missing — pokryte w `app.routes.spec.ts` |
| `shared/ui/button` | Exists |
| `shared/ui/field` | Exists |
| `shared/ui/confirm-dialog` | Exists |
| `shared/ui/data-table` (+ typy kolumn, sortowania, filtrów) | Exists |
| `shared/ui/status-badge` (+ `status-badge.spec.ts`) | Exists — test w `states.spec.ts` |
| `shared/ui/loading-state`, `empty-state`, `error-state`, `states.spec.ts` | Exists |
| `shared/format/pl-format`, `pl-format-pipes` | Exists |
| `shared/no-color-literals.spec.ts` | Missing — zastąpione `scripts/check-color-literals.mjs` (`lint:colors`) |
| Aktualizacja `conventions.md`, `role.md` | Exists |

## Database

None.

## Tests

| Test from the plan | Status | Run result |
|---|---|---|
| `pl-format.spec.ts` | Exists | Passed |
| `pl-format-pipes.spec.ts` | Exists | Passed |
| `button.spec.ts` | Exists | Passed |
| `field.spec.ts` | Exists | Passed |
| `confirm-dialog.spec.ts` | Exists | Passed |
| `data-table.spec.ts` | Exists | Passed |
| `status-badge.spec.ts` | Exists in `states.spec.ts` | Passed |
| `states.spec.ts` | Exists | Passed |
| `main-nav.spec.ts` | Exists | Passed |
| `app-shell.spec.ts` | Exists | Passed |
| `page-title-strategy.spec.ts` | Exists | Passed |
| `app.routes.spec.ts` | Exists | Passed |
| `section-pages.spec.ts` | Exists in `app.routes.spec.ts` | Passed |
| `no-color-literals.spec.ts` | Replaced by `npm run lint:colors` | Passed |
| Dodatkowo `data-table-cells.spec.ts`, `app.spec.ts` | Exists | Passed |

Łącznie: 13 plików, 93 testy, wszystkie Passed.

## Verification

Uruchomione 2026-11-03, Node v24.21.0, npm 11.6.1.

| Command | Outcome |
|---|---|
| `npm ci --ignore-scripts` (czysty katalog z `package.json`, `package-lock.json`, `.npmrc`) | Passed |
| `npm ci` w `app/` | Not run (pliki natywne zablokowane przez uruchomiony serwer deweloperski na Windows; wcześniejsze uruchomienie po zatrzymaniu serwera przeszło) |
| `npm run format:check` | Passed |
| `npm run lint` (z `lint:colors`) | Passed |
| `npm run typecheck` | Passed |
| `npm run build` | Passed (6 leniwych fragmentów sekcji) |
| `npm test -- --watch=false` | Passed (13 plików, 93 testy) |
| `grep` literałów kolorów w `app/src/app` | Passed (0 trafień) |
| Ręcznie: `/wydatki`, `/historia-zmian` w przeglądarce — motyw, tło `--sf-bg`, `aria-current`, tytuł karty, brak poziomego przewijania przy 1707 px | Passed |
| Ręcznie: szerokość 372 px (iframe) — brak poziomego przewijania, menu rozwija się, linki 44 px, Escape zwija i zwraca fokus, widoczna obwódka fokusu | Passed |
| Ręcznie: kolejność Tab od linku pomijającego i kolor obwódki fokusu na przyciskach daisyUI po poprawce | Not run (przeglądarka przestała odpowiadać) |
| Ręcznie: pułapka fokusu dialogu w przeglądarce | Not run (brak dialogu na żadnej stronie) |
| Ręcznie: 320 px | Not run (sprawdzono 372 px) |

## Deviations

- Planowany test `shared/no-color-literals.spec.ts` zastąpiono skryptem
  `app/scripts/check-color-literals.mjs` uruchamianym w `npm run lint` (`lint:colors`); build
  testów nie ma typów Node.
- `section-pages.spec.ts` połączono z `app.routes.spec.ts`; `status-badge.spec.ts` z
  `states.spec.ts`.
- `app/tsconfig.json`: dodano `lib: ["ES2023", "DOM", "DOM.Iterable"]` (formatowanie
  `useGrouping: 'always'` i ciągów dziesiętnych w `Intl.NumberFormat`).
- `DataTableColumn.value` jest opcjonalne; kolumna może mieć szablon `appTableCell`.
- Wejścia `title` komponentów `app-empty-state` i `app-confirm-dialog` nazwano `heading`.
- Od 1024 px nawigacja stoi w wierszu nagłówka, a nie pod nim (spec, Zachowanie 6).
- Na gałęzi są też artefakty pobrania SF-4 (`docs/tasks/sf-4/`).

## Additional Implementation

Additional implementation not covered by the original documents:

- dyrektywa `appTableCell` (`shared/ui/table-cell.ts`) — szablony komórek tabeli;
- unikalna nazwa nawigacji paginacji „Paginacja: {podpis}”;
- zamknięcie dialogu z zewnątrz rozstrzyga wynik jako `false`;
- wybór pozycji menu mobilnego przenosi fokus do `#tresc`;
- znacznik czasu bez strefy traktowany jako UTC;
- reguły CSS poza warstwami (fokus, granice `.input/.select/.textarea`, `aria-invalid`).

## Open Items

1. Potwierdzenie przez właściciela rozstrzygnięć autonomicznych z `02-spec.md` („Założenia”
   1–12): m.in. brak pozycji „Ustawienia”, slugi tras, próg 1024 px, brak biblioteki ikon,
   grupowanie tysięcy.
2. Merge: gałąź `sf-5-theme-shell` nie jest wypchnięta; PR do `main` uruchomi CI.
3. Ręczne sprawdzenia „Not run”: kolejność Tab i kolor fokusu na przyciskach po poprawce,
   pułapka fokusu dialogu, szerokość 320 px.
4. Code review — `pl-format.ts:13-19,58,64-68`: `"-0.001"`, `-0.001`, `"-0.004"` dają `-0,00 zł`.
5. Code review — `main-nav.html:25`, `app-shell.html:10`: nawigacja od 1024 px w wierszu
   nagłówka zamiast pod nim.
6. Code review — `confirm-dialog.ts:16-77`: zniszczenie komponentu przy otwartym dialogu
   zostawia `open()` bez rozstrzygnięcia.
7. Code review — `confirm-dialog.ts:30-35`: drugie `open()` nadpisuje element zwrotu fokusu.
8. Code review — `data-table.html:121-125`: kolumna bez `value` i bez szablonu daje pustą
   komórkę bez ostrzeżenia.
9. Code review — `styles.css:111-124`: reguły poza warstwami nie dają się nadpisać
   narzędziami Tailwind; brak wzmianki w `conventions.md`; komentarz w `:108` wskazuje złą
   warstwę.
10. Code review — `04-implementation-plan.md:184`: kontrast belki aktywnej pozycji to 4,04,
    nie 4,2 (tylko dokument).
11. Code review — luki testów: brak przypadku `-0.001`, pozytywnego sprawdzenia `submit`
    przycisku, zniszczenia otwartego dialogu i testu skryptu `lint:colors`.
12. Menu użytkownika i wygaśnięcie sesji w nagłówku muszą przejąć SF-6; stan synchronizacji —
    SF-14.
13. `npm audit`: 18 podatności moderate w zależnościach deweloperskich (bez zmian względem
    `main`).
