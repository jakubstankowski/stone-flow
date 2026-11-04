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
   3. kontrola `/`, `/logowanie` (odświeżenie podstrony) i `/api/auth/csrf` (przepisanie do API).

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
  potrzebują, a połączenie SQL i hasło FTP przez zmienne środowiskowe (nie argumenty);
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

## 10. Otwarte do weryfikacji na środowiskach

- Automatyczny przebieg obu publikacji i kontrole po publikacji.
- Odświeżenie podstrony SPA na Vercel i przepisanie `/api/*` z zachowaniem ciasteczek sesji
  (`__Host-`) i nagłówka CSRF.
- Sesja i logowanie przez Vercel (kryterium otwarte z SF-6).
- Limity prób logowania liczone są per adres wyjściowy Vercel (API nie ufa
  `X-Forwarded-For`, bo Vercel nie ma stałych adresów ani uwierzytelnienia proxy).
- Szyfrowanie kluczy Data Protection w bazie (wymaga certyfikatu na hostingu).
