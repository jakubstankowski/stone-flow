// E-8 (spec F pkt 27.7): odświeżenie podstrony SPA zostawia użytkownika na tej podstronie z danymi.
import { expect, test } from './support/fixtures';
import { createOrder } from './support/ui';

test('odświeżenie szczegółów zamówienia i listy wydatków cyklicznych', async ({
  page,
  tag,
  today,
}) => {
  const customer = `Klient ${tag}`;
  const order = await createOrder(page, { customer, date: today, value: '10,00' });

  await page.reload();
  await expect(page).toHaveURL(new RegExp(`/zamowienia/${order.id}$`));
  await expect(
    page.getByRole('heading', { level: 1, name: `Zamówienie ${order.number}` }),
  ).toBeVisible();
  await expect(page.getByText(customer)).toBeVisible();

  await page.goto('/wydatki/cykliczne');
  await page.reload();
  await expect(page).toHaveURL(/\/wydatki\/cykliczne$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Wydatki cykliczne' })).toBeVisible();
});
