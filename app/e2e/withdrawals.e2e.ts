// E-4 (spec F pkt 27.4): wypłata właściciela jest wypływem na liście „Operacje”.
import { expect, test } from './support/fixtures';
import { field, rowWith } from './support/ui';

test('wypłata właściciela jest wypływem w „Operacjach”', async ({ page, tag, today }) => {
  const person = `Właściciel ${tag}`;

  await page.goto('/operacje/wyplaty/nowa');
  await expect(page.getByRole('heading', { level: 1, name: 'Dodaj wypłatę' })).toBeVisible();
  await field(page, 'Osoba').fill(person);
  await field(page, 'Kwota (zł)').fill('12,34');
  await field(page, 'Data wypłaty').fill(today);
  await field(page, 'Metoda').selectOption({ label: 'Przelew' });
  await page.getByRole('button', { name: 'Zapisz', exact: true }).click();
  await expect(page).toHaveURL(/\/operacje\/wyplaty\/[0-9a-f-]{36}$/);

  await page.goto(`/operacje?od=${today}&do=${today}&rodzaj=withdrawal`);
  const row = rowWith(page, person);
  await expect(row).toHaveCount(1);
  await expect(row).toContainText('Wypływ');
  await expect(row).toContainText(/12,34\s*zł/);
});
