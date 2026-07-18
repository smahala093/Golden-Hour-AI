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
  await page.getByText('Use my saved profile for this patient', { exact: true }).click();
  await expect(page.getByLabel('Use my saved profile for this patient')).toBeChecked();
  await page.getByLabel('Chest pain').check();
  await page.getByRole('button', { name: 'Continue to describe what happened' }).click();

  await page.getByRole('button', { name: 'Use Hindi demo' }).click();
  await expect(page.getByLabel('Type the situation')).toHaveValue(/मेरे पिताजी/);
  await page.getByLabel('Type location instead').fill('Demo landmark, Jaipur');
  await page.evaluate(() => {
    Object.defineProperty(navigator, 'geolocation', { configurable: true, value: { getCurrentPosition: (_success: PositionCallback, failure: PositionErrorCallback) => failure({ code: 1, message: 'denied', PERMISSION_DENIED: 1, POSITION_UNAVAILABLE: 2, TIMEOUT: 3 } as GeolocationPositionError) } });
  });
  await page.getByRole('button', { name: 'Use my location' }).click();
  await page.getByLabel('Latitude (−90 to 90)').fill('26.9124');
  await page.getByLabel('Longitude (−180 to 180)').fill('75.7873');
  await page.getByLabel('I consent to share these coordinates for this active emergency.').check();
  await page.getByRole('button', { name: 'Use this map pin' }).click();
  await expect(page.getByText(/Map pin is ready/)).toBeVisible();
  await page.getByRole('button', { name: 'Use this description' }).click();

  await expect(page.getByRole('heading', { name: 'Review the report before confirming' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Interpreted facts — not yet confirmed' })).toBeVisible();
  await expect(page.getByText('Sudden chest pain', { exact: true })).toBeVisible();
  await answerCriticalQuestions(page);
  await expect(page.getByRole('heading', { name: 'Do this now' })).toBeVisible();
  await expect(page.getByText('Demonstration guidance requiring clinical review before production use.')).toBeVisible();
  await page.getByRole('button', { name: 'Read approved guidance aloud' }).click();
  await expect(page.getByText('Approved protocol audio is unavailable. Continue using the text guidance.')).toBeVisible();
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

  await page.goto(`/emergency/${new URL(page.url()).pathname.split('/')[2]}/family-summary`);
  await page.getByRole('button', { name: 'Generate server summary' }).click();
  await expect(page.locator('.summary-content')).toContainText('Summary type: family');
  await expect(page.locator('.summary-content')).toContainText('Demonstration guidance requiring clinical review before production use.');
});
