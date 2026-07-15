import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { answerCriticalQuestions, installReviewedProtocolRoute, preventTelephoneNavigation } from './helpers';

test.beforeEach(async ({ page }) => {
  await installReviewedProtocolRoute(page);
});

test('completes the deterministic Hindi emergency demo without fabricating call success', async ({ page }, testInfo) => {
  await page.goto('/');
  await expect(page.getByText('Demonstration only')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Call 112' })).toHaveAttribute('href', 'tel:112');
  await page.locator('.public-frame').screenshot({ path: testInfo.outputPath('00-sanitized-landing.png') });

  await page.getByRole('button', { name: 'Open demo' }).click();
  await expect(page).toHaveURL(/\/home$/);
  await expect(page.getByRole('heading', { name: /Good evening/ })).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath('01-home.png'), fullPage: true });

  const homeAudit = await new AxeBuilder({ page }).analyze();
  expect(homeAudit.violations, homeAudit.violations.map((violation) => `${violation.id}: ${violation.help}`).join('\n')).toEqual([]);

  await page.getByRole('link', { name: 'Start emergency help' }).click();
  await page.getByLabel('Help a family member').check();
  await page.getByLabel('Chest pain').check();
  await page.getByRole('button', { name: 'Continue to describe what happened' }).click();

  await page.getByRole('button', { name: 'Use Hindi demo' }).click();
  await expect(page.getByLabel('Type the situation')).toHaveValue(/मेरे पिताजी/);
  await page.getByLabel('Type location instead').fill('Demo landmark, Jaipur');
  await page.getByRole('button', { name: 'Use this description' }).click();

  await answerCriticalQuestions(page);
  await expect(page.getByRole('heading', { name: 'Do this now' })).toBeVisible();
  await expect(page.getByText('Demonstration guidance requiring clinical review before production use.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'I confirm the call connected' })).toHaveCount(0);
  await page.locator('.action-focus').screenshot({ path: testInfo.outputPath('02-sanitized-reviewed-action.png') });

  const actionCard = page.locator('.action-focus');
  const callAction = actionCard.getByRole('link', { name: 'Call 112' });
  await expect(callAction).toHaveAttribute('href', 'tel:112');
  await preventTelephoneNavigation(page);
  await callAction.click();
  await expect(actionCard.getByRole('button', { name: 'I confirm the call connected' })).toBeVisible();
  await actionCard.getByRole('button', { name: 'I confirm the call connected' }).click();

  for (let index = 0; index < 4; index += 1) {
    const next = actionCard.getByRole('button', { name: 'Next action' });
    if (!await next.isVisible().catch(() => false)) break;
    await next.click();
  }
  await actionCard.getByRole('button', { name: 'Open coordination room' }).click();

  await expect(page.getByRole('heading', { name: 'Emergency coordination' })).toBeVisible();
  await expect(page.getByText('Original Hindi description preserved.')).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath('03-coordination-room.png'), fullPage: true });
});
