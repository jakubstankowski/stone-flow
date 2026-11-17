# Code review — SF-4

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Implementacja pokrywa specyfikację i plan: `deploy.yml` publikuje dopiero po CI wywołanym jako workflow wielokrotnego użytku, zadania publikacji są wyłączone zmienną repozytorium `DEPLOY_ENABLED`, migracje biegną w stałej kolejności z zatrzymaniem na pierwszym błędzie (udowodnionym w środku sekwencji w `deploy-dry-run`), a sekrety trafiają wyłącznie do kroków przez zmienne środowiskowe. Uwagi z przeglądu specyfikacji (03) są zaadresowane. Niezmienniki nie są naruszone. Jest jednak błąd, który przy pierwszym włączeniu publikacji oznaczy każde udane wdrożenie jako nieudane: kontrola `/api/auth/csrf` oczekuje `200`, a endpoint zwraca `204`. Do tego dochodzi kilka problemów odporności wysyłki FTPS i uruchamiania migracji. Nic z tego nie zostało uruchomione na prawdziwym hostingu (brak kont), więc te ścieżki sprawdzono tylko czytając kod.
- Risk: high — przed merge wymagany przegląd człowieka (migracje produkcyjnej bazy są nieodwracalne, workflow zarządza sekretami produkcyjnymi).

## Project Invariant Violations

None.

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | Nie dotyczy | Brak logiki finansowej. |
| 2. Backend decimal money | Nie dotyczy | Brak obliczeń kwot. |
| 3. API-enforced access | OK | Autoryzacja bez zmian. Bazowy `app/vercel.json` nie przepisuje `/api/*` (test `vercel-config.test.mjs`). |
| 4. Retries never duplicate | Nie dotyczy | Ponowne uruchomienie migracji jest idempotentne i sprawdzane w `test-migrate.sh` (scenariusz 2). |
| 5. Financial audit trail | Nie dotyczy | Brak zmian danych finansowych. |
| 6. Secrets stay server-side | OK | Połączenie SQL przez `ConnectionStrings__StoneFlow` (`deploy.yml:61`), hasło FTP przez `LFTP_PASSWORD`/`--env-password` (`upload-api.sh:17-19`), brak `set -x`, sekrety tylko w `env` kroków. `appsettings.*.json` środowisk wykluczone z paczki (`StoneFlow.Api.csproj`, kontrola w `publish-api.sh:13-18`). Skan gitleaks `dist/` w CI i `.vercel/output/static` przed publikacją. Hasło SA kontenera CI jest jednorazowe i wyliczane z `run_id`. Uwaga niższej wagi: token Vercel w argumentach procesu (Correctness 4). |
| 7. Module and layer boundaries | OK | Osobny kontekst i schemat na moduł (`contexts.sh`). Zmiana w hoście tylko w potoku HTTP (`HttpsSetup.cs`). |
| 8. No identifying data in docs | OK | `docs/wdrozenia.md`, README i testy używają placeholderów i `example.com`. |

## Conventions Violations

### Critical (must fix before merge)

None.

### Non-Critical (should fix)

1. `README.md:190-191`: zdanie wymienia `deploy-dry-run` na liście wymaganych kontroli, a zaraz potem dodaje „(po pierwszym przebiegu także `deploy-dry-run`)”. Treść jest zdublowana i sprzeczna; zostawić jedno sformułowanie (kontrola musi raz przejść, zanim da się ją wymagać).

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| CI na PR bez zmian zachowania `backend`/`frontend`/`secrets` | Covered | `ci.yml`; instalacja gitleaks przeniesiona do akcji złożonej z tą samą wersją i SHA256. |
| `frontend`: testy skryptu Vercel + gitleaks `dist/` | Covered | `ci.yml:90-96`. |
| `deploy-dry-run`: paczka API, migracje na pustej bazie, ponownie, błąd, nieosiągalny serwer | Covered | `ci.yml:113-165`, `test-migrate.sh`; dodatkowo błąd w środku sekwencji (uwaga 2 z 03). Paczki `migrations bundle` zastąpione — patrz Plan Deviations. |
| `ci.yml` jako `workflow_call`, bez podwójnego CI na `main`, anulowanie tylko w PR | Covered | `ci.yml:5-16`; grupa `ci-<ref>` różni się od `deploy-production`, więc nie ma zakleszczenia. |
| Publikacja tylko po zielonym CI | Covered | `needs: ci` (`deploy.yml:33`); niejawne `success()` w `if`. |
| Przełącznik `DEPLOY_ENABLED` (zmienna repozytorium) | Covered | `deploy.yml:34,89`; `docs/wdrozenia.md:13,67`. |
| Migracje w kolejności, pierwszy błąd zatrzymuje publikację | Covered | `migrate.sh`, `set -euo pipefail`, kolejność kroków w zadaniu `backend`, `frontend` zależy od `backend`. |
| FTPS z `app_offline.htm`, bez `appsettings.Production.json` i `logs/` | Covered | `upload-api.sh`; odporność — Correctness 2. |
| Kontrola `/health` po publikacji API | Covered | `deploy.yml:81-84`. |
| Frontend: generator, `vercel pull/build/deploy`, gitleaks, kontrole `/`, `/logowanie`, `/api/auth/csrf` | Partial | Kroki są, ale kontrola `/api/auth/csrf` nigdy nie przejdzie (Correctness 1). |
| Współbieżność `deploy-production`, bez anulowania w trakcie | Covered | `deploy.yml:17-19`. |
| Ponowne uruchomienie starego przebiegu nie wdraża starszego kodu (uwaga 6 z 03) | Covered | `check-fresh-commit.sh` przed migracjami, przed wysyłką i w `frontend`. |
| Bazowy `vercel.json`: fallback SPA, bez `/api/*`, `git.deploymentEnabled: false` | Covered | `app/vercel.json`; wzorzec `/((?!api(?:/|$)).*)` jest poprawny w składni path-to-regexp (grupa nieprzechwytująca i lookahead dozwolone). |
| Wariant produkcyjny: `/api/:path*` → origin przed fallbackiem, walidacja originu | Covered | `vercel-config.mjs`; 15 testów `node:test` przechodzi (uruchomione ponownie w tym przeglądzie). |
| HSTS i przekierowanie HTTPS poza Development | Covered | `HttpsSetup.cs`, `Program.cs:41`; test na hoście `api.example.com` (uwaga 3 z 03). |
| Procedura: warunki włączenia, sekrety, ograniczone uprawnienia, awarie, rollback, monitoring | Covered | `docs/wdrozenia.md` sekcje 1-10 (uwagi 1, 4, 5, 7, 8 z 03). |
| Konwencje: expand/contract, zgodność API z poprzednim frontendem, kolejność potoku | Covered | `.sdd/context/conventions.md`. |
| AC1 Oba wdrożenia automatycznie po kontrolach | Partial | Mechanizm jest; działanie na środowiskach otwarte do włączenia `DEPLOY_ENABLED` (decyzja właściciela). |
| AC2 Odświeżenie podstrony SPA | Partial | Konfiguracja testowana; kontrola na Vercel otwarta. |
| AC3 Nieudana migracja zatrzymuje publikację, procedura opisana | Covered | `test-migrate.sh` scenariusz 3, `docs/wdrozenia.md` sekcja 7. |
| AC4 Preview bez dostępu do produkcji | Covered | Bazowy `vercel.json` bez przepisania, sekrety tylko w environment `production`. |
| AC5 Pakiet frontendu bez sekretów | Covered | gitleaks `dist/` (CI) i `.vercel/output/static` (publikacja). |
| AC6 HSTS i przekierowanie (test integracyjny) | Covered | `HttpsTests.cs`. |

## Plan Deviations

1. Migracje przez `dotnet ef database update --no-build` zamiast paczek `migrations bundle` ze specyfikacji. Odstępstwo jest zapisane w planie (krok 1) z uzasadnieniem (czas budowy, ta sama semantyka) i rozwiązuje uwagę 9 z 03. Specyfikacja (`02-spec.md` „Scope”, „Behavior” 3.1, „Verification”) nadal mówi o paczkach i o `deploy/scripts/build-api.sh` (`02-spec.md:252`), a skrypt nazywa się `publish-api.sh`. Manifest `sdd-done` powinien to odnotować.
2. W zadaniu `frontend` generator `vercel.json` uruchamia się przed `vercel pull`, a plan podaje odwrotną kolejność. Bez wpływu na wynik.

## Null Safety Issues

None. Skrypty sprawdzają wymagane zmienne (`:?`, `-z`), a generator odrzuca brak lub pustą wartość originu.

## Correctness Issues

1. **Wysoka — `.github/workflows/deploy.yml:141` + `deploy/scripts/wait-for-url.sh:11`: kontrola `/api/auth/csrf` nigdy nie przejdzie.** `wait-for-url.sh` przyjmuje wyłącznie `200`, a `GET /api/auth/csrf` zwraca `204 No Content` (`api/src/Modules/Access/StoneFlow.Access.Api/Endpoints/AuthEndpoints.cs:41-45`, `TypedResults.NoContent()`). Po włączeniu publikacji każde udane wdrożenie skończy się czerwonym zadaniem `frontend` po 10 próbach (~2,5 min), już po `vercel deploy --prod`. Błąd pochodzi ze specyfikacji („każde `200`”), ale to kod musi działać z rzeczywistym endpointem. Trzeba przyjmować `204` dla tego adresu (parametr oczekiwanego statusu) albo sprawdzać inny anonimowy `GET`, który zwraca `200`.
2. **Średnia — `deploy/scripts/upload-api.sh:28-29`: brak odczekania po `app_offline.htm`.** ANCM zatrzymuje aplikację in-process asynchronicznie po wykryciu pliku. `mirror` rusza od razu i nadpisuje `StoneFlow.Api.dll` oraz zależności, które w tej chwili mogą być jeszcze zablokowane przez `w3wp`. FTP odpowie wtedy `550`, `net:max-retries` dotyczy tylko błędów sieci, a `cmd:fail-exit` przerwie skrypt i zostawi `app_offline.htm`, czyli API wyłączone do ręcznej interwencji. Potrzebna krótka pauza (`sleep` w lftp) albo ponowienie `mirror` po pierwszym niepowodzeniu.
3. **Średnia — `deploy/scripts/migrate.sh:24-27`: migracje produkcyjne biegną w środowisku Development.** Bez `ASPNETCORE_ENVIRONMENT`/`DOTNET_ENVIRONMENT` i bez `--environment` narzędzie `dotnet ef` ustawia domyślnie środowisko `Development`. W `Microsoft.EntityFrameworkCore.Design` 10.0.14 jest literał `"Development"` obok `ASPNETCORE_ENVIRONMENT`/`DOTNET_ENVIRONMENT`, co zgadza się ze znanym zachowaniem `AppServiceProviderFactory`. Host migracji ładuje wtedy `appsettings.Development.json` z wyjścia builda (`Email:Mode=Log`, przykładowy właściciel), user secrets i walidację DI właściwą dla Development. Połączenie i tak nadpisuje zmienna środowiskowa, więc dziś to działa, ale produkcyjna operacja zależy od konfiguracji deweloperskiej, a lokalny wpis `ConnectionStrings` w pliku Development, przy pustym sekrecie, byłby przechwycony tylko przez kontrolę `-z`. Środowisko trzeba ustawić jawnie (np. `--environment Production` albo zmienna w skrypcie). `test-migrate.sh` ma działać w tym samym trybie co publikacja.
4. **Niska — `.github/workflows/deploy.yml:117,122,133`: `VERCEL_TOKEN` w argumentach procesu (`--token=...`).** GitHub maskuje wartość w logach, ale token jest widoczny w liście procesów runnera. To niespójne z zasadą z `docs/wdrozenia.md:77-78` („przez zmienne środowiskowe, nie argumenty”), która wymienia tylko SQL i FTP. Niezmiennik nie jest naruszony (runner jest efemeryczny), ale warto sprawdzić, czy przypięta wersja CLI czyta token ze zmiennej `VERCEL_TOKEN`, i wtedy z argumentu zrezygnować.
5. **Niska — `.github/workflows/deploy.yml:139-141`: `STONEFLOW_APP_URL` nie jest walidowany.** Końcowy ukośnik w zmiennej daje adresy typu `https://<adres>//logowanie`. Origin API jest walidowany, adres aplikacji nie.
6. **Niska — `deploy/scripts/test-migrate.sh:29`: `connection_for`.** Gdy `Database=` stoi na początku łańcucha, wynik zaczyna się od `;`. Klucz ze spacjami wokół `=` nie zostanie usunięty, a flaga `I` w `sed` działa tylko w GNU sed. W CI i w przykładzie z README łańcuch nie ma `Database`, więc problem dziś nie występuje.

Sprawdzone bez uwag: niejawne `success()` w `if` zadań zależnych od workflow wielokrotnego użytku; kontekst `vars` w `if` i `environment.url`; `github.event_name` w wywołanym `ci.yml` (to zdarzenie wywołującego, więc `cancel-in-progress` jest `false` na `main`); wyrażenia `${{ github.run_id }}` w `services.*.env`; `job.services.mssql.id`; ścieżka akcji złożonej `./.github/actions/setup-gitleaks` po checkout i wyjście `path` jako ścieżka bezwzględna (działa przy `working-directory: app`); składnia lftp (`--env-password`, `mkdir -pf`, `put … -o`, `mirror --reverse --delete --exclude ^logs/`, wykluczenia chroniące pliki przed `--delete`); `set -e` w `if`/`||` w `test-migrate.sh`; podział `$SQLCMD` na słowa (zamierzony); zewnętrzne przepisanie Vercel i pierwszeństwo plików statycznych; `UseHsts` przed `UseHttpsRedirection` za IIS in-process (schemat z IIS, `HttpsPort = 443`); klucze Data Protection w bazie (`PersistKeysToDbContext`), więc `mirror --delete` ich nie usuwa; brak `EnableRetryOnFailure`, więc scenariusz z nieosiągalnym serwerem kończy się szybko; `vercel@64.7.0` istnieje w rejestrze npm. `bash -n` przechodzi dla wszystkich skryptów w `deploy/scripts/`.

## Test Gaps

1. Nie ma testu, który złapałby Correctness 1: kontrole po publikacji (`wait-for-url.sh` na rzeczywistych odpowiedziach API) nie są sprawdzane w CI. Lokalne wywołanie na hoście testowym (`/health` → 200, `/api/auth/csrf` → 204) wykryłoby rozbieżność.
2. `upload-api.sh` (skrypt lftp) nie jest uruchamiany nigdzie. Plan to przyznaje (Risks), ale scenariusz z lokalnym serwerem FTPS w kontenerze sprawdziłby składnię, wykluczenia i zachowanie `--delete` bez konta hostingowego.
3. `actionlint` i `shellcheck` nie zostały uruchomione (brak narzędzi u autora). Workflow sprawdzono tylko przez przegląd; pierwszy przebieg CI na PR jest pierwszą faktyczną walidacją składni `deploy.yml` (który na PR się nie uruchamia — jego składnię zweryfikuje dopiero push na `main`).
4. Brak testu `check-fresh-commit.sh` i `wait-for-url.sh` (proste, niska waga).
5. Gitleaks na całej historii (`secrets`) nie był uruchamiany lokalnie dla nowych plików. Wynik zobaczymy dopiero w CI.

## Code Smells

1. `deploy/scripts/migrate.sh:28` w połączeniu z `test-migrate.sh:60-62,69-72`: oczekiwane porażki w scenariuszach 3 i 4 wypisują `::error::` i tworzą czerwone adnotacje w zielonym zadaniu `deploy-dry-run`. To utrudnia odróżnienie prawdziwego błędu. Do rozważenia: tryb wyciszenia adnotacji w próbie albo opis w logu, że błąd jest oczekiwany.
2. Kroki `checkout → setup-dotnet → tool restore → restore → build` są powielone w `ci.yml` (`backend`, `deploy-dry-run`) i `deploy.yml` (`backend`). Duplikacja jest akceptowalna, ale zmiana wersji akcji wymaga edycji w trzech miejscach.

## Recommendation

- Fix critical issues before merge. Przed merge poprawić Correctness 1 (oczekiwany status `/api/auth/csrf`). Correctness 2 i 3 poprawić przed włączeniem `DEPLOY_ENABLED`. Ze względu na ryzyko high wymagany przegląd człowieka: `deploy.yml` (warunki, sekrety, uprawnienia), kolejność migracji i procedura błędu, `docs/wdrozenia.md`.
