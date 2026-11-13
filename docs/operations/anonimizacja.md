# Anonimizacja danych klienta

Procedura ręcznej, nieodwracalnej anonimizacji danych klienta zamówienia (SF-22, PRD §5
„Anonimizacja danych klienta”, DANE-01). Zamówień się nie usuwa, więc żądanie usunięcia danych
obsługuje akcja „Anonimizuj dane klienta” w szczegółach zamówienia. Kroki właściciela lub
administratora oznaczono **[właściciel]**.

## 1. Cel i zasada

- Akcja dotyczy **jednego zamówienia** i wykonuje ją użytkownik po potwierdzeniu w oknie dialogowym.
  Nie ma anonimizacji automatycznej ani zbiorczej.
- Operacji **nie można cofnąć**. Synchronizacja ze sklepem (webhook, uzupełnianie o 23:00,
  „Synchronizuj teraz”, „Ponów”) nie przywraca potem danych klienta, a edycja zamówienia ich nie zmienia.
- Kwoty, daty, wpłaty, zwroty, korekty, kanał, numer, źródło i ID ze sklepu zostają, więc **raporty
  (Pulpit, zestawienie składników sprzedaży, „Operacje”) się nie zmieniają**.
- Akcja jest dostępna dopiero **po upływie okresu retencji** danych zamówień (D-09). Dostępność
  sprawdza API niezależnie od ekranu.

## 2. Stan domyślny: funkcja wyłączona

- Długość okresu retencji to otwarta decyzja **D-09** (PRD §12). Do jej rozstrzygnięcia funkcja jest
  **wyłączona w każdym środowisku** (fail-closed): kod, `appsettings*.json`, `launchSettings.json`,
  konfiguracja E2E i workflowy CI nie zawierają okresu. Pilnuje tego test
  `AnonymizationStartupTests.No_repository_file_sets_a_retention_period`.
- Rozpoznanie w aplikacji: w szczegółach zamówienia, w sekcji „Klient”, blok „Dane osobowe klienta”
  pokazuje wyłączony przycisk „Anonimizuj dane klienta” i tekst „Funkcja wyłączona do decyzji o okresie
  retencji.”. API odpowiada `422 anonymization_disabled`, także dla zamówienia, którego nie ma.

## 3. Włączenie **[właściciel]**

Kroki wykonuje się w tej kolejności. Pominięcie kroku 2 może otworzyć akcję za wcześnie, a operacji nie
da się cofnąć.

1. **D-09 rozstrzygnięta na piśmie** — właściciel z księgowym lub prawnikiem ustala okres przechowywania
   danych zamówień (w miesiącach).
2. **Porównanie reguły początku okresu z D-09 (warunek konieczny).** Aplikacja liczy okres od
   **najpóźniejszej** z dat:
   - data zamówienia;
   - najpóźniejsza data wpłaty, zwrotu albo korekty sprzedaży zamówienia;
   - data (Europe/Warsaw) ostatniej zmiany zamówienia lub jego wpisów — także anulowania,
     przywrócenia, edycji, zmiany ze sklepu i **usunięcia wpisu** (odczytana z „Historii zmian”).

   „Dostępne od” = początek okresu + liczba miesięcy kalendarzowych (31.01 + 1 miesiąc = ostatni dzień
   lutego). Jeżeli D-09 liczy okres inaczej (np. od końca roku obrotowego albo od innego zdarzenia),
   **nie ustawia się wartości**. Najpierw osobne zadanie zmienia regułę w
   `CustomerAnonymization.RetentionStart` (jedyne miejsce reguły, testy `CustomerAnonymizationTests`).
3. **Wpis w rejestrze właściciela**: data i treść potwierdzenia z kroku 2 (kto, kiedy, jaki okres,
   zgodność reguły).
4. **Ustawienie wartości** w konfiguracji serwera (nigdy w repozytorium), liczba całkowita 1–1200:
   - plik `appsettings.Production.json` w katalogu witryny API (jak data uruchomienia,
     [`data-uruchomienia.md`](data-uruchomienia.md) §1):

     ```json
     { "StoneFlow": { "Anonymization": { "RetentionMonths": <liczba-miesięcy> } } }
     ```

   - albo zmienna środowiskowa `StoneFlow__Anonymization__RetentionMonths=<liczba-miesięcy>`.

   Zapis pliku restartuje aplikację; wartość działa od restartu.
5. **Kontrola**:
   - `GET https://<adres-api>/health` = 200;
   - zamówienie starsze niż okres pokazuje aktywny przycisk i „Okres retencji upłynął {data}.”;
   - nowsze zamówienie pokazuje wyłączony przycisk i datę „dostępne od”;
   - błędna wartość (`0`, ujemna, ponad `1200`, nie liczba) **zatrzymuje start** — przyczyna w
     `logs/stdout_*.log`, komunikat podaje klucz `StoneFlow:Anonymization:RetentionMonths` i zakres, bez
     wartości. Pusta wartość oznacza funkcję wyłączoną.

## 4. Zmiana i wyłączenie

- Zmiana liczby miesięcy działa od restartu i nie zmienia zamówień już zanonimizowanych.
- Usunięcie wartości wyłącza akcję. Zamówienia zanonimizowane zostają oznaczone, a synchronizacja nadal
  nie przywraca ich danych (reguła domeny, niezależna od konfiguracji).

## 5. Co operacja usuwa, a co zostawia

| Miejsce | Usuwane | Zostaje |
|---|---|---|
| Zamówienie | nazwa klienta → `[zanonimizowano]`; telefon, e-mail, ulica, kod, miejscowość, NIP, uwagi do realizacji, komentarz → puste; skrót żądania utworzenia → zera | rodzaj klienta (osoba/firma), data, kanał, wartość, status realizacji, status ze sklepu, numer, źródło, ID ze sklepu, dostawcy, referencje ZO, numer faktury, flagi |
| Zwroty i korekty sprzedaży | powód → `[zanonimizowano]`; skrót żądania utworzenia → zera | kwota, data, rodzaj, metoda |
| Wpłaty | — | wszystkie pola, w tym „Przyjął” (osoba po stronie sklepu, nie klient) |
| „Historia zmian” zamówienia i jego wpisów (także wpisy usunięcia) | wartości przed i po w polach klienta, uwag, komentarza i powodu → `[zanonimizowano]` (brak wartości zostaje brakiem) | autor, czas, obiekt, numer zamówienia, data biznesowa, operacja, nazwy pól, pozostałe pola (kwoty, daty) |

Operacja zapisuje własny wpis „Anonimizacja” (operacja `anonymized`) bez danych osobowych: autor
„Właściciel”, czas UTC, zamówienie, lista wyczyszczonych pól, liczba zanonimizowanych powodów, początek
okresu retencji i okres w miesiącach. Każdy zwrot i korekta ze zmienionym powodem dostaje wpis
„Anonimizacja” z polem „Zanonimizowane pola: Powód”. Na zamówieniu zapisuje się czas i konto
wykonawcy (`AnonymizedAtUtc`, `AnonymizedByAccountId`).

Po anonimizacji:

- dane klienta są tylko do odczytu; API odrzuca ich zmianę (`422 order_customer_anonymized`), także
  zmianę samego rodzaju klienta;
- pozostałe pola ERP, wpłaty, zwroty, korekty, anulowanie i przywrócenie działają jak dotąd. Nowe
  uwagi, komentarze i powody wpisuje świadomie użytkownik;
- ponowienie starego żądania utworzenia zamówienia z tym samym kluczem dostaje
  `409 idempotency_key_reused` (komunikat podaje tylko numer zamówienia);
- wyszukiwanie po dawnej nazwie klienta nie znajduje zamówienia.

## 6. Czego operacja nie obejmuje

- **Sklep WooCommerce.** Dane klienta w sklepie (zamówienie, konto klienta, notatki, wysłane e-maile)
  są poza zasięgiem aplikacji. **Żądanie usunięcia danych trzeba obsłużyć osobno w panelu sklepu
  [właściciel].** Uzupełnianie i „Ponów” pobierają dane zamówienia ze sklepu ponownie tylko do pamięci,
  a aplikacja pomija w nich dane klienta zamówienia zanonimizowanego.
- Wydatki (kontrahent, płatnik, komentarz), wypłaty właścicieli (osoba) i pole „Przyjął” wpłaty.
- Kopie zapasowe (sekcja 7) i logi hostingu (sekcja 8).
- Historia błędów synchronizacji (`integrations.WooCommerceOrderErrors`) nie przechowuje danych klienta
  (komunikaty z kodów i ID, treści dostaw nie są zapisywane), więc nie wymaga zmian.

## 7. Kopie zapasowe

- Kopie bazy mają retencję 30 dni (D-08, [`kopie-i-odtworzenie.md`](kopie-i-odtworzenie.md) §1), więc
  dane zanonimizowane znikają z kopii najpóźniej po 30 dniach.
- **Odtworzenie kopii sprzed anonimizacji przywraca dane klienta i usuwa wpis operacji.** Po każdym
  odtworzeniu właściciel ponawia anonimizacje wykonane po dacie kopii, według rejestru z sekcji 9
  ([`kopie-i-odtworzenie.md`](kopie-i-odtworzenie.md) §7).

## 8. Logi

Operacja niczego nie loguje ponad standardowe logi żądań (identyfikator zamówienia w ścieżce,
identyfikator korelacji). Logi aplikacji nie zawierają danych klienta (niezmiennik 6, logi Integrations
tylko z ID). Komunikaty błędów nie zawierają danych osobowych.

## 9. Rejestr żądań **[właściciel]**

Właściciel prowadzi rejestr żądań usunięcia danych **poza aplikacją i poza repozytorium**: data
żądania, numer zamówienia, data wykonania anonimizacji, data obsługi w sklepie. Rejestr nie zawiera
danych klienta. Służy do ponowienia anonimizacji po odtworzeniu kopii (sekcja 7).

## 10. Powrót do wersji sprzed SF-22

- Do pierwszego użycia funkcji (funkcja wyłączona) powrót do wersji sprzed SF-22 jest bezpieczny:
  migracja `CustomerAnonymization` tylko dodaje kolumny z `NULL`, których stara wersja nie mapuje.
- **Po pierwszej anonimizacji nie wraca się do wersji sprzed SF-22**: stara „Historia zmian” nie zna
  operacji `Anonymized`, a stara synchronizacja nadpisałaby znacznik danymi klienta ze sklepu
  ([`powrot-do-wersji.md`](powrot-do-wersji.md) §1).

## 11. Uwagi techniczne

- Kolejność kontroli `POST /api/orders/{id}/anonymize`: wersja (`400`), funkcja wyłączona (`422
  anonymization_disabled`, bez odczytu bazy), w transakcji: istnienie pod blokadą (`404`), wersja
  (`409 order_conflict`), stan (`409 order_already_anonymized`), retencja (`422 retention_not_elapsed` z
  `availableFrom`).
- Zmiany zamówienia, wpisów i audytu oraz wpisy operacji zapisują się w jednej transakcji; błąd w
  dowolnym kroku wycofuje całość.
- Anonimizacja bierze blokadę zamówienia (`IOrderLock`), więc szereguje się z zapisami wpłat, zwrotów i
  korekt. Synchronizacja ze sklepem celowo jej nie bierze (szereguje ją blokada po ID w sklepie);
  spójność daje `rowversion` zamówienia: przestarzały zapis synchronizacji jest ponawiany na świeżych
  danych zanonimizowanego zamówienia, a przestarzała anonimizacja dostaje `409 order_conflict`.
- Wpisy audytu zmienia wyłącznie port `IAuditRedaction` (jedyny wyjątek od zasady „wpisy są tylko
  dodawane”).
