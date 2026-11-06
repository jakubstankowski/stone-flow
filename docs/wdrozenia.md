# Wdrożenia

Procedura publikacji Stone Flow (zadanie SF-4). Frontend działa na Vercel, backend i baza na
SmarterASP.NET. Publikacja jest automatyczna po zielonym CI na `main`, ale **obecnie wyłączona**:
konta hostingowe nie istnieją, a decyzja D-08 w PRD jest otwarta.

> Decyzja właściciela z 04.11.2026: proces wdrożeń powstaje bez kont hostingowych, bez własnej
> domeny (domyślne adresy dostawców), z monitoringiem Better Stack i bez dostawcy e-mail (D-05)
> na tym etapie. Włączenie publikacji wymaga spełnienia warunków z sekcji 1.

## 1. Warunki włączenia publikacji

Zmienną repozytorium `DEPLOY_ENABLED=true` ustawia administrator repozytorium dopiero, gdy
wszystkie punkty są spełnione:

- [ ] D-08 zamknięte w PRD (plan SmarterASP.NET aktywny, runtime .NET 10.0.x na koncie).
- [ ] D-05 rozstrzygnięte i SMTP skonfigurowany — bez niego produkcyjne API nie wystartuje.
- [ ] Domyślny adres API działa po HTTPS z ważnym certyfikatem.
- [ ] Baza SQL na hostingu przyjmuje połączenia z GitHub Actions (zdalny dostęp do SQL).
  Jeśli nie — patrz sekcja 7, wariant awaryjny.
- [ ] Na serwerze jest `appsettings.Production.json` (sekcja 4) z tymczasową datą uruchomienia.
- [ ] GitHub: environment `production`, sekrety i zmienne (sekcja 3), wymagane kontrole PR.
- [ ] Vercel: projekt z katalogiem `app`, wyłączone wdrożenia z Git (sekcja 5).
- [ ] Better Stack: monitory z sekcji 9.

## 2. Przebieg publikacji

Workflow `.github/workflows/deploy.yml` (push na `main`, także ręcznie):

1. **CI** — wywołuje `ci.yml` (`backend`, `frontend`, `secrets`, `deploy-dry-run`). Czerwone CI
   zatrzymuje wszystko.
2. **Backend** (environment `production`):
   1. kontrola, że commit jest bieżącą głową `main` (stary przebieg nie wdroży starszego kodu);
   2. build Release;
   3. **migracje** `deploy/scripts/migrate.sh` w stałej kolejności: Access → Orders →
      Expenses → Reporting → Integrations. Pierwszy błąd kończy zadanie: kod API i frontend
      **nie są publikowane**, działa poprzednia wersja;
   4. paczka API (`deploy/scripts/publish-api.sh`) i ponowna kontrola głowy `main`;
   5. wysyłka FTPS (`deploy/scripts/upload-api.sh`): `app_offline.htm` → synchronizacja z
      usuwaniem nieaktualnych plików (bez `appsettings.Production.json` i `logs/`) → usunięcie
      `app_offline.htm`;
   6. `GET <adres-api>/health` musi zwrócić `200` (ponowienia przez ok. 2,5 min).
3. **Frontend** (environment `production`, tylko po udanym backendzie):
   1. generuje produkcyjny `vercel.json` (`app/scripts/vercel-config.mjs`) z przepisaniem
      `/api/*` na `STONEFLOW_API_ORIGIN`;
   2. `vercel pull`, `vercel build --prod`, skan gitleaks pakietu, `vercel deploy --prebuilt --prod`;
   3. kontrola `/` i `/logowanie` (odświeżenie podstrony) — `200`, oraz `/api/auth/csrf`
      (przepisanie do API) — `204`.

Publikacje nie biegną równolegle i nie są przerywane w trakcie. Przy kilku szybkich pushach
GitHub zostawia w kolejce tylko najnowszy oczekujący przebieg (pośrednie są anulowane przed
startem) — wdrożony zostaje najnowszy commit.

Każdy PR uruchamia `deploy-dry-run`: buduje paczkę API i sprawdza migracje na pustej bazie,
ponowne uruchomienie, błąd w środku sekwencji (zatrzymanie kolejnych kontekstów) i
nieosiągalny serwer.

## 3. Konfiguracja GitHub i uprawnienia

**Environment `production`** (Settings → Environments):

- reguła wdrożeń: tylko gałąź `main` (sekrety niedostępne z PR i innych gałęzi);
- jeśli plan GitHub nie udostępnia reguł environment dla repozytorium prywatnego, sekrety
  i tak trafiają tylko do zadań z warunkiem `github.ref == 'refs/heads/main'` w `deploy.yml`.

| Nazwa | Rodzaj | Gdzie | Zawartość |
|---|---|---|---|
| `DEPLOY_ENABLED` | zmienna | repozytorium | `true` włącza publikację (tylko administrator) |
| `STONEFLOW_API_ORIGIN` | zmienna | `production` | `https://<adres-api>` — bez ścieżki |
| `STONEFLOW_APP_URL` | zmienna | `production` | `https://<adres-aplikacji>` |
| `VERCEL_ORG_ID`, `VERCEL_PROJECT_ID` | zmienne | `production` | identyfikatory z `vercel link` |
| `VERCEL_TOKEN` | sekret | `production` | token Vercel ograniczony do zespołu projektu |
| `PRODUCTION_MIGRATIONS_CONNECTION` | sekret | `production` | połączenie SQL loginu do migracji |
| `API_FTP_HOST`, `API_FTP_USER`, `API_FTP_PASSWORD`, `API_FTP_PATH` | sekrety | `production` | konto FTP witryny API i katalog witryny |

**Ograniczone uprawnienia:**

- workflow mają tylko `contents: read`; sekrety są przekazywane wyłącznie krokom, które ich
  potrzebują; połączenie SQL i hasło FTP przez zmienne środowiskowe (nie argumenty), token
  Vercel jako argument `--token` (zgodnie z dokumentacją Vercel CLI; maskowany w logach,
  runner jest jednorazowy);
- `DEPLOY_ENABLED`, environment i sekrety może zmieniać tylko administrator repozytorium;
- osobny login SQL do migracji (prawa DDL w bazie Stone Flow) i login aplikacji tylko z
  odczytem i zapisem danych (`db_datareader`, `db_datawriter`), jeśli hosting pozwala na kilka
  loginów; w przeciwnym razie jeden login, a sekret migracji tylko w GitHub;
- konto FTP ograniczone do katalogu witryny API; token Vercel ograniczony do zespołu projektu.

**Wymagane kontrole PR** (ochrona gałęzi `main`): `backend`, `frontend`, `secrets`,
`deploy-dry-run`.

## 4. Konfiguracja serwera API

Plik `appsettings.Production.json` w katalogu witryny, tworzony ręcznie przez panel lub FTP.
Wdrożenie go nie nadpisuje i nie usuwa. Klucze jak w `deploy/api.env.example`:

```json
{
  "ConnectionStrings": { "StoneFlow": "<połączenie loginu aplikacji>" },
  "StoneFlow": {
    "LaunchDate": "<yyyy-MM-dd — tymczasowa, do zmiany przed startem>",
    "Owner": { "Email": "<adres-właściciela>" },
    "App": { "BaseUrl": "https://<adres-aplikacji>" },
    "Email": { "Mode": "Smtp", "Smtp": { "Host": "<serwer-smtp>", "Port": 587, "UseStartTls": true, "UserName": "<użytkownik>", "Password": "<hasło>", "FromAddress": "<nadawca>" } }
  }
}
```

- `App:BaseUrl` to adres frontendu na Vercel (link resetu hasła).
- Brak pliku lub błędny klucz zatrzymuje start API — log w `logs/stdout_*.log`.
- Pakiet jest framework-dependent (`net10.0`, ANCM in-process) — wymaga runtime ASP.NET Core
  10.0.x na koncie.

## 5. Vercel

- Projekt z katalogiem głównym `app`; ustawienia budowy pochodzą z `app/vercel.json`.
- `app/vercel.json` wyłącza wdrożenia z Git (`git.deploymentEnabled: false`), więc nie powstają
  automatyczne preview. Każde wdrożenie spoza workflow produkcyjnego (np. ręczne preview)
  używa tego pliku: fallback SPA bez przepisania `/api/*` — preview nie ma drogi do API, bazy
  ani integracji (`/api/*` → 404).
- Frontend nie dostaje żadnych zmiennych środowiskowych; woła względne `/api`.

## 6. Zgodność wersji

- Migracje uruchamiają się **przed** publikacją kodu, więc muszą być zgodne z poprzednią
  wersją API (expand/contract): najpierw dodać (kolumny dopuszczające `NULL`, nowe tabele),
  usuwać dopiero w kolejnym wdrożeniu, gdy kod już nie używa starych elementów.
- Backend publikuje się przed frontendem, więc nowe API musi obsłużyć poprzednią wersję
  frontendu (między krokami i gdy publikacja frontendu się nie powiedzie).

## 7. Awarie

**Nieudana migracja** (zadanie `backend`, krok „Migracje”):

1. Publikacja jest zatrzymana; działa poprzednia wersja API i frontendu.
2. Baza jest w stanie po ostatniej udanej migracji (każda migracja w osobnej transakcji);
   konteksty po błędnym nie były uruchamiane.
3. Ustal przyczynę z logu kroku (nazwa kontekstu w komunikacie `::error::`).
4. **Nie edytuj zastosowanych migracji.** Napraw nową migracją (fix forward), przejdź PR i
   CI; publikacja uruchomi się po merge.
5. Gdy dane są uszkodzone — odtwórz kopię bazy (procedura kopii: zadanie SF-21), potem
   ponownie uruchom workflow.

**Wariant awaryjny bez zdalnego SQL:** `dotnet ef migrations script --idempotent` dla każdego
kontekstu w tej samej kolejności i wykonanie skryptów w panelu hostingu przed publikacją kodu.

**`/health` zwraca 503 po publikacji:** API działa, ale nie łączy się z bazą — sprawdź
`ConnectionStrings` w `appsettings.Production.json` i dostępność SQL. Frontend nie został
opublikowany. Po naprawie uruchom workflow ponownie (Actions → Deploy → Re-run; przebieg musi
dotyczyć bieżącej głowy `main`).

**Przerwana wysyłka FTPS:** na serwerze zostaje `app_offline.htm` — API jest wyłączone i
pokazuje komunikat o pracach. Uruchom workflow ponownie albo usuń plik przez FTP po
sprawdzeniu, że pliki są kompletne.

**Nieudana publikacja Vercel:** działa poprzednia wersja frontendu z nowym API (sekcja 6).

**Rollback kodu:** revert na `main` i zwykła publikacja. Rollback kodu **nie cofa migracji**
(PRD sekcja 10) — dlatego obowiązuje expand/contract. Frontend można też przywrócić w panelu
Vercel (Promote poprzedniego wdrożenia).

## 8. Logi

ANCM zapisuje stdout API (JSON z identyfikatorem korelacji, bez sekretów) do `logs/stdout_*.log`
w katalogu witryny — nowy plik przy każdym starcie procesu, bez rotacji. Raz w miesiącu usuń
pliki starsze niż 30 dni przez FTP lub menedżer plików panelu.

## 9. Monitoring (Better Stack)

Konfiguracja ręczna w Better Stack Uptime:

| Monitor | Adres | Oczekiwanie | Interwał |
|---|---|---|---|
| API | `https://<adres-api>/health` | status 200 | 3 min |
| Frontend | `https://<adres-aplikacji>/logowanie` | status 200 | 3 min |

- Włącz kontrolę ważności certyfikatu SSL dla obu adresów (ostrzeżenie 14 dni przed końcem).
- Alert e-mailem na adres właściciela.
- `/health` jest anonimowy i bez szczegółów; `503` oznacza brak połączenia z bazą.

## 10. Harmonogram zadań (SF-11)

Zadania automatyczne (uzupełnianie WooCommerce, wydatki cykliczne) uruchamia harmonogram w API.
Stan, ostatni sukces i dzierżawa są w schemacie `scheduling` w bazie; API sprawdza należne zadania
przy starcie i co 5 minut. Uśpiony proces budzi każde żądanie HTTP, dlatego:

| Budzik | Adres | Interwał |
|---|---|---|
| Zadanie w panelu hostingu („Scheduled Tasks”, plan Premium) | `GET https://<adres-api>/health` | 15 min |
| Monitor Better Stack (sekcja 9) | `GET https://<adres-api>/health` | 3 min |

- Wywołanie nie niesie uprawnień ani sekretu — tylko budzi i podtrzymuje proces. Nie ustawiaj
  w panelu adresów z tokenem ani hasłem (adres trafia do logów IIS).
- Stan zadań: `GET /api/jobs` po zalogowaniu — ostatni przebieg, błąd, ostatni sukces, liczba
  kolejnych błędów i najbliższa próba. Po błędzie API ponawia po 15 min, potem co 1 h.
- Konfiguracja opcjonalna w `appsettings.Production.json`: `Scheduling:TimerInterval`
  (domyślnie `00:05:00`, od 1 do 15 min). `Scheduling:Enabled` służy tylko testom — nie wyłączaj go
  na produkcji.

## 11. Webhooki WooCommerce (SF-12)

Zamówienia ze sklepu przychodzą webhookami. Integracja jest **wyłączona**, dopóki w
`appsettings.Production.json` nie ma sekretu (`StoneFlow:WooCommerce:WebhookSecret`, co najmniej
16 znaków; zbyt krótki zatrzymuje start API). Włączenie dopiero po próbie z SF-2 (sekcja 12).

W panelu sklepu (WooCommerce → Ustawienia → Zaawansowane → Webhooki) cztery webhooki:

| Nazwa | Temat | Adres | API |
|---|---|---|---|
| Stone Flow — utworzenie | Zamówienie utworzone | `https://<adres-api>/webhooks/woocommerce/orders` | WP REST API v3 |
| Stone Flow — zmiana | Zamówienie zaktualizowane | jak wyżej | v3 |
| Stone Flow — usunięcie | Zamówienie usunięte | jak wyżej | v3 |
| Stone Flow — przywrócenie | Zamówienie przywrócone | jak wyżej | v3 |

- Każdy z tym samym sekretem co w konfiguracji API; status „Aktywny”. Sekret: losowy, co najmniej
  32 znaki alfanumeryczne (bez spacji i znaków specjalnych, które panel może przekształcić).
- Limit 300 żądań/min liczony jest per adres wyjściowy sklepu (wszystkie dostawy z jednego adresu).
  Masowa zmiana statusów w panelu sklepu może go przekroczyć — rozłóż ją w czasie albo po niej
  sprawdź, czy webhooki nie zostały wyłączone.
- Adres wskazuje bezpośrednio API (nie Vercel); ścieżka jest poza `/api`, bez ciasteczek i CSRF,
  uwierzytelnia ją podpis HMAC.
- Odpowiedzi: `200` — przyjęte (także dostawa pominięta lub nieczytelna, opisana w logu);
  `401` — zły podpis; `404` — integracja wyłączona; `413` — treść > 1 MB; `429` — ponad 300
  żądań/min z jednego adresu; `503` — błąd zapisu. Kody inne niż `2xx` liczą się w WooCommerce do
  wyłączenia webhooka po kilku kolejnych błędach — po awarii sprawdź status webhooków w panelu
  i włącz je ponownie; brakujące zmiany uzupełni dzienne uzupełnianie (SF-13).
- Nie potwierdzono, czy WooCommerce ponawia dostawę po `503`. Usunięcie odebrane przy awarii bazy
  może przepaść — patrz „Znane ograniczenie” niżej.
- Nieczytelna dostawa z ID zamówienia i błąd zapisu trafiają do historii błędów (SF-13); udany zapis
  tego zamówienia z dowolnego źródła zamyka błąd.

### Dzienne uzupełnianie przez REST (SF-13)

Zadanie `woocommerce-backfill` (23:00 Europe/Warsaw, maks. 30 min, sekcja 10) pobiera zamówienia
zmienione od ostatniego sukcesu (pierwsze: od daty uruchomienia) oraz wszystkie zamówienia z kosza.
Bez kluczy REST zadanie kończy się bez działania, a „Synchronizuj teraz” i „Ponów” zwracają `409`.

1. W panelu sklepu: WooCommerce → Ustawienia → Zaawansowane → REST API → „Dodaj klucz”, opis
   „Stone Flow”, uprawnienia **„Odczyt” (Read)**. Klucz i sekret są widoczne tylko raz.
2. W `appsettings.Production.json` na serwerze (razem albo wcale; brak części zatrzymuje start API):
   - `StoneFlow:WooCommerce:BaseUrl` — `https://<adres-sklepu>` (tylko `https`, bez parametrów),
   - `StoneFlow:WooCommerce:ConsumerKey` — `ck_…`,
   - `StoneFlow:WooCommerce:ConsumerSecret` — `cs_…`,
   - opcjonalnie `StoneFlow:WooCommerce:RequestTimeout` (domyślnie `00:00:30`, maks. 5 min).
3. API wysyła klucze wyłącznie w nagłówku `Authorization: Basic` przez `https`, nigdy w adresie.
   Jeśli przebieg kończy się „Sklep odrzucił klucze REST (HTTP 401)” mimo poprawnego klucza, hosting
   sklepu prawdopodobnie obcina nagłówek `Authorization` — napraw to w konfiguracji serwera sklepu
   (np. przekazanie nagłówka do PHP); **nie** przenoś kluczy do parametrów adresu (trafiłyby do logów).
4. Stan: `GET /api/integrations/woocommerce/status` (ostatni sukces, ostatni przebieg, otwarte
   błędy), lista błędów `GET /api/integrations/woocommerce/errors`. Nieudany przebieg nie przesuwa
   kursora — kolejny (ponowienie harmonogramu po 15 min / 1 h albo „Synchronizuj teraz”) pobiera dane
   od tego samego miejsca.

**Znane ograniczenie (decyzja właściciela z 06.11.2026):** REST API nie zwraca zamówień trwale
usuniętych w sklepie. Jeśli zginie webhook takiego usunięcia (np. przy awarii API), ERP się o nim nie
dowie i zamówienie zostanie w sprzedaży — trzeba je anulować ręcznie. Przeniesienie do kosza (zwykła
droga usunięcia) uzupełnianie wykrywa. Porównanie list ID to ewentualne osobne zadanie.

## 12. Otwarte do weryfikacji na środowiskach

- SF-12 / SF-2: przed włączeniem webhooków próba na kopii testowej sklepu — podpis na prawdziwym
  payloadzie, ping (czy jest podpisany), kosz, przywrócenie i trwałe usunięcie, kolejność dostaw,
  różnica zegarów sklepu i serwera, ponawianie po `503`, obecność meta NIP w REST.

- SF-13 / SF-2: parametry REST (`modified_after`/`modified_before` z `dates_are_gmt=true`,
  `status=any` bez kosza, `status=trash`, `orderby=modified`, nagłówki `X-WP-Total`/`X-WP-TotalPages`)
  i kod `404` nieistniejącego zamówienia (`woocommerce_rest_shop_order_invalid_id` lub
  `woocommerce_rest_invalid_id`) są przyjęte według dokumentacji. Kopii testowej sklepu nie ma, więc
  weryfikacja odbywa się na produkcji **tylko do odczytu**: klucz z uprawnieniem „Odczyt”, pierwsze
  uzupełnienie („Synchronizuj teraz”) w godzinach małego ruchu, potem porównanie liczby zamówień
  z panelem sklepu. ERP zapisuje wyłącznie własne zamówienia; w sklepie nic się nie zmienia. Próby
  kosza i `404` wymagają operacji na zamówieniu — tylko za zgodą właściciela.

- D-08 pkt 2 (harmonogram odporny na restart i usypianie): dowód z hostingu wymaga zarejestrowanego
  zadania, więc zbiera się go przy pierwszym wdrożeniu SF-13 lub SF-16 — przebiegi w `GET /api/jobs`
  po recyklingu puli i po nocy bez ruchu. Do sprawdzenia: limit bezczynności puli (oczekiwany
  > 15 min) i to, że przebieg do 30 min kończy się bez uśpienia. „Bezpieczne uwierzytelnienie
  wywołań” z D-08 nie dotyczy tego projektu, bo wywołanie budzące nie niesie uprawnień — do
  odnotowania przez właściciela przy zamykaniu D-08.

- Automatyczny przebieg obu publikacji i kontrole po publikacji.
- Odświeżenie podstrony SPA na Vercel i przepisanie `/api/*` z zachowaniem ciasteczek sesji
  (`__Host-`) i nagłówka CSRF.
- Sesja i logowanie przez Vercel (kryterium otwarte z SF-6).
- Limity prób logowania liczone są per adres wyjściowy Vercel (API nie ufa
  `X-Forwarded-For`, bo Vercel nie ma stałych adresów ani uwierzytelnienia proxy).
- Szyfrowanie kluczy Data Protection w bazie (wymaga certyfikatu na hostingu).
