# SF-15 — Wydatki jednorazowe i zapłata wydatku

| | |
|---|---|
| **Source** | [SF-15](https://standev.youtrack.cloud/issue/SF-15) |
| **Project** | SF |
| **Type** | Zadanie |
| **State** | Otwarta |
| **Priority** | Poważny |
| **Sprint** | |
| **Assignee** | standev |
| **Reporter** | standev |
| **Estimate** | |
| **Created** | 2026-11-02 20:10:54 |
| **Updated** | 2026-11-02 20:15:48 |
| **Fetched** | 2026-11-06 |

> **Uwagi do zgłoszenia (tylko sygnalizacja):**
> - „Zadanie 16”, „zadania 19 i 20” i „Depends on: 6, 7” to lokalne numery z breakdownu, czyli SF-16, SF-19, SF-20 oraz SF-6 i SF-7.
> - Ryzyko high: przed merge wymagany jest przegląd przez człowieka.

---

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

## Metadata
- Breakdown ID: 15
- Risk: high — human review required before merge
- Depends on: 6, 7
- PRD sections: 5 (Wydatek jednorazowy i miesięczny, Zapłata wydatku), 6 (WYD-01, 02, PLAT-02), 7, 8
