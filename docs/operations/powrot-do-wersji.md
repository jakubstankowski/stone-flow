# Powrót do działającej wersji

Procedura powrotu Stone Flow do poprzedniej, działającej wersji (SF-21, PRD §10). Frontend działa
na Vercel, backend na SmarterASP.NET; publikacja opisana jest w [`wdrozenia.md`](../../wdrozenia.md).
Kroki właściciela lub administratora oznaczono **[właściciel]**; dowody trafiają do
[`odbior-mvp.md`](odbior-mvp.md).

## 1. Zasada: rollback kodu nie cofa migracji

- Migracje są expand/contract (`wdrozenia.md` §6), więc poprzednia wersja API działa na nowszym
  schemacie bazy. Powrót kodu zostawia schemat bez zmian.
- Schemat cofa wyłącznie odtworzenie kopii bazy ([`kopie-i-odtworzenie.md`](kopie-i-odtworzenie.md)
  §7), z utratą danych zapisanych po kopii. To ostateczność.
- Zastosowanych migracji nigdy się nie edytuje ani nie usuwa (fix forward).

## 2. Frontend (Vercel) — natychmiast

1. Panel Vercel → projekt → Deployments → poprzednie działające wdrożenie produkcyjne →
   **Instant Rollback** (albo **Promote to Production**). Bez budowy, działa od razu.
2. Kolejna publikacja z `main` nadpisze ten stan, dlatego **równolegle** wykonaj revert z sekcji 3.
3. Poprzedni frontend musi działać z bieżącym API (`wdrozenia.md` §6) — to wymaganie każdej zmiany API.
4. Kontrola: `https://<adres-aplikacji>/logowanie` (odświeżenie podstrony SPA) i
   `https://<adres-aplikacji>/api/auth/csrf` = 204.

## 3. Backend (SmarterASP.NET) — revert na `main`

`check-fresh-commit.sh` nie pozwala ponownie opublikować starszego commitu, więc jedyną drogą
powrotu backendu jest nowy commit na `main`:

1. `git revert <sha>` problematycznego commitu na nowej gałęzi, PR do `main`.
2. Wymagane kontrole PR (`backend`, `frontend`, `secrets`, `deploy-dry-run`, `e2e`) muszą być zielone.
3. Merge uruchamia `deploy.yml`: migracje (nic nowego) → API → `/health` → frontend.
4. Kontrola: `GET https://<adres-api>/health` = 200, logowanie, Pulpit.

### 3a. Commit zawiera migrację — częściowy revert

Pełny revert usunąłby plik zastosowanej migracji i zmienił snapshot modelu — **zabronione**. Jeśli
migracja jest krokiem „expand” (dodaje kolumnę, tabelę, indeks), cofnij tylko logikę:

```bash
git switch -c revert-<sha> origin/main
git revert --no-commit <sha>
# Przywróć z <sha> pliki migracji, snapshot modelu i mapowania EF dodanych elementów schematu:
git checkout <sha> -- api/src/Modules/<Moduł>/StoneFlow.<Moduł>.Infrastructure/Persistence/Migrations/<migracja>.cs \
  api/src/Modules/<Moduł>/StoneFlow.<Moduł>.Infrastructure/Persistence/Migrations/<migracja>.Designer.cs \
  api/src/Modules/<Moduł>/StoneFlow.<Moduł>.Infrastructure/Persistence/Migrations/<Moduł>DbContextModelSnapshot.cs \
  <pliki konfiguracji encji / właściwości encji, które mapują dodane kolumny>
dotnet build stone-flow.slnx -c Release
# Model EF po revercie musi zgadzać się ze snapshotem w każdym kontekście:
source deploy/scripts/contexts.sh
for entry in "${STONEFLOW_CONTEXTS[@]}"; do
  dotnet ef migrations has-pending-model-changes --no-build --configuration Release \
    --project "${entry%%|*}" --startup-project "$STONEFLOW_STARTUP_PROJECT" --context "${entry##*|}"
done
git commit -m "revert: <opis> (bez migracji <nazwa>)"
```

- Warunek konieczny: brak oczekujących zmian modelu w każdym kontekście. W przeciwnym razie
  `dotnet ef database update` w `migrate.sh` zatrzyma publikację na ostrzeżeniu o zmianach modelu.
- Zielone `deploy-dry-run` na PR revertu potwierdza, że migracje przechodzą.
- **Fix forward zamiast revertu**, gdy migracja była krokiem „contract” (usunięcie kolumny lub
  tabeli, zmiana typu), gdy warunek modelu się nie spełnia albo gdy kod nie daje się rozdzielić
  od mapowania. W ostateczności odtworzenie kopii (sekcja 1).

## 4. Kolejność przy awarii obu części

1. Frontend w panelu Vercel (sekcja 2) — najszybszy.
2. Revert na `main` (sekcja 3) — publikuje backend i frontend.
3. Przerwana wysyłka FTPS (na serwerze został `app_offline.htm`): `wdrozenia.md` §7.

## 5. Próba procedury **[właściciel]** (kryterium SF-21)

Przed startem produkcyjnym, na produkcji bez prawdziwych danych:

1. Publikacja nieszkodliwej zmiany (np. tekst w `deploy/app_offline.htm` albo komentarz) przez PR.
2. Revert tej zmiany przez PR (sekcja 3) i publikacja.
3. Kontrola `/health`, `/logowanie`, `/api/auth/csrf`.
4. Jedno **Promote** poprzedniego wdrożenia w panelu Vercel (sekcja 2) i powrót do bieżącego.
5. Dowód w `odbior-mvp.md`: linki do przebiegów workflow, identyfikatory wdrożeń Vercel, czas trwania.
