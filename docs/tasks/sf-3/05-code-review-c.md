# Code review — SF-3 (part c)

Zakres: commit `7429326` (`.github/workflows/ci.yml`, `.gitleaks.toml`, `README.md`,
`.sdd/context/conventions.md`, `.sdd/context/role.md`) względem `02-spec.md`,
`03-spec-review.md` i `04-implementation-plan-c.md`. Kod części a i b oceniany jest tylko tam,
gdzie README lub konwencje go opisują.

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Wszystkie pliki z planu istnieją, workflow ma trzy zadania o nazwach z planu, akcje są przypięte do pełnych SHA (zgodnych z tagami w repozytoriach `actions/*`), gitleaks jest pobierany z dokładną wersją i sprawdzaną sumą SHA-256 (zgodną z oficjalnym plikiem sum kontrolnych), a README i konwencje zgadzają się z rzeczywistymi ścieżkami, nazwami `DbContext`, portami i kluczami konfiguracji. Nie ma naruszeń niezmienników. Otwarte pozostaje kryterium „CI blokuje … (sprawdzone celowo zepsutą gałęzią)”: nie ma śladu ani jednego przebiegu CI, bo workflow nie uruchamia się na gałęzi `sf-3c-ci` bez PR do `main`. Pozostałe uwagi są drobne.
- Risk: medium

## Project Invariant Violations

| Invariant | File:line | What is wrong |
|---|---|---|

None.

Sprawdzenie niezmienników:

| Niezmiennik | Status | Uwagi |
|---|---|---|
| 1. Sprzedaż oddzielona od gotówki | Nie dotyczy | Brak logiki finansowej. |
| 2. Kwoty decimal na backendzie | Nie dotyczy | Brak kwot. |
| 3. Dostęp wymuszany przez API | OK | README (`README.md:80-85`) opisuje tylko anonimowy `/health` i OpenAPI w Development, zgodnie ze spec. |
| 4. Ponowienia bez duplikatów | Nie dotyczy | — |
| 5. Audyt zmian finansowych | Nie dotyczy | — |
| 6. Sekrety po stronie serwera | OK | Workflow nie używa sekretów, `permissions: contents: read` (`ci.yml:9-10`); zadanie `secrets` skanuje całą historię (`ci.yml:98`, `ci.yml:109`, `--exit-code 1`). `deploy/.env` jest ignorowany (`.gitignore`), w repozytorium są tylko `deploy/.env.example` i `deploy/api.env.example` z placeholderami. `git grep` po wzorcach `Password=`, `pwd=`, `ck_`/`cs_`, kluczach prywatnych i tokenach GitHub nie znalazł prawdziwych wartości (jedyny dosłowny connection string to `Password=unused` na nieistniejący serwer w teście `StoneFlowApiFactory.cs:15`). gitleaks nie był uruchomiony lokalnie (brak narzędzia). |
| 7. Granice modułów i warstw | OK | CI uruchamia wszystkie testy, w tym `StoneFlow.Architecture.Tests` (`ci.yml:57-58`). Konwencje (`conventions.md:41-53`) opisują granice zgodne ze spec i nie dopisują wzorca `*.Contracts` (konflikt z SF-7 z przeglądu spec respektowany). |
| 8. Brak danych identyfikujących w dokumentach | OK | README używa tylko opisu „sklep z kamieniem naturalnym” i nazwy produktu „Stone Flow”; brak nazwy handlowej, domeny, danych klientów. |

## Conventions Violations

### Critical (must fix before merge)

None.

### Non-Critical (should fix)

1. `README.md:26` i `README.md:30` — sprzeczność w tej samej sekcji: wiersz 26 podaje minimalną wersję IDE („Visual Studio 2026 lub 2022 17.14+”), a wiersz 30 mówi, że „Minimalna wersja Visual Studio obsługująca `.slnx` nie była sprawdzana”. Plan (krok 6) wymaga wersji „sprawdzonej przy implementacji”; README powinno albo podać wersję jako sprawdzoną, albo jako niezweryfikowane założenie — nie oba naraz.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Zachowanie 11: CI na push i PR do `main` | Covered | `ci.yml:3-7`. |
| Zachowanie 11: backend — restore z lockfile, format, build Release z ostrzeżeniami jako błędami, testy z SQL Server w kontenerze | Covered | `ci.yml:48-58`; `ubuntu-latest` ma Dockera dla Testcontainers. |
| Zachowanie 11: frontend — `npm ci`, Prettier, ESLint, build, Vitest | Covered | `ci.yml:84-89`; skrypty istnieją w `app/package.json:4-14`. |
| Typy w testach frontendu (przegląd spec, punkt 10) | Covered | `npm run typecheck` = `tsc -p tsconfig.app.json` + `tsc -p tsconfig.spec.json` (`ci.yml:87`). |
| Przypięcie runtime (przegląd spec, punkt 1) | Covered | `DOTNET_RUNTIME_VERSION` i kontrola `dotnet --list-runtimes` (`ci.yml:18`, `ci.yml:36-43`); patrz Correctness 2. |
| Skan sekretów jako osobne zadanie (przegląd spec, punkt 3) | Covered | Zadanie `secrets`, cała historia (`ci.yml:91-109`). |
| Ochrona `main` (przegląd spec, punkt 4) | Partial | Udokumentowana jako krok ręczny (`README.md:126-147`), zgodnie z planem; nie wykonana — wymaga pierwszego zielonego CI na `main`. |
| Kryterium: CI blokuje błąd typów, formatowania, lintu lub testu (sprawdzone celowo zepsutą gałęzią) | Missing | Brak dowodu wykonania kroku 5 planu — patrz Plan Deviations 1. |
| Kryterium: repozytorium nie zawiera sekretów (skan w CI przechodzi) | Partial | Konfiguracja poprawna, ręczny grep czysty; skan w CI nigdy nie przeszedł, bo CI nie było uruchomione. |
| Zachowanie 1 / kryterium: projekt odtwarzalny według README z zapisanymi wersjami | Covered | Kroki `README.md:35-66` zgodne z repozytorium: `deploy/.env.example`, `deploy/docker-compose.yml`, `.config/dotnet-tools.json` (`dotnet-ef` 10.0.14), `UserSecretsId` w `StoneFlow.Api.csproj`, cztery `--context {Modul}DbContext` z poprawnymi ścieżkami `--project`, profil `http` → `http://localhost:5080` (`launchSettings.json`). Świeży klon nie był odtwarzany w tym przeglądzie. |
| Konfiguracja: `ConnectionStrings:StoneFlow`, `StoneFlow:LaunchDate`, brak daty w `appsettings.json` (przegląd spec, punkt 7) | Covered | `README.md:68-78`; data tylko w `appsettings.Development.json`. |
| Kryterium: `.slnx` otwiera się w Visual Studio (minimalna wersja w README) | Partial | Patrz Conventions 1. |
| Kryterium: TODO w `conventions.md` i `role.md` rozstrzygnięte (przegląd spec, punkt 6) | Covered | Pozostałe TODO (`conventions.md:61`, `:98`, `:113`, `:118`; `role.md:23`) to dokładnie te, które plan (kroki 8–9) zostawia; `rules.md` bez zmian. |

## Plan Deviations

1. Krok 5 planu („Sprawdzenie, że CI blokuje” — cztery gałęzie próbne) nie ma śladu wykonania: w repozytorium lokalnym i zdalnym nie ma gałęzi próbnych, a workflow (`ci.yml:3-7`) uruchamia się tylko na push do `main` i PR do `main`, więc push `sf-3c-ci` na `origin` nie wywołał CI. Nie można było tego sprawdzić z linii poleceń (brak `gh`). Kryterium odbioru pozostaje niezweryfikowane.
2. Kontrola runtime jest w zmiennej `DOTNET_RUNTIME_VERSION` na poziomie workflow, jak w planie; gitleaks jest instalowany z archiwum binarnego z dokładną wersją i SHA-256 zamiast akcji lub obrazu — plan dopuszczał „obraz CLI z dokładnym tagiem”; to wariant równoważny, nie odstępstwo merytoryczne.

## Null Safety Issues

None.

## Correctness Issues

1. `.github/workflows/ci.yml:12-14` — `concurrency.group: ci-${{ github.ref }}` z `cancel-in-progress: true` dotyczy także `main`: dwa szybkie pushe na `main` anulują przebieg pierwszego commita, który zostaje bez wyniku CI. Zwykle stosuje się `cancel-in-progress: ${{ github.event_name == 'pull_request' }}`. Niskie ryzyko.
2. `.github/workflows/ci.yml:36-43` — kontrola sprawdza obecność runtime 10.0.14, a nie to, że testy na nim działają; jeśli runner ma nowszy patch 10.0.x, aplikacje testowe przejdą na niego (domyślny roll-forward do najnowszego patcha). Plan nazywa to ryzyko (sekcja 10), więc to uwaga, nie błąd.

## Test Gaps

1. Testy CI z planu (sekcja 8: błąd formatowania C#, błąd typu w `*.spec.ts`, nieprzechodzący test backendu, fałszywy sekret, zielony przebieg na `main`) nie zostały wykonane albo nie zostawiły śladu — patrz Plan Deviations 1.

## Code Smells

1. `.github/workflows/ci.yml:58` i `:66` — `--results-directory TestResults` z `if-no-files-found: ignore`: jeśli wyniki trafią w inne miejsce (ścieżka względna przy `dotnet test --solution` na Microsoft.Testing.Platform), artefakt będzie po cichu pusty. `warn` pokazałby problem w logu.

## Recommendation

- Merge as-is — krytycznych problemów nie ma. Merge przez PR do `main`, bo dopiero PR uruchamia CI; przed uznaniem kryterium „CI blokuje” za spełnione trzeba otworzyć PR do `main`, uzyskać zielony przebieg trzech zadań, wykonać gałęzie próbne z kroku 5 planu i włączyć ochronę `main`. Poprawki Conventions 1 i Correctness 1 można dołączyć w tym samym PR.
