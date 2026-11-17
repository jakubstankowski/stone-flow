# Implementation Plan — SF-3 (część b)

## 0. Część

- **Litera:** b — frontend.
- **Dostarcza:** aplikację Angular w `app/` w trybie strict (standalone, lazy routing, jedna
  strona startowa), przypięte wersje Node i npm (`.nvmrc`, `.npmrc`, `engines`, lockfile),
  testy Vitest, ESLint, Prettier i osobne sprawdzenie typów kodu i testów.
- **Wymaga wcześniejszego merge:** brak (niezależna od a). Część c wymaga a i b.

## 1. Zakres

Część tworzy od zera aplikację frontendu z `02-spec.md`, sekcje „W zakresie” (aplikacja
Angular w trybie strict) i „Zachowanie”, punkt 10. Aplikacja nie wywołuje API, nie zawiera
Tailwind CSS, daisyUI, motywu ani powłoki (SF-5). Jedyny widok to strona startowa z nazwą
produktu.

## 2. Obszary

| Warstwa | Moduł / funkcja | Dlaczego |
|---|---|---|
| Frontend | `app/` — konfiguracja | wersje, strict, lint, format, testy, sprawdzanie typów |
| Frontend | `app/src/app` — routing | lazy routing gotowy na obszary funkcjonalne |
| Frontend | `app/src/app/features/start` | strona startowa |

## 3. Pliki

| Ścieżka | Akcja | Cel |
|---|---|---|
| `app/` (wynik `ng new`) | create | szkielet generowany przez Angular CLI 22.2.x |
| `app/.nvmrc` | create | dokładna wersja Node 24.x LTS (najnowsza ≥ 24.15.0 przy implementacji) |
| `app/.npmrc` | create | `engine-strict=true`, `save-exact=true` |
| `app/package.json` | create | dokładne wersje zależności (bez `^`/`~`), `engines` (`node` `>=24.15.0 <25`, `npm` zgodny z Node), skrypty `start`, `build`, `test`, `lint`, `format`, `format:check`, `typecheck` |
| `app/package-lock.json` | create | lockfile |
| `app/angular.json` | create | builder aplikacji, Vitest, ESLint; `"strictTemplates": true` w `tsconfig` |
| `app/tsconfig.json` | create | `strict: true` i opcje strict Angulara (`strictTemplates`, `strictInjectionParameters`, `strictInputAccessModifiers`, `noImplicitOverride`, `noPropertyAccessFromIndexSignature`, `noImplicitReturns`, `noFallthroughCasesInSwitch`) |
| `app/tsconfig.app.json`, `app/tsconfig.spec.json` | create | konfiguracje kompilacji aplikacji i testów |
| `app/eslint.config.js` | create | angular-eslint (TS + szablony) |
| `app/.prettierrc.json`, `app/.prettierignore` | create | konfiguracja Prettier; ignorowane `dist/`, `.angular/`, `coverage/` |
| `app/.gitignore` | create | z `ng new` (`node_modules/`, `dist/`, `.angular/`) |
| `app/src/index.html` | create | `lang="pl"`, `<title>Stone Flow</title>` |
| `app/src/main.ts` | create | bootstrap standalone z `appConfig` |
| `app/src/app/app.config.ts` | create | `provideRouter(routes)`, wykrywanie zmian zoneless (domyślne w CLI 22) |
| `app/src/app/app.routes.ts` | create | trasa `''` z `loadComponent` do strony startowej; `**` przekierowuje na `''` |
| `app/src/app/app.ts`, `app.html` | create | komponent główny z `<router-outlet />` |
| `app/src/app/features/start/start-page.ts`, `start-page.html` | create | strona startowa: nagłówek `<h1>Stone Flow</h1>` |
| `app/src/app/app.spec.ts` | create | test komponentu głównego |
| `app/src/app/features/start/start-page.spec.ts` | create | test strony startowej |
| `app/src/app/app.routes.spec.ts` | create | test lazy routingu |

Nazewnictwo plików według bieżącego przewodnika stylu Angular generowanego przez CLI 22
(`start-page.ts` bez sufiksu `.component`). Domyślny szablon powitalny CLI jest usuwany.

## 4. Kroki implementacji

1. [ ] Zainstalować Node w dokładnej wersji 24.x LTS (najnowsza ≥ 24.15.0), zapisać ją w
   `app/.nvmrc`.
2. [ ] Wygenerować aplikację: `npx @angular/cli@22.2.<najnowszy patch> new stone-flow
   --directory app --strict --routing --style=css --ssr=false --skip-git
   --package-manager=npm` (bez konfiguracji AI i bez przykładowych plików poza wymaganymi).
   Wersja CLI = dokładna najnowsza stabilna 22.2.x; zgodność TypeScript i RxJS według
   metadanych pakietu (SF-1: TypeScript `>=6.0.0 <6.1.0`).
3. [ ] Potwierdzić, że test runnerem jest Vitest (domyślny w CLI 22); w przeciwnym razie
   skonfigurować builder testów Vitest.
4. [ ] Dodać `app/.npmrc`, `engines` w `package.json`, zamienić zakresy wersji na dokładne,
   odtworzyć `package-lock.json` (`npm install`), sprawdzić `npm ci`.
5. [ ] Dodać angular-eslint (`ng add angular-eslint` w wersji zgodnej z Angular 22) i skrypt
   `lint`.
6. [ ] Dodać Prettier w dokładnej wersji, `.prettierrc.json`, `.prettierignore`, skrypty
   `format` i `format:check`; sformatować wygenerowany kod.
7. [ ] Dodać skrypt `typecheck`: `tsc -p tsconfig.app.json --noEmit && tsc -p
   tsconfig.spec.json --noEmit`, aby błąd typu w plikach `*.spec.ts` też zatrzymał CI
   (przegląd spec, punkt 10). Szablony sprawdza `ng build` ze `strictTemplates`.
8. [ ] Zastąpić szablon powitalny: `app.ts`/`app.html` tylko z `<router-outlet />`;
   `features/start/start-page.*` z nagłówkiem „Stone Flow”; `app.routes.ts` z `loadComponent`;
   `index.html` z `lang="pl"` i tytułem.
9. [ ] Napisać testy z sekcji 8.
10. [ ] Weryfikacja z sekcji 9.

## 5. Dane i kontrakty

- Storage: brak.
- Endpointy API: brak — aplikacja nie wywołuje backendu.
- Typy na granicy: brak.

## 6. Przypadki brzegowe i null safety

| Przypadek | Gdzie obsłużony |
|---|---|
| Wersja Node spoza `engines` | `.npmrc` `engine-strict=true` → `npm ci` kończy błędem |
| Nieznana ścieżka (np. `/cokolwiek`) | trasa `**` → przekierowanie na stronę startową |
| Błąd typu w kodzie aplikacji / szablonie | `typecheck` / `ng build` ze `strictTemplates` |
| Błąd typu w `*.spec.ts` | `typecheck` z `tsconfig.spec.json` |
| Plik bez formatowania | `format:check` |

## 7. Niezmienniki

| Niezmiennik | Gdzie egzekwowany | Test |
|---|---|---|
| 2. Kwoty decimal na backendzie | frontend nie zawiera żadnych obliczeń ani danych | przegląd kodu |
| 6. Sekrety po stronie serwera | brak konfiguracji środowiskowej i kluczy w bundle; aplikacja nie łączy się z API ani WooCommerce | przegląd kodu; skan sekretów w CI — część c |
| 8. Brak danych identyfikujących | strona zawiera tylko nazwę produktu „Stone Flow” | `start-page.spec.ts` sprawdza treść nagłówka |

Niezmienniki 1, 3, 4, 5, 7 — nie dotyczy.

## 8. Testy

| Test | Typ | Co dowodzi |
|---|---|---|
| `app.spec.ts` — tworzy komponent i renderuje `router-outlet` | jednostkowy (Vitest) | komponent główny jest powłoką routingu |
| `start-page.spec.ts` — renderuje nagłówek „Stone Flow” | jednostkowy | strona startowa z nazwą produktu |
| `app.routes.spec.ts` — `''` ładuje `StartPage` leniwie | jednostkowy (`RouterTestingHarness`) | lazy routing działa |
| `app.routes.spec.ts` — nieznana ścieżka przekierowuje na `''` | jednostkowy | trasa `**` |

## 9. Weryfikacja

W katalogu `app/`:

```
npm ci
npm run format:check
npm run lint
npm run typecheck
npm run build          # konfiguracja production
npm test -- --watch=false
```

Ręcznie:
- `npm start` → `http://localhost:4200` pokazuje „Stone Flow”; `/cokolwiek` wraca na stronę
  startową; w DevTools brak żądań do API.
- Celowy błąd typu w `start-page.spec.ts` → `npm run typecheck` czerwony (cofnąć).
- `index.html` ma `lang="pl"`.

## 10. Ryzyka

- **Wersje przy otwartym D-08:** Angular 22.2.x i Node 24 według SF-1; dokładne numery
  ustalane przy implementacji i zapisywane w `package.json`/`.nvmrc`. Korekta po D-08 — osobna
  zmiana.
- **Domyślne ustawienia CLI 22:** jeśli `ng new` w 22.2.x generuje inny test runner, inne
  nazewnictwo plików albo strefę (zone.js), plan zakłada dostosowanie do Vitest i zoneless bez
  zmiany zakresu; rozbieżność zostanie opisana w code review.
- **Routing SPA na Vercel** (odświeżenie podstrony) należy do SF-4.
