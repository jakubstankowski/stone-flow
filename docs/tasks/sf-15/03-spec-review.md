# Spec review — SF-15

## Summary

- Overall assessment: PASS WITH ISSUES
- Justification: Specyfikacja pokrywa wszystkie wymagania zgłoszenia, WYD-01/02 i PLAT-02 oraz
  rozstrzyga punkty „do rozstrzygnięcia” zgodnie z odpowiedziami właściciela (Assumptions 1–4).
  Twierdzenia o kodzie sprawdziłem w repozytorium i są prawdziwe: szkielet Expenses,
  `AuditRecord` z dopuszczonym rodzicem `RecurringExpenseDefinition`, wzorzec `CreateOrderHandler`,
  `DELETE` z `{ version }` w ciele, `todayInWarsaw` w `order-form-model.ts`, `parsePlnAmount`
  odrzucający minus, `StatusTone` z `neutral`/`success`/`warning`, slot `tableFilters`, wpis
  w `contexts.sh`. Żaden inwariant nie jest naruszony. Usunięcie fizyczne z pełnym śladem
  `Deleted` jest zgodne z kontraktem `AuditEntry` i z przyjętą praktyką SF-9 i SF-10. Uwagi
  dotyczą jednej sprzeczności w przypadkach brzegowych, niejasnego kontraktu dla SF-16, podwójnej
  implementacji reguły stanu i reguł netto ostrzejszych niż PRD.

## Findings

### Critical Issues

None.

### Non-Critical Issues

1. **Edge Cases a Behavior → „Oznacz jako opłacony” / „Cofnij oznaczenie zapłaty”: sprzeczna
   kolejność.** Behavior ustala kolejność: wersja (`409 expense_conflict`), potem reguła domeny
   (`409 expense_already_paid`). Edge Cases mówi jednak: „Oznacz jako opłacony” na opłaconym
   wydatku (np. druga karta) daje `409 expense_already_paid`. Druga karta ma nieaktualną wersję,
   bo pierwsze oznaczenie zmieniło `rowversion`, więc dostanie `expense_conflict`. To samo dotyczy
   cofnięcia na planowanym wydatku (`expense_not_paid`). `expense_already_paid` i
   `expense_not_paid` wystąpią tylko przy aktualnej wersji. Przykład trzeba poprawić, bo inaczej
   test integracyjny i UI (który komunikat pokazać) dostaną sprzeczne oczekiwania.
2. **Data / API → Storage → „Kontrakt dla SF-16”: nie wiadomo, kiedy powstaje ślad pary
   definicja–okres.** Spec mówi, że SF-16 doda tabelę śladu blokującą ponowne utworzenie po
   usunięciu i że „SF-16 nie zmienia kontraktu” usunięcia. Jest to prawdą tylko wtedy, gdy ślad
   zapisuje się przy **tworzeniu** wystąpienia (rejestr przetworzonych par z unikalnością
   definicja+okres, inwariant 4). Jeśli ślad ma powstawać przy usunięciu, `DeleteExpense` z SF-15
   musi go zapisać w tej samej jednostce pracy. Bez tego usunięte wystąpienie wróci (WYD-03–06,
   „usunięte wystąpienie nie wraca”). Kontrakt powinien wskazać pierwszy wariant albo jawnie
   przewidzieć rozszerzenie handlera usunięcia w SF-16. Rozszerzenia wymaga też `ExpenseAudit`,
   który dla wystąpienia musi podać rodzica przy edycji, akcjach zapłaty i usunięciu.
3. **Behavior → „Stan wydatku (reguła domeny)”, pkt 4: dwie implementacje tej samej reguły.**
   Lista liczy `paymentStatus` i `overdue` w SQL, a szczegóły w domenie. Według conventions (DRY
   wiedzy biznesowej) i precedensu Orders (`OrderListItemDto.Payment` liczy `PaymentSummary.Compute`
   z surowych wartości z bazy, „ta sama reguła co w szczegółach (N5)”) pozycje listy powinny
   brać stan z funkcji domeny, a SQL powinien zostać tylko w predykatach filtra `state`. Test
   zgodności łagodzi ryzyko, ale go nie usuwa.
4. **Data / API → Walidacja `netAmount` i `CK_Expenses_NetAmount`: reguły ostrzejsze niż PRD
   (Assumptions 5).** PRD wymaga tylko tego samego znaku co brutto. Reguła „netto ≠ 0” stoi
   w napięciu z PRD §8 pkt 5 („zero i brak danych to różne rzeczy”). Faktura korygująca sam VAT
   ma netto 0,00 zł i brutto różne od zera, a spec zmusza wtedy do wpisania „brak netto”. Reguła
   `|netto| ≤ |brutto|` jest rozsądna, ale nie pochodzi z PRD i trafia do `CHECK` w bazie. Obie
   reguły warto potwierdzić u właściciela. Netto nie wpływa na obliczenia, więc nie blokuje to
   planowania.
5. **Context / Assumptions 16: uzasadnienie lokalnego `WarsawTime` jest nieścisłe.**
   `StoneFlow.Shared.Application.Scheduling.WarsawTimeZone.Zone` już istnieje, a Expenses.Application
   referencjonuje Shared.Application. Lokalny pomocnik powinien opierać się na tej strefie
   (z rezerwowym identyfikatorem Windows) zamiast kopiować `Find()` z Orders. „Duplikacja, bo moduł
   nie może referencjonować Orders” dotyczy tylko `FieldErrors` i parsowania kwot.
6. **Context → Frontend: przeniesienie `todayInWarsaw` dotyka Orders.** Funkcję importują
   `entry-form-model.ts`, `payment-form.ts`, `refund-form.ts` i `order-form-model.ts`. Spec
   powinna wymienić aktualizację tych importów (albo reeksport), bo to zmiana poza modułem
   wydatków.
7. **Verification: brak testu E2E.** rules.md (priorytet 5: „save an expense, … end to end”)
   i PRD §11 wymagają E2E kluczowych procesów. W repo nie ma infrastruktury E2E, a poprzednie
   zadania też jej nie miały. Spec powinna to zapisać jako jawne odroczenie (np. do SF-21), a nie
   pomijać po cichu.
8. **UI → Ujemne kwoty / „Oznacz jako opłacony”.** Opis „Wydatek trafi do wypływów w okresie daty
   zapłaty” jest mylący przy kwocie ujemnej (zwrot od dostawcy obniża wypływy, PRD §8). Przy
   ujemnym brutto formularz zapłaty powinien pokazać wariant opisu zgodny z pkt 3 „Dodanie
   wydatku”.
9. **Data / API → Audyt: dane osobowe.** `payer` z `IsPersonalData` jest nieszkodliwe, ale nie ma
   konsumenta: DANE-01 anonimizuje dane klienta zamówienia. `counterparty` (np. osoba fizyczna
   prowadząca działalność), `description` i `comment` nie są oznaczane. Decyzja jest spójna
   z Orders (dostawcy, komentarz) i zapisana (Assumptions 8). Ta sama otwarta kwestia co w przeglądzie
   SF-9 („Przyjął”) do rozstrzygnięcia w SF-22.
10. **UI / Lista: drobne luki względem design guide.**
    - §3 i §9: na telefonie filtry przechodzą w rozwijany panel; spec opisuje tylko formularz
      i tabelę.
    - §8: przykładowy tekst „Brak wydatków w wybranym okresie” różni się od „Brak wydatków dla
      wybranych filtrów”. To dopuszczalne, ale warto świadomie wybrać jeden tekst.

### Unclear or Ambiguous Sections

- **UI → Formularz** a **Behavior → Szczegóły i poprawa / Lista:** etykieta terminu to raz
  „Termin płatności” (formularz), a raz „Termin” (lista, szczegóły). Trzeba ujednolicić.
- **Data / API → Filtry listy:** otwarty zakres opisano tylko dla `paidOnFrom`. Dla
  `costDateFrom`/`costDateTo` zachowanie przy jednym końcu wynika z analogii, ale nie jest
  zapisane.
- **Behavior → Usunięcie, treść dialogu:** „zniknie z kosztów” jest nieścisłe dla wydatku z datą
  kosztu sprzed uruchomienia, bo nie było go w raportach. To drobna nieścisłość tekstu.
- **Behavior → Szczegóły i poprawa, pkt 4:** potwierdzenie wyczyszczenia daty zapłaty istnieje
  tylko w UI. API przyjmuje `PUT` z `paidOn = null` bez dodatkowej flagi. To poprawne, ale warto
  to zapisać, żeby plan nie dodawał flagi w API.

### Invented or Unsupported Requirements

- `|netto| ≤ |brutto|` i `netto ≠ 0` (Assumptions 5): nie wynikają z PRD i są egzekwowane także
  w bazie. Są jawnie uzasadnione, ale wymagają potwierdzenia (Non-Critical 4).
- Ustawianie metody płatności w „Oznacz jako opłacony” (Assumptions 13): PRD wymienia tylko datę
  zapłaty. To niewielkie, jawnie opisane rozszerzenie, które nie zmienia obliczeń.

Pozostałe elementy, czyli akcja „Cofnij oznaczenie zapłaty”, „Po terminie”, filtry znaku i tekstu
oraz sortowanie, wynikają z odpowiedzi właściciela (Assumptions 2–3), ze zgłoszenia („Lista
z filtrowaniem”) albo z PRD §10 („Listy”).

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Data / API → Walidacja `netAmount` (netto ≠ 0) | PRD §8, „Zabezpieczenia…” pkt 5 | Rzeczywiste netto 0,00 zł można zapisać tylko jako brak danych. To napięcie, nie sprzeczność z decyzją, bo PRD wymaga „tego samego znaku”, a zero znaku nie ma |
| Context / Assumptions 16 (`WarsawTime`) | Kod: `Shared.Application/Scheduling/WarsawTimeZone.cs` | Spec twierdzi, że potrzebna jest lokalna kopia. Strefa jest już współdzielona |
| Edge Cases (druga karta → `expense_already_paid`) | Behavior → „Oznacz jako opłacony”, kolejność kroków | Wewnętrzna sprzeczność kodów błędów |

Brak konfliktów z zapisanymi decyzjami (ADR, D-01–D-07). Usunięcie fizyczne ze śladem `Deleted`
jest zgodne z kontraktem `AuditEntry` („przy usunięciu ostatnie wartości obiektu (ślad
usunięcia)”), z AUD-01 („usunięty obiekt zostawia widoczny ślad”) i z praktyką SF-9 i SF-10.

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sprzedaż oddzielona od pieniędzy (plan nie jest wypływem) | OK | Stan wynika wyłącznie z `PaidOn`. Przyszła data zapłaty jest odrzucana w API z „dzisiaj” w Europe/Warsaw (`TimeProvider`, test granicy 23:30 UTC), a plan wpisuje się jako `dueDate`. Metoda „Pobranie” nie oznacza zapłaty. SF-15 nie tworzy wypływów |
| 2. Pieniądze jako decimal w backendzie | OK | `decimal(18,2)`, parsowanie tekstowe bez zaokrągleń i bez `float`. Frontend tylko formatuje i sprawdza tekstowo znak i zero, a porównanie `\|netto\| ≤ \|brutto\|` robi wyłącznie API |
| 3. Dostęp egzekwowany w API | OK | Polityka domyślna, XSRF przy zapisie, brak wpisów w `AllowedAnonymous`, testy `401` dla każdej trasy |
| 4. Ponowienia nie duplikują | OK | `Idempotency-Key` z filtrowanym indeksem unikalnym i testem wyścigu. Unikalność definicja+okres należy do SF-16, ale kontrakt wymaga doprecyzowania (Non-Critical 2) |
| 5. Audyt finansowy | OK | Utworzenie, zmiana (także akcje zapłaty) i usunięcie w tej samej `IUnitOfWork`, autor `AuditAuthor.Account`. `Deleted` ze wszystkimi niepustymi polami, etykieta bez wolnego tekstu i test wycofania. Fizyczne usunięcie zostawia ślad zgodny z AUD-01 |
| 6. Sekrety po stronie serwera | Not applicable | Brak sekretów i integracji. Problem Details i logi bez danych wydatku |
| 7. Granice modułów i warstw | OK | Brak referencji do Orders i Reporting, wyłącznie `StoneFlow.Shared.*`, testy architektury. Uwaga o `WarsawTimeZone` w Non-Critical 5 |
| 8. Brak danych identyfikujących w dokumentach | OK | Przykłady fikcyjne („Dostawca A”), bez nazwy i domeny sklepu |

## Risk

- Recorded: high
- Assessed: high. Zadanie dotyczy pieniędzy (koszty i wypływy w obu widokach), fizycznego
  usunięcia ze śladem tylko w audycie oraz modelu, na którym opierają się SF-16, SF-17, SF-19
  i SF-20. Przed merge wymagany jest przegląd przez człowieka.

## Assumptions Detected

| Założenie | Zapisane w spec? |
|---|---|
| Przyszła data zapłaty odrzucana, plan jako termin | Tak (1, odpowiedź właściciela) |
| Usunięcie daty zapłaty z potwierdzeniem i audytem | Tak (2, odpowiedź właściciela) |
| Etykieta „Planowany” + „Po terminie” | Tak (3, odpowiedź właściciela) |
| Płatnik jako wolny tekst | Tak (4, odpowiedź właściciela) |
| `\|netto\| ≤ \|brutto\|`, netto ≠ 0 | Tak (5); wymaga potwierdzenia |
| Usunięcie fizyczne z pełnym `Deleted` | Tak (6) |
| Brak numeru wydatku, etykieta z kategorii | Tak (7) |
| `payer` jako dane osobowe, pozostałe pola nie | Tak (8) |
| Siedem kategorii (VAT i PPE osobno, „Marketing” jedna) | Tak (9) |
| Cztery metody, metoda opcjonalna | Tak (10) |
| Data kosztu bez ograniczeń, zapłata może poprzedzać koszt | Tak (11) |
| Osobne kolumny dat | Tak (12) |
| Akcja zapłaty ustawia metodę, akcje tylko w szczegółach | Tak (13) |
| „Po terminie” jako podzbiór planowanych, „dzisiaj” z handlera | Tak (14) |
| Suma listy poza SF-15 | Tak (15) |
| Lokalne kopie pomocników | Tak (16); uzasadnienie częściowo nieścisłe |
| Stan pozycji listy liczony w SQL | Tak (Behavior, pkt 4); lepiej użyć funkcji domeny |
| Ślad pary definicja–okres w SF-16 powstaje przy tworzeniu, więc `DeleteExpense` się nie zmienia | Nie, tylko domniemane |
| Potwierdzenie wyczyszczenia daty zapłaty istnieje tylko w UI | Nie, tylko domniemane |
| Zakres jednostronny dla daty kosztu | Nie, tylko analogia do `paidOnFrom` |
| Znaczenie „Opłacony” dla ujemnego wydatku (zwrot otrzymany) | Nie |
| Brak testów E2E (odroczenie) | Nie |
| Ten sam klucz po usunięciu tworzy nowy wydatek | Tak (Edge Cases) |

## Recommendation

- Proceed as-is. Planowanie może ruszyć. Przed planem albo w planie warto poprawić przykład
  z Edge Cases (Non-Critical 1) i doprecyzować kontrakt dla SF-16 (Non-Critical 2). Reguły netto
  (Non-Critical 4) warto potwierdzić u właściciela.
