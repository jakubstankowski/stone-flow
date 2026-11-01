---
result: FAIL
reviewed: 2026-11-01
documents:
  - path: docs/PRD-stone-flow.md
    fingerprint: 747a08bf3b30eadba3474a809a39551bd9ae6467
  - path: docs/DESIGN-GUID-stone-flow.md
    fingerprint: 08c09f6e4d2d88d1e854e77569b2ee30904f1cea
---

# Przegląd dokumentacji projektu — Stone Flow

## Result: FAIL

PRD jest uczciwy co do swoich luk, ale wiele kryteriów akceptacji zależy od reguł, których
jeszcze nie ma. Ponadto widok „Przepływy pieniężne” nie ma procesu, który dostarczałby mu
danych. Sam PRD (§12–13) wymaga uzgodnienia D-01–D-05 przed implementacją; ten przegląd
wskazuje, co musi trafić do dokumentu przed podziałem na zadania.

Porównano PRD i design guide ze sobą, z oboma dokumentami wymagań oraz z kolumnami arkuszy DEMO.

## ✅ Ready

- Cel, użytkownicy i problem są jasne. Sekcja „poza zakresem” jest równie konkretna jak MVP.
- Rozdzielenie sprzedaży od przepływów jest spójne w PRD i design guide, włącznie z nazewnictwem
  i zabezpieczeniami przed podwójnym ujęciem (§8).
- Identyfikatory ZAM, WYD i FIN są zgodne z wymaganiami biznesowymi. FIN-07, DOST-01 i INT-01 są
  identyfikatorami projektowymi, co dopuszcza nagłówek tabeli w §6.
- Wymagania techniczne (§10) wiernie oddają wymagania technologiczne, z jednym wyjątkiem opisanym
  w punkcie 3 listy blokującej.
- Podział źródła technicznego i kanału, słowniki i kategorie są opisane. Opisane w §9 problemy
  jakości danych zgadzają się z charakterem arkuszy.
- Żadna część dokumentów nie narusza skonfigurowanych niezmienników. Nazwa handlowa ani domena
  sklepu nie występują w PRD ani design guide.
- Design guide ma mierzalne kryteria UI: tokeny, kontrast, formaty i stany.

## ❌ Blocking — fix before splitting into tasks

1. **Brak procesu rejestrowania płatności:** MVP pkt 4 i FIN-07 wymagają rzeczywistych wpłat i
   wypłat, ale §5 i §6 nie opisują, kto i jak zapisuje wpłatę za zamówienie ani zapłatę wydatku.
   Nie wiadomo też, czy opłacone zamówienie WooCommerce tworzy wpłatę automatycznie. Model wydatku
   (§7) ma jedną „datę biznesową” i status płatności, bez daty faktycznej zapłaty, choć przepływy
   liczone są według daty operacji, a design guide (§6) rozróżnia datę kosztu, termin i datę
   zapłaty. Model zamówienia nie ma też stanu płatności, który design guide pokazuje na liście.
   Wypłaty właścicieli nie mają żadnego punktu wejścia do systemu. → Dodać do §5 proces „Rejestracja
   płatności”, do §6 wymaganie z kryterium akceptacji, a do §7 pola: data zapłaty wydatku oraz stan
   płatności zamówienia (zapisany lub wyliczany). Ustalić, czy wypłata właściciela jest wydatkiem
   czy osobnym obiektem.

2. **Niezdefiniowane KPI i niejasny los „wyniku” w MVP (D-01):** FIN-02, FIN-04 i kryteria sukcesu
   wymagają przychodów, kosztów i wyniku za okres. PRD §8 i design guide §4 dopuszczają koszty i
   wynik dopiero po definicji, a kryterium FIN-01–04 w §6 w ogóle ich nie sprawdza. Nie da się dziś
   stwierdzić, czy MVP spełnia FIN-02. Kryteria ZAM-07, WYD-07 i FIN-05, 06 opierają się na regułach,
   których nie ma. → Rozstrzygnąć D-01: netto czy brutto, data ujęcia, statusy kwalifikujące, korekty,
   klasyfikacja podatków i wypłat. Zapisać, czy wynik jest w MVP i jak jest liczony, a jeśli nie,
   odnotować świadome odstępstwo od FIN-02. Zatwierdzić zakres okresów, który §8 nadal określa jako
   propozycję.

3. **Niepełny i niespójny kontrakt WooCommerce (D-02):** §5 mówi, że backend „pobiera” zamówienie,
   §10 wymaga autentyczności webhooków bezwarunkowo, a wymagania technologiczne mówią „jeżeli użyte
   zostaną webhooki”. D-02 nie pyta o mechanizm. Własność pól, która decyduje o niezmienniku
   ponowień, nie jest określona. Brakuje reguł dla późniejszych zmian zamówienia w sklepie (w tym
   anulowania), mapowania statusów, mapowania kanału, zakresu pierwszego pobrania, sposobu ponawiania
   (automatycznie czy ręcznie) i okna uzupełniania luk. → Rozstrzygnąć D-02 i dodać do PRD tabelę
   własności pól oraz wybrany mechanizm: odpytywanie, webhooki albo oba, ze wskazaniem, który
   uzupełnia luki.

4. **Korekty, zwroty i anulowania bez reguł (D-03):** §11 wymaga scenariuszy korekty i zwrotu, a
   zaliczki, płatności częściowe, pobrania i reklamacje są nierozstrzygnięte. Od tego zależy model
   płatności i słownik statusów. Nie opisano też, co zrobić z błędnym zamówieniem ręcznym: WYD-02
   obejmuje usuwanie wydatków, ale dla zamówień nie ma odpowiednika. → Rozstrzygnąć D-03 i dopisać
   kryteria dla każdego scenariusza oraz regułę anulowania lub usunięcia zamówienia.

5. **Reguły wydatków cyklicznych (D-04):** kryteria WYD-03–06 są sprawdzalne tylko częściowo, bo nie
   wiadomo, czy wystąpienie jest planem czy kosztem, ani jak traktować dni 29–31, zaległe okresy i
   zmianę kwoty. Nie opisano przypadku krawędziowego: czy usunięte wystąpienie wraca przy uzupełnianiu
   zaległości. → Zatwierdzić propozycję z D-04 i dopisać regułę dla usuniętego wystąpienia.

6. **Macierz dostępu i cykl życia kont (D-05):** MVP pkt 1 nie ma kryterium, jakie role istnieją.
   §3 podaje tylko propozycję uprawnień. Nie określono metody logowania, sposobu tworzenia i odbierania
   kont, resetu hasła ani działania wejścia z panelu sklepu, które jest wymagane. Blokuje to kryterium
   DOST-01 i widoczność finansów. → Zatwierdzić macierz, wybrać model logowania i opisać wejście z
   panelu. Jeśli SSO wymaga analizy, zapisać rozwiązanie domyślne na MVP.

7. **Brak reguł kwot:** §8 wymaga jawnej precyzji, skali i zaokrąglania, ale ich nie podaje. Zamówienie
   w §7 ma jedną „wartość zamówienia”, bez netto, brutto, VAT i waluty, podczas gdy wydatek ma netto,
   brutto i podstawę VAT, a płatność ma walutę. §8 zakazuje stałego przelicznika 1,23, lecz nie mówi,
   skąd wziąć stawkę. Pytanie o waluty jest ukryte w D-08, razem z hostingiem. → Podać precyzję, skalę
   i regułę zaokrąglania, źródło stawki VAT oraz pola kwot zamówienia. Rozstrzygnąć, czy MVP działa
   wyłącznie w PLN.

8. **Dwuznaczne słowo „migracja” w §11:** warunek „migracja i odtworzenie kopii zostały sprawdzone”
   może oznaczać migrację schematu (jak w wymaganiach technologicznych) albo migrację historii, która
   według §4 nie jest potwierdzonym zakresem. → Napisać „migracja schematu bazy” i rozstrzygnąć w D-06,
   czy historia i bilans otwarcia są warunkiem odbioru MVP.

## ⚠️ Risky areas — tight oversight during implementation

- **Obliczenia finansowe i dwa widoki (wysokie):** dane pieniężne. Błędna reguła fałszuje decyzje, a
  arkusze nie są wzorcem kontrolnym. Sprzedażowy arkusz ma kolumnę „Zapłacona Kwota brutto”, więc nie
  zawiera wprost wartości zamówienia, której wymaga model z §7.
- **Płatności, zwroty i podwójne ujęcie (wysokie):** D-07 nie należy do listy D-01–D-05 w §13, lecz
  zabezpieczenie 2 z §8 zależy od tej decyzji. Trzeba ją podjąć przed modułem wydatków powiązanych z
  zamówieniami.
- **Synchronizacja WooCommerce i webhooki (wysokie):** zmieniają zamówienia i dane klientów. Ryzyko
  nadpisania pól ERP, duplikatów i cichej niekompletności raportów.
- **Dane osobowe klientów (wysokie):** imiona, adresy, kontakty, NIP. PRD nie określa retencji ani
  usuwania danych osobowych.
- **Usuwanie danych i audyt (wysokie):** operacje trudne do odwrócenia. §7 ogranicza audyt do „zmiany
  finansowej”, a edycja wartości zamówienia WooCommerce nie jest jednoznacznie opisana.
- **Uwierzytelnianie i sesja między domenami (wysokie):** Vercel i osobne API, wymagany CORS oraz CSRF
  przy cookies, a także przyszłe wejście z panelu sklepu.
- **Hosting SMART ASP.NET (D-08) i zadania cykliczne (wysokie):** zgodność planu nie jest potwierdzona,
  a od niej zależy mechanizm odporny na usypianie. Nie blokuje podziału, jeśli sprawdzenie hostingu
  będzie pierwszym zadaniem, od którego zależą pozostałe.
- **Migracje produkcyjne i odtwarzanie (wysokie):** operacje nieodwracalne. RPO, RTO, cele wydajnościowe
  i wolumen danych nie mają wartości (§10), więc nie można ich odebrać. Muszą wynikać z zadania
  hostingowego.
- **Migracja historii (wysokie, jeśli zatwierdzona):** rozbieżności między arkuszami i ryzyko podwójnego
  ujęcia.
- **Współbieżna edycja (średnie):** wymagana w ZAM-04, ale bez reguły rozwiązywania konfliktu poza
  ostrzeżeniem.
- **Definicje liczników (niskie):** „liczba unikalnych klientów” nie ma definicji, a klient istnieje tylko
  przy zamówieniu. §3 mówi o jednym właścicielu, §7 o wypłatach właścicieli, a arkusz rozlicza dwie
  osoby: trzeba to doprecyzować przy macierzy dostępu.

## Suggested initial breakdown

Punkt wyjścia, nie backlog. Kolejność pokazuje zależności.

1. Weryfikacja hostingu i potwierdzenie wersji technologii.
2. Inicjalizacja monorepo, CI, szkielet modułów i motyw UI.
3. Uwierzytelnianie, autoryzacja i wejście z panelu sklepu.
4. Audyt zmian finansowych (część wspólna).
5. Zamówienia ręczne i wspólna lista.
6. Rejestracja płatności zamówień.
7. Synchronizacja WooCommerce (zapewne dwa zadania: import i odporność, czyli ponowienia i uzupełnianie
   luk).
8. Stan integracji w UI.
9. Wydatki jednorazowe.
10. Wydatki cykliczne i trwały harmonogram.
11. Widok sprzedaży.
12. Widok przepływów pieniężnych.
13. Kopie zapasowe, wdrożenia i odbiór operacyjny.
14. Migracja historii, jeśli zostanie zatwierdzona.

Granice są naturalne, a zależności da się już wskazać. Zadania 5–12 nie mają jednak sprawdzalnego
kryterium, dopóki punkty listy blokującej nie zostaną rozwiązane.
