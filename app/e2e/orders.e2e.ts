// E-2 (spec F pkt 27.2): zamówienie ręczne z wpłatą; podwójne kliknięcie „Zapisz” tworzy jedno
// zamówienie (design guide §10); stan płatności i lista „Operacje” pokazują wpłatę.
import { expect, test } from './support/fixtures';
import { addPayment, fillNewOrder, openedOrder, rowWith } from './support/ui';

test('zamówienie ręczne z wpłatą, podwójne kliknięcie zapisu tworzy jedno zamówienie', async ({
  page,
  tag,
  today,
}) => {
  await fillNewOrder(page, { customer: `Klient ${tag}`, date: today, value: '123,45' });
  await page.getByRole('button', { name: 'Zapisz', exact: true }).dblclick();
  const order = await openedOrder(page);

  const list = await page.request.get(`/api/orders?search=${encodeURIComponent(tag)}`);
  expect(list.ok()).toBe(true);
  expect((await list.json()).total).toBe(1);

  await addPayment(page, '100,00', today);
  await expect(page.getByText('Częściowo opłacone')).toBeVisible();

  await page.goto(`/operacje?od=${today}&do=${today}&rodzaj=payment`);
  const row = rowWith(page, order.number);
  await expect(row).toHaveCount(1);
  await expect(row).toContainText(/100,00\s*zł/);
  await expect(row).toContainText('Wpływ');
});
