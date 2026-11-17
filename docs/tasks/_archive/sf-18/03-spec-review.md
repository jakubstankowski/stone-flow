# Spec review — SF-18

## Summary

- Overall assessment: PASS WITH ISSUES
- Justification: Specyfikacja pokrywa wszystkie wymagania zadania i AUD-01: listę tylko do odczytu,
  filtry okresu zmiany i rodzaju obiektu, przejście do obiektu, ślad usunięcia i czasy w
  Europe/Warsaw. Otwarte pytanie z zadania (filtr daty biznesowej) zostało rozstrzygnięte. Nazwy
  pól w katalogu zgadzają się z kodem audytu (`OrderField`, `EntryField`, `ExpenseField`,
  `RecurringDefinitionField`, `OwnerWithdrawalField`), a trasy przejść istnieją w
  `*.routes.ts`. Nie ma naruszeń inwariantów ani problemów krytycznych. Kilka niedoprecyzowań
  (semantyka filtra daty biznesowej, format list, „—” zamiast „Brak danych”, źródło etykiet
  słowników) trzeba poprawić w specyfikacji albo rozstrzygnąć w planie.

## Findings

### Critical Issues

None.

### Non-Critical Issues

1. **(Ważne) Filtr daty biznesowej nie znajduje zmian, które przesunęły obiekt poza okres.**
   Sekcje „Behavior” pkt 3, „Edge Cases”, „Assumptions” pkt 1. Wpis `Updated` zapisuje *nową* datę
   biznesową (`ExpenseAudit.Updated`: „data biznesowa to nowa data kosztu”; tak samo w
   `PaymentAudit`, `RecurringExpenseAudit` i `OwnerWithdrawalAudit`). Zmiana daty kosztu z
   15.10 na 03.11 ma więc `ObjectBusinessDate = 2026-11-03`. Przy filtrze „data biznesowa:
   październik” ta zmiana nie pojawi się na liście, choć to ona zmieniła raport za październik.
   Data biznesowa wydatku to też data kosztu, a przepływy liczą się według `paidOn` (PRD sekcja
   8), więc zmiana daty zapłaty nie trafia do filtra według okresu przepływów. AUD-01 nadal jest
   spełnione, bo filtr okresu zmiany znajdzie wpis, a filtr daty biznesowej jest opcjonalny.
   Brakuje jednak zapisu tego ograniczenia. **Poprawka:** dodać przypadek brzegowy i założenie:
   „filtr daty biznesowej porównuje datę biznesową z chwili wpisu (po zmianie); zmiana, która
   przeniosła obiekt z okresu, ma datę nowego okresu”. Dodać też krótką podpowiedź przy filtrze
   w UI. Szersze dopasowanie, np. po starej wartości pola daty, to nowe wymaganie i decyzja
   właściciela, nie poprawka.

2. **Pola-listy zamówienia nie są „tekstem”.** Sekcja „Data / API”, katalog pól. `OrderAudit.Format`
   zapisuje `IReadOnlyList<string>` (`suppliers`, `zoReferences`) jako tablicę JSON
   (`["A","B"]`). Katalog oznacza je jako „tekst”, więc UI pokaże surowy JSON. **Poprawka:** dodać
   format „lista”: odczyt tablicy JSON i złączenie przecinkiem, a dla nieczytelnej wartości
   pokazanie jej bez zmian. Dodać też test katalogu.

3. **„—” zamiast „Brak danych”.** Sekcja „Data / API”, „Format wyświetlania”. DESIGN-GUID (sekcja
   formatów) i konwencje frontendu każą pokazywać brak wartości jako „Brak danych”
   (`NO_DATA`, które zwraca też `formatDate(null)`). **Poprawka:** użyć „Brak danych”. Jeśli
   „—” ma zostać, bo pusta komórka „Było/Jest” to co innego niż brak danych, zapisać to jako
   świadome odstępstwo w „Assumptions”.

4. **Nie wiadomo, skąd brać etykiety słowników.** Sekcja „Data / API”, „Format wyświetlania”
   („istniejące etykiety z modeli funkcji”) i „Assumptions” pkt 6. Dziś żadna funkcja nie
   importuje z innej (`features/operations/operation-models.ts` ma własne `CATEGORY_LABELS`,
   a nie importuje z `features/expenses`). **Poprawka:** wskazać jedną z dróg: import z
   `features/orders/order-models.ts`, `features/expenses/expense-models.ts` i
   `features/operations/withdrawal-models.ts` (nowy wzorzec między funkcjami) albo przeniesienie
   wspólnych słowników do `shared/`. Mapy lokalne w `audit-field-catalog.ts` nie powinny
   powielać tej samej wiedzy po raz trzeci.

5. **Słowniki zależne od rodzaju obiektu.** Katalog pól, wiersz „Wpłata / zwrot / korekta”. Pole
   `method` ma inne zbiory kodów dla wpłaty (`PAYMENT_METHOD_LABELS`, z `cashOnDelivery`) i dla
   zwrotu (`REFUND_METHOD_LABELS`). Pole `kind` występuje tylko przy zwrocie (`salesReturn`,
   `moneyReturn`). `shopStatus` to surowy status WooCommerce, który ma etykiety w
   `features/orders/shop-status.ts`, a nie w modelu funkcji. **Poprawka:** dla każdego pola
   słownikowego wskazać słownik według rodzaju obiektu.

6. **Wydatek cykliczny nie jest „jak wydatek”.** Katalog pól. `RecurringDefinitionField` nie ma
   pól `costDate`, `dueDate`, `paidOn` ani `documentReference`. **Poprawka:** wypisać pola
   definicji wprost: `description`, `category`, `counterparty`, `grossAmount`, `netAmount`,
   `paymentMethod`, `payer`, `comment`, `dayOfMonth`, `startsOn`, `endsOn`, `pausedOn`.

7. **Warunki testu `405`.** Sekcje „Endpoints” i „Verification”. Bez sesji metoda inna niż GET daje
   `401` (polityka fallback). Z sesją, ale bez `X-XSRF-TOKEN`, `CsrfValidationMiddleware` odrzuci
   POST/PUT/PATCH/DELETE przed `405`. **Poprawka:** zapisać, że `405` jest sprawdzane z sesją i
   tokenem CSRF, jak `OrdersApiTests.Delete_is_not_possible`. Warto dodać, że liczba wierszy
   `reporting.AuditEntries` się nie zmienia.

8. **Pusty wynik bez rozróżnienia.** Sekcja „UI”, „Stany”. DESIGN-GUID (stany) wymaga odróżnienia
   pustej ewidencji od pustego wyniku filtrowania. Spec ma jeden komunikat „Brak zmian w wybranym
   okresie.”. **Poprawka:** dodać wariant przy aktywnym filtrze daty biznesowej lub rodzaju, np.
   „Brak zmian dla wybranych filtrów.” z „Wyczyść filtry”.

9. **Niespójna nazwa znacznika usunięcia.** „Scope” i „Behavior” używają `objectDeleted`, a „Types
   crossing a boundary” i „Acceptance Criteria” używają `object.deleted` / `deleted`.
   **Poprawka:** ujednolicić nazwę do pola kontraktu `object.deleted`.

10. **Szczegóły czasu po stronie backendu.** Sekcja „Behavior” pkt 3. Nie podano, gdzie żyje
    zamiana Warsaw → UTC. W kodzie jest już `StoneFlow.Shared.Application/Scheduling/WarsawTimeZone.cs`
    oraz lokalne `WarsawTime` w Orders i Expenses. `OccurredAtUtc` (`datetime2`) wraca z EF jako
    `DateTimeKind.Unspecified`, a kontrakt wymaga ISO z „Z”. **Poprawka:** wskazać ponowne użycie
    istniejącej strefy i wymagać jawnego UTC przy mapowaniu `occurredAt`, z testem formatu.

11. **Brak polskich nazw rodzajów obiektu.** Sekcje „UI” (filtr „Rodzaj obiektu”, 7 pól wyboru) i
    „Behavior” pkt 5 (kolumna „Obiekt”). Nie ma listy etykiet. **Poprawka:** dodać tabelę
    `objectType` → etykieta: Zamówienie, Wpłata, Zwrot, Korekta sprzedaży, Wydatek, Wydatek
    cykliczny, Wypłata właściciela. Wskazać też, czy kolumna pokazuje rodzaj obok etykiety z
    audytu, która już go zawiera („Zwrot do ZR-…”, „Wydatek – Cięcie”).

12. **Ukryte założenie o zamówieniach.** Sekcja „Behavior” pkt 7 i „Assumptions” pkt 8. Link
    wpłaty, zwrotu i korekty do zamówienia nadrzędnego zakłada, że zamówienie zawsze istnieje.
    To prawda (PRD sekcja 5: „Zamówień nie usuwa się”; brak `Deleted` w `OrderAudit`), ale spec
    tego nie mówi. **Poprawka:** dopisać założenie z odwołaniem do PRD.

### Unclear or Ambiguous Sections

- **Behavior**, pkt 5, „Zakres zmiany”: „pierwsze dwa pola” to kolejność z `Changes`, czyli
  kolejność `Snapshot()` modułu. Warto to zapisać, bo od tego zależą testy.
- **UI**, „Tokeny”: „znaczniki operacji `badge`”. Konwencje wymagają `app-status-badge` z tonem
  (`neutral`, `info`, `warning`). Lepiej nazwać komponent wprost.
- **UI**, „Stany”, „błąd walidacji: komunikat przy polu, bez zapytania”: nie podano, czy błąd
  `400` z API (np. nieznany `objectType`) też trafia do pola. Wzorzec z „Operacji”
  (`toApiError` → `validation`) wystarczy, ale trzeba go wskazać.
- **Context**: `Reporting.Domain` — `AuditRecord`. Nie wiadomo, czy domena dostaje nową regułę,
  np. zamianę okresu na przedział UTC, czy zostaje bez zmian, a reguła trafia do Application.
  Testy w „Verification” zakładają testy domeny lub aplikacji.

### Invented or Unsupported Requirements

None. Filtr daty biznesowej odpowiada na pytanie z zadania („Do rozstrzygnięcia”). Indeks
`ObjectBusinessDate`, domyślny okres 30 dni i `405` wynikają z odpowiedzi w „Assumptions” albo z
wymagania „tylko do odczytu, także przez API”.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Data / API — Format wyświetlania („brak wartości: „—””) | DESIGN-GUID, formaty („brak wartości jako „Brak danych””); conventions, Frontend/Components | Inny tekst braku wartości, bez uzasadnienia |
| UI — Stany (pusto) | DESIGN-GUID, stany („Odróżnić pustą ewidencję od pustego wyniku filtrowania”) | Jeden komunikat dla obu przypadków |
| Data / API — Katalog pól (`suppliers`, `zoReferences` jako „tekst”) | Kod: `OrderAudit.Format` (lista → tablica JSON); conventions „Value format” | Format wartości w katalogu nie zgadza się z formatem zapisu |
| Data / API — Katalog pól (wydatek cykliczny „jak wydatek”) | Kod: `RecurringDefinitionField` | Katalog wymienia pola, których definicja nie ma |

Brak konfliktu z decyzjami D-05 (jedno konto → „Właściciel”), D-06, D-09 (anonimizacja poza
zakresem) ani z architekturą z PRD sekcja 10 („Historia zmian” w module raportowania).

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | Not applicable | Lista nie liczy sprzedaży ani przepływów. |
| 2. Backend decimal money | OK | Brak obliczeń na kwotach; frontend tylko formatuje tekst kwoty z audytu przez `formatPln`. |
| 3. API-enforced access | OK | Endpoint pod polityką domyślną (sesja); `401` w testach. Jedno konto (D-05), więc nie trzeba autoryzować osobnych zasobów. |
| 4. Retries never duplicate | Not applicable | Wyłącznie odczyt. |
| 5. Financial audit trail | OK | Tylko `GET`, inne metody `405`; zapis audytu bez zmian; usunięty obiekt ma znacznik i wpis usunięcia z ostatnimi wartościami. |
| 6. Secrets stay server-side | OK | Odpowiedź nie zawiera sekretów ani `AuthorAccountId` (tylko `author.kind`). |
| 7. Module and layer boundaries | OK | Reporting czyta tylko `reporting.AuditEntries` przez port w Application, a implementację ma w Infrastructure; nie odwołuje się do tabel Orders ani Expenses. |
| 8. No identifying data in docs | OK | Spec nie zawiera danych sklepu ani klientów; scenariusze mają używać danych fikcyjnych. |

## Risk

- Recorded: high
- Assessed: high. Lista pokazuje kwoty i dane osobowe klientów z audytu (`isPersonalData`) i jest
  jedynym narzędziem wyjaśniania zmian raportów (AUD-01, warunek odbioru z PRD sekcja 11).
  Przed merge potrzebny jest przegląd człowieka.

## Assumptions Detected

| Założenie | Zapisane w spec? |
|---|---|
| Filtr daty biznesowej opcjonalny, domyślnie pusty | Tak (1) |
| Domyślny okres to 30 dni do dziś w Europe/Warsaw | Tak (2) |
| Wszystkie wpisy usuniętego obiektu mają znacznik i nie mają linku | Tak (3) |
| Autor konta to stała etykieta „Właściciel” | Tak (4) |
| Okres zmiany bez limitu długości | Tak (5) |
| Etykiety i formatowanie we frontendzie | Tak (6), ale bez wskazania źródła słowników (problem 4) |
| Dane osobowe widoczne dla właściciela | Tak (7) |
| Wpłata, zwrot i korekta prowadzą do zamówienia | Tak (8) |
| Bez przycinania do daty uruchomienia | Tak (9) |
| Data biznesowa wpisu to wartość po zmianie (filtr nie łapie przesunięć z okresu) | **Nie** (problem 1) |
| Zamówień nie usuwa się, więc link do rodzica jest zawsze ważny | **Nie** (problem 12) |
| Wartości list są tablicą JSON | **Nie** (problem 2) |
| Kolejność pól w skrócie = kolejność `Changes` z modułu | **Nie** (Unclear) |
| `405` sprawdzane z sesją i tokenem CSRF | **Nie** (problem 7) |

## Recommendation

- Proceed as-is, poprawiając problemy 1–12 w specyfikacji albo zapisując je jako rozstrzygnięcia
  na początku planu. Problemy 1–3 trzeba rozstrzygnąć przed implementacją frontendu.
