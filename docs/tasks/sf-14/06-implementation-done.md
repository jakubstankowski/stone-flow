# Implementation done — SF-14

## Summary

Dostarczono stan integracji w UI (frontend; backend bez zmian):
- **czysta funkcja stanu** `integrationStateOf`/`presentationOf`. Kolejność: „Nie skonfigurowano”, „Synchronizacja trwa”, „Błąd”, „Dane niekompletne”, „Aktualne”. Dochodzą stany „Sprawdzanie stanu…” i „Stan nieznany” oraz wariant `aged` („Stan z …”), gdy odczyt jest starszy niż dwa okresy;
- **serwis `IntegrationStatus`:**
  - odczyt w tle wychodzi tylko w obsłudze zdarzenia aktywności przy widocznej karcie, więc sesja wygasa ≤ 60 min po ostatniej aktywności (DOST-01);
  - numer sekwencyjny odczytów;
  - limity czasu 15 s / 45 s;
  - wiek odczytu liczony z zegara;
- **wskaźnik w nagłówku:** tekst i ikona, dostępna nazwa z czasem ostatniej pełnej kontroli;
- **ekran `/integracja`:**
  - stan, „Ostatnia pełna kontrola”, ostatni przebieg;
  - „Synchronizuj teraz” (202/409/limit czasu);
  - lista błędów z filtrem i stronicowaniem;
  - „Ponów” (200/404/409/502/503/limit czasu, unikalne nazwy, fokus na stały region wyniku);
- **komunikat `app-incomplete-data-notice`** w pięciu wariantach, zawsze z „Ostatnią pełną kontrolą”. Nigdzie go nie osadzono — zrobią to SF-19 i SF-20;
- **zmiany wspólnych elementów:** `app-status-badge` (slot ikony), `app-button` (`describedBy`), `formatTime`/`formatReadTime`, wpis w `conventions.md`.

Specyfikacja przeszła 4 rundy przeglądu (v1–v3 FAIL, v4 PASS WITH ISSUES). Przegląd kodu dał PASS WITH ISSUES. Poprawiono ważne 1–4 i większość drobnych; ponownego przeglądu kodu nie wykonano.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Stany INT-01 w ustalonej kolejności, 25 h, brak sukcesu, abandoned | Implemented | |
| Nie skonfigurowano / Stan nieznany / wariant aged | Implemented | |
| „Ostatnia pełna kontrola” zawsze przy wynikach | Implemented | komunikat w każdym wariancie, czas we wskaźniku |
| Wskaźnik w nagłówku (nie tylko kolor) | Implemented | |
| Ekran integracji: stan, przebieg, Synchronizuj teraz, błędy, Ponów | Implemented | |
| Komunikat wielokrotnego użytku | Implemented | nieosadzony (poza zakresem) |
| Reguła żądań w tle (DOST-01) | Implemented | |
| Dostępność (aria-live, stałe regiony, nazwy, describedBy, fokus) | Implemented | `aria-label` zamiast ukrytego tekstu we wskaźniku |

## Files

`app/src/app/features/integration/*` (modele, API, stan, komunikaty, serwisy statusu i błędów, odznaka, komunikat, ekran + testy), `app/src/app/layout/integration-indicator.*`, `app-shell.*`, `shared/ui/status-badge.ts`, `shared/ui/button.*`, `shared/format/pl-format.ts`, `testing/integration-testing.ts`, testy powłoki i tras, `.sdd/context/conventions.md`, dokumenty `docs/tasks/sf-14/*`.

## Planned Units

Wszystkie jednostki z planu: Exists. Pipe'y dla `formatTime`/`formatReadTime`: Missing (funkcje używane tylko w TS).

## Database

None.

## Tests

| Test | Status | Run result |
|---|---|---|
| Testy jednostkowe integracji, wskaźnika, powłoki, `app-button`, `pl-format` | Exists | Passed (47 plików, 536 testów) |

## Verification

| Command | Outcome |
|---|---|
| `npm run lint` | Passed |
| `npm test -- --watch=false` | Passed (536/536) |
| `npm run build` | Passed |
| `npm run typecheck` | Passed |
| `npx prettier --check src` | Passed |
| Ręczna weryfikacja z działającym API SF-13, czytnik ekranu | Not run |

## Deviations

- Brak pipe'ów dla `formatTime`/`formatReadTime`.
- Wskaźnik w nagłówku: `aria-label` zamiast wizualnie ukrytego tekstu, bo białe znaki szablonu dawały spację przed przecinkiem.
- Strona przy wejściu wywołuje `refresh()` tylko w fazie `loading`.
- W stanie `unknown` ekran pokazuje `app-error-state`, a ostatni znany czas widać we wskaźniku i komunikacie (zgodnie z Behavior 5).

## Additional Implementation

None.

## Open Items

- PR zależy od SF-13 (PR #12) — merge po nim.
- Ponowny przegląd kodu po poprawkach nie został wykonany.
- Z `05-code-review.md`:
  - przepływ „Synchronizuj teraz” w komponencie zamiast w serwisie;
  - luki testów ekranu: „Ponów” w `loading` i `unknown`, `AGED_HINT`, `409 integration_not_configured` i limit 15 s na poziomie ekranu.
- Znane ograniczenie (spec): przy bezczynnej karcie stan nie odświeża się do najbliższej aktywności; po dwóch okresach UI przechodzi w wariant ostrożny.
- Ręczna weryfikacja na środowisku z API SF-13 i czytnikiem ekranu.
