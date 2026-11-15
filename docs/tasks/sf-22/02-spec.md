# Task Specification — SF-22

## Source

[SF-22](https://standev.youtrack.cloud/issue/SF-22)

## Goal

Dodać ręczną, nieodwracalną akcję „Anonimizuj dane klienta” w szczegółach zamówienia (PRD §5 „Anonimizacja danych klienta”, DANE-01). Akcja usuwa dane klienta i pola tekstowe, które mogą je zawierać:

- z zamówienia;
- z jego zwrotów i korekt sprzedaży;
- z wpisów audytu zamówienia i jego wpisów.

Kwoty, daty, wpłaty, zwroty, korekty, kanał, numer, źródło i ID WooCommerce zostają, więc raporty się nie zmieniają. Operacja zapisuje fakt anonimizacji na zamówieniu i własny wpis audytu bez danych osobowych. Synchronizacja WooCommerce (webhook, uzupełnianie, „Ponów”) nie przywraca potem danych klienta.

Długość okresu retencji pozostaje otwartą decyzją D-09. Zadanie jej nie rozstrzyga:

- okres to wartość konfiguracji wdrożenia;
- bez tej wartości funkcja jest wyłączona (fail-closed): UI jej nie udostępnia, a API ją odrzuca;
- kod nie zakłada domyślnej długości okresu w żadnym środowisku.

## Context

- **Backend, moduł Orders** (schemat `orders`):
  - `Orders.Domain/Orders/`:
    - `Order` dostaje fakt anonimizacji (`AnonymizedAtUtc`, `AnonymizedByAccountId`) i operację domenową anonimizacji klienta;
    - `ApplyFromShop` pomija dane klienta zamówienia zanonimizowanego;
    - `Edit` odrzuca zmianę klienta po anonimizacji;
    - nowy typ `CustomerAnonymization`: znacznik, początek okresu retencji, dostępność akcji.
  - `Orders.Domain/Payments/`: `Refund` i `SalesCorrection` dostają anonimizację powodu.
  - `Orders.Application/AnonymizeCustomer/AnonymizeCustomer.cs`: nowa komenda i handler w `IUnitOfWork.ExecuteInTransactionAsync` (wzorzec `OrderStatusAction`, blokada `IOrderLock` jak `OrderEntryWriter`).
  - `Orders.Application/Abstractions/Ports.cs`:
    - nowe porty `IAnonymizationSettings` (okres z konfiguracji) i `IOrderAnonymizationStore` (fakty retencji, wpisy z powodem do zmiany);
    - `OrderDetailsDto` dostaje fakty retencji i fakt anonimizacji.
  - `Orders.Application/GetOrder/`: zapytanie wylicza stan akcji regułą domeny.
  - `Orders.Application/OrderAnonymizationAudit.cs`: lista pól audytu do zanonimizowania i wpis operacji `Anonymized`.
  - `Orders.Infrastructure`:
    - `Options/AnonymizationOptions.cs` (sekcja `StoneFlow:Anonymization`, walidacja przy starcie);
    - implementacje portów;
    - `OrderConfiguration` z dwiema kolumnami i ograniczeniem;
    - migracja `CustomerAnonymization`.
  - `Orders.Api`:
    - `POST /api/orders/{id}/anonymize`;
    - kody problemów w `OrderProblems`;
    - pole `anonymization` w `OrderDetailsResponse`;
    - nowy wynik edycji `CustomerAnonymized`.
- **Kontrakt współdzielony** (`Shared.Application/Audit`, `Shared.Kernel/Audit`):
  - nowy port `IAuditRedaction` (anonimizacja wybranych pól we wpisach obiektu i jego obiektów podrzędnych, w transakcji jednostki pracy);
  - nowa operacja `AuditOperation.Anonymized`.
- **Backend, moduł Reporting** (schemat `reporting`, bez zmiany schematu):
  - `Reporting.Domain/Audit/AuditRecord` dostaje metodę `Redact`. To jedyna dozwolona zmiana zapisanego wpisu; komentarz „Wpis jest tylko dodawany” zostaje doprecyzowany;
  - `Reporting.Infrastructure/Audit/` implementuje `IAuditRedaction`;
  - `ChangeHistoryCodes` dostaje kod operacji `anonymized`.
- **Moduł Integrations:** bez zmian kodu. Webhook, uzupełnianie i „Ponów” zmieniają zamówienia tylko przez `IShopOrderSync` (`ShopOrderSync` w Orders) → `Order.ApplyFromShop`. Blokada przywracania danych w domenie obejmuje więc wszystkie trzy ścieżki. Historia błędów (`integrations.WooCommerceOrderErrors`) nie przechowuje danych klienta (Assumptions pkt 8).
- **Frontend:**
  - `features/orders/`:
    - `order-models.ts`: stan anonimizacji w `OrderDetails`;
    - `orders-api.ts`: `anonymize(id, version)`;
    - `orders.service.ts`: `anonymize` z mapowaniem kodów błędów;
    - `order-details-page`: przycisk, stany, dialog potwierdzenia, oznaczenie;
    - `order-form`: blokada pól klienta po anonimizacji.
  - `features/change-history/`: etykieta i ton operacji `anonymized`, pola wpisu operacji w `AUDIT_FIELD_CATALOG`, wyświetlanie tylko nowych wartości.
- **Dokumentacja i konfiguracja:**
  - `docs/operations/anonimizacja.md` (nowy);
  - `docs/operations/kopie-i-odtworzenie.md`;
  - `deploy/api.env.example`;
  - `.sdd/context/conventions.md` (audyt: wyjątek od „tylko dodawane”).

## Scope

### In scope

- Konfiguracja okresu retencji `StoneFlow:Anonymization:RetentionMonths`:
  - liczba całkowita 1–1200 albo brak;
  - wartość spoza zakresu albo nieliczbowa zatrzymuje start aplikacji;
  - brak wartości wyłącza funkcję.
- Reguła domenowa początku okresu retencji i dostępności akcji, z testami jednostkowymi.
- Operacja anonimizacji:
  - zamówienie, zwroty i korekty sprzedaży, skróty żądań idempotencji;
  - wpisy audytu zamówienia i jego wpłat, zwrotów i korekt (także wpisy usunięcia);
  - nowy wpis audytu `Anonymized`;
  - wszystko w jednej transakcji.
- `POST /api/orders/{id}/anonymize` oraz stan akcji w `GET /api/orders/{id}`.
- Blokada przywracania danych klienta przez synchronizację i blokada zmiany klienta w edycji.
- UI: przycisk w szczegółach, stany niedostępności z wyjaśnieniem, dialog z informacją o nieodwracalności, oznaczenie zanonimizowanego zamówienia, blokada pól klienta w formularzu, wpis w „Historii zmian”.
- Testy jednostkowe, integracyjne (SQL Server) i frontendu, w tym porównanie raportów przed anonimizacją i po niej.
- Opis włączenia funkcji po rozstrzygnięciu D-09, zakresu usuwanych danych, kopii zapasowych i logów.
- Scenariusze testów ręcznych UI w `06-implementation-done.md`.

### Out of scope

- Rozstrzygnięcie D-09, czyli długości okresu retencji i ewentualnego liczenia od końca roku (wymaga księgowego lub prawnika).
- Automatyczna anonimizacja po upływie okresu i anonimizacja wielu zamówień naraz.
- Anonimizacja danych spoza zamówień: wydatków (kontrahent, płatnik, komentarz), wypłat właścicieli (osoba) oraz pola „Przyjął” wpłaty (Assumptions pkt 5).
- Usuwanie danych z kopii zapasowych i logów hostingu (Assumptions pkt 9).
- Wyszukiwanie zamówień po danych klienta sprzed anonimizacji.
- Testy E2E (Assumptions pkt 13).

## Behavior

### Konfiguracja okresu retencji

1. Okres retencji czyta sekcja `StoneFlow:Anonymization` (`AnonymizationOptions.RetentionMonths`, `int?`), rejestrowana przez `AddOptions<…>().BindConfiguration(…).ValidateOnStart()`, jak `WooCommerceOptions`.
2. Brak klucza → funkcja wyłączona. Wartość 1–1200 → funkcja włączona z tym okresem w miesiącach kalendarzowych. Wartość 0, ujemna, większa od 1200 albo nieliczbowa → aplikacja się nie uruchamia. Komunikat walidacji wskazuje klucz `StoneFlow:Anonymization:RetentionMonths` i zakres, bez innych wartości konfiguracji.
3. `appsettings.json` i `appsettings.Development.json` nie zawierają klucza. W każdym środowisku funkcja jest domyślnie wyłączona. Testy ustawiają wartość w swojej konfiguracji. Do testów ręcznych ustawia się zmienną środowiskową (sekcja Verification).
4. `IAnonymizationSettings.RetentionMonths` zwraca wartość albo `null`. Handler i zapytanie nie mają wartości zastępczej.
5. Po rozstrzygnięciu D-09 właściciel ustawia w środowisku produkcyjnym `StoneFlow__Anonymization__RetentionMonths=<liczba miesięcy>` i restartuje aplikację (procedura w `docs/operations/anonimizacja.md`). Zmiana wartości działa od restartu i nie zmienia zamówień już zanonimizowanych.

### Początek okresu retencji i dostępność

6. **Początek okresu retencji** (`CustomerAnonymization.RetentionStart`) to najpóźniejsza z dat:
   - data zamówienia;
   - najpóźniejsza data wpłaty, zwrotu albo korekty sprzedaży zamówienia (data biznesowa wpisu);
   - data ostatniej zmiany: data w Europe/Warsaw najpóźniejszego z `Order.UpdatedAtUtc` i `UpdatedAtUtc` wpisów zamówienia. Obejmuje anulowanie, przywrócenie, korektę wartości, edycję i zmianę ze sklepu, także dopisanie wpisu z wcześniejszą datą biznesową.

   Wpisy usunięte nie wchodzą do wyliczenia, bo ich wierszy już nie ma.
7. **Dostępne od** = początek okresu retencji + `RetentionMonths` miesięcy kalendarzowych (`DateOnly.AddMonths`: 31.01 + 1 miesiąc = ostatni dzień lutego). Akcja jest dostępna, gdy dziś w Europe/Warsaw (`WarsawTime.Today` z `TimeProvider`) ≥ „dostępne od”.
8. Stan akcji (`AnonymizationStatus`), sprawdzany w tej kolejności:
   1. `anonymized` — zamówienie jest zanonimizowane (niezależnie od konfiguracji);
   2. `disabled` — brak okresu retencji w konfiguracji;
   3. `retentionPending` — dziś < „dostępne od”;
   4. `available` — w pozostałych przypadkach.
9. `GET /api/orders/{id}` zwraca stan, „dostępne od”, początek okresu retencji (dla `retentionPending` i `available`) oraz czas anonimizacji (dla `anonymized`). Zapytanie niczego nie zmienia.

### Anonimizacja (`POST /api/orders/{id}/anonymize`)

10. Endpoint wymaga sesji i tokenu XSRF (globalnie). Przyjmuje `{ version }` zamówienia z odczytu szczegółów. Konto pochodzi z `ClaimTypes.NameIdentifier`, a jego brak daje `401`, jak w `StatusActionAsync`.
11. Handler `AnonymizeCustomerHandler` przed transakcją:
    1. Wersja niepoprawna → `400 ValidationProblem` (`OrderVersion.Validate`).
    2. `RetentionMonths` = `null` → `422 anonymization_disabled`. Brak zapisu i odczytu bazy.
12. W `IUnitOfWork.ExecuteInTransactionAsync`, zatwierdzanej tylko przy wyniku `Saved`:
    1. `IOrderLock.LockAsync(orderId)`. Brak zamówienia → `404 order_not_found`. Blokada szereguje operację z dodaniem, poprawą i usunięciem wpłat, zwrotów i korekt.
    2. `IOrderStore.LoadAsync(orderId)`. Wersja inna niż podana → `409 order_conflict`.
    3. Zamówienie już zanonimizowane → `409 order_already_anonymized`.
    4. `IOrderAnonymizationStore.GetRetentionFactsAsync(orderId)` czyta po blokadzie najpóźniejszą datę wpisu i najpóźniejszy `UpdatedAtUtc` wpisów. Reguła z pkt 6–7 z dzisiejszą datą: przed „dostępne od” → `422 retention_not_elapsed` z `availableFrom` (`yyyy-MM-dd`).
    5. `Order.AnonymizeCustomer(accountId, nowUtc)` (pkt 13).
    6. `IOrderAnonymizationStore.LoadReasonEntriesAsync(orderId)` wczytuje do zmiany wszystkie zwroty i korekty sprzedaży zamówienia. Każdy dostaje `AnonymizeReason(nowUtc)` (pkt 14).
    7. `IOrderStore.SaveAsync(order, version)`: jeden `SaveChanges` zapisuje zamówienie z kontrolą wersji i zmienione wpisy. Konflikt → `409 order_conflict` i wycofanie całości.
    8. `IAuditRedaction.RedactAsync` z selektorami pól z pkt 15 i znacznikiem `[zanonimizowano]` dla korzenia `Order/{id}`.
    9. `IAuditLog.RecordAsync(OrderAnonymizationAudit.Anonymized(...))` (pkt 16), po kroku 8, żeby nowy wpis nie podlegał anonimizacji.
    10. Wynik `Saved` z nową wersją zamówienia → `200 { version }`.

    Wyjątek w dowolnym kroku wycofuje całość: nie zostaje ani zmiana zamówienia, ani zmiana audytu, ani nowy wpis. Handler **nie** zapisuje `OrderAudit.Updated` ze zmienionymi polami, bo jego stare wartości zawierałyby usuwane dane.
13. `Order.AnonymizeCustomer`:
    - `Customer` → `new Customer(Kind, "[zanonimizowano]", null, null, null, null, null, null)`. Rodzaj klienta (osoba / firma) zostaje, bo nie identyfikuje osoby;
    - `Operational.Notes` → `null`; dostawcy, referencje ZO, numer faktury i flagi zostają;
    - `Comment` → `null`;
    - `CreationRequestHash` (zamówienie ręczne) → 32 bajty zerowe. Skrót SHA-256 liczony jest z treści zamówienia z danymi klienta, więc pozwalałby potwierdzić zgadywane dane. Ponowienie żądania utworzenia z tym samym kluczem dostaje wtedy istniejące `409 idempotency_key_reused`, bo skróty się nie zgadzają;
    - `AnonymizedAtUtc` = `nowUtc`, `AnonymizedByAccountId` = konto, `UpdatedAtUtc` = `nowUtc`;
    - zwraca listę wyczyszczonych pól (`OrderField`), czyli tych, które miały wartość inną niż wynik. Nazwa liczy się zawsze, bo zawsze miała wartość;
    - ponowne wywołanie → naruszenie `AlreadyAnonymized`, bez zmian.
14. `Refund.AnonymizeReason` i `SalesCorrection.AnonymizeReason`:
    - `Reason` → `[zanonimizowano]`, bo powód jest wymagany i niepusty;
    - `CreationRequestHash` → 32 bajty zerowe (skrót zawiera powód);
    - `UpdatedAtUtc` = `nowUtc`, gdy coś się zmieniło.

    Wpłaty się nie zmieniają (Assumptions pkt 5).
15. Selektory anonimizacji audytu (`OrderAnonymizationAudit.Fields`), czyli rodzaj obiektu i pole:
    - `order`: `customerName`, `customerPhone`, `customerEmail`, `customerStreet`, `customerPostalCode`, `customerCity`, `customerTaxId`, `notes`, `comment`;
    - `refund`: `reason`;
    - `salesCorrection`: `reason`.

    `IAuditRedaction` obejmuje wpisy, których obiekt to `Order/{id}` albo których obiekt nadrzędny to `Order/{id}`, wszystkich operacji (`Created`, `Updated`, `Deleted`), od każdego autora (konto, synchronizacja). W pasujących polach wartość stara i nowa różna od `null` → `[zanonimizowano]`, a `null` zostaje `null`. Bez zmian zostają: nazwa pola, flaga `personal`, pozostałe pola, autor, czas, obiekt, etykieta (numer zamówienia), data biznesowa i operacja. Wpis z danymi klienta zostaje więc jako ślad zmiany: widać, kto i kiedy zmienił dane klienta, ale nie widać jakie.
16. Wpis audytu operacji (`AuditOperation.Anonymized`, autor `AuditAuthor.Account`, obiekt `Order` z numerem jako etykietą i datą zamówienia jako datą biznesową). Zakres zmiany bez danych osobowych, każde pole z `old` = `null` i `personal` = `false`:
    - `anonymizedFields` — tablica JSON kodów pól zamówienia wyczyszczonych w pkt 13 (np. `["customerName","customerEmail","notes"]`);
    - `anonymizedReasons` — liczba zwrotów i korekt z powodem zmienionym w pkt 14, jako tekst (`"2"`, także `"0"`);
    - `retentionStartDate` — początek okresu retencji (`yyyy-MM-dd`);
    - `retentionMonths` — okres z konfiguracji użyty w operacji (`"60"`).
17. Logi: handler nie loguje niczego ponad standardowe logi żądań (identyfikator zamówienia w ścieżce, korelacja). Komunikaty błędów i wyjątków nie zawierają danych klienta.

### Po anonimizacji

18. **Synchronizacja.** `Order.ApplyFromShop` dla zamówienia zanonimizowanego nie przypisuje `Customer` z dostawy. Pozostałe pola sklepu (data, wartość, status ze sklepu, zwroty w sklepie, usunięcie) i jednorazowe przejście na „Anulowane” działają bez zmian. Dzięki temu:
    - lista zmian dla audytu nie zawiera pól klienta, więc wpis `Updated` od synchronizacji nie zawiera danych osobowych;
    - dostawa różniąca się tylko danymi klienta jest „bez zmian”, gdy `ShopModifiedAtUtc` się nie zmienia; nowsza data modyfikacji jest zapisywana jak dotąd, bez wpisu audytu;
    - dotyczy to webhooka, uzupełniania o 23:00, „Synchronizuj teraz” i „Ponów”, bo wszystkie idą przez `IShopOrderSync`;
    - zamówienie usunięte w sklepie i potem przywrócone też nie odzyskuje danych klienta.
19. **Edycja zamówienia (`PUT /api/orders/{id}`).** `Order.Edit` zamówienia zanonimizowanego:
    - podany `Customer` różny od zapisanego (znacznik i puste pola) → naruszenie `CustomerAnonymized` → `422 order_customer_anonymized`, bez zmian;
    - zamówienie ze sklepu najpierw przechodzi istniejące porównanie pól sklepu (`field_owned_by_shop`), tak jak dziś;
    - pozostałe pola ERP (status realizacji, dostawcy, ZO, faktura, flagi, uwagi, komentarz) i pola zamówienia ręcznego (data, kanał, wartość) można dalej zmieniać, z audytem i limitem obniżeń sprzedaży jak dotąd.
20. **Operacje finansowe** pozostają dostępne: dodanie, poprawa i usunięcie wpłaty, zwrotu i korekty sprzedaży, anulowanie i przywrócenie zamówienia. Nowe i poprawione wpisy mają powód wpisany przez użytkownika. Powód zanonimizowany przy poprawie wpisu pozostaje znacznikiem, chyba że użytkownik go zmieni.
21. **Odczyty:**
    - lista zamówień pokazuje `[zanonimizowano]` jako klienta; wyszukiwanie po dawnej nazwie nie znajduje zamówienia;
    - szczegóły pokazują znacznik, puste pola klienta i fakt anonimizacji;
    - raporty sprzedaży, przepływów, zestawienie składników sprzedaży i lista „Operacje” nie czytają danych klienta, uwag ani powodów (`OrderSalesComponentSource`, `OrderCashOperationSource`), więc ich wyniki się nie zmieniają.

## Data / API

### Storage

- **`orders.Orders`** (migracja `CustomerAnonymization` w `StoneFlow.Orders.Infrastructure`, expand — wymaga jej tylko nowy kod):
  - `AnonymizedAtUtc datetime2 NULL`;
  - `AnonymizedByAccountId uniqueidentifier NULL`;
  - `CK_Orders_Anonymization`: `([AnonymizedAtUtc] IS NULL AND [AnonymizedByAccountId] IS NULL) OR ([AnonymizedAtUtc] IS NOT NULL AND [AnonymizedByAccountId] IS NOT NULL)`.

  Istniejące wiersze dostają `NULL` (niezanonimizowane). Poprzednia wersja API działa z nowymi kolumnami, bo ich nie mapuje.
- **`orders.Refunds`, `orders.SalesCorrections`, `orders.Payments`:** bez zmian schematu. Anonimizacja zmienia wartości `Reason` i `CreationRequestHash`.
- **`reporting.AuditEntries`:** bez zmian schematu:
  - `Operation` (`nvarchar(20)`) przyjmuje nową wartość `Anonymized`;
  - anonimizacja zmienia JSON w `Changes`;
  - istniejące indeksy `(ObjectType, ObjectId)` i `(ParentObjectType, ParentObjectId)` obsługują wyszukiwanie wpisów.
- **`integrations.*`, `expenses.*`, `access.*`, `scheduling.*`:** bez zmian.

### Endpoints

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| POST | `/api/orders/{id:guid}/anonymize` | `{ "version": "<base64>" }` | `200 { version }`; `400` VPD `errors.version` albo CSRF; `401`; `404 order_not_found`; `409 order_conflict`; `409 order_already_anonymized`; `422 anonymization_disabled`; `422 retention_not_elapsed` z `availableFrom`; `503` | sesja, CSRF | jedno zamówienie, jego wpisy i ich audyt |
| GET | `/api/orders/{id:guid}` | — | jak dziś + `anonymization` (niżej) | sesja | jedno zamówienie |
| PUT | `/api/orders/{id:guid}` | jak dziś | jak dziś + `422 order_customer_anonymized` | sesja, CSRF | jedno zamówienie |
| GET | `/api/change-history` | jak dziś | `operation` może mieć wartość `anonymized` | sesja | bez zmian |

`.WithName("Orders.AnonymizeCustomer")`. Bez nowych endpointów anonimowych; `EndpointSecurityTests` bez nowych wyjątków.

Problem Details (`OrderProblems`, tekst w `detail`, kod w `code`):

| Kod | Status | `detail` |
|---|---|---|
| `anonymization_disabled` | 422 | „Anonimizacja jest wyłączona do czasu ustalenia okresu retencji danych.” |
| `retention_not_elapsed` | 422 | „Anonimizacja będzie dostępna od {dd.MM.yyyy}, po upływie okresu retencji.” + rozszerzenie `availableFrom` (`yyyy-MM-dd`) |
| `order_already_anonymized` | 409 | „Dane klienta tego zamówienia zostały już zanonimizowane.” |
| `order_customer_anonymized` | 422 | „Dane klienta zanonimizowano. Nie można ich zmienić ani przywrócić.” |

`anonymization` w `OrderDetailsResponse`:

```json
{
  "status": "disabled | retentionPending | available | anonymized",
  "retentionStartDate": "2021-03-15 | null",
  "availableFrom": "2026-03-15 | null",
  "anonymizedAtUtc": "2026-11-13T10:15:00Z | null"
}
```

- `retentionStartDate` i `availableFrom` są wypełnione dla `retentionPending` i `available`.
- `anonymizedAtUtc` jest wypełnione tylko dla `anonymized`.
- Konto wykonawcy nie trafia do odpowiedzi, bo jest jedno; zostaje w bazie i audycie.

### Types crossing a boundary

- **`Shared.Kernel/Audit/AuditOperation`:** nowa wartość `Anonymized` (po `Deleted`).
- **`Shared.Application/Audit`:**

  ```csharp
  /// Anonimizacja wybranych pól we wpisach obiektu i jego obiektów podrzędnych (DANE-01, SF-22).
  /// Tylko w transakcji IUnitOfWork; poza nią InvalidOperationException. Zwraca liczbę zmienionych wpisów.
  public interface IAuditRedaction
  {
      Task<int> RedactAsync(AuditRedaction redaction, CancellationToken cancellationToken);
  }

  public sealed record AuditRedaction(
      AuditObjectReference Root,
      IReadOnlyCollection<AuditFieldSelector> Fields,
      string Marker);

  public sealed record AuditFieldSelector(AuditObjectType ObjectType, string Field);
  ```

  Implementacja: `Reporting.Infrastructure/Audit/AuditRedactor` z `ReportingDbContext` i `ModuleConnection`, czyli to samo połączenie i transakcja co `AuditLog`. Reguła zmiany wartości leży w `AuditRecord.Redact(IReadOnlySet<string> fields, string marker)` → `bool`. Pusta lista selektorów albo pusty znacznik → `ArgumentException`.
- **`Orders.Application/Abstractions`** (wewnątrz modułu):
  - `IAnonymizationSettings { int? RetentionMonths { get; } }`;
  - `IOrderAnonymizationStore`:
    - `GetRetentionFactsAsync(Guid orderId, ct)` → `EntryRetentionFacts(DateOnly? LastEntryDate, DateTime? LastEntryChangeUtc)`;
    - `LoadReasonEntriesAsync(Guid orderId, ct)` → śledzone `Refund` i `SalesCorrection` zamówienia;
  - `OrderDetailsDto` + `AnonymizedAtUtc` i fakty retencji wpisów (z tych samych wpisów, które czyta już szczegół, plus ich `UpdatedAtUtc`).
- **`Orders.Domain`:**
  - `CustomerAnonymization`:
    - `Marker = "[zanonimizowano]"`;
    - `RetentionStart(DateOnly orderDate, DateOnly? lastEntryDate, DateOnly lastChangeDate)`;
    - `Evaluate(...)` → `AnonymizationAvailability(Status, RetentionStartDate?, AvailableFrom?)`;
  - `AnonymizationStatus { Disabled, RetentionPending, Available, Anonymized }`;
  - `OrderRuleViolationKind.AlreadyAnonymized`, `CustomerAnonymized`.
- **Frontend (`order-models.ts`):**

  ```ts
  export type AnonymizationStatus = 'disabled' | 'retentionPending' | 'available' | 'anonymized';
  export interface OrderAnonymization {
    readonly status: AnonymizationStatus;
    readonly retentionStartDate: string | null;
    readonly availableFrom: string | null;
    readonly anonymizedAtUtc: string | null;
  }
  ```

  `OrderDetails.anonymization: OrderAnonymization`. W `change-history-models.ts` `AuditOperationCode` dostaje `'anonymized'`.

## Invariant impact

| Invariant | How it is respected |
|---|---|
| 1. Sales separate from cash | Anonimizacja nie zmienia wartości, dat, kanału, statusu realizacji, statusu ze sklepu, wpłat, zwrotów ani korekt. Źródła raportów nie czytają zmienianych pól. Test integracyjny porównuje raport sprzedaży, zestawienie składników, raport przepływów i listę „Operacje” przed operacją i po niej. |
| 2. Backend decimal money | Bez obliczeń pieniężnych. Retencję liczy backend na `DateOnly`; frontend tylko pokazuje daty z API. |
| 3. API-enforced access | Nowy `POST` pod polityką domyślną (sesja) i globalnym CSRF, bez `.AllowAnonymous()`. Dostępność (konfiguracja, retencja, stan) sprawdza handler niezależnie od UI. Testy: `401` bez sesji; `422` przy wyłączonej funkcji i przed upływem retencji, także wywołane bezpośrednio przez API. |
| 4. Retries never duplicate | Powtórzony `POST` → `409 order_already_anonymized` albo `409 order_conflict`, bez drugiego wpisu audytu. Ponowiona dostawa ze sklepu nie nadpisuje danych klienta zamówienia zanonimizowanego (pkt 18); test dla webhooka i uzupełniania. Unikalność `(Source, ExternalId)` bez zmian. |
| 5. Financial audit trail | Operacja ma własny wpis z autorem, czasem UTC, obiektem, operacją `Anonymized` i zakresem (pola, liczba powodów, początek retencji, okres). Anonimizacja audytu zmienia tylko wartości wskazanych pól. Każdy wpis zostaje z autorem, czasem, obiektem, operacją i nazwami pól, więc historia nie znika bez śladu. Kwoty i daty we wpisach zostają. |
| 6. Secrets stay server-side | Nowa konfiguracja nie jest sekretem. Komunikaty walidacji startu i logi nie zawierają wartości konfiguracji innych niż klucz i zakres. Odpowiedzi nie zawierają danych klienta po anonimizacji. |
| 7. Module and layer boundaries | Orders zmienia audyt wyłącznie przez nowy port `IAuditRedaction` w `Shared.Application`, a implementacja leży w `Reporting.Infrastructure` (właściciel `reporting.AuditEntries`). Integrations dalej zmienia zamówienia tylko przez `IShopOrderSync`. Reguły (retencja, co się czyści, blokada synchronizacji i edycji) są w `Orders.Domain`, a reguła zmiany wpisu audytu w `Reporting.Domain`. Testy architektury bez nowych wyjątków. |
| 8. No identifying data in docs | Przykłady w specyfikacji, dokumentacji i testach są fikcyjne. Znacznik `[zanonimizowano]` nie identyfikuje klienta. |

## Risk

**high** — dane osobowe klientów i operacja nieodwracalna. Zmienia też zapisane wpisy audytu, jedyny wyjątek od zasady „tylko dodawane”. Błąd w selektorach zostawi dane osobowe. Błąd w regule retencji pozwoli usunąć dane przed czasem. Błąd w synchronizacji przywróci dane. Ryzyka nie podnoszono, bo `task.md` ma już `high`. Wymagany przegląd przez człowieka przed merge, w szczególności list pól (pkt 13–15), reguły początku retencji (pkt 6) i zachowania przy braku konfiguracji.

## UI

**Trasa:** `/zamowienia/{id}` (istniejąca). Bez nowych tras.

**Szczegóły zamówienia (`order-details-page`):**

1. **Nagłówek:** przy zamówieniu zanonimizowanym obok statusu i źródła dochodzi `app-status-badge` z tekstem „Dane klienta zanonimizowane” (ton `neutral`, ze znacznikiem). Stan wynika z tekstu, nie z koloru.
2. **Sekcja danych klienta** w widoku szczegółów dostaje blok „Dane osobowe klienta”. Jest w sekcji klienta, a nie w wierszu akcji zamówienia, żeby obok „Anuluj zamówienie” nie stała druga akcja destrukcyjna. Zawartość zależy od `anonymization.status`:

   | Stan | Zawartość |
   |---|---|
   | `available` | `app-button` `variant="danger"` „Anonimizuj dane klienta”; opis: „Okres retencji upłynął {dd.MM.yyyy}. Anonimizacja usunie dane klienta, uwagi, komentarz i powody zwrotów i korekt.” |
   | `retentionPending` | przycisk wyłączony z `aria-describedby` do widocznego opisu „Dostępne po upływie okresu retencji, od {dd.MM.yyyy}. Okres liczy się od {dd.MM.yyyy} — najpóźniejszej z dat zamówienia, jego wpisów i ostatniej zmiany.” |
   | `disabled` | przycisk wyłączony z `aria-describedby` do widocznego opisu „Funkcja wyłączona do decyzji o okresie retencji.” |
   | `anonymized` | bez przycisku; tekst „Dane klienta zanonimizowano {dd.MM.yyyy, HH:mm} (Europe/Warsaw). Operacji nie można cofnąć.” Pola klienta pokazują `[zanonimizowano]` i „Brak danych”. |

   Wyłączony przycisk zawsze ma obok widoczne wyjaśnienie (design guide §2: „Wyłączenie kontrolki nie może ukrywać instrukcji”). Przycisk jest niedostępny także podczas edycji zamówienia, aktywnego formularza wpisu i innej akcji (`entryActive()`, `actionBusy()`).
3. **Dialog potwierdzenia** (`app-confirm-dialog`, `tone="danger"`, `heading` „Zanonimizować dane klienta zamówienia {numer}?”, `confirmLabel` „Anonimizuj dane klienta”). Treść:
   - „Operacji nie można cofnąć. Dane klienta (imię i nazwisko lub nazwa firmy, telefon, e-mail, adres, NIP), uwagi do realizacji, komentarz oraz powody zwrotów i korekt zostaną zastąpione znacznikiem „[zanonimizowano]” w zamówieniu i w historii zmian.”
   - „Kwoty, daty, wpłaty, zwroty, korekty, kanał i numer zamówienia zostają, a raporty się nie zmienią. Synchronizacja ze sklepem nie przywróci danych klienta.”

   Bez wpisywania tekstu potwierdzającego (Assumptions pkt 11). Fokus wraca do elementu wywołującego (zachowanie `app-confirm-dialog`).
4. **Zapis:**
   - przycisk w stanie `busy` z „Anonimizowanie…”, blokada ponownego kliknięcia;
   - sukces: ponowny odczyt zamówienia i komunikat „Dane klienta zanonimizowane.” (istniejący mechanizm komunikatu akcji w `OrdersService.change`);
   - mapowanie błędów po `code`:
     - `order_conflict` → istniejący komunikat konfliktu z ponownym odczytem;
     - `retention_not_elapsed`, `anonymization_disabled`, `order_already_anonymized` → `detail` z API i ponowny odczyt;
     - błąd sieci lub `5xx` → „Nie udało się zanonimizować danych. Spróbuj ponownie.”
5. **Formularz edycji (`order-form`):** nowe wejście `customerAnonymized`. Przy `true` pola klienta są tylko do odczytu, z opisem „Dane klienta zanonimizowano; nie można ich zmienić.”, a formularz wysyła zapisanego klienta bez zmian. Odpowiedź `422 order_customer_anonymized` pokazuje `detail` w podsumowaniu błędów.

**Historia zmian (`features/change-history`):**

- `OPERATION_LABELS.anonymized` = „Anonimizacja”, `OPERATION_TONES.anonymized` = `warning`.
- Wpis `anonymized` pokazuje tylko nowe wartości, jak `created`.
- `AUDIT_FIELD_CATALOG.order` dostaje:
  - `anonymizedFields` — „Zanonimizowane pola”, lista z etykietami pól z katalogu zamówienia, np. „Imię i nazwisko lub nazwa firmy, E-mail, Uwagi do realizacji”;
  - `anonymizedReasons` — „Zanonimizowane powody zwrotów i korekt”, liczba;
  - `retentionStartDate` — „Początek okresu retencji”, data;
  - `retentionMonths` — „Okres retencji (miesiące)”, liczba.
- Wartości `[zanonimizowano]` w dawnych wpisach są pokazywane jako tekst. Filtr rodzaju obiektu „Zamówienie” obejmuje wpis anonimizacji.

**Stany:** wczytywanie i błąd szczegółów jak dotąd. Stan akcji przychodzi w tym samym odczycie, bez osobnego żądania.

**Dostępność:**

- przycisk i dialog obsługiwane klawiaturą;
- opis niedostępności powiązany przez `aria-describedby`;
- komunikat sukcesu i błędu w regionie `role="status"` (istniejący);
- tekst nie znika tylko w toaście;
- kolory wyłącznie z tokenów (`lint:colors`).

**Formaty:** `formatDate`, `formatDateTime` z `shared/format/`.

## Edge Cases

Konfiguracja i retencja:

- Brak klucza w Production → `GET` zwraca `disabled`, `POST` → `422 anonymization_disabled`, także dla zamówienia z 2000 r.
- `RetentionMonths` = `0`, `-1`, `1201`, `"abc"` → start aplikacji kończy się błędem walidacji (test jak `LaunchDateStartupTests`).
- Zamówienie z 15.03.2021 bez wpisów, ostatnia zmiana 15.03.2021, okres 60 → dostępne od 15.03.2026. 14.03.2026 → `retentionPending`, 15.03.2026 → `available`.
- Zamówienie z 2020 r. ze zwrotem z datą 10.06.2022 → początek okresu 10.06.2022.
- Zamówienie z 2020 r. z wpłatą dopisaną dziś z datą 2020 r. → początek okresu = dziś (`UpdatedAtUtc` wpisu), więc niedostępne. Zachowanie ostrożne.
- Zamówienie anulowane dziś → `UpdatedAtUtc` dziś → początek okresu dziś.
- Zmiana ze sklepu (np. status) → `UpdatedAtUtc` przesuwa początek okresu. Dostawa bez zmian pól (`Unchanged`) go nie przesuwa.
- Data zamówienia w przyszłości → początek okresu w przyszłości, niedostępne.
- 31.01 + 1 miesiąc → 28/29.02 (`AddMonths`).
- Wpis usunięty przed anonimizacją: nie wpływa na początek okresu. Jego wpis audytu `Deleted` (z powodem) jest anonimizowany przez obiekt nadrzędny.
- Konfiguracja usunięta po anonimizacji: zamówienie zostaje `anonymized`, synchronizacja dalej pomija klienta.
- Północ w Europe/Warsaw: „dziś” z `WarsawTime.Today`, testy z `FakeTimeProvider` po obu stronach granicy.

Operacja:

- Podwójne kliknięcie albo dwa okna: pierwsze `200`. Drugie ze starą wersją dostaje `409 order_conflict` (wersja sprawdzana przed stanem), a z aktualną wersją `409 order_already_anonymized`. Jeden wpis `Anonymized`.
- Równoczesne dodanie zwrotu: blokada `IOrderLock` szereguje zapisy. Zwrot zapisany przed anonimizacją dostaje znacznik. Zwrot zapisany po niej zachowuje powód wpisany przez użytkownika. Dopisanie wpisu przesuwa początek okresu, ale anonimizacja już się odbyła i jest nieodwracalna.
- Równoczesna dostawa ze sklepu: zapis synchronizacji z kontrolą wersji → `RetryRequired` → ponowienie na świeżych danych zanonimizowanego zamówienia (pkt 18). Albo anonimizacja dostaje `409 order_conflict` i użytkownik ponawia.
- Wyjątek w `IAuditRedaction` albo `RecordAsync` → wycofanie całości; zamówienie nadal niezanonimizowane.
- Zamówienie bez wpisów audytu (np. sprzed SF-7 w testach) → `RedactAsync` zwraca 0, operacja się udaje.
- Zamówienie ręczne bez telefonu, e-maila i adresu → `anonymizedFields` zawiera tylko pola, które miały wartość, np. `["customerName"]`.
- Zamówienie ze sklepu z nazwą zastępczą „Klient sklepu #123” → zastąpiona znacznikiem.
- Zamówienie z otwartym błędem synchronizacji: błąd zostaje; „Ponów” stosuje dostawę bez danych klienta i zamyka błąd jak dotąd.
- Zamówienie usunięte w sklepie, zanonimizowane, potem przywrócone w sklepie → status ze sklepu i ostrzeżenie jak dotąd, klient bez zmian.

Po anonimizacji:

- `PUT` z klientem równym zapisanemu (znacznik i puste pola) i zmienionym statusem → `200`, audyt `Updated` bez pól klienta.
- `PUT` z inną nazwą klienta w zamówieniu ręcznym → `422 order_customer_anonymized`. W zamówieniu ze sklepu → `422 field_owned_by_shop`, jak dotąd.
- Użytkownik wpisuje nowe uwagi albo komentarz → zapisane z audytem. Odpowiedzialność za treść leży po stronie użytkownika (Assumptions pkt 6).
- Poprawa zwrotu z powodem `[zanonimizowano]` bez zmiany powodu → zapis jak dotąd.
- Ponowienie `POST /api/orders` z kluczem idempotencji zanonimizowanego zamówienia ręcznego → `409 idempotency_key_reused` (skrót zerowy). Komunikat podaje tylko numer zamówienia, nie dane osobowe.
- Raporty za okres zamówienia, wpłaty i zwrotu: identyczne przed i po.

## Dependencies

- SF-12 i SF-13 (synchronizacja, `IShopOrderSync`, historia błędów) — w main.
- SF-7 (audyt, `IsPersonalData`, obiekt nadrzędny, brak blokady zmian w bazie) — w main.
- SF-8 – SF-10 (zamówienia, wpłaty, zwroty, korekty, `IOrderLock`) — w main.
- SF-18 (Historia zmian), SF-17, SF-19, SF-20 (raporty do porównania) — w main.
- **D-09** (długość okresu retencji) — otwarta. Zadanie nie wymaga jej rozstrzygnięcia do merge, bo funkcja jest wyłączona bez konfiguracji. Włączenie w produkcji wymaga rozstrzygnięcia D-09 przez właściciela z księgowym lub prawnikiem i ustawienia wartości (pkt 5). Patrz Assumptions pkt 1 o zgodności z `rules.md`.

## Assumptions

Etap pytań zakończono. Właściciel polecił pracę autonomiczną, więc niejasne punkty rozstrzygnął wykonawca. Każda decyzja poniżej jest **decyzją wykonawcy z 13.11.2026 do potwierdzenia przez właściciela**.

1. **D-09 pozostaje otwarta; funkcja fail-closed.**
   - Długość okresu to konfiguracja `StoneFlow:Anonymization:RetentionMonths` (1–1200 miesięcy), sprawdzana przy starcie;
   - bez wartości akcja jest niedostępna w UI, a API zwraca `422 anonymization_disabled`;
   - kod, `appsettings.json` i `appsettings.Development.json` nie zawierają domyślnej długości.

   **Sprzeczność do potwierdzenia:** `rules.md` każe nie implementować obszaru blokowanego przez D-09. PRD §12 mówi, że D-09 „blokuje udostępnienie anonimizacji”, a §13, że okres trzeba ustalić „przed wdrożeniem anonimizacji”. Spec realizuje mechanizm bez udostępniania go: funkcja jest wyłączona do ustawienia wartości po decyzji. Właściciel polecił kontynuację. Przegląd specyfikacji powinien potwierdzić, że to odczytanie jest zgodne z regułą, albo zatrzymać zadanie.
2. **Początek okresu retencji** to najpóźniejsza z dat: zamówienia, wpisu (wpłata, zwrot, korekta) i ostatniej zmiany zamówienia lub jego wpisów w Europe/Warsaw (pkt 6). Uzasadnienie:
   - obejmuje każde zdarzenie gospodarcze zapisane na zamówieniu (sprzedaż, zapłata, zwrot, korekta, anulowanie) bez nowej kolumny daty anulowania;
   - jest ostrożny: późniejszy początek tylko opóźnia operację, a wcześniejszy pozwoliłby nieodwracalnie usunąć dane przed czasem;
   - nie korzysta z audytu, więc Orders nie czyta danych Reporting.

   Jeżeli D-09 przyjmie liczenie od końca roku kalendarzowego (częste w przepisach rachunkowych), zmieni się tylko `CustomerAnonymization.RetentionStart`. Spec tego nie przesądza.
3. **Zakres usuwanych danych:**
   - z zamówienia: nazwa (zastąpiona znacznikiem, bo jest wymagana), telefon, e-mail, ulica, kod, miejscowość i NIP (wyczyszczone), uwagi do realizacji i komentarz (wyczyszczone);
   - ze zwrotów i korekt: powód (zastąpiony znacznikiem);
   - te same pola we wszystkich wpisach audytu zamówienia i jego wpisów (znacznik zamiast wartości).

   Zostają: rodzaj klienta, kwoty, daty, wpłaty, zwroty, korekty, kanał, źródło, numer, ID WooCommerce, status ze sklepu, dostawcy, referencje ZO, numer faktury i flagi. Dostawcy, ZO i faktura to dane firmy i kontrahentów, nie klienta.
4. **Skróty żądań idempotencji** (zamówienie ręczne, zwrot, korekta) są zastępowane zerami, bo SHA-256 z treści pozwalałby potwierdzić zgadywane dane osobowe. Skutek: ponowienie starego żądania dostaje `409 idempotency_key_reused`, co jest poprawne dla żądania sprzed lat. Skrót wpłaty zostaje, bo nie zawiera danych klienta.
5. **„Przyjął” przy wpłacie** nie jest anonimizowane w wierszu ani w audycie, choć ma `IsPersonalData`. To osoba po stronie sklepu, nie klient (otwarta kwestia z SF-9 i przeglądu SF-15). Kontrahent, płatnik i komentarz wydatków oraz osoba wypłaty też są poza zakresem, bo DANE-01 dotyczy danych klienta zamówienia.
6. **Edycja po anonimizacji:**
   - dane klienta są tylko do odczytu, a API odrzuca ich zmianę (`422 order_customer_anonymized`), więc nie da się ich przywrócić;
   - pozostałe pola ERP i operacje finansowe (wpłaty, zwroty, korekty, anulowanie, przywrócenie, wartość zamówienia ręcznego) zostają dostępne. Blokada uniemożliwiłaby rozliczenie zwrotu, korekty albo błędnej wpłaty na starym zamówieniu, a raporty i audyt muszą dalej odzwierciedlać rzeczywistość;
   - nowe uwagi, komentarze i powody wpisuje świadomie użytkownik.
7. **Fakt anonimizacji:** kolumny `AnonymizedAtUtc` i `AnonymizedByAccountId` na zamówieniu. Wpis audytu `Anonymized` (nowa operacja) zawiera pola, liczbę powodów, początek okresu i okres, bez danych osobowych. Osobna operacja zamiast `Updated` pozwala rozpoznać wpis w „Historii zmian” bez czytania pól.
8. **Historia błędów synchronizacji** (`integrations.WooCommerceOrderErrors`) nie wymaga zmian. Według SF-13 (spec, „Historia błędów zamówień”) przechowuje ID zamówienia, kody statusu i źródła, komunikat złożony z kodu (nigdy z treści odpowiedzi ani wyjątku, bez danych klienta) i kontekst (temat z ID dostawy albo ID przebiegu). Treści dostaw nie zapisuje się nigdzie. Zgadza się z tym kod (`OrderOutcome`, `WooCommerceOrderErrors.RecordAsync`).
9. **Logi i kopie zapasowe** są poza zasięgiem operacji:
   - kopie SQL mają retencję 30 dni (D-08), więc dane znikają z nich najpóźniej po 30 dniach;
   - odtworzenie kopii sprzed anonimizacji przywraca dane, a także usuwa wpis audytu operacji. Procedura w `docs/operations/kopie-i-odtworzenie.md` dostaje krok: po odtworzeniu ponowić anonimizacje wykonane po dacie kopii, według rejestru żądań prowadzonego przez właściciela;
   - logi według istniejących zasad (`rules.md` niezmiennik 6, logi Integrations tylko z ID) nie zawierają danych klienta, a nowy kod niczego takiego nie loguje.
10. **Znacznik** `[zanonimizowano]` zastępuje wartości wymagane (nazwa, powód) i wartości w audycie. Pola opcjonalne zamówienia są czyszczone (`null`), żeby UI pokazywał „Brak danych”, a nie znacznik w każdym polu. W audycie `null` zostaje `null`, bo brak wartości różni się od wartości usuniętej.
11. **Bez wpisywania tekstu potwierdzającego.** Dialog z tonem `danger`, nazwą zamówienia i informacją o nieodwracalności wystarcza przy jednym użytkowniku i ręcznej operacji na pojedynczym zamówieniu. Wpisywanie wymagałoby rozbudowy `app-confirm-dialog` bez drugiego zastosowania (YAGNI).
12. **Kody błędów:** stan blokujący (już zanonimizowane, konflikt wersji) → `409`; reguła biznesowa (wyłączona funkcja, retencja) → `422`, zgodnie z tabelą klas błędów w conventions.
13. **Bez testów E2E:** funkcja jest domyślnie wyłączona, a test wymagałby cofania czasu. Pokrycie dają testy integracyjne na SQL Server (API, synchronizacja, raporty), testy komponentów i scenariusze ręczne UI.
14. **Wycofanie wersji:** po wdrożeniu nowej wersji i wykonaniu anonimizacji powrót do wersji sprzed SF-22 nie zna operacji `Anonymized`, więc jej „Historia zmian” nie wczyta takiego wpisu. Ten sam powrót przywróciłby nadpisywanie klienta przez synchronizację. `docs/operations/anonimizacja.md` zapisuje, że po pierwszym użyciu funkcji nie wraca się do wersji sprzed SF-22. Do pierwszego użycia (funkcja wyłączona) powrót jest bezpieczny.

## Acceptance Criteria

Kryteria ze zgłoszenia:

- [ ] Przed upływem okresu retencji akcja jest niedostępna, także przez API:
  - testy jednostkowe `CustomerAnonymization` (granica dnia, wpisy, ostatnia zmiana, `AddMonths`);
  - test integracyjny `POST` → `422 retention_not_elapsed` z `availableFrom` dzień przed granicą i `200` w dniu granicy (`FakeTimeProvider`);
  - bez konfiguracji `GET` zwraca `disabled`, a `POST` → `422 anonymization_disabled`;
  - UI pokazuje wyłączony przycisk z wyjaśnieniem w obu stanach.
- [ ] Po potwierdzeniu dane klienta znikają z zamówienia i audytu, a raporty się nie zmieniają:
  - test integracyjny: zamówienie ręczne i ze sklepu z danymi klienta, uwagami, komentarzem, zwrotem sprzedaży, zwrotem pieniędzy, korektą, wpłatą, edycją klienta i usuniętym zwrotem;
  - po operacji w `orders.*` ani w `reporting.AuditEntries` (zakres zmiany zamówienia i jego wpisów) nie występuje żadna z pierwotnych wartości pól z pkt 15. Wyszukiwanie tekstowe po kolumnach i JSON daje zero trafień;
  - `GET /api/reports/sales`, `/api/reports/sales/components`, `/api/reports/cash-flow` i `/api/operations` za okres obejmujący zamówienie i wpisy zwracają te same wartości przed i po (bez `calculatedAtUtc`).
- [ ] Synchronizacja nie przywraca danych:
  - test domenowy `ApplyFromShop` dla `Created`, `Updated`, `Restored` po anonimizacji;
  - testy integracyjne: webhook z pełnymi danymi klienta i nowszą datą modyfikacji oraz przebieg uzupełniania (fałszywe REST) dla zanonimizowanego zamówienia;
  - klient pozostaje znacznikiem, pozostałe pola sklepu się aktualizują, a wpis audytu synchronizacji nie zawiera pól klienta.
- [ ] Operacja zostawia ślad audytu bez danych osobowych: jeden wpis `Anonymized` z autorem „Właściciel”, czasem UTC, obiektem zamówienia i zakresem z pkt 16, widoczny w `GET /api/change-history` z `operation = anonymized`. Test sprawdza, że jego wartości nie zawierają pierwotnych danych.

Kryteria wynikające z zakresu i PRD:

- [ ] Nieodwracalność:
  - drugi `POST` z aktualną wersją → `409 order_already_anonymized`;
  - `PUT` z inną nazwą klienta w zamówieniu ręcznym → `422 order_customer_anonymized`;
  - `PUT` z niezmienionym klientem i innym statusem → `200`;
  - wpłata, zwrot i korekta po anonimizacji → `201`/`200` jak dotąd.
- [ ] Atomowość: wyjątek wstrzyknięty w `IAuditRedaction` albo `RecordAsync` → zamówienie, wpisy i audyt bez zmian (test integracyjny).
- [ ] Konflikt: `POST` ze starą wersją → `409 order_conflict` bez zmian.
- [ ] Konfiguracja: wartości `0`, `1201`, `abc` zatrzymują start; brak klucza uruchamia aplikację z funkcją wyłączoną.
- [ ] `POST /api/orders/{id}/anonymize` bez sesji → `401`, bez tokenu XSRF → `400`; testy architektury i `EndpointSecurityTests` bez nowych wyjątków.
- [ ] UI:
  - przycisk, opisy stanów, dialog z informacją o nieodwracalności, oznaczenie po sukcesie;
  - pola klienta tylko do odczytu w formularzu;
  - „Historia zmian” pokazuje „Anonimizacja” z etykietami pól.
- [ ] `docs/operations/anonimizacja.md` opisuje włączenie po D-09, zakres usuwanych danych, kopie, logi i ograniczenie powrotu do wersji. `kopie-i-odtworzenie.md` i `deploy/api.env.example` są uzupełnione.

## Verification

- build:
  - `dotnet restore stone-flow.slnx --locked-mode`;
  - `dotnet format stone-flow.slnx --verify-no-changes --no-restore`;
  - `dotnet build stone-flow.slnx -c Release --no-restore`;
  - frontend `npm ci`, `npm run build`;
  - `deploy/scripts/test-migrate.sh` (nowa migracja Orders na pustej bazie i na bazie z danymi).
- tests:
  - **`StoneFlow.Orders.Tests` (jednostkowe):**
    - `Domain/CustomerAnonymizationTests`:
      - początek okresu z samą datą zamówienia, z wpisem późniejszym, z ostatnią zmianą późniejszą, z przyszłą datą zamówienia;
      - dostępność na granicy dnia;
      - `AddMonths` dla 31 stycznia;
      - kolejność stanów (`anonymized` przed `disabled`);
    - `Domain/OrderAnonymizationTests`:
      - pola wyczyszczone i zachowane (lista z pkt 13), rodzaj klienta zachowany;
      - skrót wyzerowany, fakt anonimizacji ustawiony;
      - lista wyczyszczonych pól tylko z wartościami;
      - drugie wywołanie → `AlreadyAnonymized` bez zmian;
      - `Edit` z innym klientem → `CustomerAnonymized`; z tym samym → zmiany ERP zapisane;
      - `ApplyFromShop` po anonimizacji (`Created`/`Updated`/`Restored`/`Deleted`) — klient bez zmian, pozostałe pola sklepu i przejście na „Anulowane” jak dotąd, brak pól klienta w zmianach;
    - `Domain/RefundTests`, `SalesCorrectionTests` — `AnonymizeReason`;
    - `Application/AnonymizeCustomerHandlerTests` (fałszywe porty):
      - kolejność sprawdzeń z pkt 11–12;
      - `422` bez konfiguracji bez dotykania portów;
      - brak `OrderAudit.Updated`;
      - kolejność: anonimizacja audytu przed zapisem wpisu `Anonymized`;
      - zakres wpisu z pkt 16;
      - `commitWhen` tylko dla `Saved`;
    - `Application/GetOrderTests` — stan akcji i daty z faktów retencji.
  - **`StoneFlow.Reporting.Tests`:**
    - `Audit/AuditRecordTests.Redact`: tylko wskazane pola, `null` zostaje `null`, reszta wpisu bez zmian, wynik `false` bez zmian;
    - `ChangeHistoryCodes` — `anonymized`.
  - **`StoneFlow.Api.IntegrationTests` (SQL Server/LocalDB):**
    - `Orders/CustomerAnonymizationApiTests`:
      - kryteria 1, 2 i 4;
      - nieodwracalność, konflikt, atomowość;
      - `401`, `400`, `404`;
      - przeszukanie `orders.Orders`, `orders.Refunds`, `orders.SalesCorrections` i `reporting.AuditEntries` pod kątem pierwotnych wartości;
    - `Reporting/AnonymizationReportsUnchangedTests` — porównanie czterech odczytów raportów przed i po;
    - `Orders/WooCommerceWebhookTests` — webhook po anonimizacji nie przywraca klienta;
    - `Integrations/WooCommerceBackfillTests` — uzupełnianie po anonimizacji nie przywraca klienta;
    - `Audit/AuditRedactionTests` — wpisy korzenia i podrzędne, odrzucenie poza transakcją, inne zamówienia nietknięte;
    - `AnonymizationStartupTests` — walidacja konfiguracji (wzór `LaunchDateStartupTests`);
    - `Reporting/ChangeHistoryApiTests` — wpis `anonymized`.
  - **`StoneFlow.Architecture.Tests`** — bez nowych wyjątków (`IAuditRedaction` w `Shared.Application`, implementacja w Reporting.Infrastructure).
  - **Frontend** (`npm run format:check`, `npm run lint`, `npm run typecheck`, `npm test -- --watch=false`):
    - `orders-api` — `anonymize` (`POST` z wersją);
    - `orders.service` — sukces i mapowanie kodów `order_conflict`, `retention_not_elapsed`, `anonymization_disabled`, `order_already_anonymized`, błąd sieci;
    - `order-details-page`:
      - cztery stany bloku „Dane osobowe klienta” z tekstami i `aria-describedby`;
      - dialog z treścią o nieodwracalności;
      - `busy`, ponowny odczyt i oznaczenie w nagłówku po sukcesie;
      - przycisk niedostępny przy edycji i aktywnym wpisie;
    - `order-form` — `customerAnonymized` blokuje pola klienta i wysyła zapisane wartości;
    - `change-history` — etykieta i ton „Anonimizacja”, tylko nowe wartości, etykiety `anonymizedFields`, nowe pola katalogu.
- manual: scenariusze testów ręcznych UI w `06-implementation-done.md`, z API uruchomionym ze zmienną `StoneFlow__Anonymization__RetentionMonths=1` (oprócz scenariusza „wyłączona”). Zamówienie kwalifikujące się przygotowuje się cofnięciem dat w lokalnej bazie (`OrderDate`, `UpdatedAtUtc`, daty wpisów) skryptem SQL w scenariuszu:
  - funkcja wyłączona (bez zmiennej): opis „Funkcja wyłączona do decyzji o okresie retencji.”, wyłączony przycisk, klawiatura;
  - przed upływem retencji: opis z datą „dostępne od” i początkiem okresu;
  - po upływie: dialog (treść, Escape, powrót fokusu), anulowanie dialogu niczego nie zmienia, potwierdzenie, oznaczenie w nagłówku, pola klienta „[zanonimizowano]” / „Brak danych”, uwagi i komentarz puste, powody zwrotów i korekt zastąpione, kwoty i stan płatności bez zmian;
  - edycja po anonimizacji: pola klienta tylko do odczytu, zmiana statusu zapisuje się;
  - dodanie wpłaty i zwrotu po anonimizacji;
  - „Historia zmian”: wpis „Anonimizacja” z etykietami pól, dawne wpisy z „[zanonimizowano]”;
  - Pulpit (sprzedaż i przepływy) i lista „Operacje” za okres zamówienia: te same wartości przed i po;
  - lista zamówień: klient „[zanonimizowano]”, wyszukiwanie po dawnej nazwie bez wyniku;
  - widok na telefonie (blok i dialog bez poziomego przewijania).
