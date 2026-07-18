import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { installReviewedProtocolRoute } from './helpers';

test.beforeEach(async ({ page }) => {
  await installReviewedProtocolRoute(page);
  await page.addInitScript(() => {
    window.localStorage.setItem('gh-preferences-v1', JSON.stringify({ language: 'ur', reducedMotion: true }));
    window.localStorage.setItem('gh-language', 'ur');
  });
});

test('supports mobile keyboard, RTL, installability, and privacy-bounded offline use', async ({ page, context }) => {
  await page.goto('/', { waitUntil: 'domcontentloaded' });
  await expect(page.locator('html')).toHaveAttribute('lang', 'ur');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

  const manifestResponse = await page.request.get('/manifest.webmanifest');
  expect(manifestResponse.ok()).toBe(true);
  await expect.poll(async () => {
    return page.evaluate(async () => {
      if (!('serviceWorker' in navigator)) return false;
      const registration = await navigator.serviceWorker.ready;
      return Boolean(registration.active);
    });
  }, { timeout: 15_000 }).toBe(true);

  await page.getByRole('button', { name: 'Open demo' }).click();
  await expect(page).toHaveURL(/\/home$/);

  const menu = page.locator('button[aria-controls="primary-navigation"]');
  await expect(menu).toHaveAccessibleName('Open menu');
  await menu.focus();
  await page.keyboard.press('Enter');
  await expect(menu).toHaveAttribute('aria-expanded', 'true');
  await expect(menu).toHaveAccessibleName('Close menu');
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: 'Home', exact: true })).toBeFocused();

  const audit = await new AxeBuilder({ page }).analyze();
  expect(audit.violations, audit.violations.map((violation) => `${violation.id}: ${violation.help}`).join('\n')).toEqual([]);

  await page.getByRole('link', { name: 'Resume coordination' }).click();
  await expect(page.getByRole('heading', { name: 'Emergency coordination' })).toBeVisible();

  await context.setOffline(true);
  await page.evaluate(() => window.dispatchEvent(new Event('offline')));
  await expect(page.getByRole('status').filter({ hasText: 'Offline' }).first()).toBeVisible();

  const fictionalObservation = 'Fictional demo observation that must not be retained offline';
  await page.getByLabel('Report what you can see').fill(fictionalObservation);
  await page.getByRole('button', { name: 'Save changes' }).click();
  await expect(page.getByText('A noncritical status check was saved on this device and is waiting to sync.')).toBeVisible();

  const storedQueue = await page.evaluate(() => window.localStorage.getItem('gh-noncritical-queue-v1') ?? '');
  expect(storedQueue).toContain('noncritical-check-recorded');
  expect(storedQueue).toContain('A noncritical status check was recorded while offline.');
  expect(storedQueue).not.toContain(fictionalObservation);

  await page.evaluate(() => {
    window.history.pushState({}, '', '/offline');
    window.dispatchEvent(new PopStateEvent('popstate'));
  });
  await expect(page.getByRole('heading', { name: 'You are offline' })).toBeVisible();
  await expect(page.locator('a[href="tel:112"]')).toBeVisible();
});
