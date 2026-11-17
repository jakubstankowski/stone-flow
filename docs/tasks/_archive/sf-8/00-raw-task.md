# SF-8 — Zamówienia ręczne, wspólna lista, anulowanie i przywracanie

| | |
|---|---|
| **Source** | [SF-8](https://standev.youtrack.cloud/issue/SF-8) |
| **Project** | SF |
| **Type** | Zadanie |
| **State** | Otwarta |
| **Priority** | Poważny |
| **Sprint** | |
| **Assignee** | standev |
| **Reporter** | standev |
| **Estimate** | |
| **Created** | 2026-11-02 20:10:53 |
| **Updated** | 2026-11-02 20:15:38 |
| **Fetched** | 2026-11-05 |

> **Uwagi do zgłoszenia (tylko sygnalizacja):**
> - Numery „zadanie 9 / 12 / 19” i „Depends on: 6, 7” to lokalne numery z breakdownu. Według `task-breakdown.md` odpowiadają SF-9, SF-12, SF-19 oraz SF-6 i SF-7.
> - „Korekta wartości zamówienia ręcznego z audytem” jest w zakresie, a zadanie 10 (SF-10) to „Korekta sprzedaży i limit obniżeń”. Granica między nimi nie jest opisana w zgłoszeniu.
> - Metadane wymieniają PLAT-06, a wpłaty są poza zakresem (zadanie 9). Nie wiadomo, którą część PLAT-06 obejmuje to zadanie.
> - Ryzyko high: przed merge wymagany jest przegląd przez człowieka.

---

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

## Metadata
- Breakdown ID: 8
- Risk: high — human review required before merge
- Depends on: 6, 7
- PRD sections: 5 (Zamówienie ręczne, Anulowanie i zwrot, Korekta wartości), 6 (ZAM-01, 03, 04, 05, 08, PLAT-06), 7
