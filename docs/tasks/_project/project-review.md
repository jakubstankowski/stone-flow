---
result: PASS
reviewed: 2026-11-01
documents:
  - path: docs/PRD-stone-flow.md
    fingerprint: d140bf5ed7b481319222068696a6e59ea4ad0d72
  - path: docs/DESIGN-GUID-stone-flow.md
    fingerprint: 624aca12b2620c51eaefe1d4cd6412c7e4ceee08
---

# Przegląd dokumentacji projektu — Stone Flow

## Result: PASS

PRD 0.3 i design guide 0.3 można podzielić na zadania. Blokada z poprzedniego przeglądu jest zamknięta, a nowych blokad
nie ma. Nie wykryto sprzeczności między sekcjami ani z niezmiennikami. Luki z sekcji ryzyk nie zmieniają granic zadań i
można je rozstrzygnąć w specyfikacjach, ale pozycje oznaczone **„do specyfikacji”** trzeba zamknąć przed implementacją
danego zadania, bo inaczej kryteria z §6 i §11 mogą nie przejść.

Porównano PRD i design guide ze sobą, z tekstem obu dokumentów wymagań oraz z niezmiennikami z `.sdd/context/rules.md`.
Przeliczono kontrasty z design guide (zgodne). Pliki źródłowe przeszukano pod kątem domen i adresów e-mail: nie ma w nich
domeny sklepu; jedyne trafienie, `lh.pl` w arkuszu finansowym, jest niewyjaśnione i wygląda na dane kontrahenta. Formuł
arkuszy nie analizowano ponownie. Twierdzenia o zachowaniu WooCommerce wynikają z wiedzy o produkcie, nie z testu na
sklepie, dlatego są oznaczone „do sprawdzenia”.

Wynik względem poprzedniego przeglądu: zmiana z FAIL na PASS.

## ✅ Ready

- **Zamknięta blokada:** zawartość widoków jest określona (§5 „Zawartość widoków”, FIN-01–04, FIN-05, 06, design §4).
  Składniki sprzedaży są widoczne w zestawieniu wiersz po wierszu, z korektami do wcześniejszych miesięcy, a suma ma
  równać się karcie. Widok przepływów ma opisane karty, wykres, podział wypływów i ostatnie operacje. Obniżenia sprzedaży
  mają przypisany kanał, a suma kanałów równa się sprzedaży.
- **Zamknięte ryzyko:** fikcyjność danych DEMO potwierdził użytkownik 01.11.2026 (§1), więc niezmiennik 8 nie ma już
  otwartej pozycji.
- Cel, użytkownicy, problem i sekcja „poza zakresem” są konkretne. Rezygnacja z wielu kont, migracji, salda, zamykania
  okresów, wpływów spoza zamówień i powiązania wydatków z zamówieniami jest zapisana jako decyzja z datą.
- Definicje sprzedaży, kosztów, wyniku przed wypłatami i przepływów są w jednym miejscu (§8) i zgodne z design guide.
  Liczba zamówień, porównanie okresów i zakres „rok” są zdefiniowane.
- Stan płatności, nadpłata, wartość 0 zł, anulowanie, przywracanie, rodzaje zwrotu, korekta sprzedaży i limit obniżeń
  mają reguły i kryteria z przykładami liczbowymi. Prześledzono przejścia przez anulowanie, przywrócenie, pending i
  datę uruchomienia: sprzedaż pozostaje spójna.
- Przełom uruchomienia jest kompletny dla zamówień, wydatków, wpłat, zwrotów, korekt i wystąpień (§4, URU-01, §8, §11).
- Synchronizacja ma własność pól, definicję sukcesu, cztery stany w ustalonej kolejności i zakres ponowień (§5, INT-01).
- Reguły kwot są pełne: `decimal(18,2)`, zaokrąglanie, PLN, brutto i netto.
- ZAM-01–07, WYD-01–07 i FIN-01–06 mają pokrycie w PRD, podobnie wymagania technologiczne. Zawężenie do jednego konta
  jest zapisane jako świadome odstępstwo od wymagań biznesowych.
- Design guide ma mierzalne kryteria UI. Podane kontrasty są poprawne po przeliczeniu.
- Granice zadań są naturalne, a zależności można wskazać. D-08 otwiera prace (§13), a D-09 blokuje tylko DANE-01.

| Niezmiennik | Wynik | Uwagi |
| --- | --- | --- |
| 1. Sales separate from cash | Zgodny | Rodzaj zwrotu i korekta rozdzielają sprzedaż od pieniędzy; przyszła data zapłaty w ryzykach |
| 2. Backend decimal money | Zgodny | §8, KWO-01 |
| 3. API-enforced access | Zgodny | DOST-01; wyjątki webhooka i health endpointu w ryzykach |
| 4. Retries never duplicate | Zgodny | ZAM-02, WYD-03–06; anulowanie to jednorazowe przejście, ponowienie tego samego statusu niczego nie zmienia |
| 5. Financial audit trail | Zgodny | AUD-01; anonimizacja zachowuje ślad operacji |
| 6. Secrets stay server-side | Zgodny | §10 |
| 7. Module and layer boundaries | Zgodny | §10; właściciel danych audytu w ryzykach |
| 8. No identifying data in docs | Zgodny | Brak nazwy i domeny w PRD i design guide; dane DEMO potwierdzone jako fikcyjne (§1) |

## ❌ Blocking — fix before splitting into tasks

None.

## ⚠️ Risky areas — tight oversight during implementation

- **Obliczenia finansowe i dwa widoki (wysokie):** dane pieniężne, a arkusze nie są wzorcem kontrolnym.
  - **Do specyfikacji:** lista „Operacje” przy okresie obejmującym datę uruchomienia musi pomijać kwoty sprzed tej daty,
    inaczej FIN-05, 06 nie będzie spełnione. PRD opisuje wyłączenie dla raportów, a nie dla list. To samo dotyczy własnego
    zakresu: tylko dla roku zapisano, że zaczyna się najwcześniej od daty uruchomienia.
  - **Do specyfikacji:** karta „Koszty” nie ma kryterium uzgodnienia. FIN-05, 06 obejmuje sprzedaż i przepływy, a §11
    wymaga uzgodnienia obu widoków, także wyniku przed wypłatami. Lista wydatków ma w design guide jedną kolumnę „data”, a
    potrzebny jest filtr po dacie kosztu i to samo wyłączenie sprzed uruchomienia.
  - Suma zestawienia musi dotyczyć całego okresu, a nie strony przy paginacji.
- **Wpłaty, zwroty i korekty (wysokie):**
  - **Do specyfikacji:** brakuje walidacji dat przyszłych przy wpłatach, zwrotach, korektach i dacie zapłaty, dat
    wcześniejszych niż data zamówienia, kwoty wypłaty właściciela oraz ujemnej wartości zamówienia ręcznego. Przyszła data
    zapłaty oznacza wydatek jako „Opłacony”, więc plan staje się wypływem (niezmiennik 1).
  - **Do specyfikacji:** PRD nie mówi, czy po zapisie można zmienić rodzaj zwrotu. Limit i przeliczenie sprzedaży muszą
    to obsłużyć. Nieopisane są też: zmiana daty zamówienia ręcznego, usunięcie daty zapłaty i edycja zamówienia po
    anonimizacji.
  - Użytkownik może obniżyć wartość zamówienia i dodać zwrot lub korektę za to samo. Limit tego nie wykryje, jeśli suma
    mieści się w nowej wartości, więc formularze powinny wyjaśniać różnicę.
  - Zamówienie 1000 zł z zaliczką 300 zł i korektą 500 zł ma trwale stan „Częściowo opłacone”, choć klient jest winien
    200 zł, a przy pełnej korekcie nieopłaconego zamówienia zostaje „Nieopłacone”. Takie pozycje zaśmiecą szukanie
    nieopłaconych zamówień, czyli cel z §2. PRD nie wymaga też filtra po stanie płatności ani kwoty do zapłaty, a
    „wykrywanie brakujących kosztów” z §2 nie ma żadnego wymagania.
  - Wydatek z datą kosztu w przyszłości wchodzi do miesiąca, ale nie do bieżącego roku liczonego „do dziś”. PRD nie
    określa, czy bieżący miesiąc też kończy się dziś.
  - Raport za przeszłość zmienia się także, gdy zamówienie pending przechodzi do sprzedaży po terminie.
- **Audyt i „Historia zmian” (wysokie):**
  - **Do specyfikacji:** §10 nie wskazuje właściciela danych audytu. Zapisują je wszystkie moduły, listę ma raportowanie,
    a anonimizacja je zmienia (niezmiennik 7). Zadanie audytu powstaje jako jedno z pierwszych, więc decyzja musi zapaść
    przed nim.
  - **Do specyfikacji:** trzeba potwierdzić, że zmiana statusu ze sklepu, która zmienia sprzedaż, też tworzy wpis.
  - AUD-01 wymaga wskazania zmiany, która wywołała różnicę w raporcie, ale filtr działa po czasie zmiany. Filtr po dacie
    biznesowej lub okresie raportu ułatwiłby to przy dłuższej historii.
  - Usuwanie wydatków, wpłat, zwrotów i korekt jest trudne do odwrócenia, a ślad usunięcia blokuje parę definicja–okres.
- **Webhooki WooCommerce (wysokie):**
  - **Do sprawdzenia przed specyfikacją:** zdanie „przerwy w działaniu domykają się same” jest prawdopodobnie zbyt mocne
    dla usunięć. Trwale usuniętego zamówienia REST API nie zwróci, więc utracony webhook zostawia je w sprzedaży bez błędu,
    a „Ostatnia pełna kontrola” sugeruje kompletność. Jeśli to się potwierdzi, ZAM-02 nie zostanie spełnione dla usunięć i
    potrzebna będzie decyzja. Wiadomość o usunięciu może też nie mieć daty modyfikacji, której wymaga reguła pomijania
    starszych danych. Trzeba sprawdzić także widoczność kosza.
  - **Do specyfikacji:** znacznik „od ostatniego sukcesu” musi oznaczać początek przebiegu, inaczej zmiany z czasu
    przebiegu przepadną. Nie opisano nakładania się „Synchronizuj teraz” z przebiegiem o 23:00 ani stanu przed pierwszym
    sukcesem.
  - Błąd zamówienia, którego „Ponów” nie może zamknąć, bo zamówienia już nie ma, trzyma „Dane niekompletne” bez końca. Nie
    wiadomo też, czy udany zapis z webhooka zamyka błąd.
  - Trzeba sprawdzić, jak sklep reaguje na niedostępność lub uśpienie backendu, bo może wyłączyć webhook.
  - Statusy „refunded” i „on-hold” liczą się do sprzedaży, a niestandardowe statusy z D-02 domyślnie też. Model §7 nie ma
    pola na informację o zwrocie w sklepie, choć §5 nazywa ją informacją. Do ustalenia mapowanie pól: adres, źródło NIP,
    strefa czasowa daty zamówienia na granicy miesiąca i daty uruchomienia.
- **Wydatki cykliczne (wysokie):** reguły wymagają rozróżnienia okresu wstrzymanego od przerwy w działaniu systemu oraz
  zmiany początku bez tworzenia minionych wystąpień, a definicja ma tylko flagę aktywności. **Do specyfikacji:** trzeba
  zapisać, do którego okresu definicja została przetworzona. Otwarte są zmiana dnia miesiąca na termin, który już minął,
  oraz wydłużenie zakończonej definicji. Wznowienie tworzy okres bieżący także przy minionym terminie, co jest niezapisanym
  wyjątkiem od reguły o zaległych wystąpieniach.
- **Awaria zadań cyklicznych (średnie):** INT-01 obejmuje tylko WooCommerce. Niepowodzenie tworzenia wystąpień nie jest
  nigdzie widoczne, a koszty mogą być wtedy zaniżone bez ostrzeżenia.
- **Dane osobowe klientów (wysokie):** imiona, adresy, kontakty, NIP. Zakres anonimizacji obejmuje zamówienie i audyt.
  Otwarte są dane w historii błędów synchronizacji, logach, kopiach zapasowych i polach tekstowych (uwagi, powód zwrotu,
  powód korekty). D-09 musi określić także moment, od którego liczy się retencja. Anonimizacja jest nieodwracalna.
- **Uwierzytelnianie i sesja między domenami (wysokie):** Vercel i osobne API, CORS oraz CSRF przy cookies. **Do
  specyfikacji:** utworzenie pierwszego konta, czas ważności linku resetu, ochrona przed zgadywaniem hasła, wylogowanie i
  unieważnienie sesji po resecie (wymagania techniczne: „odebranie dostępu”). DOST-01 odrzuca „każdy” odczyt i zapis bez
  sesji, a webhook, logowanie, reset i health endpoint działają bez niej, więc specyfikacja musi wymienić te wyjątki.
  Dostawca wysyłki e-mail nie jest wybrany (D-05). Link z panelu sklepu jest wymagany, ale żadne kryterium nie sprawdza
  jego istnienia i nie wiadomo, kto go dodaje. Jedno konto przy kilku osobach rozliczanych nie pozwala wskazać, kto
  wykonał zmianę.
- **Hosting SMART ASP.NET, D-08 (wysokie):** od planu zależy uruchamianie zadania o 23:00 i wydatków cyklicznych przy
  usypianiu procesu. RPO, RTO, wolumen i cele wydajnościowe nie mają wartości, więc nie można ich odebrać. Muszą wynikać
  z zadania hostingowego.
- **Migracje produkcyjne i odtwarzanie (wysokie):** operacje nieodwracalne. §11 nie wymienia sprawdzenia procedury powrotu
  do działającej wersji ani braku sekretów w repozytorium i pakiecie frontendu, choć wymagają tego warunki odbioru z
  wymagań technologicznych (§13 tego dokumentu). §10 obejmuje oba tematy, więc zadanie wdrożeniowe musi je przejąć.
- **Współbieżna edycja (średnie):** ZAM-04 wymaga ostrzeżenia, a design guide opisuje ponowny odczyt. Otwarte jest, co
  dzieje się z równoczesnym zapisem użytkownika i synchronizacji oraz z dwoma wpisami przekraczającymi łącznie limit
  zwrotu lub limit obniżeń.
- **Porządki w dokumentach (niskie):**
  - Stan wydatku bez daty zapłaty ma dwie nazwy: „planowany” i „nieopłacony”. Wystąpienia powstają dopiero w dniu terminu,
    więc design guide oznaczy zaległy rachunek jako „Planowany”. Trzeba wybrać jedną etykietę w PRD.
  - Anonimizacja i lista „Operacje” nie występują na liście MVP w §4, choć mają wymagania w §5 i §6.
  - Status realizacji jest polem formularza, a anulowanie osobną akcją z ostrzeżeniem. Trzeba wyłączyć „Anulowane” z pola.
  - Przywrócenie w ERP zamówienia anulowanego w sklepie nie ma ostrzeżenia, choć odwrotne przypadki je mają.
  - „Przywróć zamówienie” mówi, że zamówienie wraca do sprzedaży, co nie zachodzi przy statusie pending ze sklepu.
  - Słowniki nie mówią, czy lista jest stała, czy edytowalna, co dotyczy kategorii, metod, płatników i osób przy wypłacie.
    Arkusze mają metodę „Płatność szybkim przelewem” i status „INNE”, których nie ma w słownikach. Pozycja „Ustawienia”
    nie ma zakresu, a design guide nie opisuje ekranu integracji ani logowania.
  - §5 mówi o wspólnych filtrach kart, wykresów i zestawienia, ale wymienia tylko okres; „Kategorie i kanały powinny
    prowadzić do danych szczegółowych” nie ma kryterium.
  - Model §7 używa dla zamówienia określeń „komentarz” i „dodatkowe uwagi”. Nie ma pól na oznaczenie usunięcia w sklepie
    ani na fakt anonimizacji.
  - Design guide pokazuje trzy pola KPI, a wymienia cztery wartości sprzedaży.
  - Porównanie bieżącego niepełnego miesiąca z pełnym poprzednim jest zgodne z regułą, ale wymaga opisu w UI.
  - Nagłówek PRD ma status „projekt wymagań do uzgodnienia”, a §13 uznaje D-01–D-07 za rozstrzygnięte.

## Suggested initial breakdown

Punkt wyjścia, nie backlog. Kolejność pokazuje zależności.

1. Weryfikacja hostingu, sposobu uruchamiania zadań i wersji technologii (D-08).
2. Weryfikacja zachowania WooCommerce na sklepie: usunięcia, kosz, statusy niestandardowe, reakcja na niedostępność.
3. Inicjalizacja monorepo, CI, szkielet modułów i motyw UI.
4. Logowanie, reset hasła, sesja, ochrona API i link z panelu sklepu.
5. Zapis audytu zmian finansowych (część wspólna).
6. Zamówienia ręczne, wspólna lista, anulowanie i przywracanie.
7. Wpłaty, zwroty, korekty sprzedaży, limit obniżeń i wyliczany stan płatności.
8. Import WooCommerce: webhooki, identyfikacja, własność pól, statusy i przejścia anulowania.
9. Odporność WooCommerce: dzienne uzupełnianie, historia błędów, ponowienia.
10. Stan integracji w UI.
11. Wydatki jednorazowe i zapłata wydatku.
12. Wydatki cykliczne i trwały harmonogram.
13. Wypłaty właścicieli i lista „Operacje”.
14. Lista „Historia zmian”.
15. Reguły raportowe i widok sprzedaży.
16. Widok przepływów pieniężnych.
17. Kopie zapasowe, wdrożenia, odbiór operacyjny i przełom uruchomienia.
18. Anonimizacja danych klienta (po rozstrzygnięciu D-09).
