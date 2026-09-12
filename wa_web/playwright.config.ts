import { defineConfig, devices } from '@playwright/test'
import * as dotenv from 'dotenv'
import path from 'path'

dotenv.config({ path: path.resolve(__dirname, '.env.local') })

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  reporter: 'html',
  use: {
    baseURL: process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:3000',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    ignoreHTTPSErrors: true,
  },
  projects: [
    {
      name: 'setup',
      testMatch: /.*\.setup\.ts/,
    },
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        storageState: 'e2e/.auth/superadmin.json',
      },
      dependencies: ['setup'],
      testIgnore: ['**/contacts/**', '**/capture/**'],
    },
    {
      name: 'chromium:companyadmin',
      use: {
        ...devices['Desktop Chrome'],
        storageState: 'e2e/.auth/companyadmin.json',
      },
      dependencies: ['setup'],
      testMatch: '**/contacts/**',
    },
    // Marketing screenshot capture. Logs in itself (no shared storageState) and is read-only —
    // start the dev server with CAPTURE_READONLY=1 first. See e2e/capture/marketing-shots.spec.ts.
    {
      name: 'capture',
      testMatch: '**/capture/*.spec.ts',
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 1440, height: 900 },
        deviceScaleFactor: 2,
      },
    },
  ],
})
