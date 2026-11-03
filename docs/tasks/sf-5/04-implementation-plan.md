# Implementation Plan — SF-5

Tryb: plan jednowarstwowy (tylko frontend `app/`, bez kontraktu z backendem). Bez podziału na
części — zmiana obejmuje jedną aplikację i da się ją przejrzeć w całości.

## 1. Zakres

Plan dodaje do `app/` Tailwind CSS 4 i daisyUI 5 z jednym motywem `stone-flow` zbudowanym z
tokenów `--sf-*`, powłokę aplikacji (`layout/`) z nagłówkiem, nawigacją poziomą i menu
mobilnym, sześć leniwie ładowanych sekcji-zaślepek w `features/`, wspólne elementy UI w
`shared/ui/` i formatowanie w `shared/format/`, testy Vitest oraz aktualizację
`.sdd/context/conventions.md` i `role.md`. Usuwa stronę startową z SF-3. Backend bez zmian.

Rozstrzygnięcia uwag z `03-spec-review.md` (bez zmiany wymagań spec):

| Uwaga przeglądu | Rozstrzygnięcie w planie |
|---|---|
| 1. Prefiks `sf-` vs reguła lint `app` | Selektory z prefiksem `app` (`app-button`, `app-confirm-dialog`, …) zgodnie z `eslint.config.js`; nazwy w spec (`sf-button`…) traktowane jako robocze. Bez zmiany reguły lint. |
| 2. Tło nawigacji i aktywnej pozycji, fokus na ciemnym tle | Nagłówek i nawigacja na jasnej powierzchni `--sf-surface`; aktywna pozycja: tło `--sf-surface-soft`, tekst `--sf-accent-text` (6,14:1), dolna belka 3 px `--sf-accent`, `font-semibold`, `aria-current`. Grafitowy fokus zawsze na jasnym tle (≥ 10,58:1). Brak ciemnej nawigacji w SF-5. |
| 3. Znaki separatora i minusa | Wynik `Intl.NumberFormat('pl-PL')`: separator tysięcy i spacja przed „zł” to U+00A0, minus to U+002D. Testy porównują dokładne ciągi z ` `. |
| 4. Kwota jako `number` | Formater przyjmuje `number \| string \| null \| undefined`; ciąg dziesiętny z API (np. `"1234.50"`) przekazywany wprost do `Intl.NumberFormat.format`, który formatuje go bez konwersji na liczbę zmiennoprzecinkową. |
| 5. Panel filtrów na telefonie | Tabela opakowuje projekcję filtrów: poniżej 768 px przycisk „Filtry” (`aria-expanded`) zwija/rozwija panel; od 768 px panel zawsze widoczny. |
| 6. „Zachowany kontekst” przy przewijaniu | Pierwsza kolumna tabeli `sticky left-0` z tłem powierzchni w kontenerze przewijania. |
| 7. `showModal()` w jsdom | `src/test-setup.ts` dodaje minimalny stub `HTMLDialogElement.prototype.showModal/close` tylko w testach; pułapka fokusu natywnego dialogu sprawdzana ręcznie w przeglądarce (Weryfikacja). |
| 8. Menu użytkownika bez właściciela | Wpis w „Ryzyka”: SF-6 musi je przejąć. |
| 9. Blokada przycisku `busy` | Komponent `app-button` z wewnętrznym natywnym `<button>`: przy `busy` `aria-disabled="true"`, `aria-busy="true"`, kliknięcie i `submit` blokowane (`preventDefault`), fokus pozostaje na przycisku. `disabled` używane tylko dla trwale niedostępnej akcji. |
| Niejasne: strona poza zakresem | Tabela pokazuje stan „brak wyników” bez wierszy, zakres liczony z obcięciem do `total`, „Poprzednia” aktywna; zmiana strony należy do rodzica. |
| Niejasne: zły format miesiąca/czasu | Zwracają „Brak danych”. |
| Niejasne: aria na projektowanej kontrolce | `app-field` szuka projekcji z referencją `#control` (`contentChild`) i ustawia `id`, `aria-describedby`, `aria-invalid`, `aria-required` przez `Renderer2` w `effect`. |
| Niejasne: `<main>` i fokus | `<main id="tresc" tabindex="-1">`, link „Przejdź do treści” przenosi fokus. |

## 2. Obszary

| Warstwa | Moduł / funkcja | Dlaczego |
|---|---|---|
| Frontend — konfiguracja | `app/package.json`, `.postcssrc.json`, `angular.json`, `styles.css`, `index.html` | Tailwind, daisyUI, motyw, tokeny, `data-theme` |
| Frontend — powłoka | `app/src/app/layout/` | nagłówek, nawigacja, menu mobilne, tytuły sekcji |
| Frontend — trasy | `app/src/app/app.routes.ts`, `features/*` | sześć sekcji z `loadComponent` |
| Frontend — wspólne UI | `app/src/app/shared/ui/` | przycisk, pole, dialog, tabela, status, stany |
| Frontend — formatowanie | `app/src/app/shared/format/` | kwoty, daty, miesiące, czas techniczny |
| Kontekst projektu | `.sdd/context/conventions.md`, `role.md` | nowe katalogi i wersje |

## 3. Pliki

| Ścieżka | Akcja | Cel |
|---|---|---|
| `app/package.json`, `app/package-lock.json` | modify | `tailwindcss` 4.3.4, `@tailwindcss/postcss` 4.3.4, `postcss` 8.5.32, `daisyui` 5.7.50 (devDependencies, dokładne wersje) |
| `app/.postcssrc.json` | create | `{"plugins":{"@tailwindcss/postcss":{}}}` |
| `app/src/styles.css` | modify | `@import "tailwindcss"`, `@plugin "daisyui" { themes: false }`, `@plugin "daisyui/theme"` motyw `stone-flow` (default), `:root` tokeny `--sf-*`, font, fokus, reduced-motion, `tabular-nums` |
| `app/src/index.html` | modify | `data-theme="stone-flow"` na `<html>` |
| `app/angular.json` | modify | budżet `anyComponentStyle` bez zmian; `test` z `setupFiles` (jeśli wymagane) |
| `app/src/test-setup.ts` | create | stub `showModal`/`close` dla jsdom |
| `app/tsconfig.spec.json` | modify | dołączenie `src/test-setup.ts` (jeśli wymagane przez builder) |
| `app/src/app/app.config.ts` | modify | `LOCALE_ID: 'pl-PL'`, `registerLocaleData(localePl)`, `withComponentInputBinding` niepotrzebne; `TitleStrategy` z sufiksem |
| `app/src/app/app.html`, `app.ts` | modify | `<app-shell />` z `<router-outlet />` |
| `app/src/app/app.routes.ts` | modify | sześć tras + przekierowania |
| `app/src/app/app.routes.spec.ts`, `app.spec.ts` | modify | testy tras i powłoki |
| `app/src/app/features/start/*` | delete | strona startowa z SF-3 |
| `app/src/app/layout/app-shell.ts`, `.html`, `.spec.ts` | create | szkielet: skip link, `header`, `nav`, `main` |
| `app/src/app/layout/main-nav.ts`, `.html`, `.spec.ts` | create | pozycje nawigacji, menu mobilne |
| `app/src/app/layout/nav-items.ts` | create | lista sekcji (etykieta, ścieżka) — jedno źródło dla tras, nawigacji i tytułów |
| `app/src/app/layout/page-title-strategy.ts`, `.spec.ts` | create | tytuł „{Sekcja} · Stone Flow” |
| `app/src/app/features/{dashboard,orders,expenses,operations,change-history,integration}/{obszar}-page.ts`, `.html` | create | zaślepki: `<h1>` + `app-empty-state` |
| `app/src/app/features/section-pages.spec.ts` | create | test zaślepek |
| `app/src/app/shared/ui/button.ts`, `.html`, `.spec.ts` | create | `app-button` |
| `app/src/app/shared/ui/field.ts`, `.html`, `.spec.ts` | create | `app-field` |
| `app/src/app/shared/ui/confirm-dialog.ts`, `.html`, `.spec.ts` | create | `app-confirm-dialog` |
| `app/src/app/shared/ui/data-table.ts`, `.html`, `.spec.ts` | create | `app-data-table` + typy kolumn, sortowania, filtrów |
| `app/src/app/shared/ui/status-badge.ts`, `.spec.ts` | create | `app-status-badge` |
| `app/src/app/shared/ui/loading-state.ts`, `empty-state.ts`, `error-state.ts`, `states.spec.ts` | create | stany systemowe |
| `app/src/app/shared/format/pl-format.ts`, `.spec.ts` | create | funkcje formatowania |
| `app/src/app/shared/format/pl-format-pipes.ts`, `.spec.ts` | create | pipe’y `plnAmount`, `plDate`, `plMonth`, `plDateTime` |
| `app/src/app/shared/no-color-literals.spec.ts` | create | test: brak literałów kolorów w `src/app/**` (czyta pliki przez `import.meta.glob` jako raw) |
| `.sdd/context/conventions.md` | modify | katalogi `layout/`, `shared/ui/`, `shared/format/`, trasy, prefiks `app`, reguły kolorów |
| `.sdd/context/role.md` | modify | Tailwind CSS 4.3.4, daisyUI 5.7.50 (zamknięcie TODO) |

## 4. Kroki implementacji

1. [ ] **Zależności:** `npm install -D -E tailwindcss@4.3.4 @tailwindcss/postcss@4.3.4 postcss@8.5.32 daisyui@5.7.50`; `.postcssrc.json`.
2. [ ] **Tokeny i motyw** w `styles.css`: `:root` z 14 tokenami `--sf-*` (jedyne miejsce
   literałów kolorów w repozytorium frontendu); `@plugin "daisyui/theme"` `name: "stone-flow"`,
   `default: true`, `color-scheme: light`, role mapowane na `var(--sf-…)` według tabeli z
   design guide; `--radius-field` 0.5rem, `--radius-box` 1rem; font systemowy; `:focus-visible`
   obwódka 2 px `--sf-text` z `outline-offset: 2px`; `@media (prefers-reduced-motion: reduce)`.
   `index.html`: `data-theme="stone-flow"`.
3. [ ] **Formatowanie** (`shared/format/pl-format.ts`): `formatPln`, `formatDate`,
   `formatMonth`, `formatDateTime`, stała `NO_DATA = 'Brak danych'`; pipe’y czyste
   (`pure: true`). Najpierw testy.
4. [ ] **Locale i tytuły:** `app.config.ts` — `registerLocaleData(localePl)`, `LOCALE_ID`,
   `PageTitleStrategy`.
5. [ ] **Stany systemowe:** `app-loading-state` (wejście `rows`), `app-empty-state` (`title`,
   `description`, projekcja akcji), `app-error-state` (`message`, wyjście `retry`).
6. [ ] **Status:** `app-status-badge` (`label` wymagane, `tone`) — daisyUI `badge` z klasą
   tonu; dla czytelności tekst w kolorze tonu na jasnej powierzchni (`badge-outline`-podobny
   wygląd z tokenów), nie biały na kolorze.
7. [ ] **Przycisk:** `app-button` (`variant`, `type`, `busy`, `busyLabel`, `disabled`;
   wyjście `pressed`) na klasach `btn btn-primary|btn-outline|btn-error`, `min-h-11`.
8. [ ] **Pole:** `app-field` (`label`, `fieldId`, `required`, `hint`, `error`).
9. [ ] **Dialog:** `app-confirm-dialog` (`title`, `confirmLabel`, `tone`; metoda
   `open(): Promise<boolean>`; projekcja treści). Zapamiętanie `document.activeElement`
   przed `showModal()`, fokus na „Anuluj”, zdarzenie `cancel` (Escape) → `false`, zwrot
   fokusu w `close`; gdy element nie jest już w DOM → `#tresc`.
10. [ ] **Tabela:** `app-data-table<T>` z typami `DataTableColumn<T>`, `SortState`,
    `ActiveFilter`; wejścia i wyjścia jak w spec; panel filtrów zwijany < 768 px; kontener
    `overflow-x-auto` z `role="region"`, `aria-label`, `tabindex="0"`; pierwsza kolumna sticky;
    nagłówki sortowalne jako przyciski z `aria-sort` na `th`; paginacja z tekstem zakresu.
11. [ ] **Powłoka:** `nav-items.ts`, `app-main-nav` (pozioma ≥ 1024 px, przycisk „Menu” <
    1024 px; `routerLinkActive` z `ariaCurrentWhenActive="page"`; Escape zwija i zwraca fokus;
    zamknięcie po `NavigationEnd`), `app-shell` (skip link, nagłówek z nazwą produktu i nazwą
    sekcji z danych trasy, `main#tresc`, `max-w-[1600px]`, `px-4 lg:px-8`).
12. [ ] **Trasy i zaślepki:** sześć komponentów `features/*/…-page`; `app.routes.ts` z
    `title` i `data.section`; `''` i `**` → `pulpit`; usunięcie `features/start`.
13. [ ] **Test braku literałów kolorów** i aktualizacja testów `app.spec.ts`, `app.routes.spec.ts`.
14. [ ] **Kontekst:** `conventions.md` (Frontend → Structure, Components, Styling) i `role.md`.
15. [ ] **Weryfikacja** z sekcji 9.

## 5. Dane i kontrakty

- Storage: brak.
- Endpointy API: brak.
- Typy wspólne (frontend, eksport z `shared/ui/data-table.ts` i `status-badge.ts`):
  `DataTableColumn<T> { key; header; align?: 'start' | 'end'; sortable?; cell?: (row: T) => string }`,
  `SortState { key: string; direction: 'asc' | 'desc' } | null`,
  `ActiveFilter { id: string; label: string }`,
  `StatusTone = 'neutral' | 'success' | 'warning' | 'error' | 'info'`.
  Sygnatury poglądowe.

## 6. Przypadki brzegowe i null safety

| Przypadek | Gdzie obsłużony |
|---|---|
| `/`, nieznana ścieżka → `/pulpit` | `app.routes.ts` (`redirectTo`, `pathMatch: 'full'`) |
| 320 px bez poziomego przewijania | `app-shell` (`min-w-0`, `px-4`), tabela w kontenerze `overflow-x-auto`, nawigacja mobilna jako lista pionowa |
| Menu otwarte → ekran ≥ 1024 px | lista mobilna ma `lg:hidden`, nawigacja pozioma `hidden lg:flex`; stan menu nie wpływa na treść |
| Escape w menu | `app-main-nav` zwija i fokus na „Menu” |
| Dialog: Escape / Anuluj → `false`; zwrot fokusu; wywołujący usunięty | `app-confirm-dialog` (`isConnected` → `#tresc`) |
| `busy` i wielokrotne kliknięcie | `app-button` nie emituje `pressed`, `preventDefault` dla submit |
| Tabela: `total = 0`, strona poza zakresem, ostatnia niepełna, rozmiar > total | `app-data-table` — obliczenia `from`/`to` z `Math.min`, przyciski wyłączone na krańcach |
| Kwoty: `0`, `null`, ujemna, `1234.5`, `"1234.50"`, `1234567.89` | `formatPln` |
| Zła data / miesiąc / czas | `formatDate`, `formatMonth`, `formatDateTime` → `NO_DATA` (walidacja regex + `Number.isNaN`) |
| UTC tuż przed północą, zmiana czasu | `formatDateTime` z `timeZone: 'Europe/Warsaw'` |
| `Activity` sekcji bez `data.section` | nagłówek pokazuje sam produkt (typ `string \| undefined` obsłużony) |

## 7. Niezmienniki

| Niezmiennik | Gdzie egzekwowany | Test |
|---|---|---|
| 2. Kwoty decimal na backendzie | `pl-format.ts` tylko formatuje; brak arytmetyki na kwotach | `pl-format.spec.ts` — `"1234.50"` formatowane bez utraty precyzji; przegląd kodu |
| 6. Sekrety po stronie serwera | brak konfiguracji API i kluczy | grep w przeglądzie; build bez zmiennych środowiskowych |
| 8. Brak danych identyfikujących | teksty i dane testowe fikcyjne | przegląd kodu |

Niezmienniki 1, 3, 4, 5, 7 — nie dotyczy.

## 8. Testy

| Test | Typ | Co dowodzi |
|---|---|---|
| `pl-format.spec.ts` — kwoty (8 przypadków z spec), daty, miesiące, czas, błędne wejście | jednostkowy | polskie formaty, „Brak danych”, precyzja ciągów |
| `pl-format-pipes.spec.ts` | jednostkowy | pipe’y delegują do funkcji |
| `button.spec.ts` — warianty, `busy` blokuje `pressed`, `aria-busy`, `aria-disabled`, etykieta „Zapisywanie…” | komponentowy | ochrona przed podwójnym zapisem |
| `field.spec.ts` — etykieta powiązana, „(wymagane)”, `aria-describedby`, `aria-invalid` | komponentowy | dostępność pól |
| `confirm-dialog.spec.ts` — `true`/`false`, fokus na „Anuluj”, zwrot fokusu, wywołujący usunięty | komponentowy (stub dialogu) | zwrot fokusu |
| `data-table.spec.ts` — zakres „41–45 z 45”, przyciski na krańcach, `aria-sort`, emisja sortowania i strony, aktywne filtry i „Wyczyść filtry”, stany ładowania/błędu/pustki (2 warianty), wyrównanie kwot | komponentowy | API tabeli dla modułów |
| `status-badge.spec.ts` — tekst zawsze widoczny, ton jako klasa | komponentowy | status nie tylko kolorem |
| `states.spec.ts` — `role="status"`, `role="alert"`, `retry` | komponentowy | stany systemowe |
| `main-nav.spec.ts` — 6 pozycji w kolejności, `aria-current`, „Menu” `aria-expanded`, Escape | komponentowy | nawigacja klawiaturą i mobilna |
| `app-shell.spec.ts` — skip link, landmarki, nazwa sekcji | komponentowy | struktura powłoki |
| `page-title-strategy.spec.ts` | jednostkowy | tytuł dokumentu |
| `app.routes.spec.ts` — każda ścieżka ładuje swoją stronę leniwie; `/` i `**` → `/pulpit` | jednostkowy (`RouterTestingHarness`) | routing |
| `section-pages.spec.ts` — `<h1>` i stan pustki | komponentowy | zaślepki |
| `no-color-literals.spec.ts` | statyczny | brak lokalnych kodów kolorów |

Kontrast (obliczony wzorem WCAG dla użytych par):

| Para | Kontrast | Wymóg |
|---|---|---|
| tekst `--sf-text` / `--sf-bg`, `--sf-surface`, `--sf-surface-soft` | 10,58 / 12,91 / 11,58 | 4,5 |
| `--sf-muted` / `--sf-bg`, `--sf-surface`, `--sf-surface-soft` | 4,97 / 6,06 / 5,44 | 4,5 |
| `--sf-accent-text` / `--sf-surface-soft` (aktywna nawigacja), `--sf-surface` | 6,14 / 6,84 | 4,5 |
| biały / `--sf-accent` (przycisk główny), `--sf-accent-hover` | 4,51 / 5,77 | 4,5 |
| biały / `--sf-error` (przycisk destrukcyjny) | 6,78 | 4,5 |
| `--sf-success`, `--sf-warning`, `--sf-error`, `--sf-info` / `--sf-surface` (statusy) | 6,34 / 6,66 / 6,78 / 6,96 | 4,5 |
| `--sf-border-strong` / `--sf-bg`, `--sf-surface` (granice pól) | 3,54 / 4,32 | 3 |
| `--sf-accent` / `--sf-surface-soft` (belka aktywnej pozycji) | ≥ 3 (4,2) | 3 |
| fokus `--sf-text` / jasne tła | ≥ 10,58 | 3 |

Pary niedozwolone (nieużywane): `--sf-text` na `--sf-accent` (2,86), `--sf-secondary` jako
tekst lub jedyna granica (2,05).

## 9. Weryfikacja

W `app/`:

```
npm ci
npm run format:check
npm run lint
npm run typecheck
npm run build
npm test -- --watch=false
```

Grep: `grep -rnE "#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(" app/src/app` → brak trafień.

Ręcznie (`npm start`, przeglądarka):
- Tab od początku strony: „Przejdź do treści” → nazwa produktu → pozycje nawigacji; widoczny fokus.
- Szerokość 320 i 375 px: brak poziomego przewijania; „Menu” rozwija listę; Escape zwija i
  zwraca fokus.
- `/` i `/cokolwiek` → `/pulpit`; tytuł karty „Pulpit · Stone Flow”.
- Dialog potwierdzenia (tymczasowo w zaślepce lub w teście w przeglądarce): Tab nie wychodzi
  poza dialog, Escape zamyka, fokus wraca do przycisku otwierającego.

## 10. Ryzyka

- **Menu użytkownika i wygaśnięcie sesji** nie mają jednoznacznego właściciela w rozbiciu;
  SF-6 musi je dodać do nagłówka.
- **Stan synchronizacji** w nagłówku dodaje SF-14.
- **Rozstrzygnięcia autonomiczne** (Założenia w spec, m.in. brak „Ustawień”, próg 1024 px,
  slugi tras, grupowanie tysięcy) wymagają potwierdzenia właściciela; zmiana któregoś
  oznacza drobną poprawkę, nie przebudowę.
- **Tailwind 4.3.4** opublikowany 02.11.2026; przy problemie z buildem cofnięcie do 4.3.3
  (wersja z SF-1).
- **Natywny `<dialog>` w jsdom** jest stubowany; pełne zachowanie pułapki fokusu sprawdza
  się tylko w przeglądarce.
