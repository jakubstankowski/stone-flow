# Stone Flow — PRD

Wersja: 0.3 · Data: 01.11.2026 · Status: projekt wymagań do uzgodnienia.

Dokument opisuje system ERP dla sklepu z kamieniem naturalnym. Ustalony zakres i ograniczenia odróżniono od propozycji oraz decyzji otwartych. Dokumentem towarzyszącym jest [design guide](DESIGN-GUID-stone-flow.md).

## 1. Źródła i sposób interpretacji

| Źródło | Wykorzystanie |
| --- | --- |
| [Wymagania biznesowe](../documents/wymagania%20biznesowe.docx) | Cele, użytkownicy, ZAM-01–07, WYD-01–07, FIN-01–06, zakres MVP i pytania biznesowe |
| [Wymagania technologiczne](../documents/wymagania%20technologiczne.docx) | Stos technologiczny, architektura, bezpieczeństwo, hosting, paleta i odbiór |
| [Ewidencja sprzedaży](../documents/ewidencja-sprzedazy-2026-DEMO.xlsx) | Arkusze CZERWIEC, LIPIEC, SIERPIEŃ, WRZESIEŃ oraz PODSUMOWANIE ROKU; pola zamówienia, kanały, dostawcy i rozliczenia |
| [Zestawienie finansowe](../documents/zestawienie-finansowe-2026-DEMO.xlsx) | Arkusze czerwiec–wrzesień; operacje finansowe, kategorie, dokumenty i wypłaty |
| [Dashboard referencyjny](../documents/example-dashboard.webp) | Hierarchia informacji i kierunek wizualny, bez rozszerzania zakresu ERP o moduły widoczne na obrazie |

Dokumenty wymagań określają zakres. Arkusze pokazują praktykę pracy i problemy danych; ich formuły nie są automatycznie prawidłową specyfikacją finansową. Dane DEMO nie są wzorcem produkcyjnych danych osobowych; ich fikcyjność potwierdził użytkownik 01.11.2026. W dokumentacji należy stosować określenie „sklep z kamieniem naturalnym”, bez nazwy handlowej i domeny.

**Decyzja użytkownika z 28.10.2026:** system ma udostępniać oba widoki: sprzedaż oraz przepływy pieniężne. Nie wolno utożsamiać wartości zamówienia z otrzymaną zapłatą.

**Decyzja użytkownika z 01.11.2026:** wpłaty za zamówienia są osobnymi wpisami dodawanymi ręcznie, także dla zamówień z WooCommerce. Stan płatności zamówienia jest wyliczany z wpłat. Zapłatę wydatku wyznacza data zapłaty. Wypłaty właścicieli są osobnym rodzajem operacji.

## 2. Problem i cel produktu

Obecna praca wymaga prowadzenia dwóch arkuszy, przepisywania zamówień i ręcznego zestawiania sprzedaży, wydatków i wypłat. Rozproszone dane utrudniają ustalenie wyniku oraz wykrycie nieopłaconych zamówień i brakujących kosztów.

Stone Flow ma zapewnić jedno miejsce do obsługi zamówień internetowych i ręcznych, ewidencji wydatków jednorazowych i miesięcznych oraz analizy sprzedaży i przepływów pieniężnych. Wyniki muszą być możliwe do prześledzenia do danych źródłowych.

## 3. Użytkownicy i dostęp

- Właściciel: jedyna rola w systemie, z jednym kontem i pełnym dostępem do zamówień, finansów i integracji.
- Integracja i zadania automatyczne: działają technicznie, bez przyznawania dostępu interaktywnego.

**Decyzja użytkownika z 01.11.2026 (D-05):** MVP ma jedną rolę i jedno konto. Jest to świadome zawężenie wymagań biznesowych, które przewidują także upoważnione osoby. W systemie nie ma zarządzania kontami, a audyt wskazuje to konto jako autora zmian użytkownika. Zmiany wykonane automatycznie mają w audycie autora systemowego: „Synchronizacja WooCommerce” albo „Zadanie automatyczne” (decyzja użytkownika z 01.11.2026).

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
7. Audyt zmian finansowych z listą „Historia zmian”.

### Poza potwierdzonym zakresem

Magazyn, fakturowanie, księgowość, deklaracje podatkowe, integracja KSeF, zakupy, CRM, portal klienta, prognozowanie sprzedaży i obsługa wielu firm. Numery faktur, dostawcy, zdjęcia z realizacji i opinie występują w danych, ale nie uzasadniają budowy osobnych modułów. Flagi zdjęć/opinii można zachować; przechowywanie plików i publikowanie opinii wymaga odrębnej decyzji.

Migracja historii z Excela nie wchodzi do MVP ani do warunków odbioru (decyzja użytkownika z 01.11.2026, D-06). Raporty obejmują dane od daty uruchomienia, a arkusze pozostają archiwum. Saldo rachunku i saldo początkowe również są poza MVP. Zamykanie okresów również jest poza MVP (decyzja użytkownika z 01.11.2026): raporty za wcześniejsze miesiące mogą się zmieniać, a przyczynę zmiany pokazuje lista „Historia zmian” (patrz „Historia zmian” w sekcji 5). Poza MVP są też wpływy niezwiązane z zamówieniem, np. zwrot z urzędu, wpłata właściciela lub inny przychód (decyzja użytkownika z 01.11.2026), więc przepływy pieniężne nie muszą zgadzać się z ruchem na rachunku. Zwroty od dostawców obsługują ujemne wydatki (patrz „Wydatek jednorazowy i miesięczny”).

Przełom uruchomienia (decyzja użytkownika z 01.11.2026): datę uruchomienia podaje właściciel przed startem produkcyjnym. Jest jednym dniem zapisanym w konfiguracji wdrożenia i nie zmienia się jej z poziomu aplikacji. Zamówienia otwarte w dniu startu użytkownik wpisuje ręcznie, z prawdziwą datą zamówienia i z wcześniejszymi wpłatami z ich prawdziwymi datami, dzięki czemu stan płatności jest poprawny. Kwoty z datą sprzed uruchomienia nie wchodzą do raportów, a wpłaty otrzymane od daty uruchomienia liczą się do wpływów. Tak samo traktuje się wydatki nieopłacone w dniu startu: użytkownik wpisuje je z prawdziwą datą kosztu, koszt nie wchodzi do raportów, a zapłata od daty uruchomienia liczy się do wypływów. Zwrot od daty uruchomienia do zamówienia z datą sprzed uruchomienia jest tylko wypływem i nie obniża sprzedaży, bo tej sprzedaży nie było w raportach.

## 5. Najważniejsze procesy

### Zamówienie ręczne

Użytkownik otwiera „Dodaj zamówienie”, wpisuje klienta, datę, kanał i wartość, uzupełnia dostępne dane realizacji oraz zapisuje. System waliduje dane i chroni przed ponownym wysłaniem. Zamówienie pojawia się na wspólnej liście. Zapis zamówienia sam w sobie nie oznacza otrzymania pieniędzy.

### Zamówienie WooCommerce

Zamówienia docierają na bieżąco przez webhooki, których autentyczność backend weryfikuje. Raz dziennie o 23:00 Europe/Warsaw backend odpytuje WooCommerce REST API o zmiany od ostatniego sukcesu i uzupełnia luki. Pierwsze pobranie obejmuje zamówienia od daty uruchomienia; historii sprzed tej daty się nie pobiera (D-06). Integracja pomija zamówienia ze sklepu z datą sprzed uruchomienia, także zmienione po starcie, i nie zapisuje ich w historii błędów. Zmiany w sklepie nie docierają więc do takich zamówień wpisanych ręcznie; użytkownik nanosi je sam. Pominięty webhook może oznaczać opóźnienie do następnego uzupełniania, dlatego stan integracji jest widoczny przy wynikach.

Backend identyfikuje zamówienie według źródła i zewnętrznego ID oraz tworzy lub aktualizuje istniejący rekord. Ponowne dostarczenie nie tworzy drugiego zamówienia. Dane starsze od zapisanych, według daty modyfikacji w sklepie, są pomijane i nie wywołują przejścia na „Anulowane”. Do ERP trafiają wszystkie statusy sklepu. Zamówienia pending, failed i szkice są oznaczone statusem ze sklepu, nie liczą się do sprzedaży i domyślnie są ukryte na liście, ale dostępne filtrem; do sprzedaży wchodzą po zmianie statusu w sklepie.

Własność pól: WooCommerce odpowiada za ID, datę, dane klienta i wartość; te pola są w ERP tylko do odczytu, a synchronizacja je aktualizuje. ERP odpowiada za status realizacji, dostawców, ZO, fakturę, flagi, uwagi i wpłaty; synchronizacja ich nie zmienia. Jedynym wyjątkiem jest opisane niżej przejście na status „Anulowane”. Kanał zamówienia ze sklepu to „sklep”.

Zmiany w sklepie po imporcie (decyzja użytkownika z 01.11.2026): anulowanie w sklepie oznacza zamówienie w ERP jako anulowane, więc wypada ono ze sprzedaży. Jest to jednorazowe przejście: działa tylko wtedy, gdy status ze sklepu zmienia się na anulowany względem zapisanego w ERP. Ponowne dostarczenie tego samego statusu nie zmienia statusu realizacji. Usunięcie zamówienia w sklepie lub przeniesienie go do kosza działa tak samo, a ERP pokazuje, że usunięto je w sklepie. Znane ograniczenie (decyzja właściciela z 06.11.2026): REST API nie zwraca zamówień trwale usuniętych, więc jeśli zginie webhook takiego usunięcia, dzienne uzupełnianie go nie wykryje i zamówienie zostanie w sprzedaży; przeniesienie do kosza uzupełnianie wykrywa. Jeżeli zamówienie ma wpłaty, użytkownik widzi ostrzeżenie, a wpłaty pozostają. Przywrócenie w sklepie zamówienia anulowanego w ERP aktualizuje status ze sklepu i pokazuje ostrzeżenie „Zamówienie aktywne w sklepie”, ale nie zmienia statusu realizacji; o przywróceniu w ERP decyduje użytkownik. To samo ostrzeżenie pojawia się, gdy użytkownik anuluje w ERP zamówienie aktywne w sklepie; ERP nie zmienia niczego w sklepie, a pola należące do sklepu nadal się aktualizują. Nowe zamówienie ze sklepu otrzymuje przy imporcie status realizacji „W trakcie”, a zaimportowane jako już anulowane status „Anulowane”. Zwrot w sklepie jest informacją i nie tworzy automatycznie wypływu.

Sklep sprzedaje wyłącznie w PLN (decyzja użytkownika z 01.11.2026), więc integracja nie obsługuje innych walut.

Sukces synchronizacji (decyzja użytkownika z 01.11.2026): sukcesem jest przebieg, w którym sklep odpowiedział i pobrano wszystkie strony zmian. Kolejne uzupełnianie startuje od ostatniego sukcesu, więc przerwy w działaniu domykają się same. Zamówienie, którego nie udało się zapisać, nie zatrzymuje przebiegu: dostaje własny błąd ze statusem, czasem i kontekstem w historii błędów. Błąd pozostaje otwarty do udanego ponowienia.

Ponowienie ręczne: „Ponów” przy błędzie pobiera jedno zamówienie, a „Synchronizuj teraz” uruchamia to samo uzupełnianie co o 23:00, od ostatniego sukcesu. Obie akcje nie tworzą duplikatów.

Stany synchronizacji, sprawdzane w tej kolejności: „Synchronizacja trwa”, gdy przebieg jest w toku; „Błąd”, gdy ostatni przebieg się nie udał; „Dane niekompletne”, gdy są otwarte błędy zamówień albo od ostatniego sukcesu minęło ponad 25 godzin; „Aktualne” w pozostałych przypadkach. Pominiętego webhooka nie da się wykryć przed uzupełnianiem, więc przy wynikach zawsze widać czas ostatniego sukcesu jako „Ostatnia pełna kontrola”.

### Wydatek jednorazowy i miesięczny

Użytkownik dodaje opis, kategorię, datę i kwoty. Kwota brutto wydatku nie może wynosić 0 zł. Ujemna kwota oznacza korektę lub zwrot od dostawcy (decyzja użytkownika z 01.11.2026): obniża koszty w okresie daty kosztu, a po uzupełnieniu daty zapłaty obniża wypływy w okresie tej daty. Kwota netto, jeśli jest podana, ma ten sam znak co brutto. Przy wydatku miesięcznym definiuje również harmonogram i okres aktywności; kwota definicji musi być większa od 0 zł. Trwały mechanizm tworzy najwyżej jedno wystąpienie na definicję i okres, także po restarcie i ponowieniu. Planowany wydatek nie może automatycznie udawać potwierdzonego wypływu środków.

Wystąpienie jest zwykłym wydatkiem bez daty zapłaty, z datą kosztu równą terminowi. Liczy się do kosztów, a do wypływów dopiero po oznaczeniu jako opłacone. W krótszym miesiącu przypada na jego ostatni dzień. Po przerwie w działaniu system tworzy wszystkie zaległe wystąpienia. Reguły definicji (decyzja użytkownika z 01.11.2026): wystąpienie powstaje w dniu terminu, więc wcześniej nie widać go na listach i w raportach ani nie można go opłacić. Okres się liczy, gdy termin wypada między początkiem a końcem definicji, włącznie z tymi dniami. Zapis nowej definicji od razu tworzy wystąpienia za wszystkie terminy od jej początku do dzisiaj, włącznie z dzisiejszym, a formularz pokazuje wcześniej ich liczbę (decyzja użytkownika z 01.11.2026). Wystąpienia z terminem sprzed daty uruchomienia powstają, ale nie wchodzą do raportów. Zmiana początku istniejącej definicji nie tworzy minionych wystąpień: terminy, które weszły w zakres przez przesunięcie początku na wcześniej i przypadają przed dniem zmiany, nie tworzą wystąpień, a termin w dniu zmiany się liczy (reguła wynikająca). Nowy dzień miesiąca obowiązuje od bieżącego okresu, a okresy wcześniejsze zachowują dotychczasowy dzień i według niego liczą się albo nie; dnia nie można zmienić, gdy definicja jest wstrzymana (reguła wynikająca). Okres, który ma już wystąpienie, nie dostaje drugiego, więc nowy dzień zadziała w nim dopiero od następnego okresu; jeśli bieżący okres nie ma jeszcze wystąpienia, a nowy termin już minął, wystąpienie bieżącego okresu powstaje od razu (decyzja użytkownika z 08.11.2026). Definicja ma pola wydatku poza datami: opis, kategorię, kontrahenta, kwotę brutto, opcjonalną netto, metodę, płatnika i komentarz, a także dzień miesiąca, początek, koniec i aktywność. Wstrzymanie zatrzymuje nowe wystąpienia. Wstrzymana definicja nie tworzy wystąpień. Po wznowieniu wystąpienie bieżącego okresu powstaje w dniu terminu, jeśli termin nie minął w czasie wstrzymania, a kolejne okresy powstają jak zwykle; okresy z czasu wstrzymania pozostają puste; jeśli w chwili wznowienia termin bieżącego okresu już minął w czasie wstrzymania, wznowienie nie tworzy wystąpienia za ten okres, a kolejne powstają od następnego okresu (decyzja użytkownika z 08.11.2026). Późniejsza zmiana dnia ocenia bieżący okres według nowego terminu (reguła wynikająca). Poza zapisem nowej definicji zaległe wystąpienia powstają po przerwie w działaniu systemu oraz przy przedłużeniu zakończonej definicji: za okresy między dawnym końcem a dzisiaj, bez okresów z czasu wstrzymania, a formularz pokazuje wcześniej ich liczbę (decyzja użytkownika z 08.11.2026). Każda zmiana definicji, także kwoty, dotyczy tylko wystąpień jeszcze nieutworzonych, w tym zaległych; definicja nie ma historii wersji, a utworzone wystąpienia można poprawić pojedynczo. Usunięte wystąpienie nie wraca przy uzupełnianiu, bo ślad usunięcia nadal blokuje parę definicja–okres; w razie pomyłki wydatek dodaje się ręcznie.

### Rejestracja płatności

Wpłata za zamówienie: użytkownik otwiera szczegóły zamówienia i dodaje wpłatę z kwotą, datą operacji i metodą. Dotyczy to zamówień ręcznych i z WooCommerce; integracja nie tworzy wpłat, a status opłacenia w WooCommerce nie potwierdza otrzymania pieniędzy. Zamówienie może mieć wiele wpłat, także zaliczki. Stan płatności zamówienia jest wyliczany z wpłat i nie jest ustawiany ręcznie: Nieopłacone, Częściowo opłacone albo Opłacone. Pobranie jest tylko metodą: wpłatę dodaje się dopiero po otrzymaniu środków, z datą ich otrzymania.

Wyliczanie stanu płatności (decyzja użytkownika z 01.11.2026): kwota „Zapłacono” to suma wpłat minus suma zwrotów. Stan to Nieopłacone przy Zapłacono równym 0 zł, Częściowo opłacone przy Zapłacono poniżej wartości zamówienia i Opłacone przy Zapłacono równym wartości lub wyższym. Zwrot nie zmienia wartości zamówienia, więc po częściowym zwrocie opłacone zamówienie staje się Częściowo opłacone. Zamówienie o wartości 0 zł ma stan Opłacone. Gdy Zapłacono przekracza wartość zamówienia, stan pozostaje Opłacone, a zamówienie pokazuje ostrzeżenie „Nadpłata X zł”. Zamówienie anulowane nie ma stanu płatności; gdy jego Zapłacono jest większe od 0 zł, pokazuje „Do zwrotu X zł”.

Wpłata i zwrot muszą być większe od 0 zł, a zwrot nie może przekroczyć kwoty Zapłacono. Wpłatę można dodać do zamówienia anulowanego; użytkownik widzi wtedy ostrzeżenie.

Zapłata wydatku: wydatek ma datę kosztu, opcjonalny termin i datę zapłaty. Użytkownik wpisuje datę zapłaty w formularzu albo później, akcją „Oznacz jako opłacony”. Uzupełniona data zapłaty oznacza stan „Opłacony”; tylko taki wydatek trafia do wypływów, do okresu wyznaczonego tą datą.

Wypłata właściciela: użytkownik dodaje wpis z osobą, kwotą, datą i metodą. Wypłata liczy się do wypływów w przepływach pieniężnych, ale nie do kosztów ani sprzedaży. Błędną wypłatę można poprawić lub usunąć; usunięcie wymaga potwierdzenia, a obie operacje zostawiają ślad audytu.

Lista „Operacje” (decyzja użytkownika z 01.11.2026): osobna pozycja nawigacji pokazuje wpłaty, zwroty, opłacone wydatki i wypłaty właścicieli z filtrem okresu i rodzaju operacji. Na tej liście użytkownik dodaje wypłaty właścicieli, a karty przepływów pieniężnych prowadzą do niej.

### Anulowanie i zwrot

Zamówień nie usuwa się. Błędne albo wycofane zamówienie, ręczne lub ze sklepu, otrzymuje status „Anulowane”, które wyłącza je ze sprzedaży i pozostawia audyt. Jeżeli zamówienie ma wpłaty, użytkownik widzi ostrzeżenie, a pieniądze rozlicza zwrotem.

Anulowanie można cofnąć akcją „Przywróć zamówienie”, z potwierdzeniem i audytem, dla zamówień ręcznych i ze sklepu (decyzja użytkownika z 01.11.2026). Zamówienie otrzymuje status „W trakcie”, wraca do sprzedaży w okresie daty zamówienia, a jego zwroty sprzedaży i korekty sprzedaży znowu obniżają sprzedaż w okresach swoich dat.

Zwrot dodaje ręcznie na zamówieniu użytkownik, podając rodzaj, kwotę, datę, metodę i powód. Rodzaj jest wymagany i nie ma wartości domyślnej (decyzja użytkownika z 01.11.2026): „Zwrot sprzedaży” dotyczy wycofanej sprzedaży, np. oddanego towaru, a „Zwrot pieniędzy” zwraca środki bez zmiany sprzedaży, np. nadpłatę lub zaliczkę. Każdy zwrot liczy się do wypływów w okresie swojej daty i obniża kwotę Zapłacono. Zwrot sprzedaży do zamówienia wchodzącego do sprzedaży obniża też sprzedaż w okresie swojej daty, więc wcześniejsze miesiące się nie zmieniają. Zwrot pieniędzy nigdy nie obniża sprzedaży. Zamówienia pending, failed i szkice nie wchodzą do sprzedaży, więc żaden ich zwrot jej nie obniża, dopóki status ze sklepu tego nie zmieni. Wyjątkiem są też zamówienia sprzed uruchomienia (patrz „Przełom uruchomienia” w sekcji 4).

Zwrot do zamówienia anulowanego jest tylko wypływem i nie obniża sprzedaży, także gdy dodano go przed anulowaniem (decyzja użytkownika z 01.11.2026). Anulowane zamówienie wypada ze sprzedaży w okresie daty zamówienia, więc anulowanie może zmienić raporty za wcześniejsze miesiące: zarówno okres zamówienia, jak i okresy jego wcześniejszych zwrotów sprzedaży i korekt sprzedaży.

Korekta sprzedaży (decyzja użytkownika z 01.11.2026): gdy klient oddaje towar, a pieniądze nie wracają, np. przy nieopłaconym zamówieniu lub zaliczce niższej od wartości oddanego towaru, użytkownik dodaje na zamówieniu korektę sprzedaży z kwotą większą od 0 zł, datą i powodem. Działa to dla zamówień ręcznych i ze sklepu. Korekta obniża sprzedaż w okresie swojej daty, tak samo jak zwrot sprzedaży, i podlega tym samym wyjątkom: nie obniża sprzedaży zamówień anulowanych, pending, failed, szkiców ani zamówień sprzed uruchomienia. Korekta nie jest operacją pieniężną: nie tworzy wypływu, nie zmienia kwoty Zapłacono, wartości zamówienia ani stanu płatności i nie pojawia się na liście „Operacje”.

Limit obniżeń sprzedaży (decyzja użytkownika z 01.11.2026): suma zwrotów sprzedaży i korekt sprzedaży zamówienia nie może przekroczyć jego wartości. System odrzuca wpis, poprawkę lub ręczną korektę wartości, która naruszyłaby ten limit. Wartości ze sklepu nie można odrzucić, więc jeżeli synchronizacja obniży ją poniżej tej sumy, zamówienie pokazuje ostrzeżenie „Obniżenia sprzedaży przekraczają wartość o X zł”, a użytkownik poprawia wpisy.

Korekta wartości: wartość zamówienia ręcznego poprawia się bezpośrednio, z audytem; zamówienie ze sklepu zmienia się przez synchronizację. W obu przypadkach sprzedaż zmienia się w okresie daty zamówienia, więc raport za ten okres może się zmienić.

Błędny wpis: użytkownik poprawia lub usuwa wpłatę, zwrot albo korektę sprzedaży. Usunięcie wymaga potwierdzenia i zostawia ślad audytu. System odrzuca poprawkę lub usunięcie, po którym kwota Zapłacono spadłaby poniżej 0 zł; użytkownik musi wtedy najpierw poprawić lub usunąć zwrot.

### Historia zmian

Lista „Historia zmian” (decyzja użytkownika z 01.11.2026) jest osobną pozycją nawigacji i służy tylko do odczytu. Pokazuje wpisy audytu: czas, autora, obiekt z jego datą biznesową, operację i zakres zmiany, z filtrem okresu zmiany i rodzaju obiektu. Wpis prowadzi do obiektu, a gdy obiekt usunięto, sam pokazuje ślad usunięcia. Audyt obejmuje każde utworzenie, zmianę i usunięcie danych finansowych: zamówień, wpłat, zwrotów, korekt sprzedaży, wydatków, definicji cyklicznych i wypłat właścicieli.

### Anonimizacja danych klienta

Zamówień nie usuwa się, więc żądanie usunięcia danych osobowych obsługuje akcja „Anonimizuj dane klienta” w szczegółach zamówienia (decyzja użytkownika z 01.11.2026). Akcja jest ręczna, nieodwracalna i wymaga potwierdzenia. Usuwa dane klienta z zamówienia i z wpisów audytu, a zachowuje kwoty, daty, wpłaty, zwroty i ślad wykonania operacji. Jest dostępna dopiero po upływie okresu retencji (D-09). Synchronizacja nie przywraca danych klienta zanonimizowanego zamówienia.

### Analiza finansów

Użytkownik wybiera „Sprzedaż” albo „Przepływy pieniężne” i okres. Karty, wykresy i zestawienie korzystają z tych samych reguł i filtrów. Widok podaje podstawę kwot, datę przypisania do okresu i aktualność danych. Niekompletna integracja jest widoczna obok wyników.

Zawartość widoków (decyzja użytkownika z 01.11.2026): widok „Sprzedaż” ma pod wykresami zestawienie składników sprzedaży w okresie. Każde zamówienie wchodzące do sprzedaży, zwrot sprzedaży i korekta sprzedaży to jeden wiersz z datą, rodzajem, kwotą ze znakiem i przejściem do zamówienia, a suma zestawienia równa się karcie „Sprzedaż”. Widok „Przepływy pieniężne” ma karty wpływów, wypływów i zmiany środków, wykres wpływów i wypływów w czasie, podział wypływów według rodzaju operacji (opłacone wydatki, wypłaty właścicieli, zwroty) oraz ostatnie operacje z przejściem do pełnej listy „Operacje”.

Podział sprzedaży na kanały (decyzja użytkownika z 01.11.2026): zwrot sprzedaży i korekta sprzedaży pomniejszają kanał swojego zamówienia w okresie swojej daty, więc suma kanałów równa się sprzedaży.

## 6. Wymagania funkcjonalne i odbiór

| ID źródłowe / projektowe | Wymaganie | Kryterium akceptacji |
| --- | --- | --- |
| ZAM-01, 03, 05 | Ręczne zamówienia i wspólna lista | Zamówienie ręczne oraz internetowe są widoczne razem; źródło techniczne i kanał sprzedaży są rozróżnione |
| ZAM-02, 06 | Synchronizacja WooCommerce | Wielokrotne dostarczenie tego samego ID, webhookiem lub przez uzupełnianie, daje jeden rekord; pominięty webhook jest uzupełniany przez dzienne odpytanie REST API (poza trwałym usunięciem zamówienia — znane ograniczenie, decyzja właściciela z 06.11.2026); synchronizacja nie zmienia pól ERP, poza jednorazowym przejściem na „Anulowane” przy zmianie statusu ze sklepu na anulowany albo usunięciu zamówienia w sklepie; ponowne dostarczenie tego samego statusu nie zmienia statusu realizacji; dostawa starsza od zapisanych danych niczego nie zmienia; błąd można rozpoznać i ponowić; zamówienie ze sklepu z datą sprzed uruchomienia jest pomijane i nie tworzy rekordu ani błędu |
| ZAM-04 | Szczegóły i edycja | Użytkownik poprawia dozwolone pola; równoczesna zmiana nie nadpisuje bez ostrzeżenia nowszych danych |
| ZAM-07 | Dane zamówień w sprzedaży | Każda uwzględniona wartość pochodzi z identyfikowalnego zamówienia i zatwierdzonej reguły okresu |
| WYD-01, 02 | Dodawanie, poprawianie i usuwanie błędnych wydatków | Zmiana aktualizuje raport i zostawia audyt; usunięcie wymaga potwierdzenia, pozostawia audyt i nie usuwa historii bez śladu; kwota 0 zł jest odrzucana; wydatek z ujemną kwotą obniża koszty według daty kosztu, a po uzupełnieniu daty zapłaty obniża wypływy według tej daty |
| WYD-03–06 | Wydatki miesięczne | Aktywny harmonogram tworzy jedno wystąpienie za okres, także zaległe po przerwie; dzień 29–31 przypada w krótszym miesiącu na ostatni dzień; wystąpienie powstaje w dniu terminu; okres liczy się tylko przy terminie między początkiem a końcem definicji; zapis nowej definicji tworzy wystąpienia za wszystkie terminy od jej początku do dzisiaj, a wystąpienia sprzed uruchomienia nie wchodzą do raportów; wstrzymanie blokuje kolejne, a wznowienie nie tworzy okresów z czasu wstrzymania; ponowienie nie duplikuje; zmiana definicji nie zmienia utworzonych wystąpień; usunięte wystąpienie nie wraca |
| WYD-07 | Wydatki w raportach | Wydatek jest liczony raz zgodnie z datą, kategorią i stanem rozliczenia |
| FIN-01–04 | Pulpit i analiza w czasie | Widok sprzedaży pokazuje sprzedaż, koszty i wynik przed wypłatami za okres oraz ich zmiany w czasie; widok przepływów pokazuje wpływy, wypływy i zmianę środków, wykres w czasie, podział wypływów według rodzaju operacji i ostatnie operacje z przejściem do listy „Operacje”; zmiana okresu aktualizuje karty, wykresy i tabelę; porównanie używa poprzedniego okresu według reguły z sekcji 8, a przy bazie równej zero lub sprzed uruchomienia pokazuje „brak podstawy porównania”; liczba zamówień obejmuje te same zamówienia co sprzedaż; wartości dostępne są również tekstowo |
| FIN-05, 06 | Spójne i wyjaśnione obliczenia | Raport obejmuje właściwe źródła i opisuje reguły; sumę wpływów i wypływów za okres można uzgodnić z listą „Operacje” przy tym samym okresie; suma zestawienia składników sprzedaży równa się karcie „Sprzedaż” przy tym samym okresie, a korekta do zamówienia z wcześniejszego miesiąca jest w nim widoczna w okresie swojej daty; suma kanałów równa się sprzedaży |
| FIN-07, decyzja użytkownika | Dwa widoki finansowe | Nieopłacone zamówienie nie zwiększa rzeczywistych wpływów; wpłata nie tworzy drugiej sprzedaży |
| PLAT-01 | Wpłaty za zamówienia | Dodana wpłata pojawia się na zamówieniu, zmienia wyliczony stan płatności i zwiększa wpływy w okresie daty operacji, a sprzedaży nie zmienia; synchronizacja WooCommerce nie tworzy wpłaty |
| PLAT-02 | Zapłata wydatku | Wydatek bez daty zapłaty nie zwiększa wypływów; po jej uzupełnieniu jest opłacony i liczony w okresie tej daty |
| PLAT-03 | Wypłaty właścicieli | Wpis dodany na liście „Operacje” zwiększa wypływy, nie zmienia kosztów ani sprzedaży; poprawa lub usunięcie aktualizuje raporty i zostawia audyt, a usunięcie wymaga potwierdzenia |
| PLAT-04 | Płatności częściowe i pobranie | Kilka wpłat sumuje się na zamówieniu, a stan zmienia się z Nieopłacone przez Częściowo opłacone na Opłacone; zwrot obniża kwotę Zapłacono i przelicza stan; nadpłata pozostawia stan Opłacone i pokazuje ostrzeżenie; zamówienie anulowane nie ma stanu i pokazuje kwotę do zwrotu; zamówienie o wartości 0 zł jest Opłacone; wpłata i zwrot muszą być większe od 0 zł, a zwrot nie może przekroczyć kwoty Zapłacono; pobranie bez dodanej wpłaty pozostaje nieopłacone |
| ZAM-08 | Anulowanie zamówienia | Zamówienia nie można usunąć; anulowane wypada ze sprzedaży, zostawia audyt i ostrzega o istniejących wpłatach; „Przywróć zamówienie” po potwierdzeniu ustawia „W trakcie”, przywraca je do sprzedaży i zostawia audyt; przywrócenie w sklepie oraz anulowanie w ERP zamówienia aktywnego w sklepie pokazują ostrzeżenie, ale nie zmieniają statusu realizacji ani niczego w sklepie; nowe zamówienie ze sklepu ma status „W trakcie”, a zaimportowane jako anulowane status „Anulowane” |
| PLAT-05 | Zwroty | Zwrot wymaga wyboru rodzaju i zwiększa wypływy w okresie swojej daty, bez utraty historii zamówienia; zwrot sprzedaży do zamówienia wchodzącego do sprzedaży obniża ją w tym okresie, bez zmiany wcześniejszych okresów; zwrot pieniędzy nie obniża sprzedaży, więc zwrot nadpłaty 200 zł przy zamówieniu 1000 zł pozostawia sprzedaż 1000 zł; zwrot do zamówienia pending, failed lub szkicu nie obniża sprzedaży; po anulowaniu zamówienia jego zwroty, także wcześniejsze, nie obniżają sprzedaży, więc pełny zwrot anulowanego zamówienia daje w sumie 0 zł sprzedaży; zwrot sprzedaży 1200 zł przy zamówieniu 1000 zł i wpłatach 1200 zł jest odrzucany |
| PLAT-06 | Korekty i błędne wpisy | Korekta wartości zmienia sprzedaż w okresie daty zamówienia i zostawia audyt; poprawa lub usunięcie wpłaty, zwrotu albo korekty sprzedaży aktualizuje stan płatności i raporty oraz zostawia audyt; zmiana, po której kwota Zapłacono byłaby ujemna, jest odrzucana |
| PLAT-07 | Korekta sprzedaży i limit obniżeń | Korekta sprzedaży 500 zł do zamówienia ze sklepu 1000 zł z zaliczką 300 zł obniża sprzedaż o 500 zł w okresie swojej daty, nie zwiększa wypływów, nie zmienia kwoty Zapłacono ani stanu płatności i nie pojawia się na liście „Operacje”; korekta nie obniża sprzedaży zamówienia anulowanego, pending, failed, szkicu ani zamówienia sprzed uruchomienia; zmiana przekraczająca limit sumy zwrotów sprzedaży i korekt sprzedaży jest odrzucana; obniżenie wartości przez synchronizację poniżej tej sumy pokazuje ostrzeżenie |
| URU-01 | Przełom uruchomienia | Zamówienie otwarte można wpisać ręcznie z datą i wpłatami sprzed uruchomienia, a stan płatności uwzględnia te wpłaty; kwoty z datą sprzed uruchomienia nie wchodzą do raportów; wpłata otrzymana po uruchomieniu zwiększa wpływy; zapłata po uruchomieniu wydatku z wcześniejszą datą kosztu zwiększa wypływy, a koszt nie wchodzi do raportów; zwrot po uruchomieniu do zamówienia sprzed uruchomienia zwiększa wypływy i nie obniża sprzedaży |
| KWO-01 | Reguły kwot | Kwoty są zapisywane do grosza w PLN; suma raportu równa się sumie zapisanych kwot |
| DOST-01 | Kontrola dostępu | Bez ważnej sesji API odmawia każdego odczytu i zapisu; sesja wygasa po 60 minutach bezczynności; reset hasła działa przez jednorazowy link e-mail; link z panelu sklepu nie uwierzytelnia |
| DANE-01 | Anonimizacja danych klienta | Po potwierdzeniu dane klienta znikają z zamówienia i audytu, a raporty się nie zmieniają; przed upływem okresu retencji akcja jest niedostępna; synchronizacja nie przywraca danych; operacja zostawia ślad audytu bez danych osobowych |
| INT-01 | Stan integracji | UI pokazuje ostatnią pełną kontrolę, błąd i dostępną akcję; nie sugeruje kompletności przy awarii; stan wynika z warunków z sekcji 5, w tym „Dane niekompletne” przy otwartych błędach zamówień lub po ponad 25 godzinach od ostatniego sukcesu; błąd pojedynczego zamówienia nie zatrzymuje przebiegu, a „Ponów” zamyka go po udanym zapisie; „Synchronizuj teraz” pobiera zmiany od ostatniego sukcesu |
| AUD-01 | Audyt i „Historia zmian” | Każde utworzenie, zmiana i usunięcie danych finansowych zapisuje autora (konto albo autora systemowego), czas UTC, obiekt, operację i zakres zmiany; lista „Historia zmian” pokazuje te wpisy tylko do odczytu, filtruje je według okresu zmiany i rodzaju obiektu i prowadzi do obiektu; usunięty obiekt zostawia widoczny ślad; po zmianie raportu za miniony okres lista pozwala wskazać zmianę, która go wywołała |

## 7. Model informacji wynikający z arkuszy

Poniższy model jest propozycją logiczną; nie narzuca tabel ani kontraktów API.

| Obiekt | Dane i reguły |
| --- | --- |
| Zamówienie | ID ERP, źródło techniczne, ID WooCommerce, kanał, data zamówienia, klient, wartość brutto (dla sklepu suma zamówienia z dostawą), status realizacji, status ze sklepu, kwota Zapłacono i stan płatności wyliczane z wpłat i zwrotów, komentarz |
| Klient przy zamówieniu | Osoba lub firma, dane kontaktowe, adres, opcjonalny NIP; rozdzielone pola zamiast jednego bloku tekstu |
| Dane operacyjne | Dostawca lub dostawcy, referencje ZO, numer faktury, flagi zdjęć i opinii, dodatkowe uwagi |
| Wpłata | Powiązanie z zamówieniem, kwota, data rzeczywistej operacji, metoda, opcjonalne pole „Przyjął” przy gotówce; zamówienie może mieć wiele wpłat |
| Wydatek | Opis, kategoria, kontrahent, data kosztu, opcjonalny termin, data zapłaty wyznaczająca stan „Opłacony”, kwota brutto (wymagana, różna od 0 zł, ujemna przy korekcie lub zwrocie od dostawcy) i netto (opcjonalna; formularz podpowiada brutto / 1,23) przepisane z dokumentu, referencja dokumentu, metoda, płatnik, komentarz |
| Wypłata właściciela | Osoba, kwota, data, metoda; odrębna od wydatku |
| Zwrot | Powiązanie z zamówieniem, rodzaj (zwrot sprzedaży albo zwrot pieniędzy), kwota, data, metoda, powód; odrębny od wpłaty |
| Korekta sprzedaży | Powiązanie z zamówieniem, kwota, data, powód; bez metody, nie jest operacją pieniężną |
| Definicja cykliczna | Opis, kategoria, kontrahent, kwota brutto, opcjonalna netto, metoda, płatnik, komentarz, dzień miesiąca, początek i koniec, aktywność; bez historii wersji |
| Wystąpienie cykliczne | ID definicji, okres, termin, kwota, stan; unikalna para definicja–okres |
| Synchronizacja | Źródło, zewnętrzne ID, stan, czas próby i sukcesu, bezpieczny opis błędu, identyfikator korelacji |
| Audyt | Autor (konto albo autor systemowy), czas UTC, obiekt, operacja i zakres zmiany finansowej |

### Słowniki i rozdzielenie pojęć

- Kanały z arkusza: sklep, OLX, znajomy, inne. Telefon wynika z wymagań biznesowych. Kanał nie jest tym samym co źródło techniczne „ręczne / WooCommerce”.
- Metody płatności: przelew, BLIK, gotówka, pobranie. „Nieopłacone” jest stanem płatności, a osoba przyjmująca gotówkę opcjonalnym polem wpłaty „Przyjął”, bez wpływu na obliczenia.
- Statusy realizacji: w trakcie, zakończone, problem/reklamacja, anulowane. Status realizacji należy do ERP; status ze sklepu jest osobnym polem i poza anulowaniem nie zmienia statusu realizacji.
- Kategorie kosztów: zakup materiału u dostawców, cięcie, transport HDS, marketing (Google Ads, OLX, SEO, media społecznościowe), pozostała działalność, podatki VAT i PPE. Podatek wpisany w ewidencji nie oznacza automatycznego wyliczania zobowiązania podatkowego.
- Wypłaty właścicieli są osobnym rodzajem operacji (patrz „Rejestracja płatności”). Nie są kosztami działalności i nie wolno odejmować ich dwukrotnie.
- Dostawca może być więcej niż jeden; numer ZO i numer faktury nie są jednym identyfikatorem. Flagi zdjęć i opinii nie oznaczają integracji z usługą opinii.

## 8. Raportowanie i zasady finansowe

### Uzgodnione

Dostępne są dwa rozdzielone widoki: sprzedaż oraz przepływy pieniężne. Obliczenia wykonuje backend. Nie wolno liczyć pieniędzy typami zmiennoprzecinkowymi. UI stosuje polskie formatowanie i podaje walutę.

Reguły kwot (decyzja użytkownika z 01.11.2026): kwoty są typu `decimal(18,2)`, zapisywane do grosza. Ewentualne zaokrąglenie odbywa się połową od zera (0,005 → 0,01). Sumy są sumami zapisanych kwot, bez ponownego zaokrąglania. MVP działa wyłącznie w PLN, bez kursów walut. Zamówienie ma tylko wartość brutto, bez netto i stawki VAT. Wydatek ma kwotę brutto i opcjonalną kwotę netto przepisane z dokumentu. Formularz wydatku podpowiada netto jako brutto / 1,23 (VAT 23%, zaokrąglenie do grosza połową od zera); użytkownik może je poprawić, np. przy innej stawce albo 0 zł, a netto wpisane ręcznie nie jest nadpisywane. Zapisywana jest tylko kwota netto widoczna w formularzu; system nie przechowuje stawki i nie przelicza netto zapisanych wydatków (decyzja użytkownika z 08.11.2026). Raporty używają kwot brutto.

### Definicje widoków

Decyzja użytkownika z 01.11.2026 (D-01, D-03, D-04).

| Widok | Znaczenie | Nierozstrzygnięte |
| --- | --- | --- |
| Sprzedaż | Wartość brutto zamówień wszystkich statusów poza anulowanymi oraz poza zamówieniami pending, failed i szkicami ze sklepu, przypisana do okresu według daty zamówienia, niezależna od wpłat, pomniejszona o zwroty sprzedaży i korekty sprzedaży do tych zamówień w okresie ich dat; zwroty pieniędzy nie zmieniają sprzedaży; zamówienia sprzed daty uruchomienia, ich zwroty i korekty nie wchodzą do sprzedaży | Brak |
| Koszty w widoku sprzedaży | Wszystkie zarejestrowane wydatki brutto, opłacone i nieopłacone, według daty kosztu; obejmują kategorie VAT i PPE. Wydatki ujemne (korekty i zwroty od dostawców) pomniejszają koszty. Wystąpienia cykliczne liczą się jak wydatki. System liczy wpisane kwoty i nie wylicza zobowiązania podatkowego | Brak |
| Wynik przed wypłatami | Sprzedaż minus koszty w tym samym okresie; wypłaty właścicieli go nie obniżają. Pokazywany w widoku sprzedaży z opisem składników | Brak |
| Przepływy pieniężne | Rzeczywiste wpływy (wpłaty za zamówienia) minus rzeczywiste wypływy (opłacone wydatki, wypłaty właścicieli, zwroty) w okresie operacji pieniężnej; opłacony wydatek ujemny pomniejsza wypływy; bez salda rachunku | Brak |

Nie nazywać zmiany środków „zyskiem”, a wartości sprzedaży „otrzymanymi wpłatami”. „Wynik zarządczy” i „pozostało po wypłatach” można pokazać dopiero po zdefiniowaniu składowych. Nie prezentować salda rachunku na podstawie samych operacji bez salda początkowego.

Zabezpieczenia wymagane przy projektowaniu obliczeń:

1. Wpłata za zamówienie jest jedynym zapisem wpływu; nie dodaje się do niej osobnego wpisu przychodowego.
2. Ten sam koszt rejestruje się jednym wydatkiem. MVP nie wiąże wydatków z zamówieniami (D-07).
3. Planowany wydatek i niezapłacone zamówienie nie zwiększają rzeczywistych przepływów.
4. Kwota zwrotu jest odrębnym zdarzeniem, a nie utratą historii zamówienia (patrz „Anulowanie i zwrot”).
5. Wartość zero, brak danych i niekompletne dane mają odmienne znaczenia.
6. Nie przeliczać zapisanych kwot netto przez stałe dzielenie przez 1,23. Taki wzór występuje w DEMO; podpowiedź brutto / 1,23 w formularzu jest tylko wartością początkową pola, którą użytkownik zatwierdza albo poprawia (decyzja użytkownika z 08.11.2026).

Zakres raportów w MVP: bieżący miesiąc, poprzedni miesiąc, rok i własny zakres; agregacja miesięczna oraz porównanie okresów. Kategorie i kanały powinny prowadzić do danych szczegółowych. Porównanie procentowe przy bazie równej zero pokazuje „brak podstawy porównania”.

Definicje raportowe (decyzja użytkownika z 01.11.2026):

- Porównanie okresów: miesiąc porównuje się z poprzednim miesiącem, rok z poprzednim rokiem, a własny zakres z taką samą liczbą dni tuż przed nim. Gdy okres bazowy zaczyna się przed datą uruchomienia, widok pokazuje „brak podstawy porównania”.
- Rok: użytkownik wybiera rok kalendarzowy, domyślnie bieżący, a raport ma podział na miesiące. Bieżący rok obejmuje dane do dziś, a zakres zaczyna się najwcześniej od daty uruchomienia.
- Liczba zamówień: zamówienia wchodzące do sprzedaży w okresie według daty zamówienia, bez anulowanych, pending, failed, szkiców i zamówień sprzed uruchomienia. Zamówienie ze zwrotem lub korektą sprzedaży nadal się liczy.
- Liczba unikalnych klientów jest poza MVP.

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
| Architektura | Modularny monolit: zamówienia (także wpłaty i zwroty), wydatki (także wypłaty właścicieli, które pozostają odrębnym obiektem i nie są kosztem), raportowanie (także listy „Operacje” i „Historia zmian”, przez jawne interfejsy), integracje; Onion Architecture, SOLID, DRY, CQRS |
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
- Oba widoki finansowe mają zatwierdzone definicje i uzgadniają się z rekordami źródłowymi. Scenariusze obejmują niezapłacone zamówienie, wpłatę w innym miesiącu, korektę, zwrot sprzedaży, zwrot nadpłaty, oddanie towaru bez zwrotu pieniędzy zapisane korektą sprzedaży, korektę od dostawcy zapisaną jako wydatek ujemny, anulowanie opłaconego zamówienia ze zwrotem, zamówienie i wydatek otwarte w dniu uruchomienia oraz wydatek planowany.
- Zmianę raportu za miniony okres można wyjaśnić na liście „Historia zmian”.
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
| D-07 | Rozstrzygnięte 01.11.2026: MVP nie wiąże wydatków z zamówieniami. Koszty zakupów, cięcia i transportu są ewidencjonowane według kategorii, a rentowność pojedynczego zamówienia jest poza MVP. | Powiązanie po MVP wymaga osobnej decyzji |
| D-08 | Częściowo rozstrzygnięte 02.11.2026: SmarterASP.NET .NET Premium W1050; codzienna kopia SQL w niezależnym miejscu, retencja 30 dni; cele RPO 24 h i RTO 8 h zaakceptowane przez właściciela. Jeden użytkownik, około 20–30 zamówień miesięcznie przez sklep. Pozostają potwierdzenie parametrów konta/runtime, bezpiecznego harmonogramu odpornego na restart i usypianie, celów wydajności oraz próba odtworzenia. Szczegóły: [SF-1](tasks/sf-1/01-weryfikacja.md). | Blokuje ostateczną konfigurację środowisk do zakończenia weryfikacji technicznej |
| D-09 | Jaki okres retencji danych zamówień wynika z przepisów? Do potwierdzenia z księgowym lub prawnikiem. | Blokuje udostępnienie anonimizacji (DANE-01) |

## 13. Kolejność dalszych prac

D-01–D-07 są rozstrzygnięte. Najpierw sprawdzić hosting (D-08). Okres retencji (D-09) trzeba ustalić przed wdrożeniem anonimizacji. Następnie opisać model domenowy, własność pól i kontrakty API, przygotować środowiska i dostęp, wdrożyć zamówienia, wydatki oraz dwa widoki raportowania. Uruchomienie produkcyjne wymaga odbioru scenariuszy z sekcji 11.
