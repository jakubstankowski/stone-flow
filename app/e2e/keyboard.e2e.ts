// E-10 (spec F pkt 30): obsługa klawiaturą — menu główne z „Przejdź do treści” i widocznym fokusem,
// formularz wydatku zapisany wyłącznie klawiaturą (z błędem walidacji powiązanym z polem) oraz
// dialog potwierdzenia (fokus w dialogu, Escape zamyka, fokus wraca do przycisku).
import { Page } from '@playwright/test';
import { expect, test } from './support/fixtures';

const NAV = ['Pulpit', 'Zamówienia', 'Wydatki', 'Operacje', 'Historia zmian', 'Integracja'];

interface Focused {
  readonly id: string;
  readonly text: string;
  readonly inNav: boolean;
  readonly inMain: boolean;
  readonly inDialog: boolean;
  readonly visibleFocus: boolean;
}

function focused(page: Page): Promise<Focused> {
  return page.evaluate(() => {
    const element = document.activeElement as HTMLElement | null;
    const style = element ? getComputedStyle(element) : null;
    const outline = !!style && style.outlineStyle !== 'none' && parseFloat(style.outlineWidth) > 0;
    const shadow = !!style && style.boxShadow !== 'none';
    return {
      id: element?.id ?? '',
      text: (element?.innerText ?? '').trim(),
      inNav: !!element?.closest('nav'),
      inMain: !!element?.closest('main'),
      inDialog: !!element?.closest('dialog[open]'),
      visibleFocus: outline || shadow,
    };
  });
}

/** Tab, aż fokus spełni warunek (najwyżej `limit` naciśnięć). */
async function tabTo(page: Page, until: (f: Focused) => boolean, limit = 60): Promise<Focused> {
  for (let i = 0; i < limit; i++) {
    await page.keyboard.press('Tab');
    const current = await focused(page);
    if (until(current)) {
      return current;
    }
  }
  throw new Error('Nie udało się dojść klawiszem Tab do oczekiwanego elementu.');
}

test('menu główne: „Przejdź do treści”, sześć pozycji z widocznym fokusem, Enter otwiera sekcję', async ({
  page,
}) => {
  await page.goto('/pulpit');
  await expect(page.getByRole('heading', { level: 1, name: 'Pulpit' })).toBeVisible();

  await page.keyboard.press('Tab');
  expect((await focused(page)).text).toBe('Przejdź do treści');
  await page.keyboard.press('Enter');
  expect((await focused(page)).inMain).toBe(true);

  await page.reload();
  await expect(page.getByRole('heading', { level: 1, name: 'Pulpit' })).toBeVisible();
  const reached: string[] = [];
  for (const label of NAV) {
    const item = await tabTo(page, (f) => f.inNav && f.text === label, 10);
    expect(item.visibleFocus, `widoczny fokus: ${label}`).toBe(true);
    reached.push(item.text);
  }
  expect(reached).toEqual(NAV);

  await page.keyboard.down('Shift');
  for (let i = 0; i < NAV.length - NAV.indexOf('Wydatki') - 1; i++) {
    await page.keyboard.press('Tab');
  }
  await page.keyboard.up('Shift');
  expect((await focused(page)).text).toBe('Wydatki');
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/wydatki$/);
});

test('formularz wydatku tylko klawiaturą, z błędem walidacji i dialogiem usunięcia', async ({
  page,
  tag,
  today,
}) => {
  const description = `Klawiatura ${tag}`;
  await page.goto('/wydatki/nowy');
  await expect(page.getByRole('heading', { level: 1, name: 'Dodaj wydatek' })).toBeVisible();

  await tabTo(page, (f) => f.id === 'wydatek-opis');
  await page.keyboard.type(description);
  await tabTo(page, (f) => f.id === 'wydatek-kategoria', 3);
  // Wybór opcji z klawiatury: pierwsza litera etykiety („Marketing”).
  await page.keyboard.type('M');
  await expect(page.locator('#wydatek-kategoria')).not.toHaveValue('');

  // Zapis z pustą kwotą: błąd wskazuje pole i jest z nim powiązany, wpisane dane zostają.
  await tabTo(page, (f) => f.id === 'wydatek-kontrahent', 3);
  await page.keyboard.press('Enter');
  const amount = page.locator('#wydatek-brutto');
  await expect(amount).toHaveAttribute('aria-invalid', 'true');
  const describedBy = (await amount.getAttribute('aria-describedby')) ?? '';
  const messages = await Promise.all(
    describedBy
      .split(/\s+/)
      .filter(Boolean)
      .map((id) => page.locator(`[id="${id}"]`).innerText()),
  );
  expect(messages.join(' ')).toMatch(/\S/);
  await expect(page.locator('#wydatek-opis')).toHaveValue(description);

  await amount.focus();
  await page.keyboard.type('12,00');
  await tabTo(page, (f) => f.id === 'wydatek-data-kosztu', 10);
  const [year, month, day] = today.split('-');
  await page.keyboard.type(`${day}${month}${year}`);
  await expect(page.locator('#wydatek-data-kosztu')).toHaveValue(today);
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/wydatki\/[0-9a-f-]{36}$/);
  await expect(page.getByRole('heading', { level: 1, name: description })).toBeVisible();

  // Dialog: fokus w środku, Escape zamyka, fokus wraca do „Usuń”, rekord zostaje.
  await tabTo(page, (f) => f.text === 'Usuń' && f.inMain);
  await page.keyboard.press('Enter');
  const dialog = page.getByRole('dialog', { name: 'Usunąć wydatek?' });
  await expect(dialog).toBeVisible();
  expect((await focused(page)).inDialog).toBe(true);
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  expect((await focused(page)).text).toBe('Usuń');
  await expect(page.getByRole('heading', { level: 1, name: description })).toBeVisible();
});
