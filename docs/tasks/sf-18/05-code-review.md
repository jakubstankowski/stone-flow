# Code review — SF-18

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Implementacja pokrywa spec v2 i plan: zapytanie `ListChangeHistory` z walidacją
  przypisaną do pól, przedział Europe/Warsaw → UTC liczony przez `WarsawTimeZone` (DST sprawdzony
  testami 23 h / 25 h), filtry, `COUNT`, stabilne sortowanie (`OccurredAtUtc`, `Id`) i skorelowane
  `EXISTS` dla `deleted` w SQL, `occurredAt` z „Z”, tylko `MapGet`. Frontend ma katalog pól zgodny z
  nazwami z `OrderField`/`EntryField`/`ExpenseField`/`RecurringDefinitionField`/`OwnerWithdrawalField`
  i kodami słowników, poprawne linki (także dla wpłat przez rodzica i dla usuniętych obiektów),
  przeniesienie słowników bez zmiany etykiet i `mobileCards` domyślnie wyłączone. Brak naruszeń
  niezmienników i uwag krytycznych; zostaje kilka drobnych uwag (skrajna data → 500, stan pustej
  listy przy brakującej dacie, „dziś” ustalone przy starcie serwisu, nazwy przycisków rozwinięcia).
- Risk: high (wymagany przegląd człowieka przed merge)

Sprawdzone uruchomieniem (2026-11-09, w tej sesji):

- `dotnet build stone-flow.slnx -c Release -m:1` — 0 ostrzeżeń, 0 błędów;
- `dotnet test --project api/tests/StoneFlow.Reporting.Tests -c Release --no-build` — 129/129;
- `dotnet test --project api/tests/StoneFlow.Architecture.Tests -c Release --no-build` — 56/56;
- `npx ng test --watch=false` dla `features/change-history`, `shared`, `features/orders`,
  `features/expenses`, `features/operations`, `layout`, `app.routes.spec.ts` — 56 plików, 623/623;
- `npm run typecheck`, `npm run lint` (w tym `lint:colors`), `npm run format:check` — bez błędów;
- grep importów między obszarami (`features/<a>` → `features/<b>`, plan F-H-7) — pusty wynik.

Nie uruchamiano (zgodnie z zakresem przeglądu): testów integracyjnych (`ChangeHistoryApiTests`,
`EndpointSecurityTests`, `OpenApiTests`), `dotnet format --verify-no-changes`,
`has-pending-model-changes`, `ng build`. Testy integracyjne przeczytano, nie uruchomiono.

## Project Invariant Violations

| Invariant | File:line | What is wrong |
|---|---|---|

None.

Sprawdzenie po kolei:

| Invariant | Wynik |
|---|---|
| 1. Sales separate from cash | nie dotyczy — lista nie liczy sprzedaży ani przepływów |
| 2. Backend decimal money | spełniony — brak arytmetyki; `formatFieldValue` formatuje tekst kwoty przez `formatPln` (`audit-field-catalog.ts:152-153`) |
| 3. API-enforced access | spełniony — fallback policy `RequireAuthenticatedUser` (`api/src/StoneFlow.Api/Security/SecuritySetup.cs:17-20`), endpoint bez `AllowAnonymous`; test `Anonymous_requests_are_rejected` (`ChangeHistoryApiTests.cs:226-237`) |
| 4. Retries never duplicate | nie dotyczy — wyłącznie odczyt |
| 5. Financial audit trail | spełniony — tylko `MapGet` (`ChangeHistoryEndpoints.cs:22`), zapis audytu bez zmian, test `405` z liczbą wierszy (`ChangeHistoryApiTests.cs:210-224`); `deleted` i ślad usunięcia z ostatnimi wartościami |
| 6. Secrets stay server-side | spełniony — `AuthorAccountId` nie wychodzi z portu (`ChangeHistoryPorts.cs:26-38`), test `DoesNotContain("accountId")` (`ChangeHistoryApiTests.cs:60`) |
| 7. Module and layer boundaries | spełniony — Reporting czyta tylko `reporting.AuditEntries`; port w Application, reader w Infrastructure; testy architektury 56/56; frontend bez importów między obszarami |
| 8. No identifying data in docs | spełniony — dane fikcyjne (`Wydatek – Cięcie`, `ZAM/2026/11/0001`) |

## Conventions Violations

### Critical (must fix before merge)

None.

### Non-Critical (should fix)

- `app/src/app/features/change-history/change-history-page.html:161-169` — wszystkie przyciski
  rozwinięcia mają tę samą nazwę dostępną „Pokaż zmiany” / „Ukryj zmiany”. Na liście 25 wierszy
  czytnik ekranu (lista przycisków, nawigacja po kontrolkach) nie odróżnia, którego wpisu dotyczy
  przycisk. Spec wymaga tylko `aria-expanded`/`aria-controls` (spełnione), ale dopisanie kontekstu
  (np. `aria-describedby` na etykietę obiektu albo tekst `sr-only`) poprawiłoby zgodność z WCAG 2.4.6.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| `GET /api/change-history` z filtrami, kierunkiem, stroną, `total` | Covered | `ChangeHistoryEndpoints.cs`, `ListChangeHistory.cs`, `ChangeHistoryReader.cs` |
| Okres zmiany Europe/Warsaw → UTC `[from 00:00, to+1 00:00)`, DST | Covered | `ChangeHistoryPeriod.cs:12-18`; testy 23 h/25 h i 23:30 (`ChangeHistoryPeriodTests.cs`), integracyjny `Change_period_is_counted_in_warsaw_time_across_the_end_of_summer_time` |
| Data biznesowa od/do, każda granica osobno, włącznie | Covered | `ChangeHistoryReader.cs:21-29`; I-H-4 |
| Rodzaje obiektu (wielokrotny), nieznany → `400` | Covered | `ListChangeHistory.cs:64-75` (jeden błąd na klucz, duplikaty usunięte) |
| Sortowanie `OccurredAtUtc`, potem `Id` w tym samym kierunku | Covered | `ChangeHistoryReader.cs:44-46`; I-H-5 |
| Strona poza zakresem → puste `items` z `total` | Covered | `ChangeHistoryReader.cs:37-42` (skip jako `long`, bez przepełnienia) |
| `object.deleted` niezależne od filtrów | Covered | `ChangeHistoryReader.cs:64-67` (`EXISTS` na całej tabeli); I-H-7 |
| `occurredAt` ISO 8601 UTC z „Z” | Covered | `ChangeHistoryReader.cs:75` + `ChangeHistoryCodes.cs:65-66`; test I-H-1 sprawdza `…0000000Z` |
| Walidacja przy polu, komunikaty po polsku, `pageSize` 1–100, `page` ≥ 1 | Partial | wszystkie przypadki ze spec dają `400` z kluczem; skrajna poprawna data `changedTo=9999-12-31` daje `500` (Correctness Issues, 1) |
| Indeks `IX_AuditEntries_ObjectBusinessDate`, migracja addytywna | Covered | `AuditRecordConfiguration.cs:47`, migracja tylko `CreateIndex`/`DropIndex`, snapshot zaktualizowany |
| Tylko `GET`; inne metody `405`, bez sesji `401` | Covered | testy `Change_history_cannot_be_modified`, `Anonymous_requests_are_rejected` (nieuruchomione w przeglądzie) |
| Domyślny okres: dziś −29 dni … dziś, Europe/Warsaw | Covered | `change-history.service.ts:47-53`; uwaga o północy (Correctness Issues, 3) |
| Filtry, chipy, „Wyczyść filtry”, panel na telefonie | Covered | `change-history.service.ts:142-170`, `change-history-page.html` (`filterable`) |
| Tabela / karty na telefonie | Covered | `mobileCards` w `data-table.html`; inne listy bez zmian (test „keeps the plain table layout by default”) |
| Wiersz: czas, autor, obiekt z datą biznesową i rodzajem `sr-only`, operacja, skrót | Covered | czas w formacie `formatDateTime` (`dd.MM.yyyy, HH:mm`) — znane, zaakceptowane odstępstwo (plan P8) |
| Rozwinięcie Pole/Było/Jest; utworzenie bez „Było”, usunięcie bez „Jest” | Covered | `change-history-page.html:172-206`; test w `change-history-page.spec.ts` |
| Przejście do obiektu, rodzic dla wpłaty/zwrotu/korekty, „Usunięto” bez linku | Covered | `change-history-models.ts:126-145`; testy `objectLink` dla 7 rodzajów |
| Katalog pól: etykiety i formaty, nieznane pole bez wyjątku | Covered | `audit-field-catalog.ts`; nazwy pól zgodne z enumami audytu w Orders/Expenses (camelCase), kody słowników zgodne z `OrderCodes`, `ExpenseCodes`, `OwnerWithdrawalCodes`; `dayOfMonth` z `int` jako tekst |
| Słowniki współdzielone w `shared/`, bez importów między obszarami | Covered | `shared/dictionaries/*`; etykiety identyczne z usuniętymi kopiami; dalsze eksporty pod starymi nazwami |
| Stany: ładowanie, pusto (dwa warianty), błąd sieci z „Spróbuj ponownie”, błąd walidacji bez zapytania | Partial | wszystkie są; przy pustej wymaganej dacie pokazuje się „Nie ma jeszcze żadnych zmian.” zamiast stanu po filtrach (Correctness Issues, 2) |
| `400` z API przy polu, nieznany klucz jako komunikat ogólny | Covered | `change-history.service.ts:108-131` (klucze znormalizowane `toLowerCase` jak w `fieldErrorOf`) |
| Brak akcji zmieniających dane w UI | Covered | test „is read only” |
| Dostępność: `aria-expanded`/`aria-controls`, `th scope`, `<time datetime>`, „Usunięto” tekstem, fokus po zwinięciu | Covered | uwaga o nazwach przycisków (Conventions, Non-Critical) |
| Filtry i strona zachowane po powrocie | Covered | serwis `providedIn: 'root'`; test „keeps the filters and page for the next injection” |

## Plan Deviations

- Format czasu: `formatDateTime` (`dd.MM.yyyy, HH:mm`) zamiast `dd.MM.yyyy HH:mm` ze spec — zgodne z
  planem P8 i odnotowane jako świadome odstępstwo (do wpisania w `06-implementation-done.md`).
- `app/src/app/layout/app-shell.spec.ts:61-77` — test nagłówka nawiguje przez `RouterTestingHarness`
  i dodatkowo tworzy drugi `AppShell` przez `TestBed.createComponent`, a potem czyści kolejne żądania
  listy. Plan przewidywał tylko „obsłużenie żądania listy”; wynik jest zgodny z celem, ale test
  renderuje powłokę dwa razy (zob. Code Smells).

Poza tym wszystkie pliki z sekcji „Files” planu są w zmianach, a pliki oznaczone „bez zmian”
(`EndpointSecurityTests`, `app.routes.ts`, `nav-items.ts`, testy architektury, `Reporting.Domain`)
nie zostały zmienione.

## Null Safety Issues

None. `ParentType`/`ParentId` mapowane tylko gdy oba mają wartość (`ChangeHistoryContracts.cs:49-51`),
`oldValue`/`newValue` `null` → „Brak danych”, `parent = null` przy wpłacie → brak linku,
`objectType` z pustymi wartościami pomijany (`ListChangeHistory.cs:65`).

## Correctness Issues

1. **Low** — `api/src/Modules/Reporting/StoneFlow.Reporting.Application/ChangeHistory/ChangeHistoryPeriod.cs:13`:
   `to.AddDays(1)` rzuca `ArgumentOutOfRangeException` dla `changedTo=9999-12-31`, a
   `ListChangeHistory.cs:125-139` (`OptionalDate`) przepuszcza tę datę jako poprawną. Wynik: `500`
   zamiast `400` przy polu `changedTo` (spec: „zła data … daje `400`”, błędy przypisane do pola).
   Skutek tylko dla ręcznie spreparowanego zapytania, ale łamie kontrakt błędów.
2. **Low** — `app/src/app/features/change-history/change-history.service.ts:175-181` z
   `:146-155`: gdy wymagana data okresu jest pusta (`changedFrom === ''` albo `changedTo === ''`),
   `load()` czyści listę bez zapytania, a chip „Zmiana” nie powstaje (wymaga obu dat). Tabela pokazuje
   wtedy „Nie ma jeszcze żadnych zmian.” — komunikat pustej ewidencji, który jest nieprawdziwy i stoi
   obok błędu „Podaj datę.” przy polu. Spec rozróżnia pustą ewidencję od pustego wyniku filtrów.
3. **Low** — `app/src/app/features/change-history/change-history.service.ts:70` i `:145`: domyślny
   okres jest ustalany raz przy utworzeniu serwisu root, a `activeFilters` porównuje go z
   `defaultChangePeriod()` liczonym przy przeliczeniu (bez sygnału czasu). Po północy w otwartej
   aplikacji lista nadal pokazuje okres do „wczoraj”, a po pierwszej zmianie dowolnego filtra pojawia
   się chip „Zmiana: …” dla okresu, którego użytkownik nie zmieniał. Drobne, ale warto odnotować.

Sprawdzone bez uwag: konwersja Warsaw → UTC (północ nigdy nie wypada w przerwie ani powtórzonej
godzinie), granice `>=`/`<`, porównania `DateOnly?` przy jednej granicy, `Contains` na liście enumów
z konwersją na tekst i skorelowane `Any` w projekcji (tłumaczone na SQL; potwierdzą testy
integracyjne), `SpecifyKind(Utc)` po materializacji, format „O” z „Z” (7 cyfr ułamka parsuje
`parseTimestamp` tak jak dotychczasowe znaczniki `DateTimeOffset`), sekwencja żądań w serwisie
(`requestSeq`), domyślne `[class]` w `data-table` przy `mobileCards = false` (statyczne klasy
zachowane, test sprawdza `className`).

## Test Gaps

- Brak testu dla skrajnej daty okresu (`9999-12-31`) — zob. Correctness Issues, 1.
- Brak testu stanu pustej listy przy brakującej wymaganej dacie (Correctness Issues, 2).
- Testy integracyjne `ChangeHistoryApiTests` (I-H-1 … I-H-10) są obecne i pokrywają plan, ale nie
  zostały uruchomione w tym przeglądzie; ich wynik (w tym tłumaczenie `Contains`/`EXISTS` na SQL) trzeba
  potwierdzić w `06-implementation-done.md` lub w CI.

## Code Smells

- `app/src/app/layout/app-shell.spec.ts:61-77` — test renderuje powłokę dwa razy (harness z trasą i
  osobny `TestBed.createComponent(AppShell)`) i dopasowuje kolejne żądania listy pętlą `match`.
  Działa, ale jest mniej czytelny niż wzór z `page-title-strategy.spec.ts` (jedna nawigacja, jedno
  `nextRequest`).
- `app/src/app/features/change-history/change-history-page.ts:28-34` — kolumna „Czas” ma zarówno
  `value`, jak i szablon `appTableCell="occurredAt"`; `value` nie jest używane do wyświetlania. Drobna
  redundancja.

## Recommendation

- Merge as-is (po przeglądzie człowieka wymaganym dla ryzyka `high`); uwagi Low warto poprawić
  przy okazji, najlepiej Correctness Issues 1 i 2 przed merge.
