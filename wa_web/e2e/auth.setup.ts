import { test as setup } from '@playwright/test'
import path from 'path'
import fs from 'fs'

const authFile = path.join(__dirname, '.auth/superadmin.json')

setup('authenticate as superadmin', async ({ page }) => {
  const email = process.env.E2E_SUPERADMIN_EMAIL
  const password = process.env.E2E_SUPERADMIN_PASSWORD

  if (!email || !password) {
    throw new Error(
      'Missing E2E_SUPERADMIN_EMAIL or E2E_SUPERADMIN_PASSWORD in .env.local'
    )
  }

  fs.mkdirSync(path.dirname(authFile), { recursive: true })

  await page.goto('/sign-in')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password').fill(password)
  await page.getByRole('button', { name: 'Sign in' }).click()

  await page.waitForURL('**/dashboard', { timeout: 15_000 })

  await page.context().storageState({ path: authFile })
})
