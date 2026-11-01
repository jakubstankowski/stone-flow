---
result: FAIL
reviewed: 2026-11-01
documents:
  - path: docs/PRD-stone-flow.md
    fingerprint: c9ed67b52ca45399ffbb51c94750941d28c88c73
  - path: docs/DESIGN-GUID-stone-flow.md
    fingerprint: f0efc8f2556e9265ce0f50f88aa8dd810e277cc5
---

# Przegląd dokumentacji projektu — Stone Flow

## Result: FAIL

Trzy blokady z poprzedniego przeglądu są zamknięte, a dokument jest bardzo blisko gotowości. Zostały dwie
nowe blokady, obie do usunięcia kilkoma zdaniami w PRD: zwrot sprzedaży nie ma górnego limitu ani drogi dla
towaru oddanego bez zwrotu pieniędzy, a audyt, na którym opiera się decyzja o braku zamykania okresów, nie ma
ekranu ani kryterium odbioru.

Porównano PRD i design guide ze sobą, z oboma dokumentami wymagań oraz z niezmiennikami z
`.sdd/context/rules.md`. Arkuszy DEMO nie analizowano ponownie: wnioski o nich pochodzą z poprzedniego
przeglądu. Twierdzenia o zachowaniu WooCommerce wynikają z wiedzy o produkcie i nie zostały sprawdzone na
sklepie, dlatego są oznaczone „do sprawdzenia”.

Wynik względem poprzedniego przeglądu: bez zmiany (FAIL → FAIL), ale wszystkie poprzednie blokady są
zamknięte, a obie poniższe są nowe.

## ✅ Ready

- **Zamknięte blokady:** rodzaj zwrotu rozdziela zwrot sprzedaży od zwrotu pieniędzy, także dla zamówień poza
  sprzedażą. Ujemny wydatek obsługuje korekty od dostawców, a wpływy spoza zamówień są świadomie poza MVP.
  Definicja cykliczna ma jedną regułę dla minionych terminów, z podglądem liczby wystąpień.
- **Zamknięte ryzyka z poprzedniego przeglądu:** kolejność dostaw (daty modyfikacji w sklepie), autor systemowy
  w audycie, anonimizacja danych klienta, decyzja o niezamykaniu okresów, porządki w nagłówkach i w D-01–D-07.
- Cel, użytkownicy, problem i sekcja „poza zakresem” są konkretne. Rezygnacja z wielu kont, migracji, salda i
  powiązania wydatków z zamówieniami jest zapisana jako decyzja z datą.
- Definicje sprzedaży, kosztów, wyniku przed wypłatami i przepływów są w jednym miejscu (§8) i zgodne z design
  guide. Liczba zamówień, porównanie okresów i zakres „rok” są zdefiniowane.
- Stan płatności, nadpłata, wartość 0 zł, anulowanie i przywracanie mają reguły oraz kryteria (§5, PLAT-04–06,
  ZAM-08). Scenariusze przejść przez anulowanie i przywrócenie dają spójną sprzedaż.
- Przełom uruchomienia jest kompletny dla zamówień, wydatków, wpłat, zwrotów i wystąpień (§4, URU-01, §11).
- Synchronizacja ma definicję sukcesu, cztery stany w ustalonej kolejności i zakres ponowień (§5, INT-01).
- Reguły kwot są pełne: `decimal(18,2)`, zaokrąglanie, PLN, brutto i netto.
- ZAM-01–07, WYD-01–07 i FIN-01–06 mają pokrycie w PRD, podobnie warunki techniczne. Zawężenie do jednego konta
  jest zapisane jako świadome odstępstwo od wymagań biznesowych.
- Design guide ma mierzalne kryteria UI: tokeny, kontrast, formaty i stany.
- Granice zadań są naturalne, a zależności można wskazać. D-08 otwiera prace (§13), a D-09 blokuje tylko DANE-01.

| Niezmiennik | Wynik | Uwagi |
| --- | --- | --- |
| 1. Sales separate from cash | Zgodny | Rodzaj zwrotu usuwa poprzednie naruszenie |
| 2. Backend decimal money | Zgodny | §8, KWO-01 |
| 3. API-enforced access | Zgodny | DOST-01; wyjątki webhooka i health endpointu w ryzykach |
| 4. Retries never duplicate | Zgodny | ZAM-02, WYD-03–06; anulowanie to jednorazowe przejście, nie nadpisanie przy ponowieniu |
| 5. Financial audit trail | Zgodny | Zapis jest wymagany i ma autora systemowego; brak odczytu w blokadzie 2 |
| 6. Secrets stay server-side | Zgodny | §10 |
| 7. Module and layer boundaries | Zgodny | §10; właściciel danych audytu w ryzykach |
| 8. No identifying data in docs | Zgodny dla PRD i design guide | Pliki DEMO w ryzykach |

## ❌ Blocking — fix before splitting into tasks

1. **Zwrot sprzedaży nie ma górnego limitu ani drogi bez ruchu pieniędzy (§5 „Rejestracja płatności”,
   „Anulowanie i zwrot”, PLAT-04, PLAT-05):** jedynym limitem zwrotu jest kwota Zapłacono, a ta dotyczy
   pieniędzy, nie sprzedaży. Daje to błędny wynik w dwóch kierunkach:
   - **Sprzedaż ujemna.** Zamówienie 1000 zł, wpłaty 1200 zł. Reguły pozwalają dodać „Zwrot sprzedaży” 1200 zł,
     więc zamówienie wnosi do sprzedaży −200 zł. Powinno wnieść najwyżej 0 zł, a 200 zł to zwrot pieniędzy.
     To samo zdarza się, gdy wartość obniżono po zwrocie sprzedaży.
   - **Oddanego towaru nie da się ująć.** Zamówienie ze sklepu 1000 zł, zaliczka 300 zł, klient oddaje towar za
     500 zł. Zwrot ma limit 300 zł, a wartość jest tylko do odczytu, więc sprzedaż nie spadnie poniżej 700 zł. Bez
     wpłaty zwrotu nie ma w ogóle, a anulowanie obejmuje całe zamówienie. Problem nie znika przez synchronizację,
     jeśli zwrot w WooCommerce nie zmienia sumy zamówienia (do sprawdzenia; dla zamówienia ręcznego wystarcza
     korekta wartości).

   → Dopisać w §5 i PLAT-05, że suma zwrotów sprzedaży nie może przekroczyć wartości zamówienia, oraz co się dzieje,
   gdy wartość spadnie poniżej tej sumy. Zdecydować, jak obniżyć sprzedaż zamówienia ze sklepu bez ruchu pieniędzy,
   na przykład dopuszczając zwrot sprzedaży bez wypływu albo świadomie uznając ten przypadek za niewspierany. Zmienić
   formularz zwrotu w design guide i dodać scenariusz do §11.

2. **Odczyt audytu nie ma zakresu ani kryterium (§4, §6, §7, design §3, §6):** decyzja o niezamykaniu okresów
   mówi, że raporty za przeszłość mogą się zmieniać, „a przyczynę zmiany pokazuje audyt”. Tymczasem audyt nie ma
   żadnego wymagania w §6, pozycji w nawigacji, ekranu w design guide ani warunku w §11. Kryteria mówią tylko, że
   operacje zostawiają ślad, a WYD-01, 02 wymaga tego wyłącznie przy usunięciu, choć niezmiennik 5 obejmuje każdą
   zmianę. Bez tego nie wiadomo, czy podział ma zawierać zadanie na ekran audytu, czy tylko zapis w bazie, a
   właściciel nie sprawdzi, dlaczego zmienił się raport za zamknięty miesiąc. → Zdecydować, gdzie właściciel czyta
   audyt: osobna lista, historia przy obiekcie albo oba miejsca. Dodać wymaganie z kryterium odbioru obejmujące
   zapis każdej zmiany finansowej (autor, czas UTC, obiekt, operacja, zakres) i jego odczyt, a w design guide
   miejsce i wygląd. Jeśli odczyt nie wchodzi do MVP, zmienić zdanie w §4.

## ⚠️ Risky areas — tight oversight during implementation

- **Obliczenia finansowe i dwa widoki (wysokie):** dane pieniężne, a arkusze nie są wzorcem kontrolnym.
  - Kryterium uzgodnienia z listą dotyczy tylko przepływów (FIN-05, 06), a §11 wymaga uzgodnienia obu widoków.
    Sprzedaży nie da się uzgodnić z listą zamówień, bo zawiera pozycje spoza sprzedaży i nie pokazuje zwrotów w ich
    okresach. Specyfikacja raportu musi określić zestawienie składników sprzedaży.
  - Lista „Operacje” przy okresie obejmującym datę uruchomienia musi pomijać kwoty sprzed tej daty, inaczej FIN-05,
    06 nie będzie spełnione. PRD opisuje to dla raportów i roku, ale nie dla listy.
  - FIN-01–04 nie opisuje kart ani wykresów przepływów, a design guide nie mówi, co pokazuje wykres
    „Kategorie / kanały” w tym widoku.
- **Wpłaty, zwroty i korekty (wysokie):**
  - Użytkownik może obniżyć wartość zamówienia i dodać zwrot sprzedaży za to samo, co odejmie kwotę dwa razy. Formularz
    powinien to wyjaśniać.
  - Zamówienie po częściowym zwrocie sprzedaży jest trwale „Częściowo opłacone”, a po pełnym „Nieopłacone”. Tak
    zapisano regułę, ale takie pozycje pojawią się przy szukaniu nieopłaconych zamówień (cel z §2).
  - Nie ma walidacji dat przyszłych przy wpłatach, zwrotach i dacie zapłaty, kwoty wypłaty właściciela ani ujemnej
    wartości zamówienia ręcznego.
  - Raport za przeszłość zmienia się także, gdy zamówienie pending przechodzi do sprzedaży po terminie.
- **Webhooki WooCommerce (wysokie):**
  - Zdanie „przerwy w działaniu domykają się same” jest prawdopodobnie zbyt mocne dla usunięć (do sprawdzenia): trwale
    usuniętego zamówienia REST API nie zwróci, więc utracony webhook zostawia je w sprzedaży bez błędu, przy
    „Ostatniej pełnej kontroli” sugerującej kompletność. Wiadomość o usunięciu może też nie mieć daty modyfikacji, której
    wymaga reguła pomijania starszych danych. Trzeba sprawdzić także widoczność kosza.
  - Znacznik „od ostatniego sukcesu” musi oznaczać początek przebiegu, inaczej zmiany z czasu przebiegu przepadną.
  - Nie opisano nakładania się „Synchronizuj teraz” z przebiegiem o 23:00 ani stanu przed pierwszym sukcesem.
  - Błąd zamówienia, którego „Ponów” nie może zamknąć, bo zamówienia już nie ma, trzyma stan „Dane niekompletne” bez
    końca. Nie wiadomo też, czy udany zapis z webhooka zamyka błąd.
  - Trzeba sprawdzić, jak sklep reaguje na niedostępność lub uśpienie backendu, bo może wyłączyć webhook.
  - Statusy „refunded” i „on-hold” liczą się do sprzedaży, a niestandardowe statusy z D-02 domyślnie też. Do ustalenia
    mapowanie pól: adres, źródło NIP, strefa czasowa daty zamówienia na granicy miesiąca.
- **Wydatki cykliczne (wysokie):** reguły wymagają rozróżnienia okresu wstrzymanego od przerwy w działaniu systemu oraz
  zmiany początku bez tworzenia minionych wystąpień, a definicja ma tylko flagę aktywności i nie ma historii. Specyfikacja
  musi zapisać, do którego okresu definicja została przetworzona. Otwarte są zmiana dnia miesiąca na termin, który już
  minął, oraz wydłużenie zakończonej definicji. Wznowienie tworzy okres bieżący także przy minionym terminie, co jest
  niezapisanym wyjątkiem od reguły o zaległych wystąpieniach.
- **Awaria zadań cyklicznych (średnie):** INT-01 obejmuje tylko WooCommerce. Niepowodzenie tworzenia wystąpień nie jest
  nigdzie widoczne, a koszty mogą być wtedy zaniżone bez ostrzeżenia.
- **Dane osobowe klientów (wysokie):** imiona, adresy, kontakty, NIP. Zakres anonimizacji obejmuje zamówienie i audyt.
  Otwarte są dane w historii błędów synchronizacji, logach, kopiach zapasowych i polach tekstowych (uwagi, powód zwrotu).
  D-09 musi określić także moment, od którego liczy się retencja. Anonimizacja jest nieodwracalna.
- **Usuwanie danych i audyt (wysokie):** ślad usunięcia blokuje parę definicja–okres, a usuwanie wydatków, wpłat i zwrotów
  jest trudne do odwrócenia. §10 nie wskazuje właściciela danych audytu, a anonimizacja je zmienia. Jedno konto przy
  kilku osobach rozliczanych nie pozwala wskazać, kto wykonał zmianę.
- **Uwierzytelnianie i sesja między domenami (wysokie):** Vercel i osobne API, CORS oraz CSRF przy cookies. Brakuje
  utworzenia pierwszego konta, czasu ważności linku resetu, ochrony przed zgadywaniem hasła, wylogowania i unieważnienia
  sesji po resecie (wymagania techniczne: „odebranie dostępu”). Dostawca wysyłki e-mail nie jest wybrany. DOST-01 odrzuca
  „każdy” zapis bez sesji, a webhook i health endpoint działają bez niej, więc specyfikacja musi wymienić te wyjątki.
  Link z panelu sklepu jest wymagany, ale żadne kryterium nie sprawdza jego istnienia i nie wiadomo, kto go dodaje.
- **Hosting SMART ASP.NET, D-08 (wysokie):** od planu zależy uruchamianie zadania o 23:00 i wydatków cyklicznych przy
  usypianiu procesu. RPO, RTO, wolumen i cele wydajnościowe nie mają wartości, więc nie można ich odebrać. Muszą wynikać
  z zadania hostingowego.
- **Migracje produkcyjne i odtwarzanie (wysokie):** operacje nieodwracalne.
- **Współbieżna edycja (średnie):** ZAM-04 wymaga ostrzeżenia, a design guide opisuje ponowny odczyt. Otwarte jest, co dzieje
  się z równoczesnym zapisem użytkownika i synchronizacji oraz z dwoma zwrotami przekraczającymi łącznie limit.
- **Pliki DEMO w repozytorium, niezmiennik 8 (średnie):** poprzedni przegląd wykazał w arkuszach firmy, adresy, ciągi o
  formacie telefonu i NIP oraz imiona osób rozliczanych. Pliki są śledzone w git. Właściciel danych powinien potwierdzić,
  że dane są fikcyjne.
- **Porządki w dokumentach (niskie):**
  - Stan wydatku bez daty zapłaty ma dwie nazwy: „planowany” i „nieopłacony”. Wystąpienia powstają dopiero w dniu terminu,
    więc design guide oznaczy zaległy rachunek jako „Planowany”. Trzeba wybrać jedną etykietę w PRD.
  - Anonimizacja i lista „Operacje” nie występują na liście MVP w §4, choć mają wymagania w §6 i §5.
  - Przywrócenie w ERP zamówienia anulowanego w sklepie nie ma ostrzeżenia, choć odwrotne przypadki je mają.
  - Słowniki nie mówią, czy lista jest stała, czy edytowalna, co dotyczy kategorii, metod i osób przy wypłacie. Arkusze mają
    metodę „Płatność szybkim przelewem” i status „INNE”, których nie ma w słownikach. Pozycja „Ustawienia” nie ma zakresu.
  - Model §7 używa dla zamówienia określeń „komentarz” i „dodatkowe uwagi” i nie ma pola na oznaczenie usunięcia w sklepie.
    PRD nie przypisuje kanału zamówienia ze sklepu do żadnej strony własności pól.
  - Design guide pokazuje trzy pola KPI, a wymienia cztery wartości sprzedaży.
  - Porównanie bieżącego niepełnego miesiąca z pełnym poprzednim jest zgodne z regułą, ale wymaga opisu w UI.

## Suggested initial breakdown

Punkt wyjścia, nie backlog. Kolejność pokazuje zależności.

1. Weryfikacja hostingu, sposobu uruchamiania zadań i wersji technologii (D-08).
2. Inicjalizacja monorepo, CI, szkielet modułów i motyw UI.
3. Logowanie, reset hasła, sesja, ochrona API i link z panelu sklepu.
4. Audyt zmian finansowych (część wspólna, zapis i odczyt).
5. Zamówienia ręczne, wspólna lista, anulowanie i przywracanie.
6. Wpłaty, zwroty i wyliczany stan płatności.
7. Import WooCommerce: webhooki, identyfikacja, własność pól, statusy i przejścia anulowania.
8. Odporność WooCommerce: dzienne uzupełnianie, historia błędów, ponowienia.
9. Stan integracji w UI.
10. Wydatki jednorazowe i zapłata wydatku.
11. Wydatki cykliczne i trwały harmonogram.
12. Wypłaty właścicieli i lista „Operacje”.
13. Reguły raportowe i widok sprzedaży.
14. Widok przepływów pieniężnych.
15. Kopie zapasowe, wdrożenia, odbiór operacyjny i przełom uruchomienia.
16. Anonimizacja danych klienta (po rozstrzygnięciu D-09).

Zadania 6 i 13 zależą od blokady 1, a zadanie 4 od blokady 2.
