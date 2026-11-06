# SF-11 — Trwały harmonogram zadań

| | |
|---|---|
| **Source** | [SF-11](https://standev.youtrack.cloud/issue/SF-11) |
| **Project** | SF |
| **Type** | Zadanie |
| **State** | W realizacji |
| **Priority** | Normalny |
| **Sprint** | |
| **Assignee** | standev |
| **Reporter** | standev |
| **Estimate** | |
| **Created** | 2026-11-02 20:10:53 |
| **Updated** | 2026-11-05 15:54:08 |
| **Fetched** | 2026-11-05 |

> **Uwagi do zgłoszenia (tylko sygnalizacja):**
> - „Zadanie 1”, „zadanie 13”, „zadanie 16” i „Depends on: 1, 3” to lokalne numery z breakdownu, czyli SF-1, SF-13, SF-16 oraz SF-1 i SF-3.
> - „Mechanizm zadań zgodny z wynikiem zadania 1” — wynik weryfikacji hostingu (SF-1) trzeba odczytać z artefaktów SF-1; zgłoszenie go nie przytacza.
> - Sposób sygnalizowania niepowodzenia zadania jest celowo pozostawiony do rozstrzygnięcia w specyfikacji.

---

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

## Metadata
- Breakdown ID: 11
- Risk: medium
- Depends on: 1, 3
- PRD sections: 10 (Automatyzacja)
