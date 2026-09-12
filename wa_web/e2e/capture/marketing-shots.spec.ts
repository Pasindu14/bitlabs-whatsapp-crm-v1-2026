import { test, expect, type Browser, type BrowserContext, type Page } from '@playwright/test'
import path from 'path'
import fs from 'fs'
import { freezePage, scrubPii } from './scrub'

/**
 * Marketing screenshot capture for the 20s vertical ad.
 *
 * READ-ONLY BY CONSTRUCTION. This runs against the live production tenant, so two guards apply:
 *
 *  1. The dev server must be started with CAPTURE_READONLY=1. That makes lib/api/client.ts refuse
 *     every non-GET request to the .NET API — the single choke point every server action goes
 *     through — so no message can be sent, no campaign created or launched, no template submitted
 *     to Meta, even if a click goes somewhere unintended.
 *  2. This spec never clicks Send, Launch, Submit, Save, Delete or Confirm. It navigates, switches
 *     a tab, selects a conversation, and opens one dialog it never submits.
 *
 * Every real name, phone number, email and message body is overwritten with fixed demo data
 * before each shot (see scrub.ts) — the output is a public ad.
 *
 * Run:  CAPTURE_READONLY=1 npm run dev      (terminal 1)
 *       npm run test:capture                (terminal 2)
 */

const SHOTS_DIR = path.resolve(__dirname, '../../../ad/assets/shots')

test.describe.configure({ mode: 'serial' })

let context: BrowserContext
let page: Page

/** Requests the browser made that were NOT plain navigations — logged for after-the-fact audit. */
const postLog: string[] = []

async function login(browser: Browser) {
  const email = process.env.MARKETING_EMAIL
  const password = process.env.MARKETING_PASSWORD
  if (!email || !password) {
    throw new Error('Missing MARKETING_EMAIL or MARKETING_PASSWORD in .env.local')
  }

  context = await browser.newContext({
    viewport: { width: 1440, height: 900 },
    deviceScaleFactor: 2,
    colorScheme: 'light',
    reducedMotion: 'reduce',
  })
  page = await context.newPage()

  page.on('request', (req) => {
    if (req.method() !== 'GET' && req.method() !== 'HEAD') {
      postLog.push(`${req.method()} ${req.url()}`)
    }
  })

  await page.goto('/sign-in')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password').fill(password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.waitForURL('**/dashboard', { timeout: 30_000 })
}

/** Navigate, wait for the page to actually be populated, then freeze and scrub it. */
async function prepare(url: string, ready: () => Promise<unknown>) {
  await page.goto(url)
  await ready()
  await page.waitForLoadState('networkidle')
  await freezePage(page)
  await scrubPii(page)
  // One paint after the DOM rewrite.
  await page.evaluate(() => new Promise((r) => requestAnimationFrame(() => r(null))))
}

async function shoot(name: string, clip?: { x: number; y: number; width: number; height: number }) {
  fs.mkdirSync(SHOTS_DIR, { recursive: true })
  await page.screenshot({ path: path.join(SHOTS_DIR, `${name}.png`), clip })
}

test.beforeAll(async ({ browser }) => {
  await login(browser)
})

test.afterAll(async () => {
  fs.mkdirSync(SHOTS_DIR, { recursive: true })
  fs.writeFileSync(
    path.join(SHOTS_DIR, '_non-get-requests.log'),
    postLog.length ? postLog.join('\n') : '(none)\n',
    'utf8'
  )
  await context?.close()
})

test('dashboard', async () => {
  await prepare('/dashboard', () =>
    expect(page.getByRole('heading', { level: 1 })).toBeVisible({ timeout: 30_000 })
  )
  await shoot('dashboard')
})

test('campaigns list', async () => {
  await prepare('/campaigns', () =>
    expect(page.getByRole('button', { name: 'New Campaign' })).toBeVisible({ timeout: 30_000 })
  )
  await shoot('campaigns')
})

test('campaign builder dialog (opened, never submitted)', async () => {
  await page.goto('/campaigns')
  await page.getByRole('button', { name: 'New Campaign' }).click()
  await expect(page.getByRole('heading', { name: 'Create Campaign' })).toBeVisible({
    timeout: 30_000,
  })
  // Let the template + contact-list selects populate.
  await page.waitForLoadState('networkidle')
  await freezePage(page)
  await scrubPii(page)
  await page.evaluate(() => new Promise((r) => requestAnimationFrame(() => r(null))))
  await shoot('campaign-builder')
  await page.keyboard.press('Escape')
})

test('inbox', async () => {
  await prepare('/inbox', () =>
    expect(page.getByRole('heading', { name: 'Inbox' })).toBeVisible({ timeout: 30_000 })
  )

  // Select a conversation that has no unread badge, so the read-receipt mutation never fires.
  // (CAPTURE_READONLY would block it anyway; this keeps the run clean rather than relying on that.)
  const rows = page.locator('ul.divide-y > li > button')
  const count = await rows.count()
  let picked = false
  for (let i = 0; i < count; i++) {
    const row = rows.nth(i)
    const hasBadge = await row.locator('span.rounded-full.bg-primary').count()
    if (hasBadge === 0) {
      await row.click()
      picked = true
      break
    }
  }
  if (!picked && count > 0) {
    test.info().annotations.push({
      type: 'warning',
      description: 'Every conversation was unread; skipped selection to avoid a read-receipt write.',
    })
  }

  if (picked) {
    await expect(page.locator('p.whitespace-pre-wrap').first()).toBeVisible({ timeout: 30_000 })
    await page.waitForLoadState('networkidle')
    await freezePage(page)
    await scrubPii(page)
    await page.evaluate(() => new Promise((r) => requestAnimationFrame(() => r(null))))
  }

  await shoot('inbox')
})

test('analytics', async () => {
  await page.goto('/analytics')
  await expect(page.getByRole('heading', { level: 1, name: 'Analytics' })).toBeVisible({
    timeout: 30_000,
  })

  // The default range is the last 30 days, which may be empty on this tenant. Widen it until the
  // chart has real bars. Both controls are plain <input type="date">, so this is a pure UI change.
  const from = page.locator('input[type="date"]').first()
  for (const start of ['2026-06-01', '2026-01-01', '2025-01-01']) {
    if ((await page.locator('.recharts-bar').count()) > 0) break
    await from.fill(start)
    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(1200)
  }

  const bars = await page.locator('.recharts-bar').count()
  test.info().annotations.push({
    type: bars > 0 ? 'note' : 'warning',
    description:
      bars > 0
        ? `Chart rendered with real data (${bars} bar series).`
        : 'No real message data in any range — analytics shot will need demo data.',
  })

  await page.waitForLoadState('networkidle')
  await freezePage(page)
  await scrubPii(page)
  await page.evaluate(() => new Promise((r) => requestAnimationFrame(() => r(null))))
  await shoot('analytics')
})

test('templates list', async () => {
  await prepare('/templates', () =>
    expect(page.getByRole('heading', { level: 1, name: /templates/i })).toBeVisible({
      timeout: 30_000,
    })
  )
  await shoot('templates')
})

test('subscription quota', async () => {
  await prepare('/my-subscription', () =>
    expect(page.getByRole('heading', { level: 1 })).toBeVisible({ timeout: 30_000 })
  )
  await shoot('subscription')
})
