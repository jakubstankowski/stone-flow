# Spec review — SF-12

## Summary

- Overall assessment: FAIL
- Justification: Specyfikacja jest szczegółowa i w większości zgodna z PRD, zgłoszeniem i kodem
  (encja `Order`, unikalny indeks, `AuditAuthor.WooCommerceSync`, `ILaunchDate`, `SalesReductionRules`,
  CSRF tylko pod `/api`, fallback policy). Reguły dla `order.deleted` są jednak niespójne:
  `order.deleted` nie zmienia `ShopStatus`, a przejście na „Anulowane” sprawdza tylko zapisany status
  sklepu. Ponowne dostarczenie usunięcia anuluje więc zamówienie przywrócone w ERP (niezmiennik 4,
  ZAM-02/06). Dostawa bez daty modyfikacji psuje też regułę kolejności: spóźniona starsza dostawa
  cofa oznaczenie usunięcia, a usunięcie, które dotrze przed utworzeniem, przepada. Do tego dochodzi
  kilka niezgodności z kodem i konwencjami. Nie blokują one planowania same w sobie, ale trzeba je
  doprecyzować.

## Findings

### Critical Issues

1. **Ponowne dostarczenie `order.deleted` ponownie anuluje zamówienie** (Behavior → „Zastosowanie
   snapshotu”, pkt 3 „Jednorazowe przejście na »Anulowane«” i „Usunięcie w sklepie”; „Reguły
   w domenie”). Według spec `order.deleted` „nie zmienia pozostałych pól”, więc `ShopStatus`
   zostaje np. `processing`. Przejście działa, „gdy zapisany status sklepu nie był anulowany ani
   usunięty”, a `IsDeleted(status)` to wyłącznie `trash`. Przebieg: `order.deleted` → „Anulowane”
   → użytkownik przywraca w ERP → ta sama dostawa przychodzi ponownie (ponowienie WooCommerce albo
   drugie zdarzenie przy trwałym usunięciu z kosza). Zapisany status nadal nie jest anulowany ani
   `trash`, więc ERP znowu ustawia „Anulowane”. To łamie kryterium „Ponowne dostarczenie tego
   samego statusu nie zmienia statusu realizacji” i niezmiennik 4 („A retry does not overwrite
   ERP-owned fields”). Spec musi jednoznacznie powiedzieć, czy stan „usunięty” to `ShopDeletedAtUtc`
   albo `ShopStatus = trash`, i że przejście wywołuje tylko zmiana tego stanu względem zapisanego.
2. **Kolejność dostaw przy usunięciu nie jest zdefiniowana** (Behavior → „Zastosowanie snapshotu”,
   pkt 2 i 3; Edge Cases). `order.deleted` nie ma daty i jest „stosowane zawsze”, ale nie zmienia
   `ShopModifiedAtUtc`. Skutki:
   - spóźniona wcześniejsza dostawa `order.updated` (data ≥ zapisanej, status inny niż `trash`)
     przechodzi kontrolę kolejności, czyści `ShopDeletedAtUtc` i nadpisuje status sklepu, więc
     pojawia się fałszywe „Zamówienie aktywne w sklepie”. PRD sekcja 5 mówi, że „dane starsze od
     zapisanych … są pomijane”, a ERP ma pokazywać usunięcie w sklepie;
   - `order.deleted` dla zamówienia, którego jeszcze nie ma, daje „nic”. Gdy spóźnione
     `order.created` dotrze później, powstaje aktywne zamówienie „W trakcie”, które liczy się do
     sprzedaży, choć w sklepie go nie ma. SF-13 tego nie naprawi: REST nie zwraca trwale usuniętych
     zamówień (SF-2: „Odtworzenie utraconego usunięcia … brak rekordu nie wystarcza”).

   SF-2 nie potwierdziło kolejności zdarzeń ani payloadu usunięcia, więc spec musi przyjąć
   bezpieczną regułę: jak dostawa z datą ma się do znacznika usunięcia, czy usunięcie zostawia ślad
   dla nieznanego zamówienia, i jakie są granice tej reguły. Rozstrzygnięcie wymagające zmiany
   PRD (np. akceptacja ryzyka) należy do właściciela.

### Non-Critical Issues

1. **Ping prawdopodobnie nie jest podpisany** (Behavior → „Przyjęcie webhooka”, pkt 3). Spec
   zakłada, że ping (`webhook_id=<n>`) przychodzi z podpisem, a ping bez podpisu dostaje `401`.
   W kodzie WooCommerce (`WC_Webhook::deliver_ping`) ping wysyła, o ile mi wiadomo, tylko treść
   i User-Agent, bez `X-WC-Webhook-Signature`, a WooCommerce oczekuje dokładnie `200`. Ani SF-2,
   ani spec tego nie potwierdzają. Jeśli tak jest, aktywacja webhooka w panelu sklepu zawsze
   pokaże błąd `401`. Ping nic nie zmienia w danych, więc bezpieczna jest odpowiedź `200` bez
   działania niezależnie od podpisu. Potrzebna decyzja i źródło, ewentualnie test w próbie SF-2.
2. **Ponowna próba po naruszeniu unikalności w tym samym zakresie DI** (Behavior → „Zastosowanie
   snapshotu”, pkt 2, „Równoczesne utworzenie”). `conventions.md` („Data access”) mówi: „after a
   rolled-back unit of work the scope does no further writes”. `OrderEntryWriter` po wycofaniu
   tylko czyta. Spec musi określić, że ponowna próba jako aktualizacja biegnie w nowym zakresie
   (nowy `DbContext`, `ModuleConnection`) albo wraca `503`, żeby ponowił WooCommerce. Musi też
   uwzględnić, że pierwszy może zadziałać unikalny indeks na `Number` (`WC-{id}`), a nie
   (`Source`, `ExternalId`).
3. **Lista nie ma statusu ze sklepu** (Behavior → „Lista zamówień”; Data / API → Endpoints). Spec
   pisze o „istniejącej kolumnie/etykiecie statusu sklepu” i „bez zmian kształtu” `GET /api/orders`.
   W kodzie `OrderListItemDto` nie ma `ShopStatus`, a `COLUMNS` w `orders-list-page.ts` nie ma
   takiej kolumny. Albo trzeba dodać pole do DTO i kolumnę lub etykietę na liście, albo usunąć to
   wymaganie. Do tego statusy sklepu nie mają polskich etykiet: szczegóły pokazują surowy kod
   (`order-details-page.html`: `{{ order.shopStatus }}`). Spec nie mówi, czy UI pokazuje
   `processing`, czy tłumaczenie.
4. **Zapis i audyt `ShopModifiedAtUtc` i `ShopRefundedAmount` nie są określone** (Behavior →
   „Zastosowanie snapshotu”, pkt 3, audyt). Spec wymienia w audycie „w tym `shopStatus`
   i `shopDeleted`” i mówi „brak zmian → brak audytu”. Nie mówi:
   - czy sama zmiana `ShopModifiedAtUtc` (dostawa bez zmian mapowanych pól) jest zapisywana. Musi
     być, inaczej reguła kolejności dopuści później starszą dostawę;
   - że taki zapis zmienia `rowversion` i daje użytkownikowi `409` mimo braku widocznej zmiany;
   - czy `shopRefundedAmount` jest w audycie (kwota, choć informacyjna).
5. **`cancelled_with_payments` dubluje istniejące ostrzeżenie** (Behavior → „Szczegóły zamówienia”).
   DESIGN-GUID (sekcja szczegółów zamówienia) i kod (`order-payments.html`) mają już „Do zwrotu
   X zł” dla anulowanego zamówienia z Zapłacono > 0, a dialog anulowania ostrzega o wpłatach. Nowy
   kod jest liczony z „sumy wpłat”, a nie z Zapłacono, i obejmuje też anulowania ręczne. Trzeba
   określić relację obu ostrzeżeń, np. tylko przy anulowaniu przez synchronizację albo bez dublowania.
6. **Styl alertów niezgodny z istniejącym wzorcem** (UI; „Szczegóły zamówienia”). Spec podaje
   `alert-warning`/`alert-info` daisyUI i `role="status"`. Istniejące ostrzeżenia w szczegółach
   używają `rounded-box border border-(--sf-warning) bg-base-100`. Warto zostać przy jednym wzorcu.
7. **Brak ochrony anonimowego endpointu przed zalewem** (Behavior → „Przyjęcie webhooka”, pkt 2).
   Każde żądanie ze złym podpisem daje log ostrzeżenia. `docs/wdrozenia.md` sekcja 8: logi
   `stdout_*.log` na dysku hostingu bez rotacji. Brak limitu (rate limiter jest w potoku, polityki
   istnieją tylko dla logowania i resetu) ani limitu długości logowanego `X-WC-Webhook-Delivery-ID`
   (wartość od klienta).
8. **Konfiguracja sekretu** (Behavior → „Przyjęcie webhooka”, pkt 1). Nie wiadomo, co się dzieje,
   gdy sekret ma mniej niż 16 znaków (`404`? błąd startu?). Spec nie podaje miejsca dokumentacji
   (sekcja `docs/wdrozenia.md`, `deploy/api.env.example`). Brakuje też ostrzeżenia, że `404`, `401`,
   `413` i `503` liczą się do wyłączenia webhooka przez WooCommerce po kolejnych niepowodzeniach
   (SF-2), więc skonfigurowany webhook bez sekretu w ERP szybko się wyłączy.
9. **„Czytelna treść” nie ma listy pól** (Behavior → „Przyjęcie webhooka”, pkt 5; „Mapowanie
   payloadu”). Spec mówi „brak wymaganych pól”, ale nie wymienia ich (`date_created_gmt`,
   `date_modified_gmt`, `status`, `total`?). Dla `checkout-draft` daty mogą być puste, a wtedy
   szkice wpadną w „nieczytelne” zamiast w ukryte statusy.
10. **Waluta** (Mapowanie payloadu; Out of scope). Właściciel odpowiedział „mamy tylko pln!!!”
    i spec to zapisuje. SF-2 widziało jednak włączone multi-currency w WooPayments. Nie
    zmieniając decyzji, warto dodać tani bezpiecznik: log ostrzeżenia, gdy `currency` ≠ `PLN`.
    Kwota w obcej walucie trafiłaby do sprzedaży jako złotówki.
11. **Nieznany status sklepu liczy się do sprzedaży** (Reguły w domenie; Assumptions). To decyzja
    autora, nie właściciela, a D-02 każe sprawdzić statusy niestandardowe w sklepie. Warto
    przynajmniej logować nieznany status, żeby próba SF-2 lub eksploatacja go wykryła.
12. **Anonimizacja (SF-22)** (Out of scope). PRD: „Synchronizacja nie przywraca danych klienta
    zanonimizowanego zamówienia”. SF-12 nadpisuje dane klienta przy każdej dostawie. Spec powinna
    zostawić ślad zależności dla SF-22 (punkt rozszerzenia w `ApplyFromShop`).
13. **NIP z meta bez normalizacji** (Mapowanie payloadu). `Customer` oczekuje NIP „z samych cyfr”
    (`Customer.cs`). Spec nie mówi, co z myślnikami, spacjami czy prefiksem `PL`, ani że NIP ze
    sklepu nie jest walidowany sumą kontrolną.
14. **Pusty stan listy** (UI → Lista). Gdy istnieją tylko zamówienia ukryte domyślnie, lista
    bez filtrów pokaże „Brak zamówień” z akcją dodania (`orders-list-page.ts`), choć rekordy są.
    Spec mówi „stany listy bez zmian”.
15. **`CountsTowardSales` bez użycia** (Reguły w domenie; Out of scope). Reguła powstaje dla SF-19,
    a w SF-12 nikt jej nie używa. To dyskusyjne wobec zasady „abstractions come from real need”.
    Do rozważenia, czy nie przenieść jej do SF-19.

### Unclear or Ambiguous Sections

- **Behavior → „Zastosowanie snapshotu”, pkt 3:** „anulowany ani usunięty” (status `trash` czy
  `ShopDeletedAtUtc`?), zob. Critical 1.
- **Behavior → „Zastosowanie snapshotu”, pkt 1:** „blokowane … (`UPDLOCK`), tak jak wpisy w SF-9”.
  Istniejące `IOrderLock.LockAsync` blokuje po `Id` i zwraca `LockedOrder` bez encji. Spec nie mówi,
  czy powstaje nowy port (blokada po `ExternalId` i wczytanie encji z wersją), ani że blokada na
  nieistniejącym wierszu niczego nie chroni (dlatego potrzebna jest ścieżka unikalności).
- **Behavior → „Zastosowanie snapshotu”, pkt 3:** „Data uruchomienia nie jest sprawdzana ponownie”.
  Nie wiadomo, co się dzieje, gdy sklep zmieni datę istniejącego zamówienia na sprzed uruchomienia.
- **Lista zamówień:** nazwa parametru w adresie strony (frontend) dla przełącznika nie jest podana.
- **Szczegóły → `active_in_shop`:** przy statusach `pending`/`failed` zamówienie anulowane w ERP
  też dostanie „aktywne w sklepie”. To prawdopodobnie zamierzone, ale nie jest powiedziane.
- **Edge Cases → „Dwie dostawy tej samej daty modyfikacji, różne dane → druga stosowana”:**
  `date_modified_gmt` ma rozdzielczość sekundy. Przy odwróconej kolejności w tej samej sekundzie
  wygrywa starsza treść. To ryzyko jest akceptowane milcząco, a nie w Assumptions.

### Invented or Unsupported Requirements

- Kod `cancelled_with_payments` z tekstem „Zamówienie ma wpłaty — pozostają zapisane” ma podstawę
  w PRD („jeżeli zamówienie ma wpłaty, użytkownik widzi ostrzeżenie”). Warunek (wszystkie anulowane
  zamówienia, suma wpłat zamiast Zapłacono) i nowe ostrzeżenie obok „Do zwrotu” to jednak decyzja
  autora (Non-Critical 5).
- Nazwa klienta „Klient sklepu #{id}” przy braku `billing`, numer `WC-{ExternalId}` i limit 1 MB /
  `413` to decyzje techniczne autora. Są potrzebne i rozsądne, ale nie wynikają z wejść. Numer jest
  widoczny dla użytkownika i nie został potwierdzony przez właściciela. Nazwa klienta zastępczego
  nie jest w Assumptions.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Behavior → „Zastosowanie snapshotu” (usunięcie, przejście na „Anulowane”) | PRD sekcja 5 „Zamówienie WooCommerce”; ZAM-02/06; rules.md niezmiennik 4 | Ponowne `order.deleted` ponownie anuluje; spóźniona starsza dostawa cofa usunięcie (Critical 1–2) |
| Behavior → „Zastosowanie snapshotu”, pkt 2 (ponowna próba) | conventions.md „Data access” (brak zapisów w zakresie po wycofaniu) | Ponowna próba w tym samym zakresie DI |
| Behavior → „Lista zamówień”; Endpoints `GET /api/orders` | Kod: `OrderListItemDto`, `orders-list-page.ts` | „Istniejąca kolumna statusu sklepu” nie istnieje; kształt odpowiedzi musi się zmienić |
| Behavior → „Szczegóły zamówienia” (`cancelled_with_payments`, styl alertów) | DESIGN-GUID (lista ostrzeżeń w szczegółach zamówienia); `order-payments.html` | Dublowanie „Do zwrotu X zł”; inny wzorzec wizualny |
| Behavior → „Przyjęcie webhooka”, pkt 3 (ping) | Zachowanie WooCommerce (`deliver_ping`), SF-2 bez potwierdzenia | Prawdopodobnie ping bez podpisu → `401` przy aktywacji |

Brak sprzeczności z zapisanymi decyzjami (D-02, D-05, D-06), jeśli Critical 2 zostanie rozwiązany
bez zmiany PRD. Jeśli nie, potrzebna jest decyzja właściciela.

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | OK | Brak wpłat i wypływów; zwrot w sklepie to tylko kwota informacyjna. |
| 2. Backend decimal money | OK | `decimal` z kulturą niezmienną, `decimal(18,2)`. |
| 3. API-enforced access | OK | Anonimowy endpoint z obowiązkowym HMAC, wpis w `EndpointSecurityTests`. CSRF dotyczy tylko `/api` (zgodne z `CsrfValidationMiddleware`), fallback policy wymaga `AllowAnonymous`. |
| 4. Retries never duplicate | Violated | Przy dosłownym czytaniu reguł ponowne `order.deleted` nadpisuje status realizacji przywrócony w ERP (Critical 1). Kolejność przy usunięciu nie jest chroniona (Critical 2). |
| 5. Financial audit trail | OK | `Created`/`Updated` z `WooCommerceSync` w jednej jednostce pracy, dane osobowe oznaczone. Do doprecyzowania: audyt `shopRefundedAmount` (Non-Critical 4). |
| 6. Secrets stay server-side | OK | Sekret tylko w `appsettings.Production.json`, nie w logach ani odpowiedziach; brak wywołań do sklepu. |
| 7. Module and layer boundaries | OK | Kontrakt w `Shared.Application` (zgodnie z conventions.md), Integrations nie dotyka tabel Orders, reguły w domenie. |
| 8. No identifying data in docs | OK | Fikcyjne dane, bez nazwy i domeny sklepu. |

## Risk

- Recorded: high
- Assessed: high. Webhooki zmieniają wartość sprzedaży i status anulowania, zapisują dane osobowe,
  a endpoint jest anonimowy. Przed merge wymagany przegląd przez człowieka. Ryzyko podnosi też
  niezakończona weryfikacja SF-2 (payload usunięcia, kolejność, ping).

## Assumptions Detected

| Założenie | Stwierdzone w spec? |
|---|---|
| Budowa według dokumentacji WooCommerce; integracja wyłączona bez sekretu | Tak (Assumptions) |
| Źródło danych to podpisany payload webhooka, bez REST | Tak |
| Mapowanie klienta z `billing`, NIP z `_billing_billing_nip`, wartość `total`, data z `date_created_gmt` | Tak |
| Tylko PLN, `currency` ignorowane | Tak (odpowiedź właściciela) |
| Nieznany status = aktywny | Tak (decyzja autora) |
| Numer `WC-{ExternalId}` | Tak (decyzja autora, bez potwierdzenia właściciela) |
| Endpoint poza `/api`, Vercel go nie przepisuje, sklep woła API bezpośrednio | Tak (zgodne z `docs/wdrozenia.md`) |
| Nieczytelna treść → `200` + log | Tak |
| Ping jest podpisany | **Nie**: ukryte i prawdopodobnie błędne (Non-Critical 1) |
| `order.deleted` zawiera tylko `id` i nie ma daty modyfikacji | Częściowo (Behavior), bez konsekwencji dla kolejności (Critical 2) |
| Kolejność zdarzeń WooCommerce nie jest gwarantowana | **Nie** wprost; SF-2 to zaznacza |
| Rozdzielczość `date_modified_gmt` to sekunda; równe daty stosowane | Częściowo (Edge Cases), ryzyko niewymienione |
| Ponowna próba po naruszeniu unikalności w tym samym zakresie DI jest dozwolona | **Nie** (Non-Critical 2) |
| Lista ma już kolumnę statusu sklepu | **Nie**, i jest nieprawdziwe (Non-Critical 3) |
| Brak `billing` → „Klient sklepu #{id}” | **Nie** (tylko Edge Cases) |
| Firma bez NIP dozwolona; brak walidacji formularza dla zamówień ze sklepu | Tak |
| `refunded` nie anuluje | Tak |
| Daty `_gmt` są UTC | Tak (do potwierdzenia w SF-2) |
| Zapis tylko `ShopModifiedAtUtc` zmienia `rowversion` | **Nie** (Non-Critical 4) |

## Recommendation

- Revise specification. Wystarczy doprecyzować reguły usunięcia i kolejności (Critical 1–2)
  i odnieść się do Non-Critical 1–4. Pozostałe uwagi można rozstrzygnąć w planie.
