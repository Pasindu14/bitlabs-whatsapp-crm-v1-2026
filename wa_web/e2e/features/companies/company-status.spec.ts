import { test, expect } from '@playwright/test'
import { CompanyPage } from '../../pages/company.page'

const uniqueSuffix = `s${Date.now().toString(36)}`
const companyName = `E2E Status ${uniqueSuffix}`

test.describe.serial('Company Status Toggle', () => {
  test('setup: create a company (starts Active)', async ({ page }) => {
    const companyPage = new CompanyPage(page)
    await companyPage.goto()
    await companyPage.openCreateDialog()
    await companyPage.fillCompanyForm({ name: companyName })
    await companyPage.submitCreateForm()
    await companyPage.expectDialogClosed()
    await companyPage.expectSuccessToast('Company created successfully')
    await companyPage.expectRowStatus(companyName, 'Active')
  })

  test('should show Deactivate option in the actions menu for an active company', async ({ page }) => {
    const companyPage = new CompanyPage(page)
    await companyPage.goto()
    await companyPage.openRowActions(companyName)
    await expect(page.getByRole('menuitem', { name: 'Deactivate' })).toBeVisible()
    await page.keyboard.press('Escape')
  })

  test('should show the deactivate confirmation dialog', async ({ page }) => {
    const companyPage = new CompanyPage(page)
    await companyPage.goto()
    await companyPage.clickDeactivate(companyName)
    await expect(page.getByRole('heading', { name: 'Deactivate company?' })).toBeVisible()
  })

  test('should cancel deactivation and keep the company Active', async ({ page }) => {
    const companyPage = new CompanyPage(page)
    await companyPage.goto()
    await companyPage.clickDeactivate(companyName)
    await companyPage.cancelAlert()
    await companyPage.expectRowStatus(companyName, 'Active')
  })

  test('should deactivate the company and show Inactive badge', async ({ page }) => {
    const companyPage = new CompanyPage(page)
    await companyPage.goto()
    await companyPage.clickDeactivate(companyName)
    await companyPage.confirmAlertAction('Deactivate')
    await companyPage.expectSuccessToast('Company deactivated successfully')
    await companyPage.expectRowStatus(companyName, 'Inactive')
  })

  test('should show Activate option in the actions menu for an inactive company', async ({ page }) => {
    const companyPage = new CompanyPage(page)
    await companyPage.goto()
    await companyPage.openRowActions(companyName)
    await expect(page.getByRole('menuitem', { name: 'Activate' })).toBeVisible()
    await page.keyboard.press('Escape')
  })

  test('should show the activate confirmation dialog', async ({ page }) => {
    const companyPage = new CompanyPage(page)
    await companyPage.goto()
    await companyPage.clickActivate(companyName)
    await expect(page.getByRole('heading', { name: 'Activate company?' })).toBeVisible()
  })

  test('should activate the company and show Active badge', async ({ page }) => {
    const companyPage = new CompanyPage(page)
    await companyPage.goto()
    await companyPage.clickActivate(companyName)
    await companyPage.confirmAlertAction('Activate')
    await companyPage.expectSuccessToast('Company activated successfully')
    await companyPage.expectRowStatus(companyName, 'Active')
  })
})
