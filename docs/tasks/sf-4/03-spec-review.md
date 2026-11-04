# Spec review — SF-4

## Summary

- Overall assessment: PASS WITH ISSUES
- Justification: Specyfikacja pokrywa wszystkie punkty zakresu i kryteria odbioru zgłoszenia na poziomie mechanizmu, a decyzję właściciela z 04.11.2026 („narazie odpuszczamy hosting robimy bez”) przekłada na wyłączony przełącznikiem proces, który nie konfiguruje środowisk i nie rozstrzyga D-08 w kodzie — to mieści się w blokadzie D-08 („blokuje ostateczną konfigurację środowisk”). Żaden niezmiennik nie jest naruszony, a twierdzenia techniczne (reusable workflow, `cancel-in-progress: false`, `git.deploymentEnabled`, paczki migracji EF, `app_offline.htm`, osobna transakcja na migrację) są co do zasady poprawne. Zostają luki, które planista musiałby rozstrzygać sam w obszarze ryzyka high: punkt zakresu „ograniczone uprawnienia” jest opisany tylko regułą gałęzi environment, dowód zatrzymania na błędzie migracji dotyczy wyłącznie błędu połączenia, test HSTS zderzy się z domyślnym wykluczeniem `localhost`, a HTTPS domyślnego adresu API na hostingu jest niejawnym założeniem. Kryteria wymagające działających środowisk pozostaną otwarte, więc SF-4 nie da się zamknąć jako w pełni odebranego.

## Findings

### Critical Issues

None.

### Non-Critical Issues

1. **„Ograniczone uprawnienia do produkcji i sekretów wdrożeniowych” opisane tylko częściowo** („Scope”, „Assumptions” Q5, „Acceptance Criteria”). Zgłoszenie i PRD sekcja 10 wymieniają ten punkt wprost. Specyfikacja zapewnia regułę gałęzi `main` dla environment `production` i alternatywę bez environment, ale nie określa: kto może zmieniać sekrety i zmienne environment oraz `DEPLOY_ENABLED`; jawnych `permissions` w `deploy.yml` (np. `contents: read`, bez `id-token`); zakresu tokenu Vercel (projekt/zespół, termin ważności); zakresu konta FTP (tylko katalog API); rozdzielenia uprawnień logowania SQL — `PRODUCTION_MIGRATIONS_CONNECTION` (DDL) vs połączenie aplikacji w `appsettings.Production.json` (bez DDL, o ile hosting pozwala na drugiego użytkownika). Brak też kryterium odbioru dla tego punktu. Planista powinien dostać przynajmniej listę wymagań do procedury w `docs/wdrozenia.md`.
2. **Dowód „nieudana migracja zatrzymuje publikację” obejmuje tylko błąd połączenia** („Kontrole na PR”, punkt 3.4). Nieosiągalny serwer sprawdza zatrzymanie skryptu na pierwszej paczce, ale nie przypadek z kryterium: migracja, która zaczyna się wykonywać i kończy błędem SQL w środku łańcucha (np. druga lub trzecia paczka), oraz zachowanie „wcześniejsze konteksty zmigrowane, kolejne nieuruchomione” z „Edge Cases”. Warto wymagać scenariusza z błędem w środku kolejności (np. kolizja obiektu w schemacie jednego modułu na przygotowanej bazie albo login bez uprawnień do jednego schematu) i sprawdzenia, że kolejne paczki się nie uruchomiły. Sam fakt, że zadania publikacji nie startują po błędzie, wynika ze struktury `needs`/kroków i nie jest testowany — wystarczy to nazwać.
3. **Test HSTS a domyślne wykluczenia** („HTTPS w API”, kryterium „API poza Development wysyła HSTS…”). `UseHsts()` nie wysyła nagłówka dla hostów `localhost`, `127.0.0.1` i `[::1]` (`HstsOptions.ExcludedHosts`) i tylko na żądaniach HTTPS. Testy integracyjne używają `https://localhost` (`StoneFlowApiFactory`), więc test w obecnej postaci nie zobaczy nagłówka. Specyfikacja powinna wskazać sposób (inny `Host` w żądaniu testowym albo jawna konfiguracja `HstsOptions` — bez osłabiania produkcji) oraz parametry HSTS (`max-age`, bez `includeSubDomains` i `preload` na współdzielonej domenie dostawcy). Trzeba też ustalić port dla `UseHttpsRedirection` w testach (bez znanego portu HTTPS middleware tylko loguje ostrzeżenie i nie przekierowuje) oraz miejsce obu middleware w kolejności potoku z `conventions.md` (np. przed `UseRouting`, po obsłudze wyjątków).
4. **HTTPS na domyślnym adresie API dostawcy — niejawne założenie** („Assumptions” Q8, Q12, „Konfiguracja Vercel”). Generator wymaga `https://` w `STONEFLOW_API_ORIGIN`, a Q12 zakłada, że „TLS kończy IIS hostingu”. Bez własnej domeny nie potwierdzono, że tymczasowy adres SmarterASP.NET ma ważny certyfikat. Jeśli nie ma, publikacja frontendu zatrzyma się na generatorze, a przepisanie Vercel → API nie zadziała. Założenie powinno być jawne i dopisane do warunków włączenia `DEPLOY_ENABLED` (obok D-08 i D-05).
5. **Zgodność nowego API ze starym frontendem** („Publikacja”, „Edge Cases” — `/health` zwraca `503`). Kolejność backend → frontend oznacza, że po publikacji API, a przy błędzie kontroli `/health` lub kroku Vercel na dłużej, nowe API obsługuje poprzedni frontend. Specyfikacja wymaga expand/contract tylko dla migracji względem poprzedniego kodu; tę samą regułę trzeba wypowiedzieć dla kontraktu API względem poprzedniego frontendu i dopisać obie do `conventions.md` (specyfikacja wymienia ten plik w „Context”, ale nie mówi, co się w nim zmienia).
6. **Ponowne uruchomienie starszego przebiegu** („Edge Cases”). „Re-run” wcześniejszego przebiegu `deploy.yml` (albo trzy szybkie pushe: GitHub trzyma tylko jeden oczekujący przebieg w grupie współbieżności, więc środkowy zostanie anulowany) może opublikować starszy commit po nowszym — starszy kod na nowszym schemacie. Brakuje reguły: czy przebieg ma sprawdzać, że publikowany commit jest bieżącym `HEAD` `main`, czy procedura dopuszcza świadomy rollback kodu tylko w ten sposób. Opis przypadku „dwa szybkie pushe” jest poprawny, ale niepełny.
7. **Wymagania kryteriów zgłoszenia pozostaną otwarte** („Acceptance Criteria”, „Dependencies”). Kryteria 1 i 2 (oraz sesja przez Vercel przekazana z SF-6) wymagają działających środowisk; PRD sekcja 11 („oba hostingi są zweryfikowane”) też. To konsekwencja decyzji właściciela, a nie błąd specyfikacji, ale plan i `sdd-done` muszą to zapisać jako otwarte pozycje, a zgłoszenie SF-4 powinno pozostać otwarte w trackerze albo dostać zadanie uzupełniające „włączenie publikacji po D-08”. Specyfikacja tego nie rozstrzyga.
8. **Warunek włączenia publikacji jest wyłącznie proceduralny** („Assumptions” Q1, D-05). „`DEPLOY_ENABLED` wolno ustawić dopiero po zamknięciu D-08” oraz wymóg konfiguracji SMTP (walidator `EmailOptions` odrzuca `Mode=Log` poza Development) są zapisane tylko w procedurze. Warto, by `docs/wdrozenia.md` miał listę kontrolną warunków włączenia (D-08 zamknięte, D-05 wybrany dostawca, HTTPS adresu API, zdalny dostęp SQL, metoda publikacji, runtime 10.0.x, Data Protection), a decyzja właściciela z 04.11.2026 została odnotowana poza specyfikacją (SF-1 lub rejestr D-08), bo rejestr decyzji w PRD nadal jej nie zawiera.
9. **Przekazanie połączenia do paczek migracji** („Publikacja”, 3.2; „Invariant impact” 6). Nie określono, czy połączenie trafia przez argument `--connection` (widoczny w liście procesów runnera i łatwy do wypisania w `set -x`/komunikacie błędu), czy przez zmienną środowiskową `ConnectionStrings__StoneFlow`. Paczki budują host aplikacji (brak fabryk design-time), więc zmienna środowiskowa działa i lepiej pasuje do niezmiennika 6; warto to przesądzić.

### Unclear or Ambiguous Sections

- **„Types crossing a boundary” vs „Scope”** — `DEPLOY_ENABLED` jest raz „zmienną repozytorium”, raz „zmienną repozytorium/environment”. Warunek `if` zadania jest liczony przed wejściem w environment, więc zmienna environment nie jest w nim dostępna; musi to być zmienna repozytorium (lub organizacji). Należy to ujednolicić.
- **„Kontrole na PR”, punkt 4** — sformułowanie „anulowanie poprzedniego przebiegu dotyczy tylko PR” wymaga zmiany obecnego `cancel-in-progress: true` w `ci.yml` (np. warunek na `github.event_name`; w wywołaniu `workflow_call` kontekst `github` pochodzi z wywołującego, więc to działa) i usunięcia wyzwalacza `push` na `main` z `ci.yml`. Specyfikacja mówi o skutku, nie o zmianie; warto też potwierdzić, że grupa współbieżności `ci.yml` ma inną nazwę niż `deploy-production` (ta sama nazwa w wywołującym i wywoływanym przepływie blokuje przebieg). Nowe zadanie `deploy-dry-run` powinno trafić do wymaganych kontroli gałęzi `main` — ustawienie poza repozytorium, do opisania w procedurze.
- **„Publikacja”, 3.3** — „synchronizuje pliki przez FTPS (usuwa nieaktualne), nie dotyka `appsettings.Production.json` ani katalogu `logs/`”: lista wykluczeń przy usuwaniu musi obejmować też `app_offline.htm` (inaczej synchronizacja z usuwaniem zdejmie go w trakcie wysyłki albo nadpisze). Nie określono też, czy `web.config` z publikacji (z ustawieniem logów stdout z Q11) zawsze nadpisuje plik na serwerze.
- **„Konfiguracja Vercel”** — „fallback SPA do `index.html` dla ścieżek innych niż `/api/*`”: dobrze byłoby nazwać, że pliki statyczne mają pierwszeństwo przed `rewrites` (zachowanie Vercel), a wykluczenie `/api` to wzorzec z negacją, oraz który katalog wyjściowy (`dist/<projekt>/browser`) jest `outputDirectory`. Testy `node --test` powinny sprawdzać kolejność reguł (przepisanie `/api` przed fallbackiem) i odrzucenie originu ze ścieżką, zapytaniem, danymi logowania lub `http://`.
- **„Assumptions” Q6** — wersja `vercel@64.7.0` nie jest potwierdzona żadnym źródłem w repozytorium; planista powinien ją sprawdzić w rejestrze npm przed przypięciem (zasada przypinania wersji z `conventions.md`).
- **„Assumptions”, framework-dependent** — publikacja na Ubuntu dla hostingu Windows/IIS: nie określono, czy paczka ma być przenośna (bez RID), ani modelu hostingu ANCM (`inprocess`/`outofprocess`) i bitowości puli. Do potwierdzenia razem z D-08, ale warto to wypisać jako założenie.

### Invented or Unsupported Requirements

None. Elementy spoza dosłownego brzmienia zgłoszenia (HSTS i przekierowanie, skan `dist/`, `node --test` generatora, logi stdout ANCM, Better Stack) wynikają z punktów zakresu „HTTPS i monitoring”, „Pakiet frontendu nie zawiera sekretów”, z PRD sekcja 10 „Obserwowalność” albo z odpowiedzi właściciela.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Assumptions Q1, Out of scope | PRD D-08, `rules.md` („Do not implement an area blocked by an unapproved PRD decision”) | Brak konfliktu przy obecnym brzmieniu: powstaje mechanizm wyłączony przełącznikiem, bez konfiguracji środowisk i bez rozstrzygania parametrów konta. Warunek: `DEPLOY_ENABLED` nie zostanie ustawione przed zamknięciem D-08 (patrz Non-Critical 8). Wybory zależne od hostingu (FTPS, zdalny SQL, runtime, ANCM) są oznaczone jako niepotwierdzone. |
| Assumptions D-05 | PRD D-05 („Do potwierdzenia: dostawca wysyłki e-mail”) | Brak konfliktu: wybór odłożony decyzją właściciela, zapisany jako warunek włączenia publikacji. |
| Acceptance Criteria | PRD sekcja 11 („oba hostingi są zweryfikowane, SPA działa po odświeżeniu podstrony, migracja schematu bazy … sprawdzone”) | Brak sprzeczności, ale warunki odbioru MVP pozostaną niespełnione po SF-4 (patrz Non-Critical 7). |
| HTTPS w API | `conventions.md` — kolejność potoku w `Program.cs` | Konwencja wymienia pełną kolejność middleware; specyfikacja nie mówi, gdzie wchodzą `UseHsts`/`UseHttpsRedirection`, więc konwencję trzeba będzie zaktualizować. |
| Assumptions Q12 | SF-6 `06-implementation-done.md`, Open Items 3 („zaufane proxy dla limitów żądań”) | Brak konfliktu: decyzja „nie włączamy `ForwardedHeaders`” jest jawna i uzasadniona; skutek (limit per adres wyjściowy Vercel) przyjęty świadomie. |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | Not applicable | Zadanie nie dotyka logiki finansowej. |
| 2. Backend decimal money | Not applicable | Brak obliczeń kwot. |
| 3. API-enforced access | OK | Autoryzacja bez zmian; `/health` i `/api/auth/csrf` już anonimowe; preview bez przepisania `/api/*`. |
| 4. Retries never duplicate | Not applicable | Brak webhooków i zadań cyklicznych; ponowne uruchomienie migracji jest idempotentne i testowane. |
| 5. Financial audit trail | Not applicable | Brak zmian danych finansowych. |
| 6. Secrets stay server-side | OK | Sekrety w GitHub Environment i na serwerze, frontend bez zmiennych, skan `dist/` i paczki Vercel, brak `set -x`. Uwaga: sposób przekazania połączenia do paczek migracji (Non-Critical 9). |
| 7. Module and layer boundaries | OK | Paczka migracji i schemat per kontekst; zmiana w hoście tylko w potoku HTTP; domena nie zależy od hostingu. |
| 8. No identifying data in docs | OK | Placeholdery w dokumentacji, prawdziwe adresy tylko w zmiennych GitHub. |

## Risk

- Recorded: high
- Assessed: high — migracje produkcyjnej bazy są nieodwracalne (PRD sekcja 10: rollback kodu nie cofa migracji), a zadanie zarządza sekretami produkcyjnymi i dostępem do bazy. Wymagany przegląd człowieka przed merge.

## Assumptions Detected

| Założenie | Zapisane w specyfikacji |
|---|---|
| Publikacja wyłączona do zamknięcia D-08 (Q1) | Tak |
| FTPS przez `lftp` dostępne na koncie (Q2) | Tak, jako niepotwierdzone |
| Zdalny dostęp do SQL hostingu z runnerów GitHub (zmienne IP) (Q3) | Tak, otwarte |
| Kopia przed migracją w SF-21 (Q4) | Tak |
| Automatyczna publikacja po zielonym CI, reguły environment zależne od planu GitHub (Q5) | Tak |
| Vercel CLI z GitHub Actions, wersja `64.7.0` (Q6) | Tak; wersja niezweryfikowana |
| Preview bez API i bez stagingu (Q7) | Tak |
| Domyślne adresy dostawców (Q8) | Tak |
| Domyślny adres API SmarterASP.NET ma ważny certyfikat HTTPS | **Nie** — niejawne (Q12 zakłada TLS w IIS) |
| Zewnętrzne przepisanie Vercel przekazuje ciasteczka, `Set-Cookie` i nagłówki CSRF bez zmian | **Nie** — niejawne; sprawdzane dopiero kontrolą `/api/auth/csrf` po publikacji |
| Better Stack, konfiguracja ręczna (Q9) | Tak |
| Tymczasowa data uruchomienia (Q10) | Tak |
| Logi stdout ANCM w `logs/` bez rotacji (Q11) | Tak |
| Bez `ForwardedHeaders`, limity per adres Vercel (Q12) | Tak |
| SMTP wymagany do startu produkcji (D-05) | Tak |
| Klucze Data Protection nieszyfrowane do konfiguracji hostingu | Tak |
| Runtime 10.0.x na hostingu, paczka framework-dependent | Tak; brak RID/modelu ANCM |
| Paczki migracji budują host bez uruchamiania walidacji startowych (`ValidateOnStart` działa dopiero przy starcie hosta) | Częściowo — „konfiguracja hosta” zapisana, skutek dla walidacji opcji nie |
| Każda migracja EF w osobnej transakcji | Tak (poprawne dla domyślnego zachowania; migracje z operacjami poza transakcją są wyjątkiem) |
| Nowe API zgodne z poprzednim frontendem w oknie publikacji | **Nie** (Non-Critical 5) |
| `DEPLOY_ENABLED` jako zmienna repozytorium, nie environment | Niejednoznaczne |

## Recommendation

- Proceed as-is — planowanie może ruszyć; Non-Critical 1–5 i niejasności dotyczące `DEPLOY_ENABLED`, `cancel-in-progress` i wykluczeń synchronizacji FTPS warto domknąć w specyfikacji albo jawnie w planie. Przy ryzyku high przegląd człowieka przed merge jest obowiązkowy.
