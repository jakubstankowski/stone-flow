# Spec review — SF-3

## Summary

- Overall assessment: PASS WITH ISSUES
- Justification: Specyfikacja pokrywa wszystkie punkty zgłoszenia i kryteria odbioru, poprawnie stosuje odpowiedzi właściciela z 03.11.2026 i nie wchodzi w zakres SF-4, SF-5, SF-6 ani SF-11. Żaden niezmiennik nie jest naruszony; anonimowy `/health` bez danych jest jawnym, uzasadnionym wyjątkiem. Pozostają luki, które planowanie musiałoby zgadywać: brak mechanizmu zapisu wersji runtime (wymaganie wprost ze zgłoszenia), niespójność kroków CI (skan sekretów), niedokładne przypięcie Node i nieweryfikowalne kryterium uzupełnienia TODO. Warto je poprawić przed `sdd-plan`, ale nie blokują planu.

## Findings

### Critical Issues

None.

### Non-Critical Issues

1. **Przypięcie runtime nie ma mechanizmu ani kryterium odbioru.** Zgłoszenie wymaga „Przypięcie SDK, runtime i Node”. Runtime 10.0.14 pojawia się tylko w „Założenia”. „W zakresie” wymienia `global.json` (pinuje tylko SDK) i `Directory.Build.props` z `net10.0` (pinuje tylko wersję główną). „Kryteria odbioru” wyliczają `global.json`, `Directory.Packages.props`, lockfile i `.nvmrc`, bez runtime. Spec powinien określić, gdzie i jak zapisana jest wersja runtime (np. właściwość projektu, README, obraz bazowy) i objąć ją kryterium.
2. **Node nie jest jednoznacznie przypięty.** „Założenia” mówią „Node 24 LTS w wersji spełniającej `^24.15.0`”, ale nie podają dokładnej wersji dla `.nvmrc`. Zakres `^24.15.0` w `engines` nie jest przypięciem. Plik `.npmrc` z `engine-strict` pojawia się tylko w „Przypadki brzegowe”, nie w liście przypiętych plików w „W zakresie”.
3. **Skan sekretów nie występuje w opisie CI.** „Wpływ na niezmienniki” (niezmiennik 6) i „Kryteria odbioru” zakładają skan sekretów w CI (gitleaks wymieniony w „Założenia”), ale punkt 11 w „Zachowanie” wymienia tylko zadania backend i frontend. Trzeba ustalić, czy skan jest osobnym zadaniem CI i co blokuje.
4. **„CI blokuje” — brak informacji o ochronie gałęzi.** Spec rozumie blokowanie jako czerwone CI. Czy w zakresie jest włączenie wymaganych kontroli (branch protection) dla `main`? To ustawienie repozytorium GitHub, a nie kod, więc spec powinien to rozstrzygnąć albo jawnie wyłączyć.
5. **Testy architektury sprawdzają tylko warstwę domeny.** Niezmiennik 7 wymaga, by zależności wskazywały do środka. Spec opisuje testy dla Domain (brak ASP.NET Core, EF Core, Hosting) i dla referencji między modułami. Nie obejmuje reguł dla `*.Application` (brak EF Core, ASP.NET Core i referencji do Infrastructure) ani dla `Shared.Kernel`. Tabela „Może zależeć od” to implikuje, ale wymusza to tylko struktura referencji, bez testu.
6. **Kryterium uzupełnienia TODO jest nieweryfikowalne.** „TODO … rozstrzygnięte przez SF-3 są uzupełnione” jest kryterium cyklicznym. Spec nie wylicza TODO, które SF-3 zamyka, a które zostają (np. styl endpointów odłożony do SF-6, podejście do stanu na froncie, wersje Tailwind/daisyUI w `role.md` należące do SF-5, długość pliku w `rules.md`).
7. **Domyślna wartość daty uruchomienia w konfiguracji bazowej.** Spec mówi, że przykładowa data jest w `appsettings.Development.json`, ale nie zabrania jej w `appsettings.json`. Data w pliku bazowym oznaczałaby, że produkcja wystartuje z datą deweloperską zamiast zatrzymać się na walidacji, czyli wbrew PRD sekcja 4 („datę uruchomienia podaje właściciel przed startem produkcyjnym”).
8. **Uprawnienia SQL na hostingu.** Projekt jednej bazy z czterema schematami i tabelami historii migracji per schemat zakłada, że konto SQL na SmarterASP.NET może tworzyć schematy. SF-1 tego nie potwierdził. Założenie powinno trafić do „Założenia” lub „Zależności” jako ryzyko przekazywane do SF-4 i D-08.
9. **Docelowe miejsce logów.** Logi JSON na konsolę są poprawne lokalnie i w CI. Na hostingu IIS stdout zwykle nie jest zachowywany. Spec nie mówi, że ujście logów produkcyjnych przechodzi do SF-4 (monitoring). PRD sekcja 10 „Obserwowalność” wymaga logów z korelacją.
10. **Typy w testach frontendu.** CI sprawdza typy przez build produkcyjny, który nie kompiluje plików `*.spec.ts`. Vitest domyślnie nie sprawdza typów. Błąd typu w teście nie zatrzyma CI, mimo że zgłoszenie wymaga blokowania błędów typów.
11. **Wyjątek `/health` wyprzedza SF-6.** Task breakdown każe SF-6 ustalić w specyfikacji „listę wyjątków od DOST-01 (webhook, logowanie, reset, health endpoint)”. SF-3 rozstrzyga `/health` jako anonimowy (decyzja właściciela). Jest to poprawne, ale SF-6 musi przejąć ten wyjątek; spec może to zaznaczyć w „Zależności”.

### Unclear or Ambiguous Sections

- **Zakres → W zakresie, `StoneFlow.Shared.Infrastructure`:** „wspólne ustawienia EF Core” nie mówi, co obejmują (konwencje, interceptory, konfiguracja providera). Przy niejasnym zakresie część wspólna może urosnąć wbrew zasadzie „mała, bez logiki modułów”.
- **Zachowanie, punkt 6:** nie wiadomo, co się dzieje z przychodzącym nagłówkiem `X-Correlation-ID` (ignorowany czy przyjmowany). Opisany jest tylko `traceparent`.
- **Zachowanie, punkt 8, i „Wpływ na niezmienniki”:** udostępnienie OpenAPI tylko w Development to decyzja projektowa, której brak w „Założenia”. Lista odpowiedzi właściciela jej nie zawiera.
- **Kryteria odbioru, „`stone-flow.slnx` otwiera się w Visual Studio”:** nie podano minimalnej wersji Visual Studio obsługującej `.slnx`. README powinno ją wymienić, inaczej kryterium zależy od środowiska.
- **Zależności, SF-1 / D-08:** zdanie „wersje mogą wymagać korekty w osobnej zmianie” nie mówi, kto i kiedy tę korektę uruchamia.

### Invented or Unsupported Requirements

None. Lint (ESLint), Vitest, `.slnx`, GitHub Actions, Testcontainers i własny dyspozytor wynikają z odpowiedzi właściciela. Skan sekretów jest opisany jako sposób weryfikacji kryterium „Repozytorium nie zawiera sekretów”. Konwencja przyszłych projektów `StoneFlow.{Modul}.Contracts` (Założenia) jest wybiegnięciem w przyszłość, ale SF-3 jej nie implementuje; patrz konflikty.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Założenia (wersje), Zależności | PRD sekcja 10 „Hosting” i D-08; `rules.md` „Do not implement an area blocked by an unapproved PRD decision”; `docs/tasks/sf-1/01-weryfikacja.md` punkt 1 (patch runtime niepotwierdzony) | Napięcie, nie sprzeczność. D-08 blokuje ostateczną konfigurację środowisk, której SF-3 nie obejmuje. Przypięcie wersji przed zamknięciem SF-1 jest świadomą decyzją właściciela z 03.11.2026, zapisaną tylko w specyfikacji. Ryzyko korekty wersji po D-08 jest nazwane. |
| Założenia („Health”), Endpointy | Task breakdown, zadanie 6 (SF-6): lista wyjątków od DOST-01 do rozstrzygnięcia w specyfikacji SF-6 | Nakładanie zakresu. SF-3 rozstrzyga wyjątek `/health` wcześniej, decyzją właściciela. Bez sprzeczności treści, ale SF-6 musi go uwzględnić. |
| Założenia (`StoneFlow.{Modul}.Contracts`) | Task breakdown, zadanie 7 (SF-7): moduł-właściciel audytu i interfejs zapisu do rozstrzygnięcia w SF-7 | Ryzyko wyprzedzenia. Spec ustala wzorzec komunikacji między modułami (osobny projekt Contracts), zanim SF-7 zaprojektuje pierwszy taki interfejs. Jeśli trafi do `conventions.md`, stanie się wiążący bez decyzji. |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sprzedaż oddzielona od gotówki | Not applicable | Brak logiki finansowej; granica modułów zamówień i wydatków nie łączy pojęć. |
| 2. Kwoty decimal na backendzie | Not applicable | Brak kwot; frontend nie liczy. |
| 3. Dostęp wymuszany przez API | OK | Brak endpointów z danymi. `/health` anonimowy, bez danych i szczegółów infrastruktury (decyzja właściciela; zgodny z listą kandydatów na wyjątki w SF-6). OpenAPI tylko w Development. Uwierzytelnianie w SF-6, przed pierwszym endpointem z danymi (SF-8). |
| 4. Ponowienia bez duplikatów | Not applicable | Brak importu i zadań cyklicznych. |
| 5. Audyt zmian finansowych | Not applicable | Brak danych finansowych. |
| 6. Sekrety po stronie serwera | OK | Connection string przez `user-secrets` lub zmienne środowiskowe, hasło SA z ignorowanego `.env`, tylko pliki przykładowe w repozytorium; Problem Details i `/health` bez szczegółów. Skan w CI wymaga dopisania do „Zachowanie” (Non-Critical 3). |
| 7. Granice modułów i warstw | OK | Wymuszone referencjami projektów i testami architektury dla domeny i referencji między modułami; brakuje testów dla warstwy Application (Non-Critical 5). |
| 8. Brak danych identyfikujących w dokumentach | OK | README i przykłady bez nazwy handlowej, domeny, danych klientów i sekretów; nazwa produktu „Stone Flow” nie identyfikuje sklepu. |

## Risk

- Recorded: medium
- Assessed: medium. Brak pieniędzy, danych klientów i nieodwracalnych operacji. Struktura modułów, granice warstw, przypięte wersje i CI są współdzielone przez wszystkie kolejne zadania, więc wymagany jest przegląd pod kątem modułów-konsumentów. Migracje dotyczą tylko lokalnej bazy; migracje środowiskowe należą do SF-4 (high).

## Assumptions Detected

- Wersje .NET SDK 10.0.403 / runtime 10.0.14, Angular 22.2.x, Node 24 przy otwartym D-08 — podane (odpowiedź właściciela); mechanizm zapisu runtime i dokładna wersja Node nie są podane.
- Cztery projekty na moduł, układ `api/` + `app/` + `deploy/`, `.slnx`, `StoneFlow.*` — podane (odpowiedzi właściciela).
- xUnit v3 na Microsoft.Testing.Platform, własny dyspozytor bez MediatR — podane.
- `DbContext` per moduł w osobnym schemacie z własną historią migracji — podane.
- Anonimowy `/health` z kontrolą SQL Server — podane.
- Walidacja daty uruchomienia przy starcie i przykładowa data w Development — podane; brak zakazu wartości w `appsettings.json` (Non-Critical 7).
- `ILogger` z JSON, korelacja przez `Activity.TraceId` — podane; zachowanie przychodzącego `X-Correlation-ID` niepodane.
- GitHub Actions, Testcontainers, `docker-compose` — podane (repozytorium zdalne jest na GitHub).
- Interpretacja „moduły nie sięgają do swoich encji” jako encji innych modułów — podane.
- Brak projektów Contracts w SF-3, przyszły wzorzec Contracts — podane (patrz konflikty).
- Styl endpointów odłożony do pierwszego zadania z endpointem — podane.
- Migracje lokalnie przez `dotnet ef`, bez migracji przy starcie — podane.
- Skan sekretów w CI jako sposób weryfikacji — podane; nieobecne w „Zachowanie”.
- `RestorePackagesWithLockFile` / `--locked-mode` — podane.
- OpenAPI tylko w Development — **niepodane** w „Założenia” (ukryte założenie).
- Konto SQL na hostingu pozwala tworzyć schematy — **niepodane** (ukryte założenie).
- Logi na konsolę wystarczają do czasu SF-4 — **niepodane**.
- Visual Studio w wersji obsługującej `.slnx` — **niepodane**.
- Ochrona gałęzi `main` poza zakresem albo w zakresie — **niepodane**.

## Recommendation

- Proceed as-is — `sdd-plan SF-3` może ruszyć. Zalecana krótka rewizja specyfikacji przed planem lub rozstrzygnięcie w planie punktów Non-Critical 1–4, 6 i 7, bo bez nich plan musi zgadywać, jak zapisać runtime i Node, jak zbudować CI i kiedy uznać kryterium TODO za spełnione.
