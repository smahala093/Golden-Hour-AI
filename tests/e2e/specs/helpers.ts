import { expect, type Page } from '@playwright/test';
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const specDirectory = dirname(fileURLToPath(import.meta.url));
const protocolDirectory = resolve(specDirectory, '..', '..', '..', 'apps', 'api', 'Protocols');

function reviewedProtocolCatalogue(): unknown[] {
  return readdirSync(protocolDirectory)
    .filter((fileName) => fileName.endsWith('.json'))
    .sort()
    .map((fileName) => JSON.parse(readFileSync(resolve(protocolDirectory, fileName), 'utf8')) as unknown);
}

export async function installReviewedProtocolRoute(page: Page): Promise<void> {
  const protocols = reviewedProtocolCatalogue();
  await page.route('**/api/v1/protocols?**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json; charset=utf-8',
      body: JSON.stringify(protocols),
    });
  });
}

export async function answerCriticalQuestions(page: Page): Promise<void> {
  while (/\/emergency\/[^/]+\/questions$/.test(new URL(page.url()).pathname)) {
    const questionCard = page.locator('.question-card');
    const notSure = questionCard.getByRole('button', { name: 'Not sure' });
    await expect(async () => {
      if (await notSure.getAttribute('aria-pressed') !== 'true') await notSure.click();
      await expect(notSure).toHaveAttribute('aria-pressed', 'true');
    }).toPass({ timeout: 10_000 });
    const next = questionCard.getByRole('button', { name: /Next question|Show next action/ });
    await expect(next).toBeEnabled();
    await next.click();
    await page.waitForTimeout(50);
  }
}

export async function preventTelephoneNavigation(page: Page): Promise<void> {
  await page.evaluate(() => {
    document.addEventListener('click', (event) => {
      const target = event.target instanceof Element ? event.target.closest('a[href^="tel:"]') : null;
      if (target) event.preventDefault();
    }, { capture: true, once: true });
  });
}
