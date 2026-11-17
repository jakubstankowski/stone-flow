# Implementation Plan — SF-3 (część c)

## 0. Część

- **Litera:** c — CI, skan sekretów, instrukcja startu, ochrona gałęzi, kontekst projektu.
- **Dostarcza:** workflow GitHub Actions (backend, frontend, sekrety) z kontrolą wersji
  runtime, `README.md` z instrukcją startu, udokumentowane włączenie wymaganych kontroli na
  `main` oraz uzupełnienie TODO w `.sdd/context/conventions.md` i `role.md`.
- **Wymaga wcześniejszego merge:** a i b.

## 1. Zakres

Część realizuje z `02-spec.md`: „Zachowanie”, punkt 11 (CI) i punkt 1 (instrukcja startu),
kryteria „CI przechodzi … i blokuje”, „Repozytorium nie zawiera sekretów”, „Projekt da się
odtworzyć … z zapisanymi wersjami” oraz uzupełnienie TODO. Rozstrzyga w planie punkty 1, 3,
4 i 6 z `03-spec-review.md` zgodnie z decyzjami z 03.11.2026: runtime przypięty przez SDK i
sprawdzany w CI, skan sekretów jako osobne zadanie CI, ochrona `main` jako udokumentowany
krok ręczny, jawna lista TODO zamykanych i pozostawionych. Nie zmienia kodu aplikacji.

## 2. Obszary

| Warstwa | Moduł / funkcja | Dlaczego |
|---|---|---|
| CI | `.github/workflows/ci.yml` | kompilacja, typy, format, lint, testy, sekrety, wersja runtime |
| Repozytorium | `README.md` | instrukcja startu, wersje, konfiguracja, ochrona gałęzi |
| Kontekst projektu | `.sdd/context/conventions.md`, `.sdd/context/role.md` | TODO rozstrzygnięte przez SF-3 |
| Ustawienia GitHub | gałąź `main` | wymagane kontrole (krok ręczny właściciela) |

## 3. Pliki

| Ścieżka | Akcja | Cel |
|---|---|---|
| `.github/workflows/ci.yml` | create | zadania `backend`, `frontend`, `secrets` |
| `.gitleaks.toml` | create | domyślne reguły gitleaks; bez wyjątków poza plikami `*.example` z placeholderami, jeśli wywołają fałszywy alarm |
| `README.md` | create | opis repozytorium, wymagania, start lokalny, konfiguracja, testy, CI, ochrona `main` |
| `.sdd/context/conventions.md` | modify | ścieżki, testy, struktura modułu, nazewnictwo, komendy weryfikacji |
| `.sdd/context/role.md` | modify | wersje .NET i Angular |

## 4. Kroki implementacji

1. [ ] **Workflow `ci.yml`:** wyzwalacze `push` i `pull_request` na `main`;
   `permissions: contents: read`; `concurrency` per gałąź z anulowaniem poprzednich przebiegów;
   akcje przypięte do pełnego SHA z komentarzem wersji.
2. [ ] **Zadanie `backend`** (`ubuntu-latest`, Docker dostępny dla Testcontainers):
   - `actions/setup-dotnet` z `global-json-file: global.json`;
   - kontrola runtime: krok kończy się błędem, jeśli `dotnet --list-runtimes` nie zawiera
     `Microsoft.NETCore.App 10.0.14` i `Microsoft.AspNetCore.App 10.0.14` (przegląd, punkt 1);
     wersja w zmiennej `DOTNET_RUNTIME_VERSION` na początku workflow;
   - `dotnet tool restore`;
   - `dotnet restore stone-flow.slnx --locked-mode`;
   - `dotnet format stone-flow.slnx --verify-no-changes --no-restore`;
   - `dotnet build stone-flow.slnx -c Release --no-restore` (ostrzeżenia jako błędy z
     `Directory.Build.props`);
   - `dotnet test --solution stone-flow.slnx -c Release --no-build` z wynikami testów jako
     artefakt.
3. [ ] **Zadanie `frontend`** (`working-directory: app`):
   - `actions/setup-node` z `node-version-file: app/.nvmrc` i cache npm na
     `app/package-lock.json`;
   - `npm ci`, `npm run format:check`, `npm run lint`, `npm run typecheck`, `npm run build`,
     `npm test -- --watch=false`.
4. [ ] **Zadanie `secrets`:** checkout z `fetch-depth: 0`; gitleaks (akcja
   `gitleaks/gitleaks-action` przypięta do SHA albo obraz `ghcr.io/gitleaks/gitleaks` z
   dokładnym tagiem) skanuje całą historię; wykrycie = czerwone CI. `.gitleaks.toml` rozszerza
   reguły domyślne.
5. [ ] **Sprawdzenie, że CI blokuje:** na gałęzi roboczej kolejno: błąd formatowania C#, błąd
   typu w `*.spec.ts`, nieprzechodzący test backendu, fałszywy sekret w pliku testowym → każdy
   przebieg czerwony we właściwym zadaniu; zmiany cofnięte, gałąź usunięta.
6. [ ] **`README.md`** (bez nazwy handlowej i domeny sklepu, bez sekretów):
   - opis: Stone Flow, układ katalogów (`api/`, `app/`, `deploy/`, `docs/`);
   - wymagania z dokładnymi wersjami: .NET SDK 10.0.403 (runtime 10.0.14), Node z
     `app/.nvmrc`, npm, Docker, Visual Studio w wersji obsługującej `.slnx` (minimalna wersja
     sprawdzona przy implementacji) lub `dotnet` CLI; informacja, że wersje pochodzą z
     odczytu SF-1 i mogą zostać skorygowane po zamknięciu D-08;
   - start lokalny: `deploy/.env` z `deploy/.env.example`; `docker compose -f
     deploy/docker-compose.yml --env-file deploy/.env up -d`; `dotnet tool restore`;
     `dotnet user-secrets set "ConnectionStrings:StoneFlow" "<...>" --project
     api/src/StoneFlow.Api`; cztery polecenia `dotnet ef database update --context
     {Modul}DbContext`; `dotnet run --project api/src/StoneFlow.Api`; `cd app && npm ci &&
     npm start`;
   - konfiguracja: tabela kluczy (`ConnectionStrings:StoneFlow`, `StoneFlow:LaunchDate` w
     `yyyy-MM-dd`), skąd pochodzą (user-secrets, zmienne środowiskowe, wzór
     `deploy/api.env.example`), zasada: data uruchomienia jest ustawiana tylko w konfiguracji
     wdrożenia i nie ma jej w `appsettings.json`;
   - endpointy diagnostyczne: `/health`, `/openapi/v1.json` (Development);
   - testy i weryfikacja: komendy z sekcji 9 planów a i b;
   - CI: opis trzech zadań;
   - ochrona `main`: krok ręczny (kroki 7).
7. [ ] **Ochrona `main`** (decyzja z 03.11.2026; ustawienie GitHub, nie kod): README zawiera
   instrukcję dla właściciela repozytorium — w ustawieniach repozytorium albo poleceniem
   `gh api` — wymagane kontrole `backend`, `frontend`, `secrets`, wymóg aktualnej gałęzi przed
   merge. Wykonuje właściciel po pierwszym zielonym przebiegu CI na `main` (nazwy kontroli
   muszą już istnieć). Agent nie zmienia ustawień repozytorium bez potwierdzenia.
8. [ ] **`.sdd/context/conventions.md`** — zamknięte TODO (przegląd, punkt 6):
   - tabela układu: `app/` (frontend), `api/src` (backend), `api/tests` (testy backendu;
     testy frontendu obok kodu w `app/src`), `deploy/` (konfiguracja wdrożeń; w SF-3 tylko
     lokalna);
   - „Tests”: xUnit v3 na Microsoft.Testing.Platform, NetArchTest, Testcontainers; Vitest;
   - „example path for a new feature”: struktura modułu
     `api/src/Modules/{Modul}/StoneFlow.{Modul}.{Domain|Application|Infrastructure|Api}`;
     handlery w `{Modul}.Application`, `DbContext` i migracje w
     `{Modul}.Infrastructure/Persistence`;
   - „Naming” — częściowo: projekty i przestrzenie nazw `StoneFlow.{Modul}.{Warstwa}`, schemat
     SQL = nazwa modułu małymi literami; trasy i styl endpointów **pozostają TODO** (SF-6);
   - frontend „file and folder naming”: przewodnik stylu Angular CLI 22, obszary w
     `app/src/app/features/{obszar}`;
   - „Verification”: komendy build, test, lint/format z planów a i b.

   Pozostają TODO (z uzasadnieniem w treści, nie usuwane): podejście do stanu na froncie
   (pierwsze zadanie z danymi), „Business logic for `sdd-scaffold`”, „Implementation order”.
   Wzorzec komunikacji między modułami (`*.Contracts`) **nie** jest dopisywany — rozstrzyga go
   SF-7 (konflikt z przeglądu spec).
9. [ ] **`.sdd/context/role.md`** — zamknięte TODO: .NET 10 (SDK 10.0.403, runtime 10.0.14),
   Angular 22.2.x, Node 24 — z adnotacją „według SF-1, do potwierdzenia po zamknięciu D-08”.
   Pozostaje TODO wersji Tailwind CSS i daisyUI (SF-5). `rules.md` (długość pliku) bez zmian.

## 5. Dane i kontrakty

- Storage: brak.
- Endpointy: brak nowych.
- Typy na granicy: brak. Nazwy zadań CI `backend`, `frontend`, `secrets` są kontraktem dla
  wymaganych kontroli na `main`.

## 6. Przypadki brzegowe i null safety

| Przypadek | Gdzie obsłużony |
|---|---|
| Runner z innym patchem runtime niż 10.0.14 | krok kontroli runtime → czerwone `backend` |
| Lockfile NuGet niezgodny z projektami | `restore --locked-mode` → czerwone `backend` |
| `package-lock.json` niezgodny z `package.json` | `npm ci` → czerwone `frontend` |
| Błąd typu w `*.spec.ts` | `npm run typecheck` |
| Sekret w historii commitów (nie tylko w HEAD) | `fetch-depth: 0` + skan całej historii |
| Placeholder w `*.example` uznany za sekret | `.gitleaks.toml` z wąskim wyjątkiem dla konkretnej reguły i ścieżki |
| Kontrole wymagane przed pierwszym przebiegiem | krok ręczny po pierwszym zielonym CI |
| Pull request z forka | `permissions: contents: read`, brak sekretów w workflow |

## 7. Niezmienniki

| Niezmiennik | Gdzie egzekwowany | Test |
|---|---|---|
| 6. Sekrety po stronie serwera | zadanie `secrets` (gitleaks, cała historia); workflow bez sekretów; README z placeholderami | krok 5: fałszywy sekret → czerwone `secrets` |
| 7. Granice modułów i warstw | CI uruchamia testy architektury z części a | krok 5: czerwony test → czerwone `backend` |
| 8. Brak danych identyfikujących | README i `conventions.md` bez nazwy handlowej, domeny, danych klientów | przegląd kodu |

Niezmienniki 1–5 (poza 3) — nie dotyczy; 3 — bez zmian względem części a.

## 8. Testy

| Test | Typ | Co dowodzi |
|---|---|---|
| Przebieg CI na `main` po merge a, b, c | CI | trzy zadania zielone na czystym repozytorium |
| Błąd formatowania C# na gałęzi próbnej | CI (ręczny) | `backend` blokuje format |
| Błąd typu w `*.spec.ts` | CI (ręczny) | `frontend` blokuje typy w testach |
| Nieprzechodzący test backendu | CI (ręczny) | `backend` blokuje test |
| Fałszywy klucz w pliku | CI (ręczny) | `secrets` blokuje sekret |
| Instrukcja z README na czystym klonie | ręczny | projekt odtwarzalny według instrukcji |

## 9. Weryfikacja

```
gh workflow view ci.yml
gh run list --workflow ci.yml --branch main --limit 1     # status: success
```

Ręcznie:
- Świeży klon w nowym katalogu → kroki z README → `/health` zwraca `Healthy`, aplikacja Angular
  pokazuje „Stone Flow”.
- Cztery gałęzie próbne z kroku 5 → odpowiednie zadania czerwone.
- Po włączeniu ochrony przez właściciela: `gh api repos/{owner}/{repo}/branches/main/protection`
  pokazuje kontrole `backend`, `frontend`, `secrets`.
- `grep -n "TODO" .sdd/context/conventions.md .sdd/context/role.md` → pozostały tylko TODO
  wymienione w krokach 8–9.

## 10. Ryzyka

- **Ochrona gałęzi zależy od właściciela:** dopóki krok 7 nie zostanie wykonany, czerwone CI
  nie blokuje merge technicznie. Kryterium „blokuje” jest spełnione dopiero po nim.
- **gitleaks:** fałszywe alarmy na placeholderach albo licencja akcji dla kont organizacji;
  alternatywą jest obraz CLI z dokładnym tagiem (krok 4).
- **Runtime na runnerze:** `setup-dotnet` instaluje SDK 10.0.403 z runtime 10.0.14; jeśli
  runner ma też inny runtime, kontrola sprawdza obecność 10.0.14, a nie wyłączność.
- **Korekta wersji po D-08:** README i `role.md` wskazują, że wersje mogą się zmienić; korektę
  uruchamia właściciel jako osobne zadanie po zamknięciu D-08.
- **Minimalna wersja Visual Studio dla `.slnx`** ustalana przy implementacji (niejasność z
  przeglądu spec).
