# Data uruchomienia

Procedura ustawienia daty uruchomienia Stone Flow (SF-21, PRD §4). Od tej daty raporty liczą
sprzedaż, koszty i operacje pieniężne; wcześniejsze zamówienia i koszty nie wchodzą do raportów, a
pieniądze z nimi związane liczą się według własnej daty (PRD §4, scenariusze „otwarte w dniu
uruchomienia”). Kroki właściciela lub administratora oznaczono **[właściciel]**; wynik trafia do
[`odbior-mvp.md`](odbior-mvp.md).

## 1. Kto i gdzie

- **Datę podaje właściciel** przed startem produkcyjnym.
- **Administrator wpisuje ją [właściciel]** jako `StoneFlow:LaunchDate` w formacie `yyyy-MM-dd` w pliku
  `appsettings.Production.json` w katalogu witryny API (`wdrozenia.md` §4):

  ```json
  { "StoneFlow": { "LaunchDate": "<yyyy-MM-dd>" } }
  ```

- Data **nigdy** nie trafia do repozytorium ani do zmiennych GitHub — jest konfiguracją serwera.
- Zapis pliku restartuje aplikację w IIS; nowa wartość działa od następnego żądania.

## 2. Brak daty albo zły format

- API **nie startuje** (walidacja opcji przy starcie, `ValidateOnStart`). Przyczyna jest w
  `logs/stdout_*.log` w katalogu witryny — komunikat wskazuje klucz, bez wartości.
- Produkcja nigdy nie przyjmuje daty domyślnej: `appsettings.json` jej nie ma, a
  `appsettings.Development.json` (z przykładową datą) nie jest wczytywany w środowisku Production.
- Dowód automatyczny: testy integracyjne `LaunchDateStartupTests` (brak klucza, zły format, brak w
  `appsettings.json`; fabryka testowa działa w Production) — zadanie CI `backend`.

## 3. Kontrola po ustawieniu **[właściciel]**

1. `GET https://<adres-api>/health` = 200.
2. Po zalogowaniu: `GET https://<adres-aplikacji>/api/operations` — pole `launchDate` ma ustawioną datę.
3. Pulpit: wybierz okres przecinający datę uruchomienia (np. „Własny zakres” od dnia wcześniej) —
   widoczny jest komunikat „Raport liczony od …” z tą datą.
4. Wynik (data ustawienia, osoba, wartość daty, wynik kontroli) wpisz do `odbior-mvp.md`.

## 4. Data jest jednorazowa

- Zmiana daty po starcie zmienia raporty wstecz (inne okresy liczone, inne zamówienia wliczone),
  dlatego wymaga decyzji właściciela i wpisu w `odbior-mvp.md`.
- Dane próbne wpisane na produkcji przed startem usuń **przed** ustawieniem prawdziwej daty. Usunięcie
  zostaje w „Historii zmian” (invariant 5) — to oczekiwane. Lepsza droga: próby na odtworzonej bazie
  ([`kopie-i-odtworzenie.md`](kopie-i-odtworzenie.md) §6).
- Tymczasowa data z `wdrozenia.md` §1 (potrzebna, żeby API wystartowało przed startem) musi zostać
  zastąpiona prawdziwą przed udostępnieniem aplikacji użytkownikom.
