// Kontrole dostępności E2E: axe-core z tagami WCAG 2.2 AA i reflow 375 px (design guide §9).
import AxeBuilder from '@axe-core/playwright';
import { expect, Page } from '@playwright/test';

export const WCAG_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

/** Zero naruszeń; komunikat wymienia regułę i selektory elementów. */
export async function expectNoAxeViolations(page: Page): Promise<void> {
  const { violations } = await new AxeBuilder({ page }).withTags(WCAG_TAGS).analyze();
  const report = violations.map(
    (violation) =>
      `${violation.id} (${violation.impact}): ${violation.help}\n` +
      violation.nodes.map((node) => `    ${node.target.join(' ')}`).join('\n'),
  );
  expect(report, report.join('\n')).toEqual([]);
}

/** Szerokość 375 px bez poziomego przewijania całej strony. */
export async function expectNoHorizontalScroll(page: Page): Promise<void> {
  await page.setViewportSize({ width: 375, height: 800 });
  await expect
    .poll(() =>
      page.evaluate(
        () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
      ),
    )
    .toBe(true);
}
