# Spec review — SF-5

## Summary

- Overall assessment: PASS WITH ISSUES
- Justification: Specyfikacja pokrywa cały zakres i wszystkie kryteria odbioru zgłoszenia, trzyma się design guide (paleta, mapowanie motywu, nawigacja, stany, formaty) i nie narusza żadnego niezmiennika. Rozstrzygnięcia podjęte bez właściciela (sekcja „Założenia”) są spójne z PRD, design guide i podziałem zadań; żadne nie przeczy zapisanej decyzji. Do poprawy są głównie niespójność prefiksu selektorów z konfiguracją lint z SF-3 oraz kilka niedookreśleń (kolory nawigacji a obwódka fokusu, znak separatora w kwotach, zachowanie tabeli przy stronie poza zakresem), które planista może rozstrzygnąć bez zmiany zakresu.

## Findings

### Critical Issues

None.

### Non-Critical Issues

1. **Prefiks selektorów a lint.** Sekcja „Zachowanie” (pkt 9–16) nazywa elementy `sf-button`, `sf-field`, `sf-confirm-dialog`, `sf-data-table` itd., a `app/eslint.config.js` z SF-3 wymaga prefiksu `app` (`@angular-eslint/component-selector`: element, kebab-case; `directive-selector`: atrybut, camelCase, np. `appButton`), a `angular.json` ma `"prefix": "app"`. Przy nazwach ze specyfikacji `npm run lint` z „Kryteria odbioru” nie przejdzie. Specyfikacja nie wymienia zmiany konfiguracji lint ani konwencji; trzeba albo przyjąć prefiks `app`, albo jawnie dopisać zmianę prefiksu (i aktualizację `conventions.md`).
2. **Kolor nawigacji a fokus.** Design guide przypisuje `--sf-text` m.in. do „ciemnej nawigacji”, a specyfikacja nie określa tła paska nawigacji ani tła/tekstu aktywnej pozycji (pkt 5: „wyraźne tło, kolor tekstu”). Jednocześnie pkt 8 ustala obwódkę fokusu w `--sf-text` dla wszystkich elementów. Przy ciemnej nawigacji obwódka byłaby niewidoczna (kontrast 1:1). Brakuje wskazania tokenów nawigacji i reguły fokusu na ciemnym tle.
3. **Znak separatora tysięcy i minusa.** Pkt 17 i „Przypadki brzegowe” podają `1 234,50 zł` i `-1 234,50 zł`, ale nie mówią, jaki to znak spacji (`Intl` dla `pl-PL` używa U+00A0) ani minusa. Testy porównujące łańcuchy będą od tego zależne; warto to zapisać.
4. **Typ wejścia kwoty.** Formatowanie przyjmuje `number`. Nie łamie to niezmiennika 2 (brak obliczeń), ale przesądza kształt kontraktu API dla `decimal(18,2)`, którego jeszcze nie ma (TODO „State and data fetching”). Rozważyć przyjmowanie także łańcucha albo zaznaczyć, że kontrakt ustali pierwsze zadanie pobierające dane.
5. **Filtry na telefonie.** Design guide sekcja 3 i 6: „na telefonie … filtry w rozwijany panel”. Specyfikacja (pkt 12) traktuje filtry jako projekcję treści i nie mówi, czy zwijanie panelu filtrów na telefonie zapewnia tabela, czy moduł.
6. **„Zachowany kontekst” przy przewijaniu tabeli.** Design guide wymaga kontrolowanego przewijania „z zachowanym kontekstem”; pkt 12 opisuje przewijanie w kontenerze, ale nie sposób zachowania kontekstu (np. przyklejona pierwsza kolumna lub nagłówek).
7. **Testowalność dialogu.** Pkt 11 opiera dialog na `showModal()`, a środowisko testów (Vitest + jsdom 30.1.3) nie implementuje `showModal()`. Kryterium „Wspólne elementy … mają testy” dla pułapki i zwrotu fokusu wymaga atrapy lub testu w przeglądarce; specyfikacja tego nie przewiduje, więc część zachowania pozostanie weryfikowana tylko ręcznie (brak pozycji w „Weryfikacja”).
8. **Właściciel menu użytkownika.** „Poza zakresem” i Założenie 2 przypisują menu użytkownika i wylogowanie do SF-6, ale w podziale zadań SF-6 ma „wylogowanie” tylko jako kwestię do rozstrzygnięcia, a menu użytkownika w nagłówku (design guide sekcja 3) nie jest nigdzie jawnie przypisane. Ryzyko osieroconego elementu nagłówka; warto zgłosić to przy specyfikacji SF-6.
9. **Stan zablokowanego przycisku.** Pkt 9 mówi „zablokowany przed ponownym kliknięciem”, ale nie rozstrzyga, czy przez `disabled` (utrata fokusu, element znika z kolejności Tab) czy `aria-disabled` z blokadą zdarzenia. Ma to wpływ na dostępność i na zapis w „Przypadki brzegowe”.

### Unclear or Ambiguous Sections

- **Przypadki brzegowe — „strona poza zakresem”** (tabela): wymieniona jako przypadek, ale bez oczekiwanego wyniku (stan pustki, korekta numeru strony, zdarzenie zmiany strony?).
- **Przypadki brzegowe — „Data w złym formacie”**: dotyczy tylko daty biznesowej; nie wiadomo, czy miesiąc i czas techniczny przy błędnym wejściu też zwracają „Brak danych”.
- **Zachowanie, pkt 10 (pole formularza)**: komponent ma ustawiać `aria-required`, `aria-describedby` i `aria-invalid` na kontrolce podanej przez projekcję treści; nie opisano mechanizmu powiązania (np. dyrektywa na kontrolce), co zostawia planiście decyzję o publicznym API.
- **Zachowanie, pkt 3**: „Element wywołujący dialog usunięty → fokus na `<main>`” wymaga, by `<main>` był fokusowalny (`tabindex="-1"`); wynika to pośrednio, ale nie jest zapisane.
- **Zakres — „skala typografii”**: nie wskazano, czy chodzi o wszystkie pozycje z design guide sekcja 5 (w tym wartości KPI, które należą do SF-19/20), czy tylko o tytuły, tekst i tabele.

### Invented or Unsupported Requirements

None. Elementy dodane ponad dosłowny opis zgłoszenia (sortowanie w tabeli, link „pomiń do treści”, format tytułu dokumentu, aktualizacja `conventions.md` i `role.md`) wynikają z design guide sekcje 6 i 9, PRD sekcja 10 („lazy loading”, „sortowanie”) oraz z TODO zostawionych w kontekście projektu.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Zachowanie, pkt 9–16 (nazwy `sf-*`) | `app/eslint.config.js`, `angular.json` (SF-3) | Prefiks `sf` zamiast wymaganego `app`; lint nie przejdzie bez nieopisanej zmiany konfiguracji. Konflikt z kodem, nie z decyzją. |
| Założenia, pkt 7 (separator tysięcy dla liczb czterocyfrowych) | Reguły CLDR `pl` | Świadome odstępstwo od domyślnego `Intl` (`1234,00 zł`); jawnie opisane i zgodne z przykładem design guide. Nie jest konfliktem z decyzją, wymaga potwierdzenia właściciela. |
| Założenia, pkt 5 (próg 1024 px) | Design guide sekcja 9 (progi 768/1200 px) | Inny próg nawigacji; design guide podaje progi jako propozycję i każe dostosowywać do treści. Jawnie opisane. |
| Założenia, pkt 1 (brak „Ustawienia”) | Design guide sekcja 3 („Integracja / Ustawienia”) | Zgłoszenie każe rozstrzygnąć zakres w specyfikacji; PRD nie definiuje ustawień edytowanych w aplikacji (data uruchomienia jest w konfiguracji wdrożenia), szkic pulpitu (sekcja 4) i podział zadań wymieniają tylko „Integracja”. Rozstrzygnięcie uzasadnione. |

Brak konfliktu z rozstrzygniętymi decyzjami D-01–D-07; zadanie nie dotyka obszarów blokowanych przez D-08 i D-09.

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sprzedaż oddzielona od gotówki | Not applicable | Brak danych finansowych; nawigacja zachowuje osobne sekcje Zamówienia / Wydatki / Operacje. |
| 2. Kwoty decimal na backendzie | OK | Formatowanie tylko prezentuje wartość; brak sumowania, zaokrągleń i przeliczeń netto/brutto. Uwaga o typie `number` w Non-Critical Issues pkt 4. |
| 3. Dostęp wymuszany przez API | Not applicable | Brak wywołań API; powłoka nie udaje uwierzytelnienia. |
| 4. Ponowienia bez duplikatów | Not applicable | Brak integracji i zadań. |
| 5. Ślad audytu finansów | Not applicable | Brak zmian danych finansowych. |
| 6. Sekrety po stronie serwera | OK | Brak adresów, kluczy i konfiguracji w pakiecie frontendu. |
| 7. Granice modułów i warstw | OK | Kod tylko we frontendzie; wspólne elementy w `shared/`, sekcje w `features/{obszar}/`, bez logiki biznesowej w komponentach. |
| 8. Brak danych identyfikujących | OK | Tylko nazwa produktu i dane fikcyjne. |

## Risk

- Recorded: medium
- Assessed: medium — brak pieniędzy, danych klientów i operacji nieodwracalnych, ale motyw, tokeny, powłoka i komponenty wspólne są używane przez wszystkie kolejne ekrany (SF-6 … SF-20), więc błąd dostępności lub API komponentu powieli się wszędzie. Przegląd kodu powinien sprawdzić API tabeli i pola pod kątem list z design guide sekcja 6.

## Assumptions Detected

- Brak pozycji „Ustawienia” w nawigacji — stated (Założenia 1), do potwierdzenia.
- Stan synchronizacji i menu użytkownika nie są renderowane — stated (Założenia 2); przypisanie menu użytkownika do SF-6 nie wynika jednoznacznie z podziału zadań.
- Polskie slugi tras, `/` i nieznane → `/pulpit`, usunięcie strony startowej SF-3 — stated (Założenia 3); implicit: istniejące testy `app.routes.spec.ts` i `start-page.spec.ts` zostaną usunięte lub zastąpione — not stated.
- Zaślepki sekcji ze stanem pustki — stated (Założenia 4).
- Próg mobilny 1024 px — stated (Założenia 5).
- Brak biblioteki ikon, inline SVG — stated (Założenia 6).
- Grupowanie tysięcy także dla liczb czterocyfrowych — stated (Założenia 7).
- Wersje Tailwind 4.3.4, `@tailwindcss/postcss` 4.3.4, PostCSS 8.5.32, daisyUI 5.7.50 — stated (Założenia 8); zgodne z rejestrem npm na 03.11.2026.
- Tabela tylko prezentacyjna, bez lokalnego sortowania i paginacji — stated (Założenia 9), zgodne z PRD sekcja 10 „Listy”.
- Natywny `<dialog>` z `showModal()` — stated (Założenia 10); implicit: zapewnia pułapkę fokusu w testach — not stated (jsdom tego nie obsługuje).
- Brak galerii komponentów — stated (Założenia 11).
- Format czasu technicznego `dd.MM.yyyy, HH:mm` — stated (Założenia 12).
- Kwoty przychodzą z API jako `number` gotowe do wyświetlenia — partially stated (pkt 17), nie oznaczone jako założenie.
- Prefiks selektorów `sf` — not stated; sprzeczny z konfiguracją lint.
- Tło i kolory nawigacji — not stated.

## Recommendation

- Proceed as-is — planowanie może ruszyć; `sdd-plan` powinien rozstrzygnąć prefiks selektorów (najprościej `app`), tokeny nawigacji i fokus na ciemnym tle, znak separatora w testach formatowania oraz sposób testowania dialogu. Rozstrzygnięcia z „Założenia” pozostają do potwierdzenia przez właściciela.
