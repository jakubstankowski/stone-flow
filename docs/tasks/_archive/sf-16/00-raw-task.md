# SF-16 — Wydatki cykliczne

| | |
|---|---|
| **Source** | [SF-16](https://standev.youtrack.cloud/issue/SF-16) |
| **Project** | SF |
| **Type** | Zadanie |
| **State** | Otwarta |
| **Priority** | Poważny |
| **Sprint** | |
| **Assignee** | standev |
| **Reporter** | standev |
| **Estimate** | |
| **Created** | 2026-11-02 20:10:54 |
| **Updated** | 2026-11-02 20:15:50 |
| **Fetched** | 2026-11-08 |

> **Uwagi do zgłoszenia (tylko sygnalizacja):**
> - „Depends on: 11, 15” to lokalne numery z breakdownu, czyli SF-11 (harmonogram) i SF-15 (wydatki).
> - SF-15 przygotowało kontrakt: wystąpienie to zwykły wiersz `expenses.Expenses`, rejestr par definicja–okres powstaje przy tworzeniu wystąpienia, opcjonalny rodzic w audycie (`docs/tasks/sf-15/02-spec.md`).
> - Ryzyko high: przed merge wymagany jest przegląd przez człowieka.

---

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

## Metadata
- Breakdown ID: 16
- Risk: high — human review required before merge
- Depends on: 11, 15
- PRD sections: 5 (Wydatek jednorazowy i miesięczny), 6 (WYD-03–06), 7, 12 (D-04)
