# Specyfikacja zadania — SF-6

## Źródło

[SF-6](https://standev.youtrack.cloud/issue/SF-6) — Logowanie, reset hasła, sesja i ochrona API.
PRD: sekcja 3 (Użytkownicy i dostęp), sekcja 6 (DOST-01), sekcje 10 i 11. Design guide: sekcje 3,
6, 8 (wygaśnięcie sesji), 9.

## Cel

Zabezpieczyć Stone Flow jednym kontem właściciela. Backend dostaje nowy moduł `Access`
(cztery warstwy, schemat `access`) z ASP.NET Core Identity do przechowywania konta i haseł,
sesją w ciasteczku HttpOnly z wygaśnięciem po 60 minutach bezczynności, ochroną CSRF,
jednorazowym linkiem resetu hasła wysyłanym przez SMTP oraz polityką autoryzacji, która
domyślnie odrzuca każde żądanie API bez ważnej sesji poza jawną listą wyjątków. Frontend
dostaje ekrany logowania i resetu hasła, ochronę tras, obsługę wygaśnięcia sesji i menu
użytkownika z wylogowaniem. Frontend i API komunikują się pod tym samym originem (`/api/*`
przez proxy), więc ciasteczko jest ciasteczkiem pierwszej strony i CORS nie jest potrzebny.

## Kontekst

- **Backend** (`api/src/`): host `StoneFlow.Api` (Program.cs, Problem Details, korelacja,
  `/health`), część wspólna `StoneFlow.Shared.*` (dyspozytor CQRS, data uruchomienia,
  `UseModuleSqlServer`), moduły Orders, Expenses, Reporting, Integrations — bez endpointów.
  SF-6 dodaje pierwszy moduł z endpointami i rozstrzyga styl endpointów (TODO w
  `conventions.md`, oddany SF-6 przez SF-3).
- **Frontend** (`app/`): powłoka i sześć sekcji z SF-5, bez komunikacji z API. SF-6 jest
  pierwszym zadaniem pobierającym dane z API i rozstrzyga podejście do HTTP i stanu (TODO w
  `conventions.md`).
- **Zaległości z SF-3 potrzebne SF-6:** `ProblemDetailsSetup` usuwa `detail` ze wszystkich
  błędów poza Development (komunikaty logowania i resetu by znikały); `Dispatcher` zwraca
  `TargetInvocationException` zamiast wyjątku handlera.

## Zakres

### W zakresie

- Moduł `Access`: `StoneFlow.Access.Domain`, `.Application`, `.Infrastructure`, `.Api`,
  schemat SQL `access`, migracja `InitialAccess`.
- Konto właściciela na ASP.NET Core Identity (hash hasła, blokada po nieudanych próbach,
  znacznik bezpieczeństwa), jedno konto, bez ról.
- Tabela tokenów resetu hasła (hash tokenu, ważność, użycie, unieważnienie).
- Klucze ASP.NET Core Data Protection przechowywane w bazie (schemat `access`), aby restart
  procesu nie unieważniał sesji i tokenów CSRF.
- Sesja w ciasteczku, wygaśnięcie po 60 minutach bezczynności i maksymalnie 12 godzinach od
  zalogowania; wylogowanie; unieważnienie wszystkich sesji po resecie hasła.
- Ochrona CSRF (antiforgery) dla żądań zmieniających stan.
- Domyślna polityka autoryzacji wymagająca sesji dla wszystkich endpointów; jawna lista
  wyjątków.
- Ograniczenie liczby żądań (rate limiting) dla logowania i prośby o reset.
- Wysyłka e-mail przez interfejs z implementacją SMTP i implementacją „do logu” tylko w
  Development.
- Polityka CORS: brak dozwolonych originów (jawnie).
- Problem Details: `detail` ukrywany tylko dla nieobsłużonych wyjątków (poprawka zaległości
  SF-3).
- Dyspozytor: przekazuje oryginalny wyjątek handlera (poprawka zaległości SF-3).
- Frontend: `HttpClient` z XSRF, proxy `/api` w serwerze deweloperskim, usługa sesji,
  ochrona tras, przechwytywanie `401`, ekrany `/logowanie`, `/reset-hasla`,
  `/reset-hasla/nowe`, menu użytkownika w nagłówku z „Wyloguj”.
- README: konfiguracja właściciela, SMTP, proxy, instrukcja dodania linku w panelu sklepu.
- Testy jednostkowe, integracyjne (SQL Server w kontenerze) i frontendowe.
- Aktualizacja `conventions.md` (styl endpointów, moduł `Access`, HTTP i stan we frontendzie)
  i testów architektury dla nowego modułu.

### Poza zakresem

- Wdrożenie, konfiguracja przekierowania `/api/*` na Vercel i potwierdzenie sesji na
  wdrożonym środowisku (SF-4).
- Wybór konkretnego dostawcy e-mail (D-05) — tylko konfiguracja SMTP.
- Wiele kont, role, rejestracja, zmiana e-maila, zmiana hasła po zalogowaniu bez resetu.
- Wspólne logowanie z WooCommerce; modyfikacja sklepu (link dodaje właściciel ręcznie).
- Webhook WooCommerce i jego uwierzytelnienie (SF-12).
- Audyt finansowy (SF-7) — zdarzenia bezpieczeństwa trafiają tylko do logów.
- Uwierzytelnianie dwuskładnikowe.

## Zachowanie

### Konto właściciela

1. Konfiguracja wdrożenia zawiera `StoneFlow:Owner:Email`. Brak lub niepoprawny adres
   kończy start API błędem walidacji z komunikatem wskazującym klucz (jak data
   uruchomienia w SF-3).
2. Konto nie istnieje po instalacji i nie ma ekranu rejestracji. Prośba o reset hasła dla
   adresu równego `StoneFlow:Owner:Email` (porównanie bez rozróżniania wielkości liter, po
   przycięciu spacji), gdy konto jeszcze nie istnieje, tworzy konto bez hasła i wysyła link
   ustawienia hasła. Właściciel ustawia pierwsze hasło tym samym procesem co reset.
3. Adres inny niż skonfigurowany nigdy nie tworzy konta.

### Logowanie

4. Ekran `/logowanie` ma pola „E-mail” i „Hasło”, przycisk „Zaloguj” i link „Nie pamiętam
   hasła”. Pola są wymagane (walidacja w UI i w API).
5. Poprawne dane tworzą sesję i przekierowują do `/pulpit` (albo do strony, z której
   przekierowano na logowanie, jeśli była trasą aplikacji).
6. Błędne dane, nieistniejące konto, konto bez hasła i zablokowane konto dają ten sam
   komunikat: „Nieprawidłowy e-mail lub hasło. Po 5 nieudanych próbach logowanie jest
   blokowane na 15 minut.” (`401` z Problem Details). Odpowiedź nie zdradza, który przypadek
   wystąpił.
7. Po 5 kolejnych nieudanych próbach konto jest blokowane na 15 minut; udane logowanie zeruje
   licznik.
8. Ponad 10 prób logowania z jednego adresu IP w ciągu minuty → `429` z Problem Details
   „Zbyt wiele prób. Spróbuj ponownie za chwilę.”
9. Zalogowany użytkownik wchodzący na `/logowanie` jest przekierowany do `/pulpit`.

### Sesja

10. Sesja to ciasteczko HttpOnly, `Secure` (poza Development), `SameSite=Lax`, ścieżka `/`,
    bez atrybutu domeny, nazwa z prefiksem `__Host-` poza Development.
11. Sesja wygasa po 60 minutach bez żądania do API; każde uwierzytelnione żądanie przesuwa
    termin. Niezależnie od aktywności sesja kończy się 12 godzin po zalogowaniu.
12. Wygasła lub nieważna sesja → API zwraca `401` z Problem Details (bez przekierowania HTTP
    i bez danych). Frontend czyści stan sesji, przechodzi na `/logowanie` i pokazuje
    komunikat „Sesja wygasła. Zaloguj się ponownie.” Formularz nie obiecuje zachowania
    wpisanych danych.
13. „Wyloguj” w menu użytkownika kończy sesję w API (`204`), usuwa ciasteczko i przechodzi na
    `/logowanie` z komunikatem „Wylogowano.”
14. Ustawienie nowego hasła unieważnia wszystkie istniejące sesje (zmiana znacznika
    bezpieczeństwa, sprawdzanego przy każdym żądaniu).

### Reset hasła

15. Ekran `/reset-hasla` ma pole „E-mail” i przycisk „Wyślij link”. Po wysłaniu zawsze
    pokazuje: „Jeśli konto istnieje, wysłaliśmy link do ustawienia hasła. Link jest ważny
    30 minut.” API zawsze odpowiada `202`, niezależnie od istnienia konta.
16. Dla istniejącego konta (lub tworzonego wg punktu 2) API tworzy losowy token (co najmniej
    256 bitów), zapisuje tylko jego hash SHA-256 z terminem ważności 30 minut, unieważnia
    wcześniejsze niewykorzystane tokeny tego konta i wysyła e-mail z linkiem
    `{StoneFlow:App:BaseUrl}/reset-hasla/nowe#konto={id}&token={token}`. Token jest we
    fragmencie URL, więc nie trafia do serwera hostingu, logów ani nagłówka `Referer`;
    frontend usuwa fragment z historii przed pierwszym żądaniem. Link nie zawiera adresu
    e-mail. (Zmiana po przeglądzie specyfikacji, uwaga 7: wcześniej query string.)
17. Ponad 3 prośby o reset z jednego adresu IP w ciągu 15 minut → `429`. Ponad 3 prośby dla
    jednego konta w ciągu 15 minut → kolejne nie wysyłają e-maila, ale odpowiedź pozostaje
    `202`.
18. Ekran `/reset-hasla/nowe` ma pola „Nowe hasło” i „Powtórz hasło” oraz przycisk „Ustaw
    hasło”. Hasło: co najmniej 12 znaków, bez innych reguł składu, różne od adresu e-mail.
    Niezgodne pola → błąd przy polu (UI).
19. Poprawny, nieużyty, niewygasły token ustawia hasło, oznacza token jako użyty, odblokowuje
    konto, unieważnia sesje (punkt 14) i przechodzi na `/logowanie` z komunikatem „Hasło
    ustawione. Zaloguj się.” Sesja nie jest tworzona automatycznie.
20. Token nieznany, wygasły, użyty lub unieważniony → `400` z Problem Details „Link wygasł lub
    został już użyty. Poproś o nowy link.” i akcją przejścia do `/reset-hasla`. Za słabe
    hasło → `400` z błędem pola.

### Ochrona API

21. Domyślna polityka autoryzacji wymaga uwierzytelnionej sesji dla każdego endpointu. Bez
    sesji API odpowiada `401` (Problem Details) — dotyczy też odczytów.
22. Wyjątki (dostęp bez sesji):

    | Endpoint | Dlaczego |
    |---|---|
    | `GET /health` | monitoring (decyzja SF-3), bez danych |
    | `GET /api/auth/csrf` | pobranie tokenu CSRF przed logowaniem |
    | `POST /api/auth/login` | logowanie |
    | `POST /api/auth/password-reset/request` | prośba o link |
    | `POST /api/auth/password-reset/confirm` | ustawienie hasła z linku |
    | `GET /openapi/v1.json` | tylko Development (SF-3) |

    Webhook WooCommerce dojdzie w SF-12 z własnym uwierzytelnieniem i zostanie dopisany do tej
    listy w tamtym zadaniu. Test architektury/integracyjny wylicza endpointy anonimowe i
    porównuje z tą listą.
23. `GET /api/auth/session` (wymaga sesji) zwraca `{ "email": "…" }`; frontend używa go do
    ustalenia stanu po odświeżeniu strony.
24. CSRF: każde żądanie `POST`, `PUT`, `PATCH`, `DELETE` do `/api/*` (także logowanie i
    reset) musi mieć nagłówek `X-XSRF-TOKEN` zgodny z tokenem antiforgery; brak lub
    niezgodność → `400` z Problem Details „Odśwież stronę i spróbuj ponownie.” Token wydaje
    `GET /api/auth/csrf` w ciasteczku `XSRF-TOKEN` (czytelnym dla JS); po zalogowaniu i
    wylogowaniu token jest odnawiany.
25. CORS: API nie zezwala na żadne żądania z innych originów (brak nagłówków
    `Access-Control-Allow-*`).

### Frontend

26. Trasy sekcji z SF-5 są chronione: bez sesji przejście na trasę → `/logowanie?powrot=…`
    (tylko ścieżki wewnętrzne aplikacji). Ekrany logowania i resetu są poza powłoką z
    nawigacją (prosty układ z nazwą produktu).
27. Przy starcie aplikacja pyta `GET /api/auth/session`; do czasu odpowiedzi chronione trasy
    pokazują stan ładowania, nie treść.
28. Każde `401` z API (poza samym logowaniem) czyści stan sesji i działa jak punkt 12.
29. Nagłówek powłoki pokazuje adres e-mail zalogowanego użytkownika i przycisk „Wyloguj”.
30. Formularze logowania i resetu: etykiety nad polami, błędy przy polach, stan „Logowanie…” /
    „Wysyłanie…” / „Zapisywanie…”, blokada ponownego wysłania, dane zostają po błędzie API
    (poza hasłami, które są czyszczone po błędzie logowania).

### Logi

31. Logi zdarzeń bezpieczeństwa: udane logowanie, nieudane logowanie, blokada, wylogowanie,
    prośba o reset, ustawienie hasła, odrzucony token — z identyfikatorem konta (nie e-mailem,
    nie hasłem, nie tokenem) i korelacją. Treść e-maila i token nie trafiają do logów poza
    implementacją „do logu” w Development.

## Dane / API

### Storage

SQL Server, nowy schemat `access` (własny `AccessDbContext`, historia migracji
`access.__EFMigrationsHistory`, migracja `InitialAccess`):

- tabele ASP.NET Core Identity potrzebne do jednego konta bez ról: użytkownicy (unikalny
  znormalizowany e-mail, hash hasła, znacznik bezpieczeństwa, znacznik współbieżności,
  licznik nieudanych prób, koniec blokady); bez tabel ról, claimów i loginów zewnętrznych;
- `access.PasswordResetTokens`: `Id`, `UserId` (FK), `TokenHash` (binary(32), unikalny),
  `CreatedAtUtc`, `ExpiresAtUtc`, `ConsumedAtUtc` (null), `RevokedAtUtc` (null);
- `access.DataProtectionKeys`: klucze Data Protection.

Znaczniki czasu w UTC. Brak danych finansowych.

### Endpointy

Styl: Minimal API w `StoneFlow.Access.Api`, grupa `/api/auth`.

| Metoda | Ścieżka | Żądanie | Odpowiedź | Auth | Uwagi |
|---|---|---|---|---|---|
| GET | `/api/auth/csrf` | — | `204`, ciasteczko `XSRF-TOKEN` | anonimowy | |
| POST | `/api/auth/login` | `{ email, password }` | `204` + ciasteczko sesji; `400` walidacja; `401` ogólny błąd; `429` | anonimowy, CSRF | |
| POST | `/api/auth/logout` | — | `204` | sesja, CSRF | |
| GET | `/api/auth/session` | — | `200 { email }`; `401` | sesja | |
| POST | `/api/auth/password-reset/request` | `{ email }` | `202` zawsze; `400` gdy pusty/niepoprawny format; `429` | anonimowy, CSRF | |
| POST | `/api/auth/password-reset/confirm` | `{ accountId, token, newPassword }` | `204`; `400` token/hasło | anonimowy, CSRF | |

Błędy jako Problem Details z `traceId`; walidacja jako `ValidationProblemDetails` z błędami
pól.

### Typy przekraczające granicę

- DTO żądań/odpowiedzi powyżej (JSON, camelCase).
- `IEmailSender` (Access.Application) — wysyłka wiadomości; implementacje w
  Access.Infrastructure.
- Porty Access.Application dla konta, tokenów i sesji, implementowane w Infrastructure / Api
  (szczegóły w planie).
- Konfiguracja: `StoneFlow:Owner:Email`, `StoneFlow:App:BaseUrl`,
  `StoneFlow:Email:Mode` (`Smtp` | `Log`; `Log` dozwolone tylko w Development),
  `StoneFlow:Email:Smtp:{Host, Port, UseStartTls, UserName, Password, FromAddress,
  FromName}`. Hasło SMTP tylko w user-secrets/zmiennych środowiskowych.

## Wpływ na niezmienniki

| Niezmiennik | Jak jest respektowany |
|---|---|
| 1. Sprzedaż oddzielona od gotówki | Nie dotyczy. |
| 2. Kwoty decimal na backendzie | Nie dotyczy. |
| 3. Dostęp wymuszany przez API | Domyślna polityka wymaga sesji dla każdego endpointu (także odczytów); wyjątki tylko z jawnej listy, sprawdzanej testem; frontendowa ochrona tras jest tylko wygodą. |
| 4. Ponowienia bez duplikatów | Ponowiona prośba o reset unieważnia poprzednie tokeny; nie powstaje drugie konto (unikalny znormalizowany e-mail, konto tworzone tylko dla skonfigurowanego adresu). |
| 5. Audyt zmian finansowych | Nie dotyczy (brak danych finansowych); zdarzenia bezpieczeństwa w logach. |
| 6. Sekrety po stronie serwera | Hasło SMTP i connection string tylko w konfiguracji serwera; tokeny resetu przechowywane jako hash; hasła tylko jako hash Identity; brak sekretów w pakiecie frontendu; logi bez haseł, tokenów i e-maili. |
| 7. Granice modułów i warstw | Nowy moduł z własnym schematem; Domain/Application bez ASP.NET Core, EF Core i Identity; testy architektury obejmują `Access`. |
| 8. Brak danych identyfikujących | README i przykłady z placeholderami (`owner@example.com`), bez nazwy i domeny sklepu. |

## Ryzyko

**high** — uwierzytelnianie chroni dane finansowe i osobowe; błąd otwiera dostęp do całego
systemu. Wymagany przegląd przez człowieka przed merge. Dodatkowo kryterium sesji na
wdrożonym środowisku pozostaje otwarte do SF-4.

## UI

- **Trasy:** `/logowanie`, `/reset-hasla`, `/reset-hasla/nowe` (poza powłoką, bez ochrony);
  sekcje SF-5 chronione.
- **Komponenty:** ekrany w `features/auth/`; wspólne `app-field`, `app-button` (stan
  zapisu), `app-error-state`; menu użytkownika w `layout/`.
- **Stany:** ładowanie sesji przy starcie; błąd logowania; błąd sieci („Nie udało się
  połączyć z serwerem.”); sukces resetu; wygaśnięcie sesji; wylogowanie.
- **Główna akcja:** „Zaloguj” / „Wyślij link” / „Ustaw hasło” — przycisk `primary`.
- **Dostępność:** etykiety nad polami, `autocomplete` (`username`, `current-password`,
  `new-password`), błędy powiązane z polami, komunikat wygaśnięcia w `role="status"`, błąd
  logowania w `role="alert"`, fokus na pierwszym polu z błędem lub na komunikacie, pełna
  obsługa klawiaturą, kontrast z tokenów SF-5.

## Przypadki brzegowe

- E-mail z wielkimi literami lub spacjami → normalizacja (przycięcie, porównanie bez wielkości
  liter).
- Logowanie na konto bez hasła (przed pierwszym resetem) → ogólny `401`.
- Dwie prośby o reset → działa tylko link z ostatniej.
- Link użyty drugi raz → `400` „Link wygasł lub został już użyty…”.
- Token zmieniony w URL lub `konto` nie pasuje do tokenu → ten sam `400`.
- Reset w trakcie aktywnej sesji w innej karcie → ta sesja dostaje `401` przy następnym
  żądaniu.
- Sesja aktywna 11 h 59 min z ciągłą aktywnością → wygasa po 12 h od logowania.
- Restart API → sesje i tokeny CSRF pozostają ważne (klucze w bazie).
- Baza niedostępna przy logowaniu → `503` Problem Details bez szczegółów; frontend: „Nie udało
  się połączyć z serwerem.”
- Brak `StoneFlow:Owner:Email`, `StoneFlow:App:BaseUrl` lub konfiguracji SMTP przy
  `Mode=Smtp` → API nie startuje (komunikat wskazuje klucz, bez wartości).
- `Mode=Log` poza Development → API nie startuje.
- Błąd SMTP przy wysyłce → odpowiedź nadal `202`, błąd w logach (bez treści e-maila).
- `powrot` wskazujący zewnętrzny adres lub `//…` → ignorowany, przejście na `/pulpit`.
- Żądanie bez nagłówka CSRF → `400`.
- Żądanie z innego originu → brak nagłówków CORS (przeglądarka blokuje).

## Zależności

- **SF-3** (zakończone): host, Problem Details, dyspozytor, testy integracyjne.
- **SF-5** (zakończone): powłoka, `app-field`, `app-button`, stany.
- **SF-4** (odłożone): przekierowanie `/api/*` na Vercel, HTTPS i potwierdzenie sesji na
  wdrożonym środowisku. Bez SF-4 kryterium „Sesja działa między domenami na wdrożonym
  środowisku” pozostaje niespełnione.
- **D-05**: dostawca e-mail niewybrany — implementacja SMTP działa z dowolnym dostawcą po
  konfiguracji.

## Założenia

Pytania doprecyzowujące zadane 04.11.2026; właściciel odpowiedział „lec z /sdd-spec”, co
przyjęto jako akceptację wszystkich zaproponowanych odpowiedzi:

1. **SF-6 przed SF-4** → implementacja i weryfikacja lokalna; kryterium sesji na wdrożonym
   środowisku otwarte do SF-4.
2. **Miejsce logowania** → nowy moduł `Access` (4 warstwy, schemat `access`) na ASP.NET Core
   Identity; klucze Data Protection w bazie.
3. **Sesja przy dwóch domenach** → wariant a: frontend woła API pod tym samym originem
   (`/api/*` przez proxy: lokalnie serwer deweloperski Angular, na produkcji przekierowanie
   Vercel w SF-4); ciasteczko pierwszej strony, `HttpOnly`, `Secure`, `SameSite=Lax`, CSRF
   tokenem w nagłówku; CORS bez dozwolonych originów.
4. **Pierwsze konto** → e-mail w `StoneFlow:Owner:Email`; konto bez hasła, hasło ustawiane
   przez „Nie pamiętam hasła”; brak rejestracji i haseł w konfiguracji. (Konto powstaje przy
   pierwszej prośbie o reset, nie przy starcie API — start nie zapisuje do bazy, zgodnie z
   SF-3.)
5. **Ważność linku** → 30 minut, jednorazowy, nowy unieważnia poprzedni.
6. **Zgadywanie hasła** → blokada konta 15 min po 5 nieudanych próbach; limity żądań na
   logowanie i reset; jeden ogólny komunikat; prośba o reset zawsze `202`.
7. **Hasło** → minimum 12 znaków, bez reguł składu.
8. **Sesja** → 60 min bezczynności (przesuwane), maksymalnie 12 h; „Wyloguj” w nagłówku;
   reset unieważnia wszystkie sesje.
9. **Dostawca e-mail (D-05)** → interfejs + implementacja SMTP; lokalnie link w logu;
   dostawcy nie wybiera SF-6.
10. **Link w panelu sklepu** → dodaje właściciel ręcznie; README z instrukcją i adresem
    `/logowanie`; ERP nie modyfikuje sklepu.
11. **Wyjątki od DOST-01** → `/health`, CSRF, logowanie, prośba o reset, ustawienie hasła,
    OpenAPI w Development; webhook w SF-12.
12. **UI** → `/logowanie`, `/reset-hasla`, `/reset-hasla/nowe`; ochrona tras; `401` → logowanie
    z komunikatem; e-mail i „Wyloguj” w nagłówku.

Pozostałe założenia przyjęte przy pisaniu:

- Ścieżki API z prefiksem `/api` (poza `/health`), aby proxy przekazywało jedną gałąź.
- Styl endpointów: Minimal API z grupami tras w projekcie `*.Api` modułu (rozstrzyga TODO z
  `conventions.md`).
- Podejście HTTP we frontendzie: `HttpClient` (`provideHttpClient` z XSRF i
  interceptorami), usługi obszaru z sygnałami jako stan; brak biblioteki stanu.
- Limity rate limitingu (10/min logowanie, 3/15 min reset) są wartościami startowymi w
  konfiguracji kodu, nie w PRD.
- Lockout i limity są w pamięci procesu (rate limiting) lub w bazie (lockout Identity);
  restart procesu zeruje tylko liczniki rate limitingu.
- Problem Details: `detail` usuwany tylko dla odpowiedzi z nieobsłużonego wyjątku; komunikaty
  błędów logowania i resetu są stałymi tekstami bez danych.
- Dyspozytor przekazuje oryginalny wyjątek handlera (poprawka zaległości SF-3).
- Towarzysząca poprawka niestabilnego testu `LaunchDateStartupTests` z SF-3 (CI) jest osobnym
  commitem na tej gałęzi i nie zmienia zachowania aplikacji.

## Kryteria odbioru

- [ ] Bez ważnej sesji API odmawia (`401`) każdego odczytu i zapisu poza wyjątkami z punktu 22;
      test porównuje listę endpointów anonimowych z tą listą.
- [ ] Sesja wygasa po 60 minutach bezczynności (test z kontrolowanym zegarem) i po 12 h od
      zalogowania; po wygaśnięciu frontend pokazuje „Sesja wygasła…” bez danych.
- [ ] Link resetu działa jednorazowo, wygasa po 30 minutach, nowy unieważnia poprzedni.
- [ ] Reset hasła unieważnia istniejące sesje.
- [ ] 5 nieudanych logowań blokuje konto na 15 minut; komunikat nie zdradza przyczyny.
- [ ] Żądania zmieniające stan bez tokenu CSRF są odrzucane; CORS nie zezwala na inne originy.
- [ ] Pierwsze konto powstaje tylko dla `StoneFlow:Owner:Email` przez proces resetu.
- [ ] README opisuje dodanie linku `/logowanie` w panelu sklepu; link nie uwierzytelnia.
- [ ] Ekrany logowania i resetu działają klawiaturą, z etykietami i komunikatami.
- [ ] Logi nie zawierają haseł, tokenów ani adresów e-mail.
- [ ] Testy architektury obejmują moduł `Access`; CI zielone.
- [ ] (Otwarte do SF-4) Sesja działa między domenami na wdrożonym środowisku.

## Weryfikacja

- build/testy backend: `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`,
  `dotnet build -c Release`, `dotnet test` (testy z SQL Server w kontenerze — CI lub maszyna
  z Dockerem).
- frontend: `npm ci`, `format:check`, `lint`, `typecheck`, `build`, `npm test`.
- ręcznie (lokalnie, API + `npm start` z proxy, `StoneFlow:Email:Mode=Log`):
  - prośba o reset dla adresu właściciela → link w logu → ustawienie hasła → logowanie;
  - 5 błędnych haseł → blokada; komunikat ogólny;
  - wylogowanie; ponowne użycie linku → błąd;
  - `curl` na `/api/auth/session` bez ciasteczka → `401`; `POST` bez CSRF → `400`;
  - przegląd przez człowieka przed merge (ryzyko high).
