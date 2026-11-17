# Spec review — SF-14

## Summary

- Overall assessment: PASS WITH ISSUES
- Justification: Wersja 4 usuwa problem krytyczny z `03-spec-review-v3.md`. Reguła wieku odczytu (Behavior → 2, Assumptions 32) sprawia, że bez świeżego odczytu żaden stan, także „Aktualne”, nie daje `dataComplete = true` dłużej niż dwa okresy. Zdanie „nigdy jej zawyżać” ma teraz pokrycie z ograniczeniem czasowym, jakie ma każde odpytywanie, a nowe kryterium akceptacji da się sprawdzić na sztucznych timerach. Rozwiązane są też uwagi niekrytyczne v3: wysyłka odczytu w tle tylko w obsłudze zdarzenia aktywności usuwa nadwyżkę sesji (DOST-01 bez tolerancji), znaczniki `lastActivityAt`/`lastReadAt` z porównaniem ścisłym ujednolicają punkt odniesienia, wpis do `conventions.md` ma jedną treść, a „Spróbuj ponownie” i limity czasu wszystkich żądań są rozstrzygnięte. Zostają drobne braki: wygląd wskaźnika i ekranu „Integracja” w wariancie `aged`, opóźnienie zegara 5 s względem kryterium „najpóźniej po dwóch okresach” oraz kilka pozostałości poprzednich wersji. Żaden nie blokuje planu.

## Findings

### Critical Issues

None.

### Non-Critical Issues

1. **Behavior → 2 („Reguła wieku odczytu”, „etykieta stanu bez zmian”); Behavior → 4; Behavior → 5 (sekcja „Stan synchronizacji”).**
   - Nie wiadomo, czy w `aged` odznaka zachowuje ton `success` i ikonę „✓”. Przy „Aktualne” w zieleni z „✓” drobny dopisek „Stan z HH:mm” słabo komunikuje niepewność. Komunikat przy wynikach jest ostrzeżeniem, więc INT-01 jest spełnione, ale specyfikacja powinna wprost ustalić ton i ikonę wskaźnika w `aged` (np. bez zmian świadomie albo `neutral`), żeby plan i testy nie zgadywały.
   - Ekran „Integracja” nie ma opisu wariantu `aged`. Sekcja stanu pokazałaby nadal wyjaśnienie „Ostatnie uzupełnianie się powiodło i nie ma otwartych błędów zamówień.” bez dopisku. Bierny obserwator ekranu integracji (scenariusz z Edge Cases „Znane ograniczenie — bierny obserwator”) widzi tam stan bez oznaczenia wieku. Trzeba dopisać, co sekcja stanu pokazuje w `aged` (co najmniej dopisek „Stan z HH:mm”).
2. **Behavior → 3 („Zegar lokalny co 5 s”); Acceptance Criteria („najpóźniej po dwóch okresach”, „odczyt starszy niż 120 s (w `running` 20 s)”).** `aged` wylicza się z `now`, które zegar aktualizuje co 5 s, a `readAt` nie jest wyrównany do tyknięć. Wariant ostrożny pojawia się więc do 5 s po progu (do 125 s, w `running` do 25 s). Kryterium „najpóźniej po dwóch okresach” jest dosłownie nie do spełnienia. Należy dopisać tolerancję tyknięcia albo zaplanować timer na chwilę `readAt + próg`. Od tego zależy granica w teście na sztucznych timerach.
3. **Pozostałości poprzednich wersji (spójność tekstu):**
   - Assumptions 25, pierwszy punkt: „aktywności użytkownika od poprzedniego żądania w tle” przeczy pkt 34 i Behavior → 3, gdzie `lastReadAt` ustawia także odczyt wymuszony. To dokładnie niejednoznaczność z NC2 w v3; obowiązuje pkt 34, ale zdanie trzeba poprawić.
   - Edge Cases („Błąd odczytu stanu po wcześniejszym sukcesie”): „Kolejne tyknięcie (przy aktywności) ponawia odczyt” przeczy Behavior → 3 („Zegar nigdy go sam nie wysyła”). Ponawia aktywność, gdy odczyt jest należny.
   - Behavior → 6: „Ma trzy warianty”, a lista ma pięć (kompletne, niekompletne, `unknown`, `aged`, `loading`).
   - Scope („treść w Assumptions 21”) wskazuje pkt 21, a jedyną treścią wpisu jest pkt 27.
   - Acceptance Criteria dla `app-incomplete-data-notice` nie wymieniają wariantu `aged` (jest w osobnym kryterium; warto dopisać odsyłacz).
4. **Behavior → 2 i Edge Cases („»Ponów« bez odpowiedzi przez 45 s … Spóźniona odpowiedź tego żądania jest ignorowana”).** Operator `timeout` odsubskrybowuje źródło, więc `HttpClient` przerywa żądanie i odpowiedzi nie będzie. Na serwerze ponowienie może jednak dalej trwać, a wiersz po wyjściu z `busy` przyjmie kolejne „Ponów”. Ochronę przed duplikatami daje backend (inwariant 4, SF-13), więc to tylko nieścisły opis. Warto zapisać, że po przekroczeniu limitu wynik po stronie serwera jest nieznany i ustali go przeładowanie listy (tak jak dla „Synchronizuj teraz”).
5. **Behavior → 2 („Stan z {formatTime(readAt)}”, `HH:mm`).** Sam czas bez daty jest niejednoznaczny, gdy odczyt jest starszy niż doba (komputer uśpiony przy widocznej karcie; po wybudzeniu `visibilitychange` może nie przyjść). Skutek jest bezpieczny (wariant ostrożny, a pierwsza aktywność odświeża stan albo `401` wylogowuje), ale należy to przyjąć jawnie albo przejść na `formatDateTime`, gdy `readAt` jest z innego dnia.

### Unclear or Ambiguous Sections

- **Behavior → 3 („Jedna reguła dla wszystkich odczytów w tle”).** Przy wysyłce wyłącznie w obsłudze zdarzenia warunki `lastActivityAt > lastReadAt` i `now − lastActivityAt ≤ okres` są spełnione z definicji, a decyduje tylko „należność” (`now − lastReadAt ≥ okres`) i widoczność karty. Kolejność „najpierw `lastActivityAt = now`, potem sprawdzenie” wynika z tekstu pośrednio. Warto zapisać ją wprost, bo od niej zależy wynik porównania ścisłego w teście „`lastActivityAt = lastReadAt` nie wysyła”.
- **Behavior → 2 („Wejście w `aged` jest ogłaszane raz”).** Nie wiadomo, czy wyjście z `aged` bez zmiany `kind` jest ogłaszane (z reguły `announcement` wynika, że nie). U czytelnika, który co kilka minut robi przerwę, ogłoszenie „Stan integracji: Aktualne, stan z HH:mm” powtórzy się po każdej przerwie. Należy potwierdzić, że to zamierzone.
- **Behavior → 2 („»Stan z {HH:mm}« we wskaźniku … oraz w komunikacie”) a Behavior → 4.** W `aged` w stanie `unknown` reguła nie obowiązuje, ale nie wiadomo, czy przejście `aged` → `unknown` (nieudany odczyt po aktywności) zdejmuje dopisek. Z tekstu wynika, że tak; warto dopisać jedno zdanie.

### Invented or Unsupported Requirements

None.

Nowe elementy v4 mają oparcie w zgłoszeniu („UI nie sugeruje kompletności przy awarii”), PRD (INT-01, DOST-01) i konwencjach (`shared/format`) i są jawne w Assumptions: reguła wieku odczytu (32), wysyłka w chwili aktywności (33), znaczniki (34), limity czasu (36), `formatTime` (32).

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Behavior → 2 („Reguła wieku odczytu”); Assumptions 25 i 32 | PRD INT-01; zgłoszenie („UI nie sugeruje kompletności przy awarii”) | Brak sprzeczności. Nieodczytana awaria jest widoczna jako „Aktualne” najwyżej przez dwa okresy (+ do 5 s tyknięcia, Non-Critical 2), potem wariant ostrożny |
| Behavior → 3; Invariant impact, wiersz 3 | PRD DOST-01, sekcja 3 („Sesja wygasa po 60 minutach bezczynności”); `SessionCookie.ValidatePrincipalAsync` (`ShouldRenew = true`) | Brak sprzeczności. Każde żądanie wychodzi w chwili celowej aktywności albo zaraz po akcji; tolerancja „+ jeden okres” usunięta |
| Behavior → 6 (komunikat zawsze) | Design guide sekcja 4 („jeżeli występuje”) | Brak rzeczywistej sprzeczności: wariant kompletny to linia czasu, PRD sekcja 5 („przy wynikach zawsze”) ma pierwszeństwo |
| Assumptions 4 („Ostatnia pełna kontrola” = kursor) | PRD sekcja 5 („czas ostatniego sukcesu”) | Kursor (start − 10 min), wariant ostrożniejszy, zgodny z SF-13 S2 i zapisany jawnie |
| Behavior → 1 (`notConfigured`) | PRD sekcja 5 (cztery stany) | Stan techniczny wynikający z INT-01, jawny w Assumptions 2 |
| Assumptions 36 (limit „Ponów” 45 s) | `WooCommerceOptions.DefaultRequestTimeout` = 30 s (SF-13) | Zgodne z kodem; brak warstwy ponowień po stronie API, więc zapas 15 s wystarcza |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | Not applicable | Brak kwot i raportów finansowych |
| 2. Backend decimal money | Not applicable | Frontend nie liczy kwot |
| 3. API-enforced access | OK | Endpointy SF-13 wymagają sesji, zapisy XSRF, `401` obsługuje interceptor. Żądania w tle tylko w chwili aktywności, bez furtki; sesja bezczynna wygasa po 60 min |
| 4. Retries never duplicate | OK | Ochrona po stronie backendu (SF-12/SF-13). UI blokuje podwójne kliknięcie; ponowne „Ponów” po przekroczeniu 45 s chroni backend (Non-Critical 4) |
| 5. Financial audit trail | Not applicable | UI nie zapisuje danych finansowych |
| 6. Secrets stay server-side | OK | Brak kluczy i adresu sklepu w bundlu, komunikaty z API bez sekretów, komunikacja z WooCommerce przez backend |
| 7. Module and layer boundaries | OK | Tylko API Integrations, link z `orderId`, logika stanu i wieku odczytu w czystej funkcji i serwisie poza komponentami |
| 8. No identifying data in docs | OK | Dane testowe fikcyjne („1001”, „Z/2026/0001”) |

## Risk

- Recorded: medium
- Assessed: medium. Wskaźnik i komunikat są wspólne dla całej aplikacji oraz SF-19/SF-20, więc błąd może sugerować kompletność raportów (INT-01). Odświeżanie dotyka DOST-01 i zmienia `conventions.md`. Zadanie nie dotyka pieniędzy, danych klientów ani operacji nieodwracalnych, a backend się nie zmienia.

## Assumptions Detected

- Stan INT-01 liczy frontend czystą funkcją. Podane (1), zgodne z `GetSyncStatus.cs`.
- „Nie skonfigurowano” sprawdzane przed stanami PRD. Podane (2).
- „Błąd” dla każdej wartości różnej od `succeeded`. Podane (3).
- Próg 25 h od kursora, akceptowany fałszywy alarm przy zmianie czasu i opóźnionym starcie. Podane (4), Edge Cases.
- Czas „teraz” i `readAt` z zegara klienta. Podane (5), Edge Cases (przestawiony zegar).
- `dataComplete` w `running` wymaga udanego `lastRun`. Podane (19).
- Brak odczytu daje `unknown`, z zachowaną poprzednią odpowiedzią. Podane (7, 31).
- Aktywność to `keydown`, `pointerdown` i powrót na kartę, bez `wheel`. Podane (21, 30).
- Bez świeżego odczytu kompletność wygasa po dwóch okresach. Podane (25, 32); bez tolerancji tyknięcia 5 s (Non-Critical 2).
- Odczyt w tle tylko w obsłudze zdarzenia aktywności; sesja bez nadwyżki. Podane (33).
- `lastReadAt` ustawia każdy odczyt, także wymuszony. Podane (34); pkt 25 ma starszą treść (Non-Critical 3).
- Etykieta, ton i ikona wskaźnika w `aged` bez zmian. Podane częściowo (tylko etykieta; Non-Critical 1).
- `HH:mm` wystarcza do oznaczenia wieku odczytu. Niepodane wprost (Non-Critical 5).
- Przekroczenie limitu przerywa żądanie po stronie klienta, a wynik na serwerze ustala przeładowanie. Niepodane wprost (Non-Critical 4).
- Wskaźnik, a z nim odświeżanie, jest wszędzie tam, gdzie komunikat. Podane (Behavior 6).
- Ciasteczko `XSRF-TOKEN` ustawia sesja. Podane w Data / API.

## Recommendation

- Proceed as-is. Uwagi niekrytyczne (ton i ikona w `aged`, `aged` na ekranie „Integracja”, tolerancja tyknięcia w kryterium, pozostałości tekstu) można domknąć w planie albo krótką poprawką specyfikacji przed implementacją.
