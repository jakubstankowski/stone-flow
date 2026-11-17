# Spec review — SF-7

## Summary

- Overall assessment: PASS WITH ISSUES
- Justification: Poprawiona specyfikacja usuwa oba błędy krytyczne z v1. Opisuje mechanizm
  dołączania kontekstów do transakcji: interceptor poleceń `CommandInitialized`, interceptor
  zapisu z `UseTransaction` i odłączenie po zakończeniu. Działa to dla kontekstów utworzonych
  przed transakcją i w jej trakcie, a Shared nie musi znać typów modułów. Do modelu wpisu dodano
  obiekt nadrzędny z regułami per rodzaj i indeksem. Prawie wszystkie uwagi niekrytyczne
  rozwiązano albo zapisano świadomie. Rewizja wprowadza jednak trzy nowe ryzyka, które warto
  domknąć w planie albo w aktualizacji konwencji: domyślne `commitWhen = null` zatwierdza
  wynik-porażkę, `PersistSecurityInfo=true` działa w całym runtime, choć potrzebują go tylko
  migracje, a wspólne połączenie wyklucza równoległe zapytania w zakresie. Niezmienniki nie są
  naruszone.

## Findings

### Critical Issues

None.

Status uwag krytycznych z v1:

1. **Dołączanie kontekstów do transakcji** — rozwiązane (Context — „Dołączanie do transakcji”;
   Edge Cases; Acceptance Criteria). Specyfikacja ustala, że:
   - każde polecenie EF dostaje transakcję w `CommandInitialized`. Zdarzenie następuje po
     ustawieniu `DbCommand.Transaction` przez EF, więc przypisanie zadziała dla zapytań,
     `ExecuteUpdate`/`ExecuteDelete` i poleceń `SaveChanges`;
   - przed `SaveChanges` interceptor zapisu wywołuje `UseTransaction`, więc EF nie otwiera
     transakcji równoległej. `SavingChanges` następuje przed decyzją EF o własnej transakcji;
   - po zatwierdzeniu albo wycofaniu konteksty dostają `UseTransaction(null)`;
   - kontekst trafia do interceptora przez zdarzenia EF, więc reguła „Shared nie zależy od
     modułów” jest zachowana.

   Jest też kryterium akceptacji z dwoma kontekstami (utworzonym przed transakcją i w jej
   trakcie), które wykonują zapytanie, `ExecuteUpdate` i `SaveChanges`, oraz test na SQL Server
   z uwierzytelnianiem SQL. Kod na dysku potwierdza wykonalność podejścia.
2. **Odniesienie do zamówienia nadrzędnego** — rozwiązane (Behavior, punkt 6; Data / API —
   Storage; Types crossing a boundary). Dodano `AuditObjectReference? Parent`, kolumny
   `ParentObjectType`/`ParentObjectId` z check constraintem „oba albo żaden”, indeks i tabelę
   reguł: `Payment`, `Refund` i `SalesCorrection` wymagają `Order`, a wystąpienie wydatku wskazuje
   definicję cykliczną. Brak wymaganego rodzica jest odrzucany (Edge Cases, Acceptance
   Criteria). Wpisy dotyczące zamówienia znajdzie się po `(ObjectType, ObjectId)` oraz
   `(ParentObjectType, ParentObjectId)`. PRD sekcja 5 nie przewiduje wpłat bez zamówienia, więc
   reguła jest zgodna z dokumentacją.

### Non-Critical Issues

Nowe w tej rewizji:

1. **Domyślne `commitWhen = null` zatwierdza wynik-porażkę** (Types crossing a boundary —
   `IUnitOfWork`; Behavior, punkt 1.5). Semantyka jest jednoznaczna, ale wartość domyślna jest
   niebezpieczna. Gdy w otwartej transakcji `SaveChanges` zawiedzie, EF wycofuje zmiany tylko do
   automatycznego punktu zapisu. Jeżeli handler złapie `DbUpdateConcurrencyException` albo błąd
   2601/2627, przetłumaczy go na wynik `Conflict`, jak każą konwencje, i nie poda `commitWhen`,
   jednostka pracy zatwierdzi wcześniejsze zapisy razem z wpisami audytu. Należy to ustalić w
   planie albo w aktualizacji konwencji. Możliwości: wymagany (nie-nullowy) `commitWhen` dla
   wyników z porażką, domyślny predykat sukcesu dla wspólnego typu wyniku, albo reguła przeglądu
   „handler zwracający porażkę zawsze podaje `commitWhen`”. Warto też dopisać test: konflikt
   przetłumaczony na wynik bez `commitWhen` nie może przejść niezauważony.
2. **`PersistSecurityInfo=true` w całym runtime** (Context — `ModuleConnection`; Invariant
   impact — 6). Wybór rozwiązuje problem z v1 (połączenie do `master` przy tworzeniu bazy z
   uwierzytelnianiem SQL) i formalnie nie narusza niezmiennika 6, bo hasło nie trafia do
   repozytorium, przeglądarki, odpowiedzi API ani logów. Rozszerza jednak powierzchnię wycieku:
   `SqlConnection.ConnectionString` zawiera hasło przez cały czas życia każdego zakresu, więc
   każdy log, komunikat wyjątku albo diagnostyka, które wypiszą połączenie, ujawnią sekret.
   Tymczasem połączenie do `master` jest potrzebne tylko wtedy, gdy migracja tworzy bazę (testy,
   `test-migrate.sh`), a nie w ścieżce żądań. Zapis „połączenie bez zmian w konfiguracji” w
   tabeli niezmienników nie opisuje tego dokładnie, bo runtime zmienia parametr połączenia.
   Zalecenie dla planu: ograniczyć `PersistSecurityInfo` do hosta migracji i trybu
   design-time albo zapisać wprost zakaz logowania `ConnectionString`/`DbConnection` i sprawdzić
   to w przeglądzie kodu. Decyzję trzeba potwierdzić w przeglądzie człowieka (risk high).
3. **Wspólne połączenie wyklucza równoległe i zachodzące na siebie zapytania** (Context —
   „Wspólna transakcja”; Scope — aktualizacja konwencji). Wcześniej każdy kontekst miał własne
   połączenie. Teraz konteksty Orders, Expenses, Reporting i Integrations w jednym zakresie dzielą
   jeden `SqlConnection` bez MARS. `Task.WhenAll` na kilku zapytaniach (np. raporty w SF-17) albo
   strumieniowe `AsAsyncEnumerable` przeplatane z innym zapytaniem kończą się błędem „open
   DataReader”/„operation in progress”. Aktualizacja `conventions.md` powinna to zapisać obok
   zakazu puli kontekstów. Warto też dopisać zakaz `EnableRetryOnFailure` (strategia ponawiania
   odrzuca transakcje inicjowane przez użytkownika) i zakaz własnego
   `Database.BeginTransaction` w kontekstach modułów finansowych.
4. **Stan `ChangeTracker` po wycofaniu w tym samym zakresie** (Edge Cases — „Kolejna jednostka
   pracy w tym samym zakresie … działa”). Odłączenie transakcji nie czyści śledzonych encji. Po
   wycofaniu (wyjątek albo `commitWhen = false`) encje zapisane w wycofanej transakcji pozostają
   w kontekście jako `Unchanged`. Kolejna jednostka pracy w tym samym zakresie widzi więc stan,
   którego nie ma w bazie. Punkt 2a rozwiązuje to dla zadań w tle (nowy zakres). Dla innych
   wywołań trzeba ustalić: albo czyszczenie `ChangeTracker` dołączonych kontekstów po wycofaniu,
   albo zasada „po wycofaniu zakres nie wykonuje kolejnych zapisów”. Edge case powinien mówić
   o kolejnej jednostce pracy po zatwierdzeniu.

Pozostałe z v1:

5. **Weryfikacja** (Verification) — częściowo. Dopisano `STONEFLOW_TEST_SQLSERVER`/`SQLCMD` i
   zadanie `deploy-dry-run`. Polecenia nadal różnią się od `conventions.md` — „Verification” i CI:
   brak `dotnet restore --locked-mode` (ważne, bo zmieniają się `packages.lock.json`) i
   `dotnet format --verify-no-changes`, a także `--no-restore`/`--no-build`.
6. **Właściciel danych (Reporting zamiast modułu `Audit`)** — zapisane świadomie w
   Assumptions jako punkt do potwierdzenia przez człowieka. Nie blokuje.
7. **Poprawność `IsPersonalData`** — zapisana jako odpowiedzialność wywołującego, a zakaz
   danych osobowych w etykiecie stał się wymaganiem kontraktu. Nie ma wymogu, by każdy moduł
   wywołujący testował oznaczenie swoich pól. Warto to przenieść do konwencji i planów SF-8 i
   kolejnych zadań.

Rozwiązane z v1: rejestracja połączenia (w `AddSharedInfrastructure`, zakaz puli,
`DbContextSchemaTests` już rejestruje Shared), `dotnet ef` i uwierzytelnianie SQL (kryterium na
kontenerze, Testcontainers MsSql w CI), osobny zakres DI na jednostkę pracy w zadaniach (2a),
brak wpisu bez zmian (2b), aktualizacja konwencji (Scope — In scope), wynik-porażka
(`commitWhen`, uwagi w punkcie 1 powyżej).

### Unclear or Ambiguous Sections

- **Behavior, punkt 3 — skąd `IAuditLog` wie o transakcji.** Wciąż nie zapisano, że
  implementacja sprawdza aktywną transakcję `ModuleConnection` tego samego zakresu, a nie np.
  `ReportingDbContext.Database.CurrentTransaction`, który jest pusty, dopóki Reporting nie wywoła
  `SaveChanges`. Wynika to z Context, ale nie jest powiedziane wprost. Do ustalenia w planie.
- **Behavior, punkt 1.5 — zatwierdzenie a anulowanie.** Nie wiadomo, czy `CommitAsync` dostaje
  `cancellationToken`. Jeżeli tak, anulowanie po zakończeniu `work` wycofa poprawnie wykonaną
  operację. To zachowanie spójne z Edge Cases („Anulowanie … → rollback”), ale warto je zapisać.
- **Behavior, punkt 6 — data biznesowa przy zmianie daty.** Wpis ma nową datę, a stara jest w
  `Changes`. AUD-01 wymaga wskazania zmiany, która zmieniła raport za miniony okres. Według
  PRD lista filtruje po okresie zmiany i rodzaju obiektu, nie po dacie biznesowej, więc reguła
  wystarcza. SF-18 musi jednak wiedzieć, że stary okres jest tylko w `Changes`.
- **Data / API — `Expense` z rodzicem.** Domena nie rozpozna, czy wydatek jest wystąpieniem
  cyklicznym. Może sprawdzić tylko, że rodzic, jeżeli jest, ma rodzaj
  `RecurringExpenseDefinition`. Wymóg rodzica dla wystąpień spoczywa na module Expenses. Warto to
  zaznaczyć, żeby Edge Case „obiekt nadrzędny niezgodny z tabelą” nie był czytany szerzej.

### Invented or Unsupported Requirements

None. Obiekt nadrzędny, `commitWhen`, reguły formatu i daty biznesowej oraz `PersistSecurityInfo`
wynikają z wymagań zgłoszenia (anonimizacja, wspólna transakcja, autor, obiekt z datą
biznesową) i z uwag v1.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Context — „Wspólna transakcja”; Types — `IUnitOfWork` | `conventions.md` — CQRS i Data access (`IAccessUnitOfWork`, atomowość jednego `SaveChanges`) | Zmiana konwencji, nie decyzji. Aktualizacja `conventions.md` jest w zakresie; powinna objąć też punkty z Non-critical 1, 3 i 4. |
| Context — `ModuleConnection` (`PersistSecurityInfo=true`) | `rules.md` — niezmiennik 6 | Brak naruszenia; większa powierzchnia wycieku hasła (Non-critical 2). |
| Verification | `conventions.md` — Verification; `.github/workflows/ci.yml` | Brak `restore --locked-mode` i `format --verify-no-changes` (Non-critical 5). |
| Context — właściciel danych (Reporting) | Assumptions — propozycja 1a (moduł `Audit`) | Świadome odejście, zgodne z PRD sekcja 10; do potwierdzenia przez człowieka. |

Brak konfliktu z PRD sekcja 5 („Historia zmian”, „Anonimizacja danych klienta”), AUD-01,
DANE-01, D-05 i D-09. `LayerDependencyTests` i `DbContextSchemaTests` nie są sprzeczne z
mechanizmem.

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | Not applicable | Brak obliczeń sprzedaży i przepływów. |
| 2. Backend decimal money | OK | Kwoty w audycie to tekst w kulturze niezmiennej; audyt niczego nie liczy. |
| 3. API-enforced access | Not applicable | Brak endpointów (SF-18). |
| 4. Retries never duplicate | OK | Brak wpisu, gdy nic się nie zmieniło (2b); nowy zakres na próbę (2a). |
| 5. Financial audit trail | OK | Autor, czas UTC, obiekt z datą i rodzicem, operacja, zakres zmiany; migawka przy usunięciu; zapis tylko w transakcji zmiany. Ryzyko częściowego zatwierdzenia przy `commitWhen = null` (Non-critical 1). |
| 6. Secrets stay server-side | OK | `PersistSecurityInfo=true` nie ujawnia sekretu, ale zwiększa ryzyko wycieku przez logi (Non-critical 2). |
| 7. Module and layer boundaries | OK | Kontrakt w `Shared.Application`, enumy w `Shared.Kernel`, implementacja w Reporting; Shared nie zna typów modułów; brak FK do innych schematów. |
| 8. No identifying data in docs | OK | Dane fikcyjne; etykieta bez danych osobowych jako wymaganie kontraktu. |

## Risk

- Recorded: high
- Assessed: high. Zadanie dotyczy audytu finansowego (niezmiennik 5), zmienia obsługę
  połączeń i transakcji wszystkich modułów finansowych i ustala model danych, który później
  nieodwracalnie anonimizuje się. Wymagany przegląd człowieka przed merge. Przegląd powinien
  objąć wybór Reporting jako właściciela i `PersistSecurityInfo`.

## Assumptions Detected

| Założenie | Podane w specyfikacji |
|---|---|
| Właścicielem danych jest Reporting | Tak (Q1, do potwierdzenia przez człowieka) |
| Zakres zmiany jako lista pól w JSON z flagą danych osobowych | Tak (Q2) |
| Obiekt = rodzaj, identyfikator, data biznesowa, etykieta, rodzic | Tak (Q3, punkt 6) |
| Autor przekazywany jawnie przez handler | Tak (Q4) |
| Jawne wywołanie zamiast interceptora zmian EF | Tak (Q5) |
| Wpisy tylko dodawane, baza bez blokady (dla SF-22) | Tak (Q6, punkt 8) |
| Access poza wspólnym połączeniem | Tak |
| Kontekst dołącza do transakcji przez interceptory, bez wiedzy Shared o typach | Tak (Context) |
| `PersistSecurityInfo=true` jest akceptowalne względem niezmiennika 6 | Tak, z uzasadnieniem; bez ograniczenia do migracji (Non-critical 2) |
| Handler zwracający porażkę zawsze podaje `commitWhen` | Nie (Non-critical 1) |
| Konteksty w zakresie nie wykonują zapytań równolegle | Nie (Non-critical 3) |
| Zakres nie wykonuje kolejnych zapisów po wycofaniu | Nie (Non-critical 4) |
| Brak `EnableRetryOnFailure` w kontekstach finansowych | Nie (Non-critical 3) |
| Moduł Expenses sam pilnuje rodzica dla wystąpień cyklicznych | Nie (Unclear — `Expense` z rodzicem) |

## Recommendation

- Proceed as-is. Można przejść do `sdd-plan SF-7`. Plan powinien domknąć Non-critical 1–4
  (bezpieczne użycie `commitWhen`, zakres `PersistSecurityInfo`, zasady wspólnego połączenia w
  konwencjach, stan `ChangeTracker` po wycofaniu) i ujednolicić polecenia weryfikacji z
  `conventions.md`.
