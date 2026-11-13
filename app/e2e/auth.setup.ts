// Projekt `setup`: jedno logowanie w UI na przebieg i zapis stanu sesji dla projektu `chromium`
// (limit logowania 10/min). Ślad (trace) jest wyłączony, a hasło wpisuje `fillSecret`,
// żeby nie trafiło do raportu.
import { expect, test as setup } from '@playwright/test';
import { OWNER_EMAIL, STORAGE_STATE, ownerPassword } from './support/env';
import { fillSecret } from './support/ui';

setup('logowanie właściciela i zapis sesji', async ({ page }) => {
  await page.goto('/logowanie');
  await page.getByLabel('E-mail').fill(OWNER_EMAIL);
  await fillSecret(page.getByLabel('Hasło'), ownerPassword());
  await page.getByRole('button', { name: 'Zaloguj' }).click();
  await expect(page).toHaveURL(/\/pulpit$/);
  await page.context().storageState({ path: STORAGE_STATE });
});
