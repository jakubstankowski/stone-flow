# Stone Flow — PRD

Wersja: 0.1 · Data: 28.10.2026 · Status: projekt wymagań do uzgodnienia.

Dokument opisuje system ERP dla sklepu z kamieniem naturalnym. Ustalony zakres i ograniczenia odróżniono od propozycji oraz decyzji otwartych. Dokumentem towarzyszącym jest [design guide](DESIGN-GUID-stone-flow.md).

## 1. Źródła i sposób interpretacji

| Źródło | Wykorzystanie |
| --- | --- |
| [Wymagania biznesowe](../documents/wymagania%20biznesowe.docx) | Cele, użytkownicy, ZAM-01–07, WYD-01–07, FIN-01–06, zakres MVP i pytania biznesowe |
| [Wymagania technologiczne](../documents/wymagania%20technologiczne.docx) | Stos technologiczny, architektura, bezpieczeństwo, hosting, paleta i odbiór |
| [Ewidencja sprzedaży](../documents/ewidencja-sprzedazy-2026-DEMO.xlsx) | Arkusze CZERWIEC, LIPIEC, SIERPIEŃ, WRZESIEŃ oraz PODSUMOWANIE ROKU; pola zamówienia, kanały, dostawcy i rozliczenia |
| [Zestawienie finansowe](../documents/zestawienie-finansowe-2026-DEMO.xlsx) | Arkusze czerwiec–wrzesień; operacje finansowe, kategorie, dokumenty i wypłaty |
| [Dashboard referencyjny](../documents/example-dashboard.webp) | Hierarchia informacji i kierunek wizualny, bez rozszerzania zakresu ERP o moduły widoczne na obrazie |

Dokumenty wymagań określają zakres. Arkusze pokazują praktykę pracy i problemy danych; ich formuły nie są automatycznie prawidłową specyfikacją finansową. Dane DEMO nie są wzorcem produkcyjnych danych osobowych. W dokumentacji należy stosować określenie „sklep z kamieniem naturalnym”, bez nazwy handlowej i domeny.

**Decyzja użytkownika z 28.10.2026:** system ma udostępniać oba widoki: sprzedaż oraz przepływy pieniężne. Nie wolno utożsamiać wartości zamówienia z otrzymaną zapłatą.

## 2. Problem i cel produktu

Obecna praca wymaga prowadzenia dwóch arkuszy, przepisywania zamówień i ręcznego zestawiania sprzedaży, wydatków i wypłat. Rozproszone dane utrudniają ustalenie wyniku oraz wykrycie nieopłaconych zamówień i brakujących kosztów.

Stone Flow ma zapewnić jedno miejsce do obsługi zamówień internetowych i ręcznych, ewidencji wydatków jednorazowych i miesięcznych oraz analizy sprzedaży i przepływów pieniężnych. Wyniki muszą być możliwe do prześledzenia do danych źródłowych.

## 3. Użytkownicy i dostęp

- Właściciel: kontroluje wyniki, wydatki, zamówienia i działanie integracji.
- Upoważniony pracownik: obsługuje zamówienia i/lub finanse zgodnie z nadanymi uprawnieniami.
- Integracja i zadania automatyczne: działają technicznie, bez przyznawania dostępu interaktywnego.

Propozycja uprawnień: niezależne możliwości odczytu i edycji zamówień, odczytu i edycji finansów oraz zarządzania integracją i dostępem. Podział na konkretne role wymaga zatwierdzenia; nie zakładamy, że każdy pracownik widzi wynik finansowy. API egzekwuje każdą operację niezależnie od widoczności elementów UI. Należy obsłużyć wygaśnięcie sesji i odebranie dostępu.

Wejście z panelu administracyjnego sklepu jest wymagane. Wspólne logowanie z WooCommerce jest preferencją wymagającą analizy bezpieczeństwa; sam link nie uwierzytelnia użytkownika.

## 4. Zakres

### MVP

1. Uwierzytelnianie i autoryzacja.
2. Wspólna lista zamówień ręcznych i z WooCommerce, szczegóły i edycja danych należących do ERP.
3. Ewidencja wydatków jednorazowych i automatycznych wystąpień wydatków miesięcznych.
4. Rozdzielenie wartości sprzedaży od rzeczywistych wpłat i wypłat, potrzebne do dwóch uzgodnionych widoków raportowania.
5. Pulpit finansowy, filtry okresu, wykresy oraz dane szczegółowe.
6. Widoczność błędów synchronizacji, ponowienia i zabezpieczenie przed duplikatami.

### Poza potwierdzonym zakresem

Magazyn, fakturowanie, księgowość, deklaracje podatkowe, integracja KSeF, zakupy, CRM, portal klienta, prognozowanie sprzedaży i obsługa wielu firm. Numery faktur, dostawcy, zdjęcia z realizacji i opinie występują w danych, ale nie uzasadniają budowy osobnych modułów. Flagi zdjęć/opinii można zachować; przechowywanie plików i publikowanie opinii wymaga odrębnej decyzji.

Migracja historii z Excela jest propozycją uruchomieniową, a nie potwierdzoną funkcją importu samoobsługowego.

## 5. Najważniejsze procesy

### Zamówienie ręczne

Pracownik otwiera „Dodaj zamówienie”, wpisuje klienta, datę, kanał i wartość, uzupełnia dostępne dane realizacji oraz zapisuje. System waliduje dane i chroni przed ponownym wysłaniem. Zamówienie pojawia się na wspólnej liście. Zapis zamówienia sam w sobie nie oznacza otrzymania pieniędzy.

### Zamówienie WooCommerce

Backend pobiera zamówienie, identyfikuje je według źródła i zewnętrznego ID oraz tworzy lub aktualizuje istniejący rekord. Ponowne dostarczenie nie tworzy drugiego zamówienia. Pola operacyjne ERP pozostają zachowane zgodnie z uzgodnioną własnością pól. Błąd ma status, czas, kontekst i możliwość ponowienia. Po awarii system uzupełnia pominięte dane.

### Wydatek jednorazowy i miesięczny

Użytkownik dodaje opis, kategorię, datę i kwoty. Przy wydatku miesięcznym definiuje również harmonogram i okres aktywności. Trwały mechanizm tworzy najwyżej jedno wystąpienie na definicję i okres, także po restarcie i ponowieniu. Planowany wydatek nie może automatycznie udawać potwierdzonego wypływu środków. Dokładna reguła księgowania wystąpienia wymaga decyzji D-04.

### Analiza finansów

Użytkownik wybiera „Sprzedaż” albo „Przepływy pieniężne” i okres. Karty, wykresy i zestawienie korzystają z tych samych reguł i filtrów. Widok podaje podstawę kwot, datę przypisania do okresu i aktualność danych. Niekompletna integracja jest widoczna obok wyników.

## 6. Wymagania funkcjonalne i odbiór

| ID źródłowe / projektowe | Wymaganie | Kryterium akceptacji |
| --- | --- | --- |
| ZAM-01, 03, 05 | Ręczne zamówienia i wspólna lista | Zamówienie ręczne oraz internetowe są widoczne razem; źródło techniczne i kanał sprzedaży są rozróżnione |
| ZAM-02, 06 | Synchronizacja WooCommerce | Wielokrotne dostarczenie tego samego ID daje jeden rekord; błąd można rozpoznać i ponowić |
| ZAM-04 | Szczegóły i edycja | Użytkownik poprawia dozwolone pola; równoczesna zmiana nie nadpisuje bez ostrzeżenia nowszych danych |
| ZAM-07 | Dane zamówień w sprzedaży | Każda uwzględniona wartość pochodzi z identyfikowalnego zamówienia i zatwierdzonej reguły okresu |
| WYD-01, 02 | Dodawanie, poprawianie i usuwanie błędnych wydatków | Zmiana aktualizuje raport; usunięcie wymaga potwierdzenia, pozostawia audyt i nie usuwa historii bez śladu |
| WYD-03–06 | Wydatki miesięczne | Aktywny harmonogram tworzy jedno wystąpienie za okres; zatrzymanie blokuje kolejne; ponowienie nie duplikuje |
| WYD-07 | Wydatki w raportach | Wydatek jest liczony raz zgodnie z datą, kategorią i stanem rozliczenia |
| FIN-01–04 | Pulpit i analiza w czasie | Zmiana okresu aktualizuje karty, wykresy i tabelę; wartości dostępne są również tekstowo |
| FIN-05, 06 | Spójne i wyjaśnione obliczenia | Raport obejmuje właściwe źródła i opisuje reguły; sumę można uzgodnić z listą operacji |
| FIN-07, decyzja użytkownika | Dwa widoki finansowe | Nieopłacone zamówienie nie zwiększa rzeczywistych wpływów; wpłata nie tworzy drugiej sprzedaży |
| DOST-01 | Kontrola dostępu | Bez sesji API odmawia dostępu; bez uprawnienia odmawia danej operacji i odczytu finansów |
| INT-01 | Stan integracji | UI pokazuje ostatnią udaną synchronizację, błąd i dostępną akcję; nie sugeruje kompletności przy awarii |

## 7. Model informacji wynikający z arkuszy

Poniższy model jest propozycją logiczną; nie narzuca tabel ani kontraktów API.

| Obiekt | Dane i reguły |
| --- | --- |
| Zamówienie | ID ERP, źródło techniczne, ID WooCommerce, kanał, data zamówienia, klient, wartość zamówienia, status realizacji, komentarz |
| Klient przy zamówieniu | Osoba lub firma, dane kontaktowe, adres, opcjonalny NIP; rozdzielone pola zamiast jednego bloku tekstu |
| Dane operacyjne | Dostawca lub dostawcy, referencje ZO, numer faktury, flagi zdjęć i opinii, dodatkowe uwagi |
| Rozliczenie / płatność | Powiązanie z zamówieniem, kwota, waluta, data rzeczywistej operacji, metoda, identyfikator źródłowy; szczegóły płatności częściowych w D-03 |
| Wydatek | Opis, kategoria, kontrahent, data biznesowa, netto/brutto i podstawa VAT, referencja dokumentu, status płatności, metoda, płatnik, komentarz, opcjonalne powiązanie z zamówieniem |
| Definicja cykliczna | Kwota i kategoria, dzień miesiąca, początek i koniec, aktywność; wersjonowanie zmian przyszłych wystąpień |
| Wystąpienie cykliczne | ID definicji, okres, termin, kwota, stan; unikalna para definicja–okres |
| Synchronizacja | Źródło, zewnętrzne ID, stan, czas próby i sukcesu, bezpieczny opis błędu, identyfikator korelacji |
| Audyt | Autor, czas UTC, obiekt, operacja i zakres zmiany finansowej |

### Słowniki i rozdzielenie pojęć

- Kanały z arkusza: sklep, OLX, znajomy, inne. Telefon wynika z wymagań biznesowych. Kanał nie jest tym samym co źródło techniczne „ręczne / WooCommerce”.
- Metody płatności: przelew, BLIK, gotówka, pobranie. „Nieopłacone” jest stanem płatności, a osoba przyjmująca gotówkę osobnym polem.
- Statusy realizacji: w trakcie, zakończone, problem/reklamacja; mapowanie anulowań, zwrotów i statusów WooCommerce wymaga D-02/D-03.
- Kategorie kosztów: zakup materiału u dostawców, cięcie, transport HDS, marketing (Google Ads, OLX, SEO, media społecznościowe), pozostała działalność, podatki VAT i PPE. Podatek wpisany w ewidencji nie oznacza automatycznego wyliczania zobowiązania podatkowego.
- Wypłaty właścicieli wymagają osobnej klasyfikacji. Nie należy automatycznie uznać ich za koszty działalności ani odejmować dwukrotnie.
- Dostawca może być więcej niż jeden; numer ZO i numer faktury nie są jednym identyfikatorem. Flagi zdjęć i opinii nie oznaczają integracji z usługą opinii.

## 8. Raportowanie i zasady finansowe

### Uzgodnione

Dostępne są dwa rozdzielone widoki: sprzedaż oraz przepływy pieniężne. Obliczenia wykonuje backend. Kwoty są typu decimal, z jawną precyzją, skalą i regułą zaokrąglania; nie wolno liczyć pieniędzy typami zmiennoprzecinkowymi. UI stosuje polskie formatowanie i podaje walutę.

### Definicje robocze do zatwierdzenia w D-01

| Widok | Znaczenie proponowane | Nierozstrzygnięte |
| --- | --- | --- |
| Sprzedaż | Wartość sprzedaży z kwalifikujących się zamówień po korektach, niezależna od wpłat | Netto/brutto, statusy kwalifikujące, data ujęcia, korekty i koszty przypisane do sprzedaży |
| Przepływy pieniężne | Rzeczywiste wpływy minus rzeczywiste wypływy w okresie operacji pieniężnej | Potwierdzanie płatności, zwroty, wypłaty właścicieli i ewentualne saldo początkowe |

Nie nazywać zmiany środków „zyskiem”, a wartości sprzedaży „otrzymanymi wpłatami”. „Wynik zarządczy”, „wynik przed wypłatami” i „pozostało po wypłatach” można pokazać dopiero po zdefiniowaniu składowych. Nie prezentować salda rachunku na podstawie samych operacji bez salda początkowego.

Zabezpieczenia wymagane przy projektowaniu obliczeń:

1. Wpływ z zamówienia i odpowiadający mu historyczny wpis przychodowy nie mogą być liczone dwa razy.
2. Koszt powiązany z zamówieniem i wydatek w ewidencji muszą wskazywać jedno zdarzenie, jeżeli dotyczą tej samej operacji.
3. Planowany wydatek i niezapłacone zamówienie nie zwiększają rzeczywistych przepływów.
4. Kwota zwrotu jest odrębnym zdarzeniem, a nie utratą historii zamówienia. Reguły korekty sprzedaży wymagają zatwierdzenia.
5. Wartość zero, brak danych i niekompletne dane mają odmienne znaczenia.
6. Nie przeliczać wszystkich kwot netto przez stałe dzielenie przez 1,23. Taki wzór występuje w DEMO, ale stawki i zasady VAT trzeba ustalić dla konkretnych operacji.

Propozycja zakresu raportów: bieżący miesiąc, poprzedni miesiąc, rok i własny zakres; agregacja miesięczna oraz porównanie okresów. Kategorie i kanały powinny prowadzić do danych szczegółowych. Porównanie procentowe przy bazie równej zero pokazuje „brak podstawy porównania”. Liczba zamówień i liczba unikalnych klientów muszą mieć odrębne definicje.

## 9. Dane historyczne i jakość

W źródłach występują: daty z lat niezgodnych z nazwą pliku, tekst „31.09.2026”, wpisy z innych miesięcy niż zakładka, różne zakresy SUMIF dla przychodów i kosztów, warianty nazw kategorii, wartości tekstowe w polach liczbowych oraz komentarze zastępujące strukturalne statusy. Miesięczne podsumowanie roczne zawiera także wartości ujemne, więc nie wolno bez analizy odrzucić wszystkich ujemnych kwot.

Jeżeli migracja zostanie zatwierdzona:

1. Zmapować kolumny wszystkich miesięcy; nagłówki są na różnych wierszach.
2. Zachować plik, arkusz i wiersz pochodzenia; pomijać puste wiersze i techniczne formuły numeracji.
3. Znormalizować słowniki i daty dopiero po pokazaniu niejednoznaczności do weryfikacji. Nie naprawiać dat przez zgadywanie.
4. Uzgodnić powiązania sprzedaży z operacjami finansowymi i usunąć podwójne ujęcie zdarzeń.
5. Przed zapisem przedstawić raport poprawnych, błędnych i podejrzanych rekordów.
6. Uzgodnić sumy miesięczne, różnice wobec obu arkuszy i zaakceptować mapowanie. Ponowienie migracji nie może tworzyć duplikatów.

Nie traktować zapisanych wartości formuł ani ręcznych podsumowań jako niepodważalnego wyniku kontrolnego.

## 10. Wymagania techniczne i jakościowe

| Obszar | Wymaganie |
| --- | --- |
| Frontend | Angular i TypeScript strict, standalone, podział funkcjonalny, lazy loading; Tailwind CSS i własny motyw daisyUI |
| Backend | ASP.NET Core, JSON API, OpenAPI, DTO oddzielone od encji, Problem Details, asynchroniczne I/O i anulowanie |
| Architektura | Modularny monolit: zamówienia, wydatki, raportowanie, integracje; Onion Architecture, SOLID, DRY, CQRS |
| Granice | Moduły posiadają własne dane i reguły; komunikacja przez jawne interfejsy lub zdarzenia. Domena nie zależy od HTTP, EF Core ani hostingu |
| CQRS | Rozdzielenie komend i zapytań; bez domyślnego event sourcingu, brokera lub osobnych baz |
| Dane | SQL Server i EF Core, wersjonowane migracje, własność tabel/schematów, ograniczenia unikalności, transakcje i kontrola współbieżności |
| Czas | Daty biznesowe oddzielone od technicznych znaczników UTC; prezentacja Europe/Warsaw |
| Repozytorium | Monorepo dla frontendu, backendu, testów, dokumentacji i konfiguracji; instrukcja startu i przykłady bez sekretów |
| Wersje | Najnowsze stabilne i zgodne wersje przy starcie implementacji; zapisane SDK/runtime/Node, lockfile, global.json i centralne wersje pakietów .NET |
| Hosting | Frontend Vercel z routingiem SPA; backend SMART ASP.NET; potwierdzenie zgodności planu z .NET, SQL Server i trwałymi zadaniami przed implementacją |
| Bezpieczeństwo | HTTPS, walidacja i autoryzacja API, sekrety tylko po stronie serwera, jawny CORS i model sesji; CSRF przy cookies |
| Integracje | Komunikacja WooCommerce wyłącznie z backendu; autentyczność webhooków, ponowienia, historia błędów, uzupełnianie luk |
| Automatyzacja | Harmonogram odporny na restart i usypianie hostingu; nie opiera się wyłącznie na timerze w pamięci |
| Obserwowalność | Health endpoint, monitoring, logi z korelacją bez sekretów i zbędnych danych osobowych, audyt finansów |
| UI | Polski język i formaty, responsywność, WCAG 2.2 AA, klawiatura i fokus, stany ładowania/pustki/błędu, walidacja i ochrona zapisu |
| Listy | Filtrowanie, sortowanie i paginacja przez API przy wzroście danych |
| Wdrożenia | CI kompiluje obie aplikacje i sprawdza typy, formatowanie i właściwe testy; automatyczne procesy publikacji frontendu i backendu po wymaganych kontrolach; kontrolowana kolejność migracji; ograniczone uprawnienia do produkcji i sekretów wdrożeniowych; izolacja preview od produkcji |
| Odzyskiwanie | Ustalona retencja i harmonogram kopii, przetestowane odtworzenie oraz powrót aplikacji; rollback kodu nie oznacza cofnięcia migracji |

Dokument źródłowy wymienia .NET 10 LTS, Angular 22, Tailwind CSS 4 i daisyUI 5 jako punkt odniesienia. Nie jest to weryfikacja aktualnych wydań ani deklaracja zgodności hostingu; należy potwierdzić je przy inicjalizacji. Cele wydajnościowe, wolumen danych, RPO i RTO pozostają do uzgodnienia.

## 11. Warunki odbioru MVP

- Codzienna obsługa uzgodnionego zakresu nie wymaga równoległego prowadzenia obu arkuszy.
- Zamówienie ręczne i z WooCommerce przechodzą pełny proces; ponowienia nie duplikują danych i nie nadpisują bez reguł pól ERP.
- Jednorazowy i miesięczny wydatek są widoczne we właściwym raporcie; ponowienie zadania, restart oraz zakończenie cyklu działają poprawnie.
- Oba widoki finansowe mają zatwierdzone definicje i uzgadniają się z rekordami źródłowymi. Scenariusze obejmują niezapłacone zamówienie, wpłatę w innym miesiącu, korektę, zwrot i wydatek planowany.
- API odrzuca nieuprawniony odczyt i zapis; wygaśnięcie sesji nie ujawnia danych.
- Kluczowe procesy mają testy E2E; reguły domenowe i finansowe testy jednostkowe, a zapis, autoryzacja i unikalność testy integracyjne.
- CI przechodzi, oba hostingi są zweryfikowane, SPA działa po odświeżeniu podstrony, migracja i odtworzenie kopii zostały sprawdzone.
- UI spełnia [design guide](DESIGN-GUID-stone-flow.md), w tym kontrast, obsługę klawiatury i prezentację niekompletności danych.

## 12. Rejestr decyzji do doprecyzowania

Pytania nie blokują przygotowania dokumentacji, lecz odpowiednie reguły należy zatwierdzić przed implementacją danego obszaru.

| ID | Decyzja / pytanie | Znaczenie |
| --- | --- | --- |
| D-01 | Dwa widoki są uzgodnione. Czy sprzedaż liczymy netto czy brutto, według jakiej daty i statusów? Jak przypisujemy koszty, podatki i wypłaty właścicieli? | Blokuje definicję KPI i zgodność raportów |
| D-02 | Jakie statusy, okres historii i częstotliwość importu WooCommerce? Które pola aktualizuje integracja, które ERP? | Blokuje kontrakt synchronizacji |
| D-03 | Czy MVP obejmuje zaliczki i płatności częściowe? Jak potwierdzamy pobrania, anulowania, zwroty i reklamacje? | Dwa widoki wymagają danych o rzeczywistych operacjach; status realizacji nie wystarcza |
| D-04 | Czy wystąpienie cykliczne jest planem czy zaksięgowanym kosztem? Co z dniem 29–31, przerwą w działaniu i zmianą kwoty? | Propozycja: ostatni dzień krótszego miesiąca, uzupełnianie zaległych wystąpień, potwierdzanie faktycznej płatności osobno |
| D-05 | Kto ma dostęp do pełnych finansów, usuwania danych i konfiguracji? Jak działa logowanie i ewentualne SSO? | Blokuje macierz dostępu |
| D-06 | Czy importujemy historię, z jakiego okresu i który zapis rozstrzyga rozbieżności między arkuszami? | Blokuje migrację i bilans otwarcia |
| D-07 | Czy koszty zakupów, cięcia i transportu mają być wiązane z konkretnymi zamówieniami? | Wpływa na rentowność zamówienia i zapobieganie podwójnemu ujęciu |
| D-08 | Jaki plan SMART ASP.NET i jakie parametry backupu, dostępności oraz wydajności? Czy poza PLN występują inne waluty? | Blokuje ostateczną konfigurację środowisk i reguły walutowe |

## 13. Kolejność dalszych prac

Najpierw uzgodnić D-01–D-05 i sprawdzić hosting. Następnie opisać model domenowy, własność pól i kontrakty API, przygotować środowiska i dostęp, wdrożyć zamówienia, wydatki oraz dwa widoki raportowania. Migrację historii wykonać po osobnej decyzji i uzgodnieniu danych. Uruchomienie produkcyjne wymaga odbioru scenariuszy z sekcji 11.
