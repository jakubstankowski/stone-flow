# Task Specification — SF-17

## Source

[SF-17](https://standev.youtrack.cloud/issue/SF-17)

## Goal

Dodać wypłaty właścicieli jako nowy obiekt modułu Expenses (osoba, kwota, data, metoda; dodanie, poprawa, usunięcie z potwierdzeniem i audytem) oraz listę „Operacje” w module Reporting. Lista pokazuje operacje pieniężne od daty uruchomienia: wpłaty i zwroty (Orders), opłacone wydatki (Expenses) i wypłaty właścicieli (Expenses). Ma filtr okresu i rodzaju, sumy wpływów i wypływów dla filtra oraz przejście do źródła. Reporting czyta dane innych modułów wyłącznie przez jawne kontrakty w `StoneFlow.Shared.Application`.

## Context

- **Backend, moduł Expenses** (schemat `expenses`): nowa encja `OwnerWithdrawal` w `Expenses.Domain`. Przypadki użycia w `Expenses.Application` (folder na przypadek użycia, jak `CreateExpense/`). Persystencja w `Expenses.Infrastructure`, endpointy w `Expenses.Api`. Audyt przez `IAuditLog` z istniejącym typem `AuditObjectType.OwnerWithdrawal` (data biznesowa = data wypłaty, bez rodzica — conventions).
- **Backend, moduł Reporting** (schemat `reporting`): pierwszy przypadek użycia odczytu `ListOperations` w `Reporting.Application` i endpoint w `Reporting.Api` (`ReportingModule.MapReportingEndpoints`). Reporting nie mapuje tabel `orders` ani `expenses` (`DbContextSchemaTests`).
- **Kontrakt międzymodułowy** w `StoneFlow.Shared.Application/Operations/`:
  - interfejs źródła operacji pieniężnych (wiele implementacji: Orders i Expenses);
  - DTO operacji i słownik rodzajów.
  
  Implementacje są w `Orders.Infrastructure` i `Expenses.Infrastructure` (wzór: `IShopOrderLookup` / `ShopOrderLookup`).
- **Frontend:** `features/operations/` zastępuje zaślepkę listą i stronami wypłaty. Trasy pod `/operacje` (`loadChildren`, wzór `EXPENSES_ROUTES`).
- **Data uruchomienia:** `ILaunchDate` (`Shared.Application/LaunchDate`).

## Scope

### In scope

- Encja wypłaty właściciela, tabela `expenses.OwnerWithdrawals`, migracja.
- API wypłat:
  - lista podpowiedzi osób;
  - szczegóły;
  - dodanie z `Idempotency-Key`;
  - poprawa i usunięcie z wersją (`rowversion`);
  - audyt każdej zmiany w tej samej jednostce pracy.
- Kontrakt źródła operacji i jego implementacje:
  - Orders: wpłaty i zwroty;
  - Expenses: opłacone wydatki i wypłaty.
- Indeksy na kolumnach dat operacji pieniężnych: `orders.Payments.PaidOn`, `orders.Refunds.RefundedOn`, `expenses.OwnerWithdrawals.WithdrawnOn`. `expenses.Expenses.PaidOn` ma indeks z SF-15.
- `GET /api/operations`:
  - filtr okresu i rodzaju, sortowanie po dacie, stronicowanie;
  - reguły kierunku, efektywnego początku i sum w `Reporting.Domain` (`CashFlowPolicy`, `CashFlowTotals`), z testami jednostkowymi;
  - sumy wpływów, wypływów i zmiany środków dla całego filtra;
  - pomijanie operacji sprzed daty uruchomienia.
- UI:
  - lista „Operacje” z filtrami, sumami i przejściem do źródła;
  - „Dodaj wypłatę”, szczegóły wypłaty z poprawą i usunięciem (dialog potwierdzenia).
- Scenariusze testów ręcznych UI w `06-implementation-done.md`.

### Out of scope

- Karty przepływów pieniężnych, wykresy i widok „Przepływy pieniężne” (SF-20).
- Lista „Historia zmian” (SF-18). Audyt wypłat jest zapisywany, a jego widok powstaje w SF-18.
- Korekty sprzedaży na liście (PRD: nie są operacją pieniężną).
- Wpłaty właściciela i ujemne wypłaty (PRD §4: wpływy niezwiązane z zamówieniem są poza MVP).
- Słownik osób edytowany w aplikacji.
- Eksport listy.

## Behavior

### Wypłata właściciela

1. Na liście „Operacje” użytkownik wybiera **Dodaj wypłatę** i przechodzi do formularza `/operacje/wyplaty/nowa`.
2. Pola formularza:
   - **Osoba** — wymagana, tekst do 100 znaków, z podpowiedziami osób z wcześniejszych wypłat;
   - **Kwota (zł)** — wymagana, > 0 zł, najwyżej 2 miejsca po przecinku;
   - **Data wypłaty** — wymagana, nie późniejsza niż dziś w Europe/Warsaw, domyślnie dziś;
   - **Metoda** — wymagana: przelew, BLIK, gotówka.
3. Zapis wysyła `Idempotency-Key`; ponowienie tego samego żądania nie tworzy duplikatu. Po zapisie widok przechodzi do szczegółów wypłaty.
4. Szczegóły `/operacje/wyplaty/:id` pokazują pola, czas utworzenia i ostatniej zmiany oraz akcje:
   - **Edytuj** — ten sam formularz, zapis z wersją;
   - **Usuń** — dialog „Usunąć wypłatę?”, usunięcie z wersją i powrót do listy z komunikatem „Wypłata usunięta.”
5. Każde dodanie, zmiana i usunięcie zapisuje audyt:
   - autor = konto, obiekt `OwnerWithdrawal`, data biznesowa = data wypłaty;
   - etykieta „Wypłata właściciela” bez nazwy osoby;
   - pole `person` oznaczone jako dane osobowe.
   
   Zapis bez zmienionych pól nie tworzy wpisu audytu. Usunięcie zapisuje wszystkie stare wartości przed skasowaniem wiersza, w tej samej transakcji.
6. Wypłata jest operacją pieniężną: wypływem w okresie swojej daty. Nie wchodzi do kosztów ani sprzedaży i nie jest wydatkiem (nie pojawia się na liście wydatków).
7. Wypłatę z datą sprzed uruchomienia można zapisać. Nie pojawia się na liście „Operacje” (pkt 8) ani w raportach.

### Lista „Operacje”

8. Lista pokazuje operacje pieniężne z datą operacji w filtrze okresu i **nie wcześniejszą niż data uruchomienia**. Efektywny początek = max(`from`, data uruchomienia), wyznaczany jedną regułą domenową (`CashFlowPolicy.EffectiveFrom`) i przekazywany do źródeł, które filtrują w SQL od tej daty. Gdy filtr zaczyna się przed datą uruchomienia, pod filtrem widnieje informacja „Operacje liczone od {data uruchomienia} (data uruchomienia).” Gdy efektywny początek jest późniejszy niż `to` (okres w całości przed uruchomieniem), Reporting nie odpytuje źródeł i zwraca pustą listę z sumami 0,00 zł. Operacje sprzed tej daty są widoczne tylko w swoich źródłach (szczegóły zamówienia, wydatek, wypłata). Przykład URU-01: wpłata z dnia uruchomienia do zamówienia z datą sprzed uruchomienia jest na liście jako wpływ.
9. Rodzaje i źródła:

   | Rodzaj (kod) | Etykieta | Kierunek | Data operacji | Kwota | Opis | Przejście |
   |---|---|---|---|---|---|---|
   | `payment` | Wpłata | wpływ | data wpłaty | kwota wpłaty | numer zamówienia | `/zamowienia/{orderId}` |
   | `refund` | Zwrot sprzedaży / Zwrot pieniędzy | wypływ | data zwrotu | kwota zwrotu | numer zamówienia | `/zamowienia/{orderId}` |
   | `expense` | Opłacony wydatek | wypływ | data zapłaty | kwota brutto ze znakiem | opis wydatku i kategoria | `/wydatki/{id}` |
   | `withdrawal` | Wypłata właściciela | wypływ | data wypłaty | kwota | osoba | `/operacje/wyplaty/{id}` |

   - Lista pokazuje tylko wydatki z datą zapłaty. Opłacony wydatek ujemny (korekta lub zwrot od dostawcy) jest wypływem z kwotą ujemną.
   - Korekty sprzedaży, nieopłacone wydatki i zamówienia bez wpłat nie pojawiają się na liście.
   - Zwrot do zamówienia sprzed uruchomienia, z datą zwrotu od uruchomienia, jest na liście jako wypływ.
   - Zapłata wydatku od uruchomienia, z datą kosztu sprzed uruchomienia, jest na liście jako wypływ.
10. Filtry:
    - okres „Data od” / „Data do” — domyślnie bieżący miesiąc w Europe/Warsaw; oba wymagane, „od” ≤ „do”; bez górnego limitu długości okresu (uzgodnienie FIN-05 musi działać dla każdego okresu widoku przepływów, także własnego zakresu);
    - rodzaj — wielokrotny wybór czterech rodzajów; pusty oznacza wszystkie.
11. Sortowanie po dacie operacji, domyślnie malejąco, z możliwością rosnącego. Remis: rodzaj (stała kolejność `payment`, `refund`, `expense`, `withdrawal`), potem id. Stronicowanie: 25 na stronę, najwyżej 100.
12. Sumy dla całego filtra (nie tylko dla strony):
    - **Wpływy** — suma kwot operacji o kierunku „Wpływ” (wpłaty);
    - **Wypływy** — suma kwot operacji o kierunku „Wypływ” (zwroty, opłacone wydatki ze znakiem, wypłaty);
    - **Zmiana środków** — wpływy minus wypływy.

    Etykieta pod sumami: „Kwoty brutto w PLN według daty operacji pieniężnej.” Przy filtrze rodzaju sumy obejmują tylko wybrane rodzaje. Sumy to sumy zapisanych kwot `decimal(18,2)`, liczone w backendzie. Każda suma może być ujemna (np. filtr „Opłacony wydatek” z samymi korektami od dostawców): UI pokazuje ją wtedy ze znakiem minus, bez zamiany na wartość bezwzględną.
13. Wiersz pokazuje kolumny zgodnie z design guide §6 („Operacje”):
    - **Data**;
    - **Rodzaj** — „Wpłata”, „Zwrot sprzedaży” / „Zwrot pieniędzy”, „Opłacony wydatek”, „Wypłata właściciela”;
    - **Opis** — link do źródła;
    - **Kierunek** — „Wpływ” albo „Wypływ” (tekst, nie tylko kolor);
    - **Kwota** — kwota zapisana, bez zmiany znaku przez kierunek: wpłata 1000 zł → `1 000,00 zł`, zwrot 200 zł → `200,00 zł`, opłacony wydatek ujemny −50 zł → `−50,00 zł` z dopiskiem „Korekta / zwrot od dostawcy” (tak samo jak na liście wydatków);
    - **Metoda** — etykieta metody; gdy wydatek nie ma metody, „Nie podano”.
14. Stan filtra i strony przetrwa przejście do źródła i powrót (wzór `ExpensesService`).

## Data / API

### Storage

`ExpensesDbContext`, migracja addytywna:

- tabela `expenses.OwnerWithdrawals`:
  - `Id uniqueidentifier PK`;
  - `Person nvarchar(100) NOT NULL`;
  - `Amount decimal(18,2) NOT NULL` + `CHECK (Amount > 0)`;
  - `WithdrawnOn date NOT NULL`;
  - `Method nvarchar(16) NOT NULL` (`Transfer` | `Blik` | `Cash`) + `CHECK` na zbiór wartości;
  - `CreationRequestId uniqueidentifier NULL` z unikalnym filtrowanym indeksem;
  - `CreationRequestHash`;
  - `CreatedAtUtc`, `UpdatedAtUtc datetime2`;
  - `Version rowversion`;
- indeks `IX_OwnerWithdrawals_WithdrawnOn` (INCLUDE `Amount`).

`OrdersDbContext`, migracja addytywna:

- `IX_Payments_PaidOn` (INCLUDE `Amount`, `OrderId`);
- `IX_Refunds_RefundedOn` (INCLUDE `Amount`, `OrderId`).

Reporting: brak zmian schematu.

### Endpoints

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| GET | `/api/owner-withdrawals/people` | — | `200 { people: string[] }` (unikalne osoby z wypłat, alfabetycznie, maks. 50) | sesja | Expenses |
| GET | `/api/owner-withdrawals/{id}` | — | `200 { id, person, amount, withdrawnOn, method, createdAtUtc, updatedAtUtc, version }`; `404 owner_withdrawal_not_found` | sesja | Expenses |
| POST | `/api/owner-withdrawals` | nagłówek `Idempotency-Key` (UUID); body `{ person, amount, withdrawnOn, method }` | `201 { id, version }`; `200` (powtórzenie); `400` walidacja / `idempotency_key_required`; `409 idempotency_key_reused` z `ownerWithdrawalId` | sesja + CSRF | Expenses |
| PUT | `/api/owner-withdrawals/{id}` | body `{ person, amount, withdrawnOn, method, version }` | `200 { version }`; `400`; `404`; `409 owner_withdrawal_conflict` | sesja + CSRF | Expenses |
| DELETE | `/api/owner-withdrawals/{id}` | body `{ version }` | `204`; `400`; `404`; `409 owner_withdrawal_conflict` | sesja + CSRF | Expenses |
| GET | `/api/operations` | `from`, `to` (`yyyy-MM-dd`, wymagane), `kind` (wielokrotny), `direction` (`desc` \| `asc`), `page`, `pageSize` | `200 { items, page, pageSize, total, totals: { inflows, outflows, change }, effectiveFrom, launchDate }`; `400` walidacja parametrów; `422 operations_range_too_large` | sesja | Reporting |

Format danych:

- Kwoty w JSON są ciągami z kropką (`"1234.50"`), daty w formacie `yyyy-MM-dd`.
- Element `items`: `{ kind, id, date, direction: "inflow"|"outflow", amount, method?, refundKind?, description, category?, sourceType: "order"|"expense"|"withdrawal", sourceId, sourceLabel }`.
  - `amount` to kwota zapisana (dodatnia dla wpłat, zwrotów i wypłat; ze znakiem dla wydatku), bez zmiany znaku przez kierunek. Kierunek niesie wyłącznie `direction`.
  - `method` to kod metody albo `null` (wydatek bez metody).
  - `sourceLabel` (tekst linku): numer zamówienia dla wpłat i zwrotów (bez danych klienta), opis wydatku, „Wypłata właściciela” dla wypłat. `description`: numer zamówienia, opis wydatku albo osoba.

Walidacja:

- osoba: przycięta, niepusta, ≤ 100 znaków;
- kwota: > 0, ≤ 16 cyfr całkowitych, ≤ 2 miejsca po przecinku;
- data: ≤ dziś w Europe/Warsaw;
- metoda: ze słownika;
- `from`, `to`: poprawne daty, `from` ≤ `to` (inaczej `400` przy `from`); `kind` ze słownika; `pageSize` 1–100.

### Types crossing a boundary

W `StoneFlow.Shared.Kernel/Operations/` (słownik używany przez `Reporting.Domain` i kontrakt; Domain może zależeć tylko od Kernel):

- `CashOperationKind { Payment, Refund, PaidExpense, OwnerWithdrawal }`.

W `StoneFlow.Shared.Application/Operations/`:

- `CashOperation` (record):
  - `Kind`, `Id`, `Date` (DateOnly);
  - `Amount` (decimal, kwota zapisana: dodatnia dla wpłat, zwrotów i wypłat, ze znakiem dla wydatku);
  - `Method` (string?, kod; `null` dla wydatku bez metody);
  - `RefundKind` (string?), `Description` (string?), `Category` (string?);
  - `SourceId` (Guid: zamówienie albo obiekt), `SourceLabel` (string).
- `ICashOperationSource`:
  - `IReadOnlySet<CashOperationKind> Kinds`;
  - `Task<IReadOnlyList<CashOperation>> ListAsync(DateOnly from, DateOnly to, IReadOnlySet<CashOperationKind> kinds, int limit, CancellationToken)` — zwraca najwyżej `limit` wierszy (SQL `TOP`, porządek po dacie i id); `from` to już efektywny początek.
  
  Implementacje: Orders (Payment, Refund), Expenses (PaidExpense, OwnerWithdrawal). Są rejestrowane jako `IEnumerable<ICashOperationSource>` i wywoływane sekwencyjnie (wspólne `ModuleConnection`, bez `Task.WhenAll`). Każda implementacja jest projekcją w SQL z filtrem zakresu dat i rodzaju. Encje nie wychodzą poza Infrastructure.
- Reporting łączy wyniki, sortuje i stronicuje w pamięci. To odstępstwo od reguły „stronicowanie w bazie” (conventions): źródła leżą w różnych modułach i kontekstach. Skala z D-08 (około 20–30 zamówień miesięcznie) daje rząd setek operacji rocznie. Bezpiecznik w SQL: Reporting wywołuje każde źródło z `limit` = 10 001 minus liczba już wczytanych operacji; gdy łącznie wczytano więcej niż 10 000, endpoint odpowiada `422 operations_range_too_large` („Za dużo operacji w wybranym okresie. Wybierz krótszy okres.”) bez liczenia sum.
- W `Reporting.Domain/CashFlow/` reguły domenowe bez I/O, testowane jednostkowo:
  - `CashFlowPolicy.DirectionOf(kind)`: `Payment` → wpływ, pozostałe → wypływ;
  - `CashFlowPolicy.EffectiveFrom(from, launchDate)` = max(`from`, `launchDate`);
  - `CashFlowTotals.Of(movements)` → wpływy, wypływy, zmiana środków (suma zapisanych kwot według kierunku);
  - typ `CashMovement(kind, amount)`, na który handler mapuje `CashOperation`.
- Spójność z SF-20 (FIN-05): widok „Przepływy pieniężne” ma korzystać z tych samych `ICashOperationSource`, `CashFlowPolicy` i `CashFlowTotals`, dzięki czemu przy tym samym okresie karty zgadzają się z listą. SF-17 tworzy te typy, SF-20 je wykorzystuje.

## Invariant impact

| Invariant | How it is respected |
|---|---|
| 1. Sales separate from cash | Wypłata jest tylko wypływem; nie zmienia kosztów, sprzedaży ani listy wydatków. Lista pokazuje wyłącznie operacje pieniężne: bez nieopłaconych wydatków, korekt sprzedaży i wartości zamówień. |
| 2. Backend decimal money | Kwoty `decimal(18,2)`, sumy liczone w backendzie jako suma zapisanych kwot; frontend tylko formatuje ciągi. |
| 3. API-enforced access | Wszystkie nowe endpointy wymagają sesji; zapisy przechodzą globalną kontrolę CSRF; testy integracyjne odrzucają odczyt i zapis bez sesji. |
| 4. Retries never duplicate | Dodanie wypłaty z `Idempotency-Key` i unikalnym `CreationRequestId`; powtórzenie zwraca istniejący wpis. |
| 5. Financial audit trail | Dodanie, zmiana i usunięcie wypłaty zapisują audyt w tej samej transakcji; usunięcie zostawia pełny ślad starych wartości. |
| 6. Secrets stay server-side | Brak nowych sekretów. |
| 7. Module and layer boundaries | Reporting używa wyłącznie `ICashOperationSource` z `Shared.Application`; implementacje w Infrastructure modułów-właścicieli; brak referencji między modułami (testy architektury bez wyjątków). |
| 8. No identifying data in docs | Przykłady w dokumentach używają fikcyjnych osób; etykieta audytu i `sourceLabel` bez danych osobowych. |

## Risk

**high** — pieniądze: nowy rodzaj wypływu i lista uzgodnienia przepływów (FIN-05). Wymaga przeglądu przez człowieka przed merge.

## UI

**Trasy:**
- `/operacje` — lista;
- `/operacje/wyplaty/nowa` — dodanie;
- `/operacje/wyplaty/:id` — szczegóły, edycja w miejscu.

**Lista „Operacje”:**
- nagłówek „Operacje” z akcją główną **Dodaj wypłatę** (złoty brąz, design guide §6);
- filtry: „Data od”, „Data do”, „Rodzaj” (checkboxy); na telefonie rozwijany panel; aktywne filtry jako chipy (`DataTable` `filters`);
- pod filtrami informacja o dacie uruchomienia (pkt 8), gdy dotyczy;
- trzy sumy tekstowo („Wpływy”, „Wypływy”, „Zmiana środków”) z etykietą podstawy. To nie są karty KPI z SF-20, tylko wiersz podsumowania listy;
- `DataTable`: kolumny Data, Rodzaj, Opis (link do źródła), Kierunek, Kwota (wyrównana do prawej, `tabular-nums`), Metoda (design guide §6); sortowanie tylko po dacie;
- stany:
  - wczytywanie (`loading-state`);
  - pusty: „Brak operacji w wybranym okresie”; przy aktywnym filtrze rodzaju tytuł filtrowanego braku;
  - błąd z „Spróbuj ponownie”.

**Formularz wypłaty:**
- pola z pkt 2; osoba jako `input` z `datalist` podpowiedzi (`/people`), metoda jako `select` bez wartości domyślnej;
- błędy przy polach i podsumowanie błędów (wzór `ExpenseForm`);
- `409 owner_withdrawal_conflict` → komunikat „Wypłata została zmieniona w międzyczasie. Wczytaj aktualne dane.”

**Szczegóły wypłaty:**
- stan wczytywania (`loading-state`) i błąd odczytu z „Spróbuj ponownie”; formularz blokuje przyciski w trakcie zapisu;
- etykiety i wartości, akcje „Edytuj” i „Usuń”, `confirm-dialog`;
- `404` → „Nie znaleziono wypłaty. Mogła zostać usunięta.” z powrotem do listy.

**Dostępność i formaty:**
- etykiety pól, `aria-live` dla podsumowań, fokus na pierwszym błędzie, klawiatura;
- formaty: `formatPln`, `formatDate`;
- kierunek jako tekst w osobnej kolumnie, więc nie zależy od koloru; kwoty ujemne ze znakiem `−` (U+2212).

## Edge Cases

- Okres całkowicie przed datą uruchomienia (efektywny początek > `to`): pusta lista bez odpytywania źródeł, sumy `0,00 zł`, informacja o dacie uruchomienia.
- `from` > `to`: `400` przy `from`.
- Więcej niż 10 000 operacji w okresie: `422 operations_range_too_large`, UI pokazuje komunikat przy filtrze okresu.
- Wpłata z dnia uruchomienia do zamówienia z datą sprzed uruchomienia: na liście jako wpływ (URU-01); wpłata z dnia przed uruchomieniem: nie ma jej.
- Filtr tylko „Opłacony wydatek” z samymi korektami od dostawców: suma wypływów ujemna, zmiana środków dodatnia.
- Opłacony wydatek bez metody: kolumna Metoda „Nie podano”.
- Wydatek opłacony, potem „Cofnij oznaczenie zapłaty”: znika z listy.
- Wydatek ujemny opłacony: wypływ z kwotą ujemną; obniża sumę wypływów.
- Zwrot pieniędzy i zwrot sprzedaży: oba wypływy; rodzaj zwrotu widoczny w kolumnie „Rodzaj”.
- Zamówienie anulowane z wpłatami: wpłaty dalej na liście (rzeczywiste pieniądze).
- Usunięcie wpłaty, zwrotu, wydatku albo wypłaty: znika z listy przy kolejnym odczycie.
- Równoległa edycja wypłaty: druga zapisana wersja → `409`.
- Powtórzone dodanie z tym samym kluczem i treścią → `200` z tym samym `id`; inna treść → `409 idempotency_key_reused`.
- Data wypłaty jutro (Warsaw) → `400`; data z dnia uruchomienia → na liście.
- Osoba z białymi znakami → przycięta; podpowiedzi bez duplikatów (porównanie bez rozróżniania wielkości liter, zwracany ostatnio użyty zapis).

## Dependencies

- SF-9 (wpłaty i zwroty) — w main.
- SF-15 (wydatki i data zapłaty) — w main.
- SF-7 (audyt, `AuditObjectType.OwnerWithdrawal`) — w main.
- SF-16 (wydatki cykliczne) nie jest wymagane: wystąpienia są zwykłymi wydatkami. Po merge obu PR lista obejmie je bez zmian.

## Assumptions

- **Pytanie 1 (odpowiedź właściciela 09.11.2026):** operacje z datą sprzed uruchomienia są ukryte na liście, a lista pokazuje tylko operacje od daty uruchomienia. Pod filtrem jest informacja, od kiedy liczone są operacje.
- **Pytanie 2 (odpowiedź 09.11.2026):** osoba to tekst z podpowiedziami z wcześniejszych wypłat, bez słownika.
- **Pytanie 3 (odpowiedź 09.11.2026):** kwota > 0 zł, data ≤ dziś, metody jak przy zwrotach (przelew, BLIK, gotówka).
- **Pytanie 4 (odpowiedź 09.11.2026):** podpowiedź netto ÷ 1,23 zostaje. PRD §8 poprawiono na gałęzi PR #16 (poza zakresem SF-17).
- Wypłata nie ma komentarza ani referencji dokumentu, bo PRD wymienia tylko osobę, kwotę, datę i metodę.
- Wypłaty dodaje się z listy „Operacje” (PRD §5). Nie ma osobnej pozycji nawigacji.
- Sumy listy są w zakresie SF-17 jako wiersz podsumowania potrzebny do uzgodnienia z widokiem przepływów (FIN-05). Karty i wykresy należą do SF-20.
- Domyślny okres to bieżący miesiąc, bez górnego limitu długości (własny zakres widoku przepływów musi dać się uzgodnić z listą).
- Łączenie źródeł w pamięci Reporting jest uzasadnione skalą (D-08); bezpiecznik 10 000 operacji jest egzekwowany limitem w SQL.
- Prezentacja kwot (v2, po przeglądzie): kolumna „Kierunek” i kwota zapisana bez zmiany znaku, zgodnie z design guide §6 (listy „Operacje” i formularz wydatku).
- `CashOperationKind` leży w `Shared.Kernel`, bo używa go `Reporting.Domain`.
- Metoda wpłaty „Pobranie” jest pokazywana na liście jako metoda wpłaty. Wypłata nie ma tej metody.
- Wypłatę można zapisać z datą sprzed uruchomienia, analogicznie do wpłat i wydatków (URU-01).
- Ryzyko pozostaje **high**.

## Acceptance Criteria

- [ ] Wypłata 2000 zł z dzisiejszą datą jest na liście „Operacje” z kierunkiem „Wypływ” i kwotą `2 000,00 zł` oraz zwiększa sumę wypływów o 2000 zł. Nie ma jej na liście wydatków, a koszty i sprzedaż (reguły SF-19) się nie zmieniają.
- [ ] Poprawa wypłaty zapisuje audyt ze zmienionymi polami. Usunięcie wymaga potwierdzenia w UI, zapisuje audyt ze starymi wartościami i usuwa wypłatę z listy.
- [ ] Korekta sprzedaży nie pojawia się na liście; wpłaty, zwroty obu rodzajów, opłacone wydatki i wypłaty się pojawiają.
- [ ] Opłacony wydatek −50 zł jest na liście z kierunkiem „Wypływ” i kwotą `−50,00 zł` (dopisek „Korekta / zwrot od dostawcy”) i obniża sumę wypływów o 50 zł.
- [ ] Przy okresie obejmującym datę uruchomienia lista i sumy nie obejmują operacji z datą sprzed uruchomienia, a UI pokazuje efektywny początek; wpłata z dnia uruchomienia do zamówienia sprzed uruchomienia jest na liście.
- [ ] Nieopłacony wydatek nie jest na liście; po „Oznacz jako opłacony” pojawia się w okresie daty zapłaty.
- [ ] Suma wpływów minus wypływów równa się „Zmianie środków”, a sumy nie zależą od strony ani sortowania.
- [ ] Ponowienie dodania wypłaty z tym samym `Idempotency-Key` nie tworzy duplikatu.
- [ ] Endpointy odrzucają żądania bez sesji (`401`) i zapisy bez tokenu CSRF.
- [ ] Testy architektury przechodzą bez nowych wyjątków.

## Verification

- build: `dotnet build stone-flow.slnx -c Release -m:1`; `dotnet format --verify-no-changes`.
- tests:
  - testy domeny wypłaty (walidacja, audyt);
  - testy jednostkowe `CashFlowPolicy` i `CashFlowTotals` (kierunek, efektywny początek, sumy z ujemnym wydatkiem) oraz łączenia, sortowania i stronicowania w `ListOperations`;
  - testy integracyjne (LocalDB): API wypłat (idempotencja, wersja, audyt, 401/CSRF), `/api/operations` z danymi z Orders i Expenses (rodzaje, okres, data uruchomienia z wpłatą z dnia uruchomienia do zamówienia sprzed uruchomienia, wydatek ujemny, wydatek bez metody, korekta pominięta, sumy, stronicowanie, bezpiecznik `422`);
  - `Architecture.Tests`;
  - frontend `npm run typecheck`, `npm test`, `npm run lint`, `npm run format:check`, `npm run build`.
- manual: scenariusze testów ręcznych UI w `06-implementation-done.md`.
