// E-9 (spec F pkt 29, 31): strony logowania i resetu hasła bez sesji (guestGuard przekierowałby
// zalogowanego użytkownika). Formularz nowego hasła z fikcyjnym fragmentem — strona sprawdza tylko
// obecność obu wartości i nie wysyła żądania przed zapisem.
import { expect, test } from '@playwright/test';
import { expectNoAxeViolations, expectNoHorizontalScroll } from './support/a11y';

test.use({ storageState: { cookies: [], origins: [] } });

const PAGES = [
  { name: 'Logowanie', path: '/logowanie', heading: 'Logowanie' },
  { name: 'Reset hasła', path: '/reset-hasla', heading: 'Reset hasła' },
  {
    name: 'Ustaw hasło',
    path: '/reset-hasla/nowe#konto=00000000-0000-0000-0000-000000000000&token=e2e',
    heading: 'Ustaw hasło',
  },
];

for (const { name, path, heading } of PAGES) {
  test(`dostępność i reflow: ${name}`, async ({ page }) => {
    await page.goto(path);
    await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible();
    await expect(page.locator('form')).toBeVisible();
    await expectNoAxeViolations(page);
    await expectNoHorizontalScroll(page);
    await expectNoAxeViolations(page);
  });
}
