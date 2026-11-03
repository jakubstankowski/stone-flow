---
source_review: project-review.md
created: 2026-11-01
---

# Task breakdown — Stone Flow

| # | Title | Risk | Depends on | Tracker ID |
|---|---|---|---|---|
| 1 | Weryfikacja hostingu i wersji technologii (D-08) | high | — | SF-1 |
| 2 | Weryfikacja zachowania WooCommerce na sklepie | high | — | SF-2 |
| 3 | Monorepo, CI i szkielet modułów backendu | medium | 1 | SF-3 |
| 4 | Automatyczne wdrożenia i kontrolowane migracje | high | 1, 3 | SF-4 |
| 5 | Motyw daisyUI i powłoka aplikacji | medium | 3 | SF-5 |
| 6 | Logowanie, reset hasła, sesja i ochrona API | high | 3, 4, 5 | SF-6 |
| 7 | Zapis audytu zmian finansowych | high | 3, 6 | SF-7 |
| 8 | Zamówienia ręczne, wspólna lista, anulowanie i przywracanie | high | 6, 7 | SF-8 |
| 9 | Wpłaty, zwroty i wyliczany stan płatności | high | 8 | SF-9 |
| 10 | Korekta sprzedaży i limit obniżeń | high | 9 | SF-10 |
| 11 | Trwały harmonogram zadań | medium | 1, 3 | SF-11 |
| 12 | Import zamówień WooCommerce | high | 2, 4, 10 | SF-12 |
| 13 | Uzupełnianie luk, historia błędów i ponowienia | high | 11, 12 | SF-13 |
| 14 | Stan integracji w UI | medium | 13 | SF-14 |
| 15 | Wydatki jednorazowe i zapłata wydatku | high | 6, 7 | SF-15 |
| 16 | Wydatki cykliczne | high | 11, 15 | SF-16 |
| 17 | Wypłaty właścicieli i lista „Operacje” | high | 9, 15 | SF-17 |
| 18 | Lista „Historia zmian” | high | 8 | SF-18 |
| 19 | Reguły raportowe i widok „Sprzedaż” | high | 10, 12, 14, 16 | SF-19 |
| 20 | Widok „Przepływy pieniężne” | high | 17, 19 | SF-20 |
| 21 | Kopie zapasowe, procedura powrotu i odbiór MVP | high | 4, 18, 20 | SF-21 |
| 22 | Anonimizacja danych klienta | high | 12, decyzja D-09 | SF-22 |

---

## 1. Weryfikacja hostingu i wersji technologii (D-08)

**Title**

```
Weryfikacja hostingu i wersji technologii (D-08)
```

**Description**

```markdown
## Context
Od planu SMART ASP.NET zależy wersja .NET, SQL Server oraz uruchamianie zadania o 23:00 i wydatków cyklicznych przy usypianiu procesu. PRD sekcje 10, 12 (D-08) i 13 stawiają tę weryfikację przed pracami.

## Scope
- Wybór planu SMART ASP.NET i potwierdzenie obsługi najnowszego stabilnego .NET, SQL Server i trwałych zadań.
- Sprawdzenie, czy i jak hosting usypia lub restartuje proces oraz jak można wyzwalać zadania.
- Potwierdzenie najnowszych stabilnych wersji .NET, Angular, Tailwind CSS i daisyUI oraz ich wzajemnej zgodności.
- Ustalenie harmonogramu i retencji kopii zapasowych, RPO, RTO, wolumenu danych i celów wydajnościowych.
- Zapisanie wyniku w dokumentacji i zamknięcie D-08 w PRD.

## Acceptance criteria
- [ ] D-08 ma zapisaną decyzję: plan, parametry kopii, dostępności i wydajności.
- [ ] Zapisano konkretne wersje technologii i ich zgodność z planem.
- [ ] Zapisano sposób uruchamiania zadań automatycznych odporny na restart i usypianie.
- [ ] Jeśli plan nie obsługuje wymaganego .NET, zapisano decyzję o sposobie wdrożenia lub zmianie planu.

## Out of scope
- Konfiguracja środowisk i wdrożenia (zadanie 4).
- Implementacja harmonogramu (zadanie 11).
```

**Metadata**

```
Breakdown ID: 1
Risk: high — human review required before merge
Depends on: none
PRD sections: 10, 12 (D-08), 13
```

---

## 2. Weryfikacja zachowania WooCommerce na sklepie

**Title**

```
Weryfikacja zachowania WooCommerce na sklepie
```

**Description**

```markdown
## Context
Przegląd projektu oznaczył kilka założeń o WooCommerce jako niesprawdzone na sklepie. Najpoważniejsze: trwale usuniętego zamówienia REST API prawdopodobnie nie zwróci, więc utracony webhook zostawiłby je w sprzedaży bez błędu, co przeczyłoby ZAM-02. Wyniki mogą wymagać decyzji i zmiany PRD, dlatego zadanie poprzedza import. PRD sekcje 5 „Zamówienie WooCommerce” i 12 (D-02).

## Scope
- Sprawdzenie, jak REST API i webhooki obsługują usunięcie trwałe i przeniesienie do kosza, w tym czy wiadomość o usunięciu niesie datę modyfikacji.
- Sprawdzenie, czy sklep używa statusów niestandardowych (D-02).
- Sprawdzenie reakcji sklepu na niedostępność backendu, w tym czy może wyłączyć webhook.
- Sprawdzenie mechanizmu podpisu webhooków i odpytywania o zmiany.
- Ustalenie mapowania pól: adres, źródło NIP, strefa czasowa daty zamówienia na granicy miesiąca i daty uruchomienia.

## Acceptance criteria
- [ ] Każdy punkt ma zapisany wynik z testu na sklepie albo jawną informację, że nie dało się go sprawdzić.
- [ ] Jeśli usunięcia nie da się wykryć przez uzupełnianie, użytkownik podjął decyzję, a PRD ją zawiera.
- [ ] Mapowanie pól i statusów jest zapisane.
- [ ] Zmiany w PRD wynikające z testów przeszły ponowny przegląd.

## Out of scope
- Implementacja integracji (zadania 12 i 13).
```

**Metadata**

```
Breakdown ID: 2
Risk: high — human review required before merge
Depends on: none
PRD sections: 5 (Zamówienie WooCommerce), 12 (D-02)
```

---

## 3. Monorepo, CI i szkielet modułów backendu

**Title**

```
Monorepo, CI i szkielet modułów backendu
```

**Description**

```markdown
## Context
Wszystkie kolejne zadania potrzebują repozytorium, przypiętych wersji, CI i granic modułów. PRD sekcja 10.

## Scope
- Struktura monorepo: frontend, backend, testy, dokumentacja, konfiguracja wdrożeń; uzupełnienie TODO w `.sdd/context/conventions.md` i `role.md`.
- Przypięcie SDK, runtime i Node, lockfile, `global.json`, centralne wersje pakietów .NET.
- Angular strict i ASP.NET Core z OpenAPI, Problem Details i korelacją logów.
- Modułowy monolit: zamówienia, wydatki, raportowanie, integracje; warstwy Onion; EF Core z SQL Server i wersjonowane migracje.
- Health endpoint.
- Odczyt daty uruchomienia z konfiguracji wdrożenia, dostępny dla modułów, bez zmiany z poziomu aplikacji.
- CI: kompilacja obu aplikacji, typy, formatowanie, testy.
- Instrukcja startu i przykładowa konfiguracja bez sekretów.

## Acceptance criteria
- [ ] Projekt da się odtworzyć i uruchomić według instrukcji, z zapisanymi wersjami.
- [ ] CI przechodzi dla frontendu i backendu i blokuje błąd typów, formatowania lub testu.
- [ ] Domena nie zależy od HTTP, EF Core ani hostingu, a moduły nie sięgają do swoich encji.
- [ ] Health endpoint odpowiada.
- [ ] Data uruchomienia jest dostępna z konfiguracji.
- [ ] Repozytorium nie zawiera sekretów.

## Out of scope
- Publikacja na hostingi (zadanie 4).
- Motyw i powłoka UI (zadanie 5).
```

**Metadata**

```
Breakdown ID: 3
Risk: medium
Depends on: 1
PRD sections: 4 (Przełom uruchomienia), 10
```

---

## 4. Automatyczne wdrożenia i kontrolowane migracje

**Title**

```
Automatyczne wdrożenia i kontrolowane migracje
```

**Description**

```markdown
## Context
Webhook i sesję między domenami można sprawdzić tylko na prawdziwym hostingu, a przegląd projektu oznaczył migracje produkcyjne jako operacje nieodwracalne. Wdrożenia powstają wcześnie, by wykryć problemy hostingu przed budową funkcji. PRD sekcje 10 i 11.

## Scope
- Publikacja frontendu na Vercel z routingiem SPA i konfigurowalnym adresem API.
- Publikacja backendu na SMART ASP.NET po przejściu wymaganych kontroli.
- Kontrolowana kolejność migracji, obsługa błędu, sekrety poza repozytorium.
- Ograniczone uprawnienia do produkcji i sekretów wdrożeniowych.
- Izolacja preview od produkcyjnej bazy i integracji.
- HTTPS i monitoring dostępności.

## Acceptance criteria
- [ ] Oba wdrożenia uruchamiają się automatycznie po przejściu kontroli.
- [ ] Odświeżenie podstrony SPA nie powoduje błędu routingu.
- [ ] Nieudana migracja zatrzymuje publikację, a procedura postępowania jest opisana.
- [ ] Preview nie ma dostępu do produkcyjnej bazy ani integracji.
- [ ] Pakiet frontendu nie zawiera sekretów.

## Out of scope
- Kopie zapasowe i próba odtworzenia (zadanie 21).
```

**Metadata**

```
Breakdown ID: 4
Risk: high — human review required before merge
Depends on: 1, 3
PRD sections: 10, 11
```

---

## 5. Motyw daisyUI i powłoka aplikacji

**Title**

```
Motyw daisyUI i powłoka aplikacji
```

**Description**

```markdown
## Context
Każdy ekran korzysta z tych samych tokenów, nawigacji i stanów. Design guide sekcje 2, 3, 5, 6, 8, 9.

## Scope
- Tokeny CSS i jeden własny motyw daisyUI z obowiązkową paletą.
- Nagłówek i nawigacja: Pulpit, Zamówienia, Wydatki, Operacje, Historia zmian, Integracja, z leniwym ładowaniem tras.
- Wspólne elementy: przyciski, pola, dialog potwierdzenia, tabela z filtrami i paginacją, statusy, stany ładowania, pustki i błędu.
- Polskie formaty kwot i dat.
- Do rozstrzygnięcia w specyfikacji: zakres pozycji „Ustawienia”.

## Acceptance criteria
- [ ] Obowiązkowe cztery kolory są zapisane centralnie, a komponenty nie mają lokalnych kodów HEX.
- [ ] Użyte pary kolorów spełniają WCAG 2.2 AA, a status nie zależy tylko od koloru.
- [ ] Nawigacja działa klawiaturą i na telefonie, fokus jest widoczny, dialog zwraca fokus.
- [ ] Strona nie wymaga poziomego przewijania na telefonie.

## Out of scope
- Treść ekranów poszczególnych modułów.
- Ciemny motyw.
```

**Metadata**

```
Breakdown ID: 5
Risk: medium
Depends on: 3
PRD sections: 10; design guide 2, 3, 5, 6, 8, 9
```

---

## 6. Logowanie, reset hasła, sesja i ochrona API

**Title**

```
Logowanie, reset hasła, sesja i ochrona API
```

**Description**

```markdown
## Context
Jedno konto właściciela chroni dane finansowe i osobowe. Frontend i API są na osobnych domenach, więc sesję, CORS i CSRF trzeba zaprojektować jawnie. PRD sekcja 3, DOST-01.

## Scope
- Logowanie e-mailem i hasłem, wygaśnięcie sesji po 60 minutach bezczynności.
- Reset hasła jednorazowym linkiem e-mail; wybór dostawcy wysyłki (D-05).
- Egzekwowanie uwierzytelnienia w API, jawny CORS, CSRF przy cookies.
- Link z panelu administracyjnego sklepu do strony logowania ERP.
- Ekran logowania i komunikat wygaśnięcia sesji.
- Do rozstrzygnięcia w specyfikacji: utworzenie pierwszego konta, czas ważności linku, ochrona przed zgadywaniem hasła, wylogowanie, unieważnienie sesji po resecie, kto dodaje link w panelu, lista wyjątków od DOST-01 (webhook, logowanie, reset, health endpoint).

## Acceptance criteria
- [ ] Bez ważnej sesji API odmawia każdego odczytu i zapisu poza jawnie wymienionymi wyjątkami.
- [ ] Sesja wygasa po 60 minutach bezczynności, a wygaśnięcie nie ujawnia danych.
- [ ] Link resetu działa jednorazowo.
- [ ] Link z panelu sklepu prowadzi do logowania i nie uwierzytelnia.
- [ ] Sesja działa między domenami na wdrożonym środowisku.

## Out of scope
- Wiele kont i role.
- Wspólne logowanie z WooCommerce.
```

**Metadata**

```
Breakdown ID: 6
Risk: high — human review required before merge
Depends on: 3, 4, 5
PRD sections: 3, 6 (DOST-01), 10, 11
```

---

## 7. Zapis audytu zmian finansowych

**Title**

```
Zapis audytu zmian finansowych
```

**Description**

```markdown
## Context
Każdy moduł finansowy zapisuje audyt, a zamykanie okresów jest poza MVP, więc audyt wyjaśnia zmiany minionych raportów. Mechanizm musi powstać przed modułami, które go używają. PRD sekcja 5 „Historia zmian”, AUD-01.

## Scope
- Wspólny mechanizm zapisu: autor (konto albo autor systemowy „Synchronizacja WooCommerce” / „Zadanie automatyczne”), czas UTC, obiekt z datą biznesową, operacja, zakres zmiany.
- Zapis w tej samej transakcji co zmiana danych.
- Ślad usunięcia widoczny po zniknięciu obiektu.
- Do rozstrzygnięcia w specyfikacji: moduł będący właścicielem danych audytu i interfejs zapisu zgodny z niezmiennikiem 7, z uwzględnieniem anonimizacji.

## Acceptance criteria
- [ ] Moduł może zapisać wpis przez jawny interfejs, bez dostępu do tabel innego modułu.
- [ ] Nieudany zapis audytu cofa zmianę danych.
- [ ] Wpis zawiera autora, czas UTC, obiekt, operację i zakres zmiany.
- [ ] Autor systemowy jest odróżniony od konta.

## Out of scope
- Lista „Historia zmian” (zadanie 18).
- Anonimizacja wpisów (zadanie 22).
```

**Metadata**

```
Breakdown ID: 7
Risk: high — human review required before merge
Depends on: 3, 6
PRD sections: 5 (Historia zmian), 6 (AUD-01), 7, 10
```

---

## 8. Zamówienia ręczne, wspólna lista, anulowanie i przywracanie

**Title**

```
Zamówienia ręczne, wspólna lista, anulowanie i przywracanie
```

**Description**

```markdown
## Context
Zamówienie jest podstawą sprzedaży i wpłat. Zadanie dostarcza model zamówienia, także pola potrzebne później integracji. PRD sekcje 5 „Zamówienie ręczne”, „Anulowanie i zwrot”, sekcja 7, ZAM-01, 03, 04, 05, 08.

## Scope
- Model zamówienia i klienta, dane operacyjne, słowniki kanałów i statusów realizacji.
- Formularz „Dodaj zamówienie” z ochroną przed ponownym wysłaniem.
- Wspólna lista z filtrowaniem, sortowaniem i paginacją przez API; szczegóły i edycja.
- Kontrola współbieżności: równoczesna zmiana nie nadpisuje nowszych danych bez ostrzeżenia.
- Korekta wartości zamówienia ręcznego z audytem.
- „Anuluj zamówienie” i „Przywróć zamówienie” z potwierdzeniem i audytem; zamówień nie można usunąć.
- Do rozstrzygnięcia w specyfikacji: walidacja wartości ujemnej, zmiana daty zamówienia, wyłączenie „Anulowane” z pola statusu na rzecz akcji, filtr po stanie płatności (z zadaniem 9), stałe czy edytowalne słowniki.

## Acceptance criteria
- [ ] Zapisane zamówienie pojawia się na liście; źródło techniczne i kanał są rozróżnione.
- [ ] Zapis zamówienia nie oznacza otrzymania pieniędzy.
- [ ] Podwójne kliknięcie zapisu nie tworzy dwóch rekordów.
- [ ] Równoczesna zmiana pokazuje konflikt zamiast cichego nadpisania.
- [ ] Zamówienia nie można usunąć przez API ani UI.
- [ ] Anulowanie i przywrócenie po potwierdzeniu zmieniają status i zostawiają audyt.

## Out of scope
- Wpłaty, zwroty i ostrzeżenia o wpłatach (zadanie 9).
- Zamówienia ze sklepu (zadanie 12).
- Wyliczanie sprzedaży (zadanie 19).
```

**Metadata**

```
Breakdown ID: 8
Risk: high — human review required before merge
Depends on: 6, 7
PRD sections: 5 (Zamówienie ręczne, Anulowanie i zwrot, Korekta wartości), 6 (ZAM-01, 03, 04, 05, 08, PLAT-06), 7
```

---

## 9. Wpłaty, zwroty i wyliczany stan płatności

**Title**

```
Wpłaty, zwroty i wyliczany stan płatności
```

**Description**

```markdown
## Context
Wpłaty są jedynym zapisem wpływu, a stan płatności wynika wyłącznie z nich. Dodawane ręcznie także dla zamówień z WooCommerce. PRD sekcje 5 „Rejestracja płatności”, „Anulowanie i zwrot”, PLAT-01, 04, 05, 06.

## Scope
- Wpłata: kwota, data operacji, metoda, opcjonalne „Przyjął” przy gotówce; wiele wpłat na zamówieniu.
- Zwrot: wymagany rodzaj bez wartości domyślnej („Zwrot sprzedaży” / „Zwrot pieniędzy”), kwota, data, metoda, powód.
- Backendowe wyliczanie kwoty Zapłacono i stanu: Nieopłacone, Częściowo opłacone, Opłacone.
- Ostrzeżenia: „Nadpłata X zł”, „Do zwrotu X zł”, wpłata do anulowanego zamówienia, anulowanie zamówienia z wpłatami.
- Poprawa i usunięcie wpisów z potwierdzeniem i audytem.
- Do rozstrzygnięcia w specyfikacji: daty przyszłe i wcześniejsze niż data zamówienia, zmiana rodzaju zwrotu po zapisie, współbieżne wpisy przekraczające limit, kwota do zapłaty.

## Acceptance criteria
- [ ] Kilka wpłat sumuje się, a stan przechodzi z Nieopłacone przez Częściowo opłacone na Opłacone.
- [ ] Zwrot obniża Zapłacono i przelicza stan.
- [ ] Nadpłata pozostawia Opłacone i pokazuje ostrzeżenie.
- [ ] Zamówienie anulowane nie ma stanu i pokazuje kwotę do zwrotu.
- [ ] Zamówienie o wartości 0 zł ma stan Opłacone.
- [ ] Wpłata i zwrot muszą być większe od 0 zł, a zwrot nie może przekroczyć Zapłacono.
- [ ] Zmiana, po której Zapłacono byłoby ujemne, jest odrzucana.
- [ ] Pobranie bez dodanej wpłaty pozostaje nieopłacone.
- [ ] Reguły mają testy jednostkowe; kwoty używają `decimal(18,2)`.

## Out of scope
- Limit obniżeń sprzedaży (zadanie 10).
- Wpływ na raporty (zadania 19 i 20).
- Automatyczne wpłaty i zwroty z WooCommerce.
```

**Metadata**

```
Breakdown ID: 9
Risk: high — human review required before merge
Depends on: 8
PRD sections: 5 (Rejestracja płatności, Anulowanie i zwrot, Błędny wpis), 6 (PLAT-01, 04, 05, 06, KWO-01), 7, 8 (Reguły kwot)
```

---

## 10. Korekta sprzedaży i limit obniżeń

**Title**

```
Korekta sprzedaży i limit obniżeń
```

**Description**

```markdown
## Context
Oddanie towaru bez zwrotu pieniędzy obniża sprzedaż bez ruchu pieniędzy. Limit chroni przed ujemną sprzedażą zamówienia. PRD sekcja 5 „Korekta sprzedaży”, „Limit obniżeń sprzedaży”, PLAT-07.

## Scope
- Korekta sprzedaży: kwota większa od 0 zł, data, powód, bez metody; dla zamówień ręcznych i ze sklepu.
- Poprawa i usunięcie korekty z potwierdzeniem i audytem.
- Limit: suma zwrotów sprzedaży i korekt nie przekracza wartości; dotyczy wpisu, poprawki i ręcznej korekty wartości.
- Ostrzeżenie „Obniżenia sprzedaży przekraczają wartość o X zł”, wyliczane ze stanu zamówienia.
- Opisy w formularzach wyjaśniające różnicę między korektą wartości, zwrotem i korektą sprzedaży.

## Acceptance criteria
- [ ] Korekta 500 zł do zamówienia 1000 zł z zaliczką 300 zł nie zmienia Zapłacono ani stanu płatności.
- [ ] Zmiana przekraczająca limit jest odrzucana z komunikatem podającym dostępną kwotę.
- [ ] Zwrot sprzedaży 1200 zł przy zamówieniu 1000 zł i wpłatach 1200 zł jest odrzucany.
- [ ] Ostrzeżenie pokazuje się, gdy wartość spadnie poniżej sumy obniżeń.
- [ ] Korekta jest pokazana osobno od wpłat i zwrotów.

## Out of scope
- Obniżenie sprzedaży w raportach (zadanie 19).
```

**Metadata**

```
Breakdown ID: 10
Risk: high — human review required before merge
Depends on: 9
PRD sections: 5 (Korekta sprzedaży, Limit obniżeń sprzedaży), 6 (PLAT-07)
```

---

## 11. Trwały harmonogram zadań

**Title**

```
Trwały harmonogram zadań
```

**Description**

```markdown
## Context
Uzupełnianie WooCommerce i wydatki cykliczne potrzebują tego samego mechanizmu odpornego na restart i usypianie hostingu. Wydzielenie go pozwala budować oba obszary niezależnie. PRD sekcja 10 „Automatyzacja”.

## Scope
- Mechanizm zadań zgodny z wynikiem zadania 1, bez polegania wyłącznie na timerze w pamięci.
- Wykrycie i nadrobienie zadania pominiętego podczas przerwy.
- Ochrona przed równoległym uruchomieniem tego samego zadania.
- Czas w strefie Europe/Warsaw.
- Do rozstrzygnięcia w specyfikacji: jak widać niepowodzenie zadania (INT-01 obejmuje tylko WooCommerce, a brak wystąpień zaniża koszty bez ostrzeżenia).

## Acceptance criteria
- [ ] Zadanie wykonuje się po restarcie i po uśpieniu procesu.
- [ ] Zadanie nie działa równolegle samo ze sobą.
- [ ] Niepowodzenie zadania zostawia zapisany ślad.

## Out of scope
- Logika synchronizacji (zadanie 13) i wydatków cyklicznych (zadanie 16).
```

**Metadata**

```
Breakdown ID: 11
Risk: medium
Depends on: 1, 3
PRD sections: 10 (Automatyzacja)
```

---

## 12. Import zamówień WooCommerce

**Title**

```
Import zamówień WooCommerce
```

**Description**

```markdown
## Context
Zamówienia ze sklepu docierają na bieżąco webhookami i mają zmieniać dane w ERP tylko według ustalonej własności pól. Webhooki zmieniają zamówienia, więc to obszar wysokiego ryzyka. PRD sekcja 5 „Zamówienie WooCommerce”, ZAM-02, 06, ZAM-08.

## Scope
- Endpoint webhooków z weryfikacją autentyczności; klucze tylko po stronie serwera.
- Identyfikacja według źródła i zewnętrznego ID z ograniczeniem unikalności.
- Własność pól: WooCommerce (ID, data, klient, wartość) tylko do odczytu w ERP; pola ERP nietknięte.
- Pomijanie danych starszych według daty modyfikacji i zamówień sprzed daty uruchomienia.
- Status ze sklepu; pending, failed i szkice ukryte domyślnie i dostępne filtrem.
- Jednorazowe przejście na „Anulowane”, usunięcie w sklepie, ostrzeżenie „Zamówienie aktywne w sklepie”, status przy imporcie.
- Audyt z autorem „Synchronizacja WooCommerce”, także dla zmian statusu wpływających na sprzedaż.
- Do rozstrzygnięcia w specyfikacji: wnioski z zadania 2, pola na oznaczenie usunięcia i informację o zwrocie w sklepie, ostrzeżenie przy przywróceniu w ERP zamówienia anulowanego w sklepie, równoczesny zapis użytkownika i synchronizacji.

## Acceptance criteria
- [ ] Wielokrotne dostarczenie tego samego ID daje jeden rekord.
- [ ] Synchronizacja nie zmienia pól ERP poza jednorazowym przejściem na „Anulowane”.
- [ ] Ponowne dostarczenie tego samego statusu nie zmienia statusu realizacji.
- [ ] Dostawa starsza od zapisanych danych niczego nie zmienia.
- [ ] Zamówienie z datą sprzed uruchomienia nie tworzy rekordu ani błędu.
- [ ] Przywrócenie w sklepie i anulowanie w ERP zamówienia aktywnego w sklepie pokazują ostrzeżenie i niczego nie zmieniają w sklepie.
- [ ] Nowe zamówienie ma status „W trakcie”, a zaimportowane jako anulowane — „Anulowane”.
- [ ] Integracja nie tworzy wpłat ani wypływów.
- [ ] Obniżenie wartości poniżej sumy obniżeń sprzedaży pokazuje ostrzeżenie.

## Out of scope
- Dzienne uzupełnianie i ponowienia (zadanie 13).
- Zapis czegokolwiek w sklepie.
- Waluty inne niż PLN.
```

**Metadata**

```
Breakdown ID: 12
Risk: high — human review required before merge
Depends on: 2, 4, 10
PRD sections: 5 (Zamówienie WooCommerce), 6 (ZAM-02, 06, 08), 7, 10 (Integracje)
```

---

## 13. Uzupełnianie luk, historia błędów i ponowienia

**Title**

```
Uzupełnianie luk, historia błędów i ponowienia
```

**Description**

```markdown
## Context
Pominięty webhook musi być uzupełniony, a błąd pojedynczego zamówienia nie może zatrzymać całości. PRD sekcja 5, ZAM-02, INT-01.

## Scope
- Odpytanie REST API o 23:00 Europe/Warsaw o zmiany od ostatniego sukcesu; pierwsze pobranie od daty uruchomienia.
- Definicja sukcesu: sklep odpowiedział i pobrano wszystkie strony.
- Błąd pojedynczego zamówienia ze statusem, czasem i kontekstem, otwarty do udanego ponowienia.
- „Ponów” dla jednego zamówienia i „Synchronizuj teraz” od ostatniego sukcesu.
- Do rozstrzygnięcia w specyfikacji: znacznik sukcesu jako początek przebiegu, nakładanie się „Synchronizuj teraz” z przebiegiem o 23:00, stan przed pierwszym sukcesem, błąd zamówienia, którego już nie ma, zamknięcie błędu przez udany webhook.

## Acceptance criteria
- [ ] Pominięty webhook jest uzupełniany przy dziennym odpytaniu.
- [ ] Po przerwie kolejne uzupełnianie startuje od ostatniego sukcesu.
- [ ] Błąd pojedynczego zamówienia nie zatrzymuje przebiegu.
- [ ] „Ponów” zamyka błąd po udanym zapisie.
- [ ] Obie akcje nie tworzą duplikatów.
- [ ] Niedostępność sklepu jest zapisana jako nieudany przebieg i da się z niej odzyskać dane.

## Out of scope
- Prezentacja stanu (zadanie 14).
```

**Metadata**

```
Breakdown ID: 13
Risk: high — human review required before merge
Depends on: 11, 12
PRD sections: 5 (Sukces synchronizacji, Ponowienie ręczne), 6 (ZAM-02, INT-01), 7, 10
```

---

## 14. Stan integracji w UI

**Title**

```
Stan integracji w UI
```

**Description**

```markdown
## Context
Użytkownik nie może sądzić, że dane są kompletne, gdy nie są. PRD sekcja 5 „Stany synchronizacji”, INT-01, design guide sekcje 3, 6, 8.

## Scope
- Stany w ustalonej kolejności: „Synchronizacja trwa”, „Błąd”, „Dane niekompletne”, „Aktualne”.
- „Ostatnia pełna kontrola” z czasem ostatniego sukcesu.
- Stan w nagłówku i ekran integracji z historią błędów oraz akcjami „Ponów” i „Synchronizuj teraz”.
- Wielokrotnego użytku komunikat o niekompletnych danych dla widoków raportowych.

## Acceptance criteria
- [ ] Stan wynika z warunków PRD, w tym „Dane niekompletne” przy otwartych błędach lub po ponad 25 godzinach od ostatniego sukcesu.
- [ ] UI pokazuje ostatnią pełną kontrolę, błąd i dostępną akcję.
- [ ] UI nie sugeruje kompletności przy awarii.
- [ ] Stan nie zależy wyłącznie od koloru.

## Out of scope
- Umieszczenie komunikatu w widokach (zadania 19 i 20).
```

**Metadata**

```
Breakdown ID: 14
Risk: medium
Depends on: 13
PRD sections: 5 (Stany synchronizacji), 6 (INT-01); design guide 3, 6, 8
```

---

## 15. Wydatki jednorazowe i zapłata wydatku

**Title**

```
Wydatki jednorazowe i zapłata wydatku
```

**Description**

```markdown
## Context
Wydatek ma datę kosztu i osobną datę zapłaty, od której zależy wypływ. PRD sekcja 5 „Wydatek jednorazowy i miesięczny”, „Rejestracja płatności”, WYD-01, 02, PLAT-02.

## Scope
- Model wydatku i słownik kategorii; kwota brutto różna od 0 zł, opcjonalna netto tego samego znaku, bez wyliczania ze stawki.
- Dodawanie, poprawianie i usuwanie z potwierdzeniem i audytem.
- Akcja „Oznacz jako opłacony” i data zapłaty w formularzu.
- Wydatki ujemne z opisem „Korekta / zwrot od dostawcy”.
- Lista z filtrowaniem, w tym po dacie kosztu (potrzebne do uzgodnienia karty „Koszty”).
- Do rozstrzygnięcia w specyfikacji: przyszła data zapłaty (nie może zamienić planu w wypływ), usunięcie daty zapłaty, jedna etykieta stanu bez daty zapłaty („Planowany” / „Nieopłacony”), słowniki metod i płatników, kolumny dat na liście.

## Acceptance criteria
- [ ] Kwota 0 zł jest odrzucana.
- [ ] Wydatek bez daty zapłaty nie ma stanu „Opłacony”, a po jej uzupełnieniu go ma.
- [ ] Zmiana i usunięcie zostawiają audyt; usunięcie wymaga potwierdzenia.
- [ ] Wydatek można wpisać z datą kosztu sprzed uruchomienia i późniejszą datą zapłaty.
- [ ] Listę można przefiltrować po dacie kosztu.

## Out of scope
- Definicje cykliczne (zadanie 16).
- Powiązanie wydatków z zamówieniami.
- Raporty (zadania 19 i 20).
```

**Metadata**

```
Breakdown ID: 15
Risk: high — human review required before merge
Depends on: 6, 7
PRD sections: 5 (Wydatek jednorazowy i miesięczny, Zapłata wydatku), 6 (WYD-01, 02, PLAT-02), 7, 8
```

---

## 16. Wydatki cykliczne

**Title**

```
Wydatki cykliczne
```

**Description**

```markdown
## Context
System ma tworzyć wystąpienia automatycznie i bez duplikatów, także po przerwie. Automatyczny wpis jest kosztem, a wypływem dopiero po zapłacie. PRD sekcja 5, WYD-03–06, D-04.

## Scope
- Definicja: pola wydatku poza datami, dzień miesiąca, początek, koniec, aktywność; kwota większa od 0 zł.
- Tworzenie wystąpień w dniu terminu; w krótszym miesiącu ostatni dzień; unikalna para definicja–okres.
- Zapis nowej definicji tworzy wystąpienia od początku do dzisiaj, a formularz pokazuje wcześniej ich liczbę.
- Zaległe wystąpienia po przerwie w działaniu systemu.
- Wstrzymanie i wznowienie; zmiana definicji dotyczy tylko nieutworzonych wystąpień.
- Ślad usunięcia blokuje ponowne utworzenie.
- Audyt z autorem „Zadanie automatyczne”.
- Do rozstrzygnięcia w specyfikacji: zapis, do którego okresu definicja została przetworzona; rozróżnienie wstrzymania od przerwy; zmiana dnia miesiąca na termin, który już minął; wydłużenie zakończonej definicji; wznowienie po minionym terminie bieżącego okresu.

## Acceptance criteria
- [ ] Ponowienie i restart nie duplikują wystąpień.
- [ ] Dzień 29–31 przypada w krótszym miesiącu na ostatni dzień.
- [ ] Przed terminem wystąpienie nie istnieje i nie można go opłacić.
- [ ] Wystąpienie nie zwiększa wypływów przed oznaczeniem jako opłacone.
- [ ] Wstrzymanie blokuje kolejne, a wznowienie nie tworzy okresów z czasu wstrzymania.
- [ ] Zmiana definicji nie zmienia utworzonych wystąpień.
- [ ] Usunięte wystąpienie nie wraca.

## Out of scope
- Historia wersji definicji.
```

**Metadata**

```
Breakdown ID: 16
Risk: high — human review required before merge
Depends on: 11, 15
PRD sections: 5 (Wydatek jednorazowy i miesięczny), 6 (WYD-03–06), 7, 12 (D-04)
```

---

## 17. Wypłaty właścicieli i lista „Operacje”

**Title**

```
Wypłaty właścicieli i lista „Operacje”
```

**Description**

```markdown
## Context
Wypłaty są odrębne od kosztów, a lista „Operacje” jest punktem uzgodnienia przepływów pieniężnych. PRD sekcja 5 „Wypłata właściciela”, „Lista Operacje”, PLAT-03, FIN-05, 06.

## Scope
- Wypłata właściciela: osoba, kwota, data, metoda; poprawa i usunięcie z potwierdzeniem i audytem.
- Lista „Operacje”: wpłaty, zwroty, opłacone wydatki, wypłaty, z filtrem okresu i rodzaju oraz przejściem do źródła.
- Odczyt danych innych modułów przez jawne interfejsy.
- Do rozstrzygnięcia w specyfikacji: pomijanie kwot sprzed daty uruchomienia, gdy okres obejmuje tę datę; walidacja kwoty wypłaty; słownik osób.

## Acceptance criteria
- [ ] Wypłata zwiększa wypływy, nie zmienia kosztów ani sprzedaży.
- [ ] Poprawa i usunięcie zostawiają audyt; usunięcie wymaga potwierdzenia.
- [ ] Korekty sprzedaży nie pojawiają się na liście.
- [ ] Opłacony wydatek ujemny jest wypływem z kwotą ujemną.
- [ ] Lista przy okresie obejmującym datę uruchomienia nie obejmuje kwot wyłączonych z raportów.

## Out of scope
- Karty przepływów (zadanie 20).
```

**Metadata**

```
Breakdown ID: 17
Risk: high — human review required before merge
Depends on: 9, 15
PRD sections: 5 (Wypłata właściciela, Lista Operacje), 6 (PLAT-03, FIN-05, 06), 7, 10 (Architektura)
```

---

## 18. Lista „Historia zmian”

**Title**

```
Lista „Historia zmian”
```

**Description**

```markdown
## Context
Bez zamykania okresów ta lista wyjaśnia, dlaczego zmienił się raport za miniony okres. PRD sekcja 5 „Historia zmian”, AUD-01.

## Scope
- Lista tylko do odczytu: czas, autor, obiekt z datą biznesową, operacja, zakres zmiany.
- Filtr okresu zmiany i rodzaju obiektu; paginacja przez API.
- Przejście do obiektu, a przy obiekcie usuniętym widoczny ślad usunięcia.
- Do rozstrzygnięcia w specyfikacji: czy filtrować też po dacie biznesowej.

## Acceptance criteria
- [ ] Lista nie pozwala niczego zmienić, także przez API.
- [ ] Filtry okresu zmiany i rodzaju obiektu działają.
- [ ] Wpis prowadzi do obiektu, a usunięty obiekt zostawia widoczny ślad.
- [ ] Czasy są prezentowane w Europe/Warsaw.

## Out of scope
- Zapis audytu (zadanie 7).
- Scenariusz wyjaśnienia zmiany raportu, sprawdzany przy odbiorze (zadanie 21).
```

**Metadata**

```
Breakdown ID: 18
Risk: high — human review required before merge
Depends on: 8
PRD sections: 5 (Historia zmian), 6 (AUD-01), 10
```

---

## 19. Reguły raportowe i widok „Sprzedaż”

**Title**

```
Reguły raportowe i widok „Sprzedaż”
```

**Description**

```markdown
## Context
Widok łączy zamówienia, zwroty sprzedaży, korekty i wydatki według definicji z PRD. To serce produktu: wyniki muszą dać się prześledzić do rekordów. Zadanie dostarcza też wspólne reguły okresów. PRD sekcje 5 „Analiza finansów”, 8, FIN-01–06, ZAM-07, WYD-07, URU-01.

## Scope
- Wspólne reguły okresów: bieżący miesiąc, poprzedni miesiąc, rok, własny zakres, porównanie, wyłączenie sprzed uruchomienia.
- Sprzedaż, koszty, wynik przed wypłatami, liczba zamówień; obliczenia wyłącznie w backendzie.
- Wykresy, podział na kanały i kategorie kosztów, wartości dostępne tekstowo.
- Zestawienie składników sprzedaży z przejściem do zamówienia.
- Opis podstawy kwot, daty przypisania i aktualności danych; komunikat o niekompletnej integracji.
- Do rozstrzygnięcia w specyfikacji: uzgodnienie karty „Koszty” z listą wydatków; początek własnego zakresu względem daty uruchomienia; czy bieżący miesiąc kończy się dziś; czwarta karta KPI w układzie z design guide; opis porównania niepełnego miesiąca; przejścia z kategorii i kanałów; suma zestawienia dla całego okresu przy paginacji.

## Acceptance criteria
- [ ] Suma zestawienia równa się karcie „Sprzedaż” przy tym samym okresie.
- [ ] Korekta do zamówienia z wcześniejszego miesiąca jest widoczna w okresie swojej daty.
- [ ] Suma kanałów równa się sprzedaży.
- [ ] Zamówienia anulowane, pending, failed, szkice i sprzed uruchomienia nie liczą się do sprzedaży ani do liczby zamówień.
- [ ] Zwrot pieniędzy nie obniża sprzedaży; zwrot nadpłaty 200 zł przy zamówieniu 1000 zł pozostawia sprzedaż 1000 zł.
- [ ] Po anulowaniu zwroty i korekty zamówienia nie obniżają sprzedaży, a po przywróceniu znów ją obniżają.
- [ ] Koszty obejmują opłacone i nieopłacone wydatki według daty kosztu, a wydatek ujemny je pomniejsza.
- [ ] Przy bazie równej zero lub sprzed uruchomienia widok pokazuje „brak podstawy porównania”.
- [ ] Wypłaty właścicieli nie obniżają wyniku.
- [ ] Reguły mają testy jednostkowe, także dla scenariuszy z §11.

## Out of scope
- Widok przepływów (zadanie 20).
- Saldo rachunku, wynik zarządczy, liczba unikalnych klientów, prognozy.
```

**Metadata**

```
Breakdown ID: 19
Risk: high — human review required before merge
Depends on: 10, 12, 14, 16
PRD sections: 5 (Analiza finansów, Podział sprzedaży na kanały), 6 (FIN-01–06, ZAM-07, WYD-07, URU-01, KWO-01), 8; design guide 4, 7
```

---

## 20. Widok „Przepływy pieniężne”

**Title**

```
Widok „Przepływy pieniężne”
```

**Description**

```markdown
## Context
Drugi widok pokazuje rzeczywiste pieniądze, niezależnie od wartości zamówień. PRD sekcje 5 „Zawartość widoków”, 8, FIN-01–07, URU-01.

## Scope
- Karty wpływów, wypływów i zmiany środków prowadzące do listy „Operacje”.
- Wykres wpływów i wypływów w czasie, podział wypływów według rodzaju operacji, ostatnie operacje.
- Przełącznik widoków: wszystkie karty, wykresy i podpisy zmieniają podstawę jednocześnie.
- Reguły okresów i komunikat o niekompletnych danych z zadań 19 i 14.

## Acceptance criteria
- [ ] Nieopłacone zamówienie nie zwiększa wpływów, a wpłata nie tworzy drugiej sprzedaży.
- [ ] Suma wpływów i wypływów równa się liście „Operacje” przy tym samym okresie.
- [ ] Wydatek bez daty zapłaty nie zwiększa wypływów.
- [ ] Opłacony wydatek ujemny pomniejsza wypływy.
- [ ] Wpłata po uruchomieniu do zamówienia sprzed uruchomienia zwiększa wpływy; zwrot zwiększa wypływy.
- [ ] Widok nie używa słowa „zysk” i nie pokazuje salda rachunku.
- [ ] Wartości są dostępne również tekstowo.

## Out of scope
- Saldo rachunku i saldo początkowe.
- Wpływy niezwiązane z zamówieniem.
```

**Metadata**

```
Breakdown ID: 20
Risk: high — human review required before merge
Depends on: 17, 19
PRD sections: 5 (Zawartość widoków), 6 (FIN-01–07, URU-01), 8; design guide 4, 7
```

---

## 21. Kopie zapasowe, procedura powrotu i odbiór MVP

**Title**

```
Kopie zapasowe, procedura powrotu i odbiór MVP
```

**Description**

```markdown
## Context
Uruchomienie produkcyjne wymaga odbioru scenariuszy z sekcji 11 i sprawdzonego odtwarzania. PRD sekcje 10 „Odzyskiwanie”, 11, 13; wymagania technologiczne sekcja 13.

## Scope
- Harmonogram i retencja kopii z zadania 1; próba odtworzenia.
- Procedura powrotu do działającej wersji frontendu i backendu.
- Testy E2E kluczowych procesów.
- Przejście scenariuszy finansowych z sekcji 11, w tym zmiany raportu wyjaśnionej listą „Historia zmian”.
- Sprawdzenie przełomu uruchomienia i ustawienie daty uruchomienia podanej przez właściciela.
- Odbiór UI według design guide sekcja 10.

## Acceptance criteria
- [ ] Odtworzenie kopii i migracja schematu zostały sprawdzone na hostingu.
- [ ] Sprawdzono procedurę powrotu do działającej wersji.
- [ ] Repozytorium i pakiet frontendu nie zawierają sekretów.
- [ ] Wszystkie scenariusze z sekcji 11 przechodzą, w tym zamówienie i wydatek otwarte w dniu uruchomienia.
- [ ] Oba widoki uzgadniają się z rekordami źródłowymi, także karta „Koszty”.
- [ ] Kluczowe procesy mają testy E2E, a CI przechodzi.
- [ ] Spełnione są kontrast, obsługa klawiatury i prezentacja niekompletności danych.

## Out of scope
- Migracja historii z Excela.
- Anonimizacja (zadanie 22).
```

**Metadata**

```
Breakdown ID: 21
Risk: high — human review required before merge
Depends on: 4, 18, 20
PRD sections: 4 (Przełom uruchomienia), 10 (Odzyskiwanie), 11, 13
```

---

## 22. Anonimizacja danych klienta

**Title**

```
Anonimizacja danych klienta
```

**Description**

```markdown
## Context
Zamówień nie usuwa się, więc żądanie usunięcia danych obsługuje ręczna, nieodwracalna anonimizacja. Implementację blokuje otwarta decyzja D-09. PRD sekcja 5 „Anonimizacja danych klienta”, DANE-01.

## Scope
- Akcja „Anonimizuj dane klienta” z potwierdzeniem, dostępna po upływie okresu retencji.
- Usunięcie danych klienta z zamówienia i wpisów audytu, z zachowaniem kwot, dat, wpłat, zwrotów i śladu operacji.
- Blokada przywrócenia danych przez synchronizację.
- Do rozstrzygnięcia w specyfikacji: moment, od którego liczy się retencja; dane w historii błędów synchronizacji, logach, kopiach zapasowych i polach tekstowych (uwagi, powód zwrotu, powód korekty); pole na fakt anonimizacji; edycja zamówienia po anonimizacji.

## Acceptance criteria
- [ ] Przed upływem okresu retencji akcja jest niedostępna, także przez API.
- [ ] Po potwierdzeniu dane klienta znikają z zamówienia i audytu, a raporty się nie zmieniają.
- [ ] Synchronizacja nie przywraca danych.
- [ ] Operacja zostawia ślad audytu bez danych osobowych.

## Out of scope
- Rozstrzygnięcie D-09, które wymaga księgowego lub prawnika.
```

**Metadata**

```
Breakdown ID: 22
Risk: high — human review required before merge
Depends on: 12, decyzja D-09
PRD sections: 5 (Anonimizacja danych klienta), 6 (DANE-01), 12 (D-09), 13
```
