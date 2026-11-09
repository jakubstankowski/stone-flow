# Spec review — SF-16

## Summary

- Overall assessment: FAIL
- Justification: Specyfikacja jest dojrzała. Rejestr z kluczem `(DefinitionId, Period)`, blokada wiersza definicji, jednostka pracy na definicję i audyt z rodzicem spełniają niezmienniki 1–8, a większość reguł PRD §5 i WYD-03–06 ma pokrycie w regułach, przypadkach brzegowych i testach. Są jednak dwa problemy krytyczne. Reguła zmiany dnia miesiąca (reguła 7, Assumptions 11) trwale odcina jeszcze nieutworzone zaległe okresy sprzed bieżącego okresu. To jest sprzeczne z PRD §5 („Po przerwie w działaniu system tworzy wszystkie zaległe wystąpienia”, zmiana „dotyczy … w tym zaległych”) i z odpowiedzią właściciela 2, gdy w jednym zapisie użytkownik przedłuża koniec i zmienia dzień. Ponadto spec bez słowa wyjaśnienia wyłącza ostrzeżenie w UI o niepowodzeniu zadania, które SF-11 przypisało SF-16.

## Findings

### Critical Issues

1. **Zmiana dnia miesiąca gubi zaległe okresy (Behavior → „Pojęcia i reguły domeny”, reguła 7; Assumptions 11; Edge Cases „Zmiana dnia”).**
   Reguła `OccurrencesFrom = max(OccurrencesFrom, pierwszy dzień bieżącego okresu)` sprawia, że każdy okres sprzed bieżącego, który jeszcze nie powstał, przepada na stałe. Dzieje się tak w dwóch sytuacjach:
   - **Przedłużenie i zmiana dnia w jednym zapisie.** Przykład: koniec 30.06, dzisiaj 20.11, nowy koniec „bez końca” i nowy dzień. Granica przesuwa się na 01.11, więc okresy lipiec–październik nie powstają. Odpowiedź właściciela 2 mówi natomiast: „przedłużenie tworzy zaległe wystąpienia za okresy między dawnym końcem a dzisiaj”. Wynik zależy przy tym od kolejności działań. Dwa osobne zapisy (najpierw przedłużenie, potem zmiana dnia) tworzą lipiec–październik, a jeden łączny zapis ich nie tworzy. To sprzeczność z zarejestrowaną decyzją właściciela.
   - **Przerwa w działaniu albo kolejne nieudane przebiegi zadania.** Zmiana dnia odcina okresy z przerwy. PRD §5 wymaga, by „po przerwie w działaniu system tworzy[ł] wszystkie zaległe wystąpienia”, a zmiana definicji ma dotyczyć wystąpień „jeszcze nieutworzonych, w tym zaległych”. Argument z Assumptions 11, że taki okres istnieje „tylko wtedy, gdy zadanie od kilku przebiegów kończy się błędem”, jest niepełny. Okresy zaległe istnieją też chwilę po wybudzeniu procesu, przed pierwszym taktem harmonogramu (5 min). Bez ostrzeżenia o niepowodzeniu zadania (problem 2) użytkownik nie wie, że zaległe okresy istnieją.
   - Podgląd pokazuje tylko liczbę wystąpień, które **powstaną**. Nie pokazuje tych, które **przepadną**, więc zdanie „Podgląd pokazuje skutek przed zapisem” nie zabezpiecza przed utratą kosztów.
   Odpowiedź 1 dotyczy wyłącznie okresu bieżącego (jest wystąpienie / nie ma go, a nowy termin minął). Nie rozstrzyga losu nieutworzonych okresów wcześniejszych. Assumptions 11 to decyzja autora sprzeczna z PRD i z odpowiedzią 2, a nie wynik odpowiedzi 1. Potrzebna jest poprawka modelu albo wyraźna decyzja właściciela. Przykładowe kierunki dla autora: przed zmianą dnia odciąć tylko okresy, które przy starym dniu nie byłyby należne, albo przed zastosowaniem zmiany dnia utworzyć zaległe okresy sprzed bieżącego według dotychczasowego harmonogramu. Spec musi też podać, czy powstają one z nowymi wartościami treści.

2. **Brak ostrzeżenia o niepowodzeniu zadania `recurring-expenses` (Scope → Out of scope: „widok stanu zadania poza istniejącym `GET /api/jobs` (SF-11)”).**
   Breakdown zadania 11 zostawił do rozstrzygnięcia: „jak widać niepowodzenie zadania (… brak wystąpień zaniża koszty bez ostrzeżenia)”. SF-11 rozstrzygnęło to w `docs/tasks/sf-11/02-spec.md`, w kilku miejscach: Assumptions („**Odpowiedź:** … UI dodadzą SF-14, SF-16, SF-19”), Out of scope („Ostrzeżenia w UI — dodadzą je SF-14, SF-16 i SF-19”), UI i „Stan zadań” pkt 3. SF-16 wyłącza to bez uzasadnienia i bez przekazania innemu zadaniu. Zakres SF-19 w breakdownie obejmuje tylko „komunikat o niekompletnej integracji”. Ostrzeżenie nie trafiło więc do żadnego zadania. Trwały błąd jednej definicji kończy każdy przebieg statusem `failed` (Behavior → „Zadanie harmonogramu”, pkt 2.4), a koszty są zaniżone w raportach bez widocznego śladu w UI. Spec musi dodać ostrzeżenie (np. na liście wydatków cyklicznych i w szczegółach definicji, na podstawie `GetJobStatusesQuery`: ostatni przebieg `failed` albo brak sukcesu od ponad doby) albo przytoczyć decyzję właściciela, która przenosi je do innego zadania.

### Non-Critical Issues

1. **Warstwa tworząca zakresy DI (Context → Application `Recurring/RecurringOccurrencesRun`; Behavior → „Zadanie harmonogramu” pkt 2.2).** `RecurringOccurrencesRun` leży w Application, ale ma otwierać „nowy zakres DI i `IUnitOfWork` na każdą” definicję. Application nie powinno używać `IServiceScopeFactory` (konwencje: brak service locatora poza korzeniem kompozycji). Testy wymagają z kolei „atrap portów”. Trzeba wskazać mechanizm: port fabryki jednostki pracy na definicję albo pętlę zakresów w `RecurringExpensesJob` (Infrastructure), która dla każdej definicji woła operację Application.
2. **Wynik „okres już utworzony” w handlerach użytkownika jest nieokreślony (Behavior → „Tworzenie wystąpień”, pkt 6).** Zdanie „wywołujący … postępuje jak w opisie danego przypadku” nie ma pokrycia w opisach dodania, edycji, wstrzymania i wznowienia. Spec nie mówi, jaki status HTTP i jaki kod dostaje użytkownik (np. `409 recurring_expense_conflict` z „Wczytaj ponownie” albo `503`). Bez translacji wyjątek trafi w fallback `DbException` → `503` (konwencje, „Errors and validation”).
3. **Skracanie końca poniżej utworzonych wystąpień (Edge Cases, Acceptance Criteria).** Spec opisuje tylko przesunięcie początku na później („zostawia utworzone wystąpienia”). Przypadek nowego końca wcześniejszego niż istniejące wystąpienia nie ma opisu ani testu, choć dotyczy kryterium „zmiana definicji nie zmienia utworzonych”.
4. **Stan „Aktywny” bez kolejnego terminu (reguły 6 i 8; „Dodanie wydatku cyklicznego” pkt 2).** Gdy `EndsOn ≥ dzisiaj`, a do końca nie wypada już żaden termin (np. koniec 25.11, dzień 28), stan to „Aktywny”, `nextDueDate = null`, a podgląd pokazuje „bo koniec już minął”. To nieprawda. Akcja „Wstrzymaj” jest wtedy dozwolona, choć nie ma czego wstrzymać. Trzeba doprecyzować tekst i regułę.
5. **`nextDueDate` pomija należny, a nieutworzony termin (reguła 6).** Między 00:00 a 00:10 albo przy nieudanym zadaniu szczegóły pokazują termin z następnego miesiąca, choć okres bieżący jest należny i nie ma wystąpienia. Trzeba ustalić, czy tak ma zostać (zwłaszcza przy braku ostrzeżenia, problem krytyczny 2).
6. **Faza odczytu zadania czyta definicje zakończone bez końca (Behavior → „Zadanie harmonogramu” pkt 2.1).** Filtr `PausedOn IS NULL, OccurrencesFrom ≤ D, StartsOn ≤ D` nie pomija definicji z `EndsOn < OccurrencesFrom` ani definicji w pełni obsłużonych do `EndsOn`. Zbiór kandydatów i odczyt rejestru rosną więc z każdą definicją przez cały czas życia systemu. Wydajnościowo to drobiazg, ale warto dodać warunek `EndsOn IS NULL OR EndsOn ≥ OccurrencesFrom`.
7. **Limit 120 wystąpień (Data/API → walidacja; Assumptions 6).** Limit odrzuca dane poprawne według PRD (początek sprzed ponad 10 lat). PRD nie przewiduje limitu. Decyzja jest jawna, ale warto ją potwierdzić z właścicielem. Przy edycji komunikat „Ustaw późniejszy początek” może wskazywać złą przyczynę, gdy limit przekracza przedłużenie końca.
8. **Powtórzenie z kluczem idempotencji (Behavior → „Dodanie wydatku cyklicznego” pkt 4).** Powtórzenie po utraconej odpowiedzi daje `createdOccurrences: 0`, więc komunikat w UI pomija utworzone wystąpienia. Powtórzenie z tym samym kluczem po usunięciu definicji tworzy nową definicję i od nowa wszystkie minione wystąpienia, czyli N zdublowanych kosztów. Prawdopodobieństwo jest małe, ale skutek większy niż w SF-15. Warto opisać to jako znane ograniczenie albo zachować ślad klucza po usunięciu.
9. **PRD wymaga aktualizacji po odpowiedziach z 08.11.2026 (Assumptions 1–3).** PRD §5 nadal mówi, że „poza zapisem nowej definicji tworzenie zaległych wystąpień dotyczy wyłącznie przerw w działaniu systemu” i że „wznowienie tworzy bieżący … okres”. Odpowiedzi 1–3 to doprecyzowują: przedłużenie tworzy zaległe, zmiana dnia tworzy bieżący, a wznowienie po minionym terminie nie tworzy bieżącego. Spec jest zgodna z odpowiedziami, ale PRD powinno je zapisać jako decyzje, żeby kolejne zadania (SF-19, SF-21) nie czytały sprzecznej reguły.
10. **Analiza blokad (Behavior → „Tworzenie wystąpień” pkt 1; „Współbieżność i błędy”).** Spec zakłada odczyt `UPDLOCK, ROWLOCK` encji śledzonej przez EF Core i brak zakleszczeń między edycją (`UPDATE` z wersją, potem generowanie) a zadaniem (`UPDLOCK`, potem wstawienia z FK do definicji). Model jest poprawny, ale nie opisuje, jak zablokować wiersz w EF (np. `FromSql`) ani jakie ma być zachowanie przy zakleszczeniu (błąd 1205 → `503` albo ponowienie). Warto dodać jedno zdanie i test.

### Unclear or Ambiguous Sections

- **Behavior → „Tworzenie wystąpień”, pkt 6:** „postępuje jak w opisie danego przypadku”, a opisy przypadków nie podają tego zachowania (Non-Critical 2).
- **Context → Application, `Recurring/RecurringOccurrencesRun`:** nie wiadomo, kto tworzy zakresy DI (Non-Critical 1).
- **Behavior → „Dodanie wydatku cyklicznego”, pkt 2:** tekst „bo koniec już minął” dla każdego `nextDueDate = null` (Non-Critical 4).
- **Assumptions 11:** uzasadnienie powołuje się na odpowiedź 1 („wbrew odpowiedzi 1”), choć ta odpowiedź nie mówi o nieutworzonych okresach sprzed bieżącego (Critical 1).
- **Edge Cases → „Przedłużenie zakończonej definicji”:** dopisek „poza okresami z terminem w czasie wstrzymania” w przykładzie, w którym nie ma wstrzymania. Lepiej rozdzielić to na dwa przykłady (z przerwą wstrzymania i bez niej).

### Invented or Unsupported Requirements

None. Ograniczenia spoza PRD (limit 120, zakres dat 2000–2099, odrzucenie wstrzymania definicji zakończonej, odrzucenie definicji bez terminu) są jawnie opisane jako decyzje autora w Assumptions 5, 6 i 16. Limit 120 warto potwierdzić (Non-Critical 7).

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Behavior → reguła 7 (zmiana dnia); Assumptions 11 | PRD §5 („Po przerwie w działaniu system tworzy wszystkie zaległe wystąpienia”; zmiana „dotyczy … w tym zaległych”), WYD-03–06 („także zaległe po przerwie”) | Zmiana dnia trwale odcina nieutworzone okresy z przerwy działania |
| Behavior → reguła 7; Assumptions 11 („także przy jednoczesnym przedłużeniu końca”) | Assumptions 2 (odpowiedź właściciela z 08.11.2026) | Łączne przedłużenie i zmiana dnia nie tworzą zaległych okresów między dawnym końcem a dzisiaj; wynik zależy od kolejności zapisów |
| Scope → Out of scope („widok stanu zadania poza `GET /api/jobs`”) | `docs/tasks/sf-11/02-spec.md` Assumptions („UI dodadzą SF-14, SF-16, SF-19”), Out of scope, UI; breakdown zadania 11 | Ostrzeżenie o niepowodzeniu zadania, przypisane SF-16, jest pominięte bez decyzji |
| Behavior → „Wstrzymanie i wznowienie” pkt 2; Scope (przedłużenie, zmiana dnia) | PRD §5 („Wznowienie tworzy bieżący i kolejne okresy”; „poza zapisem nowej definicji tworzenie zaległych wystąpień dotyczy wyłącznie przerw”) | Rozstrzygnięte odpowiedziami 1–3 z 08.11.2026; spec zgodna z odpowiedziami, PRD nieaktualne (Non-Critical 9) |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | OK | Wystąpienie z `PaidOn = null`, koszt według daty kosztu, wypływ dopiero po „Oznacz jako opłacony”; jest test |
| 2. Pieniądze jako decimal w backendzie | OK | `decimal(18,2)`, kwoty jako stringi, terminy i liczby liczy backend, podgląd z backendu |
| 3. Dostęp egzekwowany w API | OK | Wszystkie trasy, także `preview`, wymagają sesji, a zapisy XSRF; `404` dla konkretnej definicji; testy `EndpointSecurityTests` |
| 4. Ponowienia nie duplikują | OK | `PK_RecurringExpensePeriods (DefinitionId, Period)` w tej samej transakcji co wydatek, rejestr przeżywa usunięcie wydatku, blokada `UPDLOCK` porządkuje zadanie i użytkownika, `Idempotency-Key` z unikalnym indeksem; kaskada rejestru przy usunięciu definicji nie otwiera duplikatu (nowa definicja ma nowe id). Zastrzeżenie: Non-Critical 8 |
| 5. Audyt finansowy | OK | `Created`, `Updated` (także wstrzymanie i wznowienie), `Deleted` definicji z autorem konta i datą biznesową = początek; wystąpienie z `AutomatedJob` i rodzicem; późniejsze zmiany z rodzicem; `AuditRecord` dopuszcza tego rodzica (zweryfikowane w `AuditRecord.RequireAllowedParent`) |
| 6. Sekrety po stronie serwera | OK | Komunikaty wyjątków i logi zadania tylko z liczbami i id |
| 7. Granice modułów i warstw | OK | Wszystko w module Expenses i schemacie `expenses`; ze Shared tylko kontrakty harmonogramu, audytu, `IUnitOfWork`, `ILaunchDate`. Do doprecyzowania warstwa zakresów DI (Non-Critical 1) |
| 8. Brak danych identyfikujących w dokumentach | OK | Przykłady fikcyjne |

## Risk

- Recorded: high
- Assessed: high. Zadanie bez udziału użytkownika zapisuje koszty trafiające do raportów, a usunięcie definicji jest nieodwracalne. Nowa logika okresów na styku z SF-11 bezpośrednio wpływa na kompletność kosztów (Critical 1 i 2). Przed merge wymagany jest przegląd przez człowieka.

## Assumptions Detected

- Odpowiedzi właściciela 1–3: zapisane (Assumptions 1–3). Spec realizuje odpowiedzi 1 i 3 poprawnie. Odpowiedź 2 tylko wtedy, gdy zapis nie zmienia jednocześnie dnia (Critical 1).
- Rejestr zamiast kursora, granica `OccurrencesFrom`: zapisane (4). Poprawne wobec niezmiennika 4.
- `PausedOn` bez historii przedziałów, wstrzymanie najpierw tworzy należne, odrzucenie wstrzymania definicji zakończonej: zapisane (5). Spójne z odpowiedziami 2 i 3.
- Tworzenie w transakcji zapisu i limit 120: zapisane (6). Limit do potwierdzenia (Non-Critical 7).
- Wystąpienia sprzed uruchomienia powstają, raporty je wykluczają (SF-19/SF-20): zapisane (7). Zgodne z PRD §5.
- `CostDate = DueDate = termin`, brak `DocumentReference`: zapisane (8).
- Autor „Zadanie automatyczne” także przy zapisie użytkownika: zapisane (9). Zgodne z PRD §3.
- Fizyczne usunięcie definicji, kaskada rejestru, `RecurringDefinitionId` bez FK: zapisane (10). Poprawne.
- Zmiana dnia odcina okresy sprzed bieżącego: zapisane (11). Sprzeczne z PRD i odpowiedzią 2 (Critical 1).
- „Miniony” = termin przed dzisiaj: zapisane (12).
- Serializacja blokadą wiersza zamiast zmiany wersji: zapisane (13). Szczegóły blokady w EF i zakleszczenia: niezapisane (Non-Critical 10).
- Zadanie 00:10, 30 min, jedna jednostka na definicję: zapisane (14). Zgodne z kontraktem `IScheduledJob` (brak zapisów po anulowaniu, własne zakresy).
- Pierwszeństwo stanu „Zakończony”: zapisane (15).
- Walidacja dodatkowa: zapisane (16).
- Umiejscowienie UI, podgląd jako `GET`, filtr przez parametr adresu, idempotencja, E2E w SF-21, ryzyko: zapisane (17–22). E2E w SF-21 potwierdza breakdown (zadanie 21).
- **Niezapisane:** pominięcie ostrzeżenia o niepowodzeniu zadania, wbrew rozdziałowi z SF-11 (Critical 2); miejsce tworzenia zakresów DI (Non-Critical 1); zachowanie wystąpień po skróceniu końca (Non-Critical 3); poziom izolacji i zachowanie blokady w EF (Non-Critical 10).

## Recommendation

- Revise specification
