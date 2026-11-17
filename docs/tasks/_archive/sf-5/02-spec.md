# Specyfikacja zadania — SF-5

## Źródło

[SF-5](https://standev.youtrack.cloud/issue/SF-5) — Motyw daisyUI i powłoka aplikacji.
PRD sekcja 10; design guide (`docs/DESIGN-GUID-stone-flow.md`) sekcje 2, 3, 5, 6, 8, 9.

## Cel

Dodać do aplikacji Angular (`app/`) Tailwind CSS i jeden własny jasny motyw daisyUI zbudowany
z centralnych tokenów CSS (paleta obowiązkowa i tokeny uzupełniające z design guide), powłokę
aplikacji (nagłówek z nazwą produktu i bieżącej sekcji, główna nawigacja z sześcioma leniwie
ładowanymi sekcjami, menu na telefonie) oraz zestaw wspólnych elementów UI wielokrotnego
użytku: przyciski, pole formularza, dialog potwierdzenia, tabelę z filtrami, sortowaniem i
paginacją, etykietę statusu, stany ładowania, pustki i błędu, a także formatowanie kwot i dat
po polsku. Zadanie nie dodaje treści ekranów modułów ani komunikacji z API.

## Kontekst

Warstwa: frontend (`app/`). Zgodnie z `conventions.md`: standalone, strict TypeScript i
szablony, obszary funkcjonalne w `app/src/app/features/{area}/` ładowane przez
`loadComponent`, nazewnictwo plików Angular CLI 22 (bez sufiksu `.component`), Tailwind CSS
do układu, daisyUI do komponentów, kolory tylko w zmiennych CSS i jednym motywie daisyUI.
Wspólne elementy UI trafiają do `app/src/app/shared/` (nowy katalog; konwencja „wspólne
powtarzalne elementy UI, bez budowania systemu komponentów z wyprzedzeniem”).

Stan obecny (SF-3): aplikacja ma jedną stronę startową `features/start/start-page` pod `''`,
przekierowanie `**` → `''`, pusty `styles.css`, brak Tailwind i daisyUI. Backend nie jest
dotykany.

## Zakres

### W zakresie

- Instalacja Tailwind CSS 4 (`tailwindcss`, `@tailwindcss/postcss`, `postcss`) i daisyUI 5 w
  dokładnych wersjach; konfiguracja PostCSS dla buildera `@angular/build`.
- `app/src/styles.css`: tokeny `--sf-*` (tabela z design guide sekcja 2), jeden własny motyw
  daisyUI `stone-flow` (jasny, domyślny, jedyny) mapowany według design guide „Mapowanie
  motywu daisyUI”, font systemowy, skala typografii, widoczny fokus, `prefers-reduced-motion`.
- Powłoka (`app/src/app/layout/`): nagłówek, nawigacja główna, obszar treści.
- Trasy sekcji z leniwym ładowaniem i stronami-zaślepkami: Pulpit, Zamówienia, Wydatki,
  Operacje, Historia zmian, Integracja.
- Wspólne elementy w `app/src/app/shared/ui/`: przycisk (główny, drugorzędny, destrukcyjny,
  stan zapisu), pole formularza (etykieta, wymagalność, błąd), dialog potwierdzenia, tabela
  danych (filtry, aktywne filtry, „Wyczyść filtry”, sortowanie, paginacja), etykieta statusu,
  stan ładowania, stan pustki, stan błędu.
- Formatowanie w `app/src/app/shared/format/`: kwota PLN, data, miesiąc z rokiem, czas
  techniczny w Europe/Warsaw, brak wartości jako „Brak danych”.
- Testy jednostkowe (Vitest) dla powyższych.
- Aktualizacja `.sdd/context/conventions.md` (katalogi `layout/`, `shared/`, trasy) i
  `.sdd/context/role.md` (wersje Tailwind/daisyUI — TODO pozostawione przez SF-3).

### Poza zakresem

- Treść ekranów modułów, karty KPI, wykresy, formularze konkretnych obiektów (SF-8 i dalej).
- Ciemny motyw.
- Pobieranie danych z API, podejście do stanu i fetchowania (TODO w `conventions.md` zostaje
  do pierwszego zadania pobierającego dane).
- Stan synchronizacji w nagłówku (SF-14) i menu użytkownika / wylogowanie / wygaśnięcie sesji
  (SF-6) — powłoka nie pokazuje tych elementów, dopóki nie istnieją ich dane.
- Pozycja „Ustawienia” (patrz Założenia).
- Słownik statusów biznesowych i mapowanie WooCommerce (PRD, zadania modułów).
- Wyszukiwanie globalne, przełącznik firm, biblioteka ikon.
- Wdrożenia (SF-4).

## Zachowanie

1. Aplikacja ładuje się z motywem `stone-flow` ustawionym na `<html data-theme="stone-flow">`.
   Tło strony to `--sf-bg`, tekst `--sf-text`. Żaden komponent nie zawiera literału koloru;
   kolory pochodzą z tokenów `--sf-*` albo klas semantycznych daisyUI wynikających z motywu.
2. Wejście na `/` przekierowuje na `/pulpit`. Nieznana ścieżka przekierowuje na `/pulpit`.
   Strona startowa z SF-3 (`features/start`) zostaje zastąpiona przez sekcję Pulpit.
3. Powłoka składa się z: linku „pomiń do treści” (pierwszy element fokusowalny), nagłówka
   (`<header>`) z nazwą produktu „Stone Flow” (link do `/pulpit`) i nazwą bieżącej sekcji,
   nawigacji głównej (`<nav aria-label="Główna">`) oraz obszaru treści (`<main id="tresc">`).
   Szerokość obszaru maksymalnie 1600 px, marginesy boczne 24–32 px na szerokim ekranie,
   16 px na telefonie.
4. Nawigacja zawiera w tej kolejności: Pulpit (`/pulpit`), Zamówienia (`/zamowienia`),
   Wydatki (`/wydatki`), Operacje (`/operacje`), Historia zmian (`/historia-zmian`),
   Integracja (`/integracja`). Każda sekcja to osobna trasa z `loadComponent` w
   `features/{obszar}/`.
5. Aktywna pozycja ma wyraźne tło, kolor tekstu i `aria-current="page"`. Tytuł dokumentu ma
   postać „{Sekcja} · Stone Flow”, nazwa sekcji w nagłówku odpowiada aktywnej trasie.
6. Od 1024 px nawigacja jest pozioma pod nagłówkiem. Poniżej 1024 px zamiast niej jest przycisk
   „Menu” (`aria-expanded`, `aria-controls`), który rozwija pionową listę tych samych pozycji.
   Wybór pozycji lub klawisz Escape zwija menu; Escape zwraca fokus na przycisk „Menu”.
   Wszystkie pozycje są osiągalne klawiszem Tab, obszar kliknięcia ma co najmniej 44 × 44 px.
7. Strona sekcji (zaślepka) ma nagłówek `<h1>` z nazwą sekcji i stan pustki z tekstem
   „Ten widok powstanie w kolejnych etapach.” Strona nie wymaga poziomego przewijania przy
   szerokości 320 px.
8. Fokus wszystkich elementów interaktywnych jest widoczny: obwódka 2 px w kolorze
   `--sf-text` z odstępem 2 px (na ciemnym tle przycisku głównego obwódka nadal na jasnym tle
   strony dzięki odstępowi). `prefers-reduced-motion: reduce` wyłącza animacje i przejścia.
9. **Przycisk** (`sf-button` jako dyrektywa na natywnym `<button>`/`<a>`): warianty
   `primary` (tło `--sf-accent`, tekst biały, hover `--sf-accent-hover`), `secondary` (jasne
   tło, granica `--sf-border-strong`), `danger` (`--sf-error`, tekst biały). Wejście `busy`:
   przycisk ma `aria-busy="true"`, jest zablokowany przed ponownym kliknięciem i pokazuje tekst
   podany jako `busyLabel` (domyślnie „Zapisywanie…”). Wysokość co najmniej 44 px, promień
   8–12 px.
10. **Pole formularza** (`sf-field`): etykieta nad polem powiązana z kontrolką; przy
    wymaganym polu dopisek „(wymagane)” widoczny i odczytywany oraz `aria-required` na
    kontrolce; tekst pomocniczy i komunikat błędu pod polem powiązane przez
    `aria-describedby`; przy błędzie `aria-invalid="true"` i granica `--sf-error`.
    Kontrolka jest projekcją treści (natywny `input`/`select`/`textarea`), komponent nie
    zawiera logiki walidacji.
11. **Dialog potwierdzenia** (`sf-confirm-dialog`) oparty o natywny `<dialog>` otwierany
    `showModal()`: tytuł, treść opisująca obiekt i skutek, przyciski akcji (etykieta podana
    przez wywołującego, np. „Usuń”, wariant `danger` lub `primary`) i „Anuluj”. Fokus trafia
    na „Anuluj” przy otwarciu, jest uwięziony w dialogu, Escape i „Anuluj” zamykają z
    wynikiem `false`, akcja z wynikiem `true`. Po zamknięciu fokus wraca do elementu, który był
    aktywny przed otwarciem.
12. **Tabela danych** (`sf-data-table`): komponent prezentacyjny, nie pobiera danych.
    - Wejścia: definicje kolumn (klucz, nagłówek, wyrównanie, czy sortowalna), wiersze
      bieżącej strony, łączna liczba rekordów, numer strony, rozmiar strony, bieżące
      sortowanie, lista aktywnych filtrów (etykieta), stany `loading` / `error`, teksty pustki.
    - Wyjścia: zmiana strony, zmiana sortowania, usunięcie pojedynczego filtra, „Wyczyść
      filtry”, „Spróbuj ponownie”.
    - Filtry są projekcją treści nad tabelą (formularz filtrów należy do modułu); pod nimi lista
      aktywnych filtrów z przyciskiem usunięcia każdego i „Wyczyść filtry”.
    - Sortowalny nagłówek to przycisk; kolumna sortowana ma `aria-sort`
      (`ascending`/`descending`) i widoczny wskaźnik kierunku tekstem/ikoną.
    - Paginacja: tekst „{od}–{do} z {liczba}” oraz przyciski „Poprzednia”/„Następna”
      wyłączone na krańcach; zmiana strony przez wyjście.
    - Kolumny kwot wyrównane do prawej z `tabular-nums`.
    - Na wąskim ekranie tabela ma kontrolowane przewijanie poziome wewnątrz własnego
      kontenera (z dostępną nazwą i fokusowalnym obszarem), strona jako całość nie przewija się
      poziomo.
    - Stany: `loading` → stan ładowania; `error` → stan błędu z „Spróbuj ponownie”; brak
      wierszy bez aktywnych filtrów → stan pustki „pusta ewidencja”; brak wierszy przy
      aktywnych filtrach → stan pustki „brak wyników filtrowania” z „Wyczyść filtry”.
13. **Etykieta statusu** (`sf-status`): wymagany tekst, wariant tonu (`neutral`, `success`,
    `warning`, `error`, `info`), opcjonalna ikona dekoracyjna (`aria-hidden`). Ton nigdy nie
    jest jedynym nośnikiem informacji — tekst jest zawsze widoczny. Komponent nie definiuje
    słownika statusów biznesowych.
14. **Stan ładowania** (`sf-loading-state`): szkielet o zadanej liczbie wierszy, `role="status"`
    i tekst dla czytnika „Ładowanie…”, bez fikcyjnych zer. Animacja wyłączona przy
    `prefers-reduced-motion`.
15. **Stan pustki** (`sf-empty-state`): tytuł, opis i opcjonalna akcja (projekcja treści).
16. **Stan błędu** (`sf-error-state`): `role="alert"`, opis skutku (domyślnie „Nie udało się
    wczytać danych.”) i przycisk „Spróbuj ponownie” emitujący zdarzenie; bez szczegółów
    technicznych.
17. **Formatowanie** (czyste funkcje i pipe’y standalone w `shared/format/`, bez obliczeń
    finansowych):
    - kwota: `number` → `12 345,67 zł` (zawsze dwa miejsca po przecinku, separator tysięcy
      także dla liczb czterocyfrowych, np. `1 234,00 zł`, minus dla wartości ujemnych);
      `0` → `0,00 zł`; `null`/`undefined` → „Brak danych”;
    - data biznesowa: `yyyy-MM-dd` (string) → `28.10.2026`; brak → „Brak danych”;
    - miesiąc: `yyyy-MM` lub data → `październik 2026`;
    - czas techniczny: znacznik UTC (ISO 8601) → `28.10.2026, 14:05` w strefie Europe/Warsaw;
    - rejestracja lokalizacji `pl` i `LOCALE_ID = 'pl-PL'` w konfiguracji aplikacji.
    Kwoty są przyjmowane w postaci gotowej do wyświetlenia (z API); funkcje nie sumują, nie
    zaokrąglają do celów rozliczeń i nie przeliczają netto/brutto.

## Dane / API

### Storage

- Brak.

### Endpointy

Brak. Frontend nie wywołuje API w tym zadaniu.

### Typy przekraczające granicę

- Brak granicy z backendem. Typy wejść komponentów wspólnych (kolumny tabeli, sortowanie,
  aktywny filtr, ton statusu) są publiczne dla obszarów funkcjonalnych i eksportowane z
  `shared/ui/`.

## Wpływ na niezmienniki

| Niezmiennik | Jak jest respektowany |
|---|---|
| 1. Sprzedaż oddzielona od gotówki | Brak danych finansowych; zaślepki nie łączą pojęć. Nawigacja zachowuje osobne sekcje z PRD. |
| 2. Kwoty decimal na backendzie | Formatowanie kwot tylko prezentuje wartość z API; brak sumowania, zaokrąglania rozliczeniowego i przeliczeń netto/brutto we frontendzie. |
| 3. Dostęp wymuszany przez API | Brak wywołań API. Powłoka nie udaje uwierzytelnienia (menu użytkownika w SF-6). |
| 6. Sekrety po stronie serwera | Brak konfiguracji, adresów API i kluczy w pakiecie. |
| 8. Brak danych identyfikujących | Teksty i testy używają tylko nazwy produktu i danych fikcyjnych. |

Niezmienniki 4, 5, 7 — nie dotyczy.

## Ryzyko

**medium** — zgodnie z `task.md`. Brak pieniędzy, danych klientów i operacji nieodwracalnych,
ale motyw, tokeny, powłoka i wspólne komponenty są używane przez każdy kolejny ekran (SF-6 …
SF-20); błąd dostępności lub kontrastu powiela się wszędzie. Przegląd pod kątem konsumentów:
API komponentów wspólnych musi wystarczyć list zamówień, wydatków, operacji i historii zmian
z design guide sekcja 6.

## UI

- **Trasy:** `/` → `/pulpit`; `/pulpit`, `/zamowienia`, `/wydatki`, `/operacje`,
  `/historia-zmian`, `/integracja`; `**` → `/pulpit`. Każda z `loadComponent`, `title`
  w formacie „{Sekcja} · Stone Flow”.
- **Komponenty:** powłoka (`layout/app-shell`, `layout/main-nav`), zaślepki sekcji
  (`features/{obszar}/{obszar}-page`), wspólne z `shared/ui/` i `shared/format/` jak w
  „Zachowanie”.
- **Stany:** ładowanie, pustka (ewidencja / wynik filtra), błąd — jako komponenty wspólne;
  zaślepki pokazują stan pustki.
- **Tokeny:** tabela „Paleta obowiązkowa” i „Proponowane tokeny uzupełniające” z design guide;
  motyw daisyUI według „Mapowanie motywu daisyUI”; główna akcja — przycisk `primary`.
- **Dostępność:** WCAG 2.2 AA; link „pomiń do treści”; landmarki `header`, `nav`, `main`;
  widoczny fokus; nawigacja klawiaturą; menu mobilne z `aria-expanded`; dialog z pułapką i
  zwrotem fokusu; `aria-current`, `aria-sort`, `aria-busy`, `aria-invalid`,
  `aria-describedby`; status nie tylko kolorem; brak poziomego przewijania strony od 320 px;
  `prefers-reduced-motion`.

## Przypadki brzegowe

- Ścieżka `/` lub nieznana → `/pulpit`.
- Szerokość 320 px → strona bez poziomego przewijania; tabela przewija się tylko wewnątrz
  swojego kontenera.
- Menu mobilne otwarte i zmiana rozmiaru do ≥ 1024 px → nawigacja pozioma, stan menu nie
  blokuje treści.
- Dialog zamknięty klawiszem Escape → wynik `false`, fokus wraca do wywołującego.
- Element wywołujący dialog usunięty z DOM przed zamknięciem → fokus na `<main>`.
- Przycisk w stanie `busy` klikany wielokrotnie → zdarzenie kliknięcia nie jest emitowane.
- Tabela: `total = 0`; strona poza zakresem; rozmiar strony większy niż `total`; ostatnia
  niepełna strona („41–45 z 45”).
- Kwota `0` → `0,00 zł`; `null` → „Brak danych”; ujemna → `-1 234,50 zł`; `1234.5` →
  `1 234,50 zł`; bardzo duża (`1234567.89`) → `1 234 567,89 zł`.
- Data w złym formacie → „Brak danych” (bez wyjątku w szablonie).
- Czas UTC tuż przed północą (`2026-10-24T22:30:00Z`) → `25.10.2026, 00:30` (Europe/Warsaw,
  czas letni); zmiana czasu z letniego na zimowy obsłużona przez `Intl`.

## Zależności

- **SF-3** — zakończone (merge PR #1): aplikacja Angular, lint, testy, CI.
- Brak zależności od SF-4 i D-08.
- SF-6 (menu użytkownika, wygaśnięcie sesji) i SF-14 (stan synchronizacji) rozszerzą nagłówek.

## Założenia

Pytania doprecyzowujące zostały rozstrzygnięte bez udziału właściciela: 03.11.2026 właściciel
polecił wykonać cały proces SF-5 samodzielnie podczas nieobecności. Każde rozstrzygnięcie
poniżej jest **do potwierdzenia** przy powrocie.

1. **Zakres „Ustawienia”** (zgłoszenie: „Do rozstrzygnięcia w specyfikacji”). PRD nie
   definiuje żadnych ustawień edytowanych przez użytkownika; design guide łączy je z pozycją
   „Integracja / Ustawienia”, a szkic pulpitu (sekcja 4) wymienia tylko „Integracja”.
   → Rozstrzygnięcie: w SF-5 brak pozycji „Ustawienia”; nawigacja ma pozycję „Integracja”.
   Ustawienia dodaje zadanie, które wprowadzi pierwsze ustawienie.
2. **Elementy nagłówka bez danych** (stan synchronizacji, menu użytkownika). → Nie są
   renderowane w SF-5 (brak atrap); dodają je SF-14 i SF-6.
3. **Ścieżki tras.** → Polskie slugi bez znaków diakrytycznych (`/pulpit`, `/zamowienia`,
   `/wydatki`, `/operacje`, `/historia-zmian`, `/integracja`); `/` i nieznane → `/pulpit`;
   strona startowa z SF-3 usunięta.
4. **Zaślepki sekcji.** → Każda sekcja ma stronę z `<h1>` i stanem pustki „Ten widok powstanie
   w kolejnych etapach.”; treść ekranów jest poza zakresem.
5. **Próg nawigacji mobilnej.** → 1024 px (sześć pozycji nie mieści się w poziomie przy
   768 px z marginesami); design guide podaje progi jako propozycję.
6. **Biblioteka ikon.** → Brak w SF-5; ikony (hamburger, kierunek sortowania) jako inline SVG
   z `aria-hidden`. Wybór zestawu ikon odłożony do pierwszego ekranu, który go potrzebuje.
7. **Grupowanie tysięcy.** Polskie reguły CLDR nie grupują liczb czterocyfrowych (`1234,00`).
   → Kwoty zawsze z separatorem tysięcy (`1 234,00 zł`), dla jednolitości kolumn i zgodności
   z przykładem `12 345,67 zł` z design guide.
8. **Wersje.** → Tailwind CSS 4.3.4, `@tailwindcss/postcss` 4.3.4, PostCSS 8.5.32, daisyUI
   5.7.50 (najnowsze stabilne w npm 03.11.2026; SF-1 odczytał Tailwind 4.3.3 i daisyUI
   5.7.50). Dokładne wersje w `package.json`.
9. **Tabela danych prezentacyjna.** Konwencja: listy filtrują, sortują i paginują przez API.
   → Komponent przyjmuje gotową stronę danych i emituje zdarzenia; nie sortuje ani nie
   paginuje lokalnie.
10. **Dialog na natywnym `<dialog>`.** → `showModal()` zapewnia modalność i blokadę tła;
    zwrot fokusu realizuje komponent.
11. **Brak strony z galerią komponentów.** → Komponenty wspólne są weryfikowane testami
    jednostkowymi; nie powstaje dodatkowa trasa demonstracyjna.
12. **Czas techniczny.** → Format `dd.MM.yyyy, HH:mm` w Europe/Warsaw (design guide podaje
    tylko format daty i strefę).

## Kryteria odbioru

- [ ] Tailwind CSS i daisyUI zainstalowane w dokładnych wersjach; build produkcyjny
      przechodzi.
- [ ] Obowiązkowe cztery kolory (`#ebe8e5`, `#323135`, `#9b6e2b`, `#b4a091`) i tokeny
      uzupełniające są zapisane centralnie w `styles.css`, w jednym motywie daisyUI; w
      `app/src/app/**` nie ma literałów HEX/RGB/HSL kolorów (sprawdzane testem/grepem).
- [ ] Użyte pary kolorów spełniają WCAG 2.2 AA (tabela kontrastów w planie, sprawdzona
      obliczeniowo), a status zawsze ma tekst.
- [ ] Nawigacja z sześcioma pozycjami działa klawiaturą i na telefonie (menu „Menu”),
      aktywna pozycja ma `aria-current="page"`, trasy są ładowane leniwie.
- [ ] Fokus jest widoczny; dialog potwierdzenia przechwytuje fokus i zwraca go do elementu
      wywołującego.
- [ ] Strona nie wymaga poziomego przewijania przy szerokości 320 px.
- [ ] Wspólne elementy (przycisk, pole, dialog, tabela, status, stany ładowania/pustki/błędu)
      istnieją i mają testy.
- [ ] Kwoty i daty formatowane po polsku według przykładów z „Przypadki brzegowe”.
- [ ] `conventions.md` i `role.md` zaktualizowane.
- [ ] `npm run format:check`, `lint`, `typecheck`, `build`, `test` przechodzą.

## Weryfikacja

- build: `npm ci && npm run build` w `app/`
- testy: `npm test -- --watch=false`
- format/lint/typy: `npm run format:check && npm run lint && npm run typecheck`
- grep: brak literałów kolorów w `app/src/app/**` (`#[0-9a-fA-F]{3,8}\b`, `rgb(`, `hsl(`)
- ręcznie (`npm start`):
  - nawigacja Tab/Enter przez wszystkie pozycje, widoczny fokus, link „pomiń do treści”;
  - szerokość 320 px i 375 px: brak poziomego przewijania, menu „Menu” działa, Escape zwija;
  - odświeżenie `/wydatki` w dev serverze ładuje sekcję;
  - `/` i `/cokolwiek` → `/pulpit`.
