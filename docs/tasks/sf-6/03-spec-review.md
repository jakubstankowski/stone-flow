# Spec review — SF-6

## Summary

- Overall assessment: PASS WITH ISSUES
- Justification: Specyfikacja pokrywa wszystkie punkty zakresu i kryteria odbioru zgłoszenia (poza dwoma świadomie odłożonymi: wyborem dostawcy e-mail z D-05 i potwierdzeniem sesji na wdrożonym środowisku, które zależy od odłożonego SF-4). Nie przeczy żadnej zapisanej decyzji i nie narusza żadnego niezmiennika: domyślna polityka autoryzacji z jawną, testowaną listą wyjątków spełnia DOST-01, a tokeny resetu są losowe, haszowane, jednorazowe i unieważniane. Projekt bezpieczeństwa ma jednak luki, które planista musiałby rozstrzygać sam w obszarze wysokiego ryzyka: zmiana skonfigurowanego adresu właściciela (drugie konto, stare konto aktywne), wylogowanie bez unieważnienia skradzionego ciasteczka, kolejność `401`/`400` CSRF po wygaśnięciu sesji, źródło adresu IP dla limitów za proxy i zachowanie 12-godzinnego limitu przy odnawianiu ciasteczka. Zalecana poprawka specyfikacji przed planem, ale żadna z luk nie blokuje planowania.

## Findings

### Critical Issues

None.

### Non-Critical Issues

1. **Zmiana `StoneFlow:Owner:Email` po utworzeniu konta** („Konto właściciela”, „Wpływ na niezmienniki”). Specyfikacja nie opisuje, co się dzieje, gdy właściciel zmieni adres w konfiguracji. Punkt 2 utworzy wtedy drugie konto dla nowego adresu, a punkt 16 („dla istniejącego konta”) i logowanie nadal obsłużą stare konto ze starym hasłem. To przeczy D-05 („jedno konto”) i stwierdzeniu w tabeli niezmienników, że „nie powstaje drugie konto” — unikalny znormalizowany e-mail przed tym nie chroni. Trzeba ustalić regułę, np. logowanie i reset tylko dla konta o adresie równym konfiguracji, co najwyżej jeden wiersz użytkownika, zachowanie przy niezgodności.
2. **Wylogowanie nie unieważnia sesji po stronie serwera** (punkt 13). Ciasteczko uwierzytelniające ASP.NET Core jest bezstanowe: `SignOut` usuwa je tylko w tej przeglądarce, a jego kopia (np. przechwycona) działa do 60 min bezczynności / 12 h. Specyfikacja pisze „kończy sesję w API”, ale nie mówi, czy wylogowanie zmienia znacznik bezpieczeństwa (jak reset w punkcie 14). Przy jednym koncie i walidacji znacznika przy każdym żądaniu koszt jest zerowy — decyzja powinna być jawna.
3. **Kolejność `401` i błędu CSRF oraz odświeżanie tokenu** (punkty 12, 24, 28). Token antiforgery jest związany z tożsamością. Po wygaśnięciu sesji lub po resecie w innej karcie żądanie `POST` niesie token wydany dla zalogowanego użytkownika. Specyfikacja nie mówi, czy wtedy ma przyjść `401` („Sesja wygasła…”), czy `400` („Odśwież stronę…”), ani że ekran logowania pobiera nowy token (`GET /api/auth/csrf`) przy każdym wejściu i po każdym `401` — punkt 24 wymienia tylko odnowienie po zalogowaniu i wylogowaniu. Bez tego kryterium „po wygaśnięciu frontend pokazuje »Sesja wygasła…«” i ponowne logowanie po wygaśnięciu mogą nie działać.
4. **Limit 12 h a odnawianie ciasteczka** (punkty 11, 14). Walidacja znacznika bezpieczeństwa przy każdym żądaniu (`SecurityStampValidator`, `ValidationInterval = 0`) i przesuwanie terminu odtwarzają principal i ciasteczko; własne claimy giną, a `IssuedUtc` może się przesunąć. Specyfikacja powinna wymagać, aby czas zalogowania był trwałym claimem przenoszonym przy odnowieniu (np. przez `OnRefreshingPrincipal`), a test 12 h objąć ciągłą aktywnością przy włączonej walidacji znacznika.
5. **Źródło adresu IP dla limitów żądań za proxy** (punkty 8, 17, „Zależności”). Przy wybranym wariancie same-origin produkcyjny ruch przychodzi przez proxy Vercel, więc limity „z jednego adresu IP” staną się limitami globalnymi (jeden napastnik zablokuje prośby o reset dla wszystkich). Ufanie `X-Forwarded-For` bez ograniczenia źródła pozwoli z kolei podszyć adres przy bezpośrednim wejściu na API. Specyfikacja nie nazywa tej zależności; powinna wskazać, że źródło IP i zaufane proxy (`ForwardedHeaders`) ustala SF-4, a do tego czasu limity działają tylko lokalnie.
6. **Blokada konta jako wektor odmowy usługi** (punkt 7, założenie 6). Przy jednym koncie każdy, kto zna adres właściciela, może utrzymywać blokadę (5 prób co 15 min mieści się w limicie 10/min/IP). Reset odblokowuje konto (punkt 19), ale napastnik może blokować dalej. To akceptowalny kompromis, lecz powinien być zapisany jako świadome ryzyko, a nie tylko wynik zbiorczego „lec z /sdd-spec”.
7. **Token resetu w query stringu adresu SPA** (punkt 16). Link `…/reset-hasla/nowe?konto={id}&token={token}` trafia do logów dostępu hostingu frontendu (Vercel) i do historii przeglądarki. Token jest krótkotrwały i jednorazowy, ale to dane uwierzytelniające w logach poza kontrolą aplikacji (por. niezmiennik 6). Do rozważenia: fragment (`#token=`), usunięcie tokenu z adresu po odczycie (`history.replaceState`) i `Referrer-Policy: no-referrer` na tej trasie.
8. **Jednorazowość przy równoczesnych żądaniach** (punkt 19, „Przypadki brzegowe”). Dwa równoczesne `confirm` z tym samym tokenem i dwie równoczesne pierwsze prośby o reset dla nieistniejącego konta nie są opisane. Trzeba wymagać atomowego zużycia tokenu (warunkowa aktualizacja lub kontrola współbieżności) i obsługi naruszenia unikalności przy tworzeniu konta (wynik nadal `202`, bez `500`).
9. **Atrybuty ciasteczek antiforgery** (punkt 24). Punkt 10 opisuje tylko ciasteczko sesji. Ciasteczko antiforgery (HttpOnly) i `XSRF-TOKEN` (czytelne dla JS) nie mają określonych `Secure`, `SameSite`, ścieżki ani prefiksu `__Host-` poza Development.
10. **Mechanizm egzekwowania CSRF dla Minimal API** (punkt 24). W ASP.NET Core automatyczna walidacja antiforgery w Minimal API obejmuje tylko endpointy z danymi formularza, nie JSON. Wymaganie „każde żądanie `POST`, `PUT`, `PATCH`, `DELETE` do `/api/*`” jest poprawne, ale specyfikacja powinna wprost wymagać globalnej walidacji (middleware lub filtr grupy) i testu, który — podobnie jak lista endpointów anonimowych — sprawdza ją dla każdego endpointu zmieniającego stan, także przyszłych modułów.
11. **Rozróżnialność kont po czasie odpowiedzi** (punkty 6, 15). Logowanie dla nieistniejącego konta nie liczy hasha, a prośba o reset dla adresu właściciela tworzy konto i synchronicznie wysyła e-mail (punkt „Błąd SMTP… nadal `202`” sugeruje wysyłkę w żądaniu). Różnica czasu zdradza adres właściciela. Przy jednym koncie wpływ jest niski; warto zapisać decyzję (akceptacja albo wysyłka poza żądaniem i stały koszt weryfikacji).
12. **Klucze Data Protection w bazie bez szyfrowania** („W zakresie”, „Storage”). Klucze w `access.DataProtectionKeys` będą jawne w bazie i w kopiach zapasowych; kto ma odczyt bazy, może podrobić ciasteczko sesji. Specyfikacja powinna jawnie przyjąć ten stan lub wymagać ochrony kluczy (`ProtectKeysWith…`) oraz stałej nazwy aplikacji (`SetApplicationName`).
13. **D-05 i wybór dostawcy e-mail** („Poza zakresem”, „Zależności”). Zgłoszenie wprost wymienia „wybór dostawcy wysyłki (D-05)”, a PRD trzyma tę kwestię jako otwartą. Odłożenie wyboru jest poprawne (to decyzja właściciela), ale oznacza, że reset hasła — jedyna droga utworzenia konta — nie zadziała na produkcji, dopóki dostawca nie zostanie wybrany. Specyfikacja powinna wskazać, kto i kiedy zamyka D-05 (np. przed SF-4 lub w SF-4) i że SF-6 kończy się z tym punktem otwartym.
14. **Zgoda właściciela „lec z /sdd-spec”**. Odczytanie tej odpowiedzi jako akceptacji 12 propozycji jest rozsądne dla rozstrzygnięć technicznych mieszczących się w zgłoszeniu. Kilka propozycji wykracza jednak poza zgłoszenie lub przenosi pracę między zadaniami i przy ryzyku high powinno zostać potwierdzone wprost (najpóźniej w przeglądzie człowieka): wariant same-origin z przekierowaniem `/api/*` na Vercel (zmienia przesłankę „osobne domeny” i zakres SF-4, który mówi o „konfigurowalnym adresie API”), odłożenie D-05, tworzenie konta przez proces resetu, limit 12 h, blokada 5/15 min.
15. **Kryteria odbioru nie obejmują części zachowania** („Kryteria odbioru”). Brak kryteriów dla limitów żądań (`429`, punkty 8 i 17), CSRF przy logowaniu (login CSRF), blokady startu przy brakującej konfiguracji, `Mode=Log` poza Development, odrzucenia zewnętrznego `powrot` i `503` przy niedostępnej bazie. Są w „Zachowaniu” i „Przypadkach brzegowych”, ale bez kryterium łatwo je pominąć w teście.

### Unclear or Ambiguous Sections

- **Sesja, punkt 11** — „każde uwierzytelnione żądanie przesuwa termin” różni się od domyślnego `SlidingExpiration` (odnowienie dopiero po połowie okna, więc faktyczna bezczynność 30–60 min). Nie wiadomo, czy wymagane jest odnowienie przy każdym żądaniu, czy dopuszczalna jest domyślna tolerancja. Nie określono też, czy ciasteczko jest trwałe, czy sesyjne przeglądarki.
- **Reset hasła, punkt 17** — limit „3 prośby dla jednego konta w ciągu 15 minut” nie ma wskazanego miejsca liczenia (pamięć procesu czy `PasswordResetTokens.CreatedAtUtc`); założenie o rate limitingu w pamięci dotyczy tylko limitów per IP.
- **Logowanie, punkt 6 i 7** — nie wiadomo, czy nieudana próba na koncie bez hasła zwiększa licznik blokady.
- **Logi, punkt 31** — nie określono, czy zdarzenia bezpieczeństwa i odrzucenia limitów zapisują adres IP (dana osobowa; PRD sekcja 10: „bez zbędnych danych osobowych”).
- **Frontend, punkt 29** — brak zachowania adresu e-mail i „Wyloguj” w nagłówku na wąskich ekranach (design guide sekcja 9, SF-5: menu poniżej 1024 px).
- **Ochrona API, punkt 22** — `GET /health` nie leży pod `/api`, więc przy wariancie same-origin nie będzie dostępny przez proxy frontendu; warto dopisać, że monitoring woła API bezpośrednio.

### Invented or Unsupported Requirements

- Poprawka niestabilnego testu `LaunchDateStartupTests` z SF-3 („Założenia”) nie wynika ze zgłoszenia ani z potrzeb SF-6. Opisana jako osobny commit bez zmiany zachowania — akceptowalna, ale poza zakresem zadania.
- Limit 12 h od zalogowania, minimalna długość hasła 12 znaków i konkretne progi limitów nie pochodzą z PRD ani zgłoszenia; wynikają z propozycji 6–8 przyjętych zbiorczo (patrz Non-Critical 14). Są jawnie wymienione w „Założeniach”.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Cel, Założenia 3 (same-origin przez proxy) | Zgłoszenie SF-6 „Context”; task-breakdown SF-4 „konfigurowalny adres API” | Nie jest to sprzeczność z zapisaną decyzją, ale zmienia przesłankę „osobne domeny” i przenosi na SF-4 konfigurację przekierowania `/api/*`, zaufanych nagłówków proxy i HTTPS. Decyzję trzeba zapisać (PRD sekcja 10 / zgłoszenie SF-4), żeby SF-4 jej nie pominęło. |
| Poza zakresem (dostawca e-mail) | Zgłoszenie SF-6 „Scope”: „wybór dostawcy wysyłki (D-05)”; PRD D-05 „Do potwierdzenia” | Punkt zakresu zgłoszenia nie jest realizowany; poprawnie, bo to decyzja właściciela, ale wymaga jawnego potwierdzenia i wskazania, gdzie zostanie zamknięty. |
| Kryteria odbioru (sesja na wdrożonym środowisku) | Zgłoszenie SF-6 „Acceptance criteria” | Kryterium pozostaje otwarte do SF-4; zgodne z decyzją właściciela z 03.11.2026 o odłożeniu SF-4 i realizacji SF-6 lokalnie. |
| Wpływ na niezmienniki, wiersz 4 | PRD sekcja 3 (D-05: jedno konto) | Twierdzenie „nie powstaje drugie konto” nie jest prawdziwe przy zmianie adresu właściciela w konfiguracji (Non-Critical 1). |
| Zakres: Problem Details, dyspozytor | SF-3 spec, zachowanie 7 i 9; SF-3 code review a | Brak konfliktu — poprawki przywracają zachowanie opisane w SF-3 („bez szczegółów wyjątku”), zgodnie z zaległościami z 06-implementation-done SF-3. |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sprzedaż oddzielona od gotówki | Not applicable | Brak danych finansowych. |
| 2. Kwoty decimal na backendzie | Not applicable | Brak kwot. |
| 3. Dostęp wymuszany przez API | OK | Domyślna polityka wymaga sesji dla każdego endpointu, także odczytów; wyjątki jawne (`/health`, CSRF, logowanie, dwa endpointy resetu, OpenAPI tylko w Development) i sprawdzane testem; ochrona tras we frontendzie tylko jako wygoda. Lista zgodna z DOST-01 i z wyjątkami wskazanymi w przeglądzie projektu; webhook poprawnie w SF-12. |
| 4. Ponowienia bez duplikatów | Not applicable | Dotyczy WooCommerce i zadań cyklicznych. Uwaga o drugim koncie — Non-Critical 1. |
| 5. Audyt zmian finansowych | Not applicable | Brak danych finansowych; zdarzenia bezpieczeństwa w logach. SF-7 dostaje identyfikator konta z sesji. |
| 6. Sekrety po stronie serwera | OK | Hasło SMTP tylko w user-secrets/zmiennych, tokeny jako SHA-256, hasła jako hash Identity, logi bez haseł, tokenów i e-maili, tryb `Log` tylko w Development (start blokowany poza nim). Uwaga: token w query stringu adresu SPA trafia do logów hostingu frontendu (Non-Critical 7). |
| 7. Granice modułów i warstw | OK | Nowy moduł `Access` w czterech warstwach z własnym schematem i testami architektury; Domain/Application bez ASP.NET Core, EF Core i Identity. Klucze Data Protection w schemacie `access` konsumuje host jako punkt kompozycji — dopuszczalne. |
| 8. Brak danych identyfikujących | OK | README i przykłady z `owner@example.com`, bez nazwy i domeny sklepu. |

## Risk

- Recorded: high
- Assessed: high — uwierzytelnianie chroni dane finansowe i osobowe całego systemu; dodatkowo zmiany w części wspólnej (Problem Details, dyspozytor) dotykają wszystkich przyszłych modułów. Wymagany przegląd przez człowieka przed merge.

## Assumptions Detected

| Założenie | Czy specyfikacja je podaje |
|---|---|
| Odpowiedź „lec z /sdd-spec” oznacza akceptację wszystkich 12 propozycji | Tak (Założenia) — odczytanie rozsądne, ale część propozycji wymaga jawnego potwierdzenia (Non-Critical 14) |
| Produkcja użyje przekierowania `/api/*` na Vercel (same-origin) i SF-4 je dostarczy | Tak (Założenia 3, Zależności) |
| Vercel przekazuje ciasteczka i nagłówki bez zmian, a API widzi prawdziwy adres klienta | Nie — ukryte; dotyczy limitów per IP (Non-Critical 5) |
| Adres właściciela w konfiguracji się nie zmienia | Nie — ukryte (Non-Critical 1) |
| Wylogowanie wystarczy jako usunięcie ciasteczka | Nie — ukryte (Non-Critical 2) |
| Ekran logowania zawsze pobiera świeży token CSRF przed logowaniem | Częściowo — wynika z tabeli wyjątków („przed logowaniem”), brak w punkcie 24 (Non-Critical 3) |
| Kopia bazy i dostęp do niej są zaufane (klucze Data Protection jawne w bazie) | Nie — ukryte (Non-Critical 12) |
| Skrzynka pod `StoneFlow:Owner:Email` jest wyłącznie w rękach właściciela (tworzenie konta przez reset) | Nie — ukryte; naturalne dla procesu resetu, warto zapisać |
| Rate limiting w pamięci, lockout w bazie; restart zeruje tylko limity | Tak |
| Styl endpointów Minimal API, `HttpClient` z sygnałami | Tak |
| Wysyłka e-mail synchroniczna w żądaniu | Nie — wynika pośrednio z przypadku błędu SMTP (Non-Critical 11) |
| Start API nie zapisuje do bazy, konto powstaje przy pierwszej prośbie o reset | Tak (zgodnie z SF-3) |

## Recommendation

- Revise specification — przed `sdd-plan` uzupełnić punkty Non-Critical 1–5 i 8 (reguły bezpieczeństwa, których planista nie powinien zgadywać), a punkty 13–14 potwierdzić z właścicielem. Ocena PASS WITH ISSUES nie blokuje planowania.
