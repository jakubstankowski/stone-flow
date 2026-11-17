# SF-13 — Uzupełnianie luk, historia błędów i ponowienia

| | |
|---|---|
| **Source** | [SF-13](https://standev.youtrack.cloud/issue/SF-13) |
| **Project** | SF |
| **Type** | Zadanie |
| **State** | Otwarta |
| **Priority** | Poważny |
| **Sprint** | |
| **Assignee** | standev |
| **Reporter** | standev |
| **Estimate** | |
| **Created** | 2026-11-02 20:10:54 |
| **Updated** | 2026-11-02 20:15:45 |
| **Fetched** | 2026-11-06 |

> **Uwagi do zgłoszenia (tylko sygnalizacja):**
> - „Zadanie 14” i „Depends on: 11, 12” to lokalne numery z breakdownu, czyli SF-14 oraz SF-11 i SF-12.
> - SF-12 nie pobiera danych z REST API; SF-13 wprowadza pierwsze wywołania do sklepu i klucze REST (SF-2: `modified_after`, paginacja niezweryfikowane na sklepie).
> - Ryzyko high: przed merge wymagany jest przegląd przez człowieka.

---

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

## Metadata
- Breakdown ID: 13
- Risk: high — human review required before merge
- Depends on: 11, 12
- PRD sections: 5 (Sukces synchronizacji, Ponowienie ręczne), 6 (ZAM-02, INT-01), 7, 10
