# SF-5 — Motyw daisyUI i powłoka aplikacji

| | |
|---|---|
| **Source** | [SF-5](https://standev.youtrack.cloud/issue/SF-5) |
| **Project** | SF |
| **Type** | Zadanie |
| **State** | Otwarta |
| **Priority** | Normalny |
| **Sprint** | |
| **Assignee** | standev |
| **Reporter** | standev |
| **Estimate** | |
| **Created** | 2026-11-02 20:10:52 |
| **Updated** | 2026-11-02 20:15:32 |
| **Fetched** | 2026-11-03 |

---

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

## Metadata
- Breakdown ID: 5
- Risk: medium
- Depends on: 3
- PRD sections: 10; design guide 2, 3, 5, 6, 8, 9
