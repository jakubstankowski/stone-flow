# Spec review — SF-7

## Summary

- Overall assessment: FAIL
- Justification: Specyfikacja dobrze pokrywa zgłoszenie (autor, czas UTC, obiekt z datą
  biznesową, operacja, zakres zmiany, ślad usunięcia, wspólna transakcja, jawny interfejs) i nie
  narusza niezmienników. Dwie rzeczy trzeba jednak poprawić przed planem. Pierwsza to rdzeń
  zadania: EF Core sam nie dołącza kontekstów do cudzej `DbTransaction`, a specyfikacja nie mówi,
  jak to ma działać. Druga dotyczy anonimizacji, którą zgłoszenie każe uwzględnić w interfejsie:
  wpis nie zapisuje powiązania obiektów podrzędnych (wpłata, zwrot, korekta sprzedaży) z
  zamówieniem, a wpisów zapisanych przez SF-8 i późniejsze zadania nie da się potem uzupełnić,
  zwłaszcza dla obiektów usuniętych.

## Findings

### Critical Issues

1. **Dołączanie kontekstów do transakcji nie jest opisane, a opis dosłowny nie zadziała**
   (Context — „Wspólna transakcja”; Behavior, kroki 1.1–1.2). Specyfikacja zakłada, że „każdy
   kontekst automatycznie dołącza do otwartej transakcji” i że `SaveChanges` kontekstu modułu
   „dołącza do transakcji”. EF Core nie robi tego sam. Kontekst o wspólnym `SqlConnection` z
   aktywną transakcją lokalną, któremu nie wywołano `Database.UseTransaction(...)`:
   - przy `SaveChanges` (domyślnie `AutoTransactionBehavior.WhenNeeded`) próbuje otworzyć własną
     transakcję, a `SqlConnection` odrzuca to wyjątkiem o równoległych transakcjach;
   - przy każdym innym poleceniu (zapytanie odczytu w handlerze, `ExecuteUpdate`/`ExecuteDelete`)
     dostaje od SqlClient wyjątek „command … requires a transaction … pending local transaction”.

   Konteksty modułów i `ReportingDbContext` (zależność `IAuditLog`) powstają w zakresie DI zwykle
   przed wywołaniem `ExecuteInTransactionAsync`, a `Shared.Infrastructure` nie może znać ich typów
   (`LayerDependencyTests.Shared_does_not_depend_on_modules`). Specyfikacja musi ustalić:
   - jak jednostka pracy poznaje uczestniczące konteksty, np. przez rejestrację uczestnika obok
     `AddDbContext` w module i rejestr w zakresie;
   - jak dołącza konteksty utworzone przed rozpoczęciem transakcji i po nim;
   - że dołączenie obejmuje każde polecenie, nie tylko `SaveChanges`;
   - że po commicie lub rollbacku konteksty odłączają transakcję (`UseTransaction(null)`).

   Do tego potrzebne są kryteria akceptacji: odczyt, `ExecuteUpdate` i `SaveChanges` kontekstu
   modułu wewnątrz jednostki pracy działają w jednej transakcji, a test integracyjny sprawdza to
   na co najmniej dwóch kontekstach (moduł testowy i Reporting).

2. **Brak powiązania wpisu z zamówieniem nadrzędnym blokuje anonimizację i utrudnia SF-18**
   (Data / API — Storage; Types crossing a boundary; Out of scope — anonimizacja; Edge Cases —
   ostatni punkt). `AuditObject` ma tylko rodzaj, identyfikator, datę i etykietę. Dla `Payment`,
   `Refund` i `SalesCorrection` identyfikatorem jest wpis podrzędny, a nie zamówienie.
   - SF-22 (DANE-01) ma usunąć dane klienta „z wpisów audytu”, a breakdown zadania 22 wprost
     wymienia „powód zwrotu, powód korekty” jako możliwe dane osobowe. Po usunięciu zwrotu
     moduł Orders nie zna już jego identyfikatora, a wpis audytu nie wskazuje zamówienia. Nie da
     się więc odnaleźć wpisów do anonimizacji bez parsowania JSON lub etykiety.
   - SF-18 („wpis prowadzi do obiektu”) dla wpłaty lub zwrotu potrzebuje zamówienia, w którego
     szczegółach ten obiekt jest widoczny.
   - Wpisów z SF-8 i późniejszych zadań nie da się potem uzupełnić, bo usuniętych obiektów
     podrzędnych nie ma już w Orders. Zgodnie z zasadą expand/contract kolumna dodana później
     zostałaby pusta dla historii.

   Zgłoszenie wymaga, by interfejs zapisu powstał „z uwzględnieniem anonimizacji”. Specyfikacja
   powinna dodać do obiektu odniesienie nadrzędne, np. `ParentType`/`ParentId` albo `OrderId`,
   wymagane dla obiektów podrzędnych zamówienia, z indeksem. Powinna też określić, po jakim
   kluczu SF-22 wyszukuje wpisy klienta.

### Non-Critical Issues

1. **Zatwierdzenie transakcji przy wyniku-porażce** (Behavior, punkt 2; Types — `IUnitOfWork`).
   Rollback następuje tylko po wyjątku. Konwencje każą jednak tłumaczyć znane naruszenia
   ograniczeń (2601/2627) i konflikty współbieżności na wynik portu, nie na wyjątek. Gdy handler
   wykryje konflikt po części zapisów i zwróci wynik `Conflict`, `ExecuteInTransactionAsync`
   zatwierdzi częściowe zmiany razem z wpisem audytu. Specyfikacja powinna ustalić, jak `work`
   sygnalizuje wycofanie bez wyjątku (np. predykat sukcesu albo wynik z flagą), albo zakazać
   zapisów po wykryciu porażki. Dodatkowo błąd SQL bez `XACT_ABORT` nie zawsze unieważnia
   transakcję.
2. **Rejestracja wspólnego połączenia a istniejące testy i kontekst projektowy** (Context —
   „Wspólna transakcja”). `DbContextSchemaTests` buduje dostawcę z samym `Add{Module}Infrastructure()`,
   bez `AddSharedInfrastructure()`. Jeżeli połączenie zarejestruje tylko Shared, te testy przestaną
   działać. Specyfikacja powinna wskazać, gdzie rejestruje się połączenie (np. `TryAdd` w helperze
   używanym przez moduły). Musi też pozostać `optionsLifetime` Scoped, czyli bez `AddDbContextPool`,
   bo opcje rozwiązują połączenie z zakresu.
3. **`dotnet ef` i migracje z połączeniem przekazanym z zewnątrz** (Verification). `migrate.sh`
   uruchamia `dotnet ef database update` przez host. `TestDatabase.MigrateAsync` migruje wszystkie
   konteksty w jednym zakresie, czyli po zmianie na jednym współdzielonym `SqlConnection`. Przy
   uwierzytelnianiu SQL (kontener testowy, hosting) `SqlConnection.ConnectionString` po pierwszym
   otwarciu nie zawiera hasła (`Persist Security Info=False`). Każda ścieżka, która buduje z niego
   nowe połączenie, np. połączenie do `master` przy tworzeniu bazy przez EF, przestaje wtedy
   działać. Specyfikacja powinna wymagać przejścia `test-migrate.sh` i testów integracyjnych na
   kontenerze z uwierzytelnianiem SQL, a nie tylko na LocalDB z Integrated Security. Health check
   nie jest zagrożony, bo otwiera własne połączenie z konfiguracji.
4. **Zakres DI poza żądaniem HTTP** (Context — „jedno połączenie SQL na zakres DI (żądanie)”).
   Synchronizacja WooCommerce (SF-12/13) i zadania automatyczne (SF-11/16) działają poza żądaniem.
   Specyfikacja powinna wymagać osobnego zakresu na każdą jednostkę pracy zadania. Inaczej po
   rollbacku w zakresie zostaje połączenie i `ChangeTracker` z encjami z wycofanej próby.
5. **Brak zmian a ponowienia** (Behavior, punkt 7; Edge Cases). „Lista nie może być pusta”, ale
   specyfikacja nie mówi, co robi wywołujący, gdy `Updated` nic nie zmienił, np. przy ponownym
   dostarczeniu zamówienia WooCommerce bez różnic (niezmiennik 4). Jeżeli wywoła `RecordAsync` z
   pustą listą, wyjątek wycofa całą operację. Trzeba dopisać regułę: brak zmienionych pól oznacza
   brak wywołania i brak wpisu.
6. **Zmiana właściciela danych względem propozycji 1a** (Assumptions — Q1). Wybór Reportingu jest
   zgodny z PRD sekcja 10 i uzasadniony, ale odbiega od propozycji przyjętej przez właściciela
   ogólnym „rób wszystko sam”. Przy ryzyku high warto, żeby człowiek potwierdził to wprost przy
   przeglądzie. Nie blokuje.
7. **Brak aktualizacji konwencji** (Scope — In scope). `conventions.md` (CQRS, „Data access”)
   opisuje transakcje przez port modułu (`IAccessUnitOfWork`) i atomowość pojedynczego
   `SaveChanges`. Nowe zasady nie są tam opisane: wspólne `IUnitOfWork` dla modułów finansowych,
   obowiązkowy audyt w jednostce pracy i `RecordAsync` odrzucane poza transakcją. Specyfikacja
   powinna objąć aktualizację konwencji.
8. **Etykieta i dane osobowe** (Assumptions, ostatni punkt). „Etykieta nie powinna zawierać danych
   osobowych” to zalecenie, a SF-22 anonimizuje tylko pola oznaczone `personal`. Reguła powinna
   być wymaganiem kontraktu, np. dokumentacją `AuditObject.Label` z listą dozwolonej treści per
   rodzaj obiektu. Inaczej etykieta wydatku z nazwą kontrahenta-osoby wymknie się anonimizacji.
   Podobnie poprawność flagi `IsPersonalData` zależy w całości od wywołującego. Warto zapisać, że
   każdy moduł-wywołujący testuje ją dla swoich pól.
9. **Weryfikacja** (Verification). Polecenia różnią się od `conventions.md` (`restore --locked-mode`,
   `format --verify-no-changes`, `--no-restore`/`--no-build`). `test-migrate.sh` wymaga
   `STONEFLOW_TEST_SQLSERVER` i `SQLCMD`, czego specyfikacja nie podaje.

### Unclear or Ambiguous Sections

- **Data / API — `ObjectBusinessDate`.** Nie wiadomo, która data jest „datą biznesową” obiektów z
  kilkoma datami: wydatek (data kosztu albo data zapłaty), definicja cykliczna (początek, koniec
  albo brak daty), zamówienie (data zamówienia). Nie wiadomo też, którą wartość zapisać przy
  `Updated`, gdy zmienia się sama data (stara czy nowa). AUD-01 wymaga, by po zmianie raportu za
  miniony okres dało się wskazać zmianę, która go wywołała. Bez reguły per rodzaj obiektu SF-18
  (gdzie filtr po dacie biznesowej jest „do rozstrzygnięcia”) nie ma na czym oprzeć filtra.
- **Behavior, punkt 7 — format wartości.** Ustalony jest tylko format kwot. Brakuje formatu dat
  (ISO `yyyy-MM-dd`?), wartości słownikowych (kod czy polska etykieta?), wartości logicznych i
  nazw pól (identyfikator techniczny czy etykieta UI). Od tego zależy prezentacja w SF-18 i
  stabilność historii po zmianie etykiet.
- **Behavior, punkt 7 — „stara pusta”, „nowa pusta”.** Nie wiadomo, czy „pusta” to `null`, czy
  pusty tekst. Projekt odróżnia brak danych od pustej wartości („Zero, missing data and incomplete
  data mean three different things”).
- **Data / API — `ObjectType`.** Nie wiadomo, czy wystąpienie wydatku cyklicznego (tworzone przez
  zadanie automatyczne, będące kosztem według D-04) jest audytowane jako `Expense`, czy potrzebuje
  własnego rodzaju.
- **Behavior, punkt 8 / Invariant impact 5.** „Wpisy są tylko dodawane” dotyczy kontraktu
  `IAuditLog`. Warto dopisać, że nie powstaje blokada na poziomie bazy (trigger, `DENY UPDATE`) i
  że SF-22 doda osobną, należącą do Reporting operację nadpisania wartości `personal`. Inaczej
  zapis można odczytać jako zakaz, który zablokuje DANE-01.
- **Behavior, punkt 3.** Nie wiadomo, skąd implementacja `IAuditLog` wie o aktywnej transakcji
  jednostki pracy (wspólny stan w zakresie?) i czy sprawdza, że to transakcja tego samego
  połączenia.

### Invented or Unsupported Requirements

None. Odrzucanie zapisu audytu poza transakcją, zakaz zagnieżdżania i check constraint autora
wynikają z wymogu „zapis w tej samej transakcji” i z kryterium „autor systemowy odróżniony od
konta”. Oznaczanie danych osobowych wynika z polecenia, by uwzględnić anonimizację.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Context — „Wspólna transakcja”; Types — `IUnitOfWork` | `conventions.md` — CQRS („several writes … go through the module's unit-of-work port (`IAccessUnitOfWork`)”), Data access | Nowa wspólna jednostka pracy zmienia opisaną konwencję. Nie jest to sprzeczność z decyzją, ale konwencję trzeba zaktualizować w zakresie zadania. |
| Context — właściciel danych (Reporting) | Assumptions — propozycja 1a (nowy moduł `Audit`) | Odejście od przyjętej propozycji, zgodne z PRD sekcja 10. Do potwierdzenia przez człowieka, bez konfliktu z PRD. |
| Data / API — obiekt bez odniesienia nadrzędnego | PRD sekcja 5 „Anonimizacja danych klienta”, DANE-01; breakdown zadanie 22 | Model wpisu nie pozwala odnaleźć wpisów obiektów podrzędnych zamówienia (patrz Critical 2). |
| Behavior, kroki 1.1–1.2 | `LayerDependencyTests` (Shared nie zależy od modułów), `DbContextSchemaTests` (rejestracja modułu bez Shared) | Mechanizm dołączania i rejestracji połączenia nie jest opisany tak, by zmieścił się w tych regułach (patrz Critical 1, Non-critical 2). |

Brak konfliktu z D-05 (autor = jedyne konto albo autor systemowy) i z D-09 (anonimizacja nie jest
wdrażana).

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | Not applicable | Brak obliczeń sprzedaży i przepływów. |
| 2. Backend decimal money | OK | Kwoty w audycie to tekst sformatowany niezmienną kulturą; audyt niczego nie liczy. |
| 3. API-enforced access | Not applicable | Brak endpointów; odczyt dopiero w SF-18. |
| 4. Retries never duplicate | OK | Brak ponowień w zadaniu; reguła „brak zmian → brak wpisu” do dopisania (Non-critical 5). |
| 5. Financial audit trail | OK | Wymagane pola, ślad usunięcia przez migawkę, zapis w transakcji zmiany. Skuteczność zależy od rozwiązania Critical 1. |
| 6. Secrets stay server-side | OK | Wpis nie zawiera sekretów; połączenie bez zmian w konfiguracji. |
| 7. Module and layer boundaries | OK | Kontrakt w `Shared.Application`, implementacja w Reporting, brak FK do `access` i tabel innych modułów. Mechanizm dołączania musi zachować zakaz zależności Shared od modułów. |
| 8. No identifying data in docs | OK | Dane fikcyjne w testach i przykładach. |

## Risk

- Recorded: high
- Assessed: high. Zadanie dotyczy danych audytu finansowego (niezmiennik 5), zmienia obsługę
  transakcji i połączeń wszystkich modułów finansowych i ustala model danych, który później
  anonimizuje się nieodwracalnie. Wymagany przegląd człowieka przed merge.

## Assumptions Detected

| Założenie | Podane w specyfikacji |
|---|---|
| Właścicielem danych jest Reporting (zamiast modułu `Audit`) | Tak (Q1) |
| Zakres zmiany jako lista pól w JSON z flagą danych osobowych | Tak (Q2) |
| Obiekt = rodzaj, identyfikator, data biznesowa, etykieta | Tak (Q3) |
| Autor przekazywany jawnie przez handler | Tak (Q4) |
| Jawne wywołanie zamiast interceptora EF | Tak (Q5) |
| Wpisy tylko dodawane, bez endpointu | Tak (Q6) |
| Identyfikatory obiektów finansowych typu `Guid` | Tak |
| Access poza wspólnym połączeniem | Tak |
| Etykieta bez danych osobowych | Tak, ale jako zalecenie (Non-critical 8) |
| EF Core dołącza konteksty do wspólnej transakcji „automatycznie” | Nie, i jest to nieprawdziwe bez dodatkowego mechanizmu (Critical 1) |
| Porażka zgłaszana zawsze wyjątkiem (rollback tylko po wyjątku) | Nie (Non-critical 1) |
| Obiekt podrzędny zamówienia nie potrzebuje odniesienia do zamówienia | Nie (Critical 2) |
| Zakres DI = żądanie HTTP; zadania tworzą własne zakresy | Nie (Non-critical 4) |
| Jedna data biznesowa wystarcza dla każdego rodzaju obiektu | Nie (Unclear — `ObjectBusinessDate`) |
| Wywołujący nie zapisuje wpisu, gdy nic się nie zmieniło | Nie (Non-critical 5) |
| Wspólne połączenie działa z `dotnet ef` i uwierzytelnianiem SQL | Nie (Non-critical 3) |

## Recommendation

- Revise specification. Najpierw dwa punkty krytyczne: mechanizm dołączania kontekstów do
  transakcji z kryteriami testowymi oraz odniesienie do zamówienia nadrzędnego w modelu wpisu.
  Przy okazji warto domknąć obsługę wyniku-porażki w `IUnitOfWork`, regułę daty biznesowej per
  rodzaj obiektu i format wartości. Po poprawkach ponowić `sdd-review-spec SF-7`.
