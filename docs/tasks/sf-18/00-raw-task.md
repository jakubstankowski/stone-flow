# SF-18 — Lista „Historia zmian”

| | |
|---|---|
| **Source** | [SF-18](https://standev.youtrack.cloud/issue/SF-18) |
| **Project** | SF |
| **Type** | Zadanie |
| **State** | Otwarta |
| **Priority** | Poważny |
| **Sprint** | |
| **Assignee** | |
| **Reporter** | standev |
| **Estimate** | |
| **Created** | 2026-11-02 20:10:54 |
| **Updated** | 2026-11-02 20:11:46 |
| **Fetched** | 2026-11-09 |

---

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

## Metadata
- Breakdown ID: 18
- Risk: high — human review required before merge
- Depends on: 8
- PRD sections: 5 (Historia zmian), 6 (AUD-01), 10
