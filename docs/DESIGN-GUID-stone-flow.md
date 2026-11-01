# Stone Flow — Design guide

Wersja: 0.3 · Data: 01.11.2026 · Status: wytyczne do projektu interfejsu.

Zakres produktu określa [PRD](PRD-stone-flow.md). Obowiązkowa paleta pochodzi z [wymagań technologicznych](../documents/wymagania%20technologiczne.docx). Układ inspirowany jest [dashboardem referencyjnym](../documents/example-dashboard.webp). Rozmiary, typografia, odcienie pomocnicze i opisane komponenty są propozycją projektową, a nie dodatkowymi wymaganiami źródłowymi.

## 1. Kierunek wizualny

Jasny, uporządkowany panel do codziennej pracy z zamówieniami i finansami. Ciepłe powierzchnie nawiązują do naturalnego kamienia; grafit zapewnia czytelność, a złoty brąz wskazuje najważniejsze działania. Informacja finansowa ma pierwszeństwo przed dekoracją.

Z referencji przejmujemy szeroki obszar roboczy, nagłówek, wyraźną nawigację, karty KPI z ikoną i dużą wartością, dwa sąsiadujące wykresy, miękkie zaokrąglenia i subtelne rozdzielenie powierzchni. Kolorowy gradient otoczenia, niebieska identyfikacja, bardzo jasne etykiety i rozbudowane menu raportów nie są wytycznymi dla Stone Flow. Nie przenosimy widocznych tam modułów magazynu, importu, EDI czy showroomu.

## 2. Kolory i tokeny

### Paleta obowiązkowa

| Token | Kolor | Zastosowanie |
| --- | --- | --- |
| `--sf-bg` | `#ebe8e5` | Podstawowe tło aplikacji i większe powierzchnie |
| `--sf-text` | `#323135` | Tekst, nagłówki, ciemna nawigacja |
| `--sf-accent` | `#9b6e2b` | Główny akcent, zaznaczenia, najważniejsze działania |
| `--sf-secondary` | `#b4a091` | Pomocnicze powierzchnie i dekoracyjne obramowania |

### Proponowane tokeny uzupełniające

| Token | Kolor | Zastosowanie |
| --- | --- | --- |
| `--sf-surface` | `#ffffff` | Karty, pola formularza, dialogi |
| `--sf-surface-soft` | `#f5f2ef` | Nagłówki tabel, zaznaczone sekcje |
| `--sf-muted` | `#656168` | Drugoplanowy tekst |
| `--sf-border-strong` | `#82786f` | Istotne granice pól i kontrolek |
| `--sf-accent-text` | `#79531e` | Drobny tekst akcentowy na jasnym tle |
| `--sf-accent-hover` | `#865e24` | Aktywny przycisk główny przy najechaniu |
| `--sf-success` | `#2f6b45` | Potwierdzone powodzenie |
| `--sf-warning` | `#805300` | Ostrzeżenie wymagające uwagi |
| `--sf-error` | `#a33434` | Błąd, operacja destrukcyjna |
| `--sf-info` | `#365d7d` | Informacja systemowa |

Wartości definiujemy centralnie w CSS i jednym własnym motywie daisyUI. Komponenty odwołują się do tokenów semantycznych, bez lokalnych kopii kodów HEX. Jasny motyw jest podstawą MVP; ciemny nie wynika z materiałów.

### Kontrast

Każda docelowa para tekst–tło wymaga sprawdzenia WCAG 2.2 AA: co najmniej 4,5:1 dla zwykłego tekstu, 3:1 dla dużego tekstu oraz istotnych elementów nietekstowych. Za duży tekst uznajemy co najmniej 24 px regular lub około 18,7 px bold.

- `#323135` stosować na jasnych powierzchniach; biały tekst na głównym akcencie jest przewidziany dla CTA.
- `#9b6e2b` na `#ebe8e5` nie jest bezpiecznym domyślnym kolorem drobnych etykiet; użyć `--sf-accent-text`.
- `#b4a091` nie służy do tekstu ani jako jedyna granica pola na jasnym tle. Do rozpoznawalnych kontrolek użyć mocniejszego obramowania.
- Wyłączenie kontrolki nie może ukrywać instrukcji potrzebnych do jej odblokowania.
- Status wymaga tekstu i opcjonalnej ikony; kolor sam nie wystarcza.

Weryfikować finalne pary także przy hover, focus, selected, błędach, przezroczystości i cieniach. Powyższe propozycje nie zastępują odbioru gotowego interfejsu.

Obliczone kontrasty dla pełnych, nieprzezroczystych kolorów: grafit na tle podstawowym około 10,58:1; tekst pomocniczy na tle podstawowym 4,97:1; ciemny tekst akcentowy na tle podstawowym 5,61:1; biel na głównym akcencie 4,51:1. Ostatnia para ma mały zapas — nie rozjaśniać tła CTA ani nie obniżać przezroczystości tekstu. Mocna granica pola na tle podstawowym ma około 3,54:1.

### Mapowanie motywu daisyUI

| Rola motywu | Token projektu |
| --- | --- |
| base-100 / base-content | surface / text |
| base-200 / base-300 | surface-soft / bg |
| primary / primary-content | accent / white |
| secondary / secondary-content | secondary / text |
| accent / accent-content | accent / white |
| neutral / neutral-content | text / white |
| success, warning, error, info | Odpowiednie tokeny semantyczne; zawartość o sprawdzonym kontraście |

Dokładną składnię konfiguracji dobrać do zatwierdzonych wersji Tailwind i daisyUI przy implementacji.

## 3. Układ i nawigacja

Propozycja desktopowa: szeroki panel na ciepłym tle, maksymalna szerokość obszaru około 1600 px, marginesy 24–32 px. Na mniejszych ekranach panel zajmuje dostępną szerokość. Nie zmniejszać całego interfejsu, aby zmieścić tabelę.

Nagłówek zawiera nazwę produktu, nazwę bieżącej sekcji, stan synchronizacji i menu użytkownika. Wyszukiwanie umieszczać przy listach; globalna wyszukiwarka i przełącznik firm z referencji nie należą do potwierdzonego MVP.

Główna nawigacja: **Pulpit, Zamówienia, Wydatki, Operacje, Historia zmian**; **Integracja / Ustawienia**. Wydatki cykliczne są podsekcją wydatków. Na szerokim ekranie preferowana jest pozioma nawigacja nawiązująca do obrazu. Opcjonalny lewy panel służy wyłącznie filtrom lub sekcjom bieżącego modułu, gdy rzeczywiście jest potrzebny.

Aktywna pozycja ma wyraźne tło, tekst i oznaczenie bieżącej strony. Na telefonie nawigacja przechodzi w dostępne menu, a filtry w rozwijany panel.

## 4. Pulpit

```text
Nagłówek: Stone Flow | Pulpit | Aktualność danych | Użytkownik
Nawigacja: Pulpit | Zamówienia | Wydatki | Operacje | Historia zmian | Integracja
Tytuł + widok [Sprzedaż / Przepływy pieniężne] + okres
Komunikat o niekompletnych danych, jeżeli występuje
KPI 1                  KPI 2                 KPI 3
Wykres zmian w czasie                        Sprzedaż: kategorie kosztów / kanały; Przepływy: wypływy według rodzaju
Sprzedaż: zestawienie składników sprzedaży; Przepływy: ostatnie operacje + przejście do listy „Operacje”
```

Przełącznik widoków jest wynikiem decyzji użytkownika zapisanej w PRD. Po zmianie widoku wszystkie KPI, wykresy i podpisy zmieniają podstawę jednocześnie.

- Sprzedaż: wartość sprzedaży, koszty, wynik przed wypłatami i liczba zamówień, według definicji z PRD sekcja 8. Opis wyniku wymienia jego składniki. Zestawienie pod wykresami: data, rodzaj (zamówienie, zwrot sprzedaży, korekta sprzedaży), kwota ze znakiem i przejście do zamówienia; jego suma równa się karcie sprzedaży. Na wykresie kanałów zwrot i korekta pomniejszają kanał swojego zamówienia.
- Przepływy: wpływy, wypływy i zmiana środków w okresie, wykres wpływów i wypływów w czasie oraz podział wypływów według rodzaju operacji. Karty prowadzą do listy „Operacje”. Saldo rachunku jest poza MVP.
- Wypłaty właścicieli pokazywać tylko we wypływach, a podatki VAT i PPE jako kategorie kosztów.
- Przy kwotach wskazać podstawę brutto, walutę i podstawę okresu. Drobna etykieta wyjaśnia znaczenie KPI.
- Porównanie okresów według reguły z PRD sekcja 8. Gdy baza równa się zero lub zaczyna się przed datą uruchomienia, widok pokazuje „brak podstawy porównania”. Wzrost kosztów nie jest automatycznie sukcesem oznaczonym zielenią.

Karta KPI ma małą ikonę na stonowanym tle, krótką etykietę, dużą wartość i opcjonalną informację o porównaniu. Klikalność musi wynikać z wyglądu i prowadzić do danych składających się na wartość.

## 5. Typografia, odstępy i powierzchnie

Proponowany font: systemowy `ui-sans-serif, system-ui, -apple-system, "Segoe UI", sans-serif`, bez zależności od zewnętrznego CDN. Wszystkie fonty muszą obsługiwać polskie znaki.

| Element | Rozmiar / grubość |
| --- | --- |
| Tytuł strony | 28–32 px / 600–700 |
| Tytuł sekcji | 20–24 px / 600 |
| Wartość KPI | 28–36 px / 600–700 |
| Tekst i pola | 16 px / 400–500 |
| Tabele i opisy | 14 px / 400–500 |
| Drobne metadane | 12–13 px, tylko gdy pozostają czytelne |

Interlinia tekstu około 1,5. Kwoty wyrównane do prawej, cyfry tabelaryczne (`tabular-nums`), jednakowa precyzja w kolumnie. Nie używać wersalików w długich etykietach i opisach.

Skala odstępów: 4, 8, 12, 16, 24, 32, 48 px. Karty: padding 20–24 px, odstępy 16–24 px, promień 16–20 px. Pola i przyciski: promień 8–12 px. Obszary interakcji docelowo co najmniej 44 × 44 px. Nagłówki i formularze nie mogą polegać na samym cieniu do rozdzielenia.

Cienie lekkie, np. `0 6px 24px rgb(50 49 53 / 6%)`. Bez intensywnego szkła, rozmycia, faktur marmuru za tekstem i wielobarwnych gradientów. Ikony z jednego zestawu, zwykle 20–24 px; ikona bez tekstu wymaga dostępnej nazwy.

## 6. Komponenty i zachowanie

### Przyciski

Jedna dominująca akcja na sekcję: „Dodaj zamówienie”, „Dodaj wydatek” albo „Zapisz”. Główna ma złotobrązowe tło i kontrastowy tekst. Drugorzędna używa jasnego tła i czytelnej granicy. Akcja usuwania jest opisana słowem „Usuń” i wymaga potwierdzenia wskazującego obiekt oraz skutek. Podczas zapisu wyświetla się stan „Zapisywanie…”, blokada ponownego wysłania i czytelny wynik.

### Formularze

Etykiety zawsze nad polami; placeholder jest przykładem, nie zastępuje etykiety. Błąd znajduje się przy polu i w podsumowaniu, jeśli dotyczy wielu pól. Dane pozostają po błędzie API. Informacja o wymaganiu pola jest dostępna również czytnikowi ekranu. Walidacja obejmuje zarówno UI, jak i API.

Formularz zamówienia grupuje dane klienta, wartość brutto, realizację oraz referencje/uwagi. Źródło techniczne i ID integracji nie są dowolnie edytowalne. Status realizacji jest polem, a stan płatności jest wyliczany z wpłat i zwrotów i tylko wyświetlany. Pola należące do WooCommerce (ID, data, dane klienta, wartość) są tylko do odczytu, mają oznaczenie źródła i wyjaśnienie ograniczeń edycji. Status ze sklepu jest pokazywany osobno od statusu realizacji.

Szczegóły zamówienia zawierają kwotę „Zapłacono”, listę wpłat i zwrotów z ich kwotami, datami i metodami oraz akcje „Dodaj wpłatę” i „Dodaj zwrot”; zwrot ma także powód i wymagany rodzaj bez wartości domyślnej: „Zwrot sprzedaży” albo „Zwrot pieniędzy”, z krótkim opisem wpływu każdego z nich na sprzedaż. Lista pokazuje rodzaj zwrotu. Dotyczy to również zamówień ze sklepu. Przy metodzie gotówka formularz wpłaty pokazuje opcjonalne pole „Przyjął”. Kwota wpłaty i zwrotu musi być większa od 0 zł, a zwrot nie może przekroczyć kwoty Zapłacono. Poprawka lub usunięcie, po którym Zapłacono spadłoby poniżej 0 zł, jest odrzucane z komunikatem, że najpierw trzeba poprawić lub usunąć zwrot.

Szczegóły zamówienia mają też akcję „Dodaj korektę sprzedaży” z kwotą, datą i powodem, bez metody. Krótki opis wyjaśnia, że korekta służy do oddania towaru bez zwrotu pieniędzy: obniża sprzedaż, a nie tworzy wypływu ani nie zmienia kwoty Zapłacono. Korekty są pokazane osobno od wpłat i zwrotów, można je poprawić lub usunąć, a usunięcie wymaga potwierdzenia. Formularze zwrotu i korekty oraz korekta wartości odrzucają zmianę, po której suma zwrotów sprzedaży i korekt sprzedaży przekroczyłaby wartość zamówienia, z komunikatem podającym dostępną kwotę.

Zamówień nie można usunąć, dostępna jest akcja „Anuluj zamówienie” z potwierdzeniem i ostrzeżeniem, jeżeli są wpłaty. Anulowane zamówienie ma akcję „Przywróć zamówienie” z potwierdzeniem, która ustawia status „W trakcie”. Dodanie wpłaty do anulowanego zamówienia pokazuje ostrzeżenie. Akcja „Anonimizuj dane klienta” jest dostępna po upływie okresu retencji; potwierdzenie mówi, że operacji nie można cofnąć, a kwoty i raporty pozostają bez zmian.

Ostrzeżenia w szczegółach zamówienia: „Nadpłata X zł”, gdy Zapłacono przekracza wartość; „Do zwrotu X zł”, gdy anulowane zamówienie ma Zapłacono większe od 0 zł; „Obniżenia sprzedaży przekraczają wartość o X zł”, gdy synchronizacja obniżyła wartość poniżej sumy zwrotów sprzedaży i korekt sprzedaży; „Zamówienie aktywne w sklepie”, gdy zamówienie jest anulowane w ERP, a w sklepie aktywne. Zamówienie usunięte w sklepie ma oznaczenie, że usunięto je w sklepie.

Formularz wydatku obejmuje kwotę brutto (wymaganą, różną od 0 zł), kwotę netto (opcjonalną), kategorię, daty, dokument i metodę. Ujemna kwota oznacza korektę lub zwrot od dostawcy; formularz i listy pokazują ją ze znakiem minus i opisem „Korekta / zwrot od dostawcy”. Opcja „Powtarzaj co miesiąc” ujawnia harmonogram, kwotę większą od 0 zł i podgląd następnego wystąpienia. Wystąpienie powstaje w dniu terminu, więc podgląd nie może sugerować, że można je wcześniej opłacić. Gdy początek definicji obejmuje minione terminy, formularz przed zapisem pokazuje, ile wystąpień powstanie od razu. Definicję można wstrzymać i wznowić, a komunikat wyjaśnia, że okresy z czasu wstrzymania pozostają puste. Data kosztu, termin i data zapłaty mają odrębne etykiety. Datę zapłaty można wpisać w formularzu albo później akcją „Oznacz jako opłacony”. Nie oznaczać planu jako opłaconego.

Formularz wypłaty właściciela obejmuje osobę, kwotę, datę i metodę. Otwiera go akcja „Dodaj wypłatę” na liście „Operacje”. Wypłatę można poprawić lub usunąć; usunięcie wymaga potwierdzenia.

### Tabele

Zamówienia: numer, klient, data, kanał/źródło, wartość zamówienia, zapłacono, stan płatności, stan realizacji. Dostawcy, ZO, faktury i komentarze w szczegółach lub opcjonalnych kolumnach. Zamówienia pending, failed i szkice ze sklepu są domyślnie ukryte i dostępne filtrem. Wydatki: data, opis, kategoria, kwota z podstawą, stan płatności i oznaczenie cykliczności. Operacje: data, rodzaj (wpłata, zwrot, opłacony wydatek, wypłata właściciela), opis z przejściem do źródła, kierunek, kwota i metoda, z filtrem okresu i rodzaju. Opłacony wydatek ujemny jest wypływem z kwotą ujemną. Suma listy przy tym samym okresie zgadza się z kartami przepływów. Korekty sprzedaży nie pojawiają się na tej liście. Historia zmian: czas, autor, obiekt z datą biznesową, operacja i zakres zmiany, z filtrem okresu zmiany i rodzaju obiektu; lista służy tylko do odczytu, wpis prowadzi do obiektu, a przy obiekcie usuniętym pokazuje ślad usunięcia.

Filtry przy tabeli, jawne aktywne filtry i „Wyczyść filtry”. Sortowanie ma widoczny kierunek i dostępny stan. Paginacja podaje zakres oraz liczbę rekordów. Na telefonie najważniejsze kolumny przechodzą w karty lub tabela ma kontrolowane przewijanie z zachowanym kontekstem. Nie ucinać kwoty ani statusu bez możliwości odczytu.

### Statusy

| Rodzaj | Przykłady | Reguła prezentacji |
| --- | --- | --- |
| Realizacja | W trakcie, Zakończone, Problem / reklamacja, Anulowane | Etykieta i opcjonalna ikona; nie oznacza stanu płatności |
| Płatność | Nieopłacone, Częściowo opłacone, Opłacone | Wyliczana z wpłat i zwrotów według PRD; osobna etykieta i kwota Zapłacono; zamówienie anulowane nie ma stanu; pobranie pozostaje metodą |
| Wydatek | Planowany, Opłacony | Stan zgodny z regułami finansowymi i harmonogramem |
| Synchronizacja | Aktualne, Synchronizacja trwa, Błąd, Dane niekompletne | Warunki stanów ustala PRD sekcja 5; zawsze „Ostatnia pełna kontrola” z czasem ostatniego sukcesu; akcje „Ponów” przy błędzie zamówienia i „Synchronizuj teraz” |

Dokładny słownik statusów oraz mapowanie WooCommerce ustala PRD. Nie dodawać statusów biznesowych wyłącznie dla potrzeb koloru.

## 7. Wykresy i liczby

Wykres liniowy do trendów, słupkowy do porównania miesięcy i kategorii. Preferować proste porównania nad skomplikowanymi kompozycjami. Bez 3D i nieopisanych osi.

Sprzedaż lub wpływy mogą używać złotego brązu, koszty lub wypływy grafitu, a dodatkowe serie odcieni pomocniczych. Linie różnią się także stylem lub znacznikami; słupki mają etykiety i legendę. Dwie różne podstawy finansowe nie mogą wyglądać jak jedna seria.

Każdy wykres ma tytuł, okres, jednostkę, legendę i tekstowe wartości albo tabelę danych. Tooltip nie może być jedynym sposobem odczytu; musi być dostępny klawiaturą. Oś słupków zaczyna się od zera; ujemne wartości są obsługiwane. Przy wielu kategoriach pokazać czytelne zestawienie zamiast kilkudziesięciu kolorów.

Formaty: `12 345,67 zł`, `28.10.2026`, `październik 2026`. MVP działa wyłącznie w PLN. Kwoty zerowe prezentować jako `0,00 zł`, brak wartości jako „Brak danych”. Techniczne czasy prezentować w Europe/Warsaw.

## 8. Stany systemu i komunikaty

- Ładowanie: skeleton zachowuje docelowe rozmiary; komunikat dostępny dla technologii asystujących. Nie pokazywać fikcyjnych zer.
- Brak danych: „Brak wydatków w wybranym okresie” i właściwa akcja. Odróżnić pustą ewidencję od pustego wyniku filtrowania.
- Błąd: wyjaśnienie skutku i akcja „Spróbuj ponownie”; bez stack trace i sekretów.
- Niekompletna integracja: „Nie pobrano wszystkich zamówień. Wyniki mogą być niepełne”, „Ostatnia pełna kontrola” z czasem ostatniego sukcesu i przejście do integracji.
- Sukces: krótki komunikat „Wydatek zapisany”; krytyczna informacja nie może zniknąć wyłącznie w toast.
- Konflikt edycji: informacja o nowszej wersji i możliwość ponownego odczytu, bez cichego nadpisania.
- Wygaśnięcie sesji po 60 minutach bezczynności: czytelny komunikat i powrót do logowania; bez obietnicy zachowania formularza, jeśli mechanizm tego nie zapewnia.

Mikroteksty po polsku, krótkie i konkretne. Nie używać etykiety „Dochód”, jeśli nie określono jej znaczenia w regułach finansowych.

## 9. Responsywność i dostępność

Proponowane progi: poniżej 768 px jedna kolumna; 768–1199 px układ pośredni; od 1200 px pełny pulpit i dwa wykresy obok siebie. Dostosowywać do treści, nie tylko urządzenia. Na małym ekranie KPI zawijają się, filtry mają pełne etykiety, a formularz jest jednokolumnowy. Cała strona nie może wymagać poziomego przewijania.

Obsługa klawiaturą obejmuje nawigację, filtry, daty, menu, formularze i dialogi. Fokus widoczny, np. grafitowa obwódka 2 px z odstępem 2 px, o sprawdzonym kontraście na danym tle. Dialog przechwytuje fokus i zwraca go do elementu wywołującego. Semantyczne nagłówki, formularze i tabele; komunikaty błędów powiązane z polami. Powiększenie i reflow nie mogą gubić działań. Animacje krótkie i ograniczone przy `prefers-reduced-motion`.

## 10. Odbiór projektu UI

- Obowiązkowe cztery kolory są zapisane centralnie i zgodne z wymaganiami.
- Układ nawiązuje do referencji przez hierarchię, karty i wykresy, zachowując zakres Stone Flow.
- Widoki sprzedaży i przepływów wyraźnie różnią etykiety, podstawę oraz znaczenie kwot.
- Kontrast wszystkich rzeczywiście użytych par spełnia wymagania; stany nie zależą wyłącznie od koloru.
- Na telefonie i tablecie można odczytać kwoty, filtrować dane oraz zapisać formularz.
- Każdy widok obsługuje ładowanie, pustkę, błąd, wygaśnięcie sesji i niekompletność danych, jeśli mogą wystąpić.
- Podwójne kliknięcie zapisu nie tworzy dwóch rekordów; walidacja i błędy nie usuwają wpisanych danych.
- Klawiatura, fokus, dialogi, etykiety i tekstowy odczyt wykresów są sprawdzone.
- Przykłady i zrzuty nie ujawniają danych klientów, sekretów, nazwy handlowej ani domeny sklepu.
