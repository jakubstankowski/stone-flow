// E-1 (spec F pkt 27.1), projekt `session`: logowanie, komunikat przy złym haśle i wygaśnięcie
// sesji bez ujawnienia danych. Musi biec ostatni: „Wyloguj” kończy wszystkie sesje konta, także
// tę zapisaną przez projekt `setup`. Uruchomienie samego testu:
// npx playwright test --project=session --no-deps
import { expect, test } from '@playwright/test';
import { OWNER_EMAIL, ownerPassword } from './support/env';

test('złe hasło, logowanie i wygaśnięcie sesji po wylogowaniu z innego urządzenia', async ({
  browser,
}) => {
  const first = await browser.newContext();
  const a = await first.newPage();
  await a.goto('/logowanie');
  await a.getByLabel('E-mail').fill(OWNER_EMAIL);
  await a.getByLabel('Hasło').fill(`${ownerPassword()}-zle`);
  await a.getByRole('button', { name: 'Zaloguj' }).click();
  // Komunikat ogólny: nie wskazuje, czy błędny jest e-mail, czy hasło.
  await expect(
    a.getByRole('alert').filter({ hasText: 'Nieprawidłowy e-mail lub hasło.' }),
  ).toBeVisible();
  await expect(a).toHaveURL(/\/logowanie/);

  await a.getByLabel('Hasło').fill(ownerPassword());
  await a.getByRole('button', { name: 'Zaloguj' }).click();
  await expect(a).toHaveURL(/\/pulpit$/);
  await expect(a.locator('[data-kpi]').first()).toBeVisible();

  const second = await browser.newContext();
  const b = await second.newPage();
  await b.goto('/logowanie');
  await b.getByLabel('E-mail').fill(OWNER_EMAIL);
  await b.getByLabel('Hasło').fill(ownerPassword());
  await b.getByRole('button', { name: 'Zaloguj' }).click();
  await expect(b).toHaveURL(/\/pulpit$/);
  await b.getByRole('button', { name: 'Wyloguj' }).click();
  await expect(b).toHaveURL(/\/logowanie/);

  await a
    .getByRole('navigation', { name: 'Główna' })
    .getByRole('link', { name: 'Zamówienia' })
    .click();
  await expect(a).toHaveURL(/\/logowanie/);
  await expect(a.getByText(/Sesja wygasła/)).toBeVisible();
  await expect(a.getByText(/zł/)).toHaveCount(0);

  await first.close();
  await second.close();
});
