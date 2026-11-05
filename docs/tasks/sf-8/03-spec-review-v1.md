# Spec review — SF-8 (v1, przed poprawkami)

## Summary

- Overall assessment: FAIL
- Justification: Specyfikacja jest kompletna, dobrze osadzona w SF-6 i SF-7, a idempotencja,
  numeracja i kontrola współbieżności są wykonalne na wspólnym połączeniu `ModuleConnection`.
  Dwie sprawy trzeba jednak poprawić przed planowaniem. Typ kwoty `decimal(12,2)` przeczy
  zapisanej decyzji PRD („Reguły kwot”, `decimal(18,2)`). Reguły edycji zamówienia anulowanego
  wzajemnie się wykluczają: takiego zamówienia nie da się zapisać przez `PUT`. Żaden niezmiennik
  nie jest naruszony.

## Findings

### Critical Issues

**C1. Precyzja kwoty sprzeczna z decyzją PRD.** Sekcja „Data / API → Storage” podaje
`Value decimal(12,2)`, a „Types crossing a boundary → Walidacja” limit `≤ 9 999 999 999,99`.
PRD sekcja 8 „Uzgodnione”, „Reguły kwot (decyzja użytkownika z 01.11.2026)” mówi: „kwoty są typu
`decimal(18,2)`”. To samo powtarzają `project-review.md` i breakdown SF-9. Sprzeczność z zapisaną
decyzją jest zawsze krytyczna, a niezgodna precyzja wróci przy sumach w SF-9, SF-10 i SF-19.
*Poprawka:* `Value decimal(18,2)` w domenie (`HasPrecision(18, 2)`) i w bazie. Limit walidacji
albo dopasować do `decimal(18,2)`, albo zostawić jako jawny limit biznesowy z uzasadnieniem
w Assumptions. Typu w bazie nie zawężać.

**C2. Edycja zamówienia anulowanego jest niewykonalna według obecnych reguł.** Cztery
stwierdzenia razem się wykluczają:
- „Szczegóły i edycja”, pkt 5: „W zamówieniu anulowanym można edytować dane, ale statusu nie
  można zmienić”;
- pkt 2: „Zapis wysyła pełny zestaw pól edytowalnych”, a `OrderInput` zawiera `fulfillmentStatus`;
- Acceptance Criteria: „API odrzuca `fulfillmentStatus: cancelled` w `POST`/`PUT`”;
- pkt 3 i tabela Endpoints: każda inna wartość statusu przy zamówieniu anulowanym to zmiana
  statusu, czyli `422 status_change_requires_action`.

Klient nie ma więc żadnej wartości `fulfillmentStatus`, z którą `PUT` dla anulowanego zamówienia
przejdzie. Formularz nie ma też „Anulowane” na liście opcji, więc nie wiadomo, co pokazuje pole
statusu dla takiego zamówienia.
*Poprawka:* zapisać jedną regułę, na przykład: `PUT` przyjmuje `cancelled` tylko wtedy, gdy
zamówienie już jest anulowane (bez zmiany). Odrzucane są wyłącznie przejścia do `cancelled`
i z `cancelled` (`422 status_change_requires_action`). W UI pole statusu zamówienia anulowanego
jest tylko do odczytu z opisem „Przywróć zamówienie, aby zmienić status”. Inna możliwość:
`fulfillmentStatus` opcjonalny w `PUT`. Do tego dopisać kryterium akceptacji i test domeny
„edycja danych zamówienia anulowanego się udaje, status zostaje `cancelled`”. Przy okazji
ustalić kod błędu dla `cancelled` w `POST`: `400` (wartość spoza dozwolonych) czy `422`.

### Non-Critical Issues

**N1. Kolumna `Suppliers nvarchar(1000)` jest za krótka dla dozwolonych danych.** 10 pozycji po
100 znaków to 1000 znaków samej treści. JSON dokłada cudzysłowy, przecinki i nawiasy (około 31
znaków), a `"` i `\` w treści są jeszcze escapowane. Poprawne według walidacji dane obetną się
w SQL (błąd 2628, `DbException`), a to według obecnego `SecuritySetup.SelectStatusCode` daje
`503`. *Poprawka:* `nvarchar(2000)` albo `nvarchar(max)` dla `Suppliers`, a także zapas
w `ZoReferences`. Można też walidować długość serializowanego JSON. Test na 10 × 100 znaków
z cudzysłowem.

**N2. Ścieżka duplikatu `CreationRequestId` w jednostce pracy nie jest opisana do końca.** Spec
mówi tylko, że naruszenie `2601/2627` jest tłumaczone „na wynik idempotentny”. Według kodu SF-7
trzeba jeszcze rozstrzygnąć:
- `work` łapie `DbUpdateException` z `SqlException` 2601/2627 na indeksie `CreationRequestId`
  i zwraca wynik „duplikat”, a `commitWhen` zwraca dla niego `false`. Rollback cofa podbicie
  licznika, a `ModuleConnection.EndTransaction` czyści `ChangeTracker`;
- dopiero po wycofaniu handler czyta istniejące zamówienie (`id`, `number`, `version`,
  `CreationRequestHash`) zwykłym odczytem, bez zapisów. Konwencja mówi, że po wycofanej
  jednostce pracy zakres nie robi zapisów;
- porównanie skrótu decyduje o `200` albo `409 idempotency_key_reused`, także na ścieżce
  równoległej;
- indeks rozpoznaje się po nazwie z błędu SQL albo przez wcześniejsze sprawdzenie. Kod błędu
  2601 nie odróżnia `CreationRequestId` od `Number`.

*Poprawka:* opisać te kroki w „Dodanie zamówienia ręcznego” pkt 4–5 i w „Data / API”. Test
równoległy powinien obejmować oba warianty: te same dane → `200`, inne dane → `409`.

**N3. Mechanizm porównania `version` nie jest jednoznaczny.** Klient przysyła `version`
(base64), a handler ładuje encję w transakcji. Bez ustawienia
`Entry(order).Property(o => o.Version).OriginalValue = clientVersion`, albo jawnego porównania
z wczytaną wersją, `UPDATE … WHERE Version = @wczytana` nie wykryje zmiany wykonanej między
otwarciem formularza a zapisem. Byłoby to ciche nadpisanie, którego zabrania ZAM-04.
*Poprawka:* zapisać, że oryginalną wartością tokenu jest wersja od klienta (albo że najpierw
następuje jawne porównanie, a `rowversion` chroni okno między odczytem a zapisem). Testy mają
pokryć oba przypadki: starą wersję oraz zmianę równoległą w trakcie zapisu.

**N4. Normalizacja `OrderInput` do skrótu SHA-256 nie jest zdefiniowana.** Bez kanonicznej
postaci ponowienie z tymi samymi danymi może dać inny skrót i fałszywe
`409 idempotency_key_reused`. Przykłady: `"1234.5"` i `"1234.50"`, NIP z myślnikami albo bez,
spacje przed przycięciem, kolejność pól JSON, `null` i brak pola, pusta lista. *Poprawka:* skrót
liczyć z wartości po walidacji i normalizacji domeny, serializowanych deterministycznie (stała
kolejność pól, kwota w formacie `0.00`, daty `yyyy-MM-dd`), z testem jednostkowym na
równoważne warianty.

**N5. Przepływ UI po `409 idempotency_key_reused` może utworzyć duplikat.** „Edge Cases” mówią:
„UI generuje nowy klucz i prosi o ponowny zapis”. Typowy scenariusz to utrata odpowiedzi po
zatwierdzeniu, potem poprawka danych przez użytkownika i ponowne wysłanie. Pierwsze zamówienie
już istnieje, a nowy klucz utworzy drugie. Zadanie ma ryzyko high i cel „ponowienia nie
duplikują”. *Poprawka:* problem `409` niesie `id` i `number` istniejącego zamówienia. UI mówi
„Zamówienie z tego formularza zostało już zapisane” i daje link do niego. Nowy klucz powstaje
dopiero po świadomej decyzji „Zapisz jako nowe zamówienie”.

**N6. `PUT` dla zamówienia WooCommerce odsyła pola sklepu i je waliduje.** Klient wysyła pełny
`OrderInput`, w tym pola należące do sklepu. Dane ze sklepu nie muszą spełniać walidacji ERP
(kod pocztowy inny niż `NN-NNN`, NIP z myślnikami albo bez sumy kontrolnej, e-mail). Normalizacja
NIP-u i przycinanie mogą wtedy wykazać „zmianę” pola, którego użytkownik nie ruszał. Skutek to
`400` albo `422 field_owned_by_shop` przy samej zmianie statusu. Zasiany w teście integracyjnym
rekord WooCommerce trafi na ten problem. *Poprawka:* dla `Source = WooCommerce` nie walidować
i nie normalizować pól sklepu w `PUT`. Porównywać je z wartością zapisaną dosłownie albo
w ogóle ich nie przyjmować. Dopisać to do „Szczegóły i edycja” pkt 3. Rozstrzygnąć też, czy
kanał zamówienia ze sklepu jest edytowalny. PRD: „Kanał zamówienia ze sklepu to »sklep«”, ale
kanał nie występuje na liście pól ERP ani sklepu.

**N7. Nieprzetłumaczone błędy bazy kończą się `503`.** Spec tłumaczy tylko
`DbUpdateConcurrencyException` i duplikat `CreationRequestId`. Zgodnie z opisaną w konwencjach
rozbieżnością („Errors and validation”) `503` dostaną także:
- duplikat `Number` („ostatnia obrona”);
- wyścig `INSERT` nowego roku w `OrderNumberCounters` (2627 na PK `Year`);
- naruszenia check constraint (547).

*Poprawka:* dla każdego z tych przypadków zapisać oczekiwany wynik. Przykładowo duplikat
`Number` i PK licznika to błąd programu, nieosiągalny dzięki blokadzie, więc `503`/`500` jest
akceptowalne i nie ma testu. Albo przetłumaczyć je i dodać test. Precyzyjniej opisać też
numerację: jedno polecenie
`UPDATE orders.OrderNumberCounters WITH (UPDLOCK, HOLDLOCK) SET LastNumber = LastNumber + 1 OUTPUT inserted.LastNumber WHERE Year = @y`,
a przy zerze wierszy `INSERT`. Polecenie idzie przez `ExecuteSql`/`SqlQuery`; interceptor
`ModuleTransactionInterceptor` dołącza je do transakcji jednostki pracy.

**N8. Format wartości audytu dla wartości logicznych i list nie jest określony.** SF-7 definiuje
kwoty, daty, słowniki i `null`, ale nie `hasPhotos`/`hasReview` ani `suppliers`/`zoReferences`.
Nie wiadomo też, czy przy `Created` „pole puste” obejmuje `false` i pustą listę. *Poprawka:*
`true`/`false` jako wartości logiczne. Listy jako JSON tablicy (lub inny stały zapis).
Przy `Created` pomijane są `null` i pusta lista, a `false` jest zapisywane, albo odwrotnie, byle
jawnie. Dopisać to do „Audyt (SF-7…)”.

**N9. Błędne komendy w sekcji Verification.** `api/stone-flow.slnx` nie istnieje, bo solucja
leży w katalogu głównym (`stone-flow.slnx`). Konwencje wymagają też
`dotnet test --solution stone-flow.slnx` oraz `npm run typecheck` i `npm run format:check`.
Nowy projekt `StoneFlow.Orders.Tests` trzeba dodać do solucji. *Poprawka:* przepisać komendy
z „Verification” w `conventions.md`.

**N10. Brakujące elementy UI względem design guide i komponentów współdzielonych.**
- `app-data-table` nie ma miejsca na akcję w pustym stanie bez filtrów: wyświetla
  `app-empty-state` bez treści. Stan „Brak zamówień” z akcją „Dodaj zamówienie” wymaga zmiany
  komponentu współdzielonego albo akcji poza tabelą. Spec tego nie mówi.
- Design guide 6 „Formularze”: błąd „przy polu i w podsumowaniu, jeśli dotyczy wielu pól”. Spec
  opisuje tylko błędy przy polach i fokus na pierwszym.
- `app-button` ma warianty `primary`, `secondary` i `danger`. „Ton ostrzegawczy” dla „Anuluj
  zamówienie” warto nazwać wariantem.
- Klucze błędów pól zagnieżdżonych (`customer.name`, `operational.suppliers[0]`) w
  `ValidationProblem` i ich mapowanie przez `fieldErrorOf` nie są ustalone.

### Unclear or Ambiguous Sections

- **Szczegóły i edycja / Anulowanie i przywrócenie:** nie ustalono kolejności sprawdzeń. Przy
  nieaktualnej `version` i braku zmian: `409 order_conflict` czy `200`? Przy nieaktualnej wersji
  i anulowaniu już anulowanego: `order_conflict` czy `order_already_cancelled`? Proponowana
  kolejność: `404` → walidacja → `version` → reguły stanu → wyliczenie zmian.
- **Data / API → Storage, OrderNumberCounters:** „Numeru nie odzyskuje się po wycofaniu
  transakcji, więc mogą zostać luki” jest niezgodne z mechanizmem. Licznik zmienia się w tej samej
  transakcji, więc rollback cofa też jego podbicie i luk z tego powodu nie będzie. Poprawić opis
  albo podać faktyczne źródło luk.
- **Endpoints, POST:** nie podano odpowiedzi przy braku nagłówka `Idempotency-Key` lub kluczu
  niebędącym UUID (prawdopodobnie `400`). Nie wiadomo też, jaką `version` zwraca powtórzenie
  `200`, gdy zamówienie zmieniono po utworzeniu: aktualną czy z chwili utworzenia.
- **Endpoints, GET lista:** brak formatu wielokrotnego `status` (`status=a&status=b`?),
  informacji, czy `dateFrom`/`dateTo` są włącznie, i trybu wyszukiwania (zawiera czy prefiks,
  wielkość liter). Zdanie „`total` liczy się w tym samym zapytaniu z filtrami (osobne `COUNT`)”
  jest wewnętrznie sprzeczne.
- **Types crossing a boundary:** nie podano kodów `customer.kind` (np. `person`/`company`).
  Nie wiadomo, czy `fulfillmentStatus` jest w `POST` wymagany, czy API też przyjmuje domyślnie
  `inProgress`.
- **Anulowanie i przywrócenie pkt 4:** `DELETE` dostaje `405` tylko z ważną sesją i tokenem XSRF.
  Bez sesji jest `401`, a bez XSRF `400 csrf_invalid` (`CsrfValidationMiddleware` działa przed
  endpointem). Test powinien podać warunki wstępne.
- **Context → Storage:** „nowa migracja `Orders`”. W module jest już migracja `InitialSchema`
  (tylko schemat), więc warto podać nazwę nowej migracji.
- **Edge Cases, numeracja:** rok numeru według daty utworzenia (Europe/Warsaw), a nie daty
  zamówienia, to decyzja autora bez wpisu w Assumptions. Nie opisano też zachowania po 9999
  numerach w roku (`NNNN` → 5 cyfr?).

### Invented or Unsupported Requirements

None.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Data / API → Storage (`Value decimal(12,2)`), Walidacja (`≤ 9 999 999 999,99`) | PRD §8 „Reguły kwot” (decyzja z 01.11.2026), `project-review.md`, breakdown SF-9 | Zapisana decyzja: `decimal(18,2)`. Krytyczne (C1) |
| Szczegóły i edycja pkt 2–5, AC „API odrzuca `cancelled` w PUT” | Wewnętrzna spójność spec | Zamówienia anulowanego nie da się zapisać (C2) |
| Data / API (tłumaczenia błędów) | `conventions.md` „Errors and validation”, rozbieżność `DbException` → `503` | Część przypadków bazy nie ma ustalonego wyniku (N7). Spec wie o rozbieżności i ją uwzględnia dla dwóch przypadków |
| UI → Stany listy („Brak zamówień” z akcją) | `shared/ui/data-table.html` | Komponent nie ma miejsca na akcję w pustym stanie bez filtrów (N10) |
| Lista → Kolumny (bez „Zapłacono”, „Stan płatności”) | Design guide 6 „Tabele” | Brak konfliktu: przeniesione do SF-9 decyzją użytkownika z 05.11.2026 |
| Pole statusu bez „Anulowane” | Design guide 6 „Formularze” („Status realizacji jest polem”) | Brak konfliktu: decyzja użytkownika z 05.11.2026, zgodna z PRD (akcje anulowania i przywracania) |
| Korekta wartości bez limitu obniżeń | PRD §5 „Limit obniżeń sprzedaży” | Brak konfliktu: limit należy do SF-10 według breakdownu („dotyczy wpisu, poprawki i ręcznej korekty wartości”) |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | OK | Brak pola „opłacone” i wpłat; test, że utworzenie nie tworzy rekordu pieniężnego |
| 2. Kwoty decimal na backendzie | OK | Decimal w domenie i bazie, string w JSON, konwersja tekstowa w UI. Precyzja niezgodna z PRD (C1) to konflikt z decyzją, nie użycie typu zmiennoprzecinkowego |
| 3. Dostęp egzekwowany przez API | OK | Sesja na każdym endpoincie, XSRF globalnie, testy `401` dla wszystkich tras |
| 4. Ponowienia nie duplikują | OK | Unikalny `CreationRequestId` i filtrowany `(Source, ExternalId)`. Doprecyzowanie ścieżki duplikatu (N2, N4) i przepływu UI (N5) |
| 5. Audyt finansowy | OK | `IAuditLog` w tym samym `IUnitOfWork`, autor z sesji, `IsPersonalData` dla pól klienta, brak usuwania. Format części wartości do ustalenia (N8) |
| 6. Sekrety po stronie serwera | OK | Brak nowych sekretów. Logi tylko z `id` i numerem |
| 7. Granice modułów i warstw | OK | Schemat `orders`, audyt przez `Shared.Application`, porty zwracają DTO, domena bez EF/HTTP |
| 8. Brak danych identyfikujących w dokumentach | OK | Fikcyjni klienci, bez nazwy sklepu |

## Risk

- Recorded: high
- Assessed: high. Wartość zamówienia jest podstawą sprzedaży, zadanie obejmuje dane osobowe
  klienta, idempotencję i kontrolę współbieżności. Przed merge wymagany jest przegląd człowieka.

## Assumptions Detected

| Założenie | Czy spec je podaje |
|---|---|
| Wartość ≥ 0 zł, 0 zł dozwolone | Tak (odpowiedź użytkownika) |
| Data zamówienia ręcznego edytowalna, ze sklepu tylko do odczytu | Tak (odpowiedź użytkownika) |
| „Anulowane” tylko przez akcje | Tak (odpowiedź użytkownika); skutki dla edycji anulowanego niespójne (C2) |
| Słowniki stałe w kodzie | Tak (odpowiedź użytkownika) |
| Filtr i kolumny płatności w SF-9 | Tak (odpowiedź użytkownika) |
| Kanał „sklep” dozwolony dla ręcznych | Tak (odpowiedź użytkownika) |
| Numer `ZR-{rok}-{NNNN}` | Tak (odpowiedź użytkownika) |
| Rok numeru według daty utworzenia (Europe/Warsaw) | Nie: tylko w Edge Cases |
| Wymagane tylko rodzaj i nazwa klienta | Tak (odpowiedź użytkownika) |
| Granica z SF-10 (limit obniżeń) i zakres PLAT-06 | Tak; zgodne z breakdownem |
| Edycja zamówienia anulowanego dozwolona | Tak; mechanizm sprzeczny (C2) |
| Klucz idempotencji tylko dla tworzenia, przechowywany bezterminowo | Tak |
| Skrót SHA-256 znormalizowanego wejścia | Tak; normalizacja niezdefiniowana (N4) |
| Dostawcy i ZO jako JSON w kolumnie | Tak; rozmiar kolumny za mały (N1) |
| Wyszukiwanie po numerze i nazwie klienta | Tak |
| Domyślna lista z anulowanymi | Tak |
| Autor audytu z sesji | Tak |
| `total` przez `COUNT`, bez kursora | Tak |
| Przyszła data zamówienia dozwolona | Nie: tylko w Edge Cases („PRD nie zabrania”) |
| Kwota `decimal(12,2)` i limit 9 999 999 999,99 | Nie: niezgodne z PRD (C1) |
| Pełny `OrderInput` w `PUT` także dla zamówień ze sklepu | Nie: ukryte, skutki w N6 |
| Kanał zamówienia ze sklepu edytowalny lub nie | Nie: nierozstrzygnięte (N6) |

## Recommendation

- Revise specification. Do poprawy obowiązkowo: C1 i C2. Przed planowaniem warto też domknąć
  N1–N6, bo dotyczą danych finansowych i ochrony przed duplikatami. Pozostałe uwagi można
  rozstrzygnąć w planie.
