# Code review — SF-9

Przegląd commita `2f48ee9` (diff `d34cf1e..2f48ee9`, gałąź `sf-9-payments`) względem
`02-spec.md`, `03-spec-review.md`, `04-implementation-plan.md` (z korektami z sekcji 5),
`task.md`, `.sdd/context/rules.md` i `.sdd/context/conventions.md`. Recenzent nie jest autorem
kodu. Przeczytano w całości wszystkie zmienione pliki backendu (domena, aplikacja,
infrastruktura, API, migracja, testy) i frontendu (`features/orders`).

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Implementacja pokrywa specyfikację i korekty planu (N1–N9), a żaden
  niezmiennik nie jest naruszony. Reguły `PaymentSummary` zgadzają się z PRD: 0 zł daje
  Opłacone, anulowane nie ma stanu, jest „Do zwrotu”, nadpłata i „Do zapłaty”. Predykat SQL
  filtra odpowiada im dokładnie. Blokada `UPDLOCK` poprzedza sumy i każdy zapis wpisu. Klucz
  idempotencji jest sprawdzany przed limitem, a audyt `Deleted` zapisuje pełny ślad. Uwagi są
  drobne. Jedna dotyczy wąskiego okna, w którym odświeżenie po zapisie albo usunięciu wpisu może
  nadpisać niezapisaną edycję zamówienia (N6 nie obejmuje usuwania). Pozostałe to braki
  w testach i kilka zapachów kodu.
- Risk: high. Przed merge wymagany jest przegląd człowieka, niezależnie od wyniku (blokada
  i limity, fizyczne usuwanie z audytem, idempotencja).

## Project Invariant Violations

None.

| Invariant | Status | Gdzie sprawdzone |
|---|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | OK | Wpis nie dotyka `orders.Orders` (blokada to `SELECT … WITH (UPDLOCK)`, bez zmiany `rowversion`); pobranie to tylko metoda. `Payment_does_not_change_order_value_or_status` (`PaymentsApiTests.cs:98`) sprawdza `value`, `fulfillmentStatus`, `version` i `updatedAtUtc`. `Cash_on_delivery_order_without_payment_is_unpaid` (`:120`) |
| 2. Kwoty decimal na backendzie | OK | `decimal` w domenie, `HasPrecision(18, 2)` (`OrderEntryConfiguration.cs:31`), migracja `decimal(18,2)` (`20261105101805_PaymentsAndRefunds.cs:24,55`), `CHECK (Amount > 0)`. Frontend nie liczy: `paymentFormFrom` tylko zamienia `.` na `,` (`entry-form-model.ts:70`), zero rozpoznaje po tekście `0.00` (`:114`), a komunikat `422` bierze `available` z API (`orders.service.ts:425-434`) |
| 3. Dostęp egzekwowany przez API | OK | Brak `AllowAnonymous`. `Payment_routes_require_session` (6 tras → 401) i `Payment_writes_require_xsrf` (`POST`, `PUT` i `DELETE` z ciałem → `400 csrf_invalid`). `EndpointSecurityTests` działa refleksyjnie |
| 4. Ponowienia nie duplikują | OK | Unikalny filtrowany indeks `CreationRequestId` w obu tabelach. Skrót obejmuje `orderId` (`PaymentInput.cs:58`, `RefundInput.cs:66`). Ścieżka naruszenia indeksu z ponownym odczytem po wycofaniu (`OrderEntryWriter.cs:124-131`); testy `Parallel_adds_with_same_key_create_one_entry` i `Duplicate_key_on_insert_is_resolved_after_rollback` |
| 5. Audyt finansowy | OK | `IAuditLog.RecordAsync` w tej samej jednostce pracy przy Created, Updated i Deleted (`OrderEntryWriter.cs:113,171,202`), z rodzicem Order (`PaymentAudit.cs:56`). Data biznesowa to data wpisu, przy poprawie nowa. `Deleted` ze wszystkimi niepustymi polami, `receivedBy` z `IsPersonalData`. Brak zmian oznacza brak audytu. Wycofanie przy błędzie audytu: `Audit_failure_rolls_back_entry_changes` |
| 6. Sekrety po stronie serwera | OK | Bez nowych sekretów i bez logowania danych |
| 7. Granice modułów i warstw | OK | Tabele `orders.*`; audyt przez `IAuditLog`; domena bez EF i HTTP (wersja jako właściwość cienia); odczyty przez porty jako DTO |
| 8. Brak danych identyfikujących | OK | Dane fikcyjne („Jan Testowy”, „Anna Przykładowa”, `ZR-2026-9001`) |

## Conventions Violations

### Critical (must fix before merge)

None.

### Non-Critical (should fix)

- **K1 (średnia). N6 nie chroni edycji zamówienia przy usuwaniu wpisu ani w trakcie
  odświeżania po zapisie.**
  - `app/src/app/features/orders/order-details-page.html:65` blokuje „Edytuj” tylko przy
    `entryFormOpen()`.
  - Przy zapisie wpisu `order-payments.ts:268-269` najpierw woła `closeForm()`, które emituje
    `formOpenChange(false)`, a dopiero potem `await this.reload()()`.
  - Usuwanie (`order-payments.ts:298-311`) w ogóle nie emituje `formOpenChange`.
  - Skutek: w czasie żądania `DELETE` albo ponownego `GET` użytkownik może otworzyć „Edytuj” i
    zacząć pisać. Wtedy `load()` ustawia `formInitial` (`order-details-page.ts:243`), a
    `OrderForm` resetuje model przez `linkedSignal` (`order-form.ts:126`), więc niezapisane
    zmiany zamówienia przepadają.
  - Okno trwa jeden lub dwa round-tripy. Danych finansowych to nie psuje, ale plan (sekcja 6,
    N6) wprowadził tę blokadę właśnie po to, żeby odświeżenie nie kasowało edycji zamówienia.
  - Proponowana poprawka: sygnalizować stronie „zajęty” przez cały zapis lub usunięcie
    i odświeżenie, a nie tylko przy otwartym formularzu.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Wpłata: kwota > 0, data, metoda (4), „Przyjął” tylko przy gotówce, wiele wpłat | Covered | `PaymentInput.cs:14-52`, `CK_Payments_ReceivedBy`; UI czyści pole przy zmianie metody (`payment-form.ts:115-123`) |
| Zwrot: rodzaj wymagany bez domyślnej, kwota, data, metoda (3), powód 1–500 | Covered | `RefundInput.cs:14-63`; radio bez zaznaczenia z opisami w `aria-describedby` (`refund-form.html:29-60`) |
| `PaymentSummary`: Zapłacono, stan, Do zapłaty, Nadpłata, Do zwrotu | Covered | `PaymentSummary.cs:20-47`; 15 przypadków w `PaymentSummaryTests.cs:7-25` |
| Stan liczony przy odczycie; lista z sum SQL zgodna ze szczegółami | Covered | `OrderReader.cs:43-44` (sumy), `ListOrders.cs:97-98` (ta sama funkcja); test zgodności wszystkich progów `PaymentsApiTests.cs:643` |
| Filtr `paymentState` (w tym `none`, OR) w bazie | Covered | `OrderReader.cs:186-208`. Predykat sprawdzony gałąź po gałęzi z `Compute`: anulowane, `V = 0`, `P ≤ 0`, `0 < P < V`, `P ≥ V`; `COALESCE` przez `Sum` EF |
| Blokada zamówienia przed sumami i przed każdym zapisem wpisu | Covered | `OrderLock.cs:18-28` (najpierw blokada, potem dwa `SumAsync`); każda ścieżka `OrderEntryWriter` zaczyna od `LockAsync` (`:90`, `:220`); test wyścigu z dekoratorem portu `PaymentsApiTests.cs:255` |
| Limit: zwrot ≤ Zapłacono (`422` + `available`), poprawa i usunięcie wpłaty bez ujemnego Zapłacono | Covered | `PaidRules` (`PaymentSummary.cs:79-95`); handlery `EditPayment.cs:38`, `DeletePayment.cs:33`, `AddRefund.cs:35`, `EditRefund.cs:39`; usunięcie zwrotu bez reguły |
| Idempotencja: klucz przed limitem, inne zamówienie → `409` z `paymentId`/`refundId`, duplikat przy insercie | Covered | `OrderEntryWriter.cs:96-104,124-131`; testy `:204`, `:327`, `:355` |
| `rowversion`: `409 entry_conflict`, usunięty → `404` | Covered | Wstępne porównanie (`OrderEntryWriter.cs:232`) i `OriginalValue` w zapisie (`OrderEntryStore.cs:62`); testy `:392`, `:414`, `:445` |
| Wpis innego zamówienia → `404 entry_not_found` | Covered | `OrderEntryWriter.cs:227`; test `:475` |
| Data nie z przyszłości według Europe/Warsaw; wcześniejsza niż zamówienie dozwolona | Covered | `EntryInput.cs:42-63`, `WarsawTime.cs:11`; testy jednostkowy i integracyjny o 23:30 UTC |
| Audyt Created/Updated/Deleted, rodzic Order, etykieta bez danych osobowych | Covered | `PaymentAudit.cs`; `Entry_lifecycle_writes_created_updated_deleted_audit` sprawdza pola, kolejność i datę biznesową |
| `DELETE` z `{ version }` w ciele (N2), brak → `400 version` | Covered | `PaymentsEndpoints.cs:57,78`; test `:503` |
| UI: sekcja „Płatności”, podsumowanie, badge, ostrzeżenia tekstem, dwie tabele z `caption`, puste stany | Covered | `order-payments.html` |
| UI: jeden formularz naraz, fokus na pierwszym polu i powrót, dialog usuwania z opisem wpisu i skutku | Covered | `order-payments.ts:151-167,207-221`; `payment-form.ts:124`, `refund-form.ts:123` |
| UI: `busy`, jedno żądanie, klucz zachowany przy ponowieniu | Covered | `payment-form.ts:127-141`; klucz w `OpenForm` (`order-payments.ts:136`) |
| UI: konflikt z „Wczytaj aktualne dane”, komunikat `422` z `available` | Covered | `order-payments.ts:170-185,277-288`; `orders.service.ts:402-435` |
| Ostrzeżenie przy wpłacie do anulowanego zamówienia | Covered | `payment-form.html:29-33` (tylko przy dodaniu) |
| Ostrzeżenie w dialogu anulowania | Covered | `order-details-page.html:260`. Kryterium `payments.length > 0` według planu (N7), a nie `Zapłacono > 0` ze spec. Patrz „Plan Deviations” |
| N6: wzajemne wykluczenie edycji zamówienia i formularzy wpisów | Partial | Formularze tak; usuwanie i odświeżanie nie (K1) |
| Lista: kolumny „Zapłacono” (prawa) i „Stan płatności” („—” + „brak stanu”), filtr wielokrotny | Covered | `orders-list-page.ts:48-49`, `orders-list-page.html:120-152` |
| Kolory tylko z tokenów `--sf-*` | Covered | Wyłącznie `--sf-success/warning/error/muted/secondary/border-strong` i klasy daisyUI |

## Plan Deviations

Różnice faktyczne; implementujący zgłosili je sami. Żadna nie łamie niezmiennika.

- **Ogólne `OrderEntryWriter<TEntry>` i `OrderEntryStore<TEntry>`** zamiast osobnych
  `PaymentStore.cs` i `RefundStore.cs`, a mapowanie w jednym pliku `OrderEntryConfiguration.cs`
  zamiast `PaymentConfiguration.cs` i `RefundConfiguration.cs` (klasy istnieją w tym pliku).
  Są dwa realne wywołania z identycznym przebiegiem, więc konwencja („Application helper used by
  both”) to dopuszcza.
- **Enumy w bazie jako nazwy PascalCase** (`Transfer`, `Cash`, `SalesReturn`), jak w SF-8.
  Spec (`Data / API → Storage`) podaje kody camelCase. Check constraint używa `N'Cash'`
  (`OrderEntryConfiguration.cs:56`), więc jest spójny. API i audyt zwracają kody camelCase.
- **`OrderLock` to dwa polecenia:** najpierw blokada, potem `SumAsync`. Plan przewidywał jedno
  `SELECT` z sumami. Sumy liczą się po uzyskaniu blokady, więc semantyka jest zachowana.
- **`201 Location`** wskazuje `/api/orders/{orderId}`, bo wpis nie ma własnego odczytu
  (`PaymentsEndpoints.cs:101`).
- **Ostrzeżenie w dialogu anulowania** pokazuje się przy `payments.length > 0` (plan N7), a spec
  mówi o „Zapłacono > 0”. Gdy wpłaty są w całości zwrócone, dialog mówi „Zapłacono 0,00 zł.
  Wpłaty pozostaną, a pieniądze rozlicz zwrotem.”, co w tym przypadku jest mylące. Zgodne
  z planem, do decyzji właściciela.
- **Brak testu typów kolumn migracji** z planu (sekcja 7, niezmiennik 2). Patrz T1.

## Null Safety Issues

None. Wykrzykniki mają uzasadnienie w kodzie:

- `result.Id!` dla `KeyReused`, `Repeated` i `Created`: `Resolve` i `Created` zawsze ustawiają
  `Id`;
- `result.Available!` dla `RefundExceedsPaid`: `CheckRefund` zawsze podaje `Available`;
- `attempt.Duplicate!`: ustawione, gdy `Result` jest `null`.

`LockedOrder` jest `null` tylko przy braku zamówienia. Sumy są nie-null, bo EF tłumaczy `Sum`
z `COALESCE`. `CreationRequestHash ?? []` daje bezpieczny brak dopasowania.

## Correctness Issues

- **C1 (średnia), to samo co K1.** `order-payments.ts:268-269,298-311` i
  `order-details-page.html:65`. Odświeżenie po zapisie lub usunięciu wpisu może nadpisać
  niezapisaną edycję zamówienia otwartą w tym czasie.

Sprawdzone bez uwag:

- Kolejność sprawdzeń w `ChangeAsync`: blokada, wpis, wersja, reguła, zapis.
- Reguła sprawdzana przed `Edit`, więc encja nie jest zmieniana przed `422`.
- `commitWhen` tylko dla sukcesu.
- Brak zmian w poprawce: sukces bez zapisu, audytu i zmiany wersji (test sprawdza tę samą
  `version`).
- Porównanie boxed `decimal` w `Changed` (`300` = `300.00`).
- Brak cyklu blokad z edycją zamówienia z SF-8: SF-8 nie dotyka tabel wpisów, a FK S-lock jest
  zgodny z własnym U-lockiem.

## Test Gaps

- **T1 (niska).** Plan (sekcja 7, niezmiennik 2) wymienia „test typów kolumn w migracji”. W
  `api/tests` nie ma testu, który sprawdzałby `decimal(18,2)` kolumn `Amount` w
  `orders.Payments` i `orders.Refunds` (np. przez `INFORMATION_SCHEMA.COLUMNS`). Typ jest
  poprawny w migracji (`20261105101805_PaymentsAndRefunds.cs:24,55`), ale nic go nie pilnuje.
- **T2 (niska).** Wyścig pod blokadą jest testowany tylko dla dwóch dodań zwrotu
  (`PaymentsApiTests.cs:255`). Nie ma testu równoległej poprawy lub usunięcia wpłaty i dodania
  zwrotu (np. obniżenie wpłaty 1000 → 400 równolegle ze zwrotem 600). Ścieżka `ChangeAsync`
  bierze tę samą blokadę (`OrderEntryWriter.cs:220`), więc ryzyko jest niskie. Usunięcie
  `LockAsync` tylko z tej ścieżki nie zostałoby jednak wykryte.
- **T3 (niska).** Brak testu komponentu dla K1: „Edytuj” w trakcie usuwania wpisu
  (`order-details-page.spec.ts:327` sprawdza tylko otwarty formularz).

Testy nie przechodzą przy zepsutej funkcji tam, gdzie to sprawdziłem:

- test wyścigu bez blokady dałby dwa zapisy;
- test duplikatu wstawia wiersz w oknie między odczytem klucza a zapisem;
- test zgodności listy porównuje listę ze szczegółami i z oczekiwaną mapą dla wszystkich
  progów, także dla filtra;
- `PaymentSummaryTests` porównuje cały rekord.

## Code Smells

- **S1 (niska).** `order-payments.ts:144,148`: `editPayment` i `editRefund` generują
  `key: newKey()`, którego poprawa nigdy nie używa (`updateEntry` nie wysyła klucza). Typ
  `OpenForm` mógłby mieć klucz tylko przy dodaniu.
- **S2 (niska).** `OrderReader.cs:43-44` i `:199`: przy aktywnym filtrze stanu każda pozycja
  liczy sumy wpłat i zwrotów dwa razy (w predykacie i w projekcji). Przy obecnym wolumenie to
  bez znaczenia; wydajność, nie poprawność.
- **S3 (niska).** `Abstractions/Ports.cs:11-29`: wynik zapisu wpisu używa typów
  `OrderSaveStatus` i `OrderSaveResult`, nazwanych od zamówienia. Komentarz to wyjaśnia, ale
  nazwa myli przy czytaniu `OrderEntryStore`.

## Weryfikacja wykonana przez recenzenta

| Polecenie | Wynik |
|---|---|
| `dotnet test --project api/tests/StoneFlow.Orders.Tests -c Release --no-build` | Powodzenie: 129/129 |
| `npm test -- --watch=false` (w `app/`) | Powodzenie: 36 plików, 353 testy |

Testów integracyjnych (`StoneFlow.Api.IntegrationTests`, w tym `PaymentsApiTests`) ani pełnego
`dotnet test --solution` nie uruchamiałem. Pełny przebieg idzie osobno. Ich wynik trzeba
potwierdzić przed merge.

## Recommendation

- Fix critical issues before merge: krytycznych nie ma. K1/C1 warto poprawić przed merge.
  T1–T3 i S1–S3 mogą trafić do tego samego PR albo do kolejnego zadania. Ryzyko high: przed
  merge wymagany jest przegląd człowieka i zielony pełny przebieg testów integracyjnych.
