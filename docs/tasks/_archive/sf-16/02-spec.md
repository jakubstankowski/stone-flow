# Task Specification — SF-16

## Source

[SF-16](https://standev.youtrack.cloud/issue/SF-16)

Odpowiedzi właściciela na pytania doprecyzowujące: 08.11.2026 (Assumptions 1–3).

> **Poprawka redakcyjna v6 (08.11.2026), po przeglądzie v6 (PASS WITH ISSUES):**
> - NC1: osobny tekst podglądu dla definicji wstrzymanej.
> - NC2: przypadek „wznowienie, potem zmiana dnia”.
> - NC3: kolejność walidacji przy `PUT`.
> - NC4: uzasadnienie Assumptions 30 i test „skrócenie → przedłużenie → wznowienie”.
> - NC5: brzmienie „bieżący okres powstaje w dniu terminu”.
> - NC6: zablokowane pole dnia nadal trafia do żądania.

> **Wersja 6 (08.11.2026), po przeglądzie `03-spec-review-v5.md` (FAIL).** Zmiany:
> - **Krytyczne:** zmiana dnia miesiąca na definicji wstrzymanej jest odrzucana (`409
>   recurring_expense_paused`), a w UI pole dnia jest wtedy zablokowane z opisem. Generator nigdy
>   nie tworzy wystąpień definicji wstrzymanej: reguły 4 i 6, edycja, szczegóły, podgląd (0)
>   i zadanie. Pozostałe zmiany wstrzymanej definicji (koniec, początek, pola) są dozwolone, a ich
>   skutki naliczają się przy wznowieniu. Doszedł przykład recenzenta z testem.
> - **NC1/NC2:** przeformułowane fragmenty PRD §5.
> - **NC3:** walidacja „co najmniej jeden termin” i `LastDueDate` liczą termin efektywny, więc
>   okres z wystąpieniem liczy się terminem z rejestru.
> - **NC4:** reguła 2 dla okresów przed pierwszym wpisem.
> - **NC5:** nowe przypadki brzegowe i testy: kilka wstrzymań i przedłużenie, nakładające się
>   przedziały, początek później → wcześniej, `droppedDueCount` przy późniejszym początku.
> - **NC6:** `Reason` jako nazwa enumu (`Pause`, `StartMoved`).
> - **NC7:** indeks kandydatów z `StartsOn`.

> **Wersja 5 (08.11.2026), po przeglądzie `03-spec-review-v4.md` (FAIL: kolejność „najpierw dzień,
> potem przedłużenie” gubiła lipiec–październik).**
>
> Model jednej granicy `OccurrencesFrom` (v1–v4) zastępuje model zależny wyłącznie od stanu
> definicji, a nie od kolejności zapisów:
> - historia dni miesiąca: tabela `RecurringExpenseDays`, dzień obowiązuje od okresu;
> - przedziały wyłączeń: tabela `RecurringExpenseExclusions` (wstrzymanie, przesunięcie początku na
>   wcześniej).
>
> Okres kwalifikuje się według czystej funkcji domeny z reguły 4. Kroki (a)/(b) i wszystkie reguły
> granicy usunięte. Na nowy model przepisane są:
> - podgląd, `nextDueDate`, stan „Zakończony” (`LastDueDate` liczone z historii dni) i kandydaci
>   zadania;
> - audyt i tekst PRD §5.
>
> Nowe przypadki brzegowe i testy: v3-Critical2 z późniejszym przedłużeniem, v4-Critical, v4-NC1
> i v4-NC2 (podgląd „przestaje się liczyć”). Pozostałe ustalenia z v2–v4 zostają bez zmian:
> ostrzeżenie o zadaniu, idempotencja, blokada, `503`.
>
> **Historia:**
> - v2 dodała ostrzeżenie o zadaniu, tabelę `RecurringDefinitionRequests`, `LastDueDate`, wsady bez
>   limitu i aktualizację PRD;
> - v3 dodała ponowienie tylko w zadaniu, zakleszczenie przez fallback `503`, poprawiony `CHECK`,
>   blokadę na początku każdego przypadku użycia, dialog „Wznów” z podglądu i kolejność rozstrzygania
>   powtórzeń;
> - v4 doprecyzowała `version` po zapisie definicji. Reguły zmiany dnia z v2–v4 są nieaktualne.

## Goal

Moduł Expenses dostaje wydatek cykliczny. Definicja ma pola wydatku poza datami, dzień miesiąca,
początek, opcjonalny koniec i aktywność (wstrzymanie i wznowienie). Wystąpienie to zwykły wiersz
`expenses.Expenses` bez daty zapłaty, z datą kosztu i terminem płatności równymi terminowi okresu.
Jest więc kosztem i planem, a nie wypływem (D-04).

O tym, które okresy definicja tworzy, decyduje jedna czysta funkcja domeny ze stanu definicji:
początku, końca, historii dni miesiąca, przedziałów wyłączeń i rejestru okresów. Wynik nie zależy od
kolejności zapisów.

Wystąpienia tworzy jedna operacja modułu, wywoływana:
- przy zapisie definicji, nowej i zmienionej;
- przy wstrzymaniu i wznowieniu;
- przez dzienne zadanie harmonogramu SF-11 `recurring-expenses`, które nadrabia też terminy
  z przerwy w działaniu systemu.

Przy każdym utworzeniu wystąpienia w tej samej jednostce pracy powstaje wiersz rejestru z kluczem
(definicja, okres). Dlatego ponowienie, restart i wyścig nie tworzą duplikatu (niezmiennik 4),
a usunięte wystąpienie nie wraca.

Wstrzymanie zapisuje przedział wyłączenia, więc terminy z czasu wstrzymania zostają trwale puste.
Terminy z przerwy w działaniu nie są wyłączone, więc są uzupełniane.

Formularz „Powtarzaj co miesiąc” pokazuje przed zapisem podgląd następnego wystąpienia i liczbę
wystąpień, które powstaną od razu. Wylicza je backend. Niepowodzenie zadania widać w UI jako
ostrzeżenie, że koszty mogą być zaniżone.

## Context

- **Backend, moduł Expenses** (schemat `expenses`, SF-15). Zmiany w warstwach:
  - **Domain** `Expenses.Domain/Recurring/` (nowy folder):
    - encja `RecurringExpenseDefinition`:
      - treść, `StartsOn`, `EndsOn`, `PausedOn`, bieżący `DayOfMonth`, `LastDueDate`;
      - kolekcje `Days` (historia dni: `FromPeriod`, `DayOfMonth`) i `Exclusions` (przedziały:
        `FromDate`, `ToDate` wyłącznie, `Reason`);
      - operacje `Edit`, `Pause`, `Resume`;
      - wyliczanie zmienionych pól, jak `Expense.Snapshot`;
    - `RecurringSchedule`: czyste funkcje (reguły 1–8):
      - dzień okresu i termin okresu (dzień 29–31 → ostatni dzień krótszego miesiąca);
      - kwalifikacja okresu i okresy należne na dany dzień;
      - najbliższy nieutworzony termin, ostatni termin i stan definicji;
    - `RecurringPeriod` (rok, miesiąc), `RecurringDefinitionField` (pola audytu),
      `RecurringDefinitionStatus` (`Active`, `Paused`, `Ended`), `RecurringExclusionReason`
      (`Pause`, `StartMoved`);
    - `Expense` (`Expenses.Domain/Expenses/Expense.cs`): nowe właściwości `RecurringDefinitionId`
      i `RecurringPeriod` oraz fabryka `CreateOccurrence(...)`. `CreateManual` i reguły kwot bez
      zmian.
  - **Application:**
    - foldery `CreateRecurringExpense/`, `EditRecurringExpense/`, `PauseRecurringExpense/`,
      `ResumeRecurringExpense/`, `DeleteRecurringExpense/`, `GetRecurringExpense/`,
      `ListRecurringExpenses/`, `PreviewRecurringExpense/`;
    - foldery zadania: `GenerateRecurringOccurrences/` (komenda jednej definicji z własną jednostką
      pracy) i `ListRecurringGenerationCandidates/` (zapytanie fazy odczytu);
    - `RecurringExpenseInput` (walidacja i normalizacja; kwoty parsują istniejące pomocniki
      `ExpenseInput`, bez kopii) i `RecurringExpenseAudit`;
    - `Recurring/OccurrenceGenerator`: operacja modułu (bez dispatchera), wołana przez handlery zapisu
      i komendę zadania w jednostce pracy wywołującego;
    - porty w `Abstractions/`:
      - `IRecurringExpenseStore`: zapis definicji z dniami i wyłączeniami, rejestru i żądań
        idempotencji;
      - `IRecurringDefinitionLock`: blokada wiersza definicji, wzór `IOrderLock`;
      - `IRecurringExpenseReader`: DTO listy i szczegółów, kandydaci zadania, dni, wyłączenia
        i okresy z rejestru;
    - zmiany w SF-15:
      - `ExpenseChangeRunner` i `DeleteExpenseHandler` podają rodzica audytu
        (`RecurringExpenseDefinition`, `RecurringDefinitionId`), gdy wydatek jest wystąpieniem;
      - `ListExpenses` dostaje filtr `recurringDefinitionId`;
      - DTO listy i szczegółów dostają pola cykliczności.
  - **Infrastructure:**
    - konfiguracje w `Persistence/`: `RecurringExpenseDefinitionConfiguration.cs` (z kolekcjami dni
      i wyłączeń), `RecurringExpensePeriodConfiguration.cs`,
      `RecurringDefinitionRequestConfiguration.cs`;
    - implementacje portów: `RecurringExpenseStore`, `RecurringDefinitionLock`,
      `RecurringExpenseReader`;
    - zmiana `ExpenseConfiguration`: dwie kolumny i indeks;
    - migracja `RecurringExpenses` w `ExpensesDbContext`, tylko dodanie;
    - `Recurring/RecurringExpensesJob.cs`: `IScheduledJob` jak `BackfillJob` (SF-13), rejestrowane
      w `DependencyInjection.cs` przez `services.AddScheduledJob<RecurringExpensesJob>(...)`. Tylko ta
      klasa (Infrastructure) ma pętlę po definicjach, zakresy DI (`IServiceScopeFactory`)
      i ponowienia.
  - **Api:**
    - `Endpoints/RecurringExpensesEndpoints.cs` i `Endpoints/RecurringExpenseContracts.cs`, grupa
      `/api/recurring-expenses`;
    - nowe kody w `ExpenseProblems.cs`;
    - zmiana kontraktów w `ExpenseContracts.cs`: pola cykliczności i filtr listy.
- **Shared (bez zmian):**
  - harmonogram SF-11: `IScheduledJob`, `JobDefinition`, `JobSchedule.DailyAt`, `JobRunContext`.
    Stan zadań daje `GET /api/jobs` (`GetJobStatusesQuery`, `JobStatusDto`) dla ostrzeżenia w UI;
  - `IUnitOfWork` i `IAuditLog` (SF-7). `AuditAuthor.AutomatedJob`
    i `AuditObjectType.RecurringExpenseDefinition` istnieją, a `AuditRecord` dopuszcza dla `Expense`
    rodzica `RecurringExpenseDefinition`;
  - `ILaunchDate` (podgląd liczby wystąpień sprzed uruchomienia) i `WarsawTimeZone` przez lokalny
    `WarsawTime`.

  Brak nowych typów współdzielonych. Moduł nie odwołuje się do innych modułów.
- **Frontend `features/expenses/`:**
  - `expense-create-page` i `expense-form`: opcja „Powtarzaj co miesiąc” z sekcją „Harmonogram”
    i podglądem;
  - nowe strony `recurring-expenses-list-page` i `recurring-expense-details-page` (szczegóły,
    edycja, wstrzymanie, wznowienie, usunięcie);
  - klient `recurring-expenses-api.ts` i serwis stanu `recurring-expenses.service.ts`;
  - ostrzeżenie `recurring-job-notice` z klientem `GET /api/jobs` i czystą funkcją stanu;
  - zmiany w liście i szczegółach wydatku: oznaczenie „Cykliczny”, przejście do definicji, filtr
    wystąpień jednej definicji.
- **Dokumentacja:** `docs/PRD-stone-flow.md` §5 dostaje odpowiedzi właściciela z 08.11.2026 i reguły
  z nich wynikające („Behavior → PRD”).
- **Testy:**
  - domena i walidacja: `api/tests/StoneFlow.Expenses.Tests`;
  - HTTP, baza i zadanie: `StoneFlow.Api.IntegrationTests/Expenses/`, jak w SF-15.

## Scope

### In scope

- **Definicja** (PRD §7): opis, kategoria, kontrahent, kwota brutto (> 0 zł), opcjonalna netto,
  metoda, płatnik, komentarz, dzień miesiąca 1–31, początek, opcjonalny koniec, aktywność.
- **Reguły terminu:**
  - termin okresu to dzień miesiąca obowiązujący w tym okresie, a w krótszym miesiącu ostatni dzień;
  - okres liczy się, gdy termin wypada między początkiem a końcem, włącznie z tymi dniami, i nie
    wypada w przedziale wyłączenia;
  - wystąpienie powstaje najwcześniej w dniu terminu.
- **Historia dni miesiąca:** zmiana dnia obowiązuje od bieżącego okresu, a dzień wcześniejszych
  okresów się nie zmienia.
- **Przedziały wyłączeń:** wstrzymanie (od wstrzymania do wznowienia) i przesunięcie początku na
  wcześniej.
- **Rejestr okresów** z unikalnością (definicja, okres). Powstaje przy tworzeniu wystąpienia i nie
  znika przy usunięciu wydatku.
- **Tworzenie wystąpień:**
  - przy zapisie nowej definicji: za wszystkie terminy od początku do dzisiaj, włącznie
    z dzisiejszym, w tej samej jednostce pracy;
  - przy zapisie zmiany, wstrzymaniu (przed zmianą stanu) i wznowieniu: wszystkie okresy, które się
    kwalifikują, są należne i nie powstały;
  - codziennie przez zadanie harmonogramu, także zaległe po przerwie w działaniu systemu.
- **Podgląd przed zapisem:**
  - liczba wystąpień, które powstaną od razu (w tym sprzed daty uruchomienia);
  - termin następnego wystąpienia;
  - liczba należnych okresów, które po zmianie przestaną się liczyć;
  - liczba utworzonych wystąpień spoza nowego zakresu.
- **Wstrzymanie i wznowienie** z komunikatem, że okresy z czasu wstrzymania pozostają puste.
- **Zmiana definicji** dotyczy tylko wystąpień jeszcze nieutworzonych, w tym zaległych. Utworzone
  się nie zmieniają.
- **Usunięcie definicji** z potwierdzeniem. Utworzone wystąpienia zostają zwykłymi wydatkami.
- **Audyt:**
  - definicja (`Created`, `Updated` także dla zmiany dnia, wstrzymania i wznowienia, `Deleted`)
    z autorem konta;
  - utworzenie wystąpienia z autorem „Zadanie automatyczne” i rodzicem = definicja;
  - późniejsze zmiany wystąpienia z autorem konta i tym samym rodzicem.
- **UI:**
  - formularz „Powtarzaj co miesiąc” w „Dodaj wydatek”;
  - lista i szczegóły wydatków cyklicznych jako podsekcja „Wydatki”;
  - oznaczenie „Cykliczny” na liście wydatków i przejście od wystąpienia do definicji.
- **Ostrzeżenie o niepowodzeniu zadania** `recurring-expenses` na liście wydatków i liście wydatków
  cyklicznych (SF-11 przypisało je SF-16). SF-19 może osadzić ten sam komponent.
- **Aktualizacja PRD §5** („Behavior → PRD”).

### Out of scope

- Historia wersji definicji (zgłoszenie, PRD §7). Historia dni i przedziały wyłączeń służą tylko
  regułom terminów, nie przechowują wersji treści.
- Wykluczanie z raportów wystąpień z terminem sprzed daty uruchomienia: SF-19 (koszty) i SF-20
  (wypływy). SF-16 zapisuje je jak każdy wydatek.
- Lista „Historia zmian” (SF-18). SF-16 tylko zapisuje wpisy z rodzicem.
- Harmonogramy inne niż miesięczne, kilka wystąpień w okresie, przypomnienia i powiadomienia.
- Ręczne uruchomienie zadania z UI („Uruchom teraz”).
- Ostrzeżenie w widokach raportowych: komponent jest gotowy, a osadzenie należy do SF-19.
- Zmiana istniejącego wydatku jednorazowego w definicję i odwrotnie.
- Wykrywanie, że nowa definicja dubluje koszty z usuniętej definicji o tych samych danych.
- Konfiguracja harmonogramu w panelu hostingu i dowód z restartu i uśpienia (D-08 pkt 2). To krok
  ręczny po wdrożeniu, jak w SF-11 i SF-13.

## Behavior

### Model terminów (`RecurringSchedule`)

1. **Okres** to miesiąc kalendarzowy (rok, miesiąc), w bazie `date` pierwszego dnia miesiąca, w API
   `yyyy-MM`. **Bieżący okres** to miesiąc dzisiejszej daty. **„Dzisiaj”** to data w Europe/Warsaw
   z `TimeProvider` (`WarsawTime.Today`), liczona raz na przypadek użycia albo raz na przebieg
   zadania.
2. **Dzień okresu** `dzień(p)` bierze się z historii dni definicji (`RecurringExpenseDays`): to dzień
   z wpisu o największym `FromPeriod ≤ p`. Okres wcześniejszy niż pierwszy wpis dostaje dzień
   pierwszego wpisu. Takie okresy istnieją tylko po przesunięciu początku na wcześniejszy miesiąc.
   Ich dzień może się zmienić, gdy zmiana dnia usunie wpis z przyszłości (np. początek 15.03.2027
   przesunięty na 01.09.2026, potem zmiana dnia na 25: wrzesień i październik 2026 dostają 25
   zamiast 10). Nie wpływa to na koszty: ich terminy leżą przed dniem przesunięcia, więc
   w przedziale `StartMoved`, a dzisiejszy i przyszłe terminy podlegają nowemu dniu zgodnie z
   regułą.
   - Dodanie definicji tworzy jeden wpis od okresu początku.
   - Zmiana dnia ustawia wpis od **bieżącego okresu** (dodaje go albo zastępuje). Usuwa też wpisy
     z `FromPeriod` późniejszym niż bieżący okres, które istnieją tylko przy początku
     w przyszłości. Dzień okresów wcześniejszych, objętych wpisami, się nie zmienia (wyjątek dla
     okresów przed pierwszym wpisem opisuje akapit wyżej).
   - **Na definicji wstrzymanej dnia nie można zmienić:** `409 recurring_expense_paused`, „Wznów
     wydatek cykliczny, aby zmienić dzień miesiąca.” (Assumptions 30).
   - `DayOfMonth` definicji to dzień ostatniego wpisu, czyli dzień bieżącego i przyszłych okresów.
     Domena go utrzymuje (niezmiennik sprawdzany testem), a wyświetla go lista.
3. **Termin okresu:** `t(p) = data(p.rok, p.miesiąc, min(dzień(p), liczba dni w p))`.
   - Dla dnia 31: 31.01, 28.02.2027, 29.02.2028, 31.03, 30.04.
   - Dla dnia 30 w lutym: ostatni dzień lutego.
   - Dla dnia 29 w lutym roku nieprzestępnego: 28.02.
4. **Kwalifikacja okresu.** Okres p **kwalifikuje się**, gdy spełnia wszystkie warunki:
   - `StartsOn ≤ t(p) ≤ EndsOn` (bez końca nie ma górnej granicy);
   - `t(p)` nie należy do żadnego przedziału wyłączeń `[FromDate, ToDate)`;
   - definicja nie jest wstrzymana (`PausedOn` puste). Przy wstrzymanej definicji nie kwalifikuje
     się **żaden** okres, więc generator niczego nie tworzy: ani przy edycji wstrzymanej definicji,
     ani w zadaniu (kandydaci pomijają wstrzymane). Przedział `[PausedOn, dzień wznowienia)`
     powstaje dopiero przy wznowieniu (reguła 5). `resumePreview` liczy kwalifikację tak, jakby
     wznowienie nastąpiło dzisiaj;
   - okresu p nie ma w rejestrze definicji.

   Okres kwalifikujący się jest **należny na dzień D**, gdy dodatkowo `t(p) ≤ D`. Kandydaci to okresy
   od okresu `StartsOn` do okresu `min(D, EndsOn)`, a funkcja zwraca je rosnąco. Wynik zależy tylko
   od stanu definicji: początku, końca, dni, wyłączeń, `PausedOn` i rejestru. Nie zależy od
   kolejności zapisów.
5. **Przedziały wyłączeń** (`RecurringExpenseExclusions`, `FromDate` włącznie, `ToDate` wyłącznie):
   - **wstrzymanie** zapisuje tylko `PausedOn = dzisiaj`. Do wznowienia żaden okres się nie
     kwalifikuje (reguła 4);
   - **wznowienie** dodaje przedział `[PausedOn, dzień wznowienia)` z powodem `Pause` i czyści
     `PausedOn`. Termin w dniu wznowienia się liczy (odpowiedź 3). Wstrzymanie i wznowienie tego
     samego dnia dają przedział pusty, którego się nie zapisuje;
   - **przesunięcie początku na wcześniej** (nowy `StartsOn` < stary) dodaje przedział `[nowy
     początek, min(stary początek, dzisiaj))` z powodem `StartMoved`, o ile przedział jest
     niepusty. Terminy, które weszły w zakres dopiero przez tę zmianę i przypadają przed dzisiaj
     (minione), nie tworzą wystąpień (PRD: „zmiana początku nie tworzy minionych wystąpień”,
     Assumptions 12). Dzisiejszy termin się liczy i powstaje przy zapisie, tak jak przy dodaniu
     definicji („włącznie z dzisiejszym”). Terminy przyszłe w nowym zakresie też się liczą;
   - zmiana końca, dnia, kwot i pól treści nie tworzy przedziałów;
   - przedziały nie są usuwane ani zmieniane. Usunięcie definicji usuwa je kaskadą.
6. **Terminy pochodne:**
   - **najbliższy nieutworzony termin** (`nextDueDate` w szczegółach i na liście): termin
     najwcześniejszego kwalifikującego się okresu (reguła 4, bez warunku `t(p) ≤ D`).
     - Może być ≤ dzisiaj, gdy okres jest należny, a jeszcze nie powstał (np. przed przebiegiem
       o 00:10 albo przy błędzie zadania).
     - Może być ≤ `LastDueDate` przy stanie „Zakończony”, gdy nie powstał ostatni termin.
     - Brak wartości tylko wtedy, gdy nie kwalifikuje się żaden okres. Dotyczy to zawsze definicji
       wstrzymanej (reguła 4);
   - **następny termin po zapisie** (`nextDueDate` w podglądzie i w `resumePreview`): ta sama reguła
     po utworzeniu wszystkich należnych okresów, więc zawsze > dzisiaj;
   - **termin efektywny** `e(p)`: dla okresu z rejestru to termin zapisany w rejestrze (`DueDate`),
     a dla pozostałych `t(p)`. Okres z wystąpieniem liczy się więc swoim rzeczywistym terminem,
     także po zmianie dnia;
   - **ostatni termin** (`LastDueDate`): najpóźniejszy `e(p)` w [`StartsOn`, `EndsOn`]. Bez końca
     brak wartości. Nie zależy od wyłączeń. Jest **kolumną**, którą domena przelicza (z historii dni
     i rejestru, wczytanych pod blokadą) przy każdym zapisie zmieniającym początek, koniec albo dni.
     Generowanie jej nie zmienia, bo nowe wystąpienie ma `e(p) = t(p)`. Dzięki temu filtr stanu
     w SQL jest prosty (Assumptions 15).
7. **Stan definicji** (`RecurringDefinitionStatus`), według pierwszego spełnionego warunku:
   - `Ended` („Zakończony”): `LastDueDate` jest podana i `LastDueDate ≤ dzisiaj`, czyli nie wypada
     już żaden kolejny termin;
   - `Paused` („Wstrzymany”): `PausedOn` jest uzupełnione;
   - `Active` („Aktywny”): w pozostałych przypadkach.

   Stan nie jest przechowywany. Lista i szczegóły biorą go z tej funkcji. SQL powtarza regułę tylko
   w filtrze `state` (kolumny `LastDueDate`, `PausedOn`), a test sprawdza zgodność, jak `state`
   w SF-15. Stan „Zakończony” nie ukrywa nieutworzonego ostatniego terminu: szczegóły pokazują go
   jako `nextDueDate`.
8. **Skutki modelu** (każdy ma przypadek brzegowy i test):
   - **odpowiedź 1:** okres z wystąpieniem nie dostaje drugiego (rejestr). Bieżący okres bez
     wystąpienia, którego nowy termin już minął, powstaje przy zapisie zmiany. Nowy termin
     w przyszłości powstanie w dniu terminu;
   - **odpowiedź 2:** przedłużenie tworzy kwalifikujące się okresy między dawnym końcem a dzisiaj,
     w dowolnej kolejności względem zmiany dnia. Okresy z przedziałów `Pause` nie powstają;
   - **odpowiedź 3:** termin z przedziału wstrzymania nie powstaje, termin w dniu wznowienia
     powstaje;
   - **marzec przed początkiem (v2):** początek 15.03, dzień 10, a w listopadzie zmiana na 20. Dzień
     marca to nadal 10, a 10.03 < 15.03, więc marzec nie powstaje;
   - **sierpień po wznowieniu (v2):** wstrzymanie 01.08 i wznowienie 15.08 dają przedział
     [01.08, 15.08). Termin 10.08 jest w przedziale, także po późniejszej zmianie dnia, bo dzień
     sierpnia się nie zmienia;
   - **v3-Critical1:** początek 20.11, dzień 10, tego samego dnia zmiana na 25. Wpis od listopada
     zastępuje wpis z dodania, więc listopad powstanie 25.11, jak przy dodaniu od razu z dniem 25;
   - **v3-Critical2:** koniec 25.06, dzień 28, w listopadzie zmiana na 20. Dzień czerwca to nadal 28,
     a 28.06 > 25.06, więc czerwiec nie powstaje. Po późniejszym przedłużeniu do grudnia 28.06 jest
     w zakresie, więc czerwiec powstaje, zgodnie z odpowiedzią 2 („między dawnym końcem a
     dzisiaj”);
   - **v4-Critical:** „najpierw dzień, potem przedłużenie” daje te same okresy i terminy co
     „najpierw przedłużenie, potem dzień”. Lipiec–październik mają dzień sprzed zmiany i powstają;
   - **v4-NC1:** gdy zapis łączny i dwa osobne zapisy różnią się wynikiem, to tylko dlatego, że
     zapis pośredni utworzył już wystąpienie, które rejestr potem utrwala. Przykład: koniec w bieżącym
     miesiącu i zmiana dnia, w jednym zapisie albo w dwóch. Wtedy może się różnić termin albo
     obecność okresu bieżącego. Tę różnicę akceptujemy (Assumptions 11). W pozostałych przypadkach
     zbiory okresów są równe;
   - **v4-NC2:** należny, a nieutworzony okres bieżący, którego nowy termin po zmianie wypada poza
     zakres (np. przed początkiem), przestaje się liczyć. Podgląd pokazuje to przed zapisem
     (`droppedDueCount`).

### Blokada i kolejność kroków w przypadkach użycia

Każdy przypadek użycia, który zmienia definicję albo tworzy jej wystąpienia, zaczyna jednostkę pracy
od **blokady wiersza definicji** przez `IRecurringDefinitionLock`. Dotyczy to edycji, wstrzymania,
wznowienia, usunięcia i komendy zadania.

- Blokada to `context.Database.SqlQuery(... FROM [expenses].[RecurringExpenseDefinitions] WITH
  (UPDLOCK, ROWLOCK) WHERE [Id] = {id})`, jak `OrderLock`, w transakcji `IUnitOfWork` przy
  domyślnej izolacji READ COMMITTED.
- Dni i wyłączenia należą do definicji, więc blokada wiersza definicji chroni też je.
- Dodanie definicji nie bierze blokady, bo niezatwierdzonej definicji nikt inny nie widzi.

Kolejność kroków:

1. blokada (brak wiersza: `404` u użytkownika, „brak definicji” w zadaniu);
2. wczytanie definicji z dniami i wyłączeniami oraz sprawdzenie wersji (`409`);
3. reguła domeny (np. już wstrzymana);
4. krok właściwy:
   - **edycja i wznowienie:** operacja domeny, zapis definicji z oczekiwaną wersją, audyt `Updated`,
     generowanie z nowym stanem (zmiana dotyczy wystąpień nieutworzonych, w tym zaległych);
   - **wstrzymanie:** generowanie z dotychczasowym stanem, potem zapis wstrzymania i audyt;
   - **komenda zadania:** tylko generowanie;
   - **usunięcie:** audyt `Deleted` i usunięcie.

Zwracana `version` to wersja po zapisie definicji. Generowanie nie zmienia definicji, więc nie
zmienia wersji. Blokada trwa do końca transakcji, więc zmiana użytkownika i generowanie tej samej
definicji wykonują się po kolei. Dzięki temu nie powstanie wystąpienie ze starą kwotą zatwierdzone
po zapisie nowej ani wystąpienie po zatwierdzonym wstrzymaniu.

### Tworzenie wystąpień (`OccurrenceGenerator`)

Jedna operacja modułu, wywoływana w jednostce pracy wywołującego (handler zapisu albo komenda
zadania) z dniem D. Wejściem jest stan definicji z dniami i wyłączeniami. Wywołujący trzyma już
blokadę wiersza, a przy dodaniu definicja jest nowa.

1. Wczytuje okresy definicji z rejestru i liczy okresy należne na D (reguła 4).
2. Dla każdego okresu p tworzy `Expense.CreateOccurrence`:
   - opis, kategoria, kontrahent, brutto, netto, metoda, płatnik i komentarz z definicji w chwili
     tworzenia;
   - `CostDate = DueDate = t(p)`, `PaidOn = null`, `DocumentReference = null`;
   - `CreationRequestId` i `CreationRequestHash` puste;
   - `RecurringDefinitionId` = id definicji, `RecurringPeriod` = p.
3. Dla każdego wystąpienia dodaje wiersz rejestru (definicja, p, termin, id wydatku). Zapisuje audyt
   `Created` wydatku z autorem `AuditAuthor.AutomatedJob` i rodzicem
   `(RecurringExpenseDefinition, id definicji)`.
4. **Wsady:** zapis wsadami po 100 okresów (`SaveChanges` na wsad) w tej samej transakcji.
   Definicja od 2000 r. tworzy kilkaset wystąpień bez jednej ogromnej partii poleceń, a całość jest
   atomowa.
5. Zwraca liczbę utworzonych wystąpień. Nie zmienia definicji ani jej wersji.
6. **Kolizja klucza rejestru.** Naruszenie `PK_RecurringExpensePeriods` (`2601`/`2627`) port tłumaczy
   na wynik `DuplicateRequest`: okres utworzył ktoś inny, to nie jest błąd biznesowy. Wywołujący
   zwraca wynik, który wycofuje jednostkę (`commitWhen` = `false`). Przy blokadzie to ostatnia
   linia obrony.
   - **Zadanie** (`RecurringExpensesJob`, Infrastructure) ponawia definicję raz w nowym zakresie DI
     i nowej jednostce pracy. Konwencja wymaga, by po wycofaniu zakres niczego już nie zapisywał.
     Druga kolizja liczy się jako błąd definicji.
   - **Handler użytkownika** (edycja, wstrzymanie, wznowienie) nie ponawia i zwraca
     `503 recurring_expense_busy`: „Nie udało się zapisać z powodu równoczesnej operacji. Spróbuj
     ponownie.”
   - **Dodanie definicji** nie może kolidować, bo nowa definicja ma nowe id.
7. **Zakleszczenie (`1205`)** nie jest tłumaczone na wynik. Wyjątek bazy przechodzi do
   `UnitOfWork`.
   - Na ścieżce wyjątku `UnitOfWork` nie woła `RollbackAsync`. Wycofanie robią zwolnienie transakcji
     (`await using`) i `ModuleConnection.EndTransaction(false)`. SQL Server sam wycofał już
     transakcję ofiary, a zwolnienie takiej transakcji nie zgłasza wyjątku. Jeśli plan wykaże, że
     zwolnienie albo czyszczenie kontekstów jednak rzuca, `UnitOfWork` loguje i połyka wyjątek
     wycofania, a dalej idzie wyjątek oryginalny.
   - U użytkownika wyjątek trafia do istniejącego fallbacku `DbException` → `503`. UI traktuje
     `5xx` jako błąd `network` i pokazuje „Nie udało się zapisać. Spróbuj ponownie.”, a dane
     zostają w formularzu.
   - Zadanie rozpoznaje numer `1205` w łańcuchu wyjątku (w Infrastructure) i ponawia definicję raz
     w nowym zakresie. Drugi błąd liczy się jako błąd definicji.

### Dodanie wydatku cyklicznego

1. **Wejście.** W „Dodaj wydatek” (`/wydatki/nowy`) użytkownik zaznacza „Powtarzaj co miesiąc”. Pole
   wyboru jest na początku formularza. Z listy wydatków cyklicznych wejście
   `/wydatki/nowy?cykliczny=1` otwiera formularz z zaznaczoną opcją. Formularz:
   - ukrywa sekcję „Daty” (data kosztu, termin, data zapłaty) i pole „Referencja dokumentu”;
   - pokazuje sekcję „Harmonogram”: „Dzień miesiąca” (wymagany, 1–31), „Początek” (wymagany,
     domyślnie dzisiaj), „Koniec” (opcjonalny);
   - zmienia podpowiedź kwoty brutto na „Kwota większa od 0 zł.”;
   - ma osobny klucz idempotencji (UUID), generowany przy otwarciu.

   Odznaczenie opcji przywraca poprzedni układ i wpisane daty.
2. **Podgląd** (region `aria-live="polite"` pod „Harmonogramem”). Po zmianie dnia, początku albo
   końca, przy poprawnym formacie, UI odpytuje `GET /api/recurring-expenses/preview` (opóźnienie
   300 ms, ostatnie żądanie wygrywa) i pokazuje:
   - „Następne wystąpienie: {nextDueDate}. Powstanie tego dnia jako wydatek planowany — wcześniej nie
     będzie go na liście i nie można go opłacić.” Gdy następnego terminu nie ma: „Po zapisie nie
     powstaną kolejne wystąpienia — do końca nie wypada już żaden termin.”;
   - przy `occurrencesToCreate > 0`: „Po zapisie od razu powstanie {N} {wystąpienie/wystąpienia/
     wystąpień} za terminy od {firstImmediateDueDate} do dzisiaj.”;
   - przy `beforeLaunchCount > 0`: „{N} z nich ma termin sprzed uruchomienia systemu — nie wejdą do
     raportów.”;
   - przy edycji:
     - `droppedDueCount > 0`: „{N} {należne wystąpienie, które jeszcze nie powstało, przestaje/…}
       się liczyć po tej zmianie.”;
     - `createdAfterEndCount > 0`: „{N} utworzonych wystąpień ma termin po nowym końcu —
       pozostaną.”;
     - `createdBeforeStartCount > 0`: „{N} utworzonych wystąpień ma termin przed nowym początkiem —
       pozostaną.”;
   - przy błędzie podglądu: „Nie udało się przygotować podglądu.” Zapis jest nadal możliwy;
   - przy edycji definicji wstrzymanej podgląd pokazuje zamiast linii o następnym wystąpieniu
     i liczbach tylko: „Wydatek cykliczny jest wstrzymany — nowe wystąpienia nie powstają; zmiany
     zadziałają od wznowienia.” (NC1). Zdanie „do końca nie wypada już żaden termin” nie pojawia
     się dla wstrzymanej definicji. Liczby `createdAfterEndCount` i `createdBeforeStartCount` są
     nadal pokazywane.
3. **Zapis.** „Zapisz” przechodzi w `busy`. UI waliduje, a API waliduje ponownie
   (`RecurringExpenseInput`) i w jednej jednostce pracy:
   1. szuka żądania po kluczu w `RecurringDefinitionRequests`;
   2. tworzy definicję (`PausedOn = null`, wpis dnia od okresu początku, bez wyłączeń,
      `LastDueDate` według reguły 6);
   3. zapisuje audyt `Created` definicji z autorem konta;
   4. woła `OccurrenceGenerator` na dzisiaj;
   5. zapisuje wiersz żądania (klucz, id definicji, skrót, `CreatedOccurrences`).

   Zatwierdzany jest tylko wynik `Created`.
4. **Odpowiedzi:**
   - sukces: `201 { id, version, createdOccurrences }` z `Location: /api/recurring-expenses/{id}`;
   - klucz już zapisany: najpierw porównanie skrótu żądania z zapisanym, niezależnie od istnienia
     definicji:
     - inny skrót: `409 idempotency_key_reused` z `recurringExpenseId`, także po usunięciu
       definicji;
     - ten sam skrót, definicja istnieje: `200 { id, version, createdOccurrences }` z liczbą zapisaną
       przy pierwszym zapisie;
     - ten sam skrót, definicja usunięta: `409 recurring_expense_deleted`, nic nie powstaje;
   - wyścig o klucz kończy się naruszeniem PK `RecurringDefinitionRequests`. Po wycofaniu żądanie
     jest czytane na nowo, jak w `CreateExpenseHandler`. Skrót liczy się jak w SF-15, z kanonicznego
     JSON-a znormalizowanej treści.
5. **Po sukcesie** UI przechodzi do `/wydatki/cykliczne/{id}` z komunikatem „Wydatek cykliczny
   zapisany.” albo „Wydatek cykliczny zapisany. Utworzono {N} wystąpień.” Komunikat jest
   w `aria-live` i nie znika sam.

### Zadanie harmonogramu `recurring-expenses`

1. **Rejestracja:** `new JobDefinition("recurring-expenses", JobSchedule.DailyAt(new TimeOnly(0, 10)),
   TimeSpan.FromMinutes(30))`. Godzina 00:10 Europe/Warsaw nie wypada w przerwie zmiany czasu,
   a wystąpienie ma powstać w dniu terminu. Przerwę w działaniu SF-11 nadrabia jednym
   uruchomieniem. Zadanie tworzy wtedy wszystkie zaległe okresy, bo przerwa nie zapisuje przedziału
   wyłączenia.
2. **Przebieg** (`RecurringExpensesJob`, Infrastructure). D = dzisiaj w chwili startu przebiegu.
   1. **Faza odczytu:** jeden zakres DI, zapytanie Application `ListRecurringGenerationCandidates(D)`
      ze stałą liczbą zapytań (definicje, dni, wyłączenia, okresy z rejestru), bez N+1.
      - Kandydaci: `PausedOn IS NULL`, `StartsOn ≤ D` oraz (`EndsOn IS NULL` albo
        `EndsOn ≥ D − 400 dni`).
      - Domena zwraca id definicji z co najmniej jednym okresem należnym (Assumptions 14).
   2. **Jednostka pracy na definicję:** nowy zakres DI i `IDispatcher` z komendą
      `GenerateRecurringOccurrencesCommand(definitionId, D)`.
      - Handler w Application otwiera `IUnitOfWork`, bierze blokadę i woła `OccurrenceGenerator`.
        Okresy są liczone od nowa, więc zmiana definicji po fazie odczytu jest uwzględniona.
      - Przed każdą definicją zadanie sprawdza token anulowania.
      - „Brak definicji” i zero okresów nie są błędami.
      - Po `DuplicateRequest` albo zakleszczeniu zadanie ponawia definicję raz w **nowym** zakresie
        (operacja, pkt 6–7).
      - Application nie używa zakresów DI ani service locatora.
   3. **Wyjątek w definicji:** wycofanie jej jednostki, log z id definicji (bez opisu, kontrahenta
      i płatnika), przejście do następnej. Błąd jednej definicji nie blokuje innych.
   4. **Koniec przebiegu:** log z liczbą sprawdzonych definicji, utworzonych wystąpień i błędów.
      Jeśli były błędy, przebieg kończy się wyjątkiem „Nie utworzono wystąpień dla {n} wydatków
      cyklicznych.” (bez danych osobowych). SF-11 zapisuje wtedy `failed` i ponawia po 15 min, potem
      po 1 h, a UI pokazuje ostrzeżenie. Ponowienie jest bezpieczne.
3. **Anulowanie** (`MaxDuration`, zamknięcie aplikacji): bieżąca jednostka się wycofuje, kolejne się
   nie zaczynają, a wyjątek anulowania przechodzi do harmonogramu. Po anulowaniu nic nie jest
   zapisywane. Zatwierdzone definicje zostają, resztę nadrobi kolejny przebieg.
4. **Restart w trakcie przebiegu:** zatwierdzone jednostki zostają. Przebieg `abandoned` (SF-11)
   uruchamia zadanie ponownie, a ono tworzy tylko brakujące okresy.
5. Zadanie nie zmienia definicji, więc otwarty formularz edycji nie dostaje konfliktu wersji po
   przebiegu nocnym.

### Ostrzeżenie o niepowodzeniu zadania (`recurring-job-notice`)

1. **Odczyt.** Komponent w `features/expenses/` czyta `GET /api/jobs` raz przy wejściu na stronę.
   Nie odpytuje w tle i nie odświeża się sam (konwencja SF-14, DOST-01). Bierze pozycję
   `recurring-expenses`. Klucz i próg to stałe obok funkcji stanu.
2. **Reguła.** Stan liczy czysta funkcja `recurringJobWarning(status, now)` z `JobStatusDto`
   i czasu przeglądarki. Ostrzeżenie pokazuje się, gdy pozycja zadania istnieje i spełniony jest
   jeden z warunków:
   - `lastRun.status` to `failed` albo `abandoned`;
   - `lastSuccessStartedAtUtc` jest podane i starsze niż 25 h.

   Bez ostrzeżenia zostają: zadanie bez żadnego przebiegu, pierwszy przebieg `running` bez
   wcześniejszego sukcesu (komponent nic nie pokazuje) i `running` przy świeżym sukcesie.
3. **Treść.** Wygląd jak `app-incomplete-data-notice`: ton ostrzeżenia, ikona z `aria-hidden`,
   znaczenie niesie tekst. Pierwsze zdanie: „Automatyczne tworzenie wydatków cyklicznych nie
   powiodło się ({czas}). Koszty mogą być zaniżone.” Drugie: „Ostatnie udane tworzenie: {czas
   ostatniego sukcesu}” albo „Ostatnie udane tworzenie: Brak danych”.
   - Czasy są w Europe/Warsaw (`plDateTime`).
   - W nawiasie jest czas ostatniego przebiegu. Gdy powodem jest wiek sukcesu, a ostatni przebieg
     się udał albo go nie ma, w nawiasie jest czas sukcesu.
4. **Błąd odczytu** (sieć, `5xx`) daje stan ostrożny: „Nie udało się sprawdzić automatycznego
   tworzenia wydatków cyklicznych. Koszty mogą być zaniżone.” W czasie ładowania komponent nic nie
   pokazuje i nie przesuwa układu.
5. **Osadzenie:** nad tabelą `/wydatki` i `/wydatki/cykliczne`. Komponent nie ma wejść, więc SF-19
   może go osadzić bez zmian.

### Lista wydatków cyklicznych

1. **Tabela.** `/wydatki/cykliczne` (tytuł „Wydatki cykliczne”), `app-data-table`. Kolumny:
   - „Opis” z przejściem do szczegółów;
   - „Kategoria”;
   - „Kwota brutto” (do prawej, `tabular-nums`);
   - „Dzień miesiąca”: bieżący `DayOfMonth`, np. „10.”, a dla 29–31 „31. (w krótszym miesiącu
     ostatni dzień)”;
   - „Okres”: „od 01.11.2026” albo „01.11.2026 – 31.12.2027”;
   - „Najbliższy termin” (`nextDueDate` albo „Brak”);
   - „Stan”: `app-status-badge` z tekstem „Aktywny” (`success`), „Wstrzymany” (`warning`) albo
     „Zakończony” (`neutral`).
2. **Filtry i sortowanie:**
   - filtr stanu (wszystkie, aktywne, wstrzymane, zakończone) i tekstu (opis, kontrahent);
   - sortowanie po opisie (domyślnie rosnąco), początku i kwocie brutto; remis rozstrzyga `Id`;
   - domyślnie 25, najwyżej 100 na stronę;
   - stan filtrów w serwisie, jak w SF-15.
3. **Główna akcja:** „Dodaj wydatek cykliczny” (`primary`) prowadzi do `/wydatki/nowy?cykliczny=1`.
4. **Stany widoku:**
   - ładowanie: skeleton;
   - pusto: „Brak wydatków cyklicznych” z akcją dodania;
   - pusty wynik filtrów: „Brak wydatków cyklicznych dla wybranych filtrów” z „Wyczyść filtry”;
   - błąd z „Spróbuj ponownie”.
5. **Podsekcja:** nad listami „Wydatki” i „Wydatki cykliczne” są linki „Wszystkie wydatki”
   i „Wydatki cykliczne” (`aria-current="page"`). Główna nawigacja się nie zmienia (design guide §3).

### Szczegóły, edycja i usunięcie definicji

1. **Szczegóły** (`/wydatki/cykliczne/{id}`):
   - nagłówek z opisem i stanem;
   - przy wstrzymaniu stała informacja: „Wstrzymany od {pausedOn}. Okresy z terminem w czasie
     wstrzymania pozostaną puste.”;
   - sekcje danych:
     - kwoty;
     - harmonogram: bieżący dzień miesiąca z wyjaśnieniem dla 29–31, początek, koniec albo „Bez
       końca”;
     - płatność;
     - komentarz;
   - „Najbliższy termin”, w tej kolejności:
     - definicja wstrzymana: „Brak — wydatek cykliczny jest wstrzymany”. Wstrzymana definicja nie ma
       `nextDueDate` (reguła 4), więc nie pojawia się obietnica utworzenia przez zadanie;
     - termin ≤ dzisiaj: „Termin {data} już minął, a wystąpienie jeszcze nie powstało — utworzy je
       zadanie automatyczne.” Ma pierwszeństwo przed stanem „Zakończony”;
     - termin późniejszy: zdanie o niemożności wcześniejszej zapłaty, jak w podglądzie;
     - brak kwalifikującego się okresu: „Brak”;
   - „Utworzone wystąpienia: {occurrenceCount}” z linkiem „Pokaż wystąpienia”;
   - techniczne czasy utworzenia i zmiany (Europe/Warsaw).

   Brak wartości to „Brak danych”.
2. **Akcje:**
   - „Edytuj” (`primary`);
   - „Wstrzymaj” (stan aktywny) albo „Wznów” (gdy `PausedOn` jest uzupełnione, także przy stanie
     zakończonym);
   - „Usuń” (`danger`);
   - „Wróć do listy”.

   Stan zakończony bez wstrzymania nie ma akcji „Wstrzymaj”. Gdy formularz edycji jest otwarty,
   pozostałe akcje zapisu są niedostępne.
3. **Edycja.** Formularz w trybie definicji (bez „Powtarzaj co miesiąc”), wypełniony aktualnymi
   danymi, z bieżącym dniem miesiąca. Nad przyciskami stała informacja: „Zmiany dotyczą tylko
   wystąpień, które jeszcze nie powstały, także zaległych. Nowy dzień miesiąca obowiązuje od
   bieżącego miesiąca. Utworzone wystąpienia poprawia się pojedynczo na liście wydatków.” Podgląd
   działa jak przy dodaniu, z `definitionId`.

   **Edycja wstrzymanej definicji:** pole „Dzień miesiąca” jest zablokowane (`disabled`) z opisem
   „Wznów wydatek cykliczny, aby zmienić dzień miesiąca.” powiązanym przez `aria-describedby`. Nad
   przyciskami pojawia się informacja „Wydatek cykliczny jest wstrzymany. Zmiany zadziałają od
   wznowienia.” Pozostałe pola (koniec, początek, kwoty, treść) można zmienić. Zapis niczego nie
   tworzy, a podgląd pokazuje 0. Skutki naliczą się przy wznowieniu, według reguły 4 i przedziału
   `[PausedOn, dzień wznowienia)`:
   - przedłużenie końca da okresy po dawnym końcu, z wyjątkiem terminów z wstrzymania;
   - przesunięcie początku na wcześniej doda przedział `StartMoved` dla terminów minionych;
   - nowe pola trafią do wystąpień tworzonych od wznowienia.

   „Zapisz zmiany” wysyła `PUT` z `version` i zawsze z polem `dayOfMonth`. Przy zablokowanym polu
   formularz bierze wartość z modelu (odpowiednik `getRawValue()`), a nie pomija pola (NC6).
   Kolejność w API (NC3):
   1. walidacja pól bez stanu (format, zakresy, kwoty, koniec ≥ początek; `400`);
   2. w jednej jednostce pracy: blokada, istnienie (`404`) i wersja
      (`409 recurring_expense_conflict`);
   3. reguła domeny: definicja wstrzymana i `dayOfMonth` różny od bieżącego →
      `409 recurring_expense_paused`, przed walidacją harmonogramu;
   4. walidacja harmonogramu na stanie po zmianie (co najmniej jeden termin efektywny w zakresie;
      `400`, klucz `endsOn`);
   5. operacja domeny `Edit` (reguły 2, 5, 6):
      - nowe pola;
      - przy zmianie dnia wpis od bieżącego okresu;
      - przy przesunięciu początku na wcześniej przedział `StartMoved`;
      - nowy `LastDueDate`;
      - lista zmienionych pól;
   6. bez zmian: sukces bez zapisu, audytu i generowania, z dotychczasową wersją
      i `createdOccurrences: 0`;
   7. zapis z oczekiwaną wersją (`DbUpdateConcurrencyException` → `409`);
   8. audyt `Updated` z samymi zmienionymi polami;
   9. `OccurrenceGenerator` na dzisiaj z nowym stanem; przy wstrzymanej definicji tworzy 0.
      `DuplicateRequest` wycofuje całość i daje `503 recurring_expense_busy`.

   Odpowiedź `200 { version, createdOccurrences }`, gdzie `version` pochodzi z kroku 7. UI odświeża
   szczegóły i pokazuje „Zmiany zapisane.” albo „Zmiany zapisane. Utworzono {N} wystąpień.”
4. **Usunięcie.** `app-confirm-dialog` (`danger`):
   - nagłówek „Usunąć wydatek cykliczny?”;
   - treść: „Wydatek cykliczny „{opis}” zostanie usunięty i nie utworzy kolejnych wystąpień.
     Utworzone wystąpienia ({occurrenceCount}) pozostaną zwykłymi wydatkami w ewidencji
     i raportach. Ślad usunięcia zostanie w historii zmian.”;
   - przyciski „Usuń” i „Anuluj”.

   `DELETE /api/recurring-expenses/{id}` z `{ version }`:
   1. wersja (`400`);
   2. blokada, istnienie (`404`) i wersja (`409`);
   3. audyt `Deleted` ze wszystkimi niepustymi polami;
   4. fizyczne usunięcie definicji.

   Kaskada usuwa dni, wyłączenia i rejestr. Wiersze żądań idempotencji zostają. Wystąpienia zostają
   bez zmian, z dotychczasowym `RecurringDefinitionId`. Odpowiedź `204`, po której UI wraca do listy
   z komunikatem „Wydatek cykliczny usunięty.”

### Wstrzymanie i wznowienie

1. **„Wstrzymaj”** otwiera `app-confirm-dialog`:
   - nagłówek: „Wstrzymać wydatek cykliczny?”;
   - treść: „Do czasu wznowienia nie powstaną nowe wystąpienia. Okresy z terminem w czasie
     wstrzymania pozostaną puste — wznowienie ich nie uzupełni.”;
   - przyciski: „Wstrzymaj” i „Anuluj”.

   `POST /api/recurring-expenses/{id}/pause { version }`, w jednej jednostce pracy:
   1. blokada, istnienie (`404`) i wersja (`409 recurring_expense_conflict`);
   2. reguła domeny: już wstrzymany → `409 recurring_expense_already_paused`, stan `Ended` →
      `409 recurring_expense_ended`;
   3. `OccurrenceGenerator` na dzisiaj, **przed** zmianą stanu. Terminy do dzisiaj włącznie, które
      nie powstały np. przez błąd zadania, powstają teraz. Bez tego dzisiejszy termin wpadłby
      w `[PausedOn, …)`;
   4. `PausedOn = dzisiaj`, zapis z wersją, audyt `Updated` (`pausedOn`: `null` → data).

   Odpowiedź `200 { version, createdOccurrences }`. UI pokazuje „Wstrzymano wydatek cykliczny.”
2. **„Wznów”** otwiera `app-confirm-dialog`:
   - nagłówek: „Wznowić wydatek cykliczny?”;
   - treść wyłącznie z `resumePreview` szczegółów:
     - `occurrencesToCreate > 0`: „Po wznowieniu od razu powstanie wystąpienie za bieżący okres.”;
     - `occurrencesToCreate = 0` i jest `nextDueDate`: „Kolejne wystąpienie powstanie
       {nextDueDate}.”;
     - `occurrencesToCreate = 0` i brak `nextDueDate`: „Nie powstaną kolejne wystąpienia — do końca
       nie wypada już żaden termin.”;
     - zawsze na końcu: „Okresy z terminem w czasie wstrzymania (od {pausedOn}) pozostaną puste.”;
   - przyciski: „Wznów” i „Anuluj”.

   `POST /api/recurring-expenses/{id}/resume { version }`, w jednej jednostce pracy:
   1. blokada, istnienie (`404`) i wersja (`409`);
   2. reguła domeny: niewstrzymany → `409 recurring_expense_not_paused`;
   3. przedział `[PausedOn, dzisiaj)` z powodem `Pause` (pominięty, gdy pusty), `PausedOn = null`,
      zapis z wersją, audyt `Updated` (`pausedOn`: data → `null`);
   4. `OccurrenceGenerator` na dzisiaj (odpowiedź 3): termin w dniu wznowienia powstaje, a termin
      z przedziału nie.

   Odpowiedź `200 { version, createdOccurrences }`. UI pokazuje „Wznowiono wydatek cykliczny.”

### Wystąpienie na liście i w szczegółach wydatku

1. Na liście `/wydatki` wiersz z `recurringDefinitionId` ma pod opisem tekst „Cykliczny”. Kolumny,
   filtry i sortowanie z SF-15 się nie zmieniają.
2. **Filtr wystąpień jednej definicji.** „Pokaż wystąpienia” prowadzi do
   `/wydatki?wydatekCykliczny={id}`.
   - Lista odczytuje parametr raz i ustawia filtr `recurringDefinitionId` w serwisie stanu.
   - Filtr pokazuje się jako aktywny: „Wydatek cykliczny: {opis}”. Można go usunąć osobno albo przez
     „Wyczyść filtry”.
   - Gdy definicji nie ma: „Wydatek cykliczny (usunięty)”.
3. **Szczegóły wystąpienia** (`/wydatki/{id}`):
   - informacja „Wystąpienie wydatku cyklicznego za {październik 2026}” z linkiem „Przejdź do
     wydatku cyklicznego”, a po usunięciu definicji tekst „Wydatek cykliczny został usunięty.”;
   - edycja, akcje zapłaty i usunięcie jak w SF-15;
   - audyt z rodzicem `(RecurringExpenseDefinition, RecurringDefinitionId)`, także po usunięciu
     definicji.
4. Dialog usunięcia wystąpienia dostaje zdanie: „To wystąpienie wydatku cyklicznego nie zostanie
   utworzone ponownie. W razie pomyłki dodaj wydatek ręcznie.” (PRD §5).
5. Edycja wystąpienia nie zmienia `RecurringDefinitionId` ani `RecurringPeriod`. `ExpenseInput` nie
   przyjmuje tych pól.

### Współbieżność i błędy

- **Konflikt wersji** (`409 recurring_expense_conflict`): dane zostają, komunikat „Wydatek cykliczny
  został zmieniony w międzyczasie. Wczytaj aktualne dane.” z „Wczytaj ponownie”.
- **Kolizja rejestru** (`503 recurring_expense_busy`) i **zakleszczenie** (`503` z fallbacku): dane
  zostają, komunikat „Nie udało się zapisać. Spróbuj ponownie.” z „Spróbuj ponownie”. API nie
  ponawia.
- **Zmiana użytkownika i zadanie nocne** wykonują się po kolei dzięki blokadzie. Zadanie nie zmienia
  wersji definicji.
- **`404`** po usunięciu w innej karcie: „Nie znaleziono wydatku cyklicznego. Mógł zostać usunięty.”
  z „Wróć do listy”.
- **`400`** z polami: błąd przy polu i podsumowanie nad formularzem, dane zostają.
- **`401`:** globalny interceptor (SF-6).

### PRD — aktualizacja §5

W „Wydatek jednorazowy i miesięczny” zmieniają się dwa fragmenty, żeby PRD było spójne z odpowiedziami
z 08.11.2026 i z modelem z reguł 2–5. Dopisek „decyzja użytkownika z 08.11.2026” mają tylko zdania
z treścią odpowiedzi 1–3. Pozostałe zdania mają dopisek „reguła wynikająca”. Reszta sekcji się nie
zmienia.

1. Zamiast „Zmiana początku istniejącej definicji nie tworzy minionych wystąpień.”:

   > Zmiana początku istniejącej definicji nie tworzy minionych wystąpień: terminy, które weszły
   > w zakres przez przesunięcie początku na wcześniej i przypadają przed dniem zmiany, nie tworzą
   > wystąpień, a termin w dniu zmiany się liczy (reguła wynikająca). Nowy dzień miesiąca obowiązuje
   > od bieżącego okresu, a okresy wcześniejsze zachowują dotychczasowy dzień i według niego liczą
   > się albo nie; dnia nie można zmienić, gdy definicja jest wstrzymana (reguła wynikająca). Okres,
   > który ma już wystąpienie, nie dostaje drugiego, więc nowy dzień zadziała w nim dopiero od
   > następnego okresu; jeśli bieżący okres nie ma jeszcze wystąpienia, a nowy termin już minął,
   > wystąpienie bieżącego okresu powstaje od razu (decyzja użytkownika z 08.11.2026).

2. Zamiast „Wznowienie tworzy bieżący i kolejne okresy, a okresy z czasu wstrzymania pozostają
   puste; poza zapisem nowej definicji tworzenie zaległych wystąpień dotyczy wyłącznie przerw
   w działaniu systemu.”:

   > Wstrzymana definicja nie tworzy wystąpień. Po wznowieniu wystąpienie bieżącego okresu powstaje
   > w dniu terminu, jeśli termin nie minął w czasie wstrzymania, a kolejne okresy powstają jak
   > zwykle; okresy z czasu wstrzymania pozostają puste; jeśli w chwili wznowienia termin bieżącego okresu już minął w czasie wstrzymania,
   > wznowienie nie tworzy wystąpienia za ten okres, a kolejne powstają od następnego okresu (decyzja
   > użytkownika z 08.11.2026). Późniejsza zmiana dnia ocenia bieżący okres według nowego terminu
   > (reguła wynikająca). Poza zapisem nowej definicji zaległe wystąpienia powstają
   > po przerwie w działaniu systemu oraz przy przedłużeniu zakończonej definicji: za okresy między
   > dawnym końcem a dzisiaj, bez okresów z czasu wstrzymania, a formularz pokazuje wcześniej ich
   > liczbę (decyzja użytkownika z 08.11.2026).

## Data / API

### Storage

Migracja `RecurringExpenses` w `ExpensesDbContext` tylko dodaje tabele, kolumny i indeksy (expand).
Poprzednia wersja API działa na nowym schemacie: nowe kolumny `Expenses` są nullowalne i nieużywane
przez stary kod.

**`expenses.RecurringExpenseDefinitions`** (nowa):

| Kolumna | Typ | Uwagi |
|---|---|---|
| `Id` | `uniqueidentifier` PK | nadawany w aplikacji |
| `Description` | `nvarchar(200)` not null | |
| `Category` | `nvarchar(32)` not null | nazwa wartości `ExpenseCategory` |
| `Counterparty` | `nvarchar(200)` null | |
| `GrossAmount` | `decimal(18,2)` not null | |
| `NetAmount` | `decimal(18,2)` null | |
| `PaymentMethod` | `nvarchar(16)` null | nazwa wartości `ExpensePaymentMethod` |
| `Payer` | `nvarchar(100)` null | |
| `Comment` | `nvarchar(2000)` null | |
| `DayOfMonth` | `tinyint` not null | bieżący dzień = dzień ostatniego wpisu w `RecurringExpenseDays` |
| `StartsOn` | `date` not null | początek; data biznesowa audytu |
| `EndsOn` | `date` null | koniec włącznie |
| `LastDueDate` | `date` null | ostatni termin w zakresie (reguła 6), pusty bez końca |
| `PausedOn` | `date` null | dzień wstrzymania; pusty = aktywny |
| `CreatedAtUtc`, `UpdatedAtUtc` | `datetime2` not null | |
| `Version` | `rowversion` | właściwość cienia, jak w `ExpenseConfiguration` |

- Ograniczenia `CHECK`:
  - `CK_RecurringExpenseDefinitions_GrossAmount`: `[GrossAmount] > 0`;
  - `CK_..._NetAmount`: `[NetAmount] IS NULL OR ([NetAmount] >= 0 AND [NetAmount] <= [GrossAmount])`;
  - `CK_..._DayOfMonth`: `[DayOfMonth] BETWEEN 1 AND 31`;
  - `CK_..._EndsOn`: `[EndsOn] IS NULL OR [EndsOn] >= [StartsOn]`;
  - `CK_..._LastDueDate`: `([EndsOn] IS NULL AND [LastDueDate] IS NULL) OR ([EndsOn] IS NOT NULL
    AND [LastDueDate] IS NOT NULL AND [LastDueDate] >= [StartsOn] AND [LastDueDate] <= [EndsOn])`.
    Oba człony są jawne, bo `CHECK` przepuszcza wynik `UNKNOWN`.
- Indeksy: `IX_..._Description` (`Description`, `Id`) do sortowania listy i `IX_..._Generation`
  (`PausedOn`, `EndsOn`) `INCLUDE (StartsOn)` do fazy odczytu zadania. Przy skali z D-08 (jeden
  użytkownik, kilkadziesiąt definicji) indeks ma znaczenie drugorzędne, a test wydajności
  (500 definicji) i tak obejmuje fazę odczytu.

**`expenses.RecurringExpenseDays`** (historia dni, nowa):

| Kolumna | Typ | Uwagi |
|---|---|---|
| `DefinitionId` | `uniqueidentifier` not null | FK → definicje, `ON DELETE CASCADE` |
| `FromPeriod` | `date` not null | pierwszy dzień miesiąca, od którego obowiązuje dzień |
| `DayOfMonth` | `tinyint` not null | 1–31 |

- PK (`DefinitionId`, `FromPeriod`).
- `CHECK`: `DAY([FromPeriod]) = 1` i `[DayOfMonth] BETWEEN 1 AND 31`.
- Każda definicja ma co najmniej jeden wpis. Pilnuje tego domena, a test sprawdza zapis.

**`expenses.RecurringExpenseExclusions`** (przedziały wyłączeń, nowa):

| Kolumna | Typ | Uwagi |
|---|---|---|
| `Id` | `uniqueidentifier` PK | |
| `DefinitionId` | `uniqueidentifier` not null | FK → definicje, `ON DELETE CASCADE` |
| `FromDate` | `date` not null | włącznie |
| `ToDate` | `date` not null | wyłącznie |
| `Reason` | `nvarchar(16)` not null | nazwa wartości enumu `RecurringExclusionReason` (`HasConversion<string>()`, jak `Category`): `Pause` albo `StartMoved` |
| `CreatedAtUtc` | `datetime2` not null | |

- `CHECK`: `[ToDate] > [FromDate]` i `[Reason] IN ('Pause', 'StartMoved')`.
- Indeks `IX_..._DefinitionId`.
- Wiersze są tylko dodawane.

**`expenses.RecurringExpensePeriods`** (rejestr okresów, nowa):

| Kolumna | Typ | Uwagi |
|---|---|---|
| `DefinitionId` | `uniqueidentifier` not null | FK → definicje, `ON DELETE CASCADE` |
| `Period` | `date` not null | pierwszy dzień miesiąca |
| `DueDate` | `date` not null | termin w chwili utworzenia |
| `ExpenseId` | `uniqueidentifier` not null | wystąpienie; **bez FK**, bo wydatek można usunąć |
| `CreatedAtUtc` | `datetime2` not null | |

- `PK_RecurringExpensePeriods` (`DefinitionId`, `Period`) zapewnia unikalność definicja–okres
  (niezmiennik 4).
- `UX_RecurringExpensePeriods_ExpenseId` jest unikalny.
- `CK_..._Period`: `DAY([Period]) = 1`.
- Wiersz powstaje wyłącznie w `OccurrenceGenerator`. Nie znika przy usunięciu wydatku, usuwa go tylko
  kaskada przy usunięciu definicji.

**`expenses.RecurringDefinitionRequests`** (klucze idempotencji dodania, nowa):

| Kolumna | Typ | Uwagi |
|---|---|---|
| `Key` | `uniqueidentifier` PK | nagłówek `Idempotency-Key` |
| `DefinitionId` | `uniqueidentifier` not null | **bez FK**; wiersz zostaje po usunięciu definicji |
| `RequestHash` | `binary(32)` not null | SHA-256 znormalizowanej treści |
| `CreatedOccurrences` | `int` not null | `>= 0`; zwracana przy powtórzeniu |
| `CreatedAtUtc` | `datetime2` not null | |

Indeks `IX_..._DefinitionId`.

**`expenses.Expenses`** (zmiana SF-15):

| Kolumna | Typ | Uwagi |
|---|---|---|
| `RecurringDefinitionId` | `uniqueidentifier` null | **bez FK**, zostaje po usunięciu definicji |
| `RecurringPeriod` | `date` null | okres wystąpienia |

- `CK_Expenses_Recurring`: obie kolumny puste albo obie podane.
- Filtrowany indeks `IX_Expenses_RecurringDefinitionId` (`RecurringDefinitionId`, `CostDate`, `Id`)
  `WHERE [RecurringDefinitionId] IS NOT NULL`.

**Tłumaczenia w Infrastructure:**

- `DbUpdateConcurrencyException` → konflikt;
- `2601`/`2627` na PK `RecurringDefinitionRequests` → wynik idempotentny;
- `2601`/`2627` na `PK_RecurringExpensePeriods` → `DuplicateRequest`: ponowienie tylko w zadaniu,
  u użytkownika `503 recurring_expense_busy`;
- `547` (FK, definicja usunięta w trakcie) → „brak definicji”.

`1205` nie jest tłumaczone: wyjątek idzie do `UnitOfWork` i fallbacku `DbException` → `503`
(operacja, pkt 7). Inne naruszenia to błędy programu. Każde tłumaczenie ma test kodu HTTP.

### Endpoints

Wszystkie wymagają sesji (SF-6), a `POST`, `PUT` i `DELETE` także `X-XSRF-TOKEN`. Nie ma endpointów
anonimowych. Nazwy w formacie `.WithName("Expenses.{Action}")`. `{id}` ma ograniczenie `:guid`.

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| GET | `/api/recurring-expenses` | query: `page`, `pageSize` (1–100, domyślnie 25), `sort` (`description`, `startsOn`, `grossAmount`), `direction`, `state` (`active`, `paused`, `ended`), `search` (≤100) | `200 RecurringExpenseListPage { items, page, pageSize, total }`; `400` | sesja | odczyt |
| GET | `/api/recurring-expenses/{id}` | — | `200 RecurringExpenseDetails`; `404 recurring_expense_not_found` | sesja | odczyt |
| GET | `/api/recurring-expenses/preview` | query: `dayOfMonth`, `startsOn`, `endsOn?`, `definitionId?` | `200 RecurringExpensePreview`; `400`; `404` (nieznany `definitionId`); `409 recurring_expense_paused` (inny dzień przy wstrzymanej) | sesja | odczyt |
| POST | `/api/recurring-expenses` | nagłówek `Idempotency-Key`; body `RecurringExpenseInput` | `201 { id, version, createdOccurrences }` + `Location`; `200` przy powtórzeniu; `400` walidacja albo `idempotency_key_required`; `409 idempotency_key_reused` (+ `recurringExpenseId`), `409 recurring_expense_deleted` | sesja + XSRF | zapis |
| PUT | `/api/recurring-expenses/{id}` | `RecurringExpenseInput` + `version` | `200 { version, createdOccurrences }`; `400`; `404`; `409 recurring_expense_conflict`, `409 recurring_expense_paused`; `503 recurring_expense_busy` | sesja + XSRF | zapis |
| POST | `/api/recurring-expenses/{id}/pause` | `{ version }` | `200 { version, createdOccurrences }`; `400`; `404`; `409 recurring_expense_conflict`, `recurring_expense_already_paused`, `recurring_expense_ended`; `503` | sesja + XSRF | zapis |
| POST | `/api/recurring-expenses/{id}/resume` | `{ version }` | `200 { version, createdOccurrences }`; `400`; `404`; `409 recurring_expense_conflict`, `recurring_expense_not_paused`; `503` | sesja + XSRF | zapis |
| DELETE | `/api/recurring-expenses/{id}` | `{ version }` | `204`; `400`; `404`; `409 recurring_expense_conflict` | sesja + XSRF | zapis |
| GET | `/api/expenses` (zmiana SF-15) | dodatkowo `recurringDefinitionId` | jak w SF-15, pozycje z `recurringDefinitionId` | sesja | odczyt |
| GET | `/api/expenses/{id}` (zmiana SF-15) | — | jak w SF-15, z polem `recurring` | sesja | odczyt |
| GET | `/api/jobs` (SF-11, bez zmian) | — | `200 JobStatusDto[]` | sesja | odczyt |

Kody `409` w kolejności jak w SF-15: konflikt wersji przed regułą domeny. Każde `503` w tabeli to
`recurring_expense_busy` albo fallback zakleszczenia.

Problem Details (nowe kody w `ExpenseProblems`):

| Kod | Status | `detail` |
|---|---|---|
| `recurring_expense_not_found` | 404 | „Nie znaleziono wydatku cyklicznego. Mógł zostać usunięty.” |
| `recurring_expense_conflict` | 409 | „Wydatek cykliczny został zmieniony w międzyczasie. Wczytaj aktualne dane.” |
| `recurring_expense_already_paused` | 409 | „Wydatek cykliczny jest już wstrzymany.” |
| `recurring_expense_not_paused` | 409 | „Wydatek cykliczny nie jest wstrzymany.” |
| `recurring_expense_ended` | 409 | „Wydatek cykliczny jest zakończony, więc nie można go wstrzymać.” |
| `recurring_expense_deleted` | 409 | „Ten formularz zapisał już wydatek cykliczny, który został później usunięty. Odśwież formularz, aby dodać nowy.” |
| `recurring_expense_paused` | 409 | „Wznów wydatek cykliczny, aby zmienić dzień miesiąca.” |
| `recurring_expense_busy` | 503 | „Nie udało się zapisać z powodu równoczesnej operacji. Spróbuj ponownie.” |
| `idempotency_key_reused` | 409 | „Ten formularz zapisał już wydatek cykliczny. Odśwież formularz, aby dodać kolejny.” (+ `recurringExpenseId`) |

### Types crossing a boundary

Kwoty to stringi dziesiętne z kropką, daty `yyyy-MM-dd`, okres `yyyy-MM`, a słowniki kody camelCase
z SF-15.

- **`RecurringExpenseInput`:** `description`, `category`, `counterparty?`, `grossAmount`,
  `netAmount?`, `paymentMethod?`, `payer?`, `comment?`, `dayOfMonth` (liczba, bieżący dzień),
  `startsOn`, `endsOn?`. Historii dni i wyłączeń nie przyjmuje się z API, wylicza je domena.
- **Walidacja** w API (UI powtarza reguły bez porównywania kwot i bez liczenia terminów):

  | Pole | Reguła |
  |---|---|
  | `description`, `category`, `counterparty`, `paymentMethod`, `payer`, `comment` | jak w SF-15 |
  | `grossAmount` | wymagana, format SF-15, **> 0**: „Kwota wydatku cyklicznego musi być większa od 0 zł.” |
  | `netAmount` | opcjonalna, `0 ≤ netto ≤ brutto` („Kwota netto nie może być ujemna.”, „Kwota netto nie może być większa od brutto.”) |
  | `dayOfMonth` | wymagany, 1–31: „Dzień miesiąca musi być liczbą od 1 do 31.” |
  | `startsOn` | wymagany, 2000-01-01–2099-12-31: „Początek musi przypadać w latach 2000–2099.” |
  | `endsOn` | opcjonalny, ≤ 2099-12-31, ≥ `startsOn`: „Koniec nie może być wcześniejszy niż początek.” |
  | harmonogram | co najmniej jeden termin efektywny `e(p)` w [`startsOn`, `endsOn`] według historii dni po zmianie i rejestru; okres z wystąpieniem liczy się terminem z rejestru: „W tym okresie nie wypada żaden termin.” (klucz `endsOn`) |

  Teksty są przycinane, a pusty tekst to `null`. Kwoty nie są zaokrąglane. Liczba wystąpień od razu
  nie ma limitu, a bezpiecznikiem jest zakres dat (Assumptions 6).
- **`RecurringExpensePreview`** (backend stosuje zmianę do kopii stanu definicji tymi samymi
  operacjami domeny co zapis):
  - `occurrencesToCreate`: liczba okresów należnych na dzisiaj po zmianie (zawsze 0 przy definicji
    wstrzymanej; `nextDueDate` wtedy `null`);
  - `firstImmediateDueDate`: najwcześniejszy z ich terminów;
  - `beforeLaunchCount`: ile z nich ma termin < `ILaunchDate.Value`;
  - `nextDueDate`: następny termin po zapisie (reguła 6);
  - tylko przy `definitionId`:
    - `droppedDueCount`: okresy należne i nieutworzone **przed** zmianą, które po zmianie się nie
      kwalifikują (v4-NC2);
    - `createdAfterEndCount` i `createdBeforeStartCount`: istniejące wystąpienia definicji (wiersze
      `Expenses` złączone z rejestrem) z terminem rejestru po nowym końcu albo przed nowym
      początkiem. Te zostają.
- **`RecurringExpenseListItem`:** `id`, `description`, `category`, `counterparty`, `grossAmount`,
  `dayOfMonth`, `startsOn`, `endsOn`, `status`, `pausedOn`, `nextDueDate`. `nextDueDate` dla całej
  strony liczy się z dni, wyłączeń i rejestru wczytanych stałą liczbą zapytań.
- **`RecurringExpenseDetails`:**
  - pola wejścia oraz `id`, `status`, `pausedOn`, `nextDueDate`;
  - `occurrenceCount`;
  - `resumePreview { occurrencesToCreate, nextDueDate }`, tylko przy `pausedOn`, liczone jak po
    wznowieniu dzisiaj;
  - `createdAtUtc`, `updatedAtUtc`, `version`.
- **Zmiany SF-15:** `ExpenseListItem` + `recurringDefinitionId`; `ExpenseDetails` + `recurring
  { definitionId, period, definitionExists }` albo `null`; filtr `recurringDefinitionId` łączony AND.
  Frontend SF-15 ignoruje nowe pola.
- **`JobStatusDto`** (SF-11) jako `JobStatus`: `key`, `schedule`, `isRunning`, `lastRun { status,
  startedAtUtc, finishedAtUtc, error }|null`, `lastSuccessStartedAtUtc|null`, `consecutiveFailures`,
  `nextAttemptAtUtc|null`. Komponent nie pokazuje `error`.
- **Audyt definicji** (SF-7, „Value format”):
  - obiekt: `AuditObject(RecurringExpenseDefinition, id, StartsOn, "Wydatek cykliczny – {etykieta
    kategorii}", parent: null)`. Etykieta nie zawiera wolnego tekstu ani danych osobowych;
  - pola: `description`, `category`, `counterparty`, `grossAmount`, `netAmount`, `paymentMethod`,
    `payer` (`IsPersonalData`), `comment`, `dayOfMonth` (bieżący dzień), `startsOn`, `endsOn`,
    `pausedOn`;
  - zmiana dnia, wstrzymanie i wznowienie to `Updated` definicji (`dayOfMonth` stary → nowy,
    `pausedOn`). Okres, od którego obowiązuje nowy dzień, to okres z czasu wpisu;
  - dni, wyłączenia i `LastDueDate` nie są osobnymi polami audytu, bo wynikają z audytowanych zmian
    `dayOfMonth`, `startsOn` i `pausedOn` w chwili wpisu;
  - `Created` zawiera niepuste pola, `Updated` tylko zmienione, a `Deleted` wszystkie niepuste jako
    stare wartości. Autorem zawsze jest konto. Brak zmian oznacza brak wpisu.
- **Audyt wystąpienia:**
  - `ExpenseAudit.Created(expense, AuditAuthor.AutomatedJob, parent)` z rodzicem
    `(RecurringExpenseDefinition, definitionId)`; data biznesowa to termin;
  - późniejsze `Updated` i `Deleted` mają autora konta i tego samego rodzica.

## Invariant impact

| Invariant | How it is respected |
|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | Wystąpienie powstaje z `PaidOn = null` („Planowany”), więc jest kosztem, a nie wypływem. Żadna ścieżka SF-16 nie ustawia `PaidOn`. Wypływem staje się dopiero po „Oznacz jako opłacony” (SF-15). Przed terminem wystąpienie nie istnieje. Test: wygenerowane wystąpienie ma `PaidOn = null` i stan `Planned`. |
| 2. Pieniądze jako decimal w backendzie | `decimal(18,2)`, `decimal` w domenie, kwoty w API jako stringi bez zaokrągleń. Wystąpienie kopiuje kwotę. Terminy, liczby i stan liczy backend, a frontend tylko formatuje. |
| 3. Dostęp egzekwowany w API | Wszystkie trasy, w tym `preview`, wymagają sesji, a zapisy XSRF. Handlery sprawdzają istnienie definicji (`404`). `GET /api/jobs` wymaga sesji. Testy: `401` bez sesji dla każdej trasy i odrzucenie zapisu bez XSRF. |
| 4. Ponowienia nie duplikują | `PK_RecurringExpensePeriods` w bazie, wiersz w tej samej transakcji co wystąpienie, zachowany po usunięciu wydatku. Ponowienie, restart, nadrobienie i wyścig dają najwyżej jedno wystąpienie na okres, a usunięte nie wraca. Kolizja to wynik idempotentny. Dodanie definicji chroni klucz w `RecurringDefinitionRequests`, zachowany po usunięciu definicji. Model kwalifikacji nie zależy od kolejności zapisów, więc ponowiony zapis nie zmienia wyniku. |
| 5. Audyt finansowy | Każda zmiana definicji (także dzień, wstrzymanie, wznowienie) i każde utworzenie wystąpienia wywołuje `IAuditLog.RecordAsync` w tej samej `IUnitOfWork`. Wystąpienia mają autora „Zadanie automatyczne” i rodzica. Usunięcie definicji zostawia ślad `Deleted`. Test: błąd audytu wycofuje definicję razem z wystąpieniami. |
| 6. Sekrety po stronie serwera | Brak sekretów. Komunikat wyjątku zadania i logi zawierają tylko liczby i identyfikatory. UI nie pokazuje opisu błędu z historii przebiegów. |
| 7. Granice modułów i warstw | Wszystko w module Expenses i schemacie `expenses`. Ze Shared tylko kontrakty harmonogramu, audytu, jednostki pracy i daty uruchomienia. Zadanie, zakresy DI i blokada SQL są w Infrastructure. Reguły terminów to czysta domena. `StoneFlow.Architecture.Tests` bez wyjątków. |
| 8. Brak danych identyfikujących w dokumentach | Przykłady fikcyjne. |

## Risk

**high.**

- Zadanie bez udziału użytkownika zapisuje koszty trafiające do raportów.
- Błąd terminu, duplikat albo wystąpienie z czasu wstrzymania zawyża koszty, a ciche niepowodzenie
  je zaniża (dlatego jest ostrzeżenie).
- Ustawienie daty zapłaty zamieniłoby plan w wypływ.
- Usunięcie definicji jest nieodwracalne.
- Model terminów z historią dni i wyłączeniami to nowa logika na styku z harmonogramem SF-11.

Przed merge wymagany jest przegląd przez człowieka.

## UI

- **Trasy** (`expenses.routes.ts`; trasy `cykliczne` są przed `:id`; `data.section` = „Wydatki”):
  - `/wydatki` (parametr `wydatekCykliczny`);
  - `/wydatki/nowy` (parametr `cykliczny=1`);
  - `/wydatki/cykliczne` („Wydatki cykliczne”);
  - `/wydatki/cykliczne/:id` („Wydatek cykliczny”);
  - `/wydatki/:id` (SF-15).
- **Komponenty:**
  - `expense-form` w trybie jednorazowym i trybie definicji;
  - `recurring-expense-preview`;
  - `recurring-expenses-list-page` i `recurring-expense-details-page`;
  - `expenses-subnav`;
  - `recurring-job-notice` z `jobs-api.ts` i `recurringJobWarning`;
  - zmiany w `expenses-list-page` i `expense-details-page`.

  `app-data-table`, `app-field`, `app-button`, `app-confirm-dialog`, `app-status-badge`, stany i
  `shared/format` (`plMonth`, `plDateTime`) zostają bez zmian.
- **Główne akcje:**
  - „Dodaj wydatek cykliczny” na liście;
  - „Zapisz” albo „Zapisz zmiany” w formularzu;
  - „Edytuj” w szczegółach;
  - „Usuń” w stylu `danger`; „Wstrzymaj” i „Wznów” w stylu `secondary`.
- **Formularz definicji:**
  - pole wyboru „Powtarzaj co miesiąc” z `aria-expanded` i `aria-controls`;
  - „Dzień miesiąca” (`type="number"`, 1–31, `inputmode="numeric"`) z podpowiedzią: „Dla dni 29–31
    w krótszym miesiącu wystąpienie przypada na ostatni dzień miesiąca.”;
  - przy edycji dodatkowa podpowiedź: „Nowy dzień obowiązuje od bieżącego miesiąca.”;
  - „Początek” i „Koniec” (`type="date"`), osobne od „Data kosztu”, „Termin płatności” i „Data
    zapłaty”;
  - UI sprawdza wymagane pola, formaty, brutto > 0, netto ≥ 0 i koniec ≥ początek, a reguły terminów
    sprawdza API;
  - podgląd nie ma akcji zapłaty.
- **Stany:**
  - ładowanie, pusto, błąd z „Spróbuj ponownie”, nie znaleziono, konflikt, `503` z „Spróbuj
    ponownie”;
  - `busy` przy zapisach;
  - komunikaty w `aria-live`;
  - ostrzeżenie o zadaniu jest trwałe na stronie, a nie w toaście.
- **Dostępność (WCAG 2.2 AA):**
  - pełna obsługa klawiaturą;
  - dialogi przechwytują fokus i zwracają go do miejsca wywołania;
  - po rozwinięciu „Harmonogramu” fokus zostaje na polu wyboru, a sekcja jest zaraz za nim;
  - stan i oznaczenie „Cykliczny” są przekazane tekstem;
  - ostrzeżenie ma `role="status"` i nagłówek tekstowy;
  - na telefonie jedna kolumna, bez poziomego przewijania;
  - kolory wyłącznie z tokenów.

## Edge Cases

### Terminy i zakres

- **Dzień 31:** 31.01, 28.02.2027, 31.03, 30.04.
- **Dzień 29 w lutym:** 28.02.2027 i 29.02.2028.
- **Przełom roku:** grudzień → styczeń zmienia rok okresu.
- **Granice zakresu:**
  - początek 15.03, dzień 10: marzec się nie liczy, pierwszy termin to 10.04;
  - początek 10.03: marzec się liczy;
  - koniec 10.06, dzień 10: czerwiec się liczy;
  - koniec 09.06, dzień 10: czerwiec się nie liczy;
  - początek 31.01, dzień 31, koniec 28.02.2027: dwa wystąpienia.
- **Brak terminu w zakresie** (15.03–20.03, dzień 10): `400`.
- **Brak kolejnego terminu przed końcem** (dzisiaj 20.11, koniec 25.11, dzień 28): `LastDueDate` =
  28.10, więc stan „Zakończony”, „Wstrzymaj” daje `409`, a podgląd mówi, że do końca nie wypada już
  żaden termin.

### Dodanie

- **Dzisiejszy termin:** początek dzisiaj, dzień = dzisiejszy → jedno wystąpienie od razu. Dzień
  wcześniejszy → pierwsze w przyszłym miesiącu.
- **Początek w przyszłości:** `occurrencesToCreate = 0`, pierwsze wystąpienie tworzy zadanie.
- **Początek sprzed uruchomienia:** wystąpienia powstają, podgląd podaje `beforeLaunchCount`.
- **Początek 01.01.2000:** około 320 wystąpień wsadami w jednej transakcji.
- **Powtórzenie:** ta sama liczba `createdOccurrences`. Po usunięciu definicji: `409
  recurring_expense_deleted`. Inne dane z tym samym kluczem: `409 idempotency_key_reused`.

### Zadanie

- **Przerwa w działaniu:** uśpienie od 05.01 do 20.03, dzień 10 → pierwszy przebieg tworzy styczeń,
  luty i marzec, każdy ze swoim terminem.
- **Dwa przebiegi jednego dnia** → drugi nic nie tworzy.
- **Przebieg anulowany po pierwszej definicji** → kolejny przebieg tworzy tylko brakujące
  wystąpienia.
- **Definicja zakończona ponad 400 dni temu** nie jest kandydatem.

### Zmiana dnia (odpowiedź 1, reguła 2)

- **Dzisiaj 20.11, dzień 25, listopad bez wystąpienia:**
  - nowy dzień 5: termin 05.11 minął, więc listopad powstaje przy zapisie (podgląd: 1);
  - nowy dzień 28: listopad powstanie 28.11.
- **Listopad z wystąpieniem (dzień 10) → 25:** brak drugiego wystąpienia, grudzień 25.12.
- **v3-Critical1:** początek 20.11, dzień 10, tego samego dnia zmiana na 25. Wpis od listopada
  zastępuje wpis z dodania, więc listopad powstanie 25.11. Tak samo dla początku 15.12 i zmiany
  10 → 20: grudzień powstanie 20.12, bo zmiana usuwa wpis od grudnia z dodania.
- **Bieżący okres po późniejszym początku:** początek 15.11, dzień 10 (listopad się nie liczy),
  dzisiaj 20.11, zmiana na 18. 18.11 ≥ początku i termin minął, więc listopad powstaje przy zapisie.
- **Zaległy okres z przerwy:** październik nie powstał przez błąd zadania, a 20.11 dzień zmienia się
  z 10 na 15. Dzień października to nadal 10, więc październik powstaje z terminem 10.10. Listopad
  (10.11, nieutworzony) dostaje nowy dzień: 15.11 minął, więc powstaje z terminem 15.11.
- **v2, marzec przed początkiem:** początek 15.03, dzień 10, w listopadzie zmiana na 20. Dzień marca
  to nadal 10, a 10.03 < 15.03, więc marzec nie powstaje.
- **v2, sierpień po wznowieniu:** dzień 10, przedział [01.08, 15.08), w listopadzie zmiana na 20.
  Dzień sierpnia to nadal 10, więc 10.08 jest w przedziale i sierpień nie powstaje.
- **v3-Critical2, miesiąc końca:** koniec 25.06, dzień 28, dzisiaj 20.11, zmiana na 20. Dzień czerwca
  to nadal 28, a 28.06 > koniec, więc czerwiec nie powstaje. `LastDueDate` liczony z historii dni
  zostaje 28.05, a stan „Zakończony”.
- **v3-Critical2, potem przedłużenie do 31.12:** czerwiec (28.06) jest teraz w zakresie i nie ma
  wyłączenia, więc powstaje razem z lipcem–październikiem (dzień 28 według historii) i listopadem
  (20.11, nowy dzień). Zgadza się to z odpowiedzią 2.
- **v4-NC2, należny okres wypada z zakresu:** początek 05.11, dzień 10, listopad nie powstał przez
  błąd zadania, dzisiaj 20.11, zmiana na 3. 03.11 < początek, więc listopad przestaje się liczyć.
  Podgląd: „1 należne wystąpienie, które jeszcze nie powstało, przestaje się liczyć po tej zmianie.”
  Pierwsze wystąpienie: 03.12.
- **Nowy termin przyszłego okresu bieżącego poza zakresem:**
  - początek 05.11, dzień 25 → 3, dzisiaj 20.11: listopad się nie liczy, pierwsze wystąpienie
    03.12;
  - koniec 25.11, dzień 20 → 28, dzisiaj 10.11: listopad się nie liczy, stan „Zakończony”.
- **Zmiana dnia po wznowieniu w bieżącym okresie:** dzień 10, wstrzymanie 01.11, wznowienie 15.11,
  przedział [01.11, 15.11). 20.11 zmiana na 18: 18.11 jest poza przedziałem i w zakresie, więc
  listopad powstaje. Przedział dotyczy terminów, a nowy termin w nim nie leży.

### Przedłużenie i kolejność zapisów (odpowiedź 2)

- **Przedłużenie zakończonej definicji:** koniec 30.06, dzień 10, dzisiaj 20.11, nowy koniec „bez
  końca” → lipiec–listopad (5).
- **Ze wstrzymaniem:** dodatkowo przedział [01.05, 15.08) → lipiec (10.07) i sierpień (10.08) są
  wyłączone, więc powstaje wrzesień–listopad (3).
- **v4-Critical, „najpierw dzień, potem przedłużenie”:** koniec 25.06, dzień 28, dzisiaj 20.11.
  - Zmiana dnia na 20 tworzy wpis od listopada i nie tworzy niczego, bo czerwiec 28.06 > koniec.
  - 21.11 przedłużenie „bez końca” tworzy czerwiec–październik z terminami 28.06–28.10 (dzień 28
    według historii) i listopad 20.11.
  - Kolejność odwrotna („najpierw przedłużenie, potem dzień”) i zapis łączny dają te same okresy
    i terminy.
- **v4-NC1, koniec w bieżącym miesiącu i zmiana dnia** (koniec 30.06 → 15.11, dzień 10 → 25, dzisiaj
  20.11):
  - jeden zapis: listopad ma dzień 25, a 25.11 > 15.11, więc się nie liczy. Powstaje
    lipiec–październik;
  - dwa zapisy („najpierw przedłużenie, potem dzień”): przedłużenie tworzy listopad 10.11, a rejestr
    go utrwala. Powstaje lipiec–listopad;
  - kolejność odwrotna („najpierw dzień, potem przedłużenie”) daje to samo co jeden zapis.

  Różnica wynika tylko z tego, że zapis pośredni utworzył wystąpienie. Akceptujemy ją
  (Assumptions 11). Bez końca w bieżącym miesiącu różnić się może tylko termin listopada (10.11
  albo 25.11), a nie zbiór okresów.
- **Skrócenie końca poniżej utworzonych:** zapis jest dozwolony, a utworzone wystąpienia zostają.
  Podgląd podaje `createdAfterEndCount`.
- **Zmiana kwoty po utworzeniu:** utworzone wystąpienia zostają ze starą kwotą, a zaległe
  nieutworzone powstają z nową.
- **Usunięte wystąpienie** nie wraca przy przebiegu, zapisie, wznowieniu ani przedłużeniu.

### Zmiana początku (reguła 5, Assumptions 12)

- **Na wcześniejszy:** definicja od 01.06, dzisiaj 20.11, nowy początek 01.01 → przedział
  [01.01, 01.06), styczeń–maj nie powstają (podgląd: 0).
- **Na wcześniejszy, z częścią w przyszłości:** początek 15.12, dzisiaj 20.11, nowy początek 10.11,
  dzień 25 → przedział [10.11, 20.11). Listopad (25.11) leży poza przedziałem, więc powstanie
  25.11.
- **Na wcześniejszy, z terminem dzisiaj:** początek 01.12, dzisiaj 20.11, nowy początek 01.11,
  dzień 20 → przedział [01.11, 20.11). Termin 20.11 nie jest miniony, więc listopad powstaje przy
  zapisie (podgląd: 1), tak jak przy dodaniu definicji z początkiem 01.11.
- **Na wcześniejszy, z terminem wczoraj:** to samo przy dniu 19 → 19.11 leży w przedziale, więc
  listopad nie powstaje (podgląd: 0), a pierwsze wystąpienie to 19.12.
- **Na późniejszy:** utworzone wystąpienia sprzed nowego początku zostają, a podgląd podaje
  `createdBeforeStartCount`.
- **Na późniejszy z należnym nieutworzonym, potem z powrotem na wcześniej:** początek 01.10, dzień
  10, październik utworzony, listopad (10.11) nie powstał przez błąd zadania, dzisiaj 20.11.
  - Początek → 15.11: 10.11 < początek, więc listopad przestaje się liczyć (`droppedDueCount` = 1).
  - Potem początek → 01.10: przedział `StartMoved` [01.10, min(15.11, 20.11)) = [01.10, 15.11)
    obejmuje 10.11, więc listopad nie wraca (termin miniony, PRD). Pierwsze wystąpienie to 10.12.
- **Okres przed pierwszym wpisem dnia:** początek 15.03.2027 (wpis od marca 2027, dzień 10).
  20.11.2026 początek → 01.09.2026, potem zmiana dnia na 25 (wpis od listopada 2026, wpis z marca
  2027 usunięty). Wrzesień i październik 2026 dostają dzień 25, ale ich terminy leżą
  w [01.09.2026, 20.11.2026) (`StartMoved`), więc nie powstają. Listopad powstanie 25.11.
- **Nakładające się przedziały:** początek 01.01, dzień 10, wstrzymanie 01.03 i wznowienie 15.04
  (przedział `Pause` [01.03, 15.04)). Potem początek → 01.05, a dzisiaj 20.11 początek → 01.02, co
  daje `StartMoved` [01.02, 01.05). Przedziały się nakładają, a wyłączenie to ich suma. Luty,
  marzec i kwiecień nie powstają, jeśli nie powstały wcześniej. Utworzone wcześniej (styczeń,
  luty) zostają.
- **Edycja bez zmian** → `200` z tą samą wersją, bez audytu i generowania.

### Wstrzymanie i wznowienie (odpowiedź 3)

Dzień 10:

- wstrzymanie 01.11, wznowienie 15.11 → przedział [01.11, 15.11), listopad pusty, pierwsze
  wystąpienie 10.12;
- wznowienie 10.11 → przedział [01.11, 10.11), więc listopad (10.11) powstaje przy wznowieniu;
- wstrzymanie i wznowienie tego samego dnia → przedział pusty, nic nie przepada;
- wstrzymanie przy niezałożonym dzisiejszym terminie → najpierw powstaje wystąpienie, potem
  następuje wstrzymanie;
- wstrzymanie definicji zakończonej → `409`; wznowienie po końcu jest dozwolone i nic nie tworzy;
- **zmiana dnia na wstrzymanej (przykład recenzenta):** wstrzymanie 05.11 (10.11 jeszcze nie
  minął, więc listopad nie powstaje), 20.11 próba zmiany dnia na 3 → `409 recurring_expense_paused`,
  a w UI pole jest zablokowane. Po wznowieniu 20.11 dzień listopada to nadal 10, a 10.11 leży
  w [05.11, 20.11), więc listopad nie powstaje przy wznowieniu. Bez dalszych zmian pierwsze
  wystąpienie to 10.12;
- **wznowienie, potem zmiana dnia (NC2):** ten sam przykład, a po wznowieniu 20.11 zmiana dnia
  na 3. Wpis od listopada daje termin 03.11, który nie leży w [05.11, 20.11), mieści się w zakresie
  i minął, więc listopad powstaje przy zapisie z terminem 03.11. To zgodne z odpowiedzią 1:
  bieżący okres bez wystąpienia z minionym nowym terminem powstaje od razu. Odpowiedź 3 dotyczy
  chwili wznowienia;
- **skrócenie → przedłużenie → wznowienie (NC4):** dzień 10, koniec 31.12, wstrzymanie 05.08
  (lipiec utworzony, sierpień jeszcze nie). Podczas wstrzymania koniec skrócony do 31.07, a potem
  przedłużony bez końca, wznowienie 20.11. Okresy do lipca były utworzone przy wstrzymaniu. Terminy
  10.08–10.11 leżą w [05.08, 20.11), więc nie powstają, a pierwsze wystąpienie to 10.12;
- **edycja innych pól wstrzymanej:** zmiana kwoty i przedłużenie końca → zapis z
  `createdOccurrences: 0`, podgląd 0, bez wystąpień do wznowienia. Po wznowieniu okresy
  z terminem po dniu wznowienia powstają z nową kwotą, a terminy z [PausedOn, dzień wznowienia)
  nie powstają;
- **kilka wstrzymań, potem przedłużenie:** koniec 31.07, dzień 10, wstrzymania [01.03, 15.04)
  i [01.06, 15.07), dzisiaj 20.11. Przedłużenie bez końca tworzy sierpień–listopad (4). Marzec,
  kwiecień, czerwiec i lipiec są wyłączone, a maj powstał wcześniej;
- wstrzymanie w dniu terminu po utworzeniu wystąpienia i wznowienie tego samego dnia →
  `resumePreview.occurrencesToCreate = 0`, a dialog nie obiecuje wystąpienia.

### Współbieżność i stan zadania

- **Edycja a zadanie nocne:** formularz otwarty przed 00:10 zapisuje się bez konfliktu.
- **Usunięcie definicji w trakcie przebiegu** → „brak definicji”. Utworzone wcześniej wystąpienia
  zostają.
- **Usunięcie i dodanie nowej definicji z tymi samymi danymi** → nowa tworzy swoje wystąpienia
  (Out of scope).
- **Filtr po usuniętej definicji** → „Wydatek cykliczny (usunięty)”.
- **Ostrzeżenie o zadaniu:**
  - `failed` → ostrzeżenie;
  - sukces sprzed 26 h → ostrzeżenie;
  - sukces sprzed 2 h → brak ostrzeżenia;
  - brak przebiegów → brak ostrzeżenia;
  - pierwszy `running` bez sukcesu → nic;
  - błąd odczytu → stan ostrożny.
- **Granica doby:** o 23:30 UTC zimą w Warszawie jest już następny dzień (`WarsawTime`, test).

## Dependencies

- **Zrobione:**
  - SF-11 (harmonogram, `GET /api/jobs`);
  - SF-15 (model `Expense`, `ExpenseAudit` z rodzicem, kontrakt dla SF-16).
- **Istnieją:** SF-7 (audyt, `AuditAuthor.AutomatedJob`, `AuditObjectType.RecurringExpenseDefinition`)
  i `ILaunchDate`.
- **D-08 pkt 2** nie blokuje implementacji. Zostaje krokiem ręcznym po wdrożeniu.
- **Konsumenci:** SF-19 (koszty bez wystąpień sprzed uruchomienia, może osadzić
  `recurring-job-notice`), SF-18 (wpisy z rodzicem), SF-20 (wypływy z `PaidOn`).

## Assumptions

Odpowiedzi właściciela (08.11.2026):

1. **Zmiana dnia miesiąca na termin, który już minął.**
   - Odpowiedź: okres, który ma już wystąpienie, nie dostaje drugiego, a nowy dzień obowiązuje od
     następnego okresu. Jeśli bieżący okres nie ma jeszcze wystąpienia, a nowy termin już minął,
     wystąpienie bieżącego okresu powstaje od razu.
   - Realizacja:
     - rejestr (okres z wystąpieniem nie dostaje drugiego);
     - historia dni z wpisem od bieżącego okresu;
     - generowanie przy zapisie (bieżący okres bez wystąpienia z minionym nowym terminem powstaje od
       razu).
2. **Przedłużenie zakończonej definicji.**
   - Odpowiedź: przedłużenie tworzy zaległe wystąpienia za okresy między dawnym końcem a dzisiaj,
     a formularz pokazuje wcześniej ich liczbę. Okresy z czasu wstrzymania nie powstają.
   - Realizacja:
     - zmiana końca nie tworzy wyłączeń, więc te okresy się kwalifikują i powstają przy zapisie;
     - przedziały `Pause` je odcinają;
     - wynik nie zależy od kolejności względem zmiany dnia.
3. **Wznowienie po minionym terminie bieżącego okresu.**
   - Odpowiedź: brak wystąpienia za bieżący okres (termin przypadł w czasie wstrzymania), pierwsze
     powstaje w następnym okresie.
   - Realizacja: przedział `[PausedOn, dzień wznowienia)`. Termin w dniu wznowienia się liczy.

Decyzje autora specyfikacji, każda z uzasadnieniem:

4. **Model ze stanu, a nie z kolejności** (decyzja po przeglądzie v4). Pojedyncza granica
   `OccurrencesFrom` z v1–v4 zależała od kolejności zapisów: „najpierw dzień, potem przedłużenie”
   gubiło lipiec–październik. Historia dni i przedziały wyłączeń zapamiętują dokładnie dwie rzeczy:
   - jaki dzień obowiązywał w okresie;
   - które terminy świadomie wyłączono: wstrzymanie i przesunięcie początku na wcześniej.

   Kwalifikacja to czysta funkcja tego stanu. Idempotencję daje wyłącznie rejestr z kluczem w bazie
   (niezmiennik 4). Kursor „przetworzono do okresu” nie oddaje luk (usunięte, przedłużenie, przerwa).
   Koszt to najwyżej 12 okresów na rok historii i stała liczba odczytów.
5. **Wstrzymanie a przerwa w działaniu.**
   - Wstrzymanie zapisuje `PausedOn`, a wznowienie przedział. Przerwa w działaniu niczego nie
     zapisuje, więc zaległe powstają (PRD §5).
   - Wstrzymanie najpierw tworzy terminy należne do dzisiaj włącznie. Inaczej dzisiejszy termin,
     jeszcze niezałożony, wpadłby w przedział wstrzymania.
   - Wstrzymanie definicji zakończonej jest odrzucane, bo nie ma skutku (nie wypada żaden kolejny
     termin). Model przedziałów pozwoliłby je technicznie, ale akcja bez skutku tylko myli.
6. **Tworzenie przy zapisie w tej samej jednostce pracy, bez limitu liczby.**
   - PRD wymaga „od razu”, a jedna transakcja daje atomowość definicji, wystąpień i audytu.
   - Bezpiecznikiem jest zakres dat 2000–2099 (najwyżej około 1200 okresów) i zapis wsadami po 100.
     Test wydajności obejmuje definicję od 2000 r.
   - Literówkę w roku pokazuje podgląd.
7. **Wystąpienia sprzed uruchomienia powstają** (PRD §5). Z raportów wyklucza je SF-19/SF-20 po
   dacie kosztu. Podgląd podaje ich liczbę, bo frontend nie zna daty uruchomienia.
8. **`CostDate = DueDate = termin`.** PRD wymaga daty kosztu równej terminowi. Termin płatności
   równy terminowi daje „Po terminie” od następnego dnia (SF-15). `DocumentReference` jest pusty
   (PRD §7).
9. **Autor „Zadanie automatyczne” dla każdego utworzenia wystąpienia,** także przy zapisie przez
   użytkownika (PRD §3, zlecenie etapu). Definicję audyt przypisuje kontu.
10. **Usunięcie definicji jest fizyczne, a wystąpienia zostają zwykłymi wydatkami.**
    - Wystąpienia to zarejestrowane, często opłacone koszty.
    - `RecurringDefinitionId` jest bez FK, bo wiąże audyt z rodzicem.
    - Dni, wyłączenia i rejestr usuwa kaskada (nowa definicja ma nowe id).
    - Klucze idempotencji zostają.
11. **Zmiana dnia obowiązuje od bieżącego okresu** (decyzja po przeglądzie v4).
    - Odpowiedź 1 mówi o okresie bieżącym. Okresy wcześniejsze zachowują dotychczasowy dzień, więc
       zmiana dnia ich nie przelicza:
       - nie wciąga okresów, które się nie liczyły (marzec przed początkiem, sierpień w przedziale,
         czerwiec po końcu);
       - zaległe okresy zachowują swój termin. Dostają natomiast nowe wartości pozostałych pól, bo
         zmiana dotyczy wystąpień nieutworzonych, w tym zaległych (PRD §5).
    - Okres bieżący i przyszłe liczą się według nowego dnia i reguły [początek, koniec]. Należny,
      nieutworzony okres bieżący, którego nowy termin wypada poza zakres, odpada, a podgląd pokazuje
      to przed zapisem (v4-NC2).
    - Kolejność zapisów nie zmienia wyniku. Wyjątek: zapis pośredni utworzył już wystąpienie, które
      rejestr utrwala (v4-NC1). Taką różnicę terminu albo obecności okresu bieżącego akceptujemy,
      bo utworzonego wystąpienia zmiana definicji nie rusza.
    - Historii dni nie pokazuje UI, a jej skutki widać w podglądzie. Historia wersji treści
      definicji pozostaje poza zakresem.
12. **Przesunięcie początku na wcześniej wyłącza tylko terminy minione** (`ToDate` = min(stary
    początek, dzisiaj); decyzja koordynatora po v5). PRD wyklucza tylko minione wystąpienia, więc
    dzisiejszy termin się liczy i powstaje przy zapisie. To spójne z zapisem nowej definicji, który
    tworzy terminy „od początku do dzisiaj, włącznie z dzisiejszym”. Terminy przyszłe w dodanym
    zakresie się liczą.
13. **Serializacja blokadą wiersza definicji** (`UPDLOCK, ROWLOCK` jak `OrderLock`, READ COMMITTED),
    na początku każdego przypadku użycia.
    - Zadanie nie zmienia wersji definicji.
    - Ponowienie po kolizji albo zakleszczeniu jest tylko w zadaniu, w nowym zakresie DI.
    - Użytkownik dostaje `503` i ponawia sam.
    - Wyjątek wycofania po zakleszczeniu nie przykrywa wyjątku oryginalnego.
14. **Zadanie:**
    - klucz `recurring-expenses`, uruchamiane o 00:10 Europe/Warsaw, `MaxDuration` 30 min;
    - pętla i zakresy DI w Infrastructure;
    - błędy zbierane i zgłaszane na końcu przebiegu.

    Kandydaci pomijają definicje wstrzymane i zakończone ponad 400 dni temu. Po końcu zostają tylko
    okresy z przerwy działania, a przerwa ponad 400 dni wykracza poza RPO/RTO (D-08). Przedłużenie
    tworzy zaległe synchronicznie przy zapisie. Bez „Uruchom teraz” w UI.
15. **`LastDueDate` jest przechowywane, a nie liczone w locie.** Filtr stanu w SQL i stan
    „Zakończony” potrzebują ostatniego terminu dla wielu definicji naraz. Liczenie z historii dni
    w SQL byłoby złożone i łatwe do rozjechania z domeną. Domena przelicza kolumnę przy każdym
    zapisie zmieniającym początek, koniec albo dni. Test zgodności sprawdza kolumnę z funkcją
    domeny.
    - Stan „Zakończony” oznacza brak kolejnego terminu i ma pierwszeństwo przed „Wstrzymany”.
    - Bieżący `DayOfMonth` na definicji też jest przechowywany (dla listy i audytu), a domena
      utrzymuje go równym ostatniemu wpisowi historii.
    - `LastDueDate` i walidacja zakresu liczą termin efektywny (rejestr dla okresów
      z wystąpieniem). Zmiana dnia, po której jedyny okres w zakresie ma już wystąpienie (05.11–25.11,
      dzień 10 → 28, listopad utworzony 10.11), przechodzi: `LastDueDate` = 10.11 i stan
      „Zakończony”. Komunikat „nie wypada żaden termin” byłby tu nieprawdziwy.
16. **Walidacja dodatkowa:**
    - zakres dat 2000–2099;
    - netto ≥ 0;
    - odrzucenie definicji bez żadnego terminu w zakresie.
17. **UI:**
    - „Powtarzaj co miesiąc” tylko w „Dodaj wydatek”;
    - edycja w szczegółach definicji;
    - podsekcja `/wydatki/cykliczne` bez nowej pozycji głównej nawigacji (design guide §3, §6).
18. **Podgląd jako `GET`, liczony przez domenę na kopii stanu.** Frontend nie powtarza reguł.
    Żądania wynikają z wpisywania, nie z odświeżania w tle.
19. **Filtr listy wydatków po definicji przez parametr adresu,** przenoszony raz do serwisu stanu
    (SF-15, Assumptions 19).
20. **Idempotencja dodania w `RecurringDefinitionRequests`,** zachowanej po usunięciu definicji.
    Najpierw porównuje się skrót żądania:
    - inne dane dają `409 idempotency_key_reused`, zawsze;
    - te same dane i usunięta definicja dają `409 recurring_expense_deleted`, więc minione koszty nie
      powstają drugi raz;
    - powtórzenie zwraca zapisane `createdOccurrences`.

    Przedrostek `recurring_expense_` mają wszystkie kody zadania.
21. **E2E odroczone do SF-21,** jak w SF-15. Zastępują je testy integracyjne na SQL Server i testy
    komponentów.
22. **Ryzyko:** bez zmian (`high`).
23. **Ostrzeżenie o niepowodzeniu zadania** opiera się na istniejącym `GET /api/jobs`.
    - Pokazuje się przy `failed`/`abandoned` albo przy sukcesie starszym niż 25 h (próg jak
      „Dane niekompletne” w INT-01).
    - Odczyt następuje tylko przy wejściu na stronę (DOST-01). Regułę liczy czysta funkcja z testami.
    - Ostrzeżenia nie ma, gdy nie było żadnego przebiegu albo trwa pierwszy przebieg (`running`
      bez sukcesu).
24. **Aktualizacja PRD §5** w ramach zadania, tekstem z „Behavior → PRD”. Odpowiedzi 1–3 są
    przypisane tylko do zdań z ich treścią.
25. **`nextDueDate` to najbliższy nieutworzony kwalifikujący się termin,** także należny
    i ≤ `LastDueDate`. „Brak” oznacza, że nie kwalifikuje się żaden okres. Podgląd zapisu
    i `resumePreview` podają termin po zapisie.
26. **Skrócenie końca albo późniejszy początek przy istniejących wystąpieniach są dozwolone.**
    Wystąpienia zostają, a podgląd podaje ich liczbę.
27. **Kolizja klucza rejestru to wynik idempotentny.**
    - Zadanie ponawia raz w nowym zakresie.
    - Użytkownik dostaje `503 recurring_expense_busy`.
    - Przy dodaniu kolizja jest niemożliwa.
28. **Tekst dialogu „Wznów” wynika z `resumePreview`,** czyli z backendu, który zna rejestr.
29. **Zwracana `version` to wersja po zapisie definicji.** Generowanie jej nie zmienia, więc kolejny
    zapis z tą wersją przechodzi.
30. **Wstrzymana definicja nie tworzy wystąpień, a jej dnia nie można zmienić** (decyzja po
    przeglądzie v5).
    - Zmiana dnia w miesiącu wstrzymania mogłaby dać termin sprzed `PausedOn`, którego nie wyłącza
      żaden przedział. Wtedy albo edycja tworzyłaby wystąpienie w czasie wstrzymania, albo
      wznowienie tworzyłoby okres, którego podgląd nie pokazał. Blokada zmiany dnia (`409`, pole
      zablokowane) i warunek „niewstrzymana” w regule 4 usuwają obie ścieżki.
    - Pozostałe zmiany wstrzymanej definicji są dozwolone, bo nie tworzą okresu z terminem
      < `PausedOn` bez wystąpienia (NC4):
      - wstrzymanie najpierw tworzy wszystkie okresy należne do `PausedOn` włącznie przy ówczesnym
        zakresie;
      - podczas wstrzymania dzień się nie zmienia, więc terminy okresów są stałe;
      - skrócenie, a potem przedłużenie końca dotyczy tylko okresów, które przy wstrzymaniu albo
        powstały (termin ≤ `PausedOn` w ówczesnym zakresie), albo mają termin > ówczesnego
        `LastDueDate` > `PausedOn`, czyli w przedziale wstrzymania;
      - przesunięcie początku na wcześniej przykrywa terminy minione przedziałem `StartMoved`.

      Skutki naliczają się przy wznowieniu, a test „skrócenie → przedłużenie → wznowienie” to
      sprawdza.
    - Komunikat mówi „wydatek cykliczny”, jak cały UI, zamiast „definicja”.

## Acceptance Criteria

- [ ] Ponowienie przebiegu, restart i dwa przebiegi jednego dnia nie duplikują wystąpień.
      Wstawienie drugiej pary (definicja, okres) odrzuca baza. Kolizja w generowaniu wycofuje
      jednostkę bez duplikatu: zadanie ponawia, a użytkownik dostaje `503 recurring_expense_busy`.
- [ ] Dzień 29–31 przypada w krótszym miesiącu na ostatni dzień, a grudzień → styczeń zmienia rok.
- [ ] Przed terminem wystąpienie nie istnieje. Powstaje w dniu terminu przy pierwszym przebiegu albo
      przy zapisie.
- [ ] Wystąpienie ma `PaidOn = null`, stan „Planowany”, a data kosztu i termin są równe terminowi.
      Przed „Oznacz jako opłacony” nie zwiększa wypływów.
- [ ] Okres liczy się tylko przy terminie w [początek, koniec] poza przedziałami wyłączeń.
- [ ] Zapis nowej definicji tworzy w tej samej transakcji wystąpienia za wszystkie terminy od
      początku do dzisiaj włącznie, także od 2000 r. Formularz pokazuje wcześniej ich liczbę
      (w tym sprzed uruchomienia).
- [ ] Po przerwie w działaniu pierwszy przebieg tworzy wszystkie zaległe wystąpienia.
- [ ] Wstrzymana definicja nie tworzy żadnych wystąpień (edycja, zadanie, podgląd 0). Zmiana dnia
      na wstrzymanej daje `409 recurring_expense_paused`, a pole dnia jest zablokowane. Wznowienie
      zapisuje przedział [wstrzymanie, wznowienie), a terminy z niego nie powstają, także przy
      późniejszym przedłużeniu albo zmianie dnia. Termin w dniu wznowienia powstaje. Dialogi wyjaśniają, że okresy z czasu wstrzymania
      pozostają puste.
- [ ] Zmiana definicji (także kwoty) nie zmienia utworzonych wystąpień. Zaległe nieutworzone
      powstają z nowymi wartościami pól.
- [ ] Zmiana dnia obowiązuje od bieżącego okresu (Assumptions 1, 11):
      - okres z wystąpieniem nie dostaje drugiego;
      - bieżący bez wystąpienia z minionym nowym terminem powstaje od razu;
      - okresy wcześniejsze zachowują dotychczasowy dzień, więc marzec przed początkiem, sierpień
        po wznowieniu i czerwiec po końcu nie powstają, a zaległe zachowują termin;
      - definicja z 20.11 poprawiona tego dnia z 10 na 25 tworzy listopad 25.11;
      - należny okres, którego nowy termin wypada poza zakres, odpada, co podgląd pokazuje
        wcześniej.
- [ ] Przedłużenie tworzy zaległe okresy między dawnym końcem a dzisiaj, poza przedziałami
      wstrzymania (Assumptions 2). Kolejności „dzień, potem przedłużenie”, „przedłużenie, potem
      dzień” i zapis łączny dają ten sam wynik, z wyjątkiem opisanym w v4-NC1 (Edge Cases).
      Po przedłużeniu czerwiec z terminem 28.06 przy dawnym końcu 25.06 powstaje.
- [ ] Przesunięcie początku na wcześniej nie tworzy wystąpień za terminy sprzed dnia zmiany.
      Termin w dniu zmiany powstaje przy zapisie.
- [ ] Usunięte wystąpienie nie wraca.
- [ ] Usunięcie definicji wymaga potwierdzenia, zostawia wystąpienia i ślad `Deleted`. Powtórzenie
      dodania po usunięciu daje `409 recurring_expense_deleted`.
- [ ] Audyt:
      - definicja: `Created`, `Updated` (także `dayOfMonth` i `pausedOn`) i `Deleted` z autorem
        konta i datą biznesową równą początkowi;
      - wystąpienie: autor „Zadanie automatyczne” z rodzicem;
      - zmiany wystąpienia: autor konta i ten sam rodzic;
      - błąd audytu wycofuje całość.
- [ ] Błąd jednej definicji nie blokuje innych. Przebieg z błędami kończy się `failed` i jest
      ponawiany. Po anulowaniu nic nie jest zapisywane.
- [ ] Przy `failed`/`abandoned` albo sukcesie starszym niż 25 h lista wydatków i lista wydatków
      cyklicznych pokazują „Automatyczne tworzenie wydatków cyklicznych nie powiodło się (…). Koszty
      mogą być zaniżone.” z czasem ostatniego sukcesu. Odczyt jest tylko przy wejściu na stronę.
- [ ] Lista wydatków oznacza wystąpienia tekstem „Cykliczny” i filtruje je po definicji. Szczegóły
      wystąpienia prowadzą do definicji.
- [ ] Kwota definicji ≤ 0 jest odrzucana w API i w formularzu.
- [ ] Bez sesji każda trasa `/api/recurring-expenses` zwraca `401`, a zapis bez XSRF jest odrzucany.
- [ ] Filtr `state` w SQL zgadza się z funkcją domeny. `LastDueDate` i `DayOfMonth` zgadzają się
      z historią dni.
- [ ] Migracja tylko dodaje. Endpointy SF-15 działają ze starym frontendem.
- [ ] PRD §5 zawiera tekst z „Behavior → PRD”.

## Verification

- build: `dotnet restore stone-flow.slnx --locked-mode`, `dotnet format stone-flow.slnx
  --verify-no-changes --no-restore`, `dotnet build stone-flow.slnx -c Release --no-restore`;
  w `app/`: `npm ci`, `npm run format:check`, `npm run lint`, `npm run typecheck`, `npm run build`.
- tests (`dotnet test --solution stone-flow.slnx -c Release --no-build`):
  - `StoneFlow.Expenses.Tests` (domena, czyste funkcje `RecurringSchedule` i operacje encji):
    - termin okresu dla dni 1, 28–31 we wszystkich miesiącach 2027 i w lutym 2028, przełom roku;
    - dzień okresu z historii: wpis od początku, zmiana w bieżącym okresie (dodanie i zastąpienie),
      usunięcie późniejszych wpisów przy początku w przyszłości, okres przed pierwszym wpisem
      (także wariant 15.03.2027 → 01.09.2026 → dzień 25: wrzesień i październik w `StartMoved`, bez
      wystąpień);
    - wstrzymana definicja: żaden okres się nie kwalifikuje (generator 0, `nextDueDate` brak, podgląd
      0), zmiana dnia odrzucona przez domenę, przykład recenzenta (wstrzymanie 05.11, próba dnia 3 →
      odrzucona, po wznowieniu 20.11 listopad nie powstaje), edycja kwoty i końca wstrzymanej,
      a skutki po wznowieniu;
    - termin efektywny: walidacja i `LastDueDate` przy okresie z wystąpieniem (05.11–25.11, dzień
      10 → 28, zapis przechodzi, stan „Zakończony”);
    - przedziały: kilka wstrzymań i przedłużenie, nakładające się `Pause` i `StartMoved`, początek
      później → wcześniej z należnym nieutworzonym, `droppedDueCount` przy późniejszym początku;
    - kwalifikacja: granice [początek, koniec], przedziały `[From, To)` (dzień `From` wyłączony,
      `To` nie), definicja wstrzymana (nic się nie kwalifikuje), rejestr z usuniętym okresem, dzisiaj włącznie;
    - przedziały: wznowienie (pusty przy tym samym dniu), przesunięcie początku na wcześniej (część
      minione i część przyszła, `ToDate` = dzisiaj, termin dzisiaj się liczy, a wczoraj nie),
      brak przedziału przy przesunięciu na później;
    - każdy skutek z reguły 8 osobnym testem:
      - odpowiedzi 1, 2 i 3;
      - v2 marzec i v2 sierpień;
      - v3-Critical1 (z wariantem początku w przyszłości);
      - v3-Critical2 bez przedłużenia i z późniejszym przedłużeniem;
      - v4-Critical (obie kolejności i zapis łączny dają te same okresy i terminy);
      - v4-NC1 (zapis łączny, dwa zapisy w obu kolejnościach, opisana różnica);
      - v4-NC2 (`droppedDueCount` = 1);
    - zaległy okres z przerwy przy zmianie dnia (październik 10.10, listopad 15.11);
    - terminy pochodne:
      - `nextDueDate`, także należny i ≤ `LastDueDate`;
      - następny termin po zapisie;
      - `LastDueDate` z historii dni;
    - stan i jego pierwszeństwo;
    - niezmiennik `DayOfMonth` = ostatni wpis;
    - brak terminu w zakresie;
    - walidacja `RecurringExpenseInput`, skrót idempotencji, audyt definicji (pola, `payer` jako dane
      osobowe, `dayOfMonth` i `pausedOn` w `Updated`, etykieta, data biznesowa);
  - `StoneFlow.Api.IntegrationTests` (SQL Server, `FakeTimeProvider`, zadanie przez `IJobRunner`):
    - CRUD, wstrzymanie i wznowienie z kodami HTTP i kolejnością `409`;
    - kolejność `PUT`: błąd formatu pola → `400` przed odczytem; wstrzymana z innym dniem i pustym
      zakresem harmonogramu → `409 recurring_expense_paused` (nie `400`);
    - wznowienie, a potem zmiana dnia na 3 → listopad 03.11 (NC2); skrócenie → przedłużenie →
      wznowienie → brak wystąpień sierpień–listopad (NC4);
    - `PUT` ze zmienionym dniem na wstrzymanej → `409 recurring_expense_paused` bez zapisu; `PUT`
      z innymi polami na wstrzymanej → `200`, `createdOccurrences: 0`; po wznowieniu przykład
      recenzenta przez HTTP;
    - `Reason` zapisany jako `Pause`/`StartMoved` (`CHECK` odrzuca inne);
    - zwracana `version` działa w kolejnym `PUT`;
    - dodanie z minionym początkiem (liczba = podgląd, jedna transakcja, audyty);
    - idempotencja dodania: wyścig, powtórzenie z zapisaną liczbą, powtórzenie po usunięciu, inne
      dane z tym samym kluczem po usunięciu (`idempotency_key_reused`);
    - zadanie:
      - termin dzisiaj;
      - dwa przebiegi;
      - przeskok o 3 miesiące;
      - anulowanie po pierwszej definicji i ponowienie;
      - odcięcie 400 dni;
      - błąd jednej definicji (podmieniony handler komendy);
      - po anulowaniu brak wierszy;
    - przez HTTP: odpowiedzi 1–3, v3-Critical1, v3-Critical2 z późniejszym przedłużeniem,
      v4-Critical „najpierw dzień, potem przedłużenie” (lipiec–październik powstają), v4-NC1, v4-NC2
      (podgląd i zapis), przesunięcie początku na wcześniej (termin dzisiaj powstaje, wczoraj nie). Liczba w podglądzie równa się liczbie
      utworzonych;
    - zapis dni i wyłączeń, kaskada przy usunięciu definicji (dni, wyłączenia, rejestr; wiersz
      żądania zostaje; wystąpienia zostają);
    - usunięte wystąpienie nie jest odtwarzane;
    - rodzic w audycie przy edycji i akcjach zapłaty wystąpienia;
    - współbieżność:
      - dwie równoległe jednostki generowania dają jeden komplet;
      - zmiana kwoty równoległa z przebiegiem;
      - kolizja rejestru: zadanie po ponowieniu bez błędu, edycja z `503 recurring_expense_busy` bez
        zapisu;
      - zakleszczenie z prawdziwą blokadą: `503` przez fallback, wycofanie bez przykrycia wyjątku,
        jedno ponowienie w zadaniu. Gdy wymuszenie jest niestabilne, wyjątek 1205 z portu na
        poziomie store;
    - tłumaczenia `2601`/`2627`/`547`;
    - `CHECK` wszystkich tabel, w tym oba niespójne warianty `LastDueDate`, `ToDate > FromDate`
      i `DAY(FromPeriod) = 1`;
    - zgodność filtra `state`;
    - wydajność:
      - 500 definicji z historią 24 miesięcy, w tym 100 z brakującym okresem → dokładnie 100
        wystąpień;
      - faza odczytu ze stałą liczbą zapytań;
      - przebieg w czasie < 60 s;
      - dodanie od 01.01.2000 w czasie < 30 s;
    - `EndpointSecurityTests`: brak wpisów anonimowych, `401`, XSRF;
  - `StoneFlow.Architecture.Tests` (Application bez `IServiceScopeFactory`);
  - E2E: odroczone do SF-21;
  - `npm test -- --watch=false`:
    - model formularza i `recurring-expenses-api`, `jobs-api`, serwis;
    - podgląd: teksty, odmiany liczebników, `droppedDueCount`, liczby spoza zakresu, błąd;
    - `recurringJobWarning`: `failed`, `abandoned`, 24 h 59 min i 25 h 1 min, pierwszy `failed` bez
      sukcesu, pierwszy `running` bez sukcesu, brak przebiegów, brak pozycji;
    - `recurring-job-notice`: treść, stan ostrożny, brak żądań poza wejściem;
    - formularz edycji wstrzymanej: pole dnia `disabled` z opisem powiązanym `aria-describedby`,
      informacja „Zmiany zadziałają od wznowienia”, a żądanie `PUT` zawiera `dayOfMonth` mimo
      zablokowanego pola (NC6); tekst podglądu trybu wstrzymanego (NC1);
    - strony listy i szczegółów: stany, dialogi z treścią, w tym „Wznów” z `occurrencesToCreate`
      0 i 1, należny najbliższy termin, konflikt, `503`, `busy`;
    - oznaczenie „Cykliczny”, filtr z parametru, informacja o wystąpieniu i zdanie w dialogu
      usunięcia.
- manual:
  - na kopii testowej:
    - definicja z początkiem 3 miesiące wstecz;
    - wstrzymanie i wznowienie;
    - zmiana dnia, a potem przedłużenie;
    - ostrzeżenie po wymuszonym błędzie zadania;
  - `GET /api/jobs` pokazuje `recurring-expenses` (`daily 00:10 Europe/Warsaw`) z udanym przebiegiem;
  - dowód D-08 pkt 2 po wdrożeniu;
  - przegląd przez człowieka przed merge (high).
