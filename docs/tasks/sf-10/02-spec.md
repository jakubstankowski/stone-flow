# Task Specification — SF-10

## Source

[SF-10](https://standev.youtrack.cloud/issue/SF-10)

## Goal

Moduł Orders dostaje korektę sprzedaży: wpis z kwotą, datą i powodem, bez metody, który nie
jest operacją pieniężną. Korektę można dodać, poprawić i usunąć z potwierdzeniem; każda zmiana
zostawia audyt w tej samej transakcji. Backend egzekwuje limit obniżeń sprzedaży: suma zwrotów
sprzedaży i korekt sprzedaży zamówienia nie może przekroczyć jego wartości. Limit obowiązuje:

- przy dodaniu i poprawie korekty;
- przy dodaniu i poprawie zwrotu sprzedaży, także przy zmianie rodzaju zwrotu na „Zwrot
  sprzedaży”;
- przy ręcznej korekcie wartości zamówienia (SF-8).

Gdy obniżenia przekraczają wartość, czyli po obniżeniu wartości przez synchronizację sklepu,
szczegóły zamówienia pokazują ostrzeżenie „Obniżenia sprzedaży przekraczają wartość o X zł”.
Korekta nie zmienia Zapłacono, stanu płatności, wartości zamówienia ani przepływów. Jest
pokazana osobno od wpłat i zwrotów.

## Context

- **Backend, moduł Orders** (wzorce SF-9):
  - **Domain** `Orders.Domain/Payments/`:
    - encja `SalesCorrection` (dziedziczy `OrderEntry`, bez metody);
    - `SalesReductionRules`: limit, czyli „suma obniżeń po zmianie ≤ wartość”;
    - wyliczenie nadwyżki obniżeń.
  - **Application:** foldery `AddSalesCorrection/`, `EditSalesCorrection/`,
    `DeleteSalesCorrection/` przez wspólny `OrderEntryWriter<T>`. Rozszerzone:
    - `AddRefund` i `EditRefund` (limit dla `salesReturn`);
    - `EditOrder` (blokada zamówienia i limit przy zmianie wartości);
    - `GetOrder` (korekty i podsumowanie obniżeń).
  - **Infrastructure:**
    - tabela `orders.SalesCorrections` i migracja;
    - `OrderLock` zwraca dodatkowo sumę zwrotów sprzedaży i sumę korekt;
    - konfiguracja i store na bazie `OrderEntryStore<T>`.
  - **Api:** trasy `/api/orders/{orderId}/sales-corrections`.
- **Audyt:** `AuditObjectType.SalesCorrection` z rodzicem `Order` (dozwolone w SF-7).
- **Frontend:** w `features/orders/`:
  - sekcja „Korekty sprzedaży” w szczegółach, osobna od wpłat i zwrotów, z formularzem
    i usuwaniem;
  - ostrzeżenie o nadwyżce obniżeń;
  - opisy w formularzach (korekta wartości, zwrot, korekta sprzedaży);
  - obsługa nowego błędu limitu w formularzach zwrotu i zamówienia.
- **Blokada:** każdy zapis korekty, zwrotu i zmiany wartości zamówienia zaczyna się od
  `IOrderLock`, zgodnie z zapisem z SF-9.

## Scope

### In scope

- **Korekta sprzedaży:**
  - pola: kwota (> 0, `decimal(18,2)`), data (nie z przyszłości według Europe/Warsaw; wcześniejsza
    niż data zamówienia dozwolona), powód (wymagany, 1–500 znaków);
  - bez metody;
  - dla zamówień ręcznych i ze sklepu, także anulowanych.
- **Poprawa i usunięcie korekty** z potwierdzeniem, kontrolą `rowversion` i audytem. Usunięcie
  jest fizyczne, a audyt `Deleted` zachowuje wszystkie pola.
- **Dodanie korekty** z kluczem idempotencji (wzorzec SF-9: skrót z `orderId`).
- **Limit obniżeń** `R = Σ zwrotów sprzedaży + Σ korekt ≤ V` (wartość zamówienia),
  sprawdzany pod blokadą zamówienia w każdej zmianie, która podnosi `R` albo obniża `V`:
  - dodanie korekty i poprawa jej kwoty w górę;
  - dodanie zwrotu sprzedaży i poprawa jego kwoty w górę;
  - poprawa zwrotu z `moneyReturn` na `salesReturn`;
  - ręczne obniżenie wartości zamówienia ręcznego.

  Odrzucenie zwraca `422 sales_reduction_limit_exceeded` z dostępną kwotą. Limit obowiązuje
  także dla zamówień anulowanych.
- **Ostrzeżenie** „Obniżenia sprzedaży przekraczają wartość o X zł”: `X = R − V` przy `R > V`,
  wyliczane w backendzie przy odczycie.
- **Opisy w formularzach**, które wyjaśniają różnicę między korektą wartości, zwrotem
  i korektą sprzedaży.

### Out of scope

- Obniżenie sprzedaży w raportach i wyjątki raportowe (anulowane, pending, failed, szkice,
  zamówienia sprzed uruchomienia): SF-19. Tutaj zapisujemy wpisy i pilnujemy limitu.
- Obniżanie wartości przez synchronizację: SF-12. Ostrzeżenie jest wyliczane ze stanu, więc
  zadziała, gdy SF-12 zmieni wartość. Test sieje taki stan w SQL.
- Lista „Operacje”: korekty się na niej nie pojawiają (SF-17).

## Behavior

### Dodanie korekty

1. W sekcji „Korekty sprzedaży” użytkownik wybiera „Dodaj korektę sprzedaży”. Formularz ma
   opis: „Korekta służy do oddania towaru bez zwrotu pieniędzy: obniża sprzedaż, nie tworzy
   wypływu i nie zmienia kwoty Zapłacono.” Pola to kwota, data (domyślnie dzisiaj) i powód.
2. API po walidacji (`400`) wykonuje kroki w jednostce pracy:
   1. `IOrderLock` (`404 order_not_found`);
   2. klucz idempotencji: `200` przy powtórzeniu, `409 idempotency_key_reused` z
      `salesCorrectionId`;
   3. limit: `R + kwota ≤ V`, w przeciwnym razie `422 sales_reduction_limit_exceeded` z
      `available = max(V − R, 0)`;
   4. zapis i audyt `Created` (SalesCorrection, rodzic Order).
3. UI odświeża szczegóły. Zapłacono i stan płatności się nie zmieniają.

### Poprawa i usunięcie korekty

Kolejność jak w SF-9: walidacja, blokada, wpis należący do zamówienia (`404 entry_not_found`),
`version` (`409 entry_conflict`), reguła, brak zmian, zapis i audyt.

- **Reguła poprawy:** `R − stara + nowa ≤ V`; przy przekroczeniu `422` z
  `available = max(V − (R − stara), 0)`, czyli maksymalną kwotą tej korekty. Obniżenie kwoty
  korekty zawsze przechodzi, nawet gdy `R > V` (np. po zmianie przez sklep), bo zmniejsza
  nadwyżkę.
- **Usunięcie** zawsze jest dozwolone, bo zmniejsza `R`. Wymaga potwierdzenia w dialogu
  z nazwą wpisu i skutkiem.

### Limit przy zwrotach (zmiana SF-9)

- **Dodanie zwrotu `salesReturn`:** po sprawdzeniu „zwrot ≤ Zapłacono” (SF-9) sprawdzany jest
  limit `R + kwota ≤ V`.
  - Kolejność: najpierw `refund_exceeds_paid`, potem `sales_reduction_limit_exceeded`.
  - Przykład z PLAT-05: zwrot sprzedaży 1200 przy wartości 1000 i wpłatach 1200 daje `422
    sales_reduction_limit_exceeded` z `available = 1000.00`.
- **Poprawa zwrotu:** `R` po zmianie liczy nowy wkład zwrotu (`kwota`, gdy nowy rodzaj to
  `salesReturn`, inaczej 0), a odejmuje stary wkład.
  - Gdy `R` po zmianie > `V` i zmiana podnosi `R`, odpowiedź to `422` z
    `available = max(V − (R − stary wkład), 0)`.
  - Zmiana, która nie podnosi `R` (np. `salesReturn` → `moneyReturn`, obniżenie kwoty), nie
    jest blokowana przez limit.
- **Zwroty pieniędzy (`moneyReturn`)** nie wchodzą do `R`.

### Limit przy ręcznej korekcie wartości (zmiana SF-8)

- `EditOrder` w zamówieniu ręcznym, gdy zmienia się `value`, bierze `IOrderLock` przed zapisem,
  w tej samej jednostce pracy.
- Gdy nowa wartość `V' < R`, odpowiedź to `422 sales_reduction_limit_exceeded` z
  `minimumValue = R`; zamówienie się nie zmienia.
- Kolejność sprawdzeń z SF-8 zostaje: `400` → `404` → `409 order_conflict` → pola sklepu i
  status (`422`) → limit wartości (`422`) → brak zmian → zapis.
- Podwyższenie wartości i edycja bez zmiany wartości nie sprawdzają limitu.
- W zamówieniu ze sklepu wartość jest tylko do odczytu (SF-8), więc limit nie dotyczy edycji
  użytkownika.

### Ostrzeżenie o nadwyżce

- `GET /api/orders/{id}` zwraca `salesReductions { returnsTotal, correctionsTotal, total,
  excess }`, gdzie `excess = R − V` przy `R > V`, inaczej `null`.
- UI pokazuje alert z tekstem „Obniżenia sprzedaży przekraczają wartość o X zł. Popraw zwroty
  sprzedaży lub korekty.” dla każdego zamówienia, w którym `excess` nie jest `null`.

## Data / API

### Storage

Migracja `SalesCorrections`.

- **`orders.SalesCorrections`:**
  - `Id`;
  - `OrderId` FK do `orders.Orders` z `NO ACTION`;
  - `Amount decimal(18,2)` z `CHECK (Amount > 0)`;
  - `CorrectedOn date`;
  - `Reason nvarchar(500)` NOT NULL;
  - `CreationRequestId` z unikalnym filtrowanym indeksem i `CreationRequestHash binary(32)`;
  - `CreatedAtUtc`, `UpdatedAtUtc`, `Version rowversion`;
  - indeks `(OrderId) INCLUDE (Amount)`.
- **`OrderLock`** zwraca `SalesReturnsTotal` (suma zwrotów z `Kind = SalesReturn`)
  i `CorrectionsTotal`. Obie sumy liczone są po blokadzie, z `COALESCE`.

### Endpoints

Wszystkie wymagają sesji, a zapisy XSRF.

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| GET | `/api/orders/{orderId}` (rozszerzony) | — | dodatkowo `salesCorrections[]`, `salesReductions` | sesja | odczyt |
| POST | `/api/orders/{orderId}/sales-corrections` | nagłówek `Idempotency-Key`; `{ amount, date, reason }` | `201`/`200 { id, version }`; `400`; `404 order_not_found`; `409 idempotency_key_reused` (+ `salesCorrectionId`); `422 sales_reduction_limit_exceeded` (+ `available`) | sesja + XSRF | zapis |
| PUT | `/api/orders/{orderId}/sales-corrections/{entryId}` | `{ amount, date, reason, version }` | `200 { version }`; `400`; `404`; `409 entry_conflict`; `422 sales_reduction_limit_exceeded` (+ `available`) | sesja + XSRF | zapis |
| DELETE | `/api/orders/{orderId}/sales-corrections/{entryId}` | body `{ version }` | `204`; `400`; `404`; `409 entry_conflict` | sesja + XSRF | zapis |
| POST/PUT | `/api/orders/{orderId}/refunds…` (SF-9) | bez zmian | dodatkowo `422 sales_reduction_limit_exceeded` (+ `available`) | sesja + XSRF | zapis |
| PUT | `/api/orders/{orderId}` (SF-8) | bez zmian | dodatkowo `422 sales_reduction_limit_exceeded` (+ `minimumValue`) | sesja + XSRF | zapis |

### Types crossing a boundary

- **`SalesCorrectionInput`:** `amount`, `date`, `reason`. Walidacja jak `RefundInput` bez
  `kind` i `method`; klucze błędów `amount`, `date`, `reason`, `version`.
- **`SalesCorrectionEntry`:** `id`, `amount`, `date`, `reason`, `createdAtUtc`, `version`.
  Sortowanie jak w SF-9.
- **`SalesReductions`:** `returnsTotal`, `correctionsTotal`, `total`, `excess` (string albo
  `null`).
- **Audyt:** `AuditObject(SalesCorrection, id, data korekty, „Korekta sprzedaży do {numer}”,
  Parent: Order)`, pola `amount`, `date`, `reason`. `Deleted` zachowuje wszystkie pola.

## Invariant impact

| Invariant | How it is respected |
|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | Korekta nie ma metody, nie zmienia Zapłacono, stanu, wartości zamówienia ani wpływów i wypływów. Test z AC: korekta 500 przy wartości 1000 i zaliczce 300 zostawia Zapłacono 300 i stan Częściowo opłacone. |
| 2. Kwoty decimal na backendzie | `decimal(18,2)`. Limit i nadwyżka liczone są w domenie na `decimal`; frontend tylko wyświetla. Test typu kolumny. |
| 3. Dostęp egzekwowany przez API | Sesja i XSRF na nowych trasach; testy `401` i XSRF. |
| 4. Ponowienia nie duplikują | Klucz idempotencji z unikalnym `CreationRequestId`. Test równoległy. |
| 5. Audyt finansowy | Created, Updated i Deleted z rodzicem Order w jednostce pracy. Nieudany audyt cofa zmianę. |
| 6. Sekrety po stronie serwera | Bez zmian. |
| 7. Granice modułów i warstw | Tylko `orders.*`; audyt przez `IAuditLog`. |
| 8. Brak danych identyfikujących w dokumentach | Fikcyjne dane. |

## Risk

**high.** Zadanie dotyczy podstawy sprzedaży, usuwania wpisów, limitów przy współbieżności
i zmian w zatwierdzonych ścieżkach SF-8 i SF-9. Wymaga przeglądu człowieka przed merge.

## UI

- **Szczegóły zamówienia, sekcja „Korekty sprzedaży”** pod sekcją „Płatności” (osobno od wpłat
  i zwrotów):
  - opis sekcji;
  - tabela z datą, kwotą i powodem oraz akcjami „Popraw” i „Usuń” (`danger`, z dialogiem);
  - pusty stan „Brak korekt sprzedaży.”;
  - akcja „Dodaj korektę sprzedaży” (`secondary`);
  - formularz z kwotą (`parsePlnAmount`), datą (`max` = dzisiaj) i powodem, z opisem
    z Behavior.
- **Ostrzeżenie o nadwyżce** jako alert z tekstem, przy podsumowaniu.
- **Opisy w formularzach:**
  - pole wartości w formularzu zamówienia: „Korekta wartości zmienia wartość zamówienia
    w okresie jego daty. Oddany towar bez zwrotu pieniędzy zapisz korektą sprzedaży, a
    zwrócone pieniądze zwrotem.”;
  - opisy rodzajów zwrotu (SF-9) zostają.
- **Błędy limitu:**
  - przy `sales_reduction_limit_exceeded` w formularzach korekty i zwrotu: „Suma zwrotów
    sprzedaży i korekt nie może przekroczyć wartości zamówienia. Dostępna kwota: X zł.”;
  - w formularzu zamówienia: „Wartość nie może być niższa niż suma zwrotów sprzedaży i korekt
    (X zł).”, przypięte do pola wartości.
- **Wzajemne wykluczenie (N6 z SF-9)** obejmuje też korekty: otwarty formularz albo trwająca
  operacja korekty blokuje „Edytuj” i odwrotnie.
- **Dostępność i kolory** jak w SF-9.

## Edge Cases

- **Korekta 500 przy wartości 1000 i zaliczce 300** zostawia Zapłacono 300 i stan Częściowo
  opłacone (Do zapłaty 700 bez zmian). `salesReductions.total = 500`.
- **Korekta równa wartości** (1000 przy 1000) przechodzi, a 1000,01 jest odrzucane z
  `available = 1000.00`.
- **Zwrot sprzedaży 1200** przy wartości 1000 i wpłatach 1200 daje `422
  sales_reduction_limit_exceeded` (`available = 1000.00`). Zwrot pieniędzy 1200 w tej samej
  sytuacji przechodzi.
- **Zmiana rodzaju zwrotu 600 z `moneyReturn` na `salesReturn`** przy korekcie 500 i wartości
  1000 daje `422` z `available = 500.00`.
- **Ręczne obniżenie wartości** z 1000 do 400 przy obniżeniach 500 daje `422` z
  `minimumValue = 500.00`. Obniżenie do 500 przechodzi.
- **Stan zasiany SQL-em** (sklep obniżył wartość do 300 przy obniżeniach 500) daje
  `excess = 200.00` i ostrzeżenie. Obniżenie korekty i usunięcie przechodzą, a podwyższenie
  jest odrzucane.
- **Równoległe dodanie korekty 600 i zwrotu sprzedaży 600** przy wartości 1000 i wpłatach 1000
  daje jeden zapis i jedno `422` (blokada).
- **Równoległa zmiana wartości zamówienia** na 400 i dodanie korekty 500 przy wartości 1000
  dają co najwyżej jedną z dwóch zmian (blokada), a stan końcowy spełnia limit.
- **Anulowane zamówienie:** korekta jest dozwolona, a limit działa tak samo.
- **Data jutrzejsza** daje `400`; data sprzed daty zamówienia jest dozwolona.

## Dependencies

- **SF-9:** wpisy, `OrderEntryWriter`, `IOrderLock`, idempotencja, sekcja „Płatności”. Zrobione.
- **SF-8:** `EditOrder`, formularz zamówienia. Zrobione.

## Assumptions

Odpowiedzi użytkownika z 05.11.2026:
- **Daty korekty** działają jak przy wpłatach: data z przyszłości jest odrzucana, data sprzed
  daty zamówienia dozwolona.
- **Limit obniżeń** obowiązuje zawsze, także dla zamówień anulowanych.

Założenia autora:
- **Kolejność sprawdzeń przy zwrocie:** najpierw „zwrot ≤ Zapłacono” (SF-9), potem limit
  obniżeń. Odpowiedź podaje pierwszą naruszoną regułę.
- **Zmiana obniżająca `R` albo podwyższająca `V`** nie jest blokowana nawet przy istniejącej
  nadwyżce, żeby po zmianie przez sklep dało się wyjść z nadwyżki.
- **Ostrzeżenie o nadwyżce** pokazuje się dla każdego zamówienia z `R > V`. W praktyce ten
  stan powstaje tylko przez synchronizację (SF-12), bo wszystkie zmiany użytkownika są
  ograniczone limitem.
- **Korekta do zamówienia anulowanego** jest dozwolona. PRD mówi tylko, że nie obniża jego
  sprzedaży, co należy do raportów (SF-19).
- **Usunięcie wpisu** jest fizyczne, z pełnym audytem `Deleted`, a `version` idzie w ciele
  `DELETE`, tak jak w SF-9.
- **`EditOrder` bierze blokadę zamówienia tylko przy zmianie wartości,** żeby nie
  serializować edycji pól operacyjnych.

## Acceptance Criteria

- [ ] Korekta 500 zł do zamówienia 1000 zł z zaliczką 300 zł nie zmienia Zapłacono (300) ani
  stanu płatności (Częściowo opłacone), a `salesReductions.total` wynosi 500.
- [ ] Dodanie lub poprawa korekty albo zwrotu sprzedaży i ręczne obniżenie wartości, które
  przekroczyłyby limit, są odrzucane `422 sales_reduction_limit_exceeded` z dostępną kwotą
  (`available`) albo minimalną wartością (`minimumValue`). UI pokazuje kwotę w komunikacie.
- [ ] Zwrot sprzedaży 1200 zł przy zamówieniu 1000 zł i wpłatach 1200 zł jest odrzucany,
  a zwrot pieniędzy 1200 zł w tej samej sytuacji przechodzi.
- [ ] Gdy wartość jest niższa od sumy obniżeń (stan zasiany jak po synchronizacji), szczegóły
  pokazują „Obniżenia sprzedaży przekraczają wartość o X zł”.
- [ ] Korekty są w osobnej sekcji, poza wpłatami i zwrotami, z dodawaniem, poprawą i usuwaniem
  po potwierdzeniu.
- [ ] Dodanie, poprawa i usunięcie korekty zapisują audyt z rodzicem Order; nieudany audyt
  cofa zmianę.
- [ ] Podwójne wysłanie dodania korekty nie tworzy dwóch wpisów, a równoległe obniżenia nie
  przekraczają limitu.
- [ ] Formularze opisują różnicę między korektą wartości, zwrotem i korektą sprzedaży.
- [ ] Bez sesji nowe trasy dają `401`, a zapisy bez XSRF są odrzucane.

## Verification

- **build:** `dotnet build stone-flow.slnx -c Release` (0 ostrzeżeń), `npm run build`.
- **tests:**
  - `dotnet test --solution stone-flow.slnx -c Release`:
    - `SalesReductionRules` w `StoneFlow.Orders.Tests`;
    - integracyjne `SalesCorrectionsApiTests` oraz rozszerzenia `PaymentsApiTests`
      i `OrdersApiTests` dla limitu;
  - w `app/`: `npm run typecheck`, `npm run lint`, `npm run format:check`,
    `npm test -- --watch=false`;
  - `dotnet format stone-flow.slnx --verify-no-changes`.
- **manual:** scenariusze z Edge Cases w przeglądarce (korekta, zwrot sprzedaży ponad limit,
  obniżenie wartości).
