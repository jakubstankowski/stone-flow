// E-3 (spec F pkt 27.3): wydatek jednorazowy jest operacją pieniężną dopiero po opłaceniu.
// E-6 (spec F pkt 28): wydatek cykliczny — wystąpienie bieżącego okresu powstaje przy zapisie
// definicji (dzień terminu 1, więc zawsze ≤ dzisiaj).
import { currentMonthFirst, plDate } from './support/dates';
import { expect, test } from './support/fixtures';
import { createExpense, field, rowWith } from './support/ui';

test('wydatek jednorazowy trafia do „Operacji” dopiero po opłaceniu', async ({
  page,
  tag,
  today,
}) => {
  const description = `Wydatek ${tag}`;
  const id = await createExpense(page, { description, amount: '45,00', costDate: today });

  const operations = `/operacje?od=${today}&do=${today}&rodzaj=expense`;
  await page.goto(operations);
  await expect(page.getByRole('heading', { level: 1, name: 'Operacje' })).toBeVisible();
  await expect(page.locator('app-loading-state')).toHaveCount(0);
  await expect(rowWith(page, description)).toHaveCount(0);

  await page.goto(`/wydatki/${id}`);
  await page.getByRole('button', { name: 'Oznacz jako opłacony' }).click();
  await field(page, 'Data zapłaty').fill(today);
  await page.getByRole('button', { name: 'Oznacz jako opłacony' }).click();
  await expect(page.getByRole('button', { name: 'Cofnij oznaczenie zapłaty' })).toBeVisible();

  await page.goto('/wydatki');
  await field(page, 'Szukaj').fill(tag);
  await expect(rowWith(page, description)).toHaveCount(1);

  await page.goto(operations);
  const row = rowWith(page, description);
  await expect(row).toHaveCount(1);
  await expect(row).toContainText(/45,00\s*zł/);
  await expect(row).toContainText('Wypływ');
});

test('wydatek cykliczny tworzy wystąpienie bieżącego okresu przy zapisie', async ({
  page,
  tag,
  today,
}) => {
  const description = `Cykliczny ${tag}`;
  const first = currentMonthFirst(today);

  await page.goto('/wydatki/cykliczne');
  await page.getByRole('button', { name: 'Dodaj wydatek cykliczny' }).first().click();
  await expect(page.getByLabel('Powtarzaj co miesiąc')).toBeChecked();
  await field(page, 'Dzień miesiąca').fill('1');
  await field(page, 'Początek').fill(first);
  await field(page, 'Opis').fill(description);
  await field(page, 'Kategoria').selectOption({ label: 'Marketing' });
  await field(page, 'Kwota brutto (zł)').fill('30,00');
  await page.getByRole('button', { name: 'Zapisz', exact: true }).click();
  await expect(page).toHaveURL(/\/wydatki\/cykliczne\/[0-9a-f-]{36}$/);

  await page.goto('/wydatki');
  await field(page, 'Szukaj').fill(tag);
  const row = rowWith(page, description);
  await expect(row).toHaveCount(1);
  await expect(row).toContainText(plDate(first));
});
