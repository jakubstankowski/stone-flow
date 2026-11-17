# Task Specification — SF-9

## Source

[SF-9](https://standev.youtrack.cloud/issue/SF-9)

## Goal

Moduł Orders dostaje wpłaty i zwroty jako wpisy przypisane do zamówienia, z dodawaniem,
poprawą i usuwaniem z potwierdzeniem. Każdy taki zapis zostawia audyt w tej samej transakcji.
Backend wylicza dla każdego zamówienia:

- kwotę „Zapłacono” (suma wpłat minus suma zwrotów);
- stan płatności: Nieopłacone, Częściowo opłacone, Opłacone, albo brak stanu dla zamówienia
  anulowanego;
- „Do zapłaty”;
- ostrzeżenia „Nadpłata X zł” i „Do zwrotu X zł”.

Szczegóły zamówienia pokazują wpisy, kwoty, stan i ostrzeżenia. Lista zamówień dostaje kolumny
„Zapłacono” i „Stan płatności” oraz filtr po stanie, przeniesione z SF-8. Zapis zamówienia nadal
nie oznacza otrzymania pieniędzy, a jedynym zapisem wpływu jest wpłata.

## Context

- **Backend, moduł Orders.** Wpłaty i zwroty należą do Orders (PRD §10 „Architektura:
  zamówienia (także wpłaty i zwroty)”). Zmiany w warstwach:
  - **Domain** `Orders.Domain/Payments/`: encje `Payment` i `Refund` (osobne obiekty powiązane
    z zamówieniem przez `OrderId`, nie w kolekcji agregatu `Order`), słowniki metod i rodzaju
    zwrotu, `PaymentSummary` (czysta funkcja wyliczająca Zapłacono, stan, Do zapłaty,
    Nadpłatę i Do zwrotu z wartości zamówienia, jego statusu i kwot wpisów), reguły limitów.
  - **Application:** foldery `AddPayment/`, `EditPayment/`, `DeletePayment/`, `AddRefund/`,
    `EditRefund/`, `DeleteRefund/`. Rozszerzone `GetOrder/` (wpisy i podsumowanie) oraz
    `ListOrders/` (kolumny i filtr). Nowe porty w `Abstractions/`.
  - **Infrastructure:** tabele `orders.Payments` i `orders.Refunds`, migracja, porty zapisu
    i blokady zamówienia, projekcje sum w odczycie.
  - **Api:** trasy pod `/api/orders/{orderId}/payments` i `/api/orders/{orderId}/refunds`.
- **Shared:** `IUnitOfWork`, `IAuditLog` (`AuditObjectType.Payment` / `Refund` z rodzicem
  `Order`, wymaganym przez SF-7). Brak nowych typów współdzielonych.
- **Frontend:**
  - `features/orders/`: sekcja „Płatności” w `order-details-page`, formularze wpłaty
    i zwrotu;
  - lista dostaje kolumny i filtr;
  - dialog anulowania dostaje ostrzeżenie o wpłatach.
- Uwierzytelnianie, XSRF, Problem Details i konwencje kwot (string dziesiętny) działają jak
  w SF-8.

## Scope

### In scope

- **Wpłata:** kwota (> 0), data operacji, metoda (przelew, BLIK, gotówka, pobranie),
  opcjonalne „Przyjął” tylko przy gotówce. Zamówienie może mieć wiele wpłat. Wpłaty dodaje
  się do zamówień ręcznych i ze sklepu, także anulowanych (z ostrzeżeniem).
- **Zwrot:**
  - rodzaj, wymagany i bez wartości domyślnej: `salesReturn` („Zwrot sprzedaży”) albo
    `moneyReturn` („Zwrot pieniędzy”);
  - kwota (> 0, nie większa niż Zapłacono);
  - data, metoda, powód (wymagany).
- **Poprawa** wpłaty i zwrotu, w tym zmiana rodzaju zwrotu, oraz **usunięcie** wpłaty
  i zwrotu. Usunięcie wymaga potwierdzenia, a ślad zostaje w audycie (`Deleted`).
- **Reguły odrzucenia:**
  - zwrot, po którym Zapłacono byłoby ujemne;
  - poprawka lub usunięcie, po którym Zapłacono spadłoby poniżej 0 zł, z komunikatem, że
    najpierw trzeba poprawić lub usunąć zwrot.
- **Wyliczenie po stronie backendu:** Zapłacono, stan, Do zapłaty, Nadpłata, Do zwrotu.
- **Ostrzeżenia:**
  - „Nadpłata X zł”;
  - „Do zwrotu X zł” dla zamówienia anulowanego;
  - ostrzeżenie przy dodaniu wpłaty do zamówienia anulowanego;
  - ostrzeżenie w dialogu anulowania, gdy Zapłacono > 0.
- **Lista zamówień:** kolumny „Zapłacono” i „Stan płatności”, filtr po stanie płatności
  liczony w bazie.
- **Ochrona przed ponownym wysłaniem** dodania wpłaty i zwrotu (klucz idempotencji jak
  w SF-8) oraz kontrola współbieżności poprawy i usunięcia (`rowversion`).
- **Serializacja zapisów wpisów jednego zamówienia**, tak żeby równoległe wpisy nie
  przekroczyły limitu.

### Out of scope

- Limit obniżeń sprzedaży i korekty sprzedaży: SF-10. Zmiana rodzaju zwrotu nie sprawdza
  tego limitu.
- Wpływ wpłat i zwrotów na raporty i lista „Operacje”: SF-17, SF-19, SF-20.
- Automatyczne wpłaty i zwroty z WooCommerce.
- Lista „Historia zmian”: SF-18.
- Przełom uruchomienia w raportach (wpłaty sprzed uruchomienia są zapisywane normalnie;
  wykluczanie z raportów robią SF-19 i SF-20).

## Behavior

### Wyliczenie podsumowania płatności

`PaymentSummary` liczy z wartości zamówienia `V`, statusu realizacji, sumy wpłat `W` i sumy
zwrotów `Z`, na `decimal` i bez zaokrągleń, bo kwoty mają 2 miejsca:

1. Zapłacono `P = W − Z`.
2. Zamówienie anulowane:
   - nie ma stanu (`paymentState = null`) ani „Do zapłaty”, a „Nadpłata” się nie pokazuje;
   - przy `P > 0` ma „Do zwrotu P zł”.
3. Pozostałe zamówienia:
   - `V = 0` daje Opłacone, niezależnie od `P`, zgodnie z PRD;
   - `P = 0` daje Nieopłacone;
   - `0 < P < V` daje Częściowo opłacone;
   - `P ≥ V` daje Opłacone.
4. „Do zapłaty” wynosi `V − P`, gdy stan to Nieopłacone albo Częściowo opłacone; w
   pozostałych przypadkach `null`.
5. „Nadpłata” wynosi `P − V`, gdy zamówienie nie jest anulowane i `P > V`; w pozostałych
   przypadkach `null`.

Stan nie jest przechowywany. Liczy się go przy każdym odczycie: w szczegółach z wpisów
zamówienia, a na liście w zapytaniu SQL z sumami. Obie drogi muszą dawać ten sam wynik, co
sprawdza test.

### Dodanie wpłaty

1. W szczegółach zamówienia użytkownik wybiera „Dodaj wpłatę”. Formularz rozwija się w sekcji
   „Płatności”, a frontend generuje klucz idempotencji.
2. Użytkownik wpisuje kwotę, datę (domyślnie dzisiaj według Europe/Warsaw) i metodę. Pole
   „Przyjął” pojawia się tylko przy gotówce.
3. Gdy zamówienie jest anulowane, formularz przed zapisem pokazuje ostrzeżenie: „Zamówienie
   jest anulowane. Wpłata zwiększy kwotę do zwrotu.”
4. „Zapisz” przechodzi w stan `busy`. API waliduje dane, a potem w jednej jednostce pracy:
   1. blokuje wiersz zamówienia (`404`, gdy nie istnieje);
   2. sprawdza klucz idempotencji;
   3. zapisuje wpłatę;
   4. zapisuje audyt `Created` (obiekt Payment, rodzic Order).
5. Odpowiedź `201 { id, version }`. Powtórzenie z tym samym kluczem i danymi daje `200` z tym
   samym `id`, z innymi danymi `409 idempotency_key_reused` z `paymentId`. Normalizacja
   i skrót działają jak w SF-8: teksty przycięte, puste zamienione na `null`, kwota
   kanoniczna.
6. UI odświeża szczegóły zamówienia: podsumowanie i listę wpisów.

### Dodanie zwrotu

Działa jak dodanie wpłaty, z tymi różnicami:

- **Pola:** rodzaj (radio bez zaznaczenia domyślnego, z opisem wpływu na sprzedaż według
  design guide), kwota, data, metoda (przelew, BLIK, gotówka), powód.
- **Opisy rodzajów w UI:**
  - „Zwrot sprzedaży”: oddany towar, wycofana sprzedaż; obniży sprzedaż.
  - „Zwrot pieniędzy”: zwrot nadpłaty lub zaliczki; nie zmienia sprzedaży.
- **Limit:** pod blokadą zamówienia API liczy aktualne Zapłacono `P`. Gdy kwota zwrotu
  przekracza `P`, odpowiada `422 refund_exceeds_paid` z rozszerzeniem `available` (`P`).
  UI pokazuje „Zwrot nie może przekroczyć kwoty Zapłacono (X zł).”
- **Zamówienie anulowane:** zwrot jest dozwolony.

### Poprawa wpisu

1. Na liście wpisów jest akcja „Popraw”. Otwiera formularz z bieżącymi danymi i `version`
   wpisu.
2. API w jednej jednostce pracy:
   1. blokuje zamówienie;
   2. wczytuje wpis należący do tego zamówienia (`404`, także gdy wpis należy do innego
      zamówienia);
   3. porównuje `version` (`409 entry_conflict`);
   4. wylicza Zapłacono po zmianie i sprawdza reguły (niżej);
   5. zapisuje wpis i audyt `Updated`, tylko ze zmienionymi polami. Brak zmian daje sukces
      bez zapisu i bez audytu.
3. **Reguły:**
   - wpłata po poprawce: jeżeli nowe Zapłacono < 0, odpowiedź to
     `422 paid_would_be_negative` („Najpierw popraw lub usuń zwrot.”);
   - zwrot po poprawce: jeżeli nowe Zapłacono < 0, odpowiedź to `422 refund_exceeds_paid`
     z `available`, czyli kwotą, do której można podnieść zwrot. Zmiana rodzaju zwrotu jest
     dozwolona i audytowana.

### Usunięcie wpisu

1. „Usuń” otwiera `app-confirm-dialog`, który wskazuje wpis (rodzaj, kwotę i datę) i skutek:
   zmienia Zapłacono i stan płatności, a ślad zostaje w historii zmian.
2. API pod blokadą zamówienia sprawdza `version` (`409 entry_conflict`).
3. Przy wpłacie: jeżeli Zapłacono po usunięciu byłoby < 0, odpowiedź to
   `422 paid_would_be_negative`. Usunięcie zwrotu zawsze zwiększa Zapłacono, więc jest
   dozwolone.
4. Wiersz jest usuwany fizycznie. Audyt `Deleted` zapisuje wszystkie pola wpisu jako stare
   wartości, więc ślad pozostaje po zniknięciu obiektu (SF-7).

### Anulowanie zamówienia z wpłatami

Dialog „Anuluj zamówienie” z SF-8 przy Zapłacono > 0 dodaje ostrzeżenie: „Zamówienie ma wpłaty:
Zapłacono X zł. Wpłaty pozostaną, a pieniądze rozlicz zwrotem.” Endpoint anulowania się nie
zmienia, a wpłaty pozostają.

### Serializacja i współbieżność

- Każdy zapis wpisu (dodanie, poprawa, usunięcie) zaczyna jednostkę pracy od
  `SELECT … FROM orders.Orders WITH (UPDLOCK, ROWLOCK) WHERE Id = @orderId`, przez
  `OrdersDbContext` dołączony do transakcji. Sumy liczone są dopiero po uzyskaniu blokady.
  Dwa równoległe zwroty, z których każdy osobno mieści się w limicie, a razem go przekraczają,
  kończą się jednym zapisem i jednym `422 refund_exceeds_paid`.
- Blokada nie zmienia `rowversion` zamówienia, więc dodanie wpłaty nie wywołuje konfliktu
  w otwartej edycji zamówienia.
- Wpisy mają własne `rowversion`. Poprawa i usunięcie porównują wersję klienta przez
  `OriginalValue` (wzorzec z SF-8), a `DbUpdateConcurrencyException` port tłumaczy na
  `409 entry_conflict`.
- Edycja wartości zamówienia (SF-8) zmienia stan wyliczany przy odczycie i nie wymaga zmian
  w SF-8.

## Data / API

### Storage

Schemat `orders`, migracja `PaymentsAndRefunds`.

- **`orders.Payments`:**
  - `Id uniqueidentifier` PK;
  - `OrderId uniqueidentifier` NOT NULL, FK do `orders.Orders(Id)` z `ON DELETE NO ACTION`
    (zamówień się nie usuwa);
  - `Amount decimal(18,2)` NOT NULL, `CHECK (Amount > 0)`;
  - `PaidOn date` NOT NULL;
  - `Method nvarchar(16)` NOT NULL (`transfer`, `blik`, `cash`, `cashOnDelivery`);
  - `ReceivedBy nvarchar(100)` NULL, `CHECK (ReceivedBy IS NULL OR Method = 'cash')`;
  - `CreationRequestId uniqueidentifier` NULL z unikalnym filtrowanym indeksem
    i `CreationRequestHash binary(32)` NULL;
  - `CreatedAtUtc datetime2`, `UpdatedAtUtc datetime2`, `Version rowversion`;
  - indeks `(OrderId)` z `INCLUDE (Amount)`.
- **`orders.Refunds`:**
  - `Id`, `OrderId` (FK jak wyżej);
  - `Kind nvarchar(16)` NOT NULL (`salesReturn`, `moneyReturn`);
  - `Amount decimal(18,2)` `CHECK (Amount > 0)`;
  - `RefundedOn date`;
  - `Method nvarchar(16)` (`transfer`, `blik`, `cash`);
  - `Reason nvarchar(500)` NOT NULL;
  - `CreationRequestId` i `CreationRequestHash` jak w `Payments`;
  - `CreatedAtUtc`, `UpdatedAtUtc`, `Version rowversion`;
  - indeks `(OrderId)` z `INCLUDE (Amount)`.
- Enumy są zapisywane jako string z kodem. Bez kolumn wyliczanych w `orders.Orders`.

### Endpoints

Wszystkie wymagają sesji. Zapisy wymagają XSRF. `orderId` i `entryId` to GUID.

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| GET | `/api/orders/{orderId}` (rozszerzony) | — | `200 OrderDetails` + `payment { paid, state, amountDue, overpayment, toRefund }`, `payments[]`, `refunds[]` | sesja | odczyt |
| GET | `/api/orders` (rozszerzony) | dodatkowy, wielokrotny `paymentState` (`unpaid`, `partiallyPaid`, `paid`, `none`) | `OrderListItem` + `paid`, `paymentState` | sesja | odczyt |
| POST | `/api/orders/{orderId}/payments` | nagłówek `Idempotency-Key`; `{ amount, date, method, receivedBy? }` | `201 { id, version }`; `200` przy powtórzeniu; `400`; `404 order_not_found`; `409 idempotency_key_reused` (+ `paymentId`) | sesja + XSRF | zapis |
| PUT | `/api/orders/{orderId}/payments/{entryId}` | `{ amount, date, method, receivedBy?, version }` | `200 { version }`; `400`; `404 entry_not_found`; `409 entry_conflict`; `422 paid_would_be_negative` | sesja + XSRF | zapis |
| DELETE | `/api/orders/{orderId}/payments/{entryId}?version=…` | — | `204`; `400` (brak `version`); `404`; `409 entry_conflict`; `422 paid_would_be_negative` | sesja + XSRF | zapis |
| POST | `/api/orders/{orderId}/refunds` | nagłówek `Idempotency-Key`; `{ kind, amount, date, method, reason }` | `201 { id, version }`; `200`; `400`; `404`; `409 idempotency_key_reused` (+ `refundId`); `422 refund_exceeds_paid` (+ `available`) | sesja + XSRF | zapis |
| PUT | `/api/orders/{orderId}/refunds/{entryId}` | `{ kind, amount, date, method, reason, version }` | `200 { version }`; `400`; `404`; `409 entry_conflict`; `422 refund_exceeds_paid` | sesja + XSRF | zapis |
| DELETE | `/api/orders/{orderId}/refunds/{entryId}?version=…` | — | `204`; `400`; `404`; `409 entry_conflict` | sesja + XSRF | zapis |

### Types crossing a boundary

Kwoty są stringami z 2 miejscami po przecinku, daty mają format `yyyy-MM-dd`, kody są w
camelCase.

- **`PaymentInput`:** `amount`, `date`, `method` (`transfer|blik|cash|cashOnDelivery`),
  `receivedBy?`.
- **`RefundInput`:** `kind` (`salesReturn|moneyReturn`, wymagany), `amount`, `date`, `method`
  (`transfer|blik|cash`), `reason`.
- **`PaymentEntry`:** `id`, `amount`, `date`, `method`, `receivedBy`, `createdAtUtc`, `version`.
- **`RefundEntry`:** `id`, `kind`, `amount`, `date`, `method`, `reason`, `createdAtUtc`,
  `version`.
- **`OrderPaymentSummary`:**
  - `paid`;
  - `state` (`unpaid|partiallyPaid|paid|null`);
  - `amountDue`, `overpayment`, `toRefund` (string albo `null`).
- **Sortowanie wpisów w szczegółach:** po dacie rosnąco, potem `createdAtUtc`, potem `id`.
- **Walidacja** (klucze błędów to ścieżki JSON):
  - `amount`: > 0, najwyżej 2 miejsca po przecinku, w zakresie `decimal(18,2)`;
  - `date`: wymagana i nie późniejsza niż dzisiaj według Europe/Warsaw (`TimeProvider`).
    Data sprzed daty zamówienia jest dozwolona;
  - `method`: znany kod danego rodzaju wpisu;
  - `receivedBy`: najwyżej 100 znaków i tylko przy `cash`; przy innej metodzie niepusta
    wartość daje `400` pod kluczem `receivedBy`;
  - `kind`: wymagany;
  - `reason`: od 1 do 500 znaków po przycięciu;
  - `version`: wymagana w `PUT` i `DELETE`.
- **Audyt:**
  - `AuditObject(Payment|Refund, id, data wpisu, etykieta, Parent: Order)`;
  - etykieta: „Wpłata do {numer zamówienia}” / „Zwrot do {numer zamówienia}”, bez danych
    osobowych;
  - pola wpłaty: `amount`, `date`, `method`, `receivedBy` (z `IsPersonalData = true`);
  - pola zwrotu: `kind`, `amount`, `date`, `method`, `reason`;
  - format wartości jak w SF-8. `Created` pomija pola z wartością `null`. `Deleted` zapisuje
    wszystkie niepuste pola jako stare wartości z nową `null`;
  - data biznesowa to data wpisu, a przy poprawie nowa data.

## Invariant impact

| Invariant | How it is respected |
|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | Wpłata jest jedynym zapisem wpływu. Nie zmienia wartości zamówienia, nie tworzy drugiej sprzedaży i nie zmienia statusu realizacji. Pobranie to tylko metoda: zamówienie bez wpłaty pozostaje Nieopłacone. Zwrot pieniędzy i zwrot sprzedaży różnią się tylko polem `kind`; wpływ na sprzedaż liczy SF-19. Test: zamówienie z metodą pobrania bez wpłaty jest Nieopłacone, a dodanie wpłaty nie zmienia `value`. |
| 2. Kwoty decimal na backendzie | `decimal(18,2)` w bazie i `decimal` w domenie. Wyliczenia są tylko w `PaymentSummary` i w zapytaniu SQL. Frontend wyświetla otrzymane kwoty i nie liczy. Testy jednostkowe `PaymentSummary` sprawdzają wszystkie progi. |
| 3. Dostęp egzekwowany przez API | Wszystkie trasy wymagają sesji, zapisy XSRF. Testy `401` dla każdej trasy i odrzucenie zapisu bez XSRF. |
| 4. Ponowienia nie duplikują | Klucz idempotencji z unikalnym `CreationRequestId` dla dodania wpłaty i zwrotu. Test: równoległe dodania z tym samym kluczem dają jeden wpis. |
| 5. Audyt finansowy | Created, Updated i Deleted w tej samej jednostce pracy. Usunięcie zostawia wpis `Deleted` ze wszystkimi polami. Nieudany audyt cofa zapis. |
| 6. Sekrety po stronie serwera | Bez nowych sekretów. Logi zawierają tylko identyfikatory. |
| 7. Granice modułów i warstw | Tabele w `orders.*`, audyt przez `IAuditLog`, domena bez EF i HTTP, odczyty przez porty jako DTO. |
| 8. Brak danych identyfikujących w dokumentach | Dane testowe są fikcyjne. |

## Risk

**high.** Zadanie dotyczy pieniędzy (wpływy i stan płatności), usuwania danych finansowych
oraz współbieżnych zapisów z limitami. Wymaga przeglądu człowieka przed merge.

## UI

- **Trasy bez zmian:** `/zamowienia`, `/zamowienia/:id`.
- **Szczegóły zamówienia, nowa sekcja „Płatności”:**
  - **Podsumowanie:**
    - „Zapłacono X zł”;
    - stan płatności jako `app-status-badge` (Nieopłacone, Częściowo opłacone, Opłacone),
      a dla zamówienia anulowanego tekst „Brak stanu: zamówienie anulowane”;
    - „Do zapłaty X zł”, gdy jest;
    - ostrzeżenia „Nadpłata X zł” i „Do zwrotu X zł” jako alerty z tekstem, nie samym
      kolorem.
  - **Dwie tabele:**
    - „Wpłaty”: data, kwota, metoda, Przyjął;
    - „Zwroty”: data, rodzaj, kwota, metoda, powód.

    Obie mają akcje „Popraw” i „Usuń” (`danger`, z dialogiem). Puste stany to „Brak wpłat.”
    i „Brak zwrotów.”
  - **Akcje** „Dodaj wpłatę” i „Dodaj zwrot” (`secondary`; główną akcją widoku zostaje
    „Edytuj”). Rozwijają formularz w sekcji; naraz otwarty jest jeden formularz wpisu.
  - **Formularze:**
    - Signal Forms w `app-field` z podsumowaniem błędów;
    - kwota przez `parsePlnAmount`;
    - data `type="date"` z `max` równym dzisiejszej dacie;
    - pole „Przyjął” tylko przy gotówce;
    - rodzaj zwrotu to grupa radio w `fieldset` z `legend`, bez wartości domyślnej, z opisami
      powiązanymi przez `aria-describedby`.
  - **Komunikaty:** konflikt `409 entry_conflict` pokazuje „Wpis został zmieniony
    w międzyczasie” z akcją „Wczytaj aktualne dane”. Błędy `422` pokazują komunikat
    z kwotą `available`.
  - Po każdym zapisie, poprawie i usunięciu strona pobiera szczegóły zamówienia ponownie.
    Komunikat sukcesu trafia do regionu `aria-live`.
- **Dialog anulowania:** ostrzeżenie o wpłatach przy Zapłacono > 0.
- **Lista zamówień:**
  - nowe kolumny „Zapłacono” (wyrównana do prawej) i „Stan płatności” (badge, a dla
    anulowanego „—” z tekstem dla czytnika „brak stanu”);
  - filtr „Stan płatności” z wielokrotnym wyborem: Nieopłacone, Częściowo opłacone, Opłacone,
    Bez stanu (anulowane).
- **Stany:** ładowanie i błąd sekcji są wspólne ze szczegółami. Formularz ma stan
  „Zapisywanie…” z blokadą ponownego wysłania.
- **Kolory** tylko przez tokeny `--sf-*` i motyw; tony stanów płatności według design guide.
- **Dostępność:**
  - po otwarciu formularza fokus trafia na pierwsze pole, a po zamknięciu wraca na przycisk,
    który go otworzył;
  - dialog usuwania przechwytuje fokus;
  - tabele mają nagłówki i `caption`;
  - widok 375 px nie przewija się poziomo; tabele wpisów mogą przewijać się w swoim
    kontenerze.

## Edge Cases

- **Zamówienie 0 zł:**
  - bez wpłat ma stan Opłacone;
  - wpłata 50 zł daje Opłacone i „Nadpłatę 50,00 zł”;
  - zwrot pieniędzy 50 zł daje Zapłacono 0 i stan Opłacone.
- **Wpłaty 300 + 700 przy wartości 1000:** stan przechodzi z Nieopłacone przez Częściowo
  opłacone (Do zapłaty 700) na Opłacone.
- **Nadpłata:** wpłata 1200 przy wartości 1000 daje Opłacone i „Nadpłata 200,00 zł”. Zwrot
  pieniędzy 200 zł zostawia Opłacone bez nadpłaty.
- **Zwrot przekraczający Zapłacono:** zwrot 1200 przy Zapłacono 1000 daje
  `422 refund_exceeds_paid` z `available = 1000.00`.
- **Poprawa wpłaty:** zmniejszenie wpłaty z 1000 do 100 przy zwrocie 300 daje
  `422 paid_would_be_negative`.
- **Usunięcie wpłaty** przy zwrocie równym tej wpłacie daje `422`; usunięcie zwrotu jest
  zawsze dozwolone.
- **Anulowanie:** zamówienie z Zapłacono 500 nie ma stanu i pokazuje „Do zwrotu 500,00 zł”.
  Po zwrocie 500 zł „Do zwrotu” znika. Po przywróceniu stan wraca do wyliczenia.
- **Pobranie bez wpłaty:** zamówienie pozostaje Nieopłacone. Metoda `cashOnDelivery` jest
  dostępna tylko w samej wpłacie.
- **„Przyjął”** przy metodzie przelew daje `400`. Zmiana metody z gotówki na przelew
  w poprawce wymaga wyczyszczenia pola: UI czyści je samo, a API odrzuca niepuste.
- **Daty:**
  - data jutrzejsza daje `400`;
  - data sprzed daty zamówienia albo sprzed uruchomienia jest przyjmowana;
  - o 23:30 UTC, gdy w Polsce jest już następny dzień, „dzisiaj” liczy się według
    Europe/Warsaw.
- **Wyścig limitu:** dwa równoległe zwroty po 600 zł przy Zapłacono 1000 dają jeden zapis
  i jedno `422`.
- **Wpis z innego zamówienia:** wpis istniejący, ale należący do innego zamówienia, daje
  `404 entry_not_found`.
- **Dwie karty:** poprawa wpisu z dwóch kart daje drugiej `409 entry_conflict`. Usunięcie
  wpisu już usuniętego daje `404`.
- **Zamówienie ze sklepu:** wpłaty i zwroty działają tak samo jak dla zamówienia ręcznego.
- **Spójność dróg wyliczenia:** stan z listy i ze szczegółów dla tych samych danych jest
  identyczny (test zgodności dla wszystkich progów).

## Dependencies

- **SF-8:** model zamówienia, lista, szczegóły i dialog anulowania. Zrobione.
- **SF-7:** `IUnitOfWork`, `IAuditLog`, rodzic Order dla Payment i Refund. Zrobione.

## Assumptions

Odpowiedzi użytkownika z 05.11.2026:
- **Daty:** przyszła data wpłaty i zwrotu jest odrzucana według Europe/Warsaw. Data wcześniejsza
  niż data zamówienia jest dozwolona.
- **Rodzaj zwrotu:** można go zmienić po zapisie, z audytem. Limit obniżeń dodaje SF-10.
- **„Do zapłaty”:** wyliczane w backendzie i pokazywane w szczegółach przy stanie Nieopłacone
  i Częściowo opłacone. Nie ma go na liście ani przy zamówieniu anulowanym.
- **Lista:** kolumny „Zapłacono” i „Stan płatności” oraz filtr po stanie płatności wchodzą
  do SF-9 (przeniesione z SF-8).

Założenia autora specyfikacji:
- **Metody zwrotu:** przelew, BLIK, gotówka, bez pobrania. PRD wymienia metody płatności
  ogólnie, a pobranie to sposób przyjęcia pieniędzy od klienta.
- **„Przyjął”:** tekst wolny do 100 znaków, oznaczony w audycie jako dane osobowe, bo jest
  imieniem osoby.
- **Wpłaty i zwroty to osobne encje**, powiązane `OrderId`, a nie kolekcja wczytywana
  z agregatem `Order`. Granica spójności to blokada wiersza zamówienia w jednostce pracy.
  Dzięki temu dodanie wpłaty nie koliduje z otwartą edycją zamówienia.
- **Usunięcie wpisu jest fizyczne.** Ślad zostaje w audycie `Deleted` ze wszystkimi polami.
  PRD wymaga „śladu audytu”, nie miękkiego usuwania.
- **`version` w `DELETE`** jest przekazywana w parametrze zapytania, bo klient HTTP frontendu
  nie wysyła ciała w `DELETE` w ustalonym wzorcu.
- **Limit „zwrot ≤ Zapłacono”** jest sprawdzany jako „Zapłacono po zmianie ≥ 0”. Tak samo dla
  dodania i poprawy zwrotu oraz poprawy i usunięcia wpłaty.
- **Ostrzeżenie o wpłacie do anulowanego zamówienia** pokazuje formularz przed zapisem
  (informacja z `OrderDetails`). API nie wymaga potwierdzenia.
- **Wpisy w szczegółach** są zwracane w całości, bez paginacji. Wolumen to kilka wpisów na
  zamówienie.
- **Filtr „Bez stanu”** oznacza zamówienia anulowane. Łączy się z innymi stanami przez OR.

## Acceptance Criteria

- [ ] Kilka wpłat sumuje się w Zapłacono, a stan przechodzi z Nieopłacone przez Częściowo
  opłacone na Opłacone, z „Do zapłaty” przy dwóch pierwszych stanach.
- [ ] Zwrot obniża Zapłacono i przelicza stan, także na liście.
- [ ] Nadpłata pozostawia Opłacone i pokazuje „Nadpłata X zł”.
- [ ] Zamówienie anulowane nie ma stanu i przy Zapłacono > 0 pokazuje „Do zwrotu X zł”.
  Dialog anulowania zamówienia z wpłatami ostrzega o nich, a wpłata do anulowanego
  zamówienia pokazuje ostrzeżenie przed zapisem.
- [ ] Zamówienie o wartości 0 zł ma stan Opłacone.
- [ ] Wpłata i zwrot o kwocie 0 lub ujemnej dają `400`. Zwrot ponad Zapłacono daje
  `422 refund_exceeds_paid` z dostępną kwotą.
- [ ] Poprawka lub usunięcie, po którym Zapłacono byłoby ujemne, daje `422` i niczego nie
  zmienia.
- [ ] Zamówienie z metodą pobrania bez dodanej wpłaty pozostaje Nieopłacone, a dodanie wpłaty
  nie zmienia wartości zamówienia ani statusu realizacji.
- [ ] Zwrot wymaga wyboru rodzaju (brak daje `400`, UI nie ma wartości domyślnej). Rodzaj
  można zmienić poprawką, z audytem.
- [ ] Data z przyszłości daje `400`; data sprzed daty zamówienia jest przyjmowana.
- [ ] Dodanie, poprawa i usunięcie wpisu zapisują audyt (`Created`, `Updated`, `Deleted`)
  z rodzicem Order. Nieudany audyt cofa zmianę. Usunięty wpis ma w audycie wszystkie pola.
- [ ] Podwójne wysłanie dodania wpłaty lub zwrotu nie tworzy dwóch wpisów (`Idempotency-Key`
  i `busy`).
- [ ] Równoległe zwroty, które razem przekraczają Zapłacono, dają jeden zapis i jedno `422`.
- [ ] Poprawa lub usunięcie ze starą `version` daje `409 entry_conflict`.
- [ ] Lista ma kolumny Zapłacono i Stan płatności oraz filtr po stanie liczony w bazie, zgodny
  z wyliczeniem w szczegółach.
- [ ] Reguły `PaymentSummary` i limitów mają testy jednostkowe. Kwoty używają `decimal(18,2)`.
- [ ] Bez sesji wszystkie nowe trasy dają `401`, a zapis bez XSRF jest odrzucany.

## Verification

- **build:** `dotnet build stone-flow.slnx -c Release` (0 ostrzeżeń), `npm run build` w `app/`.
- **tests:**
  - `dotnet test --solution stone-flow.slnx -c Release`:
    - testy domeny `PaymentSummary` i limitów w `StoneFlow.Orders.Tests`;
    - testy integracyjne `Api.IntegrationTests/Orders/PaymentsApiTests.cs` (CRUD, limity,
      wyścig, idempotencja, audyt i jego wycofanie, konflikty, lista i filtr, autoryzacja);
  - w `app/`: `npm run typecheck`, `npm run lint`, `npm run format:check`,
    `npm test -- --watch=false`;
  - `dotnet format stone-flow.slnx --verify-no-changes`.
- **manual:**
  - na zamówieniu 1000 zł: wpłata 300, wpłata 700, zwrot pieniędzy 200, poprawka i usunięcie;
    po każdym kroku sprawdzić stan i kwoty w szczegółach i na liście;
  - anulować zamówienie z wpłatami: ostrzeżenie w dialogu, potem „Do zwrotu”;
  - sprawdzić filtr stanu płatności;
  - sprawdzić widok 375 px i klawiaturę.
