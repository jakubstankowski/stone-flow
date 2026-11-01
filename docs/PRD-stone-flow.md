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

**Decyzja użytkownika z 01.11.2026:** wpłaty za zamówienia są osobnymi wpisami dodawanymi ręcznie, także dla zamówień z WooCommerce. Stan płatności zamówienia jest wyliczany z wpłat. Zapłatę wydatku wyznacza data zapłaty. Wypłaty właścicieli są osobnym rodzajem operacji.

## 2. Problem i cel produktu

Obecna praca wymaga prowadzenia dwóch arkuszy, przepisywania zamówień i ręcznego zestawiania sprzedaży, wydatków i wypłat. Rozproszone dane utrudniają ustalenie wyniku oraz wykrycie nieopłaconych zamówień i brakujących kosztów.

Stone Flow ma zapewnić jedno miejsce do obsługi zamówień internetowych i ręcznych, ewidencji wydatków jednorazowych i miesięcznych oraz analizy sprzedaży i przepływów pieniężnych. Wyniki muszą być możliwe do prześledzenia do danych źródłowych.

## 3. Użytkownicy i dostęp

- Właściciel: jedyna rola w systemie, z jednym kontem i pełnym dostępem do zamówień, finansów i integracji.
- Integracja i zadania automatyczne: działają technicznie, bez przyznawania dostępu interaktywnego.

**Decyzja użytkownika z 01.11.2026 (D-05):** MVP ma jedną rolę i jedno konto. Jest to świadome zawężenie wymagań biznesowych, które przewidują także upoważnione osoby. W systemie nie ma zarządzania kontami, a audyt wskazuje to konto jako autora zmian.

Logowanie odbywa się e-mailem i hasłem kontem ERP. Reset hasła działa przez jednorazowy link e-mail. Sesja wygasa po 60 minutach bezczynności, a wygaśnięcie nie ujawnia danych. API egzekwuje uwierzytelnienie przy każdej operacji niezależnie od widoczności elementów UI.

Wejście z panelu administracyjnego sklepu jest wymagane i działa jako link do strony logowania ERP; sam link nie uwierzytelnia użytkownika. Wspólne logowanie z WooCommerce jest poza MVP i wymaga osobnej analizy bezpieczeństwa.

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

Migracja historii z Excela nie wchodzi do MVP ani do warunków odbioru (decyzja użytkownika z 01.11.2026, D-06). Raporty obejmują dane od daty uruchomienia, a arkusze pozostają archiwum. Saldo rachunku i saldo początkowe również są poza MVP.

## 5. Najważniejsze procesy

### Zamówienie ręczne

Użytkownik otwiera „Dodaj zamówienie”, wpisuje klienta, datę, kanał i wartość, uzupełnia dostępne dane realizacji oraz zapisuje. System waliduje dane i chroni przed ponownym wysłaniem. Zamówienie pojawia się na wspólnej liście. Zapis zamówienia sam w sobie nie oznacza otrzymania pieniędzy.

### Zamówienie WooCommerce

Zamówienia docierają na bieżąco przez webhooki, których autentyczność backend weryfikuje. Raz dziennie o 23:00 Europe/Warsaw backend odpytuje WooCommerce REST API o zmiany od ostatniego sukcesu i uzupełnia luki. Pierwsze pobranie obejmuje zamówienia od daty uruchomienia; historię rozstrzyga D-06. Pominięty webhook może oznaczać opóźnienie do następnego uzupełniania, dlatego stan integracji jest widoczny przy wynikach.

Backend identyfikuje zamówienie według źródła i zewnętrznego ID oraz tworzy lub aktualizuje istniejący rekord. Ponowne dostarczenie nie tworzy drugiego zamówienia. Do ERP trafiają wszystkie statusy sklepu. Zamówienia pending, failed i szkice są oznaczone statusem ze sklepu, nie liczą się do sprzedaży i domyślnie są ukryte na liście, ale dostępne filtrem; do sprzedaży wchodzą po zmianie statusu w sklepie.

Własność pól: WooCommerce odpowiada za ID, datę, dane klienta i wartość; te pola są w ERP tylko do odczytu, a synchronizacja je aktualizuje. ERP odpowiada za status realizacji, dostawców, ZO, fakturę, flagi, uwagi i wpłaty; synchronizacja ich nie zmienia. Kanał zamówienia ze sklepu to „sklep”.

Zmiany w sklepie po imporcie: anulowanie w sklepie oznacza zamówienie w ERP jako anulowane, więc wypada ono ze sprzedaży. Jeżeli ma wpłaty, użytkownik widzi ostrzeżenie, a wpłaty pozostają. Zwrot w sklepie jest informacją i nie tworzy automatycznie wypływu.

Zamówienie w walucie innej niż PLN nie trafia do raportów, lecz do historii błędów synchronizacji.

Błąd ma status, czas, kontekst i trafia do historii błędów. Kolejne uzupełnianie pobiera dane od ostatniego sukcesu, więc luki domykają się same, a użytkownik może też wywołać ponowienie ręcznie.

### Wydatek jednorazowy i miesięczny

Użytkownik dodaje opis, kategorię, datę i kwoty. Przy wydatku miesięcznym definiuje również harmonogram i okres aktywności. Trwały mechanizm tworzy najwyżej jedno wystąpienie na definicję i okres, także po restarcie i ponowieniu. Planowany wydatek nie może automatycznie udawać potwierdzonego wypływu środków.

Wystąpienie jest zwykłym wydatkiem bez daty zapłaty, z datą kosztu równą terminowi. Liczy się do kosztów, a do wypływów dopiero po oznaczeniu jako opłacone. W krótszym miesiącu przypada na jego ostatni dzień. Po przerwie w działaniu system tworzy wszystkie zaległe wystąpienia. Zmiana kwoty w definicji dotyczy tylko wystąpień jeszcze nieutworzonych; utworzone można poprawić pojedynczo. Usunięte wystąpienie nie wraca przy uzupełnianiu, bo ślad usunięcia nadal blokuje parę definicja–okres; w razie pomyłki wydatek dodaje się ręcznie.

### Rejestracja płatności

Wpłata za zamówienie: użytkownik otwiera szczegóły zamówienia i dodaje wpłatę z kwotą, datą operacji i metodą. Dotyczy to zamówień ręcznych i z WooCommerce; integracja nie tworzy wpłat, a status opłacenia w WooCommerce nie potwierdza otrzymania pieniędzy. Zamówienie może mieć wiele wpłat, także zaliczki. Stan płatności zamówienia jest wyliczany z wpłat i nie jest ustawiany ręcznie: Nieopłacone, Częściowo opłacone albo Opłacone. Pobranie jest tylko metodą: wpłatę dodaje się dopiero po otrzymaniu środków, z datą ich otrzymania.

Zapłata wydatku: wydatek ma datę kosztu, opcjonalny termin i datę zapłaty. Użytkownik wpisuje datę zapłaty w formularzu albo później, akcją „Oznacz jako opłacony”. Uzupełniona data zapłaty oznacza stan „Opłacony”; tylko taki wydatek trafia do wypływów, do okresu wyznaczonego tą datą.

Wypłata właściciela: użytkownik dodaje wpis z osobą, kwotą, datą i metodą. Wypłata liczy się do wypływów w przepływach pieniężnych, ale nie do kosztów ani sprzedaży.

### Anulowanie i zwrot

Zamówień nie usuwa się. Błędne albo wycofane zamówienie, ręczne lub ze sklepu, otrzymuje status „Anulowane”, które wyłącza je ze sprzedaży i pozostawia audyt. Jeżeli zamówienie ma wpłaty, użytkownik widzi ostrzeżenie, a pieniądze rozlicza zwrotem.

Zwrot dodaje ręcznie na zamówieniu użytkownik, podając kwotę, datę, metodę i powód. Zwrot liczy się do wypływów i obniża sprzedaż w okresie swojej daty, więc wcześniejsze miesiące się nie zmieniają.

Korekta wartości: wartość zamówienia ręcznego poprawia się bezpośrednio, z audytem; zamówienie ze sklepu zmienia się przez synchronizację. W obu przypadkach sprzedaż zmienia się w okresie daty zamówienia, więc raport za ten okres może się zmienić.

Błędny wpis: użytkownik poprawia lub usuwa wpłatę albo zwrot. Usunięcie wymaga potwierdzenia i zostawia ślad audytu.

### Analiza finansów

Użytkownik wybiera „Sprzedaż” albo „Przepływy pieniężne” i okres. Karty, wykresy i zestawienie korzystają z tych samych reguł i filtrów. Widok podaje podstawę kwot, datę przypisania do okresu i aktualność danych. Niekompletna integracja jest widoczna obok wyników.

## 6. Wymagania funkcjonalne i odbiór

| ID źródłowe / projektowe | Wymaganie | Kryterium akceptacji |
| --- | --- | --- |
| ZAM-01, 03, 05 | Ręczne zamówienia i wspólna lista | Zamówienie ręczne oraz internetowe są widoczne razem; źródło techniczne i kanał sprzedaży są rozróżnione |
| ZAM-02, 06 | Synchronizacja WooCommerce | Wielokrotne dostarczenie tego samego ID, webhookiem lub przez uzupełnianie, daje jeden rekord; pominięty webhook jest uzupełniany przez dzienne odpytanie REST API; synchronizacja nie zmienia pól ERP; błąd można rozpoznać i ponowić |
| ZAM-04 | Szczegóły i edycja | Użytkownik poprawia dozwolone pola; równoczesna zmiana nie nadpisuje bez ostrzeżenia nowszych danych |
| ZAM-07 | Dane zamówień w sprzedaży | Każda uwzględniona wartość pochodzi z identyfikowalnego zamówienia i zatwierdzonej reguły okresu |
| WYD-01, 02 | Dodawanie, poprawianie i usuwanie błędnych wydatków | Zmiana aktualizuje raport; usunięcie wymaga potwierdzenia, pozostawia audyt i nie usuwa historii bez śladu |
| WYD-03–06 | Wydatki miesięczne | Aktywny harmonogram tworzy jedno wystąpienie za okres, także zaległe po przerwie; dzień 29–31 przypada w krótszym miesiącu na ostatni dzień; zatrzymanie blokuje kolejne; ponowienie nie duplikuje; zmiana kwoty nie zmienia utworzonych wystąpień; usunięte wystąpienie nie wraca |
| WYD-07 | Wydatki w raportach | Wydatek jest liczony raz zgodnie z datą, kategorią i stanem rozliczenia |
| FIN-01–04 | Pulpit i analiza w czasie | Widok sprzedaży pokazuje sprzedaż, koszty i wynik przed wypłatami za okres oraz ich zmiany w czasie; zmiana okresu aktualizuje karty, wykresy i tabelę; wartości dostępne są również tekstowo |
| FIN-05, 06 | Spójne i wyjaśnione obliczenia | Raport obejmuje właściwe źródła i opisuje reguły; sumę można uzgodnić z listą operacji |
| FIN-07, decyzja użytkownika | Dwa widoki finansowe | Nieopłacone zamówienie nie zwiększa rzeczywistych wpływów; wpłata nie tworzy drugiej sprzedaży |
| PLAT-01 | Wpłaty za zamówienia | Dodana wpłata pojawia się na zamówieniu, zmienia wyliczony stan płatności i zwiększa wpływy w okresie daty operacji, a sprzedaży nie zmienia; synchronizacja WooCommerce nie tworzy wpłaty |
| PLAT-02 | Zapłata wydatku | Wydatek bez daty zapłaty nie zwiększa wypływów; po jej uzupełnieniu jest opłacony i liczony w okresie tej daty |
| PLAT-03 | Wypłaty właścicieli | Wpis zwiększa wypływy, nie zmienia kosztów ani sprzedaży |
| PLAT-04 | Płatności częściowe i pobranie | Kilka wpłat sumuje się na zamówieniu, a stan zmienia się z Nieopłacone przez Częściowo opłacone na Opłacone; pobranie bez dodanej wpłaty pozostaje nieopłacone |
| ZAM-08 | Anulowanie zamówienia | Zamówienia nie można usunąć; anulowane wypada ze sprzedaży, zostawia audyt i ostrzega o istniejących wpłatach |
| PLAT-05 | Zwroty | Zwrot zwiększa wypływy i obniża sprzedaż w okresie swojej daty, bez zmiany wcześniejszych okresów i bez utraty historii zamówienia |
| PLAT-06 | Korekty i błędne wpisy | Korekta wartości zmienia sprzedaż w okresie daty zamówienia i zostawia audyt; poprawa lub usunięcie wpłaty albo zwrotu aktualizuje stan płatności i raporty oraz zostawia audyt |
| KWO-01 | Reguły kwot | Kwoty są zapisywane do grosza w PLN; suma raportu równa się sumie zapisanych kwot; zamówienie ze sklepu w innej walucie jest widoczne jako błąd synchronizacji i nie wchodzi do raportów |
| DOST-01 | Kontrola dostępu | Bez ważnej sesji API odmawia każdego odczytu i zapisu; sesja wygasa po 60 minutach bezczynności; reset hasła działa przez jednorazowy link e-mail; link z panelu sklepu nie uwierzytelnia |
| INT-01 | Stan integracji | UI pokazuje ostatnią udaną synchronizację, błąd i dostępną akcję; nie sugeruje kompletności przy awarii |

## 7. Model informacji wynikający z arkuszy

Poniższy model jest propozycją logiczną; nie narzuca tabel ani kontraktów API.

| Obiekt | Dane i reguły |
| --- | --- |
| Zamówienie | ID ERP, źródło techniczne, ID WooCommerce, kanał, data zamówienia, klient, wartość brutto (dla sklepu suma zamówienia z dostawą), status realizacji, status ze sklepu, stan płatności wyliczany z wpłat, komentarz |
| Klient przy zamówieniu | Osoba lub firma, dane kontaktowe, adres, opcjonalny NIP; rozdzielone pola zamiast jednego bloku tekstu |
| Dane operacyjne | Dostawca lub dostawcy, referencje ZO, numer faktury, flagi zdjęć i opinii, dodatkowe uwagi |
| Wpłata | Powiązanie z zamówieniem, kwota, data rzeczywistej operacji, metoda; zamówienie może mieć wiele wpłat |
| Wydatek | Opis, kategoria, kontrahent, data kosztu, opcjonalny termin, data zapłaty wyznaczająca stan „Opłacony”, kwota brutto (wymagana) i netto (opcjonalna) przepisane z dokumentu, referencja dokumentu, metoda, płatnik, komentarz, opcjonalne powiązanie z zamówieniem |
| Wypłata właściciela | Osoba, kwota, data, metoda; odrębna od wydatku |
| Zwrot | Powiązanie z zamówieniem, kwota, data, metoda, powód; odrębny od wpłaty |
| Definicja cykliczna | Kwota i kategoria, dzień miesiąca, początek i koniec, aktywność; wersjonowanie zmian przyszłych wystąpień |
| Wystąpienie cykliczne | ID definicji, okres, termin, kwota, stan; unikalna para definicja–okres |
| Synchronizacja | Źródło, zewnętrzne ID, stan, czas próby i sukcesu, bezpieczny opis błędu, identyfikator korelacji |
| Audyt | Autor, czas UTC, obiekt, operacja i zakres zmiany finansowej |

### Słowniki i rozdzielenie pojęć

- Kanały z arkusza: sklep, OLX, znajomy, inne. Telefon wynika z wymagań biznesowych. Kanał nie jest tym samym co źródło techniczne „ręczne / WooCommerce”.
- Metody płatności: przelew, BLIK, gotówka, pobranie. „Nieopłacone” jest stanem płatności, a osoba przyjmująca gotówkę osobnym polem.
- Statusy realizacji: w trakcie, zakończone, problem/reklamacja, anulowane. Status realizacji należy do ERP; status ze sklepu jest osobnym polem i poza anulowaniem nie zmienia statusu realizacji. Reguły zwrotów i korekt wymagają D-03.
- Kategorie kosztów: zakup materiału u dostawców, cięcie, transport HDS, marketing (Google Ads, OLX, SEO, media społecznościowe), pozostała działalność, podatki VAT i PPE. Podatek wpisany w ewidencji nie oznacza automatycznego wyliczania zobowiązania podatkowego.
- Wypłaty właścicieli są osobnym rodzajem operacji (patrz „Rejestracja płatności”). Nie są kosztami działalności i nie wolno odejmować ich dwukrotnie.
- Dostawca może być więcej niż jeden; numer ZO i numer faktury nie są jednym identyfikatorem. Flagi zdjęć i opinii nie oznaczają integracji z usługą opinii.

## 8. Raportowanie i zasady finansowe

### Uzgodnione

Dostępne są dwa rozdzielone widoki: sprzedaż oraz przepływy pieniężne. Obliczenia wykonuje backend. Nie wolno liczyć pieniędzy typami zmiennoprzecinkowymi. UI stosuje polskie formatowanie i podaje walutę.

Reguły kwot (decyzja użytkownika z 01.11.2026): kwoty są typu `decimal(18,2)`, zapisywane do grosza. Ewentualne zaokrąglenie odbywa się połową od zera (0,005 → 0,01). Sumy są sumami zapisanych kwot, bez ponownego zaokrąglania. MVP działa wyłącznie w PLN, bez kursów walut. Zamówienie ma tylko wartość brutto, bez netto i stawki VAT. Wydatek ma kwotę brutto i opcjonalną kwotę netto przepisane z dokumentu; system nie wylicza ich ze stawki i jej nie przechowuje. Raporty używają kwot brutto.

### Definicje widoków

Decyzja użytkownika z 01.11.2026 (D-01, D-03, D-04).

| Widok | Znaczenie | Nierozstrzygnięte |
| --- | --- | --- |
| Sprzedaż | Wartość brutto zamówień wszystkich statusów poza anulowanymi oraz poza zamówieniami pending, failed i szkicami ze sklepu, przypisana do okresu według daty zamówienia, niezależna od wpłat, pomniejszona o zwroty w okresie daty zwrotu | Brak |
| Koszty w widoku sprzedaży | Wszystkie zarejestrowane wydatki brutto, opłacone i nieopłacone, według daty kosztu; obejmują kategorie VAT i PPE. Wystąpienia cykliczne liczą się jak wydatki. System liczy wpisane kwoty i nie wylicza zobowiązania podatkowego | Brak |
| Wynik przed wypłatami | Sprzedaż minus koszty w tym samym okresie; wypłaty właścicieli go nie obniżają. Pokazywany w widoku sprzedaży z opisem składników | Brak |
| Przepływy pieniężne | Rzeczywiste wpływy (wpłaty) minus rzeczywiste wypływy (opłacone wydatki, wypłaty właścicieli, zwroty) w okresie operacji pieniężnej; bez salda rachunku | Brak |

Nie nazywać zmiany środków „zyskiem”, a wartości sprzedaży „otrzymanymi wpłatami”. „Wynik zarządczy” i „pozostało po wypłatach” można pokazać dopiero po zdefiniowaniu składowych. Nie prezentować salda rachunku na podstawie samych operacji bez salda początkowego.

Zabezpieczenia wymagane przy projektowaniu obliczeń:

1. Wpływ z zamówienia i odpowiadający mu historyczny wpis przychodowy nie mogą być liczone dwa razy.
2. Koszt powiązany z zamówieniem i wydatek w ewidencji muszą wskazywać jedno zdarzenie, jeżeli dotyczą tej samej operacji.
3. Planowany wydatek i niezapłacone zamówienie nie zwiększają rzeczywistych przepływów.
4. Kwota zwrotu jest odrębnym zdarzeniem, a nie utratą historii zamówienia (patrz „Anulowanie i zwrot”).
5. Wartość zero, brak danych i niekompletne dane mają odmienne znaczenia.
6. Nie przeliczać wszystkich kwot netto przez stałe dzielenie przez 1,23. Taki wzór występuje w DEMO, ale stawki i zasady VAT trzeba ustalić dla konkretnych operacji.

Zakres raportów w MVP: bieżący miesiąc, poprzedni miesiąc, rok i własny zakres; agregacja miesięczna oraz porównanie okresów. Kategorie i kanały powinny prowadzić do danych szczegółowych. Porównanie procentowe przy bazie równej zero pokazuje „brak podstawy porównania”. Liczba zamówień i liczba unikalnych klientów muszą mieć odrębne definicje.

## 9. Dane historyczne i jakość

W źródłach występują: daty z lat niezgodnych z nazwą pliku, tekst „31.09.2026”, wpisy z innych miesięcy niż zakładka, różne zakresy SUMIF dla przychodów i kosztów, warianty nazw kategorii, wartości tekstowe w polach liczbowych oraz komentarze zastępujące strukturalne statusy. Miesięczne podsumowanie roczne zawiera także wartości ujemne, więc nie wolno bez analizy odrzucić wszystkich ujemnych kwot.

Jeżeli migracja zostanie zatwierdzona po MVP jako osobna decyzja:

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
- CI przechodzi, oba hostingi są zweryfikowane, SPA działa po odświeżeniu podstrony, migracja schematu bazy i odtworzenie kopii zostały sprawdzone.
- UI spełnia [design guide](DESIGN-GUID-stone-flow.md), w tym kontrast, obsługę klawiatury i prezentację niekompletności danych.

## 12. Rejestr decyzji do doprecyzowania

Pytania nie blokują przygotowania dokumentacji, lecz odpowiednie reguły należy zatwierdzić przed implementacją danego obszaru.

| ID | Decyzja / pytanie | Znaczenie |
| --- | --- | --- |
| D-01 | Rozstrzygnięte 01.11.2026, patrz „Definicje widoków” w sekcji 8. | Definiuje KPI i zgodność raportów |
| D-02 | Rozstrzygnięte 01.11.2026, patrz „Zamówienie WooCommerce” w sekcji 5. Do sprawdzenia w sklepie: czy używa statusów innych niż standardowe. | Okres historii rozstrzyga D-06 |
| D-03 | Rozstrzygnięte 01.11.2026, patrz „Rejestracja płatności” oraz „Anulowanie i zwrot” w sekcji 5. | Dwa widoki opierają się na zapisanych operacjach, nie na statusie realizacji |
| D-04 | Rozstrzygnięte 01.11.2026, patrz „Wydatek jednorazowy i miesięczny” w sekcji 5. | Wystąpienie jest kosztem, a wypływem dopiero po zapłacie |
| D-05 | Rozstrzygnięte 01.11.2026, patrz sekcja 3. Do potwierdzenia: dostawca wysyłki e-mail dla resetu hasła. | Kolejne role i wspólne logowanie są poza MVP |
| D-06 | Rozstrzygnięte 01.11.2026, patrz sekcja 4: bez migracji historii i bez salda w MVP. | Migracja po MVP wymaga osobnej decyzji |
| D-07 | Czy koszty zakupów, cięcia i transportu mają być wiązane z konkretnymi zamówieniami? | Wpływa na rentowność zamówienia i zapobieganie podwójnemu ujęciu |
| D-08 | Jaki plan SMART ASP.NET i jakie parametry backupu, dostępności oraz wydajności? | Blokuje ostateczną konfigurację środowisk |

## 13. Kolejność dalszych prac

D-01–D-06 są rozstrzygnięte. Najpierw uzgodnić D-07 i sprawdzić hosting (D-08). Następnie opisać model domenowy, własność pól i kontrakty API, przygotować środowiska i dostęp, wdrożyć zamówienia, wydatki oraz dwa widoki raportowania. Uruchomienie produkcyjne wymaga odbioru scenariuszy z sekcji 11.
