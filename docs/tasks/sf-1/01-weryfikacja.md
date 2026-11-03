# SF-1 — weryfikacja hostingu i technologii

Data: 02.11.2026. Status: częściowa weryfikacja publicznych źródeł; zadanie nie jest zakończone.
Zgłoszenie: https://standev.youtrack.cloud/issue/SF-1

## Potwierdzone w dokumentacji

W PRD występuje nazwa „SMART ASP.NET”. Poniższe wyniki dotyczą **SmarterASP.NET**.
Właściciel potwierdził dostawcę linkiem do oferty 02.11.2026.

## Ustalenie właściciela z 02.11.2026

Po porównaniu Basic i Premium właściciel wybrał **.NET Premium W1050**.
Basic ma 256 MB puli, 1 GB SQL i nie obejmuje harmonogramu; nie został wybrany.
Nie potwierdzono zakupu ani konfiguracji konta Premium. Wybór planu nie zamyka
D-08: nadal potrzebne są dowody techniczne, cele wydajności i próba odtworzenia.

Właściciel zaakceptował: codzienna kopia SQL, retencja 30 dni w niezależnym
miejscu, cel RPO 24 h i RTO 8 h. Są to cele do potwierdzenia próbą, nie gwarancja
dostawcy. Podał jednego użytkownika (właściciel) i około 20–30 zamówień miesięcznie
przez sklep. Liczba zamówień ręcznych i wzrost danych audytowych nie zostały określone.

- [Oferta hostingu](https://www.smarterasp.net/hosting_plans) deklaruje .NET 10.x.
  Premium W1050 ma 3 GB puli aplikacji i 10 GB MSSQL. Deklaracja oferty nie potwierdza
  wersji runtime ani dostępnych ustawień na konkretnym koncie.
- [Harmonogram](https://www.smarterasp.net/support/kb/a2385/how-to-schedule-a-task.aspx)
  jest dostępny od Premium, domyślnie trzy zadania. Wywołuje HTTP GET, minimalny
  interwał to 15 minut. Nie potwierdzono obsługi nagłówków autoryzacji, limitu czasu,
  ponowień, strefy czasowej ani ustawień usypiania/recyklingu puli.
- [Opis kopii SQL](https://www.smarterasp.net/support/kb/a2042/how-can-i-do-custom-backup-on-my-database.aspx)
  podaje kopie co drugi dzień i płatny CustomBackup z retencją tygodnia.
  Artykuł był modyfikowany w 2021 r.; bieżące warunki wymagają potwierdzenia w panelu
  lub u dostawcy. Nie stanowi to potwierdzenia RPO ani RTO aplikacji.

## Wersje odczytane ze źródeł

| Technologia | Wynik | Źródło |
|---|---|---|
| .NET | runtime 10.0.14, SDK 10.0.403; .NET 11 widnieje jako RC | https://dotnet.microsoft.com/en-us/download/dotnet/10.0 |
| Angular | 22.2.4 oznaczone Latest; 22.3.0-next.3 jest prerelease | https://github.com/angular/angular/releases |
| Tailwind CSS | 4.3.3 oznaczone Latest | https://github.com/tailwindlabs/tailwindcss/releases |
| daisyUI | dokumentacja pokazuje 5.7.50 | https://daisyui.com/docs/install/angular/ |

[Macierz Angular](https://angular.dev/reference/versions) pokazuje dla 22.0.x:
Node ^22.22.3 / ^24.15.0 / ^26.0.0, TypeScript >=6.0.0 <6.1.0 i RxJS
^6.5.3 / ^7.4.0. Brak osobnego wpisu 22.2.x w odczytanej tabeli: przed przypięciem
wersji trzeba sprawdzić metadane pakietów i wykonać build.
[Tailwind](https://tailwindcss.com/docs/installation/framework-guides/angular)
oraz daisyUI publikują instrukcje instalacji dla Angular. Build aplikacji nie był
wykonywany — repozytorium nie zawiera jeszcze aplikacji ani lockfile.

## Do ustalenia przed zamknięciem D-08

1. Potwierdzenie aktywacji wybranego planu, regionu, SQL Server i patcha runtime.
2. Dowód z panelu/próby dla harmonogramu, restartu i usypiania; bezpieczne
   uwierzytelnienie wywołań. Sam timer w procesie nie spełnia PRD.
3. Wybór mechanizmu trwałości: stan i ostatni sukces w SQL, blokada równoległości,
   idempotencja oraz nadrobienie zaległych okresów. To wymagania do projektu
   zadania SF-11, a nie wdrożone rozwiązanie.
4. Wybór konkretnego miejsca niezależnej kopii i potwierdzenie kosztów realizacji
   zaakceptowanych celów, określenie mierzalnych celów wydajności i próba odtworzenia.

Nie zmieniono D-08 w PRD na rozstrzygnięte. Nie kupowano planu, nie konfigurowano
hostingu i nie wykonywano wdrożenia. Warunki akceptacji pozostają otwarte.
