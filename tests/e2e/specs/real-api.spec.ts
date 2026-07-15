import { expect, test } from '@playwright/test';
import { answerCriticalQuestions } from './helpers';

test('uses authoritative REST state and a tokenless bystander projection in separate contexts', async ({ page, request, browser }) => {
  const health = await request.get('http://127.0.0.1:8080/health/live');
  expect(health.ok()).toBe(true);

  await page.goto('/login');
  await page.getByLabel('Email address').fill('demo@goldenhour.ai');
  await page.getByLabel('Password').fill('GoldenHour-Demo-Only-2026!');
  const loginResponse = page.waitForResponse((response) => response.url().endsWith('/api/v1/auth/login') && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Sign in' }).click();
  expect((await loginResponse).ok()).toBe(true);
  await expect(page).toHaveURL(/\/home$/);

  await page.getByRole('link', { name: 'Start emergency help' }).click();
  await page.getByLabel('Help a family member').check();
  await page.getByLabel('Chest pain').check();
  const createResponse = page.waitForResponse((response) => response.url().endsWith('/api/v1/sessions') && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Continue to describe what happened' }).click();
  const created = await createResponse;
  expect(created.ok()).toBe(true);
  const createdPayload = await created.json() as { id?: string };
  expect(createdPayload.id).toMatch(/^[0-9a-f-]{36}$/i);
  const sessionId = createdPayload.id as string;

  await page.getByRole('button', { name: 'Use Hindi demo' }).click();
  await page.getByLabel('Type location instead').fill('Fictional Jaipur test landmark');
  const incidentResponse = page.waitForResponse((response) => /\/api\/v1\/sessions\/[^/]+\/incident$/.test(new URL(response.url()).pathname) && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Use this description' }).click();
  expect((await incidentResponse).ok()).toBe(true);

  await answerCriticalQuestions(page);
  await expect(page.getByRole('heading', { name: 'Do this now' })).toBeVisible();
  await expect(page.getByText('Demonstration guidance requiring clinical review before production use.')).toBeVisible();
  await expect(page.getByText(/मेरे पिताजी/)).toBeVisible();
  await expect(page.getByText('Demonstration only')).toHaveCount(0);

  // A full reload must rehydrate from durable server state, not the in-memory
  // React session. The deliberately fictional observation remains server-side.
  await page.goto(`/emergency/${sessionId}`);
  await expect(page.getByRole('heading', { name: 'Emergency coordination' })).toBeVisible();
  const serverObservation = 'Fictional E2E observation persisted by the API';
  await page.getByLabel('Report what you can see').fill(serverObservation);
  const observationResponse = page.waitForResponse((response) => /\/api\/v1\/sessions\/[^/]+\/timeline$/.test(new URL(response.url()).pathname) && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Save changes' }).click();
  expect((await observationResponse).ok()).toBe(true);
  await page.reload();
  await expect(page.getByText(serverObservation)).toBeVisible();

  await page.goto(`/emergency/${sessionId}/share`);
  await expect(page.getByRole('heading', { name: 'Bystander QR' })).toBeVisible();
  const shareResponsePromise = page.waitForResponse((response) => /\/api\/v1\/sessions\/[^/]+\/share-tokens$/.test(new URL(response.url()).pathname) && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Generate emergency QR' }).click();
  const shareResponse = await shareResponsePromise;
  expect(shareResponse.ok()).toBe(true);
  const share = await shareResponse.json() as { token: string; path: string };
  expect(share.token.length).toBeGreaterThanOrEqual(20);
  expect(share.path).toBe(`/share#${share.token}`);

  const bystanderContext = await browser.newContext({ baseURL: 'http://127.0.0.1:5174', serviceWorkers: 'block' });
  const bystanderPage = await bystanderContext.newPage();
  try {
    const projectionRequestPromise = bystanderPage.waitForRequest((candidate) => new URL(candidate.url()).pathname === '/api/v1/bystander' && candidate.method() === 'GET');
    await bystanderPage.goto(share.path);
    const projectionRequest = await projectionRequestPromise;
    expect(projectionRequest.url()).not.toContain(share.token);
    expect(projectionRequest.headers()['x-emergency-share-token']).toBe(share.token);
    await expect(bystanderPage).toHaveURL(/\/share$/);
    expect(new URL(bystanderPage.url()).hash).toBe('');
    await expect(bystanderPage.getByRole('heading', { name: 'Emergency information' })).toBeVisible();
    await expect(bystanderPage.getByText('Demo policy — hidden from bystanders')).toHaveCount(0);

    const firstObservation = bystanderPage.getByRole('group', { name: 'Is the person conscious?' });
    await firstObservation.getByRole('button', { name: 'Yes' }).click();
    const bystanderPostPromise = bystanderPage.waitForRequest((candidate) => new URL(candidate.url()).pathname === '/api/v1/bystander/observations' && candidate.method() === 'POST');
    await bystanderPage.getByRole('button', { name: 'Save changes' }).click();
    const bystanderPost = await bystanderPostPromise;
    expect(bystanderPost.url()).not.toContain(share.token);
    expect(bystanderPost.headers()['x-emergency-share-token']).toBe(share.token);
    await expect(bystanderPage.getByRole('status').filter({ hasText: 'Done' })).toBeVisible();
  } finally {
    await bystanderContext.close();
  }
});
