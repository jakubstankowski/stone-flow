# Task Specification — SF-8

## Source

[SF-8](https://standev.youtrack.cloud/issue/SF-8)

## Goal

Moduł Orders dostaje model zamówienia z klientem i danymi operacyjnymi oraz pola potrzebne
później integracji WooCommerce (źródło techniczne, zewnętrzne ID, status ze sklepu). Użytkownik
może dodać zamówienie ręczne z ochroną przed ponownym wysłaniem, przeglądać wspólną listę
z filtrowaniem, sortowaniem i paginacją po stronie API, otworzyć szczegóły i poprawić dane
należące do ERP. Może też anulować i przywrócić zamówienie po potwierdzeniu. Równoczesne zmiany
kończą się konfliktem, a nie cichym nadpisaniem. Każde utworzenie i każda zmiana zostawia wpis
audytu w tej samej transakcji (SF-7). Zamówienia nie da się usunąć.

## Context

- **Backend, moduł Orders** (`api/src/Modules/Orders`), warstwy Onion:
  - `Orders.Domain/Orders/`: agregat `Order`, value object `Customer`, enumy słowników, reguły
    własności pól, anulowanie i przywracanie.
  - `Orders.Application/`: foldery przypadków użycia `CreateOrder/`, `EditOrder/`,
    `CancelOrder/`, `RestoreOrder/`, `GetOrder/`, `ListOrders/`; porty w `Abstractions/`.
  - `Orders.Infrastructure/Persistence/`: `OrdersDbContext` (schemat `orders`), konfiguracja
    encji, migracja, implementacje portów (zapis, odczyt list i szczegółów, numeracja).
  - `Orders.Api/Endpoints/OrdersEndpoints.cs`: grupa `/api/orders`, która woła tylko
    `IDispatcher`.
- **Shared:** `IUnitOfWork` i `IAuditLog` z SF-7 (`AuditObjectType.Order`). `Shared.Kernel` nie
  dostaje nowych typów.
- **Frontend, `features/orders/`:** lista, formularz „Dodaj zamówienie”, szczegóły z edycją,
  `orders-api.ts` i serwis stanu. Obecna strona `orders-page` ma tylko zaślepkę i zostaje
  zastąpiona. Gałąź `demo-teaser` nie jest bazą.
- Uwierzytelnianie i autoryzacja działają tak jak w SF-6: każdy endpoint wymaga ważnej sesji,
  a zmiany stanu wymagają tokenu XSRF.

## Scope

### In scope

- **Model zamówienia:**
  - numer ERP, źródło techniczne (`Manual`, `WooCommerce`), zewnętrzne ID, kanał, data
    zamówienia;
  - klient, wartość brutto, status realizacji, status ze sklepu, komentarz;
  - dane operacyjne;
  - techniczne znaczniki UTC i token współbieżności.
- **Słowniki stałe w kodzie:**
  - kanały: `Shop` (sklep), `Olx`, `Acquaintance` (znajomy), `Phone` (telefon), `Other` (inne);
  - statusy realizacji: `InProgress` (W trakcie), `Completed` (Zakończone), `Problem`
    (Problem/reklamacja), `Cancelled` (Anulowane).
- **Dodanie zamówienia ręcznego** z kluczem idempotencji i nadaniem numeru `ZR-{rok}-{NNNN}`.
- **Lista** z filtrowaniem, sortowaniem i paginacją w bazie.
- **Szczegóły i edycja** pól należących do ERP, z kontrolą współbieżności. W zamówieniu ręcznym
  edycja obejmuje też wartość (korekta wartości) i datę zamówienia.
- **„Anuluj zamówienie” i „Przywróć zamówienie”** z potwierdzeniem, kontrolą współbieżności
  i audytem.
- **Audyt** utworzenia, każdej zmiany, anulowania i przywrócenia.
- **Brak usuwania:** nie ma endpointu `DELETE` ani akcji „Usuń”.
- **Reguła własności pól ze sklepu w domenie:** zamówienie z `Source = WooCommerce` ma ID, datę,
  dane klienta, wartość i kanał („sklep”) tylko do odczytu w edycji użytkownika.
  - Regułę pokrywają testy domeny, ale żaden przypadek użycia jeszcze nie tworzy takich
    zamówień.

### Out of scope

- Wpłaty, zwroty, kwota „Zapłacono”, stan płatności, ostrzeżenie o wpłatach przy anulowaniu,
  „Do zwrotu X zł” i filtr po stanie płatności: SF-9.
- Korekty sprzedaży, limit obniżeń sprzedaży i jego sprawdzanie przy korekcie wartości: SF-10.
- Import i synchronizacja WooCommerce, ukrywanie statusów pending/failed/szkiców oraz ostrzeżenie
  „Zamówienie aktywne w sklepie”: SF-12 i następne.
- Wyliczanie sprzedaży i raporty: SF-19.
- Lista „Historia zmian”: SF-18.
- Anonimizacja: SF-22.
- Edytowalne słowniki i ekran ustawień.
- Strażnik niezapisanych zmian przy opuszczaniu formularza.

## Behavior

### Dodanie zamówienia ręcznego

1. Użytkownik wybiera „Dodaj zamówienie” na liście i otwiera się `/zamowienia/nowe`. Przy
   otwarciu frontend generuje klucz idempotencji (UUID) i trzyma go aż do udanego zapisu
   formularza.
2. Użytkownik wpisuje dane:
   - **klient:** rodzaj (osoba albo firma), imię i nazwisko lub nazwa;
   - **zamówienie:** datę, kanał, wartość brutto;
   - **opcjonalnie:** dane kontaktowe, adres, NIP, status realizacji (domyślnie „W trakcie”;
     „Anulowane” nie jest do wyboru), dane operacyjne i komentarz.
3. UI waliduje formularz. „Zapisz” przechodzi w stan „Zapisywanie…” i blokuje ponowne
   wysłanie.
4. Brak nagłówka `Idempotency-Key` albo wartość niebędąca UUID daje `400`
   (`idempotency_key_required`). API waliduje dane ponownie i normalizuje komendę:
   - przycina teksty, a pusty tekst zamienia na `null`;
   - z NIP zostawia same cyfry;
   - wartość zapisuje kanonicznie jako `0.00`;
   - listy zostawia w podanej kolejności, bez pustych pozycji;
   - słowniki zapisuje jako kody.

   Skrót żądania to SHA-256 z JSON tej znormalizowanej komendy, serializowanej ze stałą
   kolejnością właściwości. Dwa żądania różniące się tylko spacjami lub zapisem NIP mają ten
   sam skrót.
5. Handler woła `IUnitOfWork.ExecuteInTransactionAsync` z `commitWhen: result is Created`.
   W transakcji:
   1. szuka zamówienia o tym `CreationRequestId`; jeżeli istnieje, wynik to `Existing(id,
      skrót)` i nic nie jest zapisywane;
   2. nadaje numer;
   3. zapisuje zamówienie;
   4. zapisuje audyt `Created`.
6. Wyścig dwóch żądań z tym samym kluczem: drugie trafia na unikalny indeks
   `CreationRequestId` (`2601`). Port zapisu zwraca wtedy wynik `DuplicateRequest` i nie
   rzuca wyjątku. Jednostka pracy wycofuje transakcję, w tym przydzielony numer. Po
   wycofaniu handler czyta zamówienie po kluczu w nowym odczycie, a gdy go nie znajdzie,
   traktuje sytuację jak błąd programu. Indeksy odróżnia się po tym, czy po wycofaniu istnieje
   zamówienie z danym kluczem, a nie po tekście komunikatu SQL. Duplikat `Number` nie jest
   tłumaczony (patrz Storage).
7. Wynik dla istniejącego zamówienia, z kroku 5.1 albo 6:
   - ten sam skrót daje `200` z `{ id, number, version }` aktualnego stanu zamówienia;
   - inny skrót daje `409` (`idempotency_key_reused`) z rozszerzeniami Problem Details
     `orderId` i `orderNumber`.

   W obu przypadkach nie powstaje nowy rekord.
8. Po sukcesie (`201` albo `200`) UI przechodzi do szczegółów zamówienia z komunikatem
   „Zamówienie zapisane”. Przy `409 idempotency_key_reused` UI nie generuje nowego klucza
   automatycznie:
   - pokazuje komunikat „Ten formularz zapisał już zamówienie {numer}” z linkiem do niego;
   - akcja „Dodaj kolejne zamówienie” czyści formularz i generuje nowy klucz.

   Zapis zamówienia nie tworzy żadnej wpłaty ani wpływu.

### Lista

1. `/zamowienia` pobiera jedną stronę z API. Domyślnie sortuje po dacie zamówienia malejąco,
   z rozstrzygnięciem remisu po `id`, i pokazuje 25 wierszy na stronę.
2. **Kolumny:** numer (link do szczegółów), data, klient, kanał, źródło („Ręczne” albo
   „Sklep”), wartość brutto, status realizacji.
3. **Filtry:**
   - okres daty zamówienia, od–do, oba końce włącznie;
   - kanał i źródło;
   - status realizacji, wielokrotny wybór; w zapytaniu powtarzany parametr
     `status=inProgress&status=problem`;
   - wyszukiwanie po numerze i nazwie klienta: „zawiera”, bez rozróżniania wielkości liter
     (collation bazy), znaki `%`, `_`, `[` traktowane dosłownie.

   `total` liczy osobne zapytanie `COUNT` z tymi samymi filtrami.
   - Domyślnie widoczne są wszystkie statusy, także „Anulowane”, które ma swój znacznik.
   - Każda zmiana filtra wraca do pierwszej strony.
4. **Sortowanie:** data, numer, klient, wartość.
5. Lista nie pokazuje kolumn „Zapłacono” ani „Stan płatności” (dodaje je SF-9).

### Szczegóły i edycja

1. `/zamowienia/:id` pokazuje:
   - wszystkie pola;
   - źródło i zewnętrzne ID jako tylko do odczytu;
   - status ze sklepu osobno, jeżeli jest;
   - techniczne czasy utworzenia i ostatniej zmiany w strefie Europe/Warsaw;
   - stan „Anulowane” jako znacznik.
2. „Edytuj” przełącza sekcje w tryb formularza.
   - Zapis wysyła pełny zestaw pól edytowalnych razem z `version` odczytaną przy wczytaniu.
   - Pola należące do sklepu w zamówieniu WooCommerce są tylko do odczytu i mają opis
     ograniczenia.
3. Handler sprawdza w tej kolejności, w jednej jednostce pracy:
   1. walidację danych wejściowych (`400`); pola należące do sklepu w zamówieniu WooCommerce
      nie są walidowane, bo są tylko porównywane z zapisanymi;
   2. istnienie zamówienia (`404`);
   3. `version` z żądania względem zapisanej (`409 order_conflict`); dotyczy to także edycji
      bez zmian;
   4. reguły domeny (`422`, patrz niżej);
   5. wyliczenie zmienionych pól. Gdy nic się nie zmieniło, wynik to sukces z niezmienioną
      `version`, bez zapisu i bez audytu.
   6. zapis i audyt `Updated` (tylko zmienione pola, wartości stare i nowe).
4. Kontrola `version`: przed `SaveChanges` port ustawia
   `Entry(order).Property(Version).OriginalValue` na wersję przysłaną przez klienta, więc
   `UPDATE … WHERE Version = @klienta` nie zmienia wiersza zmienionego w międzyczasie.
   `DbUpdateConcurrencyException` port zamienia na wynik konfliktu (`409 order_conflict`).
   Ten sam mechanizm działa przy `cancel` i `restore`.
5. **Reguły statusu w `PUT`:**
   - w zamówieniu nieanulowanym `fulfillmentStatus` przyjmuje `inProgress`, `completed`
     i `problem`; wartość `cancelled` daje `422 status_change_requires_action`;
   - w zamówieniu anulowanym `fulfillmentStatus` musi być `cancelled` (bez zmiany). Każda
     inna wartość daje `422 status_change_requires_action`. Pozostałe pola zapisują się
     normalnie;
   - UI w zamówieniu anulowanym pokazuje status jako tylko do odczytu („Anulowane”) z opisem,
     że zmienia go akcja „Przywróć zamówienie”, i wysyła `cancelled`.
6. **Pola należące do sklepu w `PUT`** (zamówienie `Source = WooCommerce`): `orderDate`,
   `value`, `channel` (zawsze „sklep”) i `customer`.
   - W żądaniu są opcjonalne. Gdy ich brak albo są równe zapisanym, są ignorowane; gdy się
     różnią, API zwraca `422 field_owned_by_shop` z nazwami pól.
   - `shopOwnedFields` w `OrderDetails` wymienia je, żeby UI pokazało je jako tylko do
     odczytu.
   - W zamówieniu ręcznym te pola są wymagane jak przy tworzeniu.
7. Jeżeli `version` nie zgadza się z zapisaną, API zwraca `409` (`order_conflict`) i niczego
   nie zmienia. UI pokazuje „Zamówienie zostało zmienione w międzyczasie” i:
   - zostawia wpisane dane w formularzu;
   - proponuje „Wczytaj aktualne dane”, co zastępuje formularz stanem z serwera.
8. W zamówieniu anulowanym można edytować dane, ale statusu nie można zmienić inaczej niż
   akcją „Przywróć zamówienie” (punkt 5). Jest to egzekwowane w domenie.

### Anulowanie i przywrócenie

1. „Anuluj zamówienie” jest widoczne dla zamówienia, które nie jest anulowane.
   - Dialog potwierdzenia wskazuje numer zamówienia i skutek: zamówienie wypada ze sprzedaży
     w okresie daty zamówienia; nie można go usunąć, można je przywrócić.
   - Po potwierdzeniu API ustawia `Cancelled`, a audyt `Updated` zapisuje
     `fulfillmentStatus: {stary} → cancelled`. Obie zmiany idą w jednej transakcji z kontrolą
     `version`.
2. „Przywróć zamówienie” jest widoczne tylko dla anulowanego zamówienia. Po potwierdzeniu
   ustawia `InProgress` i zapisuje audyt w ten sam sposób.
3. Anulowanie zamówienia już anulowanego albo przywrócenie nieanulowanego zwraca `409`
   (`order_already_cancelled` / `order_not_cancelled`). Taka sytuacja zdarza się przy
   nieaktualnym widoku.
4. Kolejność sprawdzeń w `cancel`/`restore`: `404`, potem `version` (`409 order_conflict`),
   potem stan (`409 order_already_cancelled` / `order_not_cancelled`).
5. Usunięcia nie ma. `DELETE /api/orders/{id}` nie jest zmapowany. Odpowiedź zależy od
   wcześniejszych warstw: bez sesji `401`, bez XSRF odrzucenie przez ochronę XSRF z SF-6,
   a z sesją i XSRF odpowiedź frameworka `405`. W każdym przypadku dane zostają bez zmian.

## Data / API

### Storage

Schemat `orders`, nowa migracja `OrdersAndNumbering`.

- **Tabela `orders.Orders`:**
  - `Id uniqueidentifier` PK;
  - `Number nvarchar(32)` NOT NULL, unikalny indeks;
  - `Source nvarchar(16)` (enum jako string), `ExternalId nvarchar(64)` NULL:
    - unikalny filtrowany indeks `(Source, ExternalId) WHERE ExternalId IS NOT NULL`
      (przygotowanie pod niezmiennik 4);
    - check constraint: `Source = 'Manual'` ⇔ `ExternalId IS NULL`;
  - `Channel nvarchar(16)`, `OrderDate date`;
  - `Value decimal(18,2)` NOT NULL z check constraint `Value >= 0` (PRD §8 „Reguły kwot”);
  - `FulfillmentStatus nvarchar(16)`, `ShopStatus nvarchar(32)` NULL;
  - kolumny klienta: `CustomerKind nvarchar(16)`, `CustomerName nvarchar(200)` NOT NULL,
    `CustomerPhone nvarchar(32)`, `CustomerEmail nvarchar(254)`, `CustomerStreet nvarchar(200)`,
    `CustomerPostalCode nvarchar(16)`, `CustomerCity nvarchar(100)`, `CustomerTaxId nvarchar(16)`
    (wszystkie poza nazwą i rodzajem NULL);
  - dane operacyjne:
    - `Suppliers nvarchar(4000)` (lista JSON, do 10 pozycji po 100 znaków; zapas na cudzysłowy
      i znaki ucieczki, serializacja z `JavaScriptEncoder.Create(UnicodeRanges.All)` jak w SF-7);
    - `ZoReferences nvarchar(2000)` (lista JSON, do 10 pozycji po 64 znaki);
    - limity długości pozycji i liczby pozycji sprawdza walidacja przed zapisem, więc baza nigdy
      nie obcina danych;
    - `InvoiceNumber nvarchar(64)`;
    - `HasPhotos bit`, `HasReview bit`;
    - `Notes nvarchar(2000)`;
  - `Comment nvarchar(2000)`;
  - `CreationRequestId uniqueidentifier` NULL, unikalny filtrowany indeks, i
    `CreationRequestHash binary(32)` NULL;
  - `CreatedAtUtc datetime2`, `UpdatedAtUtc datetime2`, `Version rowversion` (token
    współbieżności);
  - indeksy: `(OrderDate DESC, Id)`, `(Channel)`, `(FulfillmentStatus)`.
- **Tabela `orders.OrderNumberCounters`:** `Year int` PK, `LastNumber int`. Numer nadaje port
  `IOrderNumberAllocator` w transakcji jednostki pracy, przez `OrdersDbContext` (interceptor
  z SF-7 dołącza polecenie do aktywnej transakcji):
  1. `UPDATE orders.OrderNumberCounters WITH (UPDLOCK, HOLDLOCK) SET LastNumber = LastNumber + 1
     OUTPUT inserted.LastNumber WHERE Year = @year`;
  2. gdy nie zmieniono wiersza (nowy rok): `INSERT (Year, LastNumber) VALUES (@year, 1)`;
  3. gdy `INSERT` trafi na naruszenie PK (`2627`, wyścig dwóch pierwszych zamówień roku),
     port powtarza krok 1 jeden raz.

  Rollback jednostki pracy cofa też licznik, więc po wycofanym zapisie numer nie przepada
  i numeracja nie ma luk. Unikalny indeks na `Number` jest ostatnią obroną; jego naruszenie
  oznacza błąd programu i, zgodnie z udokumentowaną rozbieżnością `DbException → 503`
  w konwencjach, kończy się `503` (to samo dotyczy check constraints, które walidacja domeny
  wyklucza przed zapisem).
- **Migracja:** `OrdersAndNumbering` (w module jest już `InitialSchema`).
- Audyt zapisuje się w `reporting.AuditEntries` przez `IAuditLog` (SF-7). Moduł Orders nie
  dotyka tej tabeli.

### Endpoints

Wszystkie wymagają ważnej sesji (SF-6). `POST` i `PUT` wymagają nagłówka `X-XSRF-TOKEN`.

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| GET | `/api/orders` | query: `page` (≥1), `pageSize` (1–100, domyślnie 25), `sort` (`orderDate`, `number`, `customer`, `value`), `direction` (`asc`/`desc`), `dateFrom`, `dateTo`, `channel`, `source`, `status` (wielokrotny), `search` (≤100 znaków) | `200 OrderListPage { items: OrderListItem[], page, pageSize, total }`; `400` przy złych parametrach | sesja | odczyt |
| GET | `/api/orders/{id}` | — | `200 OrderDetails`; `404` | sesja | odczyt |
| POST | `/api/orders` | nagłówek `Idempotency-Key` (UUID, wymagany); body `OrderInput` | `201 { id, number, version }` + `Location`; `200 { id, number, version }` (aktualna `version`) przy powtórzeniu; `400` walidacja lub `idempotency_key_required`; `409 idempotency_key_reused` z `orderId`, `orderNumber` | sesja + XSRF | zapis |
| PUT | `/api/orders/{id}` | body `OrderInput` + `version` | `200 { version }`; `400`; `404`; `409 order_conflict`; `422 field_owned_by_shop` (z listą pól), `422 status_change_requires_action` | sesja + XSRF | zapis |
| POST | `/api/orders/{id}/cancel` | body `{ version }` | `200 { version }`; `404`; `409 order_conflict`, `409 order_already_cancelled` | sesja + XSRF | zapis |
| POST | `/api/orders/{id}/restore` | body `{ version }` | `200 { version }`; `404`; `409 order_conflict`, `409 order_not_cancelled` | sesja + XSRF | zapis |

Błędy mają postać Problem Details z `code`, zgodnie z tabelą klas błędów w konwencjach.
- Infrastruktura tłumaczy `DbUpdateConcurrencyException` na wynik konfliktu, a naruszenie
  unikalności (`2601`/`2627`) na `CreationRequestId` na wynik idempotentny.
- Każde z tych tłumaczeń ma test, który sprawdza zwracany kod HTTP.

### Types crossing a boundary

Kwoty idą w JSON jako string dziesiętny z kropką (`"1234.50"`), a daty jako `yyyy-MM-dd`.
Słowniki przechodzą jako stabilne kody camelCase (`inProgress`, `olx`), a frontend mapuje je na
polskie etykiety.

- **`OrderInput`:**
  - `orderDate`, `channel`, `value`;
  - `fulfillmentStatus`: w `POST` opcjonalny, domyślnie `inProgress`; `cancelled` daje `400`;
    w `PUT` wymagany, reguły w Behavior;
  - `customer { kind (person|company), name, phone?, email?, street?, postalCode?, city?,
    taxId? }`;
  - `operational { suppliers[], zoReferences[], invoiceNumber?, hasPhotos, hasReview, notes? }`;
  - `comment?`.
- **Klucze błędów walidacji** w `ValidationProblemDetails` odpowiadają ścieżkom JSON:
  `value`, `customer.name`, `customer.taxId`, `operational.suppliers[0]`. Frontend przypina
  błąd do pola po tym kluczu.
- **`OrderListItem`:** `id`, `number`, `orderDate`, `customerName`, `channel`, `source`,
  `value`, `fulfillmentStatus`.
- **`OrderDetails`:** pola `OrderInput` oraz `id`, `number`, `source`, `externalId`,
  `shopStatus`, `createdAtUtc`, `updatedAtUtc`, `version` (base64 z `rowversion`) i
  `shopOwnedFields: string[]` (pusta dla zamówień ręcznych).
- **Walidacja:**
  - `value` ≥ 0, najwyżej 2 miejsca po przecinku i ≤ 9 999 999 999 999 999,99, czyli zakres
    `decimal(18,2)`;
  - dostawcy: do 10 pozycji po 1–100 znaków; referencje ZO: do 10 pozycji po 1–64 znaki;
    `invoiceNumber` ≤ 64; `notes` i `comment` ≤ 2000; telefon ≤ 32; ulica ≤ 200;
    miasto ≤ 100;
  - `name` od 1 do 200 znaków po przycięciu;
  - `email` w poprawnym formacie, gdy podany;
  - `taxId` (NIP) ma 10 cyfr z poprawną sumą kontrolną, gdy podany; myślniki i spacje są
    usuwane przed zapisem;
  - `postalCode` w formacie `NN-NNN`, gdy podany;
  - `orderDate` jest wymagana;
  - nieznane kody słowników są odrzucane.
- **Audyt (SF-7, konwencje „Value format”):**
  - `AuditObject(Order, id, orderDate, number)`;
  - nazwy pól: `orderDate`, `channel`, `value`, `fulfillmentStatus`, `customerKind`,
    `customerName`, `customerPhone`, `customerEmail`, `customerStreet`, `customerPostalCode`,
    `customerCity`, `customerTaxId`, `suppliers`, `zoReferences`, `invoiceNumber`, `hasPhotos`,
    `hasReview`, `notes`, `comment`;
  - pola `customer*` poza `customerKind` mają `IsPersonalData = true`;
  - etykieta obiektu to numer zamówienia (bez danych osobowych);
  - wartości:
    - kwota `1234.50`;
    - data `yyyy-MM-dd`;
    - słowniki jako kody (`inProgress`, `olx`, `person`);
    - wartości logiczne `true`/`false`;
    - listy jako tablica JSON w tekście (`["Dostawca A","Dostawca B"]`);
    - brak wartości `null`;
  - przy `Created` stara wartość każdego pola to `null`. Pomijane są pola, których nowa
    wartość to `null` albo pusta lista; wartości logiczne zapisuje się zawsze, także `false`;
  - przy `Updated` lista jest zmieniona, gdy różni się zawartością lub kolejnością.

## Invariant impact

| Invariant | How it is respected |
|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | Zamówienie nie tworzy wpłaty ani wpływu, a model nie ma pola „opłacone”. Stan płatności nie jest polem: wyliczy go SF-9. Test: utworzenie zamówienia nie dodaje żadnego rekordu pieniężnego, a odpowiedź nie zawiera stanu płatności. |
| 2. Kwoty decimal na backendzie | `Value` to `decimal(18,2)` w domenie i bazie (PRD §8 „Reguły kwot”). API przyjmuje i zwraca kwoty jako string dziesiętny, frontend tylko formatuje i nie liczy. Typ zmiennoprzecinkowy nie występuje w modelu. |
| 3. Dostęp egzekwowany przez API | Wszystkie endpointy wymagają sesji, a zapisy XSRF. Testy integracyjne: anonimowy `GET`, `POST`, `PUT`, `cancel` i `restore` dostają `401`. |
| 4. Ponowienia nie duplikują | Ręczne: klucz idempotencji z unikalnym `CreationRequestId`. Pod integrację: unikalny filtrowany indeks `(Source, ExternalId)`. Test: dwa równoległe `POST` z tym samym kluczem dają jeden rekord. |
| 5. Audyt finansowy | Utworzenie, edycja, anulowanie i przywrócenie wołają `IAuditLog` w tym samym `IUnitOfWork`. Nieudany audyt cofa zmianę. Usuwania nie ma. Zmiany danych klienta są oznaczone jako osobowe. |
| 6. Sekrety po stronie serwera | Nie dotyczy nowych sekretów. Logi nie zawierają danych klienta (logowany jest tylko `id` i numer). |
| 7. Granice modułów i warstw | Orders posiada `orders.*`. Audyt idzie przez `IAuditLog` z `Shared.Application`. Domena nie zależy od EF ani HTTP, a odczyty zwracają DTO przez porty. |
| 8. Brak danych identyfikujących w dokumentach | Przykłady i testy używają fikcyjnych klientów („Jan Testowy”, NIP testowy z poprawną sumą kontrolną), bez nazwy sklepu. |

## Risk

**high.** Zadanie dotyczy danych finansowych (wartość zamówienia jest podstawą sprzedaży),
danych osobowych klienta, kontroli współbieżności i idempotencji. Wymaga przeglądu człowieka
przed merge.

## UI

- **Trasy (lazy, w powłoce):** `/zamowienia` (lista), `/zamowienia/nowe` (formularz),
  `/zamowienia/:id` (szczegóły i edycja).
- **Komponenty:**
  - `orders-list-page` korzysta z `app-data-table` i filtrów w `tableFilters`;
  - `order-form` jest wspólny dla dodawania i edycji i grupuje sekcje: „Klient”, „Zamówienie”
    (data, kanał, wartość brutto), „Realizacja” (status, dostawcy, ZO, faktura, flagi) oraz
    „Uwagi”;
  - `order-details-page` używa `app-status-badge` dla statusu realizacji i znacznika źródła,
    a `app-confirm-dialog` dla anulowania i przywrócenia;
  - przyciski to `app-button` (`busy` przy zapisie i akcjach), a pola są osadzone w
    `app-field`;
  - formularze używają Signal Forms;
  - `orders-api.ts` i `orders.service.ts` korzystają z sygnałów.
- **Stany:**
  - lista ma ładowanie, błąd z „Spróbuj ponownie” i stan załadowany;
  - pusty stan bez filtrów: strona zamiast tabeli renderuje `app-empty-state` („Brak
    zamówień”), a pod nim przycisk „Dodaj zamówienie”. Akcja leży na stronie, bo
    `app-data-table` nie ma miejsca na akcję w pustym stanie;
  - pusty stan z filtrami („Brak wyników dla filtrów” z „Wyczyść filtry”) pokazuje tabela,
    a przycisk leży w filtrach;
  - formularz po nieudanym zapisie pokazuje nad formularzem podsumowanie błędów z linkami do
    pól (design guide, formularze) i przenosi fokus na to podsumowanie;
  - szczegóły mają ładowanie, `404` („Nie znaleziono zamówienia”), błąd i stan załadowany;
  - formularz ma błędy pól z API przypięte do pól, konflikt `409` z akcją „Wczytaj aktualne
    dane” i stan „Zapisywanie…”.
- **Akcje główne:** na liście „Dodaj zamówienie”, w formularzu „Zapisz”, w szczegółach
  „Edytuj”. „Anuluj zamówienie” to `app-button` w wariancie `danger`, bo zmiana wyłącza
  zamówienie ze sprzedaży. „Przywróć zamówienie” to wariant `secondary`. Kolory pochodzą tylko z tokenów `--sf-*` i motywu `stone-flow`
  (`lint:colors`).
- **Formatowanie:** kwoty i daty tylko przez `shared/format/`. Pole wartości przyjmuje polski
  zapis (`1 234,50`) i wysyła `"1234.50"`, a konwersja odbywa się na tekście, bez arytmetyki
  zmiennoprzecinkowej.
- **Dostępność (WCAG 2.2 AA):**
  - etykiety pól z oznaczeniem wymagalności, błędy powiązane przez `aria-describedby`;
  - po nieudanym zapisie fokus trafia na podsumowanie błędów, którego linki prowadzą do pól;
  - dialog przechwytuje fokus i oddaje go elementowi, który go otworzył;
  - nagłówki tabeli są sortowalne z klawiatury i ogłaszają `aria-sort`;
  - komunikat sukcesu i konfliktu jest w regionie `aria-live`;
  - na ekranach poniżej 768 px formularz ma jedną kolumnę, a strona nie przewija się poziomo.

## Edge Cases

- Podwójne kliknięcie „Zapisz” blokuje przycisk `busy`. Gdyby mimo to doszło do dwóch żądań
  z tym samym kluczem, jedno zapisuje, a drugie dostaje `200` z tym samym `id` (unikalny
  indeks).
- Utrata połączenia po wysłaniu formularza: ponowienie z tym samym kluczem zwraca istniejące
  zamówienie zamiast tworzyć nowe.
- Ten sam klucz z innymi danymi daje `409 idempotency_key_reused` z numerem istniejącego
  zamówienia. UI nie tworzy nowego klucza samo, tylko pokazuje link do zamówienia i akcję
  „Dodaj kolejne zamówienie” (Behavior, punkt 8).
- Ten sam klucz i dane różniące się tylko spacjami lub zapisem NIP dają po normalizacji ten sam
  skrót, więc `200`, a nie `409`.
- Wartość 0 zł jest przyjmowana, a wartości ujemne, `1,234` (3 miejsca po przecinku), tekst
  i pusta wartość są odrzucane.
- Dwa zapisy numeru w tym samym roku jednocześnie dostają kolejne numery bez duplikatu.
  Dwa pierwsze zamówienia nowego roku jednocześnie: jedno wstawia licznik, a drugie po `2627`
  powtarza `UPDATE` i dostaje `0002`. Po przełomie roku numeracja zaczyna się od
  `ZR-{nowy rok}-0001` według roku z daty utworzenia w strefie Europe/Warsaw (`TimeProvider`),
  a nie roku daty zamówienia.
- Zapis wycofany przez błąd audytu nie zużywa numeru: następne zamówienie dostaje ten sam
  numer.
- Edycja zamówienia ze sklepu wysyłająca pola sklepu równe zapisanym przechodzi, a różniące
  się dają `422`. Kanał zamówienia ze sklepu jest tylko do odczytu.
- Edycja zamówienia anulowanego z `fulfillmentStatus: cancelled` zapisuje pozostałe zmiany,
  a z innym statusem daje `422 status_change_requires_action`.
- `Idempotency-Key` pusty albo niebędący UUID daje `400 idempotency_key_required`.
- Edycja zamówienia, które w międzyczasie zostało anulowane w innej karcie, daje
  `409 order_conflict`.
- Anulowanie z dwóch kart: pierwsze się udaje, drugie dostaje `409 order_conflict` (inna
  `version`).
- Wyszukiwanie znaków specjalnych SQL (`%`, `_`, `[`) jest traktowane dosłownie.
- Okres filtra z `dateFrom > dateTo` daje `400`.
- `pageSize > 100` daje `400`. Strona poza zakresem zwraca pustą listę z prawdziwym `total`.
- Zmiana daty zamówienia ręcznego przenosi sprzedaż do innego okresu, a audyt zapisuje starą
  i nową datę. Data biznesowa wpisu audytu to nowa data zamówienia.
- Data sprzed daty uruchomienia jest dozwolona (przełom uruchomienia, URU-01). Przyszła data
  też jest przyjmowana, bo PRD nie zabrania.
- `PUT` dla zamówienia ze sklepu zmieniający wartość daje `422 field_owned_by_shop`. Ścieżka
  jest testowana w domenie i w teście integracyjnym na zamówieniu WooCommerce zasianym
  bezpośrednio w bazie.

## Dependencies

- **SF-6** (sesja, XSRF, ochrona API): zrobione.
- **SF-7** (`IUnitOfWork`, `IAuditLog`, `AuditObjectType.Order`): zrobione.

## Assumptions

Odpowiedzi użytkownika z 05.11.2026:
- **Wartość:** ≥ 0 zł; ujemna odrzucana, 0 zł dozwolone (PRD: zamówienie 0 zł jest
  Opłacone).
- **Data zamówienia ręcznego:** edytowalna z audytem. W zamówieniu ze sklepu jest tylko do
  odczytu.
- **„Anulowane”:** tylko przez akcje „Anuluj zamówienie” i „Przywróć zamówienie”; nie ma go
  w polu statusu.
- **Słowniki:** stałe w kodzie, zmiana wymaga wdrożenia.
- **Filtr i kolumny stanu płatności:** przeniesione do SF-9.
- **Kanał „sklep”:** dozwolony dla zamówienia ręcznego, np. przy przełomie uruchomienia.
  Źródło techniczne nadal pokazuje „Ręczne”.
- **Numer:** `ZR-{rok}-{NNNN}`, kolejny w roku, unikalny. Zamówienia ze sklepu dostaną własny
  numer w SF-12 (poza tym zadaniem).
- **Dane klienta:** wymagane są tylko rodzaj i nazwa (imię i nazwisko albo nazwa firmy).
  Telefon, e-mail, adres i NIP są opcjonalne; NIP i kod pocztowy są walidowane, gdy są
  podane.

Założenia autora specyfikacji:
- **Granica z SF-10:** „Korekta wartości zamówienia ręcznego z audytem” w SF-8 to bezpośrednia
  edycja pola wartości. Limit obniżeń sprzedaży przy korekcie wartości dodaje SF-10, bo
  dopiero tam powstają zwroty sprzedaży i korekty.
- **PLAT-06 w SF-8** obejmuje tylko część „korekta wartości zmienia sprzedaż w okresie daty
  zamówienia i zostawia audyt”. Sprzedaż liczy SF-19; tu zapisujemy wartość i audyt.
- **Ostrzeżenie o wpłatach przy anulowaniu** dodaje SF-9. W SF-8 dialog nie ma tego
  ostrzeżenia, bo wpłat jeszcze nie ma.
- **Edycja anulowanego zamówienia:** dozwolona. Zakazana jest tylko zmiana statusu inaczej niż
  przez przywrócenie. PRD tego nie zabrania, a pozwala to poprawić dane zamówienia
  anulowanego.
- **Przywrócenie** zawsze ustawia „W trakcie”, zgodnie z PRD.
- **Klucz idempotencji:** obejmuje tylko tworzenie. Edycję, anulowanie i przywrócenie chroni
  `version`. Klucz jest przechowywany bezterminowo w wierszu zamówienia, bo nie ma potrzeby
  osobnego czyszczenia.
- **Wykrywanie innych danych przy tym samym kluczu:** skrót SHA-256 znormalizowanej komendy,
  według reguł z Behavior, punkt 4.
- **Rok numeru** pochodzi z czasu utworzenia w Europe/Warsaw, nie z daty zamówienia. Dzięki
  temu numer nie zależy od późniejszej zmiany daty.
- **Przyszła data zamówienia** jest przyjmowana, bo PRD jej nie zabrania. Data sprzed
  uruchomienia też (URU-01).
- **Kanał zamówienia ze sklepu** jest tylko do odczytu, bo PRD określa go jako „sklep”.
- **`409 idempotency_key_reused` zwraca `orderId` i `orderNumber`.** Przy utraconej
  odpowiedzi użytkownik trafia do istniejącego zamówienia, zamiast tworzyć duplikat nowym
  kluczem.
- **Rozbieżność `DbException → 503`** (konwencje) zostaje bez zmian. SF-8 tłumaczy tylko
  konflikt `rowversion`, duplikat klucza idempotencji i wyścig licznika. Pozostałe błędy
  ograniczeń, którym zapobiega walidacja, kończą się `503`.
- **Dostawcy i referencje ZO** to listy krótkich tekstów zapisane jako JSON w kolumnie. PRD
  nie przewiduje słownika dostawców (D-07).
- **Wyszukiwanie** obejmuje numer i nazwę klienta. Szukanie po telefonie lub e-mailu nie jest
  wymagane przez PRD.
- **Domyślna lista** pokazuje także anulowane zamówienia, ze znacznikiem. Ukrywanie dotyczy
  w PRD tylko statusów pending, failed i szkiców ze sklepu (SF-12).
- **Autor audytu:** endpoint odczytuje identyfikator konta z uwierzytelnionej sesji i przekazuje
  go w komendzie, a handler buduje `AuditAuthor.Account(accountId)`.
- **Liczba stron na liście:** `total` liczy osobne zapytanie `COUNT` z tymi samymi filtrami.
  Przy wolumenie 20–30 zamówień miesięcznie (D-08) nie potrzeba stronicowania kursorem.

## Acceptance Criteria

- [ ] `POST /api/orders` z poprawnymi danymi tworzy zamówienie ze źródłem `Manual`, numerem
  `ZR-{rok}-{NNNN}` i statusem „W trakcie” (lub wybranym), a zamówienie pojawia się na
  `GET /api/orders` i na liście w UI.
- [ ] Lista i szczegóły pokazują osobno źródło techniczne („Ręczne”/„Sklep”) i kanał. Ręczne
  zamówienie z kanałem „sklep” ma źródło „Ręczne”.
- [ ] Utworzenie zamówienia nie tworzy wpłaty ani żadnego rekordu pieniężnego, a odpowiedzi
  nie zawierają stanu płatności ani kwoty „Zapłacono”.
- [ ] Dwa żądania `POST` z tym samym `Idempotency-Key`, także równoległe, dają jeden rekord
  i ten sam `id`. Ten sam klucz z innymi danymi daje `409 idempotency_key_reused`.
  W UI podwójne kliknięcie „Zapisz” nie wysyła drugiego żądania.
- [ ] `PUT`, `cancel` i `restore` ze starą `version` zwracają `409 order_conflict` i niczego
  nie zmieniają. UI pokazuje konflikt i pozwala wczytać aktualne dane.
- [ ] `DELETE /api/orders/{id}` nie usuwa zamówienia, a UI nie ma akcji „Usuń”.
- [ ] „Anuluj zamówienie” i „Przywróć zamówienie” wymagają potwierdzenia w dialogu, zmieniają
  status na „Anulowane” / „W trakcie” i zapisują wpis audytu `Updated` z
  `fulfillmentStatus`. Akcja niezgodna ze stanem daje `409`.
- [ ] Pole statusu w formularzu nie zawiera „Anulowane”. API odrzuca
  `fulfillmentStatus: cancelled` w `POST` (`400`) i w `PUT` zamówienia nieanulowanego
  (`422 status_change_requires_action`).
- [ ] Zamówienie anulowane można edytować: `PUT` z `fulfillmentStatus: cancelled` zapisuje
  zmiany innych pól z audytem, a inny status daje `422 status_change_requires_action`.
  W UI status jest tylko do odczytu.
- [ ] Wycofany zapis (np. błąd audytu) nie zużywa numeru. Równoległe zapisy, także pierwsze
  w nowym roku, dostają różne kolejne numery.
- [ ] `PUT` z `version` zmienioną w międzyczasie nie nadpisuje danych, także gdy kolumny
  różnią się od zmienionych przez innego użytkownika (test z dwiema sesjami odczytu).
- [ ] Utworzenie zapisuje audyt `Created`, a edycja `Updated` tylko ze zmienionymi polami
  i starymi oraz nowymi wartościami. Pola klienta mają `IsPersonalData`, a etykieta to
  numer zamówienia. Edycja bez zmian nie zapisuje audytu.
- [ ] Nieudany zapis audytu cofa utworzenie lub zmianę zamówienia (test integracyjny).
- [ ] Korekta wartości i zmiana daty zamówienia ręcznego są zapisywane z audytem. W zamówieniu
  ze sklepu zmiana wartości, daty lub danych klienta daje `422 field_owned_by_shop`.
- [ ] Walidacja: wartość ujemna, ponad 2 miejsca po przecinku, brak nazwy klienta, zły NIP lub
  kod pocztowy i nieznany kod słownika dają `400` z błędami pól. Wartość 0 zł jest
  przyjmowana.
- [ ] Lista filtruje (okres, kanał, źródło, status, wyszukiwanie), sortuje (data, numer, klient,
  wartość, ze stabilnym rozstrzygnięciem po `id`) i stronicuje w bazie. `pageSize > 100` daje
  `400`.
- [ ] Bez sesji każdy endpoint zamówień zwraca `401`, a zapis bez XSRF jest odrzucany.
- [ ] Stany listy, szczegółów i formularza (ładowanie, pusto, błąd, konflikt, zapisywanie) są
  obsłużone, a kwoty i daty mają polskie formatowanie.

## Verification

- **build:** `dotnet build stone-flow.slnx -c Release` (0 ostrzeżeń), `npm run build`
  w `app/`.
- **tests:**
  - `dotnet test --solution stone-flow.slnx -c Release`; nowy projekt
    `api/tests/StoneFlow.Orders.Tests` musi być dodany do `stone-flow.slnx`. Zakres testów:
    - nowe testy domeny `StoneFlow.Orders.Tests` (walidacja, własność pól, anulowanie
      i przywracanie, wyliczanie zmienionych pól);
    - testy integracyjne `Api.IntegrationTests/Orders/` (autoryzacja, idempotencja
      równoległa, konflikt `rowversion`, numeracja, audyt i jego wycofanie, `DELETE`,
      filtrowanie i sortowanie);
  - `npm test` w `app/` (serwis, formularz, lista, konflikt);
  - `npm run typecheck`, `npm run lint` (zawiera `lint:colors`) i `npm run format:check`
    w `app/`;
  - `dotnet format stone-flow.slnx --verify-no-changes`.
- **manual:**
  - dodać zamówienie, podwójnie klikając „Zapisz”: na liście jest jeden rekord;
  - otworzyć zamówienie w dwóch kartach, zapisać w jednej, potem w drugiej: drugi zapis
    pokazuje konflikt;
  - anulować i przywrócić zamówienie: dialogi, status i dwa wpisy w
    `reporting.AuditEntries`;
  - sprawdzić listę z filtrami i sortowaniem na telefonie (375 px) i z klawiatury.
