# Spec review — SF-22

## Summary

- Overall assessment: PASS WITH ISSUES
- Justification: Specyfikacja pokrywa wszystkie kryteria i punkty zakresu zgłoszenia oraz PRD §5 i DANE-01. Jest zgodna z faktycznym kodem. Sprawdzono m.in. `Order`, `Customer`, `OperationalData`, `Refund`, `SalesCorrection`, `Payment`, `OrderAudit`, `PaymentAudit`, `OrderCodes`, `OrderLock`, `OrderStore`, `ShopOrderSync`, `OrderOutcome`, `RetryOrderError`, `WooCommerceOrderPayload`, `IntegrationsDbContext`, `AuditRecord`, `AuditLog`, `AuditRecordConfiguration`, `ChangeHistoryReader`, `ChangeHistoryCodes`, `OrderSalesComponentSource`, `OrderCashOperationSource` i `UnitOfWork`. Lista pól osobowych odpowiada wszystkim miejscom w kodzie, w których są zapisane dane klienta zamówienia. Funkcja jest fail-closed w API i UI, a w żadnym pliku konfiguracji nie ma dziś domyślnego okresu. Sprzeczność z regułą o D-09 rozstrzygnęła wyraźna decyzja właściciela z 13.11.2026. Uwagi dotyczą głównie procedury włączenia, bo okres ustawia konfiguracja, a regułę początku retencji kod, oraz kilku doprecyzowań. Żadna z nich nie blokuje planowania.

## Findings

### Critical Issues

None.

### Non-Critical Issues

1. **Behavior pkt 5 / Assumptions pkt 1–2 — włączenie ustawia tylko długość okresu, a nie regułę jego początku.** Konfiguracja przyjmuje liczbę miesięcy. Początek okresu (najpóźniejsza z dat zamówienia, wpisu i ostatniej zmiany) to decyzja wykonawcy zapisana w kodzie. Jeżeli D-09 ustali inny początek, np. koniec roku obrotowego, właściciel ustawi wartość według procedury z pkt 5 i akcja otworzy się nawet o kilka do kilkunastu miesięcy za wcześnie, a operacji nie da się cofnąć. Procedura w `docs/operations/anonimizacja.md` i pkt 5 muszą przed ustawieniem wartości wymagać potwierdzenia, że reguła z pkt 6 zgadza się z rozstrzygnięciem D-09. Gdy się nie zgadza, najpierw trzeba zmienić `CustomerAnonymization.RetentionStart` w osobnym zadaniu.
2. **Behavior pkt 2–3 — pusta wartość i pliki konfiguracji poza `appsettings*.json`.**
   - Nie określono, co robi pusta wartość (`StoneFlow__Anonymization__RetentionMonths=`). Wiązanie `int?` z pustego tekstu daje zwykle `null`, czyli funkcję wyłączoną, a pkt 2 mówi tylko o wartości „nieliczbowej”, która ma zatrzymać start. Trzeba to ustalić wprost i pokryć testem `AnonymizationStartupTests`.
   - Wymóg „bez domyślnej długości” wymienia tylko `appsettings.json` i `appsettings.Development.json`. Powinien objąć też `Properties/launchSettings.json`, `deploy/api.env.example` (wpis zakomentowany albo z placeholderem `<…>`, nigdy liczba), konfigurację serwera E2E (`app/playwright.config.ts`) i workflowy CI. Dziś żaden z tych plików nie zawiera klucza.
   - Trzeba też ustalić, czy wspólna fabryka testów integracyjnych ustawia wartość dla wszystkich klas. Jeżeli tak, testy stanu „wyłączona” muszą umieć ją usunąć, a to łączy się z pustą wartością.
3. **Behavior pkt 6 — usunięcie wpisu nie przesuwa początku okresu.** Usunięcie wpłaty, zwrotu albo korekty nie zmienia `Order.UpdatedAtUtc`, a wiersza już nie ma. Ostatnia zmiana finansowa zamówienia może więc nie wejść do wyliczenia. Kłóci się to z uzasadnieniem „zachowanie ostrożne” z Assumptions pkt 2. Właściciel powinien potwierdzić to wyłączenie razem z regułą początku okresu (pkt 1). Usunięcie zostawia ślad w audycie, ale Orders go nie czyta.
4. **Behavior pkt 12 i 14 — zmiana powodu zwrotu i korekty bez wpisu na obiekcie wpisu.** Zwroty i korekty dostają nowy `Reason` i `UpdatedAtUtc`, ale wpis audytu `Anonymized` powstaje tylko dla zamówienia, z samą liczbą zmienionych powodów. W „Historii zmian” zawężonej do rodzaju „Zwrot” albo „Korekta sprzedaży” zmiana jest niewidoczna. Przy niezmienniku 5 („extent of change”) jest to do przyjęcia, bo wpis zamówienia jest obiektem nadrzędnym. Warto jednak zapisać to jako założenie albo dodać do wpisu `Anonymized` identyfikatory zmienionych wpisów, które nie są danymi osobowymi.
5. **Out of scope / Assumptions pkt 9 — dane w sklepie WooCommerce.** Sklep dalej przechowuje dane klienta. Uzupełnianie i „Ponów” pobierają je ponownie (tylko w pamięci, bez zapisu) i domena je pomija. Specyfikacja nie mówi jednak, że żądanie usunięcia danych trzeba obsłużyć osobno w sklepie. Brakuje tego punktu w „Out of scope” i w `docs/operations/anonimizacja.md`.
6. **Context, „Kontrakt współdzielony” — dokumentacja `IAuditLog`.** Specyfikacja doprecyzowuje komentarz w `AuditRecord` i `conventions.md`, ale komentarz XML `IAuditLog` w `Shared.Application/Audit/IAuditLog.cs` też mówi „Wpisy są tylko dodawane”. Trzeba go zaktualizować razem z nimi i wskazać `IAuditRedaction` jako jedyny wyjątek.
7. **Behavior pkt 11 — kolejność `disabled` przed `404`.** Przy wyłączonej funkcji `POST` dla nieistniejącego zamówienia zwraca `422 anonymization_disabled`, a nie `404`. Jest to spójne z „brak odczytu bazy”, ale tabela endpointu i testy powinny podawać to wprost, żeby test `404` uruchamiał się z ustawionym okresem.

### Unclear or Ambiguous Sections

- **Behavior → „Po anonimizacji” pkt 19:** zmiana samego rodzaju klienta (osoba / firma) też jest „Customer różny od zapisanego” i daje `422`, bo rekord porównuje wszystkie pola. Warto to zapisać, a w UI pkt 5 wskazać, że pole rodzaju też jest tylko do odczytu.
- **UI pkt 2, stan `available`:** „Okres retencji upłynął {dd.MM.yyyy}” nie mówi, która data. Prawdopodobnie `availableFrom`.
- **Edge Cases, „Równoczesna dostawa ze sklepu”:** synchronizacja blokuje `sp_getapplock` po ID zewnętrznym, a anonimizacja `IOrderLock` (`UPDLOCK` na wierszu). Poprawność wynika wyłącznie z `rowversion` zamówienia: przestarzały zapis synchronizacji dostaje konflikt, potem `RetryRequired`, wycofanie i ponowienie. Kod to potwierdza, ale specyfikacja powinna wprost stwierdzić, że synchronizacja celowo nie bierze `IOrderLock`.
- **Behavior pkt 15:** nie powiedziano, czy wartość równa już znacznikowi (np. powód wpisany jako `[zanonimizowano]` przed operacją) liczy się do `anonymizedReasons`. Przypadek marginalny, ale pkt 14 („gdy coś się zmieniło”) i pkt 16 („liczba zwrotów i korekt z powodem zmienionym”) trzeba czytać łącznie.

### Invented or Unsupported Requirements

None. Zakres 1–1200 miesięcy, zerowanie skrótów idempotencji i operacja `Anonymized` to doprecyzowania wynikające z zadania (fail-closed, „bez danych osobowych”, „ślad operacji”), opisane jako decyzje wykonawcy.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Assumptions pkt 1, Dependencies (D-09) | `rules.md` „Do not implement an area blocked by an unapproved PRD decision (open: D-08, D-09)”; `role.md` „Open decisions … must be approved before implementing” | **Rozstrzygnięte decyzją właściciela.** 13.11.2026 właściciel, wyraźnie poinformowany, że SF-22 blokuje D-09, polecił wykonać zadanie. Specyfikacja nie rozstrzyga długości okresu i udostępnia funkcję dopiero po ustawieniu konfiguracji. Sprawdzono: brak klucza w `appsettings*.json` i `deploy/api.env.example`; `GET` → `disabled`, `POST` → `422` przed dostępem do bazy; UI z wyłączonym przyciskiem. Zostaje uwaga 1, bo reguła początku okresu jest w kodzie. |
| Behavior pkt 6, Assumptions pkt 2 | PRD §12 D-09 („okres retencji danych zamówień wynika z przepisów”) | Reguła początku okresu może być częścią rozstrzygnięcia D-09. Nie jest to konflikt, bo zadanie oddaje „moment, od którego liczy się retencja” specyfikacji, ale zależy od niego procedura włączenia (uwaga 1). |
| Context (Reporting), Behavior pkt 15 | `AuditRecord` („Wpis jest tylko dodawany”), `IAuditLog` („Wpisy są tylko dodawane”), `conventions.md` | Zamierzony, wymagany przez PRD §5 („usuwa dane klienta … z wpisów audytu”). Specyfikacja aktualizuje `AuditRecord` i `conventions.md`, ale pomija `IAuditLog` (uwaga 6). |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | OK | Anonimizacja nie zmienia wartości, dat, statusów ani wpisów pieniężnych. `OrderSalesComponentSource` i `OrderCashOperationSource` czytają tylko numer, daty, kwoty, kanał, status i metodę, a nie klienta, uwag ani powodów. Test porównuje cztery odczyty raportów. |
| 2. Backend decimal money | OK | Bez obliczeń pieniężnych; retencja liczona na `DateOnly` w backendzie. |
| 3. API-enforced access | OK | Polityka domyślna i CSRF, bez `.AllowAnonymous()`. Konfigurację, retencję i stan sprawdza handler w transakcji po blokadzie, niezależnie od UI. Testy `401`, `400` i `422`. |
| 4. Retries never duplicate | OK | Drugi `POST` → `409` bez drugiego wpisu. Dostawa ze sklepu nie nadpisuje klienta (domena). Unikalność `(Source, ExternalId)` bez zmian. Wszystkie ścieżki synchronizacji (webhook, uzupełnianie, „Synchronizuj teraz”, „Ponów”) idą przez jedyną implementację `IShopOrderSync`. |
| 5. Financial audit trail | OK | Własny wpis `Anonymized` z autorem, czasem, obiektem i zakresem. Wpisy zostają z autorem, czasem, operacją i nazwami pól; zmieniają się tylko wartości wskazanych pól, zgodnie z PRD §5. Uwaga 4 dotyczy widoczności na obiekcie wpisu. |
| 6. Secrets stay server-side | OK | Konfiguracja nie jest sekretem. Komunikaty walidacji podają tylko klucz i zakres. Brak nowych logów z danymi. |
| 7. Module and layer boundaries | OK | Orders zmienia `reporting.AuditEntries` tylko przez port `IAuditRedaction` w `Shared.Application`, wzorem `IAuditLog`. Implementacja w `Reporting.Infrastructure` na wspólnym `ModuleConnection` i transakcji `IUnitOfWork`. Reguły w domenach obu modułów. |
| 8. No identifying data in docs | OK | Przykłady fikcyjne; znacznik nie identyfikuje osoby. |

## Risk

- Recorded: high
- Assessed: high. Operacja na danych osobowych, nieodwracalna, i jedyny wyjątek od niezmienności audytu. Zapisy `orders.*` i `reporting.AuditEntries` idą w jednej transakcji. Ryzyko jest zgodne z zapisem; przed merge wymagany przegląd przez człowieka.

## Assumptions Detected

| Założenie | Podane w specyfikacji |
|---|---|
| Owner decision 13.11.2026: mechanizm wyłączony do konfiguracji po D-09 | Tak (Assumptions pkt 1); przegląd uznaje to za rozstrzygnięcie sprzeczności z `rules.md` |
| Reguła początku okresu (najpóźniejsza z dat zamówienia, wpisu i ostatniej zmiany) | Tak (pkt 2), do potwierdzenia przez właściciela; brak powiązania z procedurą włączenia (uwaga 1) |
| Wpisy usunięte nie wpływają na początek okresu | Częściowo (Behavior pkt 6, bez uzasadnienia względem „ostrożności”) — uwaga 3 |
| Zakres pól: klient bez rodzaju, uwagi, komentarz, powody zwrotów i korekt; zostają dostawcy, ZO, faktura | Tak (pkt 3); zgodne z polami `IsPersonalData` w `OrderAudit` oraz z polami tekstowymi w kodzie |
| „Przyjął” przy wpłacie poza zakresem | Tak (pkt 5) |
| Zerowanie skrótów idempotencji zamówienia, zwrotu i korekty; skrót wpłaty zostaje | Tak (pkt 4); zgodne z `CreateOrder` i `OrderEntryWriter` (porównanie skrótu → `KeyReused`) |
| Historia błędów synchronizacji i przebiegi nie zawierają danych klienta; treści dostaw nie są zapisywane | Tak (pkt 8); potwierdzone w `IntegrationsDbContext`, `OrderOutcome`, `WooCommerceOrderPayload` (komunikaty tylko z ID) i `RetryOrderError` (treść z REST, tylko w pamięci) |
| Kopie i logi poza operacją; rejestr żądań po odtworzeniu prowadzi właściciel | Tak (pkt 9) |
| Sklep WooCommerce zachowuje dane klienta | Nie — uwaga 5 |
| Synchronizacja i anonimizacja używają różnych blokad, a spójność zapewnia `rowversion` | Częściowo (Edge Cases) — patrz „Unclear” |
| Semantyka pustej wartości konfiguracji | Nie — uwaga 2 |
| `[zanonimizowano]` przechodzi normalizację i walidację `OrderInput` przy edycji | Domyślnie; z kodu nic temu nie przeczy (nazwa tylko przycinana) |
| Powrót do wersji sprzed SF-22 bezpieczny tylko do pierwszego użycia | Tak (pkt 14) |
| Migracja expand: dwie kolumny `NULL` i ograniczenie spełnione przez stare wiersze i zapisy starego API | Tak (Storage); poprawne, bo stary model nie mapuje kolumn, a wstawia `NULL` |

## Recommendation

- Proceed as-is. Plan (`sdd-plan SF-22`) powinien uwzględnić uwagi 1–7. Uwaga 1 (procedura włączenia ze sprawdzeniem reguły początku okresu względem D-09) i uwaga 2 (pusta wartość i wszystkie pliki konfiguracji) są najważniejsze, bo dotyczą fail-closed i nieodwracalności.
