# SF-2 — weryfikacja WooCommerce

Data: 02.11.2026. Status: analiza dokumentacji i odczyt konfiguracji sklepu;
bez testów operacji zmieniających zamówienia lub webhooki.
Zgłoszenie: https://standev.youtrack.cloud/issue/SF-2

## Wyniki i granice weryfikacji

| Obszar | Wynik dokumentacji | Weryfikacja sklepu |
|---|---|---|
| Kosz i trwałe usunięcie | REST v3 opisuje status `trash` oraz DELETE z `force=true` | Nie wykonano; potrzebna kopia testowa i testowe zamówienia |
| Odtworzenie utraconego usunięcia | Odczyt zmian nie jest udokumentowanym dziennikiem usunięć; brak rekordu nie wystarcza do ustalenia przyczyny | Nie wykonano; nie potwierdzono, że nocne uzupełnianie spełni ZAM-02 |
| Payload usunięcia i kolejność zdarzeń | Nie potwierdzono obecności daty modyfikacji w zdarzeniu usunięcia | Potrzebny surowy payload i nagłówki z testu, bez danych osobowych i sekretów |
| Statusy niestandardowe i szkice | Dokumentacja standardowych statusów nie dowodzi konfiguracji sklepu | Nie sprawdzono wersji WooCommerce, HPOS, rozszerzeń i własnych statusów |
| Niedostępność odbiorcy | WooCommerce może wyłączyć webhook po kolejnych błędach; dokumentacje różnią się opisem progu | Potrzebna próba kontrolowanego błędu na osobnym testowym webhooku |
| Podpis | `X-WC-Webhook-Signature`: Base64 HMAC-SHA256 payloadu | Nie sprawdzono poprawnego i błędnego podpisu na faktycznym payloadzie |
| Uzupełnianie zmian | REST v3 udostępnia `modified_after`, `modified_before` i paginację | Nie sprawdzono granic, kolejności, kosza i kompletności stron na sklepie |
| Adres i NIP | API ma `billing`, `shipping`, `meta_data`; nie ustalono klucza NIP | Mapowanie adresu rozliczeniowego i dostawy oraz źródło NIP wymagają sprawdzenia |
| Daty | `date_created` i `date_modified` są w strefie sklepu; warianty `_gmt` są GMT | Nie ustalono strefy sklepu ani zachowania na granicy miesiąca/startu i zmianie czasu |

## Źródła

- [REST v3 — Orders](https://developer.woocommerce.com/docs/apis/rest-api/v3/orders/)
- [Bieżąca instrukcja webhooków](https://woocommerce.com/document/webhooks/)
  opisuje wyłączenie po więcej niż pięciu kolejnych niepowodzeniach i wyjątki
  dla odpowiedzi 2xx, 301 i 302.
- [Dokumentacja webhooków v2](https://developer.woocommerce.com/docs/apis/rest-api/v2/webhooks)
  opisuje pięć niepowodzeń i odpowiedzi inne niż 2xx oraz nagłówek podpisu.
  Nie należy rozstrzygać tej rozbieżności bez ustalenia wersji i testu sklepu.

## Test do wykonania po udostępnieniu środowiska

### Uzupełnienie po udostępnieniu panelu przez właściciela

Właściciel potwierdził, że ma tylko produkcję. Dostęp do zalogowanego panelu działa.
Odczytano bez zapisywania ustawień:

- WooCommerce 11.0.1, WordPress 7.1.4, typ środowiska `production`.
- HPOS włączone, magazyn `OrdersTableDataStore`; synchronizacja HPOS wyłączona.
- Strefa WordPress: Warszawa. Wariant `_gmt` nadal wymaga testu na granicach dat.
- Waluta podstawowa PLN; raport WooPayments pokazuje włączone multi-currency.
  Nie dowodzi to występowania zamówień w innych walutach, ale wymaga sprawdzenia
  wobec założenia PRD o wyłącznej sprzedaży w PLN.
- W konfiguracji Flexible Checkout Fields istnieje NIP typu Liczba z nazwą meta
  `_billing_billing_nip`. Nie potwierdzono obecności tej meta w odpowiedzi REST.
- Ta wtyczka pokazuje ostrzeżenie o braku działania z domyślnym checkoutem;
  raport systemu wskazuje checkout blokowy. Samo istnienie pola nie potwierdza,
  że NIP jest zbierany przy obecnym formularzu zamówienia.
- WP Cron jest włączony. Logowanie WooCommerce ma retencję 30 dni.

Nie kopiowano danych klientów, sekretów, identyfikatorów kont płatniczych ani
pełnego raportu do dokumentacji. Statusów niestandardowych nie potwierdzono.
Testów kosza, trwałego usunięcia i awarii webhooka nie wykonano: brak kopii testowej.
Powyższe ustalenia zastępują wcześniejszą informację o braku dostępu do panelu;
pozostałe pozycje macierzy nadal wymagają testów.

1. Zapisać wersję WooCommerce, HPOS, strefę, rozszerzenia NIP i statusów.
2. Na syntetycznym zamówieniu odebrać created/updated, kosz, przywrócenie,
   a następnie trwałe usunięcie. Dla każdego kroku porównać REST i webhook,
   daty, statusy oraz zachowanie po pominięciu webhooka.
3. Na osobnym testowym webhooku sprawdzić awarię odbiorcy, liczbę dostarczeń,
   przejście w disabled i uzupełnienie danych po przywróceniu.
4. Sprawdzić paginację i granice dat, w tym dwa zamówienia z tą samą datą
   modyfikacji, przełom miesiąca, dzień uruchomienia i zmianę czasu.
5. Zapisać mapowanie pól na podstawie danych syntetycznych; zweryfikować podpis
   na surowych bajtach oraz odrzucenie zmienionego payloadu.

Potrzebna kopia testowa sklepu do prób zmieniających dane. Nie wykonywano operacji na
zamówieniach produkcyjnych. Nie udowodniono spełnienia kryteriów akceptacji.
Jeśli utraconego usunięcia nie da się odtworzyć, wymagana jest decyzja właściciela
o mechanizmie wykrywania lub zmianie wymagania, aktualizacja PRD i ponowny przegląd.
D-02 i reguły PRD pozostawiono bez zmian do czasu otrzymania dowodów.
