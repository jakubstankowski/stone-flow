# Implementation done — SF-18

## Summary

- `GET /api/change-history` (moduł Reporting, tylko odczyt):
  - okres zmiany w Europe/Warsaw z obsługą zmiany czasu (przez `WarsawTimeZone`);
  - opcjonalny filtr daty biznesowej i filtr rodzaju obiektu;
  - stronicowanie, sortowanie po czasie (przy równym czasie po `Id`);
  - znacznik `object.deleted`, niezależny od filtrów;
  - `occurredAt` w UTC z „Z”; `AuthorAccountId` nie wychodzi poza moduł.
- Migracja `20261109130643_AuditEntriesBusinessDateIndex`: tylko indeks `ObjectBusinessDate`, addytywna.
- Frontend `/historia-zmian`:
  - filtry, chipy i „Wyczyść filtry”;
  - tabela, a na telefonie karty (`app-data-table` z opcją `mobileCards`, domyślnie wyłączoną);
  - rozwijana tabela zmian Pole / Było / Jest z polskimi etykietami pól i sformatowanymi wartościami;
  - linki do obiektów, a przy usuniętym obiekcie znacznik „Usunięto”.
- Słowniki etykiet używane w kilku miejscach przeniesione do `app/src/app/shared/dictionaries/`, w tym `shop-status.ts`. Dzięki temu obszary nie importują się nawzajem.
- Specyfikacja: v1 PASS WITH ISSUES; 12 uwag wprowadzonych w v2.
- Przegląd kodu: PASS WITH ISSUES, bez uwag krytycznych. Poprawione uwagi 1–4:
  - `changedTo=9999-12-31` daje `400` przy polu zamiast `500`;
  - niepełny okres liczy się jako filtr, więc pusta lista mówi o filtrach;
  - domyślny okres przesuwa się po północy, a chip okresu nie pojawia się sam;
  - przyciski „Pokaż zmiany” mają nazwę dostępną z obiektem, operacją i czasem.

## Tests

| Test | Result |
|---|---|
| Reporting.Tests | 130/130 |
| Architecture.Tests | 56/56 |
| ChangeHistoryApiTests (LocalDB) | 22/22 |
| AuditLogTests, OperationsApiTests, EndpointSecurityTests, OpenApiTests (LocalDB) | 41/41 |
| Frontend: typecheck, `ng test` (74 pliki, 843 testy), lint, format, build | Passed |
| `dotnet build -c Release -m:1`, `dotnet format --verify-no-changes`, `has-pending-model-changes` (Reporting) | Passed |
| Pełny zestaw integracyjny, `test-migrate.sh` | Not run (CI) |

ChangeHistoryApiTests i testy regresji uruchomiono przed poprawkami po przeglądzie kodu. Poprawki zmieniają backend tylko w walidacji, a tę obejmują testy jednostkowe.

## Deviations

- Czas wyświetla się przez wspólny `formatDateTime` jako `dd.MM.yyyy, HH:mm`, z przecinkiem, a nie `dd.MM.yyyy HH:mm` jak w `02-spec.md` (plan P8).
- Błąd `400` z kluczem spoza filtrów (np. `page`) pokazuje się jako komunikat ogólny nad tabelą.

## Open Items

- **Human review required before merge** (ryzyko high).
- Gałąź wychodzi z `sf-17-owner-payouts`, a PR ma bazę `sf-17-owner-payouts`, więc PR #17 trzeba zmergować najpierw.
- Ręczne sprawdzenie w przeglądarce (scenariusze niżej) nie zostało wykonane.
- Przegląd kodu, uwaga 5 (kosmetyka testu powłoki i kolumny „Czas”): bez zmian.

## Scenariusze testów ręcznych (UI)

Warunki: zalogowany właściciel. Dzisiaj: 09.11.2026. Domyślny okres to 11.10.2026 – 09.11.2026.

| # | Kroki | Oczekiwany wynik |
|---|---|---|
| 1 | Menu **Historia zmian** | Lista ostatnich 30 dni, najnowsze na górze, bez chipów. Kolumny: Czas, Autor, Obiekt, Operacja, Zakres zmiany. Brak przycisków dodawania i edycji. |
| 2 | Wydatki: dodaj wydatek `Paliwo` 100 zł → wróć do historii | Na górze wpis: autor „Właściciel”, obiekt „Wydatek – …” z datą kosztu, operacja „Utworzenie”, w skrócie dwa pola i „i N więcej”. |
| 3 | Popraw ten wydatek: kwota brutto 120 zł → historia | Wpis „Zmiana” ze skrótem „Kwota brutto: 100,00 zł → 120,00 zł” (i netto, jeśli się zmieniło). |
| 4 | Kliknij **Pokaż zmiany** przy wpisie zmiany | Tabela Pole / Było / Jest z polskimi etykietami i kwotami w zł. Pole bez wartości pokazuje „Brak danych”. **Ukryj zmiany** zwija tabelę. |
| 5 | Kliknij obiekt we wpisie wydatku | Szczegóły tego wydatku. Po powrocie te same filtry i strona. |
| 6 | Usuń ten wydatek → historia | Wpis „Usunięcie”. Wszystkie trzy wpisy tego wydatku mają znacznik „Usunięto” zamiast linku. **Pokaż zmiany** przy usunięciu pokazuje tylko kolumnę „Było” z ostatnimi wartościami. |
| 7 | Zamówienia: dodaj wpłatę do zamówienia → historia | Wpis „Wpłata – …”. Kliknięcie prowadzi do szczegółów zamówienia. |
| 8 | Wpisy wydatku cyklicznego i wypłaty właściciela (z SF-16 i SF-17) | Kliknięcie prowadzi do `/wydatki/cykliczne/:id` i `/operacje/wyplaty/:id`. |
| 9 | Filtr **Rodzaj obiektu**: tylko „Wydatek” | Tylko wpisy wydatków, chip „Rodzaj: Wydatek”. Usunięcie chipa przywraca wszystkie. |
| 10 | Wydatek z datą kosztu 15.10.2026 popraw dziś; filtr **Data biznesowa** 01.10.2026 – 31.10.2026 | Widoczne wpisy obiektów z datą biznesową w październiku, także zmiany zrobione w listopadzie. Chip „Data biznesowa: 01.10.2026 – 31.10.2026”. Pod filtrem podpowiedź, czym jest data biznesowa. |
| 11 | Tylko „Data biznesowa od” 01.11.2026 | Chip „Data biznesowa: od 01.11.2026”, lista bez wpisów z datą biznesową sprzed 01.11. |
| 12 | Okres zmiany 01.01.2025 – 31.01.2025 | „Brak zmian dla wybranych filtrów.” z „Wyczyść filtry”. Kliknięcie wraca do ostatnich 30 dni. |
| 13 | Wyczyść pole „Zmiana od” | Błąd przy polu, lista bez zapytania, chip „Zmiana: … – 09.11.2026”. |
| 14 | „Zmiana od” późniejsza niż „Zmiana do”; potem „Data biznesowa od” późniejsza niż „do” | Błąd przy polu, brak zapytania. |
| 15 | Kliknij nagłówek **Czas** | Kolejność od najstarszych; ponowne kliknięcie od najnowszych. |
| 16 | Ponad 25 wpisów w okresie | Stronicowanie: strona 2 pokazuje kolejne wpisy, liczba wszystkich się zgadza. |
| 17 | Wpisy synchronizacji WooCommerce i zadania wydatków cyklicznych | Autor „Synchronizacja WooCommerce” i „Zadanie automatyczne”. |
| 18 | Widok telefonu (~390 px) | Karty zamiast tabeli, filtry w rozwijanym panelu, bez przewijania w poziomie. |
| 19 | Czytnik ekranu / Tab | Przycisk rozwinięcia ogłasza „Pokaż zmiany: {obiekt}, {operacja}, {czas}” i stan rozwinięcia. Po zwinięciu fokus zostaje na przycisku. |
