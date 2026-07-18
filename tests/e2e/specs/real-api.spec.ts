import { expect, test } from '@playwright/test';
import { answerCriticalQuestions } from './helpers';

test('uses authoritative REST state and a tokenless bystander projection in separate contexts', async ({ page, request, browser }) => {
  const demoPassword = process.env.E2E_DEMO_PASSWORD
    ?? process.env.Seed__DemoPassword
    ?? 'GoldenHour-Demo-Only-2026!';
  const health = await request.get('http://127.0.0.1:8080/health/live');
  expect(health.ok()).toBe(true);

  await page.goto('/login');
  await page.getByLabel('Email address').fill('demo@goldenhour.ai');
  await page.getByLabel('Password').fill(demoPassword);
  const loginResponse = page.waitForResponse((response) => response.url().endsWith('/api/v1/auth/login') && response.request().method() === 'POST', { timeout: 30_000 });
  await page.getByRole('button', { name: 'Sign in' }).click();
  expect((await loginResponse).ok()).toBe(true);
  await expect(page).toHaveURL(/\/home$/);

  await page.getByRole('link', { name: 'Start emergency help' }).click();
  await page.getByLabel('Help a family member').check();
  await page.getByText('Use my saved profile for this patient', { exact: true }).click();
  await expect(page.getByLabel('Use my saved profile for this patient')).toBeChecked();
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
  await page.evaluate(() => {
    Object.defineProperty(navigator, 'geolocation', { configurable: true, value: { getCurrentPosition: (_success: PositionCallback, failure: PositionErrorCallback) => failure({ code: 1, message: 'denied', PERMISSION_DENIED: 1, POSITION_UNAVAILABLE: 2, TIMEOUT: 3 } as GeolocationPositionError) } });
  });
  await page.getByRole('button', { name: 'Use my location' }).click();
  await page.getByLabel('Latitude (−90 to 90)').fill('26.9124');
  await page.getByLabel('Longitude (−180 to 180)').fill('75.7873');
  await page.getByLabel('I consent to share these coordinates for this active emergency.').check();
  await page.getByRole('button', { name: 'Use this map pin' }).click();
  await expect(page.getByText(/Map pin is ready/)).toBeVisible();
  const incidentResponse = page.waitForResponse((response) => /\/api\/v1\/sessions\/[^/]+\/incident$/.test(new URL(response.url()).pathname) && response.request().method() === 'POST');
  const locationResponse = page.waitForResponse((response) => /\/api\/v1\/sessions\/[^/]+\/locations$/.test(new URL(response.url()).pathname) && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Use this description' }).click();
  expect((await incidentResponse).ok()).toBe(true);
  const savedLocation = await locationResponse;
  expect(savedLocation.ok()).toBe(true);
  expect(savedLocation.request().postDataJSON()).toEqual(expect.objectContaining({ latitude: 26.9124, longitude: 75.7873, description: 'Fictional Jaipur test landmark', consentProvided: true }));

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

  const journeyResponse = page.waitForResponse((response) => /\/api\/v1\/sessions\/[^/]+\/timeline$/.test(new URL(response.url()).pathname) && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Patient departed' }).click();
  expect((await journeyResponse).ok()).toBe(true);
  const journeyEvent = page.locator('.timeline li').filter({ hasText: 'The session owner reported that the patient departed.' }).last();
  await expect(journeyEvent.getByText('user reported')).toBeVisible();

  for (const summary of [
    { label: 'Family summary', path: 'family-summary', kind: 'family' },
    { label: 'Responder brief', path: 'responder', kind: 'responder' },
    { label: 'Hospital handover', path: 'handover', kind: 'hospital-handover' },
  ]) {
    await page.getByRole('link', { name: summary.label, exact: true }).click();
    await expect(page).toHaveURL(new RegExp(`/emergency/${sessionId}/${summary.path}$`));
    const summaryResponsePromise = page.waitForResponse((response) => new URL(response.url()).pathname.endsWith(`/summaries/${summary.kind}`) && response.request().method() === 'POST', { timeout: 30_000 });
    await page.getByRole('button', { name: 'Generate server summary' }).click();
    const summaryResponse = await summaryResponsePromise;
    expect(summaryResponse.ok()).toBe(true);
    const payload = await summaryResponse.json() as { content: string };
    await expect(page.locator('.summary-content')).toContainText(payload.content);
    await expect(page.locator('.summary-content')).toContainText('Demonstration guidance requiring clinical review before production use.');
  }

  await page.goto(`/emergency/${sessionId}/share`);
  await expect(page.getByRole('heading', { name: 'Bystander QR' })).toBeVisible();
  const shareResponsePromise = page.waitForResponse((response) => /\/api\/v1\/sessions\/[^/]+\/share-tokens$/.test(new URL(response.url()).pathname) && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Generate emergency QR' }).click();
  const shareResponse = await shareResponsePromise;
  expect(shareResponse.ok()).toBe(true);
  const share = await shareResponse.json() as { token: string; path: string };
  expect(share.token.length).toBeGreaterThanOrEqual(20);
  expect(share.path).toBe(`/share#${share.token}`);

  // Keep an authenticated owner view subscribed to the session while the
  // separate anonymous context reports an observation. The five-second bound
  // is deliberately shorter than the eight-second polling fallback, proving
  // that the update arrived through the live connection.
  await page.getByRole('link', { name: 'Live situation' }).click();
  await expect(page).toHaveURL(new RegExp(`/emergency/${sessionId}$`));
  await expect(page.getByRole('heading', { name: 'Emergency coordination' })).toBeVisible();
  await expect(page.getByRole('status').filter({ hasText: 'Live updates connected' })).toBeVisible({ timeout: 10_000 });

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
    await expect(page.getByText('A bystander reported visible observations; facts remain unconfirmed.')).toBeVisible({ timeout: 5_000 });
  } finally {
    await bystanderContext.close();
  }
});
