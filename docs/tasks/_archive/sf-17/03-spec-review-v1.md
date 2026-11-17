# Spec review — SF-17

## Summary

- Overall assessment: FAIL
- Justification: Specyfikacja dobrze pokrywa zgłoszenie, reguły przełomu uruchomienia (PRD §4, URU-01), audyt, idempotencję i granice modułów, a łączenie operacji w pamięci Reporting jest uzasadnione skalą i jawnie opisane jako odstępstwo. Blokuje ją jeden problem: sposób pokazania opłaconego wydatku ujemnego na liście („+50,00 zł”, bez kolumny kierunku) przeczy przewodnikowi projektowemu (§ formularz wydatku i § listy) i własnemu kryterium akceptacji specyfikacji („wypływ z kwotą ujemną”). Lista „Operacje” służy do uzgadniania przepływów (FIN-05), więc prezentacja znaku kwoty musi być jednoznaczna, zanim powstanie plan. Pozostałe uwagi nie blokują planowania.

## Findings

### Critical Issues

1. **Znak kwoty opłaconego wydatku ujemnego i brak kolumny kierunku (Behavior → Lista „Operacje”, pkt 13; UI → Lista „Operacje”).**
   - Specyfikacja koduje kierunek wyłącznie znakiem kwoty: wypływ `−1 000,00 zł`, a opłacony wydatek ujemny jako `+50,00 zł` z dopiskiem „Korekta / zwrot od dostawcy”. Kolumny tabeli: Data, Rodzaj, Opis, Metoda, Kwota — bez kierunku.
   - `docs/DESIGN-GUID-stone-flow.md` (opis list): kolumny listy „Operacje” to „data, rodzaj, opis z przejściem do źródła, **kierunek**, kwota i metoda”, a „Opłacony wydatek ujemny jest wypływem z kwotą ujemną”.
   - `docs/DESIGN-GUID-stone-flow.md` (formularz wydatku): ujemną kwotę „formularz i **listy** pokazują ze znakiem minus i opisem »Korekta / zwrot od dostawcy«”.
   - Kryterium akceptacji samej specyfikacji: „Opłacony wydatek −50 zł jest wypływem z kwotą ujemną”, a zgłoszenie: „Opłacony wydatek ujemny jest wypływem z kwotą ujemną”. Wiersz „+50,00 zł” nie pokazuje kwoty ujemnej i wygląda jak wpływ.
   - Skutek: ten sam wydatek ma przeciwny znak na liście wydatków (−50) i na liście „Operacje” (+50); użytkownik uzgadniający przepływy może odczytać korektę od dostawcy jako wpływ. Specyfikacja musi przyjąć prezentację zgodną z przewodnikiem (osobny kierunek „Wypływ” + kwota ze znakiem zapisu, np. `−50,00 zł`) albo zapisać świadomą zmianę przewodnika jako decyzję właściciela.

### Non-Critical Issues

1. **Metoda opłaconego wydatku może być pusta (Data / API → Types crossing a boundary).** `Expense.PaymentMethod` jest `ExpensePaymentMethod?`, a `MarkPaid` przyjmuje metodę opcjonalną. `CashOperation.Method` jest zdefiniowane jako nienullowalny `string`, a element `items` ma `method` bez znacznika opcjonalności. Trzeba określić typ (`string?`) i wyświetlanie braku metody w kolumnie „Metoda”.
2. **Prezentacja sumy „Wypływy” ujemnej (Behavior pkt 12; UI).** Przy filtrze rodzaju „Opłacony wydatek” w okresie z samymi korektami od dostawców suma wypływów jest ujemna. Specyfikacja nie mówi, jak ją pokazać (znak, opis), a reguła pkt 13 dotyczy tylko wierszy.
3. **Semantyka `amount` w elemencie `items` (Data / API → Format danych).** `CashOperation.Amount` jest opisane (zapis: dodatnia dla wpłat, zwrotów i wypłat; ze znakiem dla wydatku), ale dla JSON nie powiedziano, czy `amount` to kwota zapisana, czy kwota ze znakiem kierunku. Frontend ma tylko formatować, więc to musi być jednoznaczne w kontrakcie.
4. **Gdzie leży reguła sum i daty uruchomienia (Context; Verification).** Konwencje: reguła decydująca o kwocie lub kwalifikacji należy do domeny, nie do handlera. Specyfikacja umieszcza łączenie, kierunek, sumy i odcięcie daty uruchomienia w `ListOperations` (Application). `Reporting.Domain` nie może użyć `CashOperation` z `Shared.Application`. Należy wskazać miejsce reguły (np. typ domenowy Reporting na własnych wartościach albo świadome pozostawienie w Application z uzasadnieniem), żeby plan nie rozstrzygał tego sam.
5. **Uzgodnienie z SF-20 (FIN-05).** Zgodność sum listy z kartami przepływów zależy od tego, czy SF-20 użyje tych samych źródeł i tej samej reguły sum. Specyfikacja powinna nazwać jednostkę wielokrotnego użytku (źródła + kalkulacja), którą SF-20 ma użyć, zamiast zostawiać to domysłowi. Dodatkowo limit 366 dni może kolidować z przejściem z kart przy „własnym zakresie” dłuższym niż rok — warto zapisać to jako ograniczenie dla SF-20.
6. **Bezpiecznik 10 000 operacji (Data / API → Types crossing a boundary).** Próg jest sprawdzany „gdy źródła zwrócą więcej”, czyli po zmaterializowaniu całego wyniku. Nie jest też powiedziane, czy próg dotyczy każdego źródła, czy sumy. Warto ograniczyć odczyt w SQL (np. pobranie progu + 1 na źródło), żeby bezpiecznik faktycznie chronił pamięć.
7. **Przekazanie efektywnego początku do źródeł (Behavior pkt 8; Types crossing a boundary).** Nie jest napisane, czy Reporting wywołuje `ListAsync` z `effectiveFrom` (odcięcie w SQL), czy filtruje po odczycie. Reguła daty uruchomienia powinna mieć jedno miejsce egzekwowania, wskazane w specyfikacji.
8. **Testy przełomu uruchomienia dla wpłat.** Edge cases i pkt 9 wymieniają zwrot do zamówienia sprzed uruchomienia i zapłatę wydatku z wcześniejszą datą kosztu, ale nie wpłatę od uruchomienia do zamówienia sprzed uruchomienia (URU-01: „wpłata otrzymana po uruchomieniu zwiększa wpływy”) ani wpłaty sprzed uruchomienia do takiego zamówienia (ukryte). Reguła ogólna je obejmuje, ale lista testów integracyjnych powinna je nazwać.
9. **Nazwa `totals.net` (Endpoints).** W domenie „netto” oznacza kwotę bez VAT (wydatki mają `NetAmount`). `net` dla „Zmiany środków” grozi pomyłką; czytelniej np. `change`.
10. **Stany UI szczegółów i formularza wypłaty (UI).** Opisano `404` i `409`, ale nie stan wczytywania szczegółów/podpowiedzi ani błąd sieci przy zapisie (poza konfliktem) i wyczerpanie listy podpowiedzi.
11. **Strona poza zakresem (Behavior pkt 11).** Nie określono odpowiedzi dla `page` większej niż liczba stron (pusta strona czy `400`).

### Unclear or Ambiguous Sections

- **Data / API → Walidacja:** „`to - from` ≤ 366 dni” — nie wiadomo, czy liczone włącznie (366 dni kalendarzowych) czy jako różnica (do 367 dni). Rok przestępny mieści się w obu wariantach, ale test graniczny wymaga jednej definicji.
- **Edge Cases → okres całkowicie przed datą uruchomienia:** `effectiveFrom` byłby wtedy późniejszy niż `to`. Nie powiedziano, jaką wartość zwraca odpowiedź (`effectiveFrom` = data uruchomienia > `to`?) ani że nie jest to błąd walidacji `from` > `to`.
- **Behavior pkt 9 / Types crossing a boundary:** `sourceLabel` jest opisany tylko dla wpłat i zwrotów; dla wydatku i wypłaty nie wiadomo, czy to opis, kategoria, czy osoba. Mapowanie kodów API (`expense`, `withdrawal`) na `CashOperationKind` (`PaidExpense`, `OwnerWithdrawal`) jest domyślne.
- **Behavior pkt 5:** zapis bez zmienionych pól nie tworzy audytu — nie powiedziano, czy taki `PUT` zmienia `version` i `UpdatedAtUtc` (porównać ze wzorem wydatków).

### Invented or Unsupported Requirements

None. Elementy spoza tekstu zgłoszenia (wiersz sum, limit 366 dni, bezpiecznik 10 000, data ≤ dziś, indeksy) są wpisane w Assumptions z uzasadnieniem albo wynikają z odpowiedzi właściciela z 09.11.2026 i z FIN-05.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Behavior pkt 13; UI → Lista „Operacje” | `DESIGN-GUID-stone-flow.md`, opis list („Operacje: … kierunek, kwota i metoda … Opłacony wydatek ujemny jest wypływem z kwotą ujemną”) | Brak kolumny kierunku; wydatek ujemny pokazany jako `+50,00 zł` zamiast kwoty ujemnej |
| Behavior pkt 13 | `DESIGN-GUID-stone-flow.md`, formularz wydatku („formularz i listy pokazują ją ze znakiem minus”) | Przeciwny znak niż na liście wydatków dla tego samego rekordu |
| Behavior pkt 13 | Acceptance Criteria tej specyfikacji i zgłoszenia („wypływem z kwotą ujemną”) | Wewnętrzna niespójność specyfikacji |
| Data / API → Types crossing a boundary (łączenie w pamięci) | `conventions.md` → Data access („Lists filter, sort and paginate in the database”) | Odstępstwo jawne i uzasadnione skalą (D-08: ok. 20–30 zamówień miesięcznie), z limitem okresu i bezpiecznikiem — akceptowalne, nie blokuje |

PRD §8 (podpowiedź netto ÷ 1,23) pominięte zgodnie z zakresem — zmiana na gałęzi SF-16.

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | OK | Wypłata tylko wypływem; lista bez korekt sprzedaży, nieopłaconych wydatków i wartości zamówień; wypłata poza listą wydatków |
| 2. Backend decimal money | OK | `decimal(18,2)`, sumy w backendzie, JSON jako ciągi; frontend tylko formatuje (pod warunkiem doprecyzowania semantyki `amount`, Non-Critical 3) |
| 3. API-enforced access | OK | Wszystkie endpointy z sesją, zapisy z CSRF, testy 401/CSRF w Verification |
| 4. Retries never duplicate | OK | `Idempotency-Key`, unikalny filtrowany indeks `CreationRequestId`, hash treści, `409 idempotency_key_reused` |
| 5. Financial audit trail | OK | Create/update/delete w tej samej jednostce pracy; usunięcie zapisuje stare wartości; `person` jako dane osobowe; data biznesowa = data wypłaty, bez rodzica (zgodnie z conventions) |
| 6. Secrets stay server-side | Not applicable | Brak nowych sekretów |
| 7. Module and layer boundaries | OK | Kontrakt `ICashOperationSource` w `Shared.Application`, implementacje w Infrastructure modułów-właścicieli (wzór `IShopOrderLookup`), Reporting bez mapowania tabel `orders`/`expenses`, wywołania sekwencyjne na wspólnym `ModuleConnection` |
| 8. No identifying data in docs | OK | Fikcyjne osoby w przykładach; `sourceLabel` wpłat/zwrotów bez danych klienta; etykieta audytu bez osoby |

## Risk

- Recorded: high
- Assessed: high — nowy rodzaj wypływu, lista uzgodnienia przepływów (FIN-05) i reguły przełomu uruchomienia; wymagany przegląd człowieka przed merge.

## Assumptions Detected

| Assumption | Stated in spec |
|---|---|
| Operacje sprzed daty uruchomienia ukryte na liście, informacja o efektywnym początku (odpowiedź 1) | Tak |
| Osoba jako tekst z podpowiedziami, bez słownika (odpowiedź 2) | Tak |
| Kwota > 0, data ≤ dziś, metody przelew/BLIK/gotówka (odpowiedź 3) | Tak |
| Wypłata bez komentarza i referencji dokumentu | Tak |
| Brak osobnej pozycji nawigacji dla wypłat | Tak |
| Wiersz sum na liście w zakresie SF-17 | Tak |
| Domyślny okres = bieżący miesiąc, maks. 366 dni | Tak |
| Łączenie w pamięci uzasadnione skalą, bezpiecznik 10 000 | Tak |
| Wypłatę można zapisać z datą sprzed uruchomienia | Tak |
| Wpłaty do zamówień anulowanych pozostają na liście | Tak (Edge Cases) |
| Wpłaty do zamówień pending/failed/szkiców są na liście (filtr tylko po dacie i rodzaju) | Nie — wynika z braku filtra statusu, warto zapisać wprost |
| Metoda opłaconego wydatku zawsze obecna | Nie — ukryte w typie `Method: string`, sprzeczne z modelem (Non-Critical 1) |
| Reporting sam stosuje odcięcie daty uruchomienia (a nie źródła) | Nie — niejawne (Non-Critical 7) |
| SF-20 użyje tych samych źródeł i reguły sum | Nie — niejawne (Non-Critical 5) |
| Kierunek wynika z rodzaju operacji, nie z pola DTO | Częściowo — tabela pkt 9, bez pola w `CashOperation` |

## Recommendation

- Revise specification — poprawić prezentację wydatku ujemnego i kolumnę kierunku zgodnie z przewodnikiem projektowym (albo zapisać decyzję właściciela o zmianie przewodnika), a przy okazji doprecyzować uwagi niekrytyczne 1–7.
