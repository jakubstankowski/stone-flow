# Code review — SF-3 (part b)

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Część b (commit `63ad6e8`) dostarcza wszystkie pliki z planu `04-implementation-plan-b.md`: aplikację Angular 22.2.4 w trybie strict ze `strictTemplates`, standalone, z leniwą trasą strony startowej, Vitest, angular-eslint (z regułami dostępności szablonów), Prettier i skryptem `typecheck` obejmującym `*.spec.ts` (punkt 10 przeglądu specyfikacji). Node i npm są przypięte dokładnie (`.nvmrc` 24.21.0, `packageManager` npm@11.6.1, `engine-strict`), zależności mają dokładne wersje. Wszystkie polecenia weryfikacji przeszły lokalnie. Pozostają drobne uwagi: brak landmarku `<main>`, 18 podatności o umiarkowanej wadze w zależnościach deweloperskich i szablonowy komentarz w `styles.css`.
- Risk: medium

## Project Invariant Violations

| Invariant | File:line | What is wrong |
|---|---|---|

None.

Sprawdzenie niezmienników:

| Niezmiennik | Status | Uwagi |
|---|---|---|
| 1. Sprzedaż oddzielona od gotówki | Nie dotyczy | Brak logiki i danych. |
| 2. Kwoty decimal na backendzie | OK | Frontend nie zawiera żadnych obliczeń ani kwot (`app/src/app/**`). |
| 3. Dostęp wymuszany przez API | Nie dotyczy | Aplikacja nie wywołuje API; brak `HttpClient` w `app/src/app/app.config.ts:6`. |
| 4. Ponowienia bez duplikatów | Nie dotyczy | — |
| 5. Audyt zmian finansowych | Nie dotyczy | — |
| 6. Sekrety po stronie serwera | OK | W `app/src` i `app/public` brak adresów URL, kluczy, tokenów i plików środowiskowych (grep po `http`, `key`, `secret`, `token`, `password` bez trafień). |
| 7. Granice modułów i warstw | Nie dotyczy | Dotyczy backendu (część a). |
| 8. Brak danych identyfikujących w dokumentach | OK | Jedyna treść to nazwa produktu „Stone Flow” (`app/src/app/features/start/start-page.html:1`, `app/src/index.html:5`); brak nazwy handlowej i domeny sklepu. |

## Conventions Violations

### Critical (must fix before merge)

None.

### Non-Critical (should fix)

- `app/src/app/app.html:1` / `app/src/app/features/start/start-page.html:1` — strona nie ma landmarku `<main>`; nagłówek `<h1>` jest bezpośrednio w `app-root`. Czytniki ekranu nie znajdą głównej treści przez nawigację po landmarkach. Powłoka aplikacji należy do SF-5, więc wystarczy to tam uwzględnić; dla samej strony startowej w SF-3 to uwaga, nie błąd.
Dostępność — pozytywnie: `angular.configs.templateAccessibility` włączone w `app/eslint.config.js:38`, `lang="pl"` i `<title>` ustawione.

Zgodne z `conventions.md`: nazewnictwo plików w stylu CLI 22 bez sufiksu `.component` (`app/src/app/features/start/start-page.ts`), obszar funkcjonalny w `app/src/app/features/{area}/`, ładowanie przez `loadComponent` (`app/src/app/app.routes.ts:6`), strict TypeScript (`app/tsconfig.json:6-10`) i strict templates (`app/tsconfig.json:20-22`), testy jednostkowe obok kodu.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Angular w trybie strict, standalone (W zakresie) | Covered | `app/tsconfig.json:6-22`; komponenty bez `NgModule`, bootstrap `app/src/main.ts:5`. |
| Jedna strona startowa z nazwą produktu po polsku, bez wywołań API (Zachowanie 10, UI) | Covered | `app/src/app/features/start/start-page.html:1`; brak providera HTTP. |
| `lang="pl"` w `index.html` (UI) | Covered | `app/src/index.html:2`. |
| Lazy routing gotowy na obszary funkcjonalne | Covered | `app/src/app/app.routes.ts:4-8`; build pokazuje osobny chunk `start-page`. |
| Vitest, ESLint, Prettier | Covered | `app/angular.json:66-74`, `app/eslint.config.js`, `app/.prettierrc.json`. |
| Build produkcyjny ze strict templates | Covered | `app/angular.json:52` (`defaultConfiguration: production`); `npm run build` przeszedł. |
| Przypięcie Node (`.nvmrc`, `engines`, `engine-strict`) | Covered | `app/.nvmrc:1` (24.21.0), `app/package.json:16-20`, `app/.npmrc:1`. |
| Dokładne wersje npm i lockfile | Covered | `app/package.json:21-43` bez `^`/`~`; `app/package-lock.json` zgodny (`@angular/core` 22.2.4); `npm ci` przeszedł. |
| Sprawdzanie typów testów (przegląd spec, pkt 10) | Covered | `app/package.json:13` — `tsc -p tsconfig.spec.json --noEmit`. |
| Wersja Node zgodna z przypiętą (Przypadki brzegowe) | Covered | `engine-strict=true` + `engines.node` `>=24.15.0 <25`. |

## Plan Deviations

- `app/package.json:16` — dodano pole `packageManager: "npm@11.6.1"`, którego plan nie wymienia (plan wymagał tylko `engines`). Zmiana zaostrza przypięcie npm; `conventions.md` już ją opisuje.
- Pliki z `ng new` spoza tabeli planu pozostały w repozytorium: `app/.vscode/extensions.json`, `app/.vscode/launch.json`, `app/.vscode/tasks.json`, `app/.editorconfig`, `app/.gitattributes`, `app/public/favicon.ico`, `app/src/styles.css`. Plan obejmuje je wierszem „`app/` (wynik `ng new`)”, ale krok 2 mówi „bez przykładowych plików poza wymaganymi”.
- `@angular/build` i `@angular/cli` są w wersji 22.2.5, a pakiety frameworka w 22.2.4 (`app/package.json:22-34`). Plan zakłada jedną „najnowszą 22.2.x”; różnica patchy między narzędziami a frameworkiem jest dopuszczalna, ale nie jest opisana.

## Null Safety Issues

None. Testy używają opcjonalnego łańcucha przy `querySelector` (`app/src/app/app.routes.spec.ts:21`, `app/src/app/features/start/start-page.spec.ts:10`); kod produkcyjny nie ma wartości nullowalnych.

## Correctness Issues

None.

## Test Gaps

- Plan, sekcja 9, przewiduje ręczne sprawdzenie, że celowy błąd typu w `start-page.spec.ts` zatrzymuje `npm run typecheck`. Nie ma śladu tej weryfikacji w repozytorium; przegląd jej nie powtarzał (bez modyfikacji kodu). Konfiguracja `app/tsconfig.spec.json:8` obejmuje `src/**/*.spec.ts`, więc mechanizm jest poprawny.
- Wszystkie cztery testy z sekcji 8 planu są obecne i sensowne (`app/src/app/app.spec.ts:13`, `app/src/app/features/start/start-page.spec.ts:5`, `app/src/app/app.routes.spec.ts:12`, `app/src/app/app.routes.spec.ts:24`).

Weryfikacja wykonana w ramach przeglądu:

Uruchomione w `app/` 2026-11-03 (Node v24.21.0, npm 11.6.1 — zgodne z `.nvmrc` i `packageManager`):

| Polecenie | Wynik |
|---|---|
| `npm ci` | przeszło (18 moderate w `npm audit`) |
| `npm run format:check` | przeszło |
| `npm run lint` | przeszło („All files pass linting.”) |
| `npm run typecheck` | przeszło |
| `npm run build` | przeszło (chunk leniwy `start-page`) |
| `npm test -- --watch=false` | przeszło (3 pliki, 4 testy) |

## Code Smells

- `app/src/styles.css:1` — szablonowy komentarz CLI („You can add global styles…”) w pustym pliku. Plik i tak zostanie zastąpiony w SF-5; do usunięcia albo pozostawienia świadomie.
- `app/package-lock.json` — `npm audit` zgłasza 18 podatności o umiarkowanej wadze w zależnościach deweloperskich (`@typescript-eslint/typescript-estree` i pochodne, przez `typescript-eslint` 8.71.0 / `angular-eslint` 22.6.0). Nie trafiają do bundla, ale warto je odnotować i śledzić aktualizacje.

## Recommendation

- Merge as-is
