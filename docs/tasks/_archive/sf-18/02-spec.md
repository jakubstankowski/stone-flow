# Task Specification — SF-18

## Source

[SF-18](https://standev.youtrack.cloud/issue/SF-18)

## Goal

Lista „Historia zmian” tylko do odczytu, oparta na istniejących wpisach audytu
(`reporting.AuditEntries`, SF-7). Backend udostępnia stronicowane zapytanie
`GET /api/change-history` z filtrem okresu zmiany, opcjonalnym filtrem daty biznesowej i
rodzaju obiektu. Każdy wpis wie, czy jego obiekt został usunięty. Frontend zastępuje
zaślepkę `/historia-zmian` listą z filtrami, zakresem zmiany z etykietami pól i przejściem do
obiektu, a dla usuniętego obiektu pokazuje znacznik „Usunięto” i ślad usunięcia.

## Context

- **Reporting** (właściciel audytu):
  - `Reporting.Domain` — `AuditRecord`;
  - `Reporting.Application` — nowe zapytanie `ListChangeHistory` z portem odczytu;
  - `Reporting.Infrastructure` — implementacja portu na `ReportingDbContext`;
  - `Reporting.Api` — endpoint `/api/change-history`.

  Reporting czyta wyłącznie własną tabelę, bez odwołań do innych modułów.
- **Shared.Kernel**: `AuditObjectType`, `AuditOperation`, `AuditAuthorKind` (bez zmian).
- **Frontend** `app/src/app/features/change-history/`: dziś zaślepka `ChangeHistoryPage`.
  Wzorzec listy, filtrów, chipów i stronicowania jak w `features/operations/` (SF-17).
- Zapis audytu (`IAuditLog`, SF-7) i format wartości (konwencje, „Value format”) pozostają bez
  zmian.

## Scope

### In scope

- `GET /api/change-history`:
  - filtry: okres zmiany (wymagany), data biznesowa od/do (opcjonalna), rodzaje obiektu (wielokrotny);
  - kierunek sortowania po czasie, strona i rozmiar strony;
  - odpowiedź z listą wpisów i łączną liczbą.
- Dla każdego wpisu znacznik `object.deleted`: istnieje wpis `Deleted` dla tego samego
  `(ObjectType, ObjectId)`.
- Indeks na `ObjectBusinessDate` (migracja addytywna w Reporting).
- Strona `/historia-zmian`:
  - filtry: okres zmiany, data biznesowa, rodzaj obiektu;
  - chipy filtrów i „Wyczyść filtry”;
  - tabela na szerokim ekranie, karty na telefonie;
  - rozwijany zakres zmiany z etykietami pól i sformatowanymi wartościami;
  - przejście do obiektu, a dla usuniętego znacznik „Usunięto” bez linku.
- Katalog pól audytu we frontendzie: etykieta i sposób formatowania każdego pola.
- Testy: domena/aplikacja Reporting, integracyjne API, frontend.

### Out of scope

- Zapis audytu i jego zakres (SF-7 i moduły źródłowe).
- Anonimizacja danych osobowych w audycie (DANE-01, blokowana przez D-09).
- Scenariusz odbioru „wyjaśnienie zmiany raportu” (zadanie 21).
- Eksport listy i wyszukiwanie pełnotekstowe.
- Edycja, usuwanie i ukrywanie wpisów: lista jest tylko do odczytu.

## Behavior

1. Użytkownik otwiera **Historia zmian**. Domyślny okres zmiany to ostatnie 30 dni:
   od dziś minus 29 dni do dziś, w Europe/Warsaw. Filtr daty biznesowej jest pusty,
   rodzaje obiektu wszystkie, sortowanie od najnowszych.
2. Frontend wysyła `GET /api/change-history` z `changedFrom`, `changedTo` (daty
   `yyyy-MM-dd` w Europe/Warsaw), opcjonalnie `businessFrom`, `businessTo`, powtarzanym
   `objectType`, `direction`, `page`, `pageSize`.
3. Backend:
   - zamienia okres zmiany na przedział UTC (istniejący `Shared.Application/Scheduling/WarsawTimeZone`) `[changedFrom 00:00 Europe/Warsaw, changedTo+1 00:00 Europe/Warsaw)`
     z uwzględnieniem czasu letniego;
   - filtruje `OccurredAtUtc` w tym przedziale, a `ObjectBusinessDate` w zakresie daty
     biznesowej, jeśli ją podano (każda granica osobno, włącznie);
   - filtruje `ObjectType` z listy, jeśli ją podano;
   - sortuje po `OccurredAtUtc`, a przy równym czasie po `Id`, w tym samym kierunku;
   - zwraca stronę wpisów i łączną liczbę.
4. Dla każdego wpisu na stronie backend ustala `object.deleted`: czy w audycie jest wpis
   `Deleted` tego obiektu, niezależnie od filtrów. Dotyczy to wszystkich wpisów obiektu, także
   starszych niż usunięcie.
5. Wiersz listy pokazuje:
   - **Czas**: `dd.MM.yyyy HH:mm` w Europe/Warsaw;
   - **Autor**:
     - „Właściciel” dla autora `Account`,
     - „Synchronizacja WooCommerce” dla `WooCommerceSync`,
     - „Zadanie automatyczne” dla `AutomatedJob`;
   - **Obiekt**: etykieta z audytu (już zawiera rodzaj, np. „Wydatek – Paliwo”) i data biznesowa `dd.MM.yyyy`; rodzaj pokazuje się osobno tylko jako tekst pomocniczy dla czytników ekranu (nazwy rodzajów niżej);
   - **Operacja**: „Utworzenie” / „Zmiana” / „Usunięcie”;
   - **Zakres zmiany**: skrót pierwszych dwóch pól w kolejności `changes` z audytu, w postaci „Etykieta: było → jest” oraz „i N więcej”.
6. Rozwinięcie wiersza („Pokaż zmiany”) pokazuje wszystkie pola w tabeli **Pole / Było / Jest**:
   - przy utworzeniu tylko kolumnę „Jest”;
   - przy usunięciu tylko kolumnę „Było”, czyli ostatnie wartości obiektu (ślad usunięcia).
7. Przejście do obiektu:

   | Rodzaj obiektu | Przejście |
   |---|---|
   | Zamówienie | `/zamowienia/:id` |
   | Wpłata, zwrot, korekta sprzedaży | `/zamowienia/:parentId`, zamówienie nadrzędne |
   | Wydatek | `/wydatki/:id` |
   | Wydatek cykliczny (definicja) | `/wydatki/cykliczne/:id` |
   | Wypłata właściciela | `/operacje/wyplaty/:id` |

   Gdy `object.deleted = true`, wiersz zamiast linku ma znacznik „Usunięto” (decyzja 3).
8. Zmiana filtra wraca do strony 1. Filtry i strona zostają zachowane po przejściu do obiektu
   i powrocie, w stanie serwisu, jak w „Operacjach”.
9. Lista nie ma żadnych akcji zmieniających dane. API ma wyłącznie `GET`.

## Data / API

### Storage

- `reporting.AuditEntries`: nowy indeks nieunikalny `IX_AuditEntries_ObjectBusinessDate` na
  `(ObjectBusinessDate)`. Migracja `ReportingDbContext` jest addytywna i zgodna z poprzednią
  wersją API. Poza tym tabela się nie zmienia.
- Istniejący indeks `(ObjectType, ObjectId)` obsługuje sprawdzenie `objectDeleted`, a
  `(OccurredAtUtc)` obsługuje filtr okresu zmiany.

### Endpoints

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| GET | `/api/change-history` | query, opisane niżej | `200 ChangeHistoryPage`; `400 ValidationProblem` | sesja właściciela, jak pozostałe endpointy | Reporting |

Query:

| Parametr | Wymagany | Format | Reguła |
|---|---|---|---|
| `changedFrom`, `changedTo` | tak | `yyyy-MM-dd` | `changedFrom ≤ changedTo` |
| `businessFrom`, `businessTo` | nie | `yyyy-MM-dd` | gdy oba podane, `businessFrom ≤ businessTo` |
| `objectType` | nie, wielokrotny | `order`, `payment`, `refund`, `salesCorrection`, `expense`, `recurringExpenseDefinition`, `ownerWithdrawal` | nieznana wartość daje `400` |
| `direction` | nie | `desc` (domyślnie) albo `asc` | |
| `page` | nie | liczba ≥ 1, domyślnie 1 | |
| `pageSize` | nie | 1–100, domyślnie 25 | |

Błędy walidacji są przypisane do pola, którego dotyczą (`changedFrom` itd.), komunikaty po polsku.
Inne metody HTTP na `/api/change-history` zwracają `405` (test z sesją i tokenem CSRF, jak `OrdersApiTests.Delete_is_not_possible`; bez sesji `401`). `400` z API frontend pokazuje przy polu filtra o tej samej nazwie, a nieznane pole jako komunikat ogólny.

### Types crossing a boundary

`ChangeHistoryPage` (JSON, camelCase):

```text
{ items: ChangeHistoryItem[], total: number, page: number, pageSize: number }

ChangeHistoryItem {
  id: string                // Id wpisu (long jako tekst)
  occurredAt: string        // ISO 8601 UTC z „Z” (EF zwraca DateTimeKind.Unspecified — API jawnie oznacza UTC)
  author: { kind: "account" | "wooCommerceSync" | "automatedJob" }
  object: {
    type: <objectType>, id: string (uuid), businessDate: "yyyy-MM-dd",
    label: string, parent: { type, id } | null, deleted: boolean
  }
  operation: "created" | "updated" | "deleted"
  changes: { field: string, oldValue: string | null, newValue: string | null, isPersonalData: boolean }[]
}
```

Wartości pól są w formacie zapisu audytu („Value format”): kwota `1234.50`, data `yyyy-MM-dd`,
słownik jako kod. Frontend formatuje je według katalogu pól. Pole nieznane katalogowi pokazuje
nazwę i wartość bez zmian.

Katalog pól (frontend). Nazwy pól to camelCase z zapisu audytu i plan je zweryfikuje:

| Obiekt | Pola i formaty |
|---|---|
| Zamówienie | `orderDate` data, `channel` słownik, `value` kwota, `fulfillmentStatus` słownik; `customerKind` słownik, `customerName`, `customerPhone`, `customerEmail`, `customerStreet`, `customerPostalCode`, `customerCity`, `customerTaxId` tekst; `suppliers`, `zoReferences` lista (tablica JSON, wyświetlana po przecinku); `invoiceNumber`, `notes`, `comment` tekst; `hasPhotos`, `hasReview`, `shopDeleted` tak/nie; `shopStatus` słownik; `shopRefundedAmount` kwota |
| Wpłata | `amount` kwota, `date` data, `method` słownik metod wpłaty, `receivedBy` tekst |
| Zwrot | `kind` słownik rodzajów zwrotu, `amount` kwota, `date` data, `method` słownik metod zwrotu, `reason` tekst |
| Korekta sprzedaży | `amount` kwota, `date` data, `reason` tekst |
| Wydatek | `description`, `counterparty`, `documentReference`, `payer`, `comment` tekst; `category`, `paymentMethod` słownik; `costDate`, `dueDate`, `paidOn` data; `grossAmount`, `netAmount` kwota |
| Wydatek cykliczny | `description`, `counterparty`, `payer`, `comment` tekst; `category`, `paymentMethod` słownik; `grossAmount`, `netAmount` kwota; `dayOfMonth` liczba; `startsOn`, `endsOn`, `pausedOn` data |
| Wypłata właściciela | `person` tekst, `amount` kwota, `withdrawnOn` data, `method` słownik |

Słownik zależy od pary (rodzaj obiektu, pole). Etykiety słowników pochodzą z istniejących stałych funkcji (kategorie i metody wydatków, metody wpłat i zwrotów, rodzaje zwrotu, kanały, stany realizacji, `shop-status.ts`). Stałe używane przez więcej niż jedną funkcję przenosi się do `app/src/app/shared/` (bez importów między funkcjami); plan wskazuje konkretne pliki.

Nazwy rodzajów obiektu: Zamówienie, Wpłata, Zwrot, Korekta sprzedaży, Wydatek, Wydatek cykliczny, Wypłata właściciela.

Format wyświetlania:
- kwota: `formatPln`;
- data: `dd.MM.yyyy`;
- słownik: istniejące etykiety z modeli funkcji (kategorie, metody, kanały, statusy); nieznany kod pokazuje się bez zmian;
- tak/nie: „tak”/„nie”;
- lista: elementy po przecinku;
- brak wartości: „Brak danych” (`NO_DATA`, DESIGN-GUID).

## Invariant impact

| Invariant | How it is respected |
|---|---|
| 3. API-enforced access | Endpoint wymaga sesji właściciela jak pozostałe; test braku sesji (`401`) w `EndpointSecurityTests`. |
| 5. Financial audit trail | Lista tylko czyta audyt. Brak endpointów zapisu, a inne metody dają `405`. Usunięty obiekt jest widoczny przez znacznik i wpis usunięcia z ostatnimi wartościami. |
| 7. Module and layer boundaries | Reporting czyta tylko `reporting.AuditEntries`. Domena bez EF i HTTP, port odczytu w Application. Testy architektury bez wyjątków. |
| 8. No identifying data in docs | Przykłady i scenariusze używają danych fikcyjnych. |
| 2. Backend decimal money | Brak obliczeń na kwotach. Frontend tylko formatuje tekst kwoty z audytu. |

## Risk

**high** (bez zmian): lista pokazuje dane finansowe i osobowe z audytu, jest jedynym
narzędziem wyjaśnienia zmian raportów (AUD-01). Wymaga przeglądu człowieka przed merge.

## UI

- **Trasa** `/historia-zmian` (pozycja nawigacji już istnieje). Zaślepkę zastępuje
  `ChangeHistoryPage`.
- **Komponenty**:
  - `ChangeHistoryPage` ze stanem w `ChangeHistoryService`;
  - API `change-history-api.ts`;
  - katalog pól `audit-field-catalog.ts`.
  Wykorzystane istniejące elementy: `app-data-table`, filtry i chipy jak w „Operacjach”, pola dat.
- **Filtry**:
  - „Zmiana od” / „Zmiana do”, wymagane;
  - „Data biznesowa od” / „Data biznesowa do”, opcjonalne;
  - „Rodzaj obiektu”: pola wyboru dla 7 rodzajów.

  Chipy:
  - „Zmiana: dd.MM.yyyy – dd.MM.yyyy”;
  - „Data biznesowa: …” (z jedną granicą „od …” albo „do …”);
  - „Rodzaj: …”.

  „Wyczyść filtry” przywraca domyślne. Na telefonie filtry są w rozwijanym panelu.
- **Kolumny**: Czas (sortowanie asc/desc, jedyne sortowalne), Autor, Obiekt (rodzaj, etykieta,
  data biznesowa; link albo znacznik „Usunięto”), Operacja, Zakres zmiany (skrót i przycisk
  „Pokaż zmiany” / „Ukryj zmiany”).
- **Stany**:
  - ładowanie: szkielet tabeli;
  - pusto bez wpisów w audycie przy domyślnych filtrach: „Nie ma jeszcze żadnych zmian.”; pusto po filtrach: „Brak zmian dla wybranych filtrów.” z „Wyczyść filtry”;
  - błąd sieci: „Nie udało się wczytać historii zmian.” z „Spróbuj ponownie”;
  - błąd walidacji: komunikat przy polu, bez zapytania;
  - lista ze stronicowaniem 25 na stronę.
- **Akcja główna**: brak, widok tylko do odczytu. Strona nie ma przycisków dodawania ani edycji.
- **Tokeny**: znaczniki operacji przez istniejący `app-status-badge`:
  - utworzenie: neutralny;
  - zmiana: `info`;
  - usunięcie i „Usunięto”: `warning`.

  Tekst nigdy nie opiera się tylko na kolorze.
- **Dostępność**:
  - przycisk rozwinięcia ma `aria-expanded` i `aria-controls`;
  - tabela zmian ma nagłówki `th scope`;
  - czas w `<time datetime>`;
  - znacznik „Usunięto” jest tekstem;
  - fokus wraca na przycisk po zwinięciu.

## Edge Cases

- **Czas letni.** Okres obejmujący zmianę czasu (np. 25–26.10.2026) liczy przedział UTC z
  właściwym przesunięciem. Wpis o 23:30 czasu polskiego trafia do swojego dnia.
- **Filtr daty biznesowej z jedną granicą**, np. tylko „od”, działa bez drugiej.
- **Zmiana wykonana w listopadzie obiektu z datą październikową** jest widoczna przy okresie
  zmiany obejmującym listopad i dacie biznesowej w październiku.
- **Filtr daty biznesowej porównuje datę biznesową zapisaną we wpisie**, czyli przy zmianie datę po zmianie. Zmiana, która przesunęła wydatek z października na listopad, nie trafi do filtra „październik”, tylko „listopad”; stara data jest widoczna w zakresie zmiany. Data zapłaty (`paidOn`) nie jest datą biznesową wydatku. Pod filtrem podpowiedź: „Data biznesowa: data zamówienia, wpłaty, zwrotu, korekty, kosztu, startu cyklu albo wypłaty — po zmianie.”
- **Obiekt usunięty po okresie filtra.** Wpis z okresu i tak ma `deleted = true`, bo sprawdzenie
  nie zależy od filtrów.
- **Wpłata usunięta z istniejącego zamówienia** ma znacznik „Usunięto” i nie ma linku (decyzja 3).
- **Wydatek cykliczny z usuniętą definicją**: wpisy wystąpień linkują do wydatku, wpisy
  definicji mają znacznik.
- **Wpisy z jednakowym `OccurredAtUtc`** mają stabilną kolejność po `Id`.
- **Pole nieznane katalogowi** (np. dodane w przyszłości) pokazuje się bez wyjątku jako nazwa
  i surowa wartość.
- **Pola z danymi osobowymi** (`isPersonalData`) są widoczne dla właściciela bez zmian.
  Anonimizacja jest poza zakresem.
- **`pageSize` > 100, `page` 0, zła data, nieznany `objectType`** dają `400`.
- **Strona poza zakresem** zwraca pustą listę z poprawnym `total`.

## Dependencies

- SF-7 (zapis audytu i tabela `reporting.AuditEntries`) oraz SF-8, wymagane w zadaniu: oba
  zrobione.
- Linki do wydatków cyklicznych i wypłat zakładają SF-16 (w `main`) i SF-17. Gałąź wychodzi
  z `sf-17-owner-payouts`, a PR #17 musi być zmergowany przed tym zadaniem.

## Assumptions

1. **Pytanie:** filtr po dacie biznesowej? **Odpowiedź:** tak, opcjonalny „Data biznesowa
   od–do”, domyślnie pusty.
2. **Pytanie:** domyślny okres? **Odpowiedź:** ostatnie 30 dni (dziś minus 29 dni – dziś,
   Europe/Warsaw), najnowsze na górze.
3. **Pytanie:** wpisy usuniętego obiektu? **Odpowiedź:** wszystkie wpisy obiektu z wpisem
   `Deleted` mają znacznik „Usunięto” i nie mają linku. Rozwinięty wpis usunięcia pokazuje
   ostatnie wartości.
4. **Pytanie:** autor zmiany z konta? **Odpowiedź:** stała etykieta „Właściciel” (jedno konto,
   bez odczytu z Access).
5. Okres zmiany jest wymagany i nie ma limitu długości, bo lista jest stronicowana w SQL.
   Brak bezpiecznika jak w „Operacjach”, bo niczego nie sumujemy.
6. Etykiety pól i formatowanie wartości są we frontendzie, a API zwraca kody z audytu. Frontend
   ma już słowniki etykiet kategorii, metod i statusów, a Reporting nie zna modeli innych
   modułów.
7. Dane osobowe w zakresie zmiany są widoczne dla właściciela. Anonimizacja (DANE-01) usunie
   je z audytu w przyszłym zadaniu.
8. Wpłata, zwrot i korekta nie mają własnej strony, więc prowadzą do zamówienia nadrzędnego.
9. Daty audytu nie są przycinane do daty uruchomienia. Historia pokazuje wszystkie wpisy.
10. Filtr daty biznesowej działa na dacie zapisanej we wpisie (po zmianie), bez przeszukiwania starych dat w zakresie zmiany (przegląd v1, uwaga 1).
11. Zamówień nie usuwa się (PRD sekcja 5), więc link do zamówienia nadrzędnego wpłaty, zwrotu i korekty jest zawsze ważny; znacznik „Usunięto” dotyczy samego wpisu finansowego.
12. Domena Reporting nie dostaje nowej reguły; walidacja zapytania i przedział czasu są w Application.

## Acceptance Criteria

- [ ] `GET /api/change-history` zwraca wpisy z okresu zmiany, liczonego w Europe/Warsaw, wraz z
      autorem, obiektem z datą biznesową, operacją i zakresem zmiany oraz `total`.
- [ ] Filtry okresu zmiany, daty biznesowej i rodzaju obiektu działają osobno i łącznie;
      stronicowanie i kierunek sortowania działają.
- [ ] Wpisy obiektu z wpisem usunięcia mają `deleted = true`, a w UI znacznik „Usunięto” bez
      linku. Wpis usunięcia pokazuje ostatnie wartości.
- [ ] Wpis istniejącego obiektu prowadzi do właściwej strony: zamówienia (także dla
      wpłaty, zwrotu i korekty), wydatku, wydatku cyklicznego albo wypłaty.
- [ ] API nie pozwala niczego zmienić: tylko `GET`, inne metody dają `405`, a bez sesji jest `401`.
- [ ] Czasy w UI są w Europe/Warsaw, z poprawnym przejściem przez zmianę czasu.
- [ ] Zakres zmiany pokazuje polskie etykiety pól i sformatowane wartości. Nieznane pole nie
      psuje widoku.
- [ ] Testy architektury przechodzą bez nowych wyjątków.

## Verification

- build: `dotnet build -c Release -m:1`, `dotnet format --verify-no-changes`; frontend typecheck,
  `ng build`, lint, prettier.
- tests:
  - `StoneFlow.Reporting.Tests`: walidacja zapytania, przedział Warsaw → UTC, w tym DST;
  - `StoneFlow.Architecture.Tests`;
  - integracyjne `ChangeHistoryApiTests` na LocalDB albo kontenerze: filtry, `deleted`,
    stronicowanie, `405`, `401`, kolejność;
  - frontend `ng test`: serwis, katalog pól, strona;
  - `has-pending-model-changes` dla `ReportingDbContext`.
- manual: scenariusze UI w `06-implementation-done.md`. Dodaj wydatek, popraw go i usuń, potem
  sprawdź trzy wpisy, znacznik i ślad usunięcia. Sprawdź też filtr daty biznesowej, przejścia
  do obiektów i widok telefonu.
