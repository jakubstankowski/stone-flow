# Spec review — SF-12

## Summary

- Overall assessment: PASS WITH ISSUES
- Justification: Wersja 2 usuwa oba problemy krytyczne z v1. Stan „zakończone w sklepie”
  (`ShopDeletedAtUtc` albo `ShopStatus` ∈ {`cancelled`, `trash`}) i przejście liczone ze zmiany
  aktywne → zakończone sprawiają, że ponowne `order.deleted` już nie anuluje zamówienia. Znacznik
  `orders.ShopDeletions` i porównanie daty modyfikacji z czasem odbioru usunięcia chronią przed
  spóźnionymi `order.created`/`order.updated`. Non-Critical 1–15 z v1 są w większości rozwiązane.
  Twierdzenia o kodzie się zgadzają (`Order`, unikalne indeksy, `IOrderLock`, CSRF tylko pod `/api`,
  fallback policy, polityki limitu w `AccessModule`, `EndpointSecurityTests`, `OrderListItemDto` bez
  `ShopStatus`, wzorzec ostrzeżenia z `sales-corrections.html`). Zostały luki w nowych regułach.
  Najważniejsze: bezwarunkowe zastosowanie `order.restored` wbrew regule kolejności, wyścig znacznika
  z utworzeniem, kontrakt `ShopOrderSnapshot` bez informacji o temacie i niepotwierdzone założenie,
  że WooCommerce ponawia dostawy. Żadna z nich nie łamie niezmiennika w realnym przebiegu, więc nie
  blokują planu. Trzeba je jednak rozstrzygnąć w planie albo krótką poprawką spec.

## Findings

### Critical Issues

None.

### Non-Critical Issues

1. **`order.restored` omija regułę kolejności względem usunięcia** (Behavior → „Zastosowanie
   snapshotu”, pkt 2 i 3). Dostawa `order.restored` jest „stosowana i czyści `ShopDeletedAtUtc`”
   niezależnie od daty modyfikacji, a przy nieznanym zamówieniu zawsze usuwa znacznik. Pierwsza
   reguła kolejności przepuszcza datę równą zapisanej. Spóźniona dostawa przywrócenia z wcześniejszego
   cyklu (przywrócenie t1 → usunięcie t2, dostawy w odwrotnej kolejności) cofa więc późniejsze
   usunięcie. Skutki: fałszywe „Zamówienie aktywne w sklepie”, a przy nieznanym zamówieniu aktywny
   rekord, który liczy się do sprzedaży, choć w sklepie go nie ma. To ta sama klasa błędu co
   Critical 2 z v1 (PRD sekcja 5: „Dane starsze od zapisanych … są pomijane”). Wyjątek ma zapewne
   chronić przed różnicą zegarów, ale spec tego nie mówi i nie podaje granicy reguły. Potrzebna
   jest reguła (np. `order.restored` też porównywane z `ShopDeletedAtUtc`/`ReceivedAtUtc`, z tolerancją)
   albo świadomie przyjęte ryzyko w Assumptions. Do tego WooCommerce wysyła `order.deleted` zarówno
   przy przeniesieniu do kosza, jak i przy trwałym usunięciu. Po takim cofnięciu drugie usunięcie
   znowu wywołuje przejście na „Anulowane”, także po przywróceniu w ERP przez użytkownika.
2. **`ShopOrderSnapshot` nie przenosi tematu dostawy** (Data / API → „Types crossing a boundary”).
   Reguły w Orders zależą od tego, czy dostawa to `order.restored` (pkt 2: „dostawa nie jest
   `order.restored`”, pkt 3: „albo `order.restored` jest stosowana”), a kontrakt ma tylko `IsDeletion`.
   Implementujący musi zgadywać (`IsRestore`? temat jako enum?).
3. **Wynik `ShopOrderSyncResult` nie pasuje do v2** (Data / API → „Types crossing a boundary”;
   Behavior → pkt 2). `IgnoredDeletion` to nazwa z v1, bo teraz usunięcie nieznanego zamówienia
   zapisuje znacznik. Brakuje wyniku dla pominięcia spóźnionej dostawy sprzed usunięcia i dla
   naruszenia unikalności. Nie wiadomo, jak Integrations dowiaduje się, że ma ponowić „w nowym
   zakresie DI”. Według conventions.md („Errors and validation”) Infrastructure tłumaczy błąd SQL
   2601/2627 na wynik portu, a nieprzetłumaczony daje `503` (`SecuritySetup.SelectStatusCode`).
   Podobnie nieopisany jest równoczesny zapis znacznika dla tego samego ID (PK `ShopDeletions`) —
   bez tłumaczenia skończy się `503`.
4. **Wyścig znacznika usunięcia z utworzeniem** (Behavior → „Zastosowanie snapshotu”, pkt 1–2).
   Blokada `UPDLOCK` „tak jak wpisy w SF-9” (`OrderLock`: `WITH (UPDLOCK, ROWLOCK) WHERE [Id] = …`)
   nie chroni wiersza, który jeszcze nie istnieje. Przebieg: dostawa `order.created` nie widzi
   znacznika, w tym czasie `order.deleted` zapisuje znacznik i zatwierdza, a potem utworzenie „usuwa
   znacznik” i zatwierdza aktywne zamówienie. Usunięcie przepada, a REST go nie odtworzy (SF-2).
   Ponowienie po naruszeniu unikalności rozwiązuje tylko wyścig dwóch utworzeń. Spec powinna
   określić serializację obu ścieżek dla jednego `ExternalId` (np. blokada zakresu/klucza po
   `ExternalId` w obu tabelach). Musi też powiedzieć, że blokada po `ExternalId` to nowy port, a nie
   istniejące `IOrderLock.LockAsync(Guid)` (punkt niejasny już w v1).
5. **Niepotwierdzone założenie, że WooCommerce ponawia dostawy** (Behavior → „Przyjęcie webhooka”,
   pkt 6; Invariant impact 4). „`503`; WooCommerce może ponowić dostawę”. O ile mi wiadomo,
   WooCommerce nie ponawia nieudanej dostawy automatycznie, tylko liczy kolejne niepowodzenia
   i wyłącza webhook (SF-2: „więcej niż pięciu kolejnych niepowodzeniach”). SF-2 tego nie
   potwierdziło. Jeśli tak jest, `503` dla `order.deleted` oznacza trwałą utratę usunięcia. SF-2
   wymaga w takim przypadku decyzji właściciela („Jeśli utraconego usunięcia nie da się odtworzyć,
   wymagana jest decyzja właściciela …”). Spec powinna to założenie wpisać do Assumptions, wskazać
   ryzyko utraconego usunięcia (także przy wyłączonym webhooku i braku sekretu) i przypisać je do
   SF-13 lub decyzji właściciela.
6. **Wyjątek od konwencji tras nie jest nazwany** (Scope; Data / API → Endpoints). Według
   conventions.md („Naming”) „All API routes start with `/api` (except `/health`)”, a endpointy
   powstają w `MapGroup("/api/{area}")`. Spec uzasadnia `/webhooks/...` (CSRF dla każdego POST pod
   `/api`, także anonimowego, i brak przepisania przez Vercel). To uzasadnienie jest trafne, ale
   wyjątek trzeba zapisać wprost jako odstępstwo od konwencji, a nie tylko jako założenie
   o Vercelu.
7. **Klucz konfiguracji niezgodny z istniejącym układem** (Behavior → „Przyjęcie webhooka”, pkt 1).
   Wszystkie klucze aplikacji leżą pod `StoneFlow:` (`StoneFlow:LaunchDate`, `StoneFlow:Email…` —
   `docs/wdrozenia.md` sekcja 4, `deploy/api.env.example`). Spec podaje
   `Integrations:WooCommerce:WebhookSecret`. Spec nie wskazuje też miejsca dokumentacji („dokumentacja
   konfiguracji webhooka” bez sekcji `docs/wdrozenia.md` i wpisu w `api.env.example`; uwaga z v1,
   Non-Critical 8).
8. **Konfiguracja webhooków w sklepie** (Scope; Verification). Jeden endpoint obsługuje cztery tematy,
   a w WooCommerce każdy temat to osobny webhook z własnym sekretem i wersją API. Mapowanie zakłada
   payload REST v3 (`date_created_gmt`, `meta_data`, `refunds`). Dokumentacja musi wymagać czterech
   webhooków z tym samym sekretem i wersją v3. Sekret warto ograniczyć do znaków alfanumerycznych
   (WooCommerce przetwarza sekret funkcjami WordPressa przed podpisem; do sprawdzenia w próbie SF-2).
   Spec powinna też ostrzec, że każde `401`/`404`/`413`/`429`/`503` liczy się do wyłączenia webhooka
   (v1, Non-Critical 8, rozwiązane tylko częściowo).
9. **Sprzeczność co do reguły „liczy się do sprzedaży”** (Scope → Out of scope vs „Reguły
   w domenie” i Assumptions). Out of scope: „SF-12 … udostępnia regułę »liczy się do sprzedaży«
   w domenie”. „Reguły w domenie” i Assumptions: reguła „należy do SF-19 (nie w SF-12)”.
10. **Założenie o różnicy zegarów opisuje tylko jeden kierunek** (Assumptions; Behavior pkt 3).
    Spec wymienia ryzyko, że modyfikacja tuż po usunięciu zostanie uznana za wcześniejszą (zegar
    sklepu się spóźnia). Groźniejszy jest kierunek odwrotny: zegar sklepu się spieszy, a spóźniona
    dostawa sprzed usunięcia ze statusem aktywnym zostaje uznana za późniejszą, czyści
    `ShopDeletedAtUtc` i pokazuje fałszywe „aktywne w sklepie”. Ryzyko jest małe, ale powinno być
    nazwane.
11. **Kwota `total` i zaokrąglenie** (Mapowanie payloadu; niezmiennik 2). „decimal, kropka
    dziesiętna, 2 miejsca” nie mówi, co z wartością o innej liczbie miejsc (np. `"123"`, `"123.456"`
    przy innych ustawieniach sklepu): odrzucenie jako nieczytelne czy zaokrąglenie i według jakiej
    reguły. Ujemne `refunds[].total` jest opisane, ale nie wiadomo, czy `ShopRefundedAmount` ma
    ograniczenie `>= 0`.
12. **Klient z pustą nazwą przy obecnym `billing`** (Mapowanie payloadu; Edge Cases). `Customer.Name`
    jest wymagane (`Customer.cs`, kolumna `IsRequired`). Nazwa zastępcza „Klient sklepu #{id}” jest
    opisana tylko dla braku `billing`. Brakuje przypadku `billing` z pustymi `company`, `first_name`
    i `last_name` (typowe dla `pending`). NIP z prefiksem `PL` nie jest normalizowany, choć `Customer`
    oczekuje „NIP z samych cyfr”.
13. **Znaczniki usunięcia bez końca życia** (Data / API → Storage). `order.deleted` dla zamówienia
    sprzed uruchomienia (które nigdy nie powstanie) zostawia wiersz na zawsze. Nie wiadomo też, czy
    znacznik zostaje po utworzeniu zamówienia jako usuniętego („W przeciwnym razie znacznik jest
    usuwany” sugeruje, że zostaje). Przy obecnym wolumenie to drobiazg, ale reguła powinna być
    jednoznaczna.
14. **Drobne niespójności API i testów.** Tabela Endpoints nie wymienia `429`, choć Behavior go
    opisuje. Verification nie ma testu `409` dla użytkownika po zapisie synchronizacji (priorytet
    testów 3 „concurrent edits”, Edge Cases o tym mówią) ani testu frontendu dla polskich etykiet
    statusu sklepu na liście.

### Unclear or Ambiguous Sections

- **Szczegóły zamówienia → tabela ostrzeżeń:** warunki `active_in_shop` („status sklepu nie
  anulowany, nieusunięte”) i `cancelled_in_shop` („anulowany lub usunięty”) nie używają
  zdefiniowanych pojęć „aktywne/zakończone w sklepie”. Nie wiadomo, czy `trash` bez
  `ShopDeletedAtUtc` daje `active_in_shop`. Nadal nie jest powiedziane, że anulowane w ERP
  zamówienie `pending`/`failed` też dostanie „Zamówienie aktywne w sklepie” (v1).
- **Edge Cases → „Dwie dostawy tej samej daty modyfikacji … druga stosowana”:** rozdzielczość
  sekundowa `date_modified_gmt` i odwrócona kolejność w tej samej sekundzie (np. `cancelled`, potem
  `processing`) dają fałszywy stan sklepu, a przy kolejnej dostawie ponowne przejście. Ryzyko
  nadal nie jest w Assumptions (v1).
- **Zastosowanie snapshotu, pkt 2:** data uruchomienia jest sprawdzana tylko przy braku zamówienia.
  Nie wiadomo, co się dzieje, gdy sklep zmieni datę istniejącego zamówienia na sprzed uruchomienia
  (v1).
- **Lista zamówień:** nie podano nazwy parametru przełącznika w adresie strony (v1). Wyszukiwanie po
  numerze nie znajdzie ukrytego zamówienia bez przełącznika, a spec tego nie rozstrzyga.
- **Pkt 5 w „Przyjęciu webhooka”:** szkic `checkout-draft` bez dat trafia do „nieczytelnych” (200 +
  log), czyli nie trafia do ERP. PRD: „Do ERP trafiają wszystkie statusy sklepu”. To rozsądna
  konsekwencja braku daty, ale decyzję trzeba zapisać w Assumptions, a nie tylko w przykładzie.
- **Assumptions → `cancelled_in_shop`:** zgłoszenie mówi o „ostrzeżeniu przy przywróceniu w ERP”.
  Spec pokazuje je dopiero po akcji. Nie wiadomo, czy potwierdzenie „Przywróć zamówienie” ma
  wspominać, że w sklepie zamówienie jest anulowane.
- **UI → Szczegóły:** „nad danymi zamówienia (tabela w »Szczegóły zamówienia«)”. Strona ma sekcje
  „Klient”, „Zamówienie”, „Realizacja”, więc odwołanie do „tabeli” jest niejasne.

### Invented or Unsupported Requirements

- Limit 300 żądań/min/IP, limit treści 1 MB (`413`), numer `WC-{ExternalId}`, nazwa „Klient sklepu
  #{id}”, polskie etykiety statusów sklepu i znacznik `orders.ShopDeletions` to decyzje techniczne
  autora. Są potrzebne i opisane w Assumptions (poza nazwą zastępczą klienta i etykietami), ale nie
  zostały potwierdzone przez właściciela. Numer i etykiety widzi użytkownik.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Scope; Endpoints (`/webhooks/woocommerce/orders`) | conventions.md „Naming” (wszystkie trasy pod `/api`, `MapGroup("/api/{area}")`) | Uzasadnione odstępstwo, ale niezapisane jako wyjątek od konwencji |
| Przyjęcie webhooka, pkt 1 (`Integrations:WooCommerce:WebhookSecret`) | `docs/wdrozenia.md` sekcja 4, `deploy/api.env.example` (klucze pod `StoneFlow:`) | Inny układ kluczy konfiguracji |
| Przyjęcie webhooka, pkt 6 (ponowienie po `503`) | SF-2 (`01-weryfikacja.md`): wyłączenie po kolejnych niepowodzeniach, utracone usunięcie wymaga decyzji właściciela | Niepotwierdzone ponowienia; ryzyko utraty usunięcia nienazwane |
| Zastosowanie snapshotu, pkt 2–3 (`order.restored`) | PRD sekcja 5 „Zamówienie WooCommerce” („Dane starsze od zapisanych … są pomijane”) | Starsze przywrócenie nie jest pomijane względem usunięcia |
| Out of scope vs „Reguły w domenie” | ta sama spec | Sprzeczność co do reguły „liczy się do sprzedaży” |

Brak sprzeczności z zapisanymi decyzjami (D-02, D-05, D-06; tylko PLN potwierdzone przez
właściciela). Otwarte D-08 i D-09 nie blokują tego obszaru.

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | OK | Brak wpłat i wypływów; zwrot w sklepie to kwota informacyjna; „Do zwrotu X zł” zostaje bez zmian. |
| 2. Backend decimal money | OK | `decimal`, kultura niezmienna, `decimal(18,2)`. Reguła dla innej liczby miejsc nieokreślona (Non-Critical 11). |
| 3. API-enforced access | OK | Anonimowy endpoint z obowiązkowym HMAC i porównaniem w czasie stałym, wpis w `EndpointSecurityTests`, uzasadnienie w spec. Ping bez podpisu niczego nie zmienia. `/api/orders` z sesją. |
| 4. Retries never duplicate | OK | Unikalność (`Source`, `ExternalId`) i `Number`, ponowienie w nowym zakresie DI, przejście tylko przy zmianie aktywne → zakończone. Luki w kolejności (`order.restored`, wyścig znacznika) mogą dać błędny stan sklepu, a ponowne anulowanie tylko w złożonym, mało prawdopodobnym przebiegu (Non-Critical 1, 4). |
| 5. Financial audit trail | OK | `Created`/`Updated` z `WooCommerceSync` w jednej jednostce pracy; `shopStatus`, `shopDeleted`, `shopRefundedAmount` w audycie; `ShopModifiedAtUtc` i `ShopDeletions` to dane techniczne, nie finansowe. |
| 6. Secrets stay server-side | OK | Sekret tylko w konfiguracji serwera, bez logowania podpisu i treści; brak wywołań do sklepu. |
| 7. Module and layer boundaries | OK | Kontrakt w `Shared.Application`, Integrations nie dotyka tabel Orders, reguły w `Order.ApplyFromShop`. |
| 8. No identifying data in docs | OK | Fikcyjne dane, bez nazwy i domeny sklepu. |

## Risk

- Recorded: high
- Assessed: high. Webhooki zmieniają wartość sprzedaży i status anulowania, zapisują dane osobowe,
  a endpoint jest anonimowy. Weryfikacja SF-2 (payload usunięcia, kolejność, ping, ponowienia)
  jest niezakończona, a właściciel ma tylko produkcję. Przed merge wymagany przegląd przez
  człowieka.

## Assumptions Detected

| Założenie | Stwierdzone w spec? |
|---|---|
| Budowa według dokumentacji WooCommerce; integracja wyłączona bez sekretu | Tak |
| Źródło danych to podpisany payload, bez REST | Tak |
| Mapowanie `billing`, NIP z `_billing_billing_nip`, `total`, data w Europe/Warsaw | Tak |
| Tylko PLN (potwierdzone przez właściciela), log przy innej walucie | Tak |
| Nieznany status = aktywny i nieukryty | Tak (decyzja autora) |
| Ping niepodpisany, bez nagłówka tematu | Tak (do potwierdzenia w SF-2) |
| `order.deleted` bez daty; czas usunięcia = czas odbioru | Tak |
| Różnica zegarów sklepu i serwera | Częściowo: tylko jeden kierunek (Non-Critical 10) |
| `order.restored` zawsze późniejsze od usunięcia | **Nie**: ukryte w regule pkt 2–3 (Non-Critical 1) |
| WooCommerce ponawia dostawę po `503` | **Nie** w Assumptions; tylko w Behavior pkt 6, niepotwierdzone (Non-Critical 5) |
| Kolejność dostaw niegwarantowana | Tak, pośrednio (znacznik, reguła kolejności) |
| Rozdzielczość sekundowa `date_modified_gmt`, równa data stosowana | Częściowo (Edge Cases), ryzyko niewymienione |
| Payload REST v3 i ten sam sekret dla czterech webhooków | **Nie** (Non-Critical 8) |
| Blokada po `ExternalId` serializuje także ścieżkę bez rekordu | **Nie**: nieprawdziwe dla `UPDLOCK, ROWLOCK` (Non-Critical 4) |
| Numer `WC-{ExternalId}` | Tak |
| Brak `billing` → „Klient sklepu #{id}” | Tylko Edge Cases |
| Szkic bez dat nie trafia do ERP | Tylko przykład w Behavior pkt 5 |
| Endpoint poza `/api`, sklep woła backend bezpośrednio | Tak (bez wskazania odstępstwa od konwencji) |
| `refunded` nie anuluje | Tak |
| Daty `_gmt` są UTC | Tak (do potwierdzenia w SF-2) |
| Zapis samego `ShopModifiedAtUtc` zmienia `rowversion` i daje `409` użytkownikowi | Tak |
| Limit 300/min/IP | Tak |

## Recommendation

- Proceed as-is. `sdd-plan` może ruszyć. Non-Critical 1–5 (reguła `order.restored`, temat
  w `ShopOrderSnapshot`, wyniki portu i tłumaczenie naruszeń unikalności, serializacja znacznika
  z utworzeniem, założenie o ponowieniach) trzeba rozstrzygnąć w planie albo krótką poprawką spec
  przed implementacją. Pozostałe uwagi można domknąć w planie i dokumentacji.
